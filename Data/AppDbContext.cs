using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<OrganizationalUnit> OrganizationalUnits => Set<OrganizationalUnit>();
    public DbSet<EmployeeMessenger> EmployeeMessengers => Set<EmployeeMessenger>();
    public DbSet<PersonalFeature> PersonalFeatures => Set<PersonalFeature>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<EmployeeCustomFieldValue> EmployeeCustomFieldValues => Set<EmployeeCustomFieldValue>();
    public DbSet<RelativeCustomFieldValue> RelativeCustomFieldValues => Set<RelativeCustomFieldValue>();
    public DbSet<EmployeeCar> EmployeeCars => Set<EmployeeCar>();
    public DbSet<DrivingLicenseCategory> DrivingLicenseCategories => Set<DrivingLicenseCategory>();
    public DbSet<Relative> Relatives => Set<Relative>();
    public DbSet<PositionHistory> PositionHistories => Set<PositionHistory>();
    public DbSet<ClassnessHistory> ClassnessHistories => Set<ClassnessHistory>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<BusinessTrip> BusinessTrips => Set<BusinessTrip>();
    public DbSet<EmployeeAttachment> EmployeeAttachments => Set<EmployeeAttachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<EmployeeExtendedData> EmployeeExtendedData => Set<EmployeeExtendedData>();
    public DbSet<EmployeeConfidentialHealthRecord> EmployeeConfidentialHealthRecords
        => Set<EmployeeConfidentialHealthRecord>();
    public DbSet<EmployeeLeaveRecord> EmployeeLeaveRecords => Set<EmployeeLeaveRecord>();
    public DbSet<EmployeeLeaveEntitlement> EmployeeLeaveEntitlements => Set<EmployeeLeaveEntitlement>();
    public DbSet<EmployeeLeavePreference> EmployeeLeavePreferences => Set<EmployeeLeavePreference>();
    public DbSet<LeaveBlackoutPeriod> LeaveBlackoutPeriods => Set<LeaveBlackoutPeriod>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Дополнительные данные: одна запись на сотрудника.
        builder.Entity<EmployeeExtendedData>().HasKey(x => x.EmployeeId);
        builder.Entity<EmployeeExtendedData>()
            .HasOne(x => x.Employee)
            .WithOne()
            .HasForeignKey<EmployeeExtendedData>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Конфиденциальные медицинские сведения: отдельная запись на сотрудника.
        builder.Entity<EmployeeConfidentialHealthRecord>().HasKey(x => x.EmployeeId);
        builder.Entity<EmployeeConfidentialHealthRecord>()
            .HasOne(x => x.Employee)
            .WithOne()
            .HasForeignKey<EmployeeConfidentialHealthRecord>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Employee>().HasIndex(e => e.PersonnelNumber).IsUnique()
            .HasFilter("\"PersonnelNumber\" <> ''");
        // Name index is for search only: it is deliberately NOT unique.
        builder.Entity<Employee>().HasIndex(e => new { e.LastName, e.FirstName, e.MiddleName });
        builder.Entity<Employee>().HasOne(e => e.OrganizationalUnit).WithMany()
            .HasForeignKey(e => e.OrganizationalUnitId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<OrganizationalUnit>().Property(x => x.Name).IsRequired();
        builder.Entity<OrganizationalUnit>().HasIndex(x => new { x.ParentUnitId, x.Name })
            .IsUnique().HasFilter("\"ParentUnitId\" IS NOT NULL");
        builder.Entity<OrganizationalUnit>().HasIndex(x => x.Name)
            .IsUnique().HasFilter("\"ParentUnitId\" IS NULL");
        builder.Entity<OrganizationalUnit>().HasOne(x => x.ParentUnit).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentUnitId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EmployeeMessenger>().HasOne(x => x.Employee).WithMany(x => x.Messengers)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EmployeeMessenger>().HasIndex(x => new { x.EmployeeId, x.Messenger, x.Contact }).IsUnique();
        builder.Entity<PersonalFeature>().HasOne(x => x.Employee).WithMany(x => x.PersonalFeatures)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PersonalFeature>().HasOne(x => x.Attachment).WithMany()
            .HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<CustomFieldDefinition>().HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        builder.Entity<CustomFieldDefinition>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_CustomFieldDefinition_Scope", "\"Scope\" IN ('employee', 'relative')");
            t.HasCheckConstraint("CK_CustomFieldDefinition_DataType", "\"DataType\" IN ('text', 'date', 'number', 'boolean', 'select')");
        });
        builder.Entity<PersonalFeature>().ToTable(t => t.HasCheckConstraint(
            "CK_PersonalFeature_Type", "\"FeatureType\" IN ('tattoo', 'scar', 'mole', 'other')"));
        builder.Entity<EmployeeCustomFieldValue>().HasOne(x => x.Employee).WithMany(x => x.CustomFieldValues)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EmployeeCustomFieldValue>().HasOne(x => x.FieldDefinition).WithMany()
            .HasForeignKey(x => x.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeCustomFieldValue>().HasIndex(x => new { x.EmployeeId, x.FieldDefinitionId }).IsUnique();
        builder.Entity<RelativeCustomFieldValue>().HasOne(x => x.Relative).WithMany(x => x.CustomFieldValues)
            .HasForeignKey(x => x.RelativeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<RelativeCustomFieldValue>().HasOne(x => x.FieldDefinition).WithMany()
            .HasForeignKey(x => x.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RelativeCustomFieldValue>().HasIndex(x => new { x.RelativeId, x.FieldDefinitionId }).IsUnique();
        builder.Entity<Employee>().Property(e => e.LastName).IsRequired();
        builder.Entity<Employee>().Property(e => e.FirstName).IsRequired();

        builder.Entity<EmployeeCar>().HasOne(x => x.Employee).WithMany(x => x.Cars)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        // Registration plates are normalized by the API before saving; the unique index also
        // protects against two concurrent requests creating the same plate.
        builder.Entity<EmployeeCar>().HasIndex(x => x.RegistrationPlate).IsUnique()
            .HasFilter("\"RegistrationPlate\" IS NOT NULL AND \"RegistrationPlate\" <> ''");
        builder.Entity<DrivingLicenseCategory>().HasOne(x => x.Employee).WithMany(x => x.LicenseCategories)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DrivingLicenseCategory>().HasIndex(x => new { x.EmployeeId, x.Category }).IsUnique();
        builder.Entity<Relative>().HasOne(x => x.Employee).WithMany(x => x.Relatives)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PositionHistory>().HasOne(x => x.Employee).WithMany(x => x.PositionHistory)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ClassnessHistory>().HasOne(x => x.Employee).WithMany(x => x.ClassnessHistory)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Award>().HasOne(x => x.Employee).WithMany(x => x.Awards)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<BusinessTrip>().HasOne(x => x.Employee).WithMany(x => x.BusinessTrips)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EmployeeAttachment>().HasOne(x => x.Employee).WithMany(x => x.Attachments)
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EmployeeAttachment>().Property(x => x.Content).HasColumnType("bytea");  
builder.Entity<EmployeeAttachment>().ToTable(t => t.HasCheckConstraint(
    "CK_EmployeeAttachment_TargetType",
    "\"TargetType\" IN ('employee', 'relative', 'car', 'license-category', 'position-history', 'classness', 'award', 'trip', 'messenger')"));
        builder.Entity<EmployeeAttachment>().Property(x => x.TargetType).HasDefaultValue("employee");
        builder.Entity<EmployeeAttachment>().Property(x => x.TargetId).HasDefaultValue(0L);
        builder.Entity<EmployeeAttachment>().HasIndex(x => new { x.EmployeeId, x.Category, x.TargetType, x.TargetId, x.Sha256 }).IsUnique();
        builder.Entity<PositionHistory>().ToTable(t => t.HasCheckConstraint("CK_PositionHistory_DateRange", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\""));
        builder.Entity<BusinessTrip>().ToTable(t => t.HasCheckConstraint("CK_BusinessTrip_DateRange", "\"EndDate\" >= \"StartDate\""));
        builder.Entity<Employee>().ToTable(t => t.HasCheckConstraint("CK_Employee_ContractTermMonths", "\"ContractTermMonths\" IS NULL OR \"ContractTermMonths\" > 0"));
        builder.Entity<Award>().ToTable(t => t.HasCheckConstraint(
            "CK_Award_Type", "\"AwardType\" IN ('state', 'departmental')"));
        builder.Entity<AuditLog>().HasIndex(x => x.OccurredUtc);
        builder.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
		        builder.Entity<EmployeeLeaveRecord>().ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_EmployeeLeaveRecord_PlannedDateRange",
                "\"PlannedEnd\" >= \"PlannedStart\"");

            t.HasCheckConstraint(
                "CK_EmployeeLeaveRecord_ActualDateRange",
                "\"ActualEnd\" IS NULL OR \"ActualStart\" IS NULL OR \"ActualEnd\" >= \"ActualStart\"");

            t.HasCheckConstraint(
                "CK_EmployeeLeaveRecord_Days",
                "\"PlannedDays\" >= 0 AND (\"ActualDays\" IS NULL OR \"ActualDays\" >= 0)");
        });

        builder.Entity<EmployeeLeaveEntitlement>()
            .HasIndex(x => new { x.EmployeeId, x.CalendarYear })
            .IsUnique();

        builder.Entity<EmployeeLeaveEntitlement>().ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_EmployeeLeaveEntitlement_Days",
                "\"MainDays\" >= 0 AND \"VeteranDays\" >= 0 AND \"AdditionalDays\" >= 0 AND \"CarryoverDays\" >= 0");
        });

        builder.Entity<EmployeeLeavePreference>().ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_EmployeeLeavePreference_Month",
                "\"Month\" >= 1 AND \"Month\" <= 12");

            t.HasCheckConstraint(
                "CK_EmployeeLeavePreference_Priority",
                "\"Priority\" >= 1");
        });

        builder.Entity<LeaveBlackoutPeriod>().ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_LeaveBlackoutPeriod_DateRange",
                "\"EndDate\" >= \"StartDate\"");
        });
    }
}
