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

追記（衣装ホームと UI 再設計）:

- アバター内の MA 衣装配下に置いた AvatarDynamics は「衣装」をホームとして扱い、アバター本体に手を触れずに衣装だけを移植できるようにした（§5、シナリオ S18〜S26、合計 29/29 通過）。
- Inspector を 7 原則（文字説明なし・気持ちよさ・最小限・勘違いしない・アイコン化・共通認識・向きと動きの意味）で作り直した（§6）。

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
- VRC Constraint は参照の付け替えのみ行い、位置/回転オフセットの再ベイクは行わない（移植先でボーンの向きが異なる場合は再ベイクが必要。計画時に警告を出す）
- 衣装の検出は Modular Avatar の MergeArmature のみ対応。VRCFury の Armature Link 等は汎用ルート（名前/パス解決）として扱われる
- Prefab Stage（Prefabモード編集）内では AutoOnDrop でも自動適用せず、必ずプレビューでの確認を挟む

### 3.4 実装差分の監査で修正した点

再設計の差分を独立した監査（Prefab編集モード／Undo・Redo／複数PBRemap／null参照・計算量／NDMF／C#細部の6観点）にかけ、以下を修正した。

| 指摘 | 対応 |
|---|---|
| Undo/Redo による親変更を「ドロップ」と誤認し、AutoOnDrop が新規Undoグループを積んで Redo スタックを壊し得る | `Undo.undoRedoPerformed` 直後 2 秒は自動適用を抑制し、既知の親を更新 |
| `ManifestEquivalent` がボーンのローカルTRS等を比較せず、「参照情報を更新」でも古い位置が残る | 比較対象に localPosition/Rotation/Scale・boneName・Humanoid祖先・コンテキストを追加。ボタンは強制更新 |
| Prefabインスタンス上のマニフェスト更新が暗黙のオーバーライドになる | `RecordPrefabInstancePropertyModifications` で明示的に記録し、Inspector に「Revert All Overrides で失われる」旨を表示 |
| ネストした PBRemap の二重処理 | 配下の別 PBRemap のサブツリーは収集対象から除外。Tracker も外側のみ監視。Inspector に警告 |
| Tracker の常時ポーリングで `Scan`/`FindRoot` が無キャッシュに多重実行される | Scan 内で Transform→ルート、祖先→分類 をメモ化 |
| `OriginalValues.rootLossyScaleMax` が未使用 | rootTransform 参照が無いコンポーネントの lossyScale 補正に使用 |
| `PrefabStage.prefabSaving` 未購読 | 購読して保存前にマニフェストを確定 |
| NDMF Resolving 内で Modular Avatar との順序が未指定 | `BeforePlugin("nadena.dev.modular-avatar")` を宣言 |
| 移植先候補の列挙が直下の子のみ | 2階層下まで探索 |
| Prefab Stage 内で AutoOnDrop が共有Prefabへ無確認で焼き込まれ得る（網羅性批評） | Prefab Stage 内では Confirm 扱い |
| batchmode/CI でも Tracker が動く（網羅性批評） | `Application.isBatchMode` でガード |
| NDMF エラー文言の en-us が日本語（網羅性批評） | 英語文言を追加し en-us を既定に |
| Constraint のオフセット未補正（網羅性批評） | 計画時に警告。再ベイクは今後の課題 |

---

## 4. 再設計後の検証結果

同じフィクスチャ・同じシナリオで再設計版を実行した結果（Unity 2022.3.22f1 batchmode, `Reports/20260902_063820`）。

