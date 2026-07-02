using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmRebarUnitWeightTable : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;
        private readonly Color HeaderBack = Color.White;
        private readonly Color HeaderBorder = OviaFluentTheme.CardBorder;

        private Panel tableHeaderPanel;
        private Label tableTitleLabel;
        private Label tableBasisLabel;
        private DataGridView grid;
        private Label lblStatus;
        private Button btnAdd;
        private Button btnDelete;
        private Button btnReset;
        private Button btnSave;
        private Button btnClose;
        private bool isDirty = false;
        private bool isLoading = false;
        private bool suppressDirtyEvent = false;
        private string cleanSignature = "";


        public string WorkspaceHelpKey { get { return "REBAR_UNIT_WEIGHT"; } }
        public string WorkspaceHelpTitle { get { return "이형철근 단위중량표"; } }
        public string WorkspaceHelpText
        {
            get
            {
                return "규격과 단위무게 기준으로 1톤 단위 조견표와 총길이/중량 계산 기준을 관리합니다. 최고관리자만 수정할 수 있습니다.";
            }
        }
        private static readonly double[] StandardLengths = new double[] { 6.0, 6.5, 7.0, 7.5, 8.0, 9.0, 10.0, 11.0, 12.0 };
        private static readonly string[] StandardLengthColumnNames = new string[] { "L6", "L6_5", "L7", "L7_5", "L8", "L9", "L10", "L11", "L12" };
        private static readonly string[] StandardLengthHeaders = new string[] { "6", "6.5", "7", "7.5", "8", "9", "10", "11", "12" };
        private const int RowsPerSpec = 4;
        private int selectedSpecStartRow = -1;
        private static readonly Dictionary<string, int[]> StandardBundleCounts = CreateStandardBundleCounts();

        public FrmRebarUnitWeightTable(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.canEdit = OviaRebarUnitWeightStore.IsSuperAdminUser(this.userId);

            BuildUI();
            LoadRowsToGrid(OviaRebarUnitWeightStore.LoadRows());
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA - 이형철근 단위중량표";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ClientSize = new Size(1280, 760);
            this.MinimumSize = new Size(1060, 640);
            this.BackColor = SurfaceColor;
            this.AutoValidate = AutoValidate.Disable;
            this.FormClosing += FrmRebarUnitWeightTable_FormClosing;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildGrid(this);
            BuildButtons(this);
            BuildStatus(this);

            this.ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인  ›  환경설정  ›  이형철근 단위중량표",
                delegate { this.Close(); },
                delegate { this.Close(); },
                delegate { LoadRowsToGrid(OviaRebarUnitWeightStore.LoadRows()); },
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
                            this.Close();
                        }
                    }
                }
            );
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1280, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "SETTINGS");
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

        private void BuildTableHeader(Control parent)
        {
            tableHeaderPanel = new Panel();
            tableHeaderPanel.Location = new Point(32, 124);
            tableHeaderPanel.Size = new Size(1216, 56);
            tableHeaderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tableHeaderPanel.BackColor = SurfaceColor;
            parent.Controls.Add(tableHeaderPanel);

            tableTitleLabel = new Label();
            tableTitleLabel.Text = "이형철근 단위중량표";
            tableTitleLabel.AutoSize = false;
            tableTitleLabel.Location = new Point(0, 0);
            tableTitleLabel.Size = new Size(420, 32);
            tableTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            tableTitleLabel.Font = OviaFluentTheme.FontTitle(16F, FontStyle.Bold);
            tableTitleLabel.ForeColor = TextDark;
            tableTitleLabel.BackColor = Color.Transparent;
            tableHeaderPanel.Controls.Add(tableTitleLabel);

            tableBasisLabel = new Label();
            tableBasisLabel.Text = "1톤 단위 조견표 · 환산중량 단중 : KS D 3504 기준";
            tableBasisLabel.AutoSize = false;
            tableBasisLabel.Location = new Point(650, 2);
            tableBasisLabel.Size = new Size(560, 30);
            tableBasisLabel.TextAlign = ContentAlignment.MiddleRight;
            tableBasisLabel.Font = OviaFluentTheme.FontStatus(9F, FontStyle.Regular);
            tableBasisLabel.ForeColor = TextSub;
            tableBasisLabel.BackColor = Color.Transparent;
            tableHeaderPanel.Controls.Add(tableBasisLabel);

            Label helper = new Label();
            helper.Text = "규격과 단위무게만 최고관리자가 수정할 수 있으며, 조견표 값은 단위무게 기준으로 자동 계산됩니다.";
            helper.AutoSize = false;
            helper.Location = new Point(0, 34);
            helper.Size = new Size(760, 22);
            helper.TextAlign = ContentAlignment.MiddleLeft;
            helper.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            helper.ForeColor = TextSub;
            helper.BackColor = Color.Transparent;
            tableHeaderPanel.Controls.Add(helper);
        }

        private void TableHeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            // BarList 항목 매핑 화면과 같은 업무형 제목 영역으로 사용한다.
            // 별도의 배경색/테두리는 그리지 않는다.
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(32, 124);
            grid.Size = new Size(1216, 492);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = true;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.GridColor = Color.FromArgb(229, 234, 240);
            grid.EditMode = canEdit ? DataGridViewEditMode.EditOnKeystrokeOrF2 : DataGridViewEditMode.EditProgrammatically;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ReadOnly = !canEdit;
            grid.ColumnHeadersHeight = 44;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Regular);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 32;
            grid.CellBeginEdit += Grid_CellBeginEdit;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellPainting += Grid_CellPainting;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellClick += Grid_CellClick;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.KeyDown += Grid_KeyDown;
            grid.EditingControlShowing += Grid_EditingControlShowing;
            grid.DataError += Grid_DataError;
            OviaFluentTheme.ApplyDataGrid(grid);
            parent.Controls.Add(grid);

            BuildGridColumns();
        }

        private void BuildGridColumns()
        {
            grid.Columns.Clear();

            DataGridViewTextBoxColumn specCol = CreateTextColumn("Spec", "규격", 70, 64);
            grid.Columns.Add(specCol);

            DataGridViewTextBoxColumn weightCol = CreateTextColumn("UnitWeightKgM", "단위무게\r\n(kg/m)", 95, 90);
            weightCol.DefaultCellStyle.Format = "0.000";
            grid.Columns.Add(weightCol);

            DataGridViewTextBoxColumn kindCol = CreateTextColumn("RowType", "길이(m)", 88, 80);
            kindCol.ReadOnly = true;
            grid.Columns.Add(kindCol);

            int i;
            for (i = 0; i < StandardLengthColumnNames.Length; i++)
            {
                DataGridViewTextBoxColumn lenCol = CreateTextColumn(StandardLengthColumnNames[i], StandardLengthHeaders[i], 80, 70);
                grid.Columns.Add(lenCol);
            }
        }

        private DataGridViewTextBoxColumn CreateTextColumn(string name, string headerText, float fillWeight, int minWidth)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.Name = name;
            col.HeaderText = headerText;
            col.FillWeight = fillWeight;
            col.MinimumWidth = minWidth;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return col;
        }

        private void BuildButtons(Control parent)
        {
            const int buttonTop = 642;
            const int buttonGap = 10;
            const int leftMargin = 32;
            const int rightMargin = 32;

            btnAdd = CreateButton("규격 추가", leftMargin, buttonTop);
            btnAdd.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnAdd.Enabled = canEdit;
            btnAdd.Click += Add_Click;
            parent.Controls.Add(btnAdd);

            btnDelete = CreateButton("선택 규격 삭제", btnAdd.Right + buttonGap, buttonTop);
            btnDelete.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnDelete.Enabled = canEdit;
            btnDelete.Click += Delete_Click;
            parent.Controls.Add(btnDelete);

            btnReset = CreateButton("기본값 복원", btnDelete.Right + buttonGap, buttonTop);
            btnReset.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnReset.Enabled = canEdit;
            btnReset.Click += Reset_Click;
            parent.Controls.Add(btnReset);

            btnClose = CreateButton("닫기", this.ClientSize.Width - rightMargin - OviaFluentTheme.MeasureButtonWidth("닫기"), buttonTop);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnClose.CausesValidation = false;
            btnClose.Click += delegate { this.Close(); };
            parent.Controls.Add(btnClose);

            btnSave = CreateButton("저장하기", btnClose.Left - buttonGap - OviaFluentTheme.MeasureButtonWidth("저장하기"), buttonTop);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnSave.Enabled = canEdit;
            btnSave.Click += Save_Click;
            parent.Controls.Add(btnSave);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(1216, 44);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(32, 696);
            parent.Controls.Add(lblStatus);
        }

        private Button CreateButton(string text, int x, int y)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, text);
            return button;
        }

        private void LoadRowsToGrid(List<RebarUnitWeightRow> rows)
        {
            isLoading = true;
            suppressDirtyEvent = true;
            grid.Rows.Clear();
            selectedSpecStartRow = -1;

            int i;
            for (i = 0; i < rows.Count; i++)
            {
                AddGroupedRows(rows[i]);
            }

            cleanSignature = BuildGridSignature();
            isDirty = false;
            isLoading = false;
            UpdateStatus(canEdit ? "이형철근 단위중량표를 불러왔습니다. 최고관리자는 규격/단위무게만 수정할 수 있으며 조견표 값은 자동 계산됩니다." : "이형철근 단위중량표 보기 전용입니다. 최고관리자만 수정할 수 있습니다.");
            grid.ClearSelection();
            grid.Invalidate();

            FinishInitialGridLoad();
        }

        private void FinishInitialGridLoad()
        {
            if (grid == null || grid.IsDisposed)
            {
                suppressDirtyEvent = false;
                return;
            }

            cleanSignature = BuildGridSignature();
            isDirty = false;
            suppressDirtyEvent = false;
            UpdateStatus(canEdit ? "이형철근 단위중량표를 불러왔습니다. 최고관리자는 규격/단위무게만 수정할 수 있으며 조견표 값은 자동 계산됩니다." : "이형철근 단위중량표 보기 전용입니다. 최고관리자만 수정할 수 있습니다.");
        }

        private void RunAfterHandleReady(MethodInvoker action)
        {
            if (action == null || this.IsDisposed)
            {
                return;
            }

            if (this.IsHandleCreated)
            {
                try
                {
                    this.BeginInvoke(action);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // 폼 핸들이 아직 준비되지 않았거나 닫히는 중이면 지연 호출 대신 즉시 실행한다.
                }
            }

            action();
        }

        private int AddGroupedRows(RebarUnitWeightRow row)
        {
            // CSV에 저장된 조견표 값이 있더라도 화면 로드 시에는 규격/단위무게를 기준으로 다시 계산한다.
            // 조견표 계산 셀은 사용자가 직접 수정하는 값이 아니라 자동 산출 결과다.
            return AddGroupedRows(row == null ? "" : row.Spec, row == null ? 0 : row.UnitWeightKgPerMeter);
        }

        private int AddGroupedRows(string spec, double unitWeight)
        {
            int start = grid.Rows.Add();
            grid.Rows[start].Cells["Spec"].Value = spec == null ? "" : spec.Trim().ToUpperInvariant();
            grid.Rows[start].Cells["UnitWeightKgM"].Value = unitWeight > 0 ? unitWeight.ToString("0.000", CultureInfo.InvariantCulture) : "";
            grid.Rows[start].Cells["RowType"].Value = "1본중량";

            int countRow = grid.Rows.Add();
            grid.Rows[countRow].Cells["RowType"].Value = "총본수";

            int totalRow = grid.Rows.Add();
            grid.Rows[totalRow].Cells["RowType"].Value = "중량";

            int totalLengthRow = grid.Rows.Add();
            grid.Rows[totalLengthRow].Cells["RowType"].Value = "총길이";

            ApplyGroupCellEditPolicy(start);
            RecalculateGroupRows(start);
            return start;
        }

        private void ApplyGroupCellEditPolicy(int startRow)
        {
            if (grid == null || startRow < 0 || startRow + RowsPerSpec - 1 >= grid.Rows.Count)
            {
                return;
            }

            int r;
            int c;
            for (r = startRow; r < startRow + RowsPerSpec; r++)
            {
                for (c = 0; c < grid.Columns.Count; c++)
                {
                    grid.Rows[r].Cells[c].ReadOnly = true;
                }
            }

            if (canEdit)
            {
                grid.Rows[startRow].Cells["Spec"].ReadOnly = false;
                grid.Rows[startRow].Cells["UnitWeightKgM"].ReadOnly = false;
            }
        }

        private void ApplyStoredLengthValues(int startRow, RebarUnitWeightRow row)
        {
            if (row == null || startRow < 0 || startRow + RowsPerSpec - 1 >= grid.Rows.Count)
            {
                return;
            }

            ApplyStoredArray(startRow, row.OneBarWeights);
            ApplyStoredArray(startRow + 1, row.BundleCounts);
            ApplyStoredArray(startRow + 2, row.BundleWeights);
            ApplyStoredArray(startRow + 3, row.TotalLengths);
        }

        private void ApplyStoredArray(int rowIndex, string[] values)
        {
            if (values == null || values.Length == 0 || rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            int i;
            for (i = 0; i < StandardLengthColumnNames.Length && i < values.Length; i++)
            {
                if (values[i] != null && values[i].Trim() != "")
                {
                    grid.Rows[rowIndex].Cells[StandardLengthColumnNames[i]].Value = values[i].Trim();
                }
            }
        }


        private void Grid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            TextBox textBox = e.Control as TextBox;
            if (textBox == null || grid == null || grid.CurrentCell == null)
            {
                return;
            }

            textBox.KeyPress -= SpecEditTextBox_KeyPress;
            textBox.KeyPress -= UnitWeightEditTextBox_KeyPress;
            textBox.TextChanged -= SpecEditTextBox_TextChanged;
            textBox.TextChanged -= UnitWeightEditTextBox_TextChanged;
            textBox.ImeMode = ImeMode.NoControl;

            string columnName = grid.Columns[grid.CurrentCell.ColumnIndex].Name;
            if (columnName == "Spec")
            {
                textBox.ImeMode = ImeMode.Disable;
                textBox.KeyPress += SpecEditTextBox_KeyPress;
                textBox.TextChanged += SpecEditTextBox_TextChanged;
            }
            else if (columnName == "UnitWeightKgM")
            {
                textBox.ImeMode = ImeMode.Disable;
                textBox.KeyPress += UnitWeightEditTextBox_KeyPress;
                textBox.TextChanged += UnitWeightEditTextBox_TextChanged;
            }
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void SpecEditTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
            {
                return;
            }

            bool isAsciiLetter = (e.KeyChar >= 'A' && e.KeyChar <= 'Z') || (e.KeyChar >= 'a' && e.KeyChar <= 'z');
            bool isDigit = e.KeyChar >= '0' && e.KeyChar <= '9';
            if (!isAsciiLetter && !isDigit)
            {
                e.Handled = true;
            }
        }

        private void UnitWeightEditTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
            {
                return;
            }

            if (e.KeyChar >= '0' && e.KeyChar <= '9')
            {
                return;
            }

            if (e.KeyChar == '.' || e.KeyChar == ',')
            {
                TextBox textBox = sender as TextBox;
                string text = textBox == null ? "" : textBox.Text;
                if (text.IndexOf('.') < 0 && text.IndexOf(',') < 0)
                {
                    return;
                }
            }

            e.Handled = true;
        }

        private void SpecEditTextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null)
            {
                return;
            }

            string filtered = FilterSpecText(textBox.Text);
            if (filtered == textBox.Text)
            {
                return;
            }

            int selectionStart = Math.Min(filtered.Length, textBox.SelectionStart);
            textBox.Text = filtered;
            textBox.SelectionStart = selectionStart;
        }

        private void UnitWeightEditTextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null)
            {
                return;
            }

            string filtered = FilterUnitWeightInputText(textBox.Text);
            if (filtered == textBox.Text)
            {
                return;
            }

            int selectionStart = Math.Min(filtered.Length, textBox.SelectionStart);
            textBox.Text = filtered;
            textBox.SelectionStart = selectionStart;
        }

        private string FilterSpecText(string text)
        {
            if (text == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            int i;
            for (i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                bool isAsciiLetter = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                bool isDigit = ch >= '0' && ch <= '9';
                if (isAsciiLetter || isDigit)
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }
            }
            return builder.ToString();
        }

        private string FilterUnitWeightInputText(string text)
        {
            if (text == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            bool hasDecimalSeparator = false;
            int i;
            for (i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch >= '0' && ch <= '9')
                {
                    builder.Append(ch);
                }
                else if ((ch == '.' || ch == ',') && !hasDecimalSeparator)
                {
                    builder.Append(ch);
                    hasDecimalSeparator = true;
                }
            }
            return builder.ToString();
        }

        private string NormalizeUnitWeightText(string text)
        {
            text = FilterUnitWeightInputText(text == null ? "" : text.Trim());
            if (text.IndexOf(',') >= 0 && text.IndexOf('.') < 0)
            {
                text = text.Replace(',', '.');
            }
            return text;
        }

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (!CanEditCell(e.RowIndex, e.ColumnIndex))
            {
                e.Cancel = true;
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string colName = grid.Columns[e.ColumnIndex].Name;
            if (colName == "Spec")
            {
                object value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = FilterSpecText(value == null ? "" : value.ToString().Trim());
                RecalculateGroupRows(GetGroupStartRowIndex(e.RowIndex));
            }
            else if (colName == "UnitWeightKgM")
            {
                double unit;
                object value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                string normalized = NormalizeUnitWeightText(value == null ? "" : value.ToString());
                if (TryParseNumber(normalized, out unit) && unit > 0)
                {
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = unit.ToString("0.000", CultureInfo.InvariantCulture);
                }
                else
                {
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = normalized;
                }
                RecalculateGroupRows(GetGroupStartRowIndex(e.RowIndex));
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || !canEdit || e.RowIndex < 0)
            {
                return;
            }

            if (e.ColumnIndex >= 0 && (grid.Columns[e.ColumnIndex].Name == "Spec" || grid.Columns[e.ColumnIndex].Name == "UnitWeightKgM"))
            {
                RecalculateGroupRows(GetGroupStartRowIndex(e.RowIndex));
            }

            MarkDirtyIfChanged();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            int groupOffset = e.RowIndex % RowsPerSpec;
            string colName = grid.Columns[e.ColumnIndex].Name;
            bool groupSelected = IsRowInSelectedSpecGroup(e.RowIndex);

            if (groupSelected)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 248, 205);
                e.CellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
                e.CellStyle.SelectionForeColor = TextDark;
            }

            if (groupOffset == 2 && e.ColumnIndex >= 3)
            {
                e.CellStyle.ForeColor = Color.Red;
                e.CellStyle.SelectionForeColor = Color.Red;
                e.CellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            }
            else if (colName == "RowType")
            {
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                e.CellStyle.ForeColor = TextSub;
                e.CellStyle.SelectionForeColor = TextSub;
            }
            else
            {
                e.CellStyle.ForeColor = TextDark;
                e.CellStyle.SelectionForeColor = TextDark;
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string colName = grid.Columns[e.ColumnIndex].Name;
            if (colName != "Spec" && colName != "UnitWeightKgM")
            {
                return;
            }

            int startRow = GetGroupStartRowIndex(e.RowIndex);
            if (startRow < 0 || startRow >= grid.Rows.Count)
            {
                return;
            }

            e.Handled = true;
            PaintMergedSpecCellPart(e, startRow);
        }

        private void PaintMergedSpecCellPart(DataGridViewCellPaintingEventArgs e, int startRow)
        {
            int offset = e.RowIndex - startRow;
            Rectangle bounds = e.CellBounds;
            bool groupSelected = IsRowInSelectedSpecGroup(startRow);
            Color backColor = groupSelected ? Color.FromArgb(255, 248, 205) : Color.White;
            Color borderColor = Color.FromArgb(221, 226, 232);

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (Pen pen = new Pen(borderColor, 1F))
            {
                int left = bounds.Left;
                int right = bounds.Right - 1;
                int top = bounds.Top;
                int bottom = bounds.Bottom - 1;

                e.Graphics.DrawLine(pen, left, top, left, bottom);
                e.Graphics.DrawLine(pen, right, top, right, bottom);

                if (startRow == 0 && offset == 0)
                {
                    e.Graphics.DrawLine(pen, left, top, right, top);
                }

                if (offset == RowsPerSpec - 1 || e.RowIndex == grid.Rows.Count - 1)
                {
                    e.Graphics.DrawLine(pen, left, bottom, right, bottom);
                }
            }

            if (offset == 0)
            {
                string text = GetCellText(startRow, e.ColumnIndex);
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    e.CellStyle.Font,
                    bounds,
                    TextDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
                );
            }
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.Button != MouseButtons.Left)
            {
                return;
            }

            SelectSpecGroup(GetGroupStartRowIndex(e.RowIndex));
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            int startRow = GetGroupStartRowIndex(e.RowIndex);
            SelectSpecGroup(startRow);

        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (!CanEditCell(e.RowIndex, e.ColumnIndex))
            {
                return;
            }

            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            grid.BeginEdit(true);
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (!canEdit || grid.CurrentCell == null)
            {
                return;
            }

            if (e.KeyCode == Keys.F2 && CanEditCell(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex))
            {
                grid.BeginEdit(true);
                e.Handled = true;
            }
        }

        private bool IsMergedEditableColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            string name = grid.Columns[columnIndex].Name;
            return name == "Spec" || name == "UnitWeightKgM";
        }


        private bool CanEditCell(int rowIndex, int columnIndex)
        {
            if (!canEdit || rowIndex < 0 || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            string name = grid.Columns[columnIndex].Name;

            // 최고관리자도 직접 수정할 수 있는 값은 규격과 단위무게뿐이다.
            // 1본중량, 총본수, 중량, 총길이는 단위무게와 길이 기준으로 자동 계산되므로 편집하지 않는다.
            return IsGroupStartRow(rowIndex) && (name == "Spec" || name == "UnitWeightKgM");
        }

        private void MoveCurrentCellToGroupStart(int startRow, int columnIndex)
        {
            if (grid == null || startRow < 0 || startRow >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            grid.CurrentCell = grid.Rows[startRow].Cells[columnIndex];
        }

        private void SelectSpecGroup(int startRow)
        {
            if (grid == null || startRow < 0 || startRow >= grid.Rows.Count)
            {
                return;
            }

            selectedSpecStartRow = startRow;
            grid.ClearSelection();

            int endRow = Math.Min(grid.Rows.Count - 1, startRow + RowsPerSpec - 1);
            int r;
            int c;
            for (r = startRow; r <= endRow; r++)
            {
                for (c = 0; c < grid.Columns.Count; c++)
                {
                    grid.Rows[r].Cells[c].Selected = true;
                }
            }

            grid.Invalidate();
        }

        private bool IsRowInSelectedSpecGroup(int rowIndex)
        {
            return selectedSpecStartRow >= 0 && rowIndex >= selectedSpecStartRow && rowIndex < selectedSpecStartRow + RowsPerSpec;
        }

        private bool IsGroupStartRow(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex % RowsPerSpec == 0;
        }

        private int GetGroupStartRowIndex(int rowIndex)
        {
            if (rowIndex < 0)
            {
                return 0;
            }

            return rowIndex - (rowIndex % RowsPerSpec);
        }

        private void RecalculateGroupRows(int startRow)
        {
            if (grid == null || startRow < 0 || startRow + RowsPerSpec - 1 >= grid.Rows.Count)
            {
                return;
            }

            string spec = GetCellText(startRow, "Spec").Trim().ToUpperInvariant();
            double unitWeight;
            bool validUnit = TryParseNumber(GetCellText(startRow, "UnitWeightKgM"), out unitWeight) && unitWeight > 0;

            int i;
            for (i = 0; i < StandardLengthColumnNames.Length; i++)
            {
                string colName = StandardLengthColumnNames[i];

                if (!validUnit)
                {
                    int clearOffset;
                    for (clearOffset = 0; clearOffset < RowsPerSpec; clearOffset++)
                    {
                        grid.Rows[startRow + clearOffset].Cells[colName].Value = "";
                    }
                    continue;
                }

                double length = StandardLengths[i];
                double oneBarKg = Math.Round(unitWeight * length, 2, MidpointRounding.AwayFromZero);
                int bundleCount = GetBundleCount(spec, unitWeight, length, i);
                double bundleWeightKg = Math.Round((unitWeight * length) * bundleCount, 0, MidpointRounding.AwayFromZero);
                double totalLengthM = Math.Round(length * bundleCount, 3, MidpointRounding.AwayFromZero);

                grid.Rows[startRow].Cells[colName].Value = oneBarKg.ToString("0.00", CultureInfo.InvariantCulture);
                grid.Rows[startRow + 1].Cells[colName].Value = bundleCount.ToString("0", CultureInfo.InvariantCulture);
                grid.Rows[startRow + 2].Cells[colName].Value = bundleWeightKg.ToString("N0", CultureInfo.InvariantCulture);
                grid.Rows[startRow + 3].Cells[colName].Value = FormatFlexibleNumber(totalLengthM, 3);
            }
        }

        private int GetBundleCount(string spec, double unitWeight, double length, int lengthIndex)
        {
            int[] counts;
            if (spec != null && StandardBundleCounts.TryGetValue(spec.Trim().ToUpperInvariant(), out counts) && lengthIndex >= 0 && lengthIndex < counts.Length)
            {
                return counts[lengthIndex];
            }

            double oneBarKg = unitWeight * length;
            if (oneBarKg <= 0)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Floor(1000.0 / oneBarKg));
        }

        private void Add_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }

            int startRow = AddGroupedRows("", 0);
            SelectSpecGroup(startRow);
            MoveCurrentCellToGroupStart(startRow, grid.Columns["Spec"].Index);
            MarkDirtyIfChanged();
            UpdateStatus("새 규격을 추가했습니다. 규격과 단위무게를 입력한 뒤 저장하세요.");

            RunAfterHandleReady((MethodInvoker)delegate
            {
                if (grid == null || grid.IsDisposed || startRow >= grid.Rows.Count)
                {
                    return;
                }

                grid.Focus();
                MoveCurrentCellToGroupStart(startRow, grid.Columns["Spec"].Index);
                grid.BeginEdit(true);
            });
        }

        private void Delete_Click(object sender, EventArgs e)
        {
            if (!canEdit || grid.CurrentCell == null || grid.CurrentCell.RowIndex < 0)
            {
                return;
            }

            int startRow = GetGroupStartRowIndex(grid.CurrentCell.RowIndex);
            string spec = GetCellText(startRow, "Spec");

            if (MessageBox.Show("선택한 규격을 삭제하시겠습니까?\r\n\r\n규격: " + (spec.Trim() == "" ? "미입력" : spec.Trim()), "OVIA", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            int i;
            for (i = 0; i < RowsPerSpec; i++)
            {
                if (startRow < grid.Rows.Count)
                {
                    grid.Rows.RemoveAt(startRow);
                }
            }

            selectedSpecStartRow = -1;
            MarkDirtyIfChanged();
            UpdateStatus("선택한 규격이 삭제되었습니다. 저장하기를 눌러 반영하세요.");
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "기본값으로 복원하면 현재 저장된 값과 추가/삭제/수정한 내용이 기본 이형철근 단위중량표로 대체됩니다.\r\n\r\n기존의 저장된 값들이 없어질 수 있습니다. 계속하시겠습니까?",
                "OVIA 기본값 복원",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.OK)
            {
                return;
            }

            isLoading = true;
            grid.Rows.Clear();
            selectedSpecStartRow = -1;

            List<RebarUnitWeightRow> defaults = OviaRebarUnitWeightStore.CreateDefaultRows();
            int i;
            for (i = 0; i < defaults.Count; i++)
            {
                AddGroupedRows(defaults[i].Spec, defaults[i].UnitWeightKgPerMeter);
            }

            isLoading = false;
            MarkDirtyIfChanged();
            grid.ClearSelection();
            grid.Invalidate();
            UpdateStatus("기본값으로 복원되었습니다. 저장하기를 눌러 반영하세요.");
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            if (grid.IsCurrentCellInEditMode)
            {
                grid.EndEdit();
            }

            List<RebarUnitWeightRow> rows;
            if (!TryBuildRowsFromGrid(out rows))
            {
                return;
            }

            try
            {
                OviaRebarUnitWeightStore.SaveRows(rows);
                cleanSignature = BuildGridSignature();
                isDirty = false;
                UpdateStatus("이형철근 단위중량표 저장 완료. 이후 BarList 계산 기준에 반영됩니다.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("단위중량표 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message, "OVIA 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryBuildRowsFromGrid(out List<RebarUnitWeightRow> rows)
        {
            rows = new List<RebarUnitWeightRow>();
            HashSet<string> specs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int r;
            for (r = 0; r < grid.Rows.Count; r += RowsPerSpec)
            {
                string spec = GetCellText(r, "Spec").Trim().ToUpperInvariant();
                string unitText = GetCellText(r, "UnitWeightKgM");

                if (!Regex.IsMatch(spec, @"^D\d+$"))
                {
                    MessageBox.Show("규격은 D10, D13, D16처럼 입력해야 합니다.\r\n\r\n오류 행: " + (r + 1).ToString(CultureInfo.InvariantCulture), "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (r < grid.Rows.Count)
                    {
                        grid.CurrentCell = grid.Rows[r].Cells["Spec"];
                    }
                    return false;
                }

                if (specs.Contains(spec))
                {
                    MessageBox.Show("중복된 규격이 있습니다.\r\n\r\n규격: " + spec, "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    grid.CurrentCell = grid.Rows[r].Cells["Spec"];
                    return false;
                }

                double unit;
                if (!TryParseNumber(unitText, out unit) || unit <= 0)
                {
                    MessageBox.Show("단위무게(kg/m)는 0보다 큰 숫자여야 합니다.\r\n\r\n오류 행: " + (r + 1).ToString(CultureInfo.InvariantCulture), "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    grid.CurrentCell = grid.Rows[r].Cells["UnitWeightKgM"];
                    return false;
                }

                specs.Add(spec);
                RebarUnitWeightRow row = new RebarUnitWeightRow();
                row.Spec = spec;
                row.UnitWeightKgPerMeter = unit;
                row.Enabled = true;
                row.OneBarWeights = ReadGridLengthValues(r);
                row.BundleCounts = ReadGridLengthValues(r + 1);
                row.BundleWeights = ReadGridLengthValues(r + 2);
                row.TotalLengths = ReadGridLengthValues(r + 3);
                rows.Add(row);
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("저장할 단위중량 데이터가 없습니다.", "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }


        private string[] ReadGridLengthValues(int rowIndex)
        {
            string[] values = new string[StandardLengthColumnNames.Length];
            int i;
            for (i = 0; i < StandardLengthColumnNames.Length; i++)
            {
                values[i] = GetCellText(rowIndex, StandardLengthColumnNames[i]).Trim();
            }
            return values;
        }

        private string GetCellText(int rowIndex, string columnName)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count || !grid.Columns.Contains(columnName))
            {
                return "";
            }

            object value = grid.Rows[rowIndex].Cells[columnName].Value;
            return value == null ? "" : value.ToString();
        }

        private string GetCellText(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            object value = grid.Rows[rowIndex].Cells[columnIndex].Value;
            return value == null ? "" : value.ToString();
        }

        private bool TryParseNumber(string text, out double value)
        {
            value = 0;

            if (text == null)
            {
                return false;
            }

            text = text.Trim().Replace(" ", "");
            if (text.IndexOf(',') >= 0 && text.IndexOf('.') < 0)
            {
                text = text.Replace(',', '.');
            }
            else
            {
                text = text.Replace(",", "");
            }

            if (text == "")
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return false;
        }

        private string FormatFlexibleNumber(double value, int maxDecimals)
        {
            if (Math.Abs(value - Math.Round(value, 0, MidpointRounding.AwayFromZero)) < 0.0005)
            {
                return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
            }

            string format = "N" + Math.Max(0, maxDecimals).ToString(CultureInfo.InvariantCulture);
            return value.ToString(format, CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        private void UpdateStatus(string text)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = text;
            }
        }

        private void FrmRebarUnitWeightTable_FormClosing(object sender, FormClosingEventArgs e)
        {
            CommitCurrentEditIfNeeded();

            if (!canEdit || !HasUnsavedChanges())
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 단위중량표 변경사항이 있습니다.\r\n\r\n저장하지 않고 닫으시겠습니까?",
                "OVIA",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.OK)
            {
                e.Cancel = true;
            }
        }

        public bool CanLeaveWorkspaceScreen()
        {
            CommitCurrentEditIfNeeded();

            if (!canEdit || !HasUnsavedChanges())
            {
                return true;
            }

            return MessageBox.Show(
                "저장하지 않은 단위중량표 변경사항이 있습니다.\r\n\r\n이동하시겠습니까?",
                "OVIA",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning
            ) == DialogResult.OK;
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }

        public bool HasUnsavedWorkspaceData()
        {
            CommitCurrentEditIfNeeded();

            if (!canEdit)
            {
                return false;
            }

            return HasUnsavedChanges();
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "이형철근 단위중량표";
        }

        public void ApplyWorkspaceLayout()
        {
            int width = Math.Max(1, this.ClientSize.Width - 64);

            if (tableHeaderPanel != null)
            {
                tableHeaderPanel.Width = width;
            }

            if (tableBasisLabel != null && tableHeaderPanel != null)
            {
                tableBasisLabel.Left = Math.Max(430, tableHeaderPanel.Width - 570);
                tableBasisLabel.Width = 560;
            }

            if (grid != null)
            {
                grid.Width = width;
                grid.Height = Math.Max(220, this.ClientSize.Height - 268);
            }

            int buttonY = Math.Max(0, this.ClientSize.Height - 118);
            if (btnAdd != null) btnAdd.Top = buttonY;
            if (btnDelete != null) btnDelete.Top = buttonY;
            if (btnReset != null) btnReset.Top = buttonY;
            if (btnSave != null)
            {
                btnSave.Top = buttonY;
                btnSave.Left = Math.Max(32, this.ClientSize.Width - 280);
            }
            if (btnClose != null)
            {
                btnClose.Top = buttonY;
                btnClose.Left = Math.Max(32, this.ClientSize.Width - 144);
            }

            if (lblStatus != null)
            {
                lblStatus.Width = width;
                lblStatus.Top = Math.Max(0, this.ClientSize.Height - 64);
            }
        }

        private void MarkDirtyIfChanged()
        {
            if (isLoading || suppressDirtyEvent)
            {
                return;
            }

            isDirty = cleanSignature != BuildGridSignature();
            if (isDirty)
            {
                UpdateStatus("저장하지 않은 단위중량표 변경사항이 있습니다.");
            }
        }

        private bool HasUnsavedChanges()
        {
            if (grid == null)
            {
                return isDirty;
            }

            isDirty = cleanSignature != BuildGridSignature();
            return isDirty;
        }

        private void CommitCurrentEditIfNeeded()
        {
            if (grid == null)
            {
                return;
            }

            try
            {
                if (grid.IsCurrentCellInEditMode || grid.IsCurrentRowDirty)
                {
                    grid.EndEdit(DataGridViewDataErrorContexts.Commit);
                }
            }
            catch
            {
            }
        }

        private string BuildGridSignature()
        {
            if (grid == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            int r;
            for (r = 0; r < grid.Rows.Count; r += RowsPerSpec)
            {
                builder.Append(GetCellText(r, "Spec").Trim().ToUpperInvariant());
                builder.Append('|');
                double unit;
                if (TryParseNumber(GetCellText(r, "UnitWeightKgM"), out unit))
                {
                    builder.Append(unit.ToString("0.########", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(GetCellText(r, "UnitWeightKgM").Trim());
                }
                builder.Append('|');
                int i;
                for (i = 0; i < StandardLengthColumnNames.Length; i++)
                {
                    builder.Append(GetCellText(r, StandardLengthColumnNames[i]).Trim());
                    builder.Append(',');
                    builder.Append(GetCellText(r + 1, StandardLengthColumnNames[i]).Trim());
                    builder.Append(',');
                    builder.Append(GetCellText(r + 2, StandardLengthColumnNames[i]).Trim());
                    builder.Append(',');
                    builder.Append(GetCellText(r + 3, StandardLengthColumnNames[i]).Trim());
                    builder.Append(';');
                }
                builder.Append('\n');
            }

            return builder.ToString();
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
                this.Close();
            }
        }

        private static Dictionary<string, int[]> CreateStandardBundleCounts()
        {
            Dictionary<string, int[]> map = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            map["D10"] = new int[] { 300, 270, 270, 240, 210, 210, 180, 150, 150 };
            map["D13"] = new int[] { 160, 160, 140, 140, 120, 120, 100, 100, 80 };
            map["D16"] = new int[] { 105, 105, 90, 90, 75, 75, 60, 60, 60 };
            map["D19"] = new int[] { 74, 68, 63, 59, 56, 49, 44, 40, 37 };
            map["D22"] = new int[] { 55, 51, 47, 44, 41, 37, 33, 30, 27 };
            map["D25"] = new int[] { 42, 39, 36, 33, 32, 28, 25, 23, 21 };
            map["D29"] = new int[] { 33, 31, 28, 26, 25, 22, 20, 18, 17 };
            map["D32"] = new int[] { 27, 25, 23, 21, 20, 18, 16, 15, 13 };
            map["D35"] = new int[] { 22, 20, 19, 18, 17, 15, 13, 12, 11 };
            map["D38"] = new int[] { 19, 17, 16, 15, 14, 12, 11, 10, 9 };
            map["D41"] = new int[] { 16, 15, 14, 13, 12, 11, 10, 9, 8 };
            map["D51"] = new int[] { 11, 10, 9, 8, 8, 7, 6, 6, 5 };
            return map;
        }
    }

    public class RebarUnitWeightRow
    {
        public string Spec = "";
        public double UnitWeightKgPerMeter = 0;
        public bool Enabled = true;
        public string[] OneBarWeights = new string[0];
        public string[] BundleCounts = new string[0];
        public string[] BundleWeights = new string[0];
        public string[] TotalLengths = new string[0];
    }

    public static class OviaRebarUnitWeightStore
    {
        public static bool IsSuperAdminUser(string userId)
        {
            string value = userId == null ? "" : userId.Trim().ToLowerInvariant();

            if (value == "")
            {
                return false;
            }

            return value == "admin"
                || value == "administrator"
                || value == "root"
                || value == "celmon"
                || value == "oviaadmin"
                || value == "system"
                || value == "superadmin"
                || value == "systemadmin"
                || value == "최고관리자"
                || value == "시스템관리자";
        }

        public static Dictionary<string, double> LoadEnabledUnitWeights()
        {
            List<RebarUnitWeightRow> rows = LoadRows();
            Dictionary<string, double> map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (rows[i].Enabled && rows[i].Spec != null && rows[i].Spec.Trim() != "" && rows[i].UnitWeightKgPerMeter > 0)
                {
                    map[rows[i].Spec.Trim().ToUpperInvariant()] = rows[i].UnitWeightKgPerMeter;
                }
            }

            return map;
        }

        public static List<RebarUnitWeightRow> LoadRows()
        {
            string path = GetWritableFilePath();
            if (File.Exists(path))
            {
                return ReadRowsFromCsv(path);
            }

            string installed = GetInstalledFilePath();
            if (File.Exists(installed))
            {
                return ReadRowsFromCsv(installed);
            }

            return CreateDefaultRows();
        }

        public static void SaveRows(List<RebarUnitWeightRow> rows)
        {
            string path = GetWritableFilePath();
            string dir = Path.GetDirectoryName(path);

            if (dir != null && dir.Trim() != "")
            {
                Directory.CreateDirectory(dir);
            }

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(BuildCsvHeader());
                int i;
                for (i = 0; i < rows.Count; i++)
                {
                    writer.WriteLine(BuildCsvLine(rows[i]));
                }
            }
        }

        private static string BuildCsvHeader()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("규격,단위중량(kg/m)");
            string[] lengths = new string[] { "6", "6.5", "7", "7.5", "8", "9", "10", "11", "12" };
            int i;
            for (i = 0; i < lengths.Length; i++)
            {
                builder.Append(",").Append(lengths[i]).Append("_1본중량");
                builder.Append(",").Append(lengths[i]).Append("_총본수");
                builder.Append(",").Append(lengths[i]).Append("_중량");
                builder.Append(",").Append(lengths[i]).Append("_총길이");
            }
            return builder.ToString();
        }

        private static string BuildCsvLine(RebarUnitWeightRow row)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Csv(row.Spec));
            builder.Append(",").Append(Csv(row.UnitWeightKgPerMeter.ToString("0.000", CultureInfo.InvariantCulture)));
            int i;
            for (i = 0; i < 9; i++)
            {
                builder.Append(",").Append(Csv(GetArrayValue(row.OneBarWeights, i)));
                builder.Append(",").Append(Csv(GetArrayValue(row.BundleCounts, i)));
                builder.Append(",").Append(Csv(GetArrayValue(row.BundleWeights, i)));
                builder.Append(",").Append(Csv(GetArrayValue(row.TotalLengths, i)));
            }
            return builder.ToString();
        }

        private static string GetArrayValue(string[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null)
            {
                return "";
            }

            return values[index];
        }

        public static List<RebarUnitWeightRow> CreateDefaultRows()
        {
            List<RebarUnitWeightRow> rows = new List<RebarUnitWeightRow>();
            AddDefault(rows, "D10", 0.560);
            AddDefault(rows, "D13", 0.995);
            AddDefault(rows, "D16", 1.560);
            AddDefault(rows, "D19", 2.250);
            AddDefault(rows, "D22", 3.040);
            AddDefault(rows, "D25", 3.980);
            AddDefault(rows, "D29", 5.040);
            AddDefault(rows, "D32", 6.230);
            AddDefault(rows, "D35", 7.510);
            AddDefault(rows, "D38", 8.950);
            AddDefault(rows, "D41", 10.500);
            AddDefault(rows, "D51", 15.900);
            return rows;
        }

        private static void AddDefault(List<RebarUnitWeightRow> rows, string spec, double unitWeight)
        {
            RebarUnitWeightRow row = new RebarUnitWeightRow();
            row.Spec = spec;
            row.UnitWeightKgPerMeter = unitWeight;
            row.Enabled = true;
            rows.Add(row);
        }

        private static string GetWritableFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OVIA", "Rebar", "rebar_unit_weight.csv");
        }

        private static string GetInstalledFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Rebar", "rebar_unit_weight.csv");
        }

        private static List<RebarUnitWeightRow> ReadRowsFromCsv(string path)
        {
            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                if (content.Length > 0 && content[0] == '\uFEFF')
                {
                    content = content.Substring(1);
                }

                List<List<string>> parsed = ParseCsv(content);
                List<RebarUnitWeightRow> rows = new List<RebarUnitWeightRow>();
                int r;

                for (r = 1; r < parsed.Count; r++)
                {
                    if (parsed[r].Count < 2)
                    {
                        continue;
                    }

                    RebarUnitWeightRow row = new RebarUnitWeightRow();
                    row.Spec = parsed[r][0] == null ? "" : parsed[r][0].Trim().ToUpperInvariant();
                    double unit;
                    if (!TryParseNumber(parsed[r][1], out unit) || unit <= 0)
                    {
                        continue;
                    }
                    row.UnitWeightKgPerMeter = unit;
                    row.Enabled = parsed[r].Count < 3 || !IsFalseText(parsed[r][2]);
                    FillExtendedValues(parsed[r], row);

                    if (Regex.IsMatch(row.Spec, @"^D\d+$"))
                    {
                        rows.Add(row);
                    }
                }

                if (rows.Count > 0)
                {
                    return rows;
                }
            }
            catch
            {
            }

            return CreateDefaultRows();
        }


        private static void FillExtendedValues(List<string> parsedRow, RebarUnitWeightRow row)
        {
            row.OneBarWeights = new string[9];
            row.BundleCounts = new string[9];
            row.BundleWeights = new string[9];
            row.TotalLengths = new string[9];

            if (parsedRow == null || parsedRow.Count < 38)
            {
                return;
            }

            int i;
            for (i = 0; i < 9; i++)
            {
                int baseIndex = 2 + (i * 4);
                row.OneBarWeights[i] = GetParsedCell(parsedRow, baseIndex);
                row.BundleCounts[i] = GetParsedCell(parsedRow, baseIndex + 1);
                row.BundleWeights[i] = GetParsedCell(parsedRow, baseIndex + 2);
                row.TotalLengths[i] = GetParsedCell(parsedRow, baseIndex + 3);
            }
        }

        private static string GetParsedCell(List<string> row, int index)
        {
            if (row == null || index < 0 || index >= row.Count || row[index] == null)
            {
                return "";
            }

            return row[index].Trim();
        }

        private static bool IsFalseText(string text)
        {
            if (text == null)
            {
                return false;
            }

            string value = text.Trim().ToUpperInvariant();
            return value == "N" || value == "NO" || value == "FALSE" || value == "0" || value == "미사용";
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (text == null)
            {
                return false;
            }

            text = text.Trim().Replace(" ", "");
            if (text.IndexOf(',') >= 0 && text.IndexOf('.') < 0)
            {
                text = text.Replace(',', '.');
            }
            else
            {
                text = text.Replace(",", "");
            }
            if (text == "")
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        private static string Csv(string value)
        {
            if (value == null)
            {
                value = "";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool inQuotes = false;
            int i;

            for (i = 0; i < content.Length; i++)
            {
                char ch = content[i];

                if (inQuotes)
                {
                    if (ch == '\"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '\"')
                        {
                            cell.Append('\"');
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
                    if (ch == '\"')
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
                        rows.Add(row);
                        row = new List<string>();
                        cell.Length = 0;
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
            }

            row.Add(cell.ToString());
            if (row.Count > 1 || row[0].Trim() != "")
            {
                rows.Add(row);
            }

            return rows;
        }
    }
}
