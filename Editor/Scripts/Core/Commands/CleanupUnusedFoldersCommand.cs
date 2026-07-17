namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamics内の未使用フォルダを削除するコマンド。
    /// 各タブのCompositeCommandに組み込み、ProcessコマンドやFinalizeCommandとは独立して実行される。
    /// DestroyUnusedObject設定がfalseの場合は何もしない。
    /// </summary>
    public class CleanupUnusedFoldersCommand : ICommand
    {
        private readonly string[] _foldersToKeep;

        public string Description => "未使用フォルダ削除";
        public bool CanUndo => false;

        public CleanupUnusedFoldersCommand(params string[] foldersToKeep)
        {
            _foldersToKeep = foldersToKeep;
        }

        public Result<CommandResult, ProcessingError> Execute()
        {
            var avatar = AvatarFieldHelper.CurrentAvatar;
            if (avatar?.AvatarObject == null)
                return Result<CommandResult, ProcessingError>.Success(new CommandResult());

            var processor = new ComponentProcessor();

            var avatarDynamicsTransform = avatar.AvatarObject.transform
                .Find(processor.Settings.RootObjectName);
            if (avatarDynamicsTransform == null)
                return Result<CommandResult, ProcessingError>.Success(new CommandResult());

            processor.CleanupUnusedFolders(avatarDynamicsTransform.gameObject, _foldersToKeep);
            return Result<CommandResult, ProcessingError>.Success(new CommandResult());
        }

        public Result<Unit, ProcessingError> Undo()
        {
            return Result<Unit, ProcessingError>.Success(Unit.Value);
        }
    }
}
