[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [Parameter(Mandatory=$true)][string]$DatabaseConnection,
    [Parameter(Mandatory=$true)][string]$CertificatePath,
    [Parameter(Mandatory=$true)][string]$CertificatePassword,
    [Parameter(Mandatory=$true)][string]$ServerDnsName,
    [Parameter(Mandatory=$true)][string]$AdminEmail,
    [Parameter(Mandatory=$true)][securestring]$AdminPassword,
    [int]$Port = 8443,
    [switch]$InitializeEmptyDatabase
)

$ErrorActionPreference = 'Stop'
$serviceName = 'KartotekaServer'
$serviceDisplayName = 'Картотека — сервер'
$exe = Join-Path $InstallDirectory 'Kartoteka.Server.exe'
if (-not (Test-Path $exe)) { throw "Не найден опубликованный сервер: $exe" }
if (-not (Test-Path $CertificatePath)) { throw "Не найден сертификат TLS: $CertificatePath" }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Порт должен быть в диапазоне 1024–65535.' }
if ($ServerDnsName -notmatch '^[A-Za-z0-9.-]+$') { throw 'Имя сервера должно быть DNS-именем без протокола и порта.' }
if ([string]::IsNullOrWhiteSpace($DatabaseConnection)) { throw 'Строка подключения PostgreSQL не задана.' }
if ([string]::IsNullOrWhiteSpace($AdminEmail) -or $AdminEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Укажите корректный email первого администратора.' }

$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword)
try { $adminPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ($adminPasswordPlain.Length -lt 12 -or $adminPasswordPlain -notmatch '[A-ZА-Я]' -or $adminPasswordPlain -notmatch '[a-zа-я]' -or $adminPasswordPlain -notmatch '\d' -or $adminPasswordPlain -notmatch '[^A-Za-zА-Яа-я0-9]') {
    throw 'Пароль администратора должен быть не короче 12 символов и содержать верхний/нижний регистр, цифру и спецсимвол.'
}

# Keep mutable data outside Program Files. The installer never deletes this directory.
$dataRoot = Join-Path $env:ProgramData 'Kartoteka'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$acl = Get-Acl $dataRoot
$acl.SetAccessRuleProtection($true, $false)
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule('SYSTEM','FullControl','ContainerInherit,ObjectInherit','None','Allow')
$adminsRule = New-Object System.Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators','FullControl','ContainerInherit,ObjectInherit','None','Allow')
$acl.SetAccessRule($systemRule); $acl.SetAccessRule($adminsRule); Set-Acl -Path $dataRoot -AclObject $acl

# Validate the PFX password before registering the service.
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
$cert.Import($CertificatePath, $CertificatePassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet)
if (-not $cert.HasPrivateKey) { throw 'TLS-сертификат не содержит закрытого ключа.' }
if ($cert.NotAfter -le (Get-Date)) { throw 'Срок действия TLS-сертификата истёк.' }
if ($cert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false) -ne $ServerDnsName) {
    throw "Имя сертификата не совпадает с именем сервера $ServerDnsName. Подготовьте сертификат с соответствующим SAN/CN."
}

$jwtBytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Fill($jwtBytes)
$jwtKey = [Convert]::ToBase64String($jwtBytes)
$envLines = @(
    "KARTOTEKA_DB_CONNECTION=$DatabaseConnection",
    "KARTOTEKA_JWT_SIGNING_KEY=$jwtKey",
    "KARTOTEKA_BOOTSTRAP_ADMIN_EMAIL=$AdminEmail",
    "KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD=$adminPasswordPlain",
    "ASPNETCORE_ENVIRONMENT=Production",
    "ASPNETCORE_Kestrel__Endpoints__Https__Url=https://0.0.0.0:$Port",
    "ASPNETCORE_Kestrel__Endpoints__Https__Certificate__Path=$CertificatePath",
    "ASPNETCORE_Kestrel__Endpoints__Https__Certificate__Password=$CertificatePassword"
)
if ($InitializeEmptyDatabase) { $envLines += 'KARTOTEKA_INITIALIZE_DATABASE=true' }


