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
        private const double OviaBoxOverlapTolerance = 0.0001;

        private List<OviaHeaderColumn> lastDetectedHeaderColumns = new List<OviaHeaderColumn>();

        /*
         * 같은 CAD 표를 2차, 3차, N차로 나누어 추출할 때 헤더가 현재 선택 구간에 없더라도
         * 첫 번째 정상 분석에서 확정한 물리 컬럼 경계를 재사용합니다.
         * 형상 셀 내부 수직선이 표 세로선으로 오인되거나, 짧은 선택 구간에서 fallback 컬럼이
         * 달라져 길이/수량/중량이 밀리는 문제를 방지합니다.
         */
        private static readonly object GridSchemaCacheSync = new object();
        private static string cachedGridSchemaDrawing = "";
        private static double cachedGridSchemaMinX = 0;
        private static double cachedGridSchemaMaxX = 0;
        private static List<double> cachedGridSchemaVerticalXs = new List<double>();
        private static List<OviaHeaderColumn> cachedGridSchemaColumns = new List<OviaHeaderColumn>();

        private sealed class OviaEnterPromptDisplayState
        {
            public object DynamicMode;
            public object DynamicPrompt;
            public object CursorType;
            public bool HasDynamicMode;
            public bool HasDynamicPrompt;
            public bool HasCursorType;
        }

        private sealed class OviaSelectionRectangle
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public OviaSelectionRectangle(double minX, double maxX, double minY, double maxY)
            {
                MinX = Math.Min(minX, maxX);
                MaxX = Math.Max(minX, maxX);
                MinY = Math.Min(minY, maxY);
                MaxY = Math.Max(minY, maxY);
            }

            public double Width
            {
                get { return MaxX - MinX; }
            }

            public double Height
            {
                get { return MaxY - MinY; }
            }
        }

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
                int createdCount = 0;

                while (true)
                {
                    PromptPointOptions firstPointOptions = new PromptPointOptions(
                        "\nOVIA 선택박스 시작점: 표 왼쪽 경계선과 시작 행의 위쪽 가로선 교차점을 클릭하세요. 완료는 Enter 또는 OVIA의 'CAD 선택모드 해제' 버튼을 사용하세요: "
                    );

                    firstPointOptions.AllowNone = true;

                    PromptPointResult firstPointResult = ed.GetPoint(firstPointOptions);

                    if (firstPointResult.Status == PromptStatus.None)
                    {
                        ed.WriteMessage("\nOVIA: 연속 영역 선택을 완료했습니다. 총 " + createdCount.ToString() + "개 영역을 처리했습니다.\n");
                        return;
                    }

                    if (firstPointResult.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nOVIA: CAD 영역 선택모드를 해제했습니다. 총 " + createdCount.ToString() + "개 영역을 처리했습니다.\n");
                        return;
                    }

                    PromptCornerOptions secondPointOptions = new PromptCornerOptions(
                        "\nOVIA 선택박스 끝점: 표 오른쪽 경계선과 끝 행의 아래쪽 가로선 교차점을 클릭하세요: ",
                        firstPointResult.Value
                    );

                    PromptPointResult secondPointResult = ed.GetCorner(secondPointOptions);

                    if (secondPointResult.Status == PromptStatus.None)
                    {
                        ed.WriteMessage("\nOVIA: 현재 영역 선택을 취소하고 연속 선택모드를 종료했습니다. 총 " + createdCount.ToString() + "개 영역을 처리했습니다.\n");
                        return;
                    }

                    if (secondPointResult.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nOVIA: CAD 영역 선택모드를 해제했습니다. 총 " + createdCount.ToString() + "개 영역을 처리했습니다.\n");
                        return;
                    }

                    /*
                     * OVIA 2026-06-25 보정:
                     * OVIABOX를 1회만 선택하고 끝내는 방식이 아니라,
                     * 한 영역의 노란 선택박스를 먼저 생성하고 Enter로 확정하면 OVIABOXTABLE 추출을 자동 수행한 뒤 다시 다음 영역을 선택할 수 있게 합니다.
                     * 사용자는 필요한 영역마다 시작점/끝점 선택 후 Enter로 확정하고,
                     * 전체 작업 종료는 다음 시작점 대기 상태에서 Enter 또는 OVIA의 CAD 선택모드 해제 버튼을 사용합니다.
                     *
                     * OVIA 2026-06-25 추가 보정:
                     * BarList에 불러온 CAD 영역은 도면 내 작업 이력으로 남아야 하므로,
                     * 새 영역을 선택할 때 기존 OVIA_SELECT_BOX를 삭제하지 않습니다.
                     * 단, 추출 대상은 방금 생성 후 Enter로 확정한 박스 1개 영역으로 한정하여
                     * 이전 박스와 새 박스가 합쳐진 큰 영역으로 추출되지 않게 합니다.
                     */
                    Point3d boxPoint1 = firstPointResult.Value;
                    Point3d boxPoint2 = secondPointResult.Value;
                    int overlappedBoxCount;
                    List<OviaSelectionRectangle> availableRectangles = BuildNonOverlappingOviaSelectionRectangles(
                        db,
                        boxPoint1,
                        boxPoint2,
                        out overlappedBoxCount
                    );

                    if (availableRectangles.Count == 0)
                    {
                        ed.WriteMessage("\nOVIA: 선택한 범위는 기존 노란 선택영역에 모두 포함되어 있어 중복 추출하지 않았습니다.\n");
                        continue;
                    }

                    List<ObjectId> pendingBoxIds = new List<ObjectId>();

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId dashedLineTypeId = EnsureDashedLineType(db, tr);
                        EnsureOviaBoxLayer(db, tr, dashedLineTypeId, false);

                        int rectangleIndex;

                        for (rectangleIndex = 0; rectangleIndex < availableRectangles.Count; rectangleIndex++)
                        {
                            OviaSelectionRectangle rectangle = availableRectangles[rectangleIndex];
                            ObjectId pendingBoxId = CreateOviaBoxEntity(
                                db,
                                tr,
                                new Point3d(rectangle.MinX, rectangle.MinY, 0),
                                new Point3d(rectangle.MaxX, rectangle.MaxY, 0),
                                dashedLineTypeId
                            );

                            if (!pendingBoxId.IsNull)
                            {
                                pendingBoxIds.Add(pendingBoxId);
                            }
                        }

                        EnsureOviaBoxLayer(db, tr, dashedLineTypeId, true);
                        tr.Commit();
                    }

                    /*
                     * OVIA 2026-07-14 Enter 확정 재보정:
                     * 기존 정상 동작처럼 시작점과 끝점 선택 직후 노란 선택박스를 먼저 생성합니다.
                     * 사용자가 도면에서 선택 범위를 눈으로 확인한 뒤 Enter를 눌렀을 때만
                     * 형상 JSON과 CSV를 생성하여 OVIA BarList로 전달합니다.
                     * AutoCAD의 GetKeywords에서 빈 Enter는 PromptStatus.None으로 반환될 수 있으므로
                     * PromptStatus.None과 PromptStatus.OK를 모두 정상 확정으로 처리합니다.
                     *
                     * OVIA 2026-07-14 커서 깜빡임 보정:
                     * Enter만 기다리는 구간에는 좌표 선택용 십자가 커서와 동적 입력창이 필요하지 않습니다.
                     * 확인 입력 직전에 DYNMODE/DYNPROMPT를 끄고 CURSORTYPE을 Windows 포인터로 임시 변경하여
                     * 선택 완료 후 십자가 커서가 계속 깜빡이는 현상을 차단합니다.
                     * Enter 또는 취소 직후에는 사용자의 기존 설정을 반드시 원래 값으로 복원합니다.
                     */
                    ed.Regen();

                    OviaEnterPromptDisplayState enterPromptDisplayState = SuppressOviaEnterPromptDisplay();
                    PromptResult confirmResult;

                    try
                    {
                        PromptKeywordOptions confirmOptions = new PromptKeywordOptions(
                            "\n노란 선택박스의 영역을 OVIA로 전송하려면 Enter를 누르세요. 취소는 Esc: "
                        );

                        confirmOptions.AllowNone = true;
                        confirmOptions.AppendKeywordsToMessage = false;
                        confirmOptions.Keywords.Add("Send");
                        confirmOptions.Keywords.Default = "Send";

                        confirmResult = ed.GetKeywords(confirmOptions);
                    }
                    finally
                    {
                        RestoreOviaEnterPromptDisplay(enterPromptDisplayState);
                    }

                    bool isConfirmed = confirmResult.Status == PromptStatus.None || confirmResult.Status == PromptStatus.OK;

                    if (!isConfirmed)
                    {
                        DeleteOviaBoxEntitiesById(db, pendingBoxIds);
                        ed.Regen();
                        ed.WriteMessage("\nOVIA: 현재 노란 선택박스는 취소되어 삭제했으며 데이터는 전송하지 않았습니다. 총 " + createdCount.ToString() + "개 영역을 처리했습니다.\n");
                        return;
                    }

                    createdCount += availableRectangles.Count;

                    ed.WriteMessage("\n");
                    ed.WriteMessage("====================================\n");
                    ed.WriteMessage("OVIA 노란 선택박스 Enter 확정 완료 및 자동 추출 시작\n");
                    ed.WriteMessage("------------------------------------\n");
                    ed.WriteMessage("신규 처리 영역 : " + availableRectangles.Count.ToString() + "개\n");
                    ed.WriteMessage("누적 처리 영역 : " + createdCount.ToString() + "개\n");
                    ed.WriteMessage("중복 검사 박스 : " + overlappedBoxCount.ToString() + "개\n");
                    ed.WriteMessage("표시 형태 : 밝은 노란색 / 매우 두꺼운 실선\n");
                    ed.WriteMessage("박스 표시 : 선택한 모든 영역의 노란 박스를 도면에 유지\n");
                    ed.WriteMessage("중복 처리 : 기존 선택영역과 겹치는 구간은 자동 제외\n");
                    ed.WriteMessage("추출 기준 : 중복을 제외한 신규 영역만 자동 추출\n");
                    ed.WriteMessage("연속 작업 : 다음 영역도 시작점/끝점 선택 후 Enter로 확정하세요. 전체 종료는 다음 시작점 대기에서 Enter 또는 OVIA의 CAD 선택모드 해제 버튼을 사용합니다.\n");
                    ed.WriteMessage("====================================\n");

                    int extractionIndex;

                    for (extractionIndex = 0; extractionIndex < availableRectangles.Count; extractionIndex++)
                    {
                        OviaSelectionRectangle rectangle = availableRectangles[extractionIndex];

                        RunSmartBoxTableExtraction(
                            "OVIABOX",
                            new Point3d(rectangle.MinX, rectangle.MinY, 0),
                            new Point3d(rectangle.MaxX, rectangle.MaxY, 0),
                            boxPoint1,
                            boxPoint2
                        );
                    }
                }
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
                 * OVIA 2026-05-27 보정:
                 * 대표님 요청으로 OVIABOX 시작점/끝점 선택 시 AutoCAD의 네모형 끝점 스냅과
                 * 교차점 스냅이 다시 동작하도록 복구합니다.
                 *
                 * 1   = Endpoint
                 * 32  = Intersection
                 * 33  = Endpoint + Intersection
                 *
                 * 주의:
                 * 이전에 문제가 되었던 것은 AutoCAD 스냅 자체가 아니라,
                 * OVIA 내부의 추가 라인 스냅/좌측 자동 확장 보정이 사용자가 선택한 범위를
                 * 임의로 넓힌 것이었습니다.
                 * 따라서 객체스냅은 켜되, 노란 선택박스는 반환된 스냅 좌표 그대로 생성하고
                 * OVIA가 별도로 좌/우/상/하 확장하지 않습니다.
                 */
                Application.SetSystemVariable("OSMODE", 33);

                if (ed != null)
                {
                    ed.WriteMessage("\nOVIA 테이블 선택 모드: 끝점/교차점 객체스냅을 임시 적용했습니다.\n");
                    ed.WriteMessage("표 외곽선과 행 경계선이 만나는 교차점에서 시작하고, 끝 행의 반대쪽 교차점에서 마무리하세요.\n");
                    ed.WriteMessage("노란 선택박스는 스냅된 좌표 그대로 생성하며, OVIA가 임의로 범위를 확장하지 않습니다.\n");
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

        private OviaEnterPromptDisplayState SuppressOviaEnterPromptDisplay()
        {
            OviaEnterPromptDisplayState state = new OviaEnterPromptDisplayState();

            try
            {
                state.DynamicMode = Application.GetSystemVariable("DYNMODE");
                state.HasDynamicMode = true;
                Application.SetSystemVariable("DYNMODE", 0);
            }
            catch
            {
            }

            try
            {
                state.DynamicPrompt = Application.GetSystemVariable("DYNPROMPT");
                state.HasDynamicPrompt = true;
                Application.SetSystemVariable("DYNPROMPT", 0);
            }
            catch
            {
            }

            try
            {
                state.CursorType = Application.GetSystemVariable("CURSORTYPE");
                state.HasCursorType = true;
                Application.SetSystemVariable("CURSORTYPE", 1);
            }
            catch
            {
            }

            return state;
        }

        private void RestoreOviaEnterPromptDisplay(OviaEnterPromptDisplayState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.HasCursorType)
            {
                try
                {
                    Application.SetSystemVariable("CURSORTYPE", state.CursorType);
                }
                catch
                {
                }
            }

            if (state.HasDynamicPrompt)
            {
                try
                {
                    Application.SetSystemVariable("DYNPROMPT", state.DynamicPrompt);
                }
                catch
                {
                }
            }

            if (state.HasDynamicMode)
            {
                try
                {
                    Application.SetSystemVariable("DYNMODE", state.DynamicMode);
                }
                catch
                {
                }
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

            bool hasBox = GetLatestOviaBoxExtents(db, out minPoint, out maxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 도면에서 OVIA 선택박스를 찾지 못했습니다.\n");
                ed.WriteMessage("먼저 OVIABOX 명령어로 선택박스를 생성해주세요.\n");
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

            bool hasBox = GetLatestOviaBoxExtents(db, out selectedMinPoint, out selectedMaxPoint, out boxCount);

            if (!hasBox)
            {
                ed.WriteMessage("\nOVIA: 도면에서 OVIA 선택박스를 찾지 못했습니다.\n");
                ed.WriteMessage("먼저 OVIABOX 명령어로 집계표의 가로 전체 폭과 원하는 세로 행 구간을 선택해주세요.\n");
                return;
            }

            RunSmartBoxTableExtractionFromWindow(commandName, selectedMinPoint, selectedMaxPoint, boxCount);
        }

        private void RunSmartBoxTableExtraction(string commandName, Point3d point1, Point3d point2)
        {
            RunSmartBoxTableExtraction(commandName, point1, point2, point1, point2);
        }

        private void RunSmartBoxTableExtraction(
            string commandName,
            Point3d point1,
            Point3d point2,
            Point3d analysisContextPoint1,
            Point3d analysisContextPoint2)
        {
            Point3d selectedMinPoint = new Point3d(
                Math.Min(point1.X, point2.X),
                Math.Min(point1.Y, point2.Y),
                Math.Min(point1.Z, point2.Z)
            );

            Point3d selectedMaxPoint = new Point3d(
                Math.Max(point1.X, point2.X),
                Math.Max(point1.Y, point2.Y),
                Math.Max(point1.Z, point2.Z)
            );

            Point3d analysisContextMinPoint = new Point3d(
                Math.Min(analysisContextPoint1.X, analysisContextPoint2.X),
                Math.Min(analysisContextPoint1.Y, analysisContextPoint2.Y),
                Math.Min(analysisContextPoint1.Z, analysisContextPoint2.Z)
            );

            Point3d analysisContextMaxPoint = new Point3d(
                Math.Max(analysisContextPoint1.X, analysisContextPoint2.X),
                Math.Max(analysisContextPoint1.Y, analysisContextPoint2.Y),
                Math.Max(analysisContextPoint1.Z, analysisContextPoint2.Z)
            );

            RunSmartBoxTableExtractionFromWindow(
                commandName,
                selectedMinPoint,
                selectedMaxPoint,
                1,
                analysisContextMinPoint,
                analysisContextMaxPoint
            );
        }

        private void RunSmartBoxTableExtractionFromWindow(string commandName, Point3d selectedMinPoint, Point3d selectedMaxPoint, int boxCount)
        {
            RunSmartBoxTableExtractionFromWindow(
                commandName,
                selectedMinPoint,
                selectedMaxPoint,
                boxCount,
                selectedMinPoint,
                selectedMaxPoint
            );
        }

        private void RunSmartBoxTableExtractionFromWindow(
            string commandName,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            int boxCount,
            Point3d analysisContextMinPoint,
            Point3d analysisContextMaxPoint)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Point3d analysisMinPoint;
            Point3d analysisMaxPoint;

            /*
             * OVIA 2026-07-15 중복 선택 재보정:
             * 기존 노란 박스와 겹치는 부분을 잘라낸 작은 신규 구간만 분석 창의 기준으로 사용하면,
             * 헤더/표 컬럼 문맥이 부족해 형상 치수값이 길이·수량·중량 칸으로 밀릴 수 있습니다.
             * 따라서 실제 출력 행 필터는 중복 제외된 selectedMin/Max를 유지하되,
             * 표 구조 분석은 사용자가 방금 지정한 원래 전체 선택 영역을 기준으로 수행합니다.
             */
            CreateSmartTableAnalysisWindow(analysisContextMinPoint, analysisContextMaxPoint, out analysisMinPoint, out analysisMaxPoint);

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

            string validationMessage;

            if (!ValidateExtractedBarTableRows(tableRows, out validationMessage))
            {
                ed.WriteMessage("\nOVIA: 현재 선택 영역의 표 컬럼 분석 결과가 안전하지 않아 CSV 전송을 중단했습니다.\n");
                ed.WriteMessage("기존 BarList 데이터는 변경하지 않습니다.\n");

                if (validationMessage != "")
                {
                    ed.WriteMessage("검증 정보: " + validationMessage + "\n");
                }

                return;
            }

            string filePath = CreateCsvFilePath(db, "OVIA_BoxTable");

            try
            {
                CaptureCadShapeFilesForRows(ed, db, filePath, tableRows);
                WriteBarTableCsv(filePath, tableRows, doc);

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

                string sourceDrawingName;
                string sourceDrawingPath;
                ResolveSourceDrawingInfo(doc, out sourceDrawingName, out sourceDrawingPath);

                ed.WriteMessage("원본 도면     : " + sourceDrawingName + "\n");

                if (sourceDrawingPath != "")
                {
                    ed.WriteMessage("원본 경로     : " + sourceDrawingPath + "\n");
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

                if (row.RowType == "DATA" && headerMap.HeaderRowIndex < 0)
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

            /*
             * OVIA BarList 사용자 화면/CSV 고정 헤더 순서입니다.
             * CAD 도면의 실제 헤더명은 매핑 사전으로 치환하되, 출력은 이 순서를 유지합니다.
             */
            columns.Add(CreateHeaderColumn("MARK_NO", "번호", 0));
            columns.Add(CreateHeaderColumn("PART", "부위", 1));
            columns.Add(CreateHeaderColumn("SPEC", "철근규격", 2));
            columns.Add(CreateHeaderColumn("SHAPE", "철근형상", 3));
            columns.Add(CreateHeaderColumn("LENGTH_MM", "길이(mm)", 4));
            columns.Add(CreateHeaderColumn("QUANTITY_EA", "수량(EA)", 5));
            columns.Add(CreateHeaderColumn("TOTAL_LENGTH_M", "총길이(M)", 6));
            columns.Add(CreateHeaderColumn("TOTAL_WEIGHT", "중량(Ton)", 7));
            columns.Add(CreateHeaderColumn("NOTE", "비고", 8));

            return columns;
        }

        private bool ValidateExtractedBarTableRows(List<OviaBarTableRow> rows, out string message)
        {
            message = "";

            if (rows == null || rows.Count == 0)
            {
                message = "데이터 행이 없습니다.";
                return false;
            }

            int dataCount = 0;
            int validCount = 0;
            List<string> invalidSamples = new List<string>();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || row.RowType == "TOTAL" || row.RowType == "SUBTOTAL")
                {
                    continue;
                }

                dataCount++;
                string mark = row.MarkNo == null || row.MarkNo.Trim() == "" ? row.BarNo : row.MarkNo;
                bool markOk = mark != null && Regex.IsMatch(mark.Trim(), @"^[0-9]{1,6}[A-Za-z]?$", RegexOptions.IgnoreCase);
                bool specOk = DetectSpec(row.Spec) != "" || Regex.IsMatch(row.Spec == null ? "" : row.Spec.Trim(), @"^(?:UHD|SHD|HD|SD|D)[0-9]{1,3}[A-Z]{0,4}$", RegexOptions.IgnoreCase);
                bool lengthOk = IsPositiveCadTableNumber(row.Length);
                bool qtyOk = IsPositiveCadTableNumber(row.Qty);

                if (markOk && specOk && lengthOk && qtyOk)
                {
                    validCount++;
                    continue;
                }

                if (invalidSamples.Count < 3)
                {
                    invalidSamples.Add(
                        "번호=" + SafeText(mark)
                        + ", 규격=" + SafeText(row.Spec)
                        + ", 길이=" + SafeText(row.Length)
                        + ", 수량=" + SafeText(row.Qty)
                    );
                }
            }

            if (dataCount <= 0)
            {
                message = "실제 데이터 행이 없습니다.";
                return false;
            }

            if (validCount == dataCount)
            {
                return true;
            }

            double validRatio = (double)validCount / dataCount;

            if (validRatio >= 0.92 && dataCount >= 12)
            {
                return true;
            }

            message = "정상 행 " + validCount.ToString() + "/" + dataCount.ToString();

            if (invalidSamples.Count > 0)
            {
                message += " / 비정상 예: " + String.Join(" | ", invalidSamples.ToArray());
            }

            return false;
        }

        private bool IsPositiveCadTableNumber(string value)
        {
            if (value == null)
            {
                return false;
            }

            string normalized = value.Trim().Replace(",", "");
            Match match = Regex.Match(normalized, @"-?\d+(?:\.\d+)?");

            if (!match.Success)
            {
                return false;
            }

            decimal number;
            return Decimal.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out number) && number > 0;
        }

        private string AppendDiagnostic(string current, string message)
        {
            if (message == null || message.Trim() == "")
            {
                return current == null ? "" : current;
            }

            if (current == null || current.Trim() == "")
            {
                return message.Trim();
            }

            return current.Trim() + " / " + message.Trim();
        }

        private string GetCurrentGridSchemaDrawingIdentity()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null || doc.Database == null)
                {
                    return "";
                }

                string filename = doc.Database.Filename;

                if (filename == null || filename.Trim() == "")
                {
                    filename = doc.Name;
                }

                return (filename == null ? "" : filename.Trim()).ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        private bool TryApplyCachedGridSchema(Point3d minPoint, Point3d maxPoint, ref List<double> verticalXs)
        {
            string drawingIdentity = GetCurrentGridSchemaDrawingIdentity();
            double selectionMinX = Math.Min(minPoint.X, maxPoint.X);
            double selectionMaxX = Math.Max(minPoint.X, maxPoint.X);
            double selectionWidth = Math.Max(selectionMaxX - selectionMinX, 0.0001);

            lock (GridSchemaCacheSync)
            {
                if (cachedGridSchemaVerticalXs == null || cachedGridSchemaVerticalXs.Count < 3
                    || cachedGridSchemaColumns == null || cachedGridSchemaColumns.Count < 3)
                {
                    return false;
                }

                if (!String.Equals(cachedGridSchemaDrawing, drawingIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                double cacheWidth = Math.Max(cachedGridSchemaMaxX - cachedGridSchemaMinX, 0.0001);
                double overlap = Math.Min(selectionMaxX, cachedGridSchemaMaxX) - Math.Max(selectionMinX, cachedGridSchemaMinX);
                double requiredOverlap = Math.Min(selectionWidth, cacheWidth) * 0.72;

                if (overlap < requiredOverlap)
                {
                    return false;
                }

                verticalXs = new List<double>(cachedGridSchemaVerticalXs);
                return true;
            }
        }

        private List<OviaHeaderColumn> GetCachedGridSchemaColumns()
        {
            lock (GridSchemaCacheSync)
            {
                return CloneHeaderColumns(cachedGridSchemaColumns);
            }
        }

        private void CacheGridSchemaIfUsable(Point3d minPoint, Point3d maxPoint, List<double> verticalXs, List<OviaHeaderColumn> columns)
        {
            if (verticalXs == null || verticalXs.Count < 3 || columns == null || columns.Count < 3)
            {
                return;
            }

            if (!HasRequiredGridSchemaColumns(columns))
            {
                return;
            }

            List<double> orderedXs = new List<double>(verticalXs);
            orderedXs.Sort();
            double schemaMinX = orderedXs[0];
            double schemaMaxX = orderedXs[orderedXs.Count - 1];

            if (schemaMaxX - schemaMinX <= 0.0001)
            {
                return;
            }

            lock (GridSchemaCacheSync)
            {
                cachedGridSchemaDrawing = GetCurrentGridSchemaDrawingIdentity();
                cachedGridSchemaMinX = schemaMinX;
                cachedGridSchemaMaxX = schemaMaxX;
                cachedGridSchemaVerticalXs = orderedXs;
                cachedGridSchemaColumns = CloneHeaderColumns(columns);
            }
        }

        private bool HasRequiredGridSchemaColumns(List<OviaHeaderColumn> columns)
        {
            return FindHeaderColumnByKey(columns, "MARK_NO") != null
                && FindHeaderColumnByKey(columns, "SPEC") != null
                && FindHeaderColumnByKey(columns, "SHAPE") != null
                && FindHeaderColumnByKey(columns, "LENGTH_MM") != null
                && FindHeaderColumnByKey(columns, "QUANTITY_EA") != null
                && FindHeaderColumnByKey(columns, "TOTAL_WEIGHT") != null;
        }

        private List<OviaHeaderColumn> CloneHeaderColumns(List<OviaHeaderColumn> source)
        {
            List<OviaHeaderColumn> result = new List<OviaHeaderColumn>();

            if (source == null)
            {
                return result;
            }

            int i;

            for (i = 0; i < source.Count; i++)
            {
                OviaHeaderColumn item = source[i];

                if (item == null)
                {
                    continue;
                }

                OviaHeaderColumn clone = new OviaHeaderColumn();
                clone.StandardKey = item.StandardKey;
                clone.OriginalTitle = item.OriginalTitle;
                clone.X = item.X;
                clone.LeftX = item.LeftX;
                clone.RightX = item.RightX;
                clone.SourceColumnIndex = item.SourceColumnIndex;
                result.Add(clone);
            }

            return result;
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

        private List<OviaHeaderColumn> CreateGridFallbackHeaderColumnsFromData(string[,] cellTexts, int columnCount)
        {
            /*
             * 헤더를 찾지 못한 상태에서 컬럼 개수만으로 PART/SHAPE를 임의 배치하면
             * 철근형상 내부 치수값이 부위에 들어가고, 실제 형상 셀 대신 길이 또는 형번 셀이
             * 선택되는 심각한 오매핑이 발생합니다.
             *
             * 안전 fallback 원칙:
             *  1) 규격(SHD10, HD16 등)을 데이터 패턴으로 먼저 확정
             *  2) 번호는 순차 정수 컬럼으로 확정
             *  3) 실제 철근형상은 규격 바로 왼쪽의 물리 셀을 우선 사용
             *     (형번/형상코드가 끼어 있으면 규격에 가장 가까운 셀이 실제 도형인 표가 일반적)
             *  4) 헤더가 확인되지 않은 PART는 절대 추정하지 않음
             *  5) 규격 오른쪽 산정값은 길이→수량→총길이→중량 순으로만 배치
             *
             * 특정 도면/형번/행 번호에 의존하지 않는 범용 규칙입니다.
             */
            List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();

            if (cellTexts == null || columnCount <= 0)
            {
                return columns;
            }

            int specIndex = DetectFallbackSpecColumn(cellTexts, columnCount);
            int markIndex = DetectFallbackMarkColumn(cellTexts, columnCount, specIndex);

            if (specIndex < 0)
            {
                // 규격을 확정하지 못하면 잘못된 데이터 적재보다 기존 최소 fallback을 사용합니다.
                return CreateGridFallbackHeaderColumns(columnCount);
            }

            AddFallbackPhysicalColumn(columns, "MARK_NO", "번호", markIndex);

            int shapeIndex = -1;

            if (specIndex > 0)
            {
                // 번호와 규격 사이에 1개 이상 물리 컬럼이 있을 때 규격에 가장 가까운 왼쪽 셀을 실제 형상으로 사용합니다.
                int candidate = specIndex - 1;
                if (candidate != markIndex)
                {
                    shapeIndex = candidate;
                }
            }

            // 일부 표는 번호 | 규격 | 형상 | 길이 순서입니다.
            if (shapeIndex < 0 && specIndex + 1 < columnCount)
            {
                int rightCandidate = specIndex + 1;
                if (GetGridShapeContentScore(cellTexts, -1, rightCandidate) >= 1.10)
                {
                    shapeIndex = rightCandidate;
                }
            }

            // 번호와 규격 사이의 나머지 짧은 코드 컬럼은 형번/형상번호로 간주해 출력하지 않습니다.
            AddFallbackPhysicalColumn(columns, "SPEC", "철근규격", specIndex);
            if (shapeIndex >= 0)
            {
                AddFallbackPhysicalColumn(columns, "SHAPE", "철근형상", shapeIndex);
            }

            int valueStart = specIndex + 1;
            if (shapeIndex == specIndex + 1)
            {
                valueStart = shapeIndex + 1;
            }

            int remaining = columnCount - valueStart;
            if (remaining > 0) AddFallbackPhysicalColumn(columns, "LENGTH_MM", "길이(mm)", valueStart);
            if (remaining > 1) AddFallbackPhysicalColumn(columns, "QUANTITY_EA", "수량(EA)", valueStart + 1);
            if (remaining > 3)
            {
                AddFallbackPhysicalColumn(columns, "TOTAL_LENGTH_M", "총길이(M)", valueStart + 2);
                AddFallbackPhysicalColumn(columns, "TOTAL_WEIGHT", "중량(Ton)", valueStart + 3);
                if (remaining > 4) AddFallbackPhysicalColumn(columns, "NOTE", "비고", valueStart + 4);
            }
            else if (remaining > 2)
            {
                AddFallbackPhysicalColumn(columns, "TOTAL_WEIGHT", "중량(Ton)", valueStart + 2);
            }

            return columns;
        }

        private void AddFallbackPhysicalColumn(List<OviaHeaderColumn> columns, string key, string title, int sourceColumnIndex)
        {
            if (columns == null || sourceColumnIndex < 0)
            {
                return;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].SourceColumnIndex == sourceColumnIndex || columns[i].StandardKey == key)
                {
                    return;
                }
            }

            OviaHeaderColumn column = CreateHeaderColumn(key, title, sourceColumnIndex);
            column.SourceColumnIndex = sourceColumnIndex;
            columns.Add(column);
        }

        private int DetectFallbackSpecColumn(string[,] cellTexts, int columnCount)
        {
            int rowCount = cellTexts.GetLength(0);
            int bestIndex = -1;
            double bestRatio = 0.0;

            for (int c = 0; c < columnCount; c++)
            {
                int sample = 0;
                int matches = 0;
                int endRow = Math.Min(rowCount - 1, 39);

                for (int r = 0; r <= endRow; r++)
                {
                    string value = CleanCellText(cellTexts[r, c]);
                    if (value == "" || IsSummaryText(value) || IsHeaderRow(value))
                    {
                        continue;
                    }

                    sample++;
                    string first = GetFirstMeaningfulToken(value);
                    string compact = NormalizeGridHeaderText(value);
                    if (IsRebarSpecToken(first) || IsRebarSpecToken(compact))
                    {
                        matches++;
                    }
                }

                double ratio = sample == 0 ? 0.0 : (double)matches / (double)sample;
                if (matches >= 2 && ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestIndex = c;
                }
            }

            return bestRatio >= 0.55 ? bestIndex : -1;
        }

        private int DetectFallbackMarkColumn(string[,] cellTexts, int columnCount, int specIndex)
        {
            int limit = specIndex > 0 ? specIndex : columnCount;
            int bestIndex = 0;
            double bestScore = -1.0;

            for (int c = 0; c < limit; c++)
            {
                string pattern = DetectGridColumnValuePattern(cellTexts, -1, c);
                double score = 0.0;

                if (pattern == "sequential_number") score = 3.0;
                else if (pattern == "integer_number") score = 1.0;

                // 번호는 보통 가장 왼쪽에 있으므로 동률이면 왼쪽 컬럼을 유지합니다.
                score -= c * 0.01;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = c;
                }
            }

            return bestIndex;
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
             * 헤더 인식이 실패했을 때만 사용하는 보조 순서입니다.
             * 가능하면 실제 CAD 헤더를 우선 사용하고, 이 fallback은 기존에 잘 되던 기본 표 구조를 흔들지 않게 최소화합니다.
             */
            if (columnCount == 6)
            {
                // 번호 | 규격 | 형상 | 길이(mm) | 수량 | 중량(Ton)
                keys = new string[] { "MARK_NO", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_WEIGHT" };
                titles = new string[] { "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "중량(Ton)" };
            }
            else if (columnCount == 7)
            {
                // 번호 | 규격 | 형상 | 길이 | 수량 | 총길이(M) | 중량(Ton)
                keys = new string[] { "MARK_NO", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT" };
                titles = new string[] { "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)" };
            }
            else if (columnCount >= 10)
            {
                keys = new string[] { "MARK_NO", "PART", "SPEC", "IGNORE_SHAPE_NO", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "부위", "철근규격", "", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)", "비고" };
            }
            else if (columnCount == 9)
            {
                keys = new string[] { "MARK_NO", "PART", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "부위", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)", "비고" };
            }
            else if (columnCount == 8)
            {
                keys = new string[] { "MARK_NO", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_LENGTH_M", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)", "비고" };
            }
            else
            {
                keys = new string[] { "MARK_NO", "SPEC", "SHAPE", "LENGTH_MM", "QUANTITY_EA", "TOTAL_WEIGHT", "NOTE" };
                titles = new string[] { "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "중량(Ton)", "비고" };
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

            if (standardKey == "SHAPE_NO" || standardKey == "IGNORE_SHAPE_NO")
            {
                return "";
            }

            if (standardKey == "SHAPE")
            {
                return "철근형상";
            }

            if (standardKey == "SPEC")
            {
                return "철근규격";
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

            if (standardKey == "TOTAL_WEIGHT_KG")
            {
                return "중량(Ton)";
            }

            if (standardKey == "TOTAL_WEIGHT")
            {
                return "중량(Ton)";
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
            value = value.Replace(",", "");
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

            if (value.IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT_KG";
            }

            if (value.IndexOf("총중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("톤", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WEIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT";
            }

            if (value.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("총연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TOTALLENGTH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "TL")
            {
                return "TOTAL_LENGTH_M";
            }

            if (value.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("본수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("개수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("갯수", StringComparison.OrdinalIgnoreCase) >= 0 ||
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

            if (value.IndexOf("부위", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("위치", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("구간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("LOCATION", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PART";
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
                return "IGNORE_SHAPE_NO";
            }

            if (value.IndexOf("철근형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형태", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
            else if (key == "PART")
            {
                row.Part = AppendCell(row.Part, value);
            }
            else if (key == "SHAPE_NO")
            {
                row.ShapeNo = AppendCell(row.ShapeNo, value);
            }
            else if (key == "SHAPE")
            {
                row.ShapeText = AppendCell(row.ShapeText, value);
                row.ShapeRawText = AppendCell(row.ShapeRawText, value);
                row.ShapeDimensionText = AppendCell(row.ShapeDimensionText, BuildShapeDimensionAssignments(value, ""));
            }
            else if (key == "SPEC")
            {
                row.Spec = AppendCell(row.Spec, value);
            }
            else if (key == "LENGTH_MM")
            {
                row.Length = AppendCell(row.Length, FormatLengthMmText(value));
            }
            else if (key == "QUANTITY_EA")
            {
                row.Qty = AppendCell(row.Qty, value);
            }
            else if (key == "TOTAL_LENGTH_M")
            {
                row.TotalLength = AppendCell(row.TotalLength, value);
            }
            else if (key == "TOTAL_WEIGHT_KG")
            {
                row.TotalWeight = AppendCell(row.TotalWeight, ConvertKgTextToTonText(value));
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
                return FormatLengthMmText(row.Length);
            }

            if (key == "QUANTITY_EA")
            {
                return row.Qty;
            }

            if (key == "TOTAL_LENGTH_M")
            {
                return row.TotalLength;
            }

            if (key == "TOTAL_WEIGHT" || key == "TOTAL_WEIGHT_KG")
            {
                return row.TotalWeight;
            }

            if (key == "NOTE")
            {
                return row.Note;
            }

            return "";
        }

        private string ConvertKgTextToTonText(string value)
        {
            if (value == null)
            {
                return "";
            }

            Match match = Regex.Match(value.Replace(",", ""), @"-?\d+(\.\d+)?");

            if (!match.Success)
            {
                return CleanCellText(value);
            }

            decimal kg;

            if (!Decimal.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out kg))
            {
                if (!Decimal.TryParse(match.Value, out kg))
                {
                    return CleanCellText(value);
                }
            }

            decimal ton = kg / 1000m;
            string formatted = ton.ToString("0.###", CultureInfo.InvariantCulture);

            return formatted;
        }

        private string ExtractNumbersText(string text)
        {
            if (text == null)
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(NormalizeThousandsSeparators(text), @"-?\d+(\.\d+)?");

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

            // 표 선/헤더 인식이 일부 어긋나면 길이/수량/총길이/중량이 한 칸씩 밀릴 수 있습니다.
            // BarList 데이터 행에서는 규격 뒤쪽 숫자 중 마지막 4개가 산정값
            // 길이(mm) / 수량(EA) / 총길이(M) / 총중량(TON)이므로 이 값을 우선 보정합니다.
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

            Match match = Regex.Match(text, @"(?<![A-Z0-9])(?:UHD|SHD|HD|SD|D)\d{1,3}[A-Z]{0,4}(?![A-Z0-9])", RegexOptions.IgnoreCase);

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


        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Extract";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                bool isInvalid = false;

                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (current == invalidChars[j])
                    {
                        isInvalid = true;
                        break;
                    }
                }

                if (isInvalid || char.IsControl(current))
                {
                    builder.Append('_');
                }
                else if (char.IsWhiteSpace(current))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(current);
                }
            }

            string sanitized = builder.ToString().Trim('_', '.');
            return sanitized.Length == 0 ? "Extract" : sanitized;
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

            string csvBaseName = Path.GetFileNameWithoutExtension(csvFilePath);

            if (csvBaseName == null || csvBaseName.Trim() == "")
            {
                csvBaseName = "Extract_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            }

            csvBaseName = SanitizeFileName(csvBaseName);
            string relativeShapeDirectory = Path.Combine("Shapes", csvBaseName);
            string shapeDirectory = Path.Combine(csvDirectory, relativeShapeDirectory);

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
                    WriteUtf8TextAtomic(jsonFilePath, BuildCadShapeJson(row, elements));
                    row.CadShapeJsonPath = relativeShapeDirectory.Replace('\\', '/') + "/" + jsonFileName;
                    row.CadShapeTextValues = BuildCadShapeTextValues(elements);

                    /*
                     * OVIA 2026-05-27 보정:
                     * CAD 원본 형상은 JSON 안의 TEXT 요소가 실제 치수 표시입니다.
                     * 이전에는 철근형상 셀의 보조 텍스트가 OVIA_형상치수에 "0"으로 들어가면서
                     * 화면 렌더링 시 CAD 원본 치수 위에 0이 덮이는 문제가 있었습니다.
                     * 원본 CAD 형상은 JSON 값을 기준으로 수정창에서 다시 읽도록 하고,
                     * 추출 직후에는 수동 치수 오버레이 값을 비워 둡니다.
                     */
                    row.ShapeDimensionText = BuildShapeDimensionAssignments(row.ShapeRawText, row.CadShapeTextValues);
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
             * OVIA CAD 벡터 형상 v3:
             * 형상 셀의 안쪽을 임의 비율로 잘라서 수집하면 셀 가장자리에 배치된 실제 철근선과
             * 치수 문자가 함께 잘립니다. 35~37번의 하단 철근선과 38번의 좌측 160 표기가
             * 누락된 직접 원인이 이 선행 crop이었습니다.
             *
             * 이제 형상 셀 전체를 수집하고, 테이블 경계선은 "셀 전체 폭/높이를 거의 관통하는가"와
             * 실제 셀 경계에 붙어 있는가를 조합해 후처리에서만 제거합니다.
             */
            double captureWidth;
            double captureHeight;

            elements = ExtractCadShapeElementsByBounds(
                ed,
                db,
                minX,
                maxX,
                minY,
                maxY,
                out captureWidth,
                out captureHeight
            );

            /*
             * 단일 일자형 철근선은 표 가로선과 방향이 같기 때문에, 일부 도면에서는
             * 경계선 후처리 중 실제 형상선까지 제거될 수 있습니다. 필터 전 후보를 보존한 뒤
             * 최종 형상에 지오메트리가 하나도 남지 않은 경우에만 실제 셀 내부의 가장 적합한
             * 수평선을 복구합니다. 표 전체 폭을 관통하는 행 경계선은 복구 후보에서 제외합니다.
             */
            List<OviaCadShapeElement> unfilteredElements = new List<OviaCadShapeElement>(elements);

            RemoveCadShapeNoise(elements, captureWidth, captureHeight);
            RemoveCadShapeTableBorderLines(elements, captureWidth, captureHeight);
            KeepOnlyActualCadShapeElements(row, elements, captureWidth, captureHeight);
            RecoverMissingStraightCadShapeLine(elements, unfilteredElements, captureWidth, captureHeight);

            /*
             * OVIA CAD 벡터 형상 v2:
             * 실제 철근선이 녹색/회색 등 다른 색으로 작성된 도면도 있으므로
             * 색상만을 이유로 형상 요소를 삭제하지 않습니다.
             * 표 경계선 제거는 좌표·길이 기준 필터에서만 수행합니다.
             */
            return elements;
        }

        private List<OviaCadShapeElement> ExtractCadShapeElementsByBounds(Editor ed, Database db, double minX, double maxX, double minY, double maxY, out double width, out double height)
        {
            List<OviaCadShapeElement> elements = new List<OviaCadShapeElement>();

            width = maxX - minX;
            height = maxY - minY;

            if (ed == null || db == null || width <= 0.0001 || height <= 0.0001)
            {
                return elements;
            }

            /*
             * 철근형상 우측의 (DOWN)/(UP)는 실제 형상 셀 경계에 붙거나 아주 조금 바깥에
             * 배치되는 도면이 있습니다. 정확한 셀 범위만 SelectCrossingWindow에 사용하면
             * 해당 텍스트 객체가 후보 집합에 들어오지 않아 이후 필터에서 복구할 수 없습니다.
             * 후보 선택만 소폭 확장하고 실제 포함 여부는 원래 셀 범위로 다시 제한합니다.
             */
            double selectionMarginLeft = Math.Max(width * 0.03, 0.05);
            double selectionMarginRight = Math.Max(width * 0.20, 0.20);
            double selectionMarginY = Math.Max(height * 0.08, 0.05);
            Point3d selectMin = new Point3d(minX - selectionMarginLeft, minY - selectionMarginY, 0);
            Point3d selectMax = new Point3d(maxX + selectionMarginRight, maxY + selectionMarginY, 0);
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

            RemoveDuplicateCadShapeElements(elements);
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
                item.ColorIndex = GetEntityColorIndex(entity);
                item.X1 = NormalizeCadShapeX(p1.X, originX);
                item.Y1 = NormalizeCadShapeY(p1.Y, topY);
                item.X2 = NormalizeCadShapeX(p2.X, originX);
                item.Y2 = NormalizeCadShapeY(p2.Y, topY);
                elements.Add(item);
                return;
            }

            /*
             * OVIA CAD 벡터 형상 v2
             * ------------------------------------------------------------
             * Polyline의 bulge, Arc, Circle, Ellipse, Spline 등 모든 Curve 계열을
             * 원곡선의 형태가 유지되도록 충분히 세분화한 벡터 선분으로 보존합니다.
             *
             * 특히 BlockReference 안의 곡선은 블록 회전/축척 행렬을 각 점에 적용하므로,
             * 중심점만 변환하고 반지름/각도를 그대로 쓰던 이전 방식의 형상 왜곡을 방지합니다.
             */
            Polyline polyline = entity as Polyline;

            if (polyline != null)
            {
                if (CollectCadShapePolylineSegments(polyline, transform, elements, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }
            }

            Curve curve = entity as Curve;

            if (curve != null)
            {
                if (CollectCadShapeCurveSegments(curve, transform, elements, minX, maxX, minY, maxY, width, height))
                {
                    return;
                }
            }

            AttributeDefinition attributeDefinition = entity as AttributeDefinition;

            if (attributeDefinition != null)
            {
                // 실제 표시값은 BlockReference.AttributeCollection의 AttributeReference에서 수집합니다.
                return;
            }

            DBText dbText = entity as DBText;

            if (dbText != null)
            {
                Point3d p = GetTextReferencePoint(dbText, dbText.Position, transform);
                string value = CleanCadShapeText(dbText.TextString);

                if (value == "")
                {
                    return;
                }

                /*
                 * 문자의 기준점만 셀 안에 있는지 확인하면, 셀 가장자리에 걸쳐 배치된 치수 문자가
                 * 기준점 때문에 누락될 수 있습니다. 문자 실제 extents가 셀과 교차하면 보존합니다.
                 * (DOWN)/(UP) 방향 표시는 셀 우측 가장자리에 배치되는 경우가 많으므로 조금 더 넓은
                 * 허용 여유를 적용합니다.
                 */
                if (!DoesCadShapeTextIntersectCell(dbText, transform, p, minX, maxX, minY, maxY, width, height, IsCadShapeDirectionLabel(value)))
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "TEXT";
                item.Text = value;
                item.X1 = NormalizeCadShapeX(p.X, originX);
                item.Y1 = NormalizeCadShapeY(p.Y, topY);
                item.Height = dbText.Height;
                item.Rotation = GetTransformedCadShapeTextRotation(dbText.Rotation, transform);
                item.ColorIndex = GetEntityColorIndex(entity);
                PopulateCadShapeTextBounds(item, dbText, transform, originX, topY);
                elements.Add(item);
                return;
            }

            MText mText = entity as MText;

            if (mText != null)
            {
                Point3d p = GetTextReferencePoint(mText, mText.Location, transform);
                string value = CleanCadShapeText(mText.Contents);

                if (value == "")
                {
                    value = CleanCadShapeText(mText.Text);
                }

                if (value == "")
                {
                    return;
                }

                if (!DoesCadShapeTextIntersectCell(mText, transform, p, minX, maxX, minY, maxY, width, height, IsCadShapeDirectionLabel(value)))
                {
                    return;
                }

                OviaCadShapeElement item = new OviaCadShapeElement();
                item.Type = "TEXT";
                item.Text = value;
                item.X1 = NormalizeCadShapeX(p.X, originX);
                item.Y1 = NormalizeCadShapeY(p.Y, topY);
                item.Height = mText.TextHeight;
                item.Rotation = GetTransformedCadShapeTextRotation(mText.Rotation, transform);
                item.ColorIndex = GetEntityColorIndex(entity);
                PopulateCadShapeTextBounds(item, mText, transform, originX, topY);
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

                    /*
                     * 블록의 치수 숫자가 AttributeReference로 작성된 경우도 함께 수집합니다.
                     * AttributeReference 위치는 해당 블록이 놓인 상위 공간 기준이므로
                     * 현재 상위 transform만 적용하고 BlockTransform을 중복 적용하지 않습니다.
                     */
                    foreach (ObjectId attributeId in blockReference.AttributeCollection)
                    {
                        Entity attributeEntity = tr.GetObject(attributeId, OpenMode.ForRead, false) as Entity;

                        if (attributeEntity == null)
                        {
                            continue;
                        }

                        CollectCadShapeElementsFromEntity(tr, attributeEntity, transform, elements, minX, maxX, minY, maxY, width, height, depth + 1);
                    }
                }

                return;
            }

            /*
             * Dimension / Leader / MLeader처럼 화면에는 선과 문자가 보이지만
             * 모델 공간에서 DBText/Line으로 직접 존재하지 않는 주석 객체는
             * 표시 구성요소를 임시 Explode하여 벡터와 텍스트를 수집합니다.
             */
            if (ShouldExplodeCadShapeEntity(entity))
            {
                CollectCadShapeElementsFromExplodedEntity(
                    tr,
                    entity,
                    transform,
                    elements,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    width,
                    height,
                    depth
                );
            }
        }

        private bool DoesCadShapeTextIntersectCell(
            Entity entity,
            Matrix3d transform,
            Point3d referencePoint,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height,
            bool allowDirectionOutsideMargin
        )
        {
            double textMinX;
            double textMinY;
            double textMaxX;
            double textMaxY;

            if (TryGetTransformedCadShapeExtents(entity, transform, out textMinX, out textMinY, out textMaxX, out textMaxY))
            {
                double marginX = Math.Max(width * 0.012, 0.015);
                double marginY = Math.Max(height * 0.012, 0.015);

                if (allowDirectionOutsideMargin)
                {
                    marginX = Math.Max(marginX, width * 0.20);
                    marginY = Math.Max(marginY, height * 0.06);
                }

                return textMaxX >= minX - marginX
                    && textMinX <= maxX + marginX
                    && textMaxY >= minY - marginY
                    && textMinY <= maxY + marginY;
            }

            return IsPointInCadShapeCell(referencePoint, minX, maxX, minY, maxY, width, height)
                || (allowDirectionOutsideMargin
                    && referencePoint.X >= minX - Math.Max(width * 0.04, 0.05)
                    && referencePoint.X <= maxX + Math.Max(width * 0.20, 0.2)
                    && referencePoint.Y >= minY - Math.Max(height * 0.06, 0.05)
                    && referencePoint.Y <= maxY + Math.Max(height * 0.06, 0.05));
        }

        private bool IsCadShapeDirectionLabel(string value)
        {
            string normalized = NormalizeCadShapeDirectionLabel(value);
            return normalized.IndexOf("(DOWN)", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("(UP)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeCadShapeDirectionLabel(string value)
        {
            if (value == null)
            {
                return "";
            }

            string normalized = value.Trim().ToUpperInvariant();
            normalized = normalized.Replace("（", "(");
            normalized = normalized.Replace("）", ")");
            normalized = normalized.Replace("[", "(");
            normalized = normalized.Replace("]", ")");

            normalized = Regex.Replace(
                normalized,
                @"(?<![A-Z])\(?\s*(?:DOWN|DWON)\s*\)?(?![A-Z])",
                "(DOWN)",
                RegexOptions.IgnoreCase
            );
            normalized = Regex.Replace(
                normalized,
                @"(?<![A-Z])\(?\s*UP\s*\)?(?![A-Z])",
                "(UP)",
                RegexOptions.IgnoreCase
            );

            while (normalized.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized.Trim();
        }

        private void PopulateCadShapeTextBounds(
            OviaCadShapeElement item,
            Entity entity,
            Matrix3d transform,
            double originX,
            double topY
        )
        {
            if (item == null || entity == null)
            {
                return;
            }

            double minX;
            double minY;
            double maxX;
            double maxY;

            if (!TryGetTransformedCadShapeExtents(entity, transform, out minX, out minY, out maxX, out maxY))
            {
                return;
            }

            item.BoundsMinX = NormalizeCadShapeX(minX, originX);
            item.BoundsMaxX = NormalizeCadShapeX(maxX, originX);
            item.BoundsMinY = NormalizeCadShapeY(maxY, topY);
            item.BoundsMaxY = NormalizeCadShapeY(minY, topY);
            item.HasBounds = item.BoundsMaxX > item.BoundsMinX && item.BoundsMaxY > item.BoundsMinY;
        }

        private bool TryGetTransformedCadShapeExtents(
            Entity entity,
            Matrix3d transform,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY
        )
        {
            minX = Double.MaxValue;
            minY = Double.MaxValue;
            maxX = Double.MinValue;
            maxY = Double.MinValue;

            if (entity == null)
            {
                return false;
            }

            try
            {
                Extents3d extents = entity.GeometricExtents;
                Point3d sourceMin = extents.MinPoint;
                Point3d sourceMax = extents.MaxPoint;
                Point3d[] corners = new Point3d[]
                {
                    new Point3d(sourceMin.X, sourceMin.Y, sourceMin.Z),
                    new Point3d(sourceMin.X, sourceMax.Y, sourceMin.Z),
                    new Point3d(sourceMax.X, sourceMin.Y, sourceMin.Z),
                    new Point3d(sourceMax.X, sourceMax.Y, sourceMin.Z),
                    new Point3d(sourceMin.X, sourceMin.Y, sourceMax.Z),
                    new Point3d(sourceMin.X, sourceMax.Y, sourceMax.Z),
                    new Point3d(sourceMax.X, sourceMin.Y, sourceMax.Z),
                    new Point3d(sourceMax.X, sourceMax.Y, sourceMax.Z)
                };

                int i;

                for (i = 0; i < corners.Length; i++)
                {
                    Point3d point = corners[i].TransformBy(transform);

                    if (!IsFiniteCadShapePoint(point))
                    {
                        continue;
                    }

                    if (point.X < minX) minX = point.X;
                    if (point.Y < minY) minY = point.Y;
                    if (point.X > maxX) maxX = point.X;
                    if (point.Y > maxY) maxY = point.Y;
                }
            }
            catch
            {
                return false;
            }

            return minX != Double.MaxValue
                && minY != Double.MaxValue
                && maxX != Double.MinValue
                && maxY != Double.MinValue
                && maxX >= minX
                && maxY >= minY;
        }

        private string CleanCadShapeText(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = Regex.Replace(value, @"%%d", "°", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"%%p", "±", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"%%c", "Ø", RegexOptions.IgnoreCase);
            value = Regex.Replace(
                value,
                @"\\U\+([0-9A-Fa-f]{4})",
                delegate(Match match)
                {
                    int code;

                    if (Int32.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                    {
                        return Char.ConvertFromUtf32(code);
                    }

                    return match.Value;
                }
            );

            value = Regex.Replace(value, @"\\S([^;\^#]+)[\^#]([^;]+);", "$1/$2");
            value = Regex.Replace(value, @"\\[AaCcFfHhQqTtWw][^;]*;", "");
            value = Regex.Replace(value, @"\\[LlOoKk]", "");
            value = value.Replace("\\P", " ");
            value = value.Replace("\\p", " ");
            value = value.Replace("\\~", " ");
            value = value.Replace("\r", " ");
            value = value.Replace("\n", " ");
            value = value.Replace("\t", " ");
            value = value.Replace("{", "");
            value = value.Replace("}", "");
            value = value.Trim();

            while (value.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                value = value.Replace("  ", " ");
            }

            string directionLabel = NormalizeCadShapeDirectionLabel(value);

            if (IsCadShapeDirectionLabel(directionLabel))
            {
                value = directionLabel;
            }

            if (value.Length > 500)
            {
                value = value.Substring(0, 500);
            }

            return value;
        }

        private bool ShouldExplodeCadShapeEntity(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            string typeName = entity.GetType().Name;

            return typeName.IndexOf("Dimension", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.Equals("Shape", StringComparison.OrdinalIgnoreCase);
        }

        private void CollectCadShapeElementsFromExplodedEntity(
            Transaction tr,
            Entity sourceEntity,
            Matrix3d transform,
            List<OviaCadShapeElement> elements,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height,
            int depth
        )
        {
            DBObjectCollection explodedObjects = new DBObjectCollection();

            try
            {
                sourceEntity.Explode(explodedObjects);
            }
            catch
            {
                return;
            }

            foreach (DBObject explodedObject in explodedObjects)
            {
                Entity explodedEntity = explodedObject as Entity;

                try
                {
                    if (explodedEntity == null)
                    {
                        continue;
                    }

                    if (explodedEntity.GetType() == sourceEntity.GetType())
                    {
                        continue;
                    }

                    CollectCadShapeElementsFromEntity(
                        tr,
                        explodedEntity,
                        transform,
                        elements,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        width,
                        height,
                        depth + 1
                    );
                }
                finally
                {
                    if (explodedObject != null)
                    {
                        explodedObject.Dispose();
                    }
                }
            }
        }

        private bool CollectCadShapePolylineSegments(
            Polyline polyline,
            Matrix3d transform,
            List<OviaCadShapeElement> elements,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height
        )
        {
            if (polyline == null || elements == null || polyline.NumberOfVertices < 2)
            {
                return false;
            }

            int segmentCount = polyline.Closed
                ? polyline.NumberOfVertices
                : polyline.NumberOfVertices - 1;
            bool added = false;
            Matrix3d planeTransform;

            try
            {
                planeTransform = Matrix3d.PlaneToWorld(polyline.Normal);
            }
            catch
            {
                planeTransform = Matrix3d.Identity;
            }

            int segmentIndex;

            for (segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                int nextIndex = (segmentIndex + 1) % polyline.NumberOfVertices;
                Point2d start2d;
                Point2d end2d;
                double bulge;

                try
                {
                    start2d = polyline.GetPoint2dAt(segmentIndex);
                    end2d = polyline.GetPoint2dAt(nextIndex);
                    bulge = polyline.GetBulgeAt(segmentIndex);
                }
                catch
                {
                    continue;
                }

                if (Math.Abs(bulge) <= 0.0000001)
                {
                    Point3d startPoint = new Point3d(start2d.X, start2d.Y, polyline.Elevation)
                        .TransformBy(planeTransform)
                        .TransformBy(transform);
                    Point3d endPoint = new Point3d(end2d.X, end2d.Y, polyline.Elevation)
                        .TransformBy(planeTransform)
                        .TransformBy(transform);

                    if (ShouldKeepCadShapeLine(startPoint, endPoint, minX, maxX, minY, maxY, width, height))
                    {
                        AddCadShapeLineElement(elements, polyline, startPoint, endPoint, minX, maxY);
                        added = true;
                    }

                    continue;
                }

                double dx = end2d.X - start2d.X;
                double dy = end2d.Y - start2d.Y;
                double chordLength = Math.Sqrt(dx * dx + dy * dy);

                if (chordLength <= 0.0000001)
                {
                    continue;
                }

                double centerDistance = chordLength * (1.0 - bulge * bulge) / (4.0 * bulge);
                double midpointX = (start2d.X + end2d.X) / 2.0;
                double midpointY = (start2d.Y + end2d.Y) / 2.0;
                double perpendicularX = -dy / chordLength;
                double perpendicularY = dx / chordLength;
                double centerX = midpointX + perpendicularX * centerDistance;
                double centerY = midpointY + perpendicularY * centerDistance;
                double radius = chordLength * (1.0 + bulge * bulge) / (4.0 * Math.Abs(bulge));
                double startAngle = Math.Atan2(start2d.Y - centerY, start2d.X - centerX);
                double includedAngle = 4.0 * Math.Atan(bulge);
                int arcSamples = Math.Max(6, (int)Math.Ceiling(Math.Abs(includedAngle) * 18.0));

                if (arcSamples > 72)
                {
                    arcSamples = 72;
                }

                Point3d previousPoint = Point3d.Origin;
                bool hasPreviousPoint = false;
                int sampleIndex;

                for (sampleIndex = 0; sampleIndex <= arcSamples; sampleIndex++)
                {
                    double ratio = (double)sampleIndex / arcSamples;
                    double angle = startAngle + includedAngle * ratio;
                    Point3d currentPoint = new Point3d(
                        centerX + radius * Math.Cos(angle),
                        centerY + radius * Math.Sin(angle),
                        polyline.Elevation
                    ).TransformBy(planeTransform).TransformBy(transform);

                    if (hasPreviousPoint
                        && ShouldKeepCadShapeLine(previousPoint, currentPoint, minX, maxX, minY, maxY, width, height))
                    {
                        AddCadShapeLineElement(elements, polyline, previousPoint, currentPoint, minX, maxY);
                        added = true;
                    }

                    previousPoint = currentPoint;
                    hasPreviousPoint = true;
                }
            }

            return added;
        }

        private bool CollectCadShapeCurveSegments(
            Curve curve,
            Matrix3d transform,
            List<OviaCadShapeElement> elements,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height
        )
        {
            if (curve == null || elements == null)
            {
                return false;
            }

            double startParameter;
            double endParameter;

            try
            {
                startParameter = curve.StartParam;
                endParameter = curve.EndParam;
            }
            catch
            {
                return false;
            }

            if (Double.IsNaN(startParameter) || Double.IsInfinity(startParameter)
                || Double.IsNaN(endParameter) || Double.IsInfinity(endParameter)
                || Math.Abs(endParameter - startParameter) <= 0.0000001)
            {
                return false;
            }

            int sampleCount = GetCadShapeCurveSampleCount(curve, startParameter, endParameter);
            Point3d previousPoint = Point3d.Origin;
            bool hasPreviousPoint = false;
            bool added = false;
            int i;

            for (i = 0; i <= sampleCount; i++)
            {
                double ratio = sampleCount <= 0 ? 0.0 : (double)i / sampleCount;
                double parameter = startParameter + (endParameter - startParameter) * ratio;
                Point3d currentPoint;

                try
                {
                    currentPoint = curve.GetPointAtParameter(parameter).TransformBy(transform);
                }
                catch
                {
                    continue;
                }

                if (!IsFiniteCadShapePoint(currentPoint))
                {
                    continue;
                }

                if (hasPreviousPoint)
                {
                    double distance = previousPoint.DistanceTo(currentPoint);

                    if (distance > 0.000001
                        && ShouldKeepCadShapeLine(previousPoint, currentPoint, minX, maxX, minY, maxY, width, height))
                    {
                        AddCadShapeLineElement(elements, curve, previousPoint, currentPoint, minX, maxY);
                        added = true;
                    }
                }

                previousPoint = currentPoint;
                hasPreviousPoint = true;
            }

            return added;
        }

        private int GetCadShapeCurveSampleCount(Curve curve, double startParameter, double endParameter)
        {
            double span = Math.Abs(endParameter - startParameter);
            int sampleCount = (int)Math.Ceiling(span * 18.0);

            Polyline polyline = curve as Polyline;

            if (polyline != null)
            {
                sampleCount = Math.Max(sampleCount, Math.Max(2, polyline.NumberOfVertices) * 16);
            }
            else if (curve is Circle || curve is Ellipse)
            {
                sampleCount = Math.Max(sampleCount, 96);
            }
            else if (curve is Arc)
            {
                sampleCount = Math.Max(sampleCount, 36);
            }
            else if (curve is Spline)
            {
                sampleCount = Math.Max(sampleCount, 96);
            }
            else
            {
                sampleCount = Math.Max(sampleCount, 32);
            }

            if (sampleCount > 180)
            {
                sampleCount = 180;
            }

            return sampleCount;
        }

        private void AddCadShapeLineElement(
            List<OviaCadShapeElement> elements,
            Entity sourceEntity,
            Point3d point1,
            Point3d point2,
            double originX,
            double topY
        )
        {
            if (elements == null || sourceEntity == null)
            {
                return;
            }

            OviaCadShapeElement item = new OviaCadShapeElement();
            item.Type = "LINE";
            item.ColorIndex = GetEntityColorIndex(sourceEntity);
            item.X1 = NormalizeCadShapeX(point1.X, originX);
            item.Y1 = NormalizeCadShapeY(point1.Y, topY);
            item.X2 = NormalizeCadShapeX(point2.X, originX);
            item.Y2 = NormalizeCadShapeY(point2.Y, topY);
            elements.Add(item);
        }

        private bool IsFiniteCadShapePoint(Point3d point)
        {
            return !Double.IsNaN(point.X)
                && !Double.IsNaN(point.Y)
                && !Double.IsInfinity(point.X)
                && !Double.IsInfinity(point.Y);
        }

        private double GetTransformedCadShapeTextRotation(double sourceRotation, Matrix3d transform)
        {
            try
            {
                Point3d origin = Point3d.Origin.TransformBy(transform);
                Point3d directionPoint = new Point3d(
                    Math.Cos(sourceRotation),
                    Math.Sin(sourceRotation),
                    0
                ).TransformBy(transform);

                double dx = directionPoint.X - origin.X;
                double dy = directionPoint.Y - origin.Y;

                if (Math.Abs(dx) > 0.0000001 || Math.Abs(dy) > 0.0000001)
                {
                    return Math.Atan2(dy, dx) * 180.0 / Math.PI;
                }
            }
            catch
            {
            }

            return sourceRotation * 180.0 / Math.PI;
        }

        private int GetEntityColorIndex(Entity entity)
        {
            if (entity == null || entity.Color == null)
            {
                return 256;
            }

            try
            {
                return entity.Color.ColorIndex;
            }
            catch
            {
                return 256;
            }
        }

        private void RemoveDuplicateCadShapeElements(List<OviaCadShapeElement> elements)
        {
            if (elements == null || elements.Count <= 1)
            {
                return;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                string key = BuildCadShapeElementKey(item);

                if (keys.Contains(key))
                {
                    elements.RemoveAt(i);
                    continue;
                }

                keys.Add(key);
            }
        }

        private string BuildCadShapeElementKey(OviaCadShapeElement item)
        {
            if (item == null)
            {
                return "";
            }

            if (item.Type == "TEXT")
            {
                return "TEXT|"
                    + NormalizeCadShapeKeyNumber(item.X1) + "|"
                    + NormalizeCadShapeKeyNumber(item.Y1) + "|"
                    + NormalizeCadShapeKeyNumber(item.Rotation) + "|"
                    + (item.Text == null ? "" : item.Text.Trim());
            }

            if (item.Type == "LINE")
            {
                string first = NormalizeCadShapeKeyNumber(item.X1) + "," + NormalizeCadShapeKeyNumber(item.Y1);
                string second = NormalizeCadShapeKeyNumber(item.X2) + "," + NormalizeCadShapeKeyNumber(item.Y2);

                if (String.CompareOrdinal(first, second) > 0)
                {
                    string swap = first;
                    first = second;
                    second = swap;
                }

                return "LINE|" + first + "|" + second;
            }

            return (item.Type == null ? "" : item.Type) + "|"
                + NormalizeCadShapeKeyNumber(item.CX) + "|"
                + NormalizeCadShapeKeyNumber(item.CY) + "|"
                + NormalizeCadShapeKeyNumber(item.Radius) + "|"
                + NormalizeCadShapeKeyNumber(item.StartAngle) + "|"
                + NormalizeCadShapeKeyNumber(item.EndAngle);
        }

        private string NormalizeCadShapeKeyNumber(double value)
        {
            return Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }

        private List<OviaCadShapeElement> GetCadShapeTextElementsInReadingOrder(List<OviaCadShapeElement> elements)
        {
            List<OviaCadShapeElement> texts = new List<OviaCadShapeElement>();

            if (elements == null)
            {
                return texts;
            }

            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item != null && item.Type == "TEXT" && item.Text != null && item.Text.Trim() != "")
                {
                    texts.Add(item);
                }
            }

            texts.Sort(
                delegate(OviaCadShapeElement left, OviaCadShapeElement right)
                {
                    double leftHeight = Math.Max(left == null ? 0 : left.Height, 0.1);
                    double rightHeight = Math.Max(right == null ? 0 : right.Height, 0.1);
                    double rowTolerance = Math.Max(Math.Min(leftHeight, rightHeight) * 0.65, 0.25);
                    double deltaY = (left == null ? 0 : left.Y1) - (right == null ? 0 : right.Y1);

                    if (Math.Abs(deltaY) > rowTolerance)
                    {
                        return deltaY < 0 ? -1 : 1;
                    }

                    double deltaX = (left == null ? 0 : left.X1) - (right == null ? 0 : right.X1);

                    if (Math.Abs(deltaX) > 0.0001)
                    {
                        return deltaX < 0 ? -1 : 1;
                    }

                    return String.Compare(
                        left == null ? "" : left.Text,
                        right == null ? "" : right.Text,
                        StringComparison.Ordinal
                    );
                }
            );

            return texts;
        }

        private bool IsWhiteLikeCadShapeColor(int colorIndex)
        {
            return colorIndex == 0 || colorIndex == 7 || colorIndex == 8 || colorIndex == 9 || colorIndex == 256 || colorIndex >= 250;
        }

        private void PreferDominantWhiteCadShapeElements(List<OviaCadShapeElement> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            int whiteGeometryCount = 0;
            int explicitColoredGeometryCount = 0;
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type == "TEXT")
                {
                    continue;
                }

                if (IsWhiteLikeCadShapeColor(item.ColorIndex))
                {
                    whiteGeometryCount++;
                }
                else
                {
                    explicitColoredGeometryCount++;
                }
            }

            if (whiteGeometryCount <= 0 || explicitColoredGeometryCount <= 0)
            {
                return;
            }

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type == "TEXT")
                {
                    continue;
                }

                if (!IsWhiteLikeCadShapeColor(item.ColorIndex))
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private void RemoveCadShapeTableBorderLines(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.02);
            double edgeToleranceX = Math.Max(width * 0.055, 0.05);
            double edgeToleranceY = Math.Max(height * 0.055, 0.05);
            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "LINE")
                {
                    continue;
                }

                double dx = Math.Abs(item.X1 - item.X2);
                double dy = Math.Abs(item.Y1 - item.Y2);
                double centerX = (item.X1 + item.X2) / 2.0;
                double centerY = (item.Y1 + item.Y2) / 2.0;
                bool vertical = dx <= axisTolerance;
                bool horizontal = dy <= axisTolerance;

                /*
                 * 형상 셀 좌/우 경계에 붙은 수직선은 대부분 테이블 세로선입니다.
                 * 실제 철근형상 안의 짧은 끝단 표시는 셀 중앙 쪽에 있으므로 보존됩니다.
                 */
                if (vertical && dy >= height * 0.84)
                {
                    if (centerX <= edgeToleranceX || centerX >= width - edgeToleranceX)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }
                }

                /*
                 * 행 상/하 경계에 붙은 긴 수평선은 테이블 가로선입니다.
                 */
                if (horizontal && dx >= width * 0.84)
                {
                    if (centerY <= edgeToleranceY || centerY >= height - edgeToleranceY)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }
                }
            }
        }

        private bool ShouldKeepCadShapeLine(Point3d p1, Point3d p2, double minX, double maxX, double minY, double maxY, double width, double height)
        {
            Point3d center = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
            double dx = Math.Abs(p1.X - p2.X);
            double dy = Math.Abs(p1.Y - p2.Y);
            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            bool horizontal = dy <= axisTolerance;
            bool vertical = dx <= axisTolerance;

            /*
             * 일자형 철근은 표 가로선과 방향이 같지만, 셀 전체 폭을 관통하지 않고 셀 중앙에
             * 짧게 배치됩니다. 일부 행은 CAD 객체의 extents/블록 기준점 차이로 선의 끝점이
             * 셀 판정 여유 밖에 놓여 기존의 첫 번째 포함 검사에서 누락될 수 있었습니다.
             * 중앙의 중간 길이 수평선은 표 경계선이 아니므로 일반 포함 검사보다 먼저 보존합니다.
             */
            double straightMinimumLength = Math.Max(width * 0.12, 0.20);
            double straightMaximumLength = width * 0.84;
            double straightRelaxedY = Math.Max(height * 0.14, 0.10);
            double straightSideMargin = Math.Max(width * 0.035, 0.05);
            bool likelyStraightBarLine = horizontal
                && dx >= straightMinimumLength
                && dx <= straightMaximumLength
                && center.X >= minX + straightSideMargin
                && center.X <= maxX - straightSideMargin
                && center.Y >= minY - straightRelaxedY
                && center.Y <= maxY + straightRelaxedY;

            if (likelyStraightBarLine)
            {
                return true;
            }

            if (!IsPointInCadShapeCell(p1, minX, maxX, minY, maxY, width, height)
                && !IsPointInCadShapeCell(p2, minX, maxX, minY, maxY, width, height)
                && !IsPointInCadShapeCell(center, minX, maxX, minY, maxY, width, height))
            {
                return false;
            }

            double edgeToleranceX = Math.Max(width * 0.055, 0.05);
            double edgeToleranceY = Math.Max(height * 0.055, 0.05);

            if (vertical && dy >= height * 0.84)
            {
                if (center.X <= minX + edgeToleranceX || center.X >= maxX - edgeToleranceX)
                {
                    return false;
                }
            }

            if (horizontal && dx >= width * 0.84)
            {
                if (center.Y <= minY + edgeToleranceY || center.Y >= maxY - edgeToleranceY)
                {
                    return false;
                }
            }

            bool horizontalBorder = horizontal
                && dx >= width * 0.90
                && Math.Min(p1.X, p2.X) <= minX + axisTolerance
                && Math.Max(p1.X, p2.X) >= maxX - axisTolerance
                && (Math.Abs(center.Y - minY) <= axisTolerance || Math.Abs(center.Y - maxY) <= axisTolerance);

            if (horizontalBorder)
            {
                return false;
            }

            bool verticalBorder = vertical
                && dy >= height * 0.90
                && Math.Min(p1.Y, p2.Y) <= minY + axisTolerance
                && Math.Max(p1.Y, p2.Y) >= maxY - axisTolerance
                && (Math.Abs(center.X - minX) <= axisTolerance || Math.Abs(center.X - maxX) <= axisTolerance);

            if (verticalBorder)
            {
                return false;
            }

            return true;
        }

        private bool IsPointInCadShapeCell(Point3d point, double minX, double maxX, double minY, double maxY, double width, double height)
        {
            /*
             * 형상 캡처는 이미 셀 안쪽 안전 영역을 기준으로 수행합니다.
             * 여기서 여백을 크게 주면 BlockReference 내부의 테이블 경계선이 다시 살아납니다.
             */
            double marginX = Math.Max(width * 0.018, 0.02);
            double marginY = Math.Max(height * 0.018, 0.02);

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

                    double edgeTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
                    bool nearTopOrBottom = item.Y1 <= edgeTolerance || item.Y2 <= edgeTolerance || item.Y1 >= height - edgeTolerance || item.Y2 >= height - edgeTolerance;
                    bool nearLeftOrRight = item.X1 <= edgeTolerance || item.X2 <= edgeTolerance || item.X1 >= width - edgeTolerance || item.X2 >= width - edgeTolerance;

                    bool horizontalBorder = dx >= width * 0.90
                        && nearTopOrBottom
                        && Math.Min(item.X1, item.X2) <= edgeTolerance
                        && Math.Max(item.X1, item.X2) >= width - edgeTolerance;

                    if (horizontalBorder)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }

                    bool verticalBorder = dy >= height * 0.90
                        && nearLeftOrRight
                        && Math.Min(item.Y1, item.Y2) <= edgeTolerance
                        && Math.Max(item.Y1, item.Y2) >= height - edgeTolerance;

                    if (verticalBorder)
                    {
                        elements.RemoveAt(i);
                        continue;
                    }
                }
            }
        }

        private void RecoverMissingStraightCadShapeLine(
            List<OviaCadShapeElement> filteredElements,
            List<OviaCadShapeElement> unfilteredElements,
            double width,
            double height)
        {
            if (filteredElements == null || unfilteredElements == null || unfilteredElements.Count == 0)
            {
                return;
            }

            int i;

            /*
             * 기존에는 필터 결과에 아주 짧은 선 조각 하나만 남아도 "형상이 있다"고 판단해
             * 일자형 복구를 중단했습니다. 번호 2/4처럼 실제 수평선은 빠지고 보이지 않는 작은
             * 경계 조각만 남는 행에서는 숫자만 표시되는 원인이 되었습니다.
             * 이제 셀 중앙에 의미 있는 크기의 지오메트리가 남았을 때만 복구를 생략합니다.
             */
            if (HasMeaningfulCadShapeGeometry(filteredElements, width, height))
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            double minimumLength = Math.Max(width * 0.14, 0.25);
            double maximumLength = width * 0.82;
            double edgeMarginY = Math.Max(height * 0.12, 0.08);
            double edgeMarginX = Math.Max(width * 0.06, 0.08);
            OviaCadShapeElement best = null;
            double bestScore = Double.MinValue;

            for (i = 0; i < unfilteredElements.Count; i++)
            {
                OviaCadShapeElement candidate = unfilteredElements[i];

                if (candidate == null || candidate.Type != "LINE")
                {
                    continue;
                }

                double dx = Math.Abs(candidate.X2 - candidate.X1);
                double dy = Math.Abs(candidate.Y2 - candidate.Y1);

                if (dy > axisTolerance || dx < minimumLength || dx > maximumLength)
                {
                    continue;
                }

                double minLineX = Math.Min(candidate.X1, candidate.X2);
                double maxLineX = Math.Max(candidate.X1, candidate.X2);
                double centerX = (candidate.X1 + candidate.X2) / 2.0;
                double centerY = (candidate.Y1 + candidate.Y2) / 2.0;

                if (centerY <= edgeMarginY || centerY >= height - edgeMarginY)
                {
                    continue;
                }

                if (minLineX <= edgeMarginX && maxLineX >= width - edgeMarginX)
                {
                    continue;
                }

                double centerDistance = Math.Abs(centerX - width / 2.0) / Math.Max(width, 0.0001);
                double score = dx - centerDistance * width * 0.20;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
            {
                filteredElements.Add(best);
            }
        }

        private bool HasMeaningfulCadShapeGeometry(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count == 0)
            {
                return false;
            }

            double minimumLineLength = Math.Max(Math.Min(width, height) * 0.10, 0.18);
            double edgeMarginX = Math.Max(width * 0.035, 0.05);
            double edgeMarginY = Math.Max(height * 0.08, 0.06);
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type == "TEXT")
                {
                    continue;
                }

                if (item.Type == "ARC" || item.Type == "CIRCLE")
                {
                    if (Math.Abs(item.Radius) >= minimumLineLength * 0.30)
                    {
                        return true;
                    }

                    continue;
                }

                if (item.Type != "LINE")
                {
                    continue;
                }

                double dx = item.X2 - item.X1;
                double dy = item.Y2 - item.Y1;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double centerX = (item.X1 + item.X2) / 2.0;
                double centerY = (item.Y1 + item.Y2) / 2.0;

                if (length < minimumLineLength)
                {
                    continue;
                }

                if (centerX <= edgeMarginX || centerX >= width - edgeMarginX
                    || centerY <= edgeMarginY || centerY >= height - edgeMarginY)
                {
                    continue;
                }

                return true;
            }

            return false;
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

                bool inTightShapeBox = item.X1 >= tightMinX && item.X1 <= tightMaxX && item.Y1 >= tightMinY && item.Y1 <= tightMaxY;

                /*
                 * OVIA 2026-05-27 보정:
                 * 단순 직선형/ㄱ자형 철근은 형상선의 높이가 매우 작습니다.
                 * 이때 geomHeight 기준 looseBox가 지나치게 얇아져서 형상 위쪽의 치수 텍스트
                 * 10000, 9000, 9640 같은 값이 삭제되고, 화면에는 0만 남는 문제가 있었습니다.
                 *
                 * 이 함수는 이미 철근형상 셀 안에서 수집된 TEXT만 처리하므로,
                 * 텍스트를 looseBox 밖이라는 이유만으로 제거하지 않습니다.
                 * 단, 번호/규격처럼 명확한 외부 행 값이 형상 셀에 섞인 경우만 제거합니다.
                 */
                if (IsExternalRowValueText(row, item.Text) && !inTightShapeBox)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (IsExternalRowMetricText(row, item.Text)
                    && IsCadShapeTextClearlyOutsideGeometry(item, geomMinX, geomMinY, geomMaxX, geomMaxY, width, height, tightMinX, tightMinY, tightMaxX, tightMaxY))
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

            return false;
        }

        private bool IsExternalRowMetricText(OviaBarTableRow row, string text)
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

            if (IsSameCadShapeCompareValue(value, row.Length))
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

        private bool IsCadShapeTextClearlyOutsideGeometry(
            OviaCadShapeElement item,
            double geomMinX,
            double geomMinY,
            double geomMaxX,
            double geomMaxY,
            double width,
            double height,
            double tightMinX,
            double tightMinY,
            double tightMaxX,
            double tightMaxY)
        {
            if (item == null)
            {
                return false;
            }

            double textMinX = item.HasBounds ? item.BoundsMinX : item.X1;
            double textMaxX = item.HasBounds ? item.BoundsMaxX : item.X1;
            double textMinY = item.HasBounds ? item.BoundsMinY : item.Y1;
            double textMaxY = item.HasBounds ? item.BoundsMaxY : item.Y1;
            double centerX = (textMinX + textMaxX) / 2.0;
            double centerY = (textMinY + textMaxY) / 2.0;
            double textWidth = Math.Max(textMaxX - textMinX, 0.0);
            double textHeight = Math.Max(textMaxY - textMinY, 0.0);

            if (centerX >= tightMinX && centerX <= tightMaxX && centerY >= tightMinY && centerY <= tightMaxY)
            {
                return false;
            }

            double rightGap = textMinX - geomMaxX;
            double leftGap = geomMinX - textMaxX;
            double topGap = geomMinY - textMaxY;
            double bottomGap = textMinY - geomMaxY;
            double nearMarginX = Math.Max(width * 0.045, textWidth * 0.55);
            double nearMarginY = Math.Max(height * 0.08, textHeight * 0.65);
            bool overlapsGeometryEnvelope = textMaxX >= geomMinX - nearMarginX
                && textMinX <= geomMaxX + nearMarginX
                && textMaxY >= geomMinY - nearMarginY
                && textMinY <= geomMaxY + nearMarginY;

            if (overlapsGeometryEnvelope)
            {
                return false;
            }

            if (rightGap > Math.Max(width * 0.03, 0.6) || leftGap > Math.Max(width * 0.03, 0.6))
            {
                return true;
            }

            if (topGap > Math.Max(height * 0.12, 0.8) || bottomGap > Math.Max(height * 0.12, 0.8))
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
                    if (item.HasBounds)
                    {
                        ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.BoundsMinX, item.BoundsMinY);
                        ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.BoundsMaxX, item.BoundsMaxY);
                    }
                    else
                    {
                        double estimatedHeight = Math.Max(item.Height, 0.8);
                        double estimatedWidth = Math.Max(
                            estimatedHeight * 0.55 * Math.Max(item.Text == null ? 0 : item.Text.Length, 1),
                            estimatedHeight
                        );
                        double halfWidth = estimatedWidth / 2.0;
                        double halfHeight = estimatedHeight / 2.0;
                        ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.X1 - halfWidth, item.Y1 - halfHeight);
                        ExpandCadShapeBounds(ref minX, ref minY, ref maxX, ref maxY, item.X1 + halfWidth, item.Y1 + halfHeight);
                    }

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

            /*
             * v3에서는 문자 실제 extents까지 content bounds에 포함되므로
             * 단순 기준점만 사용하던 v2보다 잘림 방지 여백을 정확하게 계산할 수 있습니다.
             */
            double padX = Math.Max(contentWidth * 0.06, 0.8);
            double padY = Math.Max(contentHeight * 0.08, 0.8);
            double offsetX = cropMinX - padX;
            double offsetY = cropMinY - padY;
            double outputWidth = contentWidth + padX * 2.0;
            double outputHeight = contentHeight + padY * 2.0;

            List<OviaCadShapeElement> orderedTexts = GetCadShapeTextElementsInReadingOrder(elements);
            Dictionary<OviaCadShapeElement, string> textIds = new Dictionary<OviaCadShapeElement, string>();
            int textIndex;

            for (textIndex = 0; textIndex < orderedTexts.Count; textIndex++)
            {
                textIds[orderedTexts[textIndex]] = "T" + (textIndex + 1).ToString(CultureInfo.InvariantCulture);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"version\": 3,\r\n");
            sb.Append("  \"source\": \"CAD\",\r\n");
            sb.Append("  \"coordinateSystem\": \"TOP_LEFT_Y_DOWN\",\r\n");
            sb.Append("  \"textPolicy\": {\"fontFamily\": \"맑은 고딕\", \"fontSizePt\": 8, \"preservePosition\": true, \"editableTextIds\": true},\r\n");
            sb.Append("  \"rowNo\": ").Append(row == null ? 0 : row.No).Append(",\r\n");
            sb.Append("  \"cell\": {");
            sb.Append("\"width\": ").Append(JsonNumber(outputWidth)).Append(", ");
            sb.Append("\"height\": ").Append(JsonNumber(outputHeight));
            sb.Append("},\r\n");
            sb.Append("  \"elements\": [\r\n");

            int i;
            int writtenCount = 0;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    continue;
                }

                if (writtenCount > 0)
                {
                    sb.Append(",\r\n");
                }

                writtenCount++;

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
                    string textId;

                    sb.Append(", \"text\": ");
                    AppendJsonString(sb, item.Text);

                    if (textIds.TryGetValue(item, out textId))
                    {
                        sb.Append(", \"textId\": ");
                        AppendJsonString(sb, textId);
                    }

                    sb.Append(", \"x\": ").Append(JsonNumber(item.X1 - offsetX));
                    sb.Append(", \"y\": ").Append(JsonNumber(item.Y1 - offsetY));
                    sb.Append(", \"height\": ").Append(JsonNumber(item.Height));
                    sb.Append(", \"rotation\": ").Append(JsonNumber(item.Rotation));
                    sb.Append(", \"align\": \"CENTER\"");

                    if (item.HasBounds)
                    {
                        sb.Append(", \"boundsMinX\": ").Append(JsonNumber(item.BoundsMinX - offsetX));
                        sb.Append(", \"boundsMinY\": ").Append(JsonNumber(item.BoundsMinY - offsetY));
                        sb.Append(", \"boundsMaxX\": ").Append(JsonNumber(item.BoundsMaxX - offsetX));
                        sb.Append(", \"boundsMaxY\": ").Append(JsonNumber(item.BoundsMaxY - offsetY));
                    }
                }

                sb.Append(", \"colorIndex\": ").Append(item.ColorIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
            }

            sb.Append("\r\n  ]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private string BuildCadShapeTextValues(List<OviaCadShapeElement> elements)
        {
            List<OviaCadShapeElement> orderedTexts = GetCadShapeTextElementsInReadingOrder(elements);

            if (orderedTexts.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < orderedTexts.Count; i++)
            {
                OviaCadShapeElement item = orderedTexts[i];

                if (sb.Length > 0)
                {
                    sb.Append("|");
                }

                sb.Append(item.Text.Trim());
            }

            return sb.ToString();
        }

        private string BuildShapeDimensionAssignments(string shapeRawText, string cadShapeTextValues)
        {
            string source = CleanCellText(shapeRawText);

            if (source == "")
            {
                return "";
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddShapeDimensionAssignments(values, source);

            if (values.Count == 0 && cadShapeTextValues != null && cadShapeTextValues.Trim() != "")
            {
                AddShapeDimensionAssignments(values, cadShapeTextValues.Replace("|", " "));
            }

            if (values.Count == 0)
            {
                return "";
            }

            string[] order = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "R1", "R2", "R3", "R4" };
            StringBuilder sb = new StringBuilder();
            int i;

            for (i = 0; i < order.Length; i++)
            {
                string value;

                if (!values.TryGetValue(order[i], out value) || value == "")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }

                sb.Append(order[i]);
                sb.Append("=");
                sb.Append(value);
            }

            return sb.ToString();
        }

        private void AddShapeDimensionAssignments(Dictionary<string, string> values, string text)
        {
            if (values == null || text == null)
            {
                return;
            }

            MatchCollection beforeLabel = Regex.Matches(text, @"(?<value>-?\d+(?:,\d{3})*(?:\.\d+)?)\s*(?<key>R[1-4]|[A-H])\b", RegexOptions.IgnoreCase);
            MatchCollection afterLabel = Regex.Matches(text, @"\b(?<key>R[1-4]|[A-H])\s*(?<value>-?\d+(?:,\d{3})*(?:\.\d+)?)", RegexOptions.IgnoreCase);
            AddShapeDimensionMatches(values, beforeLabel);
            AddShapeDimensionMatches(values, afterLabel);
        }

        private void AddShapeDimensionMatches(Dictionary<string, string> values, MatchCollection matches)
        {
            if (values == null || matches == null)
            {
                return;
            }

            int i;

            for (i = 0; i < matches.Count; i++)
            {
                string key = matches[i].Groups["key"].Value.ToUpperInvariant();
                string value = NormalizeNumericToken(matches[i].Groups["value"].Value);

                if (key == "" || value == "")
                {
                    continue;
                }

                if (!values.ContainsKey(key))
                {
                    values.Add(key, value);
                }
            }
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

        private void WriteBarTableCsv(string filePath, List<OviaBarTableRow> rows, Document doc)
        {
            string sourceDrawingName;
            string sourceDrawingPath;
            ResolveSourceDrawingInfo(doc, out sourceDrawingName, out sourceDrawingPath);

            string temporaryPath = filePath + ".tmp";
            DeleteFileQuietly(temporaryPath);

            using (StreamWriter writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
            {
                /*
                 * 사용자 화면/CSV 출력 컬럼은 항상 기존 BarList 표준 순서를 유지합니다.
                 * 헤더 자동 인식 결과가 일부 컬럼만 잡힌 경우에도 출력 컬럼이 줄어들면 안 됩니다.
                 *
                 * 출력 기준:
                 * 번호 | 부위 | 철근규격 | 철근형상 | 길이(mm) | 수량(EA) | 총길이(M) | 중량(Ton) | 비고 | 원본 도면
                 *
                 * lastDetectedHeaderColumns는 CAD 셀 위치 분석/추출용으로만 사용하고,
                 * CSV 출력은 표준 컬럼으로 고정합니다.
                 */
                List<OviaHeaderColumn> columns = CreateFallbackHeaderColumns();

                writer.Write("No,RowType,SourceRowNo");

                int h;

                for (h = 0; h < columns.Count; h++)
                {
                    writer.Write(",");
                    writer.Write(Csv(columns[h].OriginalTitle));
                }

                /*
                 * OVIA 2026-07-14 원본 도면 추적:
                 * 사용자 표시용 "원본 도면"은 비고 우측의 표준 컬럼으로 전달하고,
                 * 전체 경로는 OVIA 내부 숨김 컬럼으로 함께 보존합니다.
                 * 서로 다른 DWG에서 연속 추출해도 각 CSV 행이 자신의 출처를 유지합니다.
                 */
                writer.Write(",");
                writer.Write(Csv("원본 도면"));
                writer.Write(",");
                writer.Write(Csv("OVIA_원본도면경로"));

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
                    writer.Write(Csv(sourceDrawingName));
                    writer.Write(",");
                    writer.Write(Csv(sourceDrawingPath));

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

            PublishFileAtomic(temporaryPath, filePath);
            WriteExtractionReadyMarker(filePath, rows);
        }

        private static void WriteUtf8TextAtomic(string filePath, string content)
        {
            string temporaryPath = filePath + ".tmp";
            DeleteFileQuietly(temporaryPath);
            File.WriteAllText(temporaryPath, content ?? "", new UTF8Encoding(true));
            PublishFileAtomic(temporaryPath, filePath);
        }

        private static void PublishFileAtomic(string temporaryPath, string finalPath)
        {
            if (File.Exists(finalPath))
            {
                string backupPath = finalPath + ".bak";
                DeleteFileQuietly(backupPath);
                File.Replace(temporaryPath, finalPath, backupPath, true);
                DeleteFileQuietly(backupPath);
            }
            else
            {
                File.Move(temporaryPath, finalPath);
            }
        }

        private static void DeleteFileQuietly(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private static void WriteExtractionReadyMarker(string csvFilePath, List<OviaBarTableRow> rows)
        {
            string markerPath = csvFilePath + ".ready";
            string marker = "OVIA_EXTRACTION_READY_V1" + Environment.NewLine
                + "Rows=" + (rows == null ? 0 : rows.Count).ToString(CultureInfo.InvariantCulture) + Environment.NewLine
                + "CompletedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            WriteUtf8TextAtomic(markerPath, marker);
        }

        private void ResolveSourceDrawingInfo(Document doc, out string displayName, out string fullPath)
        {
            displayName = "미저장 도면";
            fullPath = "";

            if (doc == null)
            {
                return;
            }

            string databaseFileName = "";

            try
            {
                if (doc.Database != null && doc.Database.Filename != null)
                {
                    databaseFileName = doc.Database.Filename.Trim();
                }
            }
            catch
            {
                databaseFileName = "";
            }

            string directoryName = "";

            try
            {
                directoryName = databaseFileName == "" ? "" : Path.GetDirectoryName(databaseFileName);
            }
            catch
            {
                directoryName = "";
            }

            if (databaseFileName != "" && directoryName != null && directoryName.Trim() != "")
            {
                fullPath = databaseFileName;

                try
                {
                    displayName = Path.GetFileName(databaseFileName);
                }
                catch
                {
                    displayName = databaseFileName;
                }

                if (displayName == null || displayName.Trim() == "")
                {
                    displayName = databaseFileName;
                }

                return;
            }

            string documentName = "";

            try
            {
                documentName = doc.Name == null ? "" : doc.Name.Trim();
            }
            catch
            {
                documentName = "";
            }

            if (documentName == "")
            {
                documentName = databaseFileName;
            }

            if (documentName != "")
            {
                try
                {
                    documentName = Path.GetFileName(documentName);
                }
                catch
                {
                }
            }

            if (documentName == "")
            {
                displayName = "미저장 도면";
                return;
            }

            displayName = documentName + " [미저장 도면]";
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

        private bool GetLatestOviaBoxExtents(Database db, out Point3d minPoint, out Point3d maxPoint, out int boxCount)
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

                foreach (ObjectId objectId in modelSpace)
                {
                    Entity entity = null;

                    try
                    {
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
                        Extents3d extents = entity.GeometricExtents;
                        minPoint = extents.MinPoint;
                        maxPoint = extents.MaxPoint;
                        boxCount++;
                    }
                    catch
                    {
                        continue;
                    }
                }

                tr.Commit();
            }

            return boxCount > 0;
        }

        private List<OviaSelectionRectangle> BuildNonOverlappingOviaSelectionRectangles(
            Database db,
            Point3d point1,
            Point3d point2,
            out int overlappedBoxCount
        )
        {
            overlappedBoxCount = 0;

            OviaSelectionRectangle selectedRectangle = new OviaSelectionRectangle(
                point1.X,
                point2.X,
                point1.Y,
                point2.Y
            );

            List<OviaSelectionRectangle> remainingRectangles = new List<OviaSelectionRectangle>();

            if (selectedRectangle.Width <= OviaBoxOverlapTolerance || selectedRectangle.Height <= OviaBoxOverlapTolerance)
            {
                return remainingRectangles;
            }

            remainingRectangles.Add(selectedRectangle);

            List<OviaSelectionRectangle> existingRectangles = GetExistingOviaSelectionRectangles(db);
            int existingIndex;

            for (existingIndex = 0; existingIndex < existingRectangles.Count; existingIndex++)
            {
                OviaSelectionRectangle existingRectangle = existingRectangles[existingIndex];
                List<OviaSelectionRectangle> nextRectangles = new List<OviaSelectionRectangle>();
                bool overlappedCurrentBox = false;
                int remainingIndex;

                for (remainingIndex = 0; remainingIndex < remainingRectangles.Count; remainingIndex++)
                {
                    OviaSelectionRectangle candidate = remainingRectangles[remainingIndex];

                    if (!HasMeaningfulHorizontalOverlap(candidate, existingRectangle))
                    {
                        nextRectangles.Add(candidate);
                        continue;
                    }

                    double overlapMinY = Math.Max(candidate.MinY, existingRectangle.MinY);
                    double overlapMaxY = Math.Min(candidate.MaxY, existingRectangle.MaxY);

                    if (overlapMaxY - overlapMinY <= OviaBoxOverlapTolerance)
                    {
                        nextRectangles.Add(candidate);
                        continue;
                    }

                    overlappedCurrentBox = true;

                    if (overlapMinY - candidate.MinY > OviaBoxOverlapTolerance)
                    {
                        nextRectangles.Add(
                            new OviaSelectionRectangle(
                                candidate.MinX,
                                candidate.MaxX,
                                candidate.MinY,
                                overlapMinY
                            )
                        );
                    }

                    if (candidate.MaxY - overlapMaxY > OviaBoxOverlapTolerance)
                    {
                        nextRectangles.Add(
                            new OviaSelectionRectangle(
                                candidate.MinX,
                                candidate.MaxX,
                                overlapMaxY,
                                candidate.MaxY
                            )
                        );
                    }
                }

                if (overlappedCurrentBox)
                {
                    overlappedBoxCount++;
                }

                remainingRectangles = nextRectangles;

                if (remainingRectangles.Count == 0)
                {
                    break;
                }
            }

            remainingRectangles.Sort(delegate(OviaSelectionRectangle left, OviaSelectionRectangle right)
            {
                int yCompare = right.MaxY.CompareTo(left.MaxY);

                if (yCompare != 0)
                {
                    return yCompare;
                }

                return left.MinX.CompareTo(right.MinX);
            });

            return remainingRectangles;
        }

        private bool HasMeaningfulHorizontalOverlap(OviaSelectionRectangle left, OviaSelectionRectangle right)
        {
            double overlapWidth = Math.Min(left.MaxX, right.MaxX) - Math.Max(left.MinX, right.MinX);

            if (overlapWidth <= OviaBoxOverlapTolerance)
            {
                return false;
            }

            double referenceWidth = Math.Min(left.Width, right.Width);

            if (referenceWidth <= OviaBoxOverlapTolerance)
            {
                return false;
            }

            return overlapWidth / referenceWidth >= 0.5;
        }

        private List<OviaSelectionRectangle> GetExistingOviaSelectionRectangles(Database db)
        {
            List<OviaSelectionRectangle> result = new List<OviaSelectionRectangle>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                {
                    return result;
                }

                BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                if (modelSpace == null)
                {
                    return result;
                }

                foreach (ObjectId objectId in modelSpace)
                {
                    Entity entity = null;

                    try
                    {
                        entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    }
                    catch
                    {
                        continue;
                    }

                    if (entity == null || !string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    OviaSelectionRectangle rectangle = TryGetOviaSelectionRectangle(entity);

                    if (rectangle == null)
                    {
                        continue;
                    }

                    if (rectangle.Width <= OviaBoxOverlapTolerance || rectangle.Height <= OviaBoxOverlapTolerance)
                    {
                        continue;
                    }

                    result.Add(rectangle);
                }

                tr.Commit();
            }

            return result;
        }

        private OviaSelectionRectangle TryGetOviaSelectionRectangle(Entity entity)
        {
            Polyline polyline = entity as Polyline;

            if (polyline != null && polyline.NumberOfVertices > 0)
            {
                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                double maxY = double.MinValue;
                int vertexIndex;

                for (vertexIndex = 0; vertexIndex < polyline.NumberOfVertices; vertexIndex++)
                {
                    Point2d point = polyline.GetPoint2dAt(vertexIndex);
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }

                return new OviaSelectionRectangle(minX, maxX, minY, maxY);
            }

            try
            {
                Extents3d extents = entity.GeometricExtents;

                return new OviaSelectionRectangle(
                    extents.MinPoint.X,
                    extents.MaxPoint.X,
                    extents.MinPoint.Y,
                    extents.MaxPoint.Y
                );
            }
            catch
            {
                return null;
            }
        }

        private ObjectId CreateOviaBoxEntity(Database db, Transaction tr, Point3d point1, Point3d point2, ObjectId dashedLineTypeId)
        {
            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

            if (blockTable == null)
            {
                return ObjectId.Null;
            }

            BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

            if (modelSpace == null)
            {
                return ObjectId.Null;
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

            ObjectId boxId = modelSpace.AppendEntity(box);
            tr.AddNewlyCreatedDBObject(box, true);

            return boxId;
        }

        private void DeleteOviaBoxEntitiesById(Database db, List<ObjectId> boxIds)
        {
            if (db == null || boxIds == null || boxIds.Count == 0)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int index;

                for (index = 0; index < boxIds.Count; index++)
                {
                    ObjectId boxId = boxIds[index];

                    if (boxId.IsNull)
                    {
                        continue;
                    }

                    try
                    {
                        Entity entity = tr.GetObject(boxId, OpenMode.ForWrite, false) as Entity;

                        if (entity != null)
                        {
                            entity.Erase();
                        }
                    }
                    catch
                    {
                    }
                }

                tr.Commit();
            }
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

            int i;

            if (verticalXs.Count < 3 || horizontalYs.Count < 3)
            {
                diagnostic = "표 경계선 부족: 세로선 " + verticalXs.Count.ToString() + "개, 가로선 " + horizontalYs.Count.ToString() + "개";
                return result;
            }

            bool reusedCachedGridSchema = TryApplyCachedGridSchema(minPoint, maxPoint, ref verticalXs);
            string[,] cellTexts = BuildGridCellTextMatrix(textRows, verticalXs, horizontalYs, mergeTolerance);

            if (cellTexts == null)
            {
                diagnostic = "셀 텍스트 매트릭스를 만들지 못했습니다.";
                return result;
            }

            /*
             * OVIA 2026-05-27 보정:
             * 철근형상 셀 내부의 짧은 수직선/꺾임선이 표 세로선으로 오인되면
             * 6컬럼 표가 7~9컬럼처럼 쪼개지고, 길이/수량/중량이 밀립니다.
             * 헤더 행이 확인되는 경우, 헤더가 비어 있는 내부 분할 컬럼은 실제 표 컬럼이
             * 아니므로 먼저 제거한 뒤 다시 셀 매트릭스를 구성합니다.
             */
            if (!reusedCachedGridSchema)
            {
                NormalizeGridColumnsByHeader(textRows, ref verticalXs, horizontalYs, mergeTolerance);
            }

            cellTexts = BuildGridCellTextMatrix(textRows, verticalXs, horizontalYs, mergeTolerance);

            if (cellTexts == null)
            {
                diagnostic = "셀 텍스트 매트릭스를 만들지 못했습니다.";
                return result;
            }

            int headerRowIndex = DetectGridHeaderRow(cellTexts, verticalXs, horizontalYs);
            int rowCount = horizontalYs.Count - 1;
            int colCount = verticalXs.Count - 1;

            List<OviaHeaderColumn> columns = null;

            if (reusedCachedGridSchema)
            {
                columns = GetCachedGridSchemaColumns();
                headerRowIndex = -1;
                diagnostic = AppendDiagnostic(diagnostic, "이전 정상 추출의 표 컬럼 스키마를 재사용했습니다.");
            }
            else if (headerRowIndex >= 0)
            {
                columns = BuildGridHeaderColumns(cellTexts, verticalXs, headerRowIndex);
            }

            if (columns == null || columns.Count < 3)
            {
                /*
                 * 사용자는 헤더까지 매번 선택하지 않고 필요한 데이터 행 구간만 선택합니다.
                 * 캐시된 동일 표 스키마가 없을 때만 데이터 패턴 기반 fallback을 사용합니다.
                 */
                columns = CreateGridFallbackHeaderColumnsFromData(cellTexts, colCount);
                headerRowIndex = -1;
            }

            if (columns == null || columns.Count < 3)
            {
                diagnostic = "표준 컬럼으로 매핑 가능한 헤더가 부족합니다.";
                return result;
            }

            ApplyGridHeaderColumnBoundsFromLines(columns, verticalXs);
            RestoreGridShapePhysicalBounds(columns);

            /*
             * OVIA 2026-05-27 보정:
             * 표 선 검출에 철근형상 내부 선/치수선이 섞이면 일부 행에서
             * 철근형상 셀의 치수값이 길이/수량/총길이/중량 칸으로 들어갈 수 있습니다.
             * 헤더 문자의 실제 X 위치를 기준으로 안전한 컬럼 범위를 다시 보정해서,
             * 형상 칸의 값은 형상 전용 데이터로만 사용되도록 합니다.
             */
            if (!reusedCachedGridSchema)
            {
                ApplyTextHeaderColumnBoundsIfAvailable(textRows, columns);
                RestoreGridShapePhysicalBounds(columns);
                CacheGridSchemaIfUsable(minPoint, maxPoint, verticalXs, columns);
            }

            lastDetectedHeaderColumns = CloneHeaderColumns(columns);

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

                /*
                 * 형상 셀 안의 치수 문자(120, 190 등)는 오직 CAD 형상 렌더링용입니다.
                 * 이 값들이 길이/수량/총길이/중량으로 들어가면 안 되므로,
                 * 헤더 기준 실제 데이터 컬럼에서 다시 한 번 값을 복구/보정합니다.
                 */
                ApplyGridShapeCellBoundsByHeaderColumn(row, columns, rowTopY, rowBottomY);
                RecoverGridRowValuesByHeaderBounds(textRows, row, columns, rowTopY, rowBottomY, mergeTolerance);

                /*
                 * OVIA 2026-05-27 재보정:
                 * 철근형상 내부 숫자는 데이터로 쓰지 않되, 규격(SHD10 등) 오른쪽에 있는 실제 산정값은
                 * 반드시 복구해야 합니다. 셀/선 기반 범위가 조금 어긋나도 같은 행의 원문을 X순서로 모아
                 * 규격 뒤 마지막 숫자들을 길이/수량/총길이/중량으로 재확인합니다.
                 */
                if (row.RowType == "DATA")
                {
                    string rowBandText = JoinGridRowBandTextInSelectedRange(textRows, rowTopY, rowBottomY, selectedMinPoint.X, selectedMaxPoint.X, mergeTolerance);

                    if (rowBandText != "")
                    {
                        row.RawText = rowBandText;
                        SupplementGridDataFromSpecAnchoredText(rowBandText, row, columns);
                        ApplyGridWeightAndNoteCorrection(textRows, row, columns, rowTopY, rowBottomY, mergeTolerance);
                    }
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

                if (row.RowType == "DATA" && headerRowIndex < 0)
                {
                    /*
                     * 형상 셀 내부 치수값은 데이터 컬럼 값으로 쓰면 안 됩니다.
                     * 표 선 기반 파서에서는 이미 셀/헤더 범위로 값을 복구했으므로,
                     * 형상 컬럼이 확인된 행에서는 rawText 전체 숫자를 다시 훑어 보정하지 않습니다.
                     */
                    if (!row.HasShapeCellBounds())
                    {
                        SupplementStandardDataFromRawText(rawText, row);
                    }
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

        private void NormalizeGridColumnsByHeader(List<OviaTextRow> textRows, ref List<double> verticalXs, List<double> horizontalYs, double mergeTolerance)
        {
            if (textRows == null || verticalXs == null || horizontalYs == null)
            {
                return;
            }

            if (verticalXs.Count < 4 || horizontalYs.Count < 2)
            {
                return;
            }

            int pass;

            for (pass = 0; pass < 6; pass++)
            {
                string[,] matrix = BuildGridCellTextMatrix(textRows, verticalXs, horizontalYs, mergeTolerance);

                if (matrix == null)
                {
                    return;
                }

                int headerRowIndex = DetectGridHeaderRow(matrix, verticalXs, horizontalYs);

                if (headerRowIndex < 0)
                {
                    return;
                }

                int colCount = verticalXs.Count - 1;
                int removeIndex = -1;
                int boundary;

                for (boundary = 1; boundary < colCount; boundary++)
                {
                    string leftTitle = CleanHeaderText(matrix[headerRowIndex, boundary - 1]);
                    string rightTitle = CleanHeaderText(matrix[headerRowIndex, boundary]);
                    bool leftHeader = IsKnownGridHeaderTitle(leftTitle);
                    bool rightHeader = IsKnownGridHeaderTitle(rightTitle);
                    bool leftBlank = leftTitle.Trim() == "";
                    bool rightBlank = rightTitle.Trim() == "";

                    /*
                     * 실제 표 컬럼이면 양쪽 모두 헤더가 있거나, 최소한 다음/이전 실제 헤더와
                     * 독립된 의미를 갖습니다. 철근형상 내부 수직선이 컬럼 경계로 섞이면
                     * 헤더 행에서는 한쪽이 빈칸으로 남는 경우가 많으므로 이 경계를 제거합니다.
                     */
                    if ((leftHeader && rightBlank) || (leftBlank && rightHeader))
                    {
                        removeIndex = boundary;
                        break;
                    }
                }

                if (removeIndex <= 0 || removeIndex >= verticalXs.Count - 1)
                {
                    return;
                }

                verticalXs.RemoveAt(removeIndex);
            }
        }

        private bool IsKnownGridHeaderTitle(string title)
        {
            if (title == null || title.Trim() == "")
            {
                return false;
            }

            string key = ClassifyGridHeaderTitle(title, false);

            return key != null && key.Trim() != "";
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

        private void RestoreGridShapePhysicalBounds(List<OviaHeaderColumn> columns)
        {
            if (columns == null || columns.Count == 0)
            {
                return;
            }

            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");

            if (shapeColumn == null)
            {
                return;
            }

            double left = shapeColumn.LeftX;
            double right = shapeColumn.RightX;
            double shapeCenter = shapeColumn.X;
            double bestLeft = Double.MinValue;
            double bestRight = Double.MaxValue;
            int i;

            /*
             * 반복되는 철근 세로선이 표 세로 경계 후보로 잡히면 실제 형상 셀이 둘 이상으로
             * 쪼개질 수 있습니다. 진짜 형상 범위는 왼쪽의 형번(또는 규격) 경계부터
             * 오른쪽의 길이 컬럼 경계까지입니다.
             */
            for (i = 0; i < columns.Count; i++)
            {
                OviaHeaderColumn item = columns[i];

                if (item == null || item == shapeColumn || item.RightX <= item.LeftX)
                {
                    continue;
                }

                bool leftAnchor = item.StandardKey == "IGNORE_SHAPE_NO"
                    || item.StandardKey == "SHAPE_NO"
                    || item.StandardKey == "SPEC";

                if (leftAnchor && item.RightX <= shapeCenter + 0.0001 && item.RightX > bestLeft)
                {
                    bestLeft = item.RightX;
                }

                bool rightAnchor = item.StandardKey == "LENGTH_MM"
                    || item.StandardKey == "QUANTITY_EA"
                    || item.StandardKey == "TOTAL_LENGTH_M"
                    || item.StandardKey == "TOTAL_WEIGHT"
                    || item.StandardKey == "TOTAL_WEIGHT_KG"
                    || item.StandardKey == "NOTE";

                if (rightAnchor && item.LeftX >= shapeCenter - 0.0001 && item.LeftX < bestRight)
                {
                    bestRight = item.LeftX;
                }
            }

            if (bestLeft != Double.MinValue)
            {
                left = bestLeft;
            }

            if (bestRight != Double.MaxValue)
            {
                right = bestRight;
            }

            if (right <= left)
            {
                return;
            }

            shapeColumn.LeftX = left;
            shapeColumn.RightX = right;
            shapeColumn.X = (left + right) / 2.0;
        }

        private void ApplyTextHeaderColumnBoundsIfAvailable(List<OviaTextRow> textRows, List<OviaHeaderColumn> columns)
        {
            if (textRows == null || textRows.Count == 0 || columns == null || columns.Count == 0)
            {
                return;
            }

            OviaHeaderMap textHeaderMap = null;

            try
            {
                textHeaderMap = DetectHeaderMap(GroupRowsByY(textRows), textRows);
            }
            catch
            {
                textHeaderMap = null;
            }

            if (textHeaderMap == null || textHeaderMap.Columns == null || textHeaderMap.Columns.Count < 3)
            {
                return;
            }

            int i;

            for (i = 0; i < columns.Count; i++)
            {
                OviaHeaderColumn target = columns[i];

                if (target == null)
                {
                    continue;
                }

                OviaHeaderColumn source = FindMatchingHeaderColumnForBounds(textHeaderMap.Columns, target.StandardKey);

                if (source == null)
                {
                    continue;
                }

                if (source.RightX <= source.LeftX)
                {
                    continue;
                }

                /*
                 * 표 선/셀 파서가 이미 확인한 물리 컬럼 경계는 헤더 문자 중심값보다 우선합니다.
                 * 문자 중심 중간값으로 다시 덮어쓰면 형상 셀이 절반으로 잘리고 길이/수량이 밀립니다.
                 */
                bool hasPhysicalGridBounds = target.SourceColumnIndex >= 0 && target.RightX > target.LeftX;

                if (hasPhysicalGridBounds)
                {
                    continue;
                }

                target.LeftX = source.LeftX;
                target.RightX = source.RightX;
                target.X = source.X;
            }

            /*
             * OVIA 2026-05-27 보정:
             * 표 선/셀 분할 과정에서 총길이(M) 같은 실제 헤더 컬럼이 누락되면
             * 이후 보정 로직이 3개 값(길이/수량/중량) 표로 오인하여
             * 길이 칸에 수량, 수량 칸에 총길이가 들어갑니다.
             *
             * 사용자가 BarList 항목 매핑을 만든 목적은 CAD 헤더명을 기준으로 OVIA 표준 컬럼에
             * 정확히 넣는 것이므로, 문자 헤더 분석에서 확인된 표준 컬럼은 기존 grid columns에
             * 없더라도 추가합니다. 이 추가 컬럼은 SourceColumnIndex가 없어도 LeftX/RightX 범위로
             * 실제 행 값을 다시 읽을 수 있습니다.
             */
            for (i = 0; i < textHeaderMap.Columns.Count; i++)
            {
                OviaHeaderColumn source = textHeaderMap.Columns[i];

                if (source == null || source.StandardKey == null || source.StandardKey.Trim() == "")
                {
                    continue;
                }

                if (!IsBarListDataHeaderKey(source.StandardKey))
                {
                    continue;
                }

                if (FindMatchingHeaderColumnForBounds(columns, source.StandardKey) != null)
                {
                    continue;
                }

                if (source.RightX <= source.LeftX)
                {
                    continue;
                }

                OviaHeaderColumn added = new OviaHeaderColumn();
                added.StandardKey = source.StandardKey;
                added.OriginalTitle = source.OriginalTitle;
                added.X = source.X;
                added.LeftX = source.LeftX;
                added.RightX = source.RightX;
                added.SourceColumnIndex = -1;
                columns.Add(added);
            }

            columns.Sort(delegate (OviaHeaderColumn a, OviaHeaderColumn b)
            {
                return a.X.CompareTo(b.X);
            });
        }

        private bool IsBarListDataHeaderKey(string key)
        {
            if (key == null)
            {
                return false;
            }

            return key == "MARK_NO" ||
                   key == "PART" ||
                   key == "SPEC" ||
                   key == "SHAPE" ||
                   key == "LENGTH_MM" ||
                   key == "TOTAL_LENGTH_M" ||
                   key == "QUANTITY_EA" ||
                   key == "TOTAL_WEIGHT" ||
                   key == "TOTAL_WEIGHT_KG" ||
                   key == "NOTE";
        }

        private OviaHeaderColumn FindMatchingHeaderColumnForBounds(List<OviaHeaderColumn> columns, string key)
        {
            if (columns == null || key == null)
            {
                return null;
            }

            OviaHeaderColumn exact = FindHeaderColumnByKey(columns, key);

            if (exact != null)
            {
                return exact;
            }

            return null;
        }

        private void ApplyGridShapeCellBoundsByHeaderColumn(OviaBarTableRow row, List<OviaHeaderColumn> columns, double rowTopY, double rowBottomY)
        {
            if (row == null || columns == null)
            {
                return;
            }

            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");

            if (shapeColumn == null)
            {
                return;
            }

            if (shapeColumn.RightX <= shapeColumn.LeftX)
            {
                return;
            }

            row.ShapeCellMinX = shapeColumn.LeftX;
            row.ShapeCellMaxX = shapeColumn.RightX;
            row.ShapeCellMinY = rowBottomY;
            row.ShapeCellMaxY = rowTopY;
        }

        private void RecoverGridRowValuesByHeaderBounds(List<OviaTextRow> textRows, OviaBarTableRow row, List<OviaHeaderColumn> columns, double rowTopY, double rowBottomY, double tolerance)
        {
            if (textRows == null || row == null || columns == null)
            {
                return;
            }

            if (row.RowType != "DATA")
            {
                return;
            }

            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");

            string markText = GetGridColumnTextByHeaderBounds(textRows, columns, "MARK_NO", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string partText = GetGridColumnTextByHeaderBounds(textRows, columns, "PART", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string specText = GetGridColumnTextByHeaderBounds(textRows, columns, "SPEC", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string shapeText = GetGridColumnTextByHeaderBounds(textRows, columns, "SHAPE", rowTopY, rowBottomY, tolerance, shapeColumn, true);
            string lengthText = GetGridColumnTextByHeaderBounds(textRows, columns, "LENGTH_MM", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string qtyText = GetGridColumnTextByHeaderBounds(textRows, columns, "QUANTITY_EA", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string totalLengthText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_LENGTH_M", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            string totalWeightText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_WEIGHT", rowTopY, rowBottomY, tolerance, shapeColumn, false);

            if (totalWeightText == "")
            {
                totalWeightText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_WEIGHT_KG", rowTopY, rowBottomY, tolerance, shapeColumn, false);
            }

            string value;

            value = FirstSimpleNumber(markText);
            if (value != "")
            {
                row.MarkNo = value;
                row.BarNo = value;
            }

            if (partText != "")
            {
                row.Part = partText;
            }

            value = DetectSpec(specText);
            if (value != "")
            {
                row.Spec = value;
            }
            else if (specText != "")
            {
                row.Spec = specText;
            }

            if (shapeText != "")
            {
                row.ShapeText = shapeText;
                row.ShapeRawText = shapeText;
                row.ShapeDimensionText = BuildShapeDimensionAssignments(shapeText, "");
            }

            value = PickGridNumericValue(lengthText, "LENGTH_MM");
            if (value != "")
            {
                row.Length = FormatLengthMmText(value);
            }

            value = PickGridNumericValue(qtyText, "QUANTITY_EA");
            if (value != "")
            {
                row.Qty = value;
            }

            if (FindMatchingHeaderColumnForBounds(columns, "TOTAL_LENGTH_M") != null)
            {
                value = PickGridNumericValue(totalLengthText, "TOTAL_LENGTH_M");
                if (value != "")
                {
                    row.TotalLength = value;
                }
            }
            else
            {
                row.TotalLength = "";
            }

            OviaHeaderColumn kgColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG");

            if (kgColumn != null)
            {
                string kgWeightText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_WEIGHT_KG", rowTopY, rowBottomY, tolerance, shapeColumn, false);
                value = PickGridNumericValue(kgWeightText, "TOTAL_WEIGHT_KG");

                if (value != "")
                {
                    row.TotalWeight = ConvertKgTextToTonText(value);
                    if (!HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
                    {
                        row.Note = "";
                    }
                    return;
                }
            }

            value = PickGridNumericValue(totalWeightText, "TOTAL_WEIGHT");
            if (value != "")
            {
                if (IsKgWeightColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
                {
                    row.TotalWeight = ConvertKgTextToTonText(value);
                }
                else
                {
                    row.TotalWeight = value;
                }
            }

            if (!HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
            {
                row.Note = "";
            }
        }

        private string GetGridColumnTextByHeaderBounds(List<OviaTextRow> textRows, List<OviaHeaderColumn> columns, string key, double rowTopY, double rowBottomY, double tolerance, OviaHeaderColumn shapeColumn, bool targetIsShape)
        {
            if (textRows == null || columns == null || key == null)
            {
                return "";
            }

            OviaHeaderColumn column = FindMatchingHeaderColumnForBounds(columns, key);

            if (column == null || column.RightX <= column.LeftX)
            {
                return "";
            }

            List<OviaTextRow> candidates = new List<OviaTextRow>();
            double yMargin = Math.Max(tolerance * 2.5, 0.5);
            double xMargin = Math.Max(tolerance * 0.6, 0.15);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                if (text.Y < rowBottomY - yMargin || text.Y > rowTopY + yMargin)
                {
                    continue;
                }

                string value = CleanCellText(text.TextValue);

                if (value == "")
                {
                    continue;
                }

                if (IsHeaderRow(value) || IsSummaryText(value))
                {
                    continue;
                }

                if (!IsXInsideHeaderColumn(text.X, column, xMargin))
                {
                    continue;
                }

                if (!targetIsShape && IsXInsideHeaderColumn(text.X, shapeColumn, Math.Max(tolerance, 0.5)))
                {
                    continue;
                }

                candidates.Add(text);
            }

            if (candidates.Count == 0)
            {
                return "";
            }

            candidates.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                double yDiff = Math.Abs(a.Y - b.Y);

                if (yDiff > Math.Max(tolerance, 0.2))
                {
                    return b.Y.CompareTo(a.Y);
                }

                return a.X.CompareTo(b.X);
            });

            StringBuilder sb = new StringBuilder();

            for (i = 0; i < candidates.Count; i++)
            {
                string value = CleanCellText(candidates[i].TextValue);

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

        private void ApplyGridWeightAndNoteCorrection(List<OviaTextRow> textRows, OviaBarTableRow row, List<OviaHeaderColumn> columns, double rowTopY, double rowBottomY, double tolerance)
        {
            if (row == null || columns == null)
            {
                return;
            }

            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");
            OviaHeaderColumn kgColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG");
            string value = "";

            if (kgColumn != null)
            {
                string kgWeightText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_WEIGHT_KG", rowTopY, rowBottomY, tolerance, shapeColumn, false);
                value = PickGridNumericValue(kgWeightText, "TOTAL_WEIGHT_KG");

                if (value != "")
                {
                    row.TotalWeight = ConvertKgTextToTonText(value);
                }
            }
            else if (IsKgWeightColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
            {
                string weightText = GetGridColumnTextByHeaderBounds(textRows, columns, "TOTAL_WEIGHT", rowTopY, rowBottomY, tolerance, shapeColumn, false);
                value = PickGridNumericValue(weightText, "TOTAL_WEIGHT");

                if (value != "")
                {
                    row.TotalWeight = ConvertKgTextToTonText(value);
                }
            }

            if (!HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
            {
                row.Note = "";
            }
        }

        private bool IsKgWeightColumn(List<OviaTextRow> textRows, List<OviaHeaderColumn> columns, double rowTopY, double rowBottomY, double tolerance)
        {
            OviaHeaderColumn kgColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG");

            if (kgColumn != null)
            {
                return true;
            }

            OviaHeaderColumn weightColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT");

            if (weightColumn == null)
            {
                return false;
            }

            if (HeaderTitleMeansKg(weightColumn.OriginalTitle))
            {
                return true;
            }

            if (HeaderTitleMeansTon(weightColumn.OriginalTitle))
            {
                return false;
            }

            if (HasKgTextNearColumn(textRows, weightColumn, rowTopY, rowBottomY, tolerance))
            {
                return true;
            }

            return false;
        }

        private bool HasUsableNoteColumn(List<OviaTextRow> textRows, List<OviaHeaderColumn> columns, double rowTopY, double rowBottomY, double tolerance)
        {
            OviaHeaderColumn noteColumn = FindMatchingHeaderColumnForBounds(columns, "NOTE");

            if (noteColumn == null)
            {
                return false;
            }

            if (!HeaderTitleMeansNote(noteColumn.OriginalTitle))
            {
                return false;
            }

            if (!HasNoteTextNearColumn(textRows, noteColumn, rowTopY, rowBottomY, tolerance))
            {
                return false;
            }

            OviaHeaderColumn weightColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT");
            OviaHeaderColumn kgColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG");

            if (ColumnsOverlap(noteColumn, weightColumn, 0.65) || ColumnsOverlap(noteColumn, kgColumn, 0.65))
            {
                return false;
            }

            return true;
        }

        private bool HasNoteTextNearColumn(List<OviaTextRow> textRows, OviaHeaderColumn column, double rowTopY, double rowBottomY, double tolerance)
        {
            if (textRows == null || column == null)
            {
                return false;
            }

            double rowHeight = Math.Abs(rowTopY - rowBottomY);
            double xMargin = Math.Max((column.RightX - column.LeftX) * 0.35, Math.Max(tolerance * 2.0, 0.5));
            double yTop = rowTopY + Math.Max(rowHeight * 4.0, Math.Max(tolerance * 25.0, 8.0));
            double yBottom = rowBottomY;
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                if (text.X < column.LeftX - xMargin || text.X > column.RightX + xMargin)
                {
                    continue;
                }

                if (text.Y < yBottom || text.Y > yTop)
                {
                    continue;
                }

                if (HeaderTitleMeansNote(text.TextValue))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasKgTextNearColumn(List<OviaTextRow> textRows, OviaHeaderColumn column, double rowTopY, double rowBottomY, double tolerance)
        {
            if (textRows == null || column == null)
            {
                return false;
            }

            double rowHeight = Math.Abs(rowTopY - rowBottomY);
            double xMargin = Math.Max((column.RightX - column.LeftX) * 0.35, Math.Max(tolerance * 2.0, 0.5));
            double yTop = rowTopY + Math.Max(rowHeight * 4.0, Math.Max(tolerance * 25.0, 8.0));
            double yBottom = rowBottomY;
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                if (text.X < column.LeftX - xMargin || text.X > column.RightX + xMargin)
                {
                    continue;
                }

                if (text.Y < yBottom || text.Y > yTop)
                {
                    continue;
                }

                string value = NormalizeGridHeaderText(text.TextValue);

                if (value.IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HeaderTitleMeansKg(string title)
        {
            string value = NormalizeGridHeaderText(title);

            return value.IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HeaderTitleMeansTon(string title)
        {
            string value = NormalizeGridHeaderText(title);

            return value.IndexOf("TON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("톤", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HeaderTitleMeansNote(string title)
        {
            string value = NormalizeGridHeaderText(title);

            return value.IndexOf("비고", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("NOTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("REMARK", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ColumnsOverlap(OviaHeaderColumn a, OviaHeaderColumn b, double threshold)
        {
            if (a == null || b == null)
            {
                return false;
            }

            double left = Math.Max(a.LeftX, b.LeftX);
            double right = Math.Min(a.RightX, b.RightX);
            double overlap = right - left;

            if (overlap <= 0)
            {
                return false;
            }

            double width = Math.Min(Math.Abs(a.RightX - a.LeftX), Math.Abs(b.RightX - b.LeftX));

            if (width <= 0.0001)
            {
                return false;
            }

            return overlap / width >= threshold;
        }

        private bool IsXInsideHeaderColumn(double x, OviaHeaderColumn column, double margin)
        {
            if (column == null)
            {
                return false;
            }

            return x >= column.LeftX - margin && x <= column.RightX + margin;
        }

        private string PickGridNumericValue(string text, string key)
        {
            text = CleanCellText(text);

            if (text == "")
            {
                return "";
            }

            List<string> numbers = ExtractNumericTokensPreserveThousands(text);

            if (numbers.Count == 0)
            {
                return "";
            }

            if (key == "QUANTITY_EA")
            {
                int i;

                for (i = 0; i < numbers.Count; i++)
                {
                    if (Regex.IsMatch(numbers[i], @"^-?\d+$"))
                    {
                        return numbers[i];
                    }
                }
            }

            if (key == "LENGTH_MM" || key == "TOTAL_WEIGHT" || key == "TOTAL_WEIGHT_KG")
            {
                string joined = PickJoinedThousandsCandidate(numbers);

                if (joined != "")
                {
                    return joined;
                }
            }

            return numbers[0];
        }

        private List<string> ExtractNumericTokensPreserveThousands(string text)
        {
            List<string> result = new List<string>();

            if (text == null)
            {
                return result;
            }

            text = CleanCellText(text);

            if (text == "")
            {
                return result;
            }

            MatchCollection matches = Regex.Matches(text, @"-?\d+(?:,\d{3})*(?:\.\d+)?|-?\d+(?:\.\d+)?");
            int i;

            for (i = 0; i < matches.Count; i++)
            {
                string value = NormalizeNumericToken(matches[i].Value);

                if (value != "")
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private string PickJoinedThousandsCandidate(List<string> numbers)
        {
            if (numbers == null || numbers.Count < 2)
            {
                return "";
            }

            int i;

            for (i = numbers.Count - 2; i >= 0; i--)
            {
                string left = numbers[i] == null ? "" : numbers[i].Trim();
                string right = numbers[i + 1] == null ? "" : numbers[i + 1].Trim();

                if (Regex.IsMatch(left, @"^-?\d{1,3}$") && Regex.IsMatch(right, @"^\d{3}$"))
                {
                    return left + right;
                }
            }

            return "";
        }

        private string NormalizeNumericToken(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Trim().Replace(",", "");

            return value;
        }

        private string FormatLengthMmText(string value)
        {
            value = NormalizeNumericToken(value);

            if (value == "")
            {
                return "";
            }

            decimal number;

            if (!Decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                if (!Decimal.TryParse(value, out number))
                {
                    return value;
                }
            }

            if (number == Decimal.Truncate(number))
            {
                return number.ToString("#,0", CultureInfo.InvariantCulture);
            }

            return number.ToString("#,0.###", CultureInfo.InvariantCulture);
        }

        private string NormalizeThousandsSeparators(string text)
        {
            if (text == null)
            {
                return "";
            }

            return Regex.Replace(text, @"(?<=\d),(?=\d{3}(\D|$))", "");
        }


        private string JoinGridRowBandTextInSelectedRange(List<OviaTextRow> textRows, double rowTopY, double rowBottomY, double selectedX1, double selectedX2, double tolerance)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return "";
            }

            double leftX = Math.Min(selectedX1, selectedX2);
            double rightX = Math.Max(selectedX1, selectedX2);
            double width = Math.Abs(rightX - leftX);

            if (width <= 0.0001)
            {
                width = 1.0;
            }

            double xMargin = Math.Max(width * 0.035, Math.Max(tolerance * 2.0, 1.0));
            double yMargin = Math.Max(tolerance * 2.5, 0.5);
            List<OviaTextRow> candidates = new List<OviaTextRow>();
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                if (text.Y < rowBottomY - yMargin || text.Y > rowTopY + yMargin)
                {
                    continue;
                }

                if (text.X < leftX - xMargin || text.X > rightX + xMargin)
                {
                    continue;
                }

                string value = CleanCellText(text.TextValue);

                if (value == "")
                {
                    continue;
                }

                if (IsHeaderRow(value) || IsSummaryText(value))
                {
                    continue;
                }

                candidates.Add(text);
            }

            if (candidates.Count == 0)
            {
                return "";
            }

            candidates.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                return a.X.CompareTo(b.X);
            });

            StringBuilder sb = new StringBuilder();

            for (i = 0; i < candidates.Count; i++)
            {
                string value = CleanCellText(candidates[i].TextValue);

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

        private void SupplementGridDataFromSpecAnchoredText(string rawText, OviaBarTableRow row, List<OviaHeaderColumn> columns)
        {
            if (row == null || rawText == null)
            {
                return;
            }

            if (row.RowType != "DATA")
            {
                return;
            }

            string text = CleanCellText(rawText);

            if (text == "")
            {
                return;
            }

            string[] parts = text.Split(new char[] { ' ', '\t', ';', '|', '/', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts == null || parts.Length == 0)
            {
                return;
            }

            int specIndex = -1;
            string detectedSpec = "";
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                detectedSpec = DetectSpec(parts[i]);

                if (detectedSpec != "")
                {
                    specIndex = i;
                    break;
                }
            }

            if (specIndex < 0)
            {
                return;
            }

            if (detectedSpec != "")
            {
                row.Spec = detectedSpec;
            }

            List<string> numbersAfterSpec = new List<string>();

            for (i = specIndex + 1; i < parts.Length; i++)
            {
                string token = parts[i];

                if (DetectSpec(token) != "")
                {
                    continue;
                }

                MatchCollection matches = Regex.Matches(token, @"-?\d+(?:,\d{3})*(?:\.\d+)?|-?\d+(?:\.\d+)?");
                int j;

                for (j = 0; j < matches.Count; j++)
                {
                    numbersAfterSpec.Add(NormalizeNumericToken(matches[j].Value));
                }
            }

            if (numbersAfterSpec.Count < 3)
            {
                return;
            }

            bool hasTotalLengthColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_LENGTH_M") != null;
            bool hasKgWeightColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG") != null && FindHeaderColumnByKey(columns, "TOTAL_WEIGHT") == null;

            string lengthValue = "";
            string qtyValue = "";
            string totalLengthValue = "";
            string totalWeightValue = "";
            bool pickedWithTotalLength = false;

            /*
             * OVIA 2026-05-27 핵심 보정:
             * BarList 항목 매핑의 목적은 숫자의 순서가 아니라 CAD 표 헤더 기준으로
             * OVIA 표준 컬럼에 넣는 것입니다.
             *
             * 따라서 총길이(M) 헤더가 실제로 확인된 도면에서만
             *   길이 / 수량 / 총길이 / 중량
             * 4개 패턴을 허용합니다.
             *
             * 총길이(M) 헤더가 없는 SSBAR 계열 도면은
             *   길이 / 수량 / 중량
             * 3개 패턴만 허용하며, 이때 총길이(M)는 반드시 빈칸으로 유지합니다.
             *
             * 형상/형태 셀 내부 치수값은 계속 형상 전용으로만 사용합니다.
             */
            if (hasTotalLengthColumn)
            {
                if (!TryPickSpecAnchoredBarValues(numbersAfterSpec, true, out lengthValue, out qtyValue, out totalLengthValue, out totalWeightValue))
                {
                    return;
                }

                pickedWithTotalLength = totalLengthValue != "";
            }
            else
            {
                if (!TryPickSpecAnchoredBarValues(numbersAfterSpec, false, out lengthValue, out qtyValue, out totalLengthValue, out totalWeightValue))
                {
                    return;
                }

                pickedWithTotalLength = false;
                totalLengthValue = "";
            }

            if (lengthValue != "")
            {
                row.Length = FormatLengthMmText(lengthValue);
            }

            if (qtyValue != "")
            {
                row.Qty = qtyValue;
            }

            if (pickedWithTotalLength && totalLengthValue != "")
            {
                row.TotalLength = totalLengthValue;
            }
            else if (!hasTotalLengthColumn)
            {
                row.TotalLength = "";
            }

            if (totalWeightValue != "")
            {
                if (hasKgWeightColumn)
                {
                    row.TotalWeight = ConvertKgTextToTonText(totalWeightValue);
                }
                else
                {
                    row.TotalWeight = totalWeightValue;
                }
            }
        }

        private bool TryPickSpecAnchoredBarValues(List<string> numbers, bool preferTotalLength, out string lengthValue, out string qtyValue, out string totalLengthValue, out string totalWeightValue)
        {
            lengthValue = "";
            qtyValue = "";
            totalLengthValue = "";
            totalWeightValue = "";

            if (numbers == null || numbers.Count < 3)
            {
                return false;
            }

            int i;

            if (preferTotalLength && numbers.Count >= 4)
            {
                for (i = numbers.Count - 4; i >= 0; i--)
                {
                    string candidateLength = numbers[i];
                    string candidateQty = numbers[i + 1];
                    string candidateTotalLength = numbers[i + 2];
                    string candidateWeight = numbers[i + 3];

                    if (IsLikelyBarLengthValue(candidateLength) &&
                        IsLikelyBarQuantityValue(candidateQty) &&
                        IsLikelyBarTotalLengthValue(candidateTotalLength) &&
                        IsLikelyBarWeightValue(candidateWeight))
                    {
                        lengthValue = candidateLength;
                        qtyValue = candidateQty;
                        totalLengthValue = candidateTotalLength;
                        totalWeightValue = candidateWeight;
                        return true;
                    }
                }
            }

            for (i = numbers.Count - 3; i >= 0; i--)
            {
                string candidateLength = numbers[i];
                string candidateQty = numbers[i + 1];
                string candidateWeight = numbers[i + 2];

                if (IsLikelyBarLengthValue(candidateLength) &&
                    IsLikelyBarQuantityValue(candidateQty) &&
                    IsLikelyBarWeightValue(candidateWeight))
                {
                    lengthValue = candidateLength;
                    qtyValue = candidateQty;
                    totalLengthValue = "";
                    totalWeightValue = candidateWeight;
                    return true;
                }
            }

            return false;
        }

        private bool IsLikelyBarLengthValue(string value)
        {
            decimal number;

            if (!TryParseDecimalText(value, out number))
            {
                return false;
            }

            if (!Regex.IsMatch(value == null ? "" : value.Trim(), @"^-?\d+(\.0+)?$"))
            {
                return false;
            }

            return number >= 50 && number <= 100000;
        }

        private bool IsLikelyBarQuantityValue(string value)
        {
            decimal number;

            if (!TryParseDecimalText(value, out number))
            {
                return false;
            }

            if (!Regex.IsMatch(value == null ? "" : value.Trim(), @"^-?\d+$"))
            {
                return false;
            }

            return number >= 0 && number <= 100000;
        }

        private bool IsLikelyBarTotalLengthValue(string value)
        {
            decimal number;

            if (!TryParseDecimalText(value, out number))
            {
                return false;
            }

            return number >= 0 && number <= 10000000;
        }

        private bool IsLikelyBarWeightValue(string value)
        {
            decimal number;

            if (!TryParseDecimalText(value, out number))
            {
                return false;
            }

            return number >= 0 && number <= 100000;
        }

        private bool TryParseDecimalText(string value, out decimal number)
        {
            number = 0;

            if (value == null)
            {
                return false;
            }

            value = value.Trim().Replace(",", "");

            if (value == "")
            {
                return false;
            }

            if (Decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }

            return Decimal.TryParse(value, out number);
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

            /*
             * 헤더가 병합된 도면에서는 "형상" 제목이 형번/형상코드 칸 쪽에 놓이고,
             * 실제 도형은 그 오른쪽의 무제목 물리 셀에 들어가는 경우가 많습니다.
             * 헤더 문자열만 신뢰하면 70, 407 같은 형번이 철근형상으로 매핑됩니다.
             * 따라서 헤더 분류 뒤 실제 데이터 분포를 검증하여 형상 물리 컬럼을 교정합니다.
             */
            CorrectAmbiguousGridShapeColumn(columns, cellTexts, headerRowIndex, colCount, verticalXs);

            bool hasShapeColumn = false;
            int ignoredShapeNoColumn = -1;

            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].StandardKey == "SHAPE")
                {
                    hasShapeColumn = true;
                }
                else if (columns[i].StandardKey == "IGNORE_SHAPE_NO")
                {
                    ignoredShapeNoColumn = columns[i].SourceColumnIndex;
                }
            }

            // 형번/형상번호 컬럼은 업체별 임의 코드이므로 데이터로 사용하지 않습니다.
            // 해당 컬럼 바로 오른쪽이 실제 철근형상 셀인 도면이 많으므로, 형상 헤더를 못 찾은 경우에만
            // 다음 물리 컬럼을 CAD 벡터 형상 셀로 지정합니다. 특정 형번 값에는 의존하지 않습니다.
            if (!hasShapeColumn && ignoredShapeNoColumn >= 0 && ignoredShapeNoColumn + 1 < colCount)
            {
                int shapeSourceColumn = ignoredShapeNoColumn + 1;
                OviaHeaderColumn shapeColumn = FindGridHeaderColumnByColumnIndex(columns, shapeSourceColumn);

                if (shapeColumn == null)
                {
                    shapeColumn = new OviaHeaderColumn();
                    shapeColumn.StandardKey = "SHAPE";
                    shapeColumn.OriginalTitle = "철근형상";
                    shapeColumn.X = (verticalXs[shapeSourceColumn] + verticalXs[shapeSourceColumn + 1]) / 2.0;
                    shapeColumn.LeftX = Math.Min(verticalXs[shapeSourceColumn], verticalXs[shapeSourceColumn + 1]);
                    shapeColumn.RightX = Math.Max(verticalXs[shapeSourceColumn], verticalXs[shapeSourceColumn + 1]);
                    shapeColumn.SourceColumnIndex = shapeSourceColumn;
                    columns.Add(shapeColumn);
                }
                else
                {
                    shapeColumn.StandardKey = "SHAPE";
                    shapeColumn.OriginalTitle = "철근형상";
                }
            }

            return columns;
        }

        private void CorrectAmbiguousGridShapeColumn(List<OviaHeaderColumn> columns, string[,] cellTexts, int headerRowIndex, int colCount, List<double> verticalXs)
        {
            if (columns == null || cellTexts == null || verticalXs == null || verticalXs.Count < 2)
            {
                return;
            }

            OviaHeaderColumn currentShape = FindHeaderColumnByKey(columns, "SHAPE");

            if (currentShape == null || currentShape.SourceColumnIndex < 0)
            {
                return;
            }

            int currentIndex = currentShape.SourceColumnIndex;
            int nextDataIndex = colCount;

            for (int i = 0; i < columns.Count; i++)
            {
                OviaHeaderColumn item = columns[i];

                if (item == null || item.SourceColumnIndex <= currentIndex)
                {
                    continue;
                }

                if (item.StandardKey == "LENGTH_MM" || item.StandardKey == "QUANTITY_EA" ||
                    item.StandardKey == "TOTAL_LENGTH_M" || item.StandardKey == "TOTAL_WEIGHT" ||
                    item.StandardKey == "TOTAL_WEIGHT_KG" || item.StandardKey == "NOTE")
                {
                    nextDataIndex = Math.Min(nextDataIndex, item.SourceColumnIndex);
                }
            }

            int candidateStart = currentIndex + 1;
            int candidateEnd = Math.Min(colCount - 1, nextDataIndex - 1);

            if (candidateStart > candidateEnd)
            {
                return;
            }

            double currentCodeRatio = GetGridShortCodeRatio(cellTexts, headerRowIndex, currentIndex);
            double currentRichness = GetGridShapeContentScore(cellTexts, headerRowIndex, currentIndex);
            int bestIndex = -1;
            double bestScore = currentRichness;

            for (int c = candidateStart; c <= candidateEnd; c++)
            {
                double score = GetGridShapeContentScore(cellTexts, headerRowIndex, c);

                if (score > bestScore + 0.75)
                {
                    bestScore = score;
                    bestIndex = c;
                }
            }

            /*
             * 현재 형상 칸의 대부분이 짧은 정수 코드이고, 오른쪽 후보 칸이 여러 치수/기호를
             * 포함하는 경우에만 이동합니다. 특정 코드값이나 특정 도면 번호에는 의존하지 않습니다.
             */
            if (bestIndex < 0 || currentCodeRatio < 0.60 || bestScore < 1.35)
            {
                return;
            }

            currentShape.StandardKey = "IGNORE_SHAPE_NO";
            currentShape.OriginalTitle = "";

            OviaHeaderColumn target = FindGridHeaderColumnByColumnIndex(columns, bestIndex);

            if (target == null)
            {
                target = new OviaHeaderColumn();
                target.SourceColumnIndex = bestIndex;
                target.X = (verticalXs[bestIndex] + verticalXs[bestIndex + 1]) / 2.0;
                target.LeftX = Math.Min(verticalXs[bestIndex], verticalXs[bestIndex + 1]);
                target.RightX = Math.Max(verticalXs[bestIndex], verticalXs[bestIndex + 1]);
                columns.Add(target);
            }

            target.StandardKey = "SHAPE";
            target.OriginalTitle = "철근형상";
        }

        private double GetGridShortCodeRatio(string[,] cellTexts, int headerRowIndex, int columnIndex)
        {
            int rowCount = cellTexts.GetLength(0);
            int sample = 0;
            int shortCodes = 0;
            int endRow = Math.Min(rowCount - 1, headerRowIndex + 30);

            for (int r = headerRowIndex + 1; r <= endRow; r++)
            {
                string value = CleanCellText(cellTexts[r, columnIndex]);

                if (value == "")
                {
                    continue;
                }

                sample++;
                string compact = Regex.Replace(value, @"\s+", "");

                if (Regex.IsMatch(compact, @"^[A-Za-z]?[0-9]{1,4}[A-Za-z]?$"))
                {
                    shortCodes++;
                }
            }

            return sample == 0 ? 0.0 : (double)shortCodes / sample;
        }

        private double GetGridShapeContentScore(string[,] cellTexts, int headerRowIndex, int columnIndex)
        {
            int rowCount = cellTexts.GetLength(0);
            int sampleRows = 0;
            double total = 0.0;
            int endRow = Math.Min(rowCount - 1, headerRowIndex + 30);

            for (int r = headerRowIndex + 1; r <= endRow; r++)
            {
                string value = CleanCellText(cellTexts[r, columnIndex]);

                if (value == "")
                {
                    continue;
                }

                sampleRows++;
                MatchCollection tokens = Regex.Matches(value, @"[A-Za-z]+|[-+]?\d+(?:[.,]\d+)?|[°Øø@Rr]+", RegexOptions.IgnoreCase);
                int numericCount = Regex.Matches(value, @"[-+]?\d+(?:[.,]\d+)?").Count;
                int lineCount = value.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                double rowScore = Math.Max(tokens.Count, numericCount) + Math.Max(0, lineCount - 1) * 0.75;

                if (value.IndexOf("°", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("@", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Ø", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rowScore += 1.0;
                }

                total += rowScore;
            }

            return sampleRows == 0 ? 0.0 : total / sampleRows;
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
                    return "IGNORE_SHAPE_NO";
                }

                if (hasShapeNoHeader)
                {
                    return "MARK_NO";
                }

                if (normalizedTitle.IndexOf("명칭", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedTitle.IndexOf("NAME", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "IGNORE_SHAPE_NO";
                }

                return "MARK_NO";
            }

            if (normalizedTitle.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("형상코드", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedTitle.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "IGNORE_SHAPE_NO";
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

            if (Regex.IsMatch(value, @"^(UHD|SHD|HD|SD|D)[0-9]{1,3}[A-Z]{0,4}$"))
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
            value = value.Replace(",", "");
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

            if (value.IndexOf("KG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT_KG";
            }

            if (value.IndexOf("총중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("중량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("톤", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WEIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TOTAL_WEIGHT";
            }

            if (value.IndexOf("총길이", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("총연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("연장", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("TOTALLENGTH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "TL")
            {
                return "TOTAL_LENGTH_M";
            }

            if (value.IndexOf("수량", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("본수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("개수", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("갯수", StringComparison.OrdinalIgnoreCase) >= 0 ||
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

            if (value.IndexOf("부위", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("위치", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("구간", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("ZONE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("LOCATION", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "PART";
            }


            if (value.IndexOf("철근규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("규격", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("강종", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SIZE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("DIA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SPEC";
            }

            if (value.IndexOf("형번", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상번호", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상코드", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPENO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SHAPECODE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "IGNORE_SHAPE_NO";
            }

            if (value.IndexOf("철근형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형상", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("형태", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                // OVIA 기본 매핑 기준: 번호 = 부호
                return "MARK_NO";
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

            if (standardKey == "SHAPE_NO" || standardKey == "IGNORE_SHAPE_NO")
            {
                return "";
            }

            if (standardKey == "SHAPE")
            {
                return "철근형상";
            }

            if (standardKey == "SPEC")
            {
                return "철근규격";
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

            if (standardKey == "TOTAL_WEIGHT_KG")
            {
                return "중량(Ton)";
            }

            if (standardKey == "TOTAL_WEIGHT")
            {
                return "중량(Ton)";
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

            string fileName = prefix + "_" + drawingName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv";

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
        public string Part = "";
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
        public bool HasBounds = false;
        public double BoundsMinX = 0;
        public double BoundsMinY = 0;
        public double BoundsMaxX = 0;
        public double BoundsMaxY = 0;
        public int ColorIndex = 256;
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
