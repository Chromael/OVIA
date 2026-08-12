using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OVIA.Desktop
{
    internal enum BarListExcelCellType
    {
        TextLeft,
        TextCenter,
        NumberGeneral,
        Number2,
        Number3,
        Shape
    }

    internal sealed class BarListExcelColumn
    {
        public string Header = "";
        public double Width = 12.0;
        public BarListExcelCellType CellType = BarListExcelCellType.TextLeft;
    }

    internal sealed class BarListExcelShapeText
    {
        public string Text = "";
        public double CenterXRatio = 0.5D;
        public double CenterYRatio = 0.5D;
        public double WidthRatio = 0.15D;
        public double HeightRatio = 0.18D;
        public double RotationDegrees = 0D;
        public double FontSizePt = 9.0D;
    }

    internal sealed class BarListExcelRow
    {
        public readonly List<string> Values = new List<string>();
        public byte[] ShapePngBytes;
        public readonly List<BarListExcelShapeText> ShapeTexts = new List<BarListExcelShapeText>();
    }

    internal sealed class BarListExcelDocument
    {
        public string ProjectTitle = "";
        public string SummaryText = "";
        public readonly List<BarListExcelColumn> Columns = new List<BarListExcelColumn>();
        public readonly List<BarListExcelRow> Rows = new List<BarListExcelRow>();
    }

    /// <summary>
    /// 외부 Excel 라이브러리나 Excel 설치 의존 없이 실제 .xlsx(Open XML) 파일을 생성합니다.
    /// 철근형상은 PNG 바이트를 통합문서 패키지 내부 xl/media에 직접 넣으므로 별도 이미지 파일을 만들지 않습니다.
    /// </summary>
    internal static class BarListExcelExporter
    {
        private const long EmuPerPixel = 9525L;
        private const int HeaderRowNumber = 3;
        private const int FirstDataRowNumber = 4;

        public static void Save(string filePath, BarListExcelDocument document)
        {
            if (filePath == null || filePath.Trim() == "")
            {
                throw new ArgumentException("Excel 저장 경로가 비어 있습니다.", "filePath");
            }

            if (document == null || document.Columns.Count == 0)
            {
                throw new InvalidOperationException("Excel로 저장할 BarList 컬럼이 없습니다.");
            }

            string directory = Path.GetDirectoryName(filePath);

            if (directory != null && directory.Trim() != "")
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            int shapeColumnIndex = FindShapeColumnIndex(document.Columns);
            List<int> imageRowIndexes = GetImageRowIndexes(document.Rows, shapeColumnIndex);
            bool hasImages = imageRowIndexes.Count > 0;
            bool hasShapeTexts = HasShapeTextItems(document.Rows, shapeColumnIndex);
            bool hasDrawing = hasImages || hasShapeTexts;

            using (FileStream stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                WriteTextEntry(archive, "[Content_Types].xml", BuildContentTypesXml(hasDrawing, hasImages));
                WriteTextEntry(archive, "_rels/.rels", BuildPackageRelationshipsXml());
                WriteTextEntry(archive, "docProps/core.xml", BuildCorePropertiesXml());
                WriteTextEntry(archive, "docProps/app.xml", BuildAppPropertiesXml());
                WriteTextEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
                WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
                WriteTextEntry(archive, "xl/styles.xml", BuildStylesXml());
                WriteTextEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(document, hasDrawing));

                if (hasDrawing)
                {
                    WriteTextEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", BuildWorksheetRelationshipsXml());
                    WriteTextEntry(archive, "xl/drawings/drawing1.xml", BuildDrawingXml(document, imageRowIndexes, shapeColumnIndex));

                    if (hasImages)
                    {
                        WriteTextEntry(archive, "xl/drawings/_rels/drawing1.xml.rels", BuildDrawingRelationshipsXml(imageRowIndexes.Count));
                        WriteImageEntries(archive, document.Rows, imageRowIndexes);
                    }
                }
            }
        }

        private static int FindShapeColumnIndex(List<BarListExcelColumn> columns)
        {
            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (columns[i] != null && columns[i].CellType == BarListExcelCellType.Shape)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<int> GetImageRowIndexes(List<BarListExcelRow> rows, int shapeColumnIndex)
        {
            List<int> indexes = new List<int>();

            if (shapeColumnIndex < 0 || rows == null)
            {
                return indexes;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                BarListExcelRow row = rows[i];

                if (row != null && row.ShapePngBytes != null && row.ShapePngBytes.Length > 0)
                {
                    indexes.Add(i);
                }
            }

            return indexes;
        }

        private static bool HasShapeTextItems(List<BarListExcelRow> rows, int shapeColumnIndex)
        {
            if (shapeColumnIndex < 0 || rows == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                BarListExcelRow row = rows[i];

                if (row != null && row.ShapeTexts != null && row.ShapeTexts.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteTextEntry(ZipArchive archive, string path, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);

            using (Stream entryStream = entry.Open())
            using (StreamWriter writer = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                writer.Write(text == null ? "" : text);
            }
        }

        private static void WriteImageEntries(ZipArchive archive, List<BarListExcelRow> rows, List<int> imageRowIndexes)
        {
            int i;

            for (i = 0; i < imageRowIndexes.Count; i++)
            {
                BarListExcelRow row = rows[imageRowIndexes[i]];
                ZipArchiveEntry entry = archive.CreateEntry("xl/media/image" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".png", CompressionLevel.Optimal);

                using (Stream stream = entry.Open())
                {
                    stream.Write(row.ShapePngBytes, 0, row.ShapePngBytes.Length);
                }
            }
        }

        private static string BuildContentTypesXml(bool hasDrawing, bool hasImages)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.AppendLine("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.AppendLine("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");

            if (hasImages)
            {
                sb.AppendLine("<Default Extension=\"png\" ContentType=\"image/png\"/>");
            }

            sb.AppendLine("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.AppendLine("<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.AppendLine("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");

            if (hasDrawing)
            {
                sb.AppendLine("<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
            }

            sb.AppendLine("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            sb.AppendLine("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            sb.AppendLine("</Types>");
            return sb.ToString();
        }

        private static string BuildPackageRelationshipsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>\n"
                + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>\n"
                + "</Relationships>";
        }

        private static string BuildCorePropertiesXml()
        {
            string utc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "<dc:creator>OVIA</dc:creator><cp:lastModifiedBy>OVIA</cp:lastModifiedBy>\n"
                + "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + utc + "</dcterms:created>\n"
                + "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + utc + "</dcterms:modified>\n"
                + "</cp:coreProperties>";
        }

        private static string BuildAppPropertiesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">"
                + "<Application>OVIA</Application>"
                + "</Properties>";
        }

        private static string BuildWorkbookXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n"
                + "<bookViews><workbookView/></bookViews>\n"
                + "<sheets><sheet name=\"BarList\" sheetId=\"1\" r:id=\"rId1\"/></sheets>\n"
                + "</workbook>";
        }

        private static string BuildWorkbookRelationshipsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n"
                + "</Relationships>";
        }

        private static string BuildStylesXml()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.AppendLine("<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"#,##0\"/><numFmt numFmtId=\"165\" formatCode=\"#,##0.00\"/><numFmt numFmtId=\"166\" formatCode=\"#,##0.000\"/></numFmts>");
            sb.AppendLine("<fonts count=\"3\">");
            sb.AppendLine("<font><sz val=\"10\"/><name val=\"Malgun Gothic\"/><family val=\"2\"/></font>");
            sb.AppendLine("<font><b/><sz val=\"14\"/><name val=\"Malgun Gothic\"/><family val=\"2\"/></font>");
            sb.AppendLine("<font><b/><sz val=\"10\"/><name val=\"Malgun Gothic\"/><family val=\"2\"/></font>");
            sb.AppendLine("</fonts>");
            sb.AppendLine("<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF3F4F6\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>");
            sb.AppendLine("<borders count=\"2\"><border/><border><left style=\"thin\"><color rgb=\"FFD1D5DB\"/></left><right style=\"thin\"><color rgb=\"FFD1D5DB\"/></right><top style=\"thin\"><color rgb=\"FFD1D5DB\"/></top><bottom style=\"thin\"><color rgb=\"FFD1D5DB\"/></bottom></border></borders>");
            sb.AppendLine("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            sb.AppendLine("<cellXfs count=\"10\">");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\" wrapText=\"1\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>");
            sb.AppendLine("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>");
            sb.AppendLine("</cellXfs>");
            sb.AppendLine("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            sb.AppendLine("</styleSheet>");
            return sb.ToString();
        }

        private static string BuildWorksheetXml(BarListExcelDocument document, bool hasDrawing)
        {
            int columnCount = document.Columns.Count;
            int lastRow = Math.Max(HeaderRowNumber, FirstDataRowNumber + document.Rows.Count - 1);
            string lastColumn = ToExcelColumnName(columnCount);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.AppendLine("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            sb.Append("<dimension ref=\"A1:");
            sb.Append(lastColumn);
            sb.Append(lastRow.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("\"/>");
            sb.AppendLine("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"3\" topLeftCell=\"A4\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.AppendLine("<sheetFormatPr defaultRowHeight=\"18\"/>");
            sb.AppendLine(BuildColumnsXml(document.Columns));
            sb.AppendLine("<sheetData>");
            AppendInlineStringRow(sb, 1, 1, document.ProjectTitle, 1, 24.0);
            AppendInlineStringRow(sb, 2, 1, document.SummaryText, 2, 18.0);
            AppendHeaderRow(sb, document.Columns);

            int r;

            for (r = 0; r < document.Rows.Count; r++)
            {
                AppendDataRow(sb, document.Columns, document.Rows[r], FirstDataRowNumber + r);
            }

            sb.AppendLine("</sheetData>");

            // ECMA-376 CT_Worksheet 순서: autoFilter는 mergeCells보다 먼저 와야 합니다.
            // 이 순서가 뒤집히면 Excel이 sheet1.xml을 복구 대상으로 판단하고 시트 내용을 제거할 수 있습니다.
            if (document.Rows.Count > 0)
            {
                sb.AppendLine("<autoFilter ref=\"A3:" + lastColumn + lastRow.ToString(CultureInfo.InvariantCulture) + "\"/>");
            }

            sb.AppendLine("<mergeCells count=\"2\"><mergeCell ref=\"A1:" + lastColumn + "1\"/><mergeCell ref=\"A2:" + lastColumn + "2\"/></mergeCells>");
            sb.AppendLine("<printOptions horizontalCentered=\"1\"/>");
            sb.AppendLine("<pageMargins left=\"0.3\" right=\"0.3\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/>");
            sb.AppendLine("<pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\" paperSize=\"9\"/>");

            if (hasDrawing)
            {
                sb.AppendLine("<drawing r:id=\"rId1\"/>");
            }

            sb.AppendLine("</worksheet>");
            return sb.ToString();
        }

        private static string BuildColumnsXml(List<BarListExcelColumn> columns)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<cols>");
            int i;

            for (i = 0; i < columns.Count; i++)
            {
                double width = columns[i] == null ? 12.0 : columns[i].Width;

                if (width < 4.0)
                {
                    width = 4.0;
                }

                if (width > 60.0)
                {
                    width = 60.0;
                }

                sb.Append("<col min=\"");
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                sb.Append("\" max=\"");
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                sb.Append("\" width=\"");
                sb.Append(width.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append("\" customWidth=\"1\"/>");
            }

            sb.Append("</cols>");
            return sb.ToString();
        }

        private static void AppendInlineStringRow(StringBuilder sb, int rowNumber, int columnNumber, string text, int styleIndex, double height)
        {
            sb.Append("<row r=\"");
            sb.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" ht=\"");
            sb.Append(height.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append("\" customHeight=\"1\">");
            AppendInlineStringCell(sb, ToExcelColumnName(columnNumber) + rowNumber.ToString(CultureInfo.InvariantCulture), text, styleIndex);
            sb.AppendLine("</row>");
        }

        private static void AppendHeaderRow(StringBuilder sb, List<BarListExcelColumn> columns)
        {
            sb.Append("<row r=\"3\" ht=\"22\" customHeight=\"1\">");
            int i;

            for (i = 0; i < columns.Count; i++)
            {
                string reference = ToExcelColumnName(i + 1) + HeaderRowNumber.ToString(CultureInfo.InvariantCulture);
                string header = columns[i] == null ? "" : columns[i].Header;
                AppendInlineStringCell(sb, reference, header, 3);
            }

            sb.AppendLine("</row>");
        }

        private static void AppendDataRow(StringBuilder sb, List<BarListExcelColumn> columns, BarListExcelRow row, int excelRowNumber)
        {
            bool hasImage = row != null && row.ShapePngBytes != null && row.ShapePngBytes.Length > 0;
            bool hasShapeText = row != null && row.ShapeTexts != null && row.ShapeTexts.Count > 0;
            double height = (hasImage || hasShapeText) ? 58.0 : 24.0;
            sb.Append("<row r=\"");
            sb.Append(excelRowNumber.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" ht=\"");
            sb.Append(height.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append("\" customHeight=\"1\">");
            int i;

            for (i = 0; i < columns.Count; i++)
            {
                BarListExcelColumn column = columns[i];
                string value = row != null && i < row.Values.Count && row.Values[i] != null ? row.Values[i] : "";
                string reference = ToExcelColumnName(i + 1) + excelRowNumber.ToString(CultureInfo.InvariantCulture);
                BarListExcelCellType cellType = column == null ? BarListExcelCellType.TextLeft : column.CellType;

                if (cellType == BarListExcelCellType.Shape)
                {
                    AppendInlineStringCell(sb, reference, hasImage ? "" : value, 9);
                }
                else if (cellType == BarListExcelCellType.TextCenter)
                {
                    AppendInlineStringCell(sb, reference, value, 5);
                }
                else if (cellType == BarListExcelCellType.NumberGeneral)
                {
                    AppendNumberOrTextCell(sb, reference, value, 6);
                }
                else if (cellType == BarListExcelCellType.Number2)
                {
                    AppendNumberOrTextCell(sb, reference, value, 7);
                }
                else if (cellType == BarListExcelCellType.Number3)
                {
                    AppendNumberOrTextCell(sb, reference, value, 8);
                }
                else
                {
                    AppendInlineStringCell(sb, reference, value, 4);
                }
            }

            sb.AppendLine("</row>");
        }

        private static void AppendNumberOrTextCell(StringBuilder sb, string reference, string value, int styleIndex)
        {
            decimal number;

            if (TryParseNumber(value, out number))
            {
                sb.Append("<c r=\"");
                sb.Append(reference);
                sb.Append("\" s=\"");
                sb.Append(styleIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append("\"><v>");
                sb.Append(number.ToString(CultureInfo.InvariantCulture));
                sb.Append("</v></c>");
                return;
            }

            AppendInlineStringCell(sb, reference, value, styleIndex);
        }

        private static void AppendInlineStringCell(StringBuilder sb, string reference, string text, int styleIndex)
        {
            sb.Append("<c r=\"");
            sb.Append(reference);
            sb.Append("\" s=\"");
            sb.Append(styleIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            sb.Append(EscapeXml(text));
            sb.Append("</t></is></c>");
        }

        private static bool TryParseNumber(string text, out decimal value)
        {
            value = 0M;

            if (text == null)
            {
                return false;
            }

            string normalized = text.Trim().Replace(",", "").Replace(" ", "");

            while (normalized.EndsWith(".", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            if (normalized == "")
            {
                return false;
            }

            return Decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string BuildWorksheetRelationshipsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/>"
                + "</Relationships>";
        }

        private static string BuildDrawingXml(BarListExcelDocument document, List<int> imageRowIndexes, int shapeColumnIndex)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            double widthChars = shapeColumnIndex >= 0 && shapeColumnIndex < document.Columns.Count
                ? document.Columns[shapeColumnIndex].Width
                : 30.0;
            int widthPixels = Math.Max(170, (int)Math.Round(widthChars * 7.1));
            int heightPixels = 72;
            int picturePadding = 4;
            int pictureWidthPixels = Math.Max(1, widthPixels - picturePadding * 2);
            int pictureHeightPixels = Math.Max(1, heightPixels - picturePadding * 2);
            long widthEmu = pictureWidthPixels * EmuPerPixel;
            long heightEmu = pictureHeightPixels * EmuPerPixel;
            int drawingObjectId = 1;
            int i;

            for (i = 0; i < imageRowIndexes.Count; i++)
            {
                int dataRowIndex = imageRowIndexes[i];
                int excelRowNumber = FirstDataRowNumber + dataRowIndex;
                int drawingRowIndex = excelRowNumber - 1;
                int relationshipId = i + 1;
                sb.AppendLine("<xdr:oneCellAnchor>");
                AppendDrawingAnchorFrom(sb, shapeColumnIndex, drawingRowIndex, picturePadding, picturePadding);
                sb.Append("<xdr:ext cx=\"");
                sb.Append(widthEmu.ToString(CultureInfo.InvariantCulture));
                sb.Append("\" cy=\"");
                sb.Append(heightEmu.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("\"/>");
                sb.Append("<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"");
                sb.Append(drawingObjectId.ToString(CultureInfo.InvariantCulture));
                sb.Append("\" name=\"RebarShape");
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("\"/><xdr:cNvPicPr/></xdr:nvPicPr>");
                sb.Append("<xdr:blipFill><a:blip r:embed=\"rId");
                sb.Append(relationshipId.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>");
                sb.AppendLine("<xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:ln><a:noFill/></a:ln></xdr:spPr></xdr:pic>");
                sb.AppendLine("<xdr:clientData fPrintsWithSheet=\"1\"/>");
                sb.AppendLine("</xdr:oneCellAnchor>");
                drawingObjectId++;
            }

            if (shapeColumnIndex >= 0)
            {
                int rowIndex;

                for (rowIndex = 0; rowIndex < document.Rows.Count; rowIndex++)
                {
                    BarListExcelRow row = document.Rows[rowIndex];

                    if (row == null || row.ShapeTexts == null || row.ShapeTexts.Count == 0)
                    {
                        continue;
                    }

                    int excelRowNumber = FirstDataRowNumber + rowIndex;
                    int drawingRowIndex = excelRowNumber - 1;
                    int textIndex;

                    for (textIndex = 0; textIndex < row.ShapeTexts.Count; textIndex++)
                    {
                        BarListExcelShapeText item = row.ShapeTexts[textIndex];

                        if (item == null || String.IsNullOrWhiteSpace(item.Text))
                        {
                            continue;
                        }

                        double centerX = picturePadding + Clamp01(item.CenterXRatio) * pictureWidthPixels;
                        double centerY = picturePadding + Clamp01(item.CenterYRatio) * pictureHeightPixels;
                        double boxWidth = Math.Max(22.0D, Math.Min(pictureWidthPixels, item.WidthRatio * pictureWidthPixels));
                        double boxHeight = Math.Max(14.0D, Math.Min(pictureHeightPixels, item.HeightRatio * pictureHeightPixels));
                        double left = centerX - boxWidth / 2.0D;
                        double top = centerY - boxHeight / 2.0D;
                        left = Math.Max(picturePadding, Math.Min(picturePadding + pictureWidthPixels - boxWidth, left));
                        top = Math.Max(picturePadding, Math.Min(picturePadding + pictureHeightPixels - boxHeight, top));
                        long boxWidthEmu = Math.Max(1L, (long)Math.Round(boxWidth * EmuPerPixel));
                        long boxHeightEmu = Math.Max(1L, (long)Math.Round(boxHeight * EmuPerPixel));
                        int rotation = NormalizeDrawingRotation(item.RotationDegrees);
                        int fontSize = (int)Math.Round(Math.Max(8.5D, Math.Min(12.0D, item.FontSizePt)) * 100.0D);

                        sb.AppendLine("<xdr:oneCellAnchor>");
                        AppendDrawingAnchorFrom(
                            sb,
                            shapeColumnIndex,
                            drawingRowIndex,
                            (int)Math.Round(left),
                            (int)Math.Round(top)
                        );
                        sb.Append("<xdr:ext cx=\"");
                        sb.Append(boxWidthEmu.ToString(CultureInfo.InvariantCulture));
                        sb.Append("\" cy=\"");
                        sb.Append(boxHeightEmu.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("\"/>");
                        sb.AppendLine("<xdr:sp macro=\"\" textlink=\"\">");
                        sb.Append("<xdr:nvSpPr><xdr:cNvPr id=\"");
                        sb.Append(drawingObjectId.ToString(CultureInfo.InvariantCulture));
                        sb.Append("\" name=\"RebarDimension");
                        sb.Append(drawingObjectId.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("\"/><xdr:cNvSpPr txBox=\"1\"/></xdr:nvSpPr>");
                        sb.AppendLine("<xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></xdr:spPr>");
                        sb.AppendLine("<xdr:style><a:lnRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"dk1\"/></a:fontRef></xdr:style>");
                        sb.Append("<xdr:txBody><a:bodyPr wrap=\"none\" rtlCol=\"0\" anchor=\"ctr\" anchorCtr=\"1\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"");

                        if (rotation != 0)
                        {
                            sb.Append(" rot=\"");
                            sb.Append(rotation.ToString(CultureInfo.InvariantCulture));
                            sb.Append("\"");
                        }

                        sb.AppendLine("/><a:lstStyle/><a:p><a:pPr algn=\"ctr\"/>");
                        sb.Append("<a:r><a:rPr lang=\"ko-KR\" sz=\"");
                        sb.Append(fontSize.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("\" dirty=\"0\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill><a:latin typeface=\"Malgun Gothic\"/><a:ea typeface=\"Malgun Gothic\"/><a:cs typeface=\"Malgun Gothic\"/></a:rPr>");
                        sb.Append("<a:t>");
                        sb.Append(EscapeXml(item.Text));
                        sb.AppendLine("</a:t></a:r></a:p></xdr:txBody>");
                        sb.AppendLine("</xdr:sp>");
                        sb.AppendLine("<xdr:clientData fPrintsWithSheet=\"1\"/>");
                        sb.AppendLine("</xdr:oneCellAnchor>");
                        drawingObjectId++;
                    }
                }
            }

            sb.AppendLine("</xdr:wsDr>");
            return sb.ToString();
        }

        private static void AppendDrawingAnchorFrom(StringBuilder sb, int zeroBasedColumnIndex, int zeroBasedRowIndex, int xOffsetPixels, int yOffsetPixels)
        {
            sb.Append("<xdr:from><xdr:col>");
            sb.Append(zeroBasedColumnIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append("</xdr:col><xdr:colOff>");
            sb.Append(((long)Math.Max(0, xOffsetPixels) * EmuPerPixel).ToString(CultureInfo.InvariantCulture));
            sb.Append("</xdr:colOff><xdr:row>");
            sb.Append(zeroBasedRowIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append("</xdr:row><xdr:rowOff>");
            sb.Append(((long)Math.Max(0, yOffsetPixels) * EmuPerPixel).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("</xdr:rowOff></xdr:from>");
        }

        private static double Clamp01(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                return 0.5D;
            }

            if (value < 0D)
            {
                return 0D;
            }

            if (value > 1D)
            {
                return 1D;
            }

            return value;
        }

        private static int NormalizeDrawingRotation(double degrees)
        {
            if (Double.IsNaN(degrees) || Double.IsInfinity(degrees))
            {
                return 0;
            }

            double normalized = degrees % 360.0D;

            if (normalized < 0D)
            {
                normalized += 360.0D;
            }

            if (Math.Abs(normalized) < 0.01D || Math.Abs(normalized - 360.0D) < 0.01D)
            {
                return 0;
            }

            return (int)Math.Round(normalized * 60000.0D);
        }

        private static string BuildDrawingRelationshipsXml(int imageCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            int i;

            for (i = 0; i < imageCount; i++)
            {
                int id = i + 1;
                sb.Append("<Relationship Id=\"rId");
                sb.Append(id.ToString(CultureInfo.InvariantCulture));
                sb.Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image");
                sb.Append(id.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(".png\"/>");
            }

            sb.AppendLine("</Relationships>");
            return sb.ToString();
        }

        private static string ToExcelColumnName(int oneBasedColumnIndex)
        {
            if (oneBasedColumnIndex < 1)
            {
                return "A";
            }

            StringBuilder sb = new StringBuilder();
            int index = oneBasedColumnIndex;

            while (index > 0)
            {
                index--;
                sb.Insert(0, (char)('A' + (index % 26)));
                index /= 26;
            }

            return sb.ToString();
        }

        private static string EscapeXml(string value)
        {
            if (value == null)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(value.Length + 16);
            int i;

            for (i = 0; i < value.Length; i++)
            {
                char ch = value[i];

                if (Char.IsHighSurrogate(ch))
                {
                    if (i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
                    {
                        sb.Append(ch);
                        sb.Append(value[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (Char.IsLowSurrogate(ch))
                {
                    continue;
                }

                if (ch != '\t' && ch != '\n' && ch != '\r' && (ch < 0x20 || ch == 0xFFFE || ch == 0xFFFF))
                {
                    continue;
                }

                if (ch == '&')
                {
                    sb.Append("&amp;");
                }
                else if (ch == '<')
                {
                    sb.Append("&lt;");
                }
                else if (ch == '>')
                {
                    sb.Append("&gt;");
                }
                else if (ch == '"')
                {
                    sb.Append("&quot;");
                }
                else if (ch == '\'')
                {
                    sb.Append("&apos;");
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }
    }
}
