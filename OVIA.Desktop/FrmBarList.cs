using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class FrmBarList : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;
        private const int HeaderDragNone = 0;
        private const int HeaderDragRow = 1;
        private const int HeaderDragColumn = 2;

        private readonly string companyId;
        private readonly string userId;
        private readonly string projectNo;
        private readonly string projectName;
        private readonly string clientName;
        private readonly string projectStatus;

        private DataGridView grid;
        private ContextMenuStrip gridContextMenu;
        private ToolStripMenuItem undoMenuItem;
        private ToolStripMenuItem redoMenuItem;
        private List<GridUndoSnapshot> undoStates = new List<GridUndoSnapshot>();
        private List<GridUndoSnapshot> redoStates = new List<GridUndoSnapshot>();
        private GridUndoSnapshot cellEditBeforeSnapshot = null;
        private bool isRestoringGridState = false;
        private int gridRedrawLockCount = 0;
        private bool isBulkGridSelecting = false;
        private int selectedCellCountCache = 0;
        private int headerDragMode = HeaderDragNone;
        private int headerDragStartIndex = -1;
        private int headerDragLastIndex = -1;
        private int headerSelectionVersion = 0;
        private const int MaxUndoCount = 30;
        private bool allowExtractEditMenu = false;
        private TextBox txtFilePath;
        private Label lblRowCount;
        private Label lblTotalQty;
        private Label lblTotalLength;
        private Label lblTotalWeight;
        private Label lblStatus;
        private Label lblProjectTitle;
        private Label lblProjectSub;
        private Label lblSaveState;
        private ToolTip windowToolTip;
        private OviaBarListMappingStore mappingStore;
        private int lastMappingMatchCount = 0;
        private int lastMappingTotalHeaderCount = 0;
        private string lastMappingVersion = "";

        private FileSystemWatcher autoCadWatcher;
        private DateTime autoImportStartTime;
        private string lastLoadedFilePath = "";
        private bool waitingAutoCadImport = false;
        private bool isSaved = true;
        private bool isClosingByButton = false;
        private bool suppressUnsavedClosePrompt = false;
        private bool isInternalNavigation = false;
        private bool isBackNavigationQueued = false;
        private readonly string initialFilePath;
        private string savedProjectFilePath = "";

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);
        private readonly Color ModifiedCellTextColor = Color.FromArgb(220, 38, 38);

        private const int BaseClientWidth = 1240;
        private const int BaseClientHeight = 760;
        private Panel scrollPanel;
        private Panel contentPanel;
        private bool isScrollResetQueued = false;

        public FrmBarList(string companyId, string userId)
            : this(companyId, userId, "", "공사 미선택", "", "", "")
        {
        }

        public FrmBarList(string companyId, string userId, string projectNo, string projectName, string clientName, string projectStatus)
            : this(companyId, userId, projectNo, projectName, clientName, projectStatus, "")
        {
        }

        public FrmBarList(string companyId, string userId, string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath)
        {
            this.companyId = companyId;
            this.userId = userId;
            this.projectNo = projectNo == null ? "" : projectNo;
            this.projectName = projectName == null ? "" : projectName;
            this.clientName = clientName == null ? "" : clientName;
            this.projectStatus = projectStatus == null ? "" : projectStatus;
            this.initialFilePath = initialFilePath == null ? "" : initialFilePath;
            this.savedProjectFilePath = this.initialFilePath;

            BuildUI();

            if (this.initialFilePath.Trim() != "" && File.Exists(this.initialFilePath))
            {
                LoadCsv(this.initialFilePath, true);
            }
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            this.Text = "OVIA " + GetScreenTitleText();
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(BaseClientWidth, BaseClientHeight);
            this.MinimumSize = new Size(820, 540);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmBarList_FormClosing;

            windowToolTip = new ToolTip();
            windowToolTip.AutoPopDelay = 4000;
            windowToolTip.InitialDelay = 350;
            windowToolTip.ReshowDelay = 100;
            windowToolTip.ShowAlways = true;

            scrollPanel = new Panel();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.BackColor = SurfaceColor;
            scrollPanel.AutoScroll = true;
            scrollPanel.AutoScrollMinSize = new Size(0, BaseClientHeight);
            scrollPanel.HorizontalScroll.Enabled = false;
            scrollPanel.HorizontalScroll.Visible = false;
            scrollPanel.Resize += ScrollPanel_Resize;
            this.Controls.Add(scrollPanel);

            contentPanel = new Panel();
            contentPanel.Location = new Point(0, 0);
            contentPanel.Size = new Size(BaseClientWidth, BaseClientHeight);
            contentPanel.BackColor = SurfaceColor;
            scrollPanel.Controls.Add(contentPanel);

            BuildHeader(contentPanel);
            BuildProjectInfo(contentPanel);
            BuildFileBar(contentPanel);
            BuildSummary(contentPanel);
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

            bool needVerticalScroll = this.ClientSize.Height < BaseClientHeight;

            scrollPanel.SuspendLayout();

            try
            {
                if (needVerticalScroll)
                {
                    scrollPanel.AutoScroll = true;
                    scrollPanel.AutoScrollMinSize = new Size(0, BaseClientHeight);
                    scrollPanel.HorizontalScroll.Enabled = false;
                    scrollPanel.HorizontalScroll.Visible = false;

                    int width = Math.Max(1, scrollPanel.ClientSize.Width);
                    int height = Math.Max(BaseClientHeight, scrollPanel.ClientSize.Height);

                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(width, height);
                }
                else
                {
                    scrollPanel.AutoScroll = false;
                    scrollPanel.AutoScrollMinSize = Size.Empty;
                    contentPanel.Location = new Point(0, 0);
                    contentPanel.Size = new Size(Math.Max(1, scrollPanel.ClientSize.Width), Math.Max(1, scrollPanel.ClientSize.Height));
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

        private bool IsRegisteredBarListMode()
        {
            return initialFilePath.Trim() != "" && File.Exists(initialFilePath);
        }

        private string GetScreenTitleText()
        {
            if (IsRegisteredBarListMode())
            {
                return "BarList";
            }

            return "신규 BarList 등록";
        }

        private string GetScreenDescriptionText()
        {
            if (IsRegisteredBarListMode())
            {
                return "저장된 BarList를 열었습니다. 출고, 입금완료, 종료 처리 전까지 수정 후 다시 저장할 수 있습니다.";
            }

            return "공사를 선택한 뒤 AutoCAD에서 철근 집계표를 선택하면 BarList 후보가 자동 입력됩니다.";
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = GetScreenTitleText();
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(34, 22);
            parent.Controls.Add(title);

            Button help = CreateHelpIcon(GetScreenDescriptionText());
            help.Location = new Point(title.Right + 10, 34);
            parent.Controls.Add(help);

            LinkLabel breadcrumb = CreateBreadcrumbLabel();
            breadcrumb.Text = "공사관리  >  공사별 BarList  >  " + GetScreenTitleText();
            breadcrumb.Links.Add(0, "공사관리".Length, "PROJECT_MANAGER");

            int barListListStart = breadcrumb.Text.IndexOf("공사별 BarList");
            if (barListListStart >= 0)
            {
                breadcrumb.Links.Add(barListListStart, "공사별 BarList".Length, "PROJECT_BARLIST_LIST");
            }

            breadcrumb.LinkClicked += Breadcrumb_LinkClicked;
            parent.Controls.Add(breadcrumb);

            Button defaultSize = CreateDefaultSizeButton();
            defaultSize.Location = new Point(1166, 34);
            defaultSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            defaultSize.Click += DefaultSize_Click;
            parent.Controls.Add(defaultSize);
        }

        private LinkLabel CreateBreadcrumbLabel()
        {
            LinkLabel label = new LinkLabel();
            label.Text = "";
            label.AutoSize = false;
            label.Size = new Size(860, 22);
            label.Location = new Point(38, 68);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            label.BackColor = SurfaceColor;
            label.ForeColor = TextSub;
            label.LinkColor = Color.FromArgb(80, 88, 112);
            label.ActiveLinkColor = BrandViolet;
            label.VisitedLinkColor = Color.FromArgb(80, 88, 112);
            label.DisabledLinkColor = TextSub;
            label.TabStop = false;
            return label;
        }

        private Button CreateDefaultSizeButton()
        {
            Button button = new Button();
            button.Text = "";
            button.Size = new Size(34, 30);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(185, 192, 205);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 248, 252);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(232, 236, 244);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(138, 146, 160);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.Paint += DefaultSizeButton_Paint;

            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, "창 기본크기로");
            }

            return button;
        }

        private void DefaultSizeButton_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(Color.FromArgb(138, 146, 160), 1.6F))
            {
                Rectangle backRect = new Rectangle(12, 8, 13, 11);
                Rectangle frontRect = new Rectangle(8, 12, 13, 11);

                e.Graphics.DrawRectangle(pen, backRect);
                e.Graphics.DrawRectangle(pen, frontRect);
                e.Graphics.DrawLine(pen, frontRect.Left + 3, frontRect.Top + 3, frontRect.Right - 3, frontRect.Top + 3);
            }
        }

        private Button CreateHelpIcon(string helpText)
        {
            Button button = new Button();
            button.Text = "";
            button.Size = new Size(24, 24);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SurfaceColor;
            button.FlatAppearance.MouseDownBackColor = SurfaceColor;
            button.BackColor = SurfaceColor;
            button.ForeColor = Color.FromArgb(138, 146, 160);
            button.Cursor = Cursors.Help;
            button.TabStop = false;
            button.Paint += HelpIcon_Paint;

            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, helpText);
            }

            return button;
        }

        private void HelpIcon_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(2, 2, button.Width - 5, button.Height - 5);
            Color lineColor = Color.FromArgb(185, 192, 205);
            Color textColor = Color.FromArgb(138, 146, 160);

            using (SolidBrush fillBrush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(lineColor, 1.2F))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (Font font = new Font("맑은 고딕", 9F, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                e.Graphics.FillEllipse(fillBrush, rect);
                e.Graphics.DrawEllipse(pen, rect);
                e.Graphics.DrawString("?", font, textBrush, rect, format);
            }
        }

        private void Breadcrumb_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string target = e.Link.LinkData == null ? "" : e.Link.LinkData.ToString();

            if (target == "PROJECT_MANAGER")
            {
                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                FrmProjectManager form = new FrmProjectManager(companyId, userId);
                ShowReplacementWindow(form);
                return;
            }

            if (target == "PROJECT_BARLIST_LIST")
            {
                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                FrmProjectBarListList form = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus);
                ShowReplacementWindow(form);
            }
        }

        private void BuildProjectInfo(Control parent)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = new Point(34, 100);
            card.Size = new Size(1168, 72);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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
            lblSaveState.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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
            txtFilePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtFilePath.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            txtFilePath.ReadOnly = true;
            card.Controls.Add(txtFilePath);

            OviaBarListButton autoButton = new OviaBarListButton();
            autoButton.Text = "AutoCAD에서 가져오기";
            autoButton.Location = new Point(610, 36);
            autoButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            autoButton.Size = new Size(160, 34);
            autoButton.StartColor = BrandCyan;
            autoButton.EndColor = BrandViolet;
            autoButton.Click += AutoCadImport_Click;
            card.Controls.Add(autoButton);

            OviaBarListButton recentButton = new OviaBarListButton();
            recentButton.Text = "최근 추출";
            recentButton.Location = new Point(785, 36);
            recentButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            recentButton.Size = new Size(92, 34);
            recentButton.StartColor = Color.FromArgb(70, 130, 230);
            recentButton.EndColor = BrandViolet;
            recentButton.Click += LoadRecent_Click;
            card.Controls.Add(recentButton);

            OviaBarListButton openButton = new OviaBarListButton();
            openButton.Text = "CSV 선택";
            openButton.Location = new Point(890, 36);
            openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openButton.Size = new Size(92, 34);
            openButton.StartColor = BrandViolet;
            openButton.EndColor = BrandIndigo;
            openButton.Click += OpenCsv_Click;
            card.Controls.Add(openButton);

            OviaBarListButton saveProjectButton = new OviaBarListButton();
            saveProjectButton.Text = "검토 후 저장";
            saveProjectButton.Location = new Point(995, 36);
            saveProjectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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
            lblStatus.Size = new Size(240, 28);
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = Color.FromArgb(235, 241, 252);
            lblStatus.Location = new Point(948, 330);
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
            EnableGridDoubleBuffering(grid);
            grid.Location = new Point(34, 402);
            grid.Size = new Size(1168, 265);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = true;
            grid.RowHeadersWidth = 48;
            grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 252);
            grid.RowHeadersDefaultCellStyle.ForeColor = TextSub;
            grid.RowHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.CellBeginEdit += Grid_CellBeginEdit;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.MouseDown += Grid_MouseDown;
            grid.MouseMove += Grid_MouseMove;
            grid.MouseUp += Grid_MouseUp;
            grid.CellPainting += Grid_CellPainting;
            grid.CellFormatting += Grid_CellFormatting;
            grid.RowPostPaint += Grid_RowPostPaint;
            grid.RowHeaderMouseClick += Grid_RowHeaderMouseClick;
            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            grid.SelectionChanged += Grid_SelectionChanged;
            grid.KeyDown += Grid_KeyDown;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            grid.ColumnHeadersHeight = 34;

            grid.DefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = 28;

            BuildGridContextMenu();

            parent.Controls.Add(grid);
        }

        private void BuildGridContextMenu()
        {
            gridContextMenu = new ContextMenuStrip();
            gridContextMenu.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            gridContextMenu.Opening += GridContextMenu_Opening;

            undoMenuItem = new ToolStripMenuItem("되돌리기(Ctrl + Z)");
            undoMenuItem.Click += ContextUndo_Click;
            gridContextMenu.Items.Add(undoMenuItem);

            redoMenuItem = new ToolStripMenuItem("다시 실행(Shift + Ctrl + Z)");
            redoMenuItem.Click += ContextRedo_Click;
            gridContextMenu.Items.Add(redoMenuItem);

            gridContextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem selectAllItem = new ToolStripMenuItem("전체선택");
            selectAllItem.Click += ContextSelectAll_Click;
            gridContextMenu.Items.Add(selectAllItem);

            ToolStripMenuItem moveBottomItem = new ToolStripMenuItem("맨뒤로 이동");
            moveBottomItem.Click += ContextMoveBottom_Click;
            gridContextMenu.Items.Add(moveBottomItem);

            ToolStripMenuItem copyBottomItem = new ToolStripMenuItem("맨뒤로 복사");
            copyBottomItem.Click += ContextCopyBottom_Click;
            gridContextMenu.Items.Add(copyBottomItem);

            gridContextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem addRowItem = new ToolStripMenuItem("행추가");
            addRowItem.Click += ContextAddRow_Click;
            gridContextMenu.Items.Add(addRowItem);

            ToolStripMenuItem deleteRowItem = new ToolStripMenuItem("행삭제");
            deleteRowItem.Click += ContextDeleteRows_Click;
            gridContextMenu.Items.Add(deleteRowItem);

            gridContextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem changePartItem = new ToolStripMenuItem("부위 변경");
            changePartItem.Click += ContextChangePart_Click;
            gridContextMenu.Items.Add(changePartItem);

            ToolStripMenuItem changeMarkItem = new ToolStripMenuItem("부호 및 명칭 변경");
            changeMarkItem.Click += ContextChangeMarkName_Click;
            gridContextMenu.Items.Add(changeMarkItem);

            ToolStripMenuItem changeSpecItem = new ToolStripMenuItem("규격 변경");
            changeSpecItem.Click += ContextChangeSpec_Click;
            gridContextMenu.Items.Add(changeSpecItem);

            ToolStripMenuItem changeMemoItem = new ToolStripMenuItem("비고 변경");
            changeMemoItem.Click += ContextChangeMemo_Click;
            gridContextMenu.Items.Add(changeMemoItem);

            grid.ContextMenuStrip = gridContextMenu;
        }

        private void BuildFooter(Control parent)
        {
            OviaBarListButton coverButton = new OviaBarListButton();
            coverButton.Text = "갑지출력";
            coverButton.Location = new Point(34, 690);
            coverButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            coverButton.Size = new Size(94, 34);
            coverButton.StartColor = Color.FromArgb(108, 117, 145);
            coverButton.EndColor = Color.FromArgb(78, 86, 110);
            coverButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(coverButton);

            OviaBarListButton detailButton = new OviaBarListButton();
            detailButton.Text = "내역출력";
            detailButton.Location = new Point(140, 690);
            detailButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            detailButton.Size = new Size(94, 34);
            detailButton.StartColor = Color.FromArgb(108, 117, 145);
            detailButton.EndColor = Color.FromArgb(78, 86, 110);
            detailButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(detailButton);

            OviaBarListButton tagButton = new OviaBarListButton();
            tagButton.Text = "태그발행";
            tagButton.Location = new Point(246, 690);
            tagButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            tagButton.Size = new Size(94, 34);
            tagButton.StartColor = Color.FromArgb(108, 117, 145);
            tagButton.EndColor = Color.FromArgb(78, 86, 110);
            tagButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(tagButton);

            OviaBarListButton deleteButton = new OviaBarListButton();
            deleteButton.Text = "선택 행 삭제";
            deleteButton.Location = new Point(352, 690);
            deleteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            deleteButton.Size = new Size(110, 34);
            deleteButton.StartColor = Color.FromArgb(215, 85, 85);
            deleteButton.EndColor = Color.FromArgb(165, 50, 60);
            deleteButton.Click += DeleteRows_Click;
            parent.Controls.Add(deleteButton);

            OviaBarListButton saveCsvButton = new OviaBarListButton();
            saveCsvButton.Text = "CSV 저장";
            saveCsvButton.Location = new Point(474, 690);
            saveCsvButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parent.Controls.Add(footer);
        }

        private void AutoCadImport_Click(object sender, EventArgs e)
        {
            if (!IsAutoCadRunning())
            {
                lblStatus.Text = "AutoCAD 비활성 상태 - AutoCAD를 먼저 실행하고 DWG 도면을 열어주세요.";
                lblStatus.ForeColor = Color.FromArgb(210, 78, 78);

                return;
            }

            StartAutoCadWatcher();
            ActivateAutoCad();

            lblStatus.Text = "AutoCAD 추출 대기 중 - OVIABOX → OVIABOXTABLE 실행 후 자동 입력됩니다.";
            lblStatus.ForeColor = TextSub;
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

            lblStatus.Text = "AutoCAD 추출 대기 중 - OVIABOXTABLE 실행을 기다립니다.";
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

            LoadCsv(filePath, false);
            waitingAutoCadImport = false;
            StopAutoCadWatcher();

            lblStatus.Text = "AutoCAD 추출 데이터 자동 입력 완료 - 확인 후 저장하세요.";
            lblStatus.ForeColor = TextSub;

            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            this.Activate();
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

            try
            {
                string dir = GetProjectBarListDirectory();
                Directory.CreateDirectory(dir);

                string filePath;

                if (savedProjectFilePath.Trim() != "" && File.Exists(savedProjectFilePath))
                {
                    filePath = savedProjectFilePath;
                }
                else
                {
                    string fileName = "BarList_" + projectNo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                    filePath = Path.Combine(dir, fileName);
                }

                SaveGridToCsv(filePath);
                ResetAllRowOriginalValuesToCurrent();

                isSaved = true;
                allowExtractEditMenu = true;
                UpdateSaveState();
                ClearUndoRedoStates();
                grid.Invalidate();

                lblStatus.Text = "BarList 저장 완료 - 공사별 BarList 목록에 반영되었습니다.";
                lblStatus.ForeColor = TextSub;
                txtFilePath.Text = filePath;
                lastLoadedFilePath = filePath;
                savedProjectFilePath = filePath;
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
            List<int> selectedIndexes = GetSelectedRowIndexes(false);

            if (selectedIndexes.Count == 0)
            {
                MessageBox.Show(
                    "삭제할 행 또는 셀 영역을 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            GridUndoSnapshot undoState = CaptureGridState();
            PushUndoState(undoState);

            int i;

            for (i = 0; i < selectedIndexes.Count; i++)
            {
                if (selectedIndexes[i] >= 0 && selectedIndexes[i] < grid.Rows.Count && !grid.Rows[selectedIndexes[i]].IsNewRow)
                {
                    grid.Rows.RemoveAt(selectedIndexes[i]);
                }
            }

            MarkUnsaved();
            RecalculateSummary();
            grid.Invalidate();
        }

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (isRestoringGridState)
            {
                return;
            }

            if (!CanUseExtractEditMenu())
            {
                e.Cancel = true;
                return;
            }

            cellEditBeforeSnapshot = CaptureGridState();
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (!isRestoringGridState && cellEditBeforeSnapshot != null)
            {
                PushUndoState(cellEditBeforeSnapshot);
                cellEditBeforeSnapshot = null;
            }

            RefreshModifiedCellVisual(e.RowIndex, e.ColumnIndex);
            MarkUnsaved();
            RecalculateSummary();
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (!grid.Columns[e.ColumnIndex].Visible)
            {
                return;
            }

            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            grid.BeginEdit(true);
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.ColumnIndex == -1 && e.RowIndex >= 0)
                {
                    StartRowHeaderSelection(e.RowIndex);
                    QueueHeaderSelectionRefresh();
                    return;
                }

                if (e.RowIndex == -1 && e.ColumnIndex >= 0)
                {
                    StartColumnHeaderSelection(e.ColumnIndex);
                    QueueHeaderSelectionRefresh();
                    return;
                }

                if (e.RowIndex == -1 && e.ColumnIndex == -1)
                {
                    BeginGridSelectionUpdate();

                    try
                    {
                        grid.SelectAll();
                    }
                    finally
                    {
                        EndGridSelectionUpdate();
                    }

                    return;
                }

                return;
            }

            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (!grid.Columns[e.ColumnIndex].Visible)
            {
                return;
            }

            DataGridViewCell clickedCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (!clickedCell.Selected)
            {
                BeginGridSelectionUpdate();

                try
                {
                    grid.ClearSelection();
                    clickedCell.Selected = true;
                }
                finally
                {
                    EndGridSelectionUpdate();
                }
            }

            grid.CurrentCell = clickedCell;
            InvalidateSelectionVisuals();
        }

        private void Grid_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || grid == null)
            {
                return;
            }

            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);

            if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0)
            {
                StartRowHeaderSelection(hit.RowIndex);
                QueueHeaderSelectionRefresh();
                return;
            }

            if (hit.Type == DataGridViewHitTestType.ColumnHeader && hit.ColumnIndex >= 0)
            {
                StartColumnHeaderSelection(hit.ColumnIndex);
                QueueHeaderSelectionRefresh();
                return;
            }

            if (hit.Type == DataGridViewHitTestType.TopLeftHeader)
            {
                headerDragMode = HeaderDragNone;
                BeginGridSelectionUpdate();

                try
                {
                    grid.SelectAll();
                }
                finally
                {
                    EndGridSelectionUpdate();
                }

                return;
            }

            headerDragMode = HeaderDragNone;
            headerDragStartIndex = -1;
            headerDragLastIndex = -1;
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (grid == null || e.Button != MouseButtons.Left)
            {
                return;
            }

            if (headerDragMode == HeaderDragNone || headerDragStartIndex < 0)
            {
                return;
            }

            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);

            if (headerDragMode == HeaderDragRow && hit.RowIndex >= 0 && hit.RowIndex < grid.Rows.Count)
            {
                if (hit.RowIndex != headerDragLastIndex)
                {
                    headerDragLastIndex = hit.RowIndex;
                    headerSelectionVersion++;
                    SelectRowRange(headerDragStartIndex, hit.RowIndex, false);
                }

                return;
            }

            if (headerDragMode == HeaderDragColumn && hit.ColumnIndex >= 0 && hit.ColumnIndex < grid.Columns.Count)
            {
                if (hit.ColumnIndex != headerDragLastIndex)
                {
                    headerDragLastIndex = hit.ColumnIndex;
                    headerSelectionVersion++;
                    SelectColumnRange(headerDragStartIndex, hit.ColumnIndex, false);
                }
            }
        }

        private void Grid_MouseUp(object sender, MouseEventArgs e)
        {
            headerDragMode = HeaderDragNone;
            headerDragStartIndex = -1;
            headerDragLastIndex = -1;
            InvalidateSelectionVisuals();
        }

        private void StartRowHeaderSelection(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
            {
                return;
            }

            headerSelectionVersion++;
            headerDragMode = HeaderDragRow;
            headerDragStartIndex = rowIndex;
            headerDragLastIndex = rowIndex;
            SelectRowRange(rowIndex, rowIndex, false);
        }

        private void StartColumnHeaderSelection(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            headerSelectionVersion++;
            headerDragMode = HeaderDragColumn;
            headerDragStartIndex = columnIndex;
            headerDragLastIndex = columnIndex;
            SelectColumnRange(columnIndex, columnIndex, false);
        }

        private void QueueHeaderSelectionRefresh()
        {
            int capturedMode = headerDragMode;
            int capturedStartIndex = headerDragStartIndex;
            int capturedLastIndex = headerDragLastIndex;
            int capturedVersion = headerSelectionVersion;

            try
            {
                grid.BeginInvoke(new MethodInvoker(delegate
                {
                    if (grid == null || grid.IsDisposed)
                    {
                        return;
                    }

                    if (capturedVersion != headerSelectionVersion)
                    {
                        return;
                    }

                    if (capturedMode == HeaderDragRow && capturedStartIndex >= 0)
                    {
                        SelectRowRange(capturedStartIndex, capturedLastIndex < 0 ? capturedStartIndex : capturedLastIndex, false);
                    }
                    else if (capturedMode == HeaderDragColumn && capturedStartIndex >= 0)
                    {
                        SelectColumnRange(capturedStartIndex, capturedLastIndex < 0 ? capturedStartIndex : capturedLastIndex, false);
                    }
                }));
            }
            catch
            {
            }
        }

        private void Grid_RowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            StartRowHeaderSelection(e.RowIndex);
            headerDragMode = HeaderDragNone;
            headerDragStartIndex = -1;
            headerDragLastIndex = -1;
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            StartColumnHeaderSelection(e.ColumnIndex);
            headerDragMode = HeaderDragNone;
            headerDragStartIndex = -1;
            headerDragLastIndex = -1;
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && !e.Shift && e.KeyCode == Keys.Z)
            {
                UndoGridAction();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                RedoGridAction();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (grid == null || isBulkGridSelecting)
            {
                return;
            }

            RefreshSelectionVisualCache();
            InvalidateSelectionVisuals();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.RowIndex >= grid.Rows.Count || e.ColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (IsCellModified(e.RowIndex, e.ColumnIndex))
            {
                e.CellStyle.ForeColor = ModifiedCellTextColor;
                e.CellStyle.SelectionForeColor = ModifiedCellTextColor;
            }
            else
            {
                e.CellStyle.ForeColor = TextDark;
                e.CellStyle.SelectionForeColor = TextDark;
            }
        }

        private void Grid_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            string rowNumber = (e.RowIndex + 1).ToString();
            Rectangle headerBounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, grid.RowHeadersWidth, e.RowBounds.Height);
            bool rowSelected = IsRowFullySelected(e.RowIndex);
            Color headerBack = rowSelected ? Color.FromArgb(255, 235, 112) : Color.FromArgb(242, 245, 252);
            Color headerFore = rowSelected ? TextDark : TextSub;

            using (SolidBrush brush = new SolidBrush(headerBack))
            {
                e.Graphics.FillRectangle(brush, headerBounds);
            }

            using (Pen pen = new Pen(rowSelected ? Color.FromArgb(188, 136, 0) : Color.FromArgb(220, 225, 235), rowSelected ? 2F : 1F))
            {
                e.Graphics.DrawRectangle(pen, headerBounds.Left, headerBounds.Top, headerBounds.Width - 1, headerBounds.Height - 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                rowNumber,
                grid.RowHeadersDefaultCellStyle.Font,
                headerBounds,
                headerFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (grid == null)
            {
                return;
            }

            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                PaintColumnHeaderIfSelected(e);
                return;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.RowIndex >= grid.Rows.Count || e.ColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (!cell.Selected)
            {
                return;
            }

            bool singleCellSelected = selectedCellCountCache <= 1;
            Color backColor = singleCellSelected ? Color.FromArgb(255, 219, 58) : Color.FromArgb(255, 248, 205);
            Color borderColor = singleCellSelected ? Color.FromArgb(170, 122, 0) : Color.FromArgb(226, 189, 67);

            if (IsCellModified(e.RowIndex, e.ColumnIndex))
            {
                e.CellStyle.ForeColor = ModifiedCellTextColor;
                e.CellStyle.SelectionForeColor = ModifiedCellTextColor;
            }
            else
            {
                e.CellStyle.ForeColor = TextDark;
                e.CellStyle.SelectionForeColor = TextDark;
            }

            e.Handled = true;

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }

            e.PaintContent(e.CellBounds);

            Rectangle rect = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);

            if (singleCellSelected)
            {
                using (Pen pen = new Pen(borderColor, 3F))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
            else
            {
                using (Pen innerPen = new Pen(Color.FromArgb(242, 214, 126), 1F))
                {
                    e.Graphics.DrawRectangle(innerPen, rect);
                }

                using (Pen outerPen = new Pen(borderColor, 2F))
                {
                    if (!IsGridCellSelected(e.RowIndex - 1, e.ColumnIndex))
                    {
                        e.Graphics.DrawLine(outerPen, rect.Left, rect.Top, rect.Right, rect.Top);
                    }

                    if (!IsGridCellSelected(e.RowIndex + 1, e.ColumnIndex))
                    {
                        e.Graphics.DrawLine(outerPen, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
                    }

                    if (!IsGridCellSelected(e.RowIndex, e.ColumnIndex - 1))
                    {
                        e.Graphics.DrawLine(outerPen, rect.Left, rect.Top, rect.Left, rect.Bottom);
                    }

                    if (!IsGridCellSelected(e.RowIndex, e.ColumnIndex + 1))
                    {
                        e.Graphics.DrawLine(outerPen, rect.Right, rect.Top, rect.Right, rect.Bottom);
                    }
                }
            }
        }

        private void PaintColumnHeaderIfSelected(DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (!grid.Columns[e.ColumnIndex].Visible)
            {
                return;
            }

            if (!IsColumnFullySelected(e.ColumnIndex))
            {
                return;
            }

            e.Handled = true;

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 235, 112)))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }

            using (Pen pen = new Pen(Color.FromArgb(188, 136, 0), 2F))
            {
                e.Graphics.DrawRectangle(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                grid.Columns[e.ColumnIndex].HeaderText,
                grid.ColumnHeadersDefaultCellStyle.Font,
                e.CellBounds,
                TextDark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );
        }

        private bool IsGridCellSelected(int rowIndex, int columnIndex)
        {
            if (grid == null)
            {
                return false;
            }

            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            if (!grid.Columns[columnIndex].Visible)
            {
                return false;
            }

            return grid.Rows[rowIndex].Cells[columnIndex].Selected;
        }

        private bool IsRowFullySelected(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
            {
                return false;
            }

            int visibleCount = 0;
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (grid.Columns[i].Visible)
                {
                    visibleCount++;

                    if (!grid.Rows[rowIndex].Cells[i].Selected)
                    {
                        return false;
                    }
                }
            }

            return visibleCount > 0;
        }

        private bool IsColumnFullySelected(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return false;
            }

            int visibleRowCount = 0;
            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    visibleRowCount++;

                    if (!grid.Rows[r].Cells[columnIndex].Selected)
                    {
                        return false;
                    }
                }
            }

            return visibleRowCount > 0;
        }

        private void RefreshSelectionVisualCache()
        {
            if (grid == null)
            {
                selectedCellCountCache = 0;
                return;
            }

            try
            {
                selectedCellCountCache = grid.SelectedCells.Count;
            }
            catch
            {
                selectedCellCountCache = 0;
            }
        }

        private void InvalidateSelectionVisuals()
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            try
            {
                grid.Invalidate(new Rectangle(0, 0, grid.Width, grid.ColumnHeadersHeight + 2));
                grid.Invalidate(new Rectangle(0, 0, grid.RowHeadersWidth + 2, grid.Height));
            }
            catch
            {
            }
        }

        private void EnableGridDoubleBuffering(DataGridView targetGrid)
        {
            if (targetGrid == null)
            {
                return;
            }

            try
            {
                PropertyInfo propertyInfo = typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(targetGrid, true, null);
                }
            }
            catch
            {
            }
        }

        private void BeginGridSelectionUpdate()
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            gridRedrawLockCount++;
            isBulkGridSelecting = true;

            if (gridRedrawLockCount == 1 && grid.IsHandleCreated)
            {
                SendMessage(grid.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void EndGridSelectionUpdate()
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            if (gridRedrawLockCount > 0)
            {
                gridRedrawLockCount--;
            }

            if (gridRedrawLockCount == 0)
            {
                isBulkGridSelecting = false;
                RefreshSelectionVisualCache();

                if (grid.IsHandleCreated)
                {
                    SendMessage(grid.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                }

                grid.Invalidate();
            }
        }

        private void GridContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                e.Cancel = true;
                lblStatus.Text = "BarList 데이터가 있을 때만 우클릭 편집 메뉴를 사용할 수 있습니다.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            EnsureAtLeastOneCellSelected();
            RefreshUndoRedoMenuState();
        }

        private bool CanUseExtractEditMenu()
        {
            if (!allowExtractEditMenu)
            {
                return false;
            }

            if (grid == null || grid.Columns.Count == 0 || grid.Rows.Count == 0)
            {
                return false;
            }

            return true;
        }

        private void EnsureAtLeastOneCellSelected()
        {
            if (grid.SelectedCells.Count > 0)
            {
                return;
            }

            if (grid.CurrentCell == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;

            if (rowIndex >= 0 && rowIndex < grid.Rows.Count && columnIndex >= 0 && columnIndex < grid.Columns.Count && !grid.Rows[rowIndex].IsNewRow)
            {
                grid.Rows[rowIndex].Cells[columnIndex].Selected = true;
            }
        }

        private void ContextSelectAll_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            BeginGridSelectionUpdate();

            try
            {
                grid.SelectAll();
            }
            finally
            {
                EndGridSelectionUpdate();
            }
        }

        private void ContextMoveBottom_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count == 0)
            {
                return;
            }

            PushUndoState(CaptureGridState());

            List<object[]> rowValues = new List<object[]>();
            List<object[]> rowOriginalValues = new List<object[]>();
            int i;

            for (i = 0; i < selectedIndexes.Count; i++)
            {
                rowValues.Add(CloneRowValues(grid.Rows[selectedIndexes[i]]));
                rowOriginalValues.Add(CloneRowOriginalValues(grid.Rows[selectedIndexes[i]]));
            }

            for (i = selectedIndexes.Count - 1; i >= 0; i--)
            {
                grid.Rows.RemoveAt(selectedIndexes[i]);
            }

            grid.ClearSelection();

            for (i = 0; i < rowValues.Count; i++)
            {
                int newIndex = grid.Rows.Add(rowValues[i]);
                SetRowOriginalValues(newIndex, rowOriginalValues[i]);
                SelectRowCells(newIndex, true);
            }

            MarkUnsaved();
            RecalculateSummary();
        }

        private void ContextCopyBottom_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count == 0)
            {
                return;
            }

            PushUndoState(CaptureGridState());

            grid.ClearSelection();

            int i;

            for (i = 0; i < selectedIndexes.Count; i++)
            {
                int newIndex = grid.Rows.Add(CloneRowValues(grid.Rows[selectedIndexes[i]]));
                ResetRowOriginalValuesToCurrent(newIndex);
                SelectRowCells(newIndex, true);
            }

            MarkUnsaved();
            RecalculateSummary();
        }

        private void ContextAddRow_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            int insertIndex = grid.Rows.Count;
            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count > 0)
            {
                insertIndex = selectedIndexes[selectedIndexes.Count - 1] + 1;
            }
            else if (grid.CurrentCell != null)
            {
                insertIndex = grid.CurrentCell.RowIndex + 1;
            }

            if (insertIndex < 0)
            {
                insertIndex = grid.Rows.Count;
            }

            if (insertIndex > grid.Rows.Count)
            {
                insertIndex = grid.Rows.Count;
            }

            PushUndoState(CaptureGridState());

            object[] emptyValues = new object[grid.Columns.Count];
            int i;

            for (i = 0; i < emptyValues.Length; i++)
            {
                emptyValues[i] = "";
            }

            grid.Rows.Insert(insertIndex, emptyValues);
            SetRowOriginalValues(insertIndex, emptyValues);
            grid.ClearSelection();
            SelectRowCells(insertIndex, true);

            int firstVisibleColumn = GetFirstVisibleColumnIndex();

            if (firstVisibleColumn >= 0)
            {
                grid.CurrentCell = grid.Rows[insertIndex].Cells[firstVisibleColumn];
            }

            MarkUnsaved();
            RecalculateSummary();
        }

        private void ContextDeleteRows_Click(object sender, EventArgs e)
        {
            DeleteRows_Click(sender, e);
        }

        private void ContextChangePart_Click(object sender, EventArgs e)
        {
            ApplyBulkChangeByColumn("부위", new string[] { "부위", "위치", "구간" });
        }

        private void ContextChangeMarkName_Click(object sender, EventArgs e)
        {
            ApplyBulkChangeByColumn("부호 및 명칭", new string[] { "부호", "명칭", "철근명", "기호" });
        }

        private void ContextChangeSpec_Click(object sender, EventArgs e)
        {
            ApplyBulkChangeByColumn("규격", new string[] { "규격", "강종", "직경", "Dia", "DIA" });
        }

        private void ContextChangeMemo_Click(object sender, EventArgs e)
        {
            ApplyBulkChangeByColumn("비고", new string[] { "비고", "메모", "Remark", "REMARK" });
        }

        private void ApplyBulkChangeByColumn(string displayName, string[] aliases)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            EnsureAtLeastOneCellSelected();

            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count == 0)
            {
                MessageBox.Show(
                    "변경할 행 또는 셀 영역을 먼저 선택해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            int columnIndex = FindColumnIndexByAliases(aliases);

            if (columnIndex < 0)
            {
                MessageBox.Show(
                    "현재 BarList에 [" + displayName + "] 컬럼을 찾지 못했습니다.\r\n\r\nCAD 원본 컬럼명 또는 표준화 컬럼명을 확인해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string beforeText = GetFirstSelectedValue(columnIndex);
            string newValue;

            if (!OviaTextReplaceDialog.ShowDialog(this, displayName + " 일괄 변경", "선택된 " + selectedIndexes.Count.ToString() + "개 행의 [" + grid.Columns[columnIndex].HeaderText + "] 값을 변경합니다.", beforeText, out newValue))
            {
                return;
            }

            PushUndoState(CaptureGridState());

            int i;

            for (i = 0; i < selectedIndexes.Count; i++)
            {
                grid.Rows[selectedIndexes[i]].Cells[columnIndex].Value = newValue;
            }

            MarkUnsaved();
            RecalculateSummary();
            grid.Invalidate();
        }

        private List<int> GetSelectedRowIndexes(bool ascending)
        {
            List<int> indexes = new List<int>();
            int i;

            for (i = 0; i < grid.SelectedRows.Count; i++)
            {
                AddRowIndexIfMissing(indexes, grid.SelectedRows[i].Index);
            }

            for (i = 0; i < grid.SelectedCells.Count; i++)
            {
                AddRowIndexIfMissing(indexes, grid.SelectedCells[i].RowIndex);
            }

            if (indexes.Count == 0 && grid.CurrentCell != null)
            {
                AddRowIndexIfMissing(indexes, grid.CurrentCell.RowIndex);
            }

            indexes.Sort();

            if (!ascending)
            {
                indexes.Reverse();
            }

            return indexes;
        }

        private void AddRowIndexIfMissing(List<int> indexes, int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            if (grid.Rows[rowIndex].IsNewRow)
            {
                return;
            }

            if (!indexes.Contains(rowIndex))
            {
                indexes.Add(rowIndex);
            }
        }

        private void SelectRowCells(int rowIndex, bool append)
        {
            BeginGridSelectionUpdate();

            try
            {
                if (!append)
                {
                    grid.ClearSelection();
                }

                SelectRowCellsInternal(rowIndex);
                SetCurrentCellToRow(rowIndex);
            }
            finally
            {
                EndGridSelectionUpdate();
            }
        }

        private void SelectColumnCells(int columnIndex, bool append)
        {
            BeginGridSelectionUpdate();

            try
            {
                if (!append)
                {
                    grid.ClearSelection();
                }

                SelectColumnCellsInternal(columnIndex);
                SetCurrentCellToColumn(columnIndex);
            }
            finally
            {
                EndGridSelectionUpdate();
            }
        }

        private void SelectRowRange(int startRowIndex, int endRowIndex, bool append)
        {
            if (grid == null)
            {
                return;
            }

            int from = Math.Min(startRowIndex, endRowIndex);
            int to = Math.Max(startRowIndex, endRowIndex);

            if (from < 0)
            {
                from = 0;
            }

            if (to >= grid.Rows.Count)
            {
                to = grid.Rows.Count - 1;
            }

            BeginGridSelectionUpdate();

            try
            {
                if (!append)
                {
                    grid.ClearSelection();
                }

                int r;

                for (r = from; r <= to; r++)
                {
                    SelectRowCellsInternal(r);
                }

                SetCurrentCellToRow(startRowIndex);
            }
            finally
            {
                EndGridSelectionUpdate();
            }
        }

        private void SelectColumnRange(int startColumnIndex, int endColumnIndex, bool append)
        {
            if (grid == null)
            {
                return;
            }

            int from = Math.Min(startColumnIndex, endColumnIndex);
            int to = Math.Max(startColumnIndex, endColumnIndex);

            if (from < 0)
            {
                from = 0;
            }

            if (to >= grid.Columns.Count)
            {
                to = grid.Columns.Count - 1;
            }

            BeginGridSelectionUpdate();

            try
            {
                if (!append)
                {
                    grid.ClearSelection();
                }

                int c;

                for (c = from; c <= to; c++)
                {
                    SelectColumnCellsInternal(c);
                }

                SetCurrentCellToColumn(startColumnIndex);
            }
            finally
            {
                EndGridSelectionUpdate();
            }
        }

        private void SelectRowCellsInternal(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (grid.Columns[i].Visible)
                {
                    grid.Rows[rowIndex].Cells[i].Selected = true;
                }
            }
        }

        private void SelectColumnCellsInternal(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    grid.Rows[r].Cells[columnIndex].Selected = true;
                }
            }
        }

        private void SetCurrentCellToRow(int rowIndex)
        {
            int firstVisibleColumn = GetFirstVisibleColumnIndex();

            if (firstVisibleColumn >= 0 && rowIndex >= 0 && rowIndex < grid.Rows.Count && !grid.Rows[rowIndex].IsNewRow)
            {
                grid.CurrentCell = grid.Rows[rowIndex].Cells[firstVisibleColumn];
            }
        }

        private void SetCurrentCellToColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    grid.CurrentCell = grid.Rows[r].Cells[columnIndex];
                    return;
                }
            }
        }

        private object[] CloneRowValues(DataGridViewRow row)
        {
            object[] values = new object[grid.Columns.Count];
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                values[i] = row.Cells[i].Value == null ? "" : row.Cells[i].Value.ToString();
            }

            return values;
        }

        private object[] CloneObjectArray(object[] source)
        {
            if (source == null)
            {
                return new object[0];
            }

            object[] values = new object[source.Length];
            int i;

            for (i = 0; i < source.Length; i++)
            {
                values[i] = source[i] == null ? "" : source[i].ToString();
            }

            return values;
        }

        private object[] CloneRowOriginalValues(DataGridViewRow row)
        {
            object[] originalValues = row.Tag as object[];

            if (originalValues == null)
            {
                return CloneRowValues(row);
            }

            return CloneObjectArray(originalValues);
        }

        private void SetRowOriginalValues(int rowIndex, object[] values)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            grid.Rows[rowIndex].Tag = CloneObjectArray(values);
        }

        private void ResetRowOriginalValuesToCurrent(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            grid.Rows[rowIndex].Tag = CloneRowValues(grid.Rows[rowIndex]);
        }

        private void ResetAllRowOriginalValuesToCurrent()
        {
            if (grid == null)
            {
                return;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    ResetRowOriginalValuesToCurrent(r);
                }
            }
        }

        private bool IsCellModified(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0)
            {
                return false;
            }

            if (rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            DataGridViewRow row = grid.Rows[rowIndex];

            if (row.IsNewRow)
            {
                return false;
            }

            object[] originalValues = row.Tag as object[];

            if (originalValues == null)
            {
                row.Tag = CloneRowValues(row);
                return false;
            }

            string originalText = "";

            if (columnIndex < originalValues.Length && originalValues[columnIndex] != null)
            {
                originalText = originalValues[columnIndex].ToString();
            }

            string currentText = NormalizeCellValue(row.Cells[columnIndex].Value);

            return !String.Equals(originalText, currentText, StringComparison.Ordinal);
        }

        private string NormalizeCellValue(object value)
        {
            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

        private void RefreshModifiedCellVisual(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0)
            {
                return;
            }

            if (rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            grid.InvalidateCell(columnIndex, rowIndex);
        }

        private int GetFirstVisibleColumnIndex()
        {
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (grid.Columns[i].Visible)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindColumnIndexByAliases(string[] aliases)
        {
            int i;
            int j;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].HeaderText;

                if (name == null)
                {
                    name = "";
                }

                for (j = 0; j < aliases.Length; j++)
                {
                    if (name.IndexOf(aliases[j], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private string GetFirstSelectedValue(int columnIndex)
        {
            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count == 0)
            {
                return "";
            }

            object value = grid.Rows[selectedIndexes[0]].Cells[columnIndex].Value;

            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

        private void ContextUndo_Click(object sender, EventArgs e)
        {
            UndoGridAction();
        }

        private void ContextRedo_Click(object sender, EventArgs e)
        {
            RedoGridAction();
        }

        private void RefreshUndoRedoMenuState()
        {
            if (undoMenuItem != null)
            {
                undoMenuItem.Enabled = undoStates.Count > 0;
            }

            if (redoMenuItem != null)
            {
                redoMenuItem.Enabled = redoStates.Count > 0;
            }
        }

        private void ClearUndoRedoStates()
        {
            undoStates.Clear();
            redoStates.Clear();
            cellEditBeforeSnapshot = null;
            RefreshUndoRedoMenuState();
        }

        private GridUndoSnapshot CaptureGridState()
        {
            GridUndoSnapshot state = new GridUndoSnapshot();

            if (grid == null)
            {
                return state;
            }

            if (grid.CurrentCell != null)
            {
                state.CurrentRowIndex = grid.CurrentCell.RowIndex;
                state.CurrentColumnIndex = grid.CurrentCell.ColumnIndex;
            }

            int r;
            int c;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                object[] values = new object[grid.Columns.Count];

                for (c = 0; c < grid.Columns.Count; c++)
                {
                    values[c] = grid.Rows[r].Cells[c].Value == null ? "" : grid.Rows[r].Cells[c].Value.ToString();
                }

                state.Rows.Add(values);
                state.OriginalRows.Add(CloneRowOriginalValues(grid.Rows[r]));
            }

            return state;
        }

        private void PushUndoState(GridUndoSnapshot state)
        {
            if (state == null || isRestoringGridState)
            {
                return;
            }

            undoStates.Add(state);

            while (undoStates.Count > MaxUndoCount)
            {
                undoStates.RemoveAt(0);
            }

            redoStates.Clear();
            RefreshUndoRedoMenuState();
        }

        private void UndoGridAction()
        {
            if (!CanUseExtractEditMenu() || undoStates.Count == 0)
            {
                return;
            }

            GridUndoSnapshot currentState = CaptureGridState();
            GridUndoSnapshot previousState = undoStates[undoStates.Count - 1];
            undoStates.RemoveAt(undoStates.Count - 1);
            redoStates.Add(currentState);

            RestoreGridState(previousState);
            MarkUnsaved();
            RecalculateSummary();
            RefreshUndoRedoMenuState();

            lblStatus.Text = "이전 작업으로 되돌렸습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void RedoGridAction()
        {
            if (!CanUseExtractEditMenu() || redoStates.Count == 0)
            {
                return;
            }

            GridUndoSnapshot currentState = CaptureGridState();
            GridUndoSnapshot nextState = redoStates[redoStates.Count - 1];
            redoStates.RemoveAt(redoStates.Count - 1);
            undoStates.Add(currentState);

            RestoreGridState(nextState);
            MarkUnsaved();
            RecalculateSummary();
            RefreshUndoRedoMenuState();

            lblStatus.Text = "되돌린 작업을 다시 실행했습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void RestoreGridState(GridUndoSnapshot state)
        {
            if (grid == null || state == null)
            {
                return;
            }

            isRestoringGridState = true;

            try
            {
                grid.Rows.Clear();

                int i;

                for (i = 0; i < state.Rows.Count; i++)
                {
                    int newRowIndex = grid.Rows.Add(state.Rows[i]);

                    if (i < state.OriginalRows.Count)
                    {
                        SetRowOriginalValues(newRowIndex, state.OriginalRows[i]);
                    }
                    else
                    {
                        SetRowOriginalValues(newRowIndex, state.Rows[i]);
                    }
                }

                grid.ClearSelection();

                if (grid.Rows.Count > 0 && grid.Columns.Count > 0)
                {
                    int rowIndex = state.CurrentRowIndex;
                    int columnIndex = state.CurrentColumnIndex;

                    if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
                    {
                        rowIndex = 0;
                    }

                    if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
                    {
                        columnIndex = GetFirstVisibleColumnIndex();
                    }

                    if (columnIndex >= 0)
                    {
                        grid.Rows[rowIndex].Cells[columnIndex].Selected = true;
                        grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
                    }
                }
            }
            finally
            {
                isRestoringGridState = false;
            }

            grid.Invalidate();
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

        private void ShowReplacementWindow(Form nextForm)
        {
            if (nextForm == null)
            {
                return;
            }

            Form ownerForm = this.Owner;
            FormWindowState currentState = this.WindowState;
            Rectangle normalBounds = this.WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;

            nextForm.StartPosition = FormStartPosition.Manual;
            nextForm.Bounds = normalBounds;

            if (currentState == FormWindowState.Maximized)
            {
                nextForm.WindowState = FormWindowState.Maximized;
            }

            nextForm.Show();
            nextForm.Activate();
            isInternalNavigation = true;
            this.Close();
        }

        private bool ConfirmDiscardUnsavedForNavigation()
        {
            if (!isSaved && grid != null && grid.Columns.Count > 0 && grid.Rows.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "저장하지 않은 BarList 데이터가 있습니다.\r\n\r\n이전 화면으로 이동하면 저장하지 않은 변경 내용이 사라질 수 있습니다.\r\n이동하시겠습니까?",
                    "OVIA",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result != DialogResult.Yes)
                {
                    return false;
                }
            }

            return true;
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
            NavigateBackToProjectBarListList();
        }

        private void FrmBarList_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isInternalNavigation)
            {
                StopAutoCadWatcher();
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    isClosingByButton = false;
                    e.Cancel = true;
                    return;
                }

                e.Cancel = true;
                QueueBackNavigationToProjectBarListList();
                return;
            }

            StopAutoCadWatcher();
        }

        private void QueueBackNavigationToProjectBarListList()
        {
            if (isBackNavigationQueued || this.IsDisposed)
            {
                return;
            }

            isBackNavigationQueued = true;

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    isBackNavigationQueued = false;
                    NavigateBackToProjectBarListList();
                }));
            }
            catch
            {
                isBackNavigationQueued = false;
            }
        }

        private void NavigateBackToProjectBarListList()
        {
            FrmProjectBarListList form = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus);
            suppressUnsavedClosePrompt = true;
            ShowReplacementWindow(form);
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

        private void LoadCsv(string filePath, bool loadAsSaved)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count == 0)
                {
                    lblStatus.Text = "CSV 파일에 읽을 데이터가 없습니다.";
                    lblStatus.ForeColor = Color.FromArgb(210, 78, 78);

                    return;
                }

                BindCsvRows(rows);
                allowExtractEditMenu = true;
                ClearUndoRedoStates();
                txtFilePath.Text = filePath;
                lastLoadedFilePath = filePath;

                RecalculateSummary();

                if (loadAsSaved)
                {
                    isSaved = true;
                    UpdateSaveState();
                    lblStatus.Text = "저장된 BarList 열기 - " + GetMappingSummaryText();
                    lblStatus.ForeColor = TextSub;
                }
                else
                {
                    MarkUnsaved();
                    lblStatus.Text = "BarList 후보 데이터 입력 완료 - " + GetMappingSummaryText();
                    lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CSV 불러오기 오류 - " + ex.Message;
                lblStatus.ForeColor = Color.FromArgb(210, 78, 78);
            }
        }

        private void BindCsvRows(List<List<string>> rows)
        {
            BeginGridSelectionUpdate();
            grid.SuspendLayout();

            try
            {
                grid.Columns.Clear();
                grid.Rows.Clear();

                List<string> sourceHeaders = rows[0];
                OviaBarListMappingStore store = GetMappingStore();
                OviaBarListMappedTable mappedTable = store.BuildMappedTable(sourceHeaders);

                lastMappingMatchCount = mappedTable.MatchedCount;
                lastMappingTotalHeaderCount = sourceHeaders.Count;
                lastMappingVersion = store.Version;

                int i;

                for (i = 0; i < mappedTable.Columns.Count; i++)
                {
                    string header = mappedTable.Columns[i].DisplayName;

                    if (header == null || header.Trim() == "")
                    {
                        header = "Column" + (i + 1).ToString();
                    }

                    DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                    column.Name = GetSafeColumnName(header, i);
                    column.HeaderText = header;
                    column.Tag = mappedTable.Columns[i];
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    column.MinimumWidth = 45;
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
                    column.Resizable = DataGridViewTriState.True;
                    grid.Columns.Add(column);
                }

                int r;

                for (r = 1; r < rows.Count; r++)
                {
                    List<string> values = rows[r];
                    object[] cells = new object[mappedTable.Columns.Count];

                    for (i = 0; i < mappedTable.Columns.Count; i++)
                    {
                        int sourceIndex = mappedTable.Columns[i].SourceIndex;

                        if (sourceIndex >= 0 && sourceIndex < values.Count)
                        {
                            cells[i] = values[sourceIndex];
                        }
                        else
                        {
                            cells[i] = "";
                        }
                    }

                    int newRowIndex = grid.Rows.Add(cells);
                    SetRowOriginalValues(newRowIndex, cells);
                }

                ApplyGridColumnStyle();
            }
            finally
            {
                grid.ResumeLayout();
                EndGridSelectionUpdate();
            }
        }

        private string GetSafeColumnName(string header, int index)
        {
            string name = header == null ? "" : header.Trim();

            if (name == "")
            {
                name = "Column" + (index + 1).ToString();
            }

            name = name.Replace(" ", "_");
            name = name.Replace("/", "_");
            name = name.Replace("\\", "_");
            name = name.Replace("(", "");
            name = name.Replace(")", "");
            name = name.Replace("[", "");
            name = name.Replace("]", "");

            if (grid != null && grid.Columns.Contains(name))
            {
                name = name + "_" + (index + 1).ToString();
            }

            return name;
        }

        private OviaBarListMappingStore GetMappingStore()
        {
            if (mappingStore == null)
            {
                mappingStore = OviaBarListMappingStore.LoadDefault();
            }

            return mappingStore;
        }

        private string GetMappingSummaryText()
        {
            if (lastMappingTotalHeaderCount <= 0)
            {
                return "매핑 사전 대기";
            }

            string versionText = lastMappingVersion == null || lastMappingVersion.Trim() == "" ? "내장 기본" : lastMappingVersion;

            return "매핑 " + lastMappingMatchCount.ToString() + "/" + lastMappingTotalHeaderCount.ToString() + "개 적용  |  사전 " + versionText;
        }

        private void ApplyGridColumnStyle()
        {
            int i;

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Vertical;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].HeaderText;
                int baseWidth = 95;

                if (ContainsAny(name, "No", "RowType", "SourceRowNo"))
                {
                    grid.Columns[i].Visible = false;
                    continue;
                }

                if (ContainsAny(name, "번호"))
                {
                    baseWidth = 60;
                }
                else if (ContainsAny(name, "규격"))
                {
                    baseWidth = 90;
                }
                else if (ContainsAny(name, "형상"))
                {
                    baseWidth = 130;
                }
                else if (ContainsAny(name, "길이"))
                {
                    baseWidth = 90;
                }
                else if (ContainsAny(name, "수량"))
                {
                    baseWidth = 75;
                }
                else if (ContainsAny(name, "중량"))
                {
                    baseWidth = 95;
                }
                else if (ContainsAny(name, "비고"))
                {
                    baseWidth = 130;
                }

                grid.Columns[i].Visible = true;
                grid.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns[i].FillWeight = baseWidth;
                grid.Columns[i].MinimumWidth = 45;
                grid.Columns[i].Width = baseWidth;
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

    public class GridUndoSnapshot
    {
        public List<object[]> Rows = new List<object[]>();
        public List<object[]> OriginalRows = new List<object[]>();
        public int CurrentRowIndex = 0;
        public int CurrentColumnIndex = 0;
    }

    public class OviaTextReplaceDialog : Form
    {
        private TextBox txtValue;
        private Button btnOk;
        private Button btnCancel;
        private bool confirmed = false;

        private OviaTextReplaceDialog(string title, string guide, string defaultValue)
        {
            this.Text = title;
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(430, 155);
            this.BackColor = Color.White;

            Label lblGuide = new Label();
            lblGuide.Text = guide;
            lblGuide.AutoSize = false;
            lblGuide.Location = new Point(18, 16);
            lblGuide.Size = new Size(390, 42);
            lblGuide.ForeColor = Color.FromArgb(28, 33, 72);
            this.Controls.Add(lblGuide);

            txtValue = new TextBox();
            txtValue.Location = new Point(20, 66);
            txtValue.Size = new Size(390, 23);
            txtValue.Text = defaultValue == null ? "" : defaultValue;
            txtValue.SelectAll();
            this.Controls.Add(txtValue);

            btnOk = new Button();
            btnOk.Text = "적용";
            btnOk.Location = new Point(238, 108);
            btnOk.Size = new Size(82, 30);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(328, 108);
            btnCancel.Size = new Size(82, 30);
            btnCancel.Click += BtnCancel_Click;
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            confirmed = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            confirmed = false;
            this.Close();
        }

        public static bool ShowDialog(IWin32Window owner, string title, string guide, string defaultValue, out string value)
        {
            using (OviaTextReplaceDialog dialog = new OviaTextReplaceDialog(title, guide, defaultValue))
            {
                dialog.ShowDialog(owner);

                value = dialog.txtValue.Text;

                return dialog.confirmed;
            }
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

    public class OviaBarListMappedTable
    {
        public List<OviaBarListMappedColumn> Columns = new List<OviaBarListMappedColumn>();
        public int MatchedCount = 0;
    }

    public class OviaBarListMappedColumn
    {
        public int SourceIndex = -1;
        public string SourceHeader = "";
        public string StandardKey = "";
        public string DisplayName = "";
        public bool IsMapped = false;
    }

    public class OviaBarListMappingColumn
    {
        public string Key = "";
        public string DisplayName = "";
        public string DataType = "";
        public int Priority = 100;
        public List<string> Aliases = new List<string>();
    }

    public class OviaBarListMappingStore
    {
        public string Version = "built-in";
        public string UpdatedAt = "";
        public List<OviaBarListMappingColumn> StandardColumns = new List<OviaBarListMappingColumn>();

        public static OviaBarListMappingStore LoadDefault()
        {
            OviaBarListMappingStore store = null;
            string path = FindMappingFilePath();

            if (path != "" && File.Exists(path))
            {
                try
                {
                    store = FromJson(File.ReadAllText(path, Encoding.UTF8));
                }
                catch
                {
                    store = null;
                }
            }

            if (store == null || store.StandardColumns.Count == 0)
            {
                store = CreateBuiltInDefault();
            }

            return store;
        }

        private static string FindMappingFilePath()
        {
            List<string> candidates = new List<string>();
            string startup = Application.StartupPath;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (startup != null && startup.Trim() != "")
            {
                candidates.Add(Path.Combine(startup, "Data", "Mapping", "barlist_mapping.json"));
                candidates.Add(Path.GetFullPath(Path.Combine(startup, "..", "..", "Data", "Mapping", "barlist_mapping.json")));
                candidates.Add(Path.GetFullPath(Path.Combine(startup, "..", "..", "..", "Data", "Mapping", "barlist_mapping.json")));
            }

            if (appData != null && appData.Trim() != "")
            {
                candidates.Add(Path.Combine(appData, "OVIA", "Mapping", "barlist_mapping.json"));
            }

            int i;

            for (i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return "";
        }

        public OviaBarListMappedTable BuildMappedTable(List<string> sourceHeaders)
        {
            OviaBarListMappedTable table = new OviaBarListMappedTable();
            Dictionary<string, OviaBarListMappedColumn> matchedByKey = new Dictionary<string, OviaBarListMappedColumn>(StringComparer.OrdinalIgnoreCase);
            List<OviaBarListMappedColumn> unmapped = new List<OviaBarListMappedColumn>();
            bool hasOviaSystemColumns = HasOviaSystemColumns(sourceHeaders);
            int i;

            for (i = 0; i < sourceHeaders.Count; i++)
            {
                string sourceHeader = sourceHeaders[i] == null ? "" : sourceHeaders[i].Trim();
                OviaBarListMappingColumn standard = null;

                if (!IsOviaSystemColumn(sourceHeader, hasOviaSystemColumns))
                {
                    standard = FindStandardColumn(sourceHeader);
                }

                if (standard != null && !matchedByKey.ContainsKey(standard.Key))
                {
                    OviaBarListMappedColumn col = new OviaBarListMappedColumn();
                    col.SourceIndex = i;
                    col.SourceHeader = sourceHeader;
                    col.StandardKey = standard.Key;
                    col.DisplayName = standard.DisplayName;
                    col.IsMapped = true;
                    matchedByKey.Add(standard.Key, col);
                    table.MatchedCount++;
                }
                else
                {
                    OviaBarListMappedColumn col = new OviaBarListMappedColumn();
                    col.SourceIndex = i;
                    col.SourceHeader = sourceHeader;
                    col.StandardKey = "";
                    col.DisplayName = sourceHeader == "" ? "Column" + (i + 1).ToString() : sourceHeader;
                    col.IsMapped = false;
                    unmapped.Add(col);
                }
            }

            for (i = 0; i < StandardColumns.Count; i++)
            {
                string key = StandardColumns[i].Key;

                if (matchedByKey.ContainsKey(key))
                {
                    table.Columns.Add(matchedByKey[key]);
                }
            }

            for (i = 0; i < unmapped.Count; i++)
            {
                table.Columns.Add(unmapped[i]);
            }

            return table;
        }

        private bool HasOviaSystemColumns(List<string> headers)
        {
            bool hasRowType = false;
            bool hasSourceRowNo = false;
            int i;

            for (i = 0; i < headers.Count; i++)
            {
                string value = NormalizeToken(headers[i]);

                if (value == "ROWTYPE")
                {
                    hasRowType = true;
                }
                else if (value == "SOURCEROWNO")
                {
                    hasSourceRowNo = true;
                }
            }

            return hasRowType && hasSourceRowNo;
        }

        private bool IsOviaSystemColumn(string header, bool hasOviaSystemColumns)
        {
            if (!hasOviaSystemColumns)
            {
                return false;
            }

            string value = NormalizeToken(header);

            return value == "NO" || value == "ROWTYPE" || value == "SOURCEROWNO";
        }

        private OviaBarListMappingColumn FindStandardColumn(string header)
        {
            string normalizedHeader = NormalizeToken(header);

            if (normalizedHeader == "")
            {
                return null;
            }

            OviaBarListMappingColumn best = null;
            int bestScore = -1;
            int i;

            for (i = 0; i < StandardColumns.Count; i++)
            {
                OviaBarListMappingColumn col = StandardColumns[i];
                int score = GetMatchScore(normalizedHeader, col);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = col;
                }
            }

            if (bestScore <= 0)
            {
                return null;
            }

            return best;
        }

        private int GetMatchScore(string normalizedHeader, OviaBarListMappingColumn col)
        {
            int best = 0;
            int i;

            if (NormalizeToken(col.DisplayName) == normalizedHeader)
            {
                best = Math.Max(best, 1000 + col.Priority);
            }

            for (i = 0; i < col.Aliases.Count; i++)
            {
                string alias = NormalizeToken(col.Aliases[i]);

                if (alias == "")
                {
                    continue;
                }

                if (alias == normalizedHeader)
                {
                    best = Math.Max(best, 900 + col.Priority);
                }
                else if (alias.Length >= 3 && normalizedHeader.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    best = Math.Max(best, 500 + col.Priority);
                }
                else if (normalizedHeader.Length >= 4 && alias.IndexOf(normalizedHeader, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    best = Math.Max(best, 300 + col.Priority);
                }
            }

            return best;
        }

        private static string NormalizeToken(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Trim().ToUpperInvariant();
            value = value.Replace(" ", "");
            value = value.Replace("\t", "");
            value = value.Replace("\r", "");
            value = value.Replace("\n", "");
            value = value.Replace("_", "");
            value = value.Replace("-", "");
            value = value.Replace(".", "");
            value = value.Replace(":", "");
            value = value.Replace("/", "");
            value = value.Replace("\\", "");
            value = value.Replace("(", "");
            value = value.Replace(")", "");
            value = value.Replace("[", "");
            value = value.Replace("]", "");
            value = value.Replace("{", "");
            value = value.Replace("}", "");
            value = value.Replace("㎜", "MM");
            value = value.Replace("㎡", "M2");
            value = value.Replace("㎥", "M3");

            return value;
        }

        private static OviaBarListMappingStore FromJson(string json)
        {
            OviaBarListMappingStore store = new OviaBarListMappingStore();

            store.Version = ExtractString(json, "version");
            store.UpdatedAt = ExtractString(json, "updatedAt");

            Match arrayMatch = Regex.Match(json, "\\\"standardColumns\\\"\\s*:\\s*\\[(.*)\\]", RegexOptions.Singleline);

            if (arrayMatch.Success)
            {
                string arrayText = arrayMatch.Groups[1].Value;
                MatchCollection objectMatches = Regex.Matches(arrayText, "\\{[^\\{\\}]*\\}", RegexOptions.Singleline);
                int i;

                for (i = 0; i < objectMatches.Count; i++)
                {
                    string objectText = objectMatches[i].Value;
                    string key = ExtractString(objectText, "key");
                    string displayName = ExtractString(objectText, "displayName");

                    if (key.Trim() == "" || displayName.Trim() == "")
                    {
                        continue;
                    }

                    OviaBarListMappingColumn col = new OviaBarListMappingColumn();
                    col.Key = key.Trim();
                    col.DisplayName = displayName.Trim();
                    col.DataType = ExtractString(objectText, "dataType").Trim();
                    col.Priority = ExtractInt(objectText, "priority", 100);
                    col.Aliases = ExtractStringArray(objectText, "aliases");

                    if (!ContainsText(col.Aliases, col.DisplayName))
                    {
                        col.Aliases.Add(col.DisplayName);
                    }

                    store.StandardColumns.Add(col);
                }
            }

            if (store.Version == null || store.Version.Trim() == "")
            {
                store.Version = "json-local";
            }

            return store;
        }

        private static string ExtractString(string source, string name)
        {
            Match match = Regex.Match(source, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"", RegexOptions.Singleline);

            if (!match.Success)
            {
                return "";
            }

            return UnescapeJsonString(match.Groups[1].Value);
        }

        private static int ExtractInt(string source, string name, int defaultValue)
        {
            Match match = Regex.Match(source, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*([0-9]+)", RegexOptions.Singleline);

            if (!match.Success)
            {
                return defaultValue;
            }

            int value;

            if (Int32.TryParse(match.Groups[1].Value, out value))
            {
                return value;
            }

            return defaultValue;
        }

        private static List<string> ExtractStringArray(string source, string name)
        {
            List<string> result = new List<string>();
            Match match = Regex.Match(source, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);

            if (!match.Success)
            {
                return result;
            }

            MatchCollection items = Regex.Matches(match.Groups[1].Value, "\\\"([^\\\"]*)\\\"", RegexOptions.Singleline);
            int i;

            for (i = 0; i < items.Count; i++)
            {
                string value = UnescapeJsonString(items[i].Groups[1].Value).Trim();

                if (value != "" && !ContainsText(result, value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static string UnescapeJsonString(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static bool ContainsText(List<string> list, string value)
        {
            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (String.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static OviaBarListMappingStore CreateBuiltInDefault()
        {
            OviaBarListMappingStore store = new OviaBarListMappingStore();
            store.Version = "built-in-2026.05.22.001";
            store.UpdatedAt = "2026-05-22";

            store.AddColumn("no", "번호", "number_or_text", 100, "NO", "NO.", "No", "No.", "순번", "번호", "번", "숫자");
            store.AddColumn("part", "부위", "text", 100, "부위", "위치", "층", "구간", "시공부위", "ZONE", "AREA", "LOCATION");
            store.AddColumn("mark", "부호/명칭", "text", 100, "부호", "명칭", "부호명", "부호 및 명칭", "BAR MARK", "MARK", "기호", "철근명", "ITEM");
            store.AddColumn("shape", "철근형상", "text_or_image", 100, "형상", "철근형상", "SHAPE", "BENT", "BAR SHAPE", "절곡형상", "형상번호", "형번", "SHAPE NO");
            store.AddColumn("dia", "규격", "rebar_diameter", 100, "규격", "DIA", "D", "직경", "철근규격", "BAR DIA", "SIZE", "강종");
            store.AddColumn("length_mm", "길이(mm)", "number", 100, "길이", "L", "LENGTH", "절단길이", "산출길이", "MM", "길이MM", "길이(MM)");
            store.AddColumn("qty_ea", "수량(EA)", "number", 100, "수량", "EA", "QTY", "QUANTITY", "개수", "갯수", "본수", "수량EA", "수량(EA)");
            store.AddColumn("total_length_m", "총길이(M)", "number", 90, "총길이", "총연장", "연장", "TOTAL LENGTH", "T.L", "M", "총길이M", "총길이(M)");
            store.AddColumn("total_weight_ton", "총중량(TON)", "number", 90, "총중량", "중량", "WEIGHT", "WT", "TON", "톤", "KG", "TOTAL WEIGHT", "총중량TON", "총중량(TON)");
            store.AddColumn("remark", "비고", "text", 80, "비고", "REMARK", "NOTE", "메모", "특기사항", "비고사항");

            return store;
        }

        private void AddColumn(string key, string displayName, string dataType, int priority, params string[] aliases)
        {
            OviaBarListMappingColumn col = new OviaBarListMappingColumn();
            col.Key = key;
            col.DisplayName = displayName;
            col.DataType = dataType;
            col.Priority = priority;

            int i;

            for (i = 0; i < aliases.Length; i++)
            {
                if (aliases[i] != null && aliases[i].Trim() != "" && !ContainsText(col.Aliases, aliases[i].Trim()))
                {
                    col.Aliases.Add(aliases[i].Trim());
                }
            }

            if (!ContainsText(col.Aliases, displayName))
            {
                col.Aliases.Add(displayName);
            }

            StandardColumns.Add(col);
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
