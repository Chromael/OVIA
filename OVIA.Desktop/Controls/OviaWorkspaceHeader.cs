using System;
using System.Drawing;
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
        private const int NavigationWidth = 152;
        private const int LogoutWidth = 30;
        private const int LogoutRightGap = 20;
        private const int BreadcrumbSafeGap = 40;
        private const int WmLButtonDown = 0x0201;
        private const int WmRButtonDown = 0x0204;
        private const int WmMButtonDown = 0x0207;
        private const int WmNcLButtonDown = 0x00A1;

        private readonly Color surfaceColor;
        private readonly Color textColor;
        private readonly Color inactiveColor;
        private readonly Color logoutHoverColor;
        private readonly Color logoutDownColor;

        private Button btnBack;
        private Button btnForward;
        private Button btnUp;
        private Button btnRefresh;
        private Button btnLogout;
        private Panel addressBar;
        private LinkLabel breadcrumbLabel;
        private TextBox pathTextBox;
        private ToolTip toolTip;
        private bool pathEditMessageFilterInstalled;

        public event EventHandler BackClicked;
        public event EventHandler UpClicked;
        public event EventHandler RefreshClicked;
        public event EventHandler LogoutClicked;
        public event EventHandler MainPathClicked;
        public event EventHandler<OviaWorkspacePathClickedEventArgs> PathSegmentClicked;

        public OviaWorkspaceHeader()
        {
            surfaceColor = OviaFluentTheme.AppBackground;
            textColor = Color.Black;
            inactiveColor = Color.FromArgb(175, 181, 190);
            logoutHoverColor = Color.FromArgb(220, 53, 69);
            logoutDownColor = Color.FromArgb(185, 28, 28);

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
                header.LogoutClicked += delegate { logoutAction(); };
            }

            parent.Controls.Add(header);

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
                    breadcrumbLabel.Text = text;
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
            if (breadcrumbLabel == null)
            {
                return;
            }

            breadcrumbLabel.Links.Clear();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string lastSegment = GetLastBreadcrumbSegment(text);
            AddBreadcrumbLink(text, "메인", "MAIN", lastSegment != "메인");
            AddBreadcrumbLink(text, "공사관리", "PROJECT_MANAGER", lastSegment != "공사관리");
            AddBreadcrumbLink(text, "공사별 BarList", "PROJECT_BARLIST_LIST", lastSegment != "공사별 BarList");
            AddBreadcrumbLink(text, "환경설정", "SETTINGS", lastSegment != "환경설정");
        }

        private void AddBreadcrumbLink(string text, string caption, string target, bool enabled)
        {
            if (!enabled || breadcrumbLabel == null || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(caption))
            {
                return;
            }

            int start = text.IndexOf(caption, StringComparison.Ordinal);

            if (start < 0)
            {
                return;
            }

            foreach (LinkLabel.Link link in breadcrumbLabel.Links)
            {
                int linkStart = link.Start;
                int linkEnd = link.Start + link.Length;
                int nextStart = start;
                int nextEnd = start + caption.Length;

                if (nextStart < linkEnd && nextEnd > linkStart)
                {
                    return;
                }
            }

            breadcrumbLabel.Links.Add(start, caption.Length, target);
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

            addressBar = new Panel();
            addressBar.BackColor = Color.White;
            addressBar.Margin = Padding.Empty;
            addressBar.Padding = new Padding(10, 6, 10, 0);
            Controls.Add(addressBar);

            breadcrumbLabel = new LinkLabel();
            breadcrumbLabel.AutoSize = false;
            breadcrumbLabel.TextAlign = ContentAlignment.MiddleLeft;
            breadcrumbLabel.Font = OviaFluentTheme.FontKorean(10F, FontStyle.Regular);
            breadcrumbLabel.BackColor = Color.White;
            breadcrumbLabel.ForeColor = textColor;
            breadcrumbLabel.LinkColor = textColor;
            breadcrumbLabel.ActiveLinkColor = OviaFluentTheme.Accent;
            breadcrumbLabel.VisitedLinkColor = textColor;
            breadcrumbLabel.DisabledLinkColor = textColor;
            breadcrumbLabel.LinkBehavior = LinkBehavior.NeverUnderline;
            breadcrumbLabel.TabStop = false;
            breadcrumbLabel.LinkClicked += BreadcrumbLabel_LinkClicked;
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

            btnLogout = CreateExplorerButton("\uE7E8", "로그아웃");
            StyleLogoutButton(btnLogout);
            btnLogout.Click += delegate { Raise(LogoutClicked); };
            Controls.Add(btnLogout);
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

            int logoutX = Math.Max(NavigationWidth, this.ClientSize.Width - LogoutRightGap - LogoutWidth);
            btnLogout.Location = new Point(logoutX, 0);

            addressBar.Location = new Point(NavigationWidth, 0);
            addressBar.Size = new Size(Math.Max(1, logoutX - BreadcrumbSafeGap - NavigationWidth), HeaderHeight);

            breadcrumbLabel.Location = new Point(10, 6);
            breadcrumbLabel.Size = new Size(Math.Max(1, addressBar.ClientSize.Width - 20), 22);

            pathTextBox.Location = new Point(10, 7);
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

        private Button CreateExplorerButton(string text, string tip)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(LogoutWidth, 30);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = OviaFluentTheme.NavigationHover;
            button.FlatAppearance.MouseDownBackColor = OviaFluentTheme.NavigationSelected;
            button.Font = new Font("Segoe MDL2 Assets", 9.5F, FontStyle.Regular);
            button.ForeColor = textColor;
            button.BackColor = surfaceColor;
            button.TabStop = false;

            if (toolTip != null)
            {
                toolTip.SetToolTip(button, tip);
            }

            return button;
        }

        private void StyleLogoutButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = logoutHoverColor;
            button.FlatAppearance.MouseDownBackColor = logoutDownColor;
            button.BackColor = surfaceColor;
            button.ForeColor = textColor;
            button.Region = null;

            button.MouseEnter += delegate
            {
                button.BackColor = logoutHoverColor;
                button.ForeColor = Color.White;
            };

            button.MouseLeave += delegate
            {
                button.BackColor = surfaceColor;
                button.ForeColor = textColor;
            };
        }

        private void SetNavigationEnabled(Button button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.Enabled = enabled;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            button.ForeColor = enabled ? textColor : inactiveColor;
            button.FlatAppearance.MouseOverBackColor = enabled ? OviaFluentTheme.NavigationHover : surfaceColor;
            button.FlatAppearance.MouseDownBackColor = enabled ? OviaFluentTheme.NavigationSelected : surfaceColor;
            button.BackColor = surfaceColor;
        }

        private void BreadcrumbLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string target = e.Link.LinkData == null ? string.Empty : e.Link.LinkData.ToString();

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

            int textWidth = TextRenderer.MeasureText(
                breadcrumbLabel.Text,
                breadcrumbLabel.Font,
                new Size(int.MaxValue, breadcrumbLabel.Height),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
            ).Width;

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
}
