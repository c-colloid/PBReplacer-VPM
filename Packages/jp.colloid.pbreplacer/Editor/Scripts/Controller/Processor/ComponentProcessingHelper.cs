using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
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
        #region PhysBone関連処理
        
        /// <summary>
        /// PhysBoneとPhysBoneColliderを一緒に処理するメソッド
        /// </summary>
        public static ProcessingResult ProcessPhysBones(
            ComponentProcessor processor,
            GameObject avatar, 
            List<VRCPhysBone> physBones, 
            List<VRCPhysBoneCollider> colliders)
        {
            if (avatar == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    ErrorMessage = "アバターがnullです"
                };
            }

            var result = new ProcessingResult();
            var settings = processor.Settings;

            try
            {
                // Undoグループ開始
                Undo.SetCurrentGroupName("PBReplacer - PhysBone置換");
                int undoGroup = Undo.GetCurrentGroup();

                // ルートオブジェクトを準備
                var avatarDynamics = processor.PrepareRootObject(avatar);
                result.RootObject = avatarDynamics;
                
                // コライダーマッピング（PhysBoneのコライダー参照更新用）
	            var colliderMap = 
	                new Dictionary<int, VRCPhysBoneCollider>();
                
	            // PhysBoneを処理
	            if (physBones != null && physBones.Count > 0)
	            {
		            var pbResult = processor.ProcessComponents<VRCPhysBone>(
			            avatar, 
			            physBones, 
			            settings.PhysBonesFolder,
			            (oldPB, newPB, newObj, res) => {
				            // PhysBone固有の追加処理
				            if (oldPB.rootTransform == null)
				            {
					            newPB.rootTransform = oldPB.transform;
				            }
				            else
				            {
				            	newObj.name = processor.GetSafeObjectName(newPB.rootTransform.name);
				            }
			            });
                        
		            if (!pbResult.Success)
		            {
			            return pbResult;
		            }
                    
		            result.ProcessedComponentCount += pbResult.ProcessedComponentCount;
		            result.CreatedObjects.AddRange(pbResult.CreatedObjects);
	            }
                    
                // コライダーを処理
                if (colliders != null && colliders.Count > 0)
                {
                    var colliderResult = processor.ProcessComponents<VRCPhysBoneCollider>(
                        avatar, 
                        colliders, 
                        settings.PhysBoneCollidersFolder,
                        (oldCollider, newCollider, newObj, res) => {
                            // コライダー固有の追加処理
                            if (oldCollider.rootTransform == null)
                            {
	                            newCollider.rootTransform = oldCollider.transform;
                            }
                            else
                            {
	                            newObj.name = processor.GetSafeObjectName(newCollider.rootTransform.name);
                            }
                            
                            // マッピングに追加
	                        colliderMap[oldCollider.GetInstanceID()] = newCollider;
                        });
                        
                    if (!colliderResult.Success)
                    {
                        return colliderResult;
                    }
                    
                    result.ProcessedComponentCount += colliderResult.ProcessedComponentCount;
                    result.CreatedObjects.AddRange(colliderResult.CreatedObjects);
                }

	            // PhysBoneのコライダー参照を更新
	            UpdatePhysBoneColliderReferences(physBones, colliderMap);

                // Undoグループ終了
                Undo.CollapseUndoOperations(undoGroup);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"処理中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"PhysBone処理中にエラー: {ex.Message}\n{ex.StackTrace}");
                
                if (settings.ShowProgressBar)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            return result;
        }
        
        /// <summary>
        /// PhysBoneのコライダー参照を更新するヘルパーメソッド
        /// </summary>
        private static void UpdatePhysBoneColliderReferences(
            List<VRCPhysBone> physBones,
	        Dictionary<int, VRCPhysBoneCollider> colliderMap)
	    {
            if (physBones == null || colliderMap == null || colliderMap.Count == 0)
                return;
                
            foreach (var pb in physBones)
            {
                if (pb.colliders == null) continue;

                bool modified = false;
                for (int i = 0; i < pb.colliders.Count; i++)
                {
	                var oldCollider = pb.colliders[i].GetInstanceID();
	                
                    if (oldCollider != null && colliderMap.TryGetValue(oldCollider, out var newCollider))
                    {
                        pb.colliders[i] = newCollider;
                        modified = true;
	                }
	                
                }

                if (modified)
                {
                    //EditorUtility.SetDirty(pb);
                }
            }
        }
        
	    /// <summary>
	    /// PhysBoneとPhysBoneColliderの問題をチェックするメソッド
	    /// </summary>
	    public static ValidationResult ValidatePhysBones(
		    List<VRCPhysBone> physBones, 
		    List<VRCPhysBoneCollider> colliders)
	    {
		    var result = new ValidationResult();
    
		    // PhysBoneをチェック
		    if (physBones != null)
		    {
			    foreach (var pb in physBones)
			    {
				    if (pb == null) continue;
            
				    // NullまたはMissingのrootTransformをチェック
				    /**
				    if (pb.rootTransform == null)
				    {
					    result.AddProblem(pb, new ValidationProblem
					    {
						    ComponentName = "VRCPhysBone",
						    ObjectName = pb.gameObject.name,
						    PropertyName = "Root Transform",
						    ProblemType = ValidationProblemType.NullReference
					    });
				    }
				    **/
            
				    // Nullまたは削除されたColliderの参照をチェック
				    if (pb.colliders != null)
				    {
					    var serializedProp = new SerializedObject(pb).GetIterator();
					    var count = -3;
					    while(serializedProp.NextVisible(true))
					    {
						    if (serializedProp.propertyType != SerializedPropertyType.ObjectReference) continue;
						    count++;
						    if (count < 0) continue;
						    if (serializedProp.objectReferenceValue != null) continue;

						    var fileId = serializedProp.FindPropertyRelative("m_FileID");
						    if (fileId == null || fileId.intValue == 0)
						    {
							    result.AddProblem(pb, new ValidationProblem
							    {
								    ComponentName = "VRCPhysBone",
								    ObjectName = pb.gameObject.name,
								    PropertyPath = serializedProp.propertyPath,
								    PropertyName = $"Element {count}",
								    ProblemType = ValidationProblemType.NullReference
							    });
						    	continue;
						    }

						    result.AddProblem(pb, new ValidationProblem
						    {
							    ComponentName = "VRCPhysBone",
							    ObjectName = pb.gameObject.name,
							    PropertyPath = serializedProp.propertyPath,
							    PropertyName = $"Element {count}",
							    ProblemType = ValidationProblemType.MissingReference
						    });
					    }
					    //for (int i = 0; i < pb.colliders.Count; i++)
					    //{
						    //if (pb.colliders[count] == null)
						    //{
						    //}
					    //}
				    }
			    }
		    }
    
		    // PhysBoneColliderをチェック
		    if (colliders != null)
		    {
			    foreach (var collider in colliders)
			    {
				    if (collider == null) continue;
            
				    // NullまたはMissingのrootTransformをチェック
				    /**
				    if (collider.rootTransform == null)
				    {
					    result.AddProblem(collider, new ValidationProblem
					    {
						    ComponentName = "VRCPhysBoneCollider",
						    ObjectName = collider.gameObject.name,
						    PropertyName = "Root Transform",
						    ProblemType = ValidationProblemType.NullReference
					    });
				    }
				    **/
			    }
		    }
    
		    return result;
	    }
        #endregion
        
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