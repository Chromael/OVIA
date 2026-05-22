using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class RebarShapePreviewControl : UserControl
    {
        private readonly RebarShapeRenderer renderer;
        private RebarShapeInfo shape;
        private string rawText;
        private string dimensionText;

        public RebarShapePreviewControl()
        {
            renderer = new RebarShapeRenderer();
            rawText = "";
            dimensionText = "";
            DoubleBuffered = true;
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
        }

        public RebarShapeInfo Shape
        {
            get { return shape; }
            set
            {
                shape = value;
                Invalidate();
            }
        }

        public string RawText
        {
            get { return rawText; }
            set
            {
                rawText = value == null ? "" : value;
                Invalidate();
            }
        }

        public string DimensionText
        {
            get { return dimensionText; }
            set
            {
                dimensionText = value == null ? "" : value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            renderer.DrawShape(e.Graphics, ClientRectangle, shape, rawText, false, dimensionText);
        }
    }
}
