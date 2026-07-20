using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    /// <summary>
    /// 기존 2,000여 개 형상번호 선택창을 대체하는 CAD 원본 기반 철근 형상 확인·수정창입니다.
    /// CAD 방향은 그대로 유지하며 좌우/상하 반전 또는 회전 판정을 수행하지 않습니다.
    /// </summary>
    public class FrmShapePicker : Form
    {
        private readonly string cadShapeJsonPath;
        private readonly string rawSourceJsonPath;
        private readonly bool isManualDocument;
        private readonly CadShapeEditDocument rawDocument;
        private readonly CadShapeEditDocument workingDocument;
        private CadShapeEditorControl editor;
        private DataGridView textGrid;
        private Label lblMode;
        private Label lblSelectionType;
        private Label lblSelectionId;
        private Label lblStatistics;
        private Label lblStatus;
        private TextBox txtSelectedText;
        private NumericUpDown numRotation;
        private CheckBox chkSnap;
        private Button btnSelectMode;
        private Button btnLineMode;
        private Button btnTextMode;
        private Button btnUndo;
        private Button btnRedo;
        private Button btnDelete;
        private Button btnSplit;
        private Button btnUpdateText;
        private Button btnApply;
        private Button btnCancel;
        private bool suppressUiEvents;
        private bool textGridCommitInProgress;

        public RebarShapeInfo SelectedShape { get; private set; }
        public bool SelectedCadShapeOriginal { get; private set; }
        public string SelectedDimensionText { get; private set; }
        public decimal SelectedTotalLength { get; private set; }
        public string SelectedCadShapeJsonPath { get; private set; }
        public string SelectedShapeSource { get; private set; }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue)
            : this(repository, currentValue, "", "")
        {
        }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue, string currentDimensionText)
            : this(repository, currentValue, currentDimensionText, "")
        {
        }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue, string currentDimensionText, string cadShapeJsonPath)
        {
            this.cadShapeJsonPath = cadShapeJsonPath == null ? "" : cadShapeJsonPath.Trim();

            CadShapeEditDocument loadedDocument = CadShapeEditDocument.Load(this.cadShapeJsonPath);
            CadShapeEditDocument trueOriginalDocument = loadedDocument;
            string originalSourcePath = loadedDocument.OriginalSourcePath == null ? "" : loadedDocument.OriginalSourcePath.Trim();
            string resolvedRawSourcePath = "";

            if (originalSourcePath != "" && !Path.IsPathRooted(originalSourcePath) && this.cadShapeJsonPath != "")
            {
                string editDirectory = Path.GetDirectoryName(this.cadShapeJsonPath);

                if (editDirectory != null && editDirectory.Trim() != "")
                {
                    originalSourcePath = Path.Combine(editDirectory, originalSourcePath.Replace('/', Path.DirectorySeparatorChar));
                }
            }

            if (originalSourcePath != "" && File.Exists(originalSourcePath))
            {
                resolvedRawSourcePath = Path.GetFullPath(originalSourcePath);
                trueOriginalDocument = CadShapeEditDocument.Load(resolvedRawSourcePath);
            }
            else if (this.cadShapeJsonPath != "" && File.Exists(this.cadShapeJsonPath))
            {
                resolvedRawSourcePath = Path.GetFullPath(this.cadShapeJsonPath);
            }

            rawSourceJsonPath = resolvedRawSourcePath;
            isManualDocument = this.cadShapeJsonPath == ""
                || loadedDocument.Source.Equals("OVIA_MANUAL", StringComparison.OrdinalIgnoreCase)
                || loadedDocument.Source.Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
            rawDocument = trueOriginalDocument.Clone();
            workingDocument = loadedDocument.Clone();
            ApplyDimensionOverrides(workingDocument, currentDimensionText);

            SelectedShape = null;
            SelectedCadShapeOriginal = false;
            SelectedDimensionText = "";
            SelectedTotalLength = 0M;
            SelectedCadShapeJsonPath = "";
            SelectedShapeSource = isManualDocument ? "MANUAL" : "CAD";

            BuildUI();
            editor.LoadDocument(workingDocument, rawDocument);
            RefreshTextGrid();
            RefreshSelectionPanel();
            RefreshStatistics();
            UpdateToolbarState();
        }

        private void BuildUI()
        {
            Text = "철근 형상 확인·수정";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new Size(1180, 720);
            ClientSize = new Size(1440, 860);
            BackColor = Color.FromArgb(244, 246, 250);
            Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);
            KeyPreview = true;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 82;
            header.BackColor = Color.White;
            header.Padding = new Padding(16, 10, 16, 8);
            Controls.Add(header);

            Label title = new Label();
            title.Text = isManualDocument ? "철근 형상 직접 작성·수정" : "CAD 철근 형상 확인·수정";
            title.Font = OviaFluentTheme.FontKorean(15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, 10);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = isManualDocument
                ? "형상번호 없이 빈 캔버스에서 선과 문자를 직접 작성합니다. 작성한 방향 그대로 저장됩니다."
                : "형상번호를 선택하지 않습니다. CAD 도면의 방향과 연결 구조를 그대로 유지하면서 누락·오인식된 선과 문자를 직접 보정합니다.";
            subtitle.ForeColor = Color.FromArgb(92, 101, 116);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(18, 42);
            header.Controls.Add(subtitle);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 54;
            toolbar.BackColor = Color.FromArgb(248, 249, 252);
            toolbar.Padding = new Padding(12, 10, 12, 8);
            Controls.Add(toolbar);

            FlowLayoutPanel toolFlow = new FlowLayoutPanel();
            toolFlow.Dock = DockStyle.Fill;
            toolFlow.WrapContents = false;
            toolFlow.AutoScroll = true;
            toolFlow.FlowDirection = FlowDirection.LeftToRight;
            toolbar.Controls.Add(toolFlow);

            btnSelectMode = CreateToolbarButton("선택·이동", 92, BtnSelectMode_Click);
            btnLineMode = CreateToolbarButton("선 추가", 78, BtnLineMode_Click);
            btnTextMode = CreateToolbarButton("문자 추가", 82, BtnTextMode_Click);
            btnDelete = CreateToolbarButton("선택 삭제", 86, BtnDelete_Click);
            btnSplit = CreateToolbarButton("선 분할", 74, BtnSplit_Click);
            btnUndo = CreateToolbarButton("실행 취소", 86, BtnUndo_Click);
            btnRedo = CreateToolbarButton("다시 실행", 86, BtnRedo_Click);
            Button btnHorizontal = CreateToolbarButton("수평 맞춤", 86, BtnHorizontal_Click);
            Button btnVertical = CreateToolbarButton("수직 맞춤", 86, BtnVertical_Click);
            Button btnFit = CreateToolbarButton("화면 맞춤", 86, BtnFit_Click);
            Button btnZoomIn = CreateToolbarButton("확대", 62, BtnZoomIn_Click);
            Button btnZoomOut = CreateToolbarButton("축소", 62, BtnZoomOut_Click);
            Button btnRestore = CreateToolbarButton(isManualDocument ? "초기 형상 복원" : "CAD 원본 복원", 112, BtnRestore_Click);

            toolFlow.Controls.Add(btnSelectMode);
            toolFlow.Controls.Add(btnLineMode);
            toolFlow.Controls.Add(btnTextMode);
            toolFlow.Controls.Add(CreateToolbarSeparator());
            toolFlow.Controls.Add(btnDelete);
            toolFlow.Controls.Add(btnSplit);
            toolFlow.Controls.Add(btnUndo);
            toolFlow.Controls.Add(btnRedo);
            toolFlow.Controls.Add(CreateToolbarSeparator());
            toolFlow.Controls.Add(btnHorizontal);
            toolFlow.Controls.Add(btnVertical);
            toolFlow.Controls.Add(btnFit);
            toolFlow.Controls.Add(btnZoomIn);
            toolFlow.Controls.Add(btnZoomOut);
            toolFlow.Controls.Add(CreateToolbarSeparator());
            toolFlow.Controls.Add(btnRestore);

            chkSnap = new CheckBox();
            chkSnap.Text = "15도 스냅";
            chkSnap.Checked = true;
            chkSnap.AutoSize = true;
            chkSnap.Margin = new Padding(14, 8, 4, 0);
            chkSnap.CheckedChanged += ChkSnap_CheckedChanged;
            toolFlow.Controls.Add(chkSnap);

            lblMode = new Label();
            lblMode.AutoSize = true;
            lblMode.ForeColor = Color.FromArgb(64, 76, 94);
            lblMode.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Bold);
            lblMode.Margin = new Padding(18, 9, 0, 0);
            toolFlow.Controls.Add(lblMode);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 58;
            bottom.BackColor = Color.White;
            bottom.Padding = new Padding(16, 10, 16, 10);
            Controls.Add(bottom);

            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = Color.FromArgb(92, 101, 116);
            bottom.Controls.Add(lblStatus);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Dock = DockStyle.Right;
            btnCancel.Width = 92;
            btnCancel.Margin = new Padding(8, 0, 0, 0);
            btnCancel.Click += BtnCancel_Click;
            bottom.Controls.Add(btnCancel);

            btnApply = new Button();
            btnApply.Text = "수정 적용";
            btnApply.Dock = DockStyle.Right;
            btnApply.Width = 108;
            btnApply.BackColor = Color.FromArgb(18, 103, 206);
            btnApply.ForeColor = Color.White;
            btnApply.FlatStyle = FlatStyle.Flat;
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Click += BtnApply_Click;
            bottom.Controls.Add(btnApply);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.IsSplitterFixed = false;
            split.SplitterWidth = 6;
            split.SplitterDistance = 1080;
            split.Panel1.Padding = new Padding(14, 12, 6, 12);
            split.Panel2.Padding = new Padding(6, 12, 14, 12);
            split.BackColor = Color.FromArgb(224, 229, 237);
            Controls.Add(split);
            split.SendToBack();
            toolbar.BringToFront();
            header.BringToFront();
            bottom.BringToFront();

            Panel editorFrame = new Panel();
            editorFrame.Dock = DockStyle.Fill;
            editorFrame.BackColor = Color.White;
            editorFrame.BorderStyle = BorderStyle.FixedSingle;
            split.Panel1.Controls.Add(editorFrame);

            editor = new CadShapeEditorControl();
            editor.Dock = DockStyle.Fill;
            editor.SelectionChanged += Editor_SelectionChanged;
            editor.DocumentChanged += Editor_DocumentChanged;
            editor.ModeChanged += Editor_ModeChanged;
            editor.TextEditRequested += Editor_TextEditRequested;
            editorFrame.Controls.Add(editor);

            BuildRightPanel(split.Panel2);

            // Enter/Esc는 편집 캔버스의 연속 선 그리기 종료·취소에 사용하므로
            // Form의 AcceptButton/CancelButton으로 가로채지 않습니다.
        }

        private void BuildRightPanel(Control parent)
        {
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Color.FromArgb(244, 246, 250);
            right.ColumnCount = 1;
            right.RowCount = 4;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            parent.Controls.Add(right);

            GroupBox selectedGroup = new GroupBox();
            selectedGroup.Text = "선택 요소 속성";
            selectedGroup.Dock = DockStyle.Fill;
            selectedGroup.Padding = new Padding(12, 18, 12, 12);
            right.Controls.Add(selectedGroup, 0, 0);

            Label typeTitle = CreatePropertyLabel("종류", 18, 30, 62);
            selectedGroup.Controls.Add(typeTitle);
            lblSelectionType = CreatePropertyValueLabel(88, 28, 220);
            selectedGroup.Controls.Add(lblSelectionType);

            Label idTitle = CreatePropertyLabel("문자 ID", 18, 59, 62);
            selectedGroup.Controls.Add(idTitle);
            lblSelectionId = CreatePropertyValueLabel(88, 57, 220);
            selectedGroup.Controls.Add(lblSelectionId);

            Label textTitle = CreatePropertyLabel("문자값", 18, 91, 62);
            selectedGroup.Controls.Add(textTitle);
            txtSelectedText = new TextBox();
            txtSelectedText.Location = new Point(88, 88);
            txtSelectedText.Size = new Size(150, 25);
            txtSelectedText.Leave += TxtSelectedText_Leave;
            txtSelectedText.KeyDown += TxtSelectedText_KeyDown;
            selectedGroup.Controls.Add(txtSelectedText);

            btnUpdateText = new Button();
            btnUpdateText.Text = "값 적용";
            btnUpdateText.Location = new Point(244, 87);
            btnUpdateText.Size = new Size(64, 27);
            btnUpdateText.FlatStyle = FlatStyle.Flat;
            btnUpdateText.FlatAppearance.BorderColor = Color.FromArgb(18, 103, 206);
            btnUpdateText.BackColor = Color.FromArgb(18, 103, 206);
            btnUpdateText.ForeColor = Color.White;
            btnUpdateText.Enabled = false;
            btnUpdateText.Click += BtnUpdateText_Click;
            selectedGroup.Controls.Add(btnUpdateText);

            Label rotationTitle = CreatePropertyLabel("회전각", 18, 124, 62);
            selectedGroup.Controls.Add(rotationTitle);
            numRotation = new NumericUpDown();
            numRotation.Location = new Point(88, 121);
            numRotation.Size = new Size(100, 25);
            numRotation.Minimum = -360;
            numRotation.Maximum = 360;
            numRotation.DecimalPlaces = 1;
            numRotation.Increment = 1;
            numRotation.ValueChanged += NumRotation_ValueChanged;
            selectedGroup.Controls.Add(numRotation);

            Label selectedHelp = new Label();
            selectedHelp.Text = "문자는 캔버스에서 더블클릭하거나 아래 현재값 셀을 클릭해 수정합니다. 선은 양 끝점 핸들을 끌어 보정합니다.";
            selectedHelp.ForeColor = Color.FromArgb(103, 112, 126);
            selectedHelp.Location = new Point(18, 153);
            selectedHelp.Size = new Size(290, 30);
            selectedGroup.Controls.Add(selectedHelp);

            GroupBox textGroup = new GroupBox();
            textGroup.Text = "형상 문자·치수값";
            textGroup.Dock = DockStyle.Fill;
            textGroup.Padding = new Padding(10, 18, 10, 10);
            right.Controls.Add(textGroup, 0, 1);

            textGrid = new DataGridView();
            textGrid.Dock = DockStyle.Fill;
            textGrid.AllowUserToAddRows = false;
            textGrid.AllowUserToDeleteRows = false;
            textGrid.AllowUserToResizeRows = false;
            textGrid.RowHeadersVisible = false;
            textGrid.MultiSelect = false;
            textGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            textGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            textGrid.AutoGenerateColumns = false;
            textGrid.BackgroundColor = Color.White;
            textGrid.BorderStyle = BorderStyle.FixedSingle;
            textGrid.CellEndEdit += TextGrid_CellEndEdit;
            textGrid.CellDoubleClick += TextGrid_CellDoubleClick;
            textGrid.SelectionChanged += TextGrid_SelectionChanged;

            DataGridViewTextBoxColumn idColumn = new DataGridViewTextBoxColumn();
            idColumn.Name = "TextId";
            idColumn.HeaderText = "ID";
            idColumn.Width = 52;
            idColumn.ReadOnly = true;
            idColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            textGrid.Columns.Add(idColumn);

            DataGridViewTextBoxColumn valueColumn = new DataGridViewTextBoxColumn();
            valueColumn.Name = "TextValue";
            valueColumn.HeaderText = "현재값";
            valueColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            valueColumn.ReadOnly = false;
            valueColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            textGrid.Columns.Add(valueColumn);

            DataGridViewTextBoxColumn kindColumn = new DataGridViewTextBoxColumn();
            kindColumn.Name = "TextKind";
            kindColumn.HeaderText = "분류";
            kindColumn.Width = 72;
            kindColumn.ReadOnly = true;
            kindColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            textGrid.Columns.Add(kindColumn);

            textGroup.Controls.Add(textGrid);

            Panel statisticsPanel = new Panel();
            statisticsPanel.Dock = DockStyle.Fill;
            statisticsPanel.BackColor = Color.White;
            statisticsPanel.BorderStyle = BorderStyle.FixedSingle;
            statisticsPanel.Padding = new Padding(12);
            right.Controls.Add(statisticsPanel, 0, 2);

            Label statisticsTitle = new Label();
            statisticsTitle.Text = "형상 정보";
            statisticsTitle.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Bold);
            statisticsTitle.Dock = DockStyle.Top;
            statisticsTitle.Height = 24;
            statisticsPanel.Controls.Add(statisticsTitle);

            lblStatistics = new Label();
            lblStatistics.Dock = DockStyle.Fill;
            lblStatistics.ForeColor = Color.FromArgb(82, 91, 105);
            statisticsPanel.Controls.Add(lblStatistics);

            Label policy = new Label();
            policy.Dock = DockStyle.Fill;
            policy.ForeColor = Color.FromArgb(103, 112, 126);
            policy.Text = isManualDocument
                ? "※ 신규 수동 형상도 형상번호 없이 현재 그려진 방향 그대로 벡터 JSON으로 저장합니다."
                : "※ OVIA는 형상번호·업체별 코드·반전·회전 판정 없이 CAD에 그려진 방향 그대로 저장합니다.";
            policy.Padding = new Padding(2, 8, 2, 0);
            right.Controls.Add(policy, 0, 3);
        }

        private Button CreateToolbarButton(string text, int width, EventHandler clickHandler)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 31;
            button.Margin = new Padding(3, 0, 3, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(197, 203, 213);
            button.BackColor = Color.White;
            button.Click += clickHandler;
            return button;
        }

        private Control CreateToolbarSeparator()
        {
            Panel separator = new Panel();
            separator.Width = 1;
            separator.Height = 26;
            separator.Margin = new Padding(8, 3, 8, 0);
            separator.BackColor = Color.FromArgb(218, 223, 231);
            return separator;
        }

        private Label CreatePropertyLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 22);
            label.ForeColor = Color.FromArgb(75, 84, 99);
            return label;
        }

        private Label CreatePropertyValueLabel(int x, int y, int width)
        {
            Label label = new Label();
            label.Location = new Point(x, y);
            label.Size = new Size(width, 24);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = Color.FromArgb(246, 248, 251);
            label.BorderStyle = BorderStyle.FixedSingle;
            return label;
        }

        private void ApplyDimensionOverrides(CadShapeEditDocument document, string dimensionText)
        {
            if (document == null || dimensionText == null || dimensionText.Trim() == "")
            {
                return;
            }

            Dictionary<string, string> values = ParseDimensionText(dimensionText);
            List<CadShapeEditElement> texts = document.GetTextElements();
            string[] legacyKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < texts.Count; i++)
            {
                string value;

                if (values.TryGetValue(texts[i].TextId, out value))
                {
                    texts[i].Text = value;
                    texts[i].HasBounds = false;
                    continue;
                }

                if (i < legacyKeys.Length && values.TryGetValue(legacyKeys[i], out value))
                {
                    texts[i].Text = value;
                    texts[i].HasBounds = false;
                }
            }
        }

        private Dictionary<string, string> ParseDimensionText(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = (text == null ? "" : text).Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string item = parts[i] == null ? "" : parts[i].Trim();
                int eq = item.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                string key = item.Substring(0, eq).Trim().ToUpperInvariant();
                string value = item.Substring(eq + 1).Trim();

                if (key != "")
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private void RefreshTextGrid()
        {
            if (textGrid == null || editor == null || editor.Document == null)
            {
                return;
            }

            string selectedId = GetSelectedTextGridId();
            suppressUiEvents = true;

            try
            {
                textGrid.Rows.Clear();
                List<CadShapeEditElement> texts = editor.Document.GetTextElements();
                int selectedRowIndex = -1;
                int i;

                for (i = 0; i < texts.Count; i++)
                {
                    int rowIndex = textGrid.Rows.Add(texts[i].TextId, texts[i].Text, ClassifyText(texts[i].Text));
                    textGrid.Rows[rowIndex].Tag = texts[i].TextId;

                    if (selectedId != "" && texts[i].TextId.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedRowIndex = rowIndex;
                    }
                }

                if (selectedRowIndex >= 0)
                {
                    textGrid.ClearSelection();
                    textGrid.Rows[selectedRowIndex].Selected = true;
                }
            }
            finally
            {
                suppressUiEvents = false;
            }
        }

        private string GetSelectedTextGridId()
        {
            if (textGrid == null || textGrid.SelectedRows.Count == 0)
            {
                return "";
            }

            object tag = textGrid.SelectedRows[0].Tag;
            return tag == null ? "" : tag.ToString();
        }

        private string ClassifyText(string value)
        {
            string text = value == null ? "" : value.Trim();
            string upper = text.ToUpperInvariant();

            if (upper == "UP" || upper == "DOWN" || upper == "(UP)" || upper == "(DOWN)") return "방향";
            if (upper.StartsWith("R", StringComparison.OrdinalIgnoreCase)) return "R값";
            if (text.IndexOf("°", StringComparison.Ordinal) >= 0 || text.IndexOf("도", StringComparison.Ordinal) >= 0) return "각도";

            decimal number;
            if (Decimal.TryParse(text.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return "치수";
            return "문자";
        }

        private void RefreshSelectionPanel()
        {
            if (editor == null)
            {
                return;
            }

            CadShapeEditElement selected = editor.SelectedElement;
            suppressUiEvents = true;

            try
            {
                if (selected == null)
                {
                    lblSelectionType.Text = "선택 없음";
                    lblSelectionId.Text = "";
                    txtSelectedText.Text = "";
                    txtSelectedText.Enabled = false;
                    btnUpdateText.Enabled = false;
                    numRotation.Value = 0;
                    numRotation.Enabled = false;
                }
                else
                {
                    lblSelectionType.Text = GetElementTypeName(selected.Type);
                    lblSelectionId.Text = selected.Type == "TEXT" ? selected.TextId : "";
                    txtSelectedText.Enabled = selected.Type == "TEXT";
                    btnUpdateText.Enabled = selected.Type == "TEXT";
                    txtSelectedText.Text = selected.Type == "TEXT" ? selected.Text : "";
                    numRotation.Enabled = selected.Type == "TEXT";
                    decimal rotation = (decimal)Math.Max(-360D, Math.Min(360D, selected.Rotation));
                    numRotation.Value = rotation;
                }
            }
            finally
            {
                suppressUiEvents = false;
            }
        }

        private string GetElementTypeName(string type)
        {
            if (type == "LINE") return "선";
            if (type == "TEXT") return "문자";
            if (type == "ARC") return "원호";
            if (type == "CIRCLE") return "원";
            return type == null ? "" : type;
        }

        private void RefreshStatistics()
        {
            if (editor == null || editor.Document == null)
            {
                return;
            }

            int lineCount = 0;
            int arcCount = 0;
            int circleCount = 0;
            int textCount = 0;
            int i;

            for (i = 0; i < editor.Document.Elements.Count; i++)
            {
                CadShapeEditElement element = editor.Document.Elements[i];
                if (element == null) continue;
                if (element.Type == "LINE") lineCount++;
                else if (element.Type == "ARC") arcCount++;
                else if (element.Type == "CIRCLE") circleCount++;
                else if (element.Type == "TEXT") textCount++;
            }

            lblStatistics.Text = "선 " + lineCount.ToString(CultureInfo.InvariantCulture)
                + "개   원호 " + arcCount.ToString(CultureInfo.InvariantCulture)
                + "개   원 " + circleCount.ToString(CultureInfo.InvariantCulture)
                + "개\r\n문자·치수 " + textCount.ToString(CultureInfo.InvariantCulture)
                + "개\r\n원본: " + (cadShapeJsonPath == "" ? "신규 수동 작성" : Path.GetFileName(cadShapeJsonPath));
        }

        private void UpdateToolbarState()
        {
            if (editor == null)
            {
                return;
            }

            SetModeButtonStyle(btnSelectMode, editor.Mode == CadShapeEditorMode.Select);
            SetModeButtonStyle(btnLineMode, editor.Mode == CadShapeEditorMode.AddLine);
            SetModeButtonStyle(btnTextMode, editor.Mode == CadShapeEditorMode.AddText);
            btnUndo.Enabled = editor.CanUndo;
            btnRedo.Enabled = editor.CanRedo;
            btnDelete.Enabled = editor.SelectedElement != null;
            btnSplit.Enabled = editor.SelectedElement != null && editor.SelectedElement.Type == "LINE";
            lblMode.Text = editor.Mode == CadShapeEditorMode.Select
                ? "현재: 선택·이동"
                : editor.Mode == CadShapeEditorMode.AddLine ? "현재: 연속 선 그리기" : "현재: 문자 추가";
        }

        private void SetModeButtonStyle(Button button, bool active)
        {
            if (button == null) return;
            button.BackColor = active ? Color.FromArgb(18, 103, 206) : Color.White;
            button.ForeColor = active ? Color.White : Color.FromArgb(35, 43, 57);
            button.FlatAppearance.BorderColor = active ? Color.FromArgb(18, 103, 206) : Color.FromArgb(197, 203, 213);
        }

        private void Editor_SelectionChanged(object sender, EventArgs e)
        {
            RefreshSelectionPanel();
            UpdateToolbarState();

            CadShapeEditElement selected = editor.SelectedElement;
            if (selected != null && selected.Type == "TEXT")
            {
                SelectTextGridRow(selected.TextId);
            }
        }

        private void Editor_DocumentChanged(object sender, EventArgs e)
        {
            if (!textGridCommitInProgress)
            {
                RefreshTextGrid();
            }

            RefreshSelectionPanel();
            RefreshStatistics();
            UpdateToolbarState();
            lblStatus.Text = "수정 내용은 아직 원본 CAD JSON에 덮어쓰지 않았습니다. ‘수정 적용’을 누르면 별도 편집 JSON으로 저장됩니다.";
        }

        private void Editor_ModeChanged(object sender, EventArgs e)
        {
            UpdateToolbarState();
        }

        private void Editor_TextEditRequested(object sender, EventArgs e)
        {
            CadShapeEditElement selected = editor == null ? null : editor.SelectedElement;

            if (selected == null || selected.Type != "TEXT")
            {
                return;
            }

            SelectTextGridRow(selected.TextId);
            RefreshSelectionPanel();
            txtSelectedText.Focus();
            txtSelectedText.SelectAll();
            lblStatus.Text = "문자값을 입력한 뒤 Enter 또는 ‘값 적용’을 누르세요.";
        }

        private void SelectTextGridRow(string textId)
        {
            if (textGrid == null || textId == null || textId.Trim() == "")
            {
                return;
            }

            suppressUiEvents = true;
            try
            {
                int i;
                for (i = 0; i < textGrid.Rows.Count; i++)
                {
                    object tag = textGrid.Rows[i].Tag;
                    if (tag != null && tag.ToString().Equals(textId, StringComparison.OrdinalIgnoreCase))
                    {
                        textGrid.ClearSelection();
                        textGrid.Rows[i].Selected = true;
                        if (textGrid.Rows[i].Cells.Count > 1)
                        {
                            textGrid.CurrentCell = textGrid.Rows[i].Cells[1];
                        }
                        break;
                    }
                }
            }
            finally
            {
                suppressUiEvents = false;
            }
        }

        private void TextGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (suppressUiEvents || e.RowIndex < 0 || e.ColumnIndex != 1)
            {
                return;
            }

            DataGridViewRow row = textGrid.Rows[e.RowIndex];
            string id = row.Tag == null ? "" : row.Tag.ToString();
            object value = row.Cells[e.ColumnIndex].Value;
            string textValue = value == null ? "" : value.ToString();

            textGridCommitInProgress = true;

            try
            {
                editor.SetTextValue(id, textValue);
                row.Cells[2].Value = ClassifyText(textValue);
            }
            finally
            {
                textGridCommitInProgress = false;
            }
        }

        private void TextGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1)
            {
                return;
            }

            textGrid.CurrentCell = textGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            textGrid.BeginEdit(true);
        }

        private void TextGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (suppressUiEvents)
            {
                return;
            }

            string id = GetSelectedTextGridId();
            if (id != "")
            {
                editor.SelectTextElement(id);
            }
        }

        private void BtnUpdateText_Click(object sender, EventArgs e)
        {
            if (!txtSelectedText.Enabled)
            {
                return;
            }

            editor.SetSelectedText(txtSelectedText.Text);
            txtSelectedText.Focus();
            txtSelectedText.SelectAll();
        }

        private void TxtSelectedText_Leave(object sender, EventArgs e)
        {
            if (!suppressUiEvents && txtSelectedText.Enabled)
            {
                editor.SetSelectedText(txtSelectedText.Text);
            }
        }

        private void TxtSelectedText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && txtSelectedText.Enabled)
            {
                editor.SetSelectedText(txtSelectedText.Text);
                editor.Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void NumRotation_ValueChanged(object sender, EventArgs e)
        {
            if (!suppressUiEvents && numRotation.Enabled)
            {
                editor.SetSelectedRotation((double)numRotation.Value);
            }
        }

        private void BtnSelectMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.Select;
            editor.Focus();
        }

        private void BtnLineMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddLine;
            editor.Focus();
        }

        private void BtnTextMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddText;
            editor.Focus();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            editor.DeleteSelected();
            editor.Focus();
        }

        private void BtnSplit_Click(object sender, EventArgs e)
        {
            editor.SplitSelectedLine();
            editor.Focus();
        }

        private void BtnUndo_Click(object sender, EventArgs e)
        {
            editor.Undo();
            editor.Focus();
        }

        private void BtnRedo_Click(object sender, EventArgs e)
        {
            editor.Redo();
            editor.Focus();
        }

        private void BtnHorizontal_Click(object sender, EventArgs e)
        {
            editor.AlignSelectedHorizontal();
            editor.Focus();
        }

        private void BtnVertical_Click(object sender, EventArgs e)
        {
            editor.AlignSelectedVertical();
            editor.Focus();
        }

        private void BtnFit_Click(object sender, EventArgs e)
        {
            editor.FitToScreen();
            editor.Focus();
        }

        private void BtnZoomIn_Click(object sender, EventArgs e)
        {
            editor.ZoomIn();
            editor.Focus();
        }

        private void BtnZoomOut_Click(object sender, EventArgs e)
        {
            editor.ZoomOut();
            editor.Focus();
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                isManualDocument
                    ? "현재 수정사항을 모두 취소하고 편집창을 처음 열었을 때의 형상으로 복원하시겠습니까?"
                    : "현재 수정사항을 모두 취소하고 CAD에서 처음 추출한 원본 형상으로 복원하시겠습니까?",
                isManualDocument ? "초기 형상 복원" : "CAD 원본 복원",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                editor.RestoreOriginal();
                lblStatus.Text = isManualDocument
                    ? "편집 시작 당시의 형상으로 복원했습니다. 적용 전까지 저장되지 않습니다."
                    : "CAD 원본 형상으로 복원했습니다. 적용 전까지 저장되지 않습니다.";
            }
        }

        private void ChkSnap_CheckedChanged(object sender, EventArgs e)
        {
            editor.SnapEnabled = chkSnap.Checked;
            editor.Focus();
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (editor.Document.CountGeometryElements() <= 0)
            {
                MessageBox.Show("철근 형상선이 없습니다. 선 추가 도구로 형상을 그린 후 적용해주세요.", "철근 형상 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                CadShapeEditDocument resultDocument = editor.Document.Clone();
                string outputPath = CadShapeEditDocument.BuildEditablePath(cadShapeJsonPath);

                if (!isManualDocument && cadShapeJsonPath != "")
                {
                    // 편집 JSON과 같은 폴더에 CAD 최초 원본의 동반 파일을 보관합니다.
                    // 편집 JSON에는 파일명만 기록하여 프로젝트 폴더가 이동되어도 원본 복원이 가능합니다.
                    string rawCopyPath = CadShapeEditDocument.BuildRawCopyPath(outputPath);

                    // 최초 AutoCAD JSON의 바이트를 그대로 복사하여 메타데이터와 좌표 정밀도까지 보존합니다.
                    // 원본 파일을 찾을 수 없는 예외 상황에서만 메모리 모델을 CAD_RAW JSON으로 저장합니다.
                    if (rawSourceJsonPath != "" && File.Exists(rawSourceJsonPath))
                    {
                        if (!IsSameFullPath(rawSourceJsonPath, rawCopyPath))
                        {
                            File.Copy(rawSourceJsonPath, rawCopyPath, true);
                        }
                    }
                    else
                    {
                        CadShapeEditDocument rawCopy = rawDocument.Clone();
                        rawCopy.Source = "CAD_RAW";
                        rawCopy.OriginalSourcePath = "";
                        rawCopy.Save(rawCopyPath);
                    }

                    resultDocument.OriginalSourcePath = Path.GetFileName(rawCopyPath);
                    resultDocument.Source = "OVIA_EDIT";
                }
                else
                {
                    resultDocument.OriginalSourcePath = "";
                    resultDocument.Source = "OVIA_MANUAL";
                }

                resultDocument.Save(outputPath);

                SelectedCadShapeJsonPath = outputPath;
                SelectedShapeSource = isManualDocument ? "MANUAL" : "CAD";
                SelectedShape = CreateCadImportedShape(outputPath, SelectedShapeSource);
                // 레거시 호출부와의 호환을 위해 벡터 편집 결과는 이 분기로 전달합니다.
                SelectedCadShapeOriginal = true;
                SelectedDimensionText = BuildSelectedDimensionText(resultDocument);
                SelectedTotalLength = CalculateNumericTextTotal(resultDocument);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("철근 형상 저장 중 오류가 발생했습니다.\r\n" + ex.Message, "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private bool IsSameFullPath(string pathA, string pathB)
        {
            if (pathA == null || pathB == null || pathA.Trim() == "" || pathB.Trim() == "")
            {
                return false;
            }

            try
            {
                string a = Path.GetFullPath(pathA).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string b = Path.GetFullPath(pathB).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return a.Equals(b, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private RebarShapeInfo CreateCadImportedShape(string jsonPath, string shapeSource)
        {
            bool manual = shapeSource != null && shapeSource.Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
            RebarShapeInfo shape = new RebarShapeInfo();
            shape.ShapeNo = -1000;
            shape.ShapeCode = manual ? "MANUAL" : "CAD";
            shape.ShapeName = manual ? "직접 작성 철근 형상" : "CAD 철근 형상";
            shape.Category = manual ? "MANUAL" : "CAD";
            shape.SourceImagePath = jsonPath;
            shape.VectorStatus = "CAD_IMPORTED";
            shape.ApproveStatus = manual ? "MANUAL_EDITED" : "CAD_EDITED";
            shape.FieldsText = "";
            shape.OptionText = manual ? "MANUAL" : "CAD";
            shape.IsUserSelectable = true;
            return shape;
        }

        private string BuildSelectedDimensionText(CadShapeEditDocument document)
        {
            StringBuilder sb = new StringBuilder();
            List<CadShapeEditElement> texts = document.GetTextElements();
            int i;

            for (i = 0; i < texts.Count; i++)
            {
                if (texts[i].TextId == null || texts[i].TextId.Trim() == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                sb.Append(texts[i].TextId.Trim().ToUpperInvariant());
                sb.Append("=");
                sb.Append(texts[i].Text == null ? "" : texts[i].Text.Trim());
            }

            return sb.ToString();
        }

        private decimal CalculateNumericTextTotal(CadShapeEditDocument document)
        {
            decimal total = 0M;
            List<CadShapeEditElement> texts = document.GetTextElements();
            int i;

            for (i = 0; i < texts.Count; i++)
            {
                string value = texts[i].Text == null ? "" : texts[i].Text.Trim().Replace(",", "");
                decimal number;
                if (Decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                {
                    total += number;
                }
            }

            return total;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
