using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public partial class Form1 : Form
    {
        private OviaTextInput	txtCompanyId;
        private OviaTextInput	txtUserId;
        private OviaTextInput	txtPassword;
        private Panel loginPanel;
        private Panel connectionPanel;
        private OviaTextInput txtConnectionCompanyId;
        private OviaTextInput txtConnectionErpBaseDomain;
        private OviaTextInput txtConnectionErpPath;
        private OviaTextInput txtConnectionAuthPath;
        private OviaButton btnConnectionCancel;
        private Label connectionStatusLabel;
        private bool showingConnectionSetup;
        private OVIA.Desktop.Controls.OviaCheckBox		chkSaveId;
        private Timer		loginFadeTimer;
        private bool		hasPlayedStartupFadeIn;
        private OviaWindowCaptionTheme captionTheme;
        private OviaLogoImage loginLogo;
        private Label loginOviaName;
        private Label loginSlogan;
        private Label loginDescription;
        private bool loginInProgress;

        private const double	LoginFadeStep	= 0.07D;
        private const int	LoginFadeInterval	= 15;

        private readonly Color	BrandIndigo	= OviaFluentTheme.AccentHover;
        private readonly Color	BrandViolet	= OviaFluentTheme.Accent;
        private readonly Color	TextDark		= OviaFluentTheme.TextPrimary;
        private readonly Color	TextSub		= OviaFluentTheme.TextSecondary;
        private readonly Color	BorderSoft	= OviaFluentTheme.ControlBorder;
        private readonly Color	SurfaceColor	= OviaFluentTheme.AppBackground;

        private readonly string	SaveFilePath	= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OVIA",
            "ovia_login_save.txt"
        );

        public Form1()
        {
            BuildOviaLoginUI();
            LoadSavedLoginInfo();
            InitializeConnectionScreen();
            ReloadLoginLogoForCurrentCompany();
        }

        private void BuildOviaLoginUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text				= "OVIA";
            this.Font				= OviaFluentTheme.FontBrand(9F, FontStyle.Regular);
            this.StartPosition		= FormStartPosition.CenterScreen;
            this.FormBorderStyle	= FormBorderStyle.FixedSingle;
            this.MaximizeBox		= false;
            this.MinimizeBox		= true;
            this.ClientSize			= new Size(1080, 680);
            this.BackColor			= SurfaceColor;
            this.Opacity			= 0D;
            captionTheme = OviaWindowCaptionTheme.Attach(this);

            GradientPanel bg		= new GradientPanel();
            bg.Dock					= DockStyle.Fill;
            bg.StartColor			= SurfaceColor;
            bg.EndColor				= SurfaceColor;
            this.Controls.Add(bg);

            BuildBrandArea(bg);
            BuildLoginCard(bg);
            BuildFooter(bg);

            this.ResumeLayout(false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartStartupFadeIn();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopStartupFadeTimer();
            base.OnFormClosed(e);
        }

        private void StartStartupFadeIn()
        {
            if (hasPlayedStartupFadeIn)
            {
                this.Opacity = 1D;
                return;
            }

            hasPlayedStartupFadeIn = true;
            this.Opacity = 0D;

            StopStartupFadeTimer();

            loginFadeTimer = new Timer();
            loginFadeTimer.Interval = LoginFadeInterval;
            loginFadeTimer.Tick += LoginFadeTimer_Tick;
            loginFadeTimer.Start();
        }

        private void LoginFadeTimer_Tick(object sender, EventArgs e)
        {
            double nextOpacity = this.Opacity + LoginFadeStep;

            if (nextOpacity >= 1D)
            {
                this.Opacity = 1D;
                StopStartupFadeTimer();
                return;
            }

            this.Opacity = nextOpacity;
        }

        private void StopStartupFadeTimer()
        {
            if (loginFadeTimer == null)
            {
                return;
            }

            loginFadeTimer.Stop();
            loginFadeTimer.Tick -= LoginFadeTimer_Tick;
            loginFadeTimer.Dispose();
            loginFadeTimer = null;
        }

        private void BuildBrandArea(Control parent)
        {
            Panel brand				= new Panel();
            brand.Location			= new Point(0, 0);
            brand.Size				= new Size(500, 620);
            brand.BackColor			= SurfaceColor;
            parent.Controls.Add(brand);

            loginLogo				= new OviaLogoImage();
            loginLogo.Location		= new Point(68, 65);
            loginLogo.Size			= new Size(370, 135);
            loginLogo.SurfaceColor	= SurfaceColor;
            brand.Controls.Add(loginLogo);

            loginOviaName = new Label();
            loginOviaName.Text = "OVIA";
            loginOviaName.AutoSize = true;
            loginOviaName.Font = OviaFluentTheme.FontBrand(18F, FontStyle.Bold);
            loginOviaName.ForeColor = Color.Black;
            loginOviaName.BackColor = SurfaceColor;
            brand.Controls.Add(loginOviaName);

            loginSlogan = new Label();
            loginSlogan.Text = "Operational Value Intelligence Agent";
            loginSlogan.AutoSize = true;
            loginSlogan.Font = OviaFluentTheme.FontBrand(11F, FontStyle.Regular);
            loginSlogan.ForeColor = TextDark;
            loginSlogan.BackColor = SurfaceColor;
            brand.Controls.Add(loginSlogan);

            loginDescription = new Label();
            loginDescription.Text = "업무 가치를 올리는 AI 업무 에이전트";
            loginDescription.AutoSize = false;
            loginDescription.Size = new Size(390, 60);
            loginDescription.Font = OviaFluentTheme.FontBrand(10F, FontStyle.Bold);
            loginDescription.ForeColor = Color.FromArgb(150, 158, 168);
            loginDescription.BackColor = SurfaceColor;
            brand.Controls.Add(loginDescription);

            UpdateLoginBrandTextLayout();

            OviaCubeIllustration cube = new OviaCubeIllustration();
            cube.Location			= new Point(62, 350);
            cube.Size				= new Size(390, 210);
            cube.SurfaceColor		= SurfaceColor;
            brand.Controls.Add(cube);
        }

        private void BuildLoginCard(Control parent)
        {
            OviaCard card = new OviaCard();
            card.Location = new Point(520, 48);
            card.Size = new Size(500, 589);
            card.Radius = 8;
            card.SurfaceColor = SurfaceColor;
            card.FillColor = Color.White;
            card.BorderColor = OviaFluentTheme.CardBorder;
            parent.Controls.Add(card);

            BuildLoginPanel(card);
            BuildConnectionPanel(card);
        }

        private void BuildLoginPanel(Control parent)
        {
            loginPanel = new Panel();
            loginPanel.Location = Point.Empty;
            loginPanel.Size = parent.ClientSize;
            loginPanel.BackColor = Color.White;
            loginPanel.Visible = true;
            parent.Controls.Add(loginPanel);

            Label title = new Label();
            title.Text = "LOGIN";
            title.AutoSize = true;
            title.Font = OviaFluentTheme.FontTitle(19F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = Color.White;
            title.Location = new Point(53, 42);
            loginPanel.Controls.Add(title);

            txtCompanyId = AddInput(loginPanel, "기업 아이디", "기업 아이디를 입력하세요", 55, 108, false);
            txtCompanyId.ValueChanged += CompanyId_ValueChanged;
            txtUserId = AddInput(loginPanel, "사용자 아이디", "사용자 아이디를 입력하세요", 55, 198, false);
            txtPassword = AddInput(loginPanel, "암호", "암호를 입력하세요", 55, 288, true);

            chkSaveId = new OVIA.Desktop.Controls.OviaCheckBox();
            chkSaveId.Text = "아이디 저장";
            chkSaveId.Size = new Size(130, 24);
            chkSaveId.Font = OviaFluentTheme.FontInput(9.6F, FontStyle.Regular);
            chkSaveId.ForeColor = TextDark;
            chkSaveId.BackColor = Color.White;
            chkSaveId.Location = new Point(55, 372);
            loginPanel.Controls.Add(chkSaveId);

            OviaButton btnClose = new OviaButton();
            btnClose.Text = "종료";
            btnClose.Location = new Point(55, 412);
            btnClose.Size = new Size(185, 46);
            btnClose.IsPrimary = false;
            btnClose.StartColor = OviaFluentTheme.ControlBorder;
            btnClose.EndColor = OviaFluentTheme.ControlBorder;
            btnClose.TextColor = OviaFluentTheme.TextPrimary;
            btnClose.SurfaceColor = Color.White;
            btnClose.Radius = OviaFluentTheme.ButtonRadius;
            btnClose.Font = OviaFluentTheme.FontButton(11F, FontStyle.Bold);
            btnClose.Click += delegate { this.Close(); };
            loginPanel.Controls.Add(btnClose);

            OviaButton btnLogin = new OviaButton();
            btnLogin.Text = "로그인";
            btnLogin.Location = new Point(260, 412);
            btnLogin.Size = new Size(185, 46);
            btnLogin.IsPrimary = true;
            btnLogin.StartColor = OviaFluentTheme.Accent;
            btnLogin.EndColor = OviaFluentTheme.Accent;
            btnLogin.TextColor = Color.White;
            btnLogin.SurfaceColor = Color.White;
            btnLogin.Radius = OviaFluentTheme.ButtonRadius;
            btnLogin.Font = OviaFluentTheme.FontButton(12F, FontStyle.Bold);
            btnLogin.Click += BtnLogin_Click;
            loginPanel.Controls.Add(btnLogin);

            Panel line = new Panel();
            line.Location = new Point(55, 487);
            line.Size = new Size(390, 1);
            line.BackColor = OviaFluentTheme.CardBorder;
            loginPanel.Controls.Add(line);

            Label info = new Label();
            info.Text = "본 시스템은 승인된 사용자만 로그인할 수 있습니다.\r\n비인가자가 불법 접근을 시도할 경우, 접속 IP 및 PC 고유 정보가\r\n실시간으로 추적·기록되며 법적 책임을 물을 수 있습니다.";
            info.AutoSize = false;
            info.Size = new Size(390, 60);
            info.Font = OviaFluentTheme.FontSystem(8.5F, FontStyle.Regular);
            info.ForeColor = Color.FromArgb(150, 158, 168);
            info.BackColor = Color.White;
            info.Location = new Point(55, 508);
            loginPanel.Controls.Add(info);
        }

        private void BuildConnectionPanel(Control parent)
        {
            connectionPanel = new Panel();
            connectionPanel.Location = Point.Empty;
            connectionPanel.Size = parent.ClientSize;
            connectionPanel.BackColor = Color.White;
            connectionPanel.Visible = false;
            parent.Controls.Add(connectionPanel);

            Label title = new Label();
            title.Text = "OVIA Connection";
            title.AutoSize = true;
            title.Font = OviaFluentTheme.FontTitle(19F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = Color.White;
            title.Location = new Point(53, 34);
            connectionPanel.Controls.Add(title);

            txtConnectionCompanyId = AddInput(connectionPanel, "기업 아이디", "기업 아이디를 입력하세요", 55, 104, false);
            txtConnectionErpBaseDomain = AddInput(connectionPanel, "기본 도메인", "https://를 포함하여 입력", 55, 189, false);
            txtConnectionErpPath = AddInput(connectionPanel, "ERP", "ERP Path", 55, 274, false);
            txtConnectionAuthPath = AddInput(connectionPanel, "ERP Authentication", "Authentication Path", 55, 359, false);

            btnConnectionCancel = new OviaButton();
            btnConnectionCancel.Text = "종료";
            btnConnectionCancel.Location = new Point(55, 458);
            btnConnectionCancel.Size = new Size(185, 46);
            btnConnectionCancel.IsPrimary = false;
            btnConnectionCancel.StartColor = OviaFluentTheme.ControlBorder;
            btnConnectionCancel.EndColor = OviaFluentTheme.ControlBorder;
            btnConnectionCancel.TextColor = OviaFluentTheme.TextPrimary;
            btnConnectionCancel.SurfaceColor = Color.White;
            btnConnectionCancel.Radius = OviaFluentTheme.ButtonRadius;
            btnConnectionCancel.Font = OviaFluentTheme.FontButton(11F, FontStyle.Bold);
            btnConnectionCancel.Click += ConnectionCancel_Click;
            connectionPanel.Controls.Add(btnConnectionCancel);

            OviaButton btnSaveConnection = new OviaButton();
            btnSaveConnection.Text = "저장";
            btnSaveConnection.Location = new Point(260, 458);
            btnSaveConnection.Size = new Size(185, 46);
            btnSaveConnection.IsPrimary = true;
            btnSaveConnection.StartColor = OviaFluentTheme.Accent;
            btnSaveConnection.EndColor = OviaFluentTheme.Accent;
            btnSaveConnection.TextColor = Color.White;
            btnSaveConnection.SurfaceColor = Color.White;
            btnSaveConnection.Radius = OviaFluentTheme.ButtonRadius;
            btnSaveConnection.Font = OviaFluentTheme.FontButton(12F, FontStyle.Bold);
            btnSaveConnection.Click += SaveConnection_Click;
            connectionPanel.Controls.Add(btnSaveConnection);

            connectionStatusLabel = new Label();
            connectionStatusLabel.Text = "부여받은 연결정보를 저장하면 LOGIN 화면으로 이동합니다";
            connectionStatusLabel.AutoSize = false;
            connectionStatusLabel.Size = new Size(390, 44);
            connectionStatusLabel.Font = OviaFluentTheme.FontSystem(8.4F, FontStyle.Regular);
            connectionStatusLabel.ForeColor = TextSub;
            connectionStatusLabel.BackColor = Color.White;
            connectionStatusLabel.Location = new Point(55, 530);
            connectionPanel.Controls.Add(connectionStatusLabel);
        }

        private void InitializeConnectionScreen()
        {
            string savedCompanyId = txtCompanyId == null ? "" : txtCompanyId.Value.Trim();

            if (!OviaCompanyConnectionStore.HasAnyProfile())
            {
                ShowConnectionSetup(savedCompanyId);
                return;
            }

            if (savedCompanyId != "" && !OviaCompanyConnectionStore.Exists(savedCompanyId))
            {
                ShowConnectionSetup(savedCompanyId);
                return;
            }

            ShowLoginPanel(savedCompanyId);
        }

        private void ConnectionCancel_Click(object sender, EventArgs e)
        {
            if (OviaCompanyConnectionStore.HasAnyProfile())
            {
                string companyId = txtConnectionCompanyId == null ? "" : txtConnectionCompanyId.Value.Trim();
                if (!OviaCompanyConnectionStore.Exists(companyId))
                {
                    companyId = txtCompanyId == null ? "" : txtCompanyId.Value.Trim();
                }
                ShowLoginPanel(companyId);
                return;
            }

            this.Close();
        }

        private void SaveConnection_Click(object sender, EventArgs e)
        {
            SaveConnectionAndShowLogin();
        }

        private bool SaveConnectionAndShowLogin()
        {
            string companyId = txtConnectionCompanyId == null ? "" : txtConnectionCompanyId.Value.Trim();
            string erpBaseDomain = txtConnectionErpBaseDomain == null ? "" : txtConnectionErpBaseDomain.Value.Trim();
            string erpConnectionPath = txtConnectionErpPath == null ? "" : txtConnectionErpPath.Value.Trim();
            string erpAuthPath = txtConnectionAuthPath == null ? "" : txtConnectionAuthPath.Value.Trim();

            if (!OviaCompanyConnectionStore.IsValidCompanyId(companyId))
            {
                MessageBox.Show(
                    "기업 아이디는 영문, 숫자, 하이픈(-), 밑줄(_)만 입력할 수 있습니다.",
                    "OVIA Connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                if (txtConnectionCompanyId != null) txtConnectionCompanyId.FocusInput();
                return false;
            }

            if (erpBaseDomain == "" || erpConnectionPath == "" || erpAuthPath == "")
            {
                MessageBox.Show(
                    "기업 아이디, 기본 도메인, ERP, ERP Authentication을 모두 입력해주세요.",
                    "OVIA Connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return false;
            }

            try
            {
                OviaCompanyConnectionProfile profile = new OviaCompanyConnectionProfile();
                profile.CompanyId = companyId;
                profile.ErpBaseDomain = erpBaseDomain;
                profile.ErpConnectionPath = erpConnectionPath;
                profile.ErpAuthPath = erpAuthPath;
                OviaCompanyConnectionStore.Save(profile);

                OviaCompanyConnectionProfile savedProfile;
                if (!OviaCompanyConnectionStore.TryLoad(companyId, out savedProfile))
                {
                    throw new InvalidOperationException("저장된 OVIA Connection 정보를 다시 읽을 수 없습니다.");
                }

                if (txtCompanyId != null)
                {
                    txtCompanyId.Value = savedProfile.CompanyId;
                }

                ShowLoginPanel(savedProfile.CompanyId);
                ReloadLoginLogoForCurrentCompany();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "OVIA Connection 저장 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }
        }

        private void ShowConnectionSetup(string companyId)
        {
            showingConnectionSetup = true;

            if (loginPanel != null) loginPanel.Visible = false;
            if (connectionPanel != null)
            {
                connectionPanel.Visible = true;
                connectionPanel.BringToFront();
            }

            string normalizedCompanyId = companyId == null ? "" : companyId.Trim();

            if (txtConnectionCompanyId != null) txtConnectionCompanyId.Value = normalizedCompanyId;
            if (txtConnectionErpBaseDomain != null) txtConnectionErpBaseDomain.Value = "";
            if (txtConnectionErpPath != null) txtConnectionErpPath.Value = "";
            if (txtConnectionAuthPath != null) txtConnectionAuthPath.Value = "";

            if (btnConnectionCancel != null)
            {
                btnConnectionCancel.Text = OviaCompanyConnectionStore.HasAnyProfile() ? "로그인으로" : "종료";
            }

            if (connectionStatusLabel != null)
            {
                connectionStatusLabel.Text = "부여받은 연결정보를 저장하면 LOGIN 화면으로 이동합니다";
            }

            if (txtConnectionCompanyId != null && normalizedCompanyId == "")
            {
                txtConnectionCompanyId.FocusInput();
            }
            else if (txtConnectionErpBaseDomain != null)
            {
                txtConnectionErpBaseDomain.FocusInput();
            }
        }

        private void ShowLoginPanel(string companyId)
        {
            showingConnectionSetup = false;

            if (connectionPanel != null) connectionPanel.Visible = false;
            if (loginPanel != null)
            {
                loginPanel.Visible = true;
                loginPanel.BringToFront();
            }

            string normalizedCompanyId = companyId == null ? "" : companyId.Trim();
            if (normalizedCompanyId != "" && txtCompanyId != null)
            {
                txtCompanyId.Value = normalizedCompanyId;
            }

            if (txtUserId != null)
            {
                txtUserId.FocusInput();
            }
        }

        private OviaTextInput AddInput(Control parent, string labelText, string placeholder, int x, int y, bool isPassword)
        {
            Label label				= new Label();
            label.Text				= labelText;
            label.AutoSize			= true;
            label.Font				= OviaFluentTheme.FontBrand(10F, FontStyle.Bold);
            label.ForeColor			= TextDark;
            label.BackColor			= Color.White;
            label.Location			= new Point(x, y);
            parent.Controls.Add(label);

            OviaTextInput input		= new OviaTextInput();
            input.Location			= new Point(x, y + 26);
            input.Size				= new Size(390, 48);
            input.Placeholder		= placeholder;
            input.IsPassword		= isPassword;
            input.BorderColor		= BorderSoft;
            input.FocusBorderColor	= OviaFluentTheme.Accent;
            input.TextColor			= Color.Black;
            input.PlaceholderColor	= BorderSoft;
            input.SurfaceColor		= Color.White;
            input.Radius			= 2;
            parent.Controls.Add(input);

            return input;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                if (showingConnectionSetup)
                {
                    SaveConnectionAndShowLogin();
                }
                else
                {
                    _ = ExecuteLoginAsync();
                }
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CompanyId_ValueChanged(object sender, EventArgs e)
        {
            ReloadLoginLogoForCurrentCompany();
        }

        private void ReloadLoginLogoForCurrentCompany()
        {
            if (loginLogo == null)
            {
                return;
            }

            string companyId = txtCompanyId == null ? "" : txtCompanyId.Value.Trim();
            loginLogo.Reload(companyId);
            UpdateLoginBrandTextLayout();
        }


        private void UpdateLoginBrandTextLayout()
        {
            if (loginOviaName == null || loginSlogan == null || loginDescription == null) return;
            string companyId = txtCompanyId == null ? "" : txtCompanyId.Value.Trim();
            bool hasCompanyLogo = OviaLogoLoader.HasCompanyLogo(companyId);
            loginOviaName.Visible = hasCompanyLogo;
            loginOviaName.Location = new Point(75, 202);
            int sloganY = hasCompanyLogo ? 238 : 220;
            loginSlogan.Location = new Point(78, sloganY);
            loginDescription.Location = new Point(78, loginSlogan.Bottom + 7);
        }

        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            await ExecuteLoginAsync();
        }

        private async Task ExecuteLoginAsync()
        {
            if (loginInProgress)
            {
                return;
            }
            string companyId		= txtCompanyId.Value.Trim();
            string userId			= txtUserId.Value.Trim();
            string password			= txtPassword.Value.Trim();

            if (companyId == "")
            {
                MessageBox.Show(
                    "기업 아이디를 입력해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                txtCompanyId.FocusInput();
                return;
            }

            if (!OviaCompanyConnectionStore.IsValidCompanyId(companyId))
            {
                MessageBox.Show(
                    "기업 아이디 형식이 올바르지 않습니다.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                txtCompanyId.FocusInput();
                return;
            }

            if (!OviaCompanyConnectionStore.Exists(companyId))
            {
                ShowConnectionSetup(companyId);
                MessageBox.Show(
                    "해당 기업의 OVIA Connection 정보가 없습니다.\r\n부여받은 ERP 연결정보를 먼저 저장해주세요.",
                    "OVIA Connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            if (userId == "" || password == "")
            {
                MessageBox.Show(
                    "사용자 아이디와 암호를 모두 입력해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

            loginInProgress = true;
            this.UseWaitCursor = true;

            OviaErpAuthenticationResult authentication;
            try
            {
                // 모든 OVIA 로그인은 ERP 인증 결과를 기준으로 처리합니다.
                // 클라이언트 내부의 고정/로컬 최고관리자 우회 계정은 사용하지 않습니다.
                authentication = await OviaErpAuthenticationService.AuthenticateAsync(companyId, userId, password);
            }
            finally
            {
                loginInProgress = false;
                this.UseWaitCursor = false;
            }

            if (authentication == null || !authentication.IsSuccess)
            {
                string failureMessage = authentication == null
                    ? "ERP 로그인 처리 결과를 확인할 수 없습니다."
                    : BuildErpLoginFailureMessage(authentication);

                MessageBox.Show(
                    failureMessage,
                    "OVIA ERP 로그인 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                txtPassword.FocusInput();
                return;
            }

            // 모든 로그인은 ERP 인증을 통과하므로 ERP 로고 모드에서는 회사별 ERP 로고를 동기화합니다.
            if (OviaSystemSettingsStore.GetCompanyLogoMode() == "ERP")
            {
                OviaErpCompanyLogoService.Synchronize(companyId);
            }
            ReloadLoginLogoForCurrentCompany();

            OviaSessionSecurity.SetCurrentLogin(companyId, userId, password, authentication.UserLevel);

            if (chkSaveId.Checked)
            {
                SaveLoginInfo(companyId, userId);
            }
            else
            {
                DeleteSavedLoginInfo();
            }

            FrmMain mainForm = new FrmMain(companyId, userId);

            mainForm.FormClosed += delegate
            {
                if (mainForm.IsLogoutRequested)
                {
                    txtPassword.Value = "";
                    this.Show();
                    this.Activate();
                    txtPassword.FocusInput();
                    return;
                }

                this.Close();
            };

            this.Hide();
            mainForm.Show();
        }


        private static string BuildErpLoginFailureMessage(OviaErpAuthenticationResult authentication)
        {
            if (authentication == null)
            {
                return "ERP 로그인 처리 결과를 확인할 수 없습니다.";
            }

            // res=false/msg 응답은 ERP가 보낸 msg만 그대로 표시합니다.
            if (authentication.HasAuthenticationResponse)
            {
                return authentication.Message;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(authentication.Message);

            if (!string.IsNullOrWhiteSpace(authentication.RequestMethod))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append("요청 방식: ");
                builder.Append(authentication.RequestMethod);
            }

            if (authentication.HttpStatusCode > 0)
            {
                builder.AppendLine();
                builder.Append("HTTP 상태: ");
                builder.Append(authentication.HttpStatusCode);
            }

            if (!string.IsNullOrWhiteSpace(authentication.RawResponse))
            {
                string raw = authentication.RawResponse;
                if (raw.Length > 1000)
                {
                    raw = raw.Substring(0, 1000) + "...";
                }

                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("ERP 원본 응답:");
                builder.Append(raw);
            }

            return builder.ToString();
        }

        private void LoadSavedLoginInfo()
        {
            try
            {
                if (!File.Exists(SaveFilePath))
                {
                    return;
                }

                string[] lines	= File.ReadAllLines(SaveFilePath);

                if (lines.Length >= 2)
                {
                    txtCompanyId.Value	= lines[0];
                    txtUserId.Value		= lines[1];
                    chkSaveId.Checked	= true;
                }
            }
            catch
            {
            }
        }

        private void SaveLoginInfo(string companyId, string userId)
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(SaveFilePath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string[] lines = new string[]
                {
                    companyId,
                    userId
                };

                File.WriteAllLines(SaveFilePath, lines);
            }
            catch
            {
            }
        }

        private void DeleteSavedLoginInfo()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    File.Delete(SaveFilePath);
                }
            }
            catch
            {
            }
        }

        private void BuildFooter(Control parent)
        {
            LinkLabel copyright		= new LinkLabel();
            copyright.Text			= "© 2026 CELMON. All rights reserved.";
            copyright.AutoSize		= true;
            copyright.Font			= OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            copyright.LinkColor		= TextSub;
            copyright.ActiveLinkColor	= OviaFluentTheme.Accent;
            copyright.VisitedLinkColor	= TextSub;
            copyright.LinkBehavior		= LinkBehavior.HoverUnderline;
            copyright.LinkArea		= new LinkArea(7, 6);
            copyright.BackColor		= SurfaceColor;
            copyright.Location		= new Point(30, 642);
            copyright.Cursor		= Cursors.Hand;
            copyright.LinkClicked		+= Copyright_LinkClicked;
            parent.Controls.Add(copyright);

            Label version			= new Label();
            version.Text			= OviaSystemSettingsStore.GetDisplayVersionText();
            version.AutoSize		= false;
            version.Size			= new Size(498, 20);
            version.Font			= OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            version.ForeColor		= TextSub;
            version.BackColor		= SurfaceColor;
            version.Location		= new Point(520, 642);
            version.TextAlign		= ContentAlignment.TopRight;
            parent.Controls.Add(version);
        }

        private void Copyright_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "https://www.celmon.com/";
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "셀먼 홈페이지를 여는 중 오류가 발생했습니다.\r\n\r\nhttps://www.celmon.com/\r\n\r\n" + ex.Message,
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
    }

    public class GradientPanel : Panel
    {
        public Color StartColor = Color.White;
        public Color EndColor = Color.White;

        public GradientPanel()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(this.ClientRectangle, StartColor, EndColor, 45F))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            base.OnPaint(e);
        }
    }

    public class OviaCard : Panel
    {
        public int Radius = 22;
        public Color SurfaceColor = OviaFluentTheme.AppBackground;
        public Color FillColor = Color.White;
        public Color BorderColor = OviaFluentTheme.CardBorder;
        public int BorderWidth = 1;

        public OviaCard()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Region = null;
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

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
            {
                using (SolidBrush fill = new SolidBrush(FillColor))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(BorderColor, BorderWidth))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaTextInput : UserControl
    {
        private TextBox innerTextBox;
        private OviaPlaceholderLabel placeholderLabel;
        private Label passwordMaskLabel;
        private OviaCapsLockHint capsLockHint;
        private bool focused;
        private bool mouseInsideInput;

        public string Placeholder = "";
        public bool IsPassword = false;
        public Color BorderColor = OviaFluentTheme.ControlBorder;
        public Color FocusBorderColor = OviaFluentTheme.Accent;
        public Color TextColor = OviaFluentTheme.TextPrimary;
        public Color PlaceholderColor = OviaFluentTheme.TextTertiary;
        public Color SurfaceColor = Color.White;
        public int Radius = OviaFluentTheme.ButtonRadius;

        public event EventHandler ValueChanged;

        public string Value
        {
            get
            {
                return innerTextBox.Text;
            }
            set
            {
                innerTextBox.Text = value ?? "";
                ApplyInputTextStyle();
                UpdatePlaceholderVisibility();
                UpdatePasswordMask();
            }
        }

        public OviaTextInput()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;

            innerTextBox = new TextBox();
            innerTextBox.BorderStyle = BorderStyle.None;
            innerTextBox.Font = OviaFluentTheme.FontInput(11F, FontStyle.Bold);
            innerTextBox.Location = new Point(18, 14);
            innerTextBox.Width = 350;
            innerTextBox.BackColor = Color.White;
            innerTextBox.ForeColor = TextColor;
            innerTextBox.UseSystemPasswordChar = false;
            innerTextBox.PasswordChar = '\0';

            placeholderLabel = new OviaPlaceholderLabel();
            placeholderLabel.Location = new Point(18, 14);
            placeholderLabel.Size = new Size(350, 20);
            placeholderLabel.BackColor = Color.White;
            placeholderLabel.Cursor = Cursors.IBeam;
            placeholderLabel.Click += delegate { innerTextBox.Focus(); };

            passwordMaskLabel = new Label();
            passwordMaskLabel.AutoSize = false;
            passwordMaskLabel.Location = new Point(18, 14);
            passwordMaskLabel.Size = new Size(350, 22);
            passwordMaskLabel.BackColor = Color.White;
            passwordMaskLabel.ForeColor = Color.Black;
            passwordMaskLabel.Font = OviaFluentTheme.FontInput(10.5F, FontStyle.Bold);
            passwordMaskLabel.TextAlign = ContentAlignment.MiddleLeft;
            passwordMaskLabel.Cursor = Cursors.IBeam;
            passwordMaskLabel.Visible = false;
            passwordMaskLabel.Click += delegate { innerTextBox.Focus(); };

            this.MouseEnter += InputMouse_Enter;
            this.MouseLeave += InputMouse_Leave;
            innerTextBox.MouseEnter += InputMouse_Enter;
            innerTextBox.MouseLeave += InputMouse_Leave;
            placeholderLabel.MouseEnter += InputMouse_Enter;
            placeholderLabel.MouseLeave += InputMouse_Leave;
            passwordMaskLabel.MouseEnter += InputMouse_Enter;
            passwordMaskLabel.MouseLeave += InputMouse_Leave;

            innerTextBox.Enter += InnerTextBox_Enter;
            innerTextBox.Leave += InnerTextBox_Leave;
            innerTextBox.TextChanged += InnerTextBox_TextChanged;
            innerTextBox.KeyDown += InnerTextBox_CapsLockStateChanged;
            innerTextBox.KeyUp += InnerTextBox_CapsLockStateChanged;

            this.Controls.Add(innerTextBox);
            this.Controls.Add(passwordMaskLabel);
            this.Controls.Add(placeholderLabel);
            placeholderLabel.BringToFront();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            EnsureCapsLockHint();
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            EnsureCapsLockHint();
            ApplyPlaceholderTextStyle();
            ApplyInputTextStyle();
            UpdatePlaceholderVisibility();
            UpdatePasswordMask();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Region = null;

            if (innerTextBox != null)
            {
                innerTextBox.Width = this.Width - 36;
                innerTextBox.Location = new Point(18, (this.Height - innerTextBox.Height) / 2);
            }

            if (placeholderLabel != null && innerTextBox != null)
            {
                placeholderLabel.Location = new Point(innerTextBox.Left, innerTextBox.Top - 1);
                placeholderLabel.Size = new Size(innerTextBox.Width, innerTextBox.Height + 2);
            }

            if (passwordMaskLabel != null && innerTextBox != null)
            {
                passwordMaskLabel.Location = new Point(innerTextBox.Left, innerTextBox.Top - 1);
                passwordMaskLabel.Size = new Size(innerTextBox.Width, innerTextBox.Height + 2);
            }

            UpdateCapsLockHintBounds();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            UpdateCapsLockHintBounds();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            innerTextBox.Focus();
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

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);
            Color currentBorder = focused ? FocusBorderColor : BorderColor;

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(currentBorder, 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }

        private void InnerTextBox_Enter(object sender, EventArgs e)
        {
            focused = true;
            UpdateCapsLockHint();
            UpdatePlaceholderVisibility();
            UpdatePasswordMask();
            this.Invalidate();
        }

        private void InnerTextBox_Leave(object sender, EventArgs e)
        {
            focused = false;
            mouseInsideInput = IsMouseInsideInputBounds();
            UpdateCapsLockHint();
            UpdatePlaceholderVisibility();
            UpdatePasswordMask();
            this.Invalidate();
        }

        private void InnerTextBox_TextChanged(object sender, EventArgs e)
        {
            ApplyInputTextStyle();
            UpdatePlaceholderVisibility();
            UpdatePasswordMask();
            UpdateCapsLockHint();

            EventHandler handler = ValueChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void InnerTextBox_CapsLockStateChanged(object sender, KeyEventArgs e)
        {
            UpdateCapsLockHint();
        }

        private void InputMouse_Enter(object sender, EventArgs e)
        {
            mouseInsideInput = true;
            UpdateCapsLockHint();
        }

        private void InputMouse_Leave(object sender, EventArgs e)
        {
            try
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    mouseInsideInput = IsMouseInsideInputBounds();
                    UpdateCapsLockHint();
                }));
            }
            catch
            {
                mouseInsideInput = false;
                HideCapsLockHint();
            }
        }

        private bool IsMouseInsideInputBounds()
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return false;
            }

            Point localPoint = this.PointToClient(Control.MousePosition);
            return this.ClientRectangle.Contains(localPoint);
        }

        private void EnsureCapsLockHint()
        {
            if (this.Parent == null)
            {
                return;
            }

            if (capsLockHint != null && capsLockHint.Parent == this.Parent)
            {
                UpdateCapsLockHintBounds();
                return;
            }

            if (capsLockHint != null)
            {
                try
                {
                    capsLockHint.Parent.Controls.Remove(capsLockHint);
                    capsLockHint.Dispose();
                }
                catch
                {
                }
            }

            capsLockHint = new OviaCapsLockHint();
            capsLockHint.Visible = false;
            this.Parent.Controls.Add(capsLockHint);
            capsLockHint.BringToFront();
            UpdateCapsLockHintBounds();
        }

        private void UpdateCapsLockHintBounds()
        {
            if (capsLockHint == null || this.Parent == null)
            {
                return;
            }

            Size hintSize = capsLockHint.GetPreferredSize(Size.Empty);
            int hintX = this.Left;
            int hintY = this.Top - hintSize.Height - 6;

            if (hintY < 4)
            {
                hintY = this.Bottom + 4;
            }

            capsLockHint.SetBounds(hintX, hintY, hintSize.Width, hintSize.Height);
            capsLockHint.BringToFront();
        }

        private void UpdateCapsLockHint()
        {
            if (capsLockHint == null)
            {
                EnsureCapsLockHint();
            }

            if (capsLockHint == null)
            {
                return;
            }

            if (mouseInsideInput && Control.IsKeyLocked(Keys.CapsLock))
            {
                UpdateCapsLockHintBounds();
                capsLockHint.Visible = true;
                capsLockHint.BringToFront();
                return;
            }

            HideCapsLockHint();
        }

        private void HideCapsLockHint()
        {
            if (capsLockHint == null)
            {
                return;
            }

            capsLockHint.Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (capsLockHint != null)
                {
                    try
                    {
                        if (capsLockHint.Parent != null)
                        {
                            capsLockHint.Parent.Controls.Remove(capsLockHint);
                        }
                    }
                    catch
                    {
                    }

                    capsLockHint.Dispose();
                    capsLockHint = null;
                }
            }

            base.Dispose(disposing);
        }

        private void UpdatePlaceholderVisibility()
        {
            if (placeholderLabel == null || innerTextBox == null)
            {
                return;
            }

            placeholderLabel.PlaceholderText = Placeholder;
            placeholderLabel.PlaceholderColor = PlaceholderColor;
            placeholderLabel.PlaceholderFont = OviaFluentTheme.FontInput(9F, FontStyle.Regular);
            placeholderLabel.LetterSpacing = -1F;
            placeholderLabel.Visible = !focused && innerTextBox.Text.Trim() == "";
            placeholderLabel.Invalidate();
        }

        private void UpdatePasswordMask()
        {
            if (innerTextBox == null)
            {
                return;
            }

            // 비밀번호도 실제 TextBox가 직접 렌더링하도록 한다.
            // 별도 마스킹 라벨을 덮으면 선택 영역과 입력 커서가 가려지므로 사용하지 않는다.
            innerTextBox.UseSystemPasswordChar = IsPassword;
            innerTextBox.PasswordChar = '\0';
            innerTextBox.ForeColor = TextColor;

            if (passwordMaskLabel != null)
            {
                passwordMaskLabel.Text = "";
                passwordMaskLabel.Visible = false;
                passwordMaskLabel.SendToBack();
            }
        }

        private void ApplyPlaceholderTextStyle()
        {
            if (placeholderLabel == null)
            {
                return;
            }

            placeholderLabel.PlaceholderText = Placeholder;
            placeholderLabel.PlaceholderColor = PlaceholderColor;
            placeholderLabel.PlaceholderFont = OviaFluentTheme.FontInput(9F, FontStyle.Regular);
            placeholderLabel.LetterSpacing = -1F;
            placeholderLabel.Invalidate();
        }

        private void ApplyInputTextStyle()
        {
            if (innerTextBox == null)
            {
                return;
            }

            // 약 14px 수준의 로그인 입력 글자 크기(11pt)를 공통 적용한다.
            innerTextBox.Font = OviaFluentTheme.FontInput(11F, FontStyle.Bold);
            innerTextBox.ForeColor = TextColor;
        }

        public void FocusInput()
        {
            if (innerTextBox != null)
            {
                innerTextBox.Focus();
            }
        }
    }

    public class OviaCapsLockHint : Control
    {
        private readonly Color fillColor = Color.FromArgb(28, 28, 28);
        private readonly Color textColor = Color.White;
        private const string HintText = "Caps Lock이 켜져있습니다.";

        public OviaCapsLockHint()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            // Color.Transparent는 일반 Control에서 런타임 예외가 발생할 수 있으므로 사용하지 않는다.
            // 도움말 자체가 불투명한 짙은 배경이므로 BackColor도 동일 색상으로 고정한다.
            this.BackColor = fillColor;
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            using (Graphics graphics = this.CreateGraphics())
            {
                SizeF textSize = graphics.MeasureString(HintText, this.Font);
                return new Size((int)Math.Ceiling(textSize.Width) + 20, 24);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush brush = new SolidBrush(fillColor))
            {
                pevent.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 2))
            {
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                HintText,
                this.Font,
                new Rectangle(10, 0, this.Width - 20, this.Height),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
            );
        }
    }

    public class OviaPlaceholderLabel : Control
    {
        public string PlaceholderText = "";
        public Color PlaceholderColor = OviaFluentTheme.TextTertiary;
        public Font PlaceholderFont = OviaFluentTheme.FontInput(9F, FontStyle.Regular);
        public float LetterSpacing = -1F;

        public OviaPlaceholderLabel()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.DoubleBuffered = true;
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush brush = new SolidBrush(this.BackColor))
            {
                pevent.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush brush = new SolidBrush(PlaceholderColor))
            {
                float x = 0F;
                float y = Math.Max(0F, (this.Height - PlaceholderFont.Height) / 2F);

                for (int i = 0; i < PlaceholderText.Length; i++)
                {
                    string ch = PlaceholderText[i].ToString();
                    e.Graphics.DrawString(ch, PlaceholderFont, brush, x, y);
                    SizeF size = e.Graphics.MeasureString(ch, PlaceholderFont, PointF.Empty, StringFormat.GenericTypographic);
                    x += Math.Max(1F, size.Width + LetterSpacing);
                }
            }
        }
    }

    public class OviaButton : Control
    {
        public Color StartColor = OviaFluentTheme.Accent;
        public Color EndColor = OviaFluentTheme.AccentHover;
        public Color TextColor = Color.White;
        public Color SurfaceColor = Color.White;
        public bool IsPrimary = true;
        public int Radius = OviaFluentTheme.ButtonRadius;

        private bool hover;

        public OviaButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (this.Width > 0 && this.Height > 0)
            {
                Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);

                using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
                {
                    this.Region = new Region(path);
                }
            }
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

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
            {
                if (IsPrimary)
                {
                    Color fillColor = hover ? OviaFluentTheme.AccentHover : StartColor;

                    using (SolidBrush brush = new SolidBrush(fillColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        e.Graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(OviaFluentTheme.ControlBorder, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                this.Font,
                rect,
                TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }
    }

    public class OviaLogoImage : Control
    {
        private Image logoImage;
        private Rectangle logoCrop;
        private bool hasImage;

        public Color SurfaceColor = OviaFluentTheme.AppBackground;

        public OviaLogoImage()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;

            LoadLogo("");
        }

        public void Reload(string companyId)
        {
            if (logoImage != null)
            {
                logoImage.Dispose();
                logoImage = null;
            }

            hasImage = false;
            LoadLogo(companyId);
            Invalidate();
        }

        private void LoadLogo(string companyId)
        {
            string logoPath = OviaLogoLoader.FindLogoPath(companyId);

            if (logoPath == "")
            {
                hasImage = false;
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(logoPath);

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    using (Image temp = Image.FromStream(ms))
                    {
                        Bitmap bmp = new Bitmap(temp);
                        logoImage = bmp;
                        logoCrop = OviaLogoLoader.GetContentBounds(bmp);
                        hasImage = true;
                    }
                }
            }
            catch
            {
                hasImage = false;
            }
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
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (hasImage && logoImage != null)
            {
                Rectangle dest = GetImageFitRectangle(logoCrop.Width, logoCrop.Height, this.ClientRectangle);

                e.Graphics.DrawImage(
                    logoImage,
                    dest,
                    logoCrop.X,
                    logoCrop.Y,
                    logoCrop.Width,
                    logoCrop.Height,
                    GraphicsUnit.Pixel
                );
            }
            else
            {
                DrawFallbackLogo(e.Graphics);
            }

            base.OnPaint(e);
        }

        private Rectangle GetImageFitRectangle(int sourceWidth, int sourceHeight, Rectangle target)
        {
            double ratioX = (double)target.Width / (double)sourceWidth;
            double ratioY = (double)target.Height / (double)sourceHeight;
            double ratio = Math.Min(ratioX, ratioY);

            int width = (int)(sourceWidth * ratio);
            int height = (int)(sourceHeight * ratio);

            int x = target.X + (target.Width - width) / 2;
            int y = target.Y + (target.Height - height) / 2;

            return new Rectangle(x, y, width, height);
        }

        private void DrawFallbackLogo(Graphics g)
        {
            RectangleF symbolRect = new RectangleF(0, 15, 82, 82);
            OviaSymbolMark.Draw(g, symbolRect);

            using (Font wordFont = OviaFluentTheme.FontBrand(40F, FontStyle.Bold))
            {
                using (SolidBrush textBrush = new SolidBrush(OviaFluentTheme.Accent))
                {
                    g.DrawString("OVIA", wordFont, textBrush, 112, 27);
                }
            }
        }
    }

    public static class OviaLogoLoader
    {
        public static string FindLogoPath()
        {
            return FindLogoPath("");
        }

        public static string FindLogoPath(string companyId)
        {
            string mode = OviaSystemSettingsStore.GetCompanyLogoMode();
            if (mode == "LOCAL")
            {
                string localPath = OviaSystemSettingsStore.GetConfiguredCompanyLogoPath();
                if (localPath != "") return localPath;
            }
            else if (mode == "ERP")
            {
                string erpLogoPath = OviaErpCompanyLogoService.GetCachedLogoPath(companyId);
                if (erpLogoPath != "") return erpLogoPath;
            }
            return FindDefaultLogoPath();
        }

        public static bool HasCompanyLogo(string companyId)
        {
            string mode = OviaSystemSettingsStore.GetCompanyLogoMode();
            if (mode == "LOCAL") return OviaSystemSettingsStore.GetConfiguredCompanyLogoPath() != "";
            if (mode == "ERP") return OviaErpCompanyLogoService.GetCachedLogoPath(companyId) != "";
            return false;
        }

        public static bool HasConfiguredCompanyLogo()
        {
            return HasCompanyLogo("");
        }

        public static string FindDefaultLogoPath()
        {
            string[] fileNames = new string[]
            {
                "ovia_logo.png",
                "ovia_logo_transparent.png"
            };

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(baseDir);

            int depth;
            int i;

            for (depth = 0; depth < 8 && dir != null; depth++)
            {
                for (i = 0; i < fileNames.Length; i++)
                {
                    string path = Path.Combine(dir.FullName, fileNames[i]);

                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                dir = dir.Parent;
            }

            string current = Environment.CurrentDirectory;

            for (i = 0; i < fileNames.Length; i++)
            {
                string path = Path.Combine(current, fileNames[i]);

                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "";
        }

        public static Rectangle GetContentBounds(Bitmap bmp)
        {
            int minX = bmp.Width;
            int minY = bmp.Height;
            int maxX = 0;
            int maxY = 0;
            bool found = false;

            int x;
            int y;

            for (y = 0; y < bmp.Height; y++)
            {
                for (x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);

                    if (IsContentPixel(c))
                    {
                        if (x < minX)
                        {
                            minX = x;
                        }

                        if (y < minY)
                        {
                            minY = y;
                        }

                        if (x > maxX)
                        {
                            maxX = x;
                        }

                        if (y > maxY)
                        {
                            maxY = y;
                        }

                        found = true;
                    }
                }
            }

            if (!found)
            {
                return new Rectangle(0, 0, bmp.Width, bmp.Height);
            }

            int padding = 8;

            minX = Math.Max(0, minX - padding);
            minY = Math.Max(0, minY - padding);
            maxX = Math.Min(bmp.Width - 1, maxX + padding);
            maxY = Math.Min(bmp.Height - 1, maxY + padding);

            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static bool IsContentPixel(Color c)
        {
            if (c.A < 20)
            {
                return false;
            }

            if (c.R > 246 && c.G > 246 && c.B > 246)
            {
                return false;
            }

            return true;
        }
    }

    public static class OviaSymbolMark
    {
        public static void Draw(Graphics g, RectangleF r)
        {
            PointF[] outer = new PointF[]
            {
                new PointF(r.Left + r.Width * 0.50f, r.Top + r.Height * 0.02f),
                new PointF(r.Left + r.Width * 0.92f, r.Top + r.Height * 0.25f),
                new PointF(r.Left + r.Width * 0.92f, r.Top + r.Height * 0.73f),
                new PointF(r.Left + r.Width * 0.50f, r.Top + r.Height * 0.98f),
                new PointF(r.Left + r.Width * 0.08f, r.Top + r.Height * 0.73f),
                new PointF(r.Left + r.Width * 0.08f, r.Top + r.Height * 0.25f)
            };

            using (LinearGradientBrush brush = new LinearGradientBrush(r, OviaFluentTheme.Accent, OviaFluentTheme.AccentHover, LinearGradientMode.Vertical))
            {
                g.FillPolygon(brush, outer);
            }

            using (Pen whitePen = new Pen(Color.White, 7F))
            {
                whitePen.StartCap = LineCap.Round;
                whitePen.EndCap = LineCap.Round;

                g.DrawLine(whitePen, r.Left + r.Width * 0.50f, r.Top + r.Height * 0.16f, r.Left + r.Width * 0.50f, r.Top + r.Height * 0.42f);
                g.DrawLine(whitePen, r.Left + r.Width * 0.50f, r.Top + r.Height * 0.60f, r.Left + r.Width * 0.25f, r.Top + r.Height * 0.73f);
                g.DrawLine(whitePen, r.Left + r.Width * 0.50f, r.Top + r.Height * 0.60f, r.Left + r.Width * 0.75f, r.Top + r.Height * 0.73f);
            }

            using (SolidBrush cyan = new SolidBrush(Color.FromArgb(64, 156, 255)))
            {
                PointF[] cubeTop = new PointF[]
                {
                    new PointF(r.Left + r.Width * 0.50f, r.Top + r.Height * 0.31f),
                    new PointF(r.Left + r.Width * 0.67f, r.Top + r.Height * 0.40f),
                    new PointF(r.Left + r.Width * 0.50f, r.Top + r.Height * 0.49f),
                    new PointF(r.Left + r.Width * 0.33f, r.Top + r.Height * 0.40f)
                };

                g.FillPolygon(cyan, cubeTop);
            }

            using (SolidBrush white = new SolidBrush(Color.White))
            {
                g.FillEllipse(white, r.Left + r.Width * 0.425f, r.Top + r.Height * 0.08f, r.Width * 0.15f, r.Height * 0.15f);
                g.FillEllipse(white, r.Left + r.Width * 0.14f, r.Top + r.Height * 0.67f, r.Width * 0.15f, r.Height * 0.15f);
                g.FillEllipse(white, r.Left + r.Width * 0.71f, r.Top + r.Height * 0.67f, r.Width * 0.15f, r.Height * 0.15f);
            }
        }
    }

    public class OviaCubeIllustration : Control
    {
        public Color SurfaceColor = OviaFluentTheme.AppBackground;

        // OVIA_LOGIN_SYMBOL_PATCH_260529_02
        // 로그인 화면 좌측 장식 영역 전용 디자인입니다.
        // AutoCAD / ERP / CELMON 상징 도형만 이 클래스 안에서 그립니다.
        public OviaCubeIllustration()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;
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
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle baseRect = new Rectangle(42, 102, 285, 74);

            using (GraphicsPath basePath = OviaDrawHelper.RoundRect(baseRect, 26))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(228, 236, 248)))
                {
                    e.Graphics.FillPath(brush, basePath);
                }

                using (Pen pen = new Pen(Color.FromArgb(204, 215, 234), 1))
                {
                    e.Graphics.DrawPath(pen, basePath);
                }
            }

            using (Pen line = new Pen(Color.FromArgb(196, 207, 228), 1))
            {
                line.StartCap = LineCap.Round;
                line.EndCap = LineCap.Round;
                e.Graphics.DrawLine(line, 72, 171, 141, 94);
                e.Graphics.DrawLine(line, 231, 94, 306, 89);
                e.Graphics.DrawLine(line, 141, 94, 118, 48);
            }

            Rectangle cube = new Rectangle(135, 38, 95, 95);

            using (LinearGradientBrush brush = new LinearGradientBrush(cube, Color.FromArgb(41, 156, 236), Color.FromArgb(91, 49, 225), 45F))
            {
                e.Graphics.FillRectangle(brush, cube);
            }

            using (Pen pen = new Pen(Color.FromArgb(245, 250, 255), 2))
            {
                e.Graphics.DrawRectangle(pen, cube);
                e.Graphics.DrawLine(pen, cube.Left, cube.Top + cube.Height / 2, cube.Right, cube.Top + cube.Height / 2);
                e.Graphics.DrawLine(pen, cube.Left + cube.Width / 2, cube.Top, cube.Left + cube.Width / 2, cube.Bottom);
            }

            DrawAutoCadSymbol(e.Graphics, new RectangleF(22, 136, 72, 72));
            DrawErpSymbol(e.Graphics, new RectangleF(274, 48, 84, 84));
            DrawCelmonSymbol(e.Graphics, new RectangleF(88, 18, 66, 66));

            base.OnPaint(e);
        }

        private void DrawSymbolCard(Graphics g, RectangleF rect, Color backColor, Color borderColor)
        {
            RectangleF shadowRect = new RectangleF(rect.X + 3F, rect.Y + 4F, rect.Width, rect.Height);

            using (GraphicsPath shadowPath = OviaDrawHelper.RoundRect(shadowRect, 16))
            {
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(25, 45, 58, 90)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }
            }

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 16))
            {
                using (SolidBrush brush = new SolidBrush(backColor))
                {
                    g.FillPath(brush, path);
                }

                using (Pen pen = new Pen(borderColor, 2))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        private void DrawAutoCadSymbol(Graphics g, RectangleF rect)
        {
            Color cadColor = Color.FromArgb(231, 17, 82);
            DrawSymbolCard(g, rect, Color.FromArgb(255, 255, 246, 250), Color.FromArgb(231, 17, 82));

            float cx = rect.Left + rect.Width / 2F;
            float top = rect.Top + 12F;
            float bottom = rect.Bottom - 13F;

            PointF[] mark =
            {
                new PointF(cx, top),
                new PointF(rect.Right - 11F, bottom),
                new PointF(rect.Right - 25F, bottom),
                new PointF(cx, rect.Top + 31F),
                new PointF(rect.Left + 25F, bottom),
                new PointF(rect.Left + 11F, bottom)
            };

            using (SolidBrush brush = new SolidBrush(cadColor))
            {
                g.FillPolygon(brush, mark);
            }

            using (Pen pen = new Pen(Color.White, 4))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, rect.Left + 26F, rect.Bottom - 27F, rect.Right - 26F, rect.Bottom - 27F);
            }

            using (Pen pen = new Pen(cadColor, 3))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, rect.Left + 18F, rect.Bottom - 9F, rect.Right - 18F, rect.Bottom - 9F);
            }
        }

        private void DrawErpSymbol(Graphics g, RectangleF rect)
        {
            Color erpBlue = Color.FromArgb(21, 132, 255);
            Color erpMint = Color.FromArgb(20, 196, 167);
            DrawSymbolCard(g, rect, Color.FromArgb(255, 244, 251, 255), Color.FromArgb(45, 143, 232));

            RectangleF center = new RectangleF(rect.Left + 29F, rect.Top + 29F, 26F, 26F);
            RectangleF leftTop = new RectangleF(rect.Left + 12F, rect.Top + 12F, 20F, 20F);
            RectangleF rightTop = new RectangleF(rect.Right - 32F, rect.Top + 12F, 20F, 20F);
            RectangleF leftBottom = new RectangleF(rect.Left + 12F, rect.Bottom - 32F, 20F, 20F);
            RectangleF rightBottom = new RectangleF(rect.Right - 32F, rect.Bottom - 32F, 20F, 20F);

            using (Pen pen = new Pen(Color.FromArgb(125, 160, 206), 3))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, GetCenter(leftTop), GetCenter(center));
                g.DrawLine(pen, GetCenter(rightTop), GetCenter(center));
                g.DrawLine(pen, GetCenter(leftBottom), GetCenter(center));
                g.DrawLine(pen, GetCenter(rightBottom), GetCenter(center));
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(center, erpBlue, erpMint, 45F))
            {
                using (GraphicsPath path = OviaDrawHelper.RoundRect(center, 8))
                {
                    g.FillPath(brush, path);
                }
            }

            DrawSmallNode(g, leftTop, erpBlue);
            DrawSmallNode(g, rightTop, erpMint);
            DrawSmallNode(g, leftBottom, Color.FromArgb(91, 49, 225));
            DrawSmallNode(g, rightBottom, Color.FromArgb(0, 174, 239));
        }

        private void DrawSmallNode(Graphics g, RectangleF rect, Color color)
        {
            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 6))
            {
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }

                using (Pen pen = new Pen(Color.White, 2))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        private void DrawCelmonSymbol(Graphics g, RectangleF rect)
        {
            Color gold = Color.FromArgb(214, 165, 45);
            Color goldDark = Color.FromArgb(166, 117, 24);
            Color goldLight = Color.FromArgb(255, 224, 128);
            DrawSymbolCard(g, rect, Color.FromArgb(255, 255, 251, 235), Color.FromArgb(214, 165, 45));

            PointF top = new PointF(rect.Left + rect.Width / 2F, rect.Top + 10F);
            PointF right = new PointF(rect.Right - 10F, rect.Top + rect.Height / 2F);
            PointF bottom = new PointF(rect.Left + rect.Width / 2F, rect.Bottom - 10F);
            PointF left = new PointF(rect.Left + 10F, rect.Top + rect.Height / 2F);
            PointF[] diamond = { top, right, bottom, left };

            using (LinearGradientBrush brush = new LinearGradientBrush(rect, goldLight, gold, 45F))
            {
                g.FillPolygon(brush, diamond);
            }

            using (Pen pen = new Pen(goldDark, 3))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPolygon(pen, diamond);
            }

            using (Pen pen = new Pen(Color.FromArgb(255, 250, 220), 2))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, top, bottom);
                g.DrawLine(pen, left, right);
            }

            using (SolidBrush brush = new SolidBrush(goldDark))
            {
                g.FillEllipse(brush, rect.Left + rect.Width / 2F - 4F, rect.Top + rect.Height / 2F - 4F, 8F, 8F);
            }
        }

        private PointF GetCenter(RectangleF rect)
        {
            return new PointF(rect.Left + rect.Width / 2F, rect.Top + rect.Height / 2F);
        }
    }

    public static class OviaDrawHelper
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

        public static GraphicsPath RoundRect(RectangleF rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            float d = radius * 2;

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
