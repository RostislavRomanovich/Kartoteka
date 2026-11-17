[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$InstallDirectory,
 [Parameter(Mandatory=$true)][string]$PayloadDirectory,
 [Parameter(Mandatory=$true)][string]$ServerDnsName,
 [int]$Port=8443,
 [string]$LogPath = ''
)
$ErrorActionPreference='Stop'
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $env:ProgramData 'Kartoteka\Logs\server-install.log'
}
$script:InstallLogPath = $LogPath
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $script:InstallLogPath) -Force | Out-Null
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Запуск сценария установки сервера." | Set-Content -LiteralPath $script:InstallLogPath -Encoding UTF8
} catch {
    # Если основной каталог журналов недоступен, используем временный журнал.
    $script:InstallLogPath = Join-Path $env:TEMP 'Kartoteka-server-install.log'
}
function Write-KartotekaLog([string]$Message) {
    try { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" | Add-Content -LiteralPath $script:InstallLogPath -Encoding UTF8 } catch {}
}
trap {
    $errorText = $_.Exception.Message
    Write-KartotekaLog "ОШИБКА: $errorText"
    Write-KartotekaLog ("Позиция: " + $_.InvocationInfo.PositionMessage)
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        [System.Windows.Forms.MessageBox]::Show(
            "Настройка сервера Картотеки не завершена.`r`n`r`n$errorText`r`n`r`nЖурнал: $script:InstallLogPath",
            'Картотека — ошибка установки',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    } catch {}
    exit 1
}
function Read-KartotekaInput {
    param(
        [Parameter(Mandatory=$true)][string]$Title,
        [Parameter(Mandatory=$true)][string]$Prompt,
        [switch]$Password,
        [string]$DefaultValue = ''
    )
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $Title
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ShowInTaskbar = $true
    $form.Width = 560
    $form.Height = 190
    $label = New-Object System.Windows.Forms.Label
    $label.Left = 16; $label.Top = 16; $label.Width = 510; $label.Height = 42
    $label.Text = $Prompt
    $textBox = New-Object System.Windows.Forms.TextBox
    $textBox.Left = 16; $textBox.Top = 64; $textBox.Width = 510
    $textBox.Text = $DefaultValue
    if ($Password) { $textBox.UseSystemPasswordChar = $true }
    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'Продолжить'; $ok.Left = 330; $ok.Top = 105; $ok.Width = 95; $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Отмена'; $cancel.Left = 431; $cancel.Top = 105; $cancel.Width = 95; $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.AddRange(@($label,$textBox,$ok,$cancel))
    $form.AcceptButton = $ok; $form.CancelButton = $cancel
    $form.ActiveControl = $textBox
    try {
        if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Настройка отменена пользователем.' }
        return [string]$textBox.Text
    } finally { $form.Dispose() }
}
function New-RandomSecret([int]$Bytes=32) {
    $b = New-Object byte[] $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($b)
    return [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_')
}
function Quote-PgLiteral([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }
if ($ServerDnsName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,252}[A-Za-z0-9]$' -and $ServerDnsName -notmatch '^[A-Za-z0-9]$') { throw 'Недопустимое DNS-имя сервера.' }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Порт должен быть 1024–65535.' }
Write-KartotekaLog "Проверка имени сервера '$ServerDnsName' и порта $Port."
# Do not install over an existing PostgreSQL service or occupied database port.
$existingPgServices = @(Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue)
if ($existingPgServices.Count -gt 0) {
    throw 'Обнаружена существующая служба PostgreSQL. Установка остановлена, чтобы не затронуть существующие данные.'
}

$portListener = Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue
if ($portListener) {
    throw 'Порт 5432 уже занят. Установка остановлена, чтобы не затронуть существующую службу базы данных.'
}
Write-KartotekaLog "Проверка установочного пакета PostgreSQL в '$PayloadDirectory'."
if (-not (Test-Path -LiteralPath $PayloadDirectory -PathType Container)) { throw "Не найдена папка установщика PostgreSQL: $PayloadDirectory" }
$pgInstaller=Get-ChildItem -LiteralPath $PayloadDirectory -Filter 'postgresql-*-windows-x64.exe' -File | Select-Object -First 1
if (-not $pgInstaller) { throw 'В установочном пакете нет PostgreSQL. Установка остановлена.' }
if (-not (Test-Path (Join-Path $InstallDirectory 'Kartoteka.Server.exe'))) { throw 'Не найдена опубликованная сборка Kartoteka.Server.exe.' }

Write-KartotekaLog 'Проверки пройдены; запрашивается пароль PostgreSQL через диалоговое окно.'
$pgPassword = Read-KartotekaInput -Title 'Картотека — пароль PostgreSQL' -Prompt 'Придумайте пароль суперпользователя PostgreSQL (не менее 16 символов). Не используйте пробелы или двойные кавычки. Сохраните пароль в менеджере паролей администратора.' -Password
if ($pgPassword.Length -lt 16 -or $pgPassword -match '[\s"]') { throw 'Пароль PostgreSQL должен содержать не менее 16 символов и не должен содержать пробелы или двойные кавычки.' }

# The PostgreSQL EDB installer has unattended switches. The exact package version is pinned by the payload filename and must be tested before release.
$pgRoot=Join-Path $env:ProgramFiles 'PostgreSQL'
$pgVersion=if ($pgInstaller.BaseName -match 'postgresql-(\d+)') { $Matches[1] } else { throw 'Имя установщика должно включать версию, например postgresql-17-...-windows-x64.exe.' }
$pgInstall=Join-Path $pgRoot $pgVersion
$pgArgs=@('--mode','unattended','--unattendedmodeui','none','--prefix',$pgInstall,'--datadir',(Join-Path $env:ProgramData "Kartoteka\PostgreSQL\data"),'--serverport','5432','--servicename',"postgresql-x64-$pgVersion",'--superaccount','postgres','--superpassword',$pgPassword)
Write-KartotekaLog "Запуск PostgreSQL installer: $($pgInstaller.Name)."
$proc=Start-Process -FilePath $pgInstaller.FullName -ArgumentList $pgArgs -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "Установщик PostgreSQL завершился с кодом $($proc.ExitCode)." }
Write-KartotekaLog 'Установщик PostgreSQL завершился успешно.'
$psql=Join-Path $pgInstall 'bin\psql.exe'
if (-not (Test-Path $psql)) { throw "После установки не найден psql.exe: $psql" }
$env:PGPASSWORD=$pgPassword
try {
    # Wait for PostgreSQL to accept local connections; installer process can exit just before service is ready.
    $pgReady = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & $psql -h 127.0.0.1 -U postgres -d postgres -tAc 'SELECT 1' *> $null
        if ($LASTEXITCODE -eq 0) { $pgReady = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $pgReady) { throw 'PostgreSQL не начал принимать подключения в течение 60 секунд.' }
    Write-KartotekaLog 'PostgreSQL принимает локальные подключения.'

    $exists = & $psql -h 127.0.0.1 -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='kartoteka'"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось проверить наличие базы kartoteka.' }
    if ($exists.Trim() -eq '1') {
        throw 'База kartoteka уже существует. Установка остановлена, чтобы не изменять существующие данные. Используйте отдельную процедуру обновления/восстановления.'
    }

    $roleExists = & $psql -h 127.0.0.1 -U postgres -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='kartoteka_app'"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось проверить наличие роли приложения PostgreSQL.' }
    if ($roleExists.Trim() -eq '1') {
        throw 'Роль kartoteka_app уже существует. Установка остановлена, чтобы не менять существующие права и пароли.'
    }

    $dbPassword = New-RandomSecret 36
    $roleSql = "CREATE ROLE kartoteka_app LOGIN PASSWORD " + (Quote-PgLiteral $dbPassword)
    & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c $roleSql
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать роль приложения PostgreSQL.' }

    & $psql -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE kartoteka OWNER kartoteka_app ENCODING 'UTF8'"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать базу kartoteka.' }
    Write-KartotekaLog 'Роль приложения и новая база данных созданы.'
} finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }

