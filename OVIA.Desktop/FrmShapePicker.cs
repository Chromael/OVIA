using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class FrmShapePicker : Form
    {
        private readonly RebarShapeRepository repository;
        private readonly List<RebarShapeInfo> allShapes;
        private readonly RebarShapeRenderer renderer;
        private readonly CadShapeRenderer cadRenderer;
        private readonly string cadShapeJsonPath;
        private readonly bool hasCadShapeOption;
        private readonly Dictionary<string, TextBox> dimensionBoxes;
        private readonly Dictionary<string, Label> dimensionLabels;
        private readonly Dictionary<int, Dictionary<string, string>> dimensionValueCache;

        private RebarShapeInfo currentEditingShape;
        private int lastSelectedIndex;
        private bool isApplyingShapeFields;
        private string initialSelectedRawValue;
        private bool initialSelectionApplied;

        private TextBox txtSearch;
        private Label lblShapeCodeValue;
        private ShapeGridControl shapeGrid;
        private RebarShapePreviewControl preview;
        private Label lblInfo;
        private Label lblTotalLength;
        private CheckBox chkTotalFixed;
        private CheckBox chkCoupler;
        private CheckBox chkRound;
        private CheckBox chkUpDown;
        private CheckBox chkSleeve;
        private TextBox txtPartCount;
        private Button btnQuery;
        private Button btnSelect;
        private Button btnCancel;

        public RebarShapeInfo SelectedShape { get; private set; }
        public bool SelectedCadShapeOriginal { get; private set; }
        public string SelectedDimensionText { get; private set; }
        public decimal SelectedTotalLength { get; private set; }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue)
            : this(repository, currentValue, "")
        {
        }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue, string currentDimensionText)
            : this(repository, currentValue, currentDimensionText, "")
        {
        }

        public FrmShapePicker(RebarShapeRepository repository, string currentValue, string currentDimensionText, string cadShapeJsonPath)
        {
            this.repository = repository == null ? RebarShapeRepository.CreateDefault() : repository;
            this.allShapes = this.repository.GetUserSelectableShapes();
            this.cadShapeJsonPath = cadShapeJsonPath == null ? "" : cadShapeJsonPath.Trim();
            this.hasCadShapeOption = this.cadShapeJsonPath != "" && File.Exists(this.cadShapeJsonPath);
            renderer = new RebarShapeRenderer();
            cadRenderer = new CadShapeRenderer();

            if (this.hasCadShapeOption)
            {
                this.allShapes.Insert(0, CreateCadImportedShape(this.cadShapeJsonPath));
            }

            dimensionBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
            dimensionLabels = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
            dimensionValueCache = new Dictionary<int, Dictionary<string, string>>();

            currentEditingShape = null;
            lastSelectedIndex = -1;
            isApplyingShapeFields = false;
            initialSelectedRawValue = currentValue == null ? "" : currentValue.Trim();
            initialSelectionApplied = false;

            SelectedShape = null;
            SelectedCadShapeOriginal = false;
            SelectedDimensionText = "";
            SelectedTotalLength = 0M;

            string safeCurrentValue = currentValue == null ? "" : currentValue.Trim();

            if (this.hasCadShapeOption)
            {
                // CAD 원본 형상이 있는 행을 다시 수정할 때도 CAD 원본 항목과 이미지 없음 항목은 항상 보여야 합니다.
                // 현재 행이 OVIA 형상코드로 교체된 상태라면 currentDimensionText는 OVIA 형상 입력값입니다.
                // 이 값을 CAD 원본 형상 입력란에 재사용하면 CAD 원본값이 오염되므로,
                // OVIA 형상 선택 상태에서는 CAD JSON 안의 원본 치수값을 우선 사용합니다.
                RebarShapeInfo currentManualShape = this.repository.FindByRawValue(safeCurrentValue);
                bool currentIsManualOviaShape = currentManualShape != null && currentManualShape.ShapeNo > 0;
                PreloadCadDimensionValues(currentDimensionText, currentIsManualOviaShape);

                if (safeCurrentValue != "")
                {
                    PreloadDimensionValues(safeCurrentValue, currentDimensionText);
                }

                safeCurrentValue = "";
            }
            else
            {
                PreloadDimensionValues(safeCurrentValue, currentDimensionText);
            }

            BuildUI();
            txtSearch.Text = safeCurrentValue;
            lblShapeCodeValue.Text = "";
            ApplyFilter();
        }


        private RebarShapeInfo CreateCadImportedShape(string jsonPath)
        {
            RebarShapeInfo shape = new RebarShapeInfo();
            shape.ShapeNo = -1000;
            shape.ShapeCode = "CAD";
            shape.ShapeName = "CAD에서 불러온 형상";
            shape.Category = "CAD";
            shape.SourceImagePath = jsonPath;
            shape.VectorStatus = "CAD_IMPORTED";
            shape.ApproveStatus = "CAD_CAPTURED";
            shape.FieldsText = BuildCadFieldText(jsonPath);
            shape.OptionText = "CAD";
            shape.IsUserSelectable = true;
            return shape;
        }

        private bool IsCadImportedShape(RebarShapeInfo shape)
        {
            return shape != null
                && shape.VectorStatus != null
                && shape.VectorStatus.Equals("CAD_IMPORTED", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildCadFieldText(string jsonPath)
        {
            int count = CountCadTextElements(jsonPath);
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };

            if (count <= 0)
            {
                count = 1;
            }

            if (count > keys.Length)
            {
                count = keys.Length;
            }

            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < count; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append("|");
                }

                sb.Append(keys[i]);
            }

            return sb.ToString();
        }

        private int CountCadTextElements(string jsonPath)
        {
            if (jsonPath == null || jsonPath.Trim() == "" || !File.Exists(jsonPath))
            {
                return 0;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                MatchCollection matches = Regex.Matches(json, "\\{[^\\{\\}]*\\\"type\\\"[^\\{\\}]*\\}", RegexOptions.Singleline);
                int count = 0;
                int i;

                for (i = 0; i < matches.Count; i++)
                {
                    string item = matches[i].Value;
                    string type = GetJsonString(item, "type").ToUpperInvariant();
                    string text = GetJsonString(item, "text").Trim();

                    if (type == "TEXT" && text != "")
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private void PreloadCadDimensionValues(string currentDimensionText, bool forceCadJsonValues)
        {
            RebarShapeInfo shape = null;

            if (allShapes != null && allShapes.Count > 0 && IsCadImportedShape(allShapes[0]))
            {
                shape = allShapes[0];
            }

            if (shape == null)
            {
                return;
            }

            Dictionary<string, string> values;

            if (forceCadJsonValues)
            {
                values = BuildDimensionValuesFromCadJson(shape.SourceImagePath, shape.FieldsText);
            }
            else
            {
                values = ParseDimensionText(currentDimensionText);

                if (values.Count == 0)
                {
                    values = BuildDimensionValuesFromCadJson(shape.SourceImagePath, shape.FieldsText);
                }
            }

            if (values.Count > 0)
            {
                dimensionValueCache[shape.ShapeNo] = values;
            }
        }

        private Dictionary<string, string> BuildDimensionValuesFromCadJson(string jsonPath, string fieldsText)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> keys = SplitFieldKeys(fieldsText);

            if (jsonPath == null || jsonPath.Trim() == "" || !File.Exists(jsonPath) || keys.Count == 0)
            {
                return values;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                MatchCollection matches = Regex.Matches(json, "\\{[^\\{\\}]*\\\"type\\\"[^\\{\\}]*\\}", RegexOptions.Singleline);
                int fallbackTextIndex = 0;
                int i;

                for (i = 0; i < matches.Count; i++)
                {
                    string item = matches[i].Value;
                    string type = GetJsonString(item, "type").ToUpperInvariant();
                    string textValue = GetJsonString(item, "text").Trim();

                    if (type != "TEXT" || textValue == "")
                    {
                        continue;
                    }

                    string textId = GetJsonString(item, "textId").Trim().ToUpperInvariant();
                    int mappedIndex = ParseCadTextIdIndex(textId);

                    if (mappedIndex < 0)
                    {
                        mappedIndex = fallbackTextIndex;
                    }

                    fallbackTextIndex++;

                    if (mappedIndex < 0 || mappedIndex >= keys.Count)
                    {
                        continue;
                    }

                    values[keys[mappedIndex]] = textValue;
                }
            }
            catch
            {
            }

            return values;
        }

        private int ParseCadTextIdIndex(string textId)
        {
            if (textId == null || textId.Length < 2 || Char.ToUpperInvariant(textId[0]) != 'T')
            {
                return -1;
            }

            int number;

            if (!Int32.TryParse(textId.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return -1;
            }

            return number <= 0 ? -1 : number - 1;
        }

        private List<string> SplitFieldKeys(string fieldsText)
        {
            List<string> list = new List<string>();

            if (fieldsText == null || fieldsText.Trim() == "")
            {
                return list;
            }

            string[] parts = fieldsText.Replace(",", "|").Replace("/", "|").Split('|');
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string key = RebarShapeInfo.NormalizeFieldKey(parts[i]);

                if (key != "" && !ContainsField(list, key))
                {
                    list.Add(key);
                }
            }

            return list;
        }

        private string GetJsonString(string json, string key)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\\\"])*)\\\"", RegexOptions.Singleline);

            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }


        private void PreloadDimensionValues(string currentValue, string currentDimensionText)
        {
            if (currentDimensionText == null || currentDimensionText.Trim() == "")
            {
                return;
            }

            RebarShapeInfo shape = repository.FindByRawValue(currentValue);

            if (shape == null || shape.ShapeNo <= 0)
            {
                return;
            }

            Dictionary<string, string> parsed = ParseDimensionText(currentDimensionText);

            if (parsed.Count == 0)
            {
                return;
            }

            if (dimensionValueCache.ContainsKey(shape.ShapeNo))
            {
                dimensionValueCache[shape.ShapeNo] = parsed;
            }
            else
            {
                dimensionValueCache.Add(shape.ShapeNo, parsed);
            }
        }

        private Dictionary<string, string> ParseDimensionText(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (text == null)
            {
                return values;
            }

            string[] parts = text.Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim();

                if (part == "")
                {
                    continue;
                }

                int eq = part.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string value = part.Substring(eq + 1).Trim();

                if (key == "" || value == "")
                {
                    continue;
                }

                values[key] = value;
            }

            return values;
        }

        private void BuildUI()
        {
            Text = "철근 형상 선택";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(1040, 760);
            BackColor = Color.FromArgb(246, 248, 252);
            Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);

            BuildLeftPanel();
            BuildCenterPanel();
            BuildBottomButtons();
        }

        private void BuildLeftPanel()
        {
            Panel left = new Panel();
            left.Location = new Point(12, 12);
            left.Size = new Size(260, 690);
            left.BackColor = Color.FromArgb(240, 242, 246);
            left.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(left);

            Label codeLabel = new Label();
            codeLabel.Text = "형상코드";
            codeLabel.Font = OviaFluentTheme.FontKorean(14F, FontStyle.Bold);
            codeLabel.Location = new Point(14, 14);
            codeLabel.Size = new Size(112, 30);
            left.Controls.Add(codeLabel);

            lblShapeCodeValue = new Label();
            lblShapeCodeValue.Font = OviaFluentTheme.FontKorean(14F, FontStyle.Bold);
            lblShapeCodeValue.TextAlign = ContentAlignment.MiddleLeft;
            lblShapeCodeValue.Location = new Point(142, 14);
            lblShapeCodeValue.Size = new Size(92, 30);
            lblShapeCodeValue.BackColor = Color.Transparent;
            lblShapeCodeValue.BorderStyle = BorderStyle.None;
            lblShapeCodeValue.Text = "";
            left.Controls.Add(lblShapeCodeValue);

            preview = new RebarShapePreviewControl();
            preview.Location = new Point(12, 62);
            preview.Size = new Size(230, 84);
            preview.BackColor = Color.White;
            left.Controls.Add(preview);

            chkTotalFixed = new CheckBox();
            chkTotalFixed.Text = "총길이 고정";
            chkTotalFixed.Location = new Point(18, 156);
            chkTotalFixed.Size = new Size(120, 24);
            chkTotalFixed.CheckedChanged += DimensionBox_TextChanged;
            left.Controls.Add(chkTotalFixed);

            Label totalTitle = new Label();
            totalTitle.Text = "합계 길이";
            totalTitle.ForeColor = Color.Firebrick;
            totalTitle.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Bold);
            totalTitle.Location = new Point(18, 190);
            totalTitle.Size = new Size(70, 22);
            left.Controls.Add(totalTitle);

            lblTotalLength = new Label();
            lblTotalLength.Text = "0";
            lblTotalLength.TextAlign = ContentAlignment.MiddleRight;
            lblTotalLength.BackColor = Color.White;
            lblTotalLength.BorderStyle = BorderStyle.FixedSingle;
            lblTotalLength.Font = OviaFluentTheme.FontKorean(12F, FontStyle.Bold);
            lblTotalLength.Location = new Point(92, 186);
            lblTotalLength.Size = new Size(150, 30);
            left.Controls.Add(lblTotalLength);

            string[] fields = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int y = 230;
            int i;

            for (i = 0; i < fields.Length; i++)
            {
                string key = fields[i];

                Label label = new Label();
                label.Text = key + " 값";
                label.Location = new Point(22, y + 4);
                label.Size = new Size(58, 22);
                left.Controls.Add(label);

                TextBox box = new TextBox();
                box.Location = new Point(82, y);
                box.Size = new Size(160, 25);
                box.Enabled = false;
                box.Text = "";
                box.Tag = key;
                box.TextChanged += DimensionBox_TextChanged;
                left.Controls.Add(box);

                dimensionLabels.Add(key, label);
                dimensionBoxes.Add(key, box);
                y += 31;
            }

            Label note = new Label();
            note.Text = "CAD 원본 형상은 숫자·알파벳·기호·각도 표시를 화면 읽기 순서대로 수정하며, 입력 즉시 미리보기에 반영됩니다.";
            note.ForeColor = Color.FromArgb(92, 98, 110);
            note.Location = new Point(18, 620);
            note.Size = new Size(224, 52);
            left.Controls.Add(note);
        }

        private void BuildCenterPanel()
        {
            Label searchLabel = new Label();
            searchLabel.Text = "형상번호 / 이름 / 분류 검색";
            searchLabel.Location = new Point(290, 15);
            searchLabel.Size = new Size(180, 22);
            Controls.Add(searchLabel);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(290, 40);
            txtSearch.Size = new Size(260, 25);
            txtSearch.TextChanged += TxtSearch_TextChanged;
            Controls.Add(txtSearch);

            Label partLabel = new Label();
            partLabel.Text = "부위갯수";
            partLabel.Location = new Point(565, 15);
            partLabel.Size = new Size(70, 22);
            Controls.Add(partLabel);

            txtPartCount = new TextBox();
            txtPartCount.Location = new Point(565, 40);
            txtPartCount.Size = new Size(70, 25);
            txtPartCount.BackColor = Color.Yellow;
            txtPartCount.TextAlign = HorizontalAlignment.Center;
            txtPartCount.TextChanged += TxtFilter_TextChanged;
            Controls.Add(txtPartCount);

            btnQuery = new Button();
            btnQuery.Text = "조회";
            btnQuery.Location = new Point(645, 38);
            btnQuery.Size = new Size(74, 28);
            btnQuery.Click += BtnQuery_Click;
            Controls.Add(btnQuery);

            chkCoupler = new CheckBox();
            chkCoupler.Text = "커플러";
            chkCoupler.Location = new Point(290, 76);
            chkCoupler.Size = new Size(74, 24);
            chkCoupler.CheckedChanged += TxtFilter_TextChanged;
            Controls.Add(chkCoupler);

            chkRound = new CheckBox();
            chkRound.Text = "라운드(R=)";
            chkRound.Location = new Point(370, 76);
            chkRound.Size = new Size(104, 24);
            chkRound.CheckedChanged += TxtFilter_TextChanged;
            Controls.Add(chkRound);

            chkUpDown = new CheckBox();
            chkUpDown.Text = "Up/Down";
            chkUpDown.Location = new Point(480, 76);
            chkUpDown.Size = new Size(86, 24);
            chkUpDown.CheckedChanged += TxtFilter_TextChanged;
            Controls.Add(chkUpDown);

            chkSleeve = new CheckBox();
            chkSleeve.Text = "SLEEVE";
            chkSleeve.Location = new Point(575, 76);
            chkSleeve.Size = new Size(86, 24);
            chkSleeve.CheckedChanged += TxtFilter_TextChanged;
            Controls.Add(chkSleeve);

            shapeGrid = new ShapeGridControl(renderer);
            shapeGrid.Location = new Point(290, 108);
            shapeGrid.Size = new Size(732, 594);
            shapeGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            shapeGrid.SelectedIndexChanged += ShapeGrid_SelectedIndexChanged;
            shapeGrid.ShapeDoubleClick += ShapeGrid_ShapeDoubleClick;
            Controls.Add(shapeGrid);
        }

        private void BuildRightPanel()
        {
            // 우측 형상정보/사용기준 박스는 대표님 요청에 따라 제거했습니다.
        }

        private void BuildBottomButtons()
        {
            btnSelect = new Button();
            btnSelect.Text = "선택";
            btnSelect.Location = new Point(852, 712);
            btnSelect.Size = new Size(78, 32);
            btnSelect.Click += BtnSelect_Click;
            Controls.Add(btnSelect);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(944, 712);
            btnCancel.Size = new Size(78, 32);
            btnCancel.Click += BtnCancel_Click;
            Controls.Add(btnCancel);

            AcceptButton = btnSelect;
            CancelButton = btnCancel;
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void TxtFilter_TextChanged(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void BtnQuery_Click(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void DimensionBox_TextChanged(object sender, EventArgs e)
        {
            if (isApplyingShapeFields)
            {
                return;
            }

            StoreCurrentDimensionValues();
            RecalculateTotalLength();

            if (preview != null)
            {
                preview.DimensionText = BuildSelectedDimensionText();
            }
        }

        private void ShapeGrid_SelectedIndexChanged(object sender, EventArgs e)
        {
            RebarShapeInfo shape = shapeGrid == null ? null : shapeGrid.SelectedShape;

            if (shape == null)
            {
                return;
            }

            StoreCurrentDimensionValues();

            currentEditingShape = shape;
            lastSelectedIndex = shapeGrid.SelectedIndex;
            preview.Shape = shape;
            preview.RawText = IsCadImportedShape(shape) ? "" : txtSearch.Text;

            lblShapeCodeValue.Text = shape.ShapeNo <= 0 ? "" : shape.DisplayCode;
            ApplyShapeFields(shape);
            preview.DimensionText = BuildSelectedDimensionText();
            SetInfoText(shape);
        }

        private string GetSafeShapeListTitle(RebarShapeInfo shape)
        {
            if (shape == null)
            {
                return "";
            }

            if (IsCadImportedShape(shape))
            {
                return "CAD에서 불러온 형상";
            }

            if (shape.ShapeNo <= 0)
            {
                return "이미지 없음";
            }

            return "형상 " + shape.DisplayCode;
        }

        private void ShapeGrid_ShapeDoubleClick(object sender, EventArgs e)
        {
            SelectCurrentShape();
        }

        private void BtnSelect_Click(object sender, EventArgs e)
        {
            SelectCurrentShape();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ApplyFilter()
        {
            string keyword = txtSearch == null ? "" : txtSearch.Text.Trim();
            string normalized = Normalize(keyword);
            int partCount = ParseInt(txtPartCount == null ? "" : txtPartCount.Text.Trim());

            StoreCurrentDimensionValues();
            List<RebarShapeInfo> filteredShapes = new List<RebarShapeInfo>();
            lastSelectedIndex = -1;

            int i;

            for (i = 0; i < allShapes.Count; i++)
            {
                RebarShapeInfo shape = allShapes[i];

                if (shape == null)
                {
                    continue;
                }

                bool alwaysVisibleTopItem = IsCadImportedShape(shape) || shape.ShapeNo <= 0;

                if (!alwaysVisibleTopItem && !MatchesKeyword(shape, normalized))
                {
                    continue;
                }

                if (!alwaysVisibleTopItem && partCount > 0 && shape.GetLengthFieldCount() != partCount)
                {
                    continue;
                }

                if (!alwaysVisibleTopItem && chkCoupler.Checked && !shape.HasOption("COUPLER"))
                {
                    continue;
                }

                if (!alwaysVisibleTopItem && chkRound.Checked && !shape.HasOption("ROUND"))
                {
                    continue;
                }

                if (!alwaysVisibleTopItem && chkUpDown.Checked && !shape.HasOption("UPDOWN"))
                {
                    continue;
                }

                if (!alwaysVisibleTopItem && chkSleeve.Checked && !shape.HasOption("SLEEVE"))
                {
                    continue;
                }

                filteredShapes.Add(shape);
            }

            if (shapeGrid != null)
            {
                shapeGrid.SetShapes(filteredShapes);
            }

            if (filteredShapes.Count > 0)
            {
                int selectIndex;

                if (!initialSelectionApplied && initialSelectedRawValue != null && initialSelectedRawValue.Trim() != "")
                {
                    selectIndex = FindBestSelectIndexByRawValue(filteredShapes, initialSelectedRawValue);
                    initialSelectionApplied = true;
                }
                else
                {
                    selectIndex = FindBestSelectIndex(filteredShapes, normalized);
                }

                if (shapeGrid != null)
                {
                    shapeGrid.SelectedIndex = selectIndex;
                }
            }
            else
            {
                currentEditingShape = null;
                lastSelectedIndex = -1;
                preview.Shape = null;
                preview.RawText = keyword;

                if (lblInfo != null)
                {
                    lblInfo.Text = "검색 결과가 없습니다. 관리자에게 형상 등록/승인을 요청하세요.\r\n전체 등록 형상 수: " + allShapes.Count.ToString();
                }

                ApplyShapeFields(null);
            }
        }

        private int FindBestSelectIndexByRawValue(List<RebarShapeInfo> shapes, string rawValue)
        {
            if (shapes == null || shapes.Count == 0)
            {
                return -1;
            }

            string normalized = Normalize(rawValue);

            if (normalized == "")
            {
                return 0;
            }

            int i;

            for (i = 0; i < shapes.Count; i++)
            {
                RebarShapeInfo shape = shapes[i];

                if (shape == null || IsCadImportedShape(shape))
                {
                    continue;
                }

                if (Normalize(shape.DisplayCode).Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || Normalize(shape.ShapeCode).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private int FindBestSelectIndex(List<RebarShapeInfo> shapes, string normalized)
        {
            if (shapes == null || shapes.Count == 0)
            {
                return -1;
            }

            if (normalized == "")
            {
                return 0;
            }

            int i;

            for (i = 0; i < shapes.Count; i++)
            {
                RebarShapeInfo shape = shapes[i];

                if (shape == null)
                {
                    continue;
                }

                if (Normalize(shape.DisplayCode).Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || Normalize(shape.ShapeCode).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private bool MatchesKeyword(RebarShapeInfo shape, string normalized)
        {
            if (normalized == "")
            {
                return true;
            }

            return Normalize(shape.DisplayCode).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                || Normalize(shape.ShapeCode).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                || Normalize(shape.ShapeName).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                || Normalize(shape.Category).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                || Normalize(shape.FieldsText).IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyShapeFields(RebarShapeInfo shape)
        {
            string[] allKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            List<string> activeKeys = shape == null ? new List<string>() : shape.GetFieldKeys();
            bool cadImported = IsCadImportedShape(shape);
            int i;

            isApplyingShapeFields = true;

            try
            {
                for (i = 0; i < allKeys.Length; i++)
                {
                    string key = allKeys[i];
                    bool enabled = ContainsField(activeKeys, key);

                    TextBox box;
                    Label label;

                    if (dimensionBoxes.TryGetValue(key, out box))
                    {
                        box.Enabled = enabled;
                        box.ReadOnly = false;
                        box.BackColor = enabled ? Color.White : Color.FromArgb(226, 226, 226);
                        box.Text = enabled ? GetCachedDimensionValue(shape, key) : "";
                    }

                    if (dimensionLabels.TryGetValue(key, out label))
                    {
                        label.Text = cadImported && enabled
                            ? (i + 1).ToString(CultureInfo.InvariantCulture) + "번 값"
                            : key + " 값";
                        label.ForeColor = enabled ? Color.FromArgb(35, 35, 35) : Color.FromArgb(150, 150, 150);
                    }
                }
            }
            finally
            {
                isApplyingShapeFields = false;
            }

            RecalculateTotalLength();

            if (preview != null)
            {
                preview.DimensionText = BuildSelectedDimensionText();
            }
        }

        private void StoreCurrentDimensionValues()
        {
            if (currentEditingShape == null)
            {
                return;
            }

            Dictionary<string, string> values;

            if (!dimensionValueCache.TryGetValue(currentEditingShape.ShapeNo, out values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dimensionValueCache.Add(currentEditingShape.ShapeNo, values);
            }

            foreach (KeyValuePair<string, TextBox> pair in dimensionBoxes)
            {
                if (pair.Value == null || !pair.Value.Enabled)
                {
                    continue;
                }

                values[pair.Key] = pair.Value.Text == null ? "" : pair.Value.Text.Trim();
            }
        }

        private string GetCachedDimensionValue(RebarShapeInfo shape, string key)
        {
            if (shape == null || key == null)
            {
                return "";
            }

            Dictionary<string, string> values;
            string value;

            if (dimensionValueCache.TryGetValue(shape.ShapeNo, out values) && values.TryGetValue(key, out value))
            {
                return value == null ? "" : value;
            }

            return "";
        }

        private bool ContainsField(List<string> fields, string key)
        {
            int i;

            for (i = 0; i < fields.Count; i++)
            {
                if (fields[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RecalculateTotalLength()
        {
            string[] lengthKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H" };
            decimal total = 0M;
            int i;

            for (i = 0; i < lengthKeys.Length; i++)
            {
                TextBox box;

                if (dimensionBoxes.TryGetValue(lengthKeys[i], out box) && box.Enabled)
                {
                    total += ParseDecimal(box.Text);
                }
            }

            SelectedTotalLength = total;
            lblTotalLength.Text = total.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string BuildSelectedDimensionText()
        {
            StringBuilder sb = new StringBuilder();
            string[] allKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < allKeys.Length; i++)
            {
                string key = allKeys[i];
                TextBox box;

                if (dimensionBoxes.TryGetValue(key, out box) && box.Enabled)
                {
                    string value = box.Text == null ? "" : box.Text.Trim();

                    if (value != "")
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append("; ");
                        }

                        sb.Append(key);
                        sb.Append("=");
                        sb.Append(value);
                    }
                }
            }

            if (chkTotalFixed.Checked)
            {
                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                sb.Append("총길이고정=Y");
            }

            return sb.ToString();
        }

        private void SelectCurrentShape()
        {
            RebarShapeInfo shape = shapeGrid == null ? null : shapeGrid.SelectedShape;

            if (shape == null)
            {
                MessageBox.Show("선택할 형상이 없습니다.", "OVIA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StoreCurrentDimensionValues();
            List<string> missingKeys = GetMissingRequiredKeys(shape);

            if (missingKeys.Count > 0)
            {
                string firstKey = missingKeys[0];
                TextBox firstBox;

                if (dimensionBoxes.TryGetValue(firstKey, out firstBox))
                {
                    firstBox.Focus();
                    firstBox.SelectAll();
                }

                MessageBox.Show("다음 항목의 값을 입력해주세요.\r\n- " + String.Join(", ", missingKeys.ToArray()), "입력값 누락", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedShape = shape;
            SelectedCadShapeOriginal = IsCadImportedShape(shape);
            SelectedDimensionText = BuildSelectedDimensionText();
            RecalculateTotalLength();
            DialogResult = DialogResult.OK;
            Close();
        }

        private List<string> GetMissingRequiredKeys(RebarShapeInfo shape)
        {
            List<string> list = new List<string>();

            if (shape == null)
            {
                return list;
            }

            if (shape.ShapeNo <= 0 && !IsCadImportedShape(shape))
            {
                return list;
            }

            List<string> keys = shape.GetFieldKeys();
            int i;

            for (i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                TextBox box;

                if (!dimensionBoxes.TryGetValue(key, out box) || !box.Enabled)
                {
                    continue;
                }

                string value = box.Text == null ? "" : box.Text.Trim();

                if (value == "")
                {
                    list.Add(key);
                }
            }

            return list;
        }

        private void SetInfoText(RebarShapeInfo shape)
        {
            if (lblInfo == null)
            {
                return;
            }

            if (shape == null)
            {
                lblInfo.Text = "";
                return;
            }

            if (IsCadImportedShape(shape))
            {
                lblInfo.Text = "형상구분: CAD에서 불러온 형상\r\n"
                    + "표시명: CAD 원본 문자 수정\r\n"
                    + "입력필드: " + shape.GetFieldKeys().Count.ToString(CultureInfo.InvariantCulture) + "개 (위→아래, 같은 줄은 좌→우)\r\n"
                    + "사용기준: 선과 위치는 유지하고 숫자·문자·기호만 수정합니다.";
                return;
            }

            lblInfo.Text = "형상번호: " + (shape.ShapeNo <= 0 ? "이미지 없음" : shape.DisplayCode) + "\r\n"
                + "표시명: " + (shape.ShapeNo <= 0 ? "이미지 없음" : "형상 " + shape.DisplayCode) + "\r\n"
                + "입력필드: " + (shape.FieldsText == null || shape.FieldsText.Trim() == "" ? "미정의" : shape.FieldsText.Replace("|", ", ")) + "\r\n"
                + "부위갯수: " + shape.GetLengthFieldCount().ToString() + "\r\n"
                + "상태: " + shape.ApproveStatus + "\r\n"
                + "자료: " + (shape.VectorStatus == null || shape.VectorStatus.Trim() == "" ? "OVIA" : shape.VectorStatus) + "\r\n"
                + "비고: 명칭/분류/옵션은 관리자 검수 후 표시";
        }

        private int ParseInt(string value)
        {
            int result;

            if (Int32.TryParse(value, out result))
            {
                return result;
            }

            return 0;
        }

        private decimal ParseDecimal(string value)
        {
            if (value == null)
            {
                return 0M;
            }

            value = value.Trim().Replace(",", "");

            if (value == "")
            {
                return 0M;
            }

            decimal result;

            if (Decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            if (Decimal.TryParse(value, out result))
            {
                return result;
            }

            return 0M;
        }

        private string Normalize(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Trim().Replace(" ", "").ToUpperInvariant();
        }
    }

    internal class ShapeGridControl : Panel
    {
        private readonly List<RebarShapeInfo> shapes;
        private readonly RebarShapeRenderer renderer;
        private readonly CadShapeRenderer cadRenderer;
        private int selectedIndex;
        private int hoveredIndex;

        public event EventHandler SelectedIndexChanged;
        public event EventHandler ShapeDoubleClick;

        public ShapeGridControl(RebarShapeRenderer renderer)
        {
            this.renderer = renderer == null ? new RebarShapeRenderer() : renderer;
            cadRenderer = new CadShapeRenderer();
            shapes = new List<RebarShapeInfo>();
            selectedIndex = -1;
            hoveredIndex = -1;

            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
            AutoScroll = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int newValue = value;

                if (newValue < -1)
                {
                    newValue = -1;
                }

                if (newValue >= shapes.Count)
                {
                    newValue = shapes.Count - 1;
                }

                if (selectedIndex == newValue)
                {
                    return;
                }

                selectedIndex = newValue;
                EnsureSelectedVisible();
                Invalidate();
                OnSelectedIndexChanged();
            }
        }

        public RebarShapeInfo SelectedShape
        {
            get
            {
                if (selectedIndex < 0 || selectedIndex >= shapes.Count)
                {
                    return null;
                }

                return shapes[selectedIndex];
            }
        }

        public void SetShapes(List<RebarShapeInfo> source)
        {
            shapes.Clear();

            if (source != null)
            {
                shapes.AddRange(source);
            }

            selectedIndex = -1;
            hoveredIndex = -1;
            UpdateScrollSize();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);

            if (hoveredIndex != index)
            {
                hoveredIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveredIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            int index = HitTest(e.Location);

            if (index >= 0 && index < shapes.Count)
            {
                SelectedIndex = index;
                Focus();
            }
            else
            {
                // 빈 여백 클릭 시 선택/입력값을 초기화하지 않습니다.
                Focus();
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);

            if (SelectedShape != null && ShapeDoubleClick != null)
            {
                ShapeDoubleClick(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            int columns = 3;
            int cellWidth = GetCellWidth();
            int cellHeight = GetCellHeight();
            int i;

            using (Pen borderPen = new Pen(Color.FromArgb(220, 224, 232)))
            using (SolidBrush selectedBrush = new SolidBrush(Color.FromArgb(229, 243, 255)))
            using (SolidBrush hoverBrush = new SolidBrush(Color.FromArgb(246, 249, 253)))
            using (SolidBrush normalBrush = new SolidBrush(Color.White))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(32, 38, 58)))
            using (SolidBrush selectedTextBrush = new SolidBrush(Color.FromArgb(12, 66, 120)))
            using (Pen selectedPen = new Pen(Color.FromArgb(0, 122, 204), 2F))
            {
                for (i = 0; i < shapes.Count; i++)
                {
                    int row = i / columns;
                    int col = i % columns;
                    Rectangle rect = new Rectangle(col * cellWidth + 8, row * cellHeight + 8, cellWidth - 14, cellHeight - 12);

                    if (rect.Bottom < -AutoScrollPosition.Y || rect.Top > -AutoScrollPosition.Y + Height)
                    {
                        continue;
                    }

                    bool selected = i == selectedIndex;
                    bool hovered = i == hoveredIndex;
                    RebarShapeInfo shape = shapes[i];

                    e.Graphics.FillRectangle(selected ? selectedBrush : (hovered ? hoverBrush : normalBrush), rect);
                    e.Graphics.DrawRectangle(selected ? selectedPen : borderPen, rect);

                    Rectangle previewRect = new Rectangle(rect.Left + 12, rect.Top + 8, rect.Width - 24, 68);

                    if (IsCadImportedShape(shape))
                    {
                        cadRenderer.DrawCadShape(e.Graphics, previewRect, shape.SourceImagePath, selected);
                    }
                    else
                    {
                        renderer.DrawShape(e.Graphics, previewRect, shape, "", selected);
                    }

                    string label = GetShapeLabel(shape);
                    SizeF size = e.Graphics.MeasureString(label, Font);
                    float x = rect.Left + (rect.Width - size.Width) / 2F;
                    float y = rect.Top + 82;
                    e.Graphics.DrawString(label, Font, selected ? selectedTextBrush : textBrush, x, y);
                }
            }
        }

        private int HitTest(Point location)
        {
            int columns = 3;
            int cellWidth = GetCellWidth();
            int cellHeight = GetCellHeight();
            int x = location.X - AutoScrollPosition.X;
            int y = location.Y - AutoScrollPosition.Y;

            if (x < 0 || y < 0)
            {
                return -1;
            }

            int col = x / cellWidth;
            int row = y / cellHeight;

            if (col < 0 || col >= columns)
            {
                return -1;
            }

            int index = row * columns + col;

            if (index < 0 || index >= shapes.Count)
            {
                return -1;
            }

            Rectangle rect = new Rectangle(col * cellWidth + 8, row * cellHeight + 8, cellWidth - 14, cellHeight - 12);

            if (!rect.Contains(x, y))
            {
                return -1;
            }

            return index;
        }

        private string GetShapeLabel(RebarShapeInfo shape)
        {
            if (IsCadImportedShape(shape))
            {
                return "CAD에서 불러온 형상";
            }

            if (shape == null || shape.ShapeNo <= 0)
            {
                return "이미지 없음";
            }

            return "형상 " + shape.DisplayCode;
        }


        private bool IsCadImportedShape(RebarShapeInfo shape)
        {
            return shape != null
                && shape.VectorStatus != null
                && shape.VectorStatus.Equals("CAD_IMPORTED", StringComparison.OrdinalIgnoreCase)
                && shape.SourceImagePath != null
                && shape.SourceImagePath.Trim() != "";
        }

        private int GetCellWidth()
        {
            int width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;

            if (width < 360)
            {
                width = ClientSize.Width - 4;
            }

            return Math.Max(180, width / 3);
        }

        private int GetCellHeight()
        {
            return 116;
        }

        private void UpdateScrollSize()
        {
            int rows = (int)Math.Ceiling(shapes.Count / 3.0);
            AutoScrollMinSize = new Size(0, rows * GetCellHeight() + 16);
        }

        private void EnsureSelectedVisible()
        {
            if (selectedIndex < 0)
            {
                return;
            }

            int row = selectedIndex / 3;
            int top = row * GetCellHeight();
            int bottom = top + GetCellHeight();
            int viewTop = -AutoScrollPosition.Y;
            int viewBottom = viewTop + ClientSize.Height;

            if (top < viewTop)
            {
                AutoScrollPosition = new Point(0, top);
            }
            else if (bottom > viewBottom)
            {
                AutoScrollPosition = new Point(0, bottom - ClientSize.Height + 8);
            }
        }

        private void OnSelectedIndexChanged()
        {
            if (SelectedIndexChanged != null)
            {
                SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
    }

}
