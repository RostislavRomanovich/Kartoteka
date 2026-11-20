from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
iss = (ROOT / 'installer/KartotekaSetup.iss').read_text(encoding='utf-8')
server = (ROOT / 'installer/scripts/Install-KartotekaServer.ps1').read_text(encoding='utf-8')
client = (ROOT / 'installer/scripts/Install-KartotekaWorkstation.ps1').read_text(encoding='utf-8')
checks = {
    'single installer has server and workstation modes': 'Сервер — программа' in iss and 'Рабочий компьютер' in iss,
    'server payload is gated by mode': 'Check: IsServerInstall' in iss,
    'workstation does not install PostgreSQL': 'postgresql-*-windows-x64.exe' in iss and 'Check: IsServerInstall' in iss,
    'installer fails closed when published exe absent': 'Kartoteka.Server.exe' in iss and 'dotnet publish' in iss,
    'installer fails closed when PostgreSQL payload absent': 'не найден официальный установщик PostgreSQL' in iss,
    'server script installs PostgreSQL unattended': '--mode' in server and 'unattended' in server and 'Start-Process' in server,
    'database role and database provisioned': 'CREATE ROLE kartoteka_app' in server and "CREATE DATABASE kartoteka" in server,
    'server TLS certificate generated': 'New-SelfSignedCertificate' in server and 'Export-Certificate' in server,
    'admin credentials collected interactively': 'Read-Host' in server and 'KARTOTEKA_BOOTSTRAP_ADMIN' not in server,
    'interactive installer scripts are not hidden': 'Flags: waituntilterminated; Check: IsServerInstall' in iss and 'runhidden' not in iss,
    'existing database is never silently adopted or altered': 'Установка остановлена, чтобы не изменять существующие данные' in server and 'ALTER DATABASE' not in server,
    'workstation validates HTTPS': "$uri.Scheme -ne 'https'" in client,
    'workstation validates server health': '/health' in client,
    'release caveats documented': 'не готовый EXE' in (ROOT/'installer/STAGE25_STATUS.md').read_text(encoding='utf-8'),
}
failed=[name for name, ok in checks.items() if not ok]
for name, ok in checks.items(): print(('PASS' if ok else 'FAIL')+': '+name)
print(f'{len(checks)-len(failed)}/{len(checks)} static checks passed')
if failed: raise SystemExit(1)
