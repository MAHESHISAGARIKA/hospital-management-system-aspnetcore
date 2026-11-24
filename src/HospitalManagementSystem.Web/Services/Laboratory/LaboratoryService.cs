using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Laboratory;

public sealed class LaboratoryException(string message) : Exception(message);

public sealed class LaboratoryService(HospitalDbContext db, ClinicalRecordService clinical, IAuditLogService audit)
{
    public const int PageSize = 25;

    public async Task<LaboratoryTest[]> CatalogAsync(ClaimsPrincipal user, string? search, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Administrator, ct);
        var term = search?.Trim().ToUpperInvariant();
        return await db.LaboratoryTests.AsNoTracking().Where(test => term == null ||
            test.Code.Contains(term) || test.Name.ToUpper().Contains(term)).OrderBy(test => test.Code).ToArrayAsync(ct);
    }

    public async Task SaveTestAsync(ClaimsPrincipal user, Guid? id, LabCatalogInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Administrator, ct);
        Validate(input);
        if (!Enum.IsDefined(input.Kind))
            throw new LaboratoryException("Choose a supported result type.");
        var code = input.Code.Trim().ToUpperInvariant();
        if (code.Length > 30 || await db.LaboratoryTests.AnyAsync(test => test.Code == code && test.Id != id, ct))
            throw new LaboratoryException("Choose a unique test code of up to 30 characters.");

        var test = id.HasValue ? await db.LaboratoryTests.FindAsync([id.Value], ct) : New<LaboratoryTest>(actor.Id);
        if (test is null)
            throw new LaboratoryException("This catalog test no longer exists.");
        if (id.HasValue)
            CheckToken(input.Token, test);
        else
            db.LaboratoryTests.Add(test);

        test.Code = code;
        test.Name = input.Name.Trim();
        test.Specimen = input.Specimen.Trim();
        test.Unit = Clean(input.Unit);
        test.Kind = input.Kind;
        test.Active = input.Active;
        Touch(test, actor.Id);
        await SaveAsync("LaboratoryCatalogSaved", actor.Id, "Test", test.Id, test.Active ? "Active" : "Inactive", ct);
    }

    public async Task<LabRequestPage> RequestPageAsync(ClaimsPrincipal user, Guid encounterId, LabOrderInput? input, CancellationToken ct)
    {
        var (_, encounter) = await EncounterAsync(user, encounterId, ct);
        RequireOpen(encounter);
        var tests = await db.LaboratoryTests.AsNoTracking().Where(test => test.Active).OrderBy(test => test.Name).ToArrayAsync(ct);
        return new(encounterId, $"{encounter.Patient.FirstName} {encounter.Patient.LastName} ? {encounter.Patient.PatientNumber}",
            input ?? new LabOrderInput { RequestKey = Guid.NewGuid() }, tests);
    }

    public async Task<Guid> RequestAsync(ClaimsPrincipal user, Guid encounterId, LabOrderInput input, CancellationToken ct)
    {
        var (actor, encounter) = await EncounterAsync(user, encounterId, ct);
        Validate(input);
        if (input.RequestKey == Guid.Empty || input.TestIds.Count is < 1 or > 20 ||
            input.TestIds.Distinct().Count() != input.TestIds.Count)
            throw new LaboratoryException("Select 1 to 20 different tests and use the current request form.");

        var instructions = Clean(input.Instructions);
        var existing = await FindRequestAsync(encounterId, input.RequestKey, ct);
        if (existing is not null)
            return Repeat(existing, actor, input.TestIds, instructions);

        RequireOpen(encounter);
        var tests = await db.LaboratoryTests.AsNoTracking().Where(test => test.Active && input.TestIds.Contains(test.Id)).ToArrayAsync(ct);
        if (tests.Length != input.TestIds.Count)
            throw new LaboratoryException("Choose only active catalog tests.");

        var order = New<LaboratoryOrder>(actor);
        order.Number = $"LAB-{order.Id:N}";
        order.EncounterId = encounter.Id;
        order.RequestKey = input.RequestKey;
        order.Instructions = instructions;
        order.Items = tests.Select(test => new LaboratoryOrderItem
        {
            Id = Guid.CreateVersion7(), TestId = test.Id, Code = test.Code, Name = test.Name,
            Specimen = test.Specimen, Kind = test.Kind, Unit = test.Unit
        }).ToList();
        db.LaboratoryOrders.Add(order);
        try
        {
            await audit.WriteAsync("LaboratoryRequested", actor, null, $"Order {order.Id:D} Requested", ct);
        }
        catch (DbUpdateException exception) when (DatabaseConflict(exception))
        {
            db.ChangeTracker.Clear();
            existing = await FindRequestAsync(encounterId, input.RequestKey, ct);
            if (existing is not null)
                return Repeat(existing, actor, input.TestIds, instructions);
            throw new LaboratoryException("The visit or catalog changed. Reload the form and try again.");
        }
        return order.Id;
    }

    public async Task<LabQueue> QueueAsync(ClaimsPrincipal user, LabQueueFilter filter, CancellationToken ct)
    {
        var (actor, _, scope) = await OrderScopeAsync(user, ct);
        var counts = await scope.GroupBy(o => o.Status).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, ct);
        var doctors = await db.Doctors.AsNoTracking().Where(d => scope.Any(o => o.Encounter.DoctorId == d.Id))
            .OrderBy(d => d.LastName).ToArrayAsync(ct);
        var term = Clean(filter.Search)?.ToUpperInvariant();
        if (term is not null)
            scope = scope.Where(o => o.Number.Contains(term) || o.Encounter.Patient.PatientNumber.ToUpper().Contains(term));
        if (filter.Status.HasValue)
            scope = scope.Where(o => o.Status == filter.Status);
        if (filter.DoctorId.HasValue)
            scope = scope.Where(o => o.Encounter.DoctorId == filter.DoctorId);
        if (filter.Date is { } day)
        {
            if (day.Year is < 2 or > 9998)
                throw new LaboratoryException("Choose a valid date.");
            var start = AppointmentService.ToUtc(day, TimeOnly.MinValue);
            var end = AppointmentService.ToUtc(day.AddDays(1), TimeOnly.MinValue);
            scope = scope.Where(o => o.CreatedAtUtc >= start && o.CreatedAtUtc < end);
        }
        var total = await scope.CountAsync(ct);
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / PageSize)));
        var orders = await scope.Include(o => o.Encounter).ThenInclude(e => e.Patient)
            .Include(o => o.Encounter).ThenInclude(e => e.Doctor)
            .AsNoTracking().OrderByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id)
            .Skip((filter.Page - 1) * PageSize).Take(PageSize).ToArrayAsync(ct);
        await audit.WriteAsync("LaboratoryQueueViewed", actor, null, "Laboratory queue", ct);
        return new(filter, orders, total, counts, doctors);
    }

    public async Task<LaboratoryOrder[]> EncounterOrdersAsync(ClaimsPrincipal user, Guid[] encounterIds, CancellationToken ct)
    {
        var (_, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        return await DoctorOrders(doctor).AsNoTracking().Where(order => encounterIds.Contains(order.EncounterId))
            .OrderByDescending(order => order.CreatedAtUtc).ToArrayAsync(ct);
    }

    public async Task<LabOrderPage> DetailsAsync(ClaimsPrincipal user, Guid id, bool print, CancellationToken ct)
    {
        var (actor, staff, scope) = await OrderScopeAsync(user, ct);
        var order = await LoadAsync(scope.AsNoTracking(), id, actor, ct);
        if (print && (order.Status != LabOrderStatus.Completed || order.Items.Count == 0 ||
            order.Items.Any(item => item.Results.All(result => result.Status != LabResultStatus.Verified))))
            throw new LaboratoryException("The report is available once every ordered test has a verified result.");
        if (!staff || print)
            foreach (var item in order.Items)
                item.Results.RemoveAll(result => order.Status != LabOrderStatus.Completed || result.Status != LabResultStatus.Verified);

        await audit.WriteAsync(print ? "LaboratoryReportViewed" : "LaboratoryOrderViewed", actor, null, $"Order {id:D}", ct);
        return new(order, staff, user.IsInRole(HospitalRoles.Doctor) && !staff, print, DateTime.UtcNow);
    }

    public async Task CancelAsync(ClaimsPrincipal user, Guid id, LabActionInput input, CancellationToken ct)
    {
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        var order = await LoadAsync(DoctorOrders(doctor), id, actor, ct);
        CheckToken(input.Token, order);
        if (order.Status != LabOrderStatus.Requested || order.Items.Any(item => item.Samples.Count != 0))
            throw new LaboratoryException("Only requests with no collection history can be cancelled.");

        order.CancellationReason = Reason(input.Reason);
        order.Status = LabOrderStatus.Cancelled;
        order.CancelledByUserId = actor;
        order.CancelledAtUtc = DateTime.UtcNow;
        Touch(order, actor);
        await SaveAsync("LaboratoryCancelled", actor, "Order", id, "Requested to Cancelled", ct);
    }

    public async Task CollectAsync(ClaimsPrincipal user, Guid id, LabActionInput input, CancellationToken ct)
    {
        var (actor, order, item) = await WorkAsync(user, id, input.ItemId, input.Token, ct);
        if (!string.Equals(input.PatientNumber?.Trim(), order.Encounter.Patient.PatientNumber, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(input.OrderNumber?.Trim(), order.Number, StringComparison.OrdinalIgnoreCase))
            throw new LaboratoryException("The patient number and order number must match the selected request.");
        if (item.Samples.Any(sample => sample.Status != LabSampleStatus.Rejected))
            throw new LaboratoryException("This test already has a usable sample.");

        var sample = New<LaboratorySample>(actor.Id);
        sample.ItemId = item.Id;
        sample.Accession = $"SPC-{sample.Id:N}";
        db.Add(sample);
        if (!item.Samples.Contains(sample))
            item.Samples.Add(sample);
        await CommitAsync(order, "LaboratorySampleCollected", actor.Id, "Sample", sample.Id, "Collected", ct);
    }

    public async Task ChangeSampleAsync(ClaimsPrincipal user, Guid id, LabActionInput input, LabSampleStatus next, CancellationToken ct)
    {
        var (actor, order, item) = await WorkAsync(user, id, input.ItemId, input.Token, ct);
        var sample = item.Samples.SingleOrDefault(sample => sample.Id == input.SampleId)
            ?? throw await DeniedAsync(actor.Id, "Sample", input.SampleId, ct);
        var previous = sample.Status;
        var now = DateTime.UtcNow;
        switch (next)
        {
            case LabSampleStatus.Received when previous == LabSampleStatus.Collected:
                (sample.ReceivedAtUtc, sample.ReceivedByUserId) = (now, actor.Id);
                break;
            case LabSampleStatus.Processing when previous == LabSampleStatus.Received:
                (sample.ProcessingAtUtc, sample.ProcessingByUserId) = (now, actor.Id);
                break;
            case LabSampleStatus.Rejected when previous != LabSampleStatus.Rejected && item.Results.All(r => r.SampleId != sample.Id):
                sample.RejectionReason = Reason(input.Reason);
                (sample.RejectedAtUtc, sample.RejectedByUserId) = (now, actor.Id);
                break;
            default:
                throw new LaboratoryException("This sample cannot make that transition.");
        }
        sample.Status = next;
        Touch(sample, actor.Id);
        var action = next switch
        {
            LabSampleStatus.Received => "LaboratorySampleReceived",
            LabSampleStatus.Processing => "LaboratorySampleProcessing",
            _ => "LaboratorySampleRejected"
        };
        await CommitAsync(order, action, actor.Id, "Sample", sample.Id, $"{previous} to {next}", ct);
    }

    public async Task SaveResultAsync(ClaimsPrincipal user, Guid id, LabResultInput input, bool amend, CancellationToken ct)
    {
        var (actor, order, item) = await WorkAsync(user, id, input.ItemId, input.Token, ct);
        Validate(input);
        var sample = item.Samples.SingleOrDefault(s => s.Id == input.SampleId && s.Status == LabSampleStatus.Processing)
            ?? throw await DeniedAsync(actor.Id, "Sample", input.SampleId, ct);
        var previous = item.Results.SingleOrDefault(r => r.Id == input.ResultId);
        if (input.ResultId != Guid.Empty && previous is null)
            throw await DeniedAsync(actor.Id, "Result", input.ResultId, ct);

        if (amend && (previous?.Status != LabResultStatus.Verified || item.Results.Any(r => r.PreviousId == previous.Id)) ||
            !amend && (previous is null ? item.Results.Count != 0 : previous.Status != LabResultStatus.Draft) ||
            previous is not null && previous.SampleId != sample.Id)
            throw new LaboratoryException("Edit only a draft or create a correction of the latest verified result.");

        var value = input.Value;
        var qualitative = Clean(input.QualitativeValue);
        if (item.Kind == LabResultKind.Quantitative
            ? !value.HasValue || qualitative is not null || value is <= -1000000000000m or >= 1000000000000m ||
                decimal.Round(value.Value, 6) != value.Value
            : value.HasValue || qualitative is null)
            throw new LaboratoryException("Enter the matching result type. Numbers support up to 12 whole and 6 fractional digits.");

        var result = amend || previous is null ? New<LaboratoryResult>(actor.Id) : previous;
        if (result != previous)
        {
            result.ItemId = item.Id;
            result.SampleId = sample.Id;
            result.PreviousId = amend ? previous!.Id : null;
            result.AmendmentReason = amend ? Reason(input.AmendmentReason) : null;
            db.Add(result);
            if (!item.Results.Contains(result))
                item.Results.Add(result);
        }
        result.Value = value;
        result.QualitativeValue = qualitative;
        result.Unit = item.Unit;
        result.Note = Clean(input.Note);
        Touch(result, actor.Id);
        await CommitAsync(order, amend ? "LaboratoryAmendmentDrafted" : "LaboratoryResultDraftSaved",
            actor.Id, "Result", result.Id, "Draft", ct);
    }

    public async Task ChangeResultAsync(ClaimsPrincipal user, Guid id, LabActionInput input, LabResultStatus next, CancellationToken ct)
    {
        var (actor, order, item) = await WorkAsync(user, id, input.ItemId, input.Token, ct);
        var result = item.Results.SingleOrDefault(r => r.Id == input.ResultId)
            ?? throw await DeniedAsync(actor.Id, "Result", input.ResultId, ct);
        var previous = result.Status;
        var now = DateTime.UtcNow;
        switch (next)
        {
            case LabResultStatus.Verified when previous == LabResultStatus.Submitted:
                (result.VerifiedAtUtc, result.VerifiedByUserId, result.VerifierName) = (now, actor.Id, actor.UserName ?? actor.Id.ToString("D"));
                break;
            case LabResultStatus.Submitted when previous == LabResultStatus.Draft:
                (result.SubmittedAtUtc, result.SubmittedByUserId) = (now, actor.Id);
                break;
            case LabResultStatus.Draft when previous == LabResultStatus.Submitted:
                (result.SubmittedAtUtc, result.SubmittedByUserId) = (null, null);
                break;
            default:
                throw new LaboratoryException("This result is not ready for that transition.");
        }
        result.Status = next;
        Touch(result, actor.Id);
        var action = next switch
        {
            LabResultStatus.Verified when CalculateStatus(order) == LabOrderStatus.Completed => "LaboratoryResultVerifiedAndReportReleased",
            LabResultStatus.Verified => "LaboratoryResultVerified",
            LabResultStatus.Submitted => "LaboratoryResultSubmitted",
            _ => "LaboratoryResultReturned"
        };
        await CommitAsync(order, action, actor.Id, $"Order {order.Id:D} Result", result.Id, $"{previous} to {next}", ct);
    }

    private async Task<(ApplicationUser Actor, LaboratoryOrder Order, LaboratoryOrderItem Item)> WorkAsync(
        ClaimsPrincipal user, Guid id, Guid itemId, Guid token, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.LaboratoryStaff, ct);
        var order = await LoadAsync(db.LaboratoryOrders, id, actor.Id, ct);
        CheckToken(token, order);
        if (order.Status == LabOrderStatus.Cancelled)
            throw new LaboratoryException("This order is cancelled.");
        var item = order.Items.SingleOrDefault(item => item.Id == itemId)
            ?? throw await DeniedAsync(actor.Id, "Item", itemId, ct);
        return (actor, order, item);
    }

    private async Task<(Guid Actor, bool Staff, IQueryable<LaboratoryOrder> Scope)> OrderScopeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsInRole(HospitalRoles.LaboratoryStaff))
            return ((await ActorAsync(user, HospitalRoles.LaboratoryStaff, ct)).Id, true, db.LaboratoryOrders);
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        return (actor, false, DoctorOrders(doctor));
    }

    private IQueryable<LaboratoryOrder> DoctorOrders(Guid doctor)
        => db.LaboratoryOrders.Where(order => clinical.Permitted(doctor).Any(encounter => encounter.Id == order.EncounterId));

    private async Task<(Guid Actor, ClinicalEncounter Encounter)> EncounterAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        var encounter = await clinical.Permitted(doctor).AsNoTracking().Include(e => e.Patient).SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Encounter", id, ct);
        return (actor, encounter);
    }

    private async Task<ApplicationUser> ActorAsync(ClaimsPrincipal user, string role, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole(role) ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new ClinicalAccessException();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsEnabled, ct)
            ?? throw new ClinicalAccessException();
    }

    private async Task<LaboratoryOrder> LoadAsync(IQueryable<LaboratoryOrder> scope, Guid id, Guid actor, CancellationToken ct)
        => await scope.Include(o => o.Encounter).ThenInclude(e => e.Patient).Include(o => o.Encounter).ThenInclude(e => e.Doctor)
            .Include(o => o.Items).ThenInclude(i => i.Samples).Include(o => o.Items).ThenInclude(i => i.Results)
            .AsSplitQuery().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw await DeniedAsync(actor, "Order", id, ct);

    private Task<LaboratoryOrder?> FindRequestAsync(Guid encounter, Guid key, CancellationToken ct)
        => db.LaboratoryOrders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.EncounterId == encounter && o.RequestKey == key, ct);

    private static Guid Repeat(LaboratoryOrder order, Guid actor, List<Guid> tests, string? instructions)
        => order.CreatedByUserId == actor && order.Instructions == instructions &&
            order.Items.Select(i => i.TestId).ToHashSet().SetEquals(tests) ? order.Id
            : throw new LaboratoryException("This request key is already in use. Open a fresh request form.");

    private static void RequireOpen(ClinicalEncounter encounter)
    {
        if (encounter.FinalizedAtUtc.HasValue)
            throw new LaboratoryException("New laboratory requests require an open encounter.");
    }

    private async Task<ClinicalAccessException> DeniedAsync(Guid actor, string type, Guid id, CancellationToken ct)
    {
        await audit.WriteAsync("LaboratoryAccessDenied", actor, null, $"{type} {id:D}", ct);
        return new ClinicalAccessException();
    }

    private Task CommitAsync(LaboratoryOrder order, string action, Guid actor, string entity, Guid id, string status, CancellationToken ct)
    {
        order.Status = CalculateStatus(order);
        Touch(order, actor);
        return SaveAsync(action, actor, entity, id, status, ct);
    }

    private static LabOrderStatus CalculateStatus(LaboratoryOrder order)
    {
        if (order.Items.All(i => i.Results.Any(r => r.Status == LabResultStatus.Verified)))
            return LabOrderStatus.Completed;
        if (order.Items.Any(i => i.Results.Count != 0))
            return LabOrderStatus.ResultsEntered;
        if (order.Items.Any(i => i.Samples.Any(s => s.Status == LabSampleStatus.Processing)))
            return LabOrderStatus.InProgress;
        if (order.Items.Any(i => i.Samples.Any(s => s.Status != LabSampleStatus.Rejected)))
            return LabOrderStatus.SampleCollected;
        return LabOrderStatus.Requested;
    }

    private async Task SaveAsync(string action, Guid actor, string entity, Guid id, string status, CancellationToken ct)
    {
        try
        {
            // Audit writes save the shared DbContext, including the business changes.
            await audit.WriteAsync(action, actor, null, $"{entity} {id:D} {status}", ct);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || DatabaseConflict(exception))
        {
            db.ChangeTracker.Clear();
            throw new LaboratoryException("The record changed or conflicts with an existing record. Reload and try again.");
        }
    }

    private static bool DatabaseConflict(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" or "23503" or "23514" or "40P01" };

    private static T New<T>(Guid actor) where T : ILaboratoryTracked, new()
    {
        var entity = new T { Id = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
        Touch(entity, actor);
        return entity;
    }

    private static void Touch(ILaboratoryTracked entity, Guid actor)
        => (entity.UpdatedAtUtc, entity.UpdatedByUserId, entity.ConcurrencyToken) = (DateTime.UtcNow, actor, Guid.CreateVersion7());

    private static void CheckToken(Guid token, ILaboratoryTracked entity)
    {
        if (token == Guid.Empty || token != entity.ConcurrencyToken)
            throw new LaboratoryException("This record has changed. Reload it before trying again.");
    }

    private static void Validate(object input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), null, true))
            throw new LaboratoryException("Check the required fields and field lengths.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Reason(string? value) => Clean(value) is { Length: <= 500 } reason
        ? reason : throw new LaboratoryException("Enter a reason of up to 500 characters.");
}
