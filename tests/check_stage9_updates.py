from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
controller = (ROOT / "Controllers" / "EmployeeDetailsController.cs").read_text(encoding="utf-8")
db_context = (ROOT / "Data" / "AppDbContext.cs").read_text(encoding="utf-8")

checks = {
    "updates use PUT endpoints for all eight detail types": all(
        f'[HttpPut("{route}/{{recordId:long}}")]' in controller
        for route in ["cars", "license-categories", "relatives", "position-history", "classness-history", "awards", "business-trips", "messengers"]
    ),
    "updates require edit policy": controller.count('[Authorize(Policy = "CanEditEmployees")]') >= 17,
    "updates scope record lookup to owning employee": controller.count("x.Id == recordId && x.EmployeeId == employeeId") >= 8,
    "position update excludes current record from overlap check": "x.Id != recordId && (x.EndDate == null || x.EndDate >= r.StartDate)" in controller,
    "award update excludes current record from duplicate check": "x.Id != recordId && x.AwardType == type" in controller,
    "messenger update excludes current record from duplicate check": "x.Id != recordId && x.Messenger == messenger" in controller,
    "license update excludes current record from duplicate check": "x.Id != recordId && x.Category == category" in controller,
    "registration plates normalized on create and update": controller.count("NormalizePlate(r.RegistrationPlate)") == 2,
    "database unique index protects registration plates": "HasIndex(x => x.RegistrationPlate).IsUnique()" in db_context,
    "audit is recorded for edits": controller.count('await Audit("') >= 15 and "_updated\", employeeId, recordId" in controller,
    "invalid date ranges are rejected on edits": "Дата окончания не может быть раньше даты начала." in controller,
    "old IDs remain in place during edits": "entity.Id" in controller and "UpdateCar" in controller,
}
for label, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {label}")
if not all(checks.values()):
    raise SystemExit(1)
print(f"\n{sum(checks.values())}/{len(checks)} static checks passed")
