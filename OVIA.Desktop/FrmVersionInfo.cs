using System;
using System.Drawing;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmVersionInfo : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private Label lblStatus;
        private TextBox txtVersion;
        private Button btnSave;
        private Button btnClose;
        private bool isDirty;
        private bool isLoading;
        private string cleanVersion = "";

        public FrmVersionInfo(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.canEdit = OviaSystemSettingsStore.IsSuperAdminUser(this.userId);

            BuildUI();
            LoadVersionToUi();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA - 버전정보";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ClientSize = new Size(1180, 720);
            this.MinimumSize = new Size(1060, 650);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmVersionInfo_FormClosing;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildTitle(this);
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
                "메인  ›  환경설정  ›  버전정보",
                delegate { this.Close(); },
                delegate { this.Close(); },
                delegate { if (ConfirmDiscardUnsavedChanges()) LoadVersionToUi(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target) { NavigateByWorkspacePath(target); }
            );
        }

        private void NavigateByWorkspacePath(string target)
        {
            if (target == "MAIN" || target == "SETTINGS")
            {
                if (!ConfirmDiscardUnsavedChanges())
                {
                    return;
                }

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
            OviaWorkspaceCommandBar.Populate(commandBar, "SETTINGS");
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
            Label title = new Label();
            title.Text = "버전정보";
            title.AutoSize = true;
            title.Font = OviaFluentTheme.FontTitle(20F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(32, 128);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = canEdit
                ? "최고관리자는 OVIA 로그인 화면과 버전정보 페이지에 표시할 버전을 수정할 수 있습니다."
                : "현재 사용자는 보기 전용입니다. 버전정보 수정은 최고관리자만 가능합니다.";
            desc.AutoSize = false;
            desc.Size = new Size(920, 42);
            desc.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(35, 172);
            parent.Controls.Add(desc);
        }

        private void BuildContent(Control parent)
        {
            Panel card = new Panel();
            card.Location = new Point(32, 226);
            card.Size = new Size(760, 156);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.BackColor = Color.White;
            card.Paint += Card_Paint;
            parent.Controls.Add(card);

            Label label = new Label();
            label.Text = "버전";
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(26, 26);
            label.Size = new Size(130, 30);
            label.Font = OviaFluentTheme.FontButton(9F, FontStyle.Bold);
            label.ForeColor = TextDark;
            label.BackColor = Color.White;
            card.Controls.Add(label);

            Label prefix = new Label();
            prefix.Text = "Version";
            prefix.AutoSize = false;
            prefix.TextAlign = ContentAlignment.MiddleCenter;
            prefix.Location = new Point(160, 26);
            prefix.Size = new Size(88, 34);
            prefix.Font = OviaFluentTheme.FontButton(9F, FontStyle.Bold);
            prefix.ForeColor = TextDark;
            prefix.BackColor = OviaFluentTheme.HeaderBackground;
            prefix.BorderStyle = BorderStyle.FixedSingle;
            card.Controls.Add(prefix);

            txtVersion = new TextBox();
            txtVersion.Location = new Point(258, 31);
            txtVersion.Size = new Size(230, 24);
            txtVersion.BorderStyle = BorderStyle.FixedSingle;
            txtVersion.Font = OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            txtVersion.ReadOnly = !canEdit;
            txtVersion.TextChanged += delegate
            {
                if (!isLoading)
                {
                    isDirty = GetCurrentVersion() != cleanVersion;
                    UpdateSaveButtonVisibility();
                    UpdateStatusText();
                }
            };
            card.Controls.Add(txtVersion);

            Label note = new Label();
            note.Text = "예: 1.0.0 또는 2026.06.26 형태로 입력합니다. 로그인 화면 하단에는 Version 접두어가 붙어 표시됩니다.";
            note.AutoSize = false;
            note.Location = new Point(160, 76);
            note.Size = new Size(560, 44);
            note.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            note.ForeColor = TextSub;
            note.BackColor = Color.White;
            card.Controls.Add(note);
        }

        private void Card_Paint(object sender, PaintEventArgs e)
        {
            Control control = sender as Control;
            if (control == null)
            {
                return;
            }

            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
            }
        }

        private void BuildBottomButtons(Control parent)
        {
            btnSave = CreateButton("저장하기", 902, 596, 120);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnSave.BackColor = OviaFluentTheme.Accent;
            btnSave.ForeColor = Color.White;
            btnSave.Enabled = false;
            btnSave.Visible = false;
            btnSave.Click += Save_Click;
            parent.Controls.Add(btnSave);

            btnClose = CreateButton("닫기", 1038, 596, 110);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnClose.Click += delegate { this.Close(); };
            parent.Controls.Add(btnClose);
        }

        private Button CreateButton(string text, int x, int y, int width)
        {
            Button button = new OVIA.Desktop.Controls.OviaButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = OviaFluentTheme.MeasureButtonSize(text);
            OviaFluentTheme.ApplyButton(button, text);
            return button;
        }

        private void BuildStatus(Control parent)
        {
            lblStatus = new Label();
            lblStatus.Text = "버전정보를 불러오는 중입니다.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(1116, 40);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(32, 654);
            parent.Controls.Add(lblStatus);
        }

        private void LoadVersionToUi()
        {
            isLoading = true;
            try
            {
                if (txtVersion != null)
                {
                    txtVersion.Text = OviaSystemSettingsStore.GetConfiguredVersionText();
                }

                cleanVersion = GetCurrentVersion();
                isDirty = false;
                UpdateSaveButtonVisibility();
                UpdateStatusText();
            }
            finally
            {
                isLoading = false;
            }
        }

        private string GetCurrentVersion()
        {
            return OviaSystemSettingsStore.NormalizeVersionText(txtVersion == null ? "" : txtVersion.Text);
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                MessageBox.Show("버전정보는 최고관리자만 수정할 수 있습니다.", "OVIA 권한 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!isDirty)
            {
                UpdateSaveButtonVisibility();
                return;
            }

            string version = GetCurrentVersion();
            if (version == "")
            {
                MessageBox.Show("버전정보를 입력해 주세요.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (txtVersion != null)
                {
                    txtVersion.Focus();
                }
                return;
            }

            OviaSystemSettings settings = OviaSystemSettingsStore.Load();
            settings.VersionText = version;
            OviaSystemSettingsStore.Save(settings);

            cleanVersion = version;
            isDirty = false;
            UpdateSaveButtonVisibility();
            UpdateStatusText();
            MessageBox.Show("버전정보가 저장되었습니다.", "OVIA 버전정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void UpdateStatusText()
        {
            if (lblStatus == null)
            {
                return;
            }

            if (!canEdit)
            {
                lblStatus.Text = "버전정보 보기 전용입니다. 최고관리자만 수정할 수 있습니다.";
            }
            else if (isDirty)
            {
                lblStatus.Text = "저장하지 않은 버전정보 변경사항이 있습니다.";
            }
            else
            {
                lblStatus.Text = "버전정보를 불러왔습니다. 저장 위치: " + OviaSystemSettingsStore.GetSettingsFilePath();
            }
        }

        private void FrmVersionInfo_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!canEdit || !isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 버전정보 변경사항이 있습니다.\r\n\r\n저장하지 않고 이동하시겠습니까?",
                "OVIA 버전정보",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            return result == DialogResult.Yes;
        }

        public bool CanLeaveWorkspaceScreen()
        {
            return ConfirmDiscardUnsavedChanges();
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
            return "버전정보";
        }

        public void ApplyWorkspaceLayout()
        {
            if (btnSave != null)
            {
                btnSave.Location = new Point(Math.Max(32, this.ClientSize.Width - 278), Math.Max(420, this.ClientSize.Height - 124));
            }

            if (btnClose != null)
            {
                btnClose.Location = new Point(Math.Max(32, this.ClientSize.Width - 142), Math.Max(420, this.ClientSize.Height - 124));
            }

            if (lblStatus != null)
            {
                lblStatus.Width = Math.Max(1, this.ClientSize.Width - 64);
                lblStatus.Location = new Point(32, Math.Max(460, this.ClientSize.Height - 66));
            }
        }

        private void RequestLogout()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.RequestLogout();
            }
        }
    }
}