| ID | シナリオ | 現行 | 再設計 | 再設計での挙動 |
|---|---|---|---|---|
| S01 | 標準→標準（身長差） | ○ | **○** | 移植元Aにいる時点でマニフェスト取得（6件）。スケール1.2、ヘルパー自動作成。Constraint対象 `Accessory` は未解決として警告 |
| S02 | 標準→VRoid（ルール無し） | △ | **○** | Humanoid参照解決、髪/スカートは「スケルトンボーンのため自動作成不可」として明示的に未解決報告 |
| S02b | 標準→Booth＋ルール | ○ | **○** | 全解決 |
| S03 | Prefab化→別アバター | × | **○** | Broken＋マニフェストで解決、スケール1.2 |
| S03b | Prefab化（Inspector未閲覧） | × | **○** | Tracker の自動取得（テストでは1tickを模擬）で解決 |
| S03c | A内でInspector→Prefab化 | × | **○** | AtHome 状態でもマニフェストが取得される |
| S04 | 着せ替え済み→着せ替え済み | × | **○** | 移植元=AvatarA、髪→B/Armature、スカート→B/Costume/Armature/Hips/Skirt_L、衣装Hips PBC→B/Costume |
| S04b | 同上 prefix付き | × | **○** | prefix を考慮して衣装コンテキスト内で解決 |
| S05 | MA衣装→MA衣装（prefix違い） | × | **○** | 移植元=CostumeC ルート、`LeftUpperLeg`→`D_LeftUpperLeg`、スケール1.133（ボーン間距離比） |
| S06 | 小物→小物（スケール1.5） | △ | **○** | 世界寸法比1.5 × lossyScale比(1/1.5) = 1.0 → radius据え置き（実効半径は SDK のスケールで1.5倍） |
| S07 | 移植実行2回 | △ | **○** | 2回目は AtHome として拒否、値は不変 |
| S08 | NDMFビルド | × | **○** | クローン上で AtHome → スキップ、radius 0.036 のまま |
| S09 | ルート直付け | △ | **○** | NoDestination として案内 |
| S10 | 空親への誤ドロップ | × | **○** | 移植先未検出、候補 `AvatarB` を提示 |
| S11 | Armatureスケール0.01 | × | **○** | radius 0.03 → 3.0（lossyScale 0.01 補正）、実効半径 0.03 を維持 |
| S12 | 同名ボーン | × | **○** | Ambiguous（候補2件）として未確定、誤解決しない |
| S13 | AvatarDynamics外PBC参照 | × | **○** | 移植先に対応PBCが無いため参照解除＋警告 |

17/17 シナリオ通過。加えて S14（衣装単体→アバター直接）、S16（Undo）、S17（旧形式移行）を追加検証（§4.1）。

### 4.1 追加シナリオ

| ID | シナリオ | 結果 | 挙動 |
|---|---|---|---|
| S14 | MA衣装単体（prefix `C_`）のAvatarDynamicsを、衣装を着ていないAvatarBへ直接ドロップ | ○ | `C_LeftUpperLeg` PBC → B の `LeftUpperLeg`（prefix を剥いだ正規化名で本体Armatureに解決）、`Skirt_L` PB → B の `Hips/Skirt_L`。「移植先に衣装がありません」を警告 |
| S16 | 移植実行後に `Undo.PerformUndo()` | ○ | 参照（AvatarA へ）・radius（0.03）・自動作成ヘルパー（削除）の全てが 1 回の Undo で復元 |
| S17 | 旧形式 `serializedBoneReferences` + `autoCalculateScale=false, scaleFactor=2.0` のみを持つPBRemap（参照null） | ○ | マニフェストへ移行（2件）、`ScaleMode.Manual`/係数2.0 へ移行、移行データから解決 |

20/20 シナリオ通過（`Reports/` の最終実行）。

## 5. 衣装ホーム: アバター内の MA 衣装だけを移植する

### 5.1 課題

§4 までの再設計は「PBRemap が属するルート」を **最も外側** の単位（Descriptor / MA衣装ルート / Animator）としていた。
そのため `AvatarA/Costume_v1/AvatarDynamics`（衣装ルート配下に置いた衣装専用の AvatarDynamics）は
ルートが `AvatarA` になり、次の実使用ケースが成立しなかった。

| ケース | 旧挙動 |
|---|---|
| 同じアバター内で衣装 v1 → v2 へ衣装の AvatarDynamics だけを移す | ルートが移動前後とも `AvatarA` のため **AtHome と誤判定**され何も起きない |
| AvatarA 内の衣装 → AvatarB 内の同衣装 | 衣装ボーンもアバターの Hips-Head 比でスケールされる（衣装は同サイズなのに半径が変わる） |
| 衣装 Prefab（AvatarDynamics 同梱）を別アバターへ | アバターボーン参照が null になっても Live 扱いのため解決されない |

### 5.2 モデル

- **ホーム = 最も近い単位**。祖先を上に辿って最初に見つかる Descriptor / MA衣装ルート / Animator をホームとし、
  その外側にある単位を **Outer** として記録する（`RootInfo.Outer`）。汎用（自前SMR）は強い単位が無い場合のみ。
- **コンテキストに Scope を持たせる**（`BoneContext.scope = Self | Outer`）。
  衣装ホームのマニフェストは `Self: Generic(衣装), Costume(衣装/Armature)` と `Outer: Generic(アバター), Main(アバター/Armature), 兄弟衣装…` を持つ。
- **整合判定**（`PBRemapManifestBuilder.IsConsistentWithHome`）。参照先の単位が
  ホーム自身 / ホーム配下の単位 / 外側の単位 なら「整合」、外側の中の別単位（兄弟衣装）や別アバターなら「外れ」。
  外れた参照が 1 件でもあれば Displaced、無ければ AtHome。移植元は外れた参照の多数決（マニフェストが記録する移植元 → 衣装ホームなら衣装 → 入れ子なら外側 → 多数）。
