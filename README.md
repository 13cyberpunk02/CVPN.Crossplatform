# CVPN (Avalonia)

Кроссплатформенная версия [CVPN](https://github.com/13CyberPunk02/CVPN) - клиента sing-box -
на Avalonia: Windows, Linux, Android.

## Структура

```
CVPN.Shared/    протокол IPC, файловый лог, остановка процессов - общий для приложения и службы
CVPN.Core/      логика без UI: модели, ConfigBuilder, парсеры, подписки, Clash API, ViewModels,
                интерфейсы платформы (Platform/), способы запуска ядра (Tunnel/), локализация
CVPN/           общий интерфейс Avalonia (сборка CVPN.UI): страницы, тема и UI Kit (Theme/),
                круг подключения, шапка окна, трей, диалоги
CVPN.Desktop/   точка входа Windows / Linux (CVPN.exe / CVPN), реализации платформы
CVPN.Service/   служба туннеля: служба Windows и юнит systemd
CVPN.Android/   точка входа Android (VpnService - в работе)
CVPN.Tests/     xUnit: Core и санитайзер службы
packaging/      установщик и портативная сборка Windows, tar.gz/.deb/AppImage для Linux
```

## Сборка и запуск

```
dotnet build CVPN.Desktop
dotnet test  CVPN.Tests
dotnet build CVPN.Android
```

`CVPN_DATA_DIR=<каталог>` - свой каталог данных вместо `%AppData%\CVPN` / `~/.config/CVPN`:
для проверок и портативного запуска.

В Debug-сборке: `--page=<connection|profiles|routing|connections|logs|byedpi|settings>`,
`--theme=dark|light`, `--snapshot=файл.png[|ширина[|высота]]` и `--scroll-end` -
отрисовать окно в PNG и выйти (проверка вёрстки без ручных кликов).

## Linux

**Права на TUN.** Два способа:

1. Служба (рекомендуется): «Настройки → Служба туннеля → Установить». Через `pkexec`
   служба и ядро копируются в `/opt/cvpn`, ставится юнит `cvpn-tunnel.service`,
   пользователь добавляется в группу `cvpn`. **Доступ появится после повторного входа
   в систему.** Вручную: `sudo sh linux/cvpn-service.sh install <service> <sing-box> $USER`.
2. Без службы - выдать права самому ядру:
   `sudo setcap cap_net_admin,cap_net_raw,cap_net_bind_service+ep <путь к sing-box>`.
   Учтите: это права на сеть для любого, кто запустит этот файл.

**Kill switch** - таблица nftables `inet cvpn_killswitch`: исходящий трафик запрещён, кроме
TUN, `lo`, локальной сети, DHCP и соединений ядра (метка `route.default_mark`).
Трафик ByeDPI метки не несёт, поэтому при включённом kill switch сайты через ByeDPI не откроются.
Включение и снятие - через `pkexec`. Аварийно: `sudo nft delete table inet cvpn_killswitch`.

**Системный прокси** настраивается через `gsettings` (GNOME и производные).
**Трей** - StatusNotifierItem: KDE, Cinnamon, XFCE; в GNOME нужно расширение AppIndicator.

## Пакеты

```
packaging/windows/build.ps1 -Version 1.2.3     # dist\CVPN-1.2.3-setup.exe, -portable.zip
packaging/linux/build.sh 1.2.3                 # dist/CVPN-1.2.3-linux-x64.tar.gz, cvpn_1.2.3_amd64.deb, .AppImage
```

Релиз в GitHub собирается по тегу `v*` (`.github/workflows/release.yml`).

## Android

Режим TUN на Android - это VPN:

```
приложения ─► TUN (VpnService) ─► hev-socks5-tunnel ─► SOCKS5 127.0.0.1:mixed ─► sing-box ─► сервер
```

- sing-box - обычный процесс приложения (`libsingbox.so` в nativeLibraryDir: запускать можно
  только оттуда). Само приложение исключено из своего VPN, поэтому соединения ядра не
  зацикливаются.
- tun2socks - [hev-socks5-tunnel](https://github.com/heiher/hev-socks5-tunnel), его C-API
  вызывается напрямую (P/Invoke), без Java-обёрток.
- Конфиг для Android правит `ConfigBuilder.AdaptForAndroid`: без входа tun, без привязки к
  интерфейсу и поиска процессов, локальный DNS - DNS текущей сети.
- Kill switch - системный: «Настройки → Сеть → VPN → CVPN → Постоянная VPN» и
  «Блокировать соединения без VPN».

Нативные библиотеки (ARM64 и x86_64) не хранятся в git:

```
pwsh packaging/android/fetch-natives.ps1
dotnet build CVPN.Android -c Release
```

Если Release падает на «Precompiling failed» и в пути профиля Windows есть кириллица -
это AOT-компилятор Mono: задайте латинский временный каталог, например
`$env:TEMP = $env:TMP = "C:\tmp"`.
