using System;
using System.Drawing;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    /// <summary>
    /// WebView2 전환 단계에서 Web ERP 페이지를 OVIA Desktop 내부에 표시하는 공통 화면입니다.
    /// AutoCAD 제어와 로컬 설치 환경 체크는 WinForms/C#에 남기고, 공사등록 같은 업무 콘텐츠는 Web ERP로 이전합니다.
    /// </summary>
    public class FrmOviaWebErpPage : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly string workspaceHelpKey;
        private readonly string workspaceHelpTitle;
        private readonly string workspaceHelpText;
        private readonly string pathText;
        private readonly string selectedMenu;
        private readonly string routePath;

        private OviaWebViewHost webViewHost;
        private Label lblStatus;

        public FrmOviaWebErpPage(string companyId, string userId, string key, string title, string pathText, string selectedMenu, string routePath, string helpText)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.workspaceHelpKey = string.IsNullOrWhiteSpace(key) ? "WEB_ERP_PAGE" : key.Trim();
            this.workspaceHelpTitle = string.IsNullOrWhiteSpace(title) ? "Web ERP" : title.Trim();
            this.pathText = string.IsNullOrWhiteSpace(pathText) ? "메인  ›  " + this.workspaceHelpTitle : pathText;
            this.selectedMenu = string.IsNullOrWhiteSpace(selectedMenu) ? string.Empty : selectedMenu;
            this.routePath = string.IsNullOrWhiteSpace(routePath) ? string.Empty : routePath.Trim();
            this.workspaceHelpText = string.IsNullOrWhiteSpace(helpText) ? this.workspaceHelpTitle + " Web ERP 페이지입니다." : helpText;

            BuildUI();
        }

        public string WorkspaceHelpKey { get { return workspaceHelpKey; } }
        public string WorkspaceHelpTitle { get { return workspaceHelpTitle; } }
        public string WorkspaceHelpText { get { return workspaceHelpText; } }

        private void BuildUI()
        {
            SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            Controls.Clear();

            Text = "OVIA - " + workspaceHelpTitle;
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1060, 650);
            BackColor = OviaFluentTheme.AppBackground;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildContent(this);

            ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { NavigateBackOrMain(); },
                delegate { NavigateBackOrMain(); },
                delegate { RefreshContent(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    NavigateByTarget(target);
                });
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(Math.Max(1, parent.ClientSize.Width), 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, selectedMenu, companyId, userId);
            parent.Controls.Add(commandBar);
        }

        private void BuildContent(Control parent)
        {
            webViewHost = new OviaWebViewHost();
            webViewHost.BackColor = Color.White;
            webViewHost.BorderStyle = BorderStyle.None;
            webViewHost.Margin = Padding.Empty;
            webViewHost.Padding = Padding.Empty;
            webViewHost.Location = new Point(0, 98);
            webViewHost.Size = new Size(Math.Max(1, parent.ClientSize.Width), Math.Max(1, parent.ClientSize.Height - 98));
            webViewHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            webViewHost.AutoResizeToDocumentHeight = false;
            webViewHost.ForwardMouseWheelToParentScroll = false;
            webViewHost.InitialUrl = ResolveWebErpUrl();
            parent.Controls.Add(webViewHost);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Location = new Point(32, 638);
            lblStatus.Size = new Size(1116, 24);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.5F, FontStyle.Regular);
            lblStatus.ForeColor = OviaFluentTheme.TextMuted;
            lblStatus.Text = "Web ERP 연결 페이지입니다. 주소: " + ResolveWebErpUrl();
            parent.Controls.Add(lblStatus);
        }

        private void CommandBar_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawLine(pen, 0, 0, control.Width, 0);
                e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
            }
        }

        private string ResolveWebErpUrl()
        {
            OviaSystemSettings settings = OviaSystemSettingsStore.Load();
            string baseUrl = settings == null || settings.ErpLoginUrl == null ? string.Empty : settings.ErpLoginUrl.Trim();

            if (baseUrl == "")
            {
                return OviaWebViewHost.NormalizeUrl("https://celmon.com");
            }

            string normalizedBase = OviaWebViewHost.NormalizeUrl(baseUrl);
            if (string.IsNullOrWhiteSpace(routePath))
            {
                return normalizedBase;
            }

            try
            {
                Uri baseUri = new Uri(normalizedBase, UriKind.Absolute);
                string relativePath = routePath.TrimStart('/');
                Uri targetUri = new Uri(baseUri, relativePath);
                return targetUri.AbsoluteUri;
            }
            catch
            {
                return normalizedBase;
            }
        }

        private void RefreshContent()
        {
            string url = ResolveWebErpUrl();
            if (webViewHost != null)
            {
                webViewHost.Navigate(url);
            }

            if (lblStatus != null)
            {
                lblStatus.Text = "Web ERP 페이지를 새로고침했습니다. " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " / 주소: " + url;
            }
        }

        private void NavigateBackOrMain()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateToMain();
            }
            else
            {
                Close();
            }
        }

        private void NavigateByTarget(string target)
        {
            string normalized = target == null ? string.Empty : target.Trim().ToUpperInvariant();
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace == null)
            {
                Close();
                return;
            }

            if (normalized == "MAIN")
            {
                workspace.NavigateToMain();
                return;
            }

            if (normalized == "PROJECT_MANAGER")
            {
                workspace.NavigateToProjectManager();
                return;
            }

            if (normalized == "SETTINGS")
            {
                workspace.NavigateToWorkspaceInfoPage("SETTINGS", "메인  ›  시스템관리", "시스템관리", "SETTINGS", "OVIA 시스템 동작과 양식/출력 환경 설정을 관리합니다.", "시스템관리의 세부 설정은 드롭다운 메뉴에서 선택합니다.");
                return;
            }

            if (normalized == "ERP")
            {
                workspace.NavigateToWorkspaceInfoPage("ERP", "메인  ›  ERP", "ERP", "ERP", "시스템 설정에 저장된 ERP 연결 주소를 기본 웹 브라우저로 엽니다.", "ERP는 2차 드롭다운 없이 1차 메뉴 클릭으로 바로 이동하는 단일 메뉴입니다.");
            }
        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.RequestLogout();
            }
            else
            {
                Close();
            }
        }

        public void ApplyWorkspaceLayout()
        {
            int width = Math.Max(1, ClientSize.Width - 64);
            if (webViewHost != null)
            {
                webViewHost.Width = width;
                webViewHost.Height = Math.Max(250, ClientSize.Height - 184);
            }

            if (lblStatus != null)
            {
                lblStatus.Top = Math.Max(0, ClientSize.Height - 58);
                lblStatus.Width = width;
            }
        }

        public bool CanLeaveWorkspaceScreen()
        {
            return true;
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }
    }
}
