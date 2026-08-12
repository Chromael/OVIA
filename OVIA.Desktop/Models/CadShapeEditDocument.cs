using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    public sealed class CadShapeEditDocument
    {
        public int Version = 4;
        public int RowNo = 0;
        public double Width = 100D;
        public double Height = 60D;
        public string Source = "CAD";
        public string OriginalSourcePath = "";
        public List<CadShapeEditElement> Elements = new List<CadShapeEditElement>();

        public static CadShapeEditDocument CreateEmpty()
        {
            CadShapeEditDocument document = new CadShapeEditDocument();
            document.Version = 4;
            document.Source = "OVIA_MANUAL";
            document.Width = 160D;
            document.Height = 80D;
            return document;
        }

        public static CadShapeEditDocument Load(string path)
        {
            if (path == null || path.Trim() == "" || !File.Exists(path))
            {
                return CreateEmpty();
            }

            try
            {
                string json = File.ReadAllText(path);
                CadShapeEditDocument document = new CadShapeEditDocument();
                document.Version = (int)Math.Round(GetNumber(json, "version", 3D));
                document.RowNo = (int)Math.Round(GetNumber(json, "rowNo", 0D));
                document.Width = GetNumber(json, "width", 100D);
                document.Height = GetNumber(json, "height", 60D);
                document.Source = GetString(json, "source");
                document.OriginalSourcePath = GetString(json, "originalSourcePath");

                MatchCollection matches = Regex.Matches(
                    json,
                    "\\{[^\\{\\}]*\\\"type\\\"[^\\{\\}]*\\}",
                    RegexOptions.Singleline
                );

                int i;

                for (i = 0; i < matches.Count; i++)
                {
                    string item = matches[i].Value;
                    CadShapeEditElement element = new CadShapeEditElement();
                    element.Type = GetString(item, "type").ToUpperInvariant();
                    element.Text = GetString(item, "text");
                    element.TextId = GetString(item, "textId");
                    element.ObjectGroupId = GetString(item, "objectGroupId");
                    element.ObjectGroupKind = GetString(item, "objectGroupKind").ToUpperInvariant();
                    element.X1 = GetNumber(item, "x1", GetNumber(item, "x", 0D));
                    element.Y1 = GetNumber(item, "y1", GetNumber(item, "y", 0D));
                    element.X2 = GetNumber(item, "x2", 0D);
                    element.Y2 = GetNumber(item, "y2", 0D);
                    element.CX = GetNumber(item, "cx", 0D);
                    element.CY = GetNumber(item, "cy", 0D);
                    element.Radius = Math.Abs(GetNumber(item, "radius", 0D));
                    element.StartAngle = GetNumber(item, "startAngle", 0D);
                    element.EndAngle = GetNumber(item, "endAngle", 0D);
                    element.Height = GetNumber(item, "height", 2.5D);
                    element.TextScale = Math.Max(0.25D, GetNumber(item, "textScale", 1D));
                    element.Rotation = GetNumber(item, "rotation", 0D);
                    element.ColorIndex = (int)Math.Round(GetNumber(item, "colorIndex", 7D));
                    element.HasBounds = HasNumber(item, "boundsMinX")
                        && HasNumber(item, "boundsMinY")
                        && HasNumber(item, "boundsMaxX")
                        && HasNumber(item, "boundsMaxY");
                    element.BoundsMinX = GetNumber(item, "boundsMinX", 0D);
                    element.BoundsMinY = GetNumber(item, "boundsMinY", 0D);
                    element.BoundsMaxX = GetNumber(item, "boundsMaxX", 0D);
                    element.BoundsMaxY = GetNumber(item, "boundsMaxY", 0D);

                    if (element.Type != "")
                    {
                        document.Elements.Add(element);
                    }
                }

                document.EnsureTextIds();

                if (document.Source == null || document.Source.Trim() == "")
                {
                    document.Source = "CAD";
                }

                if (document.Width <= 0D)
                {
                    document.Width = 100D;
                }

                if (document.Height <= 0D)
                {
                    document.Height = 60D;
                }

                return document;
            }
            catch
            {
                return CreateEmpty();
            }
        }

        public CadShapeEditDocument Clone()
        {
            CadShapeEditDocument copy = new CadShapeEditDocument();
            copy.Version = Version;
            copy.RowNo = RowNo;
            copy.Width = Width;
            copy.Height = Height;
            copy.Source = Source;
            copy.OriginalSourcePath = OriginalSourcePath;

            int i;

            for (i = 0; i < Elements.Count; i++)
            {
                if (Elements[i] != null)
                {
                    copy.Elements.Add(Elements[i].Clone());
                }
            }

            return copy;
        }

        public void EnsureTextIds()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int next = 1;
            int i;

            for (i = 0; i < Elements.Count; i++)
            {
                CadShapeEditElement element = Elements[i];

                if (element == null || element.Type != "TEXT")
                {
                    continue;
                }

                string id = element.TextId == null ? "" : element.TextId.Trim().ToUpperInvariant();

                if (id != "" && !used.Contains(id))
                {
                    element.TextId = id;
                    used.Add(id);
                }
                else
                {
                    while (used.Contains("T" + next.ToString(CultureInfo.InvariantCulture)))
                    {
                        next++;
                    }

                    element.TextId = "T" + next.ToString(CultureInfo.InvariantCulture);
                    used.Add(element.TextId);
                    next++;
                }
            }
        }

        public List<CadShapeEditElement> GetTextElements()
        {
            List<CadShapeEditElement> result = new List<CadShapeEditElement>();
            int i;

            EnsureTextIds();

            for (i = 0; i < Elements.Count; i++)
            {
                CadShapeEditElement element = Elements[i];

                if (element != null && element.Type == "TEXT")
                {
                    result.Add(element);
                }
            }

            result.Sort(delegate(CadShapeEditElement a, CadShapeEditElement b)
            {
                return CompareTextIds(a == null ? "" : a.TextId, b == null ? "" : b.TextId);
            });

            return result;
        }

        public int CountGeometryElements()
        {
            int count = 0;
            int i;

            for (i = 0; i < Elements.Count; i++)
            {
                CadShapeEditElement element = Elements[i];

                if (element != null && (element.Type == "LINE" || element.Type == "ARC" || element.Type == "CIRCLE"))
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetBounds(out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = Double.MaxValue;
            minY = Double.MaxValue;
            maxX = Double.MinValue;
            maxY = Double.MinValue;
            bool found = false;
            int i;

            for (i = 0; i < Elements.Count; i++)
            {
                CadShapeEditElement element = Elements[i];

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
                else if (element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    double radius = Math.Abs(element.Radius);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX - radius, element.CY - radius);
                    IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.CX + radius, element.CY + radius);
                    found = true;
                }
                else if (element.Type == "TEXT")
                {
                    if (element.HasBounds)
                    {
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMinX, element.BoundsMinY);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.BoundsMaxX, element.BoundsMaxY);
                    }
                    else
                    {
                        double textScale = Math.Max(0.25D, element.TextScale);
                        double height = Math.Max(element.Height, 2D) * textScale;
                        double width = Math.Max(height * 0.58D * Math.Max((element.Text == null ? "" : element.Text).Length, 1), height);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 - width / 2D, element.Y1 - height / 2D);
                        IncludePoint(ref minX, ref minY, ref maxX, ref maxY, element.X1 + width / 2D, element.Y1 + height / 2D);
                    }

                    found = true;
                }
            }

            if (!found)
            {
                minX = 0D;
                minY = 0D;
                maxX = Math.Max(Width, 100D);
                maxY = Math.Max(Height, 60D);
            }

            return found;
        }

        public void RecalculateDocumentSize()
        {
            double minX;
            double minY;
            double maxX;
            double maxY;

            TryGetBounds(out minX, out minY, out maxX, out maxY);
            Width = Math.Max(maxX - minX, 1D);
            Height = Math.Max(maxY - minY, 1D);
        }

        public void Save(string path)
        {
            if (path == null || path.Trim() == "")
            {
                throw new ArgumentException("저장 경로가 없습니다.", "path");
            }

            EnsureTextIds();
            RecalculateDocumentSize();

            string directory = Path.GetDirectoryName(path);

            if (directory != null && directory.Trim() != "")
            {
                Directory.CreateDirectory(directory);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"version\": 4,\r\n");
            sb.Append("  \"source\": ").Append(JsonString(Source == null || Source.Trim() == "" ? "OVIA_EDIT" : Source)).Append(",\r\n");
            sb.Append("  \"coordinateSystem\": \"TOP_LEFT_Y_DOWN\",\r\n");
            sb.Append("  \"editor\": {\"name\": \"OVIA CAD Shape Editor\", \"preserveCadDirection\": true, \"cellFit\": \"PRESERVE_CAD_GEOMETRY\", \"dimensionTextAffectsGeometry\": false},\r\n");

            if (OriginalSourcePath != null && OriginalSourcePath.Trim() != "")
            {
                sb.Append("  \"originalSourcePath\": ").Append(JsonString(OriginalSourcePath)).Append(",\r\n");
            }

            sb.Append("  \"rowNo\": ").Append(RowNo.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"cell\": {\"width\": ").Append(JsonNumber(Width)).Append(", \"height\": ").Append(JsonNumber(Height)).Append("},\r\n");
            sb.Append("  \"elements\": [\r\n");

            int i;
            int written = 0;

            for (i = 0; i < Elements.Count; i++)
            {
                CadShapeEditElement element = Elements[i];

                if (element == null || element.Type == null || element.Type.Trim() == "")
                {
                    continue;
                }

                if (written > 0)
                {
                    sb.Append(",\r\n");
                }

                written++;
                sb.Append("    {");
                sb.Append("\"type\": ").Append(JsonString(element.Type.ToUpperInvariant()));

                if (element.Type == "LINE")
                {
                    sb.Append(", \"x1\": ").Append(JsonNumber(element.X1));
                    sb.Append(", \"y1\": ").Append(JsonNumber(element.Y1));
                    sb.Append(", \"x2\": ").Append(JsonNumber(element.X2));
                    sb.Append(", \"y2\": ").Append(JsonNumber(element.Y2));
                }
                else if (element.Type == "ARC" || element.Type == "CIRCLE")
                {
                    sb.Append(", \"cx\": ").Append(JsonNumber(element.CX));
                    sb.Append(", \"cy\": ").Append(JsonNumber(element.CY));
                    sb.Append(", \"radius\": ").Append(JsonNumber(Math.Abs(element.Radius)));
                    sb.Append(", \"startAngle\": ").Append(JsonNumber(element.StartAngle));
                    sb.Append(", \"endAngle\": ").Append(JsonNumber(element.EndAngle));
                }
                else if (element.Type == "TEXT")
                {
                    sb.Append(", \"text\": ").Append(JsonString(element.Text == null ? "" : element.Text));
                    sb.Append(", \"textId\": ").Append(JsonString(element.TextId == null ? "" : element.TextId));
                    sb.Append(", \"x\": ").Append(JsonNumber(element.X1));
                    sb.Append(", \"y\": ").Append(JsonNumber(element.Y1));
                    sb.Append(", \"height\": ").Append(JsonNumber(Math.Max(element.Height, 0.1D)));
                    sb.Append(", \"textScale\": ").Append(JsonNumber(Math.Max(element.TextScale, 0.25D)));
                    sb.Append(", \"rotation\": ").Append(JsonNumber(element.Rotation));
                    sb.Append(", \"align\": \"CENTER\"");

                    if (element.HasBounds)
                    {
                        sb.Append(", \"boundsMinX\": ").Append(JsonNumber(element.BoundsMinX));
                        sb.Append(", \"boundsMinY\": ").Append(JsonNumber(element.BoundsMinY));
                        sb.Append(", \"boundsMaxX\": ").Append(JsonNumber(element.BoundsMaxX));
                        sb.Append(", \"boundsMaxY\": ").Append(JsonNumber(element.BoundsMaxY));
                    }
                }

                if (element.ObjectGroupId != null && element.ObjectGroupId.Trim() != "")
                {
                    sb.Append(", \"objectGroupId\": ").Append(JsonString(element.ObjectGroupId.Trim()));

                    if (element.ObjectGroupKind != null && element.ObjectGroupKind.Trim() != "")
                    {
                        sb.Append(", \"objectGroupKind\": ").Append(JsonString(element.ObjectGroupKind.Trim().ToUpperInvariant()));
                    }
                }

                sb.Append(", \"colorIndex\": ").Append(element.ColorIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
            }

            sb.Append("\r\n  ]\r\n");
            sb.Append("}\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string BuildEditablePath(string sourcePath)
        {
            string preferredPath = "";

            if (sourcePath != null && sourcePath.Trim() != "")
            {
                try
                {
                    string fullPath = Path.GetFullPath(sourcePath);
                    string directory = Path.GetDirectoryName(fullPath);
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);

                    if (fileNameWithoutExtension.EndsWith("_ovia_edit", StringComparison.OrdinalIgnoreCase))
                    {
                        preferredPath = fullPath;
                    }
                    else if (directory != null && directory.Trim() != "")
                    {
                        preferredPath = Path.Combine(directory, fileNameWithoutExtension + "_ovia_edit.json");
                    }
                }
                catch
                {
                    preferredPath = "";
                }
            }

            if (preferredPath != "")
            {
                try
                {
                    string directory = Path.GetDirectoryName(preferredPath);

                    if (directory != null && directory.Trim() != "")
                    {
                        Directory.CreateDirectory(directory);
                    }

                    return preferredPath;
                }
                catch
                {
                }
            }

            string localDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA",
                "Data",
                "CadShapes"
            );
            Directory.CreateDirectory(localDirectory);

            return Path.Combine(
                localDirectory,
                "manual_shape_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".json"
            );
        }


        public static string BuildRawCopyPath(string editablePath)
        {
            if (editablePath == null || editablePath.Trim() == "")
            {
                return "";
            }

            string fullPath = Path.GetFullPath(editablePath);
            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileNameWithoutExtension(fullPath);

            if (fileName.EndsWith("_ovia_edit", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - "_ovia_edit".Length);
            }

            return Path.Combine(directory == null ? "" : directory, fileName + "_ovia_raw.json");
        }

        private static int CompareTextIds(string a, string b)
        {
            int ai = ParseTextId(a);
            int bi = ParseTextId(b);

            if (ai >= 0 && bi >= 0)
            {
                return ai.CompareTo(bi);
            }

            return String.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseTextId(string value)
        {
            if (value == null || value.Length < 2 || Char.ToUpperInvariant(value[0]) != 'T')
            {
                return -1;
            }

            int number;
            return Int32.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : -1;
        }

        private static void IncludePoint(ref double minX, ref double minY, ref double maxX, ref double maxY, double x, double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private static bool HasNumber(string json, string key)
        {
            return Regex.IsMatch(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*-?\\d+(?:\\.\\d+)?", RegexOptions.Singleline);
        }

        private static double GetNumber(string json, string key, double defaultValue)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)", RegexOptions.Singleline);

            if (!match.Success)
            {
                return defaultValue;
            }

            double value;
            return Double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }

        private static string GetString(string json, string key)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Singleline);

            if (!match.Success)
            {
                return "";
            }

            return Regex.Unescape(match.Groups[1].Value);
        }

        private static string JsonNumber(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                value = 0D;
            }

            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string JsonString(string value)
        {
            string safe = value == null ? "" : value;
            safe = safe.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return "\"" + safe + "\"";
        }
    }

    public sealed class CadShapeEditElement
    {
        public string Type = "";
        public string Text = "";
        public string TextId = "";
        public string ObjectGroupId = "";
        public string ObjectGroupKind = "";
        public double X1 = 0D;
        public double Y1 = 0D;
        public double X2 = 0D;
        public double Y2 = 0D;
        public double CX = 0D;
        public double CY = 0D;
        public double Radius = 0D;
        public double StartAngle = 0D;
        public double EndAngle = 0D;
        public double Height = 2.5D;
        public double TextScale = 1D;
        public double Rotation = 0D;
        public int ColorIndex = 7;
        public bool HasBounds = false;
        public double BoundsMinX = 0D;
        public double BoundsMinY = 0D;
        public double BoundsMaxX = 0D;
        public double BoundsMaxY = 0D;

        public CadShapeEditElement Clone()
        {
            return (CadShapeEditElement)MemberwiseClone();
        }
    }
}
