# PBRemap 再設計ドキュメント（ドラフト）

対象: `jp.colloid.pbreplacer` 3.0.0-beta.7 の PBRemap 機能
検証環境: Unity 2022.3.22f1 (Linux) / VRChat SDK 3.10.4 / NDMF 1.14.8 / Modular Avatar 1.18.7
検証方法: 実機シナリオテスト（`Assets/PBRemapTests` フィクスチャ: Humanoidアバター×命名3種、MA衣装、着せ替え済み、小物）＋ VRC SDK DLL の逆コンパイルによる仕様確認 ＋ 8観点コードレビュー

---

## 0. 結論（要約）

現行実装は「同一シーン内で、AvatarDescriptor付きHumanoidアバターA→B（同じボーン命名）」という最も単純なケースでは動作するが、
ユーザーが実際に遭遇する以下のケースで **無警告の移植漏れ・データ破壊・経路の破綻** が実測で確認された。

| # | 事象 | 実測シナリオ | 深刻度 |
|---|---|---|---|
| 1 | NDMFビルドのたびにスケール補正が再適用され、PB/Collider/Contactのradius等が二重拡大される | S08: 編集時0.036 → ビルド後0.0432 | **致命的**（アップロード物が壊れる） |
| 2 | Prefab経路（Projectウィンドウからのドロップ / 別プロジェクトへの持ち出し）が実質使えない。移植元A内でInspectorを開いてもシリアライズデータは0件 | S03 / S03c: `serializedBoneRefs=0` → 「シリアライズされたボーン参照データがありません」 | **致命的** |
| 3 | 着せ替え済み（本体+MA衣装）では参照の多数決で「衣装Armature」が移植元と誤認され、本体の髪PBが無警告で移植漏れ | S04/S04b: `source=AvatarA/Costume/Armature`, Hair_Root が EXTERNAL のまま、警告0件 | **致命的** |
| 4 | VRC SDKは radius/height/position を「参照ボーンの lossyScale」で乗算する。現行は世界寸法比のみ補正するため、スケール付き小物では二重拡大、Armatureスケール0.01系では1/100になる | S06: 0.01→0.015 かつ lossyScale1.5 / S11: lossyScale0.01 なのに radius 0.03 のまま | **重大** |
| 5 | Liveモードで未解決参照が `UnresolvedReferenceCount=0`・警告なしで残る（完了ダイアログは成功表示） | S05: LeftUpperLeg PBC が EXTERNAL のまま unresolved=0 | **重大** |
| 6 | 名前一致フォールバックが最初に見つかった同名ボーンを採用し、左右を取り違える | S12: HairR_Root/Hair_01 → HairL/Hair_01 | 重大 |
| 7 | AvatarDynamics外に残ったPBCや、Constraintの対象オブジェクト（A/Accessory）への参照が移植元を指したまま残る | S13 / S01 | 重大 |
| 8 | 誤ドロップ（空オブジェクト直下）でも transform.root / 最大子階層ヒューリスティックにより「成功」してしまう | S10: dest=Props, destArmature=Props/AvatarB | 中 |
| 9 | MA衣装単体では移植元がルートではなく `Armature` 自身として検出される（SkinnedMesh判定・Animator検出が失われる） | S05: source=CostumeC/Armature | 中 |

根本原因は次の3点に集約される。

1. **「移植元」を移植先との相対関係（多数決・階層走査）から毎回推定している** … 移植元の情報は「PBRemapが移植元にいる時」にしか正しく取れないのに、その時点（IsReferencingDestination状態）では何も保存しない。
2. **単一Armature前提のデータモデル** … 着せ替え済みアバター（複数Armature）、MAのprefix/suffix、ボーン以外の参照先（Collider/対象オブジェクト）を表現できない。
3. **適用が冪等でない** … 「元の値×係数」ではなく「現在の値×係数」で上書きするため、再実行・ビルド時再実行で壊れる。スケール係数もVRC SDKの実装（lossyScale乗算）と食い違う。

