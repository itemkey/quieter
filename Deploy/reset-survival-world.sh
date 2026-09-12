#!/bin/sh
set -eu

required_confirmation="RESET-QUIETER-WORLD-V7"
if [ "${1:-}" != "${required_confirmation}" ]; then
    echo "Refusing to reset. Usage: ./reset-survival-world.sh ${required_confirmation}" >&2
    exit 64
fi

if [ ! -f "docker-compose.yml" ] || [ ! -f "secrets/postgres_password.txt" ]; then
    echo "Run this script from the Quieter Deploy directory." >&2
    exit 66
fi

backup_directory="${QUIETER_RELEASE_BACKUP_DIR:-./release-backups}"
case "${backup_directory}" in
    /|.|..|"")
        echo "QUIETER_RELEASE_BACKUP_DIR must name a dedicated backup directory." >&2
        exit 65
        ;;
esac

mkdir -p -- "${backup_directory}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="${backup_directory}/pre-survival-reset-${timestamp}.dump"
temporary_path="${backup_path}.partial"

writers_stopped=1
cleanup_on_failure() {
    status=$?
    trap - EXIT HUP INT TERM
    rm -f -- "${temporary_path}"
    if [ "${writers_stopped}" -eq 1 ]; then
        echo "Reset failed; restarting writers against the current database state..." >&2
        docker compose up -d --wait profile-service game-server || true
    fi
    exit "${status}"
}
trap cleanup_on_failure EXIT HUP INT TERM

echo "Stopping writers before the release backup..."
docker compose stop game-server profile-service

echo "Creating PostgreSQL backup: ${backup_path}"
docker compose exec -T postgres sh -eu -c '
    export PGPASSWORD="$(cat /run/secrets/postgres_password)"
    exec pg_dump --format=custom --compress=9 \
        --dbname="$POSTGRES_DB" --username="$POSTGRES_USER"
' >"${temporary_path}"

if [ ! -s "${temporary_path}" ]; then
    echo "Backup is empty; reset cancelled." >&2
    exit 74
fi

docker compose exec -T postgres pg_restore --list \
    <"${temporary_path}" >/dev/null
mv -- "${temporary_path}" "${backup_path}"

echo "Backup verified. Applying the release schema while writers are stopped..."
docker compose run --rm profile-service --migrate

echo "Backup verified. Resetting world, accounts, characters, and possessions..."
docker compose exec -T postgres sh -eu -c '
    export PGPASSWORD="$(cat /run/secrets/postgres_password)"
    exec psql --set=ON_ERROR_STOP=1 \
        --dbname="$POSTGRES_DB" --username="$POSTGRES_USER"
' <<'SQL'
BEGIN;
TRUNCATE TABLE worlds, players, characters, character_transfers, heir_offers
    RESTART IDENTITY CASCADE;
COMMIT;
SQL

echo "Starting profile service and game server..."
docker compose up -d --wait profile-service game-server
writers_stopped=0
trap - EXIT HUP INT TERM
echo "Survival world reset completed. Verified backup: ${backup_path}"
