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

2. 開いたウィンドウ上部の左ノードにアバターをドラッグ＆ドロップ（クリックして選択も可）
3. 真ん中の「再配置 n →」ボタンを押す（n は未処理の件数。Ctrl+Z か ↶ で元に戻せます）

ウィンドウの見方:

* 上段の流れ `[アバター] ──(再配置 n →)── [AvatarDynamics]` が状態を示します。線が琥珀=未処理あり、緑で ✔ =すべて配置済み、赤=エラー（Console に詳細）
* 左のレールはカテゴリ（PhysBone / PhysBone Collider / Constraint / Contact）のアイコン。右下の数字が未処理件数で、クリックで列の表示切替、Alt+クリックでその列だけ表示
* 各列は行頭の → が未処理、✔ が配置済み（列末尾に畳めます）。Hierarchy のオブジェクトを列へドロップするとそのカテゴリのコンポーネントを追加できます
* 右上の ↻ 再読み込み / ↶ 元に戻す / ⚙ 詳細設定（検索範囲・空フォルダ削除・Prefab の Unpack・自動読み込み・確認ダイアログ）/ ⋮ その他
* 行にマウスを乗せると「Hierarchy で表示」「削除」のボタンが出ます（右クリックでも同じ操作ができます）

### フォント（任意）

UI の日本語は同梱の Noto Sans JP で表示します。[UITKFontFix](https://github.com/c-colloid/UITKFontFix)（`jp.colloid.uitk-font-fix`）を導入すると、OS が日本語/中国語/韓国語のときは OS のフォント（Yu Gothic UI / Meiryo / Noto Sans CJK）で表示されます。Package Manager の「Add package from git URL...」に `https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-fix` を指定してください。

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
   - 「AutoOnDrop」にすると、ドロップした時点で自動で移植します（Project からの Prefab ドロップやペーストも対象）。対応先の候補が複数ある参照がある場合だけ保留して選択を促し、移植先に対応物が無い参照は移植元を指したまま残して Console に警告します
   - 「BuildOnly」にすると編集時には何もせず、NDMF ビルド（再生）時に非破壊で移植します。Hierarchy と Inspector には ▶ が出て「ビルド時に移植される」ことを示します。VRC アバター配下ではない置き場所（単体の衣装・小物・Animator だけのオブジェクト）は NDMF が処理しないため、再生開始時に PBRemap 自身が非破壊で移植します（VRChat へのビルドには含まれません）
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
