using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 再生モードに入ったとき、NDMF が処理しない置き場所にある「置かれたまま」の PBRemap を非破壊で移植する。
    ///
    /// NDMF の apply-on-play は VRC アバター（VRCAvatarDescriptor 付き）だけを処理する。
    /// 小物・単体の衣装・Animator だけのオブジェクトへ置いた PBRemap（BuildOnly など）は NDMF の対象外なので、
    /// ここで再生開始時に適用し、PhysBone 等のランタイム状態を作り直す。再生モードでの変更は再生を止めると元に戻る。
    /// VRC アバター配下（NDMF 導入時）は <c>PBRemapNDMFPass</c> が処理するため触らない。NDMF 未導入なら全てをここで扱う。
    /// </summary>
    [InitializeOnLoad]
    public static class PBRemapPlayModeApplier
    {
        static PBRemapPlayModeApplier()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            try { ApplyAll(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>この PBRemap の置き場所は NDMF（ビルド時/再生時）が処理するか。VRC アバター配下かつ NDMF 導入時のみ true</summary>
        public static bool IsHandledByNdmf(PBRemap definition)
        {
#if NDMF
            return definition != null && definition.GetComponentInParent<VRC_AvatarDescriptor>(true) != null;
#else
            return false;
#endif
        }

        /// <summary>
        /// NDMF の対象外にある、置かれたままの PBRemap を全て移植する。
        /// 再生モード中に呼ばれる想定（参照の付け替え後にランタイムを再初期化する）。再生中でなければ参照の付け替えだけを行う（テスト用）。
        /// </summary>
        /// <returns>移植した PBRemap の数</returns>
        public static int ApplyAll()
        {
            int applied = 0;
            var targets = Resources.FindObjectsOfTypeAll<PBRemap>()
                .Where(d => d != null && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(d)
                            && (d.hideFlags & HideFlags.HideInHierarchy) == 0)
                .ToList();
            foreach (var def in targets)
            {
                if (def == null) continue;
                if (IsHandledByNdmf(def)) continue;
                // ネストした PBRemap は外側が扱う
                if (def.transform.parent != null && def.transform.parent.GetComponentInParent<PBRemap>(true) != null) continue;
                if (PrefabStageUtility.GetPrefabStage(def.gameObject) != null) continue;
                if (ApplyOne(def)) applied++;
            }
            return applied;
        }

        private static bool ApplyOne(PBRemap def)
        {
            string name = def.gameObject.name;
            PBRemapper.MigrateLegacyIfNeeded(def);
            var situation = PBRemapper.Inspect(def);
            bool pending = situation.State == PBRemapState.Displaced || (situation.State == PBRemapState.Broken && situation.HasManifest);
            if (!pending) return false;

            var plan = PBRemapper.Plan(def, situation);
            if (!plan.CanApply)
            {
                Debug.LogWarning($"[PBRemap] '{name}': 再生時の移植ができませんでした: {string.Join(" / ", plan.Errors)}", def);
                return false;
            }
            var result = PBRemapApplier.Apply(def, plan, registerUndo: false);
            if (result.IsFailure)
            {
                Debug.LogWarning($"[PBRemap] '{name}': 再生時の移植に失敗しました: {result.Error}", def);
                return false;
            }
            var r = result.Value;
            if (Application.isPlaying) Reinitialize(def.gameObject);

            string msg = $"[PBRemap] '{name}' を '{situation.DestinationDisplayName}' へ再生時に移植しました（VRC アバター配下ではないため NDMF ではなく PBRemap が適用。再生を止めると元に戻ります）: "
                         + $"{r.RemappedReferences} 参照, スケール x{r.WorldScaleRatio:F3}" + (r.AutoCreated > 0 ? $", 自動作成 {r.AutoCreated}" : "");
            if (r.Unresolved + r.Ambiguous > 0)
                Debug.LogWarning(msg + $"\n{r.Unresolved + r.Ambiguous} 件は解決できず移植元を指したままです:\n" + string.Join("\n", r.Warnings), def);
            else
                Debug.Log(msg, def);

            // NDMF と同じくランタイムでは PBRemap を残さない（再生モードの変更なので終了時に戻る）
            if (Application.isPlaying) UnityEngine.Object.Destroy(def);
            return true;
        }

        /// <summary>
        /// 参照を付け替えた後、VRC コンポーネントのランタイム状態を作り直す。
        /// PhysBone / Collider / Contact は最初の初期化で参照を焼き込み、以後は変更を見ない（NDMF の ForceReinit フックと同じ対処）。
        /// </summary>
        public static void Reinitialize(GameObject root)
        {
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                if (pb == null) continue;
                bool wasOn = pb.enabled && pb.gameObject.activeInHierarchy;
                if (wasOn) pb.enabled = false; // マネージャーから外し、新しいチェーンで登録し直す
                pb.InitTransforms(true);
                pb.InitParameters();
                if (wasOn) pb.enabled = true;
            }
            foreach (var c in root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true))
                if (c != null) c.UpdateShape();
            foreach (var c in root.GetComponentsInChildren<ContactBase>(true))
                if (c != null) c.UpdateShape();

            // VRC Constraint は TargetTransform を Awake で一度だけキャッシュする。NDMF と同じ方法で Awake をやり直す
            var awake = typeof(VRCConstraintBase).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            var assigned = typeof(VRCConstraintBase).GetField("_isRuntimeTargetTransformAssigned", BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake != null && assigned != null)
            {
                foreach (var c in root.GetComponentsInChildren<VRCConstraintBase>(true))
                {
                    if (c == null) continue;
                    assigned.SetValue(c, false);
                    awake.Invoke(c, null);
                }
            }
        }
    }
}
