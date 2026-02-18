using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// SceneViewプレビューの共有状態を管理するシングルトン。
	/// プレビューデータの設定とレンダリング設定を保持する。
	/// </summary>
	public class PBRemapScenePreviewState
	{
		private static PBRemapScenePreviewState _instance;
		public static PBRemapScenePreviewState Instance =>
			_instance ??= new PBRemapScenePreviewState();

		/// <summary>フィルター状態が変更された時に発火するイベント</summary>
		public event Action FilterStateChanged;

		/// <summary>プレビューデータが変更された時に発火するイベント（Activate/Deactivate）</summary>
		public event Action PreviewDataChanged;

		public bool IsActive { get; private set; }

		public PBRemapPreviewData PreviewData { get; private set; }
		public SourceDetector.DetectionResult Detection { get; private set; }

		/// <summary>ワールド座標解決済みのボーンマッピングキャッシュ</summary>
		public List<BoneMappingVisual> VisualMappings { get; private set; }
			= new List<BoneMappingVisual>();

		// 表示設定
		public bool ShowConnectionLines { get; set; } = true;
		public bool ShowBoneLabels { get; set; } = false;

		private bool _showResolved = true;
		private bool _showAutoCreatable = true;
		private bool _showUnresolved = true;

		public bool ShowResolved
		{
			get => _showResolved;
			set { if (_showResolved != value) { _showResolved = value; FilterStateChanged?.Invoke(); } }
		}

		public bool ShowAutoCreatable
		{
			get => _showAutoCreatable;
			set { if (_showAutoCreatable != value) { _showAutoCreatable = value; FilterStateChanged?.Invoke(); } }
		}

		public bool ShowUnresolved
		{
			get => _showUnresolved;
			set { if (_showUnresolved != value) { _showUnresolved = value; FilterStateChanged?.Invoke(); } }
		}

		// サマリー
		public int ResolvedCount { get; private set; }
		public int AutoCreatableCount { get; private set; }
		public int TotalCount { get; private set; }

		/// <summary>
		/// プレビューデータと検出結果を設定し、ビジュアルキャッシュを再構築する。
		/// </summary>
		public void Activate(PBRemapPreviewData previewData,
			SourceDetector.DetectionResult detection)
		{
			PreviewData = previewData;
			Detection = detection;
			IsActive = true;
			RebuildVisualCache();
			PreviewDataChanged?.Invoke();
		}

		/// <summary>
		/// プレビューを非アクティブにし、キャッシュをクリアする。
		/// </summary>
		public void Deactivate()
		{
			IsActive = false;
			PreviewData = null;
			Detection = null;
			VisualMappings.Clear();
			ResolvedCount = 0;
			AutoCreatableCount = 0;
			TotalCount = 0;
			SceneView.RepaintAll();
			PreviewDataChanged?.Invoke();
		}

		/// <summary>
		/// BoneMappingのパスからTransformを解決し、ワールド座標をキャッシュする。
		/// Live Modeでのみ有効。
		/// </summary>
		public void RebuildVisualCache()
		{
			VisualMappings.Clear();
			ResolvedCount = 0;
			AutoCreatableCount = 0;
			TotalCount = 0;

			if (PreviewData == null || Detection == null)
				return;

			if (!Detection.IsLiveMode)
				return;

			if (Detection.SourceAvatarData == null || Detection.DestAvatarData == null)
				return;

			var sourceArmature = Detection.SourceAvatarData.Armature.transform;
			var destArmature = Detection.DestAvatarData.Armature.transform;

			foreach (var mapping in PreviewData.BoneMappings)
			{
				TotalCount++;
				var visual = new BoneMappingVisual
				{
					SourcePath = mapping.sourceBonePath,
					DestPath = mapping.destinationBonePath,
					Resolved = mapping.resolved,
					ErrorMessage = mapping.errorMessage
				};

				visual.SourceTransform =
					BoneMapper.FindBoneByRelativePath(mapping.sourceBonePath, sourceArmature);

				if (mapping.resolved)
				{
					visual.DestTransform =
						BoneMapper.FindBoneByRelativePath(mapping.destinationBonePath, destArmature);
					ResolvedCount++;
				}
				else if (mapping.autoCreatable && !string.IsNullOrEmpty(mapping.autoCreateDestPath))
				{
					// autoCreateDestPathの親パスからTransformを取得
					int lastSlash = mapping.autoCreateDestPath.LastIndexOf('/');
					string parentDestPath = lastSlash >= 0
						? mapping.autoCreateDestPath.Substring(0, lastSlash)
						: "";
					visual.AutoCreateParentTransform =
						BoneMapper.FindBoneByRelativePath(parentDestPath, destArmature);
					visual.AutoCreatable = true;
					AutoCreatableCount++;
				}

				VisualMappings.Add(visual);
			}

			SceneView.RepaintAll();
		}
	}

	/// <summary>
	/// 1つのボーンマッピングのSceneView描画用キャッシュ
	/// </summary>
	public class BoneMappingVisual
	{
		public string SourcePath;
		public string DestPath;
		public bool Resolved;
		public string ErrorMessage;
		public Transform SourceTransform;
		public Transform DestTransform;
		public bool AutoCreatable;
		public Transform AutoCreateParentTransform;
	}
}
