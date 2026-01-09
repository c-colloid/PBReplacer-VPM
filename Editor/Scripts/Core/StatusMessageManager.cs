using System;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// メッセージの優先度
    /// </summary>
    public enum MessagePriority
    {
        Info = 0,      // 通常情報
        Success = 1,   // 成功メッセージ
        Warning = 2,   // 警告
        Error = 3      // エラー（最優先）
    }

    /// <summary>
    /// ステータスメッセージを一元管理するクラス
    /// 優先度システムにより、重要なメッセージが上書きされないようにする
    /// </summary>
    public static class StatusMessageManager
    {
        private static string _currentMessage = "";
        private static MessagePriority _currentPriority = MessagePriority.Info;
        private static double _messageTime;

        /// <summary>
        /// メッセージが変更された時に発火するイベント
        /// </summary>
        public static event Action<string> OnMessageChanged;

        /// <summary>
        /// 現在のメッセージを取得
        /// </summary>
        public static string CurrentMessage => _currentMessage;

        /// <summary>
        /// 現在の優先度を取得
        /// </summary>
        public static MessagePriority CurrentPriority => _currentPriority;

        /// <summary>
        /// 優先度付きでメッセージを設定
        /// 現在のメッセージより優先度が高いか、一定時間経過した場合のみ更新
        /// </summary>
        /// <param name="message">表示するメッセージ</param>
        /// <param name="priority">メッセージの優先度</param>
        public static void SetMessage(string message, MessagePriority priority = MessagePriority.Info)
        {
            // 現在のメッセージより優先度が高いか、一定時間経過した場合のみ更新
            if (priority >= _currentPriority || HasMessageExpired())
            {
                _currentMessage = message;
                _currentPriority = priority;
                _messageTime = EditorApplication.timeSinceStartup;
                OnMessageChanged?.Invoke(message);

                // EventBus経由でも通知（後方互換性）
                EventBus.Publish(new StatusMessageEvent(message));
            }
        }

        /// <summary>
        /// エラーメッセージを設定（最高優先度）
        /// </summary>
        /// <param name="message">エラーメッセージ</param>
        public static void Error(string message)
        {
            SetMessage($"エラー: {message}", MessagePriority.Error);
        }

        /// <summary>
        /// 警告メッセージを設定
        /// </summary>
        /// <param name="message">警告メッセージ</param>
        public static void Warning(string message)
        {
            SetMessage($"警告: {message}", MessagePriority.Warning);
        }

        /// <summary>
        /// 成功メッセージを設定
        /// </summary>
        /// <param name="message">成功メッセージ</param>
        public static void Success(string message)
        {
            SetMessage(message, MessagePriority.Success);
        }

        /// <summary>
        /// 情報メッセージを設定（最低優先度）
        /// </summary>
        /// <param name="message">情報メッセージ</param>
        public static void Info(string message)
        {
            SetMessage(message, MessagePriority.Info);
        }

        /// <summary>
        /// メッセージの期限切れをチェック
        /// エラーは3秒、Successは2秒、その他は1秒で上書き可能になる
        /// </summary>
        private static bool HasMessageExpired()
        {
            // _messageTimeが0の場合は期限切れとみなす（初期状態）
            if (_messageTime <= 0) return true;

            double expireTime = _currentPriority switch
            {
                MessagePriority.Error => 3f,
                MessagePriority.Success => 2f,
                _ => 1f
            };

            // EditorApplication.timeSinceStartupを使用（Editorで確実に動作）
            return EditorApplication.timeSinceStartup - _messageTime > expireTime;
        }

        /// <summary>
        /// 優先度をリセット（新しい処理を開始する前に呼ぶ）
        /// </summary>
        public static void ResetPriority()
        {
            _currentPriority = MessagePriority.Info;
            _messageTime = 0;
        }

        /// <summary>
        /// メッセージと優先度をクリア
        /// </summary>
        public static void Clear()
        {
            _currentMessage = "";
            _currentPriority = MessagePriority.Info;
            _messageTime = 0;
        }
    }
}
