#!/usr/bin/env bash
# 启动 Debug 版应用（分离运行，不阻塞终端）
set -u
ROOT="/c/Users/The_R/Desktop/Kugo-Music-Converter"
EXE="$ROOT/KGMusicConverter.UI/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/KGMusicConverter.exe"
[ -f "$EXE" ] || { echo "找不到 exe: $EXE"; exit 1; }
"$EXE" >/dev/null 2>&1 &
sleep 6
tasklist /FI "IMAGENAME eq KGMusicConverter.exe" 2>&1 | tail -2
