param(
    [string]$Version = "1.0.0",
    [string]$SingBoxVersion = "1.14.3",
    [string]$ByeDpiVersion = "0.17.3",
    # При смене версии ByeDPI нужно обновить и хеш: сборка с чужим архивом остановится
    [string]$ByeDpiSha256 = "70d2c94147193cb915f9c6eb5144b8d404dacbcfa90bda2383b6b211afafa456",
    # Установщик качает .NET сам, портативная сборка несёт его внутри
    [switch]$SkipInstaller
)

# Сборка CVPN для Windows: dist\CVPN-<версия>-setup.exe и dist\CVPN-<версия>-portable.zip.
# Нужны: .NET 10 SDK, Inno Setup 6 (для установщика).

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$work = Join-Path $root "build\windows"
$payload = Join-Path $PSScriptRoot "payload"
$dist = Join-Path $root "dist"

foreach ($dir in $work, $payload) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Path $work, $payload, $dist -Force | Out-Null

function Publish([string]$project, [string]$out, [bool]$selfContained) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 `
        --self-contained $selfContained -p:Version=$Version -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project вернул $LASTEXITCODE" }
    Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item
}

Write-Host "== sing-box $SingBoxVersion ==" -ForegroundColor Cyan
$zip = Join-Path $work "sing-box.zip"
Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-amd64.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath (Join-Path $work "sing-box") -Force
$core = Get-ChildItem (Join-Path $work "sing-box") -Filter sing-box.exe -Recurse | Select-Object -First 1
$coreLicense = Get-ChildItem (Join-Path $work "sing-box") -Filter LICENSE -Recurse | Select-Object -First 1

Write-Host "== ByeDPI $ByeDpiVersion ==" -ForegroundColor Cyan
# Тег v0.17.3, а файл byedpi-17.3-...: ведущий «0.» в имени архива опущен
$short = $ByeDpiVersion -replace '^0\.', ''
$byedpiZip = Join-Path $work "byedpi.zip"
Invoke-WebRequest "https://github.com/hufrea/byedpi/releases/download/v$ByeDpiVersion/byedpi-$short-x86_64-w64.zip" -OutFile $byedpiZip
$actual = (Get-FileHash $byedpiZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $ByeDpiSha256) { throw "Хеш ByeDPI не совпал: ожидался $ByeDpiSha256, получен $actual" }
Expand-Archive $byedpiZip -DestinationPath (Join-Path $work "byedpi") -Force
$ciadpi = Get-ChildItem (Join-Path $work "byedpi") -Filter ciadpi.exe -Recurse | Select-Object -First 1
if (-not $ciadpi) { throw "В архиве ByeDPI нет ciadpi.exe" }
$byedpiLicense = Join-Path $work "LICENSE.byedpi.txt"
Invoke-WebRequest "https://raw.githubusercontent.com/hufrea/byedpi/v$ByeDpiVersion/LICENSE" -OutFile $byedpiLicense

# Общее для установщика и портативной сборки: ядро, ciadpi, лицензии
function Add-Bundled([string]$target) {
    New-Item -ItemType Directory -Path (Join-Path $target "core"), (Join-Path $target "byedpi") -Force | Out-Null
    Copy-Item $core.FullName (Join-Path $target "core")
    Copy-Item $ciadpi.FullName (Join-Path $target "byedpi")
    # GPL-3.0 и MIT: тексты лицензий едут вместе с бинарниками
    Copy-Item $coreLicense.FullName (Join-Path $target "LICENSE.sing-box.txt")
    Copy-Item $byedpiLicense (Join-Path $target "LICENSE.byedpi.txt")
    Copy-Item (Join-Path $root "LICENSE") $target
}

if (-not $SkipInstaller) {
    Write-Host "== установщик ==" -ForegroundColor Cyan
    Publish "CVPN.Desktop\CVPN.Desktop.csproj" $payload $false
    Publish "CVPN.Service\CVPN.Service.csproj" (Join-Path $payload "service") $false
    Add-Bundled $payload

    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "ISCC.exe не найден. Установите Inno Setup 6: https://jrsoftware.org/isdl.php" }

    & $iscc "/DAppVersion=$Version" /DNeedsRuntime (Join-Path $PSScriptRoot "CVPN.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup вернул код $LASTEXITCODE" }
}

Write-Host "== портативная сборка ==" -ForegroundColor Cyan
$portable = Join-Path $work "portable"
Publish "CVPN.Desktop\CVPN.Desktop.csproj" $portable $true
Publish "CVPN.Service\CVPN.Service.csproj" (Join-Path $portable "service") $true
Add-Bundled $portable
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath (Join-Path $dist "CVPN-$Version-portable.zip") -Force

Get-ChildItem $dist | Format-Table Name, Length
