# CVPN (Avalonia)

Кроссплатформенная версия [CVPN](https://github.com/13CyberPunk02/CVPN) — клиента sing-box —
на Avalonia: Windows, Linux, Android.

## Структура

```
CVPN.Shared/    протокол IPC, логи, остановка процессов (общий с будущей службой)
CVPN.Core/      логика без UI: модели, ConfigBuilder, парсеры ссылок, подписки, Clash API, локализация
CVPN/           общий Avalonia-UI: страницы, тема, контролы
CVPN.Desktop/   точка входа Windows / Linux
CVPN.Android/   точка входа Android (VpnService — в работе)
CVPN.Tests/     xUnit на CVPN.Core; Pending/ — тесты на ещё не перенесённый код
```

## Сборка

```
dotnet build CVPN.Desktop
dotnet build CVPN.Android
dotnet test  CVPN.Tests
```
