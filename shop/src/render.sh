#!/bin/bash
# 使い方: bash render.sh 01_thumbnail 02_before_after 03_features 04_usage 05_pbremap
# 出力は shop/ 直下に 1200x1200 の PNG。Playwright (npx) が必要。
S="$(cd "$(dirname "$0")" && pwd)"
for f in "$@"; do
  npx -y playwright@1.49.1 screenshot --viewport-size=1200,1200 --wait-for-timeout=1200 "file:///$S/$f.html" "$S/../$f.png" >/dev/null 2>&1
  ls -la "$S/../$f.png"
done
