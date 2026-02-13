using System;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Prefab用にボーン参照情報をシリアライズするデータクラス。
    /// Transform参照はPrefab化時にnullになるため、ボーンパスとHumanoid情報を文字列/enumで保持する。
    /// </summary>
    [Serializable]
    public class SerializedBoneReference
    {
        /// <summary>PBRemapからコンポーネントのGameObjectへの相対パス（例: "PhysBones/Hair_PB"）</summary>
        public string componentObjectPath;

        /// <summary>コンポーネントの型名（例: "VRCPhysBone"）</summary>
        public string componentTypeName;

        /// <summary>SerializedPropertyのパス（例: "rootTransform"）</summary>
        public string propertyPath;

        /// <summary>ソースArmatureからの相対ボーンパス（例: "Hips/Spine/Chest/Neck/Head"）</summary>
        public string boneRelativePath;

        /// <summary>このボーン自体のHumanoid ID。非Humanoidボーンの場合はLastBone</summary>
        public HumanBodyBones humanBodyBone = HumanBodyBones.LastBone;

        /// <summary>最も近いHumanoid祖先ボーンのID。なしの場合はLastBone</summary>
        public HumanBodyBones nearestHumanoidAncestor = HumanBodyBones.LastBone;

        /// <summary>Humanoid祖先からの相対パス（例: "Hair_Root/Hair_01"）</summary>
        public string pathFromHumanoidAncestor;
    }
}
