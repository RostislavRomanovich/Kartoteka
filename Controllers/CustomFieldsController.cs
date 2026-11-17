using System.Globalization;
using System.Text.RegularExpressions;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/custom-fields")]
public sealed class CustomFieldsController(AppDbContext db) : ControllerBase
{
    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal) { "employee", "relative" };
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "text", "date", "number", "boolean", "select" };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string scope = "employee")
    {
        scope = scope.Trim().ToLowerInvariant();
        if (!Scopes.Contains(scope)) return BadRequest(new { error = "Недопустимая область пользовательского поля." });
        var items = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(x => x.Scope == scope && x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Scope, x.Key, x.Label, x.DataType, x.IsRequired, x.SortOrder })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create(FieldDefinitionRequest request)
    {
        var error = ValidateDefinition(request);
        if (error is not null) return BadRequest(new { error });
        var scope = request.Scope.Trim().ToLowerInvariant();
        var key = request.Key.Trim().ToLowerInvariant();
        if (await db.CustomFieldDefinitions.AnyAsync(x => x.Scope == scope && x.Key == key))
            return Conflict(new { error = "Поле с таким ключом уже существует в этой области." });
        var item = new CustomFieldDefinition
        {
            Scope = scope, Key = key, Label = request.Label.Trim(),
            DataType = request.DataType.Trim().ToLowerInvariant(), IsRequired = request.IsRequired,
            IsActive = true, SortOrder = request.SortOrder
        };
        db.CustomFieldDefinitions.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), new { scope }, new { item.Id, item.Scope, item.Key, item.Label, item.DataType, item.IsRequired, item.SortOrder });
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(long id, FieldDefinitionRequest request)
    {
        var item = await db.CustomFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        var error = ValidateDefinition(request);
        if (error is not null) return BadRequest(new { error });
        var scope = request.Scope.Trim().ToLowerInvariant();
        var key = request.Key.Trim().ToLowerInvariant();
        if (await db.CustomFieldDefinitions.AnyAsync(x => x.Id != id && x.Scope == scope && x.Key == key))
            return Conflict(new { error = "Поле с таким ключом уже существует в этой области." });
        // Changing scope/key can break existing values, so identity is immutable after creation.
        if (scope != item.Scope || key != item.Key)
            return BadRequest(new { error = "Область и ключ существующего поля неизменяемы. Создайте новое поле и перенесите значения явно." });
        item.Label = request.Label.Trim();
        item.DataType = request.DataType.Trim().ToLowerInvariant();
        item.IsRequired = request.IsRequired;
        item.SortOrder = request.SortOrder;
        await db.SaveChangesAsync();
        return Ok(new { item.Id, item.Scope, item.Key, item.Label, item.DataType, item.IsRequired, item.SortOrder });
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Deactivate(long id)
    {
        var item = await db.CustomFieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        item.IsActive = false; // Keep existing values for history; do not physically delete.
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/api/employees/{employeeId:long}/custom-fields")]
    public async Task<IActionResult> GetEmployeeValues(long employeeId)
    {
        if (!await db.Employees.AnyAsync(x => x.Id == employeeId && !x.IsArchived)) return NotFound();
        var defs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(x => x.Scope == "employee" && x.IsActive).OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Key, x.Label, x.DataType, x.IsRequired, x.SortOrder }).ToListAsync();
        var values = await db.EmployeeCustomFieldValues.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .Select(x => new { x.FieldDefinitionId, x.Value }).ToDictionaryAsync(x => x.FieldDefinitionId, x => x.Value);
        return Ok(defs.Select(d => new { d.Id, d.Key, d.Label, d.DataType, d.IsRequired, d.SortOrder, Value = values.GetValueOrDefault(d.Id) }));
    }

    [HttpPut("/api/employees/{employeeId:long}/custom-fields")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> SaveEmployeeValues(long employeeId, SaveFieldValuesRequest request)
    {
        if (!await db.Employees.AnyAsync(x => x.Id == employeeId && !x.IsArchived)) return NotFound();
        var defs = await db.CustomFieldDefinitions.Where(x => x.Scope == "employee" && x.IsActive).ToListAsync();
        var error = ValidateValues(defs, request.Values);
        if (error is not null) return BadRequest(new { error });
        foreach (var value in request.Values)
        {
            var def = defs.Single(x => x.Id == value.FieldDefinitionId);
            var current = await db.EmployeeCustomFieldValues.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.FieldDefinitionId == def.Id);
            var normalized = string.IsNullOrWhiteSpace(value.Value) ? null : value.Value.Trim();
            if (current is null)
            {
                if (normalized is not null) db.EmployeeCustomFieldValues.Add(new EmployeeCustomFieldValue { EmployeeId = employeeId, FieldDefinitionId = def.Id, Value = normalized });
            }
            else current.Value = normalized;
        }
        await db.SaveChangesAsync();
        return await GetEmployeeValues(employeeId);
    }

    [HttpGet("/api/employees/{employeeId:long}/relatives/{relativeId:long}/custom-fields")]
    public async Task<IActionResult> GetRelativeValues(long employeeId, long relativeId)
    {
        if (!await db.Relatives.AnyAsync(x => x.Id == relativeId && x.EmployeeId == employeeId)) return NotFound();
        var defs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(x => x.Scope == "relative" && x.IsActive).OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Key, x.Label, x.DataType, x.IsRequired, x.SortOrder }).ToListAsync();
        var values = await db.RelativeCustomFieldValues.AsNoTracking().Where(x => x.RelativeId == relativeId)
            .Select(x => new { x.FieldDefinitionId, x.Value }).ToDictionaryAsync(x => x.FieldDefinitionId, x => x.Value);
        return Ok(defs.Select(d => new { d.Id, d.Key, d.Label, d.DataType, d.IsRequired, d.SortOrder, Value = values.GetValueOrDefault(d.Id) }));
    }

    [HttpPut("/api/employees/{employeeId:long}/relatives/{relativeId:long}/custom-fields")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> SaveRelativeValues(long employeeId, long relativeId, SaveFieldValuesRequest request)
    {
        if (!await db.Relatives.AnyAsync(x => x.Id == relativeId && x.EmployeeId == employeeId)) return NotFound();
        var defs = await db.CustomFieldDefinitions.Where(x => x.Scope == "relative" && x.IsActive).ToListAsync();
        var error = ValidateValues(defs, request.Values);
        if (error is not null) return BadRequest(new { error });
        foreach (var value in request.Values)
        {
            var def = defs.Single(x => x.Id == value.FieldDefinitionId);
            var current = await db.RelativeCustomFieldValues.FirstOrDefaultAsync(x => x.RelativeId == relativeId && x.FieldDefinitionId == def.Id);
            var normalized = string.IsNullOrWhiteSpace(value.Value) ? null : value.Value.Trim();
            if (current is null)
            {
                if (normalized is not null) db.RelativeCustomFieldValues.Add(new RelativeCustomFieldValue { RelativeId = relativeId, FieldDefinitionId = def.Id, Value = normalized });
            }
            else current.Value = normalized;
        }
        await db.SaveChangesAsync();
        return await GetRelativeValues(employeeId, relativeId);
    }

    private static string? ValidateDefinition(FieldDefinitionRequest r)
    {
        if (!Scopes.Contains((r.Scope ?? "").Trim().ToLowerInvariant())) return "Область должна быть employee или relative.";
        if (string.IsNullOrWhiteSpace(r.Key) || r.Key.Length > 100 || !Regex.IsMatch(r.Key, "^[a-zA-Z][a-zA-Z0-9_]*$"))
            return "Ключ поля должен начинаться с латинской буквы и содержать только латинские буквы, цифры и подчёркивание.";
        if (string.IsNullOrWhiteSpace(r.Label) || r.Label.Trim().Length > 200) return "Укажите название поля длиной до 200 символов.";
        if (!Types.Contains((r.DataType ?? "").Trim().ToLowerInvariant())) return "Недопустимый тип данных.";
        if (r.SortOrder is < 0 or > 100000) return "Недопустимый порядок сортировки.";
        return null;
    }

    private static string? ValidateValues(List<CustomFieldDefinition> defs, List<FieldValueInput>? values)
    {
        values ??= [];
        if (values.Select(x => x.FieldDefinitionId).Distinct().Count() != values.Count)
            return "Одно поле нельзя передавать несколько раз в одном запросе.";
        if (values.Any(v => defs.All(d => d.Id != v.FieldDefinitionId)))
            return "Передано неактивное поле или поле из другой области.";
        foreach (var def in defs.Where(x => x.IsRequired))
        {
            var item = values.FirstOrDefault(x => x.FieldDefinitionId == def.Id);
            if (item is null || string.IsNullOrWhiteSpace(item.Value))
                return $"Обязательное поле «{def.Label}» не заполнено.";
        }
        foreach (var v in values)
        {
            var def = defs.Single(x => x.Id == v.FieldDefinitionId);
            var value = v.Value?.Trim();
            if (value is { Length: > 4000 }) return $"Значение поля «{def.Label}» превышает 4000 символов.";
            if (string.IsNullOrWhiteSpace(value)) continue;
            var valid = def.DataType switch
            {
                "date" => DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "number" => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
                "boolean" => bool.TryParse(value, out _),
                _ => true
            };
            if (!valid) return $"Значение поля «{def.Label}» не соответствует типу {def.DataType}.";
        }
        return null;
    }
}

public sealed record FieldDefinitionRequest(string Scope, string Key, string Label, string DataType, bool IsRequired, int SortOrder = 0);
public sealed record SaveFieldValuesRequest(List<FieldValueInput> Values);
public sealed record FieldValueInput(long FieldDefinitionId, string? Value);
