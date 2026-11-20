from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
controller = (ROOT / "Controllers" / "EmployeesController.cs").read_text(encoding="utf-8")
units = (ROOT / "Controllers" / "OrganizationalUnitsController.cs").read_text(encoding="utf-8")
dtos = (ROOT / "Contracts" / "EmployeeDtos.cs").read_text(encoding="utf-8")

checks = {
    "employee list pagination": "pageSize = Math.Clamp(pageSize, 1, 200)" in controller,
    "search by personnel number": "PersonnelNumber.Contains(normalizedNumber)" in controller,
    "duplicate personnel number conflict": "Conflict(new { error = \"Сотрудник с таким табельным номером уже существует.\" })" in controller,
    "archive instead of hard deletion": "employee.IsArchived = true" in controller and "db.Employees.Remove(employee)" not in controller,
    "restore endpoint": '[HttpPost("{id:long}/restore")]' in controller,
    "archived list restricted to admin": 'if (includeArchived && !User.IsInRole("admin"))' in controller,
    "organization unit assigned through FK": "OrganizationalUnitId = r.OrganizationalUnitId" in controller,
    "military registration DTO fields": "MilitaryRegistrationOffice" in dtos,
    "unit hierarchy cycle prevention": "собственного дочернего подразделения" in units,
    "unit administration admin-only": '[Authorize(Policy = "AdminOnly")]' in units,
    "unit deactivate protects active employees": "пока к нему привязаны действующие сотрудники" in units,
    "audit records archive/restore": '"employee_archived"' in controller and '"employee_restored"' in controller,
}
for label, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {label}")
if not all(checks.values()):
    raise SystemExit(1)
print(f"\n{sum(checks.values())}/{len(checks)} static checks passed")
