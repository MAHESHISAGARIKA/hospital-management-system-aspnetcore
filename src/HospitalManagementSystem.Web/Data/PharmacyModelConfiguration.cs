using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

internal static class PharmacyModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var medicine = model.Entity<Medicine>();
        medicine.ToTable("medicines", t => t.HasCheckConstraint("ck_medicine_threshold", "\"ReorderThreshold\" >= 0 AND (\"Unit\" IN ('Millilitre','Gram') OR trunc(\"ReorderThreshold\") = \"ReorderThreshold\")"));
        medicine.HasIndex(x => x.Code).IsUnique();
        medicine.HasIndex(x => x.GenericName);
        medicine.Property(x => x.Code).HasMaxLength(30);
        medicine.Property(x => x.GenericName).HasMaxLength(150);
        medicine.Property(x => x.BrandName).HasMaxLength(150);
        medicine.Property(x => x.Strength).HasMaxLength(80);
        medicine.Property(x => x.Form).HasMaxLength(80);
        medicine.Property(x => x.Manufacturer).HasMaxLength(150);
        medicine.Property(x => x.Description).HasMaxLength(500);
        medicine.Property(x => x.Unit).HasConversion<string>().HasMaxLength(20);
        medicine.Property(x => x.ReorderThreshold).HasPrecision(18, 3);
        medicine.Property(x => x.ConcurrencyToken).IsConcurrencyToken();

        var batch = model.Entity<MedicineBatch>();
        batch.ToTable("medicine_batches", t =>
        {
            t.HasCheckConstraint("ck_batch_balance", "\"Available\" >= 0 AND \"ReceivedQuantity\" > 0 AND (\"Unit\" IN ('Millilitre','Gram') OR (trunc(\"Available\") = \"Available\" AND trunc(\"ReceivedQuantity\") = \"ReceivedQuantity\"))");
            t.HasCheckConstraint("ck_batch_status", "\"Status\" IN ('Available','Quarantined','Recalled')");
            t.HasCheckConstraint("ck_batch_dates", "\"Expiry\" >= \"ReceivedDate\"");
        });
        batch.HasIndex(x => new { x.MedicineId, x.Number }).IsUnique();
        batch.HasIndex(x => new { x.Status, x.Expiry });
        batch.HasOne(x => x.Medicine).WithMany(x => x.Batches).HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        batch.Property(x => x.Number).HasMaxLength(60);
        batch.Property(x => x.Supplier).HasMaxLength(150);
        batch.Property(x => x.Notes).HasMaxLength(500);
        batch.Property(x => x.Unit).HasConversion<string>().HasMaxLength(20);
        batch.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        batch.Property(x => x.Available).HasPrecision(18, 3);
        batch.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
        batch.Property(x => x.ConcurrencyToken).IsConcurrencyToken();

        model.Entity<PrescriptionItem>().Property(x => x.DispenseUnit).HasConversion<string>().HasMaxLength(20);
        model.Entity<PrescriptionItem>().HasAlternateKey(x => new { x.Id, x.PrescriptionId });

        var dispense = model.Entity<PrescriptionDispense>();
        dispense.ToTable("prescription_dispenses");
        dispense.HasAlternateKey(x => new { x.Id, x.PrescriptionId });
        dispense.HasIndex(x => x.Number).IsUnique();
        dispense.HasIndex(x => new { x.ActorId, x.RequestKey }).IsUnique();
        dispense.HasIndex(x => new { x.PrescriptionId, x.OccurredAtUtc });
        dispense.Property(x => x.Number).HasMaxLength(36);
        dispense.HasOne(x => x.Prescription).WithMany().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);

        var line = model.Entity<PrescriptionDispenseItem>();
        line.ToTable("prescription_dispense_items", t => t.HasCheckConstraint("ck_dispense_positive", "\"Quantity\" > 0"));
        line.HasIndex(x => new { x.DispenseId, x.PrescriptionItemId, x.BatchId }).IsUnique();
        dispense.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.DispenseId, x.PrescriptionId })
            .HasPrincipalKey(x => new { x.Id, x.PrescriptionId }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne(x => x.PrescriptionItem).WithMany().HasForeignKey(x => new { x.PrescriptionItemId, x.PrescriptionId })
            .HasPrincipalKey(x => new { x.Id, x.PrescriptionId }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        line.Property(x => x.Quantity).HasPrecision(18, 3);
        line.Property(x => x.FefoOverrideReason).HasMaxLength(500);

        var movement = model.Entity<PharmacyStockMovement>();
        movement.ToTable("pharmacy_stock_movements", t => t.HasCheckConstraint("ck_stock_movement_quantity",
            "(\"Kind\" IN ('Quarantined','Recalled') AND \"Quantity\" = 0) OR (\"Kind\" NOT IN ('Quarantined','Recalled') AND \"Quantity\" > 0)"));
        movement.HasIndex(x => new { x.BatchId, x.OccurredAtUtc });
        movement.HasIndex(x => new { x.ActorId, x.RequestKey }).IsUnique();
        movement.HasIndex(x => x.DispenseItemId).IsUnique();
        movement.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        movement.HasOne<PrescriptionDispenseItem>().WithMany().HasForeignKey(x => x.DispenseItemId).OnDelete(DeleteBehavior.Restrict);
        movement.Property(x => x.Unit).HasConversion<string>().HasMaxLength(20);
        movement.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        movement.Property(x => x.Quantity).HasPrecision(18, 3);
        movement.Property(x => x.Reason).HasMaxLength(500);
    }
}
