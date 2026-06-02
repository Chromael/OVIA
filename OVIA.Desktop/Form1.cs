using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public partial class Form1 : Form
    {
        private OviaTextInput	txtCompanyId;
        private OviaTextInput	txtUserId;
        private OviaTextInput	txtPassword;
        private CheckBox		chkSaveId;

        private readonly Color	BrandIndigo	= OviaFluentTheme.AccentHover;
        private readonly Color	BrandViolet	= OviaFluentTheme.Accent;
        private readonly Color	TextDark		= OviaFluentTheme.TextPrimary;
        private readonly Color	TextSub		= OviaFluentTheme.TextSecondary;
        private readonly Color	BorderSoft	= OviaFluentTheme.ControlBorder;
        private readonly Color	SurfaceColor	= OviaFluentTheme.AppBackground;

        private readonly string	SaveFilePath	= Path.Combine(Application.StartupPath, "ovia_login_save.txt");

        public Form1()
        {
            BuildOviaLoginUI();
            LoadSavedLoginInfo();
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

            GradientPanel bg		= new GradientPanel();
            bg.Dock					= DockStyle.Fill;
            bg.StartColor			= OviaFluentTheme.AppBackgroundAlt;
            bg.EndColor				= OviaFluentTheme.AppBackground;
            this.Controls.Add(bg);

            BuildBrandArea(bg);
            BuildLoginCard(bg);
            BuildFooter(bg);

            this.ResumeLayout(false);
        }

        private void BuildBrandArea(Control parent)
        {
            Panel brand				= new Panel();
            brand.Location			= new Point(0, 0);
            brand.Size				= new Size(500, 620);
            brand.BackColor			= SurfaceColor;
            parent.Controls.Add(brand);

            OviaLogoImage logo		= new OviaLogoImage();
            logo.Location			= new Point(68, 105);
            logo.Size				= new Size(370, 135);
            logo.SurfaceColor		= SurfaceColor;
            brand.Controls.Add(logo);

            Label slogan			= new Label();
            slogan.Text				= "Operation · Value · Intelligence · Automation";
            slogan.AutoSize			= true;
            slogan.Font				= OviaFluentTheme.FontBrand(11F, FontStyle.Regular);
            slogan.ForeColor		= TextDark;
            slogan.BackColor		= SurfaceColor;
            slogan.Location			= new Point(78, 260);
            brand.Controls.Add(slogan);

            Label desc				= new Label();
            desc.Text				= "엔지니어링과 데이터를 연결하여\r\n더 스마트한 의사결정과 효율적인 협업을 실현합니다.";
            desc.AutoSize			= false;
            desc.Size				= new Size(390, 60);
            desc.Font				= OviaFluentTheme.FontBrand(10F, FontStyle.Regular);
            desc.ForeColor			= TextSub;
            desc.BackColor			= SurfaceColor;
            desc.Location			= new Point(78, 305);
            brand.Controls.Add(desc);

            OviaCubeIllustration cube = new OviaCubeIllustration();
            cube.Location			= new Point(62, 390);
            cube.Size				= new Size(390, 210);
            cube.SurfaceColor		= SurfaceColor;
            brand.Controls.Add(cube);
        }

        private void BuildLoginCard(Control parent)
        {
            OviaCard card			= new OviaCard();
            card.Location			= new Point(520, 58);
            card.Size				= new Size(500, 585);
            card.Radius				= 8;
            card.SurfaceColor		= SurfaceColor;
            card.FillColor			= Color.White;
            card.BorderColor		= OviaFluentTheme.CardBorder;
            parent.Controls.Add(card);

            Label title			= new Label();
            title.Text				= "LOGIN";
            title.AutoSize			= true;
            title.Font				= OviaFluentTheme.FontTitle(19F, FontStyle.Bold);
            title.ForeColor			= TextDark;
            title.BackColor			= Color.White;
            title.Location			= new Point(55, 42);
            card.Controls.Add(title);

            Label sub				= new Label();
            sub.Text				= "계정정보를 입력하고 로그인하세요.";
            sub.AutoSize			= true;
            sub.Font				= OviaFluentTheme.FontBrand(10F, FontStyle.Regular);
            sub.ForeColor			= TextSub;
            sub.BackColor			= Color.White;
            sub.Location			= new Point(57, 86);
            card.Controls.Add(sub);

            txtCompanyId			= AddInput(card, "회사 아이디", "회사 아이디를 입력하세요", 55, 125, false);
            txtUserId				= AddInput(card, "사용자 아이디", "사용자 아이디를 입력하세요", 55, 215, false);
            txtPassword				= AddInput(card, "암호", "암호를 입력하세요", 55, 305, true);

            chkSaveId				= new CheckBox();
            chkSaveId.Text			= "아이디 저장";
            chkSaveId.AutoSize		= true;
            chkSaveId.Font			= OviaFluentTheme.FontInput(10F, FontStyle.Regular);
            chkSaveId.ForeColor		= TextDark;
            chkSaveId.BackColor		= Color.White;
            OviaFluentTheme.ApplyCheckBox(chkSaveId);
            chkSaveId.Location		= new Point(55, 390);
            card.Controls.Add(chkSaveId);

            OviaButton btnClose		= new OviaButton();
            btnClose.Text			= "종료";
            btnClose.Location		= new Point(55, 430);
            btnClose.Size			= new Size(185, 46);
            btnClose.IsPrimary		= false;
            btnClose.StartColor		= OviaFluentTheme.ControlBorder;
            btnClose.EndColor		= OviaFluentTheme.ControlBorder;
            btnClose.TextColor		= OviaFluentTheme.TextPrimary;
            btnClose.SurfaceColor	= Color.White;
            btnClose.Radius			= 6;
            btnClose.Font			= OviaFluentTheme.FontButton(11F, FontStyle.Bold);
            btnClose.Click			+= delegate { this.Close(); };
            card.Controls.Add(btnClose);

            OviaButton btnLogin		= new OviaButton();
            btnLogin.Text			= "로그인";
            btnLogin.Location		= new Point(260, 430);
            btnLogin.Size			= new Size(185, 46);
            btnLogin.IsPrimary		= true;
            btnLogin.StartColor		= OviaFluentTheme.Accent;
            btnLogin.EndColor		= OviaFluentTheme.Accent;
            btnLogin.TextColor		= Color.White;
            btnLogin.SurfaceColor	= Color.White;
            btnLogin.Radius			= 6;
            btnLogin.Font			= OviaFluentTheme.FontButton(12F, FontStyle.Bold);
            btnLogin.Click			+= BtnLogin_Click;
            card.Controls.Add(btnLogin);

            Panel line				= new Panel();
            line.Location			= new Point(55, 505);
            line.Size				= new Size(390, 1);
            line.BackColor			= OviaFluentTheme.CardBorder;
            card.Controls.Add(line);

            Label info				= new Label();
            info.Text				= "ⓘ  승인된 사용자만 로그인할 수 있습니다.";
            info.AutoSize			= true;
            info.Font				= OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            info.ForeColor			= TextSub;
            info.BackColor			= Color.White;
            info.Location			= new Point(58, 526);
            card.Controls.Add(info);
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
            input.Location			= new Point(x, y + 30);
            input.Size				= new Size(390, 48);
            input.Placeholder		= placeholder;
            input.IsPassword		= isPassword;
            input.BorderColor		= BorderSoft;
            input.FocusBorderColor	= OviaFluentTheme.Accent;
            input.TextColor			= TextDark;
            input.PlaceholderColor	= OviaFluentTheme.TextTertiary;
            input.SurfaceColor		= Color.White;
            input.Radius			= 2;
            parent.Controls.Add(input);

            return input;
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            string companyId		= txtCompanyId.Value.Trim();
            string userId			= txtUserId.Value.Trim();
            string password			= txtPassword.Value.Trim();

            if (companyId == "" || userId == "" || password == "")
            {
                MessageBox.Show(
                    "회사 아이디, 사용자 아이디, 암호를 모두 입력해주세요.",
                    "OVIA",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                return;
            }

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
                    txtPassword.Focus();
                    return;
                }

                this.Close();
            };

            this.Hide();
            mainForm.Show();
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
            version.Text			= "Version 1.0.0";
            version.AutoSize		= true;
            version.Font			= OviaFluentTheme.FontSystem(9F, FontStyle.Regular);
            version.ForeColor		= TextSub;
            version.BackColor		= SurfaceColor;
            version.Location		= new Point(900, 642);
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
        private bool focused;

        private const int EM_HIDEBALLOONTIP = 0x1504;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public string Placeholder = "";
        public bool IsPassword = false;
        public Color BorderColor = OviaFluentTheme.ControlBorder;
        public Color FocusBorderColor = OviaFluentTheme.Accent;
        public Color TextColor = OviaFluentTheme.TextPrimary;
        public Color PlaceholderColor = OviaFluentTheme.TextTertiary;
        public Color SurfaceColor = Color.White;
        public int Radius = 6;

        public string Value
        {
            get
            {
                if (innerTextBox.Text == Placeholder && innerTextBox.ForeColor.ToArgb() == PlaceholderColor.ToArgb())
                {
                    return "";
                }

                return innerTextBox.Text;
            }
            set
            {
                innerTextBox.Text = value;
                innerTextBox.ForeColor = TextColor;

                if (IsPassword)
                {
                    innerTextBox.UseSystemPasswordChar = true;
                }
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
            innerTextBox.Font = OviaFluentTheme.FontInput(10.5F, FontStyle.Regular);
            innerTextBox.Location = new Point(18, 14);
            innerTextBox.Width = 350;
            innerTextBox.BackColor = Color.White;
            innerTextBox.ForeColor = PlaceholderColor;

            innerTextBox.Enter += InnerTextBox_Enter;
            innerTextBox.Leave += InnerTextBox_Leave;
            innerTextBox.KeyDown += InnerTextBox_CapsLockStateChanged;
            innerTextBox.KeyUp += InnerTextBox_CapsLockStateChanged;

            this.Controls.Add(innerTextBox);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            ApplyPlaceholder();
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
            RefreshPasswordCapsLockBalloon();

            if (innerTextBox.Text == Placeholder && innerTextBox.ForeColor.ToArgb() == PlaceholderColor.ToArgb())
            {
                innerTextBox.Text = "";
                innerTextBox.ForeColor = TextColor;

                if (IsPassword)
                {
                    innerTextBox.UseSystemPasswordChar = true;
                }
            }

            this.Invalidate();
        }

        private void InnerTextBox_Leave(object sender, EventArgs e)
        {
            focused = false;
            HidePasswordCapsLockBalloon();

            if (innerTextBox.Text.Trim() == "")
            {
                ApplyPlaceholder();
            }

            this.Invalidate();
        }

        private void InnerTextBox_CapsLockStateChanged(object sender, KeyEventArgs e)
        {
            RefreshPasswordCapsLockBalloon();
        }

        private void RefreshPasswordCapsLockBalloon()
        {
            if (!IsPassword || innerTextBox == null || !innerTextBox.IsHandleCreated)
            {
                return;
            }

            if (!innerTextBox.Focused || !Control.IsKeyLocked(Keys.CapsLock))
            {
                HidePasswordCapsLockBalloon();

                try
                {
                    this.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (innerTextBox != null && innerTextBox.IsHandleCreated)
                        {
                            if (!innerTextBox.Focused || !Control.IsKeyLocked(Keys.CapsLock))
                            {
                                HidePasswordCapsLockBalloon();
                            }
                        }
                    }));
                }
                catch
                {
                }
            }
        }

        private void HidePasswordCapsLockBalloon()
        {
            if (innerTextBox == null || !innerTextBox.IsHandleCreated)
            {
                return;
            }

            try
            {
                SendMessage(innerTextBox.Handle, EM_HIDEBALLOONTIP, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
            }
        }

        private void ApplyPlaceholder()
        {
            if (innerTextBox == null)
            {
                return;
            }

            if (innerTextBox.Text.Trim() == "")
            {
                innerTextBox.UseSystemPasswordChar = false;
                innerTextBox.Text = Placeholder;
                innerTextBox.ForeColor = PlaceholderColor;
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
        public int Radius = 6;

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

            LoadLogo();
        }

        private void LoadLogo()
        {
            string logoPath = OviaLogoLoader.FindLogoPath();

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
