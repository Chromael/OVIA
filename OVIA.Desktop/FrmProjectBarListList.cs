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

            OviaProjectBarListButton newButton = new OviaProjectBarListButton();
            newButton.Text = "신규 BarList 등록";
            newButton.Location = new Point(22, 28);
            newButton.Size = new Size(145, 36);
            newButton.StartColor = BrandCyan;
            newButton.EndColor = BrandViolet;
            newButton.Click += NewButton_Click;
            card.Controls.Add(newButton);

            OviaProjectBarListButton refreshButton = new OviaProjectBarListButton();
            refreshButton.Text = "새로고침";
            refreshButton.Location = new Point(180, 28);
            refreshButton.Size = new Size(95, 36);
            refreshButton.StartColor = OviaFluentTheme.TextSecondary;
            refreshButton.EndColor = OviaFluentTheme.TextSecondary;
            refreshButton.Click += RefreshButton_Click;
            card.Controls.Add(refreshButton);

            Label guide = new Label();
            guide.Text = "주의: AutoCAD에서 가져온 데이터는 반드시 도면의 BarList와 비교 확인 후 저장하세요. 저장 전 후보 데이터는 이 목록에 표시되지 않습니다.";
            guide.AutoSize = false;
            guide.Size = new Size(780, 38);
            guide.Font = OviaFluentTheme.FontSystem(8.8F, FontStyle.Bold);
            guide.ForeColor = OviaFluentTheme.Danger;
            guide.BackColor = Color.FromArgb(255, 248, 230);
            guide.Location = new Point(295, 27);
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
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.Resizable = DataGridViewTriState.True;
            grid.Columns.Add(column);
        }

        private void BuildFooter(Control parent)
        {
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

            List<ProjectBarListSummary> list = GetBarListSummaries();

            int i;

            for (i = 0; i < list.Count; i++)
            {
                grid.Rows.Add(
                    list[i].Status,
                    list[i].Title,
                    list[i].CreatedDate,
                    list[i].ModifiedDate,
                    list[i].RowCount.ToString(),
                    list[i].TotalQty.ToString("0.###"),
                    list[i].TotalLength.ToString("0.###"),
                    list[i].TotalWeight.ToString("0.###"),
                    list[i].Writer,
                    list[i].Memo,
                    list[i].FilePath
                );
            }

            lblStatus.Text = "저장된 BarList: " + list.Count.ToString() + "건";
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

            list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
            {
                DateTime at;
                DateTime bt;

                DateTime.TryParse(a.ModifiedDate, out at);
                DateTime.TryParse(b.ModifiedDate, out bt);

                return bt.CompareTo(at);
            });

            return list;
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

        private void RefreshButton_Click(object sender, EventArgs e)
        {
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

            Color fillColor = hover ? OviaFluentTheme.AccentHover : StartColor;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, 7))
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                OviaFluentTheme.FontButton(9F, FontStyle.Bold),
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