（以降、§1 現行設計の分析 / §2 再設計 / §3 移行計画 を記述）

---

## 1. 現行設計の分析（実機検証に基づく）

### 1.1 検証環境とフィクスチャ

`Assets/PBRemapTests`（本リポジトリには含めない検証用プロジェクト）で、Editorスクリプトから以下を生成して検証した。

| フィクスチャ | 構成 |
|---|---|
| AvatarA | Humanoid（AvatarBuilderで構築）、標準命名、身長1.5、`Armature/Hips/…/Head/Hair_Root/Hair_01/Hair_02`、`Hips/Skirt_L/…`、非スキンのヘルパー `Head/HeadCollider`、SkinnedMeshRenderer `Body`、VRCAvatarDescriptor、別オブジェクト `Accessory`（VRCParentConstraint, Source=Head） |
| AvatarB / B_VRoid / B_Booth / B_001 | 命名違い（`J_Bip_C_*` / `Left arm` 等）、身長差（1.8）、Armatureスケール0.01 |
| Costume（MA衣装） | `Costume/Armature`（ModularAvatarMergeArmature, prefix任意）+ Hips/Spine/Chest/脚 + `Skirt_L/R` チェーン + SMR `Cloth` |
| Bag（小物） | Animator/Descriptor無し、`Root/Strap_01..03`, `Flap`、SMR、ルートスケール1.0/1.5 |

AvatarDynamics 階層は PBReplacer 本体の Apply（`ProcessPhysBoneColliderCommand` 等）で実際に生成した。

### 1.2 シナリオ別の実測結果（現行 3.0.0-beta.7）

| ID | シナリオ | 結果 | 実測 |
|---|---|---|---|
| S01 | 標準→標準（身長1.5→1.8） | ○ | 6参照リマップ、スケール1.2、ヘルパー自動作成。ただし Constraint の対象 `A/Accessory` は移植元のまま |
| S02 | 標準→VRoid命名 | △ | Humanoidボーンは解決、髪/スカートは未解決（名前差）。想定どおりだが警告のみ |
| S02b | 標準→Booth命名＋ルール3種 | ○ | CharacterSubstitution/RegexReplace で全解決 |
| **S03** | Prefab化→別アバターへ（A内でInspector閲覧） | **×** | `serializedBoneRefs=0`。「シリアライズされたボーン参照データがありません」で失敗 |
| S03b | Prefab化（Inspector未閲覧） | × | 同上 |
| **S03c** | A内でInspector閲覧→Prefab化 | **×** | A内では `IsReferencingDestination` 扱いになり保存処理が走らない |
| **S04** | 着せ替え済み(A+MA衣装)→(B+同衣装) | **×** | 移植元が `AvatarA/Costume/Armature` に誤検出（参照の多数決）。本体の髪PBが `EXTERNAL` のまま、警告0。プレビューは「0未解決」と表示 |
| S04b | 同上 prefix付き衣装 | × | 同上＋衣装ボーンが本体Armature直下に自動作成される |
| **S05** | MA衣装単体→MA衣装単体(prefix違い) | **×** | 移植元が `CostumeC/Armature`。`LeftUpperLeg` PBC が未解決のまま `unresolved=0` 報告。スケール1.0（実際は1.133） |
| S06 | 小物→小物（同名・スケール1.5） | △ | 参照は解決。radius 0.01→0.015 だが移植先ボーンの lossyScale も1.5のため実効半径は 0.0225（二重拡大） |
| S07 | 移植実行を2回 | △ | 2回目は「シリアライズ無し」で失敗するため無害。ただしInspectorを開いた後なら二重適用になる（S08と同経路） |
| **S08** | NDMFビルド | **×** | ビルド後 radius 0.036→0.0432（PB/PBC/Contact全て1.2倍の再適用） |
| S09 | PBRemapをアバタールートに付与 | △ | 移植先未検出（仕様どおりだが案内が弱い） |
| S10 | 空の親 `Props` 直下に誤ドロップ | × | 移植先=`Props`, Armature=`Props/AvatarB` として「成功」してしまう |
| **S11** | Armatureスケール0.01のFBXへ | **×** | スケール1.0のまま radius 0.03 → 実効半径 0.0003（1/100） |
| S12 | 同名ボーン HairL/Hair_01, HairR/Hair_01 | × | 右髪PBが `HairL/Hair_01` に誤解決 |
| S13 | AvatarDynamics外のPBCを参照 | × | `colliders[1]` が移植元のPBCを指したまま、警告なし |

