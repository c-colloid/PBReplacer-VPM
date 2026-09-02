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

あるアバター/衣装/小物のAvatarDynamics配下（PhysBone等）を、別のアバター/衣装/小物へボーン構成の違いを吸収しながら移植する機能です。
**AvatarDynamicsのGameObjectを移植先へドラッグ＆ドロップするだけ**で移植できます。

1. 移植元でPBReplacerのApplyを実行し、AvatarDynamics階層を作ります
2. PBReplacerメインウィンドウ右上の「⋮」→「他のアバターへ移植 (PBRemap)...」を選ぶと、AvatarDynamicsにPBRemapコンポーネントが付き、移植元のボーン参照情報（マニフェスト）が自動保存されます
   （Add Component から「PBReplacer/PB Remap」を付けても同じです。Inspectorを開かなくても参照情報はバックグラウンドで保存されます）
3. AvatarDynamics（PBRemap付き）を移植先アバターの子階層へドラッグ＆ドロップします
   - Inspector の上部に「移植元 → 移植先」の流れが表示されます。真ん中の **→ 移植** ボタンを押すと移植されます（Ctrl+Z で取り消せます）
   - Hierarchy の PBRemap 行にも状態アイコンが出ます（🔗 接続済み / → 移植できる / 参照切れ / 置き場所が認識できない）
   - 「AutoOnDrop」にすると、全ての参照が一意に解決できる場合はドロップ時に自動で移植します
   - 「BuildOnly」にすると編集時には何もせず、NDMFビルド時に非破壊で移植します
4. Prefab化して別シーン・別プロジェクトへ持ち出した場合も、保存済みの参照情報から解決できます

Inspector の見かた:

* 流れの行: 左が移植元、右が移植先（置き場所）。右のノードへ Hierarchy からアバター/衣装をドロップすると、その配下へ移動します
* チップ: ✔ 解決済み / ＋ 自動作成 / ⚠ 要選択 / ✖ 未解決 の件数（クリックで表の表示を切り替え）と、スケール比（クリックで 自動/手動/なし）
* 表: 問題のある行だけが出ます。右の欄へボーンをドロップすると手動で対応付け、⚠ の行は ▾ から候補を選べます
* ↻ 参照情報の取り直し / 👁 SceneView に対応線を表示 / ⚙ 詳細設定（ドロップ時の動作、名前の対応ルール、手動指定、参照情報）
* SceneView: 青の輪（移植元）→ 緑の点（移植先）を曲線と矢印で結びます。赤✕（対応先なし）をクリックすると「ボーン対応」ツールが起動し、移植先の骨に出る点をクリックして対応を決められます。琥珀▾の候補は輪をクリックで確定。オーバーレイのアイコンで線/名前/件数の絞り込みを切り替えます

衣装だけを移植する（アバター本体には触れない）:

* 衣装ルート（MergeArmature を持つ Armature の親）の配下に置いた AvatarDynamics は **衣装を単位** として扱われます。
  同じアバター内の衣装 v1 → v2、AvatarA に着せた衣装 → AvatarB に着せた同じ衣装、いずれも衣装の AvatarDynamics をドラッグするだけで、アバター本体のボーンや本体の AvatarDynamics には手を触れません
* 衣装ボーンへの参照は衣装同士の寸法比で、アバターボーンへの参照（帽子の Constraint など）はアバター同士の寸法比で補正されます
* 衣装 Prefab に AvatarDynamics を同梱して別のアバターへ置いた場合、失われたアバターボーン参照は参照情報から復元されます

対応している移植元/移植先:

* VRCAvatarDescriptor付きアバター（Humanoidボーンで対応付け）
* Modular Avatar の MergeArmature 付き衣装（prefix/suffixを考慮。着せ替え済みアバターでは本体・衣装それぞれのボーンを別コンテキストとして扱います）
* Animator/Descriptorの無い小物（SkinnedMeshRendererを持つオブジェクト。パス/名前で対応付け）

解決できないボーンは Inspector の表で手動指定（Transformをドロップ、または同名候補から選択）できます。
PhysBone等のradius/height等は「移植元の元値 × 世界寸法比 × 移植元/移植先ボーンのlossyScale比」で補正されるため、何度実行しても（NDMFビルドで再実行されても）二重に補正されません。

# 注意

* オプションでその他オブジェクトを読み込む機能もありますが、動作の保証は致しかねます。

## 連絡先

Twitter @C\_Colloid
