using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public static class OviaFluentTheme
    {
        public static readonly Color AppBackground = Color.FromArgb(244, 246, 248);
        public static readonly Color AppBackgroundAlt = Color.FromArgb(250, 251, 253);
        public static readonly Color NavigationBackground = Color.FromArgb(248, 249, 251);
        public static readonly Color NavigationSelected = Color.FromArgb(234, 247, 255);
        public static readonly Color NavigationHover = Color.FromArgb(246, 247, 249);
        public static readonly Color NavigationTextActive = Color.FromArgb(37, 99, 235);
        public static readonly Color NavigationText = Color.FromArgb(45, 55, 72);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(218, 225, 236);
        public static readonly Color CardShadow = Color.FromArgb(235, 239, 245);
        public static readonly Color ControlBorder = Color.FromArgb(209, 213, 219);
        public static readonly Color ControlBorderFocus = Color.FromArgb(91, 95, 239);
        public static readonly Color HeaderBackground = Color.FromArgb(246, 248, 251);
        public static readonly Color GridLine = Color.FromArgb(229, 233, 240);
        public static readonly Color TextPrimary = Color.FromArgb(32, 36, 42);
        public static readonly Color TextSecondary = Color.FromArgb(55, 65, 81);
        public static readonly Color TextTertiary = Color.FromArgb(85, 96, 112);
        public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentLight = Color.FromArgb(239, 246, 255);
        public static readonly Color AccentSoft = Color.FromArgb(248, 250, 255);
        public static readonly Color DashboardPrimary = Color.FromArgb(37, 99, 235);
        public static readonly Color DashboardPrimaryDark = Color.FromArgb(30, 64, 175);
        public static readonly Color DashboardPrimaryLight = Color.FromArgb(239, 246, 255);
        public static readonly Color ChartBlue1 = Color.FromArgb(37, 99, 235);
        public static readonly Color ChartBlue2 = Color.FromArgb(59, 130, 246);
        public static readonly Color ChartBlue3 = Color.FromArgb(96, 165, 250);
        public static readonly Color ChartBlue4 = Color.FromArgb(147, 197, 253);
        public static readonly Color ChartNeutral = Color.FromArgb(203, 213, 225);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color BlueLight = Color.FromArgb(239, 246, 255);
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
        public const int ButtonRadius = 8;
        public const int MenuRadius = 3;
        public const int PillRadius = 13;


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

        public static void ApplyCheckBox(CheckBox checkBox)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.Font = FontInput(10F, FontStyle.Regular);
            checkBox.ForeColor = TextPrimary;
            checkBox.FlatStyle = FlatStyle.Standard;
            checkBox.UseVisualStyleBackColor = true;
        }
    }
}
