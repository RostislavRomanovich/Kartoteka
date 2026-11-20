
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerUrl,
    [Parameter(Mandatory=$true)][string]$ServerDnsName,
    [string]$CertificateFile,
    [switch]$TrustCertificate,
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($LogPath)) { $LogPath = Join-Path $env:ProgramData 'Kartoteka\Logs\workstation-install.log' }
$script:InstallLogPath = $LogPath
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $script:InstallLogPath) -Force | Out-Null
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Запуск настройки рабочего места." | Set-Content -LiteralPath $script:InstallLogPath -Encoding UTF8
} catch { $script:InstallLogPath = Join-Path $env:TEMP 'Kartoteka-workstation-install.log' }
function Write-KartotekaLog([string]$Message) { try { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" | Add-Content -LiteralPath $script:InstallLogPath -Encoding UTF8 } catch {} }
trap {
    $errorText = $_.Exception.Message
    Write-KartotekaLog "ОШИБКА: $errorText"
    Write-KartotekaLog ("Позиция: " + $_.InvocationInfo.PositionMessage)
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        [System.Windows.Forms.MessageBox]::Show("Настройка рабочего места не завершена.`r`n`r`n$errorText`r`n`r`nЖурнал: $script:InstallLogPath", 'Картотека — ошибка установки', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    } catch {}
    exit 1
}

$uri = $null
if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -ne 'https' -or
    $uri.UserInfo -ne '' -or
    $uri.AbsolutePath -ne '/' -or
    $uri.Query -ne '' -or
    $uri.Fragment -ne '') {
    throw 'Укажите HTTPS-адрес сервера без пути и дополнительных параметров.'
}

if ($uri.Host -ine $ServerDnsName) {
    throw 'Имя сервера в URL должно совпадать с DNS-именем сертификата.'
}

if (-not $CertificateFile) {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = 'Выберите публичный сертификат сервера Картотеки (.cer)'
    $dialog.Filter = 'Сертификат X.509 (*.cer)|*.cer'
    try { if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Выбор сертификата отменён.' }; $CertificateFile = $dialog.FileName } finally { $dialog.Dispose() }
}

if (-not (Test-Path -LiteralPath $CertificateFile -PathType Leaf)) {
    throw "Файл сертификата не найден: $CertificateFile"
}

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    (Resolve-Path -LiteralPath $CertificateFile).Path
)

if ($cert.HasPrivateKey) {
    throw 'Выбран сертификат с закрытым ключом. Передавайте только публичный файл .cer.'
}

if ($cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)) {
    throw 'Сертификат ещё не действует или срок его действия истёк.'
}

$certDnsName = $cert.GetNameInfo(
    [System.Security.Cryptography.X509Certificates.X509NameType]::DnsName,
    $false
)

if ($certDnsName -ine $ServerDnsName) {
    throw "Имя сертификата '$certDnsName' не совпадает с именем сервера '$ServerDnsName'."
}

Write-KartotekaLog "Сертификат прочитан; отпечаток: $($cert.Thumbprint)."

function Confirm-KartotekaCertificate {
    param([Parameter(Mandatory=$true)][string]$DnsName, [Parameter(Mandatory=$true)][string]$Thumbprint)
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Картотека — проверка сертификата сервера'
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ShowInTaskbar = $true
    $form.Width = 700
    $form.Height = 275

    $label = New-Object System.Windows.Forms.Label
    $label.Left = 16; $label.Top = 15; $label.Width = 650; $label.Height = 55
    $label.Text = "Сервер: $DnsName`r`nСверьте отпечаток ниже с отпечатком на серверном компьютере по независимому каналу связи. Не подтверждайте сертификат, если значения отличаются."

    $value = New-Object System.Windows.Forms.TextBox
    $value.Left = 16; $value.Top = 78; $value.Width = 650; $value.Height = 28
    $value.ReadOnly = $true
    $value.Font = New-Object System.Drawing.Font('Consolas', 11)
    $value.Text = $Thumbprint
    $value.SelectionStart = 0; $value.SelectionLength = $value.Text.Length

    $copy = New-Object System.Windows.Forms.Button
    $copy.Text = 'Копировать отпечаток'; $copy.Left = 16; $copy.Top = 118; $copy.Width = 180
    $copy.Add_Click({ [System.Windows.Forms.Clipboard]::SetText($value.Text) })

    $yes = New-Object System.Windows.Forms.Button
    $yes.Text = 'Да, отпечаток совпадает'; $yes.Left = 300; $yes.Top = 180; $yes.Width = 205
    $yes.DialogResult = [System.Windows.Forms.DialogResult]::Yes

    $no = New-Object System.Windows.Forms.Button
    $no.Text = 'Отмена'; $no.Left = 515; $no.Top = 180; $no.Width = 150
    $no.DialogResult = [System.Windows.Forms.DialogResult]::No

    $form.Controls.AddRange(@($label,$value,$copy,$yes,$no))
    $form.AcceptButton = $no
    $form.CancelButton = $no
    try { return ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::Yes) }
    finally { $form.Dispose() }
}

$rootStore = 'Cert:\LocalMachine\Root'
$thumbprint = $cert.Thumbprint.Replace(' ', '').ToUpperInvariant()
$alreadyTrusted = @(Get-ChildItem -LiteralPath $rootStore | Where-Object { $_.Thumbprint.Replace(' ', '').ToUpperInvariant() -eq $thumbprint }).Count -gt 0
$importedByThisRun = $false

try {
    if ($TrustCertificate) {
        if (-not (Confirm-KartotekaCertificate -DnsName $ServerDnsName -Thumbprint $cert.Thumbprint)) {
            throw 'Пользователь не подтвердил совпадение отпечатка сертификата. Доверие не установлено.'
        }
        if (-not $alreadyTrusted) {
            Import-Certificate -FilePath (Resolve-Path -LiteralPath $CertificateFile).Path `
                -CertStoreLocation $rootStore | Out-Null
            $importedByThisRun = $true
            Write-KartotekaLog "Сертификат $thumbprint добавлен в LocalMachine\Root."
        } else {
            Write-KartotekaLog "Сертификат $thumbprint уже был доверенным; существующая запись сохранена."
        }
    }

    $health = [Uri]::new($uri, '/health')
    $response = Invoke-WebRequest -Uri $health.AbsoluteUri -TimeoutSec 8 -UseBasicParsing
    if ($response.StatusCode -ne 200) {
        throw "Сервер ответил кодом $($response.StatusCode), ожидался 200."
    }
} catch {
    if ($importedByThisRun) {
        Remove-Item -LiteralPath (Join-Path $rootStore $thumbprint) -Force -ErrorAction SilentlyContinue
        Write-KartotekaLog "Сертификат $thumbprint удалён из LocalMachine\Root после неудачной проверки соединения."
    }
    throw
}

$shortcutRoot = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Картотека'
New-Item -ItemType Directory -Path $shortcutRoot -Force | Out-Null

$shortcutPath = Join-Path $shortcutRoot 'Картотека.url'
@('[InternetShortcut]', "URL=$($uri.AbsoluteUri)", 'IconIndex=0') |
    Set-Content -LiteralPath $shortcutPath -Encoding ASCII

$desktopPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Картотека.url'
@('[InternetShortcut]', "URL=$($uri.AbsoluteUri)", 'IconIndex=0') |
    Set-Content -LiteralPath $desktopPath -Encoding ASCII

Write-KartotekaLog "Рабочее место настроено: $($uri.AbsoluteUri)."
Write-Host "Рабочее место настроено: $($uri.AbsoluteUri)" -ForegroundColor Green
