using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class UcProjectBarListList : UserControl
    {
        public event Action<OviaProjectInfo> BackToProjectList;
        public event Action<OviaProjectInfo> NewBarListRequested;
        public event Action<OviaProjectInfo, string> BarListOpenRequested;

        private readonly OviaProjectInfo project;

        private DataGridView grid;
        private Label lblTitle;
        private Label lblSub;
        private Label lblStatus;

        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);
        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);

        public UcProjectBarListList(OviaProjectInfo project)
        {
            this.project = project;
            BuildUI();
            BindBarLists();
        }

        private void BuildUI()
        {
            this.BackColor = SurfaceColor;
            this.Dock = DockStyle.Fill;

            lblTitle = new Label();
            lblTitle.Text = project.DisplayName;
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("맑은 고딕", 20F, FontStyle.Bold);
            lblTitle.ForeColor = TextDark;
            lblTitle.BackColor = SurfaceColor;
            lblTitle.Location = new Point(34, 25);
            this.Controls.Add(lblTitle);

            lblSub = new Label();
            lblSub.Text = "공사별로 저장된 BarList 목록입니다. 신규 등록 후 검토 저장해야 이 목록에 반영됩니다.";
            lblSub.AutoSize = true;
            lblSub.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            lblSub.ForeColor = TextSub;
            lblSub.BackColor = SurfaceColor;
            lblSub.Location = new Point(38, 70);
            this.Controls.Add(lblSub);

            BuildToolbar();
            BuildGrid();

            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(38, 655);
            this.Controls.Add(lblStatus);
        }

        private void BuildToolbar()
        {
            OviaUiCard card = new OviaUiCard();
            card.Location = new Point(34, 110);
            card.Size = new Size(1050, 92);
            card.SurfaceColor = SurfaceColor;
            this.Controls.Add(card);

            OviaUiButton backButton = new OviaUiButton();
            backButton.Text = "공사목록";
            backButton.Location = new Point(22, 30);
            backButton.Size = new Size(95, 34);
            backButton.StartColor = Color.FromArgb(120, 128, 150);
            backButton.EndColor = Color.FromArgb(85, 93, 115);
            backButton.Click += BackButton_Click;
            card.Controls.Add(backButton);

            OviaUiButton newButton = new OviaUiButton();
            newButton.Text = "신규 BarList 등록";
            newButton.Location = new Point(135, 30);
            newButton.Size = new Size(145, 34);
            newButton.StartColor = BrandCyan;
            newButton.EndColor = BrandViolet;
            newButton.Click += NewButton_Click;
            card.Controls.Add(newButton);

            OviaUiButton refreshButton = new OviaUiButton();
            refreshButton.Text = "새로고침";
            refreshButton.Location = new Point(298, 30);
            refreshButton.Size = new Size(95, 34);
            refreshButton.StartColor = BrandViolet;
            refreshButton.EndColor = BrandIndigo;
            refreshButton.Click += RefreshButton_Click;
            card.Controls.Add(refreshButton);

            Label guide = new Label();
            guide.Text = "저장된 BarList를 더블클릭하면 상세 내역을 확인합니다. 저장되지 않은 AutoCAD 추출 후보는 이 목록에 표시되지 않습니다.";
            guide.AutoSize = true;
            guide.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            guide.ForeColor = TextSub;
            guide.BackColor = Color.White;
            guide.Location = new Point(420, 38);
            card.Controls.Add(guide);
        }

        private void BuildGrid()
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 225);
            grid.Size = new Size(1050, 410);
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

            AddColumn("상태", 70);
            AddColumn("제목", 220);
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

            this.Controls.Add(grid);
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

        private void BindBarLists()
        {
            grid.Rows.Clear();

            List<OviaBarListSummary> list = OviaLocalStore.GetBarListSummaries(project);

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
                    list[i].Note,
                    list[i].FilePath
                );
            }

            lblStatus.Text = "저장된 BarList: " + list.Count.ToString() + "건";
        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            if (BackToProjectList != null)
            {
                BackToProjectList(project);
            }
        }

        private void NewButton_Click(object sender, EventArgs e)
        {
            if (NewBarListRequested != null)
            {
                NewBarListRequested(project);
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            BindBarLists();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            string filePath = "";

            object value = grid.Rows[e.RowIndex].Cells["FilePath"].Value;

            if (value != null)
            {
                filePath = value.ToString();
            }

            if (filePath == "")
            {
                return;
            }

            if (BarListOpenRequested != null)
            {
                BarListOpenRequested(project, filePath);
            }
        }
    }
}
