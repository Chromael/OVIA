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
            repository.LoadCleanVectorTokenDefinitions();
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

        private void LoadCleanVectorTokenDefinitions()
        {
            // 대표님 확정 기준:
            // 1) PDF 이미지 위에 흰색 박스로 알파벳을 덮지 않습니다.
            // 2) 검수된 형상은 순수 라인/곡선 벡터 + 텍스트 토큰으로만 표시합니다.
            // 3) A/B/C/R1 토큰은 값 입력 시 같은 위치에서 숫자로 실시간 치환됩니다.
            // 4) 형상명/분류/옵션명은 PDF에서 공식 확인된 값이 아니면 넣지 않습니다.

            RegisterCleanVectorTokenShape(1, "A", new RebarShapeCommand[] {
                RebarShapeCommand.Line(50F, 46F, 130F, 46F),
                RebarShapeCommand.TextLabel(90F, 32F, "A", false)
            });

            RegisterCleanVectorTokenShape(2, "A|B", new RebarShapeCommand[] {
                RebarShapeCommand.Line(58F, 70F, 58F, 42F),
                RebarShapeCommand.Line(58F, 42F, 127F, 42F),
                RebarShapeCommand.TextLabel(48F, 55F, "A", false),
                RebarShapeCommand.TextLabel(90F, 28F, "B", false)
            });

            RegisterCleanVectorTokenShape(3, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(45F, 68F, 55F, 48F),
                RebarShapeCommand.Arc(66F, 48F, 11F, 180F, 90F),
                RebarShapeCommand.Line(66F, 37F, 128F, 37F),
                RebarShapeCommand.TextLabel(41F, 56F, "A", false),
                RebarShapeCommand.TextLabel(86F, 24F, "B", false),
                RebarShapeCommand.TextLabel(70F, 52F, "R1", false)
            });

            RegisterCleanVectorTokenShape(4, "A|B|C", new RebarShapeCommand[] {
                RebarShapeCommand.Line(52F, 70F, 52F, 42F),
                RebarShapeCommand.Line(52F, 42F, 130F, 42F),
                RebarShapeCommand.Line(130F, 42F, 130F, 70F),
                RebarShapeCommand.TextLabel(44F, 56F, "A", false),
                RebarShapeCommand.TextLabel(91F, 28F, "B", false),
                RebarShapeCommand.TextLabel(139F, 56F, "C", false)
            });

            RegisterCleanVectorTokenShape(5, "A|B|C|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 70F, 48F, 49F),
                RebarShapeCommand.Line(48F, 49F, 82F, 49F),
                RebarShapeCommand.Arc(90F, 57F, 8F, 270F, 90F),
                RebarShapeCommand.Line(96F, 62F, 130F, 76F),
                RebarShapeCommand.TextLabel(40F, 59F, "A", false),
                RebarShapeCommand.TextLabel(73F, 34F, "B", false),
                RebarShapeCommand.TextLabel(128F, 55F, "C", false),
                RebarShapeCommand.TextLabel(84F, 62F, "R1", false)
            });

            RegisterCleanVectorTokenShape(6, "A|B|C", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 70F, 48F, 50F),
                RebarShapeCommand.Line(48F, 50F, 118F, 50F),
                RebarShapeCommand.Line(118F, 50F, 118F, 30F),
                RebarShapeCommand.TextLabel(40F, 60F, "A", false),
                RebarShapeCommand.TextLabel(82F, 38F, "B", false),
                RebarShapeCommand.TextLabel(128F, 42F, "C", false)
            });

            RegisterCleanVectorTokenShape(7, "A|B|C|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 70F, 48F, 50F),
                RebarShapeCommand.Line(48F, 50F, 86F, 50F),
                RebarShapeCommand.Arc(94F, 50F, 8F, 180F, 90F),
                RebarShapeCommand.Line(94F, 42F, 132F, 25F),
                RebarShapeCommand.TextLabel(40F, 60F, "A", false),
                RebarShapeCommand.TextLabel(75F, 66F, "B", false),
                RebarShapeCommand.TextLabel(121F, 55F, "C", false),
                RebarShapeCommand.TextLabel(83F, 37F, "R1", false)
            });

            RegisterCleanVectorTokenShape(8, "A|B|C|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(42F, 46F, 83F, 46F),
                RebarShapeCommand.Arc(91F, 54F, 8F, 270F, 90F),
                RebarShapeCommand.Line(97F, 60F, 116F, 72F),
                RebarShapeCommand.Arc(124F, 72F, 8F, 180F, -90F),
                RebarShapeCommand.Line(124F, 80F, 150F, 80F),
                RebarShapeCommand.TextLabel(50F, 34F, "A", false),
                RebarShapeCommand.TextLabel(99F, 68F, "B", false),
                RebarShapeCommand.TextLabel(135F, 92F, "C", false),
                RebarShapeCommand.TextLabel(67F, 56F, "R1", false),
                RebarShapeCommand.TextLabel(126F, 56F, "R2", false)
            });

            RegisterCleanVectorTokenShape(9, "A|B|C|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(35F, 64F, 50F, 50F),
                RebarShapeCommand.Arc(60F, 50F, 10F, 180F, 90F),
                RebarShapeCommand.Line(60F, 40F, 120F, 40F),
                RebarShapeCommand.Arc(130F, 50F, 10F, 270F, 90F),
                RebarShapeCommand.Line(140F, 50F, 155F, 64F),
                RebarShapeCommand.TextLabel(42F, 42F, "A", false),
                RebarShapeCommand.TextLabel(90F, 27F, "B", false),
                RebarShapeCommand.TextLabel(143F, 42F, "C", false),
                RebarShapeCommand.TextLabel(60F, 66F, "R1", false),
                RebarShapeCommand.TextLabel(125F, 66F, "R2", false)
            });

            RegisterCleanVectorTokenShape(10, "A|B|C|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 28F, 70F, 50F),
                RebarShapeCommand.Arc(80F, 50F, 10F, 180F, -90F),
                RebarShapeCommand.Line(80F, 60F, 124F, 60F),
                RebarShapeCommand.Arc(134F, 60F, 10F, 180F, -90F),
                RebarShapeCommand.Line(141F, 67F, 154F, 80F),
                RebarShapeCommand.TextLabel(43F, 48F, "A", false),
                RebarShapeCommand.TextLabel(95F, 75F, "B", false),
                RebarShapeCommand.TextLabel(151F, 60F, "C", false),
                RebarShapeCommand.TextLabel(82F, 39F, "R1", false),
                RebarShapeCommand.TextLabel(130F, 75F, "R2", false)
            });

            RegisterCleanVectorTokenShape(11, "A|B|C|D", new RebarShapeCommand[] {
                RebarShapeCommand.Line(50F, 68F, 50F, 45F),
                RebarShapeCommand.Line(50F, 45F, 112F, 45F),
                RebarShapeCommand.Line(112F, 45F, 112F, 63F),
                RebarShapeCommand.Line(112F, 63F, 150F, 63F),
                RebarShapeCommand.TextLabel(42F, 60F, "A", false),
                RebarShapeCommand.TextLabel(82F, 31F, "B", false),
                RebarShapeCommand.TextLabel(105F, 58F, "C", false),
                RebarShapeCommand.TextLabel(132F, 50F, "D", false)
            });

            RegisterCleanVectorTokenShape(12, "A|B|C|D", new RebarShapeCommand[] {
                RebarShapeCommand.Line(50F, 68F, 50F, 42F),
                RebarShapeCommand.Line(50F, 42F, 130F, 42F),
                RebarShapeCommand.Line(130F, 42F, 130F, 68F),
                RebarShapeCommand.Line(130F, 68F, 105F, 68F),
                RebarShapeCommand.TextLabel(42F, 56F, "A", false),
                RebarShapeCommand.TextLabel(90F, 28F, "B", false),
                RebarShapeCommand.TextLabel(138F, 56F, "C", false),
                RebarShapeCommand.TextLabel(107F, 78F, "D", false)
            });

            RegisterCleanVectorTokenShape(13, "A|B|C|D|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(42F, 66F, 55F, 48F),
                RebarShapeCommand.Arc(66F, 48F, 11F, 180F, 90F),
                RebarShapeCommand.Line(66F, 37F, 105F, 37F),
                RebarShapeCommand.Line(105F, 37F, 105F, 65F),
                RebarShapeCommand.Line(105F, 65F, 150F, 65F),
                RebarShapeCommand.TextLabel(42F, 50F, "A", false),
                RebarShapeCommand.TextLabel(80F, 24F, "B", false),
                RebarShapeCommand.TextLabel(111F, 51F, "C", false),
                RebarShapeCommand.TextLabel(132F, 79F, "D", false),
                RebarShapeCommand.TextLabel(64F, 61F, "R1", false)
            });

            RegisterCleanVectorTokenShape(14, "A|B|C|D|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(50F, 68F, 50F, 42F),
                RebarShapeCommand.Line(50F, 42F, 93F, 42F),
                RebarShapeCommand.Arc(101F, 50F, 8F, 270F, 90F),
                RebarShapeCommand.Line(109F, 50F, 109F, 64F),
                RebarShapeCommand.Line(109F, 64F, 150F, 64F),
                RebarShapeCommand.TextLabel(42F, 56F, "A", false),
                RebarShapeCommand.TextLabel(78F, 28F, "B", false),
                RebarShapeCommand.TextLabel(111F, 42F, "C", false),
                RebarShapeCommand.TextLabel(134F, 78F, "D", false),
                RebarShapeCommand.TextLabel(80F, 57F, "R1", false)
            });

            RegisterCleanVectorTokenShape(15, "A|B|C|D|E", new RebarShapeCommand[] {
                RebarShapeCommand.Line(52F, 32F, 70F, 32F),
                RebarShapeCommand.Line(70F, 32F, 70F, 56F),
                RebarShapeCommand.Line(70F, 56F, 112F, 56F),
                RebarShapeCommand.Line(112F, 56F, 112F, 32F),
                RebarShapeCommand.Line(112F, 32F, 130F, 32F),
                RebarShapeCommand.TextLabel(58F, 18F, "A", false),
                RebarShapeCommand.TextLabel(62F, 45F, "B", false),
                RebarShapeCommand.TextLabel(91F, 72F, "C", false),
                RebarShapeCommand.TextLabel(120F, 45F, "D", false),
                RebarShapeCommand.TextLabel(125F, 18F, "E", false)
            });

            RegisterCleanVectorTokenShape(16, "A|B|C|D|E|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(52F, 32F, 70F, 32F),
                RebarShapeCommand.Line(70F, 32F, 70F, 46F),
                RebarShapeCommand.Arc(78F, 54F, 8F, 180F, -90F),
                RebarShapeCommand.Line(78F, 62F, 112F, 62F),
                RebarShapeCommand.Line(112F, 62F, 112F, 32F),
                RebarShapeCommand.Line(112F, 32F, 130F, 32F),
                RebarShapeCommand.TextLabel(58F, 18F, "A", false),
                RebarShapeCommand.TextLabel(62F, 45F, "B", false),
                RebarShapeCommand.TextLabel(92F, 76F, "C", false),
                RebarShapeCommand.TextLabel(120F, 48F, "D", false),
                RebarShapeCommand.TextLabel(126F, 18F, "E", false),
                RebarShapeCommand.TextLabel(89F, 50F, "R1", false)
            });

            RegisterCleanVectorTokenShape(17, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(36F, 32F, 54F, 32F),
                RebarShapeCommand.Line(54F, 32F, 65F, 62F),
                RebarShapeCommand.Arc(73F, 62F, 8F, 180F, -90F),
                RebarShapeCommand.Line(73F, 70F, 108F, 70F),
                RebarShapeCommand.Arc(116F, 62F, 8F, 90F, -90F),
                RebarShapeCommand.Line(124F, 62F, 136F, 32F),
                RebarShapeCommand.Line(136F, 32F, 154F, 32F),
                RebarShapeCommand.TextLabel(46F, 18F, "A", false),
                RebarShapeCommand.TextLabel(59F, 48F, "B", false),
                RebarShapeCommand.TextLabel(90F, 84F, "C", false),
                RebarShapeCommand.TextLabel(119F, 48F, "D", false),
                RebarShapeCommand.TextLabel(146F, 18F, "E", false),
                RebarShapeCommand.TextLabel(79F, 40F, "R1", false),
                RebarShapeCommand.TextLabel(111F, 40F, "R2", false)
            });

            // 18번은 제공된 PDF 추출 이미지에 존재하지 않아 등록하지 않습니다.

            RegisterCleanVectorTokenShape(19, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(40F, 68F, 40F, 45F),
                RebarShapeCommand.Line(40F, 45F, 78F, 45F),
                RebarShapeCommand.Arc(86F, 53F, 8F, 270F, 90F),
                RebarShapeCommand.Line(92F, 58F, 105F, 70F),
                RebarShapeCommand.Arc(113F, 70F, 8F, 180F, -90F),
                RebarShapeCommand.Line(113F, 78F, 134F, 78F),
                RebarShapeCommand.Line(134F, 78F, 150F, 48F),
                RebarShapeCommand.Line(150F, 48F, 164F, 48F),
                RebarShapeCommand.TextLabel(32F, 58F, "A", false),
                RebarShapeCommand.TextLabel(70F, 31F, "B", false),
                RebarShapeCommand.TextLabel(90F, 52F, "C", false),
                RebarShapeCommand.TextLabel(113F, 86F, "D", false),
                RebarShapeCommand.TextLabel(148F, 62F, "E", false),
                RebarShapeCommand.TextLabel(98F, 44F, "R1", false),
                RebarShapeCommand.TextLabel(128F, 60F, "R2", false)
            });

            RegisterCleanVectorTokenShape(20, "A|B|C|D|E|F|G|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(38F, 68F, 38F, 42F),
                RebarShapeCommand.Line(38F, 42F, 68F, 42F),
                RebarShapeCommand.Line(68F, 42F, 68F, 58F),
                RebarShapeCommand.Arc(76F, 58F, 8F, 180F, -90F),
                RebarShapeCommand.Line(76F, 66F, 104F, 66F),
                RebarShapeCommand.Arc(112F, 58F, 8F, 90F, -90F),
                RebarShapeCommand.Line(120F, 58F, 120F, 42F),
                RebarShapeCommand.Line(120F, 42F, 148F, 42F),
                RebarShapeCommand.Line(148F, 42F, 148F, 68F),
                RebarShapeCommand.TextLabel(29F, 58F, "A", false),
                RebarShapeCommand.TextLabel(58F, 30F, "B", false),
                RebarShapeCommand.TextLabel(71F, 54F, "C", false),
                RebarShapeCommand.TextLabel(91F, 82F, "D", false),
                RebarShapeCommand.TextLabel(118F, 54F, "E", false),
                RebarShapeCommand.TextLabel(139F, 30F, "F", false),
                RebarShapeCommand.TextLabel(156F, 58F, "G", false),
                RebarShapeCommand.TextLabel(83F, 50F, "R1", false),
                RebarShapeCommand.TextLabel(111F, 34F, "R2", false)
            });

            RegisterCleanVectorTokenShape(21, "A|B|C|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(28F, 70F, 48F, 54F),
                RebarShapeCommand.Arc(60F, 54F, 12F, 180F, 90F),
                RebarShapeCommand.Line(60F, 42F, 120F, 42F),
                RebarShapeCommand.Arc(132F, 54F, 12F, 270F, 90F),
                RebarShapeCommand.Line(144F, 54F, 164F, 70F),
                RebarShapeCommand.TextLabel(30F, 82F, "A", false),
                RebarShapeCommand.TextLabel(48F, 60F, "B", false),
                RebarShapeCommand.TextLabel(96F, 30F, "C", false),
                RebarShapeCommand.TextLabel(132F, 60F, "B", false),
                RebarShapeCommand.TextLabel(162F, 82F, "A", false),
                RebarShapeCommand.TextLabel(68F, 57F, "R1", false),
                RebarShapeCommand.TextLabel(126F, 57F, "R2", false)
            });

            RegisterCleanVectorTokenShape(22, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(38F, 32F, 58F, 32F),
                RebarShapeCommand.Arc(66F, 40F, 8F, 270F, -90F),
                RebarShapeCommand.Line(66F, 40F, 66F, 62F),
                RebarShapeCommand.Line(66F, 62F, 115F, 62F),
                RebarShapeCommand.Line(115F, 62F, 115F, 40F),
                RebarShapeCommand.Arc(123F, 40F, 8F, 180F, -90F),
                RebarShapeCommand.Line(123F, 32F, 146F, 32F),
                RebarShapeCommand.TextLabel(39F, 18F, "A", false),
                RebarShapeCommand.TextLabel(63F, 50F, "B", false),
                RebarShapeCommand.TextLabel(90F, 77F, "C", false),
                RebarShapeCommand.TextLabel(119F, 50F, "D", false),
                RebarShapeCommand.TextLabel(143F, 18F, "E", false),
                RebarShapeCommand.TextLabel(54F, 42F, "R1", false),
                RebarShapeCommand.TextLabel(130F, 42F, "R2", false)
            });

            RegisterCleanVectorTokenShape(23, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Arc(90F, 75F, 58F, 205F, 130F),
                RebarShapeCommand.Line(90F, 75F, 90F, 28F),
                RebarShapeCommand.Line(90F, 75F, 90F, 86F),
                RebarShapeCommand.TextLabel(92F, 20F, "A", false),
                RebarShapeCommand.TextLabel(52F, 63F, "B", false),
                RebarShapeCommand.TextLabel(103F, 70F, "R1", false)
            });

            RegisterCleanVectorTokenShape(24, "A|B|C|D|E|F|G|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(42F, 32F, 42F, 72F),
                RebarShapeCommand.Line(42F, 32F, 58F, 32F),
                RebarShapeCommand.Line(58F, 32F, 58F, 24F),
                RebarShapeCommand.Line(58F, 24F, 126F, 24F),
                RebarShapeCommand.Arc(132F, 32F, 8F, 270F, 90F),
                RebarShapeCommand.Line(140F, 32F, 140F, 72F),
                RebarShapeCommand.Line(140F, 72F, 106F, 72F),
                RebarShapeCommand.Arc(98F, 64F, 8F, 0F, 90F),
                RebarShapeCommand.Line(90F, 64F, 90F, 44F),
                RebarShapeCommand.Line(90F, 44F, 42F, 44F),
                RebarShapeCommand.TextLabel(92F, 13F, "A", false),
                RebarShapeCommand.TextLabel(36F, 52F, "B", false),
                RebarShapeCommand.TextLabel(89F, 82F, "C", false),
                RebarShapeCommand.TextLabel(147F, 54F, "D", false),
                RebarShapeCommand.TextLabel(125F, 34F, "E", false),
                RebarShapeCommand.TextLabel(58F, 14F, "F", false),
                RebarShapeCommand.TextLabel(43F, 22F, "G", false),
                RebarShapeCommand.TextLabel(112F, 60F, "R1", false),
                RebarShapeCommand.TextLabel(104F, 43F, "R2", false)
            });

            RegisterCleanVectorTokenShape(25, "A|B|C|D|E|F|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(55F, 78F, 55F, 50F),
                RebarShapeCommand.Line(55F, 50F, 82F, 28F),
                RebarShapeCommand.Arc(92F, 38F, 10F, 225F, 90F),
                RebarShapeCommand.Line(92F, 28F, 124F, 50F),
                RebarShapeCommand.Line(124F, 50F, 124F, 78F),
                RebarShapeCommand.Line(124F, 78F, 55F, 78F),
                RebarShapeCommand.Line(82F, 28F, 92F, 46F),
                RebarShapeCommand.TextLabel(78F, 20F, "A", false),
                RebarShapeCommand.TextLabel(100F, 20F, "A", false),
                RebarShapeCommand.TextLabel(51F, 50F, "B", false),
                RebarShapeCommand.TextLabel(45F, 70F, "C", false),
                RebarShapeCommand.TextLabel(90F, 88F, "D", false),
                RebarShapeCommand.TextLabel(130F, 70F, "E", false),
                RebarShapeCommand.TextLabel(130F, 50F, "F", false),
                RebarShapeCommand.TextLabel(88F, 48F, "R1", false),
                RebarShapeCommand.TextLabel(64F, 58F, "R2", false)
            });

            RegisterCleanVectorTokenShape(26, "A|B|C|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 72F, 48F, 48F),
                RebarShapeCommand.Arc(64F, 48F, 16F, 180F, 90F),
                RebarShapeCommand.Line(64F, 32F, 128F, 32F),
                RebarShapeCommand.TextLabel(40F, 60F, "A", false),
                RebarShapeCommand.TextLabel(60F, 28F, "B", false),
                RebarShapeCommand.TextLabel(116F, 20F, "C", false),
                RebarShapeCommand.TextLabel(78F, 52F, "R1", false)
            });

            RegisterCleanVectorTokenShape(27, "A|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Arc(90F, 82F, 58F, 205F, 130F),
                RebarShapeCommand.Line(90F, 82F, 90F, 42F),
                RebarShapeCommand.Line(90F, 82F, 90F, 88F),
                RebarShapeCommand.TextLabel(90F, 31F, "A", false),
                RebarShapeCommand.TextLabel(105F, 72F, "R1", false)
            });

            RegisterCleanVectorTokenShape(28, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Arc(86F, 82F, 55F, 205F, 115F),
                RebarShapeCommand.Line(86F, 82F, 86F, 48F),
                RebarShapeCommand.Line(122F, 48F, 140F, 70F),
                RebarShapeCommand.TextLabel(86F, 32F, "A", false),
                RebarShapeCommand.TextLabel(132F, 62F, "B", false),
                RebarShapeCommand.TextLabel(94F, 66F, "R1", false)
            });

            RegisterCleanVectorTokenShape(29, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(42F, 72F, 42F, 46F),
                RebarShapeCommand.Line(42F, 46F, 86F, 20F),
                RebarShapeCommand.Line(86F, 20F, 126F, 46F),
                RebarShapeCommand.Line(126F, 46F, 126F, 70F),
                RebarShapeCommand.Line(126F, 70F, 106F, 70F),
                RebarShapeCommand.Line(106F, 70F, 106F, 82F),
                RebarShapeCommand.Line(106F, 82F, 82F, 82F),
                RebarShapeCommand.TextLabel(34F, 66F, "A", false),
                RebarShapeCommand.TextLabel(70F, 29F, "B", false),
                RebarShapeCommand.TextLabel(110F, 30F, "C", false),
                RebarShapeCommand.TextLabel(134F, 57F, "D", false),
                RebarShapeCommand.TextLabel(99F, 92F, "E", false),
                RebarShapeCommand.TextLabel(92F, 29F, "R1", false),
                RebarShapeCommand.TextLabel(54F, 61F, "R2", false)
            });

            RegisterCleanVectorTokenShape(30, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(44F, 76F, 44F, 48F),
                RebarShapeCommand.Line(44F, 48F, 74F, 32F),
                RebarShapeCommand.Arc(84F, 42F, 10F, 225F, 95F),
                RebarShapeCommand.Line(92F, 34F, 126F, 34F),
                RebarShapeCommand.Line(126F, 34F, 126F, 76F),
                RebarShapeCommand.Line(126F, 76F, 103F, 76F),
                RebarShapeCommand.Line(103F, 76F, 103F, 86F),
                RebarShapeCommand.Line(103F, 86F, 82F, 86F),
                RebarShapeCommand.TextLabel(36F, 66F, "A", false),
                RebarShapeCommand.TextLabel(76F, 26F, "B", false),
                RebarShapeCommand.TextLabel(116F, 20F, "C", false),
                RebarShapeCommand.TextLabel(135F, 58F, "D", false),
                RebarShapeCommand.TextLabel(96F, 95F, "E", false),
                RebarShapeCommand.TextLabel(99F, 48F, "R1", false),
                RebarShapeCommand.TextLabel(54F, 69F, "R2", false)
            });

            RegisterCleanVectorTokenShape(31, "A|B|C|D|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(46F, 68F, 46F, 43F),
                RebarShapeCommand.Line(46F, 43F, 110F, 43F),
                RebarShapeCommand.Arc(118F, 51F, 8F, 270F, 90F),
                RebarShapeCommand.Line(126F, 51F, 126F, 64F),
                RebarShapeCommand.Arc(134F, 64F, 8F, 180F, -90F),
                RebarShapeCommand.Line(134F, 72F, 146F, 72F),
                RebarShapeCommand.TextLabel(38F, 56F, "A", false),
                RebarShapeCommand.TextLabel(80F, 30F, "B", false),
                RebarShapeCommand.TextLabel(126F, 43F, "C", false),
                RebarShapeCommand.TextLabel(142F, 68F, "D", false),
                RebarShapeCommand.TextLabel(98F, 53F, "R1", false),
                RebarShapeCommand.TextLabel(120F, 69F, "R2", false)
            });

            RegisterCleanVectorTokenShape(32, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Circle(90F, 50F, 28F),
                RebarShapeCommand.Line(65F, 50F, 115F, 50F),
                RebarShapeCommand.Line(115F, 50F, 130F, 36F),
                RebarShapeCommand.TextLabel(107F, 76F, "A", false),
                RebarShapeCommand.TextLabel(124F, 30F, "B", false),
                RebarShapeCommand.TextLabel(90F, 59F, "R1", false)
            });

            RegisterCleanVectorTokenShape(33, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Circle(83F, 50F, 27F),
                RebarShapeCommand.Line(55F, 50F, 111F, 50F),
                RebarShapeCommand.Line(111F, 50F, 126F, 42F),
                RebarShapeCommand.Line(126F, 42F, 126F, 58F),
                RebarShapeCommand.TextLabel(63F, 72F, "A", false),
                RebarShapeCommand.TextLabel(118F, 60F, "B", false),
                RebarShapeCommand.TextLabel(143F, 50F, "R1", false)
            });

            RegisterCleanVectorTokenShape(34, "A|B|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Arc(80F, 50F, 32F, 20F, 250F),
                RebarShapeCommand.Line(80F, 50F, 80F, 82F),
                RebarShapeCommand.Line(80F, 50F, 115F, 50F),
                RebarShapeCommand.TextLabel(55F, 69F, "A", false),
                RebarShapeCommand.TextLabel(103F, 56F, "B", false),
                RebarShapeCommand.TextLabel(94F, 75F, "R1", false)
            });

            RegisterCleanVectorTokenShape(35, "A|B|C|D|E|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(46F, 70F, 46F, 43F),
                RebarShapeCommand.Line(46F, 43F, 111F, 43F),
                RebarShapeCommand.Arc(119F, 51F, 8F, 270F, 90F),
                RebarShapeCommand.Line(127F, 51F, 127F, 70F),
                RebarShapeCommand.Line(127F, 70F, 127F, 82F),
                RebarShapeCommand.Line(127F, 82F, 108F, 82F),
                RebarShapeCommand.TextLabel(38F, 56F, "A", false),
                RebarShapeCommand.TextLabel(80F, 30F, "B", false),
                RebarShapeCommand.TextLabel(127F, 43F, "C", false),
                RebarShapeCommand.TextLabel(137F, 58F, "D", false),
                RebarShapeCommand.TextLabel(116F, 91F, "E", false),
                RebarShapeCommand.TextLabel(98F, 53F, "R1", false),
                RebarShapeCommand.TextLabel(119F, 70F, "R2", false)
            });

            RegisterCleanVectorTokenShape(36, "A|B|C|D|E|F", new RebarShapeCommand[] {
                RebarShapeCommand.Line(32F, 66F, 58F, 66F),
                RebarShapeCommand.Line(58F, 66F, 58F, 36F),
                RebarShapeCommand.Line(58F, 36F, 88F, 36F),
                RebarShapeCommand.Line(88F, 36F, 88F, 66F),
                RebarShapeCommand.Line(88F, 66F, 122F, 66F),
                RebarShapeCommand.Line(122F, 66F, 122F, 36F),
                RebarShapeCommand.Line(122F, 36F, 150F, 36F),
                RebarShapeCommand.Line(150F, 36F, 150F, 66F),
                RebarShapeCommand.Line(150F, 66F, 166F, 66F),
                RebarShapeCommand.TextLabel(37F, 54F, "A", false),
                RebarShapeCommand.TextLabel(51F, 48F, "B", false),
                RebarShapeCommand.TextLabel(67F, 22F, "C", false),
                RebarShapeCommand.TextLabel(92F, 78F, "D", false),
                RebarShapeCommand.TextLabel(132F, 22F, "E", false),
                RebarShapeCommand.TextLabel(154F, 78F, "F", false)
            });

            RegisterCleanVectorTokenShape(37, "A|B|C|D|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(58F, 72F, 58F, 50F),
                RebarShapeCommand.Line(58F, 50F, 106F, 50F),
                RebarShapeCommand.Arc(114F, 58F, 8F, 270F, 90F),
                RebarShapeCommand.Line(120F, 62F, 154F, 78F),
                RebarShapeCommand.TextLabel(75F, 82F, "A", false),
                RebarShapeCommand.TextLabel(48F, 64F, "B", false),
                RebarShapeCommand.TextLabel(98F, 36F, "C", false),
                RebarShapeCommand.TextLabel(130F, 45F, "D", false),
                RebarShapeCommand.TextLabel(90F, 62F, "R1", false)
            });

            RegisterCleanVectorTokenShape(38, "A|B|C|R1|R2|R3", new RebarShapeCommand[] {
                RebarShapeCommand.Line(32F, 78F, 58F, 58F),
                RebarShapeCommand.Line(58F, 58F, 58F, 42F),
                RebarShapeCommand.Line(58F, 42F, 92F, 42F),
                RebarShapeCommand.Arc(100F, 50F, 8F, 270F, 90F),
                RebarShapeCommand.Line(108F, 50F, 108F, 68F),
                RebarShapeCommand.Arc(116F, 68F, 8F, 180F, -90F),
                RebarShapeCommand.Line(116F, 76F, 142F, 76F),
                RebarShapeCommand.Line(152F, 44F, 152F, 84F),
                RebarShapeCommand.Line(144F, 44F, 160F, 44F),
                RebarShapeCommand.Line(144F, 84F, 160F, 84F),
                RebarShapeCommand.TextLabel(36F, 90F, "A", false),
                RebarShapeCommand.TextLabel(55F, 48F, "B", false),
                RebarShapeCommand.TextLabel(80F, 28F, "C", false),
                RebarShapeCommand.TextLabel(77F, 55F, "R1", false),
                RebarShapeCommand.TextLabel(120F, 55F, "R2", false),
                RebarShapeCommand.TextLabel(165F, 78F, "R3", false)
            });

            RegisterCleanVectorTokenShape(39, "A|B|C|R1|R2|R3", new RebarShapeCommand[] {
                RebarShapeCommand.Line(34F, 72F, 73F, 46F),
                RebarShapeCommand.Line(73F, 46F, 92F, 64F),
                RebarShapeCommand.Arc(100F, 64F, 8F, 180F, -90F),
                RebarShapeCommand.Line(100F, 72F, 134F, 72F),
                RebarShapeCommand.Line(148F, 42F, 148F, 82F),
                RebarShapeCommand.Line(140F, 42F, 156F, 42F),
                RebarShapeCommand.Line(140F, 82F, 156F, 82F),
                RebarShapeCommand.TextLabel(52F, 81F, "A", false),
                RebarShapeCommand.TextLabel(88F, 81F, "B", false),
                RebarShapeCommand.TextLabel(118F, 85F, "C", false),
                RebarShapeCommand.TextLabel(84F, 42F, "R1", false),
                RebarShapeCommand.TextLabel(113F, 53F, "R2", false),
                RebarShapeCommand.TextLabel(164F, 78F, "R3", false)
            });

            RegisterCleanVectorTokenShape(40, "A|B|C|R1|R2|R3", new RebarShapeCommand[] {
                RebarShapeCommand.Line(36F, 48F, 74F, 48F),
                RebarShapeCommand.Arc(82F, 56F, 8F, 270F, 90F),
                RebarShapeCommand.Line(88F, 62F, 106F, 72F),
                RebarShapeCommand.Arc(114F, 72F, 8F, 180F, -90F),
                RebarShapeCommand.Line(114F, 80F, 140F, 80F),
                RebarShapeCommand.Line(154F, 44F, 154F, 84F),
                RebarShapeCommand.Line(146F, 44F, 162F, 44F),
                RebarShapeCommand.Line(146F, 84F, 162F, 84F),
                RebarShapeCommand.TextLabel(60F, 36F, "A", false),
                RebarShapeCommand.TextLabel(97F, 83F, "B", false),
                RebarShapeCommand.TextLabel(124F, 93F, "C", false),
                RebarShapeCommand.TextLabel(82F, 37F, "R1", false),
                RebarShapeCommand.TextLabel(112F, 58F, "R2", false),
                RebarShapeCommand.TextLabel(170F, 78F, "R3", false)
            });


            RegisterCleanVectorTokenShape(74, "A|B|C|D|R1", new RebarShapeCommand[] {
                RebarShapeCommand.Line(48F, 70F, 48F, 52F),
                RebarShapeCommand.Arc(62F, 52F, 14F, 180F, 90F),
                RebarShapeCommand.Line(62F, 38F, 118F, 38F),
                RebarShapeCommand.Arc(118F, 52F, 14F, 270F, 90F),
                RebarShapeCommand.Line(132F, 52F, 132F, 70F),
                RebarShapeCommand.TextLabel(38F, 60F, "A", false),
                RebarShapeCommand.TextLabel(60F, 29F, "B", false),
                RebarShapeCommand.TextLabel(90F, 25F, "C", false),
                RebarShapeCommand.TextLabel(120F, 29F, "B", false),
                RebarShapeCommand.TextLabel(142F, 60F, "D", false),
                RebarShapeCommand.TextLabel(90F, 53F, "R1", false)
            });

            RegisterCleanVectorTokenShape(274, "A|B|C|D|R1|R2", new RebarShapeCommand[] {
                RebarShapeCommand.Line(50F, 72F, 50F, 42F),
                RebarShapeCommand.Line(50F, 42F, 103F, 42F),
                RebarShapeCommand.Arc(103F, 50F, 8F, 270F, 90F),
                RebarShapeCommand.Line(111F, 50F, 111F, 72F),
                RebarShapeCommand.TextLabel(40F, 57F, "A", false),
                RebarShapeCommand.TextLabel(75F, 29F, "B", false),
                RebarShapeCommand.TextLabel(116F, 40F, "C", false),
                RebarShapeCommand.TextLabel(121F, 61F, "D", false),
                RebarShapeCommand.TextLabel(86F, 49F, "R1", false),
                RebarShapeCommand.TextLabel(96F, 63F, "R2", false)
            });
        }

        private void RegisterCleanVectorTokenShape(int shapeNo, string fields, RebarShapeCommand[] commands)
        {
            RebarShapeInfo shape = FindBuiltInShapeByNo(shapeNo);

            if (shape == null)
            {
                return;
            }

            shape.FieldsText = fields == null ? "" : fields;
            shape.OptionText = "";
            shape.VectorStatus = "CLEAN_VECTOR_TOKEN_VERIFIED";
            shape.Commands.Clear();

            if (commands == null)
            {
                return;
            }

            int i;

            for (i = 0; i < commands.Length; i++)
            {
                if (commands[i] != null)
                {
                    shape.Commands.Add(commands[i]);
                }
            }
        }

        private bool HasRoundField(string fields)
        {
            if (fields == null)
            {
                return false;
            }

            return fields.IndexOf("R1", StringComparison.OrdinalIgnoreCase) >= 0
                || fields.IndexOf("R2", StringComparison.OrdinalIgnoreCase) >= 0
                || fields.IndexOf("R3", StringComparison.OrdinalIgnoreCase) >= 0
                || fields.IndexOf("R4", StringComparison.OrdinalIgnoreCase) >= 0;
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
