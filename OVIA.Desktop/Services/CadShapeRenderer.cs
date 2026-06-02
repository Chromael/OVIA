using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    public class CadShapeRenderer
    {
        private const float Padding = 1F;

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected)
        {
            DrawCadShape(g, bounds, jsonPath, selected, "");
        }

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected, string dimensionText)
        {
            if (g == null || bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            Rectangle inner = new Rectangle(bounds.Left + 1, bounds.Top + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));

            // 셀 배경/테두리는 FrmBarList의 공통 그리드 페인터에서만 처리합니다.
            // CAD 형상 렌더러는 형상 자체만 그려야 셀 라인 두께가 일정하게 유지됩니다.

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

            DrawData(g, inner, data, dimensionText);
        }

        private void DrawData(Graphics g, Rectangle inner, CadShapeData data, string dimensionText)
        {
            SmoothingMode oldSmoothing = g.SmoothingMode;
            TextRenderingHint oldTextRenderingHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            try
            {
                RectangleF drawArea = new RectangleF(inner.Left + Padding, inner.Top + Padding, Math.Max(1F, inner.Width - Padding * 2), Math.Max(1F, inner.Height - Padding * 2));

                if (drawArea.Width <= 1 || drawArea.Height <= 1)
                {
                    return;
                }

                /*
                 * CAD에서 가져온 철근형상은 실제 형상과 치수값을 사람이 읽을 수 있어야 합니다.
                 * 기존 방식은 JSON의 전체 셀 폭/높이를 기준으로 축소해서 그려, 복잡한 형상이 너무 작아졌습니다.
                 * 여기서는 실제 객체가 존재하는 범위만 다시 계산해서 표시 영역에 맞춥니다.
                 */
                double contentMinX;
                double contentMinY;
                double contentMaxX;
                double contentMaxY;

                if (!GetElementBounds(data, out contentMinX, out contentMinY, out contentMaxX, out contentMaxY))
                {
                    contentMinX = 0;
                    contentMinY = 0;
                    contentMaxX = Math.Max(data.Width, 1);
                    contentMaxY = Math.Max(data.Height, 1);
                }

                double contentWidth = Math.Max(contentMaxX - contentMinX, 1);
                double contentHeight = Math.Max(contentMaxY - contentMinY, 1);

                int textCount = CountElements(data, "TEXT");
                int geometryCount = CountGeometryElements(data);

                // 복잡한 형상은 너무 좁게 제한하지 않고, 단순 직선은 과도하게 길어지지 않도록 제한합니다.
                float widthLimitRatio = textCount >= 4 || geometryCount >= 4 ? 5.20F : 3.20F;
                float maxShapeWidth = Math.Min(drawArea.Width, drawArea.Height * widthLimitRatio);

                if (maxShapeWidth > 1 && maxShapeWidth < drawArea.Width)
                {
                    drawArea = new RectangleF(
                        drawArea.Left + (drawArea.Width - maxShapeWidth) / 2F,
                        drawArea.Top,
                        maxShapeWidth,
                        drawArea.Height
                    );
                }

                double scale = Math.Min(drawArea.Width / contentWidth, drawArea.Height / contentHeight);
                float offsetX = drawArea.Left + (float)((drawArea.Width - contentWidth * scale) / 2.0) - (float)(contentMinX * scale);
                float offsetY = drawArea.Top + (float)((drawArea.Height - contentHeight * scale) / 2.0) - (float)(contentMinY * scale);

                float penWidth = Math.Max(1.1F, Math.Min(2.8F, drawArea.Height / 34F));
                float defaultFontPx = Math.Max(8.0F, Math.Min(20F, drawArea.Height / 5.6F));
                List<string> overrideValues = BuildOverrideTextList(dimensionText);
                int overrideTextIndex = 0;

                using (Pen pen = new Pen(Color.FromArgb(15, 20, 35), penWidth))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(15, 20, 35)))
                {
                    int i;

                    // 선/곡선 먼저 렌더링
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
                    }

                    // 치수값은 별도 레이어처럼 나중에 렌더링
                    for (i = 0; i < data.Elements.Count; i++)
                    {
                        CadShapeElement element = data.Elements[i];

                        if (element.Type == "TEXT")
                        {
                            string text = element.Text == null ? "" : element.Text.Trim();

                            if (overrideValues.Count > overrideTextIndex)
                            {
                                string replacement = overrideValues[overrideTextIndex] == null ? "" : overrideValues[overrideTextIndex].Trim();

                                if (replacement != "")
                                {
                                    text = replacement;
                                }
                            }

                            overrideTextIndex++;

                            if (text != "")
                            {
                                float textFontPx = GetElementFontPixelSize(element, scale, defaultFontPx);

                                using (Font font = OviaFluentTheme.FontKorean(textFontPx, FontStyle.Regular, GraphicsUnit.Pixel))
                                {
                                    DrawReadableText(g, text, font, textBrush, element, data.Elements, drawArea, offsetX, offsetY, scale);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
                g.TextRenderingHint = oldTextRenderingHint;
            }
        }


        private List<string> BuildOverrideTextList(string dimensionText)
        {
            List<string> result = new List<string>();

            if (dimensionText == null || dimensionText.Trim() == "")
            {
                return result;
            }

            Dictionary<string, string> values = ParseDimensionValues(dimensionText);
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < keys.Length; i++)
            {
                string value;

                if (values.TryGetValue(keys[i], out value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private Dictionary<string, string> ParseDimensionValues(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (text == null)
            {
                return values;
            }

            string[] parts = text.Split(new char[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim();

                if (part == "")
                {
                    continue;
                }

                int eq = part.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string value = part.Substring(eq + 1).Trim();

                if (key != "" && value != "")
                {
                    values[key] = value;
                }
            }

            return values;
        }


        private bool GetElementBounds(CadShapeData data, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = Double.MaxValue;
            minY = Double.MaxValue;
            maxX = Double.MinValue;
            maxY = Double.MinValue;

            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return false;
            }

            int i;
            bool found = false;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "LINE")
                {
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1, element.Y1);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X2, element.Y2);
                    found = true;
                }
                else if (element.Type == "CIRCLE" || element.Type == "ARC")
                {
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX - element.Radius, element.CY - element.Radius);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX + element.Radius, element.CY + element.Radius);
                    found = true;
                }
                else if (element.Type == "TEXT")
                {
                    double margin = Math.Max(element.Height, Math.Max(data.Width, data.Height) * 0.035);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 - margin, element.Y1 - margin);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 + margin, element.Y1 + margin);
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            if (maxX <= minX)
            {
                maxX = minX + 1;
            }

            if (maxY <= minY)
            {
                maxY = minY + 1;
            }

            return true;
        }

        private void IncludePoint(ref double minX, ref double minY, ref double maxX, ref double maxY, double x, double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private int CountGeometryElements(CadShapeData data)
        {
            if (data == null || data.Elements == null)
            {
                return 0;
            }

            int count = 0;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null || element.Type == null)
                {
                    continue;
                }

                if (element.Type == "LINE" || element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    count++;
                }
            }

            return count;
        }

        private float GetElementFontPixelSize(CadShapeElement element, double scale, float defaultFontPx)
        {
            float size = defaultFontPx;

            if (element != null && element.Height > 0.0001)
            {
                size = (float)(element.Height * scale * 0.92);
            }

            if (size < 7F)
            {
                size = 7F;
            }

            if (size > 18F)
            {
                size = 18F;
            }

            return size;
        }

        private int CountElements(CadShapeData data, string type)
        {
            if (data == null || data.Elements == null || type == null)
            {
                return 0;
            }

            int count = 0;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element != null && element.Type != null && element.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawReadableText(Graphics g, string text, Font font, Brush brush, CadShapeElement textElement, List<CadShapeElement> elements, RectangleF drawArea, float offsetX, float offsetY, double scale)
        {
            if (text == null || text.Trim() == "")
            {
                return;
            }

            SizeF rawSize = g.MeasureString(text, font);
            PointF center = new PointF(X(textElement.X1, offsetX, scale), Y(textElement.Y1, offsetY, scale));
            float rotation = NormalizeRotation((float)textElement.Rotation);
            SizeF visualSize = GetVisualTextSize(rawSize, rotation);

            /*
             * CAD 원본 텍스트는 선 가까이에 붙어 있는 경우가 많습니다.
             * OVIA 셀 안에서 축소 렌더링하면 선과 숫자가 겹쳐 보이므로,
             * 회전 텍스트까지 포함해 가장 가까운 선에서 일정 간격만큼 떨어뜨립니다.
             */
            center = AdjustTextCenterAwayFromLines(center, visualSize, elements, drawArea, offsetX, offsetY, scale);

            DrawTextAtCenter(g, text, font, brush, center, rotation);
        }

        private SizeF GetVisualTextSize(SizeF rawSize, float rotation)
        {
            float abs = Math.Abs(rotation);

            if (Math.Abs(abs - 90F) < 12F)
            {
                return new SizeF(rawSize.Height, rawSize.Width);
            }

            return rawSize;
        }

        private void DrawTextAtCenter(Graphics g, string text, Font font, Brush brush, PointF center, float rotation)
        {
            GraphicsState state = g.Save();

            try
            {
                SizeF size = g.MeasureString(text, font);
                g.TranslateTransform(center.X, center.Y);

                if (Math.Abs(rotation) >= 8F)
                {
                    // CAD 좌표계와 화면 좌표계는 Y축 방향이 반대이므로 회전 방향을 반전합니다.
                    g.RotateTransform(-rotation);
                }

                g.DrawString(text, font, brush, -size.Width / 2F, -size.Height / 2F);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private float NormalizeRotation(float value)
        {
            while (value > 180F) value -= 360F;
            while (value < -180F) value += 360F;

            // 0도, 90도, -90도에 가까운 치수값은 보기 좋게 스냅합니다.
            if (Math.Abs(value) < 6F) return 0F;
            if (Math.Abs(value - 90F) < 6F) return 90F;
            if (Math.Abs(value + 90F) < 6F) return -90F;
            if (Math.Abs(Math.Abs(value) - 180F) < 6F) return 180F;

            return value;
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

            float threshold = Math.Max(6F, Math.Max(size.Width, size.Height) * 0.78F);

            if (nearest == null || nearestDistance > threshold)
            {
                return ClampPointToArea(center, size, drawArea);
            }

            PointF lp1 = new PointF(X(nearest.X1, offsetX, scale), Y(nearest.Y1, offsetY, scale));
            PointF lp2 = new PointF(X(nearest.X2, offsetX, scale), Y(nearest.Y2, offsetY, scale));
            float dx = lp2.X - lp1.X;
            float dy = lp2.Y - lp1.Y;
            PointF adjusted = center;
            float gap = Math.Max(4F, Math.Min(size.Width, size.Height) * 0.35F);

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                float lineY = (lp1.Y + lp2.Y) / 2F;

                if (center.Y <= lineY)
                {
                    adjusted.Y = lineY - size.Height * 0.55F - gap;
                }
                else
                {
                    adjusted.Y = lineY + size.Height * 0.55F + gap;
                }
            }
            else
            {
                float lineX = (lp1.X + lp2.X) / 2F;

                if (center.X <= lineX)
                {
                    adjusted.X = lineX - size.Width * 0.55F - gap;
                }
                else
                {
                    adjusted.X = lineX + size.Width * 0.55F + gap;
                }
            }

            return ClampPointToArea(adjusted, size, drawArea);
        }

        private PointF ClampPointToArea(PointF center, SizeF size, RectangleF drawArea)
        {
            center.X = Clamp(center.X, drawArea.Left + size.Width / 2F, drawArea.Right - size.Width / 2F);
            center.Y = Clamp(center.Y, drawArea.Top + size.Height / 2F, drawArea.Bottom - size.Height / 2F);
            return center;
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
            using (Font font = OviaFluentTheme.FontKorean(8F, FontStyle.Regular, GraphicsUnit.Point))
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
                    element.Height = GetNumber(item, "height", 0);
                    element.Rotation = GetNumber(item, "rotation", 0);
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
        public double Height = 0;
        public double Rotation = 0;
    }
}
