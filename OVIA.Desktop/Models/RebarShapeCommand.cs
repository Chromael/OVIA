using System;
using System.Drawing;

namespace OVIA.Desktop
{
    public class RebarShapeCommand
    {
        public string CommandType { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float X3 { get; set; }
        public float Y3 { get; set; }
        public float Radius { get; set; }
        public float StartAngle { get; set; }
        public float SweepAngle { get; set; }
        public string Text { get; set; }
        public bool IsRedText { get; set; }

        public static RebarShapeCommand Line(float x1, float y1, float x2, float y2)
        {
            return new RebarShapeCommand { CommandType = "LINE", X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
        }

        public static RebarShapeCommand TextLabel(float x, float y, string text, bool isRedText)
        {
            return new RebarShapeCommand { CommandType = "TEXT", X1 = x, Y1 = y, Text = text, IsRedText = isRedText };
        }

        public static RebarShapeCommand Circle(float cx, float cy, float radius)
        {
            return new RebarShapeCommand { CommandType = "CIRCLE", X1 = cx, Y1 = cy, Radius = radius };
        }

        public static RebarShapeCommand Arc(float cx, float cy, float radius, float startAngle, float sweepAngle)
        {
            return new RebarShapeCommand { CommandType = "ARC", X1 = cx, Y1 = cy, Radius = radius, StartAngle = startAngle, SweepAngle = sweepAngle };
        }
    }
}
