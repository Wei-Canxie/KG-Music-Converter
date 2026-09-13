#!/usr/bin/env bash
# 构建三个发版变体（对标 OsuCursorPatcherWin v1.1.1 的三个 Built 版）
#
#   1. KGMusicConverter.exe                        Self-contained 单文件（零依赖，体积最大）
#   2. KGMusicConverter-dotnet-only.zip            仅需 .NET 8（WinAppSDK 已内置，推荐）
#   3. KGMusicConverter-full-framework-dependent.zip  完全框架依赖（需 .NET 8 + WinAppSDK Runtime，最小）
#
# 用法：bash scripts/build-release.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/KGMusicConverter.UI/KGMusicConverter.UI.csproj"
OUT="$ROOT/build-out"
RID_DIR="bin/x64/Release/net8.0-windows10.0.19041.0/win-x64"

echo "==> 清理旧产物"
rm -rf "$OUT"
mkdir -p "$OUT/selfcontained" "$OUT/dotnet-only" "$OUT/framework-dependent"

# 旧实例占用 exe 会让构建失败
taskkill //F //IM KGMusicConverter.exe >/dev/null 2>&1 || true

echo "==> [1/3] Self-contained 单文件"
dotnet publish "$PROJ" -c Release -p:Platform=x64 -r win-x64 \
  --self-contained true -p:WindowsAppSDKSelfContained=true \
  -p:PublishSingleFile=true -p:EnableMsixTooling=true \
  -o "$OUT/selfcontained" 2>&1 | grep -E "error|warning CS" || true

echo "==> [2/3] 仅需 .NET（WinAppSDK 内置）"
dotnet publish "$PROJ" -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false -p:WindowsAppSDKSelfContained=true \
  -o "$OUT/dotnet-only" 2>&1 | grep -E "error|warning CS" || true

echo "==> [3/3] 完全框架依赖"
dotnet publish "$PROJ" -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false -p:WindowsAppSDKSelfContained=false \
  -o "$OUT/framework-dependent" 2>&1 | grep -E "error|warning CS" || true

# ── 把解密引擎放进每个发布产物（程序首次运行会自动播种到工作区）──
ENGINES_DIR="${ENGINES_DIR:-$ROOT/Release_v0.2}"
if [ -f "$ENGINES_DIR/unlockKuGoWin-64.exe" ]; then
  echo "==> 复制解密引擎（来自 $ENGINES_DIR）"
  for target in "$OUT/selfcontained" "$OUT/dotnet-only" "$OUT/framework-dependent"; do
    mkdir -p "$target/kgm-vpr-out"
    for f in unlockKuGoWin-64.exe unlockKuGoWin-32.exe kgg-dec.exe kgm.mask; do
      [ -f "$ENGINES_DIR/$f" ] && cp "$ENGINES_DIR/$f" "$target/" || true
    done
    [ -f "$ENGINES_DIR/kgm-vpr-out/ffmpeg.exe" ] && cp "$ENGINES_DIR/kgm-vpr-out/ffmpeg.exe" "$target/kgm-vpr-out/" || true
  done
else
  echo "!! 未找到引擎目录 $ENGINES_DIR —— 产物将不含解密引擎（用户需自行放置）"
fi

echo "==> 打包 zip（排除 .pdb）"
python - "$OUT" <<'PY'
import os, sys, zipfile
out = sys.argv[1]
for name in ("dotnet-only", "framework-dependent"):
    src = os.path.join(out, name)
    dst = os.path.join(out, f"KGMusicConverter-{name}.zip")
    files = [os.path.join(r, n) for r, _, ns in os.walk(src)
             for n in ns if not n.endswith(".pdb")]
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for f in files:
            z.write(f, os.path.relpath(f, src))
    print(f"  {os.path.basename(dst)}: {os.path.getsize(dst)/1024/1024:.1f} MB ({len(files)} files)")
PY

cp "$OUT/selfcontained/KGMusicConverter.exe" "$OUT/KGMusicConverter.exe" 2>/dev/null || true

echo
echo "==> 产物清单"
ls -la "$OUT"/*.exe "$OUT"/*.zip 2>/dev/null | awk '{printf "  %-14s %s\n", $5, $9}'
echo "完成。"
