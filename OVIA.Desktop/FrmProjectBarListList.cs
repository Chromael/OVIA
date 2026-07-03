using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmProjectBarListList : Form, IOviaWorkspaceLayout
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly string projectNo;
        private readonly string projectName;
        private readonly string clientName;
        private readonly string projectStatus;

        private DataGridView grid;
        private Label lblStatus;
        private Panel pagerPanel;
        private Label lblProjectTitle;
        private Label lblProjectSub;
        private ToolTip windowToolTip;

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
        private readonly Color BrandCyan = Color.FromArgb(64, 156, 255);
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private const int BaseClientWidth = 1180;
        private const int BaseClientHeight = 720;
        private Panel scrollPanel;
        private Panel contentPanel;
        private bool isScrollResetQueued = false;
        private bool isInternalNavigation = false;
        private bool isBackNavigationQueued = false;
        private List<ProjectBarListSummary> currentBarListRows = new List<ProjectBarListSummary>();
        private int pageSize = 100;
        private int currentPage = 1;
        private string headerSortColumn = "";
        private bool headerSortAscending = true;

        public FrmProjectBarListList(string companyId, string userId, string projectNo, string projectName, string clientName, string projectStatus)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.projectNo = projectNo == null ? "" : projectNo;
            this.projectName = projectName == null ? "" : projectName;
            this.clientName = clientName == null ? "" : clientName;
            this.projectStatus = projectStatus == null ? "" : projectStatus;

            BuildUI();
            BindBarListRows();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA 공사별 BarList";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(BaseClientWidth, BaseClientHeight);
            this.MinimumSize = new Size(1100, 750);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmProjectBarListList_FormClosing;

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
            BuildProjectInfo(contentPanel);
            BuildToolbar(contentPanel);
            BuildGrid(contentPanel);
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
            BuildExplorerHeader(parent, "메인  ›  공사관리  ›  공사별 BarList");
        }

        private void BuildExplorerHeader(Control parent, string pathText)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { NavigateBackToProjectManager(); },
                delegate { NavigateBackToProjectManager(); },
                delegate { RefreshButton_Click(null, EventArgs.Empty); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    NavigateByWorkspacePath(target);
                }
            );
        }

        private void NavigateByWorkspacePath(string target)
        {
            if (target == "MAIN")
            {
                NavigateToMain();
                return;
            }

            if (target == "PROJECT_MANAGER")
            {
                NavigateBackToProjectManager();
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
            int projectStart = breadcrumb.Text.IndexOf("공사관리");
            if (projectStart >= 0)
            {
                breadcrumb.Links.Add(projectStart, "공사관리".Length, "PROJECT_MANAGER");
            }
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
            textBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            textBox.ForeColor = Color.Black;
            textBox.BackColor = Color.White;
            textBox.Location = new Point(10, 7);
            textBox.Size = new Size(880, 20);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = Padding.Empty;
            textBox.TabStop = false;
            textBox.Visible = false;
            textBox.Click += delegate
            {
                textBox.SelectAll();
            };
            textBox.Enter += delegate
            {
                textBox.SelectAll();
            };
            textBox.KeyDown += PathCopy_KeyDown;

            textBox.Leave += delegate
            {
                HidePathEditMode(breadcrumb, textBox);
            };
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
            if (pathText == null)
            {
                return "";
            }

            return pathText.Replace("  ›  ", "\\");
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
            TextBox textBox = sender as TextBox;

            if (textBox == null)
            {
                return;
            }

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
            label.Size = new Size(760, 22);
            label.Location = new Point(38, 72);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            label.BackColor = Color.White;
            label.ForeColor = Color.Black;
            label.LinkColor = Color.Black;
            label.ActiveLinkColor = OviaFluentTheme.Accent;
            label.VisitedLinkColor = Color.Black;
            label.DisabledLinkColor = Color.Black;
            label.LinkBehavior = LinkBehavior.NeverUnderline;
            label.TabStop = false;
            return label;
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
            using (Font font = OviaFluentTheme.FontButton(9F, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                e.Graphics.FillEllipse(fillBrush, rect);
                e.Graphics.DrawEllipse(pen, rect);
                e.Graphics.DrawString("?", font, textBrush, rect, format);
            }
        }

        private void Breadcrumb_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string target = e.Link.LinkData == null ? "" : e.Link.LinkData.ToString();
            NavigateByWorkspacePath(target);
        }

        private void BuildProjectInfo(Control parent)
        {
            OviaProjectBarListCard card = new OviaProjectBarListCard();
            card.Location = new Point(34, 128);
            card.Size = new Size(1108, 78);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            lblProjectTitle = new Label();
            lblProjectTitle.Text = projectNo + "  " + projectName;
            lblProjectTitle.AutoSize = true;
            lblProjectTitle.Font = OviaFluentTheme.FontTitle(14F, FontStyle.Bold);
            lblProjectTitle.ForeColor = TextDark;
            lblProjectTitle.BackColor = Color.White;
            lblProjectTitle.Location = new Point(22, 15);
            card.Controls.Add(lblProjectTitle);

            lblProjectSub = new Label();
            lblProjectSub.Text = "거래처: " + clientName + "   |   상태: " + projectStatus;
            lblProjectSub.AutoSize = true;
            lblProjectSub.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            lblProjectSub.ForeColor = TextSub;
            lblProjectSub.BackColor = Color.White;
            lblProjectSub.Location = new Point(24, 48);
            card.Controls.Add(lblProjectSub);
        }

        private void BuildToolbar(Control parent)
        {
            OviaProjectBarListCard card = new OviaProjectBarListCard();
            card.Location = new Point(34, 221);
            card.Size = new Size(1108, 92);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            OVIA.Desktop.Controls.OviaButton newButton = new OVIA.Desktop.Controls.OviaButton();
            newButton.Text = "신규 BarList 등록";
            newButton.Location = new Point(22, 28);
            newButton.Size = OviaFluentTheme.MeasureButtonSize(newButton.Text);
            newButton.Role = OVIA.Desktop.OviaButtonRole.Primary;
            newButton.Click += NewButton_Click;
            card.Controls.Add(newButton);

            OVIA.Desktop.Controls.OviaButton refreshButton = new OVIA.Desktop.Controls.OviaButton();
            refreshButton.Text = "새로고침";
            refreshButton.Location = new Point(newButton.Right + 10, 28);
            refreshButton.Size = OviaFluentTheme.MeasureButtonSize(refreshButton.Text);
            refreshButton.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            refreshButton.Click += RefreshButton_Click;
            card.Controls.Add(refreshButton);

            Label guide = new Label();
            guide.Text = "주의: AutoCAD에서 가져온 데이터는 반드시 도면의 BarList와 비교 확인 후 저장하세요. 저장 전 후보 데이터는 이 목록에 표시되지 않습니다.";
            guide.AutoSize = false;
            guide.Size = new Size(780, 38);
            guide.Font = OviaFluentTheme.FontSystem(8.8F, FontStyle.Bold);
            guide.ForeColor = OviaFluentTheme.Danger;
            guide.BackColor = Color.FromArgb(255, 248, 230);
            guide.Location = new Point(refreshButton.Right + 20, 27);
            guide.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(guide);
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 335);
            grid.Size = new Size(1108, 261);
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
            grid.CellDoubleClick += Grid_CellDoubleClick;
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

            AddColumn("상태", 70);
            AddColumn("제목", 260);
            AddColumn("등록일", 125);
            AddColumn("수정일", 125);
            AddColumn("행수", 70);
            AddColumn("총수량", 90);
            AddColumn("총길이(M)", 110);
            AddColumn("중량(Ton)", 110);
            AddColumn("작성자", 90);
            AddColumn("비고", 130);
            AddColumn("FilePath", 0);

            grid.Columns["FilePath"].Visible = false;

            ApplyGridColumnAlignment();

            parent.Controls.Add(grid);
        }

        private void ApplyGridColumnAlignment()
        {
            SetColumnAlignment("상태", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("등록일", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("수정일", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("행수", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("작성자", DataGridViewContentAlignment.MiddleCenter);

            SetColumnAlignment("제목", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("비고", DataGridViewContentAlignment.MiddleLeft);

            SetColumnAlignment("총수량", DataGridViewContentAlignment.MiddleRight);
            SetColumnAlignment("총길이(M)", DataGridViewContentAlignment.MiddleRight);
            SetColumnAlignment("중량(Ton)", DataGridViewContentAlignment.MiddleRight);
        }

        private void SetColumnAlignment(string columnName, DataGridViewContentAlignment alignment)
        {
            if (grid == null || !grid.Columns.Contains(columnName))
            {
                return;
            }

            grid.Columns[columnName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns[columnName].DefaultCellStyle.Alignment = alignment;
        }

        private void AddColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = Math.Max(5, width);
            column.FillWeight = Math.Max(1, width);
            column.MinimumWidth = width <= 0 ? 5 : 45;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
            column.Resizable = DataGridViewTriState.True;
            grid.Columns.Add(column);
        }

        private void BuildFooter(Control parent)
        {
            pagerPanel = new Panel();
            pagerPanel.Location = new Point(38, 612);
            pagerPanel.Size = new Size(1040, 36);
            pagerPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            pagerPanel.BackColor = SurfaceColor;
            parent.Controls.Add(pagerPanel);

            lblStatus = OviaWorkspaceStatusLabel.Create(parent, "", 38, 660);
            lblStatus.Font = OviaFluentTheme.FontStatus(8.2F, FontStyle.Regular);
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

        private void BindBarListRows()
        {
            grid.Rows.Clear();

            currentBarListRows = GetBarListSummaries();
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

            int start = (currentPage - 1) * pageSize;
            int end = Math.Min(start + pageSize, currentBarListRows.Count);
            int i;

            for (i = start; i < end; i++)
            {
                grid.Rows.Add(
                    currentBarListRows[i].Status,
                    currentBarListRows[i].Title,
                    currentBarListRows[i].CreatedDate,
                    currentBarListRows[i].ModifiedDate,
                    currentBarListRows[i].RowCount.ToString(),
                    currentBarListRows[i].TotalQty.ToString("0.###"),
                    currentBarListRows[i].TotalLength.ToString("0.###"),
                    currentBarListRows[i].TotalWeight.ToString("0.###"),
                    currentBarListRows[i].Writer,
                    currentBarListRows[i].Memo,
                    currentBarListRows[i].FilePath
                );
            }

            RenderPager();
            UpdateSortGlyph();

            int displayCount = Math.Max(0, end - start);
            lblStatus.Text = currentBarListRows.Count > displayCount
                ? "저장된 BarList: " + currentBarListRows.Count.ToString() + "건 / 현재 표시: " + displayCount.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건"
                : "저장된 BarList: " + currentBarListRows.Count.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건";
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
            BindBarListRows();
        }

        private int GetMaxPage()
        {
            if (pageSize <= 0)
            {
                pageSize = 100;
            }

            int count = currentBarListRows == null ? 0 : currentBarListRows.Count;
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

        private List<ProjectBarListSummary> GetBarListSummaries()
        {
            List<ProjectBarListSummary> list = new List<ProjectBarListSummary>();
            string dir = GetProjectBarListDirectory();

            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "BarList_*.csv");

            int i;

            for (i = 0; i < files.Length; i++)
            {
                list.Add(BuildSummary(files[i]));
            }

            if (!string.IsNullOrWhiteSpace(headerSortColumn))
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    int result = CompareBarListRows(a, b, headerSortColumn);
                    return headerSortAscending ? result : -result;
                });
            }
            else
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    return CompareDateText(b.ModifiedDate, a.ModifiedDate);
                });
            }

            return list;
        }

        private int CompareBarListRows(ProjectBarListSummary a, ProjectBarListSummary b, string columnName)
        {
            if (columnName == "상태")
            {
                return string.Compare(a.Status, b.Status, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "제목")
            {
                return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "등록일")
            {
                return CompareDateText(a.CreatedDate, b.CreatedDate);
            }

            if (columnName == "수정일")
            {
                return CompareDateText(a.ModifiedDate, b.ModifiedDate);
            }

            if (columnName == "행수")
            {
                return a.RowCount.CompareTo(b.RowCount);
            }

            if (columnName == "총수량")
            {
                return a.TotalQty.CompareTo(b.TotalQty);
            }

            if (columnName == "총길이(M)")
            {
                return a.TotalLength.CompareTo(b.TotalLength);
            }

            if (columnName == "중량(Ton)")
            {
                return a.TotalWeight.CompareTo(b.TotalWeight);
            }

            if (columnName == "작성자")
            {
                return string.Compare(a.Writer, b.Writer, StringComparison.CurrentCultureIgnoreCase);
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

        private ProjectBarListSummary BuildSummary(string filePath)
        {
            ProjectBarListSummary summary = new ProjectBarListSummary();

            summary.FilePath = filePath;
            summary.Title = Path.GetFileNameWithoutExtension(filePath);
            summary.CreatedDate = File.GetCreationTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.ModifiedDate = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.Status = "저장";
            summary.Writer = Environment.UserName;
            summary.Memo = "";

            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count > 1)
                {
                    List<string> headers = rows[0];

                    int qtyIndex = FindHeaderIndex(headers, "수량");
                    int totalLengthIndex = FindHeaderIndex(headers, "총길이");
                    int weightIndex = FindHeaderIndex(headers, "중량");

                    int r;

                    for (r = 1; r < rows.Count; r++)
                    {
                        summary.RowCount++;

                        if (qtyIndex >= 0 && qtyIndex < rows[r].Count)
                        {
                            summary.TotalQty += ParseNumber(rows[r][qtyIndex]);
                        }

                        if (totalLengthIndex >= 0 && totalLengthIndex < rows[r].Count)
                        {
                            summary.TotalLength += ParseNumber(rows[r][totalLengthIndex]);
                        }

                        if (weightIndex >= 0 && weightIndex < rows[r].Count)
                        {
                            summary.TotalWeight += ParseNumber(rows[r][weightIndex]);
                        }
                    }
                }
            }
            catch
            {
                summary.Memo = "요약 계산 실패";
            }

            return summary;
        }

        private int FindHeaderIndex(List<string> headers, string keyword)
        {
            int i;

            for (i = 0; i < headers.Count; i++)
            {
                if (headers[i] != null && headers[i].IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private double ParseNumber(string text)
        {
            if (text == null)
            {
                return 0;
            }

            text = text.Trim();
            text = text.Replace(",", "");
            text = text.Replace(" ", "");

            if (text == "")
            {
                return 0;
            }

            double value;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            return 0;
        }

        private List<List<string>> ReadCsv(string filePath)
        {
            string content = File.ReadAllText(filePath, Encoding.UTF8);

            if (content.Length > 0 && content[0] == '\uFEFF')
            {
                content = content.Substring(1);
            }

            return ParseCsv(content);
        }

        private List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();

            bool inQuotes = false;
            int i = 0;

            while (i < content.Length)
            {
                char ch = content[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"')
                    {
                        inQuotes = true;
                    }
                    else if (ch == ',')
                    {
                        row.Add(cell.ToString());
                        cell.Length = 0;
                    }
                    else if (ch == '\r')
                    {
                    }
                    else if (ch == '\n')
                    {
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }

                i++;
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private string GetProjectBarListDirectory()
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA",
                "Projects"
            );

            string projectKey = SanitizeFileName(projectNo + "_" + projectName);

            if (projectKey == "_")
            {
                projectKey = "NoProject";
            }

            return Path.Combine(baseDir, projectKey, "BarList");
        }

        private string SanitizeFileName(string value)
        {
            if (value == null)
            {
                value = "";
            }

            char[] invalids = Path.GetInvalidFileNameChars();
            int i;

            for (i = 0; i < invalids.Length; i++)
            {
                value = value.Replace(invalids[i], '_');
            }

            value = value.Replace(" ", "_");

            return value;
        }

        private void NewButton_Click(object sender, EventArgs e)
        {
            if (!IsAutoCadRunning())
            {
                MessageBox.Show(
                    "현재 AutoCAD가 실행중이지 않습니다. AutoCAD를 먼저 실행하세요",
                    "OVIA AutoCAD 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (lblStatus != null)
                {
                    lblStatus.Text = "AutoCAD 실행 필요";
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                }

                return;
            }

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToBarList(projectNo, projectName, clientName, projectStatus, "");
                return;
            }

            FrmBarList form = new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus);
            ShowReplacementWindow(form);
        }

        private bool IsAutoCadRunning()
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

        private void OpenButton_Click(object sender, EventArgs e)
        {
            OpenSelectedBarList();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OpenSelectedBarList();
        }

        private void OpenSelectedBarList()
        {
            if (grid.SelectedRows.Count == 0)
            {
                lblStatus.Text = "열 BarList를 선택해주세요.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            object value = grid.SelectedRows[0].Cells["FilePath"].Value;

            if (value == null || value.ToString().Trim() == "")
            {
                lblStatus.Text = "BarList 파일 경로를 찾지 못했습니다.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            string filePath = value.ToString();

            if (!File.Exists(filePath))
            {
                lblStatus.Text = "저장된 BarList 파일이 존재하지 않습니다.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToBarList(projectNo, projectName, clientName, projectStatus, filePath);
                return;
            }

            FrmBarList form = new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath);
            ShowReplacementWindow(form);
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
            isInternalNavigation = true;
            this.Close();
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (grid == null || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (string.IsNullOrWhiteSpace(columnName) || columnName == "FilePath")
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
            BindBarListRows();
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

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            currentPage = 1;
            BindBarListRows();
        }

        private void Close_Click(object sender, EventArgs e)
        {
            NavigateBackToProjectManager();
        }

        private void FrmProjectBarListList_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isInternalNavigation)
            {
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                QueueBackNavigationToProjectManager();
            }
        }

        private void QueueBackNavigationToProjectManager()
        {
            if (isBackNavigationQueued || this.IsDisposed)
            {
                return;
            }

            isBackNavigationQueued = true;

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    isBackNavigationQueued = false;
                    NavigateBackToProjectManager();
                }));
            }
            catch
            {
                isBackNavigationQueued = false;
            }
        }

        private void NavigateBackToProjectManager()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToProjectManager();
                return;
            }

            FrmProjectManager form = new FrmProjectManager(companyId, userId);
            ShowReplacementWindow(form);
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
    }

    public class ProjectBarListSummary
    {
        public string FilePath = "";
        public string Status = "";
        public string Title = "";
        public string CreatedDate = "";
        public string ModifiedDate = "";
        public int RowCount = 0;
        public double TotalQty = 0;
        public double TotalLength = 0;
        public double TotalWeight = 0;
        public string Writer = "";
        public string Memo = "";
    }

    public class OviaProjectBarListCard : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;

        public OviaProjectBarListCard()
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

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, 14))
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

    public class OviaProjectBarListButton : Control
    {
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.Accent;

        private bool hover;

        public OviaProjectBarListButton()
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

            OVIA.Desktop.OviaButtonRole role = OviaFluentTheme.InferButtonRole(this.Text);
            Color fillColor = OviaFluentTheme.ButtonPrimaryBack;
            Color borderColor = OviaFluentTheme.ButtonPrimaryBorder;
            Color textColor = OviaFluentTheme.ButtonPrimaryText;

            if (role == OVIA.Desktop.OviaButtonRole.Danger)
            {
                fillColor = hover ? OviaFluentTheme.ButtonDangerBackHover : OviaFluentTheme.ButtonDangerBack;
                borderColor = OviaFluentTheme.ButtonDangerBorder;
                textColor = OviaFluentTheme.ButtonDangerText;
            }
            else if (role == OVIA.Desktop.OviaButtonRole.Neutral)
            {
                fillColor = hover ? OviaFluentTheme.ButtonNeutralBackHover : OviaFluentTheme.ButtonNeutralBack;
                borderColor = OviaFluentTheme.ButtonNeutralBorder;
                textColor = OviaFluentTheme.ButtonNeutralText;
            }
            else
            {
                fillColor = hover ? OviaFluentTheme.ButtonPrimaryBackHover : OviaFluentTheme.ButtonPrimaryBack;
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush brush = new SolidBrush(fillColor))
            using (Pen pen = new Pen(borderColor, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                OviaFluentTheme.FontButton(OviaFluentTheme.ButtonFontSize, FontStyle.Bold),
                rect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
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

    public static class OviaProjectBarListDrawHelper
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
