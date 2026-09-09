# メインウィンドウ再設計（3.0.0-beta.9）

PBRemap Inspector の再設計（`PBRemap-Redesign.md` §6, §7）と同じ 7 原則・同じ語彙でメインウィンドウを作り直した記録。
検討した 3 案（A: 横タブ＋1リスト / B: 縦タブ＋多列 / C: PBRemap 語彙への統合）のうち、C 案にレールのアイコン化（案 i）を組み合わせたものを採用した。

## 1. 現行（beta.8 まで）の問題

| # | 問題 | 影響 |
|---|---|---|
| 1 | アバター欄のラベルが「Avater」。ラベルが欄の上に積まれ 1 行を消費 | 誤字、縦の無駄 |
| 2 | ⚙ ⋮ が absolute 配置でアバター欄の右端に重なる | 押し間違い |
| 3 | 縦書きタブ。未処理の印が 5px の赤い点（Toggle の流用） | 件数が読めない。赤＝危険の慣習と衝突 |
| 4 | リスト見出しが内部名（PBList / PBCList / ReciverList）。処理済みはグレーアウトのみ | 状態が色以外で判別できない |
| 5 | Constraint タブは 6 リストを 3×2 で固定配置 | 空でも面積を取り、最小サイズ 650×450 を強いる |
| 6 | Apply が「現在のタブだけ」に効くことが画面から読めない | 他カテゴリの処理漏れ |
| 7 | USS で全文字 Bold、Toggle が緑、ラベル focus が青塗り。タブ色が C# のダーク固定値 | ライトテーマ非対応、階層感の喪失 |
| 8 | 設定ウィンドウが別窓で明示保存。「Prefabを継承」は実装（完全 Unpack）と逆の印象 | 誤解 |

## 2. 採用した設計（C 案 ＋ 案 i）

```
[↻] [↶] [⚙] [⋮]                                  ← ツール行（アイコンのみ、説明はツールチップ）
┌──────────────────────────────────────────────┐
│ [A] Sakura_v2        ──( 再配置 22 → )──  [▣] AvatarDynamics │  ← 流れ。左ノード=アバター（ドロップ先）
│     VRC Avatar Descriptor · Armature 内 31 件         配置済み 6 件 │     中央=ピル（主操作）、右ノード=配置先
└──────────────────────────────────────────────┘
┌──┬───────────────┬───────────────┬──────────┬──────────┐
│PB│ PhysBone 15/18  ＋ │ Collider 7/9  ＋ │ Constraint │ Contact  │  ← 列見出し: アイコン + 名前 + 未処理/全 + ドロップ先
│15│ ○ Hair_Front       │ ○ Head          │ ○ Bag_Pos  │ ○ Hand_L │
│C │ ○ Hair_Back_L      │ ○ Chest         │            │          │  ← 行頭 ○ 未処理 / ✔ 配置済み
│7 │ …                  │ …               │            │          │
│⊕ │ ▸ ✔ 3              │ ▸ ✔ 2           │            │ ▸ ✔ 1    │  ← 配置済みは列末尾に畳む
│3 │                    │                 │            │          │
│◉ │                    │                 │            │          │
│✔ │                    │                 │            │          │
└──┴───────────────┴───────────────┴──────────┴──────────┘
 ↑ レール: 28px のアイコンチップ＋右下バッジ。枠色 琥珀=未処理あり / 緑=すべて✔ / 無彩色=0件
```

### 2.1 原則との対応

| 原則 | 対応 |
|---|---|
| 1. 説明なしで使える | 主操作は流れ中央のピル 1 つ。空状態は左ノードの点線枠がドロップ先を示す。状態文字列のツールバーと完了ダイアログを廃止 |
| 2. 気持ちよさ | 成功時に流れが緑に光って戻る（PBRemap と同じ transition）。確認ダイアログは既定オフ（Undo 可能）。↶ で直前の再配置を戻せる |
| 3. 必要最小限 | レールの文字を削除（列見出しと重複）、ListView 10 個→カテゴリ 4 列、設定ウィンドウ廃止、進捗バー設定を UI から削除 |
| 4. 誤解ゼロ | ○ / ✔ の記号と枠色の二重コード（beta.10 で → を ○ に変更: ▶ が畳み記号に見えるため）。赤はバッチ失敗だけ。Unpack には「元に戻せません」。ピルのラベルは「再配置 n」で対象件数を明示 |
| 5. アイコン圧縮 | カテゴリはコンポーネント型のスクリプトアイコン（Inspector / Add Component と同じ記号）。無ければ内蔵型で代替（レイアウトは常に案 i）。ホバーで名前と件数のツールチップ |
| 6. 共通認識 | Console のフィルタチップ（クリックで表示切替、Alt+クリックで Solo）、Prefab の Linked、Hierarchy からのドロップ、内蔵アイコン（Refresh / back / Settings / Valid / forward / FolderEmpty / Prefab） |
| 7. 向きと動作 | 左→右＝再配置の向き（PBRemap の移植元→移植先と同一）。ドロップ先は左ノード（アバター）と列（コンポーネント追加） |

