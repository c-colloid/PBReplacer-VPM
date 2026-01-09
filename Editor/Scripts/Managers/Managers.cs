using System;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 全マネージャーへの統一アクセスを提供するレジストリクラス
    /// </summary>
    public static class Managers
    {
        /// <summary>
        /// PhysBoneマネージャー
        /// </summary>
        public static PhysBoneDataManager PhysBone => PhysBoneDataManager.Instance;

        /// <summary>
        /// PhysBoneColliderマネージャー
        /// </summary>
        public static PhysBoneColliderManager PhysBoneCollider => PhysBoneColliderManager.Instance;

        /// <summary>
        /// Constraintマネージャー
        /// </summary>
        public static ConstraintDataManager Constraint => ConstraintDataManager.Instance;

        /// <summary>
        /// Contactマネージャー
        /// </summary>
        public static ContactDataManager Contact => ContactDataManager.Instance;

        /// <summary>
        /// 全マネージャーのデータをリロード
        /// </summary>
        public static void ReloadAll()
        {
            PhysBone.ReloadData();
            PhysBoneCollider.ReloadData();
            Constraint.ReloadData();
            Contact.ReloadData();
        }

        /// <summary>
        /// 全マネージャーのデータをクリア
        /// </summary>
        public static void ClearAll()
        {
            PhysBone.ClearData();
            PhysBoneCollider.ClearData();
            Constraint.ClearData();
            Contact.ClearData();
        }

        /// <summary>
        /// 全マネージャーのクリーンアップ
        /// </summary>
        public static void CleanupAll()
        {
            PhysBone.Cleanup();
            PhysBoneCollider.Cleanup();
            Constraint.Cleanup();
            Contact.Cleanup();
        }

        /// <summary>
        /// UIタブに対応するマネージャーのデータをリロード
        /// タブインデックス: 0=PhysBone(PB+PBC), 1=Constraint, 2=Contact
        /// </summary>
        public static void ReloadForTab(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: // PhysBone（PB + PBC両方）
                    PhysBoneCollider.ReloadData();
                    PhysBone.ReloadData();
                    break;
                case 1: // Constraint
                    Constraint.ReloadData();
                    break;
                case 2: // Contact
                    Contact.ReloadData();
                    break;
            }
        }

        /// <summary>
        /// UIタブに対応するマネージャーのコンポーネントを処理
        /// タブインデックス: 0=PhysBone(PB+PBC), 1=Constraint, 2=Contact
        /// </summary>
        public static bool ProcessForTab(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: // PhysBone（PBC→PBの順で処理）
                    var pbcResult = PhysBoneCollider.ProcessComponents();
                    var pbResult = PhysBone.ProcessComponents();
                    return pbcResult && pbResult;
                case 1: // Constraint
                    return Constraint.ProcessComponents();
                case 2: // Contact
                    return Contact.ProcessComponents();
                default:
                    return false;
            }
        }
    }
}
