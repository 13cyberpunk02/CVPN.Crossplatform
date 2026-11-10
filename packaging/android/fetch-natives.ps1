param(
    [string]$SingBoxVersion = "1.14.3",
    [string]$HevVersion = "2.19.3",
    [string]$ByeDpiVersion = "0.17.3"
)

# Нативные компоненты APK, кроме ядра для телефонов. Кладёт их в
# CVPN.Android/natives/<abi>/ - оттуда SDK упаковывает их в APK.
#
#   arm64-v8a (телефоны): libhev-socks5-tunnel.so, libciadpi.so.
#       Ядро libsingbox.so собирается отдельно, урезанным: build-core.sh.
#   x86_64 (эмулятор):    то же плюс официальное ядро sing-box - урезанное
#       под x86_64 без NDK не собрать, а в релиз эта архитектура не идёт.
#
# Почему .so: Android разрешает запускать только файлы из nativeLibraryDir,
# а туда установщик распаковывает лишь lib*.so. Поэтому исполняемые sing-box
# и ciadpi едут под именами libsingbox.so и libciadpi.so.

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$natives = Join-Path $root "CVPN.Android\natives"
$work = Join-Path ([IO.Path]::GetTempPath()) "cvpn-android-natives"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null
foreach ($abi in "arm64-v8a", "x86_64") { New-Item -ItemType Directory -Path (Join-Path $natives $abi) -Force | Out-Null }

function Get-Checked([string]$url, [string]$file, [string]$sha256) {
    Invoke-WebRequest $url -OutFile $file
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { throw "Хеш не совпал для $url`: ожидался $sha256, получен $actual" }
}

Write-Host "== sing-box $SingBoxVersion (официальный) для эмулятора x86_64 ==" -ForegroundColor Cyan
$name = "sing-box-$SingBoxVersion-android-amd64"
Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$name.tar.gz" -OutFile (Join-Path $work "$name.tar.gz")
tar -xzf (Join-Path $work "$name.tar.gz") -C $work
if ($LASTEXITCODE -ne 0) { throw "tar вернул $LASTEXITCODE" }
Copy-Item (Join-Path $work "$name\sing-box") (Join-Path $natives "x86_64\libsingbox.so") -Force

Write-Host "== ByeDPI $ByeDpiVersion ==" -ForegroundColor Cyan
# Статические Linux-сборки ciadpi: от системных библиотек не зависят и запускаются
# на Android как есть. Тег v0.17.3, а файлы byedpi-17.3-...: «0.» в именах опущен.
# При смене версии нужно обновить и хеши.
$short = $ByeDpiVersion -replace '^0\.', ''
$byedpi = @{
    "arm64-v8a" = @{ Arch = "aarch64"; Sha = "d5806504e159e8119ede5af1164a609251b0520cd4483a7db611b6d238b9404b" }
    "x86_64"    = @{ Arch = "x86_64";  Sha = "98f73c32eacb571ebd88d790f6376ed9e70f02d44c1fe862472ecea75cd7117d" }
}
foreach ($abi in $byedpi.Keys) {
    $arch = $byedpi[$abi].Arch
    $archive = Join-Path $work "byedpi-$arch.tar.gz"
    Get-Checked "https://github.com/hufrea/byedpi/releases/download/v$ByeDpiVersion/byedpi-$short-$arch.tar.gz" $archive $byedpi[$abi].Sha
    $dir = Join-Path $work "byedpi-$arch"
    New-Item -ItemType Directory -Path $dir | Out-Null
    tar -xzf $archive -C $dir
    Copy-Item (Join-Path $dir "ciadpi-$arch") (Join-Path $natives "$abi\libciadpi.so") -Force
}
Invoke-WebRequest "https://raw.githubusercontent.com/hufrea/byedpi/v$ByeDpiVersion/LICENSE" -OutFile (Join-Path $natives "LICENSE.byedpi.txt")

Write-Host "== hev-socks5-tunnel $HevVersion ==" -ForegroundColor Cyan
$aar = Join-Path $work "hev.aar"
Invoke-WebRequest "https://github.com/heiher/hev-socks5-tunnel/releases/download/$HevVersion/hev-socks5-tunnel.aar" -OutFile $aar
Expand-Archive $aar -DestinationPath (Join-Path $work "hev") -Force

foreach ($abi in "arm64-v8a", "x86_64") {
    Copy-Item (Join-Path $work "hev\jni\$abi\libhev-socks5-tunnel.so") (Join-Path $natives $abi) -Force
}

# Java-класс hev.htproxy.TProxyService. Сами мы его не вызываем, но JNI_OnLoad
# библиотеки регистрирует на нём методы: без класса загрузка библиотеки через
# System.loadLibrary (так делает .NET Android в отлаживаемой сборке) падает.
Copy-Item (Join-Path $work "hev\classes.jar") (Join-Path $natives "hev-socks5-tunnel.jar") -Force
Invoke-WebRequest "https://raw.githubusercontent.com/heiher/hev-socks5-tunnel/$HevVersion/LICENSE" -OutFile (Join-Path $natives "LICENSE.hev-socks5-tunnel.txt")

Remove-Item $work -Recurse -Force

if (-not (Test-Path (Join-Path $natives "arm64-v8a\libsingbox.so"))) {
    Write-Warning "Ядра для телефонов ещё нет: соберите его - packaging/android/build-core.sh (Linux или WSL)"
}

Get-ChildItem $natives -Recurse -File | Format-Table FullName, Length
