# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

PBReplacerはVRChatアバター開発用のUnityエディタ拡張です。アバターのArmature内にあるVRC関連コンポーネント（PhysBone、PhysBoneCollider、VRCConstraint、VRCContact）を「AvatarDynamics」階層に1オブジェクト＝1コンポーネントとして再配置します。これにより複数選択での一括編集やアニメーションでのオンオフ制御が容易になります。

## アーキテクチャ

### MVC風構造 (Editor/Scripts/)

- **UI/Windows/** - EditorWindowクラス（`PBReplacerWindow.cs`、`PBReplacerSettingsWindow.cs`）。UI ToolkitのUXML/USSファイルはEditor/Resources/に配置
- **Core/** - `StatusMessageManager`（ステータス表示）、`EventBus`（イベント管理）、`Result`型などの基盤クラス
- **Managers/** - `IComponentManager<T>`を実装するシングルトンデータマネージャー:
  - `PhysBoneDataManager` - VRCPhysBone
  - `PhysBoneColliderManager` - VRCPhysBoneCollider（IReferenceResolver実装で参照解決をサポート）
  - `ConstraintDataManager` - 全VRCConstraint系
  - `ContactDataManager` - VRCContactSender/Receiver
- **Commands/** - `ICommand`実装（ProcessPhysBoneCommand、ProcessConstraintCommand等）
- **Processor/** - `ComponentProcessor`がリフレクションによるプロパティコピーを含むコンポーネント再配置処理を実行
- **Models/** - `AvatarData`、`PBReplacerSettings`、`ProcessorSettings`
- **Utilities/** - `AvatarFieldHelper`（グローバルなアバター状態管理）、`DataManagerHelper`、`UIHelper`

### 主要パターン

- マネージャーは`ComponentManagerBase<T>`を継承し、共通のロード処理、イベント通知、処理済みコンポーネント検索用の`GetAvatarDynamicsComponent<T>()`を提供
- `AvatarFieldHelper`は全マネージャー間で選択中のアバターを管理する静的クラス
- コンポーネント処理はUndoグループを使用し完全に元に戻せる
- UI更新は`EditorApplication.delayCall`でスケジュール
- `Managers`クラスが全マネージャーへの統一アクセスとタブ別リロードを提供
- `StatusMessageManager`が優先度ベースのステータスメッセージ表示を一元管理（Info/Success/Warning/Error）
- `EventBus`によるパブリッシュ/サブスクライブパターンでコンポーネント間の疎結合を実現
- Commandパターン（`ICommand`、`CompositeCommand`）で処理を抽象化
- Result型（`Result<T, E>`）によるRailway Oriented Programmingでエラーハンドリング

### 条件付きコンパイル

`#if MODULAR_AVATAR`でModularAvatarのMergeArmatureコンポーネント検出をサポート。

## VPMパッケージ

VPM（VRChat Package Manager）パッケージです。`package.json`でパッケージメタデータとVPM依存関係（`com.vrchat.avatars`）を定義。

## リリースプロセス

タグプッシュ時にGitHub Actionsで自動化:
1. `create-tag.yml` - バージョンタグ作成
2. `release.yml` - .zipと.unitypackageをビルドしGitHubリリース作成
3. `build-listing.yml` - VPMパッケージリスト更新
