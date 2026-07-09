using System;
using System.Drawing;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA 새 메뉴 구조에서 아직 전용 화면이 구현되지 않은 메뉴를 안전하게 표시하는 공통 안내 화면입니다.
    /// 기존 BarList/CAD 추출/공사관리 핵심 화면은 건드리지 않고, 신규 메뉴의 진입점만 먼저 구성합니다.
    /// </summary>
    public class FrmOviaMenuPage : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly string workspaceHelpKey;
        private readonly string workspaceHelpTitle;
        private readonly string workspaceHelpText;
        private readonly string pathText;
        private readonly string selectedMenu;
        private readonly string bodyText;

        private Panel contentPanel;
        private Label lblStatus;
        private Label lblBody;

        public FrmOviaMenuPage(string companyId, string userId, string key, string title, string pathText, string selectedMenu, string helpText, string bodyText)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.workspaceHelpKey = string.IsNullOrWhiteSpace(key) ? "MENU_PAGE" : key.Trim();
            this.workspaceHelpTitle = string.IsNullOrWhiteSpace(title) ? "메뉴" : title.Trim();
            this.workspaceHelpText = string.IsNullOrWhiteSpace(helpText) ? this.workspaceHelpTitle + " 화면 도움말이 아직 등록되지 않았습니다." : helpText;
            this.pathText = string.IsNullOrWhiteSpace(pathText) ? "메인  ›  " + this.workspaceHelpTitle : pathText;
            this.selectedMenu = string.IsNullOrWhiteSpace(selectedMenu) ? string.Empty : selectedMenu;
            this.bodyText = string.IsNullOrWhiteSpace(bodyText) ? this.workspaceHelpText : bodyText;

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
            BuildStatus(this);

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
            contentPanel = new Panel();
            contentPanel.BackColor = Color.White;
            contentPanel.BorderStyle = BorderStyle.FixedSingle;
            contentPanel.Location = new Point(32, 124);
            contentPanel.Size = new Size(1116, 470);
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(contentPanel);

            Label icon = new Label();
            icon.AutoSize = false;
            icon.Text = "\uE897";
            icon.Font = OviaIconFont.Create(28F, FontStyle.Regular);
            icon.ForeColor = OviaFluentTheme.Accent;
            icon.Location = new Point(32, 36);
            icon.Size = new Size(52, 52);
            icon.TextAlign = ContentAlignment.MiddleCenter;
            icon.BackColor = OviaFluentTheme.AccentLight;
            contentPanel.Controls.Add(icon);

            Label lblNotice = new Label();
            lblNotice.AutoSize = false;
            lblNotice.Text = workspaceHelpTitle;
            lblNotice.Font = OviaFluentTheme.FontTitle(13F, FontStyle.Bold);
            lblNotice.ForeColor = OviaFluentTheme.TextPrimary;
            lblNotice.Location = new Point(102, 38);
            lblNotice.Size = new Size(780, 28);
            lblNotice.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(lblNotice);

            Label lblSub = new Label();
            lblSub.AutoSize = false;
            lblSub.Text = "새 OVIA 메뉴 구조에 따라 진입점이 구성되었습니다.";
            lblSub.Font = OviaFluentTheme.FontSystem(9.3F, FontStyle.Regular);
            lblSub.ForeColor = OviaFluentTheme.TextMuted;
            lblSub.Location = new Point(104, 68);
            lblSub.Size = new Size(860, 24);
            lblSub.TextAlign = ContentAlignment.MiddleLeft;
            contentPanel.Controls.Add(lblSub);

            Panel line = new Panel();
            line.BackColor = OviaFluentTheme.CardBorder;
            line.Location = new Point(32, 114);
            line.Size = new Size(1052, 1);
            line.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.Controls.Add(line);

            lblBody = new Label();
            lblBody.AutoSize = false;
            lblBody.Font = OviaFluentTheme.FontSystem(10F, FontStyle.Regular);
            lblBody.ForeColor = OviaFluentTheme.TextSecondary;
            lblBody.Location = new Point(34, 140);
            lblBody.Size = new Size(1050, 280);
            lblBody.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblBody.TextAlign = ContentAlignment.TopLeft;
            lblBody.Text = bodyText;
            contentPanel.Controls.Add(lblBody);
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
            lblStatus.Text = workspaceHelpTitle + " 화면입니다. 실제 업무 기능은 후속 개발에서 연결됩니다.";
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
                workspace.NavigateToWorkspaceInfoPage("SETTINGS", OviaMenuHelpStore.GetWorkspacePath("SETTINGS", "메인  ›  환경설정"), OviaMenuHelpStore.GetMenuName("SETTINGS", "환경설정"), "SETTINGS", "OVIA 시스템 동작과 양식/출력 환경 설정을 관리합니다.", "환경설정의 세부 설정은 드롭다운 메뉴에서 선택합니다.");
                return;
            }

            if (normalized == "OPERATIONS")
            {
                workspace.NavigateToWorkspaceInfoPage("OPERATIONS", "메인  ›  운영현황", "운영현황", "OPERATIONS", "전체 업무 흐름을 통합 조회하고 모니터링합니다.", "운영현황의 세부 조회 화면은 드롭다운 메뉴에서 선택합니다.");
                return;
            }

            if (normalized == "MATERIAL_STOCK")
            {
                workspace.NavigateToWorkspaceInfoPage("MATERIAL_STOCK", "메인  ›  자재/재고", "자재/재고", "MATERIAL", "입고와 재고 흐름을 관리합니다.", "자재/재고의 세부 화면은 드롭다운 메뉴에서 선택합니다.");
                return;
            }

            if (normalized == "SHIPPING_INVOICE")
            {
                workspace.NavigateToWorkspaceInfoPage("SHIPPING_INVOICE", "메인  ›  출하/송장", "출하/송장", "SHIPPING", "송장, 납품, 출하 실적을 처리합니다.", "출하/송장의 세부 화면은 드롭다운 메뉴에서 선택합니다.");
                return;
            }

            if (normalized == "ERP")
            {
                workspace.NavigateToWorkspaceInfoPage("ERP", "메인  ›  ERP", "ERP", "ERP", "시스템 설정에 저장된 ERP 연결 주소를 기본 웹 브라우저로 엽니다.", "ERP는 2차 드롭다운 없이 1차 메뉴 클릭으로 바로 이동하는 단일 메뉴입니다.");
                return;
            }

            if (normalized == "MASTER_DATA")
            {
                workspace.NavigateToWorkspaceInfoPage("MASTER_DATA", "메인  ›  기준정보", "기준정보", "MASTER", "업무 기준 데이터를 관리합니다.", "기준정보의 세부 관리 화면은 드롭다운 메뉴에서 선택합니다.");
            }
        }

        private void RefreshContent()
        {
            if (lblStatus != null)
            {
                lblStatus.Text = workspaceHelpTitle + " 화면을 새로고침했습니다. " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
            if (contentPanel != null)
            {
                contentPanel.Width = width;
                contentPanel.Height = Math.Max(250, ClientSize.Height - 248);
            }

            if (lblBody != null && contentPanel != null)
            {
                lblBody.Width = Math.Max(1, contentPanel.ClientSize.Width - 68);
                lblBody.Height = Math.Max(80, contentPanel.ClientSize.Height - 170);
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
