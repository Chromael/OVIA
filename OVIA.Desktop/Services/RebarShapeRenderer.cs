using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class RebarShapeRenderer
    {
        private const float ViewWidth = 180F;
        private const float ViewHeight = 90F;
        private const int ImageCacheLimit = 250;
        private static readonly Dictionary<string, Image> ImageCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> ImageCacheOrder = new List<string>();

        public void DrawShape(Graphics g, Rectangle bounds, RebarShapeInfo shape, string rawText, bool selected)
        {
            DrawShape(g, bounds, shape, rawText, selected, "");
        }

        public void DrawShape(Graphics g, Rectangle bounds, RebarShapeInfo shape, string rawText, bool selected, string dimensionText)
        {
            if (g == null)
            {
                return;
            }

            Dictionary<string, string> dimensionValues = ParseDimensionText(dimensionText);

            Color backColor = selected ? Color.FromArgb(255, 248, 205) : Color.White;
            Color borderColor = selected ? Color.FromArgb(226, 189, 67) : Color.FromArgb(225, 230, 240);

            using (SolidBrush backBrush = new SolidBrush(backColor))
            {
                g.FillRectangle(backBrush, bounds);
            }

            using (Pen borderPen = new Pen(borderColor, selected ? 2F : 1F))
            {
                Rectangle border = new Rectangle(bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
                g.DrawRectangle(borderPen, border);
            }

            Rectangle inner = Rectangle.Inflate(bounds, -5, -4);

            if (inner.Width <= 4 || inner.Height <= 4)
            {
                return;
            }

            if (shape == null)
            {
                DrawNoShape(g, inner, rawText);
                return;
            }

            bool hasCommands = shape.Commands != null && shape.Commands.Count > 0;

            if (hasCommands && IsCleanVectorTokenVerified(shape))
            {
                DrawCommandVector(g, inner, shape, dimensionValues);
                return;
            }

            bool preferSourceImage = ShouldPreferSourceImage(shape);

            if (preferSourceImage && DrawSourceImage(g, inner, shape))
            {
                return;
            }

            if (hasCommands)
            {
                DrawCommandVector(g, inner, shape, dimensionValues);
                return;
            }

            if (DrawSourceImage(g, inner, shape))
            {
                return;
            }

            DrawNoShape(g, inner, rawText);
        }


        private bool IsCleanVectorTokenVerified(RebarShapeInfo shape)
        {
            if (shape == null || shape.VectorStatus == null)
            {
                return false;
            }

            return shape.VectorStatus.IndexOf("CLEAN_VECTOR_TOKEN_VERIFIED", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ShouldPreferSourceImage(RebarShapeInfo shape)
        {
            if (shape == null)
            {
                return false;
            }

            if (IsCleanVectorTokenVerified(shape))
            {
                return false;
            }

            // 대표님 지시 기준:
            // PDF에서 가져온 형상은 실제 형상 기준 이미지가 우선입니다.
            // 임의 벡터나 임의 좌표 오버레이보다 PDF 원본 형상이 안전합니다.
            if (shape.VectorStatus != null && shape.VectorStatus.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (shape.SourceImagePath != null && shape.SourceImagePath.Trim() != "")
            {
                return true;
            }

            return false;
        }

        private bool DrawSourceImage(Graphics g, Rectangle bounds, RebarShapeInfo shape)
        {
            string path = GetResolvedImagePath(shape);

            if (path == "")
            {
                return false;
            }

            Image image = GetCachedImage(path);

            if (image == null)
            {
                return false;
            }

            InterpolationMode oldInterpolation = g.InterpolationMode;
            PixelOffsetMode oldPixelOffset = g.PixelOffsetMode;
            SmoothingMode oldSmoothing = g.SmoothingMode;

            try
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                Rectangle dest = GetImageDestination(bounds, image.Width, image.Height);
                g.DrawImage(image, dest);
                return true;
            }
            finally
            {
                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldPixelOffset;
                g.SmoothingMode = oldSmoothing;
            }
        }

        private string GetResolvedImagePath(RebarShapeInfo shape)
        {
            if (shape == null)
            {
                return "";
            }

            string path = shape.SourceImagePath;

            if (path == null || path.Trim() == "")
            {
                path = "Data/Shapes/source_jpg/shape_" + shape.ShapeNo.ToString("0000") + ".jpg";
            }

            return ResolveFilePath(path);
        }

        private string ResolveFilePath(string relativePath)
        {
            if (relativePath == null)
            {
                return "";
            }

            relativePath = relativePath.Trim();

            if (relativePath == "")
            {
                return "";
            }

            if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            {
                return relativePath;
            }

            string safeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDir, safeRelativePath);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo dir = new DirectoryInfo(baseDir);
            int i;

            for (i = 0; i < 8 && dir != null; i++)
            {
                candidate = Path.Combine(dir.FullName, safeRelativePath);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(dir.FullName, "OVIA.Desktop", safeRelativePath);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return "";
        }

        private Image GetCachedImage(string path)
        {
            if (path == null || path == "")
            {
                return null;
            }

            Image image;

            lock (ImageCache)
            {
                if (ImageCache.TryGetValue(path, out image))
                {
                    return image;
                }
            }

            try
            {
                using (Image loaded = Image.FromFile(path))
                {
                    image = new Bitmap(loaded);
                }
            }
            catch
            {
                return null;
            }

            lock (ImageCache)
            {
                if (!ImageCache.ContainsKey(path))
                {
                    ImageCache.Add(path, image);
                    ImageCacheOrder.Add(path);
                    TrimImageCache();
                }
                else
                {
                    image.Dispose();
                    image = ImageCache[path];
                }
            }

            return image;
        }

        private void TrimImageCache()
        {
            while (ImageCacheOrder.Count > ImageCacheLimit)
            {
                string key = ImageCacheOrder[0];
                ImageCacheOrder.RemoveAt(0);

                Image image;

                if (ImageCache.TryGetValue(key, out image))
                {
                    ImageCache.Remove(key);
                    image.Dispose();
                }
            }
        }

        private Rectangle GetImageDestination(Rectangle bounds, int imageWidth, int imageHeight)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return bounds;
            }

            float scale = Math.Min((float)bounds.Width / imageWidth, (float)bounds.Height / imageHeight);
            int width = Math.Max(1, (int)Math.Round(imageWidth * scale));
            int height = Math.Max(1, (int)Math.Round(imageHeight * scale));
            int left = bounds.Left + (bounds.Width - width) / 2;
            int top = bounds.Top + (bounds.Height - height) / 2;

            return new Rectangle(left, top, width, height);
        }

        private void DrawCommandVector(Graphics g, Rectangle inner, RebarShapeInfo shape, Dictionary<string, string> dimensionValues)
        {
            SmoothingMode oldMode = g.SmoothingMode;
            TextRenderingHintScope textScope = new TextRenderingHintScope(g);

            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                float scale = Math.Min(inner.Width / ViewWidth, inner.Height / ViewHeight);
                float drawWidth = ViewWidth * scale;
                float drawHeight = ViewHeight * scale;
                float offsetX = inner.Left + (inner.Width - drawWidth) / 2F;
                float offsetY = inner.Top + (inner.Height - drawHeight) / 2F;

                using (Pen linePen = new Pen(Color.FromArgb(38, 48, 64), 1.8F))
                using (Font font = new Font("맑은 고딕", Math.Max(7F, 8.5F * scale), FontStyle.Regular, GraphicsUnit.Point))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(42, 83, 130)))
                using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(190, 55, 65)))
                {
                    int i;

                    for (i = 0; i < shape.Commands.Count; i++)
                    {
                        RebarShapeCommand cmd = shape.Commands[i];

                        if (cmd == null || cmd.CommandType == null)
                        {
                            continue;
                        }

                        string type = cmd.CommandType.ToUpperInvariant();

                        if (type == "LINE")
                        {
                            g.DrawLine(linePen, X(cmd.X1, offsetX, scale), Y(cmd.Y1, offsetY, scale), X(cmd.X2, offsetX, scale), Y(cmd.Y2, offsetY, scale));
                        }
                        else if (type == "CIRCLE")
                        {
                            float r = cmd.Radius * scale;
                            g.DrawEllipse(linePen, X(cmd.X1, offsetX, scale) - r, Y(cmd.Y1, offsetY, scale) - r, r * 2F, r * 2F);
                        }
                        else if (type == "ARC")
                        {
                            float r = cmd.Radius * scale;
                            g.DrawArc(linePen, X(cmd.X1, offsetX, scale) - r, Y(cmd.Y1, offsetY, scale) - r, r * 2F, r * 2F, cmd.StartAngle, cmd.SweepAngle);
                        }
                        else if (type == "TEXT")
                        {
                            string text = cmd.Text == null ? "" : cmd.Text;
                            string dimensionKey = NormalizeDimensionKey(text);
                            bool isDimensionValue = false;

                            if (dimensionValues != null)
                            {
                                string valueText;

                                if (dimensionValues.TryGetValue(dimensionKey, out valueText) && valueText != "")
                                {
                                    text = valueText;
                                    isDimensionValue = true;
                                }
                            }

                            Brush brush = cmd.IsRedText ? redBrush : (isDimensionValue ? new SolidBrush(Color.FromArgb(20, 20, 20)) : textBrush);
                            SizeF size = g.MeasureString(text, font);
                            PointF center = new PointF(X(cmd.X1, offsetX, scale), Y(cmd.Y1, offsetY, scale));

                            // R1/R2/R3/R4 값은 반경 토큰의 위치 자체가 의미를 가지므로 자동 이동시키지 않습니다.
                            // 길이값(A~H)만 선과 너무 겹칠 때 선 바깥쪽으로 살짝 보정합니다.
                            if (isDimensionValue && !dimensionKey.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                            {
                                RectangleF drawArea = new RectangleF(offsetX, offsetY, drawWidth, drawHeight);
                                center = AdjustTextCenterAwayFromShapeLines(center, size, shape, drawArea, offsetX, offsetY, scale);
                            }

                            g.DrawString(text, font, brush, center.X - size.Width / 2F, center.Y - size.Height / 2F);

                            if (isDimensionValue && !cmd.IsRedText)
                            {
                                brush.Dispose();
                            }
                        }
                    }
                }
            }
            finally
            {
                textScope.Dispose();
                g.SmoothingMode = oldMode;
            }
        }



        private PointF AdjustTextCenterAwayFromShapeLines(PointF center, SizeF size, RebarShapeInfo shape, RectangleF drawArea, float offsetX, float offsetY, float scale)
        {
            if (shape == null || shape.Commands == null)
            {
                return center;
            }

            RebarShapeCommand nearest = null;
            double nearestDistance = Double.MaxValue;
            int i;

            for (i = 0; i < shape.Commands.Count; i++)
            {
                RebarShapeCommand line = shape.Commands[i];

                if (line == null || line.CommandType == null || !line.CommandType.Equals("LINE", StringComparison.OrdinalIgnoreCase))
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

            float threshold = Math.Max(5F, size.Height * 0.70F);

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
                adjusted.Y = Math.Min(center.Y, (lp1.Y + lp2.Y) / 2F - size.Height * 0.75F - 2F);
            }
            else
            {
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

        private void DrawCommandTextOverlay(Graphics g, Rectangle inner, RebarShapeInfo shape, Dictionary<string, string> dimensionValues)
        {
            // 이전 버전의 PDF 글자 흰색 덮어쓰기 방식은 사용하지 않습니다.
            // 이 메서드는 호환용으로만 남기며, 배경 사각형을 절대 그리지 않습니다.
            if (shape == null || shape.Commands == null || shape.Commands.Count == 0 || dimensionValues == null || dimensionValues.Count == 0)
            {
                return;
            }

            TextRenderingHintScope textScope = new TextRenderingHintScope(g);
            SmoothingMode oldMode = g.SmoothingMode;

            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                float scale = Math.Min(inner.Width / ViewWidth, inner.Height / ViewHeight);
                float drawWidth = ViewWidth * scale;
                float drawHeight = ViewHeight * scale;
                float offsetX = inner.Left + (inner.Width - drawWidth) / 2F;
                float offsetY = inner.Top + (inner.Height - drawHeight) / 2F;

                using (Font font = new Font("맑은 고딕", Math.Max(7F, 8.5F * scale), FontStyle.Bold, GraphicsUnit.Point))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                {
                    int i;

                    for (i = 0; i < shape.Commands.Count; i++)
                    {
                        RebarShapeCommand cmd = shape.Commands[i];

                        if (cmd == null || cmd.CommandType == null || !cmd.CommandType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string key = NormalizeDimensionKey(cmd.Text);
                        string valueText;

                        if (!dimensionValues.TryGetValue(key, out valueText) || valueText == null || valueText.Trim() == "")
                        {
                            continue;
                        }

                        valueText = valueText.Trim();
                        SizeF size = g.MeasureString(valueText, font);
                        float x = X(cmd.X1, offsetX, scale) - size.Width / 2F;
                        float y = Y(cmd.Y1, offsetY, scale) - size.Height / 2F;
                        g.DrawString(valueText, font, textBrush, x, y);
                    }
                }
            }
            finally
            {
                textScope.Dispose();
                g.SmoothingMode = oldMode;
            }
        }

        private void DrawDimensionSummary(Graphics g, Rectangle bounds, Dictionary<string, string> dimensionValues)
        {
            if (dimensionValues == null || dimensionValues.Count == 0)
            {
                return;
            }

            string text = BuildDimensionSummaryText(dimensionValues);

            if (text == "")
            {
                return;
            }

            Rectangle box = new Rectangle(bounds.Left + 2, bounds.Bottom - 18, bounds.Width - 4, 17);

            if (box.Width < 10 || box.Height < 8)
            {
                return;
            }

            using (SolidBrush back = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (Pen border = new Pen(Color.FromArgb(220, 220, 220)))
            using (Font font = new Font("맑은 고딕", 7F, FontStyle.Regular))
            {
                g.FillRectangle(back, box);
                g.DrawRectangle(border, box.Left, box.Top, box.Width - 1, box.Height - 1);
                TextRenderer.DrawText(g, text, font, box, Color.FromArgb(30, 30, 30), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private string BuildDimensionSummaryText(Dictionary<string, string> dimensionValues)
        {
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            List<string> parts = new List<string>();
            int i;

            for (i = 0; i < keys.Length; i++)
            {
                string value;

                if (dimensionValues.TryGetValue(keys[i], out value) && value != "")
                {
                    parts.Add(keys[i] + "=" + value);
                }
            }

            return String.Join("  ", parts.ToArray());
        }

        private Dictionary<string, string> ParseDimensionText(string dimensionText)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (dimensionText == null)
            {
                return result;
            }

            string text = dimensionText.Trim();

            if (text == "")
            {
                return result;
            }

            text = text.Replace("\r", ";").Replace("\n", ";").Replace(",", ";");
            string[] parts = text.Split(';');
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? "" : parts[i].Trim();

                if (part == "")
                {
                    continue;
                }

                int pos = part.IndexOf('=');

                if (pos < 0)
                {
                    pos = part.IndexOf(':');
                }

                if (pos <= 0)
                {
                    continue;
                }

                string key = NormalizeDimensionKey(part.Substring(0, pos));
                string value = part.Substring(pos + 1).Trim();

                if (key == "" || value == "")
                {
                    continue;
                }

                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
                else
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private string NormalizeDimensionKey(string key)
        {
            if (key == null)
            {
                return "";
            }

            key = key.Trim().ToUpperInvariant();
            key = key.Replace(" ", "");
            key = key.Replace("값", "");

            if (key == "R")
            {
                return "R1";
            }

            return key;
        }

        private void DrawNoShape(Graphics g, Rectangle bounds, string rawText)
        {
            string message = "형상 선택 필요";

            if (rawText != null && rawText.Trim() != "")
            {
                message = "미등록: " + rawText.Trim();
            }

            using (Font font = new Font("맑은 고딕", 8.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, message, font, bounds, Color.FromArgb(135, 142, 158), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private float X(float x, float offsetX, float scale)
        {
            return offsetX + x * scale;
        }

        private float Y(float y, float offsetY, float scale)
        {
            return offsetY + y * scale;
        }

        private class TextRenderingHintScope : IDisposable
        {
            private readonly Graphics graphics;
            private readonly System.Drawing.Text.TextRenderingHint oldHint;

            public TextRenderingHintScope(Graphics graphics)
            {
                this.graphics = graphics;
                this.oldHint = graphics.TextRenderingHint;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }

            public void Dispose()
            {
                graphics.TextRenderingHint = oldHint;
            }
        }
    }
}
