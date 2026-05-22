using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OVIA.Desktop
{
    public class RebarShapeRepository
    {
        private readonly List<RebarShapeInfo> shapes;
        private readonly Dictionary<string, RebarShapeInfo> aliasMap;

        private RebarShapeRepository()
        {
            shapes = new List<RebarShapeInfo>();
            aliasMap = new Dictionary<string, RebarShapeInfo>(StringComparer.OrdinalIgnoreCase);
        }

        public static RebarShapeRepository CreateDefault()
        {
            RebarShapeRepository repository = new RebarShapeRepository();

            // PDF에서 공식 확인되지 않은 형상명/분류/입력필드는 절대 기본값으로 넣지 않습니다.
            // 실제 데이터는 shape_index.csv / 관리자 검수 완료 파일에서만 읽습니다.
            repository.LoadExternalShapeIndexIfExists();

            if (repository.shapes.Count == 0)
            {
                repository.AddShape(0, "이미지 없음", "", "");
            }

            repository.LoadShapeFieldOverridesIfExists();
            repository.RebuildAliasMap();
            return repository;
        }

        public List<RebarShapeInfo> GetUserSelectableShapes()
        {
            List<RebarShapeInfo> list = new List<RebarShapeInfo>();
            int i;

            for (i = 0; i < shapes.Count; i++)
            {
                RebarShapeInfo shape = shapes[i];

                if (shape != null && shape.IsUserSelectable && IsApprovedForUser(shape.ApproveStatus))
                {
                    list.Add(shape);
                }
            }

            list.Sort(delegate (RebarShapeInfo a, RebarShapeInfo b)
            {
                return a.ShapeNo.CompareTo(b.ShapeNo);
            });

            return list;
        }

        private bool IsApprovedForUser(string approveStatus)
        {
            if (approveStatus == null)
            {
                return false;
            }

            approveStatus = approveStatus.Trim();

            return approveStatus.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
                || approveStatus.Equals("APPROVED_REF", StringComparison.OrdinalIgnoreCase)
                || approveStatus.Equals("PDF_REFERENCE", StringComparison.OrdinalIgnoreCase);
        }

        public RebarShapeInfo FindByRawValue(string rawValue)
        {
            if (rawValue == null)
            {
                rawValue = "";
            }

            string key = NormalizeAlias(rawValue);

            if (key == "")
            {
                return null;
            }

            RebarShapeInfo found;

            if (aliasMap.TryGetValue(key, out found))
            {
                return found;
            }

            int numeric;

            if (Int32.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
            {
                string padded = numeric.ToString("0000", CultureInfo.InvariantCulture);

                if (aliasMap.TryGetValue(padded, out found))
                {
                    return found;
                }
            }

            return null;
        }

        private void LoadExternalShapeIndexIfExists()
        {
            string filePath = FindShapeIndexFile();

            if (filePath == "")
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                int i;

                for (i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line == null || line.Trim() == "")
                    {
                        continue;
                    }

                    string[] parts = SplitCsvLine(line);

                    if (parts.Length < 6)
                    {
                        continue;
                    }

                    int shapeNo;

                    if (!Int32.TryParse(parts[1], out shapeNo))
                    {
                        continue;
                    }

                    RebarShapeInfo shape = FindBuiltInShapeByNo(shapeNo);

                    if (shape == null)
                    {
                        shape = AddShape(shapeNo, parts.Length > 2 ? parts[2] : "", parts.Length > 3 ? parts[3] : "", parts.Length > 4 ? parts[4] : "");
                    }
                    else
                    {
                        if (parts.Length > 2) shape.ShapeName = parts[2];
                        if (parts.Length > 3) shape.Category = parts[3];
                        if (parts.Length > 4) shape.FieldsText = parts[4];
                    }

                    shape.ShapeCode = parts[0];
                    shape.IsUserSelectable = parts[5].Trim() == "1";
                    shape.ApproveStatus = parts.Length >= 7 ? parts[6].Trim() : "APPROVED";
                    shape.SourceImagePath = parts.Length >= 8 ? parts[7].Trim() : "";
                    shape.RefSvgPath = parts.Length >= 9 ? parts[8].Trim() : "";
                    shape.CleanSvgPath = parts.Length >= 10 ? parts[9].Trim() : "";
                    shape.VectorStatus = parts.Length >= 11 ? parts[10].Trim() : "";
                    if (parts.Length >= 12)
                    {
                        shape.OptionText = parts[11].Trim();
                    }
                }
            }
            catch
            {
                // 외부 인덱스가 깨져도 BarList 화면이 죽으면 안 됩니다.
            }
        }


        private void LoadShapeFieldOverridesIfExists()
        {
            string filePath = FindShapeFieldOverridesFile();

            if (filePath == "")
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                int i;

                for (i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line == null || line.Trim() == "")
                    {
                        continue;
                    }

                    string[] parts = SplitCsvLine(line);

                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    int shapeNo;

                    if (!Int32.TryParse(parts[0], out shapeNo))
                    {
                        continue;
                    }

                    RebarShapeInfo shape = FindBuiltInShapeByNo(shapeNo);

                    if (shape == null)
                    {
                        // 검수 파일에 번호가 있어도 기본 목록에 없는 형상을 임의 생성하지 않습니다.
                        continue;
                    }

                    if (parts.Length >= 2 && parts[1].Trim() != "")
                    {
                        shape.FieldsText = parts[1].Trim();
                    }

                    if (parts.Length >= 3 && parts[2].Trim() != "")
                    {
                        shape.OptionText = parts[2].Trim();
                    }

                    if (parts.Length >= 4 && parts[3].Trim() != "")
                    {
                        shape.ShapeName = parts[3].Trim();
                    }

                    if (parts.Length >= 5 && parts[4].Trim() != "")
                    {
                        shape.Category = parts[4].Trim();
                    }
                }
            }
            catch
            {
                // 필드 보정 파일이 깨져도 형상 선택 화면 전체가 죽으면 안 됩니다.
            }
        }

        private string FindShapeFieldOverridesFile()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fileName = Path.Combine("Data", "Shapes", "shape_field_overrides.csv");
            string candidate = Path.Combine(baseDir, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo dir = new DirectoryInfo(baseDir);
            int i;

            for (i = 0; i < 8 && dir != null; i++)
            {
                candidate = Path.Combine(dir.FullName, fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(dir.FullName, "OVIA.Desktop", fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return "";
        }

        private string FindShapeIndexFile()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fileName = Path.Combine("Data", "Shapes", "shape_index.csv");
            string candidate = Path.Combine(baseDir, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo dir = new DirectoryInfo(baseDir);
            int i;

            for (i = 0; i < 8 && dir != null; i++)
            {
                candidate = Path.Combine(dir.FullName, fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(dir.FullName, "OVIA.Desktop", fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return "";
        }

        public string ResolveDataPath(string relativePath)
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

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo dir = new DirectoryInfo(baseDir);
            int i;

            for (i = 0; i < 8 && dir != null; i++)
            {
                candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(dir.FullName, "OVIA.Desktop", relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return "";
        }

        private string[] SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuote = false;
            int i;

            for (i = 0; i < line.Length; i++)
            {
                char ch = line[i];

                if (ch == '"')
                {
                    if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = !inQuote;
                    }
                }
                else if (ch == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private RebarShapeInfo FindBuiltInShapeByNo(int shapeNo)
        {
            int i;

            for (i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] != null && shapes[i].ShapeNo == shapeNo)
                {
                    return shapes[i];
                }
            }

            return null;
        }

        private void RebuildAliasMap()
        {
            aliasMap.Clear();
            int i;

            for (i = 0; i < shapes.Count; i++)
            {
                RebarShapeInfo shape = shapes[i];

                if (shape == null)
                {
                    continue;
                }

                AddAlias(shape.ShapeNo.ToString(CultureInfo.InvariantCulture), shape);
                AddAlias(shape.ShapeNo.ToString("0000", CultureInfo.InvariantCulture), shape);
                AddAlias(shape.ShapeCode, shape);
                AddAlias(shape.ShapeName, shape);
            }
        }

        private void AddAlias(string alias, RebarShapeInfo shape)
        {
            string key = NormalizeAlias(alias);

            if (key == "")
            {
                return;
            }

            if (!aliasMap.ContainsKey(key))
            {
                aliasMap.Add(key, shape);
            }
        }

        private string NormalizeAlias(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Trim();
            value = value.Replace(" ", "");
            value = value.Replace("-", "");
            value = value.Replace("_", "");

            return value.ToUpperInvariant();
        }

        private void LoadBuiltInShapes()
        {
            AddShape(0, "이미지 없음", "", "");
        }

        private RebarShapeInfo AddShape(int no, string name, string category, string fields)
        {
            RebarShapeInfo shape = new RebarShapeInfo();
            shape.ShapeNo = no;
            shape.ShapeCode = no.ToString("0000", CultureInfo.InvariantCulture);
            shape.ShapeName = name;
            shape.Category = category;
            shape.FieldsText = fields;
            shape.IsUserSelectable = true;
            shape.ApproveStatus = "APPROVED";
            BuildCommands(shape);
            shapes.Add(shape);
            return shape;
        }

        private void BuildCommands(RebarShapeInfo s)
        {
            if (s == null || s.ShapeNo != 0)
            {
                return;
            }

            s.Commands.Add(RebarShapeCommand.TextLabel(90, 45, "이미지 없음", false));
        }

        private void Line(List<RebarShapeCommand> c, float x1, float y1, float x2, float y2) { c.Add(RebarShapeCommand.Line(x1, y1, x2, y2)); }
        private void T(List<RebarShapeCommand> c, float x, float y, string text) { c.Add(RebarShapeCommand.TextLabel(x, y, text, false)); }
        private void R(List<RebarShapeCommand> c, float x, float y, string text) { c.Add(RebarShapeCommand.TextLabel(x, y, text, true)); }
        private void Circle(List<RebarShapeCommand> c, float x, float y, float r) { c.Add(RebarShapeCommand.Circle(x, y, r)); }
        private void Arc(List<RebarShapeCommand> c, float x, float y, float r, float start, float sweep) { c.Add(RebarShapeCommand.Arc(x, y, r, start, sweep)); }
        private void Rect(List<RebarShapeCommand> c, float x1, float y1, float x2, float y2) { Line(c,x1,y1,x2,y1); Line(c,x2,y1,x2,y2); Line(c,x2,y2,x1,y2); Line(c,x1,y2,x1,y1); }
        private void Poly(List<RebarShapeCommand> c, float[] pts)
        {
            int i;
            for (i = 0; i + 3 < pts.Length; i += 2)
            {
                Line(c, pts[i], pts[i + 1], pts[i + 2], pts[i + 3]);
            }
        }
    }
}
