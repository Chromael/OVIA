using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmMenuManager : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;
        private readonly Font iconCellFont = OviaIconFont.Create(13.5F, FontStyle.Regular);

        private Panel commandBarPanel;
        private Panel contentScrollPanel;
        private Panel bottomButtonPanel;
        private DataGridView grid;
        private Label lblStatus;
        private Button btnSave;
        private Button btnIconReference;
        private Button btnClose;
        private List<OviaMenuSetting> rows = new List<OviaMenuSetting>();
        private bool isDirty;
        private bool isLoading;
        private bool isApplyingRowHeight;
        private bool isSynchronizingGridValues;
        private string originalRowsSignature = string.Empty;
        private const string FluentIconReferenceUrl = "https://learn.microsoft.com/ko-kr/windows/apps/design/iconography/segoe-fluent-icons-font";

        private bool isApplyingGridLayout = false;
        private bool isApplyingWorkspaceBounds = false;
        private readonly Dictionary<string, int> userColumnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public FrmMenuManager(string companyId, string userId)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.canEdit = OviaSystemSettingsStore.IsSystemAdministrator(this.companyId, this.userId);

            BuildUI();
            LoadRowsToGrid(OviaMenuSettingsStore.Load());
        }

        private void BuildUI()
        {
            SuspendLayout();
            OviaFluentTheme.ApplyForm(this);
            Controls.Clear();

            Text = "OVIA - 메뉴관리";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(1180, 720);
            MinimumSize = Size.Empty;
            BackColor = SurfaceColor;
            FormClosing += FrmMenuManager_FormClosing;
            Resize += WorkspaceContent_Resize;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildGrid(this);
            BuildButtons(this);
            BuildStatus(this);

            ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                OviaMenuSettingsStore.GetWorkspacePath("MENU_MANAGER", "메인  ›  환경설정  ›  메뉴관리"),
                delegate { Close(); },
                delegate { Close(); },
                delegate { LoadRowsToGrid(OviaMenuSettingsStore.Load()); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    if (target == "MAIN" || target == "SETTINGS")
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
                });
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBarPanel = commandBar;
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

        private void BuildGrid(Control parent)
        {
            contentScrollPanel = new Panel();
            contentScrollPanel.Location = new Point(0, 98);
            contentScrollPanel.Size = new Size(1180, 430);
            contentScrollPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            contentScrollPanel.BackColor = SurfaceColor;
            contentScrollPanel.Margin = Padding.Empty;
            contentScrollPanel.Padding = Padding.Empty;
            contentScrollPanel.AutoScrollMargin = Size.Empty;
            contentScrollPanel.AutoScroll = false;
            parent.Controls.Add(contentScrollPanel);

            grid = new DataGridView();
            grid.Location = Point.Empty;
            grid.Size = new Size(1180, 430);
            grid.Anchor = AnchorStyles.None;
            grid.Dock = DockStyle.Fill;
            grid.ScrollBars = ScrollBars.Both;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 42;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Regular);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 244, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 34;
            grid.RowTemplate.Resizable = DataGridViewTriState.False;
            grid.AllowUserToResizeColumns = true;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.RowsDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellPainting += Grid_CellPainting;
            grid.DataError += Grid_DataError;
            grid.ColumnWidthChanged += Grid_ColumnWidthChanged;
            grid.RowHeightChanged += Grid_RowHeightChanged;
            grid.RowsAdded += Grid_RowsAdded;
            grid.RowsRemoved += Grid_RowsRemoved;
            // DataGridView 자체 세로 스크롤을 사용한다. 외부 Panel AutoScroll 전달은 가로/세로 중복 스크롤의 원인이므로 사용하지 않는다.
            OviaFluentTheme.ApplyDataGrid(grid);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            DataGridViewTextBoxColumn levelCol = new DataGridViewTextBoxColumn();
            levelCol.Name = "LevelMarker";
            levelCol.HeaderText = "단계 구분";
            levelCol.Width = 76;
            levelCol.MinimumWidth = 76;
            levelCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            levelCol.ReadOnly = true;
            levelCol.Resizable = DataGridViewTriState.False;
            levelCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            levelCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            levelCol.DefaultCellStyle.Padding = new Padding(3, 0, 0, 0);
            grid.Columns.Add(levelCol);

            DataGridViewTextBoxColumn iconCol = new DataGridViewTextBoxColumn();
            iconCol.Name = "Icon";
            iconCol.HeaderText = "아이콘";
            iconCol.Width = 52;
            iconCol.MinimumWidth = 52;
            iconCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            iconCol.ReadOnly = true;
            iconCol.Resizable = DataGridViewTriState.False;
            iconCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            iconCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            iconCol.DefaultCellStyle.Font = iconCellFont;
            grid.Columns.Add(iconCol);

            DataGridViewTextBoxColumn iconCodeCol = new DataGridViewTextBoxColumn();
            iconCodeCol.Name = "IconCode";
            iconCodeCol.HeaderText = "아이콘 CODE";
            iconCodeCol.Width = 106;
            iconCodeCol.MinimumWidth = 106;
            iconCodeCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            iconCodeCol.ReadOnly = !canEdit;
            iconCodeCol.Resizable = DataGridViewTriState.False;
            iconCodeCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            iconCodeCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(iconCodeCol);

            DataGridViewTextBoxColumn menuCol = new DataGridViewTextBoxColumn();
            menuCol.Name = "MenuName";
            menuCol.HeaderText = "메뉴 / 페이지";
            menuCol.Width = 260;
            menuCol.MinimumWidth = 220;
            menuCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            menuCol.ReadOnly = !canEdit;
            menuCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(menuCol);

            DataGridViewTextBoxColumn keyCol = new DataGridViewTextBoxColumn();
            keyCol.Name = "Key";
            keyCol.HeaderText = "메뉴키";
            keyCol.Width = 190;
            keyCol.MinimumWidth = 160;
            keyCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            keyCol.ReadOnly = true;
            keyCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(keyCol);

            DataGridViewTextBoxColumn modulePathCol = new DataGridViewTextBoxColumn();
            modulePathCol.Name = "ModulePath";
            modulePathCol.HeaderText = "경로";
            modulePathCol.Width = 360;
            modulePathCol.MinimumWidth = 260;
            modulePathCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            modulePathCol.ReadOnly = true;
            modulePathCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(modulePathCol);

            DataGridViewCheckBoxColumn erpLoadCol = new DataGridViewCheckBoxColumn();
            erpLoadCol.Name = "ErpLoad";
            erpLoadCol.HeaderText = "ERP로드";
            erpLoadCol.Width = 78;
            erpLoadCol.MinimumWidth = 72;
            erpLoadCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            erpLoadCol.ReadOnly = true;
            erpLoadCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(erpLoadCol);

            DataGridViewTextBoxColumn erpModuleNameCol = new DataGridViewTextBoxColumn();
            erpModuleNameCol.Name = "ErpModuleName";
            erpModuleNameCol.HeaderText = "ERP모듈명";
            erpModuleNameCol.Width = 150;
            erpModuleNameCol.MinimumWidth = 120;
            erpModuleNameCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            erpModuleNameCol.ReadOnly = !canEdit;
            erpModuleNameCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            erpModuleNameCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns.Add(erpModuleNameCol);

            DataGridViewCheckBoxColumn enabledCol = new DataGridViewCheckBoxColumn();
            enabledCol.Name = "Enabled";
            enabledCol.HeaderText = "사용";
            enabledCol.Width = 70;
            enabledCol.MinimumWidth = 64;
            enabledCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            enabledCol.ReadOnly = true;
            enabledCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(enabledCol);

            DataGridViewComboBoxColumn permissionCol = new DataGridViewComboBoxColumn();
            permissionCol.Name = "PermissionLevel";
            permissionCol.HeaderText = "사용자 권한";
            permissionCol.Width = 110;
            permissionCol.MinimumWidth = 100;
            permissionCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            permissionCol.ReadOnly = !canEdit;
            permissionCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            permissionCol.FlatStyle = FlatStyle.Flat;
            int permissionLevel;
            for (permissionLevel = 1; permissionLevel <= 10; permissionLevel++)
            {
                permissionCol.Items.Add(GetPermissionLevelText(permissionLevel));
            }
            grid.Columns.Add(permissionCol);

            ApplyGridHeaderCenterAlignment();

            grid.CellClick += Grid_CellClick;
            contentScrollPanel.Controls.Add(grid);
        }

        private void ApplyGridHeaderCenterAlignment()
        {
            if (grid == null)
            {
                return;
            }

            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private void BuildButtons(Control parent)
        {
            int buttonTop = 0;
            int initialButtonPanelHeight = Math.Max(1, Math.Min(50, OviaFluentTheme.ButtonHeight));

            bottomButtonPanel = new Panel();
            bottomButtonPanel.Location = new Point(0, 576);
            bottomButtonPanel.Size = new Size(1180, initialButtonPanelHeight);
            bottomButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottomButtonPanel.BackColor = SurfaceColor;
            bottomButtonPanel.Margin = Padding.Empty;
            bottomButtonPanel.Padding = Padding.Empty;
            parent.Controls.Add(bottomButtonPanel);

            btnIconReference = CreateButton("아이콘입력 참조", 25, buttonTop, 142);
            btnIconReference.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            btnIconReference.Enabled = true;
            OviaFluentTheme.ApplyButton(btnIconReference, OviaButtonRole.Neutral);
            btnIconReference.Click += IconReference_Click;
            bottomButtonPanel.Controls.Add(btnIconReference);

            btnSave = CreateButton("저장하기", 902, buttonTop, 120);
            btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSave.BackColor = OviaFluentTheme.Accent;
            btnSave.ForeColor = Color.White;
            btnSave.Enabled = false;
            btnSave.Visible = false;
            btnSave.Click += Save_Click;
            bottomButtonPanel.Controls.Add(btnSave);

            btnClose = CreateButton("닫기", 1038, buttonTop, 110);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };
            bottomButtonPanel.Controls.Add(btnClose);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Location = new Point(0, 692);
            lblStatus.Size = new Size(1180, 28);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            parent.Controls.Add(lblStatus);
        }

        private Button CreateButton(string text, int x, int y, int width)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, text);
            return button;
        }

        private void LoadRowsToGrid(List<OviaMenuSetting> settings)
        {
            isLoading = true;
            rows = settings == null ? OviaMenuSettingsStore.CreateDefaultSettings() : settings;
            grid.Rows.Clear();

            int i;
            for (i = 0; i < rows.Count; i++)
            {
                OviaMenuSetting row = rows[i];
                if (IsBrowserOnlyErpShortcutRow(row))
                {
                    row.ErpLoad = false;
                    row.ErpModuleName = string.Empty;
                }

                int index = grid.Rows.Add();
                grid.Rows[index].Cells["LevelMarker"].Value = GetLevelMarker(row.Level);
                grid.Rows[index].Cells["Icon"].Value = OviaMenuSettingsStore.GetIconGlyphFromCode(row.IconCode, string.Empty);
                grid.Rows[index].Cells["IconCode"].Value = row.IconCode;
                grid.Rows[index].Cells["MenuName"].Value = row.MenuName;
                grid.Rows[index].Cells["Key"].Value = row.Key;
                grid.Rows[index].Cells["ModulePath"].Value = OviaMenuSettingsStore.GetModulePath(row.Key);
                grid.Rows[index].Cells["ErpLoad"].Value = row.ErpLoad;
                grid.Rows[index].Cells["ErpModuleName"].Value = row.ErpModuleName;
                grid.Rows[index].Cells["Enabled"].Value = row.Enabled;
                grid.Rows[index].Cells["PermissionLevel"].Value = GetPermissionLevelText(row.PermissionLevel);
                grid.Rows[index].Height = grid.RowTemplate.Height;
                grid.Rows[index].Resizable = DataGridViewTriState.False;
                grid.Rows[index].Tag = row;
            }

            originalRowsSignature = BuildRowsSignature(rows);
            isDirty = false;
            isLoading = false;
            UpdateSaveButtonVisibility();
            UpdateStatus(canEdit ? "메뉴별 사용 여부와 사용자 권한 레벨(1~10)을 관리합니다." : "메뉴관리는 레벨 10 사용자만 수정할 수 있습니다.");
            ApplyWorkspaceLayout();
        }

        private string GetLevelMarker(int level)
        {
            if (level <= 1) return "1차";
            if (level == 2) return "    2차└";
            return "        3차└";
        }

        private static bool IsBrowserOnlyErpShortcutRow(OviaMenuSetting row)
        {
            return row != null && OviaMenuSettingsStore.IsBrowserOnlyErpShortcut(row.Key);
        }

        private static bool IsErpModuleEditableRow(OviaMenuSetting row)
        {
            return row != null && !IsBrowserOnlyErpShortcutRow(row) && row.ErpLoad;
        }

        private string Indent(int level)
        {
            if (level <= 1) return "";
            if (level == 2) return "   └ ";
            return "       └ ";
        }

        private string Shorten(string text, int maxLength)
        {
            string value = text == null ? string.Empty : text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length <= maxLength)
            {
                return value;
            }
            return value.Substring(0, maxLength) + "…";
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            OviaMenuSetting row = grid.Rows[e.RowIndex].Tag as OviaMenuSetting;
            if (row == null)
            {
                return;
            }

            if (e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count && grid.Columns[e.ColumnIndex].Name == "Icon")
            {
                e.CellStyle.Font = iconCellFont;
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            if (e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count && grid.Columns[e.ColumnIndex].Name == "ErpModuleName")
            {
                bool moduleEditable = IsErpModuleEditableRow(row);
                e.CellStyle.ForeColor = moduleEditable ? TextDark : Color.FromArgb(150, 160, 174);
                e.CellStyle.BackColor = moduleEditable ? Color.White : Color.FromArgb(246, 248, 251);
            }

            if (e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count && grid.Columns[e.ColumnIndex].Name == "ErpLoad" && IsBrowserOnlyErpShortcutRow(row))
            {
                e.CellStyle.ForeColor = Color.FromArgb(150, 160, 174);
                e.CellStyle.BackColor = Color.FromArgb(246, 248, 251);
            }

            if (row.Level == 1)
            {
                grid.Rows[e.RowIndex].DefaultCellStyle.Font = OviaFluentTheme.FontData(8.8F, FontStyle.Bold);
                grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            }
            else
            {
                grid.Rows[e.RowIndex].DefaultCellStyle.Font = OviaFluentTheme.FontData(8.6F, FontStyle.Regular);
                grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.White;
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName != "Enabled" && columnName != "ErpLoad")
            {
                return;
            }

            OviaMenuSetting paintRow = grid.Rows[e.RowIndex].Tag as OviaMenuSetting;
            if (columnName == "ErpLoad" && IsBrowserOnlyErpShortcutRow(paintRow))
            {
                using (SolidBrush disabledBrush = new SolidBrush(Color.FromArgb(246, 248, 251)))
                {
                    e.Graphics.FillRectangle(disabledBrush, e.CellBounds);
                }
                using (Pen borderPen = new Pen(OviaFluentTheme.GridLine))
                {
                    e.Graphics.DrawRectangle(borderPen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);
                }
                e.Handled = true;
                return;
            }

            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);

            bool isChecked = false;
            if (e.Value != null && e.Value != DBNull.Value)
            {
                bool.TryParse(e.Value.ToString(), out isChecked);
            }

            int boxSize = OviaFluentTheme.CheckBoxSize;
            Rectangle boxRect = new Rectangle(
                e.CellBounds.Left + (e.CellBounds.Width - boxSize) / 2,
                e.CellBounds.Top + (e.CellBounds.Height - boxSize) / 2,
                boxSize,
                boxSize);

            Color borderColor = isChecked ? OviaFluentTheme.CheckBoxCheckedBorder : OviaFluentTheme.ControlBorder;
            Color backColor = isChecked ? OviaFluentTheme.CheckBoxCheckedBack : Color.White;

            using (GraphicsPath path = CreateRoundRectPath(new Rectangle(boxRect.X, boxRect.Y, boxRect.Width - 1, boxRect.Height - 1), OviaFluentTheme.CheckBoxRadius))
            using (SolidBrush brush = new SolidBrush(backColor))
            using (Pen borderPen = new Pen(borderColor, 1F))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            if (isChecked)
            {
                using (Pen checkPen = new Pen(Color.White, 1.8F))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(checkPen, new PointF[]
                    {
                        new PointF(boxRect.Left + 3.5F, boxRect.Top + 7.5F),
                        new PointF(boxRect.Left + 6.5F, boxRect.Top + 10.5F),
                        new PointF(boxRect.Left + 11.5F, boxRect.Top + 4.5F)
                    });
                }
            }

            e.Handled = true;
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

        private static GraphicsPath CreateRoundRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            int diameter = radius * 2;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid != null && grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || isSynchronizingGridValues || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            SyncRowFromGrid(e.RowIndex);
            MarkMenuSettingsDirty();
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || isSynchronizingGridValues || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            SyncRowFromGrid(e.RowIndex);
            MarkMenuSettingsDirty();
        }

        private void MarkMenuSettingsDirty()
        {
            if (isLoading || !canEdit)
            {
                return;
            }

            isDirty = HasMenuSettingsChanged();
            UpdateSaveButtonVisibility();
            UpdateStatus(isDirty ? "메뉴관리 설정이 변경되었습니다. 저장하기를 클릭하면 상단 메뉴에 반영됩니다." : "기존 저장값과 동일합니다. 저장할 변경사항이 없습니다.");
        }

        private bool HasMenuSettingsChanged()
        {
            return !string.Equals(BuildRowsSignature(rows), originalRowsSignature, StringComparison.Ordinal);
        }

        private string BuildRowsSignature(List<OviaMenuSetting> sourceRows)
        {
            if (sourceRows == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            int i;
            for (i = 0; i < sourceRows.Count; i++)
            {
                OviaMenuSetting row = sourceRows[i];
                if (row == null)
                {
                    continue;
                }

                sb.Append(row.Key == null ? string.Empty : row.Key.Trim());
                sb.Append('|');
                sb.Append(row.MenuName == null ? string.Empty : row.MenuName.Trim());
                sb.Append('|');
                sb.Append(row.Level.ToString());
                sb.Append('|');
                sb.Append(row.Enabled ? "1" : "0");
                sb.Append('|');
                sb.Append(OviaMenuSettingsStore.NormalizePermissionLevel(row.PermissionLevel).ToString());
                sb.Append('|');
                sb.Append(OviaMenuSettingsStore.NormalizeIconCode(row.IconCode));
                sb.Append('|');
                bool signatureErpLoad = !IsBrowserOnlyErpShortcutRow(row) && row.ErpLoad;
                string signatureErpModuleName = IsBrowserOnlyErpShortcutRow(row) ? string.Empty : OviaSystemSettingsStore.NormalizeErpModuleName(row.ErpModuleName);
                sb.Append(signatureErpLoad ? "1" : "0");
                sb.Append('|');
                sb.Append(signatureErpModuleName);
                sb.Append('|');
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void CommitGridEdit()
        {
            if (grid == null)
            {
                return;
            }

            try
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }

                grid.EndEdit();
            }
            catch
            {
            }
        }

        private void SyncRowFromGrid(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            OviaMenuSetting row = grid.Rows[rowIndex].Tag as OviaMenuSetting;
            if (row == null)
            {
                return;
            }

            row.MenuName = NormalizeMenuName(grid.Rows[rowIndex].Cells["MenuName"].Value, row.MenuName);
            row.IconCode = OviaMenuSettingsStore.NormalizeIconCode(GetCellText(rowIndex, "IconCode"));
            if (IsBrowserOnlyErpShortcutRow(row))
            {
                row.ErpLoad = false;
                row.ErpModuleName = string.Empty;
            }
            else
            {
                row.ErpLoad = GetCellBoolean(rowIndex, "ErpLoad");
                row.ErpModuleName = OviaSystemSettingsStore.NormalizeErpModuleName(GetCellText(rowIndex, "ErpModuleName"));
            }
            row.Enabled = GetCellBoolean(rowIndex, "Enabled");
            row.PermissionLevel = ParsePermissionLevel(grid.Rows[rowIndex].Cells["PermissionLevel"].Value, row.PermissionLevel);

            try
            {
                isSynchronizingGridValues = true;
                grid.Rows[rowIndex].Cells["MenuName"].Value = row.MenuName;
                grid.Rows[rowIndex].Cells["IconCode"].Value = row.IconCode;
                grid.Rows[rowIndex].Cells["Icon"].Value = OviaMenuSettingsStore.GetIconGlyphFromCode(row.IconCode, string.Empty);
                grid.Rows[rowIndex].Cells["ErpLoad"].Value = row.ErpLoad;
                grid.Rows[rowIndex].Cells["ErpModuleName"].Value = row.ErpModuleName;
            }
            finally
            {
                isSynchronizingGridValues = false;
            }
        }

        private void SyncAllRowsFromGrid()
        {
            if (grid == null)
            {
                return;
            }

            int i;
            for (i = 0; i < grid.Rows.Count; i++)
            {
                SyncRowFromGrid(i);
            }
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }


        private string GetCellText(int rowIndex, string columnName)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || !grid.Columns.Contains(columnName))
            {
                return string.Empty;
            }

            object value = grid.Rows[rowIndex].Cells[columnName].Value;
            return value == null || value == DBNull.Value ? string.Empty : value.ToString();
        }

        private string NormalizeMenuName(object value, string fallback)
        {
            string text = value == null || value == DBNull.Value ? string.Empty : value.ToString();
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text == string.Empty)
            {
                return fallback == null ? string.Empty : fallback.Trim();
            }

            return text;
        }

        private bool GetCellBoolean(int rowIndex, string columnName)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || !grid.Columns.Contains(columnName))
            {
                return false;
            }

            object value = grid.Rows[rowIndex].Cells[columnName].Value;
            if (value == null || value == DBNull.Value)
            {
                return false;
            }

            bool result;
            if (bool.TryParse(value.ToString(), out result))
            {
                return result;
            }

            return value.ToString() == "1";
        }

        private static string GetPermissionLevelText(int level)
        {
            int normalized = OviaMenuSettingsStore.NormalizePermissionLevel(level);
            return "레벨 " + normalized.ToString();
        }

        private static int ParsePermissionLevel(object value, int fallback)
        {
            if (value == null)
            {
                return OviaMenuSettingsStore.NormalizePermissionLevel(fallback);
            }

            string text = value.ToString().Trim();
            int i;
            for (i = 1; i <= 10; i++)
            {
                if (string.Equals(text, GetPermissionLevelText(i), StringComparison.OrdinalIgnoreCase) || text == i.ToString())
                {
                    return i;
                }
            }

            string digits = string.Empty;
            for (i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    digits += text[i];
                }
            }

            int parsed;
            if (int.TryParse(digits, out parsed))
            {
                return OviaMenuSettingsStore.NormalizePermissionLevel(parsed);
            }

            return OviaMenuSettingsStore.NormalizePermissionLevel(fallback);
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (!canEdit || grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName == "Enabled")
            {
                ToggleEnabledCell(e.RowIndex);
                return;
            }

            if (columnName == "ErpLoad")
            {
                ToggleErpLoadCell(e.RowIndex);
                return;
            }

            if (columnName == "PermissionLevel")
            {
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                grid.BeginEdit(true);
                ComboBox combo = grid.EditingControl as ComboBox;
                if (combo != null)
                {
                    combo.DroppedDown = true;
                }
            }
        }

        private void ToggleEnabledCell(int rowIndex)
        {
            if (!canEdit || grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || !grid.Columns.Contains("Enabled"))
            {
                return;
            }

            CommitGridEdit();

            bool currentValue = GetCellBoolean(rowIndex, "Enabled");
            try
            {
                isSynchronizingGridValues = true;
                grid.Rows[rowIndex].Cells["Enabled"].Value = !currentValue;
            }
            finally
            {
                isSynchronizingGridValues = false;
            }

            SyncRowFromGrid(rowIndex);
            MarkMenuSettingsDirty();
            grid.InvalidateCell(grid.Rows[rowIndex].Cells["Enabled"]);
        }

        private void ToggleErpLoadCell(int rowIndex)
        {
            if (!canEdit || grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || !grid.Columns.Contains("ErpLoad"))
            {
                return;
            }

            OviaMenuSetting row = grid.Rows[rowIndex].Tag as OviaMenuSetting;
            if (IsBrowserOnlyErpShortcutRow(row))
            {
                try
                {
                    isSynchronizingGridValues = true;
                    grid.Rows[rowIndex].Cells["ErpLoad"].Value = false;
                    grid.Rows[rowIndex].Cells["ErpModuleName"].Value = string.Empty;
                }
                finally
                {
                    isSynchronizingGridValues = false;
                }
                SyncRowFromGrid(rowIndex);
                grid.InvalidateRow(rowIndex);
                return;
            }

            CommitGridEdit();

            bool currentValue = GetCellBoolean(rowIndex, "ErpLoad");
            try
            {
                isSynchronizingGridValues = true;
                grid.Rows[rowIndex].Cells["ErpLoad"].Value = !currentValue;
            }
            finally
            {
                isSynchronizingGridValues = false;
            }

            SyncRowFromGrid(rowIndex);
            MarkMenuSettingsDirty();
            grid.InvalidateRow(rowIndex);
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (!canEdit || grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName != "MenuName" && columnName != "IconCode" && columnName != "ErpModuleName")
            {
                return;
            }

            if (columnName == "ErpModuleName")
            {
                OviaMenuSetting row = grid.Rows[e.RowIndex].Tag as OviaMenuSetting;
                if (!IsErpModuleEditableRow(row))
                {
                    return;
                }
            }

            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            grid.BeginEdit(true);
        }

        private void IconReference_Click(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = FluentIconReferenceUrl;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "아이콘 입력 참조 페이지를 여는 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 메뉴관리",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            CommitGridEdit();
            SyncAllRowsFromGrid();
            isDirty = HasMenuSettingsChanged();

            if (!isDirty)
            {
                UpdateSaveButtonVisibility();
                UpdateStatus("저장할 메뉴관리 변경사항이 없습니다.");
                return;
            }

            try
            {
                OviaMenuSettingsStore.Save(rows);
                originalRowsSignature = BuildRowsSignature(rows);
                isDirty = false;
                UpdateSaveButtonVisibility();
                if (commandBarPanel != null && !commandBarPanel.IsDisposed)
                {
                    OviaWorkspaceCommandBar.Populate(commandBarPanel, "SETTINGS", companyId, userId);
                }
                UpdateStatus("메뉴관리 설정이 저장되었습니다. 상단 메뉴에 즉시 반영됩니다.");
                OviaNotificationStore.AddWorkLog(companyId, userId, "메뉴관리 설정 저장", OviaMenuSettingsStore.GetWorkspacePath("MENU_MANAGER", "메인  ›  환경설정  ›  메뉴관리"));
            }
            catch (Exception ex)
            {
                isDirty = true;
                UpdateSaveButtonVisibility();
                UpdateStatus("메뉴관리 설정 저장 오류: " + ex.Message);
                MessageBox.Show(
                    "메뉴관리 설정을 저장하는 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 메뉴관리",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UpdateSaveButtonVisibility()
        {
            if (btnSave == null)
            {
                return;
            }

            btnSave.Visible = canEdit;
            btnSave.Enabled = canEdit && isDirty;

            if (bottomButtonPanel != null)
            {
                bottomButtonPanel.Visible = true;
            }
        }

        private void UpdateStatus(string text)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = text == null ? string.Empty : text;
            }
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
            int buttonVisualHeight = btnIconReference == null ? OviaFluentTheme.ButtonHeight : btnIconReference.Height;
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

            if (btnIconReference != null)
            {
                btnIconReference.Location = new Point(contentHorizontalInset, buttonTopInPanel);
            }
            if (btnClose != null)
            {
                int panelWidth = bottomButtonPanel == null ? width : Math.Max(1, bottomButtonPanel.ClientSize.Width);
                btnClose.Location = new Point(Math.Max(0, panelWidth - rightMargin - btnClose.Width), buttonTopInPanel);
            }
            if (btnSave != null)
            {
                int panelWidth = bottomButtonPanel == null ? width : Math.Max(1, bottomButtonPanel.ClientSize.Width);
                int saveLeft = btnClose == null ? panelWidth - rightMargin - btnSave.Width : btnClose.Left - buttonGap - btnSave.Width;
                btnSave.Location = new Point(Math.Max(0, saveLeft), buttonTopInPanel);
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

        private bool IsCompactFixedColumn(DataGridViewColumn column)
        {
            if (column == null || string.IsNullOrEmpty(column.Name))
            {
                return false;
            }

            return string.Equals(column.Name, "LevelMarker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(column.Name, "Icon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(column.Name, "IconCode", StringComparison.OrdinalIgnoreCase);
        }

        private int GetCompactFixedColumnWidth(DataGridViewColumn column)
        {
            if (column == null || string.IsNullOrEmpty(column.Name))
            {
                return 1;
            }

            if (string.Equals(column.Name, "LevelMarker", StringComparison.OrdinalIgnoreCase)) return 76;
            if (string.Equals(column.Name, "Icon", StringComparison.OrdinalIgnoreCase)) return 52;
            if (string.Equals(column.Name, "IconCode", StringComparison.OrdinalIgnoreCase)) return 106;
            return Math.Max(1, column.MinimumWidth);
        }

        private int GetGridColumnBaseWidth(DataGridViewColumn column)
        {
            if (column == null)
            {
                return 1;
            }

            if (IsCompactFixedColumn(column))
            {
                return GetCompactFixedColumnWidth(column);
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
                int expandableBaseWidth = 0;
                int visibleCount = 0;
                int i;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    if (column == null || !column.Visible)
                    {
                        continue;
                    }

                    int baseWidth = GetGridColumnBaseWidth(column);
                    totalBaseWidth += baseWidth;
                    if (!IsCompactFixedColumn(column))
                    {
                        expandableBaseWidth += baseWidth;
                    }
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
                DataGridViewColumn lastExpandableColumn = null;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    DataGridViewColumn column = grid.Columns[i];
                    if (column == null || !column.Visible)
                    {
                        continue;
                    }

                    int baseWidth = GetGridColumnBaseWidth(column);
                    int newWidth = baseWidth;

                    if (IsCompactFixedColumn(column))
                    {
                        column.Resizable = DataGridViewTriState.False;
                    }
                    else if (extraWidth > 0 && expandableBaseWidth > 0)
                    {
                        int addWidth = (int)Math.Floor((double)extraWidth * (double)baseWidth / (double)expandableBaseWidth);
                        newWidth += addWidth;
                        remainingExtra -= addWidth;
                        lastExpandableColumn = column;
                    }

                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = Math.Max(column.MinimumWidth, newWidth);
                }

                if (extraWidth > 0 && remainingExtra > 0 && lastExpandableColumn != null)
                {
                    lastExpandableColumn.Width = lastExpandableColumn.Width + remainingExtra;
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

            if (IsCompactFixedColumn(e.Column))
            {
                try
                {
                    isApplyingGridLayout = true;
                    e.Column.Width = GetCompactFixedColumnWidth(e.Column);
                }
                finally
                {
                    isApplyingGridLayout = false;
                }
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
            if (isApplyingRowHeight || grid == null || e == null || e.Row == null)
            {
                return;
            }

            try
            {
                isApplyingRowHeight = true;
                e.Row.Height = grid.RowTemplate.Height;
                e.Row.Resizable = DataGridViewTriState.False;
            }
            finally
            {
                isApplyingRowHeight = false;
            }

            RefreshGridScrollbarsAfterDimensionChange(true);
        }

        private void Grid_RowsAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
            if (grid != null && e != null)
            {
                int end = Math.Min(grid.Rows.Count, e.RowIndex + e.RowCount);
                int i;
                for (i = Math.Max(0, e.RowIndex); i < end; i++)
                {
                    grid.Rows[i].Height = grid.RowTemplate.Height;
                    grid.Rows[i].Resizable = DataGridViewTriState.False;
                }
            }

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

        private int GetMenuGridPreferredHeight(int minimumHeight)
        {
            return Math.Max(1, minimumHeight);
        }

        public bool CanLeaveWorkspaceScreen()
        {
            if (!isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 메뉴관리 변경사항이 있습니다.\r\n\r\n이동하시겠습니까?",
                "OVIA 메뉴관리",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            return result == DialogResult.OK;
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }

        public bool HasUnsavedWorkspaceData()
        {
            return isDirty;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "메뉴관리";
        }

        private void FrmMenuManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!CanLeaveWorkspaceScreen())
            {
                e.Cancel = true;
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
    }

    public class OviaMenuSetting
    {
        public string Key = string.Empty;
        public string MenuName = string.Empty;
        public int Level = 1;
        public bool Enabled = true;
        public int PermissionLevel = 9;
        public string IconCode = string.Empty;
        public bool ErpLoad = false;
        public string ErpModuleName = string.Empty;
        public string ModulePath = string.Empty;
    }

    internal static class OviaMenuSettingsStore
    {
        private const string FileName = "menu_settings.dat";

        public static List<OviaMenuSetting> Load()
        {
            List<OviaMenuSetting> defaults = CreateDefaultSettings();
            string path = GetFilePath();

            if (!File.Exists(path))
            {
                return defaults;
            }

            Dictionary<string, OviaMenuSetting> map = new Dictionary<string, OviaMenuSetting>(StringComparer.OrdinalIgnoreCase);
            int i;
            for (i = 0; i < defaults.Count; i++)
            {
                map[defaults[i].Key] = defaults[i];
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] parts = line.Split(new char[] { '\t' });
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    string key = Decode(parts[0]);
                    if (IsObsoleteMenuKey(key))
                    {
                        continue;
                    }

                    OviaMenuSetting setting;
                    if (!map.TryGetValue(key, out setting))
                    {
                        setting = new OviaMenuSetting();
                        setting.Key = key;
                        defaults.Add(setting);
                        map[key] = setting;
                    }

                    setting.MenuName = Decode(parts[1]);
                    int level;
                    if (int.TryParse(parts[2], out level))
                    {
                        setting.Level = Math.Max(1, level);
                    }
                    setting.Enabled = parts[3] == "1";

                    bool legacySuperAdminOnly = parts[4] == "1";
                    bool isNewLevelFormat = IsNewPermissionLevelFormat(lines);
                    bool isIconCodeFormat = IsIconCodeFormat(lines);
                    bool isErpModuleFormat = IsErpModuleFormat(lines);
                    if (parts.Length >= 9 && isErpModuleFormat)
                    {
                        setting.PermissionLevel = isNewLevelFormat ? NormalizePermissionLevel(parts[5]) : (legacySuperAdminOnly ? 10 : 1);
                        setting.IconCode = NormalizeIconCode(parts[6]);
                        setting.ErpLoad = parts[7] == "1";
                        setting.ErpModuleName = OviaSystemSettingsStore.NormalizeErpModuleName(Decode(parts[8]));
                    }
                    else if (parts.Length >= 7 && isIconCodeFormat)
                    {
                        setting.PermissionLevel = isNewLevelFormat ? NormalizePermissionLevel(parts[5]) : (legacySuperAdminOnly ? 10 : 1);
                        setting.IconCode = NormalizeIconCode(parts[6]);
                    }
                    else if (parts.Length >= 6)
                    {
                        setting.PermissionLevel = isNewLevelFormat ? NormalizePermissionLevel(parts[5]) : (legacySuperAdminOnly ? 10 : 1);
                    }
                    else
                    {
                        setting.PermissionLevel = legacySuperAdminOnly ? 10 : 1;
                    }
                }
            }
            catch
            {
                return defaults;
            }

            ApplyMenuNameMigrations(defaults);
            ApplyErpShortcutRule(defaults);
            ApplyModulePaths(defaults);
            return defaults;
        }

        private static void ApplyMenuNameMigrations(List<OviaMenuSetting> settings)
        {
            if (settings == null)
            {
                return;
            }

            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row == null)
                {
                    continue;
                }

                if (string.Equals(row.Key, "SETTINGS", StringComparison.OrdinalIgnoreCase)
                    && string.Equals((row.MenuName == null ? string.Empty : row.MenuName.Trim()), "시스템관리", StringComparison.Ordinal))
                {
                    row.MenuName = "환경설정";
                }
            }
        }

        private static void ApplyErpShortcutRule(List<OviaMenuSetting> settings)
        {
            if (settings == null)
            {
                return;
            }

            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row != null && IsBrowserOnlyErpShortcut(row.Key))
                {
                    row.ErpLoad = false;
                    row.ErpModuleName = string.Empty;
                }
            }
        }

        private static bool IsNewPermissionLevelFormat(string[] lines)
        {
            if (lines == null)
            {
                return false;
            }

            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? string.Empty : lines[i];
                if (line.IndexOf("level 1-10", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIconCodeFormat(string[] lines)
        {
            if (lines == null)
            {
                return false;
            }

            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? string.Empty : lines[i];
                if (line.IndexOf("icon code", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsErpModuleFormat(string[] lines)
        {
            if (lines == null)
            {
                return false;
            }

            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? string.Empty : lines[i];
                if (line.IndexOf("ERP load/module", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyModulePaths(List<OviaMenuSetting> settings)
        {
            if (settings == null)
            {
                return;
            }

            int i;
            for (i = 0; i < settings.Count; i++)
            {
                if (settings[i] != null)
                {
                    settings[i].ModulePath = GetModulePath(settings[i].Key);
                }
            }
        }

        private static bool IsObsoleteMenuKey(string key)
        {
            return string.Equals(key, "ERP_SHORTCUT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "ERP_SYNC_STATUS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "IMPORT_TEMPLATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "PRINT_TEMPLATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "QR_BARCODE_TEMPLATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "PRINTER_SETTINGS", StringComparison.OrdinalIgnoreCase);
        }

        public static void Save(List<OviaMenuSetting> settings)
        {
            if (settings == null)
            {
                settings = CreateDefaultSettings();
            }

            ApplyErpShortcutRule(settings);

            string path = GetFilePath();
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# OVIA menu settings v5 - permission level 1-10, ERP meber_level compatible, Segoe Fluent icon code, ERP load/module");
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row == null || IsObsoleteMenuKey(row.Key))
                {
                    continue;
                }

                sb.Append(Encode(row.Key));
                sb.Append('\t');
                sb.Append(Encode(row.MenuName));
                sb.Append('\t');
                sb.Append(row.Level.ToString());
                sb.Append('\t');
                sb.Append(row.Enabled ? "1" : "0");
                sb.Append('\t');
                sb.Append("0");
                sb.Append('\t');
                sb.Append(NormalizePermissionLevel(row.PermissionLevel).ToString());
                sb.Append('\t');
                sb.Append(Encode(NormalizeIconCode(row.IconCode)));
                sb.Append('\t');
                bool saveErpLoad = !IsBrowserOnlyErpShortcut(row.Key) && row.ErpLoad;
                string saveErpModuleName = IsBrowserOnlyErpShortcut(row.Key) ? string.Empty : OviaSystemSettingsStore.NormalizeErpModuleName(row.ErpModuleName);
                sb.Append(saveErpLoad ? "1" : "0");
                sb.Append('\t');
                sb.Append(Encode(saveErpModuleName));
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static bool CanAccess(string key, string companyId, string userId)
        {
            if (OviaSystemSettingsStore.IsSystemAdministrator(companyId, userId))
            {
                return true;
            }

            OviaMenuSetting setting = FindSetting(key);
            if (setting == null)
            {
                return true;
            }

            if (!setting.Enabled)
            {
                return false;
            }

            int userLevel = GetCurrentUserPermissionLevel(companyId, userId);
            int requiredLevel = NormalizePermissionLevel(setting.PermissionLevel);
            return userLevel >= requiredLevel;
        }

        public static bool IsEnabled(string key)
        {
            OviaMenuSetting setting = FindSetting(key);
            return setting == null || setting.Enabled;
        }

        public static int GetCurrentUserPermissionLevel(string companyId, string userId)
        {
            return OviaSessionSecurity.GetCurrentUserLevel(companyId, userId);
        }

        public static int NormalizePermissionLevel(string value)
        {
            int parsed;
            if (!int.TryParse(value == null ? string.Empty : value.Trim(), out parsed))
            {
                parsed = 1;
            }

            return NormalizePermissionLevel(parsed);
        }

        public static int NormalizePermissionLevel(int value)
        {
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }

        private static OviaMenuSetting FindSetting(string key)
        {
            string normalized = key == null ? string.Empty : key.Trim();
            if (normalized == string.Empty)
            {
                return null;
            }

            List<OviaMenuSetting> settings = Load();
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row != null && string.Equals(row.Key, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return null;
        }

        public static bool IsBrowserOnlyErpShortcut(string key)
        {
            return string.Equals(key == null ? string.Empty : key.Trim(), "ERP", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsErpLoadEnabled(string key)
        {
            if (IsBrowserOnlyErpShortcut(key))
            {
                return false;
            }

            OviaMenuSetting setting = FindSetting(key);
            return setting != null && setting.ErpLoad && OviaSystemSettingsStore.NormalizeErpModuleName(setting.ErpModuleName) != "";
        }

        public static string GetErpModuleName(string key)
        {
            OviaMenuSetting setting = FindSetting(key);
            return setting == null ? string.Empty : OviaSystemSettingsStore.NormalizeErpModuleName(setting.ErpModuleName);
        }

        public static string GetMenuName(string key, string fallbackName)
        {
            string normalized = key == null ? string.Empty : key.Trim();
            List<OviaMenuSetting> settings = Load();
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row != null && string.Equals(row.Key, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(row.MenuName) ? fallbackName : row.MenuName;
                }
            }

            return fallbackName == null ? string.Empty : fallbackName;
        }

        public static string GetWorkspacePath(string key, string fallbackPath)
        {
            string normalized = key == null ? string.Empty : key.Trim().ToUpperInvariant();
            if (normalized == string.Empty)
            {
                return fallbackPath == null ? string.Empty : fallbackPath;
            }

            string mainName = GetMenuName("MAIN", "메인");
            string menuName = GetMenuName(normalized, GetDefaultMenuName(normalized));
            if (string.IsNullOrWhiteSpace(menuName))
            {
                menuName = normalized;
            }

            if (normalized == "MAIN")
            {
                return mainName;
            }

            if (normalized == "PROJECT_BARLIST_LIST")
            {
                return JoinPath(mainName, GetMenuName("PROJECT_MANAGER", "공사관리"), menuName);
            }

            if (normalized == "BARLIST")
            {
                return JoinPath(mainName, GetMenuName("PROJECT_MANAGER", "공사관리"), GetMenuName("PROJECT_BARLIST_LIST", "공사별 BarList"), menuName);
            }

            string parentKey = GetParentMenuKeyForPath(normalized);
            if (parentKey == string.Empty)
            {
                return fallbackPath == null ? JoinPath(mainName, menuName) : fallbackPath;
            }

            if (parentKey == normalized)
            {
                return JoinPath(mainName, menuName);
            }

            return JoinPath(mainName, GetMenuName(parentKey, GetDefaultMenuName(parentKey)), menuName);
        }

        private static string JoinPath(params string[] parts)
        {
            List<string> normalizedParts = new List<string>();
            int i;
            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? string.Empty : parts[i].Trim();
                if (part != string.Empty)
                {
                    normalizedParts.Add(part);
                }
            }

            return string.Join("  ›  ", normalizedParts.ToArray());
        }

        private static string GetParentMenuKeyForPath(string normalizedKey)
        {
            switch (normalizedKey)
            {
                case "PROJECT_MANAGER":
                case "OPERATIONS":
                case "MATERIAL_STOCK":
                case "SHIPPING_INVOICE":
                case "ERP":
                case "MASTER_DATA":
                case "SETTINGS":
                    return normalizedKey;
                case "PROJECT_REGISTER":
                    return "PROJECT_MANAGER";
                case "OPERATIONS_ALL_BARLIST":
                case "OPERATIONS_ALL_ORDER":
                case "OPERATIONS_INOUT":
                case "OPERATIONS_STOCK":
                case "OPERATIONS_INVOICE":
                case "OPERATIONS_TAG_QR":
                case "OPERATIONS_PENDING":
                case "OPERATIONS_PRINT_CENTER":
                    return "OPERATIONS";
                case "MATERIAL_INBOUND":
                case "MATERIAL_STOCK_STATUS":
                case "MATERIAL_STOCK_ADJUST":
                case "MATERIAL_OUTBOUND_USAGE":
                    return "MATERIAL_STOCK";
                case "SHIPPING_INVOICE_MANAGE":
                case "SHIPPING_RESULT_REGISTER":
                    return "SHIPPING_INVOICE";
                case "MASTER_COMPANY":
                case "MASTER_REBAR_MAKER":
                case "MASTER_MATERIAL_SPEC":
                case "MASTER_SHAPE_CODE":
                case "MASTER_CAR_DRIVER":
                case "MASTER_WORKER_TEAM":
                case "MASTER_MACHINE_LOCATION":
                    return "MASTER_DATA";
                case "LEGACY_MAIN_DASHBOARD":
                case "SYSTEM_SETTINGS":
                case "BARLIST_MAPPING":
                case "REBAR_UNIT_WEIGHT":
                case "BACKUP_RESTORE":
                case "MENU_MANAGER":
                case "VERSION_INFO":
                    return "SETTINGS";
                default:
                    return string.Empty;
            }
        }

        public static string GetSelectedMenuKey(string key)
        {
            string normalized = key == null ? string.Empty : key.Trim().ToUpperInvariant();
            string parentKey = GetParentMenuKeyForPath(normalized);
            string useKey = parentKey == string.Empty ? normalized : parentKey;

            switch (useKey)
            {
                case "PROJECT_MANAGER": return "PROJECT";
                case "OPERATIONS": return "OPERATIONS";
                case "MATERIAL_STOCK": return "MATERIAL";
                case "SHIPPING_INVOICE": return "SHIPPING";
                case "ERP": return "ERP";
                case "MASTER_DATA": return "MASTER";
                case "SETTINGS": return "SETTINGS";
                case "MAIN": return "MAIN";
                default: return string.Empty;
            }
        }

        private static string GetDefaultMenuName(string normalizedKey)
        {
            List<OviaMenuSetting> defaults = CreateDefaultSettings();
            int i;
            for (i = 0; i < defaults.Count; i++)
            {
                OviaMenuSetting row = defaults[i];
                if (row != null && string.Equals(row.Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return row.MenuName == null ? string.Empty : row.MenuName;
                }
            }

            return normalizedKey;
        }

        public static string GetIconCode(string key, string fallbackCode)
        {
            string normalized = key == null ? string.Empty : key.Trim();
            List<OviaMenuSetting> settings = Load();
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row != null && string.Equals(row.Key, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeIconCode(row.IconCode);
                }
            }

            return NormalizeIconCode(fallbackCode);
        }

        public static string GetIconGlyph(string key, string fallbackGlyph)
        {
            string fallbackCode = GlyphToIconCode(fallbackGlyph);
            return GetIconGlyphFromCode(GetIconCode(key, fallbackCode), fallbackGlyph);
        }

        public static string GetIconGlyphFromCode(string iconCode, string fallbackGlyph)
        {
            string normalizedCode = NormalizeIconCode(iconCode);
            if (normalizedCode == string.Empty)
            {
                return fallbackGlyph == null ? string.Empty : fallbackGlyph;
            }

            int value;
            if (int.TryParse(normalizedCode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                try
                {
                    return char.ConvertFromUtf32(value);
                }
                catch
                {
                }
            }

            return fallbackGlyph == null ? string.Empty : fallbackGlyph;
        }

        public static string NormalizeIconCode(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            if (text.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            if (text.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            if (text.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && text.EndsWith(";", StringComparison.Ordinal))
            {
                text = text.Substring(3, text.Length - 4);
            }

            StringBuilder hex = new StringBuilder();
            int i;
            for (i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    hex.Append(char.ToUpperInvariant(c));
                }
            }

            if (hex.Length < 4 || hex.Length > 6)
            {
                return string.Empty;
            }

            int parsed;
            if (!int.TryParse(hex.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
            {
                return string.Empty;
            }

            return hex.ToString();
        }

        private static string GlyphToIconCode(string glyph)
        {
            if (string.IsNullOrEmpty(glyph))
            {
                return string.Empty;
            }

            try
            {
                int value = char.ConvertToUtf32(glyph, 0);
                return value.ToString("X4", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetModulePath(string key)
        {
            string normalized = key == null ? string.Empty : key.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "MAIN":
                    return "OVIA.Desktop/FrmMain.cs";
                case "NOTIFICATIONS":
                    return "OVIA.Desktop/FrmNotificationList.cs";
                case "PROJECT_MANAGER":
                    return "OVIA.Desktop/FrmProjectManager.cs";
                case "PROJECT_REGISTER":
                    return "OVIA.Desktop/FrmOviaWebErpPage.cs";
                case "PROJECT_BARLIST_LIST":
                    return "OVIA.Desktop/FrmProjectBarListList.cs";
                case "BARLIST":
                    return "OVIA.Desktop/FrmBarList.cs";
                case "ERP":
                    return "OVIA.Desktop/FrmWorkspaceShell.cs";
                case "LEGACY_MAIN_DASHBOARD":
                    return "OVIA.Desktop/FrmMain.cs";
                case "SYSTEM_SETTINGS":
                    return "OVIA.Desktop/FrmSystemSettings.cs";
                case "BARLIST_MAPPING":
                    return "OVIA.Desktop/FrmBarListMappingManager.cs";
                case "REBAR_UNIT_WEIGHT":
                    return "OVIA.Desktop/FrmRebarUnitWeightTable.cs";
                case "MENU_MANAGER":
                    return "OVIA.Desktop/FrmMenuManager.cs";
                case "VERSION_INFO":
                    return "OVIA.Desktop/FrmWorkspaceShell.cs";
                case "SETTINGS":
                    return "OVIA.Desktop/FrmWorkspaceShell.cs";
                default:
                    return "OVIA.Desktop/FrmOviaMenuPage.cs";
            }
        }

        public static List<OviaMenuSetting> CreateDefaultSettings()
        {
            List<OviaMenuSetting> list = new List<OviaMenuSetting>();
            Add(list, "MAIN", "메인", 1, false);
            Add(list, "NOTIFICATIONS", "알림", 2, false);
            Add(list, "PROJECT_MANAGER", "공사관리", 1, false);
            Add(list, "PROJECT_REGISTER", "공사등록", 2, false);
            Add(list, "PROJECT_BARLIST_LIST", "공사별 BarList", 3, false);
            Add(list, "BARLIST", "BarList", 3, false);

            Add(list, "OPERATIONS", "운영현황", 1, false);
            Add(list, "OPERATIONS_ALL_BARLIST", "전체 BarList", 2, false);
            Add(list, "OPERATIONS_ALL_ORDER", "전체 생산오더", 2, false);
            Add(list, "OPERATIONS_INOUT", "입출고 현황", 2, false);
            Add(list, "OPERATIONS_STOCK", "재고 현황", 2, false);
            Add(list, "OPERATIONS_INVOICE", "송장/납품 현황", 2, false);
            Add(list, "OPERATIONS_TAG_QR", "태그/QR 현황", 2, false);
            Add(list, "OPERATIONS_PENDING", "미처리 작업", 2, false);
            Add(list, "OPERATIONS_PRINT_CENTER", "출력센터", 2, false);

            Add(list, "MATERIAL_STOCK", "자재/재고", 1, false);
            Add(list, "MATERIAL_INBOUND", "입고관리", 2, false);
            Add(list, "MATERIAL_STOCK_STATUS", "재고현황", 2, false);
            Add(list, "MATERIAL_STOCK_ADJUST", "재고조정", 2, false);
            Add(list, "MATERIAL_OUTBOUND_USAGE", "출고사용내역", 2, false);

            Add(list, "SHIPPING_INVOICE", "출하/송장", 1, false);
            Add(list, "SHIPPING_INVOICE_MANAGE", "송장관리", 2, false);
            Add(list, "SHIPPING_RESULT_REGISTER", "출하실적등록", 2, false);

            Add(list, "ERP", "ERP", 1, false);

            Add(list, "MASTER_DATA", "기준정보", 1, false);
            Add(list, "MASTER_COMPANY", "거래처 관리", 2, false);
            Add(list, "MASTER_REBAR_MAKER", "철근메이커 관리", 2, false);
            Add(list, "MASTER_MATERIAL_SPEC", "자재/규격 관리", 2, false);
            Add(list, "MASTER_SHAPE_CODE", "형상코드 관리", 2, false);
            Add(list, "MASTER_CAR_DRIVER", "차량/운전자 관리", 2, false);
            Add(list, "MASTER_WORKER_TEAM", "작업자/작업반 관리", 2, false);
            Add(list, "MASTER_MACHINE_LOCATION", "기계/위치 관리", 2, false);

            Add(list, "SETTINGS", "환경설정", 1, true);
            Add(list, "LEGACY_MAIN_DASHBOARD", "기존 메인대시보드", 2, true);
            Add(list, "SYSTEM_SETTINGS", "시스템 설정", 2, true);
            Add(list, "BARLIST_MAPPING", "BarList 항목 매핑", 2, true);
            Add(list, "REBAR_UNIT_WEIGHT", "이형철근 단위중량표", 2, true);
            Add(list, "BACKUP_RESTORE", "백업/복원", 2, true);
            Add(list, "MENU_MANAGER", "메뉴관리", 2, true);
            Add(list, "VERSION_INFO", "버전정보", 2, true);
            return list;
        }

        private static string GetDefaultIconCode(string key)
        {
            string normalized = key == null ? string.Empty : key.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "MAIN": return "E80F";
                case "NOTIFICATIONS": return "E7F4";
                case "PROJECT_MANAGER": return "E74C";
                case "PROJECT_REGISTER": return "E710";
                case "PROJECT_BARLIST_LIST": return "E8A5";
                case "BARLIST": return "E8A5";
                case "OPERATIONS": return "E9D2";
                case "OPERATIONS_ALL_BARLIST": return "E8A5";
                case "OPERATIONS_ALL_ORDER": return "E8F1";
                case "OPERATIONS_INOUT": return "E8CB";
                case "OPERATIONS_STOCK": return "E8D5";
                case "OPERATIONS_INVOICE": return "E7C3";
                case "OPERATIONS_TAG_QR": return "E8B3";
                case "OPERATIONS_PENDING": return "E7BA";
                case "OPERATIONS_PRINT_CENTER": return "E749";
                case "MATERIAL_STOCK": return "E7BC";
                case "MATERIAL_INBOUND": return "E8CB";
                case "MATERIAL_STOCK_STATUS": return "E8D5";
                case "MATERIAL_STOCK_ADJUST": return "E70F";
                case "MATERIAL_OUTBOUND_USAGE": return "E7C3";
                case "SHIPPING_INVOICE": return "E7C3";
                case "SHIPPING_INVOICE_MANAGE": return "E7C3";
                case "SHIPPING_RESULT_REGISTER": return "E9D9";
                case "ERP": return "E774";
                case "MASTER_DATA": return "E8EC";
                case "MASTER_COMPANY": return "E77B";
                case "MASTER_REBAR_MAKER": return "E8EC";
                case "MASTER_MATERIAL_SPEC": return "E8D5";
                case "MASTER_SHAPE_CODE": return "E8A5";
                case "MASTER_CAR_DRIVER": return "E804";
                case "MASTER_WORKER_TEAM": return "E716";
                case "MASTER_MACHINE_LOCATION": return "E950";
                case "SETTINGS": return "E713";
                case "LEGACY_MAIN_DASHBOARD": return "E9D2";
                case "SYSTEM_SETTINGS": return "E713";
                case "BARLIST_MAPPING": return "E8A5";
                case "REBAR_UNIT_WEIGHT": return "E9D9";
                case "BACKUP_RESTORE": return "E74E";
                case "MENU_MANAGER": return "E8A4";
                case "VERSION_INFO": return "E946";
                default: return string.Empty;
            }
        }

        private static void Add(List<OviaMenuSetting> list, string key, string name, int level, bool adminOnly)
        {
            OviaMenuSetting setting = new OviaMenuSetting();
            setting.Key = key;
            setting.MenuName = name;
            setting.Level = level;
            setting.Enabled = true;
            setting.PermissionLevel = adminOnly ? 10 : 1;
            setting.IconCode = GetDefaultIconCode(key);
            setting.ModulePath = GetModulePath(key);
            list.Add(setting);
        }

        private static string GetFilePath()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OVIA");
            return Path.Combine(baseDir, FileName);
        }

        private static string Encode(string value)
        {
            string text = value == null ? string.Empty : value;
            return text.Replace("%", "%25").Replace("\t", "%09").Replace("\r", "%0D").Replace("\n", "%0A");
        }

        private static string Decode(string value)
        {
            string text = value == null ? string.Empty : value;
            return text.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%09", "\t").Replace("%25", "%");
        }
    }

}
