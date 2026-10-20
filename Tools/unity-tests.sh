#!/bin/bash
# Прогон тестов Unity (EditMode и PlayMode) для ветки на Маке, по одному на все треды.
#
#   Tools/unity-tests.sh <ветка> [--name ИМЯ] [--modes "EditMode PlayMode"] [--timeout МИНУТ] [--wait]
#   Tools/unity-tests.sh --status [ИМЯ]
#   Tools/unity-tests.sh --help
#
# Что делает:
#   1. Готовит worktree ../emberhold-wt-<ИМЯ> на свежем origin/<ветка> (detached, с git lfs pull).
#      Если в worktree есть незакоммиченные правки или в нём открыт Unity, останавливается.
#   2. Ждёт общий замок /tmp/emberhold-unity.lock (проверка раз в 30 секунд). В замок пишет
#      owner, pid и unity_pid. Брошенный замок (процесс-владелец и его Unity мертвы) снимает сам.
#   3. Гоняет режимы в batchmode по очереди, каждый не дольше --timeout минут (по умолчанию 40).
#   4. Снимает замок (и при ошибке, и при Ctrl+C) и пишет итог в
#      Logs/unity-tests/<ИМЯ>/summary.txt основной папки проекта.
#
# По умолчанию уходит в фон и сразу возвращает управление: сессии ждать не нужно.
# Ход прогона: Logs/unity-tests/<ИМЯ>/status, итог: summary.txt, вывод скрипта: run.log.
# --wait — работать в этом терминале до конца.

set -u

LOCK=/tmp/emberhold-unity.lock
POLL=${EMBERHOLD_POLL:-30}

usage() { sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; }

die() { echo "unity-tests: $*" >&2; exit 2; }

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
ROOT=$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null) || die "не нашёл git-репозиторий"
# Если скрипт запущен из worktree, основная папка — та, где лежит общий .git.
COMMON=$(git -C "$ROOT" rev-parse --path-format=absolute --git-common-dir 2>/dev/null)
[ -n "$COMMON" ] && ROOT=$(dirname "$COMMON")
RESULTS_ROOT="$ROOT/Logs/unity-tests"

BRANCH=""; NAME=""; MODES="EditMode PlayMode"; TIMEOUT_MIN=40; WAIT=0; FOREGROUND=0; STATUS=0
while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --status) STATUS=1 ;;
    --name) NAME="${2:-}"; shift ;;
    --modes) MODES="${2:-}"; shift ;;
    --timeout) TIMEOUT_MIN="${2:-}"; shift ;;
    --wait) WAIT=1 ;;
    --foreground) FOREGROUND=1 ;;
    -*) die "неизвестный ключ $1 (см. --help)" ;;
    *) if [ "$STATUS" = 1 ] && [ -z "$NAME" ]; then NAME="$1"; else BRANCH="$1"; fi ;;
  esac
  shift
done

