# PBReplacer assets

コードとは履歴を共有しない orphan ブランチ `assets`。ショップ用画像とアイコンを管理する。
パッケージのリリース（zip / unitypackage）には含まれない。README 用の画像は `main` 側の `Docs~/images` にある。

## 構成

```
icon/   アイコン一式（icon_* = 背景付き、symbol_* = シンボルのみ。tier はサイズ帯）
shop/   BOOTH 用画像 5 枚（1200x1200 PNG）
  src/  画像の元になる HTML / CSS、切り出し済みスクリーンショット、同梱フォント
    img/    ページが参照する画像
    shots/  Unity から撮影したウィンドウ全体（切り出し前）
```

## 画像の再生成

`shop/src/*.html` を編集して、以下で書き出す（Node.js が必要。初回は Playwright の Chromium をダウンロードする）。

```bash
bash shop/src/render.sh 01_thumbnail 02_before_after 03_features 04_usage 05_pbremap
```

## スクリーンショットの撮り方

`shop/src/shots/` の画像は Unity 2022.3 で PBReplacer のウィンドウを 1000x640 の浮動ウィンドウとして開き、
アバター（HAOLAN）を読み込んだ状態 / 再配置後 / PBRemap の Inspector・SceneView・Hierarchy を撮影したもの。
再撮影するときは同じサイズで撮ると、HTML 側のレイアウトをそのまま使える。

- コマンドラインから Unity を起動するときは、作業ディレクトリを Unity のインストール先（`Editor/`）にする。
  それ以外だとエディタ UI のシェーダーが読み込めず画面がマゼンタになる。
- SceneView の対応線は PB Remap Inspector の 👁 で表示する。撮影前に選択を外すと PhysBone のギズモが重ならない。

## クレジット

スクリーンショットのアバターは「-ハオラン-HAOLAN【オリジナル3Dモデル】」（かなﾘぁさんち）。
