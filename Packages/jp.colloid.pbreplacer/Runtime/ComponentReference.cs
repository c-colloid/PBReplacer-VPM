using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// コンポーネントの種類
    /// </summary>
    public enum DynamicsComponentType
    {
        PhysBone,
        PhysBoneCollider,
        ContactSender,
        ContactReceiver,
        PositionConstraint,
        RotationConstraint,
        ScaleConstraint,
        ParentConstraint,
        LookAtConstraint,
        AimConstraint
    }

    /// <summary>
    /// Transform参照をパスで保持するための構造体
    /// </summary>
    [Serializable]
    public class TransformPathReference
    {
        [Tooltip("参照先のパス（Armatureからの相対パス）")]
        public string relativePath;

        [Tooltip("参照先の絶対パス（アバタールートからの相対パス）")]
        public string absolutePath;

        [Tooltip("HumanoidボーンタイプHintName（あれば）")]
        public string humanoidBoneHint;

        [Tooltip("解決済みのTransform参照（ランタイムで設定される）")]
        [NonSerialized]
        public Transform resolvedTransform;

        public TransformPathReference() { }

        public TransformPathReference(string relativePath, string absolutePath, string humanoidBoneHint = null)
        {
            this.relativePath = relativePath;
            this.absolutePath = absolutePath;
            this.humanoidBoneHint = humanoidBoneHint;
        }

        /// <summary>
        /// パスが有効かどうか
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(relativePath) || !string.IsNullOrEmpty(absolutePath);
    }

    /// <summary>
    /// Collider参照のマッピング情報
    /// </summary>
    [Serializable]
    public class ColliderReference
    {
        [Tooltip("このコンポーネントのローカルインデックス")]
        public int localIndex;

        [Tooltip("Colliderのパス参照")]
        public TransformPathReference pathReference;

        public ColliderReference() { }

        public ColliderReference(int localIndex, TransformPathReference pathReference)
        {
            this.localIndex = localIndex;
            this.pathReference = pathReference;
        }
    }

    /// <summary>
    /// 単一コンポーネントの参照情報
    /// </summary>
    [Serializable]
    public class ComponentReferenceData
    {
        [Tooltip("コンポーネントが配置されているGameObject名")]
        public string gameObjectName;

        [Tooltip("コンポーネントの種類")]
        public DynamicsComponentType componentType;

        [Tooltip("RootTransformのパス参照")]
        public TransformPathReference rootTransformPath;

        [Tooltip("追加のTransform参照リスト（IgnoreTransforms等）")]
        public List<TransformPathReference> additionalTransformPaths = new List<TransformPathReference>();

        [Tooltip("Collider参照リスト（PhysBone用）")]
        public List<ColliderReference> colliderReferences = new List<ColliderReference>();

        [Tooltip("ソースTransform参照リスト（Constraint用）")]
        public List<TransformPathReference> sourceTransformPaths = new List<TransformPathReference>();

        [Tooltip("ターゲットTransform参照（Constraint用）")]
        public TransformPathReference targetTransformPath;

        public ComponentReferenceData() { }

        public ComponentReferenceData(string gameObjectName, DynamicsComponentType componentType)
        {
            this.gameObjectName = gameObjectName;
            this.componentType = componentType;
        }
    }
}
