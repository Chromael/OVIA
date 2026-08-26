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

            OviaErpLaunchRequest launchRequest;
            bool hasLaunchRequest = OviaErpLaunchService.TryParseLaunchRequest(args, out launchRequest);
            OviaErpLogoutRequest startupLogoutRequest;
            bool hasStartupLogoutRequest = OviaErpLaunchService.TryParseLogoutRequest(args, out startupLogoutRequest);

            if (!OviaSingleInstanceService.TryBecomePrimary())
            {
                string command = OviaSingleInstanceService.GetForwardCommand(args);

                if (!OviaSingleInstanceService.ForwardToPrimary(command))
                {
                    MessageBox.Show(
                        "OVIA가 이미 실행 중이지만 기존 OVIA에 실행 요청을 전달하지 못했습니다.\r\n" +
                        "기존 OVIA 창을 확인한 뒤 다시 시도해주세요.",
                        "OVIA 실행",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }

                return;
            }

            try
            {
                // ERP 로그아웃 이벤트만으로 새 OVIA 프로세스를 띄우지 않습니다.
                // 기존 OVIA 인스턴스가 없으면 이 명령은 조용히 종료합니다.
                if (hasStartupLogoutRequest)
                {
                    return;
                }

                if (hasLaunchRequest)
                {
                    RunFromErpLaunch(launchRequest);
                    return;
                }

                RunNormalLogin();
            }
            finally
            {
                OviaSingleInstanceService.Stop();
            }
        }

        private static void RunNormalLogin()
        {
            Form1 loginForm = new Form1();

            loginForm.Shown += delegate
            {
                OviaSingleInstanceService.StartServer(
                    loginForm,
                    delegate(string command)
                    {
                        HandleForwardedCommand(loginForm, command);
                    }
                );
            };

            Application.Run(loginForm);
        }

        private static void RunFromErpLaunch(OviaErpLaunchRequest launchRequest)
        {
            OviaErpLaunchResult launch;
            try
            {
                launch = OviaErpLaunchService.ExchangeAsync(launchRequest).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP OVIA 실행 처리 중 오류가 발생했습니다.\r\n" + ex.Message,
                    "OVIA ERP 실행 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (launch == null || !launch.IsSuccess)
            {
                MessageBox.Show(
                    launch == null ? "ERP OVIA 실행 인증 결과를 확인할 수 없습니다." : launch.Message,
                    "OVIA ERP 실행 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            // Launch Ticket은 로그인 화면을 건너뛰기 위한 1회성 인증수단이다.
            // ID/PW는 명령행/URL에 전달하지 않고, 교환 성공 후 받은 OVIA API 토큰만 메모리에 유지한다.
            OviaErpAuthenticationService.AdoptLaunchSession(launch.CompanyId, launch.UserId, launch.OviaToken, launch.WebSessionTicket, launch.LogoutTicket);
            OviaSessionSecurity.SetCurrentLogin(launch.CompanyId, launch.UserId, "", launch.UserLevel);

            FrmMain mainForm = new FrmMain(launch.CompanyId, launch.UserId);
            mainForm.Shown += async delegate
            {
                OviaSingleInstanceService.StartServer(
                    mainForm,
                    delegate(string command)
                    {
                        HandleForwardedCommand(mainForm, command);
                    }
                );

                await OpenLaunchTargetAsync(mainForm, launch);
            };

            Application.Run(mainForm);
        }

        private static async void HandleForwardedCommand(Form dispatcherForm, string command)
        {
            if (OviaSingleInstanceService.IsActivateCommand(command))
            {
                ActivateCurrentOviaWindow();
                return;
            }

            OviaErpLogoutRequest logoutRequest;
            if (OviaErpLaunchService.TryParseLogoutRequest(new string[] { command }, out logoutRequest))
            {
                FrmMain logoutMainForm = FindOpenMainForm();
                if (logoutMainForm != null)
                {
                    logoutMainForm.HandleErpLogoutSignal(logoutRequest.CompanyId, logoutRequest.Ticket);
                }
                return;
            }

            OviaErpLaunchRequest request;
            if (!OviaErpLaunchService.TryParseLaunchRequest(new string[] { command }, out request))
            {
                return;
            }

            OviaErpLaunchResult launch;
            try
            {
                launch = await OviaErpLaunchService.ExchangeAsync(request);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP OVIA 실행 처리 중 오류가 발생했습니다.\r\n" + ex.Message,
                    "OVIA ERP 실행 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (launch == null || !launch.IsSuccess)
            {
                MessageBox.Show(
                    launch == null ? "ERP OVIA 실행 인증 결과를 확인할 수 없습니다." : launch.Message,
                    "OVIA ERP 실행 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            FrmMain mainForm = FindOpenMainForm();

            if (mainForm == null)
            {
                OviaErpAuthenticationService.AdoptLaunchSession(launch.CompanyId, launch.UserId, launch.OviaToken, launch.WebSessionTicket, launch.LogoutTicket);
                OviaSessionSecurity.SetCurrentLogin(launch.CompanyId, launch.UserId, "", launch.UserLevel);

                Form1 loginForm = FindOpenLoginForm();
                mainForm = new FrmMain(launch.CompanyId, launch.UserId);

                if (loginForm != null)
                {
                    Form1 capturedLogin = loginForm;
                    FrmMain capturedMain = mainForm;

                    mainForm.FormClosed += delegate
                    {
                        if (capturedMain.IsLogoutRequested)
                        {
                            capturedLogin.Show();
                            capturedLogin.Activate();
                            return;
                        }

                        capturedLogin.Close();
                    };

                    loginForm.Hide();
                    mainForm.Show();
                }
                else
                {
                    mainForm.Show();
                }
            }
            else
            {
                // 이미 열린 메인 화면의 권한/회사 컨텍스트를 다른 사용자로 몰래 바꾸지 않습니다.
                if (!OviaSessionSecurity.IsCurrentLoginUser(launch.CompanyId, launch.UserId))
                {
                    MessageBox.Show(
                        "현재 실행 중인 OVIA의 로그인 사용자와 ERP 실행 사용자가 다릅니다.\r\n" +
                        "데이터와 권한이 섞이지 않도록 현재 OVIA에서 로그아웃한 뒤 다시 실행해주세요.",
                        "OVIA 사용자 확인",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    ActivateCurrentOviaWindow();
                    return;
                }

                // 같은 사용자라면 ERP가 새로 발급한 API 토큰만 현재 메모리 세션에 갱신합니다.
                OviaErpAuthenticationService.AdoptLaunchSession(launch.CompanyId, launch.UserId, launch.OviaToken, launch.WebSessionTicket, launch.LogoutTicket);
            }

            ActivateForm(mainForm);
            await OpenLaunchTargetAsync(mainForm, launch);
        }

        private static FrmMain FindOpenMainForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                FrmMain main = form as FrmMain;
                if (main != null && !main.IsDisposed)
                {
                    return main;
                }
            }

            return null;
        }

        private static Form1 FindOpenLoginForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                Form1 login = form as Form1;
                if (login != null && !login.IsDisposed)
                {
                    return login;
                }
            }

            return null;
        }

        private static void ActivateCurrentOviaWindow()
        {
            FrmMain main = FindOpenMainForm();
            if (main != null)
            {
                ActivateForm(main);
                return;
            }

            Form1 login = FindOpenLoginForm();
            if (login != null)
            {
                ActivateForm(login);
            }
        }

        private static void ActivateForm(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            if (!form.Visible)
            {
                form.Show();
            }

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.BringToFront();
            form.Activate();
        }

        private static async Task OpenLaunchTargetAsync(FrmMain mainForm, OviaErpLaunchResult launch)
        {
            if (mainForm == null || launch == null) return;

            if (OviaErpLaunchService.IsNewBarListTarget(launch))
            {
                mainForm.NavigateToNewBarListRegistration(
                    launch.ProjectNo,
                    launch.ProjectName,
                    launch.ClientName,
                    launch.ProjectStatus
                );
                return;
            }

            if (!string.Equals(launch.TargetType, "barlist", StringComparison.OrdinalIgnoreCase)) return;
            if (launch.BarListId <= 0 || string.IsNullOrWhiteSpace(launch.ProjectNo)) return;

            try
            {
                string filePath = await OviaErpLaunchService.PrepareBarListAsync(launch);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    mainForm.NavigateToBarList(
                        launch.ProjectNo,
                        launch.ProjectName,
                        launch.ClientName,
                        launch.ProjectStatus,
                        filePath
                    );
                    return;
                }

                mainForm.NavigateToProjectBarListList(
                    launch.ProjectNo,
                    launch.ProjectName,
                    launch.ClientName,
                    launch.ProjectStatus
                );

                MessageBox.Show(
                    "ERP에서 지정한 BarList를 OVIA에 동기화했지만 해당 항목을 바로 찾지 못했습니다.\r\n" +
                    "공사별 BarList 목록을 열었습니다. 새로고침 후 다시 확인해주세요.",
                    "OVIA BarList 열기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP BarList를 여는 중 오류가 발생했습니다.\r\n" + ex.Message,
                    "OVIA BarList 열기 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
    }

}
