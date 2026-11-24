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

    public DbSet<Doctor> Doctors => Set<Doctor>();

    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

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

            entity.Property(department => department.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

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

        modelBuilder.Entity<Doctor>(entity =>
        {
            entity.ToTable("doctors");

            entity.HasKey(doctor => doctor.Id);
            entity.Property(doctor => doctor.Id)
                .ValueGeneratedOnAdd();

            entity.Property(doctor => doctor.DoctorNumber)
                .IsRequired()
                .HasMaxLength(30);

            entity.HasIndex(doctor => doctor.DoctorNumber)
                .IsUnique()
                .HasDatabaseName("ix_doctors_doctor_number");

            entity.Property(doctor => doctor.FirstName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(doctor => doctor.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(doctor => doctor.RegistrationNumber)
                .IsRequired()
                .HasMaxLength(50);
            // Normalized registration number (uppercase, no spaces) for uniqueness
            entity.HasIndex(doctor => doctor.RegistrationNumber)
                .IsUnique()
                .HasDatabaseName("ix_doctors_registration_number");

            entity.Property(doctor => doctor.Specialization)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(doctor => doctor.Qualifications)
                .HasMaxLength(200);
            entity.Property(doctor => doctor.PhoneNumber)
                .HasMaxLength(32);
            entity.Property(doctor => doctor.Email)
                .HasMaxLength(254);

            entity.Property(doctor => doctor.DepartmentId)
                .IsRequired();
            entity.HasOne(doctor => doctor.Department)
                .WithMany()
                .HasForeignKey(doctor => doctor.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(doctor => doctor.ApplicationUserId);
            entity.HasOne(doctor => doctor.ApplicationUser)
                .WithOne()
                .HasForeignKey<Doctor>(doctor => doctor.ApplicationUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(doctor => doctor.ApplicationUserId)
                .IsUnique()
                .HasFilter(null) // allow multiple nulls
                .HasDatabaseName("ix_doctors_application_user_id");

            entity.Property(doctor => doctor.IsActive)
                .IsRequired()
                .HasDefaultValue(true);
            entity.Property(doctor => doctor.CreatedAtUtc)
                .IsRequired();
            entity.Property(doctor => doctor.UpdatedAtUtc)
                .IsRequired();
            entity.Property(doctor => doctor.CreatedByUserId);
            entity.Property(doctor => doctor.UpdatedByUserId);

            entity.Property(doctor => doctor.ConcurrencyToken)
                .IsConcurrencyToken()
                .IsRequired()
                .HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<DoctorSchedule>(entity =>
        {
            entity.ToTable("doctor_schedules");
            entity.HasKey(schedule => schedule.Id);

            entity.Property(schedule => schedule.Id).ValueGeneratedOnAdd();
            entity.Property(schedule => schedule.DayOfWeek).IsRequired();
            entity.Property(schedule => schedule.StartTime).IsRequired();
            entity.Property(schedule => schedule.EndTime).IsRequired();
            entity.Property(schedule => schedule.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(schedule => schedule.CreatedAtUtc).IsRequired();
            entity.Property(schedule => schedule.UpdatedAtUtc).IsRequired();
            entity.Property(schedule => schedule.ConcurrencyToken)
                .IsConcurrencyToken()
                .IsRequired()
                .HasDefaultValueSql("gen_random_uuid()");

            entity.HasOne(schedule => schedule.Doctor)
                .WithMany()
                .HasForeignKey(schedule => schedule.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(schedule => new { schedule.DoctorId, schedule.DayOfWeek, schedule.IsActive });
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("appointments", table =>
            {
                table.HasCheckConstraint("CK_appointments_valid_interval", "\"EndAtUtc\" > \"StartAtUtc\"");
                table.HasCheckConstraint("CK_appointments_valid_status", "\"Status\" IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'Missed')");
            });

            entity.HasKey(appointment => appointment.Id);
            entity.Property(appointment => appointment.Id).ValueGeneratedOnAdd();
            entity.Property(appointment => appointment.AppointmentNumber).IsRequired().HasMaxLength(30);
            entity.Property(appointment => appointment.ReasonForVisit).HasMaxLength(300);
            entity.Property(appointment => appointment.Notes).HasMaxLength(500);
            entity.Property(appointment => appointment.CancellationReason).HasMaxLength(300);
            entity.Property(appointment => appointment.IdempotencyKey).IsRequired().HasMaxLength(64);
            entity.Property(appointment => appointment.BookingRequestFingerprint).HasMaxLength(64);
            entity.Property(appointment => appointment.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
            entity.Property(appointment => appointment.StartAtUtc).IsRequired();
            entity.Property(appointment => appointment.EndAtUtc).IsRequired();
            entity.Property(appointment => appointment.CreatedAtUtc).IsRequired();
            entity.Property(appointment => appointment.UpdatedAtUtc).IsRequired();
            entity.Property(appointment => appointment.CreatedByUserId).IsRequired();
            entity.Property(appointment => appointment.UpdatedByUserId).IsRequired();
            entity.Property(appointment => appointment.ConcurrencyToken)
                .IsConcurrencyToken()
                .IsRequired()
                .HasDefaultValueSql("gen_random_uuid()");

            entity.HasOne(appointment => appointment.Patient)
                .WithMany()
                .HasForeignKey(appointment => appointment.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(appointment => appointment.Doctor)
                .WithMany()
                .HasForeignKey(appointment => appointment.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(appointment => appointment.Department)
                .WithMany()
                .HasForeignKey(appointment => appointment.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(appointment => appointment.AppointmentNumber)
                .IsUnique()
                .HasDatabaseName("ix_appointments_appointment_number");
            entity.HasIndex(appointment => new { appointment.CreatedByUserId, appointment.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("ix_appointments_actor_idempotency_key");
            entity.HasIndex(appointment => new { appointment.DoctorId, appointment.StartAtUtc });
            entity.HasIndex(appointment => new { appointment.PatientId, appointment.StartAtUtc });
            entity.HasIndex(appointment => new { appointment.Status, appointment.StartAtUtc });
        });
    }
}
