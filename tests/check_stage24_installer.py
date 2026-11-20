from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
csproj = (ROOT / 'Kartoteka.Server.csproj').read_text(encoding='utf-8')
program = (ROOT / 'Program.cs').read_text(encoding='utf-8')
server = (ROOT / 'installer/scripts/Install-KartotekaServerService.ps1').read_text(encoding='utf-8')
client = (ROOT / 'installer/scripts/Install-KartotekaWorkstation.ps1').read_text(encoding='utf-8')

checks = {
    'Windows Service hosting package': 'Microsoft.Extensions.Hosting.WindowsServices' in csproj,
    'UseWindowsService enabled': 'UseWindowsService(' in program,
    'database creation requires explicit flag': 'KARTOTEKA_INITIALIZE_DATABASE' in program and 'EnsureCreatedAsync' in program,
    'bootstrap secrets are not hard-coded': 'RandomNumberGenerator]::Fill' in server and 'KARTOTEKA_JWT_SIGNING_KEY=$jwtKey' in server,
    'server requires TLS certificate': 'CertificatePath' in server and 'CertificatePassword' in server,
    'server waits for health endpoint': '/health' in server and 'Start-Service' in server,
    'one-time bootstrap vars removed': 'KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD=' in server and 'KARTOTEKA_INITIALIZE_DATABASE=true' in server and 'Set-ItemProperty' in server,
    'workstation enforces HTTPS': "$uri.Scheme -ne 'https'" in client,
    'workstation checks health': '/health' in client,
    'workstation refuses private-key cert import': 'HasPrivateKey' in client,
    'workstation creates shortcuts': 'Start Menu' in client and 'Desktop' in client,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
print(f'{len(checks)-len(failed)}/{len(checks)} static checks passed')
if failed:
    raise SystemExit(1)
