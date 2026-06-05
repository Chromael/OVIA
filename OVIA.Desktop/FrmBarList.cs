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
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmBarList : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout
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
        private RebarShapeRepository shapeRepository;
        private RebarShapeRenderer shapeRenderer = new RebarShapeRenderer();
        private CadShapeRenderer cadShapeRenderer = new CadShapeRenderer();
        private int lastMappingMatchCount = 0;
        private int lastMappingTotalHeaderCount = 0;
        private string lastMappingVersion = "";

        private const int GridZoomMinPercent = 100;
        private const int GridZoomMaxPercent = 220;
        private const int GridZoomStepPercent = 10;
        private const int GridBaseHeaderHeight = 34;
        private const int GridBaseRowHeight = 62;
        private const int GridBaseRowHeaderWidth = 48;
        private int gridZoomPercent = GridZoomMinPercent;

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

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
        private readonly Color BrandCyan = Color.FromArgb(64, 156, 255);
        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;
        private readonly Color ModifiedCellTextColor = OviaFluentTheme.Danger;

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

            shapeRepository = RebarShapeRepository.CreateDefault();

            BuildUI();

            if (this.initialFilePath.Trim() != "" && File.Exists(this.initialFilePath))
            {
                LoadCsv(this.initialFilePath, true);
            }
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA " + GetScreenTitleText();
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
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
            BuildCommandBar(contentPanel);
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

        public void ApplyWorkspaceLayout()
        {
            UpdateScrollableContentSize();
            ResetScrollToTopLeft();
            QueueResetScrollToTopLeft();
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
            BuildExplorerHeader(parent, "메인  ›  공사관리  ›  공사별 BarList  ›  " + GetScreenTitleText());
        }

        private void BuildExplorerHeader(Control parent, string pathText)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                pathText,
                delegate { NavigateBackToProjectBarListList(); },
                delegate { NavigateBackToProjectBarListList(); },
                delegate
                {
                    if (lastLoadedFilePath.Trim() != "" && File.Exists(lastLoadedFilePath))
                    {
                        LoadCsv(lastLoadedFilePath, false);
                    }
                },
                delegate { RequestLogout(); },
                true,
                true
            );
        }


        private Panel CreatePathAddressBar(string pathText)
        {
            Panel panel = new Panel();
            panel.BackColor = Color.White;
            panel.Margin = Padding.Empty;
            panel.Padding = new Padding(10, 6, 10, 0);

            TextBox textBox = null;
            LinkLabel breadcrumb = CreateBreadcrumbLabel();
            breadcrumb.Text = pathText == null ? "" : pathText;
            breadcrumb.Location = new Point(10, 6);
            breadcrumb.Size = new Size(940, 22);
            breadcrumb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            breadcrumb.Links.Add(0, "메인".Length, "MAIN");
            int projectStart = breadcrumb.Text.IndexOf("공사관리");
            if (projectStart >= 0)
            {
                breadcrumb.Links.Add(projectStart, "공사관리".Length, "PROJECT_MANAGER");
            }

            int barListListStart = breadcrumb.Text.IndexOf("공사별 BarList");
            if (barListListStart >= 0)
            {
                breadcrumb.Links.Add(barListListStart, "공사별 BarList".Length, "PROJECT_BARLIST_LIST");
            }

            breadcrumb.LinkClicked += Breadcrumb_LinkClicked;
            breadcrumb.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (IsPathBlankAreaClick(breadcrumb, e))
                {
                    ShowPathEditMode(breadcrumb, textBox);
                }
            };
            panel.Controls.Add(breadcrumb);

            textBox = new TextBox();
            textBox.Text = NormalizeCopyPath(pathText);
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            textBox.ForeColor = Color.Black;
            textBox.BackColor = Color.White;
            textBox.Location = new Point(10, 7);
            textBox.Size = new Size(940, 20);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = Padding.Empty;
            textBox.TabStop = false;
            textBox.Visible = false;
            textBox.Click += delegate
            {
                textBox.SelectAll();
            };
            textBox.Enter += delegate
            {
                textBox.SelectAll();
            };
            textBox.KeyDown += PathCopy_KeyDown;

            textBox.Leave += delegate
            {
                HidePathEditMode(breadcrumb, textBox);
            };
            panel.Controls.Add(textBox);

            return panel;
        }

        private bool IsPathBlankAreaClick(LinkLabel breadcrumb, MouseEventArgs e)
        {
            if (breadcrumb == null || e == null || e.Button != MouseButtons.Left)
            {
                return false;
            }

            int textWidth = TextRenderer.MeasureText(
                breadcrumb.Text,
                breadcrumb.Font,
                new Size(int.MaxValue, breadcrumb.Height),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
            ).Width;

            return e.X > textWidth + 8;
        }

        private string NormalizeCopyPath(string pathText)
        {
            if (pathText == null)
            {
                return "";
            }

            return pathText.Replace("  ›  ", "\\");
        }

        private void ShowPathEditMode(LinkLabel breadcrumb, TextBox textBox)
        {
            if (breadcrumb != null)
            {
                breadcrumb.Visible = false;
            }

            if (textBox != null)
            {
                textBox.Visible = true;
                textBox.BringToFront();
                textBox.Focus();
                textBox.SelectAll();
                OviaPathEditExitFilter.Attach(breadcrumb, textBox);
            }
        }

        private void HidePathEditMode(LinkLabel breadcrumb, TextBox textBox)
        {
            if (textBox != null)
            {
                textBox.Visible = false;
            }

            if (breadcrumb != null)
            {
                breadcrumb.Visible = true;
                breadcrumb.BringToFront();
            }

            OviaPathEditExitFilter.Detach();
        }

        private void PathCopy_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textBox = sender as TextBox;

            if (textBox == null)
            {
                return;
            }

            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
            {
                this.ActiveControl = null;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private Button CreateExplorerButton(string text, string tip)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(30, 30);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.NavigationHover;
            button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.NavigationSelected;
            button.Font = new Font("Segoe MDL2 Assets", 9.5F, FontStyle.Regular);
            button.ForeColor = Color.Black;
            button.BackColor = SurfaceColor;
            button.TabStop = false;

            if (windowToolTip != null)
            {
                windowToolTip.SetToolTip(button, tip);
            }

            return button;
        }

        private void StyleExplorerLogoutButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 53, 69);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
            button.BackColor = SurfaceColor;
            button.ForeColor = Color.Black;

            button.MouseEnter += delegate
            {
                button.BackColor = Color.FromArgb(220, 53, 69);
                button.ForeColor = Color.White;
            };

            button.MouseLeave += delegate
            {
                button.BackColor = SurfaceColor;
                button.ForeColor = Color.Black;
            };

            button.SizeChanged += delegate
            {
                ApplyExplorerButtonRadius(button, 2);
            };

            ApplyExplorerButtonRadius(button, 2);
        }

        private void ApplyExplorerButtonRadius(Button button, int radius)
        {
            if (button == null || button.Width <= 0 || button.Height <= 0)
            {
                return;
            }

            int diameter = Math.Max(1, radius * 2);
            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            GraphicsPath path = new GraphicsPath();

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter - 1, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter - 1, rect.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();

            Region oldRegion = button.Region;
            button.Region = new Region(path);
            path.Dispose();

            if (oldRegion != null)
            {
                oldRegion.Dispose();
            }
        }

        private void StyleExplorerButtonInactive(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.ForeColor = Color.FromArgb(175, 181, 190);
            button.Cursor = Cursors.Default;
            button.FlatAppearance.MouseOverBackColor = SurfaceColor;
            button.FlatAppearance.MouseDownBackColor = SurfaceColor;
        }

        private LinkLabel CreateBreadcrumbLabel()
        {
            LinkLabel label = new LinkLabel();
            label.Text = "";
            label.AutoSize = false;
            label.Size = new Size(860, 22);
            label.Location = new Point(38, 68);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            label.BackColor = Color.White;
            label.ForeColor = Color.Black;
            label.LinkColor = Color.Black;
            label.ActiveLinkColor = OviaFluentTheme.Accent;
            label.VisitedLinkColor = Color.Black;
            label.DisabledLinkColor = Color.Black;
            label.LinkBehavior = LinkBehavior.NeverUnderline;
            label.TabStop = false;
            return label;
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
            button.ForeColor = OviaFluentTheme.TextTertiary;
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
            Color lineColor = OviaFluentTheme.ControlBorder;
            Color textColor = OviaFluentTheme.TextTertiary;

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
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    workspace.NavigateToProjectManager();
                    return;
                }

                FrmProjectManager form = new FrmProjectManager(companyId, userId);
                ShowReplacementWindow(form);
                return;
            }

            if (target == "MAIN")
            {
                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    workspace.NavigateToMain();
                    return;
                }

                this.Close();
                return;
            }

            if (target == "PROJECT_BARLIST_LIST")
            {
                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    workspace.NavigateToProjectBarListList(projectNo, projectName, clientName, projectStatus);
                    return;
                }

                FrmProjectBarListList form = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus);
                ShowReplacementWindow(form);
            }
        }

        private void BuildProjectInfo(Control parent)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = new Point(34, 128);
            card.Size = new Size(1168, 72);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.SurfaceColor = SurfaceColor;
            parent.Controls.Add(card);

            lblProjectTitle = new Label();
            lblProjectTitle.Text = GetProjectTitleText();
            lblProjectTitle.AutoSize = true;
            lblProjectTitle.Font = OviaFluentTheme.FontTitle(14F, FontStyle.Bold);
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
            card.Location = new Point(34, 215);
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
            OviaFluentTheme.ApplyTextBox(txtFilePath);
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
            recentButton.StartColor = OviaFluentTheme.Accent;
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
            saveProjectButton.StartColor = OviaFluentTheme.Success;
            saveProjectButton.EndColor = OviaFluentTheme.Success;
            saveProjectButton.Click += SaveProjectBarList_Click;
            card.Controls.Add(saveProjectButton);

            Label guide = new Label();
            guide.Text = "※ AutoCAD에서 OVIABOX → OVIABOXTABLE을 실행하면 새 추출 CSV를 감지해 자동 입력합니다. 반드시 확인 후 저장하세요.";
            guide.AutoSize = true;
            guide.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            guide.ForeColor = OviaFluentTheme.Danger;
            guide.BackColor = Color.White;
            guide.Location = new Point(24, 74);
            card.Controls.Add(guide);
        }

        private void BuildSummary(Control parent)
        {
            AddSummaryCard(parent, "행 개수", "0", new Point(34, 333), out lblRowCount);
            AddSummaryCard(parent, "총 수량", "0", new Point(260, 333), out lblTotalQty);
            AddSummaryCard(parent, "총길이(M)", "0", new Point(486, 333), out lblTotalLength);
            AddSummaryCard(parent, "중량 합계", "0", new Point(712, 333), out lblTotalWeight);

            lblStatus = new Label();
            lblStatus.Text = "AutoCAD에서 가져오거나 CSV를 선택하세요.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(240, 28);
            lblStatus.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = OviaFluentTheme.AccentLight;
            lblStatus.Location = new Point(948, 358);
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
            titleLabel.Font = OviaFluentTheme.FontTitle(9F, FontStyle.Bold);
            titleLabel.ForeColor = TextSub;
            titleLabel.BackColor = Color.White;
            titleLabel.Location = new Point(18, 14);
            card.Controls.Add(titleLabel);

            valueLabel = new Label();
            valueLabel.Text = value;
            valueLabel.AutoSize = true;
            valueLabel.Font = OviaFluentTheme.FontTitle(18F, FontStyle.Bold);
            valueLabel.ForeColor = TextDark;
            valueLabel.BackColor = Color.White;
            valueLabel.Location = new Point(16, 36);
            card.Controls.Add(valueLabel);
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            EnableGridDoubleBuffering(grid);
            grid.Location = new Point(34, 430);
            grid.Size = new Size(1168, 209);
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
            grid.RowHeadersWidth = ScaleGridSize(GridBaseRowHeaderWidth);
            grid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.RowHeadersDefaultCellStyle.ForeColor = TextSub;
            grid.RowHeadersDefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.2F), FontStyle.Regular);
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
            grid.MouseWheel += Grid_MouseWheel;

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.7F), FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersHeight = ScaleGridSize(GridBaseHeaderHeight);

            grid.DefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.7F), FontStyle.Regular);
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.RowTemplate.Height = ScaleGridSize(GridBaseRowHeight);

            OviaFluentTheme.ApplyDataGrid(grid);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;

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
            coverButton.StartColor = OviaFluentTheme.TextSecondary;
            coverButton.EndColor = OviaFluentTheme.TextSecondary;
            coverButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(coverButton);

            OviaBarListButton detailButton = new OviaBarListButton();
            detailButton.Text = "내역출력";
            detailButton.Location = new Point(140, 690);
            detailButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            detailButton.Size = new Size(94, 34);
            detailButton.StartColor = OviaFluentTheme.TextSecondary;
            detailButton.EndColor = OviaFluentTheme.TextSecondary;
            detailButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(detailButton);

            OviaBarListButton tagButton = new OviaBarListButton();
            tagButton.Text = "태그발행";
            tagButton.Location = new Point(246, 690);
            tagButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            tagButton.Size = new Size(94, 34);
            tagButton.StartColor = OviaFluentTheme.TextSecondary;
            tagButton.EndColor = OviaFluentTheme.TextSecondary;
            tagButton.Click += OutputPlaceholder_Click;
            parent.Controls.Add(tagButton);

            OviaBarListButton deleteButton = new OviaBarListButton();
            deleteButton.Text = "선택 행 삭제";
            deleteButton.Location = new Point(352, 690);
            deleteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            deleteButton.Size = new Size(110, 34);
            deleteButton.StartColor = OviaFluentTheme.Danger;
            deleteButton.EndColor = OviaFluentTheme.Danger;
            deleteButton.Click += DeleteRows_Click;
            parent.Controls.Add(deleteButton);

            OviaBarListButton saveCsvButton = new OviaBarListButton();
            saveCsvButton.Text = "CSV 저장";
            saveCsvButton.Location = new Point(474, 690);
            saveCsvButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            saveCsvButton.Size = new Size(94, 34);
            saveCsvButton.StartColor = OviaFluentTheme.Success;
            saveCsvButton.EndColor = OviaFluentTheme.Success;
            saveCsvButton.Click += SaveCsv_Click;
            parent.Controls.Add(saveCsvButton);

            Label footer = new Label();
            footer.Text = "※ 불러온 내용은 반드시 검토 후 저장해야 공사별 BarList에 반영됩니다.";
            footer.AutoSize = true;
            footer.Font = OviaFluentTheme.FontStatus(8.0F, FontStyle.Bold);
            footer.ForeColor = OviaFluentTheme.Danger;
            footer.BackColor = SurfaceColor;
            footer.Location = new Point(590, 700);
            footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parent.Controls.Add(footer);
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(BaseClientWidth, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "BARLIST");
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

        private void AutoCadImport_Click(object sender, EventArgs e)
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();

            if (!report.IsCurrentDevelopmentAutoCadReady())
            {
                MessageBox.Show(
                    report.GetAutoCadExtractionBlockMessage() + "\r\n\r\n" + report.GetDisplayText(),
                    "OVIA AutoCAD 확인",
                    MessageBoxButtons.OK,
                    report.OverallStatus == OviaEnvironmentStatus.Blocked ? MessageBoxIcon.Error : MessageBoxIcon.Warning
                );

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
            autoCadWatcher.Filter = "*.csv";
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

                NormalizeCadShapeJsonFilesForSave(filePath);
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
                NormalizeCadShapeJsonFilesForSave(dialog.FileName);
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

            if (IsRebarShapeColumn(e.ColumnIndex))
            {
                OpenShapePickerForCell(e.RowIndex, e.ColumnIndex);
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

            if (e.Control && !e.Shift && e.KeyCode == Keys.D0)
            {
                ResetGridZoom();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        private void Grid_MouseWheel(object sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) != Keys.Control)
            {
                return;
            }

            HandledMouseEventArgs handled = e as HandledMouseEventArgs;

            if (handled != null)
            {
                handled.Handled = true;
            }

            if (e.Delta > 0)
            {
                ChangeGridZoom(GridZoomStepPercent);
            }
            else if (e.Delta < 0)
            {
                ChangeGridZoom(-GridZoomStepPercent);
            }
        }

        private void ChangeGridZoom(int deltaPercent)
        {
            int nextPercent = gridZoomPercent + deltaPercent;

            if (nextPercent < GridZoomMinPercent)
            {
                nextPercent = GridZoomMinPercent;
            }

            if (nextPercent > GridZoomMaxPercent)
            {
                nextPercent = GridZoomMaxPercent;
            }

            if (nextPercent == gridZoomPercent)
            {
                return;
            }

            gridZoomPercent = nextPercent;
            ApplyGridZoomLayout();

            if (lblStatus != null)
            {
                lblStatus.Text = "BarList 보기 배율 " + gridZoomPercent.ToString() + "%";
                lblStatus.ForeColor = TextSub;
            }
        }

        private void ResetGridZoom()
        {
            if (gridZoomPercent == GridZoomMinPercent)
            {
                return;
            }

            gridZoomPercent = GridZoomMinPercent;
            ApplyGridZoomLayout();

            if (lblStatus != null)
            {
                lblStatus.Text = "BarList 보기 배율 100%로 복귀했습니다.";
                lblStatus.ForeColor = TextSub;
            }
        }

        private void ApplyGridZoomLayout()
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            grid.SuspendLayout();

            try
            {
                grid.RowHeadersWidth = ScaleGridSize(GridBaseRowHeaderWidth);
                grid.ColumnHeadersHeight = ScaleGridSize(GridBaseHeaderHeight);
                grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.7F), FontStyle.Bold);
                grid.DefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.7F), FontStyle.Regular);
                grid.RowHeadersDefaultCellStyle.Font = new Font("맑은 고딕", ScaleGridFont(8.2F), FontStyle.Regular);
                ApplyGridColumnStyle();
            }
            finally
            {
                grid.ResumeLayout();
                grid.Invalidate();
            }
        }

        private int ScaleGridSize(int baseValue)
        {
            int scaled = (int)Math.Round(baseValue * (gridZoomPercent / 100.0));

            if (scaled < 1)
            {
                scaled = 1;
            }

            return scaled;
        }

        private float ScaleGridFont(float baseSize)
        {
            float scaled = baseSize * gridZoomPercent / 100F;

            if (scaled < baseSize)
            {
                scaled = baseSize;
            }

            if (scaled > 22F)
            {
                scaled = 22F;
            }

            return scaled;
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
            Color headerBack = rowSelected ? Color.FromArgb(255, 235, 112) : OviaFluentTheme.HeaderBackground;
            Color headerFore = rowSelected ? TextDark : TextSub;

            using (SolidBrush brush = new SolidBrush(headerBack))
            {
                e.Graphics.FillRectangle(brush, headerBounds);
            }

            using (Pen pen = new Pen(rowSelected ? Color.FromArgb(188, 136, 0) : OviaFluentTheme.CardBorder, 1F))
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

            if (IsRebarShapeColumn(e.ColumnIndex))
            {
                PaintRebarShapeGridCell(e);
                return;
            }

            DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];

            if (!cell.Selected)
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

            e.Handled = true;
            PaintGridCellBase(e, true);
            e.PaintContent(e.CellBounds);
            PaintGridCellBorder(e.Graphics, e.CellBounds, true);
        }

        private void PaintGridCellBase(DataGridViewCellPaintingEventArgs e, bool selected)
        {
            Color backColor = selected ? Color.FromArgb(255, 248, 205) : Color.White;

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }
        }

        private void PaintGridCellBorder(Graphics graphics, Rectangle bounds, bool selected)
        {
            Color borderColor = selected ? Color.FromArgb(226, 189, 67) : OviaFluentTheme.ControlBorder;
            Rectangle rect = new Rectangle(bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

            using (Pen pen = new Pen(borderColor, 1F))
            {
                graphics.DrawRectangle(pen, rect);
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

        public bool CanLeaveWorkspaceScreen()
        {
            return ConfirmDiscardUnsavedForNavigation();
        }

        public void BeforeLeaveWorkspaceScreen()
        {
            StopAutoCadWatcher();
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
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToProjectBarListList(projectNo, projectName, clientName, projectStatus);
                return;
            }

            FrmProjectBarListList form = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus);
            suppressUnsavedClosePrompt = true;
            ShowReplacementWindow(form);
        }

        private void NavigateToMain()
        {
            if (!ConfirmDiscardUnsavedForNavigation())
            {
                return;
            }

            suppressUnsavedClosePrompt = true;
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.NavigateToMain();
                return;
            }

            this.Close();
        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (workspace != null)
            {
                workspace.RequestLogout();
                return;
            }

            this.Close();
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

            /*
             * OVIABOXTABLE은 이제 스마트 통합 추출 명령입니다.
             * 과거 테스트용 OVIAGRIDTABLE 파일명도 자동 입력 대상에 포함해 둡니다.
             */
            string[] files = Directory.GetFiles(desktop, "OVIA_*Table_*.csv");

            if (files == null || files.Length == 0)
            {
                return "";
            }

            List<string> candidates = new List<string>();
            int i;

            for (i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);

                if (!IsOviaAutoCadTableCsvFile(fileName))
                {
                    continue;
                }

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

        private bool IsOviaAutoCadTableCsvFile(string fileName)
        {
            if (fileName == null)
            {
                return false;
            }

            return fileName.StartsWith("OVIA_BoxTable_", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("OVIA_GridTable_", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("OVIA_GridTable_Fallback_", StringComparison.OrdinalIgnoreCase);
        }

        private void LoadCsv(string filePath, bool loadAsSaved)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count == 0)
                {
                    lblStatus.Text = "CSV 파일에 읽을 데이터가 없습니다.";
                    lblStatus.ForeColor = OviaFluentTheme.Danger;

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
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CSV 불러오기 오류 - " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
            }
        }

        private void BindCsvRows(List<List<string>> rows)
        {
            rows = RemoveRuntimeCsvColumnsForDisplay(rows);

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

                ApplyUnitConversionAfterMapping(mappedTable);
                ApplyGridColumnStyle();

                // 형상번호 표시 컬럼은 수동 형상코드 선택 시 코드값을 보여주기 위해 유지합니다.
                // 컬럼이 추가된 뒤 기존 row.Tag 원본값 배열이 밀리면 모든 컬럼이 수정된 것처럼 빨간색으로 보일 수 있으므로,
                // CSV를 새로 불러온 직후에는 현재 화면 상태를 원본값으로 다시 잡습니다.
                ResetAllRowOriginalValuesToCurrent();
            }
            finally
            {
                grid.ResumeLayout();
                EndGridSelectionUpdate();
            }
        }


        private List<List<string>> RemoveRuntimeCsvColumnsForDisplay(List<List<string>> rows)
        {
            if (rows == null || rows.Count == 0 || rows[0] == null)
            {
                return rows;
            }

            List<int> keepIndexes = new List<int>();
            int i;

            for (i = 0; i < rows[0].Count; i++)
            {
                string header = rows[0][i] == null ? "" : rows[0][i].Trim();

                // AutoCAD 추출 CSV의 No/RowType/SourceRowNo는 OVIA 내부 관리용입니다.
                // 이 값이 매핑 테이블에서 사용자용 "번호"로 먼저 잡히면
                // CAD 원본 번호 7, 8, 9 대신 화면에 1, 2, 3처럼 내부 순번이 표시됩니다.
                // 따라서 화면 매핑 전 단계에서 이 3개 내부 컬럼만 제거하고,
                // OVIA_CAD_SHAPE_JSON 등 렌더링에 필요한 숨김 컬럼은 그대로 유지합니다.
                if (IsRuntimeCsvColumn(header))
                {
                    continue;
                }

                keepIndexes.Add(i);
            }

            if (keepIndexes.Count == rows[0].Count)
            {
                return rows;
            }

            List<List<string>> filtered = new List<List<string>>();
            int r;

            for (r = 0; r < rows.Count; r++)
            {
                List<string> sourceRow = rows[r];
                List<string> newRow = new List<string>();

                for (i = 0; i < keepIndexes.Count; i++)
                {
                    int sourceIndex = keepIndexes[i];

                    if (sourceRow != null && sourceIndex >= 0 && sourceIndex < sourceRow.Count)
                    {
                        newRow.Add(sourceRow[sourceIndex]);
                    }
                    else
                    {
                        newRow.Add("");
                    }
                }

                filtered.Add(newRow);
            }

            return filtered;
        }

        private bool IsRuntimeCsvColumn(string header)
        {
            if (header == null)
            {
                return false;
            }

            string value = header.Trim();

            if (value.Equals("No", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("RowType", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("SourceRowNo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool IsRebarShapeColumn(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            return IsRebarShapeHeader(grid.Columns[columnIndex].HeaderText);
        }

        private bool IsRebarShapeHeader(string header)
        {
            if (header == null)
            {
                return false;
            }

            string value = header.Trim();

            if (value == "")
            {
                return false;
            }

            if (ContainsAny(value, "형번", "형상번호", "ShapeCodeRaw", "ShapeVector", "ShapeSvg", "ShapeSource", "ShapeReview"))
            {
                return false;
            }

            return value.IndexOf("철근형상", StringComparison.OrdinalIgnoreCase) >= 0
                || value.Equals("형상", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Shape", StringComparison.OrdinalIgnoreCase)
                || value.Equals("BENT", StringComparison.OrdinalIgnoreCase);
        }

        private RebarShapeRepository GetShapeRepository()
        {
            if (shapeRepository == null)
            {
                shapeRepository = RebarShapeRepository.CreateDefault();
            }

            return shapeRepository;
        }

        private void PaintRebarShapeGridCell(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            object value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            string rawText = value == null ? "" : value.ToString();
            string shapeNoText = GetShapeNumberText(e.RowIndex);
            RebarShapeInfo shape = GetShapeRepository().FindByRawValue(rawText);

            if (shape == null && shapeNoText != "")
            {
                shape = GetShapeRepository().FindByRawValue(shapeNoText);
                if (shape != null)
                {
                    rawText = shape.DisplayCode;
                }
            }

            bool selected = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected;
            string cadShapePath = ResolveCadShapeJsonPath(GetCadShapeJsonText(e.RowIndex));

            e.Handled = true;
            PaintGridCellBase(e, selected);

            if (!IsManualShapeOverrideRow(e.RowIndex) && cadShapePath != "" && File.Exists(cadShapePath))
            {
                cadShapeRenderer.DrawCadShape(e.Graphics, e.CellBounds, cadShapePath, selected, GetShapeDimensionText(e.RowIndex));
                PaintGridCellBorder(e.Graphics, e.CellBounds, selected);
                return;
            }

            string dimensionText = GetShapeDimensionText(e.RowIndex);
            shapeRenderer.DrawShape(e.Graphics, e.CellBounds, shape, rawText, selected, dimensionText);
            PaintGridCellBorder(e.Graphics, e.CellBounds, selected);
        }

        private string GetCadShapeJsonText(int rowIndex)
        {
            return GetFirstExistingCellText(rowIndex, new string[] { "OVIA_CAD_SHAPE_JSON", "CAD_SHAPE_JSON", "OVIA CAD SHAPE JSON" });
        }

        private string GetShapeSourceText(int rowIndex)
        {
            return GetFirstExistingCellText(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" });
        }

        private bool IsManualShapeOverrideRow(int rowIndex)
        {
            string source = GetShapeSourceText(rowIndex);

            return source != null && source.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveCadShapeJsonPath(string value)
        {
            if (value == null)
            {
                return "";
            }

            string path = value.Trim();

            if (path == "")
            {
                return "";
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string csvPath = txtFilePath == null || txtFilePath.Text == null ? "" : txtFilePath.Text.Trim();

            if (csvPath == "")
            {
                return path;
            }

            string dir = Path.GetDirectoryName(csvPath);

            if (dir == null || dir.Trim() == "")
            {
                return path;
            }

            return Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
        }


        private void NormalizeCadShapeJsonFilesForSave(string targetCsvPath)
        {
            if (grid == null || grid.Rows.Count == 0 || targetCsvPath == null || targetCsvPath.Trim() == "")
            {
                return;
            }

            int cadShapeColumnIndex = FindExactColumnIndexByHeaders(new string[] { "OVIA_CAD_SHAPE_JSON", "CAD_SHAPE_JSON", "OVIA CAD SHAPE JSON" });

            if (cadShapeColumnIndex < 0)
            {
                return;
            }

            string targetDir = Path.GetDirectoryName(targetCsvPath);

            if (targetDir == null || targetDir.Trim() == "")
            {
                return;
            }

            string shapeDir = Path.Combine(targetDir, "Shapes");
            bool shapeDirCreated = false;
            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                object rawValue = grid.Rows[r].Cells[cadShapeColumnIndex].Value;
                string savedValue = rawValue == null ? "" : rawValue.ToString().Trim();

                if (savedValue == "")
                {
                    continue;
                }

                string sourcePath = ResolveCadShapeJsonPath(savedValue);

                if (sourcePath == "" || !File.Exists(sourcePath))
                {
                    continue;
                }

                if (!shapeDirCreated)
                {
                    Directory.CreateDirectory(shapeDir);
                    shapeDirCreated = true;
                }

                string fileName = Path.GetFileName(sourcePath);

                if (fileName == null || fileName.Trim() == "")
                {
                    fileName = "row_" + (r + 1).ToString("000") + "_shape.json";
                }

                string targetPath = Path.Combine(shapeDir, fileName);

                if (!IsSameFullPath(sourcePath, targetPath))
                {
                    File.Copy(sourcePath, targetPath, true);
                }

                grid.Rows[r].Cells[cadShapeColumnIndex].Value = "Shapes/" + fileName;
            }
        }

        private int FindExactColumnIndexByHeaders(string[] headers)
        {
            if (grid == null || headers == null)
            {
                return -1;
            }

            int i;
            int j;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();

                for (j = 0; j < headers.Length; j++)
                {
                    string target = headers[j] == null ? "" : headers[j].Trim();

                    if (target == "")
                    {
                        continue;
                    }

                    if (header.Equals(target, StringComparison.OrdinalIgnoreCase) || name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private bool IsSameFullPath(string pathA, string pathB)
        {
            if (pathA == null || pathB == null)
            {
                return false;
            }

            try
            {
                string a = Path.GetFullPath(pathA).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string b = Path.GetFullPath(pathB).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return a.Equals(b, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void OpenShapePickerForCell(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (grid.Rows[rowIndex].IsNewRow)
            {
                return;
            }

            string currentValue = GetCellText(rowIndex, columnIndex);
            string currentShapeNo = GetShapeNumberText(rowIndex);
            string currentDimensionText = GetShapeDimensionText(rowIndex);
            string pickerSearchValue = currentShapeNo != "" ? currentShapeNo : currentValue;
            string currentCadShapePath = ResolveCadShapeJsonPath(GetCadShapeJsonText(rowIndex));
            FrmShapePicker picker = new FrmShapePicker(GetShapeRepository(), pickerSearchValue, currentDimensionText, currentCadShapePath);

            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedShape == null)
            {
                return;
            }

            PushUndoState(CaptureGridState());

            if (picker.SelectedCadShapeOriginal)
            {
                // CAD에서 가져온 형상은 그대로 유지하고, 형상 안의 치수값만 수정합니다.
                // 형상번호는 OVIA 마스터 형상코드가 아니므로 비워둡니다.
                grid.Rows[rowIndex].Cells[columnIndex].Value = "";
                SetShapeMetaCellIfExists(rowIndex, new string[] { "형상번호", "OVIA_형상번호", "OVIA 형상번호", "OVIA형상번호" }, "");
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_형상치수", "OVIA 형상치수", "OVIA형상치수" }, picker.SelectedDimensionText);
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" }, "CAD");
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" }, "CAD_EDITED");
                SetShapeDimensionColumnsIfExists(rowIndex, picker.SelectedDimensionText);
                lblStatus.Text = "CAD에서 불러온 철근형상의 치수값을 수정했습니다.";
            }
            else
            {
                // 사용자가 OVIA 형상코드 또는 이미지 없음을 선택해도 기존 CAD 원본 형상 경로는 보존합니다.
                // 그래야 다시 수정할 때 목록 맨 위에서 CAD 원본 형상으로 되돌릴 수 있습니다.
                // 화면 렌더링은 OVIA_SHAPE_SOURCE=MANUAL 기준으로 수동 형상을 우선 표시합니다.
                if (picker.SelectedShape.ShapeNo <= 0)
                {
                    grid.Rows[rowIndex].Cells[columnIndex].Value = "";
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "형상번호", "OVIA_형상번호", "OVIA 형상번호", "OVIA형상번호" }, "");
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_형상치수", "OVIA 형상치수", "OVIA형상치수" }, "");
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" }, "MANUAL");
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" }, "MANUAL_NO_IMAGE");
                    lblStatus.Text = "철근 형상을 이미지 없음으로 변경했습니다.";
                }
                else
                {
                    grid.Rows[rowIndex].Cells[columnIndex].Value = picker.SelectedShape.DisplayCode;
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "형상번호", "OVIA_형상번호", "OVIA 형상번호", "OVIA형상번호" }, picker.SelectedShape.DisplayCode);
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_형상치수", "OVIA 형상치수", "OVIA형상치수" }, picker.SelectedDimensionText);
                    SetShapeDimensionColumnsIfExists(rowIndex, picker.SelectedDimensionText);
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" }, "MANUAL");
                    SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" }, "MANUAL_SELECTED");
                    lblStatus.Text = "철근 형상 " + picker.SelectedShape.DisplayName + "을(를) 선택했습니다.";
                }
            }

            RefreshModifiedCellVisual(rowIndex, columnIndex);
            MarkUnsaved();
            RecalculateSummary();
            lblStatus.ForeColor = TextSub;
            grid.InvalidateRow(rowIndex);
        }

        private string GetShapeNumberText(int rowIndex)
        {
            return GetFirstExistingCellText(rowIndex, new string[] { "형상번호", "OVIA_형상번호", "OVIA 형상번호", "OVIA형상번호", "ShapeCode", "ShapeNo", "ShapeCodeRaw" });
        }

        private void ClearCadShapeMetaCells(int rowIndex)
        {
            SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_CAD_SHAPE_JSON", "CAD_SHAPE_JSON", "OVIA CAD SHAPE JSON" }, "");
            SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_CAD_SHAPE_TEXTS", "CAD_SHAPE_TEXTS", "OVIA CAD SHAPE TEXTS" }, "");
            SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" }, "MANUAL");
            SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" }, "MANUAL_SELECTED");
        }

        private string GetShapeDimensionText(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return "";
            }

            string value = GetFirstExistingCellText(rowIndex, new string[] { "OVIA_형상치수", "OVIA 형상치수", "OVIA형상치수", "형상치수" });

            if (value != "")
            {
                return value;
            }

            return BuildDimensionTextFromIndividualColumns(rowIndex);
        }

        private string GetFirstExistingCellText(int rowIndex, string[] headerNames)
        {
            if (grid == null || headerNames == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return "";
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();
                int j;

                for (j = 0; j < headerNames.Length; j++)
                {
                    string target = headerNames[j] == null ? "" : headerNames[j].Trim();

                    if (target == "")
                    {
                        continue;
                    }

                    if (header.Equals(target, StringComparison.OrdinalIgnoreCase) || name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        object cellValue = grid.Rows[rowIndex].Cells[i].Value;
                        return cellValue == null ? "" : cellValue.ToString().Trim();
                    }
                }
            }

            return "";
        }

        private string BuildDimensionTextFromIndividualColumns(int rowIndex)
        {
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < keys.Length; i++)
            {
                string value = GetFirstExistingCellText(rowIndex, GetDimensionHeaderCandidates(keys[i]));

                if (value == "" || value == "0")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                sb.Append(keys[i]);
                sb.Append("=");
                sb.Append(value);
            }

            return sb.ToString();
        }

        private void SetShapeDimensionColumnsIfExists(int rowIndex, string dimensionText)
        {
            Dictionary<string, string> values = ParseShapeDimensionText(dimensionText);
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < keys.Length; i++)
            {
                string value;

                if (values.TryGetValue(keys[i], out value))
                {
                    SetShapeMetaCellIfExists(rowIndex, GetDimensionHeaderCandidates(keys[i]), value);
                }
            }
        }

        private string[] GetDimensionHeaderCandidates(string key)
        {
            return new string[]
            {
                key,
                key + "값",
                key + " 값",
                key + "_값",
                "OVIA_" + key,
                "OVIA " + key,
                "OVIA_" + key + "값",
                "OVIA " + key + "값"
            };
        }

        private Dictionary<string, string> ParseShapeDimensionText(string dimensionText)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (dimensionText == null)
            {
                return result;
            }

            string text = dimensionText.Trim();

            if (text == "")
            {
                return result;
            }

            text = text.Replace("\r", ";").Replace("\n", ";").Replace(",", ";");
            string[] parts = text.Split(';');
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim();

                if (part == "")
                {
                    continue;
                }

                int pos = part.IndexOf('=');

                if (pos < 0)
                {
                    pos = part.IndexOf(':');
                }

                if (pos <= 0)
                {
                    continue;
                }

                string key = NormalizeShapeDimensionKey(part.Substring(0, pos));
                string value = part.Substring(pos + 1).Trim();

                if (key == "" || value == "")
                {
                    continue;
                }

                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
                else
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private string NormalizeShapeDimensionKey(string key)
        {
            if (key == null)
            {
                return "";
            }

            key = key.Trim().ToUpperInvariant();
            key = key.Replace(" ", "");
            key = key.Replace("값", "");

            if (key == "R")
            {
                return "R1";
            }

            return key;
        }

        private void SetShapeMetaCellIfExists(int rowIndex, string[] headerNames, string value)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || headerNames == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();
                int j;

                for (j = 0; j < headerNames.Length; j++)
                {
                    string target = headerNames[j] == null ? "" : headerNames[j].Trim();

                    if (target == "")
                    {
                        continue;
                    }

                    if (header.Equals(target, StringComparison.OrdinalIgnoreCase) || name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        grid.Rows[rowIndex].Cells[i].Value = value == null ? "" : value;
                        RefreshModifiedCellVisual(rowIndex, i);
                        grid.InvalidateCell(i, rowIndex);
                        return;
                    }
                }
            }
        }


        private void ApplyUnitConversionAfterMapping(OviaBarListMappedTable mappedTable)
        {
            if (grid == null || mappedTable == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count && i < mappedTable.Columns.Count; i++)
            {
                OviaBarListMappedColumn mapped = mappedTable.Columns[i];

                if (mapped == null || !String.Equals(mapped.StandardKey, "weight_ton", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HeaderLooksKg(mapped.SourceHeader))
                {
                    continue;
                }

                ConvertColumnKgToTon(i);
            }
        }

        private bool HeaderLooksKg(string header)
        {
            if (header == null)
            {
                return false;
            }

            string value = header.Trim().ToUpperInvariant();
            value = value.Replace(" ", "");

            return value.IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("KGS", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("킬로", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ConvertColumnKgToTon(int columnIndex)
        {
            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                string text = GetCellText(r, columnIndex);
                double value;

                if (!TryParseNumber(text, out value))
                {
                    continue;
                }

                double ton = value / 1000.0;
                grid.Rows[r].Cells[columnIndex].Value = ton.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        private bool TryParseNumber(string text, out double value)
        {
            value = 0;

            if (text == null)
            {
                return false;
            }

            text = text.Trim().Replace(",", "").Replace(" ", "");

            if (text == "")
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return false;
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
            // BarList 항목 매핑은 관리자 화면에서 실행 중에도 변경될 수 있습니다.
            // CSV를 새로 불러올 때마다 최신 매핑 파일을 다시 읽어 기존 화면 구조를 유지하면서 변경사항을 반영합니다.
            mappingStore = OviaBarListMappingStore.LoadDefault();
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

        private void EnsurePartColumnExists()
        {
            if (grid == null)
            {
                return;
            }

            if (FindPartColumnIndex() >= 0)
            {
                return;
            }

            int insertIndex = FindNumberColumnIndex();

            if (insertIndex >= 0)
            {
                insertIndex = insertIndex + 1;
            }
            else
            {
                insertIndex = 0;
            }

            if (insertIndex > grid.Columns.Count)
            {
                insertIndex = grid.Columns.Count;
            }

            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = "OVIA_PartVisible";
            column.HeaderText = "부위";
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.ReadOnly = false;
            column.MinimumWidth = 55;
            column.Width = 68;
            column.FillWeight = 68;
            grid.Columns.Insert(insertIndex, column);
        }

        private int FindNumberColumnIndex()
        {
            if (grid == null)
            {
                return -1;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();

                if (header.Equals("번호", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("번호", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindPartColumnIndex()
        {
            if (grid == null)
            {
                return -1;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();

                if (header.Equals("부위", StringComparison.OrdinalIgnoreCase)
                    || header.Equals("위치", StringComparison.OrdinalIgnoreCase)
                    || header.Equals("구간", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("OVIA_PartVisible", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureShapeNumberColumnExists()
        {
            if (grid == null)
            {
                return;
            }

            int shapeColumnIndex = FindFirstRebarShapeColumnIndex();

            if (shapeColumnIndex < 0)
            {
                return;
            }

            int existingIndex = FindShapeNumberColumnIndex();

            if (existingIndex < 0)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = "OVIA_ShapeNoVisible";
                column.HeaderText = "형상번호";
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.ReadOnly = false;
                column.MinimumWidth = 55;
                column.Width = 62;
                column.FillWeight = 62;
                grid.Columns.Insert(shapeColumnIndex, column);
                existingIndex = column.Index;

                if (shapeColumnIndex >= existingIndex)
                {
                    shapeColumnIndex++;
                }
            }

            PopulateShapeNumberColumn(existingIndex, shapeColumnIndex);
        }

        private int FindFirstRebarShapeColumnIndex()
        {
            if (grid == null)
            {
                return -1;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (IsRebarShapeHeader(grid.Columns[i].HeaderText))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindShapeNumberColumnIndex()
        {
            if (grid == null)
            {
                return -1;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();

                if (header.Equals("형상번호", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("OVIA_ShapeNoVisible", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void PopulateShapeNumberColumn(int shapeNumberColumnIndex, int shapeColumnIndex)
        {
            if (grid == null || shapeNumberColumnIndex < 0 || shapeNumberColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                string current = GetCellText(r, shapeNumberColumnIndex);

                if (current != "")
                {
                    continue;
                }

                string rawShape = "";

                if (shapeColumnIndex >= 0 && shapeColumnIndex < grid.Columns.Count)
                {
                    rawShape = GetCellText(r, shapeColumnIndex);
                }

                RebarShapeInfo shape = GetShapeRepository().FindByRawValue(rawShape);

                if (shape != null)
                {
                    grid.Rows[r].Cells[shapeNumberColumnIndex].Value = shape.DisplayCode;
                }
            }
        }


        private string NormalizeInternalColumnToken(string value)
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

        private bool IsInternalOviaColumn(string header)
        {
            if (header == null)
            {
                return false;
            }

            string name = header.Trim();
            string normalized = NormalizeInternalColumnToken(name);

            if (normalized == "NO" || normalized == "ROWTYPE" || normalized == "SOURCEROWNO")
            {
                return true;
            }

            if (name.StartsWith("OVIA_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.StartsWith("OVIA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.IndexOf("CADSHAPE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (ContainsAny(name, "ShapeVectorFile", "ShapeSvgPath", "ShapeSource", "ShapeReviewStatus", "ShapeStatus", "ShapeJson"))
            {
                return true;
            }

            return false;
        }

        private void ApplyGridColumnStyle()
        {
            int i;

            // CAD 원본에는 부위/형상번호가 없을 수 있지만 사용자가 일괄 변경/수동 형상 선택을 할 수 있어야 하므로
            // 사용자 표시용 부위 컬럼과 형상번호 컬럼을 번호 뒤쪽에 유지합니다.
            EnsurePartColumnExists();
            EnsureShapeNumberColumnExists();

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars = ScrollBars.Both;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].HeaderText;
                int baseWidth = 95;
                bool fillColumn = false;

                if (grid.Columns[i].Name != null && grid.Columns[i].Name.Equals("OVIA_PartVisible", StringComparison.OrdinalIgnoreCase))
                {
                    baseWidth = 68;
                }
                else if (grid.Columns[i].Name != null && grid.Columns[i].Name.Equals("OVIA_ShapeNoVisible", StringComparison.OrdinalIgnoreCase))
                {
                    baseWidth = 62;
                }
                else if (IsInternalOviaColumn(name))
                {
                    grid.Columns[i].Visible = false;
                    continue;
                }
                else if (IsRebarShapeHeader(name))
                {
                    baseWidth = 145;
                }
                else if (name != null && name.Trim().Equals("형상번호", StringComparison.OrdinalIgnoreCase))
                {
                    baseWidth = 62;
                }
                else if (ContainsAny(name, "부위", "위치", "구간"))
                {
                    baseWidth = 68;
                }
                else if (name != null && name.Trim().Equals("번호", StringComparison.OrdinalIgnoreCase))
                {
                    baseWidth = 48;
                }
                else if (ContainsAny(name, "규격", "철근규격"))
                {
                    baseWidth = 90;
                }
                else if (ContainsAny(name, "형상"))
                {
                    baseWidth = 145;
                }
                else if (ContainsAny(name, "길이"))
                {
                    baseWidth = 105;
                }
                else if (ContainsAny(name, "수량"))
                {
                    baseWidth = 88;
                }
                else if (ContainsAny(name, "총길이"))
                {
                    baseWidth = 110;
                }
                else if (ContainsAny(name, "중량"))
                {
                    baseWidth = 112;
                }
                else if (ContainsAny(name, "비고"))
                {
                    baseWidth = 150;
                    fillColumn = true;
                }

                int scaledWidth = ScaleGridSize(baseWidth);

                grid.Columns[i].Visible = true;
                grid.Columns[i].AutoSizeMode = fillColumn ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None;
                grid.Columns[i].FillWeight = scaledWidth;
                grid.Columns[i].MinimumWidth = Math.Min(scaledWidth, ScaleGridSize(45));
                grid.Columns[i].Width = scaledWidth;
                grid.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[i].DefaultCellStyle.Alignment = GetBarListCellAlignment(name);
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    grid.Rows[r].Height = ScaleGridSize(GridBaseRowHeight);
                }
            }
        }

        private DataGridViewContentAlignment GetBarListCellAlignment(string header)
        {
            if (header == null)
            {
                return DataGridViewContentAlignment.MiddleLeft;
            }

            string name = header.Trim();

            if (IsRebarShapeHeader(name) || ContainsAny(name, "번호", "부위", "위치", "구간", "규격"))
            {
                return DataGridViewContentAlignment.MiddleCenter;
            }

            if (ContainsAny(name, "길이", "수량", "중량"))
            {
                return DataGridViewContentAlignment.MiddleRight;
            }

            return DataGridViewContentAlignment.MiddleLeft;
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
                lblSaveState.ForeColor = OviaFluentTheme.Success;
            }
            else
            {
                lblSaveState.Text = "저장 상태: 확인 필요";
                lblSaveState.ForeColor = OviaFluentTheme.Danger;
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
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
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
            lblGuide.ForeColor = OviaFluentTheme.TextPrimary;
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
        public Color SurfaceColor = OviaFluentTheme.AppBackground;

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

                using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
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

            NormalizeStandardColumnOrder(store);

            return store;
        }

        private static void NormalizeStandardColumnOrder(OviaBarListMappingStore store)
        {
            if (store == null || store.StandardColumns == null)
            {
                return;
            }

            string[] order = new string[]
            {
                "no",
                "part",
                "dia",
                "shape_no",
                "shape",
                "length_mm",
                "qty_ea",
                "total_length_m",
                "weight_ton",
                "remark"
            };

            Dictionary<string, OviaBarListMappingColumn> current = new Dictionary<string, OviaBarListMappingColumn>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < store.StandardColumns.Count; i++)
            {
                OviaBarListMappingColumn col = store.StandardColumns[i];

                if (col == null || col.Key == null || col.Key.Trim() == "")
                {
                    continue;
                }

                if (!current.ContainsKey(col.Key))
                {
                    current.Add(col.Key, col);
                }
            }

            OviaBarListMappingStore builtIn = CreateBuiltInDefault();
            Dictionary<string, OviaBarListMappingColumn> builtInMap = new Dictionary<string, OviaBarListMappingColumn>(StringComparer.OrdinalIgnoreCase);

            for (i = 0; i < builtIn.StandardColumns.Count; i++)
            {
                OviaBarListMappingColumn col = builtIn.StandardColumns[i];

                if (col != null && col.Key != null && col.Key.Trim() != "" && !builtInMap.ContainsKey(col.Key))
                {
                    builtInMap.Add(col.Key, col);
                }
            }

            List<OviaBarListMappingColumn> ordered = new List<OviaBarListMappingColumn>();
            Dictionary<string, bool> used = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            for (i = 0; i < order.Length; i++)
            {
                string key = order[i];

                if (current.ContainsKey(key))
                {
                    ordered.Add(current[key]);
                    used[key] = true;
                }
                else if (builtInMap.ContainsKey(key))
                {
                    ordered.Add(builtInMap[key]);
                    used[key] = true;
                }
            }

            for (i = 0; i < store.StandardColumns.Count; i++)
            {
                OviaBarListMappingColumn col = store.StandardColumns[i];

                if (col == null || col.Key == null || col.Key.Trim() == "")
                {
                    continue;
                }

                if (!used.ContainsKey(col.Key))
                {
                    ordered.Add(col);
                    used[col.Key] = true;
                }
            }

            store.StandardColumns = ordered;
        }

        private static string FindMappingFilePath()
        {
            List<string> candidates = new List<string>();
            string startup = Application.StartupPath;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 시스템관리자가 저장한 사용자 설정을 배포 기본값보다 먼저 적용합니다.
            // 기존에는 실행 폴더의 기본 JSON이 먼저 잡혀 관리자 저장값이 무시될 수 있었습니다.
            if (appData != null && appData.Trim() != "")
            {
                candidates.Add(Path.Combine(appData, "OVIA", "Mapping", "barlist_mapping.json"));
            }

            if (startup != null && startup.Trim() != "")
            {
                candidates.Add(Path.Combine(startup, "Data", "Mapping", "barlist_mapping.json"));
                candidates.Add(Path.GetFullPath(Path.Combine(startup, "..", "..", "Data", "Mapping", "barlist_mapping.json")));
                candidates.Add(Path.GetFullPath(Path.Combine(startup, "..", "..", "..", "Data", "Mapping", "barlist_mapping.json")));
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

        public static string GetWritableMappingFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (appData == null || appData.Trim() == "")
            {
                appData = Application.StartupPath;
            }

            return Path.Combine(appData, "OVIA", "Mapping", "barlist_mapping.json");
        }

        public void SaveToDefaultPath()
        {
            SaveToFile(GetWritableMappingFilePath());
        }

        public void SaveToFile(string path)
        {
            if (path == null || path.Trim() == "")
            {
                throw new ArgumentException("저장 경로가 비어 있습니다.");
            }

            string dir = Path.GetDirectoryName(path);

            if (dir != null && dir.Trim() != "" && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson(), Encoding.UTF8);
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            int i;

            sb.Append("{\r\n");
            sb.Append("  \"version\": \"" + EscapeJson(Version) + "\",\r\n");
            sb.Append("  \"updatedAt\": \"" + EscapeJson(UpdatedAt) + "\",\r\n");
            sb.Append("  \"standardColumns\": [\r\n");

            for (i = 0; i < StandardColumns.Count; i++)
            {
                OviaBarListMappingColumn col = StandardColumns[i];
                int j;

                sb.Append("    {\r\n");
                sb.Append("      \"key\": \"" + EscapeJson(col.Key) + "\",\r\n");
                sb.Append("      \"displayName\": \"" + EscapeJson(col.DisplayName) + "\",\r\n");
                sb.Append("      \"dataType\": \"" + EscapeJson(col.DataType) + "\",\r\n");
                sb.Append("      \"priority\": " + col.Priority.ToString() + ",\r\n");
                sb.Append("      \"aliases\": [");

                for (j = 0; j < col.Aliases.Count; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append("\"" + EscapeJson(col.Aliases[j]) + "\"");
                }

                sb.Append("]\r\n");
                sb.Append("    }");

                if (i < StandardColumns.Count - 1)
                {
                    sb.Append(",");
                }

                sb.Append("\r\n");
            }

            sb.Append("  ]\r\n");
            sb.Append("}\r\n");

            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
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
                OviaBarListMappingColumn standardColumn = StandardColumns[i];
                string key = standardColumn.Key;

                if (matchedByKey.ContainsKey(key))
                {
                    table.Columns.Add(matchedByKey[key]);
                }
                else
                {
                    // OVIA 기본 헤더는 CAD 원본에 해당 컬럼이 없어도 항상 유지합니다.
                    // 예: 총길이(M)가 없는 도면은 총길이(M) 컬럼을 빈 값으로 표시/저장합니다.
                    OviaBarListMappedColumn emptyCol = new OviaBarListMappedColumn();
                    emptyCol.SourceIndex = -1;
                    emptyCol.SourceHeader = "";
                    emptyCol.StandardKey = standardColumn.Key;
                    emptyCol.DisplayName = standardColumn.DisplayName;
                    emptyCol.IsMapped = false;
                    table.Columns.Add(emptyCol);
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

            if (value == "NO" || value == "ROWTYPE" || value == "SOURCEROWNO")
            {
                return true;
            }

            if (value.StartsWith("OVIA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.IndexOf("CADSHAPE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
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

        public static OviaBarListMappingStore CreateBuiltInDefault()
        {
            OviaBarListMappingStore store = new OviaBarListMappingStore();
            store.Version = "built-in-2026.05.27.009";
            store.UpdatedAt = "2026-05-27";

            // OVIA BarList 고정 헤더 순서입니다.
            // 이 순서는 CAD 도면마다 헤더명이 달라도 화면/저장 기준으로 유지합니다.
            store.AddColumn("no", "번호", "number_or_text", 100, "NO", "NO.", "No", "No.", "순번", "번호", "번", "부호", "부호번호", "기호", "ITEM");
            store.AddColumn("part", "부위", "text", 100, "부위", "위치", "층", "구간", "시공부위", "ZONE", "AREA", "LOCATION");
            store.AddColumn("dia", "철근규격", "rebar_diameter", 100, "규격", "철근규격", "DIA", "D", "직경", "BAR DIA", "SIZE", "강종");
            store.AddColumn("shape_no", "형상번호", "text", 100, "형상번호", "형번", "형상코드", "SHAPE NO", "SHAPE CODE", "BAR MARK", "MARK");
            store.AddColumn("shape", "철근형상", "text_or_image", 100, "형상", "형태", "철근형상", "SHAPE", "BENT", "BAR SHAPE", "절곡형상");
            store.AddColumn("length_mm", "길이(mm)", "number", 100, "길이", "L", "LENGTH", "절단길이", "산출길이", "MM", "길이MM", "길이(MM)");
            store.AddColumn("qty_ea", "수량(EA)", "number", 100, "수량", "개수", "갯수", "본수", "EA", "QTY", "QUANTITY", "수량EA", "수량(EA)");
            store.AddColumn("total_length_m", "총길이(M)", "number", 90, "총길이", "총연장", "연장", "TOTAL LENGTH", "T.L", "M", "총길이M", "총길이(M)");
            store.AddColumn("weight_ton", "중량(Ton)", "number_ton", 90, "중량", "총중량", "톤", "TON", "Ton", "ton", "WEIGHT", "WT", "TOTAL WEIGHT", "중량TON", "중량(TON)", "총중량TON", "총중량(TON)", "KG", "kg", "중량KG", "중량(KG)");
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
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.Accent;

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

            Color fillColor = hover ? OviaFluentTheme.AccentHover : StartColor;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, 7))
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                OviaFluentTheme.FontButton(9F, FontStyle.Bold),
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
