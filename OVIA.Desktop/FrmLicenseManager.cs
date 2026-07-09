using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class FrmLicenseManager : Form, IOviaWorkspaceScreen, IOviaWorkspaceLayout, IOviaWorkspaceUnsavedState
    {
        private readonly string companyId;
        private readonly string userId;
        private readonly bool canEdit;

        private readonly Color SurfaceColor = OviaFluentTheme.AppBackground;
        private readonly Color TextDark = OviaFluentTheme.TextPrimary;
        private readonly Color TextSub = OviaFluentTheme.TextSecondary;

        private Panel listPanel;
        private Label lblStatus;
        private Button btnAdd;
        private Button btnSave;
        private Button btnClose;
        private List<OviaLicenseEntry> entries = new List<OviaLicenseEntry>();
        private List<OviaLicenseEntryPanel> entryPanels = new List<OviaLicenseEntryPanel>();
        private bool isLoading;
        private bool isDirty;
        private string cleanSignature = "";

        public FrmLicenseManager(string companyId, string userId)
        {
            this.companyId = companyId == null ? "" : companyId;
            this.userId = userId == null ? "" : userId;
            this.canEdit = OviaSystemSettingsStore.IsSystemAdministrator(this.companyId, this.userId);

            BuildUI();
            LoadLicensesToUi();
        }

        private void BuildUI()
        {
            this.SuspendLayout();

            OviaFluentTheme.ApplyForm(this);
            this.Controls.Clear();

            this.Text = "OVIA - License";
            this.Font = OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = true;
            this.MaximizeBox = true;
            this.ClientSize = new Size(1180, 720);
            this.MinimumSize = new Size(1060, 650);
            this.BackColor = SurfaceColor;
            this.FormClosing += FrmLicenseManager_FormClosing;

            BuildExplorerHeader(this);
            BuildCommandBar(this);
            BuildTitle(this);
            BuildListPanel(this);
            BuildBottomButtons(this);
            BuildStatus(this);

            this.ResumeLayout(false);
            ApplyWorkspaceLayout();
        }

        private void BuildExplorerHeader(Control parent)
        {
            OviaWorkspaceHeader.AddTo(
                parent,
                "메인  ›  환경설정  ›  License",
                delegate { this.Close(); },
                delegate { this.Close(); },
                delegate { if (ConfirmDiscardUnsavedChanges()) LoadLicensesToUi(); },
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
            Label title = new Label();
            title.Text = "License";
            title.AutoSize = true;
            title.Font = OviaFluentTheme.FontTitle(20F, FontStyle.Bold);
            title.ForeColor = TextDark;
            title.BackColor = SurfaceColor;
            title.Location = new Point(32, 128);
            parent.Controls.Add(title);

            Label desc = new Label();
            desc.Text = canEdit
                ? "OVIA에 포함되거나 사용되는 라이선스 정보를 관리합니다. 에디터 모드는 사용하지 않고 텍스트 영역에 원문을 입력합니다."
                : "OVIA에 포함되거나 사용되는 라이선스 정보를 확인합니다. 현재 사용자는 보기 전용입니다.";
            desc.AutoSize = false;
            desc.Size = new Size(980, 42);
            desc.Font = OviaFluentTheme.FontSystem(9.5F, FontStyle.Regular);
            desc.ForeColor = TextSub;
            desc.BackColor = SurfaceColor;
            desc.Location = new Point(35, 172);
            parent.Controls.Add(desc);
        }

        private void BuildListPanel(Control parent)
        {
            listPanel = new Panel();
            listPanel.Location = new Point(32, 226);
            listPanel.Size = new Size(1116, 348);
            listPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listPanel.BackColor = SurfaceColor;
            listPanel.AutoScroll = true;
            parent.Controls.Add(listPanel);
        }

        private void BuildBottomButtons(Control parent)
        {
            btnAdd = CreateButton("라이선스 추가", 32, 596, 130);
            btnAdd.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnAdd.Enabled = canEdit;
            btnAdd.Click += Add_Click;
            parent.Controls.Add(btnAdd);

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
            lblStatus.Text = "License 정보를 불러오는 중입니다.";
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(1116, 40);
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblStatus.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            lblStatus.ForeColor = TextSub;
            lblStatus.BackColor = SurfaceColor;
            lblStatus.Location = new Point(32, 654);
            parent.Controls.Add(lblStatus);
        }

        private void LoadLicensesToUi()
        {
            isLoading = true;
            try
            {
                entries = OviaOpenSourceLicenseStore.Load();
                RenderEntries();
                cleanSignature = BuildSignature();
                isDirty = false;
                UpdateSaveButtonVisibility();
                UpdateStatusText();
            }
            finally
            {
                isLoading = false;
            }
        }

        private void RenderEntries()
        {
            if (listPanel == null)
            {
                return;
            }

            listPanel.SuspendLayout();
            listPanel.Controls.Clear();
            entryPanels.Clear();

            int y = 0;
            int width = Math.Max(360, listPanel.ClientSize.Width - 24);
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaLicenseEntryPanel panel = new OviaLicenseEntryPanel(entries[i], i, canEdit);
                panel.Location = new Point(0, y);
                panel.Width = width;
                panel.EntryChanged += Entry_Changed;
                panel.DeleteRequested += Entry_DeleteRequested;
                listPanel.Controls.Add(panel);
                entryPanels.Add(panel);
                y += panel.Height + 14;
            }

            listPanel.ResumeLayout(false);
        }

        private void Entry_Changed(object sender, EventArgs e)
        {
            if (isLoading)
            {
                return;
            }

            PullEntriesFromPanels();
            isDirty = BuildSignature() != cleanSignature;
            UpdateSaveButtonVisibility();
            UpdateStatusText();
        }

        private void Entry_DeleteRequested(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                return;
            }

            OviaLicenseEntryPanel panel = sender as OviaLicenseEntryPanel;
            if (panel == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                "선택한 라이선스 항목을 삭제하시겠습니까?",
                "OVIA License",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result != DialogResult.Yes)
            {
                return;
            }

            PullEntriesFromPanels();
            int index = panel.EntryIndex;
            if (index >= 0 && index < entries.Count)
            {
                entries.RemoveAt(index);
            }

            RenderEntries();
            isDirty = BuildSignature() != cleanSignature;
            UpdateSaveButtonVisibility();
            UpdateStatusText();
            OviaNotificationStore.AddWorkLog(companyId, userId, "라이선스 항목 삭제", "메인  ›  환경설정  ›  License");
        }

        private void Add_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                MessageBox.Show("License 정보는 최고관리자만 추가할 수 있습니다.", "OVIA 권한 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PullEntriesFromPanels();
            OviaLicenseEntry entry = new OviaLicenseEntry();
            entry.Title = "";
            entry.Url = "";
            entry.Content = "";
            entries.Add(entry);
            RenderEntries();
            isDirty = true;
            UpdateSaveButtonVisibility();
            UpdateStatusText();

            if (entryPanels.Count > 0)
            {
                OviaLicenseEntryPanel last = entryPanels[entryPanels.Count - 1];
                last.FocusTitle();
                listPanel.ScrollControlIntoView(last);
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (!canEdit)
            {
                MessageBox.Show("License 정보는 최고관리자만 저장할 수 있습니다.", "OVIA 권한 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PullEntriesFromPanels();
            isDirty = BuildSignature() != cleanSignature;
            UpdateSaveButtonVisibility();
            if (!isDirty)
            {
                return;
            }

            int i;
            for (i = 0; i < entries.Count; i++)
            {
                if (entries[i].Title.Trim() == "")
                {
                    MessageBox.Show("라이선스명을 입력해 주세요.", "OVIA License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (i < entryPanels.Count)
                    {
                        entryPanels[i].FocusTitle();
                    }
                    return;
                }
            }

            OviaOpenSourceLicenseStore.Save(entries);
            cleanSignature = BuildSignature();
            isDirty = false;
            UpdateSaveButtonVisibility();
            UpdateStatusText();
            MessageBox.Show("License 정보가 저장되었습니다.", "OVIA License", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OviaNotificationStore.AddWorkLog(companyId, userId, "License 정보 저장", "메인  ›  환경설정  ›  License");
        }

        private void PullEntriesFromPanels()
        {
            List<OviaLicenseEntry> next = new List<OviaLicenseEntry>();
            int i;
            for (i = 0; i < entryPanels.Count; i++)
            {
                next.Add(entryPanels[i].ToEntry());
            }

            entries = next;
        }

        private string BuildSignature()
        {
            PullEntriesFromPanelsIfReady();
            return OviaOpenSourceLicenseStore.BuildSignature(entries);
        }

        private void PullEntriesFromPanelsIfReady()
        {
            if (entryPanels == null || entryPanels.Count == 0)
            {
                return;
            }

            PullEntriesFromPanels();
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
                lblStatus.Text = "License 정보 보기 전용입니다. 최고관리자만 추가/수정/삭제할 수 있습니다.";
            }
            else if (isDirty)
            {
                lblStatus.Text = "저장하지 않은 License 변경사항이 있습니다.";
            }
            else
            {
                lblStatus.Text = "License 정보를 불러왔습니다. 저장 위치: " + OviaOpenSourceLicenseStore.GetLicenseFilePath();
            }
        }

        private void FrmLicenseManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!canEdit)
            {
                return true;
            }

            isDirty = BuildSignature() != cleanSignature;
            if (!isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(
                "저장하지 않은 License 변경사항이 있습니다.\r\n\r\n저장하지 않고 이동하시겠습니까?",
                "OVIA License",
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
            if (!canEdit)
            {
                return false;
            }

            isDirty = BuildSignature() != cleanSignature;
            return isDirty;
        }

        public string GetUnsavedWorkspaceDataName()
        {
            return "License";
        }

        public void ApplyWorkspaceLayout()
        {
            if (listPanel != null)
            {
                listPanel.Width = Math.Max(1, this.ClientSize.Width - 64);
                listPanel.Height = Math.Max(180, this.ClientSize.Height - 338);
            }

            int y = Math.Max(420, this.ClientSize.Height - 124);
            if (btnAdd != null)
            {
                btnAdd.Location = new Point(32, y);
            }
            if (btnSave != null)
            {
                btnSave.Location = new Point(Math.Max(32, this.ClientSize.Width - 278), y);
            }
            if (btnClose != null)
            {
                btnClose.Location = new Point(Math.Max(32, this.ClientSize.Width - 142), y);
            }
            if (lblStatus != null)
            {
                lblStatus.Width = Math.Max(1, this.ClientSize.Width - 64);
                lblStatus.Location = new Point(32, Math.Max(460, this.ClientSize.Height - 66));
            }

            int width = listPanel == null ? 0 : Math.Max(360, listPanel.ClientSize.Width - 24);
            int i;
            for (i = 0; i < entryPanels.Count; i++)
            {
                entryPanels[i].Width = width;
                entryPanels[i].ApplyInnerLayout();
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

    internal class OviaLicenseEntryPanel : Panel
    {
        private readonly bool canEdit;
        private TextBox txtTitle;
        private TextBox txtUrl;
        private TextBox txtContent;
        private Button btnDelete;

        public int EntryIndex { get; private set; }
        public event EventHandler EntryChanged;
        public event EventHandler DeleteRequested;

        public OviaLicenseEntryPanel(OviaLicenseEntry entry, int index, bool canEdit)
        {
            this.canEdit = canEdit;
            this.EntryIndex = index;
            BuildUI(entry == null ? new OviaLicenseEntry() : entry);
        }

        private void BuildUI(OviaLicenseEntry entry)
        {
            this.Height = 230;
            this.BackColor = Color.White;
            this.Paint += Panel_Paint;

            Label lblTitle = CreateLabel("라이선스명", 18, 18, 90);
            this.Controls.Add(lblTitle);
            txtTitle = CreateTextBox(116, 16, 360, entry.Title, false);
            this.Controls.Add(txtTitle);

            Label lblUrl = CreateLabel("링크주소", 500, 18, 80);
            this.Controls.Add(lblUrl);
            txtUrl = CreateTextBox(580, 16, 350, entry.Url, false);
            this.Controls.Add(txtUrl);

            btnDelete = new OVIA.Desktop.Controls.OviaButton();
            btnDelete.Text = "삭제";
            btnDelete.Size = OviaFluentTheme.MeasureButtonSize(btnDelete.Text);
            OviaFluentTheme.ApplyButton(btnDelete, OviaButtonRole.Danger);
            btnDelete.Enabled = canEdit;
            btnDelete.Click += delegate
            {
                if (DeleteRequested != null)
                {
                    DeleteRequested(this, EventArgs.Empty);
                }
            };
            this.Controls.Add(btnDelete);

            Label lblContent = CreateLabel("내용", 18, 62, 90);
            this.Controls.Add(lblContent);
            txtContent = CreateTextBox(116, 62, 930, entry.Content, true);
            this.Controls.Add(txtContent);

            txtTitle.TextChanged += TextChanged_Event;
            txtUrl.TextChanged += TextChanged_Event;
            txtContent.TextChanged += TextChanged_Event;
        }

        private Label CreateLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 24);
            label.Font = OviaFluentTheme.FontButton(8.7F, FontStyle.Bold);
            label.ForeColor = OviaFluentTheme.TextSecondary;
            label.BackColor = Color.White;
            return label;
        }

        private TextBox CreateTextBox(int x, int y, int width, string text, bool multiline)
        {
            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Size = new Size(width, multiline ? 142 : 24);
            box.Text = text == null ? "" : text;
            box.ReadOnly = !canEdit;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = OviaFluentTheme.FontInput(9F, FontStyle.Regular);
            box.Multiline = multiline;
            box.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
            box.AcceptsReturn = multiline;
            box.AcceptsTab = false;
            return box;
        }

        private void TextChanged_Event(object sender, EventArgs e)
        {
            if (EntryChanged != null)
            {
                EntryChanged(this, EventArgs.Empty);
            }
        }

        public void ApplyInnerLayout()
        {
            int contentWidth = Math.Max(220, this.Width - 156);
            if (txtContent != null)
            {
                txtContent.Width = contentWidth;
            }

            if (txtUrl != null)
            {
                int rightButtonWidth = canEdit ? 92 : 12;
                int urlWidth = Math.Max(180, this.Width - txtUrl.Left - rightButtonWidth);
                txtUrl.Width = urlWidth;
            }

            if (btnDelete != null)
            {
                btnDelete.Location = new Point(Math.Max(116, this.Width - 88), 14);
            }
        }

        public void FocusTitle()
        {
            if (txtTitle != null)
            {
                txtTitle.Focus();
                txtTitle.SelectAll();
            }
        }

        public OviaLicenseEntry ToEntry()
        {
            OviaLicenseEntry entry = new OviaLicenseEntry();
            entry.Title = txtTitle == null ? "" : txtTitle.Text.Trim();
            entry.Url = txtUrl == null ? "" : txtUrl.Text.Trim();
            entry.Content = txtContent == null ? "" : txtContent.Text;
            return entry;
        }

        private void Panel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(OviaFluentTheme.CardBorder, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }

    internal class OviaLicenseEntry
    {
        public string Title = "";
        public string Url = "";
        public string Content = "";
    }

    internal static class OviaOpenSourceLicenseStore
    {
        private const string LicenseFileName = "open_source_licenses.dat";

        public static List<OviaLicenseEntry> Load()
        {
            string path = GetLicenseFilePath();
            if (!File.Exists(path))
            {
                return GetDefaultEntries();
            }

            try
            {
                List<OviaLicenseEntry> result = new List<OviaLicenseEntry>();
                OviaLicenseEntry current = null;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int i;
                for (i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] == null ? "" : lines[i];
                    if (line.Trim().Equals("[License]", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current != null && current.Title.Trim() != "")
                        {
                            result.Add(current);
                        }
                        current = new OviaLicenseEntry();
                        continue;
                    }

                    if (current == null)
                    {
                        continue;
                    }

                    int index = line.IndexOf('=');
                    if (index <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, index).Trim();
                    string value = Decode(line.Substring(index + 1));
                    if (key.Equals("Title", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Title = value;
                    }
                    else if (key.Equals("Url", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Url = value;
                    }
                    else if (key.Equals("Content", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Content = value;
                    }
                }

                if (current != null && current.Title.Trim() != "")
                {
                    result.Add(current);
                }

                if (result.Count == 0)
                {
                    return GetDefaultEntries();
                }

                return result;
            }
            catch
            {
                return GetDefaultEntries();
            }
        }

        public static void Save(List<OviaLicenseEntry> entries)
        {
            if (entries == null)
            {
                entries = new List<OviaLicenseEntry>();
            }

            string folder = Path.GetDirectoryName(GetLicenseFilePath());
            if (folder != null && folder.Trim() != "")
            {
                Directory.CreateDirectory(folder);
            }

            List<string> lines = new List<string>();
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaLicenseEntry entry = entries[i];
                if (entry == null || entry.Title.Trim() == "")
                {
                    continue;
                }

                lines.Add("[License]");
                lines.Add("Title=" + Encode(entry.Title));
                lines.Add("Url=" + Encode(entry.Url));
                lines.Add("Content=" + Encode(entry.Content));
                lines.Add("");
            }

            File.WriteAllLines(GetLicenseFilePath(), lines.ToArray(), Encoding.UTF8);
        }

        public static string GetLicenseFilePath()
        {
            return Path.Combine(OviaSystemSettingsStore.GetSettingsFolder(), "System", LicenseFileName);
        }

        public static string BuildSignature(List<OviaLicenseEntry> entries)
        {
            if (entries == null)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaLicenseEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                sb.Append(entry.Title == null ? "" : entry.Title.Trim());
                sb.Append("\u001f");
                sb.Append(entry.Url == null ? "" : entry.Url.Trim());
                sb.Append("\u001f");
                sb.Append(entry.Content == null ? "" : entry.Content.Replace("\r\n", "\n"));
                sb.Append("\u001e");
            }

            return sb.ToString();
        }

        private static List<OviaLicenseEntry> GetDefaultEntries()
        {
            List<OviaLicenseEntry> result = new List<OviaLicenseEntry>();
            OviaLicenseEntry pretendard = new OviaLicenseEntry();
            pretendard.Title = "Pretendard";
            pretendard.Url = "https://github.com/orioncactus/pretendard";
            pretendard.Content = "Pretendard is distributed under the SIL Open Font License 1.1.\r\n\r\nOVIA bundles Pretendard font files locally for offline desktop use.\r\n\r\nThe full license notice is stored in OVIA.Desktop/Assets/Fonts/LICENSE.txt.";
            result.Add(pretendard);
            return result;
        }

        private static string Encode(string value)
        {
            if (value == null)
            {
                value = "";
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            try
            {
                if (value == null)
                {
                    return "";
                }

                byte[] bytes = Convert.FromBase64String(value.Trim());
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
