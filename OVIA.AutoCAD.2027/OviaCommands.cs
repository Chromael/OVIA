using System;
using System.Collections.Generic;
using System.IO;
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

            PromptPointOptions firstPointOptions = new PromptPointOptions(
                "\nOVIA 선택박스 시작점: 표 전체 가로폭의 왼쪽, 원하는 행 구간의 위쪽 바깥을 클릭하세요: "
            );

            PromptPointResult firstPointResult = ed.GetPoint(firstPointOptions);

            if (firstPointResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nOVIA: 선택박스 생성이 취소되었습니다.\n");
                return;
            }

            PromptCornerOptions secondPointOptions = new PromptCornerOptions(
                "\nOVIA 선택박스 끝점: 표 전체 가로폭의 오른쪽, 원하는 행 구간의 아래쪽 바깥을 클릭하세요: ",
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
            ed.WriteMessage("라인 스냅 : " + (isSnapped ? "가까운 테이블 라인에 자동 보정됨" : "스냅 가능한 테이블 라인 없음, 클릭 좌표 사용") + "\n");
            ed.WriteMessage("편집 방식 : 잠금 없음, 필요 시 OVIA 전용 조정 명령으로 직사각형 유지\n");
            ed.WriteMessage("상단 조정 : OVIABOXTOP\n");
            ed.WriteMessage("하단 조정 : OVIABOXBOTTOM\n");
            ed.WriteMessage("좌측 조정 : OVIABOXLEFT\n");
            ed.WriteMessage("우측 조정 : OVIABOXRIGHT\n");
            ed.WriteMessage("이동      : OVIABOXMOVE\n");
            ed.WriteMessage("추출      : OVIABOXCSV\n");
            ed.WriteMessage("====================================\n");
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

            double padding = Math.Max(longSide * 0.15, 50.0);
            double lineTolerance = Math.Max(longSide * 0.002, 1.0);
            double snapDistance = Math.Max(longSide * 0.04, 10.0);

            Point3d searchPoint1 = new Point3d(minX - padding, minY - padding, 0);
            Point3d searchPoint2 = new Point3d(maxX + padding, maxY + padding, 0);

            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(searchPoint1, searchPoint2);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return false;
            }

            List<double> verticalXs = new List<double>();
            List<double> horizontalYs = new List<double>();

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

                    CollectAxisLinesFromEntity(
                        tr,
                        entity,
                        Matrix3d.Identity,
                        verticalXs,
                        horizontalYs,
                        lineTolerance,
                        0
                    );
                }

                tr.Commit();
            }

            if (verticalXs.Count == 0 && horizontalYs.Count == 0)
            {
                return false;
            }

            double x1 = SnapCoordinate(rawPoint1.X, verticalXs, snapDistance);
            double y1 = SnapCoordinate(rawPoint1.Y, horizontalYs, snapDistance);
            double x2 = SnapCoordinate(rawPoint2.X, verticalXs, snapDistance);
            double y2 = SnapCoordinate(rawPoint2.Y, horizontalYs, snapDistance);

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

            List<OviaBarTableRow> tableRows = BuildOviaBarTableRows(rows);

            if (tableRows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: 집계표로 변환할 데이터 행을 찾지 못했습니다.\n");
                ed.WriteMessage("선택박스가 표의 가로 전체 폭과 원하는 세로 행 구간을 포함하는지 확인해주세요.\n");
                return;
            }

            string filePath = CreateCsvFilePath(db, "OVIA_BoxTable");

            try
            {
                WriteBarTableCsv(filePath, tableRows);

                ed.WriteMessage("\n");
                ed.WriteMessage("====================================\n");
                ed.WriteMessage("OVIA 철근 집계표 구조화 완료\n");
                ed.WriteMessage("------------------------------------\n");
                ed.WriteMessage("원본 문자 개수 : " + rows.Count.ToString() + "\n");
                ed.WriteMessage("변환 행 개수   : " + tableRows.Count.ToString() + "\n");
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
            columns.Add(CreateHeaderColumn("TOTAL_WEIGHT", "총중량(TON)", 6));
            columns.Add(CreateHeaderColumn("NOTE", "비고", 7));

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

            if (standardKey == "SHAPE_NO")
            {
                return "형번";
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
                return "총중량";
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

            if (value.IndexOf("번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("부호", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                return row.MarkNo != "" ? row.MarkNo : row.BarNo;
            }

            if (key == "SHAPE_NO")
            {
                return row.ShapeNo;
            }

            if (key == "SHAPE")
            {
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

        private void WriteBarTableCsv(string filePath, List<OviaBarTableRow> rows)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                List<OviaHeaderColumn> columns = lastDetectedHeaderColumns;

                if (columns == null || columns.Count == 0)
                {
                    columns = CreateFallbackHeaderColumns();
                }

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
                    ", 총중량=" + row.TotalWeight +
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

            BlockTableRecord blockRecord = tr.GetObject(blockTable[blockName], OpenMode.ForWrite) as BlockTableRecord;

            if (blockRecord == null)
            {
                return deletedCount;
            }

            foreach (ObjectId objectId in blockRecord)
            {
                Entity entity = tr.GetObject(objectId, OpenMode.ForWrite, false) as Entity;

                if (entity == null)
                {
                    continue;
                }

                if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
                {
                    entity.Erase();
                    deletedCount++;
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
                Point3d position = dbText.Position.TransformBy(transform);

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
                Point3d position = mText.Location.TransformBy(transform);

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
                Point3d position = attributeReference.Position.TransformBy(transform);

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
                        Point3d position = dbText.Position;

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
                        Point3d position = mText.Location;

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
                        Point3d position = attributeReference.Position;

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
    }

    public class OviaHeaderColumn
    {
        public string StandardKey = "";
        public string OriginalTitle = "";
        public double X = 0;
        public double LeftX = 0;
        public double RightX = 0;
    }

    public class OviaHeaderMap
    {
        public int HeaderRowIndex = -1;
        public List<OviaHeaderColumn> Columns = new List<OviaHeaderColumn>();
        public double MinX = 0;
        public double MaxX = 0;
    }
}
