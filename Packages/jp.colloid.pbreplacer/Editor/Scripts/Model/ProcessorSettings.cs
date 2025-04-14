using System;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// コンポーネント処理の設定を保持するクラス
    /// </summary>
    [Serializable]
    public class ProcessorSettings
    {
        // 共通設定
        public bool ShowProgressBar { get; set; } = true;
        public bool PreserveHierarchy { get; set; } = true;
	    public bool ShowConfirmDialog { get; set; } = true;
	    public bool UnpackPrefab { get; set; } = true;
	    public bool DestroyUnusedObject { get; set; } = true;
	    public FindComponent FindComponent { get; set; } = 0;
        
        // 親オブジェクト設定
        public string RootObjectName { get; set; } = "AvatarDynamics";
        public string RootPrefabName { get; set; } = "AvatarDynamics";
        
        // PhysBone関連設定
        public string PhysBonesFolder { get; set; } = "PhysBones";
        public string PhysBoneCollidersFolder { get; set; } = "PhysBoneColliders";
        
        // コンストレイント関連設定
        public string ConstraintsFolder { get; set; } = "Constraints";
        public string PositionConstraintsFolder { get; set; } = "Position";
        public string RotationConstraintsFolder { get; set; } = "Rotation";
        public string ScaleConstraintsFolder { get; set; } = "Scale";
        public string ParentConstraintsFolder { get; set; } = "Parent";
        public string LookAtConstraintsFolder { get; set; } = "LookAt";
        public string AimConstraintsFolder { get; set; } = "Aim";
        
        // コンタクト関連設定
        public string ContactsFolder { get; set; } = "Contacts";
        public string SenderFolder { get; set; } = "Sender";
        public string ReceiverFolder { get; set; } = "Receiver";
        
        // クローンメソッド
        public ProcessorSettings Clone()
        {
            return new ProcessorSettings
            {
                ShowProgressBar = this.ShowProgressBar,
                PreserveHierarchy = this.PreserveHierarchy,
                ShowConfirmDialog = this.ShowConfirmDialog,
                RootObjectName = this.RootObjectName,
                RootPrefabName = this.RootPrefabName,
                PhysBonesFolder = this.PhysBonesFolder,
                PhysBoneCollidersFolder = this.PhysBoneCollidersFolder,
                ConstraintsFolder = this.ConstraintsFolder,
                PositionConstraintsFolder = this.PositionConstraintsFolder,
                RotationConstraintsFolder = this.RotationConstraintsFolder,
                ScaleConstraintsFolder = this.ScaleConstraintsFolder,
                ParentConstraintsFolder = this.ParentConstraintsFolder,
                LookAtConstraintsFolder = this.LookAtConstraintsFolder,
                AimConstraintsFolder = this.AimConstraintsFolder,
                ContactsFolder = this.ContactsFolder,
                SenderFolder = this.SenderFolder,
	            ReceiverFolder = this.ReceiverFolder,
                
	            UnpackPrefab = this.UnpackPrefab,
	            DestroyUnusedObject = this.DestroyUnusedObject,
	            FindComponent = this.FindComponent
            };
        }
    }
}