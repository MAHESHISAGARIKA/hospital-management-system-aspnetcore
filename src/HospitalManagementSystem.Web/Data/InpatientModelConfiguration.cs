using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;
internal static class InpatientModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var ward = model.Entity<HospitalWard>();
        ward.ToTable("hospital_wards");
        ward.HasIndex(x => x.Code).IsUnique();
        ward.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        ward.Property(x => x.Code).HasMaxLength(30);
        ward.Property(x => x.Name).HasMaxLength(120);
        ward.Property(x => x.Location).HasMaxLength(120);
        ward.Property(x => x.Token).IsConcurrencyToken();

        var bed = model.Entity<HospitalBed>();
        bed.ToTable("hospital_beds", t => t.HasCheckConstraint("ck_bed_condition", "\"Condition\" IN ('Ready','Cleaning','Maintenance','Unavailable')"));
        bed.HasIndex(x => new { x.WardId, x.Label }).IsUnique();
        bed.HasOne(x => x.Ward).WithMany(x => x.Beds).HasForeignKey(x => x.WardId).OnDelete(DeleteBehavior.Restrict);
        bed.Property(x => x.Label).HasMaxLength(40);
        bed.Property(x => x.Condition).HasConversion<string>().HasMaxLength(20);
        bed.Property(x => x.Token).IsConcurrencyToken();

        var admission = model.Entity<InpatientAdmission>();
        admission.ToTable("inpatient_admissions", t =>
        {
            t.HasCheckConstraint("ck_admission_status", "\"Status\" IN ('Pending','Approved','Admitted','Discharged','Cancelled')");
            t.HasCheckConstraint("ck_admission_approval", "(\"ApprovedAtUtc\" IS NULL) = (\"ApprovedByUserId\" IS NULL) AND (\"Status\" NOT IN ('Approved','Admitted','Discharged') OR \"ApprovedAtUtc\" IS NOT NULL)");
            t.HasCheckConstraint("ck_admission_start", "(\"Status\" IN ('Admitted','Discharged')) = (\"AdmittedAtUtc\" IS NOT NULL AND \"AdmittedByUserId\" IS NOT NULL)");
            t.HasCheckConstraint("ck_admission_cancel", "(\"Status\" = 'Cancelled') = (\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
        });
        admission.HasIndex(x => x.Number).IsUnique();
        admission.HasIndex(x => new { x.CreatedByUserId, x.RequestKey }).IsUnique();
        admission.HasIndex(x => x.PatientId, "ux_admission_patient_open").IsUnique().HasFilter("\"Status\" IN ('Pending','Approved','Admitted')");
        admission.HasIndex(x => x.OriginEncounterId).IsUnique().HasFilter("\"OriginEncounterId\" IS NOT NULL AND \"Status\" <> 'Cancelled'");
        admission.HasIndex(x => new { x.Status, x.AdmittedAtUtc });
        admission.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        admission.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        admission.HasOne(x => x.OriginEncounter).WithMany().HasForeignKey(x => x.OriginEncounterId).OnDelete(DeleteBehavior.Restrict);
        admission.Property(x => x.Number).HasMaxLength(36);
        admission.Property(x => x.Reason).HasMaxLength(500);
        admission.Property(x => x.CancellationReason).HasMaxLength(500);
        admission.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        admission.Property(x => x.Token).IsConcurrencyToken();

        var allocation = model.Entity<BedAllocation>();
        allocation.ToTable("bed_allocations", t => t.HasCheckConstraint("ck_bed_allocation_interval",
            "(\"EndedAtUtc\" IS NULL) = (\"EndedByUserId\" IS NULL) AND (\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\")"));
        allocation.HasIndex(x => x.AdmissionId, "ux_allocation_current_admission").IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        allocation.HasIndex(x => x.BedId, "ux_allocation_current_bed").IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        allocation.HasIndex(x => new { x.BedId, x.StartedAtUtc });
        allocation.HasOne(x => x.Bed).WithMany(x => x.Allocations).HasForeignKey(x => x.BedId).OnDelete(DeleteBehavior.Restrict);
        allocation.Property(x => x.TransferReason).HasMaxLength(500);
        admission.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);

        var assignment = model.Entity<InpatientCareAssignment>();
        assignment.ToTable("inpatient_care_assignments");
        assignment.HasIndex(x => new { x.AdmissionId, x.NurseUserId }).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        assignment.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.NurseUserId).OnDelete(DeleteBehavior.Restrict);
        admission.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);

        var observation = model.Entity<InpatientObservation>();
        observation.ToTable("inpatient_observations");
        observation.HasIndex(x => x.CorrectsId).IsUnique();
        observation.HasOne<InpatientObservation>().WithMany().HasForeignKey(x => x.CorrectsId).OnDelete(DeleteBehavior.Restrict);
        observation.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
        observation.Property(x => x.Value).HasPrecision(10, 2);
        observation.Property(x => x.CorrectionReason).HasMaxLength(500);
        admission.HasMany(x => x.Observations).WithOne().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);

        var discharge = model.Entity<InpatientDischarge>();
        discharge.ToTable("inpatient_discharges");
        admission.HasOne(x => x.Discharge).WithOne().HasForeignKey<InpatientDischarge>(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
        discharge.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(20);
        discharge.Property(x => x.Diagnosis).HasMaxLength(500);
        discharge.Property(x => x.Summary).HasMaxLength(4000);

        model.Entity<PatientInvoiceItem>().HasOne<InpatientAdmission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PatientInvoiceItem>().HasIndex(x => x.AdmissionId, "ux_billing_admission_claim").IsUnique()
            .HasFilter("\"Claimed\" AND \"AdmissionId\" IS NOT NULL");
    }
}
