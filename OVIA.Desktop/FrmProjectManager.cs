using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class FrmProjectManager : Form
    {
        private readonly string companyId;
        private readonly string userId;

        private TextBox txtSearch;
        private ComboBox cboSort;
        private CheckBox chkIncludeDone;
        private DataGridView grid;
        private Label lblStatus;

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);

        private List<OviaProjectRow> allProjects = new List<OviaProjectRow>();

        public FrmProjectManager(string companyId, string userId)
        {
            this.companyId = companyId;
            this.userId = userId;

            BuildUI();
            LoadSampleProjects();
            BindProjects();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            this.Text = "OVIA 공사관리";
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1180, 720);
            this.BackColor = SurfaceColor;

            Panel bg = new Panel();
            bg.Dock = DockStyle.Fill;
            bg.BackColor = SurfaceColor;
            this.Controls.Add(bg);

            BuildHeader(bg);
            BuildSearchArea(bg);
            BuildProjectGrid(bg);
            BuildFooter(bg);

            this.ResumeLayout(false);
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = "공사관리";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 26);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "진행 중인 공사를 검색하고 선택합니다. 이후 ERP 연동 시 거래처/공사 정보를 자동으로 불러옵니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(38, 72);
            parent.Controls.Add(desc);

            OviaProjectButton close = new OviaProjectButton();
            close.Text = "닫기";
            close.Location = new Point(1060, 36);
            close.Size = new Size(82, 34);
            close.StartColor = Color.FromArgb(120, 128, 150);
            close.EndColor = Color.FromArgb(85, 93, 115);
            close.Click += Close_Click;
            parent.Controls.Add(close);
        }

        private void BuildSearchArea(Control parent)
        {
            OviaProjectCard card = new OviaProjectCard();
            card.Location = new Point(34, 105);
            card.Size = new Size(1108, 108);
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            Label searchLabel = new Label();
            searchLabel.Text = "공사 검색";
            searchLabel.AutoSize = true;
            searchLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            searchLabel.ForeColor = TextSub;
            searchLabel.BackColor = Color.White;
            searchLabel.Location = new Point(22, 17);
            card.Controls.Add(searchLabel);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(22, 44);
            txtSearch.Size = new Size(380, 23);
            txtSearch.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtSearch.TextChanged += Filter_Changed;
            card.Controls.Add(txtSearch);

            Label sortLabel = new Label();
            sortLabel.Text = "정렬";
            sortLabel.AutoSize = true;
            sortLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            sortLabel.ForeColor = TextSub;
            sortLabel.BackColor = Color.White;
            sortLabel.Location = new Point(430, 17);
            card.Controls.Add(sortLabel);

            cboSort = new ComboBox();
            cboSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSort.Items.Add("최근작업순");
            cboSort.Items.Add("생성순");
            cboSort.Items.Add("명칭순");
            cboSort.Items.Add("번호순");
            cboSort.SelectedIndex = 0;
            cboSort.Location = new Point(430, 44);
            cboSort.Size = new Size(150, 23);
            cboSort.SelectedIndexChanged += Filter_Changed;
            card.Controls.Add(cboSort);

            chkIncludeDone = new CheckBox();
            chkIncludeDone.Text = "완료공사 포함";
            chkIncludeDone.AutoSize = true;
            chkIncludeDone.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            chkIncludeDone.ForeColor = TextDark;
            chkIncludeDone.BackColor = Color.White;
            chkIncludeDone.Location = new Point(610, 46);
            chkIncludeDone.CheckedChanged += Filter_Changed;
            card.Controls.Add(chkIncludeDone);

            OviaProjectButton openButton = new OviaProjectButton();
            openButton.Text = "선택한 공사 열기";
            openButton.Location = new Point(800, 38);
            openButton.Size = new Size(145, 36);
            openButton.StartColor = BrandCyan;
            openButton.EndColor = BrandViolet;
            openButton.Click += OpenSelectedProject_Click;
            card.Controls.Add(openButton);

            OviaProjectButton newButton = new OviaProjectButton();
            newButton.Text = "새 공사";
            newButton.Location = new Point(965, 38);
            newButton.Size = new Size(105, 36);
            newButton.StartColor = BrandViolet;
            newButton.EndColor = BrandIndigo;
            newButton.Click += NewProject_Click;
            card.Controls.Add(newButton);
        }

        private void BuildProjectGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 235);
            grid.Size = new Size(1108, 390);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
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

            AddColumn("공사번호", 90);
            AddColumn("공사명", 320);
            AddColumn("거래처", 180);
            AddColumn("상태", 80);
            AddColumn("생성일", 110);
            AddColumn("최근작업일", 120);
            AddColumn("담당자", 90);
            AddColumn("비고", 190);

            parent.Controls.Add(grid);
        }

        private void AddColumn(string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = header;
            column.HeaderText = header;
            column.Width = width;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.Resizable = DataGridViewTriState.True;
            grid.Columns.Add(column);
        }

        private void BuildFooter(Control parent)
        {
            lblStatus = new Label();
            lblStatus.Text = "※ 현재 공사 목록은 임시 샘플입니다. 추후 셀먼 ERP/API에서 거래처와 공사 정보를 불러옵니다.";
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(38, 655);
            parent.Controls.Add(lblStatus);
        }

        private void LoadSampleProjects()
        {
            allProjects.Clear();

            allProjects.Add(new OviaProjectRow("1538", "2026_공장판매", "셀먼", "진행", "2026-04-28", "2026-05-20", "임대표", "최근 추출 테스트"));
            allProjects.Add(new OviaProjectRow("1606", "광양 홍숭 수성복합 신축공사", "현대건설", "진행", "2026-04-15", "2026-05-18", "김팀장", ""));
            allProjects.Add(new OviaProjectRow("1618", "나주 봉황 참송 이앤씨", "나주현장", "진행", "2026-05-01", "2026-05-14", "관리자", ""));
            allProjects.Add(new OviaProjectRow("1523", "고창 프로젝트", "거래처A", "완료", "2026-03-02", "2026-04-10", "관리자", "완료공사"));
            allProjects.Add(new OviaProjectRow("1637", "광주 상무 오피스텔", "거래처B", "진행", "2026-05-11", "2026-05-19", "관리자", ""));
        }

        private void BindProjects()
        {
            List<OviaProjectRow> list = GetFilteredProjects();

            grid.Rows.Clear();

            int i;

            for (i = 0; i < list.Count; i++)
            {
                grid.Rows.Add(
                    list[i].ProjectNo,
                    list[i].ProjectName,
                    list[i].ClientName,
                    list[i].Status,
                    list[i].CreatedDate,
                    list[i].LastWorkDate,
                    list[i].Manager,
                    list[i].Memo
                );
            }

            lblStatus.Text = "검색 결과: " + list.Count.ToString() + "건";
        }

        private List<OviaProjectRow> GetFilteredProjects()
        {
            List<OviaProjectRow> list = new List<OviaProjectRow>();
            string keyword = "";

            if (txtSearch != null && txtSearch.Text != null)
            {
                keyword = txtSearch.Text.Trim();
            }

            int i;

            for (i = 0; i < allProjects.Count; i++)
            {
                OviaProjectRow row = allProjects[i];

                if (!chkIncludeDone.Checked && row.Status == "완료")
                {
                    continue;
                }

                if (keyword != "")
                {
                    string target = row.ProjectNo + " " + row.ProjectName + " " + row.ClientName + " " + row.Manager;

                    if (target.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                list.Add(row);
            }

            string sort = cboSort.SelectedItem == null ? "최근작업순" : cboSort.SelectedItem.ToString();

            list.Sort(delegate (OviaProjectRow a, OviaProjectRow b)
            {
                if (sort == "명칭순")
                {
                    return string.Compare(a.ProjectName, b.ProjectName, StringComparison.CurrentCultureIgnoreCase);
                }

                if (sort == "번호순")
                {
                    return string.Compare(a.ProjectNo, b.ProjectNo, StringComparison.CurrentCultureIgnoreCase);
                }

                if (sort == "생성순")
                {
                    return string.Compare(b.CreatedDate, a.CreatedDate, StringComparison.CurrentCultureIgnoreCase);
                }

                return string.Compare(b.LastWorkDate, a.LastWorkDate, StringComparison.CurrentCultureIgnoreCase);
            });

            return list;
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            BindProjects();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OpenSelectedProject();
        }

        private void OpenSelectedProject_Click(object sender, EventArgs e)
        {
            OpenSelectedProject();
        }

        private void OpenSelectedProject()
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    "공사를 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string projectNo = GetSelectedCellText("공사번호");
            string projectName = GetSelectedCellText("공사명");
            string clientName = GetSelectedCellText("거래처");
            string status = GetSelectedCellText("상태");

            FrmBarList barList = new FrmBarList(companyId, userId, projectNo, projectName, clientName, status);
            barList.ShowDialog(this);
        }

        private string GetSelectedCellText(string columnName)
        {
            if (grid.SelectedRows.Count == 0)
            {
                return "";
            }

            if (!grid.Columns.Contains(columnName))
            {
                return "";
            }

            object value = grid.SelectedRows[0].Cells[columnName].Value;

            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

        private void NewProject_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "새 공사 등록은 다음 단계에서 셀먼 ERP/API 연동 구조와 함께 구현합니다.",
                "OVIA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void Close_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }

    public class OviaProjectRow
    {
        public string ProjectNo = "";
        public string ProjectName = "";
        public string ClientName = "";
        public string Status = "";
        public string CreatedDate = "";
        public string LastWorkDate = "";
        public string Manager = "";
        public string Memo = "";

        public OviaProjectRow(
            string projectNo,
            string projectName,
            string clientName,
            string status,
            string createdDate,
            string lastWorkDate,
            string manager,
            string memo
        )
        {
            ProjectNo = projectNo;
            ProjectName = projectName;
            ClientName = clientName;
            Status = status;
            CreatedDate = createdDate;
            LastWorkDate = lastWorkDate;
            Manager = manager;
            Memo = memo;
        }
    }

    public class OviaProjectCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

        public OviaProjectCard()
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

            using (GraphicsPath path = OviaProjectDrawHelper.RoundRect(rect, 14))
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

    public class OviaProjectButton : Control
    {
        public Color StartColor = Color.FromArgb(91, 49, 225);
        public Color EndColor = Color.FromArgb(37, 30, 130);

        private bool hover;

        public OviaProjectButton()
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

            using (GraphicsPath path = OviaProjectDrawHelper.RoundRect(rect, 7))
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

    public static class OviaProjectDrawHelper
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
