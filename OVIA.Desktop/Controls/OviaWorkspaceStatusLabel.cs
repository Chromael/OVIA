using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop.Controls
{
    public class OviaWorkspaceStatusLabel : Label
    {
        public OviaWorkspaceStatusLabel()
        {
            this.AutoSize = true;
            this.Font = OviaFluentTheme.FontStatus(8.7F, FontStyle.Regular);
            this.ForeColor = OviaFluentTheme.TextSecondary;
            this.BackColor = OviaFluentTheme.AppBackground;
            this.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        }

        public static OviaWorkspaceStatusLabel Create(Control parent, string text, int x, int y)
        {
            OviaWorkspaceStatusLabel label = new OviaWorkspaceStatusLabel();
            label.Text = text == null ? string.Empty : text;
            label.Location = new Point(x, y);

            if (parent != null)
            {
                parent.Controls.Add(label);
            }

            return label;
        }
    }
}
