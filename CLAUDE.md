# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

PBReplacerはVRChatアバター開発用のUnityエディタ拡張です。アバターのArmature内にあるVRC関連コンポーネント（PhysBone、PhysBoneCollider、VRCConstraint、VRCContact）を「AvatarDynamics」階層に1オブジェクト＝1コンポーネントとして再配置します。これにより複数選択での一括編集やアニメーションでのオンオフ制御が容易になります。

## アーキテクチャ

### MVC風構造 (Editor/Scripts/)

- **View/** - EditorWindowクラス（`PBReplacerWindow.cs`、`PBReplacerSettingsWindow.cs`）。UI ToolkitのUXML/USSファイルはEditor/Resources/に配置
- **Controller/Manager/** - `IComponentManager<T>`を実装するシングルトンデータマネージャー:
  - `PhysBoneDataManager` - VRCPhysBoneとVRCPhysBoneCollider
  - `ConstraintDataManager` - 全VRCConstraint系
  - `ContactDataManager` - VRCContactSender/Receiver
- **Controller/Processor/** - `ComponentProcessor`がリフレクションによるプロパティコピーを含むコンポーネント再配置処理を実行
- **Model/** - `AvatarData`、`PBReplacerSettings`、`ProcessorSettings`
- **Utility/** - `AvatarFieldHelper`（グローバルなアバター状態管理）、`DataManagerHelper`、`UIHelper`

### 主要パターン

- マネージャーは`ComponentManagerBase<T>`を継承し、共通のロード処理、イベント通知、処理済みコンポーネント検索用の`GetAvatarDynamicsComponent<T>()`を提供
- `AvatarFieldHelper`は全マネージャー間で選択中のアバターを管理する静的クラス
- コンポーネント処理はUndoグループを使用し完全に元に戻せる
- UI更新は`EditorApplication.delayCall`でスケジュール

### 条件付きコンパイル

`#if MODULAR_AVATAR`でModularAvatarのMergeArmatureコンポーネント検出をサポート。

## VPMパッケージ

VPM（VRChat Package Manager）パッケージです。`package.json`でパッケージメタデータとVPM依存関係（`com.vrchat.avatars`）を定義。

## リリースプロセス

タグプッシュ時にGitHub Actionsで自動化:
1. `create-tag.yml` - バージョンタグ作成
2. `release.yml` - .zipと.unitypackageをビルドしGitHubリリース作成
3. `build-listing.yml` - VPMパッケージリスト更新
