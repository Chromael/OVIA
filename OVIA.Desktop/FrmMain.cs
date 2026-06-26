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
using OVIA.Desktop.Controls;
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
        private Label lblEnvironmentValue;
        private Label lblEnvironmentNote;
        private Label lblLicenseValue;
        private Label lblLicenseNote;
        private Label lblRecentBarListValue;
        private Label lblRecentBarListNote;
        private Label lblProjectValue;
        private Label lblProjectNote;
        private Label lblDashboardDate;
        private Label lblLastRefresh;
        private OviaStatusBadge badgeAutoCad;
        private OviaStatusBadge badgeEnvironment;
        private OviaStatusBadge badgeLicense;
        private OviaStatusBadge badgeRecentBarList;
        private OviaStatusBadge badgeProject;
        private OviaDashboardBarChart chartProjectStatus;
        private OviaDashboardDonutChart chartBarListSave;
        private OviaDashboardLineChart chartProjectTrend;
        private Timer autoCadStatusTimer;
        private Timer workspaceStatusTimer;
        private ToolTip windowToolTip;
        private TableLayoutPanel mainLayout;
        private Panel workspacePanel;
        private Label bottomStatusLabel;
        private string bottomAutoCadStatusText = "AutoCAD 상태 확인 중";
        private Form currentScreen;
        private Form projectManagerForm;
        private FrmBarList barListForm;
        private FrmBarListMappingManager barListMappingForm;
        private bool logoutConfirmed;

        private readonly Color BrandIndigo = OviaFluentTheme.DashboardPrimaryDark;
        private readonly Color BrandViolet = OviaFluentTheme.DashboardPrimary;
        private readonly Color BrandCyan = OviaFluentTheme.DashboardPrimary;
        private readonly Color BrandOrange = OviaFluentTheme.DashboardPrimary;
        private readonly Color BrandGreen = OviaFluentTheme.DashboardPrimary;
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        public bool IsLogoutRequested { get; private set; }

        public string CurrentCompanyId
        {
            get { return companyId; }
        }

        public string CurrentUserId
        {
            get { return userId; }
        }

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
            this.Font = OviaFluentTheme.FontSystem(10F, FontStyle.Regular);
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
            bottomStatusLabel.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
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
            menu.Size = new Size(text.Length > 7 ? 142 : 100, 34);
            menu.Selected = selected;
            parent.Controls.Add(menu);

            return menu;
        }

        private void BuildDashboardMainContent(Control parent)
        {
            Panel content = new Panel();
            content.Location = new Point(0, 98);
            content.Size = new Size(Math.Max(1, parent.ClientSize.Width), Math.Max(1, parent.ClientSize.Height - 98));
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            content.AutoScroll = true;
            content.BackColor = SurfaceColor;
            parent.Controls.Add(content);

            BuildDashboardHero(content);
            BuildDashboardSummaryCards(content);
            BuildDashboardCharts(content);
            BuildDashboardDetailPanels(content);

            parent.Resize += delegate
            {
                content.Size = new Size(Math.Max(1, parent.ClientSize.Width), Math.Max(1, parent.ClientSize.Height - 98));
            };
        }

        private void BuildDashboardHero(Control parent)
        {
            // 상단 우측 날짜/새로고침 문구는 하단 상태바로 이동했다.
            lblDashboardDate = null;
            lblLastRefresh = null;
        }

        private void BuildDashboardSummaryCards(Control parent)
        {
            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Location = new Point(34, 48);
            cards.Size = new Size(Math.Max(1, parent.ClientSize.Width - 68), 208);
            cards.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cards.ColumnCount = 5;
            cards.RowCount = 1;
            cards.BackColor = SurfaceColor;
            cards.Margin = Padding.Empty;
            cards.Padding = Padding.Empty;
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            parent.Controls.Add(cards);

            OviaModernCard cadCard = CreateSummaryCard(
                "AutoCAD 상태",
                "AutoCAD 비활성",
                "AutoCAD 실행 후 도면 추출을 준비하세요.",
                "\uE71B",
                OviaFluentTheme.DashboardPrimary,
                "비활성",
                OviaStatusKind.Danger,
                out lblAutoCadValue,
                out lblAutoCadNote,
                out badgeAutoCad
            );
            cards.Controls.Add(cadCard, 0, 0);

            OviaModernCard envCard = CreateSummaryCard(
                "환경 점검",
                "점검 대기",
                "Windows / .NET / 권한을 확인합니다.",
                "\uE9D9",
                OviaFluentTheme.DashboardPrimary,
                "대기",
                OviaStatusKind.Neutral,
                out lblEnvironmentValue,
                out lblEnvironmentNote,
                out badgeEnvironment
            );
            cards.Controls.Add(envCard, 1, 0);

            OviaModernCard licenseCard = CreateSummaryCard(
                "라이선스 상태",
                GetLicenseStatusTitle(),
                GetLicenseStatusNote(),
                "\uE8D7",
                OviaFluentTheme.DashboardPrimary,
                GetLicenseBadgeText(),
                OviaStatusKind.Warning,
                out lblLicenseValue,
                out lblLicenseNote,
                out badgeLicense
            );
            cards.Controls.Add(licenseCard, 2, 0);

            OviaModernCard barListCard = CreateSummaryCard(
                "최근 BarList 작업",
                GetRecentBarListTitle(),
                GetRecentBarListNote(),
                "\uE8A5",
                OviaFluentTheme.DashboardPrimary,
                "작업",
                OviaStatusKind.Supported,
                out lblRecentBarListValue,
                out lblRecentBarListNote,
                out badgeRecentBarList
            );
            cards.Controls.Add(barListCard, 3, 0);

            OviaModernCard projectCard = CreateSummaryCard(
                "공사 현황",
                GetProjectStatusTitle(),
                GetProjectStatusNote(),
                "\uE90F",
                OviaFluentTheme.DashboardPrimary,
                "현황",
                OviaStatusKind.Warning,
                out lblProjectValue,
                out lblProjectNote,
                out badgeProject
            );
            cards.Controls.Add(projectCard, 4, 0);

            parent.Resize += delegate
            {
                cards.Width = Math.Max(1, parent.ClientSize.Width - 68);
            };
        }

        private OviaModernCard CreateSummaryCard(string title, string value, string note, string iconText, Color accentColor, string badgeText, OviaStatusKind badgeKind, out Label valueLabel, out Label noteLabel, out OviaStatusBadge badge)
        {
            OviaModernCard card = new OviaModernCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 14, 0);
            card.SurfaceColor = SurfaceColor;
            card.AccentColor = accentColor;
            card.HeaderDividerY = 62;
            card.BackColor = SurfaceColor;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.AutoSize = false;
            lblTitle.Size = new Size(220, 28);
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.Font = OviaFluentTheme.FontTitle(10.7F, FontStyle.Bold);
            lblTitle.ForeColor = TextDark;
            lblTitle.BackColor = Color.Transparent;
            lblTitle.Location = new Point(22, 14);
            card.Controls.Add(lblTitle);


            OviaLineIconBox icon = new OviaLineIconBox();
            icon.IconText = iconText;
            icon.IconColor = accentColor;
            icon.Location = new Point(22, 92);
            icon.Size = new Size(44, 44);
            card.Controls.Add(icon);

            Label localValueLabel = new Label();
            localValueLabel.Text = value;
            localValueLabel.AutoSize = false;
            localValueLabel.Size = new Size(170, 46);
            localValueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            localValueLabel.Font = OviaFluentTheme.FontTitle(14.2F, FontStyle.Bold);
            localValueLabel.TextAlign = ContentAlignment.MiddleLeft;
            localValueLabel.ForeColor = TextDark;
            localValueLabel.BackColor = Color.Transparent;
            localValueLabel.Location = new Point(78, 91);
            card.Controls.Add(localValueLabel);

            Label localNoteLabel = new Label();
            localNoteLabel.Text = note;
            localNoteLabel.AutoSize = false;
            localNoteLabel.Size = new Size(220, 44);
            localNoteLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            localNoteLabel.Font = OviaFluentTheme.FontSystem(9.3F, FontStyle.Regular);
            localNoteLabel.ForeColor = TextSub;
            localNoteLabel.BackColor = Color.Transparent;
            localNoteLabel.Location = new Point(22, 150);
            card.Controls.Add(localNoteLabel);

            card.Resize += delegate
            {
                lblTitle.Width = Math.Max(80, card.Width - 44);
                localValueLabel.Width = Math.Max(60, card.Width - 104);
                localNoteLabel.Width = Math.Max(80, card.Width - 44);
            };

            valueLabel = localValueLabel;
            noteLabel = localNoteLabel;
            badge = null;

            return card;
        }

        private void BuildDashboardCharts(Control parent)
        {
            TableLayoutPanel charts = new TableLayoutPanel();
            charts.Location = new Point(34, 286);
            charts.Size = new Size(Math.Max(1, parent.ClientSize.Width - 68), 250);
            charts.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            charts.ColumnCount = 3;
            charts.RowCount = 1;
            charts.BackColor = SurfaceColor;
            charts.Margin = Padding.Empty;
            charts.Padding = Padding.Empty;
            charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            parent.Controls.Add(charts);

            charts.Controls.Add(BuildProjectStatusChartPanel(), 0, 0);
            charts.Controls.Add(BuildBarListSaveChartPanel(), 1, 0);
            charts.Controls.Add(BuildProjectTrendChartPanel(), 2, 0);

            parent.Resize += delegate
            {
                charts.Width = Math.Max(1, parent.ClientSize.Width - 68);
            };
        }

        private Control BuildProjectStatusChartPanel()
        {
            OviaModernCard card = CreateChartCard("공사 상태 분석", "진행 / 완료 공사 현황");

            chartProjectStatus = new OviaDashboardBarChart();
            chartProjectStatus.Location = new Point(24, 116);
            chartProjectStatus.Size = new Size(340, 126);
            chartProjectStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chartProjectStatus.BackColor = Color.White;
            chartProjectStatus.Labels = new string[] { "진행", "완료", "보류" };
            chartProjectStatus.Values = GetProjectStatusChartValues();
            card.Controls.Add(chartProjectStatus);

            card.Resize += delegate
            {
                if (chartProjectStatus != null)
                {
                    chartProjectStatus.Width = Math.Max(160, card.Width - 48);
                    chartProjectStatus.Invalidate();
                }
            };

            return card;
        }

        private Control BuildBarListSaveChartPanel()
        {
            OviaModernCard card = CreateChartCard("BarList 저장 분포", "공사별 저장 여부");

            chartBarListSave = new OviaDashboardDonutChart();
            chartBarListSave.Location = new Point(22, 104);
            chartBarListSave.Size = new Size(340, 132);
            chartBarListSave.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chartBarListSave.BackColor = Color.White;
            chartBarListSave.Labels = new string[] { "저장 있음", "미저장" };
            chartBarListSave.Values = GetBarListSaveChartValues();
            card.Controls.Add(chartBarListSave);

            card.Resize += delegate
            {
                if (chartBarListSave != null)
                {
                    chartBarListSave.Width = Math.Max(160, card.Width - 44);
                    chartBarListSave.Invalidate();
                }
            };

            return card;
        }

        private Control BuildProjectTrendChartPanel()
        {
            OviaModernCard card = CreateChartCard("최근 공사 작업 추이", "최근 작업일 기준 월별 현황");

            chartProjectTrend = new OviaDashboardLineChart();
            chartProjectTrend.Location = new Point(24, 116);
            chartProjectTrend.Size = new Size(340, 126);
            chartProjectTrend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chartProjectTrend.BackColor = Color.White;
            chartProjectTrend.Labels = GetProjectTrendLabels();
            chartProjectTrend.Values = GetProjectTrendValues(chartProjectTrend.Labels);
            card.Controls.Add(chartProjectTrend);

            card.Resize += delegate
            {
                if (chartProjectTrend != null)
                {
                    chartProjectTrend.Width = Math.Max(160, card.Width - 48);
                    chartProjectTrend.Invalidate();
                }
            };

            return card;
        }

        private OviaModernCard CreateChartCard(string titleText, string subtitleText)
        {
            OviaModernCard card = new OviaModernCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 14, 0);
            card.SurfaceColor = SurfaceColor;
            card.AccentColor = Color.Transparent;
            card.HeaderDividerY = 82;
            card.BackColor = SurfaceColor;

            Label title = CreateCardTitle(titleText, 24, 13);
            card.Controls.Add(title);

            Label subtitle = CreateSmallText(subtitleText, 24, 45, 310, OviaFluentTheme.TextSecondary);
            subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(subtitle);

            card.Resize += delegate
            {
                subtitle.Width = Math.Max(80, card.Width - 48);
            };

            return card;
        }

        private OviaQuickActionTile CreateQuickAction(string title, string description, string iconText, Color iconColor)
        {
            OviaQuickActionTile tile = new OviaQuickActionTile();
            tile.Size = new Size(190, 56);
            tile.Margin = new Padding(0, 0, 14, 0);
            tile.TitleText = title;
            tile.DescriptionText = description;
            tile.IconText = iconText;
            tile.IconColor = iconColor;
            tile.BackColor = Color.White;
            return tile;
        }

        private void BuildDashboardDetailPanels(Control parent)
        {
            TableLayoutPanel detail = new TableLayoutPanel();
            detail.Location = new Point(34, 562);
            detail.Size = new Size(Math.Max(1, parent.ClientSize.Width - 68), 264);
            detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            detail.ColumnCount = 3;
            detail.RowCount = 1;
            detail.BackColor = SurfaceColor;
            detail.Margin = Padding.Empty;
            detail.Padding = Padding.Empty;
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            parent.Controls.Add(detail);

            detail.Controls.Add(BuildRecentWorkPanel(), 0, 0);
            detail.Controls.Add(BuildCheckRequiredPanel(), 1, 0);
            detail.Controls.Add(BuildNoticePanel(), 2, 0);

            parent.Resize += delegate
            {
                detail.Width = Math.Max(1, parent.ClientSize.Width - 68);
            };
        }

        private Control BuildRecentWorkPanel()
        {
            OviaModernCard card = CreateDetailCard("공사관리 최근 내역", "더보기 +", OpenProjectManager_Click);

            AddRowText(card, 24, 104, "상태", "공사명", "최근 작업일");

            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            projects.Sort(delegate (OviaProjectInfo a, OviaProjectInfo b)
            {
                DateTime ad;
                DateTime bd;
                DateTime.TryParse(a.LastWorkDate, out ad);
                DateTime.TryParse(b.LastWorkDate, out bd);
                return bd.CompareTo(ad);
            });

            int i;
            int y = 136;

            for (i = 0; i < projects.Count && i < 4; i++)
            {
                AddProjectWorkRow(card, 24, y, projects[i]);
                y += 30;
            }


            return card;
        }

        private void AddProjectWorkRow(Control parent, int x, int y, OviaProjectInfo project)
        {
            string status = project == null ? "-" : project.Status;
            string name = project == null ? "-" : TrimDashboardText(project.ProjectName, 18);
            string date = project == null ? "-" : project.LastWorkDate;

            Color statusColor = status == "완료" ? OviaFluentTheme.TextTertiary : OviaFluentTheme.DashboardPrimary;

            Label a = CreateSmallText(status, x, y, 72, statusColor);
            a.Font = OviaFluentTheme.FontData(9.2F, FontStyle.Bold);
            parent.Controls.Add(a);

            Label b = CreateSmallText(name, x + 88, y, 210, TextDark);
            parent.Controls.Add(b);

            Label c = CreateSmallText(date, x + 315, y, 120, TextSub);
            parent.Controls.Add(c);
        }

        private string TrimDashboardText(string text, int maxLength)
        {
            string value = text == null ? "" : text.Trim();

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private Control BuildCheckRequiredPanel()
        {
            OviaModernCard card = CreateDetailCard("주의 / 확인 필요", "더보기 +", DetectAutoCad_Click);

            AddAlertRow(card, 24, 94, OviaFluentTheme.Warning, "라이선스 인증", "현재는 개발/테스트 모드입니다.");
            AddAlertRow(card, 24, 138, OviaFluentTheme.Blue, "AutoCAD NETLOAD", "도면 추출 전 DLL 로드가 필요합니다.");
            AddAlertRow(card, 24, 182, OviaFluentTheme.Success, "작업 폴더", "로컬 OVIA 폴더 쓰기 권한 확인.");


            return card;
        }

        private Control BuildNoticePanel()
        {
            OviaModernCard card = CreateDetailCard("공지사항", "더보기 +", null);

            AddNoticeRow(card, 24, 92, BrandViolet, "OVIA v1.0.0 개발 진행", "2026-06-01");
            AddNoticeRow(card, 24, 126, OviaFluentTheme.Blue, "AutoCAD 2027 우선 지원", "2026-06-01");
            AddNoticeRow(card, 24, 160, BrandOrange, "BarList 매핑 안정화 진행", "2026-05-31");
            AddNoticeRow(card, 24, 194, OviaFluentTheme.Success, "환경 점검 도구 추가", "2026-05-31");


            return card;
        }

        private OviaModernCard CreateDetailCard(string titleText)
        {
            return CreateDetailCard(titleText, "더보기 +", null);
        }

        private OviaModernCard CreateDetailCard(string titleText, string moreText, EventHandler moreClick)
        {
            OviaModernCard card = new OviaModernCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 14, 0);
            card.SurfaceColor = SurfaceColor;
            card.AccentColor = Color.Transparent;
            card.HeaderDividerY = 70;
            card.BackColor = SurfaceColor;

            Label title = CreateCardTitle(titleText, 24, 13);
            card.Controls.Add(title);

            if (!string.IsNullOrEmpty(moreText))
            {
                Label more = CreateLinkLabel(moreText);
                more.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                more.Location = new Point(Math.Max(24, card.Width - 96), 19);
                more.TextAlign = ContentAlignment.MiddleRight;
                if (moreClick != null)
                {
                    more.Click += moreClick;
                }
                card.Controls.Add(more);

                card.Resize += delegate
                {
                    more.Location = new Point(Math.Max(24, card.Width - more.Width - 24), 19);
                };
            }

            return card;
        }

        private Label CreateCardTitle(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(260, 30);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontTitle(10.8F, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.BackColor = Color.Transparent;
            label.Location = new Point(x, y);
            return label;
        }

        private Label CreateLinkLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = OviaFluentTheme.FontButton(8.8F, FontStyle.Bold);
            label.ForeColor = OviaFluentTheme.Accent;
            label.BackColor = Color.Transparent;
            label.Cursor = Cursors.Hand;
            label.TextAlign = ContentAlignment.MiddleRight;
            return label;
        }

        private void AddRowText(Control parent, int x, int y, string col1, string col2, string col3)
        {
            Label a = CreateSmallText(col1, x, y, 78, OviaFluentTheme.TextTertiary);
            Label b = CreateSmallText(col2, x + 88, y, 190, TextDark);
            Label c = CreateSmallText(col3, x + 300, y, 160, TextSub);
            parent.Controls.Add(a);
            parent.Controls.Add(b);
            parent.Controls.Add(c);
        }

        private void AddAlertRow(Control parent, int x, int y, Color color, string title, string desc)
        {
            OviaDot dot = new OviaDot();
            dot.DotColor = color;
            dot.Location = new Point(x, y + 3);
            dot.Size = new Size(22, 22);
            parent.Controls.Add(dot);

            Label titleLabel = CreateSmallText(title, x + 32, y, 220, TextDark);
            titleLabel.Font = OviaFluentTheme.FontTitle(9.2F, FontStyle.Bold);
            parent.Controls.Add(titleLabel);

            Label descLabel = CreateSmallText(desc, x + 32, y + 24, 260, TextSub);
            parent.Controls.Add(descLabel);
        }

        private void AddNoticeRow(Control parent, int x, int y, Color color, string title, string date)
        {
            OviaDot dot = new OviaDot();
            dot.DotColor = OviaFluentTheme.TextMuted;
            dot.Location = new Point(x + 5, y + 12);
            dot.Size = new Size(5, 5);
            parent.Controls.Add(dot);

            Label titleLabel = CreateSmallText(title, x + 20, y, 210, TextDark);
            parent.Controls.Add(titleLabel);

            Label dateLabel = CreateSmallText(date, x + 235, y, 90, TextSub);
            dateLabel.TextAlign = ContentAlignment.MiddleRight;
            dateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            parent.Controls.Add(dateLabel);
        }

        private Label CreateSmallText(string text, int x, int y, int width, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(width, 28);
            label.Font = OviaFluentTheme.FontData(8.8F, FontStyle.Regular);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(x, y);
            return label;
        }

        private int[] GetProjectStatusChartValues()
        {
            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            int active = 0;
            int done = 0;
            int hold = 0;
            int i;

            for (i = 0; i < projects.Count; i++)
            {
                string status = projects[i].Status == null ? "" : projects[i].Status.Trim();

                if (status == "완료")
                {
                    done++;
                }
                else if (status == "보류")
                {
                    hold++;
                }
                else
                {
                    active++;
                }
            }

            return new int[] { active, done, hold };
        }

        private int[] GetBarListSaveChartValues()
        {
            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            int saved = 0;
            int empty = 0;
            int i;

            for (i = 0; i < projects.Count; i++)
            {
                try
                {
                    if (OviaLocalStore.GetBarListSummaries(projects[i]).Count > 0)
                    {
                        saved++;
                    }
                    else
                    {
                        empty++;
                    }
                }
                catch
                {
                    empty++;
                }
            }

            if (projects.Count == 0)
            {
                empty = 1;
            }

            return new int[] { saved, empty };
        }

        private string[] GetProjectTrendLabels()
        {
            DateTime now = DateTime.Now;
            string[] labels = new string[6];
            int i;

            for (i = 5; i >= 0; i--)
            {
                DateTime target = now.AddMonths(-i);
                labels[5 - i] = target.ToString("MM월");
            }

            return labels;
        }

        private int[] GetProjectTrendValues(string[] labels)
        {
            int[] values = new int[labels.Length];
            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            int i;
            int j;

            for (i = 0; i < projects.Count; i++)
            {
                DateTime date;

                if (!DateTime.TryParse(projects[i].LastWorkDate, out date))
                {
                    continue;
                }

                string label = date.ToString("MM월");

                for (j = 0; j < labels.Length; j++)
                {
                    if (labels[j] == label)
                    {
                        values[j]++;
                        break;
                    }
                }
            }

            return values;
        }

        private void RefreshDashboardCharts()
        {
            if (chartProjectStatus != null)
            {
                chartProjectStatus.Values = GetProjectStatusChartValues();
                chartProjectStatus.Invalidate();
            }

            if (chartBarListSave != null)
            {
                chartBarListSave.Values = GetBarListSaveChartValues();
                chartBarListSave.Invalidate();
            }

            if (chartProjectTrend != null)
            {
                chartProjectTrend.Labels = GetProjectTrendLabels();
                chartProjectTrend.Values = GetProjectTrendValues(chartProjectTrend.Labels);
                chartProjectTrend.Invalidate();
            }
        }

        private string GetLicenseStatusTitle()
        {
            if (IsSystemAdminUser())
            {
                return "관리자 라이선스";
            }

            return "개발 라이선스";
        }

        private string GetLicenseStatusNote()
        {
            return "로컬 개발/테스트 모드 · 정식 인증 연동 예정";
        }

        private string GetLicenseBadgeText()
        {
            if (IsSystemAdminUser())
            {
                return "관리자";
            }

            return "테스트";
        }

        private string GetRecentBarListTitle()
        {
            int count = CountSavedBarListFiles();

            if (count <= 0)
            {
                return "저장 0건";
            }

            return "저장 " + count.ToString() + "건";
        }

        private string GetRecentBarListNote()
        {
            string latest = GetLatestBarListFileName();

            if (latest == "")
            {
                return "검토 후 저장된 BarList가 아직 없습니다.";
            }

            return latest;
        }

        private string GetProjectStatusTitle()
        {
            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            return "전체 " + projects.Count.ToString() + "건";
        }

        private string GetProjectStatusNote()
        {
            List<OviaProjectInfo> projects = OviaLocalStore.GetSampleProjects();
            int active = 0;
            int done = 0;
            int i;

            for (i = 0; i < projects.Count; i++)
            {
                if (projects[i].Status == "완료")
                {
                    done++;
                }
                else
                {
                    active++;
                }
            }

            return "진행 " + active.ToString() + "건 · 완료 " + done.ToString() + "건";
        }

        private int CountSavedBarListFiles()
        {
            try
            {
                string baseDir = System.IO.Path.Combine(OviaLocalStore.GetBaseDirectory(), "Projects");

                if (!System.IO.Directory.Exists(baseDir))
                {
                    return 0;
                }

                return System.IO.Directory.GetFiles(baseDir, "*.csv", System.IO.SearchOption.AllDirectories).Length;
            }
            catch
            {
                return 0;
            }
        }

        private string GetLatestBarListFileName()
        {
            try
            {
                string baseDir = System.IO.Path.Combine(OviaLocalStore.GetBaseDirectory(), "Projects");

                if (!System.IO.Directory.Exists(baseDir))
                {
                    return "";
                }

                string[] files = System.IO.Directory.GetFiles(baseDir, "*.csv", System.IO.SearchOption.AllDirectories);
                string latestFile = "";
                DateTime latestTime = DateTime.MinValue;
                int i;

                for (i = 0; i < files.Length; i++)
                {
                    DateTime time = System.IO.File.GetLastWriteTime(files[i]);

                    if (time > latestTime)
                    {
                        latestTime = time;
                        latestFile = files[i];
                    }
                }

                if (latestFile == "")
                {
                    return "";
                }

                return System.IO.Path.GetFileName(latestFile);
            }
            catch
            {
                return "";
            }
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
            BuildDashboardMainContent(dashboard);
            UpdateBottomStatusWithRefresh();
            UpdateAutoCadRunStatus();
        }

        private void BuildDashboardExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인",
                null,
                null,
                delegate { ShowDashboard(); },
                delegate { RequestLogout(); },
                false,
                false
            );
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
            textBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
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
            button.Font = OviaIconFont.Create(9.5F, FontStyle.Regular);
            button.ForeColor = Color.Black;
            button.BackColor = SurfaceColor;
            button.TabStop = false;
            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, tip);
            }
            return button;
        }

        private void StyleExplorerLogoutButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 53, 69);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
            button.BackColor = SurfaceColor;
            button.ForeColor = Color.Black;

            button.MouseEnter += delegate
            {
                button.BackColor = Color.FromArgb(220, 53, 69);
                button.ForeColor = Color.White;
            };

            button.MouseLeave += delegate
            {
                button.BackColor = SurfaceColor;
                button.ForeColor = Color.Black;
            };

            button.SizeChanged += delegate
            {
                ApplyExplorerButtonRadius(button, 2);
            };

            ApplyExplorerButtonRadius(button, 2);
        }

        private void ApplyExplorerButtonRadius(Button button, int radius)
        {
            if (button == null || button.Width <= 0 || button.Height <= 0)
            {
                return;
            }

            int diameter = Math.Max(1, radius * 2);
            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            GraphicsPath path = new GraphicsPath();

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter - 1, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter - 1, rect.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();

            Region oldRegion = button.Region;
            button.Region = new Region(path);
            path.Dispose();

            if (oldRegion != null)
            {
                oldRegion.Dispose();
            }
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
            ShowAutoCadEnvironmentCheck();
        }

        public void ShowAutoCadEnvironmentCheck()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.Check();

            ApplyEnvironmentReportToDashboard(report);

            MessageBoxIcon icon = MessageBoxIcon.Information;

            if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                icon = MessageBoxIcon.Error;
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                icon = MessageBoxIcon.Warning;
            }

            MessageBox.Show(
                report.GetDisplayText(),
                "OVIA 설치 전 환경 점검 결과",
                MessageBoxButtons.OK,
                icon
            );
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

        private void OpenRebarUnitWeightTable_Click(object sender, EventArgs e)
        {
            NavigateToRebarUnitWeightTable();
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

        public void NavigateToRebarUnitWeightTable()
        {
            ShowWorkspaceScreen(new FrmRebarUnitWeightTable(companyId, userId), "OVIA 이형철근 단위중량표", "이형철근 단위중량표를 불러왔습니다.");
        }

        public void NavigateToSystemSettings()
        {
            if (!OviaSystemSettingsStore.IsSuperAdminUser(userId))
            {
                MessageBox.Show(
                    "시스템 설정은 최고관리자만 접근할 수 있습니다.\r\n\r\n현재 사용자 ID: " + userId,
                    "OVIA 권한 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            ShowWorkspaceScreen(new FrmSystemSettings(companyId, userId), "OVIA 시스템 설정", "시스템 설정을 불러왔습니다.");
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

        private void UpdateBottomStatusWithRefresh()
        {
            string today = DateTime.Now.ToString("yyyy년 M월 d일 dddd", new System.Globalization.CultureInfo("ko-KR"));
            string refresh = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string autoCadText = string.IsNullOrWhiteSpace(bottomAutoCadStatusText) ? "AutoCAD 상태 확인 중" : bottomAutoCadStatusText;
            SetBottomStatus("회사 ID : " + companyId + " / 사용자 ID : " + userId + " / 사용자명 : " + userId + " / 접속 IP : " + GetLocalIPAddress() + " / AutoCAD : " + autoCadText + " / " + today + " / 마지막 새로고침 : " + refresh);
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
            return OviaSystemSettingsStore.IsSuperAdminUser(userId) || value == "관리자";
        }

        private void ExtractReady_Click(object sender, EventArgs e)
        {
            ShowAutoCadExtractGuide();
        }

        public void ShowAutoCadExtractGuide()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();
            ApplyEnvironmentReportToDashboard(report);

            if (!report.IsCurrentDevelopmentAutoCadReady())
            {
                MessageBox.Show(
                    report.GetAutoCadExtractionBlockMessage() + "\r\n\r\n" + report.GetDisplayText(),
                    "OVIA AutoCAD 추출 준비",
                    MessageBoxButtons.OK,
                    report.OverallStatus == OviaEnvironmentStatus.Blocked ? MessageBoxIcon.Error : MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                report.GetAutoCadExtractionReadyMessage(),
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
            autoCadStatusTimer.Interval = 5000;
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
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();
            ApplyEnvironmentReportToDashboard(report);
        }

        private void ApplyEnvironmentReportToDashboard(OviaEnvironmentReport report)
        {
            if (report == null)
            {
                return;
            }

            OviaStatusKind autoCadKind = OviaStatusKind.Danger;
            string autoCadBadge = "비활성";

            if (report.IsCurrentDevelopmentAutoCadReady())
            {
                autoCadKind = OviaStatusKind.Supported;
                autoCadBadge = "활성";
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                autoCadKind = OviaStatusKind.Warning;
                autoCadBadge = "주의";
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                autoCadKind = OviaStatusKind.Danger;
                autoCadBadge = "차단";
            }

            bottomAutoCadStatusText = report.GetDesktopAutoCadStatusText();

            if (lblAutoCadValue != null)
            {
                lblAutoCadValue.Text = bottomAutoCadStatusText;
                lblAutoCadValue.ForeColor = GetStatusTextColor(autoCadKind);
            }

            if (lblAutoCadNote != null)
            {
                lblAutoCadNote.Text = report.GetDesktopAutoCadDetailText();
            }

            if (badgeAutoCad != null)
            {
                badgeAutoCad.BadgeText = autoCadBadge;
                badgeAutoCad.Kind = autoCadKind;
                badgeAutoCad.Invalidate();
            }

            OviaStatusKind envKind = OviaStatusKind.Supported;
            string envBadge = "정상";

            if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                envKind = OviaStatusKind.Warning;
                envBadge = "주의";
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                envKind = OviaStatusKind.Danger;
                envBadge = "차단";
            }

            if (lblEnvironmentValue != null)
            {
                lblEnvironmentValue.Text = GetEnvironmentDashboardStatusText(report);
                lblEnvironmentValue.ForeColor = GetStatusTextColor(envKind);
            }

            if (lblEnvironmentNote != null)
            {
                lblEnvironmentNote.Text = report.WindowsName + " · " + report.DotNetVersionText + " · 작업 폴더 " + (report.CanWriteOviaWorkFolder ? "쓰기 가능" : "쓰기 불가");
            }

            if (badgeEnvironment != null)
            {
                badgeEnvironment.BadgeText = envBadge;
                badgeEnvironment.Kind = envKind;
                badgeEnvironment.Invalidate();
            }

            if (lblLicenseValue != null)
            {
                lblLicenseValue.Text = GetLicenseStatusTitle();
            }

            if (lblLicenseNote != null)
            {
                lblLicenseNote.Text = GetLicenseStatusNote();
            }

            if (badgeLicense != null)
            {
                badgeLicense.BadgeText = GetLicenseBadgeText();
                badgeLicense.Kind = OviaStatusKind.Warning;
                badgeLicense.Invalidate();
            }

            if (lblRecentBarListValue != null)
            {
                lblRecentBarListValue.Text = GetRecentBarListTitle();
            }

            if (lblRecentBarListNote != null)
            {
                lblRecentBarListNote.Text = GetRecentBarListNote();
            }

            if (lblProjectValue != null)
            {
                lblProjectValue.Text = GetProjectStatusTitle();
            }

            if (lblProjectNote != null)
            {
                lblProjectNote.Text = GetProjectStatusNote();
            }

            UpdateBottomStatusWithRefresh();

            RefreshDashboardCharts();
        }

        private string GetEnvironmentDashboardStatusText(OviaEnvironmentReport report)
        {
            if (report == null)
            {
                return "점검 대기";
            }

            if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                return "지원 불가";
            }

            if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                return "제한지원";
            }

            return "정상지원가능";
        }

        private Color GetStatusTextColor(OviaStatusKind kind)
        {
            if (kind == OviaStatusKind.Supported)
            {
                return OviaFluentTheme.DashboardPrimary;
            }

            if (kind == OviaStatusKind.Warning)
            {
                return Color.FromArgb(176, 111, 0);
            }

            if (kind == OviaStatusKind.Danger)
            {
                return OviaFluentTheme.Danger;
            }

            return TextDark;
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

            if (!IsDisplayableAutoCadProductName(productName))
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

                    if (IsDisplayableAutoCadProductName(displayName))
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
                    if (IsSameAutoCadInstall(list[i], list[j]))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool IsSameAutoCadInstall(AutoCadInstallInfo a, AutoCadInstallInfo b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.Year != b.Year)
            {
                return false;
            }

            if (a.IsLT != b.IsLT)
            {
                return false;
            }

            if (string.Equals(a.ProductName, b.ProductName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (NormalizePath(a.InstallPath) != "" && string.Equals(NormalizePath(a.InstallPath), NormalizePath(b.InstallPath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (NormalizeAutoCadProductName(a.ProductName) != "" && string.Equals(NormalizeAutoCadProductName(a.ProductName), NormalizeAutoCadProductName(b.ProductName), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (path == null)
            {
                return "";
            }

            return path.Trim().TrimEnd('\\', '/');
        }

        private static string NormalizeAutoCadProductName(string productName)
        {
            if (productName == null)
            {
                return "";
            }

            return productName
                .Replace("Autodesk", "")
                .Replace("autodesk", "")
                .Trim();
        }

        private static bool IsDisplayableAutoCadProductName(string productName)
        {
            if (productName == null)
            {
                return false;
            }

            string name = productName.Trim();

            if (name.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (name.IndexOf("MCP Server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Open in Desktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Open Desktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Desktop Connector", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
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

    public static class OviaIconFont
    {
        private static string cachedFamilyName;

        public static Font Create(float size, FontStyle style)
        {
            return new Font(GetFamilyName(), size, style);
        }

        public static string GetFamilyName()
        {
            if (!string.IsNullOrEmpty(cachedFamilyName))
            {
                return cachedFamilyName;
            }

            string[] candidates = new string[]
            {
                "Segoe Fluent Icons",
                "Segoe MDL2 Assets"
            };

            try
            {
                FontFamily[] families = FontFamily.Families;
                int i;
                int j;

                for (i = 0; i < candidates.Length; i++)
                {
                    for (j = 0; j < families.Length; j++)
                    {
                        if (string.Equals(families[j].Name, candidates[i], StringComparison.OrdinalIgnoreCase))
                        {
                            cachedFamilyName = families[j].Name;
                            return cachedFamilyName;
                        }
                    }
                }
            }
            catch
            {
            }

            cachedFamilyName = "Segoe MDL2 Assets";
            return cachedFamilyName;
        }
    }

    public class OviaMenuButton : Control
    {
        public bool Selected = false;
        public string IconText = "";
        private bool hover = false;

        public OviaMenuButton()
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
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 2, this.Width - 3, this.Height - 5);

            if (Selected || hover)
            {
                Color fillColor = Selected ? OviaFluentTheme.NavigationSelected : OviaFluentTheme.NavigationHover;
                using (GraphicsPath path = MainDrawHelper.RoundRect(rect, OviaFluentTheme.MenuRadius))
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            Color color = Selected ? OviaFluentTheme.NavigationTextActive : OviaFluentTheme.NavigationText;
            bool hasDropDownIcon = this.Text != null && this.Text.IndexOf("", StringComparison.Ordinal) >= 0;
            string displayText = hasDropDownIcon ? this.Text.Replace("", "").TrimEnd() : this.Text;
            string iconText = IconText == null ? "" : IconText;

            using (Font textFont = OviaFluentTheme.FontButton(10.4F, FontStyle.Bold))
            using (Font iconFont = OviaIconFont.Create(13.2F, FontStyle.Regular))
            {
                Size textSize = TextRenderer.MeasureText(
                    e.Graphics,
                    displayText,
                    textFont,
                    new Size(this.Width, this.Height),
                    TextFormatFlags.SingleLine
                );

                Size iconSize = string.IsNullOrEmpty(iconText)
                    ? Size.Empty
                    : new Size(20, 20);

                int dropWidth = hasDropDownIcon ? 12 : 0;
                int gap = string.IsNullOrEmpty(iconText) ? 0 : 7;
                int totalWidth = iconSize.Width + gap + textSize.Width + dropWidth;
                int x = Math.Max(10, (this.Width - totalWidth) / 2);

                if (!string.IsNullOrEmpty(iconText))
                {
                    Rectangle iconRect = new Rectangle(x, 0, iconSize.Width + 6, this.Height);
                    TextRenderer.DrawText(
                        e.Graphics,
                        iconText,
                        iconFont,
                        iconRect,
                        color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    );
                    x += iconSize.Width + gap;
                }

                Rectangle textRect = new Rectangle(x, 0, Math.Max(1, this.Width - x - dropWidth), this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    displayText,
                    textFont,
                    textRect,
                    color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                );

                if (hasDropDownIcon)
                {
                    int iconX = Math.Min(this.Width - 14, x + Math.Min(textSize.Width, textRect.Width - 8) + 6);
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
            }

            base.OnPaint(e);
        }
    }

    public enum OviaStatusKind
    {
        Supported,
        Warning,
        Danger,
        Neutral
    }

    public class OviaModernCard : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;
        public Color AccentColor = Color.Transparent;
        public int HeaderDividerY = 50;

        public OviaModernCard()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;
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
            e.Graphics.SmoothingMode = SmoothingMode.None;

            Rectangle rect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));

            using (SolidBrush fill = new SolidBrush(Color.White))
            {
                e.Graphics.FillRectangle(fill, rect);
            }

            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, rect);
            }

            if (HeaderDividerY > 0 && HeaderDividerY < rect.Height)
            {
                using (Pen divider = new Pen(OviaFluentTheme.CardBorder, 1))
                {
                    e.Graphics.DrawLine(divider, rect.Left + 1, HeaderDividerY, rect.Right - 1, HeaderDividerY);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaStatusBadge : Control
    {
        public string BadgeText = "상태";
        public OviaStatusKind Kind = OviaStatusKind.Neutral;

        public OviaStatusBadge()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color back;
            Color border;
            Color text;

            if (Kind == OviaStatusKind.Supported)
            {
                back = OviaFluentTheme.SuccessLight;
                border = Color.FromArgb(134, 239, 172);
                text = OviaFluentTheme.Success;
            }
            else if (Kind == OviaStatusKind.Warning)
            {
                back = OviaFluentTheme.WarningLight;
                border = Color.FromArgb(253, 186, 116);
                text = Color.FromArgb(176, 111, 0);
            }
            else if (Kind == OviaStatusKind.Danger)
            {
                back = OviaFluentTheme.DangerLight;
                border = Color.FromArgb(252, 165, 165);
                text = OviaFluentTheme.Danger;
            }
            else
            {
                back = OviaFluentTheme.NeutralLight;
                border = Color.FromArgb(209, 213, 219);
                text = OviaFluentTheme.TextSecondary;
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, OviaFluentTheme.PillRadius))
            {
                using (SolidBrush fill = new SolidBrush(back))
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
                BadgeText,
                OviaFluentTheme.FontButton(8F, FontStyle.Bold),
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );

            base.OnPaint(e);
        }
    }

    public class OviaLineIconBox : Control
    {
        public string IconText = "\uE8A5";
        public Color IconColor = OviaFluentTheme.Accent;

        public OviaLineIconBox()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 12))
            {
                using (SolidBrush fill = new SolidBrush(GetSoftColor(IconColor)))
                {
                    e.Graphics.FillPath(fill, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                IconText,
                OviaIconFont.Create(17F, FontStyle.Regular),
                rect,
                IconColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }

        private Color GetSoftColor(Color color)
        {
            return Color.FromArgb(24, color.R, color.G, color.B);
        }
    }

    public class OviaQuickActionTile : Control
    {
        public string TitleText = "";
        public string DescriptionText = "";
        public string IconText = "\uE8A5";
        public Color IconColor = OviaFluentTheme.Accent;

        private bool hover;

        public OviaQuickActionTile()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
            this.BackColor = Color.White;
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
            Color border = hover ? OviaFluentTheme.Accent : OviaFluentTheme.CardBorder;
            Color fill = hover ? OviaFluentTheme.AccentSoft : Color.White;

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 10))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(brush, path);
                }

                using (Pen pen = new Pen(border, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            Rectangle iconRect = new Rectangle(12, 12, 32, 32);
            using (GraphicsPath iconPath = MainDrawHelper.RoundRect(iconRect, 9))
            {
                using (SolidBrush iconFill = new SolidBrush(Color.FromArgb(22, IconColor.R, IconColor.G, IconColor.B)))
                {
                    e.Graphics.FillPath(iconFill, iconPath);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                IconText,
                OviaIconFont.Create(14F, FontStyle.Regular),
                iconRect,
                IconColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            TextRenderer.DrawText(
                e.Graphics,
                TitleText,
                OviaFluentTheme.FontTitle(9F, FontStyle.Bold),
                new Rectangle(54, 10, this.Width - 64, 22),
                OviaFluentTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );

            TextRenderer.DrawText(
                e.Graphics,
                DescriptionText,
                OviaFluentTheme.FontData(9F, FontStyle.Regular),
                new Rectangle(54, 31, this.Width - 64, 20),
                OviaFluentTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );

            base.OnPaint(e);
        }
    }

    public class OviaDot : Control
    {
        public Color DotColor = OviaFluentTheme.Accent;

        public OviaDot()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (this.Width <= 8 || this.Height <= 8)
            {
                Rectangle tiny = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));
                using (SolidBrush main = new SolidBrush(DotColor))
                {
                    e.Graphics.FillEllipse(main, tiny);
                }

                base.OnPaint(e);
                return;
            }

            Rectangle outer = new Rectangle(1, 1, this.Width - 2, this.Height - 2);
            Rectangle inner = new Rectangle(6, 6, Math.Max(1, this.Width - 12), Math.Max(1, this.Height - 12));

            using (SolidBrush soft = new SolidBrush(Color.FromArgb(35, DotColor.R, DotColor.G, DotColor.B)))
            {
                e.Graphics.FillEllipse(soft, outer);
            }

            using (SolidBrush main = new SolidBrush(DotColor))
            {
                e.Graphics.FillEllipse(main, inner);
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
                OviaFluentTheme.FontTitle(10.5F, FontStyle.Bold),
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
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color mainColor = IsActive ? OviaFluentTheme.Success : OviaFluentTheme.Danger;
            Color softColor = Color.FromArgb(22, mainColor.R, mainColor.G, mainColor.B);
            Rectangle rect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 7))
            using (SolidBrush fill = new SolidBrush(softColor))
            {
                e.Graphics.FillPath(fill, path);
            }

            using (Font iconFont = OviaIconFont.Create(13.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "\uE71B",
                    iconFont,
                    rect,
                    mainColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                );
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
                OviaFluentTheme.FontTitle(9F, FontStyle.Bold),
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public class OviaDashboardBarChart : Control
    {
        public string[] Labels = new string[0];
        public int[] Values = new int[0];

        public OviaDashboardBarChart()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            Rectangle chartRect = new Rectangle(12, 12, Math.Max(1, this.Width - 24), Math.Max(1, this.Height - 38));
            int max = 1;
            int i;

            for (i = 0; i < Values.Length; i++)
            {
                if (Values[i] > max)
                {
                    max = Values[i];
                }
            }

            using (Pen gridPen = new Pen(Color.FromArgb(235, 239, 245), 1))
            {
                e.Graphics.DrawLine(gridPen, chartRect.Left, chartRect.Bottom, chartRect.Right, chartRect.Bottom);
                e.Graphics.DrawLine(gridPen, chartRect.Left, chartRect.Top + chartRect.Height / 2, chartRect.Right, chartRect.Top + chartRect.Height / 2);
            }

            if (Values.Length <= 0)
            {
                DrawEmpty(e.Graphics, chartRect);
                return;
            }

            int count = Values.Length;
            int gap = 18;
            int barWidth = Math.Max(22, (chartRect.Width - gap * (count + 1)) / count);
            Color[] colors = OviaFluentTheme.ChartPalette();

            for (i = 0; i < count; i++)
            {
                int value = Values[i];
                int barHeight = (int)Math.Round((double)value / (double)max * (double)(chartRect.Height - 18));
                int x = chartRect.Left + gap + i * (barWidth + gap);
                int y = chartRect.Bottom - barHeight;
                Rectangle barRect = new Rectangle(x, y, barWidth, Math.Max(2, barHeight));

                using (GraphicsPath path = MainDrawHelper.RoundRect(barRect, 6))
                {
                    using (SolidBrush brush = new SolidBrush(colors[i % colors.Length]))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    value.ToString(),
                    OviaFluentTheme.FontUI(8.5F, FontStyle.Bold),
                    new Rectangle(x - 10, y - 22, barWidth + 20, 18),
                    OviaFluentTheme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );

                string label = i < Labels.Length ? Labels[i] : "";
                TextRenderer.DrawText(
                    e.Graphics,
                    label,
                    OviaFluentTheme.FontData(9F, FontStyle.Regular),
                    new Rectangle(x - 12, chartRect.Bottom + 6, barWidth + 24, 20),
                    OviaFluentTheme.TextTertiary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                );
            }

            base.OnPaint(e);
        }

        private void DrawEmpty(Graphics g, Rectangle rect)
        {
            TextRenderer.DrawText(
                g,
                "표시할 데이터가 없습니다.",
                OviaFluentTheme.FontData(9.3F, FontStyle.Regular),
                rect,
                OviaFluentTheme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
    }

    public class OviaDashboardDonutChart : Control
    {
        public string[] Labels = new string[0];
        public int[] Values = new int[0];

        public OviaDashboardDonutChart()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            int size = Math.Min(116, Math.Max(70, this.Height - 28));
            Rectangle pieRect = new Rectangle(16, 12, size, size);
            int total = 0;
            int i;

            for (i = 0; i < Values.Length; i++)
            {
                if (Values[i] > 0)
                {
                    total += Values[i];
                }
            }

            if (total <= 0)
            {
                total = 1;
            }

            Color[] colors = OviaFluentTheme.ChartPalette();
            float start = -90F;

            for (i = 0; i < Values.Length; i++)
            {
                int value = Values[i] < 0 ? 0 : Values[i];
                float sweep = 360F * (float)value / (float)total;

                using (SolidBrush brush = new SolidBrush(colors[i % colors.Length]))
                {
                    e.Graphics.FillPie(brush, pieRect, start, sweep);
                }

                start += sweep;
            }

            int inner = Math.Max(36, size / 2);
            Rectangle innerRect = new Rectangle(pieRect.Left + (size - inner) / 2, pieRect.Top + (size - inner) / 2, inner, inner);

            using (SolidBrush fill = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(fill, innerRect);
            }

            TextRenderer.DrawText(
                e.Graphics,
                total.ToString(),
                OviaFluentTheme.FontUI(15F, FontStyle.Bold),
                innerRect,
                OviaFluentTheme.DashboardPrimaryDark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            int legendX = pieRect.Right + 28;
            int legendY = 22;

            for (i = 0; i < Values.Length; i++)
            {
                int value = Values[i] < 0 ? 0 : Values[i];
                string label = i < Labels.Length ? Labels[i] : "항목";
                Rectangle dot = new Rectangle(legendX, legendY + i * 34 + 4, 10, 10);

                using (SolidBrush brush = new SolidBrush(colors[i % colors.Length]))
                {
                    e.Graphics.FillEllipse(brush, dot);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    label,
                    OviaFluentTheme.FontData(9.3F, FontStyle.Regular),
                    new Rectangle(legendX + 18, legendY + i * 34, Math.Max(60, this.Width - legendX - 26), 18),
                    OviaFluentTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                );

                TextRenderer.DrawText(
                    e.Graphics,
                    value.ToString() + "건",
                    OviaFluentTheme.FontData(9.5F, FontStyle.Bold),
                    new Rectangle(legendX + 18, legendY + i * 34 + 17, Math.Max(60, this.Width - legendX - 26), 18),
                    OviaFluentTheme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                );
            }

            base.OnPaint(e);
        }
    }

    public class OviaDashboardLineChart : Control
    {
        public string[] Labels = new string[0];
        public int[] Values = new int[0];

        public OviaDashboardLineChart()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            Rectangle chartRect = new Rectangle(12, 10, Math.Max(1, this.Width - 24), Math.Max(1, this.Height - 36));
            int max = 1;
            int i;

            for (i = 0; i < Values.Length; i++)
            {
                if (Values[i] > max)
                {
                    max = Values[i];
                }
            }

            using (Pen gridPen = new Pen(Color.FromArgb(235, 239, 245), 1))
            {
                e.Graphics.DrawLine(gridPen, chartRect.Left, chartRect.Bottom, chartRect.Right, chartRect.Bottom);
                e.Graphics.DrawLine(gridPen, chartRect.Left, chartRect.Top + chartRect.Height / 2, chartRect.Right, chartRect.Top + chartRect.Height / 2);
                e.Graphics.DrawLine(gridPen, chartRect.Left, chartRect.Top, chartRect.Right, chartRect.Top);
            }

            if (Values.Length <= 0)
            {
                return;
            }

            PointF[] points = new PointF[Values.Length];
            int count = Values.Length;

            for (i = 0; i < count; i++)
            {
                float x = count == 1 ? chartRect.Left + chartRect.Width / 2F : chartRect.Left + (float)i * chartRect.Width / (float)(count - 1);
                float y = chartRect.Bottom - ((float)Values[i] / (float)max * (chartRect.Height - 14));
                points[i] = new PointF(x, y);
            }

            if (points.Length >= 2)
            {
                using (Pen linePen = new Pen(OviaFluentTheme.DashboardPrimary, 3))
                {
                    linePen.StartCap = LineCap.Round;
                    linePen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(linePen, points);
                }
            }

            for (i = 0; i < points.Length; i++)
            {
                RectangleF dot = new RectangleF(points[i].X - 4, points[i].Y - 4, 8, 8);
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    e.Graphics.FillEllipse(brush, dot);
                }
                using (Pen pen = new Pen(OviaFluentTheme.DashboardPrimary, 2))
                {
                    e.Graphics.DrawEllipse(pen, dot.X, dot.Y, dot.Width, dot.Height);
                }

                string label = i < Labels.Length ? Labels[i] : "";
                TextRenderer.DrawText(
                    e.Graphics,
                    label,
                    OviaFluentTheme.FontData(9F, FontStyle.Regular),
                    new Rectangle((int)points[i].X - 24, chartRect.Bottom + 7, 48, 18),
                    OviaFluentTheme.TextTertiary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                );
            }

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