### 1.3 根本原因の整理

**(a) 移植元の推定が「移植先との相対関係」に依存している**

`SourceDetector.DetectSourceFromChildComponents` は外部参照ごとに `FindAvatarRoot(t, includeSelf:true)` で祖先を辿り、参照数の多数決で単一の「移植元」を決める。
`FindAvatarRoot` は `scan.GetComponentInChildren<MergeArmature>` を祖先ごとに評価するため、衣装ボーンから辿ると MergeArmature が付いた `Armature` 自身で止まる（自己一致）。
着せ替え済みアバターでは衣装側の参照が多数派になり、本体の参照が「移植元Armature外」として黙って捨てられる（S04）。

**(b) Prefab用データの取得条件が「移植先に置いた後」**

`PBRemapEditor.UpdateSerializedBoneReferences` は `IsLiveMode` のときだけ動く。PBRemapが移植元Aにある間は
`SourceAvatar == DestinationAvatar` → `IsReferencingDestination` → `IsLiveMode=false` となり、何も保存しない（S03/S03c）。
つまり「AのAvatarDynamicsをPrefab化して配布・持ち出す」という最も自然なフローで Prefab モードが成立しない。

**(c) 単一Armature・単一種類の参照しか表現できない**

`SerializedBoneReference` は `boneRelativePath`（本体Armature基準）しか持たず、衣装Armature配下・ルート直下のオブジェクト（Accessory）・コンポーネント参照（colliders）を表現できない。
`GetRelativePath` が null を返す参照は無警告でスキップされる。

**(d) 適用が非冪等で、スケール係数の意味論が VRC SDK と食い違う**

`ApplyScaleFactor` は現在値に係数を掛ける。NDMFパスはクローン上で `IsReferencingDestination` → Prefabモードに落ち、`SourceAvatarScale`（A基準）と B を比較して同じ係数を再度掛ける（S08）。
VRC SDK 3.10.4 では `globalRadius = radius × cmax(bone.lossyScale)`（PhysBoneManager.cs:1207-1231）、Collider/Contact も `radius × cmax(|lossyScale|)`（CollisionScene.cs:395-455）であり、
ボーンの lossyScale 差は SDK 側で自動的に反映される。現行は世界寸法比だけを掛けるため、lossyScale が 1 でない移植先で二重拡大／過小になる（S06/S11）。

**(e) 曖昧な解決を無条件に確定する**

`FirstOrDefault(t => t.name == boneName)` は同名ボーンの最初の1件を採用する（S12）。ルート検出の最終フォールバック `transform.root` ＋ `FindLargestChildrenStructure` も検証なしに何かを返す（S10）。

### 1.4 現行UIの問題

- 「他のアバターへ移植」メニューは AvatarDynamics ではなく専用子オブジェクト `PBRemap` を作る（D&Dする対象がAvatarDynamics階層そのものではなくなる）
- プレビューの「解決済み」と実行結果が一致しない（S04: プレビュー0未解決／実行後EXTERNAL残り）
- Inspector を開かないとデータが保存されない（`hierarchyChanged` も直下の子数しか見ない）
- Live モードの完了ダイアログは未解決0件を常に表示する

