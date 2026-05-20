using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OVIA.Desktop
{
    public class FrmMain : Form
    {
        private readonly string companyId;
        private readonly string userId;

        private Label lblAutoCadValue;
        private Label lblAutoCadNote;
        private Label lblAutoCadRunStatus;
        private Label lblAutoCadRunNote;
        private OviaStatusLamp autoCadStatusLamp;
        private Timer autoCadStatusTimer;

        private readonly Color BrandIndigo = Color.FromArgb(37, 30, 130);
        private readonly Color BrandViolet = Color.FromArgb(91, 49, 225);
        private readonly Color BrandCyan = Color.FromArgb(0, 174, 239);
        private readonly Color SurfaceColor = Color.FromArgb(244, 248, 255);
        private readonly Color TextDark = Color.FromArgb(28, 33, 72);
        private readonly Color TextSub = Color.FromArgb(102, 111, 135);

        public FrmMain(string companyId, string userId)
        {
            this.companyId = companyId;
            this.userId = userId;

            BuildMainUI();
        }

        private void BuildMainUI()
        {
            this.SuspendLayout();
            this.Controls.Clear();

            this.Text = "OVIA";
            this.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ClientSize = new Size(1080, 680);
            this.BackColor = SurfaceColor;

            GradientPanel bg = new GradientPanel();
            bg.Dock = DockStyle.Fill;
            bg.StartColor = Color.FromArgb(249, 251, 255);
            bg.EndColor = Color.FromArgb(235, 242, 253);
            this.Controls.Add(bg);

            BuildSidePanel(bg);
            BuildHeader(bg);
            BuildStatusCards(bg);
            BuildActionCards(bg);
            BuildFooter(bg);

            this.ResumeLayout(false);

            StartAutoCadStatusTimer();
        }

        private void BuildSidePanel(Control parent)
        {
            Panel side = new Panel();
            side.Location = new Point(0, 0);
            side.Size = new Size(250, 680);
            side.BackColor = Color.FromArgb(28, 24, 93);
            parent.Controls.Add(side);

            Label logo = new Label();
            logo.Text = "OVIA";
            logo.AutoSize = true;
            logo.Font = new Font("Segoe UI", 28F, FontStyle.Bold);
            logo.ForeColor = Color.White;
            logo.Location = new Point(34, 36);
            side.Controls.Add(logo);

            Label sub = new Label();
            sub.Text = "Engineering Workflow";
            sub.AutoSize = true;
            sub.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            sub.ForeColor = Color.FromArgb(190, 196, 235);
            sub.Location = new Point(38, 86);
            side.Controls.Add(sub);

            AddMenu(side, "대시보드", 150, true);
            OviaMenuButton projectMenu = AddMenu(side, "공사관리", 205, false);
            projectMenu.Click += OpenProjectManager_Click;

            AddMenu(side, "AutoCAD 연결", 260, false);
            AddMenu(side, "도면 추출", 315, false);

            OviaMenuButton barListMenu = AddMenu(side, "BarList", 370, false);
            barListMenu.Click += OpenBarList_Click;

            AddMenu(side, "환경 설정", 425, false);

            Label account = new Label();
            account.Text = "회사 ID : " + companyId + "\r\n사용자 ID : " + userId;
            account.AutoSize = false;
            account.Size = new Size(190, 48);
            account.Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            account.ForeColor = Color.FromArgb(215, 220, 248);
            account.Location = new Point(34, 580);
            side.Controls.Add(account);
        }

        private OviaMenuButton AddMenu(Control parent, string text, int top, bool selected)
        {
            OviaMenuButton menu = new OviaMenuButton();
            menu.Text = text;
            menu.Location = new Point(25, top);
            menu.Size = new Size(200, 40);
            menu.Selected = selected;
            parent.Controls.Add(menu);

            return menu;
        }

        private void BuildHeader(Control parent)
        {
            Label title = new Label();
            title.Text = "OVIA 대시보드";
            title.AutoSize = true;
            title.Font = new Font("맑은 고딕", 22F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(300, 45);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = "로그인에 성공했습니다. AutoCAD 연결과 도면 추출 기능을 준비합니다.";
            desc.AutoSize = true;
            desc.Font = new Font("맑은 고딕", 10F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(304, 90);
            parent.Controls.Add(desc);

            Panel cadStatusBox = new Panel();
            cadStatusBox.Location = new Point(735, 50);
            cadStatusBox.Size = new Size(175, 40);
            cadStatusBox.BackColor = SurfaceColor;
            parent.Controls.Add(cadStatusBox);

            autoCadStatusLamp = new OviaStatusLamp();
            autoCadStatusLamp.Location = new Point(0, 8);
            autoCadStatusLamp.Size = new Size(24, 24);
            autoCadStatusLamp.IsActive = false;
            cadStatusBox.Controls.Add(autoCadStatusLamp);

            lblAutoCadRunStatus = new Label();
            lblAutoCadRunStatus.Text = "AutoCAD 비활성";
            lblAutoCadRunStatus.AutoSize = true;
            lblAutoCadRunStatus.Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold);
            lblAutoCadRunStatus.ForeColor = Color.FromArgb(210, 78, 78);
            lblAutoCadRunStatus.BackColor = SurfaceColor;
            lblAutoCadRunStatus.Location = new Point(30, 2);
            cadStatusBox.Controls.Add(lblAutoCadRunStatus);

            lblAutoCadRunNote = new Label();
            lblAutoCadRunNote.Text = "실행 필요";
            lblAutoCadRunNote.AutoSize = true;
            lblAutoCadRunNote.Font = new Font("맑은 고딕", 8F, FontStyle.Regular);
            lblAutoCadRunNote.ForeColor = TextSub;
            lblAutoCadRunNote.BackColor = SurfaceColor;
            lblAutoCadRunNote.Location = new Point(31, 21);
            cadStatusBox.Controls.Add(lblAutoCadRunNote);

            OviaSmallButton logout = new OviaSmallButton();
            logout.Text = "로그아웃";
            logout.Location = new Point(930, 52);
            logout.Size = new Size(95, 34);
            logout.Click += Logout_Click;
            parent.Controls.Add(logout);
        }

        private void BuildStatusCards(Control parent)
        {
            Label dummyValue1;
            Label dummyNote1;
            Label dummyValue2;
            Label dummyNote2;

            AddStatusCard(
                parent,
                "라이선스 상태",
                "정상",
                "셀먼 OVIA 관리자 인증 대기",
                new Point(300, 140),
                BrandViolet,
                out dummyValue1,
                out dummyNote1
            );

            AddStatusCard(
                parent,
                "AutoCAD 상태",
                "비활성",
                "AutoCAD를 실행해주세요.",
                new Point(550, 140),
                BrandCyan,
                out lblAutoCadValue,
                out lblAutoCadNote
            );

            AddStatusCard(
                parent,
                "프로그램 버전",
                "1.0.0",
                "초기 개발 테스트 버전",
                new Point(800, 140),
                BrandIndigo,
                out dummyValue2,
                out dummyNote2
            );
        }

        private void AddStatusCard(Control parent, string title, string value, string note, Point location, Color accent, out Label valueLabel, out Label noteLabel)
        {
            valueLabel = null;
            noteLabel = null;

            OviaDashboardCard card = new OviaDashboardCard();
            card.Location = location;
            card.Size = new Size(220, 130);
            card.SurfaceColor = SurfaceColor;
            card.AccentColor = accent;
            parent.Controls.Add(card);

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
            lblTitle.ForeColor = TextSub;
            lblTitle.BackColor = Color.White;
            lblTitle.Location = new Point(20, 18);
            card.Controls.Add(lblTitle);

            Label lblValue = new Label();
            lblValue.Text = value;
            lblValue.AutoSize = true;
            lblValue.Font = new Font("맑은 고딕", 20F, FontStyle.Bold);
            lblValue.ForeColor = TextDark;
            lblValue.BackColor = Color.White;
            lblValue.Location = new Point(18, 45);
            card.Controls.Add(lblValue);

            Label lblNote = new Label();
            lblNote.Text = note;
            lblNote.AutoSize = false;
            lblNote.Size = new Size(180, 34);
            lblNote.Font = new Font("맑은 고딕", 8.5F, FontStyle.Regular);
            lblNote.ForeColor = TextSub;
            lblNote.BackColor = Color.White;
            lblNote.Location = new Point(20, 92);
            card.Controls.Add(lblNote);

            valueLabel = lblValue;
            noteLabel = lblNote;
        }

        private void BuildActionCards(Control parent)
        {
            OviaLargeCard cadCard = new OviaLargeCard();
            cadCard.Location = new Point(300, 310);
            cadCard.Size = new Size(345, 235);
            cadCard.SurfaceColor = SurfaceColor;
            parent.Controls.Add(cadCard);

            Label cadTitle = new Label();
            cadTitle.Text = "AutoCAD 연결";
            cadTitle.AutoSize = true;
            cadTitle.Font = new Font("맑은 고딕", 16F, FontStyle.Bold);
            cadTitle.ForeColor = TextDark;
            cadTitle.BackColor = Color.White;
            cadTitle.Location = new Point(28, 28);
            cadCard.Controls.Add(cadTitle);

            Label cadDesc = new Label();
            cadDesc.Text = "사용자 PC에 설치된 AutoCAD 버전을 확인하고,\r\n지원 가능한 버전에 맞는 OVIA 연동 모듈을\r\n준비합니다.";
            cadDesc.AutoSize = false;
            cadDesc.Size = new Size(290, 70);
            cadDesc.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            cadDesc.ForeColor = TextSub;
            cadDesc.BackColor = Color.White;
            cadDesc.Location = new Point(30, 72);
            cadCard.Controls.Add(cadDesc);

            OviaActionButton cadButton = new OviaActionButton();
            cadButton.Text = "AutoCAD 감지 시작";
            cadButton.Location = new Point(30, 160);
            cadButton.Size = new Size(280, 44);
            cadButton.StartColor = BrandViolet;
            cadButton.EndColor = BrandIndigo;
            cadButton.Click += DetectAutoCad_Click;
            cadCard.Controls.Add(cadButton);

            OviaLargeCard extractCard = new OviaLargeCard();
            extractCard.Location = new Point(680, 310);
            extractCard.Size = new Size(345, 235);
            extractCard.SurfaceColor = SurfaceColor;
            parent.Controls.Add(extractCard);

            Label extractTitle = new Label();
            extractTitle.Text = "도면 추출";
            extractTitle.AutoSize = true;
            extractTitle.Font = new Font("맑은 고딕", 16F, FontStyle.Bold);
            extractTitle.ForeColor = TextDark;
            extractTitle.BackColor = Color.White;
            extractTitle.Location = new Point(28, 28);
            extractCard.Controls.Add(extractTitle);

            Label extractDesc = new Label();
            extractDesc.Text = "AutoCAD 도면에서 선택 영역의 문자와 표를\r\n읽어 BarList 후보 데이터로 정리합니다.\r\n현재는 화면 구성 단계입니다.";
            extractDesc.AutoSize = false;
            extractDesc.Size = new Size(290, 70);
            extractDesc.Font = new Font("맑은 고딕", 9.5F, FontStyle.Regular);
            extractDesc.ForeColor = TextSub;
            extractDesc.BackColor = Color.White;
            extractDesc.Location = new Point(30, 72);
            extractCard.Controls.Add(extractDesc);

            OviaActionButton extractButton = new OviaActionButton();
            extractButton.Text = "도면 추출 준비";
            extractButton.Location = new Point(30, 160);
            extractButton.Size = new Size(280, 44);
            extractButton.StartColor = BrandCyan;
            extractButton.EndColor = BrandViolet;
            extractButton.Click += ExtractReady_Click;
            extractCard.Controls.Add(extractButton);
        }

        private void BuildFooter(Control parent)
        {
            Label footer = new Label();
            footer.Text = "© 2026 CELMON. All rights reserved.   |   OVIA Desktop";
            footer.AutoSize = true;
            footer.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            footer.ForeColor = TextSub;
            footer.BackColor = SurfaceColor;
            footer.Location = new Point(300, 635);
            parent.Controls.Add(footer);
        }

        private void DetectAutoCad_Click(object sender, EventArgs e)
        {
            List<AutoCadInstallInfo> installs = AutoCadDetector.FindInstalledAutoCad();

            if (installs.Count == 0)
            {
                lblAutoCadValue.Text = "미감지";
                lblAutoCadNote.Text = "AutoCAD 일반 버전을 찾지 못했습니다.";

                MessageBox.Show(
                    "설치된 AutoCAD 일반 버전을 찾지 못했습니다.\r\n\r\nAutoCAD LT만 설치되어 있거나, AutoCAD가 설치되어 있지 않을 수 있습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                UpdateAutoCadRunStatus();

                return;
            }

            AutoCadInstallInfo selected = installs[0];

            lblAutoCadValue.Text = selected.YearText;
            lblAutoCadNote.Text = selected.PluginGroup;

            MessageBox.Show(
                selected.GetDisplayText(),
                "OVIA AutoCAD 감지 결과",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            UpdateAutoCadRunStatus();
        }

        private void OpenProjectManager_Click(object sender, EventArgs e)
        {
            FrmProjectManager form = new FrmProjectManager(companyId, userId);
            form.ShowDialog(this);
        }

        private void OpenBarList_Click(object sender, EventArgs e)
        {
            FrmBarList form = new FrmBarList(companyId, userId);
            form.ShowDialog(this);
        }

        private void ExtractReady_Click(object sender, EventArgs e)
        {
            UpdateAutoCadRunStatus();

            if (!AutoCadRuntimeChecker.IsAutoCadRunning())
            {
                MessageBox.Show(
                    "AutoCAD 비활성 상태입니다.\r\n\r\n도면 추출 기능을 사용하려면 먼저 AutoCAD를 실행하고 DWG 도면을 열어주세요.",
                    "OVIA AutoCAD 비활성",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            MessageBox.Show(
                "AutoCAD 활성 상태입니다.\r\n\r\nAutoCAD에서 OVIA 플러그인 DLL을 NETLOAD로 로드한 뒤 OVIABOX / OVIABOXTABLE 명령어를 사용할 수 있습니다.",
                "OVIA AutoCAD 활성",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void StartAutoCadStatusTimer()
        {
            if (autoCadStatusTimer != null)
            {
                autoCadStatusTimer.Stop();
                autoCadStatusTimer.Dispose();
                autoCadStatusTimer = null;
            }

            autoCadStatusTimer = new Timer();
            autoCadStatusTimer.Interval = 2000;
            autoCadStatusTimer.Tick += AutoCadStatusTimer_Tick;
            autoCadStatusTimer.Start();

            UpdateAutoCadRunStatus();
        }

        private void AutoCadStatusTimer_Tick(object sender, EventArgs e)
        {
            UpdateAutoCadRunStatus();
        }

        private void UpdateAutoCadRunStatus()
        {
            bool isRunning = AutoCadRuntimeChecker.IsAutoCadRunning();

            if (autoCadStatusLamp != null)
            {
                autoCadStatusLamp.IsActive = isRunning;
                autoCadStatusLamp.Invalidate();
            }

            if (lblAutoCadRunStatus != null)
            {
                lblAutoCadRunStatus.Text = isRunning ? "AutoCAD 활성" : "AutoCAD 비활성";
                lblAutoCadRunStatus.ForeColor = isRunning ? Color.FromArgb(18, 166, 91) : Color.FromArgb(210, 78, 78);
            }

            if (lblAutoCadRunNote != null)
            {
                lblAutoCadRunNote.Text = isRunning ? "acad.exe 실행 중" : "AutoCAD 실행 필요";
            }

            if (lblAutoCadValue != null)
            {
                lblAutoCadValue.Text = isRunning ? "활성" : "비활성";
                lblAutoCadValue.ForeColor = isRunning ? Color.FromArgb(18, 166, 91) : Color.FromArgb(210, 78, 78);
            }

            if (lblAutoCadNote != null)
            {
                lblAutoCadNote.Text = isRunning ? "AutoCAD가 실행 중입니다." : "AutoCAD를 실행해주세요.";
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (autoCadStatusTimer != null)
            {
                autoCadStatusTimer.Stop();
                autoCadStatusTimer.Dispose();
                autoCadStatusTimer = null;
            }

            base.OnFormClosed(e);
        }

        private void Logout_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }

    public class AutoCadInstallInfo
    {
        public string ProductName = "";
        public string VersionKey = "";
        public string InstallPath = "";
        public int Year = 0;
        public bool IsLT = false;

        public string YearText
        {
            get
            {
                if (Year > 0)
                {
                    return Year.ToString();
                }

                return "감지됨";
            }
        }

        public string PluginGroup
        {
            get
            {
                if (IsLT)
                {
                    return "AutoCAD LT는 지원하지 않습니다.";
                }

                if (Year >= 2027)
                {
                    return ".NET 10용 OVIA 모듈 대상";
                }

                if (Year >= 2025 && Year <= 2026)
                {
                    return ".NET 8용 OVIA 모듈 대상";
                }

                if (Year >= 2021 && Year <= 2024)
                {
                    return ".NET Framework 4.8용 OVIA 모듈 대상";
                }

                if (Year >= 2019 && Year <= 2020)
                {
                    return "2차 지원 검토 대상";
                }

                return "지원 버전 추가 검토 필요";
            }
        }

        public string GetDisplayText()
        {
            string text = "";

            text += "AutoCAD 감지 결과\r\n\r\n";
            text += "제품명: " + ProductName + "\r\n";

            if (VersionKey != "")
            {
                text += "버전 키: " + VersionKey + "\r\n";
            }

            if (Year > 0)
            {
                text += "판단 연도: " + Year.ToString() + "\r\n";
            }

            if (InstallPath != "")
            {
                text += "설치 경로: " + InstallPath + "\r\n";
            }

            text += "\r\nOVIA 판단: " + PluginGroup;

            return text;
        }
    }

    public static class AutoCadDetector
    {
        public static List<AutoCadInstallInfo> FindInstalledAutoCad()
        {
            List<AutoCadInstallInfo> results = new List<AutoCadInstallInfo>();

            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry32);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry32);

            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry32);

            RemoveDuplicates(results);
            SortByYearDesc(results);
            RemoveLtOnlyIfGeneralExists(results);

            return results;
        }

        private static void ScanAutoCadRegistryRoot(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");

                if (root == null)
                {
                    return;
                }

                ScanRegistryRecursive(results, root, "", 0);
                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static void ScanRegistryRecursive(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey, int depth)
        {
            if (depth > 4 || key == null)
            {
                return;
            }

            TryReadAutoCadInfo(results, key, versionKey);

            string[] subNames;

            try
            {
                subNames = key.GetSubKeyNames();
            }
            catch
            {
                return;
            }

            int i;

            for (i = 0; i < subNames.Length; i++)
            {
                try
                {
                    RegistryKey sub = key.OpenSubKey(subNames[i]);

                    string nextVersionKey = versionKey;

                    if (nextVersionKey == "")
                    {
                        nextVersionKey = subNames[i];
                    }
                    else
                    {
                        nextVersionKey += "\\" + subNames[i];
                    }

                    ScanRegistryRecursive(results, sub, nextVersionKey, depth + 1);

                    if (sub != null)
                    {
                        sub.Close();
                    }
                }
                catch
                {
                }
            }
        }

        private static void TryReadAutoCadInfo(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey)
        {
            string productName = ReadRegistryString(key, "ProductName");

            if (productName == "")
            {
                productName = ReadRegistryString(key, "DisplayName");
            }

            if (productName == "")
            {
                productName = ReadRegistryString(key, "Product");
            }

            if (productName == "")
            {
                return;
            }

            if (productName.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            AutoCadInstallInfo info = new AutoCadInstallInfo();
            info.ProductName = productName;
            info.VersionKey = versionKey;
            info.InstallPath = ReadPossibleInstallPath(key);
            info.Year = ExtractYear(productName + " " + versionKey);
            info.IsLT = productName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

            results.Add(info);
        }

        private static void ScanUninstallRegistry(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

                if (root == null)
                {
                    return;
                }

                string[] subNames = root.GetSubKeyNames();
                int i;

                for (i = 0; i < subNames.Length; i++)
                {
                    RegistryKey sub = root.OpenSubKey(subNames[i]);

                    if (sub == null)
                    {
                        continue;
                    }

                    string displayName = ReadRegistryString(sub, "DisplayName");

                    if (displayName.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AutoCadInstallInfo info = new AutoCadInstallInfo();
                        info.ProductName = displayName;
                        info.VersionKey = subNames[i];
                        info.InstallPath = ReadPossibleInstallPath(sub);
                        info.Year = ExtractYear(displayName);
                        info.IsLT = displayName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

                        results.Add(info);
                    }

                    sub.Close();
                }

                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static string ReadPossibleInstallPath(RegistryKey key)
        {
            string value = "";

            value = ReadRegistryString(key, "AcadLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "Location");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallDir");
            if (value != "")
            {
                return value;
            }

            return "";
        }

        private static string ReadRegistryString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);

                if (value == null)
                {
                    return "";
                }

                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static int ExtractYear(string text)
        {
            Match match = Regex.Match(text, @"20\d{2}");

            if (!match.Success)
            {
                return 0;
            }

            int year = 0;
            int.TryParse(match.Value, out year);

            return year;
        }

        private static void RemoveDuplicates(List<AutoCadInstallInfo> list)
        {
            int i;
            int j;

            for (i = list.Count - 1; i >= 0; i--)
            {
                for (j = 0; j < i; j++)
                {
                    if (
                        string.Equals(list[i].ProductName, list[j].ProductName, StringComparison.OrdinalIgnoreCase) &&
                        list[i].Year == list[j].Year
                    )
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static void SortByYearDesc(List<AutoCadInstallInfo> list)
        {
            list.Sort(delegate (AutoCadInstallInfo a, AutoCadInstallInfo b)
            {
                return b.Year.CompareTo(a.Year);
            });
        }

        private static void RemoveLtOnlyIfGeneralExists(List<AutoCadInstallInfo> list)
        {
            bool hasGeneral = false;
            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (!list[i].IsLT)
                {
                    hasGeneral = true;
                    break;
                }
            }

            if (!hasGeneral)
            {
                return;
            }

            for (i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsLT)
                {
                    list.RemoveAt(i);
                }
            }
        }
    }

    public static class AutoCadRuntimeChecker
    {
        public static bool IsAutoCadRunning()
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
    }

    public class OviaMenuButton : Control
    {
        public bool Selected = false;

        public OviaMenuButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            if (Selected)
            {
                using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 8))
                {
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
            }

            Color color = Selected ? Color.FromArgb(37, 30, 130) : Color.FromArgb(215, 220, 248);

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 10F, FontStyle.Bold),
                rect,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding
            );

            base.OnPaint(e);
        }
    }

    public class OviaDashboardCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);
        public Color AccentColor = Color.FromArgb(91, 49, 225);

        public OviaDashboardCard()
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

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 14))
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

            using (SolidBrush accent = new SolidBrush(AccentColor))
            {
                e.Graphics.FillRectangle(accent, 0, 0, 5, this.Height);
            }

            base.OnPaint(e);
        }
    }

    public class OviaLargeCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

        public OviaLargeCard()
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

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 18))
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

    public class OviaActionButton : Control
    {
        public Color StartColor = Color.FromArgb(91, 49, 225);
        public Color EndColor = Color.FromArgb(37, 30, 130);

        private bool hover;

        public OviaActionButton()
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

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color start = hover ? Color.FromArgb(105, 64, 236) : StartColor;
            Color end = hover ? Color.FromArgb(50, 38, 150) : EndColor;

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 8))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, start, end, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 10.5F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public class OviaStatusLamp : Control
    {
        public bool IsActive = false;

        public OviaStatusLamp()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(248, 251, 255);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color mainColor = IsActive ? Color.FromArgb(25, 210, 115) : Color.FromArgb(230, 75, 75);
            Color glowColor = IsActive ? Color.FromArgb(80, 25, 210, 115) : Color.FromArgb(80, 230, 75, 75);

            Rectangle glowRect = new Rectangle(2, 2, this.Width - 4, this.Height - 4);
            Rectangle mainRect = new Rectangle(6, 6, this.Width - 12, this.Height - 12);
            Rectangle pointRect = new Rectangle(9, 9, this.Width - 18, this.Height - 18);

            using (SolidBrush glow = new SolidBrush(glowColor))
            {
                e.Graphics.FillEllipse(glow, glowRect);
            }

            using (SolidBrush main = new SolidBrush(mainColor))
            {
                e.Graphics.FillEllipse(main, mainRect);
            }

            using (SolidBrush point = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(point, pointRect);
            }

            using (Pen pen = new Pen(Color.FromArgb(180, Color.White), 1))
            {
                e.Graphics.DrawEllipse(pen, mainRect);
            }

            base.OnPaint(e);
        }
    }

    public class OviaSmallButton : Control
    {
        private bool hover;

        public OviaSmallButton()
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

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color border = hover ? Color.FromArgb(91, 49, 225) : Color.FromArgb(216, 223, 238);
            Color text = hover ? Color.FromArgb(91, 49, 225) : Color.FromArgb(102, 111, 135);

            using (GraphicsPath path = MainDrawHelper.RoundRect(rect, 6))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(border, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                new Font("맑은 고딕", 9F, FontStyle.Bold),
                rect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public static class MainDrawHelper
    {
        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;

            if (d > rect.Width)
            {
                d = rect.Width;
            }

            if (d > rect.Height)
            {
                d = rect.Height;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
