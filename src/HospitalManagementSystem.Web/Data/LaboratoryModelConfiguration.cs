using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HospitalManagementSystem.Web.Data;

internal static class LaboratoryModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var tests = model.Entity<LaboratoryTest>();
        Track(tests);
        tests.ToTable("laboratory_tests", table =>
            table.HasCheckConstraint("ck_lab_test_kind", "\"Kind\" IN ('Quantitative','Qualitative')"));
        tests.HasIndex(t => t.Code).IsUnique();
        tests.Property(t => t.Code).HasMaxLength(30);
        tests.Property(t => t.Name).HasMaxLength(150);
        tests.Property(t => t.Specimen).HasMaxLength(80);
        tests.Property(t => t.Unit).HasMaxLength(40);
        tests.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);

        var orders = model.Entity<LaboratoryOrder>();
        Track(orders);
        orders.ToTable("laboratory_orders", table =>
        {
            table.HasCheckConstraint("ck_lab_order_status", "\"Status\" IN ('Requested','SampleCollected','InProgress','ResultsEntered','Completed','Cancelled')");
            table.HasCheckConstraint("ck_lab_order_cancel", "(\"Status\" = 'Cancelled') = (\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
        });
        orders.HasOne(o => o.Encounter).WithMany().HasForeignKey(o => o.EncounterId).OnDelete(DeleteBehavior.Restrict);
        orders.HasIndex(o => o.Number).IsUnique();
        orders.HasIndex(o => new { o.EncounterId, o.RequestKey }).IsUnique();
        orders.HasIndex(o => new { o.Status, o.CreatedAtUtc });
        orders.Property(o => o.Number).HasMaxLength(36);
        orders.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        orders.Property(o => o.Instructions).HasMaxLength(1000);
        orders.Property(o => o.CancellationReason).HasMaxLength(500);

        var items = model.Entity<LaboratoryOrderItem>();
        items.ToTable("laboratory_order_items");
        items.HasIndex(i => new { i.OrderId, i.TestId }).IsUnique();
        items.HasOne<LaboratoryTest>().WithMany().HasForeignKey(i => i.TestId).OnDelete(DeleteBehavior.Restrict);
        orders.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        items.Property(i => i.Code).HasMaxLength(30);
        items.Property(i => i.Name).HasMaxLength(150);
        items.Property(i => i.Specimen).HasMaxLength(80);
        items.Property(i => i.Unit).HasMaxLength(40);
        items.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);

        var samples = model.Entity<LaboratorySample>();
        Track(samples);
        samples.ToTable("laboratory_samples", table =>
        {
            table.HasCheckConstraint("ck_lab_sample_status", "\"Status\" IN ('Collected','Received','Processing','Rejected')");
            table.HasCheckConstraint("ck_lab_sample_rejected", "(\"Status\" = 'Rejected') = (\"RejectedAtUtc\" IS NOT NULL AND \"RejectedByUserId\" IS NOT NULL AND \"RejectionReason\" IS NOT NULL)");
        });
        samples.HasIndex(s => s.Accession).IsUnique();
        samples.HasIndex(s => s.ItemId).IsUnique().HasFilter("\"Status\" <> 'Rejected'");
        samples.HasAlternateKey(s => new { s.Id, s.ItemId });
        items.HasMany(i => i.Samples).WithOne().HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.Restrict);
        samples.Property(s => s.Accession).HasMaxLength(36);
        samples.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        samples.Property(s => s.RejectionReason).HasMaxLength(500);

        var results = model.Entity<LaboratoryResult>();
        Track(results);
        results.ToTable("laboratory_results", table =>
        {
            table.HasCheckConstraint("ck_lab_result_value", "(\"Value\" IS NOT NULL) <> (\"QualitativeValue\" IS NOT NULL)");
            table.HasCheckConstraint("ck_lab_result_status", "\"Status\" IN ('Draft','Submitted','Verified')");
            table.HasCheckConstraint("ck_lab_result_verified", "(\"Status\" = 'Verified') = (\"VerifiedAtUtc\" IS NOT NULL AND \"VerifiedByUserId\" IS NOT NULL AND \"VerifierName\" IS NOT NULL)");
            table.HasCheckConstraint("ck_lab_result_submitted", "(\"Status\" <> 'Draft') = (\"SubmittedAtUtc\" IS NOT NULL AND \"SubmittedByUserId\" IS NOT NULL)");
            table.HasCheckConstraint("ck_lab_result_amendment", "(\"PreviousId\" IS NULL) = (\"AmendmentReason\" IS NULL)");
        });
        results.HasIndex(x => x.PreviousId).IsUnique();
        results.HasIndex(x => x.ItemId).IsUnique().HasFilter("\"PreviousId\" IS NULL");
        results.HasOne<LaboratoryResult>().WithMany()
            .HasForeignKey(x => new { x.PreviousId, x.ItemId })
            .HasPrincipalKey(x => new { x.Id, x.ItemId }).OnDelete(DeleteBehavior.Restrict);
        results.HasOne(x => x.Sample).WithMany().HasForeignKey(x => new { x.SampleId, x.ItemId })
            .HasPrincipalKey(x => new { x.Id, x.ItemId }).OnDelete(DeleteBehavior.Restrict);
        items.HasMany(i => i.Results).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        results.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        results.Property(x => x.Value).HasPrecision(18, 6);
        results.Property(x => x.QualitativeValue).HasMaxLength(250);
        results.Property(x => x.Unit).HasMaxLength(40);
        results.Property(x => x.Note).HasMaxLength(1000);
        results.Property(x => x.AmendmentReason).HasMaxLength(500);
        results.Property(x => x.VerifierName).HasMaxLength(256);
    }

    private static void Track<T>(EntityTypeBuilder<T> entity) where T : class, ILaboratoryTracked
        => entity.Property(nameof(ILaboratoryTracked.ConcurrencyToken)).IsConcurrencyToken();
}
