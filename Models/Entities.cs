using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Kartoteka.Server.Models;

public sealed class AppUser : IdentityUser
{
    [MaxLength(200)]
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Employee
{
    public long Id { get; set; }
    [MaxLength(100)] public string PersonnelNumber { get; set; } = "";
    [Required, MaxLength(200)] public string LastName { get; set; } = "";
    [Required, MaxLength(200)] public string FirstName { get; set; } = "";
    [MaxLength(200)] public string? MiddleName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    // Passport fields belong to one document group; the scan is attached once to category "passport".
    [MaxLength(20)] public string? PassportSeries { get; set; }
    [MaxLength(20)] public string? PassportNumber { get; set; }
    [MaxLength(300)] public string? PassportIssuedBy { get; set; }
    public DateOnly? PassportIssuedOn { get; set; }
    [MaxLength(300)] public string? Position { get; set; }
    [MaxLength(300)] public string? Subdivision { get; set; }
    [MaxLength(100)] public string? Rank { get; set; }
    // Conscription / military registration details. Keep identifiers optional and access-controlled.
    [MaxLength(100)] public string? MilitaryIdNumber { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationOffice { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationCategory { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationStatus { get; set; }
    public DateOnly? TransferDate { get; set; }
    [MaxLength(300)] public string? TransferFrom { get; set; }
    public long? OrganizationalUnitId { get; set; }
    public OrganizationalUnit? OrganizationalUnit { get; set; }
    [MaxLength(50)] public string? MaritalStatus { get; set; }
    [MaxLength(100)] public string? AccessForm { get; set; }
    public DateOnly? AccessGrantedOn { get; set; }
    [MaxLength(300)] public string? AccessIssuedBy { get; set; }
    public DateOnly? ContractStart { get; set; }
    public int? ContractTermMonths { get; set; }
    public DateOnly? ContractEnd { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<EmployeeCar> Cars { get; set; } = [];
    public List<EmployeeMessenger> Messengers { get; set; } = [];
    public List<PersonalFeature> PersonalFeatures { get; set; } = [];
    public List<EmployeeCustomFieldValue> CustomFieldValues { get; set; } = [];
    public List<DrivingLicenseCategory> LicenseCategories { get; set; } = [];
    public List<Relative> Relatives { get; set; } = [];
    public List<PositionHistory> PositionHistory { get; set; } = [];
    public List<ClassnessHistory> ClassnessHistory { get; set; } = [];
    public List<Award> Awards { get; set; } = [];
    public List<BusinessTrip> BusinessTrips { get; set; } = [];
    public List<EmployeeAttachment> Attachments { get; set; } = [];
}

public sealed class OrganizationalUnit
{
    public long Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public long? ParentUnitId { get; set; }
    public OrganizationalUnit? ParentUnit { get; set; }
    public List<OrganizationalUnit> Children { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class EmployeeMessenger
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(80)] public string Messenger { get; set; } = "";
    [MaxLength(200)] public string Contact { get; set; } = "";
    [MaxLength(200)] public string? Note { get; set; }
}

public sealed class PersonalFeature
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    // tattoo / scar / mole / other; photos are stored as admin-only attachments.
    [MaxLength(30)] public string FeatureType { get; set; } = "other";
    [MaxLength(500)] public string Description { get; set; } = "";
    [MaxLength(200)] public string? LocationOnBody { get; set; }
    public long? AttachmentId { get; set; }
    public EmployeeAttachment? Attachment { get; set; }
}

public sealed class CustomFieldDefinition
{
    public long Id { get; set; }
    // employee / relative; lets administrators extend forms without schema changes.
    [MaxLength(20)] public string Scope { get; set; } = "employee";
    [MaxLength(100)] public string Key { get; set; } = "";
    [MaxLength(200)] public string Label { get; set; } = "";
    [MaxLength(20)] public string DataType { get; set; } = "text";
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class EmployeeCustomFieldValue
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public long FieldDefinitionId { get; set; }
    public CustomFieldDefinition FieldDefinition { get; set; } = null!;
    public string? Value { get; set; }
}

public sealed class RelativeCustomFieldValue
{
    public long Id { get; set; }
    public long RelativeId { get; set; }
    public Relative Relative { get; set; } = null!;
    public long FieldDefinitionId { get; set; }
    public CustomFieldDefinition FieldDefinition { get; set; } = null!;
    public string? Value { get; set; }
}

public sealed class EmployeeCar
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(200)] public string MakeModel { get; set; } = "";
    [MaxLength(30)] public string? RegistrationPlate { get; set; }
    public int? ProductionYear { get; set; }
    [MaxLength(80)] public string? RegistrationCertificateNumber { get; set; }
}

public sealed class DrivingLicenseCategory
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(5)] public string Category { get; set; } = "";
    public DateOnly? ExpiresOn { get; set; }
}

