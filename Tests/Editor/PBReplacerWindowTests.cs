using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using colloid.PBReplacer;

/// <summary>
/// メインウィンドウ（C案＋案i）の EditMode テスト。
/// UI の生成 → アバター設定 → 件数/レール/流れ → 再配置 → Undo を一通り通す。
/// private メンバーはリフレクションで叩く（テストのための公開 API は増やさない）。
/// </summary>
public class PBReplacerWindowTests
{
	private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

	private static object Invoke(object o, string name, params object[] args)
	{
		var m = o.GetType().GetMethod(name, F);
		Assert.IsNotNull(m, $"method {name} not found");
		return m.Invoke(o, args);
	}
	private static T Get<T>(object o, string field)
	{
		var f = o.GetType().GetField(field, F);
		Assert.IsNotNull(f, $"field {field} not found");
		return (T)f.GetValue(o);
	}

	private GameObject _avatar;
	private PBReplacerWindow _window;

	[SetUp]
	public void SetUp()
	{
		EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

		_avatar = new GameObject("TestAvatar");
		_avatar.AddComponent<Animator>();
		_avatar.AddComponent<VRCAvatarDescriptor>();
		var armature = new GameObject("Armature"); armature.transform.SetParent(_avatar.transform);
		var hips = new GameObject("Hips"); hips.transform.SetParent(armature.transform);
		var spine = new GameObject("Spine"); spine.transform.SetParent(hips.transform);
		var head = new GameObject("Head"); head.transform.SetParent(spine.transform);
		var hair = new GameObject("Hair"); hair.transform.SetParent(head.transform);
		var hair2 = new GameObject("Hair2"); hair2.transform.SetParent(head.transform);
		var skirt = new GameObject("Skirt"); skirt.transform.SetParent(hips.transform);
		var hand = new GameObject("Hand"); hand.transform.SetParent(spine.transform);
		var bag = new GameObject("Bag"); bag.transform.SetParent(hips.transform);

		// PhysBone x3, Collider x2, Contact Sender/Receiver, ParentConstraint
		hair.AddComponent<VRCPhysBone>().rootTransform = hair.transform;
		hair2.AddComponent<VRCPhysBone>().rootTransform = hair2.transform;
		skirt.AddComponent<VRCPhysBone>().rootTransform = skirt.transform;
		head.AddComponent<VRCPhysBoneCollider>().rootTransform = head.transform;
		hand.AddComponent<VRCPhysBoneCollider>().rootTransform = hand.transform;
		hand.AddComponent<VRCContactSender>();
		head.AddComponent<VRCContactReceiver>();
		bag.AddComponent<VRCParentConstraint>();

		_window = EditorWindow.GetWindow<PBReplacerWindow>();
		if (Get<object>(_window, "_root") == null)
		{
			// batchmode では CreateGUI が呼ばれないことがあるので明示的に呼ぶ
			Invoke(_window, "CreateGUI");
		}
		Assert.IsNotNull(Get<object>(_window, "_root"), "UXML root should be built");
	}

	[TearDown]
	public void TearDown()
	{
		if (_window != null) _window.Close();
		AvatarFieldHelper.ClearAvatar();
	}

	[Test]
	public void CategoryIcons_AreResolved()
	{
		foreach (var c in ComponentCategoryInfo.All)
		{
			var tex = ComponentIconUtility.GetCategoryIcon(c, out bool fallback);
			Debug.Log($"[icon] {c}: {(tex != null ? tex.name : "null")} fallback={fallback}");
			Assert.IsNotNull(tex, $"icon for {c}");
		}
	}

	[Test]
	public void Window_BuildsToolsStripRailColumns()
	{
		var root = Get<VisualElement>(_window, "_root");
		Assert.AreEqual(4, root.Q<VisualElement>("tools").childCount, "4 tool buttons");
		Assert.IsNotNull(root.Q<VisualElement>("strip"));
		Assert.AreEqual(4, root.Q<VisualElement>("rail").childCount, "4 rail chips");
		Assert.IsFalse(root.Q<VisualElement>("chip-PhysBone").ClassListContains("pbr-rail-chip--fallback"), "rail always uses the 案 i layout");
		Assert.IsNotNull(_window.rootVisualElement.style.unityFontDefinition.value.fontAsset ?? (object)_window.rootVisualElement.style.unityFontDefinition.value.font, "font applied on root");
		Assert.AreEqual(4, root.Q<VisualElement>("columns").childCount, "4 columns");
		Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("advanced").style.display.value, "advanced closed by default");

