#!/usr/bin/env bash
# Урезанное ядро sing-box для Android (arm64): CVPN.Android/natives/arm64-v8a/libsingbox.so
#
#   packaging/android/build-core.sh
#
# Официальная сборка под Android - 79 МБ: в ней Tailscale, Cloudflared, WireGuard,
# OpenVPN, OpenConnect, USB/IP и прочее, чего CVPN не использует. Здесь только:
#   with_utls       - Reality и отпечатки TLS (uTLS)
#   with_clash_api  - управление ядром из приложения
#   with_quic       - DNS по HTTP/3 и QUIC
# Получается около 42 МБ. NaiveProxy на Android нет и в официальной сборке
# (ему нужен Cronet через cgo).
#
# Нужны curl, git, tar. Go скачивается во временный каталог, если его нет.
# Работает на Linux и в WSL; android/arm64 собирается без cgo и без NDK.

set -euo pipefail

SINGBOX_VERSION="${SINGBOX_VERSION:-1.14.3}"
GO_VERSION="${GO_VERSION:-1.26.8}"
TAGS="with_utls,with_clash_api,with_quic,badlinkname,tfogo_checklinkname0"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TARGET="$ROOT/CVPN.Android/natives/arm64-v8a/libsingbox.so"
WORK="$(mktemp -d -t cvpn-core-XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

if command -v go >/dev/null && go version | grep -q "go$GO_VERSION "; then
    echo "== Go $GO_VERSION из системы"
else
    echo "== Go $GO_VERSION во временный каталог"
    curl -fsSL "https://go.dev/dl/go$GO_VERSION.linux-amd64.tar.gz" | tar -xz -C "$WORK"
    export PATH="$WORK/go/bin:$PATH"
fi
# -modcacherw: иначе кэш модулей только для чтения и временный каталог не удалить
export GOPATH="$WORK/gopath" GOCACHE="$WORK/gocache" GOTOOLCHAIN=local GOFLAGS=-modcacherw

echo "== sing-box $SINGBOX_VERSION"
git clone --quiet --depth 1 --branch "v$SINGBOX_VERSION" https://github.com/SagerNet/sing-box "$WORK/src"

cd "$WORK/src"
GOOS=android GOARCH=arm64 CGO_ENABLED=0 go build -trimpath -tags "$TAGS" \
    -ldflags "-s -w -buildid= -checklinkname=0 -X github.com/sagernet/sing-box/constant.Version=$SINGBOX_VERSION" \
    -o "$WORK/sing-box" ./cmd/sing-box

mkdir -p "$(dirname "$TARGET")"
install -m 755 "$WORK/sing-box" "$TARGET"
# GPL-3.0: текст лицензии едет вместе с бинарником
cp "$WORK/src/LICENSE" "$ROOT/CVPN.Android/natives/LICENSE.sing-box.txt"

ls -la "$TARGET"