### 2.2 流れ（strip）の状態

| 状態 | 左ノード | 線 | 中央 | 背景 |
|---|---|---|---|---|
| アバター未設定 | 点線枠 + Unlinked | 無彩色 | ピル無効「再配置」 | 無彩色 |
| 読み込み中 / 処理中 | アバター | 無彩色 | ピル無効 | 無彩色 |
| 未処理あり | アバター | 琥珀 | ピル緑「再配置 n」（表示中カテゴリが一部なら琥珀） | 琥珀 |
| すべて配置済み | アバター | 緑 | Linked ✔（ピル非表示） | 緑 |
| 成功直後 | — | — | — | 緑に光って戻る（0.45s） |
| エラー | アバター | 無彩色 | エラー記号（ツールチップに内容） + ピル | 赤 |

### 2.3 一括処理

表示中のカテゴリが属する処理グループ（0=PhysBone+Collider, 1=Constraint, 2=Contact）をまとめて 1 つの `CompositeCommand` に包み、1 回の Undo で戻せる。
PhysBone と Collider は参照解決のため常に同じグループで処理する（Collider の列を隠していても PhysBone を処理すれば Collider も処理される。ツールチップで明記）。

## 3. 検証結果（設計を変えた点）

| 検証項目 | 結果 |
|---|---|
| 行の ✖（個別の失敗）状態 | `ComponentProcessor` はバッチ全体の例外しか返さず、RootTransform 未設定は自動補完される。行の記号は → / ✔ の 2 種に削減し、赤は流れ（バッチ失敗）だけに残した |
| アイコンの入手性 | SDK が固有アイコンを持つかは実機依存。名前文字列ではなく `AssetPreview.GetMiniThumbnail(component)` で型から取得し、既定のスクリプトアイコンと同じなら内蔵型（HingeJoint / CapsuleCollider / ParentConstraint / SphereCollider）で代替。レイアウトは常に案 i（beta.10 で 2 段表示への切替を廃止） |
| 一覧性 | 650×450 で列の高さ ≈ 340px、行高 20px で約 17 行 × 最大 4 列。最小サイズ 600×400 |
| ライトテーマ | 内蔵アイコンは `PBRemapIcons.Get` が d_ の有無を切り替える。色はダーク前提の白系オーバーレイのままでは線・枠・琥珀の注記が読めなかったため、beta.10 でルートの `pbr-theme-light` クラスによる USS 上書きを追加（§5.2） |
| ドロップ | `ListViewDragHandler`（一時コンポーネントでプレビューする方式）を廃止し、列ルートで TrickleDown で受ける `ColumnDropHandler` に置換。Constraint / Contact は型をメニューで選ぶ |

## 4. 実装

- 共通 USS: `Editor/Resources/USS/PBReplacerCommon.uss`（`PBRemap.uss` から strip / node / apply / chip / icon-button / advanced を移動。`.pbremap-*` と `.pbr-*` の両名で提供し PBRemap 側は無変更）
- メインウィンドウ: `Editor/Resources/UXML/PBReplacer.uxml`（＋列テンプレート `PBReplacerColumn.uxml`、行テンプレート `PBReplacerRow.uxml`）, `USS/PBReplacer.uss`, `Editor/Scripts/UI/Windows/PBReplacerWindow.{cs,UI.cs,Columns.cs,Events.cs}`。構成要素は UXML、C# は内蔵アイコン・イベント・データのバインドのみ
- カテゴリ: `Editor/Scripts/Models/ComponentCategory.cs`（表示名 / 型 / 代替アイコン型 / 処理グループ）
- アイコン: `Editor/Scripts/Utilities/ComponentIconUtility.cs`
- ドロップ: `Editor/Scripts/UI/Handlers/ColumnDropHandler.cs`（アバターは既存 `AvatarFieldDropManipulator`。ピッカー用に `ResolveAvatarComponent` を公開）
- 削除: `SideBar.uxml/.uss`, `VerticalTabContainer/Element`, `GroupBoxUtility` 系, `ListViewDragHandler`, `PBReplacerSettingsWindow.cs/.uxml`（`Tools/PBReplacer/Settings` はメインウィンドウの ⚙ を開く導線として維持）
- 設定: `ShowConfirmDialog` の既定を false に（メインウィンドウの再配置と PBRemap の移植で共通。EditorPrefs に保存済みの値は尊重される）

## 5. 実機検証（Unity 2022.3.22f1 / VRChat SDK 3.10.4, Linux + Xvfb）

