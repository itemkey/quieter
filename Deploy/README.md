# Домашний сервер Quieter

Compose поднимает PostgreSQL, внутренний профильный сервис, Unity Dedicated Server,
официальный playit-agent и ежедневное резервное копирование. Наружу не публикуются
ни база, ни HTTP API. playit должен направлять публичный UDP-туннель на
`127.0.0.1:7777` хоста.

## Первый запуск

1. Установите Docker Engine с Compose plugin на Ubuntu x86_64.
2. Создайте `/opt/quieter/shared/.env` по образцу `.env.example` и заполните
   `PLAYIT_SECRET_KEY`.
3. Создайте `/opt/quieter/shared/secrets` и четыре файла из
   `secrets/README.md`; реальные секреты не копируйте в Git. Например:

   ```sh
   openssl rand -base64 48 > /opt/quieter/shared/secrets/postgres_password.txt
   openssl rand -base64 48 > /opt/quieter/shared/secrets/profile_token.txt
   openssl req -x509 -newkey rsa:3072 -nodes -days 365 \
     -subj '/CN=quieter-server' -addext 'subjectAltName=DNS:quieter-server' \
     -keyout /opt/quieter/shared/secrets/dtls_private_key.pem \
     -out /opt/quieter/shared/secrets/dtls_certificate.pem
   chmod 600 /opt/quieter/shared/.env /opt/quieter/shared/secrets/*
   ```

   Файл `dtls_certificate.pem` не является секретом: его копия передаётся в
   `Build-Unity.ps1 -DtlsCaFile` и закрепляется внутри клиента. Закрытый ключ
   остаётся только на сервере.
4. Положите Linux-сборку в `Builds/LinuxServer` или используйте
   `Scripts/Deploy-Server.ps1` с рабочей Windows-машины.
5. В панели playit создайте UDP tunnel с origin `127.0.0.1:7777`. Назначенные
   публичные hostname/port используйте при Windows-сборке клиента.
6. В каталоге `Deploy` выполните `docker compose up -d --build --wait` и
   проверьте `docker compose ps`.

Для App ID 480 сервер подходит только для разработки. Перед публикацией задайте
настоящий Steam App ID, установите серверные Steam redistributables, настройте
депо и замените сертификат/закреплённый CA в клиентской сборке.

## Восстановление базы

1. Остановите `game-server` и `profile-service`.
2. Найдите архив в volume `quieter_postgres-backups`.
3. Очистите целевую базу и восстановите архив командой `pg_restore --clean
   --if-exists --no-owner --dbname=quieter <archive.dump>` внутри контейнера
   PostgreSQL с заданным `PGPASSWORD`.
4. Запустите профильный сервис (он применит новые миграции), затем игровой сервер.

Сначала отрепетируйте восстановление на отдельной базе. Автоматическая ротация
хранит резервные копии за последние семь суток.

## Публичный сброс мира для survival-обновления

Старый мир нельзя преобразовывать простым повышением номера генератора: профили,
карты и предметы имеют другую модель владения. Перед релизом остановите вход
игроков. Рекомендуемый путь — единая выкладка с явным двойным подтверждением:

```powershell
./Scripts/Deploy-Server.ps1 -RemoteHost "server.lan" -RemoteUser quieter `
  -ResetSurvivalWorld -ResetConfirmation RESET-QUIETER-WORLD-V7
```

Скрипт загружает новую версию, останавливает писателей, создаёт и проверяет backup,
применяет новую схему и только затем очищает состояние. После survival-reset
автоматический откат к старому коду запрещён: при сбое используется созданный
dump и ручное восстановление. Уже загруженный релиз можно сбросить отдельно из
его каталога `Deploy`:

```sh
chmod +x reset-survival-world.sh
./reset-survival-world.sh RESET-QUIETER-WORLD-V7
```

Операция сначала останавливает процессы, которые пишут данные, создаёт custom
dump в `Deploy/release-backups`, проверяет его через `pg_restore --list`, применяет
миграции и только после этого одной транзакцией очищает мир, аккаунты, персонажей,
квитанции переходов и имущество.
При любой ошибке до проверки дампа удаление не выполняется. Новый мир версии 7
создаст профильный сервис при запуске игрового сервера. Путь для архива можно
переопределить переменной `QUIETER_RELEASE_BACKUP_DIR`.
