using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>適用結果</summary>
    public class ApplyResult
    {
        public int RemappedReferences;
        public int RemappedComponents;
        public int AutoCreated;
        public int Unresolved;
        public int Ambiguous;
        public int ExternalCleared;
        public float WorldScaleRatio = 1f;
        public string ScaleMethod = "";
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 解決計画を実際のコンポーネントへ適用する。
    /// - Undoグループ1つにまとめ、例外時は全て巻き戻す
    /// - 数値は「マニフェストの元値 × 係数」で書くため冪等
    /// - 適用後はマニフェストを移植先基準で取り直し、Applied 記録を残す（再実行/ビルド時の二重適用防止）
    /// </summary>
    public static class PBRemapApplier
    {
        public static Result<ApplyResult, string> Apply(PBRemap definition, ResolutionPlan plan, bool registerUndo = true)
        {
            if (plan == null) return Result<ApplyResult, string>.Failure("解決計画がありません");
            if (!plan.CanApply) return Result<ApplyResult, string>.Failure(string.Join("\n", plan.Errors.DefaultIfEmpty("適用できる参照がありません")));

            var result = new ApplyResult { WorldScaleRatio = plan.WorldScaleRatio, ScaleMethod = plan.ScaleMethod };
            result.Warnings.AddRange(plan.Warnings);
            var definitionRoot = definition.transform;
            var manifest = plan.Manifest;

            int undoGroup = -1;
            if (registerUndo)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("PBRemap 移植");
            }

            try
            {
                // 1. 自動作成（浅い方から。親が同じくAutoCreateの場合は作成済みから引く）
                var created = new Dictionary<string, Transform>();
                foreach (var res in plan.Resolutions.Where(r => r.Status == ResolutionStatus.AutoCreate)
                             .OrderBy(r => r.Ref.relPath.Count(ch => ch == '/')))
                {
                    if (created.TryGetValue(res.SourceKey, out var already)) { res.Target = already; continue; }
                    Transform parent = res.AutoCreateParent;
                    if (parent == null)
                    {
                        // 親も自動作成対象
                        int i = res.Ref.relPath.LastIndexOf('/');
                        var parentKey = $"{res.Ref.contextId}:{(i >= 0 ? res.Ref.relPath.Substring(0, i) : "")}|";
                        parent = created.FirstOrDefault(kv => kv.Key.StartsWith(parentKey)).Value;
                    }
                    if (parent == null)
                    {
                        res.Status = ResolutionStatus.Unresolved;
                        res.Message = "自動作成の親が解決できません";
                        continue;
                    }
                    var existing = parent.Find(res.Ref.boneName);
                    if (existing != null)
                    {
                        res.Target = existing;
                    }
                    else
                    {
                        var go = new GameObject(res.Ref.boneName);
                        go.transform.SetParent(parent, false);
                        // 元ボーンのローカルTRSを引き継ぎ、位置は世界寸法比で補正
                        float posScale = plan.WorldScaleRatio;
                        go.transform.localPosition = res.Ref.localPosition * posScale;
                        go.transform.localRotation = res.Ref.localRotation;
                        go.transform.localScale = res.Ref.localScale;
                        if (registerUndo) Undo.RegisterCreatedObjectUndo(go, "PBRemap 自動作成");
                        res.Target = go.transform;
                        result.AutoCreated++;
                    }
                    created[res.SourceKey] = res.Target;
                    // 自動作成したボーンの lossyScale で係数を再計算
                    float srcLossy = PBRemapManifestBuilder.MaxComponent(res.Ref.lossyScale);
                    float dstLossy = PBRemapManifestBuilder.MaxComponent(res.Target.lossyScale);
                    if (srcLossy > 1e-6f && dstLossy > 1e-6f) res.ScaleFactor = plan.WorldScaleRatio * srcLossy / dstLossy;
                }

                // 2. 参照の書き換え
                var componentCache = new Dictionary<string, Component>();
                var touched = new HashSet<Component>();
                foreach (var res in plan.Resolutions)
                {
                    var component = FindComponent(definitionRoot, res.Ref, componentCache);
                    if (component == null)
                    {
                        result.Warnings.Add($"コンポーネント '{res.Ref.componentPath}' ({res.Ref.componentType}) が見つかりません");
                        continue;
                    }
                    switch (res.Status)
                    {
                        case ResolutionStatus.Resolved:
                        case ResolutionStatus.Manual:
                        case ResolutionStatus.AutoCreate:
                        {
                            if (res.Target == null) { result.Unresolved++; break; }
                            UnityEngine.Object value = res.Target;
                            if (!string.IsNullOrEmpty(res.Ref.targetComponentType))
                                value = res.Target.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == res.Ref.targetComponentType);
                            if (SetReference(component, res.Ref.propertyPath, value, registerUndo))
                            {
                                result.RemappedReferences++;
                                touched.Add(component);
                            }
                            break;
                        }
                        case ResolutionStatus.ExternalObject:
                        {
                            // 移植先に対応物が無い外部コンポーネント参照は解除する（ボーンTransformの外部参照は残す）
                            if (!string.IsNullOrEmpty(res.Ref.targetComponentType))
                            {
                                if (SetReference(component, res.Ref.propertyPath, null, registerUndo))
                                {
                                    result.ExternalCleared++;
                                    touched.Add(component);
                                }
                                result.Warnings.Add($"{res.Ref.componentPath}.{res.Ref.propertyPath}: {res.Message}");
                            }
                            else
                            {
                                result.Unresolved++;
                                result.Warnings.Add($"{res.Ref.componentPath}.{res.Ref.propertyPath}: {res.Message}");
                            }
                            break;
                        }
                        case ResolutionStatus.Ambiguous:
                            result.Ambiguous++;
                            result.Warnings.Add($"{res.Ref.componentPath}.{res.Ref.propertyPath}: {res.Message}");
                            break;
                        default:
                            result.Unresolved++;
                            result.Warnings.Add($"{res.Ref.componentPath}.{res.Ref.propertyPath}: {res.Message}");
                            break;
                    }
                }
                result.RemappedComponents = touched.Count;

                // 3. スケール（元値 × 係数。係数は rootTransform 参照のもの）
                foreach (var component in PBRemapManifestBuilder.CollectVRCComponents(definitionRoot))
                {
                    var path = BoneMapper.GetRelativePath(component.transform, definitionRoot) ?? "";
                    var typeName = component.GetType().Name;
                    var orig = manifest.GetOriginal(path, typeName);
                    var rootRes = plan.Resolutions.FirstOrDefault(r => r.Ref.componentPath == path && r.Ref.componentType == typeName && r.Ref.propertyPath == "rootTransform")
                                  ?? plan.Resolutions.FirstOrDefault(r => r.Ref.componentPath == path && r.Ref.componentType == typeName && r.Target != null);
                    float factor;
                    if (rootRes != null && rootRes.Target != null)
                    {
                        factor = rootRes.ScaleFactor;
                    }
                    else
                    {
                        // rootTransform 参照が無い（自身のTransformが基準）: マニフェストの元 lossyScale と現在の lossyScale の比で補正
                        Transform selfRoot = component.transform;
                        switch (component)
                        {
                            case VRCPhysBoneBase pb when pb.rootTransform != null: selfRoot = pb.rootTransform; break;
                            case VRCPhysBoneColliderBase pbc when pbc.rootTransform != null: selfRoot = pbc.rootTransform; break;
                            case ContactBase ct when ct.rootTransform != null: selfRoot = ct.rootTransform; break;
                        }
                        float srcLossy = orig != null && orig.rootLossyScaleMax > 1e-6f ? orig.rootLossyScaleMax : 1f;
                        float dstLossy = PBRemapManifestBuilder.MaxComponent(selfRoot.lossyScale);
                        if (dstLossy < 1e-6f) dstLossy = 1f;
                        factor = plan.WorldScaleRatio * srcLossy / dstLossy;
                    }
                    ApplyScale(component, orig, factor, registerUndo, result);
                }

                // 4. 適用記録とマニフェスト更新（移植先を新しいホームにする）
                var record = new AppliedRecord
                {
                    isApplied = true,
                    destinationRootName = plan.DestinationRoot.name,
                    destinationRootInstanceId = plan.DestinationRoot.GetInstanceID(),
                    appliedAtUtc = DateTime.UtcNow.ToString("o"),
                    worldScaleRatio = plan.WorldScaleRatio,
                    sourceRootName = manifest.sourceRootName,
                };
                if (registerUndo) Undo.RecordObject(definition, "PBRemap 適用記録");
                definition.SetApplied(record);
                // 移植先を新しいホームとしてマニフェストを取り直す（未解決で移植元を指したままの参照は「外」として記録される）
                var scan = PBRemapManifestBuilder.Scan(definition, plan.DestinationInfo);
                if (scan.State == PBRemapManifestBuilder.ReferenceState.Live && plan.DestinationInfo != null && plan.DestinationInfo.IsFound)
                {
                    scan.SourceRoot = plan.DestinationInfo;
                    scan.Contexts = PBRemapContextResolver.BuildContexts(plan.DestinationInfo);
                }
                var newManifest = PBRemapManifestBuilder.Build(definition, scan);
                if (newManifest != null && !newManifest.IsEmpty)
                    definition.SetManifest(newManifest);
                PBRemapper.MarkDirty(definition);
            }
            catch (Exception ex)
            {
                if (registerUndo) Undo.RevertAllDownToGroup(undoGroup);
                return Result<ApplyResult, string>.Failure($"移植中にエラーが発生しました: {ex.Message}");
            }

            if (registerUndo) Undo.CollapseUndoOperations(undoGroup);
            return Result<ApplyResult, string>.Success(result);
        }

        private static Component FindComponent(Transform definitionRoot, BoneRef r, Dictionary<string, Component> cache)
        {
            var key = r.componentPath + "|" + r.componentType;
            if (cache.TryGetValue(key, out var c)) return c;
            var t = string.IsNullOrEmpty(r.componentPath) ? definitionRoot : definitionRoot.Find(r.componentPath);
            Component found = null;
            if (t != null)
                found = t.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == r.componentType);
            cache[key] = found;
            return found;
        }

        private static bool SetReference(Component component, string propertyPath, UnityEngine.Object value, bool registerUndo)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(propertyPath);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return false;
            if (prop.objectReferenceValue == value) return true;
            if (registerUndo) Undo.RecordObject(component, "PBRemap 参照更新");
            prop.objectReferenceValue = value;
            if (registerUndo) so.ApplyModifiedProperties(); else so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static void ApplyScale(Component component, OriginalValues orig, float factor, bool registerUndo, ApplyResult result)
        {
            if (orig == null)
            {
                // 元値が無い（旧データ）: 現在値に掛ける（非冪等のため警告）
                if (Mathf.Approximately(factor, 1f)) return;
                result.Warnings.Add($"{component.name}: 元値が無いため現在値にスケールを掛けます（再実行で二重適用になる可能性）");
                if (registerUndo) Undo.RecordObject(component, "PBRemap スケール");
                switch (component)
                {
                    case VRCPhysBoneBase pb: pb.radius *= factor; pb.endpointPosition *= factor; break;
                    case VRCPhysBoneColliderBase pbc: pbc.radius *= factor; pbc.height *= factor; pbc.position *= factor; break;
                    case ContactBase ct: ct.radius *= factor; ct.height *= factor; ct.position *= factor; break;
                }
                EditorUtility.SetDirty(component);
                return;
            }
            if (registerUndo) Undo.RecordObject(component, "PBRemap スケール");
            switch (component)
            {
                case VRCPhysBoneBase pb: pb.radius = orig.radius * factor; pb.endpointPosition = orig.endpointPosition * factor; break;
                case VRCPhysBoneColliderBase pbc: pbc.radius = orig.radius * factor; pbc.height = orig.height * factor; pbc.position = orig.position * factor; break;
                case ContactBase ct: ct.radius = orig.radius * factor; ct.height = orig.height * factor; ct.position = orig.position * factor; break;
                default: return;
            }
            EditorUtility.SetDirty(component);
        }
    }
}
