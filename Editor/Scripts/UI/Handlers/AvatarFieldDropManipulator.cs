using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
	/// <summary>
	/// アバターフィールドへのドラッグ&ドロップを処理するマニピュレータ
	/// </summary>
	public class AvatarFieldDropManipulator : Manipulator
	{
        #region Variables
		// ドロップ対象のオブジェクト
		private GameObject _targetObject;
        
		// オブジェクトフィールドへの参照
		private ObjectField _objectField;
        
		// ドロップ完了時のコールバック
		private Action<Component> _onDropCallback;
        
		// ダイアログタイトル
		private const string DIALOG_TITLE = "衣装用オプション";
        
		// ダイアログメッセージ
		private const string DIALOG_MESSAGE = 
		"このオブジェクトにはAvatarDiscriptorがついていません\n" +
		"衣装用オプションを適用しますか？\n\n" +
		"※このオプションは想定外の挙動をする可能性があります\n" +
		"※ツールの特性を理解したうえでご利用ください";
        #endregion

        #region Constructor
		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="callback">ドロップ完了時のコールバック</param>
		public AvatarFieldDropManipulator(Action<Component> callback = null)
		{
			_onDropCallback = callback;
		}
        #endregion

        #region Manipulator Implementation
		/// <summary>
		/// マニピュレータ登録時にコールバックを登録
		/// </summary>
		protected override void RegisterCallbacksOnTarget()
		{
			// 親のObjectFieldを検索
			VisualElement current = target;
			while (current != null && !(_objectField is ObjectField))
			{
				if (current is ObjectField objField)
				{
					_objectField = objField;
					break;
				}
				current = current.parent;
			}
            
			target.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
			target.RegisterCallback<DragPerformEvent>(OnDragPerform);
		}

		/// <summary>
		/// マニピュレータ解除時にコールバックを解除
		/// </summary>
		protected override void UnregisterCallbacksFromTarget()
		{
			target.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
			target.UnregisterCallback<DragPerformEvent>(OnDragPerform);
            
			_objectField = null;
		}
        #endregion

        #region Drag & Drop Event Handlers
		/// <summary>
		/// ドラッグ更新イベントの処理
		/// </summary>
		private void OnDragUpdated(DragUpdatedEvent evt)
		{
			// ドラッグ中のオブジェクト参照を取得
			if (DragAndDrop.objectReferences.Length == 0)
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
				return;
			}
            
			_targetObject = DragAndDrop.objectReferences[0] as GameObject;
			if (_targetObject == null)
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
				return;
			}
            
			// 許可条件チェック
			if (IsValidDragTarget(_targetObject))
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
			}
			else
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
			}
            
			evt.StopPropagation();
		}

		/// <summary>
		/// ドラッグ実行イベントの処理
		/// </summary>
		private void OnDragPerform(DragPerformEvent evt)
		{
			if (_targetObject == null)
			{
				return;
			}

			// 判定はAvatarValidatorに共通化。判定後の挙動（受け入れ方・警告ダイアログ）はここで維持
			var validation = AvatarValidator.Validate(_targetObject);
			switch (validation.Method)
			{
			case AvatarDetectionMethod.VRCAvatarDescriptor:
				// AvatarDescriptorがある場合は直接受け入れ
				AcceptObject(_targetObject.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>());
				break;

			case AvatarDetectionMethod.MergeArmature:
				AcceptObject(_targetObject.transform);
				break;

			case AvatarDetectionMethod.Animator:
				AcceptObject(_targetObject.GetComponent<Animator>());
				break;

			default:
				// 警告ダイアログを表示
				if (EditorUtility.DisplayDialog(DIALOG_TITLE, DIALOG_MESSAGE, "OK", "Cancel"))
				{
					AcceptObject(_targetObject.transform);
				}
				break;
			}

			evt.StopPropagation();
		}
        #endregion

        #region Helper Methods
		/// <summary>
		/// ドラッグ対象が有効かどうかを判定
		/// （いずれも持たない場合、ドラッグ中の受付表示は不可。ドロップ時の警告ダイアログでの受け入れ判断は別途行われる）
		/// </summary>
		private bool IsValidDragTarget(GameObject obj)
		{
			return AvatarValidator.Validate(obj).IsValid;
		}

		/// <summary>
		/// オブジェクトをフィールドに設定
		/// </summary>
		private void AcceptObject(Component obj)
		{	
			if (_objectField != null)
			{
				_objectField.value = obj;
			}
            
			// コールバック通知
			_onDropCallback?.Invoke(obj);
		}
        #endregion
	}
}