### 1.5 コードレビュー（8観点）で追加された主な指摘

実機で再現したもの以外に、コードレビュー（8観点・反証検証つき）で以下が確認された。詳細は付録A。

- 自動作成ヘルパーが元ボーンのローカルTRSを引き継がず親の原点に生成される（PBRemapper.cs:239-240）
- `UpdateSerializedBoneReferences` が Inspector 表示のたびに `ApplyModifiedPropertiesWithoutUndo` を実行し、Prefabオーバーライドとシーンdirtyを無自覚に発生させる
- Live モードの自動作成が Undo グループの外で登録され、1回の Undo で戻らない
- Animator フォールバックが「最上位の」Animator を採用するため、整理用オブジェクトに Animator があると誤検出する
- 複数 MergeArmature がある衣装で `mergeComponents[0]` を無条件採用する

---

## 2. 再設計

### 2.1 設計原則

| 原則 | 内容 | 現行との違い |
|---|---|---|
| **P1. 自己記述的な移植データを「移植元にいる間」に取る** | PBRemapは、自分の配下にあるコンポーネントの外部参照を、移植元の構造情報（後述のマニフェスト）として常に保持する。取得はInspector表示に依存せず、コンポーネント追加時・PBReplacer Apply時・階層変更時・Prefab保存/ビルド前に自動で行う。 | 現行は「移植先に置かれた後にInspectorを開いた時」だけ保存（S03/S03c）。 |
| **P2. 「ホーム」と「移植先」を明示的に区別する** | PBRemapの状態は `AtHome`（参照が自分を含むルート内に解決している）/ `Displaced`（参照がルート外を指す、またはnull）/ `Applied`（このルートへ適用済み）の3状態。移植元は「参照先ボーンが属するルート」、移植先は「PBRemapが今いるルート」。 | 現行は移植元を毎回、参照の多数決で推定し、移植先と一致すると「移植済み」とみなして情報を捨てる。 |
| **P3. 参照ごとにコンテキスト（Armature）を持つ** | 参照先ボーンは「本体Armature」「MA衣装Armature（prefix/suffix/mergeTarget付き）」「汎用ルート」のいずれかのコンテキストに属する。マニフェストはコンテキスト単位で相対パスを持つ。 | 現行は単一Armature前提（S04で本体の髪が移植漏れ）。 |
| **P4. 解決戦略は「一意に決まるものだけ自動」** | Humanoid ID → Humanoid祖先+相対パス → コンテキスト対応（MA正規化名） → ルール適用パス → 名前一致（候補が一意な場合のみ）。曖昧な場合は候補リスト付きで未解決とし、手動マッピングで確定する。 | 現行は `FirstOrDefault` で最初の同名ボーンを採用（S12）。 |
| **P5. 適用は冪等（元の値×係数）** | 数値パラメータは「移植元で取得した元値」をマニフェストに保持し、常に `元値 × 係数` を書く。係数は VRC SDK の実装に合わせ `世界寸法比 × (移植元ボーンlossyScale / 移植先ボーンlossyScale)` を参照ごとに計算する。 | 現行は現在値に係数を掛けるため再実行・ビルドで二重適用（S07/S08）。lossyScaleを無視（S06/S11）。 |
| **P6. 編集時適用とビルド時適用を排他にする** | 編集時に適用したら `Applied` 状態を記録し、NDMFパスは `Displaced` のPBRemapのみ処理する。どちらの経路でも同じ `PBRemapResolver` を使う。 | 現行はビルド時に必ず再実行（S08）。 |
| **P7. 失敗は必ず可視化する** | 未解決・曖昧・ルート外参照（Collider/対象オブジェクト）は件数と一覧を返し、ダイアログ/Inspector/NDMFエラー報告に出す。 | 現行はLiveモードで警告0（S05）。 |
| **P8. D&Dだけで完結する** | ドロップ → 自動解決 → 全件一意なら自動適用（Undo可）／曖昧があればマッピングウィンドウを開く。NDMFがある環境では非破壊（ビルド時適用）も選べる。 | 現行は「子オブジェクトにPBRemapを追加→Inspector→プレビュー→移植実行」が必須。 |

