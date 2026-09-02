using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Hierarchy ウィンドウの PBRemap 行に状態アイコンを描く。
    /// Inspector を開かなくても「接続済み / 移植できる / 参照が切れている / 置き場所が違う」が一目で分かる。
    /// 状態は <see cref="PBRemapTracker"/> が保持するキャッシュを使い、ここでは計算しない。
    /// </summary>
    [InitializeOnLoad]
    public static class PBRemapHierarchyBadge
    {
        private static readonly Dictionary<PBRemapState, (string icon, string tip)> Badges = new Dictionary<PBRemapState, (string, string)>
        {
            { PBRemapState.AtHome, (PBRemapIcons.Linked, "PBRemap: 参照はこのルートに接続済み。別のアバター/衣装/小物へドラッグすると移植できます") },
            { PBRemapState.Displaced, (PBRemapIcons.Apply, "PBRemap: 移植先に置かれています。Inspector の → で移植します") },
            { PBRemapState.Broken, (PBRemapIcons.Unlinked, "PBRemap: 参照が失われています（参照情報から解決します）") },
            { PBRemapState.NoDestination, (PBRemapIcons.Error, "PBRemap: 置き場所がアバター/衣装/小物として認識できません") },
        };

        static PBRemapHierarchyBadge()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnItemGUI;
            PBRemapTracker.StatesChanged += EditorApplication.RepaintHierarchyWindow;
        }

        private static void OnItemGUI(int instanceID, Rect selectionRect)
        {
            if (!PBRemapTracker.TryGetState(instanceID, out var state, out var hasManifest)) return;
            if (!Badges.TryGetValue(state, out var badge)) return;
            string icon = badge.icon, tip = badge.tip;
            if (state == PBRemapState.Broken && !hasManifest) { icon = PBRemapIcons.Error; tip = "PBRemap: 参照が失われており、参照情報もありません（移植元のシーンで更新してください）"; }
            var tex = PBRemapIcons.Get(icon);
            if (tex == null) return;
            // 右端は Unity の Prefab 矢印が使うので、その左に置く
            var r = new Rect(selectionRect.xMax - 36, selectionRect.y + (selectionRect.height - 14) * 0.5f, 14, 14);
            var prev = GUI.color;
            GUI.color = state == PBRemapState.AtHome ? new Color(0.55f, 0.85f, 0.55f, 0.9f)
                : state == PBRemapState.Displaced ? new Color(1f, 0.8f, 0.3f, 1f)
                : state == PBRemapState.NoDestination || (state == PBRemapState.Broken && !hasManifest) ? new Color(1f, 0.45f, 0.45f, 1f)
                : new Color(1f, 0.8f, 0.3f, 0.9f);
            GUI.Label(r, new GUIContent(tex, tip));
            GUI.color = prev;
        }
    }
}
