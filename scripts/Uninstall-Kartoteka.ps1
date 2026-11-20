[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
$logRoot = Join-Path $env:ProgramData 'Kartoteka\Logs'
try { New-Item -ItemType Directory -Path $logRoot -Force | Out-Null } catch {}
$logPath = Join-Path $logRoot 'uninstall.log'
function Write-UninstallLog([string]$Message) {
    try { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" | Add-Content -LiteralPath $logPath -Encoding UTF8 } catch {}
}

# The mode marker is written by Inno Setup and is still present while [UninstallRun] executes.
# A workstation uninstall must never stop/remove a server installed on the same computer.
$installMode = $null
try { $installMode = (Get-ItemProperty -LiteralPath 'HKLM:\Software\Kartoteka' -Name InstallMode -ErrorAction Stop).InstallMode } catch {}

if ($installMode -eq 'server') {
    # Remove only the application-owned Windows service and firewall rule.
    # PostgreSQL, its data directory, Kartoteka database, ProgramData files, and certificates are retained.
    $serviceName = 'KartotekaServer'
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        try {
            if ($service.Status -ne 'Stopped') {
                Stop-Service -Name $serviceName -Force -ErrorAction Stop
                $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
            }
        } catch { Write-UninstallLog "Не удалось штатно остановить службу: $($_.Exception.Message)" }

        & "$env:SystemRoot\System32\sc.exe" delete $serviceName | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-UninstallLog 'Служба KartotekaServer удалена.' }
        else { Write-UninstallLog "Команда удаления службы вернула код $LASTEXITCODE." }
    }

    try {
        $rule = Get-NetFirewallRule -Name 'KartotekaServerHTTPS' -ErrorAction SilentlyContinue
        if ($rule) {
            $rule | Remove-NetFirewallRule -ErrorAction Stop
            Write-UninstallLog 'Правило HTTPS-брандмауэра удалено.'
        }
    } catch { Write-UninstallLog "Не удалось удалить правило брандмауэра: $($_.Exception.Message)" }
} elseif ($installMode -eq 'workstation') {
    Write-UninstallLog 'Режим рабочего места: серверная служба и правило брандмауэра не затрагиваются.'
} else {
    Write-UninstallLog 'Маркер режима установки не найден; серверная служба и правило брандмауэра оставлены без изменений.'
}

$shortcutPaths = @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Картотека\Картотека.url'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Картотека.url')
)
foreach ($path in $shortcutPaths) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        try { Remove-Item -LiteralPath $path -Force -ErrorAction Stop; Write-UninstallLog "Ярлык удалён: $path" }
        catch { Write-UninstallLog "Не удалось удалить ярлык $path : $($_.Exception.Message)" }
    }
}

$shortcutDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Картотека'
if (Test-Path -LiteralPath $shortcutDirectory -PathType Container) {
    try { Remove-Item -LiteralPath $shortcutDirectory -Force -ErrorAction SilentlyContinue } catch {}
}
Write-UninstallLog 'Удаление приложения завершено. PostgreSQL, база данных, файлы ProgramData и сертификаты сохранены.'
exit 0
