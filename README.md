# PBReplacer
Version 1.3.0

## 概要
* アバターに付いているPhysBoneコンポーネントとPhysBoneColliderコンポーネントを整理するUnity拡張です。
* 元のコンポーネントのパラメーターを保持したまま、PhysBoneとPhysBoneColliderを1オブジェクト＝1コンポーネントに分けて再配置します。

## 使用用途
* ___複数コンポーネントの一括編集がしたい時。___

1オブジェクト＝1コンポーネントなのでHierarchy上で複数選択すると一括編集することが出来ます。

* __アニメーションでのオンオフを簡易化させたい時。__

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
* 元のコンポーネントがRootTransformと違う場所についていた場合、「コンポーネントが付いていたオブジェクト名＞RootTransformのオブジェクト名」となるように生成されます。

## 連絡先
Twitter @C_Colloid
