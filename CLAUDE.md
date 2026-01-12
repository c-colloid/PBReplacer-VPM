# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

PBReplacerはVRChatアバター開発用のUnityエディタ拡張です。アバターのArmature内にあるVRC関連コンポーネント（PhysBone、PhysBoneCollider、VRCConstraint、VRCContact）を「AvatarDynamics」階層に1オブジェクト＝1コンポーネントとして再配置します。これにより複数選択での一括編集やアニメーションでのオンオフ制御が容易になります。

## 開発環境

- Unity Editor拡張（Editor-onlyコード）
- VPMパッケージ（VRChat Package Manager）
- 依存関係: `com.vrchat.avatars` (VRChat Avatars SDK)
- 名前空間: `colloid.PBReplacer`
- アセンブリ定義: `jp.colloid.pbreplacer.asmdef`

## アーキテクチャ

### ディレクトリ構造 (Editor/Scripts/)

```
Core/           - 基盤クラス（EventBus、Result型、StateMachine、Commands、Specifications）
Managers/       - シングルトンデータマネージャー群
Models/         - データモデル（AvatarData、Settings等）
Processing/     - コンポーネント処理ロジック
UI/Elements/    - カスタムUI要素
UI/Handlers/    - UIイベントハンドラ
UI/Windows/     - EditorWindowクラス
Utilities/      - ヘルパークラス
```

UI ToolkitのUXML/USSファイルは`Editor/Resources/`に配置。

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
- UIのステータス表示を状態遷移で管理

**ComponentManager** (`Managers/`)
- `ComponentManagerBase<T>`を継承したシングルトン
- `IComponentManager<T>`インターフェースを実装
- `Managers`静的クラスで全マネージャーへの統一アクセス
- タブインデックス: 0=PhysBone(PB+PBC), 1=Constraint, 2=Contact

### データフロー

1. `AvatarFieldHelper`でアバター選択を管理
2. 各`ComponentManager`がアバターからコンポーネントを検索・ロード
3. `ComponentProcessor`がリフレクションでプロパティをコピーし再配置
4. `ProcessingContext`で削除待ちコンポーネントを管理
5. Undoグループで全操作を巻き戻し可能に

### 参照解決

`PhysBoneColliderManager`は`IReferenceResolver`を実装し、PhysBoneのCollider参照を新コンポーネントに解決。

### 条件付きコンパイル

`#if MODULAR_AVATAR`でModularAvatarのMergeArmatureコンポーネント検出をサポート（`versionDefines`で自動定義）。

## リリースプロセス

タグプッシュ時にGitHub Actionsで自動化:
1. `create-tag.yml` - バージョンタグ作成
2. `release.yml` - .zipと.unitypackageをビルドしGitHubリリース作成
3. `build-listing.yml` - VPMパッケージリスト更新
