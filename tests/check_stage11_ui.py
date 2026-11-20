from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
program = (ROOT / "Program.cs").read_text(encoding="utf-8")
html = (ROOT / "wwwroot" / "index.html").read_text(encoding="utf-8")
js = (ROOT / "wwwroot" / "app.js").read_text(encoding="utf-8")
css = (ROOT / "wwwroot" / "styles.css").read_text(encoding="utf-8")
checks = {
    "default static file middleware enabled": "app.UseDefaultFiles();" in program,
    "static file middleware enabled": "app.UseStaticFiles();" in program,
    "login form exists": 'id="login-form"' in html and "/api/auth/login" in js,
    "alerts fetched from server": "api('/api/alerts')" in js,
    "employee search uses existing API": "api(`/api/employees?${params.toString()}`)" in js,
    "employee details use existing API": "api(`/api/employees/${encodeURIComponent(id)}`)" in js,
    "employee name highlight wired to server flag": "a.highlightEmployeeName" in js and "' highlight'" in js,
    "per-employee alerts displayed": "state.alerts.filter(a => Number(a.employeeId) === Number(employee.id))" in js,
    "no unsafe HTML injection": "innerHTML" not in js,
    "expired, due and warning states styled": all(x in css for x in ["severity-overdue", "severity-due", "severity-warning"]),
    "logout clears in-memory token": "state.token = null" in js,
    "alert refresh uses no-store API fetch": "cache: 'no-store'" in js,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {name}")
print(f"\n{len(checks) - len(failed)}/{len(checks)} static checks passed")
if failed:
    raise SystemExit(1)
