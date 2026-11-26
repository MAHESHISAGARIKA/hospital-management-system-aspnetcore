#!/bin/sh
set -eu
umask 077

if [ -n "${RAILWAY_SERVICE_ID:-}" ]; then
    if [ -L /data ] || ! mountpoint -q /data; then
        printf '%s\n' 'Railway persistent storage requires a mounted filesystem at /data.' >&2
        exit 1
    fi
    export DataProtection__KeyDirectory=/data/keys
    export PatientDocuments__StorageRoot=/data/documents
fi

for directory in /data/keys /data/documents; do
    if [ -L "$directory" ]; then
        printf '%s\n' 'A storage directory cannot be a symbolic link.' >&2
        exit 1
    fi
done

if [ "$(id -u)" -eq 0 ]; then
    for directory in /data/keys /data/documents; do
        install -d -m 0700 -o app -g app "$directory"
    done
    exec setpriv --reuid=app --regid=app --clear-groups --no-new-privs /bin/sh /usr/local/bin/hms-start "$@"
fi

if [ "${RAILWAY_SERVICE_ID:-}" ]; then
    for directory in /data/keys /data/documents; do
        if [ ! -d "$directory" ] || [ ! -w "$directory" ] || [ ! -x "$directory" ]; then
            printf 'The application user needs write and search access to %s.\n' "$directory" >&2
            exit 1
        fi
    done
fi

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"
exec dotnet /app/HospitalManagementSystem.Web.dll "$@"
