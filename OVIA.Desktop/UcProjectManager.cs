using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class UcProjectManager : UserControl
    {
        public event Action<OviaProjectInfo> ProjectSelected;

        private TextBox txtSearch;
        private ComboBox cboSort;
        private CheckBox chkIncludeDone;
        private DataGridView grid;
        private Label lblStatus;

        private List<OviaProjectInfo> allProjects = new List<OviaProjectInfo>();

        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);
        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);

        public UcProjectManager()
        {
            BuildUI();
            allProjects = OviaLocalStore.GetSampleProjects();
            BindProjects();
        }

        private void BuildUI()
        {
            this.BackColor = SurfaceColor;
            this.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = "공사관리";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 28);
            this.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "공사를 선택하면 해당 공사의 BarList 목록으로 이동합니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(38, 75);
            this.Controls.Add(desc);

            BuildSearchArea();
            BuildGrid();

            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(38, 655);
            this.Controls.Add(lblStatus);
        }

        private void BuildSearchArea()
        {
            OviaUiCard card = new OviaUiCard();
            card.Location = new Point(34, 110);
            card.Size = new Size(1050, 98);
            card.SurfaceColor = SurfaceColor;
            this.Controls.Add(card);

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
            txtSearch.Size = new Size(390, 23);
            txtSearch.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtSearch.TextChanged += FilterChanged;
            card.Controls.Add(txtSearch);

            Label sortLabel = new Label();
            sortLabel.Text = "정렬";
            sortLabel.AutoSize = true;
            sortLabel.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            sortLabel.ForeColor = TextSub;
            sortLabel.BackColor = Color.White;
            sortLabel.Location = new Point(435, 17);
            card.Controls.Add(sortLabel);

            cboSort = new ComboBox();
            cboSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSort.Items.Add("최근작업순");
            cboSort.Items.Add("생성순");
            cboSort.Items.Add("명칭순");
            cboSort.Items.Add("번호순");
            cboSort.SelectedIndex = 0;
            cboSort.Location = new Point(435, 44);
            cboSort.Size = new Size(150, 23);
            cboSort.SelectedIndexChanged += FilterChanged;
            card.Controls.Add(cboSort);

            chkIncludeDone = new CheckBox();
            chkIncludeDone.Text = "완료공사 포함";
            chkIncludeDone.AutoSize = true;
            chkIncludeDone.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            chkIncludeDone.ForeColor = TextDark;
            chkIncludeDone.BackColor = Color.White;
            chkIncludeDone.Location = new Point(610, 46);
            chkIncludeDone.CheckedChanged += FilterChanged;
            card.Controls.Add(chkIncludeDone);

            OviaUiButton openButton = new OviaUiButton();
            openButton.Text = "선택한 공사 열기";
            openButton.Location = new Point(790, 37);
            openButton.Size = new Size(145, 34);
            openButton.StartColor = BrandCyan;
            openButton.EndColor = BrandViolet;
            openButton.Click += OpenButton_Click;
            card.Controls.Add(openButton);
        }

        private void BuildGrid()
        {
            grid = new DataGridView();
            grid.Location = new Point(34, 230);
            grid.Size = new Size(1050, 400);
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
            AddColumn("거래처", 170);
            AddColumn("상태", 80);
            AddColumn("생성일", 105);
            AddColumn("최근작업일", 115);
            AddColumn("담당자", 90);
            AddColumn("비고", 160);

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

        private void BindProjects()
        {
            List<OviaProjectInfo> list = GetFilteredProjects();

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

        private List<OviaProjectInfo> GetFilteredProjects()
        {
            List<OviaProjectInfo> list = new List<OviaProjectInfo>();
            string keyword = txtSearch == null ? "" : txtSearch.Text.Trim();

            int i;

            for (i = 0; i < allProjects.Count; i++)
            {
                OviaProjectInfo row = allProjects[i];

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

            list.Sort(delegate (OviaProjectInfo a, OviaProjectInfo b)
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

        private void FilterChanged(object sender, EventArgs e)
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

        private void OpenButton_Click(object sender, EventArgs e)
        {
            OpenSelectedProject();
        }

        private void OpenSelectedProject()
        {
            if (grid.SelectedRows.Count == 0)
            {
                return;
            }

            OviaProjectInfo project = new OviaProjectInfo();

            project.ProjectNo = GetSelectedCellText("공사번호");
            project.ProjectName = GetSelectedCellText("공사명");
            project.ClientName = GetSelectedCellText("거래처");
            project.Status = GetSelectedCellText("상태");
            project.CreatedDate = GetSelectedCellText("생성일");
            project.LastWorkDate = GetSelectedCellText("최근작업일");
            project.Manager = GetSelectedCellText("담당자");
            project.Memo = GetSelectedCellText("비고");

            if (ProjectSelected != null)
            {
                ProjectSelected(project);
            }
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
    }
}
