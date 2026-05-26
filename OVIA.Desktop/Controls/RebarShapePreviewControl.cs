using System;
using System.Drawing;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class RebarShapePreviewControl : UserControl
    {
        private readonly RebarShapeRenderer renderer;
        private readonly CadShapeRenderer cadRenderer;
        private RebarShapeInfo shape;
        private string rawText;
        private string dimensionText;

        public RebarShapePreviewControl()
        {
            renderer = new RebarShapeRenderer();
            cadRenderer = new CadShapeRenderer();
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

            if (IsCadImportedShape(shape))
            {
                cadRenderer.DrawCadShape(e.Graphics, ClientRectangle, shape.SourceImagePath, false, dimensionText);
                return;
            }

            renderer.DrawShape(e.Graphics, ClientRectangle, shape, rawText, false, dimensionText);
        }

        private bool IsCadImportedShape(RebarShapeInfo info)
        {
            return info != null
                && info.VectorStatus != null
                && info.VectorStatus.Equals("CAD_IMPORTED", StringComparison.OrdinalIgnoreCase)
                && info.SourceImagePath != null
                && info.SourceImagePath.Trim() != "";
        }
    }
}
