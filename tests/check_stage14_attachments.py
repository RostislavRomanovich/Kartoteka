from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
entities = (ROOT / "Models/Entities.cs").read_text(encoding="utf-8")
dtos = (ROOT / "Contracts/EmployeeDtos.cs").read_text(encoding="utf-8")
controller = (ROOT / "Controllers/AttachmentsController.cs").read_text(encoding="utf-8")
context = (ROOT / "Data/AppDbContext.cs").read_text(encoding="utf-8")
html = (ROOT / "wwwroot/index.html").read_text(encoding="utf-8")
js = (ROOT / "wwwroot/app.js").read_text(encoding="utf-8")
docs = (ROOT / "docs/STAGE14_DOCUMENT_GROUP_ATTACHMENTS.md").read_text(encoding="utf-8")

checks = {
    "passport fields in entity": all(x in entities for x in ["PassportSeries", "PassportNumber", "PassportIssuedBy", "PassportIssuedOn"]),
    "passport fields in API contract": all(x in dtos for x in ["PassportSeries", "PassportNumber", "PassportIssuedBy", "PassportIssuedOn"]),
    "passport fields in UI": all(f'name="{x}"' in html for x in ["passportSeries", "passportNumber", "passportIssuedBy", "passportIssuedOn"]),
    "date picker includes passport issue date": "passportIssuedOn" in js and "dateFields" in js,
    "group-level attachment button": "Добавить скан" in js and "attachmentGroups" in js,
    "passport is a single document group": "'passport'" in js and "'Паспорт'" in js,
    "all groups are allow-listed on server": all(f'["{x}"]' in controller for x in ["passport", "military_id", "access", "contract", "driving_license", "car_registration", "birth_certificate", "award", "personal_feature", "relative", "other"]),
    "attachment endpoint admin-only": '[Authorize(Policy = "AdminOnly")]' in controller,
    "file size limit": "10 * 1024 * 1024" in controller,
    "signature validation": "DetectSafeContentType" in controller and "application/pdf" in controller and "image/png" in controller,
    "duplicate hash check": "a.Sha256 == hash" in controller,
    "database uniqueness protects concurrent duplicate uploads": "new { x.EmployeeId, x.Category, x.TargetType, x.TargetId, x.Sha256 }).IsUnique()" in context,
    "safe filename extraction": "Path.GetFileName(file.FileName)" in controller,
    "no sniff header": 'X-Content-Type-Options' in controller,
    "download and delete actions exist": '[HttpGet("{attachmentId:long}")]' in controller and '[HttpDelete("{attachmentId:long}")]' in controller,
    "migration instructions included": "Stage14PassportAndAttachmentGroups" in docs,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(("PASS" if ok else "FAIL") + " - " + name)
print(f"\n{len(checks)-len(failed)}/{len(checks)} static checks passed")
if failed:
    raise SystemExit(1)
