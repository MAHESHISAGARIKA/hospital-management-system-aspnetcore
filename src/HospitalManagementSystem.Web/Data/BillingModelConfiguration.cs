using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

internal static class BillingModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var service = model.Entity<BillableService>();
        service.ToTable("billing_services");
        service.HasIndex(s => s.Code).IsUnique();
        service.Property(s => s.Code).HasMaxLength(40);
        service.Property(s => s.Name).HasMaxLength(120);
        service.Property(s => s.SourceCode).HasMaxLength(40);
        service.Property(s => s.Category).HasConversion<string>().HasMaxLength(20);
        service.Property(s => s.Token).IsConcurrencyToken();

        var price = model.Entity<BillingPrice>();
        price.ToTable("billing_prices", t => t.HasCheckConstraint("ck_billing_price", "\"Amount\" >= 0 AND (\"Until\" IS NULL OR \"Until\" > \"From\")"));
        service.HasMany(s => s.Prices).WithOne().HasForeignKey(p => p.ServiceId).OnDelete(DeleteBehavior.Restrict);
        price.HasIndex(p => new { p.ServiceId, p.From }).IsUnique();
        price.Property(p => p.Amount).HasPrecision(18, 2);

        var invoice = model.Entity<PatientInvoice>();
        invoice.ToTable("patient_invoices", t =>
        {
            t.HasCheckConstraint("ck_invoice_status", "\"Status\" IN ('Draft','Issued','Voided')");
            t.HasCheckConstraint("ck_invoice_total", "\"Total\" >= 0");
            t.HasCheckConstraint("ck_invoice_issue", "(\"Status\" <> 'Draft') = (\"Number\" IS NOT NULL AND \"IssuedAtUtc\" IS NOT NULL AND \"IssuedByUserId\" IS NOT NULL AND \"PatientLabel\" IS NOT NULL AND \"PatientReference\" IS NOT NULL)");
            t.HasCheckConstraint("ck_invoice_void", "(\"Status\" = 'Voided') = (\"VoidedAtUtc\" IS NOT NULL AND \"VoidedByUserId\" IS NOT NULL AND \"VoidReason\" IS NOT NULL)");
        });
        invoice.HasIndex(i => i.Number).IsUnique();
        invoice.HasIndex(i => i.ReplacesId).IsUnique().HasFilter("\"Status\" <> 'Voided'");
        invoice.HasIndex(i => new { i.PatientId, i.CreatedAtUtc });
        invoice.HasIndex(i => new { i.Status, i.IssuedAtUtc });
        invoice.HasOne(i => i.Patient).WithMany().HasForeignKey(i => i.PatientId).OnDelete(DeleteBehavior.Restrict);
        invoice.HasOne<PatientInvoice>().WithMany().HasForeignKey(i => i.ReplacesId).OnDelete(DeleteBehavior.Restrict);
        invoice.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        invoice.Property(i => i.Number).HasMaxLength(36);
        invoice.Property(i => i.PatientLabel).HasMaxLength(201);
        invoice.Property(i => i.PatientReference).HasMaxLength(30);
        invoice.Property(i => i.VoidReason).HasMaxLength(500);
        invoice.Property(i => i.Total).HasPrecision(18, 2);
        invoice.Property(i => i.Token).IsConcurrencyToken();

        var line = model.Entity<PatientInvoiceItem>();
        line.ToTable("patient_invoice_items", t =>
        {
            t.HasCheckConstraint("ck_invoice_item_amount", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Total\" = round(\"Quantity\" * \"UnitPrice\", 2)");
            t.HasCheckConstraint("ck_invoice_item_source", "(\"Category\" = 'Consultation' AND \"AppointmentId\" IS NOT NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Laboratory' AND \"LabItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Pharmacy' AND \"DispenseItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL) OR (\"Category\" = 'Other' AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL)");
        });
        invoice.HasMany(i => i.Items).WithOne().HasForeignKey(i => i.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        line.HasOne(i => i.Price).WithMany().HasForeignKey(i => i.PriceId).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<Appointment>().WithMany().HasForeignKey(i => i.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<LaboratoryOrderItem>().WithMany().HasForeignKey(i => i.LabItemId).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<PrescriptionDispenseItem>().WithMany().HasForeignKey(i => i.DispenseItemId).OnDelete(DeleteBehavior.Restrict);
        line.HasIndex(i => i.AppointmentId, "ux_billing_appointment_claim").IsUnique().HasFilter("\"Claimed\" AND \"AppointmentId\" IS NOT NULL");
        line.HasIndex(i => i.LabItemId, "ux_billing_lab_claim").IsUnique().HasFilter("\"Claimed\" AND \"LabItemId\" IS NOT NULL");
        line.HasIndex(i => i.DispenseItemId, "ux_billing_dispense_claim").IsUnique().HasFilter("\"Claimed\" AND \"DispenseItemId\" IS NOT NULL");
        line.Property(i => i.Category).HasConversion<string>().HasMaxLength(20);
        line.Property(i => i.Code).HasMaxLength(40);
        line.Property(i => i.Name).HasMaxLength(120);
        line.Property(i => i.UnitPrice).HasPrecision(18, 2);
        line.Property(i => i.Total).HasPrecision(18, 2);
        line.Property(i => i.Quantity).HasPrecision(18, 3);

        var payment = model.Entity<PatientPayment>();
        payment.ToTable("patient_payments", t =>
        {
            t.HasCheckConstraint("ck_payment_amount", "\"Amount\" > 0 AND \"BalanceAfter\" >= 0");
            t.HasCheckConstraint("ck_payment_method", "(\"Method\" = 'Cash' AND \"BankReference\" IS NULL) OR (\"Method\" = 'BankTransfer' AND length(trim(\"BankReference\")) > 0 AND \"BankReference\" IS NOT NULL)");
        });
        payment.HasIndex(p => p.ReceiptNumber).IsUnique();
        payment.HasIndex(p => p.BankReference).IsUnique();
        payment.HasIndex(p => new { p.ActorId, p.RequestKey }).IsUnique();
        payment.HasIndex(p => p.RecordedAtUtc);
        payment.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        payment.Property(p => p.Amount).HasPrecision(18, 2);
        payment.Property(p => p.BalanceAfter).HasPrecision(18, 2);
        payment.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        payment.Property(p => p.BankReference).HasMaxLength(80);
        payment.Property(p => p.ReceiptNumber).HasMaxLength(36);
        payment.Property(p => p.RecordedBy).HasMaxLength(256);

        var reversal = model.Entity<PaymentReversal>();
        reversal.ToTable("payment_reversals", t => t.HasCheckConstraint("ck_payment_reversal_reason", "length(trim(\"Reason\")) > 0"));
        payment.HasOne(p => p.Reversal).WithOne().HasForeignKey<PaymentReversal>(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        reversal.Property(r => r.Reason).HasMaxLength(500);
        reversal.HasIndex(r => r.CreatedAtUtc);
    }
}
