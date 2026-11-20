from pathlib import Path
root = Path(__file__).resolve().parents[1]
controller = (root / 'Controllers' / 'AuditController.cs').read_text(encoding='utf-8')
html = (root / 'wwwroot' / 'index.html').read_text(encoding='utf-8')
js = (root / 'wwwroot' / 'app.js').read_text(encoding='utf-8')
assert '[Authorize(Policy = "AdminOnly")]' in controller
assert '[Route("api/admin/audit")]' in controller
assert 'OrderByDescending(x => x.OccurredUtc)' in controller
assert 'Math.Clamp(pageSize, 1, 200)' in controller
assert 'total, items' in controller
assert 'audit-log-button' in html and 'audit-log-dialog' in html
assert 'loadAuditLog' in js and '/api/admin/audit?' in js
assert "roles.includes('admin')" in js
assert 'textContent' in js
print('Stage 16 static audit checks passed (9 assertions).')
