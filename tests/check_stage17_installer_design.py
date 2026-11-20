from pathlib import Path

root = Path(__file__).resolve().parents[1]
iss = (root / 'installer' / 'KartotekaSetup.prototype.iss').read_text(encoding='utf-8')
readme = (root / 'installer' / 'README.md').read_text(encoding='utf-8')
status = (root / 'STAGE17_STATUS.txt').read_text(encoding='utf-8')

checks = {
    'server mode': "Сервер — приложение" in iss,
    'workstation mode': "Рабочий компьютер" in iss,
    'https address field': 'https://' in iss,
    'prototype blocked': 'PrepareToInstall' in iss and 'Установка остановлена' in iss,
    'shared web architecture documented': 'отдаёт сервер' in readme,
    'release prerequisites documented': 'Условия, необходимые до выпуска' in readme,
    'limitations documented': 'Не сделано и не заявляется как готовое' in status,
}
for name, passed in checks.items():
    print(('PASS' if passed else 'FAIL') + ': ' + name)
if not all(checks.values()):
    raise SystemExit(1)
print(f'{sum(checks.values())}/{len(checks)} checks passed')
