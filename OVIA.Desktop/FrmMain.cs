using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OVIA.Desktop
{
    public class FrmMain : Form, IOviaWorkspaceNavigator
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly string companyId;
        private readonly string userId;

        private Label lblAutoCadValue;
        private Label lblAutoCadNote;
        private Label lblAutoCadRunStatus;
        private Label lblAutoCadRunNote;
        private OviaStatusLamp autoCadStatusLamp;
        private Timer autoCadStatusTimer;
        private Timer workspaceStatusTimer;
        private ToolTip windowToolTip;
        private TableLayoutPanel mainLayout;
        private Panel workspacePanel;
        private Label bottomStatusLabel;
        private Form currentScreen;
        private Form projectManagerForm;
        private FrmBarList barListForm;
        private FrmBarListMappingManager barListMappingForm;
        private bool logoutConfirmed;

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
        private readonly Color BrandCyan = Color.FromArgb(64, 156, 255);
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        public bool IsLogoutRequested { get; private set; }

        public FrmMain(string companyId, string userId)
        {
            this.companyId = companyId;
            this.userId = userId;

            BuildMainUI();
        }

        private void BuildMainUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA";
            this.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1240, 760);
            this.MinimumSize = new Size(1100, 750);
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmMain_FormClosing;

            windowToolTip = new ToolTip();
            windowToolTip.AutoPopDelay = 4000;
            windowToolTip.InitialDelay = 350;
            windowToolTip.ReshowDelay = 100;
            windowToolTip.ShowAlways = true;

            GradientPanel bg = new GradientPanel();
            bg.Dock = DockStyle.Fill;
            bg.StartColor = OviaFluentTheme.AppBackgroundAlt;
            bg.EndColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(bg);
            EnableDashboardDrag(bg);

            mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 2;
            mainLayout.BackColor = SurfaceColor;
            mainLayout.Margin = Padding.Empty;
            mainLayout.Padding = Padding.Empty;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            bg.Controls.Add(mainLayout);

            workspacePanel = new Panel();
            workspacePanel.Dock = DockStyle.Fill;
            workspacePanel.BackColor = SurfaceColor;
            workspacePanel.Margin = Padding.Empty;
            workspacePanel.Padding = Padding.Empty;
            workspacePanel.Resize += WorkspacePanel_Resize;
            mainLayout.Controls.Add(workspacePanel, 0, 0);

            BuildBottomStatus(mainLayout);

            ShowDashboard();

            this.ResumeLayout(false);

            StartAutoCadStatusTimer();
            StartWorkspaceStatusTimer();
        }

        private void BuildTopMenu(TableLayoutPanel parent)
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.Margin = Padding.Empty;
            top.Padding = new Padding(16, 8, 16, 8);
            top.BackColor = Color.White;
            parent.Controls.Add(top, 0, 0);

            OviaMenuButton dashboardMenu = AddMenu(top, "메인", 16, true);
            dashboardMenu.Click += delegate { ShowDashboard(); };
            OviaMenuButton projectMenu = AddMenu(top, "공사관리", 112, false);
            projectMenu.Click += OpenProjectManager_Click;

            OviaMenuButton cadMenu = AddMenu(top, "AutoCAD 연결", 220, false);
            cadMenu.Click += DetectAutoCad_Click;
            OviaMenuButton extractMenu = AddMenu(top, "도면 추출", 348, false);
            extractMenu.Click += ExtractReady_Click;

            OviaMenuButton barListMenu = AddMenu(top, "BarList", 456, false);
            barListMenu.Click += OpenBarList_Click;

            OviaMenuButton settingsMenu = AddMenu(top, "환경 설정", 548, false);
            settingsMenu.Click += OpenBarListMapping_Click;

            OviaMenuButton barListMappingMenu = AddMenu(top, "BarList 항목 매핑", 668, false);
            barListMappingMenu.Click += OpenBarListMapping_Click;

            OviaSmallButton logout = new OviaSmallButton();
            logout.Text = "로그아웃";
            logout.Size = new Size(92, 32);
            logout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            logout.Location = new Point(Math.Max(16, top.ClientSize.Width - 108), 10);
            logout.Click += Logout_Click;
            top.Controls.Add(logout);
            top.Resize += delegate
            {
                logout.Location = new Point(Math.Max(16, top.ClientSize.Width - 108), 10);
            };
        }

        private void BuildBottomStatus(TableLayoutPanel parent)
        {
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = Padding.Empty;
            bottom.Padding = new Padding(0, 1, 0, 0);
            bottom.BackColor = SurfaceColor;
            bottom.Paint += BottomStatus_Paint;
            parent.Controls.Add(bottom, 0, 1);

            bottomStatusLabel = new Label();
            bottomStatusLabel.AutoSize = false;
            bottomStatusLabel.Dock = DockStyle.Fill;
            bottomStatusLabel.Padding = new Padding(16, 0, 16, 0);
            bottomStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            bottomStatusLabel.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            bottomStatusLabel.ForeColor = TextSub;
            bottomStatusLabel.BackColor = SurfaceColor;
            bottom.Controls.Add(bottomStatusLabel);
        }

        private void BottomStatus_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            using (Pen pen = new Pen(Color.FromArgb(234, 234, 234), 1))
            {
                e.Graphics.DrawLine(pen, 0, 0, control.Width, 0);
            }
        }

        private OviaMenuButton AddMenu(Control parent, string text, int left, bool selected)
        {
            OviaMenuButton menu = new OviaMenuButton();
            menu.Text = text;
            menu.Location = new Point(left, 8);
            menu.Size = new Size(text.Length > 7 ? 134 : 94, 36);
            menu.Selected = selected;
            parent.Controls.Add(menu);

            return menu;
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = "OVIA 메인";
            title.AutoSize = true;
            title.Font = OviaFluentTheme.FontKorean(22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 128);
            parent.Controls.Add(title);
            EnableDashboardDrag(title);

            Panel cadStatusBox = new Panel();
            cadStatusBox.Location = new Point(470, 133);
            cadStatusBox.Size = new Size(175, 40);
            cadStatusBox.BackColor = SurfaceColor;
            parent.Controls.Add(cadStatusBox);
            EnableDashboardDrag(cadStatusBox);

            autoCadStatusLamp = new OviaStatusLamp();
            autoCadStatusLamp.Location = new Point(0, 8);
            autoCadStatusLamp.Size = new Size(24, 24);
            autoCadStatusLamp.IsActive = false;
            cadStatusBox.Controls.Add(autoCadStatusLamp);

            lblAutoCadRunStatus = new Label();
            lblAutoCadRunStatus.Text = "AutoCAD 비활성";
            lblAutoCadRunStatus.AutoSize = true;
            lblAutoCadRunStatus.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
            lblAutoCadRunStatus.ForeColor = OviaFluentTheme.Danger;
            lblAutoCadRunStatus.BackColor = SurfaceColor;
            lblAutoCadRunStatus.Location = new Point(30, 2);
            cadStatusBox.Controls.Add(lblAutoCadRunStatus);

            lblAutoCadRunNote = new Label();
            lblAutoCadRunNote.Text = "실행 필요";
            lblAutoCadRunNote.AutoSize = true;
            lblAutoCadRunNote.Font = new Font("맑은 고딕", 8F, FontStyle.Regular);
            lblAutoCadRunNote.ForeColor = TextSub;
            lblAutoCadRunNote.BackColor = SurfaceColor;
            lblAutoCadRunNote.Location = new Point(31, 21);
            cadStatusBox.Controls.Add(lblAutoCadRunNote);

        }

        private void BuildStatusCards(Control parent)
        {
            Label dummyValue1;
            Label dummyNote1;
            Label dummyValue2;
            Label dummyNote2;

            AddStatusCard(
                parent,
                "라이선스 상태",
                "정상",
                "셀먼 OVIA 관리자 인증 대기",
                new Point(34, 223),
                BrandViolet,
                out dummyValue1,
                out dummyNote1
            );

            AddStatusCard(
                parent,
                "AutoCAD 상태",
                "비활성",
                "AutoCAD를 실행해주세요.",
                new Point(284, 223),
                BrandCyan,
                out lblAutoCadValue,
                out lblAutoCadNote
            );

            AddStatusCard(
                parent,
                "프로그램 버전",
                "1.0.0",
                "초기 개발 테스트 버전",
                new Point(534, 223),
                BrandIndigo,
                out dummyValue2,
                out dummyNote2
            );
        }

        private void AddStatusCard(Control parent, string title, string value, string note, Point location, Color accent, out Label valueLabel, out Label noteLabel)
        {
            valueLabel = null;
            noteLabel = null;

            OviaDashboardCard card = new OviaDashboardCard();
            card.Location = location;
            card.Size = new Size(220, 130);
            card.SurfaceColor = SurfaceColor;
            card.AccentColor = accent;
            parent.Controls.Add(card);

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            lblTitle.ForeColor = TextSub;
            lblTitle.BackColor = Color.White;
            lblTitle.Location = new Point(20, 18);
            card.Controls.Add(lblTitle);

            Label lblValue = new Label();
            lblValue.Text = value;
            lblValue.AutoSize = true;
            lblValue.Font = new Font("맑은 고딕", 20F, FontStyle.Bold);
            lblValue.ForeColor = TextDark;
            lblValue.BackColor = Color.White;
            lblValue.Location = new Point(18, 45);
            card.Controls.Add(lblValue);

            Label lblNote = new Label();
            lblNote.Text = note;
            lblNote.AutoSize = false;
            lblNote.Size = new Size(180, 34);
            lblNote.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblNote.ForeColor = TextSub;
            lblNote.BackColor = Color.White;
            lblNote.Location = new Point(20, 92);
            card.Controls.Add(lblNote);

            valueLabel = lblValue;
            noteLabel = lblNote;
        }

        private void BuildActionCards(Control parent)
        {
            OviaLargeCard cadCard = new OviaLargeCard();
            cadCard.Location = new Point(34, 393);
            cadCard.Size = new Size(345, 235);
            cadCard.SurfaceColor = SurfaceColor;
            parent.Controls.Add(cadCard);

            Label cadTitle = new Label();
            cadTitle.Text = "AutoCAD 연결";
            cadTitle.AutoSize = true;
            cadTitle.Font = OviaFluentTheme.FontKorean(16F, FontStyle.Bold);
            cadTitle.ForeColor = TextDark;
            cadTitle.BackColor = Color.White;
            cadTitle.Location = new Point(28, 28);
            cadCard.Controls.Add(cadTitle);

            Label cadDesc = new Label();
            cadDesc.Text = "사용자 PC에 설치된 AutoCAD 버전을 확인하고,\r\n지원 가능한 버전에 맞는 OVIA 연동 모듈을\r\n준비합니다.";
            cadDesc.AutoSize = false;
            cadDesc.Size = new Size(290, 70);
            cadDesc.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            cadDesc.ForeColor = TextSub;
            cadDesc.BackColor = Color.White;
            cadDesc.Location = new Point(30, 72);
            cadCard.Controls.Add(cadDesc);

            OviaActionButton cadButton = new OviaActionButton();
            cadButton.Text = "AutoCAD 감지 시작";
            cadButton.Location = new Point(30, 160);
            cadButton.Size = new Size(280, 44);
            cadButton.StartColor = BrandViolet;
            cadButton.EndColor = BrandIndigo;
            cadButton.Click += DetectAutoCad_Click;
            cadCard.Controls.Add(cadButton);

            OviaLargeCard extractCard = new OviaLargeCard();
            extractCard.Location = new Point(414, 393);
            extractCard.Size = new Size(345, 235);
            extractCard.SurfaceColor = SurfaceColor;
            parent.Controls.Add(extractCard);

            Label extractTitle = new Label();
            extractTitle.Text = "도면 추출";
            extractTitle.AutoSize = true;
            extractTitle.Font = OviaFluentTheme.FontKorean(16F, FontStyle.Bold);
            extractTitle.ForeColor = TextDark;
            extractTitle.BackColor = Color.White;
            extractTitle.Location = new Point(28, 28);
            extractCard.Controls.Add(extractTitle);

            Label extractDesc = new Label();
            extractDesc.Text = "AutoCAD 도면에서 선택 영역의 문자와 표를\r\n읽어 BarList 후보 데이터로 정리합니다.\r\n현재는 화면 구성 단계입니다.";
            extractDesc.AutoSize = false;
            extractDesc.Size = new Size(290, 70);
            extractDesc.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            extractDesc.ForeColor = TextSub;
            extractDesc.BackColor = Color.White;
            extractDesc.Location = new Point(30, 72);
            extractCard.Controls.Add(extractDesc);

            OviaActionButton extractButton = new OviaActionButton();
            extractButton.Text = "도면 추출 준비";
            extractButton.Location = new Point(30, 160);
            extractButton.Size = new Size(280, 44);
            extractButton.StartColor = BrandCyan;
            extractButton.EndColor = BrandViolet;
            extractButton.Click += ExtractReady_Click;
            extractCard.Controls.Add(extractButton);
        }

        private void ShowDashboard()
        {
            if (!CloseCurrentScreenForNavigation())
            {
                return;
            }

            this.Text = "OVIA 메인";
            workspacePanel.Controls.Clear();
            currentScreen = null;

            Panel dashboard = new Panel();
            dashboard.Dock = DockStyle.Fill;
            dashboard.BackColor = SurfaceColor;
            workspacePanel.Controls.Add(dashboard);

            BuildDashboardExplorerHeader(dashboard);
            BuildDashboardCommandBar(dashboard);
            BuildHeader(dashboard);
            BuildStatusCards(dashboard);
            BuildActionCards(dashboard);
            SetBottomStatus("회사 ID : " + companyId + " / 사용자 ID : " + userId + " / 사용자명 : " + userId + " / 접속 IP : " + GetLocalIPAddress());
            UpdateAutoCadRunStatus();
        }

        private void BuildDashboardExplorerHeader(Control parent)
        {
            Panel bar = new Panel();
            bar.Location = new Point(34, 8);
            bar.Size = new Size(Math.Max(1, parent.ClientSize.Width - 68), 32);
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bar.BackColor = SurfaceColor;
            parent.Controls.Add(bar);

            Button back = CreateExplorerButton("\uE72B", "뒤로");
            StyleExplorerButtonInactive(back);
            bar.Controls.Add(back);

            Button forward = CreateExplorerButton("\uE72A", "앞으로");
            forward.Location = new Point(36, 0);
            StyleExplorerButtonInactive(forward);
            bar.Controls.Add(forward);

            Button up = CreateExplorerButton("\uE74A", "위로");
            up.Location = new Point(72, 0);
            StyleExplorerButtonInactive(up);
            bar.Controls.Add(up);

            Button refresh = CreateExplorerButton("\uE72C", "새로고침");
            refresh.Location = new Point(108, 0);
            refresh.Click += delegate { ShowDashboard(); };
            bar.Controls.Add(refresh);

            Panel addressBar = CreateDashboardPathAddressBar();
            addressBar.Location = new Point(152, 0);
            addressBar.Size = new Size(Math.Max(1, bar.ClientSize.Width - 188), 32);
            addressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bar.Controls.Add(addressBar);

            Button logout = CreateExplorerButton("\uE7E8", "로그아웃");
            logout.Location = new Point(Math.Max(152, bar.ClientSize.Width - 30), 0);
            logout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            logout.Click += delegate { RequestLogout(); };
            bar.Controls.Add(logout);

            bar.Resize += delegate
            {
                addressBar.Width = Math.Max(1, bar.ClientSize.Width - 188);
                logout.Location = new Point(Math.Max(152, bar.ClientSize.Width - 30), 0);
            };

            parent.Resize += delegate
            {
                bar.Width = Math.Max(1, parent.ClientSize.Width - 68);
                addressBar.Width = Math.Max(1, bar.ClientSize.Width - 188);
                logout.Location = new Point(Math.Max(152, bar.ClientSize.Width - 30), 0);
            };
        }

        private Panel CreateDashboardPathAddressBar()
        {
            Panel panel = new Panel();
            panel.BackColor = Color.White;
            panel.Margin = Padding.Empty;
            panel.Padding = new Padding(10, 6, 10, 0);

            TextBox textBox = new TextBox();
            textBox.Text = "메인";
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            textBox.ForeColor = Color.Black;
            textBox.BackColor = Color.White;
            textBox.Location = new Point(10, 7);
            textBox.Size = new Size(880, 20);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = Padding.Empty;
            textBox.TabStop = false;
            textBox.Click += delegate { textBox.SelectAll(); };
            textBox.Enter += delegate { textBox.SelectAll(); };
            panel.Controls.Add(textBox);

            return panel;
        }

        private void BuildDashboardCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(Math.Max(1, parent.ClientSize.Width), 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "MAIN");
            parent.Controls.Add(commandBar);
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

        private Button CreateExplorerButton(string text, string tip)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(30, 30);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.NavigationHover;
            button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.NavigationSelected;
            button.Font = new Font("Segoe MDL2 Assets", 9.5F, FontStyle.Regular);
            button.ForeColor = Color.Black;
            button.BackColor = SurfaceColor;
            button.TabStop = false;
            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, tip);
            }
            return button;
        }

        private void StyleExplorerButtonInactive(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.ForeColor = Color.FromArgb(175, 181, 190);
            button.Cursor = Cursors.Default;
            button.FlatAppearance.MouseOverBackColor = SurfaceColor;
            button.FlatAppearance.MouseDownBackColor = SurfaceColor;
        }

        public void NavigateToMain()
        {
            ShowDashboard();
        }

        public void RequestLogout()
        {
            if (!ConfirmLogout())
            {
                return;
            }

            logoutConfirmed = true;
            IsLogoutRequested = true;
            this.Close();
        }

        private string GetLocalIPAddress()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                int i;

                for (i = 0; i < host.AddressList.Length; i++)
                {
                    if (host.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        return host.AddressList[i].ToString();
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private void EnableDashboardDrag(Control control)
        {
            if (control == null)
            {
                return;
            }

            control.MouseDown += DashboardDrag_MouseDown;
        }

        private void DashboardDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (this.WindowState == FormWindowState.Maximized)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        private void RestoreAndActivate(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.WindowState = FormWindowState.Maximized;
            form.Show();
            form.Activate();
        }

        private void PrepareDashboardChildWindow(Form form)
        {
            if (form == null)
            {
                return;
            }

            form.StartPosition = FormStartPosition.Manual;
            form.Location = GetChildWindowLocation(form.Size);
            form.WindowState = FormWindowState.Maximized;
        }

        private Point GetChildWindowLocation(Size childSize)
        {
            int x = this.Left + 160;
            int y = this.Top + 100;

            Screen screen = Screen.FromControl(this);

            if (x + childSize.Width > screen.WorkingArea.Right)
            {
                x = screen.WorkingArea.Right - childSize.Width - 20;
            }

            if (y + childSize.Height > screen.WorkingArea.Bottom)
            {
                y = screen.WorkingArea.Bottom - childSize.Height - 20;
            }

            if (x < screen.WorkingArea.Left)
            {
                x = screen.WorkingArea.Left + 20;
            }

            if (y < screen.WorkingArea.Top)
            {
                y = screen.WorkingArea.Top + 20;
            }

            return new Point(x, y);
        }

        private void DetectAutoCad_Click(object sender, EventArgs e)
        {
            List<AutoCadInstallInfo> installs = AutoCadDetector.FindInstalledAutoCad();

            if (installs.Count == 0)
            {
                lblAutoCadValue.Text = "미감지";
                lblAutoCadNote.Text = "AutoCAD 일반 버전을 찾지 못했습니다.";

                MessageBox.Show(
                    "설치된 AutoCAD 일반 버전을 찾지 못했습니다.\r\n\r\nAutoCAD LT만 설치되어 있거나, AutoCAD가 설치되어 있지 않을 수 있습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                UpdateAutoCadRunStatus();

                return;
            }

            AutoCadInstallInfo selected = installs[0];

            lblAutoCadValue.Text = selected.YearText;
            lblAutoCadNote.Text = selected.PluginGroup;

            MessageBox.Show(
                selected.GetDisplayText(),
                "OVIA AutoCAD 감지 결과",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            UpdateAutoCadRunStatus();
        }

        private void OpenProjectManager_Click(object sender, EventArgs e)
        {
            NavigateToProjectManager();
        }

        private void OpenBarList_Click(object sender, EventArgs e)
        {
            NavigateToBarList("", "", "", "", "");
        }

        private void OpenBarListMapping_Click(object sender, EventArgs e)
        {
            if (!IsSystemAdminUser())
            {
                MessageBox.Show(
                    "BarList 항목 매핑은 시스템관리자만 접근할 수 있습니다.\r\n\r\n현재 사용자 ID: " + userId,
                    "OVIA 권한 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            NavigateToBarListMapping();
        }

        public void NavigateToProjectManager()
        {
            ShowWorkspaceScreen(new FrmProjectManager(companyId, userId), "OVIA 공사관리", "공사관리 화면입니다.");
        }

        public void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus)
        {
            ShowWorkspaceScreen(new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus), "OVIA 공사별 BarList", "저장된 BarList 목록을 불러왔습니다.");
        }

        public void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath)
        {
            string filePath = initialFilePath == null ? "" : initialFilePath;
            string title = filePath.Trim() == "" ? "OVIA 신규 BarList 등록" : "OVIA BarList";
            ShowWorkspaceScreen(new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath), title, filePath.Trim() == "" ? "신규 BarList 등록 화면입니다." : "저장된 BarList를 열었습니다.");
        }

        public void NavigateToBarListMapping()
        {
            ShowWorkspaceScreen(new FrmBarListMappingManager(companyId, userId), "OVIA BarList 항목 매핑", "BarList 항목 매핑 설정을 불러왔습니다.");
        }

        private void ShowWorkspaceScreen(Form nextScreen, string title, string statusText)
        {
            if (nextScreen == null)
            {
                return;
            }

            if (!CloseCurrentScreenForNavigation())
            {
                nextScreen.Dispose();
                return;
            }

            workspacePanel.SuspendLayout();

            try
            {
                workspacePanel.Controls.Clear();
                nextScreen.TopLevel = false;
                nextScreen.FormBorderStyle = FormBorderStyle.None;
                nextScreen.Dock = DockStyle.Fill;
                nextScreen.StartPosition = FormStartPosition.Manual;
                nextScreen.WindowState = FormWindowState.Normal;
                nextScreen.FormClosed += delegate
                {
                    if (currentScreen == nextScreen)
                    {
                        currentScreen = null;
                        ShowDashboard();
                    }
                };

                currentScreen = nextScreen;
                this.Text = title;
                workspacePanel.Controls.Add(nextScreen);
                nextScreen.Show();
                nextScreen.Bounds = workspacePanel.ClientRectangle;
                ApplyWorkspaceLayout(nextScreen);
                nextScreen.BringToFront();
                MirrorWorkspaceStatus(nextScreen, statusText);

                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (currentScreen == nextScreen && !nextScreen.IsDisposed)
                        {
                            nextScreen.Bounds = workspacePanel.ClientRectangle;
                            ApplyWorkspaceLayout(nextScreen);
                            MirrorWorkspaceStatus(nextScreen, statusText);
                        }
                    }));
                }
                catch
                {
                }
            }
            finally
            {
                workspacePanel.ResumeLayout(false);
            }
        }

        private bool CloseCurrentScreenForNavigation()
        {
            if (currentScreen == null)
            {
                return true;
            }

            IOviaWorkspaceScreen workspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (workspaceScreen != null && !workspaceScreen.CanLeaveWorkspaceScreen())
            {
                return false;
            }

            if (workspaceScreen != null)
            {
                workspaceScreen.BeforeLeaveWorkspaceScreen();
            }

            if (!currentScreen.IsDisposed)
            {
                Form closingScreen = currentScreen;
                currentScreen = null;
                workspacePanel.Controls.Remove(closingScreen);
                closingScreen.Dispose();
            }
            else
            {
                currentScreen = null;
            }
            return true;
        }

        private void ApplyWorkspaceLayout(Form screen)
        {
            IOviaWorkspaceLayout workspaceLayout = screen as IOviaWorkspaceLayout;

            if (workspaceLayout != null)
            {
                workspaceLayout.ApplyWorkspaceLayout();
            }
        }

        private void WorkspacePanel_Resize(object sender, EventArgs e)
        {
            if (currentScreen == null || currentScreen.IsDisposed)
            {
                return;
            }

            currentScreen.Bounds = workspacePanel.ClientRectangle;
            ApplyWorkspaceLayout(currentScreen);
        }

        private void SetBottomStatus(string text)
        {
            if (bottomStatusLabel != null)
            {
                bottomStatusLabel.Text = text == null ? "" : text;
            }
        }

        private void StartWorkspaceStatusTimer()
        {
            if (workspaceStatusTimer != null)
            {
                workspaceStatusTimer.Stop();
                workspaceStatusTimer.Dispose();
                workspaceStatusTimer = null;
            }

            workspaceStatusTimer = new Timer();
            workspaceStatusTimer.Interval = 500;
            workspaceStatusTimer.Tick += delegate
            {
                if (currentScreen != null && !currentScreen.IsDisposed)
                {
                    MirrorWorkspaceStatus(currentScreen, "");
                }
            };
            workspaceStatusTimer.Start();
        }

        private void MirrorWorkspaceStatus(Form screen, string fallbackText)
        {
            string text = GetScreenStatusText(screen);

            if (text.Trim() == "")
            {
                text = fallbackText == null ? "" : fallbackText;
            }

            SetBottomStatus(text);
        }

        private string GetScreenStatusText(Form screen)
        {
            if (screen == null)
            {
                return "";
            }

            try
            {
                FieldInfo field = screen.GetType().GetField("lblStatus", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null)
                {
                    return "";
                }

                Label label = field.GetValue(screen) as Label;

                if (label == null)
                {
                    return "";
                }

                label.Visible = false;
                return label.Text == null ? "" : label.Text;
            }
            catch
            {
                return "";
            }
        }

        private bool IsSystemAdminUser()
        {
            string value = userId == null ? "" : userId.Trim().ToLowerInvariant();

            if (value == "")
            {
                return false;
            }

            return value == "admin"
                || value == "administrator"
                || value == "systemadmin"
                || value == "sysadmin"
                || value == "root"
                || value == "celmon"
                || value == "oviaadmin"
                || value == "system"
                || value == "관리자"
                || value == "시스템관리자";
        }

        private void ExtractReady_Click(object sender, EventArgs e)
        {
            UpdateAutoCadRunStatus();

            if (!AutoCadRuntimeChecker.IsAutoCadRunning())
            {
                MessageBox.Show(
                    "AutoCAD 비활성 상태입니다.\r\n\r\n도면 추출 기능을 사용하려면 먼저 AutoCAD를 실행하고 DWG 도면을 열어주세요.",
                    "OVIA AutoCAD 비활성",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                "AutoCAD 활성 상태입니다.\r\n\r\nAutoCAD에서 OVIA 플러그인 DLL을 NETLOAD로 로드한 뒤 OVIABOX / OVIABOXTABLE 명령어를 사용할 수 있습니다.",
                "OVIA AutoCAD 활성",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void StartAutoCadStatusTimer()
        {
            if (autoCadStatusTimer != null)
            {
                autoCadStatusTimer.Stop();
                autoCadStatusTimer.Dispose();
                autoCadStatusTimer = null;
            }

            autoCadStatusTimer = new Timer();
            autoCadStatusTimer.Interval = 2000;
            autoCadStatusTimer.Tick += AutoCadStatusTimer_Tick;
            autoCadStatusTimer.Start();

            UpdateAutoCadRunStatus();
        }

        private void AutoCadStatusTimer_Tick(object sender, EventArgs e)
        {
            UpdateAutoCadRunStatus();
        }

        private void UpdateAutoCadRunStatus()
        {
            bool isRunning = AutoCadRuntimeChecker.IsAutoCadRunning();

            if (autoCadStatusLamp != null)
            {
                autoCadStatusLamp.IsActive = isRunning;
                autoCadStatusLamp.Invalidate();
            }

            if (lblAutoCadRunStatus != null)
            {
                lblAutoCadRunStatus.Text = isRunning ? "AutoCAD 활성" : "AutoCAD 비활성";
                lblAutoCadRunStatus.ForeColor = isRunning ? OviaFluentTheme.Success : OviaFluentTheme.Danger;
            }

            if (lblAutoCadRunNote != null)
            {
                lblAutoCadRunNote.Text = isRunning ? "acad.exe 실행 중" : "AutoCAD 실행 필요";
            }

            if (lblAutoCadValue != null)
            {
                lblAutoCadValue.Text = isRunning ? "활성" : "비활성";
                lblAutoCadValue.ForeColor = isRunning ? OviaFluentTheme.Success : OviaFluentTheme.Danger;
            }

            if (lblAutoCadNote != null)
            {
                lblAutoCadNote.Text = isRunning ? "AutoCAD가 실행 중입니다." : "AutoCAD를 실행해주세요.";
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (currentScreen != null && !currentScreen.IsDisposed)
            {
                currentScreen.Dispose();
                currentScreen = null;
            }

            if (projectManagerForm != null && !projectManagerForm.IsDisposed)
            {
                projectManagerForm.Close();
                projectManagerForm = null;
            }

            if (barListForm != null && !barListForm.IsDisposed)
            {
                barListForm.Close();
                barListForm = null;
            }

            if (barListMappingForm != null && !barListMappingForm.IsDisposed)
            {
                barListMappingForm.Close();
                barListMappingForm = null;
            }

            if (autoCadStatusTimer != null)
            {
                autoCadStatusTimer.Stop();
                autoCadStatusTimer.Dispose();
                autoCadStatusTimer = null;
            }

            if (workspaceStatusTimer != null)
            {
                workspaceStatusTimer.Stop();
                workspaceStatusTimer.Dispose();
                workspaceStatusTimer = null;
            }

            base.OnFormClosed(e);
        }

        private void Logout_Click(object sender, EventArgs e)
        {
            RequestLogout();
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!logoutConfirmed && currentScreen != null && !currentScreen.IsDisposed)
            {
                e.Cancel = true;
                ShowDashboard();
                return;
            }

            if (!logoutConfirmed && !ConfirmLogout())
            {
                e.Cancel = true;
                return;
            }

            if (!CloseCurrentScreenForNavigation())
            {
                e.Cancel = true;
                logoutConfirmed = false;
                IsLogoutRequested = false;
                return;
            }

            logoutConfirmed = true;
            IsLogoutRequested = true;
        }

        private bool ConfirmLogout()
        {
            return MessageBox.Show(
                "로그아웃을 하시겠습니까?",
                "OVIA",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question
            ) == DialogResult.OK;
        }
    }

    public class AutoCadInstallInfo
    {
        public string ProductName = "";
        public string VersionKey = "";
        public string InstallPath = "";
        public int Year = 0;
        public bool IsLT = false;

        public string YearText
        {
            get
            {
                if (Year > 0)
                {
                    return Year.ToString();
                }

                return "감지됨";
            }
        }

        public string PluginGroup
        {
            get
            {
                if (IsLT)
                {
                    return "AutoCAD LT는 지원하지 않습니다.";
                }

                if (Year >= 2027)
                {
                    return ".NET 10용 OVIA 모듈 대상";
                }

                if (Year >= 2025 && Year <= 2026)
                {
                    return ".NET 8용 OVIA 모듈 대상";
                }

                if (Year >= 2021 && Year <= 2024)
                {
                    return ".NET Framework 4.8용 OVIA 모듈 대상";
                }

                if (Year >= 2019 && Year <= 2020)
                {
                    return "2차 지원 검토 대상";
                }

                return "지원 버전 추가 검토 필요";
            }
        }

        public string GetDisplayText()
        {
            string text = "";

            text += "AutoCAD 감지 결과\r\n\r\n";
            text += "제품명: " + ProductName + "\r\n";

            if (VersionKey != "")
            {
                text += "버전 키: " + VersionKey + "\r\n";
            }

            if (Year > 0)
            {
                text += "판단 연도: " + Year.ToString() + "\r\n";
            }

            if (InstallPath != "")
            {
                text += "설치 경로: " + InstallPath + "\r\n";
            }

            text += "\r\nOVIA 판단: " + PluginGroup;

            return text;
        }
    }

    public static class AutoCadDetector
    {
        public static List<AutoCadInstallInfo> FindInstalledAutoCad()
        {
            List<AutoCadInstallInfo> results = new List<AutoCadInstallInfo>();

            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry32);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry32);

            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry32);

            RemoveDuplicates(results);
            SortByYearDesc(results);
            RemoveLtOnlyIfGeneralExists(results);

            return results;
        }

        private static void ScanAutoCadRegistryRoot(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");

                if (root == null)
                {
                    return;
                }

                ScanRegistryRecursive(results, root, "", 0);
                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static void ScanRegistryRecursive(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey, int depth)
        {
            if (depth > 4 || key == null)
            {
                return;
            }

            TryReadAutoCadInfo(results, key, versionKey);

            string[] subNames;

            try
            {
                subNames = key.GetSubKeyNames();
            }
            catch
            {
                return;
            }

            int i;

            for (i = 0; i < subNames.Length; i++)
            {
                try
                {
                    RegistryKey sub = key.OpenSubKey(subNames[i]);

                    string nextVersionKey = versionKey;

                    if (nextVersionKey == "")
                    {
                        nextVersionKey = subNames[i];
                    }
                    else
                    {
                        nextVersionKey += "\\" + subNames[i];
                    }

                    ScanRegistryRecursive(results, sub, nextVersionKey, depth + 1);

                    if (sub != null)
                    {
                        sub.Close();
                    }
                }
                catch
                {
                }
            }
        }

        private static void TryReadAutoCadInfo(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey)
        {
            string productName = ReadRegistryString(key, "ProductName");

            if (productName == "")
            {
                productName = ReadRegistryString(key, "DisplayName");
            }

            if (productName == "")
            {
                productName = ReadRegistryString(key, "Product");
            }

            if (productName == "")
            {
                return;
            }

            if (productName.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            AutoCadInstallInfo info = new AutoCadInstallInfo();
            info.ProductName = productName;
            info.VersionKey = versionKey;
            info.InstallPath = ReadPossibleInstallPath(key);
            info.Year = ExtractYear(productName + " " + versionKey);
            info.IsLT = productName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

            results.Add(info);
        }

        private static void ScanUninstallRegistry(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

                if (root == null)
                {
                    return;
                }

                string[] subNames = root.GetSubKeyNames();
                int i;

                for (i = 0; i < subNames.Length; i++)
                {
                    RegistryKey sub = root.OpenSubKey(subNames[i]);

                    if (sub == null)
                    {
                        continue;
                    }

                    string displayName = ReadRegistryString(sub, "DisplayName");

                    if (displayName.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AutoCadInstallInfo info = new AutoCadInstallInfo();
                        info.ProductName = displayName;
                        info.VersionKey = subNames[i];
                        info.InstallPath = ReadPossibleInstallPath(sub);
                        info.Year = ExtractYear(displayName);
                        info.IsLT = displayName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

                        results.Add(info);
                    }

                    sub.Close();
                }

                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static string ReadPossibleInstallPath(RegistryKey key)
        {
            string value = "";

            value = ReadRegistryString(key, "AcadLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "Location");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallDir");
            if (value != "")
            {
                return value;
            }

            return "";
        }

        private static string ReadRegistryString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);

                if (value == null)
                {
                    return "";
                }

                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static int ExtractYear(string text)
        {
            Match match = Regex.Match(text, @"20\d{2}");

            if (!match.Success)
            {
                return 0;
            }

            int year = 0;
            int.TryParse(match.Value, out year);

            return year;
        }

        private static void RemoveDuplicates(List<AutoCadInstallInfo> list)
        {
            int i;
            int j;

            for (i = list.Count - 1; i >= 0; i--)
            {
                for (j = 0; j < i; j++)
                {
                    if (
                        string.Equals(list[i].ProductName, list[j].ProductName, StringComparison.OrdinalIgnoreCase) &&
                        list[i].Year == list[j].Year
                    )
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static void SortByYearDesc(List<AutoCadInstallInfo> list)
        {
            list.Sort(delegate (AutoCadInstallInfo a, AutoCadInstallInfo b)
            {
                return b.Year.CompareTo(a.Year);
            });
        }

        private static void RemoveLtOnlyIfGeneralExists(List<AutoCadInstallInfo> list)
        {
            bool hasGeneral = false;
            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (!list[i].IsLT)
                {
                    hasGeneral = true;
                    break;
                }
            }

            if (!hasGeneral)
            {
                return;
            }

            for (i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsLT)
                {
                    list.RemoveAt(i);
                }
            }
        }
    }

    public static class AutoCadRuntimeChecker
    {
        public static bool IsAutoCadRunning()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("acad");

                return processes != null && processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public class OviaMenuButton : Control
    {
        public bool Selected = false;

        public OviaMenuButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            if (Selected)
            {
                using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 8))
                {
                    using (SolidBrush brush = new SolidBrush(OviaFluentTheme.NavigationSelected))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
            }

            Color color = Selected ? OviaFluentTheme.Accent : OviaFluentTheme.TextPrimary;
            bool hasDropDownIcon = this.Text != null && this.Text.IndexOf("\uE70D", StringComparison.Ordinal) >= 0;
            string displayText = hasDropDownIcon ? this.Text.Replace("\uE70D", "").TrimEnd() : this.Text;

            TextRenderer.DrawText(
                e.Graphics,
                displayText,
                new Font("맑은 고딕", 10F, FontStyle.Bold),
                rect,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding
            );

            if (hasDropDownIcon)
            {
                Size textSize = TextRenderer.MeasureText(
                    e.Graphics,
                    displayText,
                    new Font("맑은 고딕", 10F, FontStyle.Bold),
                    new Size(this.Width, this.Height),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                );

                int iconX = Math.Min(this.Width - 12, 10 + textSize.Width + 8);
                int iconY = (this.Height - 4) / 2 + 1;
                Point[] points = new Point[]
                {
                    new Point(iconX, iconY),
                    new Point(iconX + 6, iconY),
                    new Point(iconX + 3, iconY + 4)
                };

                using (SolidBrush brush = new SolidBrush(color))
                {
                    e.Graphics.FillPolygon(brush, points);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaDashboardCard : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;
        public Color AccentColor = OviaFluentTheme.Accent;

        public OviaDashboardCard()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 14))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            using (SolidBrush accent = new SolidBrush(AccentColor))
            {
                e.Graphics.FillRectangle(accent, 0, 0, 5, this.Height);
            }

            base.OnPaint(e);
        }
    }

    public class OviaLargeCard : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;

        public OviaLargeCard()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 18))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaActionButton : Control
    {
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.Accent;

        private bool hover;

        public OviaActionButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color fillColor = hover ? OviaFluentTheme.AccentHover : StartColor;

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 8))
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 10.5F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public class OviaStatusLamp : Control
    {
        public bool IsActive = false;

        public OviaStatusLamp()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = OviaFluentTheme.AppBackground;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color mainColor = IsActive ? Color.FromArgb(25, 210, 115) : Color.FromArgb(230, 75, 75);
            Color glowColor = IsActive ? Color.FromArgb(80, 25, 210, 115) : Color.FromArgb(80, 230, 75, 75);

            Rectangle glowRect = new Rectangle(2, 2, this.Width - 4, this.Height - 4);
            Rectangle mainRect = new Rectangle(6, 6, this.Width - 12, this.Height - 12);
            Rectangle pointRect = new Rectangle(9, 9, this.Width - 18, this.Height - 18);

            using (SolidBrush glow = new SolidBrush(glowColor))
            {
                e.Graphics.FillEllipse(glow, glowRect);
            }

            using (SolidBrush main = new SolidBrush(mainColor))
            {
                e.Graphics.FillEllipse(main, mainRect);
            }

            using (SolidBrush point = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(point, pointRect);
            }

            using (Pen pen = new Pen(Color.FromArgb(180, Color.White), 1))
            {
                e.Graphics.DrawEllipse(pen, mainRect);
            }

            base.OnPaint(e);
        }
    }

    public class OviaSmallButton : Control
    {
        private bool hover;

        public OviaSmallButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color border = hover ? OviaFluentTheme.Accent : OviaFluentTheme.ControlBorder;
            Color text = hover ? OviaFluentTheme.Accent : OviaFluentTheme.TextSecondary;

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 6))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(border, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 9F, FontStyle.Bold),
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public static class MainDrawHelper
    {
        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;

            if (d > rect.Width)
            {
                d = rect.Width;
            }

            if (d > rect.Height)
            {
                d = rect.Height;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
