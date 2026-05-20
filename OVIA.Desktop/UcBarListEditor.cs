using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class UcBarListEditor : UserControl
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public event Action<OviaProjectInfo> BackToBarListList;

        private readonly string companyId;
        private readonly string userId;
        private readonly OviaProjectInfo project;
        private readonly string openFilePath;

        private DataGridView grid;
        private TextBox txtTitle;
        private TextBox txtFilePath;
        private Label lblRowCount;
        private Label lblTotalQty;
        private Label lblTotalLength;
        private Label lblTotalWeight;
        private Label lblStatus;
        private Label lblSaveState;
        private Label lblGuide;

        private FileSystemWatcher autoCadWatcher;
        private DateTime autoImportStartTime;
        private string lastLoadedFilePath = "";
        private bool waitingAutoCadImport = false;
        private bool isSaved = true;

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);

        public UcBarListEditor(string companyId, string userId, OviaProjectInfo project, string openFilePath)
        {
            this.companyId = companyId;
            this.userId = userId;
            this.project = project;
            this.openFilePath = openFilePath == null ? "" : openFilePath;

            BuildUI();

            if (this.openFilePath != "")
            {
                LoadCsv(this.openFilePath, true);
                txtTitle.Text = Path.GetFileNameWithoutExtension(this.openFilePath);
                isSaved = true;
                UpdateSaveState();
                SetStatus("저장된 BarList 상세를 열었습니다. 수정 후 저장하면 새 이력으로 저장됩니다.", false);
            }
            else
            {
                isSaved = true;
                UpdateSaveState();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAutoCadWatcher();
            }

            base.Dispose(disposing);
        }

        private void BuildUI()
        {
            this.BackColor = SurfaceColor;
            this.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = "BarList 등록 / 상세";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 21F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 22);
            this.Controls.Add(title);

            Label desc = new Label();
            desc.Text = project.DisplayName + "  |  AutoCAD에서 추출한 데이터를 확인 후 저장합니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(38, 68);
            this.Controls.Add(desc);

            BuildTopArea();
            BuildSummary();
            BuildGrid();
            BuildFooter();
        }

        private void BuildTopArea()
        {
            OviaUiCard card = new OviaUiCard();
            card.Location = new Point(34, 100);
            card.Size = new Size(1050, 156);
            card.SurfaceColor = SurfaceColor;
            this.Controls.Add(card);

            Label titleLabel = new Label();
            titleLabel.Text = "BarList 제목";
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            titleLabel.ForeColor = TextSub;
            titleLabel.BackColor = Color.White;
            titleLabel.Location = new Point(22, 16);
            card.Controls.Add(titleLabel);

            txtTitle = new TextBox();
            txtTitle.Location = new Point(22, 41);
            txtTitle.Size = new Size(300, 23);
            txtTitle.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtTitle.Text = "신규 BarList";
            txtTitle.TextChanged += DataChanged;
            card.Controls.Add(txtTitle);

            Label fileLabel = new Label();
            fileLabel.Text = "추출 파일";
            fileLabel.AutoSize = true;
            fileLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            fileLabel.ForeColor = TextSub;
            fileLabel.BackColor = Color.White;
            fileLabel.Location = new Point(342, 16);
            card.Controls.Add(fileLabel);

            txtFilePath = new TextBox();
            txtFilePath.Location = new Point(342, 41);
            txtFilePath.Size = new Size(420, 23);
            txtFilePath.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtFilePath.ReadOnly = true;
            card.Controls.Add(txtFilePath);

            OviaUiButton autoButton = new OviaUiButton();
            autoButton.Text = "AutoCAD에서 가져오기";
            autoButton.Location = new Point(780, 34);
            autoButton.Size = new Size(160, 34);
            autoButton.StartColor = BrandCyan;
            autoButton.EndColor = BrandViolet;
            autoButton.Click += AutoCadImport_Click;
            card.Controls.Add(autoButton);

            OviaUiButton backButton = new OviaUiButton();
            backButton.Text = "BarList 목록";
            backButton.Location = new Point(952, 34);
            backButton.Size = new Size(82, 34);
            backButton.StartColor = Color.FromArgb(120, 128, 150);
            backButton.EndColor = Color.FromArgb(85, 93, 115);
            backButton.Click += BackButton_Click;
            card.Controls.Add(backButton);

            OviaUiButton recentButton = new OviaUiButton();
            recentButton.Text = "최근 추출";
            recentButton.Location = new Point(22, 82);
            recentButton.Size = new Size(92, 32);
            recentButton.StartColor = Color.FromArgb(70, 130, 230);
            recentButton.EndColor = BrandViolet;
            recentButton.Click += LoadRecent_Click;
            card.Controls.Add(recentButton);

            OviaUiButton csvButton = new OviaUiButton();
            csvButton.Text = "CSV 선택";
            csvButton.Location = new Point(126, 82);
            csvButton.Size = new Size(92, 32);
            csvButton.StartColor = BrandViolet;
            csvButton.EndColor = BrandIndigo;
            csvButton.Click += OpenCsv_Click;
            card.Controls.Add(csvButton);

            OviaUiButton saveButton = new OviaUiButton();
            saveButton.Text = "검토 후 저장";
            saveButton.Location = new Point(230, 82);
            saveButton.Size = new Size(120, 32);
            saveButton.StartColor = Color.FromArgb(30, 160, 105);
            saveButton.EndColor = Color.FromArgb(20, 120, 82);
            saveButton.Click += SaveBarList_Click;
            card.Controls.Add(saveButton);

            lblSaveState = new Label();
            lblSaveState.Text = "저장 상태: 대기";
            lblSaveState.AutoSize = true;
            lblSaveState.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            lblSaveState.ForeColor = TextSub;
            lblSaveState.BackColor = Color.White;
            lblSaveState.Location = new Point(370, 88);
            card.Controls.Add(lblSaveState);

            lblGuide = new Label();
            lblGuide.Text = "주의: 도면의 BarList와 변환 데이터를 반드시 비교 확인한 후 저장하세요. 저장 전 데이터는 공사별 목록에 반영되지 않습니다.";
            lblGuide.AutoSize = false;
            lblGuide.Size = new Size(1000, 28);
            lblGuide.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            lblGuide.ForeColor = Color.FromArgb(210, 78, 78);
            lblGuide.BackColor = Color.FromArgb(255, 248, 230);
            lblGuide.Location = new Point(22, 122);
            card.Controls.Add(lblGuide);
        }

        private void BuildSummary()
        {
            AddSummaryCard("행 개수", "0", new Point(34, 274), out lblRowCount);
            AddSummaryCard("총 수량", "0", new Point(260, 274), out lblTotalQty);
            AddSummaryCard("총길이(M)", "0", new Point(486, 274), out lblTotalLength);
            AddSummaryCard("중량 합계", "0", new Point(712, 274), out lblTotalWeight);

            lblStatus = new Label();
            lblStatus.Text = "AutoCAD에서 가져오거나 CSV를 선택하세요.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(240, 62);
            lblStatus.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(948, 284);
            this.Controls.Add(lblStatus);
        }

        private void AddSummaryCard(string title, string value, Point location, out Label valueLabel)
        {
            OviaUiCard card = new OviaUiCard();
            card.Location = location;
            card.Size = new Size(200, 78);
            card.SurfaceColor = SurfaceColor;
            this.Controls.Add(card);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            titleLabel.ForeColor = TextSub;
            titleLabel.BackColor = Color.White;
            titleLabel.Location = new Point(18, 14);
            card.Controls.Add(titleLabel);

            valueLabel = new Label();
            valueLabel.Text = value;
            valueLabel.AutoSize = true;
            valueLabel.Font = new Font("맑은 고딕", 18F, FontStyle.Bold);
            valueLabel.ForeColor = TextDark;
            valueLabel.BackColor = Color.White;
            valueLabel.Location = new Point(16, 36);
            card.Controls.Add(valueLabel);
        }

        private void BuildGrid()
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 372);
            grid.Size = new Size(1050, 265);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.CellEndEdit += Grid_CellEndEdit;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 241, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 28;

            this.Controls.Add(grid);
        }

        private void BuildFooter()
        {
            OviaUiButton coverButton = new OviaUiButton();
            coverButton.Text = "갑지출력";
            coverButton.Location = new Point(34, 660);
            coverButton.Size = new Size(94, 34);
            coverButton.StartColor = Color.FromArgb(108, 117, 145);
            coverButton.EndColor = Color.FromArgb(78, 86, 110);
            coverButton.Click += OutputPlaceholder_Click;
            this.Controls.Add(coverButton);

            OviaUiButton detailButton = new OviaUiButton();
            detailButton.Text = "내역출력";
            detailButton.Location = new Point(140, 660);
            detailButton.Size = new Size(94, 34);
            detailButton.StartColor = Color.FromArgb(108, 117, 145);
            detailButton.EndColor = Color.FromArgb(78, 86, 110);
            detailButton.Click += OutputPlaceholder_Click;
            this.Controls.Add(detailButton);

            OviaUiButton tagButton = new OviaUiButton();
            tagButton.Text = "태그발행";
            tagButton.Location = new Point(246, 660);
            tagButton.Size = new Size(94, 34);
            tagButton.StartColor = Color.FromArgb(108, 117, 145);
            tagButton.EndColor = Color.FromArgb(78, 86, 110);
            tagButton.Click += OutputPlaceholder_Click;
            this.Controls.Add(tagButton);

            OviaUiButton deleteButton = new OviaUiButton();
            deleteButton.Text = "선택 행 삭제";
            deleteButton.Location = new Point(352, 660);
            deleteButton.Size = new Size(110, 34);
            deleteButton.StartColor = Color.FromArgb(215, 85, 85);
            deleteButton.EndColor = Color.FromArgb(165, 50, 60);
            deleteButton.Click += DeleteRows_Click;
            this.Controls.Add(deleteButton);
        }

        private void AutoCadImport_Click(object sender, EventArgs e)
        {
            if (!IsAutoCadRunning())
            {
                SetStatus("AutoCAD 비활성 상태입니다. AutoCAD를 먼저 실행하고 DWG 도면을 열어주세요.", true);
                return;
            }

            StartAutoCadWatcher();
            ActivateAutoCad();

            SetStatus("AutoCAD 추출 대기 중입니다. 현재 개발 단계에서는 OVIABOX → OVIABOXTABLE 실행 후 자동 입력됩니다.", false);
        }

        private void StartAutoCadWatcher()
        {
            StopAutoCadWatcher();

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (!Directory.Exists(desktop))
            {
                return;
            }

            autoImportStartTime = DateTime.Now.AddSeconds(-3);
            waitingAutoCadImport = true;

            autoCadWatcher = new FileSystemWatcher();
            autoCadWatcher.Path = desktop;
            autoCadWatcher.Filter = "OVIA_BoxTable_*.csv";
            autoCadWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            autoCadWatcher.Created += AutoCadWatcher_Changed;
            autoCadWatcher.Changed += AutoCadWatcher_Changed;
            autoCadWatcher.EnableRaisingEvents = true;
        }

        private void StopAutoCadWatcher()
        {
            if (autoCadWatcher != null)
            {
                autoCadWatcher.EnableRaisingEvents = false;
                autoCadWatcher.Created -= AutoCadWatcher_Changed;
                autoCadWatcher.Changed -= AutoCadWatcher_Changed;
                autoCadWatcher.Dispose();
                autoCadWatcher = null;
            }
        }

        private void AutoCadWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!waitingAutoCadImport || this.IsDisposed)
            {
                return;
            }

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    TryLoadAutoCadLatestCsv();
                }));
            }
            catch
            {
            }
        }

        private void TryLoadAutoCadLatestCsv()
        {
            string filePath = FindLatestOviaBoxTableCsvAfter(autoImportStartTime);

            if (filePath == "" || filePath == lastLoadedFilePath)
            {
                return;
            }

            if (!WaitUntilFileReady(filePath))
            {
                return;
            }

            LoadCsv(filePath, false);
            waitingAutoCadImport = false;
            StopAutoCadWatcher();

            SetStatus("AutoCAD 추출 데이터가 자동 입력되었습니다. 반드시 도면과 비교 확인 후 저장하세요.", true);
        }

        private bool WaitUntilFileReady(string filePath)
        {
            int i;

            for (i = 0; i < 10; i++)
            {
                try
                {
                    using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length > 0)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }

                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
            }

            return false;
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

        private void ActivateAutoCad()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("acad");

                if (processes == null || processes.Length == 0)
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

        private void LoadRecent_Click(object sender, EventArgs e)
        {
            string filePath = FindLatestOviaBoxTableCsv();

            if (filePath == "")
            {
                SetStatus("바탕화면에서 OVIA_BoxTable CSV 파일을 찾지 못했습니다.", true);
                return;
            }

            LoadCsv(filePath, false);
        }

        private void OpenCsv_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "OVIA BoxTable CSV 선택";
            dialog.Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*";
            dialog.Multiselect = false;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            LoadCsv(dialog.FileName, false);
        }

        private string FindLatestOviaBoxTableCsv()
        {
            return FindLatestOviaBoxTableCsvAfter(DateTime.MinValue);
        }

        private string FindLatestOviaBoxTableCsvAfter(DateTime startTime)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (!Directory.Exists(desktop))
            {
                return "";
            }

            string[] files = Directory.GetFiles(desktop, "OVIA_BoxTable_*.csv");

            if (files == null || files.Length == 0)
            {
                return "";
            }

            List<string> candidates = new List<string>();
            int i;

            for (i = 0; i < files.Length; i++)
            {
                DateTime t = File.GetLastWriteTime(files[i]);

                if (t >= startTime)
                {
                    candidates.Add(files[i]);
                }
            }

            if (candidates.Count == 0)
            {
                return "";
            }

            candidates.Sort(delegate (string a, string b)
            {
                DateTime at = File.GetLastWriteTime(a);
                DateTime bt = File.GetLastWriteTime(b);

                return bt.CompareTo(at);
            });

            return candidates[0];
        }

        private void LoadCsv(string filePath, bool alreadySaved)
        {
            try
            {
                List<List<string>> rows = OviaLocalStore.ReadCsv(filePath);

                if (rows.Count == 0)
                {
                    SetStatus("CSV 파일에 읽을 데이터가 없습니다.", true);
                    return;
                }

                BindCsvRows(rows);
                txtFilePath.Text = filePath;
                lastLoadedFilePath = filePath;

                if (txtTitle.Text.Trim() == "" || txtTitle.Text == "신규 BarList")
                {
                    txtTitle.Text = Path.GetFileNameWithoutExtension(filePath);
                }

                isSaved = alreadySaved;
                UpdateSaveState();
                RecalculateSummary();

                if (alreadySaved)
                {
                    SetStatus("저장된 BarList를 불러왔습니다.", false);
                }
                else
                {
                    SetStatus("BarList 후보 데이터를 불러왔습니다. 도면과 비교 확인 후 저장하세요.", true);
                }
            }
            catch (Exception ex)
            {
                SetStatus("CSV 불러오기 오류: " + ex.Message, true);
            }
        }

        private void BindCsvRows(List<List<string>> rows)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();

            List<string> headers = rows[0];
            int i;

            for (i = 0; i < headers.Count; i++)
            {
                string header = headers[i];

                if (header == null || header.Trim() == "")
                {
                    header = "Column" + (i + 1).ToString();
                }

                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = header;
                column.HeaderText = header;
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.Resizable = DataGridViewTriState.True;
                grid.Columns.Add(column);
            }

            int r;

            for (r = 1; r < rows.Count; r++)
            {
                List<string> values = rows[r];
                object[] cells = new object[headers.Count];

                for (i = 0; i < headers.Count; i++)
                {
                    cells[i] = i < values.Count ? values[i] : "";
                }

                grid.Rows.Add(cells);
            }

            ApplyGridColumnStyle();
        }

        private void ApplyGridColumnStyle()
        {
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].HeaderText;

                if (ContainsAny(name, "No", "RowType", "SourceRowNo"))
                {
                    grid.Columns[i].Visible = false;
                    continue;
                }

                if (ContainsAny(name, "번호"))
                {
                    grid.Columns[i].Width = 70;
                }
                else if (ContainsAny(name, "규격"))
                {
                    grid.Columns[i].Width = 95;
                }
                else if (ContainsAny(name, "형상"))
                {
                    grid.Columns[i].Width = 150;
                }
                else if (ContainsAny(name, "길이"))
                {
                    grid.Columns[i].Width = 100;
                }
                else if (ContainsAny(name, "수량"))
                {
                    grid.Columns[i].Width = 85;
                }
                else if (ContainsAny(name, "중량"))
                {
                    grid.Columns[i].Width = 105;
                }
                else
                {
                    grid.Columns[i].Width = 95;
                }
            }
        }

        private void SaveBarList_Click(object sender, EventArgs e)
        {
            if (grid.Columns.Count == 0 || grid.Rows.Count == 0)
            {
                SetStatus("저장할 BarList 데이터가 없습니다.", true);
                return;
            }

            List<string> headers = new List<string>();
            List<List<string>> rows = new List<List<string>>();

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                headers.Add(grid.Columns[i].HeaderText);
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                List<string> row = new List<string>();

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    object value = grid.Rows[r].Cells[i].Value;
                    row.Add(value == null ? "" : value.ToString());
                }

                rows.Add(row);
            }

            try
            {
                string savedPath = OviaLocalStore.SaveBarListCsv(project, txtTitle.Text.Trim(), headers, rows);
                txtFilePath.Text = savedPath;
                lastLoadedFilePath = savedPath;
                isSaved = true;
                UpdateSaveState();
                SetStatus("검토 저장 완료. 공사별 BarList 목록에 반영되었습니다.", false);
            }
            catch (Exception ex)
            {
                SetStatus("BarList 저장 오류: " + ex.Message, true);
            }
        }

        private void DeleteRows_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0)
            {
                SetStatus("삭제할 행을 선택해주세요.", true);
                return;
            }

            int i;

            for (i = grid.SelectedRows.Count - 1; i >= 0; i--)
            {
                if (!grid.SelectedRows[i].IsNewRow)
                {
                    grid.Rows.Remove(grid.SelectedRows[i]);
                }
            }

            MarkUnsaved();
            RecalculateSummary();
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            MarkUnsaved();
            RecalculateSummary();
        }

        private void DataChanged(object sender, EventArgs e)
        {
            if (grid != null && grid.Columns.Count > 0)
            {
                MarkUnsaved();
            }
        }

        private void OutputPlaceholder_Click(object sender, EventArgs e)
        {
            if (!isSaved)
            {
                SetStatus("출력/태그발행 전 BarList를 먼저 검토 저장해주세요.", true);
                return;
            }

            SetStatus("갑지출력, 내역출력, 태그발행은 다음 단계에서 구현합니다.", false);
        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            StopAutoCadWatcher();

            if (BackToBarListList != null)
            {
                BackToBarListList(project);
            }
        }

        private void RecalculateSummary()
        {
            int rowCount = 0;
            double totalQty = 0;
            double totalLength = 0;
            double totalWeight = 0;

            int qtyCol = FindColumnIndex("수량");
            int lengthCol = FindColumnIndex("총길이");
            int weightCol = FindColumnIndex("중량");

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                rowCount++;

                if (qtyCol >= 0)
                {
                    totalQty += OviaLocalStore.ParseNumber(GetCellText(r, qtyCol));
                }

                if (lengthCol >= 0)
                {
                    totalLength += OviaLocalStore.ParseNumber(GetCellText(r, lengthCol));
                }

                if (weightCol >= 0)
                {
                    totalWeight += OviaLocalStore.ParseNumber(GetCellText(r, weightCol));
                }
            }

            lblRowCount.Text = rowCount.ToString();
            lblTotalQty.Text = totalQty.ToString("0.###");
            lblTotalLength.Text = totalLength.ToString("0.###");
            lblTotalWeight.Text = totalWeight.ToString("0.###");
        }

        private int FindColumnIndex(string keyword)
        {
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (!grid.Columns[i].Visible)
                {
                    continue;
                }

                string name = grid.Columns[i].HeaderText;

                if (name != null && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private string GetCellText(int rowIndex, int columnIndex)
        {
            object value = grid.Rows[rowIndex].Cells[columnIndex].Value;

            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

        private bool ContainsAny(string value, params string[] keywords)
        {
            if (value == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < keywords.Length; i++)
            {
                if (value.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkUnsaved()
        {
            isSaved = false;
            UpdateSaveState();
        }

        private void UpdateSaveState()
        {
            if (lblSaveState == null)
            {
                return;
            }

            if (isSaved)
            {
                lblSaveState.Text = "저장 상태: 저장 완료";
                lblSaveState.ForeColor = Color.FromArgb(18, 166, 91);
            }
            else
            {
                lblSaveState.Text = "저장 상태: 확인 필요";
                lblSaveState.ForeColor = Color.FromArgb(210, 78, 78);
            }
        }

        private void SetStatus(string message, bool warning)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = warning ? Color.FromArgb(210, 78, 78) : TextSub;
        }
    }
}