		// アバター未設定: ピルは無効
		var apply = root.Q<Button>("apply-button");
		Assert.IsFalse(apply.enabledSelf, "apply disabled without avatar");
		Assert.IsTrue(root.Q<VisualElement>("node-avatar").ClassListContains("pbr-node--empty"));
	}

	[Test]
	public void SetAvatar_CountsAndStrip()
	{
		Invoke(_window, "SetAvatar", _avatar);
		Invoke(_window, "RefreshAll");

		int pending = (int)Invoke(_window, "TotalPending");
		int total = (int)Invoke(_window, "TotalComponents");
		Debug.Log($"[counts] pending={pending} total={total}");
		Assert.AreEqual(8, total, "3 PB + 2 PBC + 2 Contact + 1 Constraint");
		Assert.AreEqual(8, pending);

		var root = Get<VisualElement>(_window, "_root");
		var apply = root.Q<Button>("apply-button");
		Assert.IsTrue(apply.enabledSelf, "apply enabled with pending");
		Assert.IsTrue(apply.ClassListContains("pbr-apply--ready"), "all categories visible → ready (green)");
		StringAssert.Contains("8", root.Q<Label>("apply-label").text);
		Assert.IsTrue(root.Q<VisualElement>("strip").ClassListContains("pbr-strip--displaced"));

		// レールのバッジ
		var rail = root.Q<VisualElement>("rail");
		var badges = rail.Query<Label>(className: "pbr-rail-badge").ToList();
		Assert.AreEqual(4, badges.Count);
		CollectionAssert.AreEqual(new[] { "3", "2", "1", "2" }, badges.Select(b => b.text).ToArray());
		Assert.IsTrue(rail.Q<VisualElement>("chip-PhysBone").ClassListContains("pbr-rail-chip--pending"));

		// 列の件数
		var pbCol = root.Q<VisualElement>("column-PhysBone");
		Assert.AreEqual("3 / 3", pbCol.Q<Label>(className: "pbr-column-count").text);

		// 行にはホバーで出る操作（Ping / 削除）がある
		var rowElement = pbCol.Q<ListView>().makeItem();
		Assert.AreEqual(2, rowElement.Q<VisualElement>(className: "pbr-row-actions").childCount, "ping + delete actions");
	}

	[Test]
	public void RailChip_TogglesColumnAndPillCount()
	{
		Invoke(_window, "SetAvatar", _avatar);
		Invoke(_window, "RefreshAll");
		var root = Get<VisualElement>(_window, "_root");

		// Contact を非表示に → Contact 列が消え、ピルの件数が 8 → 6
		Invoke(_window, "OnRailChipClicked", ComponentCategory.Contact, false);
		Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("column-Contact").style.display.value);
		var apply = root.Q<Button>("apply-button");
		StringAssert.Contains("6", root.Q<Label>("apply-label").text);
		Assert.IsTrue(apply.ClassListContains("pbr-apply--partial"), "partial → amber");

		// Alt+クリックで Constraint だけ
		Invoke(_window, "OnRailChipClicked", ComponentCategory.Constraint, true);
		StringAssert.Contains("1", root.Q<Label>("apply-label").text);
		Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("column-PhysBone").style.display.value);
		Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("column-Constraint").style.display.value);

		// 戻す
		Invoke(_window, "OnRailChipClicked", ComponentCategory.PhysBone, false);
		Invoke(_window, "OnRailChipClicked", ComponentCategory.PhysBoneCollider, false);
		Invoke(_window, "OnRailChipClicked", ComponentCategory.Contact, false);
		StringAssert.Contains("8", root.Q<Label>("apply-label").text);
	}

	[Test]
	public void Apply_MovesComponentsThenUndoRestores()
	{
		Invoke(_window, "SetAvatar", _avatar);
		Invoke(_window, "RefreshAll");

		var groups = (List<int>)Invoke(_window, "VisibleProcessGroups");
		Invoke(_window, "ExecuteApply", groups, 8);
		Invoke(_window, "RefreshAll");

		var dynamics = _avatar.transform.Find("AvatarDynamics");
		Assert.IsNotNull(dynamics, "AvatarDynamics created");
		Assert.AreEqual(3, dynamics.GetComponentsInChildren<VRCPhysBone>(true).Length);
		Assert.AreEqual(2, dynamics.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length);
		Assert.AreEqual(1, dynamics.GetComponentsInChildren<VRCParentConstraint>(true).Length);
		Assert.AreEqual(1, dynamics.GetComponentsInChildren<VRCContactSender>(true).Length);
		Assert.AreEqual(1, dynamics.GetComponentsInChildren<VRCContactReceiver>(true).Length);

		int pending = (int)Invoke(_window, "TotalPending");
		int processed = (int)Invoke(_window, "TotalProcessed");
		Debug.Log($"[after apply] pending={pending} processed={processed}");
		Assert.AreEqual(0, pending);
		Assert.AreEqual(8, processed, "processed counts only the 4 target types");

		var root = Get<VisualElement>(_window, "_root");
		Assert.IsTrue(root.Q<VisualElement>("strip").ClassListContains("pbr-strip--home"), "all done → green");
		Assert.IsTrue(root.Q<Button>("apply-button").ClassListContains("pbr-apply--hidden"));
		var badges = root.Q<VisualElement>("rail").Query<Label>(className: "pbr-rail-badge").ToList();
		Assert.IsTrue(badges.All(b => b.text == "✔"), string.Join(",", badges.Select(b => b.text)));
		Assert.IsTrue(Get<bool>(_window, "_undoAvailable"), "↶ enabled after apply");

		// 配置済み行は列末尾の見出し行にまとまる
		var pbCol = root.Q<VisualElement>("column-PhysBone");
		Assert.AreEqual("0 / 3", pbCol.Q<Label>(className: "pbr-column-count").text);

		// Undo で戻る
		Undo.PerformUndo();
		Invoke(_window, "RefreshAll");
		Assert.IsNull(_avatar.transform.Find("AvatarDynamics"), "AvatarDynamics removed by undo");
		Assert.AreEqual(3, _avatar.GetComponentsInChildren<VRCPhysBone>(true).Length);
		Assert.AreEqual(8, (int)Invoke(_window, "TotalPending"));
	}

	[Test]
	public void AdvancedPanel_TogglesAndSavesSettings()
	{
		var root = Get<VisualElement>(_window, "_root");
		Invoke(_window, "SetAdvancedVisible", true);
		Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("advanced").style.display.value);
		var toggle = root.Q<Toggle>("setting-destroy-unused");
		bool before = toggle.value;
		toggle.value = !before;
		var reloaded = PBReplacerSettings.Load();
		Assert.AreEqual(!before, reloaded.DestroyUnusedObject, "toggle saves immediately");
		toggle.value = before;
		Invoke(_window, "SetAdvancedVisible", false);
	}
}
