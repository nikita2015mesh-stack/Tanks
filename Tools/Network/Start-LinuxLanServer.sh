#!/usr/bin/env bash
set -euo pipefail

if [[ $# -gt 1 ]]; then
    printf 'Использование: bash %s [IPv4 для привязки]\n' "$0" >&2
    exit 2
fi

bind_address=${1:-0.0.0.0}
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."
server_build=Builds/Network/Match-LinuxServer
if [[ ! -f "$server_build/active.txt" ]]; then
    printf 'Сначала соберите сервер в Unity: Tools → Mad Machines → Networking → Собрать сервер.\n' >&2
    exit 1
fi

server_id=$(<"$server_build/active.txt")
if [[ ! $server_id =~ ^[[:xdigit:]]{32}$ ]]; then
    printf 'Неверный идентификатор сборки в %s/active.txt. Пересоберите сервер.\n' "$server_build" >&2
    exit 1
fi

server_executable="$server_build/versions/$server_id/MadMachinesServer.x86_64"
if [[ ! -x "$server_executable" ]]; then
    printf 'Не удалось найти исполняемый файл сервера: %s\n' "$server_executable" >&2
    exit 1
fi

mkdir -p Logs/Network
run_dir=$(mktemp -d "$PWD/Logs/Network/lan.XXXXXX")
printf 'Сервер матчей — %s, UDP 7777 и 7778\n' "$bind_address"
printf 'Журнал: %s/server.log\n' "$run_dir"
printf 'Остановка из другого терминала: touch -- %q\n' "$run_dir/stop"
"$server_executable" -batchmode -nographics -mm-bind "$bind_address" -mm-port 7777 \
    -logFile - -mm-stop-file "$run_dir/stop" 2>&1 | tee "$run_dir/server.log"