$adminEmail = Read-KartotekaInput -Title 'Картотека — первый администратор' -Prompt 'Введите email первого администратора.'
$adminPassword = Read-KartotekaInput -Title 'Картотека — пароль администратора' -Prompt 'Придумайте пароль не короче 12 символов: верхний и нижний регистр, цифра и спецсимвол. Пробелы не допускаются.' -Password
if ($adminEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Email администратора указан неверно.' }
if ($adminPassword.Length -lt 12 -or $adminPassword -match '\s' -or $adminPassword -notmatch '[A-ZА-Я]' -or $adminPassword -notmatch '[a-zа-я]' -or $adminPassword -notmatch '\d' -or $adminPassword -notmatch '[^A-Za-zА-Яа-я0-9]') { throw 'Пароль администратора не соответствует требованиям: 12+ символов, верхний/нижний регистр, цифра, спецсимвол, без пробелов.' }
Write-KartotekaLog 'Email и пароль администратора введены; создаётся сертификат и настраивается служба.'

$dataRoot=Join-Path $env:ProgramData 'Kartoteka'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$cert=New-SelfSignedCertificate -DnsName $ServerDnsName -CertStoreLocation 'Cert:\LocalMachine\My' -FriendlyName 'Картотека — локальный сервер' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3) -KeyExportPolicy Exportable
$pfxPath=Join-Path $dataRoot 'kartoteka-server.pfx'
$cerPath=Join-Path $dataRoot 'kartoteka-server.cer'
$pfxPassword=New-RandomSecret 36
$securePfx=ConvertTo-SecureString $pfxPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePfx | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

