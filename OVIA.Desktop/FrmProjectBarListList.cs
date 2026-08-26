using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private OviaProjectContextHeader projectContextHeader;
        private ToolTip windowToolTip;
        private OviaSearchBox txtBarListSearch;
        private OviaSelectBox cboBarListSort;
        private OviaSelectBox cboStatusFilter;
        private OviaSelectBox cboWriteFilter;
        private OviaSelectBox cboBuildingFilter;
        private OviaSelectBox cboFloorFilter;
        private OviaSelectBox cboWorkTypeFilter;
        private OviaSelectBox cboShippingFilter;
        private bool suppressFilterEvents = false;

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
        private OviaContentLoadingOverlay contentLoadingOverlay;
        private bool isScrollResetQueued = false;
        private bool isInternalNavigation = false;
        private bool isBackNavigationQueued = false;
        private List<ProjectBarListSummary> currentBarListRows = new List<ProjectBarListSummary>();
        private int pageSize = 100;
        private int currentPage = 1;
        private string headerSortColumn = "";
        private bool headerSortAscending = true;
        private readonly Color GridSelectedRowBack = Color.FromArgb(255, 248, 205);

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
            this.Shown += async delegate
            {
                await SynchronizeBarListsFromErpAsync(false);
            };
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
            BuildActionBar(contentPanel);
            BuildProjectInfo(contentPanel);
            BuildToolbar(contentPanel);
            BuildGrid(contentPanel);
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
            label.BackColor = Color.Transparent;
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

        private void BuildActionBar(Control parent)
        {
            Panel actionPanel = new Panel();
            actionPanel.Location = new Point(34, 110);
            actionPanel.Size = new Size(1108, 38);
            actionPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionPanel.BackColor = SurfaceColor;
            parent.Controls.Add(actionPanel);

            OVIA.Desktop.Controls.OviaButton newButton = new OVIA.Desktop.Controls.OviaButton();
            newButton.Text = "신규등록";
            newButton.Location = new Point(0, 2);
            newButton.Size = OviaFluentTheme.MeasureButtonSize(newButton.Text);
            newButton.Role = OVIA.Desktop.OviaButtonRole.Primary;
            newButton.Click += NewButton_Click;
            actionPanel.Controls.Add(newButton);

            OviaExcelActionButton excelButton = new OviaExcelActionButton();
            excelButton.Text = "엑셀저장";
            excelButton.Location = new Point(newButton.Right + 8, 2);
            excelButton.Size = OviaFluentTheme.MeasureButtonSize(excelButton.Text);
            excelButton.Click += ExcelSaveButton_Click;
            actionPanel.Controls.Add(excelButton);
        }


        private void BuildProjectInfo(Control parent)
        {
            projectContextHeader = new OviaProjectContextHeader();
            projectContextHeader.Location = new Point(34, 156);
            projectContextHeader.Size = new Size(1108, 58);
            projectContextHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            projectContextHeader.SetContext(projectNo, projectName, "", "", "", clientName, projectStatus);
            parent.Controls.Add(projectContextHeader);
        }

        private void BuildToolbar(Control parent)
        {
            OviaProjectBarListCard card = new OviaProjectBarListCard();
            card.Location = new Point(34, 228);
            card.Size = new Size(1108, 108);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            txtBarListSearch = new OviaSearchBox();
            txtBarListSearch.PlaceholderText = "제목 / 발주번호 / 태그 검색";
            txtBarListSearch.Location = new Point(22, 18);
            txtBarListSearch.Size = new Size(292, OviaFluentTheme.CommonInputHeight);
            txtBarListSearch.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            txtBarListSearch.TextChanged += BarListFilter_Changed;
            card.Controls.Add(txtBarListSearch);

            cboBarListSort = CreateFilterSelectBox(new Point(328, 18), new Size(132, OviaFluentTheme.CommonInputHeight), new string[] { "최근등록순", "수정일순", "제목순", "발주일순", "납기일순" });
            card.Controls.Add(cboBarListSort);

            cboStatusFilter = CreateFilterSelectBox(new Point(470, 18), new Size(118, OviaFluentTheme.CommonInputHeight), new string[] { "상태전체", "접수", "미전송", "전송" });
            card.Controls.Add(cboStatusFilter);

            cboWriteFilter = CreateFilterSelectBox(new Point(598, 18), new Size(118, OviaFluentTheme.CommonInputHeight), new string[] { "작성전체", "공장", "현장" });
            card.Controls.Add(cboWriteFilter);

            cboShippingFilter = CreateFilterSelectBox(new Point(726, 18), new Size(118, OviaFluentTheme.CommonInputHeight), new string[] { "출하전체", "출하", "미출하" });
            card.Controls.Add(cboShippingFilter);

            OVIA.Desktop.Controls.OviaButton resetButton = new OVIA.Desktop.Controls.OviaButton();
            resetButton.Text = "초기화";
            resetButton.Location = new Point(982, 18);
            resetButton.Size = OviaFluentTheme.MeasureButtonSize(resetButton.Text);
            resetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            resetButton.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            resetButton.Click += ResetFilterButton_Click;
            card.Controls.Add(resetButton);

            cboBuildingFilter = CreateFilterSelectBox(new Point(22, 60), new Size(128, OviaFluentTheme.CommonInputHeight), new string[] { "동 전체" });
            card.Controls.Add(cboBuildingFilter);

            cboFloorFilter = CreateFilterSelectBox(new Point(162, 60), new Size(128, OviaFluentTheme.CommonInputHeight), new string[] { "층 전체" });
            card.Controls.Add(cboFloorFilter);

            cboWorkTypeFilter = CreateFilterSelectBox(new Point(302, 60), new Size(140, OviaFluentTheme.CommonInputHeight), new string[] { "공종 전체", "작성", "공장", "현장" });
            card.Controls.Add(cboWorkTypeFilter);
        }


        private OviaSelectBox CreateFilterSelectBox(Point location, Size size, string[] items)
        {
            OviaSelectBox selectBox = new OviaSelectBox();
            selectBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            selectBox.DropDownStyle = ComboBoxStyle.DropDownList;
            selectBox.Location = location;
            selectBox.Size = size;

            int i;
            for (i = 0; i < items.Length; i++)
            {
                selectBox.Items.Add(items[i]);
            }

            if (selectBox.Items.Count > 0)
            {
                selectBox.SelectedIndex = 0;
            }

            selectBox.SelectedIndexChanged += BarListFilter_Changed;
            return selectBox;
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 358);
            grid.Size = new Size(1108, 238);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars = ScrollBars.Both;
            grid.ShowCellToolTips = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellPainting += Grid_CellPainting;
            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            grid.Resize += Grid_Resize;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(9F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = GridSelectedRowBack;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 30;

            OviaFluentTheme.ApplyDataGrid(grid);
            grid.DefaultCellStyle.SelectionBackColor = GridSelectedRowBack;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;

            AddColumn("No.", 52);
            AddColumn("상태", 58);
            AddColumn("작성", 58);
            AddColumn("발주번호", 96);
            AddColumn("발주일", 68);
            AddColumn("등록일", 68);
            AddColumn("납기일", 68);
            AddColumn("동", 44);
            AddColumn("층", 44);
            AddColumn("공종", 60);
            AddColumn("진행", 60);
            AddColumn("제목", 320);
            AddColumn("태그", 80);
            AddColumn("색상", 52);
            AddColumn("주문량", 76);
            AddColumn("태그발행", 68);
            AddColumn("기타", 56);
            AddColumn("장대", 56);
            AddColumn("절단", 56);
            AddColumn("절곡", 56);
            AddColumn("출하", 56);
            AddColumn("미출하", 62);
            AddColumn("작성자", 70);
            AddColumn("비고", 130, false);
            AddColumn("FilePath", 0, false);

            grid.Columns["FilePath"].Visible = false;

            ApplyGridColumnAlignment();
            ApplyResponsiveGridColumnWidths();

            parent.Controls.Add(grid);
        }


        private void ApplyGridColumnAlignment()
        {
            SetColumnAlignment("No.", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("상태", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("작성", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("발주일", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("등록일", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("납기일", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("동", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("층", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("공종", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("진행", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("색상", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("태그발행", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("장대", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("절단", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("절곡", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("출하", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("미출하", DataGridViewContentAlignment.MiddleCenter);
            SetColumnAlignment("작성자", DataGridViewContentAlignment.MiddleCenter);

            SetColumnAlignment("발주번호", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("제목", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("태그", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("기타", DataGridViewContentAlignment.MiddleLeft);
            SetColumnAlignment("비고", DataGridViewContentAlignment.MiddleLeft);

            SetColumnAlignment("주문량", DataGridViewContentAlignment.MiddleRight);
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

        private void AddColumn(string header, int width, bool sortable = true)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = Math.Max(5, width);
            column.Tag = width;
            column.FillWeight = Math.Max(1, width);
            column.MinimumWidth = GetGridColumnMinimumWidth(header, width);
            column.AutoSizeMode = width <= 0
                ? DataGridViewAutoSizeColumnMode.None
                : DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = sortable ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable;
            column.Resizable = DataGridViewTriState.True;
            grid.Columns.Add(column);
        }



        private int GetGridColumnMinimumWidth(string header, int baseWidth)
        {
            if (baseWidth <= 0)
            {
                return 5;
            }

            if (header == "No.") return 38;
            if (header == "상태") return 44;
            if (header == "작성") return 44;
            if (header == "발주번호") return 76;
            if (header == "발주일" || header == "등록일" || header == "납기일") return 54;
            if (header == "동" || header == "층") return 38;
            if (header == "공종" || header == "진행") return 48;
            if (header == "제목") return 280;
            if (header == "태그") return 60;
            if (header == "색상") return 42;
            if (header == "주문량") return 60;
            if (header == "태그발행") return 54;
            if (header == "미출하") return 54;
            if (header == "작성자") return 56;
            if (header == "비고") return 100;

            return Math.Min(baseWidth, 48);
        }

        private void Grid_Resize(object sender, EventArgs e)
        {
            ApplyResponsiveGridColumnWidths();
        }

        private void ApplyResponsiveGridColumnWidths()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return;
            }

            grid.SuspendLayout();

            try
            {
                int i;
                for (i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    int baseWidth = column.Tag is int ? (int)column.Tag : column.Width;

                    column.MinimumWidth = GetGridColumnMinimumWidth(column.Name, baseWidth);
                    column.FillWeight = Math.Max(1, baseWidth);

                    if (column.Visible)
                    {
                        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    }
                    else
                    {
                        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    }
                }
            }
            finally
            {
                grid.ResumeLayout(false);
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (grid == null || e.RowIndex != -1 || e.ColumnIndex < 0)
            {
                return;
            }

            e.Handled = true;
            e.PaintBackground(e.ClipBounds, false);

            Rectangle bounds = e.CellBounds;
            using (SolidBrush brush = new SolidBrush(OviaFluentTheme.HeaderBackground))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (Pen pen = new Pen(OviaFluentTheme.GridLine, 1F))
            {
                e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
                e.Graphics.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom);
            }

            DataGridViewColumn paintedColumn = grid.Columns[e.ColumnIndex];
            string headerText = paintedColumn.HeaderText;
            Font font = grid.ColumnHeadersDefaultCellStyle.Font == null ? OviaFluentTheme.FontData(9F, FontStyle.Bold) : grid.ColumnHeadersDefaultCellStyle.Font;
            Size textSize = TextRenderer.MeasureText(e.Graphics, headerText, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            bool isActiveSortColumn = paintedColumn.SortMode == DataGridViewColumnSortMode.Programmatic
                && string.Equals(paintedColumn.Name, headerSortColumn, StringComparison.OrdinalIgnoreCase);
            int arrowWidth = isActiveSortColumn ? 8 : 0;
            int totalWidth = Math.Min(bounds.Width - 8, textSize.Width + arrowWidth + (arrowWidth > 0 ? 3 : 0));
            int startX = bounds.Left + Math.Max(4, (bounds.Width - totalWidth) / 2);
            Rectangle textRect = new Rectangle(startX, bounds.Top, Math.Max(1, Math.Min(textSize.Width + 2, bounds.Right - startX - 4)), bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                headerText,
                font,
                textRect,
                TextDark,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            );

            if (isActiveSortColumn)
            {
                int arrowX = Math.Min(bounds.Right - 10, startX + textSize.Width + 3);
                int arrowY = bounds.Top + (bounds.Height / 2) - 2;
                DrawSortArrow(e.Graphics, arrowX, arrowY, headerSortAscending);
            }
        }

        private void DrawSortArrow(Graphics graphics, int x, int y, bool ascending)
        {
            Point[] points;
            if (ascending)
            {
                points = new Point[] { new Point(x, y + 5), new Point(x + 4, y), new Point(x + 8, y + 5) };
            }
            else
            {
                points = new Point[] { new Point(x, y), new Point(x + 4, y + 5), new Point(x + 8, y) };
            }

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(142, 148, 158)))
            {
                graphics.FillPolygon(brush, points);
            }
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

        private void BindBarListRows()
        {
            BeginContentLoading();
            try
            {
                grid.Rows.Clear();

            List<ProjectBarListSummary> allRows = GetBarListSummaries();
            RefreshDynamicFilterOptions(allRows);
            currentBarListRows = GetFilteredBarListSummaries(allRows);
            ApplyBarListHeaderSort(currentBarListRows);
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
                // No.는 정렬/검색/페이지 위치에 따라 다시 매기지 않는다.
                // 오래된 BarList=1, 최신 BarList=가장 큰 숫자 계약을 항상 유지한다.
                int displayNumber = currentBarListRows[i].ListNumber;

                int rowIndex = grid.Rows.Add(
                    displayNumber.ToString(),
                    currentBarListRows[i].Status,
                    currentBarListRows[i].WriteStatus,
                    currentBarListRows[i].OrderNumber,
                    FormatDateMonthDay(currentBarListRows[i].OrderDate),
                    FormatDateMonthDay(currentBarListRows[i].CreatedDate),
                    FormatDateMonthDay(currentBarListRows[i].DueDate),
                    currentBarListRows[i].Building,
                    currentBarListRows[i].Floor,
                    currentBarListRows[i].WorkType,
                    currentBarListRows[i].Progress,
                    currentBarListRows[i].Title,
                    currentBarListRows[i].Tags,
                    currentBarListRows[i].Color,
                    FormatNumberForCell(currentBarListRows[i].OrderQty > 0 ? currentBarListRows[i].OrderQty : currentBarListRows[i].TotalQty),
                    currentBarListRows[i].TagIssued,
                    currentBarListRows[i].Etc,
                    currentBarListRows[i].LongBar,
                    currentBarListRows[i].Cutting,
                    currentBarListRows[i].Bending,
                    currentBarListRows[i].Shipped,
                    currentBarListRows[i].NotShipped,
                    currentBarListRows[i].Writer,
                    currentBarListRows[i].Memo,
                    currentBarListRows[i].FilePath
                );

                grid.Rows[rowIndex].Cells["발주일"].ToolTipText = "발주일: " + FormatDateFull(currentBarListRows[i].OrderDate);
                grid.Rows[rowIndex].Cells["등록일"].ToolTipText = "수정일: " + currentBarListRows[i].ModifiedDate;
                grid.Rows[rowIndex].Cells["납기일"].ToolTipText = "납기일: " + FormatDateFull(currentBarListRows[i].DueDate);
                grid.Rows[rowIndex].Cells["제목"].ToolTipText = currentBarListRows[i].Title;
            }

            if (grid.Rows.Count > 0 && grid.SelectedRows.Count == 0)
            {
                grid.ClearSelection();
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells["No."];
            }

            UpdateProjectContextHeaderFromSelection();
            RenderPager();
            UpdateSortGlyph();
            ApplyResponsiveGridColumnWidths();

            int displayCount = Math.Max(0, end - start);
            lblStatus.Text = currentBarListRows.Count > displayCount
                ? "검색 결과: " + currentBarListRows.Count.ToString() + "건 / 현재 표시: " + displayCount.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건"
                : "검색 결과: " + currentBarListRows.Count.ToString() + "건 / 페이지당 " + pageSize.ToString() + "건";
            }
            finally
            {
                EndContentLoading();
            }
        }

        private List<ProjectBarListSummary> GetFilteredBarListSummaries(List<ProjectBarListSummary> source)
        {
            List<ProjectBarListSummary> filtered = new List<ProjectBarListSummary>();

            if (source == null)
            {
                return filtered;
            }

            string keyword = GetSearchText(txtBarListSearch);
            string status = GetSelectedFilterText(cboStatusFilter, "전체");
            string write = GetSelectedFilterText(cboWriteFilter, "전체");
            string building = GetSelectedFilterText(cboBuildingFilter, "전체");
            string floor = GetSelectedFilterText(cboFloorFilter, "전체");
            string workType = GetSelectedFilterText(cboWorkTypeFilter, "전체");
            string shipping = GetSelectedFilterText(cboShippingFilter, "전체");

            int i;
            for (i = 0; i < source.Count; i++)
            {
                ProjectBarListSummary row = source[i];

                if (!MatchesKeyword(row, keyword))
                {
                    continue;
                }

                if (!MatchesSelectedText(row.Status, status))
                {
                    continue;
                }

                if (!MatchesSelectedText(row.WriteStatus, write))
                {
                    continue;
                }

                if (!MatchesSelectedText(row.Building, building))
                {
                    continue;
                }

                if (!MatchesSelectedText(row.Floor, floor))
                {
                    continue;
                }

                if (!MatchesSelectedText(row.WorkType, workType))
                {
                    continue;
                }

                if (!MatchesShipping(row, shipping))
                {
                    continue;
                }

                filtered.Add(row);
            }

            return filtered;
        }


        private bool MatchesKeyword(ProjectBarListSummary row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            if (row == null)
            {
                return false;
            }

            return ContainsText(row.Title, keyword)
                || ContainsText(row.OrderNumber, keyword)
                || ContainsText(row.Tags, keyword)
                || ContainsText(row.Memo, keyword)
                || ContainsText(row.Writer, keyword);
        }

        private bool ContainsText(string source, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            if (source == null)
            {
                source = "";
            }

            return source.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private bool MatchesSelectedText(string source, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected))
            {
                return true;
            }

            return ContainsText(source, selected);
        }

        private bool MatchesShipping(ProjectBarListSummary row, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected))
            {
                return true;
            }

            if (row == null)
            {
                return false;
            }

            if (selected == "출하")
            {
                return !string.IsNullOrWhiteSpace(row.Shipped) || ContainsText(row.Progress, "출하");
            }

            if (selected == "미출하")
            {
                return !string.IsNullOrWhiteSpace(row.NotShipped) || ContainsText(row.Progress, "미출하");
            }

            return true;
        }

        private bool MatchesColor(string source, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected))
            {
                return true;
            }

            if (selected == "미지정")
            {
                return string.IsNullOrWhiteSpace(source);
            }

            return ContainsText(source, selected);
        }

        private string GetSearchText(OviaSearchBox searchBox)
        {
            if (searchBox == null || searchBox.Text == null)
            {
                return "";
            }

            return searchBox.Text.Trim();
        }

        private string GetSelectedFilterText(OviaSelectBox selectBox, string allText)
        {
            if (selectBox == null || selectBox.SelectedItem == null)
            {
                return "";
            }

            string text = selectBox.SelectedItem.ToString();
            if (text == null)
            {
                return "";
            }

            text = text.Trim();
            if (text == "" || text.IndexOf(allText, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                return "";
            }

            return text;
        }


        private void RefreshDynamicFilterOptions(List<ProjectBarListSummary> source)
        {
            if (source == null)
            {
                return;
            }

            suppressFilterEvents = true;
            try
            {
                RefreshSelectItems(cboBuildingFilter, "동 전체", GetDistinctValues(source, "동"));
                RefreshSelectItems(cboFloorFilter, "층 전체", GetDistinctValues(source, "층"));
                RefreshSelectItems(cboWorkTypeFilter, "공종 전체", MergeDefaultAndDistinctValues(new string[] { "작성", "공장", "현장" }, GetDistinctValues(source, "공종")));
            }
            finally
            {
                suppressFilterEvents = false;
            }
        }

        private List<string> MergeDefaultAndDistinctValues(string[] defaults, List<string> values)
        {
            List<string> merged = new List<string>();
            int i;

            if (defaults != null)
            {
                for (i = 0; i < defaults.Length; i++)
                {
                    AddDistinctValue(merged, defaults[i]);
                }
            }

            if (values != null)
            {
                for (i = 0; i < values.Count; i++)
                {
                    AddDistinctValue(merged, values[i]);
                }
            }

            return merged;
        }

        private List<string> GetDistinctValues(List<ProjectBarListSummary> source, string fieldName)
        {
            List<string> values = new List<string>();
            int i;

            for (i = 0; i < source.Count; i++)
            {
                ProjectBarListSummary row = source[i];
                string value = "";

                if (fieldName == "동") value = row.Building;
                else if (fieldName == "층") value = row.Floor;
                else if (fieldName == "공종") value = row.WorkType;
                else if (fieldName == "작성") value = row.WriteStatus;
                else if (fieldName == "태그") value = row.Tags;
                else if (fieldName == "색상") value = row.Color;

                AddDistinctValue(values, value);
            }

            values.Sort(StringComparer.CurrentCultureIgnoreCase);
            return values;
        }

        private void AddDistinctValue(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            value = value.Trim();
            int i;
            for (i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private void RefreshSelectItems(OviaSelectBox selectBox, string allText, List<string> values)
        {
            if (selectBox == null)
            {
                return;
            }

            string previous = selectBox.SelectedItem == null ? allText : selectBox.SelectedItem.ToString();
            selectBox.Items.Clear();
            selectBox.Items.Add(allText);

            if (values != null)
            {
                int i;
                for (i = 0; i < values.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        selectBox.Items.Add(values[i]);
                    }
                }
            }

            int selectedIndex = 0;
            int index;
            for (index = 0; index < selectBox.Items.Count; index++)
            {
                object item = selectBox.Items[index];
                if (item != null && string.Equals(item.ToString(), previous, StringComparison.CurrentCultureIgnoreCase))
                {
                    selectedIndex = index;
                    break;
                }
            }

            selectBox.SelectedIndex = selectedIndex;
        }

        private void BarListFilter_Changed(object sender, EventArgs e)
        {
            if (suppressFilterEvents)
            {
                return;
            }

            if (sender == cboBarListSort)
            {
                headerSortColumn = "";
                headerSortAscending = true;
            }

            currentPage = 1;
            BindBarListRows();
        }

        private void ResetFilterButton_Click(object sender, EventArgs e)
        {
            ResetBarListSearchBox(txtBarListSearch);
            ResetSelectBox(cboBarListSort);
            ResetSelectBox(cboStatusFilter);
            ResetSelectBox(cboWriteFilter);
            ResetSelectBox(cboBuildingFilter);
            ResetSelectBox(cboFloorFilter);
            ResetSelectBox(cboWorkTypeFilter);
            ResetSelectBox(cboShippingFilter);

            currentPage = 1;
            BindBarListRows();
        }


        private void ResetBarListSearchBox(OviaSearchBox searchBox)
        {
            if (searchBox != null)
            {
                searchBox.Text = "";
            }
        }

        private void ResetSelectBox(OviaSelectBox selectBox)
        {
            if (selectBox != null && selectBox.Items.Count > 0)
            {
                selectBox.SelectedIndex = 0;
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

            // No.는 현재 화면 정렬순서가 아니라 BarList의 생성 이력 순번이다.
            // 가장 오래된 BarList를 1로 두고 신규 BarList가 생길수록 숫자가 증가해야 하므로
            // 먼저 오래된 순으로 고정 번호를 부여한 뒤 사용자가 선택한 화면 정렬을 적용한다.
            AssignStableBarListNumbers(list);
            ApplyBarListDefaultSort(list);

            return list;
        }


        private void AssignStableBarListNumbers(List<ProjectBarListSummary> list)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }

            List<ProjectBarListSummary> chronological = new List<ProjectBarListSummary>(list);
            chronological.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
            {
                int result = CompareBarListNumberingOrder(a, b);
                if (result != 0)
                {
                    return result;
                }

                return string.Compare(
                    a == null ? "" : a.FilePath,
                    b == null ? "" : b.FilePath,
                    StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < chronological.Count; i++)
            {
                if (chronological[i] != null)
                {
                    chronological[i].ListNumber = i + 1;
                }
            }
        }

        private int CompareBarListNumberingOrder(ProjectBarListSummary a, ProjectBarListSummary b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            DateTime aCreated;
            DateTime bCreated;
            bool aCreatedOk = DateTime.TryParse(a.CreatedDate, out aCreated);
            bool bCreatedOk = DateTime.TryParse(b.CreatedDate, out bCreated);

            if (aCreatedOk && bCreatedOk)
            {
                int dateResult = aCreated.Date.CompareTo(bCreated.Date);
                if (dateResult != 0) return dateResult;
            }
            else if (aCreatedOk != bCreatedOk)
            {
                return aCreatedOk ? -1 : 1;
            }

            // ERP에서 내려온 BarList는 auto increment idx가 생성 순서를 가장 안정적으로 보존한다.
            int aErpId = OviaErpBarListSyncService.GetPersistedErpBarListId(a.FilePath);
            int bErpId = OviaErpBarListSyncService.GetPersistedErpBarListId(b.FilePath);
            if (aErpId > 0 && bErpId > 0 && aErpId != bErpId)
            {
                return aErpId.CompareTo(bErpId);
            }

            DateTime aFileCreated = GetFileCreationTimeSafe(a.FilePath);
            DateTime bFileCreated = GetFileCreationTimeSafe(b.FilePath);
            int fileResult = aFileCreated.CompareTo(bFileCreated);
            if (fileResult != 0) return fileResult;

            if (aErpId != bErpId)
            {
                if (aErpId <= 0) return -1;
                if (bErpId <= 0) return 1;
                return aErpId.CompareTo(bErpId);
            }

            return 0;
        }

        private DateTime GetFileCreationTimeSafe(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    return File.GetCreationTimeUtc(filePath);
                }
            }
            catch
            {
            }

            return DateTime.MinValue;
        }

        private void ApplyBarListHeaderSort(List<ProjectBarListSummary> list)
        {
            if (list == null || string.IsNullOrWhiteSpace(headerSortColumn))
            {
                return;
            }

            list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
            {
                int result = CompareBarListRows(a, b, headerSortColumn);
                if (result == 0 && headerSortColumn != "No.")
                {
                    // 동일 값에서는 최신 생성 BarList(큰 No.)를 위에 둔다.
                    return b.ListNumber.CompareTo(a.ListNumber);
                }

                return headerSortAscending ? result : -result;
            });
        }

        private void ApplyBarListDefaultSort(List<ProjectBarListSummary> list)
        {
            if (list == null)
            {
                return;
            }

            string sortText = cboBarListSort == null || cboBarListSort.SelectedItem == null ? "최근등록순" : cboBarListSort.SelectedItem.ToString();

            if (sortText == "제목순")
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                });
                return;
            }

            if (sortText == "발주일순")
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    return CompareDateText(b.OrderDate, a.OrderDate);
                });
                return;
            }

            if (sortText == "납기일순")
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    return CompareDateText(b.DueDate, a.DueDate);
                });
                return;
            }

            if (sortText == "수정일순")
            {
                list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
                {
                    return CompareDateText(b.ModifiedDate, a.ModifiedDate);
                });
                return;
            }

            // 최근등록순은 표시용 등록일(yyyy-MM-dd)만으로 정렬하지 않는다.
            // AssignStableBarListNumbers()에서 생성 이력에 따라
            // 오래된 BarList=1, 최신 BarList=가장 큰 No.가 이미 확정되어 있으므로,
            // 기본 최근등록순은 고정 No.를 내림차순으로 표시한다.
            list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
            {
                return b.ListNumber.CompareTo(a.ListNumber);
            });
        }

        private int CompareBarListRows(ProjectBarListSummary a, ProjectBarListSummary b, string columnName)
        {
            if (columnName == "No.")
            {
                return a.ListNumber.CompareTo(b.ListNumber);
            }

            if (columnName == "상태")
            {
                return string.Compare(a.Status, b.Status, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "작성")
            {
                return string.Compare(a.WriteStatus, b.WriteStatus, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "발주번호")
            {
                return string.Compare(a.OrderNumber, b.OrderNumber, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "발주일")
            {
                return CompareDateText(a.OrderDate, b.OrderDate);
            }

            if (columnName == "납기일")
            {
                return CompareDateText(a.DueDate, b.DueDate);
            }

            if (columnName == "동")
            {
                return string.Compare(a.Building, b.Building, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "층")
            {
                return string.Compare(a.Floor, b.Floor, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "공종")
            {
                return string.Compare(a.WorkType, b.WorkType, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "진행")
            {
                return string.Compare(a.Progress, b.Progress, StringComparison.CurrentCultureIgnoreCase);
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

            if (columnName == "태그")
            {
                return string.Compare(a.Tags, b.Tags, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "색상")
            {
                return string.Compare(a.Color, b.Color, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "주문량")
            {
                double aq = a.OrderQty > 0 ? a.OrderQty : a.TotalQty;
                double bq = b.OrderQty > 0 ? b.OrderQty : b.TotalQty;
                return aq.CompareTo(bq);
            }

            if (columnName == "태그발행")
            {
                return string.Compare(a.TagIssued, b.TagIssued, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "기타")
            {
                return string.Compare(a.Etc, b.Etc, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "장대")
            {
                return string.Compare(a.LongBar, b.LongBar, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "절단")
            {
                return string.Compare(a.Cutting, b.Cutting, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "절곡")
            {
                return string.Compare(a.Bending, b.Bending, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "출하")
            {
                return string.Compare(a.Shipped, b.Shipped, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "미출하")
            {
                return string.Compare(a.NotShipped, b.NotShipped, StringComparison.CurrentCultureIgnoreCase);
            }

            if (columnName == "작성자")
            {
                return string.Compare(a.Writer, b.Writer, StringComparison.CurrentCultureIgnoreCase);
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
            summary.Title = "";

            // 등록일은 해당 BarList CSV/ERP의 "등록일" 값을 우선한다.
            // 파일 생성일을 먼저 넣으면 SetFirstCellValue()가 CSV 등록일을 읽지 못하므로
            // CSV를 읽기 전에는 비워 둔다.
            summary.CreatedDate = "";

            // 수정일 hover는 각 BarList CSV 파일 자체의 마지막 실제 저장시각이다.
            summary.ModifiedDate = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm");
            // ERP/CSV 값을 먼저 읽고, 값이 없을 때만 fallback을 적용한다.
            // 기본값을 미리 넣으면 SetFirstCellValue()가 실제 CSV 값을 읽지 못한다.
            summary.Status = "";
            summary.WriteStatus = "";
            summary.Writer = "";
            summary.Memo = "";

            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count > 1)
                {
                    List<string> headers = rows[0];

                    int statusIndex = FindHeaderIndex(headers, "상태");
                    int writeIndex = FindHeaderIndex(headers, "작성");
                    int titleIndex = FindHeaderIndex(headers, "제목");
                    int memoIndex = FindHeaderIndex(headers, "OVIA_BARLIST_MEMO");
                    if (memoIndex < 0)
                    {
                        memoIndex = FindHeaderIndex(headers, "BARLIST_MEMO");
                    }
                    int createdDateIndex = FindHeaderIndex(headers, "등록일");
                    int qtyIndex = FindHeaderIndex(headers, "수량");
                    int totalLengthIndex = FindHeaderIndex(headers, "총길이");
                    int weightIndex = FindHeaderIndex(headers, "중량");
                    int orderQtyIndex = FindHeaderIndex(headers, "주문량");
                    int writerIndex = FindHeaderIndex(headers, "작성자");
                    int orderNumberIndex = FindHeaderIndex(headers, "발주번호");
                    int orderDateIndex = FindHeaderIndex(headers, "발주일");
                    int dueDateIndex = FindHeaderIndex(headers, "납기일");
                    int buildingIndex = FindHeaderIndex(headers, "동");
                    int floorIndex = FindHeaderIndex(headers, "층");
                    int workTypeIndex = FindHeaderIndex(headers, "공종");
                    int progressIndex = FindHeaderIndex(headers, "진행");
                    int tagsIndex = FindHeaderIndex(headers, "태그");
                    int colorIndex = FindHeaderIndex(headers, "색상");
                    int tagIssuedIndex = FindHeaderIndex(headers, "태그발행");
                    int etcIndex = FindHeaderIndex(headers, "기타");
                    int longBarIndex = FindHeaderIndex(headers, "장대");
                    int cuttingIndex = FindHeaderIndex(headers, "절단");
                    int bendingIndex = FindHeaderIndex(headers, "절곡");
                    int shippedIndex = FindHeaderIndex(headers, "출하");
                    int notShippedIndex = FindHeaderIndex(headers, "미출하");

                    int r;

                    for (r = 1; r < rows.Count; r++)
                    {
                        summary.RowCount++;

                        SetFirstCellValue(ref summary.Status, rows[r], statusIndex);
                        SetFirstCellValue(ref summary.WriteStatus, rows[r], writeIndex);
                        SetFirstCellValue(ref summary.Title, rows[r], titleIndex);
                        SetFirstCellValue(ref summary.Memo, rows[r], memoIndex);
                        SetFirstCellValue(ref summary.CreatedDate, rows[r], createdDateIndex);

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

                        // 주문량은 BarList 헤더 메타값이다. ERP pull 시 모든 상세행에 같은 값이
                        // 반복될 수 있으므로 합산하지 않고 첫 유효값만 사용한다.
                        if (summary.OrderQty <= 0 && orderQtyIndex >= 0 && orderQtyIndex < rows[r].Count)
                        {
                            double orderQty = ParseNumber(rows[r][orderQtyIndex]);
                            if (orderQty > 0) summary.OrderQty = orderQty;
                        }

                        SetFirstCellValue(ref summary.Writer, rows[r], writerIndex);

                        SetFirstCellValue(ref summary.OrderNumber, rows[r], orderNumberIndex);
                        SetFirstCellValue(ref summary.OrderDate, rows[r], orderDateIndex);
                        SetFirstCellValue(ref summary.DueDate, rows[r], dueDateIndex);
                        SetFirstCellValue(ref summary.Building, rows[r], buildingIndex);
                        SetFirstCellValue(ref summary.Floor, rows[r], floorIndex);
                        SetFirstCellValue(ref summary.WorkType, rows[r], workTypeIndex);
                        SetFirstCellValue(ref summary.Progress, rows[r], progressIndex);
                        SetFirstCellValue(ref summary.Tags, rows[r], tagsIndex);
                        SetFirstCellValue(ref summary.Color, rows[r], colorIndex);
                        SetFirstCellValue(ref summary.TagIssued, rows[r], tagIssuedIndex);
                        SetFirstCellValue(ref summary.Etc, rows[r], etcIndex);
                        SetFirstCellValue(ref summary.LongBar, rows[r], longBarIndex);
                        SetFirstCellValue(ref summary.Cutting, rows[r], cuttingIndex);
                        SetFirstCellValue(ref summary.Bending, rows[r], bendingIndex);
                        SetFirstCellValue(ref summary.Shipped, rows[r], shippedIndex);
                        SetFirstCellValue(ref summary.NotShipped, rows[r], notShippedIndex);
                    }
                }
            }
            catch
            {
                summary.Memo = "요약 계산 실패";
            }

            if (string.IsNullOrWhiteSpace(summary.Title))
            {
                summary.Title = Path.GetFileNameWithoutExtension(filePath);
            }

            // CSV/ERP 등록일이 없는 과거 로컬 파일만 파일 생성일을 fallback으로 사용한다.
            if (string.IsNullOrWhiteSpace(summary.CreatedDate))
            {
                summary.CreatedDate = File.GetCreationTime(filePath).ToString("yyyy-MM-dd");
            }

            if (string.IsNullOrWhiteSpace(summary.Status)) summary.Status = "접수";
            if (string.IsNullOrWhiteSpace(summary.WriteStatus)) summary.WriteStatus = "공장";
            if (string.IsNullOrWhiteSpace(summary.Writer))
            {
                string currentCompanyId;
                string currentUserId;
                string currentToken;
                string authUrl;
                if (OviaErpAuthenticationService.TryGetCurrentErpWebSso(out currentCompanyId, out currentUserId, out currentToken, out authUrl)
                    && !string.IsNullOrWhiteSpace(currentUserId))
                {
                    summary.Writer = currentUserId.Trim();
                }
                else
                {
                    summary.Writer = Environment.UserName;
                }
            }

            summary.Status = NormalizeBarListStatus(summary.Status);
            summary.WriteStatus = NormalizeWriteLocation(summary.WriteStatus);
            summary.OrderDate = NormalizeDateText(summary.OrderDate);
            summary.DueDate = NormalizeDateText(summary.DueDate);
            summary.CreatedDate = NormalizeDateText(summary.CreatedDate);

            return summary;
        }

        private void SetFirstCellValue(ref string target, List<string> row, int index)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            if (row == null || index < 0 || index >= row.Count || row[index] == null)
            {
                return;
            }

            string value = row[index].Trim();
            if (value != "")
            {
                target = value;
            }
        }

        private string FormatNumberForCell(double value)
        {
            if (Math.Abs(value) < 0.0000001)
            {
                return "";
            }

            return value.ToString("0.###");
        }


        private string NormalizeBarListStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "접수";
            }

            value = value.Trim();
            if (ContainsText(value, "전송") && !ContainsText(value, "미전송")) return "전송";
            if (ContainsText(value, "미전송")) return "미전송";
            if (ContainsText(value, "접수")) return "접수";
            if (ContainsText(value, "완료")) return "전송";
            if (ContainsText(value, "저장")) return "접수";
            return value;
        }

        private string NormalizeWriteLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "공장";
            }

            value = value.Trim();
            if (ContainsText(value, "현장")) return "현장";
            if (ContainsText(value, "공장")) return "공장";
            if (ContainsText(value, "작성완료")) return "공장";
            return value;
        }

        private string NormalizeDateText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            DateTime date;
            if (DateTime.TryParse(value, out date))
            {
                return date.ToString("yyyy-MM-dd");
            }

            return value.Trim();
        }

        private string FormatDateMonthDay(string value)
        {
            DateTime date;
            if (DateTime.TryParse(value, out date))
            {
                return date.ToString("MM-dd");
            }

            return value == null ? "" : value;
        }

        private string FormatDateFull(string value)
        {
            DateTime date;
            if (DateTime.TryParse(value, out date))
            {
                return date.ToString("yyyy-MM-dd");
            }

            return value == null ? "" : value;
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
            BeginNewBarListRegistration();
        }

        /// <summary>
        /// ERP Launch에서도 OVIA 내부 신규등록 버튼과 완전히 동일한 흐름을 재사용합니다.
        /// 등록정보 팝업 -> ERP 헤더 등록 -> Draft에 ERP idx 보관 -> 신규 BarList 화면 순서를 유지합니다.
        /// </summary>
        public async void BeginNewBarListRegistration()
        {
            List<ProjectBarListSummary> allRows = GetBarListSummaries();
            ProjectBarListSummary newSummary = new ProjectBarListSummary();
            newSummary.CreatedDate = DateTime.Today.ToString("yyyy-MM-dd");
            BarListEditResult registration = null;

            using (OviaBarListEditDialog dialog = new OviaBarListEditDialog(
                projectName,
                "",
                newSummary,
                GetDistinctValues(allRows, "작성"),
                GetDistinctValues(allRows, "동"),
                GetDistinctValues(allRows, "층"),
                GetDistinctValues(allRows, "공종"),
                GetDistinctValues(allRows, "태그"),
                GetDistinctValues(allRows, "색상"),
                true))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                registration = dialog.Result;
                // 공사별 BarList의 작성자는 신규등록 당시 OVIA 로그인 ID를 최초 작성자로 고정한다.
                // 상세 BarList 철근행에는 작성자 필드를 노출하지 않으며, 이후 수정 저장에서도 이 값은 바꾸지 않는다.
                registration.Writer = (userId ?? "").Trim();
            }

            // 신규등록은 팝업 확인 시점에 ERP barlist 헤더를 먼저 생성한다.
            // 이 단계에서는 빈 CSV나 가짜 철근 행을 만들지 않는다. ERP가 반환한 idx만
            // 메모리 Draft에 보관하고, 이후 CAD 추출 + 검토 후 저장에서 같은 idx로 상세를 upsert한다.
            OviaErpBarListSyncResult registerSync = await OviaErpBarListSyncService.RegisterNewBarListAsync(
                companyId,
                projectNo,
                registration);

            if (!registerSync.IsSuccess || registerSync.BarListId <= 0)
            {
                string message = string.IsNullOrWhiteSpace(registerSync.Message)
                    ? "ERP에 신규 BarList를 등록하지 못했습니다."
                    : registerSync.Message;

                MessageBox.Show(
                    "신규 BarList를 ERP에 등록하지 못했습니다.\r\n\r\n" + message +
                    "\r\n\r\nERP 연결/API 상태를 확인한 뒤 다시 신규등록해 주세요.",
                    "OVIA ERP 신규등록",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                lblStatus.Text = "ERP 신규등록 실패: " + message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            registration.ErpBarListId = registerSync.BarListId;
            OviaBarListRegistrationDraftStore.Set(companyId, projectNo, registration);
            OviaNotificationStore.AddWorkLog(
                companyId,
                userId,
                "BarList 신규등록",
                "메인  ›  공사관리  ›  공사별 BarList");

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToBarList(projectNo, projectName, clientName, projectStatus, "");
                return;
            }

            FrmBarList form = new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus);
            ShowReplacementWindow(form);
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            OpenSelectedBarList();
        }

        private void EditButton_Click(object sender, EventArgs e)
        {
            OpenSelectedBarList();
        }

        private async void DeleteButton_Click(object sender, EventArgs e)
        {
            string filePath = GetSelectedBarListFilePath();
            if (filePath == "")
            {
                lblStatus.Text = "삭제할 BarList를 선택해주세요.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            if (!File.Exists(filePath))
            {
                lblStatus.Text = "삭제할 BarList 파일이 존재하지 않습니다.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            // 삭제 권한/업무 진행 상태는 OVIA의 로컬 문자열로 추측하지 않는다.
            // 현재 로그인 ERP 토큰으로 삭제 API를 호출하고 ERP가 허용한 경우에만 로컬 파일을 삭제한다.
            DialogResult result = MessageBox.Show(
                "선택한 BarList를 정말 삭제하시겠습니까?\r\n\r\n"
                + "ERP의 삭제 권한과 현재 업무 상태를 확인한 뒤 삭제합니다.\r\n"
                + Path.GetFileName(filePath),
                "OVIA BarList 삭제 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                OviaErpBarListSyncResult sync = await OviaErpBarListSyncService.DeleteBarListAsync(companyId, projectNo, filePath);
                if (!sync.IsSuccess)
                {
                    string message = string.IsNullOrWhiteSpace(sync.Message)
                        ? "ERP에서 이 BarList의 삭제를 허용하지 않았습니다."
                        : sync.Message;

                    MessageBox.Show(
                        message,
                        "OVIA BarList 삭제 불가",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    lblStatus.Text = "ERP 삭제 불가 - 로컬 파일은 보존했습니다: " + message;
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                    return;
                }

                File.Delete(filePath);
                OviaErpBarListSyncService.DeleteLocalShapeDirectory(filePath);
                currentPage = 1;
                BindBarListRows();
                lblStatus.Text = "선택한 BarList를 삭제했습니다.";
                lblStatus.ForeColor = TextSub;
                OviaNotificationStore.AddWorkLog(
                    companyId,
                    userId,
                    "BarList 삭제",
                    "메인  ›  공사관리  ›  공사별 BarList");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "BarList 삭제 실패: " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
            }
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (e.Button == MouseButtons.Right)
            {
                ContextMenuStrip menu = OviaGridContextMenuFactory.CreateMenu(
                    OviaGridContextMenuFactory.CreateItem("수정", delegate { ShowEditSelectedBarListDialog(); }),
                    OviaGridContextMenuFactory.CreateItem("삭제", delegate { DeleteButton_Click(this, EventArgs.Empty); })
                );

                menu.Show(grid, grid.PointToClient(Cursor.Position));
            }
        }

        private void ExcelSaveButton_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "BarList 엑셀 저장";
            dialog.Filter = "Excel 97-2003 통합 문서 (*.xls)|*.xls";
            dialog.FileName = DateTime.Now.ToString("yyyy-MM-dd") + "_" + SanitizeExcelDownloadFileName(projectName) + " BarList.xls";
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                SaveGridToExcelHtml(dialog.FileName);
                lblStatus.Text = "엑셀 파일을 저장했습니다: " + Path.GetFileName(dialog.FileName);
                lblStatus.ForeColor = TextSub;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "엑셀 저장 실패: " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
            }
        }

        private string SanitizeExcelDownloadFileName(string value)
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

            return value.Trim() == "" ? "공사명" : value.Trim();
        }

        private void SaveGridToExcelHtml(string filePath)
        {
            StringBuilder html = new StringBuilder();
            html.AppendLine("<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" /></head><body>");
            html.AppendLine("<table border=\"1\" cellspacing=\"0\" cellpadding=\"3\">");
            html.AppendLine("<tr>");

            int c;
            for (c = 0; c < grid.Columns.Count; c++)
            {
                DataGridViewColumn column = grid.Columns[c];
                if (!column.Visible || column.Name == "FilePath")
                {
                    continue;
                }

                html.Append("<th>");
                html.Append(HtmlEncode(column.HeaderText));
                html.AppendLine("</th>");
            }

            html.AppendLine("</tr>");

            int r;
            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                html.AppendLine("<tr>");
                for (c = 0; c < grid.Columns.Count; c++)
                {
                    DataGridViewColumn column = grid.Columns[c];
                    if (!column.Visible || column.Name == "FilePath")
                    {
                        continue;
                    }

                    object value = grid.Rows[r].Cells[c].Value;
                    html.Append("<td>");
                    html.Append(HtmlEncode(value == null ? "" : value.ToString()));
                    html.AppendLine("</td>");
                }
                html.AppendLine("</tr>");
            }

            html.AppendLine("</table></body></html>");
            File.WriteAllText(filePath, html.ToString(), Encoding.UTF8);
        }

        private string HtmlEncode(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private ProjectBarListSummary GetSelectedBarListSummary()
        {
            string filePath = GetSelectedBarListFilePath();
            if (filePath == "" || currentBarListRows == null)
            {
                return null;
            }

            int i;
            for (i = 0; i < currentBarListRows.Count; i++)
            {
                if (string.Equals(currentBarListRows[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return currentBarListRows[i];
                }
            }

            return null;
        }

        private async void ShowEditSelectedBarListDialog()
        {
            ProjectBarListSummary summary = GetSelectedBarListSummary();
            if (summary == null)
            {
                lblStatus.Text = "수정할 BarList를 선택해주세요.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            List<ProjectBarListSummary> allRows = GetBarListSummaries();
            OviaBarListEditDialog dialog = new OviaBarListEditDialog(
                projectName,
                GetSelectedOrderStepText(),
                summary,
                GetDistinctValues(allRows, "작성"),
                GetDistinctValues(allRows, "동"),
                GetDistinctValues(allRows, "층"),
                GetDistinctValues(allRows, "공종"),
                GetDistinctValues(allRows, "태그"),
                GetDistinctValues(allRows, "색상")
            );

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                List<string> workLogDescriptions = BuildBarListMetadataChangeLogs(summary, dialog.Result);
                UpdateBarListMetadata(summary.FilePath, dialog.Result);
                OviaErpBarListSyncResult sync = await OviaErpBarListSyncService.PushSavedBarListAsync(companyId, projectNo, summary.FilePath);
                currentPage = 1;
                BindBarListRows();
                lblStatus.Text = sync.IsSuccess
                    ? "BarList 정보를 수정하고 ERP와 동기화했습니다."
                    : "BarList 정보는 로컬에 수정했지만 ERP 동기화가 보류되었습니다: " + sync.Message;
                lblStatus.ForeColor = sync.IsSuccess ? TextSub : OviaFluentTheme.Danger;

                WriteBarListMetadataWorkLogs(workLogDescriptions);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "BarList 수정 실패: " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
            }
        }


        private List<string> BuildBarListMetadataChangeLogs(ProjectBarListSummary before, BarListEditResult after)
        {
            List<string> logs = new List<string>();
            if (before == null || after == null) return logs;

            AddChangedFieldLog(logs, before.Title, after.Title, "제목 수정");
            AddChangedFieldLog(logs, before.WriteStatus, after.WriteStatus, "작성 수정");
            AddChangedFieldLog(logs, before.Building, after.Building, "동 수정");
            AddChangedFieldLog(logs, before.Floor, after.Floor, "층 수정");
            AddChangedFieldLog(logs, before.WorkType, after.WorkType, "공종 수정");
            AddChangedFieldLog(logs, before.Tags, after.Tags, "태그 수정");
            AddChangedFieldLog(logs, before.Color, after.Color, "색상 수정");
            AddChangedFieldLog(logs, NormalizeDateText(before.OrderDate), NormalizeDateText(after.OrderDate), "발주일 수정");
            AddChangedFieldLog(logs, NormalizeDateText(before.CreatedDate), NormalizeDateText(after.CreatedDate), "등록일 수정");
            AddChangedFieldLog(logs, NormalizeDateText(before.DueDate), NormalizeDateText(after.DueDate), "납기일 수정");
            AddChangedFieldLog(logs, before.Memo, after.Memo, "비고 수정");
            return logs;
        }

        private void AddChangedFieldLog(List<string> logs, string before, string after, string description)
        {
            if (logs == null) return;
            string left = before == null ? "" : before.Trim();
            string right = after == null ? "" : after.Trim();
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                logs.Add(description);
            }
        }

        private void WriteBarListMetadataWorkLogs(List<string> descriptions)
        {
            if (descriptions == null || descriptions.Count == 0) return;
            const string route = "메인  ›  공사관리  ›  공사별 BarList";
            HashSet<string> sent = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < descriptions.Count; i++)
            {
                string description = descriptions[i] == null ? "" : descriptions[i].Trim();
                if (description == "" || !sent.Add(description)) continue;
                OviaNotificationStore.AddWorkLog(companyId, userId, description, route);
            }
        }

        private string GetSelectedOrderStepText()
        {
            if (grid == null || grid.SelectedRows.Count == 0 || !grid.Columns.Contains("No."))
            {
                return "";
            }

            object value = grid.SelectedRows[0].Cells["No."].Value;
            return value == null ? "" : value.ToString();
        }

        private void UpdateBarListMetadata(string filePath, BarListEditResult result)
        {
            if (result == null)
            {
                return;
            }

            List<List<string>> rows = File.Exists(filePath) ? ReadCsv(filePath) : new List<List<string>>();
            if (rows.Count == 0)
            {
                rows.Add(new List<string>());
            }

            List<string> headers = rows[0];
            EnsureCsvHeader(headers, "제목");
            EnsureCsvHeader(headers, "작성");
            EnsureCsvHeader(headers, "동");
            EnsureCsvHeader(headers, "층");
            EnsureCsvHeader(headers, "공종");
            EnsureCsvHeader(headers, "태그");
            EnsureCsvHeader(headers, "색상");
            EnsureCsvHeader(headers, "발주일");
            EnsureCsvHeader(headers, "등록일");
            EnsureCsvHeader(headers, "납기일");
            EnsureCsvHeader(headers, "OVIA_BARLIST_MEMO");

            if (rows.Count == 1)
            {
                rows.Add(new List<string>());
            }

            int r;
            for (r = 1; r < rows.Count; r++)
            {
                EnsureCsvRowWidth(rows[r], headers.Count);
                SetCsvCell(rows[r], headers, "제목", result.Title);
                SetCsvCell(rows[r], headers, "작성", result.WriteStatus);
                SetCsvCell(rows[r], headers, "동", result.Building);
                SetCsvCell(rows[r], headers, "층", result.Floor);
                SetCsvCell(rows[r], headers, "공종", result.WorkType);
                SetCsvCell(rows[r], headers, "태그", result.Tags);
                SetCsvCell(rows[r], headers, "색상", result.Color);
                SetCsvCell(rows[r], headers, "발주일", result.OrderDate);
                SetCsvCell(rows[r], headers, "등록일", result.CreatedDate);
                SetCsvCell(rows[r], headers, "납기일", result.DueDate);
                SetCsvCell(rows[r], headers, "OVIA_BARLIST_MEMO", result.Memo);
            }

            WriteCsv(filePath, rows);
        }

        private void EnsureCsvHeader(List<string> headers, string header)
        {
            if (headers == null)
            {
                return;
            }

            if (FindHeaderIndex(headers, header) < 0)
            {
                headers.Add(header);
            }
        }

        private void EnsureCsvRowWidth(List<string> row, int width)
        {
            while (row.Count < width)
            {
                row.Add("");
            }
        }

        private void SetCsvCell(List<string> row, List<string> headers, string header, string value)
        {
            int index = FindHeaderIndex(headers, header);
            if (index < 0)
            {
                return;
            }

            EnsureCsvRowWidth(row, headers.Count);
            row[index] = value == null ? "" : value;
        }

        private void WriteCsv(string filePath, List<List<string>> rows)
        {
            StringBuilder builder = new StringBuilder();
            int r;
            for (r = 0; r < rows.Count; r++)
            {
                if (r > 0)
                {
                    builder.AppendLine();
                }

                List<string> row = rows[r];
                int c;
                for (c = 0; c < row.Count; c++)
                {
                    if (c > 0)
                    {
                        builder.Append(',');
                    }
                    builder.Append(EscapeCsv(row[c]));
                }
            }

            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
        }

        private string EscapeCsv(string value)
        {
            if (value == null)
            {
                value = "";
            }

            bool needsQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            value = value.Replace("\"", "\"\"");
            return needsQuote ? "\"" + value + "\"" : value;
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            UpdateProjectContextHeaderFromSelection();
        }

        private void UpdateProjectContextHeaderFromSelection()
        {
            if (projectContextHeader == null)
            {
                return;
            }

            ProjectBarListSummary summary = GetSelectedBarListSummary();

            if (summary == null)
            {
                projectContextHeader.SetContext(projectNo, projectName, "", "", "", clientName, projectStatus);
                return;
            }

            projectContextHeader.SetContext(
                projectNo,
                projectName,
                summary.OrderNumber,
                FormatDateFull(summary.DueDate),
                summary.Title,
                clientName,
                projectStatus
            );
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OpenSelectedBarList();
        }

        private string GetSelectedBarListFilePath()
        {
            if (grid == null || grid.SelectedRows.Count == 0)
            {
                return "";
            }

            if (!grid.Columns.Contains("FilePath"))
            {
                return "";
            }

            object value = grid.SelectedRows[0].Cells["FilePath"].Value;
            if (value == null || value.ToString().Trim() == "")
            {
                return "";
            }

            return value.ToString();
        }

        private void OpenSelectedBarList()
        {
            string filePath = GetSelectedBarListFilePath();

            if (filePath == "")
            {
                lblStatus.Text = "열 BarList를 선택해주세요.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

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

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            string columnName = column.Name;
            if (string.IsNullOrWhiteSpace(columnName) || column.SortMode != DataGridViewColumnSortMode.Programmatic)
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
                DataGridViewColumn sortedColumn = grid.Columns[headerSortColumn];
                if (sortedColumn.SortMode == DataGridViewColumnSortMode.Programmatic)
                {
                    sortedColumn.HeaderCell.SortGlyphDirection = headerSortAscending ? SortOrder.Ascending : SortOrder.Descending;
                }
            }

            grid.Invalidate();
        }


        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            headerSortColumn = "";
            headerSortAscending = true;
            currentPage = 1;
            await SynchronizeBarListsFromErpAsync(true);
        }

        private async System.Threading.Tasks.Task SynchronizeBarListsFromErpAsync(bool showFailure)
        {
            BeginContentLoading();
            try
            {
                OviaErpBarListSyncResult sync = await OviaErpBarListSyncService.PullProjectBarListsAsync(
                    companyId,
                    projectNo,
                    projectName,
                    GetProjectBarListDirectory());

                BindBarListRows();

                if (sync.IsSuccess)
                {
                    lblStatus.Text = "ERP BarList 동기화 완료";
                    lblStatus.ForeColor = TextSub;
                }
                else if (showFailure)
                {
                    lblStatus.Text = "ERP 동기화 실패 - 로컬 목록을 유지합니다: " + sync.Message;
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                }
            }
            finally
            {
                EndContentLoading();
            }
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


    public class BarListEditResult
    {
        // 신규등록 팝업에서 ERP가 먼저 생성한 barlist.idx.
        // 로컬 정식 CSV를 만들기 전까지만 메모리 Draft에서 유지한다.
        public int ErpBarListId = 0;
        public string Title = "";
        public string WriteStatus = "";
        public string Building = "";
        public string Floor = "";
        public string WorkType = "";
        public string Tags = "";
        public string Color = "";
        public string OrderDate = "";
        public string CreatedDate = "";
        public string DueDate = "";
        // 메인 > 공사관리 > 공사별 BarList의 최초 작성자(OVIA 로그인 ID).
        public string Writer = "";
        public string Memo = "";
    }

    internal static class OviaBarListRegistrationDraftStore
    {
        private static readonly Dictionary<string, BarListEditResult> Drafts = new Dictionary<string, BarListEditResult>(StringComparer.OrdinalIgnoreCase);

        private static string BuildKey(string companyId, string projectNo)
        {
            return (companyId == null ? "" : companyId.Trim()) + "|" + (projectNo == null ? "" : projectNo.Trim());
        }

        public static void Set(string companyId, string projectNo, BarListEditResult source)
        {
            if (source == null)
            {
                return;
            }

            Drafts[BuildKey(companyId, projectNo)] = Clone(source);
        }

        public static BarListEditResult Get(string companyId, string projectNo)
        {
            BarListEditResult value;

            if (!Drafts.TryGetValue(BuildKey(companyId, projectNo), out value) || value == null)
            {
                return null;
            }

            return Clone(value);
        }

        public static void Clear(string companyId, string projectNo)
        {
            Drafts.Remove(BuildKey(companyId, projectNo));
        }

        private static BarListEditResult Clone(BarListEditResult source)
        {
            BarListEditResult copy = new BarListEditResult();
            copy.ErpBarListId = source.ErpBarListId;
            copy.Title = source.Title;
            copy.WriteStatus = source.WriteStatus;
            copy.Building = source.Building;
            copy.Floor = source.Floor;
            copy.WorkType = source.WorkType;
            copy.Tags = source.Tags;
            copy.Color = source.Color;
            copy.OrderDate = source.OrderDate;
            copy.CreatedDate = source.CreatedDate;
            copy.DueDate = source.DueDate;
            copy.Writer = source.Writer;
            copy.Memo = source.Memo;
            return copy;
        }
    }

    public class OviaBarListEditDialog : Form
    {
        private readonly string projectName;
        private readonly string orderStep;
        private readonly ProjectBarListSummary summary;
        private Label lblProjectNameValue;
        private OviaDialogTextBox txtTitle;
        private OviaEditableSelectBox cboWrite;
        private OviaEditableSelectBox cboBuilding;
        private OviaEditableSelectBox cboFloor;
        private OviaEditableSelectBox cboWorkType;
        private OviaEditableSelectBox cboTags;
        private OviaEditableSelectBox cboColor;
        private OviaSimpleDatePicker dtOrderDate;
        private OviaSimpleDatePicker dtCreatedDate;
        private OviaSimpleDatePicker dtDueDate;
        private OviaDialogTextBox txtMemo;
        private OVIA.Desktop.Controls.OviaButton btnCadTitleText;
        private OVIA.Desktop.Controls.OviaButton btnOk;
        private ToolTip cadTitleToolTip;
        private Timer cadTitleResultTimer;
        private string pendingCadTitleRequestToken = "";
        private DateTime cadTitleRequestStartedAt = DateTime.MinValue;
        private bool cadTitleRequestPending = false;
        private string originalSnapshot = "";
        private readonly bool isNewRegistration;

        public BarListEditResult Result = new BarListEditResult();

        public OviaBarListEditDialog(string projectName, string orderStep, ProjectBarListSummary summary, List<string> writes, List<string> buildings, List<string> floors, List<string> workTypes, List<string> tags, List<string> colors)
            : this(projectName, orderStep, summary, writes, buildings, floors, workTypes, tags, colors, false)
        {
        }

        public OviaBarListEditDialog(string projectName, string orderStep, ProjectBarListSummary summary, List<string> writes, List<string> buildings, List<string> floors, List<string> workTypes, List<string> tags, List<string> colors, bool isNewRegistration)
        {
            this.projectName = projectName == null ? "" : projectName;
            this.orderStep = orderStep == null ? "" : orderStep;
            this.summary = summary == null ? new ProjectBarListSummary() : summary;
            this.isNewRegistration = isNewRegistration;

            this.Text = this.isNewRegistration
                ? "BarList 신규등록 - " + this.projectName
                : "BarList 수정 - 주문차수 : " + this.orderStep + " | 발주번호 : " + this.summary.OrderNumber + " | " + this.summary.Title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowIcon = false;
            this.ClientSize = new Size(720, 560);
            this.BackColor = OviaFluentTheme.AppBackground;
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);

            BuildDialog(writes, buildings, floors, workTypes, tags, colors);
            this.FormClosed += OviaBarListEditDialog_FormClosed;
            CaptureOriginalSnapshot();
            UpdateConfirmState();
        }

        private void BuildDialog(List<string> writes, List<string> buildings, List<string> floors, List<string> workTypes, List<string> tags, List<string> colors)
        {
            writes = EnsureBaseItems(writes, new string[] { "공장", "현장" });

            OviaRoundedDialogPanel body = new OviaRoundedDialogPanel();
            body.Location = new Point(24, 22);
            body.Size = new Size(672, 456);
            body.SurfaceColor = OviaFluentTheme.AppBackground;
            body.BackColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(body);

            int labelWidth = 82;
            int gap = 18;
            int itemWidth = 266;
            int rowHeight = 42;
            int left1 = 24;
            int left2 = 24 + labelWidth + itemWidth + gap;
            int top = 22;

            Label lblProject = CreateFieldLabel("공사명", left1, top, labelWidth, 34);
            body.Controls.Add(lblProject);
            lblProjectNameValue = new Label();
            lblProjectNameValue.Text = projectName;
            lblProjectNameValue.AutoSize = false;
            lblProjectNameValue.Location = new Point(left1 + labelWidth, top);
            lblProjectNameValue.Size = new Size(body.Width - left1 - labelWidth - 24, 34);
            lblProjectNameValue.TextAlign = ContentAlignment.MiddleLeft;
            lblProjectNameValue.Font = OviaFluentTheme.FontInput(10F, FontStyle.Bold);
            lblProjectNameValue.ForeColor = OviaFluentTheme.TextPrimary;
            lblProjectNameValue.BackColor = Color.Transparent;
            body.Controls.Add(lblProjectNameValue);

            top += rowHeight + 4;
            Label lblTitle = CreateFieldLabel("제목", left1, top, labelWidth, 34);
            body.Controls.Add(lblTitle);

            int titleAreaWidth = body.Width - left1 - labelWidth - 24;
            int titleIconSize = 34;
            int titleIconGap = 6;
            txtTitle = CreateTextBox(summary.Title, left1 + labelWidth, top, titleAreaWidth - titleIconSize - titleIconGap, 34);
            txtTitle.TextChanged += AnyValueChanged;
            body.Controls.Add(txtTitle);

            btnCadTitleText = new OVIA.Desktop.Controls.OviaButton();
            btnCadTitleText.Text = "\uF87C";
            btnCadTitleText.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            btnCadTitleText.MinimumSize = new Size(titleIconSize, titleIconSize);
            btnCadTitleText.Size = new Size(titleIconSize, titleIconSize);
            btnCadTitleText.Location = new Point(left1 + labelWidth + titleAreaWidth - titleIconSize, top);
            btnCadTitleText.Font = OviaIconFont.Create(13.5F, FontStyle.Regular);
            btnCadTitleText.TabStop = true;
            btnCadTitleText.AccessibleName = "CAD에서 제목 텍스트 가져오기";
            btnCadTitleText.Click += BtnCadTitleText_Click;
            body.Controls.Add(btnCadTitleText);

            cadTitleToolTip = new ToolTip();
            cadTitleToolTip.SetToolTip(btnCadTitleText, "CAD에서 제목 텍스트 가져오기");

            cadTitleResultTimer = new Timer();
            cadTitleResultTimer.Interval = 250;
            cadTitleResultTimer.Tick += CadTitleResultTimer_Tick;

            top += rowHeight;
            AddComboPair(body, "작성", ref cboWrite, writes, summary.WriteStatus, left1, top, labelWidth, itemWidth, "동", ref cboBuilding, buildings, summary.Building, left2, top);

            top += rowHeight;
            AddComboPair(body, "층", ref cboFloor, floors, summary.Floor, left1, top, labelWidth, itemWidth, "공종", ref cboWorkType, workTypes, summary.WorkType, left2, top);

            top += rowHeight;
            AddComboPair(body, "태그기호", ref cboTags, tags, summary.Tags, left1, top, labelWidth, itemWidth, "태그색상", ref cboColor, colors, summary.Color, left2, top);

            top += rowHeight;
            Label lblOrderDate = CreateFieldLabel("발주일", left1, top, labelWidth, 34);
            body.Controls.Add(lblOrderDate);
            dtOrderDate = CreateDatePicker(summary.OrderDate, left1 + labelWidth, top, itemWidth - labelWidth, 34, true);
            body.Controls.Add(dtOrderDate);

            Label lblCreatedDate = CreateFieldLabel("등록일", left2, top, labelWidth, 34);
            body.Controls.Add(lblCreatedDate);
            dtCreatedDate = CreateDatePicker(summary.CreatedDate, left2 + labelWidth, top, itemWidth - labelWidth, 34, false);
            body.Controls.Add(dtCreatedDate);

            top += rowHeight;
            Label lblDueDate = CreateFieldLabel("납기일", left1, top, labelWidth, 34);
            body.Controls.Add(lblDueDate);
            dtDueDate = CreateDatePicker(summary.DueDate, left1 + labelWidth, top, itemWidth - labelWidth, 34, true);
            body.Controls.Add(dtDueDate);

            top += rowHeight + 6;
            Label lblMemo = CreateFieldLabel("비고", left1, top, labelWidth, 34);
            body.Controls.Add(lblMemo);
            txtMemo = CreateTextBox(summary.Memo, left1 + labelWidth, top, body.Width - left1 - labelWidth - 24, 112);
            txtMemo.Multiline = true;
            txtMemo.ScrollBars = ScrollBars.Vertical;
            txtMemo.AcceptsReturn = true;
            txtMemo.TextChanged += AnyValueChanged;
            body.Controls.Add(txtMemo);

            OVIA.Desktop.Controls.OviaButton btnCancel = new OVIA.Desktop.Controls.OviaButton();
            btnCancel.Text = "취소";
            btnCancel.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            btnCancel.Size = OviaFluentTheme.MeasureButtonSize(btnCancel.Text);
            btnCancel.Location = new Point((this.ClientSize.Width / 2) - btnCancel.Width - 8, 500);
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; Close(); };
            this.Controls.Add(btnCancel);

            btnOk = new OVIA.Desktop.Controls.OviaButton();
            btnOk.Text = "저장";
            btnOk.Role = OVIA.Desktop.OviaButtonRole.Primary;
            btnOk.Size = OviaFluentTheme.MeasureButtonSize(btnOk.Text);
            btnOk.Location = new Point((this.ClientSize.Width / 2) + 8, 500);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);
        }

        private void BtnCadTitleText_Click(object sender, EventArgs e)
        {
            if (cadTitleRequestPending)
            {
                return;
            }

            string requestToken;
            string errorMessage;

            if (!OviaCadTitleTextBridge.TryBeginRequest(out requestToken, out errorMessage))
            {
                MessageBox.Show(
                    errorMessage,
                    "OVIA CAD 제목 가져오기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            pendingCadTitleRequestToken = requestToken;
            cadTitleRequestStartedAt = DateTime.Now;
            cadTitleRequestPending = true;
            SetCadTitleButtonWaiting(true);

            if (cadTitleResultTimer != null)
            {
                cadTitleResultTimer.Start();
            }

            System.Threading.Thread commandThread = new System.Threading.Thread(
                delegate()
                {
                    string commandError;
                    bool success = OviaCadTitleTextBridge.TrySendAutoCadCommand("OVIATITLETEXT", out commandError);

                    try
                    {
                        this.BeginInvoke(new MethodInvoker(delegate
                        {
                            if (this.IsDisposed || !cadTitleRequestPending)
                            {
                                return;
                            }

                            if (!success)
                            {
                                FinishCadTitleRequest();
                                OviaCadTitleTextBridge.CleanupRequestFiles();
                                MessageBox.Show(
                                    commandError,
                                    "OVIA CAD 제목 가져오기",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning
                                );
                                return;
                            }

                            OviaCadTitleTextBridge.BringAutoCadToFront();
                        }));
                    }
                    catch
                    {
                    }
                }
            );

            commandThread.IsBackground = true;
            commandThread.SetApartmentState(System.Threading.ApartmentState.STA);
            commandThread.Start();
        }

        private void CadTitleResultTimer_Tick(object sender, EventArgs e)
        {
            if (!cadTitleRequestPending)
            {
                return;
            }

            if (cadTitleRequestStartedAt != DateTime.MinValue
                && DateTime.Now - cadTitleRequestStartedAt > TimeSpan.FromMinutes(5))
            {
                FinishCadTitleRequest();
                OviaCadTitleTextBridge.CleanupRequestFiles();
                MessageBox.Show(
                    "CAD 텍스트 선택 시간이 초과되었습니다. 다시 시도해 주세요.",
                    "OVIA CAD 제목 가져오기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string status;
            string titleText;
            string readError;

            if (!OviaCadTitleTextBridge.TryReadResult(
                pendingCadTitleRequestToken,
                out status,
                out titleText,
                out readError))
            {
                return;
            }

            FinishCadTitleRequest();
            OviaCadTitleTextBridge.CleanupRequestFiles();

            if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                string normalizedTitle = OviaCadTitleTextBridge.NormalizeTitle(titleText);
                if (normalizedTitle == "")
                {
                    MessageBox.Show(
                        "선택한 객체에서 제목 텍스트를 읽지 못했습니다. TEXT 또는 MTEXT를 선택해 주세요.",
                        "OVIA CAD 제목 가져오기",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    RestoreDialogAfterCadSelection();
                    return;
                }

                txtTitle.Text = normalizedTitle;
                txtTitle.Focus();
                RestoreDialogAfterCadSelection();
                return;
            }

            if (string.Equals(status, "CANCEL", StringComparison.OrdinalIgnoreCase))
            {
                RestoreDialogAfterCadSelection();
                return;
            }

            MessageBox.Show(
                readError == "" ? "CAD에서 제목 텍스트를 가져오지 못했습니다." : readError,
                "OVIA CAD 제목 가져오기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            RestoreDialogAfterCadSelection();
        }

        private void SetCadTitleButtonWaiting(bool waiting)
        {
            if (btnCadTitleText == null)
            {
                return;
            }

            btnCadTitleText.Enabled = !waiting;
            btnCadTitleText.Cursor = waiting ? Cursors.WaitCursor : Cursors.Hand;
            btnCadTitleText.Text = waiting ? "…" : "T";

            if (cadTitleToolTip != null)
            {
                cadTitleToolTip.SetToolTip(
                    btnCadTitleText,
                    waiting ? "AutoCAD에서 텍스트를 선택한 후 Enter를 누르세요" : "CAD에서 제목 텍스트 가져오기"
                );
            }
        }

        private void FinishCadTitleRequest()
        {
            cadTitleRequestPending = false;
            pendingCadTitleRequestToken = "";
            cadTitleRequestStartedAt = DateTime.MinValue;

            if (cadTitleResultTimer != null)
            {
                cadTitleResultTimer.Stop();
            }

            SetCadTitleButtonWaiting(false);
        }

        private void RestoreDialogAfterCadSelection()
        {
            try
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.WindowState = FormWindowState.Normal;
                }

                this.Show();
                this.BringToFront();
                this.Activate();
            }
            catch
            {
            }
        }

        private void OviaBarListEditDialog_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (cadTitleResultTimer != null)
            {
                cadTitleResultTimer.Stop();
                cadTitleResultTimer.Dispose();
                cadTitleResultTimer = null;
            }

            if (cadTitleToolTip != null)
            {
                cadTitleToolTip.Dispose();
                cadTitleToolTip = null;
            }

            OviaCadTitleTextBridge.CleanupRequestFiles();
        }

        private void Body_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, 10))
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1F))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private List<string> EnsureBaseItems(List<string> source, string[] baseItems)
        {
            List<string> result = new List<string>();
            int i;

            if (baseItems != null)
            {
                for (i = 0; i < baseItems.Length; i++)
                {
                    AddUniqueText(result, baseItems[i]);
                }
            }

            if (source != null)
            {
                for (i = 0; i < source.Count; i++)
                {
                    AddUniqueText(result, source[i]);
                }
            }

            return result;
        }

        private void AddUniqueText(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            value = value.Trim();
            int i;
            for (i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }
            }

            list.Add(value);
        }

        private void AddComboPair(Panel body, string label1, ref OviaEditableSelectBox combo1, List<string> items1, string value1, int x1, int y, int labelWidth, int itemWidth, string label2, ref OviaEditableSelectBox combo2, List<string> items2, string value2, int x2, int y2)
        {
            body.Controls.Add(CreateFieldLabel(label1, x1, y, labelWidth, 34));
            combo1 = CreateEditableCombo(items1, value1, x1 + labelWidth, y, itemWidth - labelWidth, 34);
            body.Controls.Add(combo1);

            body.Controls.Add(CreateFieldLabel(label2, x2, y2, labelWidth, 34));
            combo2 = CreateEditableCombo(items2, value2, x2 + labelWidth, y2, itemWidth - labelWidth, 34);
            body.Controls.Add(combo2);
        }

        private Label CreateFieldLabel(string text, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontInput(9.4F, FontStyle.Bold);
            label.ForeColor = OviaFluentTheme.TextSecondary;
            label.BackColor = Color.Transparent;
            return label;
        }

        private OviaDialogTextBox CreateTextBox(string value, int x, int y, int width, int height)
        {
            OviaDialogTextBox textBox = new OviaDialogTextBox();
            textBox.Text = value == null ? "" : value;
            textBox.Location = new Point(x, y);
            textBox.Size = new Size(width, height);
            textBox.Font = OviaFluentTheme.FontInput(9.8F, FontStyle.Regular);
            textBox.SurfaceColor = Color.White;
            return textBox;
        }

        private OviaEditableSelectBox CreateEditableCombo(List<string> items, string value, int x, int y, int width, int height)
        {
            OviaEditableSelectBox combo = new OviaEditableSelectBox();
            combo.Location = new Point(x, y);
            combo.Size = new Size(width, height);
            combo.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            combo.SurfaceColor = Color.White;

            int i;
            if (items != null)
            {
                for (i = 0; i < items.Count; i++)
                {
                    AddComboItem(combo, items[i]);
                }
            }
            AddComboItem(combo, value);
            combo.Text = value == null ? "" : value;
            combo.TextChanged += AnyValueChanged;
            combo.SelectedIndexChanged += AnyValueChanged;
            return combo;
        }

        private void AddComboItem(OviaEditableSelectBox combo, string value)
        {
            if (combo == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            combo.AddItem(value.Trim());
        }

        private string GetComboText(OviaEditableSelectBox combo)
        {
            return combo == null || combo.Text == null ? "" : combo.Text.Trim();
        }

        private OviaSimpleDatePicker CreateDatePicker(string value, int x, int y, int width, int height, bool enabled)
        {
            OviaSimpleDatePicker picker = new OviaSimpleDatePicker();
            picker.Location = new Point(x, y);
            picker.Size = new Size(width, height);
            picker.Value = ParseDateOrToday(value);
            picker.Enabled = enabled;
            picker.ValueChanged += AnyValueChanged;
            return picker;
        }

        private DateTime ParseDateOrToday(string value)
        {
            DateTime date;
            if (DateTime.TryParse(value, out date))
            {
                return date;
            }
            return DateTime.Today;
        }

        private void CaptureOriginalSnapshot()
        {
            originalSnapshot = BuildSnapshot();
        }

        private string BuildSnapshot()
        {
            return (txtTitle == null ? "" : txtTitle.Text) + "|" +
                GetComboText(cboWrite) + "|" +
                GetComboText(cboBuilding) + "|" +
                GetComboText(cboFloor) + "|" +
                GetComboText(cboWorkType) + "|" +
                GetComboText(cboTags) + "|" +
                GetComboText(cboColor) + "|" +
                dtOrderDate.Value.ToString("yyyy-MM-dd") + "|" +
                dtCreatedDate.Value.ToString("yyyy-MM-dd") + "|" +
                dtDueDate.Value.ToString("yyyy-MM-dd") + "|" +
                (txtMemo == null ? "" : txtMemo.Text);
        }

        private void AnyValueChanged(object sender, EventArgs e)
        {
            UpdateConfirmState();
        }

        private void UpdateConfirmState()
        {
            if (btnOk == null)
            {
                return;
            }

            bool enabled;

            if (isNewRegistration)
            {
                enabled = txtTitle != null && txtTitle.Text.Trim() != "";
            }
            else
            {
                enabled = originalSnapshot != BuildSnapshot();
            }

            btnOk.Enabled = enabled;
            btnOk.Cursor = enabled ? Cursors.Hand : Cursors.Default;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            Result.Title = txtTitle.Text.Trim();
            Result.WriteStatus = GetComboText(cboWrite);
            Result.Building = GetComboText(cboBuilding);
            Result.Floor = GetComboText(cboFloor);
            Result.WorkType = GetComboText(cboWorkType);
            Result.Tags = GetComboText(cboTags);
            Result.Color = GetComboText(cboColor);
            Result.OrderDate = dtOrderDate.Value.ToString("yyyy-MM-dd");
            Result.CreatedDate = dtCreatedDate.Value.ToString("yyyy-MM-dd");
            Result.DueDate = dtDueDate.Value.ToString("yyyy-MM-dd");
            Result.Memo = txtMemo.Text;
            this.DialogResult = DialogResult.OK;
            Close();
        }
    }


    internal static class OviaCadTitleTextBridge
    {
        private const string RequestFileName = "cad_title_text.request";
        private const string ResultFileName = "cad_title_text.result";

        public static bool TryBeginRequest(out string requestToken, out string errorMessage)
        {
            requestToken = "";
            errorMessage = "";

            try
            {
                string bridgeDirectory = GetBridgeDirectory();
                Directory.CreateDirectory(bridgeDirectory);
                CleanupRequestFiles();

                requestToken = Guid.NewGuid().ToString("N");
                File.WriteAllText(GetRequestFilePath(), requestToken, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "CAD 제목 추출 요청 파일을 만들지 못했습니다.\r\n\r\n상세: " + ex.Message;
                return false;
            }
        }

        public static bool TrySendAutoCadCommand(string command, out string errorMessage)
        {
            errorMessage = "";

            if (command == null || command.Trim() == "")
            {
                errorMessage = "실행할 AutoCAD 명령이 지정되지 않았습니다.";
                return false;
            }

            object autoCadApplication = null;
            object activeDocument = null;

            try
            {
                autoCadApplication = Marshal.GetActiveObject("AutoCAD.Application");
                if (autoCadApplication == null)
                {
                    errorMessage = "실행 중인 AutoCAD에 연결하지 못했습니다. AutoCAD와 DWG 도면을 연 뒤 다시 시도해 주세요.";
                    return false;
                }

                activeDocument = autoCadApplication.GetType().InvokeMember(
                    "ActiveDocument",
                    BindingFlags.GetProperty,
                    null,
                    autoCadApplication,
                    null
                );

                if (activeDocument == null)
                {
                    errorMessage = "AutoCAD에서 활성 도면을 찾지 못했습니다. DWG 도면을 연 뒤 다시 시도해 주세요.";
                    return false;
                }

                activeDocument.GetType().InvokeMember(
                    "SendCommand",
                    BindingFlags.InvokeMethod,
                    null,
                    activeDocument,
                    new object[] { command.Trim() + "\r" }
                );

                return true;
            }
            catch (Exception ex)
            {
                Exception detail = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;

                errorMessage = "AutoCAD 제목 추출 명령을 실행하지 못했습니다. 최신 OVIA AutoCAD 플러그인을 NETLOAD했는지 확인해 주세요.\r\n\r\n상세: " + detail.Message;
                return false;
            }
        }

        public static bool TryReadResult(string requestToken, out string status, out string titleText, out string errorMessage)
        {
            status = "";
            titleText = "";
            errorMessage = "";

            string resultPath = GetResultFilePath();
            if (!File.Exists(resultPath))
            {
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(resultPath, Encoding.UTF8);
                if (lines.Length < 3)
                {
                    return false;
                }

                string resultToken = lines[0] == null ? "" : lines[0].Trim();
                if (!string.Equals(resultToken, requestToken, StringComparison.Ordinal))
                {
                    return false;
                }

                status = lines[1] == null ? "" : lines[1].Trim();
                string encodedText = lines[2] == null ? "" : lines[2].Trim();

                if (encodedText != "")
                {
                    byte[] bytes = Convert.FromBase64String(encodedText);
                    titleText = Encoding.UTF8.GetString(bytes);
                }

                if (lines.Length >= 4 && lines[3] != null && lines[3].Trim() != "")
                {
                    byte[] errorBytes = Convert.FromBase64String(lines[3].Trim());
                    errorMessage = Encoding.UTF8.GetString(errorBytes);
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception ex)
            {
                status = "ERROR";
                errorMessage = "CAD 제목 추출 결과를 읽지 못했습니다.\r\n\r\n상세: " + ex.Message;
                return true;
            }
        }

        public static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string normalized = value
                .Replace("\\P", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized.Trim();
        }

        public static void BringAutoCadToFront()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("acad");
                if (processes == null)
                {
                    return;
                }

                int i;
                for (i = 0; i < processes.Length; i++)
                {
                    if (processes[i].MainWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(processes[i].MainWindowHandle);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        public static void CleanupRequestFiles()
        {
            TryDelete(GetRequestFilePath());
            TryDelete(GetResultFilePath());
            TryDelete(GetResultFilePath() + ".tmp");
        }

        private static string GetBridgeDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA",
                "Bridge"
            );
        }

        private static string GetRequestFilePath()
        {
            return Path.Combine(GetBridgeDirectory(), RequestFileName);
        }

        private static string GetResultFilePath()
        {
            return Path.Combine(GetBridgeDirectory(), ResultFileName);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }


    public static class OviaSessionSecurity
    {
        public const int SystemAdministratorLevel = 99;

        private static string currentCompanyId = "";
        private static string currentUserId = "";
        private static string currentPassword = "";
        private static int currentUserLevel = 1;

        public static void SetCurrentLogin(string companyId, string userId, string password, int userLevel)
        {
            currentCompanyId = companyId == null ? "" : companyId.Trim();
            currentUserId = userId == null ? "" : userId.Trim();
            currentPassword = password == null ? "" : password;
            currentUserLevel = NormalizeLoginLevel(userLevel);
        }

        public static bool ValidateCurrentLogin(string companyId, string userId, string password)
        {
            string c = companyId == null ? "" : companyId.Trim();
            string u = userId == null ? "" : userId.Trim();
            string p = password == null ? "" : password;

            if (currentCompanyId == "" || currentUserId == "" || currentPassword == "")
            {
                return false;
            }

            return string.Equals(currentCompanyId, c, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentUserId, u, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentPassword, p, StringComparison.Ordinal);
        }

        public static int GetCurrentUserLevel(string companyId, string userId)
        {
            if (!MatchesCurrentUser(companyId, userId))
            {
                return 1;
            }

            return currentUserLevel;
        }

        public static bool IsCurrentSystemAdministrator(string companyId, string userId)
        {
            return MatchesCurrentUser(companyId, userId)
                && currentUserLevel == SystemAdministratorLevel;
        }

        public static bool IsCurrentLoginUser(string companyId, string userId)
        {
            return MatchesCurrentUser(companyId, userId);
        }

        private static bool MatchesCurrentUser(string companyId, string userId)
        {
            string requestedCompanyId = companyId == null ? "" : companyId.Trim();
            string requestedUserId = userId == null ? "" : userId.Trim();

            if (currentCompanyId == "" || currentUserId == "")
            {
                return false;
            }

            if (requestedCompanyId != "" && !string.Equals(requestedCompanyId, currentCompanyId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (requestedUserId != "" && !string.Equals(requestedUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static int NormalizeLoginLevel(int value)
        {
            if (value == SystemAdministratorLevel) return SystemAdministratorLevel;
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }
    }

    public class OviaBarListDeleteAuthDialog : Form
    {
        private readonly string defaultUserId;
        private readonly ProjectBarListSummary summary;
        private OviaDialogTextBox txtUserId;
        private OviaDialogTextBox txtPassword;
        private OVIA.Desktop.Controls.OviaButton btnOk;

        public string InputUserId = "";
        public string InputPassword = "";

        public OviaBarListDeleteAuthDialog(string defaultUserId, ProjectBarListSummary summary, string fileName)
        {
            this.defaultUserId = defaultUserId == null ? "" : defaultUserId;
            this.summary = summary == null ? new ProjectBarListSummary() : summary;
            this.Text = "BarList 삭제 권한 확인";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowIcon = false;
            this.ClientSize = new Size(430, 286);
            this.BackColor = OviaFluentTheme.AppBackground;
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            BuildDialog(fileName == null ? "" : fileName);
            UpdateOkState();
        }

        private void BuildDialog(string fileName)
        {
            OviaRoundedDialogPanel body = new OviaRoundedDialogPanel();
            body.Location = new Point(20, 20);
            body.Size = new Size(this.ClientSize.Width - 40, 190);
            body.SurfaceColor = OviaFluentTheme.AppBackground;
            body.BackColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(body);

            Label title = new Label();
            title.Text = "선택한 BarList를 삭제하려면 현재 로그인 사용자의 아이디와 비밀번호를 입력하세요.";
            title.Location = new Point(20, 16);
            title.Size = new Size(body.Width - 40, 38);
            title.Font = OviaFluentTheme.FontInput(9.2F, FontStyle.Bold);
            title.ForeColor = OviaFluentTheme.TextPrimary;
            title.BackColor = Color.Transparent;
            body.Controls.Add(title);

            Label target = new Label();
            target.Text = "대상 : " + (string.IsNullOrWhiteSpace(summary.Title) ? fileName : summary.Title);
            target.Location = new Point(20, 58);
            target.Size = new Size(body.Width - 40, 24);
            target.Font = OviaFluentTheme.FontInput(8.8F, FontStyle.Regular);
            target.ForeColor = OviaFluentTheme.TextSecondary;
            target.BackColor = Color.Transparent;
            body.Controls.Add(target);

            Label lblUser = CreateAuthLabel("사용자 아이디", 20, 94);
            body.Controls.Add(lblUser);
            txtUserId = new OviaDialogTextBox();
            txtUserId.Location = new Point(120, 88);
            txtUserId.Size = new Size(body.Width - 145, 34);
            txtUserId.Text = defaultUserId;
            txtUserId.TextChanged += delegate { UpdateOkState(); };
            body.Controls.Add(txtUserId);

            Label lblPassword = CreateAuthLabel("비밀번호", 20, 138);
            body.Controls.Add(lblPassword);
            txtPassword = new OviaDialogTextBox();
            txtPassword.Location = new Point(120, 132);
            txtPassword.Size = new Size(body.Width - 145, 34);
            txtPassword.UseSystemPasswordChar = true;
            txtPassword.TextChanged += delegate { UpdateOkState(); };
            body.Controls.Add(txtPassword);

            OVIA.Desktop.Controls.OviaButton btnCancel = new OVIA.Desktop.Controls.OviaButton();
            btnCancel.Text = "취소";
            btnCancel.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            btnCancel.Size = OviaFluentTheme.MeasureButtonSize(btnCancel.Text);
            btnCancel.Location = new Point((this.ClientSize.Width / 2) - btnCancel.Width - 8, 230);
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; Close(); };
            this.Controls.Add(btnCancel);

            btnOk = new OVIA.Desktop.Controls.OviaButton();
            btnOk.Text = "확인";
            btnOk.Role = OVIA.Desktop.OviaButtonRole.Primary;
            btnOk.Size = OviaFluentTheme.MeasureButtonSize(btnOk.Text);
            btnOk.Location = new Point((this.ClientSize.Width / 2) + 8, 230);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);
        }

        private Label CreateAuthLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(96, 28);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontInput(9F, FontStyle.Bold);
            label.ForeColor = OviaFluentTheme.TextSecondary;
            label.BackColor = Color.Transparent;
            return label;
        }

        private void UpdateOkState()
        {
            if (btnOk == null)
            {
                return;
            }

            bool enabled = txtUserId != null && txtPassword != null && txtUserId.Text.Trim() != "" && txtPassword.Text != "";
            btnOk.Enabled = enabled;
            btnOk.Cursor = enabled ? Cursors.Hand : Cursors.Default;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            InputUserId = txtUserId == null ? "" : txtUserId.Text.Trim();
            InputPassword = txtPassword == null ? "" : txtPassword.Text;
            this.DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class OviaRoundedDialogPanel : Panel
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;
        public Color FillColor = Color.White;
        public int Radius = 12;

        public OviaRoundedDialogPanel()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, Radius))
            using (SolidBrush fill = new SolidBrush(FillColor))
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    public class OviaDialogTextBox : UserControl
    {
        private TextBox innerTextBox;
        private bool focused;
        private bool multiline;
        public Color SurfaceColor = Color.White;
        public int Radius = OviaFluentTheme.CommonInputRadius;
        public new event EventHandler TextChanged;

        public OviaDialogTextBox()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;
            this.Size = new Size(200, OviaFluentTheme.CommonInputHeight);

            innerTextBox = new TextBox();
            innerTextBox.BorderStyle = BorderStyle.None;
            innerTextBox.Font = OviaFluentTheme.FontInput(9.8F, FontStyle.Regular);
            innerTextBox.ForeColor = OviaFluentTheme.TextPrimary;
            innerTextBox.BackColor = Color.White;
            innerTextBox.Location = new Point(12, 8);
            innerTextBox.TextChanged += delegate
            {
                EventHandler handler = TextChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            innerTextBox.Enter += delegate { focused = true; Invalidate(); };
            innerTextBox.Leave += delegate { focused = false; Invalidate(); };
            this.Controls.Add(innerTextBox);
            this.Resize += delegate { LayoutInner(); };
            this.Click += delegate { innerTextBox.Focus(); };
            LayoutInner();
        }

        public override string Text
        {
            get { return innerTextBox == null ? "" : innerTextBox.Text; }
            set { if (innerTextBox != null) innerTextBox.Text = value == null ? "" : value; }
        }

        public bool UseSystemPasswordChar
        {
            get { return innerTextBox != null && innerTextBox.UseSystemPasswordChar; }
            set { if (innerTextBox != null) innerTextBox.UseSystemPasswordChar = value; }
        }

        public bool Multiline
        {
            get { return multiline; }
            set
            {
                multiline = value;
                if (innerTextBox != null)
                {
                    innerTextBox.Multiline = value;
                }
                LayoutInner();
            }
        }

        public ScrollBars ScrollBars
        {
            get { return innerTextBox == null ? ScrollBars.None : innerTextBox.ScrollBars; }
            set { if (innerTextBox != null) innerTextBox.ScrollBars = value; }
        }

        public bool AcceptsReturn
        {
            get { return innerTextBox != null && innerTextBox.AcceptsReturn; }
            set { if (innerTextBox != null) innerTextBox.AcceptsReturn = value; }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (innerTextBox != null) innerTextBox.Font = value;
                LayoutInner();
            }
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
            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, Radius))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(focused ? OviaFluentTheme.CommonInputBorderFocus : OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void LayoutInner()
        {
            if (innerTextBox == null)
            {
                return;
            }

            int paddingX = 12;
            int paddingY = multiline ? 9 : Math.Max(6, (this.Height - innerTextBox.PreferredHeight) / 2);
            innerTextBox.Location = new Point(paddingX, paddingY);
            innerTextBox.Size = new Size(Math.Max(10, this.Width - paddingX * 2), Math.Max(10, this.Height - paddingY * 2));
        }
    }

    public class OviaEditableSelectBox : UserControl
    {
        private TextBox textBox;
        private Label arrowLabel;
        private ListBox listBox;
        private ToolStripDropDown dropDown;
        private ToolStripControlHost host;
        private Panel dropPanel;
        private bool focused;
        private bool dropOpen;
        public Color SurfaceColor = Color.White;
        public new event EventHandler TextChanged;
        public event EventHandler SelectedIndexChanged;

        public OviaEditableSelectBox()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;
            this.Size = new Size(150, OviaFluentTheme.CommonInputHeight);

            textBox = new TextBox();
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            textBox.ForeColor = OviaFluentTheme.TextPrimary;
            textBox.BackColor = Color.White;
            textBox.TextChanged += TextBox_TextChanged;
            textBox.Enter += delegate { focused = true; Invalidate(); };
            textBox.Leave += delegate { focused = false; Invalidate(); };
            textBox.KeyDown += TextBox_KeyDown;
            this.Controls.Add(textBox);

            arrowLabel = new Label();
            arrowLabel.AutoSize = false;
            arrowLabel.Text = "";
            arrowLabel.TextAlign = ContentAlignment.MiddleCenter;
            arrowLabel.BackColor = Color.White;
            arrowLabel.Cursor = Cursors.Hand;
            arrowLabel.Paint += ArrowLabel_Paint;
            arrowLabel.Click += delegate { ShowDropDown(); };
            this.Controls.Add(arrowLabel);

            dropPanel = new Panel();
            dropPanel.BackColor = Color.White;
            dropPanel.Paint += DropPanel_Paint;

            listBox = new ListBox();
            listBox.BorderStyle = BorderStyle.None;
            listBox.DrawMode = DrawMode.OwnerDrawFixed;
            listBox.ItemHeight = 28;
            listBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            listBox.DrawItem += ListBox_DrawItem;
            listBox.Click += ListBox_Click;
            listBox.KeyDown += ListBox_KeyDown;
            dropPanel.Controls.Add(listBox);

            host = new ToolStripControlHost(dropPanel);
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            dropDown = new ToolStripDropDown();
            dropDown.Padding = Padding.Empty;
            dropDown.Margin = Padding.Empty;
            dropDown.AutoClose = true;
            dropDown.Items.Add(host);
            dropDown.Closed += delegate { dropOpen = false; Invalidate(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            ToolStripMenuItem removeItem = new ToolStripMenuItem("선택값 삭제");
            removeItem.Click += delegate { RemoveCurrentItem(); };
            menu.Items.Add(removeItem);
            this.ContextMenuStrip = menu;
            textBox.ContextMenuStrip = menu;

            this.Resize += delegate { LayoutChildren(); };
            this.Click += delegate { textBox.Focus(); };
            LayoutChildren();
        }

        public override string Text
        {
            get { return textBox == null ? "" : textBox.Text; }
            set { if (textBox != null) textBox.Text = value == null ? "" : value; }
        }

        public void AddItem(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            value = value.Trim();
            int i;
            for (i = 0; i < listBox.Items.Count; i++)
            {
                object item = listBox.Items[i];
                if (item != null && string.Equals(item.ToString(), value, StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }
            }

            listBox.Items.Add(value);
        }

        public void RemoveCurrentItem()
        {
            string value = this.Text == null ? "" : this.Text.Trim();
            if (value == "")
            {
                return;
            }

            int i;
            for (i = listBox.Items.Count - 1; i >= 0; i--)
            {
                object item = listBox.Items[i];
                if (item != null && string.Equals(item.ToString(), value, StringComparison.CurrentCultureIgnoreCase))
                {
                    listBox.Items.RemoveAt(i);
                }
            }

            this.Text = "";
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (textBox != null) textBox.Font = value;
                if (listBox != null) listBox.Font = value;
                LayoutChildren();
            }
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
            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, OviaFluentTheme.CommonInputRadius))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen((focused || dropOpen) ? OviaFluentTheme.CommonInputBorderFocus : OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void TextBox_TextChanged(object sender, EventArgs e)
        {
            EventHandler handler = TextChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Delete)
            {
                RemoveCurrentItem();
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Down)
            {
                ShowDropDown();
                e.Handled = true;
            }
        }

        private void ArrowLabel_Paint(object sender, PaintEventArgs e)
        {
            int centerX = arrowLabel.Width / 2;
            int centerY = arrowLabel.Height / 2 + 1;

            using (Pen pen = new Pen(OviaFluentTheme.CommonInputIcon, 1.25F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLine(pen, centerX - 4, centerY - 2, centerX, centerY + 2);
                e.Graphics.DrawLine(pen, centerX, centerY + 2, centerX + 4, centerY - 2);
            }
        }

        private void LayoutChildren()
        {
            int arrowWidth = 28;
            int textTop = Math.Max(6, (this.Height - textBox.PreferredHeight) / 2);
            textBox.Location = new Point(10, textTop);
            textBox.Size = new Size(Math.Max(10, this.Width - arrowWidth - 16), textBox.PreferredHeight);
            arrowLabel.Location = new Point(this.Width - arrowWidth - 3, 2);
            arrowLabel.Size = new Size(arrowWidth, Math.Max(1, this.Height - 4));
        }

        private void ShowDropDown()
        {
            if (listBox.Items.Count == 0)
            {
                return;
            }

            int itemCount = Math.Min(Math.Max(listBox.Items.Count, 1), 10);
            int dropHeight = itemCount * listBox.ItemHeight + 2;
            int dropWidth = Math.Max(this.Width, 120);
            dropPanel.Size = new Size(dropWidth, dropHeight);
            listBox.Location = new Point(1, 1);
            listBox.Size = new Size(dropWidth - 2, dropHeight - 2);
            host.Size = dropPanel.Size;
            dropDown.Size = dropPanel.Size;
            dropOpen = true;
            Invalidate();
            dropDown.Show(this, new Point(0, this.Height - 1));
            listBox.Focus();
        }

        private void ListBox_Click(object sender, EventArgs e)
        {
            ApplySelectedListItem();
        }

        private void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                ApplySelectedListItem();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                dropDown.Close();
                e.Handled = true;
            }
        }

        private void ApplySelectedListItem()
        {
            if (listBox.SelectedIndex < 0)
            {
                return;
            }

            object item = listBox.Items[listBox.SelectedIndex];
            this.Text = item == null ? "" : item.ToString();
            EventHandler handler = SelectedIndexChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
            dropDown.Close();
        }

        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? OviaFluentTheme.CommonInputItemHover : Color.White;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            object item = listBox.Items[e.Index];
            string text = item == null ? "" : item.ToString();
            TextRenderer.DrawText(e.Graphics, text, listBox.Font, new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - 20, e.Bounds.Height), OviaFluentTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void DropPanel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, dropPanel.Width - 1, dropPanel.Height - 1);
            }
        }
    }

    public class OviaSimpleDatePicker : UserControl
    {
        private TextBox textBox;
        private Timer clickTimer;
        private DateTime value = DateTime.Today;
        private bool manualEditMode = false;
        private bool invalidMessageShowing = false;
        public event EventHandler ValueChanged;

        public OviaSimpleDatePicker()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.White;
            this.Size = new Size(160, 34);

            // 화살표 버튼 제거.
            // 1회 클릭: OVIA 달력 열기 / 더블클릭: yyyy-MM-dd 직접 입력
            textBox = new TextBox();
            textBox.BorderStyle = BorderStyle.None;
            textBox.ReadOnly = true;
            textBox.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            textBox.BackColor = Color.White;
            textBox.ForeColor = OviaFluentTheme.TextPrimary;
            textBox.TextAlign = HorizontalAlignment.Left;
            textBox.MouseDown += TextBox_MouseDown;
            textBox.KeyDown += TextBox_KeyDown;
            textBox.Leave += TextBox_Leave;
            this.Controls.Add(textBox);

            clickTimer = new Timer();
            clickTimer.Interval = SystemInformation.DoubleClickTime;
            clickTimer.Tick += delegate
            {
                clickTimer.Stop();
                if (!manualEditMode)
                {
                    ShowCalendar();
                }
            };

            this.Resize += delegate { LayoutChildren(); };
            this.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (!this.Enabled)
                {
                    return;
                }

                if (e.Clicks >= 2)
                {
                    clickTimer.Stop();
                    BeginManualEdit();
                }
                else
                {
                    clickTimer.Stop();
                    clickTimer.Start();
                }
            };

            LayoutChildren();
            UpdateText();
        }

        public DateTime Value
        {
            get { return value; }
            set
            {
                DateTime newValue = value.Date;
                bool changed = this.value.Date != newValue;
                this.value = newValue;
                UpdateText();

                if (changed)
                {
                    EventHandler handler = ValueChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            textBox.Enabled = this.Enabled;
            textBox.BackColor = this.Enabled ? Color.White : OviaFluentTheme.NeutralLight;
            Invalidate();
        }

        private void LayoutChildren()
        {
            textBox.Location = new Point(10, Math.Max(1, (Height - textBox.PreferredHeight) / 2));
            textBox.Size = new Size(Math.Max(1, Width - 20), textBox.PreferredHeight);
        }

        private void UpdateText()
        {
            if (!manualEditMode)
            {
                textBox.Text = value.ToString("yyyy-MM-dd");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, OviaFluentTheme.CommonInputRadius))
            using (SolidBrush brush = new SolidBrush(this.Enabled ? Color.White : OviaFluentTheme.NeutralLight))
            using (Pen pen = new Pen(OviaFluentTheme.CommonInputBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void TextBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (!this.Enabled)
            {
                return;
            }

            if (e.Clicks >= 2)
            {
                clickTimer.Stop();
                BeginManualEdit();
                return;
            }

            if (!manualEditMode)
            {
                clickTimer.Stop();
                clickTimer.Start();
            }
        }

        private void BeginManualEdit()
        {
            if (!this.Enabled)
            {
                return;
            }

            manualEditMode = true;
            textBox.ReadOnly = false;
            textBox.Text = value.ToString("yyyy-MM-dd");
            textBox.Focus();
            textBox.SelectAll();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (manualEditMode && e.KeyCode == Keys.Enter)
            {
                CommitManualText(true);
                e.SuppressKeyPress = true;
                return;
            }

            if (manualEditMode && e.KeyCode == Keys.Escape)
            {
                manualEditMode = false;
                textBox.ReadOnly = true;
                UpdateText();
                e.SuppressKeyPress = true;
                return;
            }

            if (!manualEditMode && (e.KeyCode == Keys.Down || e.KeyCode == Keys.F4))
            {
                ShowCalendar();
                e.SuppressKeyPress = true;
            }
        }

        private void TextBox_Leave(object sender, EventArgs e)
        {
            if (manualEditMode && !invalidMessageShowing)
            {
                CommitManualText(true);
            }
        }

        private bool CommitManualText(bool showInvalidMessage)
        {
            DateTime parsed;
            string raw = (textBox.Text ?? "").Trim();

            if (DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out parsed))
            {
                manualEditMode = false;
                textBox.ReadOnly = true;
                Value = parsed.Date;
                return true;
            }

            if (showInvalidMessage)
            {
                if (!invalidMessageShowing)
                {
                    invalidMessageShowing = true;
                    MessageBox.Show(
                        "올바른 날짜를 yyyy-MM-dd 형식으로 입력해 주세요.\r\n예: 2026-09-25\r\n\r\n월은 01~12, 일은 해당 월의 실제 날짜 범위여야 합니다.",
                        "OVIA",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    invalidMessageShowing = false;
                }

                if (!textBox.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (manualEditMode && textBox.CanFocus)
                        {
                            textBox.Focus();
                            textBox.SelectAll();
                        }
                    });
                }
            }
            else
            {
                manualEditMode = false;
                textBox.ReadOnly = true;
                UpdateText();
            }

            return false;
        }

        private void ShowCalendar()
        {
            if (!this.Enabled)
            {
                return;
            }

            manualEditMode = false;
            textBox.ReadOnly = true;
            UpdateText();

            OviaCalendarPopup popup = new OviaCalendarPopup(value);

            // 핵심: ShowDialog 반환값에 의존하지 않고
            // 달력에서 날짜가 확정되는 즉시 Value에 직접 반영한다.
            popup.DateConfirmed += delegate(object sender, DateSelectedEventArgs e)
            {
                Value = e.Date.Date;
            };

            popup.ShowCalendar(this, new Point(0, this.Height + 2));
        }
    }

    public class OviaCalendarPopup : Form
    {
        private OviaCalendarSurface calendar;
        private Button btnToday;
        public DateTime SelectedDate;
        public event EventHandler<DateSelectedEventArgs> DateConfirmed;

        public OviaCalendarPopup(DateTime selectedDate)
        {
            SelectedDate = selectedDate.Date;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.White;
            this.Size = new Size(300, 388);
            this.Padding = new Padding(8);
            this.Deactivate += delegate
            {
                if (this.Visible)
                {
                    this.DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            // Native MonthCalendar의 선택 이벤트가 사용자 환경에서 확정되지 않는 문제가 있어
            // OVIA 자체 달력 Surface를 사용한다. 날짜 셀 클릭은 OVIA가 직접 좌표 계산하여
            // 실제 DateTime으로 확정하므로 월 이동/날짜 선택 동작을 안정적으로 분리할 수 있다.
            calendar = new OviaCalendarSurface(SelectedDate);
            calendar.Location = new Point(8, 8);
            calendar.Size = new Size(this.ClientSize.Width - 16, 326);
            calendar.DateSelected += Calendar_DateSelected;
            this.Controls.Add(calendar);

            btnToday = new Button();
            btnToday.Text = "오늘";
            btnToday.FlatStyle = FlatStyle.Flat;
            btnToday.FlatAppearance.BorderColor = OviaFluentTheme.CommonInputBorder;
            btnToday.BackColor = Color.White;
            btnToday.Font = OviaFluentTheme.FontInput(9F, FontStyle.Regular);
            btnToday.Size = new Size(64, 28);
            btnToday.Location = new Point(this.Width - btnToday.Width - 10, this.Height - btnToday.Height - 10);
            btnToday.Click += delegate
            {
                ConfirmDate(DateTime.Today);
            };
            this.Controls.Add(btnToday);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1F))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        public void ShowCalendar(Control owner, Point point)
        {
            Point screenPoint = owner.PointToScreen(point);
            this.Location = screenPoint;

            // 비모달 Owned Form으로 표시한다.
            // 따라서 사용자가 달력 바깥의 OVIA 화면을 클릭하면 Deactivate가 발생하고 즉시 닫힌다.
            Form ownerForm = owner.FindForm();
            if (ownerForm != null)
            {
                this.Show(ownerForm);
            }
            else
            {
                this.Show();
            }

            this.Activate();
        }

        private void Calendar_DateSelected(object sender, DateSelectedEventArgs e)
        {
            ConfirmDate(e.Date);
        }

        private void ConfirmDate(DateTime date)
        {
            SelectedDate = date.Date;

            EventHandler<DateSelectedEventArgs> handler = DateConfirmed;
            if (handler != null)
            {
                // 달력을 닫기 전에 발주일/납기일 입력 컨트롤에 먼저 반영한다.
                handler(this, new DateSelectedEventArgs(SelectedDate));
            }

            this.DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class DateSelectedEventArgs : EventArgs
    {
        public readonly DateTime Date;
        public DateSelectedEventArgs(DateTime date)
        {
            Date = date;
        }
    }

    public class OviaCalendarSurface : Control
    {
        private enum CalendarViewMode
        {
            Days,
            Years,
            Months
        }

        private DateTime displayMonth;
        private DateTime selectedDate;
        private CalendarViewMode viewMode = CalendarViewMode.Days;
        private int yearPageStart;
        private DateTime? hoveredDate;
        private const int RowGap = 5;
        public event EventHandler<DateSelectedEventArgs> DateSelected;

        public OviaCalendarSurface(DateTime selectedDate)
        {
            this.selectedDate = selectedDate.Date;
            this.displayMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            this.yearPageStart = GetYearPageStart(selectedDate.Year);
            this.DoubleBuffered = true;
            this.Font = OviaFluentTheme.FontInput(9.5F, FontStyle.Regular);
            this.BackColor = Color.White;
            this.Cursor = Cursors.Default;
            this.MouseDown += OviaCalendarSurface_MouseDown;
            this.MouseMove += OviaCalendarSurface_MouseMove;
            this.MouseLeave += OviaCalendarSurface_MouseLeave;
        }

        private int GetYearPageStart(int year)
        {
            int start = year - 5;
            if (start < 1)
            {
                start = 1;
            }
            if (start > 9988)
            {
                start = 9988;
            }
            return start;
        }

        private Rectangle PrevButtonRect
        {
            get { return new Rectangle(18, 22, 28, 30); }
        }

        private Rectangle NextButtonRect
        {
            get { return new Rectangle(Width - 46, 22, 28, 30); }
        }

        private Rectangle YearTitleRect
        {
            get
            {
                int center = Width / 2;
                return new Rectangle(center - 76, 24, 78, 30);
            }
        }

        private Rectangle MonthTitleRect
        {
            get
            {
                int center = Width / 2;
                return new Rectangle(center + 2, 24, 62, 30);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            using (Pen border = new Pen(OviaFluentTheme.CardBorder, 1F))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            DrawNavigation(e.Graphics);

            if (viewMode == CalendarViewMode.Years)
            {
                DrawYearSelector(e.Graphics);
                return;
            }

            if (viewMode == CalendarViewMode.Months)
            {
                DrawMonthSelector(e.Graphics);
                return;
            }

            DrawDayCalendar(e.Graphics);
        }

        private void DrawNavigation(Graphics g)
        {
            TextRenderer.DrawText(
                g,
                "‹",
                OviaFluentTheme.FontTitle(14F, FontStyle.Bold),
                PrevButtonRect,
                OviaFluentTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

            TextRenderer.DrawText(
                g,
                "›",
                OviaFluentTheme.FontTitle(14F, FontStyle.Bold),
                NextButtonRect,
                OviaFluentTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

            if (viewMode == CalendarViewMode.Years)
            {
                string rangeText = yearPageStart.ToString() + " - " + Math.Min(9999, yearPageStart + 11).ToString();
                TextRenderer.DrawText(
                    g,
                    rangeText,
                    OviaFluentTheme.FontInput(10F, FontStyle.Bold),
                    new Rectangle(46, 24, Width - 92, 30),
                    OviaFluentTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                return;
            }

            if (viewMode == CalendarViewMode.Months)
            {
                TextRenderer.DrawText(
                    g,
                    displayMonth.Year.ToString() + "년",
                    OviaFluentTheme.FontInput(10F, FontStyle.Bold),
                    new Rectangle(46, 24, Width - 92, 30),
                    OviaFluentTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                return;
            }

            // 기존 디자인을 유지하되 년도와 월을 각각 클릭할 수 있도록 제목 영역만 분리한다.
            TextRenderer.DrawText(
                g,
                displayMonth.Year.ToString() + "년",
                OviaFluentTheme.FontInput(10F, FontStyle.Bold),
                YearTitleRect,
                OviaFluentTheme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

            TextRenderer.DrawText(
                g,
                displayMonth.Month.ToString() + "월",
                OviaFluentTheme.FontInput(10F, FontStyle.Bold),
                MonthTitleRect,
                OviaFluentTheme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        private void DrawDayCalendar(Graphics g)
        {
            string[] days = new string[] { "일", "월", "화", "수", "목", "금", "토" };
            int gridLeft = 24;
            int gridTop = 72;
            int cellW = (Width - (gridLeft * 2)) / 7;
            int headerH = 28;
            int cellH = 30;

            int i;
            for (i = 0; i < 7; i++)
            {
                TextRenderer.DrawText(
                    g,
                    days[i],
                    OviaFluentTheme.FontInput(9F, FontStyle.Bold),
                    new Rectangle(gridLeft + i * cellW, gridTop, cellW, headerH),
                    OviaFluentTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }

            DateTime firstDay = displayMonth;
            int startOffset = (int)firstDay.DayOfWeek;
            DateTime firstCell = firstDay.AddDays(-startOffset);

            int row;
            for (row = 0; row < 6; row++)
            {
                for (i = 0; i < 7; i++)
                {
                    DateTime day = firstCell.AddDays(row * 7 + i);
                    int x = gridLeft + i * cellW;
                    int y = gridTop + headerH + row * (cellH + RowGap);
                    Rectangle rect = new Rectangle(x, y, cellW, cellH);
                    bool isCurrentMonth = day.Month == displayMonth.Month && day.Year == displayMonth.Year;
                    bool isSelected = day.Date == selectedDate.Date;
                    bool isHovered = hoveredDate.HasValue && day.Date == hoveredDate.Value.Date;

                    // Hover 날짜는 연한 노란색 원형 배경으로 표시한다.
                    // 선택된 날짜(녹색)가 우선이며, 선택 상태는 그대로 유지한다.
                    if (isHovered && !isSelected)
                    {
                        Rectangle hoverCircle = new Rectangle(rect.Left + (rect.Width - 28) / 2, rect.Top + 1, 28, 28);
                        using (SolidBrush hoverBrush = new SolidBrush(Color.FromArgb(255, 247, 204)))
                        {
                            g.FillEllipse(hoverBrush, hoverCircle);
                        }
                    }

                    if (isSelected)
                    {
                        Rectangle circle = new Rectangle(rect.Left + (rect.Width - 28) / 2, rect.Top + 1, 28, 28);
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(24, 128, 42)))
                        {
                            g.FillEllipse(brush, circle);
                        }
                    }

                    Color textColor = isSelected
                        ? Color.White
                        : (isCurrentMonth ? OviaFluentTheme.TextPrimary : OviaFluentTheme.TextMuted);

                    TextRenderer.DrawText(
                        g,
                        day.Day.ToString(),
                        this.Font,
                        rect,
                        textColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
            }
        }

        private void DrawYearSelector(Graphics g)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int index = 0;
            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    int year = yearPageStart + index;
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    bool selected = year == displayMonth.Year;
                    if (selected)
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(234, 246, 237)))
                        using (Pen pen = new Pen(Color.FromArgb(24, 128, 42), 1F))
                        {
                            g.FillRectangle(brush, rect);
                            g.DrawRectangle(pen, rect);
                        }
                    }

                    TextRenderer.DrawText(
                        g,
                        year.ToString(),
                        OviaFluentTheme.FontInput(9.5F, selected ? FontStyle.Bold : FontStyle.Regular),
                        rect,
                        selected ? Color.FromArgb(24, 128, 42) : OviaFluentTheme.TextPrimary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

                    index++;
                }
            }
        }

        private void DrawMonthSelector(Graphics g)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int month = 1;
            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    bool selected = month == displayMonth.Month;
                    if (selected)
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(234, 246, 237)))
                        using (Pen pen = new Pen(Color.FromArgb(24, 128, 42), 1F))
                        {
                            g.FillRectangle(brush, rect);
                            g.DrawRectangle(pen, rect);
                        }
                    }

                    TextRenderer.DrawText(
                        g,
                        month.ToString() + "월",
                        OviaFluentTheme.FontInput(9.5F, selected ? FontStyle.Bold : FontStyle.Regular),
                        rect,
                        selected ? Color.FromArgb(24, 128, 42) : OviaFluentTheme.TextPrimary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

                    month++;
                }
            }
        }

        private void OviaCalendarSurface_MouseMove(object sender, MouseEventArgs e)
        {
            DateTime? newHoveredDate = null;
            bool handCursor = false;

            if (PrevButtonRect.Contains(e.Location) || NextButtonRect.Contains(e.Location))
            {
                handCursor = true;
            }
            else if (viewMode == CalendarViewMode.Days)
            {
                if (YearTitleRect.Contains(e.Location) || MonthTitleRect.Contains(e.Location))
                {
                    handCursor = true;
                }
                else
                {
                    DateTime hitDate;
                    if (TryGetDateAtPoint(e.Location, out hitDate))
                    {
                        newHoveredDate = hitDate.Date;
                        handCursor = true;
                    }
                }
            }
            else if (viewMode == CalendarViewMode.Years)
            {
                handCursor = IsYearCellAtPoint(e.Location);
            }
            else if (viewMode == CalendarViewMode.Months)
            {
                handCursor = IsMonthCellAtPoint(e.Location);
            }

            bool hoverChanged =
                hoveredDate.HasValue != newHoveredDate.HasValue ||
                (hoveredDate.HasValue && newHoveredDate.HasValue &&
                 hoveredDate.Value.Date != newHoveredDate.Value.Date);

            if (hoverChanged)
            {
                hoveredDate = newHoveredDate;
                Invalidate();
            }

            this.Cursor = handCursor ? Cursors.Hand : Cursors.Default;
        }

        private void OviaCalendarSurface_MouseLeave(object sender, EventArgs e)
        {
            if (hoveredDate.HasValue)
            {
                hoveredDate = null;
                Invalidate();
            }

            this.Cursor = Cursors.Default;
        }

        private bool TryGetDateAtPoint(Point location, out DateTime date)
        {
            date = DateTime.MinValue;

            if (viewMode != CalendarViewMode.Days)
            {
                return false;
            }

            int gridLeft = 24;
            int gridTop = 72;
            int cellW = (Width - (gridLeft * 2)) / 7;
            int headerH = 28;
            int cellH = 30;
            int startY = gridTop + headerH;

            if (location.X < gridLeft || location.X >= Width - gridLeft || location.Y < startY)
            {
                return false;
            }

            int col = (location.X - gridLeft) / cellW;
            int row = (location.Y - startY) / (cellH + RowGap);
            int rowY = startY + row * (cellH + RowGap);

            if (col < 0 || col > 6 || row < 0 || row > 5 || location.Y > rowY + cellH)
            {
                return false;
            }

            DateTime firstDay = displayMonth;
            DateTime firstCell = firstDay.AddDays(-(int)firstDay.DayOfWeek);
            date = firstCell.AddDays(row * 7 + col).Date;
            return true;
        }

        private bool IsYearCellAtPoint(Point location)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    if (rect.Contains(location))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsMonthCellAtPoint(Point location)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    if (rect.Contains(location))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void OviaCalendarSurface_MouseDown(object sender, MouseEventArgs e)
        {
            if (PrevButtonRect.Contains(e.Location))
            {
                if (viewMode == CalendarViewMode.Years)
                {
                    yearPageStart = Math.Max(1, yearPageStart - 12);
                }
                else if (viewMode == CalendarViewMode.Months)
                {
                    int year = Math.Max(1, displayMonth.Year - 1);
                    displayMonth = new DateTime(year, displayMonth.Month, 1);
                }
                else
                {
                    if (displayMonth.Year > 1 || displayMonth.Month > 1)
                    {
                        displayMonth = displayMonth.AddMonths(-1);
                    }
                }

                Invalidate();
                return;
            }

            if (NextButtonRect.Contains(e.Location))
            {
                if (viewMode == CalendarViewMode.Years)
                {
                    yearPageStart = Math.Min(9988, yearPageStart + 12);
                }
                else if (viewMode == CalendarViewMode.Months)
                {
                    int year = Math.Min(9999, displayMonth.Year + 1);
                    displayMonth = new DateTime(year, displayMonth.Month, 1);
                }
                else
                {
                    if (displayMonth.Year < 9999 || displayMonth.Month < 12)
                    {
                        displayMonth = displayMonth.AddMonths(1);
                    }
                }

                Invalidate();
                return;
            }

            if (viewMode == CalendarViewMode.Days)
            {
                if (YearTitleRect.Contains(e.Location))
                {
                    yearPageStart = GetYearPageStart(displayMonth.Year);
                    viewMode = CalendarViewMode.Years;
                    Invalidate();
                    return;
                }

                if (MonthTitleRect.Contains(e.Location))
                {
                    viewMode = CalendarViewMode.Months;
                    Invalidate();
                    return;
                }

                HandleDayClick(e.Location);
                return;
            }

            if (viewMode == CalendarViewMode.Years)
            {
                HandleYearClick(e.Location);
                return;
            }

            if (viewMode == CalendarViewMode.Months)
            {
                HandleMonthClick(e.Location);
            }
        }

        private void HandleDayClick(Point location)
        {
            DateTime picked;
            if (!TryGetDateAtPoint(location, out picked))
            {
                return;
            }

            selectedDate = picked.Date;
            hoveredDate = picked.Date;
            displayMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            Invalidate();

            EventHandler<DateSelectedEventArgs> handler = DateSelected;
            if (handler != null)
            {
                handler(this, new DateSelectedEventArgs(picked));
            }
        }

        private void HandleYearClick(Point location)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    if (rect.Contains(location))
                    {
                        int year = yearPageStart + r * cols + c;
                        if (year >= 1 && year <= 9999)
                        {
                            displayMonth = new DateTime(year, displayMonth.Month, 1);
                            viewMode = CalendarViewMode.Days;
                            Invalidate();
                        }
                        return;
                    }
                }
            }
        }

        private void HandleMonthClick(Point location)
        {
            int left = 28;
            int top = 82;
            int cols = 3;
            int rows = 4;
            int gapX = 8;
            int gapY = 10;
            int cellW = (Width - (left * 2) - (gapX * (cols - 1))) / cols;
            int cellH = 46;

            int r;
            int c;
            for (r = 0; r < rows; r++)
            {
                for (c = 0; c < cols; c++)
                {
                    Rectangle rect = new Rectangle(
                        left + c * (cellW + gapX),
                        top + r * (cellH + gapY),
                        cellW,
                        cellH);

                    if (rect.Contains(location))
                    {
                        int month = r * cols + c + 1;
                        if (month >= 1 && month <= 12)
                        {
                            displayMonth = new DateTime(displayMonth.Year, month, 1);
                            viewMode = CalendarViewMode.Days;
                            Invalidate();
                        }
                        return;
                    }
                }
            }
        }
    }

    public class OviaExcelActionButton : OVIA.Desktop.Controls.OviaButton
    {
        private bool excelHovered;
        private bool excelPressed;

        public OviaExcelActionButton()
        {
            this.Role = OVIA.Desktop.OviaButtonRole.Neutral;
            this.ForeColor = Color.FromArgb(33, 115, 70);

            // 공사별 BarList의 Excel 전용 버튼은 직접 그리므로 Windows 기본 버튼 잔상/포커스 배경이
            // 뒤에 비치지 않도록 완전한 사용자 렌더링 상태를 유지한다.
            this.UseVisualStyleBackColor = false;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.FlatAppearance.MouseOverBackColor = Color.Transparent;
            this.FlatAppearance.MouseDownBackColor = Color.Transparent;
            this.TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            excelHovered = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            excelHovered = false;
            excelPressed = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                excelPressed = true;
            }

            base.OnMouseDown(e);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            excelPressed = false;
            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            PaintParentSurface(pevent.Graphics);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            // 매 프레임 버튼 바깥 픽셀까지 부모 배경으로 먼저 지워서
            // 이전 hover/focus 색상이나 흰색 사각 잔상이 모서리/후면에 남지 않게 한다.
            PaintParentSurface(e.Graphics);

            Color excelGreen = Color.FromArgb(33, 115, 70);
            Color fill = excelPressed
                ? OviaFluentTheme.NeutralLight
                : (excelHovered ? Color.FromArgb(238, 248, 241) : OviaFluentTheme.ButtonNeutralBack);
            Color border = OviaFluentTheme.ButtonNeutralBorder;

            // OVIA 버튼 규칙에 맞춰 그림자/외곽 잔선은 파란 계열을 사용하지 않는다.
            // 그림자는 공통 중립 회색 계열(CardShadow)로만 1px 안쪽에 표현한다.
            Rectangle shadowRect = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            Rectangle buttonRect = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));

            using (GraphicsPath shadowPath = OviaProjectBarListDrawHelper.RoundRect(shadowRect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush shadowBrush = new SolidBrush(OviaFluentTheme.CardShadow))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(buttonRect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            Rectangle textRect = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                excelGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            );
        }

        private void PaintParentSurface(Graphics graphics)
        {
            Color parentBackColor = OviaFluentTheme.AppBackground;

            if (Parent != null)
            {
                parentBackColor = Parent.BackColor;

                if (parentBackColor == Color.Transparent && Parent.Parent != null)
                {
                    parentBackColor = Parent.Parent.BackColor;
                }
            }

            if (parentBackColor == Color.Transparent)
            {
                parentBackColor = OviaFluentTheme.AppBackground;
            }

            using (SolidBrush parentBrush = new SolidBrush(parentBackColor))
            {
                graphics.FillRectangle(parentBrush, ClientRectangle);
            }
        }
    }

    public class ProjectBarListSummary
    {
        public int ListNumber = 0;
        public string FilePath = "";
        public string Status = "";
        public string WriteStatus = "";
        public string OrderNumber = "";
        public string OrderDate = "";
        public string DueDate = "";
        public string Building = "";
        public string Floor = "";
        public string WorkType = "";
        public string Progress = "";
        public string Title = "";
        public string Tags = "";
        public string Color = "";
        public double OrderQty = 0;
        public string TagIssued = "";
        public string Etc = "";
        public string LongBar = "";
        public string Cutting = "";
        public string Bending = "";
        public string Shipped = "";
        public string NotShipped = "";
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
