using UnityEditor;
using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
	/// <summary>
	/// エディタのスキン（ダーク / ライト）を UI Toolkit の画面に伝える。
	/// USS の色は基本的にダーク前提の半透明オーバーレイなので、ライトテーマでは
	/// ルートに <c>pbr-theme-light</c> クラスを付け、USS 側の
	/// <c>.pbr-theme-light .pbr-xxx</c> / <c>.pbr-theme-light .pbremap-xxx</c> の上書きで
	/// 黒系オーバーレイ・濃い目のアクセント色に置き換える。C# で色は決めない。
	/// </summary>
	public static class PBReplacerTheme
	{
		public const string LightClass = "pbr-theme-light";

		/// <summary>ウィンドウ / Inspector のルートに 1 回だけ呼ぶ（PBReplacerFonts.Apply と同じ場所）</summary>
		public static void Apply(VisualElement root)
		{
			if (root == null) return;
			root.EnableInClassList(LightClass, !EditorGUIUtility.isProSkin);
		}
	}
}
