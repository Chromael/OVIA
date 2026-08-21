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
    public class FrmOviaWebErpPage : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceBrowserNavigation
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly string workspaceMenuKey;
        private readonly string workspaceTitle;
        private readonly string workspaceDescription;
        private readonly string pathText;
        private readonly string selectedMenu;
        private readonly string routePath;

        private OviaWebViewHost webViewHost;
        private OviaWorkspaceHeader workspaceHeader;

        public FrmOviaWebErpPage(string companyId, string userId, string key, string title, string pathText, string selectedMenu, string routePath, string descriptionText)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.workspaceMenuKey = string.IsNullOrWhiteSpace(key) ? "WEB_ERP_PAGE" : key.Trim();
            this.workspaceTitle = string.IsNullOrWhiteSpace(title) ? "Web ERP" : title.Trim();
            this.pathText = string.IsNullOrWhiteSpace(pathText) ? "메인  ›  " + this.workspaceTitle : pathText;
            this.selectedMenu = string.IsNullOrWhiteSpace(selectedMenu) ? string.Empty : selectedMenu;
            this.routePath = string.IsNullOrWhiteSpace(routePath) ? string.Empty : routePath.Trim();
            this.workspaceDescription = string.IsNullOrWhiteSpace(descriptionText) ? this.workspaceTitle + " Web ERP 페이지입니다." : descriptionText;

            BuildUI();
        }

        private void BuildUI()
        {
            SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            Controls.Clear();

            Text = "OVIA - " + workspaceTitle;
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
            workspaceHeader = OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { NavigateBackOrMain(); },
                delegate { NavigateBackOrMain(); },
                delegate { RefreshContent(); },
                delegate { RequestLogout(); },
                false,
                true,
                delegate(string target)
                {
                    NavigateByTarget(target);
                });
            UpdateHeaderNavigationState();
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
            webViewHost.Location = new Point(0, 48);
            webViewHost.Size = new Size(Math.Max(1, parent.ClientSize.Width), Math.Max(1, parent.ClientSize.Height - 48));
            webViewHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            webViewHost.AutoResizeToDocumentHeight = false;
            webViewHost.ForwardMouseWheelToParentScroll = false;
            webViewHost.EnableErpAutomaticLogin = true;
            webViewHost.InitialUrl = ResolveWebErpUrl();
            webViewHost.NavigationStateChanged += WebViewHost_NavigationStateChanged;
            parent.Controls.Add(webViewHost);
            UpdateHeaderNavigationState();
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
            string connectionUrl = OviaCompanyConnectionStore.GetErpConnectionUrl(companyId);

            if (string.IsNullOrWhiteSpace(connectionUrl))
            {
                return "about:blank";
            }

            if (string.Equals(workspaceMenuKey, "ERP", StringComparison.OrdinalIgnoreCase))
            {
                return connectionUrl;
            }

            if (!string.IsNullOrWhiteSpace(routePath))
            {
                string route = routePath.Trim();
                if (route.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || route.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return OviaWebViewHost.NormalizeUrl(route);
                }

                try
                {
                    if (!connectionUrl.EndsWith("/", StringComparison.Ordinal))
                    {
                        connectionUrl += "/";
                    }

                    Uri baseUri = new Uri(connectionUrl, UriKind.Absolute);
                    Uri targetUri = new Uri(baseUri, route.TrimStart('/'));
                    return targetUri.AbsoluteUri;
                }
                catch
                {
                }
            }

            return connectionUrl;
        }

        private void WebViewHost_NavigationStateChanged(object sender, EventArgs e)
        {
            UpdateHeaderNavigationState();
        }

        private void UpdateHeaderNavigationState()
        {
            if (workspaceHeader != null && !workspaceHeader.IsDisposed)
            {
                workspaceHeader.RefreshNavigationButtonStates();
            }
        }

        private void RefreshContent()
        {
            string url = ResolveWebErpUrl();
            if (webViewHost != null)
            {
                webViewHost.Navigate(url);
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
                workspace.NavigateToWorkspaceInfoPage("SETTINGS", "메인  ›  환경설정", "환경설정", "SETTINGS", "OVIA 시스템 동작과 공통 환경 설정을 관리합니다.", "환경설정 아이콘에서 필요한 설정 화면을 선택합니다.");
                return;
            }

            if (normalized == "ERP")
            {
                workspace.NavigateToErpModulePage("ERP");
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
            if (webViewHost != null)
            {
                webViewHost.Width = Math.Max(1, ClientSize.Width);
                webViewHost.Height = Math.Max(1, ClientSize.Height - webViewHost.Top);
            }
        }


        public bool CanNavigateBackInBrowser
        {
            get { return webViewHost != null && webViewHost.CanGoBackInWebView; }
        }

        public bool CanNavigateForwardInBrowser
        {
            get { return webViewHost != null && webViewHost.CanGoForwardInWebView; }
        }

        public bool NavigateBackInBrowser()
        {
            bool navigated = webViewHost != null && webViewHost.TryGoBackInWebView();
            UpdateHeaderNavigationState();
            return navigated;
        }

        public bool NavigateForwardInBrowser()
        {
            bool navigated = webViewHost != null && webViewHost.TryGoForwardInWebView();
            UpdateHeaderNavigationState();
            return navigated;
        }

        public bool RefreshBrowser()
        {
            bool reloaded = webViewHost != null && webViewHost.TryReloadCurrentWebViewPage();

            UpdateHeaderNavigationState();
            return reloaded;
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
