using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
#if UITK_FONT_FIX
using Colloid.UitkFontFix;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacer の UI Toolkit 画面に日本語フォントを適用する。
	/// USS の -unity-font-definition は各テキスト要素に直接効いて継承を壊すため使わず、
	/// ルート要素へのインラインスタイルで継承させる（UITKFontFix と同じ方針）。
	///
	/// 優先順:
	///   1. UITKFontFix（jp.colloid.uitk-font-fix）が導入され、OS が日本語/中国語/韓国語のとき: OS フォント（Yu Gothic UI / Meiryo / Noto Sans CJK）
	///   2. それ以外: 同梱の Noto Sans JP（SDF）
	/// </summary>
	public static class PBReplacerFonts
	{
		private const string BundledFontResource = "Font/Noto_Sans_JP/NotoSansJP-VariableFont_wght SDF";
		private static FontAsset _bundled;

		/// <summary>ウィンドウ / Inspector のルートに 1 回だけ呼ぶ。子要素はすべて継承する</summary>
		public static void Apply(VisualElement root)
		{
			if (root == null) return;

#if UITK_FONT_FIX
			if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
			{
				FontFix.ApplyCjkUi(root);
				if (HasFont(root)) return; // OS フォントが見つかった
			}
#endif
			ApplyBundled(root);
		}

		private static bool HasFont(VisualElement root)
		{
			var def = root.style.unityFontDefinition;
			return def.keyword == StyleKeyword.Undefined && (def.value.fontAsset != null || def.value.font != null);
		}

		private static void ApplyBundled(VisualElement root)
		{
			if (_bundled == null) _bundled = Resources.Load<FontAsset>(BundledFontResource);
			if (_bundled == null) return;
			root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(_bundled));
		}
	}
}
