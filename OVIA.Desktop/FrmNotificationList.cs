using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmNotificationList : Form, IOviaWorkspaceLayout
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private DataGridView grid;
        private Label lblTitle;
        private Label lblDesc;
        private Panel pagerPanel;
        private Button btnToggleSelectAll;
        private Button btnConfirmSelected;
        private OviaContentLoadingOverlay contentLoadingOverlay;
        private List<OviaNotificationEntry> allEntries = new List<OviaNotificationEntry>();
        private int pageSize = 100;
        private int currentPage = 1;

        public FrmNotificationList(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;

            BuildUI();
            LoadEntries();
        }

        private void BuildUI()
        {
            SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            Text = "OVIA - 알림";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1100, 750);
            BackColor = SurfaceColor;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildToolbar(this);
            BuildGrid(this);
            BuildPager(this);
            BuildContentLoadingOverlay();

            ResumeLayout(false);
            ApplyWorkspaceLayout();
        }


        private void BuildContentLoadingOverlay()
        {
            contentLoadingOverlay = new OviaContentLoadingOverlay();
            this.Controls.Add(contentLoadingOverlay);
            contentLoadingOverlay.BringToFront();
        }

        private void BeginContentLoading()
        {
            if (contentLoadingOverlay != null)
            {
                contentLoadingOverlay.BeginLoading();
            }
        }

        private void EndContentLoading()
        {
            if (contentLoadingOverlay != null)
            {
                contentLoadingOverlay.EndLoading();
            }
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인  ›  알림",
                delegate { NavigateToMain(); },
                delegate { NavigateToMain(); },
                delegate { LoadEntries(); },
                null,
                true,
                true,
                delegate(string target)
                {
                    if (target == "MAIN")
                    {
                        NavigateToMain();
                    }
                }
            );
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1180, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += delegate(object sender, PaintEventArgs e)
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
            };
            OviaWorkspaceCommandBar.Populate(commandBar, "MAIN", companyId, userId);
            parent.Controls.Add(commandBar);
        }

        private void BuildTitle(Control parent)
        {
            lblTitle = new Label();
            lblTitle.Text = "알림";
            lblTitle.AutoSize = false;
            lblTitle.Location = new Point(34, 120);
            lblTitle.Size = new Size(300, 34);
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.Font = OviaFluentTheme.FontTitle(20F, FontStyle.Bold);
            lblTitle.ForeColor = TextDark;
            lblTitle.BackColor = SurfaceColor;
            parent.Controls.Add(lblTitle);

            lblDesc = new Label();
            lblDesc.Text = OviaSystemSettingsStore.IsSystemAdministrator(companyId, userId)
                ? "최고관리자는 모든 사용자의 최근 7일 작업 알림을 확인할 수 있습니다."
                : "최근 7일 동안 내가 저장하거나 삭제한 작업 알림을 확인할 수 있습니다.";
            lblDesc.AutoSize = false;
            lblDesc.Location = new Point(36, 158);
            lblDesc.Size = new Size(980, 24);
            lblDesc.TextAlign = ContentAlignment.MiddleLeft;
            lblDesc.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            lblDesc.ForeColor = TextSub;
            lblDesc.BackColor = SurfaceColor;
            parent.Controls.Add(lblDesc);
        }

        private void BuildToolbar(Control parent)
        {
            btnToggleSelectAll = CreateButton("전체 선택", 0, 116, 110, OviaButtonRole.Neutral);
            btnToggleSelectAll.Click += ToggleSelectAll_Click;
            parent.Controls.Add(btnToggleSelectAll);

            btnConfirmSelected = CreateButton("확인", btnToggleSelectAll.Right + 10, 122, 92, OviaButtonRole.Primary);
            btnConfirmSelected.Click += ConfirmSelected_Click;
            parent.Controls.Add(btnConfirmSelected);

            LayoutToolbarButtons();
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            grid.Location = new Point(0, 164);
            grid.Size = new Size(1180, 432);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoGenerateColumns = false;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.GridColor = OviaFluentTheme.CardBorder;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.Font = OviaFluentTheme.FontData(9F, FontStyle.Regular);
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = OviaFluentTheme.Accent;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 237, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 34;
            grid.CellClick += Grid_CellClick;
            grid.CellPainting += Grid_CellPainting;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellValueChanged += Grid_CellValueChanged;

            DataGridViewTextBoxColumn idColumn = new DataGridViewTextBoxColumn();
            idColumn.Name = "Id";
            idColumn.HeaderText = "Id";
            idColumn.Visible = false;
            grid.Columns.Add(idColumn);

            DataGridViewCheckBoxColumn checkColumn = new DataGridViewCheckBoxColumn();
            checkColumn.Name = "Check";
            checkColumn.HeaderText = "체크";
            checkColumn.Width = 54;
            checkColumn.TrueValue = true;
            checkColumn.FalseValue = false;
            checkColumn.HeaderCell.Style.BackColor = OviaFluentTheme.HeaderBackground;
            checkColumn.HeaderCell.Style.ForeColor = TextDark;
            checkColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(checkColumn);

            AddTextColumn("No", "순번", 64, DataGridViewContentAlignment.MiddleCenter);
            AddTextColumn("WorkContent", "작업내용", 240, DataGridViewContentAlignment.MiddleLeft);
            AddTextColumn("WorkPath", "경로", 330, DataGridViewContentAlignment.MiddleLeft);
            AddTextColumn("WorkDate", "일시", 150, DataGridViewContentAlignment.MiddleCenter);
            AddTextColumn("Worker", "작업자", 120, DataGridViewContentAlignment.MiddleCenter);

            DataGridViewButtonColumn confirmColumn = new DataGridViewButtonColumn();
            confirmColumn.Name = "Confirm";
            confirmColumn.HeaderText = "확인";
            confirmColumn.Width = 100;
            confirmColumn.UseColumnTextForButtonValue = false;
            grid.Columns.Add(confirmColumn);

            ApplyNotificationHeaderSelectionStyle();

            parent.Controls.Add(grid);
        }

        private void ApplyNotificationHeaderSelectionStyle()
        {
            if (grid == null)
            {
                return;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.Style.SelectionBackColor = OviaFluentTheme.Accent;
                grid.Columns[i].HeaderCell.Style.SelectionForeColor = Color.White;
            }
        }

        private void AddTextColumn(string name, string headerText, int width, DataGridViewContentAlignment alignment)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = true;
            column.DefaultCellStyle.Alignment = alignment;
            if (name == "WorkContent" || name == "WorkPath")
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.FillWeight = name == "WorkPath" ? 150 : 100;
            }
            grid.Columns.Add(column);
        }

        private void BuildPager(Control parent)
        {
            pagerPanel = new Panel();
            pagerPanel.Location = new Point(0, 612);
            pagerPanel.Size = new Size(1180, 38);
            pagerPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            pagerPanel.BackColor = SurfaceColor;
            parent.Controls.Add(pagerPanel);
        }

        private Button CreateButton(string text, int x, int y, int width, OviaButtonRole role)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, role);
            return button;
        }

        private void LoadEntries()
        {
            BeginContentLoading();
            try
            {
                pageSize = OviaSystemSettingsStore.GetListPageSize();
            allEntries = OviaNotificationStore.GetVisibleEntries(companyId, userId);

            int maxPage = GetMaxPage();
            if (currentPage > maxPage)
            {
                currentPage = maxPage;
            }
            if (currentPage < 1)
            {
                currentPage = 1;
            }

            BindCurrentPage();
            RefreshOpenNotificationBadges();
            }
            finally
            {
                EndContentLoading();
            }
        }

        private void BindCurrentPage()
        {
            if (grid == null)
            {
                return;
            }

            grid.Rows.Clear();

            int start = (currentPage - 1) * pageSize;
            int end = Math.Min(start + pageSize, allEntries.Count);
            int i;

            for (i = start; i < end; i++)
            {
                OviaNotificationEntry entry = allEntries[i];
                int rowIndex = grid.Rows.Add();
                DataGridViewRow row = grid.Rows[rowIndex];
                row.Cells["Id"].Value = entry.Id;
                row.Cells["Check"].Value = false;
                row.Cells["No"].Value = (i + 1).ToString();
                row.Cells["WorkContent"].Value = entry.WorkContent;
                row.Cells["WorkPath"].Value = entry.WorkPath;
                row.Cells["WorkDate"].Value = entry.WorkDate.ToString("yyyy-MM-dd HH:mm");
                row.Cells["Worker"].Value = entry.Worker;
                row.Cells["Confirm"].Value = entry.IsConfirmed ? "확인됨" : "확인";

                if (entry.IsConfirmed)
                {
                    row.DefaultCellStyle.ForeColor = TextSub;
                }
            }

            RenderPager();
            UpdateSelectAllButtonText();
        }

        private void RenderPager()
        {
            if (pagerPanel == null)
            {
                return;
            }

            pagerPanel.Controls.Clear();

            int maxPage = GetMaxPage();
            int left = 0;

            AddPagerButton("처음", 1, ref left, currentPage > 1);
            AddPagerButton("이전", currentPage - 1, ref left, currentPage > 1);

            int firstPage = Math.Max(1, currentPage - 2);
            int lastPage = Math.Min(maxPage, firstPage + 4);
            if (lastPage - firstPage < 4)
            {
                firstPage = Math.Max(1, lastPage - 4);
            }

            int i;
            for (i = firstPage; i <= lastPage; i++)
            {
                AddPagerButton(i.ToString(), i, ref left, true);
            }

            AddPagerButton("다음", currentPage + 1, ref left, currentPage < maxPage);
            AddPagerButton("끝", maxPage, ref left, currentPage < maxPage);
        }

        private void AddPagerButton(string text, int targetPage, ref int left, bool enabled)
        {
            bool isCurrentPage = targetPage == currentPage && IsNumericText(text);
            Button button = new Button();
            button.Text = text;
            button.Tag = targetPage;
            button.AutoSize = false;
            button.Size = MeasurePagerButtonSize(text);
            button.Location = new Point(left, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = enabled ? Color.FromArgb(208, 216, 226) : Color.FromArgb(222, 226, 232);
            button.FlatAppearance.MouseOverBackColor = isCurrentPage ? OviaFluentTheme.AccentHover : Color.FromArgb(241, 246, 255);
            button.FlatAppearance.MouseDownBackColor = isCurrentPage ? OviaFluentTheme.AccentHover : Color.FromArgb(226, 237, 255);
            button.BackColor = isCurrentPage ? OviaFluentTheme.Accent : Color.White;
            button.ForeColor = isCurrentPage ? Color.White : (enabled ? TextDark : TextSub);
            button.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            button.Enabled = enabled;
            button.UseVisualStyleBackColor = false;
            button.Click += Pager_Click;
            pagerPanel.Controls.Add(button);
            left += button.Width + 7;
        }

        private Size MeasurePagerButtonSize(string text)
        {
            Size textSize = TextRenderer.MeasureText(text, OviaFluentTheme.FontData(8.7F, FontStyle.Regular));
            return new Size(Math.Max(34, textSize.Width + 18), 28);
        }

        private bool IsNumericText(string text)
        {
            int value;
            return int.TryParse(text, out value);
        }

        private void Pager_Click(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control == null || control.Tag == null)
            {
                return;
            }

            int nextPage = Convert.ToInt32(control.Tag);
            nextPage = Math.Max(1, Math.Min(GetMaxPage(), nextPage));
            if (nextPage == currentPage)
            {
                return;
            }

            currentPage = nextPage;
            BindCurrentPage();
        }

        private int GetMaxPage()
        {
            if (pageSize <= 0)
            {
                pageSize = 100;
            }

            return Math.Max(1, (int)Math.Ceiling(allEntries.Count / (double)pageSize));
        }

        private void ToggleSelectAll_Click(object sender, EventArgs e)
        {
            if (grid == null)
            {
                return;
            }

            bool allChecked = AreAllVisibleRowsChecked();
            bool nextChecked = !allChecked;
            int i;

            for (i = 0; i < grid.Rows.Count; i++)
            {
                grid.Rows[i].Cells["Check"].Value = nextChecked;
            }

            UpdateSelectAllButtonText();
            InvalidateCheckColumn();
        }

        private void InvalidateCheckColumn()
        {
            if (grid == null || !grid.Columns.Contains("Check"))
            {
                return;
            }

            grid.InvalidateColumn(grid.Columns["Check"].Index);
        }

        private bool AreAllVisibleRowsChecked()
        {
            if (grid == null || grid.Rows.Count == 0)
            {
                return false;
            }

            int i;
            for (i = 0; i < grid.Rows.Count; i++)
            {
                object value = grid.Rows[i].Cells["Check"].Value;
                if (!(value is bool) || !(bool)value)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateSelectAllButtonText()
        {
            if (btnToggleSelectAll == null)
            {
                return;
            }

            btnToggleSelectAll.Text = AreAllVisibleRowsChecked() ? "전체 해제" : "전체 선택";
            btnToggleSelectAll.Size = OviaFluentTheme.MeasureButtonSize(btnToggleSelectAll.Text);
            LayoutToolbarButtons();
        }

        private void LayoutToolbarButtons()
        {
            if (btnToggleSelectAll == null || btnConfirmSelected == null)
            {
                return;
            }

            btnConfirmSelected.Left = btnToggleSelectAll.Right + 10;
            btnConfirmSelected.Top = btnToggleSelectAll.Top;
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (grid == null || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.RowIndex == -1)
            {
                PaintColumnHeader(e);
                return;
            }

            string columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName == "Check")
            {
                PaintNotificationCheckBox(e);
                return;
            }

            if (columnName != "Confirm")
            {
                return;
            }

            bool confirmed = Convert.ToString(grid.Rows[e.RowIndex].Cells["Confirm"].Value) == "확인됨";
            bool selected = grid.Rows[e.RowIndex].Selected;
            Color cellBack = selected ? grid.DefaultCellStyle.SelectionBackColor : Color.White;

            using (SolidBrush backBrush = new SolidBrush(cellBack))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
            }

            using (Pen linePen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawLine(linePen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            int buttonWidth = confirmed ? 66 : 54;
            int buttonHeight = 22;
            Rectangle buttonRect = new Rectangle(
                e.CellBounds.Left + Math.Max(4, (e.CellBounds.Width - buttonWidth) / 2),
                e.CellBounds.Top + Math.Max(3, (e.CellBounds.Height - buttonHeight) / 2),
                buttonWidth,
                buttonHeight);

            Color fill = confirmed ? Color.FromArgb(226, 229, 234) : OviaFluentTheme.Accent;
            Color border = confirmed ? Color.FromArgb(205, 210, 218) : OviaFluentTheme.Accent;
            Color fore = confirmed ? TextSub : Color.White;

            using (GraphicsPath path = CreateRoundRectPath(new Rectangle(buttonRect.X, buttonRect.Y, buttonRect.Width - 1, buttonRect.Height - 1), 4))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border, 1))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                confirmed ? "확인됨" : "확인",
                OviaFluentTheme.FontData(8F, FontStyle.Regular),
                buttonRect,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            e.Handled = true;
        }

        private void PaintNotificationCheckBox(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
            Color cellBack = selected ? grid.DefaultCellStyle.SelectionBackColor : Color.White;

            using (SolidBrush backBrush = new SolidBrush(cellBack))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
            }

            using (Pen linePen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawLine(linePen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            bool isChecked = false;
            if (e.Value != null && e.Value != DBNull.Value)
            {
                bool.TryParse(e.Value.ToString(), out isChecked);
            }

            int boxSize = OviaFluentTheme.CheckBoxSize;
            Rectangle boxRect = new Rectangle(
                e.CellBounds.Left + (e.CellBounds.Width - boxSize) / 2,
                e.CellBounds.Top + (e.CellBounds.Height - boxSize) / 2,
                boxSize,
                boxSize);

            Color borderColor = isChecked ? OviaFluentTheme.CheckBoxCheckedBorder : OviaFluentTheme.ControlBorder;
            Color backColor = isChecked ? OviaFluentTheme.CheckBoxCheckedBack : Color.White;

            using (GraphicsPath path = CreateRoundRectPath(new Rectangle(boxRect.X, boxRect.Y, boxRect.Width - 1, boxRect.Height - 1), OviaFluentTheme.CheckBoxRadius))
            using (SolidBrush brush = new SolidBrush(backColor))
            using (Pen borderPen = new Pen(borderColor, 1F))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            if (isChecked)
            {
                using (Pen checkPen = new Pen(Color.White, 1.8F))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(checkPen, new PointF[]
                    {
                        new PointF(boxRect.Left + 3.5F, boxRect.Top + 7.5F),
                        new PointF(boxRect.Left + 6.5F, boxRect.Top + 10.5F),
                        new PointF(boxRect.Left + 11.5F, boxRect.Top + 4.5F)
                    });
                }
            }

            e.Handled = true;
        }

        private void PaintColumnHeader(DataGridViewCellPaintingEventArgs e)
        {
            bool activeColumn = grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == e.ColumnIndex;
            Color back = activeColumn ? OviaFluentTheme.Accent : OviaFluentTheme.HeaderBackground;
            Color fore = activeColumn ? Color.White : TextDark;

            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }

            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);
            }

            string headerText = grid.Columns[e.ColumnIndex].HeaderText;
            TextRenderer.DrawText(
                e.Graphics,
                headerText,
                OviaFluentTheme.FontData(9F, FontStyle.Bold),
                e.CellBounds,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            e.Handled = true;
        }

        private static GraphicsPath CreateRoundRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            Rectangle arc = new Rectangle(rect.Left, rect.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
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
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Check")
            {
                UpdateSelectAllButtonText();
                grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
            }
        }

        private void ConfirmSelected_Click(object sender, EventArgs e)
        {
            List<string> ids = new List<string>();
            int i;

            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

            for (i = 0; i < grid.Rows.Count; i++)
            {
                bool selected = false;
                object value = grid.Rows[i].Cells["Check"].Value;
                if (value is bool)
                {
                    selected = (bool)value;
                }

                if (selected)
                {
                    string id = Convert.ToString(grid.Rows[i].Cells["Id"].Value);
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            if (ids.Count == 0)
            {
                MessageBox.Show("확인 처리할 알림을 체크해 주세요.", "OVIA 알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OviaNotificationStore.ConfirmMany(ids);
            LoadEntries();
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (grid.Columns[e.ColumnIndex].Name != "Confirm")
            {
                return;
            }

            if (Convert.ToString(grid.Rows[e.RowIndex].Cells["Confirm"].Value) == "확인됨")
            {
                return;
            }

            string id = Convert.ToString(grid.Rows[e.RowIndex].Cells["Id"].Value);
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            OviaNotificationStore.Confirm(id);
            LoadEntries();
        }

        public void ApplyWorkspaceLayout()
        {
            const int toolbarTop = 116;
            const int gridTop = 164;
            const int contentInset = 25;
            const int pagerHeight = 38;
            const int bottomInset = 12;
            const int buttonGap = 10;

            int width = Math.Max(1, ClientSize.Width);
            int pagerTop = Math.Max(gridTop + 220, ClientSize.Height - pagerHeight - bottomInset);
            int gridY = gridTop + contentInset;
            int gridHeight = Math.Max(240, pagerTop - gridY - 10);

            if (btnToggleSelectAll != null)
            {
                btnToggleSelectAll.Location = new Point(contentInset, toolbarTop + contentInset);
            }
            if (btnConfirmSelected != null && btnToggleSelectAll != null)
            {
                btnConfirmSelected.Location = new Point(btnToggleSelectAll.Right + buttonGap, toolbarTop + contentInset);
            }

            if (grid != null)
            {
                grid.Location = new Point(contentInset, gridY);
                grid.Size = new Size(Math.Max(1, width - contentInset), Math.Max(1, gridHeight));
            }

            if (pagerPanel != null)
            {
                pagerPanel.Location = new Point(contentInset, pagerTop);
                pagerPanel.Width = Math.Max(1, width - contentInset);
            }

        }

        private void RefreshOpenNotificationBadges()
        {
            try
            {
                int i;
                for (i = 0; i < Application.OpenForms.Count; i++)
                {
                    RefreshNotificationBadgesInControl(Application.OpenForms[i]);
                }
            }
            catch
            {
            }
        }

        private void RefreshNotificationBadgesInControl(Control control)
        {
            if (control == null)
            {
                return;
            }

            OviaWorkspaceHeader header = control as OviaWorkspaceHeader;
            if (header != null)
            {
                header.RefreshNotificationBadge();
            }

            int i;
            for (i = 0; i < control.Controls.Count; i++)
            {
                RefreshNotificationBadgesInControl(control.Controls[i]);
            }
        }

        private void NavigateToMain()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateToMain();
                return;
            }

            Close();
        }
    }
}
