using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal interface IOviaWorkspaceNavigator
    {
        void NavigateToMain();
        void NavigateToProjectManager();
        void NavigateToProjectBarListList(string projectNo, string projectName, string clientName, string projectStatus);
        void NavigateToBarList(string projectNo, string projectName, string clientName, string projectStatus, string initialFilePath);
        void NavigateToBarListMapping();
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
        public static void Populate(Control commandBar, string selectedMenu)
        {
            if (commandBar == null)
            {
                return;
            }

            commandBar.Controls.Clear();

            AddMenu(commandBar, "메인", 34, selectedMenu == "MAIN", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToMain();
                }
            });

            AddMenu(commandBar, "공사관리", 130, selectedMenu == "PROJECT", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToProjectManager();
                }
            });

            AddMenu(commandBar, "AutoCAD 연결", 238, selectedMenu == "CAD", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.ShowAutoCadEnvironmentCheck();
                }
            });

            AddMenu(commandBar, "도면 추출", 366, selectedMenu == "EXTRACT", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.ShowAutoCadExtractGuide();
                }
            });

            AddMenu(commandBar, "BarList", 474, selectedMenu == "BARLIST", delegate(Control source)
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(source);
                if (navigator != null)
                {
                    navigator.NavigateToBarList("", "", "", "", "");
                }
            });

            OviaMenuButton settings = AddMenu(commandBar, "환경 설정 \uE70D", 570, selectedMenu == "SETTINGS", null);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            menu.BackColor = Color.White;
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.Renderer = new OviaExplorerDropDownRenderer();

            ToolStripMenuItem mapping = new ToolStripMenuItem("BarList 항목 매핑");
            mapping.Padding = new Padding(8, 6, 18, 6);
            mapping.Click += delegate
            {
                IOviaWorkspaceNavigator navigator = OviaWorkspaceNavigation.FindNavigator(settings);
                if (navigator != null)
                {
                    navigator.NavigateToBarListMapping();
                }
            };

            menu.Items.Add(mapping);
            settings.Click += delegate
            {
                menu.Show(settings, new Point(0, settings.Height));
            };

            AddAutoCadStatusIndicator(commandBar);
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
            label.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
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

            int x = Math.Max(760, commandBar.ClientSize.Width - statusPanel.Width - 34);
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

        private static OviaMenuButton AddMenu(Control parent, string text, int left, bool selected, Action<Control> action)
        {
            OviaMenuButton menu = new OviaMenuButton();
            menu.Text = text;
            menu.Location = new Point(left, 10);
            menu.Size = new Size(text.Length > 7 ? 122 : 92, 30);
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
            this.Font = OviaFluentTheme.FontKorean(9F, FontStyle.Regular);
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

