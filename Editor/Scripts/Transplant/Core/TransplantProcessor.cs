using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 移植結果を格納するデータクラス
    /// </summary>
    public class TransplantResult
    {
        public int PhysBoneCount { get; set; }
        public int PhysBoneColliderCount { get; set; }
        public int ConstraintCount { get; set; }
        public int ContactCount { get; set; }
        public int TotalCount => PhysBoneCount + PhysBoneColliderCount + ConstraintCount + ContactCount;
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 移植先のAvatarDynamics階層構造を作成するプロセッサ
    /// ソース側の階層構造をデスティネーション側に再現する
    /// </summary>
    public static class TransplantProcessor
    {
        /// <summary>
        /// デスティネーションアバター直下にAvatarDynamicsルートオブジェクトを確保する
        /// 既存があればそれを返し、なければ新規作成する（冪等）
        /// </summary>
        /// <param name="destAvatar">デスティネーションアバターのGameObject</param>
        /// <returns>AvatarDynamicsルートのGameObject</returns>
        public static GameObject EnsureAvatarDynamicsRoot(GameObject destAvatar)
        {
            var settings = PBReplacerSettings.Load();
            string rootName = settings.RootObjectName;

            // 既存のルートオブジェクトを検索
            var existing = destAvatar.transform.Find(rootName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            // 新規作成
            var rootObject = new GameObject(rootName);
            rootObject.transform.SetParent(destAvatar.transform);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;

            Undo.RegisterCreatedObjectUndo(rootObject, $"Create {rootName}");

            return rootObject;
        }

        /// <summary>
        /// ルート以下にフォルダパスを再帰的に作成または取得する（冪等）
        /// </summary>
        /// <param name="root">起点となるTransform（通常はAvatarDynamicsルート）</param>
        /// <param name="folderPath">作成するフォルダパス（例: "PhysBones", "Constraints/Position"）</param>
        /// <returns>最深階層のTransform</returns>
        public static Transform EnsureFolderPath(Transform root, string folderPath)
        {
            string[] folders = folderPath.Split('/');
            Transform current = root;

            foreach (string folderName in folders)
            {
                if (string.IsNullOrEmpty(folderName))
                    continue;

                var child = current.Find(folderName);
                if (child != null)
                {
                    current = child;
                    continue;
                }

                // 新規作成
                var folderObj = new GameObject(folderName);
                folderObj.transform.SetParent(current);
                folderObj.transform.localPosition = Vector3.zero;
                folderObj.transform.localRotation = Quaternion.identity;
                folderObj.transform.localScale = Vector3.one;

                Undo.RegisterCreatedObjectUndo(folderObj, $"Create {folderName}");

                current = folderObj.transform;
            }

            return current;
        }

        /// <summary>
        /// 親ボーンの子に補助オブジェクトを作成または取得する（冪等）
        /// コライダー用等のボーン直下の補助オブジェクトに使用
        /// </summary>
        /// <param name="parentBone">親ボーンのTransform</param>
        /// <param name="objectName">作成する補助オブジェクト名</param>
        /// <returns>補助オブジェクトのTransform</returns>
        public static Transform EnsureIntermediateObject(Transform parentBone, string objectName)
        {
            // 既存チェック
            var existing = parentBone.Find(objectName);
            if (existing != null)
            {
                return existing;
            }

            // 新規作成
            var intermediateObj = new GameObject(objectName);
            intermediateObj.transform.SetParent(parentBone);
            intermediateObj.transform.localPosition = Vector3.zero;
            intermediateObj.transform.localRotation = Quaternion.identity;
            intermediateObj.transform.localScale = Vector3.one;

            Undo.RegisterCreatedObjectUndo(intermediateObj, $"Create {objectName}");

            return intermediateObj.transform;
        }

        #region コンポーネント移植

        /// <summary>
        /// TransplantDefinitionに基づきソースアバターのAvatarDynamicsコンポーネントを
        /// デスティネーションアバターにコピーし、ボーン参照をリマッピングする。
        /// </summary>
        /// <param name="definition">移植定義</param>
        /// <returns>移植結果またはエラーメッセージ</returns>
        public static Result<TransplantResult, string> TransplantComponents(
            TransplantDefinition definition)
        {
            // バリデーション
            if (definition == null)
                return Result<TransplantResult, string>.Failure("TransplantDefinitionがnullです");
            if (definition.SourceAvatar == null)
                return Result<TransplantResult, string>.Failure("ソースアバターが設定されていません");
            if (definition.DestinationAvatar == null)
                return Result<TransplantResult, string>.Failure("デスティネーションアバターが設定されていません");

            // AvatarDataでArmature取得
            AvatarData sourceData;
            AvatarData destData;
            try
            {
                sourceData = new AvatarData(definition.SourceAvatar);
            }
            catch (Exception ex)
            {
                return Result<TransplantResult, string>.Failure(
                    $"ソースアバターの解析に失敗しました: {ex.Message}");
            }

            try
            {
                destData = new AvatarData(definition.DestinationAvatar);
            }
            catch (Exception ex)
            {
                return Result<TransplantResult, string>.Failure(
                    $"デスティネーションアバターの解析に失敗しました: {ex.Message}");
            }

            // スケールファクター算出
            float scaleFactor;
            if (definition.AutoCalculateScale)
            {
                scaleFactor = ScaleCalculator.CalculateScaleFactor(
                    sourceData.Armature.transform,
                    destData.Armature.transform,
                    sourceData.AvatarAnimator,
                    destData.AvatarAnimator);
            }
            else
            {
                scaleFactor = definition.ScaleFactor;
            }

            // ボーンマップ構築
            var boneMap = BuildFullBoneMap(
                sourceData, destData,
                definition.PathRemapRules?.ToList());

            // ソースのAvatarDynamics配下を走査してコンポーネント収集
            var settings = PBReplacerSettings.Load();
            Transform sourceRoot = definition.SourceAvatar.transform.Find(settings.RootObjectName);
            if (sourceRoot == null)
                return Result<TransplantResult, string>.Failure(
                    $"ソースアバターに '{settings.RootObjectName}' が見つかりません");

            var sourcePBC = sourceRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            var sourcePB = sourceRoot.GetComponentsInChildren<VRCPhysBone>(true);
            var sourceConstraints = sourceRoot.GetComponentsInChildren<VRCConstraintBase>(true);
            var sourceContacts = sourceRoot.GetComponentsInChildren<ContactBase>(true);

            var result = new TransplantResult();

            // Undoグループ開始
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Transplant AvatarDynamics");

            try
            {
                // デスティネーションにルート確保
                var destRootObj = EnsureAvatarDynamicsRoot(definition.DestinationAvatar);
                Transform destRoot = destRootObj.transform;

                // 1. PBCコピー → old→new Colliderマップ構築
                var colliderMap = new Dictionary<int, VRCPhysBoneCollider>();
                foreach (var pbc in sourcePBC)
                {
                    var targetObj = CreateTargetObject(pbc, destRoot, settings);
                    var newPBC = CopyAndRemapComponent(
                        pbc, targetObj, boneMap, scaleFactor, result.Warnings);
                    if (newPBC != null)
                    {
                        colliderMap[pbc.GetInstanceID()] = newPBC;
                        result.PhysBoneColliderCount++;
                    }
                }

                // 2. PBコピー → collidersをColliderマップ解決
                foreach (var pb in sourcePB)
                {
                    var targetObj = CreateTargetObject(pb, destRoot, settings);
                    var newPB = CopyAndRemapComponent(
                        pb, targetObj, boneMap, scaleFactor, result.Warnings);
                    if (newPB != null)
                    {
                        RemapColliderReferences(newPB, colliderMap);
                        result.PhysBoneCount++;
                    }
                }

                // 3. Constraintコピー → Sources解決
                foreach (var constraint in sourceConstraints)
                {
                    var targetObj = CreateTargetObject(constraint, destRoot, settings);
                    var newConstraint = CopyAndRemapComponent(
                        constraint, targetObj, boneMap, scaleFactor, result.Warnings);
                    if (newConstraint != null)
                    {
                        RemapConstraintSources(newConstraint as VRCConstraintBase, boneMap);
                        result.ConstraintCount++;
                    }
                }

                // 4. Contactコピー → rootTransform解決
                foreach (var contact in sourceContacts)
                {
                    var targetObj = CreateTargetObject(contact, destRoot, settings);
                    var newContact = CopyAndRemapComponent(
                        contact, targetObj, boneMap, scaleFactor, result.Warnings);
                    if (newContact != null)
                    {
                        result.ContactCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return Result<TransplantResult, string>.Failure(
                    $"移植処理中にエラーが発生しました: {ex.Message}");
            }

            // Undoグループ完了
            Undo.CollapseUndoOperations(undoGroup);

            return Result<TransplantResult, string>.Success(result);
        }

        /// <summary>
        /// ソースコンポーネントの所属フォルダに基づいてターゲットGameObjectを作成する
        /// </summary>
        private static GameObject CreateTargetObject(
            Component source, Transform destRoot, PBReplacerSettings settings)
        {
            string folderPath = GetFolderPathForComponent(source, settings);
            Transform folder = EnsureFolderPath(destRoot, folderPath);

            string objName = source.gameObject.name;
            var targetObj = new GameObject(objName);
            targetObj.transform.SetParent(folder);
            targetObj.transform.localPosition = Vector3.zero;
            targetObj.transform.localRotation = Quaternion.identity;
            targetObj.transform.localScale = Vector3.one;

            Undo.RegisterCreatedObjectUndo(targetObj, $"Create {objName}");

            return targetObj;
        }

        /// <summary>
        /// コンポーネント型に応じた配置先フォルダパスを返す
        /// </summary>
        private static string GetFolderPathForComponent(Component component, PBReplacerSettings settings)
        {
            switch (component)
            {
                case VRCPhysBoneCollider _:
                    return settings.PhysBoneCollidersFolder;
                case VRCPhysBone _:
                    return settings.PhysBonesFolder;
                case VRCConstraintBase _:
                    return settings.ConstraintsFolder;
                case ContactBase _:
                    return settings.ContactsFolder;
                default:
                    return "";
            }
        }

        /// <summary>
        /// ソース・デスティネーション間の完全なボーンマップを構築する。
        /// Humanoidマッピング + Armature以下の全ボーンをパス/名前マッチで解決する。
        /// </summary>
        private static Dictionary<Transform, Transform> BuildFullBoneMap(
            AvatarData sourceData, AvatarData destData,
            List<PathRemapRule> remapRules)
        {
            var boneMap = new Dictionary<Transform, Transform>();

            Transform sourceArmature = sourceData.Armature.transform;
            Transform destArmature = destData.Armature.transform;
            Animator sourceAnimator = sourceData.AvatarAnimator;
            Animator destAnimator = destData.AvatarAnimator;

            // Humanoidボーンマップを先に構築
            if (sourceAnimator != null && destAnimator != null
                && sourceAnimator.isHuman && destAnimator.isHuman)
            {
                var humanoidMap = BoneMapper.BuildHumanoidBoneMap(sourceAnimator, destAnimator);
                foreach (var kvp in humanoidMap)
                    boneMap[kvp.Key] = kvp.Value;
            }

            // ソースArmature配下の全Transformをリマップ付きで解決
            var allSourceBones = sourceArmature.GetComponentsInChildren<Transform>(true);
            foreach (var srcBone in allSourceBones)
            {
                if (boneMap.ContainsKey(srcBone))
                    continue;

                var resolveResult = (remapRules != null && remapRules.Count > 0)
                    ? BoneMapper.ResolveBoneWithRemap(
                        srcBone, sourceArmature, destArmature,
                        remapRules, sourceAnimator, destAnimator)
                    : BoneMapper.ResolveBone(
                        srcBone, sourceArmature, destArmature,
                        sourceAnimator, destAnimator);

                if (resolveResult.IsSuccess)
                    boneMap[srcBone] = resolveResult.Value;
            }

            return boneMap;
        }

        #endregion

        #region コンポーネントコピーとリマッピング

        /// <summary>
        /// ソースコンポーネントをターゲットオブジェクトにコピーし、
        /// Transform参照をリマッピング、スケールを適用する。
        /// </summary>
        /// <typeparam name="T">コンポーネントの型</typeparam>
        /// <param name="source">コピー元のコンポーネント</param>
        /// <param name="targetObject">コピー先のGameObject</param>
        /// <param name="boneMap">ボーンマッピング辞書</param>
        /// <param name="scaleFactor">スケールファクター</param>
        /// <param name="warnings">警告リスト</param>
        /// <returns>コピーされたコンポーネント、失敗時はnull</returns>
        private static T CopyAndRemapComponent<T>(
            T source, GameObject targetObject,
            Dictionary<Transform, Transform> boneMap,
            float scaleFactor,
            List<string> warnings) where T : Component
        {
            try
            {
                // コンポーネント追加
                var newComponent = Undo.AddComponent(targetObject, source.GetType()) as T;
                if (newComponent == null)
                {
                    warnings.Add($"コンポーネント '{source.GetType().Name}' の追加に失敗しました ({source.gameObject.name})");
                    return null;
                }

                // プロパティコピー（ComponentProcessorと同等の処理）
                CopyFieldsAndProperties(source, newComponent);
                CopySerializedProperties(source, newComponent);

                // Transform参照リマッピング
                RemapTransformReferences(newComponent, boneMap);

                // rootTransformがnullの場合は明示的にボーン設定
                ResolveNullRootTransform(source, newComponent, boneMap);

                // スケール適用
                ApplyScaleFactor(newComponent, scaleFactor);

                return newComponent;
            }
            catch (Exception ex)
            {
                warnings.Add($"コンポーネント '{source.GetType().Name}' のコピー中にエラー: {ex.Message} ({source.gameObject.name})");
                return null;
            }
        }

        /// <summary>
        /// パブリックフィールドとプロパティをリフレクションでコピーする。
        /// リスト型はSerializedPropertiesでコピーされるためスキップ。
        /// </summary>
        private static void CopyFieldsAndProperties(Component source, Component destination)
        {
            Type type = source.GetType();

            // パブリックフィールドをコピー（リスト/配列型はスキップ）
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                try
                {
                    if (IsListOrArrayType(field.FieldType)) continue;
                    object value = field.GetValue(source);
                    field.SetValue(destination, value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"フィールド '{field.Name}' のコピー中にエラーが発生しました: {ex.Message}");
                }
            }

            // パブリックプロパティで書き込み可能なものをコピー（リスト/配列型はスキップ）
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (property.CanWrite && property.CanRead)
                {
                    try
                    {
                        if (IsListOrArrayType(property.PropertyType)) continue;
                        object value = property.GetValue(source);
                        property.SetValue(destination, value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"プロパティ '{property.Name}' のコピー中にエラーが発生しました: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// SerializedObjectでシリアライズされたフィールドをコピーする
        /// </summary>
        private static void CopySerializedProperties(Component source, Component destination)
        {
            var serializedSource = new SerializedObject(source);
            var serializedDest = new SerializedObject(destination);

            SerializedProperty prop = serializedSource.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    serializedDest.CopyFromSerializedProperty(prop);
                }
                while (prop.NextVisible(false));
            }

            serializedDest.ApplyModifiedProperties();
        }

        private static bool IsListOrArrayType(Type type)
        {
            if (type.IsArray) return true;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return true;
            if (typeof(System.Collections.IList).IsAssignableFrom(type)) return true;
            return false;
        }

        /// <summary>
        /// SerializedObjectで全プロパティをイテレートし、
        /// Transform型のObjectReferenceをboneMapで置換する。
        /// </summary>
        /// <param name="component">対象のコンポーネント</param>
        /// <param name="boneMap">ボーンマッピング辞書</param>
        private static void RemapTransformReferences(
            Component component,
            Dictionary<Transform, Transform> boneMap)
        {
            var so = new SerializedObject(component);
            SerializedProperty prop = so.GetIterator();
            bool changed = false;

            while (prop.Next(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var objRef = prop.objectReferenceValue as Transform;
                if (objRef == null)
                    continue;

                if (boneMap.TryGetValue(objRef, out Transform mapped))
                {
                    prop.objectReferenceValue = mapped;
                    changed = true;
                }
            }

            if (changed)
                so.ApplyModifiedProperties();
        }

        /// <summary>
        /// VRC SDK仕様: rootTransformがnullの場合、コンポーネントの親がroot。
        /// 移植時はnullにせず明示的にボーンを設定する。
        /// </summary>
        private static void ResolveNullRootTransform(
            Component source, Component destination,
            Dictionary<Transform, Transform> boneMap)
        {
            Transform sourceParent = source.transform.parent;
            if (sourceParent == null)
                return;

            if (!boneMap.TryGetValue(sourceParent, out Transform destBone))
                return;

            switch (destination)
            {
                case VRCPhysBone pb when pb.rootTransform == null:
                    Undo.RecordObject(pb, "Set rootTransform");
                    pb.rootTransform = destBone;
                    break;
                case VRCPhysBoneCollider pbc when pbc.rootTransform == null:
                    Undo.RecordObject(pbc, "Set rootTransform");
                    pbc.rootTransform = destBone;
                    break;
                case ContactBase contact when contact.rootTransform == null:
                    Undo.RecordObject(contact, "Set rootTransform");
                    contact.rootTransform = destBone;
                    break;
            }
        }

        /// <summary>
        /// PhysBoneのcolliders参照を旧Collider→新Colliderに置換する。
        /// </summary>
        /// <param name="pb">対象のVRCPhysBone</param>
        /// <param name="colliderMap">旧instanceID→新VRCPhysBoneColliderのマップ</param>
        private static void RemapColliderReferences(
            VRCPhysBone pb,
            Dictionary<int, VRCPhysBoneCollider> colliderMap)
        {
            if (pb.colliders == null || pb.colliders.Count == 0)
                return;

            Undo.RecordObject(pb, "Remap Collider References");

            for (int i = 0; i < pb.colliders.Count; i++)
            {
                var oldCollider = pb.colliders[i];
                if (oldCollider == null)
                    continue;

                int oldId = oldCollider.GetInstanceID();
                if (colliderMap.TryGetValue(oldId, out VRCPhysBoneCollider newCollider))
                {
                    pb.colliders[i] = newCollider;
                }
            }
        }

        /// <summary>
        /// VRCConstraintBaseのSources内SourceTransformとTargetTransformをboneMapで置換する。
        /// </summary>
        /// <param name="constraint">対象のVRCConstraintBase</param>
        /// <param name="boneMap">ボーンマッピング辞書</param>
        private static void RemapConstraintSources(
            VRCConstraintBase constraint,
            Dictionary<Transform, Transform> boneMap)
        {
            if (constraint == null)
                return;

            Undo.RecordObject(constraint, "Remap Constraint Sources");

            // TargetTransformの置換
            if (constraint.TargetTransform != null
                && boneMap.TryGetValue(constraint.TargetTransform, out Transform newTarget))
            {
                constraint.TargetTransform = newTarget;
            }

            // Sources内のSourceTransformを置換
            // SerializedObjectでSourcesにアクセス
            var so = new SerializedObject(constraint);
            var sourcesProp = so.FindProperty("Sources");
            if (sourcesProp != null && sourcesProp.isArray)
            {
                bool changed = false;
                for (int i = 0; i < sourcesProp.arraySize; i++)
                {
                    var element = sourcesProp.GetArrayElementAtIndex(i);
                    var srcTransformProp = element.FindPropertyRelative("SourceTransform");
                    if (srcTransformProp != null
                        && srcTransformProp.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var srcTransform = srcTransformProp.objectReferenceValue as Transform;
                        if (srcTransform != null && boneMap.TryGetValue(srcTransform, out Transform mapped))
                        {
                            srcTransformProp.objectReferenceValue = mapped;
                            changed = true;
                        }
                    }
                }

                if (changed)
                    so.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// コンポーネントの距離/サイズ系パラメータにスケールファクターを適用する。
        /// </summary>
        /// <param name="component">対象のコンポーネント</param>
        /// <param name="scaleFactor">スケールファクター</param>
        private static void ApplyScaleFactor(Component component, float scaleFactor)
        {
            if (Mathf.Approximately(scaleFactor, 1.0f))
                return;

            Undo.RecordObject(component, "Apply Scale Factor");

            switch (component)
            {
                case VRCPhysBone pb:
                    pb.radius = ScaleCalculator.ScaleValue(pb.radius, scaleFactor);
                    pb.endpointPosition = ScaleCalculator.ScaleVector3(
                        pb.endpointPosition, scaleFactor);
                    break;

                case VRCPhysBoneCollider pbc:
                    pbc.radius = ScaleCalculator.ScaleValue(pbc.radius, scaleFactor);
                    pbc.height = ScaleCalculator.ScaleValue(pbc.height, scaleFactor);
                    pbc.position = ScaleCalculator.ScaleVector3(pbc.position, scaleFactor);
                    break;

                case ContactBase contact:
                    contact.radius = ScaleCalculator.ScaleValue(contact.radius, scaleFactor);
                    contact.height = ScaleCalculator.ScaleValue(contact.height, scaleFactor);
                    contact.position = ScaleCalculator.ScaleVector3(contact.position, scaleFactor);
                    break;
            }
        }

        #endregion
    }
}
