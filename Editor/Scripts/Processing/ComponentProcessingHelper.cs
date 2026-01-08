using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// コンポーネント型ごとの固有処理を提供するヘルパークラス
    /// </summary>
    public static class ComponentProcessingHelper
    {
        #region コンストレイント関連処理
        
        /// <summary>
        /// コンストレイントを処理するメソッド
        /// </summary>
        public static ProcessingResult ProcessConstraints<T>(
            ComponentProcessor processor,
            GameObject avatar,
            List<T> constraints,
            string subfolder) where T : VRCConstraintBase
        {
            var settings = processor.Settings;
            
            // コンストレイント用のフォルダを組み合わせる
            string folderPath = $"{settings.ConstraintsFolder}/{subfolder}";
            
            // 汎用処理を呼び出し
            return processor.ProcessComponents<T>(
                avatar, 
                constraints, 
                folderPath,
                (oldConstraint, newConstraint, newObj, res) => {
	                // コンストレイント固有の追加処理があればここに実装
	                if (oldConstraint.TargetTransform == null)
	                {
	                	newConstraint.TargetTransform = oldConstraint.transform;
	                }
	                else
	                {
		                newObj.name = processor.GetSafeObjectName(newConstraint.TargetTransform.name);
	                }
                });
        }
        
        #endregion
        
        #region コンタクト関連処理
        
        /// <summary>
        /// コンタクトコンポーネントを処理するメソッド
        /// </summary>
        public static ProcessingResult ProcessContacts<T>(
            ComponentProcessor processor,
            GameObject avatar,
            List<T> contacts,
	        string subfolder) where T : ContactBase
        {
            var settings = processor.Settings;
            
            // コンタクト用のフォルダを組み合わせる
            string folderPath = $"{settings.ContactsFolder}/{subfolder}";
            
            // 汎用処理を呼び出し
            return processor.ProcessComponents<T>(
                avatar, 
                contacts, 
                folderPath,
                (oldContact, newContact, newObj, res) => {
	                // コンタクト固有の追加処理があればここに実装
	                if (oldContact.rootTransform == null)
                	{
                		newContact.rootTransform = oldContact.transform;
                	}
                	else
                	{
                    	newObj.name = processor.GetSafeObjectName(newContact.rootTransform.name);
                	}
                });
        }
        
        #endregion
    }
}