using HospitalManagementSystem.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

public sealed class HospitalDbContext(DbContextOptions<HospitalDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.UserName).IsRequired().HasMaxLength(256);
            entity.Property(user => user.NormalizedUserName).HasMaxLength(256);
            entity.Property(user => user.Email).IsRequired().HasMaxLength(256);
            entity.Property(user => user.NormalizedEmail).IsRequired().HasMaxLength(256);
            entity.Property(user => user.CreatedAtUtc).IsRequired();
            entity.Property(user => user.UpdatedAtUtc).IsRequired();

            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.Property(role => role.Name).IsRequired().HasMaxLength(256);
            entity.Property(role => role.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("audit_log_entries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Action).IsRequired().HasMaxLength(100);
            entity.Property(entry => entry.TargetUserId).HasMaxLength(100);
            entity.Property(entry => entry.Details).HasMaxLength(500);
            entity.Property(entry => entry.IpAddress).IsRequired().HasMaxLength(45);
            entity.Property(entry => entry.OccurredAtUtc).IsRequired();
            entity.HasIndex(entry => entry.OccurredAtUtc);
            entity.HasIndex(entry => entry.ActorUserId);
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.ToTable("patients");

            entity.HasKey(patient => patient.Id);

            entity.Property(patient => patient.Id)
                .ValueGeneratedOnAdd();

            entity.Property(patient => patient.PatientNumber)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(patient => patient.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(patient => patient.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(patient => patient.DateOfBirth)
                .IsRequired();

            entity.Property(patient => patient.PhoneNumber)
                .HasMaxLength(32);

            entity.Property(patient => patient.Email)
                .HasMaxLength(254);

            entity.Property(patient => patient.Address)
                .HasMaxLength(500);

            entity.Property(patient => patient.CreatedAtUtc)
                .IsRequired();

            entity.Property(patient => patient.UpdatedAtUtc)
                .IsRequired();

            entity.Property(patient => patient.Gender)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Unknown");

            entity.Property(patient => patient.NIC)
                .HasMaxLength(30);

            entity.Property(patient => patient.BloodGroup)
                .HasMaxLength(10);

            entity.Property(patient => patient.EmergencyContactName)
                .HasMaxLength(100);

            entity.Property(patient => patient.EmergencyContactPhone)
                .HasMaxLength(32);

            entity.Property(patient => patient.CreatedByUserId);
            entity.Property(patient => patient.UpdatedByUserId);
            entity.Property(patient => patient.IsActive)
                .IsRequired()
                .HasDefaultValue(true);
            entity.Property(patient => patient.ConcurrencyToken)
                .IsConcurrencyToken()
                .IsRequired()
                .HasDefaultValueSql("gen_random_uuid()");

            entity.HasIndex(patient => patient.PatientNumber)
                .IsUnique()
                .HasDatabaseName("ix_patients_patient_number");

            entity.HasIndex(patient => new { patient.LastName, patient.FirstName })
                .HasDatabaseName("ix_patients_name");

            entity.HasIndex(patient => new { patient.LastName, patient.FirstName, patient.DateOfBirth })
                .HasDatabaseName("ix_patients_identity_match");

            entity.HasIndex(patient => patient.NIC)
                .HasDatabaseName("ix_patients_nic");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");

            entity.HasKey(department => department.Id);

            entity.Property(department => department.Id)
                .ValueGeneratedOnAdd();

            entity.Property(department => department.DepartmentCode)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(department => department.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(department => department.CreatedAtUtc)
                .IsRequired();

            entity.Property(department => department.UpdatedAtUtc)
                .IsRequired();

            entity.HasIndex(department => department.DepartmentCode)
                .IsUnique()
                .HasDatabaseName("ix_departments_department_code");

            entity.HasIndex(department => department.Name)
                .IsUnique()
                .HasDatabaseName("ix_departments_name");
        });

        modelBuilder.Entity<PatientDocument>(entity =>
        {
            entity.ToTable("patient_documents");
            entity.HasKey(document => document.Id);

            entity.Property(document => document.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(document => document.StoredFileName).IsRequired().HasMaxLength(255);
            entity.Property(document => document.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(document => document.SizeBytes).IsRequired();
            entity.Property(document => document.UploadedByUserId).IsRequired();
            entity.Property(document => document.UploadedAtUtc).IsRequired();

            entity.HasIndex(document => document.PatientId);
            entity.HasIndex(document => document.StoredFileName).IsUnique();

            entity.HasOne(document => document.Patient)
                .WithMany()
                .HasForeignKey(document => document.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
