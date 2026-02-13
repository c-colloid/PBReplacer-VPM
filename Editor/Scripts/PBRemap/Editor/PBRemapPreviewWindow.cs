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
		private ScrollView _boneScrollView;
		private VisualElement _warningsContainer;

		public static PBRemapPreviewWindow Open(
			PBRemap definition,
			SourceDetector.DetectionResult detection)
		{
			var window = GetWindow<PBRemapPreviewWindow>(true, "移植プレビュー");
			window.minSize = new Vector2(520, 300);
			window._definition = definition;
			window._detection = detection;
			window.RefreshPreview();
			return window;
		}

		public void RefreshPreview()
		{
			if (_definition == null || _detection == null)
				return;
			_preview = PBRemapPreview.GeneratePreview(_definition, _detection);
			Rebuild();
		}

		public void UpdateDetection(SourceDetector.DetectionResult detection)
		{
			_detection = detection;
			RefreshPreview();
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
			_boneScrollView = root.Q<ScrollView>("preview-bone-scroll");
			_warningsContainer = root.Q<VisualElement>("preview-warnings");

			if (_preview != null)
				Rebuild();
		}

		private void Rebuild()
		{
			if (_summaryLabel == null)
				return;

			if (_preview == null)
			{
				_summaryLabel.text = "";
				_boneScrollView.Clear();
				_warningsContainer.Clear();
				return;
			}

			int totalBones = _preview.ResolvedBones + _preview.UnresolvedBones;
			_summaryLabel.text =
				$"PB: {_preview.TotalPhysBones}  PBC: {_preview.TotalPhysBoneColliders}  " +
				$"Constraint: {_preview.TotalConstraints}  Contact: {_preview.TotalContacts}\n" +
				$"ボーン解決: {_preview.ResolvedBones}/{totalBones}  |  " +
				$"スケール: {_preview.CalculatedScaleFactor:F3}";

			_boneScrollView.Clear();
			foreach (var mapping in _preview.BoneMappings)
			{
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
				else
				{
					destLabel.text = mapping.errorMessage ?? "未解決";
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
