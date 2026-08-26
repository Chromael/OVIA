using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal interface IOviaWorkspaceNavigator
    {
        string CurrentCompanyId { get; }
        string CurrentUserId { get; }

        void NavigateToMain();
        void NavigateToProjectManager();
        void NavigateToProjectRegisterWebErp();
        void NavigateToErpModulePage(string menuKey);
        void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus);
        void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath);
        void NavigateToBarListMapping();
        void NavigateToRebarUnitWeightTable();
        void NavigateToSystemSettings();
        void NavigateToBackupRestore();
        void NavigateToVersionInfo();
        void NavigateToWorkspaceInfoPage(string menuKey, string pathText, string title, string selectedMenu, string descriptionText, string bodyText);
        bool CanNavigateBackInWorkspace { get; }
        bool CanNavigateForwardInWorkspace { get; }
        bool CanNavigateUpInWorkspace { get; }
        bool NavigateBackInWorkspace();
        bool NavigateForwardInWorkspace();
        bool NavigateUpInWorkspace();
        void ShowAutoCadEnvironmentCheck();
        void ShowAutoCadExtractGuide();
        void RequestLogout();
    }

    internal interface IOviaWorkspaceScreen
    {
        bool CanLeaveWorkspaceScreen();
        void BeforeLeaveWorkspaceScreen();
    }

    internal interface IOviaWorkspaceUnsavedState
    {
        bool HasUnsavedWorkspaceData();
        string GetUnsavedWorkspaceDataName();
    }

    internal interface IOviaWorkspaceBrowserNavigation
    {
        bool CanNavigateBackInBrowser { get; }
        bool CanNavigateForwardInBrowser { get; }
        bool NavigateBackInBrowser();
        bool NavigateForwardInBrowser();
        bool RefreshBrowser();
    }

    internal static class OviaWorkspaceExitHelper
    {
        public static bool ConfirmSystemExit(IWin32Window owner, Form currentScreen)
        {
            string dataName = GetUnsavedDataName(currentScreen);
            bool hasUnsavedData = dataName != string.Empty;
            string message;

            if (hasUnsavedData)
            {
                message = "저장되지 않은 \"" + dataName + "\" 데이터가 있습니다.\r\n\r\n그래도 프로그램을 종료하시겠습니까?";
            }
            else
            {
                message = "프로그램을 종료하시겠습니까?";
            }

            return MessageBox.Show(
                owner,
                message,
                "OVIA 프로그램 종료",
                MessageBoxButtons.YesNo,
                hasUnsavedData ? MessageBoxIcon.Warning : MessageBoxIcon.Question
            ) == DialogResult.Yes;
        }

        public static bool ConfirmLogout(IWin32Window owner, Form currentScreen)
        {
            string dataName = GetUnsavedDataName(currentScreen);
            bool hasUnsavedData = dataName != string.Empty;
            string message;

            if (hasUnsavedData)
            {
                message = "저장되지 않은 \"" + dataName + "\" 데이터가 있습니다.\r\n\r\n그래도 프로그램을 종료하시겠습니까?";
            }
            else
            {
                message = "프로그램을 종료하시겠습니까?";
            }

            return MessageBox.Show(
                owner,
                message,
                "OVIA 프로그램 종료",
                MessageBoxButtons.YesNo,
                hasUnsavedData ? MessageBoxIcon.Warning : MessageBoxIcon.Question
            ) == DialogResult.Yes;
        }

        public static bool HasUnsavedData(Form currentScreen)
        {
            return GetUnsavedDataName(currentScreen) != string.Empty;
        }

        private static string GetUnsavedDataName(Form currentScreen)
        {
            IOviaWorkspaceUnsavedState unsavedState = currentScreen as IOviaWorkspaceUnsavedState;

            if (unsavedState == null)
            {
                return string.Empty;
            }

            if (!unsavedState.HasUnsavedWorkspaceData())
            {
                return string.Empty;
            }

            string dataName = unsavedState.GetUnsavedWorkspaceDataName();
            if (string.IsNullOrWhiteSpace(dataName))
            {
                return "현재 화면";
            }

            return dataName.Trim();
        }
    }

    internal interface IOviaWorkspaceLayout
    {
        void ApplyWorkspaceLayout();
    }

    internal static class OviaWorkspaceNavigation
    {
        public static IOviaWorkspaceNavigator FindNavigator(Control control)
        {
            Control current = control;

            while (current != null)
            {
                IOviaWorkspaceNavigator navigator = current as IOviaWorkspaceNavigator;

                if (navigator != null)
                {
                    return navigator;
                }

                current = current.Parent;
            }

            Form form = control == null ? null : control.FindForm();

            while (form != null)
            {
                IOviaWorkspaceNavigator navigator = form as IOviaWorkspaceNavigator;

                if (navigator != null)
                {
                    return navigator;
                }

                form = form.Owner;
            }

            return null;
        }

        public static IOviaWorkspaceBrowserNavigation FindBrowserNavigation(Control control)
        {
            Control current = control;

            while (current != null)
            {
                IOviaWorkspaceBrowserNavigation browserNavigation = current as IOviaWorkspaceBrowserNavigation;

                if (browserNavigation != null)
                {
                    return browserNavigation;
                }

                current = current.Parent;
            }

            Form form = control == null ? null : control.FindForm();

            while (form != null)
            {
                IOviaWorkspaceBrowserNavigation browserNavigation = form as IOviaWorkspaceBrowserNavigation;

                if (browserNavigation != null)
                {
                    return browserNavigation;
                }

                form = form.Owner;
            }

            return null;
        }
    }

    internal static class OviaWorkspaceCommandBar
    {
        private const int LegacyMenuTop = 48;
        private const int LegacyMenuHeight = 50;
        private static OviaAnimatedDropDownMenu currentSettingsDropDown;

        // 2026-08-14: 상단 대분류 메뉴 영역은 완전히 제거되었습니다.
        // 기존 개별 화면은 이 메서드를 호출하고 있으므로 호환 진입점은 유지하되,
        // 메뉴를 생성하지 않고 기존 50px 영역만 접어 화면 본문을 위로 당깁니다.
        public static void Populate(Control commandBar, string selectedMenu)
        {
            Populate(commandBar, selectedMenu, string.Empty, string.Empty);
        }

        public static void Populate(Control commandBar, string selectedMenu, string currentCompanyId, string currentUserId)
        {
            if (commandBar == null)
            {
                return;
            }

            commandBar.Controls.Clear();
            commandBar.Visible = false;
            commandBar.Enabled = false;
            commandBar.Height = 0;

            EventHandler parentChanged = null;
            parentChanged = delegate
            {
                Control parent = commandBar.Parent;
                if (parent == null || parent.IsDisposed)
                {
                    return;
                }

                commandBar.ParentChanged -= parentChanged;
                ScheduleLegacyMenuGapRemoval(parent, commandBar);
            };
            commandBar.ParentChanged += parentChanged;
        }

        private static void ScheduleLegacyMenuGapRemoval(Control parent, Control commandBar)
        {
            if (parent == null || parent.IsDisposed)
            {
                return;
            }

            if (!parent.IsHandleCreated)
            {
                EventHandler handleCreated = null;
                handleCreated = delegate
                {
                    parent.HandleCreated -= handleCreated;
                    ScheduleLegacyMenuGapRemoval(parent, commandBar);
                };
                parent.HandleCreated += handleCreated;
                return;
            }

            try
            {
                parent.BeginInvoke(new MethodInvoker(delegate
                {
                    CompactLegacyMenuGap(parent, commandBar);
                }));
            }
            catch
            {
                CompactLegacyMenuGap(parent, commandBar);
            }
        }

        private static void CompactLegacyMenuGap(Control parent, Control commandBar)
        {
            if (parent == null || parent.IsDisposed)
            {
                return;
            }

            int legacyBottom = LegacyMenuTop + LegacyMenuHeight;
            Control[] children = new Control[parent.Controls.Count];
            parent.Controls.CopyTo(children, 0);

            // 일부 화면은 공통 레이아웃 정책을 통해 이미 48px 경로영역 기준으로
            // 재배치되어 있습니다. 이 경우 다시 50px를 당기면 컨텐츠가 경로영역과
            // 겹치므로, 48~97px 구간에 실제 컨텐츠가 있으면 추가 이동을 생략합니다.
            bool headerOnlyLayoutAlreadyApplied = false;
            int i;
            for (i = 0; i < children.Length; i++)
            {
                Control probe = children[i];
                if (probe == null || probe == commandBar || probe.IsDisposed || !probe.Visible)
                {
                    continue;
                }

                if (probe.Top >= LegacyMenuTop && probe.Top < legacyBottom && probe.Height > 0)
                {
                    headerOnlyLayoutAlreadyApplied = true;
                    break;
                }
            }

            if (!headerOnlyLayoutAlreadyApplied)
            {
                for (i = 0; i < children.Length; i++)
                {
                    Control child = children[i];
                    if (child == null || child == commandBar || child.IsDisposed || child.Top < legacyBottom)
                    {
                        continue;
                    }

                    bool topAnchored = (child.Anchor & AnchorStyles.Top) == AnchorStyles.Top;
                    bool bottomAnchored = (child.Anchor & AnchorStyles.Bottom) == AnchorStyles.Bottom;

                    // 하단 전용 컨트롤은 기존 하단 여백을 유지하고,
                    // 상단 또는 고정 위치 컨트롤만 과거 50px 영역만큼 위로 이동합니다.
                    if (bottomAnchored && !topAnchored)
                    {
                        continue;
                    }

                    child.Top = Math.Max(LegacyMenuTop, child.Top - LegacyMenuHeight);

                    if (topAnchored && bottomAnchored)
                    {
                        child.Height += LegacyMenuHeight;
                    }
                }
            }

            if (commandBar != null && !commandBar.IsDisposed)
            {
                parent.Controls.Remove(commandBar);
                commandBar.Dispose();
            }
        }

        public static void CloseOpenDropDown()
        {
            if (currentSettingsDropDown != null && !currentSettingsDropDown.IsDisposed && currentSettingsDropDown.Visible)
            {
                currentSettingsDropDown.CloseAnimated();
                currentSettingsDropDown = null;
            }
        }

        public static void ToggleSettingsOptions(Control source)
        {
            if (source == null || source.IsDisposed)
            {
                return;
            }

            ToggleSettingsDropDown(source);
        }

        public static void OpenErpShortcut(Control source)
        {
            if (source == null || source.IsDisposed)
            {
                return;
            }

            CloseOpenDropDown();

            IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
            if (navigator != null)
            {
                navigator.NavigateToErpModulePage("ERP");
            }
        }

        private static void ToggleDropDown(Control menuButton, Action<OviaAnimatedDropDownMenu> buildItems)
        {
            if (menuButton == null || menuButton.IsDisposed)
            {
                return;
            }

            if (currentSettingsDropDown != null && !currentSettingsDropDown.IsDisposed && currentSettingsDropDown.Visible)
            {
                currentSettingsDropDown.CloseAnimated();
                currentSettingsDropDown = null;
                return;
            }

            OviaAnimatedDropDownMenu menu = new OviaAnimatedDropDownMenu();
            currentSettingsDropDown = menu;

            if (buildItems != null)
            {
                buildItems(menu);
            }

            menu.Closed += delegate
            {
                if (currentSettingsDropDown == menu)
                {
                    currentSettingsDropDown = null;
                }
            };

            menu.ShowBelow(menuButton);
        }

        private static void AddDropDownItem(OviaAnimatedDropDownMenu menu, Control source, string text, string iconText, Action<IOviaWorkspaceNavigator> action)
        {
            menu.AddItem(text, iconText, delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                menu.CloseImmediate();
                currentSettingsDropDown = null;

                if (navigator != null && action != null)
                {
                    action(navigator);
                }
            });
        }

        private static void ToggleSettingsDropDown(Control settingsButton)
        {
            ToggleDropDown(settingsButton, delegate(OviaAnimatedDropDownMenu menu)
            {

                AddDropDownItem(menu, settingsButton, "시스템 설정", "\uE713", delegate(IOviaWorkspaceNavigator navigator)
                {
                    navigator.NavigateToSystemSettings();
                });

                AddDropDownItem(menu, settingsButton, "BarList 항목 매핑", "\uE8A5", delegate(IOviaWorkspaceNavigator navigator)
                {
                    navigator.NavigateToBarListMapping();
                });

                AddDropDownItem(menu, settingsButton, "이형철근 단위중량표", "\uE9D9", delegate(IOviaWorkspaceNavigator navigator)
                {
                    navigator.NavigateToRebarUnitWeightTable();
                });

                AddDropDownItem(menu, settingsButton, "백업/복원", "\uE74E", delegate(IOviaWorkspaceNavigator navigator)
                {
                    navigator.NavigateToBackupRestore();
                });

                AddDropDownItem(menu, settingsButton, "버전정보", "\uE946", delegate(IOviaWorkspaceNavigator navigator)
                {
                    navigator.NavigateToVersionInfo();
                });
            });
        }
    }

    internal class OviaAnimatedDropDownMenu : Panel, IMessageFilter
    {
        private readonly Timer animationTimer;
        private readonly int itemHeight = 38;
        private readonly int verticalPadding = 8;
        private readonly int menuWidth = 226;
        private int targetHeight;
        private bool opening;
        private Control anchorControl;
        private bool filterAttached;

        public event EventHandler Closed;

        public OviaAnimatedDropDownMenu()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Visible = false;
            this.Size = new Size(menuWidth, 0);
            this.Padding = new Padding(6, verticalPadding, 6, verticalPadding);

            animationTimer = new Timer();
            animationTimer.Interval = 12;
            animationTimer.Tick += AnimationTimer_Tick;
        }

        public void AddItem(string text, string iconText, Action action)
        {
            OviaDropDownMenuItem item = new OviaDropDownMenuItem();
            item.Text = text;
            item.IconText = iconText;
            item.Action = action;
            item.Size = new Size(menuWidth - 12, itemHeight);
            item.Location = new Point(6, verticalPadding + this.Controls.Count * itemHeight);
            this.Controls.Add(item);

            targetHeight = verticalPadding * 2 + this.Controls.Count * itemHeight;
        }

        public void ShowBelow(Control anchor)
        {
            if (anchor == null || anchor.FindForm() == null)
            {
                return;
            }

            anchorControl = anchor;
            Form form = anchor.FindForm();

            if (this.Parent != form)
            {
                if (this.Parent != null)
                {
                    this.Parent.Controls.Remove(this);
                }

                form.Controls.Add(this);
            }

            Point screenPoint = anchor.PointToScreen(new Point(0, anchor.Height + 4));
            Point formPoint = form.PointToClient(screenPoint);
            int left = formPoint.X;

            if (left + menuWidth > form.ClientSize.Width - 12)
            {
                left = Math.Max(12, form.ClientSize.Width - menuWidth - 12);
            }

            this.Location = new Point(left, formPoint.Y);
            this.Width = menuWidth;
            this.Height = 0;
            this.Visible = true;
            this.BringToFront();

            ApplyRoundedRegion();
            AttachFilter();

            opening = true;
            animationTimer.Start();
        }

        public void CloseAnimated()
        {
            if (this.IsDisposed)
            {
                return;
            }

            opening = false;
            animationTimer.Start();
        }

        public void CloseImmediate()
        {
            animationTimer.Stop();
            DetachFilter();
            this.Visible = false;
            OnClosed();
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            int step = 22;

            if (opening)
            {
                this.Height = Math.Min(targetHeight, this.Height + step);
                ApplyRoundedRegion();

                if (this.Height >= targetHeight)
                {
                    animationTimer.Stop();
                    this.Height = targetHeight;
                    ApplyRoundedRegion();
                }
            }
            else
            {
                this.Height = Math.Max(0, this.Height - step);
                ApplyRoundedRegion();

                if (this.Height <= 0)
                {
                    animationTimer.Stop();
                    DetachFilter();
                    this.Visible = false;
                    OnClosed();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Math.Max(1, this.Width - 1), Math.Max(1, this.Height - 1));

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 7))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen border = new Pen(Color.FromArgb(218, 223, 230), 1))
                {
                    e.Graphics.DrawPath(border, path);
                }
            }

            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            if (this.Width <= 0 || this.Height <= 0)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 7))
            {
                this.Region = new Region(path);
            }
        }

        private void AttachFilter()
        {
            if (!filterAttached)
            {
                Application.AddMessageFilter(this);
                filterAttached = true;
            }
        }

        private void DetachFilter()
        {
            if (filterAttached)
            {
                Application.RemoveMessageFilter(this);
                filterAttached = false;
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WmLButtonDown = 0x0201;
            const int WmRButtonDown = 0x0204;
            const int WmMButtonDown = 0x0207;

            if (m.Msg != WmLButtonDown && m.Msg != WmRButtonDown && m.Msg != WmMButtonDown)
            {
                return false;
            }

            if (!this.Visible)
            {
                return false;
            }

            Point mouse = Control.MousePosition;
            Rectangle menuRect = this.RectangleToScreen(this.ClientRectangle);
            Rectangle anchorRect = anchorControl == null
                ? Rectangle.Empty
                : anchorControl.RectangleToScreen(anchorControl.ClientRectangle);

            if (!menuRect.Contains(mouse) && !anchorRect.Contains(mouse))
            {
                CloseAnimated();
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachFilter();

                if (animationTimer != null)
                {
                    animationTimer.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void OnClosed()
        {
            EventHandler handler = Closed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    internal class OviaDropDownMenuItem : Control
    {
        public string IconText = "";
        public Action Action;
        private bool hover;

        public OviaDropDownMenuItem()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
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

        protected override void OnClick(EventArgs e)
        {
            if (Action != null)
            {
                Action();
            }

            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle hoverRect = new Rectangle(4, 3, this.Width - 8, this.Height - 6);

            if (hover)
            {
                using (GraphicsPath path = MainDrawHelper.RoundRect(hoverRect, 5))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(243, 244, 246)))
                {
                    e.Graphics.FillPath(fill, path);
                }
            }

            Color iconColor = Color.FromArgb(96, 104, 116);
            Color textColor = OviaFluentTheme.TextPrimary;

            using (Font iconFont = new Font("Segoe MDL2 Assets", 12.5F, FontStyle.Regular))
            using (Font textFont = OviaFluentTheme.FontButton(9.2F, FontStyle.Regular))
            {
                Rectangle iconRect = new Rectangle(15, 0, 22, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    IconText,
                    iconFont,
                    iconRect,
                    iconColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                );

                Rectangle textRect = new Rectangle(47, 0, this.Width - 56, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this.Text,
                    textFont,
                    textRect,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                );
            }

            base.OnPaint(e);
        }
    }

    internal class OviaExplorerDropDownRenderer : ToolStripProfessionalRenderer
    {
        public OviaExplorerDropDownRenderer()
            : base(new OviaExplorerDropDownColorTable())
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                Rectangle rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }

    internal class OviaExplorerDropDownColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return OviaFluentTheme.NavigationHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return OviaFluentTheme.NavigationHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return OviaFluentTheme.NavigationHover; } }
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
    }

    internal class OviaPathEditExitFilter : IMessageFilter
    {
        private const int WmLButtonDown = 0x0201;
        private static OviaPathEditExitFilter current;

        private readonly LinkLabel breadcrumb;
        private readonly TextBox textBox;

        private OviaPathEditExitFilter(LinkLabel breadcrumb, TextBox textBox)
        {
            this.breadcrumb = breadcrumb;
            this.textBox = textBox;
        }

        public static void Attach(LinkLabel breadcrumb, TextBox textBox)
        {
            Detach();

            if (breadcrumb == null || textBox == null)
            {
                return;
            }

            current = new OviaPathEditExitFilter(breadcrumb, textBox);
            Application.AddMessageFilter(current);
        }

        public static void Detach()
        {
            if (current != null)
            {
                Application.RemoveMessageFilter(current);
                current = null;
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmLButtonDown)
            {
                return false;
            }

            if (textBox == null || textBox.IsDisposed || !textBox.Visible)
            {
                Detach();
                return false;
            }

            Rectangle textBounds = textBox.RectangleToScreen(textBox.ClientRectangle);

            if (textBounds.Contains(Control.MousePosition))
            {
                return false;
            }

            textBox.Visible = false;

            if (breadcrumb != null && !breadcrumb.IsDisposed)
            {
                breadcrumb.Visible = true;
                breadcrumb.BringToFront();
            }

            Detach();
            return false;
        }
    }

    public class FrmWorkspaceShell : Form, IOviaWorkspaceNavigator
    {
        private readonly string companyId;
        private readonly string userId;
        private Panel hostPanel;
        private Form currentScreen;
        private OviaWindowCaptionTheme captionTheme;
        private bool systemExitConfirmed;
        private bool navigateToMainClose;
        private readonly Stack<OviaWorkspaceNavigationEntry> backHistory = new Stack<OviaWorkspaceNavigationEntry>();
        private readonly Stack<OviaWorkspaceNavigationEntry> forwardHistory = new Stack<OviaWorkspaceNavigationEntry>();
        private OviaWorkspaceNavigationEntry currentNavigationEntry;
        private bool suppressNavigationHistory;

        private sealed class OviaWorkspaceNavigationEntry
        {
            public OviaWorkspaceNavigationEntry(string kind, params string[] values)
            {
                Kind = kind == null ? string.Empty : kind.Trim().ToUpperInvariant();
                Values = values == null ? new string[0] : values;
            }

            public string Kind { get; private set; }
            public string[] Values { get; private set; }

            public string Get(int index)
            {
                if (Values == null || index < 0 || index >= Values.Length || Values[index] == null)
                {
                    return string.Empty;
                }

                return Values[index];
            }
        }

        public string CurrentCompanyId
        {
            get { return companyId; }
        }

        public string CurrentUserId
        {
            get { return userId; }
        }

        public FrmWorkspaceShell(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;

            BuildUI();
            NavigateToProjectManager();
        }

        private void BuildUI()
        {
            OviaFluentTheme.ApplyForm(this);

            this.Text = "OVIA 공사관리";
            this.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1240, 760);
            this.MinimumSize = new Size(1100, 750);
            this.BackColor = OviaFluentTheme.AppBackground;
            this.FormClosing += FrmWorkspaceShell_FormClosing;
            captionTheme = OviaWindowCaptionTheme.Attach(this);

            hostPanel = new Panel();
            hostPanel.Dock = DockStyle.Fill;
            hostPanel.BackColor = OviaFluentTheme.AppBackground;
            hostPanel.Resize += HostPanel_Resize;
            this.Controls.Add(hostPanel);
        }

        private void HostPanel_Resize(object sender, EventArgs e)
        {
            FillCurrentWorkspaceScreen();
        }

        private void FillCurrentWorkspaceScreen()
        {
            if (currentScreen == null || currentScreen.IsDisposed || hostPanel == null)
            {
                return;
            }

            Rectangle bounds = hostPanel.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            currentScreen.Dock = DockStyle.Fill;
            currentScreen.Location = Point.Empty;
            currentScreen.Size = bounds.Size;
            ApplyWorkspaceLayout(currentScreen);
        }

        public void NavigateToMain()
        {
            NavigateToProjectManager();
        }

        public bool CanNavigateBackInWorkspace
        {
            get { return backHistory.Count > 0; }
        }

        public bool CanNavigateForwardInWorkspace
        {
            get { return forwardHistory.Count > 0; }
        }

        public bool CanNavigateUpInWorkspace
        {
            get { return ResolveParentNavigationEntry(currentNavigationEntry) != null; }
        }

        public bool NavigateBackInWorkspace()
        {
            if (backHistory.Count == 0)
            {
                NavigateToMain();
                return true;
            }

            OviaWorkspaceNavigationEntry entry = backHistory.Pop();
            OviaWorkspaceNavigationEntry forwardEntry = currentNavigationEntry;
            bool oldSuppress = suppressNavigationHistory;
            suppressNavigationHistory = true;

            try
            {
                bool navigated = NavigateToStoredEntry(entry);
                if (navigated && forwardEntry != null)
                {
                    forwardHistory.Push(forwardEntry);
                }

                if (navigated)
                {
                    RefreshCurrentWorkspaceNavigationState();
                }

                return navigated;
            }
            finally
            {
                suppressNavigationHistory = oldSuppress;
            }
        }

        public bool NavigateForwardInWorkspace()
        {
            if (forwardHistory.Count == 0)
            {
                return false;
            }

            OviaWorkspaceNavigationEntry entry = forwardHistory.Pop();
            OviaWorkspaceNavigationEntry backEntry = currentNavigationEntry;
            bool oldSuppress = suppressNavigationHistory;
            suppressNavigationHistory = true;

            try
            {
                bool navigated = NavigateToStoredEntry(entry);
                if (navigated && backEntry != null)
                {
                    backHistory.Push(backEntry);
                }

                if (navigated)
                {
                    RefreshCurrentWorkspaceNavigationState();
                }

                return navigated;
            }
            finally
            {
                suppressNavigationHistory = oldSuppress;
            }
        }

        public bool NavigateUpInWorkspace()
        {
            OviaWorkspaceNavigationEntry parentEntry = ResolveParentNavigationEntry(currentNavigationEntry);

            if (parentEntry == null)
            {
                NavigateToMain();
                return true;
            }

            return NavigateToStoredEntry(parentEntry);
        }

        public void NavigateToProjectManager()
        {
            ShowScreenWithHistory(
                new FrmProjectManager(companyId, userId),
                "OVIA 공사목록",
                new OviaWorkspaceNavigationEntry("PROJECT_MANAGER")
            );
        }

        public void NavigateToProjectRegisterWebErp()
        {
            ShowScreenWithHistory(
                new FrmOviaWebErpPage(
                    companyId,
                    userId,
                    "PROJECT_REGISTER",
                    "공사등록",
                    "메인  ›  공사관리  ›  공사등록",
                    "PROJECT",
                    "projects/register",
                    "ERP 공사등록 페이지를 WebView2로 불러옵니다. Web ERP에서 공사를 등록하면 공사목록에 표시되는 구조로 전환합니다."
                ),
                "OVIA 공사등록",
                new OviaWorkspaceNavigationEntry("PROJECT_REGISTER")
            );
        }


        public void NavigateToErpModulePage(string menuKey)
        {
            string key = string.IsNullOrWhiteSpace(menuKey) ? "ERP" : menuKey.Trim();
            string title = key == "ERP" ? "ERP" : "ERP 모듈";
            string selected = key == "ERP" ? "ERP" : string.Empty;
            string path = key == "ERP"
                ? "메인  ›  ERP"
                : "메인  ›  " + title;

            ShowScreenWithHistory(
                new FrmOviaWebErpPage(
                    companyId,
                    userId,
                    key,
                    title,
                    path,
                    selected,
                    string.Empty,
                    title + " ERP 모듈 페이지를 WebView2로 불러옵니다."
                ),
                "OVIA " + title,
                new OviaWorkspaceNavigationEntry("ERP_MODULE_PAGE", key)
            );
        }

        public void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus)
        {
            ShowScreenWithHistory(
                new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus),
                "OVIA 공사별 BarList",
                new OviaWorkspaceNavigationEntry("PROJECT_BARLIST_LIST", projectNo, projectName, clientName, projectStatus)
            );
        }

        public void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath)
        {
            string filePath = initialFilePath == null ? "" : initialFilePath;
            ShowScreenWithHistory(
                new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath),
                filePath.Trim() == "" ? "OVIA 신규 BarList 등록" : "OVIA BarList",
                new OviaWorkspaceNavigationEntry("BARLIST", projectNo, projectName, clientName, projectStatus, filePath)
            );
        }

        public void NavigateToBarListMapping()
        {
ShowScreenWithHistory(
                new FrmBarListMappingManager(companyId, userId),
                "OVIA BarList 항목 매핑",
                new OviaWorkspaceNavigationEntry("BARLIST_MAPPING")
            );
        }

        public void NavigateToRebarUnitWeightTable()
        {
ShowScreenWithHistory(
                new FrmRebarUnitWeightTable(companyId, userId),
                "OVIA 이형철근 단위중량표",
                new OviaWorkspaceNavigationEntry("REBAR_UNIT_WEIGHT")
            );
        }

        public void NavigateToSystemSettings()
        {
ShowScreenWithHistory(
                new FrmSystemSettings(companyId, userId),
                "OVIA 시스템 설정",
                new OviaWorkspaceNavigationEntry("SYSTEM_SETTINGS")
            );
        }

        public void NavigateToBackupRestore()
        {
            ShowScreenWithHistory(
                new FrmBackupRestore(companyId, userId),
                "OVIA 백업/복원",
                new OviaWorkspaceNavigationEntry("BACKUP_RESTORE")
            );
        }

        public void NavigateToVersionInfo()
        {
ShowScreenWithHistory(
                new FrmVersionInfo(companyId, userId),
                "OVIA 버전정보",
                new OviaWorkspaceNavigationEntry("VERSION_INFO")
            );
        }

        public void NavigateToWorkspaceInfoPage(string menuKey, string pathText, string title, string selectedMenu, string descriptionText, string bodyText)
        {
            string displayTitle = string.IsNullOrWhiteSpace(title) ? "화면" : title.Trim();
ShowScreenWithHistory(
                new FrmOviaMenuPage(companyId, userId, menuKey, displayTitle, pathText, selectedMenu, descriptionText, bodyText),
                "OVIA " + displayTitle,
                new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", menuKey, pathText, displayTitle, selectedMenu, descriptionText, bodyText)
            );
        }

        private bool NavigateToStoredEntry(OviaWorkspaceNavigationEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            switch (entry.Kind)
            {
                case "PROJECT_MANAGER":
                    NavigateToProjectManager();
                    return true;
                case "PROJECT_REGISTER":
                    NavigateToProjectRegisterWebErp();
                    return true;
                case "ERP_MODULE_PAGE":
                    NavigateToErpModulePage(entry.Get(0));
                    return true;
                case "PROJECT_BARLIST_LIST":
                    NavigateToProjectBarListList(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3));
                    return true;
                case "BARLIST":
                    NavigateToBarList(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3), entry.Get(4));
                    return true;
                case "BARLIST_MAPPING":
                    NavigateToBarListMapping();
                    return true;
                case "REBAR_UNIT_WEIGHT":
                    NavigateToRebarUnitWeightTable();
                    return true;
                case "SYSTEM_SETTINGS":
                    NavigateToSystemSettings();
                    return true;
                case "BACKUP_RESTORE":
                    NavigateToBackupRestore();
                    return true;
                case "VERSION_INFO":
                    NavigateToVersionInfo();
                    return true;
                case "WORKSPACE_INFO":
                    NavigateToWorkspaceInfoPage(entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3), entry.Get(4), entry.Get(5));
                    return true;
                default:
                    return false;
            }
        }

        private OviaWorkspaceNavigationEntry ResolveParentNavigationEntry(OviaWorkspaceNavigationEntry entry)
        {
            if (entry == null || entry.Kind == "")
            {
                return null;
            }

            switch (entry.Kind)
            {
                case "ERP_MODULE_PAGE":
                    return ResolveErpModuleParentEntry(entry.Get(0));
                case "PROJECT_REGISTER":
                case "PROJECT_BARLIST_LIST":
                    return new OviaWorkspaceNavigationEntry("PROJECT_MANAGER");
                case "BARLIST":
                    return new OviaWorkspaceNavigationEntry("PROJECT_BARLIST_LIST", entry.Get(0), entry.Get(1), entry.Get(2), entry.Get(3));
                case "BARLIST_MAPPING":
                case "REBAR_UNIT_WEIGHT":
                case "BACKUP_RESTORE":
                case "VERSION_INFO":
                    return CreateEnvironmentSettingsParentEntry();
                case "SYSTEM_SETTINGS":
                    return CreateEnvironmentSettingsParentEntry();
                case "WORKSPACE_INFO":
                    return ResolveWorkspaceInfoParentEntry(entry);
                case "PROJECT_MANAGER":
                default:
                    return null;
            }
        }

        private OviaWorkspaceNavigationEntry ResolveErpModuleParentEntry(string menuKey)
        {
            // ERP는 공통 경로 아이콘에서 여는 독립 WebView 화면으로 동작합니다.
            return null;
        }

        private OviaWorkspaceNavigationEntry ResolveWorkspaceInfoParentEntry(OviaWorkspaceNavigationEntry entry)
        {
            string selectedMenu = entry.Get(3).Trim().ToUpperInvariant();
            string menuKey = entry.Get(0).Trim().ToUpperInvariant();
            string pathText = entry.Get(1);
            string[] segments = SplitWorkspacePath(pathText);

            if (segments.Length <= 2)
            {
                return null;
            }

            if (selectedMenu == "SETTINGS" || menuKey == "SETTINGS")
            {
                return CreateSystemManagementParentEntry();
            }

            string parentTitle = segments[segments.Length - 2];
            string parentPath = JoinWorkspacePath(segments, segments.Length - 1);
            string parentKey = GetWorkspaceKeyBySelectedArea(selectedMenu);
            string parentDescription = parentTitle + " 화면의 상위 경로입니다.";
            return new OviaWorkspaceNavigationEntry("WORKSPACE_INFO", parentKey, parentPath, parentTitle, selectedMenu, parentDescription, parentDescription);
        }

        private OviaWorkspaceNavigationEntry CreateSystemManagementParentEntry()
        {
            return new OviaWorkspaceNavigationEntry(
                "WORKSPACE_INFO",
                "SETTINGS",
                "메인  ›  환경설정",
                "환경설정",
                "SETTINGS",
                "OVIA 전체 환경값과 시스템 동작 기준을 관리하는 설정 화면입니다.",
                "환경설정 아이콘에서 필요한 설정 화면을 선택해 작업합니다."
            );
        }

        private OviaWorkspaceNavigationEntry CreateEnvironmentSettingsParentEntry()
        {
            return new OviaWorkspaceNavigationEntry(
                "WORKSPACE_INFO",
                "SETTINGS",
                "메인  ›  환경설정",
                "환경설정",
                "SETTINGS",
                "OVIA 전체 환경값을 관리하는 환경설정 화면입니다.",
                "환경설정 아이콘에서 ERP 연결, 회사 로고, 페이지 로딩 설정 같은 전체 적용값을 관리합니다."
            );
        }

        private string GetWorkspaceKeyBySelectedArea(string selectedMenu)
        {
            switch (selectedMenu)
            {
                case "PROJECT": return "PROJECT_MANAGER";
                case "SETTINGS": return "SETTINGS";
                case "ERP": return "ERP";
                default: return "MAIN";
            }
        }

        private string[] SplitWorkspacePath(string pathText)
        {
            if (string.IsNullOrWhiteSpace(pathText))
            {
                return new string[0];
            }

            string[] raw = pathText.Split(new char[] { '›' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> parts = new List<string>();
            int i;
            for (i = 0; i < raw.Length; i++)
            {
                string part = raw[i] == null ? string.Empty : raw[i].Trim();
                if (part != string.Empty)
                {
                    parts.Add(part);
                }
            }

            return parts.ToArray();
        }

        private string JoinWorkspacePath(string[] segments, int count)
        {
            if (segments == null || count <= 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            int limit = Math.Min(count, segments.Length);
            int i;
            for (i = 0; i < limit; i++)
            {
                if (!string.IsNullOrWhiteSpace(segments[i]))
                {
                    parts.Add(segments[i].Trim());
                }
            }

            return string.Join("  ›  ", parts.ToArray());
        }

        public void ShowAutoCadEnvironmentCheck()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.Check();
            MessageBoxIcon icon = MessageBoxIcon.Information;

            if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                icon = MessageBoxIcon.Error;
            }
            else if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                icon = MessageBoxIcon.Warning;
            }

            MessageBox.Show(
                report.GetDisplayText(),
                "OVIA 설치 전 환경 점검 결과",
                MessageBoxButtons.OK,
                icon
            );
        }

        public void ShowAutoCadExtractGuide()
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();

            if (!report.IsCurrentDevelopmentAutoCadReady())
            {
                MessageBox.Show(
                    report.GetAutoCadExtractionBlockMessage() + "\r\n\r\n" + report.GetDisplayText(),
                    "OVIA AutoCAD 추출 준비",
                    MessageBoxButtons.OK,
                    report.OverallStatus == OviaEnvironmentStatus.Blocked ? MessageBoxIcon.Error : MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                report.GetAutoCadExtractionReadyMessage(),
                "OVIA AutoCAD 활성",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        public void RequestLogout()
        {
            if (!OviaWorkspaceExitHelper.ConfirmLogout(this, currentScreen))
            {
                return;
            }

            systemExitConfirmed = true;
            this.Close();
        }

        private void ShowScreenWithHistory(Form nextScreen, string title, OviaWorkspaceNavigationEntry entry)
        {
            OviaWorkspaceNavigationEntry previousEntry = currentNavigationEntry;

            if (!ShowScreen(nextScreen))
            {
                return;
            }

            this.Text = title == null ? "OVIA" : title;

            if (!suppressNavigationHistory && previousEntry != null)
            {
                backHistory.Push(previousEntry);
                forwardHistory.Clear();
            }

            currentNavigationEntry = entry;
            RefreshCurrentWorkspaceNavigationState();
        }

        private void RefreshCurrentWorkspaceNavigationState()
        {
            if (currentScreen == null || currentScreen.IsDisposed)
            {
                return;
            }

            RefreshWorkspaceNavigationHeaders(currentScreen);
        }

        private void RefreshWorkspaceNavigationHeaders(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            OVIA.Desktop.Controls.OviaWorkspaceHeader header = root as OVIA.Desktop.Controls.OviaWorkspaceHeader;
            if (header != null)
            {
                header.RefreshNavigationButtonStates();
            }

            int i;
            for (i = 0; i < root.Controls.Count; i++)
            {
                RefreshWorkspaceNavigationHeaders(root.Controls[i]);
            }
        }

        private bool ShowScreen(Form nextScreen)
        {
            if (nextScreen == null)
            {
                return false;
            }

            IOviaWorkspaceScreen currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
            {
                nextScreen.Dispose();
                return false;
            }

            this.SuspendLayout();
            hostPanel.SuspendLayout();

            try
            {
                if (currentScreen != null)
                {
                    currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

                    if (currentWorkspaceScreen != null)
                    {
                        currentWorkspaceScreen.BeforeLeaveWorkspaceScreen();
                    }

                    hostPanel.Controls.Remove(currentScreen);
                    currentScreen.Dispose();
                    currentScreen = null;
                }

                nextScreen.TopLevel = false;
                nextScreen.FormBorderStyle = FormBorderStyle.None;
                nextScreen.Dock = DockStyle.Fill;
                nextScreen.StartPosition = FormStartPosition.Manual;
                nextScreen.WindowState = FormWindowState.Normal;

                currentScreen = nextScreen;
                hostPanel.Controls.Add(nextScreen);
                nextScreen.Show();
                FillCurrentWorkspaceScreen();
                nextScreen.BringToFront();

                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (currentScreen == nextScreen && !nextScreen.IsDisposed)
                        {
                            FillCurrentWorkspaceScreen();
                        }
                    }));
                }
                catch
                {
                }
            }
            finally
            {
                hostPanel.ResumeLayout(false);
                this.ResumeLayout(false);
            }

            return true;
        }

        private void ApplyWorkspaceLayout(Form screen)
        {
            IOviaWorkspaceLayout workspaceLayout = screen as IOviaWorkspaceLayout;

            if (workspaceLayout != null)
            {
                workspaceLayout.ApplyWorkspaceLayout();
            }
        }

        private void FrmWorkspaceShell_FormClosing(object sender, FormClosingEventArgs e)
        {
            IOviaWorkspaceScreen currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (navigateToMainClose)
            {
                if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
                {
                    navigateToMainClose = false;
                    e.Cancel = true;
                    return;
                }
            }
            else if (!systemExitConfirmed)
            {
                if (!OviaWorkspaceExitHelper.ConfirmLogout(this, currentScreen))
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (currentWorkspaceScreen != null)
            {
                currentWorkspaceScreen.BeforeLeaveWorkspaceScreen();
            }
        }
    }
}