public sealed class Relative
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(200)] public string FullName { get; set; } = "";
    [MaxLength(100)] public string Relationship { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(300)] public string? Workplace { get; set; }
    [MaxLength(300)] public string? Position { get; set; }
    [MaxLength(300)] public string? SchoolOrInstitution { get; set; }
    [MaxLength(80)] public string? BirthCertificateNumber { get; set; }
    [MaxLength(1000)] public string? MessengerContactsJson { get; set; }
    public List<RelativeCustomFieldValue> CustomFieldValues { get; set; } = [];
}

public sealed class PositionHistory
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(300)] public string Position { get; set; } = "";
    [MaxLength(300)] public string? OrganizationOrSubdivision { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [MaxLength(500)] public string? BasisDocument { get; set; }
}

public sealed class ClassnessHistory
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(100)] public string Classness { get; set; } = "";
    public DateOnly GrantedOn { get; set; }
    public DateOnly? ReviewDueOn { get; set; }
    [MaxLength(500)] public string? BasisDocument { get; set; }
}

public sealed class Award
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(30)] public string AwardType { get; set; } = ""; // state / departmental
    [MaxLength(300)] public string Name { get; set; } = "";
    public DateOnly? AwardedOn { get; set; }
    [MaxLength(500)] public string? BasisDocument { get; set; }
}

public sealed class BusinessTrip
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [MaxLength(300)] public string Destination { get; set; } = "";
    [MaxLength(300)] public string? PositionOrAssignment { get; set; }
    [MaxLength(500)] public string? BasisDocument { get; set; }
}

public sealed class EmployeeAttachment
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(255)] public string OriginalFileName { get; set; } = "";
    [MaxLength(120)] public string ContentType { get; set; } = "application/octet-stream";
    [MaxLength(80)] public string Category { get; set; } = "document";
    // The file belongs to one document group on a specific record, not to every field individually.
    // employee/0 means an employee-level document; other targets identify a related record.
    [MaxLength(30)] public string TargetType { get; set; } = "employee";
    public long TargetId { get; set; }
    public byte[] Content { get; set; } = [];
    [MaxLength(256)] public string Sha256 { get; set; } = "";
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UploadedByUserId { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? ActorUserId { get; set; }
    [MaxLength(200)] public string? ActorEmail { get; set; }
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string EntityType { get; set; } = "";
    [MaxLength(100)] public string? EntityId { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(1000)] public string? Details { get; set; }
}


// Дополнительные сведения, не включаемые в общий DTO сотрудника.
public sealed class EmployeeExtendedData
{
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(12)] public string? Inn { get; set; }
    [MaxLength(14)] public string? Snils { get; set; }
    [MaxLength(100)] public string? PekNumber { get; set; }
    public bool? DactyloscopyPerformed { get; set; }
    public DateOnly? DactyloscopyDate { get; set; }
    [MaxLength(12)] public string? BloodGroup { get; set; }
    [MaxLength(8)] public string? RhFactor { get; set; }
    public bool? DnaTestPerformed { get; set; }
    public DateOnly? DnaTestDate { get; set; }
    public DateOnly? UmoPassedOn { get; set; }
    public DateOnly? UmoExpiresOn { get; set; }
    [MaxLength(100)] public string? FitnessCategory { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    [MaxLength(40)] public string? ClothingSize { get; set; }
    [MaxLength(20)] public string? ShoeSize { get; set; }
    [MaxLength(30)] public string? DrivingLicenseSeries { get; set; }
    [MaxLength(30)] public string? DrivingLicenseNumber { get; set; }
    [MaxLength(300)] public string? DrivingLicenseIssuedBy { get; set; }
    public DateOnly? DrivingLicenseIssuedOn { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
}

// Психиатрические сведения хранятся отдельно и доступны только admin.
public sealed class EmployeeConfidentialHealthRecord
{
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    [MaxLength(100)] public string? PsychiatricFitnessCategory { get; set; }
    public DateOnly? PsychiatricAssessmentDate { get; set; }
    [MaxLength(1000)] public string? RestrictedNote { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
}

public sealed class EmployeeLeaveRecord
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    // main / veteran / additional / other
    [MaxLength(20)] public string LeaveType { get; set; } = "main";
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public int PlannedDays { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public int? ActualDays { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "planned"; // planned / in-progress / completed / cancelled
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
}

// Годовые нормы задаются уполномоченным сотрудником; законные основания не зашиваются в код.
public sealed class EmployeeLeaveEntitlement
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int CalendarYear { get; set; }
    public int MainDays { get; set; }
    public int VeteranDays { get; set; }
    public int AdditionalDays { get; set; }
    public int CarryoverDays { get; set; }
    public int ManualAdjustmentDays { get; set; }
    [MaxLength(1000)] public string? BasisNote { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
}

public sealed class EmployeeLeavePreference
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int CalendarYear { get; set; }
    public int Month { get; set; }
    public int Priority { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class LeaveBlackoutPeriod
{
    public long Id { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public long? OrganizationalUnitId { get; set; }
    public OrganizationalUnit? OrganizationalUnit { get; set; }
    [MaxLength(300)] public string Reason { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
}