- **対応付け**（`PBRemapResolver.FindDestContext`）は Scope × 種別で移植先のホーム側/外側へ振り分ける。
  Self/Costume → ホーム側の衣装 → 外側の衣装 → ホームの本体（衣装→本体）、Outer/Main → 外側の本体 → ホームの本体、など。
- **スケールはコンテキストごと**（`BoneContext.hipsToHead`）。Humanoid 本体同士なら Hips-Head 比、衣装同士ならそのコンテキストの解決済み参照のボーン間距離比の中央値。
  衣装ボーンはアバターの身長差に引きずられない。
- **一部失われた参照**（`ScanResult.LostKeys`）。生きている参照がホームに整合していても、マニフェストにある参照が null なら Displaced（PartiallyLost）とし、
  マニフェストの記録（以前のコンテキストを複製して保持）から解決する。衣装 Prefab を別アバターへ置いた場合にアバター参照だけが復元される。
- **ネストした PBRemap** は外側が管理するが、親の追跡だけは行い、外へ出された瞬間にドロップとして検知する。

### 5.3 追加シナリオ（S18〜S25）

| ID | 内容 | 主なアサーション |
|---|---|---|
| S18 | 同一アバター内 衣装v1 → v2 | Displaced 判定・衣装参照が v2 へ・アバター Head 参照は同一オブジェクトのまま・半径不変・**アバター本体不変** |
| S19 | AvatarA 内衣装 → AvatarB(1.8) 内同衣装 | 衣装参照は衣装比 1.0（半径 0.02 のまま）、Head 参照は AvatarB へ |
| S20 | アバター参照が多数 | 衣装ホームで AtHome。Outer/Main コンテキストに Humanoid ID で記録 |
| S21 | 衣装 Prefab を別アバターへ | 衣装参照は生存、失われた Head 参照をマニフェストから AvatarB へ |
| S22 | 未適用のまま NDMF ビルド | クローン内の衣装v2ボーンへ解決、本体 AvatarDynamics 不変、PBRemap 除去 |
| S23 | 本体 AvatarDynamics 内の衣装サブフォルダ（ネスト PBRemap）を切り出し | 移植元は衣装v1（アバターではなく）、本体側 PBRemap は AtHome のまま |
| S24 | 衣装 C1/C2 を着た状態で C1 だけ C3 へ | 移植元 C1、C2 の PBRemap は不変 |
| S25 | 衣装v1 → prefix 付き衣装v2 | `V2_Hips` へ prefix 正規化で解決 |

「アバター本体に手を触れない」は各シナリオで共通のアサーション（ボーン階層不変・本体 AvatarDynamics の参照/半径不変・本体 PBRemap が AtHome かつマニフェスト不変）として検証する。

## 6. UI 再設計（7原則）

### 6.1 原則と対応

| 原則 | 対応 |
|---|---|
| 1. 文字による説明が無くても使える | 状態説明の HelpBox・完了ダイアログを廃止。状態は「流れ」の絵（アイコン・線・色）で伝え、説明はツールチップに退避 |
| 2. 使っていて気持ちがいい | 移植/更新の成功時に流れの背景が緑に光って戻る（USS transition）。ダイアログで手を止めない。Undo 可能 |
| 3. 必要最小限・見やすさ | 既定表示は「ツール3つ／流れ1行／チップ1行／問題のある行だけの表」。スケール・ルール・手動指定・参照情報は ⚙ の奥 |
| 4. 勘違いが起きない | 移植元/移植先を常に左右同じ位置に置く。ホーム名を1行目、外側（アバター）名を2行目に分けて衣装 v1/v2 の違いが切れない。名前不一致の衣装への対応付けや同名衣装の複数存在は警告 |
| 5. 文字をアイコンに置き換え | Unity 内蔵アイコンのみ使用: Avatar / Cloth（衣装）/ GameObject（小物）/ MoveTool（自分）/ Linked・Unlinked（接続）/ Valid・Invalid（✔✖）/ Toolbar Plus（自動作成）/ console.warnicon（要選択）/ Refresh / scenevis（👁）/ Settings（⚙）/ ScaleTool |
| 6. 共通認識 | Console の「アイコン＋件数」フィルタ、Hierarchy の状態アイコン、ObjectField へのドロップ、▾ の候補メニュー、Prefab の Linked/Unlinked 記号など Unity ユーザーが既に知っている操作だけを使う |
| 7. 向き・動かし方に意味 | 左→右＝移植の向き。真ん中の「→」がそのまま移植ボタン。移植先ノードは Hierarchy からのドロップ先（＝その配下へ移動）。移植先が無いときは候補チップをクリックすると移動する |

### 6.2 レイアウト（上から）

