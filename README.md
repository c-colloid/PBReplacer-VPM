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

### PBRemapコンポーネント

VRC関連コンポーネントを他のアバターに移植する為のコンポーネントです。
* このゲームオブジェクトの子に配置してあるVRC関連コンポーネントを収集し、このゲームオブジェクトをD＆Dすることで他のアバターに移植できます。
* Prefab化することで他のシーンで利用することも可能です。

# 注意

* オプションでその他オブジェクトを読み込む機能もありますが、動作の保証は致しかねます。

## 連絡先

Twitter @C\_Colloid
