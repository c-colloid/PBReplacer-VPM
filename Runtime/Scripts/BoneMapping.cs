using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// ボーンマッピングのプレビュー用データクラス。
    /// ソースボーンとデスティネーションボーンの対応関係を保持する。
    /// </summary>
    [Serializable]
    public class BoneMapping
    {
        /// <summary>ソース側のボーンパス（Armatureからの相対パス）</summary>
        public string sourceBonePath;

        /// <summary>デスティネーション側のボーンパス（Armatureからの相対パス）</summary>
        public string destinationBonePath;

        /// <summary>ボーンが正常に解決されたかどうか</summary>
        public bool resolved;

        /// <summary>解決に失敗した場合のエラーメッセージ</summary>
        public string errorMessage;

        /// <summary>親ボーンが解決済みで、子オブジェクトの自動作成が可能か</summary>
        public bool autoCreatable;

        /// <summary>自動作成時のデスティネーション側パス（親のdestPath + "/" + ソースのボーン名）</summary>
        public string autoCreateDestPath;

        /// <summary>複数候補があり自動確定できない</summary>
        public bool ambiguous;

        /// <summary>ユーザーが手動で確定した</summary>
        public bool manual;

        /// <summary>移植元の外側（アバター内衣装に対するアバター）のボーンか</summary>
        public bool isOuter;

        /// <summary>解決方法（表示用）</summary>
        public string method;

        /// <summary>対応するコンポーネントとプロパティ（表示用, "PhysBones/Hair.rootTransform"）</summary>
        public string referenceKey;

        /// <summary>手動マッピング用のキー（"{contextId}:{relPath}|{pathFromRoot}"）</summary>
        public string sourceKey;

        /// <summary>ソース側Transform（同一シーン時のみ。シリアライズしない）</summary>
        [NonSerialized] public Transform sourceTransform;

        /// <summary>デスティネーション側Transform（解決済み時。シリアライズしない）</summary>
        [NonSerialized] public Transform destinationTransform;

        /// <summary>自動作成時の親Transform（シリアライズしない）</summary>
        [NonSerialized] public Transform autoCreateParentTransform;

        /// <summary>曖昧な場合の候補Transform（シリアライズしない）</summary>
        [NonSerialized] public List<Transform> candidateTransforms = new List<Transform>();
    }
}
