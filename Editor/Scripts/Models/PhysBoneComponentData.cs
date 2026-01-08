using System;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PhysBoneコンポーネントのデータを保持するクラス
    /// </summary>
    [Serializable]
    public class PhysBoneComponentData
    {
        // コンポーネントID（一意に識別するため）
        public string Id { get; private set; }
        
        // 元のコンポーネントへの参照
        public VRCPhysBone Component { get; private set; }
        
        // コンポーネントの種類（PhysBoneかPhysBoneCollider）
        public PhysBoneComponentType ComponentType { get; private set; }
        
        // コンポーネントが付いているゲームオブジェクト
        public GameObject GameObject { get; private set; }
        
        // ルートトランスフォーム（未設定の場合はnull）
        public Transform RootTransform { get; private set; }
        
        // コンポーネント名（表示用）
        public string Name { get; private set; }
        
        // パス（階層を表示するため）
        public string Path { get; private set; }
        
        // 展開状態（UIでの表示状態）
        public bool IsExpanded { get; set; }
        
        // 選択状態（UIでの選択状態）
        public bool IsSelected { get; set; }

        /// <summary>
        /// PhysBoneからデータを作成するコンストラクタ
        /// </summary>
        public PhysBoneComponentData(VRCPhysBone physBone)
        {
            if (physBone == null)
                throw new ArgumentNullException(nameof(physBone));
                
            Id = System.Guid.NewGuid().ToString();
            Component = physBone;
            ComponentType = PhysBoneComponentType.PhysBone;
            GameObject = physBone.gameObject;
            RootTransform = physBone.rootTransform;
            Name = physBone.name;
            Path = GetHierarchyPath(physBone.transform);
            IsExpanded = false;
            IsSelected = false;
        }
        
        /// <summary>
        /// PhysBoneColliderからデータを作成するコンストラクタ
        /// </summary>
        public PhysBoneComponentData(VRCPhysBoneCollider collider)
        {
            if (collider == null)
                throw new ArgumentNullException(nameof(collider));
                
            Id = System.Guid.NewGuid().ToString();
            Component = null; // VRCPhysBoneBaseに変換できれば共通処理できる
            ComponentType = PhysBoneComponentType.PhysBoneCollider;
            GameObject = collider.gameObject;
            RootTransform = collider.rootTransform;
            Name = collider.name;
            Path = GetHierarchyPath(collider.transform);
            IsExpanded = false;
            IsSelected = false;
        }
        
        /// <summary>
        /// ヒエラルキーパスを取得
        /// </summary>
        private string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;
                
            string path = transform.name;
            Transform parent = transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
        
        /// <summary>
        /// 表示名を取得（ルートトランスフォームが設定されている場合は表示）
        /// </summary>
        public string GetDisplayName()
        {
            if (RootTransform != null && RootTransform != GameObject.transform)
            {
                return $"{Name} (Root: {RootTransform.name})";
            }
            return Name;
        }
    }
    
    /// <summary>
    /// PhysBoneコンポーネントの種類
    /// </summary>
    public enum PhysBoneComponentType
    {
        PhysBone,
        PhysBoneCollider
    }
}
