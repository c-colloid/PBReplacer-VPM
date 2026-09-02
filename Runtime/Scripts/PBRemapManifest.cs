using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 参照先ボーンが属する「コンテキスト」（本体Armature / MA衣装Armature / 汎用ルート）の種別。
    /// </summary>
    public enum BoneContextKind
    {
        /// <summary>アバター本体のArmature（Humanoidなら Hips.parent）</summary>
        Main,
        /// <summary>ModularAvatar MergeArmature が付いた衣装のArmature</summary>
        Costume,
        /// <summary>Animator/Descriptorの無い汎用オブジェクト（小物等）</summary>
        Generic,
    }

    /// <summary>
    /// 参照先ボーンが属するArmatureの情報。マニフェストは参照ごとにこのコンテキストIDを持つ。
    /// </summary>
    [Serializable]
    public class BoneContext
    {
        /// <summary>コンテキストID（マニフェスト内で一意）</summary>
        public int id;

        /// <summary>種別</summary>
        public BoneContextKind kind = BoneContextKind.Main;

        /// <summary>移植元ルートから見た Armature の相対パス（Main: "Armature", Costume: "Costume/Armature" 等）</summary>
        public string armaturePathFromRoot = "";

        /// <summary>Armature オブジェクト名</summary>
        public string armatureName = "";

        /// <summary>Humanoid（Animator.isHuman）か</summary>
        public bool isHumanoid;

        /// <summary>MA MergeArmature の prefix（Costume のみ）</summary>
        public string maPrefix = "";

        /// <summary>MA MergeArmature の suffix（Costume のみ）</summary>
        public string maSuffix = "";

        /// <summary>MA MergeArmature の mergeTarget referencePath（Costume のみ）</summary>
        public string maMergeTargetPath = "";

        /// <summary>衣装のルートオブジェクト名（Costume のみ。移植先で同じ衣装を探す手掛かり）</summary>
        public string costumeName = "";

        /// <summary>衣装ルートの移植元ルートからの相対パス（Costume のみ）</summary>
        public string costumeRootPathFromRoot = "";

        /// <summary>Armature の lossyScale（スケール参照用）</summary>
        public Vector3 armatureLossyScale = Vector3.one;
    }

    /// <summary>
    /// コンポーネントのプロパティから外部ボーンへの参照1件。
    /// </summary>
    [Serializable]
    public class BoneRef
    {
        /// <summary>PBRemapからコンポーネントのGameObjectへの相対パス（例: "PhysBones/Hair_PB"）</summary>
        public string componentPath = "";

        /// <summary>コンポーネントの型名（例: "VRCPhysBone"）</summary>
        public string componentType = "";

        /// <summary>SerializedPropertyのパス（例: "rootTransform", "colliders.Array.data[0]"）</summary>
        public string propertyPath = "";

        /// <summary>所属コンテキストID</summary>
        public int contextId;

        /// <summary>コンテキストArmatureからの相対パス（例: "Hips/Spine/Chest/Neck/Head/Hair_Root"）</summary>
        public string relPath = "";

        /// <summary>ボーン名</summary>
        public string boneName = "";

        /// <summary>このボーン自体のHumanoid ID。非Humanoidボーンの場合はLastBone</summary>
        public HumanBodyBones humanBone = HumanBodyBones.LastBone;

        /// <summary>最も近いHumanoid祖先ボーンのID。なしの場合はLastBone</summary>
        public HumanBodyBones nearestHumanoidAncestor = HumanBodyBones.LastBone;

        /// <summary>Humanoid祖先からの相対パス（例: "Hair_Root/Hair_01"）</summary>
        public string pathFromAncestor = "";

        /// <summary>このボーンまたは子孫がSkinnedMeshRendererにバインドされているか</summary>
        public bool isSkeletonBone;

        /// <summary>元ボーンのローカル位置（自動作成用）</summary>
        public Vector3 localPosition;

        /// <summary>元ボーンのローカル回転（自動作成用）</summary>
        public Quaternion localRotation = Quaternion.identity;

        /// <summary>元ボーンのローカルスケール（自動作成用）</summary>
        public Vector3 localScale = Vector3.one;

        /// <summary>元ボーンの lossyScale（VRC SDK はこれを radius 等に乗算する）</summary>
        public Vector3 lossyScale = Vector3.one;

        /// <summary>参照先がボーンではなく、コンポーネント参照（PhysBoneCollider等）の場合の型名。ボーン参照なら空</summary>
        public string targetComponentType = "";

        /// <summary>移植元ルートからの相対パス（コンテキスト外の参照のフォールバック用）</summary>
        public string pathFromRoot = "";

        /// <summary>解決用のキー（componentPath + "." + propertyPath）</summary>
        public string Key => componentPath + "." + propertyPath;
    }

    /// <summary>
    /// 移植元での元値（冪等な適用のために保持）。
    /// </summary>
    [Serializable]
    public class OriginalValues
    {
        public string componentPath = "";
        public string componentType = "";
        public float radius;
        public float height;
        public Vector3 position;
        public Vector3 endpointPosition;
        /// <summary>PhysBoneのrootTransform（またはコンポーネントの親）の lossyScale の最大成分</summary>
        public float rootLossyScaleMax = 1f;
    }

    /// <summary>
    /// 移植元のスケール基準。
    /// </summary>
    [Serializable]
    public class ScaleReference
    {
        /// <summary>Hips→Head のワールド距離（Humanoidのみ、なければ0）</summary>
        public float hipsToHead;

        /// <summary>本体Armatureの lossyScale.y</summary>
        public float armatureLossyScaleY = 1f;

        /// <summary>Humanoid でない場合の参照用: 参照ボーンとその親のワールド距離（BoneRef順）</summary>
        public List<float> boneParentDistances = new List<float>();
    }

    /// <summary>
    /// PBRemap配下のコンポーネントが移植元でどのボーンを参照していたかを完全に記述するマニフェスト。
    /// 移植元にいる間に自動取得され、Prefab化・別シーン/別プロジェクトへの持ち出し後も解決に使える。
    /// </summary>
    [Serializable]
    public class PBRemapManifest
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        /// <summary>取得日時（UTC, ISO8601）</summary>
        public string capturedAtUtc = "";

        /// <summary>移植元ルートのオブジェクト名</summary>
        public string sourceRootName = "";

        /// <summary>移植元ルートの種別（VRCAvatarDescriptor / MergeArmature / Animator / Generic）</summary>
        public string sourceRootKind = "";

        /// <summary>移植元ルートのインスタンスID（同一シーンでの同一性判定用。別シーンでは無効）</summary>
        public int sourceRootInstanceId;

        public List<BoneContext> contexts = new List<BoneContext>();
        public List<BoneRef> refs = new List<BoneRef>();
        public List<OriginalValues> originals = new List<OriginalValues>();
        public ScaleReference scaleReference = new ScaleReference();

        public bool IsEmpty => refs == null || refs.Count == 0;

        public BoneContext GetContext(int id)
        {
            foreach (var c in contexts) if (c.id == id) return c;
            return null;
        }

        public OriginalValues GetOriginal(string componentPath, string componentType)
        {
            foreach (var o in originals)
                if (o.componentPath == componentPath && o.componentType == componentType) return o;
            return null;
        }
    }

    /// <summary>
    /// ユーザーが手動で確定したボーン対応。解決時に最優先で使われる。
    /// </summary>
    [Serializable]
    public class ManualMapping
    {
        /// <summary>移植元側のキー: "{contextId}:{relPath}"（コンテキストArmature基準）</summary>
        public string sourceKey = "";

        /// <summary>表示用: 移植元ボーンの相対パス</summary>
        public string sourcePath = "";

        /// <summary>移植先ボーン（同一シーン内でのみ有効）</summary>
        public Transform target;

        /// <summary>移植先ルートからの相対パス（Prefab化やシーン跨ぎ後のフォールバック）</summary>
        public string targetPathFromRoot = "";
    }

    /// <summary>
    /// 適用済み記録。ビルド時/再実行時の二重適用を防ぐ。
    /// </summary>
    [Serializable]
    public class AppliedRecord
    {
        public bool isApplied;
        public string destinationRootName = "";
        public int destinationRootInstanceId;
        public string appliedAtUtc = "";
        public float worldScaleRatio = 1f;
        public string sourceRootName = "";
    }

    /// <summary>PBRemap の適用モード</summary>
    public enum PBRemapApplyMode
    {
        /// <summary>ドロップ時に自動で解決・適用する（曖昧/未解決があればウィンドウを開く）</summary>
        AutoOnDrop = 0,
        /// <summary>ドロップ時にプレビューを開き、ユーザーが適用を押す</summary>
        Confirm = 1,
        /// <summary>編集時は何もしない。NDMFビルド時にのみ非破壊で適用する</summary>
        BuildOnly = 2,
    }
}
