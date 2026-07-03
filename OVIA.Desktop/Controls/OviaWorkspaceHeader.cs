using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop.Controls
{
    public sealed class OviaWorkspacePathClickedEventArgs : EventArgs
    {
        public OviaWorkspacePathClickedEventArgs(string target)
        {
            Target = target == null ? string.Empty : target;
        }

        public string Target { get; private set; }
        public bool Handled { get; set; }
    }

    public sealed class OviaWorkspaceHeader : UserControl, IMessageFilter
    {
        private const int HeaderLeft = 34;
        private const int HeaderTop = 8;
        private const int HeaderHeight = 32;
        private const int NavigationWidth = 188;
        private const int NotificationWidth = 30;
        private const int NotificationRightGap = 20;
        private const int BreadcrumbSafeGap = 40;
        private const int WmLButtonDown = 0x0201;
        private const int WmRButtonDown = 0x0204;
        private const int WmMButtonDown = 0x0207;
        private const int WmNcLButtonDown = 0x00A1;

        private readonly Color surfaceColor;
        private readonly Color textColor;
        private readonly Color inactiveColor;
        private readonly Color explorerHoverColor;
        private readonly Color explorerDownColor;
        private readonly Color notificationBadgeColor;

        private OviaExplorerIconButton btnBack;
        private OviaExplorerIconButton btnForward;
        private OviaExplorerIconButton btnUp;
        private OviaExplorerIconButton btnRefresh;
        private OviaExplorerIconButton btnHome;
        private OviaExplorerIconButton btnNotification;
        private Timer notificationRefreshTimer;
        private OviaRoundedPanel addressBar;
        private OviaBreadcrumbLabel breadcrumbLabel;
        private TextBox pathTextBox;
        private ToolTip toolTip;
        private bool pathEditMessageFilterInstalled;

        public event EventHandler BackClicked;
        public event EventHandler UpClicked;
        public event EventHandler RefreshClicked;
        public event EventHandler NotificationClicked;
        public event EventHandler MainPathClicked;
        public event EventHandler<OviaWorkspacePathClickedEventArgs> PathSegmentClicked;

        public OviaWorkspaceHeader()
        {
            surfaceColor = OviaFluentTheme.AppBackground;
            textColor = Color.Black;
            inactiveColor = Color.FromArgb(175, 181, 190);
            explorerHoverColor = Color.FromArgb(229, 233, 238);
            explorerDownColor = Color.FromArgb(218, 224, 232);
            notificationBadgeColor = Color.FromArgb(37, 99, 235);

            this.Height = HeaderHeight;
            this.BackColor = surfaceColor;
            this.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 4000;
            toolTip.InitialDelay = 350;
            toolTip.ReshowDelay = 100;
            toolTip.ShowAlways = true;

            BuildControls();
            LayoutControls();
            StartNotificationRefreshTimer();
        }

        public static OviaWorkspaceHeader AddTo(Control parent, string pathText, Action backAction, Action upAction, Action refreshAction, Action logoutAction, bool backEnabled, bool upEnabled, Action<string> pathSegmentAction = null)
        {
            OviaWorkspaceHeader header = new OviaWorkspaceHeader();
            header.PathText = pathText;
            header.Location = new Point(HeaderLeft, HeaderTop);
            header.Size = new Size(Math.Max(1, parent.ClientSize.Width - HeaderLeft), HeaderHeight);
            header.BackEnabled = backEnabled;
            header.UpEnabled = upEnabled;
            header.ForwardEnabled = false;

            if (backAction != null)
            {
                header.BackClicked += delegate { backAction(); };
            }

            if (pathSegmentAction != null)
            {
                header.PathSegmentClicked += delegate(object sender, OviaWorkspacePathClickedEventArgs e)
                {
                    pathSegmentAction(e.Target);
                    e.Handled = true;
                };
            }
            else if (backAction != null)
            {
                header.MainPathClicked += delegate { backAction(); };
            }

            if (upAction != null)
            {
                header.UpClicked += delegate { upAction(); };
            }

            if (refreshAction != null)
            {
                header.RefreshClicked += delegate { refreshAction(); };
            }

            if (logoutAction != null)
            {
                header.NotificationClicked += delegate { };
            }

            parent.Controls.Add(header);
            header.RefreshNotificationBadge();

            parent.Resize += delegate
            {
                if (!header.IsDisposed)
                {
                    header.Width = Math.Max(1, parent.ClientSize.Width - HeaderLeft);
                    header.LayoutControls();
                }
            };

            return header;
        }

        public string PathText
        {
            get { return breadcrumbLabel == null ? string.Empty : breadcrumbLabel.Text; }
            set
            {
                string text = value == null ? string.Empty : value;

                if (breadcrumbLabel != null)
                {
                    breadcrumbLabel.PathText = text;
                    ApplyBreadcrumbLinks(text);
                }

                if (pathTextBox != null)
                {
                    pathTextBox.Text = NormalizeCopyPath(text);
                }
            }
        }

        public bool BackEnabled
        {
            get { return btnBack != null && btnBack.Enabled; }
            set { SetNavigationEnabled(btnBack, value); }
        }

        public bool ForwardEnabled
        {
            get { return btnForward != null && btnForward.Enabled; }
            set { SetNavigationEnabled(btnForward, value); }
        }

        public bool UpEnabled
        {
            get { return btnUp != null && btnUp.Enabled; }
            set { SetNavigationEnabled(btnUp, value); }
        }

        private void ApplyBreadcrumbLinks(string text)
        {
            if (breadcrumbLabel != null)
            {
                breadcrumbLabel.PathText = text == null ? string.Empty : text;
            }
        }

        private string GetLastBreadcrumbSegment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string[] parts = text.Split(new char[] { '›' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return text.Trim();
            }

            return parts[parts.Length - 1].Trim();
        }

        private void BuildControls()
        {
            btnBack = CreateExplorerButton("\uE72B", "뒤로");
            btnBack.Click += delegate { Raise(BackClicked); };
            Controls.Add(btnBack);

            btnForward = CreateExplorerButton("\uE72A", "앞으로");
            btnForward.Click += delegate { };
            Controls.Add(btnForward);

            btnUp = CreateExplorerButton("\uE74A", "위로");
            btnUp.Click += delegate { Raise(UpClicked); };
            Controls.Add(btnUp);

            btnRefresh = CreateExplorerButton("\uE72C", "새로고침");
            btnRefresh.Click += delegate { Raise(RefreshClicked); };
            Controls.Add(btnRefresh);

            btnHome = CreateExplorerButton("\uE7F4", "메인");
            btnHome.Click += delegate
            {
                if (RaisePathSegmentClicked("MAIN"))
                {
                    return;
                }

                Raise(MainPathClicked);
            };
            Controls.Add(btnHome);

            addressBar = new OviaRoundedPanel();
            addressBar.BackColor = surfaceColor;
            addressBar.FillColor = Color.White;
            addressBar.Radius = 2;
            addressBar.Margin = Padding.Empty;
            addressBar.Padding = new Padding(10, 6, 10, 0);
            Controls.Add(addressBar);

            breadcrumbLabel = new OviaBreadcrumbLabel();
            breadcrumbLabel.AutoSize = false;
            breadcrumbLabel.TextAlign = ContentAlignment.MiddleLeft;
            breadcrumbLabel.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            breadcrumbLabel.BackColor = Color.White;
            breadcrumbLabel.ForeColor = textColor;
            breadcrumbLabel.TabStop = false;
            breadcrumbLabel.PathSegmentClicked += BreadcrumbLabel_PathSegmentClicked;
            breadcrumbLabel.MouseClick += BreadcrumbLabel_MouseClick;
            addressBar.Controls.Add(breadcrumbLabel);

            pathTextBox = new TextBox();
            pathTextBox.ReadOnly = true;
            pathTextBox.BorderStyle = BorderStyle.None;
            pathTextBox.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            pathTextBox.ForeColor = textColor;
            pathTextBox.BackColor = Color.White;
            pathTextBox.Margin = Padding.Empty;
            pathTextBox.TabStop = false;
            pathTextBox.Visible = false;
            pathTextBox.HideSelection = false;
            pathTextBox.ShortcutsEnabled = true;
            pathTextBox.Cursor = Cursors.IBeam;
            pathTextBox.Leave += delegate { HidePathEditMode(); };
            pathTextBox.KeyDown += PathTextBox_KeyDown;
            addressBar.Controls.Add(pathTextBox);

            btnNotification = CreateExplorerButton("\uF2A3", "알림");
            // U+F2A3 종 아이콘은 같은 버튼 영역 안에서 뒤로/앞으로/위로가기 아이콘과 시각 크기를 맞춘다.
            btnNotification.Font = OVIA.Desktop.OviaIconFont.Create(15F, FontStyle.Regular);
            btnNotification.BadgeBackColor = notificationBadgeColor;
            btnNotification.BadgeFont = OviaFluentTheme.FontData(7.2F, FontStyle.Bold);
            btnNotification.Click += Notification_Click;
            Controls.Add(btnNotification);
        }

        private void LayoutControls()
        {
            if (btnBack == null)
            {
                return;
            }

            btnBack.Location = new Point(0, 0);
            btnForward.Location = new Point(36, 0);
            btnUp.Location = new Point(72, 0);
            btnRefresh.Location = new Point(108, 0);
            btnHome.Location = new Point(144, 0);

            int notificationX = Math.Max(NavigationWidth, this.ClientSize.Width - NotificationRightGap - NotificationWidth);
            btnNotification.Location = new Point(notificationX, 0);
            addressBar.Location = new Point(NavigationWidth, 0);
            addressBar.Size = new Size(Math.Max(1, notificationX - BreadcrumbSafeGap - NavigationWidth), HeaderHeight);
            addressBar.RefreshRoundedRegion();

            breadcrumbLabel.Location = new Point(10, 4);
            breadcrumbLabel.Size = new Size(Math.Max(1, addressBar.ClientSize.Width - 20), 22);

            pathTextBox.Location = new Point(10, 5);
            pathTextBox.Size = new Size(Math.Max(1, addressBar.ClientSize.Width - 20), 20);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UninstallPathEditMessageFilter();
            StopNotificationRefreshTimer();
            base.OnHandleDestroyed(e);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (!IsPathEditModeVisible() || !IsPathEditCloseCandidateMessage(m.Msg))
            {
                return false;
            }

            Point screenPoint;

            if (!TryGetScreenPointFromMessage(m, out screenPoint))
            {
                return false;
            }

            if (!IsScreenPointInsidePathWhiteArea(screenPoint))
            {
                HidePathEditMode();
            }

            return false;
        }

        private OviaExplorerIconButton CreateExplorerButton(string text, string tip)
        {
            OviaExplorerIconButton button = new OviaExplorerIconButton();
            button.Text = text;
            button.Size = new Size(NotificationWidth, 30);
            button.Font = OVIA.Desktop.OviaIconFont.Create(9.5F, FontStyle.Regular);
            button.ForeColor = textColor;
            button.NormalForeColor = textColor;
            button.HoverForeColor = textColor;
            button.DownForeColor = textColor;
            button.BackColor = surfaceColor;
            button.HoverBackColor = explorerHoverColor;
            button.DownBackColor = explorerDownColor;
            button.CornerRadius = 2;
            button.TabStop = false;

            if (toolTip != null)
            {
                toolTip.SetToolTip(button, tip);
            }

            return button;
        }

        private void StartNotificationRefreshTimer()
        {
            if (notificationRefreshTimer != null)
            {
                return;
            }

            OVIA.Desktop.OviaNotificationStore.NotificationsChanged += NotificationStore_NotificationsChanged;

            notificationRefreshTimer = new Timer();
            notificationRefreshTimer.Interval = 15000;
            notificationRefreshTimer.Tick += delegate { RefreshNotificationBadge(); };
            notificationRefreshTimer.Start();
        }

        private void StopNotificationRefreshTimer()
        {
            if (notificationRefreshTimer == null)
            {
                return;
            }

            OVIA.Desktop.OviaNotificationStore.NotificationsChanged -= NotificationStore_NotificationsChanged;
            notificationRefreshTimer.Stop();
            notificationRefreshTimer.Dispose();
            notificationRefreshTimer = null;
        }

        private void NotificationStore_NotificationsChanged(object sender, EventArgs e)
        {
            RefreshNotificationBadge();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            RefreshNotificationBadge();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                RefreshNotificationBadge();
            }
        }

        public void RefreshNotificationBadge()
        {
            if (btnNotification == null || btnNotification.IsDisposed)
            {
                return;
            }

            int count = 0;

            try
            {
                OVIA.Desktop.IOviaWorkspaceNavigator navigator = OVIA.Desktop.OviaWorkspaceNavigation.FindNavigator(this);
                if (navigator != null)
                {
                    count = OVIA.Desktop.OviaNotificationStore.GetUnreadCount(navigator.CurrentCompanyId, navigator.CurrentUserId);
                }
            }
            catch
            {
                count = 0;
            }

            if (count <= 0)
            {
                btnNotification.BadgeVisible = false;
                btnNotification.BadgeText = string.Empty;
                btnNotification.Invalidate();
                return;
            }

            btnNotification.BadgeText = count > 99 ? "99+" : count.ToString();
            btnNotification.BadgeVisible = true;
            btnNotification.Invalidate();
        }

        private void LayoutNotificationBadge()
        {
            // 알림 숫자 배지는 알림 아이콘 버튼 내부에서 직접 렌더링한다.
            // 별도 자식 컨트롤을 겹치지 않아 hover 시 사각 배경이 보이지 않는다.
        }

        private void ApplyNotificationBadgeRegion()
        {
        }

        private void Notification_Click(object sender, EventArgs e)
        {
            OVIA.Desktop.IOviaWorkspaceNavigator navigator = OVIA.Desktop.OviaWorkspaceNavigation.FindNavigator(this);
            if (navigator != null)
            {
                navigator.NavigateToNotifications();
                RefreshNotificationBadge();
                return;
            }

            Raise(NotificationClicked);
        }

        private void SetNavigationEnabled(OviaExplorerIconButton button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.Enabled = enabled;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            button.ForeColor = enabled ? textColor : inactiveColor;
            button.NormalForeColor = enabled ? textColor : inactiveColor;
            button.HoverForeColor = enabled ? textColor : inactiveColor;
            button.DownForeColor = enabled ? textColor : inactiveColor;
            button.HoverBackColor = enabled ? explorerHoverColor : Color.Empty;
            button.DownBackColor = enabled ? explorerDownColor : Color.Empty;
            button.ResetInteractionState();
            button.Invalidate();
        }

        private void BreadcrumbLabel_PathSegmentClicked(object sender, OviaBreadcrumbSegmentClickedEventArgs e)
        {
            string target = e == null ? string.Empty : e.Target;

            if (RaisePathSegmentClicked(target))
            {
                return;
            }

            if (target == "MAIN")
            {
                Raise(MainPathClicked);
            }
        }

        private bool RaisePathSegmentClicked(string target)
        {
            EventHandler<OviaWorkspacePathClickedEventArgs> handler = PathSegmentClicked;

            if (handler == null)
            {
                return false;
            }

            OviaWorkspacePathClickedEventArgs args = new OviaWorkspacePathClickedEventArgs(target);
            handler(this, args);
            return args.Handled;
        }

        private void BreadcrumbLabel_MouseClick(object sender, MouseEventArgs e)
        {
            if (IsPathBlankAreaClick(e))
            {
                ShowPathEditMode();
            }
        }

        private bool IsPathBlankAreaClick(MouseEventArgs e)
        {
            if (breadcrumbLabel == null || e == null || e.Button != MouseButtons.Left)
            {
                return false;
            }

            int textWidth = breadcrumbLabel.ContentWidth;
            return e.X > textWidth + 8;
        }

        private void ShowPathEditMode()
        {
            if (breadcrumbLabel != null)
            {
                breadcrumbLabel.Visible = false;
            }

            if (pathTextBox != null)
            {
                pathTextBox.Visible = true;
                pathTextBox.Focus();
                pathTextBox.SelectAll();
            }

            InstallPathEditMessageFilter();
        }

        private void HidePathEditMode()
        {
            UninstallPathEditMessageFilter();

            if (pathTextBox != null)
            {
                pathTextBox.Visible = false;
            }

            if (breadcrumbLabel != null)
            {
                breadcrumbLabel.Visible = true;
            }
        }

        private void InstallPathEditMessageFilter()
        {
            if (pathEditMessageFilterInstalled)
            {
                return;
            }

            Application.AddMessageFilter(this);
            pathEditMessageFilterInstalled = true;
        }

        private void UninstallPathEditMessageFilter()
        {
            if (!pathEditMessageFilterInstalled)
            {
                return;
            }

            Application.RemoveMessageFilter(this);
            pathEditMessageFilterInstalled = false;
        }

        private bool IsPathEditModeVisible()
        {
            return pathTextBox != null && pathTextBox.Visible;
        }

        private bool IsPathEditCloseCandidateMessage(int messageId)
        {
            return messageId == WmLButtonDown
                || messageId == WmRButtonDown
                || messageId == WmMButtonDown
                || messageId == WmNcLButtonDown;
        }

        private bool TryGetScreenPointFromMessage(Message message, out Point screenPoint)
        {
            screenPoint = Point.Empty;

            if (message.Msg == WmNcLButtonDown)
            {
                int raw = message.LParam.ToInt32();
                screenPoint = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
                return true;
            }

            Control source = Control.FromHandle(message.HWnd);

            if (source == null)
            {
                return false;
            }

            int lParam = message.LParam.ToInt32();
            Point clientPoint = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            screenPoint = source.PointToScreen(clientPoint);
            return true;
        }

        private bool IsScreenPointInsidePathWhiteArea(Point screenPoint)
        {
            if (addressBar == null || addressBar.IsDisposed || !addressBar.Visible)
            {
                return false;
            }

            Rectangle whiteArea = addressBar.RectangleToScreen(addressBar.ClientRectangle);
            return whiteArea.Contains(screenPoint);
        }

        private void PathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
            {
                HidePathEditMode();
                e.SuppressKeyPress = true;
            }
        }

        private string NormalizeCopyPath(string pathText)
        {
            return pathText == null ? string.Empty : pathText.Replace("  ›  ", "\\");
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }




    internal sealed class OviaNotificationBadge : Control
    {
        public Color BadgeBackColor { get; set; }

        public OviaNotificationBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            BadgeBackColor = OviaFluentTheme.Accent;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = CreateRoundRectPath(rect, Math.Max(1, rect.Height / 2)))
            using (SolidBrush brush = new SolidBrush(BadgeBackColor))
            {
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
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
    }

    internal sealed class OviaRoundedPanel : Panel
    {
        public OviaRoundedPanel()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);

            Margin = Padding.Empty;
        }

        public Color FillColor { get; set; }
        public int Radius { get; set; }

        public void RefreshRoundedRegion()
        {
            ApplyRoundedRegion();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRoundedRegion();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color back = Parent == null ? BackColor : Parent.BackColor;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = CreateRoundRectPath(rect, Math.Max(0, Radius)))
            using (SolidBrush brush = new SolidBrush(FillColor == Color.Empty ? Color.White : FillColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            base.OnPaint(e);
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = CreateRoundRectPath(rect, Math.Max(0, Radius)))
            {
                Region oldRegion = Region;
                Region = new Region(path);

                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
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

            int diameter = radius * 2;
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
    }

    internal sealed class OviaBreadcrumbSegmentClickedEventArgs : EventArgs
    {
        public OviaBreadcrumbSegmentClickedEventArgs(string target)
        {
            Target = target == null ? string.Empty : target;
        }

        public string Target { get; private set; }
    }

    internal sealed class OviaBreadcrumbLabel : Control
    {
        private readonly System.Collections.Generic.List<OviaBreadcrumbSegmentInfo> segments = new System.Collections.Generic.List<OviaBreadcrumbSegmentInfo>();
        private string pathText = string.Empty;
        private int contentWidth;
        private const float Tracking = -0.5F;

        public event EventHandler<OviaBreadcrumbSegmentClickedEventArgs> PathSegmentClicked;

        public OviaBreadcrumbLabel()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);

            Cursor = Cursors.IBeam;
            TabStop = false;
        }

        public ContentAlignment TextAlign { get; set; }

        public string PathText
        {
            get { return pathText; }
            set
            {
                pathText = value == null ? string.Empty : value;
                Text = pathText;
                Invalidate();
            }
        }

        public int ContentWidth
        {
            get { return contentWidth; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = GetSegmentAt(e.Location) == null ? Cursors.IBeam : Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.IBeam;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            OviaBreadcrumbSegmentInfo segment = GetSegmentAt(e.Location);
            if (segment != null && e.Button == MouseButtons.Left)
            {
                EventHandler<OviaBreadcrumbSegmentClickedEventArgs> handler = PathSegmentClicked;
                if (handler != null)
                {
                    handler(this, new OviaBreadcrumbSegmentClickedEventArgs(segment.Target));
                    return;
                }
            }

            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(BackColor);

            segments.Clear();
            contentWidth = 0;

            string text = pathText == null ? string.Empty : pathText;
            if (text.Trim() == string.Empty)
            {
                return;
            }

            string[] parts = text.Split(new char[] { '›' }, StringSplitOptions.None);
            string lastSegment = GetLastSegment(parts);
            float x = 0F;
            float y = Math.Max(0F, (Height - Font.Height) / 2F + 1F);

            int visibleIndex = 0;
            int i;
            for (i = 0; i < parts.Length; i++)
            {
                string raw = parts[i] == null ? string.Empty : parts[i].Trim();
                if (raw == string.Empty)
                {
                    continue;
                }

                if (visibleIndex > 0)
                {
                    x = DrawTrackedText(e.Graphics, "  ", Font, ForeColor, x, y, Tracking);
                    x = DrawTrackedText(e.Graphics, "›", Font, ForeColor, x, y - 1F, Tracking);
                    x = DrawTrackedText(e.Graphics, "  ", Font, ForeColor, x, y, Tracking);
                }

                bool isLast = raw == lastSegment;
                bool isMain = raw == "메인";
                bool isBold = isLast && !isMain;
                Font drawFont = isBold ? OviaFluentTheme.FontKorean(Font.Size, FontStyle.Bold) : Font;
                float startX = x;
                x = DrawTrackedText(e.Graphics, raw, drawFont, ForeColor, x, y, Tracking);
                Rectangle rect = new Rectangle((int)Math.Floor(startX), 0, Math.Max(1, (int)Math.Ceiling(x - startX)), Height);

                if (!isLast)
                {
                    string target = ResolveTarget(raw);
                    if (target != string.Empty)
                    {
                        segments.Add(new OviaBreadcrumbSegmentInfo(rect, target));
                    }
                }

                if (!object.ReferenceEquals(drawFont, Font))
                {
                    drawFont.Dispose();
                }

                visibleIndex++;
            }

            contentWidth = (int)Math.Ceiling(x);
        }

        private static string GetLastSegment(string[] parts)
        {
            if (parts == null)
            {
                return string.Empty;
            }

            int i;
            for (i = parts.Length - 1; i >= 0; i--)
            {
                string raw = parts[i] == null ? string.Empty : parts[i].Trim();
                if (raw != string.Empty)
                {
                    return raw;
                }
            }

            return string.Empty;
        }

        private static string ResolveTarget(string segment)
        {
            if (segment == "메인") return "MAIN";
            if (segment == "공사관리") return "PROJECT_MANAGER";
            if (segment == "공사별 BarList") return "PROJECT_BARLIST_LIST";
            if (segment == "운영현황") return "OPERATIONS";
            if (segment == "자재/재고") return "MATERIAL_STOCK";
            if (segment == "출하/송장") return "SHIPPING_INVOICE";
            if (segment == "ERP") return "ERP";
            if (segment == "기준정보") return "MASTER_DATA";
            if (segment == "환경설정") return "SETTINGS";
            if (segment == "BarList 항목 매핑") return "BARLIST_MAPPING";
            if (segment == "이형철근 단위중량표") return "REBAR_UNIT_WEIGHT";
            if (segment == "시스템 설정") return "SYSTEM_SETTINGS";
            if (segment == "메뉴관리") return "MENU_MANAGER";
            return string.Empty;
        }

        private static float DrawTrackedText(Graphics g, string text, Font font, Color color, float x, float y, float tracking)
        {
            if (text == null || text.Length == 0)
            {
                return x;
            }

            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                int i;
                for (i = 0; i < text.Length; i++)
                {
                    string ch = text[i].ToString();
                    g.DrawString(ch, font, brush, x, y, format);
                    SizeF size = g.MeasureString(ch, font, PointF.Empty, format);
                    x += Math.Max(0F, size.Width + tracking);
                }
            }

            return x;
        }

        private OviaBreadcrumbSegmentInfo GetSegmentAt(Point point)
        {
            int i;
            for (i = 0; i < segments.Count; i++)
            {
                if (segments[i].Bounds.Contains(point))
                {
                    return segments[i];
                }
            }

            return null;
        }
    }

    internal sealed class OviaBreadcrumbSegmentInfo
    {
        public OviaBreadcrumbSegmentInfo(Rectangle bounds, string target)
        {
            Bounds = bounds;
            Target = target == null ? string.Empty : target;
        }

        public Rectangle Bounds { get; private set; }
        public string Target { get; private set; }
    }

    internal sealed class OviaExplorerIconButton : Control
    {
        private bool isHover;
        private bool isDown;

        public OviaExplorerIconButton()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);

            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            TabStop = false;
            BadgeText = string.Empty;
            BadgeBackColor = OviaFluentTheme.Accent;
        }

        public Color NormalForeColor { get; set; }
        public Color HoverForeColor { get; set; }
        public Color DownForeColor { get; set; }
        public Color HoverBackColor { get; set; }
        public Color DownBackColor { get; set; }
        public int CornerRadius { get; set; }
        public bool BadgeVisible { get; set; }
        public string BadgeText { get; set; }
        public Color BadgeBackColor { get; set; }
        public Font BadgeFont { get; set; }

        public void ResetInteractionState()
        {
            isHover = false;
            isDown = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            if (Enabled)
            {
                isHover = true;
                Invalidate();
            }

            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHover = false;
            isDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left)
            {
                isDown = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isDown = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            ResetInteractionState();
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // 배경은 OnPaint에서 부모 배경색으로 한 번만 지운다.
            // 기본 Button/Control 배경 도형이나 포커스 잔상이 섞이지 않게 한다.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBackColor = Parent == null ? SystemColors.Control : Parent.BackColor;
            g.Clear(parentBackColor);

            Color fillColor = Color.Empty;
            Color drawForeColor = NormalForeColor == Color.Empty ? ForeColor : NormalForeColor;

            if (Enabled && isDown)
            {
                fillColor = DownBackColor;
                if (DownForeColor != Color.Empty)
                {
                    drawForeColor = DownForeColor;
                }
            }
            else if (Enabled && isHover)
            {
                fillColor = HoverBackColor;
                if (HoverForeColor != Color.Empty)
                {
                    drawForeColor = HoverForeColor;
                }
            }

            if (fillColor != Color.Empty && fillColor != Color.Transparent)
            {
                Rectangle hoverRect = GetCenteredSquareRectangle();
                using (GraphicsPath path = CreateRoundRectPath(hoverRect, Math.Max(0, CornerRadius)))
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    g.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                drawForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            DrawNotificationBadge(g);
        }

        private void DrawNotificationBadge(Graphics g)
        {
            if (!BadgeVisible || string.IsNullOrWhiteSpace(BadgeText))
            {
                return;
            }

            string text = BadgeText.Trim();
            int badgeHeight = 18;
            int badgeWidth = text.Length >= 3 ? 27 : 18;
            Rectangle rect = new Rectangle(Width - badgeWidth - 1, 0, badgeWidth, badgeHeight);

            using (GraphicsPath path = CreateRoundRectPath(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), badgeHeight / 2))
            using (SolidBrush brush = new SolidBrush(BadgeBackColor == Color.Empty ? OviaFluentTheme.Accent : BadgeBackColor))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
            }

            using (Font font = BadgeFont == null ? OviaFluentTheme.FontData(7.2F, FontStyle.Bold) : (Font)BadgeFont.Clone())
            {
                TextRenderer.DrawText(
                    g,
                    text,
                    font,
                    rect,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        private Rectangle GetCenteredSquareRectangle()
        {
            int size = Math.Min(30, Math.Min(Width, Height));
            int left = Math.Max(0, (Width - size) / 2);
            int top = Math.Max(0, (Height - size) / 2);
            return new Rectangle(left, top, Math.Max(1, size - 1), Math.Max(1, size - 1));
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

            int diameter = radius * 2;
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
    }

}