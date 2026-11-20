using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

public sealed class HospitalDbContext(DbContextOptions<HospitalDbContext> options)
    : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

            entity.HasIndex(patient => patient.PatientNumber)
                .IsUnique()
                .HasDatabaseName("ix_patients_patient_number");

            entity.HasIndex(patient => new { patient.LastName, patient.FirstName })
                .HasDatabaseName("ix_patients_name");
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
    }
}
