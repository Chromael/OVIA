using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmBackupRestore : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canRestore;

        private Panel backupCard;
        private Panel restoreCard;
        private Button btnCreateBackup;
        private Button btnChooseBackup;
        private Button btnRestore;
        private Label lblBackupTitle;
        private Label lblBackupDescription;
        private Label lblRestoreTitle;
        private Label lblRestoreDescription;
        private Label lblRestoreWarning;
        private Label lblBackupStatus;
        private Label lblSelectedFile;
        private Label lblBackupMeta;
        private OviaCheckBox chkSystemSettings;
        private OviaCheckBox chkConnections;
        private OviaCheckBox chkMapping;
        private OviaCheckBox chkRebar;

        private string selectedBackupPath = "";
        private OviaBackupInspection selectedInspection;

        public FrmBackupRestore(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.canRestore = OviaSystemSettingsStore.IsSystemAdministrator(this.companyId, this.userId);

            BuildUI();
        }

        private void BuildUI()
        {
            SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            Controls.Clear();

            Text = "OVIA 백업/복원";
            Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(1180, 720);
            MinimumSize = new Size(1060, 650);
            BackColor = OviaFluentTheme.AppBackground;
            Resize += delegate { ApplyWorkspaceLayout(); };

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildCards(this);

            ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인  ›  환경설정  ›  백업/복원",
                delegate { NavigateBack(); },
                delegate { NavigateUp(); },
                delegate { ResetPage(); },
                delegate { RequestLogout(); },
                true,
                true,
                delegate(string target) { NavigateByWorkspacePath(target); }
            );
        }

        private void BuildCommandBar(Control parent)
        {
            Panel commandBar = new Panel();
            commandBar.Location = new Point(0, 48);
            commandBar.Size = new Size(1180, 50);
            commandBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            commandBar.BackColor = Color.White;
            commandBar.Paint += delegate(object sender, PaintEventArgs e)
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
            };
            OviaWorkspaceCommandBar.Populate(commandBar, "SETTINGS", companyId, userId);
            parent.Controls.Add(commandBar);
        }

        private void BuildCards(Control parent)
        {
            backupCard = CreateCard();
            restoreCard = CreateCard();
            parent.Controls.Add(backupCard);
            parent.Controls.Add(restoreCard);

            lblBackupTitle = CreateTitle("백업");
            lblBackupTitle.Location = new Point(24, 22);
            backupCard.Controls.Add(lblBackupTitle);

            lblBackupDescription = CreateDescription(
                "현재 OVIA의 사용자 환경과 운영 설정을 하나의 ZIP 파일로 저장합니다.\r\n"
                + "업무 BarList, ERP 작업로그, ERP 공사목록, 로그인 토큰은 백업하지 않습니다.");
            lblBackupDescription.Location = new Point(24, 58);
            lblBackupDescription.Size = new Size(500, 54);
            backupCard.Controls.Add(lblBackupDescription);

            int y = 126;
            AddFixedItem(backupCard, "시스템 설정", "페이지 크기, 브랜드 색상, 로고/로딩 이미지 등", y); y += 54;
            AddFixedItem(backupCard, "회사별 ERP 연결 설정", "등록된 기업별 기본 도메인/ERP 경로/인증 경로", y); y += 54;
            AddFixedItem(backupCard, "BarList 항목 매핑", "현재 적용 중인 BarList 헤더 매핑 사전", y); y += 54;
            AddFixedItem(backupCard, "이형철근 단위중량표", "현재 적용 중인 규격별 단위중량 및 길이별 기준", y); y += 62;

            btnCreateBackup = CreatePrimaryButton("백업 파일 만들기");
            btnCreateBackup.Location = new Point(24, y);
            btnCreateBackup.Click += CreateBackup_Click;
            backupCard.Controls.Add(btnCreateBackup);

            lblBackupStatus = CreateStatusLabel();
            lblBackupStatus.Location = new Point(24, y + 50);
            lblBackupStatus.Size = new Size(500, 50);
            lblBackupStatus.Text = "백업 파일에는 비밀번호와 로그인 토큰을 저장하지 않습니다.";
            backupCard.Controls.Add(lblBackupStatus);

            lblRestoreTitle = CreateTitle("복원");
            lblRestoreTitle.Location = new Point(24, 22);
            restoreCard.Controls.Add(lblRestoreTitle);

            lblRestoreDescription = CreateDescription(
                "OVIA 백업 ZIP을 먼저 검사한 뒤 선택한 항목만 복원합니다.\r\n"
                + "복원 도중 오류가 발생하면 변경 전 파일로 되돌립니다.");
            lblRestoreDescription.Location = new Point(24, 58);
            lblRestoreDescription.Size = new Size(500, 54);
            restoreCard.Controls.Add(lblRestoreDescription);

            btnChooseBackup = CreateNormalButton("백업 파일 선택");
            btnChooseBackup.Location = new Point(24, 122);
            btnChooseBackup.Click += ChooseBackup_Click;
            restoreCard.Controls.Add(btnChooseBackup);

            lblSelectedFile = CreateStatusLabel();
            lblSelectedFile.Location = new Point(154, 122);
            lblSelectedFile.Size = new Size(370, 38);
            lblSelectedFile.TextAlign = ContentAlignment.MiddleLeft;
            lblSelectedFile.Text = "선택된 파일 없음";
            restoreCard.Controls.Add(lblSelectedFile);

            lblBackupMeta = CreateStatusLabel();
            lblBackupMeta.Location = new Point(24, 170);
            lblBackupMeta.Size = new Size(500, 44);
            lblBackupMeta.Text = "백업 파일을 선택하면 생성일시와 포함 항목을 확인합니다.";
            restoreCard.Controls.Add(lblBackupMeta);

            chkSystemSettings = CreateRestoreCheckBox("시스템 설정", 24, 228);
            chkConnections = CreateRestoreCheckBox("회사별 ERP 연결 설정", 24, 270);
            chkMapping = CreateRestoreCheckBox("BarList 항목 매핑", 24, 312);
            chkRebar = CreateRestoreCheckBox("이형철근 단위중량표", 24, 354);

            chkSystemSettings.CheckedChanged += RestoreSelection_CheckedChanged;
            chkConnections.CheckedChanged += RestoreSelection_CheckedChanged;
            chkMapping.CheckedChanged += RestoreSelection_CheckedChanged;
            chkRebar.CheckedChanged += RestoreSelection_CheckedChanged;

            restoreCard.Controls.Add(chkSystemSettings);
            restoreCard.Controls.Add(chkConnections);
            restoreCard.Controls.Add(chkMapping);
            restoreCard.Controls.Add(chkRebar);

            btnRestore = CreatePrimaryButton("선택 항목 복원");
            btnRestore.Location = new Point(24, 414);
            btnRestore.Enabled = false;
            btnRestore.Click += Restore_Click;
            restoreCard.Controls.Add(btnRestore);

            lblRestoreWarning = CreateStatusLabel();
            lblRestoreWarning.Location = new Point(24, 464);
            lblRestoreWarning.Size = new Size(500, 70);
            lblRestoreWarning.Text =
                canRestore
                ? "복원 완료 후 시스템 설정은 즉시 다시 읽습니다. 열린 설정 화면은 새로고침하거나 다시 열어 확인하세요."
                : "복원은 시스템 관리자만 실행할 수 있습니다. 백업 파일 생성과 검사 기능은 사용할 수 있습니다.";
            restoreCard.Controls.Add(lblRestoreWarning);
        }

        private Panel CreateCard()
        {
            Panel panel = new Panel();
            panel.BackColor = Color.White;
            panel.Size = new Size(540, 560);
            panel.Resize += delegate
            {
                panel.Invalidate();
            };
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                Panel p = sender as Panel;
                if (p == null)
                {
                    return;
                }

                using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return panel;
        }

        private Label CreateTitle(string text)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Size = new Size(500, 28);
            label.Font = OviaFluentTheme.FontTitle(13F, FontStyle.Bold);
            label.ForeColor = OviaFluentTheme.TextPrimary;
            label.BackColor = Color.Transparent;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label CreateDescription(string text)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            label.ForeColor = OviaFluentTheme.TextSecondary;
            label.BackColor = Color.Transparent;
            label.Text = text;
            label.TextAlign = ContentAlignment.TopLeft;
            return label;
        }

        private Label CreateStatusLabel()
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            label.ForeColor = OviaFluentTheme.TextSecondary;
            label.BackColor = Color.Transparent;
            label.AutoEllipsis = true;
            return label;
        }

        private void AddFixedItem(Control parent, string title, string description, int y)
        {
            Label check = new Label();
            check.AutoSize = false;
            check.Location = new Point(24, y + 1);
            check.Size = new Size(24, 24);
            check.Font = OviaFluentTheme.FontSystem(11F, FontStyle.Bold);
            check.ForeColor = OviaFluentTheme.Success;
            check.Text = "✓";
            check.TextAlign = ContentAlignment.MiddleCenter;
            check.Tag = "BACKUP_CHECK";
            parent.Controls.Add(check);

            Label titleLabel = new Label();
            titleLabel.AutoSize = false;
            titleLabel.Location = new Point(56, y);
            titleLabel.Size = new Size(430, 22);
            titleLabel.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Bold);
            titleLabel.ForeColor = OviaFluentTheme.TextPrimary;
            titleLabel.Text = title;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Tag = "BACKUP_ITEM_TITLE";
            parent.Controls.Add(titleLabel);

            Label descLabel = new Label();
            descLabel.AutoSize = false;
            descLabel.Location = new Point(56, y + 22);
            descLabel.Size = new Size(430, 24);
            descLabel.Font = OviaFluentTheme.FontStatus(8.5F, FontStyle.Regular);
            descLabel.ForeColor = OviaFluentTheme.TextSecondary;
            descLabel.Text = description;
            descLabel.TextAlign = ContentAlignment.MiddleLeft;
            descLabel.AutoEllipsis = true;
            descLabel.Tag = "BACKUP_ITEM_DESC";
            parent.Controls.Add(descLabel);
        }

        private OviaCheckBox CreateRestoreCheckBox(string text, int x, int y)
        {
            OviaCheckBox box = new OviaCheckBox();
            box.Location = new Point(x, y);
            box.Size = new Size(300, 30);
            box.Text = text;
            box.Checked = false;
            box.Enabled = false;
            return box;
        }

        private void RestoreSelection_CheckedChanged(object sender, EventArgs e)
        {
            UpdateRestoreButton();
        }

        private Button CreatePrimaryButton(string text)
        {
            OVIA.Desktop.Controls.OviaButton button = CreateBaseButton(text);
            OviaFluentTheme.ApplyButton(button, OviaButtonRole.Primary);
            return button;
        }

        private Button CreateNormalButton(string text)
        {
            OVIA.Desktop.Controls.OviaButton button = CreateBaseButton(text);
            OviaFluentTheme.ApplyButton(button, OviaButtonRole.Neutral);
            return button;
        }

        private OVIA.Desktop.Controls.OviaButton CreateBaseButton(string text)
        {
            OVIA.Desktop.Controls.OviaButton button = new OVIA.Desktop.Controls.OviaButton();
            button.Size = new Size(120, OviaFluentTheme.ButtonHeight);
            button.Text = text;
            return button;
        }

        private void CreateBackup_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "OVIA 백업 파일 저장";
                dialog.Filter = "OVIA 백업 ZIP (*.zip)|*.zip";
                dialog.AddExtension = true;
                dialog.DefaultExt = "zip";
                dialog.FileName = "OVIA_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetBusy(true, "백업 파일을 생성하고 있습니다.");

                try
                {
                    string path = OviaBackupRestoreService.CreateBackup(dialog.FileName, companyId);
                    lblBackupStatus.Text = "백업 완료: " + path;

                    OviaNotificationStore.AddWorkLog(
                        companyId,
                        userId,
                        "OVIA 환경 백업 생성",
                        "메인  ›  환경설정  ›  백업/복원");

                    MessageBox.Show(
                        "OVIA 백업 파일을 만들었습니다.\r\n\r\n" + path,
                        "OVIA 백업",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    lblBackupStatus.Text = "백업 실패";
                    MessageBox.Show(
                        "백업 파일 생성 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                        "OVIA 백업",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
        }

        private void ChooseBackup_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "OVIA 백업 파일 선택";
                dialog.Filter = "OVIA 백업 ZIP (*.zip)|*.zip";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetBusy(true, "백업 파일을 검사하고 있습니다.");

                try
                {
                    OviaBackupInspection inspection = OviaBackupRestoreService.InspectBackup(dialog.FileName);

                    selectedBackupPath = dialog.FileName;
                    selectedInspection = inspection;

                    lblSelectedFile.Text = Path.GetFileName(dialog.FileName);
                    lblSelectedFile.Tag = dialog.FileName;

                    string createdAt = inspection.Manifest == null ? "" : inspection.Manifest.created_at;
                    lblBackupMeta.Text =
                        "생성일시: " + createdAt
                        + "  |  ERP 연결 설정: " + inspection.CompanyConnectionCount.ToString() + "개";

                    chkSystemSettings.Enabled = canRestore && inspection.HasSystemSettings;
                    chkConnections.Enabled = canRestore && inspection.HasCompanyConnections;
                    chkMapping.Enabled = canRestore && inspection.HasBarListMapping;
                    chkRebar.Enabled = canRestore && inspection.HasRebarUnitWeight;

                    chkSystemSettings.Checked = chkSystemSettings.Enabled;
                    chkConnections.Checked = chkConnections.Enabled;
                    chkMapping.Checked = chkMapping.Enabled;
                    chkRebar.Checked = chkRebar.Enabled;

                    UpdateRestoreButton();
                }
                catch (Exception ex)
                {
                    ClearSelectedBackup();

                    MessageBox.Show(
                        "선택한 파일을 OVIA 백업 파일로 사용할 수 없습니다.\r\n\r\n" + ex.Message,
                        "OVIA 백업 검사",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
        }

        private void Restore_Click(object sender, EventArgs e)
        {
            if (!canRestore)
            {
                MessageBox.Show(
                    "복원은 시스템 관리자만 실행할 수 있습니다.",
                    "OVIA 복원",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (selectedInspection == null || string.IsNullOrWhiteSpace(selectedBackupPath))
            {
                MessageBox.Show(
                    "먼저 OVIA 백업 파일을 선택해 주세요.",
                    "OVIA 복원",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            OviaBackupRestoreSelection selection = new OviaBackupRestoreSelection();
            selection.SystemSettings = chkSystemSettings.Checked && chkSystemSettings.Enabled;
            selection.CompanyConnections = chkConnections.Checked && chkConnections.Enabled;
            selection.BarListMapping = chkMapping.Checked && chkMapping.Enabled;
            selection.RebarUnitWeight = chkRebar.Checked && chkRebar.Enabled;

            if (!selection.SystemSettings
                && !selection.CompanyConnections
                && !selection.BarListMapping
                && !selection.RebarUnitWeight)
            {
                MessageBox.Show(
                    "복원할 항목을 한 개 이상 선택해 주세요.",
                    "OVIA 복원",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "선택한 OVIA 환경을 복원하시겠습니까?\r\n\r\n"
                + "복원 중 오류가 발생하면 변경 전 파일로 자동 되돌립니다.",
                "OVIA 백업 복원",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true, "선택한 항목을 복원하고 있습니다.");

            try
            {
                OviaBackupRestoreService.RestoreBackup(selectedBackupPath, selection);

                OviaNotificationStore.AddWorkLog(
                    companyId,
                    userId,
                    "OVIA 환경 백업 복원",
                    "메인  ›  환경설정  ›  백업/복원");

                MessageBox.Show(
                    "선택한 OVIA 환경을 복원했습니다.\r\n\r\n"
                    + "열려 있는 설정/매핑 화면이 있다면 새로고침하거나 다시 열어 확인해 주세요.",
                    "OVIA 복원 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "복원 중 오류가 발생했습니다.\r\n"
                    + "변경된 파일은 가능한 범위에서 복원 전 상태로 되돌렸습니다.\r\n\r\n"
                    + ex.Message,
                    "OVIA 복원 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void UpdateRestoreButton()
        {
            btnRestore.Enabled =
                canRestore
                && selectedInspection != null
                && ((chkSystemSettings.Enabled && chkSystemSettings.Checked)
                    || (chkConnections.Enabled && chkConnections.Checked)
                    || (chkMapping.Enabled && chkMapping.Checked)
                    || (chkRebar.Enabled && chkRebar.Checked));
        }

        private void SetBusy(bool busy, string status)
        {
            btnCreateBackup.Enabled = !busy;
            btnChooseBackup.Enabled = !busy;
            if (busy)
            {
                btnRestore.Enabled = false;
                Cursor = Cursors.WaitCursor;
            }
            else
            {
                Cursor = Cursors.Default;
                UpdateRestoreButton();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                lblBackupStatus.Text = status;
            }

            Application.DoEvents();
        }

        private void ResetPage()
        {
            ClearSelectedBackup();
            lblBackupStatus.Text = "백업 파일에는 비밀번호와 로그인 토큰을 저장하지 않습니다.";
        }

        private void ClearSelectedBackup()
        {
            selectedBackupPath = "";
            selectedInspection = null;
            lblSelectedFile.Text = "선택된 파일 없음";
            lblSelectedFile.Tag = null;
            lblBackupMeta.Text = "백업 파일을 선택하면 생성일시와 포함 항목을 확인합니다.";

            chkSystemSettings.Checked = false;
            chkConnections.Checked = false;
            chkMapping.Checked = false;
            chkRebar.Checked = false;

            chkSystemSettings.Enabled = false;
            chkConnections.Enabled = false;
            chkMapping.Enabled = false;
            chkRebar.Enabled = false;
            btnRestore.Enabled = false;
        }

        public void ApplyWorkspaceLayout()
        {
            int width = Math.Max(1, ClientSize.Width);
            int contentTop = 116;
            int inset = 25;
            int gap = 18;
            int available = Math.Max(700, width - (inset * 2));
            int cardWidth = Math.Max(330, (available - gap) / 2);
            int cardHeight = Math.Max(540, ClientSize.Height - contentTop - 24);

            backupCard.Location = new Point(inset, contentTop);
            backupCard.Size = new Size(cardWidth, cardHeight);

            restoreCard.Location = new Point(inset + cardWidth + gap, contentTop);
            restoreCard.Size = new Size(Math.Max(330, available - cardWidth - gap), cardHeight);

            LayoutBackupCard();
            LayoutRestoreCard();

            backupCard.Invalidate();
            restoreCard.Invalidate();
            Invalidate();
        }

        private void LayoutBackupCard()
        {
            if (backupCard == null)
            {
                return;
            }

            int fullWidth = Math.Max(220, backupCard.ClientSize.Width - 48);
            int itemWidth = Math.Max(180, backupCard.ClientSize.Width - 80);

            if (lblBackupTitle != null) lblBackupTitle.Width = fullWidth;
            if (lblBackupDescription != null) lblBackupDescription.Width = fullWidth;
            if (lblBackupStatus != null) lblBackupStatus.Width = fullWidth;

            for (int i = 0; i < backupCard.Controls.Count; i++)
            {
                Label label = backupCard.Controls[i] as Label;
                if (label == null)
                {
                    continue;
                }

                string role = label.Tag as string;
                if (role == "BACKUP_ITEM_TITLE" || role == "BACKUP_ITEM_DESC")
                {
                    label.Width = itemWidth;
                }
            }
        }

        private void LayoutRestoreCard()
        {
            if (restoreCard == null)
            {
                return;
            }

            int fullWidth = Math.Max(220, restoreCard.ClientSize.Width - 48);

            if (lblRestoreTitle != null) lblRestoreTitle.Width = fullWidth;
            if (lblRestoreDescription != null) lblRestoreDescription.Width = fullWidth;
            if (lblBackupMeta != null) lblBackupMeta.Width = fullWidth;
            if (lblRestoreWarning != null) lblRestoreWarning.Width = fullWidth;

            if (lblSelectedFile != null)
            {
                lblSelectedFile.Width = Math.Max(120, restoreCard.ClientSize.Width - lblSelectedFile.Left - 24);
            }

            if (chkSystemSettings != null) chkSystemSettings.Width = fullWidth;
            if (chkConnections != null) chkConnections.Width = fullWidth;
            if (chkMapping != null) chkMapping.Width = fullWidth;
            if (chkRebar != null) chkRebar.Width = fullWidth;
        }

        public bool CanLeaveWorkspaceScreen()
        {
            return true;
        }

        public void BeforeLeaveWorkspaceScreen()
        {
        }

        public bool HasUnsavedWorkspaceData()
        {
            return false;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "";
        }

        private void NavigateBack()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateBackInWorkspace();
            }
            else
            {
                Close();
            }
        }

        private void NavigateUp()
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace != null)
            {
                workspace.NavigateUpInWorkspace();
            }
            else
            {
                Close();
            }
        }

        private void NavigateByWorkspacePath(string target)
        {
            IOviaWorkspaceNavigator workspace = OviaWorkspaceNavigation.FindNavigator(this);
            if (workspace == null)
            {
                Close();
                return;
            }

            if (target == "MAIN")
            {
                workspace.NavigateToMain();
            }
            else if (target == "SETTINGS")
            {
                workspace.NavigateToWorkspaceInfoPage(
                    "SETTINGS",
                    "메인  ›  환경설정",
                    "환경설정",
                    "SETTINGS",
                    "환경설정 화면입니다.",
                    "시스템 설정, BarList 항목 매핑, 이형철근 단위중량표, 백업/복원, 버전정보를 관리합니다."
                );
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
