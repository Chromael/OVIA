using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    public class CadShapeRenderer
    {
        private const float Padding = 8F;

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected)
        {
            if (g == null || bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            Rectangle inner = new Rectangle(bounds.Left + 4, bounds.Top + 4, bounds.Width - 8, bounds.Height - 8);

            using (SolidBrush backBrush = new SolidBrush(selected ? Color.FromArgb(255, 250, 218) : Color.White))
            {
                g.FillRectangle(backBrush, inner);
            }

            if (jsonPath == null || jsonPath.Trim() == "" || !File.Exists(jsonPath))
            {
                DrawEmpty(g, inner, "CAD 형상 없음");
                return;
            }

            CadShapeData data = Load(jsonPath);

            if (data == null || data.Elements.Count == 0)
            {
                DrawEmpty(g, inner, "CAD 형상 없음");
                return;
            }

            DrawData(g, inner, data);
        }

        private void DrawData(Graphics g, Rectangle inner, CadShapeData data)
        {
            SmoothingMode oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                RectangleF drawArea = new RectangleF(inner.Left + Padding, inner.Top + Padding, inner.Width - Padding * 2, inner.Height - Padding * 2);

                if (drawArea.Width <= 1 || drawArea.Height <= 1)
                {
                    return;
                }

                /*
                 * CAD 형상은 BarList의 철근형상 셀 안에서 실제 형상 비율을 유지해야 합니다.
                 * 셀 폭이 넓다고 해서 직선 형상이 가로 끝까지 늘어나면 실제 도면보다 과장되어 보입니다.
                 * 대표님 확인 기준에 맞춰 실제 CAD 셀 안의 형상처럼 짧고 안정적인 비율로 보이도록
                 * 렌더링 영역의 최대 폭을 행 높이 기준으로 더 강하게 제한하고 가운데 정렬합니다.
                 */
                float maxShapeWidth = Math.Min(drawArea.Width, drawArea.Height * 2.35F);

                if (maxShapeWidth > 1 && maxShapeWidth < drawArea.Width)
                {
                    drawArea = new RectangleF(
                        drawArea.Left + (drawArea.Width - maxShapeWidth) / 2F,
                        drawArea.Top,
                        maxShapeWidth,
                        drawArea.Height
                    );
                }

                double maxX = Math.Max(data.Width, 1);
                double maxY = Math.Max(data.Height, 1);
                double scale = Math.Min(drawArea.Width / maxX, drawArea.Height / maxY);
                float offsetX = drawArea.Left + (float)((drawArea.Width - maxX * scale) / 2.0);
                float offsetY = drawArea.Top + (float)((drawArea.Height - maxY * scale) / 2.0);

                using (Pen pen = new Pen(Color.FromArgb(15, 20, 35), 1.35F))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(15, 20, 35)))
                using (Font font = new Font("맑은 고딕", 7.5F, FontStyle.Regular, GraphicsUnit.Point))
                {
                    int i;

                    for (i = 0; i < data.Elements.Count; i++)
                    {
                        CadShapeElement element = data.Elements[i];

                        if (element.Type == "LINE")
                        {
                            g.DrawLine(pen, X(element.X1, offsetX, scale), Y(element.Y1, offsetY, scale), X(element.X2, offsetX, scale), Y(element.Y2, offsetY, scale));
                        }
                        else if (element.Type == "CIRCLE")
                        {
                            float r = (float)(element.Radius * scale);
                            float cx = X(element.CX, offsetX, scale);
                            float cy = Y(element.CY, offsetY, scale);
                            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        }
                        else if (element.Type == "ARC")
                        {
                            float r = (float)(element.Radius * scale);
                            float cx = X(element.CX, offsetX, scale);
                            float cy = Y(element.CY, offsetY, scale);
                            RectangleF rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                            float start = (float)(-element.StartAngle);
                            float sweep = (float)(-(element.EndAngle - element.StartAngle));

                            if (Math.Abs(sweep) < 0.1F)
                            {
                                sweep = 360F;
                            }

                            g.DrawArc(pen, rect, start, sweep);
                        }
                        else if (element.Type == "TEXT")
                        {
                            string text = element.Text == null ? "" : element.Text.Trim();

                            if (text != "")
                            {
                                DrawReadableText(g, text, font, textBrush, element, data.Elements, drawArea, offsetX, offsetY, scale);
                            }
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
            }
        }


        private void DrawReadableText(Graphics g, string text, Font font, Brush brush, CadShapeElement textElement, List<CadShapeElement> elements, RectangleF drawArea, float offsetX, float offsetY, double scale)
        {
            if (text == null || text.Trim() == "")
            {
                return;
            }

            SizeF size = g.MeasureString(text, font);
            PointF center = new PointF(X(textElement.X1, offsetX, scale), Y(textElement.Y1, offsetY, scale));
            center = AdjustTextCenterAwayFromLines(center, size, elements, drawArea, offsetX, offsetY, scale);

            float x = center.X - size.Width / 2F;
            float y = center.Y - size.Height / 2F;
            g.DrawString(text, font, brush, x, y);
        }

        private PointF AdjustTextCenterAwayFromLines(PointF center, SizeF size, List<CadShapeElement> elements, RectangleF drawArea, float offsetX, float offsetY, double scale)
        {
            CadShapeElement nearest = null;
            double nearestDistance = Double.MaxValue;
            int i;

            if (elements == null)
            {
                return center;
            }

            for (i = 0; i < elements.Count; i++)
            {
                CadShapeElement line = elements[i];

                if (line == null || line.Type != "LINE")
                {
                    continue;
                }

                PointF p1 = new PointF(X(line.X1, offsetX, scale), Y(line.Y1, offsetY, scale));
                PointF p2 = new PointF(X(line.X2, offsetX, scale), Y(line.Y2, offsetY, scale));
                double distance = DistancePointToSegment(center, p1, p2);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = line;
                }
            }

            float threshold = Math.Max(5F, size.Height * 0.65F);

            if (nearest == null || nearestDistance > threshold)
            {
                return center;
            }

            PointF lp1 = new PointF(X(nearest.X1, offsetX, scale), Y(nearest.Y1, offsetY, scale));
            PointF lp2 = new PointF(X(nearest.X2, offsetX, scale), Y(nearest.Y2, offsetY, scale));
            float dx = lp2.X - lp1.X;
            float dy = lp2.Y - lp1.Y;
            PointF adjusted = center;

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                // 수평 계열 철근 위의 치수는 선과 겹치지 않도록 선 위쪽 중앙에 배치합니다.
                adjusted.Y = Math.Min(center.Y, (lp1.Y + lp2.Y) / 2F - size.Height * 0.75F - 2F);
            }
            else
            {
                // 수직 계열 철근의 치수는 선과 겹치지 않도록 선 바깥쪽 중앙에 배치합니다.
                float lineX = (lp1.X + lp2.X) / 2F;

                if (center.X <= lineX)
                {
                    adjusted.X = lineX - size.Width * 0.65F - 4F;
                }
                else
                {
                    adjusted.X = lineX + size.Width * 0.65F + 4F;
                }
            }

            adjusted.X = Clamp(adjusted.X, drawArea.Left + size.Width / 2F, drawArea.Right - size.Width / 2F);
            adjusted.Y = Clamp(adjusted.Y, drawArea.Top + size.Height / 2F, drawArea.Bottom - size.Height / 2F);
            return adjusted;
        }

        private double DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;

            if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001)
            {
                double sx = p.X - a.X;
                double sy = p.Y - a.Y;
                return Math.Sqrt(sx * sx + sy * sy);
            }

            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);

            if (t < 0) t = 0;
            if (t > 1) t = 1;

            double px = a.X + t * dx;
            double py = a.Y + t * dy;
            double ex = p.X - px;
            double ey = p.Y - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                return value;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private float X(double value, float offset, double scale)
        {
            return offset + (float)(value * scale);
        }

        private float Y(double value, float offset, double scale)
        {
            return offset + (float)(value * scale);
        }

        private void DrawEmpty(Graphics g, Rectangle inner, string text)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(130, 135, 145)))
            using (Font font = new Font("맑은 고딕", 8F, FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, inner.Left + (inner.Width - size.Width) / 2F, inner.Top + (inner.Height - size.Height) / 2F);
            }
        }

        private CadShapeData Load(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                CadShapeData data = new CadShapeData();
                data.Width = GetNumber(json, "width", 100);
                data.Height = GetNumber(json, "height", 60);

                MatchCollection matches = Regex.Matches(json, "\\{[^\\{\\}]*\\\"type\\\"[^\\{\\}]*\\}", RegexOptions.Singleline);
                int i;

                for (i = 0; i < matches.Count; i++)
                {
                    string item = matches[i].Value;
                    CadShapeElement element = new CadShapeElement();
                    element.Type = GetString(item, "type").ToUpperInvariant();
                    element.Text = GetString(item, "text");
                    element.X1 = GetNumber(item, "x1", GetNumber(item, "x", 0));
                    element.Y1 = GetNumber(item, "y1", GetNumber(item, "y", 0));
                    element.X2 = GetNumber(item, "x2", 0);
                    element.Y2 = GetNumber(item, "y2", 0);
                    element.CX = GetNumber(item, "cx", 0);
                    element.CY = GetNumber(item, "cy", 0);
                    element.Radius = GetNumber(item, "radius", 0);
                    element.StartAngle = GetNumber(item, "startAngle", 0);
                    element.EndAngle = GetNumber(item, "endAngle", 0);
                    data.Elements.Add(element);
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        private double GetNumber(string json, string key, double defaultValue)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.Singleline);

            if (!match.Success)
            {
                return defaultValue;
            }

            double value;

            if (double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return defaultValue;
        }

        private string GetString(string json, string key)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Singleline);

            if (!match.Success)
            {
                return "";
            }

            return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    internal class CadShapeData
    {
        public double Width = 100;
        public double Height = 60;
        public List<CadShapeElement> Elements = new List<CadShapeElement>();
    }

    internal class CadShapeElement
    {
        public string Type = "";
        public string Text = "";
        public double X1 = 0;
        public double Y1 = 0;
        public double X2 = 0;
        public double Y2 = 0;
        public double CX = 0;
        public double CY = 0;
        public double Radius = 0;
        public double StartAngle = 0;
        public double EndAngle = 0;
    }
}
