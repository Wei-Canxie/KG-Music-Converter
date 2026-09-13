#!/usr/bin/env bash
# 发布/替换一个 GitHub Release：
#   删旧 release+tag → 在 main 当前提交打新 tag → 推 main/alpha/tag → 上传资产 → 核对
#
# 用法：bash scripts/publish-release.sh [tag] [标题]
# 注意：git/gh 的远程操作必须带 socks5 代理（本机直连 github.com:443 不通），
#       且 -c 参数要写在这条命令里（存进变量再展开会被拆掉、静默失败）。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TAG="${1:-v1.0.0}"
TITLE="${2:-$TAG — WinUI 3 桌面版}"
NOTES="release-notes-$TAG.md"
ASSETS=("build-out/KGMusicConverter.exe" "build-out/KGMusicConverter-framework-dependent.zip")

export HTTPS_PROXY=socks5://127.0.0.1:10888

echo "==> 资产检查"
for a in "${ASSETS[@]}"; do
  if [ ! -f "$a" ]; then echo "!! 缺少资产 $a（先跑 scripts/build-release.sh）"; exit 1; fi
  printf '  %-52s %s 字节\n' "$a" "$(stat -c %s "$a")"
done
[ -f "$NOTES" ] || { echo "!! 缺少说明文件 $NOTES"; exit 1; }

echo "==> 删除旧 release / tag"
gh release delete "$TAG" --yes --cleanup-tag 2>/dev/null && echo "  远端 release 已删" || echo "  （远端没有该 release）"
git tag -d "$TAG" 2>/dev/null && echo "  本地 tag 已删" || echo "  （本地没有该 tag）"

echo "==> 打 tag"
git tag -a "$TAG" -m "$TITLE" || exit 1
git log --oneline -1

echo "==> 推送 main / alpha / tag"
git -c http.proxy=socks5h://127.0.0.1:10888 -c credential.helper='!gh auth git-credential' push origin main || exit 1
git -c http.proxy=socks5h://127.0.0.1:10888 -c credential.helper='!gh auth git-credential' push origin alpha || exit 1
git -c http.proxy=socks5h://127.0.0.1:10888 -c credential.helper='!gh auth git-credential' push origin "$TAG" || exit 1

echo "==> 创建 release 并上传资产"
gh release create "$TAG" --title "$TITLE" --notes-file "$NOTES" "${ASSETS[@]}" || exit 1

echo "==> 核对远端 ref"
git -c http.proxy=socks5h://127.0.0.1:10888 ls-remote --heads origin
git -c http.proxy=socks5h://127.0.0.1:10888 ls-remote --tags origin

echo "==> 核对 release 资产"
gh release view "$TAG" --json tagName,name,assets \
  --jq '.tagName, .name, (.assets[] | "  \(.name)  \(.size) 字节  \(.state)")'
echo "完成。"
