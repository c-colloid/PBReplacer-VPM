using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
    /// <summary>
    /// ルート（アバター/衣装/小物）の種別。
    /// </summary>
    public enum RootKind
    {
        None,
        VRCAvatarDescriptor,
        MACostume,
        Animator,
        Generic,
    }

    /// <summary>
    /// ルート検出結果。
    /// <see cref="Root"/> は PBRemap（または参照先ボーン）が属する「最も近い単位」（ホーム）。
    /// ホームが別の単位の中にある場合（アバター内の衣装など）、その外側の単位を <see cref="Outer"/> に持つ。
    /// </summary>
    public class RootInfo
    {
        public GameObject Root;
        public RootKind Kind = RootKind.None;
        /// <summary>UIバッジ互換用</summary>
        public AvatarDetectionMethod Method = AvatarDetectionMethod.None;
        /// <summary>ホームを包む外側の単位（例: 衣装ホームに対するアバター）。無ければ null</summary>
        public GameObject Outer;
        public RootKind OuterKind = RootKind.None;
        /// <summary>検出できなかった場合の候補（直下の子など）</summary>
        public List<GameObject> Candidates = new List<GameObject>();
        public string Reason = "";

        public bool IsFound => Root != null && Kind != RootKind.None;
        public bool HasOuter => Outer != null;

        /// <summary>表示名（外側があれば "Avatar › Costume"）</summary>
        public string DisplayName => Root == null ? "" : (Outer != null ? Outer.name + " › " + Root.name : Root.name);

        /// <summary>手動指定などから RootInfo を作る</summary>
        public static RootInfo For(GameObject root, AvatarDetectionMethod method = AvatarDetectionMethod.Manual)
        {
            if (root == null) return new RootInfo();
            var info = PBRemapContextResolver.FindRoot(root.transform, excludeSelf: false);
            if (!info.IsFound || info.Root != root)
            {
                var k = PBRemapContextResolver.ClassifyRootCandidate(root.transform);
                info = new RootInfo { Root = root, Kind = k != RootKind.None ? k : RootKind.Generic };
                var outerInfo = root.transform.parent != null ? PBRemapContextResolver.FindRoot(root.transform.parent, excludeSelf: false) : null;
                if (outerInfo != null && outerInfo.IsFound) { info.Outer = outerInfo.Root; info.OuterKind = outerInfo.Kind; }
            }
            info.Method = method;
            return info;
        }
    }

    /// <summary>コンテキストがホーム自身のものか、外側の単位のものか</summary>
    public enum ContextScope
    {
        Self,
        Outer,
    }

    /// <summary>
    /// ルート配下の1コンテキスト（Armature）の実体情報。
    /// </summary>
    public class ContextInfo
    {
        public int Id;
        public BoneContextKind Kind;
        public ContextScope Scope = ContextScope.Self;
        /// <summary>このコンテキストが属するルート（ホームまたは外側）の Transform</summary>
        public Transform RootTransform;
        public Transform Armature;
        public Animator Animator;
        public bool IsHumanoid => Animator != null && Animator.isHuman;
        public string MaPrefix = "";
        public string MaSuffix = "";
        public string MaMergeTargetPath = "";
        public GameObject CostumeRoot;
        public string CostumeName => CostumeRoot != null ? CostumeRoot.name : "";

        public override string ToString() => $"{Scope}/{Kind}#{Id}({(Armature != null ? Armature.name : "null")})";
    }

    /// <summary>
    /// 「ルート（ホーム）」と「コンテキスト（Armature）」を決定する。
    ///
    /// ルート判定（祖先を上に辿り、条件を満たす「最も近い」祖先をホームとして採用する）:
    ///   1. VRC_AvatarDescriptor を持つ
    ///   2. 直下の子に ModularAvatarMergeArmature を持つ（= MA衣装のルート）
    ///   3. Animator を持ち、自前の SkinnedMeshRenderer を子孫に持つ
    ///   4. 自前の SkinnedMeshRenderer を子孫に持つ（汎用。1〜3 が祖先に無い場合のみ、最も近いもの）
    /// ホームのさらに外側に 1〜3 の祖先があればそれを Outer（外側の単位）として記録する。
    /// 例: Avatar/Costume/AvatarDynamics → ホーム = Costume、Outer = Avatar。
    /// 「自前の」= 途中に別ルート（1〜3）を挟まずに到達できる SkinnedMeshRenderer。
    /// 空の整理用オブジェクト（例: "Props"）は、配下のメッシュが全て別ルート内にあるため採用されない。
    /// </summary>
    public static class PBRemapContextResolver
    {
        #region Root detection

        /// <summary>
        /// start を含む（または start の親から見た）ルートを検出する。
        /// </summary>
        /// <param name="start">走査開始 Transform（PBRemap 自身、または参照先ボーン）</param>
        /// <param name="excludeSelf">start 自身を候補から除外するか（PBRemap 自身の検出では true）</param>
        public static RootInfo FindRoot(Transform start, bool excludeSelf) => FindRoot(start, excludeSelf, null);

        /// <summary>
        /// <see cref="FindRoot(Transform,bool)"/> のキャッシュ付き版。同じ走査内で祖先の分類結果を共有する。
        /// </summary>
        public static RootInfo FindRoot(Transform start, bool excludeSelf, Dictionary<Transform, RootKind> classifyCache)
        {
            RootKind Classify(Transform t)
            {
                if (classifyCache == null) return ClassifyRootCandidate(t);
                if (!classifyCache.TryGetValue(t, out var k)) { k = ClassifyRootCandidate(t); classifyCache[t] = k; }
                return k;
            }
            var info = new RootInfo();
            if (start == null) { info.Reason = "start is null"; return info; }

            Transform scan = excludeSelf ? start.parent : start;
            if (scan == null) { info.Reason = "親がありません"; return info; }

            GameObject home = null;
            RootKind homeKind = RootKind.None;
            GameObject outer = null;
            RootKind outerKind = RootKind.None;
            GameObject nearestGeneric = null;

            for (Transform a = scan; a != null; a = a.parent)
            {
                var kind = Classify(a);
                bool strong = kind == RootKind.VRCAvatarDescriptor || kind == RootKind.MACostume || kind == RootKind.Animator;
                if (strong)
                {
                    if (home == null) { home = a.gameObject; homeKind = kind; }
                    else { outer = a.gameObject; outerKind = kind; break; }
                }
                else if (kind == RootKind.Generic && nearestGeneric == null && home == null)
                {
                    nearestGeneric = a.gameObject;
                }
            }

            if (home != null)
            {
                info.Root = home;
                info.Kind = homeKind;
                info.Outer = outer;
                info.OuterKind = outerKind;
            }
            else if (nearestGeneric != null)
            {
                info.Root = nearestGeneric;
                info.Kind = RootKind.Generic;
            }
            else
            {
                info.Reason = "アバター/衣装/小物として認識できる祖先がありません";
                // 候補: 走査開始点（とその祖先）の直下の子で、ルートとして成立するもの
                for (Transform a = scan; a != null; a = a.parent)
                {
                    // 直下の子と、その下2階層までを候補として探す
                    foreach (Transform child in a)
                    {
                        if (child == start) continue;
                        CollectCandidates(child, 2, info.Candidates, Classify);
                    }
                }
            }

            info.Method = ToMethod(info.Kind);
            return info;
        }

        private static void CollectCandidates(Transform t, int depth, List<GameObject> into, System.Func<Transform, RootKind> classify)
        {
            var k = classify(t);
            if (k != RootKind.None)
            {
                if (!into.Contains(t.gameObject)) into.Add(t.gameObject);
                return;
            }
            if (depth <= 0) return;
            foreach (Transform c in t) CollectCandidates(c, depth - 1, into, classify);
        }

        /// <summary>
        /// 単一の GameObject がルートとして成立するかを分類する。
        /// </summary>
        public static RootKind ClassifyRootCandidate(Transform t)
        {
            if (t == null) return RootKind.None;

            if (t.GetComponent<VRC_AvatarDescriptor>() != null)
                return RootKind.VRCAvatarDescriptor;

            if (IsCostumeRoot(t))
                return RootKind.MACostume;

            if (t.GetComponent<Animator>() != null && HasOwnSkinnedMesh(t))
                return RootKind.Animator;

            if (HasOwnSkinnedMesh(t))
                return RootKind.Generic;

            return RootKind.None;
        }

        /// <summary>
        /// 直下の子に MergeArmature を持つ（= MA衣装ルート）か。
        /// 親を持たない MergeArmature 自身（Armature単体のPrefab等）もルート扱いにする。
        /// </summary>
        public static bool IsCostumeRoot(Transform t)
        {
#if MODULAR_AVATAR
            if (t == null) return false;
            foreach (Transform child in t)
            {
                if (child.GetComponent<ModularAvatarMergeArmature>() != null)
                    return true;
            }
            if (t.parent == null && t.GetComponent<ModularAvatarMergeArmature>() != null)
                return true;
#endif
            return false;
        }

        /// <summary>
        /// t の子孫に、途中に別ルート（Descriptor/衣装ルート/Animator）を挟まずに到達できる
        /// SkinnedMeshRenderer が存在するか。
        /// </summary>
        public static bool HasOwnSkinnedMesh(Transform t)
        {
            if (t == null) return false;
            foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool blocked = false;
                for (Transform p = smr.transform.parent; p != null && p != t; p = p.parent)
                {
                    if (p.GetComponent<VRC_AvatarDescriptor>() != null || IsCostumeRoot(p) || p.GetComponent<Animator>() != null)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked) return true;
            }
            return false;
        }

        public static AvatarDetectionMethod ToMethod(RootKind kind)
        {
            switch (kind)
            {
                case RootKind.VRCAvatarDescriptor: return AvatarDetectionMethod.VRCAvatarDescriptor;
                case RootKind.MACostume: return AvatarDetectionMethod.MergeArmature;
                case RootKind.Animator: return AvatarDetectionMethod.Animator;
                case RootKind.Generic: return AvatarDetectionMethod.Root;
                default: return AvatarDetectionMethod.None;
            }
        }

        public static string KindLabel(RootKind kind)
        {
            switch (kind)
            {
                case RootKind.VRCAvatarDescriptor: return "VRCアバター";
                case RootKind.MACostume: return "MA衣装";
                case RootKind.Animator: return "Animator";
                case RootKind.Generic: return "汎用オブジェクト";
                default: return "未検出";
            }
        }

        #endregion

        #region Contexts

        /// <summary>
        /// ルート配下の全コンテキストを列挙する（ルート自身のもののみ。Scope = Self）。
        /// [0] は常にルート自身を Armature とする Generic コンテキスト（フォールバック用）。
        /// 続いて本体Armature（Humanoidなら Hips.parent、MA衣装単体ならその Armature）、各MA衣装Armature。
        /// </summary>
        public static List<ContextInfo> BuildContexts(GameObject root)
        {
            var list = new List<ContextInfo>();
            if (root == null) return list;
            var rootT = root.transform;

            list.Add(new ContextInfo { Id = 0, Kind = BoneContextKind.Generic, Armature = rootT, RootTransform = rootT });

            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var armature = hips != null && hips.parent != null ? hips.parent : null;
                if (armature != null)
                    list.Add(new ContextInfo { Id = 1, Kind = BoneContextKind.Main, Armature = armature, Animator = animator, RootTransform = rootT });
            }

