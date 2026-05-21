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
    public class FrmProjectBarListList : Form
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly string projectNo;
        private readonly string projectName;
        private readonly string clientName;
        private readonly string projectStatus;

        private DataGridView grid;
        private Label lblStatus;
        private Label lblProjectTitle;
        private Label lblProjectSub;

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);

        private const int BaseClientWidth = 1180;
        private const int BaseClientHeight = 720;
        private Panel scrollPanel;
        private Panel contentPanel;
        private bool isScrollResetQueued = false;

        public FrmProjectBarListList(string companyId, string userId, string projectNo, string projectName, string clientName, string projectStatus)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.projectNo = projectNo == null ? "" : projectNo;
            this.projectName = projectName == null ? "" : projectName;
            this.clientName = clientName == null ? "" : clientName;
            this.projectStatus = projectStatus == null ? "" : projectStatus;

            BuildUI();
            BindBarListRows();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            this.Text = "OVIA 공사별 BarList";
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(BaseClientWidth, BaseClientHeight);
            this.MinimumSize = new Size(760, 520);
            this.BackColor = SurfaceColor;

            scrollPanel = new Panel();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.BackColor = SurfaceColor;
            scrollPanel.AutoScroll = true;
            scrollPanel.AutoScrollMinSize = new Size(BaseClientWidth, BaseClientHeight);
            scrollPanel.Resize += ScrollPanel_Resize;
            this.Controls.Add(scrollPanel);

            contentPanel = new Panel();
            contentPanel.Location = new Point(0, 0);
            contentPanel.Size = new Size(BaseClientWidth, BaseClientHeight);
            contentPanel.BackColor = SurfaceColor;
            scrollPanel.Controls.Add(contentPanel);

            BuildHeader(contentPanel);
            BuildProjectInfo(contentPanel);
            BuildToolbar(contentPanel);
            BuildGrid(contentPanel);
            BuildFooter(contentPanel);
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();

            this.ResumeLayout(false);
        }

        private void ScrollPanel_Resize(object sender, EventArgs e)
        {
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            QueueResetScrollToTopLeft();
        }

        private void UpdateScrollableContentSize()
        {
            if (scrollPanel == null || contentPanel == null || scrollPanel.IsDisposed || contentPanel.IsDisposed)
            {
                return;
            }

            bool needScroll = this.ClientSize.Width < BaseClientWidth || this.ClientSize.Height < BaseClientHeight;

            scrollPanel.SuspendLayout();

            try
            {
                if (needScroll)
                {
                    scrollPanel.AutoScroll = true;
                    scrollPanel.AutoScrollMinSize = new Size(BaseClientWidth, BaseClientHeight);

                    int width = Math.Max(BaseClientWidth, scrollPanel.ClientSize.Width);
                    int height = Math.Max(BaseClientHeight, scrollPanel.ClientSize.Height);

                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(width, height);
                }
                else
                {
                    scrollPanel.AutoScroll = false;
                    scrollPanel.AutoScrollMinSize = Size.Empty;
                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(scrollPanel.ClientSize.Width, scrollPanel.ClientSize.Height);
                }
            }
            finally
            {
                scrollPanel.ResumeLayout(false);
            }
        }

        private void QueueResetScrollToTopLeft()
        {
            if (isScrollResetQueued || this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            isScrollResetQueued = true;

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    isScrollResetQueued = false;
                    ResetScrollToTopLeft();
                }));
            }
            catch
            {
                isScrollResetQueued = false;
            }
        }

        private void ResetScrollToTopLeft()
        {
            if (scrollPanel == null || contentPanel == null || scrollPanel.IsDisposed || contentPanel.IsDisposed)
            {
                return;
            }

            scrollPanel.SuspendLayout();

            try
            {
                scrollPanel.AutoScrollPosition = new Point(0, 0);
                contentPanel.Location = new Point(0, 0);
            }
            finally
            {
                scrollPanel.ResumeLayout(false);
            }
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = "공사별 BarList";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 26);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "선택한 공사에 저장된 BarList 목록입니다. 신규 등록 후 검토 저장해야 이 목록에 반영됩니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(38, 72);
            parent.Controls.Add(desc);

            OviaProjectBarListButton defaultSize = new OviaProjectBarListButton();
            defaultSize.Text = "기본크기";
            defaultSize.Location = new Point(956, 36);
            defaultSize.Size = new Size(92, 34);
            defaultSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            defaultSize.StartColor = Color.FromArgb(55, 65, 95);
            defaultSize.EndColor = Color.FromArgb(37, 30, 130);
            defaultSize.Click += DefaultSize_Click;
            parent.Controls.Add(defaultSize);

            OviaProjectBarListButton close = new OviaProjectBarListButton();
            close.Text = "닫기";
            close.Location = new Point(1060, 36);
            close.Size = new Size(82, 34);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.StartColor = Color.FromArgb(120, 128, 150);
            close.EndColor = Color.FromArgb(85, 93, 115);
            close.Click += Close_Click;
            parent.Controls.Add(close);
        }

        private void BuildProjectInfo(Control parent)
        {
            OviaProjectBarListCard card = new OviaProjectBarListCard();
            card.Location = new Point(34, 105);
            card.Size = new Size(1108, 78);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            lblProjectTitle = new Label();
            lblProjectTitle.Text = projectNo + "  " + projectName;
            lblProjectTitle.AutoSize = true;
            lblProjectTitle.Font = new Font("맑은 고딕", 14F, FontStyle.Bold);
            lblProjectTitle.ForeColor = TextDark;
            lblProjectTitle.BackColor = Color.White;
            lblProjectTitle.Location = new Point(22, 15);
            card.Controls.Add(lblProjectTitle);

            lblProjectSub = new Label();
            lblProjectSub.Text = "거래처: " + clientName + "   |   상태: " + projectStatus;
            lblProjectSub.AutoSize = true;
            lblProjectSub.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            lblProjectSub.ForeColor = TextSub;
            lblProjectSub.BackColor = Color.White;
            lblProjectSub.Location = new Point(24, 48);
            card.Controls.Add(lblProjectSub);
        }

        private void BuildToolbar(Control parent)
        {
            OviaProjectBarListCard card = new OviaProjectBarListCard();
            card.Location = new Point(34, 198);
            card.Size = new Size(1108, 92);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            OviaProjectBarListButton newButton = new OviaProjectBarListButton();
            newButton.Text = "신규 BarList 등록";
            newButton.Location = new Point(22, 28);
            newButton.Size = new Size(145, 36);
            newButton.StartColor = BrandCyan;
            newButton.EndColor = BrandViolet;
            newButton.Click += NewButton_Click;
            card.Controls.Add(newButton);

            OviaProjectBarListButton openButton = new OviaProjectBarListButton();
            openButton.Text = "선택 BarList 열기";
            openButton.Location = new Point(180, 28);
            openButton.Size = new Size(145, 36);
            openButton.StartColor = BrandViolet;
            openButton.EndColor = BrandIndigo;
            openButton.Click += OpenButton_Click;
            card.Controls.Add(openButton);

            OviaProjectBarListButton refreshButton = new OviaProjectBarListButton();
            refreshButton.Text = "새로고침";
            refreshButton.Location = new Point(338, 28);
            refreshButton.Size = new Size(95, 36);
            refreshButton.StartColor = Color.FromArgb(108, 117, 145);
            refreshButton.EndColor = Color.FromArgb(78, 86, 110);
            refreshButton.Click += RefreshButton_Click;
            card.Controls.Add(refreshButton);

            Label guide = new Label();
            guide.Text = "주의: AutoCAD에서 가져온 데이터는 반드시 도면의 BarList와 비교 확인 후 저장하세요. 저장 전 후보 데이터는 이 목록에 표시되지 않습니다.";
            guide.AutoSize = false;
            guide.Size = new Size(620, 38);
            guide.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            guide.ForeColor = Color.FromArgb(210, 78, 78);
            guide.BackColor = Color.FromArgb(255, 248, 230);
            guide.Location = new Point(455, 27);
            guide.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(guide);
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 312);
            grid.Size = new Size(1108, 320);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.CellDoubleClick += Grid_CellDoubleClick;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 241, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 30;

            AddColumn("상태", 70);
            AddColumn("제목", 260);
            AddColumn("등록일", 125);
            AddColumn("수정일", 125);
            AddColumn("행수", 70);
            AddColumn("총수량", 90);
            AddColumn("총길이(M)", 110);
            AddColumn("중량(Ton)", 110);
            AddColumn("작성자", 90);
            AddColumn("비고", 130);
            AddColumn("FilePath", 0);

            grid.Columns["FilePath"].Visible = false;

            parent.Controls.Add(grid);
        }

        private void AddColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = Math.Max(5, width);
            column.FillWeight = Math.Max(1, width);
            column.MinimumWidth = width <= 0 ? 5 : 45;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.Resizable = DataGridViewTriState.True;
            grid.Columns.Add(column);
        }

        private void BuildFooter(Control parent)
        {
            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(38, 660);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parent.Controls.Add(lblStatus);
        }

        private void BindBarListRows()
        {
            grid.Rows.Clear();

            List<ProjectBarListSummary> list = GetBarListSummaries();

            int i;

            for (i = 0; i < list.Count; i++)
            {
                grid.Rows.Add(
                    list[i].Status,
                    list[i].Title,
                    list[i].CreatedDate,
                    list[i].ModifiedDate,
                    list[i].RowCount.ToString(),
                    list[i].TotalQty.ToString("0.###"),
                    list[i].TotalLength.ToString("0.###"),
                    list[i].TotalWeight.ToString("0.###"),
                    list[i].Writer,
                    list[i].Memo,
                    list[i].FilePath
                );
            }

            lblStatus.Text = "저장된 BarList: " + list.Count.ToString() + "건";
        }

        private List<ProjectBarListSummary> GetBarListSummaries()
        {
            List<ProjectBarListSummary> list = new List<ProjectBarListSummary>();
            string dir = GetProjectBarListDirectory();

            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "BarList_*.csv");

            int i;

            for (i = 0; i < files.Length; i++)
            {
                list.Add(BuildSummary(files[i]));
            }

            list.Sort(delegate (ProjectBarListSummary a, ProjectBarListSummary b)
            {
                DateTime at;
                DateTime bt;

                DateTime.TryParse(a.ModifiedDate, out at);
                DateTime.TryParse(b.ModifiedDate, out bt);

                return bt.CompareTo(at);
            });

            return list;
        }

        private ProjectBarListSummary BuildSummary(string filePath)
        {
            ProjectBarListSummary summary = new ProjectBarListSummary();

            summary.FilePath = filePath;
            summary.Title = Path.GetFileNameWithoutExtension(filePath);
            summary.CreatedDate = File.GetCreationTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.ModifiedDate = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.Status = "저장";
            summary.Writer = Environment.UserName;
            summary.Memo = "";

            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count > 1)
                {
                    List<string> headers = rows[0];

                    int qtyIndex = FindHeaderIndex(headers, "수량");
                    int totalLengthIndex = FindHeaderIndex(headers, "총길이");
                    int weightIndex = FindHeaderIndex(headers, "중량");

                    int r;

                    for (r = 1; r < rows.Count; r++)
                    {
                        summary.RowCount++;

                        if (qtyIndex >= 0 && qtyIndex < rows[r].Count)
                        {
                            summary.TotalQty += ParseNumber(rows[r][qtyIndex]);
                        }

                        if (totalLengthIndex >= 0 && totalLengthIndex < rows[r].Count)
                        {
                            summary.TotalLength += ParseNumber(rows[r][totalLengthIndex]);
                        }

                        if (weightIndex >= 0 && weightIndex < rows[r].Count)
                        {
                            summary.TotalWeight += ParseNumber(rows[r][weightIndex]);
                        }
                    }
                }
            }
            catch
            {
                summary.Memo = "요약 계산 실패";
            }

            return summary;
        }

        private int FindHeaderIndex(List<string> headers, string keyword)
        {
            int i;

            for (i = 0; i < headers.Count; i++)
            {
                if (headers[i] != null && headers[i].IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
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

        private void NewButton_Click(object sender, EventArgs e)
        {
            FrmBarList form = new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus);
            form.ShowDialog(this);

            BindBarListRows();
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            OpenSelectedBarList();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OpenSelectedBarList();
        }

        private void OpenSelectedBarList()
        {
            if (grid.SelectedRows.Count == 0)
            {
                lblStatus.Text = "열 BarList를 선택해주세요.";
                lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
                return;
            }

            object value = grid.SelectedRows[0].Cells["FilePath"].Value;

            if (value == null || value.ToString().Trim() == "")
            {
                lblStatus.Text = "BarList 파일 경로를 찾지 못했습니다.";
                lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
                return;
            }

            string filePath = value.ToString();

            if (!File.Exists(filePath))
            {
                lblStatus.Text = "저장된 BarList 파일이 존재하지 않습니다.";
                lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
                return;
            }

            FrmBarList form = new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath);
            form.ShowDialog(this);

            BindBarListRows();
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            BindBarListRows();
        }

        private void DefaultSize_Click(object sender, EventArgs e)
        {
            RestoreDefaultWindowSize();
        }

        private void RestoreDefaultWindowSize()
        {
            if (this.WindowState != FormWindowState.Normal)
            {
                this.WindowState = FormWindowState.Normal;
            }

            Rectangle workArea = Screen.FromControl(this).WorkingArea;

            if (scrollPanel != null && !scrollPanel.IsDisposed)
            {
                scrollPanel.AutoScroll = false;
                scrollPanel.AutoScrollMinSize = Size.Empty;
                scrollPanel.AutoScrollPosition = new Point(0, 0);
            }

            if (contentPanel != null && !contentPanel.IsDisposed)
            {
                contentPanel.Location = new Point(0, 0);
            }

            this.ClientSize = new Size(BaseClientWidth, BaseClientHeight);

            int left = this.Left;
            int top = this.Top;

            if (left < workArea.Left)
            {
                left = workArea.Left;
            }

            if (top < workArea.Top)
            {
                top = workArea.Top;
            }

            if (left + this.Width > workArea.Right)
            {
                left = Math.Max(workArea.Left, workArea.Right - this.Width);
            }

            if (top + this.Height > workArea.Bottom)
            {
                top = Math.Max(workArea.Top, workArea.Bottom - this.Height);
            }

            this.Location = new Point(left, top);

            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            QueueResetScrollToTopLeft();
        }

        private void Close_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }

    public class ProjectBarListSummary
    {
        public string FilePath = "";
        public string Status = "";
        public string Title = "";
        public string CreatedDate = "";
        public string ModifiedDate = "";
        public int RowCount = 0;
        public double TotalQty = 0;
        public double TotalLength = 0;
        public double TotalWeight = 0;
        public string Writer = "";
        public string Memo = "";
    }

    public class OviaProjectBarListCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

        public OviaProjectBarListCard()
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

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, 14))
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

    public class OviaProjectBarListButton : Control
    {
        public Color StartColor = Color.FromArgb(91, 49, 225);
        public Color EndColor = Color.FromArgb(37, 30, 130);

        private bool hover;

        public OviaProjectBarListButton()
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

            using (GraphicsPath path = OviaProjectBarListDrawHelper.RoundRect(rect, 7))
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

    public static class OviaProjectBarListDrawHelper
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
