using System.ComponentModel.DataAnnotations;

namespace Kartoteka.Server.Contracts;

public sealed record EmployeeDto(
    long Id,
    string PersonnelNumber,
    string LastName,
    string FirstName,
    string? MiddleName,
    DateOnly? DateOfBirth,
    string? PassportSeries,
    string? PassportNumber,
    string? PassportIssuedBy,
    DateOnly? PassportIssuedOn,
    string? Position,
    string? Subdivision,
    string? Rank,
    string? MaritalStatus,
    string? AccessForm,
    DateOnly? AccessGrantedOn,
    string? AccessIssuedBy,
    DateOnly? ContractStart,
    int? ContractTermMonths,
    DateOnly? ContractEnd,
    bool IsArchived,
    DateTime UpdatedUtc,
    long? OrganizationalUnitId,
    string? OrganizationalUnitName,
    string? MilitaryIdNumber,
    string? MilitaryRegistrationOffice,
    string? MilitaryRegistrationCategory,
    string? MilitaryRegistrationStatus,
    DateOnly? TransferDate,
    string? TransferFrom);

public sealed class CreateEmployeeRequest
{
    [MaxLength(100)] public string PersonnelNumber { get; set; } = "";
    [Required, MaxLength(200)] public string LastName { get; set; } = "";
    [Required, MaxLength(200)] public string FirstName { get; set; } = "";
    [MaxLength(200)] public string? MiddleName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(20)] public string? PassportSeries { get; set; }
    [MaxLength(20)] public string? PassportNumber { get; set; }
    [MaxLength(300)] public string? PassportIssuedBy { get; set; }
    public DateOnly? PassportIssuedOn { get; set; }
    [MaxLength(300)] public string? Position { get; set; }
    [MaxLength(300)] public string? Subdivision { get; set; }
    [MaxLength(100)] public string? Rank { get; set; }
    [MaxLength(100)] public string? MilitaryIdNumber { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationOffice { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationCategory { get; set; }
    [MaxLength(200)] public string? MilitaryRegistrationStatus { get; set; }
    public DateOnly? TransferDate { get; set; }
    [MaxLength(300)] public string? TransferFrom { get; set; }
    public long? OrganizationalUnitId { get; set; }
    [MaxLength(50)] public string? MaritalStatus { get; set; }
    [MaxLength(100)] public string? AccessForm { get; set; }
    public DateOnly? AccessGrantedOn { get; set; }
    [MaxLength(300)] public string? AccessIssuedBy { get; set; }
    public DateOnly? ContractStart { get; set; }
    [Range(1, 600)] public int? ContractTermMonths { get; set; }
    public DateOnly? ContractEnd { get; set; }
}