1. ツール行: ↻（参照情報の取り直し。移植元にいるときだけ有効）・👁（SceneView の対応線）・⚙（詳細設定の開閉）
2. 流れ: `[移植元アイコン] 名前 ──(→ 移植)── [移植先アイコン] 名前`
   - AtHome: 左＝自分（MoveTool）、真ん中＝Linked（緑）、右＝ホーム
   - Displaced: 左＝移植元、真ん中＝「→ 移植」ボタン（全解決なら緑、未解決ありなら琥珀）、線が琥珀
   - Broken（参照情報あり）: 左＝参照情報の移植元（半透明＋Unlinked バッジ）、「→ 移植」
   - Broken（参照情報なし）: 左＝エラー、ボタンなし（赤）
   - NoDestination: 右＝空の枠（Unlinked）、下に候補チップ（クリックで移動）
   - NoReferences: 左＝空フォルダアイコン
3. 警告（必要なときだけ）
4. チップ: ✔ n / ＋ n / ⚠ n / ✖ n（クリックで表の絞り込み。SceneView の線のフィルタと連動）、手動対応 n（🗑 で解除）、右端に ×比（クリックで 自動/手動/なし）
5. 表: `[状態アイコン] ボーン名 → [ObjectField] [▾ 候補 | +自動作成先]`。既定では ✔ を隠す
6. ⚙ 詳細設定: ドロップ時の動作 / スケール / 名前の対応ルール / 手動指定 / 参照情報

### 6.3 Hierarchy と SceneView

- Hierarchy: PBRemap 行の右側（Prefab 矢印の左）に状態アイコン。Inspector を開かなくても「接続済み（緑 Linked）/ 移植できる（琥珀 →）/ 参照切れ（Unlinked）/ 置き場所不明（赤）」が分かる
- SceneView: 👁 で移植元ボーンと移植先ボーンを結ぶ線を表示（既存機能。フィルタはチップと共有）
- ドロップ時の確認（Confirm）は別ウィンドウを開かず、PBRemap を選択して Inspector の流れを見せ、SceneView の線を表示する

### 6.4 独立審査で取り込んだ点と未決事項

3 案（流れ主役 / Unity 純正記号流用 / 段階的開示）を 2 名の審査（初見ユーザー視点・ツール開発者視点）が 7 原則で採点し、勝者案への移植（graft）として次を反映した。

- 要選択（Ambiguous）の記号を「？」や「⚠」ではなく ▾（Dropdown）にする。「？」はヘルプと誤読される
- Hierarchy の状態アイコンは「形 = 状態、色 = 健全度」の二重コードにし、置き場所未確定（中立・無彩色の Unlinked）と参照情報なし（赤のエラー）を分ける
- 移植元が今この場に無い（参照情報のみ）ときは半透明のゴースト表示にする
- 主ボタンは部分適用可能な状態でも押せる（琥珀）。「全解決まで押せない」ように見せない

未決（後続タスク）: ライトテーマでの内蔵アイコンのコントラスト確認、Hierarchy ドラッグ中の受け入れ可能行ハイライト（永続キャッシュが必要）、完全初見ユーザー向けのワンタイムヒント、SceneView オーバーレイの文字チップのアイコン化。

### 6.5 実装

`Editor/Scripts/PBRemap/Editor/PBRemapIcons.cs`（意味→内蔵アイコン）、`PBRemapHierarchyBadge.cs`（Hierarchy 行）、`PBRemapEditor.cs` / `Resources/UXML/PBRemap.uxml` / `Resources/USS/PBRemap.uss`（Inspector）、`PBRemapTracker`（状態キャッシュ・Invalidate）。

---

## 付録A. コードレビュー所見（8観点、反証検証つき）

確認済み 47 件（うち反証検証まで完了 47 件）、反証 2 件。実機で確認できたものはシナリオIDを付記。

