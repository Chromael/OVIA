using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(OVIA.AutoCAD_2027.OviaCommands))]

namespace OVIA.AutoCAD_2027
{
    public class OviaCommands
    {
        private const string OviaBoxLayerName = "OVIA_SELECT_BOX";
        private const string OviaBoxLineTypeName = "Continuous";

        private List<OviaHeaderColumn> lastDetectedHeaderColumns = new List<OviaHeaderColumn>();

        [CommandMethod("OVIAHELLO")]
        public void OviaHello()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            ed.WriteMessage("\n");
            ed.WriteMessage("====================================\n");
            ed.WriteMessage("OVIA AutoCAD 2027 플러그인 로드 성공\n");
            ed.WriteMessage("명령어 OVIAHELLO가 정상 실행되었습니다.\n");
            ed.WriteMessage("====================================\n");
        }

        [CommandMethod("OVIADWGINFO")]
        public void OviaDwgInfo()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n");
            ed.WriteMessage("====================================\n");
            ed.WriteMessage("OVIA 현재 도면 정보\n");
            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("문서 이름 : " + SafeText(doc.Name) + "\n");
            ed.WriteMessage("도면 파일 : " + SafeText(db.Filename) + "\n");
            ed.WriteMessage("DWG 버전  : " + db.LastSavedAsVersion.ToString() + "\n");
            ed.WriteMessage("====================================\n");
        }

        [CommandMethod("OVIATEXTSCAN")]
        public void OviaTextScan()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            int dbTextCount = 0;
            int mTextCount = 0;
            int totalCount = 0;

            List<string> samples = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                {
                    ed.WriteMessage("\nOVIA 오류: BlockTable을 읽을 수 없습니다.\n");
                    return;
                }

                ScanBlockTableRecord(tr, blockTable, BlockTableRecord.ModelSpace, ref dbTextCount, ref mTextCount, ref totalCount, samples);
                ScanBlockTableRecord(tr, blockTable, BlockTableRecord.PaperSpace, ref dbTextCount, ref mTextCount, ref totalCount, samples);

                tr.Commit();
            }

            ed.WriteMessage("\n");
            ed.WriteMessage("====================================\n");
            ed.WriteMessage("OVIA Text / MText 스캔 결과\n");
            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("DBText 개수 : " + dbTextCount.ToString() + "\n");
            ed.WriteMessage("MText 개수  : " + mTextCount.ToString() + "\n");
            ed.WriteMessage("전체 개수   : " + totalCount.ToString() + "\n");

            if (samples.Count > 0)
            {
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("샘플 텍스트 최대 20개\n");

                int i;

                for (i = 0; i < samples.Count; i++)
                {
                    ed.WriteMessage((i + 1).ToString() + ". " + samples[i] + "\n");
                }
            }
            else
            {
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("읽을 수 있는 Text / MText가 없습니다.\n");
            }

            ed.WriteMessage("====================================\n");
        }

        [CommandMethod("OVIATEXTCSV")]
        public void OviaTextCsv()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<OviaTextRow> rows = new List<OviaTextRow>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                {
                    ed.WriteMessage("\nOVIA 오류: BlockTable을 읽을 수 없습니다.\n");
                    return;
                }

                CollectTextRows(tr, blockTable, BlockTableRecord.ModelSpace, "ModelSpace", rows);
                CollectTextRows(tr, blockTable, BlockTableRecord.PaperSpace, "PaperSpace", rows);

                tr.Commit();
            }

            if (rows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: CSV로 저장할 Text / MText가 없습니다.\n");
                return;
            }

            SortRowsTopToBottomLeftToRight(rows);
            ApplySimpleRowNumbers(rows);

            string filePath = CreateCsvFilePath(db, "OVIA_TextScan");

            try
            {
                WriteCsv(filePath, rows);

                ed.WriteMessage("\n");
                ed.WriteMessage("====================================\n");
                ed.WriteMessage("OVIA Text / MText CSV 저장 완료\n");
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("저장 개수 : " + rows.Count.ToString() + "\n");
                ed.WriteMessage("저장 위치 : " + filePath + "\n");
                ed.WriteMessage("====================================\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nOVIA CSV 저장 오류: " + ex.Message + "\n");
            }
        }

        [CommandMethod("OVIABOX")]
        public void OviaBox()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            object previousOsMode = EnableOviaTableSnapMode(ed);

            try
            {
                PromptPointOptions firstPointOptions = new PromptPointOptions(
                    "\nOVIA 선택박스 시작점: 표 왼쪽 경계선과 시작 행의 위쪽 가로선이 만나는 교차점에 맞춰 클릭하세요: "
                );

                firstPointOptions.AllowNone = false;

                PromptPointResult firstPointResult = ed.GetPoint(firstPointOptions);

                if (firstPointResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOVIA: 선택박스 생성이 취소되었습니다.\n");
                    return;
                }

                PromptCornerOptions secondPointOptions = new PromptCornerOptions(
                    "\nOVIA 선택박스 끝점: 표 오른쪽 경계선과 끝 행의 아래쪽 가로선이 만나는 교차점에 맞춰 클릭하세요: ",
                    firstPointResult.Value
                );

                PromptPointResult secondPointResult = ed.GetCorner(secondPointOptions);

                if (secondPointResult.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOVIA: 선택박스 생성이 취소되었습니다.\n");
                    return;
                }

                Point3d boxPoint1 = firstPointResult.Value;
                Point3d boxPoint2 = secondPointResult.Value;
                bool isSnapped = TrySnapOviaBoxToTableLines(ed, db, firstPointResult.Value, secondPointResult.Value, out boxPoint1, out boxPoint2);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId dashedLineTypeId = EnsureDashedLineType(db, tr);
                    EnsureOviaBoxLayer(db, tr, dashedLineTypeId, false);
                    DeleteExistingOviaBoxes(db, tr);

                    CreateOviaBoxEntity(db, tr, boxPoint1, boxPoint2, dashedLineTypeId);

                    EnsureOviaBoxLayer(db, tr, dashedLineTypeId, true);
                    tr.Commit();
                }

                ed.WriteMessage("\n");
                ed.WriteMessage("====================================\n");
                ed.WriteMessage("OVIA 선택박스 생성 완료\n");
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("표시 형태 : 밝은 노란색 / 매우 두꺼운 실선\n");
                ed.WriteMessage("박스 개수 : 기존 박스 삭제 후 1개만 유지\n");
                ed.WriteMessage("정확 선택 : AutoCAD 객체스냅은 끝점/교차점만 임시 적용하여 내부선으로 붙는 문제를 줄임\n");
                ed.WriteMessage("라인 스냅 : " + (isSnapped ? "표 전체를 이루는 실제 테이블 라인에 자동 보정됨" : "스냅 가능한 테이블 라인 없음, 클릭 좌표 사용") + "\n");
                ed.WriteMessage("번호 보정 : 왼쪽 경계가 번호 컬럼 안쪽 선으로 붙으면 클릭 지점/직전 세로선까지 강제 확장\n");
                ed.WriteMessage("주의      : 철근형상 내부의 작은 치수선이 아니라 표 외곽/행 경계선 교차점을 클릭하세요.\n");
                ed.WriteMessage("편집 방식 : 잠금 없음, 필요 시 OVIA 전용 조정 명령으로 직사각형 유지\n");
                ed.WriteMessage("상단 조정 : OVIABOXTOP\n");
                ed.WriteMessage("하단 조정 : OVIABOXBOTTOM\n");
                ed.WriteMessage("좌측 조정 : OVIABOXLEFT\n");
                ed.WriteMessage("우측 조정 : OVIABOXRIGHT\n");
                ed.WriteMessage("이동      : OVIABOXMOVE\n");
                ed.WriteMessage("추출      : OVIABOXTABLE\n");
                ed.WriteMessage("====================================\n");
            }
            finally
            {
                RestoreOviaTableSnapMode(previousOsMode);
            }
        }

        private object EnableOviaTableSnapMode(Editor ed)
        {
            object previousOsMode = null;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");

                /*
                 * OVIA 정확 선택 모드:
                 * 1   = Endpoint
                 * 32  = Intersection
                 *
                 * 이전 버전에서는 Nearest(512)까지 켰기 때문에 사용자가 번호 컬럼 왼쪽을 찍어도
                 * AutoCAD가 더 가까운 내부 세로선으로 스냅시키는 문제가 있었습니다.
                 * 이번 버전부터는 Endpoint + Intersection만 임시 적용합니다.
                 */
                Application.SetSystemVariable("OSMODE", 33);

                if (ed != null)
                {
                    ed.WriteMessage("\nOVIA 정확 선택 모드: 끝점/교차점 객체스냅을 임시 적용했습니다.\n");
                    ed.WriteMessage("번호 컬럼 왼쪽 외곽선과 행 경계선이 만나는 교차점에서 시작하고, 오른쪽 경계선과 끝 행 경계선에서 마무리하세요.\n");
                }
            }
            catch
            {
            }

            return previousOsMode;
        }

        private void RestoreOviaTableSnapMode(object previousOsMode)
        {
            if (previousOsMode == null)
            {
                return;
            }

            try
            {
                Application.SetSystemVariable("OSMODE", previousOsMode);
            }
            catch
            {
            }
        }

        private bool TrySnapOviaBoxToTableLines(Editor ed, Database db, Point3d rawPoint1, Point3d rawPoint2, out Point3d snappedPoint1, out Point3d snappedPoint2)
        {
            snappedPoint1 = rawPoint1;
            snappedPoint2 = rawPoint2;

            if (ed == null || db == null)
            {
                return false;
            }

            double minX = Math.Min(rawPoint1.X, rawPoint2.X);
            double maxX = Math.Max(rawPoint1.X, rawPoint2.X);
            double minY = Math.Min(rawPoint1.Y, rawPoint2.Y);
            double maxY = Math.Max(rawPoint1.Y, rawPoint2.Y);
            double width = maxX - minX;
            double height = maxY - minY;
            double longSide = Math.Max(width, height);

            if (longSide <= 0)
            {
                return false;
            }

            double paddingX = Math.Max(width * 0.12, 20.0);
            double paddingY = Math.Max(height * 0.25, 20.0);
            double axisTolerance = Math.Max(Math.Min(width, height) * 0.006, 0.5);
            double mergeTolerance = Math.Max(Math.Min(width, height) * 0.008, 1.0);

            Point3d searchPoint1 = new Point3d(minX - paddingX, minY - paddingY, 0);
            Point3d searchPoint2 = new Point3d(maxX + paddingX, maxY + paddingY, 0);

            List<OviaGridLineSegment> segments = ExtractGridLineSegmentsByWindow(ed, db, searchPoint1, searchPoint2);

            if (segments.Count == 0)
            {
                return false;
            }

            /*
             * 기존 단순 스냅은 철근형상 셀 내부의 작은 치수선까지 스냅 후보로 볼 수 있었습니다.
             * 이번 방식은 같은 X/Y 좌표의 선 조각들이 선택 영역 폭/높이를 충분히 덮을 때만
             * 실제 표 경계선으로 인정합니다.
             */
            double minHorizontalSegmentLength = Math.Max(width * 0.02, 0.5);
            double minVerticalSegmentLength = Math.Max(height * 0.02, 0.5);
            double minHorizontalCoverage = Math.Max(width * 0.55, width - (width * 0.20));
            double minVerticalCoverage = Math.Max(height * 0.45, height - (height * 0.35));

            List<double> verticalXs = ExtractCoveredGridCoordinates(
                segments,
                true,
                axisTolerance,
                mergeTolerance,
                minVerticalSegmentLength,
                minVerticalCoverage,
                minY,
                maxY
            );

            List<double> horizontalYs = ExtractCoveredGridCoordinates(
                segments,
                false,
                axisTolerance,
                mergeTolerance,
                minHorizontalSegmentLength,
                minHorizontalCoverage,
                minX,
                maxX
            );

            /*
             * 번호 컬럼 왼쪽 외곽선은 도면에 따라 짧은 선 조각으로 분리되어 있어
             * 표 전체 커버리지 기준에서는 빠질 수 있습니다.
             * 선택박스 왼쪽 확장 판단에만 사용할 더 느슨한 세로선 후보를 별도로 수집합니다.
             */
            List<double> softVerticalXs = ExtractCoveredGridCoordinates(
                segments,
                true,
                axisTolerance,
                mergeTolerance,
                minVerticalSegmentLength,
                Math.Max(height * 0.18, 2.0),
                minY,
                maxY
            );

            List<double> leftBoundaryXs = new List<double>();
            leftBoundaryXs.AddRange(verticalXs);
            leftBoundaryXs.AddRange(softVerticalXs);
            leftBoundaryXs = MergeGridCoordinates(leftBoundaryXs, mergeTolerance, true);

            if (verticalXs.Count == 0 && horizontalYs.Count == 0)
            {
                return false;
            }

            double xSnapDistance = Math.Max(width * 0.08, 8.0);
            double ySnapDistance = Math.Max(height * 0.12, 8.0);

            double x1 = SnapCoordinate(rawPoint1.X, verticalXs, xSnapDistance);
            double y1 = SnapCoordinate(rawPoint1.Y, horizontalYs, ySnapDistance);
            double x2 = SnapCoordinate(rawPoint2.X, verticalXs, xSnapDistance);
            double y2 = SnapCoordinate(rawPoint2.Y, horizontalYs, ySnapDistance);

            /*
             * 중요 보정:
             * AutoCAD 객체스냅이 번호 컬럼의 왼쪽 외곽선이 아니라,
             * 번호 컬럼 오른쪽 내부 세로선으로 붙는 경우가 있습니다.
             * 이 경우 선택박스가 번호 컬럼을 제외한 채 생성되어 번호 데이터가 누락됩니다.
             *
             * 따라서 스냅된 왼쪽 경계 바로 왼쪽에 가까운 표 세로선이 있으면
             * 그 선을 실제 표의 왼쪽 외곽선으로 보고 선택박스를 한 칸 확장합니다.
             */
            double snappedLeft = Math.Min(x1, x2);
            double snappedRight = Math.Max(x1, x2);
            double snappedBottom = Math.Min(y1, y2);
            double snappedTop = Math.Max(y1, y2);

            double rawLeft = Math.Min(rawPoint1.X, rawPoint2.X);
            double expandedLeft = ExpandLeftBoundaryToPreviousGridLine(snappedLeft, leftBoundaryXs, width);
            expandedLeft = ExpandLeftBoundaryByClickedSide(rawLeft, expandedLeft, leftBoundaryXs, width);

            if (expandedLeft < snappedLeft)
            {
                snappedLeft = expandedLeft;
            }

            if (rawPoint1.X <= rawPoint2.X)
            {
                x1 = snappedLeft;
                x2 = snappedRight;
            }
            else
            {
                x1 = snappedRight;
                x2 = snappedLeft;
            }

            if (rawPoint1.Y <= rawPoint2.Y)
            {
                y1 = snappedBottom;
                y2 = snappedTop;
            }
            else
            {
                y1 = snappedTop;
                y2 = snappedBottom;
            }

            snappedPoint1 = new Point3d(x1, y1, rawPoint1.Z);
            snappedPoint2 = new Point3d(x2, y2, rawPoint2.Z);

            return Math.Abs(x1 - rawPoint1.X) > 0.0001 ||
                   Math.Abs(y1 - rawPoint1.Y) > 0.0001 ||
                   Math.Abs(x2 - rawPoint2.X) > 0.0001 ||
                   Math.Abs(y2 - rawPoint2.Y) > 0.0001;
        }

        private void CollectAxisLinesFromEntity(
            Transaction tr,
            Entity entity,
            Matrix3d transform,
            List<double> verticalXs,
            List<double> horizontalYs,
            double tolerance,
            int depth
        )
        {
            if (entity == null)
            {
                return;
            }

            if (depth > 8)
            {
                return;
            }

            if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Line line = entity as Line;

            if (line != null)
            {
                AddAxisLineCandidate(
                    line.StartPoint.TransformBy(transform),
                    line.EndPoint.TransformBy(transform),
                    verticalXs,
                    horizontalYs,
                    tolerance
                );

                return;
            }

            Polyline polyline = entity as Polyline;

            if (polyline != null)
            {
                int count = polyline.NumberOfVertices;
                int i;

                for (i = 0; i < count - 1; i++)
                {
                    AddAxisLineCandidate(
                        polyline.GetPoint3dAt(i).TransformBy(transform),
                        polyline.GetPoint3dAt(i + 1).TransformBy(transform),
                        verticalXs,
                        horizontalYs,
                        tolerance
                    );
                }

                if (polyline.Closed && count > 1)
                {
                    AddAxisLineCandidate(
                        polyline.GetPoint3dAt(count - 1).TransformBy(transform),
                        polyline.GetPoint3dAt(0).TransformBy(transform),
                        verticalXs,
                        horizontalYs,
                        tolerance
                    );
                }

                return;
            }

            BlockReference blockReference = entity as BlockReference;

            if (blockReference != null)
            {
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                if (blockRecord != null)
                {
                    Matrix3d nextTransform = transform * blockReference.BlockTransform;

                    foreach (ObjectId childId in blockRecord)
                    {
                        Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                        if (childEntity == null)
                        {
                            continue;
                        }

                        CollectAxisLinesFromEntity(
                            tr,
                            childEntity,
                            nextTransform,
                            verticalXs,
                            horizontalYs,
                            tolerance,
                            depth + 1
                        );
                    }
                }

                CollectAxisLinesFromExplodedBlock(
                    blockReference,
                    verticalXs,
                    horizontalYs,
                    tolerance,
                    depth + 1
                );

                return;
            }
        }

        private void CollectAxisLinesFromExplodedBlock(
            BlockReference blockReference,
            List<double> verticalXs,
            List<double> horizontalYs,
            double tolerance,
            int depth
        )
        {
            if (blockReference == null || depth > 8)
            {
                return;
            }

            DBObjectCollection explodedObjects = new DBObjectCollection();

            try
            {
                blockReference.Explode(explodedObjects);
            }
            catch
            {
                return;
            }

            foreach (DBObject dbObject in explodedObjects)
            {
                Entity explodedEntity = dbObject as Entity;

                if (explodedEntity == null)
                {
                    if (dbObject != null)
                    {
                        dbObject.Dispose();
                    }

                    continue;
                }

                try
                {
                    Line line = explodedEntity as Line;

                    if (line != null)
                    {
                        AddAxisLineCandidate(
                            line.StartPoint,
                            line.EndPoint,
                            verticalXs,
                            horizontalYs,
                            tolerance
                        );

                        continue;
                    }

                    Polyline polyline = explodedEntity as Polyline;

                    if (polyline != null)
                    {
                        int count = polyline.NumberOfVertices;
                        int i;

                        for (i = 0; i < count - 1; i++)
                        {
                            AddAxisLineCandidate(
                                polyline.GetPoint3dAt(i),
                                polyline.GetPoint3dAt(i + 1),
                                verticalXs,
                                horizontalYs,
                                tolerance
                            );
                        }

                        if (polyline.Closed && count > 1)
                        {
                            AddAxisLineCandidate(
                                polyline.GetPoint3dAt(count - 1),
                                polyline.GetPoint3dAt(0),
                                verticalXs,
                                horizontalYs,
                                tolerance
                            );
                        }

                        continue;
                    }

                    BlockReference nestedBlock = explodedEntity as BlockReference;

                    if (nestedBlock != null)
                    {
                        CollectAxisLinesFromExplodedBlock(
                            nestedBlock,
                            verticalXs,
                            horizontalYs,
                            tolerance,
                            depth + 1
                        );

                        continue;
                    }
                }
                finally
                {
                    explodedEntity.Dispose();
                }
            }
        }

        private void AddAxisLineCandidate(
            Point3d point1,
            Point3d point2,
            List<double> verticalXs,
            List<double> horizontalYs,
            double tolerance
        )
        {
            double dx = Math.Abs(point1.X - point2.X);
            double dy = Math.Abs(point1.Y - point2.Y);

            if (dx <= tolerance && dy > tolerance)
            {
                verticalXs.Add((point1.X + point2.X) / 2.0);
                return;
            }

            if (dy <= tolerance && dx > tolerance)
            {
                horizontalYs.Add((point1.Y + point2.Y) / 2.0);
                return;
            }
        }

        private double SnapCoordinate(double value, List<double> candidates, double maxDistance)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return value;
            }

            double best = value;
            double bestDistance = maxDistance;
            int i;

            for (i = 0; i < candidates.Count; i++)
            {
                double distance = Math.Abs(value - candidates[i]);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidates[i];
                }
            }

            return best;
        }

        private double ExpandLeftBoundaryToPreviousGridLine(double currentLeft, List<double> verticalXs, double selectedWidth)
        {
            if (verticalXs == null || verticalXs.Count == 0)
            {
                return currentLeft;
            }

            List<double> sortedXs = new List<double>(verticalXs);
            sortedXs.Sort();

            double mergeTolerance = Math.Max(selectedWidth * 0.002, 0.5);
            List<double> uniqueXs = new List<double>();
            int i;

            for (i = 0; i < sortedXs.Count; i++)
            {
                if (uniqueXs.Count == 0 || Math.Abs(sortedXs[i] - uniqueXs[uniqueXs.Count - 1]) > mergeTolerance)
                {
                    uniqueXs.Add(sortedXs[i]);
                }
            }

            double previous = currentLeft;
            bool hasPrevious = false;

            for (i = 0; i < uniqueXs.Count; i++)
            {
                if (uniqueXs[i] < currentLeft - mergeTolerance)
                {
                    previous = uniqueXs[i];
                    hasPrevious = true;
                    continue;
                }

                break;
            }

            if (!hasPrevious)
            {
                return currentLeft;
            }

            double gap = currentLeft - previous;

            /*
             * 번호 컬럼은 보통 전체 표 폭의 5~15% 정도입니다.
             * 너무 멀리 떨어진 선까지 포함하면 좌측의 다른 표나 도면선이 들어올 수 있으므로
             * 가까운 직전 세로선만 제한적으로 확장합니다.
             */
            double maxNumberColumnWidth = Math.Max(selectedWidth * 0.22, 12.0);

            if (gap > mergeTolerance && gap <= maxNumberColumnWidth)
            {
                return previous;
            }

            return currentLeft;
        }

        private double ExpandLeftBoundaryByClickedSide(double rawLeft, double currentLeft, List<double> verticalXs, double selectedWidth)
        {
            if (verticalXs == null || verticalXs.Count == 0)
            {
                return currentLeft;
            }

            List<double> sortedXs = new List<double>(verticalXs);
            sortedXs.Sort();

            double mergeTolerance = Math.Max(selectedWidth * 0.002, 0.5);
            double maxNumberColumnWidth = Math.Max(selectedWidth * 0.30, 18.0);
            double best = currentLeft;
            int i;

            /*
             * 사용자가 실제로 클릭한 X가 현재 선택박스 왼쪽 경계보다 왼쪽이라면,
             * 사용자의 의도는 번호 컬럼 왼쪽 외곽선을 포함하는 것입니다.
             * 따라서 rawLeft 부근 또는 currentLeft 바로 왼쪽의 세로선 중 가까운 것을 우선 선택합니다.
             */
            for (i = 0; i < sortedXs.Count; i++)
            {
                double x = sortedXs[i];

                if (x >= currentLeft - mergeTolerance)
                {
                    break;
                }

                double gapFromCurrent = currentLeft - x;

                if (gapFromCurrent <= maxNumberColumnWidth)
                {
                    if (best == currentLeft || x < best)
                    {
                        best = x;
                    }
                }
            }

            if (best < currentLeft)
            {
                return best;
            }

            /*
             * 세로선 후보가 누락된 경우에도 사용자가 currentLeft보다 왼쪽을 클릭했다면
             * 최소한 클릭 지점까지는 선택박스를 넓혀 번호 텍스트가 빠지지 않도록 합니다.
             */
            if (rawLeft < currentLeft - mergeTolerance)
            {
                double clickGap = currentLeft - rawLeft;

                if (clickGap <= maxNumberColumnWidth)
                {
                    return rawLeft;
                }
            }

            return currentLeft;
        }

        [CommandMethod("OVIABOXCSV")]
        public void OviaBoxCsv()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d minPoint;
            Point3d maxPoint;
            int boxCount = 0;

            bool hasBox = GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 도면에서 OVIA 선택박스를 찾지 못했습니다.\n");
                ed.WriteMessage("먼저 OVIABOX 명령어로 선택박스를 생성해주세요.\n");
                return;
            }

            FixOviaBoxRectangle(db);

            hasBox = GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 선택박스 보정 중 오류가 발생했습니다.\n");
                return;
            }

            List<OviaTextRow> rows = ExtractRowsByWindow(ed, db, minPoint, maxPoint);

            if (rows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: 선택박스 안에서 Text / MText를 찾지 못했습니다.\n");
                return;
            }

            SortRowsTopToBottomLeftToRight(rows);
            ApplySimpleRowNumbers(rows);

            string filePath = CreateCsvFilePath(db, "OVIA_BoxPick");

            try
            {
                WriteCsv(filePath, rows);

                ed.WriteMessage("\n");
                ed.WriteMessage("====================================\n");
                ed.WriteMessage("OVIA 선택박스 문자 추출 완료\n");
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("선택박스 기준 : 가로 전체 폭 + 세로 선택 구간\n");
                ed.WriteMessage("선택 문자 개수 : " + rows.Count.ToString() + "\n");
                ed.WriteMessage("저장 위치     : " + filePath + "\n");

                WritePreview(ed, rows);

                ed.WriteMessage("====================================\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nOVIA 선택박스 CSV 저장 오류: " + ex.Message + "\n");
            }
        }


        [CommandMethod("OVIABOXTABLE")]
        public void OviaBoxTable()
        {
            /*
             * OVIA 2026-05-22:
             * 사용자는 OVIABOXTABLE 하나만 사용합니다.
             * 내부에서는 표 라인/셀 기반 추출을 먼저 시도하고,
             * 실패하면 기존 문자 좌표 기반 추출로 자동 보정합니다.
             */
            RunSmartBoxTableExtraction("OVIABOXTABLE");
        }


        [CommandMethod("OVIAGRIDTABLE")]
        public void OviaGridTable()
        {
            /*
             * 개발/테스트 호환용 별칭입니다.
             * 실제 동작은 OVIABOXTABLE과 동일하게 통합했습니다.
             */
            RunSmartBoxTableExtraction("OVIAGRIDTABLE");
        }


        private void RunSmartBoxTableExtraction(string commandName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d selectedMinPoint;
            Point3d selectedMaxPoint;
            int boxCount = 0;

            bool hasBox = GetOviaBoxExtents(db, out selectedMinPoint, out selectedMaxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 도면에서 OVIA 선택박스를 찾지 못했습니다.\n");
                ed.WriteMessage("먼저 OVIABOX 명령어로 집계표의 가로 전체 폭과 원하는 세로 행 구간을 선택해주세요.\n");
                return;
            }

            FixOviaBoxRectangle(db);

            hasBox = GetOviaBoxExtents(db, out selectedMinPoint, out selectedMaxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 선택박스 보정 중 오류가 발생했습니다.\n");
                return;
            }

            Point3d analysisMinPoint;
            Point3d analysisMaxPoint;

            CreateSmartTableAnalysisWindow(selectedMinPoint, selectedMaxPoint, out analysisMinPoint, out analysisMaxPoint);

            List<OviaTextRow> selectedTextRows = ExtractRowsByWindow(ed, db, selectedMinPoint, selectedMaxPoint);
            List<OviaTextRow> analysisTextRows = ExtractRowsByWindow(ed, db, analysisMinPoint, analysisMaxPoint);
            List<OviaGridLineSegment> analysisGridLines = ExtractGridLineSegmentsByWindow(ed, db, analysisMinPoint, analysisMaxPoint);

            if (selectedTextRows.Count == 0 && analysisTextRows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: 선택박스 안에서 Text / MText를 찾지 못했습니다.\n");
                return;
            }

            string diagnostic = "";
            bool usedGridParser = false;
            List<OviaBarTableRow> tableRows = BuildOviaGridTableRows(
                analysisTextRows,
                analysisGridLines,
                analysisMinPoint,
                analysisMaxPoint,
                selectedMinPoint,
                selectedMaxPoint,
                out diagnostic
            );

            if (tableRows.Count > 0)
            {
                usedGridParser = true;
            }

            if (tableRows.Count == 0)
            {
                /*
                 * 표 선 기반 분석이 실패하면 기존 좌표 기반 파서로 자동 전환합니다.
                 * 이 경우에도 파일명은 OVIA_BoxTable로 저장하여 Desktop 자동 입력 흐름을 유지합니다.
                 */
                SortRowsTopToBottomLeftToRight(selectedTextRows);
                ApplySimpleRowNumbers(selectedTextRows);
                tableRows = BuildOviaBarTableRows(selectedTextRows);
            }

            if (tableRows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: 집계표로 변환할 데이터 행을 찾지 못했습니다.\n");
                ed.WriteMessage("선택박스가 표의 가로 전체 폭과 원하는 세로 행 구간을 포함하는지 확인해주세요.\n");
                return;
            }

            string filePath = CreateCsvFilePath(db, "OVIA_BoxTable");

            try
            {
                CaptureCadShapeFilesForRows(ed, db, filePath, tableRows);
                WriteBarTableCsv(filePath, tableRows);

                ed.WriteMessage("\n");
                ed.WriteMessage("====================================\n");
                ed.WriteMessage("OVIA 철근 집계표 스마트 추출 완료\n");
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("실행 명령     : " + commandName + "\n");
                ed.WriteMessage("추출 방식     : " + (usedGridParser ? "표 라인/셀 기반" : "기존 문자 좌표 기반 보정") + "\n");
                ed.WriteMessage("선택 문자 수   : " + selectedTextRows.Count.ToString() + "\n");
                ed.WriteMessage("분석 문자 수   : " + analysisTextRows.Count.ToString() + "\n");
                ed.WriteMessage("표 선 후보 수  : " + analysisGridLines.Count.ToString() + "\n");
                ed.WriteMessage("변환 행 개수   : " + tableRows.Count.ToString() + "\n");

                if (diagnostic != "")
                {
                    ed.WriteMessage("분석 정보     : " + diagnostic + "\n");
                }

                ed.WriteMessage("저장 위치     : " + filePath + "\n");
                WriteBarTablePreview(ed, tableRows);
                ed.WriteMessage("====================================\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nOVIA 집계표 CSV 저장 오류: " + ex.Message + "\n");
            }
        }



        [CommandMethod("OVIABOXFIX")]
        public void OviaBoxFix()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            FixOviaBoxRectangle(doc.Database);
            doc.Editor.WriteMessage("\nOVIA 선택박스를 직사각형으로 보정했습니다.\n");
        }

        [CommandMethod("OVIABOXTOP")]
        public void OviaBoxTop()
        {
            AdjustOviaBoxEdge("TOP");
        }

        [CommandMethod("OVIABOXBOTTOM")]
        public void OviaBoxBottom()
        {
            AdjustOviaBoxEdge("BOTTOM");
        }

        [CommandMethod("OVIABOXLEFT")]
        public void OviaBoxLeft()
        {
            AdjustOviaBoxEdge("LEFT");
        }

        [CommandMethod("OVIABOXRIGHT")]
        public void OviaBoxRight()
        {
            AdjustOviaBoxEdge("RIGHT");
        }

        [CommandMethod("OVIABOXMOVE")]
        public void OviaBoxMove()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d minPoint;
            Point3d maxPoint;
            int boxCount;

            if (!GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount))
            {
                ed.WriteMessage("\nOVIA: 이동할 선택박스가 없습니다. 먼저 OVIABOX를 실행해주세요.\n");
                return;
            }

            PromptPointResult baseResult = ed.GetPoint("\nOVIA 선택박스 이동 기준점을 클릭하세요: ");

            if (baseResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nOVIA: 이동이 취소되었습니다.\n");
                return;
            }

            PromptPointOptions targetOptions = new PromptPointOptions("\nOVIA 선택박스를 이동할 위치를 클릭하세요: ");
            targetOptions.BasePoint = baseResult.Value;
            targetOptions.UseBasePoint = true;

            PromptPointResult targetResult = ed.GetPoint(targetOptions);

            if (targetResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nOVIA: 이동이 취소되었습니다.\n");
                return;
            }

            double dx = targetResult.Value.X - baseResult.Value.X;
            double dy = targetResult.Value.Y - baseResult.Value.Y;

            Point3d newMin = new Point3d(minPoint.X + dx, minPoint.Y + dy, minPoint.Z);
            Point3d newMax = new Point3d(maxPoint.X + dx, maxPoint.Y + dy, maxPoint.Z);

            RecreateOviaBoxFromMinMax(db, newMin, newMax);

            ed.WriteMessage("\nOVIA 선택박스를 이동했습니다.\n");
        }

        [CommandMethod("OVIABOXDEL")]
        public void OviaBoxDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            int deletedCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dashedLineTypeId = EnsureDashedLineType(db, tr);
                EnsureOviaBoxLayer(db, tr, dashedLineTypeId, false);
                deletedCount = DeleteExistingOviaBoxes(db, tr);
                EnsureOviaBoxLayer(db, tr, dashedLineTypeId, true);
                tr.Commit();
            }

            ed.WriteMessage("\nOVIA 선택박스 삭제 완료: " + deletedCount.ToString() + "개\n");
        }


        private List<OviaBarTableRow> BuildOviaBarTableRows(List<OviaTextRow> sourceRows)
        {
            List<OviaBarTableRow> result = new List<OviaBarTableRow>();
            lastDetectedHeaderColumns = new List<OviaHeaderColumn>();

            if (sourceRows == null || sourceRows.Count == 0)
            {
                return result;
            }

            List<List<OviaTextRow>> groupedRows = GroupRowsByY(sourceRows);

            OviaHeaderMap headerMap = DetectHeaderMap(groupedRows, sourceRows);

            if (headerMap != null && headerMap.Columns != null && headerMap.Columns.Count >= 3)
            {
                lastDetectedHeaderColumns = headerMap.Columns;
                result = BuildRowsByHeaderMap(groupedRows, headerMap);

                int mappedNo = 1;
                int mappedIndex;

                for (mappedIndex = 0; mappedIndex < result.Count; mappedIndex++)
                {
                    result[mappedIndex].No = mappedNo;
                    mappedNo++;
                }

                return result;
            }

            /*
             * 헤더가 선택 영역에 포함되지 않았거나 회사별 표 양식을 아직 인식하지 못한 경우에는
             * 기존 1차 파서로 처리합니다.
             */
            lastDetectedHeaderColumns = CreateFallbackHeaderColumns();

            double minX = GetMinX(sourceRows);
            double maxX = GetMaxX(sourceRows);

            int i;

            for (i = 0; i < groupedRows.Count; i++)
            {
                List<OviaTextRow> line = groupedRows[i];

                if (line == null || line.Count == 0)
                {
                    continue;
                }

                line.Sort(delegate (OviaTextRow a, OviaTextRow b)
                {
                    return a.X.CompareTo(b.X);
                });

                string rawText = JoinRowText(line);

                if (IsHeaderRow(rawText))
                {
                    continue;
                }

                if (IsEmptyNoiseRow(rawText))
                {
                    continue;
                }

                OviaBarTableRow tableRow = ConvertLineToBarTableRow(line, minX, maxX);
                tableRow.SourceRowNo = i + 1;
                tableRow.RawText = rawText;

                if (IsMeaninglessTableRow(tableRow))
                {
                    continue;
                }

                result.Add(tableRow);
            }

            int no = 1;

            for (i = 0; i < result.Count; i++)
            {
                result[i].No = no;
                no++;
            }

            return result;
        }

        private OviaHeaderMap DetectHeaderMap(List<List<OviaTextRow>> groupedRows, List<OviaTextRow> sourceRows)
        {
            if (groupedRows == null || groupedRows.Count == 0 || sourceRows == null || sourceRows.Count == 0)
            {
                return null;
            }

            double minX = GetMinX(sourceRows);
            double maxX = GetMaxX(sourceRows);

            int i;

            for (i = 0; i < groupedRows.Count; i++)
            {
                List<OviaTextRow> line = groupedRows[i];

                if (line == null || line.Count == 0)
                {
                    continue;
                }

                line.Sort(delegate (OviaTextRow a, OviaTextRow b)
                {
                    return a.X.CompareTo(b.X);
                });

                List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

                int j;

                for (j = 0; j < line.Count; j++)
                {
                    string title = CleanHeaderText(line[j].TextValue);
                    string standardKey = ClassifyHeaderTitle(title);

                    if (standardKey == "")
                    {
                        continue;
                    }

                    OviaHeaderColumn existing = FindHeaderColumnByKey(columns, standardKey);

                    if (existing != null)
                    {
                        existing.OriginalTitle = MergeHeaderTitle(existing.OriginalTitle, title);
                        existing.X = (existing.X + line[j].X) / 2.0;
                    }
                    else
                    {
                        OviaHeaderColumn column = new OviaHeaderColumn();
                        column.StandardKey = standardKey;
                        column.OriginalTitle = NormalizeHeaderTitleForOutput(title, standardKey);
                        column.X = line[j].X;

                        columns.Add(column);
                    }
                }

                int score = GetHeaderScore(columns);

                if (score < 3)
                {
                    continue;
                }

                if (!HasImportantHeader(columns))
                {
                    continue;
                }

                columns.Sort(delegate (OviaHeaderColumn a, OviaHeaderColumn b)
                {
                    return a.X.CompareTo(b.X);
                });

                ApplyHeaderColumnBounds(columns, minX, maxX);

                OviaHeaderMap map = new OviaHeaderMap();
                map.HeaderRowIndex = i;
                map.Columns = columns;
                map.MinX = minX;
                map.MaxX = maxX;

                return map;
            }

            return null;
        }

        private List<OviaBarTableRow> BuildRowsByHeaderMap(List<List<OviaTextRow>> groupedRows, OviaHeaderMap headerMap)
        {
            List<OviaBarTableRow> result = new List<OviaBarTableRow>();

            if (groupedRows == null || headerMap == null || headerMap.Columns == null)
            {
                return result;
            }

            int i;

            for (i = headerMap.HeaderRowIndex + 1; i < groupedRows.Count; i++)
            {
                List<OviaTextRow> line = groupedRows[i];

                if (line == null || line.Count == 0)
                {
                    continue;
                }

                line.Sort(delegate (OviaTextRow a, OviaTextRow b)
                {
                    return a.X.CompareTo(b.X);
                });

                string rawText = JoinRowText(line);

                if (IsEmptyNoiseRow(rawText))
                {
                    continue;
                }

                if (IsHeaderRow(rawText))
                {
                    continue;
                }

                OviaBarTableRow row = new OviaBarTableRow();
                row.SourceRowNo = i + 1;
                row.RawText = rawText;
                row.RowType = "DATA";

                if (rawText.IndexOf("총계", StringComparison.OrdinalIgnoreCase) >= 0 || rawText.IndexOf("합계", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.RowType = "TOTAL";
                }
                else if (rawText.IndexOf("소계", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.RowType = "SUBTOTAL";
                }

                int j;

                for (j = 0; j < line.Count; j++)
                {
                    string value = CleanCellText(line[j].TextValue);

                    if (value == "")
                    {
                        continue;
                    }

                    OviaHeaderColumn column = FindHeaderColumnByX(headerMap.Columns, line[j].X);

                    if (column == null)
                    {
                        continue;
                    }


                    ApplyValueByStandardKey(row, column.StandardKey, value);
                }

                if (row.Spec == "")
                {
                    string detectedSpec = DetectSpec(rawText);

                    if (detectedSpec != "")
                    {
                        row.Spec = detectedSpec;
                    }
                }

                if (row.RowType == "DATA")
                {
                    SupplementStandardDataFromRawText(rawText, row);
                }

                if (row.RowType == "SUBTOTAL" || row.RowType == "TOTAL")
                {
                    if (row.TotalLength == "")
                    {
                        row.TotalLength = LastNumberBeforeWeight(rawText);
                    }

                    if (row.TotalWeight == "")
                    {
                        row.TotalWeight = LastDecimalOrLastNumber(rawText);
                    }

                    if (row.MarkNo == "")
                    {
                        if (row.RowType == "SUBTOTAL")
                        {
                            row.MarkNo = "소계";
                        }
                        else
                        {
                            row.MarkNo = "총계";
                        }
                    }
                }

                if (IsMeaninglessTableRow(row))
                {
                    continue;
                }

                result.Add(row);
            }

            return result;
        }

        private List<OviaHeaderColumn> CreateFallbackHeaderColumns()
        {
            List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

            columns.Add(CreateHeaderColumn("MARK_NO", "번호", 0));
            columns.Add(CreateHeaderColumn("SHAPE", "철근형상", 1));
            columns.Add(CreateHeaderColumn("SPEC", "규격", 2));
            columns.Add(CreateHeaderColumn("LENGTH_MM", "길이(mm)", 3));
            columns.Add(CreateHeaderColumn("QUANTITY_EA", "수량(EA)", 4));
            columns.Add(CreateHeaderColumn("TOTAL_LENGTH_M", "총길이(M)", 5));
            columns.Add(CreateHeaderColumn("TOTAL_WEIGHT", "중량(TON)", 6));
            columns.Add(CreateHeaderColumn("NOTE", "비고", 7));

            return columns;
        }

        private void CreateSmartTableAnalysisWindow(Point3d selectedMinPoint, Point3d selectedMaxPoint, out Point3d analysisMinPoint, out Point3d analysisMaxPoint)
        {
            double width = Math.Abs(selectedMaxPoint.X - selectedMinPoint.X);
            double height = Math.Abs(selectedMaxPoint.Y - selectedMinPoint.Y);

            if (width <= 0.0001)
            {
                width = 1.0;
            }

            if (height <= 0.0001)
            {
                height = 1.0;
            }

            /*
             * 사용자는 보통 데이터 행 구간만 선택합니다.
             * OVIA는 같은 표의 헤더를 찾기 위해 선택 영역 위쪽을 자동으로 확장해서 분석합니다.
             * 출력 행은 원래 선택 영역 안의 행만 사용합니다.
             */
            /*
             * 표를 선택할 때 사용자가 왼쪽 번호 컬럼 경계선을 아주 딱 맞게 찍거나,
             * 실제 번호 텍스트의 기준점이 선택박스 경계선보다 살짝 왼쪽에 있으면
             * 번호 컬럼이 누락될 수 있습니다.
             * 그래서 분석 영역은 좌우로 조금 더 넓게 잡되, 출력 행은 기존 선택박스의 Y범위만 사용합니다.
             */
            double leftColumnSearchMargin = Math.Max(width * 0.12, 5.0);
            double rightColumnSearchMargin = Math.Max(width * 0.04, 2.0);
            double bottomMargin = Math.Max(height * 0.03, 1.0);
            double headerSearchHeight = Math.Max(height * 1.60, width * 0.15);

            analysisMinPoint = new Point3d(
                selectedMinPoint.X - leftColumnSearchMargin,
                selectedMinPoint.Y - bottomMargin,
                selectedMinPoint.Z
            );

            analysisMaxPoint = new Point3d(
                selectedMaxPoint.X + rightColumnSearchMargin,
                selectedMaxPoint.Y + headerSearchHeight,
                selectedMaxPoint.Z
            );
        }

        private List<OviaHeaderColumn> CreateGridFallbackHeaderColumns(int columnCount)
        {
            List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

            if (columnCount <= 0)
            {
                return columns;
            }

            string[] keys;
            string[] titles;

            /*
             * 중요 수정 기준
             * ------------------------------------------------------------
             * 도면의 기본 BarList 표는 대부분 아래 순서입니다.
             *   번호 | 철근형상 | 규격 | 길이 | 수량 | 총길이 | 총중량 | 비고
             *
             * 기존 02번 패치에서는 columnCount >= 8이면 무조건 두 번째 컬럼을
             * SHAPE_NO(형상번호/부호명칭)로 가정했습니다. 그 결과 실제 철근형상 칸이
             * 한 칸씩 밀리고, 철근형상 JSON 캡처 범위도 잘못 잡혔습니다.
             *
             * 형상번호/부호명칭 컬럼은 실제 헤더에서 확인될 때만 사용해야 하므로,
             * 헤더 인식이 실패한 fallback 상태에서는 8컬럼까지는 기존 기본 순서를 유지합니다.
             * 9컬럼 이상일 때만 별도 형상번호/부호명칭 컬럼이 있다고 추정합니다.
             */
            if (columnCount == 7)
            {
                /*
                 * 일부 CAD BarList는 다음 순서입니다.
                 *   번호 | 규격 | 형상 | 길이 | 수량 | 총길이 | 중량
                 * 헤더 인식이 약해 fallback으로 들어온 경우에도 이 순서를 우선 보존합니다.
                 */
                keys = new string[] { "MARK_NO", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT" };
                titles = new string[] { "번호", "규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(TON)" };
            }
            else if (columnCount >= 9)
            {
                keys = new string[] { "MARK_NO", "SHAPE_NO", "SHAPE", "SPEC", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "부호/명칭", "철근형상", "규격", "길이(mm)", "수량(EA)", "총길이(M)", "중량(TON)", "비고" };
            }
            else
            {
                keys = new string[] { "MARK_NO", "SHAPE", "SPEC", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "철근형상", "규격", "길이(mm)", "수량(EA)", "총길이(M)", "중량(TON)", "비고" };
            }

            int limit = Math.Min(columnCount, keys.Length);
            int i;

            for (i = 0; i < limit; i++)
            {
                OviaHeaderColumn column = CreateHeaderColumn(keys[i], titles[i], i);
                column.SourceColumnIndex = i;
                columns.Add(column);
            }

            return columns;
        }


        private OviaHeaderColumn CreateHeaderColumn(string key, string title, double x)
        {
            OviaHeaderColumn column = new OviaHeaderColumn();
            column.StandardKey = key;
            column.OriginalTitle = title;
            column.X = x;
            return column;
        }

        private void ApplyHeaderColumnBounds(List<OviaHeaderColumn> columns, double minX, double maxX)
        {
            if (columns == null || columns.Count == 0)
            {
                return;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (columns.Count == 1)
                {
                    columns[i].LeftX = minX;
                    columns[i].RightX = maxX;
                    continue;
                }

                if (i == 0)
                {
                    columns[i].LeftX = minX;
                    columns[i].RightX = (columns[i].X + columns[i + 1].X) / 2.0;
                }
                else if (i == columns.Count - 1)
                {
                    columns[i].LeftX = (columns[i - 1].X + columns[i].X) / 2.0;
                    columns[i].RightX = maxX;
                }
                else
                {
                    columns[i].LeftX = (columns[i - 1].X + columns[i].X) / 2.0;
                    columns[i].RightX = (columns[i].X + columns[i + 1].X) / 2.0;
                }
            }
        }

        private OviaHeaderColumn FindHeaderColumnByKey(List<OviaHeaderColumn> columns, string key)
        {
            if (columns == null || key == null)
            {
                return null;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i].StandardKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return columns[i];
                }
            }

            return null;
        }

        private OviaHeaderColumn FindHeaderColumnByX(List<OviaHeaderColumn> columns, double x)
        {
            if (columns == null || columns.Count == 0)
            {
                return null;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (x >= columns[i].LeftX && x <= columns[i].RightX)
                {
                    return columns[i];
                }
            }

            OviaHeaderColumn nearest = columns[0];
            double nearestDistance = Math.Abs(x - nearest.X);

            for (i = 1; i < columns.Count; i++)
            {
                double distance = Math.Abs(x - columns[i].X);

                if (distance < nearestDistance)
                {
                    nearest = columns[i];
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private int GetHeaderScore(List<OviaHeaderColumn> columns)
        {
            if (columns == null)
            {
                return 0;
            }

            int score = 0;
            bool hasMark = false;
            bool hasSpec = false;
            bool hasLength = false;
            bool hasQty = false;
            bool hasWeight = false;

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (columns[i].StandardKey == "MARK_NO")
                {
                    hasMark = true;
                }
                else if (columns[i].StandardKey == "SPEC")
                {
                    hasSpec = true;
                }
                else if (columns[i].StandardKey == "LENGTH_MM")
                {
                    hasLength = true;
                }
                else if (columns[i].StandardKey == "QUANTITY_EA")
                {
                    hasQty = true;
                }
                else if (columns[i].StandardKey == "TOTAL_WEIGHT")
                {
                    hasWeight = true;
                }
            }

            if (hasMark)
            {
                score++;
            }

            if (hasSpec)
            {
                score++;
            }

            if (hasLength)
            {
                score++;
            }

            if (hasQty)
            {
                score++;
            }

            if (hasWeight)
            {
                score++;
            }

            return score;
        }

        private bool HasImportantHeader(List<OviaHeaderColumn> columns)
        {
            if (columns == null)
            {
                return false;
            }

            bool hasSpec = false;
            bool hasLength = false;
            bool hasQty = false;
            bool hasWeight = false;

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (columns[i].StandardKey == "SPEC")
                {
                    hasSpec = true;
                }
                else if (columns[i].StandardKey == "LENGTH_MM")
                {
                    hasLength = true;
                }
                else if (columns[i].StandardKey == "QUANTITY_EA")
                {
                    hasQty = true;
                }
                else if (columns[i].StandardKey == "TOTAL_WEIGHT")
                {
                    hasWeight = true;
                }
            }

            return hasLength && (hasSpec || hasQty || hasWeight);
        }

        private string CleanHeaderText(string value)
        {
            value = CleanText(value);

            value = value.Replace("\\\\P", " ");
            value = value.Replace("  ", " ");
            value = value.Trim();

            return value;
        }

        private string NormalizeHeaderTitleForOutput(string title, string standardKey)
        {
            if (title == null)
            {
                title = "";
            }

            title = title.Trim();

            if (title != "")
            {
                return title;
            }

            if (standardKey == "MARK_NO")
            {
                return "번호";
            }

            if (standardKey == "PART")
            {
                return "부위";
            }

            if (standardKey == "SYMBOL")
            {
                return "부호";
            }

            if (standardKey == "SHAPE_NO")
            {
                return "형상번호";
            }

            if (standardKey == "SHAPE")
            {
                return "철근형상";
            }

            if (standardKey == "SPEC")
            {
                return "규격";
            }

            if (standardKey == "LENGTH_MM")
            {
                return "길이(mm)";
            }

            if (standardKey == "QUANTITY_EA")
            {
                return "수량(EA)";
            }

            if (standardKey == "TOTAL_LENGTH_M")
            {
                return "총길이(M)";
            }

            if (standardKey == "TOTAL_WEIGHT")
            {
                return "중량(TON)";
            }

            if (standardKey == "NOTE")
            {
                return "비고";
            }

            return standardKey;
        }

        private string MergeHeaderTitle(string first, string second)
        {
            first = first == null ? "" : first.Trim();
            second = second == null ? "" : second.Trim();

            if (first == "")
            {
                return second;
            }

            if (second == "")
            {
                return first;
            }

            if (first.IndexOf(second, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return first;
            }

            if (second.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return second;
            }

            return first + " " + second;
        }

        private string ClassifyHeaderTitle(string title)
        {
            if (title == null)
            {
                return "";
            }

            string value = title.ToUpperInvariant();
            value = value.Replace(" ", "");
            value = value.Replace("\t", "");
            value = value.Replace("\r", "");
            value = value.Replace("\n", "");
            value = value.Replace("_", "");
            value = value.Replace("-", "");
            value = value.Replace(".", "");
            value = value.Replace("(", "");
            value = value.Replace(")", "");
            value = value.Replace("[", "");
            value = value.Replace("]", "");

            if (value == "")
            {
                return "";
            }

            if (value.IndexOf("비고", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("NOTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("REMARK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "NOTE";
            }

            if (value.IndexOf("총중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WEIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT";
            }

            if (value.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("총연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TOTALLENGTH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_LENGTH_M";
            }

            if (value.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("본수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("개수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("QTY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("EA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "QUANTITY_EA";
            }

            if (value.IndexOf("길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("LENGTH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "LENGTH_MM";
            }

            if (value.IndexOf("철근규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("강종", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SIZE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("DIA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SPEC";
            }

            if (value.IndexOf("부위", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("위치", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("구간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PART";
            }

            if (value.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상코드", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SHAPE_NO";
            }

            if (value.IndexOf("철근형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SHAPE";
            }

            if (value.IndexOf("부호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SYMBOL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SYMBOL";
            }

            if (value.IndexOf("번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "NO" ||
                value == "N" ||
                value.IndexOf("MARK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("BARNO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "MARK_NO";
            }

            return "";
        }

        private void ApplyValueByStandardKey(OviaBarTableRow row, string key, string value)
        {
            if (row == null || key == null)
            {
                return;
            }

            if (key == "MARK_NO")
            {
                row.MarkNo = AppendCell(row.MarkNo, value);
                row.BarNo = row.MarkNo;
            }
            else if (key == "PART")
            {
                row.Part = AppendCell(row.Part, value);
            }
            else if (key == "SYMBOL")
            {
                row.Symbol = AppendCell(row.Symbol, value);
            }
            else if (key == "SHAPE_NO")
            {
                row.ShapeNo = AppendCell(row.ShapeNo, value);
            }
            else if (key == "SHAPE")
            {
                row.ShapeText = AppendCell(row.ShapeText, value);
                row.ShapeRawText = AppendCell(row.ShapeRawText, value);
                row.ShapeDimensionText = AppendCell(row.ShapeDimensionText, ExtractNumbersText(value));
            }
            else if (key == "SPEC")
            {
                row.Spec = AppendCell(row.Spec, value);
            }
            else if (key == "LENGTH_MM")
            {
                row.Length = AppendCell(row.Length, value);
            }
            else if (key == "QUANTITY_EA")
            {
                row.Qty = AppendCell(row.Qty, value);
            }
            else if (key == "TOTAL_LENGTH_M")
            {
                row.TotalLength = AppendCell(row.TotalLength, value);
            }
            else if (key == "TOTAL_WEIGHT")
            {
                row.TotalWeight = AppendCell(row.TotalWeight, value);
            }
            else if (key == "NOTE")
            {
                row.Note = AppendCell(row.Note, value);
            }
        }

        private string GetValueByStandardKey(OviaBarTableRow row, string key)
        {
            if (row == null || key == null)
            {
                return "";
            }

            if (key == "MARK_NO")
            {
                if (row.MarkNo != "")
                {
                    return row.MarkNo;
                }

                if (row.BarNo != "")
                {
                    return row.BarNo;
                }

                return "";
            }

            if (key == "PART")
            {
                return row.Part;
            }

            if (key == "SYMBOL")
            {
                return row.Symbol;
            }

            if (key == "SHAPE_NO")
            {
                return row.ShapeNo;
            }

            if (key == "SHAPE")
            {
                // CAD 원본 형상이 추출된 경우, 사용자 화면의 철근형상 셀은
                // 숨김 컬럼의 JSON을 렌더링해서 보여줍니다.
                // 따라서 CSV의 표시용 철근형상 텍스트에는 규격/길이/수량 등이 섞인 값을 쓰지 않습니다.
                if (row.CadShapeJsonPath != null && row.CadShapeJsonPath.Trim() != "")
                {
                    return "";
                }

                return row.ShapeText;
            }

            if (key == "SPEC")
            {
                return row.Spec;
            }

            if (key == "LENGTH_MM")
            {
                return row.Length;
            }

            if (key == "QUANTITY_EA")
            {
                return row.Qty;
            }

            if (key == "TOTAL_LENGTH_M")
            {
                return row.TotalLength;
            }

            if (key == "TOTAL_WEIGHT")
            {
                return row.TotalWeight;
            }

            if (key == "NOTE")
            {
                return row.Note;
            }

            return "";
        }

        private string ExtractNumbersText(string text)
        {
            if (text == null)
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(text, @"-?\d+(\.\d+)?");

            if (matches.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < matches.Count; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" ");
                }

                sb.Append(matches[i].Value);
            }

            return sb.ToString();
        }

        private string LastDecimalOrLastNumber(string text)
        {
            string decimalValue = LastDecimalNumber(text);

            if (decimalValue != "")
            {
                return decimalValue;
            }

            if (text == null)
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(text, @"\d+(\.\d+)?");

            if (matches.Count == 0)
            {
                return "";
            }

            return matches[matches.Count - 1].Value;
        }

        private List<List<OviaTextRow>> GroupRowsByY(List<OviaTextRow> rows)
        {
            List<List<OviaTextRow>> groups = new List<List<OviaTextRow>>();

            if (rows == null || rows.Count == 0)
            {
                return groups;
            }

            SortRowsTopToBottomLeftToRight(rows);

            double tolerance = GetAverageTextHeight(rows) * 0.85;

            if (tolerance <= 0)
            {
                tolerance = 1.0;
            }

            List<OviaTextRow> current = new List<OviaTextRow>();
            double baseY = rows[0].Y;

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (current.Count > 0 && Math.Abs(rows[i].Y - baseY) > tolerance)
                {
                    groups.Add(current);

                    current = new List<OviaTextRow>();
                    baseY = rows[i].Y;
                }

                current.Add(rows[i]);
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            return groups;
        }

        private OviaBarTableRow ConvertLineToBarTableRow(List<OviaTextRow> line, double minX, double maxX)
        {
            OviaBarTableRow row = new OviaBarTableRow();
            row.RowType = "DATA";

            string all = JoinRowText(line);

            if (all.IndexOf("총계", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row.RowType = "TOTAL";
            }
            else if (all.IndexOf("소계", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row.RowType = "SUBTOTAL";
            }

            /*
             * 2026-05-20 보정
             * 기존 버전은 선택박스 안의 실제 문자 X좌표 범위만 기준으로 컬럼을 나누었습니다.
             * 이 방식은 사용자가 표의 선/비고 영역까지 선택해도, 문자 자체의 최소/최대 X가 좁게 잡히면
             * 총길이(M), 총중량(TON)이 한 칸씩 밀리는 문제가 발생합니다.
             *
             * 그래서 1차 판단은 도면에 표시된 행의 문자 순서 기준으로 처리합니다.
             * 일반 데이터 행은 보통 아래 순서입니다.
             * 번호 / 규격 / 길이(mm) / 수량(EA) / 총길이(M) / 총중량(TON)
             */
            if (row.RowType == "DATA")
            {
                if (TryParseStandardDataRow(line, row))
                {
                    return row;
                }
            }

            if (row.RowType == "SUBTOTAL" || row.RowType == "TOTAL")
            {
                if (TryParseSummaryRow(line, row))
                {
                    return row;
                }
            }

            string noText = "";
            string shapeText = "";
            string specText = "";
            string lengthText = "";
            string qtyText = "";
            string totalLengthText = "";
            string totalWeightText = "";
            string noteText = "";

            int i;

            for (i = 0; i < line.Count; i++)
            {
                OviaTextRow item = line[i];
                string text = CleanCellText(item.TextValue);

                if (text == "")
                {
                    continue;
                }

                string column = GuessTableColumn(item.X, minX, maxX);

                if (column == "NO")
                {
                    noText = AppendCell(noText, text);
                }
                else if (column == "SHAPE")
                {
                    shapeText = AppendCell(shapeText, text);
                }
                else if (column == "SPEC")
                {
                    specText = AppendCell(specText, text);
                }
                else if (column == "LENGTH")
                {
                    lengthText = AppendCell(lengthText, text);
                }
                else if (column == "QTY")
                {
                    qtyText = AppendCell(qtyText, text);
                }
                else if (column == "TOTAL_LENGTH")
                {
                    totalLengthText = AppendCell(totalLengthText, text);
                }
                else if (column == "TOTAL_WEIGHT")
                {
                    totalWeightText = AppendCell(totalWeightText, text);
                }
                else
                {
                    noteText = AppendCell(noteText, text);
                }
            }

            string detectedSpec = DetectSpec(all);

            if (specText == "" && detectedSpec != "")
            {
                specText = detectedSpec;
            }

            row.BarNo = FirstSimpleNumber(noText);
            row.ShapeText = shapeText;
            row.Spec = specText;
            row.Length = FirstNumber(lengthText);
            row.Qty = FirstNumber(qtyText);
            row.TotalLength = FirstNumber(totalLengthText);
            row.TotalWeight = FirstNumber(totalWeightText);
            row.Note = noteText;

            if (row.RowType == "SUBTOTAL" || row.RowType == "TOTAL")
            {
                if (row.TotalLength == "")
                {
                    row.TotalLength = LastNumberBeforeWeight(all);
                }

                if (row.TotalWeight == "")
                {
                    row.TotalWeight = LastDecimalNumber(all);
                }
            }

            if (row.BarNo == "" && row.RowType == "DATA")
            {
                row.BarNo = FirstNumberFromFirstCell(line, minX, maxX);
            }

            return row;
        }

        private void SupplementStandardDataFromRawText(string rawText, OviaBarTableRow row)
        {
            if (row == null || rawText == null)
            {
                return;
            }

            string text = CleanCellText(rawText);

            if (text == "")
            {
                return;
            }

            if (row.Spec == "")
            {
                string detectedSpec = DetectSpec(text);

                if (detectedSpec != "")
                {
                    row.Spec = detectedSpec;
                }
            }

            string[] parts = text.Split(new char[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            int specIndex = -1;
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                if (DetectSpec(parts[i]) != "")
                {
                    specIndex = i;
                    break;
                }
            }

            if (specIndex < 0)
            {
                return;
            }

            List<string> numbersAfterSpec = new List<string>();

            for (i = specIndex + 1; i < parts.Length; i++)
            {
                MatchCollection matches = Regex.Matches(parts[i], @"-?\d+(\.\d+)?");
                int j;

                for (j = 0; j < matches.Count; j++)
                {
                    numbersAfterSpec.Add(matches[j].Value);
                }
            }

            if (numbersAfterSpec.Count < 4)
            {
                return;
            }

            /*
             * 철근형상 칸에는 CAD 형상 내부 치수값이 같이 들어올 수 있습니다.
             * 따라서 규격 뒤 첫 숫자를 길이로 보지 않고, 행 오른쪽의 산정값 4개를 우선 사용합니다.
             * 기준: 길이(mm) / 수량(EA) / 총길이(M) / 총중량(TON)
             */
            int baseIndex = numbersAfterSpec.Count - 4;

            // 표 선/헤더 인식이 실패한 경우에만 rawText로 누락값을 보강합니다.
            // 이미 grid column 좌표로 들어온 길이/수량/총길이/중량 값은 절대 덮어쓰지 않습니다.
            // 철근형상 내부 치수(120, 490 등)가 rawText 뒤쪽에 붙는 도면에서는
            // 기존 값을 덮어쓰면 길이/수량/중량이 한 칸씩 밀리는 큰 오류가 발생합니다.
            if (row.Length == "")
            {
                row.Length = numbersAfterSpec[baseIndex];
            }

            if (row.Qty == "")
            {
                row.Qty = numbersAfterSpec[baseIndex + 1];
            }

            if (row.TotalLength == "")
            {
                row.TotalLength = numbersAfterSpec[baseIndex + 2];
            }

            if (row.TotalWeight == "")
            {
                row.TotalWeight = numbersAfterSpec[baseIndex + 3];
            }
        }

        private bool TryParseStandardDataRow(List<OviaTextRow> line, OviaBarTableRow row)
        {
            if (line == null || line.Count == 0 || row == null)
            {
                return false;
            }

            line.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                return a.X.CompareTo(b.X);
            });

            List<string> cells = new List<string>();

            int i;

            for (i = 0; i < line.Count; i++)
            {
                string value = CleanCellText(line[i].TextValue);

                if (value == "")
                {
                    continue;
                }

                cells.Add(value);
            }

            if (cells.Count < 5)
            {
                return false;
            }

            int noIndex = -1;
            int specIndex = -1;

            for (i = 0; i < cells.Count; i++)
            {
                if (noIndex < 0 && Regex.IsMatch(cells[i], @"^\d+$"))
                {
                    noIndex = i;
                    continue;
                }

                if (specIndex < 0 && DetectSpec(cells[i]) != "")
                {
                    specIndex = i;
                    break;
                }
            }

            if (specIndex < 0)
            {
                return false;
            }

            if (noIndex >= 0)
            {
                row.BarNo = FirstSimpleNumber(cells[noIndex]);
            }

            row.Spec = DetectSpec(cells[specIndex]);

            List<string> numbersAfterSpec = new List<string>();

            for (i = specIndex + 1; i < cells.Count; i++)
            {
                MatchCollection matches = Regex.Matches(cells[i], @"-?\d+(\.\d+)?");

                int j;

                for (j = 0; j < matches.Count; j++)
                {
                    numbersAfterSpec.Add(matches[j].Value);
                }
            }

            if (numbersAfterSpec.Count < 4)
            {
                return false;
            }

            /*
             * 철근형상 칸 안에는 치수 문자도 함께 들어올 수 있습니다.
             * 예:
             * 번호 / 규격 / 형상 내부 치수 / 길이 / 수량 / 총길이 / 총중량
             *
             * 기존 방식처럼 규격 뒤의 첫 숫자부터 읽으면 형상 내부 치수를 길이로 오인합니다.
             * 그래서 실제 산정값은 행의 오른쪽 끝 4개 숫자를 기준으로 잡습니다.
             * 길이(mm) / 수량(EA) / 총길이(M) / 총중량
             */
            int baseIndex = numbersAfterSpec.Count - 4;

            row.Length = numbersAfterSpec[baseIndex];
            row.Qty = numbersAfterSpec[baseIndex + 1];
            row.TotalLength = numbersAfterSpec[baseIndex + 2];
            row.TotalWeight = numbersAfterSpec[baseIndex + 3];

            return true;
        }

        private bool TryParseSummaryRow(List<OviaTextRow> line, OviaBarTableRow row)
        {
            if (line == null || line.Count == 0 || row == null)
            {
                return false;
            }

            string all = JoinRowText(line);

            MatchCollection matches = Regex.Matches(all, @"-?\d+(\.\d+)?");

            if (matches.Count == 0)
            {
                return false;
            }

            if (matches.Count >= 2)
            {
                row.TotalLength = matches[matches.Count - 2].Value;
                row.TotalWeight = matches[matches.Count - 1].Value;
            }
            else
            {
                row.TotalLength = matches[0].Value;
            }

            if (all.IndexOf("소계", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row.BarNo = "소계";
            }
            else if (all.IndexOf("총계", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                row.BarNo = "총계";
            }

            return true;
        }

        private string GuessTableColumn(double x, double minX, double maxX)
        {
            double width = maxX - minX;

            if (width <= 0)
            {
                return "NOTE";
            }

            double ratio = (x - minX) / width;

            /*
             * 1차 구조화 기준
             * 선택박스가 집계표 전체 가로폭을 포함한다는 전제로 컬럼을 비율로 나눕니다.
             * 회사별 표 형태가 다를 수 있으므로, 다음 단계에서 사용자가 컬럼 기준선을 조정할 수 있게 확장합니다.
             */
            if (ratio < 0.11)
            {
                return "NO";
            }

            if (ratio < 0.28)
            {
                return "SHAPE";
            }

            if (ratio < 0.40)
            {
                return "SPEC";
            }

            if (ratio < 0.52)
            {
                return "LENGTH";
            }

            if (ratio < 0.62)
            {
                return "QTY";
            }

            if (ratio < 0.76)
            {
                return "TOTAL_LENGTH";
            }

            if (ratio < 0.90)
            {
                return "TOTAL_WEIGHT";
            }

            return "NOTE";
        }

        private bool IsHeaderRow(string text)
        {
            if (text == null)
            {
                return false;
            }

            int score = 0;

            if (text.IndexOf("번호", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            if (text.IndexOf("철근", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            if (text.IndexOf("규격", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            if (text.IndexOf("길이", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            if (text.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            if (text.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score++;
            }

            return score >= 3;
        }

        private bool IsEmptyNoiseRow(string text)
        {
            if (text == null)
            {
                return true;
            }

            text = text.Trim();

            if (text == "")
            {
                return true;
            }

            if (text == "NONE")
            {
                return true;
            }

            return false;
        }

        private bool IsMeaninglessTableRow(OviaBarTableRow row)
        {
            if (row == null)
            {
                return true;
            }

            if (row.RowType == "SUBTOTAL" || row.RowType == "TOTAL")
            {
                return false;
            }

            if (row.Spec != "")
            {
                return false;
            }

            if (row.Length != "" && row.Qty != "")
            {
                return false;
            }

            if (row.TotalLength != "" || row.TotalWeight != "")
            {
                return false;
            }

            return true;
        }

        private string DetectSpec(string text)
        {
            if (text == null)
            {
                return "";
            }

            Match match = Regex.Match(text, @"[A-Z]{1,5}D?\d{1,3}", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }

        private string FirstSimpleNumber(string text)
        {
            if (text == null)
            {
                return "";
            }

            Match match = Regex.Match(text, @"\d+");

            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }

        private string FirstNumber(string text)
        {
            if (text == null)
            {
                return "";
            }

            Match match = Regex.Match(text, @"-?\d+(\.\d+)?");

            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }

        private string LastDecimalNumber(string text)
        {
            if (text == null)
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(text, @"\d+\.\d+");

            if (matches.Count == 0)
            {
                return "";
            }

            return matches[matches.Count - 1].Value;
        }

        private string LastNumberBeforeWeight(string text)
        {
            if (text == null)
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(text, @"\d+(\.\d+)?");

            if (matches.Count == 0)
            {
                return "";
            }

            if (matches.Count >= 2)
            {
                return matches[matches.Count - 2].Value;
            }

            return matches[matches.Count - 1].Value;
        }

        private string FirstNumberFromFirstCell(List<OviaTextRow> line, double minX, double maxX)
        {
            if (line == null || line.Count == 0)
            {
                return "";
            }

            int i;

            for (i = 0; i < line.Count; i++)
            {
                string column = GuessTableColumn(line[i].X, minX, maxX);

                if (column == "NO")
                {
                    string value = FirstSimpleNumber(line[i].TextValue);

                    if (value != "")
                    {
                        return value;
                    }
                }
            }

            return "";
        }

        private string CleanCellText(string value)
        {
            value = CleanText(value);

            if (value == "NONE")
            {
                return "";
            }

            return value;
        }

        private string AppendCell(string origin, string value)
        {
            if (value == null || value.Trim() == "")
            {
                return origin;
            }

            if (origin == null || origin.Trim() == "")
            {
                return value.Trim();
            }

            return origin + " " + value.Trim();
        }

        private string JoinRowText(List<OviaTextRow> line)
        {
            if (line == null || line.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();

            int i;

            for (i = 0; i < line.Count; i++)
            {
                string value = CleanText(line[i].TextValue);

                if (value == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" ");
                }

                sb.Append(value);
            }

            return sb.ToString();
        }

        private double GetMinX(List<OviaTextRow> rows)
        {
            double value = rows[0].X;
            int i;

            for (i = 1; i < rows.Count; i++)
            {
                if (rows[i].X < value)
                {
                    value = rows[i].X;
                }
            }

            return value;
        }

        private double GetMaxX(List<OviaTextRow> rows)
        {
            double value = rows[0].X;
            int i;

            for (i = 1; i < rows.Count; i++)
            {
                if (rows[i].X > value)
                {
                    value = rows[i].X;
                }
            }

            return value;
        }


        private void CaptureCadShapeFilesForRows(Editor ed, Database db, string csvFilePath, List<OviaBarTableRow> rows)
        {
            if (ed == null || db == null || rows == null || rows.Count == 0 || csvFilePath == null || csvFilePath.Trim() == "")
            {
                return;
            }

            string csvDirectory = Path.GetDirectoryName(csvFilePath);

            if (csvDirectory == null || csvDirectory.Trim() == "")
            {
                return;
            }

            string shapeDirectory = Path.Combine(csvDirectory, "Shapes");

            try
            {
                if (!Directory.Exists(shapeDirectory))
                {
                    Directory.CreateDirectory(shapeDirectory);
                }
            }
            catch
            {
                return;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null)
                {
                    continue;
                }

                if (!row.HasShapeCellBounds())
                {
                    continue;
                }

                Point3d minPoint = new Point3d(row.ShapeCellMinX, row.ShapeCellMinY, 0);
                Point3d maxPoint = new Point3d(row.ShapeCellMaxX, row.ShapeCellMaxY, 0);
                List<OviaCadShapeElement> elements = ExtractCadShapeElementsByWindow(ed, db, minPoint, maxPoint, row);

                if (elements.Count == 0)
                {
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_EMPTY";
                    continue;
                }

                string jsonFileName = "row_" + row.No.ToString("000", CultureInfo.InvariantCulture) + "_shape.json";
                string jsonFilePath = Path.Combine(shapeDirectory, jsonFileName);

                try
                {
                    File.WriteAllText(jsonFilePath, BuildCadShapeJson(row, elements), new UTF8Encoding(true));
                    row.CadShapeJsonPath = "Shapes/" + jsonFileName;
                    row.CadShapeTextValues = BuildCadShapeTextValues(elements);
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_CAPTURED";
                }
                catch
                {
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_JSON_SAVE_FAILED";
                }
            }
        }

        private List<OviaCadShapeElement> ExtractCadShapeElementsByWindow(Editor ed, Database db, Point3d point1, Point3d point2, OviaBarTableRow row)
        {
            List<OviaCadShapeElement> elements = new List<OviaCadShapeElement>();

            double minX = Math.Min(point1.X, point2.X);
            double maxX = Math.Max(point1.X, point2.X);
            double minY = Math.Min(point1.Y, point2.Y);
            double maxY = Math.Max(point1.Y, point2.Y);
            double width = maxX - minX;
            double height = maxY - minY;

            if (width <= 0.0001 || height <= 0.0001)
            {
                return elements;
            }

            /*
             * 중요:
             * 철근형상 칸 안의 객체만 가져와야 합니다.
             * SelectCrossingWindow는 셀을 가로지르는 표 전체 선, 전체 표 BlockReference까지 잡을 수 있습니다.
             * 그래서 우선 후보를 넓게 잡되, 실제 추가 단계에서 좌표가 해당 셀 내부에 있는 객체만 보존합니다.
             */
            double insetX = Math.Max(width * 0.015, 0.05);
            double insetY = Math.Max(height * 0.015, 0.05);
            Point3d selectMin = new Point3d(minX + insetX, minY + insetY, 0);
            Point3d selectMax = new Point3d(maxX - insetX, maxY - insetY, 0);
            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(selectMin, selectMax);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return elements;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId[] ids = selectionResult.Value.GetObjectIds();
                int i;

                for (i = 0; i < ids.Length; i++)
                {
                    Entity entity = tr.GetObject(ids[i], OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        continue;
                    }

                    CollectCadShapeElementsFromEntity(tr, entity, Matrix3d.Identity, elements, minX, maxX, minY, maxY, width, height, 0);
                }

                tr.Commit();
            }

            RemoveCadShapeNoise(elements, width, height);
            KeepOnlyActualCadShapeElements(row, elements, width, height);
            return elements;
        }

        private void CollectCadShapeElementsFromEntity(Transaction tr, Entity entity, Matrix3d transform, List<OviaCadShapeElement> elements, double minX, double maxX, double minY, double maxY, double width, double height, int depth)
        {
            if (entity == null || elements == null || depth > 8)
            {
                return;
            }

            if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            double originX = minX;
            double topY = maxY;

            Line line = entity as Line;

            if (line != null)
            {
                Point3d p1 = line.StartPoint.TransformBy(transform);
                Point3d p2 = line.EndPoint.TransformBy(transform);

                if (!ShouldKeepCadShapeLine(p1, p2, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "LINE";
                item.X1 = NormalizeCadShapeX(p1.X, originX);
                item.Y1 = NormalizeCadShapeY(p1.Y, topY);
                item.X2 = NormalizeCadShapeX(p2.X, originX);
                item.Y2 = NormalizeCadShapeY(p2.Y, topY);
                elements.Add(item);
                return;
            }

            Polyline polyline = entity as Polyline;

            if (polyline != null)
            {
                int count = polyline.NumberOfVertices;
                int i;

                for (i = 0; i < count - 1; i++)
                {
                    Point3d p1 = polyline.GetPoint3dAt(i).TransformBy(transform);
                    Point3d p2 = polyline.GetPoint3dAt(i + 1).TransformBy(transform);

                    if (!ShouldKeepCadShapeLine(p1, p2, minX, maxX, minY, maxY, width, height))
                    {
                        continue;
                    }

                    OviaCadShapeElement item = new OviaCadShapeElement();
                    item.Type = "LINE";
                    item.X1 = NormalizeCadShapeX(p1.X, originX);
                    item.Y1 = NormalizeCadShapeY(p1.Y, topY);
                    item.X2 = NormalizeCadShapeX(p2.X, originX);
                    item.Y2 = NormalizeCadShapeY(p2.Y, topY);
                    elements.Add(item);
                }

                if (polyline.Closed && count > 1)
                {
                    Point3d p1 = polyline.GetPoint3dAt(count - 1).TransformBy(transform);
                    Point3d p2 = polyline.GetPoint3dAt(0).TransformBy(transform);

                    if (ShouldKeepCadShapeLine(p1, p2, minX, maxX, minY, maxY, width, height))
                    {
                        OviaCadShapeElement item = new OviaCadShapeElement();
                        item.Type = "LINE";
                        item.X1 = NormalizeCadShapeX(p1.X, originX);
                        item.Y1 = NormalizeCadShapeY(p1.Y, topY);
                        item.X2 = NormalizeCadShapeX(p2.X, originX);
                        item.Y2 = NormalizeCadShapeY(p2.Y, topY);
                        elements.Add(item);
                    }
                }

                return;
            }

            Arc arc = entity as Arc;

            if (arc != null)
            {
                Point3d center = arc.Center.TransformBy(transform);

                if (!IsPointInCadShapeCell(center, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }

                if (arc.Radius > Math.Max(width, height) * 1.25)
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "ARC";
                item.CX = NormalizeCadShapeX(center.X, originX);
                item.CY = NormalizeCadShapeY(center.Y, topY);
                item.Radius = arc.Radius;
                item.StartAngle = arc.StartAngle * 180.0 / Math.PI;
                item.EndAngle = arc.EndAngle * 180.0 / Math.PI;
                elements.Add(item);
                return;
            }

            Circle circle = entity as Circle;

            if (circle != null)
            {
                Point3d center = circle.Center.TransformBy(transform);

                if (!IsPointInCadShapeCell(center, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }

                if (circle.Radius > Math.Max(width, height) * 1.25)
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "CIRCLE";
                item.CX = NormalizeCadShapeX(center.X, originX);
                item.CY = NormalizeCadShapeY(center.Y, topY);
                item.Radius = circle.Radius;
                elements.Add(item);
                return;
            }

            DBText dbText = entity as DBText;

            if (dbText != null)
            {
                Point3d p = dbText.Position.TransformBy(transform);

                if (!IsPointInCadShapeCell(p, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "TEXT";
                item.Text = CleanText(dbText.TextString);
                item.X1 = NormalizeCadShapeX(p.X, originX);
                item.Y1 = NormalizeCadShapeY(p.Y, topY);
                item.Height = dbText.Height;
                item.Rotation = dbText.Rotation * 180.0 / Math.PI;
                elements.Add(item);
                return;
            }

            MText mText = entity as MText;

            if (mText != null)
            {
                Point3d p = mText.Location.TransformBy(transform);

                if (!IsPointInCadShapeCell(p, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "TEXT";
                item.Text = CleanText(mText.Text);
                item.X1 = NormalizeCadShapeX(p.X, originX);
                item.Y1 = NormalizeCadShapeY(p.Y, topY);
                item.Height = mText.TextHeight;
                item.Rotation = mText.Rotation * 180.0 / Math.PI;
                elements.Add(item);
                return;
            }

            BlockReference blockReference = entity as BlockReference;

            if (blockReference != null)
            {
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                if (blockRecord != null)
                {
                    Matrix3d nextTransform = transform * blockReference.BlockTransform;

                    foreach (ObjectId childId in blockRecord)
                    {
                        Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                        if (childEntity == null)
                        {
                            continue;
                        }

                        CollectCadShapeElementsFromEntity(tr, childEntity, nextTransform, elements, minX, maxX, minY, maxY, width, height, depth + 1);
                    }
                }

                return;
            }
        }

        private bool ShouldKeepCadShapeLine(Point3d p1, Point3d p2, double minX, double maxX, double minY, double maxY, double width, double height)
        {
            Point3d center = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);

            if (!IsPointInCadShapeCell(p1, minX, maxX, minY, maxY, width, height)
                && !IsPointInCadShapeCell(p2, minX, maxX, minY, maxY, width, height)
                && !IsPointInCadShapeCell(center, minX, maxX, minY, maxY, width, height))
            {
                return false;
            }

            double dx = Math.Abs(p1.X - p2.X);
            double dy = Math.Abs(p1.Y - p2.Y);
            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            bool horizontal = dy <= axisTolerance;
            bool vertical = dx <= axisTolerance;

            if (horizontal && dx >= width * 0.82)
            {
                return false;
            }

            if (vertical && dy >= height * 0.82)
            {
                return false;
            }

            if (horizontal && (Math.Abs(center.Y - minY) <= axisTolerance || Math.Abs(center.Y - maxY) <= axisTolerance))
            {
                return false;
            }

            if (vertical && (Math.Abs(center.X - minX) <= axisTolerance || Math.Abs(center.X - maxX) <= axisTolerance))
            {
                return false;
            }

            return true;
        }

        private bool IsPointInCadShapeCell(Point3d point, double minX, double maxX, double minY, double maxY, double width, double height)
        {
            double marginX = Math.Max(width * 0.08, 0.15);
            double marginY = Math.Max(height * 0.08, 0.15);

            return point.X >= minX - marginX
                && point.X <= maxX + marginX
                && point.Y >= minY - marginY
                && point.Y <= maxY + marginY;
        }

        private void RemoveCadShapeNoise(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (item.Type == "TEXT")
                {
                    if (item.Text == null || item.Text.Trim() == "")
                    {
                        elements.RemoveAt(i);
                        continue;
                    }

                    continue;
                }

                if (item.Type == "LINE")
                {
                    double dx = Math.Abs(item.X1 - item.X2);
                    double dy = Math.Abs(item.Y1 - item.Y2);

                    if (dx < 0.0001 && dy < 0.0001)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }

                    if (dx >= width * 0.90 || dy >= height * 0.90)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }
                }
            }
        }

        private void KeepOnlyActualCadShapeElements(OviaBarTableRow row, List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            double geomMinX;
            double geomMinY;
            double geomMaxX;
            double geomMaxY;
            bool hasGeometry = GetCadShapeContentBounds(elements, false, out geomMinX, out geomMinY, out geomMaxX, out geomMaxY);

            if (!hasGeometry)
            {
                RemoveExternalRowValueTexts(row, elements, 0, 0, width, height, false);
                return;
            }

            double geomWidth = Math.Max(geomMaxX - geomMinX, 0.0001);
            double geomHeight = Math.Max(geomMaxY - geomMinY, 0.0001);
            double looseMarginX = Math.Max(geomWidth * 0.70, width * 0.08);
            double looseMarginY = Math.Max(geomHeight * 1.10, height * 0.20);
            double looseMinX = geomMinX - looseMarginX;
            double looseMaxX = geomMaxX + looseMarginX;
            double looseMinY = geomMinY - looseMarginY;
            double looseMaxY = geomMaxY + looseMarginY;

            double tightMarginX = Math.Max(geomWidth * 0.20, width * 0.025);
            double tightMarginY = Math.Max(geomHeight * 0.45, height * 0.08);
            double tightMinX = geomMinX - tightMarginX;
            double tightMaxX = geomMaxX + tightMarginX;
            double tightMinY = geomMinY - tightMarginY;
            double tightMaxY = geomMaxY + tightMarginY;

            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (item.Type != "TEXT")
                {
                    continue;
                }

                bool inLooseShapeBox = item.X1 >= looseMinX && item.X1 <= looseMaxX && item.Y1 >= looseMinY && item.Y1 <= looseMaxY;
                bool inTightShapeBox = item.X1 >= tightMinX && item.X1 <= tightMaxX && item.Y1 >= tightMinY && item.Y1 <= tightMaxY;

                if (!inLooseShapeBox)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (IsExternalRowValueText(row, item.Text) && !inTightShapeBox)
                {
                    elements.RemoveAt(i);
                    continue;
                }
            }
        }

        private void RemoveExternalRowValueTexts(OviaBarTableRow row, List<OviaCadShapeElement> elements, double minX, double minY, double maxX, double maxY, bool useBox)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (item.Type == "TEXT" && IsExternalRowValueText(row, item.Text))
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private bool IsExternalRowValueText(OviaBarTableRow row, string text)
        {
            if (row == null || text == null)
            {
                return false;
            }

            string value = NormalizeCadShapeCompareText(text);

            if (value == "")
            {
                return false;
            }

            if (IsSameCadShapeCompareValue(value, row.BarNo))
            {
                return true;
            }

            if (IsSameCadShapeCompareValue(value, row.MarkNo))
            {
                return true;
            }

            if (IsSameCadShapeCompareValue(value, row.Spec))
            {
                return true;
            }

            if (IsSameCadShapeCompareValue(value, row.Qty))
            {
                return true;
            }

            if (IsSameCadShapeCompareValue(value, row.TotalLength))
            {
                return true;
            }

            if (IsSameCadShapeCompareValue(value, row.TotalWeight))
            {
                return true;
            }

            return false;
        }

        private bool IsSameCadShapeCompareValue(string normalizedText, string source)
        {
            if (normalizedText == null || normalizedText == "" || source == null || source.Trim() == "")
            {
                return false;
            }

            string[] parts = source.Split(new char[] { ' ', '\t', ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string part = NormalizeCadShapeCompareText(parts[i]);

                if (part != "" && part == normalizedText)
                {
                    return true;
                }
            }

            return false;
        }

        private string NormalizeCadShapeCompareText(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Trim().ToUpperInvariant();
            value = value.Replace(" ", "");
            value = value.Replace("\t", "");
            value = value.Replace("\r", "");
            value = value.Replace("\n", "");
            value = value.Replace(",", "");
            value = value.Replace("TON", "");
            value = value.Replace("EA", "");
            value = value.Replace("MM", "");
            value = value.Replace("M", "");

            return value;
        }

        private bool GetCadShapeContentBounds(List<OviaCadShapeElement> elements, bool includeText, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = Double.MaxValue;
            minY = Double.MaxValue;
            maxX = Double.MinValue;
            maxY = Double.MinValue;

            if (elements == null || elements.Count == 0)
            {
                return false;
            }

            bool found = false;
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    continue;
                }

                if (item.Type == "TEXT" && !includeText)
                {
                    continue;
                }

                if (item.Type == "LINE")
                {
                    ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.X1, item.Y1);
                    ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.X2, item.Y2);
                    found = true;
                }
                else if (item.Type == "ARC" || item.Type == "CIRCLE")
                {
                    double radius = Math.Abs(item.Radius);
                    ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.CX - radius, item.CY - radius);
                    ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.CX + radius, item.CY + radius);
                    found = true;
                }
                else if (item.Type == "TEXT")
                {
                    ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.X1, item.Y1);
                    found = true;
                }
            }

            return found;
        }

        private void ExpandCadShapeBounds(ref double minX, ref double minY, ref double maxX, ref double maxY, double x, double y)
        {
            if (x < minX)
            {
                minX = x;
            }

            if (y < minY)
            {
                minY = y;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            if (y > maxY)
            {
                maxY = y;
            }
        }

        private double NormalizeCadShapeX(double x, double originX)
        {
            return x - originX;
        }

        private double NormalizeCadShapeY(double y, double topY)
        {
            return topY - y;
        }

        private string BuildCadShapeJson(OviaBarTableRow row, List<OviaCadShapeElement> elements)
        {
            double cropMinX;
            double cropMinY;
            double cropMaxX;
            double cropMaxY;
            bool hasBounds = GetCadShapeContentBounds(elements, true, out cropMinX, out cropMinY, out cropMaxX, out cropMaxY);

            if (!hasBounds)
            {
                cropMinX = 0;
                cropMinY = 0;
                cropMaxX = row == null ? 100 : Math.Max(row.ShapeCellMaxX - row.ShapeCellMinX, 100);
                cropMaxY = row == null ? 60 : Math.Max(row.ShapeCellMaxY - row.ShapeCellMinY, 60);
            }

            double contentWidth = Math.Max(cropMaxX - cropMinX, 1.0);
            double contentHeight = Math.Max(cropMaxY - cropMinY, 1.0);
            double padX = Math.Max(contentWidth * 0.08, 1.0);
            double padY = Math.Max(contentHeight * 0.18, 1.0);
            double offsetX = cropMinX - padX;
            double offsetY = cropMinY - padY;
            double outputWidth = contentWidth + padX * 2.0;
            double outputHeight = contentHeight + padY * 2.0;

            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"version\": 1,\r\n");
            sb.Append("  \"source\": \"CAD\",\r\n");
            sb.Append("  \"rowNo\": ").Append(row == null ? 0 : row.No).Append(",\r\n");
            sb.Append("  \"cell\": {");
            sb.Append("\"width\": ").Append(JsonNumber(outputWidth)).Append(", ");
            sb.Append("\"height\": ").Append(JsonNumber(outputHeight));
            sb.Append("},\r\n");
            sb.Append("  \"elements\": [\r\n");

            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (i > 0)
                {
                    sb.Append(",\r\n");
                }

                sb.Append("    {");
                sb.Append("\"type\": ");
                AppendJsonString(sb, item.Type);

                if (item.Type == "LINE")
                {
                    sb.Append(", \"x1\": ").Append(JsonNumber(item.X1 - offsetX));
                    sb.Append(", \"y1\": ").Append(JsonNumber(item.Y1 - offsetY));
                    sb.Append(", \"x2\": ").Append(JsonNumber(item.X2 - offsetX));
                    sb.Append(", \"y2\": ").Append(JsonNumber(item.Y2 - offsetY));
                }
                else if (item.Type == "ARC" || item.Type == "CIRCLE")
                {
                    sb.Append(", \"cx\": ").Append(JsonNumber(item.CX - offsetX));
                    sb.Append(", \"cy\": ").Append(JsonNumber(item.CY - offsetY));
                    sb.Append(", \"radius\": ").Append(JsonNumber(item.Radius));
                    sb.Append(", \"startAngle\": ").Append(JsonNumber(item.StartAngle));
                    sb.Append(", \"endAngle\": ").Append(JsonNumber(item.EndAngle));
                }
                else if (item.Type == "TEXT")
                {
                    sb.Append(", \"text\": ");
                    AppendJsonString(sb, item.Text);
                    sb.Append(", \"x\": ").Append(JsonNumber(item.X1 - offsetX));
                    sb.Append(", \"y\": ").Append(JsonNumber(item.Y1 - offsetY));
                    sb.Append(", \"height\": ").Append(JsonNumber(item.Height));
                    sb.Append(", \"rotation\": ").Append(JsonNumber(item.Rotation));
                }

                sb.Append("}");
            }

            sb.Append("\r\n  ]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private string BuildCadShapeTextValues(List<OviaCadShapeElement> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "TEXT" || item.Text == null || item.Text.Trim() == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("|");
                }

                sb.Append(item.Text.Trim());
            }

            return sb.ToString();
        }

        private string JsonNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void AppendJsonString(StringBuilder sb, string value)
        {
            if (sb == null)
            {
                return;
            }

            if (value == null)
            {
                value = "";
            }

            sb.Append('"');
            int i;

            for (i = 0; i < value.Length; i++)
            {
                char ch = value[i];

                if (ch == '"' || ch == '\\')
                {
                    sb.Append('\\');
                    sb.Append(ch);
                }
                else if (ch == '\r')
                {
                    sb.Append("\\r");
                }
                else if (ch == '\n')
                {
                    sb.Append("\\n");
                }
                else
                {
                    sb.Append(ch);
                }
            }

            sb.Append('"');
        }


        private List<OviaHeaderColumn> CreateStandardOutputHeaderColumns()
        {
            List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

            // 사용자 화면/CSV의 고정 출력 순서입니다.
            // 번호 / 부위 / 부호 / 규격 / 형상번호 / 철근형상 / 길이 / 총길이(M) / 수량 / 중량(Ton) / 비고
            columns.Add(CreateHeaderColumn("MARK_NO", "번호", 0));
            columns.Add(CreateHeaderColumn("PART", "부위", 1));
            columns.Add(CreateHeaderColumn("SYMBOL", "부호", 2));
            columns.Add(CreateHeaderColumn("SPEC", "규격", 3));
            columns.Add(CreateHeaderColumn("SHAPE_NO", "형상번호", 4));
            columns.Add(CreateHeaderColumn("SHAPE", "철근형상", 5));
            columns.Add(CreateHeaderColumn("LENGTH_MM", "길이(mm)", 6));
            columns.Add(CreateHeaderColumn("TOTAL_LENGTH_M", "총길이(M)", 7));
            columns.Add(CreateHeaderColumn("QUANTITY_EA", "수량(EA)", 8));
            columns.Add(CreateHeaderColumn("TOTAL_WEIGHT", "중량(TON)", 9));
            columns.Add(CreateHeaderColumn("NOTE", "비고", 10));

            return columns;
        }

        private void WriteBarTableCsv(string filePath, List<OviaBarTableRow> rows)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                /*
                 * 사용자 화면/CSV 출력 컬럼은 항상 기존 BarList 표준 순서를 유지합니다.
                 * 헤더 자동 인식 결과가 일부 컬럼만 잡힌 경우에도 출력 컬럼이 줄어들면 안 됩니다.
                 *
                 * 출력 기준:
                 * 번호 | 부위 | 부호 | 규격 | 형상번호 | 철근형상 | 길이(mm) | 총길이(M) | 수량(EA) | 중량(TON) | 비고
                 *
                 * lastDetectedHeaderColumns는 CAD 셀 위치 분석/추출용으로만 사용하고,
                 * CSV 출력은 표준 컬럼으로 고정합니다.
                 */
                List<OviaHeaderColumn> columns = CreateStandardOutputHeaderColumns();

                writer.Write("No,RowType,SourceRowNo");

                int h;

                for (h = 0; h < columns.Count; h++)
                {
                    writer.Write(",");
                    writer.Write(Csv(columns[h].OriginalTitle));
                }

                writer.Write(",");
                writer.Write(Csv("OVIA_형상원본"));
                writer.Write(",");
                writer.Write(Csv("OVIA_형상치수"));
                writer.Write(",");
                writer.Write(Csv("OVIA_CAD_SHAPE_JSON"));
                writer.Write(",");
                writer.Write(Csv("OVIA_CAD_SHAPE_TEXTS"));
                writer.Write(",");
                writer.Write(Csv("OVIA_SHAPE_SOURCE"));
                writer.Write(",");
                writer.Write(Csv("OVIA_SHAPE_STATUS"));

                writer.WriteLine();

                int i;

                for (i = 0; i < rows.Count; i++)
                {
                    OviaBarTableRow row = rows[i];

                    writer.Write(row.No.ToString());
                    writer.Write(",");
                    writer.Write(Csv(row.RowType));
                    writer.Write(",");
                    writer.Write(row.SourceRowNo.ToString());

                    for (h = 0; h < columns.Count; h++)
                    {
                        writer.Write(",");
                        writer.Write(Csv(GetValueByStandardKey(row, columns[h].StandardKey)));
                    }

                    writer.Write(",");
                    writer.Write(Csv(row.ShapeRawText));
                    writer.Write(",");
                    writer.Write(Csv(row.ShapeDimensionText));
                    writer.Write(",");
                    writer.Write(Csv(row.CadShapeJsonPath));
                    writer.Write(",");
                    writer.Write(Csv(row.CadShapeTextValues));
                    writer.Write(",");
                    writer.Write(Csv(row.ShapeSource));
                    writer.Write(",");
                    writer.Write(Csv(row.ShapeStatus));

                    writer.WriteLine();
                }
            }
        }

        private void WriteBarTablePreview(Editor ed, List<OviaBarTableRow> rows)
        {
            int max = rows.Count;

            if (max > 20)
            {
                max = 20;
            }

            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("집계표 미리보기 최대 20행\n");

            int i;

            for (i = 0; i < max; i++)
            {
                OviaBarTableRow row = rows[i];

                ed.WriteMessage(
                    row.No.ToString() +
                    ". [" + row.RowType + "] " +
                    "부호/번호=" + (row.MarkNo != "" ? row.MarkNo : row.BarNo) +
                    ", 규격=" + row.Spec +
                    ", 길이=" + row.Length +
                    ", 수량=" + row.Qty +
                    ", 총길이=" + row.TotalLength +
                    ", 중량=" + row.TotalWeight +
                    "\n"
                );
            }
        }

        private void AdjustOviaBoxEdge(string edgeName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d minPoint;
            Point3d maxPoint;
            int boxCount;

            if (!GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount))
            {
                ed.WriteMessage("\nOVIA: 조정할 선택박스가 없습니다. 먼저 OVIABOX를 실행해주세요.\n");
                return;
            }

            string message = "\n새 위치를 클릭하세요: ";

            if (edgeName == "TOP")
            {
                message = "\n선택박스 위쪽 선의 새 Y 위치를 클릭하세요: ";
            }
            else if (edgeName == "BOTTOM")
            {
                message = "\n선택박스 아래쪽 선의 새 Y 위치를 클릭하세요: ";
            }
            else if (edgeName == "LEFT")
            {
                message = "\n선택박스 왼쪽 선의 새 X 위치를 클릭하세요: ";
            }
            else if (edgeName == "RIGHT")
            {
                message = "\n선택박스 오른쪽 선의 새 X 위치를 클릭하세요: ";
            }

            PromptPointResult pointResult = ed.GetPoint(message);

            if (pointResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nOVIA: 선택박스 조정이 취소되었습니다.\n");
                return;
            }

            double minX = minPoint.X;
            double maxX = maxPoint.X;
            double minY = minPoint.Y;
            double maxY = maxPoint.Y;

            if (edgeName == "TOP")
            {
                maxY = pointResult.Value.Y;
            }
            else if (edgeName == "BOTTOM")
            {
                minY = pointResult.Value.Y;
            }
            else if (edgeName == "LEFT")
            {
                minX = pointResult.Value.X;
            }
            else if (edgeName == "RIGHT")
            {
                maxX = pointResult.Value.X;
            }

            Point3d newPoint1 = new Point3d(minX, minY, 0);
            Point3d newPoint2 = new Point3d(maxX, maxY, 0);

            RecreateOviaBoxFromMinMax(db, newPoint1, newPoint2);

            ed.WriteMessage("\nOVIA 선택박스를 " + edgeName + " 방향으로 조정했습니다.\n");
        }

        private void RecreateOviaBoxFromMinMax(Database db, Point3d point1, Point3d point2)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dashedLineTypeId = EnsureDashedLineType(db, tr);
                EnsureOviaBoxLayer(db, tr, dashedLineTypeId, false);
                DeleteExistingOviaBoxes(db, tr);
                CreateOviaBoxEntity(db, tr, point1, point2, dashedLineTypeId);
                EnsureOviaBoxLayer(db, tr, dashedLineTypeId, true);
                tr.Commit();
            }
        }

        private void FixOviaBoxRectangle(Database db)
        {
            Point3d minPoint;
            Point3d maxPoint;
            int boxCount;

            if (!GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount))
            {
                return;
            }

            RecreateOviaBoxFromMinMax(db, minPoint, maxPoint);
        }

        private bool GetOviaBoxExtents(Database db, out Point3d minPoint, out Point3d maxPoint, out int boxCount)
        {
            minPoint = new Point3d();
            maxPoint = new Point3d();
            boxCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                {
                    return false;
                }

                BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                if (modelSpace == null)
                {
                    return false;
                }

                bool isFirst = true;

                foreach (ObjectId objectId in modelSpace)
                {
                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        continue;
                    }

                    if (!string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Extents3d extents = entity.GeometricExtents;

                    if (isFirst)
                    {
                        minPoint = extents.MinPoint;
                        maxPoint = extents.MaxPoint;
                        isFirst = false;
                    }
                    else
                    {
                        minPoint = new Point3d(
                            Math.Min(minPoint.X, extents.MinPoint.X),
                            Math.Min(minPoint.Y, extents.MinPoint.Y),
                            0
                        );

                        maxPoint = new Point3d(
                            Math.Max(maxPoint.X, extents.MaxPoint.X),
                            Math.Max(maxPoint.Y, extents.MaxPoint.Y),
                            0
                        );
                    }

                    boxCount++;
                }

                tr.Commit();
            }

            return boxCount > 0;
        }

        private void CreateOviaBoxEntity(Database db, Transaction tr, Point3d point1, Point3d point2, ObjectId dashedLineTypeId)
        {
            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

            if (blockTable == null)
            {
                return;
            }

            BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

            if (modelSpace == null)
            {
                return;
            }

            Polyline box = CreateSelectionBoxPolyline(point1, point2);
            box.Layer = OviaBoxLayerName;
            box.Color = Color.FromRgb(255, 255, 0);
            box.LineWeight = LineWeight.LineWeight211;
            box.ConstantWidth = GetAdaptiveBoxWidth(point1, point2);
            box.Closed = true;
            box.LinetypeScale = GetAdaptiveLineTypeScale(point1, point2);

            if (!dashedLineTypeId.IsNull)
            {
                box.LinetypeId = dashedLineTypeId;
            }

            modelSpace.AppendEntity(box);
            tr.AddNewlyCreatedDBObject(box, true);
        }

        private Polyline CreateSelectionBoxPolyline(Point3d point1, Point3d point2)
        {
            double minX = Math.Min(point1.X, point2.X);
            double maxX = Math.Max(point1.X, point2.X);
            double minY = Math.Min(point1.Y, point2.Y);
            double maxY = Math.Max(point1.Y, point2.Y);

            Polyline polyline = new Polyline();

            polyline.AddVertexAt(0, new Point2d(minX, maxY), 0, 0, 0);
            polyline.AddVertexAt(1, new Point2d(maxX, maxY), 0, 0, 0);
            polyline.AddVertexAt(2, new Point2d(maxX, minY), 0, 0, 0);
            polyline.AddVertexAt(3, new Point2d(minX, minY), 0, 0, 0);
            polyline.Closed = true;

            return polyline;
        }

        private double GetAdaptiveBoxWidth(Point3d point1, Point3d point2)
        {
            double width = Math.Abs(point2.X - point1.X);
            double height = Math.Abs(point2.Y - point1.Y);
            double longSide = Math.Max(width, height);
            double value = longSide * 0.008;

            if (value < 2.0)
            {
                value = 2.0;
            }

            if (value > 35.0)
            {
                value = 35.0;
            }

            return value;
        }

        private double GetAdaptiveLineTypeScale(Point3d point1, Point3d point2)
        {
            double width = Math.Abs(point2.X - point1.X);
            double height = Math.Abs(point2.Y - point1.Y);
            double longSide = Math.Max(width, height);
            double value = longSide / 140.0;

            if (value < 1.0)
            {
                value = 1.0;
            }

            if (value > 50.0)
            {
                value = 50.0;
            }

            return value;
        }

        private ObjectId EnsureDashedLineType(Database db, Transaction tr)
        {
            LinetypeTable lineTypeTable = tr.GetObject(db.LinetypeTableId, OpenMode.ForRead) as LinetypeTable;

            if (lineTypeTable == null)
            {
                return ObjectId.Null;
            }

            if (lineTypeTable.Has(OviaBoxLineTypeName))
            {
                return lineTypeTable[OviaBoxLineTypeName];
            }

            if (lineTypeTable.Has("DOTTED"))
            {
                return lineTypeTable["DOTTED"];
            }

            try
            {
                db.LoadLineTypeFile(OviaBoxLineTypeName, "acad.lin");
            }
            catch (System.Exception)
            {
            }

            if (lineTypeTable.Has(OviaBoxLineTypeName))
            {
                return lineTypeTable[OviaBoxLineTypeName];
            }

            try
            {
                db.LoadLineTypeFile("DOTTED", "acad.lin");
            }
            catch (System.Exception)
            {
            }

            if (lineTypeTable.Has("DOTTED"))
            {
                return lineTypeTable["DOTTED"];
            }

            try
            {
                db.LoadLineTypeFile(OviaBoxLineTypeName, "acadiso.lin");
            }
            catch (System.Exception)
            {
            }

            if (lineTypeTable.Has(OviaBoxLineTypeName))
            {
                return lineTypeTable[OviaBoxLineTypeName];
            }

            try
            {
                db.LoadLineTypeFile("DOTTED", "acadiso.lin");
            }
            catch (System.Exception)
            {
            }

            if (lineTypeTable.Has("DOTTED"))
            {
                return lineTypeTable["DOTTED"];
            }

            if (lineTypeTable.Has("Continuous"))
            {
                return lineTypeTable["Continuous"];
            }

            return ObjectId.Null;
        }

        private void EnsureOviaBoxLayer(Database db, Transaction tr, ObjectId dashedLineTypeId, bool isLocked)
        {
            /*
             * OVIA 선택박스 전용 레이어 보호 정책
             * ------------------------------------------------------------
             * 일반 도면 레이어는 사용자가 잠가둔 상태를 유지합니다.
             * 대신 OVIA가 직접 생성/삭제/수정하는 선택박스는 항상 전용 레이어에만 둡니다.
             * 이 전용 레이어가 잠겨 있거나 꺼져 있으면 OVIA가 자동으로 풀어 eOnLockedLayer 오류를 방지합니다.
             *
             * isLocked 매개변수는 이전 버전 호환을 위해 남겨두지만, 현재 정책에서는 항상 잠금 해제 상태로 유지합니다.
             */
            LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (layerTable == null)
            {
                return;
            }

            if (!layerTable.Has(OviaBoxLayerName))
            {
                layerTable.UpgradeOpen();

                LayerTableRecord layer = new LayerTableRecord();
                layer.Name = OviaBoxLayerName;
                layer.Color = Color.FromRgb(255, 255, 0);
                layer.LineWeight = LineWeight.LineWeight211;
                layer.IsLocked = false;
                layer.IsOff = false;

                try
                {
                    layer.IsFrozen = false;
                }
                catch
                {
                }

                if (!dashedLineTypeId.IsNull)
                {
                    layer.LinetypeObjectId = dashedLineTypeId;
                }

                layerTable.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);

                return;
            }

            LayerTableRecord existingLayer = tr.GetObject(layerTable[OviaBoxLayerName], OpenMode.ForWrite) as LayerTableRecord;

            if (existingLayer != null)
            {
                existingLayer.Color = Color.FromRgb(255, 255, 0);
                existingLayer.LineWeight = LineWeight.LineWeight211;
                existingLayer.IsLocked = false;
                existingLayer.IsOff = false;

                try
                {
                    existingLayer.IsFrozen = false;
                }
                catch
                {
                }

                if (!dashedLineTypeId.IsNull)
                {
                    existingLayer.LinetypeObjectId = dashedLineTypeId;
                }
            }
        }

        private int DeleteExistingOviaBoxes(Database db, Transaction tr)
        {
            int deletedCount = 0;

            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

            if (blockTable == null)
            {
                return deletedCount;
            }

            deletedCount += DeleteOviaBoxesFromSpace(tr, blockTable, BlockTableRecord.ModelSpace);
            deletedCount += DeleteOviaBoxesFromSpace(tr, blockTable, BlockTableRecord.PaperSpace);

            return deletedCount;
        }

        private int DeleteOviaBoxesFromSpace(Transaction tr, BlockTable blockTable, string blockName)
        {
            int deletedCount = 0;

            if (!blockTable.Has(blockName))
            {
                return deletedCount;
            }

            BlockTableRecord blockRecord = tr.GetObject(blockTable[blockName], OpenMode.ForRead) as BlockTableRecord;

            if (blockRecord == null)
            {
                return deletedCount;
            }

            foreach (ObjectId objectId in blockRecord)
            {
                Entity entity = null;

                try
                {
                    /*
                     * 중요:
                     * 잠긴 일반 도면 레이어의 객체를 처음부터 ForWrite로 열면 eOnLockedLayer가 발생할 수 있습니다.
                     * 그래서 먼저 ForRead로 열고, OVIA 전용 레이어 객체인지 확인한 뒤에만 UpgradeOpen 합니다.
                     */
                    entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                }
                catch
                {
                    continue;
                }

                if (entity == null)
                {
                    continue;
                }

                if (!string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (!entity.IsWriteEnabled)
                    {
                        entity.UpgradeOpen();
                    }

                    entity.Erase();
                    deletedCount++;
                }
                catch
                {
                    /*
                     * OVIA 전용 레이어가 예외적으로 잠겨 있거나 외부참조 상태인 경우에도
                     * 일반 도면 추출 작업이 중단되지 않도록 삭제 실패 객체는 건너뜁니다.
                     */
                }
            }

            return deletedCount;
        }

        private List<OviaTextRow> ExtractRowsByWindow(Editor ed, Database db, Point3d point1, Point3d point2)
        {
            List<OviaTextRow> rows = new List<OviaTextRow>();

            Point3d minPoint = new Point3d(
                Math.Min(point1.X, point2.X),
                Math.Min(point1.Y, point2.Y),
                Math.Min(point1.Z, point2.Z)
            );

            Point3d maxPoint = new Point3d(
                Math.Max(point1.X, point2.X),
                Math.Max(point1.Y, point2.Y),
                Math.Max(point1.Z, point2.Z)
            );

            /*
             * 기존 버전은 TEXT, MTEXT만 선택했습니다.
             * 일부 도면은 집계표 문자가 BlockReference / AttributeReference / Xref 내부에 들어있습니다.
             * 그래서 이번 버전은 우선 선택박스 안의 모든 객체를 잡은 뒤,
             * TEXT / MTEXT / ATTRIBUTE / BLOCK 내부 TEXT까지 재귀적으로 읽습니다.
             */
            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(point1, point2);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return rows;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                SelectionSet selectionSet = selectionResult.Value;
                ObjectId[] ids = selectionSet.GetObjectIds();

                int i;

                for (i = 0; i < ids.Length; i++)
                {
                    Entity entity = tr.GetObject(ids[i], OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        continue;
                    }

                    CollectTextRowsFromEntity(
                        tr,
                        entity,
                        Matrix3d.Identity,
                        rows,
                        minPoint,
                        maxPoint,
                        0
                    );
                }

                tr.Commit();
            }

            RemoveDuplicateRows(rows);

            return rows;
        }


        private List<OviaBarTableRow> BuildOviaGridTableRows(
            List<OviaTextRow> textRows,
            List<OviaGridLineSegment> gridLines,
            Point3d minPoint,
            Point3d maxPoint,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            out string diagnostic
        )
        {
            diagnostic = "";
            List<OviaBarTableRow> result = new List<OviaBarTableRow>();
            lastDetectedHeaderColumns = new List<OviaHeaderColumn>();

            if (textRows == null || textRows.Count == 0 || gridLines == null || gridLines.Count == 0)
            {
                diagnostic = "문자 또는 표 선 후보가 부족합니다.";
                return result;
            }

            double tableWidth = Math.Abs(maxPoint.X - minPoint.X);
            double tableHeight = Math.Abs(maxPoint.Y - minPoint.Y);

            if (tableWidth <= 0.0001 || tableHeight <= 0.0001)
            {
                diagnostic = "선택박스 크기가 올바르지 않습니다.";
                return result;
            }

            double axisTolerance = Math.Max(Math.Min(tableWidth, tableHeight) * 0.003, 0.5);
            double mergeTolerance = Math.Max(Math.Min(tableWidth, tableHeight) * 0.004, 1.0);

            /*
             * OVIA 2026-05-22 개선:
             * 도면업체 표는 하나의 긴 선이 아니라 셀 단위의 짧은 선 조각으로
             * 가로/세로 경계가 구성되는 경우가 많습니다.
             *
             * 기존 방식은 "개별 선 길이"가 충분히 긴 선만 표 경계로 보았기 때문에,
             * 짧은 선 조각으로 구성된 행 경계선을 놓치고 여러 행을 하나로 합치는 문제가 있었습니다.
             *
             * 개선 방식은 같은 Y 또는 X 좌표에 있는 짧은 선 조각들을 먼저 묶고,
             * 해당 좌표에서 실제로 덮고 있는 전체 길이가 표 폭/높이의 일정 비율 이상이면
             * 표 경계선으로 인정합니다.
             *
             * 철근형상 칸 내부의 작은 치수선은 한 셀 안에만 있으므로
             * 전체 표 폭/높이를 충분히 덮지 못해 표 경계선에서 제외됩니다.
             */
            double minHorizontalSegmentLength = Math.Max(tableWidth * 0.015, 0.5);
            double minVerticalSegmentLength = Math.Max(tableHeight * 0.015, 0.5);
            double minHorizontalCoverage = tableWidth * 0.50;
            double minVerticalCoverage = tableHeight * 0.50;

            List<double> verticalXs = ExtractCoveredGridCoordinates(
                gridLines,
                true,
                axisTolerance,
                mergeTolerance,
                minVerticalSegmentLength,
                minVerticalCoverage,
                minPoint.Y,
                maxPoint.Y
            );

            List<double> horizontalYs = ExtractCoveredGridCoordinates(
                gridLines,
                false,
                axisTolerance,
                mergeTolerance,
                minHorizontalSegmentLength,
                minHorizontalCoverage,
                minPoint.X,
                maxPoint.X
            );

            /*
             * 예외 도면 대응:
             * 표 선이 실제로 긴 단일 선으로 구성되어 있는데 커버리지 계산에서 누락되는 경우를 대비해
             * 기존 긴 선 기반 방식으로 한 번 더 보정합니다.
             */
            if (verticalXs.Count < 3 || horizontalYs.Count < 3)
            {
                double minHorizontalLength = tableWidth * 0.45;
                double minVerticalLength = tableHeight * 0.35;

                List<double> fallbackVerticalXs = new List<double>();
                List<double> fallbackHorizontalYs = new List<double>();

                int fallbackIndex;

                for (fallbackIndex = 0; fallbackIndex < gridLines.Count; fallbackIndex++)
                {
                    OviaGridLineSegment fallbackSegment = gridLines[fallbackIndex];

                    if (fallbackSegment == null)
                    {
                        continue;
                    }

                    double dx = Math.Abs(fallbackSegment.X1 - fallbackSegment.X2);
                    double dy = Math.Abs(fallbackSegment.Y1 - fallbackSegment.Y2);

                    if (dx <= axisTolerance && dy >= minVerticalLength)
                    {
                        fallbackVerticalXs.Add((fallbackSegment.X1 + fallbackSegment.X2) / 2.0);
                    }
                    else if (dy <= axisTolerance && dx >= minHorizontalLength)
                    {
                        fallbackHorizontalYs.Add((fallbackSegment.Y1 + fallbackSegment.Y2) / 2.0);
                    }
                }

                fallbackVerticalXs = MergeGridCoordinates(fallbackVerticalXs, mergeTolerance, true);
                fallbackHorizontalYs = MergeGridCoordinates(fallbackHorizontalYs, mergeTolerance, false);

                if (verticalXs.Count < fallbackVerticalXs.Count)
                {
                    verticalXs = fallbackVerticalXs;
                }

                if (horizontalYs.Count < fallbackHorizontalYs.Count)
                {
                    horizontalYs = fallbackHorizontalYs;
                }
            }

            // 헤더를 선택하지 않아도 분석창에서 상단 헤더를 찾아 실제 표 컬럼 경계만 유지합니다.
            // 철근형상 내부의 작은 사각형/치수선이 여러 행에서 같은 X좌표로 반복되면
            // 표 세로선으로 오인될 수 있으므로, 헤더 행을 관통하지 않는 세로선 후보는 제거합니다.
            List<double> headerFilteredVerticalXs = FilterVerticalCoordinatesByHeaderBand(verticalXs, gridLines, textRows, axisTolerance, mergeTolerance);

            if (headerFilteredVerticalXs != null && headerFilteredVerticalXs.Count >= 3)
            {
                verticalXs = headerFilteredVerticalXs;
            }

            int i;

            if (verticalXs.Count < 3 || horizontalYs.Count < 3)
            {
                diagnostic = "표 경계선 부족: 세로선 " + verticalXs.Count.ToString() + "개, 가로선 " + horizontalYs.Count.ToString() + "개";
                return result;
            }

            string[,] cellTexts = BuildGridCellTextMatrix(textRows, verticalXs, horizontalYs, mergeTolerance);

            if (cellTexts == null)
            {
                diagnostic = "셀 텍스트 매트릭스를 만들지 못했습니다.";
                return result;
            }

            int headerRowIndex = DetectGridHeaderRow(cellTexts, verticalXs, horizontalYs);
            int rowCount = horizontalYs.Count - 1;
            int colCount = verticalXs.Count - 1;

            List<OviaHeaderColumn> columns = null;

            if (headerRowIndex >= 0)
            {
                columns = BuildGridHeaderColumns(cellTexts, verticalXs, headerRowIndex);
            }

            if (columns == null || columns.Count < 3)
            {
                /*
                 * 대표님이 원하는 흐름:
                 * 사용자는 헤더까지 매번 선택하지 않고, 필요한 데이터 행 구간만 선택합니다.
                 * 헤더 자동 탐색이 실패하더라도 표 선 기준 컬럼 수가 확인되면
                 * 철근 집계표 기본 순서로 임시 매핑합니다.
                 */
                columns = CreateGridFallbackHeaderColumns(colCount);
                headerRowIndex = -1;
            }

            if (columns == null || columns.Count < 3)
            {
                diagnostic = "표준 컬럼으로 매핑 가능한 헤더가 부족합니다.";
                return result;
            }

            ApplyGridHeaderColumnBoundsFromLines(columns, verticalXs);

            lastDetectedHeaderColumns = columns;

            int rowNo = 1;
            int firstDataRowIndex = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;
            bool useSelectedRangeFilter = Math.Abs(selectedMaxPoint.Y - selectedMinPoint.Y) > 0.0001;

            for (i = firstDataRowIndex; i < rowCount; i++)
            {
                double rowTopY = Math.Max(horizontalYs[i], horizontalYs[i + 1]);
                double rowBottomY = Math.Min(horizontalYs[i], horizontalYs[i + 1]);
                double rowCenterY = (rowTopY + rowBottomY) / 2.0;

                if (useSelectedRangeFilter)
                {
                    if (rowCenterY < selectedMinPoint.Y - mergeTolerance || rowCenterY > selectedMaxPoint.Y + mergeTolerance)
                    {
                        continue;
                    }
                }

                string rawText = JoinGridRowText(cellTexts, i, colCount);

                if (IsEmptyNoiseRow(rawText))
                {
                    continue;
                }

                if (IsHeaderRow(rawText))
                {
                    continue;
                }

                OviaBarTableRow row = new OviaBarTableRow();
                row.No = rowNo;
                row.SourceRowNo = i + 1;
                row.RawText = rawText;
                row.RowType = "DATA";

                if (rawText.IndexOf("총계", StringComparison.OrdinalIgnoreCase) >= 0 || rawText.IndexOf("합계", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.RowType = "TOTAL";
                }
                else if (rawText.IndexOf("소계", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.RowType = "SUBTOTAL";
                }

                int c;

                for (c = 0; c < colCount; c++)
                {
                    string value = cellTexts[i, c];
                    value = CleanCellText(value);

                    OviaHeaderColumn column = FindGridHeaderColumnByColumnIndex(columns, c);

                    if (column == null)
                    {
                        continue;
                    }

                    /*
                     * CAD 형상 캡처는 철근형상 칸에 텍스트가 없어도 반드시 셀 좌표가 필요합니다.
                     * 기존 코드는 value == ""이면 먼저 continue 되어 형상 셀 좌표가 비어버릴 수 있었습니다.
                     */
                    if (column.StandardKey == "SHAPE")
                    {
                        row.ShapeCellMinX = Math.Min(verticalXs[c], verticalXs[c + 1]);
                        row.ShapeCellMaxX = Math.Max(verticalXs[c], verticalXs[c + 1]);
                        row.ShapeCellMinY = rowBottomY;
                        row.ShapeCellMaxY = rowTopY;
                    }

                    if (value == "")
                    {
                        continue;
                    }

                    ApplyValueByStandardKey(row, column.StandardKey, value);
                }

                if (row.MarkNo == "" && row.BarNo == "")
                {
                    string recoveredMarkNo = RecoverGridMarkNo(textRows, rowTopY, rowBottomY, columns, verticalXs, mergeTolerance, rowNo);

                    if (recoveredMarkNo == "")
                    {
                        recoveredMarkNo = RecoverGridMarkNoFromRawText(rawText);
                    }

                    if (recoveredMarkNo != "")
                    {
                        row.MarkNo = recoveredMarkNo;
                        row.BarNo = recoveredMarkNo;
                    }
                }

                if (row.Spec == "")
                {
                    string detectedSpec = DetectSpec(rawText);

                    if (detectedSpec != "")
                    {
                        row.Spec = detectedSpec;
                    }
                }

                if (row.ShapeDimensionText == "" && row.ShapeText != "")
                {
                    row.ShapeDimensionText = ExtractNumbersText(row.ShapeText);
                }

                // CAD 원본 번호는 행의 가장 왼쪽 번호 셀에 있는 값이 최우선입니다.
                // 렌더링/형상 치수 보정 과정에서 형상 내부 숫자(590, 470 등)가 번호로 섞이지 않도록
                // 같은 행의 HD10 등 규격값보다 왼쪽에 있는 순수 숫자만 번호 후보로 인정합니다.
                string leftMostMarkNo = RecoverGridLeftMostMarkNo(textRows, rowTopY, rowBottomY, mergeTolerance);

                if (leftMostMarkNo != "")
                {
                    row.MarkNo = leftMostMarkNo;
                    row.BarNo = leftMostMarkNo;
                }

                if (row.RowType == "DATA")
                {
                    SupplementStandardDataFromRawText(rawText, row);
                }

                if (row.RowType == "SUBTOTAL" || row.RowType == "TOTAL")
                {
                    if (row.MarkNo == "")
                    {
                        row.MarkNo = row.RowType == "SUBTOTAL" ? "소계" : "총계";
                        row.BarNo = row.MarkNo;
                    }
                }

                if (IsMeaninglessTableRow(row))
                {
                    continue;
                }

                result.Add(row);
                rowNo++;
            }

            diagnostic = "세로선 " + verticalXs.Count.ToString() + "개, 가로선 " + horizontalYs.Count.ToString() + "개, " + (headerRowIndex >= 0 ? "헤더 행 " + (headerRowIndex + 1).ToString() + "번" : "기본 컬럼 순서 적용");
            return result;
        }

        private void ApplyGridHeaderColumnBoundsFromLines(List<OviaHeaderColumn> columns, List<double> verticalXs)
        {
            if (columns == null || verticalXs == null || verticalXs.Count < 2)
            {
                return;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                OviaHeaderColumn column = columns[i];

                if (column == null)
                {
                    continue;
                }

                int sourceIndex = column.SourceColumnIndex;

                if (sourceIndex < 0 || sourceIndex >= verticalXs.Count - 1)
                {
                    continue;
                }

                double left = Math.Min(verticalXs[sourceIndex], verticalXs[sourceIndex + 1]);
                double right = Math.Max(verticalXs[sourceIndex], verticalXs[sourceIndex + 1]);

                column.LeftX = left;
                column.RightX = right;
                column.X = (left + right) / 2.0;
            }
        }


        private List<double> FilterVerticalCoordinatesByHeaderBand(List<double> verticalXs, List<OviaGridLineSegment> gridLines, List<OviaTextRow> textRows, double axisTolerance, double mergeTolerance)
        {
            if (verticalXs == null || verticalXs.Count < 3 || gridLines == null || gridLines.Count == 0 || textRows == null || textRows.Count == 0)
            {
                return verticalXs;
            }

            double headerY = FindLikelyGridHeaderY(textRows);

            if (headerY == Double.MinValue)
            {
                return verticalXs;
            }

            List<double> filtered = new List<double>();
            double xTolerance = Math.Max(mergeTolerance * 1.5, axisTolerance * 3.0);
            double yTolerance = Math.Max(mergeTolerance * 0.75, 0.5);
            int i;

            for (i = 0; i < verticalXs.Count; i++)
            {
                double x = verticalXs[i];

                if (HasVerticalGridLineAcrossY(gridLines, x, headerY, xTolerance, yTolerance))
                {
                    filtered.Add(x);
                }
            }

            // 필터 후 컬럼 수가 과도하게 줄면 기존 후보를 유지합니다.
            // 단, 철근형상 내부선이 제거되어 후보 수가 줄어드는 정상 케이스는 필터 결과를 사용합니다.
            if (filtered.Count >= 3 && filtered.Count <= verticalXs.Count)
            {
                return MergeGridCoordinates(filtered, mergeTolerance, true);
            }

            return verticalXs;
        }

        private double FindLikelyGridHeaderY(List<OviaTextRow> textRows)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return Double.MinValue;
            }

            Dictionary<string, HeaderYVote> votes = new Dictionary<string, HeaderYVote>();
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                string text = CleanHeaderText(textRow.TextValue);

                if (text == "")
                {
                    continue;
                }

                string key = ClassifyGridHeaderTitle(text, false);

                if (key == "")
                {
                    key = ClassifyHeaderTitle(text);
                }

                if (key == "")
                {
                    continue;
                }

                // 비고만 단독으로 잡힌 행은 헤더 후보로 약하므로 제외합니다.
                if (key == "NOTE")
                {
                    continue;
                }

                string bucket = Math.Round(textRow.Y, 2).ToString("0.00", CultureInfo.InvariantCulture);
                HeaderYVote vote;

                if (!votes.TryGetValue(bucket, out vote))
                {
                    vote = new HeaderYVote();
                    vote.Y = textRow.Y;
                    votes.Add(bucket, vote);
                }

                if (!ContainsText(vote.Keys, key))
                {
                    vote.Keys.Add(key);
                }

                vote.Count++;
            }

            HeaderYVote best = null;

            foreach (HeaderYVote vote in votes.Values)
            {
                if (vote == null)
                {
                    continue;
                }

                int score = vote.Keys.Count * 10 + vote.Count;

                if (ContainsText(vote.Keys, "MARK_NO"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "SPEC"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "SHAPE"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "LENGTH_MM"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "QUANTITY_EA"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "TOTAL_LENGTH_M"))
                {
                    score += 5;
                }

                if (ContainsText(vote.Keys, "TOTAL_WEIGHT"))
                {
                    score += 5;
                }

                vote.Score = score;

                if (best == null || vote.Score > best.Score)
                {
                    best = vote;
                }
            }

            if (best == null || best.Keys.Count < 3)
            {
                return Double.MinValue;
            }

            return best.Y;
        }

        private bool HasVerticalGridLineAcrossY(List<OviaGridLineSegment> gridLines, double x, double y, double xTolerance, double yTolerance)
        {
            if (gridLines == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < gridLines.Count; i++)
            {
                OviaGridLineSegment segment = gridLines[i];

                if (segment == null)
                {
                    continue;
                }

                double dx = Math.Abs(segment.X1 - segment.X2);

                if (dx > xTolerance)
                {
                    continue;
                }

                double segmentX = (segment.X1 + segment.X2) / 2.0;

                if (Math.Abs(segmentX - x) > xTolerance)
                {
                    continue;
                }

                double minY = Math.Min(segment.Y1, segment.Y2) - yTolerance;
                double maxY = Math.Max(segment.Y1, segment.Y2) + yTolerance;

                if (y >= minY && y <= maxY)
                {
                    return true;
                }
            }

            return false;
        }


        private bool ContainsText(List<string> list, string value)
        {
            if (list == null || value == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (String.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private List<OviaGridLineSegment> ExtractGridLineSegmentsByWindow(Editor ed, Database db, Point3d point1, Point3d point2)
        {
            List<OviaGridLineSegment> segments = new List<OviaGridLineSegment>();

            Point3d minPoint = new Point3d(
                Math.Min(point1.X, point2.X),
                Math.Min(point1.Y, point2.Y),
                Math.Min(point1.Z, point2.Z)
            );

            Point3d maxPoint = new Point3d(
                Math.Max(point1.X, point2.X),
                Math.Max(point1.Y, point2.Y),
                Math.Max(point1.Z, point2.Z)
            );

            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(point1, point2);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return segments;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId[] ids = selectionResult.Value.GetObjectIds();
                int i;

                for (i = 0; i < ids.Length; i++)
                {
                    Entity entity = tr.GetObject(ids[i], OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        continue;
                    }

                    CollectGridLineSegmentsFromEntity(
                        tr,
                        entity,
                        Matrix3d.Identity,
                        segments,
                        minPoint,
                        maxPoint,
                        0
                    );
                }

                tr.Commit();
            }

            return segments;
        }

        private void CollectGridLineSegmentsFromEntity(
            Transaction tr,
            Entity entity,
            Matrix3d transform,
            List<OviaGridLineSegment> segments,
            Point3d minPoint,
            Point3d maxPoint,
            int depth
        )
        {
            if (entity == null || segments == null)
            {
                return;
            }

            if (depth > 8)
            {
                return;
            }

            if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Line line = entity as Line;

            if (line != null)
            {
                AddGridLineSegmentCandidate(
                    line.StartPoint.TransformBy(transform),
                    line.EndPoint.TransformBy(transform),
                    segments,
                    minPoint,
                    maxPoint,
                    "Line"
                );

                return;
            }

            Polyline polyline = entity as Polyline;

            if (polyline != null)
            {
                int count = polyline.NumberOfVertices;
                int i;

                for (i = 0; i < count - 1; i++)
                {
                    AddGridLineSegmentCandidate(
                        polyline.GetPoint3dAt(i).TransformBy(transform),
                        polyline.GetPoint3dAt(i + 1).TransformBy(transform),
                        segments,
                        minPoint,
                        maxPoint,
                        "Polyline"
                    );
                }

                if (polyline.Closed && count > 1)
                {
                    AddGridLineSegmentCandidate(
                        polyline.GetPoint3dAt(count - 1).TransformBy(transform),
                        polyline.GetPoint3dAt(0).TransformBy(transform),
                        segments,
                        minPoint,
                        maxPoint,
                        "Polyline"
                    );
                }

                return;
            }

            BlockReference blockReference = entity as BlockReference;

            if (blockReference != null)
            {
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                if (blockRecord != null)
                {
                    Matrix3d nextTransform = transform * blockReference.BlockTransform;

                    foreach (ObjectId childId in blockRecord)
                    {
                        Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                        if (childEntity == null)
                        {
                            continue;
                        }

                        CollectGridLineSegmentsFromEntity(
                            tr,
                            childEntity,
                            nextTransform,
                            segments,
                            minPoint,
                            maxPoint,
                            depth + 1
                        );
                    }
                }

                CollectGridLineSegmentsFromExplodedBlock(
                    blockReference,
                    segments,
                    minPoint,
                    maxPoint,
                    depth + 1
                );

                return;
            }
        }

        private void CollectGridLineSegmentsFromExplodedBlock(
            BlockReference blockReference,
            List<OviaGridLineSegment> segments,
            Point3d minPoint,
            Point3d maxPoint,
            int depth
        )
        {
            if (blockReference == null || segments == null)
            {
                return;
            }

            if (depth > 8)
            {
                return;
            }

            DBObjectCollection explodedObjects = new DBObjectCollection();

            try
            {
                blockReference.Explode(explodedObjects);
            }
            catch
            {
                return;
            }

            foreach (DBObject dbObject in explodedObjects)
            {
                Entity explodedEntity = dbObject as Entity;

                if (explodedEntity == null)
                {
                    if (dbObject != null)
                    {
                        dbObject.Dispose();
                    }

                    continue;
                }

                try
                {
                    Line line = explodedEntity as Line;

                    if (line != null)
                    {
                        AddGridLineSegmentCandidate(line.StartPoint, line.EndPoint, segments, minPoint, maxPoint, "ExplodedLine");
                        continue;
                    }

                    Polyline polyline = explodedEntity as Polyline;

                    if (polyline != null)
                    {
                        int count = polyline.NumberOfVertices;
                        int i;

                        for (i = 0; i < count - 1; i++)
                        {
                            AddGridLineSegmentCandidate(
                                polyline.GetPoint3dAt(i),
                                polyline.GetPoint3dAt(i + 1),
                                segments,
                                minPoint,
                                maxPoint,
                                "ExplodedPolyline"
                            );
                        }

                        if (polyline.Closed && count > 1)
                        {
                            AddGridLineSegmentCandidate(
                                polyline.GetPoint3dAt(count - 1),
                                polyline.GetPoint3dAt(0),
                                segments,
                                minPoint,
                                maxPoint,
                                "ExplodedPolyline"
                            );
                        }

                        continue;
                    }

                    BlockReference nestedBlock = explodedEntity as BlockReference;

                    if (nestedBlock != null)
                    {
                        CollectGridLineSegmentsFromExplodedBlock(nestedBlock, segments, minPoint, maxPoint, depth + 1);
                        continue;
                    }
                }
                finally
                {
                    explodedEntity.Dispose();
                }
            }
        }

        private void AddGridLineSegmentCandidate(
            Point3d point1,
            Point3d point2,
            List<OviaGridLineSegment> segments,
            Point3d minPoint,
            Point3d maxPoint,
            string sourceType
        )
        {
            if (segments == null)
            {
                return;
            }

            if (!IsPointInsideWindow(point1, minPoint, maxPoint) && !IsPointInsideWindow(point2, minPoint, maxPoint))
            {
                return;
            }

            OviaGridLineSegment segment = new OviaGridLineSegment();
            segment.X1 = point1.X;
            segment.Y1 = point1.Y;
            segment.X2 = point2.X;
            segment.Y2 = point2.Y;
            segment.SourceType = sourceType == null ? "" : sourceType;
            segments.Add(segment);
        }

        private List<double> ExtractCoveredGridCoordinates(
            List<OviaGridLineSegment> segments,
            bool vertical,
            double axisTolerance,
            double mergeTolerance,
            double minSegmentLength,
            double minCoverageLength,
            double rangeStart,
            double rangeEnd
        )
        {
            List<OviaGridAxisSegment> candidates = new List<OviaGridAxisSegment>();

            if (segments == null || segments.Count == 0)
            {
                return new List<double>();
            }

            double rangeMin = Math.Min(rangeStart, rangeEnd);
            double rangeMax = Math.Max(rangeStart, rangeEnd);

            int i;

            for (i = 0; i < segments.Count; i++)
            {
                OviaGridLineSegment segment = segments[i];

                if (segment == null)
                {
                    continue;
                }

                double dx = Math.Abs(segment.X1 - segment.X2);
                double dy = Math.Abs(segment.Y1 - segment.Y2);

                if (vertical)
                {
                    if (dx > axisTolerance || dy < minSegmentLength)
                    {
                        continue;
                    }

                    OviaGridAxisSegment candidate = new OviaGridAxisSegment();
                    candidate.Coordinate = (segment.X1 + segment.X2) / 2.0;
                    candidate.Start = Math.Max(rangeMin, Math.Min(segment.Y1, segment.Y2));
                    candidate.End = Math.Min(rangeMax, Math.Max(segment.Y1, segment.Y2));

                    if (candidate.End > candidate.Start)
                    {
                        candidates.Add(candidate);
                    }
                }
                else
                {
                    if (dy > axisTolerance || dx < minSegmentLength)
                    {
                        continue;
                    }

                    OviaGridAxisSegment candidate = new OviaGridAxisSegment();
                    candidate.Coordinate = (segment.Y1 + segment.Y2) / 2.0;
                    candidate.Start = Math.Max(rangeMin, Math.Min(segment.X1, segment.X2));
                    candidate.End = Math.Min(rangeMax, Math.Max(segment.X1, segment.X2));

                    if (candidate.End > candidate.Start)
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return new List<double>();
            }

            candidates.Sort(delegate (OviaGridAxisSegment a, OviaGridAxisSegment b)
            {
                if (a.Coordinate < b.Coordinate)
                {
                    return -1;
                }

                if (a.Coordinate > b.Coordinate)
                {
                    return 1;
                }

                return 0;
            });

            List<double> result = new List<double>();
            int index = 0;

            while (index < candidates.Count)
            {
                List<OviaGridAxisSegment> cluster = new List<OviaGridAxisSegment>();
                double baseCoordinate = candidates[index].Coordinate;

                while (index < candidates.Count && Math.Abs(candidates[index].Coordinate - baseCoordinate) <= mergeTolerance)
                {
                    cluster.Add(candidates[index]);
                    index++;
                }

                if (cluster.Count == 0)
                {
                    continue;
                }

                double coordinateSum = 0;
                int coordinateCount = 0;

                for (i = 0; i < cluster.Count; i++)
                {
                    coordinateSum += cluster[i].Coordinate;
                    coordinateCount++;
                }

                double coveredLength = GetMergedIntervalLength(cluster, mergeTolerance);

                if (coveredLength >= minCoverageLength)
                {
                    result.Add(coordinateSum / (double)coordinateCount);
                }
            }

            result = MergeGridCoordinates(result, mergeTolerance, true);

            if (!vertical)
            {
                result.Reverse();
            }

            return result;
        }

        private double GetMergedIntervalLength(List<OviaGridAxisSegment> intervals, double tolerance)
        {
            if (intervals == null || intervals.Count == 0)
            {
                return 0;
            }

            intervals.Sort(delegate (OviaGridAxisSegment a, OviaGridAxisSegment b)
            {
                if (a.Start < b.Start)
                {
                    return -1;
                }

                if (a.Start > b.Start)
                {
                    return 1;
                }

                return 0;
            });

            double total = 0;
            double currentStart = intervals[0].Start;
            double currentEnd = intervals[0].End;

            int i;

            for (i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].Start <= currentEnd + tolerance)
                {
                    if (intervals[i].End > currentEnd)
                    {
                        currentEnd = intervals[i].End;
                    }
                }
                else
                {
                    total += Math.Max(0, currentEnd - currentStart);
                    currentStart = intervals[i].Start;
                    currentEnd = intervals[i].End;
                }
            }

            total += Math.Max(0, currentEnd - currentStart);

            return total;
        }

        private List<double> MergeGridCoordinates(List<double> values, double tolerance, bool ascending)
        {
            List<double> result = new List<double>();

            if (values == null || values.Count == 0)
            {
                return result;
            }

            values.Sort();

            int i = 0;

            while (i < values.Count)
            {
                double sum = values[i];
                int count = 1;
                int j = i + 1;

                while (j < values.Count && Math.Abs(values[j] - values[i]) <= tolerance)
                {
                    sum += values[j];
                    count++;
                    j++;
                }

                result.Add(sum / (double)count);
                i = j;
            }

            if (!ascending)
            {
                result.Reverse();
            }

            return result;
        }

        private string[,] BuildGridCellTextMatrix(
            List<OviaTextRow> textRows,
            List<double> verticalXs,
            List<double> horizontalYs,
            double tolerance
        )
        {
            int rowCount = horizontalYs.Count - 1;
            int colCount = verticalXs.Count - 1;

            if (rowCount <= 0 || colCount <= 0)
            {
                return null;
            }

            List<OviaTextRow>[,] cellRows = new List<OviaTextRow>[rowCount, colCount];
            int r;
            int c;

            for (r = 0; r < rowCount; r++)
            {
                for (c = 0; c < colCount; c++)
                {
                    cellRows[r, c] = new List<OviaTextRow>();
                }
            }

            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null || CleanCellText(text.TextValue) == "")
                {
                    continue;
                }

                int rowIndex = FindGridRowIndex(horizontalYs, text.Y, tolerance);
                int colIndex = FindGridColumnIndex(verticalXs, text.X, tolerance);

                if (rowIndex < 0 || colIndex < 0)
                {
                    continue;
                }

                cellRows[rowIndex, colIndex].Add(text);
            }

            string[,] cellTexts = new string[rowCount, colCount];

            for (r = 0; r < rowCount; r++)
            {
                for (c = 0; c < colCount; c++)
                {
                    cellTexts[r, c] = JoinGridCellTexts(cellRows[r, c]);
                }
            }

            return cellTexts;
        }

        private int FindGridColumnIndex(List<double> verticalXs, double x, double tolerance)
        {
            if (verticalXs == null || verticalXs.Count < 2)
            {
                return -1;
            }

            int i;

            for (i = 0; i < verticalXs.Count - 1; i++)
            {
                double left = Math.Min(verticalXs[i], verticalXs[i + 1]);
                double right = Math.Max(verticalXs[i], verticalXs[i + 1]);

                if (x >= left - tolerance && x <= right + tolerance)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindGridRowIndex(List<double> horizontalYs, double y, double tolerance)
        {
            if (horizontalYs == null || horizontalYs.Count < 2)
            {
                return -1;
            }

            int i;

            for (i = 0; i < horizontalYs.Count - 1; i++)
            {
                double top = Math.Max(horizontalYs[i], horizontalYs[i + 1]);
                double bottom = Math.Min(horizontalYs[i], horizontalYs[i + 1]);

                if (y <= top + tolerance && y >= bottom - tolerance)
                {
                    return i;
                }
            }

            return -1;
        }

        private string RecoverGridMarkNoFromRawText(string rawText)
        {
            if (rawText == null)
            {
                return "";
            }

            string text = CleanCellText(rawText);

            if (text == "")
            {
                return "";
            }

            string[] parts = text.Split(new char[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts == null || parts.Length == 0)
            {
                return "";
            }

            string first = CleanCellText(parts[0]);

            if (first == "")
            {
                return "";
            }

            // 번호는 행의 첫 번째 토큰이어야 합니다.
            // 첫 번째가 HD10 같은 규격이면 형상 내부 숫자를 번호로 오인하지 않기 위해 복구하지 않습니다.
            if (!Regex.IsMatch(first, @"^\d{1,5}$"))
            {
                return "";
            }

            return first;
        }

        private string RecoverGridLeftMostMarkNo(List<OviaTextRow> textRows, double rowTopY, double rowBottomY, double tolerance)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return "";
            }

            List<OviaTextRow> rowTexts = new List<OviaTextRow>();
            double yMargin = Math.Max(tolerance * 2.5, 0.5);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.Y < rowBottomY - yMargin || textRow.Y > rowTopY + yMargin)
                {
                    continue;
                }

                string value = CleanCellText(textRow.TextValue);

                if (value == "")
                {
                    continue;
                }

                if (IsHeaderRow(value) || IsSummaryText(value))
                {
                    continue;
                }

                rowTexts.Add(textRow);
            }

            if (rowTexts.Count == 0)
            {
                return "";
            }

            rowTexts.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                return a.X.CompareTo(b.X);
            });

            double minX = rowTexts[0].X;
            double maxX = rowTexts[0].X;
            double firstSpecX = Double.MaxValue;

            for (i = 0; i < rowTexts.Count; i++)
            {
                OviaTextRow textRow = rowTexts[i];
                string value = CleanCellText(textRow.TextValue);
                string compact = NormalizeGridHeaderText(value);
                string firstToken = GetFirstMeaningfulToken(value);

                if (textRow.X < minX)
                {
                    minX = textRow.X;
                }

                if (textRow.X > maxX)
                {
                    maxX = textRow.X;
                }

                if (firstSpecX == Double.MaxValue && (IsRebarSpecToken(firstToken) || IsRebarSpecToken(compact)))
                {
                    firstSpecX = textRow.X;
                }
            }

            double rowWidth = Math.Abs(maxX - minX);

            if (rowWidth <= 0.0001)
            {
                rowWidth = 1.0;
            }

            for (i = 0; i < rowTexts.Count; i++)
            {
                OviaTextRow textRow = rowTexts[i];
                string value = CleanCellText(textRow.TextValue);
                string candidate = FirstSimpleNumber(value);

                if (candidate == "")
                {
                    continue;
                }

                if (!Regex.IsMatch(candidate, @"^\d{1,5}$"))
                {
                    continue;
                }

                // 번호는 규격 컬럼보다 반드시 왼쪽에 있어야 합니다.
                // 이 조건을 통과하지 못하면 철근형상 내부 치수값을 번호로 오인할 수 있으므로 버립니다.
                if (firstSpecX != Double.MaxValue)
                {
                    if (textRow.X < firstSpecX - Math.Max(tolerance, 0.5))
                    {
                        return candidate;
                    }

                    continue;
                }

                // 규격 텍스트를 찾지 못한 예외 도면에서는 행 전체의 가장 왼쪽 18% 안에 있는 숫자만 번호로 인정합니다.
                if (textRow.X <= minX + rowWidth * 0.18)
                {
                    return candidate;
                }
            }

            return "";
        }

        private string RecoverGridMarkNo(List<OviaTextRow> textRows, double rowTopY, double rowBottomY, List<OviaHeaderColumn> columns, List<double> verticalXs, double tolerance, int fallbackNo)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return "";
            }

            double leftX = 0;
            double rightX = 0;
            double centerX = 0;

            OviaHeaderColumn noColumn = FindHeaderColumnByKey(columns, "MARK_NO");

            if (noColumn != null)
            {
                leftX = noColumn.LeftX;
                rightX = noColumn.RightX;
                centerX = (leftX + rightX) / 2.0;
            }
            else if (verticalXs != null && verticalXs.Count >= 2)
            {
                leftX = Math.Min(verticalXs[0], verticalXs[1]);
                rightX = Math.Max(verticalXs[0], verticalXs[1]);
                centerX = (leftX + rightX) / 2.0;
            }
            else
            {
                return "";
            }

            double width = Math.Abs(rightX - leftX);

            if (width <= 0.0001)
            {
                width = 1.0;
            }

            double xMargin = Math.Max(width * 0.70, tolerance * 3.0);
            double yMargin = Math.Max(tolerance * 2.5, 0.5);
            string bestValue = "";
            double bestDistance = Double.MaxValue;

            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.Y < rowBottomY - yMargin || textRow.Y > rowTopY + yMargin)
                {
                    continue;
                }

                if (textRow.X < leftX - xMargin || textRow.X > rightX + xMargin)
                {
                    continue;
                }

                string value = CleanCellText(textRow.TextValue);

                if (value == "")
                {
                    continue;
                }

                if (IsHeaderRow(value) || IsSummaryText(value))
                {
                    continue;
                }

                string candidate = FirstSimpleNumber(value);

                if (candidate == "")
                {
                    continue;
                }

                if (!Regex.IsMatch(candidate, @"^\d{1,5}$"))
                {
                    continue;
                }

                double distance = Math.Abs(textRow.X - centerX);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestValue = candidate;
                }
            }

            if (bestValue != "")
            {
                return bestValue;
            }

            return "";
        }

        private string JoinGridCellTexts(List<OviaTextRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return "";
            }

            rows.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                if (Math.Abs(b.Y - a.Y) > Math.Max(Math.Max(a.Height, b.Height) * 0.4, 0.5))
                {
                    return b.Y.CompareTo(a.Y);
                }

                return a.X.CompareTo(b.X);
            });

            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                string value = CleanCellText(rows[i].TextValue);

                if (value == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" ");
                }

                sb.Append(value);
            }

            return sb.ToString().Trim();
        }

        private string JoinGridRowText(string[,] cellTexts, int rowIndex, int colCount)
        {
            StringBuilder sb = new StringBuilder();
            int c;

            for (c = 0; c < colCount; c++)
            {
                string value = cellTexts[rowIndex, c];

                if (value == null || value.Trim() == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" ");
                }

                sb.Append(value.Trim());
            }

            return sb.ToString();
        }

        private int DetectGridHeaderRow(string[,] cellTexts, List<double> verticalXs, List<double> horizontalYs)
        {
            if (cellTexts == null)
            {
                return -1;
            }

            int rowCount = horizontalYs.Count - 1;
            int colCount = verticalXs.Count - 1;
            int bestRow = -1;
            int bestScore = 0;
            int r;

            for (r = 0; r < rowCount; r++)
            {
                List<OviaHeaderColumn> columns = BuildGridHeaderColumns(cellTexts, verticalXs, r);
                int score = GetHeaderScore(columns);

                if (HasImportantHeader(columns))
                {
                    score += 2;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRow = r;
                }
            }

            if (bestScore < 4)
            {
                return -1;
            }

            return bestRow;
        }

        private List<OviaHeaderColumn> BuildGridHeaderColumns(string[,] cellTexts, List<double> verticalXs, int headerRowIndex)
        {
            List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

            if (cellTexts == null || verticalXs == null || verticalXs.Count < 2)
            {
                return columns;
            }

            int colCount = verticalXs.Count - 1;
            int c;
            bool hasShapeNoHeader = false;

            for (c = 0; c < colCount; c++)
            {
                string titleCheck = CleanHeaderText(cellTexts[headerRowIndex, c]);
                string normalizedCheck = NormalizeGridHeaderText(titleCheck);

                if (normalizedCheck.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedCheck.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedCheck.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedCheck.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasShapeNoHeader = true;
                    break;
                }
            }

            for (c = 0; c < colCount; c++)
            {
                string title = CleanHeaderText(cellTexts[headerRowIndex, c]);

                if (title == "")
                {
                    continue;
                }

                string standardKey = ClassifyGridHeaderTitleByColumnValues(title, cellTexts, headerRowIndex, c, hasShapeNoHeader);

                if (standardKey == "")
                {
                    continue;
                }

                OviaHeaderColumn existing = FindGridHeaderColumnByColumnIndex(columns, c);

                if (existing != null)
                {
                    continue;
                }

                OviaHeaderColumn column = new OviaHeaderColumn();
                column.StandardKey = standardKey;
                column.OriginalTitle = GetGridOutputHeaderTitle(title, standardKey);
                column.X = (verticalXs[c] + verticalXs[c + 1]) / 2.0;
                column.LeftX = Math.Min(verticalXs[c], verticalXs[c + 1]);
                column.RightX = Math.Max(verticalXs[c], verticalXs[c + 1]);
                column.SourceColumnIndex = c;

                columns.Add(column);
            }

            return columns;
        }

        private string ClassifyGridHeaderTitleByColumnValues(string title, string[,] cellTexts, int headerRowIndex, int columnIndex, bool hasShapeNoHeader)
        {
            string normalizedTitle = NormalizeGridHeaderText(title);

            if (normalizedTitle == "")
            {
                return "";
            }

            string pattern = DetectGridColumnValuePattern(cellTexts, headerRowIndex, columnIndex);

            /*
             * OVIA 조건부 매핑 핵심 규칙
             * ------------------------------------------------------------
             * CAD 도면에서 "부호"라는 헤더는 업체마다 의미가 다릅니다.
             *  - 아래 값이 1, 2, 3, 4처럼 순번이면 OVIA 표준 컬럼 "번호"
             *  - 아래 값이 G1005, D1002처럼 문자+숫자 형번이면 "부호/명칭"
             *
             * 따라서 헤더명만 보고 고정하지 않고, 실제 데이터 값을 함께 분석합니다.
             */
            if (IsAmbiguousMarkHeader(normalizedTitle))
            {
                if (pattern == "sequential_number" || pattern == "integer_number")
                {
                    return "MARK_NO";
                }

                if (pattern == "alpha_numeric_mark")
                {
                    return "SYMBOL";
                }

                if (hasShapeNoHeader)
                {
                    return "MARK_NO";
                }

                if (normalizedTitle.IndexOf("명칭", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedTitle.IndexOf("NAME", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "SYMBOL";
                }

                return "MARK_NO";
            }

            if (normalizedTitle.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("형상코드", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SHAPE_NO";
            }

            return ClassifyGridHeaderTitle(title, hasShapeNoHeader);
        }

        private bool IsAmbiguousMarkHeader(string normalizedTitle)
        {
            if (normalizedTitle == null)
            {
                return false;
            }

            if (normalizedTitle.IndexOf("부호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("명칭", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle == "MARK" ||
                normalizedTitle == "BARMARK" ||
                normalizedTitle == "BARNO" ||
                normalizedTitle == "ITEM" ||
                normalizedTitle == "SYMBOL" ||
                normalizedTitle == "CODE")
            {
                return true;
            }

            return false;
        }

        private string DetectGridColumnValuePattern(string[,] cellTexts, int headerRowIndex, int columnIndex)
        {
            if (cellTexts == null)
            {
                return "unknown";
            }

            int rowCount = cellTexts.GetLength(0);
            int nonEmptyCount = 0;
            int integerCount = 0;
            int alphaNumericMarkCount = 0;
            int rebarSpecCount = 0;
            List<int> integerValues = new List<int>();

            int startRow = headerRowIndex + 1;
            int endRow = Math.Min(rowCount - 1, headerRowIndex + 30);
            int r;

            for (r = startRow; r <= endRow; r++)
            {
                string value = "";

                try
                {
                    value = cellTexts[r, columnIndex];
                }
                catch
                {
                    value = "";
                }

                value = CleanCellText(value);

                if (value == "")
                {
                    continue;
                }

                if (IsSummaryText(value) || IsHeaderRow(value))
                {
                    continue;
                }

                nonEmptyCount++;

                string compact = NormalizeGridHeaderText(value);
                string firstToken = GetFirstMeaningfulToken(value);

                if (IsSimpleIntegerToken(firstToken))
                {
                    integerCount++;
                    int n;
                    if (Int32.TryParse(firstToken.Replace(",", ""), out n))
                    {
                        integerValues.Add(n);
                    }
                }
                else if (IsAlphaNumericMarkToken(firstToken) || IsAlphaNumericMarkToken(compact))
                {
                    alphaNumericMarkCount++;
                }

                if (IsRebarSpecToken(firstToken) || IsRebarSpecToken(compact))
                {
                    rebarSpecCount++;
                }
            }

            if (nonEmptyCount == 0)
            {
                return "unknown";
            }

            double integerRatio = (double)integerCount / (double)nonEmptyCount;
            double markRatio = (double)alphaNumericMarkCount / (double)nonEmptyCount;
            double specRatio = (double)rebarSpecCount / (double)nonEmptyCount;

            if (integerRatio >= 0.70)
            {
                if (IsMostlySequentialIntegers(integerValues))
                {
                    return "sequential_number";
                }

                return "integer_number";
            }

            if (markRatio >= 0.50 && specRatio < 0.50)
            {
                return "alpha_numeric_mark";
            }

            if (specRatio >= 0.60)
            {
                return "rebar_spec";
            }

            return "unknown";
        }

        private string GetFirstMeaningfulToken(string value)
        {
            value = value == null ? "" : value.Trim();

            if (value == "")
            {
                return "";
            }

            string[] tokens = value.Split(new char[] { ' ', '\t', '\r', '\n', '/', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens == null || tokens.Length == 0)
            {
                return value;
            }

            return tokens[0].Trim();
        }

        private bool IsSimpleIntegerToken(string value)
        {
            value = value == null ? "" : value.Trim();

            if (value == "")
            {
                return false;
            }

            value = value.Replace(",", "");

            return Regex.IsMatch(value, @"^[0-9]{1,5}$");
        }

        private bool IsAlphaNumericMarkToken(string value)
        {
            value = value == null ? "" : value.Trim();

            if (value == "")
            {
                return false;
            }

            value = value.Replace(" ", "");
            value = value.Replace("-", "");
            value = value.Replace("_", "");

            if (!Regex.IsMatch(value, @"[A-Z가-힣]") || !Regex.IsMatch(value, @"[0-9]"))
            {
                return false;
            }

            if (IsRebarSpecToken(value))
            {
                return false;
            }

            return Regex.IsMatch(value, @"^[A-Z가-힣]{1,6}[0-9][A-Z0-9가-힣]*$");
        }

        private bool IsRebarSpecToken(string value)
        {
            value = value == null ? "" : value.Trim();

            if (value == "")
            {
                return false;
            }

            value = value.ToUpperInvariant();
            value = value.Replace(" ", "");
            value = value.Replace("-", "");
            value = value.Replace("_", "");

            if (Regex.IsMatch(value, @"^(SD|SHD|HD|UHD|D)[0-9]{1,3}$"))
            {
                return true;
            }

            return false;
        }

        private bool IsMostlySequentialIntegers(List<int> values)
        {
            if (values == null || values.Count < 2)
            {
                return values != null && values.Count == 1;
            }

            values.Sort();

            int sequentialSteps = 0;
            int totalSteps = 0;
            int i;

            for (i = 1; i < values.Count; i++)
            {
                if (values[i] == values[i - 1])
                {
                    continue;
                }

                totalSteps++;

                if (values[i] == values[i - 1] + 1)
                {
                    sequentialSteps++;
                }
            }

            if (totalSteps == 0)
            {
                return true;
            }

            return ((double)sequentialSteps / (double)totalSteps) >= 0.60;
        }

        private bool IsSummaryText(string value)
        {
            value = value == null ? "" : value.Trim();

            if (value.IndexOf("소계", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("합계", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("총계", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TOTAL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private string NormalizeGridHeaderText(string title)
        {
            if (title == null)
            {
                return "";
            }

            string value = title.ToUpperInvariant();
            value = value.Replace(" ", "");
            value = value.Replace("\t", "");
            value = value.Replace("\r", "");
            value = value.Replace("\n", "");
            value = value.Replace("_", "");
            value = value.Replace("-", "");
            value = value.Replace(".", "");
            value = value.Replace("(", "");
            value = value.Replace(")", "");
            value = value.Replace("[", "");
            value = value.Replace("]", "");

            return value;
        }

        private string ClassifyGridHeaderTitle(string title, bool hasShapeNoHeader)
        {
            string value = NormalizeGridHeaderText(title);

            if (value == "")
            {
                return "";
            }

            if (value.IndexOf("비고", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("NOTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("REMARK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "NOTE";
            }

            if (value.IndexOf("총중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WEIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT";
            }

            if (value.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("총연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TOTALLENGTH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_LENGTH_M";
            }

            if (value.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("본수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("개수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("QTY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("EA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "QUANTITY_EA";
            }

            if (value.IndexOf("길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("LENGTH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "LENGTH_MM";
            }

            if (value.IndexOf("철근규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("강종", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SIZE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("DIA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SPEC";
            }

            if (value.IndexOf("부위", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("위치", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("구간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PART";
            }

            if (value.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상코드", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SHAPE_NO";
            }

            if (value.IndexOf("철근형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SHAPE";
            }

            if (value.IndexOf("번호", StringComparison.OrdinalIgnoreCase) >= 0 || value == "NO" || value == "N")
            {
                return "MARK_NO";
            }

            if (value.IndexOf("부호", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SYMBOL";
            }

            if (value.IndexOf("MARK", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("BARNO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "MARK_NO";
            }

            return "";
        }

        private string GetGridOutputHeaderTitle(string originalTitle, string standardKey)
        {
            if (standardKey == "MARK_NO")
            {
                return "번호";
            }

            if (standardKey == "PART")
            {
                return "부위";
            }

            if (standardKey == "SYMBOL")
            {
                return "부호";
            }

            if (standardKey == "SHAPE_NO")
            {
                return "형상번호";
            }

            if (standardKey == "SHAPE")
            {
                return "철근형상";
            }

            if (standardKey == "SPEC")
            {
                return "규격";
            }

            if (standardKey == "LENGTH_MM")
            {
                return "길이(mm)";
            }

            if (standardKey == "QUANTITY_EA")
            {
                return "수량(EA)";
            }

            if (standardKey == "TOTAL_LENGTH_M")
            {
                return "총길이(M)";
            }

            if (standardKey == "TOTAL_WEIGHT")
            {
                if (originalTitle != null && originalTitle.ToUpperInvariant().IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "중량(KG)";
                }

                return "중량(TON)";
            }

            if (standardKey == "NOTE")
            {
                return "비고";
            }

            return NormalizeHeaderTitleForOutput(originalTitle, standardKey);
        }

        private OviaHeaderColumn FindGridHeaderColumnByColumnIndex(List<OviaHeaderColumn> columns, int columnIndex)
        {
            if (columns == null)
            {
                return null;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                if (columns[i].SourceColumnIndex == columnIndex)
                {
                    return columns[i];
                }
            }

            return null;
        }


        [CommandMethod("OVIABOXDEBUG")]
        public void OviaBoxDebug()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d minPoint;
            Point3d maxPoint;
            int boxCount = 0;

            bool hasBox = GetOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 도면에서 OVIA 선택박스를 찾지 못했습니다.\n");
                ed.WriteMessage("먼저 OVIABOX 명령어로 선택박스를 생성해주세요.\n");
                return;
            }

            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(minPoint, maxPoint);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                ed.WriteMessage("\nOVIA DEBUG: 선택박스 안에서 선택 가능한 객체를 찾지 못했습니다.\n");
                return;
            }

            Dictionary<string, int> typeCounts = new Dictionary<string, int>();
            List<string> samples = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId[] ids = selectionResult.Value.GetObjectIds();

                int i;

                for (i = 0; i < ids.Length; i++)
                {
                    Entity entity = tr.GetObject(ids[i], OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        continue;
                    }

                    string typeName = entity.GetType().Name;

                    if (!typeCounts.ContainsKey(typeName))
                    {
                        typeCounts[typeName] = 0;
                    }

                    typeCounts[typeName]++;

                    if (samples.Count < 30)
                    {
                        string sample = typeName + " / Layer=" + SafeText(entity.Layer);

                        BlockReference blockReference = entity as BlockReference;

                        if (blockReference != null)
                        {
                            sample += " / Block=" + GetBlockName(tr, blockReference);
                        }

                        samples.Add(sample);
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\n");
            ed.WriteMessage("====================================\n");
            ed.WriteMessage("OVIA 선택박스 객체 진단 결과\n");
            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("선택 객체 종류 수 : " + typeCounts.Count.ToString() + "\n");

            foreach (KeyValuePair<string, int> item in typeCounts)
            {
                ed.WriteMessage(item.Key + " : " + item.Value.ToString() + "\n");
            }

            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("샘플 최대 30개\n");

            int j;

            for (j = 0; j < samples.Count; j++)
            {
                ed.WriteMessage((j + 1).ToString() + ". " + samples[j] + "\n");
            }

            ed.WriteMessage("====================================\n");
        }

        private void CollectTextRowsFromEntity(
            Transaction tr,
            Entity entity,
            Matrix3d transform,
            List<OviaTextRow> rows,
            Point3d minPoint,
            Point3d maxPoint,
            int depth
        )
        {
            if (entity == null)
            {
                return;
            }

            if (depth > 8)
            {
                return;
            }

            if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DBText dbText = entity as DBText;

            if (dbText != null)
            {
                Point3d position = GetTextReferencePoint(entity, dbText.Position, transform);

                if (IsPointInsideWindow(position, minPoint, maxPoint))
                {
                    AddOviaTextRow(
                        rows,
                        entity,
                        "DBText",
                        dbText.TextString,
                        position,
                        dbText.Height,
                        dbText.Rotation
                    );
                }

                return;
            }

            MText mText = entity as MText;

            if (mText != null)
            {
                Point3d position = GetTextReferencePoint(entity, mText.Location, transform);

                if (IsPointInsideWindow(position, minPoint, maxPoint))
                {
                    AddOviaTextRow(
                        rows,
                        entity,
                        "MText",
                        mText.Contents,
                        position,
                        mText.TextHeight,
                        mText.Rotation
                    );
                }

                return;
            }

            AttributeReference attributeReference = entity as AttributeReference;

            if (attributeReference != null)
            {
                Point3d position = GetTextReferencePoint(entity, attributeReference.Position, transform);

                if (IsPointInsideWindow(position, minPoint, maxPoint))
                {
                    AddOviaTextRow(
                        rows,
                        entity,
                        "AttributeReference",
                        attributeReference.TextString,
                        position,
                        attributeReference.Height,
                        attributeReference.Rotation
                    );
                }

                return;
            }

            BlockReference blockReference = entity as BlockReference;

            if (blockReference != null)
            {
                foreach (ObjectId attributeId in blockReference.AttributeCollection)
                {
                    AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;

                    if (attribute == null)
                    {
                        continue;
                    }

                    Point3d attrPosition = attribute.Position;

                    if (depth > 0)
                    {
                        attrPosition = attrPosition.TransformBy(transform);
                    }

                    if (IsPointInsideWindow(attrPosition, minPoint, maxPoint))
                    {
                        AddOviaTextRow(
                            rows,
                            attribute,
                            "AttributeReference",
                            attribute.TextString,
                            attrPosition,
                            attribute.Height,
                            attribute.Rotation
                        );
                    }
                }

                /*
                 * 블록 내부 문자 추출 1차: BlockTableRecord 직접 순회
                 * 일반 블록은 이 방식으로 내부 DBText/MText를 읽을 수 있습니다.
                 */
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                if (blockRecord != null)
                {
                    Matrix3d nextTransform = transform * blockReference.BlockTransform;

                    foreach (ObjectId childId in blockRecord)
                    {
                        Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                        if (childEntity == null)
                        {
                            continue;
                        }

                        CollectTextRowsFromEntity(
                            tr,
                            childEntity,
                            nextTransform,
                            rows,
                            minPoint,
                            maxPoint,
                            depth + 1
                        );
                    }
                }

                /*
                 * 블록 내부 문자 추출 2차: Explode 보조 방식
                 * 일부 외부 CAD 저장 DWG, 동적 블록, 특수 블록은 직접 순회 좌표 변환이 맞지 않을 수 있습니다.
                 * 이때 Explode로 현재 화면 좌표 기준 복제 객체를 만든 뒤 다시 읽습니다.
                 */
                CollectTextRowsFromExplodedBlock(
                    blockReference,
                    rows,
                    minPoint,
                    maxPoint,
                    depth + 1
                );

                return;
            }
        }

        private void CollectTextRowsFromExplodedBlock(
            BlockReference blockReference,
            List<OviaTextRow> rows,
            Point3d minPoint,
            Point3d maxPoint,
            int depth
        )
        {
            if (blockReference == null)
            {
                return;
            }

            if (depth > 8)
            {
                return;
            }

            DBObjectCollection explodedObjects = new DBObjectCollection();

            try
            {
                blockReference.Explode(explodedObjects);
            }
            catch (System.Exception)
            {
                return;
            }

            foreach (DBObject dbObject in explodedObjects)
            {
                Entity explodedEntity = dbObject as Entity;

                if (explodedEntity == null)
                {
                    if (dbObject != null)
                    {
                        dbObject.Dispose();
                    }

                    continue;
                }

                try
                {
                    DBText dbText = explodedEntity as DBText;

                    if (dbText != null)
                    {
                        Point3d position = GetTextReferencePoint(explodedEntity, dbText.Position, Matrix3d.Identity);

                        if (IsPointInsideWindow(position, minPoint, maxPoint))
                        {
                            AddOviaTextRow(
                                rows,
                                explodedEntity,
                                "ExplodedDBText",
                                dbText.TextString,
                                position,
                                dbText.Height,
                                dbText.Rotation
                            );
                        }

                        continue;
                    }

                    MText mText = explodedEntity as MText;

                    if (mText != null)
                    {
                        Point3d position = GetTextReferencePoint(explodedEntity, mText.Location, Matrix3d.Identity);

                        if (IsPointInsideWindow(position, minPoint, maxPoint))
                        {
                            AddOviaTextRow(
                                rows,
                                explodedEntity,
                                "ExplodedMText",
                                mText.Contents,
                                position,
                                mText.TextHeight,
                                mText.Rotation
                            );
                        }

                        continue;
                    }

                    AttributeReference attributeReference = explodedEntity as AttributeReference;

                    if (attributeReference != null)
                    {
                        Point3d position = GetTextReferencePoint(explodedEntity, attributeReference.Position, Matrix3d.Identity);

                        if (IsPointInsideWindow(position, minPoint, maxPoint))
                        {
                            AddOviaTextRow(
                                rows,
                                explodedEntity,
                                "ExplodedAttributeReference",
                                attributeReference.TextString,
                                position,
                                attributeReference.Height,
                                attributeReference.Rotation
                            );
                        }

                        continue;
                    }

                    BlockReference nestedBlock = explodedEntity as BlockReference;

                    if (nestedBlock != null)
                    {
                        CollectTextRowsFromExplodedBlock(
                            nestedBlock,
                            rows,
                            minPoint,
                            maxPoint,
                            depth + 1
                        );

                        continue;
                    }
                }
                finally
                {
                    explodedEntity.Dispose();
                }
            }
        }

        /*
         * OVIA 2026-05-23 개선:
         * AutoCAD DBText는 가운데 정렬/맞춤 정렬 상태일 때 Position 값이
         * 사용자가 보는 글자의 중심이 아니라 기준점 또는 정렬점으로 잡힐 수 있습니다.
         *
         * 기존에는 Position/Location만 기준으로 셀을 찾았기 때문에,
         * 번호 컬럼처럼 폭이 좁고 가운데 정렬된 숫자가 선택박스 안에 보여도
         * 실제 기준점이 셀 밖으로 판정되어 번호가 누락되는 문제가 있었습니다.
         *
         * 이제는 가능한 경우 Entity.GeometricExtents의 중심점을 사용합니다.
         * 실패할 때만 기존 Position/Location 값을 사용합니다.
         */
        private Point3d GetTextReferencePoint(Entity entity, Point3d fallbackPoint, Matrix3d transform)
        {
            Point3d transformedFallback = fallbackPoint;

            try
            {
                transformedFallback = fallbackPoint.TransformBy(transform);
            }
            catch
            {
                transformedFallback = fallbackPoint;
            }

            if (entity == null)
            {
                return transformedFallback;
            }

            try
            {
                Extents3d extents = entity.GeometricExtents;

                Point3d minPoint = extents.MinPoint.TransformBy(transform);
                Point3d maxPoint = extents.MaxPoint.TransformBy(transform);

                double x = (minPoint.X + maxPoint.X) / 2.0;
                double y = (minPoint.Y + maxPoint.Y) / 2.0;
                double z = (minPoint.Z + maxPoint.Z) / 2.0;

                if (!Double.IsNaN(x) && !Double.IsNaN(y) && !Double.IsInfinity(x) && !Double.IsInfinity(y))
                {
                    return new Point3d(x, y, z);
                }
            }
            catch
            {
            }

            return transformedFallback;
        }

        private void AddOviaTextRow(
            List<OviaTextRow> rows,
            Entity entity,
            string objectType,
            string textValue,
            Point3d position,
            double height,
            double rotation
        )
        {
            OviaTextRow row = new OviaTextRow();

            row.SpaceName = GetSpaceName(entity);
            row.ObjectType = objectType;
            row.LayerName = SafeText(entity.Layer);
            row.TextValue = CleanText(textValue);
            row.X = position.X;
            row.Y = position.Y;
            row.Z = position.Z;
            row.Height = height;
            row.Rotation = rotation;
            try
            {
                row.Handle = entity.Handle.ToString();
            }
            catch
            {
                row.Handle = "";
            }

            if (row.TextValue != "")
            {
                rows.Add(row);
            }
        }

        private bool IsPointInsideWindow(Point3d point, Point3d minPoint, Point3d maxPoint)
        {
            double marginX = Math.Max((maxPoint.X - minPoint.X) * 0.02, 1.0);
            double marginY = Math.Max((maxPoint.Y - minPoint.Y) * 0.02, 1.0);

            if (point.X < minPoint.X - marginX)
            {
                return false;
            }

            if (point.X > maxPoint.X + marginX)
            {
                return false;
            }

            if (point.Y < minPoint.Y - marginY)
            {
                return false;
            }

            if (point.Y > maxPoint.Y + marginY)
            {
                return false;
            }

            return true;
        }

        private void RemoveDuplicateRows(List<OviaTextRow> rows)
        {
            Dictionary<string, bool> exists = new Dictionary<string, bool>();
            List<OviaTextRow> unique = new List<OviaTextRow>();

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                /*
                 * BlockReference 내부 직접 순회 + Explode 보조 추출을 같이 사용하면
                 * 같은 문자가 DBText / ExplodedDBText처럼 서로 다른 ObjectType으로 중복 수집될 수 있습니다.
                 * 그래서 중복 판단에서는 ObjectType을 제외하고, 위치와 텍스트 중심으로만 비교합니다.
                 */
                string key =
                    rows[i].LayerName + "|" +
                    rows[i].TextValue + "|" +
                    rows[i].X.ToString("0.##") + "|" +
                    rows[i].Y.ToString("0.##");

                if (exists.ContainsKey(key))
                {
                    continue;
                }

                exists[key] = true;
                unique.Add(rows[i]);
            }

            rows.Clear();

            for (i = 0; i < unique.Count; i++)
            {
                rows.Add(unique[i]);
            }
        }

        private string GetBlockName(Transaction tr, BlockReference blockReference)
        {
            if (blockReference == null)
            {
                return "";
            }

            try
            {
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;

                if (blockRecord != null)
                {
                    return blockRecord.Name;
                }
            }
            catch
            {
            }

            return "";
        }


        private void ScanBlockTableRecord(
            Transaction tr,
            BlockTable blockTable,
            string blockName,
            ref int dbTextCount,
            ref int mTextCount,
            ref int totalCount,
            List<string> samples
        )
        {
            if (!blockTable.Has(blockName))
            {
                return;
            }

            ObjectId blockId = blockTable[blockName];
            BlockTableRecord blockRecord = tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;

            if (blockRecord == null)
            {
                return;
            }

            foreach (ObjectId objectId in blockRecord)
            {
                Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;

                if (entity == null)
                {
                    continue;
                }

                DBText dbText = entity as DBText;

                if (dbText != null)
                {
                    dbTextCount++;
                    totalCount++;
                    AddSample(samples, dbText.TextString);
                    continue;
                }

                MText mText = entity as MText;

                if (mText != null)
                {
                    mTextCount++;
                    totalCount++;
                    AddSample(samples, mText.Contents);
                    continue;
                }
            }
        }

        private void CollectTextRows(
            Transaction tr,
            BlockTable blockTable,
            string blockName,
            string spaceName,
            List<OviaTextRow> rows
        )
        {
            if (!blockTable.Has(blockName))
            {
                return;
            }

            ObjectId blockId = blockTable[blockName];
            BlockTableRecord blockRecord = tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;

            if (blockRecord == null)
            {
                return;
            }

            foreach (ObjectId objectId in blockRecord)
            {
                Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;

                if (entity == null)
                {
                    continue;
                }

                DBText dbText = entity as DBText;

                if (dbText != null)
                {
                    OviaTextRow row = new OviaTextRow();

                    row.SpaceName = spaceName;
                    row.ObjectType = "DBText";
                    row.LayerName = SafeText(entity.Layer);
                    row.TextValue = CleanText(dbText.TextString);
                    row.X = dbText.Position.X;
                    row.Y = dbText.Position.Y;
                    row.Z = dbText.Position.Z;
                    row.Height = dbText.Height;
                    row.Rotation = dbText.Rotation;
                    try
                    {
                        row.Handle = entity.Handle.ToString();
                    }
                    catch
                    {
                        row.Handle = "";
                    }

                    if (row.TextValue != "")
                    {
                        rows.Add(row);
                    }

                    continue;
                }

                MText mText = entity as MText;

                if (mText != null)
                {
                    OviaTextRow row = new OviaTextRow();

                    row.SpaceName = spaceName;
                    row.ObjectType = "MText";
                    row.LayerName = SafeText(entity.Layer);
                    row.TextValue = CleanText(mText.Contents);
                    row.X = mText.Location.X;
                    row.Y = mText.Location.Y;
                    row.Z = mText.Location.Z;
                    row.Height = mText.TextHeight;
                    row.Rotation = mText.Rotation;
                    try
                    {
                        row.Handle = entity.Handle.ToString();
                    }
                    catch
                    {
                        row.Handle = "";
                    }

                    if (row.TextValue != "")
                    {
                        rows.Add(row);
                    }

                    continue;
                }
            }
        }

        private void SortRowsTopToBottomLeftToRight(List<OviaTextRow> rows)
        {
            rows.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                double yDiff = b.Y - a.Y;

                if (Math.Abs(yDiff) > 0.0001)
                {
                    return yDiff > 0 ? 1 : -1;
                }

                double xDiff = a.X - b.X;

                if (Math.Abs(xDiff) > 0.0001)
                {
                    return xDiff > 0 ? 1 : -1;
                }

                return string.Compare(a.TextValue, b.TextValue, StringComparison.Ordinal);
            });
        }

        private void ApplySimpleRowNumbers(List<OviaTextRow> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            int rowNo = 1;
            double baseY = rows[0].Y;
            double tolerance = GetAverageTextHeight(rows) * 0.7;

            if (tolerance <= 0)
            {
                tolerance = 1.0;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (Math.Abs(rows[i].Y - baseY) > tolerance)
                {
                    rowNo++;
                    baseY = rows[i].Y;
                }

                rows[i].RowNo = rowNo;
            }
        }

        private double GetAverageTextHeight(List<OviaTextRow> rows)
        {
            double total = 0;
            int count = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (rows[i].Height > 0)
                {
                    total += rows[i].Height;
                    count++;
                }
            }

            if (count == 0)
            {
                return 0;
            }

            return total / count;
        }

        private string GetSpaceName(Entity entity)
        {
            if (entity == null)
            {
                return "";
            }

            try
            {
                if (entity.OwnerId.IsNull)
                {
                    return "";
                }

                return entity.OwnerId.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void WritePreview(Editor ed, List<OviaTextRow> rows)
        {
            int max = rows.Count;

            if (max > 30)
            {
                max = 30;
            }

            ed.WriteMessage("------------------------------------\n");
            ed.WriteMessage("미리보기 최대 30개\n");

            int i;

            for (i = 0; i < max; i++)
            {
                ed.WriteMessage(
                    (i + 1).ToString() +
                    ". [R" + rows[i].RowNo.ToString() + "] " +
                    rows[i].TextValue +
                    "\n"
                );
            }
        }

        private string CreateCsvFilePath(Database db, string prefix)
        {
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string drawingName = "unsaved";

            if (db != null && db.Filename != null && db.Filename.Trim() != "")
            {
                drawingName = Path.GetFileNameWithoutExtension(db.Filename);
            }

            drawingName = MakeSafeFileName(drawingName);

            string fileName = prefix + "_" + drawingName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

            return Path.Combine(baseFolder, fileName);
        }

        private void WriteCsv(string filePath, List<OviaTextRow> rows)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("No,RowNo,Space,ObjectType,Layer,Text,X,Y,Z,Height,Rotation,Handle");

                int i;

                for (i = 0; i < rows.Count; i++)
                {
                    OviaTextRow row = rows[i];

                    writer.Write((i + 1).ToString());
                    writer.Write(",");
                    writer.Write(row.RowNo.ToString());
                    writer.Write(",");
                    writer.Write(Csv(row.SpaceName));
                    writer.Write(",");
                    writer.Write(Csv(row.ObjectType));
                    writer.Write(",");
                    writer.Write(Csv(row.LayerName));
                    writer.Write(",");
                    writer.Write(Csv(row.TextValue));
                    writer.Write(",");
                    writer.Write(row.X.ToString("0.########"));
                    writer.Write(",");
                    writer.Write(row.Y.ToString("0.########"));
                    writer.Write(",");
                    writer.Write(row.Z.ToString("0.########"));
                    writer.Write(",");
                    writer.Write(row.Height.ToString("0.########"));
                    writer.Write(",");
                    writer.Write(row.Rotation.ToString("0.########"));
                    writer.Write(",");
                    writer.Write(Csv(row.Handle));
                    writer.WriteLine();
                }
            }
        }

        private string Csv(string value)
        {
            if (value == null)
            {
                value = "";
            }

            value = value.Replace("\"", "\"\"");

            return "\"" + value + "\"";
        }

        private void AddSample(List<string> samples, string value)
        {
            if (samples.Count >= 20)
            {
                return;
            }

            value = CleanText(value);

            if (value == "")
            {
                return;
            }

            samples.Add(value);
        }

        private string CleanText(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Replace("\r", " ");
            value = value.Replace("\n", " ");
            value = value.Replace("\t", " ");
            value = value.Replace("\\P", " ");
            value = value.Replace("{", "");
            value = value.Replace("}", "");
            value = value.Trim();

            while (value.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                value = value.Replace("  ", " ");
            }

            if (value.Length > 500)
            {
                value = value.Substring(0, 500) + "...";
            }

            return value;
        }

        private string SafeText(string value)
        {
            if (value == null || value.Trim() == "")
            {
                return "(없음)";
            }

            return value;
        }

        private string MakeSafeFileName(string value)
        {
            if (value == null || value.Trim() == "")
            {
                return "drawing";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            int i;

            for (i = 0; i < invalidChars.Length; i++)
            {
                value = value.Replace(invalidChars[i], '_');
            }

            return value;
        }
    }

    public class HeaderYVote
    {
        public double Y = 0;
        public int Count = 0;
        public int Score = 0;
        public List<string> Keys = new List<string>();
    }

    public class OviaTextRow
    {
        public int RowNo = 0;
        public string SpaceName = "";
        public string ObjectType = "";
        public string LayerName = "";
        public string TextValue = "";
        public double X = 0;
        public double Y = 0;
        public double Z = 0;
        public double Height = 0;
        public double Rotation = 0;
        public string Handle = "";
    }

    public class OviaBarTableRow
    {
        public int No = 0;
        public string RowType = "";
        public int SourceRowNo = 0;
        public string BarNo = "";
        public string MarkNo = "";
        public string Part = "";
        public string Symbol = "";
        public string ShapeNo = "";
        public string ShapeText = "";
        public string ShapeRawText = "";
        public string ShapeDimensionText = "";
        public string Spec = "";
        public string Length = "";
        public string Qty = "";
        public string TotalLength = "";
        public string TotalWeight = "";
        public string Note = "";
        public string RawText = "";
        public string CadShapeJsonPath = "";
        public string CadShapeTextValues = "";
        public string ShapeSource = "";
        public string ShapeStatus = "";
        public double ShapeCellMinX = 0;
        public double ShapeCellMaxX = 0;
        public double ShapeCellMinY = 0;
        public double ShapeCellMaxY = 0;

        public bool HasShapeCellBounds()
        {
            return Math.Abs(ShapeCellMaxX - ShapeCellMinX) > 0.0001 && Math.Abs(ShapeCellMaxY - ShapeCellMinY) > 0.0001;
        }
    }

    public class OviaCadShapeElement
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

    public class OviaHeaderColumn
    {
        public string StandardKey = "";
        public string OriginalTitle = "";
        public double X = 0;
        public double LeftX = 0;
        public double RightX = 0;
        public int SourceColumnIndex = -1;
    }

    public class OviaGridAxisSegment
    {
        public double Coordinate = 0;
        public double Start = 0;
        public double End = 0;
    }

    public class OviaGridLineSegment
    {
        public double X1 = 0;
        public double Y1 = 0;
        public double X2 = 0;
        public double Y2 = 0;
        public string SourceType = "";
    }

    public class OviaHeaderMap
    {
        public int HeaderRowIndex = -1;
        public List<OviaHeaderColumn> Columns = new List<OviaHeaderColumn>();
        public double MinX = 0;
        public double MaxX = 0;
    }
}
