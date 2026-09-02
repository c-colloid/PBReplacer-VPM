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
    /// (2) 別ルートへ置かれた（Displaced / 参照切れ+参照情報あり になった）ことを検知して ApplyMode に従い 自動適用 / 選択して案内 / ビルド時まで待つ
    /// (3) Prefab保存前・シーン保存前にマニフェストを確定する
    ///
    /// ドロップの検知は2系統:
    ///   - <see cref="ObjectChangeEvents"/>: 親の変更（自身または先祖の D&amp;D）と、階層の新規作成（Project からの Prefab ドロップ、ペースト、複製、Instantiate）
    ///   - 親の差分ポーリング（上記が届かない場合の保険）
    /// Undo/Redo・シーンを開いた直後・再生モード終了直後は「ドロップ」と見なさない。
    /// </summary>
    [InitializeOnLoad]
    public static class PBRemapTracker
    {
        private static readonly Dictionary<int, Transform> _lastParent = new Dictionary<int, Transform>();
        /// <summary>変更イベントで「置かれた」と判定した PBRemap（コンポーネントのインスタンスID）</summary>
        private static readonly HashSet<int> _dropped = new HashSet<int>();
        private static bool _dirty = true;
        private static double _lastRefresh;
        private const double RefreshInterval = 0.5;

        /// <summary>自動処理を止める（テスト・バッチ用）</summary>
        public static bool Suspended { get; set; }

        private static double _suppressDropUntil;

        // ---- 状態キャッシュ（Hierarchy バッジ用。Inspect の結果を GameObject のインスタンスIDで保持） ----
        private static readonly Dictionary<int, (PBRemapState state, bool hasManifest, bool buildOnly)> _states = new Dictionary<int, (PBRemapState, bool, bool)>();
        /// <summary>状態キャッシュが変わったとき（Hierarchy の再描画用）</summary>
        public static event System.Action StatesChanged;

        /// <summary>GameObject のインスタンスIDから PBRemap の状態を引く（キャッシュ）</summary>
        public static bool TryGetState(int gameObjectInstanceId, out PBRemapState state, out bool hasManifest)
            => TryGetState(gameObjectInstanceId, out state, out hasManifest, out _);

        /// <summary>GameObject のインスタンスIDから PBRemap の状態と「ビルド時のみ適用」かを引く（キャッシュ）</summary>
        public static bool TryGetState(int gameObjectInstanceId, out PBRemapState state, out bool hasManifest, out bool buildOnly)
        {
            if (_states.TryGetValue(gameObjectInstanceId, out var v)) { state = v.state; hasManifest = v.hasManifest; buildOnly = v.buildOnly; return true; }
            state = PBRemapState.NoReferences; hasManifest = false; buildOnly = false; return false;
        }

        /// <summary>次の更新で全 PBRemap を再評価する（適用後・参照情報更新後などに呼ぶ）</summary>
        public static void Invalidate() { _dirty = true; _lastRefresh = 0; }

        static PBRemapTracker()
        {
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.update += OnUpdate;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorSceneManager.sceneSaving += (scene, path) => FlushManifests();
            PrefabUtility.prefabInstanceUpdated += _ => FlushManifests();
            PrefabStage.prefabSaving += _ => FlushManifests();
            AssemblyReloadEvents.beforeAssemblyReload += FlushManifests;
            // シーンを開いた直後 / 再生モードから戻った直後 / ドメインリロード直後に届く生成イベントは「ドロップ」ではない
            // （追加ロードは対象外: NDMF のプレビューシーンなどがシーン作成のたびに追加ロードされるため）
            EditorSceneManager.sceneOpened += (scene, mode) => { if (mode == OpenSceneMode.Single) SuppressDrops(1.0); };
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) SuppressDrops(1.0); };
            if (!Application.isBatchMode) SuppressDrops(1.0);
            // Undo/Redo による親変更を「ドロップ」と誤認して自動適用しない（Redoスタックを壊さない）
            Undo.undoRedoPerformed += () =>
            {
                SuppressDrops(2.0);
                foreach (var def in FindAll(includeNested: true)) _lastParent[def.GetInstanceID()] = def.transform.parent;
                _dropped.Clear();
                _dirty = true;
            };
        }

        private static void SuppressDrops(double seconds)
        {
            _suppressDropUntil = System.Math.Max(_suppressDropUntil, EditorApplication.timeSinceStartup + seconds);
        }

        /// <summary>ドロップ扱いの抑制中か（Undo/Redo・シーンを開いた直後など）</summary>
        public static bool IsSuppressed => EditorApplication.timeSinceStartup < _suppressDropUntil;

        /// <summary>抑制を解除する（テスト・自動化用）</summary>
        public static void ClearSuppression() => _suppressDropUntil = 0;

        // ---- ドロップ検知（変更イベント） ----

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (Suspended || Application.isPlaying) return;
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectParent:
                    {
                        // 自身または先祖が別の親へ移された（Hierarchy の D&D、Inspector の移植先ノードへのドロップ など）
                        stream.GetChangeGameObjectParentEvent(i, out var e);
                        if (e.newParentInstanceId != e.previousParentInstanceId || e.newScene != e.previousScene)
                            MarkDropped(EditorUtility.InstanceIDToObject(e.instanceId) as GameObject);
                        break;
                    }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        // 階層が新しく現れた（Project からの Prefab ドロップ、ペースト、複製、Instantiate）
                        stream.GetCreateGameObjectHierarchyEvent(i, out var e);
                        MarkDropped(EditorUtility.InstanceIDToObject(e.instanceId) as GameObject);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// このオブジェクト配下の PBRemap を「置かれた」として次の更新で評価する。
        /// 変更イベントから呼ばれるほか、自動化やテストで Prefab の配置を模擬するときにも使う。
        /// </summary>
        public static void MarkDropped(GameObject root)
        {
            if (root == null) return;
            bool any = false;
            foreach (var def in root.GetComponentsInChildren<PBRemap>(true))
            {
                if (def == null) continue;
                _dropped.Add(def.GetInstanceID());
                any = true;
            }
            if (any) Invalidate();
        }

        // ---- 定期処理 ----

        private static void OnUpdate()
        {
            if (Suspended || Application.isPlaying || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!_dirty) return;
            if (EditorApplication.timeSinceStartup - _lastRefresh < RefreshInterval) return;
            Process();
        }

        /// <summary>今すぐ1回分の監視処理を行う（テスト・自動化用。通常は EditorApplication.update から呼ばれる）</summary>
        public static void ProcessNow() => Process();

        private static void Process()
        {
            _lastRefresh = EditorApplication.timeSinceStartup;
            _dirty = false;
            bool statesChanged = false;
            bool suppress = EditorApplication.timeSinceStartup < _suppressDropUntil;

            foreach (var def in FindAll(includeNested: true))
            {
                int id = def.GetInstanceID();
                var parent = def.transform.parent;
                bool moved = _lastParent.TryGetValue(id, out var prev) ? prev != parent : false;
                bool known = _lastParent.ContainsKey(id);
                _lastParent[id] = parent;
                // 変更イベントで拾ったもの、または親の差分（保険）。抑制中でもフラグは消費する
                bool dropped = _dropped.Remove(id) | (known && moved);

                // ネストした PBRemap は外側が管理する。親の追跡だけ行い（外へ出された時にドロップとして検知するため）、処理はしない
                if (IsNested(def)) continue;

                PBRemapper.MigrateLegacyIfNeeded(def);
                var situation = PBRemapper.Inspect(def);

                int goId = def.gameObject.GetInstanceID();
                var entry = (situation.State, situation.HasManifest, def.ApplyMode == PBRemapApplyMode.BuildOnly);
                if (!_states.TryGetValue(goId, out var prevEntry) || prevEntry != entry) { _states[goId] = entry; statesChanged = true; }

                // 移植元にいる間は参照情報を常に最新化（Undoには載せない: 派生データのため）
                if (situation.State == PBRemapState.AtHome || situation.State == PBRemapState.Displaced)
                    PBRemapper.RefreshManifestIfLive(def, situation);

                if (dropped && !suppress && IsDropTarget(situation))
                    OnDropped(def, situation, IsInPrefabStage(def));
            }

            // 消えたものを掃除
            var all = FindAll(includeNested: true).ToList();
            var alive = new HashSet<int>(all.Select(d => d.GetInstanceID()));
            foreach (var k in _lastParent.Keys.ToList()) if (!alive.Contains(k)) _lastParent.Remove(k);
            _dropped.RemoveWhere(k => !alive.Contains(k));
            var aliveGo = new HashSet<int>(all.Select(d => d.gameObject.GetInstanceID()));
            foreach (var k in _states.Keys.ToList()) if (!aliveGo.Contains(k)) { _states.Remove(k); statesChanged = true; }
            if (statesChanged) StatesChanged?.Invoke();
        }

        /// <summary>置かれた結果として処理すべき状態か: 参照が別ルートを指している、または参照が切れていて参照情報から解決できる</summary>
        private static bool IsDropTarget(PBRemapSituation s)
            => s.State == PBRemapState.Displaced || (s.State == PBRemapState.Broken && s.HasManifest);

        /// <summary>Prefab Stage（Prefabモード編集）中のオブジェクトか。共有アセットへの無確認の自動適用を避けるために使う</summary>
        private static bool IsInPrefabStage(PBRemap def)
        {
            var stage = PrefabStageUtility.GetPrefabStage(def.gameObject);
            return stage != null;
        }

        private static void OnDropped(PBRemap def, PBRemapSituation situation, bool inPrefabStage)
        {
            string name = def.gameObject.name;
            string dest = situation.DestinationDisplayName;
            // Prefab Stage 内では自動適用せず、必ず確認（プレビュー）を挟む
            var mode = inPrefabStage && def.ApplyMode == PBRemapApplyMode.AutoOnDrop ? PBRemapApplyMode.Confirm : def.ApplyMode;
            switch (mode)
            {
                case PBRemapApplyMode.AutoOnDrop:
                {
                    var plan = PBRemapper.Plan(def, situation);
                    // 迷わず決められる（候補が複数ある参照が無い）なら適用する。
                    // 移植先に対応するものが無い参照は移植元を指したまま残し、警告で知らせる（→ ボタンを押したときと同じ）
                    bool decisive = plan.CanApply && plan.AmbiguousCount == 0 && plan.ResolvedCount + plan.AutoCreateCount > 0;
                    if (decisive)
                    {
                        var r = PBRemapper.Remap(def);
                        r.Match(
                            onSuccess: s =>
                            {
                                string msg = $"[PBRemap] '{name}' を '{dest}' へ自動移植しました: {s.RemappedReferenceCount} 参照, スケール x{s.WorldScaleRatio:F3}"
                                             + (s.AutoCreatedObjectCount > 0 ? $", 自動作成 {s.AutoCreatedObjectCount}" : "");
                                if (s.UnresolvedReferenceCount > 0)
                                    Debug.LogWarning(msg + $"\n{s.UnresolvedReferenceCount} 件は移植先に対応するものが無く、そのまま残しました:\n" + string.Join("\n", s.Warnings), def);
                                else
                                    Debug.Log(msg, def);
                            },
                            onFailure: e =>
                            {
                                Debug.LogWarning($"[PBRemap] '{name}' の自動移植に失敗しました: {e}", def);
                                OpenPreview(def);
                            });
                    }
                    else
                    {
                        string why = plan.Errors.Count > 0 ? string.Join(" / ", plan.Errors)
                            : plan.AmbiguousCount > 0 ? $"{plan.AmbiguousCount} 件の参照は対応先の候補が複数あります"
                            : "解決できる参照がありません";
                        Debug.LogWarning($"[PBRemap] '{name}' → '{dest}': 自動移植を保留しました（{why}）。Inspector の表か SceneView で対応先を選び、→ で移植してください", def);
                        OpenPreview(def);
                    }
                    break;
                }
                case PBRemapApplyMode.Confirm:
                    OpenPreview(def);
                    break;
                default:
                    // BuildOnly: 編集時は触らない。NDMF ビルド（再生）時に非破壊で移植される
                    Debug.Log($"[PBRemap] '{name}' は '{dest}' へ NDMF ビルド時（再生時）に移植されます（BuildOnly）。今すぐ移植するなら Inspector の →", def);
                    break;
            }
        }

        /// <summary>
        /// 確認（Confirm）: 別ウィンドウは開かず、PBRemap を選択して Inspector の流れ（移植元 → 移植先）を見せ、
        /// SceneView に対応線を表示する。→ ボタンを押せば移植される。
        /// </summary>
        private static void OpenPreview(PBRemap def)
        {
            var det = SourceDetector.Detect(def);
            if (det.IsFailure) return;
            Selection.activeGameObject = def.gameObject;
            EditorGUIUtility.PingObject(def.gameObject);
            EditorApplication.delayCall += () =>
            {
                if (def == null || !det.Value.IsLiveMode) return;
                var previewData = PBRemapPreview.GeneratePreview(def, det.Value);
                PBRemapScenePreviewState.Instance.Activate(previewData, det.Value, def);
                SceneView.RepaintAll();
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

        private static IEnumerable<PBRemap> FindAll(bool includeNested = false)
        {
            return Resources.FindObjectsOfTypeAll<PBRemap>()
                .Where(d => d != null && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(d)
                            && (d.hideFlags & HideFlags.HideInHierarchy) == 0
                            && (includeNested || !IsNested(d)));
        }

        /// <summary>別の PBRemap の配下にある（ネストした）PBRemap か。管理は外側に委ねる</summary>
        private static bool IsNested(PBRemap d)
        {
            var parent = d.transform.parent;
            return parent != null && parent.GetComponentInParent<PBRemap>(true) != null;
        }
    }
}