### 2.2 データモデル（Runtime, シリアライズ）

```
PBRemap : MonoBehaviour, IEditorOnly
  ├ manifest        : PBRemapManifest      // P1/P3/P5。移植元で自動取得
  ├ mappingOverrides: List<ManualMapping>  // P4。ユーザーが確定した対応 (sourceKey → Transform)
  ├ pathRemapRules  : List<PathRemapRule>  // 既存（双方向ルール）
  ├ scaleMode       : Auto / Manual / None
  ├ manualScale     : float
  ├ applyMode       : OnDrop / Confirm / BuildOnly     // P8
  ├ applied         : AppliedRecord? (destRootName, appliedAt, factors)  // P6
  └ (互換) serializedBoneReferences → 初回ロード時にmanifestへ移行

PBRemapManifest
  ├ version, capturedAtUtc, sourceRootName, sourceRootKind (VRCAvatar/MACostume/Generic)
  ├ contexts[] : BoneContext
  │     id, kind (Main/Costume/Generic), armaturePathFromRoot,
  │     isHumanoid, maPrefix, maSuffix, maMergeTargetPath, costumeName(prefab名/ルート名)
  ├ refs[]     : BoneRef
  │     componentPath, componentType, propertyPath,
  │     contextId, relPath (context armature基準), humanBone, nearestHumanoidAncestor, pathFromAncestor,
  │     isSkeletonBone, boneLocalPosition/Rotation/Scale (自動作成用), boneLossyScale (P5)
  ├ nonBoneRefs[] : ExternalRef   // Collider/対象オブジェクト等、ボーン以外への参照 (P7)
  │     componentPath, propertyPath, targetKind (Component/Transform), targetPathFromRoot, targetTypeName
  ├ originals[] : OriginalValues   // P5
  │     componentPath, componentType, radius, height, position, endpointPosition
  └ scaleReference : { hipsToHead, armatureLossyScale, boneDistances[] }
```

### 2.3 コアロジック（Editor）

```
PBRemapContextResolver   … 「ルート」と「コンテキスト」を決める（現行SourceDetector/AvatarDataの置換）
   FindRoot(Transform start, excludeSelf)      : VRCAvatarDescriptor > MA衣装ルート(MergeArmatureを含む最外の非アバター) > 最寄りAnimator > Prefabインスタンスルート > 直上の親  …ただし「ボーンらしさ」で検証し、失敗時は候補を返す
   ClassifyBone(Transform bone)                : bone→(rootGO, BoneContext)  ※MergeArmatureが最寄り祖先ならCostumeコンテキスト
   BuildContexts(GameObject root)              : rootの全コンテキスト列挙（本体Armature + 各MA衣装Armature）

PBRemapManifestBuilder  … マニフェスト生成（PBRemap配下のVRCコンポーネント全走査、SerializedPropertyでObjectReferenceを網羅）
PBRemapResolver         … マニフェスト × 移植先 → ResolutionPlan（参照ごとに {status, target, method, candidates, autoCreate, scaleFactor}）
PBRemapApplier          … ResolutionPlan を適用（Undoグループ1つ、失敗時全戻し、Applied記録、マニフェスト再取得）
PBRemapTracker          … [InitializeOnLoad] ドロップ検知（hierarchyChanged/Undo.postprocess）→ 状態遷移とP8の自動適用
PBRemapNDMFPass         … Resolving フェーズで Displaced のみ Resolver+Applier を実行。未解決は ErrorReport に出す
```

解決戦略（P4）の順序と根拠:

