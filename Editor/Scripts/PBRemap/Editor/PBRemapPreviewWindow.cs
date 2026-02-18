using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace colloid.PBReplacer
{
	public class PBRemapPreviewWindow : EditorWindow
	{
		private PBRemap _definition;
		private SourceDetector.DetectionResult _detection;
		private PBRemapPreviewData _preview;

		private Label _summaryLabel;
		private VisualElement _filterBar;
		private ScrollView _boneScrollView;
		private VisualElement _warningsContainer;

		// フィルター状態のキャッシュ（外部変更検知用）
		private bool _cachedShowResolved;
		private bool _cachedShowAutoCreatable;
		private bool _cachedShowUnresolved;

		/// <summary>共有フィルター状態へのアクセサ</summary>
		private static PBRemapScenePreviewState FilterState => PBRemapScenePreviewState.Instance;

		public static PBRemapPreviewWindow Open(
			PBRemap definition,
			SourceDetector.DetectionResult detection)
		{
			var window = GetWindow<PBRemapPreviewWindow>(true, "移植プレビュー");
			//window.minSize = new Vector2(520, 300);
			window._definition = definition;
			window._detection = detection;
			window.RefreshPreview();

			// SceneViewプレビューを有効化
			if (detection.IsLiveMode && window._preview != null)
				PBRemapScenePreviewState.Instance.Activate(window._preview, detection);

			return window;
		}

		public void RefreshPreview()
		{
			if (_definition == null || _detection == null)
				return;
			_preview = PBRemapPreview.GeneratePreview(_definition, _detection);
			Rebuild();

			// SceneViewプレビューが有効ならキャッシュを更新
			if (PBRemapScenePreviewState.Instance.IsActive && _detection.IsLiveMode)
				PBRemapScenePreviewState.Instance.Activate(_preview, _detection);
		}

		public void UpdateDetection(SourceDetector.DetectionResult detection)
		{
			_detection = detection;
			RefreshPreview();
		}

		private void OnDestroy()
		{
			PBRemapScenePreviewState.Instance.Deactivate();
		}

		private void CreateGUI()
		{
			var root = rootVisualElement;
			root.style.paddingTop = 4;
			root.style.paddingBottom = 4;

			var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBRemapPreview");
			if (visualTree != null)
				visualTree.CloneTree(root);

			var styleSheet = Resources.Load<StyleSheet>("USS/PBRemap");
			if (styleSheet != null)
				root.styleSheets.Add(styleSheet);

			_summaryLabel = root.Q<Label>("preview-summary");
			_filterBar = root.Q<VisualElement>("preview-filter-bar");
			_boneScrollView = root.Q<ScrollView>("preview-bone-scroll");
			_warningsContainer = root.Q<VisualElement>("preview-warnings");

			// フィルターキャッシュを初期化
			_cachedShowResolved = FilterState.ShowResolved;
			_cachedShowAutoCreatable = FilterState.ShowAutoCreatable;
			_cachedShowUnresolved = FilterState.ShowUnresolved;

			if (_preview != null)
				Rebuild();

			// SceneOverlay側のフィルター変更を検知して連動
			root.schedule.Execute(CheckFilterSync).Every(200);
		}

		private void Rebuild()
		{
			if (_summaryLabel == null)
				return;

			if (_preview == null)
			{
				_summaryLabel.text = "";
				_filterBar?.Clear();
				_boneScrollView.Clear();
				_warningsContainer.Clear();
				return;
			}

			int totalBones = _preview.ResolvedBones + _preview.UnresolvedBones;
			int autoCreatable = _preview.AutoCreatableBones;
			int trueUnresolved = _preview.UnresolvedBones - autoCreatable;

			_summaryLabel.text =
				$"PB: {_preview.TotalPhysBones}  PBC: {_preview.TotalPhysBoneColliders}  " +
				$"Constraint: {_preview.TotalConstraints}  Contact: {_preview.TotalContacts}" +
				$"  |  スケール: {_preview.CalculatedScaleFactor:F3}";

			// フィルターバー構築
			RebuildFilterBar(_preview.ResolvedBones, autoCreatable, trueUnresolved);

			_boneScrollView.Clear();
			foreach (var mapping in _preview.BoneMappings)
			{
				// フィルター適用（共有状態を参照）
				if (mapping.resolved && !FilterState.ShowResolved) continue;
				if (!mapping.resolved && mapping.autoCreatable && !FilterState.ShowAutoCreatable) continue;
				if (!mapping.resolved && !mapping.autoCreatable && !FilterState.ShowUnresolved) continue;

				var row = new VisualElement();
				row.AddToClassList("preview-bone-item");

				var sourceLabel = new Label(mapping.sourceBonePath);
				sourceLabel.AddToClassList("preview-bone-sourcelabel");
				sourceLabel.tooltip = mapping.sourceBonePath;
				row.Add(sourceLabel);

				var arrow = new Label("\u2192");
				arrow.AddToClassList("preview-bone-arrow");
				row.Add(arrow);

				var destLabel = new Label();
				destLabel.AddToClassList("preview-bone-destlabel");
				row.Add(destLabel);

				// 全行にアクションエリアを追加（レイアウト統一）
				var actions = new VisualElement();
				actions.AddToClassList("preview-bone-actions");

				if (mapping.resolved)
				{
					destLabel.text = mapping.destinationBonePath;
					destLabel.tooltip = mapping.destinationBonePath;
					row.AddToClassList("preview-bone-resolved");

					// destラベルクリック → 該当ボーンをヒエラルキーでPing
					string destPath = mapping.destinationBonePath;
					destLabel.RegisterCallback<ClickEvent>(evt => PingBone(destPath));

					// 解決済み行: actionsは空スペーサー
				}
				else if (mapping.autoCreatable)
				{
					// 自動作成予定: 親ボーンは解決済み、子オブジェクトを作成予定
					destLabel.text = mapping.autoCreateDestPath + " (作成予定)";
					destLabel.tooltip = "親ボーンが解決済みのため、子オブジェクトを自動作成できます"
						+ $"\n作成先: {mapping.autoCreateDestPath}";
					row.AddToClassList("preview-bone-auto-creatable");

					// destラベルクリック → 親ボーンの解決先をPing
					string sourcePath = mapping.sourceBonePath;
					destLabel.RegisterCallback<ClickEvent>(evt => PingNearestResolvedBone(sourcePath));
				}
				else
				{
					string partialDest = ComputePartialDestPath(mapping.sourceBonePath);
					destLabel.text = partialDest;
					destLabel.tooltip = (mapping.errorMessage ?? "未解決")
						+ $"\n(元パス: {mapping.sourceBonePath}, リマップ後: {partialDest})";
					row.AddToClassList("preview-bone-unresolved");

					// destラベルクリック → 最寄り解決済み祖先ボーンをPing
					string sourcePath = mapping.sourceBonePath;
					destLabel.RegisterCallback<ClickEvent>(evt => PingNearestResolvedBone(sourcePath));

					// 未解決行: +ボタン（リマップルール追加）
					var addRuleBtn = new Button(() => AddRemapRule(sourcePath));
					addRuleBtn.text = "+";
					addRuleBtn.tooltip = "ボーン名からリマップルールを追加";
					addRuleBtn.AddToClassList("preview-bone-action-button");
					actions.Add(addRuleBtn);
				}

				row.Add(actions);
				_boneScrollView.Add(row);
			}

			_warningsContainer.Clear();
			foreach (var warning in _preview.Warnings)
			{
				var row = new VisualElement();
				row.AddToClassList("preview-warning-item");
				var label = new Label(warning);
				label.style.whiteSpace = WhiteSpace.Normal;
				row.Add(label);
				_warningsContainer.Add(row);
			}
		}

		private void RebuildFilterBar(int resolved, int autoCreatable, int unresolved)
		{
			if (_filterBar == null) return;
			_filterBar.Clear();

			_filterBar.Add(CreateFilterToggle(
				resolved, "解決済み",
				new Color(0.39f, 0.78f, 0.39f), // green
				FilterState.ShowResolved,
				v => { FilterState.ShowResolved = v; SyncFilterCache(); Rebuild(); SceneView.RepaintAll(); }));

			_filterBar.Add(CreateFilterToggle(
				autoCreatable, "作成予定",
				new Color(0.86f, 0.71f, 0.20f), // yellow
				FilterState.ShowAutoCreatable,
				v => { FilterState.ShowAutoCreatable = v; SyncFilterCache(); Rebuild(); SceneView.RepaintAll(); }));

			_filterBar.Add(CreateFilterToggle(
				unresolved, "未解決",
				new Color(0.86f, 0.31f, 0.31f), // red
				FilterState.ShowUnresolved,
				v => { FilterState.ShowUnresolved = v; SyncFilterCache(); Rebuild(); SceneView.RepaintAll(); }));
		}

		private void SyncFilterCache()
		{
			_cachedShowResolved = FilterState.ShowResolved;
			_cachedShowAutoCreatable = FilterState.ShowAutoCreatable;
			_cachedShowUnresolved = FilterState.ShowUnresolved;
		}

		/// <summary>
		/// SceneOverlay側でフィルターが変更された場合にPreviewWindowを再構築する。
		/// </summary>
		private void CheckFilterSync()
		{
			if (_cachedShowResolved != FilterState.ShowResolved
				|| _cachedShowAutoCreatable != FilterState.ShowAutoCreatable
				|| _cachedShowUnresolved != FilterState.ShowUnresolved)
			{
				SyncFilterCache();
				Rebuild();
			}
		}

		private static VisualElement CreateFilterToggle(
			int count, string label, Color color, bool active, System.Action<bool> onToggle)
		{
			var toggle = new VisualElement();
			toggle.AddToClassList("preview-filter-toggle");
			toggle.AddToClassList(active ? "preview-filter-toggle-active" : "preview-filter-toggle-inactive");
			toggle.style.borderTopColor = active ? (StyleColor)color : new StyleColor(new Color(1, 1, 1, 0.06f));
			toggle.style.borderBottomColor = toggle.style.borderTopColor;
			toggle.style.borderLeftColor = toggle.style.borderTopColor;
			toggle.style.borderRightColor = toggle.style.borderTopColor;

			var dot = new VisualElement();
			dot.AddToClassList("preview-filter-dot");
			dot.style.backgroundColor = active ? (StyleColor)color : new StyleColor(new Color(color.r, color.g, color.b, 0.3f));
			toggle.Add(dot);

			var text = new Label($"{count} {label}");
			text.AddToClassList("preview-filter-label");
			text.style.color = active ? (StyleColor)color : new StyleColor(new Color(0.6f, 0.6f, 0.6f));
			toggle.Add(text);

			toggle.RegisterCallback<ClickEvent>(evt => onToggle(!active));

			return toggle;
		}

		/// <summary>
		/// 解決済みボーンをヒエラルキーでPing＆選択する。
		/// </summary>
		private void PingBone(string destRelativePath)
		{
			var destArmature = _detection?.DestAvatarData?.Armature?.transform;
			if (destArmature == null) return;
			var bone = BoneMapper.FindBoneByRelativePath(destRelativePath, destArmature);
			if (bone != null)
			{
				EditorGUIUtility.PingObject(bone.gameObject);
				Selection.activeGameObject = bone.gameObject;
			}
		}

		/// <summary>
		/// 未解決ボーンの名前からリマップルールを追加する。
		/// 最初に解決不能になるセグメントをsourcePatternに設定し、
		/// destinationPatternは空欄（ユーザーがインスペクタで入力）。
		/// </summary>
		private void AddRemapRule(string sourceBonePath)
		{
			if (_definition == null) return;

			string segment = FindFirstUnresolvedSegment(sourceBonePath);

			var so = new SerializedObject(_definition);
			var rulesProp = so.FindProperty("pathRemapRules");

			int newIndex = rulesProp.arraySize;
			rulesProp.InsertArrayElementAtIndex(newIndex);
			var newRule = rulesProp.GetArrayElementAtIndex(newIndex);
			newRule.FindPropertyRelative("enabled").boolValue = true;
			newRule.FindPropertyRelative("mode").enumValueIndex =
				(int)PathRemapRule.RemapMode.CharacterSubstitution;
			newRule.FindPropertyRelative("sourcePattern").stringValue = segment;
			newRule.FindPropertyRelative("destinationPattern").stringValue = "";

			so.ApplyModifiedProperties();
			RefreshPreview();
		}

		/// <summary>
		/// 移植先アバターで最も近い解決済み祖先ボーンをヒエラルキーで選択する。
		/// プレビューデータの解決済みマッピングから祖先を推定する。
		/// </summary>
		private void PingNearestResolvedBone(string sourceBonePath)
		{
			if (_detection?.DestAvatarData == null) return;

			var destArmature = _detection.DestAvatarData.Armature.transform;
			string[] segments = sourceBonePath.Split('/');

			// プレビューデータから最寄りの解決済み祖先を探す
			for (int depth = segments.Length - 1; depth >= 1; depth--)
			{
				string parentPrefix = string.Join("/", segments, 0, depth);
				var destBone = FindDestBoneForSourcePrefix(parentPrefix);
				if (destBone != null)
				{
					EditorGUIUtility.PingObject(destBone.gameObject);
					Selection.activeGameObject = destBone.gameObject;
					return;
				}
			}

			EditorGUIUtility.PingObject(destArmature.gameObject);
			Selection.activeGameObject = destArmature.gameObject;
		}

		/// <summary>
		/// 未解決パス内の最初の解決不能セグメント名を返す。
		/// プレビューデータの解決済みマッピングからプレフィックスの解決状況を判定する。
		/// </summary>
		private string FindFirstUnresolvedSegment(string sourceBonePath)
		{
			string[] segments = sourceBonePath.Split('/');

			if (_preview != null)
			{
				for (int depth = 1; depth <= segments.Length; depth++)
				{
					string partialPath = string.Join("/", segments, 0, depth);
					if (!IsSourcePrefixResolved(partialPath))
						return segments[depth - 1];
				}
				return segments[segments.Length - 1];
			}

			return segments[segments.Length - 1];
		}

		/// <summary>
		/// 未解決ボーンのソースパスから、解決済み祖先を反映した部分解決デストパスを生成する。
		/// 例: "Hips/Tail" で "Hips"→"J_Hips" が解決済みなら "J_Hips/Tail" を返す。
		/// </summary>
		private string ComputePartialDestPath(string sourceBonePath)
		{
			if (_preview == null || _detection?.DestAvatarData == null)
				return sourceBonePath;

			var destArmature = _detection.DestAvatarData.Armature.transform;
			string[] segments = sourceBonePath.Split('/');

			// 最も深い解決済み祖先プレフィックスを探す
			for (int depth = segments.Length - 1; depth >= 1; depth--)
			{
				string sourcePrefix = string.Join("/", segments, 0, depth);
				var destBone = FindDestBoneForSourcePrefix(sourcePrefix);
				if (destBone != null)
				{
					string destPrefixPath = BoneMapper.GetRelativePath(destBone, destArmature)
						?? destBone.name;
					string remaining = string.Join("/", segments, depth, segments.Length - depth);
					return destPrefixPath + "/" + remaining;
				}
			}

			return sourceBonePath;
		}

		/// <summary>
		/// プレビューデータから、ソースパスの指定プレフィックスが解決済みかを判定する。
		/// プレフィックスと一致する、またはプレフィックスで始まる解決済みマッピングが
		/// 存在すれば、そのプレフィックス自体も解決可能と判断する。
		/// </summary>
		private bool IsSourcePrefixResolved(string sourcePrefix)
		{
			if (_preview == null) return false;

			foreach (var m in _preview.BoneMappings)
			{
				if (!m.resolved) continue;
				if (m.sourceBonePath == sourcePrefix) return true;
				if (m.sourceBonePath.StartsWith(sourcePrefix + "/")) return true;
			}
			return false;
		}

		/// <summary>
		/// プレビューデータから、ソースパスのプレフィックスに対応する
		/// デスティネーション側のTransformを取得する。
		/// 例: sourcePrefix="Hips" で解決済み "Hips/Spine"→"J_Hips/Spine" がある場合、
		/// destの深さ1のプレフィックス "J_Hips" を返す。
		/// </summary>
		private Transform FindDestBoneForSourcePrefix(string sourcePrefix)
		{
			if (_preview == null || _detection?.DestAvatarData == null) return null;

			var destArmature = _detection.DestAvatarData.Armature.transform;
			int prefixDepth = sourcePrefix.Split('/').Length;

			foreach (var m in _preview.BoneMappings)
			{
				if (!m.resolved) continue;

				// ソースパスが完全一致 → destパスをそのまま使用
				if (m.sourceBonePath == sourcePrefix)
					return BoneMapper.FindBoneByRelativePath(m.destinationBonePath, destArmature);

				// ソースパスがプレフィックスで始まる → destの同深度プレフィックスを抽出
				if (m.sourceBonePath.StartsWith(sourcePrefix + "/"))
				{
					string[] destSegments = m.destinationBonePath.Split('/');
					if (destSegments.Length >= prefixDepth)
					{
						string destPrefix = string.Join("/", destSegments, 0, prefixDepth);
						return BoneMapper.FindBoneByRelativePath(destPrefix, destArmature);
					}
				}
			}

			return null;
		}

	}
}
