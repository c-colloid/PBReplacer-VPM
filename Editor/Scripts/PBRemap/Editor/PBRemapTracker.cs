using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace colloid.PBReplacer
{
    /// <summary>
    /// シーン上の PBRemap を監視し、
    /// (1) 移植元にいる間はマニフェスト（参照情報）を自動で最新に保つ（Inspectorを開かなくても持ち出せる）
    /// (2) 別ルートへドロップされた（Displaced になった）ことを検知して ApplyMode に従い自動適用/確認ウィンドウ表示を行う
    /// (3) Prefab保存前・シーン保存前にマニフェストを確定する
    /// </summary>
    [InitializeOnLoad]
    public static class PBRemapTracker
    {
        private static readonly Dictionary<int, Transform> _lastParent = new Dictionary<int, Transform>();
        private static readonly HashSet<int> _pendingPrompt = new HashSet<int>();
        private static bool _dirty = true;
        private static double _lastRefresh;
        private const double RefreshInterval = 0.5;

        /// <summary>自動処理を止める（テスト・バッチ用）</summary>
        public static bool Suspended { get; set; }

        private static double _suppressDropUntil;

        static PBRemapTracker()
        {
            EditorApplication.hierarchyChanged += () => { _dirty = true; _lastRefresh = 0; };
            EditorApplication.update += OnUpdate;
            EditorSceneManager.sceneSaving += (scene, path) => FlushManifests();
            PrefabUtility.prefabInstanceUpdated += _ => FlushManifests();
            UnityEditor.SceneManagement.PrefabStage.prefabSaving += _ => FlushManifests();
            AssemblyReloadEvents.beforeAssemblyReload += FlushManifests;
            // Undo/Redo による親変更を「ドロップ」と誤認して自動適用しない（Redoスタックを壊さない）
            Undo.undoRedoPerformed += () =>
            {
                _suppressDropUntil = EditorApplication.timeSinceStartup + 2.0;
                foreach (var def in FindAll()) _lastParent[def.GetInstanceID()] = def.transform.parent;
                _dirty = true;
            };
        }

        private static void OnUpdate()
        {
            if (Suspended || Application.isPlaying || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!_dirty) return;
            if (EditorApplication.timeSinceStartup - _lastRefresh < RefreshInterval) return;
            _lastRefresh = EditorApplication.timeSinceStartup;
            _dirty = false;

            foreach (var def in FindAll())
            {
                int id = def.GetInstanceID();
                var parent = def.transform.parent;
                bool moved = _lastParent.TryGetValue(id, out var prev) ? prev != parent : false;
                bool known = _lastParent.ContainsKey(id);
                _lastParent[id] = parent;

                PBRemapper.MigrateLegacyIfNeeded(def);
                var situation = PBRemapper.Inspect(def);

                // 移植元にいる間は参照情報を常に最新化（Undoには載せない: 派生データのため）
                if (situation.State == PBRemapState.AtHome || situation.State == PBRemapState.Displaced)
                    PBRemapper.RefreshManifestIfLive(def, situation);

                // ドロップ検知（Undo/Redo 直後は抑制）
                if (known && moved && situation.State == PBRemapState.Displaced && EditorApplication.timeSinceStartup >= _suppressDropUntil)
                    OnDropped(def, situation, IsInPrefabStage(def));
            }

            // 消えたものを掃除
            var alive = new HashSet<int>(FindAll().Select(d => d.GetInstanceID()));
            foreach (var k in _lastParent.Keys.ToList()) if (!alive.Contains(k)) _lastParent.Remove(k);
        }

        /// <summary>Prefab Stage（Prefabモード編集）中のオブジェクトか。共有アセットへの無確認の自動適用を避けるために使う</summary>
        private static bool IsInPrefabStage(PBRemap def)
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(def.gameObject);
            return stage != null;
        }

        private static void OnDropped(PBRemap def, PBRemapSituation situation, bool inPrefabStage)
        {
            // Prefab Stage 内では自動適用せず、必ず確認（プレビュー）を挟む
            var mode = inPrefabStage && def.ApplyMode == PBRemapApplyMode.AutoOnDrop ? PBRemapApplyMode.Confirm : def.ApplyMode;
            switch (mode)
            {
                case PBRemapApplyMode.AutoOnDrop:
                {
                    var plan = PBRemapper.Plan(def, situation);
                    if (plan.CanApply && plan.IsFullyResolved)
                    {
                        var r = PBRemapper.Remap(def);
                        r.Match(
                            onSuccess: s => Debug.Log($"[PBRemap] '{def.gameObject.name}' を '{situation.DestinationRoot.name}' へ自動移植しました: {s.RemappedReferenceCount} 参照, スケール x{s.WorldScaleRatio:F3}" + (s.AutoCreatedObjectCount > 0 ? $", 自動作成 {s.AutoCreatedObjectCount}" : ""), def),
                            onFailure: e => Debug.LogWarning($"[PBRemap] 自動移植に失敗: {e}", def));
                    }
                    else
                    {
                        OpenPreview(def);
                    }
                    break;
                }
                case PBRemapApplyMode.Confirm:
                    OpenPreview(def);
                    break;
                default:
                    break;
            }
        }

        private static void OpenPreview(PBRemap def)
        {
            var det = SourceDetector.Detect(def);
            if (det.IsFailure) return;
            Selection.activeGameObject = def.gameObject;
            EditorApplication.delayCall += () =>
            {
                if (def == null) return;
                PBRemapPreviewWindow.Open(def, det.Value);
            };
        }

        /// <summary>
        /// 保存前などに、Live な PBRemap のマニフェストを確定させる。
        /// </summary>
        public static void FlushManifests()
        {
            if (Application.isPlaying) return;
            foreach (var def in FindAll())
            {
                PBRemapper.MigrateLegacyIfNeeded(def);
                PBRemapper.RefreshManifestIfLive(def);
            }
        }

        private static IEnumerable<PBRemap> FindAll()
        {
            return Resources.FindObjectsOfTypeAll<PBRemap>()
                .Where(d => d != null && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(d)
                            && (d.hideFlags & HideFlags.HideInHierarchy) == 0
                            && !IsNested(d));
        }

        /// <summary>別の PBRemap の配下にある（ネストした）PBRemap か。管理は外側に委ねる</summary>
        private static bool IsNested(PBRemap d)
        {
            var parent = d.transform.parent;
            return parent != null && parent.GetComponentInParent<PBRemap>(true) != null;
        }
    }
}
