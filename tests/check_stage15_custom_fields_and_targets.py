from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
entities = (ROOT / "Models/Entities.cs").read_text(encoding="utf-8")
context = (ROOT / "Data/AppDbContext.cs").read_text(encoding="utf-8")
custom = (ROOT / "Controllers/CustomFieldsController.cs").read_text(encoding="utf-8")
attachments = (ROOT / "Controllers/AttachmentsController.cs").read_text(encoding="utf-8")
html = (ROOT / "wwwroot/index.html").read_text(encoding="utf-8")
js = (ROOT / "wwwroot/app.js").read_text(encoding="utf-8")
docs = (ROOT / "docs/STAGE15_CUSTOM_FIELDS_AND_TARGETED_ATTACHMENTS.md").read_text(encoding="utf-8")

checks = {
    "custom field definitions controller exists": "class CustomFieldsController" in custom,
    "definitions restricted to admin for mutations": '[Authorize(Policy = "AdminOnly")]' in custom,
    "employee custom field values GET/PUT routes": '"/api/employees/{employeeId:long}/custom-fields"' in custom,
    "relative custom field values GET/PUT routes": '"/api/employees/{employeeId:long}/relatives/{relativeId:long}/custom-fields"' in custom,
    "custom field scope and data types validated": "ValidateDefinition" in custom and '"employee", "relative"' in custom and '"text", "date", "number", "boolean", "select"' in custom,
    "custom field values validated and duplicate ids rejected": "ValidateValues" in custom and "Distinct().Count()" in custom,
    "custom field values unique per employee and field": "new { x.EmployeeId, x.FieldDefinitionId }).IsUnique()" in context,
    "custom field values unique per relative and field": "new { x.RelativeId, x.FieldDefinitionId }).IsUnique()" in context,
    "field deactivation preserves historical values": "item.IsActive = false" in custom,
    "employee custom fields rendered in editor": 'id="custom-fields-editor"' in html and "loadCustomFieldEditor" in js,
    "admin has custom field management UI": 'id="manage-custom-fields-button"' in html and "createCustomFieldDefinition" in js,
    "relative custom field UI exists": 'id="relative-custom-fields"' in html and "relative-custom-fields" in js,
    "attachments store target type and id": "TargetType" in entities and "TargetId" in entities,
    "attachment duplicate key includes target record": "new { x.EmployeeId, x.Category, x.TargetType, x.TargetId, x.Sha256 }).IsUnique()" in context,
    "attachment target must belong to same employee": "TargetBelongsToEmployee" in attachments and "x.EmployeeId == employeeId" in attachments,
    "attachment list returns target identity": "a.TargetType, a.TargetId" in attachments,
    "UI can select a related record as attachment target": "attachmentTargetOptions" in js and "targetType=${encodeURIComponent(targetType)}" in js,
    "migration instructions included": "Stage15CustomFieldsAndAttachmentTargets" in docs,
    "no HTML injection for custom field labels": "text('label'" in js and "innerHTML" not in js,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(("PASS" if ok else "FAIL") + " - " + name)
print(f"\n{len(checks)-len(failed)}/{len(checks)} static checks passed")
if failed:
    raise SystemExit(1)
