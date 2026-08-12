using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private Label lblSelectionType;
        private Label lblSelectionId;
        private Label lblStatistics;
        private TextBox txtSelectedText;
        private NumericUpDown numRotation;
        private Button btnSelectMode;
        private Button btnLineMode;
        private Button btnRectangleMode;
        private Button btnCircleMode;
        private Button btnAngleMode;
        private Button btnScrewMode;
        private Button btnTextMode;
        private Button btnUndo;
        private Button btnRedo;
        private Button btnDelete;
        private Button btnSplit;
        private Button btnUpdateText;
        private Button btnApply;
        private Button btnCancel;
        private Button btnHelp;
        private bool suppressUiEvents;
        private bool textGridCommitInProgress;
        private OviaWindowCaptionTheme captionTheme;
        private ToolTip toolTip;

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

            CadShapeEditDocument loadedDocument = CadShapeDisplayNormalizer.CreateEditableDocument(
                CadShapeEditDocument.Load(this.cadShapeJsonPath)
            );
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
                trueOriginalDocument = CadShapeDisplayNormalizer.CreateEditableDocument(
                    CadShapeEditDocument.Load(resolvedRawSourcePath)
                );
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
            BackColor = OviaFluentTheme.AppBackground;
            Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);
            KeyPreview = true;
            toolTip = new ToolTip();

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 90;
            toolbar.BackColor = Color.FromArgb(248, 249, 252);
            toolbar.Padding = new Padding(12, 6, 12, 6);
            Controls.Add(toolbar);

            TableLayoutPanel toolbarRows = new TableLayoutPanel();
            toolbarRows.Dock = DockStyle.Fill;
            toolbarRows.ColumnCount = 1;
            toolbarRows.RowCount = 2;
            toolbarRows.Margin = new Padding(0);
            toolbarRows.Padding = new Padding(0);
            toolbarRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbarRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            toolbarRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            toolbar.Controls.Add(toolbarRows);

            FlowLayoutPanel primaryToolFlow = CreateToolbarFlowPanel();
            FlowLayoutPanel secondaryToolFlow = CreateToolbarFlowPanel();
            toolbarRows.Controls.Add(primaryToolFlow, 0, 0);
            toolbarRows.Controls.Add(secondaryToolFlow, 0, 1);

            btnSelectMode = CreateToolbarButton("선택·이동", "\uE762", 104, BtnSelectMode_Click, "요소를 선택하거나 이동합니다. CAD에서 추출된 곡선·원형은 구성 점이 아니라 객체 전체가 선택됩니다.");
            btnLineMode = CreateToolbarButton("선 추가", "\uE710", 88, BtnLineMode_Click, "연속 선을 추가합니다. 기존 선 끝점 가까이에서는 자동으로 연결됩니다.");
            btnRectangleMode = CreateToolbarButton("사각형 추가", "\uF12A", 104, BtnRectangleMode_Click, "첫 모서리와 반대편 모서리를 지정하여 사각형을 추가합니다. 생성 후 길이·폭을 각각 조절하거나 비례 확대·축소·회전할 수 있습니다.");
            btnCircleMode = CreateToolbarButton("원 추가", "\uEA3A", 88, BtnCircleMode_Click, "중심점과 반지름 지점을 차례로 클릭하여 원을 추가합니다. 생성 후 가로·세로를 각각 조절하여 타원으로 수정할 수 있습니다.");
            btnAngleMode = CreateToolbarButton("각도 추가", "\uF0B4", 94, BtnAngleMode_Click, "중심점과 시작 방향을 지정한 뒤 마우스를 원하는 방향으로 돌려 최대 270°까지 각도를 추가합니다.");
            btnScrewMode = CreateToolbarButton("나사 추가", "\uEE6F", 94, BtnScrewMode_Click, "시작점과 끝점을 지정하여 나사 형상을 추가합니다. 전체 객체의 길이·폭을 각각 조절하거나 비례 확대·축소·회전할 수 있습니다.");
            btnTextMode = CreateToolbarButton("문자 추가", "\uE8D2", 94, BtnTextMode_Click, "문자 또는 치수값을 추가하고 즉시 값을 입력합니다.");
            btnDelete = CreateToolbarButton("선택 삭제", "\uE74D", 98, BtnDelete_Click, "선택한 요소를 삭제합니다. Delete 키도 사용할 수 있습니다.");
            btnSplit = CreateToolbarButton("선 분할", "\uE8C6", 88, BtnSplit_Click, "선택한 선을 가운데 지점에서 두 개로 분할합니다.");
            btnUndo = CreateToolbarButton("실행 취소", "\uE7A7", 98, BtnUndo_Click, "마지막 수정 작업을 취소합니다. Ctrl+Z");
            btnRedo = CreateToolbarButton("다시 실행", "\uE7A6", 98, BtnRedo_Click, "취소한 작업을 다시 실행합니다. Ctrl+Y");
            Button btnHorizontal = CreateToolbarButton("수평 맞춤", "\uE8E4", 98, BtnHorizontal_Click, "선택한 선 또는 문자를 수평으로 맞춥니다.");
            Button btnVertical = CreateToolbarButton("수직 맞춤", "\uE8E3", 98, BtnVertical_Click, "선택한 선 또는 문자를 수직으로 맞춥니다.");
            Button btnFit = CreateToolbarButton("화면 맞춤", "\uE9A6", 98, BtnFit_Click, "형상을 기본 50% 비율로 중앙에 맞춥니다.");
            Button btnZoomIn = CreateToolbarButton("확대", "\uE8A3", 74, BtnZoomIn_Click, "형상과 문자·치수값을 함께 확대합니다.");
            Button btnZoomOut = CreateToolbarButton("축소", "\uE71F", 74, BtnZoomOut_Click, "형상과 문자·치수값을 함께 축소합니다.");
            Button btnRestore = CreateToolbarButton(isManualDocument ? "초기 형상 복원" : "CAD 원본 복원", "\uE777", 132, BtnRestore_Click, "현재 편집 내용을 취소하고 최초 형상으로 복원합니다.");

            primaryToolFlow.Controls.Add(btnSelectMode);
            primaryToolFlow.Controls.Add(btnLineMode);
            primaryToolFlow.Controls.Add(btnRectangleMode);
            primaryToolFlow.Controls.Add(btnCircleMode);
            primaryToolFlow.Controls.Add(btnAngleMode);
            primaryToolFlow.Controls.Add(btnScrewMode);
            primaryToolFlow.Controls.Add(btnTextMode);
            primaryToolFlow.Controls.Add(CreateToolbarSeparator());
            primaryToolFlow.Controls.Add(btnDelete);
            primaryToolFlow.Controls.Add(btnSplit);

            secondaryToolFlow.Controls.Add(btnUndo);
            secondaryToolFlow.Controls.Add(btnRedo);
            secondaryToolFlow.Controls.Add(CreateToolbarSeparator());
            secondaryToolFlow.Controls.Add(btnHorizontal);
            secondaryToolFlow.Controls.Add(btnVertical);
            secondaryToolFlow.Controls.Add(btnFit);
            secondaryToolFlow.Controls.Add(btnZoomIn);
            secondaryToolFlow.Controls.Add(btnZoomOut);
            secondaryToolFlow.Controls.Add(CreateToolbarSeparator());
            secondaryToolFlow.Controls.Add(btnRestore);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 62;
            bottom.BackColor = Color.White;
            bottom.Padding = new Padding(16, 12, 16, 12);
            Controls.Add(bottom);

            FlowLayoutPanel bottomLeft = new FlowLayoutPanel();
            bottomLeft.Dock = DockStyle.Left;
            bottomLeft.AutoSize = true;
            bottomLeft.WrapContents = false;
            bottomLeft.FlowDirection = FlowDirection.LeftToRight;
            bottomLeft.Margin = new Padding(0);
            bottomLeft.Padding = new Padding(0);
            bottom.Controls.Add(bottomLeft);

            btnHelp = new OVIA.Desktop.Controls.OviaButton();
            btnHelp.Text = "도움말";
            btnHelp.Margin = new Padding(0);
            btnHelp.Click += BtnHelp_Click;
            OviaFluentTheme.ApplyButton(btnHelp, OviaButtonRole.Neutral);
            OviaFluentTheme.FitButtonSize(btnHelp);
            bottomLeft.Controls.Add(btnHelp);

            FlowLayoutPanel bottomRight = new FlowLayoutPanel();
            bottomRight.Dock = DockStyle.Right;
            bottomRight.AutoSize = true;
            bottomRight.WrapContents = false;
            bottomRight.FlowDirection = FlowDirection.LeftToRight;
            bottomRight.Margin = new Padding(0);
            bottomRight.Padding = new Padding(0);
            bottom.Controls.Add(bottomRight);

            btnCancel = new OVIA.Desktop.Controls.OviaButton();
            btnCancel.Text = "취소";
            btnCancel.Margin = new Padding(0, 0, 10, 0);
            btnCancel.Click += BtnCancel_Click;
            OviaFluentTheme.ApplyButton(btnCancel, OviaButtonRole.Neutral);
            OviaFluentTheme.FitButtonSize(btnCancel);
            bottomRight.Controls.Add(btnCancel);

            btnApply = new OVIA.Desktop.Controls.OviaButton();
            btnApply.Text = "수정 적용";
            btnApply.Margin = new Padding(0);
            btnApply.Click += BtnApply_Click;
            OviaFluentTheme.ApplyButton(btnApply, OviaButtonRole.Primary);
            OviaFluentTheme.FitButtonSize(btnApply);
            bottomRight.Controls.Add(btnApply);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel2;
            split.IsSplitterFixed = true;
            split.SplitterWidth = 1;
            split.BorderStyle = BorderStyle.None;
            split.Panel1.Padding = new Padding(0);
            split.Panel2.Padding = new Padding(0);
            split.BackColor = Color.FromArgb(250, 251, 253);
            Controls.Add(split);
            split.SendToBack();
            toolbar.BringToFront();
            bottom.BringToFront();

            Panel editorFrame = new Panel();
            editorFrame.Dock = DockStyle.Fill;
            editorFrame.BackColor = Color.FromArgb(250, 251, 253);
            editorFrame.BorderStyle = BorderStyle.None;
            split.Panel1.Controls.Add(editorFrame);

            editor = new CadShapeEditorControl();
            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(0);
            editor.SelectionChanged += Editor_SelectionChanged;
            editor.DocumentChanged += Editor_DocumentChanged;
            editor.ModeChanged += Editor_ModeChanged;
            editor.TextEditRequested += Editor_TextEditRequested;
            editorFrame.Controls.Add(editor);

            BuildRightPanel(split.Panel2);
            split.Panel2Collapsed = true;
            captionTheme = OviaWindowCaptionTheme.Attach(this);

            // Enter/Esc는 편집 캔버스의 선·원 작성 종료와 문자값 입력에 사용하므로
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
            selectedHelp.Text = "문자·치수는 더블클릭하여 수정하고, 위쪽 핸들로 회전하며 오른쪽 아래 십자 핸들로 확대·축소합니다. 선은 회전 핸들, 원은 십자 핸들로 편집합니다.";
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

        private FlowLayoutPanel CreateToolbarFlowPanel()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.AutoScroll = false;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Margin = new Padding(0);
            flow.Padding = new Padding(0, 1, 0, 1);
            return flow;
        }

        private Button CreateToolbarButton(string text, string iconGlyph, int width, EventHandler clickHandler, string helpText)
        {
            Button button = new ShapeToolbarButton();
            button.Text = text;
            button.Tag = iconGlyph == null ? "" : iconGlyph;
            button.Height = 34;
            button.Margin = new Padding(3, 1, 3, 1);
            button.AutoSize = false;
            button.AutoEllipsis = false;
            button.UseCompatibleTextRendering = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(197, 203, 213);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 248, 251);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(35, 43, 57);
            button.Font = new Font("맑은 고딕", 9F, FontStyle.Regular, GraphicsUnit.Point);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(8, 0, 8, 0);

            Size textSize = TextRenderer.MeasureText(
                text == null ? "" : text,
                button.Font,
                new Size(int.MaxValue, button.Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            );
            const int iconWidth = 18;
            const int iconTextGap = 7;
            const int horizontalPadding = 20;
            int balancedWidth = iconWidth + iconTextGap + textSize.Width + horizontalPadding;
            button.Width = Math.Max(Math.Max(72, width), balancedWidth);

            ApplyToolbarButtonIcon(button, button.ForeColor);
            button.Click += clickHandler;

            if (toolTip != null && helpText != null && helpText.Trim() != "")
            {
                toolTip.SetToolTip(button, helpText);
            }

            return button;
        }

        private void ApplyToolbarButtonIcon(Button button, Color color)
        {
            if (button == null)
            {
                return;
            }

            Image oldImage = button.Image;
            string glyph = button.Tag == null ? "" : button.Tag.ToString();
            button.Image = CreateToolbarIcon(glyph, color);

            if (oldImage != null)
            {
                oldImage.Dispose();
            }
        }

        private Image CreateToolbarIcon(string glyph, Color color)
        {
            Bitmap bitmap = new Bitmap(18, 18);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font iconFont = glyph == "\u2220"
                ? new Font("Segoe UI Symbol", 11.5F, FontStyle.Bold, GraphicsUnit.Point)
                : OviaIconFont.Create(11.5F, FontStyle.Regular))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.Clear(Color.Transparent);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                SizeF size = graphics.MeasureString(glyph == null ? "" : glyph, iconFont);
                graphics.DrawString(
                    glyph == null ? "" : glyph,
                    iconFont,
                    brush,
                    (18F - size.Width) / 2F,
                    (18F - size.Height) / 2F
                );
            }

            return bitmap;
        }

        private void BtnHelp_Click(object sender, EventArgs e)
        {
            ShowEditorHelp();
        }

        private void ShowEditorHelp()
        {
            using (Form helpForm = new Form())
            {
                helpForm.Text = "철근 형상 편집 도움말";
                helpForm.StartPosition = FormStartPosition.CenterParent;
                helpForm.FormBorderStyle = FormBorderStyle.Sizable;
                helpForm.MinimizeBox = false;
                helpForm.MaximizeBox = false;
                helpForm.MinimumSize = new Size(620, 500);
                helpForm.ClientSize = new Size(700, 610);
                helpForm.BackColor = OviaFluentTheme.AppBackground;
                helpForm.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);

                Panel scrollPanel = new Panel();
                scrollPanel.Dock = DockStyle.Fill;
                scrollPanel.AutoScroll = true;
                scrollPanel.Padding = new Padding(20, 18, 20, 24);
                scrollPanel.BackColor = Color.White;
                helpForm.Controls.Add(scrollPanel);

                TableLayoutPanel helpLayout = new TableLayoutPanel();
                helpLayout.Dock = DockStyle.Top;
                helpLayout.AutoSize = true;
                helpLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                helpLayout.ColumnCount = 1;
                helpLayout.RowCount = 1;
                helpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                helpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                helpLayout.Padding = new Padding(0, 0, 0, 12);
                scrollPanel.Controls.Add(helpLayout);

                Label intro = new Label();
                intro.AutoSize = true;
                intro.MaximumSize = new Size(620, 0);
                intro.Font = OviaFluentTheme.FontKorean(11F, FontStyle.Bold);
                intro.ForeColor = Color.FromArgb(32, 41, 56);
                intro.Text = "철근 형상 확인·수정 기능 안내";
                intro.Margin = new Padding(0, 0, 0, 14);
                helpLayout.Controls.Add(intro, 0, 0);

                AddHelpSection(helpLayout, "선택과 이동",
                    "요소를 클릭하면 선택됩니다. 빈 공간을 드래그하면 사각영역 안의 선·원·문자·치수를 한꺼번에 선택할 수 있습니다. Ctrl+A는 전체 선택이며, 선택된 요소를 드래그하면 함께 이동합니다. 사각형·나사·새로 추가한 원/타원은 구성 선 중 하나를 클릭해도 객체 전체가 선택됩니다.");
                AddHelpSection(helpLayout, "선·사각형·원·각도·나사·문자 추가",
                    "선 추가는 시작점과 끝점을 순서대로 지정합니다. 사각형 추가는 첫 모서리와 반대편 모서리를 지정합니다. 원 추가는 중심점과 반지름 위치를 지정합니다. 각도 추가는 중심점과 시작 방향을 지정한 뒤 마우스를 원하는 방향으로 돌려 최대 270°까지 만든 다음 끝 위치를 클릭합니다. 나사 추가는 나사의 시작점과 끝점을 지정합니다. 문자 추가 후 위치를 클릭하면 바로 값을 입력할 수 있습니다. 선 끝점 가까이에서는 자동으로 연결됩니다.");
                AddHelpSection(helpLayout, "문자와 수치 수정",
                    "문자 또는 치수값을 더블클릭하거나 선택 후 F2·Enter를 누르면 값을 수정할 수 있습니다. Enter는 적용, Esc는 현재 입력 취소입니다.");
                AddHelpSection(helpLayout, "회전과 크기 조절",
                    "선 끝점 바깥의 회전 핸들을 드래그하면 자유 각도로 회전합니다. 새로 추가한 원은 하나의 객체로 선택되며 가로·세로 핸들로 폭과 높이를 따로 조절해 타원으로 만들 수 있고, 우하단 핸들로 비례 확대·축소할 수 있습니다. 각도 원호는 시작·끝·반지름·회전 핸들로 벌어진 각도, 크기, 방향을 조절할 수 있습니다. 사각형과 나사도 객체 전체 선택 후 가로·세로 핸들로 길이와 폭을 각각 조절하고, 우하단 핸들로 비례 확대·축소하며, 위쪽 원형 핸들로 회전합니다. CAD에서 추출된 곡선·원형은 작은 선분 점이 아니라 하나의 객체로 선택되어 전체 이동·삭제할 수 있습니다. 문자와 수치는 위쪽 회전 핸들로 회전하고 오른쪽 아래 십자 크기 핸들로 개별 확대·축소할 수 있습니다.");
                AddHelpSection(helpLayout, "캔버스 보기",
                    "가운데 마우스 버튼을 누른 채 드래그하면 캔버스를 이동합니다. Ctrl+마우스 휠 또는 상단 확대·축소 버튼으로 형상과 문자 크기를 함께 조절합니다. 화면 맞춤은 형상을 중앙에 배치합니다.");
                AddHelpSection(helpLayout, "작업 취소와 삭제",
                    "Ctrl+C는 선택 객체 복사, Ctrl+V는 붙여넣기입니다. 드래그로 여러 객체를 선택한 경우에도 한 번에 복사·붙여넣기할 수 있습니다. Ctrl+Z는 실행 취소, Ctrl+Y는 다시 실행입니다. Delete는 선택 요소 삭제, Esc 또는 마우스 우클릭은 현재 추가 작업이나 선택 작업을 종료합니다.");
                AddHelpSection(helpLayout, "수정 적용",
                    "수정 적용을 누르면 편집한 형상과 문자·치수값이 BarList 철근형상 셀에 반영됩니다. 취소를 누르면 현재 창에서 변경한 내용은 적용하지 않습니다.");

                using (OviaWindowCaptionTheme helpCaptionTheme = OviaWindowCaptionTheme.Attach(helpForm))
                {
                    helpForm.ShowDialog(this);
                }
            }
        }

        private void AddHelpSection(TableLayoutPanel layout, string title, string description)
        {
            Panel section = new Panel();
            section.Dock = DockStyle.Top;
            section.AutoSize = true;
            section.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            section.Padding = new Padding(14, 12, 14, 12);
            section.Margin = new Padding(0, 0, 0, 10);
            section.BackColor = Color.FromArgb(247, 249, 252);
            section.BorderStyle = BorderStyle.FixedSingle;

            TableLayoutPanel sectionLayout = new TableLayoutPanel();
            sectionLayout.Dock = DockStyle.Top;
            sectionLayout.AutoSize = true;
            sectionLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sectionLayout.ColumnCount = 1;
            sectionLayout.RowCount = 2;
            sectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.Controls.Add(sectionLayout);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.Font = OviaFluentTheme.FontKorean(9.5F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(35, 43, 57);
            titleLabel.Text = title;
            titleLabel.Margin = new Padding(0, 0, 0, 6);
            sectionLayout.Controls.Add(titleLabel, 0, 0);

            Label descriptionLabel = new Label();
            descriptionLabel.AutoSize = true;
            descriptionLabel.MaximumSize = new Size(600, 0);
            descriptionLabel.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);
            descriptionLabel.ForeColor = Color.FromArgb(82, 91, 105);
            descriptionLabel.Text = description;
            descriptionLabel.Margin = new Padding(0);
            sectionLayout.Controls.Add(descriptionLabel, 0, 1);

            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(section, 0, layout.RowCount - 1);
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
            bool allowLegacySequenceOverrides = document.Version <= 2;
            int i;

            for (i = 0; i < texts.Count; i++)
            {
                string value;

                /*
                 * JSON v3 이상은 각 CAD 문자에 안정 ID(T1...Tn)가 있으므로 해당 ID로만 수정값을
                 * 적용해야 합니다. 추출 직후 CSV의 A/B/C 값은 형상원본 파싱용 레거시 값이며,
                 * 이를 텍스트 목록 순서로 덮어쓰면 BarList와 수정 팝업의 치수 위치·값이 달라집니다.
                 */
                if (texts[i].TextId != null
                    && texts[i].TextId.Trim() != ""
                    && values.TryGetValue(texts[i].TextId, out value))
                {
                    texts[i].Text = value;
                    texts[i].HasBounds = false;
                    continue;
                }

                if (allowLegacySequenceOverrides
                    && i < legacyKeys.Length
                    && values.TryGetValue(legacyKeys[i], out value))
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
                else if (editor.IsSingleManualObjectSelected)
                {
                    string kind = editor.SelectedObjectGroupKind;
                    lblSelectionType.Text = String.Equals(kind, "RECTANGLE", StringComparison.OrdinalIgnoreCase)
                        ? "사각형 객체"
                        : (String.Equals(kind, "SCREW", StringComparison.OrdinalIgnoreCase)
                            ? "나사 객체"
                            : (String.Equals(kind, "ELLIPSE", StringComparison.OrdinalIgnoreCase) ? "원·타원 객체" : "그룹 객체"));
                    lblSelectionId.Text = "하나의 객체로 선택됨";
                    txtSelectedText.Text = "";
                    txtSelectedText.Enabled = false;
                    btnUpdateText.Enabled = false;
                    numRotation.Value = 0;
                    numRotation.Enabled = false;
                }
                else if (editor.IsSingleCadCurveObjectSelected)
                {
                    lblSelectionType.Text = "곡선 객체";
                    lblSelectionId.Text = "CAD 원본 곡선";
                    txtSelectedText.Text = "";
                    txtSelectedText.Enabled = false;
                    btnUpdateText.Enabled = false;
                    numRotation.Value = 0;
                    numRotation.Enabled = false;
                }
                else if (editor.SelectedCount > 1)
                {
                    lblSelectionType.Text = editor.SelectedCount.ToString(CultureInfo.InvariantCulture) + "개 객체 선택";
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
            HashSet<string> ellipseGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < editor.Document.Elements.Count; i++)
            {
                CadShapeEditElement element = editor.Document.Elements[i];
                if (element == null) continue;

                if (element.Type == "LINE"
                    && String.Equals(element.ObjectGroupKind, "ELLIPSE", StringComparison.OrdinalIgnoreCase)
                    && element.ObjectGroupId != null
                    && element.ObjectGroupId.Trim() != "")
                {
                    if (ellipseGroups.Add(element.ObjectGroupId.Trim()))
                    {
                        circleCount++;
                    }
                    continue;
                }

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
            SetModeButtonStyle(btnRectangleMode, editor.Mode == CadShapeEditorMode.AddRectangle);
            SetModeButtonStyle(btnCircleMode, editor.Mode == CadShapeEditorMode.AddCircle);
            SetModeButtonStyle(btnAngleMode, editor.Mode == CadShapeEditorMode.AddAngle);
            SetModeButtonStyle(btnScrewMode, editor.Mode == CadShapeEditorMode.AddScrew);
            SetModeButtonStyle(btnTextMode, editor.Mode == CadShapeEditorMode.AddText);
            btnUndo.Enabled = editor.CanUndo;
            btnRedo.Enabled = editor.CanRedo;
            btnDelete.Enabled = editor.SelectedCount > 0;
            btnSplit.Enabled = editor.CanSplitSelectedLine;

        }

        private void SetModeButtonStyle(Button button, bool active)
        {
            if (button == null) return;
            button.BackColor = active ? OviaFluentTheme.Accent : Color.White;
            button.ForeColor = active ? Color.White : Color.FromArgb(35, 43, 57);
            button.FlatAppearance.BorderColor = active ? OviaFluentTheme.Accent : Color.FromArgb(197, 203, 213);
            button.FlatAppearance.MouseOverBackColor = active ? OviaFluentTheme.AccentHover : Color.FromArgb(246, 248, 251);
            ApplyToolbarButtonIcon(button, button.ForeColor);
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

        private void BtnRectangleMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddRectangle;
            editor.Focus();
        }

        private void BtnCircleMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddCircle;
            editor.Focus();
        }

        private void BtnAngleMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddAngle;
            editor.Focus();
        }

        private void BtnScrewMode_Click(object sender, EventArgs e)
        {
            editor.Mode = CadShapeEditorMode.AddScrew;
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
                }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            editor.CommitInlineTextEdit();

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

        private sealed class ShapeToolbarButton : Button
        {
            private const float ToolbarCornerRadius = 3F;
            private const int IconTextGap = 7;
            private bool mouseOver;
            private bool mouseDown;

            public ShapeToolbarButton()
            {
                Cursor = Cursors.Hand;
                SetStyle(
                    ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                    true
                );
                UseVisualStyleBackColor = false;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                mouseOver = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                mouseOver = false;
                mouseDown = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                base.OnMouseDown(mevent);

                if (mevent.Button == MouseButtons.Left)
                {
                    mouseDown = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                base.OnMouseUp(mevent);
                mouseDown = false;
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnBackColorChanged(EventArgs e)
            {
                base.OnBackColorChanged(e);
                Invalidate();
            }

            protected override void OnForeColorChanged(EventArgs e)
            {
                base.OnForeColorChanged(e);
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                // 네 모서리의 투명 영역을 부모 배경으로 직접 칠하므로 기본 사각 배경은 그리지 않습니다.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width <= 1 || Height <= 1)
                {
                    return;
                }

                Graphics graphics = e.Graphics;
                SmoothingMode oldSmoothing = graphics.SmoothingMode;
                PixelOffsetMode oldPixelOffset = graphics.PixelOffsetMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Color outsideColor = Parent == null ? SystemColors.Control : Parent.BackColor;
                graphics.Clear(outsideColor);

                Color fillColor = BackColor;

                if (!Enabled)
                {
                    fillColor = Color.FromArgb(246, 247, 249);
                }
                else if (mouseDown)
                {
                    fillColor = FlatAppearance.MouseDownBackColor.IsEmpty
                        ? ControlPaint.Dark(BackColor, 0.04F)
                        : FlatAppearance.MouseDownBackColor;
                }
                else if (mouseOver && !FlatAppearance.MouseOverBackColor.IsEmpty)
                {
                    fillColor = FlatAppearance.MouseOverBackColor;
                }

                Color borderColor = Enabled
                    ? FlatAppearance.BorderColor
                    : Color.FromArgb(218, 222, 228);
                Color textColor = Enabled ? ForeColor : Color.FromArgb(158, 163, 172);

                RectangleF bounds = new RectangleF(0.5F, 0.5F, Math.Max(1F, Width - 1F), Math.Max(1F, Height - 1F));

                using (GraphicsPath path = CreateRoundRectangle(bounds, ToolbarCornerRadius))
                using (SolidBrush fillBrush = new SolidBrush(fillColor))
                using (Pen borderPen = new Pen(borderColor, 1F))
                {
                    graphics.FillPath(fillBrush, path);
                    graphics.DrawPath(borderPen, path);
                }

                string buttonText = Text == null ? "" : Text;
                Image buttonImage = Image;
                TextFormatFlags flags = TextFormatFlags.NoPrefix
                    | TextFormatFlags.NoPadding
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.VerticalCenter;
                Size textSize = TextRenderer.MeasureText(
                    graphics,
                    buttonText,
                    Font,
                    new Size(Int32.MaxValue, Math.Max(1, Height)),
                    flags
                );
                int imageWidth = buttonImage == null ? 0 : buttonImage.Width;
                int imageHeight = buttonImage == null ? 0 : buttonImage.Height;
                int gap = imageWidth > 0 && buttonText != "" ? IconTextGap : 0;
                int contentWidth = imageWidth + gap + textSize.Width;
                int contentX = Math.Max(Padding.Left, (Width - contentWidth) / 2);

                if (buttonImage != null)
                {
                    int imageY = (Height - imageHeight) / 2;

                    if (Enabled)
                    {
                        graphics.DrawImage(buttonImage, contentX, imageY, imageWidth, imageHeight);
                    }
                    else
                    {
                        ControlPaint.DrawImageDisabled(graphics, buttonImage, contentX, imageY, outsideColor);
                    }

                    contentX += imageWidth + gap;
                }

                Rectangle textBounds = new Rectangle(
                    contentX,
                    0,
                    Math.Max(1, Width - contentX - Padding.Right),
                    Height
                );
                TextRenderer.DrawText(graphics, buttonText, Font, textBounds, textColor, flags);

                if (Focused && ShowFocusCues)
                {
                    Rectangle focusBounds = Rectangle.Inflate(ClientRectangle, -4, -4);
                    ControlPaint.DrawFocusRectangle(graphics, focusBounds, textColor, fillColor);
                }

                graphics.SmoothingMode = oldSmoothing;
                graphics.PixelOffsetMode = oldPixelOffset;
            }

            private static GraphicsPath CreateRoundRectangle(RectangleF rectangle, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float safeRadius = Math.Max(0F, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2F));

                if (safeRadius <= 0.01F)
                {
                    path.AddRectangle(rectangle);
                    path.CloseFigure();
                    return path;
                }

                float diameter = safeRadius * 2F;
                RectangleF arc = new RectangleF(rectangle.Left, rectangle.Top, diameter, diameter);
                path.AddArc(arc, 180F, 90F);
                arc.X = rectangle.Right - diameter;
                path.AddArc(arc, 270F, 90F);
                arc.Y = rectangle.Bottom - diameter;
                path.AddArc(arc, 0F, 90F);
                arc.X = rectangle.Left;
                path.AddArc(arc, 90F, 90F);
                path.CloseFigure();
                return path;
            }
        }
    }
}