# Build a correctly escaped Npgsql connection string; do not print secrets to the console.
$dbConnection="Host=127.0.0.1;Port=5432;Database=kartoteka;Username=kartoteka_app;Password=$dbPassword;Pooling=true;Timeout=15;Command Timeout=30;SSL Mode=Prefer"
$serviceScript=Join-Path $PSScriptRoot 'Install-KartotekaServerService.ps1'
$adminSecure2=ConvertTo-SecureString $adminPassword -AsPlainText -Force
try {
  & $serviceScript -InstallDirectory $InstallDirectory -DatabaseConnection $dbConnection -CertificatePath $pfxPath -CertificatePassword $pfxPassword -ServerDnsName $ServerDnsName -AdminEmail $adminEmail -AdminPassword $adminSecure2 -Port $Port -InitializeEmptyDatabase -ErrorAction Stop
  if (-not $?) {
    throw 'Сценарий установки службы завершился с ошибкой.'
  }
} catch {
  throw "Не удалось установить службу Картотеки: $($_.Exception.Message)"
}

# Keep public trust certificate and a short handoff note in a separate, administrator-only data folder.
$note=Join-Path $dataRoot 'ПОДКЛЮЧЕНИЕ.txt'
@("Адрес картотеки: https://$ServerDnsName`:$Port/", "Публичный сертификат для доверенной установки на рабочих компьютерах: $cerPath", 'Передайте файл .cer на рабочие компьютеры защищённым способом. Не передавайте .pfx и пароли.') | Set-Content -LiteralPath $note -Encoding UTF8
Write-KartotekaLog 'Служба успешно установлена и прошла проверку готовности.'
# PostgreSQL installer is large; remove its copied payload only after complete success. Keep it on failure for diagnosis/retry.
if (Test-Path -LiteralPath $PayloadDirectory -PathType Container) { Remove-Item -LiteralPath $PayloadDirectory -Recurse -Force -ErrorAction SilentlyContinue }
Write-KartotekaLog 'Установочный payload удалён после успешной настройки.'
Write-Host "Сервер установлен. Адрес: https://$ServerDnsName`:$Port/" -ForegroundColor Green
Write-Host "Публичный сертификат: $cerPath" -ForegroundColor Yellow
Write-Host 'Сохраните пароли администратора и PostgreSQL в менеджере паролей.' -ForegroundColor Yellow
$pgPassword=$null; $dbPassword=$null; $adminPassword=$null; $pfxPassword=$null; $dbConnection=$null; $pgArgs=$null
