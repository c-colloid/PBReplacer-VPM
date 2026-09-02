using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 手動マッピング（移植元ボーン → 移植先 Transform）の書き込み。Inspector / SceneView ツールで共用。
    /// Undo に載せ、Prefab インスタンスでもオーバーライドとして記録する。
    /// </summary>
    public static class PBRemapManualMapping
    {
        public static void Set(PBRemap definition, string sourceKey, string sourcePath, Transform targetTransform, GameObject destRoot)
        {
            if (definition == null || string.IsNullOrEmpty(sourceKey)) return;
            var so = new SerializedObject(definition);
            var prop = so.FindProperty("mappingOverrides");
            int found = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).FindPropertyRelative("sourceKey").stringValue == sourceKey) { found = i; break; }
            }
            if (targetTransform == null)
            {
                if (found >= 0) prop.DeleteArrayElementAtIndex(found);
            }
            else
            {
                if (found < 0) { found = prop.arraySize; prop.arraySize = found + 1; }
                var el = prop.GetArrayElementAtIndex(found);
                el.FindPropertyRelative("sourceKey").stringValue = sourceKey;
                el.FindPropertyRelative("sourcePath").stringValue = sourcePath ?? "";
                el.FindPropertyRelative("target").objectReferenceValue = targetTransform;
                el.FindPropertyRelative("targetPathFromRoot").stringValue = destRoot != null ? (BoneMapper.GetRelativePath(targetTransform, destRoot.transform) ?? "") : "";
            }
            so.ApplyModifiedProperties();
            PBRemapper.MarkDirty(definition);
            PBRemapTracker.Invalidate();
        }

        public static void Clear(PBRemap definition)
        {
            if (definition == null) return;
            var so = new SerializedObject(definition);
            so.FindProperty("mappingOverrides").ClearArray();
            so.ApplyModifiedProperties();
            PBRemapper.MarkDirty(definition);
            PBRemapTracker.Invalidate();
        }
    }
}
