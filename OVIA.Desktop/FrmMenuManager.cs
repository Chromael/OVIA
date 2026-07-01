using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public interface IOviaWorkspaceHelpProvider
    {
        string WorkspaceHelpKey { get; }
        string WorkspaceHelpTitle { get; }
        string WorkspaceHelpText { get; }
    }

    public class FrmMenuManager : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private DataGridView grid;
        private Label lblStatus;
        private Button btnEditHelp;
        private Button btnSave;
        private Button btnReset;
        private Button btnClose;
        private List<OviaMenuSetting> rows = new List<OviaMenuSetting>();
        private bool isDirty;
        private bool isLoading;

        public FrmMenuManager(string companyId, string userId)
        {
            this.companyId = companyId == null ? string.Empty : companyId;
            this.userId = userId == null ? string.Empty : userId;
            this.canEdit = OviaSystemSettingsStore.IsSuperAdminUser(this.userId);

            BuildUI();
            LoadRowsToGrid(OviaMenuHelpStore.Load());
        }

        public string WorkspaceHelpKey { get { return "MENU_MANAGER"; } }
        public string WorkspaceHelpTitle { get { return "메뉴관리"; } }
        public string WorkspaceHelpText
        {
            get
            {
                return "OVIA 메뉴와 페이지별 도움말, 사용 여부, 최고관리자 전용 여부를 관리합니다. 권한 정보는 추후 ERP 사용자/권한과 양방향 연동될 예정입니다.";
            }
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
            MinimumSize = new Size(1060, 650);
            BackColor = SurfaceColor;
            FormClosing += FrmMenuManager_FormClosing;

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
                "메인  ›  환경설정  ›  메뉴관리",
                delegate { Close(); },
                delegate { Close(); },
                delegate { LoadRowsToGrid(OviaMenuHelpStore.Load()); },
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
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1180, 50);
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

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(32, 124);
            grid.Size = new Size(1116, 430);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            grid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Regular);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 244, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 34;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CellFormatting += Grid_CellFormatting;
            OviaFluentTheme.ApplyDataGrid(grid);

            DataGridViewTextBoxColumn levelCol = new DataGridViewTextBoxColumn();
            levelCol.Name = "Level";
            levelCol.HeaderText = "단계";
            levelCol.FillWeight = 42;
            levelCol.ReadOnly = true;
            grid.Columns.Add(levelCol);

            DataGridViewTextBoxColumn menuCol = new DataGridViewTextBoxColumn();
            menuCol.Name = "MenuName";
            menuCol.HeaderText = "메뉴 / 페이지";
            menuCol.FillWeight = 210;
            menuCol.ReadOnly = true;
            grid.Columns.Add(menuCol);

            DataGridViewTextBoxColumn keyCol = new DataGridViewTextBoxColumn();
            keyCol.Name = "Key";
            keyCol.HeaderText = "메뉴키";
            keyCol.FillWeight = 130;
            keyCol.ReadOnly = true;
            grid.Columns.Add(keyCol);

            DataGridViewCheckBoxColumn enabledCol = new DataGridViewCheckBoxColumn();
            enabledCol.Name = "Enabled";
            enabledCol.HeaderText = "사용";
            enabledCol.FillWeight = 50;
            enabledCol.ReadOnly = !canEdit;
            grid.Columns.Add(enabledCol);

            DataGridViewCheckBoxColumn adminCol = new DataGridViewCheckBoxColumn();
            adminCol.Name = "SuperAdminOnly";
            adminCol.HeaderText = "최고관리자";
            adminCol.FillWeight = 70;
            adminCol.ReadOnly = !canEdit;
            grid.Columns.Add(adminCol);

            DataGridViewTextBoxColumn helpCol = new DataGridViewTextBoxColumn();
            helpCol.Name = "HelpText";
            helpCol.HeaderText = "도움말";
            helpCol.FillWeight = 320;
            helpCol.ReadOnly = true;
            grid.Columns.Add(helpCol);

            DataGridViewButtonColumn editCol = new DataGridViewButtonColumn();
            editCol.Name = "EditHelp";
            editCol.HeaderText = "편집";
            editCol.Text = "도움말 입력";
            editCol.UseColumnTextForButtonValue = true;
            editCol.FillWeight = 90;
            grid.Columns.Add(editCol);

            grid.CellContentClick += Grid_CellContentClick;
            parent.Controls.Add(grid);
        }

        private void BuildButtons(Control parent)
        {
            btnEditHelp = CreateButton("선택 도움말 입력", 32, 580, 150);
            btnEditHelp.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnEditHelp.Enabled = canEdit;
            btnEditHelp.Click += delegate { EditSelectedHelp(); };
            parent.Controls.Add(btnEditHelp);

            btnReset = CreateButton("기본값 복원", 194, 580, 120);
            btnReset.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnReset.Enabled = canEdit;
            btnReset.Click += Reset_Click;
            parent.Controls.Add(btnReset);

            btnSave = CreateButton("저장하기", 902, 580, 120);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnSave.BackColor = OviaFluentTheme.Accent;
            btnSave.ForeColor = Color.White;
            btnSave.Enabled = canEdit;
            btnSave.Click += Save_Click;
            parent.Controls.Add(btnSave);

            btnClose = CreateButton("닫기", 1038, 580, 110);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnClose.Click += delegate { Close(); };
            parent.Controls.Add(btnClose);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.AutoSize = false;
            lblStatus.Location = new Point(32, 638);
            lblStatus.Size = new Size(1116, 42);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            parent.Controls.Add(lblStatus);
        }

        private Button CreateButton(string text, int x, int y, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 34);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = OviaFluentTheme.FontButton(8.7F, FontStyle.Bold);
            button.BackColor = Color.White;
            button.ForeColor = TextDark;
            button.FlatAppearance.BorderColor = OviaFluentTheme.ControlBorder;
            button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.AccentLight;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void LoadRowsToGrid(List<OviaMenuSetting> settings)
        {
            isLoading = true;
            rows = settings == null ? OviaMenuHelpStore.CreateDefaultSettings() : settings;
            grid.Rows.Clear();

            int i;
            for (i = 0; i < rows.Count; i++)
            {
                OviaMenuSetting row = rows[i];
                int index = grid.Rows.Add();
                grid.Rows[index].Cells["Level"].Value = row.Level + "차";
                grid.Rows[index].Cells["MenuName"].Value = Indent(row.Level) + row.MenuName;
                grid.Rows[index].Cells["Key"].Value = row.Key;
                grid.Rows[index].Cells["Enabled"].Value = row.Enabled;
                grid.Rows[index].Cells["SuperAdminOnly"].Value = row.SuperAdminOnly;
                grid.Rows[index].Cells["HelpText"].Value = Shorten(row.HelpText, 80);
                grid.Rows[index].Tag = row;
            }

            isDirty = false;
            isLoading = false;
            UpdateStatus(canEdit ? "메뉴별 사용 여부, 최고관리자 권한, 도움말을 관리합니다. 도움말은 하단 물음표 아이콘에서 표시됩니다." : "메뉴관리는 최고관리자만 수정할 수 있습니다.");
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

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid != null && grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            OviaMenuSetting row = grid.Rows[e.RowIndex].Tag as OviaMenuSetting;
            if (row == null)
            {
                return;
            }

            row.Enabled = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells["Enabled"].Value);
            row.SuperAdminOnly = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells["SuperAdminOnly"].Value);
            isDirty = true;
            UpdateStatus("저장하지 않은 메뉴관리 변경사항이 있습니다.");
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (grid.Columns[e.ColumnIndex].Name == "EditHelp")
            {
                EditRowHelp(e.RowIndex);
            }
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                EditRowHelp(e.RowIndex);
            }
        }

        private void EditSelectedHelp()
        {
            if (grid == null || grid.CurrentRow == null)
            {
                return;
            }

            EditRowHelp(grid.CurrentRow.Index);
        }

        private void EditRowHelp(int rowIndex)
        {
            if (!canEdit)
            {
                return;
            }

            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            OviaMenuSetting row = grid.Rows[rowIndex].Tag as OviaMenuSetting;
            if (row == null)
            {
                return;
            }

            string newHelp;
            if (!OviaMenuHelpEditDialog.TryEdit(this, row.MenuName, row.HelpText, out newHelp))
            {
                return;
            }

            row.HelpText = newHelp;
            grid.Rows[rowIndex].Cells["HelpText"].Value = Shorten(row.HelpText, 80);
            isDirty = true;
            UpdateStatus("저장하지 않은 메뉴관리 도움말 변경사항이 있습니다.");
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "메뉴관리 설정을 기본값으로 복원하시겠습니까?\r\n\r\n현재 저장하지 않은 변경사항은 사라집니다.",
                "OVIA 메뉴관리",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (result != DialogResult.OK)
            {
                return;
            }

            LoadRowsToGrid(OviaMenuHelpStore.CreateDefaultSettings());
            isDirty = true;
            UpdateStatus("기본값으로 복원되었습니다. 저장하기를 클릭해야 실제 저장됩니다.");
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            OviaMenuHelpStore.Save(rows);
            isDirty = false;
            UpdateStatus("메뉴관리 설정이 저장되었습니다.");
        }

        private void UpdateStatus(string text)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = text == null ? string.Empty : text;
            }
        }

        public void ApplyWorkspaceLayout()
        {
            int width = Math.Max(1, ClientSize.Width - 64);
            if (grid != null)
            {
                grid.Width = width;
                grid.Height = Math.Max(220, ClientSize.Height - 292);
            }

            int buttonY = Math.Max(0, ClientSize.Height - 112);
            if (btnEditHelp != null) btnEditHelp.Top = buttonY;
            if (btnReset != null) btnReset.Top = buttonY;
            if (btnSave != null)
            {
                btnSave.Top = buttonY;
                btnSave.Left = Math.Max(32, ClientSize.Width - 280);
            }
            if (btnClose != null)
            {
                btnClose.Top = buttonY;
                btnClose.Left = Math.Max(32, ClientSize.Width - 144);
            }
            if (lblStatus != null)
            {
                lblStatus.Top = Math.Max(0, ClientSize.Height - 58);
                lblStatus.Width = width;
            }
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
        public bool SuperAdminOnly;
        public string HelpText = string.Empty;
    }

    internal static class OviaMenuHelpStore
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
                    if (parts.Length < 6)
                    {
                        continue;
                    }

                    string key = Decode(parts[0]);
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
                    setting.SuperAdminOnly = parts[4] == "1";
                    setting.HelpText = Decode(parts[5]);
                }
            }
            catch
            {
                return defaults;
            }

            return defaults;
        }

        public static void Save(List<OviaMenuSetting> settings)
        {
            if (settings == null)
            {
                settings = CreateDefaultSettings();
            }

            string path = GetFilePath();
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# OVIA menu settings");
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row == null)
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
                sb.Append(row.SuperAdminOnly ? "1" : "0");
                sb.Append('\t');
                sb.Append(Encode(row.HelpText));
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static string GetHelpText(string key, string fallbackText)
        {
            string normalized = key == null ? string.Empty : key.Trim();
            List<OviaMenuSetting> settings = Load();
            int i;
            for (i = 0; i < settings.Count; i++)
            {
                OviaMenuSetting row = settings[i];
                if (row != null && string.Equals(row.Key, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(row.HelpText))
                    {
                        return row.HelpText;
                    }
                }
            }

            return fallbackText == null ? string.Empty : fallbackText;
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

        public static List<OviaMenuSetting> CreateDefaultSettings()
        {
            List<OviaMenuSetting> list = new List<OviaMenuSetting>();
            Add(list, "MAIN", "메인", 1, false, "OVIA 전체 업무 현황, AutoCAD 상태, 최근 BarList 작업, 공사 현황, 공지사항을 확인하는 대시보드입니다.");
            Add(list, "PROJECT_MANAGER", "공사관리", 1, false, "공사 목록을 검색하고 공사 등록, 수정, 완료공사 포함 조회를 처리합니다. 공사별 BarList, 생산오더, 송장, 태그 업무는 공사 상세 콘텐츠에서 연결됩니다.");
            Add(list, "PROJECT_BARLIST_LIST", "공사별 BarList", 2, false, "선택한 공사에 저장된 BarList 목록을 조회하고 신규 등록, 수정, 다른 공사 BarList 불러오기 흐름으로 이동합니다.");
            Add(list, "BARLIST", "BarList", 3, false, "CAD 도면 또는 Excel에서 BarList를 가져와 검토하고 저장하는 화면입니다. 형상, 수량, 길이, 중량 데이터를 확인합니다.");
            Add(list, "OPERATIONS", "운영현황", 1, false, "전체 BarList, 생산오더, 입출고, 재고, 송장, 태그/QR, 미처리 작업을 통합 조회하는 메뉴입니다.");
            Add(list, "MATERIAL_STOCK", "자재/재고", 1, false, "입고, 재고현황, 재고조정, 출고사용내역을 관리하는 메뉴입니다.");
            Add(list, "SHIPPING_INVOICE", "출하/송장", 1, false, "송장 조회와 발행, 납품표, 인수증, 검수양식, 출하 실적등록을 처리하는 메뉴입니다.");
            Add(list, "ERP", "ERP", 1, false, "시스템 설정에 저장된 ERP 주소를 기본 웹 브라우저로 열고, 추후 ERP 동기화 상태를 확인합니다.");
            Add(list, "MASTER_DATA", "기준정보", 1, false, "거래처, 철근메이커, 자재/규격, 형상코드, 차량, 작업자, 기계, 위치 같은 업무 기준 데이터를 관리합니다.");
            Add(list, "SETTINGS", "환경설정", 1, false, "OVIA 시스템 동작, BarList 매핑, 단위중량표, 출력 양식, QR/바코드 양식, 프린터, 백업, 버전정보를 관리합니다.");
            Add(list, "BARLIST_MAPPING", "BarList 항목 매핑", 2, true, "CAD 도면마다 다른 철근재료표 헤더명을 OVIA 기본 헤더로 치환합니다. 매핑 텍스트는 셀 단위로 추가/수정할 수 있으며, 매핑 열은 드래그로 순서를 바꿀 수 있습니다.");
            Add(list, "REBAR_UNIT_WEIGHT", "이형철근 단위중량표", 2, true, "규격과 단위무게 기준으로 1톤 단위 조견표와 총길이/중량 계산 기준을 관리합니다. 최고관리자만 수정할 수 있습니다.");
            Add(list, "SYSTEM_SETTINGS", "시스템 설정", 2, true, "ERP 연결 주소와 회사 로고처럼 OVIA 전체에 적용되는 기본값을 관리합니다. 이 화면의 저장 권한은 최고관리자에게만 부여됩니다.");
            Add(list, "IMPORT_TEMPLATE", "가져오기 양식 설정", 2, true, "SSBAR, Tekla, Excel, DBF, BAR 등 외부 데이터 가져오기 템플릿을 관리할 예정입니다.");
            Add(list, "PRINT_TEMPLATE", "출력 양식 설정", 2, true, "송장, 납품표, 인수증, 검수양식, BarList 출력 템플릿을 관리할 예정입니다.");
            Add(list, "QR_BARCODE_TEMPLATE", "QR/바코드 양식 설정", 2, true, "QR 데이터 구조, 바코드 종류, 태그 양식 연결 기준을 관리할 예정입니다.");
            Add(list, "PRINTER_SETTINGS", "프린터 설정", 2, true, "기본 프린터, 라벨 프린터, 송장 프린터, 용지와 여백을 관리할 예정입니다.");
            Add(list, "BACKUP_RESTORE", "백업/복원", 2, true, "로컬 데이터, 설정, 공사 데이터를 백업하거나 복원하는 메뉴입니다.");
            Add(list, "MENU_MANAGER", "메뉴관리", 2, true, "OVIA에 있는 모든 메뉴 및 페이지의 사용 여부, 최고관리자 접근 여부, 페이지별 도움말을 관리합니다. 권한관리는 추후 ERP 사용자 정보와 연동됩니다.");
            Add(list, "VERSION_INFO", "버전정보", 2, true, "로그인 화면 하단과 시스템 정보에 표시되는 OVIA 버전 정보를 관리합니다.");
            return list;
        }

        private static void Add(List<OviaMenuSetting> list, string key, string name, int level, bool adminOnly, string help)
        {
            OviaMenuSetting setting = new OviaMenuSetting();
            setting.Key = key;
            setting.MenuName = name;
            setting.Level = level;
            setting.Enabled = true;
            setting.SuperAdminOnly = adminOnly;
            setting.HelpText = help == null ? string.Empty : help;
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

    internal sealed class OviaMenuHelpDialog : Form
    {
        public OviaMenuHelpDialog(string title, string helpText)
        {
            BuildUI(title, helpText);
        }

        public static void ShowHelp(IWin32Window owner, string title, string helpText)
        {
            using (OviaMenuHelpDialog dialog = new OviaMenuHelpDialog(title, helpText))
            {
                Form ownerForm = owner as Form;
                if (ownerForm != null)
                {
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.ShowDialog(ownerForm);
                }
                else
                {
                    dialog.StartPosition = FormStartPosition.CenterScreen;
                    dialog.ShowDialog(owner);
                }
            }
        }

        private void BuildUI(string title, string helpText)
        {
            string menuTitle = string.IsNullOrWhiteSpace(title) ? "도움말" : title.Trim();
            Text = menuTitle.EndsWith("도움말", StringComparison.Ordinal) ? menuTitle : menuTitle + " 도움말";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            ClientSize = new Size(600, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(255, 252, 232);

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.BorderStyle = BorderStyle.None;
            body.ScrollBars = ScrollBars.Vertical;
            body.Location = new Point(24, 22);
            body.Size = new Size(552, 336);
            body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            body.Font = OviaFluentTheme.FontSystem(9.8F, FontStyle.Regular);
            body.ForeColor = OviaFluentTheme.TextSecondary;
            body.BackColor = Color.FromArgb(255, 252, 232);
            body.Text = string.IsNullOrWhiteSpace(helpText) ? "이 메뉴의 도움말이 아직 등록되지 않았습니다." : helpText;
            body.WordWrap = true;
            body.HideSelection = false;
            Controls.Add(body);

            Shown += delegate
            {
                body.SelectionStart = 0;
                body.SelectionLength = 0;
            };
        }
    }

    internal sealed class OviaMenuHelpEditDialog : Form
    {
        private TextBox txtHelp;
        public string HelpText { get; private set; }

        public static bool TryEdit(Form owner, string menuName, string currentHelp, out string helpText)
        {
            helpText = currentHelp == null ? string.Empty : currentHelp;
            using (OviaMenuHelpEditDialog dialog = new OviaMenuHelpEditDialog(menuName, currentHelp))
            {
                DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result == DialogResult.OK)
                {
                    helpText = dialog.HelpText;
                    return true;
                }
            }

            return false;
        }

        private OviaMenuHelpEditDialog(string menuName, string currentHelp)
        {
            BuildUI(menuName, currentHelp);
        }

        private void BuildUI(string menuName, string currentHelp)
        {
            string menuTitle = string.IsNullOrWhiteSpace(menuName) ? "메뉴" : menuName.Trim();
            Text = menuTitle + " 도움말 입력";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            ClientSize = new Size(620, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;

            txtHelp = new TextBox();
            txtHelp.Multiline = true;
            txtHelp.AcceptsReturn = true;
            txtHelp.AcceptsTab = true;
            txtHelp.WordWrap = true;
            txtHelp.ScrollBars = ScrollBars.Vertical;
            txtHelp.Location = new Point(28, 28);
            txtHelp.Size = new Size(564, 284);
            txtHelp.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            txtHelp.Text = currentHelp == null ? string.Empty : currentHelp;
            Controls.Add(txtHelp);

            Button save = new Button();
            save.Text = "저장";
            save.Location = new Point(380, 334);
            save.Size = new Size(100, 34);
            save.FlatStyle = FlatStyle.Flat;
            save.BackColor = OviaFluentTheme.Accent;
            save.ForeColor = Color.White;
            save.Font = OviaFluentTheme.FontButton(9F, FontStyle.Bold);
            save.FlatAppearance.BorderSize = 0;
            save.Click += Save_Click;
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "취소";
            cancel.Location = new Point(492, 334);
            cancel.Size = new Size(100, 34);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.White;
            cancel.ForeColor = OviaFluentTheme.TextPrimary;
            cancel.Font = OviaFluentTheme.FontButton(9F, FontStyle.Regular);
            cancel.FlatAppearance.BorderColor = OviaFluentTheme.ControlBorder;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            CancelButton = cancel;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            HelpText = txtHelp == null ? string.Empty : txtHelp.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
