#!/usr/bin/env bash
# 发布"仅需 .NET"变体并连跑 3 次：验证启动崩溃（NavigationView + 异步解码）是否真修好
set -u
cd /c/Users/The_R/Desktop/Kugo-Music-Converter
export HTTPS_PROXY=socks5://127.0.0.1:10888

echo "=== 发布 ==="
dotnet publish "C:/Users/The_R/Desktop/Kugo-Music-Converter/KGMusicConverter.UI/KGMusicConverter.UI.csproj" \
  -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false -p:WindowsAppSDKSelfContained=true \
  -o "C:/Users/The_R/Desktop/Kugo-Music-Converter/build-out/dotnet-only" 2>&1 \
  | grep -E "error|->" | tail -2

L="/c/Users/The_R/AppData/Local/Temp/KGMusicConverter.log"
DUMP="/c/Users/The_R/AppData/Local/CrashDumps"
echo "=== 连跑 3 次（每次 9 秒）==="
for i in 1 2 3; do
  BEFORE=$(ls -t "$DUMP" 2>/dev/null | head -1)
  OFF=$(stat -c %s "$L")
  (cd build-out/dotnet-only && ./KGMusicConverter.exe >/dev/null 2>&1 &)
  sleep 9
  ALIVE=$(tasklist /FI "IMAGENAME eq KGMusicConverter.exe" 2>/dev/null | grep -c KGMusicConverter || true)
  BANNER=$(tail -c +$((OFF + 1)) "$L" | grep -c "启动" || true)
  FATAL=$(tail -c +$((OFF + 1)) "$L" | grep -c "FATAL" || true)
  AFTER=$(ls -t "$DUMP" 2>/dev/null | head -1)
  if [ "$BEFORE" = "$AFTER" ]; then NEWDUMP="无"; else NEWDUMP="有 → $AFTER"; fi
  echo "第 $i 次: 存活=$ALIVE 启动横幅=$BANNER FATAL=$FATAL 新转储=$NEWDUMP"
  taskkill /F /IM KGMusicConverter.exe >/dev/null 2>&1
  sleep 1
done
