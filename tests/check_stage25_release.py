from pathlib import Path
import re
ROOT = Path(__file__).resolve().parents[1]
iss = (ROOT / 'installer/KartotekaSetup.iss').read_text(encoding='utf-8-sig')
server = (ROOT / 'installer/scripts/Install-KartotekaServer.ps1').read_text(encoding='utf-8-sig')
service = (ROOT / 'installer/scripts/Install-KartotekaServerService.ps1').read_text(encoding='utf-8-sig')
client = (ROOT / 'installer/scripts/Install-KartotekaWorkstation.ps1').read_text(encoding='utf-8-sig')
uninstall = (ROOT / 'installer/scripts/Uninstall-Kartoteka.ps1').read_text(encoding='utf-8-sig')
checks = {
    'server and workstation modes exist': 'Сервер — программа' in iss and 'Рабочий компьютер' in iss,
    'PostgreSQL payload stays in application installer folder': 'DestDir: "{app}\\installer\\payload"' in iss and 'deleteafterinstall' not in iss.lower(),
    'server script and service script are included': 'Install-KartotekaServer.ps1' in iss and 'Install-KartotekaServerService.ps1' in iss,
    'uninstaller script is included and invoked': 'Uninstall-Kartoteka.ps1' in iss and '[UninstallRun]' in iss,
    'installer records server/workstation mode for safe uninstall': '[Registry]' in iss and 'ValueData: "server"' in iss and 'ValueData: "workstation"' in iss,
    'only one ShouldSkipPage implementation exists': len(re.findall(r'function\s+ShouldSkipPage\s*\(', iss, re.I)) == 1,
    'failed setup skips success page': 'InstallationFailed and (PageID = wpFinished)' in iss,
    'custom installer exit code reports failure': 'GetCustomSetupExitCode' in iss and 'Result := 1' in iss,
    'server installation refuses existing PostgreSQL services': "Get-Service -Name 'postgresql*'" in server and 'Установка остановлена' in server,
    'server installation refuses existing database and role': 'pg_database' in server and 'pg_roles' in server and 'База kartoteka уже существует' in server,
    'database password is passed to psql via temporary environment variable': '$env:PGPASSWORD=$pgPassword' in server and 'Remove-Item Env:\\PGPASSWORD' in server,
    'service stores secrets under restricted service registry ACL': 'SetAccessRuleProtection($true, $false)' in service and 'RegistryAccessRule' in service,
    'HTTPS firewall is restricted to local subnet and private/domain profiles': 'New-NetFirewallRule' in service and '-Profile Domain,Private' in service and '-RemoteAddress LocalSubnet' in service,
    'firewall never opens PostgreSQL port': not re.search(r'New-NetFirewallRule[^\n]*5432', service),
    'service and firewall are rolled back on service-script failure': '$serviceCreated' in service and 'sc.exe" delete $serviceName' in service and '$firewallRuleCreated' in service,
    'workstation displays exact thumbprint in a visible form': 'Confirm-KartotekaCertificate' in client and '$value.Text = $Thumbprint' in client,
    'workstation supports copying thumbprint': 'Копировать отпечаток' in client and 'Clipboard]::SetText' in client,
    'workstation only trusts after explicit confirmation': 'Да, отпечаток совпадает' in client and 'DialogResult]::Yes' in client,
    'workstation removes only newly imported certificate on failed health check': '$importedByThisRun' in client and 'Remove-Item -LiteralPath (Join-Path $rootStore $thumbprint)' in client,
    'workstation health request uses PowerShell 5.1 basic parsing': '-UseBasicParsing' in client,
    'uninstaller scopes server cleanup to server mode only': "$installMode -eq 'server'" in uninstall and "$installMode -eq 'workstation'" in uninstall and 'KartotekaServerHTTPS' in uninstall,
    'uninstaller removes application shortcuts': 'Картотека.url' in uninstall,
    'uninstaller explicitly preserves PostgreSQL and ProgramData': 'PostgreSQL' in uninstall and 'ProgramData' in uninstall and 'не удал' in uninstall.lower(),
    'no database drop or PostgreSQL data deletion in uninstall script': 'DROP DATABASE' not in uninstall.upper() and 'PostgreSQL\\data' not in uninstall,
    'server bootstrap prompts through GUI rather than Read-Host': 'Read-KartotekaInput' in server and 'Read-Host' not in server,
    'server installer does not ship default admin credentials': 'KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD=' not in server,
    'server install log exists': 'server-install.log' in server and 'Write-KartotekaLog' in server,
}
failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
print(f'{len(checks)-len(failed)}/{len(checks)} static checks passed')
if failed:
    raise SystemExit(1)
