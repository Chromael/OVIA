using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public static class OviaFluentTheme
    {
        public static readonly Color AppBackground = Color.FromArgb(245, 247, 250);
        public static readonly Color AppBackgroundAlt = Color.FromArgb(250, 251, 253);
        public static readonly Color NavigationBackground = Color.FromArgb(248, 249, 251);
        public static readonly Color NavigationSelected = Color.FromArgb(232, 242, 255);
        public static readonly Color NavigationHover = Color.FromArgb(241, 246, 253);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(224, 228, 235);
        public static readonly Color ControlBorder = Color.FromArgb(209, 213, 219);
        public static readonly Color ControlBorderFocus = Color.FromArgb(0, 120, 212);
        public static readonly Color HeaderBackground = Color.FromArgb(243, 246, 250);
        public static readonly Color GridLine = Color.FromArgb(229, 233, 240);
        public static readonly Color TextPrimary = Color.FromArgb(31, 31, 31);
        public static readonly Color TextSecondary = Color.FromArgb(96, 96, 96);
        public static readonly Color TextTertiary = Color.FromArgb(118, 118, 118);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212);
        public static readonly Color AccentHover = Color.FromArgb(16, 110, 190);
        public static readonly Color AccentLight = Color.FromArgb(229, 241, 255);
        public static readonly Color Success = Color.FromArgb(16, 124, 16);
        public static readonly Color Warning = Color.FromArgb(202, 80, 16);
        public static readonly Color Danger = Color.FromArgb(196, 43, 28);
        public static readonly Color NeutralButton = Color.FromArgb(248, 249, 251);

        public static Font FontUI(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style);
        }

        public static Font FontKorean(float size, FontStyle style)
        {
            return new Font("맑은 고딕", size, style);
        }

        public static void ApplyForm(Form form)
        {
            if (form == null)
            {
                return;
            }

            form.Font = FontKorean(9F, FontStyle.Regular);
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
            grid.ColumnHeadersDefaultCellStyle.Font = FontKorean(9F, FontStyle.Bold);
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
            grid.DefaultCellStyle.Font = FontKorean(9F, FontStyle.Regular);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TextPrimary;
        }

        public static void ApplyTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            textBox.Font = FontKorean(9F, FontStyle.Regular);
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

            comboBox.Font = FontKorean(9F, FontStyle.Regular);
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

            checkBox.Font = FontKorean(9F, FontStyle.Regular);
            checkBox.ForeColor = TextPrimary;
            checkBox.FlatStyle = FlatStyle.Standard;
            checkBox.UseVisualStyleBackColor = true;
        }
    }
}
