using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
    /// <summary>
    /// アバター関連のデータを保持するクラス
    /// </summary>
    [Serializable]
    public class AvatarData
    {
        // アバター本体のGameObject
        public GameObject AvatarObject { get; private set; }
        
        // アバターのアーマチュア
        public GameObject Armature { get; private set; }
        
        // アバターのAnimatorコンポーネント
        public Animator AvatarAnimator { get; private set; }
        
        // アバターが完全なVRChatアバターかどうか（VRC_AvatarDescriptorを持つ）
        public bool IsFullAvatar { get; private set; }
        
        // ModularAvatarを使用しているかどうか
        public bool UsesModularAvatar { get; private set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="avatarObject">アバターのGameObject</param>
        public AvatarData(GameObject avatarObject)
        {
            if (avatarObject == null)
                throw new ArgumentNullException(nameof(avatarObject), "アバターオブジェクトがnullです");

            AvatarObject = avatarObject;
            
            // VRC_AvatarDescriptorの確認
            IsFullAvatar = avatarObject.GetComponent<VRC_AvatarDescriptor>() != null;
            
            // Animatorの取得
            AvatarAnimator = avatarObject.GetComponent<Animator>();
            
            // ModularAvatarの確認
            #if MODULAR_AVATAR
            UsesModularAvatar = HasModularAvatarComponents(avatarObject);
            #else
            UsesModularAvatar = false;
            #endif
            
            // アーマチュアの検出
            DetectArmature();
            
            if (Armature == null)
                throw new InvalidOperationException("アーマチュアを検出できませんでした");
        }

        /// <summary>
        /// アーマチュアを検出する
        /// </summary>
        private void DetectArmature()
        {
            // アニメーターが存在し、Humanoidアバターならヒエラルキーから検出
            if (AvatarAnimator != null && AvatarAnimator.isHuman)
            {
                Transform hips = AvatarAnimator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null && hips.parent != null)
                {
                    Armature = hips.parent.gameObject;
                    return;
                }
            }
            
            // ModularAvatarのマージアーマチュアを確認
            #if MODULAR_AVATAR
            if (TryGetModularAvatarArmature(out GameObject maArmature))
            {
                Armature = maArmature;
                return;
            }
            #endif
            
            // 従来の方式でアーマチュアを検出
            FindLargestChildrenStructure();
        }

        /// <summary>
        /// 最も大きな子階層を持つオブジェクトをアーマチュアとして検出
        /// </summary>
        private void FindLargestChildrenStructure()
        {
            GameObject avatarDynamics = AvatarObject.transform.Find("AvatarDynamics")?.gameObject;
            IEnumerable<Transform> avatarDynamicsChildren = avatarDynamics?.GetComponentsInChildren<Transform>();
            
            GameObject largestChild = null;
            int maxChildCount = 0;
            
            foreach (Transform child in AvatarObject.GetComponentsInChildren<Transform>())
            {
                // AvatarObject自身とAvatarDynamicsの子は除外
                if (child == AvatarObject.transform ||
                    (avatarDynamicsChildren != null && avatarDynamicsChildren.Contains(child)))
                {
                    continue;
                }
                
                int childCount = child.GetComponentsInChildren<Transform>().Length;
                if (childCount > maxChildCount)
                {
                    maxChildCount = childCount;
                    largestChild = child.gameObject;
                }
            }
            
            Armature = largestChild;
        }
        
        #if MODULAR_AVATAR
        /// <summary>
        /// ModularAvatarのマージアーマチュアを取得
        /// </summary>
        private bool TryGetModularAvatarArmature(out GameObject maArmature)
        {
            maArmature = null;
            
            // アバター自体にマージアーマチュアがあるか確認
            ModularAvatarMergeArmature mergeComponent = AvatarObject.GetComponent<ModularAvatarMergeArmature>();
            if (mergeComponent != null)
            {
                maArmature = AvatarObject;
                return true;
            }
            
            // 子オブジェクトにマージアーマチュアがあるか確認
            var mergeComponents = AvatarObject.GetComponentsInChildren<ModularAvatarMergeArmature>(true);
            if (mergeComponents.Length > 0)
            {
                maArmature = mergeComponents[0].gameObject;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// ModularAvatarのコンポーネントがアバターに存在するか確認
        /// </summary>
        private bool HasModularAvatarComponents(GameObject avatar)
        {
            // ModularAvatarのコンポーネントを検索
            return avatar.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
        }
        #endif
    }
}
