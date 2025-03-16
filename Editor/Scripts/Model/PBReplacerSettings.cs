using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerの設定を管理するクラス
	/// </summary>
	[Serializable]
	public class PBReplacerSettings
	{
		// 設定ファイルのパス
		private static readonly string SettingsPath = "Library/PBReplacerSettings.json";
        
		// 前回使用したアバターのGUID
		public string LastAvatarGUID;
        
		// 自動的に前回のアバターを読み込むかどうか
		public bool AutoLoadLastAvatar = true;
        
		// コンポーネント処理時に確認ダイアログを表示するかどうか
		public bool ShowConfirmDialog = true;
        
		// 処理進捗を表示するかどうか
		public bool ShowProgressBar = true;
        
		// ダークモード対応（ユーザー設定に従う）
		public bool FollowEditorTheme = true;
        
		// PhysBoneの設定内容を表示するかどうか
		public bool ShowPhysBoneSettings = false;
        
		// 親子階層構造を維持するかどうか
		public bool PreserveHierarchy = true;
        
		// Animatorがないオブジェクトでもアーマチュアを検出するかどうか
		public bool DetectNonAnimatorArmature = true;
		
		/// <summary>
		/// ProcessorSettings関連
		/// </summary>
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
		
		public static event Action OnSettingsChanged;
        
		// 設定を保存
		public void Save()
		{
			try
			{
				string json = JsonUtility.ToJson(this, true);
				File.WriteAllText(SettingsPath, json);
				
				OnSettingsChanged?.Invoke();
			}
				catch (Exception ex)
				{
					Debug.LogError($"設定の保存中にエラーが発生しました: {ex.Message}");
				}
		}
        
		// 設定を読み込み
		public static PBReplacerSettings Load()
		{
			try
			{
				if (File.Exists(SettingsPath))
				{
					string json = File.ReadAllText(SettingsPath);
					return JsonUtility.FromJson<PBReplacerSettings>(json);
				}
			}
				catch (Exception ex)
				{
					Debug.LogError($"設定の読み込み中にエラーが発生しました: {ex.Message}");
				}
            
			return new PBReplacerSettings();
		}
		
		public static PBReplacerSettings GetLatestSettings()
		{
			return Load();
		}
        
		// アバターのGUIDを保存
		public void SaveLastAvatarGUID(GameObject avatar)
		{
			if (avatar == null)
			{
				LastAvatarGUID = string.Empty;
				Save();
				return;
			}
            
			try
			{
				string assetPath = AssetDatabase.GetAssetPath(avatar);
				if (!string.IsNullOrEmpty(assetPath))
				{
					LastAvatarGUID = AssetDatabase.AssetPathToGUID(assetPath);
				}
				else
				{
					// シーン内のオブジェクトの場合はインスタンスIDを使用
					LastAvatarGUID = $"instance_{avatar.GetInstanceID()}";
				}
                
				Save();
			}
				catch (Exception ex)
				{
					Debug.LogError($"アバターGUIDの保存中にエラーが発生しました: {ex.Message}");
				}
		}
        
		// 前回のアバターを読み込み
		public GameObject LoadLastAvatar()
		{
			if (string.IsNullOrEmpty(LastAvatarGUID))
			{
				return null;
			}
            
			try
			{
				// インスタンスIDの場合
				if (LastAvatarGUID.StartsWith("instance_"))
				{
					string instanceIdStr = LastAvatarGUID.Substring("instance_".Length);
					if (int.TryParse(instanceIdStr, out int instanceId))
					{
						return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
					}
					return null;
				}
                
				// アセットの場合
				string assetPath = AssetDatabase.GUIDToAssetPath(LastAvatarGUID);
				if (!string.IsNullOrEmpty(assetPath))
				{
					return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
				}
			}
				catch (Exception ex)
				{
					Debug.LogError($"前回のアバター読み込み中にエラーが発生しました: {ex.Message}");
				}
            
			return null;
		}
		
		// ProcessorSettings相当のインスタンスを取得するメソッド
		public ProcessorSettings GetProcessorSettings()
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
				ReceiverFolder = this.ReceiverFolder
			};
		}
		
		protected void NotifySettingsChanged()
		{
			OnSettingsChanged?.Invoke();
		}
	}
}