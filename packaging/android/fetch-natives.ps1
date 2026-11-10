param(
    [string]$SingBoxVersion = "1.14.3",
    [string]$HevVersion = "2.19.3"
)

# Нативные компоненты для APK: ядро sing-box и tun2socks (hev-socks5-tunnel).
# Кладёт их в CVPN.Android/natives/<abi>/ - оттуда csproj упаковывает их в APK.
#
# Почему .so: Android разрешает запускать только файлы из nativeLibraryDir,
# а туда установщик распаковывает лишь lib*.so. Поэтому исполняемый sing-box
# едет под именем libsingbox.so.

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$natives = Join-Path $root "CVPN.Android\natives"
$work = Join-Path ([IO.Path]::GetTempPath()) "cvpn-android-natives"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

# Android ABI -> имя архива sing-box
$abis = @{ "arm64-v8a" = "arm64"; "x86_64" = "amd64" }

foreach ($abi in $abis.Keys) {
    $arch = $abis[$abi]
    $name = "sing-box-$SingBoxVersion-android-$arch"
    $archive = Join-Path $work "$name.tar.gz"

    Write-Host "== sing-box $SingBoxVersion для $abi ==" -ForegroundColor Cyan
    Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$name.tar.gz" -OutFile $archive
    tar -xzf $archive -C $work
    if ($LASTEXITCODE -ne 0) { throw "tar вернул $LASTEXITCODE" }

    $target = Join-Path $natives $abi
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $work "$name\sing-box") (Join-Path $target "libsingbox.so") -Force
}

Write-Host "== hev-socks5-tunnel $HevVersion ==" -ForegroundColor Cyan
$aar = Join-Path $work "hev.aar"
Invoke-WebRequest "https://github.com/heiher/hev-socks5-tunnel/releases/download/$HevVersion/hev-socks5-tunnel.aar" -OutFile $aar
Expand-Archive $aar -DestinationPath (Join-Path $work "hev") -Force

foreach ($abi in $abis.Keys) {
    Copy-Item (Join-Path $work "hev\jni\$abi\libhev-socks5-tunnel.so") (Join-Path $natives $abi) -Force
}

# Лицензии едут вместе с бинарниками: sing-box - GPL-3.0, hev-socks5-tunnel - MIT
Copy-Item (Join-Path $work "sing-box-$SingBoxVersion-android-arm64\LICENSE") (Join-Path $natives "LICENSE.sing-box.txt") -Force
Invoke-WebRequest "https://raw.githubusercontent.com/heiher/hev-socks5-tunnel/$HevVersion/LICENSE" -OutFile (Join-Path $natives "LICENSE.hev-socks5-tunnel.txt")

Remove-Item $work -Recurse -Force
Get-ChildItem $natives -Recurse -File | Format-Table FullName, Length
