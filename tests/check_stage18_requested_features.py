from pathlib import Path
root = Path(__file__).resolve().parents[1]
js = (root / 'wwwroot/app.js').read_text(encoding='utf-8')
html = (root / 'wwwroot/index.html').read_text(encoding='utf-8')
css = (root / 'wwwroot/styles.css').read_text(encoding='utf-8')
photo = (root / 'Controllers/EmployeePhotoController.cs').read_text(encoding='utf-8')
attachments = (root / 'Controllers/AttachmentsController.cs').read_text(encoding='utf-8')
export = (root / 'Controllers/ExportController.cs').read_text(encoding='utf-8')
checks = {
    'photo has separate authenticated endpoint': '[Route("api/employees/{employeeId:long}/photo")]' in photo and '[HttpGet]' in photo,
    'only admins can replace or delete photo': '[Authorize(Policy = "AdminOnly")]' in photo and '[HttpPut]' in photo and '[HttpDelete]' in photo,
    'photo is scoped to one employee': 'a.EmployeeId == employeeId && a.Category == PhotoCategory' in photo,
    'photo type and size validated': '5 МБ' in photo and 'DetectImageType' in photo,
    'photo is hidden from document listing': 'a.Category != "employee_photo"' in attachments,
    'portrait block exists in employee card': 'id="employee-photo-section"' in html and 'id="employee-photo-image"' in html,
    'edit button is in dialog heading': 'dialog-heading-actions' in html and 'id="edit-employee-button"' in html,
    'employee rows can be selected': 'id="select-page-employees"' in html and 'selectedEmployeeIds' in js,
    'export field picker exists': 'id="export-fields-list"' in html and "api('/api/export/fields')" in js,
    'Word Excel CSV and print choices exist': all(x in html for x in ['value="docx"', 'value="xlsx"', 'value="csv"', 'value="print"']),
    'server provides Word Excel CSV and print formats': all(x in export for x in ['"docx" =>', '"xlsx" =>', '"csv" =>', '"print" =>']),
    'custom employee fields can be exported': 'customField_' in export and 'EmployeeCustomFieldValues' in export,
    'export field restrictions enforced server-side': 'SensitiveKeys.Contains' in export and 'return Forbid()' in export,
    'employee target label removed from attachment selector': 'Карточка сотрудника\' }' not in js and "const options = [];" in js,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items(): print(('PASS' if ok else 'FAIL') + ': ' + name)
print(f'\n{len(checks)-len(failed)}/{len(checks)} static checks passed')
if failed: raise SystemExit(1)