lock_field() { cat "$LOCK/$1" 2>/dev/null; }
alive() { [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null; }

show_lock() {
  if [ -d "$LOCK" ]; then
    echo "Замок: $(lock_field owner) (pid $(lock_field pid), unity $(lock_field unity_pid), с $(lock_field started))"
  else
    echo "Замок свободен."
  fi
}

if [ "$STATUS" = 1 ]; then
  show_lock
  if [ -n "$NAME" ]; then
    D="$RESULTS_ROOT/$NAME"
    [ -d "$D" ] || die "нет прогона $NAME в $RESULTS_ROOT"
    echo "Статус: $(cat "$D/status" 2>/dev/null)"
    [ -f "$D/summary.txt" ] && cat "$D/summary.txt"
  else
    for D in "$RESULTS_ROOT"/*/; do
      [ -d "$D" ] && echo "$(basename "$D"): $(cat "$D/status" 2>/dev/null)"
    done
  fi
  exit 0
fi

[ -n "$BRANCH" ] || { usage; die "не указана ветка"; }
case "$TIMEOUT_MIN" in ''|*[!0-9]*) die "--timeout должен быть числом минут" ;; esac
[ -n "$NAME" ] || NAME=$(echo "$BRANCH" | sed 's#^claude/##; s#[^A-Za-z0-9._-]#-#g')
OUT="$RESULTS_ROOT/$NAME"
WT="$(dirname "$ROOT")/emberhold-wt-$NAME"

# Запуск в фоне: перезапускаем себя в новой сессии, чтобы прогон пережил закрытие терминала.
if [ "$WAIT" = 0 ] && [ "$FOREGROUND" = 0 ]; then
  mkdir -p "$OUT" || die "не создать $OUT"
  rm -f "$OUT/summary.txt"
  echo "queued $(date '+%F %T')" > "$OUT/status"
  ARGS=("$0" "$BRANCH" --name "$NAME" --modes "$MODES" --timeout "$TIMEOUT_MIN" --foreground)
  perl -MPOSIX -e 'POSIX::setsid(); exec @ARGV or die' -- "${ARGS[@]}" \
    </dev/null >"$OUT/run.log" 2>&1 &
  echo "Прогон $NAME запущен в фоне (pid $!)."
  echo "Ход: $OUT/status"
  echo "Итог: $OUT/summary.txt"
  echo "Проверить: Tools/unity-tests.sh --status $NAME"
  exit 0
fi

mkdir -p "$OUT" || die "не создать $OUT"
rm -f "$OUT/summary.txt"
status() { echo "$* $(date '+%F %T')" > "$OUT/status"; echo "[$(date '+%T')] $*"; }

UNITY_PID=""
HAVE_LOCK=0
finish() {
  local code=$?
  if alive "$UNITY_PID"; then kill "$UNITY_PID" 2>/dev/null; sleep 5; kill -9 "$UNITY_PID" 2>/dev/null; fi
  if [ "$HAVE_LOCK" = 1 ] && [ "$(lock_field pid)" = "$$" ]; then rm -rf "$LOCK"; fi
  if [ ! -f "$OUT/summary.txt" ]; then
    echo "Прогон прерван (код $code), подробности в run.log." > "$OUT/summary.txt"
    status "aborted"
  fi
}
trap finish EXIT
trap 'exit 130' INT TERM HUP

fail() { status "failed"; echo "$*" | tee "$OUT/summary.txt"; exit 1; }

# Сетевые команды повторяем: разовый сбой SSL до github.com не должен ронять прогон.
retry() {
  local delay
  for delay in ${EMBERHOLD_RETRY_DELAYS:-2 4 8 16}; do
    "$@" && return 0
    echo "Не вышло: $*. Повтор через $delay с."
    sleep "$delay"
  done
  "$@"
}

# 1. Worktree.
status "preparing worktree"
retry git -C "$ROOT" fetch -q origin "$BRANCH" || fail "git fetch origin $BRANCH не удался"
TARGET=$(git -C "$ROOT" rev-parse "refs/remotes/origin/$BRANCH") || fail "нет ветки origin/$BRANCH"
if [ -d "$WT" ]; then
  [ -z "$(git -C "$WT" status --porcelain --untracked-files=no)" ] \
    || fail "В $WT есть незакоммиченные правки, прогон остановлен. Разберитесь с ними вручную."
  GIT_LFS_SKIP_SMUDGE=1 git -C "$WT" checkout -q --detach "$TARGET" || fail "git checkout в $WT не удался"
else
  GIT_LFS_SKIP_SMUDGE=1 git -C "$ROOT" worktree add -q --detach "$WT" "$TARGET" || fail "git worktree add $WT не удался"
fi
retry git -C "$WT" lfs pull || fail "git lfs pull в $WT не удался"
COMMIT=$(git -C "$WT" rev-parse --short HEAD)

VERSION=$(sed -n 's/^m_EditorVersion: *//p' "$WT/ProjectSettings/ProjectVersion.txt")
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
[ -x "$UNITY" ] || fail "Не найден Unity $VERSION: $UNITY"

# В этом worktree не должен быть открыт Unity (редактор держит папку).
if pgrep -if -- "-projectpath $WT" >/dev/null; then
  fail "В $WT уже открыт Unity. Закройте его и запустите прогон снова."
fi

# 2. Замок.
lock_is_stale() {
  local pid upid
  pid=$(lock_field pid); upid=$(lock_field unity_pid)
  if [ -n "$pid" ]; then
    ! alive "$pid" && ! alive "$upid"
  else
    # Замок старого образца (без pid) или ещё создаётся: брошенный, если ему больше 5 минут
    # и ни один Unity не запущен.
    [ -n "$(find "$LOCK" -maxdepth 0 -mmin +5 2>/dev/null)" ] \
      && ! pgrep -f 'Unity.app/Contents/MacOS/Unity' >/dev/null
  fi
}

while :; do
  if mkdir "$LOCK" 2>/dev/null; then
    HAVE_LOCK=1
    echo "$NAME ($BRANCH @ $COMMIT)" > "$LOCK/owner"
    echo "$$" > "$LOCK/pid"
    date '+%F %T' > "$LOCK/started"
    break
  fi
  if lock_is_stale; then
    echo "Снимаю брошенный замок: $(lock_field owner), pid $(lock_field pid)"
    mv "$LOCK" "$LOCK.stale.$$" 2>/dev/null && rm -rf "$LOCK.stale.$$"
    continue
  fi
  status "waiting for lock: $(lock_field owner)"
  sleep "$POLL"
done

# 3. Прогон режимов.
run_mode() {
  local mode=$1 xml="$OUT/$1.xml" log="$OUT/$1.log" start limit
  rm -f "$xml" "$log"
  status "running $mode"
  "$UNITY" -batchmode -projectPath "$WT" -runTests -testPlatform "$mode" \
    -testResults "$xml" -logFile "$log" &
  UNITY_PID=$!
  echo "$UNITY_PID" > "$LOCK/unity_pid"
  start=$(date +%s); limit=$((TIMEOUT_MIN * 60))
  while alive "$UNITY_PID"; do
    if [ $(( $(date +%s) - start )) -ge "$limit" ]; then
      kill "$UNITY_PID" 2>/dev/null; sleep 10; kill -9 "$UNITY_PID" 2>/dev/null
      echo "$mode: остановлен по тайм-ауту $TIMEOUT_MIN мин" >> "$OUT/summary.tmp"
      break
    fi
    sleep 5
  done
  wait "$UNITY_PID" 2>/dev/null
  UNITY_PID=""
  rm -f "$LOCK/unity_pid"
}

attr() { sed -n "s/.*<test-run[^>]* $1=\"\\([^\"]*\\)\".*/\\1/p" "$2" | head -1; }

summarize_mode() {
  local mode=$1 xml="$OUT/$1.xml" log="$OUT/$1.log"
  if [ -s "$xml" ]; then
    echo "$mode: $(attr result "$xml"), всего $(attr total "$xml"), прошло $(attr passed "$xml"), упало $(attr failed "$xml"), пропущено $(attr skipped "$xml")"
    # Упавшие тесты: имя и первые строки сообщения.
    perl -0777 -ne 'while (/<test-case\b[^>]*?fullname="([^"]*)"[^>]*?result="Failed"[^>]*>(.*?)<\/test-case>/gs) {
        my ($n, $b) = ($1, $2); my ($m) = $b =~ /<message><!\[CDATA\[(.*?)\]\]><\/message>/s; $m //= "";
        my @l = split /\n/, $m; print "  FAIL $n\n"; print "    $_\n" for @l[0 .. ($#l < 4 ? $#l : 4)]; }' "$xml"
  else
    echo "$mode: нет файла результатов (Unity не дошёл до тестов)"
  fi
  if [ -f "$log" ] && grep -q 'error CS[0-9]' "$log"; then
    echo "  Ошибки компиляции:"
    grep 'error CS[0-9]' "$log" | sort -u | head -30 | sed 's/^/    /'
  fi
}

rm -f "$OUT/summary.tmp"
for MODE in $MODES; do run_mode "$MODE"; done

# 4. Замок снимаем сразу после Unity, итог пишем уже без него.
rm -rf "$LOCK"; HAVE_LOCK=0

{
  echo "Ветка $BRANCH @ $COMMIT, worktree $WT, $(date '+%F %T')"
  for MODE in $MODES; do summarize_mode "$MODE"; done
  [ -f "$OUT/summary.tmp" ] && cat "$OUT/summary.tmp"
  echo "Логи: $OUT"
} > "$OUT/summary.txt"
rm -f "$OUT/summary.tmp"
cat "$OUT/summary.txt"

OK=1
for MODE in $MODES; do
  [ "$(attr result "$OUT/$MODE.xml" 2>/dev/null)" = "Passed" ] || OK=0
done
if [ "$OK" = 1 ]; then status "passed"; exit 0; else status "failed"; exit 1; fi
