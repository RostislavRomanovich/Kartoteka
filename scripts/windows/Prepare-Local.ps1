[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$AdminEmail = "admin@localhost"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) { Write-Error $Message; exit 1 }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail "Не найден dotnet. Установите .NET 8 SDK, затем откройте новое окно PowerShell." }
$version = (& dotnet --version).Trim()
if (-not $version.StartsWith('8.')) { Write-Warning "Обнаружен .NET SDK $version. Проект рассчитан на .NET 8; рекомендуется SDK 8.x." }

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = Read-Host "Строка подключения PostgreSQL (пример: Host=localhost;Port=5432;Database=kartoteka;Username=kartoteka_app;Password=...)"
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { Fail "Строка подключения не задана." }
$env:KARTOTEKA_DB_CONNECTION = $ConnectionString

$jwtBytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Fill($jwtBytes)
$env:KARTOTEKA_JWT_SIGNING_KEY = [Convert]::ToBase64String($jwtBytes)
$env:KARTOTEKA_BOOTSTRAP_ADMIN_EMAIL = $AdminEmail
$securePassword = Read-Host "Задайте временный пароль первого администратора (не менее 12 символов, верхний/нижний регистр, цифра и спецсимвол)" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try { $env:KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ($env:KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD.Length -lt 12) { Fail "Пароль слишком короткий. Запустите скрипт повторно." }

Push-Location (Join-Path $PSScriptRoot '..\..')
try {
    Write-Host "Восстановление NuGet-пакетов..." -ForegroundColor Cyan
    & dotnet restore
    if ($LASTEXITCODE -ne 0) { Fail "dotnet restore завершился с ошибкой." }

    $ef = & dotnet ef --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Установка dotnet-ef 8.0.11..." -ForegroundColor Cyan
        & dotnet tool install --global dotnet-ef --version 8.0.11
        if ($LASTEXITCODE -ne 0) { Fail "Не удалось установить dotnet-ef. Выполните: dotnet tool install --global dotnet-ef --version 8.0.11" }
        $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
    }

    $migrationDir = Join-Path (Get-Location) 'Migrations'
    if (-not (Test-Path $migrationDir) -or -not (Get-ChildItem $migrationDir -Filter '*_*.cs' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*ModelSnapshot.cs' })) {
        Write-Host "Миграций ещё нет. Создание InitialCreate из текущей модели..." -ForegroundColor Cyan
        & dotnet ef migrations add InitialCreate
        if ($LASTEXITCODE -ne 0) { Fail "Не удалось создать миграцию InitialCreate. Сохраните текст ошибки и передайте его для исправления." }
    } elseif (-not (Get-ChildItem $migrationDir -Filter '*_Stage6CoreSchema.cs' -ErrorAction SilentlyContinue)) {
        Write-Host "Создание миграции Stage6CoreSchema для обновления существующей схемы..." -ForegroundColor Cyan
        & dotnet ef migrations add Stage6CoreSchema
        if ($LASTEXITCODE -ne 0) { Fail "Не удалось создать миграцию Stage6CoreSchema. Сохраните текст ошибки и передайте его для исправления." }
    } else {
        Write-Host "Миграция Stage6CoreSchema уже существует; повторно не создаётся." -ForegroundColor Yellow
    }

    Write-Host "Применение миграций к PostgreSQL..." -ForegroundColor Cyan
    & dotnet ef database update
    if ($LASTEXITCODE -ne 0) { Fail "Миграции не применены. Проверьте доступность PostgreSQL и права пользователя БД." }

    Write-Host "Сборка проекта..." -ForegroundColor Cyan
    & dotnet build --no-restore
    if ($LASTEXITCODE -ne 0) { Fail "Сборка завершилась с ошибкой. Передайте полный вывод сборки." }

    $env:ASPNETCORE_URLS = 'http://localhost:5080'
    Write-Host "Подготовка завершена. Запускаю API на http://localhost:5080 (только локальная разработка)..." -ForegroundColor Green
    Write-Host "Пароль администратора хранится только в переменной текущего процесса; сохраните его в менеджере паролей." -ForegroundColor Yellow
    & dotnet run --no-build
} finally {
    Pop-Location
    Remove-Item Env:KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:KARTOTEKA_JWT_SIGNING_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
}