| 深刻度 | 所見 | 箇所 | 実機確認 |
|---|---|---|---|
| critical | MergeArmatureがArmatureに直付けされた標準構成で、Source側がAvatarではなくArmature自身に誤検出される | `SourceDetector.cs:224` | S04, S04b |
| critical | Source誤検出（Armature自身）によりCollectSkinnedBonesがSkinnedMeshRendererを一つも見つけられず、IsSkeletonBone判定が常にfalseになる | `PBRemapper.cs:187` | S04, S04b |
| critical | ModularAvatarMergeArmatureのprefix/suffixが一切参照されず、リテラルなボーン名/パス一致に依存しているため、MA側でprefix/suffixが設定された衣装間ではボーン解決が破綻する | `BoneMapper.cs:20` | S04b |
| critical | 衣装ボーン(sourceArmature)の外側を直接参照するPBが実行時に無警告で移植漏れになる（Previewは「解決済み」と誤表示） | `PBRemapper.cs:223` | S04, S04b |
| critical | IsLiveModeのままInspectorを一度も開かないとSerializedBoneReferencesが空のままPrefab化・持ち出しされ、以後回復不能になる | `PBRemapEditor.cs:632` | S03, S03b, S03c |
| critical | 移植の実行トリガーがInspector内ボタンしか存在せず「D&Dだけ」で完結しない | `PBRemapEditor.cs:1025` | なし（scenario_report_v1.mdはPBRemapper.Remap()を直接スクリプトから呼び出す検証手法のため、実際のD&D操作だけでは移植が起きないことをUI経由で確認したものではない。ただしS07/S08の検証手順自体が『移植実行』という明示的なAPI呼び出しを前提としており、D&Dだけでは完結しないという設計と整合する） |
| critical | VRCPhysBone.colliders（コンポーネント参照）がスキャン・キャプチャ・リマップいずれの経路からも完全に漏れている | `PBRemapper.cs:340` | S13: AvatarDynamics外に残ったPBCを参照するPB — remap後もcolliders.Array.data[1]=EXTERNAL:AvatarA/Armature/Hips/Spine/Chest/LeftUpperArm/LeftLowerArm/LeftHand [VRCPhysBoneCollider]のまま、警告なし |
| critical | 手動「移植実行」後にNDMFビルドすると二重リマップ・二重スケール適用が発生する（IsReferencingDestinationがPBRemapper.Remapの分岐に反映されていない） | `PBRemapper.cs:56` | S08: NDMFビルド: 移植済みアバターをNDMFで処理 — edit-time remap後radius=0.036だったのが、NDMF build後radius=0.0432(scaleFactor 1.2の二乗適用)になり『NDMF build keeps radius 0.036 (no double scaling)』がFAILと判定されている |
| critical | 非Humanoid判定時のArmature検出がModularAvatarMergeArmatureを持つ最初の子を機械的に採用するため、本体ではなく衣装側の元FBXスケール（0.01系等）をArmature基準に誤採用しうる | `AvatarData.cs:154` | 直接の実機シナリオは無し（S04/S04b/S05はMergeArmature候補が単一のため誤検出は発生していない）。ただしコード上、TryGetModularAvatarArmatureの結果が実スケール計算に直結することは0398d36のAvatarData.cs/SourceDetector.cs/PBRemapper.csの読解で確認済み |
| major | Source誤検出（Armature自身）により、実在するHumanoid Animator/isHumanが見えなくなりHumanoidベースのボーン対応・スケール算出が使われなくなる | `AvatarData.cs:50` | S04, S04b, S05 |
| major | Liveモードのリマップでは未解決の外部参照が一切警告されず、失敗が静かに握りつぶされる | `PBRemapper.cs:272` | S01 |
| major | 非Humanoid（Animator無し、またはisHuman=false）なMA衣装間の移植では、スケール算出がArmatureのlossyScale比にフォールバックし、実際の体格差を反映しない | `ScaleCalculator.cs:44` | S05 |
| major | 移植先Armatureが常にマージ前のボディ骨格に固定され、MAのMergeArmature設定(prefix/suffix)を一切参照しないため衣装固有ボーンへの正しい対応付けができない | `AvatarData.cs:72` | S04b |
| major | AutoCreateHelperObjects/TryAutoCreateFromSerializedが生成する代替ボーンがソース元ボーンのローカル位置を引き継がず、常に親の原点に生成される | `PBRemapper.cs:240` |  |
| major | （推測）移植先自身のMA衣装マージ時、PBReplacer製スタブに実ボーンが吸収され宛先メッシュが破損する可能性 | `PBRemapper.cs:240` |  |
| major | MA検出時のCollectSkinnedBones(sourceAvatar)が常に空集合になり、IsSkeletonBone判定が機能しない | `PBRemapper.cs:187` | S04,S04b |
| major | PBRemapEditor.ScanComponentReferencesがsourceArmature外を参照するコンポーネントを無警告でシリアライズから除外する | `PBRemapEditor.cs:713` | S04,S04b |
| major | Prefab境界フォールバックが最初に見つかった「任意の」Prefabインスタンス境界で止まり、ネストしたPrefabのケースで小物Prefab自体をアバタールートと誤認する | `SourceDetector.cs:234` |  |
| major | FindLargestChildrenStructure は初期値 maxChildCount=0 のため、実体のあるボーン階層が無くても常に何らかのオブジェクトを『Armature』として確定してしまう | `AvatarData.cs:104` |  |
| major | 誤検出されたArmatureのlossyScaleがそのままPhysBone/Collider/Contactのradius等のスケール係数として使われる | `ScaleCalculator.cs:33` |  |
| major | 名前マッチ戦略の FirstOrDefault が、同名の子オブジェクトを複数持つ小物集合（複製されたProps等）で誤った対応先を選ぶ | `BoneMapper.cs:134` | S12 |
| major | OnHierarchyChangedはPBRemap直下の子オブジェクト数と親しか見ておらず、孫以下の変更やAddComponentによるコンポーネント追加を検知できない | `PBRemapEditor.cs:343` |  |
| major | Liveモードの自動作成ヘルパーオブジェクトがリマップ本体のUndoグループより前に登録され、1回のUndoで完全に戻らない／失敗時にオーファンとして残る | `PBRemapper.cs:86` |  |
| major | 「他のアバターへ移植」メニューはAvatarDynamics階層そのものではなく新規の空オブジェクトを生成するだけで、コンセプトと実装がズレている | `PBReplacerWindow.Events.cs:205` | なし（scenario_report_v1.mdはPBRemapコンポーネントの直接付与によるシナリオ検証であり、PBReplacerWindowの「⋮」メニュー経由のUIフローは検証対象外） |
| major | コンセプトの主要ユースケースであるPrefabモード（同一シーンにソースが無いD&D移植）ではSceneViewの可視化プレビューが動作しない | `PBRemapEditor.cs:1016` | コード確認のみ（scenario_report_v1.mdはSceneViewの可視化状態を直接検証していないが、S03/S03b/S03cはPrefabモード全般が実運用上機能しないことを裏付けている） |
| major | リマップルールの逆方向フォールバック（ApplyReverse系）がSource→Dest解決中にも無条件に試行され、無関係なボーンへ誤マッチしうる | `BoneMapper.cs:291` | 直接この衝突ケースを検証したシナリオはscenario_report_v1.mdに存在しないが、コード読解により機序は完全に裏付けられる |
| major | NDMFパスはリマップ失敗をNDMFのErrorReport機構に伝えず、失敗時もPBRemapを無条件に削除するためビルドが「サイレント失敗」する | `PBRemapNDMFPass.cs:51` | この失敗パス自体を直接テストしたシナリオはscenario_report_v1.mdには無いが、PBRemapNDMFPass.csの全文読解により機序は完全に裏付けられる |
| major | NDMFパッケージが無効/未インストールの環境では、VRChat SDKのIEditorOnlyストリップにより移植処理が実行されないままPBRemapが黙って消える | `PBRemap.cs:16` |  |
| major | Hips→Head距離ベースのスケール算出がワールド座標のポーズに依存し、T-pose以外・Animator実行中の姿勢では誤ったスケール比になる | `ScaleCalculator.cs:77` | S01 (0398d36): detection.sourceAvatarScale等はTポーズ相当の通常姿勢での実行のため直接的な姿勢ずれの実証は無いが、CalculateFromHumanoid経路自体（scale=1.2, Hips-Head距離比）が実際に稼働していることは確認できる |
| major | Hips→Head距離比は全身の頭身差を表すだけで、個々のPhysBoneが実際に付いているボーン枝（尻尾・耳・リボン等）の局所スケールとは無関係であり、単一グローバル値が全コンポーネントへ一律適用される | `PBRemapper.cs:80` | S01 (0398d36) post: Hair_Root 0.03→0.036, Skirt_L 0.02→0.024, HeadCollider 0.05→0.06, LeftHand Contact 0.04→0.048, Head Receiver 0.08→0.09599999 と全コンポーネントが同一のscale=1.2で一律スケールされている |
| major | ApplyScaleFactorはVRCPhysBone/Collider/ContactBaseのみをスケールし、VRCConstraintBase系のオフセット値（Position At Rest / Position Offset等）は一切スケールされない | `PBRemapper.cs:780` |  |
| major | Prefabモードでスケール未算出（SourceAvatarScale<=0）の状態はInspector上のラベルでのみ示唆され、Remap実行系・NDMFビルドログには一切警告が出ない | `PBRemapNDMFPass.cs:32` | 直接の実機検証は無し（S03/S03b/S03cはSerializedBoneReferences自体が0件のため別の早期エラーで停止しており、本所見が想定する『シリアライズ済みボーン参照はあるがSourceAvatarScaleだけ0』のケースは検証されていない）。コード読解のみによる確認 |
| minor | 複数MergeArmatureが存在する場合、AvatarData.DetectArmatureが配列の先頭要素を無条件採用するため、意図しないArmatureが選ばれ得る | `AvatarData.cs:154` |  |
| minor | Animator フォールバックが「最上位」を採用するため、無関係な祖先ヒエラルキーに引っ張られる | `SourceDetector.cs:257` |  |
| minor | root フォールバックは階層構造を検証せず transform.root をそのままアバタールートとして採用する | `SourceDetector.cs:270` | S10 |
| minor | UpdateSerializedBoneReferencesがInspectorを開いただけで無条件にApplyModifiedPropertiesWithoutUndoを実行し、Prefabオーバーライドとシーンdirty化を無自覚に発生させる | `PBRemapEditor.cs:686` |  |
| minor | PBRemapがアバタールート自身に付いている場合、Destination検出は常に自身を除外して親から走査するため未検出または誤検出になる | `SourceDetector.cs:150` | S09(ただし再設計前コードに対する実測であり、HEADはコード読解のみで確認) |
| minor | EditorApplication.hierarchyChangedは無関係な操作でも全PBRemap Inspectorインスタンス分発火し、対象自身の変化検知時は差分計算なしの全件フルスキャンが同期実行される | `PBRemapEditor.cs:317` | なし（scenario_report_v1.mdはPBRemapper.Remap()等のAPIを直接呼び出す機能検証であり、Inspectorのパフォーマンス/イベント発火挙動は測定対象外） |
| minor | ⋮メニューが対象とするアバターはHierarchy選択ではなくPBReplacerメインウィンドウにロード中のアバターであり、意図しないアバターにPBRemapが付く恐れがある | `PBReplacerWindow.Events.cs:185` |  |
| minor | 検出失敗時のガイダンスがInspectorを開かないと表示されず、文言も『AvatarDynamicsをドラッグ』という具体操作を示していない | `PBRemap.uxml:90` | なし（scenario_report_v1.mdはAPI呼び出しによる機能検証でありUI文言やSceneView表示は検証対象外） |
| minor | 移植実行完了後の状態遷移・後片付け導線が無く、PBRemapオブジェクトを削除してよいか判断できない | `PBRemap.uxml:89` | scenario_report_v1.mdの各シナリオpost状態にPBRemap削除に関する案内・処理が一切現れないことから間接的に確認（直接テストする専用シナリオIDはなし） |
| minor | PBRemap実行後にコンポーネントが削除されず、後続のNDMFビルドでスケールが二重適用される（無警告） | `PBRemapNDMFPass.cs:27` |  |
| minor | BuildHumanoidBoneMapの辞書上書きによるHumanoidボーン衝突と、Live/Prefab間での衝突解決ロジックの不一致 | `BoneMapper.cs:86` |  |
| minor | リマップルールのパターン文字列に"/"を含めた場合、パスのセグメント分割・再結合ロジックが破綻する | `BoneMapper.cs:156` |  |
| minor | lossyScaleフォールバックはY成分のみを比較しており、非一様スケール（XZのみの拡縮等）では実際のスケール差を代表できない | `ScaleCalculator.cs:44` | レポートに非一様スケール(X≠Y≠Z)を直接検証したシナリオは無し。コード読解のみによる確認 |
| minor | 同一アバターの別インスタンス間移植でもスケール1.0が保証されない：ポーズ依存(上記のCalculateFromHumanoid問題)に加え、NDMFビルド時はソース側が『生シーンの現在のTransform』を直接参照し続ける | `SourceDetector.cs:182` | 直接の実機検証なし（S08はIsReferencingDestination=trueのPrefabモード経路を検証したものであり、本所見が想定するLiveモードNDMFビルドのシナリオとは異なる）。所見提出者自身もis_speculation=trueと明記 |
| info | （確認事項・非バグ）PBRemapのGeneratingフェーズ実行はMAのMergeArmature(Transformingフェーズ)より前だが、MA側の事前ボーン保持スキャンにより整合性は保たれている | `PBRemapNDMFPlugin.cs:19` |  |

