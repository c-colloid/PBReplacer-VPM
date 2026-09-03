# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

PBReplacerはVRChatアバター開発用のUnityエディタ拡張です。アバターのArmature内にあるVRC関連コンポーネント（PhysBone、PhysBoneCollider、VRCConstraint、VRCContact）を「AvatarDynamics」階層に1オブジェクト＝1コンポーネントとして再配置します。これにより複数選択での一括編集やアニメーションでのオンオフ制御が容易になります。

## 開発環境

- Unity Editor拡張（Editor-onlyコード）
- VPMパッケージ（VRChat Package Manager）
- 依存関係: `com.vrchat.avatars` (VRChat Avatars SDK)
- 名前空間: `colloid.PBReplacer`
- アセンブリ定義（Editor）: `jp.colloid.pbreplacer.asmdef`
- アセンブリ定義（Runtime）: `jp.colloid.pbreplacer.runtime.asmdef`（`autoReferenced: false`）

## アーキテクチャ

### ディレクトリ構造

```
Runtime/Scripts/           - Runtimeコード（MonoBehaviour、Serializableデータクラス）
  PBRemap.cs               - 移植設定MonoBehaviour（PB Remap）
  PathRemapRule.cs         - パスリマップルール
  BoneMapping.cs           - ボーンマッピングプレビュー用データ

Editor/Scripts/
  Core/           - 基盤クラス（EventBus、Result型、StateMachine、Commands、Specifications）
  Managers/       - シングルトンデータマネージャー群
  Models/         - データモデル（AvatarData、Settings等）
  Processing/     - コンポーネント処理ロジック
  PBRemap/        - 移植機能（PBRemap）
    Core/         - 移植コアロジック（BoneMapper、PBRemapper、ScaleCalculator、SourceDetector）
    Editor/       - 移植UI（PBRemapEditor、PBRemapPreviewWindow）
    NDMF/         - NDMFビルド時統合（条件付き: #if NDMF）
  UI/Handlers/    - UIイベントハンドラ（アバタードロップ、列へのドロップ）
  UI/Windows/     - EditorWindowクラス（PBReplacerWindow: partial 4 ファイル）
  Utilities/      - ヘルパークラス（ComponentIconUtility: 型からカテゴリアイコンを取得、PBReplacerFonts: ルートへの日本語フォント適用）
```

UI ToolkitのUXML/USSファイルは`Editor/Resources/`に配置。
**見た目（構成要素・クラス・文言・ツールチップ）はUXML/USSに置き、C#は要素をnameで取得して
「内蔵アイコンの画像・イベント・データ」だけをバインドする**（内蔵アイコンはUXMLから参照できないためC#で設定）。
- `UXML/PBReplacer.uxml`: メインウィンドウ（ツール行のボタン、流れ、詳細設定、レールのチップ×4、列のInstance×4）
- `UXML/PBReplacerColumn.uxml`: 列テンプレート（見出し / ListView / 空状態）。列名は`AttributeOverrides`で上書き
- `UXML/PBReplacerRow.uxml`: 行テンプレート（畳み記号 / 状態アイコン / 名前 / ホバー操作）。`ListView.makeItem`でInstantiate
- `UXML/PBRemap.uxml`: PBRemap Inspector（ツール行のボタンを含む）。候補チップ・対応表の行はデータ駆動のためC#生成
`USS/PBReplacerCommon.uss` はメインウィンドウと PBRemap Inspector で共有する UI 語彙
（ツール行のアイコンボタン / 流れ strip・node・apply / チップ / 詳細設定パネル）。
同じ規則を `.pbremap-*` と `.pbr-*` の両方のクラス名で提供する。

### メインウィンドウの UI 語彙（PBRemap と共通）

- ツール行: アイコンのみ（↻ 再読み込み / ↶ 元に戻す / ⚙ 詳細設定 / ⋮ その他）。説明はツールチップ
- 流れ: `[アバター] ──(再配置 n →)── [AvatarDynamics]`。真ん中のピルが主操作。線と背景の色が状態
- レール: `ComponentCategory` ごとのアイコンチップ。枠色=状態、右下バッジ=未処理件数。クリックで列の表示切替
- 列: カテゴリごとの ListView。行頭 → 未処理 / ✔ 配置済み。列へのドロップで追加（`ColumnDropHandler`）
- 文字は「オブジェクト名 / 数値 / 動詞1語」に限り、HelpBox・完了ダイアログは使わない
- 設計の経緯と検証結果は `Docs~/MainWindow-Redesign.md`

### 主要パターン

**Commandパターン** (`Core/Commands/`)
- `ICommand`インターフェースで処理を抽象化
- `CompositeCommand`で複数コマンドを合成
- Undo/Redo対応を自然に実現

**Result型** (`Core/Result.cs`)
- Railway Oriented Programmingによるエラーハンドリング
- `Result<TSuccess, TError>`で成功/失敗を型安全に表現
- `Map`, `Bind`, `Match`などの関数型操作をサポート

**EventBus** (`Core/EventBus.cs`)
- 型安全なパブリッシュ/サブスクライブパターン
- `IDisposable`で購読解除を管理
- 主要イベント: `AvatarChangedEvent`, `ProcessingCompletedEvent`, `SettingsChangedEvent`, `StatusStateChangedEvent`

**StatusStateMachine** (`Core/StateMachine/`)
- 状態: None → Loading → Idle → Processing → Complete/Warning/Error
- メインウィンドウの流れ（strip）の色・ピルの有効/無効・ツールチップを状態遷移で管理

**ComponentManager** (`Managers/`)
- `ComponentManagerBase<T>`を継承したシングルトン
- `IComponentManager<T>`インターフェースを実装
- `Managers`静的クラスで全マネージャーへの統一アクセス
- 処理グループ（`ComponentCategoryInfo.ProcessGroup`）: 0=PhysBone(PB+PBC), 1=Constraint, 2=Contact。PB と PBC は参照解決のため常に同時に処理する

### データフロー

1. `AvatarFieldHelper`でアバター選択を管理（ウィンドウ左ノードへのドロップ / ピッカー経由）
2. 各`ComponentManager`がアバターからコンポーネントを検索・ロード
3. `ComponentProcessor`がリフレクションでプロパティをコピーし再配置
4. `ProcessingContext`で削除待ちコンポーネントを管理
5. Undoグループで全操作を巻き戻し可能に

### 参照解決

`PhysBoneColliderManager`は`IReferenceResolver`を実装し、PhysBoneのCollider参照を新コンポーネントに解決。

### 条件付きコンパイル

`#if MODULAR_AVATAR`でModularAvatarのMergeArmatureコンポーネント検出をサポート（`versionDefines`で自動定義）。

`#if UITK_FONT_FIX`でUITKFontFix（`jp.colloid.uitk-font-fix`）によるOSフォント適用をサポート（`versionDefines`で自動定義）。未導入時は同梱のNoto Sans JPを`PBReplacerFonts.Apply(root)`がルートにインラインで適用する。USSで`-unity-font-definition`を各要素に書くと継承が壊れるので書かない。

`#if NDMF`でNDMFビルドパイプラインへの統合をサポート（`versionDefines`で自動定義）。
PBRemapコンポーネントを`BuildPhase.Resolving`で自動処理し、ランタイムでは除去する。

## リリースプロセス

タグプッシュ時にGitHub Actionsで自動化:
1. `create-tag.yml` - バージョンタグ作成
2. `release.yml` - .zipと.unitypackageをビルドしGitHubリリース作成
3. `build-listing.yml` - VPMパッケージリスト更新
