using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class FrmBarList : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly string companyId;
        private readonly string userId;
        private readonly string projectNo;
        private readonly string projectName;
        private readonly string clientName;
        private readonly string projectStatus;

        private DataGridView grid;
        private TextBox txtFilePath;
        private Label lblRowCount;
        private Label lblTotalQty;
        private Label lblTotalLength;
        private Label lblTotalWeight;
        private Label lblStatus;
        private Label lblProjectTitle;
        private Label lblProjectSub;
        private Label lblSaveState;

        private FileSystemWatcher autoCadWatcher;
        private DateTime autoImportStartTime;
        private string lastLoadedFilePath = "";
        private bool waitingAutoCadImport = false;
        private bool isSaved = true;
        private bool isClosingByButton = false;

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);

        public FrmBarList(string companyId, string userId)
            : this(companyId, userId, "", "공사 미선택", "", "")
        {
        }

        public FrmBarList(string companyId, string userId, string projectNo, string projectName, string clientName, string projectStatus)
        {
            this.companyId = companyId;
            this.userId = userId;
            this.projectNo = projectNo == null ? "" : projectNo;
            this.projectName = projectName == null ? "" : projectName;
            this.clientName = clientName == null ? "" : clientName;
            this.projectStatus = projectStatus == null ? "" : projectStatus;

            BuildUI();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            this.Text = "OVIA BarList";
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1240, 760);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmBarList_FormClosing;

            Panel bg = new Panel();
            bg.Dock = DockStyle.Fill;
            bg.BackColor = SurfaceColor;
            this.Controls.Add(bg);

            BuildHeader(bg);
            BuildProjectInfo(bg);
            BuildFileBar(bg);
            BuildSummary(bg);
            BuildGrid(bg);
            BuildFooter(bg);

            this.ResumeLayout(false);
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = "BarList";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 24);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "공사를 선택한 뒤 AutoCAD에서 철근 집계표를 선택하면 BarList 후보가 자동 입력됩니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(38, 70);
            parent.Controls.Add(desc);

            OviaBarListButton close = new OviaBarListButton();
            close.Text = "닫기";
            close.Location = new Point(1120, 34);
            close.Size = new Size(82, 34);
            close.StartColor = Color.FromArgb(120, 128, 150);
            close.EndColor = Color.FromArgb(85, 93, 115);
            close.Click += Close_Click;
            parent.Controls.Add(close);
        }

        private void BuildProjectInfo(Control parent)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = new Point(34, 100);
            card.Size = new Size(1168, 72);
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            lblProjectTitle = new Label();
            lblProjectTitle.Text = GetProjectTitleText();
            lblProjectTitle.AutoSize = true;
            lblProjectTitle.Font = new Font("맑은 고딕", 14F, FontStyle.Bold);
            lblProjectTitle.ForeColor = TextDark;
            lblProjectTitle.BackColor = Color.White;
            lblProjectTitle.Location = new Point(22, 13);
            card.Controls.Add(lblProjectTitle);

            lblProjectSub = new Label();
            lblProjectSub.Text = GetProjectSubText();
            lblProjectSub.AutoSize = true;
            lblProjectSub.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            lblProjectSub.ForeColor = TextSub;
            lblProjectSub.BackColor = Color.White;
            lblProjectSub.Location = new Point(24, 44);
            card.Controls.Add(lblProjectSub);

            lblSaveState = new Label();
            lblSaveState.Text = "저장 상태: 대기";
            lblSaveState.AutoSize = true;
            lblSaveState.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            lblSaveState.ForeColor = TextSub;
            lblSaveState.BackColor = Color.White;
            lblSaveState.Location = new Point(980, 26);
            card.Controls.Add(lblSaveState);
        }

        private string GetProjectTitleText()
        {
            if (projectNo.Trim() == "" && projectName.Trim() == "")
            {
                return "공사 미선택";
            }

            return projectNo + "  " + projectName;
        }

        private string GetProjectSubText()
        {
            string text = "";

            if (clientName.Trim() != "")
            {
                text += "거래처: " + clientName;
            }

            if (projectStatus.Trim() != "")
            {
                if (text != "")
                {
                    text += "   |   ";
                }

                text += "상태: " + projectStatus;
            }

            if (text == "")
            {
                text = "공사관리에서 공사를 선택하면 해당 공사에 BarList를 저장할 수 있습니다.";
            }

            return text;
        }

        private void BuildFileBar(Control parent)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = new Point(34, 187);
            card.Size = new Size(1168, 100);
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            Label fileLabel = new Label();
            fileLabel.Text = "추출 파일";
            fileLabel.AutoSize = true;
            fileLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            fileLabel.ForeColor = TextSub;
            fileLabel.BackColor = Color.White;
            fileLabel.Location = new Point(22, 17);
            card.Controls.Add(fileLabel);

            txtFilePath = new TextBox();
            txtFilePath.Location = new Point(22, 43);
            txtFilePath.Size = new Size(570, 23);
            txtFilePath.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtFilePath.ReadOnly = true;
            card.Controls.Add(txtFilePath);

            OviaBarListButton autoButton = new OviaBarListButton();
            autoButton.Text = "AutoCAD에서 가져오기";
            autoButton.Location = new Point(610, 36);
            autoButton.Size = new Size(160, 34);
            autoButton.StartColor = BrandCyan;
            autoButton.EndColor = BrandViolet;
            autoButton.Click += AutoCadImport_Click;
            card.Controls.Add(autoButton);

            OviaBarListButton recentButton = new OviaBarListButton();
            recentButton.Text = "최근 추출";
            recentButton.Location = new Point(785, 36);
            recentButton.Size = new Size(92, 34);
            recentButton.StartColor = Color.FromArgb(70, 130, 230);
            recentButton.EndColor = BrandViolet;
            recentButton.Click += LoadRecent_Click;
            card.Controls.Add(recentButton);

            OviaBarListButton openButton = new OviaBarListButton();
            openButton.Text = "CSV 선택";
            openButton.Location = new Point(890, 36);
            openButton.Size = new Size(92, 34);
            openButton.StartColor = BrandViolet;
            openButton.EndColor = BrandIndigo;
            openButton.Click += OpenCsv_Click;
            card.Controls.Add(openButton);

            OviaBarListButton saveProjectButton = new OviaBarListButton();
            saveProjectButton.Text = "검토 후 저장";
            saveProjectButton.Location = new Point(995, 36);
            saveProjectButton.Size = new Size(120, 34);
            saveProjectButton.StartColor = Color.FromArgb(30, 160, 105);
            saveProjectButton.EndColor = Color.FromArgb(20, 120, 82);
            saveProjectButton.Click += SaveProjectBarList_Click;
            card.Controls.Add(saveProjectButton);

            Label guide = new Label();
            guide.Text = "※ AutoCAD에서 OVIABOX → OVIABOXTABLE을 실행하면 새 추출 CSV를 감지해 자동 입력합니다. 반드시 확인 후 저장하세요.";
            guide.AutoSize = true;
            guide.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            guide.ForeColor = Color.FromArgb(210, 78, 78);
            guide.BackColor = Color.White;
            guide.Location = new Point(24, 74);
            card.Controls.Add(guide);
        }

        private void BuildSummary(Control parent)
        {
            AddSummaryCard(parent, "행 개수", "0", new Point(34, 305), out lblRowCount);
            AddSummaryCard(parent, "총 수량", "0", new Point(260, 305), out lblTotalQty);
            AddSummaryCard(parent, "총길이(M)", "0", new Point(486, 305), out lblTotalLength);
            AddSummaryCard(parent, "중량 합계", "0", new Point(712, 305), out lblTotalWeight);

            lblStatus = new Label();
            lblStatus.Text = "AutoCAD에서 가져오거나 CSV를 선택하세요.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(240, 62);
            lblStatus.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(948, 315);
            parent.Controls.Add(lblStatus);
        }

        private void AddSummaryCard(Control parent, string title, string value, Point location, out Label valueLabel)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = location;
            card.Size = new Size(200, 78);
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

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

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 402);
            grid.Size = new Size(1168, 265);
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

            parent.Controls.Add(grid);
        }

        private void BuildFooter(Control parent)
        {
            OviaBarListButton coverButton = new OviaBarListButton();
            coverButton.Text = "갑지출력";
            coverButton.Location = new Point(34, 690);
            coverButton.Size = new Size(94, 34);
            coverButton.StartColor = Color.FromArgb(108, 117, 145);
            coverButton.EndColor = Color.FromArgb(78, 86, 110);
            coverButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(coverButton);

            OviaBarListButton detailButton = new OviaBarListButton();
            detailButton.Text = "내역출력";
            detailButton.Location = new Point(140, 690);
            detailButton.Size = new Size(94, 34);
            detailButton.StartColor = Color.FromArgb(108, 117, 145);
            detailButton.EndColor = Color.FromArgb(78, 86, 110);
            detailButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(detailButton);

            OviaBarListButton tagButton = new OviaBarListButton();
            tagButton.Text = "태그발행";
            tagButton.Location = new Point(246, 690);
            tagButton.Size = new Size(94, 34);
            tagButton.StartColor = Color.FromArgb(108, 117, 145);
            tagButton.EndColor = Color.FromArgb(78, 86, 110);
            tagButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(tagButton);

            OviaBarListButton deleteButton = new OviaBarListButton();
            deleteButton.Text = "선택 행 삭제";
            deleteButton.Location = new Point(352, 690);
            deleteButton.Size = new Size(110, 34);
            deleteButton.StartColor = Color.FromArgb(215, 85, 85);
            deleteButton.EndColor = Color.FromArgb(165, 50, 60);
            deleteButton.Click += DeleteRows_Click;
            parent.Controls.Add(deleteButton);

            OviaBarListButton saveCsvButton = new OviaBarListButton();
            saveCsvButton.Text = "CSV 저장";
            saveCsvButton.Location = new Point(474, 690);
            saveCsvButton.Size = new Size(94, 34);
            saveCsvButton.StartColor = Color.FromArgb(30, 160, 105);
            saveCsvButton.EndColor = Color.FromArgb(20, 120, 82);
            saveCsvButton.Click += SaveCsv_Click;
            parent.Controls.Add(saveCsvButton);

            Label footer = new Label();
            footer.Text = "※ 불러온 내용은 반드시 검토 후 저장해야 공사별 BarList에 반영됩니다.";
            footer.AutoSize = true;
            footer.Font = new Font("맑은 고딕", 8.5F, FontStyle.Bold);
            footer.ForeColor = Color.FromArgb(210, 78, 78);
            footer.BackColor = SurfaceColor;
            footer.Location = new Point(590, 700);
            parent.Controls.Add(footer);
        }

        private void AutoCadImport_Click(object sender, EventArgs e)
        {
            if (!IsAutoCadRunning())
            {
                MessageBox.Show(
                    "AutoCAD 비활성 상태입니다.\r\n\r\nAutoCAD를 먼저 실행하고 DWG 도면을 연 뒤 다시 시도해주세요.",
                    "OVIA AutoCAD 비활성",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            StartAutoCadWatcher();
            ActivateAutoCad();

            MessageBox.Show(
                "AutoCAD로 이동합니다.\r\n\r\n작업 순서:\r\n1. AutoCAD 명령창에서 OVIABOX 실행\r\n2. 원하는 배근표/집계표를 드래그 선택\r\n3. OVIABOXTABLE 실행\r\n\r\n추출 CSV가 생성되면 OVIA BarList 화면에 자동 입력됩니다.",
                "OVIA AutoCAD 가져오기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
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

            lblStatus.Text = "AutoCAD 추출 대기 중\r\nOVIABOXTABLE 실행을 기다립니다.";
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
            if (!waitingAutoCadImport)
            {
                return;
            }

            if (this.IsDisposed)
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

            if (filePath == "")
            {
                return;
            }

            if (filePath == lastLoadedFilePath)
            {
                return;
            }

            if (!WaitUntilFileReady(filePath))
            {
                return;
            }

            LoadCsv(filePath);
            waitingAutoCadImport = false;
            StopAutoCadWatcher();

            lblStatus.Text = "AutoCAD 추출 데이터 자동 입력 완료\r\n반드시 확인 후 저장하세요.";

            MessageBox.Show(
                "AutoCAD 추출 데이터가 BarList에 자동 입력되었습니다.\r\n\r\n내용을 반드시 확인한 후 [검토 후 저장]을 눌러야 공사별 BarList에 반영됩니다.",
                "OVIA BarList 자동 입력",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
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
                MessageBox.Show(
                    "바탕화면에서 OVIA_BoxTable CSV 파일을 찾지 못했습니다.\r\n\r\nAutoCAD에서 OVIABOXTABLE을 먼저 실행하거나 CSV 선택 버튼으로 파일을 직접 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            LoadCsv(filePath);
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

            LoadCsv(dialog.FileName);
        }

        private void SaveProjectBarList_Click(object sender, EventArgs e)
        {
            if (grid.Columns.Count == 0 || grid.Rows.Count == 0)
            {
                MessageBox.Show(
                    "저장할 BarList 데이터가 없습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            if (projectNo.Trim() == "")
            {
                MessageBox.Show(
                    "공사가 선택되지 않았습니다.\r\n\r\n공사관리에서 공사를 선택한 뒤 BarList를 저장해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            DialogResult result = MessageBox.Show(
                "불러온 내용을 모두 확인하셨습니까?\r\n\r\n[예]를 누르면 현재 BarList가 공사별 데이터로 저장됩니다.",
                "OVIA BarList 저장 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                string dir = GetProjectBarListDirectory();
                Directory.CreateDirectory(dir);

                string fileName = "BarList_" + projectNo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                string filePath = Path.Combine(dir, fileName);

                SaveGridToCsv(filePath);

                isSaved = true;
                UpdateSaveState();

                MessageBox.Show(
                    "BarList 저장이 완료되었습니다.\r\n\r\n" + filePath,
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "BarList 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
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

        private void SaveCsv_Click(object sender, EventArgs e)
        {
            if (grid.Columns.Count == 0)
            {
                MessageBox.Show(
                    "저장할 데이터가 없습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "BarList CSV 저장";
            dialog.Filter = "CSV 파일 (*.csv)|*.csv";
            dialog.FileName = "OVIA_BarList_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                SaveGridToCsv(dialog.FileName);

                MessageBox.Show(
                    "CSV 저장이 완료되었습니다.\r\n\r\n" + dialog.FileName,
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "CSV 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void DeleteRows_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "삭제할 행을 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

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

        private void OutputPlaceholder_Click(object sender, EventArgs e)
        {
            if (!isSaved)
            {
                MessageBox.Show(
                    "출력/태그 발행 전 BarList를 먼저 저장해주세요.\r\n\r\n불러온 내용을 확인한 후 [검토 후 저장]을 누르시면 됩니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                "갑지출력, 내역출력, 태그발행은 다음 단계에서 구현합니다.",
                "OVIA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void Close_Click(object sender, EventArgs e)
        {
            isClosingByButton = true;
            this.Close();
        }

        private void FrmBarList_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!isSaved && grid.Columns.Count > 0 && grid.Rows.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "저장하지 않은 BarList 데이터가 있습니다.\r\n\r\n저장하지 않고 닫으시겠습니까?",
                    "OVIA",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result != DialogResult.Yes)
                {
                    isClosingByButton = false;
                    e.Cancel = true;
                    return;
                }
            }

            StopAutoCadWatcher();
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

        private void LoadCsv(string filePath)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count == 0)
                {
                    MessageBox.Show(
                        "CSV 파일에 읽을 데이터가 없습니다.",
                        "OVIA",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    return;
                }

                BindCsvRows(rows);
                txtFilePath.Text = filePath;
                lastLoadedFilePath = filePath;
                lblStatus.Text = "불러오기 완료\r\n" + Path.GetFileName(filePath);

                MarkUnsaved();
                RecalculateSummary();

                MessageBox.Show(
                    "BarList 후보 데이터를 불러왔습니다.\r\n\r\n화면의 내용이 도면과 맞는지 반드시 확인한 후 [검토 후 저장]을 눌러주세요.",
                    "OVIA BarList 확인 필요",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "CSV 불러오기 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
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
                    if (i < values.Count)
                    {
                        cells[i] = values[i];
                    }
                    else
                    {
                        cells[i] = "";
                    }
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
                else if (ContainsAny(name, "비고"))
                {
                    grid.Columns[i].Width = 150;
                }
                else
                {
                    grid.Columns[i].Width = 95;
                }
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
                    totalQty += ParseNumber(GetCellText(r, qtyCol));
                }

                if (lengthCol >= 0)
                {
                    totalLength += ParseNumber(GetCellText(r, lengthCol));
                }

                if (weightCol >= 0)
                {
                    totalWeight += ParseNumber(GetCellText(r, weightCol));
                }
            }

            lblRowCount.Text = rowCount.ToString();
            lblTotalQty.Text = totalQty.ToString("0.###");
            lblTotalLength.Text = totalLength.ToString("0.###");
            lblTotalWeight.Text = totalWeight.ToString("0.###");
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

        private void SaveGridToCsv(string filePath)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                int i;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        writer.Write(",");
                    }

                    writer.Write(Csv(grid.Columns[i].HeaderText));
                }

                writer.WriteLine();

                int r;

                for (r = 0; r < grid.Rows.Count; r++)
                {
                    if (grid.Rows[r].IsNewRow)
                    {
                        continue;
                    }

                    for (i = 0; i < grid.Columns.Count; i++)
                    {
                        if (i > 0)
                        {
                            writer.Write(",");
                        }

                        object value = grid.Rows[r].Cells[i].Value;

                        if (value == null)
                        {
                            writer.Write(Csv(""));
                        }
                        else
                        {
                            writer.Write(Csv(value.ToString()));
                        }
                    }

                    writer.WriteLine();
                }
            }
        }

        private string Csv(string value)
        {
            if (value == null)
            {
                value = "";
            }

            value = value.Replace("\"", "\"\"");

            return "\"" + value + "\"";
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
    }

    public class OviaBarListCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

        public OviaBarListCard()
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

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, 14))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(Color.FromArgb(230, 235, 246), 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaBarListButton : Control
    {
        public Color StartColor = Color.FromArgb(91, 49, 225);
        public Color EndColor = Color.FromArgb(37, 30, 130);

        private bool hover;

        public OviaBarListButton()
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

            Color s = hover ? Lighten(StartColor, 18) : StartColor;
            Color en = hover ? Lighten(EndColor, 18) : EndColor;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, 7))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, s, en, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 9F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
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

    public static class OviaBarListDrawHelper
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
