from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
html = (ROOT / 'wwwroot' / 'index.html').read_text(encoding='utf-8')
js = (ROOT / 'wwwroot' / 'app.js').read_text(encoding='utf-8')
css = (ROOT / 'wwwroot' / 'styles.css').read_text(encoding='utf-8')
controller = (ROOT / 'Controllers' / 'EmployeesController.cs').read_text(encoding='utf-8')
checks = {
    'create employee button exists': 'id="create-employee-button"' in html,
    'edit employee action exists': 'id="edit-employee-button"' in html,
    'employee form has required first and last name': 'name="lastName"' in html and 'name="firstName"' in html,
    'fields grouped into logical fieldsets': all(label in html for label in ['Основные сведения','Служба и подразделение','Допуск','Контракт','Воинский учёт']),
    'date inputs declare Russian date format': html.count('placeholder="ДД.ММ.ГГГГ"') >= 5,
    'strict DD.MM.YYYY parser exists': r"/^(\d{2})\.(\d{2})\.(\d{4})$/" in js,
    'calendar date validation rejects impossible dates': 'date.getUTCFullYear() !== year' in js,
    'dates converted to ISO before API submit': 'return `${match[3]}-${match[2]}-${match[1]}`' in js,
    'create and update use same employee API endpoints': "'/api/employees'" in js and '`${path}`' not in js and "method: editingId == null ? 'POST' : 'PUT'" in js,
    'edit operation uses existing employee id': 'encodeURIComponent(editingId)' in js and 'state.currentEmployeeId' in js,
    'server role check gates form and create button': "state.roles.includes('admin') || state.roles.includes('editor')" in js,
    'submit button disabled during save': 'state.savingEmployee = true; saveButton.disabled = true' in js,
    'server enforces edit policy': '[Authorize(Policy = "CanEditEmployees")]' in controller,
    'server checks duplicate personnel number on create and update': controller.count('NormalizePersonnelNumber(request.PersonnelNumber)') >= 2,
    'form errors use textContent rather than HTML injection': 'box.textContent = message ||' in js and 'innerHTML' not in js,
    'form responsive styling exists': '.form-grid' in css and '@media(max-width:600px)' in css,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'}: {name}")
print(f"\n{len(checks)-len(failed)}/{len(checks)} static checks passed")
if failed:
    raise SystemExit(1)
