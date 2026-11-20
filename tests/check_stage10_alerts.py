from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
controller = (ROOT / "Controllers" / "AlertsController.cs").read_text(encoding="utf-8")
checks = {
    "authenticated alert endpoint": '[Authorize(Roles = "admin,editor,viewer")]' in controller and '[HttpGet]' in controller,
    "30-day driver license window": "today.AddDays(30)" in controller,
    "six-calendar-month contract reminder": "contractEnd.Value.AddMonths(-6)" in controller,
    "six-month classness review window": "today.AddMonths(6)" in controller and '"classness-review"' in controller,
    "archived employees excluded": "!e.IsArchived" in controller,
    "contract date can be derived from start and term": "employee.ContractStart.Value.AddMonths(employee.ContractTermMonths.Value)" in controller,
    "alerts have stable non-random IDs": '$"contract:{employee.Id}"' in controller and '$"license:{license.Id}"' in controller and '$"classness:{classness.Id}"' in controller,
    "alerts are computed and not persisted": "db.Alerts" not in controller and "db.Add" not in controller,
    "current data is read without tracking": ".AsNoTracking()" in controller,
    "response is not cached": 'Response.Headers.CacheControl = "no-store"' in controller,
    "employee name highlight flag returned": "bool HighlightEmployeeName" in controller and ", true));" in controller,
    "expired and due statuses represented": '"overdue"' in controller and '"due"' in controller,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {name}")
print(f"\n{len(checks) - len(failed)}/{len(checks)} static checks passed")
if failed:
    raise SystemExit(1)
