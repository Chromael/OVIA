using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmBarList : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;
        private const int HeaderDragNone = 0;
        private const int HeaderDragRow = 1;
        private const int HeaderDragColumn = 2;

        private enum BarListImportMode
        {
            Cancel,
            Replace,
            Append
        }

        private enum BarListSummaryMode
        {
            Spec,
            Part,
            Drawing
        }

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
        private ToolStripMenuItem rowCopyMenuItem;
        private ToolStripMenuItem rowPasteMenuItem;
        private BarListCellClipboardData cellClipboardData = null;
        private List<object[]> rowClipboardRows = new List<object[]>();
        private string rowClipboardSchemaKey = "";
        private List<GridUndoSnapshot> undoStates = new List<GridUndoSnapshot>();
        private List<GridUndoSnapshot> redoStates = new List<GridUndoSnapshot>();
        private GridUndoSnapshot cellEditBeforeSnapshot = null;
        private GridUndoSnapshot savedGridBaseline = null;
        private Dictionary<DataGridViewRow, long> logicalRowOrderKeys = new Dictionary<DataGridViewRow, long>();
        private long nextLogicalRowOrderKey = 1L;
        private bool isRestoringGridState = false;
        private int gridRedrawLockCount = 0;
        private bool isBulkGridSelecting = false;
        private int selectedCellCountCache = 0;
        private int headerDragMode = HeaderDragNone;
        private int headerDragStartIndex = -1;
        private int headerDragLastIndex = -1;
        private int headerSelectionVersion = 0;
        private bool columnHeaderDragOccurred = false;
        private string gridSortColumnName = "";
        private bool gridSortAscending = true;
        private const int MaxUndoCount = 30;
        private bool allowExtractEditMenu = false;
        private const string ReferenceFilePrefix = "참고용 : ";
        private Panel actionPanel;
        private TextBox txtFilePath;
        private Label lblRowCount;
        private Label lblTotalQty;
        private Label lblTotalLength;
        private Label lblTotalWeight;
        private Label lblStatus;
        private OviaProjectContextHeader projectContextHeader;
        private OviaBarListButton saveProjectButton;
        private OviaBarListButton cadSelectionButton;
        private OviaBarListButton cadSelectionModeOffButton;
        private OviaBarListButton deleteCadBoxButton;
        private OviaBarListButton excelExportButton;
        private OviaBarListButton summaryButton;
        private OviaBarListButton otherBarListButton;
        private OviaBarListButton filterChipButton;
        private Panel actionSeparator1;
        private Panel actionSeparator2;
        private Panel summaryDrawer;
        private DataGridView summaryGrid;
        private Button summarySpecTabButton;
        private Button summaryPartTabButton;
        private Button summaryDrawingTabButton;
        private OviaBarListPinButton summaryPinButton;
        private Button summaryCloseButton;
        private Label summaryDrawerTitle;
        private Label summaryDrawerHint;
        private Panel selectionSummaryPanel;
        private Label selectionSummaryLabel;
        private OviaBarListButton selectionCopyButton;
        private bool summaryDrawerVisible = false;
        private bool summaryDrawerPinned = false;
        private bool isApplyingSummaryFilter = false;
        private BarListSummaryMode summaryMode = BarListSummaryMode.Spec;
        private BarListSummaryMode activeSummaryFilterMode = BarListSummaryMode.Spec;
        private string activeSummaryFilterValue = "";
        private bool hasActiveSummaryFilter = false;
        private const int SummaryDrawerWidth = 430;
        private const int SummaryDrawerGap = 10;
        private ToolTip windowToolTip;
        private OviaBarListMappingStore mappingStore;
        private RebarShapeRepository shapeRepository;
        private RebarShapeRenderer shapeRenderer = new RebarShapeRenderer();
        private CadShapeRenderer cadShapeRenderer = new CadShapeRenderer();
        private int lastMappingMatchCount = 0;
        private int lastMappingTotalHeaderCount = 0;
        private string lastMappingVersion = "";
        private Dictionary<string, RebarCalculationMismatchInfo> rebarCalculationMismatches = new Dictionary<string, RebarCalculationMismatchInfo>();
        private bool isApplyingRebarCalculation = false;
        private bool rebarMismatchWarningShown = false;

        private const int GridZoomMinPercent = 100;
        private const int GridZoomMaxPercent = 220;
        private const int GridZoomStepPercent = 10;
        private const int GridBaseHeaderHeight = 34;
        private const int GridBaseRowHeight = 48;
        private const int GridBaseRowHeaderWidth = 48;
        private const float GridBaseCellFontSize = 8.7F;
        private const float GridNumericFontPixelIncreaseInPoints = 0.75F;
        private int gridZoomPercent = GridZoomMinPercent;

        private FileSystemWatcher autoCadWatcher;
        private DateTime autoImportStartTime;
        private string lastLoadedFilePath = "";
        private HashSet<string> autoCadProcessedCsvFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> autoCadImportedCsvFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool isProcessingAutoCadCsvQueue = false;
        private bool waitingAutoCadImport = false;
        private bool autoCadContinuousAppendMode = false;
        private bool autoCadSelectionModeActive = false;
        private BarListImportMode autoCadInitialImportMode = BarListImportMode.Replace;
        private System.Windows.Forms.Timer autoCadImportPollTimer;
        private System.Windows.Forms.Timer autoCadAvailabilityTimer;
        private DateTime autoCadSelectionCommandIssuedAt = DateTime.MinValue;
        private DateTime autoCadSelectionCommandEndedAt = DateTime.MinValue;
        private bool autoCadSelectionCommandObserved = false;
        private bool autoCadSelectionCommandDispatchReturned = false;
        private int autoCadLoadedCsvCount = 0;
        private bool isDeletingAutoCadSelectionBoxes = false;
        private const int AutoCadDeleteRetryIntervalMs = 250;
        private const int AutoCadDeleteTimeoutMs = 6000;
        private const int RpcECallRejected = unchecked((int)0x80010001);
        private const int RpcEServerCallRetryLater = unchecked((int)0x8001010A);
        private const string AutoCadBusyErrorPrefix = "AUTOCAD_BUSY:";
        private bool isSaved = true;
        private bool isClosingByButton = false;
        private bool suppressUnsavedClosePrompt = false;
        private bool isInternalNavigation = false;
        private bool isBackNavigationQueued = false;
        private readonly string initialFilePath;
        private readonly BarListEditResult registrationDraft;
        private string savedProjectFilePath = "";

        private readonly Color BrandIndigo = OviaFluentTheme.AccentHover;
        private readonly Color BrandViolet = OviaFluentTheme.Accent;
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
            this.registrationDraft = this.initialFilePath.Trim() == ""
                ? OviaBarListRegistrationDraftStore.Get(this.companyId, this.projectNo)
                : null;

            shapeRepository = RebarShapeRepository.CreateDefault();

            BuildUI();
            StartAutoCadAvailabilityTimer();

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
            // 공통 Idle 감시 없이도 기존 업무화면 최소 크기 계약(1100x750)을 직접 유지합니다.
            this.MinimumSize = new Size(1100, 750);
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
            BuildActionBar(contentPanel);
            BuildProjectInfo(contentPanel);
            BuildReferenceBar(contentPanel);
            BuildSummary(contentPanel);
            BuildGrid(contentPanel);
            BuildSummaryDrawer(contentPanel);
            BuildSelectionSummaryOverlay(contentPanel);
            contentPanel.Resize += ContentPanel_Resize;
            UpdateScrollableContentSize();
            LayoutBarListFloatingPanels();
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
                delegate { RefreshCurrentBarListFromSavedSource(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    NavigateByWorkspacePath(target);
                }
            );
        }

        private void RefreshCurrentBarListFromSavedSource()
        {
            string sourcePath = "";
            bool loadAsSaved = false;

            if (savedProjectFilePath.Trim() != "" && File.Exists(savedProjectFilePath))
            {
                sourcePath = savedProjectFilePath;
                loadAsSaved = true;
            }
            else if (initialFilePath.Trim() != "" && File.Exists(initialFilePath))
            {
                sourcePath = initialFilePath;
                loadAsSaved = true;
            }
            else if (lastLoadedFilePath.Trim() != "" && File.Exists(lastLoadedFilePath))
            {
                sourcePath = lastLoadedFilePath;
            }

            if (sourcePath == "")
            {
                RefreshSaveStateFromCurrentGrid();
                return;
            }

            LoadCsv(sourcePath, loadAsSaved);
        }

        private void NavigateByWorkspacePath(string target)
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

            if (target == "PROJECT_MANAGER")
            {
                if (workspace != null)
                {
                    workspace.NavigateToProjectManager();
                    return;
                }

                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                FrmProjectManager form = new FrmProjectManager(companyId, userId);
                ShowReplacementWindow(form);
                return;
            }

            if (target == "MAIN")
            {
                if (workspace != null)
                {
                    workspace.NavigateToMain();
                    return;
                }

                NavigateToMain();
                return;
            }

            if (target == "PROJECT_BARLIST_LIST")
            {
                if (workspace != null)
                {
                    workspace.NavigateToProjectBarListList(projectNo, projectName, clientName, projectStatus);
                    return;
                }

                if (!ConfirmDiscardUnsavedForNavigation())
                {
                    return;
                }

                suppressUnsavedClosePrompt = true;
                FrmProjectBarListList form = new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus);
                ShowReplacementWindow(form);
            }
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
            button.Font = OviaIconFont.Create(9.5F, FontStyle.Regular);
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
            NavigateByWorkspacePath(target);
        }

        private void BuildActionBar(Control parent)
        {
            actionPanel = new Panel();
            actionPanel.Location = new Point(34, 110);
            actionPanel.Size = new Size(1168, 38);
            actionPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionPanel.BackColor = SurfaceColor;
            actionPanel.Resize += delegate { LayoutActionButtons(); };
            parent.Controls.Add(actionPanel);

            cadSelectionButton = new OviaBarListButton();
            cadSelectionButton.Text = "CAD에서 영역선택";
            cadSelectionButton.Size = new Size(125, 34);
            cadSelectionButton.StartColor = OviaFluentTheme.Accent;
            cadSelectionButton.EndColor = OviaFluentTheme.Accent;
            cadSelectionButton.UseCustomColors = true;
            cadSelectionButton.Click += AutoCadImport_Click;
            actionPanel.Controls.Add(cadSelectionButton);

            cadSelectionModeOffButton = new OviaBarListButton();
            cadSelectionModeOffButton.Text = "CAD 선택모드 해제";
            cadSelectionModeOffButton.Size = new Size(118, 34);
            cadSelectionModeOffButton.Click += ReleaseAutoCadSelectionMode_Click;
            cadSelectionModeOffButton.Enabled = false;
            cadSelectionModeOffButton.Visible = false;
            actionPanel.Controls.Add(cadSelectionModeOffButton);

            deleteCadBoxButton = new OviaBarListButton();
            deleteCadBoxButton.Text = "CAD에 선택된 영역 삭제";
            deleteCadBoxButton.Size = new Size(150, 34);
            deleteCadBoxButton.StartColor = OviaFluentTheme.Danger;
            deleteCadBoxButton.EndColor = OviaFluentTheme.Danger;
            deleteCadBoxButton.Click += DeleteAutoCadSelectionBoxes_Click;
            deleteCadBoxButton.Visible = false;
            actionPanel.Controls.Add(deleteCadBoxButton);

            actionSeparator1 = CreateActionSeparator(actionPanel);

            saveProjectButton = new OviaBarListButton();
            saveProjectButton.Text = "검토 후 저장";
            saveProjectButton.Size = new Size(92, 34);
            saveProjectButton.Click += SaveProjectBarList_Click;
            actionPanel.Controls.Add(saveProjectButton);

            actionSeparator2 = CreateActionSeparator(actionPanel);

            excelExportButton = new OviaBarListButton();
            excelExportButton.Text = "Excel 다운";
            excelExportButton.Size = new Size(86, 34);
            excelExportButton.UseCustomTextColor = true;
            excelExportButton.CustomTextColor = Color.FromArgb(33, 115, 70);
            excelExportButton.Click += ExcelExport_Click;
            windowToolTip.SetToolTip(excelExportButton, "현재 표시 중인 BarList와 철근형상을 Excel 파일로 저장합니다.");
            actionPanel.Controls.Add(excelExportButton);

            summaryButton = new OviaBarListButton();
            summaryButton.Text = "요약 \uE70D";
            summaryButton.Size = new Size(72, 34);
            summaryButton.Click += SummaryButton_Click;
            windowToolTip.SetToolTip(summaryButton, "규격별, 부위별, 원본도면별 요약을 엽니다.");
            actionPanel.Controls.Add(summaryButton);

            filterChipButton = new OviaBarListButton();
            filterChipButton.Text = "필터 해제";
            filterChipButton.Size = new Size(92, 34);
            filterChipButton.Visible = false;
            filterChipButton.Click += ClearSummaryFilter_Click;
            windowToolTip.SetToolTip(filterChipButton, "현재 요약 필터를 해제합니다.");
            actionPanel.Controls.Add(filterChipButton);

            otherBarListButton = new OviaBarListButton();
            otherBarListButton.Text = "다른 BarList";
            otherBarListButton.Size = new Size(104, 34);
            otherBarListButton.Click += OtherBarList_Click;
            windowToolTip.SetToolTip(otherBarListButton, "다른 공사 또는 다른 BarList의 행을 조회하여 현재 목록 뒤에 추가합니다.");
            actionPanel.Controls.Add(otherBarListButton);

            LayoutActionButtons();
            UpdateSaveState();
        }

        private Panel CreateActionSeparator(Control parent)
        {
            Panel separator = new Panel();
            separator.Size = new Size(1, 20);
            separator.BackColor = OviaFluentTheme.ControlBorder;
            parent.Controls.Add(separator);
            return separator;
        }

        private void LayoutActionButtons()
        {
            if (actionPanel == null || actionPanel.IsDisposed)
            {
                return;
            }

            Control[][] groups = new Control[][]
            {
                new Control[] { cadSelectionButton, cadSelectionModeOffButton, deleteCadBoxButton },
                new Control[] { saveProjectButton },
                new Control[] { excelExportButton, summaryButton, filterChipButton, otherBarListButton }
            };
            Panel[] separators = new Panel[] { actionSeparator1, actionSeparator2 };
            int x = 0;
            int groupIndex;
            int separatorIndex = 0;
            bool hasPreviousVisibleGroup = false;

            for (groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                Control[] group = groups[groupIndex];
                bool hasVisible = false;
                int i;

                for (i = 0; i < group.Length; i++)
                {
                    Control button = group[i];

                    if (button != null && !button.IsDisposed && button.Visible)
                    {
                        hasVisible = true;
                        break;
                    }
                }

                if (!hasVisible)
                {
                    continue;
                }

                if (hasPreviousVisibleGroup && separatorIndex < separators.Length)
                {
                    Panel separator = separators[separatorIndex++];
                    separator.Visible = true;
                    separator.Location = new Point(x + 4, 9);
                    x += 14;
                }

                for (i = 0; i < group.Length; i++)
                {
                    Control button = group[i];

                    if (button == null || button.IsDisposed || !button.Visible)
                    {
                        continue;
                    }

                    button.Location = new Point(x, 2);
                    x = button.Right + 8;
                }

                hasPreviousVisibleGroup = true;
            }

            while (separatorIndex < separators.Length)
            {
                if (separators[separatorIndex] != null)
                {
                    separators[separatorIndex].Visible = false;
                }

                separatorIndex++;
            }
        }

        private void BuildProjectInfo(Control parent)
        {
            projectContextHeader = new OviaProjectContextHeader();
            projectContextHeader.Location = new Point(34, 156);
            projectContextHeader.Size = new Size(1168, 58);
            projectContextHeader.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            if (registrationDraft != null)
            {
                projectContextHeader.SetContext(
                    projectNo,
                    projectName,
                    "",
                    NormalizeProjectHeaderDate(registrationDraft.DueDate),
                    registrationDraft.Title,
                    clientName,
                    projectStatus
                );
            }
            else
            {
                projectContextHeader.SetContext(projectNo, projectName, "", "", "", clientName, projectStatus);
            }
            parent.Controls.Add(projectContextHeader);
        }

        private void BuildReferenceBar(Control parent)
        {
            txtFilePath = new TextBox();
            txtFilePath.Location = new Point(34, 228);
            txtFilePath.Size = new Size(1168, 25);
            txtFilePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtFilePath.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            OviaFluentTheme.ApplyTextBox(txtFilePath);
            txtFilePath.ReadOnly = true;
            txtFilePath.TabStop = false;
            txtFilePath.BackColor = Color.White;
            SetReferenceFilePath("");
            parent.Controls.Add(txtFilePath);
        }

        private void SetReferenceFilePath(string filePath)
        {
            if (txtFilePath == null || txtFilePath.IsDisposed)
            {
                return;
            }

            string normalizedPath = filePath == null ? "" : filePath.Trim();
            txtFilePath.Tag = normalizedPath;
            txtFilePath.Text = ReferenceFilePrefix + normalizedPath;
            txtFilePath.SelectionStart = 0;
            txtFilePath.SelectionLength = 0;
        }

        private string GetReferenceFilePath()
        {
            if (txtFilePath == null || txtFilePath.IsDisposed)
            {
                return "";
            }

            string taggedPath = txtFilePath.Tag as string;

            if (taggedPath != null)
            {
                return taggedPath.Trim();
            }

            string text = txtFilePath.Text == null ? "" : txtFilePath.Text.Trim();

            if (text.StartsWith(ReferenceFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return text.Substring(ReferenceFilePrefix.Length).Trim();
            }

            return text;
        }

        private void BuildSummary(Control parent)
        {
            int y = 267;
            AddCompactSummaryCard(parent, "행", "0", "", new Point(34, y), new Size(165, 50), out lblRowCount);
            AddCompactSummaryCard(parent, "수량", "0", "EA", new Point(209, y), new Size(210, 50), out lblTotalQty);
            AddCompactSummaryCard(parent, "총길이", "0.00", "M", new Point(429, y), new Size(240, 50), out lblTotalLength);
            AddCompactSummaryCard(parent, "중량", "0.000", "Ton", new Point(679, y), new Size(220, 50), out lblTotalWeight);

            lblStatus = new Label();
            lblStatus.Text = "CAD에서 영역을 추출하거나 저장된 BarList를 불러오세요.";
            lblStatus.AutoSize = false;
            lblStatus.AutoEllipsis = true;
            lblStatus.Size = new Size(289, 44);
            lblStatus.Font = OviaFluentTheme.FontData(8.5F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Location = new Point(913, y + 3);
            parent.Controls.Add(lblStatus);
        }

        private void AddCompactSummaryCard(Control parent, string title, string value, string unit, Point location, Size size, out Label valueLabel)
        {
            OviaBarListCard card = new OviaBarListCard();
            card.Location = location;
            card.Size = size;
            card.SurfaceColor = SurfaceColor;
            card.CompactMode = true;
            parent.Controls.Add(card);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.AutoSize = true;
            titleLabel.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
            titleLabel.ForeColor = TextSub;
            titleLabel.BackColor = Color.White;
            titleLabel.Location = new Point(14, 16);
            card.Controls.Add(titleLabel);

            int valueLeft = 58;
            int valueRight = Math.Max(valueLeft + 36, size.Width - 14);

            if (!String.IsNullOrWhiteSpace(unit))
            {
                Label unitLabel = new Label();
                unitLabel.Text = unit;
                unitLabel.AutoSize = true;
                unitLabel.Font = OviaFluentTheme.FontData(8.5F, FontStyle.Regular);
                unitLabel.ForeColor = TextSub;
                unitLabel.BackColor = Color.White;
                unitLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                Size unitSize = TextRenderer.MeasureText(unit, unitLabel.Font);
                int unitX = Math.Max(72, size.Width - unitSize.Width - 14);
                unitLabel.Location = new Point(unitX, 17);
                valueRight = Math.Max(valueLeft + 36, unitX - 8);
                card.Controls.Add(unitLabel);
            }

            valueLabel = new Label();
            valueLabel.Text = value;
            valueLabel.AutoSize = false;
            valueLabel.Font = OviaFluentTheme.FontTitle(13F, FontStyle.Bold);
            valueLabel.ForeColor = TextDark;
            valueLabel.BackColor = Color.White;
            valueLabel.Location = new Point(valueLeft, 7);
            valueLabel.Size = new Size(Math.Max(36, valueRight - valueLeft), 34);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(valueLabel);
        }

        private void BuildGrid(Control parent)
        {
            grid = new DataGridView();
            EnableGridDoubleBuffering(grid);
            grid.Location = new Point(34, 329);
            grid.Size = new Size(1168, 397);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.ShowCellToolTips = true;
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
            grid.SortCompare += Grid_SortCompare;
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

        private void BuildSummaryDrawer(Control parent)
        {
            summaryDrawer = new Panel();
            summaryDrawer.Size = new Size(SummaryDrawerWidth, Math.Max(260, grid.Height));
            summaryDrawer.BackColor = Color.White;
            summaryDrawer.BorderStyle = BorderStyle.FixedSingle;
            summaryDrawer.Visible = false;
            summaryDrawer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            parent.Controls.Add(summaryDrawer);

            summaryDrawerTitle = new Label();
            summaryDrawerTitle.Text = "BarList 요약";
            summaryDrawerTitle.AutoSize = true;
            summaryDrawerTitle.Font = OviaFluentTheme.FontTitle(10F, FontStyle.Bold);
            summaryDrawerTitle.ForeColor = TextDark;
            summaryDrawerTitle.Location = new Point(14, 13);
            summaryDrawer.Controls.Add(summaryDrawerTitle);

            summaryPinButton = new OviaBarListPinButton();
            summaryPinButton.Size = new Size(30, 28);
            summaryPinButton.Location = new Point(SummaryDrawerWidth - 76, 7);
            summaryPinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            summaryPinButton.Click += SummaryPinButton_Click;
            windowToolTip.SetToolTip(summaryPinButton, "기본은 Overlay이며, 핀을 고정하면 BarList 폭을 줄이고 요약을 계속 표시합니다.");
            summaryDrawer.Controls.Add(summaryPinButton);

            summaryCloseButton = new Button();
            summaryCloseButton.Text = "×";
            summaryCloseButton.Size = new Size(30, 28);
            summaryCloseButton.Location = new Point(SummaryDrawerWidth - 38, 7);
            summaryCloseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ApplySummaryUtilityButtonStyle(summaryCloseButton);
            summaryCloseButton.Click += SummaryCloseButton_Click;
            summaryDrawer.Controls.Add(summaryCloseButton);

            summarySpecTabButton = CreateSummaryTabButton("규격별", BarListSummaryMode.Spec, 14);
            summaryPartTabButton = CreateSummaryTabButton("부위별", BarListSummaryMode.Part, 96);
            summaryDrawingTabButton = CreateSummaryTabButton("원본도면별", BarListSummaryMode.Drawing, 178);
            summaryDrawer.Controls.Add(summarySpecTabButton);
            summaryDrawer.Controls.Add(summaryPartTabButton);
            summaryDrawer.Controls.Add(summaryDrawingTabButton);

            summaryGrid = new DataGridView();
            EnableGridDoubleBuffering(summaryGrid);
            summaryGrid.Location = new Point(12, 80);
            summaryGrid.Size = new Size(SummaryDrawerWidth - 26, Math.Max(150, summaryDrawer.Height - 116));
            summaryGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            summaryGrid.BackgroundColor = Color.White;
            summaryGrid.BorderStyle = BorderStyle.None;
            summaryGrid.AllowUserToAddRows = false;
            summaryGrid.AllowUserToDeleteRows = false;
            summaryGrid.AllowUserToResizeRows = false;
            summaryGrid.AllowUserToResizeColumns = true;
            summaryGrid.ReadOnly = true;
            summaryGrid.RowHeadersVisible = false;
            summaryGrid.MultiSelect = false;
            summaryGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            summaryGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            summaryGrid.ScrollBars = ScrollBars.Vertical;
            summaryGrid.EnableHeadersVisualStyles = false;
            summaryGrid.ColumnHeadersHeight = 30;
            summaryGrid.RowTemplate.Height = 29;
            summaryGrid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Regular);
            summaryGrid.DefaultCellStyle.ForeColor = TextDark;
            summaryGrid.DefaultCellStyle.SelectionBackColor = OviaFluentTheme.AccentLight;
            summaryGrid.DefaultCellStyle.SelectionForeColor = TextDark;
            summaryGrid.CellClick += SummaryGrid_CellClick;
            OviaFluentTheme.ApplyDataGrid(summaryGrid);
            summaryGrid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
            summaryGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            summaryGrid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Regular);
            summaryGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            summaryGrid.DefaultCellStyle.SelectionBackColor = OviaFluentTheme.AccentLight;
            summaryGrid.DefaultCellStyle.SelectionForeColor = TextDark;

            DataGridViewTextBoxColumn groupColumn = new DataGridViewTextBoxColumn();
            groupColumn.Name = "SummaryGroup";
            groupColumn.HeaderText = "규격";
            groupColumn.FillWeight = 145F;
            groupColumn.MinimumWidth = 100;
            groupColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            groupColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            summaryGrid.Columns.Add(groupColumn);

            DataGridViewTextBoxColumn rowColumn = new DataGridViewTextBoxColumn();
            rowColumn.Name = "SummaryRows";
            rowColumn.HeaderText = "행";
            rowColumn.FillWeight = 52F;
            rowColumn.MinimumWidth = 42;
            rowColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            rowColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            summaryGrid.Columns.Add(rowColumn);

            DataGridViewTextBoxColumn qtyColumn = new DataGridViewTextBoxColumn();
            qtyColumn.Name = "SummaryQty";
            qtyColumn.HeaderText = "수량";
            qtyColumn.FillWeight = 72F;
            qtyColumn.MinimumWidth = 56;
            qtyColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            qtyColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            summaryGrid.Columns.Add(qtyColumn);

            DataGridViewTextBoxColumn lengthColumn = new DataGridViewTextBoxColumn();
            lengthColumn.Name = "SummaryLength";
            lengthColumn.HeaderText = "총길이";
            lengthColumn.FillWeight = 84F;
            lengthColumn.MinimumWidth = 68;
            lengthColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            lengthColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            summaryGrid.Columns.Add(lengthColumn);

            DataGridViewTextBoxColumn weightColumn = new DataGridViewTextBoxColumn();
            weightColumn.Name = "SummaryWeight";
            weightColumn.HeaderText = "중량";
            weightColumn.FillWeight = 72F;
            weightColumn.MinimumWidth = 58;
            weightColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            weightColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            summaryGrid.Columns.Add(weightColumn);

            summaryDrawer.Controls.Add(summaryGrid);

            summaryDrawerHint = new Label();
            summaryDrawerHint.Text = "항목을 클릭하면 해당 데이터만 임시 필터링합니다.";
            summaryDrawerHint.AutoSize = false;
            summaryDrawerHint.Size = new Size(SummaryDrawerWidth - 26, 22);
            summaryDrawerHint.Location = new Point(12, summaryDrawer.Height - 28);
            summaryDrawerHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            summaryDrawerHint.Font = OviaFluentTheme.FontData(8F, FontStyle.Regular);
            summaryDrawerHint.ForeColor = TextSub;
            summaryDrawerHint.TextAlign = ContentAlignment.MiddleLeft;
            summaryDrawer.Controls.Add(summaryDrawerHint);

            RefreshSummaryTabAppearance();
            RefreshSummaryDrawerData();
        }

        private void ApplySummaryUtilityButtonStyle(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = OviaFluentTheme.ButtonNeutralBorder;
            button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.ButtonNeutralBackHover;
            button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.NeutralLight;
            button.BackColor = Color.White;
            button.ForeColor = OviaFluentTheme.ButtonNeutralText;
            button.Font = OviaFluentTheme.FontButton(8.2F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
        }

        private Button CreateSummaryTabButton(string text, BarListSummaryMode mode, int x)
        {
            Button button = new Button();
            button.Text = text;
            button.Tag = mode;
            button.Size = mode == BarListSummaryMode.Drawing ? new Size(92, 30) : new Size(74, 30);
            button.Location = new Point(x, 43);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = OviaFluentTheme.FontButton(8.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Click += SummaryTabButton_Click;
            return button;
        }

        private void BuildSelectionSummaryOverlay(Control parent)
        {
            selectionSummaryPanel = new Panel();
            selectionSummaryPanel.Height = 31;
            selectionSummaryPanel.BackColor = Color.White;
            selectionSummaryPanel.BorderStyle = BorderStyle.FixedSingle;
            selectionSummaryPanel.Visible = false;
            selectionSummaryPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            parent.Controls.Add(selectionSummaryPanel);

            selectionSummaryLabel = new Label();
            selectionSummaryLabel.AutoSize = false;
            selectionSummaryLabel.Location = new Point(10, 3);
            selectionSummaryLabel.Size = new Size(700, 24);
            selectionSummaryLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            selectionSummaryLabel.Font = OviaFluentTheme.FontData(8.5F, FontStyle.Bold);
            selectionSummaryLabel.ForeColor = TextDark;
            selectionSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            selectionSummaryPanel.Controls.Add(selectionSummaryLabel);

            selectionCopyButton = new OviaBarListButton();
            selectionCopyButton.Text = "Ctrl+C 복사";
            selectionCopyButton.Size = new Size(92, 25);
            selectionCopyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            selectionCopyButton.Click += delegate { CopySelectedCellsToClipboard(); };
            selectionSummaryPanel.Controls.Add(selectionCopyButton);

            LayoutSelectionSummaryOverlay();
        }

        private void ContentPanel_Resize(object sender, EventArgs e)
        {
            LayoutBarListFloatingPanels();
        }

        private void LayoutBarListFloatingPanels()
        {
            if (contentPanel == null || grid == null || grid.IsDisposed)
            {
                return;
            }

            int fullGridWidth = Math.Max(240, contentPanel.ClientSize.Width - grid.Left - 38);

            if (summaryDrawer != null && !summaryDrawer.IsDisposed)
            {
                int drawerHeight = Math.Max(220, contentPanel.ClientSize.Height - grid.Top - 34);
                summaryDrawer.Size = new Size(SummaryDrawerWidth, drawerHeight);
                summaryDrawer.Location = new Point(Math.Max(grid.Left + 180, contentPanel.ClientSize.Width - 38 - SummaryDrawerWidth), grid.Top);

                if (summaryDrawerPinned && summaryDrawerVisible)
                {
                    int pinnedWidth = Math.Max(240, summaryDrawer.Left - SummaryDrawerGap - grid.Left);
                    grid.Width = pinnedWidth;
                }
                else
                {
                    grid.Width = fullGridWidth;
                }
            }
            else
            {
                grid.Width = fullGridWidth;
            }

            grid.Height = Math.Max(120, contentPanel.ClientSize.Height - grid.Top - 34);
            LayoutSelectionSummaryOverlay();
        }

        private void LayoutSelectionSummaryOverlay()
        {
            if (selectionSummaryPanel == null || selectionSummaryPanel.IsDisposed || grid == null || grid.IsDisposed || contentPanel == null)
            {
                return;
            }

            int bottomY = Math.Max(grid.Top + 120, contentPanel.ClientSize.Height - 34);
            int fullGridHeight = Math.Max(120, bottomY - grid.Top);

            if (selectionSummaryPanel.Visible)
            {
                grid.Height = Math.Max(120, fullGridHeight - selectionSummaryPanel.Height - 4);
                selectionSummaryPanel.Location = new Point(grid.Left, grid.Bottom + 4);
            }
            else
            {
                grid.Height = fullGridHeight;
                selectionSummaryPanel.Location = new Point(grid.Left, grid.Bottom);
            }

            selectionSummaryPanel.Width = grid.Width;

            if (selectionSummaryLabel != null && selectionCopyButton != null)
            {
                selectionCopyButton.Location = new Point(Math.Max(8, selectionSummaryPanel.ClientSize.Width - selectionCopyButton.Width - 8), 2);
                selectionSummaryLabel.Width = Math.Max(80, selectionCopyButton.Left - 18);
            }

            if (selectionSummaryPanel.Visible)
            {
                selectionSummaryPanel.BringToFront();
            }

            if (summaryDrawer != null && summaryDrawerVisible)
            {
                summaryDrawer.BringToFront();
            }
        }

        private void SummaryButton_Click(object sender, EventArgs e)
        {
            if (summaryDrawer == null || summaryDrawer.IsDisposed)
            {
                return;
            }

            summaryDrawerVisible = !summaryDrawerVisible;
            summaryDrawer.Visible = summaryDrawerVisible;
            summaryButton.DropDownChevronUp = summaryDrawerVisible;
            summaryButton.Invalidate();

            if (summaryDrawerVisible)
            {
                RefreshSummaryDrawerData();
                summaryDrawer.BringToFront();
            }
            else if (hasActiveSummaryFilter)
            {
                ClearSummaryFilter();
            }

            LayoutBarListFloatingPanels();
        }

        private void SummaryCloseButton_Click(object sender, EventArgs e)
        {
            summaryDrawerVisible = false;
            summaryDrawerPinned = false;

            if (summaryDrawer != null)
            {
                summaryDrawer.Visible = false;
            }

            if (summaryButton != null)
            {
                summaryButton.DropDownChevronUp = false;
                summaryButton.Invalidate();
            }

            if (hasActiveSummaryFilter)
            {
                ClearSummaryFilter();
            }

            RefreshSummaryPinAppearance();
            LayoutBarListFloatingPanels();
        }

        private void SummaryPinButton_Click(object sender, EventArgs e)
        {
            summaryDrawerPinned = !summaryDrawerPinned;
            RefreshSummaryPinAppearance();
            LayoutBarListFloatingPanels();
        }

        private void RefreshSummaryPinAppearance()
        {
            if (summaryPinButton == null || summaryPinButton.IsDisposed)
            {
                return;
            }

            summaryPinButton.Pinned = summaryDrawerPinned;
        }

        private void SummaryTabButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;

            if (button == null || !(button.Tag is BarListSummaryMode))
            {
                return;
            }

            BarListSummaryMode nextMode = (BarListSummaryMode)button.Tag;

            if (summaryMode != nextMode && hasActiveSummaryFilter)
            {
                ClearSummaryFilter();
            }

            summaryMode = nextMode;
            RefreshSummaryTabAppearance();
            RefreshSummaryDrawerData();
        }

        private void RefreshSummaryTabAppearance()
        {
            ApplySummaryTabAppearance(summarySpecTabButton, BarListSummaryMode.Spec == summaryMode);
            ApplySummaryTabAppearance(summaryPartTabButton, BarListSummaryMode.Part == summaryMode);
            ApplySummaryTabAppearance(summaryDrawingTabButton, BarListSummaryMode.Drawing == summaryMode);
        }

        private void ApplySummaryTabAppearance(Button button, bool active)
        {
            if (button == null || button.IsDisposed)
            {
                return;
            }

            button.BackColor = active ? OviaFluentTheme.AccentLight : Color.White;
            button.ForeColor = active ? OviaFluentTheme.Accent : TextSub;
            button.FlatAppearance.BorderColor = active ? OviaFluentTheme.Accent : OviaFluentTheme.ButtonNeutralBorder;
        }

        private void RefreshSummaryDrawerData()
        {
            if (summaryGrid == null || summaryGrid.IsDisposed || grid == null)
            {
                return;
            }

            int groupColumnIndex = GetSummaryGroupColumnIndex(summaryMode);
            int qtyColumnIndex = FindColumnIndex("수량");
            int lengthColumnIndex = FindColumnIndex("총길이");
            int weightColumnIndex = FindColumnIndex("중량");
            Dictionary<string, BarListSummaryGroupInfo> groups = new Dictionary<string, BarListSummaryGroupInfo>(StringComparer.CurrentCultureIgnoreCase);
            BarListSummaryGroupInfo total = new BarListSummaryGroupInfo();
            total.DisplayName = "전체";
            total.RawValue = "";
            total.IsTotal = true;

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                string rawValue = groupColumnIndex >= 0 ? GetCellText(r, groupColumnIndex).Trim() : "";
                string key = rawValue;
                BarListSummaryGroupInfo info;

                if (!groups.TryGetValue(key, out info))
                {
                    info = new BarListSummaryGroupInfo();
                    info.RawValue = rawValue;
                    info.DisplayName = rawValue == "" ? "(미입력)" : rawValue;
                    groups.Add(key, info);
                }

                double qty = qtyColumnIndex >= 0 ? ParseNumber(GetCellText(r, qtyColumnIndex)) : 0.0;
                double length = lengthColumnIndex >= 0 ? ParseNumber(GetCellText(r, lengthColumnIndex)) : 0.0;
                decimal weight = 0M;

                if (weightColumnIndex >= 0)
                {
                    TryParseDecimalNumber(GetCellText(r, weightColumnIndex), out weight);
                }

                info.RowCount++;
                info.TotalQty += qty;
                info.TotalLength += length;
                info.TotalWeight += weight;

                total.RowCount++;
                total.TotalQty += qty;
                total.TotalLength += length;
                total.TotalWeight += weight;
            }

            List<BarListSummaryGroupInfo> ordered = new List<BarListSummaryGroupInfo>(groups.Values);
            ordered.Sort(delegate(BarListSummaryGroupInfo left, BarListSummaryGroupInfo right)
            {
                if (summaryMode == BarListSummaryMode.Spec)
                {
                    int leftDiameter = GetSummaryRebarDiameter(left == null ? "" : left.RawValue);
                    int rightDiameter = GetSummaryRebarDiameter(right == null ? "" : right.RawValue);

                    if (leftDiameter >= 0 && rightDiameter >= 0 && leftDiameter != rightDiameter)
                    {
                        return leftDiameter.CompareTo(rightDiameter);
                    }
                }

                return String.Compare(
                    left == null ? "" : left.DisplayName,
                    right == null ? "" : right.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase
                );
            });

            summaryGrid.SuspendLayout();

            try
            {
                summaryGrid.Rows.Clear();
                summaryGrid.Columns[0].HeaderText = GetSummaryModeColumnTitle(summaryMode);
                AddSummaryGridRow(total);

                int i;

                for (i = 0; i < ordered.Count; i++)
                {
                    AddSummaryGridRow(ordered[i]);
                }

                if (summaryGrid.Rows.Count > 0)
                {
                    summaryGrid.Rows[0].DefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Bold);
                    summaryGrid.Rows[0].DefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
                }
            }
            finally
            {
                summaryGrid.ResumeLayout();
            }
        }

        private int GetSummaryRebarDiameter(string spec)
        {
            string baseSpec = ExtractBaseRebarSpec(spec);

            if (baseSpec.Length <= 1)
            {
                return -1;
            }

            int diameter;
            return Int32.TryParse(baseSpec.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out diameter)
                ? diameter
                : -1;
        }

        private void AddSummaryGridRow(BarListSummaryGroupInfo info)
        {
            if (summaryGrid == null || info == null)
            {
                return;
            }

            int index = summaryGrid.Rows.Add(
                info.DisplayName,
                info.RowCount.ToString("N0", CultureInfo.InvariantCulture),
                info.TotalQty.ToString("#,0.###", CultureInfo.InvariantCulture),
                info.TotalLength.ToString("#,0.00", CultureInfo.InvariantCulture),
                info.TotalWeight.ToString("#,0.###", CultureInfo.InvariantCulture)
            );

            summaryGrid.Rows[index].Tag = info;

            if (hasActiveSummaryFilter
                && !info.IsTotal
                && activeSummaryFilterMode == summaryMode
                && String.Equals(info.RawValue, activeSummaryFilterValue, StringComparison.CurrentCultureIgnoreCase))
            {
                summaryGrid.Rows[index].DefaultCellStyle.BackColor = OviaFluentTheme.AccentLight;
                summaryGrid.Rows[index].DefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Bold);
            }

            if (summaryMode == BarListSummaryMode.Drawing && info.DisplayName.Length > 18)
            {
                summaryGrid.Rows[index].Cells[0].ToolTipText = info.DisplayName;
            }
        }

        private int GetSummaryGroupColumnIndex(BarListSummaryMode mode)
        {
            if (mode == BarListSummaryMode.Part)
            {
                return FindColumnIndexByAliases(new string[] { "부위", "위치", "구간" });
            }

            if (mode == BarListSummaryMode.Drawing)
            {
                return FindColumnIndexByAliases(new string[] { "원본 도면", "원본도면", "SOURCE DRAWING" });
            }

            return FindColumnIndexByAliases(new string[] { "철근규격", "철근 규격", "규격", "DIA" });
        }

        private string GetSummaryModeColumnTitle(BarListSummaryMode mode)
        {
            if (mode == BarListSummaryMode.Part)
            {
                return "부위";
            }

            if (mode == BarListSummaryMode.Drawing)
            {
                return "원본도면";
            }

            return "규격";
        }

        private string GetSummaryModeDisplayName(BarListSummaryMode mode)
        {
            if (mode == BarListSummaryMode.Part)
            {
                return "부위";
            }

            if (mode == BarListSummaryMode.Drawing)
            {
                return "원본도면";
            }

            return "규격";
        }

        private void SummaryGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (summaryGrid == null || e.RowIndex < 0 || e.RowIndex >= summaryGrid.Rows.Count)
            {
                return;
            }

            BarListSummaryGroupInfo info = summaryGrid.Rows[e.RowIndex].Tag as BarListSummaryGroupInfo;

            if (info == null)
            {
                return;
            }

            if (info.IsTotal)
            {
                ClearSummaryFilter();
                return;
            }

            hasActiveSummaryFilter = true;
            activeSummaryFilterMode = summaryMode;
            activeSummaryFilterValue = info.RawValue;
            ApplyActiveSummaryFilter();
            UpdateSummaryFilterChip();
            RefreshSummaryDrawerData();

            if (lblStatus != null)
            {
                lblStatus.Text = GetSummaryModeDisplayName(summaryMode) + " [" + info.DisplayName + "] 항목만 표시 중입니다.";
                lblStatus.ForeColor = TextSub;
            }
        }

        private void ApplyActiveSummaryFilter()
        {
            if (grid == null || grid.IsDisposed || isApplyingSummaryFilter)
            {
                return;
            }

            int groupColumnIndex = hasActiveSummaryFilter ? GetSummaryGroupColumnIndex(activeSummaryFilterMode) : -1;
            isApplyingSummaryFilter = true;
            BeginGridSelectionUpdate();
            grid.SuspendLayout();

            try
            {
                grid.ClearSelection();
                grid.CurrentCell = null;
                int r;

                for (r = 0; r < grid.Rows.Count; r++)
                {
                    if (grid.Rows[r].IsNewRow)
                    {
                        continue;
                    }

                    bool visible = true;

                    if (hasActiveSummaryFilter && groupColumnIndex >= 0)
                    {
                        string rowValue = GetCellText(r, groupColumnIndex).Trim();
                        visible = String.Equals(rowValue, activeSummaryFilterValue, StringComparison.CurrentCultureIgnoreCase);
                    }

                    grid.Rows[r].Visible = visible;
                }
            }
            finally
            {
                grid.ResumeLayout();
                EndGridSelectionUpdate();
                isApplyingSummaryFilter = false;
            }

            UpdateSelectionSummaryOverlay();
            grid.Invalidate();
        }

        private void ClearSummaryFilter_Click(object sender, EventArgs e)
        {
            ClearSummaryFilter();
        }

        private void ClearSummaryFilter()
        {
            hasActiveSummaryFilter = false;
            activeSummaryFilterValue = "";
            ApplyActiveSummaryFilter();
            UpdateSummaryFilterChip();
            RefreshSummaryDrawerData();

            if (lblStatus != null)
            {
                lblStatus.Text = "요약 필터를 해제했습니다. 전체 BarList를 표시합니다.";
                lblStatus.ForeColor = TextSub;
            }
        }

        private void UpdateSummaryFilterChip()
        {
            if (filterChipButton == null || filterChipButton.IsDisposed)
            {
                return;
            }

            filterChipButton.Visible = hasActiveSummaryFilter;

            if (hasActiveSummaryFilter)
            {
                string displayValue = activeSummaryFilterValue == "" ? "미입력" : activeSummaryFilterValue;
                string text = "필터: " + displayValue + " ×";
                filterChipButton.Text = text;
                int measured = TextRenderer.MeasureText(text, OviaFluentTheme.FontButton(OviaFluentTheme.ButtonFontSize, FontStyle.Bold)).Width + 24;
                filterChipButton.Width = Math.Max(92, Math.Min(170, measured));
            }

            LayoutActionButtons();
        }

        private void UpdateSelectionSummaryOverlay()
        {
            if (selectionSummaryPanel == null || selectionSummaryLabel == null || grid == null || grid.IsDisposed)
            {
                return;
            }

            List<DataGridViewCell> selectedCells = GetClipboardSelectedCells();

            if (selectedCells.Count <= 1)
            {
                selectionSummaryPanel.Visible = false;
                LayoutSelectionSummaryOverlay();
                return;
            }

            HashSet<int> rowIndexes = new HashSet<int>();
            int visibleColumnCount = 0;
            int c;

            for (c = 0; c < grid.Columns.Count; c++)
            {
                if (grid.Columns[c].Visible)
                {
                    visibleColumnCount++;
                }
            }

            int i;

            for (i = 0; i < selectedCells.Count; i++)
            {
                rowIndexes.Add(selectedCells[i].RowIndex);
            }

            bool fullRowsSelected = visibleColumnCount > 0 && selectedCells.Count == rowIndexes.Count * visibleColumnCount;

            if (fullRowsSelected)
            {
                int qtyCol = FindColumnIndex("수량");
                int lengthCol = FindColumnIndex("총길이");
                int weightCol = FindColumnIndex("중량");
                double qty = 0.0;
                double length = 0.0;
                decimal weight = 0M;

                foreach (int rowIndex in rowIndexes)
                {
                    if (rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
                    {
                        continue;
                    }

                    if (qtyCol >= 0)
                    {
                        qty += ParseNumber(GetCellText(rowIndex, qtyCol));
                    }

                    if (lengthCol >= 0)
                    {
                        length += ParseNumber(GetCellText(rowIndex, lengthCol));
                    }

                    if (weightCol >= 0)
                    {
                        decimal rowWeight;

                        if (TryParseDecimalNumber(GetCellText(rowIndex, weightCol), out rowWeight))
                        {
                            weight += rowWeight;
                        }
                    }
                }

                selectionSummaryLabel.Text = "선택 " + rowIndexes.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + "행   |   수량 " + qty.ToString("#,0.###", CultureInfo.InvariantCulture)
                    + " EA   |   총길이 " + length.ToString("#,0.00", CultureInfo.InvariantCulture)
                    + " M   |   중량 " + weight.ToString("#,0.###", CultureInfo.InvariantCulture) + " Ton";
            }
            else
            {
                selectionSummaryLabel.Text = "선택 " + selectedCells.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + "셀 · " + rowIndexes.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + "행   |   Excel 또는 텍스트로 Ctrl+C 복사 가능";
            }

            selectionSummaryPanel.Visible = true;
            LayoutSelectionSummaryOverlay();
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

            rowCopyMenuItem = new ToolStripMenuItem("행 복사");
            rowCopyMenuItem.Click += ContextRowCopy_Click;
            gridContextMenu.Items.Add(rowCopyMenuItem);

            rowPasteMenuItem = new ToolStripMenuItem("행 붙여넣기");
            rowPasteMenuItem.Click += ContextRowPaste_Click;
            gridContextMenu.Items.Add(rowPasteMenuItem);

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

            ToolStripMenuItem changeSpecItem = new ToolStripMenuItem("규격 변경");
            changeSpecItem.Click += ContextChangeSpec_Click;
            gridContextMenu.Items.Add(changeSpecItem);

            ToolStripMenuItem changeMemoItem = new ToolStripMenuItem("비고 변경");
            changeMemoItem.Click += ContextChangeMemo_Click;
            gridContextMenu.Items.Add(changeMemoItem);

            grid.ContextMenuStrip = gridContextMenu;
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(BaseClientWidth, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "BARLIST", companyId, userId);
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
            if (autoCadSelectionModeActive)
            {
                ActivateAutoCad();
                lblStatus.Text = "CAD 영역 선택모드가 실행 중입니다. 영역별 시작점·끝점을 연속 지정한 뒤 다음 시작점 대기에서 Enter를 한 번 눌러 전체를 전송하세요.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            UpdateAutoCadSelectionButtonState();

            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();

            if (report.RecommendedAutoCad != null && !report.IsAutoCadRunning)
            {
                MessageBox.Show(
                    "AutoCAD가 설치되어 있지만 현재 실행중이 아닙니다.\r\nAutoCAD를 먼저 실행하세요.",
                    "OVIA AutoCAD 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            if (!report.IsCurrentDevelopmentAutoCadReady())
            {
                MessageBox.Show(
                    report.GetAutoCadExtractionBlockMessage(),
                    "OVIA AutoCAD 확인",
                    MessageBoxButtons.OK,
                    report.OverallStatus == OviaEnvironmentStatus.Blocked ? MessageBoxIcon.Error : MessageBoxIcon.Warning
                );

                return;
            }

            if (!CanImportIntoCurrentBarList())
            {
                return;
            }

            if (!PrepareAutoCadImportMode())
            {
                return;
            }

            StartAutoCadWatcher();
            SetAutoCadSelectionModeState(true);
            autoCadSelectionCommandIssuedAt = DateTime.Now;
            autoCadSelectionCommandEndedAt = DateTime.MinValue;
            autoCadSelectionCommandObserved = false;
            autoCadSelectionCommandDispatchReturned = false;
            ActivateAutoCad();

            lblStatus.Text = "CAD 영역 선택모드 실행 중 - 각 범위의 시작점·끝점을 Enter 없이 연속 선택하고, 모든 선택이 끝나면 다음 시작점 대기에서 Enter를 한 번 눌러 하나의 CSV로 전송하세요.";
            lblStatus.ForeColor = TextSub;

            BeginAutoCadCommandDispatch(
                "OVIABOX",
                delegate(bool success, string commandError)
                {
                    autoCadSelectionCommandDispatchReturned = true;

                    if (!success)
                    {
                        waitingAutoCadImport = false;
                        StopAutoCadWatcher();
                        SetAutoCadSelectionModeState(false);

                        MessageBox.Show(
                            commandError,
                            "OVIA AutoCAD 명령 실행",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );

                        return;
                    }

                    TryLoadAutoCadLatestCsv();

                    if (autoCadSelectionModeActive
                        && autoCadSelectionCommandIssuedAt != DateTime.MinValue
                        && DateTime.Now - autoCadSelectionCommandIssuedAt > TimeSpan.FromSeconds(1)
                        && (autoCadLoadedCsvCount > 0 || autoCadSelectionCommandObserved))
                    {
                        SetAutoCadSelectionModeState(false);
                        autoCadSelectionCommandEndedAt = DateTime.Now;
                    }
                }
            );
        }

        private bool PrepareAutoCadImportMode()
        {
            if (!HasGridData())
            {
                autoCadInitialImportMode = BarListImportMode.Replace;
                return true;
            }

            autoCadInitialImportMode = DecideImportModeForCurrentGrid();
            return autoCadInitialImportMode != BarListImportMode.Cancel;
        }

        private void ReleaseAutoCadSelectionMode_Click(object sender, EventArgs e)
        {
            if (!autoCadSelectionModeActive && !waitingAutoCadImport)
            {
                SetAutoCadSelectionModeState(false);
                lblStatus.Text = "CAD 영역 선택모드는 이미 해제되어 있습니다.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            lblStatus.Text = "CAD 영역 선택모드를 해제하는 중입니다.";
            lblStatus.ForeColor = TextSub;

            BeginCancelAutoCadCommand(
                delegate(bool success, string commandError)
                {
                    FlushAutoCadCsvQueueBeforeStop();
                    waitingAutoCadImport = false;
                    StopAutoCadWatcher();
                    SetAutoCadSelectionModeState(false);

                    if (!success)
                    {
                        MessageBox.Show(
                            commandError,
                            "OVIA CAD 선택모드 해제",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    ActivateAutoCad();
                    lblStatus.Text = "CAD 영역 선택모드를 해제했습니다. 최종 Enter 전 미전송 선택영역은 삭제되며 데이터는 추가되지 않습니다.";
                    lblStatus.ForeColor = TextSub;
                }
            );
        }

        private void DeleteAutoCadSelectionBoxes_Click(object sender, EventArgs e)
        {
            if (isDeletingAutoCadSelectionBoxes)
            {
                lblStatus.Text = "CAD 선택영역을 삭제하고 있습니다. 잠시만 기다려 주세요.";
                lblStatus.ForeColor = TextSub;
                return;
            }

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

            SetAutoCadDeleteBusyState(true);
            lblStatus.Text = "CAD의 현재 명령을 종료하고 선택영역을 삭제하고 있습니다. 잠시만 기다려 주세요.";
            lblStatus.ForeColor = TextSub;

            BeginCancelAndRunAutoCadCommand(
                "OVIABOXDEL",
                delegate(bool success, string commandError)
                {
                    try
                    {
                        FlushAutoCadCsvQueueBeforeStop();
                        waitingAutoCadImport = false;
                        StopAutoCadWatcher();
                        SetAutoCadSelectionModeState(false);

                        if (!success)
                        {
                            string friendlyMessage = GetFriendlyAutoCadDeleteError(commandError);
                            lblStatus.Text = "CAD 선택영역을 삭제하지 못했습니다. AutoCAD 상태를 확인한 뒤 다시 실행해 주세요.";
                            lblStatus.ForeColor = OviaFluentTheme.Danger;

                            MessageBox.Show(
                                friendlyMessage,
                                "OVIA CAD 선택영역 삭제",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }

                        lblStatus.Text = "CAD에 표시된 노란 선택영역을 삭제했습니다. 새로운 영역을 다시 선택할 수 있습니다.";
                        lblStatus.ForeColor = TextSub;
                        ActivateAutoCad();
                    }
                    finally
                    {
                        SetAutoCadDeleteBusyState(false);
                    }
                }
            );
        }

        private void SetAutoCadDeleteBusyState(bool isBusy)
        {
            isDeletingAutoCadSelectionBoxes = isBusy;

            if (deleteCadBoxButton != null && !deleteCadBoxButton.IsDisposed)
            {
                deleteCadBoxButton.Text = isBusy ? "CAD 선택영역 삭제 중..." : "CAD에 선택된 영역 삭제";
                deleteCadBoxButton.Enabled = !isBusy && IsAutoCadRunning();
                deleteCadBoxButton.Cursor = isBusy ? Cursors.Default : Cursors.Hand;
                deleteCadBoxButton.Invalidate();
            }

            if (cadSelectionModeOffButton != null && !cadSelectionModeOffButton.IsDisposed)
            {
                cadSelectionModeOffButton.Enabled = !isBusy && IsAutoCadRunning() && autoCadSelectionModeActive;
            }

            if (cadSelectionButton != null && !cadSelectionButton.IsDisposed)
            {
                if (isBusy)
                {
                    cadSelectionButton.Enabled = false;
                    cadSelectionButton.Cursor = Cursors.Default;
                    cadSelectionButton.Invalidate();
                }
                else
                {
                    UpdateAutoCadSelectionButtonState();
                }
            }
        }

        private string GetFriendlyAutoCadDeleteError(string errorMessage)
        {
            if (IsAutoCadBusyError(errorMessage))
            {
                return "AutoCAD가 현재 명령 종료 또는 화면 작업을 처리 중이어서 선택영역을 삭제하지 못했습니다.\r\n\r\n"
                    + "AutoCAD 명령창에서 Esc 키를 한두 번 누른 뒤, ‘CAD에 선택된 영역 삭제’를 다시 실행해 주세요.";
            }

            if (errorMessage == null || errorMessage.Trim() == "")
            {
                return "CAD 선택영역을 삭제하지 못했습니다. AutoCAD에서 현재 명령을 종료한 뒤 다시 실행해 주세요.";
            }

            return errorMessage;
        }

        private void BeginAutoCadCommandDispatch(string command, Action<bool, string> completed)
        {
            System.Threading.Thread commandThread = new System.Threading.Thread(
                delegate()
                {
                    string errorMessage;
                    bool success = TrySendAutoCadCommand(command, out errorMessage);
                    CompleteAutoCadBackgroundAction(completed, success, errorMessage);
                }
            );

            commandThread.IsBackground = true;
            commandThread.SetApartmentState(System.Threading.ApartmentState.STA);
            commandThread.Start();
        }

        private void BeginCancelAutoCadCommand(Action<bool, string> completed)
        {
            System.Threading.Thread commandThread = new System.Threading.Thread(
                delegate()
                {
                    string errorMessage;
                    bool success = TryCancelActiveAutoCadCommand(out errorMessage);
                    CompleteAutoCadBackgroundAction(completed, success, errorMessage);
                }
            );

            commandThread.IsBackground = true;
            commandThread.SetApartmentState(System.Threading.ApartmentState.STA);
            commandThread.Start();
        }

        private void BeginCancelAndRunAutoCadCommand(string command, Action<bool, string> completed)
        {
            System.Threading.Thread commandThread = new System.Threading.Thread(
                delegate()
                {
                    DateTime cancelDeadline = DateTime.UtcNow.AddMilliseconds(AutoCadDeleteTimeoutMs);
                    string cancelError = "";
                    bool cancelled = false;
                    int cancelAttempt = 0;

                    while (DateTime.UtcNow <= cancelDeadline)
                    {
                        cancelAttempt++;
                        cancelled = TryCancelActiveAutoCadCommand(out cancelError);

                        if (cancelled)
                        {
                            break;
                        }

                        Trace.WriteLine("OVIA CAD delete cancel retry " + cancelAttempt.ToString(CultureInfo.InvariantCulture) + ": " + cancelError);

                        if (!IsAutoCadBusyError(cancelError) && cancelAttempt >= 2)
                        {
                            break;
                        }

                        System.Threading.Thread.Sleep(AutoCadDeleteRetryIntervalMs);
                    }

                    if (!cancelled)
                    {
                        CompleteAutoCadBackgroundAction(completed, false, cancelError);
                        return;
                    }

                    string readyError;

                    if (!WaitForAutoCadReadyAfterCancel("OVIABOX", AutoCadDeleteTimeoutMs, out readyError))
                    {
                        CompleteAutoCadBackgroundAction(completed, false, readyError);
                        return;
                    }

                    DateTime commandDeadline = DateTime.UtcNow.AddMilliseconds(AutoCadDeleteTimeoutMs);
                    string commandError = "";
                    bool commandExecuted = false;
                    int commandAttempt = 0;

                    while (DateTime.UtcNow <= commandDeadline)
                    {
                        commandAttempt++;
                        commandExecuted = TrySendAutoCadCommand(command, out commandError);

                        if (commandExecuted)
                        {
                            break;
                        }

                        Trace.WriteLine("OVIA CAD delete command retry " + commandAttempt.ToString(CultureInfo.InvariantCulture) + ": " + commandError);

                        if (!IsAutoCadBusyError(commandError))
                        {
                            break;
                        }

                        System.Threading.Thread.Sleep(AutoCadDeleteRetryIntervalMs);
                    }

                    if (!commandExecuted && IsAutoCadBusyError(commandError))
                    {
                        commandError = AutoCadBusyErrorPrefix + "AutoCAD가 다른 명령을 처리 중이어서 삭제 명령을 받을 수 없습니다.";
                    }

                    if (commandExecuted)
                    {
                        // SendCommand는 비동기 큐에 명령을 넣으므로 버튼이 즉시 다시 눌리지 않게 짧게 안정화합니다.
                        System.Threading.Thread.Sleep(450);
                    }

                    CompleteAutoCadBackgroundAction(completed, commandExecuted, commandError);
                }
            );

            commandThread.IsBackground = true;
            commandThread.SetApartmentState(System.Threading.ApartmentState.STA);
            commandThread.Start();
        }

        private bool WaitForAutoCadReadyAfterCancel(string commandName, int timeoutMilliseconds, out string errorMessage)
        {
            errorMessage = "";
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMilliseconds, 500));

            while (DateTime.UtcNow <= deadline)
            {
                string commandNames;

                if (TryGetAutoCadCommandNames(out commandNames) && commandNames.Trim() == "")
                {
                    return true;
                }

                System.Threading.Thread.Sleep(AutoCadDeleteRetryIntervalMs);
            }

            errorMessage = AutoCadBusyErrorPrefix + "AutoCAD가 현재 선택 명령의 종료 처리를 완료하지 못했습니다.";
            return false;
        }

        private void CompleteAutoCadBackgroundAction(Action<bool, string> completed, bool success, string errorMessage)
        {
            if (completed == null || this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }

            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    completed(success, errorMessage == null ? "" : errorMessage);
                }));
            }
            catch
            {
            }
        }

        private bool TryCancelActiveAutoCadCommand(out string errorMessage)
        {
            errorMessage = "";
            bool commandCancelSent = false;
            string rawError;

            try
            {
                rawError = "";

                bool activeBeforeCancel;

                if (TryIsAutoCadCommandActive("OVIABOX", out activeBeforeCancel) && !activeBeforeCancel)
                {
                    return true;
                }

                ActivateAutoCad();
                System.Threading.Thread.Sleep(120);

                try
                {
                    System.Windows.Forms.SendKeys.SendWait("{ESC}");
                    System.Threading.Thread.Sleep(100);
                    System.Windows.Forms.SendKeys.SendWait("{ESC}");
                    commandCancelSent = true;
                }
                catch
                {
                }

                System.Threading.Thread.Sleep(150);

                bool rawCancelSent = TrySendAutoCadRawCommand("\u0003\u0003", out rawError);

                if (rawCancelSent)
                {
                    commandCancelSent = true;
                }

                System.Threading.Thread.Sleep(250);

                bool stillActive;

                if (TryIsAutoCadCommandActive("OVIABOX", out stillActive) && stillActive)
                {
                    string retryError;
                    TrySendAutoCadRawCommand("\u0003\u0003\r", out retryError);
                    System.Threading.Thread.Sleep(300);

                    if (TryIsAutoCadCommandActive("OVIABOX", out stillActive) && stillActive)
                    {
                        errorMessage = "AutoCAD의 영역 선택 명령이 아직 실행 중입니다. AutoCAD 창에서 Esc를 두 번 누른 뒤 다시 버튼을 눌러 주세요.";
                        return false;
                    }
                }

                if (!commandCancelSent)
                {
                    errorMessage = rawError == null || rawError.Trim() == ""
                        ? "AutoCAD의 현재 선택 명령을 종료하지 못했습니다. AutoCAD 창에서 Esc를 두 번 누른 뒤 다시 시도해 주세요."
                        : rawError;
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                errorMessage = "AutoCAD 선택 명령을 종료하는 중 오류가 발생했습니다.\r\n\r\n상세: " + ex.Message;
                return false;
            }
        }

        private void SetAutoCadSelectionModeState(bool isActive)
        {
            autoCadSelectionModeActive = isActive;

            if (cadSelectionButton != null)
            {
                if (isActive)
                {
                    cadSelectionButton.Text = "CAD에서 영역선택중";
                    cadSelectionButton.StartColor = OviaFluentTheme.Success;
                    cadSelectionButton.EndColor = OviaFluentTheme.Success;
                    cadSelectionButton.UseCustomColors = true;
                    cadSelectionButton.UseDisabledAppearance = false;
                    cadSelectionButton.KeepCustomColorsWhenDisabled = true;
                    cadSelectionButton.Enabled = false;
                    cadSelectionButton.Cursor = Cursors.Default;
                    cadSelectionButton.Invalidate();
                }
                else
                {
                    UpdateAutoCadSelectionButtonState();
                }
            }

            if (cadSelectionModeOffButton != null)
            {
                cadSelectionModeOffButton.Enabled = isActive;
            }
        }

        private void StartAutoCadAvailabilityTimer()
        {
            StopAutoCadAvailabilityTimer();

            autoCadAvailabilityTimer = new System.Windows.Forms.Timer();
            autoCadAvailabilityTimer.Interval = 1000;
            autoCadAvailabilityTimer.Tick += AutoCadAvailabilityTimer_Tick;
            autoCadAvailabilityTimer.Start();

            UpdateAutoCadSelectionButtonState();
        }

        private void StopAutoCadAvailabilityTimer()
        {
            if (autoCadAvailabilityTimer == null)
            {
                return;
            }

            autoCadAvailabilityTimer.Stop();
            autoCadAvailabilityTimer.Tick -= AutoCadAvailabilityTimer_Tick;
            autoCadAvailabilityTimer.Dispose();
            autoCadAvailabilityTimer = null;
        }

        private void AutoCadAvailabilityTimer_Tick(object sender, EventArgs e)
        {
            UpdateAutoCadSelectionButtonState();
        }

        private void UpdateAutoCadSelectionButtonState()
        {
            bool isAutoCadRunning = IsAutoCadRunning();

            UpdateAutoCadAuxiliaryButtonVisibility(isAutoCadRunning);

            if (cadSelectionButton == null || cadSelectionButton.IsDisposed || autoCadSelectionModeActive || isDeletingAutoCadSelectionBoxes)
            {
                return;
            }

            cadSelectionButton.Text = "CAD에서 영역선택";
            cadSelectionButton.StartColor = OviaFluentTheme.Accent;
            cadSelectionButton.EndColor = OviaFluentTheme.Accent;
            cadSelectionButton.UseCustomColors = true;
            cadSelectionButton.KeepCustomColorsWhenDisabled = false;
            cadSelectionButton.UseDisabledAppearance = !isAutoCadRunning;
            cadSelectionButton.Enabled = true;
            cadSelectionButton.Cursor = Cursors.Hand;
            cadSelectionButton.Invalidate();
        }

        private void UpdateAutoCadAuxiliaryButtonVisibility(bool isAutoCadRunning)
        {
            if (cadSelectionModeOffButton != null && !cadSelectionModeOffButton.IsDisposed)
            {
                cadSelectionModeOffButton.Visible = isAutoCadRunning;
                cadSelectionModeOffButton.Enabled = isAutoCadRunning && autoCadSelectionModeActive && !isDeletingAutoCadSelectionBoxes;
            }

            if (deleteCadBoxButton != null && !deleteCadBoxButton.IsDisposed)
            {
                deleteCadBoxButton.Visible = isAutoCadRunning;
                deleteCadBoxButton.Enabled = isAutoCadRunning && !isDeletingAutoCadSelectionBoxes;
            }

            LayoutActionButtons();
        }

        private void ReleaseAutoCadSelectionModeSilently()
        {
            if (autoCadSelectionModeActive)
            {
                string ignoredError;
                TryCancelActiveAutoCadCommand(out ignoredError);
            }

            waitingAutoCadImport = false;
            SetAutoCadSelectionModeState(false);
        }

        private void FlushAutoCadCsvQueueBeforeStop()
        {
            if (!waitingAutoCadImport || autoCadWatcher == null)
            {
                return;
            }

            try
            {
                TryLoadAutoCadLatestCsv();
            }
            catch
            {
            }
        }

        private void StartAutoCadWatcher()
        {
            StopAutoCadWatcher();

            string importDirectory;

            try
            {
                importDirectory = OviaProjectWorkspacePaths.PrepareCadOutputDirectory(projectNo);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CAD 추출 임시폴더를 준비하지 못했습니다: " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return;
            }

            autoImportStartTime = DateTime.Now.AddSeconds(-3);
            waitingAutoCadImport = true;
            autoCadContinuousAppendMode = false;
            isProcessingAutoCadCsvQueue = false;
            autoCadLoadedCsvCount = 0;
            autoCadSelectionCommandIssuedAt = DateTime.MinValue;
            autoCadSelectionCommandEndedAt = DateTime.MinValue;
            autoCadSelectionCommandObserved = false;
            autoCadSelectionCommandDispatchReturned = false;
            autoCadProcessedCsvFiles.Clear();

            List<string> existingCsvFiles = FindOviaBoxTableCsvFilesAfter(DateTime.MinValue);
            int existingIndex;

            for (existingIndex = 0; existingIndex < existingCsvFiles.Count; existingIndex++)
            {
                autoCadProcessedCsvFiles.Add(existingCsvFiles[existingIndex]);
            }

            autoCadWatcher = new FileSystemWatcher();
            autoCadWatcher.Path = importDirectory;
            autoCadWatcher.Filter = "*.csv";
            autoCadWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            autoCadWatcher.Created += AutoCadWatcher_Changed;
            autoCadWatcher.Changed += AutoCadWatcher_Changed;
            autoCadWatcher.EnableRaisingEvents = true;
            StartAutoCadImportPollTimer();

            lblStatus.Text = "CAD에서 영역선택 대기 중 - 각 영역은 선택 즉시 메모리에 누적되며, 최종 Enter로 통합 CSV 1개를 게시합니다.";
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

            StopAutoCadImportPollTimer();
            autoCadContinuousAppendMode = false;
            isProcessingAutoCadCsvQueue = false;
        }

        private void StartAutoCadImportPollTimer()
        {
            StopAutoCadImportPollTimer();

            autoCadImportPollTimer = new System.Windows.Forms.Timer();
            autoCadImportPollTimer.Interval = 500;
            autoCadImportPollTimer.Tick += AutoCadImportPollTimer_Tick;
            autoCadImportPollTimer.Start();
        }

        private void StopAutoCadImportPollTimer()
        {
            if (autoCadImportPollTimer == null)
            {
                return;
            }

            autoCadImportPollTimer.Stop();
            autoCadImportPollTimer.Tick -= AutoCadImportPollTimer_Tick;
            autoCadImportPollTimer.Dispose();
            autoCadImportPollTimer = null;
        }

        private void AutoCadImportPollTimer_Tick(object sender, EventArgs e)
        {
            if (!waitingAutoCadImport || this.IsDisposed)
            {
                return;
            }

            TryLoadAutoCadLatestCsv();

            bool commandActive;

            if (TryIsAutoCadCommandActive("OVIABOX", out commandActive))
            {
                if (commandActive)
                {
                    autoCadSelectionCommandObserved = true;
                    autoCadSelectionCommandEndedAt = DateTime.MinValue;
                }
                else if (autoCadSelectionModeActive
                    && (autoCadSelectionCommandObserved
                        || (autoCadSelectionCommandDispatchReturned
                            && autoCadSelectionCommandIssuedAt != DateTime.MinValue
                            && DateTime.Now - autoCadSelectionCommandIssuedAt > TimeSpan.FromSeconds(2))))
                {
                    SetAutoCadSelectionModeState(false);
                    autoCadSelectionCommandEndedAt = DateTime.Now;
                    lblStatus.Text = autoCadLoadedCsvCount > 0
                        ? "CAD 영역 선택이 완료되었습니다. 마지막 추출 데이터를 확인하고 있습니다."
                        : "CAD 영역 선택이 종료되었습니다. 생성된 추출 데이터를 확인하고 있습니다.";
                    lblStatus.ForeColor = TextSub;
                }
            }

            if (!autoCadSelectionModeActive
                && autoCadSelectionCommandEndedAt != DateTime.MinValue
                && DateTime.Now - autoCadSelectionCommandEndedAt > TimeSpan.FromSeconds(2))
            {
                TryLoadAutoCadLatestCsv();
                waitingAutoCadImport = false;
                StopAutoCadWatcher();

                if (autoCadLoadedCsvCount > 0)
                {
                    lblStatus.Text = "CAD 추출 데이터 입력을 완료했습니다.";
                    lblStatus.ForeColor = TextSub;
                }
                else
                {
                    lblStatus.Text = "CAD 선택은 종료되었지만 새 OVIA_BoxTable CSV를 찾지 못했습니다. AutoCAD 명령창의 추출 오류를 확인해 주세요.";
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                }
            }
        }

        private bool TryGetAutoCadCommandNames(out string commandNames)
        {
            commandNames = "";
            object autoCadApplication = null;
            object activeDocument = null;

            try
            {
                autoCadApplication = Marshal.GetActiveObject("AutoCAD.Application");

                if (autoCadApplication == null)
                {
                    return false;
                }

                activeDocument = autoCadApplication.GetType().InvokeMember(
                    "ActiveDocument",
                    BindingFlags.GetProperty,
                    null,
                    autoCadApplication,
                    null
                );

                if (activeDocument == null)
                {
                    return false;
                }

                object value = activeDocument.GetType().InvokeMember(
                    "GetVariable",
                    BindingFlags.InvokeMethod,
                    null,
                    activeDocument,
                    new object[] { "CMDNAMES" }
                );

                commandNames = value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (activeDocument != null && Marshal.IsComObject(activeDocument))
                {
                    Marshal.ReleaseComObject(activeDocument);
                }

                if (autoCadApplication != null && Marshal.IsComObject(autoCadApplication))
                {
                    Marshal.ReleaseComObject(autoCadApplication);
                }
            }
        }

        private bool TryIsAutoCadCommandActive(string commandName, out bool isActive)
        {
            isActive = false;
            object autoCadApplication = null;
            object activeDocument = null;

            try
            {
                autoCadApplication = Marshal.GetActiveObject("AutoCAD.Application");

                if (autoCadApplication == null)
                {
                    return false;
                }

                activeDocument = autoCadApplication.GetType().InvokeMember(
                    "ActiveDocument",
                    BindingFlags.GetProperty,
                    null,
                    autoCadApplication,
                    null
                );

                if (activeDocument == null)
                {
                    return false;
                }

                object commandNames = activeDocument.GetType().InvokeMember(
                    "GetVariable",
                    BindingFlags.InvokeMethod,
                    null,
                    activeDocument,
                    new object[] { "CMDNAMES" }
                );

                string value = commandNames == null ? "" : Convert.ToString(commandNames, CultureInfo.InvariantCulture);
                isActive = value.IndexOf(commandName, StringComparison.OrdinalIgnoreCase) >= 0;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (activeDocument != null && Marshal.IsComObject(activeDocument))
                {
                    Marshal.ReleaseComObject(activeDocument);
                }

                if (autoCadApplication != null && Marshal.IsComObject(autoCadApplication))
                {
                    Marshal.ReleaseComObject(autoCadApplication);
                }
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
            if (isProcessingAutoCadCsvQueue)
            {
                return;
            }

            isProcessingAutoCadCsvQueue = true;
            bool loadedAny = false;
            string lastImportStatusText = "";
            Color lastImportStatusColor = TextSub;

            try
            {
                List<string> filePaths = FindOviaBoxTableCsvFilesAfter(autoImportStartTime);
                int fileIndex;

                for (fileIndex = 0; fileIndex < filePaths.Count; fileIndex++)
                {
                    string filePath = filePaths[fileIndex];

                    if (autoCadProcessedCsvFiles.Contains(filePath))
                    {
                        continue;
                    }

                    if (!WaitUntilFileReady(filePath))
                    {
                        continue;
                    }

                    bool loaded = LoadCsvWithImportPolicy(filePath, false);

                    if (!loaded)
                    {
                        waitingAutoCadImport = false;
                        StopAutoCadWatcher();
                        return;
                    }

                    autoCadProcessedCsvFiles.Add(filePath);
                    autoCadImportedCsvFiles.Add(filePath);
                    autoCadLoadedCsvCount++;
                    waitingAutoCadImport = true;
                    autoCadContinuousAppendMode = true;
                    loadedAny = true;

                    if (lblStatus != null)
                    {
                        lastImportStatusText = lblStatus.Text;
                        lastImportStatusColor = lblStatus.ForeColor;
                    }
                }
            }
            finally
            {
                isProcessingAutoCadCsvQueue = false;
            }

            if (!loadedAny)
            {
                return;
            }

            // AppendCsv가 남긴 실제 추가/중복/무효 행 결과를 일반 완료 문구로 덮어쓰지 않습니다.
            // 특히 CSV는 생성됐지만 신규 철근행이 0개인 경우 원인을 화면에서 확인할 수 있어야 합니다.
            lblStatus.Text = (lastImportStatusText == ""
                ? "추출 완료"
                : lastImportStatusText)
                + "  다음 영역을 계속 선택하면 자동 추가됩니다.";
            lblStatus.ForeColor = lastImportStatusColor;

            if (autoCadSelectionModeActive)
            {
                ActivateAutoCad();
                return;
            }

            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }

            this.Activate();
        }

        private bool WaitUntilFileReady(string filePath)
        {
            string readyMarkerPath = filePath + ".ready";
            long previousLength = -1;
            int stableCount = 0;
            int i;

            for (i = 0; i < 30; i++)
            {
                try
                {
                    FileInfo csvInfo = new FileInfo(filePath);
                    FileInfo markerInfo = new FileInfo(readyMarkerPath);

                    if (csvInfo.Exists && csvInfo.Length > 0 && markerInfo.Exists && markerInfo.Length > 0)
                    {
                        bool lengthStable = csvInfo.Length == previousLength;
                        bool writeSettled = DateTime.Now - csvInfo.LastWriteTime > TimeSpan.FromMilliseconds(200)
                            && DateTime.Now - markerInfo.LastWriteTime > TimeSpan.FromMilliseconds(100);

                        if (lengthStable && writeSettled && ValidateAutoCadExtractionPackage(filePath))
                        {
                            stableCount++;
                            if (stableCount >= 2)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                        }

                        previousLength = csvInfo.Length;
                    }
                }
                catch
                {
                    stableCount = 0;
                }

                Application.DoEvents();
                System.Threading.Thread.Sleep(100);
            }

            return false;
        }

        private bool ValidateAutoCadExtractionPackage(string filePath)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);
                if (rows == null || rows.Count < 2 || rows[0] == null || rows[0].Count == 0)
                {
                    return false;
                }

                int headerCount = rows[0].Count;
                int rowTypeColumn = FindCsvColumnIndex(rows[0], "ROWTYPE");
                int markColumn = FindCsvColumnIndex(rows[0], "번호", "MARK", "MARKNO", "BARNO");
                int specColumn = FindCsvColumnIndex(rows[0], "철근규격", "철근 규격", "규격", "DIA");
                int lengthColumn = FindCsvColumnIndex(rows[0], "길이MM", "길이(mm)", "길이", "LENGTH");
                int qtyColumn = FindCsvColumnIndex(rows[0], "수량EA", "수량(EA)", "수량", "QTY", "QUANTITY");

                if (rowTypeColumn < 0 || markColumn < 0 || specColumn < 0 || lengthColumn < 0 || qtyColumn < 0)
                {
                    return false;
                }

                int actualRebarRowCount = 0;

                for (int r = 1; r < rows.Count; r++)
                {
                    List<string> row = rows[r];
                    if (row == null || row.Count != headerCount)
                    {
                        return false;
                    }

                    if (!GetCsvCellText(row, rowTypeColumn).Equals("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsActualRebarCsvRow(row, markColumn, specColumn, lengthColumn, qtyColumn))
                    {
                        actualRebarRowCount++;
                    }
                }

                /*
                 * CSV와 .ready는 원자적으로 게시되므로 데이터 준비 여부는 고정 컬럼과 실제 DATA 행으로
                 * 판단합니다. CAD_NO_CELL_BOUNDS, CAD_EMPTY, CAD_JSON_SAVE_FAILED 또는 누락된 형상 JSON은
                 * 해당 행의 형상 표시 문제일 뿐 번호·규격·길이·수량 데이터를 버릴 사유가 아닙니다.
                 * 이전에는 한 행의 형상 상태만 달라도 CSV 전체를 거부하여 60~64가 추가되지 않았습니다.
                 */
                return actualRebarRowCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsAutoCadRunning()
        {
            Process[] processes = null;

            try
            {
                processes = Process.GetProcessesByName("acad");
                return processes != null && processes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (processes != null)
                {
                    int i;

                    for (i = 0; i < processes.Length; i++)
                    {
                        if (processes[i] != null)
                        {
                            processes[i].Dispose();
                        }
                    }
                }
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

        private bool TrySendAutoCadCommand(string command, out string errorMessage)
        {
            errorMessage = "";

            if (command == null || command.Trim() == "")
            {
                errorMessage = "실행할 AutoCAD 명령이 지정되지 않았습니다.";
                return false;
            }

            return TrySendAutoCadRawCommand(command.Trim() + "\r", out errorMessage);
        }

        private bool TrySendAutoCadRawCommand(string commandText, out string errorMessage)
        {
            errorMessage = "";

            if (commandText == null || commandText.Length == 0)
            {
                errorMessage = "실행할 AutoCAD 명령이 지정되지 않았습니다.";
                return false;
            }

            object autoCadApplication = null;
            object activeDocument = null;

            try
            {
                autoCadApplication = Marshal.GetActiveObject("AutoCAD.Application");

                if (autoCadApplication == null)
                {
                    errorMessage = "실행 중인 AutoCAD에 연결하지 못했습니다. AutoCAD와 도면을 연 뒤 다시 시도해 주세요.";
                    return false;
                }

                activeDocument = autoCadApplication.GetType().InvokeMember(
                    "ActiveDocument",
                    BindingFlags.GetProperty,
                    null,
                    autoCadApplication,
                    null
                );

                if (activeDocument == null)
                {
                    errorMessage = "AutoCAD에서 활성 도면을 찾지 못했습니다. DWG 도면을 연 뒤 다시 시도해 주세요.";
                    return false;
                }

                activeDocument.GetType().InvokeMember(
                    "SendCommand",
                    BindingFlags.InvokeMethod,
                    null,
                    activeDocument,
                    new object[] { commandText }
                );

                return true;
            }
            catch (COMException ex)
            {
                if (IsAutoCadBusyHResult(ex.HResult))
                {
                    errorMessage = AutoCadBusyErrorPrefix + ex.Message;
                    return false;
                }

                errorMessage = "AutoCAD 명령을 전달하지 못했습니다. AutoCAD와 활성 도면을 확인하고 OVIA 플러그인이 로드되어 있는지 확인해 주세요.\r\n\r\n상세: " + ex.Message;
                return false;
            }
            catch (TargetInvocationException ex)
            {
                Exception detail = ex.InnerException == null ? ex : ex.InnerException;
                COMException comDetail = detail as COMException;

                if (comDetail != null && IsAutoCadBusyHResult(comDetail.HResult))
                {
                    errorMessage = AutoCadBusyErrorPrefix + comDetail.Message;
                    return false;
                }

                errorMessage = "AutoCAD 명령을 실행하지 못했습니다. AutoCAD와 활성 도면을 확인하고 OVIA 플러그인이 로드되어 있는지 확인해 주세요.\r\n\r\n상세: " + detail.Message;
                return false;
            }
            catch (System.Exception ex)
            {
                errorMessage = "AutoCAD 명령 실행 중 오류가 발생했습니다. AutoCAD와 활성 도면, OVIA 플러그인 로드 상태를 확인해 주세요.\r\n\r\n상세: " + ex.Message;
                return false;
            }
            finally
            {
                if (activeDocument != null && Marshal.IsComObject(activeDocument))
                {
                    Marshal.ReleaseComObject(activeDocument);
                }

                if (autoCadApplication != null && Marshal.IsComObject(autoCadApplication))
                {
                    Marshal.ReleaseComObject(autoCadApplication);
                }
            }
        }

        private bool IsAutoCadBusyHResult(int hresult)
        {
            return hresult == RpcECallRejected || hresult == RpcEServerCallRetryLater;
        }

        private bool IsAutoCadBusyError(string errorMessage)
        {
            return errorMessage != null
                && errorMessage.StartsWith(AutoCadBusyErrorPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void LoadRecent_Click(object sender, EventArgs e)
        {
            string filePath = FindLatestOviaBoxTableCsv();

            if (filePath == "")
            {
                MessageBox.Show(
                    "현재 공사의 CAD 임시폴더에서 OVIA_BoxTable CSV 파일을 찾지 못했습니다.\r\n\r\nCAD에서 영역선택 버튼으로 도면 영역을 다시 추출해 주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            LoadCsvWithImportPolicy(filePath, false);
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

            LoadCsvWithImportPolicy(dialog.FileName, false);
        }

        private void CommitPendingGridEdit()
        {
            if (grid == null || grid.IsDisposed)
            {
                return;
            }

            try
            {
                // 마지막 셀이 아직 편집 모드인 채로 저장 버튼을 누르면
                // CellEndEdit가 저장 완료 이후(예: 뒤로가기 클릭 시점)에 발생하면서
                // MarkUnsaved()가 호출되어 실제 저장본과 무관하게 미저장 상태가 될 수 있다.
                // 저장/이동 판정 전에 현재 편집을 먼저 확정하여 이벤트 순서를 고정한다.
                if (grid.IsCurrentCellInEditMode)
                {
                    grid.EndEdit();
                }
            }
            catch
            {
                // 편집 확정 실패는 기존 저장/이동 흐름의 오류 처리에 맡긴다.
            }
        }

        private async void SaveProjectBarList_Click(object sender, EventArgs e)
        {
            // 저장 baseline을 잡기 전에 현재 셀의 편집값을 확정한다.
            // 이렇게 해야 저장 후 뒤로가기 시 늦게 발생한 CellEndEdit가 저장 상태를 다시 dirty로 만들지 않는다.
            CommitPendingGridEdit();

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

            RefreshSaveStateFromCurrentGrid();

            if (isSaved)
            {
                MessageBox.Show(
                    "변경된 내용이 없습니다",
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

                List<string> workLogDescriptions = BuildBarListWorkLogDescriptions();

                // 기존 ERP 고유 idx는 화면 그리드 컬럼이 아니라 CSV 시스템 메타데이터에 저장된다.
                // SaveGridToCsv가 파일을 다시 쓸 때 이 메타데이터가 사라지면 다음 저장이 신규 INSERT로 처리되므로
                // 저장 직전 idx를 보관하고 CSV 재작성 직후 반드시 복원한다.
                int persistedErpBarListId = OviaErpBarListSyncService.GetPersistedErpBarListId(filePath);
                if (persistedErpBarListId <= 0 && registrationDraft != null && registrationDraft.ErpBarListId > 0)
                {
                    // 신규등록 팝업에서 이미 ERP barlist 헤더를 생성했다.
                    // 첫 상세 저장은 반드시 그 idx를 재사용하여 중복 INSERT가 발생하지 않게 한다.
                    persistedErpBarListId = registrationDraft.ErpBarListId;
                }

                NormalizeCadShapeJsonFilesForSave(filePath);
                SaveGridToCsv(filePath);
                OviaErpBarListSyncService.RestorePersistedErpBarListId(filePath, persistedErpBarListId);

                OviaErpBarListSyncResult erpSync = await OviaErpBarListSyncService.PushSavedBarListAsync(
                    companyId,
                    projectNo,
                    filePath);

                ResetAllRowOriginalValuesToCurrent();

                allowExtractEditMenu = true;
                ClearUndoRedoStates();
                grid.Invalidate();

                lblStatus.Text = erpSync.IsSuccess
                    ? "BarList 저장 완료 - ERP와 동기화되었습니다."
                    : "BarList 로컬 저장 완료 - ERP 동기화 보류: " + erpSync.Message;
                lblStatus.ForeColor = erpSync.IsSuccess ? TextSub : OviaFluentTheme.Danger;

                if (!erpSync.IsSuccess)
                {
                    MessageBox.Show(
                        "BarList는 OVIA 로컬에 저장되었습니다.\r\n\r\nERP에는 아직 반영되지 않았습니다.\r\n"
                        + erpSync.Message
                        + "\r\n\r\n서버/API 상태를 확인한 뒤 '검토 후 저장'을 다시 눌러주세요.",
                        "OVIA ERP 동기화",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
                else
                {
                    // ERP 저장 성공 후에는 project_no + ERP idx 기준의 단일 canonical cache만 유지한다.
                    if (!string.IsNullOrWhiteSpace(erpSync.LocalCachePath) && File.Exists(erpSync.LocalCachePath))
                    {
                        filePath = erpSync.LocalCachePath;
                    }

                    // AutoCAD → OVIA 전달용 CSV/.ready/Temp Shapes는 ERP 저장 성공 후 수명이 끝난다.
                    CleanupImportedAutoCadTempPackages();
                }

                // 저장 경로를 먼저 현재 BarList의 canonical 경로로 확정한 뒤 저장 baseline을 잡는다.
                // 신규 BarList 첫 저장에서 baseline을 먼저 잡으면 lastLoadedFilePath(CAD 추출 CSV)의
                // ERP pending 상태를 잘못 참조하여 실제 저장 성공 후에도 isSaved=false가 남을 수 있다.
                SetReferenceFilePath(filePath);
                lastLoadedFilePath = filePath;
                savedProjectFilePath = filePath;

                if (erpSync.IsSuccess)
                {
                    CaptureSavedGridBaseline();
                }
                else
                {
                    // 로컬 저장만 성공하고 ERP 반영이 실패한 경우에는 기존 정책대로 미저장 상태를 유지한다.
                    RefreshSaveStateFromCurrentGrid();
                }

                OviaBarListRegistrationDraftStore.Clear(companyId, projectNo);
                RefreshProjectContextHeaderFromGrid();
                WriteBarListWorkLogs(workLogDescriptions);
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

        private List<string> BuildBarListWorkLogDescriptions()
        {
            List<string> logs = new List<string>();

            if (savedGridBaseline == null)
            {
                logs.Add("BarList 저장");
                return logs;
            }

            GridUndoSnapshot current = CaptureGridState();
            GridUndoSnapshot saved = savedGridBaseline;
            Dictionary<long, int> currentRows = BuildSnapshotRowIndexMap(current);
            Dictionary<long, int> savedRows = BuildSnapshotRowIndexMap(saved);

            foreach (KeyValuePair<long, int> pair in currentRows)
            {
                if (!savedRows.ContainsKey(pair.Key))
                {
                    AddUniqueWorkLog(logs, "BarList 행 추가");
                }
            }

            foreach (KeyValuePair<long, int> pair in savedRows)
            {
                if (!currentRows.ContainsKey(pair.Key))
                {
                    AddUniqueWorkLog(logs, "BarList 행 삭제");
                }
            }

            foreach (KeyValuePair<long, int> pair in currentRows)
            {
                int savedIndex;
                if (!savedRows.TryGetValue(pair.Key, out savedIndex)) continue;

                int currentIndex = pair.Value;
                object[] currentRow = currentIndex >= 0 && currentIndex < current.Rows.Count ? current.Rows[currentIndex] : null;
                object[] savedRow = savedIndex >= 0 && savedIndex < saved.Rows.Count ? saved.Rows[savedIndex] : null;
                if (currentRow == null || savedRow == null) continue;

                string currentShape = currentIndex < current.ShapeContentFingerprints.Count ? current.ShapeContentFingerprints[currentIndex] : "";
                string savedShape = savedIndex < saved.ShapeContentFingerprints.Count ? saved.ShapeContentFingerprints[savedIndex] : "";
                if (!string.Equals(currentShape, savedShape, StringComparison.Ordinal))
                {
                    AddUniqueWorkLog(logs, "철근형상 수정");
                }

                int columnCount = Math.Min(currentRow.Length, savedRow.Length);
                for (int c = 0; c < columnCount && c < grid.Columns.Count; c++)
                {
                    string currentText = currentRow[c] == null ? "" : currentRow[c].ToString();
                    string savedText = savedRow[c] == null ? "" : savedRow[c].ToString();
                    if (string.Equals(currentText, savedText, StringComparison.Ordinal)) continue;

                    string description = GetBarListColumnChangeDescription(c);
                    if (description != "") AddUniqueWorkLog(logs, description);
                }
            }

            if (logs.Count == 0 && !AreGridStatesEquivalent(current, saved))
            {
                logs.Add("BarList 내용 수정");
            }

            return logs;
        }

        private Dictionary<long, int> BuildSnapshotRowIndexMap(GridUndoSnapshot state)
        {
            Dictionary<long, int> map = new Dictionary<long, int>();
            if (state == null) return map;

            for (int i = 0; i < state.Rows.Count; i++)
            {
                long key = i < state.RowOrderKeys.Count ? state.RowOrderKeys[i] : (i + 1);
                if (!map.ContainsKey(key)) map.Add(key, i);
            }
            return map;
        }

        private string GetBarListColumnChangeDescription(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count) return "";
            DataGridViewColumn column = grid.Columns[columnIndex];
            string header = column.HeaderText == null ? "" : column.HeaderText.Trim();
            string name = column.Name == null ? "" : column.Name.Trim();
            string key = (header + " " + name).ToUpperInvariant();

            if (key.Contains("OVIA_CAD_SHAPE") || key.Contains("CAD_SHAPE_JSON")
                || key.Contains("SHAPE_SOURCE") || key.Contains("SHAPE_STATUS")
                || key.Contains("SHAPE_TEXT") || key.Contains("형상치수")
                || IsRebarShapeHeader(header))
            {
                return "철근형상 수정";
            }

            if (header == "부위") return "부위 수정";
            if (header == "번호") return "번호 수정";
            if (header.Contains("철근규격") || header == "규격") return "철근규격 수정";
            if ((header.Contains("길이") && !header.Contains("총길이")) || header == "길이(mm)") return "길이 수정";
            if (header.Contains("수량")) return "수량 수정";
            if (header == "비고") return "비고 수정";
            if (header == "제목") return "제목 수정";
            if (header == "발주일") return "발주일 수정";
            if (header == "납기일") return "납기일 수정";
            if (header == "등록일") return "등록일 수정";

            // 계산 결과/원본 경로/OVIA 내부 메타데이터는 사용자가 직접 한 작업으로 기록하지 않는다.
            if (header.Contains("총길이") || header.Contains("중량") || header.Contains("원본")
                || key.Contains("OVIA_") || key.Contains("SOURCE_"))
            {
                return "";
            }

            if (column.Visible && header != "") return header + " 수정";
            return "";
        }

        private void AddUniqueWorkLog(List<string> logs, string description)
        {
            if (logs == null || string.IsNullOrWhiteSpace(description)) return;
            for (int i = 0; i < logs.Count; i++)
            {
                if (string.Equals(logs[i], description, StringComparison.Ordinal)) return;
            }
            logs.Add(description);
        }

        private void WriteBarListWorkLogs(List<string> descriptions)
        {
            if (descriptions == null || descriptions.Count == 0) return;
            const string route = "메인  ›  공사관리  ›  공사별 BarList  ›  BarList";
            for (int i = 0; i < descriptions.Count; i++)
            {
                string description = descriptions[i] == null ? "" : descriptions[i].Trim();
                if (description == "") continue;
                OviaNotificationStore.AddWorkLog(companyId, userId, description, route);
            }
        }

        private void CleanupImportedAutoCadTempPackages()
        {
            if (autoCadImportedCsvFiles == null || autoCadImportedCsvFiles.Count == 0) return;

            string tempDirectory = GetProjectBarListTempDirectory();
            List<string> completed = new List<string>();

            foreach (string csvPath in autoCadImportedCsvFiles)
            {
                if (!OviaProjectWorkspacePaths.IsPathInsideDirectory(csvPath, tempDirectory)) continue;

                try
                {
                    DeleteAutoCadTempFile(csvPath);
                    DeleteAutoCadTempFile(csvPath + ".tmp");
                    DeleteAutoCadTempFile(csvPath + ".ready");
                    DeleteAutoCadTempFile(csvPath + ".ready.tmp");

                    string csvBaseName = Path.GetFileNameWithoutExtension(csvPath);
                    if (!string.IsNullOrWhiteSpace(csvBaseName))
                    {
                        string extractionShapeDirectory = Path.Combine(tempDirectory, "Shapes", SanitizeFileName(csvBaseName));
                        if (Directory.Exists(extractionShapeDirectory)) Directory.Delete(extractionShapeDirectory, true);
                    }

                    completed.Add(csvPath);
                }
                catch
                {
                    // ERP 저장은 이미 완료되었으므로 임시파일 정리 실패가 저장 성공을 되돌리지는 않는다.
                }
            }

            for (int i = 0; i < completed.Count; i++) autoCadImportedCsvFiles.Remove(completed[i]);

            try
            {
                string tempShapes = Path.Combine(tempDirectory, "Shapes");
                if (Directory.Exists(tempShapes) && Directory.GetFileSystemEntries(tempShapes).Length == 0) Directory.Delete(tempShapes);
            }
            catch
            {
            }
        }

        private void DeleteAutoCadTempFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private string GetProjectBarListDirectory()
        {
            return OviaProjectWorkspacePaths.GetProjectBarListDirectory(projectNo);
        }

        private string GetProjectBarListTempDirectory()
        {
            return OviaProjectWorkspacePaths.GetProjectBarListTempDirectory(projectNo);
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
                OviaNotificationStore.AddWorkLog(companyId, userId, "BarList CSV 저장", "메인  ›  공사관리  ›  공사별 BarList  ›  BarList");
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

        private void ExcelExport_Click(object sender, EventArgs e)
        {
            if (!HasGridData())
            {
                MessageBox.Show(
                    "Excel로 저장할 BarList 데이터가 없습니다.",
                    "OVIA Excel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "BarList Excel 저장";
            dialog.Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx";
            string projectFileName = SanitizeFileName(projectName);

            if (projectFileName.Trim('_') == "")
            {
                projectFileName = "BarList";
            }

            string filterSuffix = "";

            if (hasActiveSummaryFilter)
            {
                string filterValue = activeSummaryFilterValue == "" ? "미입력" : activeSummaryFilterValue;
                filterSuffix = "_" + SanitizeFileName(filterValue);
            }

            dialog.FileName = DateTime.Now.ToString("yyyy-MM-dd") + "_" + projectFileName + "_BarList" + filterSuffix + ".xlsx";
            dialog.RestoreDirectory = true;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                int exportedRows = SaveCurrentBarListToExcelXlsx(dialog.FileName);
                lblStatus.Text = hasActiveSummaryFilter
                    ? "현재 필터된 " + exportedRows.ToString("N0", CultureInfo.InvariantCulture) + "행과 철근형상을 Excel로 저장했습니다."
                    : exportedRows.ToString("N0", CultureInfo.InvariantCulture) + "개 BarList 행과 철근형상을 Excel로 저장했습니다.";
                lblStatus.ForeColor = TextSub;
                OviaNotificationStore.AddWorkLog(companyId, userId, "BarList Excel 저장", "메인  ›  공사관리  ›  공사별 BarList  ›  BarList");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Excel 저장 실패 - " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                MessageBox.Show(
                    "Excel 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA Excel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private int SaveCurrentBarListToExcelXlsx(string filePath)
        {
            int qtyColumnIndex = FindColumnIndex("수량");
            int lengthColumnIndex = FindColumnIndex("총길이");
            int weightColumnIndex = FindColumnIndex("중량");
            int exportedRows = 0;
            double exportedQty = 0.0;
            double exportedLength = 0.0;
            decimal exportedWeight = 0M;
            List<int> visibleColumnIndexes = new List<int>();
            List<DataGridViewColumn> visibleGridColumns = new List<DataGridViewColumn>();
            BarListExcelDocument document = new BarListExcelDocument();
            int c;

            for (c = 0; c < grid.Columns.Count; c++)
            {
                DataGridViewColumn gridColumn = grid.Columns[c];

                if (!gridColumn.Visible || IsInternalOviaColumn(gridColumn.HeaderText))
                {
                    continue;
                }

                visibleGridColumns.Add(gridColumn);
            }

            visibleGridColumns.Sort(delegate(DataGridViewColumn left, DataGridViewColumn right)
            {
                return left.DisplayIndex.CompareTo(right.DisplayIndex);
            });

            for (c = 0; c < visibleGridColumns.Count; c++)
            {
                DataGridViewColumn gridColumn = visibleGridColumns[c];
                visibleColumnIndexes.Add(gridColumn.Index);
                document.Columns.Add(CreateBarListExcelColumn(gridColumn));
            }

            if (visibleColumnIndexes.Count == 0)
            {
                throw new InvalidOperationException("Excel로 저장할 표시 컬럼이 없습니다.");
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow || !grid.Rows[r].Visible)
                {
                    continue;
                }

                exportedRows++;

                if (qtyColumnIndex >= 0)
                {
                    exportedQty += ParseNumber(GetCellText(r, qtyColumnIndex));
                }

                if (lengthColumnIndex >= 0)
                {
                    exportedLength += ParseNumber(GetCellText(r, lengthColumnIndex));
                }

                if (weightColumnIndex >= 0)
                {
                    decimal rowWeight;

                    if (TryParseDecimalNumber(GetCellText(r, weightColumnIndex), out rowWeight))
                    {
                        exportedWeight += rowWeight;
                    }
                }

                BarListExcelRow exportRow = new BarListExcelRow();
                int visibleIndex;
                int shapeColumnIndex = -1;

                for (visibleIndex = 0; visibleIndex < visibleColumnIndexes.Count; visibleIndex++)
                {
                    int gridColumnIndex = visibleColumnIndexes[visibleIndex];
                    DataGridViewCell cell = grid.Rows[r].Cells[gridColumnIndex];
                    exportRow.Values.Add(GetCellClipboardDisplayText(cell));

                    if (IsRebarShapeColumn(gridColumnIndex))
                    {
                        shapeColumnIndex = gridColumnIndex;
                    }
                }

                if (shapeColumnIndex >= 0)
                {
                    exportRow.ShapePngBytes = RenderRebarShapeForExcel(r, shapeColumnIndex, exportRow.ShapeTexts);
                }

                document.Rows.Add(exportRow);
            }

            string barListTitle = GetFirstNonEmptyGridValue(new string[] { "제목", "BarList 제목", "바리스트 제목" });
            StringBuilder title = new StringBuilder();

            if (!String.IsNullOrWhiteSpace(projectNo))
            {
                title.Append(projectNo.Trim());
            }

            if (!String.IsNullOrWhiteSpace(projectName))
            {
                if (title.Length > 0)
                {
                    title.Append(" ");
                }

                title.Append(projectName.Trim());
            }

            if (!String.IsNullOrWhiteSpace(barListTitle))
            {
                if (title.Length > 0)
                {
                    title.Append(" | ");
                }

                title.Append(barListTitle.Trim());
            }

            document.ProjectTitle = title.Length == 0 ? "OVIA BarList" : title.ToString();
            document.SummaryText = "행 "
                + exportedRows.ToString("N0", CultureInfo.InvariantCulture)
                + "    수량 " + exportedQty.ToString("#,0.###", CultureInfo.InvariantCulture) + " EA"
                + "    총길이 " + exportedLength.ToString("#,0.00", CultureInfo.InvariantCulture) + " M"
                + "    중량 " + exportedWeight.ToString("#,0.000", CultureInfo.InvariantCulture) + " Ton";

            BarListExcelExporter.Save(filePath, document);
            return exportedRows;
        }

        private BarListExcelColumn CreateBarListExcelColumn(DataGridViewColumn gridColumn)
        {
            BarListExcelColumn column = new BarListExcelColumn();
            string header = gridColumn == null || gridColumn.HeaderText == null ? "" : gridColumn.HeaderText.Trim();
            column.Header = header;
            column.Width = gridColumn == null ? 12.0 : Math.Max(7.0, Math.Min(45.0, gridColumn.Width / 7.0));

            if (IsRebarShapeHeader(header))
            {
                column.CellType = BarListExcelCellType.Shape;
                column.Width = Math.Max(30.0, column.Width);
                return column;
            }

            string normalized = NormalizeInternalColumnToken(header);

            if (normalized == "총길이M" || normalized == "총길이")
            {
                column.CellType = BarListExcelCellType.Number2;
            }
            else if (normalized == "중량TON" || normalized == "총중량TON" || normalized == "중량" || normalized == "총중량")
            {
                column.CellType = BarListExcelCellType.Number3;
            }
            else if (normalized == "길이MM" || normalized == "길이" || normalized == "수량EA" || normalized == "수량")
            {
                column.CellType = BarListExcelCellType.NumberGeneral;
            }
            else if (GetBarListCellAlignment(header) == DataGridViewContentAlignment.MiddleCenter)
            {
                column.CellType = BarListExcelCellType.TextCenter;
            }
            else
            {
                column.CellType = BarListExcelCellType.TextLeft;
            }

            return column;
        }

        private byte[] RenderRebarShapeForExcel(int rowIndex, int shapeColumnIndex, List<BarListExcelShapeText> shapeTexts)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || shapeColumnIndex < 0 || shapeColumnIndex >= grid.Columns.Count)
            {
                return null;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[shapeColumnIndex];
            object rawValue = cell.Value;
            string rawText = rawValue == null ? "" : rawValue.ToString();
            string shapeNoText = GetShapeNumberText(rowIndex);
            RebarShapeInfo shape = GetShapeRepository().FindByRawValue(rawText);

            if (shape == null && shapeNoText != "")
            {
                shape = GetShapeRepository().FindByRawValue(shapeNoText);

                if (shape != null)
                {
                    rawText = shape.DisplayCode;
                }
            }

            string cadShapePath = ResolveCadShapeJsonPath(GetCadShapeJsonText(rowIndex));
            string shapeSource = GetShapeSourceText(rowIndex);
            bool cadSource = shapeSource != null && shapeSource.Trim().Equals("CAD", StringComparison.OrdinalIgnoreCase);
            bool manualVectorSource = IsManualVectorEditedRow(rowIndex) && cadShapePath != "";
            bool drawCadShape = (!IsManualShapeOverrideRow(rowIndex) || manualVectorSource) && (cadSource || cadShapePath != "");
            string dimensionText = GetShapeDimensionText(rowIndex);

            if (!drawCadShape && shape == null && String.IsNullOrWhiteSpace(rawText) && String.IsNullOrWhiteSpace(dimensionText))
            {
                return null;
            }

            const int imageWidth = 320;
            const int imageHeight = 96;

            /*
             * Excel에서는 철근 선/원호는 PNG로 유지하되 CAD SOURCE_CELL 형상의 치수문자는
             * 이미지에 굽지 않고 DrawingML 텍스트 상자로 분리한다.
             * 따라서 Excel에서 축소되어도 수치가 래스터와 함께 작아지지 않고 선명하게 인쇄된다.
             * 기존 CadShapeRenderer/JSON은 읽기 전용이며 수정하지 않는다.
             */
            if (drawCadShape && cadShapePath != "" && File.Exists(cadShapePath))
            {
                byte[] cadGeometry = RenderCadRebarShapeGeometryForExcel(
                    cadShapePath,
                    dimensionText,
                    IsCadShapeTextEditedRow(rowIndex),
                    shapeTexts,
                    imageWidth,
                    imageHeight
                );

                if (cadGeometry != null && cadGeometry.Length > 0)
                {
                    return cadGeometry;
                }

                if (shapeTexts != null)
                {
                    shapeTexts.Clear();
                }
            }

            using (Bitmap bitmap = new Bitmap(imageWidth, imageHeight))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (MemoryStream stream = new MemoryStream())
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                Rectangle bounds = new Rectangle(2, 2, imageWidth - 4, imageHeight - 4);

                if (drawCadShape)
                {
                    cadShapeRenderer.DrawCadShape(
                        graphics,
                        bounds,
                        cadShapePath,
                        false,
                        dimensionText,
                        IsCadShapeTextEditedRow(rowIndex),
                        1F
                    );
                }
                else
                {
                    shapeRenderer.DrawShape(
                        graphics,
                        bounds,
                        shape,
                        rawText,
                        false,
                        dimensionText
                    );
                }

                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private byte[] RenderCadRebarShapeGeometryForExcel(
            string cadShapePath,
            string dimensionText,
            bool applyTextOverrides,
            List<BarListExcelShapeText> shapeTexts,
            int imageWidth,
            int imageHeight)
        {
            CadShapeData data = LoadCadShapeDataForExcel(cadShapePath);

            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return null;
            }

            CadShapeData displayData = CadShapeDisplayNormalizer.CreateDisplayData(data);

            if (displayData != null)
            {
                data = displayData;
            }

            /*
             * Excel 형상 내 문자 분리 규칙:
             * - SOURCE_CELL뿐 아니라 과거 CONTENT_BOUNDS 및 OVIA_EDIT/OVIA_MANUAL JSON도 모두 처리한다.
             * - 철근 geometry(LINE/ARC/CIRCLE)만 PNG에 그리고 TEXT는 절대로 PNG에 굽지 않는다.
             * - TEXT는 BarListExcelShapeText로 변환해 Excel DrawingML native text box로 저장한다.
             *
             * 20260812_03은 SOURCE_CELL만 허용하여 CONTENT_BOUNDS/편집 JSON이 기존 CadShapeRenderer
             * fallback으로 내려가면서 숫자가 다시 PNG에 포함될 수 있었다. 여기서는 레이아웃 정책을
             * 제한 조건으로 사용하지 않고, CadShapeRenderer와 동일하게 SOURCE_CELL이면 물리 셀 기준,
             * 그 외에는 실제 요소 bounds 기준으로 좌표계를 계산한다.
             */
            bool hasGeometry = HasCadGeometryForExcel(data);

            if (!hasGeometry)
            {
                TryEnsureStraightShapeFallbackForExcel(data);
                hasGeometry = HasCadGeometryForExcel(data);
            }

            if (!hasGeometry && !HasCadTextForExcel(data))
            {
                return null;
            }

            bool useSourceCellLayout = data.LayoutPolicy != null
                && data.LayoutPolicy.Trim().Equals("SOURCE_CELL", StringComparison.OrdinalIgnoreCase)
                && data.Width > 0.0001D
                && data.Height > 0.0001D;
            double contentMinX;
            double contentMinY;
            double contentMaxX;
            double contentMaxY;

            if (useSourceCellLayout)
            {
                contentMinX = 0D;
                contentMinY = 0D;
                contentMaxX = Math.Max(data.Width, 1D);
                contentMaxY = Math.Max(data.Height, 1D);
            }
            else if (!TryGetCadElementBoundsForExcel(data, out contentMinX, out contentMinY, out contentMaxX, out contentMaxY))
            {
                contentMinX = 0D;
                contentMinY = 0D;
                contentMaxX = Math.Max(data.Width, 1D);
                contentMaxY = Math.Max(data.Height, 1D);
            }

            double contentWidth = Math.Max(contentMaxX - contentMinX, 1D);
            double contentHeight = Math.Max(contentMaxY - contentMinY, 1D);
            const double visualScale = 0.90D;
            RectangleF drawArea = new RectangleF(2F, 2F, Math.Max(1, imageWidth - 4), Math.Max(1, imageHeight - 4));
            double scale = Math.Min(drawArea.Width / contentWidth, drawArea.Height / contentHeight) * visualScale;

            // 기존 CadShapeRenderer의 CONTENT_BOUNDS 일자형 과대 확대 방지 규칙을 Excel에도 동일 적용합니다.
            if (!useSourceCellLayout && IsStraightHorizontalCadShapeForExcel(data))
            {
                double straightWidthScale = drawArea.Width * 0.60D / contentWidth;

                if (straightWidthScale < scale)
                {
                    scale = straightWidthScale;
                }
            }

            float offsetX = drawArea.Left
                + (float)((drawArea.Width - contentWidth * scale) / 2.0D)
                - (float)(contentMinX * scale);
            float offsetY = drawArea.Top
                + (float)((drawArea.Height - contentHeight * scale) / 2.0D)
                - (float)(contentMinY * scale);
            bool useOviaEditedRotation = UsesOviaEditedRotationForExcel(data);
            Dictionary<string, string> overrideValues = applyTextOverrides
                ? BuildCadTextOverrideMapForExcel(dimensionText)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int overrideTextIndex = 0;

            using (Bitmap bitmap = new Bitmap(imageWidth, imageHeight))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (MemoryStream stream = new MemoryStream())
            using (Pen pen = new Pen(Color.FromArgb(8, 12, 22), Math.Max(1.15F, Math.Min(1.85F, imageHeight / 56F))))
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                int i;

                // PNG에는 오직 geometry만 그립니다. TEXT는 아래에서 native Excel textbox로 분리합니다.
                for (i = 0; i < data.Elements.Count; i++)
                {
                    CadShapeElement element = data.Elements[i];

                    if (element == null)
                    {
                        continue;
                    }

                    if (element.Type == "LINE")
                    {
                        graphics.DrawLine(
                            pen,
                            (float)(offsetX + element.X1 * scale),
                            (float)(offsetY + element.Y1 * scale),
                            (float)(offsetX + element.X2 * scale),
                            (float)(offsetY + element.Y2 * scale)
                        );
                    }
                    else if (element.Type == "CIRCLE")
                    {
                        float radius = (float)(element.Radius * scale);
                        float centerX = (float)(offsetX + element.CX * scale);
                        float centerY = (float)(offsetY + element.CY * scale);
                        graphics.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2F, radius * 2F);
                    }
                    else if (element.Type == "ARC")
                    {
                        float radius = (float)(element.Radius * scale);
                        float centerX = (float)(offsetX + element.CX * scale);
                        float centerY = (float)(offsetY + element.CY * scale);
                        RectangleF arcBounds = new RectangleF(centerX - radius, centerY - radius, radius * 2F, radius * 2F);
                        float startAngle = (float)(-element.StartAngle);
                        float sweepAngle = (float)(-(element.EndAngle - element.StartAngle));

                        if (Math.Abs(sweepAngle) < 0.1F)
                        {
                            sweepAngle = 360F;
                        }

                        graphics.DrawArc(pen, arcBounds, startAngle, sweepAngle);
                    }
                }

                if (shapeTexts != null)
                {
                    for (i = 0; i < data.Elements.Count; i++)
                    {
                        CadShapeElement element = data.Elements[i];

                        if (element == null || element.Type != "TEXT")
                        {
                            continue;
                        }

                        string text = element.Text == null ? "" : element.Text.Trim();
                        string replacement = "";
                        string textId = element.TextId == null ? "" : element.TextId.Trim();

                        if (textId != "" && overrideValues.TryGetValue(textId, out replacement))
                        {
                            replacement = replacement == null ? "" : replacement.Trim();
                        }
                        else
                        {
                            string legacyKey = GetLegacyCadOverrideKeyForExcel(overrideTextIndex);

                            if (legacyKey != "")
                            {
                                overrideValues.TryGetValue(legacyKey, out replacement);
                                replacement = replacement == null ? "" : replacement.Trim();
                            }
                        }

                        if (replacement != "")
                        {
                            text = replacement;
                        }

                        overrideTextIndex++;

                        if (text == "")
                        {
                            continue;
                        }

                        double x = offsetX + element.X1 * scale;
                        double y = offsetY + element.Y1 * scale;
                        double rotation = NormalizeCadTextRotationForExcel(element.Rotation);

                        if (!useOviaEditedRotation)
                        {
                            rotation = -rotation;
                        }

                        double textScale = Math.Max(0.55D, Math.Min(1.45D, Math.Sqrt(Math.Max(0.25D, element.TextScale))));
                        double fontSizePt = Math.Max(9.5D, Math.Min(11.0D, 9.5D * textScale));
                        double boxWidthPixels;
                        double boxHeightPixels;

                        if (element.HasBounds
                            && element.BoundsMaxX > element.BoundsMinX
                            && element.BoundsMaxY > element.BoundsMinY)
                        {
                            // CAD 원본 TEXT extents를 Excel textbox 크기에 직접 반영합니다.
                            boxWidthPixels = Math.Max(28.0D, (element.BoundsMaxX - element.BoundsMinX) * scale + 8.0D);
                            boxHeightPixels = Math.Max(17.0D, (element.BoundsMaxY - element.BoundsMinY) * scale + 6.0D);
                        }
                        else
                        {
                            Size measured;

                            using (Font font = OviaFluentTheme.FontKorean((float)fontSizePt, FontStyle.Regular, GraphicsUnit.Point))
                            {
                                measured = TextRenderer.MeasureText(
                                    text,
                                    font,
                                    new Size(1200, 240),
                                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                                );
                            }

                            boxWidthPixels = Math.Max(28.0D, measured.Width + 8.0D);
                            boxHeightPixels = Math.Max(17.0D, measured.Height + 4.0D);
                        }

                        BarListExcelShapeText item = new BarListExcelShapeText();
                        item.Text = text;
                        item.CenterXRatio = ClampExcelRatio(x / imageWidth);
                        item.CenterYRatio = ClampExcelRatio(y / imageHeight);
                        item.WidthRatio = Math.Max(0.10D, Math.Min(0.78D, boxWidthPixels / imageWidth));
                        item.HeightRatio = Math.Max(0.18D, Math.Min(0.48D, boxHeightPixels / imageHeight));
                        item.RotationDegrees = rotation;
                        item.FontSizePt = fontSizePt;
                        shapeTexts.Add(item);
                    }
                }

                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private bool HasCadTextForExcel(CadShapeData data)
        {
            if (data == null || data.Elements == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element != null && element.Type == "TEXT" && !String.IsNullOrWhiteSpace(element.Text))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasCadGeometryForExcel(CadShapeData data)
        {
            if (data == null || data.Elements == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element != null && (element.Type == "LINE" || element.Type == "ARC" || element.Type == "CIRCLE"))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryEnsureStraightShapeFallbackForExcel(CadShapeData data)
        {
            if (data == null || data.Elements == null || data.Elements.Count == 0 || HasCadGeometryForExcel(data))
            {
                return;
            }

            CadShapeElement onlyText = null;
            int textCount = 0;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null || element.Type != "TEXT" || String.IsNullOrWhiteSpace(element.Text))
                {
                    continue;
                }

                textCount++;
                onlyText = element;
            }

            if (textCount != 1 || onlyText == null)
            {
                return;
            }

            string numericText = onlyText.Text.Trim().Replace(",", "");
            double numericValue;

            if ((!Double.TryParse(numericText, NumberStyles.Any, CultureInfo.InvariantCulture, out numericValue)
                && !Double.TryParse(numericText, out numericValue))
                || numericValue <= 0D)
            {
                return;
            }

            double textHeight = Math.Max(onlyText.Height, 1.0D);
            double lineLength = Math.Max(textHeight * 7.5D, 12.0D);
            double lineY = onlyText.Y1 + Math.Max(textHeight * 0.95D, 0.8D);
            CadShapeElement line = new CadShapeElement();
            line.Type = "LINE";
            line.X1 = onlyText.X1 - lineLength / 2.0D;
            line.Y1 = lineY;
            line.X2 = onlyText.X1 + lineLength / 2.0D;
            line.Y2 = lineY;
            data.Elements.Insert(0, line);
        }

        private bool TryGetCadElementBoundsForExcel(
            CadShapeData data,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY)
        {
            minX = Double.MaxValue;
            minY = Double.MaxValue;
            maxX = Double.MinValue;
            maxY = Double.MinValue;

            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return false;
            }

            bool geometryFound = false;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE")
                {
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.X1, element.Y1);
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.X2, element.Y2);
                    geometryFound = true;
                }
                else if (element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.CX - element.Radius, element.CY - element.Radius);
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.CX + element.Radius, element.CY + element.Radius);
                    geometryFound = true;
                }
            }

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null || element.Type != "TEXT")
                {
                    continue;
                }

                if (element.HasBounds)
                {
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMinX, element.BoundsMinY);
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMaxX, element.BoundsMaxY);
                }
                else if (geometryFound)
                {
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.X1, element.Y1);
                }
                else
                {
                    double textScale = Math.Max(0.25D, element.TextScale);
                    double estimatedHeight = Math.Max(element.Height, 0.8D) * textScale;
                    double estimatedWidth = Math.Max(estimatedHeight * 3.0D, estimatedHeight);
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 - estimatedWidth / 2.0D, element.Y1 - estimatedHeight / 2.0D);
                    IncludeCadExcelPoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 + estimatedWidth / 2.0D, element.Y1 + estimatedHeight / 2.0D);
                }
            }

            if (minX == Double.MaxValue || minY == Double.MaxValue || maxX == Double.MinValue || maxY == Double.MinValue)
            {
                return false;
            }

            if (maxX <= minX)
            {
                maxX = minX + 1D;
            }

            if (maxY <= minY)
            {
                maxY = minY + 1D;
            }

            return true;
        }

        private void IncludeCadExcelPoint(
            ref double minX,
            ref double minY,
            ref double maxX,
            ref double maxY,
            double x,
            double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private bool IsStraightHorizontalCadShapeForExcel(CadShapeData data)
        {
            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return false;
            }

            bool hasLine = false;
            double minY = Double.MaxValue;
            double maxY = Double.MinValue;
            double maxLineLength = 0D;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null || element.Type == "TEXT")
                {
                    continue;
                }

                if (element.Type != "LINE")
                {
                    return false;
                }

                double dx = Math.Abs(element.X2 - element.X1);
                double dy = Math.Abs(element.Y2 - element.Y1);
                double lineLength = Math.Sqrt(dx * dx + dy * dy);
                double horizontalTolerance = Math.Max(lineLength * 0.035D, 0.10D);

                if (dy > horizontalTolerance || dx <= 0.0001D)
                {
                    return false;
                }

                hasLine = true;
                maxLineLength = Math.Max(maxLineLength, lineLength);
                minY = Math.Min(minY, Math.Min(element.Y1, element.Y2));
                maxY = Math.Max(maxY, Math.Max(element.Y1, element.Y2));
            }

            if (!hasLine)
            {
                return false;
            }

            double verticalSpread = Math.Max(maxY - minY, 0D);
            return verticalSpread <= Math.Max(maxLineLength * 0.05D, 0.20D);
        }

        private CadShapeData LoadCadShapeDataForExcel(string path)
        {
            try
            {
                if (path == null || path.Trim() == "" || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                CadShapeData data = new CadShapeData();
                data.Version = (int)Math.Round(GetJsonNumberForExcel(json, "version", 1D));
                data.Source = GetJsonStringForExcel(json, "source");
                data.LayoutPolicy = GetJsonStringForExcel(json, "layoutPolicy");
                data.Width = GetJsonNumberForExcel(json, "width", 100D);
                data.Height = GetJsonNumberForExcel(json, "height", 60D);
                MatchCollection matches = Regex.Matches(json, "\\{[^\\{\\}]*\\\"type\\\"[^\\{\\}]*\\}", RegexOptions.Singleline);
                int i;

                for (i = 0; i < matches.Count; i++)
                {
                    string item = matches[i].Value;
                    CadShapeElement element = new CadShapeElement();
                    element.Type = GetJsonStringForExcel(item, "type").ToUpperInvariant();
                    element.Text = GetJsonStringForExcel(item, "text");
                    element.TextId = GetJsonStringForExcel(item, "textId");
                    element.X1 = GetJsonNumberForExcel(item, "x1", GetJsonNumberForExcel(item, "x", 0D));
                    element.Y1 = GetJsonNumberForExcel(item, "y1", GetJsonNumberForExcel(item, "y", 0D));
                    element.X2 = GetJsonNumberForExcel(item, "x2", 0D);
                    element.Y2 = GetJsonNumberForExcel(item, "y2", 0D);
                    element.CX = GetJsonNumberForExcel(item, "cx", 0D);
                    element.CY = GetJsonNumberForExcel(item, "cy", 0D);
                    element.Radius = GetJsonNumberForExcel(item, "radius", 0D);
                    element.StartAngle = GetJsonNumberForExcel(item, "startAngle", 0D);
                    element.EndAngle = GetJsonNumberForExcel(item, "endAngle", 0D);
                    element.Height = GetJsonNumberForExcel(item, "height", 0D);
                    element.TextScale = Math.Max(0.25D, GetJsonNumberForExcel(item, "textScale", 1D));
                    element.Rotation = GetJsonNumberForExcel(item, "rotation", 0D);
                    element.HasBounds = HasJsonNumberForExcel(item, "boundsMinX")
                        && HasJsonNumberForExcel(item, "boundsMinY")
                        && HasJsonNumberForExcel(item, "boundsMaxX")
                        && HasJsonNumberForExcel(item, "boundsMaxY");
                    element.BoundsMinX = GetJsonNumberForExcel(item, "boundsMinX", 0D);
                    element.BoundsMinY = GetJsonNumberForExcel(item, "boundsMinY", 0D);
                    element.BoundsMaxX = GetJsonNumberForExcel(item, "boundsMaxX", 0D);
                    element.BoundsMaxY = GetJsonNumberForExcel(item, "boundsMaxY", 0D);
                    data.Elements.Add(element);
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        private bool HasJsonNumberForExcel(string json, string key)
        {
            if (json == null || key == null)
            {
                return false;
            }

            return Regex.IsMatch(
                json,
                "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*-?\\d+(?:\\.\\d+)?",
                RegexOptions.Singleline
            );
        }

        private double GetJsonNumberForExcel(string json, string key, double defaultValue)
        {
            if (json == null || key == null)
            {
                return defaultValue;
            }

            Match match = Regex.Match(
                json,
                "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)",
                RegexOptions.Singleline
            );

            if (!match.Success)
            {
                return defaultValue;
            }

            double value;

            if (Double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return defaultValue;
        }

        private string GetJsonStringForExcel(string json, string key)
        {
            if (json == null || key == null)
            {
                return "";
            }

            Match match = Regex.Match(
                json,
                "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"",
                RegexOptions.Singleline
            );

            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private Dictionary<string, string> BuildCadTextOverrideMapForExcel(string dimensionText)
        {
            Dictionary<string, string> values = ParseDimensionValuesForExcel(dimensionText);
            string[] legacyKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < legacyKeys.Length; i++)
            {
                string value;

                if (values.TryGetValue(legacyKeys[i], out value))
                {
                    string textId = "T" + (i + 1).ToString(CultureInfo.InvariantCulture);

                    if (!values.ContainsKey(textId))
                    {
                        values[textId] = value;
                    }
                }
            }

            return values;
        }

        private Dictionary<string, string> ParseDimensionValuesForExcel(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (text == null)
            {
                return values;
            }

            string[] parts = text.Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim();

                if (part == "")
                {
                    continue;
                }

                int eq = part.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string value = part.Substring(eq + 1).Trim();

                if (key != "" && value != "")
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private string GetLegacyCadOverrideKeyForExcel(int textIndex)
        {
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };

            if (textIndex < 0 || textIndex >= keys.Length)
            {
                return "";
            }

            return keys[textIndex];
        }

        private bool UsesOviaEditedRotationForExcel(CadShapeData data)
        {
            if (data == null || data.Source == null)
            {
                return false;
            }

            string source = data.Source.Trim();
            return source.Equals("OVIA_EDIT", StringComparison.OrdinalIgnoreCase)
                || source.Equals("OVIA_MANUAL", StringComparison.OrdinalIgnoreCase);
        }

        private double NormalizeCadTextRotationForExcel(double value)
        {
            while (value > 180D)
            {
                value -= 360D;
            }

            while (value < -180D)
            {
                value += 360D;
            }

            return value;
        }

        private double ClampExcelRatio(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                return 0.5D;
            }

            if (value < 0D)
            {
                return 0D;
            }

            if (value > 1D)
            {
                return 1D;
            }

            return value;
        }

        private void OtherBarList_Click(object sender, EventArgs e)
        {
            if (!CanImportIntoCurrentBarList())
            {
                return;
            }

            List<OtherBarListFileInfo> allFiles = DiscoverOtherBarListFiles();

            if (allFiles.Count == 0)
            {
                MessageBox.Show(
                    "가져올 수 있는 다른 BarList를 찾지 못했습니다.\r\n\r\n공사별 BarList에 저장된 다른 BarList가 있는지 확인해주세요.",
                    "OVIA 다른 BarList",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            string selectedFilePath = "";
            List<int> selectedSourceRows = new List<int>();
            List<List<string>> currentPreviewRows = null;
            OtherBarListFileInfo currentFileInfo = null;

            using (Form dialog = new Form())
            {
                OviaFluentTheme.ApplyForm(dialog);
                OviaWindowCaptionTheme.Attach(dialog);
                dialog.Text = "다른 BarList 가져오기";
                dialog.ShowIcon = true;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = true;
                dialog.ClientSize = new Size(1460, 720);
                dialog.MinimumSize = new Size(1200, 620);
                dialog.BackColor = SurfaceColor;
                dialog.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);

                Label searchLabel = new Label();
                searchLabel.Text = "BarList 검색";
                searchLabel.AutoSize = true;
                searchLabel.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
                searchLabel.ForeColor = TextSub;
                searchLabel.Location = new Point(20, 20);
                dialog.Controls.Add(searchLabel);

                TextBox searchBox = new TextBox();
                searchBox.Location = new Point(96, 16);
                searchBox.Size = new Size(404, 26);
                searchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                OviaFluentTheme.ApplyTextBox(searchBox);
                dialog.Controls.Add(searchBox);

                DataGridView fileGrid = new DataGridView();
                EnableGridDoubleBuffering(fileGrid);
                fileGrid.Location = new Point(20, 58);
                fileGrid.Size = new Size(420, 578);
                fileGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
                fileGrid.ReadOnly = true;
                fileGrid.AllowUserToAddRows = false;
                fileGrid.AllowUserToDeleteRows = false;
                fileGrid.AllowUserToResizeRows = false;
                fileGrid.RowHeadersVisible = false;
                fileGrid.MultiSelect = false;
                fileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                fileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                fileGrid.BackgroundColor = Color.White;
                fileGrid.BorderStyle = BorderStyle.FixedSingle;
                fileGrid.EnableHeadersVisualStyles = false;
                fileGrid.ColumnHeadersHeight = 31;
                fileGrid.RowTemplate.Height = 31;
                fileGrid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Regular);
                fileGrid.DefaultCellStyle.SelectionBackColor = OviaFluentTheme.AccentLight;
                fileGrid.DefaultCellStyle.SelectionForeColor = TextDark;
                OviaFluentTheme.ApplyDataGrid(fileGrid);
                fileGrid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
                fileGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
                fileGrid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.4F, FontStyle.Regular);
                fileGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                fileGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
                fileGrid.DefaultCellStyle.SelectionBackColor = OviaFluentTheme.AccentLight;
                fileGrid.DefaultCellStyle.SelectionForeColor = TextDark;
                fileGrid.Columns.Add("OtherNo", "No.");
                fileGrid.Columns.Add("OtherProject", "공사");
                fileGrid.Columns.Add("OtherTitle", "BarList");
                fileGrid.Columns.Add("OtherDate", "수정일");
                fileGrid.Columns.Add("OtherRows", "행");
                fileGrid.Columns[0].FillWeight = 42F;
                fileGrid.Columns[1].FillWeight = 118F;
                fileGrid.Columns[2].FillWeight = 190F;
                fileGrid.Columns[3].FillWeight = 92F;
                fileGrid.Columns[4].FillWeight = 48F;
                fileGrid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
                for (int fileColumnIndex = 0; fileColumnIndex < fileGrid.Columns.Count; fileColumnIndex++)
                {
                    fileGrid.Columns[fileColumnIndex].SortMode = DataGridViewColumnSortMode.Automatic;
                }
                fileGrid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                fileGrid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                dialog.Controls.Add(fileGrid);

                DataGridView previewGrid = new DataGridView();
                EnableGridDoubleBuffering(previewGrid);
                previewGrid.Location = new Point(454, 58);
                previewGrid.Size = new Size(986, 578);
                previewGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                previewGrid.ReadOnly = true;
                previewGrid.AllowUserToAddRows = false;
                previewGrid.AllowUserToDeleteRows = false;
                previewGrid.AllowUserToResizeRows = false;
                previewGrid.RowHeadersVisible = true;
                previewGrid.RowHeadersWidth = 48;
                previewGrid.TopLeftHeaderCell.Value = "No.";
                previewGrid.MultiSelect = true;
                previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                previewGrid.BackgroundColor = Color.White;
                previewGrid.BorderStyle = BorderStyle.FixedSingle;
                previewGrid.EnableHeadersVisualStyles = false;
                previewGrid.ColumnHeadersHeight = 31;
                previewGrid.RowTemplate.Height = 52;
                previewGrid.DefaultCellStyle.Font = OviaFluentTheme.FontData(8.2F, FontStyle.Regular);
                previewGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
                previewGrid.DefaultCellStyle.SelectionForeColor = TextDark;
                OviaFluentTheme.ApplyDataGrid(previewGrid);
                previewGrid.ColumnHeadersDefaultCellStyle.BackColor = OviaFluentTheme.HeaderBackground;
                previewGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
                previewGrid.ColumnHeadersDefaultCellStyle.Font = OviaFluentTheme.FontData(8.2F, FontStyle.Regular);
                previewGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                previewGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
                previewGrid.TopLeftHeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                previewGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 248, 205);
                previewGrid.DefaultCellStyle.SelectionForeColor = TextDark;
                AddOtherBarListPreviewColumns(previewGrid);
                previewGrid.CellPainting += OtherBarListPreviewGrid_CellPainting;
                dialog.Controls.Add(previewGrid);

                Label selectionLabel = new Label();
                selectionLabel.Text = "선택 0행";
                selectionLabel.AutoSize = false;
                selectionLabel.Location = new Point(434, 614);
                selectionLabel.Size = new Size(420, 30);
                selectionLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                selectionLabel.Font = OviaFluentTheme.FontData(8.7F, FontStyle.Bold);
                selectionLabel.ForeColor = TextDark;
                selectionLabel.BackColor = Color.Transparent;
                selectionLabel.TextAlign = ContentAlignment.MiddleLeft;
                dialog.Controls.Add(selectionLabel);

                OVIA.Desktop.Controls.OviaButton importSelectedButton = new OVIA.Desktop.Controls.OviaButton();
                importSelectedButton.Text = "선택 행 가져오기";
                importSelectedButton.Role = OviaButtonRole.Primary;
                importSelectedButton.Size = new Size(124, OviaFluentTheme.ButtonHeight);
                importSelectedButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                dialog.Controls.Add(importSelectedButton);

                OVIA.Desktop.Controls.OviaButton importAllButton = new OVIA.Desktop.Controls.OviaButton();
                importAllButton.Text = "전체 가져오기";
                importAllButton.Role = OviaButtonRole.Neutral;
                importAllButton.Size = new Size(108, OviaFluentTheme.ButtonHeight);
                importAllButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                dialog.Controls.Add(importAllButton);

                Action layoutPopupBottom = delegate
                {
                    int buttonY = Math.Max(12, dialog.ClientSize.Height - 54);
                    int right = Math.Max(20, dialog.ClientSize.Width - 20);

                    importAllButton.Location = new Point(right - importAllButton.Width, buttonY);
                    importSelectedButton.Location = new Point(importAllButton.Left - 10 - importSelectedButton.Width, buttonY);

                    previewGrid.Width = Math.Max(360, dialog.ClientSize.Width - previewGrid.Left - 20);
                    selectionLabel.Location = new Point(previewGrid.Left, Math.Max(previewGrid.Top + 80, buttonY - 32));
                    selectionLabel.Size = new Size(previewGrid.Width, 24);

                    int gridBottom = selectionLabel.Top - 8;
                    int gridHeight = Math.Max(240, gridBottom - fileGrid.Top);
                    fileGrid.Height = gridHeight;
                    previewGrid.Height = gridHeight;
                };

                dialog.Resize += delegate { layoutPopupBottom(); };
                layoutPopupBottom();

                Action bindFiles = delegate
                {
                    string keyword = searchBox.Text == null ? "" : searchBox.Text.Trim();
                    fileGrid.SuspendLayout();

                    try
                    {
                        fileGrid.Rows.Clear();
                        int shown = 0;
                        int i;

                        for (i = 0; i < allFiles.Count; i++)
                        {
                            OtherBarListFileInfo info = allFiles[i];

                            if (keyword != "" && info.SearchText.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
                            {
                                continue;
                            }

                            // "No." is an OVIA display sequence, not ERP barlist_idx.
                            // The oldest discovered BarList is No.1 and the newest has the largest No.
                            // allFiles is already newest-first, so preserve the assigned sequence even when filtered.
                            int displayNo = allFiles.Count - i;

                            int rowIndex = fileGrid.Rows.Add(
                                displayNo,
                                info.ProjectDisplayName,
                                info.Title,
                                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                                info.RowCount.ToString("N0", CultureInfo.InvariantCulture)
                            );
                            fileGrid.Rows[rowIndex].Tag = info;
                            fileGrid.Rows[rowIndex].Cells[1].ToolTipText = info.ProjectDisplayName;
                            fileGrid.Rows[rowIndex].Cells[2].ToolTipText = info.Title + "\r\n" + info.FilePath;
                            shown++;
                        }

                        if (shown > 0)
                        {
                            fileGrid.ClearSelection();
                            fileGrid.Rows[0].Selected = true;
                            fileGrid.CurrentCell = fileGrid.Rows[0].Cells[0];
                        }
                        else
                        {
                            previewGrid.Rows.Clear();
                            currentPreviewRows = null;
                            currentFileInfo = null;
                            selectionLabel.Text = "검색 결과가 없습니다.";
                        }
                    }
                    finally
                    {
                        fileGrid.ResumeLayout();
                    }
                };

                Action loadPreview = delegate
                {
                    if (fileGrid.SelectedRows.Count == 0)
                    {
                        return;
                    }

                    OtherBarListFileInfo info = fileGrid.SelectedRows[0].Tag as OtherBarListFileInfo;

                    if (info == null || !File.Exists(info.FilePath))
                    {
                        return;
                    }

                    try
                    {
                        List<List<string>> rows = ReadCsv(info.FilePath);
                        rows = RemoveNonRebarRowsFromAutoCadCsv(rows);
                        NormalizeCadShapePathsInCsvRows(rows, info.FilePath);
                        currentPreviewRows = rows;
                        currentFileInfo = info;
                        BindOtherBarListPreview(previewGrid, rows);
                        UpdateOtherBarListPreviewSelectionSummary(previewGrid, selectionLabel);
                    }
                    catch (Exception ex)
                    {
                        currentPreviewRows = null;
                        currentFileInfo = null;
                        previewGrid.Rows.Clear();
                        selectionLabel.Text = "미리보기 오류 - " + ex.Message;
                    }
                };

                searchBox.TextChanged += delegate { bindFiles(); };
                fileGrid.SelectionChanged += delegate { loadPreview(); };
                previewGrid.SelectionChanged += delegate { UpdateOtherBarListPreviewSelectionSummary(previewGrid, selectionLabel); };

                importSelectedButton.Click += delegate
                {
                    if (currentFileInfo == null || currentPreviewRows == null)
                    {
                        MessageBox.Show("가져올 BarList를 먼저 선택해주세요.", "OVIA 다른 BarList", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    List<int> rows = GetSelectedOtherBarListSourceRows(previewGrid);

                    if (rows.Count == 0)
                    {
                        MessageBox.Show("가져올 행을 미리보기에서 선택해주세요.", "OVIA 다른 BarList", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    selectedFilePath = currentFileInfo.FilePath;
                    selectedSourceRows = rows;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };

                importAllButton.Click += delegate
                {
                    if (currentFileInfo == null || currentPreviewRows == null || currentPreviewRows.Count <= 1)
                    {
                        MessageBox.Show("가져올 BarList를 먼저 선택해주세요.", "OVIA 다른 BarList", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    selectedFilePath = currentFileInfo.FilePath;
                    selectedSourceRows = new List<int>();
                    int r;

                    for (r = 1; r < currentPreviewRows.Count; r++)
                    {
                        selectedSourceRows.Add(r);
                    }

                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };

                bindFiles();
                searchBox.Focus();

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            if (selectedFilePath == "" || selectedSourceRows.Count == 0)
            {
                return;
            }

            ApplyOtherBarListImport(selectedFilePath, selectedSourceRows);
        }

        private List<OtherBarListFileInfo> DiscoverOtherBarListFiles()
        {
            List<OtherBarListFileInfo> result = new List<OtherBarListFileInfo>();
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA",
                "Projects"
            );

            if (!Directory.Exists(root))
            {
                return result;
            }

            string currentSavedPath = NormalizeFilePathForCompare(savedProjectFilePath);
            string currentInitialPath = NormalizeFilePathForCompare(initialFilePath);
            string[] projectDirectories;

            try
            {
                projectDirectories = Directory.GetDirectories(root);
            }
            catch
            {
                return result;
            }

            int p;

            for (p = 0; p < projectDirectories.Length; p++)
            {
                string barListDirectory = Path.Combine(projectDirectories[p], "BarList");

                if (!Directory.Exists(barListDirectory))
                {
                    continue;
                }

                string[] files;

                try
                {
                    files = Directory.GetFiles(barListDirectory, "BarList_*.csv");
                }
                catch
                {
                    continue;
                }

                int f;

                for (f = 0; f < files.Length; f++)
                {
                    string normalized = NormalizeFilePathForCompare(files[f]);

                    if ((currentSavedPath != "" && String.Equals(normalized, currentSavedPath, StringComparison.OrdinalIgnoreCase))
                        || (currentInitialPath != "" && String.Equals(normalized, currentInitialPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    OtherBarListFileInfo info = BuildOtherBarListFileInfo(projectDirectories[p], files[f]);

                    if (info != null)
                    {
                        result.Add(info);
                    }
                }
            }

            result.Sort(delegate(OtherBarListFileInfo left, OtherBarListFileInfo right)
            {
                // "다른 BarList"의 표시 순서는 BarList의 입력/등록 시점을 기준으로 최신순입니다.
                // 화면의 No.는 ERP barlist_idx와 무관한 OVIA 표시 순번입니다.
                // SESSION cache는 파일 시간을 재생성하므로 등록일을 우선하고, 같은 등록일에서는
                // ERP id를 오직 안정적인 동률 해소용으로만 사용합니다.
                int registeredCompare = right.RegisteredDate.CompareTo(left.RegisteredDate);

                if (registeredCompare != 0)
                {
                    return registeredCompare;
                }

                int erpIdCompare = right.ErpBarListId.CompareTo(left.ErpBarListId);

                if (erpIdCompare != 0 && (left.ErpBarListId > 0 || right.ErpBarListId > 0))
                {
                    return erpIdCompare;
                }

                int timeCompare = right.LastWriteTime.CompareTo(left.LastWriteTime);

                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                return String.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase);
            });

            return result;
        }

        private OtherBarListFileInfo BuildOtherBarListFileInfo(string projectDirectory, string filePath)
        {
            try
            {
                OtherBarListFileInfo info = new OtherBarListFileInfo();
                info.FilePath = filePath;
                info.FileName = Path.GetFileNameWithoutExtension(filePath);
                string projectFolder = Path.GetFileName(projectDirectory);
                info.ProjectDisplayName = String.IsNullOrWhiteSpace(projectFolder) ? "공사" : projectFolder.Replace('_', ' ');
                info.LastWriteTime = File.GetLastWriteTime(filePath);
                info.Title = info.FileName;

                List<List<string>> rows = ReadCsv(filePath);
                rows = RemoveNonRebarRowsFromAutoCadCsv(rows);

                if (rows != null && rows.Count > 0 && rows[0] != null)
                {
                    info.RowCount = Math.Max(0, rows.Count - 1);
                    int titleColumn = FindCsvColumnIndex(rows[0], "제목", "BarList 제목", "바리스트 제목");
                    int orderColumn = FindCsvColumnIndex(rows[0], "발주번호", "발주 번호");
                    int dueColumn = FindCsvColumnIndex(rows[0], "납기일", "납기 일자", "납기일자");
                    int authorColumn = FindCsvColumnIndex(rows[0], "작성자", "작성");
                    int erpIdColumn = FindCsvColumnIndex(rows[0], "OVIA_ERP_BARLIST_IDX");
                    int registeredColumn = FindCsvColumnIndex(rows[0], "등록일", "등록 일자", "등록일자");
                    int r;

                    for (r = 1; r < rows.Count; r++)
                    {
                        if (titleColumn >= 0 && info.Title == info.FileName)
                        {
                            string title = GetCsvCellText(rows[r], titleColumn);

                            if (title != "")
                            {
                                info.Title = title;
                            }
                        }

                        if (orderColumn >= 0 && info.OrderNumber == "")
                        {
                            info.OrderNumber = GetCsvCellText(rows[r], orderColumn);
                        }

                        if (dueColumn >= 0 && info.DueDate == "")
                        {
                            info.DueDate = GetCsvCellText(rows[r], dueColumn);
                        }

                        if (authorColumn >= 0 && info.Author == "")
                        {
                            info.Author = GetCsvCellText(rows[r], authorColumn);
                        }

                        if (erpIdColumn >= 0 && info.ErpBarListId <= 0)
                        {
                            int erpId;
                            if (Int32.TryParse(GetCsvCellText(rows[r], erpIdColumn), NumberStyles.Integer, CultureInfo.InvariantCulture, out erpId))
                            {
                                info.ErpBarListId = erpId;
                            }
                        }

                        if (registeredColumn >= 0 && info.RegisteredDate == DateTime.MinValue)
                        {
                            info.RegisteredDate = ParseOtherBarListRegisteredDate(GetCsvCellText(rows[r], registeredColumn), info.LastWriteTime);
                        }
                    }
                }

                info.SearchText = String.Join(" ", new string[]
                {
                    info.ProjectDisplayName,
                    info.Title,
                    info.FileName,
                    info.OrderNumber,
                    info.DueDate,
                    info.Author,
                    info.LastWriteTime.ToString("yyyy-MM-dd")
                });
                return info;
            }
            catch
            {
                return null;
            }
        }

        private DateTime ParseOtherBarListRegisteredDate(string value, DateTime fallbackReference)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            string text = value.Trim();
            DateTime parsed;
            string[] fullFormats = new string[]
            {
                "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
                "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm", "yyyy.MM.dd HH:mm"
            };

            if (DateTime.TryParseExact(text, fullFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            string[] shortFormats = new string[] { "MM-dd", "MM/dd", "MM.dd" };
            DateTime shortDate;
            if (DateTime.TryParseExact(text, shortFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out shortDate))
            {
                int year = fallbackReference == DateTime.MinValue ? DateTime.Now.Year : fallbackReference.Year;
                try
                {
                    return new DateTime(year, shortDate.Month, shortDate.Day);
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }

            return DateTime.MinValue;
        }

        private string NormalizeFilePathForCompare(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private void AddOtherBarListPreviewColumns(DataGridView previewGrid)
        {
            string[] headers = new string[]
            {
                "부위", "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)", "비고", "원본 도면"
            };
            int[] widths = new int[] { 58, 50, 74, 148, 82, 74, 82, 82, 105, 145 };
            int i;

            for (i = 0; i < headers.Length; i++)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = "Preview" + i.ToString(CultureInfo.InvariantCulture);
                column.HeaderText = headers[i];
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = widths[i];
                column.MinimumWidth = widths[i];
                column.SortMode = DataGridViewColumnSortMode.Automatic;
                column.DefaultCellStyle.Alignment = GetBarListCellAlignment(headers[i]);
                previewGrid.Columns.Add(column);
            }
        }

        private void BindOtherBarListPreview(DataGridView previewGrid, List<List<string>> rows)
        {
            previewGrid.SuspendLayout();

            try
            {
                previewGrid.Rows.Clear();

                if (rows == null || rows.Count <= 1 || rows[0] == null)
                {
                    return;
                }

                List<string> headers = rows[0];
                int noColumn = FindCsvColumnIndex(headers, "번호", "MARK", "MARKNO", "BARNO");
                int partColumn = FindCsvColumnIndex(headers, "부위", "위치", "구간");
                int specColumn = FindCsvColumnIndex(headers, "철근규격", "규격", "DIA");
                int shapeColumn = FindCsvColumnIndex(headers, "철근형상", "형상", "SHAPE", "BENT");
                int shapeDimensionColumn = FindCsvColumnIndex(headers, "OVIA_형상치수", "형상치수", "OVIA_SHAPE_TEXTS", "OVIA_CAD_SHAPE_TEXTS");
                int cadShapeJsonColumn = FindCsvColumnIndex(headers, "OVIA_CAD_SHAPE_JSON", "CAD_SHAPE_JSON", "OVIA CAD SHAPE JSON");
                int shapeSourceColumn = FindCsvColumnIndex(headers, "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE");
                int shapeStatusColumn = FindCsvColumnIndex(headers, "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS");
                int lengthColumn = FindCsvColumnIndex(headers, "길이(mm)", "길이MM", "길이", "LENGTH");
                int qtyColumn = FindCsvColumnIndex(headers, "수량(EA)", "수량EA", "수량", "QTY", "QUANTITY");
                int totalLengthColumn = FindCsvColumnIndex(headers, "총길이(M)", "총길이M", "총길이", "TOTAL LENGTH");
                int weightColumn = FindCsvColumnIndex(headers, "중량(Ton)", "중량TON", "총중량(Ton)", "중량", "TOTAL WEIGHT");
                int noteColumn = FindCsvColumnIndex(headers, "비고", "NOTE", "REMARK");
                int drawingColumn = FindCsvColumnIndex(headers, "원본 도면", "원본도면", "SOURCE DRAWING", "SOURCE DRAWING NAME");
                int r;

                for (r = 1; r < rows.Count; r++)
                {
                    List<string> row = rows[r];
                    string rawShapeText = GetCsvCellText(row, shapeColumn);
                    string dimensionText = GetCsvCellText(row, shapeDimensionColumn);
                    string shapeText = dimensionText;

                    if (shapeText == "")
                    {
                        shapeText = rawShapeText;
                    }

                    int previewRowIndex = previewGrid.Rows.Add(
                        GetCsvCellText(row, partColumn),
                        GetCsvCellText(row, noColumn),
                        GetCsvCellText(row, specColumn),
                        shapeText,
                        FormatBarListNumberForDisplay(GetCsvCellText(row, lengthColumn)),
                        FormatBarListNumberForDisplay(GetCsvCellText(row, qtyColumn)),
                        FormatBarListTotalLengthForDisplay(GetCsvCellText(row, totalLengthColumn)),
                        FormatBarListNumberForDisplay(GetCsvCellText(row, weightColumn)),
                        GetCsvCellText(row, noteColumn),
                        GetCsvCellText(row, drawingColumn)
                    );
                    DataGridViewRow previewRow = previewGrid.Rows[previewRowIndex];
                    previewRow.Tag = r;
                    // Match the main BarList row-header rule exactly: top row has the largest No.
                    // Example: 20 rows => 20, 19, ... 1.
                    previewRow.HeaderCell.Value = (rows.Count - 1 - previewRowIndex);

                    OtherBarListPreviewShapeInfo shapeInfo = new OtherBarListPreviewShapeInfo();
                    shapeInfo.RawShapeText = rawShapeText;
                    shapeInfo.DimensionText = dimensionText;
                    shapeInfo.CadShapeJsonPath = GetCsvCellText(row, cadShapeJsonColumn);
                    shapeInfo.ShapeSource = GetCsvCellText(row, shapeSourceColumn);
                    shapeInfo.ShapeStatus = GetCsvCellText(row, shapeStatusColumn);
                    previewRow.Cells[3].Tag = shapeInfo;

                    if (shapeInfo.CadShapeJsonPath != "")
                    {
                        previewRow.Height = cadShapeRenderer.GetRecommendedRowHeight(shapeInfo.CadShapeJsonPath, 52, 76);
                    }
                    else
                    {
                        previewRow.Height = 52;
                    }

                    if (drawingColumn >= 0)
                    {
                        previewRow.Cells[9].ToolTipText = GetCsvCellText(row, drawingColumn);
                    }
                }

                if (previewGrid.Rows.Count > 0)
                {
                    previewGrid.ClearSelection();
                    previewGrid.Rows[0].Selected = true;
                    previewGrid.CurrentCell = previewGrid.Rows[0].Cells[0];
                }
            }
            finally
            {
                previewGrid.ResumeLayout();
            }
        }

        private void OtherBarListPreviewGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            DataGridView previewGrid = sender as DataGridView;

            if (previewGrid == null || e.RowIndex < 0 || e.RowIndex >= previewGrid.Rows.Count)
            {
                return;
            }

            if (e.ColumnIndex == -1)
            {
                bool rowSelected = previewGrid.Rows[e.RowIndex].Selected;
                e.PaintBackground(e.CellBounds, rowSelected);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

                object headerValue = previewGrid.Rows[e.RowIndex].HeaderCell.Value;
                string rowNumber = headerValue == null
                    ? (previewGrid.Rows.Count - e.RowIndex).ToString(CultureInfo.InvariantCulture)
                    : Convert.ToString(headerValue, CultureInfo.InvariantCulture);

                TextRenderer.DrawText(
                    e.Graphics,
                    rowNumber,
                    previewGrid.RowHeadersDefaultCellStyle.Font ?? previewGrid.Font,
                    e.CellBounds,
                    TextDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                );
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex != 3)
            {
                return;
            }

            DataGridViewCell cell = previewGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            OtherBarListPreviewShapeInfo shapeInfo = cell.Tag as OtherBarListPreviewShapeInfo;

            if (shapeInfo == null)
            {
                return;
            }

            bool selected = cell.Selected || previewGrid.Rows[e.RowIndex].Selected;
            Color backColor = selected
                ? Color.FromArgb(255, 248, 205)
                : (e.RowIndex % 2 == 1 ? previewGrid.AlternatingRowsDefaultCellStyle.BackColor : previewGrid.DefaultCellStyle.BackColor);

            if (backColor == Color.Empty || backColor == Color.Transparent)
            {
                backColor = Color.White;
            }

            e.Handled = true;

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }

            bool cadSource = shapeInfo.ShapeSource != null
                && shapeInfo.ShapeSource.Trim().Equals("CAD", StringComparison.OrdinalIgnoreCase);
            bool manualEdited = shapeInfo.ShapeSource != null
                && shapeInfo.ShapeSource.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase)
                && shapeInfo.ShapeStatus != null
                && shapeInfo.ShapeStatus.Trim().Equals("MANUAL_EDITED", StringComparison.OrdinalIgnoreCase);
            bool cadTextEdited = shapeInfo.ShapeStatus != null
                && shapeInfo.ShapeStatus.Trim().Equals("CAD_EDITED", StringComparison.OrdinalIgnoreCase);

            if ((cadSource || manualEdited || !String.IsNullOrWhiteSpace(shapeInfo.CadShapeJsonPath))
                && !String.IsNullOrWhiteSpace(shapeInfo.CadShapeJsonPath))
            {
                cadShapeRenderer.DrawCadShape(
                    e.Graphics,
                    e.CellBounds,
                    shapeInfo.CadShapeJsonPath,
                    selected,
                    shapeInfo.DimensionText,
                    cadTextEdited,
                    1F
                );
            }
            else
            {
                RebarShapeInfo shape = GetShapeRepository().FindByRawValue(shapeInfo.RawShapeText);
                shapeRenderer.DrawShape(e.Graphics, e.CellBounds, shape, shapeInfo.RawShapeText, selected, shapeInfo.DimensionText);
            }

            using (Pen pen = new Pen(previewGrid.GridColor, 1F))
            {
                e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            }
        }

        private List<int> GetSelectedOtherBarListSourceRows(DataGridView previewGrid)
        {
            List<int> rows = new List<int>();

            if (previewGrid == null)
            {
                return rows;
            }

            int i;

            for (i = 0; i < previewGrid.SelectedRows.Count; i++)
            {
                object tag = previewGrid.SelectedRows[i].Tag;
                int sourceRow;

                if (tag != null && Int32.TryParse(tag.ToString(), out sourceRow) && !rows.Contains(sourceRow))
                {
                    rows.Add(sourceRow);
                }
            }

            rows.Sort();
            return rows;
        }

        private void UpdateOtherBarListPreviewSelectionSummary(DataGridView previewGrid, Label label)
        {
            if (previewGrid == null || label == null)
            {
                return;
            }

            List<DataGridViewRow> selectedRows = new List<DataGridViewRow>();
            int i;

            for (i = 0; i < previewGrid.SelectedRows.Count; i++)
            {
                if (!previewGrid.SelectedRows[i].IsNewRow)
                {
                    selectedRows.Add(previewGrid.SelectedRows[i]);
                }
            }

            if (selectedRows.Count == 0)
            {
                label.Text = "선택 0행";
                return;
            }

            double qty = 0.0;
            double length = 0.0;
            decimal weight = 0M;

            for (i = 0; i < selectedRows.Count; i++)
            {
                qty += ParseNumber(Convert.ToString(selectedRows[i].Cells[5].Value, CultureInfo.InvariantCulture));
                length += ParseNumber(Convert.ToString(selectedRows[i].Cells[6].Value, CultureInfo.InvariantCulture));
                decimal rowWeight;

                if (TryParseDecimalNumber(Convert.ToString(selectedRows[i].Cells[7].Value, CultureInfo.InvariantCulture), out rowWeight))
                {
                    weight += rowWeight;
                }
            }

            label.Text = "선택 " + selectedRows.Count.ToString("N0", CultureInfo.InvariantCulture)
                + "행   |   수량 " + qty.ToString("#,0.###", CultureInfo.InvariantCulture)
                + " EA   |   총길이 " + length.ToString("#,0.00", CultureInfo.InvariantCulture)
                + " M   |   중량 " + weight.ToString("#,0.###", CultureInfo.InvariantCulture) + " Ton";
        }

        private bool ApplyOtherBarListImport(string filePath, List<int> selectedSourceRows)
        {
            if (!CanImportIntoCurrentBarList() || String.IsNullOrWhiteSpace(filePath) || selectedSourceRows == null || selectedSourceRows.Count == 0)
            {
                return false;
            }

            try
            {
                List<List<string>> rows = ReadCsv(filePath);
                rows = RemoveNonRebarRowsFromAutoCadCsv(rows);
                NormalizeCadShapePathsInCsvRows(rows, filePath);

                if (rows == null || rows.Count <= 1 || rows[0] == null)
                {
                    MessageBox.Show("선택한 BarList에 가져올 데이터가 없습니다.", "OVIA 다른 BarList", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                List<List<string>> selectedRows = new List<List<string>>();
                selectedRows.Add(new List<string>(rows[0]));
                int i;

                for (i = 0; i < selectedSourceRows.Count; i++)
                {
                    int sourceIndex = selectedSourceRows[i];

                    if (sourceIndex <= 0 || sourceIndex >= rows.Count || rows[sourceIndex] == null)
                    {
                        continue;
                    }

                    selectedRows.Add(new List<string>(rows[sourceIndex]));
                }

                if (selectedRows.Count <= 1)
                {
                    MessageBox.Show("선택한 행을 읽지 못했습니다.", "OVIA 다른 BarList", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (hasActiveSummaryFilter)
                {
                    hasActiveSummaryFilter = false;
                    activeSummaryFilterValue = "";
                    UpdateSummaryFilterChip();
                }

                int importedCount;

                if (!HasGridData())
                {
                    BindCsvRows(selectedRows);
                    importedCount = selectedRows.Count - 1;
                    ReindexLogicalRowOrder();
                    HighlightOtherBarListImportedRows(0, grid.Rows.Count - 1);
                }
                else
                {
                    importedCount = AppendCsvRows(selectedRows, true);
                }

                if (importedCount <= 0)
                {
                    lblStatus.Text = "다른 BarList에서 추가된 행이 없습니다.";
                    lblStatus.ForeColor = TextSub;
                    return true;
                }

                rebarMismatchWarningShown = false;
                ApplyRebarCalculationAndValidation(true);
                allowExtractEditMenu = true;
                ClearUndoRedoStates();
                RecalculateSummary();
                ApplyActiveSummaryFilter();
                MarkUnsaved();
                lblStatus.Text = "다른 BarList에서 " + importedCount.ToString("N0", CultureInfo.InvariantCulture) + "개 행을 현재 목록 뒤에 추가했습니다.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                OviaNotificationStore.AddWorkLog(companyId, userId, "다른 BarList 가져오기", "메인  ›  공사관리  ›  공사별 BarList  ›  BarList");
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "다른 BarList 가져오기 오류 - " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                MessageBox.Show(
                    "다른 BarList를 가져오는 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 다른 BarList",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

        private void HighlightOtherBarListImportedRows(int startRowIndex, int endRowIndex)
        {
            if (grid == null || grid.IsDisposed || grid.Rows.Count == 0)
            {
                return;
            }

            int start = Math.Max(0, startRowIndex);
            int end = Math.Min(grid.Rows.Count - 1, endRowIndex);
            int rowIndex;

            for (rowIndex = start; rowIndex <= end; rowIndex++)
            {
                if (!grid.Rows[rowIndex].IsNewRow)
                {
                    ApplyOtherBarListImportedRowStyle(grid.Rows[rowIndex]);
                }
            }
        }

        private void ApplyOtherBarListImportedRowStyle(DataGridViewRow row)
        {
            if (row == null)
            {
                return;
            }

            row.DefaultCellStyle.BackColor = OviaFluentTheme.SuccessLight;
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

            ApplyRebarCalculationAndValidation(false);
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

            if (IsCalculatedResultColumn(e.ColumnIndex) && !IsRebarCalculationMismatchCell(e.RowIndex, e.ColumnIndex))
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
            UpdateImportedTotalMetaFromUserEdit(e.RowIndex, e.ColumnIndex);
            ApplyRebarCalculationAndValidation(false);
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

            if (IsCalculatedResultColumn(e.ColumnIndex) && !IsRebarCalculationMismatchCell(e.RowIndex, e.ColumnIndex))
            {
                MessageBox.Show(
                    "총길이와 총중량은 OVIA 이형철근 단위중량표 기준으로 자동 계산됩니다.\r\n\r\nCAD 원본값과 OVIA 계산값이 다른 옅은 빨강색 셀만 직접 수정할 수 있습니다.",
                    "OVIA 자동 계산",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
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
                    columnHeaderDragOccurred = true;
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
            columnHeaderDragOccurred = false;
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
            if (e.Button != MouseButtons.Left || grid == null || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            // 여러 헤더를 드래그한 경우에는 기존 세로 범위 선택만 유지합니다.
            if (columnHeaderDragOccurred)
            {
                columnHeaderDragOccurred = false;
                return;
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];

            if (column.SortMode != DataGridViewColumnSortMode.Programmatic)
            {
                return;
            }

            if (String.Equals(gridSortColumnName, column.Name, StringComparison.OrdinalIgnoreCase))
            {
                gridSortAscending = !gridSortAscending;
            }
            else
            {
                gridSortColumnName = column.Name;
                gridSortAscending = true;
            }

            ApplyBarListGridSort(column);
        }

        private void ApplyBarListGridSort(DataGridViewColumn column)
        {
            if (grid == null || column == null || column.SortMode != DataGridViewColumnSortMode.Programmatic)
            {
                return;
            }

            BeginGridSelectionUpdate();
            grid.SuspendLayout();

            try
            {
                System.ComponentModel.ListSortDirection direction = gridSortAscending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending;

                grid.Sort(column, direction);
                UpdateBarListGridSortGlyph();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[OVIA] BarList 헤더 정렬 실패: " + ex);

                if (lblStatus != null)
                {
                    lblStatus.Text = "선택한 헤더를 정렬하지 못했습니다.";
                }
            }
            finally
            {
                grid.ResumeLayout();
                EndGridSelectionUpdate();
                grid.Invalidate();
            }
        }

        private void Grid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (grid == null || e.Column == null || e.RowIndex1 < 0 || e.RowIndex2 < 0)
            {
                return;
            }

            string leftText = GetBarListGridSortText(e.RowIndex1, e.Column.Index);
            string rightText = GetBarListGridSortText(e.RowIndex2, e.Column.Index);
            e.SortResult = CompareBarListGridSortText(leftText, rightText);
            e.Handled = true;
        }

        private string GetBarListGridSortText(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            string value = GetCellText(rowIndex, columnIndex).Trim();

            if (!IsRebarShapeHeader(grid.Columns[columnIndex].HeaderText))
            {
                return value;
            }

            // CAD 형상은 화면 셀 값이 비어 있을 수 있으므로 관련 메타데이터를 함께 비교합니다.
            return value
                + "|" + GetShapeNumberText(rowIndex)
                + "|" + GetShapeDimensionText(rowIndex)
                + "|" + GetShapeSourceText(rowIndex)
                + "|" + GetCadShapeJsonText(rowIndex);
        }

        private int CompareBarListGridSortText(string leftText, string rightText)
        {
            leftText = leftText == null ? "" : leftText.Trim();
            rightText = rightText == null ? "" : rightText.Trim();

            if (leftText.Length == 0 && rightText.Length == 0)
            {
                return 0;
            }

            if (leftText.Length == 0)
            {
                return 1;
            }

            if (rightText.Length == 0)
            {
                return -1;
            }

            double leftNumber;
            double rightNumber;

            if (TryParseNumber(leftText, out leftNumber) && TryParseNumber(rightText, out rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return String.Compare(leftText, rightText, StringComparison.CurrentCultureIgnoreCase);
        }

        private bool IsBarListGridSortableHeader(string header)
        {
            if (String.IsNullOrWhiteSpace(header) || IsInternalOviaColumn(header))
            {
                return false;
            }

            return header.Trim().IndexOf("비고", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void ReapplyBarListGridSortIfNeeded()
        {
            if (grid == null || String.IsNullOrWhiteSpace(gridSortColumnName) || !grid.Columns.Contains(gridSortColumnName))
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[gridSortColumnName];

            if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
            {
                ApplyBarListGridSort(column);
            }
        }

        private void ResetBarListGridSortState()
        {
            gridSortColumnName = "";
            gridSortAscending = true;
            UpdateBarListGridSortGlyph();
        }

        private void UpdateBarListGridSortGlyph()
        {
            if (grid == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                grid.Columns[i].HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            if (!String.IsNullOrWhiteSpace(gridSortColumnName) && grid.Columns.Contains(gridSortColumnName))
            {
                DataGridViewColumn column = grid.Columns[gridSortColumnName];

                if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
                {
                    column.HeaderCell.SortGlyphDirection = gridSortAscending ? SortOrder.Ascending : SortOrder.Descending;
                }
            }
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (grid != null && grid.IsCurrentCellInEditMode
                && e.Control && !e.Shift
                && (e.KeyCode == Keys.C || e.KeyCode == Keys.V))
            {
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.C)
            {
                CopySelectedCellsToClipboard();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.V)
            {
                PasteCopiedCells();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

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
            UpdateSelectionSummaryOverlay();
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

            string header = grid.Columns[e.ColumnIndex].HeaderText;

            if (IsTotalLengthDisplayHeader(header) && e.Value != null)
            {
                string formatted = FormatBarListTotalLengthForDisplay(e.Value.ToString());

                if (formatted != "")
                {
                    e.Value = formatted;
                    e.FormattingApplied = true;
                }
            }
            else if (IsBarListNumericDisplayHeader(header) && e.Value != null)
            {
                string formatted = FormatBarListNumberForDisplay(e.Value.ToString());

                if (formatted != "")
                {
                    e.Value = formatted;
                    e.FormattingApplied = true;
                }
            }
        }

        private void Grid_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            // ERP BarList의 No. 표시와 동일하게 큰 번호가 위에 오도록 표시합니다.
            // 예: 50행이면 50, 49, 48 ... 1
            string rowNumber = (grid.Rows.Count - e.RowIndex).ToString(CultureInfo.InvariantCulture);
            Rectangle headerBounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, grid.RowHeadersWidth, e.RowBounds.Height);
            bool rowSelected = IsRowFullySelected(e.RowIndex);
            Color headerBack = rowSelected ? Color.FromArgb(255, 235, 112) : OviaFluentTheme.HeaderBackground;
            Color headerFore = rowSelected ? TextDark : TextSub;

            using (SolidBrush brush = new SolidBrush(headerBack))
            {
                e.Graphics.FillRectangle(brush, headerBounds);
            }

            Color headerBorderColor = rowSelected ? Color.FromArgb(188, 136, 0) : OviaFluentTheme.CardBorder;
            int headerRight = headerBounds.Right - 1;
            int headerBottom = headerBounds.Bottom - 1;

            using (Pen pen = new Pen(headerBorderColor, 1F))
            {
                // 인접 행의 위·아래 테두리가 같은 위치에 중복으로 그려지지 않도록
                // 행 헤더는 오른쪽선과 아래쪽 가로선만 각각 한 번 그립니다.
                e.Graphics.DrawLine(pen, headerRight, headerBounds.Top, headerRight, headerBottom);
                e.Graphics.DrawLine(pen, headerBounds.Left, headerBottom, headerRight, headerBottom);
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

            if (IsRebarCalculationMismatchCell(e.RowIndex, e.ColumnIndex))
            {
                PaintRebarCalculationMismatchCell(e);
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
            PaintGridCellBorder(e.Graphics, e.CellBounds);
        }

        private void PaintGridCellBase(DataGridViewCellPaintingEventArgs e, bool selected)
        {
            Color backColor = selected ? Color.FromArgb(255, 248, 205) : Color.White;

            if (!selected && grid != null && e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count)
            {
                Color rowBackColor = grid.Rows[e.RowIndex].DefaultCellStyle.BackColor;

                if (rowBackColor != Color.Empty && rowBackColor != Color.Transparent)
                {
                    backColor = rowBackColor;
                }
            }

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }
        }

        private void PaintGridCellBorder(Graphics graphics, Rectangle bounds)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            Color borderColor = grid == null ? OviaFluentTheme.GridLine : grid.GridColor;
            int bottom = bounds.Bottom - 1;
            int right = bounds.Right - 1;

            using (Pen pen = new Pen(borderColor, 1F))
            {
                // Grid의 SingleHorizontal 기본 규칙과 동일하게 아래쪽 가로선만 1px로 그립니다.
                // 셀마다 사각형을 그리면 위 셀의 하단선과 아래 셀의 상단선이 겹쳐 2px처럼 보입니다.
                graphics.DrawLine(pen, bounds.Left, bottom, right, bottom);
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

            using (Pen pen = new Pen(Color.FromArgb(188, 136, 0), 1F))
            {
                // 선택 헤더도 기본 그리드 선과 동일한 1px 두께를 사용합니다.
                e.Graphics.DrawRectangle(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 1, e.CellBounds.Height - 1);
            }

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            string headerText = column.HeaderText == null ? "" : column.HeaderText;
            Font font = grid.ColumnHeadersDefaultCellStyle.Font;
            SortOrder sortOrder = column.HeaderCell.SortGlyphDirection;
            bool showSortArrow = sortOrder != SortOrder.None;
            Size textSize = TextRenderer.MeasureText(e.Graphics, headerText, font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.NoPadding);
            int arrowWidth = showSortArrow ? 9 : 0;
            int totalWidth = Math.Min(e.CellBounds.Width - 8, textSize.Width + arrowWidth + (showSortArrow ? 4 : 0));
            int startX = e.CellBounds.Left + Math.Max(4, (e.CellBounds.Width - totalWidth) / 2);
            Rectangle textBounds = new Rectangle(startX, e.CellBounds.Top, Math.Max(1, Math.Min(textSize.Width + 2, e.CellBounds.Right - startX - 4)), e.CellBounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                headerText,
                font,
                textBounds,
                TextDark,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            );

            if (showSortArrow)
            {
                int arrowX = Math.Min(e.CellBounds.Right - 11, startX + textSize.Width + 4);
                int arrowY = e.CellBounds.Top + (e.CellBounds.Height / 2) - 3;
                DrawBarListGridSortArrow(e.Graphics, arrowX, arrowY, sortOrder);
            }
        }

        private void DrawBarListGridSortArrow(Graphics graphics, int x, int y, SortOrder sortOrder)
        {
            Point[] points = sortOrder == SortOrder.Ascending
                ? new Point[] { new Point(x, y + 6), new Point(x + 4, y), new Point(x + 8, y + 6) }
                : new Point[] { new Point(x, y), new Point(x + 4, y + 6), new Point(x + 8, y) };

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(105, 109, 118)))
            {
                graphics.FillPolygon(brush, points);
            }
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
            RefreshClipboardMenuState();
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


        private void RefreshClipboardMenuState()
        {
            if (rowCopyMenuItem != null)
            {
                rowCopyMenuItem.Enabled = CanUseExtractEditMenu();
            }

            if (rowPasteMenuItem != null)
            {
                rowPasteMenuItem.Enabled = CanUseExtractEditMenu()
                    && rowClipboardRows != null
                    && rowClipboardRows.Count > 0
                    && String.Equals(rowClipboardSchemaKey, BuildClipboardSchemaKey(), StringComparison.Ordinal);
            }
        }

        private void CopySelectedCellsToClipboard()
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            List<DataGridViewCell> selectedCells = GetClipboardSelectedCells();

            if (selectedCells.Count == 0)
            {
                MessageBox.Show(
                    "복사할 셀을 먼저 선택해주세요.",
                    "OVIA 셀 복사",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            bool systemClipboardCopied = TrySetSystemClipboardText(BuildSystemClipboardText(selectedCells));
            int sourceColumnIndex = selectedCells[0].ColumnIndex;
            bool sameColumn = true;
            int i;

            for (i = 1; i < selectedCells.Count; i++)
            {
                if (selectedCells[i].ColumnIndex != sourceColumnIndex)
                {
                    sameColumn = false;
                    break;
                }
            }

            if (!sameColumn)
            {
                cellClipboardData = null;
                RefreshClipboardMenuState();

                lblStatus.Text = systemClipboardCopied
                    ? selectedCells.Count.ToString() + "개 셀 영역을 복사했습니다. Excel이나 텍스트에 붙여넣을 수 있습니다."
                    : "선택 영역을 Windows 클립보드에 복사하지 못했습니다.";
                lblStatus.ForeColor = TextSub;
                return;
            }

            BarListCellClipboardData clipboard = new BarListCellClipboardData();
            clipboard.SourceColumnIndex = sourceColumnIndex;
            clipboard.SourceColumnKey = GetClipboardColumnKey(sourceColumnIndex);
            clipboard.SourceColumnTitle = GetClipboardColumnTitle(sourceColumnIndex);
            clipboard.SchemaKey = BuildClipboardSchemaKey();

            List<int> copiedColumnIndexes = GetCellClipboardColumnIndexes(sourceColumnIndex);

            for (i = 0; i < selectedCells.Count; i++)
            {
                DataGridViewCell sourceCell = selectedCells[i];
                BarListCellClipboardEntry entry = new BarListCellClipboardEntry();
                entry.SourceRowIndex = sourceCell.RowIndex;

                int c;

                for (c = 0; c < copiedColumnIndexes.Count; c++)
                {
                    int columnIndex = copiedColumnIndexes[c];
                    object value = grid.Rows[sourceCell.RowIndex].Cells[columnIndex].Value;
                    entry.ValuesByColumn[columnIndex] = value == null ? "" : value.ToString();
                }

                clipboard.Entries.Add(entry);
            }

            cellClipboardData = clipboard;
            RefreshClipboardMenuState();

            if (!systemClipboardCopied)
            {
                lblStatus.Text = "OVIA 내부 셀 복사는 완료했지만 Windows 클립보드에는 복사하지 못했습니다.";
            }
            else if (selectedCells.Count == 1)
            {
                lblStatus.Text = "[" + clipboard.SourceColumnTitle + "] 셀을 복사했습니다. OVIA Ctrl+V와 Excel/텍스트 붙여넣기를 모두 사용할 수 있습니다.";
            }
            else
            {
                lblStatus.Text = "[" + clipboard.SourceColumnTitle + "] 열의 " + selectedCells.Count.ToString()
                    + "개 셀을 복사했습니다. OVIA Ctrl+V와 Excel/텍스트 붙여넣기를 모두 사용할 수 있습니다.";
            }

            lblStatus.ForeColor = TextSub;
        }

        private void PasteCopiedCells()
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            if (cellClipboardData == null || cellClipboardData.Entries.Count == 0)
            {
                MessageBox.Show(
                    "OVIA에서 복사한 셀이 없습니다.\r\n\r\n먼저 같은 BarList에서 셀을 선택한 뒤 Ctrl+C를 눌러주세요.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            if (!String.Equals(cellClipboardData.SchemaKey, BuildClipboardSchemaKey(), StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "복사한 셀과 현재 BarList의 열 구성이 달라 붙여넣을 수 없습니다.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            List<DataGridViewCell> targetCells = GetClipboardSelectedCells();

            if (targetCells.Count == 0)
            {
                MessageBox.Show(
                    "붙여넣을 대상 셀을 먼저 선택해주세요.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            int targetColumnIndex = targetCells[0].ColumnIndex;
            int i;

            for (i = 1; i < targetCells.Count; i++)
            {
                if (targetCells[i].ColumnIndex != targetColumnIndex)
                {
                    MessageBox.Show(
                        "여러 종류의 열에는 한 번에 붙여넣을 수 없습니다.\r\n\r\n같은 세로 열의 대상 셀만 선택해주세요.",
                        "OVIA 셀 붙여넣기",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }
            }

            string targetColumnKey = GetClipboardColumnKey(targetColumnIndex);

            if (cellClipboardData.SourceColumnIndex != targetColumnIndex
                || !String.Equals(cellClipboardData.SourceColumnKey, targetColumnKey, StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "복사한 [" + cellClipboardData.SourceColumnTitle + "] 셀은 ["
                    + GetClipboardColumnTitle(targetColumnIndex)
                    + "] 열에 붙여넣을 수 없습니다.\r\n\r\n같은 세로 열의 셀을 선택해주세요.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (grid.Columns[targetColumnIndex].ReadOnly || IsCalculatedResultColumn(targetColumnIndex))
            {
                MessageBox.Show(
                    "[" + GetClipboardColumnTitle(targetColumnIndex)
                    + "] 열은 자동 계산 또는 읽기 전용 열이므로 붙여넣을 수 없습니다.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (cellClipboardData.Entries.Count != 1
                && cellClipboardData.Entries.Count != targetCells.Count)
            {
                MessageBox.Show(
                    "복사한 셀 수와 붙여넣을 대상 셀 수가 다릅니다.\r\n\r\n"
                    + "셀 하나를 복사하면 같은 열의 여러 셀에 일괄 붙여넣을 수 있습니다.\r\n"
                    + "여러 셀을 복사한 경우에는 같은 수의 대상 셀을 선택해주세요.",
                    "OVIA 셀 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            PushUndoState(CaptureGridState());

            for (i = 0; i < targetCells.Count; i++)
            {
                BarListCellClipboardEntry entry = cellClipboardData.Entries.Count == 1
                    ? cellClipboardData.Entries[0]
                    : cellClipboardData.Entries[i];

                ApplyCellClipboardEntry(entry, targetCells[i].RowIndex, targetColumnIndex);
            }

            ApplyRebarCalculationAndValidation(false);
            MarkUnsaved();
            RecalculateSummary();
            grid.Invalidate();

            lblStatus.Text = "[" + cellClipboardData.SourceColumnTitle + "] 값을 "
                + targetCells.Count.ToString() + "개 셀에 붙여넣었습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private List<DataGridViewCell> GetClipboardSelectedCells()
        {
            List<DataGridViewCell> cells = new List<DataGridViewCell>();

            if (grid == null)
            {
                return cells;
            }

            int i;

            for (i = 0; i < grid.SelectedCells.Count; i++)
            {
                DataGridViewCell cell = grid.SelectedCells[i];

                if (cell == null
                    || cell.RowIndex < 0
                    || cell.ColumnIndex < 0
                    || cell.RowIndex >= grid.Rows.Count
                    || cell.ColumnIndex >= grid.Columns.Count
                    || grid.Rows[cell.RowIndex].IsNewRow
                    || !grid.Columns[cell.ColumnIndex].Visible)
                {
                    continue;
                }

                cells.Add(cell);
            }

            if (cells.Count == 0 && grid.CurrentCell != null
                && grid.CurrentCell.RowIndex >= 0
                && grid.CurrentCell.ColumnIndex >= 0
                && grid.CurrentCell.RowIndex < grid.Rows.Count
                && grid.CurrentCell.ColumnIndex < grid.Columns.Count
                && !grid.Rows[grid.CurrentCell.RowIndex].IsNewRow
                && grid.Columns[grid.CurrentCell.ColumnIndex].Visible)
            {
                cells.Add(grid.CurrentCell);
            }

            cells.Sort(delegate(DataGridViewCell left, DataGridViewCell right)
            {
                int rowCompare = left.RowIndex.CompareTo(right.RowIndex);

                if (rowCompare != 0)
                {
                    return rowCompare;
                }

                return left.ColumnIndex.CompareTo(right.ColumnIndex);
            });

            return cells;
        }

        private List<int> GetCellClipboardColumnIndexes(int sourceColumnIndex)
        {
            List<int> indexes = new List<int>();

            if (sourceColumnIndex < 0 || sourceColumnIndex >= grid.Columns.Count)
            {
                return indexes;
            }

            if (!IsRebarShapeColumn(sourceColumnIndex))
            {
                indexes.Add(sourceColumnIndex);
                return indexes;
            }

            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                if (IsShapeClipboardColumn(i))
                {
                    indexes.Add(i);
                }
            }

            if (!indexes.Contains(sourceColumnIndex))
            {
                indexes.Insert(0, sourceColumnIndex);
            }

            return indexes;
        }

        private bool IsShapeClipboardColumn(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            if (IsRebarShapeColumn(columnIndex))
            {
                return true;
            }

            DataGridViewColumn column = grid.Columns[columnIndex];
            string header = column.HeaderText == null ? "" : column.HeaderText.Trim();
            string name = column.Name == null ? "" : column.Name.Trim();
            string normalized = NormalizeInternalColumnToken(header + name);

            if (normalized.IndexOf("형상", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("SHAPE", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("CADSHAPE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!column.Visible && IsShapeDimensionClipboardColumn(column))
            {
                return true;
            }

            return false;
        }

        private bool IsShapeDimensionClipboardColumn(DataGridViewColumn column)
        {
            if (column == null)
            {
                return false;
            }

            string header = column.HeaderText == null ? "" : column.HeaderText.Trim();
            string name = column.Name == null ? "" : column.Name.Trim();
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < keys.Length; i++)
            {
                string[] candidates = GetDimensionHeaderCandidates(keys[i]);
                int j;

                for (j = 0; j < candidates.Length; j++)
                {
                    string candidate = candidates[j];

                    if (header.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                        || name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void ApplyCellClipboardEntry(BarListCellClipboardEntry entry, int targetRowIndex, int targetColumnIndex)
        {
            if (entry == null
                || targetRowIndex < 0
                || targetRowIndex >= grid.Rows.Count
                || grid.Rows[targetRowIndex].IsNewRow)
            {
                return;
            }

            if (!IsRebarShapeColumn(targetColumnIndex))
            {
                string value;

                if (entry.ValuesByColumn.TryGetValue(cellClipboardData.SourceColumnIndex, out value))
                {
                    grid.Rows[targetRowIndex].Cells[targetColumnIndex].Value = value;
                    RefreshModifiedCellVisual(targetRowIndex, targetColumnIndex);
                }

                return;
            }

            Dictionary<string, string> clonedJsonPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<int, string> pair in entry.ValuesByColumn)
            {
                int columnIndex = pair.Key;

                if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                {
                    continue;
                }

                string value = pair.Value == null ? "" : pair.Value;

                if (IsCadShapeJsonColumn(columnIndex) && value.Trim() != "")
                {
                    string clonedValue;

                    if (!clonedJsonPaths.TryGetValue(value, out clonedValue))
                    {
                        clonedValue = CloneCadShapeJsonForPaste(value, targetRowIndex);
                        clonedJsonPaths[value] = clonedValue;
                    }

                    value = clonedValue;
                }

                grid.Rows[targetRowIndex].Cells[columnIndex].Value = value;
                RefreshModifiedCellVisual(targetRowIndex, columnIndex);
            }

            grid.InvalidateRow(targetRowIndex);
        }

        private string GetCellClipboardDisplayText(DataGridViewCell cell)
        {
            if (cell == null)
            {
                return "";
            }

            if (IsRebarShapeColumn(cell.ColumnIndex))
            {
                string dimensions = GetShapeDimensionText(cell.RowIndex);

                if (dimensions != "")
                {
                    return dimensions;
                }
            }

            object formattedValue = cell.FormattedValue;

            if (formattedValue != null)
            {
                return formattedValue.ToString();
            }

            object value = cell.Value;
            return value == null ? "" : value.ToString();
        }

        private string BuildSystemClipboardText(List<DataGridViewCell> selectedCells)
        {
            if (grid == null || selectedCells == null || selectedCells.Count == 0)
            {
                return "";
            }

            int minDisplayIndex = Int32.MaxValue;
            int maxDisplayIndex = Int32.MinValue;
            HashSet<string> selectedCellKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> selectedRowIndexSet = new HashSet<int>();
            int i;

            for (i = 0; i < selectedCells.Count; i++)
            {
                DataGridViewCell cell = selectedCells[i];

                if (cell == null
                    || cell.RowIndex < 0
                    || cell.ColumnIndex < 0
                    || cell.RowIndex >= grid.Rows.Count
                    || cell.ColumnIndex >= grid.Columns.Count
                    || grid.Rows[cell.RowIndex].IsNewRow
                    || !grid.Rows[cell.RowIndex].Visible
                    || !grid.Columns[cell.ColumnIndex].Visible)
                {
                    continue;
                }

                int displayIndex = grid.Columns[cell.ColumnIndex].DisplayIndex;
                minDisplayIndex = Math.Min(minDisplayIndex, displayIndex);
                maxDisplayIndex = Math.Max(maxDisplayIndex, displayIndex);
                selectedRowIndexSet.Add(cell.RowIndex);
                selectedCellKeys.Add(cell.RowIndex.ToString(CultureInfo.InvariantCulture) + ":"
                    + cell.ColumnIndex.ToString(CultureInfo.InvariantCulture));
            }

            if (selectedRowIndexSet.Count == 0 || minDisplayIndex == Int32.MaxValue)
            {
                return "";
            }

            List<int> selectedRowIndexes = new List<int>(selectedRowIndexSet);
            selectedRowIndexes.Sort();
            List<DataGridViewColumn> columns = new List<DataGridViewColumn>();

            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];

                if (column.Visible
                    && column.DisplayIndex >= minDisplayIndex
                    && column.DisplayIndex <= maxDisplayIndex)
                {
                    columns.Add(column);
                }
            }

            columns.Sort(delegate(DataGridViewColumn left, DataGridViewColumn right)
            {
                return left.DisplayIndex.CompareTo(right.DisplayIndex);
            });

            StringBuilder builder = new StringBuilder();
            int rowOffset;

            for (rowOffset = 0; rowOffset < selectedRowIndexes.Count; rowOffset++)
            {
                int rowIndex = selectedRowIndexes[rowOffset];

                if (rowIndex < 0
                    || rowIndex >= grid.Rows.Count
                    || grid.Rows[rowIndex].IsNewRow
                    || !grid.Rows[rowIndex].Visible)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("\r\n");
                }

                int columnOffset;

                for (columnOffset = 0; columnOffset < columns.Count; columnOffset++)
                {
                    if (columnOffset > 0)
                    {
                        builder.Append('\t');
                    }

                    int columnIndex = columns[columnOffset].Index;
                    string cellKey = rowIndex.ToString(CultureInfo.InvariantCulture) + ":"
                        + columnIndex.ToString(CultureInfo.InvariantCulture);

                    if (!selectedCellKeys.Contains(cellKey))
                    {
                        continue;
                    }

                    string value = GetCellClipboardDisplayText(grid.Rows[rowIndex].Cells[columnIndex]);
                    builder.Append(NormalizeSystemClipboardCellText(value));
                }
            }

            return builder.ToString();
        }

        private string NormalizeSystemClipboardCellText(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return "";
            }

            return value
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");
        }

        private bool TrySetSystemClipboardText(string value)
        {
            try
            {
                string clipboardText = value == null ? "" : value;
                DataObject clipboardData = new DataObject();
                clipboardData.SetData(DataFormats.UnicodeText, clipboardText);
                clipboardData.SetData(DataFormats.Text, clipboardText);
                Clipboard.SetDataObject(clipboardData, true, 5, 80);
                return true;
            }
            catch
            {
                // Windows Clipboard가 잠겨 있어도 같은 열 복사의 OVIA 내부 복사 데이터는 유지합니다.
                return false;
            }
        }

        private string GetClipboardColumnKey(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            DataGridViewColumn column = grid.Columns[columnIndex];
            OviaBarListMappedColumn mapped = column.Tag as OviaBarListMappedColumn;

            if (mapped != null && mapped.StandardKey != null && mapped.StandardKey.Trim() != "")
            {
                return "MAP:" + mapped.StandardKey.Trim().ToUpperInvariant();
            }

            string header = column.HeaderText == null ? "" : column.HeaderText;
            string name = column.Name == null ? "" : column.Name;
            return "COL:" + NormalizeInternalColumnToken(header + "|" + name);
        }

        private string GetClipboardColumnTitle(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            string title = grid.Columns[columnIndex].HeaderText;

            if (title == null || title.Trim() == "")
            {
                title = grid.Columns[columnIndex].Name;
            }

            return title == null ? "" : title.Trim();
        }

        private string BuildClipboardSchemaKey()
        {
            if (grid == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(grid.Columns.Count.ToString(CultureInfo.InvariantCulture));
            int i;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                builder.Append("|");
                builder.Append(GetClipboardColumnKey(i));
            }

            return builder.ToString();
        }

        private bool IsCadShapeJsonColumn(int columnIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            string header = grid.Columns[columnIndex].HeaderText == null ? "" : grid.Columns[columnIndex].HeaderText;
            string name = grid.Columns[columnIndex].Name == null ? "" : grid.Columns[columnIndex].Name;
            string normalized = NormalizeInternalColumnToken(header + name);

            return normalized.IndexOf("CADSHAPEJSON", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string CloneCadShapeJsonForPaste(string savedValue, int targetRowIndex)
        {
            if (savedValue == null || savedValue.Trim() == "")
            {
                return "";
            }

            string sourcePath = ResolveCadShapeJsonPath(savedValue);

            if (sourcePath == "" || !File.Exists(sourcePath))
            {
                return savedValue;
            }

            try
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "OVIA", "ShapeClipboard");
                Directory.CreateDirectory(tempDirectory);

                string extension = Path.GetExtension(sourcePath);

                if (extension == null || extension.Trim() == "")
                {
                    extension = ".json";
                }

                string baseName = Path.GetFileNameWithoutExtension(sourcePath);

                if (baseName == null || baseName.Trim() == "")
                {
                    baseName = "cad_shape";
                }

                string token = Guid.NewGuid().ToString("N").Substring(0, 12);
                string targetFileName = baseName
                    + "_copy_r"
                    + (targetRowIndex + 1).ToString("000", CultureInfo.InvariantCulture)
                    + "_"
                    + token
                    + extension;
                string targetPath = Path.Combine(tempDirectory, targetFileName);

                File.Copy(sourcePath, targetPath, false);

                try
                {
                    CadShapeEditDocument targetDocument = CadShapeEditDocument.Load(targetPath);
                    string originalValue = targetDocument.OriginalSourcePath == null
                        ? ""
                        : targetDocument.OriginalSourcePath.Trim();

                    if (originalValue != "")
                    {
                        string originalPath = originalValue;

                        if (!Path.IsPathRooted(originalPath))
                        {
                            string sourceDirectory = Path.GetDirectoryName(sourcePath);

                            if (sourceDirectory != null && sourceDirectory.Trim() != "")
                            {
                                originalPath = Path.Combine(
                                    sourceDirectory,
                                    originalPath.Replace('/', Path.DirectorySeparatorChar)
                                );
                            }
                        }

                        if (File.Exists(originalPath))
                        {
                            string rawExtension = Path.GetExtension(originalPath);

                            if (rawExtension == null || rawExtension.Trim() == "")
                            {
                                rawExtension = ".json";
                            }

                            string rawFileName = baseName + "_copy_raw_" + token + rawExtension;
                            string targetRawPath = Path.Combine(tempDirectory, rawFileName);
                            File.Copy(originalPath, targetRawPath, false);
                            targetDocument.OriginalSourcePath = rawFileName;
                            targetDocument.Save(targetPath);
                        }
                    }
                }
                catch
                {
                    // 동반 원본 복제 실패 시에도 독립 편집 JSON 복사본은 사용합니다.
                }

                return targetPath;
            }
            catch
            {
                return savedValue;
            }
        }

        private void ContextRowCopy_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            List<int> selectedIndexes = GetSelectedRowIndexes(true);

            if (selectedIndexes.Count == 0)
            {
                MessageBox.Show(
                    "복사할 행을 먼저 선택해주세요.",
                    "OVIA 행 복사",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            rowClipboardRows = new List<object[]>();
            int i;

            for (i = 0; i < selectedIndexes.Count; i++)
            {
                rowClipboardRows.Add(CloneRowValues(grid.Rows[selectedIndexes[i]]));
            }

            rowClipboardSchemaKey = BuildClipboardSchemaKey();
            RefreshClipboardMenuState();

            lblStatus.Text = selectedIndexes.Count == 1
                ? "행 전체를 복사했습니다. 추가한 행 또는 기존 행을 선택한 뒤 [행 붙여넣기]를 사용할 수 있습니다."
                : selectedIndexes.Count.ToString() + "개 행을 복사했습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void ContextRowPaste_Click(object sender, EventArgs e)
        {
            if (!CanUseExtractEditMenu())
            {
                return;
            }

            if (rowClipboardRows == null || rowClipboardRows.Count == 0)
            {
                MessageBox.Show(
                    "복사한 행이 없습니다.\r\n\r\n우클릭 메뉴에서 [행 복사]를 먼저 실행해주세요.",
                    "OVIA 행 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            if (!String.Equals(rowClipboardSchemaKey, BuildClipboardSchemaKey(), StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "복사한 행과 현재 BarList의 열 구성이 달라 붙여넣을 수 없습니다.",
                    "OVIA 행 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            List<int> targetIndexes = GetSelectedRowIndexes(true);

            if (targetIndexes.Count == 0)
            {
                MessageBox.Show(
                    "붙여넣을 대상 행을 먼저 선택해주세요.",
                    "OVIA 행 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            if (rowClipboardRows.Count > 1 && targetIndexes.Count == 1)
            {
                int startIndex = targetIndexes[0];

                if (startIndex + rowClipboardRows.Count > grid.Rows.Count)
                {
                    MessageBox.Show(
                        "복사한 행 수만큼 붙여넣을 대상 행이 부족합니다.\r\n\r\n행을 추가한 뒤 다시 실행해주세요.",
                        "OVIA 행 붙여넣기",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                targetIndexes.Clear();
                int rowOffset;

                for (rowOffset = 0; rowOffset < rowClipboardRows.Count; rowOffset++)
                {
                    targetIndexes.Add(startIndex + rowOffset);
                }
            }
            else if (rowClipboardRows.Count != 1 && rowClipboardRows.Count != targetIndexes.Count)
            {
                MessageBox.Show(
                    "복사한 행 수와 붙여넣을 대상 행 수가 다릅니다.\r\n\r\n"
                    + "행 하나를 복사하면 여러 행에 반복해서 붙여넣을 수 있습니다.\r\n"
                    + "여러 행을 복사한 경우에는 같은 수의 대상 행을 선택해주세요.",
                    "OVIA 행 붙여넣기",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            PushUndoState(CaptureGridState());

            int i;

            for (i = 0; i < targetIndexes.Count; i++)
            {
                object[] sourceValues = rowClipboardRows.Count == 1
                    ? rowClipboardRows[0]
                    : rowClipboardRows[i];

                ApplyRowClipboardValues(targetIndexes[i], sourceValues);
            }

            ApplyRebarCalculationAndValidation(false);
            MarkUnsaved();
            RecalculateSummary();
            grid.Invalidate();

            lblStatus.Text = rowClipboardRows.Count == 1
                ? "복사한 행을 " + targetIndexes.Count.ToString() + "개 대상 행에 붙여넣었습니다."
                : rowClipboardRows.Count.ToString() + "개 행을 붙여넣었습니다.";
            lblStatus.ForeColor = TextSub;
        }

        private void ApplyRowClipboardValues(int targetRowIndex, object[] sourceValues)
        {
            if (sourceValues == null
                || sourceValues.Length != grid.Columns.Count
                || targetRowIndex < 0
                || targetRowIndex >= grid.Rows.Count
                || grid.Rows[targetRowIndex].IsNewRow)
            {
                return;
            }

            int c;

            for (c = 0; c < grid.Columns.Count; c++)
            {
                string value = sourceValues[c] == null ? "" : sourceValues[c].ToString();

                if (IsCadShapeJsonColumn(c) && value.Trim() != "")
                {
                    value = CloneCadShapeJsonForPaste(value, targetRowIndex);
                }

                grid.Rows[targetRowIndex].Cells[c].Value = value;
                RefreshModifiedCellVisual(targetRowIndex, c);
            }

            ResetImportedCalculationMetaForRows(targetRowIndex, targetRowIndex);
            grid.InvalidateRow(targetRowIndex);
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

            EnsureLogicalRowOrderKeys();

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
                state.RowOrderKeys.Add(GetLogicalRowOrderKey(grid.Rows[r]));
                state.ShapeContentFingerprints.Add(BuildCadShapeContentFingerprint(r));
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
                logicalRowOrderKeys.Clear();
                nextLogicalRowOrderKey = 1L;

                int i;

                for (i = 0; i < state.Rows.Count; i++)
                {
                    int newRowIndex = grid.Rows.Add(state.Rows[i]);
                    long logicalOrderKey = i < state.RowOrderKeys.Count ? state.RowOrderKeys[i] : nextLogicalRowOrderKey;
                    SetLogicalRowOrderKey(grid.Rows[newRowIndex], logicalOrderKey);

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
            // 뒤로가기 클릭 자체가 마지막 셀의 CellEndEdit를 늦게 발생시키지 않도록
            // 먼저 편집을 확정한 다음 실제 저장 baseline과 비교한다.
            CommitPendingGridEdit();
            RefreshSaveStateFromCurrentGrid();

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
            StopAutoCadAvailabilityTimer();
            ReleaseAutoCadSelectionModeSilently();
            StopAutoCadWatcher();
        }

        public bool HasUnsavedWorkspaceData()
        {
            CommitPendingGridEdit();
            RefreshSaveStateFromCurrentGrid();
            return !isSaved && grid != null && grid.Columns.Count > 0 && grid.Rows.Count > 0;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "BarList";
        }

        private void Close_Click(object sender, EventArgs e)
        {
            NavigateBackToProjectBarListList();
        }

        private void FrmBarList_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isInternalNavigation)
            {
                StopAutoCadAvailabilityTimer();
                ReleaseAutoCadSelectionModeSilently();
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

            StopAutoCadAvailabilityTimer();
            ReleaseAutoCadSelectionModeSilently();
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
            List<string> candidates = FindOviaBoxTableCsvFilesAfter(startTime);

            if (candidates.Count == 0)
            {
                return "";
            }

            return candidates[candidates.Count - 1];
        }

        private List<string> FindOviaBoxTableCsvFilesAfter(DateTime startTime)
        {
            List<string> candidates = new List<string>();
            string importDirectory = GetProjectBarListTempDirectory();

            if (!Directory.Exists(importDirectory))
            {
                return candidates;
            }

            /*
             * OVIABOXTABLE은 이제 스마트 통합 추출 명령입니다.
             * 과거 테스트용 OVIAGRIDTABLE 파일명도 자동 입력 대상에 포함해 둡니다.
             */
            string[] files = Directory.GetFiles(importDirectory, "OVIA_*Table_*.csv");

            if (files == null || files.Length == 0)
            {
                return candidates;
            }

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
                return candidates;
            }

            candidates.Sort(delegate (string a, string b)
            {
                DateTime at = File.GetLastWriteTime(a);
                DateTime bt = File.GetLastWriteTime(b);

                int timeCompare = at.CompareTo(bt);

                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            return candidates;
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

        private bool LoadCsvWithImportPolicy(string filePath, bool loadAsSaved)
        {
            if (loadAsSaved)
            {
                return LoadCsv(filePath, true);
            }

            if (!CanImportIntoCurrentBarList())
            {
                return false;
            }

            BarListImportMode mode;

            if (waitingAutoCadImport)
            {
                if (autoCadContinuousAppendMode && HasGridData())
                {
                    mode = BarListImportMode.Append;
                }
                else
                {
                    mode = autoCadInitialImportMode;
                }
            }
            else
            {
                mode = DecideImportModeForCurrentGrid();
            }

            if (mode == BarListImportMode.Cancel)
            {
                return false;
            }

            if (mode == BarListImportMode.Append)
            {
                return AppendCsv(filePath);
            }

            return LoadCsv(filePath, false);
        }

        private BarListImportMode DecideImportModeForCurrentGrid()
        {
            if (!HasGridData())
            {
                return BarListImportMode.Replace;
            }

            DialogResult result = MessageBox.Show(
                "기존 BarList 데이터가 있습니다.\r\n\r\n" +
                "새롭게 불러오면 현재 화면의 기존 데이터는 삭제되고 새 추출 데이터로 교체됩니다.\r\n" +
                "기존 데이터 뒤에 이어서 추가하면 현재 행 아래로 새 추출 데이터가 계속 추가됩니다.\r\n\r\n" +
                "[예] 새롭게 불러오기\r\n" +
                "[아니오] 기존 데이터에 이어서 추가\r\n" +
                "[취소] 가져오기 취소",
                "OVIA BarList 데이터 추가 방식",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                return BarListImportMode.Replace;
            }

            if (result == DialogResult.No)
            {
                return BarListImportMode.Append;
            }

            return BarListImportMode.Cancel;
        }

        private bool CanImportIntoCurrentBarList()
        {
            if (HasGridData() && IsCurrentBarListImportLocked())
            {
                MessageBox.Show(
                    "이미 출하가 완료되었거나 태그가 발행되었습니다.",
                    "OVIA BarList",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                lblStatus.Text = "이미 출하가 완료되었거나 태그가 발행되어 데이터를 추가할 수 없습니다.";
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return false;
            }

            return true;
        }

        private bool HasGridData()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return false;
            }

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCurrentBarListImportLocked()
        {
            if (IsBarListFileLocked(savedProjectFilePath))
            {
                return true;
            }

            if (txtFilePath != null && IsBarListFileLocked(GetReferenceFilePath()))
            {
                return true;
            }

            return GridContainsLockedStatus();
        }

        private bool IsBarListFileLocked(string filePath)
        {
            if (filePath == null || filePath.Trim() == "")
            {
                return false;
            }

            filePath = filePath.Trim();

            if (File.Exists(filePath) && FileNameLooksLocked(filePath))
            {
                return true;
            }

            if (File.Exists(filePath) && CsvContainsLockedStatus(filePath))
            {
                return true;
            }

            string metaPath = filePath + ".ovia-meta";

            if (File.Exists(metaPath) && MetaFileContainsLockedStatus(metaPath))
            {
                return true;
            }

            try
            {
                string changedMetaPath = Path.ChangeExtension(filePath, ".ovia-meta");

                if (!String.Equals(metaPath, changedMetaPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(changedMetaPath)
                    && MetaFileContainsLockedStatus(changedMetaPath))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool FileNameLooksLocked(string filePath)
        {
            string name = "";

            try
            {
                name = Path.GetFileNameWithoutExtension(filePath);
            }
            catch
            {
                name = filePath;
            }

            return ContainsLockedKeyword(name);
        }

        private bool CsvContainsLockedStatus(string filePath)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows == null || rows.Count <= 1 || rows[0] == null)
                {
                    return false;
                }

                List<int> statusColumns = new List<int>();
                int i;
                int r;

                for (i = 0; i < rows[0].Count; i++)
                {
                    string header = rows[0][i] == null ? "" : rows[0][i];

                    if (ContainsAny(header, "출하", "출고", "태그", "TAG", "상태", "Status", "shipping", "shipped", "tag"))
                    {
                        statusColumns.Add(i);
                    }
                }

                for (r = 1; r < rows.Count; r++)
                {
                    if (rows[r] == null)
                    {
                        continue;
                    }

                    if (statusColumns.Count > 0)
                    {
                        for (i = 0; i < statusColumns.Count; i++)
                        {
                            int columnIndex = statusColumns[i];

                            if (columnIndex >= 0 && columnIndex < rows[r].Count && ContainsLockedKeyword(rows[r][columnIndex]))
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        int c;

                        for (c = 0; c < rows[r].Count; c++)
                        {
                            if (ContainsLockedKeyword(rows[r][c]))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool MetaFileContainsLockedStatus(string metaPath)
        {
            try
            {
                string text = File.ReadAllText(metaPath, Encoding.UTF8);
                return ContainsLockedKeyword(text)
                    || Regex.IsMatch(text, "tag\\s*issued\\s*[:=]\\s*true", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(text, "shipping\\s*done\\s*[:=]\\s*true", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(text, "shipped\\s*[:=]\\s*true", RegexOptions.IgnoreCase);
            }
            catch
            {
            }

            return false;
        }

        private bool GridContainsLockedStatus()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return false;
            }

            int r;
            int c;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                for (c = 0; c < grid.Columns.Count; c++)
                {
                    string header = grid.Columns[c].HeaderText == null ? "" : grid.Columns[c].HeaderText;
                    string name = grid.Columns[c].Name == null ? "" : grid.Columns[c].Name;

                    if (!ContainsAny(header, "출하", "태그", "TAG", "상태", "Status")
                        && !ContainsAny(name, "shipping", "shipped", "tag", "status"))
                    {
                        continue;
                    }

                    if (ContainsLockedKeyword(GetCellText(r, c)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ContainsLockedKeyword(string value)
        {
            if (value == null)
            {
                return false;
            }

            string text = value.Trim();

            if (text == "")
            {
                return false;
            }

            string normalized = text.Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();

            return normalized.IndexOf("출하완료", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("출고완료", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("TAG발행", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("태그발행", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("태그발급", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("TAGISSUED", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("SHIPPED", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("SHIPPINGDONE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LoadCsv(string filePath, bool loadAsSaved)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);
                rows = RemoveNonRebarRowsFromAutoCadCsv(rows);
                NormalizeCadShapePathsInCsvRows(rows, filePath);
                ApplyRegistrationDraftToCsvRows(rows);

                if (rows == null || rows.Count <= 1)
                {
                    lblStatus.Text = "CSV 파일에 읽을 데이터가 없습니다.";
                    lblStatus.ForeColor = OviaFluentTheme.Danger;

                    return false;
                }

                BindCsvRows(rows);
                rebarMismatchWarningShown = false;
                ApplyRebarCalculationAndValidation(true);
                allowExtractEditMenu = true;
                ClearUndoRedoStates();
                SetReferenceFilePath(filePath);
                lastLoadedFilePath = filePath;

                RecalculateSummary();
                ReindexLogicalRowOrder();

                if (loadAsSaved)
                {
                    CaptureSavedGridBaseline();
                    lblStatus.Text = "저장된 BarList 열기 - " + GetMappingSummaryText();
                    lblStatus.ForeColor = TextSub;
                }
                else
                {
                    MarkUnsaved();
                    lblStatus.Text = "BarList 후보 데이터 입력 완료 - " + GetMappingSummaryText();
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                }

                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CSV 불러오기 오류 - " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return false;
            }
        }

        private bool AppendCsv(string filePath)
        {
            try
            {
                List<List<string>> rows = ReadCsv(filePath);
                rows = RemoveNonRebarRowsFromAutoCadCsv(rows);
                NormalizeCadShapePathsInCsvRows(rows, filePath);
                ApplyRegistrationDraftToCsvRows(rows);

                if (rows == null || rows.Count <= 1)
                {
                    lblStatus.Text = "CSV 파일에 추가할 데이터가 없습니다.";
                    lblStatus.ForeColor = OviaFluentTheme.Danger;
                    return false;
                }

                if (!HasGridData())
                {
                    return LoadCsv(filePath, false);
                }

                int appendedRowCount = AppendCsvRows(rows);

                if (appendedRowCount <= 0)
                {
                    allowExtractEditMenu = true;
                    SetReferenceFilePath(filePath);
                    lastLoadedFilePath = filePath;
                    RecalculateSummary();
                    lblStatus.Text = "추가할 신규 BarList 행이 없습니다.";
                    lblStatus.ForeColor = TextSub;
                    return true;
                }

                rebarMismatchWarningShown = false;
                ApplyRebarCalculationAndValidation(true);
                allowExtractEditMenu = true;
                ClearUndoRedoStates();
                SetReferenceFilePath(filePath);
                lastLoadedFilePath = filePath;
                RecalculateSummary();
                MarkUnsaved();
                lblStatus.Text = "BarList 데이터가 기존 행 뒤에 추가되었습니다 - " + GetMappingSummaryText();
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CSV 추가 입력 오류 - " + ex.Message;
                lblStatus.ForeColor = OviaFluentTheme.Danger;
                return false;
            }
        }

        private void ApplyRegistrationDraftToCsvRows(List<List<string>> rows)
        {
            if (registrationDraft == null || rows == null || rows.Count == 0 || rows[0] == null)
            {
                return;
            }

            SetRegistrationDraftCsvValue(rows, "제목", registrationDraft.Title);
            SetRegistrationDraftCsvValue(rows, "작성", registrationDraft.WriteStatus);
            SetRegistrationDraftCsvValue(rows, "동", registrationDraft.Building);
            SetRegistrationDraftCsvValue(rows, "층", registrationDraft.Floor);
            SetRegistrationDraftCsvValue(rows, "공종", registrationDraft.WorkType);
            SetRegistrationDraftCsvValue(rows, "태그", registrationDraft.Tags);
            SetRegistrationDraftCsvValue(rows, "색상", registrationDraft.Color);
            SetRegistrationDraftCsvValue(rows, "발주일", registrationDraft.OrderDate);
            SetRegistrationDraftCsvValue(rows, "등록일", registrationDraft.CreatedDate);
            SetRegistrationDraftCsvValue(rows, "납기일", registrationDraft.DueDate);
            // 공사별 BarList 헤더의 최초 작성자를 CSV 메타로 보존한다. 상세 철근 Grid에는 표시하지 않는다.
            SetRegistrationDraftCsvValue(rows, "작성자", registrationDraft.Writer);
            SetRegistrationDraftCsvValue(rows, "OVIA_BARLIST_MEMO", registrationDraft.Memo);
        }

        private void SetRegistrationDraftCsvValue(List<List<string>> rows, string header, string value)
        {
            if (rows == null || rows.Count == 0 || rows[0] == null)
            {
                return;
            }

            int columnIndex = FindCsvColumnIndex(rows[0], header);

            if (columnIndex < 0)
            {
                columnIndex = rows[0].Count;
                rows[0].Add(header);

                int addRowIndex;
                for (addRowIndex = 1; addRowIndex < rows.Count; addRowIndex++)
                {
                    if (rows[addRowIndex] == null)
                    {
                        rows[addRowIndex] = new List<string>();
                    }

                    while (rows[addRowIndex].Count <= columnIndex)
                    {
                        rows[addRowIndex].Add("");
                    }
                }
            }

            int rowIndex;
            for (rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                if (rows[rowIndex] == null)
                {
                    rows[rowIndex] = new List<string>();
                }

                while (rows[rowIndex].Count <= columnIndex)
                {
                    rows[rowIndex].Add("");
                }

                rows[rowIndex][columnIndex] = value == null ? "" : value;
            }
        }

        private List<List<string>> RemoveNonRebarRowsFromAutoCadCsv(List<List<string>> rows)
        {
            if (rows == null || rows.Count == 0 || rows[0] == null)
            {
                return rows;
            }

            int rowTypeColumn = FindCsvColumnIndex(rows[0], "ROWTYPE");

            // 일반 사용자가 선택한 CSV와 저장된 BarList에는 이 필터를 적용하지 않습니다.
            // AutoCAD 추출 계약의 내부 RowType 컬럼이 있는 파일만 대상으로 합니다.
            if (rowTypeColumn < 0)
            {
                return rows;
            }

            int markColumn = FindCsvColumnIndex(rows[0], "번호", "MARK", "MARKNO", "BARNO");
            int specColumn = FindCsvColumnIndex(rows[0], "철근규격", "철근 규격", "규격", "DIA");
            int lengthColumn = FindCsvColumnIndex(rows[0], "길이MM", "길이(mm)", "길이", "LENGTH");
            int qtyColumn = FindCsvColumnIndex(rows[0], "수량EA", "수량(EA)", "수량", "QTY", "QUANTITY");
            int totalLengthColumn = FindCsvColumnIndex(rows[0], "총길이M", "총길이(M)", "총길이", "TOTALLENGTH");
            int totalWeightColumn = FindCsvColumnIndex(rows[0], "중량TON", "중량(Ton)", "총중량TON", "총중량(Ton)", "중량", "총중량", "TOTALWEIGHT");
            bool canValidateRebarData = markColumn >= 0 && specColumn >= 0 && lengthColumn >= 0 && qtyColumn >= 0;

            List<List<string>> filtered = new List<List<string>>();
            filtered.Add(rows[0]);
            int r;

            for (r = 1; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                string rowType = GetCsvCellText(row, rowTypeColumn).Trim();

                if (!rowType.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (canValidateRebarData && !IsActualRebarCsvRow(row, markColumn, specColumn, lengthColumn, qtyColumn))
                {
                    continue;
                }

                RepairShiftedAutoCadTotalColumns(
                    row,
                    lengthColumn,
                    qtyColumn,
                    totalLengthColumn,
                    totalWeightColumn
                );

                filtered.Add(row);
            }

            return filtered;
        }

        private bool RepairShiftedAutoCadTotalColumns(
            List<string> row,
            int lengthColumn,
            int qtyColumn,
            int totalLengthColumn,
            int totalWeightColumn)
        {
            if (row == null
                || lengthColumn < 0
                || qtyColumn < 0
                || totalLengthColumn < 0
                || totalWeightColumn < 0
                || GetCsvCellText(row, totalLengthColumn).Trim() != "")
            {
                return false;
            }

            double lengthMm;
            double qty;
            decimal importedWeightCell;

            if (!TryParseNumber(GetCsvCellText(row, lengthColumn), out lengthMm)
                || !TryParseNumber(GetCsvCellText(row, qtyColumn), out qty)
                || !TryParseDecimalNumber(GetCsvCellText(row, totalWeightColumn), out importedWeightCell)
                || lengthMm <= 0
                || qty <= 0)
            {
                return false;
            }

            decimal expectedTotalLength = Decimal.Round(
                ((decimal)lengthMm * (decimal)qty) / 1000M,
                3,
                MidpointRounding.AwayFromZero
            );
            decimal importedRounded = Decimal.Round(
                importedWeightCell,
                3,
                MidpointRounding.AwayFromZero
            );

            if (expectedTotalLength != importedRounded)
            {
                return false;
            }

            while (row.Count <= Math.Max(totalLengthColumn, totalWeightColumn))
            {
                row.Add("");
            }

            /*
             * 일부 구버전 CAD CSV는 총길이 열을 비우고 그 값을 중량 열로 한 칸 밀어 기록했습니다.
             * 길이×수량/1000과 중량 칸 값이 소수 셋째 자리까지 정확히 같은 경우에만 총길이로
             * 되돌립니다. 이 CSV에는 실제 CAD 중량이 소실되어 있으므로 잘못된 값을 원본 중량으로
             * 비교하지 않도록 중량 원본은 빈 값으로 둡니다. 이후 화면 값은 OVIA 단위중량으로 계산됩니다.
             */
            row[totalLengthColumn] = GetCsvCellText(row, totalWeightColumn);
            row[totalWeightColumn] = "";
            return true;
        }

        private int FindCsvColumnIndex(List<string> headers, params string[] candidates)
        {
            if (headers == null || candidates == null)
            {
                return -1;
            }

            int i;
            int c;

            for (i = 0; i < headers.Count; i++)
            {
                string header = NormalizeInternalColumnToken(headers[i]);

                for (c = 0; c < candidates.Length; c++)
                {
                    if (header == NormalizeInternalColumnToken(candidates[c]))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private bool IsActualRebarCsvRow(List<string> row, int markColumn, int specColumn, int lengthColumn, int qtyColumn)
        {
            string mark = GetCsvCellText(row, markColumn);
            string spec = GetCsvCellText(row, specColumn);
            double length;
            double qty;

            Match markMatch = Regex.Match(mark.Trim(), @"^([0-9]{1,6})[A-Za-z]?$", RegexOptions.IgnoreCase);
            int markNumber;
            bool markOk = markMatch.Success
                && Int32.TryParse(markMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out markNumber)
                && markNumber > 0;
            bool specOk = Regex.IsMatch(spec.Trim(), @"^(?:UHD|SHD|HD|SD|D)[0-9]{1,3}[A-Z]{0,4}$", RegexOptions.IgnoreCase);
            bool lengthOk = TryParseNumber(GetCsvCellText(row, lengthColumn), out length) && length > 0;
            bool qtyOk = TryParseNumber(GetCsvCellText(row, qtyColumn), out qty) && qty > 0;

            return markOk && specOk && lengthOk && qtyOk;
        }

        private string GetCsvCellText(List<string> row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Count || row[columnIndex] == null)
            {
                return "";
            }

            return row[columnIndex].Trim();
        }

        private void NormalizeCadShapePathsInCsvRows(List<List<string>> rows, string csvFilePath)
        {
            if (rows == null || rows.Count == 0 || rows[0] == null || csvFilePath == null)
            {
                return;
            }

            int jsonColumnIndex = -1;

            for (int i = 0; i < rows[0].Count; i++)
            {
                string header = rows[0][i] == null ? "" : rows[0][i].Trim();

                if (header.Equals("OVIA_CAD_SHAPE_JSON", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("CAD_SHAPE_JSON", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("OVIA CAD SHAPE JSON", StringComparison.OrdinalIgnoreCase))
                {
                    jsonColumnIndex = i;
                    break;
                }
            }

            if (jsonColumnIndex < 0)
            {
                return;
            }

            string csvDirectory = Path.GetDirectoryName(csvFilePath);

            if (csvDirectory == null || csvDirectory.Trim() == "")
            {
                return;
            }

            for (int r = 1; r < rows.Count; r++)
            {
                if (rows[r] == null || jsonColumnIndex >= rows[r].Count)
                {
                    continue;
                }

                string value = rows[r][jsonColumnIndex] == null ? "" : rows[r][jsonColumnIndex].Trim();

                if (value == "" || Path.IsPathRooted(value))
                {
                    continue;
                }

                string absolutePath = Path.GetFullPath(Path.Combine(csvDirectory, value.Replace('/', Path.DirectorySeparatorChar)));
                rows[r][jsonColumnIndex] = absolutePath;
            }
        }

        private int AppendCsvRows(List<List<string>> rows)
        {
            return AppendCsvRows(rows, false);
        }

        private int AppendCsvRows(List<List<string>> rows, bool highlightOtherBarListRows)
        {
            rows = RemoveRuntimeCsvColumnsForDisplay(rows);

            if (rows == null || rows.Count == 0)
            {
                return 0;
            }

            BeginGridSelectionUpdate();
            grid.SuspendLayout();

            try
            {
                List<string> sourceHeaders = rows[0];
                OviaBarListMappingStore store = GetMappingStore();
                OviaBarListMappedTable mappedTable = store.BuildMappedTable(sourceHeaders);

                lastMappingMatchCount = mappedTable.MatchedCount;
                lastMappingTotalHeaderCount = sourceHeaders.Count;
                lastMappingVersion = store.Version;

                Dictionary<int, int> destinationColumns = EnsureMappedColumnsForAppend(mappedTable);

                /*
                 * OVIA 2026-07-20 동일 번호 독립 표 보존:
                 * 선택영역 중복은 AutoCAD의 기존 OVIA 선택박스와 행 좌표로 이미 제외됩니다.
                 * 여기서 원본 DWG + 철근 번호를 다시 중복키로 사용하면 같은 도면의 서로 다른
                 * 철근재료표(예: 표 A 1~12, 표 B 1~8)에서 표 B 행이 잘못 삭제됩니다.
                 * 따라서 준비 검사를 통과한 새 CSV 행은 번호 중복 여부와 무관하게 모두 추가합니다.
                 * 동일 CSV 파일 이벤트의 중복 수신은 autoCadProcessedCsvFiles가 별도로 차단합니다.
                 */
                int startRowIndex = grid.Rows.Count;
                int appendedRowCount = 0;
                int r;
                int i;

                for (r = 1; r < rows.Count; r++)
                {
                    List<string> values = rows[r];
                    object[] cells = new object[grid.Columns.Count];

                    for (i = 0; i < cells.Length; i++)
                    {
                        cells[i] = "";
                    }

                    for (i = 0; i < mappedTable.Columns.Count; i++)
                    {
                        if (!destinationColumns.ContainsKey(i))
                        {
                            continue;
                        }

                        int destinationIndex = destinationColumns[i];
                        int sourceIndex = mappedTable.Columns[i].SourceIndex;

                        if (sourceIndex >= 0 && sourceIndex < values.Count)
                        {
                            cells[destinationIndex] = values[sourceIndex];
                        }
                    }

                    int newRowIndex = grid.Rows.Add(cells);
                    SetRowOriginalValues(newRowIndex, cells);

                    if (highlightOtherBarListRows)
                    {
                        ApplyOtherBarListImportedRowStyle(grid.Rows[newRowIndex]);
                    }

                    appendedRowCount++;
                }

                if (appendedRowCount > 0)
                {
                    ConvertAppendedWeightColumnsIfNeeded(mappedTable, destinationColumns, startRowIndex, grid.Rows.Count - 1);
                    ResetImportedCalculationMetaForRows(startRowIndex, grid.Rows.Count - 1);
                    ApplyGridColumnStyle();
                    ApplySourceDrawingToolTips(startRowIndex, grid.Rows.Count - 1);

                    for (r = startRowIndex; r < grid.Rows.Count; r++)
                    {
                        if (!grid.Rows[r].IsNewRow)
                        {
                            SetRowOriginalValues(r, CloneRowValues(grid.Rows[r]));
                        }
                    }

                    ReapplyBarListGridSortIfNeeded();
                }

                return appendedRowCount;
            }
            finally
            {
                grid.ResumeLayout();
                EndGridSelectionUpdate();
            }
        }

        private Dictionary<int, int> EnsureMappedColumnsForAppend(OviaBarListMappedTable mappedTable)
        {
            Dictionary<int, int> destinationColumns = new Dictionary<int, int>();

            if (mappedTable == null)
            {
                return destinationColumns;
            }

            int i;

            for (i = 0; i < mappedTable.Columns.Count; i++)
            {
                string header = mappedTable.Columns[i].DisplayName;

                if (header == null || header.Trim() == "")
                {
                    header = "Column" + (i + 1).ToString();
                }

                int destinationIndex = FindExactColumnIndexByHeaders(new string[] { header });

                if (destinationIndex < 0)
                {
                    DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                    column.Name = GetSafeColumnName(header, grid.Columns.Count);
                    column.HeaderText = header;
                    column.Tag = mappedTable.Columns[i];
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    column.MinimumWidth = 45;
                    column.SortMode = IsBarListGridSortableHeader(header) ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable;
                    column.Resizable = DataGridViewTriState.True;
                    grid.Columns.Add(column);
                    destinationIndex = grid.Columns.Count - 1;
                }

                destinationColumns[i] = destinationIndex;
            }

            return destinationColumns;
        }

        private void ConvertAppendedWeightColumnsIfNeeded(OviaBarListMappedTable mappedTable, Dictionary<int, int> destinationColumns, int startRowIndex, int endRowIndex)
        {
            if (mappedTable == null || destinationColumns == null)
            {
                return;
            }

            int i;

            for (i = 0; i < mappedTable.Columns.Count; i++)
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

                if (!destinationColumns.ContainsKey(i))
                {
                    continue;
                }

                ConvertColumnKgToTon(destinationColumns[i], startRowIndex, endRowIndex);
            }
        }

        private void BindCsvRows(List<List<string>> rows)
        {
            rows = RemoveRuntimeCsvColumnsForDisplay(rows);

            BeginGridSelectionUpdate();
            grid.SuspendLayout();

            try
            {
                ResetBarListGridSortState();
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
                    column.SortMode = IsBarListGridSortableHeader(header) ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable;
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
                ResetImportedCalculationMetaForRows(0, grid.Rows.Count - 1);
                ApplyGridColumnStyle();
                ApplySourceDrawingToolTips(0, grid.Rows.Count - 1);

                // 숨김 CAD 형상 경로를 포함한 현재 열 구성이 확정된 뒤 원본값을 다시 저장합니다.
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
            string shapeSource = GetShapeSourceText(e.RowIndex);
            bool cadSource = shapeSource != null && shapeSource.Trim().Equals("CAD", StringComparison.OrdinalIgnoreCase);
            bool manualVectorSource = IsManualVectorEditedRow(e.RowIndex) && cadShapePath != "";

            e.Handled = true;
            PaintGridCellBase(e, selected);

            if ((!IsManualShapeOverrideRow(e.RowIndex) || manualVectorSource) && (cadSource || cadShapePath != ""))
            {
                /*
                 * CAD 추출 행은 JSON 파일이 일시적으로 확인되지 않더라도 형번/형상코드 텍스트로
                 * 되돌아가면 안 됩니다. 스크롤 재페인트 때 70, 407 등이 나타나는 현상을 차단하고,
                 * CAD 벡터 경로만 단일 소스로 사용합니다.
                 */
                cadShapeRenderer.DrawCadShape(
                    e.Graphics,
                    e.CellBounds,
                    cadShapePath,
                    selected,
                    GetShapeDimensionText(e.RowIndex),
                    IsCadShapeTextEditedRow(e.RowIndex),
                    gridZoomPercent / 100F
                );
                PaintGridCellBorder(e.Graphics, e.CellBounds);
                return;
            }

            string dimensionText = GetShapeDimensionText(e.RowIndex);
            shapeRenderer.DrawShape(e.Graphics, e.CellBounds, shape, rawText, selected, dimensionText);
            PaintGridCellBorder(e.Graphics, e.CellBounds);
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

        private string GetShapeStatusText(int rowIndex)
        {
            return GetFirstExistingCellText(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" });
        }

        private bool IsManualVectorEditedRow(int rowIndex)
        {
            string source = GetShapeSourceText(rowIndex);
            string status = GetShapeStatusText(rowIndex);

            return source != null
                && source.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase)
                && status != null
                && status.Trim().Equals("MANUAL_EDITED", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCadShapeTextEditedRow(int rowIndex)
        {
            string status = GetShapeStatusText(rowIndex);

            return status != null
                && status.Trim().Equals("CAD_EDITED", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildCadShapeContentFingerprint(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows[rowIndex].IsNewRow)
            {
                return "";
            }

            string savedValue = GetCadShapeJsonText(rowIndex);
            string path = ResolveCadShapeJsonPath(savedValue);

            if (path == "" || !File.Exists(path))
            {
                return savedValue == null ? "" : savedValue.Trim();
            }

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder sb = new StringBuilder(hash.Length * 2);

                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                try
                {
                    FileInfo info = new FileInfo(path);
                    return info.Length.ToString(CultureInfo.InvariantCulture)
                        + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    return savedValue == null ? "" : savedValue.Trim();
                }
            }
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

            string csvPath = GetReferenceFilePath();

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
            HashSet<string> usedShapeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                fileName = BuildUniqueCadShapeFileName(fileName, r + 1, usedShapeFileNames);
                string targetPath = Path.Combine(shapeDir, fileName);

                if (!IsSameFullPath(sourcePath, targetPath))
                {
                    File.Copy(sourcePath, targetPath, true);
                }

                CopyCadShapeOriginalCompanion(sourcePath, targetPath, shapeDir);
                grid.Rows[r].Cells[cadShapeColumnIndex].Value = "Shapes/" + fileName;
            }
        }

        private string BuildUniqueCadShapeFileName(string originalFileName, int rowNumber, HashSet<string> usedFileNames)
        {
            string safeName = originalFileName == null ? "" : Path.GetFileName(originalFileName);

            if (safeName == "")
            {
                safeName = "row_" + rowNumber.ToString("000") + "_shape.json";
            }

            if (usedFileNames == null || usedFileNames.Add(safeName))
            {
                return safeName;
            }

            string extension = Path.GetExtension(safeName);
            string name = Path.GetFileNameWithoutExtension(safeName);
            string suffix = "_row_" + rowNumber.ToString("000");

            if (name.EndsWith("_ovia_edit", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "_ovia_edit".Length) + suffix + "_ovia_edit";
            }
            else
            {
                name += suffix;
            }

            string candidate = name + extension;
            int sequence = 2;

            while (!usedFileNames.Add(candidate))
            {
                candidate = name + "_" + sequence.ToString("00") + extension;
                sequence++;
            }

            return candidate;
        }

        private string BuildRawCompanionFileName(string editedFileName)
        {
            string safeName = editedFileName == null ? "" : Path.GetFileNameWithoutExtension(editedFileName);

            if (safeName == "")
            {
                safeName = "cad_shape";
            }

            if (safeName.EndsWith("_ovia_edit", StringComparison.OrdinalIgnoreCase))
            {
                safeName = safeName.Substring(0, safeName.Length - "_ovia_edit".Length);
            }

            return safeName + "_ovia_raw.json";
        }

        private void CopyCadShapeOriginalCompanion(string sourceEditedPath, string targetEditedPath, string targetShapeDirectory)
        {
            if (sourceEditedPath == null || sourceEditedPath.Trim() == "" || targetEditedPath == null || targetEditedPath.Trim() == "")
            {
                return;
            }

            try
            {
                CadShapeEditDocument sourceDocument = CadShapeEditDocument.Load(sourceEditedPath);
                string originalValue = sourceDocument.OriginalSourcePath == null ? "" : sourceDocument.OriginalSourcePath.Trim();

                if (originalValue == "")
                {
                    return;
                }

                string originalPath = originalValue;

                if (!Path.IsPathRooted(originalPath))
                {
                    string sourceDirectory = Path.GetDirectoryName(sourceEditedPath);

                    if (sourceDirectory == null || sourceDirectory.Trim() == "")
                    {
                        return;
                    }

                    originalPath = Path.Combine(sourceDirectory, originalPath.Replace('/', Path.DirectorySeparatorChar));
                }

                if (!File.Exists(originalPath))
                {
                    return;
                }

                string rawFileName = BuildRawCompanionFileName(Path.GetFileName(targetEditedPath));
                string targetRawPath = Path.Combine(targetShapeDirectory, rawFileName);

                if (!IsSameFullPath(originalPath, targetRawPath))
                {
                    File.Copy(originalPath, targetRawPath, true);
                }

                CadShapeEditDocument targetDocument = CadShapeEditDocument.Load(targetEditedPath);
                targetDocument.OriginalSourcePath = rawFileName;
                targetDocument.Save(targetEditedPath);
            }
            catch
            {
                // 원본 동반 파일 복사 실패가 BarList 자체 저장을 차단하지 않도록 합니다.
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
                // CAD에서 가져온 원본 방향은 유지하고, 편집기에서 보정한 선·원호·문자 JSON을 연결합니다.
                // 형상번호는 업체·샵프로그램별 코드에 종속되지 않도록 비워둡니다.
                grid.Rows[rowIndex].Cells[columnIndex].Value = "";
                SetShapeMetaCellIfExists(rowIndex, new string[] { "형상번호", "OVIA_형상번호", "OVIA 형상번호", "OVIA형상번호" }, "");
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_형상치수", "OVIA 형상치수", "OVIA형상치수" }, picker.SelectedDimensionText);

                if (picker.SelectedCadShapeJsonPath != null && picker.SelectedCadShapeJsonPath.Trim() != "")
                {
                    // 철근형상 확인·수정은 같은 *_ovia_edit.json 파일을 다시 덮어쓸 수 있습니다.
                    // ERP 동기화가 과거 파일/동일 경로를 다시 읽지 않도록, "수정 적용" 시점마다
                    // 프로젝트 Shapes 폴더에 현재 편집 결과의 새 snapshot을 만들고 그 파일을 행에 연결합니다.
                    // 이렇게 하면 검토 후 저장 시 CSV의 OVIA_CAD_SHAPE_JSON 경로 자체도 변경되어
                    // 최신 편집 JSON이 ERP barlist_sync_push에 확실하게 전달됩니다.
                    string appliedCadShapePath = PersistAppliedCadShapeSnapshotForRow(
                        rowIndex,
                        picker.SelectedCadShapeJsonPath
                    );

                    SetShapeMetaCellIfExists(
                        rowIndex,
                        new string[] { "OVIA_CAD_SHAPE_JSON", "CAD_SHAPE_JSON", "OVIA CAD SHAPE JSON" },
                        appliedCadShapePath
                    );
                }

                string editedShapeSource = picker.SelectedShapeSource != null
                    && picker.SelectedShapeSource.Equals("MANUAL", StringComparison.OrdinalIgnoreCase)
                    ? "MANUAL"
                    : "CAD";
                string editedShapeStatus = editedShapeSource == "MANUAL" ? "MANUAL_EDITED" : "CAD_EDITED";
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_SOURCE", "SHAPE_SOURCE", "OVIA SHAPE SOURCE" }, editedShapeSource);
                SetShapeMetaCellIfExists(rowIndex, new string[] { "OVIA_SHAPE_STATUS", "SHAPE_STATUS", "OVIA SHAPE STATUS" }, editedShapeStatus);
                SetShapeDimensionColumnsIfExists(rowIndex, picker.SelectedDimensionText);
                lblStatus.Text = editedShapeSource == "MANUAL"
                    ? "직접 작성한 철근형상 수정사항을 적용했습니다. 검토 후 저장 시 ERP에 최신 형상이 반영됩니다."
                    : "CAD 철근형상 수정사항을 적용했습니다. 검토 후 저장 시 ERP에 최신 형상이 반영됩니다.";
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

        private string PersistAppliedCadShapeSnapshotForRow(int rowIndex, string editedJsonPath)
        {
            if (editedJsonPath == null || editedJsonPath.Trim() == "")
            {
                return "";
            }

            string sourcePath = ResolveCadShapeJsonPath(editedJsonPath);

            if (sourcePath == "" || !File.Exists(sourcePath))
            {
                return editedJsonPath.Trim();
            }

            try
            {
                string projectDirectory = GetProjectBarListDirectory();

                if (projectDirectory == null || projectDirectory.Trim() == "")
                {
                    return sourcePath;
                }

                string shapeDirectory = Path.Combine(projectDirectory, "Shapes");
                Directory.CreateDirectory(shapeDirectory);

                // 같은 행을 여러 번 수정해도 매번 다른 파일명이 되도록 현재 JSON 내용의 SHA-256과
                // 시각을 조합합니다. 경로가 바뀌므로 저장 상태/ERP 동기화에서 이전 형상과 혼동하지 않습니다.
                string contentHash = ComputeFileSha256Hex(sourcePath);
                string shortHash = contentHash.Length >= 12 ? contentHash.Substring(0, 12) : contentHash;
                string sourceBaseName = Path.GetFileNameWithoutExtension(sourcePath);

                if (sourceBaseName == null || sourceBaseName.Trim() == "")
                {
                    sourceBaseName = "shape";
                }

                // 누적 suffix가 길어지는 것을 방지합니다.
                sourceBaseName = Regex.Replace(
                    sourceBaseName,
                    @"_ovia_applied_\d{8}_\d{9}_[a-f0-9]{6,64}$",
                    "",
                    RegexOptions.IgnoreCase
                );

                string snapshotFileName =
                    sourceBaseName
                    + "_ovia_applied_"
                    + DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture)
                    + "_"
                    + shortHash
                    + ".json";

                string snapshotPath = Path.Combine(shapeDirectory, snapshotFileName);
                File.Copy(sourcePath, snapshotPath, true);

                // CAD 원본 복원용 companion도 함께 보존하고, snapshot 내부 originalSourcePath를
                // 해당 companion 파일명으로 다시 맞춥니다.
                CopyCadShapeOriginalCompanion(sourcePath, snapshotPath, shapeDirectory);

                // 저장 전 grid에서는 절대경로를 사용해 참조 파일 위치가 바뀌지 않도록 하고,
                // 검토 후 저장의 NormalizeCadShapeJsonFilesForSave에서 프로젝트 상대경로로 정리합니다.
                return snapshotPath;
            }
            catch
            {
                // snapshot 생성 실패가 기존 철근형상 수정 적용 자체를 막지 않도록 기존 편집 파일로 fallback합니다.
                return sourcePath;
            }
        }

        private string ComputeFileSha256Hex(string filePath)
        {
            if (filePath == null || filePath.Trim() == "" || !File.Exists(filePath))
            {
                return "";
            }

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder sb = new StringBuilder(hash.Length * 2);

                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
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

            text = text.Replace("\r", ";").Replace("\n", ";").Replace("|", ";");
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
            if (grid == null)
            {
                return;
            }

            ApplyUnitConversionAfterMapping(mappedTable, 0, grid.Rows.Count - 1);
        }

        private void ApplyUnitConversionAfterMapping(OviaBarListMappedTable mappedTable, int startRowIndex, int endRowIndex)
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

                ConvertColumnKgToTon(i, startRowIndex, endRowIndex);
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
            if (grid == null)
            {
                return;
            }

            ConvertColumnKgToTon(columnIndex, 0, grid.Rows.Count - 1);
        }

        private void ConvertColumnKgToTon(int columnIndex, int startRowIndex, int endRowIndex)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (startRowIndex < 0)
            {
                startRowIndex = 0;
            }

            if (endRowIndex >= grid.Rows.Count)
            {
                endRowIndex = grid.Rows.Count - 1;
            }

            int r;

            for (r = startRowIndex; r <= endRowIndex; r++)
            {
                if (r < 0 || r >= grid.Rows.Count || grid.Rows[r].IsNewRow)
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

            // OVIA 표준 표시 순서는 "부위 → 번호"입니다.
            // 원본 CAD/CSV의 실제 열 위치는 헤더 매핑으로 읽으므로 여기서는 화면 열 위치만 고정합니다.
            if (insertIndex < 0)
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

        private void EnsurePartAndNumberColumnOrder()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return;
            }

            int partColumnIndex = FindPartColumnIndex();
            int numberColumnIndex = FindNumberColumnIndex();

            if (partColumnIndex < 0 || numberColumnIndex < 0 || partColumnIndex == numberColumnIndex)
            {
                return;
            }

            DataGridViewColumn partColumn = grid.Columns[partColumnIndex];
            DataGridViewColumn numberColumn = grid.Columns[numberColumnIndex];

            // DisplayIndex만 조정하여 행 데이터/CSV 원본 인덱스와 CAD 추출 매핑은 건드리지 않습니다.
            // 숨김 OVIA 메타데이터 열이 있더라도 사용자에게 보이는 첫 두 표준 열은 항상 부위/번호입니다.
            if (partColumn.DisplayIndex != 0)
            {
                partColumn.DisplayIndex = 0;
            }

            if (numberColumn.DisplayIndex != 1)
            {
                numberColumn.DisplayIndex = 1;
            }
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

        private void RemoveDeprecatedShapeNumberColumns()
        {
            if (grid == null)
            {
                return;
            }

            for (int i = grid.Columns.Count - 1; i >= 0; i--)
            {
                string header = grid.Columns[i].HeaderText == null ? "" : grid.Columns[i].HeaderText.Trim();
                string name = grid.Columns[i].Name == null ? "" : grid.Columns[i].Name.Trim();

                if (header.Equals("형상번호", StringComparison.OrdinalIgnoreCase)
                    || header.Equals("형번", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("OVIA_ShapeNoVisible", StringComparison.OrdinalIgnoreCase))
                {
                    grid.Columns.RemoveAt(i);
                }
            }
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

        private bool IsBarListDetailVisibleColumn(DataGridViewColumn column)
        {
            if (column == null)
            {
                return false;
            }

            // 누락된 부위 컬럼을 화면용으로 보강한 컬럼은 상세 표준 컬럼으로 표시합니다.
            if (column.Name != null && column.Name.Equals("OVIA_PartVisible", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            OviaBarListMappedColumn mapped = column.Tag as OviaBarListMappedColumn;

            if (mapped != null && mapped.StandardKey != null && mapped.StandardKey.Trim() != "")
            {
                string key = mapped.StandardKey.Trim();

                return key.Equals("no", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("part", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("dia", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("shape", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("length_mm", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("qty_ea", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("total_length_m", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("weight_ton", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("remark", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("source_drawing_name", StringComparison.OrdinalIgnoreCase);
            }

            string normalized = NormalizeInternalColumnToken(column.HeaderText);

            // 저장된 일부 CSV에는 공사별 BarList 목록용 메타데이터가 함께 들어갈 수 있습니다.
            // 상세 그리드는 아래 10개 표준 컬럼만 표시하고, 그 밖의 컬럼은 데이터 보존용으로 숨깁니다.
            return normalized == "번호"
                || normalized == "부위"
                || normalized == "철근규격"
                || normalized == "철근형상"
                || normalized == "길이MM"
                || normalized == "수량EA"
                || normalized == "총길이M"
                || normalized == "중량TON"
                || normalized == "비고"
                || normalized == "원본도면";
        }

        private void ApplyGridColumnStyle()
        {
            int i;

            // 형상번호/형번은 업체별 임의 코드이므로 OVIA 표준 컬럼에서 사용하지 않습니다.
            // 사용자 화면은 부위, 번호, 철근규격, CAD 원본 철근형상을 중심으로 구성합니다.
            EnsurePartColumnExists();
            RemoveDeprecatedShapeNumberColumns();
            EnsurePartAndNumberColumnOrder();
            EnsureSourceDrawingColumnPosition();

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars = ScrollBars.Both;

            for (i = 0; i < grid.Columns.Count; i++)
            {
                string name = grid.Columns[i].HeaderText;

                if (!IsBarListDetailVisibleColumn(grid.Columns[i]))
                {
                    grid.Columns[i].Visible = false;
                    grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
                    continue;
                }

                grid.Columns[i].SortMode = IsBarListGridSortableHeader(name)
                    ? DataGridViewColumnSortMode.Programmatic
                    : DataGridViewColumnSortMode.NotSortable;
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
                    baseWidth = 190;
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
                    baseWidth = 190;
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
                else if (name != null && name.Trim().Equals("원본 도면", StringComparison.OrdinalIgnoreCase))
                {
                    baseWidth = 190;
                    grid.Columns[i].ReadOnly = true;
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

                if (IsRebarSpecDisplayHeader(name))
                {
                    grid.Columns[i].DefaultCellStyle.Font = new Font(
                        "맑은 고딕",
                        GetBarListBaseCellFontSize(),
                        FontStyle.Bold
                    );
                }
                else if (IsBarListNumericDisplayHeader(name))
                {
                    grid.Columns[i].DefaultCellStyle.Font = new Font(
                        "맑은 고딕",
                        GetBarListNumericCellFontSize(),
                        FontStyle.Regular
                    );
                }
            }

            int uniformRowHeight = GetUniformCadShapeRowHeight();
            grid.RowTemplate.Height = uniformRowHeight;

            int r;

            for (r = 0; r < grid.Rows.Count; r++)
            {
                if (!grid.Rows[r].IsNewRow)
                {
                    grid.Rows[r].Height = uniformRowHeight;
                }
            }

            UpdateBarListGridSortGlyph();
        }

        private int GetUniformCadShapeRowHeight()
        {
            int baseHeight = ScaleGridSize(GridBaseRowHeight);
            int maximumHeight = ScaleGridSize(92);

            if (grid == null || grid.Rows.Count == 0)
            {
                return baseHeight;
            }

            int uniformHeight = baseHeight;
            int rowIndex;

            for (rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                if (grid.Rows[rowIndex].IsNewRow)
                {
                    continue;
                }

                int candidate = GetRecommendedCadShapeRowHeight(rowIndex);

                if (candidate > uniformHeight)
                {
                    uniformHeight = candidate;
                }
            }

            if (uniformHeight > maximumHeight)
            {
                uniformHeight = maximumHeight;
            }

            return Math.Max(baseHeight, uniformHeight);
        }

        private int GetRecommendedCadShapeRowHeight(int rowIndex)
        {
            int baseHeight = ScaleGridSize(GridBaseRowHeight);
            int maximumHeight = ScaleGridSize(92);

            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return baseHeight;
            }

            string cadShapePath = ResolveCadShapeJsonPath(GetCadShapeJsonText(rowIndex));

            if (cadShapePath == "" || !File.Exists(cadShapePath))
            {
                return baseHeight;
            }

            return GetRecommendedCadShapeRowHeightFromJson(cadShapePath, baseHeight, maximumHeight);
        }

        private int GetRecommendedCadShapeRowHeightFromJson(string jsonPath, int baseHeight, int maximumHeight)
        {
            if (baseHeight <= 0)
            {
                baseHeight = ScaleGridSize(GridBaseRowHeight);
            }

            if (maximumHeight < baseHeight)
            {
                maximumHeight = baseHeight;
            }

            if (jsonPath == null || jsonPath.Trim() == "" || !File.Exists(jsonPath))
            {
                return baseHeight;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                int textCount = Regex.Matches(json, "\\\"type\\\"\\s*:\\s*\\\"TEXT\\\"", RegexOptions.IgnoreCase).Count;
                int lineCount = Regex.Matches(json, "\\\"type\\\"\\s*:\\s*\\\"(?:LINE|ARC|CIRCLE)\\\"", RegexOptions.IgnoreCase).Count;
                int recommended = baseHeight;

                if (textCount >= 8 || lineCount >= 120)
                {
                    recommended = (int)Math.Round(baseHeight * 1.72);
                }
                else if (textCount >= 5 || lineCount >= 70)
                {
                    recommended = (int)Math.Round(baseHeight * 1.45);
                }
                else if (textCount >= 3 || lineCount >= 35)
                {
                    recommended = (int)Math.Round(baseHeight * 1.23);
                }

                if (recommended > maximumHeight)
                {
                    recommended = maximumHeight;
                }

                return Math.Max(baseHeight, recommended);
            }
            catch
            {
                return baseHeight;
            }
        }

        private void EnsureSourceDrawingColumnPosition()
        {
            if (grid == null || grid.Columns.Count == 0)
            {
                return;
            }

            int remarkColumnIndex = FindExactColumnIndexByHeaders(new string[] { "비고" });
            int sourceDrawingColumnIndex = FindExactColumnIndexByHeaders(new string[] { "원본 도면" });

            if (remarkColumnIndex < 0 || sourceDrawingColumnIndex < 0)
            {
                return;
            }

            int desiredDisplayIndex = grid.Columns[remarkColumnIndex].DisplayIndex + 1;

            if (desiredDisplayIndex >= grid.Columns.Count)
            {
                desiredDisplayIndex = grid.Columns.Count - 1;
            }

            if (grid.Columns[sourceDrawingColumnIndex].DisplayIndex != desiredDisplayIndex)
            {
                grid.Columns[sourceDrawingColumnIndex].DisplayIndex = desiredDisplayIndex;
            }
        }

        private void ApplySourceDrawingToolTips(int startRowIndex, int endRowIndex)
        {
            if (grid == null || grid.Columns.Count == 0 || grid.Rows.Count == 0)
            {
                return;
            }

            int nameColumnIndex = FindExactColumnIndexByHeaders(new string[] { "원본 도면" });
            int pathColumnIndex = FindExactColumnIndexByHeaders(new string[] { "OVIA_원본도면경로" });

            if (nameColumnIndex < 0)
            {
                return;
            }

            if (startRowIndex < 0)
            {
                startRowIndex = 0;
            }

            if (endRowIndex >= grid.Rows.Count)
            {
                endRowIndex = grid.Rows.Count - 1;
            }

            int r;

            for (r = startRowIndex; r <= endRowIndex; r++)
            {
                if (r < 0 || r >= grid.Rows.Count || grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                string displayName = GetGridCellText(r, nameColumnIndex);
                string fullPath = pathColumnIndex >= 0 ? GetGridCellText(r, pathColumnIndex) : "";

                if (fullPath != "")
                {
                    grid.Rows[r].Cells[nameColumnIndex].ToolTipText = fullPath;
                }
                else if (displayName != "")
                {
                    grid.Rows[r].Cells[nameColumnIndex].ToolTipText = displayName;
                }
                else
                {
                    grid.Rows[r].Cells[nameColumnIndex].ToolTipText = "";
                }
            }
        }

        private string GetGridCellText(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            object value = grid.Rows[rowIndex].Cells[columnIndex].Value;
            return value == null ? "" : value.ToString().Trim();
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

        private bool IsRebarSpecDisplayHeader(string header)
        {
            string normalized = NormalizeInternalColumnToken(header);
            return normalized == "철근규격" || normalized == "규격";
        }

        private float GetBarListBaseCellFontSize()
        {
            if (grid != null && grid.DefaultCellStyle.Font != null)
            {
                return grid.DefaultCellStyle.Font.Size;
            }

            return ScaleGridFont(GridBaseCellFontSize);
        }

        private float GetBarListNumericCellFontSize()
        {
            float zoomScale = gridZoomPercent / 100F;
            return GetBarListBaseCellFontSize() + (GridNumericFontPixelIncreaseInPoints * zoomScale);
        }

        private bool IsBarListNumericDisplayHeader(string header)
        {
            string normalized = NormalizeInternalColumnToken(header);

            return normalized == "길이MM"
                || normalized == "길이"
                || normalized == "수량EA"
                || normalized == "수량"
                || normalized == "총길이M"
                || normalized == "총길이"
                || normalized == "중량TON"
                || normalized == "총중량TON"
                || normalized == "중량"
                || normalized == "총중량";
        }

        private bool IsTotalLengthDisplayHeader(string header)
        {
            string normalized = NormalizeInternalColumnToken(header);

            return normalized == "총길이M"
                || normalized == "총길이";
        }

        private string FormatBarListTotalLengthForDisplay(string text)
        {
            decimal value;

            if (!TryParseDecimalNumber(text, out value))
            {
                return FormatBarListNumberForDisplay(text);
            }

            decimal rounded = Decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            return rounded.ToString("#,0.00", CultureInfo.InvariantCulture);
        }

        private string FormatBarListNumberForDisplay(string text)
        {
            if (text == null)
            {
                return "";
            }

            string normalized = text.Trim().Replace(",", "").Replace(" ", "");

            if (!Regex.IsMatch(normalized, @"^[+-]?\d+(?:\.\d+)?$"))
            {
                return text;
            }

            string sign = "";

            if (normalized.StartsWith("+", StringComparison.Ordinal) || normalized.StartsWith("-", StringComparison.Ordinal))
            {
                sign = normalized.Substring(0, 1);
                normalized = normalized.Substring(1);
            }

            int decimalIndex = normalized.IndexOf('.');
            string integerPart = decimalIndex >= 0 ? normalized.Substring(0, decimalIndex) : normalized;
            string decimalPart = decimalIndex >= 0 ? normalized.Substring(decimalIndex) : "";

            integerPart = integerPart.TrimStart('0');

            if (integerPart == "")
            {
                integerPart = "0";
            }

            StringBuilder grouped = new StringBuilder();
            int i;

            for (i = 0; i < integerPart.Length; i++)
            {
                if (i > 0 && (integerPart.Length - i) % 3 == 0)
                {
                    grouped.Append(',');
                }

                grouped.Append(integerPart[i]);
            }

            return sign + grouped.ToString() + decimalPart;
        }

        private void RecalculateSummary()
        {
            int rowCount = 0;
            double totalQty = 0;
            double totalLength = 0;
            decimal totalWeight = 0M;

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
                    decimal rowWeight;

                    if (TryParseDecimalNumber(GetCellText(r, weightCol), out rowWeight))
                    {
                        totalWeight += rowWeight;
                    }
                }
            }

            lblRowCount.Text = rowCount.ToString("N0", CultureInfo.InvariantCulture);
            lblTotalQty.Text = totalQty.ToString("#,0.###", CultureInfo.InvariantCulture);
            lblTotalLength.Text = totalLength.ToString("#,0.00", CultureInfo.InvariantCulture);

            /*
             * 상단 중량 합계는 현재 OVIA 리스트의 중량(Ton) 셀 값을 그대로 합산합니다.
             * 행별 중량 셀은 기존 계산/검증 계약에 따라 소수 셋째 자리 값을 유지하며,
             * 화면에 표시된 각 행의 중량 합과 상단 카드 값이 항상 일치해야 합니다.
             */
            lblTotalWeight.Text = totalWeight.ToString("#,0.###", CultureInfo.InvariantCulture);

            RefreshProjectContextHeaderFromGrid();
            RefreshSummaryDrawerData();

            if (hasActiveSummaryFilter)
            {
                ApplyActiveSummaryFilter();
            }

            UpdateSelectionSummaryOverlay();
        }

        private void MarkUnsaved()
        {
            RefreshSaveStateFromCurrentGrid();
        }

        private void CaptureSavedGridBaseline()
        {
            ReindexLogicalRowOrder();
            savedGridBaseline = CaptureGridState();
            isSaved = true;

            string syncPath = savedProjectFilePath.Trim() != "" ? savedProjectFilePath : lastLoadedFilePath;
            if (syncPath.Trim() != ""
                && File.Exists(syncPath)
                && OviaErpBarListSyncService.IsSynchronizationPending(syncPath))
            {
                // ERP에 반영되지 않은 로컬 저장본은 별도 '재동기화' 기능을 노출하지 않고
                // 기존 '검토 후 저장' 액션으로 다시 저장/전송할 수 있도록 미저장 상태로 유지한다.
                isSaved = false;
            }

            UpdateSaveState();
        }

        private void RefreshSaveStateFromCurrentGrid()
        {
            isSaved = IsCurrentGridEquivalentToSavedBaseline();

            string syncPath = savedProjectFilePath.Trim() != "" ? savedProjectFilePath : lastLoadedFilePath;
            if (syncPath.Trim() != ""
                && File.Exists(syncPath)
                && OviaErpBarListSyncService.IsSynchronizationPending(syncPath))
            {
                isSaved = false;
            }

            UpdateSaveState();
        }

        private bool IsCurrentGridEquivalentToSavedBaseline()
        {
            if (savedGridBaseline == null)
            {
                return !HasGridData();
            }

            GridUndoSnapshot currentState = CaptureGridState();
            return AreGridStatesEquivalent(currentState, savedGridBaseline);
        }

        private bool AreGridStatesEquivalent(GridUndoSnapshot left, GridUndoSnapshot right)
        {
            if (left == null || right == null || left.Rows.Count != right.Rows.Count)
            {
                return false;
            }

            List<int> leftIndexes = GetGridStateLogicalIndexes(left);
            List<int> rightIndexes = GetGridStateLogicalIndexes(right);
            int i;

            for (i = 0; i < leftIndexes.Count; i++)
            {
                object[] leftRow = left.Rows[leftIndexes[i]];
                object[] rightRow = right.Rows[rightIndexes[i]];

                if (leftRow == null || rightRow == null || leftRow.Length != rightRow.Length)
                {
                    return false;
                }

                int c;

                for (c = 0; c < leftRow.Length; c++)
                {
                    string leftText = leftRow[c] == null ? "" : leftRow[c].ToString();
                    string rightText = rightRow[c] == null ? "" : rightRow[c].ToString();

                    if (!String.Equals(leftText, rightText, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                // 동일한 *_ovia_edit.json 경로를 덮어쓴 경우에도 실제 형상 내용 변경을 감지합니다.
                string leftShapeFingerprint = leftIndexes[i] < left.ShapeContentFingerprints.Count
                    ? left.ShapeContentFingerprints[leftIndexes[i]]
                    : "";
                string rightShapeFingerprint = rightIndexes[i] < right.ShapeContentFingerprints.Count
                    ? right.ShapeContentFingerprints[rightIndexes[i]]
                    : "";

                if (!String.Equals(leftShapeFingerprint, rightShapeFingerprint, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private List<int> GetGridStateLogicalIndexes(GridUndoSnapshot state)
        {
            List<int> indexes = new List<int>();
            int i;

            for (i = 0; i < state.Rows.Count; i++)
            {
                indexes.Add(i);
            }

            indexes.Sort(delegate(int leftIndex, int rightIndex)
            {
                long leftKey = leftIndex < state.RowOrderKeys.Count ? state.RowOrderKeys[leftIndex] : leftIndex + 1L;
                long rightKey = rightIndex < state.RowOrderKeys.Count ? state.RowOrderKeys[rightIndex] : rightIndex + 1L;
                int keyCompare = leftKey.CompareTo(rightKey);

                if (keyCompare != 0)
                {
                    return keyCompare;
                }

                return leftIndex.CompareTo(rightIndex);
            });

            return indexes;
        }

        private void EnsureLogicalRowOrderKeys()
        {
            if (grid == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow row = grid.Rows[i];

                if (row.IsNewRow)
                {
                    continue;
                }

                if (!logicalRowOrderKeys.ContainsKey(row))
                {
                    SetLogicalRowOrderKey(row, nextLogicalRowOrderKey);
                }
            }
        }

        private long GetLogicalRowOrderKey(DataGridViewRow row)
        {
            if (row == null)
            {
                return 0L;
            }

            long key;

            if (logicalRowOrderKeys.TryGetValue(row, out key))
            {
                return key;
            }

            object headerTag = row.HeaderCell == null ? null : row.HeaderCell.Tag;

            if (headerTag != null && Int64.TryParse(headerTag.ToString(), out key) && key > 0L)
            {
                SetLogicalRowOrderKey(row, key);
                return key;
            }

            key = nextLogicalRowOrderKey;
            SetLogicalRowOrderKey(row, key);
            return key;
        }

        private void SetLogicalRowOrderKey(DataGridViewRow row, long key)
        {
            if (row == null)
            {
                return;
            }

            if (key <= 0L)
            {
                key = nextLogicalRowOrderKey;
            }

            logicalRowOrderKeys[row] = key;

            if (row.HeaderCell != null)
            {
                row.HeaderCell.Tag = key;
            }

            if (key >= nextLogicalRowOrderKey)
            {
                nextLogicalRowOrderKey = key + 1L;
            }
        }

        private void ReindexLogicalRowOrder()
        {
            logicalRowOrderKeys.Clear();
            nextLogicalRowOrderKey = 1L;

            if (grid == null)
            {
                return;
            }

            int i;

            for (i = 0; i < grid.Rows.Count; i++)
            {
                if (!grid.Rows[i].IsNewRow)
                {
                    SetLogicalRowOrderKey(grid.Rows[i], nextLogicalRowOrderKey);
                }
            }
        }

        private void UpdateSaveState()
        {
            if (saveProjectButton == null)
            {
                return;
            }

            if (isSaved)
            {
                // 저장이 완료된 상태는 완료 표식일 뿐 추가 액션이 아니다.
                // AutoCAD 비활성 버튼과 같은 중립/비활성 외형으로 낮춰 사용자의 시선을 끌지 않는다.
                saveProjectButton.Enabled = false;
                saveProjectButton.UseDisabledAppearance = true;
                saveProjectButton.KeepCustomColorsWhenDisabled = false;
                saveProjectButton.Cursor = Cursors.Default;
                saveProjectButton.Text = "저장완료";
                saveProjectButton.UseCustomColors = false;
                saveProjectButton.StartColor = OviaFluentTheme.Accent;
                saveProjectButton.EndColor = OviaFluentTheme.Accent;
            }
            else
            {
                // 마지막 저장본과 실제 차이가 있을 때만 주 액션인 '검토 후 저장'을 다시 활성화한다.
                saveProjectButton.Enabled = true;
                saveProjectButton.UseDisabledAppearance = false;
                saveProjectButton.KeepCustomColorsWhenDisabled = false;
                saveProjectButton.Cursor = Cursors.Hand;
                saveProjectButton.Text = "검토 후 저장";
                saveProjectButton.UseCustomColors = false;
                saveProjectButton.StartColor = OviaFluentTheme.Accent;
                saveProjectButton.EndColor = OviaFluentTheme.Accent;
            }

            saveProjectButton.Invalidate();
        }

        private void RefreshProjectContextHeaderFromGrid()
        {
            if (projectContextHeader == null)
            {
                return;
            }

            string orderNumber = GetFirstNonEmptyGridValue(new string[] { "발주번호", "발주 번호" });
            string dueDate = NormalizeProjectHeaderDate(GetFirstNonEmptyGridValue(new string[] { "납기일", "납기 일자", "납기일자" }));
            string barListTitle = GetFirstNonEmptyGridValue(new string[] { "제목", "BarList 제목", "바리스트 제목" });

            if (barListTitle == "")
            {
                string sourcePath = savedProjectFilePath.Trim() != "" ? savedProjectFilePath : lastLoadedFilePath;
                if (sourcePath.Trim() == "")
                {
                    sourcePath = initialFilePath;
                }

                if (sourcePath.Trim() != "")
                {
                    barListTitle = Path.GetFileNameWithoutExtension(sourcePath);
                }
            }

            projectContextHeader.SetContext(projectNo, projectName, orderNumber, dueDate, barListTitle, clientName, projectStatus);
        }

        private string GetFirstNonEmptyGridValue(string[] headers)
        {
            if (grid == null || headers == null || headers.Length == 0)
            {
                return "";
            }

            int columnIndex = FindExactColumnIndexByHeaders(headers);
            if (columnIndex < 0)
            {
                return "";
            }

            int rowIndex;
            for (rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                if (grid.Rows[rowIndex].IsNewRow)
                {
                    continue;
                }

                string value = GetGridCellText(rowIndex, columnIndex);
                if (value != "")
                {
                    return value;
                }
            }

            return "";
        }

        private string NormalizeProjectHeaderDate(string value)
        {
            if (value == null || value.Trim() == "")
            {
                return "";
            }

            string text = value.Trim();
            DateTime date;
            string[] formats = new string[]
            {
                "yyyy-MM-dd",
                "yyyy.MM.dd",
                "yyyy/MM/dd",
                "yy-MM-dd",
                "yy.MM.dd",
                "yy/MM/dd",
                "MM-dd",
                "MM.dd",
                "MM/dd"
            };

            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            {
                return date.ToString("yyyy-MM-dd");
            }

            return text;
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
                List<DataGridViewColumn> orderedColumns = new List<DataGridViewColumn>();
                int i;

                for (i = 0; i < grid.Columns.Count; i++)
                {
                    orderedColumns.Add(grid.Columns[i]);
                }

                orderedColumns.Sort(delegate(DataGridViewColumn left, DataGridViewColumn right)
                {
                    return left.DisplayIndex.CompareTo(right.DisplayIndex);
                });

                for (i = 0; i < orderedColumns.Count; i++)
                {
                    if (i > 0)
                    {
                        writer.Write(",");
                    }

                    writer.Write(Csv(orderedColumns[i].HeaderText));
                }

                writer.WriteLine();

                int r;

                for (r = 0; r < grid.Rows.Count; r++)
                {
                    if (grid.Rows[r].IsNewRow)
                    {
                        continue;
                    }

                    for (i = 0; i < orderedColumns.Count; i++)
                    {
                        if (i > 0)
                        {
                            writer.Write(",");
                        }

                        object value = grid.Rows[r].Cells[orderedColumns[i].Index].Value;

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

        private void UpdateImportedTotalMetaFromUserEdit(int rowIndex, int columnIndex)
        {
            if (!IsCalculatedResultColumn(columnIndex) || rowIndex < 0 || grid == null || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            RebarCalculationCellMeta meta = cell.Tag as RebarCalculationCellMeta;

            if (meta == null)
            {
                meta = new RebarCalculationCellMeta();
                cell.Tag = meta;
            }

            object value = cell.Value;
            meta.OriginalImportedText = value == null ? "" : value.ToString();
        }

        private void ResetImportedCalculationMetaForRows(int startRowIndex, int endRowIndex)
        {
            if (grid == null || grid.Rows.Count == 0 || grid.Columns.Count == 0)
            {
                return;
            }

            if (startRowIndex < 0)
            {
                startRowIndex = 0;
            }

            if (endRowIndex >= grid.Rows.Count)
            {
                endRowIndex = grid.Rows.Count - 1;
            }

            int totalLengthCol = FindTotalLengthColumnIndex();
            int totalWeightCol = FindTotalWeightColumnIndex();
            int r;

            for (r = startRowIndex; r <= endRowIndex; r++)
            {
                if (r < 0 || r >= grid.Rows.Count || grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                ResetImportedCalculationMetaForCell(r, totalLengthCol);
                ResetImportedCalculationMetaForCell(r, totalWeightCol);
            }
        }

        private void ResetImportedCalculationMetaForCell(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0
                || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            RebarCalculationCellMeta meta = new RebarCalculationCellMeta();
            meta.OriginalImportedText = cell.Value == null ? "" : cell.Value.ToString().Trim();
            cell.Tag = meta;
            cell.ToolTipText = "";
        }

        private void ApplyRebarCalculationAndValidation(bool showMismatchMessage)
        {
            if (grid == null || grid.Columns.Count == 0 || grid.Rows.Count == 0 || isApplyingRebarCalculation)
            {
                return;
            }

            int specCol = FindRebarSpecColumnIndex();
            int lengthCol = FindSingleLengthColumnIndex();
            int qtyCol = FindQuantityColumnIndex();
            int totalLengthCol = FindTotalLengthColumnIndex();
            int totalWeightCol = FindTotalWeightColumnIndex();

            if (specCol < 0 || lengthCol < 0 || qtyCol < 0)
            {
                ClearRebarCalculationMismatchState();
                return;
            }

            bool hasResultColumn = totalLengthCol >= 0 || totalWeightCol >= 0;

            if (!hasResultColumn)
            {
                ClearRebarCalculationMismatchState();
                return;
            }

            Dictionary<string, RebarCalculationMismatchInfo> next = new Dictionary<string, RebarCalculationMismatchInfo>();
            bool mismatchFound = false;
            bool totalLengthMismatchFound = false;
            bool totalWeightMismatchFound = false;
            bool anyCalculated = false;

            isApplyingRebarCalculation = true;

            try
            {
                Dictionary<string, double> unitWeights = OviaRebarUnitWeightStore.LoadEnabledUnitWeights();
                bool importedTotalWeightUsesKilograms = ShouldCompareImportedTotalWeightAsKilograms(
                    specCol,
                    lengthCol,
                    qtyCol,
                    totalWeightCol,
                    unitWeights
                );
                int r;

                for (r = 0; r < grid.Rows.Count; r++)
                {
                    if (grid.Rows[r].IsNewRow)
                    {
                        continue;
                    }

                    string rawSpec = GetCellText(r, specCol);
                    string baseSpec = ExtractBaseRebarSpec(rawSpec);

                    if (baseSpec == "" || !unitWeights.ContainsKey(baseSpec))
                    {
                        ClearRebarCalculationCellState(r, totalLengthCol);
                        ClearRebarCalculationCellState(r, totalWeightCol);
                        continue;
                    }

                    double lengthMm;
                    double qty;

                    if (!TryParseNumber(GetCellText(r, lengthCol), out lengthMm) || !TryParseNumber(GetCellText(r, qtyCol), out qty))
                    {
                        ClearRebarCalculationCellState(r, totalLengthCol);
                        ClearRebarCalculationCellState(r, totalWeightCol);
                        continue;
                    }

                    if (lengthMm <= 0 || qty <= 0)
                    {
                        ClearRebarCalculationCellState(r, totalLengthCol);
                        ClearRebarCalculationCellState(r, totalWeightCol);
                        continue;
                    }

                    double unitWeightKgM = unitWeights[baseSpec];
                    double calculatedTotalLengthM = Math.Round((lengthMm / 1000.0) * qty, 3, MidpointRounding.AwayFromZero);
                    double calculatedTotalWeightTon = Math.Round((calculatedTotalLengthM * unitWeightKgM) / 1000.0, 3, MidpointRounding.AwayFromZero);

                    if (totalLengthCol >= 0)
                    {
                        string originalText = GetOriginalImportedTotalText(r, totalLengthCol);
                        bool mismatch = SetCalculatedCellValue(r, totalLengthCol, calculatedTotalLengthM, "총길이(M)", originalText, baseSpec, unitWeightKgM, false, next);
                        mismatchFound = mismatchFound || mismatch;
                        totalLengthMismatchFound = totalLengthMismatchFound || mismatch;
                        anyCalculated = true;
                    }

                    if (totalWeightCol >= 0)
                    {
                        string originalText = GetOriginalImportedTotalText(r, totalWeightCol);
                        bool rowWeightUsesKilograms = ShouldCompareImportedWeightRowAsKilograms(
                            originalText,
                            calculatedTotalWeightTon,
                            importedTotalWeightUsesKilograms
                        );
                        bool mismatch = SetCalculatedCellValue(r, totalWeightCol, calculatedTotalWeightTon, "총중량(Ton)", originalText, baseSpec, unitWeightKgM, rowWeightUsesKilograms, next);
                        mismatchFound = mismatchFound || mismatch;
                        totalWeightMismatchFound = totalWeightMismatchFound || mismatch;
                        anyCalculated = true;
                    }
                }
            }
            finally
            {
                isApplyingRebarCalculation = false;
            }

            /*
             * 최종 경고/색상 상태는 중간 bool이 아니라 실제로 남은 셀 상태를 다시 집계합니다.
             * CAD 원본과 OVIA 계산값이 소수 셋째 자리까지 같으면 여기서 불일치를 강제로 제거합니다.
             */
            PruneThreeDecimalMatches(next);
            RebuildRebarCalculationMismatchFlags(
                next,
                out mismatchFound,
                out totalLengthMismatchFound,
                out totalWeightMismatchFound
            );

            rebarCalculationMismatches = next;
            ClearResolvedRebarCalculationCellVisualStyles();
            ApplyCalculatedColumnReadOnlyState();
            grid.Invalidate();

            if (anyCalculated)
            {
                lblStatus.Text = "총길이/총중량은 OVIA 이형철근 단위중량표 기준으로 계산되었습니다.";
                lblStatus.ForeColor = mismatchFound ? OviaFluentTheme.Danger : TextSub;
            }

            if (showMismatchMessage && mismatchFound && !rebarMismatchWarningShown)
            {
                rebarMismatchWarningShown = true;
                string mismatchMessage;

                if (totalLengthMismatchFound && totalWeightMismatchFound)
                {
                    mismatchMessage = "총길이 또는 총중량 값이 다른 곳이 있습니다. 빨간색 셀의 CAD 원본값과 OVIA 계산값을 확인해주세요.";
                }
                else if (totalLengthMismatchFound)
                {
                    mismatchMessage = "총길이 값이 다른 곳이 있습니다. 빨간색 셀의 CAD 원본값과 OVIA 계산값을 확인해주세요.";
                }
                else
                {
                    mismatchMessage = "총중량 값이 다른 곳이 있습니다. 빨간색 셀의 CAD 원본값과 OVIA 계산값을 확인해주세요.";
                }

                MessageBox.Show(
                    mismatchMessage,
                    "OVIA 계산 검증",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void ClearRebarCalculationMismatchState()
        {
            rebarCalculationMismatches.Clear();
            if (grid != null)
            {
                ClearResolvedRebarCalculationCellVisualStyles();
                grid.Invalidate();
            }
        }

        private void ClearRebarCalculationCellState(int rowIndex, int columnIndex)
        {
            if (columnIndex < 0 || rowIndex < 0 || grid == null || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            RebarCalculationCellMeta meta = cell.Tag as RebarCalculationCellMeta;

            if (meta != null)
            {
                meta.HasMismatch = false;
            }

            cell.ToolTipText = "";
            ClearRebarCalculationCellVisualStyle(cell);
        }

        private bool SetCalculatedCellValue(int rowIndex, int columnIndex, double calculatedValue, string valueName, string originalText, string baseSpec, double unitWeightKgM, bool originalWeightUsesKilograms, Dictionary<string, RebarCalculationMismatchInfo> next)
        {
            if (columnIndex < 0 || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            RebarCalculationCellMeta meta = cell.Tag as RebarCalculationCellMeta;

            if (meta == null)
            {
                meta = new RebarCalculationCellMeta();
                meta.OriginalImportedText = originalText == null ? "" : originalText.Trim();
                cell.Tag = meta;
            }
            else if (meta.OriginalImportedText == null || meta.OriginalImportedText.Trim() == "")
            {
                meta.OriginalImportedText = originalText == null ? "" : originalText.Trim();
            }

            meta.OriginalWeightUsesKilograms = originalWeightUsesKilograms
                && valueName != null
                && valueName.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0;

            string calculatedDisplayText = GetThreeDecimalComparisonText(calculatedValue);
            string originalDisplayText = GetThreeDecimalComparisonText(meta.OriginalImportedText);
            bool mismatch = originalDisplayText != ""
                && !AreImportedAndCalculatedValuesEquivalent(
                    meta.OriginalImportedText,
                    calculatedDisplayText,
                    meta.OriginalWeightUsesKilograms
                );

            meta.CalculatedValue = calculatedValue;
            meta.HasMismatch = mismatch;
            meta.ValueName = valueName;
            meta.BaseSpec = baseSpec;
            meta.UnitWeightKgM = unitWeightKgM;

            cell.Value = calculatedDisplayText;
            SetOriginalValueForCell(rowIndex, columnIndex, cell.Value);

            if (mismatch)
            {
                string key = GetRebarCalculationCellKey(rowIndex, columnIndex);
                RebarCalculationMismatchInfo info = new RebarCalculationMismatchInfo();
                info.RowIndex = rowIndex;
                info.ColumnIndex = columnIndex;
                info.ValueName = valueName;
                info.OriginalText = meta.OriginalImportedText;
                info.CalculatedText = calculatedDisplayText;
                info.BaseSpec = baseSpec;
                info.UnitWeightKgM = unitWeightKgM;
                info.OriginalWeightUsesKilograms = meta.OriginalWeightUsesKilograms;
                next[key] = info;

                string mismatchOriginalText = info.OriginalText;

                if (info.OriginalWeightUsesKilograms)
                {
                    mismatchOriginalText = info.OriginalText
                        + " kg ("
                        + GetKilogramAsTonComparisonText(info.OriginalText)
                        + " Ton)";
                }

                cell.ToolTipText = "CAD 원본값: " + mismatchOriginalText + " / OVIA 계산값: " + info.CalculatedText + "\r\n" + baseSpec + " 단위중량: " + unitWeightKgM.ToString("0.000", CultureInfo.InvariantCulture) + " kg/m";
            }
            else
            {
                ClearRebarCalculationCellVisualStyle(cell);
                string originalTooltipText = originalDisplayText == "" ? meta.OriginalImportedText : originalDisplayText;

                if (meta.OriginalWeightUsesKilograms && originalDisplayText != "")
                {
                    originalTooltipText = meta.OriginalImportedText
                        + " kg ("
                        + GetKilogramAsTonComparisonText(meta.OriginalImportedText)
                        + " Ton)";
                }

                cell.ToolTipText = "CAD 원본값: " + originalTooltipText
                    + " / OVIA 계산값: " + calculatedDisplayText
                    + " / " + baseSpec + " 단위중량 "
                    + unitWeightKgM.ToString("0.000", CultureInfo.InvariantCulture) + " kg/m";
            }

            return mismatch;
        }

        private void PruneThreeDecimalMatches(Dictionary<string, RebarCalculationMismatchInfo> mismatches)
        {
            if (mismatches == null || mismatches.Count == 0)
            {
                return;
            }

            List<string> resolvedKeys = new List<string>();

            foreach (KeyValuePair<string, RebarCalculationMismatchInfo> pair in mismatches)
            {
                RebarCalculationMismatchInfo info = pair.Value;

                if (info == null)
                {
                    resolvedKeys.Add(pair.Key);
                    continue;
                }

                if (AreImportedAndCalculatedValuesEquivalent(
                    info.OriginalText,
                    info.CalculatedText,
                    info.OriginalWeightUsesKilograms
                ))
                {
                    resolvedKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < resolvedKeys.Count; i++)
            {
                mismatches.Remove(resolvedKeys[i]);
            }
        }

        private void RebuildRebarCalculationMismatchFlags(
            Dictionary<string, RebarCalculationMismatchInfo> mismatches,
            out bool mismatchFound,
            out bool totalLengthMismatchFound,
            out bool totalWeightMismatchFound)
        {
            mismatchFound = false;
            totalLengthMismatchFound = false;
            totalWeightMismatchFound = false;

            if (mismatches == null)
            {
                return;
            }

            foreach (KeyValuePair<string, RebarCalculationMismatchInfo> pair in mismatches)
            {
                RebarCalculationMismatchInfo info = pair.Value;

                if (info == null)
                {
                    continue;
                }

                mismatchFound = true;

                if (info.ValueName != null && info.ValueName.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    totalWeightMismatchFound = true;
                }
                else
                {
                    totalLengthMismatchFound = true;
                }
            }
        }

        private void ClearResolvedRebarCalculationCellVisualStyles()
        {
            if (grid == null)
            {
                return;
            }

            int totalLengthCol = FindTotalLengthColumnIndex();
            int totalWeightCol = FindTotalWeightColumnIndex();

            for (int r = 0; r < grid.Rows.Count; r++)
            {
                if (grid.Rows[r].IsNewRow)
                {
                    continue;
                }

                ClearResolvedRebarCalculationCellVisualStyle(r, totalLengthCol);
                ClearResolvedRebarCalculationCellVisualStyle(r, totalWeightCol);
            }
        }

        private void ClearResolvedRebarCalculationCellVisualStyle(int rowIndex, int columnIndex)
        {
            if (columnIndex < 0 || rowIndex < 0 || grid == null
                || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            if (!IsRebarCalculationMismatchCell(rowIndex, columnIndex))
            {
                ClearRebarCalculationCellVisualStyle(grid.Rows[rowIndex].Cells[columnIndex]);
            }
        }

        private void ClearRebarCalculationCellVisualStyle(DataGridViewCell cell)
        {
            if (cell == null)
            {
                return;
            }

            cell.Style.ForeColor = Color.Empty;
            cell.Style.SelectionForeColor = Color.Empty;
            cell.Style.BackColor = Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
        }

        private string GetOriginalImportedTotalText(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return "";
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            RebarCalculationCellMeta meta = cell.Tag as RebarCalculationCellMeta;

            if (meta != null && meta.OriginalImportedText != null && meta.OriginalImportedText.Trim() != "")
            {
                return meta.OriginalImportedText;
            }

            object value = cell.Value;
            return value == null ? "" : value.ToString();
        }

        private bool ShouldCompareImportedTotalWeightAsKilograms(
            int specColumnIndex,
            int lengthColumnIndex,
            int quantityColumnIndex,
            int totalWeightColumnIndex,
            Dictionary<string, double> unitWeights)
        {
            if (grid == null
                || totalWeightColumnIndex < 0
                || specColumnIndex < 0
                || lengthColumnIndex < 0
                || quantityColumnIndex < 0
                || unitWeights == null
                || unitWeights.Count == 0)
            {
                return false;
            }

            int directTonMatches = 0;
            int kilogramMatches = 0;
            int comparableRows = 0;
            decimal importedWeightSum = 0M;
            decimal calculatedWeightTonSum = 0M;

            for (int rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
            {
                if (grid.Rows[rowIndex].IsNewRow)
                {
                    continue;
                }

                string baseSpec = ExtractBaseRebarSpec(GetCellText(rowIndex, specColumnIndex));
                double unitWeightKgM;
                double lengthMm;
                double quantity;
                decimal importedWeight;

                if (baseSpec == ""
                    || !unitWeights.TryGetValue(baseSpec, out unitWeightKgM)
                    || !TryParseNumber(GetCellText(rowIndex, lengthColumnIndex), out lengthMm)
                    || !TryParseNumber(GetCellText(rowIndex, quantityColumnIndex), out quantity)
                    || !TryParseDecimalNumber(GetOriginalImportedTotalText(rowIndex, totalWeightColumnIndex), out importedWeight)
                    || lengthMm <= 0
                    || quantity <= 0)
                {
                    continue;
                }

                double calculatedTotalLengthM = Math.Round(
                    (lengthMm / 1000.0) * quantity,
                    3,
                    MidpointRounding.AwayFromZero
                );
                double calculatedTotalWeightTon = Math.Round(
                    (calculatedTotalLengthM * unitWeightKgM) / 1000.0,
                    3,
                    MidpointRounding.AwayFromZero
                );
                decimal calculatedWeightTon;

                if (!TryParseDecimalNumber(
                    GetThreeDecimalComparisonText(calculatedTotalWeightTon),
                    out calculatedWeightTon))
                {
                    continue;
                }

                decimal importedRounded = Decimal.Round(importedWeight, 3, MidpointRounding.AwayFromZero);
                decimal importedKgAsTonRounded = Decimal.Round(importedWeight / 1000M, 3, MidpointRounding.AwayFromZero);

                if (importedRounded == calculatedWeightTon)
                {
                    directTonMatches++;
                }

                if (importedKgAsTonRounded == calculatedWeightTon)
                {
                    kilogramMatches++;
                }

                comparableRows++;
                importedWeightSum += importedWeight;
                calculatedWeightTonSum += calculatedWeightTon;
            }

            if (comparableRows == 0)
            {
                return false;
            }

            decimal importedSumAsTon = Decimal.Round(importedWeightSum, 3, MidpointRounding.AwayFromZero);
            decimal importedKgSumAsTon = Decimal.Round(importedWeightSum / 1000M, 3, MidpointRounding.AwayFromZero);
            decimal calculatedSum = Decimal.Round(calculatedWeightTonSum, 3, MidpointRounding.AwayFromZero);
            bool directTotalMatches = importedSumAsTon == calculatedSum;
            bool kilogramTotalMatches = importedKgSumAsTon == calculatedSum;

            if (kilogramTotalMatches && !directTotalMatches)
            {
                return true;
            }

            return kilogramMatches > directTonMatches
                && kilogramMatches > 0
                && kilogramMatches * 2 >= comparableRows;
        }

        private bool ShouldCompareImportedWeightRowAsKilograms(
            string originalText,
            double calculatedWeightTon,
            bool fallbackToKilograms)
        {
            decimal originalValue;
            decimal calculatedValue;

            if (!TryParseDecimalNumber(originalText, out originalValue)
                || !TryParseDecimalNumber(
                    GetThreeDecimalComparisonText(calculatedWeightTon),
                    out calculatedValue))
            {
                return fallbackToKilograms;
            }

            decimal originalRounded = Decimal.Round(originalValue, 3, MidpointRounding.AwayFromZero);
            decimal originalKgAsTonRounded = Decimal.Round(originalValue / 1000M, 3, MidpointRounding.AwayFromZero);
            bool directTonMatches = originalRounded == calculatedValue;
            bool kilogramMatches = originalKgAsTonRounded == calculatedValue;

            if (kilogramMatches && !directTonMatches)
            {
                return true;
            }

            if (directTonMatches && !kilogramMatches)
            {
                return false;
            }

            return fallbackToKilograms;
        }

        private bool AreImportedAndCalculatedValuesEquivalent(
            string originalText,
            string calculatedText,
            bool originalWeightUsesKilograms)
        {
            decimal originalValue;
            decimal calculatedValue;

            if (!TryParseDecimalNumber(originalText, out originalValue)
                || !TryParseDecimalNumber(calculatedText, out calculatedValue))
            {
                return false;
            }

            decimal originalRounded = Decimal.Round(originalValue, 3, MidpointRounding.AwayFromZero);
            decimal calculatedRounded = Decimal.Round(calculatedValue, 3, MidpointRounding.AwayFromZero);

            if (originalRounded == calculatedRounded)
            {
                return true;
            }

            if (!originalWeightUsesKilograms)
            {
                return false;
            }

            decimal originalKgAsTonRounded = Decimal.Round(
                originalValue / 1000M,
                3,
                MidpointRounding.AwayFromZero
            );

            return originalKgAsTonRounded == calculatedRounded;
        }

        private string GetKilogramAsTonComparisonText(string originalText)
        {
            decimal originalValue;

            if (!TryParseDecimalNumber(originalText, out originalValue))
            {
                return "";
            }

            decimal tonValue = Decimal.Round(
                originalValue / 1000M,
                3,
                MidpointRounding.AwayFromZero
            );

            return tonValue.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private bool IsImportedValueDifferent(string originalText, double calculatedValue)
        {
            /*
             * CAD 원본값과 OVIA 계산값은 사용자가 보는 소수 셋째 자리 값을 기준으로 비교합니다.
             * 숫자 허용오차가 아니라 양쪽을 동일한 0.000 문자열로 정규화한 뒤 비교하므로
             * 화면에 0.252 / 0.252처럼 같게 보이는 값은 경고와 빨간 표시가 발생하지 않습니다.
             */
            string originalDisplayText = GetThreeDecimalComparisonText(originalText);

            if (originalDisplayText == "")
            {
                return false;
            }

            return !string.Equals(
                originalDisplayText,
                GetThreeDecimalComparisonText(calculatedValue),
                StringComparison.Ordinal
            );
        }

        private string GetThreeDecimalComparisonText(string text)
        {
            decimal value;

            if (!TryParseDecimalNumber(text, out value))
            {
                return "";
            }

            decimal rounded = Decimal.Round(value, 3, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private string GetThreeDecimalComparisonText(double value)
        {
            decimal decimalValue;

            if (!Decimal.TryParse(
                value.ToString("R", CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimalValue))
            {
                return value.ToString("0.000", CultureInfo.InvariantCulture);
            }

            decimal rounded = Decimal.Round(decimalValue, 3, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private bool TryParseDecimalNumber(string text, out decimal value)
        {
            value = 0M;

            if (text == null)
            {
                return false;
            }

            text = text.Trim().Replace(",", "").Replace(" ", "");

            if (text == "")
            {
                return false;
            }

            if (Decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return Decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        private void ApplyCalculatedColumnReadOnlyState()
        {
            if (grid == null)
            {
                return;
            }

            int totalLengthCol = FindTotalLengthColumnIndex();
            int totalWeightCol = FindTotalWeightColumnIndex();

            if (totalLengthCol >= 0)
            {
                grid.Columns[totalLengthCol].ReadOnly = false;
            }

            if (totalWeightCol >= 0)
            {
                grid.Columns[totalWeightCol].ReadOnly = false;
            }
        }

        private bool IsCalculatedResultColumn(int columnIndex)
        {
            if (columnIndex < 0)
            {
                return false;
            }

            return columnIndex == FindTotalLengthColumnIndex() || columnIndex == FindTotalWeightColumnIndex();
        }

        private bool IsRebarCalculationMismatchCell(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0
                || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return false;
            }

            RebarCalculationMismatchInfo info;

            if (!rebarCalculationMismatches.TryGetValue(GetRebarCalculationCellKey(rowIndex, columnIndex), out info)
                || info == null)
            {
                return false;
            }

            // 그리기 직전에도 CAD 원본 단위(Ton 또는 KG)를 반영하여 소수 셋째 자리로 재검증합니다.
            object currentValue = grid.Rows[rowIndex].Cells[columnIndex].Value;
            string current = currentValue == null ? "" : currentValue.ToString();

            return !AreImportedAndCalculatedValuesEquivalent(
                info.OriginalText,
                current,
                info.OriginalWeightUsesKilograms
            );
        }

        private string GetRebarCalculationCellKey(int rowIndex, int columnIndex)
        {
            return rowIndex.ToString(CultureInfo.InvariantCulture) + ":" + columnIndex.ToString(CultureInfo.InvariantCulture);
        }

        private void SetOriginalValueForCell(int rowIndex, int columnIndex, object value)
        {
            if (grid == null || rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            object[] originals = grid.Rows[rowIndex].Tag as object[];

            if (originals == null || originals.Length != grid.Columns.Count)
            {
                originals = CloneRowValues(grid.Rows[rowIndex]);
                grid.Rows[rowIndex].Tag = originals;
            }

            originals[columnIndex] = value == null ? "" : value.ToString();
        }

        private void PaintRebarCalculationMismatchCell(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected;
            Color backColor = selected ? Color.FromArgb(255, 226, 226) : Color.FromArgb(255, 241, 241);
            Color borderColor = grid == null ? OviaFluentTheme.GridLine : grid.GridColor;

            e.Handled = true;
            e.CellStyle.ForeColor = OviaFluentTheme.Danger;
            e.CellStyle.SelectionForeColor = OviaFluentTheme.Danger;
            e.CellStyle.BackColor = backColor;
            e.CellStyle.SelectionBackColor = backColor;

            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.CellBounds);
            }

            e.PaintContent(e.CellBounds);

            int bottom = e.CellBounds.Bottom - 1;
            int right = e.CellBounds.Right - 1;
            using (Pen pen = new Pen(borderColor, 1F))
            {
                // 계산 불일치 셀도 일반 셀과 동일하게 아래쪽 가로선만 한 번 그립니다.
                e.Graphics.DrawLine(pen, e.CellBounds.Left, bottom, right, bottom);
            }
        }

        private int FindRebarSpecColumnIndex()
        {
            int exact = FindExactColumnIndexByHeaders(new string[] { "철근규격", "철근 규격", "규격" });
            if (exact >= 0)
            {
                return exact;
            }

            return FindColumnIndex("규격");
        }

        private int FindSingleLengthColumnIndex()
        {
            int exact = FindExactColumnIndexByHeaders(new string[] { "길이(mm)", "길이", "Length", "LENGTH" });
            if (exact >= 0)
            {
                return exact;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = NormalizeHeaderText(grid.Columns[i].HeaderText);
                if (header.IndexOf("길이", StringComparison.OrdinalIgnoreCase) >= 0 && header.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindQuantityColumnIndex()
        {
            int exact = FindExactColumnIndexByHeaders(new string[] { "수량(EA)", "수량", "조립(EA)", "EA", "Qty", "Quantity" });
            if (exact >= 0)
            {
                return exact;
            }

            return FindColumnIndex("수량");
        }

        private int FindTotalLengthColumnIndex()
        {
            int exact = FindExactColumnIndexByHeaders(new string[] { "총길이(M)", "총길이", "총 길이", "TotalLength", "TOTAL_LENGTH" });
            if (exact >= 0)
            {
                return exact;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = NormalizeHeaderText(grid.Columns[i].HeaderText);
                if (header.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindTotalWeightColumnIndex()
        {
            int exact = FindExactColumnIndexByHeaders(new string[] { "총중량(Ton)", "중량(Ton)", "중량", "총중량", "TotalWeight", "TOTAL_WEIGHT" });
            if (exact >= 0)
            {
                return exact;
            }

            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                string header = NormalizeHeaderText(grid.Columns[i].HeaderText);
                if ((header.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0 || header.IndexOf("WEIGHT", StringComparison.OrdinalIgnoreCase) >= 0) && header.IndexOf("단위중량", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private string NormalizeHeaderText(string header)
        {
            if (header == null)
            {
                return "";
            }

            return header.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
        }

        private string ExtractBaseRebarSpec(string rawSpec)
        {
            if (rawSpec == null)
            {
                return "";
            }

            Match match = Regex.Match(rawSpec.ToUpperInvariant(), @"D\s*(\d+)");

            if (!match.Success)
            {
                return "";
            }

            return "D" + match.Groups[1].Value;
        }


        private class BarListCellClipboardData
        {
            public int SourceColumnIndex = -1;
            public string SourceColumnKey = "";
            public string SourceColumnTitle = "";
            public string SchemaKey = "";
            public List<BarListCellClipboardEntry> Entries = new List<BarListCellClipboardEntry>();
        }

        private class BarListCellClipboardEntry
        {
            public int SourceRowIndex = -1;
            public Dictionary<int, string> ValuesByColumn = new Dictionary<int, string>();
        }

        private class RebarCalculationCellMeta
        {
            public string OriginalImportedText = "";
            public double CalculatedValue = 0;
            public bool HasMismatch = false;
            public string ValueName = "";
            public string BaseSpec = "";
            public double UnitWeightKgM = 0;
            public bool OriginalWeightUsesKilograms = false;
        }

        private class RebarCalculationMismatchInfo
        {
            public int RowIndex = -1;
            public int ColumnIndex = -1;
            public string ValueName = "";
            public string OriginalText = "";
            public string CalculatedText = "";
            public string BaseSpec = "";
            public double UnitWeightKgM = 0;
            public bool OriginalWeightUsesKilograms = false;
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
        public List<long> RowOrderKeys = new List<long>();

        // 저장 상태 판정용 철근형상 JSON 내용 지문
        public List<string> ShapeContentFingerprints = new List<string>();

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
            OviaFluentTheme.ApplyButton(btnOk, OviaButtonRole.Primary);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(328, 108);
            btnCancel.Size = new Size(82, 30);
            OviaFluentTheme.ApplyButton(btnCancel, OviaButtonRole.Neutral);
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
        public bool CompactMode = false;

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

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, CompactMode ? 8 : 14))
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

    internal class BarListSummaryGroupInfo
    {
        public string DisplayName = "";
        public string RawValue = "";
        public int RowCount = 0;
        public double TotalQty = 0.0;
        public double TotalLength = 0.0;
        public decimal TotalWeight = 0M;
        public bool IsTotal = false;
    }

    internal class OtherBarListFileInfo
    {
        public string FilePath = "";
        public string FileName = "";
        public string ProjectDisplayName = "";
        public string Title = "";
        public string OrderNumber = "";
        public string DueDate = "";
        public string Author = "";
        public string SearchText = "";
        public int RowCount = 0;
        public int ErpBarListId = 0;
        public DateTime RegisteredDate = DateTime.MinValue;
        public DateTime LastWriteTime = DateTime.MinValue;
    }

    internal class OtherBarListPreviewShapeInfo
    {
        public string RawShapeText = "";
        public string DimensionText = "";
        public string CadShapeJsonPath = "";
        public string ShapeSource = "";
        public string ShapeStatus = "";
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
                "part",
                "no",
                "dia",
                "shape",
                "length_mm",
                "qty_ea",
                "total_length_m",
                "weight_ton",
                "remark",
                "source_drawing_name"
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

                if (col.Key.Equals("shape_no", StringComparison.OrdinalIgnoreCase))
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

                if (col.Key.Equals("shape_no", StringComparison.OrdinalIgnoreCase))
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
            store.Version = "built-in-2026.08.12.013";
            store.UpdatedAt = "2026-08-12";

            // OVIA BarList 고정 헤더 순서입니다.
            // 이 순서는 CAD 도면마다 헤더명이 달라도 화면/저장 기준으로 유지합니다.
            store.AddColumn("part", "부위", "text", 100, "부위", "위치", "층", "구간", "시공부위", "ZONE", "AREA", "LOCATION");
            store.AddColumn("no", "번호", "number_or_text", 100, "NO", "NO.", "No", "No.", "순번", "번호", "번", "부호", "부호번호", "기호", "ITEM");
            store.AddColumn("dia", "철근규격", "rebar_diameter", 100, "규격", "철근규격", "DIA", "D", "직경", "BAR DIA", "SIZE", "강종");
            store.AddColumn("shape", "철근형상", "text_or_image", 100, "형상", "형태", "철근형상", "SHAPE", "BENT", "BAR SHAPE", "절곡형상");
            store.AddColumn("length_mm", "길이(mm)", "number", 100, "길이", "L", "LENGTH", "절단길이", "산출길이", "MM", "길이MM", "길이(MM)");
            store.AddColumn("qty_ea", "수량(EA)", "number", 100, "수량", "개수", "갯수", "본수", "EA", "QTY", "QUANTITY", "수량EA", "수량(EA)");
            store.AddColumn("total_length_m", "총길이(M)", "number", 90, "총길이", "총연장", "연장", "TOTAL LENGTH", "T.L", "M", "총길이M", "총길이(M)");
            store.AddColumn("weight_ton", "중량(Ton)", "number_ton", 90, "중량", "총중량", "톤", "TON", "Ton", "ton", "WEIGHT", "WT", "TOTAL WEIGHT", "중량TON", "중량(TON)", "총중량TON", "총중량(TON)", "KG", "kg", "중량KG", "중량(KG)");
            store.AddColumn("remark", "비고", "text", 80, "비고", "REMARK", "NOTE", "메모", "특기사항", "비고사항");
            store.AddColumn("source_drawing_name", "원본 도면", "readonly_text", 80, "원본 도면", "원본도면", "도면 파일명", "도면파일명", "SOURCE DRAWING", "SOURCE DRAWING NAME", "DWG NAME");

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

    public class OviaBarListPinButton : Control
    {
        private bool hover;
        private bool pinned;

        public bool Pinned
        {
            get { return pinned; }
            set
            {
                if (pinned == value)
                {
                    return;
                }

                pinned = value;
                Invalidate();
            }
        }

        public OviaBarListPinButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            // WinForms base Control does not support transparent BackColor by default.
            // The parent background is painted explicitly in OnPaint(), so keep a safe opaque color here.
            BackColor = Color.White;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBack = Parent == null ? Color.White : Parent.BackColor;

            using (SolidBrush parentBrush = new SolidBrush(parentBack))
            {
                e.Graphics.FillRectangle(parentBrush, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fill = hover ? OviaFluentTheme.ButtonNeutralBackHover : Color.White;

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(OviaFluentTheme.ButtonNeutralBorder, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            Color iconColor = pinned ? OviaFluentTheme.Danger : Color.FromArgb(142, 148, 158);

            using (Font iconFont = OviaIconFont.Create(12.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "\uE718",
                    iconFont,
                    rect,
                    iconColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                );
            }

            base.OnPaint(e);
        }
    }

    public class OviaBarListButton : Control
    {
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.Accent;
        public bool UseCustomColors = false;
        public bool UseCustomTextColor = false;
        public Color CustomTextColor = OviaFluentTheme.ButtonNeutralText;
        public bool DropDownChevronUp = false;
        public bool UseDisabledAppearance = false;
        public bool KeepCustomColorsWhenDisabled = false;

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

            OVIA.Desktop.OviaButtonRole role = OviaFluentTheme.InferButtonRole(this.Text);
            Color fillColor = OviaFluentTheme.ButtonPrimaryBack;
            Color borderColor = OviaFluentTheme.ButtonPrimaryBorder;
            Color textColor = OviaFluentTheme.ButtonPrimaryText;

            if (UseCustomColors)
            {
                fillColor = hover ? Lighten(StartColor, 10) : StartColor;
                borderColor = StartColor;
                textColor = Color.White;
            }
            else if (role == OVIA.Desktop.OviaButtonRole.Danger)
            {
                fillColor = hover ? OviaFluentTheme.ButtonDangerBackHover : OviaFluentTheme.ButtonDangerBack;
                borderColor = OviaFluentTheme.ButtonDangerBorder;
                textColor = OviaFluentTheme.ButtonDangerText;
            }
            else if (role == OVIA.Desktop.OviaButtonRole.Neutral)
            {
                fillColor = hover ? OviaFluentTheme.ButtonNeutralBackHover : OviaFluentTheme.ButtonNeutralBack;
                borderColor = OviaFluentTheme.ButtonNeutralBorder;
                textColor = OviaFluentTheme.ButtonNeutralText;
            }
            else
            {
                fillColor = hover ? OviaFluentTheme.ButtonPrimaryBackHover : OviaFluentTheme.ButtonPrimaryBack;
            }

            if (UseCustomTextColor && this.Enabled)
            {
                textColor = CustomTextColor;
            }

            if (UseDisabledAppearance || (!this.Enabled && !KeepCustomColorsWhenDisabled))
            {
                fillColor = OviaFluentTheme.ButtonNeutralBack;
                borderColor = OviaFluentTheme.ButtonNeutralBorder;
                textColor = Color.FromArgb(156, 163, 175);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaBarListDrawHelper.RoundRect(rect, OviaFluentTheme.ButtonRadius))
            using (SolidBrush brush = new SolidBrush(fillColor))
            using (Pen pen = new Pen(borderColor, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            string rawText = this.Text == null ? "" : this.Text;
            bool hasDropDownChevron = rawText.IndexOf("\uE70D", StringComparison.Ordinal) >= 0;
            string displayText = hasDropDownChevron ? rawText.Replace("\uE70D", "").TrimEnd() : rawText;

            using (Font textFont = OviaFluentTheme.FontButton(OviaFluentTheme.ButtonFontSize, FontStyle.Bold))
            {
                if (!hasDropDownChevron)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        displayText,
                        textFont,
                        rect,
                        textColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
                    );
                }
                else
                {
                    Size textSize = TextRenderer.MeasureText(
                        e.Graphics,
                        displayText,
                        textFont,
                        new Size(Math.Max(1, rect.Width - 20), Math.Max(1, rect.Height)),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                    );
                    int chevronWidth = 7;
                    int gap = 6;
                    int totalWidth = Math.Min(rect.Width - 8, textSize.Width + gap + chevronWidth);
                    int startX = rect.Left + Math.Max(4, (rect.Width - totalWidth) / 2);
                    Rectangle textRect = new Rectangle(startX, rect.Top, Math.Max(1, Math.Min(textSize.Width + 2, rect.Right - startX - chevronWidth - gap)), rect.Height);

                    TextRenderer.DrawText(
                        e.Graphics,
                        displayText,
                        textFont,
                        textRect,
                        textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
                    );

                    int iconX = Math.Min(rect.Right - 10, startX + textSize.Width + gap);
                    int centerY = rect.Top + rect.Height / 2;

                    using (Pen chevronPen = new Pen(textColor, 1.35F))
                    {
                        chevronPen.StartCap = LineCap.Round;
                        chevronPen.EndCap = LineCap.Round;

                        if (DropDownChevronUp)
                        {
                            e.Graphics.DrawLine(chevronPen, iconX, centerY + 2, iconX + 3, centerY - 1);
                            e.Graphics.DrawLine(chevronPen, iconX + 6, centerY + 2, iconX + 3, centerY - 1);
                        }
                        else
                        {
                            e.Graphics.DrawLine(chevronPen, iconX, centerY - 1, iconX + 3, centerY + 2);
                            e.Graphics.DrawLine(chevronPen, iconX + 6, centerY - 1, iconX + 3, centerY + 2);
                        }
                    }
                }
            }

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
