using System.Collections.Generic;
using System.Linq;
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

				if (mapping.resolved)
				{
					destLabel.text = mapping.destinationBonePath;
					destLabel.tooltip = mapping.destinationBonePath;
					row.AddToClassList("preview-bone-resolved");
				}
				else
				{
					destLabel.text = mapping.errorMessage ?? "未解決";
					row.AddToClassList("preview-bone-unresolved");

					var actions = new VisualElement();
					actions.AddToClassList("preview-bone-actions");

					string sourcePath = mapping.sourceBonePath;

					var addRuleBtn = new Button(() => AddRemapRule(sourcePath));
					addRuleBtn.text = "+";
					addRuleBtn.tooltip = "ボーン名からリマップルールを追加";
					addRuleBtn.AddToClassList("preview-bone-action-button");
					actions.Add(addRuleBtn);

					var pingBtn = new Button(() => PingNearestResolvedBone(sourcePath));
					pingBtn.text = "\u25B2";
					pingBtn.tooltip = "移植先の最寄り解決済みボーンをヒエラルキーで選択";
					pingBtn.AddToClassList("preview-bone-action-button");
					actions.Add(pingBtn);

					row.Add(actions);
				}

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
		/// ユーザーがそのボーンの子を確認してリマップルールを判断するため。
		/// </summary>
		private void PingNearestResolvedBone(string sourceBonePath)
		{
			if (_detection?.DestAvatarData == null) return;

			var destArmature = _detection.DestAvatarData.Armature.transform;
			var rules = _definition?.PathRemapRules?.ToList();
			string[] segments = sourceBonePath.Split('/');

			for (int depth = segments.Length - 1; depth >= 1; depth--)
			{
				string parentPath = string.Join("/", segments, 0, depth);
				var found = FindInDestArmature(parentPath, destArmature, rules);
				if (found != null)
				{
					EditorGUIUtility.PingObject(found.gameObject);
					Selection.activeGameObject = found.gameObject;
					return;
				}
			}

			EditorGUIUtility.PingObject(destArmature.gameObject);
			Selection.activeGameObject = destArmature.gameObject;
		}

		/// <summary>
		/// 未解決パス内の最初の解決不能セグメント名を返す。
		/// </summary>
		private string FindFirstUnresolvedSegment(string sourceBonePath)
		{
			if (_detection?.DestAvatarData == null)
			{
				string[] parts = sourceBonePath.Split('/');
				return parts[parts.Length - 1];
			}

			var destArmature = _detection.DestAvatarData.Armature.transform;
			var rules = _definition?.PathRemapRules?.ToList();
			string[] segments = sourceBonePath.Split('/');

			for (int depth = 1; depth <= segments.Length; depth++)
			{
				string partialPath = string.Join("/", segments, 0, depth);
				if (FindInDestArmature(partialPath, destArmature, rules) == null)
					return segments[depth - 1];
			}

			return segments[segments.Length - 1];
		}

		/// <summary>
		/// 移植先Armature内でパスに対応するボーンを検索する（リマップルール適用込み）。
		/// </summary>
		private static Transform FindInDestArmature(
			string relativePath, Transform destArmature, List<PathRemapRule> rules)
		{
			var found = BoneMapper.FindBoneByRelativePath(relativePath, destArmature);
			if (found != null) return found;

			if (rules == null || rules.Count == 0) return null;

			string remapped = BoneMapper.ApplyRemapRules(relativePath, rules);
			found = BoneMapper.FindBoneByRelativePath(remapped, destArmature);
			if (found != null) return found;

			string reversed = BoneMapper.ApplyRemapRulesReverse(relativePath, rules);
			if (reversed != remapped)
			{
				found = BoneMapper.FindBoneByRelativePath(reversed, destArmature);
				if (found != null) return found;
			}

			return null;
		}
	}
}
