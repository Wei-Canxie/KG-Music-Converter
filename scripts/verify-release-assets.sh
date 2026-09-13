#!/usr/bin/env bash
# 发版前最终验证：把 Release 资产当成用户拿到的东西来跑
# 注意：python 是原生程序，必须喂 C:/... 形式（MSYS 不转换路径，/c/... 会 FileNotFoundError）
set -u
ROOT="/c/Users/The_R/Desktop/Kugo-Music-Converter"
OUT="$ROOT/build-out"
T="/c/Users/The_R/AppData/Local/Temp/kgm-release-check"
OUT_WIN="$(cygpath -m "$OUT")"
T_WIN="$(cygpath -m "$T")"

rm -rf "$T"; mkdir -p "$T/framework-dependent"
python -c "
import zipfile
zipfile.ZipFile(r'$OUT_WIN/KGMusicConverter-framework-dependent.zip').extractall(r'$T_WIN/framework-dependent')
print('  解压完成')
"
echo "  解压后文件数: $(ls "$T/framework-dependent" | wc -l)，引擎文件: $(ls "$T/framework-dependent" | grep -cE 'unlock|kgg-dec|mask')"

python - "$OUT_WIN/KGMusicConverter.exe" "$T_WIN/framework-dependent/KGMusicConverter.exe" <<'PY'
import os, subprocess, sys, time
log = os.path.join(os.environ["LOCALAPPDATA"], "Temp", "KGMusicConverter.log")
dumpdir = os.path.join(os.environ["LOCALAPPDATA"], "CrashDumps")

for label, exe in (("单文件 exe（release 资产）", sys.argv[1]),
                   ("框架依赖 zip（解压后）", sys.argv[2])):
    if not os.path.isfile(exe):
        print(f"  {label}: 找不到 {exe}"); continue
    d = os.path.dirname(exe)
    before = set(os.listdir(dumpdir))
    off = os.path.getsize(log)
    p = subprocess.Popen([exe], cwd=d, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    alive = False
    try:
        p.communicate(timeout=13)
    except subprocess.TimeoutExpired:
        alive = True
        p.terminate()
        try: p.wait(timeout=5)
        except Exception: p.kill()
    time.sleep(1)
    delta = open(log, encoding="utf-8", errors="replace").read()[off:]
    print(f"  {label}: 存活13秒={alive}  启动横幅={'启动' in delta}  "
          f"引擎就绪={'解密引擎: 就绪' in delta}  新崩溃转储={len(set(os.listdir(dumpdir)) - before)}")
PY

rm -rf "$T"
echo "（临时解压目录已清理）"
