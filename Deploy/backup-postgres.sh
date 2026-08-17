#!/bin/sh
set -eu

retention_days="${BACKUP_RETENTION_DAYS:-7}"
export PGPASSWORD="$(cat "${POSTGRES_PASSWORD_FILE:-/run/secrets/postgres_password}")"

while true; do
    timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
    destination="/backups/quieter-${timestamp}.dump"
    temporary="${destination}.partial"

    pg_dump --format=custom --compress=9 --file="${temporary}"
    mv "${temporary}" "${destination}"
    find /backups -maxdepth 1 -type f -name 'quieter-*.dump' -mtime "+${retention_days}" -delete
    count=0
    for backup in $(ls -1t /backups/quieter-*.dump); do
        count=$((count + 1))
        if [ "${count}" -gt 7 ]; then
            rm -f -- "${backup}"
        fi
    done
    date +%s >/tmp/quieter-backup-alive
    sleep 86400
done
