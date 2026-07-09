using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmBarListMappingManager : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;

        private Panel contentScrollPanel;
        private Panel bottomButtonPanel;
        private DataGridView grid;
        private Label lblStatus;
        private Button btnAddColumn;
        private Button btnClearCell;
        private Button btnReset;
        private Button btnClose;
        private Button btnSave;
        private ToolTip windowToolTip;
        private ContextMenuStrip columnMenu;
        private ToolStripMenuItem deleteColumnMenuItem;
        private int aliasColumnSeed = 0;
        private int columnMenuIndex = -1;
        private int activeRowIndex = -1;
        private int activeColumnIndex = -1;
        private int selectedCellRowIndex = -1;
        private int selectedCellColumnIndex = -1;
        private int editingCellRowIndex = -1;
        private int editingCellColumnIndex = -1;
        private bool suppressNextCellClick = false;
        private bool restoringSnapshot = false;
        private GridSnapshot editBeforeSnapshot = null;
        private string editBeforeValue = "";
        private readonly Stack<GridSnapshot> undoStack = new Stack<GridSnapshot>();
        private readonly Stack<GridSnapshot> redoStack = new Stack<GridSnapshot>();
        private readonly HashSet<string> changedCells = new HashSet<string>();

        private bool isApplyingGridLayout = false;
        private bool isApplyingWorkspaceBounds = false;
        private readonly Dictionary<string, int> userColumnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Color ActiveRowBackColor = Color.FromArgb(255, 248, 205);
        private readonly Color ActiveCellBorderColor = Color.FromArgb(255, 204, 0);
        private readonly Color ActiveColumnBackColor = Color.FromArgb(222, 242, 255);
        private readonly Color EditCellBorderColor = Color.FromArgb(20, 20, 20);
        private readonly Color ChangedTextColor = OviaFluentTheme.Danger;


        public string WorkspaceHelpKey { get { return "BARLIST_MAPPING"; } }
        public string WorkspaceHelpTitle { get { return "BarList 항목 매핑"; } }
        public string WorkspaceHelpText
        {
            get
            {
                return "CAD 도면마다 다른 철근재료표 헤더명을 OVIA 기본 헤더로 치환합니다. 매핑 텍스트는 셀 단위로 추가/수정할 수 있으며, 매핑 열은 드래그로 순서를 바꿀 수 있습니다.";
            }
        }
        public FrmBarListMappingManager(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;

            BuildUI();
            LoadStoreToGrid(OviaBarListMappingStore.LoadDefault());
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA - BarList 항목 매핑";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ClientSize = new Size(1180, 720);
            this.MinimumSize = Size.Empty;
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmBarListMappingManager_FormClosing;
            Resize += WorkspaceContent_Resize;

            windowToolTip = new ToolTip();
            windowToolTip.AutoPopDelay = 4000;
            windowToolTip.InitialDelay = 350;
            windowToolTip.ReshowDelay = 100;
            windowToolTip.ShowAlways = true;

            BuildExplorerHeader(this, OviaMenuHelpStore.GetWorkspacePath("BARLIST_MAPPING", "메인  ›  환경설정  ›  BarList 항목 매핑"));
            BuildCommandBar(this);

            contentScrollPanel = new Panel();
            contentScrollPanel.Location = new Point(0, 98);
            contentScrollPanel.Size = new Size(1180, 430);
            contentScrollPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            contentScrollPanel.BackColor = SurfaceColor;
            contentScrollPanel.Margin = Padding.Empty;
            contentScrollPanel.Padding = Padding.Empty;
            contentScrollPanel.AutoScrollMargin = Size.Empty;
            contentScrollPanel.AutoScroll = false;
            this.Controls.Add(contentScrollPanel);

            grid = new DataGridView();
            grid.Location = Point.Empty;
            grid.Size = new Size(1180, 430);
            grid.Anchor = AnchorStyles.None;
            grid.Dock = DockStyle.Fill;
            grid.ScrollBars = ScrollBars.Both;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToOrderColumns = true;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.BackgroundColor = SurfaceColor;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.5F, FontStyle.Regular);
            grid.DefaultCellStyle.SelectionBackColor = ActiveRowBackColor;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 34;
            grid.AllowUserToResizeColumns = true;
            grid.AllowUserToResizeRows = true;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

            OviaFluentTheme.ApplyDataGrid(grid);
            grid.BackgroundColor = SurfaceColor;
            grid.DefaultCellStyle.SelectionBackColor = ActiveRowBackColor;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;

            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellClick += Grid_CellClick;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CellBeginEdit += Grid_CellBeginEdit;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellPainting += Grid_CellPainting;
            grid.EditingControlShowing += Grid_EditingControlShowing;
            grid.KeyDown += Grid_KeyDown;
            grid.ColumnWidthChanged += Grid_ColumnWidthChanged;
            grid.RowHeightChanged += Grid_RowHeightChanged;
            grid.RowsAdded += Grid_RowsAdded;
            grid.RowsRemoved += Grid_RowsRemoved;
            // DataGridView 자체 세로 스크롤을 사용한다. 외부 Panel AutoScroll 전달은 가로/세로 중복 스크롤의 원인이므로 사용하지 않는다.
            contentScrollPanel.Controls.Add(grid);

            BuildColumnContextMenu();

            int buttonTop = 0;
            int initialButtonPanelHeight = Math.Max(1, Math.Min(50, OviaFluentTheme.ButtonHeight));
            const int buttonGap = 10;
            const int rightMargin = 25;

            bottomButtonPanel = new Panel();
            bottomButtonPanel.Location = new Point(0, 596);
            bottomButtonPanel.Size = new Size(1180, initialButtonPanelHeight);
            bottomButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottomButtonPanel.BackColor = SurfaceColor;
            bottomButtonPanel.Margin = Padding.Empty;
            bottomButtonPanel.Padding = Padding.Empty;
            this.Controls.Add(bottomButtonPanel);

            btnAddColumn = CreateButton("매핑 열 추가", 25, buttonTop);
            btnAddColumn.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            btnAddColumn.Click += AddAliasColumn_Click;
            bottomButtonPanel.Controls.Add(btnAddColumn);

            btnClearCell = CreateButton("선택 셀 비우기", btnAddColumn.Right + buttonGap, buttonTop);
            btnClearCell.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            btnClearCell.Click += ClearSelectedCell_Click;
            bottomButtonPanel.Controls.Add(btnClearCell);

            btnReset = CreateButton("기본값 복원", btnClearCell.Right + buttonGap, buttonTop);
            btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            btnReset.Click += ResetDefault_Click;
            bottomButtonPanel.Controls.Add(btnReset);

            btnClose = CreateButton("닫기", this.ClientSize.Width - rightMargin - OviaFluentTheme.MeasureButtonWidth("닫기"), buttonTop);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { this.Close(); };
            bottomButtonPanel.Controls.Add(btnClose);

            btnSave = CreateButton("저장하기", btnClose.Left - buttonGap - OviaFluentTheme.MeasureButtonWidth("저장하기"), buttonTop);
            btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSave.Visible = false;
            btnSave.Enabled = false;
            btnSave.Click += Save_Click;
            bottomButtonPanel.Controls.Add(btnSave);

            lblStatus = new Label();
            lblStatus.Text = "매핑 설정을 불러오는 중입니다.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(1180, 28);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Visible = false;
            lblStatus.Location = new Point(0, 692);
            this.Controls.Add(lblStatus);

            this.ResumeLayout(false);
        }

        private void WorkspaceContent_Resize(object sender, EventArgs e)
        {
            ApplyWorkspaceLayout();
        }


        private Size GetWorkspaceClientSize()
        {
            Control parent = this.Parent;

            if (parent != null && !parent.IsDisposed)
            {
                Rectangle parentBounds = parent.ClientRectangle;

                if (parentBounds.Width > 0 && parentBounds.Height > 0)
                {
                    if (this.Dock != DockStyle.Fill)
                    {
                        this.Dock = DockStyle.Fill;
                    }

                    if (!isApplyingWorkspaceBounds && (this.Location != Point.Empty || this.Size != parentBounds.Size))
                    {
                        try
                        {
                            isApplyingWorkspaceBounds = true;
                            this.Location = Point.Empty;
                            this.Size = parentBounds.Size;
                        }
                        finally
                        {
                            isApplyingWorkspaceBounds = false;
                        }
                    }

                    return parentBounds.Size;
                }
            }

            return this.ClientSize;
        }

        public void ApplyWorkspaceLayout()
        {
            const int menuBottom = 98;
            const int fixedAreaGap = 12;
            const int contentHorizontalInset = 25;
            const int fixedAreaMaxHeight = 50;
            const int buttonGap = 10;
            const int rightMargin = 25;

            Size layoutSize = GetWorkspaceClientSize();
            int width = Math.Max(1, layoutSize.Width);
            int height = Math.Max(1, layoutSize.Height);
            int buttonVisualHeight = btnAddColumn == null ? OviaFluentTheme.ButtonHeight : btnAddColumn.Height;
            int fixedAreaTop = menuBottom + fixedAreaGap;
            int fixedAreaHeight = Math.Max(1, Math.Min(fixedAreaMaxHeight, buttonVisualHeight));
            int scrollTop = fixedAreaTop + fixedAreaHeight + fixedAreaGap;

            if (scrollTop >= height)
            {
                scrollTop = Math.Max(menuBottom, height - 1);
            }

            int contentHeight = Math.Max(1, height - scrollTop);
            int buttonTopInPanel = 0;

            if (bottomButtonPanel != null)
            {
                bottomButtonPanel.Location = new Point(0, fixedAreaTop);
                bottomButtonPanel.Size = new Size(width, fixedAreaHeight);
                bottomButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                bottomButtonPanel.Margin = Padding.Empty;
                bottomButtonPanel.Padding = Padding.Empty;
                bottomButtonPanel.Visible = true;
                bottomButtonPanel.BringToFront();
            }

            if (contentScrollPanel != null)
            {
                contentScrollPanel.SuspendLayout();
                contentScrollPanel.AutoScroll = false;
                contentScrollPanel.AutoScrollMinSize = Size.Empty;
                contentScrollPanel.Location = new Point(0, scrollTop);
                contentScrollPanel.Size = new Size(width, contentHeight);
                contentScrollPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                contentScrollPanel.Padding = Padding.Empty;
                contentScrollPanel.Margin = Padding.Empty;
            }

            if (grid != null)
            {
                grid.SuspendLayout();
                grid.Dock = DockStyle.Fill;
                grid.Location = Point.Empty;
                grid.Size = contentScrollPanel == null ? new Size(width, contentHeight) : contentScrollPanel.ClientSize;
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                grid.Margin = Padding.Empty;
                grid.ScrollBars = ScrollBars.Both;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                FitGridColumnsToContentFrame();
                grid.ResumeLayout(false);
            }

            if (contentScrollPanel != null)
            {
                contentScrollPanel.ResumeLayout(false);
            }

            if (btnAddColumn != null)
            {
                btnAddColumn.Location = new Point(contentHorizontalInset, buttonTopInPanel);
            }
            if (btnClearCell != null && btnAddColumn != null)
            {
                btnClearCell.Location = new Point(btnAddColumn.Right + buttonGap, buttonTopInPanel);
            }
            if (btnReset != null && btnClearCell != null)
            {
                btnReset.Location = new Point(btnClearCell.Right + buttonGap, buttonTopInPanel);
            }
            if (btnClose != null)
            {
                int panelWidth = bottomButtonPanel == null ? width : Math.Max(1, bottomButtonPanel.ClientSize.Width);
                btnClose.Location = new Point(Math.Max(0, panelWidth - rightMargin - btnClose.Width), buttonTopInPanel);
            }
            if (btnSave != null && btnClose != null)
            {
                btnSave.Location = new Point(Math.Max(0, btnClose.Left - buttonGap - btnSave.Width), buttonTopInPanel);
            }
            if (lblStatus != null)
            {
                lblStatus.Visible = false;
                lblStatus.Location = new Point(0, height);
                lblStatus.Size = new Size(width, 0);
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;
                lblStatus.Padding = Padding.Empty;
                lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            }
        }

        private string GetGridColumnKey(DataGridViewColumn column)
        {
            if (column == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(column.Name))
            {
                return column.Name;
            }

            return column.Index.ToString();
        }

        private int MeasureGridTextWidth(string text, Font font)
        {
            string value = string.IsNullOrEmpty(text) ? " " : text;
            Font useFont = font == null ? this.Font : font;
            try
            {
                return TextRenderer.MeasureText(value, useFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            }
            catch
            {
                return Math.Max(24, value.Length * 9);
            }
        }

        private int GetGridColumnBaseWidth(DataGridViewColumn column)
        {
            if (column == null)
            {
                return 1;
            }

            string key = GetGridColumnKey(column);
            int manualWidth;
            if (!string.IsNullOrEmpty(key) && userColumnWidths.TryGetValue(key, out manualWidth))
            {
                return Math.Max(column.MinimumWidth, manualWidth);
            }

            int preferredWidth = Math.Max(1, column.MinimumWidth);
            try
            {
                preferredWidth = Math.Max(preferredWidth, column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.DisplayedCells, true));
            }
            catch
            {
            }

            Font headerFont = grid == null ? this.Font : grid.ColumnHeadersDefaultCellStyle.Font;
            if (headerFont == null && grid != null)
            {
                headerFont = grid.Font;
            }

            int headerWidth = MeasureGridTextWidth(column.HeaderText, headerFont) + 28;
            return Math.Max(column.MinimumWidth, Math.Max(preferredWidth, headerWidth));
        }

        private int GetVisibleGridRowsHeight()
        {
            if (grid == null)
            {
                return 0;
            }

            int totalHeight = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
            try
            {
                totalHeight += grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible);
            }
            catch
            {
                int i;
                for (i = 0; i < grid.Rows.Count; i++)
                {
                    if (grid.Rows[i].Visible)
                    {
                        totalHeight += grid.Rows[i].Height;
                    }
                }
            }

            return totalHeight;
        }

        private void FitGridColumnsToContentFrame()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return;
            }

            bool previousApplying = isApplyingGridLayout;
            isApplyingGridLayout = true;

            try
            {
                if (contentScrollPanel != null && contentScrollPanel.ClientSize.Width > 0 && contentScrollPanel.ClientSize.Height > 0)
                {
                    contentScrollPanel.PerformLayout();
                    Size frameSize = contentScrollPanel.ClientSize;
                    if (grid.Size != frameSize)
                    {
                        grid.Size = frameSize;
                    }
                }

                int frameWidth = contentScrollPanel == null ? grid.ClientSize.Width : contentScrollPanel.ClientSize.Width;
                int frameHeight = contentScrollPanel == null ? grid.ClientSize.Height : contentScrollPanel.ClientSize.Height;
                int visibleRowsHeight = GetVisibleGridRowsHeight();
                int verticalScrollReserve = visibleRowsHeight > frameHeight ? SystemInformation.VerticalScrollBarWidth : 0;
                int availableWidth = Math.Max(1, frameWidth - verticalScrollReserve - 2);
                int totalBaseWidth = 0;
                int visibleCount = 0;
                int i;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    if (column == null || !column.Visible)
                    {
                        continue;
                    }

                    totalBaseWidth += GetGridColumnBaseWidth(column);
                    visibleCount++;
                }

                if (visibleCount == 0 || totalBaseWidth <= 0)
                {
                    return;
                }

                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                grid.ScrollBars = ScrollBars.Both;

                int extraWidth = Math.Max(0, availableWidth - totalBaseWidth);
                int remainingExtra = extraWidth;
                DataGridViewColumn lastVisibleColumn = null;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    if (column == null || !column.Visible)
                    {
                        continue;
                    }

                    int baseWidth = GetGridColumnBaseWidth(column);
                    int newWidth = baseWidth;

                    if (extraWidth > 0)
                    {
                        int addWidth = (int)Math.Floor((double)extraWidth * (double)baseWidth / (double)totalBaseWidth);
                        newWidth += addWidth;
                        remainingExtra -= addWidth;
                    }

                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = Math.Max(column.MinimumWidth, newWidth);
                    lastVisibleColumn = column;
                }

                if (extraWidth > 0 && remainingExtra > 0 && lastVisibleColumn != null)
                {
                    lastVisibleColumn.Width = lastVisibleColumn.Width + remainingExtra;
                }

                grid.PerformLayout();
                grid.Invalidate();
            }
            finally
            {
                isApplyingGridLayout = previousApplying;
            }
        }

        private void RefreshGridScrollbarsAfterDimensionChange(bool refitColumns)
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            if (refitColumns)
            {
                FitGridColumnsToContentFrame();
            }

            grid.ScrollBars = ScrollBars.None;
            grid.ScrollBars = ScrollBars.Both;
            grid.PerformLayout();
            grid.Invalidate();

            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (grid == null || grid.IsDisposed)
                        {
                            return;
                        }

                        if (refitColumns)
                        {
                            FitGridColumnsToContentFrame();
                        }

                        grid.ScrollBars = ScrollBars.None;
                        grid.ScrollBars = ScrollBars.Both;
                        grid.PerformLayout();
                        grid.Invalidate();
                    });
                }
                catch
                {
                }
            }
        }

        private void Grid_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
        {
            if (isApplyingGridLayout || e == null || e.Column == null)
            {
                return;
            }

            string key = GetGridColumnKey(e.Column);
            if (!string.IsNullOrEmpty(key))
            {
                userColumnWidths[key] = Math.Max(e.Column.MinimumWidth, e.Column.Width);
            }

            RefreshGridScrollbarsAfterDimensionChange(false);
        }

        private void Grid_RowHeightChanged(object sender, DataGridViewRowEventArgs e)
        {
            RefreshGridScrollbarsAfterDimensionChange(true);
        }

        private void Grid_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            RefreshGridScrollbarsAfterDimensionChange(true);
        }

        private void Grid_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)
        {
            RefreshGridScrollbarsAfterDimensionChange(true);
        }

        private void ResetGridVerticalScrollToTop()
        {
            if (grid == null || grid.Rows.Count == 0)
            {
                return;
            }

            try
            {
                grid.FirstDisplayedScrollingRowIndex = 0;
            }
            catch
            {
            }
        }

        private int GetGridPreferredContentHeight(int minimumHeight)
        {
            return Math.Max(1, minimumHeight);
        }


        private void Grid_ForwardMouseWheelToContentScrollPanel(object sender, MouseEventArgs e)
        {
            ScrollContentPanelByMouseWheel(e);
        }

        private void ScrollContentPanelByMouseWheel(MouseEventArgs e)
        {
            if (contentScrollPanel == null || e == null)
            {
                return;
            }

            int currentX = -contentScrollPanel.AutoScrollPosition.X;
            int currentY = contentScrollPanel.VerticalScroll.Value;
            int wheelLines = SystemInformation.MouseWheelScrollLines <= 0 ? 3 : SystemInformation.MouseWheelScrollLines;
            int rowStep = grid == null ? 24 : Math.Max(18, grid.RowTemplate.Height);
            int step = Math.Max(24, wheelLines * rowStep);
            int maxY = Math.Max(0, contentScrollPanel.VerticalScroll.Maximum - contentScrollPanel.ClientSize.Height);
            int nextY = e.Delta > 0 ? currentY - step : currentY + step;

            if (nextY < 0) nextY = 0;
            if (nextY > maxY) nextY = maxY;

            contentScrollPanel.AutoScrollPosition = new Point(currentX, nextY);
        }

        private void BuildExplorerHeader(Control parent, string pathText)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { this.Close(); },
                delegate { this.Close(); },
                delegate
                {
                    if (ConfirmDiscardUnsavedChanges())
                    {
                        LoadStoreToGrid(OviaBarListMappingStore.LoadDefault());
                    }
                },
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
            if (target == "MAIN" || target == "SETTINGS")
            {
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    workspace.NavigateToMain();
                    return;
                }

                this.Close();
            }
        }


        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1180, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "SETTINGS", companyId, userId);
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
            int settingsStart = breadcrumb.Text.IndexOf("환경설정");
            if (settingsStart >= 0)
            {
                breadcrumb.Links.Add(settingsStart, "환경설정".Length, "SETTINGS");
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
            textBox.Text = pathText == null ? "" : pathText.Replace("  ›  ", "\\");
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = OviaFluentTheme.FontInput(9.3F, FontStyle.Regular);
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

        private LinkLabel CreateBreadcrumbLabel()
        {
            LinkLabel label = new LinkLabel();
            label.Text = "";
            label.AutoSize = false;
            label.Size = new Size(880, 22);
            label.Location = new Point(10, 6);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontSystem(9.3F, FontStyle.Regular);
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

        private void Breadcrumb_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string target = e.Link.LinkData == null ? "" : e.Link.LinkData.ToString();
            NavigateByWorkspacePath(target);
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

        private Button CreateButton(string text, int x, int y)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y + 1);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, text);

            return button;
        }

        private void LoadStoreToGrid(OviaBarListMappingStore store)
        {
            if (store == null || store.StandardColumns == null || store.StandardColumns.Count == 0)
            {
                store = OviaBarListMappingStore.CreateBuiltInDefault();
            }

            grid.SuspendLayout();
            restoringSnapshot = true;

            try
            {
                grid.Columns.Clear();
                grid.Rows.Clear();
                aliasColumnSeed = 0;

                DataGridViewTextBoxColumn noColumn = new DataGridViewTextBoxColumn();
                noColumn.Name = "No";
                noColumn.HeaderText = "순서";
                noColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                noColumn.Width = 58;
                noColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                noColumn.MinimumWidth = 50;
                noColumn.FillWeight = 55;
                noColumn.ReadOnly = true;
                noColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
                noColumn.Frozen = false;
                noColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns.Add(noColumn);

                DataGridViewTextBoxColumn displayColumn = new DataGridViewTextBoxColumn();
                displayColumn.Name = "DisplayName";
                displayColumn.HeaderText = "OVIA 기본 헤더";
                displayColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                displayColumn.Width = 145;
                displayColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                displayColumn.MinimumWidth = 130;
                displayColumn.FillWeight = 140;
                displayColumn.ReadOnly = true;
                displayColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
                displayColumn.Frozen = false;
                displayColumn.DefaultCellStyle.BackColor = OviaFluentTheme.AppBackgroundAlt;
                displayColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns.Add(displayColumn);

                int maxAliasCount = 2;
                int i;

                for (i = 0; i < store.StandardColumns.Count; i++)
                {
                    List<string> aliases = GetEditableAliases(store.StandardColumns[i]);

                    if (aliases.Count > maxAliasCount)
                    {
                        maxAliasCount = aliases.Count;
                    }
                }

                for (i = 0; i < maxAliasCount; i++)
                {
                    AddAliasColumn();
                }

                for (i = 0; i < store.StandardColumns.Count; i++)
                {
                    OviaBarListMappingColumn col = store.StandardColumns[i];
                    List<string> aliases = GetEditableAliases(col);
                    object[] values = new object[grid.Columns.Count];
                    int j;

                    values[0] = (i + 1).ToString();
                    values[1] = col.DisplayName;

                    for (j = 0; j < aliases.Count && j + 2 < values.Length; j++)
                    {
                        values[j + 2] = aliases[j];
                    }

                    int rowIndex = grid.Rows.Add(values);
                    grid.Rows[rowIndex].Tag = col;
                }

                lblStatus.Text = "현재 매핑 사전: " + store.Version + "  |  저장 위치: " + OviaBarListMappingStore.GetWritableMappingFilePath();
                lblStatus.ForeColor = TextSub;
            }
            finally
            {
                restoringSnapshot = false;
                NormalizeAliasColumnHeadersByDisplayOrder();
                ApplyColumnHeaderAlignment();
                changedCells.Clear();
                activeRowIndex = -1;
                activeColumnIndex = -1;
                selectedCellRowIndex = -1;
                selectedCellColumnIndex = -1;
                editingCellRowIndex = -1;
                editingCellColumnIndex = -1;
                ApplyActiveRowHighlight();
                grid.ResumeLayout();
                ApplyWorkspaceLayout();
            }
        }

        private void AddAliasColumn_Click(object sender, EventArgs e)
        {
            PushUndoSnapshot();
            AddAliasColumn();
            NormalizeAliasColumnHeadersByDisplayOrder();
            ApplyColumnHeaderAlignment();
            UpdateSaveButtonVisibility();
            lblStatus.Text = "매핑 열을 추가했습니다. 필요한 헤더명을 입력한 뒤 저장하세요.";
            lblStatus.ForeColor = TextSub;
        }

        private void AddAliasColumn()
        {
            aliasColumnSeed++;

            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = "Alias_" + aliasColumnSeed.ToString();
            column.HeaderText = "매핑 " + aliasColumnSeed.ToString();
            column.Width = 118;
            column.Tag = 118;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = 85;
            column.FillWeight = 100;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.ReadOnly = false;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(column);
        }

        private void ClearSelectedCell_Click(object sender, EventArgs e)
        {
            ClearSelectedCellWithConfirm();
        }

        private void ResetDefault_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                "OVIA 기본 BarList 매핑값으로 화면을 되돌리시겠습니까?\r\n\r\n저장하기를 클릭해야 실제 설정 파일에 반영됩니다.",
                "OVIA 기본값 복원",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            PushUndoSnapshot();
            LoadStoreToGrid(OviaBarListMappingStore.CreateBuiltInDefault());
            UpdateSaveButtonVisibility();
            lblStatus.Text = "기본값으로 화면을 복원했습니다. 저장하기를 클릭하면 기본값이 설정 파일에 저장됩니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!HasUnsavedChanges())
            {
                UpdateSaveButtonVisibility();
                return;
            }

            DialogResult result = MessageBox.Show(
                "현재 설정을 저장하시겠습니까?\r\n\r\n저장 후 새로 불러오는 BarList CSV부터 변경된 매핑 기준이 적용됩니다.",
                "OVIA BarList 항목 매핑 저장",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                OviaBarListMappingStore newStore = BuildStoreFromGrid();
                newStore.SaveToDefaultPath();

                lblStatus.Text = "저장 완료: " + OviaBarListMappingStore.GetWritableMappingFilePath();
                lblStatus.ForeColor = OviaFluentTheme.Success;
                changedCells.Clear();
                undoStack.Clear();
                UpdateSaveButtonVisibility();
                redoStack.Clear();
                ApplyActiveRowHighlight();

                MessageBox.Show(
                    "BarList 항목 매핑을 저장했습니다.\r\n\r\n이미 열린 BarList 데이터는 다시 불러오면 변경된 기준으로 매핑됩니다.",
                    "OVIA 저장 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                OviaNotificationStore.AddWorkLog(companyId, userId, "BarList 항목 매핑 저장", OviaMenuHelpStore.GetWorkspacePath("BARLIST_MAPPING", "메인  ›  환경설정  ›  BarList 항목 매핑"));
            }
            catch (Exception ex)
            {
                lblStatus.Text = "저장 오류: " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;

                MessageBox.Show(
                    "BarList 항목 매핑 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 저장 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        public bool CanLeaveWorkspaceScreen()
        {
            return ConfirmDiscardUnsavedChanges();
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }

        public bool HasUnsavedWorkspaceData()
        {
            return HasUnsavedChanges();
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "BarList 항목 매핑";
        }

        private void FrmBarListMappingManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!HasUnsavedChanges())
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 BarList 항목 매핑 변경 내용이 있습니다.\r\n\r\n이 화면을 닫으면 저장되지 않은 변경 내용은 사라집니다.\r\n그래도 닫으시겠습니까?",
                "OVIA 변경 내용 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            return result == DialogResult.Yes;
        }

        private void UpdateSaveButtonVisibility()
        {
            if (btnSave == null)
            {
                return;
            }

            bool hasChanges = HasUnsavedChanges();
            btnSave.Visible = hasChanges;
            btnSave.Enabled = hasChanges;
        }

        private bool HasUnsavedChanges()
        {
            return changedCells.Count > 0 || undoStack.Count > 0;
        }

        private void BuildColumnContextMenu()
        {
            columnMenu = new ContextMenuStrip();
            deleteColumnMenuItem = new ToolStripMenuItem("해당 매핑 세로줄 전체삭제");
            deleteColumnMenuItem.Click += DeleteSelectedAliasColumn_Click;
            columnMenu.Items.Add(deleteColumnMenuItem);
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0)
            {
                return;
            }

            if (IsAliasColumn(e.ColumnIndex))
            {
                SelectEntireColumn(e.ColumnIndex);

                if (e.Button == MouseButtons.Right)
                {
                    ShowColumnContextMenu(e.ColumnIndex);
                }

                return;
            }

            activeColumnIndex = -1;
            activeRowIndex = -1;
            selectedCellRowIndex = -1;
            selectedCellColumnIndex = -1;
            ApplyActiveRowHighlight();

            if (e.Button == MouseButtons.Right)
            {
                columnMenuIndex = e.ColumnIndex;
                deleteColumnMenuItem.Enabled = false;
                deleteColumnMenuItem.Text = "고정 헤더는 삭제할 수 없습니다";
                columnMenu.Show(grid, grid.PointToClient(Cursor.Position));
            }
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            suppressNextCellClick = true;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (IsAliasColumn(e.ColumnIndex))
            {
                SelectEntireColumn(e.ColumnIndex);
                ShowColumnContextMenu(e.ColumnIndex);
            }
            else
            {
                grid.DefaultCellStyle.SelectionBackColor = ActiveRowBackColor;
                activeColumnIndex = -1;
                activeRowIndex = e.RowIndex;
                selectedCellRowIndex = e.RowIndex;
                selectedCellColumnIndex = e.ColumnIndex;
                ApplyActiveRowHighlight();
            }
        }

        private void ShowColumnContextMenu(int columnIndex)
        {
            columnMenuIndex = columnIndex;
            deleteColumnMenuItem.Enabled = IsAliasColumn(columnIndex);
            deleteColumnMenuItem.Text = IsAliasColumn(columnIndex) ? "해당 매핑 세로줄 전체삭제" : "고정 헤더는 삭제할 수 없습니다";
            columnMenu.Show(grid, grid.PointToClient(Cursor.Position));
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (restoringSnapshot)
            {
                return;
            }

            if (suppressNextCellClick)
            {
                suppressNextCellClick = false;
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            grid.DefaultCellStyle.SelectionBackColor = ActiveRowBackColor;
            activeColumnIndex = -1;
            activeRowIndex = e.RowIndex;
            selectedCellRowIndex = e.RowIndex;
            selectedCellColumnIndex = e.ColumnIndex;
            ApplyActiveRowHighlight();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 2)
            {
                return;
            }

            activeColumnIndex = -1;
            activeRowIndex = e.RowIndex;
            selectedCellRowIndex = e.RowIndex;
            selectedCellColumnIndex = e.ColumnIndex;
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            ApplyActiveRowHighlight();
            grid.BeginEdit(true);
        }

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (restoringSnapshot || e.RowIndex < 0 || e.ColumnIndex < 2)
            {
                editBeforeSnapshot = null;
                editBeforeValue = "";
                editingCellRowIndex = -1;
                editingCellColumnIndex = -1;
                return;
            }

            editingCellRowIndex = e.RowIndex;
            editingCellColumnIndex = e.ColumnIndex;
            editBeforeSnapshot = CaptureSnapshot();
            editBeforeValue = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor = Color.White;
            grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (restoringSnapshot || editBeforeSnapshot == null || e.RowIndex < 0 || e.ColumnIndex < 2)
            {
                editBeforeSnapshot = null;
                editBeforeValue = "";
                editingCellRowIndex = -1;
                editingCellColumnIndex = -1;
                return;
            }

            string afterValue = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);

            if (!String.Equals(editBeforeValue, afterValue, StringComparison.Ordinal))
            {
                undoStack.Push(editBeforeSnapshot);
                redoStack.Clear();
                MarkCellChanged(e.RowIndex, e.ColumnIndex);
                lblStatus.Text = "셀 내용을 수정했습니다. Ctrl+Z로 되돌릴 수 있습니다.";
                lblStatus.ForeColor = TextSub;
            }

            editingCellRowIndex = -1;
            editingCellColumnIndex = -1;
            editBeforeSnapshot = null;
            editBeforeValue = "";
            ApplyActiveRowHighlight();
            grid.Invalidate();
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            bool isEditingCell = e.RowIndex == editingCellRowIndex && e.ColumnIndex == editingCellColumnIndex;
            bool isSelectedCell = e.RowIndex == selectedCellRowIndex && e.ColumnIndex == selectedCellColumnIndex && activeColumnIndex < 0;

            if (!isEditingCell && !isSelectedCell)
            {
                return;
            }

            if (isEditingCell)
            {
                e.CellStyle.BackColor = Color.White;
                e.CellStyle.SelectionBackColor = Color.White;
                e.CellStyle.SelectionForeColor = TextDark;
            }

            e.Paint(e.CellBounds, e.PaintParts);

            Rectangle rect = e.CellBounds;
            rect.Width = rect.Width - 1;
            rect.Height = rect.Height - 1;

            Color borderColor = isEditingCell ? EditCellBorderColor : ActiveCellBorderColor;
            int borderWidth = isEditingCell ? 3 : 2;

            using (Pen pen = new Pen(borderColor, borderWidth))
            {
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(pen, rect);
            }

            e.Handled = true;
        }

        private void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (editingCellRowIndex < 0 || editingCellColumnIndex < 2)
            {
                return;
            }

            if (e.Control != null)
            {
                e.Control.BackColor = Color.White;
                e.Control.ForeColor = TextDark;
            }
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                RedoLastAction();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                UndoLastAction();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode)
            {
                ClearSelectedCellWithConfirm();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void SelectEntireColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            grid.ClearSelection();
            grid.DefaultCellStyle.SelectionBackColor = ActiveColumnBackColor;
            activeRowIndex = -1;
            activeColumnIndex = columnIndex;
            selectedCellRowIndex = -1;
            selectedCellColumnIndex = -1;

            if (grid.Rows.Count > 0)
            {
                grid.CurrentCell = grid.Rows[0].Cells[columnIndex];
                grid.CurrentCell.Selected = false;
            }

            ApplyActiveRowHighlight();
        }

        private void DeleteSelectedAliasColumn_Click(object sender, EventArgs e)
        {
            DeleteAliasColumn(columnMenuIndex);
        }

        private void DeleteAliasColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (!IsAliasColumn(columnIndex))
            {
                MessageBox.Show(
                    "순서와 OVIA 기본 헤더는 고정값이므로 삭제할 수 없습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string headerText = grid.Columns[columnIndex].HeaderText;

            if (!ShowConfirmDialog("매핑 열 삭제", "선택한 [" + headerText + "] 세로 매핑 열 전체를 삭제하시겠습니까?", "삭제"))
            {
                return;
            }

            PushUndoSnapshot();
            grid.Columns.RemoveAt(columnIndex);
            NormalizeAliasColumnHeadersByDisplayOrder();
            ApplyColumnHeaderAlignment();
            activeRowIndex = -1;
            activeColumnIndex = -1;
            selectedCellRowIndex = -1;
            selectedCellColumnIndex = -1;
            ApplyActiveRowHighlight();

            UpdateSaveButtonVisibility();
            lblStatus.Text = "선택한 매핑 열 전체를 삭제했습니다. 저장하기 전까지 실제 설정에는 반영되지 않습니다.";
            lblStatus.ForeColor = TextSub;
            OviaNotificationStore.AddWorkLog(companyId, userId, "BarList 매핑 열 삭제", OviaMenuHelpStore.GetWorkspacePath("BARLIST_MAPPING", "메인  ›  환경설정  ›  BarList 항목 매핑"));
        }

        private bool IsAliasColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            string name = grid.Columns[columnIndex].Name;
            return name != null && name.StartsWith("Alias_", StringComparison.OrdinalIgnoreCase);
        }

        private void ClearSelectedCellWithConfirm()
        {
            if (grid.CurrentCell == null)
            {
                return;
            }

            if (grid.CurrentCell.ColumnIndex < 2)
            {
                MessageBox.Show(
                    "순서와 OVIA 기본 헤더는 고정값이므로 비울 수 없습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            if (!ShowConfirmDialog("매핑 셀 삭제", "선택한 매핑 셀의 내용을 삭제하시겠습니까?", "삭제"))
            {
                return;
            }

            PushUndoSnapshot();
            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            ShiftAliasCellsLeft(rowIndex, columnIndex);
            ClearChangedCellsFromAliasColumn(rowIndex, columnIndex);
            activeColumnIndex = -1;
            activeRowIndex = rowIndex;
            selectedCellRowIndex = rowIndex;
            selectedCellColumnIndex = columnIndex;
            ApplyActiveRowHighlight();
            UpdateSaveButtonVisibility();

            lblStatus.Text = "선택한 매핑 셀의 내용을 삭제했습니다.";
            lblStatus.ForeColor = TextSub;
            OviaNotificationStore.AddWorkLog(companyId, userId, "BarList 매핑 셀 삭제", OviaMenuHelpStore.GetWorkspacePath("BARLIST_MAPPING", "메인  ›  환경설정  ›  BarList 항목 매핑"));
        }

        private void ShiftAliasCellsLeft(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            List<DataGridViewColumn> aliasColumns = GetAliasColumnsByDisplayOrder();
            int startIndex = -1;
            int i;

            for (i = 0; i < aliasColumns.Count; i++)
            {
                if (aliasColumns[i].Index == columnIndex)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
            {
                return;
            }

            DataGridViewRow row = grid.Rows[rowIndex];

            for (i = startIndex; i < aliasColumns.Count - 1; i++)
            {
                row.Cells[aliasColumns[i].Index].Value = row.Cells[aliasColumns[i + 1].Index].Value;
            }

            if (aliasColumns.Count > 0)
            {
                row.Cells[aliasColumns[aliasColumns.Count - 1].Index].Value = "";
            }
        }

        private bool ShowConfirmDialog(string title, string message, string confirmText)
        {
            Form dialog = new Form();
            dialog.Text = title;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MinimizeBox = false;
            dialog.MaximizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.ClientSize = new Size(420, 170);
            dialog.BackColor = Color.White;
            dialog.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);

            Label label = new Label();
            label.Text = message;
            label.AutoSize = false;
            label.Location = new Point(24, 24);
            label.Size = new Size(372, 72);
            label.ForeColor = TextDark;
            label.BackColor = Color.White;
            dialog.Controls.Add(label);

            Button btnCancel = new OVIA.Desktop.Controls.OviaButton();
            btnCancel.Text = "취소";
            btnCancel.Size = OviaFluentTheme.MeasureButtonSize(btnCancel.Text);
            btnCancel.Location = new Point(208, 112);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.FlatStyle = FlatStyle.Flat;
            OviaFluentTheme.ApplyButton(btnCancel, OviaButtonRole.Neutral);
            dialog.Controls.Add(btnCancel);

            Button btnConfirm = new OVIA.Desktop.Controls.OviaButton();
            btnConfirm.Text = confirmText;
            btnConfirm.Size = OviaFluentTheme.MeasureButtonSize(btnConfirm.Text);
            btnConfirm.Location = new Point(304, 112);
            btnConfirm.DialogResult = DialogResult.OK;
            btnConfirm.FlatStyle = FlatStyle.Flat;
            OviaFluentTheme.ApplyButton(btnConfirm, OviaButtonRole.Danger);
            dialog.Controls.Add(btnConfirm);

            dialog.AcceptButton = btnConfirm;
            dialog.CancelButton = btnCancel;

            DialogResult result = dialog.ShowDialog(this);
            dialog.Dispose();

            return result == DialogResult.OK;
        }

        private void ApplyActiveRowHighlight()
        {
            if (grid == null || grid.Rows == null)
            {
                return;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                int c;

                for (c = 0; c < grid.Columns.Count; c++)
                {
                    DataGridViewCell cell = grid.Rows[r].Cells[c];

                    if (r == editingCellRowIndex && c == editingCellColumnIndex)
                    {
                        cell.Style.BackColor = Color.White;
                    }
                    else if (c == activeColumnIndex && IsAliasColumn(c))
                    {
                        cell.Style.BackColor = ActiveColumnBackColor;
                    }
                    else if (r == activeRowIndex)
                    {
                        cell.Style.BackColor = ActiveRowBackColor;
                    }
                    else if (grid.Columns[c].Name != null && String.Equals(grid.Columns[c].Name, "DisplayName", StringComparison.OrdinalIgnoreCase))
                    {
                        cell.Style.BackColor = OviaFluentTheme.AppBackgroundAlt;
                    }
                    else
                    {
                        cell.Style.BackColor = Color.Empty;
                    }

                    ApplyChangedCellForeColor(r, c);
                }
            }

            grid.Invalidate();
        }

        private void ApplyChangedCellForeColor(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            string text = Convert.ToString(cell.Value);

            if (columnIndex >= 2 && text != null && text.Trim() != "" && changedCells.Contains(GetCellKey(rowIndex, columnIndex)))
            {
                cell.Style.ForeColor = ChangedTextColor;
            }
            else
            {
                cell.Style.ForeColor = Color.Empty;
            }
        }

        private void MarkCellChanged(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 2 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            changedCells.Add(GetCellKey(rowIndex, columnIndex));
            UpdateSaveButtonVisibility();
        }

        private void ClearChangedCellsFromAliasColumn(int rowIndex, int columnIndex)
        {
            List<DataGridViewColumn> aliasColumns = GetAliasColumnsByDisplayOrder();
            bool clear = false;
            int i;

            for (i = 0; i < aliasColumns.Count; i++)
            {
                if (aliasColumns[i].Index == columnIndex)
                {
                    clear = true;
                }

                if (clear)
                {
                    changedCells.Remove(GetCellKey(rowIndex, aliasColumns[i].Index));
                    ApplyChangedCellForeColor(rowIndex, aliasColumns[i].Index);
                }
            }
        }

        private string GetCellKey(int rowIndex, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return rowIndex.ToString() + "|";
            }

            return rowIndex.ToString() + "|" + grid.Columns[columnIndex].Name;
        }

        private void ApplyColumnHeaderAlignment()
        {
            if (grid == null || grid.Columns == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private void NormalizeAliasColumnHeadersByDisplayOrder()
        {
            List<DataGridViewColumn> aliasColumns = GetAliasColumnsByDisplayOrder();
            int i;

            for (i = 0; i < aliasColumns.Count; i++)
            {
                aliasColumns[i].HeaderText = "매핑 " + (i + 1).ToString();
            }
        }

        private void PushUndoSnapshot()
        {
            if (restoringSnapshot || grid == null)
            {
                return;
            }

            undoStack.Push(CaptureSnapshot());
            redoStack.Clear();
        }

        private GridSnapshot CaptureSnapshot()
        {
            GridSnapshot snapshot = new GridSnapshot();
            snapshot.AliasColumnSeed = aliasColumnSeed;
            snapshot.ActiveRowIndex = activeRowIndex;
            snapshot.ActiveColumnIndex = activeColumnIndex;
            snapshot.SelectedCellRowIndex = selectedCellRowIndex;
            snapshot.SelectedCellColumnIndex = selectedCellColumnIndex;
            snapshot.CurrentRowIndex = grid.CurrentCell == null ? -1 : grid.CurrentCell.RowIndex;
            snapshot.CurrentColumnIndex = grid.CurrentCell == null ? -1 : grid.CurrentCell.ColumnIndex;

            foreach (string key in changedCells)
            {
                snapshot.ChangedCellKeys.Add(key);
            }

            int c;
            int r;

            for (c = 0; c < grid.Columns.Count; c++)
            {
                DataGridViewColumn column = grid.Columns[c];
                ColumnSnapshot columnSnapshot = new ColumnSnapshot();
                columnSnapshot.Name = column.Name;
                columnSnapshot.HeaderText = column.HeaderText;
                columnSnapshot.Width = column.Width;
                columnSnapshot.ReadOnly = column.ReadOnly;
                columnSnapshot.Frozen = column.Frozen;
                columnSnapshot.DisplayIndex = column.DisplayIndex;
                snapshot.Columns.Add(columnSnapshot);
            }

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                List<string> rowValues = new List<string>();

                for (c = 0; c < grid.Columns.Count; c++)
                {
                    rowValues.Add(Convert.ToString(grid.Rows[r].Cells[c].Value));
                }

                snapshot.Values.Add(rowValues);
                snapshot.RowTags.Add(grid.Rows[r].Tag as OviaBarListMappingColumn);
            }

            return snapshot;
        }

        private void ApplySnapshot(GridSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            restoringSnapshot = true;
            grid.SuspendLayout();

            try
            {
                grid.Columns.Clear();
                grid.Rows.Clear();

                int c;
                int r;

                for (c = 0; c < snapshot.Columns.Count; c++)
                {
                    ColumnSnapshot columnSnapshot = snapshot.Columns[c];
                    DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                    column.Name = columnSnapshot.Name;
                    column.HeaderText = columnSnapshot.HeaderText;
                    column.Width = columnSnapshot.Width;
                    column.Tag = columnSnapshot.Width;
                    column.ReadOnly = columnSnapshot.ReadOnly;
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
                    column.Frozen = columnSnapshot.Frozen;
                    column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

                    if (String.Equals(columnSnapshot.Name, "No", StringComparison.OrdinalIgnoreCase))
                    {
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }
                    else if (String.Equals(columnSnapshot.Name, "DisplayName", StringComparison.OrdinalIgnoreCase))
                    {
                        column.DefaultCellStyle.BackColor = OviaFluentTheme.AppBackgroundAlt;
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }
                    else
                    {
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }

                    grid.Columns.Add(column);
                }

                for (c = 0; c < snapshot.Columns.Count; c++)
                {
                    try
                    {
                        if (snapshot.Columns[c].DisplayIndex >= 0 && snapshot.Columns[c].DisplayIndex < grid.Columns.Count)
                        {
                            grid.Columns[c].DisplayIndex = snapshot.Columns[c].DisplayIndex;
                        }
                    }
                    catch
                    {
                    }
                }

                for (r = 0; r < snapshot.Values.Count; r++)
                {
                    object[] values = new object[grid.Columns.Count];

                    for (c = 0; c < grid.Columns.Count && c < snapshot.Values[r].Count; c++)
                    {
                        values[c] = snapshot.Values[r][c];
                    }

                    int rowIndex = grid.Rows.Add(values);

                    if (r < snapshot.RowTags.Count)
                    {
                        grid.Rows[rowIndex].Tag = snapshot.RowTags[r];
                    }
                }

                aliasColumnSeed = snapshot.AliasColumnSeed;
                activeRowIndex = snapshot.ActiveRowIndex;
                activeColumnIndex = snapshot.ActiveColumnIndex;
                selectedCellRowIndex = snapshot.SelectedCellRowIndex;
                selectedCellColumnIndex = snapshot.SelectedCellColumnIndex;
                editingCellRowIndex = -1;
                editingCellColumnIndex = -1;
                changedCells.Clear();

                int k;

                for (k = 0; k < snapshot.ChangedCellKeys.Count; k++)
                {
                    changedCells.Add(snapshot.ChangedCellKeys[k]);
                }

                if (snapshot.CurrentRowIndex >= 0 && snapshot.CurrentRowIndex < grid.Rows.Count && snapshot.CurrentColumnIndex >= 0 && snapshot.CurrentColumnIndex < grid.Columns.Count)
                {
                    grid.CurrentCell = grid.Rows[snapshot.CurrentRowIndex].Cells[snapshot.CurrentColumnIndex];
                }
            }
            finally
            {
                NormalizeAliasColumnHeadersByDisplayOrder();
                ApplyColumnHeaderAlignment();
                ApplyActiveRowHighlight();
                grid.ResumeLayout();
                restoringSnapshot = false;
            }
        }

        private void UndoLastAction()
        {
            if (undoStack.Count == 0)
            {
                lblStatus.Text = "되돌릴 작업이 없습니다.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }

            GridSnapshot current = CaptureSnapshot();
            GridSnapshot previous = undoStack.Pop();
            redoStack.Push(current);
            ApplySnapshot(previous);

            lblStatus.Text = "이전 상태로 되돌렸습니다. Shift+Ctrl+Z로 다시 실행할 수 있습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void RedoLastAction()
        {
            if (redoStack.Count == 0)
            {
                lblStatus.Text = "다시 실행할 작업이 없습니다.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }

            GridSnapshot current = CaptureSnapshot();
            GridSnapshot next = redoStack.Pop();
            undoStack.Push(current);
            ApplySnapshot(next);

            lblStatus.Text = "다시 실행했습니다. Ctrl+Z로 되돌릴 수 있습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private class GridSnapshot
        {
            public List<ColumnSnapshot> Columns = new List<ColumnSnapshot>();
            public List<List<string>> Values = new List<List<string>>();
            public List<OviaBarListMappingColumn> RowTags = new List<OviaBarListMappingColumn>();
            public int CurrentRowIndex = -1;
            public int CurrentColumnIndex = -1;
            public int ActiveRowIndex = -1;
            public int ActiveColumnIndex = -1;
            public int SelectedCellRowIndex = -1;
            public int SelectedCellColumnIndex = -1;
            public int AliasColumnSeed = 0;
            public List<string> ChangedCellKeys = new List<string>();
        }

        private class ColumnSnapshot
        {
            public string Name = "";
            public string HeaderText = "";
            public int Width = 118;
            public bool ReadOnly = false;
            public bool Frozen = false;
            public int DisplayIndex = 0;
        }

        private OviaBarListMappingStore BuildStoreFromGrid()
        {
            OviaBarListMappingStore store = new OviaBarListMappingStore();
            store.Version = "ovia-barlist-mapping-" + DateTime.Now.ToString("yyyy.MM.dd.HHmmss");
            store.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            List<DataGridViewColumn> aliasColumns = GetAliasColumnsByDisplayOrder();
            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                DataGridViewRow row = grid.Rows[r];

                if (row == null || row.IsNewRow)
                {
                    continue;
                }

                OviaBarListMappingColumn source = row.Tag as OviaBarListMappingColumn;

                if (source == null)
                {
                    continue;
                }

                string displayName = GetCellText(row, "DisplayName");

                if (displayName.Trim() == "")
                {
                    displayName = source.DisplayName;
                }

                OviaBarListMappingColumn col = new OviaBarListMappingColumn();
                col.Key = source.Key;
                col.DisplayName = displayName.Trim();
                col.DataType = source.DataType;
                col.Priority = source.Priority;

                AddAlias(col.Aliases, col.DisplayName);

                int i;

                for (i = 0; i < aliasColumns.Count; i++)
                {
                    string text = Convert.ToString(row.Cells[aliasColumns[i].Index].Value);
                    AddAliasText(col.Aliases, text);
                }

                store.StandardColumns.Add(col);
            }

            return store;
        }

        private List<DataGridViewColumn> GetAliasColumnsByDisplayOrder()
        {
            List<DataGridViewColumn> columns = new List<DataGridViewColumn>();
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];

                if (column.Name != null && column.Name.StartsWith("Alias_", StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(column);
                }
            }

            columns.Sort(delegate (DataGridViewColumn a, DataGridViewColumn b)
            {
                return a.DisplayIndex.CompareTo(b.DisplayIndex);
            });

            return columns;
        }

        private string GetCellText(DataGridViewRow row, string columnName)
        {
            if (row == null || !grid.Columns.Contains(columnName))
            {
                return "";
            }

            object value = row.Cells[columnName].Value;

            if (value == null)
            {
                return "";
            }

            return Convert.ToString(value).Trim();
        }

        private void AddAliasText(List<string> list, string text)
        {
            if (text == null)
            {
                return;
            }

            string[] parts = text.Split(new char[] { ',', ';', '|', '，', '；' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            if (parts.Length == 0 && text.Trim() != "")
            {
                AddAlias(list, text.Trim());
                return;
            }

            for (i = 0; i < parts.Length; i++)
            {
                AddAlias(list, parts[i].Trim());
            }
        }

        private void AddAlias(List<string> list, string value)
        {
            if (list == null || value == null)
            {
                return;
            }

            value = value.Trim();

            if (value == "")
            {
                return;
            }

            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (String.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add(value);
        }

        private List<string> GetEditableAliases(OviaBarListMappingColumn col)
        {
            List<string> list = new List<string>();

            if (col == null || col.Aliases == null)
            {
                return list;
            }

            int i;

            for (i = 0; i < col.Aliases.Count; i++)
            {
                string value = col.Aliases[i] == null ? "" : col.Aliases[i].Trim();

                if (value == "")
                {
                    continue;
                }

                if (String.Equals(value, col.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddAlias(list, value);
            }

            return list;
        }
    }
}
