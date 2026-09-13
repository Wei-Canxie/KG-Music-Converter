#!/usr/bin/env bash
# 构建三个发版变体（对标 OsuCursorPatcherWin v1.1.1 的三个 Built 版）
#
#   1. KGMusicConverter.exe                              Self-contained 单文件（零依赖，体积最大）
#   2. KGMusicConverter-dotnet-only.zip                  仅需 .NET 8（WinAppSDK 已内置，推荐）
#   3. KGMusicConverter-full-framework-dependent.zip     完全框架依赖（需 .NET 8 + WinAppSDK Runtime，最小）
#
# 用法：bash scripts/build-release.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/build-out"

# ── 路径形式：dotnet / python 是原生程序，而 MSYS 不做路径转换 ──
# 传 /c/Users/... 会被 MSBuild 当成开关（MSB1001: Unknown switch），
# 所以给它们的一律是 C:/Users/... 形式；bash 自己的 mkdir/cp/ls 仍用 /c/... 。
ROOT_WIN="$(cygpath -m "$ROOT")"
OUT_WIN="$(cygpath -m "$OUT")"
PROJ="$ROOT_WIN/KGMusicConverter.UI/KGMusicConverter.UI.csproj"

echo "==> 清理旧产物"
rm -rf "$OUT"
mkdir -p "$OUT/selfcontained" "$OUT/dotnet-only" "$OUT/framework-dependent"

# 旧实例占用 exe 会让构建失败（MSYS 下必须写单斜杠 /F，写成 //F 会被判非法参数而静默失效）
taskkill /F /IM KGMusicConverter.exe >/dev/null 2>&1 || true

# ── publish 包装：不能把 dotnet 的输出直接管到 grep ──
# 管道的退出码是 grep 的，publish 失败会被 `|| true` 吞掉，产物是空目录却当成功继续往下走。
run_publish() {
  local label="$1" outdir="$2"; shift 2
  echo "==> $label"
  local log="$OUT/publish-$(basename "$outdir").log"
  if ! dotnet publish "$PROJ" "$@" -o "$outdir" > "$log" 2>&1; then
    echo "!! publish 失败（$label）—— 日志末尾："
    tail -20 "$log"
    exit 1
  fi
  local count
  count=$(find "$outdir" -type f | wc -l)
  if [ "$count" -lt 10 ]; then
    echo "!! 产物异常：只有 $count 个文件，视为失败"
    exit 1
  fi
  grep -E "error|warning CS" "$log" || true
  echo "    产物 $count 个文件"
}

run_publish "[1/3] Self-contained 单文件" "$OUT_WIN/selfcontained" \
  -c Release -p:Platform=x64 -r win-x64 \
  --self-contained true -p:WindowsAppSDKSelfContained=true \
  -p:PublishSingleFile=true -p:EnableMsixTooling=true

run_publish "[2/3] 仅需 .NET（WinAppSDK 内置）" "$OUT_WIN/dotnet-only" \
  -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false -p:WindowsAppSDKSelfContained=true

run_publish "[3/3] 完全框架依赖" "$OUT_WIN/framework-dependent" \
  -c Release -p:Platform=x64 -r win-x64 \
  --self-contained false -p:WindowsAppSDKSelfContained=false

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

echo "==> 打包 zip（排除 .pdb 与本次构建日志）"
python - "$OUT_WIN" "$ROOT" <<'PY'
import os, sys, zipfile
out_win, root = sys.argv[1], sys.argv[2]
for name in ("dotnet-only", "framework-dependent"):
    src = os.path.join(out_win, name)
    dst = os.path.join(out_win, f"KGMusicConverter-{name}.zip")
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
