# Quieter

Первый сетевой вертикальный срез на Unity `6000.5.8f1`: Windows-клиент входит
через Steam, подключается одной кнопкой к выделенному Linux-серверу и получает
детерминированный процедурный мир 2×2 км. Сервер авторитетно симулирует игроков,
проверяет Steam ticket и сохраняет последнее положение в PostgreSQL.

## Что уже реализовано

- Netcode for GameObjects `2.13.1`, Unity Transport, 30 сетевых тиков/с и
  независимая симуляция движения 60 Гц;
- лимит 16 игроков, timeout авторизации, проверки protocol/generator version,
  заполненности и повторного SteamID;
- WASD, мышь, бег, прощающий прыжок, client prediction/reconciliation,
  покадровая камера и интерполяция других игроков;
- 32×32 чанка по 64 м, сетка 33×33, целочисленный детерминированный noise,
  безопасная центральная область и streaming radius 3;
- процедурный mesh/collider земли и стабильный `WorldObjectCatalog`: куб можно
  заменить prefab-моделью без изменения генератора, сохранений или сети;
- ASP.NET Core 10 профильный сервис, PostgreSQL 18, EF migration, автоматическое
  создание мира/профиля и сохранение позиции каждые 30 секунд;
- Docker Compose для игры, API, базы, playit-agent и семи ежедневных backup;
- версионная выкладка по SSH с health checks и откатом на предыдущий релиз.

## Открытие проекта

1. Откройте корень проекта в Unity `6000.5.8f1`.
2. Дождитесь разрешения пакетов. Steamworks.NET `2025.163.0` уже встроен в
   `Packages`, поэтому установленный Git для открытия проекта не требуется.
3. Если ресурсы ещё не созданы, выполните `Tools > Quieter > Configure Project`.
4. В Editor нажмите `ИГРАТЬ ЛОКАЛЬНО (ТЕСТ)`, чтобы без Steam запустить host,
   загрузить процедурный мир и сразу войти игроком. Для запуска готовой
   Development-сборки используйте `--host --development-auth`.
   Тестовый authenticator отсутствует в production-сборках.

В обычном клиенте нет поля IP: адрес хранится в
`Assets/Resources/Quieter/ServerEndpoint.asset`. Для публичной сборки он
подставляется скриптом вместе с CA сертификата.

## Сборки

Windows development client:

```powershell
./Scripts/Build-Unity.ps1 -Target WindowsClient `
  -ServerHost "your-tunnel.playit.gg" -ServerPort 30123 `
  -DtlsCaFile "C:\secure\quieter-ca.pem"
```

Linux Dedicated Server:

```powershell
./Scripts/Build-Unity.ps1 -Target LinuxServer
```

Для Linux-сборки установите через Unity Hub модуль **Linux Build Support
(Mono)** для `6000.5.8f1`. Production build со Steam App ID 480 блокируется;
перед релизом передайте настоящий `-SteamAppId` и `-Production`.

## Домашний сервер

Полная инструкция и восстановление backup находятся в
[`Deploy/README.md`](Deploy/README.md). Секреты живут только в
`/opt/quieter/shared/.env` и `/opt/quieter/shared/secrets` на сервере.
playit-agent должен направлять публичный UDP tunnel на `127.0.0.1:7777`.
В клиентскую сборку передаются именно публичные hostname и порт, назначенные
playit (они обычно отличаются от локального `7777`).

Выкладка с Windows после настройки SSH:

```powershell
./Scripts/Deploy-Server.ps1 -RemoteHost "server.lan" -RemoteUser quieter
```

Скрипт собирает сервер, создаёт версионный архив, копирует его в
`/opt/quieter/releases`, заранее применяет миграции, переключает Compose и ждёт
здоровья всех контейнеров. При неудаче он возвращает предыдущую версию.

## Проверки

Unity Test Runner содержит EditMode- и PlayMode-наборы в `Assets/Quieter/Tests`.
Backend-тесты запускаются командой:

```powershell
dotnet test Backend/Quieter.ProfileService.Tests/Quieter.ProfileService.Tests.csproj
```

Перед первым публичным тестом остаются операционные действия: выдать playit
agent key и UDP hostname/port, создать DTLS сертификат, установить Linux Build
Support и провести ручной вход двумя Steam-аккаунтами App ID 480.
