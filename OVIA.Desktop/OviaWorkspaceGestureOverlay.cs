using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    /// <summary>
    /// 우클릭 좌/우 드래그 중 방향을 알려주는 표시 전용 오버레이.
    /// 입력을 받지 않으며(WS_EX_TRANSPARENT), 활성 창을 빼앗지 않는다(WS_EX_NOACTIVATE).
    /// </summary>
    internal sealed class OviaWorkspaceGestureOverlay : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExNoActivate = 0x08000000;
        private const int Diameter = 142;

        private bool showBack;

        public OviaWorkspaceGestureOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = Diameter;
            Height = Diameter;
            BackColor = Color.FromArgb(45, 45, 48);
            Opacity = 0.68D;
            DoubleBuffered = true;

            GraphicsPath path = new GraphicsPath();
            path.AddEllipse(0, 0, Diameter - 1, Diameter - 1);
            Region = new Region(path);
            path.Dispose();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExTransparent | WsExNoActivate;
                return cp;
            }
        }

        public void ShowDirection(Form owner, bool back)
        {
            if (owner == null || owner.IsDisposed || !owner.Visible)
            {
                HideOverlay();
                return;
            }

            showBack = back;
            Rectangle client = owner.RectangleToScreen(owner.ClientRectangle);
            Location = new Point(
                client.Left + ((client.Width - Width) / 2),
                client.Top + ((client.Height - Height) / 2));

            if (!Visible)
            {
                Show(owner);
            }
            else
            {
                Invalidate();
            }
        }

        public void HideOverlay()
        {
            if (Visible)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen arrowPen = new Pen(Color.White, 5F))
            using (Font textFont = new Font("Malgun Gothic", 10F, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush textBrush = new SolidBrush(Color.White))
            {
                arrowPen.StartCap = LineCap.Round;
                arrowPen.EndCap = LineCap.Round;
                arrowPen.LineJoin = LineJoin.Round;

                int centerY = 58;
                if (showBack)
                {
                    e.Graphics.DrawLine(arrowPen, 86, centerY, 48, centerY);
                    e.Graphics.DrawLine(arrowPen, 48, centerY, 64, centerY - 16);
                    e.Graphics.DrawLine(arrowPen, 48, centerY, 64, centerY + 16);
                }
                else
                {
                    e.Graphics.DrawLine(arrowPen, 48, centerY, 86, centerY);
                    e.Graphics.DrawLine(arrowPen, 86, centerY, 70, centerY - 16);
                    e.Graphics.DrawLine(arrowPen, 86, centerY, 70, centerY + 16);
                }

                string text = showBack ? "이전 페이지" : "다음 페이지";
                SizeF textSize = e.Graphics.MeasureString(text, textFont);
                e.Graphics.DrawString(text, textFont, textBrush, (Width - textSize.Width) / 2F, 92F);
            }
        }
    }
}
