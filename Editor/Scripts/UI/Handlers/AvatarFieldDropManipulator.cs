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

			var accepted = ResolveAvatarComponent(_targetObject);
			if (accepted != null)
			{
				AcceptObject(accepted);
			}

			evt.StopPropagation();
		}

		/// <summary>
		/// ドロップ／ピッカーで指定された GameObject を「アバターとして受け入れるコンポーネント」に解決する。
		/// 判定は AvatarValidator に共通化。AvatarDescriptor / MA MergeArmature / Animator は直接受け入れ、
		/// それ以外は衣装用オプションの警告ダイアログを出し、了承されたときだけ受け入れる（キャンセルは null）。
		/// </summary>
		public static Component ResolveAvatarComponent(GameObject obj)
		{
			if (obj == null) return null;

			var validation = AvatarValidator.Validate(obj);
			switch (validation.Method)
			{
			case AvatarDetectionMethod.VRCAvatarDescriptor:
				return obj.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>();

			case AvatarDetectionMethod.MergeArmature:
				return obj.transform;

			case AvatarDetectionMethod.Animator:
				return obj.GetComponent<Animator>();

			default:
				if (EditorUtility.DisplayDialog(DIALOG_TITLE, DIALOG_MESSAGE, "OK", "Cancel"))
				{
					return obj.transform;
				}
				return null;
			}
		}
        #endregion

        #region Helper Methods
		/// <summary>
		/// ドラッグ対象が有効かどうかを判定
		/// 意図的に常にtrueを返す: AvatarDescriptor等を持たないオブジェクト(衣装・抜き出したボーン等)も受け入れ、
		/// 可否の最終判断はOnDragPerformの警告ダイアログでユーザーに委ねる設計。
		/// ここでRejectedにするとUnityの仕様上DragPerform自体が発火せず、ダイアログでの受け入れ経路が死ぬため、
		/// この判定で弾いてはならない。
		/// </summary>
		private bool IsValidDragTarget(GameObject obj)
		{
			return obj != null;
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