using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class CadShapeRenderer
    {
        private const float Padding = 0F;
        private const float CadTextFontSizePt = 8F;
        private const float CadRotatedTextFontSizePt = 9.5F;
        private const float RotatedTextTrackingPixels = 0.70F;
        private const float VisualScale = 0.90F;
        private const float StraightShapeMaxWidthRatio = 0.60F;
        private const float MinimumCadTextFontSizePt = 3.0F;
        private const float TextBoundsFitTolerance = 1.04F;
        private const string SourceCellLayoutPolicy = "SOURCE_CELL";

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected)
        {
            DrawCadShape(g, bounds, jsonPath, selected, "", false, 1F);
        }

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected, string dimensionText)
        {
            // 형상 수정창의 실시간 미리보기는 전달된 텍스트 값을 즉시 반영합니다.
            DrawCadShape(g, bounds, jsonPath, selected, dimensionText, true, 1F);
        }

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected, string dimensionText, bool applyTextOverrides)
        {
            DrawCadShape(g, bounds, jsonPath, selected, dimensionText, applyTextOverrides, 1F);
        }

        public void DrawCadShape(Graphics g, Rectangle bounds, string jsonPath, bool selected, string dimensionText, bool applyTextOverrides, float viewZoomScale)
        {
            if (g == null || bounds.Width <= 2 || bounds.Height <= 2)
            {
                return;
            }

            Rectangle inner = new Rectangle(bounds.Left + 1, bounds.Top + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
            GraphicsState cellClipState = g.Save();

            try
            {
                /*
                 * DataGridView의 CellPainting Graphics는 인접 컬럼까지 그릴 수 있는 상태로 전달될 수 있습니다.
                 * 긴 CAD 설명문자나 회전문자가 철근규격/길이 컬럼까지 넘어가지 않도록 렌더러 자체에서
                 * 철근형상 셀 내부를 최종 clip 경계로 고정합니다.
                 */
                g.SetClip(inner, CombineMode.Intersect);

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

                /*
                 * 예외적인 CAD 객체 구성이나 기존 JSON에서 일자형 숫자만 남고 선이 빠진 경우에도
                 * 숫자만 표시되지 않도록 단일 숫자 TEXT 데이터에는 표시용 수평선을 복원합니다.
                 * 실제 지오메트리가 하나라도 있으면 적용하지 않습니다.
                 */
                EnsureStraightShapeFallback(data);
                DrawData(g, inner, data, dimensionText, applyTextOverrides, NormalizeViewZoomScale(viewZoomScale));
            }
            finally
            {
                g.Restore(cellClipState);
            }
        }

        public int GetRecommendedRowHeight(string jsonPath, int baseHeight, int maximumHeight)
        {
            if (baseHeight <= 0)
            {
                baseHeight = 62;
            }

            if (maximumHeight < baseHeight)
            {
                maximumHeight = baseHeight;
            }

            if (jsonPath == null || jsonPath.Trim() == "" || !File.Exists(jsonPath))
            {
                return baseHeight;
            }

            CadShapeData data = Load(jsonPath);

            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return baseHeight;
            }

            int textCount = CountElements(data, "TEXT");
            int geometryCount = CountGeometryElements(data);
            int recommended = baseHeight;

            if (textCount >= 8 || geometryCount >= 120)
            {
                recommended = (int)Math.Round(baseHeight * 1.72);
            }
            else if (textCount >= 5 || geometryCount >= 70)
            {
                recommended = (int)Math.Round(baseHeight * 1.45);
            }
            else if (textCount >= 3 || geometryCount >= 35)
            {
                recommended = (int)Math.Round(baseHeight * 1.23);
            }

            if (recommended > maximumHeight)
            {
                recommended = maximumHeight;
            }

            return Math.Max(baseHeight, recommended);
        }

        private void DrawData(Graphics g, Rectangle inner, CadShapeData data, string dimensionText, bool applyTextOverrides, float viewZoomScale)
        {
            // CAD 원본과 OVIA 편집 JSON은 rotation 부호의 기준이 다릅니다.
            // 원본 CAD는 기존 렌더링 방향을 유지하고, OVIA_EDIT/OVIA_MANUAL은 편집창에서 본 방향 그대로 표시합니다.
            bool useOviaEditedRotation = UsesOviaEditedRotation(data);

            /*
             * BarList/ERP/출력용 화면에서도 CAD에 그려진 형상 좌표와 종횡비를 그대로 사용합니다.
             * 형상 내부 TEXT의 숫자값은 표시 문자열일 뿐이며, 선분 길이를 다시 계산하거나
             * 구간별 비율을 변경하는 기준으로 사용하지 않습니다.
             * 셀 크기 맞춤은 아래 bounds 계산 후 X/Y 공통 단일 배율로만 수행합니다.
             */
            CadShapeData displayData = CadShapeDisplayNormalizer.CreateDisplayData(data);

            if (displayData != null)
            {
                data = displayData;
            }

            bool useSourceCellLayout = UsesSourceCellLayout(data);
            bool useCadSourceTextMetrics = data != null
                && data.Source != null
                && data.Source.Trim().Equals("CAD", StringComparison.OrdinalIgnoreCase);
            SmoothingMode oldSmoothing = g.SmoothingMode;
            PixelOffsetMode oldPixelOffsetMode = g.PixelOffsetMode;
            CompositingQuality oldCompositingQuality = g.CompositingQuality;
            TextRenderingHint oldTextRenderingHint = g.TextRenderingHint;
            int oldTextContrast = g.TextContrast;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.TextContrast = 0;

            try
            {
                RectangleF drawArea = new RectangleF(
                    inner.Left + Padding,
                    inner.Top + Padding,
                    Math.Max(1F, inner.Width - Padding * 2F),
                    Math.Max(1F, inner.Height - Padding * 2F)
                );

                if (drawArea.Width <= 1F || drawArea.Height <= 1F)
                {
                    return;
                }

                double contentMinX;
                double contentMinY;
                double contentMaxX;
                double contentMaxY;

                if (useSourceCellLayout && data.Width > 0.0001 && data.Height > 0.0001)
                {
                    /*
                     * 새 SOURCE_CELL JSON은 실제 CAD 철근형상 물리 셀 좌표계를 기준으로 맞춥니다.
                     * 따라서 콘텐츠만 타이트하게 다시 중앙 확대하지 않고, CAD 셀 안의 원래 여백과
                     * 문자·숫자·철근선의 상대 좌표를 동일한 X/Y 단일 배율로 보존합니다.
                     */
                    contentMinX = 0.0;
                    contentMinY = 0.0;
                    contentMaxX = Math.Max(data.Width, 1.0);
                    contentMaxY = Math.Max(data.Height, 1.0);
                }
                else if (!GetElementBounds(data, out contentMinX, out contentMinY, out contentMaxX, out contentMaxY))
                {
                    contentMinX = 0;
                    contentMinY = 0;
                    contentMaxX = Math.Max(data.Width, 1);
                    contentMaxY = Math.Max(data.Height, 1);
                }

                double contentWidth = Math.Max(contentMaxX - contentMinX, 1.0);
                double contentHeight = Math.Max(contentMaxY - contentMinY, 1.0);
                double scale = Math.Min(drawArea.Width / contentWidth, drawArea.Height / contentHeight) * VisualScale;

                /*
                 * 과거 CONTENT_BOUNDS JSON의 일자형 철근은 세로 높이가 거의 없어 가로 폭을
                 * 과도하게 채울 수 있으므로 기존 60% 상한을 유지합니다. SOURCE_CELL JSON은
                 * CAD 물리 셀 안의 원래 여백과 선 길이 비율을 이미 보존하므로 별도 상한을
                 * 다시 적용하지 않습니다.
                 */
                if (!useSourceCellLayout && IsStraightHorizontalShape(data))
                {
                    double straightWidthScale = drawArea.Width * StraightShapeMaxWidthRatio / contentWidth;

                    if (straightWidthScale < scale)
                    {
                        scale = straightWidthScale;
                    }
                }

                float offsetX = drawArea.Left
                    + (float)((drawArea.Width - contentWidth * scale) / 2.0)
                    - (float)(contentMinX * scale);
                float offsetY = drawArea.Top
                    + (float)((drawArea.Height - contentHeight * scale) / 2.0)
                    - (float)(contentMinY * scale);

                float penWidth = Math.Max(1.15F, Math.Min(1.85F, inner.Height / 56F));
                Dictionary<string, string> overrideValues = applyTextOverrides
                    ? BuildCadTextOverrideMap(dimensionText)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int overrideTextIndex = 0;

                using (Pen pen = new Pen(Color.FromArgb(8, 12, 22), penWidth))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(0, 0, 0)))
                using (Font textFont = OviaFluentTheme.FontKorean(CadTextFontSizePt * VisualScale * viewZoomScale, FontStyle.Regular, GraphicsUnit.Point))
                using (Font rotatedTextFont = OviaFluentTheme.FontKorean(CadRotatedTextFontSizePt * VisualScale * viewZoomScale, FontStyle.Regular, GraphicsUnit.Point))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    int i;

                    // 도형은 CAD 좌표를 같은 비율로만 축소하여 먼저 그립니다.
                    for (i = 0; i < data.Elements.Count; i++)
                    {
                        CadShapeElement element = data.Elements[i];

                        if (element == null)
                        {
                            continue;
                        }

                        if (element.Type == "LINE")
                        {
                            g.DrawLine(
                                pen,
                                X(element.X1, offsetX, scale),
                                Y(element.Y1, offsetY, scale),
                                X(element.X2, offsetX, scale),
                                Y(element.Y2, offsetY, scale)
                            );
                        }
                        else if (element.Type == "CIRCLE")
                        {
                            float radius = (float)(element.Radius * scale);
                            float centerX = X(element.CX, offsetX, scale);
                            float centerY = Y(element.CY, offsetY, scale);
                            g.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2F, radius * 2F);
                        }
                        else if (element.Type == "ARC")
                        {
                            float radius = (float)(element.Radius * scale);
                            float centerX = X(element.CX, offsetX, scale);
                            float centerY = Y(element.CY, offsetY, scale);
                            RectangleF arcBounds = new RectangleF(
                                centerX - radius,
                                centerY - radius,
                                radius * 2F,
                                radius * 2F
                            );

                            float start = (float)(-element.StartAngle);
                            float sweep = (float)(-(element.EndAngle - element.StartAngle));

                            if (Math.Abs(sweep) < 0.1F)
                            {
                                sweep = 360F;
                            }

                            g.DrawArc(pen, arcBounds, start, sweep);
                        }
                    }

                    // CAD 원본 텍스트는 이동시키지 않고 원래 상대 좌표와 회전값 그대로 표시합니다.
                    // 기본 상한은 맑은 고딕 8pt이며, 원본 CAD 문자 높이와 실제 bounds가 더 작으면
                    // 해당 비율에 맞춰 축소하여 인접 컬럼 침범과 문자/형상 비율 왜곡을 방지합니다.
                    for (i = 0; i < data.Elements.Count; i++)
                    {
                        CadShapeElement element = data.Elements[i];

                        if (element == null || element.Type != "TEXT")
                        {
                            continue;
                        }

                        string text = element.Text == null ? "" : element.Text.Trim();
                        string replacement = "";
                        string textId = element.TextId == null ? "" : element.TextId.Trim();

                        if (textId != "" && overrideValues.TryGetValue(textId, out replacement))
                        {
                            replacement = replacement == null ? "" : replacement.Trim();
                        }
                        else
                        {
                            string legacyKey = GetLegacyCadOverrideKey(overrideTextIndex);

                            if (legacyKey != "")
                            {
                                overrideValues.TryGetValue(legacyKey, out replacement);
                                replacement = replacement == null ? "" : replacement.Trim();
                            }
                        }

                        if (replacement != "")
                        {
                            text = replacement;
                        }

                        overrideTextIndex++;

                        if (text == "")
                        {
                            continue;
                        }

                        PointF center = new PointF(
                            X(element.X1, offsetX, scale),
                            Y(element.Y1, offsetY, scale)
                        );

                        float normalizedRotation = NormalizeRotation((float)element.Rotation);
                        float renderRotation = useOviaEditedRotation
                            ? normalizedRotation
                            : -normalizedRotation;
                        Font baseFont = Math.Abs(renderRotation) <= 0.35F ? textFont : rotatedTextFont;
                        float fittedFontSize = GetCadTextFontSize(
                            g,
                            text,
                            baseFont,
                            element,
                            renderRotation,
                            scale,
                            useSourceCellLayout,
                            useCadSourceTextMetrics
                        );
                        Font fittedFont = null;
                        Font drawFont = baseFont;

                        if (Math.Abs(fittedFontSize - baseFont.SizeInPoints) > 0.01F)
                        {
                            fittedFont = OviaFluentTheme.FontKorean(
                                fittedFontSize,
                                FontStyle.Regular,
                                GraphicsUnit.Point
                            );
                            drawFont = fittedFont;
                        }

                        try
                        {
                            DrawTextAtCenter(
                                g,
                                text,
                                drawFont,
                                textBrush,
                                center,
                                renderRotation
                            );
                        }
                        finally
                        {
                            if (fittedFont != null)
                            {
                                fittedFont.Dispose();
                            }
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
                g.PixelOffsetMode = oldPixelOffsetMode;
                g.CompositingQuality = oldCompositingQuality;
                g.TextRenderingHint = oldTextRenderingHint;
                g.TextContrast = oldTextContrast;
            }
        }


        private bool UsesOviaEditedRotation(CadShapeData data)
        {
            if (data == null || data.Source == null)
            {
                return false;
            }

            string source = data.Source.Trim();
            return source.Equals("OVIA_EDIT", StringComparison.OrdinalIgnoreCase)
                || source.Equals("OVIA_MANUAL", StringComparison.OrdinalIgnoreCase);
        }

        private bool UsesSourceCellLayout(CadShapeData data)
        {
            if (data == null || data.LayoutPolicy == null)
            {
                return false;
            }

            return data.LayoutPolicy.Trim().Equals(SourceCellLayoutPolicy, StringComparison.OrdinalIgnoreCase);
        }

        private float GetCadTextFontSize(
            Graphics g,
            string text,
            Font baseFont,
            CadShapeElement element,
            float rotation,
            double coordinateScale,
            bool useSourceCellLayout,
            bool useCadSourceTextMetrics)
        {
            if (g == null || baseFont == null || element == null)
            {
                return baseFont == null ? CadTextFontSizePt : baseFont.SizeInPoints;
            }

            double elementTextScale = Math.Max(0.25D, Math.Min(8D, element.TextScale));
            float requestedSize = Math.Max(
                MinimumCadTextFontSizePt,
                Math.Min(72F, baseFont.SizeInPoints * (float)elementTextScale)
            );

            if (useSourceCellLayout && useCadSourceTextMetrics && element.Height > 0.0001 && coordinateScale > 0.0001)
            {
                /*
                 * CAD 문자 높이를 현재 SOURCE_CELL 단일 배율로 변환합니다.
                 * 기존처럼 모든 문자를 무조건 8pt로 그리면 CAD에서 작게 배치된 설명문자가
                 * 철근선보다 과도하게 커지고 셀 왼쪽으로 튀어나옵니다. 8pt는 최대 상한으로만
                 * 유지하고, 원본 CAD 높이가 더 작을 때는 원본 비율을 우선합니다.
                 */
                float sourceHeightPoints = (float)(element.Height * coordinateScale * 72.0 / Math.Max(g.DpiY, 1F));
                sourceHeightPoints *= (float)elementTextScale;

                if (sourceHeightPoints > 0.1F)
                {
                    requestedSize = Math.Min(requestedSize, Math.Max(MinimumCadTextFontSizePt, sourceHeightPoints));
                }
            }

            if (!useCadSourceTextMetrics || !element.HasBounds || String.IsNullOrEmpty(text))
            {
                return requestedSize;
            }

            double targetWidth = Math.Abs(element.BoundsMaxX - element.BoundsMinX) * coordinateScale * elementTextScale;
            double targetHeight = Math.Abs(element.BoundsMaxY - element.BoundsMinY) * coordinateScale * elementTextScale;

            if (targetWidth <= 0.5 || targetHeight <= 0.5)
            {
                return requestedSize;
            }

            using (Font probeFont = OviaFluentTheme.FontKorean(
                requestedSize,
                FontStyle.Regular,
                GraphicsUnit.Point))
            {
                SizeF measured = MeasureCadTextBounds(g, text, probeFont, rotation);

                if (measured.Width <= 0.1F || measured.Height <= 0.1F)
                {
                    return requestedSize;
                }

                double widthRatio = targetWidth * TextBoundsFitTolerance / measured.Width;
                double heightRatio = targetHeight * TextBoundsFitTolerance / measured.Height;
                double fitRatio = Math.Min(1.0, Math.Min(widthRatio, heightRatio));

                if (fitRatio < 0.999)
                {
                    requestedSize = Math.Max(
                        MinimumCadTextFontSizePt,
                        (float)(requestedSize * fitRatio)
                    );
                }
            }

            return requestedSize;
        }

        private SizeF MeasureCadTextBounds(Graphics g, string text, Font font, float rotation)
        {
            if (g == null || font == null || String.IsNullOrEmpty(text))
            {
                return SizeF.Empty;
            }

            float textWidth;
            float textHeight;

            if (Math.Abs(rotation) <= 0.35F)
            {
                Size measured = TextRenderer.MeasureText(
                    g,
                    text,
                    font,
                    new Size(10000, 1000),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                );
                textWidth = Math.Max(measured.Width + 2F, 1F);
                textHeight = Math.Max(measured.Height + 2F, 1F);
            }
            else
            {
                textWidth = MeasureTrackedCadTextWidth(g, text, font, RotatedTextTrackingPixels);
                textHeight = Math.Max(font.GetHeight(g), 1F);
            }

            double radians = Math.Abs(rotation) * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            float rotatedWidth = (float)(textWidth * cos + textHeight * sin);
            float rotatedHeight = (float)(textWidth * sin + textHeight * cos);
            return new SizeF(Math.Max(rotatedWidth, 1F), Math.Max(rotatedHeight, 1F));
        }

        private float MeasureTrackedCadTextWidth(Graphics g, string text, Font font, float trackingPixels)
        {
            if (g == null || font == null || String.IsNullOrEmpty(text))
            {
                return 0F;
            }

            float totalWidth = 0F;

            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                int i;

                for (i = 0; i < text.Length; i++)
                {
                    SizeF size = g.MeasureString(text[i].ToString(), font, new PointF(0F, 0F), format);
                    totalWidth += Math.Max(size.Width, 1F);
                }
            }

            if (text.Length > 1)
            {
                totalWidth += trackingPixels * (text.Length - 1);
            }

            return totalWidth;
        }

        private float NormalizeViewZoomScale(float value)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0F)
            {
                return 1F;
            }

            if (value < 1F)
            {
                return 1F;
            }

            if (value > 2.2F)
            {
                return 2.2F;
            }

            return value;
        }

        private Dictionary<string, string> BuildCadTextOverrideMap(string dimensionText)
        {
            Dictionary<string, string> values = ParseDimensionValues(dimensionText);

            /*
             * 기존 형상 수정창은 A~H, R1~R4 키를 사용합니다.
             * JSON v3의 안정적인 텍스트 ID(T1, T2...)에도 같은 값을 함께 연결하여
             * 기존 UI를 유지하면서 CAD 텍스트가 실제 화면에 반영되도록 합니다.
             */
            string[] legacyKeys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            int i;

            for (i = 0; i < legacyKeys.Length; i++)
            {
                string value;

                if (values.TryGetValue(legacyKeys[i], out value))
                {
                    string textId = "T" + (i + 1).ToString(CultureInfo.InvariantCulture);

                    if (!values.ContainsKey(textId))
                    {
                        values[textId] = value;
                    }
                }
            }

            return values;
        }

        private string GetLegacyCadOverrideKey(int textIndex)
        {
            string[] keys = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };

            if (textIndex < 0 || textIndex >= keys.Length)
            {
                return "";
            }

            return keys[textIndex];
        }

        private Dictionary<string, string> ParseDimensionValues(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (text == null)
            {
                return values;
            }

            string[] parts = text.Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
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
            bool geometryFound = false;

            /*
             * 지오메트리와 CAD 원본 TEXT bounds를 함께 사용합니다.
             * 표시 문자열을 다시 측정해 bounds를 만들지 않고 JSON에 저장된 원본 extents만 사용하므로,
             * 사용자가 330을 3,300으로 수정해도 형상 전체 배율이 임의로 바뀌지 않습니다.
             * 반면 긴 원본 설명문자의 실제 폭은 맞춤 범위에 포함되어 인접 컬럼 침범을 방지합니다.
             */
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
                    geometryFound = true;
                }
                else if (element.Type == "CIRCLE" || element.Type == "ARC")
                {
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX - element.Radius, element.CY - element.Radius);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX + element.Radius, element.CY + element.Radius);
                    geometryFound = true;
                }
            }

            if (geometryFound)
            {
                for (i = 0; i < data.Elements.Count; i++)
                {
                    CadShapeElement textElement = data.Elements[i];

                    if (textElement == null || textElement.Type != "TEXT")
                    {
                        continue;
                    }

                    if (textElement.HasBounds)
                    {
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, textElement.BoundsMinX, textElement.BoundsMinY);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, textElement.BoundsMaxX, textElement.BoundsMaxY);
                    }
                    else
                    {
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, textElement.X1, textElement.Y1);
                    }
                }
            }
            else
            {
                // 지오메트리가 전혀 없는 과거 TEXT 전용 JSON만 기존 호환 방식으로 표시합니다.
                bool textFound = false;

                for (i = 0; i < data.Elements.Count; i++)
                {
                    CadShapeElement element = data.Elements[i];

                    if (element == null || element.Type != "TEXT")
                    {
                        continue;
                    }

                    if (element.HasBounds)
                    {
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMinX, element.BoundsMinY);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMaxX, element.BoundsMaxY);
                    }
                    else
                    {
                        double textScale = Math.Max(0.25D, element.TextScale);
                        double estimatedHeight = Math.Max(element.Height, 0.8) * textScale;
                        double estimatedWidth = Math.Max(estimatedHeight * 3.0, estimatedHeight);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 - estimatedWidth / 2.0, element.Y1 - estimatedHeight / 2.0);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 + estimatedWidth / 2.0, element.Y1 + estimatedHeight / 2.0);
                    }

                    textFound = true;
                }

                if (!textFound)
                {
                    return false;
                }
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

        private void EnsureStraightShapeFallback(CadShapeData data)
        {
            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return;
            }

            CadShapeElement onlyText = null;
            int textCount = 0;
            int geometryCount = 0;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null)
                {
                    continue;
                }

                if (element.Type == "TEXT")
                {
                    if (element.Text != null && element.Text.Trim() != "")
                    {
                        textCount++;
                        onlyText = element;
                    }

                    continue;
                }

                if (element.Type == "LINE" || element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    geometryCount++;
                }
            }

            if (geometryCount > 0 || textCount != 1 || onlyText == null)
            {
                return;
            }

            string text = onlyText.Text == null ? "" : onlyText.Text.Trim().Replace(",", "");
            double numericValue;

            if (!Double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out numericValue)
                && !Double.TryParse(text, out numericValue))
            {
                return;
            }

            if (numericValue <= 0)
            {
                return;
            }

            double textHeight = Math.Max(onlyText.Height, 1.0);
            // 과거 TEXT 전용 JSON의 표시용 직선도 숫자 자릿수와 무관한 고정 비율로 복원합니다.
            double lineLength = Math.Max(textHeight * 7.5, 12.0);
            double centerX = onlyText.X1;
            double lineY = onlyText.Y1 + Math.Max(textHeight * 0.95, 0.8);

            CadShapeElement line = new CadShapeElement();
            line.Type = "LINE";
            line.X1 = centerX - lineLength / 2.0;
            line.Y1 = lineY;
            line.X2 = centerX + lineLength / 2.0;
            line.Y2 = lineY;
            data.Elements.Insert(0, line);
        }

        private bool IsStraightHorizontalShape(CadShapeData data)
        {
            if (data == null || data.Elements == null || data.Elements.Count == 0)
            {
                return false;
            }

            bool hasLine = false;
            double minY = Double.MaxValue;
            double maxY = Double.MinValue;
            double maxLineLength = 0.0;
            int i;

            for (i = 0; i < data.Elements.Count; i++)
            {
                CadShapeElement element = data.Elements[i];

                if (element == null || element.Type == "TEXT")
                {
                    continue;
                }

                if (element.Type != "LINE")
                {
                    return false;
                }

                double dx = Math.Abs(element.X2 - element.X1);
                double dy = Math.Abs(element.Y2 - element.Y1);
                double lineLength = Math.Sqrt(dx * dx + dy * dy);
                double horizontalTolerance = Math.Max(lineLength * 0.035, 0.10);

                if (dy > horizontalTolerance || dx <= 0.0001)
                {
                    return false;
                }

                hasLine = true;
                maxLineLength = Math.Max(maxLineLength, lineLength);
                minY = Math.Min(minY, Math.Min(element.Y1, element.Y2));
                maxY = Math.Max(maxY, Math.Max(element.Y1, element.Y2));
            }

            if (!hasLine)
            {
                return false;
            }

            double verticalSpread = Math.Max(maxY - minY, 0.0);
            return verticalSpread <= Math.Max(maxLineLength * 0.05, 0.20);
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

        private void DrawTextAtCenter(Graphics g, string text, Font font, Brush brush, PointF center, float rotation)
        {
            if (g == null || font == null || text == null || text.Trim() == "")
            {
                return;
            }

            /*
             * 수평 문자는 GDI TextRenderer를 사용해 ClearType으로 출력합니다.
             * 기존 DrawString + 소수점 좌표 조합은 8pt 숫자가 회색으로 번져 보이는 원인이었습니다.
             */
            if (Math.Abs(rotation) <= 0.35F)
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.PreserveGraphicsClipping;

                Size measured = TextRenderer.MeasureText(
                    g,
                    text,
                    font,
                    new Size(10000, 1000),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                );

                int width = Math.Max(measured.Width + 2, 4);
                int height = Math.Max(measured.Height + 2, 4);
                Rectangle textRect = new Rectangle(
                    (int)Math.Round(center.X - width / 2F),
                    (int)Math.Round(center.Y - height / 2F),
                    width,
                    height
                );

                TextRenderer.DrawText(g, text, font, textRect, Color.Black, flags);
                return;
            }

            GraphicsState state = g.Save();
            TextRenderingHint oldHint = g.TextRenderingHint;

            try
            {
                /*
                 * 90도 회전 치수는 작은 폰트에서 숫자 간격이 뭉쳐 보이기 쉽습니다.
                 * 회전 문자에는 조금 더 큰 전용 폰트를 사용하고, 문자별 미세 자간을 주어
                 * 140, 160 같은 세 자리 수치가 붙어 보이지 않도록 출력합니다.
                 */
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform((float)Math.Round(center.X), (float)Math.Round(center.Y));
                g.RotateTransform(rotation);
                DrawTrackedCadText(g, text, font, brush, RotatedTextTrackingPixels);
            }
            finally
            {
                g.TextRenderingHint = oldHint;
                g.Restore(state);
            }
        }

        private void DrawTrackedCadText(Graphics g, string text, Font font, Brush brush, float trackingPixels)
        {
            if (g == null || font == null || brush == null || String.IsNullOrEmpty(text))
            {
                return;
            }

            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;

                float[] widths = new float[text.Length];
                float totalWidth = 0F;
                int i;

                for (i = 0; i < text.Length; i++)
                {
                    string character = text[i].ToString();
                    SizeF size = g.MeasureString(character, font, new PointF(0F, 0F), format);
                    widths[i] = Math.Max(size.Width, 1F);
                    totalWidth += widths[i];
                }

                if (text.Length > 1)
                {
                    totalWidth += trackingPixels * (text.Length - 1);
                }

                float x = -totalWidth / 2F;

                for (i = 0; i < text.Length; i++)
                {
                    g.DrawString(text[i].ToString(), font, brush, new PointF(x, 0F), format);
                    x += widths[i] + trackingPixels;
                }
            }
        }

        private float NormalizeRotation(float value)
        {
            while (value > 180F)
            {
                value -= 360F;
            }

            while (value < -180F)
            {
                value += 360F;
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
                data.Version = (int)Math.Round(GetNumber(json, "version", 1));
                data.Source = GetString(json, "source");
                data.LayoutPolicy = GetString(json, "layoutPolicy");
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
                    element.TextId = GetString(item, "textId");
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
                    element.TextScale = Math.Max(0.25D, GetNumber(item, "textScale", 1D));
                    element.Rotation = GetNumber(item, "rotation", 0);
                    element.HasBounds = HasNumber(item, "boundsMinX")
                        && HasNumber(item, "boundsMinY")
                        && HasNumber(item, "boundsMaxX")
                        && HasNumber(item, "boundsMaxY");
                    element.BoundsMinX = GetNumber(item, "boundsMinX", 0);
                    element.BoundsMinY = GetNumber(item, "boundsMinY", 0);
                    element.BoundsMaxX = GetNumber(item, "boundsMaxX", 0);
                    element.BoundsMaxY = GetNumber(item, "boundsMaxY", 0);
                    data.Elements.Add(element);
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        private bool HasNumber(string json, string key)
        {
            return Regex.IsMatch(
                json,
                "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*-?\\d+(?:\\.\\d+)?",
                RegexOptions.Singleline
            );
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
        public int Version = 1;
        public string Source = "";
        public string LayoutPolicy = "";
        public double Width = 100;
        public double Height = 60;
        public List<CadShapeElement> Elements = new List<CadShapeElement>();
    }

    internal class CadShapeElement
    {
        public string Type = "";
        public string Text = "";
        public string TextId = "";
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
        public double TextScale = 1;
        public double Rotation = 0;
        public bool HasBounds = false;
        public double BoundsMinX = 0;
        public double BoundsMinY = 0;
        public double BoundsMaxX = 0;
        public double BoundsMaxY = 0;
    }
}
