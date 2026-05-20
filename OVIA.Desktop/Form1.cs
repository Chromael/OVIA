using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public partial class Form1 : Form
    {
        private OviaTextInput	txtCompanyId;
        private OviaTextInput	txtUserId;
        private OviaTextInput	txtPassword;
        private CheckBox		chkSaveId;

        private readonly Color	BrandIndigo	= Color.FromArgb(37, 30, 130);
        private readonly Color	BrandViolet	= Color.FromArgb(91, 49, 225);
        private readonly Color	TextDark		= Color.FromArgb(28, 33, 72);
        private readonly Color	TextSub		= Color.FromArgb(102, 111, 135);
        private readonly Color	BorderSoft	= Color.FromArgb(216, 223, 238);
        private readonly Color	SurfaceColor	= Color.FromArgb(244, 248, 255);

        private readonly string	SaveFilePath	= Path.Combine(Application.StartupPath, "ovia_login_save.txt");

        public Form1()
        {
            BuildOviaLoginUI();
            LoadSavedLoginInfo();
        }

        private void BuildOviaLoginUI()
        {
            this.SuspendLayout();
            this.Controls.Clear();

            this.Text				= "OVIA";
            this.Font				= new Font("맑은 고딕", 9F, FontStyle.Regular);
            this.StartPosition		= FormStartPosition.CenterScreen;
            this.FormBorderStyle	= FormBorderStyle.FixedSingle;
            this.MaximizeBox		= false;
            this.MinimizeBox		= true;
            this.ClientSize			= new Size(1080, 680);
            this.BackColor			= SurfaceColor;

            GradientPanel bg		= new GradientPanel();
            bg.Dock					= DockStyle.Fill;
            bg.StartColor			= Color.FromArgb(249, 251, 255);
            bg.EndColor				= Color.FromArgb(235, 242, 253);
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
            slogan.Font				= new Font("Segoe UI", 11F, FontStyle.Regular);
            slogan.ForeColor		= TextDark;
            slogan.BackColor		= SurfaceColor;
            slogan.Location			= new Point(78, 260);
            brand.Controls.Add(slogan);

            Label desc				= new Label();
            desc.Text				= "엔지니어링과 데이터를 연결하여\r\n더 스마트한 의사결정과 효율적인 협업을 실현합니다.";
            desc.AutoSize			= false;
            desc.Size				= new Size(390, 60);
            desc.Font				= new Font("맑은 고딕", 10F, FontStyle.Regular);
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
            card.Radius				= 22;
            card.SurfaceColor		= SurfaceColor;
            card.FillColor			= Color.White;
            card.BorderColor		= Color.FromArgb(236, 240, 248);
            parent.Controls.Add(card);

            Label title			= new Label();
            title.Text				= "OVIA에 오신 것을 환영합니다.";
            title.AutoSize			= true;
            title.Font				= new Font("맑은 고딕", 19F, FontStyle.Bold);
            title.ForeColor			= TextDark;
            title.BackColor			= Color.White;
            title.Location			= new Point(55, 42);
            card.Controls.Add(title);

            Label sub				= new Label();
            sub.Text				= "계정 정보를 입력하고 로그인하세요.";
            sub.AutoSize			= true;
            sub.Font				= new Font("맑은 고딕", 10F, FontStyle.Regular);
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
            chkSaveId.Font			= new Font("맑은 고딕", 10F, FontStyle.Regular);
            chkSaveId.ForeColor		= TextDark;
            chkSaveId.BackColor		= Color.White;
            chkSaveId.FlatStyle		= FlatStyle.Flat;
            chkSaveId.Location		= new Point(55, 390);
            card.Controls.Add(chkSaveId);

            OviaButton btnClose		= new OviaButton();
            btnClose.Text			= "종료";
            btnClose.Location		= new Point(55, 430);
            btnClose.Size			= new Size(185, 46);
            btnClose.IsPrimary		= false;
            btnClose.StartColor		= BrandViolet;
            btnClose.EndColor		= BrandIndigo;
            btnClose.TextColor		= BrandIndigo;
            btnClose.SurfaceColor	= Color.White;
            btnClose.Radius			= 6;
            btnClose.Font			= new Font("맑은 고딕", 11F, FontStyle.Bold);
            btnClose.Click			+= delegate { this.Close(); };
            card.Controls.Add(btnClose);

            OviaButton btnLogin		= new OviaButton();
            btnLogin.Text			= "로그인";
            btnLogin.Location		= new Point(260, 430);
            btnLogin.Size			= new Size(185, 46);
            btnLogin.IsPrimary		= true;
            btnLogin.StartColor		= BrandViolet;
            btnLogin.EndColor		= BrandIndigo;
            btnLogin.TextColor		= Color.White;
            btnLogin.SurfaceColor	= Color.White;
            btnLogin.Radius			= 6;
            btnLogin.Font			= new Font("맑은 고딕", 12F, FontStyle.Bold);
            btnLogin.Click			+= BtnLogin_Click;
            card.Controls.Add(btnLogin);

            Panel line				= new Panel();
            line.Location			= new Point(55, 505);
            line.Size				= new Size(390, 1);
            line.BackColor			= Color.FromArgb(225, 230, 242);
            card.Controls.Add(line);

            Label info				= new Label();
            info.Text				= "ⓘ  승인된 사용자만 로그인할 수 있습니다.";
            info.AutoSize			= true;
            info.Font				= new Font("맑은 고딕", 9F, FontStyle.Regular);
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
            label.Font				= new Font("맑은 고딕", 10F, FontStyle.Bold);
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
            input.FocusBorderColor	= BrandViolet;
            input.TextColor			= TextDark;
            input.PlaceholderColor	= Color.FromArgb(160, 166, 182);
            input.SurfaceColor		= Color.White;
            input.Radius			= 6;
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
            copyright.Font			= new Font("Segoe UI", 9F, FontStyle.Regular);
            copyright.LinkColor		= TextSub;
            copyright.ActiveLinkColor	= BrandViolet;
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
            version.Font			= new Font("Segoe UI", 9F, FontStyle.Regular);
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
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);
        public Color FillColor = Color.White;
        public Color BorderColor = Color.FromArgb(235, 239, 248);
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

            if (this.Width > 0 && this.Height > 0)
            {
                Rectangle rect = new Rectangle(0, 0, this.Width - 4, this.Height - 6);

                using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
                {
                    this.Region = new Region(path);
                }
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

            Rectangle rect = new Rectangle(0, 0, this.Width - 5, this.Height - 7);

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

        public string Placeholder = "";
        public bool IsPassword = false;
        public Color BorderColor = Color.FromArgb(216, 223, 238);
        public Color FocusBorderColor = Color.FromArgb(91, 49, 225);
        public Color TextColor = Color.FromArgb(28, 33, 72);
        public Color PlaceholderColor = Color.FromArgb(160, 166, 182);
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
            innerTextBox.Font = new Font("맑은 고딕", 10.5F, FontStyle.Regular);
            innerTextBox.Location = new Point(18, 14);
            innerTextBox.Width = 350;
            innerTextBox.BackColor = Color.White;
            innerTextBox.ForeColor = PlaceholderColor;

            innerTextBox.Enter += InnerTextBox_Enter;
            innerTextBox.Leave += InnerTextBox_Leave;

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

            if (this.Width > 0 && this.Height > 0)
            {
                Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);

                using (GraphicsPath path = OviaDrawHelper.RoundRect(rect, Radius))
                {
                    this.Region = new Region(path);
                }
            }

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

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
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

            if (innerTextBox.Text.Trim() == "")
            {
                ApplyPlaceholder();
            }

            this.Invalidate();
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
        public Color StartColor = Color.FromArgb(92, 48, 224);
        public Color EndColor = Color.FromArgb(42, 31, 145);
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
                    Color start = hover ? Color.FromArgb(105, 64, 236) : StartColor;
                    Color end = hover ? Color.FromArgb(50, 38, 150) : EndColor;

                    using (LinearGradientBrush brush = new LinearGradientBrush(rect, start, end, LinearGradientMode.Horizontal))
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

                    using (Pen pen = new Pen(StartColor, 1))
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

        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

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

            using (Font wordFont = new Font("Segoe UI", 40F, FontStyle.Bold))
            {
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(37, 30, 130)))
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

            using (LinearGradientBrush brush = new LinearGradientBrush(r, Color.FromArgb(87, 55, 235), Color.FromArgb(30, 24, 117), LinearGradientMode.Vertical))
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

            using (SolidBrush cyan = new SolidBrush(Color.FromArgb(0, 174, 239)))
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
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

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

            Rectangle baseRect = new Rectangle(45, 100, 275, 75);

            using (GraphicsPath basePath = OviaDrawHelper.RoundRect(baseRect, 26))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(225, 235, 252)))
                {
                    e.Graphics.FillPath(brush, basePath);
                }

                using (Pen pen = new Pen(Color.FromArgb(204, 217, 246), 1))
                {
                    e.Graphics.DrawPath(pen, basePath);
                }
            }

            Rectangle cube = new Rectangle(135, 38, 95, 95);

            using (LinearGradientBrush brush = new LinearGradientBrush(cube, Color.FromArgb(0, 174, 239), Color.FromArgb(92, 48, 224), 45F))
            {
                e.Graphics.FillRectangle(brush, cube);
            }

            using (Pen pen = new Pen(Color.FromArgb(255, 255, 255), 2))
            {
                e.Graphics.DrawRectangle(pen, cube);
                e.Graphics.DrawLine(pen, cube.Left, cube.Top + cube.Height / 2, cube.Right, cube.Top + cube.Height / 2);
                e.Graphics.DrawLine(pen, cube.Left + cube.Width / 2, cube.Top, cube.Left + cube.Width / 2, cube.Bottom);
            }

            using (SolidBrush smallBrush = new SolidBrush(Color.FromArgb(210, 232, 252)))
            {
                e.Graphics.FillRectangle(smallBrush, 35, 150, 45, 45);
                e.Graphics.FillRectangle(smallBrush, 290, 60, 58, 58);
                e.Graphics.FillRectangle(smallBrush, 95, 30, 40, 40);
            }

            using (Pen line = new Pen(Color.FromArgb(205, 218, 245), 1))
            {
                e.Graphics.DrawLine(line, 80, 170, 135, 86);
                e.Graphics.DrawLine(line, 230, 86, 290, 89);
                e.Graphics.DrawLine(line, 135, 86, 115, 50);
            }

            base.OnPaint(e);
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
