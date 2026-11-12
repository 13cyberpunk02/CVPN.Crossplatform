#!/usr/bin/env bash
# Сборка CVPN для Linux x64: tar.gz, .deb и AppImage в dist/.
#
#   packaging/linux/build.sh <версия>
#
# Нужны: dotnet 10 SDK, curl, dpkg-deb. AppImage-инструмент
# скачивается сам. .NET встраивается в пакет (self-contained): ставить runtime
# пользователю не нужно.

set -euo pipefail

VERSION="${1:?usage: build.sh <version>}"
SINGBOX_VERSION="${SINGBOX_VERSION:-1.14.3}"
BYEDPI_VERSION="${BYEDPI_VERSION:-0.17.3}"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/dist"
WORK="$ROOT/build/linux"
APP="$WORK/app"

rm -rf "$WORK"
mkdir -p "$APP" "$OUT"

echo "== приложение и служба =="
dotnet publish "$ROOT/CVPN.Desktop/CVPN.Desktop.csproj" -c Release -r linux-x64 --self-contained true \
    -p:Version="$VERSION" -o "$APP"
dotnet publish "$ROOT/CVPN.Service/CVPN.Service.csproj" -c Release -r linux-x64 --self-contained true \
    -p:Version="$VERSION" -o "$APP/service"
rm -f "$APP"/*.pdb "$APP"/service/*.pdb

echo "== sing-box $SINGBOX_VERSION =="
curl -fsSL "https://github.com/SagerNet/sing-box/releases/download/v$SINGBOX_VERSION/sing-box-$SINGBOX_VERSION-linux-amd64.tar.gz" \
    | tar -xz -C "$WORK"
install -Dm755 "$WORK/sing-box-$SINGBOX_VERSION-linux-amd64/sing-box" "$APP/core/sing-box"
# GPL-3.0: текст лицензии обязан ехать вместе с бинарником
cp "$WORK/sing-box-$SINGBOX_VERSION-linux-amd64/LICENSE" "$APP/LICENSE.sing-box.txt"

echo "== ByeDPI $BYEDPI_VERSION =="
# Статическая сборка из релиза: от glibc не зависит и запускается на любом
# дистрибутиве. Тег v0.17.3, а файл byedpi-17.3-...: «0.» в имени опущен.
# При смене версии нужно обновить и хеш.
BYEDPI_SHA256="${BYEDPI_SHA256:-98f73c32eacb571ebd88d790f6376ed9e70f02d44c1fe862472ecea75cd7117d}"
curl -fsSL -o "$WORK/byedpi.tar.gz" \
    "https://github.com/hufrea/byedpi/releases/download/v$BYEDPI_VERSION/byedpi-${BYEDPI_VERSION#0.}-x86_64.tar.gz"
echo "$BYEDPI_SHA256  $WORK/byedpi.tar.gz" | sha256sum -c --quiet
mkdir -p "$WORK/byedpi"
tar -xzf "$WORK/byedpi.tar.gz" -C "$WORK/byedpi"
install -Dm755 "$WORK/byedpi/ciadpi-x86_64" "$APP/byedpi/ciadpi"
curl -fsSL -o "$APP/LICENSE.byedpi.txt" "https://raw.githubusercontent.com/hufrea/byedpi/v$BYEDPI_VERSION/LICENSE"

cp "$ROOT/LICENSE" "$APP/LICENSE"
chmod 755 "$APP/CVPN" "$APP/service/CVPN.Service"

ICON="$ROOT/CVPN/Assets/cvpn.png"
DESKTOP="$ROOT/packaging/linux/cvpn.desktop"

echo "== tar.gz =="
tar -C "$WORK" -czf "$OUT/CVPN-$VERSION-linux-x64.tar.gz" --transform "s,^app,cvpn-$VERSION," app

echo "== .deb =="
DEB="$WORK/deb"
mkdir -p "$DEB/DEBIAN" "$DEB/opt/cvpn" "$DEB/usr/bin" "$DEB/usr/share/applications" \
         "$DEB/usr/share/icons/hicolor/512x512/apps"
cp -a "$APP" "$DEB/opt/cvpn/app"
ln -s /opt/cvpn/app/CVPN "$DEB/usr/bin/cvpn"
sed 's#^Exec=.*#Exec=/opt/cvpn/app/CVPN %U#' "$DESKTOP" > "$DEB/usr/share/applications/cvpn.desktop"
cp "$ICON" "$DEB/usr/share/icons/hicolor/512x512/apps/cvpn.png"

cat > "$DEB/DEBIAN/control" <<EOF
Package: cvpn
Version: $VERSION
Section: net
Priority: optional
Architecture: amd64
Maintainer: CVPN <noreply@users.noreply.github.com>
Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6, libicu74 | libicu72 | libicu70 | libicu-dev
Recommends: pkexec | policykit-1, nftables
Description: sing-box client with routing rules
 Builds sing-box configuration, runs the core and shows connections.
 VLESS (Reality, WebSocket, XHTTP), AnyTLS and NaiveProxy.
EOF

# Служба ставится из настроек приложения; при удалении пакета её надо снять,
# иначе юнит будет указывать на исчезнувшие файлы
cat > "$DEB/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] && [ -f /etc/systemd/system/cvpn-tunnel.service ]; then
    sh /opt/cvpn/app/linux/cvpn-service.sh uninstall || true
fi
EOF
chmod 755 "$DEB/DEBIAN/prerm"

# До 1.2.3 скрипт службы лежал в Linux/ вместо linux/, и обходной путь был
# ссылкой linux -> Linux. Ссылку убираем: иначе dpkg положит новый скрипт
# через неё в старую папку и тут же удалит его вместе с ней
cat > "$DEB/DEBIAN/preinst" <<'EOF'
#!/bin/sh
set -e
if [ -L /opt/cvpn/app/linux ]; then
    rm -f /opt/cvpn/app/linux
fi
EOF
chmod 755 "$DEB/DEBIAN/preinst"

dpkg-deb --root-owner-group --build "$DEB" "$OUT/cvpn_${VERSION}_amd64.deb"

echo "== AppImage =="
APPDIR="$WORK/CVPN.AppDir"
mkdir -p "$APPDIR/usr/lib"
cp -a "$APP" "$APPDIR/usr/lib/cvpn"
cp "$ICON" "$APPDIR/cvpn.png"
cp "$DESKTOP" "$APPDIR/cvpn.desktop"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/cvpn/CVPN" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

TOOL="$WORK/appimagetool"
curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod 755 "$TOOL"
# Без FUSE в CI: инструмент распаковывает себя и запускается
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$OUT/CVPN-$VERSION-x86_64.AppImage"

echo "== готово =="
ls -la "$OUT"
