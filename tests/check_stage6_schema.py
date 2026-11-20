from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
entities = (ROOT / "Models" / "Entities.cs").read_text(encoding="utf-8")
context = (ROOT / "Data" / "AppDbContext.cs").read_text(encoding="utf-8")
controller = (ROOT / "Controllers" / "EmployeesController.cs").read_text(encoding="utf-8")
checks = {
    "organizational units": "class OrganizationalUnit" in entities and "DbSet<OrganizationalUnit>" in context,
    "messengers": "class EmployeeMessenger" in entities and "DbSet<EmployeeMessenger>" in context,
    "personal features": "class PersonalFeature" in entities and "DbSet<PersonalFeature>" in context,
    "custom field definitions": "class CustomFieldDefinition" in entities and "DbSet<CustomFieldDefinition>" in context,
    "employee custom field unique key": "new { x.EmployeeId, x.FieldDefinitionId }).IsUnique()" in context,
    "relative custom field unique key": "new { x.RelativeId, x.FieldDefinitionId }).IsUnique()" in context,
    "license category unique key": "new { x.EmployeeId, x.Category }).IsUnique()" in context,
    "date-range database checks": "CK_PositionHistory_DateRange" in context and "CK_BusinessTrip_DateRange" in context,
    "contract term check": "CK_Employee_ContractTermMonths" in context,
    "FIO not unique": "Name index is for search only: it is deliberately NOT unique." in context,
    "personnel number normalized": "NormalizePersonnelNumber" in controller and "ToUpperInvariant()" in controller,
    "duplicate personnel number checked": "табельным номером уже существует" in controller,
    "custom field scope constrained": "CK_CustomFieldDefinition_Scope" in context,
}
for label, ok in checks.items():
    print(("PASS" if ok else "FAIL") + " - " + label)
if not all(checks.values()):
    raise SystemExit(1)
print(f"All {len(checks)} static schema checks passed. This does not replace dotnet build or PostgreSQL integration tests.")
