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
		// 旧設定ファイルのパス（移行元。VCS管理外）
		private static readonly string LegacySettingsPath = "Library/PBReplacerSettings.json";

		// プロジェクト設定（処理結果に影響・チーム共有）の保存先。VCSコミット対象
		private static readonly string ProjectSettingsPath = "ProjectSettings/PBReplacerSettings.json";

		// 個人設定（UI/利便性）のEditorPrefsキー。EditorPrefsは全プロジェクト共通のため
		// プロジェクト固有の値をここに保存しないこと
		private const string PrefKeyAutoLoadLastAvatar = "PBReplacer.AutoLoadLastAvatar";
		private const string PrefKeyShowConfirmDialog = "PBReplacer.ShowConfirmDialog";
		private const string PrefKeyShowProgressBar = "PBReplacer.ShowProgressBar";

		// LastAvatarGUIDはプロジェクト内アセットのGUID参照でありプロジェクト固有の値のため、
		// 全プロジェクト共通のEditorPrefsではなくプロジェクトごとのEditorUserSettingsへ保存
		private const string ConfigKeyLastAvatarGUID = "PBReplacer.LastAvatarGUID";

		// 旧設定からの移行済みフラグ。移行要否はプロジェクトごとに判定する必要があるため
		// こちらもEditorPrefsではなくプロジェクトごとのEditorUserSettingsへ保存
		private const string ConfigKeyMigratedFromLegacy = "PBReplacer.MigratedFromLegacyJson";
        
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
		
		// Prefabの継承を破棄するかどうか
		public bool UnpackPrefab = false;

		// 未使用のフォルダを削除するかどうか
		public bool DestroyUnusedObject = true;

		// コンポーネントを検索する階層
		public FindComponent FindComponent = 0;
		
		// 親オブジェクト設定
		const string rootobjectname = "AvatarDynamics";
		public string RootObjectName { get; set; } = "AvatarDynamics";
		public string RootPrefabName { get => rootobjectname; }
		public string RootPrefabPath { get => $"Prefab/{rootobjectname}"; }
    
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
				// プロジェクト設定（処理結果に影響・チーム共有）はProjectSettingsへJsonUtilityで保存
				var projectData = new ProjectSettingsData
				{
					UnpackPrefab = UnpackPrefab,
					DestroyUnusedObject = DestroyUnusedObject,
					FindComponent = FindComponent
				};
				string json = JsonUtility.ToJson(projectData, true);
				File.WriteAllText(ProjectSettingsPath, json);

				// 個人設定（UI/利便性）はEditorPrefsへ保存
				EditorPrefs.SetBool(PrefKeyAutoLoadLastAvatar, AutoLoadLastAvatar);
				EditorPrefs.SetBool(PrefKeyShowConfirmDialog, ShowConfirmDialog);
				EditorPrefs.SetBool(PrefKeyShowProgressBar, ShowProgressBar);

				// LastAvatarGUIDはプロジェクト固有の参照のためEditorUserSettingsへ保存
				EditorUserSettings.SetConfigValue(ConfigKeyLastAvatarGUID, LastAvatarGUID ?? string.Empty);

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
			// 旧Library配下のJSONからの自動移行（初回のみ）
			TryMigrateFromLegacyJson();

			var settings = new PBReplacerSettings();

			try
			{
				if (File.Exists(ProjectSettingsPath))
				{
					string json = File.ReadAllText(ProjectSettingsPath);
					var projectData = JsonUtility.FromJson<ProjectSettingsData>(json);
					if (projectData != null)
					{
						settings.UnpackPrefab = projectData.UnpackPrefab;
						settings.DestroyUnusedObject = projectData.DestroyUnusedObject;
						settings.FindComponent = projectData.FindComponent;
					}
				}
			}
				catch (Exception ex)
				{
					Debug.LogError($"設定の読み込み中にエラーが発生しました: {ex.Message}");
				}

			string savedGuid = EditorUserSettings.GetConfigValue(ConfigKeyLastAvatarGUID);
			settings.LastAvatarGUID = string.IsNullOrEmpty(savedGuid) ? (settings.LastAvatarGUID ?? string.Empty) : savedGuid;
			settings.AutoLoadLastAvatar = EditorPrefs.GetBool(PrefKeyAutoLoadLastAvatar, settings.AutoLoadLastAvatar);
			settings.ShowConfirmDialog = EditorPrefs.GetBool(PrefKeyShowConfirmDialog, settings.ShowConfirmDialog);
			settings.ShowProgressBar = EditorPrefs.GetBool(PrefKeyShowProgressBar, settings.ShowProgressBar);

			return settings;
		}

		// 旧Library/PBReplacerSettings.jsonから新形式（ProjectSettings + EditorPrefs）への自動移行
		private static void TryMigrateFromLegacyJson()
		{
			// 既に移行判定済みなら何もしない（再移行はしない）。
			// 移行要否はプロジェクトごとに異なるため、全プロジェクト共通のEditorPrefsではなく
			// プロジェクトごとのEditorUserSettingsで判定する
			if (EditorUserSettings.GetConfigValue(ConfigKeyMigratedFromLegacy) == "1")
			{
				return;
			}

			try
			{
				bool legacyExists = File.Exists(LegacySettingsPath);
				bool newFormatSaved = File.Exists(ProjectSettingsPath);

				if (legacyExists && !newFormatSaved)
				{
					string json = File.ReadAllText(LegacySettingsPath);
					var legacy = JsonUtility.FromJson<PBReplacerSettings>(json);
					if (legacy != null)
					{
						legacy.Save();
						Debug.Log("既存設定を引き継ぎました");
					}
				}
			}
				catch (Exception ex)
				{
					Debug.LogError($"旧設定の移行中にエラーが発生しました: {ex.Message}");
				}
				finally
				{
					EditorUserSettings.SetConfigValue(ConfigKeyMigratedFromLegacy, "1");
				}
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
		
		protected void NotifySettingsChanged()
		{
			OnSettingsChanged?.Invoke();
		}

		// プロジェクト設定（処理結果に影響・チーム共有）のシリアライズ用データ
		[Serializable]
		private class ProjectSettingsData
		{
			public bool UnpackPrefab;
			public bool DestroyUnusedObject;
			public FindComponent FindComponent;
		}
	}

	public enum FindComponent
	{
		[InspectorName("アーマチュア内のみ")]
		InArmature = 0,
		[InspectorName("同一Prefab内まで")]
		InPrefab = 1,
		[InspectorName("子階層全て")]
		AllChilds = 2
	}
}