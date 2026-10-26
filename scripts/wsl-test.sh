#!/usr/bin/env bash
# Сборка и тесты в Linux (WSL) — для машин, где Windows (Smart App Control) блокирует неподписанные .NET-сборки.
#
# Запуск из PowerShell в корне репозитория:
#   wsl bash scripts/wsl-test.sh                                   # build -warnaserror + все тесты
#   wsl bash scripts/wsl-test.sh --filter "FullyQualifiedName~Items"  # аргументы уходят в `dotnet test`
#   wsl bash scripts/wsl-test.sh run                               # запустить API на http://localhost:5000
#
# Режим `run`: поднимает PostgreSQL из docker-compose (если не запущен), берёт собранную SPA из
# src/KnowledgeHub.Api/wwwroot (сначала `npm --prefix src/KnowledgeHub.Web run build` на Windows) и запускает
# `dotnet run` в Development. Порт WSL2 доступен из Windows по localhost. Остановка — Ctrl+C.
#
# Что делает: ставит .NET SDK и ICU в домашний каталог (без sudo, если их нет), копирует репозиторий
# в ~/kh (файловая система Linux — быстро и без конфликта bin/obj с Windows), собирает и запускает тесты.
# Нужен доступ к Docker из WSL (Testcontainers поднимает PostgreSQL).
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${KH_WSL_DIR:-$HOME/kh}"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

# SDK нужной версии берётся из global.json (rollForward latestFeature), ставим канал 10.0.
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
  echo ">> Устанавливаю .NET SDK 10.0 в $DOTNET_ROOT"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" >/dev/null
fi

# Без ICU SDK падает или выдаёт NETSDK1188 — режим Invariant тут не подходит.
if ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
  ICU_DIR="$HOME/icu/root/usr/lib/x86_64-linux-gnu"
  if [ ! -d "$ICU_DIR" ]; then
    echo ">> Устанавливаю ICU в $HOME/icu (без root)"
    mkdir -p "$HOME/icu" && (
      cd "$HOME/icu"
      pkg="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)"
      apt download "$pkg" >/dev/null 2>&1
      dpkg -x "${pkg}"_*.deb "$HOME/icu/root"
    )
  fi
  export LD_LIBRARY_PATH="$ICU_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

MODE=test
if [ "${1:-}" = "run" ]; then MODE=run; shift; fi

docker info >/dev/null 2>&1 || { echo "Docker недоступен из WSL (нужен для PostgreSQL / Testcontainers)." >&2; exit 1; }

# wwwroot — результат сборки SPA; в режиме тестов он не нужен, в режиме run — нужен.
EXCLUDES=(--exclude .git --exclude bin --exclude obj --exclude node_modules)
[ "$MODE" = "test" ] && EXCLUDES+=(--exclude wwwroot)

echo ">> Синхронизирую $SRC -> $WORK"
mkdir -p "$WORK"
rsync -a --delete "${EXCLUDES[@]}" "$SRC/" "$WORK/"
cd "$WORK"

if [ "$MODE" = "run" ]; then
  if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
    echo ">> PostgreSQL не отвечает на :5432 — запускаю docker compose"
    docker compose -f "$SRC/docker-compose.yml" --project-directory "$SRC" up -d
    for _ in $(seq 1 30); do
      (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null && break
      sleep 1
    done
  fi
  [ -f src/KnowledgeHub.Api/wwwroot/index.html ] || echo "!! wwwroot пуст: SPA не собрана (npm --prefix src/KnowledgeHub.Web run build), будет работать только API." >&2
  echo ">> dotnet run (http://localhost:5000, Ctrl+C — остановить)"
  exec dotnet run --project src/KnowledgeHub.Api -warnaserror "$@"
fi

echo ">> dotnet build -warnaserror"
dotnet build KnowledgeHub.slnx -warnaserror
echo ">> dotnet test"
dotnet test KnowledgeHub.slnx --no-build "$@"