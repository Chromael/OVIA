using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmVersionInfo : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        private const string ColumnNo = "colNo";
        private const string ColumnBuildVersion = "colBuildVersion";
        private const string ColumnFeatureVersion = "colFeatureVersion";
        private const string ColumnPatchVersion = "colPatchVersion";
        private const string ColumnWorkDate = "colWorkDate";
        private const string ColumnContent = "colContent";
        private const string ColumnAction = "colAction";

        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;
        private readonly Color GridSelectionBlue = Color.FromArgb(37, 99, 235);

        private Label lblCurrentVersion;
        private Label lblVersionGuide;
        private Label lblVersionPath;
        private DataGridView grid;
        private Button btnAdd;
        private Button btnExport;
        private Button btnSave;
        private string cleanSnapshot = string.Empty;
        private bool isDirty;
        private bool isLoading;
        private bool isCommittingEdit;

        public FrmVersionInfo(string companyId, string userId)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.canEdit = OviaSystemSettingsStore.IsSystemAdministrator(this.companyId, this.userId);

            BuildUI();
            LoadRowsToGrid();
        }

        private void BuildUI()
        {
            SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            Controls.Clear();

            Text = "OVIA 버전정보";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1060, 650);
            BackColor = SurfaceColor;
            FormClosing += FrmVersionInfo_FormClosing;
            Resize += delegate { ApplyWorkspaceLayout(); };

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildTopArea(this);
            BuildGrid(this);

            ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인  ›  환경설정  ›  버전정보",
                delegate { NavigateBack(); },
                delegate { NavigateUp(); },
                delegate { if (ConfirmDiscardUnsavedChanges()) LoadRowsToGrid(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target) { NavigateByWorkspacePath(target); }
            );
        }

        private void NavigateBack()
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateBackInWorkspace();
            }
            else
            {
                Close();
            }
        }

        private void NavigateUp()
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateUpInWorkspace();
            }
            else
            {
                Close();
            }
        }

        private void NavigateByWorkspacePath(string target)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace == null)
            {
                Close();
                return;
            }

            if (target == "MAIN")
            {
                workspace.NavigateToMain();
            }
            else if (target == "SETTINGS")
            {
                workspace.NavigateToWorkspaceInfoPage(
                    "SETTINGS",
                    "메인  ›  환경설정",
                    "환경설정",
                    "SETTINGS",
                    "환경설정 화면입니다.",
                    "시스템 설정, 버전정보 등 OVIA 설치·운영 기준을 관리합니다."
                );
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

        private void BuildTopArea(Control parent)
        {
            lblCurrentVersion = new Label();
            lblCurrentVersion.AutoSize = false;
            lblCurrentVersion.Location = new Point(32, 122);
            lblCurrentVersion.Size = new Size(520, 40);
            lblCurrentVersion.Font = OviaFluentTheme.FontTitle(16F, FontStyle.Bold);
            lblCurrentVersion.ForeColor = TextDark;
            lblCurrentVersion.BackColor = SurfaceColor;
            lblCurrentVersion.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(lblCurrentVersion);

            lblVersionGuide = new Label();
            lblVersionGuide.AutoSize = false;
            lblVersionGuide.Location = new Point(0, 122);
            lblVersionGuide.Size = new Size(520, 40);
            lblVersionGuide.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            lblVersionGuide.ForeColor = Color.FromArgb(148, 163, 184);
            lblVersionGuide.BackColor = SurfaceColor;
            lblVersionGuide.TextAlign = ContentAlignment.MiddleLeft;
            lblVersionGuide.AutoEllipsis = true;
            lblVersionGuide.Text = "버전 번호는 변경 범위에 맞춰 관리합니다. 첫 자리는 대규모 개편, 두 번째 자리는 기능 추가, 세 번째 자리는 오류 수정·세부 보정을 의미합니다.";
            parent.Controls.Add(lblVersionGuide);

            lblVersionPath = new Label();
            lblVersionPath.AutoSize = false;
            lblVersionPath.Location = new Point(0, 146);
            lblVersionPath.Size = new Size(520, 22);
            lblVersionPath.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblVersionPath.ForeColor = Color.FromArgb(156, 163, 175);
            lblVersionPath.BackColor = SurfaceColor;
            lblVersionPath.TextAlign = ContentAlignment.MiddleLeft;
            lblVersionPath.AutoEllipsis = true;
            parent.Controls.Add(lblVersionPath);

            btnSave = CreateButton("저장하기", 0, 122, true);
            btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSave.Enabled = false;
            btnSave.Visible = canEdit;
            btnSave.Click += Save_Click;
            parent.Controls.Add(btnSave);

            btnExport = CreateButton("엑셀다운로드", 0, 122, false);
            btnExport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExport.Click += Export_Click;
            parent.Controls.Add(btnExport);

            btnAdd = CreateButton("항목 추가", 0, 122, false);
            btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAdd.Visible = canEdit;
            btnAdd.Enabled = canEdit;
            btnAdd.Click += Add_Click;
            parent.Controls.Add(btnAdd);
        }

        private Button CreateButton(string text, int x, int y, bool primary)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, primary ? OviaButtonRole.Primary : OviaButtonRole.Neutral);
            return button;
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(32, 178);
            grid.Size = new Size(1116, 430);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            OviaFluentTheme.ApplyDataGrid(grid);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = OviaFluentTheme.GridLine;
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;
            grid.AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersHeight = 40;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = 38;
            grid.RowTemplate.Resizable = DataGridViewTriState.False;
            grid.AllowUserToResizeRows = false;
            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(9F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.White;
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(250, 251, 253);
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowsDefaultCellStyle.SelectionBackColor = Color.White;
            grid.RowsDefaultCellStyle.SelectionForeColor = TextDark;
            grid.ReadOnly = !canEdit;
            grid.Leave += delegate { EndGridEditSilently(); };
            parent.MouseDown += VersionInfoParent_MouseDown;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellClick += Grid_CellClick;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellPainting += Grid_CellPainting;
            grid.EditingControlShowing += Grid_EditingControlShowing;
            grid.CellValidating += Grid_CellValidating;
            grid.DataError += Grid_DataError;
            parent.Controls.Add(grid);

            AddColumns();
            RefreshColumnHeaderSelection();
        }

        private void AddColumns()
        {
            grid.Columns.Clear();

            DataGridViewTextBoxColumn colNo = new DataGridViewTextBoxColumn();
            colNo.Name = ColumnNo;
            colNo.HeaderText = "순번";
            colNo.Width = 70;
            colNo.ReadOnly = true;
            colNo.SortMode = DataGridViewColumnSortMode.NotSortable;
            colNo.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colNo.DefaultCellStyle.BackColor = Color.White;
            colNo.DefaultCellStyle.SelectionBackColor = Color.White;
            colNo.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.Columns.Add(colNo);

            DataGridViewTextBoxColumn colBuildVersion = new DataGridViewTextBoxColumn();
            colBuildVersion.Name = ColumnBuildVersion;
            colBuildVersion.HeaderText = "빌드버전";
            colBuildVersion.Width = 86;
            colBuildVersion.ReadOnly = !canEdit;
            colBuildVersion.SortMode = DataGridViewColumnSortMode.NotSortable;
            colBuildVersion.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(colBuildVersion);

            DataGridViewTextBoxColumn colFeatureVersion = new DataGridViewTextBoxColumn();
            colFeatureVersion.Name = ColumnFeatureVersion;
            colFeatureVersion.HeaderText = "기능버전";
            colFeatureVersion.Width = 86;
            colFeatureVersion.ReadOnly = !canEdit;
            colFeatureVersion.SortMode = DataGridViewColumnSortMode.NotSortable;
            colFeatureVersion.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(colFeatureVersion);

            DataGridViewTextBoxColumn colPatchVersion = new DataGridViewTextBoxColumn();
            colPatchVersion.Name = ColumnPatchVersion;
            colPatchVersion.HeaderText = "수정버전";
            colPatchVersion.Width = 86;
            colPatchVersion.ReadOnly = !canEdit;
            colPatchVersion.SortMode = DataGridViewColumnSortMode.NotSortable;
            colPatchVersion.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(colPatchVersion);

            DataGridViewTextBoxColumn colDate = new DataGridViewTextBoxColumn();
            colDate.Name = ColumnWorkDate;
            colDate.HeaderText = "작업일";
            colDate.Width = 170;
            colDate.ReadOnly = !canEdit;
            colDate.SortMode = DataGridViewColumnSortMode.NotSortable;
            colDate.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(colDate);

            DataGridViewTextBoxColumn colContent = new DataGridViewTextBoxColumn();
            colContent.Name = ColumnContent;
            colContent.HeaderText = "업데이트 내용";
            colContent.Width = 620;
            colContent.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colContent.ReadOnly = !canEdit;
            colContent.SortMode = DataGridViewColumnSortMode.NotSortable;
            colContent.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.Columns.Add(colContent);

            DataGridViewButtonColumn colAction = new DataGridViewButtonColumn();
            colAction.Name = ColumnAction;
            colAction.HeaderText = "작업";
            colAction.Text = "삭제";
            colAction.UseColumnTextForButtonValue = true;
            colAction.Width = 82;
            colAction.ReadOnly = true;
            colAction.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(colAction);
        }

        private void LoadRowsToGrid()
        {
            isLoading = true;
            try
            {
                List<OviaVersionInfoEntry> entries = OviaVersionInfoStore.Load(OviaSystemSettingsStore.Load().VersionText);
                grid.Rows.Clear();
                int i;
                for (i = 0; i < entries.Count; i++)
                {
                    AddRow(entries[i]);
                }

                RenumberRows();
                cleanSnapshot = BuildSnapshot(CollectEntriesFromGrid());
                isDirty = false;
                UpdateCurrentVersionLabel();
                UpdateVersionPathLabel();
                UpdateSaveButtonVisibility();
            }
            finally
            {
                isLoading = false;
            }
        }

        private void AddRow(OviaVersionInfoEntry entry)
        {
            if (entry == null)
            {
                entry = new OviaVersionInfoEntry();
            }

            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Cells[ColumnNo].Value = entry.SequenceNo.ToString(CultureInfo.InvariantCulture);
            row.Cells[ColumnBuildVersion].Value = OviaVersionInfoStore.GetVersionPartText(entry.VersionText, 0);
            row.Cells[ColumnFeatureVersion].Value = OviaVersionInfoStore.GetVersionPartText(entry.VersionText, 1);
            row.Cells[ColumnPatchVersion].Value = OviaVersionInfoStore.GetVersionPartText(entry.VersionText, 2);
            row.Cells[ColumnWorkDate].Value = OviaVersionInfoStore.NormalizeWorkDateText(entry.WorkDateText);
            row.Cells[ColumnContent].Value = entry.UpdateContent == null ? string.Empty : entry.UpdateContent;
            row.Cells[ColumnAction].Value = "삭제";
        }

        private void Add_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            CommitGridEdit();
            List<OviaVersionInfoEntry> entries = CollectEntriesFromGrid();
            OviaVersionInfoEntry entry = OviaVersionInfoStore.CreateNewEntry(entries);
            AddRow(entry);
            RenumberRows();
            int rowIndex = grid.Rows.Count - 1;
            if (rowIndex >= 0)
            {
                grid.ClearSelection();
                grid.CurrentCell = grid.Rows[rowIndex].Cells[ColumnBuildVersion];
                grid.BeginEdit(true);
            }

            MarkDirtyFromCurrentState();
        }


        private void VersionInfoParent_MouseDown(object sender, MouseEventArgs e)
        {
            if (grid == null || !grid.IsCurrentCellInEditMode)
            {
                return;
            }

            Point screenPoint = ((Control)sender).PointToScreen(e.Location);
            Point gridPoint = grid.PointToClient(screenPoint);
            if (!grid.ClientRectangle.Contains(gridPoint))
            {
                CommitGridEdit();
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid == null)
            {
                return;
            }

            DataGridViewRow row = grid.Rows[e.RowIndex];
            bool isAlt = e.RowIndex % 2 == 1;
            Color rowBack = isAlt ? Color.FromArgb(250, 251, 253) : Color.White;
            e.CellStyle.BackColor = rowBack;
            e.CellStyle.ForeColor = TextDark;
            e.CellStyle.SelectionBackColor = rowBack;
            e.CellStyle.SelectionForeColor = TextDark;

            if (grid.Columns[e.ColumnIndex].Name == ColumnNo)
            {
                e.CellStyle.BackColor = Color.White;
                e.CellStyle.SelectionBackColor = Color.White;
                e.CellStyle.SelectionForeColor = TextDark;
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 && e.ColumnIndex >= 0)
            {
                bool isCurrentHeader = grid != null && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == e.ColumnIndex;
                Color backColor = isCurrentHeader ? GridSelectionBlue : OviaFluentTheme.HeaderBackground;
                Color foreColor = isCurrentHeader ? Color.White : TextDark;

                using (SolidBrush brush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(brush, e.CellBounds);
                }

                using (Pen pen = new Pen(OviaFluentTheme.GridLine, 1))
                {
                    e.Graphics.DrawRectangle(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    Convert.ToString(e.Value),
                    e.CellStyle.Font == null ? OviaFluentTheme.FontData(8.7F, FontStyle.Bold) : e.CellStyle.Font,
                    e.CellBounds,
                    foreColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                );
                e.Handled = true;
            }
        }



        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            TextBox textBox = e.Control as TextBox;
            if (textBox == null || grid == null || grid.CurrentCell == null)
            {
                return;
            }

            textBox.KeyPress -= VersionPart_KeyPress;
            textBox.KeyPress -= WorkDate_KeyPress;
            textBox.MaxLength = 32767;

            string columnName = grid.Columns[grid.CurrentCell.ColumnIndex].Name;
            if (IsVersionPartColumn(columnName))
            {
                textBox.MaxLength = 3;
                textBox.KeyPress += VersionPart_KeyPress;
            }
            else if (columnName == ColumnWorkDate)
            {
                textBox.MaxLength = 16;
                textBox.KeyPress += WorkDate_KeyPress;
            }
        }

        private void VersionPart_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
            {
                return;
            }

            if (!char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void WorkDate_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
            {
                return;
            }

            if (!(char.IsDigit(e.KeyChar) || e.KeyChar == '-' || e.KeyChar == ':' || e.KeyChar == ' '))
            {
                e.Handled = true;
            }
        }

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (!canEdit || grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            string value = Convert.ToString(e.FormattedValue) == null ? string.Empty : Convert.ToString(e.FormattedValue).Trim();

            if (IsVersionPartColumn(columnName) && !OviaVersionInfoStore.IsValidVersionPartText(value))
            {
                MessageBox.Show("버전 항목은 0~999 사이의 숫자만 입력할 수 있습니다.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            if (columnName == ColumnWorkDate && value != string.Empty && !OviaVersionInfoStore.IsValidWorkDateText(value))
            {
                MessageBox.Show("작업일은 2026-07-09 00:00 형식으로 입력해 주세요.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        }

        private bool IsVersionPartColumn(string columnName)
        {
            return columnName == ColumnBuildVersion || columnName == ColumnFeatureVersion || columnName == ColumnPatchVersion;
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            RefreshColumnHeaderSelection();
        }

        private void RefreshColumnHeaderSelection()
        {
            if (grid == null)
            {
                return;
            }

            int currentColumnIndex = -1;
            if (grid.CurrentCell != null)
            {
                currentColumnIndex = grid.CurrentCell.ColumnIndex;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                if (column == null)
                {
                    continue;
                }

                if (i == currentColumnIndex)
                {
                    column.HeaderCell.Style.BackColor = GridSelectionBlue;
                    column.HeaderCell.Style.ForeColor = Color.White;
                    column.HeaderCell.Style.SelectionBackColor = GridSelectionBlue;
                    column.HeaderCell.Style.SelectionForeColor = Color.White;
                }
                else
                {
                    column.HeaderCell.Style.BackColor = OviaFluentTheme.HeaderBackground;
                    column.HeaderCell.Style.ForeColor = TextDark;
                    column.HeaderCell.Style.SelectionBackColor = OviaFluentTheme.HeaderBackground;
                    column.HeaderCell.Style.SelectionForeColor = TextDark;
                }
            }

            grid.Invalidate();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !canEdit)
            {
                return;
            }

            if (grid.Columns[e.ColumnIndex].Name == ColumnAction)
            {
                DeleteRow(e.RowIndex);
            }
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !canEdit)
            {
                return;
            }

            string name = grid.Columns[e.ColumnIndex].Name;
            if (IsVersionPartColumn(name) || name == ColumnWorkDate || name == ColumnContent)
            {
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                grid.BeginEdit(true);
            }
        }

        private void DeleteRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "선택한 버전정보 항목을 삭제하시겠습니까?",
                "OVIA 버전정보",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            grid.Rows.RemoveAt(rowIndex);
            RenumberRows();
            MarkDirtyFromCurrentState();
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // TextBox 기반 셀 입력 중에는 강제 Commit/EndEdit을 호출하지 않는다.
            // 이전 구현은 입력 중 CellValueChanged → MarkDirty → EndEdit이 재진입되어
            // 업데이트 내용 입력 시 화면이 멈추거나 NullReferenceException이 발생했다.
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (!isLoading)
            {
                MarkDirtyFromCurrentState();
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                NormalizeRow(e.RowIndex);
            }

            if (!isLoading)
            {
                MarkDirtyFromCurrentState();
            }
        }

        private void NormalizeRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = grid.Rows[rowIndex];
            row.Cells[ColumnBuildVersion].Value = OviaVersionInfoStore.NormalizeVersionPartText(Convert.ToString(row.Cells[ColumnBuildVersion].Value));
            row.Cells[ColumnFeatureVersion].Value = OviaVersionInfoStore.NormalizeVersionPartText(Convert.ToString(row.Cells[ColumnFeatureVersion].Value));
            row.Cells[ColumnPatchVersion].Value = OviaVersionInfoStore.NormalizeVersionPartText(Convert.ToString(row.Cells[ColumnPatchVersion].Value));
            row.Cells[ColumnWorkDate].Value = OviaVersionInfoStore.NormalizeWorkDateText(Convert.ToString(row.Cells[ColumnWorkDate].Value));
            string content = Convert.ToString(row.Cells[ColumnContent].Value);
            row.Cells[ColumnContent].Value = content == null ? string.Empty : content.Trim();
        }

        private void RenumberRows()
        {
            int i;
            for (i = 0; i < grid.Rows.Count; i++)
            {
                grid.Rows[i].Cells[ColumnNo].Value = (i + 1).ToString(CultureInfo.InvariantCulture);
                grid.Rows[i].Cells[ColumnAction].Value = "삭제";
            }
        }

        private List<OviaVersionInfoEntry> CollectEntriesFromGrid()
        {
            List<OviaVersionInfoEntry> entries = new List<OviaVersionInfoEntry>();
            if (grid == null)
            {
                return entries;
            }

            int i;
            for (i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow row = grid.Rows[i];
                if (row.IsNewRow)
                {
                    continue;
                }

                OviaVersionInfoEntry entry = new OviaVersionInfoEntry();
                entry.SequenceNo = i + 1;
                entry.VersionText = OviaVersionInfoStore.BuildVersionText(
                    Convert.ToString(row.Cells[ColumnBuildVersion].Value),
                    Convert.ToString(row.Cells[ColumnFeatureVersion].Value),
                    Convert.ToString(row.Cells[ColumnPatchVersion].Value)
                );
                entry.WorkDateText = OviaVersionInfoStore.NormalizeWorkDateText(Convert.ToString(row.Cells[ColumnWorkDate].Value));
                entry.UpdateContent = Convert.ToString(row.Cells[ColumnContent].Value) == null ? string.Empty : Convert.ToString(row.Cells[ColumnContent].Value).Trim();
                if (entry.VersionText == string.Empty && entry.UpdateContent == string.Empty)
                {
                    continue;
                }

                entries.Add(entry);
            }

            OviaVersionInfoStore.NormalizeEntries(entries);
            return entries;
        }

        private string BuildSnapshot(List<OviaVersionInfoEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaVersionInfoEntry entry = entries[i];
                sb.Append(entry.SequenceNo.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.VersionText == null ? string.Empty : entry.VersionText).Append('\t')
                    .Append(entry.WorkDateText == null ? string.Empty : entry.WorkDateText).Append('\t')
                    .Append((entry.UpdateContent == null ? string.Empty : entry.UpdateContent).Replace("\r\n", "\n"))
                    .Append('\n');
            }

            return sb.ToString();
        }

        private void MarkDirtyFromCurrentState()
        {
            RenumberRows();
            string snapshot = BuildSnapshot(CollectEntriesFromGrid());
            isDirty = snapshot != cleanSnapshot;
            UpdateCurrentVersionLabel();
            UpdateSaveButtonVisibility();
        }

        private bool CommitGridEdit()
        {
            if (grid == null)
            {
                return true;
            }

            if (isCommittingEdit)
            {
                return true;
            }

            try
            {
                isCommittingEdit = true;
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }

                if (grid.IsCurrentCellInEditMode)
                {
                    return grid.EndEdit(DataGridViewDataErrorContexts.Commit);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                isCommittingEdit = false;
            }
        }

        private void EndGridEditSilently()
        {
            CommitGridEdit();
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                MessageBox.Show("버전정보는 최고관리자만 수정할 수 있습니다.", "OVIA 권한 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!CommitGridEdit())
            {
                MessageBox.Show("현재 편집 중인 셀의 입력값을 먼저 확인해 주세요.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RenumberRows();
            List<OviaVersionInfoEntry> entries = CollectEntriesFromGrid();
            if (entries.Count == 0)
            {
                MessageBox.Show("저장할 버전정보를 1개 이상 입력해 주세요.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int i;
            for (i = 0; i < entries.Count; i++)
            {
                if (!OviaVersionInfoStore.IsValidVersionText(entries[i].VersionText))
                {
                    MessageBox.Show(
                        "빌드버전, 기능버전, 수정버전은 각각 0~999 사이의 숫자만 입력해 주세요.",
                        "OVIA 버전정보",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                if (!OviaVersionInfoStore.IsValidWorkDateText(entries[i].WorkDateText))
                {
                    MessageBox.Show(
                        "작업일은 2026-07-09 00:00 형식으로 입력해 주세요.",
                        "OVIA 버전정보",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }
            }

            try
            {
                OviaVersionInfoStore.Save(entries);
                OviaVersionInfoEntry latest = OviaVersionInfoStore.GetLatestEntry(entries);
                OviaSystemSettings settings = OviaSystemSettingsStore.Load();
                settings.VersionText = latest == null ? string.Empty : latest.VersionText;
                OviaSystemSettingsStore.Save(settings);

                cleanSnapshot = BuildSnapshot(entries);
                isDirty = false;
                LoadRowsToGrid();
                MessageBox.Show("버전정보가 저장되었습니다.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "버전정보를 저장하는 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 버전정보",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            CommitGridEdit();
            List<OviaVersionInfoEntry> entries = CollectEntriesFromGrid();
            if (entries.Count == 0)
            {
                MessageBox.Show("다운로드할 버전정보가 없습니다.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "버전정보 엑셀다운로드";
                dialog.Filter = "Excel 파일 (*.xls)|*.xls|모든 파일 (*.*)|*.*";
                dialog.FileName = "OVIA_버전정보_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".xls";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    OviaVersionInfoStore.ExportToExcelHtml(dialog.FileName, entries);
                    MessageBox.Show("엑셀 파일로 저장되었습니다.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "엑셀 파일을 저장하는 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                        "OVIA 버전정보",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
        }

        private void UpdateCurrentVersionLabel()
        {
            if (lblCurrentVersion == null)
            {
                return;
            }

            OviaVersionInfoEntry latest = OviaVersionInfoStore.GetLatestEntry(CollectEntriesFromGrid());
            string version = latest == null ? OviaSystemSettingsStore.GetConfiguredVersionText() : latest.VersionText;
            lblCurrentVersion.Text = "현재 버전 : " + OviaVersionInfoStore.FormatDisplayVersion(version);
        }


        private void UpdateVersionPathLabel()
        {
            if (lblVersionPath == null)
            {
                return;
            }

            lblVersionPath.Text = "저장경로 : " + OviaVersionInfoStore.GetDisplayInstallVersionInfoFilePath();
        }

        private void UpdateSaveButtonVisibility()
        {
            if (btnSave != null)
            {
                btnSave.Visible = canEdit;
                btnSave.Enabled = canEdit && isDirty;
            }

            if (btnAdd != null)
            {
                btnAdd.Visible = canEdit;
                btnAdd.Enabled = canEdit;
            }
        }

        private void FrmVersionInfo_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!canEdit || !isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 버전정보 변경사항이 있습니다.\r\n\r\n저장하지 않고 이동하시겠습니까?",
                "OVIA 버전정보",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            return result == DialogResult.Yes;
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
            return canEdit && isDirty;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "버전정보";
        }

        public void ApplyWorkspaceLayout()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;

            if (lblCurrentVersion != null)
            {
                int measuredVersionWidth = TextRenderer.MeasureText(lblCurrentVersion.Text, lblCurrentVersion.Font).Width + 12;
                lblCurrentVersion.Width = Math.Max(300, Math.Min(360, measuredVersionWidth));
                lblCurrentVersion.Location = new Point(32, 122);
            }

            int right = Math.Max(32, width - 32);
            if (btnSave != null)
            {
                btnSave.Location = new Point(right - btnSave.Width, 122);
                right = btnSave.Left - 10;
            }

            if (btnExport != null)
            {
                btnExport.Location = new Point(right - btnExport.Width, 122);
                right = btnExport.Left - 10;
            }

            if (btnAdd != null)
            {
                btnAdd.Location = new Point(right - btnAdd.Width, 122);
            }

            if (lblVersionGuide != null && lblCurrentVersion != null)
            {
                int guideLeft = lblCurrentVersion.Right + 30;
                int guideRight = width - 32;
                if (btnAdd != null && btnAdd.Visible)
                {
                    guideRight = btnAdd.Left - 24;
                }
                else if (btnExport != null)
                {
                    guideRight = btnExport.Left - 24;
                }

                lblVersionGuide.Location = new Point(guideLeft, 118);
                lblVersionGuide.Size = new Size(Math.Max(160, guideRight - guideLeft), 22);

                if (lblVersionPath != null)
                {
                    lblVersionPath.Location = new Point(guideLeft, 142);
                    lblVersionPath.Size = new Size(Math.Max(160, guideRight - guideLeft), 22);
                    UpdateVersionPathLabel();
                }
            }

            if (grid != null)
            {
                grid.Location = new Point(32, 178);
                grid.Size = new Size(Math.Max(400, width - 64), Math.Max(260, height - grid.Top - 18));
                if (grid.Columns.Contains(ColumnContent))
                {
                    int fixedWidth = 70 + 86 + 86 + 86 + 170 + 82 + 20;
                    grid.Columns[ColumnContent].Width = Math.Max(360, grid.Width - fixedWidth);
                }
            }

        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.RequestLogout();
            }
        }
    }
}
