from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
controller = (ROOT / "Controllers" / "EmployeeDetailsController.cs").read_text(encoding="utf-8")
entities = (ROOT / "Models" / "Entities.cs").read_text(encoding="utf-8")

checks = {
    "nested records are scoped to owning employee": '[Route("api/employees/{employeeId:long}/details")]' in controller,
    "detail read excludes archived employees": 'e.Id == employeeId && !e.IsArchived' in controller,
    "read endpoint returns all eight detail groups": all(x in controller for x in ["cars, drivingLicenseCategories", "relatives, positionHistory", "classnessHistory", "businessTrips", "messengers"]),
    "mutations require admin or editor policy": controller.count('[Authorize(Policy = "CanEditEmployees")]') >= 9,
    "vehicle plate duplicates are rejected": "Автомобиль с таким регистрационным номером" in controller,
    "license category duplicates are rejected": "Эта категория уже указана" in controller,
    "position history overlap is rejected": "Период пересекается" in controller,
    "award duplicate check exists": "Такая награда с указанной датой уже внесена" in controller,
    "messenger duplicate check exists": "Этот контакт уже указан" in controller,
    "child deletion verifies employee ownership": 'x.Id == recordId && x.EmployeeId == employeeId' in controller,
    "child mutations write audit records": "await Audit(" in controller and "AuditLog" in entities,
    "business trip date validation exists": "Дата окончания командировки не может быть раньше даты начала" in controller,
    "relative supports school and birth certificate": "SchoolOrInstitution" in controller and "BirthCertificateNumber" in controller,
}
for label, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {label}")
if not all(checks.values()):
    raise SystemExit(1)
print(f"\n{sum(checks.values())}/{len(checks)} static checks passed")
