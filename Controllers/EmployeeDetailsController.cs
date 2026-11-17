using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartoteka.Server.Controllers;

// Child records are always addressed through their owning employee to prevent cross-employee edits.
[ApiController]
[Authorize]
[Route("api/employees/{employeeId:long}/details")]
public sealed class EmployeeDetailsController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDetails(long employeeId)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && !e.IsArchived)
            .Select(e => new { e.Id, e.LastName, e.FirstName, e.MiddleName })
            .FirstOrDefaultAsync();
        if (employee is null) return NotFound();

        var cars = await db.EmployeeCars.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.MakeModel, x.RegistrationPlate, x.ProductionYear, x.RegistrationCertificateNumber }).ToListAsync();
        var licenses = await db.DrivingLicenseCategories.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Category).Select(x => new { x.Id, x.Category, x.ExpiresOn }).ToListAsync();
        var relatives = await db.Relatives.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName, x.Relationship, x.DateOfBirth, x.Workplace, x.Position, x.SchoolOrInstitution, x.BirthCertificateNumber, x.MessengerContactsJson }).ToListAsync();
        var positions = await db.PositionHistories.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.StartDate).Select(x => new { x.Id, x.Position, x.OrganizationOrSubdivision, x.StartDate, x.EndDate, x.BasisDocument }).ToListAsync();
        var classness = await db.ClassnessHistories.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.GrantedOn).Select(x => new { x.Id, x.Classness, x.GrantedOn, x.ReviewDueOn, x.BasisDocument }).ToListAsync();
        var awards = await db.Awards.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.AwardedOn).Select(x => new { x.Id, x.AwardType, x.Name, x.AwardedOn, x.BasisDocument }).ToListAsync();
        var trips = await db.BusinessTrips.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.StartDate).Select(x => new { x.Id, x.StartDate, x.EndDate, x.Destination, x.PositionOrAssignment, x.BasisDocument }).ToListAsync();
        var messengers = await db.EmployeeMessengers.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Messenger).Select(x => new { x.Id, x.Messenger, x.Contact, x.Note }).ToListAsync();
        return Ok(new { employee, cars, drivingLicenseCategories = licenses, relatives, positionHistory = positions, classnessHistory = classness, awards, businessTrips = trips, messengers });
    }

    [HttpPost("cars")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddCar(long employeeId, CarRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (string.IsNullOrWhiteSpace(r.MakeModel)) return BadRequest(new { error = "Укажите марку и модель автомобиля." });
        if (r.ProductionYear is < 1886 or > 2100) return BadRequest(new { error = "Некорректный год выпуска." });
        var plate = NormalizePlate(r.RegistrationPlate);
        if (plate is not null && await db.EmployeeCars.AnyAsync(x => x.EmployeeId == employeeId && x.RegistrationPlate == plate))
            return Conflict(new { error = "Автомобиль с таким регистрационным номером уже указан у этого сотрудника." });
        var entity = new EmployeeCar { EmployeeId = employeeId, MakeModel = r.MakeModel.Trim(), RegistrationPlate = plate, ProductionYear = r.ProductionYear, RegistrationCertificateNumber = Clean(r.RegistrationCertificateNumber) };
        db.EmployeeCars.Add(entity);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { error = "Автомобиль с таким регистрационным номером уже зарегистрирован." });
        }
        await Audit("employee_car_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/cars/{entity.Id}", new { entity.Id, entity.MakeModel, entity.RegistrationPlate, entity.ProductionYear, entity.RegistrationCertificateNumber });
    }

    [HttpPost("license-categories")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddLicenseCategory(long employeeId, LicenseCategoryRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var category = Clean(r.Category)?.ToUpperInvariant();
        if (category is null || category.Length > 5) return BadRequest(new { error = "Укажите корректную категорию водительского удостоверения." });
        if (await db.DrivingLicenseCategories.AnyAsync(x => x.EmployeeId == employeeId && x.Category == category))
            return Conflict(new { error = "Эта категория уже указана у сотрудника." });
        var entity = new DrivingLicenseCategory { EmployeeId = employeeId, Category = category, ExpiresOn = r.ExpiresOn };
        db.DrivingLicenseCategories.Add(entity); await db.SaveChangesAsync(); await Audit("license_category_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/license-categories/{entity.Id}", new { entity.Id, entity.Category, entity.ExpiresOn });
    }

    [HttpPost("relatives")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddRelative(long employeeId, RelativeRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (string.IsNullOrWhiteSpace(r.FullName) || string.IsNullOrWhiteSpace(r.Relationship)) return BadRequest(new { error = "ФИО родственника и степень родства обязательны." });
        var entity = new Relative { EmployeeId = employeeId, FullName = r.FullName.Trim(), Relationship = r.Relationship.Trim(), DateOfBirth = r.DateOfBirth, Workplace = Clean(r.Workplace), Position = Clean(r.Position), SchoolOrInstitution = Clean(r.SchoolOrInstitution), BirthCertificateNumber = Clean(r.BirthCertificateNumber), MessengerContactsJson = Clean(r.MessengerContactsJson) };
        db.Relatives.Add(entity); await db.SaveChangesAsync(); await Audit("relative_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/relatives/{entity.Id}", new { entity.Id, entity.FullName, entity.Relationship, entity.DateOfBirth, entity.Workplace, entity.Position, entity.SchoolOrInstitution, entity.BirthCertificateNumber, entity.MessengerContactsJson });
    }

    [HttpPost("position-history")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddPosition(long employeeId, PositionHistoryRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Position)) return BadRequest(new { error = "Наименование должности обязательно." });
        if (r.EndDate is not null && r.EndDate < r.StartDate) return BadRequest(new { error = "Дата окончания не может быть раньше даты начала." });
        var overlap = await db.PositionHistories.AnyAsync(x => x.EmployeeId == employeeId && (x.EndDate == null || x.EndDate >= r.StartDate) && (r.EndDate == null || x.StartDate <= r.EndDate));
        if (overlap) return Conflict(new { error = "Период пересекается с уже сохранённой историей должностей. Проверьте даты." });
        var entity = new PositionHistory { EmployeeId = employeeId, Position = r.Position.Trim(), OrganizationOrSubdivision = Clean(r.OrganizationOrSubdivision), StartDate = r.StartDate, EndDate = r.EndDate, BasisDocument = Clean(r.BasisDocument) };
        db.PositionHistories.Add(entity); await db.SaveChangesAsync(); await Audit("position_history_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/position-history/{entity.Id}", new { entity.Id, entity.Position, entity.OrganizationOrSubdivision, entity.StartDate, entity.EndDate, entity.BasisDocument });
    }

    [HttpPost("classness-history")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddClassness(long employeeId, ClassnessRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Classness)) return BadRequest(new { error = "Укажите классность." });
        if (r.ReviewDueOn is not null && r.ReviewDueOn < r.GrantedOn) return BadRequest(new { error = "Дата проверки не может быть раньше даты присвоения." });
        var entity = new ClassnessHistory { EmployeeId = employeeId, Classness = r.Classness.Trim(), GrantedOn = r.GrantedOn, ReviewDueOn = r.ReviewDueOn, BasisDocument = Clean(r.BasisDocument) };
        db.ClassnessHistories.Add(entity); await db.SaveChangesAsync(); await Audit("classness_history_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/classness-history/{entity.Id}", new { entity.Id, entity.Classness, entity.GrantedOn, entity.ReviewDueOn, entity.BasisDocument });
    }

    [HttpPost("awards")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddAward(long employeeId, AwardRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var type = Clean(r.AwardType)?.ToLowerInvariant();
        if (type is not ("state" or "departmental")) return BadRequest(new { error = "Тип награды должен быть state или departmental." });
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { error = "Наименование награды обязательно." });
        if (await db.Awards.AnyAsync(x => x.EmployeeId == employeeId && x.AwardType == type && x.Name == r.Name.Trim() && x.AwardedOn == r.AwardedOn))
            return Conflict(new { error = "Такая награда с указанной датой уже внесена." });
        var entity = new Award { EmployeeId = employeeId, AwardType = type, Name = r.Name.Trim(), AwardedOn = r.AwardedOn, BasisDocument = Clean(r.BasisDocument) };
        db.Awards.Add(entity); await db.SaveChangesAsync(); await Audit("award_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/awards/{entity.Id}", new { entity.Id, entity.AwardType, entity.Name, entity.AwardedOn, entity.BasisDocument });
    }

    [HttpPost("business-trips")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddBusinessTrip(long employeeId, BusinessTripRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (r.EndDate < r.StartDate) return BadRequest(new { error = "Дата окончания командировки не может быть раньше даты начала." });
        if (string.IsNullOrWhiteSpace(r.Destination)) return BadRequest(new { error = "Укажите место командировки." });
        var entity = new BusinessTrip { EmployeeId = employeeId, StartDate = r.StartDate, EndDate = r.EndDate, Destination = r.Destination.Trim(), PositionOrAssignment = Clean(r.PositionOrAssignment), BasisDocument = Clean(r.BasisDocument) };
        db.BusinessTrips.Add(entity); await db.SaveChangesAsync(); await Audit("business_trip_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/business-trips/{entity.Id}", new { entity.Id, entity.StartDate, entity.EndDate, entity.Destination, entity.PositionOrAssignment, entity.BasisDocument });
    }

    [HttpPost("messengers")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> AddMessenger(long employeeId, MessengerRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Messenger) || string.IsNullOrWhiteSpace(r.Contact)) return BadRequest(new { error = "Мессенджер и контакт обязательны." });
        var messenger = r.Messenger.Trim(); var contact = r.Contact.Trim();
        if (await db.EmployeeMessengers.AnyAsync(x => x.EmployeeId == employeeId && x.Messenger == messenger && x.Contact == contact))
            return Conflict(new { error = "Этот контакт уже указан у сотрудника." });
        var entity = new EmployeeMessenger { EmployeeId = employeeId, Messenger = messenger, Contact = contact, Note = Clean(r.Note) };
        db.EmployeeMessengers.Add(entity); await db.SaveChangesAsync(); await Audit("messenger_added", employeeId, entity.Id);
        return Created($"/api/employees/{employeeId}/details/messengers/{entity.Id}", new { entity.Id, entity.Messenger, entity.Contact, entity.Note });
    }

    // Stage 9: edit existing child records in place. IDs remain stable; no replacement rows are created.
    [HttpPut("cars/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateCar(long employeeId, long recordId, CarRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.EmployeeCars.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.MakeModel)) return BadRequest(new { error = "Укажите марку и модель автомобиля." });
        if (r.ProductionYear is < 1886 or > 2100) return BadRequest(new { error = "Некорректный год выпуска." });
        var plate = NormalizePlate(r.RegistrationPlate);
        if (plate is not null && await db.EmployeeCars.AnyAsync(x => x.Id != recordId && x.RegistrationPlate == plate))
            return Conflict(new { error = "Автомобиль с таким регистрационным номером уже зарегистрирован." });
        entity.MakeModel = r.MakeModel.Trim(); entity.RegistrationPlate = plate;
        entity.ProductionYear = r.ProductionYear; entity.RegistrationCertificateNumber = Clean(r.RegistrationCertificateNumber);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { error = "Автомобиль с таким регистрационным номером уже зарегистрирован." });
        }
        await Audit("employee_car_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.MakeModel, entity.RegistrationPlate, entity.ProductionYear, entity.RegistrationCertificateNumber });
    }

    [HttpPut("license-categories/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateLicenseCategory(long employeeId, long recordId, LicenseCategoryRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.DrivingLicenseCategories.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        var category = Clean(r.Category)?.ToUpperInvariant();
        if (category is null || category.Length > 5) return BadRequest(new { error = "Укажите корректную категорию водительского удостоверения." });
        if (await db.DrivingLicenseCategories.AnyAsync(x => x.EmployeeId == employeeId && x.Id != recordId && x.Category == category))
            return Conflict(new { error = "Эта категория уже указана у сотрудника." });
        entity.Category = category; entity.ExpiresOn = r.ExpiresOn;
        await db.SaveChangesAsync(); await Audit("license_category_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.Category, entity.ExpiresOn });
    }

    [HttpPut("relatives/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateRelative(long employeeId, long recordId, RelativeRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.Relatives.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.FullName) || string.IsNullOrWhiteSpace(r.Relationship)) return BadRequest(new { error = "ФИО родственника и степень родства обязательны." });
        entity.FullName = r.FullName.Trim(); entity.Relationship = r.Relationship.Trim(); entity.DateOfBirth = r.DateOfBirth;
        entity.Workplace = Clean(r.Workplace); entity.Position = Clean(r.Position); entity.SchoolOrInstitution = Clean(r.SchoolOrInstitution);
        entity.BirthCertificateNumber = Clean(r.BirthCertificateNumber); entity.MessengerContactsJson = Clean(r.MessengerContactsJson);
        await db.SaveChangesAsync(); await Audit("relative_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.FullName, entity.Relationship, entity.DateOfBirth, entity.Workplace, entity.Position, entity.SchoolOrInstitution, entity.BirthCertificateNumber, entity.MessengerContactsJson });
    }

    [HttpPut("position-history/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdatePosition(long employeeId, long recordId, PositionHistoryRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.PositionHistories.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Position)) return BadRequest(new { error = "Наименование должности обязательно." });
        if (r.EndDate is not null && r.EndDate < r.StartDate) return BadRequest(new { error = "Дата окончания не может быть раньше даты начала." });
        var overlap = await db.PositionHistories.AnyAsync(x => x.EmployeeId == employeeId && x.Id != recordId && (x.EndDate == null || x.EndDate >= r.StartDate) && (r.EndDate == null || x.StartDate <= r.EndDate));
        if (overlap) return Conflict(new { error = "Период пересекается с другой записью истории должностей." });
        entity.Position = r.Position.Trim(); entity.OrganizationOrSubdivision = Clean(r.OrganizationOrSubdivision);
        entity.StartDate = r.StartDate; entity.EndDate = r.EndDate; entity.BasisDocument = Clean(r.BasisDocument);
        await db.SaveChangesAsync(); await Audit("position_history_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.Position, entity.OrganizationOrSubdivision, entity.StartDate, entity.EndDate, entity.BasisDocument });
    }

    [HttpPut("classness-history/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateClassness(long employeeId, long recordId, ClassnessRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.ClassnessHistories.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Classness)) return BadRequest(new { error = "Укажите классность." });
        if (r.ReviewDueOn is not null && r.ReviewDueOn < r.GrantedOn) return BadRequest(new { error = "Дата проверки не может быть раньше даты присвоения." });
        entity.Classness = r.Classness.Trim(); entity.GrantedOn = r.GrantedOn; entity.ReviewDueOn = r.ReviewDueOn; entity.BasisDocument = Clean(r.BasisDocument);
        await db.SaveChangesAsync(); await Audit("classness_history_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.Classness, entity.GrantedOn, entity.ReviewDueOn, entity.BasisDocument });
    }

    [HttpPut("awards/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateAward(long employeeId, long recordId, AwardRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.Awards.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        var type = Clean(r.AwardType)?.ToLowerInvariant();
        if (type is not ("state" or "departmental")) return BadRequest(new { error = "Тип награды должен быть state или departmental." });
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { error = "Наименование награды обязательно." });
        var name = r.Name.Trim();
        if (await db.Awards.AnyAsync(x => x.EmployeeId == employeeId && x.Id != recordId && x.AwardType == type && x.Name == name && x.AwardedOn == r.AwardedOn))
            return Conflict(new { error = "Такая награда с указанной датой уже внесена." });
        entity.AwardType = type; entity.Name = name; entity.AwardedOn = r.AwardedOn; entity.BasisDocument = Clean(r.BasisDocument);
        await db.SaveChangesAsync(); await Audit("award_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.AwardType, entity.Name, entity.AwardedOn, entity.BasisDocument });
    }

    [HttpPut("business-trips/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateBusinessTrip(long employeeId, long recordId, BusinessTripRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.BusinessTrips.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (r.EndDate < r.StartDate) return BadRequest(new { error = "Дата окончания командировки не может быть раньше даты начала." });
        if (string.IsNullOrWhiteSpace(r.Destination)) return BadRequest(new { error = "Укажите место командировки." });
        entity.StartDate = r.StartDate; entity.EndDate = r.EndDate; entity.Destination = r.Destination.Trim();
        entity.PositionOrAssignment = Clean(r.PositionOrAssignment); entity.BasisDocument = Clean(r.BasisDocument);
        await db.SaveChangesAsync(); await Audit("business_trip_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.StartDate, entity.EndDate, entity.Destination, entity.PositionOrAssignment, entity.BasisDocument });
    }

    [HttpPut("messengers/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> UpdateMessenger(long employeeId, long recordId, MessengerRequest r)
    {
        if (!await ActiveEmployeeExists(employeeId)) return NotFound();
        var entity = await db.EmployeeMessengers.SingleOrDefaultAsync(x => x.Id == recordId && x.EmployeeId == employeeId);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Messenger) || string.IsNullOrWhiteSpace(r.Contact)) return BadRequest(new { error = "Мессенджер и контакт обязательны." });
        var messenger = r.Messenger.Trim(); var contact = r.Contact.Trim();
        if (await db.EmployeeMessengers.AnyAsync(x => x.EmployeeId == employeeId && x.Id != recordId && x.Messenger == messenger && x.Contact == contact))
            return Conflict(new { error = "Этот контакт уже указан у сотрудника." });
        entity.Messenger = messenger; entity.Contact = contact; entity.Note = Clean(r.Note);
        await db.SaveChangesAsync(); await Audit("messenger_updated", employeeId, recordId);
        return Ok(new { entity.Id, entity.Messenger, entity.Contact, entity.Note });
    }

    // Generic delete route is type-scoped and verifies ownership before removal.
    [HttpDelete("{kind}/{recordId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> DeleteDetail(long employeeId, string kind, long recordId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived)) return NotFound();
        int affected = kind.ToLowerInvariant() switch
        {
            "cars" => await db.EmployeeCars.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "license-categories" => await db.DrivingLicenseCategories.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "relatives" => await db.Relatives.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "position-history" => await db.PositionHistories.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "classness-history" => await db.ClassnessHistories.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "awards" => await db.Awards.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "business-trips" => await db.BusinessTrips.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            "messengers" => await db.EmployeeMessengers.Where(x => x.Id == recordId && x.EmployeeId == employeeId).ExecuteDeleteAsync(),
            _ => -1
        };
        if (affected == -1) return BadRequest(new { error = "Неизвестный тип записи." });
        if (affected == 0) return NotFound();
        await Audit($"{kind.ToLowerInvariant()}_deleted", employeeId, recordId);
        return NoContent();
    }

    private Task<bool> ActiveEmployeeExists(long id) => db.Employees.AnyAsync(e => e.Id == id && !e.IsArchived);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizePlate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }
    private async Task Audit(string action, long employeeId, long recordId)
    {
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value, ActorEmail = User.FindFirst(ClaimTypes.Email)?.Value, Action = action, EntityType = "employee_detail", EntityId = $"employee:{employeeId};record:{recordId}", IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() });
        await db.SaveChangesAsync();
    }
}

public sealed record CarRequest(string MakeModel, string? RegistrationPlate, int? ProductionYear, string? RegistrationCertificateNumber);
public sealed record LicenseCategoryRequest(string Category, DateOnly? ExpiresOn);
public sealed record RelativeRequest(string FullName, string Relationship, DateOnly? DateOfBirth, string? Workplace, string? Position, string? SchoolOrInstitution, string? BirthCertificateNumber, string? MessengerContactsJson);
public sealed record PositionHistoryRequest(string Position, string? OrganizationOrSubdivision, DateOnly StartDate, DateOnly? EndDate, string? BasisDocument);
public sealed record ClassnessRequest(string Classness, DateOnly GrantedOn, DateOnly? ReviewDueOn, string? BasisDocument);
public sealed record AwardRequest(string AwardType, string Name, DateOnly? AwardedOn, string? BasisDocument);
public sealed record BusinessTripRequest(DateOnly StartDate, DateOnly EndDate, string Destination, string? PositionOrAssignment, string? BasisDocument);
public sealed record MessengerRequest(string Messenger, string Contact, string? Note);
