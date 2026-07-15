using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmSystemSettings : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceHelpProvider, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private Panel contentPanel;
        private Panel bottomButtonPanel;
        private Panel erpSection;
        private Panel logoSection;
        private Panel loadingSection;
        private Panel listSection;
        private Panel colorSection;
        private Label titleLabel;
        private Label descLabel;
        private Label lblStatus;
        private OviaSystemInputBox txtErpBaseDomain;
        private OviaSystemInputBox txtErpConnectionPath;
        private OviaSystemInputBox txtErpAuthPath;
        private OviaSystemInputBox txtErpModuleBasePath;
        private Label lblErpConnectionPreview;
        private Label lblErpAuthPreview;
        private Label lblErpModuleBasePreview;
        private Button btnDefaultErpConnectionPath;
        private Button btnDefaultErpAuthPath;
        private Button btnDefaultErpModuleBasePath;
        private OviaSystemInputBox txtLogoPath;
        private OviaSystemInputBox txtLoadingImagePath;
        private OviaSystemInputBox txtLoadingDelayUnit;
        private OviaSystemInputBox txtListPageSize;
        private OviaSystemInputBox txtBrandPrimaryHex;
        private OviaSystemInputBox txtBrandHoverHex;
        private PictureBox logoPreview;
        private PictureBox loadingImagePreview;
        private Label lblLoadingDelaySeconds;
        private Label lblLoadingDelayHelper;
        private Button btnBrowseLogo;
        private Button btnErpLogo;
        private Button btnDefaultLogo;
        private Button btnBrowseLoadingImage;
        private Button btnDefaultLoadingImage;
        private Button btnDefaultLoadingDelay;
        private Panel pnlBrandPrimaryPreview;
        private Panel pnlBrandHoverPreview;
        private Button btnPickBrandPrimary;
        private Button btnPickBrandHover;
        private Button btnDefaultBrandColors;
        private Button btnSave;
        private bool isApplyingWorkspaceBounds = false;

        private string currentLogoPath = "";
        private string pendingLogoSourcePath = "";
        private string currentLoadingImagePath = "";
        private string pendingLoadingImageSourcePath = "";
        private bool defaultLogoRequested = false;
        private string pendingLogoMode = "DEFAULT";
        private bool defaultLoadingImageRequested = false;
        private bool isDirty = false;
        private bool isLoading = false;
        private string cleanSignature = "";

        private const int SectionCardTop = 40;
        private const int SectionContentShiftY = 25;
        private const int SectionCardPadding = 25;
        private const int SectionGap = 30;


        public string WorkspaceHelpKey { get { return "SYSTEM_SETTINGS"; } }
        public string WorkspaceHelpTitle { get { return "시스템 설정"; } }
        public string WorkspaceHelpText
        {
            get
            {
                return "ERP 연결 주소, 회사 로고, 페이지 로딩 설정, 리스트 출력 수, OVIA 주력 색상처럼 OVIA 전체에 적용되는 기본값을 관리합니다. 페이지 로딩 설정은 WebView2와 OVIA 내부 콘텐츠 로딩 오버레이가 함께 사용합니다.";
            }
        }
        public FrmSystemSettings(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.canEdit = OviaSystemSettingsStore.IsSystemAdministrator(this.companyId, this.userId);

            BuildUI();
            LoadSettingsToUi();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA - 시스템 설정";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ClientSize = new Size(1180, 720);
            this.MinimumSize = Size.Empty;
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmSystemSettings_FormClosing;
            Resize += WorkspaceContent_Resize;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildContent(this);
            BuildBottomButtons(this);
            BuildStatus(this);

            this.ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                OviaMenuHelpStore.GetWorkspacePath("SYSTEM_SETTINGS", "메인  ›  환경설정  ›  시스템 설정"),
                delegate { this.Close(); },
                delegate { this.Close(); },
                delegate { LoadSettingsToUi(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target)
                {
                    NavigateByWorkspacePath(target);
                }
            );
        }

        private void NavigateByWorkspacePath(string target)
        {
            string normalized = target == null ? string.Empty : target.Trim().ToUpperInvariant();

            if (normalized == "MAIN" || normalized == "SETTINGS")
            {
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    if (normalized == "SETTINGS")
                    {
                        workspace.NavigateUpInWorkspace();
                    }
                    else
                    {
                        workspace.NavigateToMain();
                    }
                    return;
                }

                this.Close();
            }
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1180, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += CommandBar_Paint;
            OviaWorkspaceCommandBar.Populate(commandBar, "SETTINGS", companyId, userId);
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

        private void BuildTitle(Control parent)
        {
            titleLabel = new Label();
            titleLabel.Text = "시스템 설정";
            titleLabel.AutoSize = false;
            titleLabel.Location = new Point(32, 126);
            titleLabel.Size = new Size(360, 34);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Font = OviaFluentTheme.FontTitle(20F, FontStyle.Bold);
            titleLabel.ForeColor = TextDark;
            titleLabel.BackColor = SurfaceColor;
            parent.Controls.Add(titleLabel);

            descLabel = new Label();
            descLabel.Text = "ERP 연결 주소, 회사 로고, 페이지 로딩 설정처럼 OVIA 전체에 적용되는 기본값을 관리합니다. 이 화면의 저장 권한은 셀먼 시스템 관리자에게만 부여됩니다.";
            descLabel.AutoSize = false;
            descLabel.Location = new Point(35, 166);
            descLabel.Size = new Size(980, 24);
            descLabel.TextAlign = ContentAlignment.MiddleLeft;
            descLabel.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            descLabel.ForeColor = TextSub;
            descLabel.BackColor = SurfaceColor;
            parent.Controls.Add(descLabel);
        }

        private void BuildContent(Control parent)
        {
            contentPanel = new Panel();
            contentPanel.Location = new Point(0, 98);
            contentPanel.Size = new Size(1180, 472);
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.BackColor = SurfaceColor;
            contentPanel.Margin = Padding.Empty;
            contentPanel.Padding = Padding.Empty;
            contentPanel.AutoScrollMargin = Size.Empty;
            contentPanel.AutoScroll = true;
            contentPanel.HorizontalScroll.Enabled = false;
            contentPanel.HorizontalScroll.Visible = false;
            parent.Controls.Add(contentPanel);

            BuildErpSection(contentPanel);
            BuildLogoSection(contentPanel);
            BuildLoadingSection(contentPanel);
            BuildListSection(contentPanel);
            BuildColorSection(contentPanel);
            contentPanel.AutoScrollMinSize = new Size(0, 1160);
        }

        private void BuildErpSection(Control parent)
        {
            erpSection = new Panel();
            erpSection.Location = new Point(25, 25);
            erpSection.Size = new Size(1088, 328);
            erpSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            erpSection.BackColor = SurfaceColor;
            parent.Controls.Add(erpSection);

            AddRequiredTitle(erpSection, "ERP 연결", 0, 0);

            Label domainLabel = CreateErpLabel("ERP 기본 도메인", 0, 40);
            erpSection.Controls.Add(domainLabel);

            txtErpBaseDomain = new OviaSystemInputBox();
            txtErpBaseDomain.Location = new Point(150, 40);
            txtErpBaseDomain.Size = new Size(420, OviaFluentTheme.ButtonHeight);
            txtErpBaseDomain.Placeholder = OviaSystemSettingsStore.DefaultErpBaseDomain;
            txtErpBaseDomain.ValueChanged += ErpInput_ValueChanged;
            erpSection.Controls.Add(txtErpBaseDomain);

            Label connectionLabel = CreateErpLabel("ERP 연결 URL", 0, 96);
            erpSection.Controls.Add(connectionLabel);

            lblErpConnectionPreview = CreateErpPreviewLabel(150, 96);
            erpSection.Controls.Add(lblErpConnectionPreview);

            txtErpConnectionPath = new OviaSystemInputBox();
            txtErpConnectionPath.Location = new Point(420, 96);
            txtErpConnectionPath.Size = new Size(220, OviaFluentTheme.ButtonHeight);
            txtErpConnectionPath.Placeholder = OviaSystemSettingsStore.DefaultErpConnectionPath;
            txtErpConnectionPath.ValueChanged += ErpInput_ValueChanged;
            erpSection.Controls.Add(txtErpConnectionPath);

            btnDefaultErpConnectionPath = CreateNormalButton("기본값", 660, 96, 96, OviaFluentTheme.ButtonHeight);
            btnDefaultErpConnectionPath.Click += DefaultErpConnectionPath_Click;
            erpSection.Controls.Add(btnDefaultErpConnectionPath);

            Label authLabel = CreateErpLabel("ERP 사용자 인증", 0, 152);
            erpSection.Controls.Add(authLabel);

            lblErpAuthPreview = CreateErpPreviewLabel(150, 152);
            erpSection.Controls.Add(lblErpAuthPreview);

            txtErpAuthPath = new OviaSystemInputBox();
            txtErpAuthPath.Location = new Point(520, 152);
            txtErpAuthPath.Size = new Size(180, OviaFluentTheme.ButtonHeight);
            txtErpAuthPath.Placeholder = OviaSystemSettingsStore.DefaultErpAuthPath;
            txtErpAuthPath.ValueChanged += ErpInput_ValueChanged;
            erpSection.Controls.Add(txtErpAuthPath);

            btnDefaultErpAuthPath = CreateNormalButton("기본값", 740, 152, 96, OviaFluentTheme.ButtonHeight);
            btnDefaultErpAuthPath.Click += DefaultErpAuthPath_Click;
            erpSection.Controls.Add(btnDefaultErpAuthPath);

            Label moduleLabel = CreateErpLabel("ERP 모듈 기본 URL", 0, 208);
            erpSection.Controls.Add(moduleLabel);

            lblErpModuleBasePreview = CreateErpPreviewLabel(150, 208);
            erpSection.Controls.Add(lblErpModuleBasePreview);

            txtErpModuleBasePath = new OviaSystemInputBox();
            txtErpModuleBasePath.Location = new Point(420, 208);
            txtErpModuleBasePath.Size = new Size(260, OviaFluentTheme.ButtonHeight);
            txtErpModuleBasePath.Placeholder = OviaSystemSettingsStore.DefaultErpModuleBasePath;
            txtErpModuleBasePath.ValueChanged += ErpInput_ValueChanged;
            erpSection.Controls.Add(txtErpModuleBasePath);

            btnDefaultErpModuleBasePath = CreateNormalButton("기본값", 700, 208, 96, OviaFluentTheme.ButtonHeight);
            btnDefaultErpModuleBasePath.Click += DefaultErpModuleBasePath_Click;
            erpSection.Controls.Add(btnDefaultErpModuleBasePath);



            FinalizeSystemSection(erpSection);
        }

        private Label CreateErpLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Location = new Point(x, y);
            label.Size = new Size(140, 24);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.BackColor = SurfaceColor;
            return label;
        }

        private Label CreateErpPreviewLabel(int x, int y)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Location = new Point(x, y);
            label.Size = new Size(260, 24);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            label.ForeColor = TextSub;
            label.BackColor = SurfaceColor;
            return label;
        }

        private void BuildLogoSection(Control parent)
        {
            logoSection = new Panel();
            logoSection.Location = new Point(25, 183);
            logoSection.Size = new Size(1088, 236);
            logoSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            logoSection.BackColor = SurfaceColor;
            parent.Controls.Add(logoSection);

            AddRequiredTitle(logoSection, "회사로고 설정", 0, 0);

            txtLogoPath = new OviaSystemInputBox();
            txtLogoPath.Location = new Point(0, 38);
            txtLogoPath.Size = new Size(520, OviaFluentTheme.ButtonHeight);
            txtLogoPath.Placeholder = "회사 로고 이미지 파일을 선택해 주세요.";
            txtLogoPath.ReadOnly = true;
            logoSection.Controls.Add(txtLogoPath);

            btnBrowseLogo = CreateBlackButton("이미지 선택", 536, 38, 126, OviaFluentTheme.ButtonHeight);
            btnBrowseLogo.Click += BrowseLogo_Click;
            logoSection.Controls.Add(btnBrowseLogo);

            btnErpLogo = CreateNormalButton("ERP 업로드 로고 사용", 676, 38, 176, OviaFluentTheme.ButtonHeight);
            btnErpLogo.Click += ErpLogo_Click;
            logoSection.Controls.Add(btnErpLogo);

            btnDefaultLogo = CreateNormalButton("기본 OVIA 로고 사용", 866, 38, 158, OviaFluentTheme.ButtonHeight);
            btnDefaultLogo.Click += DefaultLogo_Click;
            logoSection.Controls.Add(btnDefaultLogo);

            Panel previewPanel = new Panel();
            previewPanel.Location = new Point(0, 104);
            previewPanel.Size = new Size(360, 72);
            previewPanel.BackColor = Color.Transparent;
            previewPanel.Paint += PreviewPanel_Paint;
            logoSection.Controls.Add(previewPanel);

            logoPreview = new PictureBox();
            logoPreview.Location = new Point(16, 10);
            logoPreview.Size = new Size(328, 52);
            logoPreview.SizeMode = PictureBoxSizeMode.Zoom;
            logoPreview.BackColor = Color.Transparent;
            previewPanel.Controls.Add(logoPreview);

            Label previewText = new Label();
            previewText.Text = "로그인 화면 로고 미리보기";
            previewText.AutoSize = false;
            previewText.Location = new Point(380, 114);
            previewText.Size = new Size(260, 24);
            previewText.TextAlign = ContentAlignment.MiddleLeft;
            previewText.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            previewText.ForeColor = TextDark;
            previewText.BackColor = SurfaceColor;
            logoSection.Controls.Add(previewText);

            Label previewNote = new Label();
            previewNote.Text = "로고 파일은 저장 시 OVIA 로컬 설정 폴더에 복사되어 관리됩니다.";
            previewNote.AutoSize = false;
            previewNote.Location = new Point(380, 138);
            previewNote.Size = new Size(460, 24);
            previewNote.TextAlign = ContentAlignment.MiddleLeft;
            previewNote.Font = OviaFluentTheme.FontStatus(8.6F, FontStyle.Regular);
            previewNote.ForeColor = TextSub;
            previewNote.BackColor = SurfaceColor;
            logoSection.Controls.Add(previewNote);

            FinalizeSystemSection(logoSection);
        }


        private void BuildLoadingSection(Control parent)
        {
            loadingSection = new Panel();
            loadingSection.Location = new Point(25, 425);
            loadingSection.Size = new Size(1088, 236);
            loadingSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            loadingSection.BackColor = SurfaceColor;
            parent.Controls.Add(loadingSection);

            AddRequiredTitle(loadingSection, "페이지 로딩 설정", 0, 0);

            txtLoadingImagePath = new OviaSystemInputBox();
            txtLoadingImagePath.Location = new Point(0, 38);
            txtLoadingImagePath.Size = new Size(690, OviaFluentTheme.ButtonHeight);
            txtLoadingImagePath.Placeholder = "로딩 애니메이션 이미지를 선택해 주세요.";
            txtLoadingImagePath.ReadOnly = true;
            loadingSection.Controls.Add(txtLoadingImagePath);

            btnBrowseLoadingImage = CreateBlackButton("이미지 선택", 706, 38, 126, OviaFluentTheme.ButtonHeight);
            btnBrowseLoadingImage.Click += BrowseLoadingImage_Click;
            loadingSection.Controls.Add(btnBrowseLoadingImage);

            btnDefaultLoadingImage = CreateNormalButton("기본값 설정", 846, 38, 126, OviaFluentTheme.ButtonHeight);
            btnDefaultLoadingImage.Click += DefaultLoadingImage_Click;
            loadingSection.Controls.Add(btnDefaultLoadingImage);

            Panel previewPanel = new Panel();
            previewPanel.Location = new Point(0, 126);
            previewPanel.Size = new Size(72, 58);
            previewPanel.BackColor = Color.Transparent;
            previewPanel.Paint += PreviewPanel_Paint;
            loadingSection.Controls.Add(previewPanel);

            loadingImagePreview = new PictureBox();
            loadingImagePreview.Location = new Point(10, 6);
            loadingImagePreview.Size = new Size(52, 46);
            loadingImagePreview.SizeMode = PictureBoxSizeMode.Zoom;
            loadingImagePreview.BackColor = Color.Transparent;
            previewPanel.Controls.Add(loadingImagePreview);

            Label delayLabel = new Label();
            delayLabel.Text = "지연속도";
            delayLabel.AutoSize = false;
            delayLabel.Location = new Point(102, 126);
            delayLabel.Size = new Size(90, 20);
            delayLabel.TextAlign = ContentAlignment.MiddleLeft;
            delayLabel.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            delayLabel.ForeColor = TextDark;
            delayLabel.BackColor = SurfaceColor;
            loadingSection.Controls.Add(delayLabel);

            txtLoadingDelayUnit = new OviaSystemInputBox();
            txtLoadingDelayUnit.Location = new Point(102, 150);
            txtLoadingDelayUnit.Size = new Size(112, OviaFluentTheme.ButtonHeight);
            txtLoadingDelayUnit.Placeholder = OviaSystemSettingsStore.DefaultLoadingDelayUnit.ToString();
            txtLoadingDelayUnit.ValueChanged += LoadingDelayInput_ValueChanged;
            loadingSection.Controls.Add(txtLoadingDelayUnit);

            lblLoadingDelaySeconds = new Label();
            lblLoadingDelaySeconds.Text = "0.35초";
            lblLoadingDelaySeconds.AutoSize = false;
            lblLoadingDelaySeconds.Location = new Point(228, 158);
            lblLoadingDelaySeconds.Size = new Size(160, 22);
            lblLoadingDelaySeconds.TextAlign = ContentAlignment.MiddleLeft;
            lblLoadingDelaySeconds.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            lblLoadingDelaySeconds.ForeColor = TextSub;
            lblLoadingDelaySeconds.BackColor = SurfaceColor;
            loadingSection.Controls.Add(lblLoadingDelaySeconds);

            btnDefaultLoadingDelay = CreateNormalButton("기본값 설정", 406, 150, 126, OviaFluentTheme.ButtonHeight);
            btnDefaultLoadingDelay.Click += DefaultLoadingDelay_Click;
            loadingSection.Controls.Add(btnDefaultLoadingDelay);

            lblLoadingDelayHelper = new Label();
            lblLoadingDelayHelper.Text = "입력값은 10ms 단위입니다. 예: 35 입력 시 350ms(0.35초) 이상 지연될 때 로딩 애니메이션이 표시됩니다.";
            lblLoadingDelayHelper.AutoSize = false;
            lblLoadingDelayHelper.Location = new Point(552, 150);
            lblLoadingDelayHelper.Size = new Size(510, 44);
            lblLoadingDelayHelper.TextAlign = ContentAlignment.MiddleLeft;
            lblLoadingDelayHelper.Font = OviaFluentTheme.FontStatus(8.6F, FontStyle.Regular);
            lblLoadingDelayHelper.ForeColor = TextSub;
            lblLoadingDelayHelper.BackColor = SurfaceColor;
            loadingSection.Controls.Add(lblLoadingDelayHelper);


            FinalizeSystemSection(loadingSection);
        }

        private void BuildListSection(Control parent)
        {
            listSection = new Panel();
            listSection.Location = new Point(25, 651);
            listSection.Size = new Size(1088, 174);
            listSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            listSection.BackColor = SurfaceColor;
            parent.Controls.Add(listSection);

            AddRequiredTitle(listSection, "리스트 출력 수 설정", 0, 0);

            txtListPageSize = new OviaSystemInputBox();
            txtListPageSize.Location = new Point(0, 38);
            txtListPageSize.Size = new Size(180, OviaFluentTheme.ButtonHeight);
            txtListPageSize.Placeholder = "100";
            txtListPageSize.ValueChanged += Input_ValueChanged;
            listSection.Controls.Add(txtListPageSize);

            Label helper = new Label();
            helper.Text = "기본값은 100개입니다. 입력한 숫자는 알림 목록을 포함한 OVIA 리스트 형식 화면의 한 페이지 출력 개수 기준으로 사용됩니다.";
            helper.AutoSize = false;
            helper.Location = new Point(2, 96);
            helper.Size = new Size(980, 24);
            helper.TextAlign = ContentAlignment.MiddleLeft;
            helper.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            helper.ForeColor = TextSub;
            helper.BackColor = SurfaceColor;
            listSection.Controls.Add(helper);


            FinalizeSystemSection(listSection);
        }

        private void BuildColorSection(Control parent)
        {
            colorSection = new Panel();
            colorSection.Location = new Point(25, 817);
            colorSection.Size = new Size(1088, 204);
            colorSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            colorSection.BackColor = SurfaceColor;
            parent.Controls.Add(colorSection);

            AddRequiredTitle(colorSection, "색상규칙", 0, 0);

            Label primaryLabel = new Label();
            primaryLabel.Text = "OVIA 주력 색상";
            primaryLabel.AutoSize = false;
            primaryLabel.Location = new Point(2, 34);
            primaryLabel.Size = new Size(160, 20);
            primaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            primaryLabel.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            primaryLabel.ForeColor = TextDark;
            primaryLabel.BackColor = SurfaceColor;
            colorSection.Controls.Add(primaryLabel);

            txtBrandPrimaryHex = new OviaSystemInputBox();
            txtBrandPrimaryHex.Location = new Point(0, 58);
            txtBrandPrimaryHex.Size = new Size(160, OviaFluentTheme.ButtonHeight);
            txtBrandPrimaryHex.Placeholder = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
            txtBrandPrimaryHex.ValueChanged += ColorInput_ValueChanged;
            colorSection.Controls.Add(txtBrandPrimaryHex);

            pnlBrandPrimaryPreview = CreateColorPreviewPanel(174, 66);
            colorSection.Controls.Add(pnlBrandPrimaryPreview);

            btnPickBrandPrimary = CreateNormalButton("색상 선택", 220, 58, 112, OviaFluentTheme.ButtonHeight);
            btnPickBrandPrimary.Click += PickBrandPrimary_Click;
            colorSection.Controls.Add(btnPickBrandPrimary);

            Label hoverLabel = new Label();
            hoverLabel.Text = "OVIA Hover 색상";
            hoverLabel.AutoSize = false;
            hoverLabel.Location = new Point(374, 34);
            hoverLabel.Size = new Size(180, 20);
            hoverLabel.TextAlign = ContentAlignment.MiddleLeft;
            hoverLabel.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            hoverLabel.ForeColor = TextDark;
            hoverLabel.BackColor = SurfaceColor;
            colorSection.Controls.Add(hoverLabel);

            txtBrandHoverHex = new OviaSystemInputBox();
            txtBrandHoverHex.Location = new Point(372, 58);
            txtBrandHoverHex.Size = new Size(160, OviaFluentTheme.ButtonHeight);
            txtBrandHoverHex.Placeholder = OviaSystemSettingsStore.DefaultBrandHoverHex;
            txtBrandHoverHex.ValueChanged += ColorInput_ValueChanged;
            colorSection.Controls.Add(txtBrandHoverHex);

            pnlBrandHoverPreview = CreateColorPreviewPanel(546, 66);
            colorSection.Controls.Add(pnlBrandHoverPreview);

            btnPickBrandHover = CreateNormalButton("색상 선택", 592, 58, 112, OviaFluentTheme.ButtonHeight);
            btnPickBrandHover.Click += PickBrandHover_Click;
            colorSection.Controls.Add(btnPickBrandHover);

            btnDefaultBrandColors = CreateNormalButton("기본값 설정", 742, 58, 126, OviaFluentTheme.ButtonHeight);
            btnDefaultBrandColors.Click += DefaultBrandColors_Click;
            colorSection.Controls.Add(btnDefaultBrandColors);

            Label helper = new Label();
            helper.Text = "저장 시 체크박스 색상, 페이징 활성 색상, 파란색 계열 버튼 등 OVIA 전체 주력 색감에 적용됩니다.";
            helper.AutoSize = false;
            helper.Location = new Point(2, 120);
            helper.Size = new Size(980, 24);
            helper.TextAlign = ContentAlignment.MiddleLeft;
            helper.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            helper.ForeColor = TextSub;
            helper.BackColor = SurfaceColor;
            colorSection.Controls.Add(helper);


            FinalizeSystemSection(colorSection);
        }

        private Panel CreateColorPreviewPanel(int x, int y)
        {
            OviaColorPreviewPanel panel = new OviaColorPreviewPanel();
            panel.Location = new Point(x, y);
            panel.Size = new Size(30, 30);
            panel.PreviewColor = OviaFluentTheme.Accent;
            panel.BackColor = Color.Transparent;
            return panel;
        }

        private void ColorPreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            OviaColorPreviewPanel preview = sender as OviaColorPreviewPanel;
            if (preview != null)
            {
                preview.DrawPreview(e.Graphics);
            }
        }

        private void ColorInput_ValueChanged(object sender, EventArgs e)
        {
            UpdateColorPreviews();
            MarkDirty();
        }

        private void PickBrandPrimary_Click(object sender, EventArgs e)
        {
            PickColorForInput(txtBrandPrimaryHex, OviaSystemSettingsStore.DefaultBrandPrimaryHex);
        }

        private void PickBrandHover_Click(object sender, EventArgs e)
        {
            PickColorForInput(txtBrandHoverHex, OviaSystemSettingsStore.DefaultBrandHoverHex);
        }

        private void PickColorForInput(OviaSystemInputBox input, string fallbackHex)
        {
            if (!EnsureCanEdit() || input == null)
            {
                return;
            }

            Color fallback = OviaSystemSettingsStore.HexToColor(fallbackHex, OviaFluentTheme.Accent);
            ColorDialog dialog = new ColorDialog();
            dialog.FullOpen = true;
            dialog.AnyColor = true;
            dialog.Color = OviaSystemSettingsStore.HexToColor(input.Value, fallback);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            input.Value = OviaSystemSettingsStore.ColorToHex(dialog.Color);
            UpdateColorPreviews();
            MarkDirty();
        }

        private void DefaultBrandColors_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (txtBrandPrimaryHex != null)
            {
                txtBrandPrimaryHex.Value = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
            }

            if (txtBrandHoverHex != null)
            {
                txtBrandHoverHex.Value = OviaSystemSettingsStore.DefaultBrandHoverHex;
            }

            UpdateColorPreviews();
            MarkDirty();
        }

        private void UpdateColorPreviews()
        {
            if (pnlBrandPrimaryPreview != null)
            {
                SetColorPreviewValue(
                    pnlBrandPrimaryPreview,
                    OviaSystemSettingsStore.HexToColor(
                        txtBrandPrimaryHex == null ? OviaSystemSettingsStore.DefaultBrandPrimaryHex : txtBrandPrimaryHex.Value,
                        OviaSystemSettingsStore.HexToColor(OviaSystemSettingsStore.DefaultBrandPrimaryHex, Color.FromArgb(37, 99, 235))
                    )
                );
            }

            if (pnlBrandHoverPreview != null)
            {
                SetColorPreviewValue(
                    pnlBrandHoverPreview,
                    OviaSystemSettingsStore.HexToColor(
                        txtBrandHoverHex == null ? OviaSystemSettingsStore.DefaultBrandHoverHex : txtBrandHoverHex.Value,
                        OviaSystemSettingsStore.HexToColor(OviaSystemSettingsStore.DefaultBrandHoverHex, Color.FromArgb(29, 78, 216))
                    )
                );
            }
        }


        private void SetColorPreviewValue(Panel panel, Color color)
        {
            OviaColorPreviewPanel preview = panel as OviaColorPreviewPanel;
            if (preview != null)
            {
                preview.PreviewColor = color;
                preview.BackColor = Color.Transparent;
                preview.Invalidate();
                return;
            }

            if (panel != null)
            {
                panel.BackColor = color;
                panel.Invalidate();
            }
        }

        private void BuildBottomButtons(Control parent)
        {
            int buttonTop = 0;
            int initialButtonPanelHeight = Math.Max(1, Math.Min(50, OviaFluentTheme.ButtonHeight));

            bottomButtonPanel = new Panel();
            bottomButtonPanel.Location = new Point(0, 632);
            bottomButtonPanel.Size = new Size(1180, initialButtonPanelHeight);
            bottomButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottomButtonPanel.BackColor = SurfaceColor;
            bottomButtonPanel.Margin = Padding.Empty;
            bottomButtonPanel.Padding = Padding.Empty;
            parent.Controls.Add(bottomButtonPanel);

            btnSave = CreateBlackButton("저장하기", 1018, buttonTop, 130, OviaFluentTheme.ButtonHeight);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnSave.Click += Save_Click;
            btnSave.Visible = true;
            btnSave.Enabled = false;
            bottomButtonPanel.Controls.Add(btnSave);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.Text = "시스템 설정을 불러오는 중입니다.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(940, 38);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Visible = false;
            lblStatus.Location = new Point(32, 618);
            parent.Controls.Add(lblStatus);
        }

        private void AddRequiredTitle(Control parent, string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = OviaFluentTheme.FontBrand(12.5F, FontStyle.Bold);
            label.Tag = "SECTION_TITLE";
            label.ForeColor = TextDark;
            label.BackColor = SurfaceColor;
            label.Location = new Point(x, y);
            parent.Controls.Add(label);

            Label required = new Label();
            required.Text = "•";
            required.AutoSize = true;
            required.Font = OviaFluentTheme.FontBrand(15F, FontStyle.Bold);
            required.Tag = "SECTION_TITLE";
            required.ForeColor = OviaFluentTheme.Danger;
            required.BackColor = SurfaceColor;
            required.Location = new Point(x + label.PreferredWidth + 4, y - 2);
            parent.Controls.Add(required);
        }



        private void FinalizeSystemSection(Panel section)
        {
            if (section == null)
            {
                return;
            }

            section.BackColor = SurfaceColor;
            section.Paint += SystemSection_Paint;
            ShiftSectionContent(section, SectionContentShiftY);
            ApplySectionCardSurface(section);
        }

        private void ShiftSectionContent(Control parent, int offsetY)
        {
            if (parent == null)
            {
                return;
            }

            int i;
            for (i = 0; i < parent.Controls.Count; i++)
            {
                Control child = parent.Controls[i];
                if (child == null)
                {
                    continue;
                }

                if (IsSectionTitleControl(child))
                {
                    continue;
                }

                child.Left = child.Left + SectionCardPadding;
                child.Top = child.Top + offsetY;
            }
        }

        private void ApplySectionCardSurface(Control parent)
        {
            if (parent == null)
            {
                return;
            }

            int i;
            for (i = 0; i < parent.Controls.Count; i++)
            {
                Control child = parent.Controls[i];
                if (child == null || IsSectionTitleControl(child))
                {
                    continue;
                }

                Label label = child as Label;
                if (label != null)
                {
                    label.BackColor = Color.Transparent;
                }

                Button button = child as Button;
                if (button != null)
                {
                    button.BackColor = Color.Transparent;
                }

                OviaSystemInputBox input = child as OviaSystemInputBox;
                if (input != null)
                {
                    input.SurfaceColor = Color.Transparent;
                    input.BackColor = Color.Transparent;
                    input.Invalidate();
                }

                PictureBox pictureBox = child as PictureBox;
                if (pictureBox != null)
                {
                    pictureBox.BackColor = Color.Transparent;
                }

                Panel panel = child as Panel;
                if (panel != null)
                {
                    panel.BackColor = Color.Transparent;
                }
            }
        }

        private bool IsSectionTitleControl(Control control)
        {
            return control != null
                && control.Tag != null
                && control.Tag.ToString() == "SECTION_TITLE";
        }

        private void SystemSection_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            int cardTop = SectionCardTop;
            int cardHeight = Math.Max(1, control.Height - cardTop - 1);
            Rectangle rect = new Rectangle(0, cardTop, Math.Max(1, control.Width - 1), cardHeight);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush fill = new SolidBrush(Color.White))
            using (Pen border = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.FillRectangle(fill, rect);
                e.Graphics.DrawRectangle(border, rect);
            }
        }

        private Button CreateBlackButton(string text, int x, int y, int width, int height)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y + Math.Max(0, (height - OviaFluentTheme.ButtonHeight) / 2));
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, OviaButtonRole.Primary);
            return button;
        }

        private Button CreateNormalButton(string text, int x, int y, int width, int height)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y + Math.Max(0, (height - OviaFluentTheme.ButtonHeight) / 2));
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, OviaButtonRole.Neutral);
            return button;
        }

        private void WorkspaceContent_Resize(object sender, EventArgs e)
        {
            ApplyWorkspaceLayout();
        }


        private Size GetWorkspaceClientSize()
        {
            Control parent = this.Parent;

            if (parent != null && !parent.IsDisposed)
            {
                Rectangle parentBounds = parent.ClientRectangle;

                if (parentBounds.Width > 0 && parentBounds.Height > 0)
                {
                    if (this.Dock != DockStyle.Fill)
                    {
                        this.Dock = DockStyle.Fill;
                    }

                    if (!isApplyingWorkspaceBounds && (this.Location != Point.Empty || this.Size != parentBounds.Size))
                    {
                        try
                        {
                            isApplyingWorkspaceBounds = true;
                            this.Location = Point.Empty;
                            this.Size = parentBounds.Size;
                        }
                        finally
                        {
                            isApplyingWorkspaceBounds = false;
                        }
                    }

                    return parentBounds.Size;
                }
            }

            return this.ClientSize;
        }

        public void ApplyWorkspaceLayout()
        {
            const int menuBottom = 98;
            const int fixedAreaGap = 12;
            const int innerLeft = 25;
            const int innerRight = 25;
            const int innerTop = 0;
            const int fixedAreaMaxHeight = 50;
            const int rightButtonMargin = 25;

            Size layoutSize = GetWorkspaceClientSize();
            int contentWidth = Math.Max(1, layoutSize.Width);
            int contentHeight = Math.Max(1, layoutSize.Height);
            int buttonVisualHeight = btnSave == null ? OviaFluentTheme.ButtonHeight : btnSave.Height;
            int fixedAreaTop = menuBottom + fixedAreaGap;
            int fixedAreaHeight = Math.Max(1, Math.Min(fixedAreaMaxHeight, buttonVisualHeight));
            int scrollTop = fixedAreaTop + fixedAreaHeight + fixedAreaGap;

            if (scrollTop >= contentHeight)
            {
                scrollTop = Math.Max(menuBottom, contentHeight - 1);
            }

            int scrollHeight = Math.Max(1, contentHeight - scrollTop);
            int sectionWidth = Math.Max(1, contentWidth - innerLeft - innerRight);
            int sectionInnerWidth = Math.Max(1, sectionWidth - (SectionCardPadding * 2));
            int buttonTopInPanel = 0;

            if (bottomButtonPanel != null)
            {
                bottomButtonPanel.Location = new Point(0, fixedAreaTop);
                bottomButtonPanel.Size = new Size(contentWidth, fixedAreaHeight);
                bottomButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                bottomButtonPanel.Margin = Padding.Empty;
                bottomButtonPanel.Padding = Padding.Empty;
                bottomButtonPanel.Visible = true;
                bottomButtonPanel.BringToFront();
            }

            if (contentPanel != null)
            {
                contentPanel.SuspendLayout();
                contentPanel.Location = new Point(0, scrollTop);
                contentPanel.Size = new Size(contentWidth, scrollHeight);
                contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                contentPanel.Padding = Padding.Empty;
                contentPanel.Margin = Padding.Empty;
                contentPanel.AutoScroll = true;
                contentPanel.AutoScrollMinSize = Size.Empty;
                contentPanel.AutoScrollPosition = Point.Empty;
                contentPanel.HorizontalScroll.Enabled = false;
                contentPanel.HorizontalScroll.Visible = false;
            }

            int nextSectionTop = innerTop;

            if (erpSection != null)
            {
                erpSection.Left = innerLeft;
                erpSection.Top = nextSectionTop;
                erpSection.Width = sectionWidth;
                erpSection.Height = 328;
                nextSectionTop = erpSection.Bottom + SectionGap;
            }

            if (logoSection != null)
            {
                logoSection.Left = innerLeft;
                logoSection.Top = nextSectionTop;
                logoSection.Width = sectionWidth;
                logoSection.Height = 236;
                nextSectionTop = logoSection.Bottom + SectionGap;
            }

            if (loadingSection != null)
            {
                loadingSection.Left = innerLeft;
                loadingSection.Top = nextSectionTop;
                loadingSection.Width = sectionWidth;
                loadingSection.Height = 236;
                nextSectionTop = loadingSection.Bottom + SectionGap;
            }

            if (listSection != null)
            {
                listSection.Left = innerLeft;
                listSection.Top = nextSectionTop;
                listSection.Width = sectionWidth;
                listSection.Height = 174;
                nextSectionTop = listSection.Bottom + SectionGap;
            }

            if (colorSection != null)
            {
                colorSection.Left = innerLeft;
                colorSection.Top = nextSectionTop;
                colorSection.Width = sectionWidth;
                colorSection.Height = 204;
                nextSectionTop = colorSection.Bottom + SectionGap;
            }

            if (contentPanel != null)
            {
                int requiredHeight = 1160;
                if (colorSection != null)
                {
                    requiredHeight = Math.Max(requiredHeight, colorSection.Bottom + 12);
                }

                contentPanel.AutoScrollMinSize = new Size(0, requiredHeight);
                contentPanel.HorizontalScroll.Enabled = false;
                contentPanel.HorizontalScroll.Visible = false;
                contentPanel.ResumeLayout(false);
            }

            if (txtErpBaseDomain != null)
            {
                txtErpBaseDomain.Width = Math.Max(260, Math.Min(520, sectionInnerWidth - 168));
            }

            int erpInputLeft = SectionCardPadding + Math.Max(360, Math.Min(520, sectionInnerWidth - 420));
            int erpDefaultButtonGap = 12;
            int erpDefaultButtonWidth = btnDefaultErpConnectionPath == null ? 96 : btnDefaultErpConnectionPath.Width;
            int erpPathInputWidth = Math.Max(180, sectionWidth - erpInputLeft - erpDefaultButtonWidth - erpDefaultButtonGap - SectionCardPadding);

            if (txtErpConnectionPath != null)
            {
                txtErpConnectionPath.Left = erpInputLeft;
                txtErpConnectionPath.Width = erpPathInputWidth;
            }
            if (btnDefaultErpConnectionPath != null && txtErpConnectionPath != null)
            {
                btnDefaultErpConnectionPath.Left = txtErpConnectionPath.Right + erpDefaultButtonGap;
            }
            if (txtErpAuthPath != null)
            {
                txtErpAuthPath.Left = erpInputLeft;
                txtErpAuthPath.Width = erpPathInputWidth;
            }
            if (btnDefaultErpAuthPath != null && txtErpAuthPath != null)
            {
                btnDefaultErpAuthPath.Left = txtErpAuthPath.Right + erpDefaultButtonGap;
            }
            if (txtErpModuleBasePath != null)
            {
                txtErpModuleBasePath.Left = erpInputLeft;
                txtErpModuleBasePath.Width = Math.Max(220, sectionWidth - erpInputLeft - erpDefaultButtonWidth - erpDefaultButtonGap - SectionCardPadding);
            }
            if (btnDefaultErpModuleBasePath != null && txtErpModuleBasePath != null)
            {
                btnDefaultErpModuleBasePath.Left = txtErpModuleBasePath.Right + erpDefaultButtonGap;
            }
            if (lblErpConnectionPreview != null)
            {
                lblErpConnectionPreview.Width = Math.Max(180, erpInputLeft - lblErpConnectionPreview.Left - 8);
            }
            if (lblErpAuthPreview != null)
            {
                lblErpAuthPreview.Width = Math.Max(180, erpInputLeft - lblErpAuthPreview.Left - 8);
            }
            if (lblErpModuleBasePreview != null)
            {
                lblErpModuleBasePreview.Width = Math.Max(180, erpInputLeft - lblErpModuleBasePreview.Left - 8);
            }
            UpdateErpPreviewLabels();

            if (txtLogoPath != null)
            {
                int buttonArea = 318;
                txtLogoPath.Width = Math.Max(1, sectionInnerWidth - buttonArea - 18);
            }

            if (btnBrowseLogo != null && txtLogoPath != null)
            {
                btnBrowseLogo.Left = txtLogoPath.Right + 16;
            }

            if (btnDefaultLogo != null && btnBrowseLogo != null)
            {
                btnDefaultLogo.Left = btnBrowseLogo.Right + 14;
            }

            if (txtLoadingImagePath != null)
            {
                int buttonArea = 318;
                txtLoadingImagePath.Width = Math.Max(1, sectionInnerWidth - buttonArea - 18);
            }

            if (btnBrowseLoadingImage != null && txtLoadingImagePath != null)
            {
                btnBrowseLoadingImage.Left = txtLoadingImagePath.Right + 16;
            }

            if (btnDefaultLoadingImage != null && btnBrowseLoadingImage != null)
            {
                btnDefaultLoadingImage.Left = btnBrowseLoadingImage.Right + 14;
            }

            if (lblLoadingDelayHelper != null)
            {
                lblLoadingDelayHelper.Width = Math.Max(320, sectionWidth - lblLoadingDelayHelper.Left - SectionCardPadding);
                lblLoadingDelayHelper.Height = 44;
            }

            if (btnSave != null)
            {
                int panelWidth = bottomButtonPanel == null ? contentWidth : Math.Max(1, bottomButtonPanel.ClientSize.Width);
                btnSave.Visible = canEdit;
                btnSave.Location = new Point(Math.Max(0, panelWidth - rightButtonMargin - btnSave.Width), buttonTopInPanel);
                btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }

            if (lblStatus != null)
            {
                lblStatus.Visible = false;
                lblStatus.Location = new Point(0, contentHeight);
                lblStatus.Size = new Size(contentWidth, 0);
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;
                lblStatus.Padding = Padding.Empty;
                lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            }
        }

        private void LoadSettingsToUi()
        {
            isLoading = true;

            try
            {
                OviaSystemSettings settings = OviaSystemSettingsStore.Load();
                if (txtErpBaseDomain != null)
                {
                    txtErpBaseDomain.Value = OviaSystemSettingsStore.NormalizeErpBaseDomain(settings.ErpBaseDomain);
                }
                if (txtErpConnectionPath != null)
                {
                    txtErpConnectionPath.Value = OviaSystemSettingsStore.NormalizeErpPath(settings.ErpConnectionPath, OviaSystemSettingsStore.DefaultErpConnectionPath);
                }
                if (txtErpAuthPath != null)
                {
                    txtErpAuthPath.Value = OviaSystemSettingsStore.NormalizeErpPath(settings.ErpAuthPath, OviaSystemSettingsStore.DefaultErpAuthPath);
                }
                if (txtErpModuleBasePath != null)
                {
                    txtErpModuleBasePath.Value = OviaSystemSettingsStore.NormalizeErpPath(settings.ErpModuleBasePath, OviaSystemSettingsStore.DefaultErpModuleBasePath);
                }
                UpdateErpPreviewLabels();
                if (txtListPageSize != null)
                {
                    txtListPageSize.Value = OviaSystemSettingsStore.NormalizeListPageSize(settings.ListPageSize.ToString()).ToString();
                }

                if (txtBrandPrimaryHex != null)
                {
                    txtBrandPrimaryHex.Value = OviaSystemSettingsStore.NormalizeHexColor(settings.BrandPrimaryHex, OviaSystemSettingsStore.DefaultBrandPrimaryHex);
                }

                if (txtBrandHoverHex != null)
                {
                    txtBrandHoverHex.Value = OviaSystemSettingsStore.NormalizeHexColor(settings.BrandHoverHex, OviaSystemSettingsStore.DefaultBrandHoverHex);
                }

                UpdateColorPreviews();

                currentLogoPath = "";
                pendingLogoSourcePath = "";
                defaultLogoRequested = false;

                string logoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath.Trim();
                if (logoPath != "" && File.Exists(logoPath))
                {
                    currentLogoPath = logoPath;
                    txtLogoPath.Value = logoPath;
                    UpdateLogoPreview(logoPath);
                }
                else
                {
                    txtLogoPath.Value = "기본 OVIA 로고 사용";
                    UpdateLogoPreview("");
                }

                currentLoadingImagePath = "";
                pendingLoadingImageSourcePath = "";
                defaultLoadingImageRequested = false;

                string loadingImagePath = settings.LoadingAnimationImagePath == null ? "" : settings.LoadingAnimationImagePath.Trim();
                if (loadingImagePath != "" && File.Exists(loadingImagePath))
                {
                    currentLoadingImagePath = loadingImagePath;
                    txtLoadingImagePath.Value = loadingImagePath;
                    UpdateLoadingImagePreview(loadingImagePath);
                }
                else
                {
                    txtLoadingImagePath.Value = "기본 OVIA 심볼 사용";
                    UpdateLoadingImagePreview("");
                }

                if (txtLoadingDelayUnit != null)
                {
                    txtLoadingDelayUnit.Value = OviaSystemSettingsStore.NormalizeLoadingDelayUnit(settings.LoadingDelayUnit.ToString()).ToString();
                    UpdateLoadingDelaySeconds();
                }

                cleanSignature = GetCurrentSignature();
                isDirty = false;

                if (!canEdit)
                {
                    SetReadOnlyMode();
                    UpdateStatus("시스템 설정은 셀먼 시스템 관리자만 저장할 수 있습니다. 현재 화면은 보기 전용입니다.");
                }
                else
                {
                    UpdateStatus("시스템 설정을 불러왔습니다. 변경사항이 있으면 상단 저장하기 버튼이 활성화됩니다.");
                }

                UpdateSaveButtonVisibility();
            }
            finally
            {
                isLoading = false;
            }
        }

        private void SetReadOnlyMode()
        {
            if (txtErpBaseDomain != null)
            {
                txtErpBaseDomain.ReadOnly = true;
            }
            if (txtErpConnectionPath != null)
            {
                txtErpConnectionPath.ReadOnly = true;
            }
            if (txtErpAuthPath != null)
            {
                txtErpAuthPath.ReadOnly = true;
            }
            if (txtErpModuleBasePath != null)
            {
                txtErpModuleBasePath.ReadOnly = true;
            }

            if (btnDefaultErpConnectionPath != null)
            {
                btnDefaultErpConnectionPath.Enabled = false;
            }
            if (btnDefaultErpAuthPath != null)
            {
                btnDefaultErpAuthPath.Enabled = false;
            }
            if (btnDefaultErpModuleBasePath != null)
            {
                btnDefaultErpModuleBasePath.Enabled = false;
            }

            if (txtLogoPath != null)
            {
                txtLogoPath.ReadOnly = true;
            }

            if (txtListPageSize != null)
            {
                txtListPageSize.ReadOnly = true;
            }

            if (txtLoadingImagePath != null)
            {
                txtLoadingImagePath.ReadOnly = true;
            }

            if (txtLoadingDelayUnit != null)
            {
                txtLoadingDelayUnit.ReadOnly = true;
            }

            if (txtBrandPrimaryHex != null)
            {
                txtBrandPrimaryHex.ReadOnly = true;
            }

            if (txtBrandHoverHex != null)
            {
                txtBrandHoverHex.ReadOnly = true;
            }

            if (btnPickBrandPrimary != null)
            {
                btnPickBrandPrimary.Enabled = false;
            }

            if (btnPickBrandHover != null)
            {
                btnPickBrandHover.Enabled = false;
            }

            if (btnDefaultBrandColors != null)
            {
                btnDefaultBrandColors.Enabled = false;
            }

            if (btnBrowseLogo != null)
            {
                btnBrowseLogo.Enabled = false;
            }

            if (btnDefaultLogo != null)
            {
                btnDefaultLogo.Enabled = false;
            }

            if (btnBrowseLoadingImage != null)
            {
                btnBrowseLoadingImage.Enabled = false;
            }

            if (btnDefaultLoadingImage != null)
            {
                btnDefaultLoadingImage.Enabled = false;
            }

            if (btnDefaultLoadingDelay != null)
            {
                btnDefaultLoadingDelay.Enabled = false;
            }

            if (btnSave != null)
            {
                btnSave.Enabled = false;
                btnSave.Visible = true;
            }
        }

        private void BrowseLogo_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "회사 로고 이미지 선택";
            dialog.Filter = "이미지 파일 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일 (*.*)|*.*";
            dialog.Multiselect = false;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            pendingLogoSourcePath = dialog.FileName;
            defaultLogoRequested = false;
            pendingLogoMode = "LOCAL";
            txtLogoPath.Value = pendingLogoSourcePath;
            UpdateLogoPreview(pendingLogoSourcePath);
            MarkDirty();
        }

        private void ErpLogo_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit()) return;
            this.UseWaitCursor = true;
            bool found = false;
            try
            {
                found = OviaErpCompanyLogoService.Synchronize(companyId);
            }
            finally
            {
                this.UseWaitCursor = false;
            }
            string path = OviaErpCompanyLogoService.GetCachedLogoPath(companyId);
            if (!found || path == "")
            {
                MessageBox.Show("ERP에 등록된 로고가 없습니다", "OVIA 회사로고", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            pendingLogoSourcePath = "";
            currentLogoPath = path;
            defaultLogoRequested = false;
            pendingLogoMode = "ERP";
            txtLogoPath.Value = "ERP 업로드 로고 사용: " + path;
            UpdateLogoPreview(path);
            MarkDirty();
        }

        private void DefaultLogo_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            pendingLogoSourcePath = "";
            currentLogoPath = "";
            defaultLogoRequested = true;
            pendingLogoMode = "DEFAULT";
            OviaErpCompanyLogoService.ClearCachedLogo(companyId);
            txtLogoPath.Value = "기본 OVIA 로고 사용";
            UpdateLogoPreview("");
            MarkDirty();
        }


        private void BrowseLoadingImage_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "로딩 애니메이션 이미지 선택";
            dialog.Filter = "이미지 파일 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일 (*.*)|*.*";
            dialog.Multiselect = false;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            pendingLoadingImageSourcePath = dialog.FileName;
            defaultLoadingImageRequested = false;
            txtLoadingImagePath.Value = pendingLoadingImageSourcePath;
            UpdateLoadingImagePreview(pendingLoadingImageSourcePath);
            MarkDirty();
        }

        private void DefaultLoadingImage_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            pendingLoadingImageSourcePath = "";
            currentLoadingImagePath = "";
            defaultLoadingImageRequested = true;
            txtLoadingImagePath.Value = "기본 OVIA 심볼 사용";
            UpdateLoadingImagePreview("");
            MarkDirty();
        }

        private void DefaultLoadingDelay_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (txtLoadingDelayUnit != null)
            {
                txtLoadingDelayUnit.Value = OviaSystemSettingsStore.DefaultLoadingDelayUnit.ToString();
            }

            UpdateLoadingDelaySeconds();
            MarkDirty();
        }

        private void LoadingDelayInput_ValueChanged(object sender, EventArgs e)
        {
            UpdateLoadingDelaySeconds();
            MarkDirty();
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (!isDirty)
            {
                UpdateSaveButtonVisibility();
                UpdateStatus("변경사항이 없습니다.");
                return;
            }

            string erpBaseDomain = txtErpBaseDomain == null ? OviaSystemSettingsStore.DefaultErpBaseDomain : txtErpBaseDomain.Value.Trim();
            string erpConnectionPath = txtErpConnectionPath == null ? OviaSystemSettingsStore.DefaultErpConnectionPath : txtErpConnectionPath.Value.Trim();
            string erpAuthPath = txtErpAuthPath == null ? OviaSystemSettingsStore.DefaultErpAuthPath : txtErpAuthPath.Value.Trim();
            string erpModuleBasePath = txtErpModuleBasePath == null ? OviaSystemSettingsStore.DefaultErpModuleBasePath : txtErpModuleBasePath.Value.Trim();
            string listPageSizeText = txtListPageSize == null ? "100" : txtListPageSize.Value.Trim();
            string brandPrimaryHex;
            string brandHoverHex;
            int listPageSize;
            int loadingDelayUnit;

            if (!int.TryParse(listPageSizeText, out listPageSize) || listPageSize < 1 || listPageSize > 1000)
            {
                MessageBox.Show(
                    "리스트 출력 수는 1 이상 1000 이하의 숫자로 입력해 주세요.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtListPageSize != null)
                {
                    txtListPageSize.Focus();
                }
                return;
            }

            listPageSize = OviaSystemSettingsStore.NormalizeListPageSize(listPageSize.ToString());

            string loadingDelayText = txtLoadingDelayUnit == null ? OviaSystemSettingsStore.DefaultLoadingDelayUnit.ToString() : txtLoadingDelayUnit.Value.Trim();
            if (!int.TryParse(loadingDelayText, out loadingDelayUnit) || loadingDelayUnit < OviaSystemSettingsStore.MinLoadingDelayUnit || loadingDelayUnit > OviaSystemSettingsStore.MaxLoadingDelayUnit)
            {
                MessageBox.Show(
                    "지연속도는 0 이상 600 이하의 숫자로 입력해 주세요. 35 입력 시 0.35초입니다.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtLoadingDelayUnit != null)
                {
                    txtLoadingDelayUnit.Focus();
                }
                return;
            }

            loadingDelayUnit = OviaSystemSettingsStore.NormalizeLoadingDelayUnit(loadingDelayUnit.ToString());

            if (!OviaSystemSettingsStore.TryNormalizeHexColor(txtBrandPrimaryHex == null ? OviaSystemSettingsStore.DefaultBrandPrimaryHex : txtBrandPrimaryHex.Value, out brandPrimaryHex))
            {
                MessageBox.Show(
                    "OVIA 주력 색상은 #2563EB 형식의 6자리 HEX 색상값으로 입력해 주세요.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtBrandPrimaryHex != null)
                {
                    txtBrandPrimaryHex.Focus();
                }
                return;
            }

            if (!OviaSystemSettingsStore.TryNormalizeHexColor(txtBrandHoverHex == null ? OviaSystemSettingsStore.DefaultBrandHoverHex : txtBrandHoverHex.Value, out brandHoverHex))
            {
                MessageBox.Show(
                    "OVIA Hover 색상은 #1D4ED8 형식의 6자리 HEX 색상값으로 입력해 주세요.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtBrandHoverHex != null)
                {
                    txtBrandHoverHex.Focus();
                }
                return;
            }

            erpBaseDomain = OviaSystemSettingsStore.NormalizeErpBaseDomain(erpBaseDomain);
            erpConnectionPath = OviaSystemSettingsStore.NormalizeErpPath(erpConnectionPath, OviaSystemSettingsStore.DefaultErpConnectionPath);
            erpAuthPath = OviaSystemSettingsStore.NormalizeErpPath(erpAuthPath, OviaSystemSettingsStore.DefaultErpAuthPath);
            erpModuleBasePath = OviaSystemSettingsStore.NormalizeErpPath(erpModuleBasePath, OviaSystemSettingsStore.DefaultErpModuleBasePath);

            if (!IsValidWebUrl(erpBaseDomain))
            {
                MessageBox.Show(
                    "ERP 기본 도메인은 http:// 또는 https:// 로 시작하는 웹 주소로 입력해 주세요.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (txtErpBaseDomain != null) txtErpBaseDomain.Focus();
                return;
            }

            try
            {
                OviaSystemSettings settings = OviaSystemSettingsStore.Load();
                settings.ErpBaseDomain = erpBaseDomain;
                settings.ErpConnectionPath = erpConnectionPath;
                settings.ErpAuthPath = erpAuthPath;
                settings.ErpModuleBasePath = erpModuleBasePath;
                settings.ErpLoginUrl = OviaSystemSettingsStore.BuildErpConnectionUrl(settings);
                settings.ListPageSize = listPageSize;
                settings.BrandPrimaryHex = brandPrimaryHex;
                settings.BrandHoverHex = brandHoverHex;
                settings.LoadingDelayUnit = loadingDelayUnit;

                if (defaultLoadingImageRequested)
                {
                    settings.LoadingAnimationImagePath = "";
                }
                else if (pendingLoadingImageSourcePath.Trim() != "")
                {
                    settings.LoadingAnimationImagePath = OviaSystemSettingsStore.CopyLoadingAnimationImageToStore(pendingLoadingImageSourcePath);
                }
                else
                {
                    settings.LoadingAnimationImagePath = currentLoadingImagePath != null && File.Exists(currentLoadingImagePath) ? currentLoadingImagePath : "";
                }

                settings.CompanyLogoMode = OviaSystemSettingsStore.NormalizeCompanyLogoMode(pendingLogoMode);
                if (settings.CompanyLogoMode == "DEFAULT")
                {
                    settings.CompanyLogoFilePath = "";
                    OviaErpCompanyLogoService.ClearCachedLogo(companyId);
                }
                else if (settings.CompanyLogoMode == "ERP")
                {
                    settings.CompanyLogoFilePath = "";
                }
                else if (pendingLogoSourcePath.Trim() != "")
                {
                    settings.CompanyLogoFilePath = OviaSystemSettingsStore.CopyCompanyLogoToStore(pendingLogoSourcePath);
                }
                else
                {
                    settings.CompanyLogoFilePath = currentLogoPath != null && File.Exists(currentLogoPath) ? currentLogoPath : "";
                }

                OviaSystemSettingsStore.Save(settings);

                if (txtErpBaseDomain != null)
                {
                    txtErpBaseDomain.Value = settings.ErpBaseDomain;
                }
                if (txtErpConnectionPath != null)
                {
                    txtErpConnectionPath.Value = settings.ErpConnectionPath;
                }
                if (txtErpAuthPath != null)
                {
                    txtErpAuthPath.Value = settings.ErpAuthPath;
                }
                if (txtErpModuleBasePath != null)
                {
                    txtErpModuleBasePath.Value = settings.ErpModuleBasePath;
                }
                UpdateErpPreviewLabels();

                if (txtListPageSize != null)
                {
                    txtListPageSize.Value = settings.ListPageSize.ToString();
                }

                if (txtBrandPrimaryHex != null)
                {
                    txtBrandPrimaryHex.Value = settings.BrandPrimaryHex;
                }

                if (txtBrandHoverHex != null)
                {
                    txtBrandHoverHex.Value = settings.BrandHoverHex;
                }

                if (txtLoadingDelayUnit != null)
                {
                    txtLoadingDelayUnit.Value = settings.LoadingDelayUnit.ToString();
                    UpdateLoadingDelaySeconds();
                }

                currentLoadingImagePath = settings.LoadingAnimationImagePath == null ? "" : settings.LoadingAnimationImagePath;
                pendingLoadingImageSourcePath = "";
                defaultLoadingImageRequested = currentLoadingImagePath.Trim() == "";

                if (currentLoadingImagePath.Trim() != "" && File.Exists(currentLoadingImagePath))
                {
                    txtLoadingImagePath.Value = currentLoadingImagePath;
                    UpdateLoadingImagePreview(currentLoadingImagePath);
                }
                else
                {
                    txtLoadingImagePath.Value = "기본 OVIA 심볼 사용";
                    UpdateLoadingImagePreview("");
                }

                UpdateColorPreviews();

                pendingLogoMode = OviaSystemSettingsStore.NormalizeCompanyLogoMode(settings.CompanyLogoMode);
                currentLogoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath;
                pendingLogoSourcePath = "";
                defaultLogoRequested = pendingLogoMode == "DEFAULT";

                if (pendingLogoMode == "ERP")
                {
                    currentLogoPath = OviaErpCompanyLogoService.GetCachedLogoPath(companyId);
                    txtLogoPath.Value = currentLogoPath == "" ? "ERP 업로드 로고 사용" : "ERP 업로드 로고 사용: " + currentLogoPath;
                    UpdateLogoPreview(currentLogoPath);
                }
                else if (pendingLogoMode == "LOCAL" && currentLogoPath.Trim() != "" && File.Exists(currentLogoPath))
                {
                    txtLogoPath.Value = currentLogoPath;
                    UpdateLogoPreview(currentLogoPath);
                }
                else
                {
                    pendingLogoMode = "DEFAULT";
                    defaultLogoRequested = true;
                    txtLogoPath.Value = "기본 OVIA 로고 사용";
                    UpdateLogoPreview("");
                }

                cleanSignature = GetCurrentSignature();
                isDirty = false;
                UpdateSaveButtonVisibility();

                MessageBox.Show(
                    "저장되었습니다.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                UpdateStatus("시스템 설정을 저장했습니다. 저장 위치: " + OviaSystemSettingsStore.GetSettingsFilePath());
                OviaNotificationStore.AddWorkLog(companyId, userId, "시스템 설정 저장", OviaMenuHelpStore.GetWorkspacePath("SYSTEM_SETTINGS", "메인  ›  환경설정  ›  시스템 설정"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "시스템 설정 저장 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                UpdateStatus("시스템 설정 저장 실패: " + ex.Message);
            }
        }

        private bool IsValidWebUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }


        private void UpdateLoadingDelaySeconds()
        {
            if (lblLoadingDelaySeconds == null)
            {
                return;
            }

            int delayUnit = OviaSystemSettingsStore.NormalizeLoadingDelayUnit(txtLoadingDelayUnit == null ? OviaSystemSettingsStore.DefaultLoadingDelayUnit.ToString() : txtLoadingDelayUnit.Value);
            int milliseconds = OviaSystemSettingsStore.LoadingDelayUnitToMilliseconds(delayUnit);
            lblLoadingDelaySeconds.Text = milliseconds.ToString() + "ms / " + OviaSystemSettingsStore.FormatLoadingDelaySecondsText(delayUnit);
        }

        private void UpdateLoadingImagePreview(string imagePath)
        {
            if (loadingImagePreview == null)
            {
                return;
            }

            Image old = loadingImagePreview.Image;
            loadingImagePreview.Image = null;

            if (old != null)
            {
                old.Dispose();
            }

            string path = imagePath == null ? "" : imagePath.Trim();
            if (path == "" || !File.Exists(path))
            {
                path = OviaSystemSettingsStore.GetDefaultLoadingSymbolPath();
            }

            if (path == "" || !File.Exists(path))
            {
                Bitmap fallback = new Bitmap(52, 46, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(fallback))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen pen = new Pen(OviaFluentTheme.Accent, 4F))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawArc(pen, 8, 5, 34, 34, 40, 280);
                    }
                }
                loadingImagePreview.Image = fallback;
                return;
            }

            try
            {
                using (Image loaded = Image.FromFile(path))
                {
                    Bitmap transparentBitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(transparentBitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(loaded, new Rectangle(0, 0, transparentBitmap.Width, transparentBitmap.Height));
                    }

                    loadingImagePreview.Image = transparentBitmap;
                }
            }
            catch
            {
                loadingImagePreview.Image = null;
            }
        }

        private void UpdateLogoPreview(string imagePath)
        {
            if (logoPreview == null)
            {
                return;
            }

            Image old = logoPreview.Image;
            logoPreview.Image = null;

            if (old != null)
            {
                old.Dispose();
            }

            string path = imagePath == null ? "" : imagePath.Trim();
            if (path == "" || !File.Exists(path))
            {
                path = OviaLogoLoader.FindDefaultLogoPath();
            }

            if (path == "" || !File.Exists(path))
            {
                Bitmap fallback = new Bitmap(328, 52, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(fallback))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Font font = OviaFluentTheme.FontBrand(24F, FontStyle.Bold))
                    using (SolidBrush brush = new SolidBrush(OviaFluentTheme.Accent))
                    {
                        g.DrawString("OVIA", font, brush, 8, 8);
                    }
                }
                logoPreview.Image = fallback;
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                MemoryStream ms = new MemoryStream(bytes);
                logoPreview.Image = Image.FromStream(ms);
            }
            catch
            {
                logoPreview.Image = null;
            }
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 6))
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }


        private void DefaultErpConnectionPath_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (txtErpConnectionPath != null)
            {
                txtErpConnectionPath.Value = OviaSystemSettingsStore.DefaultErpConnectionPath;
            }

            UpdateErpPreviewLabels();
            MarkDirty();
        }

        private void DefaultErpAuthPath_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (txtErpAuthPath != null)
            {
                txtErpAuthPath.Value = OviaSystemSettingsStore.DefaultErpAuthPath;
            }

            UpdateErpPreviewLabels();
            MarkDirty();
        }

        private void DefaultErpModuleBasePath_Click(object sender, EventArgs e)
        {
            if (!EnsureCanEdit())
            {
                return;
            }

            if (txtErpModuleBasePath != null)
            {
                txtErpModuleBasePath.Value = OviaSystemSettingsStore.DefaultErpModuleBasePath;
            }

            UpdateErpPreviewLabels();
            MarkDirty();
        }

        private void ErpInput_ValueChanged(object sender, EventArgs e)
        {
            UpdateErpPreviewLabels();
            MarkDirty();
        }

        private void UpdateErpPreviewLabels()
        {
            string domain = txtErpBaseDomain == null ? OviaSystemSettingsStore.DefaultErpBaseDomain : txtErpBaseDomain.Value.Trim();
            string connectionPath = txtErpConnectionPath == null ? OviaSystemSettingsStore.DefaultErpConnectionPath : txtErpConnectionPath.Value.Trim();
            string authPath = txtErpAuthPath == null ? OviaSystemSettingsStore.DefaultErpAuthPath : txtErpAuthPath.Value.Trim();
            string moduleBasePath = txtErpModuleBasePath == null ? OviaSystemSettingsStore.DefaultErpModuleBasePath : txtErpModuleBasePath.Value.Trim();

            OviaSystemSettings preview = new OviaSystemSettings();
            preview.ErpBaseDomain = domain;
            preview.ErpConnectionPath = connectionPath;
            preview.ErpAuthPath = authPath;
            preview.ErpModuleBasePath = moduleBasePath;

            string normalizedDomain = OviaSystemSettingsStore.NormalizeErpBaseDomain(domain);
            string connectionUrl = OviaSystemSettingsStore.BuildErpConnectionUrl(preview);
            string moduleBaseUrl = OviaSystemSettingsStore.BuildErpModuleBaseUrl(preview);

            if (lblErpConnectionPreview != null)
            {
                lblErpConnectionPreview.Text = normalizedDomain;
            }
            if (lblErpAuthPreview != null)
            {
                lblErpAuthPreview.Text = connectionUrl;
            }
            if (lblErpModuleBasePreview != null)
            {
                lblErpModuleBasePreview.Text = normalizedDomain;
            }
        }

        private void Input_ValueChanged(object sender, EventArgs e)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (isLoading)
            {
                return;
            }

            isDirty = GetCurrentSignature() != cleanSignature;
            UpdateSaveButtonVisibility();

            if (isDirty)
            {
                UpdateStatus("변경된 시스템 설정이 있습니다. 저장하기를 눌러 적용하세요.");
            }
            else
            {
                UpdateStatus("변경사항이 없습니다.");
            }
        }

        private void UpdateSaveButtonVisibility()
        {
            if (btnSave == null)
            {
                return;
            }

            btnSave.Visible = canEdit;
            btnSave.Enabled = canEdit && isDirty;

            if (bottomButtonPanel != null)
            {
                bottomButtonPanel.Visible = canEdit;
            }
        }

        private string GetCurrentSignature()
        {
            string erpDomain = txtErpBaseDomain == null ? OviaSystemSettingsStore.DefaultErpBaseDomain : OviaSystemSettingsStore.NormalizeErpBaseDomain(txtErpBaseDomain.Value);
            string erpConnection = txtErpConnectionPath == null ? OviaSystemSettingsStore.DefaultErpConnectionPath : OviaSystemSettingsStore.NormalizeErpPath(txtErpConnectionPath.Value, OviaSystemSettingsStore.DefaultErpConnectionPath);
            string erpAuth = txtErpAuthPath == null ? OviaSystemSettingsStore.DefaultErpAuthPath : OviaSystemSettingsStore.NormalizeErpPath(txtErpAuthPath.Value, OviaSystemSettingsStore.DefaultErpAuthPath);
            string erpModuleBase = txtErpModuleBasePath == null ? OviaSystemSettingsStore.DefaultErpModuleBasePath : OviaSystemSettingsStore.NormalizeErpPath(txtErpModuleBasePath.Value, OviaSystemSettingsStore.DefaultErpModuleBasePath);
            string erp = erpDomain + "|" + erpConnection + "|" + erpAuth + "|" + erpModuleBase;
            string pending = pendingLogoSourcePath == null ? "" : pendingLogoSourcePath.Trim();
            string current = currentLogoPath == null ? "" : currentLogoPath.Trim();
            string logo = pendingLogoMode + ":" + (defaultLogoRequested || (pending == "" && current == "") ? "DEFAULT" : (pending != "" ? pending : current));
            string loadingPending = pendingLoadingImageSourcePath == null ? "" : pendingLoadingImageSourcePath.Trim();
            string loadingCurrent = currentLoadingImagePath == null ? "" : currentLoadingImagePath.Trim();
            string loadingImage = defaultLoadingImageRequested || (loadingPending == "" && loadingCurrent == "") ? "DEFAULT" : (loadingPending != "" ? loadingPending : loadingCurrent);
            string loadingDelay = txtLoadingDelayUnit == null ? OviaSystemSettingsStore.DefaultLoadingDelayUnit.ToString() : OviaSystemSettingsStore.NormalizeLoadingDelayUnit(txtLoadingDelayUnit.Value).ToString();
            string listPageSize = txtListPageSize == null ? "100" : txtListPageSize.Value.Trim();
            string brandPrimary = txtBrandPrimaryHex == null ? OviaSystemSettingsStore.DefaultBrandPrimaryHex : OviaSystemSettingsStore.NormalizeHexColor(txtBrandPrimaryHex.Value, OviaSystemSettingsStore.DefaultBrandPrimaryHex);
            string brandHover = txtBrandHoverHex == null ? OviaSystemSettingsStore.DefaultBrandHoverHex : OviaSystemSettingsStore.NormalizeHexColor(txtBrandHoverHex.Value, OviaSystemSettingsStore.DefaultBrandHoverHex);
            return erp + "|" + logo + "|" + loadingImage + "|" + loadingDelay + "|" + listPageSize + "|" + brandPrimary + "|" + brandHover;
        }

        private bool AreSameLogoPath(string left, string right)
        {
            string a = NormalizeLogoPathForCompare(left);
            string b = NormalizeLogoPathForCompare(right);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeLogoPathForCompare(string path)
        {
            string value = path == null ? "" : path.Trim();
            if (value == "")
            {
                return "DEFAULT";
            }

            try
            {
                return Path.GetFullPath(value);
            }
            catch
            {
                return value;
            }
        }

        private bool EnsureCanEdit()
        {
            if (canEdit)
            {
                return true;
            }

            MessageBox.Show(
                "시스템 설정은 셀먼 시스템 관리자만 수정할 수 있습니다.\r\n\r\n현재 사용자 ID: " + userId,
                "OVIA 권한 확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return false;
        }

        private void UpdateStatus(string text)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = text == null ? "" : text;
            }
        }

        public bool CanLeaveWorkspaceScreen()
        {
            if (!canEdit || !isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 시스템 설정 변경사항이 있습니다.\r\n\r\n저장하지 않고 이동하시겠습니까?",
                "OVIA 시스템 설정",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            return result == DialogResult.Yes;
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }

        public bool HasUnsavedWorkspaceData()
        {
            return canEdit && isDirty;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "시스템 설정";
        }

        private void FrmSystemSettings_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!CanLeaveWorkspaceScreen())
            {
                e.Cancel = true;
            }
        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.RequestLogout();
            }
            else
            {
                this.Close();
            }
        }
    }


    internal class OviaColorPreviewPanel : Panel
    {
        public Color PreviewColor = OviaFluentTheme.Accent;

        public OviaColorPreviewPanel()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (this.BackColor.A == 0)
            {
                return;
            }

            using (SolidBrush brush = new SolidBrush(this.BackColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawPreview(e.Graphics);
        }

        public void DrawPreview(Graphics graphics)
        {
            if (graphics == null)
            {
                return;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 6))
            using (SolidBrush brush = new SolidBrush(PreviewColor))
            using (Pen pen = new Pen(OviaFluentTheme.ControlBorder, 1))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }
        }
    }

    internal class OviaSystemInputBox : UserControl
    {
        private TextBox innerTextBox;
        private bool focused;
        private bool isPlaceholderVisible;
        private bool readOnly;

        public string Placeholder = "";
        public Color BorderColor = OviaFluentTheme.ControlBorder;
        public Color FocusBorderColor = OviaFluentTheme.Accent;
        public Color TextColor = OviaFluentTheme.TextPrimary;
        public Color PlaceholderColor = OviaFluentTheme.TextTertiary;
        public Color SurfaceColor = OviaFluentTheme.AppBackground;
        public int Radius = 8;

        public event EventHandler ValueChanged;

        public string Value
        {
            get
            {
                if (isPlaceholderVisible)
                {
                    return "";
                }

                return innerTextBox.Text == null ? "" : innerTextBox.Text;
            }
            set
            {
                SetValue(value == null ? "" : value);
            }
        }

        public bool ReadOnly
        {
            get { return readOnly; }
            set
            {
                readOnly = value;
                if (innerTextBox != null)
                {
                    innerTextBox.ReadOnly = value;
                    innerTextBox.Cursor = value ? Cursors.Default : Cursors.IBeam;
                }
            }
        }

        public override string Text
        {
            get { return Value; }
            set { Value = value; }
        }

        public OviaSystemInputBox()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);

            this.DoubleBuffered = true;
            this.BackColor = SurfaceColor;

            innerTextBox = new TextBox();
            innerTextBox.BorderStyle = BorderStyle.None;
            innerTextBox.Font = OviaFluentTheme.FontInput(10.3F, FontStyle.Regular);
            innerTextBox.Location = new Point(20, 14);
            innerTextBox.Width = 300;
            innerTextBox.BackColor = Color.White;
            innerTextBox.ForeColor = TextColor;
            innerTextBox.TextChanged += InnerTextBox_TextChanged;
            innerTextBox.Enter += InnerTextBox_Enter;
            innerTextBox.Leave += InnerTextBox_Leave;
            this.Controls.Add(innerTextBox);
        }

        public new void Focus()
        {
            if (innerTextBox != null)
            {
                innerTextBox.Focus();
            }
            else
            {
                base.Focus();
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            ApplyPlaceholder();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (innerTextBox != null)
            {
                innerTextBox.Width = Math.Max(10, this.Width - 40);
                innerTextBox.Location = new Point(20, (this.Height - innerTextBox.Height) / 2);
            }

            this.Region = null;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (e == null || e.Graphics == null)
            {
                return;
            }

            if (SurfaceColor.A == 0)
            {
                PaintTransparentParentBackground(e.Graphics, e.ClipRectangle);
                return;
            }

            using (SolidBrush brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        private void PaintTransparentParentBackground(Graphics graphics, Rectangle clipRectangle)
        {
            if (graphics == null)
            {
                return;
            }

            Control parent = this.Parent;
            if (parent == null)
            {
                return;
            }

            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(-this.Left, -this.Top);
                Rectangle parentClip = new Rectangle(
                    this.Left + clipRectangle.Left,
                    this.Top + clipRectangle.Top,
                    clipRectangle.Width,
                    clipRectangle.Height
                );

                PaintEventArgs parentArgs = new PaintEventArgs(graphics, parentClip);
                InvokePaintBackground(parent, parentArgs);
                InvokePaint(parent, parentArgs);
            }
            finally
            {
                graphics.Restore(state);
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

            if (isPlaceholderVisible && !readOnly)
            {
                isPlaceholderVisible = false;
                innerTextBox.Text = "";
                innerTextBox.ForeColor = TextColor;
            }

            this.Invalidate();
        }

        private void InnerTextBox_Leave(object sender, EventArgs e)
        {
            focused = false;
            ApplyPlaceholder();
            this.Invalidate();
        }

        private void InnerTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!isPlaceholderVisible)
            {
                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void SetValue(string value)
        {
            isPlaceholderVisible = false;
            innerTextBox.ForeColor = TextColor;
            innerTextBox.Text = value == null ? "" : value;
            ApplyPlaceholder();
        }

        private void ApplyPlaceholder()
        {
            if (innerTextBox == null)
            {
                return;
            }

            if (innerTextBox.Text.Trim() == "" && Placeholder.Trim() != "")
            {
                isPlaceholderVisible = true;
                innerTextBox.ForeColor = PlaceholderColor;
                innerTextBox.Text = Placeholder;
            }
            else if (!isPlaceholderVisible)
            {
                innerTextBox.ForeColor = TextColor;
            }
        }
    }
}
