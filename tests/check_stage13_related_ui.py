from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
html = (ROOT / 'wwwroot' / 'index.html').read_text(encoding='utf-8')
js = (ROOT / 'wwwroot' / 'app.js').read_text(encoding='utf-8')
css = (ROOT / 'wwwroot' / 'styles.css').read_text(encoding='utf-8')
controller = (ROOT / 'Controllers' / 'EmployeeDetailsController.cs').read_text(encoding='utf-8')
checks = {
    'organizational unit selector exists': 'id="organizational-unit-select"' in html,
    'organizational units loaded from API': "api('/api/organizational-units')" in js,
    'selected unit id sent with employee payload': 'payload.organizationalUnitId = unitValue ? Number(unitValue) : null' in js,
    'related-record form exists': 'id="related-form-dialog"' in html and 'id="related-form-fields"' in html,
    'all eight related record types configured': all(k in js for k in ['cars:', "'license-categories':", 'relatives:', "'position-history':", "'classness-history':", 'awards:', "'business-trips':", 'messengers:']),
    'related records loaded from details API': 'loadEmployeeDetails(employee.id)' in js and '/details`' in js,
    'existing related records use PUT': "method: editingId == null ? 'POST' : 'PUT'" in js,
    'new related records use POST': "method: editingId == null ? 'POST' : 'PUT'" in js,
    'deletion requires confirmation': 'window.confirm(' in js and "method: 'DELETE'" in js,
    'related record dates use strict Russian date parser': 'parseRuDate(raw, label)' in js,
    'trip and position date ranges validated': "kind === 'business-trips'" in js and "kind === 'position-history'" in js,
    'edit controls gated by roles': "state.roles.includes('admin') || state.roles.includes('editor')" in js,
    'server detail controller still enforces CanEditEmployees': '[Authorize(Policy = "CanEditEmployees")]' in controller,
    'safe text rendering used for related data': 'text(\'div\', \'related-value\'' in js and 'innerHTML' not in js,
    'responsive related-record styling exists': '.related-record' in css and '@media(max-width:600px)' in css,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items(): print(f"{'PASS' if ok else 'FAIL'}: {name}")
print(f"\n{len(checks)-len(failed)}/{len(checks)} static checks passed")
if failed: raise SystemExit(1)
