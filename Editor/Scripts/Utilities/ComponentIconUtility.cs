using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// カテゴリのアイコンを「コンポーネント型のスクリプトアイコン」から取得する。
	/// SDK 側が固有アイコンを持たない（既定のスクリプトアイコンと同じ）場合は
	/// Unity 内蔵の近い型（HingeJoint / CapsuleCollider / ParentConstraint / SphereCollider）で引き直す。
	/// 名前文字列に依存しないので、Unity / SDK のバージョン差で欠けることがない。
	/// </summary>
	public static class ComponentIconUtility
	{
		private struct Entry
		{
			public Texture2D Texture;
			public bool IsFallback;
		}

		private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

		/// <summary>
		/// カテゴリのアイコンを取得する。isFallback は SDK アイコンが見つからず内蔵型で代替したときに true。
		/// </summary>
		public static Texture2D GetCategoryIcon(ComponentCategory category, out bool isFallback)
		{
			string key = (EditorGUIUtility.isProSkin ? "d_" : "") + category;
			if (_cache.TryGetValue(key, out var cached) && cached.Texture != null)
			{
				isFallback = cached.IsFallback;
				return cached.Texture;
			}

			Texture2D tex = null;
			bool fallback = false;

			var types = ComponentCategoryInfo.ComponentTypes(category);
			if (types.Length > 0)
			{
				tex = GetScriptIcon(types[0]);
			}

			if (tex == null || IsDefaultScriptIcon(tex))
			{
				fallback = true;
				tex = GetTypeIcon(ComponentCategoryInfo.FallbackIconType(category));
			}

			_cache[key] = new Entry { Texture = tex, IsFallback = fallback };
			isFallback = fallback;
			return tex;
		}

		/// <summary>キャッシュを破棄（テーマ切替やスクリプト再読込後に使う）</summary>
		public static void ClearCache() => _cache.Clear();

		/// <summary>
		/// MonoBehaviour 型のスクリプトアイコンを取得する。
		/// 型からは MonoScript を引けない（SDK は DLL）ため、非表示の一時オブジェクトに付けて
		/// Inspector と同じ経路（AssetPreview.GetMiniThumbnail）で解決する。
		/// </summary>
		private static Texture2D GetScriptIcon(Type componentType)
		{
			if (componentType == null) return null;

			GameObject probe = null;
			try
			{
				probe = new GameObject("PBReplacerIconProbe") { hideFlags = HideFlags.HideAndDontSave };
				var component = probe.AddComponent(componentType);
				if (component == null) return null;
				return AssetPreview.GetMiniThumbnail(component) as Texture2D;
			}
			catch
			{
				return null;
			}
			finally
			{
				if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
			}
		}

		/// <summary>Unity 内蔵コンポーネント型のアイコン</summary>
		private static Texture2D GetTypeIcon(Type type)
		{
			if (type == null) return null;
			try
			{
				var tex = AssetPreview.GetMiniTypeThumbnail(type) as Texture2D;
				if (tex != null) return tex;
				var content = EditorGUIUtility.ObjectContent(null, type);
				return content?.image as Texture2D;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>既定のスクリプトアイコン（cs / dll Script Icon）と同一か（＝固有アイコンが無い）</summary>
		private static bool IsDefaultScriptIcon(Texture2D tex)
		{
			if (tex == null) return true;
			try
			{
				var byType = AssetPreview.GetMiniTypeThumbnail(typeof(MonoBehaviour)) as Texture2D;
				if (byType != null && byType == tex) return true;
			}
			catch { }

			// C# スクリプトは "cs Script Icon"、DLL 内の型（VRC SDK など）は "dll Script Icon" が既定
			foreach (var name in new[] { "cs Script Icon", "d_cs Script Icon", "dll Script Icon", "d_dll Script Icon" })
			{
				try
				{
					var c = EditorGUIUtility.IconContent(name);
					if (c?.image != null && c.image == tex) return true;
				}
				catch { }
			}

			// 名前で判定できる場合（例: "d_cs Script Icon"）
			return tex.name != null && tex.name.EndsWith("Script Icon", StringComparison.Ordinal);
		}
	}
}
