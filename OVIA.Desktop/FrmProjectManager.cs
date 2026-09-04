using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmProjectManager : Form, IOviaWorkspaceLayout
    {
        private readonly string companyId;
        private readonly string userId;

        private OviaSearchBox txtSearch;
        private OviaCheckBox chkIncludeDone;
        private DataGridView grid;
        private Label lblStatus;
        private Panel sessionInfoPanel;
        private Label lblSessionInfo;
        private Label lblAutoCadInfo;
        private AutoCadRuntimeInfo currentAutoCadRuntimeInfo;
        private OviaWorkspaceHeader workspaceHeader;
        private Panel pagerPanel;
        private ToolTip windowToolTip;
        private ContextMenuStrip supportInfoContextMenu;

        private string currentSessionCompanyId = "";
        private string currentSessionUserId = "";
        private string currentSessionUserName = "";
        private string currentSessionIpAddress = "";

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
        private readonly Color BrandCyan = Color.FromArgb(64, 156, 255);
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private List<OviaProjectRow> allProjects = new List<OviaProjectRow>();
        private List<OviaProjectRow> currentProjects = new List<OviaProjectRow>();
        private int pageSize = 100;
        private int currentPage = 1;
        private string headerSortColumn = "";
        private bool headerSortAscending = true;

        private const int BaseClientWidth = 1180;
        private const int BaseClientHeight = 720;
        private const int MinFormWidth = 1100;
        private const int MinFormHeight = 750;
        private Panel scrollPanel;
        private Panel contentPanel;
        private OviaContentLoadingOverlay contentLoadingOverlay;
        private bool isScrollResetQueued = false;
        private bool isErpProjectListLoading = false;

        public FrmProjectManager(string companyId, string userId)
        {
            this.companyId = companyId;
            this.userId = userId;
            currentSessionCompanyId = companyId == null ? "" : companyId.Trim();
            currentSessionUserId = userId == null ? "" : userId.Trim();

            BuildUI();
            this.Shown += FrmProjectManager_Shown;
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA 공사목록";
            this.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(BaseClientWidth, BaseClientHeight);
            this.MinimumSize = new Size(MinFormWidth, MinFormHeight);
            this.BackColor = SurfaceColor;

            windowToolTip = new ToolTip();
            windowToolTip.AutoPopDelay = 4000;
            windowToolTip.InitialDelay = 350;
            windowToolTip.ReshowDelay = 100;
            windowToolTip.ShowAlways = true;

            scrollPanel = new Panel();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.BackColor = SurfaceColor;
            scrollPanel.AutoScroll = true;
            scrollPanel.AutoScrollMinSize = new Size(0, BaseClientHeight);
            scrollPanel.HorizontalScroll.Enabled = false;
            scrollPanel.HorizontalScroll.Visible = false;
            scrollPanel.Resize += ScrollPanel_Resize;
            this.Controls.Add(scrollPanel);

            contentPanel = new Panel();
            contentPanel.Location = new Point(0, 0);
            contentPanel.Size = new Size(BaseClientWidth, BaseClientHeight);
            contentPanel.BackColor = SurfaceColor;
            scrollPanel.Controls.Add(contentPanel);

            BuildHeader(contentPanel);
            BuildCommandBar(contentPanel);
            BuildSearchArea(contentPanel);
            BuildProjectGrid(contentPanel);
            BuildFooter(contentPanel);
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            BuildContentLoadingOverlay();

            this.ResumeLayout(false);
        }


        private void BuildContentLoadingOverlay()
        {
            contentLoadingOverlay = new OviaContentLoadingOverlay();
            this.Controls.Add(contentLoadingOverlay);
            contentLoadingOverlay.BringToFront();
        }

        private void BeginContentLoading()
        {
            if (contentLoadingOverlay != null)
            {
                contentLoadingOverlay.BeginLoading();
            }
        }

        private void EndContentLoading()
        {
            if (contentLoadingOverlay != null)
            {
                contentLoadingOverlay.EndLoading();
            }
        }

        private void ScrollPanel_Resize(object sender, EventArgs e)
        {
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            QueueResetScrollToTopLeft();
        }

        private void UpdateScrollableContentSize()
        {
            if (scrollPanel == null || contentPanel == null || scrollPanel.IsDisposed || contentPanel.IsDisposed)
            {
                return;
            }

            bool needVerticalScroll = this.ClientSize.Height < BaseClientHeight;

            scrollPanel.SuspendLayout();

            try
            {
                if (needVerticalScroll)
                {
                    scrollPanel.AutoScroll = true;
                    scrollPanel.AutoScrollMinSize = new Size(0, BaseClientHeight);
                    scrollPanel.HorizontalScroll.Enabled = false;
                    scrollPanel.HorizontalScroll.Visible = false;

                    int width = Math.Max(1, scrollPanel.ClientSize.Width);
                    int height = Math.Max(BaseClientHeight, scrollPanel.ClientSize.Height);

                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(width, height);
                }
                else
                {
                    scrollPanel.AutoScroll = false;
                    scrollPanel.AutoScrollMinSize = Size.Empty;
                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(Math.Max(1, scrollPanel.ClientSize.Width), Math.Max(1, scrollPanel.ClientSize.Height));
                }
            }
            finally
            {
                scrollPanel.ResumeLayout(false);
            }
        }

        private void QueueResetScrollToTopLeft()
        {
            if (isScrollResetQueued || this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            isScrollResetQueued = true;

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    isScrollResetQueued = false;
                    ResetScrollToTopLeft();
                }));
            }
            catch
            {
                isScrollResetQueued = false;
            }
        }

        private void ResetScrollToTopLeft()
        {
            if (scrollPanel == null || contentPanel == null || scrollPanel.IsDisposed || contentPanel.IsDisposed)
            {
                return;
            }

            scrollPanel.SuspendLayout();

            try
            {
                scrollPanel.AutoScrollPosition = new Point(0, 0);
                contentPanel.Location = new Point(0, 0);
            }
            finally
            {
                scrollPanel.ResumeLayout(false);
            }
        }

        public void ApplyWorkspaceLayout()
        {
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            QueueResetScrollToTopLeft();
        }

        private void BuildHeader(Control parent)
        {
            BuildExplorerHeader(parent, "메인  ›  공사관리  ›  공사목록");
        }

        private void BuildExplorerHeader(Control parent, string pathText)
        {
            workspaceHeader = OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { NavigateToMain(); },
                delegate { NavigateToMain(); },
                delegate { RefreshProjectListFromInitialSort(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    NavigateByWorkspacePath(target);
                }
            );

            if (workspaceHeader != null)
            {
                workspaceHeader.AutoCadRuntimeStatusChanged += WorkspaceHeader_AutoCadRuntimeStatusChanged;
            }
        }

        private void WorkspaceHeader_AutoCadRuntimeStatusChanged(object sender, OviaAutoCadRuntimeStatusChangedEventArgs e)
        {
            AutoCadRuntimeInfo runtimeInfo = e != null && e.IsRunning ? e.RuntimeInfo : null;
            RefreshSessionInfoLabel(runtimeInfo);
        }

        private async void FrmProjectManager_Shown(object sender, EventArgs e)
        {
            this.Shown -= FrmProjectManager_Shown;
            await LoadProjectsFromErpAsync(true);
        }

        private async void RefreshProjectListFromInitialSort()
        {
            headerSortColumn = "";
            headerSortAscending = true;
            currentPage = 1;
            await LoadProjectsFromErpAsync(true);
        }

        private bool CanUpdateProjectGrid()
        {
            return !this.IsDisposed
                && !this.Disposing
                && grid != null
                && !grid.IsDisposed
                && grid.ColumnCount > 0;
        }

        private async System.Threading.Tasks.Task LoadProjectsFromErpAsync(bool showErrorMessage)
        {
            if (isErpProjectListLoading || !CanUpdateProjectGrid())
            {
                return;
            }

            isErpProjectListLoading = true;
            BeginContentLoading();

            try
            {
                if (lblStatus != null)
                {
                    lblStatus.Text = "ERP 공사목록을 불러오는 중입니다.";
                }

                OviaErpProjectListResult result = await OviaErpApiService.GetProjectListAsync(companyId);

                // ERP Deep Link로 공사목록 화면을 벗어난 동안 비동기 응답이 늦게 돌아올 수 있습니다.
                // 이미 Dispose된 화면의 DataGridView는 컬럼이 제거되므로 Rows.Add를 수행하면
                // "열이 없는 DataGridView 컨트롤에는 행을 추가할 수 없습니다." 예외가 발생합니다.
                if (!CanUpdateProjectGrid())
                {
                    return;
                }

                if (result != null && result.IsSuccess)
                {
                    allProjects = result.Projects ?? new List<OviaProjectRow>();
                    UpdateSessionInfo(
                        result.SessionCompanyId,
                        result.SessionUserId,
                        result.SessionUserName,
                        result.SessionIpAddress
                    );
                    currentPage = 1;
                    BindProjects();
                    return;
                }

                BindProjects();

                string message = result == null || string.IsNullOrWhiteSpace(result.Message)
                    ? "ERP 공사목록을 불러오지 못했습니다."
                    : result.Message;

                if (lblStatus != null)
                {
                    lblStatus.Text = "ERP 공사목록 조회 실패";
                }

                if (showErrorMessage)
                {
                    MessageBox.Show(
                        message,
                        "OVIA",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            finally
            {
                isErpProjectListLoading = false;

                if (!this.IsDisposed && !this.Disposing)
                {
                    EndContentLoading();
                }
            }
        }

        private void NavigateByWorkspacePath(string target)
        {
            if (target == "MAIN")
            {
                NavigateToMain();
            }
        }


        private Panel CreatePathAddressBar(string pathText)
        {
            Panel panel = new Panel();
            panel.BackColor = Color.White;
            panel.Margin = Padding.Empty;
            panel.Padding = new Padding(10, 6, 10, 0);

            TextBox textBox = null;
            LinkLabel breadcrumb = CreateBreadcrumbLabel();
            breadcrumb.Text = pathText == null ? "" : pathText;
            breadcrumb.Location = new Point(10, 6);
            breadcrumb.Size = new Size(880, 22);
            breadcrumb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            breadcrumb.Links.Add(0, "메인".Length, "MAIN");
            breadcrumb.LinkClicked += Breadcrumb_LinkClicked;
            breadcrumb.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (IsPathBlankAreaClick(breadcrumb, e))
                {
                    ShowPathEditMode(breadcrumb, textBox);
                }
            };
            panel.Controls.Add(breadcrumb);

            textBox = new TextBox();
            textBox.Text = NormalizeCopyPath(pathText);
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            textBox.ForeColor = Color.Black;
            textBox.BackColor = Color.White;
            textBox.Location = new Point(10, 7);
            textBox.Size = new Size(880, 20);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = Padding.Empty;
            textBox.TabStop = false;
            textBox.Visible = false;
            textBox.Click += delegate { textBox.SelectAll(); };
            textBox.Enter += delegate { textBox.SelectAll(); };
            textBox.Leave += delegate { HidePathEditMode(breadcrumb, textBox); };
            textBox.KeyDown += PathCopy_KeyDown;
            panel.Controls.Add(textBox);

            return panel;
        }

        private bool IsPathBlankAreaClick(LinkLabel breadcrumb, MouseEventArgs e)
        {
            if (breadcrumb == null || e == null || e.Button != MouseButtons.Left)
            {
                return false;
            }

            int textWidth = TextRenderer.MeasureText(
                breadcrumb.Text,
                breadcrumb.Font,
                new Size(int.MaxValue, breadcrumb.Height),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
            ).Width;

            return e.X > textWidth + 8;
        }

        private string NormalizeCopyPath(string pathText)
        {
            return pathText == null ? "" : pathText.Replace("  ›  ", "\\");
        }

        private void ShowPathEditMode(LinkLabel breadcrumb, TextBox textBox)
        {
            if (breadcrumb != null)
            {
                breadcrumb.Visible = false;
            }

            if (textBox != null)
            {
                textBox.Visible = true;
                textBox.BringToFront();
                textBox.Focus();
                textBox.SelectAll();
                OviaPathEditExitFilter.Attach(breadcrumb, textBox);
            }
        }

        private void HidePathEditMode(LinkLabel breadcrumb, TextBox textBox)
        {
            if (textBox != null)
            {
                textBox.Visible = false;
            }

            if (breadcrumb != null)
            {
                breadcrumb.Visible = true;
                breadcrumb.BringToFront();
            }

            OviaPathEditExitFilter.Detach();
        }

        private void PathCopy_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
            {
                this.ActiveControl = null;
                e.Handled = true;
                e.SuppressKeyPress = true;
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

        private LinkLabel CreateBreadcrumbLabel()
        {
            LinkLabel label = new LinkLabel();
            label.Text = "";
            label.AutoSize = false;
            label.Size = new Size(880, 22);
            label.Location = new Point(10, 6);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            label.BackColor = Color.White;
            label.ForeColor = Color.Black;
            label.LinkColor = Color.Black;
            label.ActiveLinkColor = BrandViolet;
            label.VisitedLinkColor = Color.Black;
            label.DisabledLinkColor = Color.Black;
            label.LinkBehavior = LinkBehavior.NeverUnderline;
            label.TabStop = false;
            return label;
        }

        private void Breadcrumb_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string target = e.Link.LinkData == null ? "" : e.Link.LinkData.ToString();
            NavigateByWorkspacePath(target);
        }

        private void NavigateToMain()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToMain();
                return;
            }

            this.Close();
        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.RequestLogout();
                return;
            }

            this.Close();
        }

        private Button CreateHelpIcon(string helpText)
        {
            Button button = new Button();
            button.Text = "";
            button.Size = new Size(24, 24);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SurfaceColor;
            button.FlatAppearance.MouseDownBackColor = SurfaceColor;
            button.BackColor = SurfaceColor;
            button.ForeColor = OviaFluentTheme.TextTertiary;
            button.Cursor = Cursors.Help;
            button.TabStop = false;
            button.Paint += HelpIcon_Paint;

            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, helpText);
            }

            return button;
        }

        private void HelpIcon_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(2, 2, button.Width - 5, button.Height - 5);
            Color lineColor = OviaFluentTheme.ControlBorder;
            Color textColor = OviaFluentTheme.TextTertiary;

            using (SolidBrush fillBrush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(lineColor, 1.2F))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (Font font = OviaFluentTheme.FontKorean(10F, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                e.Graphics.FillEllipse(fillBrush, rect);
                e.Graphics.DrawEllipse(pen, rect);
                e.Graphics.DrawString("?", font, textBrush, rect, format);
            }
        }

        private void BuildSearchArea(Control parent)
        {
            OviaProjectCard card = new OviaProjectCard();
            card.Location = new Point(34, 128);
            card.Size = new Size(1108, 108);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            txtSearch = new OviaSearchBox();
            txtSearch.PlaceholderText = "공사 검색";
            txtSearch.Location = new Point(22, 36);
            txtSearch.Size = new Size(380, OviaFluentTheme.CommonInputHeight);
            txtSearch.Font = OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            txtSearch.TextChanged += Filter_Changed;
            card.Controls.Add(txtSearch);

            chkIncludeDone = new OviaCheckBox();
            chkIncludeDone.Text = "완료공사 포함";
            chkIncludeDone.AutoSize = false;
            chkIncludeDone.Font = OviaFluentTheme.FontInput(9.6F, FontStyle.Regular);
            chkIncludeDone.ForeColor = TextDark;
            chkIncludeDone.BackColor = Color.Transparent;
            chkIncludeDone.Location = new Point(426, 42);
            chkIncludeDone.Size = new Size(128, 24);
            chkIncludeDone.CheckedChanged += Filter_Changed;
            card.Controls.Add(chkIncludeDone);

        }

        private void BuildProjectGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 258);
            grid.Size = new Size(1108, 390);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.CellClick += Grid_CellClick;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CellFormatting += Grid_CellFormatting;
            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.5F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = OviaFluentTheme.AccentLight;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 30;

            OviaFluentTheme.ApplyDataGrid(grid);

            AddSequenceColumn();
            AddColumn("공사번호", 90);
            AddColumn("공사명", 320);
            AddColumn("거래처", 180);
            AddColumn("상태", 80);
            AddColumn("생성일", 110);
            AddColumn("최근작업일", 120);
            AddColumn("담당자", 90);
            AddColumn("비고", 190);

            ApplyProjectGridAlignments();

            parent.Controls.Add(grid);
        }


        private void AddSequenceColumn()
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = "No.";
            column.HeaderText = "No.";
            column.Width = 55;
            column.MinimumWidth = 45;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.Resizable = DataGridViewTriState.False;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(column);
        }

        private void AddColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = width;
            column.FillWeight = width;
            column.MinimumWidth = 45;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
            column.Resizable = DataGridViewTriState.True;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(column);
        }

        private void ApplyProjectGridAlignments()
        {
            if (grid == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            SetColumnAlignment("공사명", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("거래처", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("비고", DataGridViewContentAlignment.MiddleLeft);
        }

        private void SetColumnAlignment(string columnName, DataGridViewContentAlignment alignment)
        {
            if (grid == null || !grid.Columns.Contains(columnName))
            {
                return;
            }

            grid.Columns[columnName].DefaultCellStyle.Alignment = alignment;
        }

        private void BuildFooter(Control parent)
        {
            pagerPanel = new Panel();
            pagerPanel.Location = new Point(38, 654);
            pagerPanel.Size = new Size(1040, 36);
            pagerPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            pagerPanel.BackColor = SurfaceColor;
            parent.Controls.Add(pagerPanel);

            lblStatus = OviaWorkspaceStatusLabel.Create(
                parent,
                "ERP 공사목록을 불러오는 중입니다.",
                38,
                692
            );

            sessionInfoPanel = new Panel();
            sessionInfoPanel.Location = new Point(420, 692);
            sessionInfoPanel.Size = new Size(720, 24);
            sessionInfoPanel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            sessionInfoPanel.BackColor = Color.Transparent;
            sessionInfoPanel.Resize += delegate { ApplySessionInfoLayout(); };

            lblSessionInfo = new Label();
            lblSessionInfo.Location = new Point(0, 0);
            lblSessionInfo.Size = new Size(sessionInfoPanel.Width, 24);
            lblSessionInfo.BackColor = Color.Transparent;
            lblSessionInfo.ForeColor = OviaFluentTheme.TextSecondary;
            lblSessionInfo.Font = OviaFluentTheme.FontKorean(8.8F, FontStyle.Regular);
            lblSessionInfo.TextAlign = ContentAlignment.MiddleRight;
            lblSessionInfo.AutoEllipsis = true;
            lblSessionInfo.Cursor = Cursors.Default;

            lblAutoCadInfo = new Label();
            lblAutoCadInfo.Location = new Point(sessionInfoPanel.Width, 0);
            lblAutoCadInfo.Size = new Size(0, 24);
            lblAutoCadInfo.BackColor = Color.Transparent;
            lblAutoCadInfo.ForeColor = OviaFluentTheme.TextSecondary;
            lblAutoCadInfo.Font = OviaFluentTheme.FontKorean(8.8F, FontStyle.Regular);
            lblAutoCadInfo.TextAlign = ContentAlignment.MiddleRight;
            lblAutoCadInfo.AutoEllipsis = false;
            lblAutoCadInfo.Cursor = Cursors.Hand;
            lblAutoCadInfo.Visible = false;

            supportInfoContextMenu = OviaGridContextMenuFactory.CreateMenu(
                OviaGridContextMenuFactory.CreateItem("지원정보 복사", delegate { CopySupportInfoToClipboard(); })
            );
            lblAutoCadInfo.ContextMenuStrip = supportInfoContextMenu;

            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(lblAutoCadInfo, "우클릭하면 지원정보 복사");
            }

            sessionInfoPanel.Controls.Add(lblSessionInfo);
            sessionInfoPanel.Controls.Add(lblAutoCadInfo);
            parent.Controls.Add(sessionInfoPanel);
            sessionInfoPanel.BringToFront();

            AutoCadRuntimeInfo initialAutoCadRuntimeInfo;
            bool isAutoCadRunning = AutoCadRuntimeChecker.TryGetRunningAutoCadRuntimeInfo(out initialAutoCadRuntimeInfo);
            RefreshSessionInfoLabel(isAutoCadRunning ? initialAutoCadRuntimeInfo : null);
        }

        private void UpdateSessionInfo(string sessionCompanyId, string sessionUserId, string sessionUserName, string sessionIpAddress)
        {
            currentSessionCompanyId = string.IsNullOrWhiteSpace(sessionCompanyId) ? companyId : sessionCompanyId.Trim();
            currentSessionUserId = string.IsNullOrWhiteSpace(sessionUserId) ? userId : sessionUserId.Trim();
            currentSessionUserName = sessionUserName == null ? "" : sessionUserName.Trim();
            currentSessionIpAddress = sessionIpAddress == null ? "" : sessionIpAddress.Trim();

            AutoCadRuntimeInfo runtimeInfo;
            bool isAutoCadRunning = AutoCadRuntimeChecker.TryGetRunningAutoCadRuntimeInfo(out runtimeInfo);
            RefreshSessionInfoLabel(isAutoCadRunning ? runtimeInfo : null);
        }

        private void RefreshSessionInfoLabel(AutoCadRuntimeInfo runtimeInfo)
        {
            currentAutoCadRuntimeInfo = runtimeInfo;

            if (lblSessionInfo == null || lblSessionInfo.IsDisposed)
            {
                return;
            }

            lblSessionInfo.Text = BuildSessionInfoText(
                currentSessionCompanyId,
                currentSessionUserId,
                currentSessionUserName,
                currentSessionIpAddress
            );

            if (lblAutoCadInfo != null && !lblAutoCadInfo.IsDisposed)
            {
                lblAutoCadInfo.Text = runtimeInfo == null ? "" : "  |  AutoCAD " + runtimeInfo.DisplayText;
            }

            ApplySessionInfoLayout();
        }

        private void ApplySessionInfoLayout()
        {
            if (sessionInfoPanel == null || sessionInfoPanel.IsDisposed
                || lblSessionInfo == null || lblSessionInfo.IsDisposed
                || lblAutoCadInfo == null || lblAutoCadInfo.IsDisposed)
            {
                return;
            }

            int panelWidth = Math.Max(0, sessionInfoPanel.ClientSize.Width);
            int panelHeight = Math.Max(1, sessionInfoPanel.ClientSize.Height);
            bool showAutoCad = currentAutoCadRuntimeInfo != null && !string.IsNullOrWhiteSpace(lblAutoCadInfo.Text);
            int autoCadWidth = 0;

            if (showAutoCad)
            {
                Size measured = TextRenderer.MeasureText(
                    lblAutoCadInfo.Text,
                    lblAutoCadInfo.Font,
                    new Size(int.MaxValue, panelHeight),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                );
                autoCadWidth = Math.Min(panelWidth, Math.Max(1, measured.Width + 4));
            }

            lblAutoCadInfo.Visible = showAutoCad;
            lblAutoCadInfo.SetBounds(panelWidth - autoCadWidth, 0, autoCadWidth, panelHeight);
            lblSessionInfo.SetBounds(0, 0, Math.Max(0, panelWidth - autoCadWidth), panelHeight);
        }

        private string BuildSessionInfoText(string sessionCompanyId, string sessionUserId, string sessionUserName, string sessionIpAddress)
        {
            string company = string.IsNullOrWhiteSpace(sessionCompanyId) ? "-" : sessionCompanyId.Trim();
            string user = string.IsNullOrWhiteSpace(sessionUserId) ? "-" : sessionUserId.Trim();
            string name = sessionUserName == null ? "" : sessionUserName.Trim();
            string ip = string.IsNullOrWhiteSpace(sessionIpAddress) ? "-" : sessionIpAddress.Trim();

            string userText = user;
            if (name != "")
            {
                userText += " (" + name + ")";
            }

            return "Biz ID : " + company
                + "  |  ID : " + userText
                + "  |  IP : " + ip;
        }

        private void CopySupportInfoToClipboard()
        {
            string supportInfo = BuildSupportInfoText();

            try
            {
                Clipboard.SetText(supportInfo);
                ShowSupportInfoMessage(
                    "지원정보가 클립보드에 복사되었습니다.\r\n시스템관리자에게 전달해 주세요.",
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                ShowSupportInfoMessage(
                    "지원정보를 복사하지 못했습니다.\r\n" + ex.Message,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void ShowSupportInfoMessage(string message, MessageBoxIcon icon)
        {
            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                MessageBox.Show(
                    this,
                    message,
                    "OVIA 지원정보",
                    MessageBoxButtons.OK,
                    icon
                );
                return;
            }

            MessageBox.Show(
                message,
                "OVIA 지원정보",
                MessageBoxButtons.OK,
                icon
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (lblAutoCadInfo != null && !lblAutoCadInfo.IsDisposed)
                {
                    lblAutoCadInfo.ContextMenuStrip = null;
                }

                if (supportInfoContextMenu != null)
                {
                    supportInfoContextMenu.Dispose();
                    supportInfoContextMenu = null;
                }
            }

            base.Dispose(disposing);
        }

        private string BuildSupportInfoText()
        {
            StringBuilder builder = new StringBuilder();
            string company = string.IsNullOrWhiteSpace(currentSessionCompanyId) ? "-" : currentSessionCompanyId.Trim();
            string user = string.IsNullOrWhiteSpace(currentSessionUserId) ? "-" : currentSessionUserId.Trim();
            string name = currentSessionUserName == null ? "" : currentSessionUserName.Trim();
            string ip = string.IsNullOrWhiteSpace(currentSessionIpAddress) ? "-" : currentSessionIpAddress.Trim();
            string userText = user;

            if (name != "")
            {
                userText += " (" + name + ")";
            }

            builder.AppendLine("OVIA 지원정보");
            builder.AppendLine("생성일시 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("OVIA Version : " + (string.IsNullOrWhiteSpace(Application.ProductVersion) ? "-" : Application.ProductVersion));
            builder.AppendLine("Biz ID : " + company);
            builder.AppendLine("ID : " + userText);
            builder.AppendLine("IP : " + ip);
            builder.AppendLine("Windows : " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " / x64" : " / x86"));
            builder.AppendLine();

            List<AutoCadRuntimeInfo> runtimeInfos = AutoCadRuntimeChecker.GetRunningAutoCadRuntimeInfos();

            if (runtimeInfos == null || runtimeInfos.Count == 0)
            {
                builder.AppendLine("실행 중 AutoCAD : 없음");
            }
            else
            {
                builder.AppendLine("실행 중 AutoCAD");
                HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int displayIndex = 1;
                int i;

                for (i = 0; i < runtimeInfos.Count; i++)
                {
                    AutoCadRuntimeInfo info = runtimeInfos[i];

                    if (info == null)
                    {
                        continue;
                    }

                    string key = info.Year.ToString() + "|" + (info.BuildVersion ?? "");

                    if (emitted.Contains(key))
                    {
                        continue;
                    }

                    emitted.Add(key);
                    builder.AppendLine("  AutoCAD #" + displayIndex.ToString() + " : " + info.DisplayText);

                    if (!string.IsNullOrWhiteSpace(info.ProductName))
                    {
                        builder.AppendLine("  Product #" + displayIndex.ToString() + " : " + info.ProductName.Trim());
                    }

                    builder.AppendLine("  CAD Plugin #" + displayIndex.ToString() + " : " + info.PluginAssemblyName);
                    displayIndex++;
                }
            }

            builder.AppendLine();
            builder.AppendLine("OVIA CAD Plugin 설치정보");

            int[] supportedYears = new int[] { 2024, 2025, 2026, 2027 };
            int yearIndex;

            for (yearIndex = 0; yearIndex < supportedYears.Length; yearIndex++)
            {
                int year = supportedYears[yearIndex];
                string pluginFileVersion;
                string pluginPath;
                bool installed = AutoCadRuntimeChecker.TryGetInstalledOviaPluginFileInfo(year, out pluginFileVersion, out pluginPath);
                string pluginName = "OVIA.AutoCAD." + year.ToString() + ".dll";

                if (installed)
                {
                    builder.Append("  " + year.ToString() + " : " + pluginName + " / 설치됨");

                    if (!string.IsNullOrWhiteSpace(pluginFileVersion))
                    {
                        builder.Append(" / File Version " + pluginFileVersion.Trim());
                    }

                    builder.AppendLine();
                }
                else
                {
                    builder.AppendLine("  " + year.ToString() + " : " + pluginName + " / 찾을 수 없음");
                }
            }

            return builder.ToString().TrimEnd();
        }


        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(BaseClientWidth, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "PROJECT", companyId, userId);
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

        private void BindProjects()
        {
            if (!CanUpdateProjectGrid())
            {
                return;
            }

            BeginContentLoading();
            try
            {
                if (!CanUpdateProjectGrid())
                {
                    return;
                }

                currentProjects = GetFilteredProjects();
            pageSize = GetConfiguredListPageSize();

            int maxPage = GetMaxPage();
            if (currentPage > maxPage)
            {
                currentPage = maxPage;
            }
            if (currentPage < 1)
            {
                currentPage = 1;
            }

            if (!CanUpdateProjectGrid())
            {
                return;
            }

            grid.Rows.Clear();

            int start = (currentPage - 1) * pageSize;
            int end = Math.Min(start + pageSize, currentProjects.Count);
            int i;

            for (i = start; i < end; i++)
            {
                if (!CanUpdateProjectGrid())
                {
                    return;
                }

                grid.Rows.Add(
                    (currentProjects.Count - i).ToString(),
                    currentProjects[i].ProjectNo,
                    currentProjects[i].ProjectName,
                    currentProjects[i].ClientName,
                    currentProjects[i].Status,
                    currentProjects[i].CreatedDate,
                    currentProjects[i].LastWorkDate,
                    currentProjects[i].Manager,
                    currentProjects[i].Memo
                );
            }

            RenderPager();
            UpdateSortGlyph();

            int displayCount = Math.Max(0, end - start);
            lblStatus.Text = currentProjects.Count > displayCount
                ? "검색 결과: " + currentProjects.Count.ToString() + "건 / 현재 표시: " + displayCount.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건"
                : "검색 결과: " + currentProjects.Count.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건";
            }
            finally
            {
                EndContentLoading();
            }
        }

        private void RenderPager()
        {
            if (pagerPanel == null)
            {
                return;
            }

            pagerPanel.Controls.Clear();

            int maxPage = GetMaxPage();
            int left = 0;

            AddPagerLink("처음", 1, ref left, currentPage > 1);
            AddPagerLink("이전", currentPage - 1, ref left, currentPage > 1);

            int firstPage = Math.Max(1, currentPage - 2);
            int lastPage = Math.Min(maxPage, firstPage + 4);
            if (lastPage - firstPage < 4)
            {
                firstPage = Math.Max(1, lastPage - 4);
            }

            for (int page = firstPage; page <= lastPage; page++)
            {
                AddPagerLink(page.ToString(), page, ref left, true);
            }

            AddPagerLink("다음", currentPage + 1, ref left, currentPage < maxPage);
            AddPagerLink("끝", maxPage, ref left, currentPage < maxPage);
        }

        private void AddPagerLink(string text, int targetPage, ref int left, bool enabled)
        {
            bool isCurrentPage = targetPage == currentPage && IsNumericText(text);
            Button button = new Button();
            button.Text = text;
            button.Tag = targetPage;
            button.AutoSize = false;
            button.Size = MeasurePagerButtonSize(text);
            button.Location = new Point(left, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = enabled ? Color.FromArgb(208, 216, 226) : Color.FromArgb(222, 226, 232);
            button.FlatAppearance.MouseOverBackColor = isCurrentPage ? OviaFluentTheme.AccentHover : Color.FromArgb(241, 246, 255);
            button.FlatAppearance.MouseDownBackColor = isCurrentPage ? OviaFluentTheme.AccentHover : Color.FromArgb(226, 237, 255);
            button.BackColor = isCurrentPage ? OviaFluentTheme.Accent : Color.White;
            button.ForeColor = isCurrentPage ? Color.White : (enabled ? TextDark : TextSub);
            button.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            button.Enabled = enabled;
            button.UseVisualStyleBackColor = false;
            button.Click += Pager_Click;
            pagerPanel.Controls.Add(button);
            left += button.Width + 7;
        }

        private Size MeasurePagerButtonSize(string text)
        {
            Size textSize = TextRenderer.MeasureText(text, OviaFluentTheme.FontData(8.7F, FontStyle.Regular));
            return new Size(Math.Max(34, textSize.Width + 18), 28);
        }

        private bool IsNumericText(string text)
        {
            int value;
            return int.TryParse(text, out value);
        }

        private void Pager_Click(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control == null || control.Tag == null)
            {
                return;
            }

            int nextPage = Convert.ToInt32(control.Tag);
            nextPage = Math.Max(1, Math.Min(GetMaxPage(), nextPage));
            if (nextPage == currentPage)
            {
                return;
            }

            currentPage = nextPage;
            BindProjects();
        }

        private int GetMaxPage()
        {
            if (pageSize <= 0)
            {
                pageSize = 100;
            }

            int count = currentProjects == null ? 0 : currentProjects.Count;
            return Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        }

        private int GetConfiguredListPageSize()
        {
            try
            {
                return OviaSystemSettingsStore.GetListPageSize();
            }
            catch
            {
                return 100;
            }
        }

        private List<OviaProjectRow> GetFilteredProjects()
        {
            List<OviaProjectRow> list = new List<OviaProjectRow>();
            string keyword = "";

            if (txtSearch != null && txtSearch.Text != null)
            {
                keyword = txtSearch.Text.Trim();
            }

            int i;

            for (i = 0; i < allProjects.Count; i++)
            {
                OviaProjectRow row = allProjects[i];

                if (!chkIncludeDone.Checked && row.Status == "완료")
                {
                    continue;
                }

                if (keyword != "")
                {
                    string target = row.ProjectNo + " " + row.ProjectName + " " + row.ClientName + " " + row.Manager;

                    if (target.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                list.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(headerSortColumn))
            {
                list.Sort(delegate (OviaProjectRow a, OviaProjectRow b)
                {
                    int result = CompareProjectRows(a, b, headerSortColumn);
                    return headerSortAscending ? result : -result;
                });

                return list;
            }

            // 공사목록의 기본 순서는 ERP project_list가 반환한 project_no 기준 내림차순이다.
            // 숫자형 공사번호는 숫자값으로 비교하고, 숫자가 아닌 값은 문자열 비교로 안전하게 정렬한다.
            list.Sort(delegate (OviaProjectRow a, OviaProjectRow b)
            {
                return CompareProjectNo(b.ProjectNo, a.ProjectNo);
            });

            return list;
        }

        private static int CompareProjectNo(string left, string right)
        {
            long leftNumber;
            long rightNumber;
            bool leftIsNumber = long.TryParse(left == null ? "" : left.Trim(), out leftNumber);
            bool rightIsNumber = long.TryParse(right == null ? "" : right.Trim(), out rightNumber);

            if (leftIsNumber && rightIsNumber)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
        }

        private int CompareProjectRows(OviaProjectRow a, OviaProjectRow b, string columnName)
        {
            if (columnName == "공사번호")
            {
                return CompareProjectNo(a.ProjectNo, b.ProjectNo);
            }

            if (columnName == "공사명")
            {
                return string.Compare(a.ProjectName, b.ProjectName, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "거래처")
            {
                return string.Compare(a.ClientName, b.ClientName, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "상태")
            {
                return string.Compare(a.Status, b.Status, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "생성일")
            {
                return CompareDateText(a.CreatedDate, b.CreatedDate);
            }

            if (columnName == "최근작업일")
            {
                return CompareDateText(a.LastWorkDate, b.LastWorkDate);
            }

            if (columnName == "담당자")
            {
                return string.Compare(a.Manager, b.Manager, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "비고")
            {
                return string.Compare(a.Memo, b.Memo, StringComparison.CurrentCultureIgnoreCase);
            }

            return 0;
        }

        private int CompareDateText(string a, string b)
        {
            DateTime da;
            DateTime db;
            bool aOk = DateTime.TryParse(a, out da);
            bool bOk = DateTime.TryParse(b, out db);

            if (aOk && bOk)
            {
                return da.CompareTo(db);
            }

            return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (grid == null || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (string.IsNullOrWhiteSpace(columnName) || columnName == "No.")
            {
                return;
            }

            if (headerSortColumn == columnName)
            {
                headerSortAscending = !headerSortAscending;
            }
            else
            {
                headerSortColumn = columnName;
                headerSortAscending = true;
            }

            currentPage = 1;
            BindProjects();
        }

        private void UpdateSortGlyph()
        {
            if (grid == null)
            {
                return;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            if (!string.IsNullOrWhiteSpace(headerSortColumn) && grid.Columns.Contains(headerSortColumn))
            {
                grid.Columns[headerSortColumn].HeaderCell.SortGlyphDirection = headerSortAscending ? SortOrder.Ascending : SortOrder.Descending;
            }
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            currentPage = 1;
            BindProjects();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // 공사 상세 이동은 더블클릭으로만 처리한다.
            // 원클릭은 행 선택만 수행해 사용자가 실수로 공사별 BarList로 이동하지 않도록 한다.
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (column == null || column.Name != "상태")
            {
                return;
            }

            string status = e.Value == null ? "" : e.Value.ToString().Trim();
            if (status == "")
            {
                return;
            }

            if (status == "진행중" || status == "진행")
            {
                e.CellStyle.ForeColor = OviaFluentTheme.Accent;
                e.CellStyle.SelectionForeColor = OviaFluentTheme.Accent;
            }
            else if (status == "완료")
            {
                e.CellStyle.ForeColor = OviaFluentTheme.TextMuted;
                e.CellStyle.SelectionForeColor = OviaFluentTheme.TextMuted;
            }
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OpenSelectedProject();
        }

        private void OpenSelectedProject()
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "공사를 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string projectNo = GetSelectedCellText("공사번호");
            string projectName = GetSelectedCellText("공사명");
            string clientName = GetSelectedCellText("거래처");
            string status = GetSelectedCellText("상태");

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToProjectBarListList(projectNo, projectName, clientName, status);
                return;
            }

            FrmProjectBarListList barListList = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, status);
            ShowReplacementWindow(barListList);
        }

        private void ShowReplacementWindow(Form nextForm)
        {
            if (nextForm == null)
            {
                return;
            }

            Form ownerForm = this.Owner;
            FormWindowState currentState = this.WindowState;
            Rectangle normalBounds = this.WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;

            nextForm.StartPosition = FormStartPosition.Manual;
            nextForm.Bounds = normalBounds;

            if (currentState == FormWindowState.Maximized)
            {
                nextForm.WindowState = FormWindowState.Maximized;
            }

            nextForm.Show();
            nextForm.Activate();
            this.Close();
        }

        private string GetSelectedCellText(string columnName)
        {
            if (grid.SelectedRows.Count == 0)
            {
                return "";
            }

            if (!grid.Columns.Contains(columnName))
            {
                return "";
            }

            object value = grid.SelectedRows[0].Cells[columnName].Value;

            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

        private void Close_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }

    public class OviaProjectRow
    {
        public string ProjectNo = "";
        public string ProjectName = "";
        public string ClientName = "";
        public string Status = "";
        public string CreatedDate = "";
        public string LastWorkDate = "";
        public string Manager = "";
        public string Memo = "";

        public OviaProjectRow(
            string projectNo,
            string projectName,
            string clientName,
            string status,
            string createdDate,
            string lastWorkDate,
            string manager,
            string memo
        )
        {
            ProjectNo = projectNo;
            ProjectName = projectName;
            ClientName = clientName;
            Status = status;
            CreatedDate = createdDate;
            LastWorkDate = lastWorkDate;
            Manager = manager;
            Memo = memo;
        }
    }

    public class OviaProjectCard : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;

        public OviaProjectCard()
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

            using (GraphicsPath path = OviaProjectDrawHelper.RoundRect(rect, 14))
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

    public class OviaProjectButton : Control
    {
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.Accent;

        private bool hover;

        public OviaProjectButton()
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

            Color fillColor = hover ? OviaFluentTheme.AccentHover : StartColor;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaProjectDrawHelper.RoundRect(rect, OviaFluentTheme.ButtonRadius))
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                OviaFluentTheme.FontButton(10F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }

        private Color Lighten(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, color.R + amount),
                Math.Min(255, color.G + amount),
                Math.Min(255, color.B + amount)
            );
        }
    }

    public static class OviaProjectDrawHelper
    {
        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
