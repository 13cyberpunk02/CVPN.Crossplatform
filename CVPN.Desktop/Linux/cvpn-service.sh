#!/bin/sh
# Установка и удаление службы туннеля CVPN на Linux.
# Запускается приложением через pkexec (то есть от root), вручную - через sudo:
#
#   sudo sh cvpn-service.sh install <каталог службы> <путь к sing-box> <пользователь>
#   sudo sh cvpn-service.sh uninstall
#
# Служба и ядро копируются в /opt/cvpn: приложение может жить в AppImage,
# который монтируется во временный каталог и исчезает после выхода.

set -eu

UNIT=/etc/systemd/system/cvpn-tunnel.service
PREFIX=/opt/cvpn
GROUP=cvpn

case "${1:-}" in
install)
    SRC_SERVICE="$2"
    SRC_CORE="$3"
    TARGET_USER="$4"

    [ -x "$SRC_SERVICE/CVPN.Service" ] || { echo "CVPN.Service not found in $SRC_SERVICE" >&2; exit 2; }
    [ -f "$SRC_CORE" ] || { echo "sing-box not found: $SRC_CORE" >&2; exit 3; }

    # Остановить старую версию, если есть: иначе файлы заняты
    systemctl stop cvpn-tunnel.service 2>/dev/null || true

    install -d -m 0755 "$PREFIX/service" "$PREFIX/core"
    cp -a "$SRC_SERVICE"/. "$PREFIX/service/"
    install -m 0755 "$SRC_CORE" "$PREFIX/core/sing-box"
    chmod 0755 "$PREFIX/service/CVPN.Service"

    # Управлять службой может только группа cvpn - в неё добавляем пользователя.
    # Членство начнёт действовать после повторного входа в систему.
    getent group "$GROUP" >/dev/null || groupadd --system "$GROUP"
    usermod -aG "$GROUP" "$TARGET_USER"

    cat > "$UNIT" <<EOF
[Unit]
Description=CVPN Tunnel (sing-box)
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=$PREFIX/service/CVPN.Service
Restart=on-failure
RestartSec=3
# /run/cvpn - для сокета команд, /var/lib/cvpn - конфиг и кэш (0700)
RuntimeDirectory=cvpn
RuntimeDirectoryMode=0755
StateDirectory=cvpn
StateDirectoryMode=0700

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable --now cvpn-tunnel.service
    ;;

uninstall)
    systemctl disable --now cvpn-tunnel.service 2>/dev/null || true
    rm -f "$UNIT"
    systemctl daemon-reload
    rm -rf "$PREFIX/service" "$PREFIX/core"
    rmdir "$PREFIX" 2>/dev/null || true
    # Группу не удаляем: в ней могут быть пользователи, и это ничего не открывает
    ;;

*)
    echo "usage: $0 install <service dir> <sing-box> <user> | uninstall" >&2
    exit 1
    ;;
esac
