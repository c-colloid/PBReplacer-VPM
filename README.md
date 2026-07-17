![](https://img.shields.io/github/license/c-colloid/PBReplacer-VPM)
![](https://img.shields.io/github/package-json/v/c-colloid/PBReplacer-VPM?link=https%3A%2F%2Fgithub.com%2Fc-colloid%2FPBReplacer-VPM%2Freleases%2Flatest)
![](https://img.shields.io/github/release-date/c-colloid/PBReplacer-VPM)
![](https://img.shields.io/github/downloads/c-colloid/PBReplacer-VPM/total)

# PBReplacer

## 概要

アバターに付いているVRC関連コンポーネントを整理するUnity拡張です。

元のコンポーネントのパラメーターを保持したまま、

* PhysBone/PhysBoneCollider
* VRCContactSender/Receiver
* VRCConstraint全般

を1オブジェクト＝1コンポーネントに分けて再配置します。

## 使用用途

* **複数コンポーネントの一括編集がしたい時。**

1オブジェクト＝1コンポーネントなのでHierarchy上で複数選択すると一括編集することが出来ます。

* **アニメーションでのオンオフを簡易化させたい時。**

コンポーネントがボーンについていない為、好きな場所に移動させることができます。

服の子に付ければ服のオンオフアニメーションでPhysBoneも止めることができます。

## 使い方

1. 以下のいずれかの方法で拡張ウィンドウを表示

   * 1.1 ツールバーのTools>PBReplacerを選択
   * 1.2 Hierarchyで右クリックをしてPBReplacerを選択

2. 開いたウィンドウにアバターをドラッグ＆ドロップ
3. Applyボタンを押す

## その他仕様

* RootTransformが設定されていないものは自動補完します。
* オブジェクトの名称はRootTransformのオブジェクト名になります。
* ModularAvatarを導入してる場合、MA MargeArmatuaコンポーネントの付いた衣装などにも対応しています。

### PBRemap（移植機能）

あるアバターのAvatarDynamics配下（PhysBone等）を、別アバターへボーン構成の違いを吸収しながら移植する機能です。

1. 以下のいずれかの方法で移植先アバターのルートにPBRemapコンポーネントを追加

   * PBReplacerメインウィンドウ右上の「⋮」メニュー→「他のアバターへ移植 (PBRemap)...」（アバター読込済み時のみ選択可）
   * Hierarchyで対象オブジェクトを右クリック→「PBRemapを追加」（GameObjectメニューからも可）
   * Add Componentから「PBReplacer/PB Remap」を検索して追加

2. InspectorでPBRemapコンポーネントを開き、移植元/移植先アバターが自動検出されていることを確認（検出できない場合は手動指定も可能）
3. 「プレビュー」でボーンのマッピング結果を確認
4. 「移植実行」を押して移植を反映

# 注意

* オプションでその他オブジェクトを読み込む機能もありますが、動作の保証は致しかねます。

## 連絡先

Twitter @C\_Colloid