#if MODULAR_AVATAR
            int nextId = 2;
            foreach (var merge in root.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
            {
                // 別のMergeArmatureの内側にネストしているものは親のコンテキストに含める（MA自身もスキップする）
                bool nested = false;
                for (Transform p = merge.transform.parent; p != null && p != rootT; p = p.parent)
                {
                    if (p.GetComponent<ModularAvatarMergeArmature>() != null) { nested = true; break; }
                }
                if (nested) continue;

                var costumeRoot = merge.transform.parent != null ? merge.transform.parent.gameObject : merge.gameObject;
                list.Add(new ContextInfo
                {
                    Id = nextId++,
                    Kind = BoneContextKind.Costume,
                    Armature = merge.transform,
                    Animator = null,
                    MaPrefix = merge.prefix ?? "",
                    MaSuffix = merge.suffix ?? "",
                    MaMergeTargetPath = merge.mergeTarget != null ? merge.mergeTarget.referencePath ?? "" : "",
                    CostumeRoot = costumeRoot,
                    RootTransform = rootT,
                });
            }
#endif

            // 非Humanoidで本体コンテキストが無い場合: 最も大きなボーン階層を Main とみなす（小物/Generic Animator）
            if (!list.Any(c => c.Kind == BoneContextKind.Main))
            {
                var mainArmature = GuessArmature(root, list);
                if (mainArmature != null && mainArmature != rootT)
                    list.Add(new ContextInfo { Id = list.Count == 1 ? 1 : list.Max(c => c.Id) + 1, Kind = BoneContextKind.Main, Armature = mainArmature, Animator = animator, RootTransform = rootT });
            }

            return list;
        }

        /// <summary>
        /// ホームのコンテキスト（Self）に、外側の単位のコンテキスト（Outer。ホーム配下のものは除く）を続けて列挙する。
        /// 例: ホーム = Avatar/Costume なら [Generic(Costume), Costume(Costume/Armature)] + [Generic(Avatar), Main(Avatar/Armature), 兄弟衣装...]。
        /// </summary>
        public static List<ContextInfo> BuildContexts(RootInfo info)
        {
            var list = new List<ContextInfo>();
            if (info == null || info.Root == null) return list;
            foreach (var c in BuildContexts(info.Root)) { c.Scope = ContextScope.Self; list.Add(c); }
            if (info.Outer != null && info.Outer != info.Root)
            {
                var homeT = info.Root.transform;
                int next = list.Count == 0 ? 0 : list.Max(c => c.Id) + 1;
                foreach (var c in BuildContexts(info.Outer))
                {
                    // ホーム配下の Armature（ホーム衣装自身の MergeArmature 等）は Self 側で扱う
                    if (c.Armature != null && c.Armature != info.Outer.transform && (c.Armature == homeT || c.Armature.IsChildOf(homeT))) continue;
                    c.Id = next++;
                    c.Scope = ContextScope.Outer;
                    list.Add(c);
                }
            }
            return list;
        }

        /// <summary>
        /// Humanoid でないルートの「本体Armature」を推定する。
        /// SkinnedMeshRenderer の rootBone / bones の共通祖先のうち、ルート直下に最も近いものを採用する。
        /// 衣装コンテキスト配下のメッシュは除外する。
        /// </summary>
        private static Transform GuessArmature(GameObject root, List<ContextInfo> existing)
        {
            var costumeArmatures = existing.Where(c => c.Kind == BoneContextKind.Costume).Select(c => c.Armature).ToList();
            Transform best = null;
            int bestCount = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones == null || smr.bones.Length == 0) continue;
                if (costumeArmatures.Any(ca => smr.transform.IsChildOf(ca) || smr.bones.Any(b => b != null && b.IsChildOf(ca)))) continue;
                var bones = smr.bones.Where(b => b != null && b.IsChildOf(root.transform)).ToList();
                if (bones.Count == 0) continue;
                var common = CommonAncestor(bones, root.transform);
                if (common == null) continue;
                // 「Armature」は通常ボーンの共通祖先の親（Hips.parent）。共通祖先がルート直下の子でなければ親を使う
                var armature = common.parent != null && common.parent != root.transform && common != root.transform ? common.parent : common;
                if (armature == root.transform) armature = common;
                int count = armature.GetComponentsInChildren<Transform>(true).Length;
                if (count > bestCount) { bestCount = count; best = armature; }
            }
            return best;
        }

        private static Transform CommonAncestor(List<Transform> bones, Transform limit)
        {
            if (bones.Count == 0) return null;
            var chain = new List<Transform>();
            for (var t = bones[0]; t != null; t = t.parent) { chain.Add(t); if (t == limit) break; }
            foreach (var b in bones)
            {
                chain.RemoveAll(c => !b.IsChildOf(c));
            }
            return chain.Count > 0 ? chain[0] : null;
        }

        /// <summary>
        /// ボーンが属するコンテキストを返す（最も深い Armature を優先。同じ深さなら Self を優先）。該当なしなら null。
        /// </summary>
        public static ContextInfo ClassifyBone(Transform bone, List<ContextInfo> contexts)
        {
            ContextInfo best = null;
            int bestDepth = -1;
            foreach (var c in contexts)
            {
                if (c.Armature == null) continue;
                if (bone == c.Armature || bone.IsChildOf(c.Armature))
                {
                    int depth = Depth(c.Armature);
                    if (depth > bestDepth || (depth == bestDepth && best != null && best.Scope == ContextScope.Outer && c.Scope == ContextScope.Self))
                    { bestDepth = depth; best = c; }
                }
            }
            return best;
        }

        public static int Depth(Transform t)
        {
            int d = 0;
            for (var p = t; p != null; p = p.parent) d++;
            return d;
        }

        /// <summary>
        /// コンテキストの実体情報をシリアライズ用データに変換する。パスはコンテキストが属するルート基準。
        /// </summary>
        public static BoneContext ToSerializable(ContextInfo c, Transform root)
        {
            var baseT = c.RootTransform != null ? c.RootTransform : root;
            return new BoneContext
            {
                id = c.Id,
                kind = c.Kind,
                scope = c.Scope == ContextScope.Outer ? BoneContextScope.Outer : BoneContextScope.Self,
                armaturePathFromRoot = BoneMapper.GetRelativePath(c.Armature, baseT) ?? "",
                armatureName = c.Armature != null ? c.Armature.name : "",
                isHumanoid = c.IsHumanoid,
                maPrefix = c.MaPrefix ?? "",
                maSuffix = c.MaSuffix ?? "",
                maMergeTargetPath = c.MaMergeTargetPath ?? "",
                costumeName = c.CostumeName,
                costumeRootPathFromRoot = c.CostumeRoot != null ? (BoneMapper.GetRelativePath(c.CostumeRoot.transform, baseT) ?? "") : "",
                armatureLossyScale = c.Armature != null ? c.Armature.lossyScale : Vector3.one,
                hipsToHead = HipsToHead(c),
            };
        }

        /// <summary>Humanoid 本体コンテキストの Hips→Head ワールド距離（非Humanoidなら0）</summary>
        public static float HipsToHead(ContextInfo c)
        {
            if (c == null || !c.IsHumanoid) return 0f;
            var hips = c.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = c.Animator.GetBoneTransform(HumanBodyBones.Head);
            return hips != null && head != null ? Vector3.Distance(hips.position, head.position) : 0f;
        }

        #endregion
    }
}
