using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

internal static class ClinicalModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<ClinicalEncounter>(entity =>
        {
            entity.ToTable("clinical_encounters", table => table.HasCheckConstraint("ck_encounter_finalized",
                "(\"FinalizedAtUtc\" IS NULL) = (\"FinalizedByUserId\" IS NULL)"));
            entity.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(item => item.AppointmentId).IsUnique();
            entity.HasIndex(item => new { item.DoctorId, item.PatientId, item.CreatedAtUtc });
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Appointment).WithMany()
                .HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ClinicalRecord>(entity =>
        {
            entity.ToTable("clinical_records", table =>
            {
                table.HasCheckConstraint("ck_clinical_record_kind", "\"Kind\" IN ('Diagnosis','Prescription','Treatment')");
                table.HasCheckConstraint("ck_clinical_record_status", "\"Status\" IN ('Draft','Finalized','Voided')");
                table.HasCheckConstraint("ck_clinical_record_finalized",
                    "(\"Status\" = 'Draft' AND \"FinalizedAtUtc\" IS NULL AND \"FinalizedByUserId\" IS NULL) OR " +
                    "(\"Status\" <> 'Draft' AND \"FinalizedAtUtc\" IS NOT NULL AND \"FinalizedByUserId\" IS NOT NULL)");
                table.HasCheckConstraint("ck_clinical_record_revision",
                    "(\"PreviousRevisionId\" IS NULL AND \"AmendmentReason\" IS NULL AND \"Status\" <> 'Voided') OR " +
                    "(\"PreviousRevisionId\" IS NOT NULL AND length(trim(\"AmendmentReason\")) > 0 AND \"AmendmentReason\" IS NOT NULL)");
            });
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.Content).HasMaxLength(4000);
            entity.Property(item => item.Code).HasMaxLength(50);
            entity.Property(item => item.Notes).HasMaxLength(2000);
            entity.Property(item => item.AmendmentReason).HasMaxLength(500);
            entity.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(item => item.PreviousRevisionId).IsUnique();
            entity.HasIndex(item => new { item.EncounterId, item.RecordedDate });
            entity.HasOne(item => item.Encounter).WithMany(item => item.Records)
                .HasForeignKey(item => item.EncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.PreviousRevision).WithMany()
                .HasForeignKey(item => new { item.PreviousRevisionId, item.EncounterId, item.Kind })
                .HasPrincipalKey(item => new { item.Id, item.EncounterId, item.Kind }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PrescriptionItem>(entity =>
        {
            entity.ToTable("clinical_prescription_items", table =>
                table.HasCheckConstraint("ck_prescription_item_quantity", "\"Quantity\" IS NULL OR \"Quantity\" > 0"));
            entity.HasOne<ClinicalRecord>().WithMany(item => item.Items)
                .HasForeignKey(item => item.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.PrescriptionId, item.Position }).IsUnique();
            entity.Property(item => item.Medicine).HasMaxLength(150);
            entity.Property(item => item.Strength).HasMaxLength(60);
            entity.Property(item => item.Dose).HasMaxLength(60);
            entity.Property(item => item.DoseUnit).HasMaxLength(40);
            entity.Property(item => item.Route).HasMaxLength(80);
            entity.Property(item => item.Frequency).HasMaxLength(120);
            entity.Property(item => item.Duration).HasMaxLength(80);
            entity.Property(item => item.Instructions).HasMaxLength(500);
        });
    }
}
