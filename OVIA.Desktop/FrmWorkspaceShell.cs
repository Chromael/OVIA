using System;
using System.Diagnostics;
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
        void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus);
        void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath);
        void NavigateToBarListMapping();
        void NavigateToRebarUnitWeightTable();
        void NavigateToSystemSettings();
        void ShowAutoCadEnvironmentCheck();
        void ShowAutoCadExtractGuide();
        void RequestLogout();
    }

    internal interface IOviaWorkspaceScreen
    {
        bool CanLeaveWorkspaceScreen();
        void BeforeLeaveWorkspaceScreen();
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
    }

    internal static class OviaWorkspaceCommandBar
    {
        private static OviaAnimatedDropDownMenu currentSettingsDropDown;

        public static void Populate(Control commandBar, string selectedMenu)
        {
            if (commandBar == null)
            {
                return;
            }

            commandBar.Controls.Clear();

            AddMenu(commandBar, "메인", "\uE7F4", 34, 88, selectedMenu == "MAIN", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToMain();
                }
            });

            AddMenu(commandBar, "공사관리", "\uE90F", 132, 112, selectedMenu == "PROJECT", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToProjectManager();
                }
            });

            AddMenu(commandBar, "AutoCAD 연결", "\uE71B", 254, 140, selectedMenu == "CAD", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.ShowAutoCadEnvironmentCheck();
                }
            });

            AddMenu(commandBar, "도면 추출", "\uE896", 404, 118, selectedMenu == "EXTRACT", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.ShowAutoCadExtractGuide();
                }
            });

            AddMenu(commandBar, "BarList", "\uE8A5", 532, 104, selectedMenu == "BARLIST", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToBarList("", "", "", "", "");
                }
            });

            AddMenu(commandBar, "ERP", "\uE774", 646, 76, selectedMenu == "ERP", delegate(Control source)
            {
                OpenErpInDefaultBrowser(source);
            });

            OviaMenuButton settings = AddMenu(commandBar, "환경 설정 \uE70D", "\uE713", 732, 142, selectedMenu == "SETTINGS", null);
            settings.Click += delegate
            {
                ToggleSettingsDropDown(settings);
            };

            AddAutoCadStatusIndicator(commandBar);
        }

        private static void ToggleSettingsDropDown(Control settingsButton)
        {
            if (settingsButton == null || settingsButton.IsDisposed)
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

            menu.AddItem("BarList 항목 매핑", "\uE8A5", delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                menu.CloseImmediate();
                currentSettingsDropDown = null;

                if (navigator != null)
                {
                    navigator.NavigateToBarListMapping();
                }
            });

            menu.AddItem("이형철근 단위중량표", "\uE9D9", delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                menu.CloseImmediate();
                currentSettingsDropDown = null;

                if (navigator != null)
                {
                    navigator.NavigateToRebarUnitWeightTable();
                }
            });

            menu.AddItem("백업하기", "\uE74E", delegate
            {
                menu.CloseImmediate();
                currentSettingsDropDown = null;
                ShowBackupGuide(settingsButton);
            });

            menu.AddItem("시스템 설정", "\uE713", delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settingsButton);
                menu.CloseImmediate();
                currentSettingsDropDown = null;

                if (navigator != null)
                {
                    navigator.NavigateToSystemSettings();
                }
            });

            menu.AddItem("버전정보", "\uE946", delegate
            {
                menu.CloseImmediate();
                currentSettingsDropDown = null;
                ShowVersionInfo(settingsButton);
            });

            menu.Closed += delegate
            {
                if (currentSettingsDropDown == menu)
                {
                    currentSettingsDropDown = null;
                }
            };

            menu.ShowBelow(settingsButton);
        }

        private static void OpenErpInDefaultBrowser(Control source)
        {
            OviaSystemSettings settings = OviaSystemSettingsStore.Load();
            string erpUrl = settings == null || settings.ErpLoginUrl == null ? "" : settings.ErpLoginUrl.Trim();

            if (erpUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소가 아직 설정되지 않았습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 연결 주소를 먼저 저장해주세요.",
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            string browserUrl = NormalizeErpBrowserUrl(erpUrl);

            if (browserUrl == "")
            {
                MessageBox.Show(
                    "ERP 연결 주소 형식이 올바르지 않습니다.\r\n\r\n환경설정 > 시스템 설정에서 ERP 로그인페이지 URL을 다시 확인해주세요.\r\n\r\n입력값: " + erpUrl,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = browserUrl;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ERP 연결 주소를 기본 웹 브라우저로 여는 중 오류가 발생했습니다.\r\n\r\n주소: " + browserUrl + "\r\n\r\n" + ex.Message,
                    "OVIA ERP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private static string NormalizeErpBrowserUrl(string value)
        {
            string url = value == null ? "" : value.Trim();

            if (url == "")
            {
                return "";
            }

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return uri.AbsoluteUri;
                }

                return "";
            }

            string lower = url.ToLowerInvariant();
            string prefix = "https://";

            if (lower.StartsWith("localhost") ||
                lower.StartsWith("127.") ||
                lower.StartsWith("10.") ||
                lower.StartsWith("192.168.") ||
                lower.Contains(":"))
            {
                prefix = "http://";
            }

            string candidate = prefix + url;

            if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) &&
                (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return uri.AbsoluteUri;
            }

            return "";
        }

        private static void ShowBackupGuide(Control source)
        {
            string oviaFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA"
            );

            MessageBox.Show(
                "백업하기 메뉴가 준비되었습니다.\r\n\r\n" +
                "현재 백업 대상 기본 폴더:\r\n" + oviaFolder + "\r\n\r\n" +
                "다음 단계에서 이 메뉴를 실제 ZIP 백업 생성 기능으로 연결하겠습니다.",
                "OVIA 백업하기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private static void ShowVersionInfo(Control source)
        {
            IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
            string userId = navigator == null ? "" : navigator.CurrentUserId;
            bool canEdit = OviaSystemSettingsStore.IsSuperAdminUser(userId);
            string displayVersion = OviaSystemSettingsStore.GetDisplayVersionText();

            if (!canEdit)
            {
                ShowVersionInfoMessage(displayVersion);
                return;
            }

            DialogResult result = MessageBox.Show(
                "OVIA / 오비아\r\n" +
                "Operation + Value + Intelligence + Automation\r\n\r\n" +
                "현재 버전: " + displayVersion + "\r\n\r\n" +
                "최고관리자 권한으로 버전정보를 수정하시겠습니까?",
                "OVIA 버전정보",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            string currentVersion = OviaSystemSettingsStore.GetConfiguredVersionText();
            if (currentVersion == "")
            {
                currentVersion = "1.0.0";
            }

            string newVersion;
            Form owner = source == null ? null : source.FindForm();
            if (!OviaVersionInfoEditDialog.TryEdit(owner, currentVersion, out newVersion))
            {
                return;
            }

            OviaSystemSettings settings = OviaSystemSettingsStore.Load();
            settings.VersionText = OviaSystemSettingsStore.NormalizeVersionText(newVersion);
            OviaSystemSettingsStore.Save(settings);

            MessageBox.Show(
                "버전정보가 저장되었습니다.\r\n\r\n로그인 화면 하단에는 다음부터 " + OviaSystemSettingsStore.GetDisplayVersionText() + " 로 표시됩니다.",
                "OVIA 버전정보",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private static void ShowVersionInfoMessage(string displayVersion)
        {
            MessageBox.Show(
                "OVIA / 오비아\r\n" +
                "Operation + Value + Intelligence + Automation\r\n\r\n" +
                "버전: " + displayVersion + "\r\n" +
                "모드: 개발/테스트 버전\r\n\r\n" +
                "AutoCAD BarList 추출 및 공사별 철근 데이터 관리 솔루션입니다.",
                "OVIA 버전정보",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private static void AddAutoCadStatusIndicator(Control commandBar)
        {
            Panel statusPanel = new Panel();
            statusPanel.Size = new Size(165, 30);
            statusPanel.BackColor = Color.White;
            statusPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            commandBar.Controls.Add(statusPanel);

            OviaStatusLamp lamp = new OviaStatusLamp();
            lamp.Location = new Point(0, 3);
            lamp.Size = new Size(24, 24);
            lamp.BackColor = Color.White;
            statusPanel.Controls.Add(lamp);

            Label label = new Label();
            label.AutoSize = false;
            label.Location = new Point(28, 0);
            label.Size = new Size(132, 30);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontKorean(9.5F, FontStyle.Bold);
            label.BackColor = Color.White;
            statusPanel.Controls.Add(label);

            ToolTip statusToolTip = new ToolTip();
            statusToolTip.AutoPopDelay = 5000;
            statusToolTip.InitialDelay = 350;
            statusToolTip.ReshowDelay = 100;
            statusToolTip.ShowAlways = true;

            PositionAutoCadStatusIndicator(commandBar, statusPanel);
            UpdateAutoCadStatusIndicator(lamp, label, statusPanel, statusToolTip);

            bool statusTimerDisposed = false;
            Timer statusTimer = new Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += delegate
            {
                if (statusPanel.IsDisposed || commandBar.IsDisposed || commandBar.FindForm() == null)
                {
                    if (!statusTimerDisposed)
                    {
                        statusTimer.Stop();
                        statusTimer.Dispose();
                        statusTimerDisposed = true;
                    }
                    return;
                }

                UpdateAutoCadStatusIndicator(lamp, label, statusPanel, statusToolTip);
            };
            statusTimer.Start();

            commandBar.Resize += delegate
            {
                PositionAutoCadStatusIndicator(commandBar, statusPanel);
            };

            commandBar.Disposed += delegate
            {
                if (!statusTimerDisposed)
                {
                    statusTimer.Stop();
                    statusTimer.Dispose();
                    statusTimerDisposed = true;
                }
            };
        }

        private static void PositionAutoCadStatusIndicator(Control commandBar, Control statusPanel)
        {
            if (commandBar == null || statusPanel == null)
            {
                return;
            }

            int x = Math.Max(900, commandBar.ClientSize.Width - statusPanel.Width - 34);
            statusPanel.Location = new Point(x, 10);
        }

        private static void UpdateAutoCadStatusIndicator(OviaStatusLamp lamp, Label label, Control statusPanel, ToolTip statusToolTip)
        {
            OviaEnvironmentReport report = OviaEnvironmentChecker.CheckForUi();
            bool isReady = report != null && report.IsCurrentDevelopmentAutoCadReady();

            if (lamp != null)
            {
                lamp.IsActive = isReady;
                lamp.Invalidate();
            }

            if (label != null)
            {
                label.Text = report == null ? "환경 점검 필요" : report.GetDesktopAutoCadStatusText();

                if (isReady)
                {
                    label.ForeColor = OviaFluentTheme.Success;
                }
                else if (report != null && report.OverallStatus == OviaEnvironmentStatus.Warning && report.RecommendedAutoCad != null && report.RecommendedAutoCad.Year != 2027)
                {
                    label.ForeColor = Color.FromArgb(176, 111, 0);
                }
                else
                {
                    label.ForeColor = OviaFluentTheme.Danger;
                }
            }

            if (statusToolTip != null && statusPanel != null && report != null)
            {
                statusToolTip.SetToolTip(statusPanel, report.GetDesktopAutoCadDetailText());
            }
        }

        private static OviaMenuButton AddMenu(Control parent, string text, string iconText, int left, int width, bool selected, Action<Control> action)
        {
            OviaMenuButton menu = new OviaMenuButton();
            menu.Text = text;
            menu.IconText = iconText;
            menu.Location = new Point(left, 6);
            menu.Size = new Size(width, 38);
            menu.Selected = selected;
            menu.Click += delegate
            {
                if (action != null)
                {
                    action(menu);
                }
            };
            parent.Controls.Add(menu);
            return menu;
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

    internal class OviaVersionInfoEditDialog : Form
    {
        private TextBox txtVersion;
        private Button btnOk;
        private Button btnCancel;
        private string versionText = "";

        public string VersionText
        {
            get { return versionText; }
        }

        public OviaVersionInfoEditDialog(string currentVersion)
        {
            BuildUI(currentVersion == null ? "" : currentVersion);
        }

        public static bool TryEdit(Form owner, string currentVersion, out string newVersion)
        {
            newVersion = "";

            using (OviaVersionInfoEditDialog dialog = new OviaVersionInfoEditDialog(currentVersion))
            {
                DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

                if (result != DialogResult.OK)
                {
                    return false;
                }

                newVersion = dialog.VersionText;
                return true;
            }
        }

        private void BuildUI(string currentVersion)
        {
            OviaFluentTheme.ApplyForm(this);
            this.Text = "OVIA 버전정보 수정";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(430, 210);
            this.BackColor = OviaFluentTheme.AppBackground;
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);

            Label title = new Label();
            title.Text = "버전정보";
            title.AutoSize = true;
            title.Location = new Point(28, 24);
            title.Font = OviaFluentTheme.FontTitle(16F, FontStyle.Bold);
            title.ForeColor = OviaFluentTheme.TextPrimary;
            title.BackColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "로그인 화면 하단에 표시할 버전 값을 입력하세요.";
            desc.AutoSize = false;
            desc.Location = new Point(30, 58);
            desc.Size = new Size(360, 22);
            desc.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            desc.ForeColor = OviaFluentTheme.TextSecondary;
            desc.BackColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(desc);

            Label prefix = new Label();
            prefix.Text = "Version";
            prefix.AutoSize = false;
            prefix.TextAlign = ContentAlignment.MiddleCenter;
            prefix.Location = new Point(30, 96);
            prefix.Size = new Size(78, 38);
            prefix.Font = OviaFluentTheme.FontButton(9F, FontStyle.Bold);
            prefix.ForeColor = OviaFluentTheme.TextPrimary;
            prefix.BackColor = Color.White;
            prefix.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(prefix);

            txtVersion = new TextBox();
            txtVersion.Location = new Point(118, 103);
            txtVersion.Size = new Size(274, 24);
            txtVersion.BorderStyle = BorderStyle.FixedSingle;
            txtVersion.Font = OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            txtVersion.Text = OviaSystemSettingsStore.NormalizeVersionText(currentVersion);
            this.Controls.Add(txtVersion);

            btnOk = new Button();
            btnOk.Text = "저장";
            btnOk.Location = new Point(222, 154);
            btnOk.Size = new Size(82, 34);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.BackColor = Color.FromArgb(17, 17, 19);
            btnOk.ForeColor = Color.White;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Font = OviaFluentTheme.FontButton(9F, FontStyle.Bold);
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "취소";
            btnCancel.Location = new Point(314, 154);
            btnCancel.Size = new Size(78, 34);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.BackColor = Color.White;
            btnCancel.ForeColor = OviaFluentTheme.TextPrimary;
            btnCancel.FlatAppearance.BorderColor = OviaFluentTheme.ControlBorder;
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.Font = OviaFluentTheme.FontButton(9F, FontStyle.Regular);
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string value = txtVersion == null ? "" : txtVersion.Text.Trim();
            value = OviaSystemSettingsStore.NormalizeVersionText(value);

            if (value == "")
            {
                MessageBox.Show(
                    "버전정보를 입력해 주세요.",
                    "OVIA 버전정보",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtVersion != null)
                {
                    txtVersion.Focus();
                }

                return;
            }

            versionText = value;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    public class FrmWorkspaceShell : Form, IOviaWorkspaceNavigator
    {
        private readonly string companyId;
        private readonly string userId;
        private Panel hostPanel;
        private Form currentScreen;

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

            hostPanel = new Panel();
            hostPanel.Dock = DockStyle.Fill;
            hostPanel.BackColor = OviaFluentTheme.AppBackground;
            this.Controls.Add(hostPanel);
        }

        public void NavigateToMain()
        {
            this.Close();
        }

        public void NavigateToProjectManager()
        {
            this.Text = "OVIA 공사관리";
            ShowScreen(new FrmProjectManager(companyId, userId));
        }

        public void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus)
        {
            this.Text = "OVIA 공사별 BarList";
            ShowScreen(new FrmProjectBarListList(companyId, userId, projectNo, projectName, clientName, projectStatus));
        }

        public void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath)
        {
            string filePath = initialFilePath == null ? "" : initialFilePath;
            this.Text = filePath.Trim() == "" ? "OVIA 신규 BarList 등록" : "OVIA BarList";
            ShowScreen(new FrmBarList(companyId, userId, projectNo, projectName, clientName, projectStatus, filePath));
        }

        public void NavigateToBarListMapping()
        {
            this.Text = "OVIA BarList 항목 매핑";
            ShowScreen(new FrmBarListMappingManager(companyId, userId));
        }

        public void NavigateToRebarUnitWeightTable()
        {
            this.Text = "OVIA 이형철근 단위중량표";
            ShowScreen(new FrmRebarUnitWeightTable(companyId, userId));
        }

        public void NavigateToSystemSettings()
        {
            if (!OviaSystemSettingsStore.IsSuperAdminUser(userId))
            {
                MessageBox.Show(
                    "시스템 설정은 최고관리자만 접근할 수 있습니다.\r\n\r\n현재 사용자 ID: " + userId,
                    "OVIA 권한 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            this.Text = "OVIA 시스템 설정";
            ShowScreen(new FrmSystemSettings(companyId, userId));
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
            this.Close();
        }

        private void ShowScreen(Form nextScreen)
        {
            if (nextScreen == null)
            {
                return;
            }

            IOviaWorkspaceScreen currentWorkspaceScreen = currentScreen as IOviaWorkspaceScreen;

            if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
            {
                nextScreen.Dispose();
                return;
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
                nextScreen.Bounds = hostPanel.ClientRectangle;
                ApplyWorkspaceLayout(nextScreen);
                nextScreen.BringToFront();

                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (currentScreen == nextScreen && !nextScreen.IsDisposed)
                        {
                            nextScreen.Bounds = hostPanel.ClientRectangle;
                            ApplyWorkspaceLayout(nextScreen);
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

            if (currentWorkspaceScreen != null && !currentWorkspaceScreen.CanLeaveWorkspaceScreen())
            {
                e.Cancel = true;
                return;
            }

            if (currentWorkspaceScreen != null)
            {
                currentWorkspaceScreen.BeforeLeaveWorkspaceScreen();
            }
        }
    }
}