$serviceCreated = $false
$firewallRuleCreated = $false
$firewallRuleName = 'KartotekaServerHTTPS'
try {
        $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if ($existing) {
        throw 'Служба KartotekaServer уже существует. Повторная установка остановлена, чтобы не нарушить работу действующего сервера. Для обновления потребуется отдельная процедура.'
    }
    New-Service -Name $serviceName -DisplayName $serviceDisplayName -BinaryPathName ('"' + $exe + '"') -StartupType Automatic -Description 'Сервер локальной сетевой картотеки сотрудников.' | Out-Null
    $serviceCreated = $true
    $serviceReg = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    New-ItemProperty -Path $serviceReg -Name Environment -PropertyType MultiString -Value $envLines -Force | Out-Null
    # Restrict the service registry key to SYSTEM and local administrators.
    $key = Get-Acl $serviceReg
    $key.SetAccessRuleProtection($true, $false)
    $key.SetOwner([System.Security.Principal.NTAccount]'BUILTIN\Administrators')
    $key.SetAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule('SYSTEM','FullControl','ContainerInherit','None','Allow')))
    $key.SetAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule('BUILTIN\Administrators','FullControl','ContainerInherit','None','Allow')))
    Set-Acl -Path $serviceReg -AclObject $key
    Start-Service -Name $serviceName

    
    # Проверка доступности HTTPS только с сертификатом, созданным для этой установки.
    function Test-KartotekaHealth {
        param(
            [Parameter(Mandatory=$true)][string]$Url,
            [Parameter(Mandatory=$true)][string]$ExpectedThumbprint,
            [int]$TimeoutSeconds = 90
        )

        $expected = $ExpectedThumbprint.Replace(' ', '').ToUpperInvariant()
        $handler = New-Object System.Net.Http.HttpClientHandler

        $validationCallback = {
            param($request, $serverCertificate, $chain, $sslPolicyErrors)

            if ($null -eq $serverCertificate) {
                return $false
            }

            $actual = ([System.Security.Cryptography.X509Certificates.X509Certificate2]$serverCertificate).Thumbprint
            if ([string]::IsNullOrWhiteSpace($actual)) {
                return $false
            }

            return ($actual.Replace(' ', '').ToUpperInvariant() -eq $expected)
        }.GetNewClosure()

        $handler.ServerCertificateCustomValidationCallback = $validationCallback
        $client = New-Object System.Net.Http.HttpClient($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(5)
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

        try {
            while ((Get-Date) -lt $deadline) {
                try {
                    $response = $client.GetAsync($Url).GetAwaiter().GetResult()
                    try {
                        if ([int]$response.StatusCode -eq 200) {
                            return $true
                        }
                    }
                    finally {
                        $response.Dispose()
                    }
                }
                catch {
                    # Служба ещё запускается; повторяем запрос.
                }

                Start-Sleep -Seconds 2
            }

            return $false
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }

    $healthUrl = "https://127.0.0.1:$Port/health"

    if (-not (Test-KartotekaHealth -Url $healthUrl -ExpectedThumbprint $cert.Thumbprint)) {
        $svc = Get-Service -Name $serviceName
        throw "Служба не прошла первую проверку /health. Статус: $($svc.Status). Проверьте журналы приложения и подключение к БД."
    }

    # Удаляем временные данные первоначальной настройки.
    $safeEnv = @(
        $envLines | Where-Object {
            $_ -notmatch '^KARTOTEKA_BOOTSTRAP_ADMIN_(EMAIL|PASSWORD)=' -and
            $_ -ne 'KARTOTEKA_INITIALIZE_DATABASE=true'
        }
    )

    Set-ItemProperty -Path $serviceReg -Name Environment -Value $safeEnv
    Restart-Service -Name $serviceName -Force

    # Проверяем, что приложение работает и после удаления временных параметров.
    if (-not (Test-KartotekaHealth -Url $healthUrl -ExpectedThumbprint $cert.Thumbprint)) {
        $svc = Get-Service -Name $serviceName
        throw "Служба не прошла проверку /health после перезапуска. Статус: $($svc.Status)."
    }

    # Expose only the HTTPS application port to the local subnet on Domain/Private profiles.
    # PostgreSQL (5432) is deliberately never opened to the LAN.
    $oldFirewallRule = Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue
    if ($oldFirewallRule) { $oldFirewallRule | Remove-NetFirewallRule -ErrorAction Stop }
    New-NetFirewallRule -Name $firewallRuleName `
        -DisplayName 'Картотека — HTTPS-сервер' `
        -Group 'Kartoteka' `
        -Direction Inbound -Action Allow -Enabled True `
        -Protocol TCP -LocalPort $Port `
        -Profile Domain,Private -RemoteAddress LocalSubnet | Out-Null
    $firewallRuleCreated = $true

    Write-Host "Служба установлена. Проверка готовности: $healthUrl" -ForegroundColor Green
    Write-Host "Откройте с доверенным сертификатом: https://$ServerDnsName`:$Port/" -ForegroundColor Green
    Write-Host "Данные и будущие резервные копии хранятся отдельно от приложения: $dataRoot" -ForegroundColor Yellow

}
catch {
    $failure = $_
    if ($firewallRuleCreated) {
        try { Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue } catch {}
    }
    if ($serviceCreated) {
        try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
        & "$env:SystemRoot\System32\sc.exe" delete $serviceName | Out-Null
    }
    Write-Error $failure
    throw $failure
}
finally {
    $adminPasswordPlain = $null
    $jwtKey = $null
    $envLines = $null
    [GC]::Collect()
}