- コンパイル: エラーなし。EditMode テスト 6 件（`Tests/Editor/PBReplacerWindowTests.cs`）すべて合格
- SDK の VRCPhysBone 等は固有アイコンを持たず既定の「dll Script Icon」だった。型ベース判定が代替（HingeJoint / CapsuleCollider / ParentConstraint / SphereCollider）を選び、レールは 2 段表示になる
- 未設定 / 未処理あり / 全件配置済み / ⚙ 展開 の 4 状態を Xvfb 上で描画して確認（流れの色・ピル・レールのバッジ・列の ✔ 畳み込みが設計どおり）

## 5.1 beta.10 での差異解消

- レールは SDK アイコンの有無にかかわらず案 i（28px＋右下バッジ）。beta.9 では VRChat SDK に固有アイコンが無いため常に 2 段表示（案 ii）になっていた
- 行のホバー操作（Hierarchy で表示 / 削除）を追加。右クリックメニューは維持
- フォント適用を USS の `.unity-text-element` 指定からルートへのインライン適用（`PBReplacerFonts.Apply`）に変更。UITKFontFix 導入時は OS フォントを優先し、無ければ同梱 Noto Sans JP

## 5.2 beta.10 ライトテーマ

ライトスキン（`UserSkin=0`）で描画すると、線 rgba(255,255,255,.18)・空ノードやチップの白系の枠・レールの白系オーバーレイが背景に溶け、「元に戻せません」の琥珀 rgb(220,180,50) と「初期値に戻す」の青 rgb(90,160,255) が読めなかった。ピルの塗り（緑 / 琥珀）と内蔵アイコン（d_ 無し版）は問題なし。

対応: `PBReplacerTheme.Apply(root)` が `EditorGUIUtility.isProSkin` が false のときルートに `pbr-theme-light` を付け、USS 側で色だけを上書きする（構造・寸法は共通）。

| 要素 | ダーク | ライト |
|---|---|---|
| strip 背景 | rgba(0,0,0,.12) / home 緑 .08 / displaced 琥珀 .10 | rgba(0,0,0,.06) / home rgba(40,150,80,.12) / displaced rgba(210,150,0,.14) |
| 線 | rgba(255,255,255,.18) / 琥珀 .7 / 緑 .55 | rgba(0,0,0,.22) / rgba(200,140,0,.85) / rgba(40,150,80,.75) |
| 空ノード・ドロップ枠 | rgba(255,255,255,.28) | rgba(0,0,0,.30) |
| チップ・レールチップ | 枠 白 .12 / 地 白 .04 | 枠 rgba(0,0,0,.20) / 地 白 .45〜.55（レールの黒 .06 の上に浮かせる） |
| レールのバッジ | rgb(58,58,58) + 白 .25 | rgb(238,238,238) + 黒 .30。pending は rgb(175,120,20)、done は rgb(30,120,50) |
| 注記（琥珀）/ リンク（青） | rgb(220,180,50) / rgb(90,160,255) | rgb(160,105,0) / rgb(20,90,200) |
| 列見出し / 配置済み見出し行 | 白 .04 / 黒 .18 | 黒 .04 / 黒 .07 |
| PBRemap の行（unresolved / ambiguous / auto）、プレビューの赤・琥珀文字 | rgb(220,80,80) / rgb(220,180,50) | rgb(190,40,40) / rgb(160,105,0) |

実機検証（Xvfb、ライト）: 未設定 / 未処理あり / 全件配置済み / ⚙ 展開 の 4 状態を描画して確認。ダークは変更なし（同じ状態で再描画して確認）。EditMode テストはライト・ダークの両方で 13 件合格。

## 5.3 beta.10 設定のスイッチと行頭の印

- ⚙ 詳細設定の Toggle は標準のチェックボックスだと Console 風のチップ・ピルの中で浮いていたため、`pbr-switch` クラスでスイッチ（30×16 のレール＋12px のノブ、オンは再配置ピルと同じ緑）にした。Toggle の `__input` をレール、`__checkmark` をノブとして USS だけで描く。構造・C# は不変
- 詳細設定の行は `pbr-setting`（Toggle / EnumField にそのまま付ける）で「ラベルが伸びて操作を右端で揃える」1 行 22px に統一。見出しは「プロジェクト」「この PC のみ」の 2 語にし、保存先・共有の説明はツールチップへ。Prefab Unpack の「元に戻せません」は Unpack 自体が Undo 可能なため誤解を招くので削除し、Prefab との接続が切れることをツールチップで説明
- 行頭の未処理の印 ▶（forward アイコン）は畳み記号 ▸ と紛らわしいので、琥珀のリング ○（USS の border だけで描く。画像なし）に変更。琥珀=未処理 / 緑 ✔=配置済み の色コードはレール・流れと同じ

## 6. 未決（後続）

- Hierarchy の未処理コンポーネント行への → バッジ（`PBRemapHierarchyBadge` と同じ仕組みで追加可能）
- 複数選択中の「選択した n 件だけ再配置」（B 案で検討。ProcessXxxCommand が対象指定を受けるようになれば追加）
