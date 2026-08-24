using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            OviaMasterRecoveryContext masterRecoveryContext;
            string masterRecoveryError;
            bool hasMasterRecoveryArgument = OviaMasterRecoveryService.HasRecoveryArgument(args);

            if (hasMasterRecoveryArgument)
            {
                if (!OviaMasterRecoveryService.TryAuthorize(args, out masterRecoveryContext, out masterRecoveryError))
                {
                    MessageBox.Show(
                        masterRecoveryError,
                        "OVIA Master 복구 실패",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // 복구 실행은 ERP 인증/세션과 완전히 분리합니다.
                // 일반 배포본에는 master 계정 비밀번호나 개인키가 존재하지 않습니다.
                OviaErpAuthenticationService.ClearSession();
                OviaSessionSecurity.SetCurrentLogin(
                    masterRecoveryContext.CompanyId,
                    masterRecoveryContext.UserId,
                    masterRecoveryContext.SessionPassword,
                    OviaSessionSecurity.SystemAdministratorLevel
                );

                MessageBox.Show(
                    "OVIA Master 복구 세션으로 실행합니다.\r\n\r\n" +
                    "이 권한은 현재 OVIA 실행 세션에만 적용되며 프로그램을 종료하면 사라집니다.\r\n" +
                    "ERP 자동 로그인 세션은 생성하지 않습니다.",
                    "OVIA Master",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                Application.Run(new FrmMain(
                    masterRecoveryContext.CompanyId,
                    masterRecoveryContext.UserId
                ));
                return;
            }

            Application.Run(new Form1());
        }
    }

}
