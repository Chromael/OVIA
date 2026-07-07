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
        private Panel erpSection;
        private Panel logoSection;
        private Panel listSection;
        private Panel colorSection;
        private Label titleLabel;
        private Label descLabel;
        private Label lblStatus;
        private OviaSystemInputBox txtErpUrl;
        private OviaSystemInputBox txtLogoPath;
        private OviaSystemInputBox txtListPageSize;
        private OviaSystemInputBox txtBrandPrimaryHex;
        private OviaSystemInputBox txtBrandHoverHex;
        private PictureBox logoPreview;
        private Button btnBrowseLogo;
        private Button btnDefaultLogo;
        private Panel pnlBrandPrimaryPreview;
        private Panel pnlBrandHoverPreview;
        private Button btnPickBrandPrimary;
        private Button btnPickBrandHover;
        private Button btnDefaultBrandColors;
        private Button btnSave;

        private string currentLogoPath = "";
        private string pendingLogoSourcePath = "";
        private bool defaultLogoRequested = false;
        private bool isDirty = false;
        private bool isLoading = false;
        private string cleanSignature = "";


        public string WorkspaceHelpKey { get { return "SYSTEM_SETTINGS"; } }
        public string WorkspaceHelpTitle { get { return "시스템 설정"; } }
        public string WorkspaceHelpText
        {
            get
            {
                return "ERP 연결 주소, 회사 로고, 리스트 출력 수, OVIA 주력 색상처럼 OVIA 전체에 적용되는 기본값을 관리합니다. 리스트 출력 수는 알림, 공사관리, 공사별 BarList 등 리스트 형식 화면의 한 페이지 표시 기준으로 사용됩니다.";
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
            this.MinimumSize = new Size(1060, 650);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmSystemSettings_FormClosing;

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
                "메인  ›  시스템관리  ›  시스템 설정",
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
            if (target == "MAIN" || target == "SETTINGS")
            {
                IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);

                if (workspace != null)
                {
                    workspace.NavigateToMain();
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
            descLabel.Text = "ERP 연결 주소와 회사 로고처럼 OVIA 전체에 적용되는 기본값을 관리합니다. 이 화면의 저장 권한은 셀먼 시스템 관리자에게만 부여됩니다.";
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
            contentPanel.Location = new Point(0, 104);
            contentPanel.Size = new Size(1180, 472);
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.BackColor = SurfaceColor;
            contentPanel.AutoScroll = true;
            parent.Controls.Add(contentPanel);

            BuildErpSection(contentPanel);
            BuildLogoSection(contentPanel);
            BuildListSection(contentPanel);
            BuildColorSection(contentPanel);
            contentPanel.AutoScrollMinSize = new Size(0, 810);
        }

        private void BuildErpSection(Control parent)
        {
            erpSection = new Panel();
            erpSection.Location = new Point(25, 25);
            erpSection.Size = new Size(1088, 142);
            erpSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            erpSection.BackColor = SurfaceColor;
            parent.Controls.Add(erpSection);

            AddRequiredTitle(erpSection, "ERP 연결", 0, 0);

            txtErpUrl = new OviaSystemInputBox();
            txtErpUrl.Location = new Point(0, 38);
            txtErpUrl.Size = new Size(830, 48);
            txtErpUrl.Placeholder = "ERP 웹 로그인페이지 URL을 입력해 주세요. 예: https://erp.example.com/login";
            txtErpUrl.ValueChanged += Input_ValueChanged;
            erpSection.Controls.Add(txtErpUrl);

            Label helper = new Label();
            helper.Text = "저장된 ERP 주소는 WebView2로 불러올 웹 ERP 기준 주소입니다. ERP 주소가 변경되면 시스템관리자가 이 값을 수정한 뒤 설치파일 재배포 또는 OVIA 실행 시 자동 업데이트 방식으로 반영할 수 있습니다.";
            helper.AutoSize = false;
            helper.Location = new Point(2, 96);
            helper.Size = new Size(980, 24);
            helper.TextAlign = ContentAlignment.MiddleLeft;
            helper.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            helper.ForeColor = TextSub;
            helper.BackColor = SurfaceColor;
            erpSection.Controls.Add(helper);

            Panel line = new Panel();
            line.Location = new Point(0, 136);
            line.Size = new Size(1088, 1);
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            line.BackColor = OviaFluentTheme.CardBorder;
            erpSection.Controls.Add(line);
        }

        private void BuildLogoSection(Control parent)
        {
            logoSection = new Panel();
            logoSection.Location = new Point(25, 183);
            logoSection.Size = new Size(1088, 218);
            logoSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            logoSection.BackColor = SurfaceColor;
            parent.Controls.Add(logoSection);

            AddRequiredTitle(logoSection, "회사로고 설정", 0, 0);

            txtLogoPath = new OviaSystemInputBox();
            txtLogoPath.Location = new Point(0, 38);
            txtLogoPath.Size = new Size(690, 48);
            txtLogoPath.Placeholder = "회사 로고 이미지 파일을 선택해 주세요.";
            txtLogoPath.ReadOnly = true;
            logoSection.Controls.Add(txtLogoPath);

            btnBrowseLogo = CreateBlackButton("이미지 선택", 706, 38, 126, 48);
            btnBrowseLogo.Click += BrowseLogo_Click;
            logoSection.Controls.Add(btnBrowseLogo);

            btnDefaultLogo = CreateNormalButton("기본 OVIA 로고 사용", 846, 38, 158, 48);
            btnDefaultLogo.Click += DefaultLogo_Click;
            logoSection.Controls.Add(btnDefaultLogo);

            Label helper = new Label();
            helper.Text = "이미지를 저장하면 다음 로그인 화면부터 현재 OVIA 로고 영역에 회사 로고가 표시됩니다. 기본값으로 되돌리면 기존 OVIA 로고가 표시됩니다.";
            helper.AutoSize = false;
            helper.Location = new Point(2, 96);
            helper.Size = new Size(980, 24);
            helper.TextAlign = ContentAlignment.MiddleLeft;
            helper.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            helper.ForeColor = TextSub;
            helper.BackColor = SurfaceColor;
            logoSection.Controls.Add(helper);

            Panel previewPanel = new Panel();
            previewPanel.Location = new Point(0, 132);
            previewPanel.Size = new Size(360, 72);
            previewPanel.BackColor = Color.White;
            previewPanel.Paint += PreviewPanel_Paint;
            logoSection.Controls.Add(previewPanel);

            logoPreview = new PictureBox();
            logoPreview.Location = new Point(16, 10);
            logoPreview.Size = new Size(328, 52);
            logoPreview.SizeMode = PictureBoxSizeMode.Zoom;
            logoPreview.BackColor = Color.White;
            previewPanel.Controls.Add(logoPreview);

            Label previewText = new Label();
            previewText.Text = "로그인 화면 로고 미리보기";
            previewText.AutoSize = false;
            previewText.Location = new Point(380, 142);
            previewText.Size = new Size(260, 24);
            previewText.TextAlign = ContentAlignment.MiddleLeft;
            previewText.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Bold);
            previewText.ForeColor = TextDark;
            previewText.BackColor = SurfaceColor;
            logoSection.Controls.Add(previewText);

            Label previewNote = new Label();
            previewNote.Text = "로고 파일은 저장 시 OVIA 로컬 설정 폴더에 복사되어 관리됩니다.";
            previewNote.AutoSize = false;
            previewNote.Location = new Point(380, 166);
            previewNote.Size = new Size(460, 24);
            previewNote.TextAlign = ContentAlignment.MiddleLeft;
            previewNote.Font = OviaFluentTheme.FontStatus(8.6F, FontStyle.Regular);
            previewNote.ForeColor = TextSub;
            previewNote.BackColor = SurfaceColor;
            logoSection.Controls.Add(previewNote);
        }

        private void BuildListSection(Control parent)
        {
            listSection = new Panel();
            listSection.Location = new Point(25, 425);
            listSection.Size = new Size(1088, 142);
            listSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            listSection.BackColor = SurfaceColor;
            parent.Controls.Add(listSection);

            AddRequiredTitle(listSection, "리스트 출력 수 설정", 0, 0);

            txtListPageSize = new OviaSystemInputBox();
            txtListPageSize.Location = new Point(0, 38);
            txtListPageSize.Size = new Size(180, 48);
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

            Panel line = new Panel();
            line.Location = new Point(0, 136);
            line.Size = new Size(1088, 1);
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            line.BackColor = OviaFluentTheme.CardBorder;
            listSection.Controls.Add(line);
        }

        private void BuildColorSection(Control parent)
        {
            colorSection = new Panel();
            colorSection.Location = new Point(25, 591);
            colorSection.Size = new Size(1088, 176);
            colorSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            colorSection.BackColor = SurfaceColor;
            parent.Controls.Add(colorSection);

            AddRequiredTitle(colorSection, "색상 규칙", 0, 0);

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
            txtBrandPrimaryHex.Size = new Size(160, 48);
            txtBrandPrimaryHex.Placeholder = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
            txtBrandPrimaryHex.ValueChanged += ColorInput_ValueChanged;
            colorSection.Controls.Add(txtBrandPrimaryHex);

            pnlBrandPrimaryPreview = CreateColorPreviewPanel(174, 66);
            colorSection.Controls.Add(pnlBrandPrimaryPreview);

            btnPickBrandPrimary = CreateNormalButton("색상 선택", 220, 58, 112, 48);
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
            txtBrandHoverHex.Size = new Size(160, 48);
            txtBrandHoverHex.Placeholder = OviaSystemSettingsStore.DefaultBrandHoverHex;
            txtBrandHoverHex.ValueChanged += ColorInput_ValueChanged;
            colorSection.Controls.Add(txtBrandHoverHex);

            pnlBrandHoverPreview = CreateColorPreviewPanel(546, 66);
            colorSection.Controls.Add(pnlBrandHoverPreview);

            btnPickBrandHover = CreateNormalButton("색상 선택", 592, 58, 112, 48);
            btnPickBrandHover.Click += PickBrandHover_Click;
            colorSection.Controls.Add(btnPickBrandHover);

            btnDefaultBrandColors = CreateNormalButton("기본값 설정", 742, 58, 126, 48);
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

            Panel line = new Panel();
            line.Location = new Point(0, 168);
            line.Size = new Size(1088, 1);
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            line.BackColor = OviaFluentTheme.CardBorder;
            colorSection.Controls.Add(line);
        }

        private Panel CreateColorPreviewPanel(int x, int y)
        {
            Panel panel = new Panel();
            panel.Location = new Point(x, y);
            panel.Size = new Size(30, 30);
            panel.BackColor = OviaFluentTheme.Accent;
            panel.Paint += ColorPreviewPanel_Paint;
            return panel;
        }

        private void ColorPreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);

            using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, 6))
            using (SolidBrush brush = new SolidBrush(control.BackColor))
            using (Pen pen = new Pen(OviaFluentTheme.ControlBorder, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
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
                pnlBrandPrimaryPreview.BackColor = OviaSystemSettingsStore.HexToColor(
                    txtBrandPrimaryHex == null ? OviaSystemSettingsStore.DefaultBrandPrimaryHex : txtBrandPrimaryHex.Value,
                    OviaSystemSettingsStore.HexToColor(OviaSystemSettingsStore.DefaultBrandPrimaryHex, Color.FromArgb(37, 99, 235))
                );
                pnlBrandPrimaryPreview.Invalidate();
            }

            if (pnlBrandHoverPreview != null)
            {
                pnlBrandHoverPreview.BackColor = OviaSystemSettingsStore.HexToColor(
                    txtBrandHoverHex == null ? OviaSystemSettingsStore.DefaultBrandHoverHex : txtBrandHoverHex.Value,
                    OviaSystemSettingsStore.HexToColor(OviaSystemSettingsStore.DefaultBrandHoverHex, Color.FromArgb(29, 78, 216))
                );
                pnlBrandHoverPreview.Invalidate();
            }
        }

        private void BuildBottomButtons(Control parent)
        {
            btnSave = CreateBlackButton("저장하기", 1018, 616, 130, 46);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnSave.Click += Save_Click;
            btnSave.Visible = false;
            parent.Controls.Add(btnSave);
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.Text = "시스템 설정을 불러오는 중입니다.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(940, 38);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.8F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(32, 618);
            parent.Controls.Add(lblStatus);
        }

        private void AddRequiredTitle(Control parent, string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = OviaFluentTheme.FontBrand(10.5F, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.BackColor = SurfaceColor;
            label.Location = new Point(x, y);
            parent.Controls.Add(label);

            Label required = new Label();
            required.Text = "•";
            required.AutoSize = true;
            required.Font = OviaFluentTheme.FontBrand(13F, FontStyle.Bold);
            required.ForeColor = OviaFluentTheme.Danger;
            required.BackColor = SurfaceColor;
            required.Location = new Point(x + label.PreferredWidth + 4, y - 2);
            parent.Controls.Add(required);
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

        public void ApplyWorkspaceLayout()
        {
            const int contentTop = 104;
            const int innerLeft = 25;
            const int innerRight = 25;
            const int statusHeight = 28;
            const int buttonGapAboveStatus = 30;
            const int rightButtonMargin = 25;

            int statusTop = Math.Max(contentTop + 260, this.ClientSize.Height - statusHeight);
            int buttonTop = btnSave == null ? Math.Max(contentTop + 220, statusTop - buttonGapAboveStatus - OviaFluentTheme.ButtonHeight) : Math.Max(contentTop + 220, statusTop - buttonGapAboveStatus - btnSave.Height);
            int contentWidth = Math.Max(1, this.ClientSize.Width);
            int contentHeight = Math.Max(220, buttonTop - contentTop - 12);
            int sectionWidth = Math.Max(360, contentWidth - innerLeft - innerRight - SystemInformation.VerticalScrollBarWidth);

            if (descLabel != null)
            {
                descLabel.Width = Math.Max(1, this.ClientSize.Width - 70);
            }

            if (contentPanel != null)
            {
                contentPanel.Location = new Point(0, contentTop);
                contentPanel.Size = new Size(contentWidth, contentHeight);
            }

            if (erpSection != null)
            {
                erpSection.Left = innerLeft;
                erpSection.Top = innerLeft;
                erpSection.Width = sectionWidth;
            }

            if (logoSection != null)
            {
                logoSection.Left = innerLeft;
                logoSection.Top = innerLeft + 158;
                logoSection.Width = sectionWidth;
            }

            if (listSection != null)
            {
                listSection.Left = innerLeft;
                listSection.Top = innerLeft + 400;
                listSection.Width = sectionWidth;
            }

            if (colorSection != null)
            {
                colorSection.Left = innerLeft;
                colorSection.Top = innerLeft + 566;
                colorSection.Width = sectionWidth;
            }

            if (contentPanel != null)
            {
                contentPanel.AutoScrollMinSize = new Size(Math.Max(0, sectionWidth + innerLeft + innerRight), 810);
            }

            if (txtErpUrl != null)
            {
                txtErpUrl.Width = Math.Max(520, sectionWidth - 18);
            }

            if (txtLogoPath != null)
            {
                int buttonArea = 318;
                txtLogoPath.Width = Math.Max(420, sectionWidth - buttonArea - 18);
            }

            if (btnBrowseLogo != null && txtLogoPath != null)
            {
                btnBrowseLogo.Left = txtLogoPath.Right + 16;
            }

            if (btnDefaultLogo != null && btnBrowseLogo != null)
            {
                btnDefaultLogo.Left = btnBrowseLogo.Right + 14;
            }

            if (lblStatus != null)
            {
                lblStatus.Location = new Point(0, statusTop);
                lblStatus.Size = new Size(Math.Max(1, this.ClientSize.Width), statusHeight);
                lblStatus.TextAlign = ContentAlignment.MiddleLeft;
                lblStatus.Padding = new Padding(16, 0, 0, 0);
            }

            if (btnSave != null)
            {
                btnSave.Location = new Point(Math.Max(0, this.ClientSize.Width - rightButtonMargin - btnSave.Width), buttonTop);
            }
        }

        private void LoadSettingsToUi()
        {
            isLoading = true;

            try
            {
                OviaSystemSettings settings = OviaSystemSettingsStore.Load();
                txtErpUrl.Value = settings.ErpLoginUrl;
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

                cleanSignature = GetCurrentSignature();
                isDirty = false;

                if (!canEdit)
                {
                    SetReadOnlyMode();
                    UpdateStatus("시스템 설정은 셀먼 시스템 관리자만 저장할 수 있습니다. 현재 화면은 보기 전용입니다.");
                }
                else
                {
                    UpdateStatus("시스템 설정을 불러왔습니다. 변경사항이 있으면 저장하기 버튼이 표시됩니다.");
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
            if (txtErpUrl != null)
            {
                txtErpUrl.ReadOnly = true;
            }

            if (txtLogoPath != null)
            {
                txtLogoPath.ReadOnly = true;
            }

            if (txtListPageSize != null)
            {
                txtListPageSize.ReadOnly = true;
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

            if (btnSave != null)
            {
                btnSave.Enabled = false;
                btnSave.Visible = false;
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
            txtLogoPath.Value = pendingLogoSourcePath;
            UpdateLogoPreview(pendingLogoSourcePath);
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
            txtLogoPath.Value = "기본 OVIA 로고 사용";
            UpdateLogoPreview("");
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
                return;
            }

            string erpUrl = txtErpUrl.Value.Trim();
            string listPageSizeText = txtListPageSize == null ? "100" : txtListPageSize.Value.Trim();
            string brandPrimaryHex;
            string brandHoverHex;
            int listPageSize;

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

            if (erpUrl != "" && !IsValidWebUrl(erpUrl))
            {
                MessageBox.Show(
                    "ERP 연결 주소는 http:// 또는 https:// 로 시작하는 웹 주소로 입력해 주세요.",
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                txtErpUrl.Focus();
                return;
            }

            try
            {
                OviaSystemSettings settings = OviaSystemSettingsStore.Load();
                string beforeLogoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath.Trim();

                settings.ErpLoginUrl = erpUrl;
                settings.ListPageSize = listPageSize;
                settings.BrandPrimaryHex = brandPrimaryHex;
                settings.BrandHoverHex = brandHoverHex;

                if (defaultLogoRequested)
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

                string afterLogoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath.Trim();
                bool logoChanged = !AreSameLogoPath(beforeLogoPath, afterLogoPath);

                OviaSystemSettingsStore.Save(settings);

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

                UpdateColorPreviews();

                currentLogoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath;
                pendingLogoSourcePath = "";
                defaultLogoRequested = currentLogoPath.Trim() == "";

                if (currentLogoPath.Trim() != "" && File.Exists(currentLogoPath))
                {
                    txtLogoPath.Value = currentLogoPath;
                    UpdateLogoPreview(currentLogoPath);
                }
                else
                {
                    txtLogoPath.Value = "기본 OVIA 로고 사용";
                    UpdateLogoPreview("");
                }

                cleanSignature = GetCurrentSignature();
                isDirty = false;
                UpdateSaveButtonVisibility();

                string savedMessage = logoChanged
                    ? "저장되었습니다.\r\n\r\n회사 로고는 다음 로그인 화면부터 적용됩니다."
                    : "저장되었습니다.";

                MessageBox.Show(
                    savedMessage,
                    "OVIA 시스템 설정",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                UpdateStatus("시스템 설정을 저장했습니다. 저장 위치: " + OviaSystemSettingsStore.GetSettingsFilePath());
                OviaNotificationStore.AddWorkLog(companyId, userId, "시스템 설정 저장", "메인  ›  시스템관리  ›  시스템 설정");
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

            btnSave.Visible = canEdit && isDirty;
            btnSave.Enabled = canEdit && isDirty;
        }

        private string GetCurrentSignature()
        {
            string erp = txtErpUrl == null ? "" : txtErpUrl.Value.Trim();
            string pending = pendingLogoSourcePath == null ? "" : pendingLogoSourcePath.Trim();
            string current = currentLogoPath == null ? "" : currentLogoPath.Trim();
            string logo = defaultLogoRequested || (pending == "" && current == "") ? "DEFAULT" : (pending != "" ? pending : current);
            string listPageSize = txtListPageSize == null ? "100" : txtListPageSize.Value.Trim();
            string brandPrimary = txtBrandPrimaryHex == null ? OviaSystemSettingsStore.DefaultBrandPrimaryHex : OviaSystemSettingsStore.NormalizeHexColor(txtBrandPrimaryHex.Value, OviaSystemSettingsStore.DefaultBrandPrimaryHex);
            string brandHover = txtBrandHoverHex == null ? OviaSystemSettingsStore.DefaultBrandHoverHex : OviaSystemSettingsStore.NormalizeHexColor(txtBrandHoverHex.Value, OviaSystemSettingsStore.DefaultBrandHoverHex);
            return erp + "|" + logo + "|" + listPageSize + "|" + brandPrimary + "|" + brandHover;
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