1. `humanBone != LastBone` かつ移植先Humanoid → `Animator.GetBoneTransform`
2. `nearestHumanoidAncestor` + `pathFromAncestor`（各セグメントに PathRemapRule と MA prefix/suffix 正規化を適用）
3. コンテキスト対応: 移植元がCostumeコンテキストなら、移植先で「同じ衣装」（costumeName一致 or MergeArmature構造一致）を探し、その衣装Armature基準で相対パス解決。無ければ移植先本体Armatureで **MA正規化名**（prefix/suffixを剥いだ名前）で解決（MAがマージ後に生成する名前空間と一致させる）
4. コンテキストArmature基準の相対パス（ルール適用あり・双方向）
5. 名前一致: 候補が **1件のときだけ** 採用。複数なら `Ambiguous` として候補列挙
6. 自動作成: `isSkeletonBone == false` かつ親が解決済み → 親配下に生成（ローカル位置/回転/スケールを元ボーンからコピーし、係数でスケール）
7. 手動マッピング（`mappingOverrides`）は常に最優先

### 2.4 スケール（P5）

VRC SDK 3.10.4 の実装（`VRC.Dynamics.dll` 逆コンパイル）:

- PhysBone: `globalRadius = radius × cmax(bone.lossyScale)`（PhysBoneManager.cs:1207-1231）
- PhysBoneCollider / Contact: `outRadius = radius × cmax(|lossyScale|)`, `height` 同様, `position` はrootTransformローカル空間（CollisionScene.cs:395-455）

したがって参照 *i* の係数は

```
factor_i = worldRatio × lossy(srcBone_i) / lossy(dstBone_i)
worldRatio = Hips-Head距離比（両Humanoid）
           | 解決済み参照ペアの親子距離比の中央値（非Humanoid、ボーンが3組以上解決）
           | 1.0 + 警告（算出不能）
```

適用は `value = original × factor_i`（originals はマニフェスト保持）。これにより再実行・ビルド時再実行が冪等になる。

### 2.5 UI / UX（P7, P8）

- **Hierarchy**: PBRemapオブジェクトに状態アイコン（AtHome=灰 / Displaced=黄 / Applied=緑 / 未解決あり=赤）
- **ドロップ時**: `applyMode` に応じて (a)自動適用してステータス通知 (b)マッピングウィンドウを開く (c)何もしない（ビルド時）
- **Inspector**: 状態カード（移植元 → 移植先、検出根拠バッジ、係数）／解決サマリ（解決・自動作成・曖昧・未解決・ルート外）／マッピングテーブル（フィルタ、行ごとにObjectFieldで手動確定、候補ドロップダウン）／アクション（適用・元に戻す・マニフェスト更新・プレビュー）／詳細（ルール、手動ルート指定、スケール）
- **SceneViewオーバーレイ**: 既存を踏襲（解決=緑、自動作成=黄、未解決=赤）に「曖昧=橙（候補へ点線）」を追加
- **PBReplacerメインウィンドウ**: 「他のアバターへ移植」は AvatarDynamics 自身に PBRemap を付ける（専用子オブジェクトを作らない）。コンセプト（AvatarDynamics階層ごとD&D）と一致させる

### 2.6 NDMF（P6）

- フェーズ: `Resolving`（MAのMergeArmatureより前、参照解決の意味論に合致）
- 処理対象: `Displaced` のPBRemapのみ。`Applied` / `AtHome` は何もしない（除去のみ）
- 未解決があれば `ErrorReport.ReportError` で一覧を出し、ビルドは継続（該当コンポーネントの参照は変更しない）

### 2.7 互換・移行

- `serializedBoneReferences`/`sourceAvatarScale` は読み込み時にマニフェストへ変換（相対パス→本体コンテキスト）
- `PathRemapRule` はそのまま
- 既存の Inspector 文字列リソース（UXML）は再利用

---

## 3. 実装と移行

### 3.1 追加/変更ファイル

