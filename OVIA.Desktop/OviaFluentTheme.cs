using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public enum OviaButtonRole
    {
        Primary,
        Neutral,
        Danger
    }

    public static class OviaFluentTheme
    {
        public static readonly Color AppBackground = Color.FromArgb(244, 246, 248);
        public static readonly Color AppBackgroundAlt = Color.FromArgb(250, 251, 253);
        public static readonly Color NavigationBackground = Color.FromArgb(248, 249, 251);
        public static readonly Color NavigationSelected = Color.FromArgb(234, 247, 255);
        public static readonly Color NavigationHover = Color.FromArgb(246, 247, 249);
        public static Color NavigationTextActive { get { return Accent; } }
        public static readonly Color NavigationText = Color.FromArgb(45, 55, 72);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(218, 225, 236);
        public static readonly Color CardShadow = Color.FromArgb(235, 239, 245);
        public static readonly Color ControlBorder = Color.FromArgb(209, 213, 219);
        public static Color ControlBorderFocus { get { return Accent; } }
        public static readonly Color CommonInputBackground = Color.FromArgb(251, 251, 251);
        public static readonly Color CommonInputBackgroundHover = Color.White;
        public static readonly Color CommonInputBorder = Color.FromArgb(228, 228, 228);
        public static readonly Color CommonInputBorderFocus = Color.FromArgb(204, 204, 204);
        public static readonly Color CommonInputPlaceholder = Color.FromArgb(88, 88, 88);
        public static readonly Color CommonInputIcon = Color.FromArgb(135, 135, 135);
        public static readonly Color CommonInputItemHover = Color.FromArgb(242, 242, 242);
        public static readonly Color HeaderBackground = Color.FromArgb(246, 248, 251);
        public static readonly Color GridLine = Color.FromArgb(229, 233, 240);
        public static readonly Color TextPrimary = Color.FromArgb(32, 36, 42);
        public static readonly Color TextSecondary = Color.FromArgb(55, 65, 81);
        public static readonly Color TextTertiary = Color.FromArgb(85, 96, 112);
        public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);
        public static Color Accent { get { return OviaSystemSettingsStore.GetBrandPrimaryColor(); } }
        public static Color AccentHover { get { return OviaSystemSettingsStore.GetBrandHoverColor(); } }
        public static Color AccentLight { get { return BlendWithWhite(Accent, 0.92F); } }
        public static Color AccentSoft { get { return BlendWithWhite(Accent, 0.96F); } }
        public static Color DashboardPrimary { get { return Accent; } }
        public static Color DashboardPrimaryDark { get { return AccentHover; } }
        public static Color DashboardPrimaryLight { get { return AccentLight; } }
        public static Color ChartBlue1 { get { return Accent; } }
        public static Color ChartBlue2 { get { return BlendWithWhite(Accent, 0.18F); } }
        public static Color ChartBlue3 { get { return BlendWithWhite(Accent, 0.35F); } }
        public static Color ChartBlue4 { get { return BlendWithWhite(Accent, 0.55F); } }
        public static readonly Color ChartNeutral = Color.FromArgb(203, 213, 225);
        public static Color Blue { get { return Accent; } }
        public static Color BlueLight { get { return AccentLight; } }
        public static Color NotificationBadgeBack { get { return Accent; } }
        public static Color CheckBoxCheckedBack { get { return Accent; } }
        public static Color CheckBoxCheckedBorder { get { return Accent; } }
        public static Color PagingActiveBack { get { return Accent; } }
        public static Color PagingActiveBorder { get { return Accent; } }
        public static Color PrimaryActionBack { get { return Accent; } }
        public static Color PrimaryActionHoverBack { get { return AccentHover; } }
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color SuccessLight = Color.FromArgb(236, 253, 245);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color WarningLight = Color.FromArgb(255, 247, 237);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerLight = Color.FromArgb(254, 242, 242);
        public static readonly Color Neutral = Color.FromArgb(156, 163, 175);
        public static readonly Color NeutralLight = Color.FromArgb(243, 244, 246);
        public static readonly Color NeutralButton = Color.FromArgb(248, 249, 251);
        public const int CardRadius = 0;
        public const int ButtonRadius = 4;
        public const int MenuRadius = 3;
        public const int PillRadius = 13;
        public const int CommonInputRadius = 5;
        public const int CommonInputHeight = 36;
        public const int ButtonHeight = 36;
        public const int ButtonMinWidth = 92;
        public const int ButtonHorizontalPadding = 36;
        public const float ButtonFontSize = 9.5F;
        public const int CheckBoxSize = 15;
        public const int CheckBoxRadius = 3;

        public static Color ButtonPrimaryBack { get { return Accent; } }
        public static Color ButtonPrimaryBackHover { get { return AccentHover; } }
        public static Color ButtonPrimaryBorder { get { return Accent; } }
        public static readonly Color ButtonPrimaryText = Color.White;

        public static readonly Color ButtonNeutralBack = Color.White;
        public static readonly Color ButtonNeutralBackHover = Color.FromArgb(248, 249, 251);
        public static readonly Color ButtonNeutralBorder = Color.FromArgb(209, 213, 219);
        public static readonly Color ButtonNeutralText = TextSecondary;

        public static readonly Color ButtonDangerBack = Color.White;
        public static readonly Color ButtonDangerBackHover = DangerLight;
        public static readonly Color ButtonDangerBorder = Color.FromArgb(239, 68, 68);
        public static readonly Color ButtonDangerText = Danger;


        public static Color BlendWithWhite(Color color, float whiteAmount)
        {
            if (whiteAmount < 0F)
            {
                whiteAmount = 0F;
            }
            else if (whiteAmount > 1F)
            {
                whiteAmount = 1F;
            }

            int r = (int)Math.Round(color.R + (255 - color.R) * whiteAmount);
            int g = (int)Math.Round(color.G + (255 - color.G) * whiteAmount);
            int b = (int)Math.Round(color.B + (255 - color.B) * whiteAmount);
            return Color.FromArgb(r, g, b);
        }


        public static Color[] ChartPalette()
        {
            return new Color[]
            {
                ChartBlue1,
                ChartBlue2,
                ChartBlue3,
                ChartBlue4,
                ChartNeutral
            };
        }

        public static Font FontBrand(float size, FontStyle style)
        {
            return OviaFontManager.CreateBrandFont(size, style);
        }

        public static Font FontTitle(float size, FontStyle style)
        {
            return OviaFontManager.CreateTitleFont(size, style);
        }

        public static Font FontButton(float size, FontStyle style)
        {
            return OviaFontManager.CreateButtonFont(size, style);
        }

        public static Font FontSystem(float size, FontStyle style)
        {
            return OviaFontManager.CreateSystemFont(size, style);
        }

        public static Font FontData(float size, FontStyle style)
        {
            return OviaFontManager.CreateDataFont(size, style);
        }

        public static Font FontInput(float size, FontStyle style)
        {
            return OviaFontManager.CreateInputFont(size, style);
        }

        public static Font FontStatus(float size, FontStyle style)
        {
            return OviaFontManager.CreateStatusFont(size, style);
        }

        public static Font FontUI(float size, FontStyle style)
        {
            return OviaFontManager.CreateUIFont(size, style);
        }

        // 기존 코드 호환용: 일반 한글/데이터/입력 계열은 Windows 시스템 폰트로 처리한다.
        public static Font FontKorean(float size, FontStyle style)
        {
            return OviaFontManager.CreateKoreanFont(size, style);
        }

        public static Font FontKorean(float size, FontStyle style, GraphicsUnit unit)
        {
            return OviaFontManager.CreateKoreanFont(size, style, unit);
        }

        public static string CurrentFontName()
        {
            return OviaFontManager.CurrentBrandFontName();
        }

        public static string CurrentSystemFontName()
        {
            return OviaFontManager.CurrentSystemFontName();
        }

        public static void ApplyForm(Form form)
        {
            if (form == null)
            {
                return;
            }

            form.Font = FontSystem(10F, FontStyle.Regular);
            form.BackColor = AppBackground;
        }

        public static void ApplyDataGrid(DataGridView grid)
        {
            if (grid == null)
            {
                return;
            }

            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = CardBackground;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = GridLine;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.Font = FontData(9.2F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBackground;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;

            grid.RowHeadersDefaultCellStyle.BackColor = HeaderBackground;
            grid.RowHeadersDefaultCellStyle.ForeColor = TextSecondary;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = AccentLight;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;

            grid.DefaultCellStyle.BackColor = CardBackground;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = AccentLight;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Font = FontData(9F, FontStyle.Regular);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TextPrimary;

            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 30);
            grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 32);
        }

        public static void ApplyTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            textBox.Font = FontInput(10F, FontStyle.Regular);
            textBox.BackColor = CardBackground;
            textBox.ForeColor = TextPrimary;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void ApplyComboBox(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return;
            }

            comboBox.Font = FontInput(10F, FontStyle.Regular);
            comboBox.BackColor = CardBackground;
            comboBox.ForeColor = TextPrimary;
            comboBox.FlatStyle = FlatStyle.Standard;
        }

        public static OviaButtonRole InferButtonRole(string text)
        {
            string safeText = text == null ? "" : text.Replace(" ", "").Trim();

            if (safeText.IndexOf("새로고침", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("Refresh", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("닫기", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("취소", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("복원", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return OviaButtonRole.Neutral;
            }

            if (safeText.IndexOf("삭제", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("제거", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("초기화", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return OviaButtonRole.Danger;
            }

            if (safeText.IndexOf("신규", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("새", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("저장", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("입력", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("등록", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("추가", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("생성", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("확인", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("적용", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("실행", StringComparison.OrdinalIgnoreCase) >= 0
                || safeText.IndexOf("열기", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return OviaButtonRole.Primary;
            }

            return OviaButtonRole.Neutral;
        }

        public static int MeasureButtonWidth(string text)
        {
            string safeText = text == null ? "" : text.Trim();
            Size textSize = TextRenderer.MeasureText(safeText, FontButton(ButtonFontSize, FontStyle.Bold));
            return Math.Max(ButtonMinWidth, textSize.Width + ButtonHorizontalPadding);
        }

        public static Size MeasureButtonSize(string text)
        {
            return new Size(MeasureButtonWidth(text), ButtonHeight);
        }

        public static void FitButtonSize(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Size = MeasureButtonSize(button.Text);
            button.MinimumSize = MeasureButtonSize(button.Text);
        }

        public static void ApplyButton(Button button, string text)
        {
            ApplyButton(button, InferButtonRole(text));
        }

        public static void ApplyButton(Button button, OviaButtonRole role)
        {
            if (button == null)
            {
                return;
            }

            button.Height = ButtonHeight;
            button.MinimumSize = new Size(ButtonMinWidth, ButtonHeight);
            button.Width = Math.Max(button.Width, MeasureButtonWidth(button.Text));
            button.Font = FontButton(ButtonFontSize, FontStyle.Bold);
            button.Cursor = Cursors.Hand;

            OVIA.Desktop.Controls.OviaButton oviaButton = button as OVIA.Desktop.Controls.OviaButton;
            if (oviaButton != null)
            {
                oviaButton.Role = role;
                oviaButton.FlatStyle = FlatStyle.Flat;
                oviaButton.UseVisualStyleBackColor = false;
                oviaButton.FlatAppearance.BorderSize = 0;
                oviaButton.BackColor = Color.Transparent;
                oviaButton.Invalidate();
                return;
            }

            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;

            if (role == OviaButtonRole.Primary)
            {
                button.BackColor = ButtonPrimaryBack;
                button.ForeColor = ButtonPrimaryText;
                button.FlatAppearance.BorderColor = ButtonPrimaryBorder;
                button.FlatAppearance.MouseOverBackColor = ButtonPrimaryBackHover;
                button.FlatAppearance.MouseDownBackColor = AccentHover;
            }
            else if (role == OviaButtonRole.Danger)
            {
                button.BackColor = ButtonDangerBack;
                button.ForeColor = ButtonDangerText;
                button.FlatAppearance.BorderColor = ButtonDangerBorder;
                button.FlatAppearance.MouseOverBackColor = ButtonDangerBackHover;
                button.FlatAppearance.MouseDownBackColor = DangerLight;
            }
            else
            {
                button.BackColor = ButtonNeutralBack;
                button.ForeColor = ButtonNeutralText;
                button.FlatAppearance.BorderColor = ButtonNeutralBorder;
                button.FlatAppearance.MouseOverBackColor = ButtonNeutralBackHover;
                button.FlatAppearance.MouseDownBackColor = NeutralLight;
            }

            button.FlatAppearance.BorderSize = 1;
            ApplyButtonRegion(button);
            button.SizeChanged -= CommonButton_SizeChanged;
            button.SizeChanged += CommonButton_SizeChanged;
        }

        private static void CommonButton_SizeChanged(object sender, EventArgs e)
        {
            ApplyButtonRegion(sender as Button);
        }

        private static void ApplyButtonRegion(Button button)
        {
            if (button == null || button.Width <= 0 || button.Height <= 0)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            GraphicsPath path = CreateRoundRectangle(rect, ButtonRadius);
            Region oldRegion = button.Region;
            button.Region = new Region(path);
            path.Dispose();

            if (oldRegion != null)
            {
                oldRegion.Dispose();
            }
        }

        private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }

            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter - 1, rectangle.Bottom - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyCheckBox(CheckBox checkBox)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.Font = FontInput(9.6F, FontStyle.Regular);
            checkBox.ForeColor = TextPrimary;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.UseVisualStyleBackColor = false;
            checkBox.BackColor = Color.Transparent;
        }
    }
}
