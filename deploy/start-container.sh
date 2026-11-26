#!/bin/sh
set -eu
umask 077

if [ -n "${RAILWAY_SERVICE_ID:-}" ]; then
    if [ "${RAILWAY_VOLUME_MOUNT_PATH:-}" != /data ]; then
        printf '%s\n' 'Attach the Railway service volume at /data.' >&2
        exit 1
    fi
    export DataProtection__KeyDirectory=/data/keys
    export PatientDocuments__StorageRoot=/data/documents
fi

if [ "$(id -u)" -eq 0 ]; then
    for directory in /data/keys /data/documents; do
        if [ -L "$directory" ]; then
            printf '%s\n' 'A storage directory cannot be a symbolic link.' >&2
            exit 1
        fi
        install -d -m 0700 -o app -g app "$directory"
    done
    exec setpriv --reuid=app --regid=app --clear-groups --no-new-privs /bin/sh /usr/local/bin/hms-start "$@"
fi

if [ -n "${RAILWAY_SERVICE_ID:-}" ] && { [ ! -w /data/keys ] || [ ! -w /data/documents ]; }; then
    printf '%s\n' 'The application user needs writable private directories under /data.' >&2
    exit 1
fi

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"
exec dotnet /app/HospitalManagementSystem.Web.dll "$@"