| ファイル | 役割 |
|---|---|
| `Runtime/Scripts/PBRemapManifest.cs` | マニフェスト（BoneContext / BoneRef / OriginalValues / ScaleReference）、ManualMapping、AppliedRecord、PBRemapApplyMode |
| `Runtime/Scripts/PBRemap.cs` | 新フィールド（manifest, mappingOverrides, scaleMode, manualScaleFactor, applyMode, applied）。旧フィールドは HideInInspector で保持し移行に使用 |
| `Editor/.../Core/PBRemapContextResolver.cs` | ルート検出（Descriptor > MA衣装ルート > Animator > 汎用、妥当性検証つき）とコンテキスト列挙 |
| `Editor/.../Core/PBRemapManifestBuilder.cs` | 外部参照の走査（SerializedPropertyで網羅）、マニフェスト生成、旧形式からの移行 |
| `Editor/.../Core/PBRemapResolver.cs` | 解決計画（手動→Humanoid→Humanoid祖先+パス→コンテキストパス→MA正規化名→一意名→自動作成）とスケール係数（世界寸法比×lossyScale比） |
| `Editor/.../Core/PBRemapApplier.cs` | 適用（Undo 1グループ、元値×係数、適用記録、マニフェスト再取得） |
| `Editor/.../Core/PBRemapper.cs` | ファサード（Inspect / RefreshManifestIfLive / Plan / Remap）と状態モデル（NoReferences / AtHome / Displaced / Broken / NoDestination） |
| `Editor/.../Core/SourceDetector.cs` | 互換レイヤ（DetectionResult を新モデルから構築） |
| `Editor/.../Editor/PBRemapTracker.cs` | [InitializeOnLoad] 監視: 移植元にいる間のマニフェスト自動更新、ドロップ検知→ApplyMode に従う自動適用/プレビュー、保存前フラッシュ |
| `Editor/.../Editor/PBRemapEditor.cs` + `UXML/PBRemap.uxml` | 状態カード／解決サマリ／ボーン対応テーブル（手動マッピング・候補選択）／スケールモード／参照情報更新 |
| `Editor/.../Editor/PBRemapPreview.cs` | 解決計画から表示データを生成（曖昧・外部参照を区別） |
| `Editor/.../NDMF/PBRemapNDMFPass.cs` | Displaced/Broken のみ適用、AtHome はスキップ、未解決は ErrorReport |
| `Editor/.../UI/Windows/PBReplacerWindow.Events.cs` | 「他のアバターへ移植」は AvatarDynamics 自身に PBRemap を付与し、その場でマニフェスト取得 |

### 3.2 互換性

- 既存シーン/Prefab の `serializedBoneReferences` は初回アクセス時（Inspector表示・Tracker・NDMF）にマニフェストへ移行し、旧データは消去する
- `autoCalculateScale=false` は `scaleMode=Manual` + `manualScaleFactor=旧scaleFactor` に写す
- `PBRemapper.Remap(PBRemap)` / `SourceDetector.Detect` / `PBRemapPreview.GeneratePreview` の公開シグネチャは維持

### 3.3 既知の制約（設計上の判断）

- Constraint の対象（TargetTransform）が移植元の非ボーンオブジェクト（例: `Accessory`）を指す場合は自動では解決せず、未解決として警告する。対象オブジェクト自体は移植対象外（メッシュを伴うため）で、手動マッピングまたは移植先に同名オブジェクトを用意して対応する
- AvatarDynamics 外の PhysBoneCollider 参照は、移植先の同位置に同型コンポーネントがあればそれを参照し、無ければ参照を解除して警告する
- スケルトンボーン（SkinnedMeshRendererにバインド済み）の自動作成は行わない（メッシュのウェイトを持たない空ボーンを作っても意味がないため）
- Ambiguous（同名複数）は自動確定しない。Inspector の「候補…」から選ぶか、パスリマップルールで一意にする
