using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBRemap UI で使う Unity 内蔵アイコンの意味付け。
    /// 文字の代わりに、Unity ユーザーが既に知っている記号（Console の警告/エラー、Linked/Unlinked、Move ツール等）で状態を伝える。
    /// </summary>
    public static class PBRemapIcons
    {
        // ---- 意味 → 内蔵アイコン名（ダークテーマは "d_" が自動で付く） ----
        public const string Avatar = "Avatar Icon";                 // VRC アバター
        public const string Costume = "Cloth Icon";                 // MA 衣装
        public const string Animator = "Animator Icon";             // Animator 付きオブジェクト
        public const string Prop = "GameObject Icon";               // 汎用小物
        public const string Self = "MoveTool";                      // PBRemap 自身（移動するもの）
        public const string Linked = "Linked";                      // 接続済み（ホーム）
        public const string Unlinked = "Unlinked";                  // 未接続（参照情報から解決 / 移植先なし）
        public const string Resolved = "Valid";                     // 解決済み ✔
        public const string Unresolved = "Invalid";                 // 未解決 ✖
        public const string AutoCreate = "Toolbar Plus";            // 自動作成 ＋
        public const string Ambiguous = "console.warnicon.sml";     // 要選択 ⚠
        public const string Error = "console.erroricon.sml";        // エラー
        public const string Info = "console.infoicon.sml";          // 情報
        public const string Warning = "console.warnicon.sml";
        public const string Apply = "forward";                      // 移植（→）
        public const string Refresh = "Refresh";                    // 参照情報を更新
        public const string Settings = "Settings";                  // 詳細設定
        public const string Eye = "scenevis_visible_hover";         // SceneView プレビュー ON
        public const string EyeOff = "scenevis_hidden_hover";       // SceneView プレビュー OFF
        public const string Scale = "ScaleTool";                    // スケール
        public const string Manual = "editicon.sml";                // 手動
        public const string Trash = "TreeEditor.Trash";             // 手動マッピングをクリア
        public const string Dropdown = "icon dropdown";             // 候補から選ぶ
        public const string Empty = "FolderEmpty Icon";             // 対象なし
        public const string Prefab = "Prefab Icon";
        public const string Undo = "back";

        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>内蔵アイコンを取得する（テーマに応じて d_ 付き/無しを試す。無ければ null）</summary>
        public static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = (EditorGUIUtility.isProSkin ? "d_" : "") + name;
            if (_cache.TryGetValue(key, out var cached)) return cached;
            Texture2D tex = Load(key) ?? Load(name) ?? Load("d_" + name);
            _cache[key] = tex;
            return tex;
        }

        private static Texture2D Load(string name)
        {
            try
            {
                var c = EditorGUIUtility.IconContent(name);
                return c != null ? c.image as Texture2D : null;
            }
            catch { return null; }
        }

        /// <summary>ルート種別に対応するアイコン名</summary>
        public static string ForKind(RootKind kind)
        {
            switch (kind)
            {
                case RootKind.VRCAvatarDescriptor: return Avatar;
                case RootKind.MACostume: return Costume;
                case RootKind.Animator: return Animator;
                case RootKind.Generic: return Prop;
                default: return Unlinked;
            }
        }

        public static string ForKind(string kindName)
        {
            if (System.Enum.TryParse<RootKind>(kindName, out var k)) return ForKind(k);
            if (kindName == "MergeArmature") return Costume;
            return Prop;
        }

        /// <summary>UI Toolkit の Image 要素を作る</summary>
        public static Image Image(string name, int size = 16, string tooltip = null)
        {
            var img = new Image { image = Get(name), scaleMode = ScaleMode.ScaleToFit };
            img.style.width = size; img.style.height = size;
            img.style.minWidth = size; img.style.minHeight = size;
            img.style.flexShrink = 0;
            if (tooltip != null) img.tooltip = tooltip;
            img.AddToClassList("pbremap-icon");
            return img;
        }

        /// <summary>既存の Image 要素のアイコンを差し替える</summary>
        public static void Set(Image img, string name, string tooltip = null)
        {
            if (img == null) return;
            img.image = Get(name);
            if (tooltip != null) img.tooltip = tooltip;
        }

        /// <summary>アイコンだけのボタン</summary>
        public static Button IconButton(string name, string tooltip, System.Action onClick, int size = 16)
        {
            var b = new Button(onClick) { tooltip = tooltip };
            b.AddToClassList("pbremap-icon-button");
            b.Add(Image(name, size));
            return b;
        }
    }
}
