using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class FrmProjectManager : Form, IOviaWorkspaceLayout
    {
        private readonly string companyId;
        private readonly string userId;

        private TextBox txtSearch;
        private ComboBox cboSort;
        private CheckBox chkIncludeDone;
        private DataGridView grid;
        private Label lblStatus;
        private ToolTip windowToolTip;

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
        private readonly Color BrandCyan = Color.FromArgb(64, 156, 255);
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private List<OviaProjectRow> allProjects = new List<OviaProjectRow>();

        private const int BaseClientWidth = 1180;
        private const int BaseClientHeight = 720;
        private const int MinFormWidth = 1100;
        private const int MinFormHeight = 750;
        private Panel scrollPanel;
        private Panel contentPanel;
        private bool isScrollResetQueued = false;

        public FrmProjectManager(string companyId, string userId)
        {
            this.companyId = companyId;
            this.userId = userId;

            BuildUI();
            LoadSampleProjects();
            BindProjects();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA 공사관리";
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

            this.ResumeLayout(false);
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
            BuildExplorerHeader(parent, "메인  ›  공사관리");
        }

        private void BuildExplorerHeader(Control parent, string pathText)
        {
            Panel bar = new Panel();
            bar.Location = new Point(34, 8);
            bar.Size = new Size(Math.Max(1, parent.ClientSize.Width - 68), 32);
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bar.BackColor = SurfaceColor;
            parent.Controls.Add(bar);

            Button back = CreateExplorerButton("\uE72B", "메인(으)로 이동");
            back.Click += delegate { NavigateToMain(); };
            bar.Controls.Add(back);

            Button forward = CreateExplorerButton("\uE72A", "앞으로");
            forward.Location = new Point(36, 0);
            StyleExplorerButtonInactive(forward);
            bar.Controls.Add(forward);

            Button up = CreateExplorerButton("\uE74A", "메인(으)로 이동");
            up.Location = new Point(72, 0);
            up.Click += delegate { NavigateToMain(); };
            bar.Controls.Add(up);

            Button refresh = CreateExplorerButton("\uE72C", "새로고침");
            refresh.Location = new Point(108, 0);
            refresh.Click += delegate
            {
                BindProjects();
            };
            bar.Controls.Add(refresh);

            Panel addressBar = CreatePathAddressBar(pathText);
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

            if (target == "MAIN")
            {
                NavigateToMain();
            }
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

            Label searchLabel = new Label();
            searchLabel.Text = "공사 검색";
            searchLabel.AutoSize = true;
            searchLabel.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Bold);
            searchLabel.ForeColor = TextSub;
            searchLabel.BackColor = Color.White;
            searchLabel.Location = new Point(22, 17);
            card.Controls.Add(searchLabel);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(22, 44);
            txtSearch.Size = new Size(380, 23);
            txtSearch.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            OviaFluentTheme.ApplyTextBox(txtSearch);
            txtSearch.TextChanged += Filter_Changed;
            card.Controls.Add(txtSearch);

            Label sortLabel = new Label();
            sortLabel.Text = "정렬";
            sortLabel.AutoSize = true;
            sortLabel.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Bold);
            sortLabel.ForeColor = TextSub;
            sortLabel.BackColor = Color.White;
            sortLabel.Location = new Point(430, 17);
            card.Controls.Add(sortLabel);

            cboSort = new ComboBox();
            OviaFluentTheme.ApplyComboBox(cboSort);
            cboSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSort.Items.Add("최근작업순");
            cboSort.Items.Add("생성순");
            cboSort.Items.Add("명칭순");
            cboSort.Items.Add("번호순");
            cboSort.SelectedIndex = 0;
            cboSort.Location = new Point(430, 44);
            cboSort.Size = new Size(150, 23);
            cboSort.SelectedIndexChanged += Filter_Changed;
            card.Controls.Add(cboSort);

            chkIncludeDone = new CheckBox();
            chkIncludeDone.Text = "완료공사 포함";
            chkIncludeDone.AutoSize = true;
            chkIncludeDone.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            chkIncludeDone.ForeColor = TextDark;
            chkIncludeDone.BackColor = Color.White;
            OviaFluentTheme.ApplyCheckBox(chkIncludeDone);
            chkIncludeDone.Location = new Point(610, 46);
            chkIncludeDone.CheckedChanged += Filter_Changed;
            card.Controls.Add(chkIncludeDone);

            OviaProjectButton newButton = new OviaProjectButton();
            newButton.Text = "새 공사";
            newButton.Location = new Point(981, 38);
            newButton.Size = new Size(105, 36);
            newButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            newButton.StartColor = BrandViolet;
            newButton.EndColor = BrandIndigo;
            newButton.Click += NewProject_Click;
            card.Controls.Add(newButton);
        }

        private void BuildProjectGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 258);
            grid.Size = new Size(1108, 334);
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

        private void AddColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = width;
            column.FillWeight = width;
            column.MinimumWidth = 45;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
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
            lblStatus = new Label();
            lblStatus.Text = "※ 현재 공사 목록은 임시 샘플입니다. 추후 셀먼 ERP/API에서 거래처와 공사 정보를 불러옵니다.";
            lblStatus.AutoSize = true;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(38, 655);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parent.Controls.Add(lblStatus);
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(BaseClientWidth, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "PROJECT");
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

        private void LoadSampleProjects()
        {
            allProjects.Clear();

            allProjects.Add(new OviaProjectRow("1538", "2026_공장판매", "셀먼", "진행", "2026-04-28", "2026-05-20", "임대표", "최근 추출 테스트"));
            allProjects.Add(new OviaProjectRow("1606", "광양 홍숭 수성복합 신축공사", "현대건설", "진행", "2026-04-15", "2026-05-18", "김팀장", ""));
            allProjects.Add(new OviaProjectRow("1618", "나주 봉황 참송 이앤씨", "나주현장", "진행", "2026-05-01", "2026-05-14", "관리자", ""));
            allProjects.Add(new OviaProjectRow("1523", "고창 프로젝트", "거래처A", "완료", "2026-03-02", "2026-04-10", "관리자", "완료공사"));
            allProjects.Add(new OviaProjectRow("1637", "광주 상무 오피스텔", "거래처B", "진행", "2026-05-11", "2026-05-19", "관리자", ""));
        }

        private void BindProjects()
        {
            List<OviaProjectRow> list = GetFilteredProjects();

            grid.Rows.Clear();

            int i;

            for (i = 0; i < list.Count; i++)
            {
                grid.Rows.Add(
                    list[i].ProjectNo,
                    list[i].ProjectName,
                    list[i].ClientName,
                    list[i].Status,
                    list[i].CreatedDate,
                    list[i].LastWorkDate,
                    list[i].Manager,
                    list[i].Memo
                );
            }

            lblStatus.Text = "검색 결과: " + list.Count.ToString() + "건";
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

            string sort = cboSort.SelectedItem == null ? "최근작업순" : cboSort.SelectedItem.ToString();

            list.Sort(delegate (OviaProjectRow a, OviaProjectRow b)
            {
                if (sort == "명칭순")
                {
                    return string.Compare(a.ProjectName, b.ProjectName, StringComparison.CurrentCultureIgnoreCase);
                }

                if (sort == "번호순")
                {
                    return string.Compare(a.ProjectNo, b.ProjectNo, StringComparison.CurrentCultureIgnoreCase);
                }

                if (sort == "생성순")
                {
                    return string.Compare(b.CreatedDate, a.CreatedDate, StringComparison.CurrentCultureIgnoreCase);
                }

                return string.Compare(b.LastWorkDate, a.LastWorkDate, StringComparison.CurrentCultureIgnoreCase);
            });

            return list;
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            BindProjects();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (grid.Columns[e.ColumnIndex].HeaderText == "공사명")
            {
                OpenSelectedProject();
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

        private void NewProject_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "새 공사 등록은 다음 단계에서 셀먼 ERP/API 연동 구조와 함께 구현합니다.",
                "OVIA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
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

            using (GraphicsPath path = OviaProjectDrawHelper.RoundRect(rect, 7))
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
