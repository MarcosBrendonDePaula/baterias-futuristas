#!/usr/bin/env bash
# Usage: run_autotest.sh <source.sav> [timeout seconds]. Runs the in-game test suite on a copy of the save.
set -u
SOURCE="$1"
TIMEOUT="${2:-420}"
MOD="$HOME/Documents/Klei/OxygenNotIncluded/mods/Local/BateriasFuturistas"
RESULT="$MOD/autotest_result.txt"
COPY="$MOD/autotest/AutoTest.sav"

if tasklist | grep -qi OxygenNotIncluded; then
    echo "ERRO: feche o jogo antes do teste"
    exit 2
fi
( cd "$(dirname "$0")/.." && dotnet build -c Dev -v q ) || exit 1
mkdir -p "$MOD/autotest"
rm -f "$RESULT"
cp "$SOURCE" "$COPY"
cygpath -m "$COPY" > "$MOD/autotest.flag"
GAME_DIR='D:/SteamLibrary/steamapps/common/OxygenNotIncluded'
SteamAppId=457140 SteamGameId=457140 "$GAME_DIR/OxygenNotIncluded.exe" -screen-fullscreen 0 -window-mode windowed &

START=$(date +%s)
while [ ! -f "$RESULT" ] && [ $(( $(date +%s) - START )) -lt "$TIMEOUT" ]; do
    sleep 3
done
sleep 6
if [ -f "$RESULT" ]; then
    cat "$RESULT"
else
    echo "ERRO: sem resultado em ${TIMEOUT}s"
    rm -f "$MOD/autotest.flag"
fi
tasklist | grep -i OxygenNotIncluded || echo "jogo fechado"