### 反証された所見

| 所見 | 反証理由 |
|---|---|
| 複数MergeArmatureに参照が分散する場合、外部参照ごとの多数決でSourceAvatarを一意に決めるため少数派側のボーンが誤ったArmature基準で解決される | 0398d36のPBRemapper.BuildBoneMap(151-170行付近)は sourceArmature.GetComponentsInChildren<Transform>(true)(151行目、sourceArmature=多数決で選ばれた勝者側のArmature)のみをループしてBoneMapper.ResolveBone/ResolveBoneWithRemapを呼ぶため、 |
| SourceAvatarScale が UI 駆動でしか保存されず、未取得(0)のまま Prefab 化されると AutoCalculateScale が無警告で scaleFactor=1.0 にフォールバックする | 0398d36のコードでRuntime/Scripts/PBRemap.cs:36(private float sourceAvatarScale;)、PBRemapEditor.cs:632-687(UpdateSerializedBoneReferences)、PBRemapper.cs:100-125(RemapPrefabMode)を実際に確認したところ、所見が引用する各行・各処理の存在自 |

### 網羅性批評で追加された観点

- [critical] PBRemapTrackerの自動処理（AutoOnDrop等）がPrefab Stage（Prefabモード編集中）でも無条件に発火し、共有Prefabアセットへ確認なしで自動移植・自動作成・自動スケールが焼き込まれる（`PBRemapTracker.cs:122`）: PBRemapTracker.FindAll() (122-127行) は `d.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(d)` のみで対象を絞り込んでいる。Unity の Prefab Stage（Prefabモード編集）中のオブジェクトは専用のプレビューシーンに実体化されており、scene.IsValid()==tru
- [critical] PBRemapの入れ子（PBRemap配下に別のPBRemap管理下ツリーが含まれる）が範囲を区別せず二重に走査・二重に適用される（`PBRemapManifestBuilder.cs:258`）: CollectVRCComponents(root) (258-266行) は `root.GetComponentsInChildren<T>(true)` で無条件にサブツリー全体を収集しており、途中に別のPBRemapコンポーネントが挟まっているかどうかを一切考慮しない。そのため、外側のPBRemap（本体一式用）のScan/Build/Applyは、内側のPBRemap（帽子+羽根用）が管
- [major] PBRemapTrackerの常時ポーリングが、PBRemapと無関係なあらゆるhierarchyChangedをトリガーに、プロジェクト内の全PBRemapへ対して重いフル再スキャンを実行する（`PBRemapTracker.cs:29`）: `EditorApplication.hierarchyChanged += () => _dirty = true;` (29行) はエディタ全体のどこでヒエラルキーが変わるたびに（PBRemapと無関係な変更でも）発火し、OnUpdate (36-67行) は0.5秒に一度、`Resources.FindObjectsOfTypeAll<PBRemap>()` (124行、ロード中の全アセット
- [major] PBRemapTracker.Suspendedはバッチ/CI用として用意されているが、コードベースのどこからも設定されておらず、-batchmodeガードも無いため、ヘッドレスビルドパイプライン中も自動移植ロジックが動き続ける（`PBRemapTracker.cs:25`）: `Suspended` プロパティ (25行) のXMLコメントには明示的に「自動処理を止める（テスト・バッチ用）」と書かれているが、リポジトリ全体を検索しても `PBRemapTracker.Suspended = true` のようにこれを設定している箇所は存在しない（設計上の唯一の無効化スイッチが未配線のデッドプロパティになっている）。また OnUpdate (38行) のガードは `Sus
- [minor] NDMFエラーレポートのen-usロケールが日本語辞書をそのまま返しており、英語環境のユーザーにも常に日本語のエラー文言が表示される（`PBRemapNDMFPass.cs:109`）: PBRemapErrorLocalizer.Create() (105-112行) は `nadena.dev.ndmf.localization.Localizer` に "ja-jp" と "en-us" の2ロケールを登録しているが、双方とも同一の `Ja` 辞書 (94-103行、日本語文字列のみ) を参照する `key => Ja.TryGetValue(key, out var v) 
- [major] ルート/コンテキスト検出がModularAvatarのMergeArmatureのみに対応しており、VRCFury等の同等機能（アーマチュアリンク）には一切対応していない（`PBRemapContextResolver.cs:165`）: 衣装ルート判定 `IsCostumeRoot` (165-178行) は `#if MODULAR_AVATAR` ブロック内で `ModularAvatarMergeArmature` の有無のみをチェックしており、`ClassifyRootCandidate`（RootKind.MACostume）や `BuildContexts`（衣装Armatureコンテキストの列挙、220行付近の `r
- [minor] AutoOnDropの自動移植はドロップ操作の最大0.5秒後にポーリング経由で実行されるため、その間に行われたユーザー操作とUndoスタック上の順序が食い違い、1回のCtrl+Zで意図しない操作が取り消される（`PBRemapTracker.cs:78`）: OnDropped の AutoOnDrop 分岐 (73-88行) は、ドラッグ操作（Unityのヒエラルキー上での実際のreparent操作、それ自体が既に1つのUndoエントリになる）そのものの中では実行されず、次のOnUpdateポーリング(40行のRefreshIntervalによって最短でも0.5秒後)で初めて `PBRemapper.Remap(def)` (78行、内部でUndo.
- [major] VRCConstraintBaseのSources/TargetTransformの付け替えは参照の差し替えのみで、ソースとデスティネーションのボーンの向き（ローカル座標系）の違いを一切補正しないため、スケール差が無くてもコンストレイントのオフセットが破綻する（`PBRemapApplier.cs:242`）: ApplyScale (204-245行) のswitch文は `VRCPhysBoneBase`/`VRCPhysBoneColliderBase`/`ContactBase` のみを明示的に処理し、`VRCConstraintBase` はどちらの分岐（226-234行の非冪等パス、236-244行の冪等パス）にもcaseが無いため `default: return;` (242行) で完全に
