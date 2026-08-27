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

        private sealed class OviaPendingSelection
        {
            public Point3d Point1;
            public Point3d Point2;
            public List<OviaSelectionRectangle> AllowedOutputRectangles = new List<OviaSelectionRectangle>();
            public List<ObjectId> BoxIds = new List<ObjectId>();
            public int OverlappedBoxCount;
            public int RedSelectionBoxCount;
            public int ExtractedRowCount;
            public string ExtractionError = "";
        }

        private sealed class OviaExtractionBatch
        {
            public string FilePath = "";
            public List<OviaBarTableRow> Rows = new List<OviaBarTableRow>();
        }

        private sealed class OviaResolvedCadColor
        {
            public bool IsValid;
            public int ColorIndex = 256;
            public int Red;
            public int Green;
            public int Blue;

            public OviaResolvedCadColor Clone()
            {
                OviaResolvedCadColor result = new OviaResolvedCadColor();
                result.IsValid = IsValid;
                result.ColorIndex = ColorIndex;
                result.Red = Red;
                result.Green = Green;
                result.Blue = Blue;
                return result;
            }
        }

        private sealed class OviaSelectionColorStats
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double AxisTolerance;
            public double MinimumAxisLength;
            public int AxisSegmentCount;
            public int YellowAxisSegmentCount;
            public int YellowHorizontalCount;
            public int YellowVerticalCount;
            public double TotalAxisLength;
            public double YellowAxisLength;
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
            List<OviaPendingSelection> pendingSelections = new List<OviaPendingSelection>();
            List<ObjectId> pendingBoxIds = new List<ObjectId>();
            List<OviaSelectionRectangle> currentBatchOutputRectangles = new List<OviaSelectionRectangle>();

            /*
             * OVIA 2026-07-30 _03 - 선택 즉시 독립 추출 / 최종 Enter 단일 게시
             * -----------------------------------------------------------------
             * 별도 Enter로는 정상인 표 A, 표 B를 한 세션에서 연속 선택했을 때 마지막 표만 남는
             * 문제를 구조적으로 차단합니다. 기존 구조는 좌표만 저장한 뒤 사용자가 다른 표로
             * Pan/Zoom한 최종 Enter 시점에 Editor.SelectCrossingWindow로 앞선 영역을 다시 읽었습니다.
             * AutoCAD의 현재 화면 밖으로 이동한 앞선 표는 선택 객체가 0개가 될 수 있어 마지막으로
             * 화면에 보이는 표만 남았습니다. 따라서 각 영역의 끝점을 지정하는 즉시, 그 영역이
             * 화면에 보이는 시점에 독립 분석하고 검증된 DATA 행과 형상 JSON 경로를 배치에 Append합니다.
             *
             * 최종 Enter는 이미 누적된 행의 내부 No를 정규화하고 CSV 1개와 ready 1개를
             * 원자적으로 게시하는 역할만 수행합니다. 뒤 선택은 앞 선택의 List를 초기화하거나
             * 번호를 키로 교체할 수 없습니다. 철근 번호 중복은 허용하며, DATA 중복은 현재
             * OVIABOX 세션 안에서 실제 선택 좌표가 겹치는 부분에만 적용합니다.
             */
            OviaExtractionBatch batch = new OviaExtractionBatch();
            batch.FilePath = CreateCsvFilePath(db, "OVIA_BoxTable");
            int successfulAreaCount = 0;
            int failedAreaCount = 0;

            try
            {
                while (true)
                {
                    PromptPointOptions firstPointOptions = new PromptPointOptions(
                        "\nOVIA 선택박스 시작점: 표 왼쪽 경계선과 시작 행의 위쪽 가로선 교차점을 클릭하세요. 모든 영역 선택 완료는 Enter, 취소는 Esc: "
                    );

                    firstPointOptions.AllowNone = true;

                    PromptPointResult firstPointResult = ed.GetPoint(firstPointOptions);

                    if (firstPointResult.Status == PromptStatus.None)
                    {
                        if (pendingSelections.Count == 0)
                        {
                            DeleteBatchExtractionArtifacts(batch.FilePath);
                            ed.WriteMessage("\nOVIA: 선택된 영역이 없어 CAD 영역 선택모드를 종료했습니다.\n");
                            return;
                        }

                        if (batch.Rows.Count == 0)
                        {
                            DeleteBatchExtractionArtifacts(batch.FilePath);
                            ed.WriteMessage("\nOVIA: 최종 Enter로 확정한 선택영역에서 전송 가능한 철근 DATA 행을 찾지 못했습니다. CSV와 ready는 생성하지 않았습니다.\n");
                            return;
                        }

                        try
                        {
                            int finalRowIndex;

                            for (finalRowIndex = 0; finalRowIndex < batch.Rows.Count; finalRowIndex++)
                            {
                                batch.Rows[finalRowIndex].No = finalRowIndex + 1;
                            }

                            WriteBarTableCsv(batch.FilePath, batch.Rows, doc);

                            ed.WriteMessage("\n");
                            ed.WriteMessage("====================================\n");
                            ed.WriteMessage("OVIA 다중 선택영역 통합 추출 완료\n");
                            ed.WriteMessage("------------------------------------\n");
                            ed.WriteMessage("선택 영역      : " + pendingSelections.Count.ToString(CultureInfo.InvariantCulture) + "개\n");
                            ed.WriteMessage("성공 영역      : " + successfulAreaCount.ToString(CultureInfo.InvariantCulture) + "개\n");
                            ed.WriteMessage("미추출 영역    : " + failedAreaCount.ToString(CultureInfo.InvariantCulture) + "개\n");
                            ed.WriteMessage("통합 DATA 행   : " + batch.Rows.Count.ToString(CultureInfo.InvariantCulture) + "개\n");
                            ed.WriteMessage("처리 방식      : 영역 선택 직후 독립 추출 + 최종 Enter 단일 게시\n");
                            ed.WriteMessage("번호 중복      : 허용, 선택 순서대로 모두 보존\n");
                            ed.WriteMessage("생성 CSV       : 1개\n");
                            ed.WriteMessage("생성 ready     : 1개\n");
                            ed.WriteMessage("저장 위치      : " + batch.FilePath + "\n");
                            WriteBarTablePreview(ed, batch.Rows);
                            ed.WriteMessage("====================================\n");
                        }
                        catch (System.Exception ex)
                        {
                            DeleteBatchExtractionArtifacts(batch.FilePath);
                            ed.WriteMessage("\nOVIA 통합 집계표 CSV 저장 오류: " + ex.Message + "\n");
                        }

                        return;
                    }

                    if (firstPointResult.Status != PromptStatus.OK)
                    {
                        DeleteOviaBoxEntitiesById(db, pendingBoxIds);
                        DeleteBatchExtractionArtifacts(batch.FilePath);
                        ed.Regen();
                        ed.WriteMessage("\nOVIA: 최종 Enter 전에 선택모드를 취소하여 이번 배치의 선택박스와 임시 추출 결과를 삭제했습니다. CSV와 ready는 생성하지 않았습니다.\n");
                        return;
                    }

                    PromptCornerOptions secondPointOptions = new PromptCornerOptions(
                        "\nOVIA 선택박스 끝점: 표 오른쪽 경계선과 끝 행의 아래쪽 가로선 교차점을 클릭하세요: ",
                        firstPointResult.Value
                    );

                    PromptPointResult secondPointResult = ed.GetCorner(secondPointOptions);

                    if (secondPointResult.Status != PromptStatus.OK)
                    {
                        DeleteOviaBoxEntitiesById(db, pendingBoxIds);
                        DeleteBatchExtractionArtifacts(batch.FilePath);
                        ed.Regen();
                        ed.WriteMessage("\nOVIA: 최종 Enter 전에 선택모드를 취소하여 이번 배치의 선택박스와 임시 추출 결과를 삭제했습니다. CSV와 ready는 생성하지 않았습니다.\n");
                        return;
                    }

                    Point3d boxPoint1 = firstPointResult.Value;
                    Point3d boxPoint2 = secondPointResult.Value;
                    int overlappedCurrentBatchCount;

                    List<OviaSelectionRectangle> availableOutputRectangles = BuildNonOverlappingOviaSelectionRectangles(
                        boxPoint1,
                        boxPoint2,
                        currentBatchOutputRectangles,
                        out overlappedCurrentBatchCount
                    );

                    if (availableOutputRectangles.Count == 0)
                    {
                        ed.WriteMessage("\nOVIA: 이번 배치 안에서 이미 선택한 좌표와 전부 겹쳐 중복 DATA 선택을 누적하지 않았습니다. 철근 번호가 같은 것은 중복 기준이 아닙니다.\n");
                        continue;
                    }

                    currentBatchOutputRectangles.AddRange(availableOutputRectangles);

                    List<OviaSelectionRectangle> displayRectangles = new List<OviaSelectionRectangle>();
                    int overlappedDisplayBoxCount = 0;
                    int outputRectangleIndex;

                    for (outputRectangleIndex = 0; outputRectangleIndex < availableOutputRectangles.Count; outputRectangleIndex++)
                    {
                        OviaSelectionRectangle outputRectangle = availableOutputRectangles[outputRectangleIndex];
                        int displayOverlapCount;
                        List<OviaSelectionRectangle> newDisplayRectangles = BuildNonOverlappingOviaSelectionRectangles(
                            db,
                            new Point3d(outputRectangle.MinX, outputRectangle.MinY, 0),
                            new Point3d(outputRectangle.MaxX, outputRectangle.MaxY, 0),
                            out displayOverlapCount
                        );

                        displayRectangles.AddRange(newDisplayRectangles);
                        overlappedDisplayBoxCount += displayOverlapCount;
                    }

                    OviaPendingSelection pendingSelection = new OviaPendingSelection();
                    pendingSelection.Point1 = boxPoint1;
                    pendingSelection.Point2 = boxPoint2;
                    pendingSelection.AllowedOutputRectangles.AddRange(availableOutputRectangles);
                    pendingSelection.OverlappedBoxCount = overlappedCurrentBatchCount;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId dashedLineTypeId = EnsureDashedLineType(db, tr);
                        EnsureOviaBoxLayer(db, tr, dashedLineTypeId, false);

                        int rectangleIndex;

                        for (rectangleIndex = 0; rectangleIndex < displayRectangles.Count; rectangleIndex++)
                        {
                            OviaSelectionRectangle rectangle = displayRectangles[rectangleIndex];
                            bool usedRedSelectionColor;
                            ObjectId pendingBoxId = CreateOviaBoxEntity(
                                db,
                                tr,
                                new Point3d(rectangle.MinX, rectangle.MinY, 0),
                                new Point3d(rectangle.MaxX, rectangle.MaxY, 0),
                                dashedLineTypeId,
                                out usedRedSelectionColor
                            );

                            if (!pendingBoxId.IsNull)
                            {
                                pendingSelection.BoxIds.Add(pendingBoxId);
                                pendingBoxIds.Add(pendingBoxId);

                                if (usedRedSelectionColor)
                                {
                                    pendingSelection.RedSelectionBoxCount++;
                                }
                            }
                        }

                        EnsureOviaBoxLayer(db, tr, dashedLineTypeId, true);
                        tr.Commit();
                    }

                    /*
                     * 가장 중요한 실행 순서:
                     * 다음 영역의 좌표를 받거나 사용자가 다른 위치로 Pan/Zoom하기 전에 현재 영역을
                     * 즉시 분석해 batch.Rows에 Append합니다. 뒤 영역의 화면 위치, 스키마, 선택박스,
                     * 번호, 파서 상태가 앞 영역 결과를 다시 계산하거나 교체할 수 없도록 앞 영역을
                     * 확정된 메모리 스냅샷으로 만듭니다.
                     */
                    int beforeRowCount = batch.Rows.Count;

                    try
                    {
                        RunSmartBoxTableExtraction(
                            "OVIABOX",
                            pendingSelection.Point1,
                            pendingSelection.Point2,
                            pendingSelection.Point1,
                            pendingSelection.Point2,
                            pendingSelection.AllowedOutputRectangles,
                            batch
                        );
                    }
                    catch (System.Exception areaException)
                    {
                        pendingSelection.ExtractionError = areaException.Message;
                    }

                    pendingSelection.ExtractedRowCount = batch.Rows.Count - beforeRowCount;

                    if (pendingSelection.ExtractedRowCount > 0)
                    {
                        successfulAreaCount++;
                    }
                    else
                    {
                        failedAreaCount++;
                    }

                    pendingSelections.Add(pendingSelection);
                    ed.Regen();

                    ed.WriteMessage("\n");
                    ed.WriteMessage("OVIA 선택영역 " + pendingSelections.Count.ToString(CultureInfo.InvariantCulture) + "번째 누적 완료\n");
                    ed.WriteMessage("- 현재 영역 즉시 추출: " + pendingSelection.ExtractedRowCount.ToString(CultureInfo.InvariantCulture) + "행\n");
                    ed.WriteMessage("- 전체 메모리 누적: " + batch.Rows.Count.ToString(CultureInfo.InvariantCulture) + "행\n");
                    ed.WriteMessage("- DATA 신규 비중복 구간: " + availableOutputRectangles.Count.ToString(CultureInfo.InvariantCulture) + "개\n");
                    ed.WriteMessage("- 새로 그린 선택박스: " + displayRectangles.Count.ToString(CultureInfo.InvariantCulture) + "개\n");
                    ed.WriteMessage("- 과거 표시선 중복: " + overlappedDisplayBoxCount.ToString(CultureInfo.InvariantCulture) + "개 (DATA 제외 안 함)\n");
                    ed.WriteMessage("- 현재 배치 좌표 중복: " + overlappedCurrentBatchCount.ToString(CultureInfo.InvariantCulture) + "개 (겹친 좌표만 제외)\n");
                    ed.WriteMessage("- 철근 번호 중복: 허용, 앞 영역 행을 교체하지 않고 Append\n");

                    if (pendingSelection.ExtractionError != "")
                    {
                        ed.WriteMessage("- 현재 영역 오류: " + pendingSelection.ExtractionError + "\n");
                    }

                    ed.WriteMessage("- 현재까지 CSV/ready: 0개 (최종 Enter 후 1개씩 생성)\n");
                    ed.WriteMessage("- 다음 영역의 시작점을 클릭하거나, 모든 선택이 끝났으면 Enter를 한 번 누르세요.\n");
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
                 * 따라서 객체스냅은 켜되, OVIA 선택박스는 반환된 스냅 좌표 그대로 생성하고
                 * OVIA가 별도로 좌/우/상/하 확장하지 않습니다.
                 */
                Application.SetSystemVariable("OSMODE", 33);

                if (ed != null)
                {
                    ed.WriteMessage("\nOVIA 테이블 선택 모드: 끝점/교차점 객체스냅을 임시 적용했습니다.\n");
                    ed.WriteMessage("표 외곽선과 행 경계선이 만나는 교차점에서 시작하고, 끝 행의 반대쪽 교차점에서 마무리하세요.\n");
                    ed.WriteMessage("OVIA 선택박스는 스냅된 좌표 그대로 생성하며, OVIA가 임의로 범위를 확장하지 않습니다.\n");
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

        private void RunSmartBoxTableExtraction(
            string commandName,
            Point3d point1,
            Point3d point2,
            Point3d analysisContextPoint1,
            Point3d analysisContextPoint2,
            List<OviaSelectionRectangle> allowedOutputRectangles)
        {
            RunSmartBoxTableExtraction(
                commandName,
                point1,
                point2,
                analysisContextPoint1,
                analysisContextPoint2,
                allowedOutputRectangles,
                null
            );
        }

        private void RunSmartBoxTableExtraction(
            string commandName,
            Point3d point1,
            Point3d point2,
            Point3d analysisContextPoint1,
            Point3d analysisContextPoint2,
            List<OviaSelectionRectangle> allowedOutputRectangles,
            OviaExtractionBatch extractionBatch)
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
                analysisContextMaxPoint,
                allowedOutputRectangles,
                extractionBatch
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
            RunSmartBoxTableExtractionFromWindow(
                commandName,
                selectedMinPoint,
                selectedMaxPoint,
                boxCount,
                analysisContextMinPoint,
                analysisContextMaxPoint,
                null,
                null
            );
        }

        private void RunSmartBoxTableExtractionFromWindow(
            string commandName,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            int boxCount,
            Point3d analysisContextMinPoint,
            Point3d analysisContextMaxPoint,
            List<OviaSelectionRectangle> allowedOutputRectangles,
            OviaExtractionBatch extractionBatch)
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
             * OVIA 2026-07-17 중복 선택 안정화:
             * 표 구조 분석과 철근형상 셀 캡처는 사용자가 방금 지정한 원래 전체 선택 영역을 기준으로 합니다.
             * 기존 박스와 겹치지 않는 신규 범위는 파싱이 끝난 행 목록에서 마지막으로 필터합니다.
             * 이렇게 해야 중복 경계에서 선택창이 잘려 형상 셀 안에 표 세로선과 길이/수량 문자가
             * 섞이는 현상을 방지할 수 있습니다.
             */
            CreateSmartTableAnalysisWindow(analysisContextMinPoint, analysisContextMaxPoint, out analysisMinPoint, out analysisMaxPoint);

            List<OviaTextRow> selectedTextRows = ExtractRowsByWindow(ed, db, selectedMinPoint, selectedMaxPoint);
            List<OviaTextRow> analysisTextRows = ExtractRowsByWindow(ed, db, analysisMinPoint, analysisMaxPoint);
            List<OviaGridLineSegment> analysisGridLines = ExtractGridLineSegmentsByWindow(ed, db, analysisMinPoint, analysisMaxPoint);
            List<OviaTextRow> currentTableAnalysisTextRows = FilterTextRowsToSelectedTableX(
                analysisTextRows,
                selectedMinPoint,
                selectedMaxPoint
            );

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

            int overlapFilteredRowCount = 0;

            if (allowedOutputRectangles != null && allowedOutputRectangles.Count > 0 && tableRows.Count > 0)
            {
                tableRows = FilterBarTableRowsBySelectionRectangles(
                    tableRows,
                    allowedOutputRectangles,
                    out overlapFilteredRowCount
                );

                if (overlapFilteredRowCount > 0)
                {
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "기존 선택영역과 겹친 " + overlapFilteredRowCount.ToString(CultureInfo.InvariantCulture) + "개 행을 CSV 전송 대상에서 제외했습니다."
                    );
                }
            }

            int repairedMarkNoCount;
            int rejectedContaminatedMarkNoCount;

            if (RecoverBarTableMarkNumbersByPhysicalColumn(
                tableRows,
                currentTableAnalysisTextRows,
                out repairedMarkNoCount,
                out rejectedContaminatedMarkNoCount))
            {
                if (repairedMarkNoCount > 0)
                {
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "번호 물리 열 기준으로 "
                        + repairedMarkNoCount.ToString(CultureInfo.InvariantCulture)
                        + "개 행의 번호를 복구했습니다."
                    );
                }

                if (rejectedContaminatedMarkNoCount > 0)
                {
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "철근형상·길이 숫자를 번호로 오인한 "
                        + rejectedContaminatedMarkNoCount.ToString(CultureInfo.InvariantCulture)
                        + "개 행을 차단했습니다."
                    );
                }
            }

            int rejectedMisalignedGridRowCount;

            if (HasLikelyMisappliedGridSchema(tableRows, out rejectedMisalignedGridRowCount))
            {
                /*
                 * 인접 표의 캐시 스키마가 잘못 적용된 결과를 그대로 CSV로 내보내지 않습니다.
                 * 대표 증상은 모든 번호가 수량과 같고, 형상 원문이 실제 도형 치수가 아니라
                 * 총길이·중량 두 값으로만 구성되는 경우입니다. 이때 grid 결과를 폐기하면 아래의
                 * 문자 헤더 좌표 파서와 규격 앵커 파서가 현재 선택 표를 독립적으로 다시 복구합니다.
                 */
                tableRows = new List<OviaBarTableRow>();
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "번호·철근형상 열 오정렬이 의심되는 "
                    + rejectedMisalignedGridRowCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행을 폐기하고 현재 선택 표를 재분석합니다."
                );
            }

            int ignoredNonRebarRowCount;
            tableRows = FilterActualRebarDataRows(tableRows, out ignoredNonRebarRowCount);

            if (ignoredNonRebarRowCount > 0)
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "소계·합계·총계 및 철근 데이터가 아닌 "
                    + ignoredNonRebarRowCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행을 제외했습니다."
                );
            }

            /*
             * OVIA 2026-07-20 소계/총계 병합행 선택 회귀 보정:
             * 짧은 마지막 표에서 60~64 데이터행과 소계/총계를 함께 선택하면 병합된 요약행의
             * 세로선 구조 때문에 grid 파서가 행 객체는 만들지만 모든 DATA 행의 컬럼을 안전하지
             * 않게 해석할 수 있습니다. 기존 코드는 grid 결과가 한 번이라도 생성되면 좌표 파서를
             * 사용하지 않아 후단 실제 철근행 필터에서 0행이 된 채 추출을 종료했습니다.
             *
             * grid 결과가 실제 철근행 0건으로 끝난 경우에만 선택 영역의 문자 헤더 좌표 파서를
             * 한 번 재시도합니다. 소계/총계는 동일한 실제 철근행 필터에서 다시 제외하고, 번호·규격·
             * 길이·수량이 유효한 60~64와 같은 DATA 행만 복구합니다.
             */
            if (tableRows.Count == 0 && usedGridParser && selectedTextRows.Count > 0)
            {
                List<OviaTextRow> coordinateTextRows = new List<OviaTextRow>(selectedTextRows);
                SortRowsTopToBottomLeftToRight(coordinateTextRows);
                ApplySimpleRowNumbers(coordinateTextRows);

                List<OviaBarTableRow> coordinateRows = BuildOviaBarTableRows(coordinateTextRows);
                int coordinateOverlapFilteredCount = 0;

                if (allowedOutputRectangles != null && allowedOutputRectangles.Count > 0 && coordinateRows.Count > 0)
                {
                    coordinateRows = FilterBarTableRowsBySelectionRectangles(
                        coordinateRows,
                        allowedOutputRectangles,
                        out coordinateOverlapFilteredCount
                    );
                }

                int coordinateRepairedMarkCount;
                int coordinateRejectedMarkCount;
                RecoverBarTableMarkNumbersByPhysicalColumn(
                    coordinateRows,
                    currentTableAnalysisTextRows,
                    out coordinateRepairedMarkCount,
                    out coordinateRejectedMarkCount
                );

                int coordinateMisalignedRowCount;

                if (HasLikelyMisappliedGridSchema(coordinateRows, out coordinateMisalignedRowCount))
                {
                    coordinateRows = new List<OviaBarTableRow>();
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "좌표 재분석에서도 번호·철근형상 열 오정렬 "
                        + coordinateMisalignedRowCount.ToString(CultureInfo.InvariantCulture)
                        + "개 행을 차단했습니다."
                    );
                }

                int coordinateIgnoredRowCount;
                coordinateRows = FilterActualRebarDataRows(coordinateRows, out coordinateIgnoredRowCount);

                if (coordinateRows.Count > 0)
                {
                    tableRows = coordinateRows;
                    usedGridParser = false;
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "소계·총계 병합행을 제외한 문자 헤더 좌표 재분석으로 실제 철근 "
                        + tableRows.Count.ToString(CultureInfo.InvariantCulture)
                        + "개 행을 복구했습니다."
                    );
                }
            }

            /*
             * 최종 DATA 앵커 복구:
             * 병합 소계/총계가 포함된 아주 짧은 표는 grid 파서와 일반 Y 그룹 파서가 모두 실패할
             * 수 있습니다. 이 경우 표 행 경계 개수나 병합 셀 구조를 더 이상 신뢰하지 않고, 선택
             * 영역 안의 실제 철근 규격(UHD19 등)을 한 행의 기준점으로 사용합니다. 각 기준점 사이
             * 중간값으로 행 Y 범위를 만들고 실제 헤더 물리 열에서 번호/길이/수량/총길이/중량을
             * 직접 다시 읽습니다. 소계/총계에는 규격이 없으므로 후보 생성 단계에서부터 제외됩니다.
             */
            if (tableRows.Count == 0 && selectedTextRows.Count > 0)
            {
                string anchoredDiagnostic;
                List<OviaBarTableRow> anchoredRows = BuildSpecAnchoredBarTableRows(
                    selectedTextRows,
                    analysisTextRows,
                    selectedMinPoint,
                    selectedMaxPoint,
                    out anchoredDiagnostic
                );

                if (anchoredDiagnostic != "")
                {
                    diagnostic = AppendDiagnostic(diagnostic, anchoredDiagnostic);
                }

                int anchoredOverlapFilteredCount = 0;

                if (allowedOutputRectangles != null && allowedOutputRectangles.Count > 0 && anchoredRows.Count > 0)
                {
                    anchoredRows = FilterBarTableRowsBySelectionRectangles(
                        anchoredRows,
                        allowedOutputRectangles,
                        out anchoredOverlapFilteredCount
                    );
                }

                int anchoredIgnoredRowCount;
                anchoredRows = FilterActualRebarDataRows(anchoredRows, out anchoredIgnoredRowCount);

                if (anchoredRows.Count > 0)
                {
                    tableRows = anchoredRows;
                    usedGridParser = false;
                }
            }

            /*
             * OVIA 2026-08-06 _01 - 부분 행 누락 복구:
             * grid 파서가 일부 DATA 행을 정상 구성한 상태에서는 기존 규격 앵커 복구가 실행되지
             * 않았습니다. 실제 회귀 도면에서 1~20을 선택했지만 7~20만 남고 1~6이 누락된 직접
             * 원인입니다. 현재 표의 규격 열에서 복구한 행 수가 더 많을 때만, 번호가 아니라 물리 Y
             * 중심을 기준으로 누락 행을 병합합니다. 다른 표에서 같은 번호가 반복되는 정상 데이터는
             * 번호 중복으로 제거하지 않습니다.
             */
            if (tableRows.Count > 0 && usedGridParser && selectedTextRows.Count > 0)
            {
                string partialAnchoredDiagnostic;
                List<OviaBarTableRow> partialAnchoredRows = BuildSpecAnchoredBarTableRows(
                    selectedTextRows,
                    analysisTextRows,
                    selectedMinPoint,
                    selectedMaxPoint,
                    out partialAnchoredDiagnostic
                );

                if (partialAnchoredDiagnostic != "")
                {
                    diagnostic = AppendDiagnostic(diagnostic, partialAnchoredDiagnostic);
                }

                int partialAnchoredOverlapFilteredCount = 0;

                if (allowedOutputRectangles != null && allowedOutputRectangles.Count > 0 && partialAnchoredRows.Count > 0)
                {
                    partialAnchoredRows = FilterBarTableRowsBySelectionRectangles(
                        partialAnchoredRows,
                        allowedOutputRectangles,
                        out partialAnchoredOverlapFilteredCount
                    );
                }

                int partialAnchoredIgnoredRowCount;
                partialAnchoredRows = FilterActualRebarDataRows(partialAnchoredRows, out partialAnchoredIgnoredRowCount);

                if (partialAnchoredRows.Count > tableRows.Count)
                {
                    int mergedMissingRowCount = MergeMissingBarTableRowsByPhysicalCenter(
                        tableRows,
                        partialAnchoredRows
                    );

                    if (mergedMissingRowCount > 0)
                    {
                        diagnostic = AppendDiagnostic(
                            diagnostic,
                            "규격 물리 행과 비교하여 grid 파서가 누락한 "
                            + mergedMissingRowCount.ToString(CultureInfo.InvariantCulture)
                            + "개 철근행을 Y 좌표 기준으로 복구했습니다."
                        );
                    }
                }
            }

            if (tableRows.Count == 0)
            {
                ed.WriteMessage("\nOVIA: 집계표로 변환할 데이터 행을 찾지 못했습니다.\n");
                ed.WriteMessage("선택박스가 표의 가로 전체 폭과 원하는 세로 행 구간을 포함하는지 확인해주세요.\n");

                if (diagnostic != "")
                {
                    ed.WriteMessage("분석 정보: " + diagnostic + "\n");
                }

                return;
            }

            /*
             * OVIA 2026-07-21 짧은 후속 표 번호-길이 오염 최종 복구:
             * 13~14처럼 DATA가 2행뿐이고 소계/총계 병합행이 함께 선택된 표에서는
             * MARK_NO 물리 열 후보가 길이 열로 잘못 확정되어 번호 13,14 대신
             * 길이 3710,3840이 번호로 덮어써질 수 있습니다.
             *
             * 행 원문은 X 좌표 순서로 결합되므로 정상 표의 첫 토큰은 실제 번호입니다.
             * 현재 번호가 같은 행의 길이값과 수치적으로 같고, 원문의 첫 번호 토큰은
             * 그 길이값과 다를 때만 원문 번호로 되돌립니다. 특정 번호·도면·좌표를
             * 하드코딩하지 않으며, 실제 번호와 길이가 우연히 같은 행은 변경하지 않습니다.
             */
            int repairedLengthContaminatedMarkCount;

            if (RepairLengthContaminatedMarkNumbersFromRawText(
                tableRows,
                out repairedLengthContaminatedMarkCount))
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "길이값으로 오염된 "
                    + repairedLengthContaminatedMarkCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행의 번호를 행 원문 첫 토큰으로 복구했습니다."
                );
            }

            /*
             * OVIA 2026-08-07 _06 - 가짜 내부 수평축으로 분할된 동일 DATA 행 재결합:
             * 선택 표 X 범위 재검증이 어려운 특수 Block/Proxy 도면에서도 동일 물리 행이 둘로
             * 남을 수 있으므로 최종 안전망을 둡니다. 번호만 같다는 이유로 합치지 않습니다.
             * 서로 바로 맞닿은 두 반쪽 행이 정상 행 높이 한 개 범위 안에 있고,
             * 번호·규격·길이·수량·중량 등 비형상 DATA가 모두 동일할 때만 하나로 결합합니다.
             * 두 정상 높이의 실제 행, 다른 표의 동일 번호, 선택영역 간 동일 번호는 합치지 않습니다.
             */
            int mergedFragmentedGridRowCount = usedGridParser
                ? MergeFragmentedGridRowsBySharedScalarIdentity(tableRows)
                : 0;

            if (mergedFragmentedGridRowCount > 0)
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "형상 내부 수평선으로 분할된 동일 물리 DATA 행 "
                    + mergedFragmentedGridRowCount.ToString(CultureInfo.InvariantCulture)
                    + "개를 원래 행으로 재결합했습니다."
                );
            }

            /*
             * OVIA 2026-07-20 최종 형상 셀 경계 복구:
             * 60~64처럼 실제 철근행과 병합 소계/총계를 함께 선택한 짧은 표에서는 문자 좌표
             * fallback이 숫자행은 복구해도 형상 셀의 X/Y 경계를 만들지 못할 수 있습니다.
             * 형상 JSON 캡처 직전에 실제 "철근형상" 헤더의 물리 X 범위와 인접 DATA 행 중심의
             * 중간 Y 범위를 다시 결합합니다. 형상 내부 가로선을 행 경계로 사용하지 않으며,
             * 소계/총계는 이미 제거되어 있으므로 철근 DATA 행에만 적용됩니다.
             */
            int recoveredShapeCellBoundCount = RecoverMissingShapeCellBoundsForDataRows(
                tableRows,
                selectedTextRows,
                analysisTextRows,
                analysisGridLines,
                selectedMinPoint,
                selectedMaxPoint
            );

            if (recoveredShapeCellBoundCount > 0)
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "현재 표의 실제 형상 헤더와 물리 GRID/요약행 차단 경계로 "
                    + recoveredShapeCellBoundCount.ToString(CultureInfo.InvariantCulture)
                    + "개 철근행의 형상 셀 경계를 검증·복구했습니다."
                );
            }

            /*
             * OVIA 2026-08-06 _01 - 형상 셀의 최종 물리 소유권 확정:
             * 행이 이미 GRID 경계를 가지고 있더라도 그 X 폭이 철근형상 헤더 셀보다 과도하게 넓으면
             * 번호·규격·길이·수량·총길이·중량 문자가 형상 JSON으로 들어갈 수 있습니다. 헤더 행에는
             * 철근 도형이 없으므로, 형상 헤더를 실제로 감싸는 두 세로 GRID를 최종 기준으로 사용하고
             * 각 DATA 중심을 감싸는 전폭 가로 GRID 두 개로 Y 경계를 다시 검증합니다.
             */
            int authoritativeShapeCellBoundCount = ApplyAuthoritativePhysicalShapeCellBounds(
                tableRows,
                currentTableAnalysisTextRows,
                analysisGridLines,
                selectedMinPoint,
                selectedMaxPoint
            );

            if (authoritativeShapeCellBoundCount > 0)
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "철근형상 헤더를 관통하는 실제 셀 GRID로 "
                    + authoritativeShapeCellBoundCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행의 형상 셀 소유권을 최종 확정했습니다."
                );
            }

            int duplicateWeightNoteCorrectionCount = RevalidateDuplicatedWeightNotesByPhysicalOwnership(
                tableRows,
                currentTableAnalysisTextRows,
                lastDetectedHeaderColumns
            );

            if (duplicateWeightNoteCorrectionCount > 0)
            {
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "중량과 비고에 동시에 배정된 동일 CAD 문자 "
                    + duplicateWeightNoteCorrectionCount.ToString(CultureInfo.InvariantCulture)
                    + "개를 NOTE 물리 셀 소유권으로 분리했습니다."
                );
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

            string filePath = extractionBatch == null
                ? CreateCsvFilePath(db, "OVIA_BoxTable")
                : extractionBatch.FilePath;

            try
            {
                /*
                 * OVIA 2026-07-22 _05 표 위상 기반 형상 추출:
                 * 행별 형상 셀 안에서 선 길이 비율로 표선과 철근선을 구분하지 않습니다.
                 * 사용자가 선택한 표 전체의 반복 수직선/전폭 수평선을 먼저 GRID 모델로 확정한 뒤,
                 * 각 형상 셀에서 그 GRID와 일치하는 선만 제외합니다.
                 */
                OviaCadTableGridModel cadTableGridModel = BuildCadTableGridModel(
                    analysisGridLines,
                    tableRows,
                    selectedMinPoint,
                    selectedMaxPoint
                );

                if (extractionBatch != null)
                {
                    int rowIndex;

                    for (rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
                    {
                        tableRows[rowIndex].No = extractionBatch.Rows.Count + rowIndex + 1;
                    }
                }

                CaptureCadShapeFilesForRows(ed, db, filePath, tableRows, cadTableGridModel);

                if (extractionBatch != null)
                {
                    extractionBatch.Rows.AddRange(tableRows);
                    ed.WriteMessage(
                        "\nOVIA: 현재 선택영역에서 "
                        + tableRows.Count.ToString(CultureInfo.InvariantCulture)
                        + "개 DATA 행을 통합 배치에 누적했습니다.\n"
                    );
                    return;
                }

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
                ed.WriteMessage(
                    "확정 GRID 축   : 형상경계 수직 "
                    + cadTableGridModel.VerticalXs.Count.ToString(CultureInfo.InvariantCulture)
                    + "개 / 물리표 수직 "
                    + cadTableGridModel.PhysicalTableVerticalXs.Count.ToString(CultureInfo.InvariantCulture)
                    + "개 / 수평 "
                    + cadTableGridModel.HorizontalYs.Count.ToString(CultureInfo.InvariantCulture)
                    + "개 / 원본객체 "
                    + cadTableGridModel.GridSourceHandles.Count.ToString(CultureInfo.InvariantCulture)
                    + "개\n"
                );
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

            ResetGridSchemaCache();
            ed.WriteMessage("\nOVIA 선택박스 삭제 완료: " + deletedCount.ToString() + "개\n");
        }

        private void ResetGridSchemaCache()
        {
            lock (GridSchemaCacheSync)
            {
                cachedGridSchemaDrawing = "";
                cachedGridSchemaMinX = 0;
                cachedGridSchemaMaxX = 0;
                cachedGridSchemaVerticalXs = new List<double>();
                cachedGridSchemaColumns = new List<OviaHeaderColumn>();
            }
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
                        column.HeaderTextVerified = true;
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

            OviaHeaderColumn coordinateShapeColumn = FindHeaderColumnByKey(headerMap.Columns, "SHAPE");
            List<double> coordinateDataRowCenters = GetCoordinateDataRowCenters(groupedRows, headerMap.HeaderRowIndex + 1);
            double coordinateAverageTextHeight = GetAverageGroupedTextHeight(groupedRows);

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
                row.RowCenterY = GetAverageTextY(line);
                row.RowBandHeight = GetAverageTextHeight(line);

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

                if (row.RowType == "DATA" && row.Spec != "")
                {
                    ApplyCoordinateShapeCellBounds(
                        row,
                        coordinateShapeColumn,
                        coordinateDataRowCenters,
                        coordinateAverageTextHeight
                    );
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

        private List<double> GetCoordinateDataRowCenters(List<List<OviaTextRow>> groupedRows, int startIndex)
        {
            List<double> centers = new List<double>();

            if (groupedRows == null)
            {
                return centers;
            }

            int i;

            for (i = Math.Max(startIndex, 0); i < groupedRows.Count; i++)
            {
                List<OviaTextRow> line = groupedRows[i];

                if (line == null || line.Count == 0)
                {
                    continue;
                }

                string rawText = JoinRowText(line);

                if (IsHeaderRow(rawText) || IsSummaryText(rawText) || DetectSpec(rawText) == "")
                {
                    continue;
                }

                centers.Add(GetAverageTextY(line));
            }

            centers.Sort();
            return centers;
        }

        private double GetAverageGroupedTextHeight(List<List<OviaTextRow>> groupedRows)
        {
            double total = 0;
            int count = 0;

            if (groupedRows == null)
            {
                return 1.0;
            }

            int i;

            for (i = 0; i < groupedRows.Count; i++)
            {
                List<OviaTextRow> line = groupedRows[i];

                if (line == null || line.Count == 0)
                {
                    continue;
                }

                double height = GetAverageTextHeight(line);

                if (height > 0.0001)
                {
                    total += height;
                    count++;
                }
            }

            return count == 0 ? 1.0 : total / (double)count;
        }

        private void ApplyCoordinateShapeCellBounds(
            OviaBarTableRow row,
            OviaHeaderColumn shapeColumn,
            List<double> dataRowCenters,
            double averageTextHeight)
        {
            if (row == null || shapeColumn == null || shapeColumn.RightX <= shapeColumn.LeftX)
            {
                return;
            }

            double centerY = row.RowCenterY;
            double upperCenter = Double.MaxValue;
            double lowerCenter = Double.MinValue;
            int i;

            if (dataRowCenters != null)
            {
                for (i = 0; i < dataRowCenters.Count; i++)
                {
                    double candidate = dataRowCenters[i];

                    if (candidate > centerY + 0.0001 && candidate < upperCenter)
                    {
                        upperCenter = candidate;
                    }

                    if (candidate < centerY - 0.0001 && candidate > lowerCenter)
                    {
                        lowerCenter = candidate;
                    }
                }
            }

            /*
             * 평균 간격은 중간 소계·총계 또는 서로 다른 높이의 행 하나가 섞이면 크게 흔들립니다.
             * 마지막 DATA 행의 범위를 안정적으로 만들기 위해 정상 DATA 중심 간격의 중앙값을 사용합니다.
             */
            double fallbackGap = GetTypicalDataRowCenterGap(dataRowCenters, averageTextHeight);
            double topY = upperCenter == Double.MaxValue
                ? centerY + (fallbackGap / 2.0)
                : (centerY + upperCenter) / 2.0;
            double bottomY = lowerCenter == Double.MinValue
                ? centerY - (fallbackGap / 2.0)
                : (centerY + lowerCenter) / 2.0;

            if (topY <= bottomY)
            {
                topY = centerY + (fallbackGap / 2.0);
                bottomY = centerY - (fallbackGap / 2.0);
            }

            row.ShapeCellMinX = shapeColumn.LeftX;
            row.ShapeCellMaxX = shapeColumn.RightX;
            row.ShapeCellMinY = bottomY;
            row.ShapeCellMaxY = topY;
            row.RowBandHeight = Math.Abs(topY - bottomY);
            row.ShapeCellBoundsSource = "COORDINATE";
        }

        private double GetTypicalDataRowCenterGap(List<double> dataRowCenters, double averageTextHeight)
        {
            List<double> gaps = new List<double>();
            double minimumGap = Math.Max(averageTextHeight * 0.35, 0.05);

            if (dataRowCenters != null && dataRowCenters.Count > 1)
            {
                int i;

                for (i = 1; i < dataRowCenters.Count; i++)
                {
                    double gap = Math.Abs(dataRowCenters[i] - dataRowCenters[i - 1]);

                    if (gap >= minimumGap)
                    {
                        gaps.Add(gap);
                    }
                }
            }

            if (gaps.Count == 0)
            {
                return Math.Max(averageTextHeight * 3.0, 1.0);
            }

            gaps.Sort();
            int middle = gaps.Count / 2;

            if ((gaps.Count % 2) == 0)
            {
                return (gaps[middle - 1] + gaps[middle]) / 2.0;
            }

            return gaps[middle];
        }

        private int MergeMissingBarTableRowsByPhysicalCenter(
            List<OviaBarTableRow> primaryRows,
            List<OviaBarTableRow> recoveryRows)
        {
            if (primaryRows == null || recoveryRows == null || recoveryRows.Count <= primaryRows.Count)
            {
                return 0;
            }

            List<double> rowHeights = new List<double>();
            int i;

            for (i = 0; i < recoveryRows.Count; i++)
            {
                OviaBarTableRow recoveryRow = recoveryRows[i];

                if (recoveryRow == null)
                {
                    continue;
                }

                double height = recoveryRow.RowBandHeight;

                if (height <= 0.0001 && recoveryRow.HasShapeCellBounds())
                {
                    height = Math.Abs(recoveryRow.ShapeCellMaxY - recoveryRow.ShapeCellMinY);
                }

                if (height > 0.0001)
                {
                    rowHeights.Add(height);
                }
            }

            double medianRowHeight = GetMedianCadGridValue(rowHeights);
            double centerTolerance = medianRowHeight > 0.0001
                ? Math.Max(medianRowHeight * 0.35, 0.20)
                : 0.50;
            int mergedCount = 0;

            for (i = 0; i < recoveryRows.Count; i++)
            {
                OviaBarTableRow recoveryRow = recoveryRows[i];

                if (recoveryRow == null
                    || !String.Equals(recoveryRow.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double recoveryCenterY = GetEffectiveBarTableRowCenterY(recoveryRow);
                bool alreadyExists = false;
                int primaryIndex;

                for (primaryIndex = 0; primaryIndex < primaryRows.Count; primaryIndex++)
                {
                    OviaBarTableRow primaryRow = primaryRows[primaryIndex];

                    if (primaryRow == null
                        || !String.Equals(primaryRow.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double primaryCenterY = GetEffectiveBarTableRowCenterY(primaryRow);

                    if (Math.Abs(primaryCenterY - recoveryCenterY) <= centerTolerance)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    primaryRows.Add(recoveryRow);
                    mergedCount++;
                }
            }

            if (mergedCount > 0)
            {
                primaryRows.Sort(delegate (OviaBarTableRow left, OviaBarTableRow right)
                {
                    return GetEffectiveBarTableRowCenterY(right).CompareTo(GetEffectiveBarTableRowCenterY(left));
                });

                for (i = 0; i < primaryRows.Count; i++)
                {
                    primaryRows[i].No = i + 1;
                }
            }

            return mergedCount;
        }

        private int MergeFragmentedGridRowsBySharedScalarIdentity(List<OviaBarTableRow> rows)
        {
            if (rows == null || rows.Count < 2)
            {
                return 0;
            }

            List<double> normalHeightCandidates = new List<double>();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null
                    || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double height = GetEffectiveBarTableRowHeight(row);

                if (height > 0.0001)
                {
                    normalHeightCandidates.Add(height);
                }
            }

            double typicalRowHeight = GetMedianCadGridValue(normalHeightCandidates);

            if (typicalRowHeight <= 0.0001)
            {
                return 0;
            }

            int mergedCount = 0;
            i = 0;

            while (i < rows.Count - 1)
            {
                OviaBarTableRow upperRow = rows[i];
                OviaBarTableRow lowerRow = rows[i + 1];

                if (!CanMergeFragmentedGridRows(upperRow, lowerRow, typicalRowHeight))
                {
                    i++;
                    continue;
                }

                MergeFragmentedGridRowPair(upperRow, lowerRow);
                rows.RemoveAt(i + 1);
                mergedCount++;
            }

            if (mergedCount > 0)
            {
                for (i = 0; i < rows.Count; i++)
                {
                    rows[i].No = i + 1;
                }
            }

            return mergedCount;
        }

        private bool CanMergeFragmentedGridRows(
            OviaBarTableRow upperRow,
            OviaBarTableRow lowerRow,
            double typicalRowHeight)
        {
            if (upperRow == null || lowerRow == null || typicalRowHeight <= 0.0001)
            {
                return false;
            }

            if (!String.Equals(upperRow.RowType, "DATA", StringComparison.OrdinalIgnoreCase)
                || !String.Equals(lowerRow.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (upperRow.SourceRowNo <= 0
                || lowerRow.SourceRowNo <= 0
                || Math.Abs(upperRow.SourceRowNo - lowerRow.SourceRowNo) != 1)
            {
                return false;
            }

            if (!HasSameRequiredBarTableScalarValue(upperRow.MarkNo, lowerRow.MarkNo)
                || !HasSameRequiredBarTableScalarValue(upperRow.Spec, lowerRow.Spec)
                || !HasSameRequiredBarTableScalarValue(upperRow.Length, lowerRow.Length)
                || !HasSameRequiredBarTableScalarValue(upperRow.Qty, lowerRow.Qty)
                || !HasSameRequiredBarTableScalarValue(upperRow.TotalWeight, lowerRow.TotalWeight))
            {
                return false;
            }

            if (!HasCompatibleOptionalBarTableScalarValue(upperRow.TotalLength, lowerRow.TotalLength)
                || !HasCompatibleOptionalBarTableScalarValue(upperRow.Part, lowerRow.Part)
                || !HasCompatibleOptionalBarTableScalarValue(upperRow.Note, lowerRow.Note))
            {
                return false;
            }

            double upperHeight = GetEffectiveBarTableRowHeight(upperRow);
            double lowerHeight = GetEffectiveBarTableRowHeight(lowerRow);

            if (upperHeight <= 0.0001 || lowerHeight <= 0.0001)
            {
                return false;
            }

            double upperMinY;
            double upperMaxY;
            double lowerMinY;
            double lowerMaxY;

            GetEffectiveBarTableRowBoundsY(upperRow, out upperMinY, out upperMaxY);
            GetEffectiveBarTableRowBoundsY(lowerRow, out lowerMinY, out lowerMaxY);

            double unionHeight = Math.Max(upperMaxY, lowerMaxY) - Math.Min(upperMinY, lowerMinY);
            double sharedBoundaryGap = Math.Abs(upperMinY - lowerMaxY);
            double gapTolerance = Math.Max(typicalRowHeight * 0.12, 0.20);

            if (sharedBoundaryGap > gapTolerance)
            {
                return false;
            }

            if (unionHeight < typicalRowHeight * 0.62
                || unionHeight > typicalRowHeight * 1.38)
            {
                return false;
            }

            /*
             * 두 정상 행이 우연히 같은 값을 가질 수 있으므로 두 행 모두 정상 높이에 가까우면
             * 절대 병합하지 않습니다. 가짜 내부축으로 잘린 경우에만 최소 한쪽은 정상 행보다
             * 확실히 작고, 큰 쪽도 정상 한 행 높이를 넘지 않습니다.
             */
            if (upperHeight > typicalRowHeight * 0.88
                || lowerHeight > typicalRowHeight * 0.88)
            {
                return false;
            }

            if (upperRow.HasShapeCellBounds() && lowerRow.HasShapeCellBounds())
            {
                double upperWidth = Math.Abs(upperRow.ShapeCellMaxX - upperRow.ShapeCellMinX);
                double lowerWidth = Math.Abs(lowerRow.ShapeCellMaxX - lowerRow.ShapeCellMinX);
                double overlap = Math.Min(upperRow.ShapeCellMaxX, lowerRow.ShapeCellMaxX)
                    - Math.Max(upperRow.ShapeCellMinX, lowerRow.ShapeCellMinX);
                double minimumWidth = Math.Min(upperWidth, lowerWidth);

                if (minimumWidth <= 0.0001 || overlap < minimumWidth * 0.85)
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasSameRequiredBarTableScalarValue(string first, string second)
        {
            string normalizedFirst = NormalizeBarTableScalarValue(first);
            string normalizedSecond = NormalizeBarTableScalarValue(second);

            return normalizedFirst != ""
                && normalizedSecond != ""
                && String.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasCompatibleOptionalBarTableScalarValue(string first, string second)
        {
            string normalizedFirst = NormalizeBarTableScalarValue(first);
            string normalizedSecond = NormalizeBarTableScalarValue(second);

            if (normalizedFirst == "" || normalizedSecond == "")
            {
                return true;
            }

            return String.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeBarTableScalarValue(string value)
        {
            if (value == null)
            {
                return "";
            }

            return Regex.Replace(value.Trim().ToUpperInvariant(), @"[\s,]+", "");
        }

        private double GetEffectiveBarTableRowHeight(OviaBarTableRow row)
        {
            if (row == null)
            {
                return 0.0;
            }

            if (row.RowBandHeight > 0.0001)
            {
                return Math.Abs(row.RowBandHeight);
            }

            if (row.HasShapeCellBounds())
            {
                return Math.Abs(row.ShapeCellMaxY - row.ShapeCellMinY);
            }

            return 0.0;
        }

        private void GetEffectiveBarTableRowBoundsY(
            OviaBarTableRow row,
            out double minY,
            out double maxY)
        {
            minY = 0.0;
            maxY = 0.0;

            if (row == null)
            {
                return;
            }

            if (row.HasShapeCellBounds())
            {
                minY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
                maxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
                return;
            }

            double height = GetEffectiveBarTableRowHeight(row);
            double centerY = GetEffectiveBarTableRowCenterY(row);
            minY = centerY - (height / 2.0);
            maxY = centerY + (height / 2.0);
        }

        private void MergeFragmentedGridRowPair(
            OviaBarTableRow targetRow,
            OviaBarTableRow fragmentRow)
        {
            if (targetRow == null || fragmentRow == null)
            {
                return;
            }

            double targetMinY;
            double targetMaxY;
            double fragmentMinY;
            double fragmentMaxY;

            GetEffectiveBarTableRowBoundsY(targetRow, out targetMinY, out targetMaxY);
            GetEffectiveBarTableRowBoundsY(fragmentRow, out fragmentMinY, out fragmentMaxY);

            double mergedMinY = Math.Min(targetMinY, fragmentMinY);
            double mergedMaxY = Math.Max(targetMaxY, fragmentMaxY);

            targetRow.RowCenterY = (mergedMinY + mergedMaxY) / 2.0;
            targetRow.RowBandHeight = Math.Abs(mergedMaxY - mergedMinY);
            targetRow.SourceRowNo = Math.Min(targetRow.SourceRowNo, fragmentRow.SourceRowNo);
            targetRow.RawText = MergeDistinctBarTableText(targetRow.RawText, fragmentRow.RawText);
            targetRow.ShapeText = MergeDistinctBarTableText(targetRow.ShapeText, fragmentRow.ShapeText);
            targetRow.ShapeRawText = MergeDistinctBarTableText(targetRow.ShapeRawText, fragmentRow.ShapeRawText);
            targetRow.ShapeDimensionText = MergeDistinctBarTableText(
                targetRow.ShapeDimensionText,
                fragmentRow.ShapeDimensionText
            );

            if (targetRow.Part == "")
            {
                targetRow.Part = fragmentRow.Part;
            }

            if (targetRow.ShapeNo == "")
            {
                targetRow.ShapeNo = fragmentRow.ShapeNo;
            }

            if (targetRow.Note == "")
            {
                targetRow.Note = fragmentRow.Note;
            }

            bool targetHasBounds = targetRow.HasShapeCellBounds();
            bool fragmentHasBounds = fragmentRow.HasShapeCellBounds();

            if (targetHasBounds || fragmentHasBounds)
            {
                if (targetHasBounds && fragmentHasBounds)
                {
                    targetRow.ShapeCellMinX = Math.Min(targetRow.ShapeCellMinX, fragmentRow.ShapeCellMinX);
                    targetRow.ShapeCellMaxX = Math.Max(targetRow.ShapeCellMaxX, fragmentRow.ShapeCellMaxX);
                }
                else if (!targetHasBounds && fragmentHasBounds)
                {
                    targetRow.ShapeCellMinX = fragmentRow.ShapeCellMinX;
                    targetRow.ShapeCellMaxX = fragmentRow.ShapeCellMaxX;
                }

                targetRow.ShapeCellMinY = mergedMinY;
                targetRow.ShapeCellMaxY = mergedMaxY;
                targetRow.ShapeCellBoundsSource = "GRID_FRAGMENT_MERGED";
            }

            targetRow.CadShapeJsonPath = "";
            targetRow.CadShapeTextValues = "";
            targetRow.ShapeSource = "";
            targetRow.ShapeStatus = "";
        }

        private string MergeDistinctBarTableText(string first, string second)
        {
            string cleanFirst = first == null ? "" : Regex.Replace(first.Trim(), @"\s+", " ");
            string cleanSecond = second == null ? "" : Regex.Replace(second.Trim(), @"\s+", " ");

            if (cleanFirst == "")
            {
                return cleanSecond;
            }

            if (cleanSecond == "")
            {
                return cleanFirst;
            }

            if (String.Equals(cleanFirst, cleanSecond, StringComparison.OrdinalIgnoreCase))
            {
                return cleanFirst;
            }

            return cleanFirst + " " + cleanSecond;
        }

        private double GetEffectiveBarTableRowCenterY(OviaBarTableRow row)
        {
            if (row == null)
            {
                return 0.0;
            }

            if (Math.Abs(row.RowCenterY) > 0.0001)
            {
                return row.RowCenterY;
            }

            if (row.HasShapeCellBounds())
            {
                return (row.ShapeCellMinY + row.ShapeCellMaxY) / 2.0;
            }

            return 0.0;
        }

        private int ApplyAuthoritativePhysicalShapeCellBounds(
            List<OviaBarTableRow> rows,
            List<OviaTextRow> tableTextRows,
            List<OviaGridLineSegment> gridLines,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint)
        {
            if (rows == null || rows.Count == 0 || tableTextRows == null || tableTextRows.Count == 0)
            {
                return 0;
            }

            OviaHeaderColumn authoritativeShapeColumn;

            if (!TryBuildAuthoritativePhysicalShapeColumn(
                tableTextRows,
                gridLines,
                selectedMinPoint,
                selectedMaxPoint,
                out authoritativeShapeColumn))
            {
                return 0;
            }

            double selectedMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double averageTextHeight = GetAverageTextHeight(tableTextRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = 1.0;
            }

            List<double> verifiedHorizontalYs = GetVerifiedPhysicalTableHorizontalYs(
                gridLines,
                selectedMinX,
                selectedMaxX,
                averageTextHeight
            );
            double typicalPhysicalRowHeight = GetTypicalPhysicalShapeRowHeight(
                rows,
                verifiedHorizontalYs,
                averageTextHeight
            );
            int updatedCount = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double previousMinX = row.ShapeCellMinX;
                double previousMaxX = row.ShapeCellMaxX;
                double previousMinY = row.ShapeCellMinY;
                double previousMaxY = row.ShapeCellMaxY;
                string previousSource = row.ShapeCellBoundsSource;
                bool replaceX = ShouldReplaceShapeCellXWithAuthoritativeGrid(row, authoritativeShapeColumn);
                bool replacedY = false;

                if (replaceX)
                {
                    row.ShapeCellMinX = authoritativeShapeColumn.LeftX;
                    row.ShapeCellMaxX = authoritativeShapeColumn.RightX;
                }

                if (verifiedHorizontalYs != null && verifiedHorizontalYs.Count >= 2)
                {
                    replacedY = TryApplyVerifiedPhysicalGridShapeRowBounds(
                        row,
                        verifiedHorizontalYs,
                        averageTextHeight,
                        typicalPhysicalRowHeight
                    );
                }

                bool changed = Math.Abs(previousMinX - row.ShapeCellMinX) > 0.0001
                    || Math.Abs(previousMaxX - row.ShapeCellMaxX) > 0.0001
                    || Math.Abs(previousMinY - row.ShapeCellMinY) > 0.0001
                    || Math.Abs(previousMaxY - row.ShapeCellMaxY) > 0.0001;

                if (changed)
                {
                    row.ShapeCellBoundsSource = "GRID_HEADER_AUTH";
                    updatedCount++;
                }
                else if ((replaceX || replacedY) && previousSource == "")
                {
                    row.ShapeCellBoundsSource = "GRID_HEADER_AUTH";
                }
            }

            return updatedCount;
        }

        private bool ShouldReplaceShapeCellXWithAuthoritativeGrid(
            OviaBarTableRow row,
            OviaHeaderColumn authoritativeShapeColumn)
        {
            if (row == null || authoritativeShapeColumn == null
                || authoritativeShapeColumn.RightX <= authoritativeShapeColumn.LeftX)
            {
                return false;
            }

            if (!row.HasShapeCellBounds())
            {
                return true;
            }

            double currentLeft = Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX);
            double currentRight = Math.Max(row.ShapeCellMinX, row.ShapeCellMaxX);
            double currentWidth = currentRight - currentLeft;
            double currentCenter = (currentLeft + currentRight) / 2.0;
            double authoritativeWidth = authoritativeShapeColumn.RightX - authoritativeShapeColumn.LeftX;
            double authoritativeCenter = (authoritativeShapeColumn.LeftX + authoritativeShapeColumn.RightX) / 2.0;

            if (currentWidth <= 0.0001 || authoritativeWidth <= 0.0001)
            {
                return true;
            }

            /*
             * 정상 GRID 경계는 물리 헤더 셀과 거의 같은 폭/중심을 가집니다. 현재 셀이 헤더 셀보다
             * 30% 이상 넓거나, 중심이 헤더 셀 폭의 20% 이상 이동했거나, 좌우 경계가 12% 이상
             * 다르면 다른 데이터 열까지 포함한 것으로 보고 헤더 행의 실제 GRID로 교체합니다.
             */
            return currentWidth > authoritativeWidth * 1.30
                || currentWidth < authoritativeWidth * 0.70
                || Math.Abs(currentCenter - authoritativeCenter) > authoritativeWidth * 0.20
                || Math.Abs(currentLeft - authoritativeShapeColumn.LeftX) > authoritativeWidth * 0.12
                || Math.Abs(currentRight - authoritativeShapeColumn.RightX) > authoritativeWidth * 0.12;
        }

        private bool TryBuildAuthoritativePhysicalShapeColumn(
            List<OviaTextRow> tableTextRows,
            List<OviaGridLineSegment> gridLines,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            out OviaHeaderColumn shapeColumn)
        {
            shapeColumn = null;

            if (tableTextRows == null || tableTextRows.Count == 0 || gridLines == null || gridLines.Count == 0)
            {
                return false;
            }

            double selectedMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMinY = Math.Min(selectedMinPoint.Y, selectedMaxPoint.Y);
            double selectedMaxY = Math.Max(selectedMinPoint.Y, selectedMaxPoint.Y);
            double tableWidth = Math.Max(selectedMaxX - selectedMinX, 0.0001);
            double tableHeight = Math.Max(selectedMaxY - selectedMinY, 0.0001);
            double averageTextHeight = GetAverageTextHeight(tableTextRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = Math.Max(tableHeight * 0.02, 1.0);
            }

            OviaTextRow shapeHeaderText = null;
            double nearestHeaderDistance = Double.MaxValue;
            double xMargin = Math.Max(tableWidth * 0.025, 0.5);
            int i;

            for (i = 0; i < tableTextRows.Count; i++)
            {
                OviaTextRow textRow = tableTextRows[i];

                if (textRow == null
                    || textRow.X < selectedMinX - xMargin
                    || textRow.X > selectedMaxX + xMargin)
                {
                    continue;
                }

                string title = CleanHeaderText(textRow.TextValue);
                string key = ClassifyHeaderTitle(title);

                if (!String.Equals(key, "SHAPE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double distance = Math.Abs(textRow.Y - selectedMaxY);

                if (distance < nearestHeaderDistance)
                {
                    nearestHeaderDistance = distance;
                    shapeHeaderText = textRow;
                }
            }

            if (shapeHeaderText == null)
            {
                return false;
            }

            double axisTolerance = Math.Max(Math.Max(averageTextHeight * 0.10, tableWidth * 0.00025), 0.03);
            double mergeTolerance = Math.Max(Math.Max(averageTextHeight * 0.20, tableWidth * 0.00045), 0.05);
            double headerBandMargin = Math.Max(averageTextHeight * 0.45, 0.12);
            List<double> verticalXs = new List<double>();

            for (i = 0; i < gridLines.Count; i++)
            {
                OviaGridLineSegment segment = gridLines[i];

                if (segment == null)
                {
                    continue;
                }

                double dx = Math.Abs(segment.X2 - segment.X1);
                double minSegmentY = Math.Min(segment.Y1, segment.Y2);
                double maxSegmentY = Math.Max(segment.Y1, segment.Y2);

                if (dx > axisTolerance
                    || minSegmentY > shapeHeaderText.Y - headerBandMargin
                    || maxSegmentY < shapeHeaderText.Y + headerBandMargin)
                {
                    continue;
                }

                double x = (segment.X1 + segment.X2) / 2.0;

                if (x >= selectedMinX - xMargin && x <= selectedMaxX + xMargin)
                {
                    verticalXs.Add(x);
                }
            }

            verticalXs = MergeGridCoordinates(verticalXs, mergeTolerance, true);
            verticalXs = LimitGridVerticalCoordinatesToSelectedTable(
                verticalXs,
                selectedMinPoint,
                selectedMaxPoint,
                mergeTolerance
            );

            if (verticalXs.Count < 3)
            {
                /*
                 * 셀 단위 세로선이 문자 기준 Y에서 아주 조금 끊긴 도면은 선택 DATA 높이의 72%를
                 * 실제로 관통하는 세로선으로 한 번 더 복구합니다. 형상 내부선은 각 행 안에서만
                 * 존재하므로 이 커버리지를 만족하지 않습니다.
                 */
                verticalXs = ExtractCoveredGridCoordinates(
                    gridLines,
                    true,
                    axisTolerance,
                    mergeTolerance,
                    Math.Max(tableHeight * 0.02, 0.20),
                    tableHeight * 0.72,
                    selectedMinY,
                    selectedMaxY
                );
                verticalXs = LimitGridVerticalCoordinatesToSelectedTable(
                    verticalXs,
                    selectedMinPoint,
                    selectedMaxPoint,
                    mergeTolerance
                );
            }

            if (verticalXs.Count < 3)
            {
                return false;
            }

            verticalXs.Sort();
            double leftX = Double.NaN;
            double rightX = Double.NaN;

            for (i = 0; i < verticalXs.Count; i++)
            {
                double x = verticalXs[i];

                if (x <= shapeHeaderText.X + mergeTolerance && (Double.IsNaN(leftX) || x > leftX))
                {
                    leftX = x;
                }

                if (x >= shapeHeaderText.X - mergeTolerance && (Double.IsNaN(rightX) || x < rightX))
                {
                    rightX = x;
                }
            }

            if (!Double.IsNaN(leftX) && !Double.IsNaN(rightX) && Math.Abs(rightX - leftX) <= mergeTolerance)
            {
                rightX = Double.NaN;

                for (i = 0; i < verticalXs.Count; i++)
                {
                    if (verticalXs[i] > leftX + mergeTolerance)
                    {
                        rightX = verticalXs[i];
                        break;
                    }
                }
            }

            if (Double.IsNaN(leftX) || Double.IsNaN(rightX) || rightX <= leftX)
            {
                return false;
            }

            double shapeWidth = rightX - leftX;

            if (shapeWidth < tableWidth * 0.03 || shapeWidth > tableWidth * 0.35)
            {
                return false;
            }

            /*
             * 동일 헤더 밴드의 규격·길이·수량 등 다른 헤더 중심이 후보 셀 안에 있으면 경계 하나가
             * 누락되어 여러 데이터 열이 합쳐진 상태입니다. 이 후보는 authoritative로 사용하지 않습니다.
             */
            double headerYTolerance = Math.Max(averageTextHeight * 5.0, 2.0);

            for (i = 0; i < tableTextRows.Count; i++)
            {
                OviaTextRow textRow = tableTextRows[i];

                if (textRow == null || Math.Abs(textRow.Y - shapeHeaderText.Y) > headerYTolerance)
                {
                    continue;
                }

                string key = ClassifyHeaderTitle(CleanHeaderText(textRow.TextValue));

                if (key == "" || String.Equals(key, "SHAPE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (textRow.X > leftX + mergeTolerance && textRow.X < rightX - mergeTolerance)
                {
                    return false;
                }
            }

            shapeColumn = new OviaHeaderColumn();
            shapeColumn.StandardKey = "SHAPE";
            shapeColumn.OriginalTitle = "철근형상";
            shapeColumn.LeftX = leftX;
            shapeColumn.RightX = rightX;
            shapeColumn.X = (leftX + rightX) / 2.0;
            shapeColumn.SourceColumnIndex = -1;
            return true;
        }

        private int RecoverMissingShapeCellBoundsForDataRows(
            List<OviaBarTableRow> rows,
            List<OviaTextRow> selectedTextRows,
            List<OviaTextRow> analysisTextRows,
            List<OviaGridLineSegment> analysisGridLines,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint)
        {
            if (rows == null || rows.Count == 0)
            {
                return 0;
            }

            int i;

            double selectedMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMinY = Math.Min(selectedMinPoint.Y, selectedMaxPoint.Y);
            double selectedMaxY = Math.Max(selectedMinPoint.Y, selectedMaxPoint.Y);
            double selectedWidth = Math.Max(selectedMaxX - selectedMinX, 0.0001);
            double xMargin = Math.Max(selectedWidth * 0.025, 0.5);
            List<OviaTextRow> headerSourceRows = analysisTextRows == null || analysisTextRows.Count == 0
                ? selectedTextRows
                : analysisTextRows;
            List<OviaTextRow> tableTextRows = new List<OviaTextRow>();

            if (headerSourceRows != null)
            {
                for (i = 0; i < headerSourceRows.Count; i++)
                {
                    OviaTextRow textRow = headerSourceRows[i];

                    if (textRow == null
                        || textRow.X < selectedMinX - xMargin
                        || textRow.X > selectedMaxX + xMargin)
                    {
                        continue;
                    }

                    tableTextRows.Add(textRow);
                }
            }

            OviaHeaderColumn detectedShapeColumn = null;
            OviaHeaderColumn parsedShapeColumn = FindHeaderColumnByKey(lastDetectedHeaderColumns, "SHAPE");

            if (tableTextRows.Count > 0)
            {
                OviaHeaderMap headerMap = null;

                try
                {
                    headerMap = DetectHeaderMap(
                        GroupRowsByY(new List<OviaTextRow>(tableTextRows)),
                        tableTextRows
                    );
                }
                catch
                {
                    headerMap = null;
                }

                if (headerMap != null && headerMap.Columns != null)
                {
                    detectedShapeColumn = FindHeaderColumnByKey(headerMap.Columns, "SHAPE");
                }
            }

            /*
             * OVIA 2026-07-22 _09 - 형상 X 경계의 물리 GRID 우선권:
             * BuildOviaGridTableRows가 실제 표의 세로 GRID로 만든 행별 SHAPE 셀 경계는
             * 선택 행 수와 무관한 물리 경계입니다. 이전 복구 단계는 이 정상 경계를 버리고,
             * 선택영역 안에서 반복되는 U형 철근의 좌우 수직선을 "여러 행을 관통하는 선"으로
             * 오인해 SHAPE 열을 안쪽으로 축소했습니다. 그 결과 21~34의 좌우 치수와 38의
             * 우측 연결부가 셀 밖으로 잘렸습니다.
             *
             * 기존 GRID 행이 하나라도 있으면 그 행들의 물리 X 경계를 최우선 복구 열로 사용합니다.
             * 헤더/문자 기반 열 재검출은 GRID 경계가 없는 SPEC_ANCHOR/COORDINATE 행에만 사용합니다.
             */
            OviaHeaderColumn existingGridShapeColumn = GetRecoveredShapeColumnFromExistingGridRows(
                rows,
                selectedMinX,
                selectedMaxX
            );

            OviaHeaderColumn shapeColumn = existingGridShapeColumn;

            if (shapeColumn == null)
            {
                if ((parsedShapeColumn == null || parsedShapeColumn.RightX <= parsedShapeColumn.LeftX)
                    && (detectedShapeColumn == null || detectedShapeColumn.RightX <= detectedShapeColumn.LeftX))
                {
                    return 0;
                }

                parsedShapeColumn = RefineRecoveredShapeColumnWithPhysicalGridLines(
                    parsedShapeColumn,
                    analysisGridLines,
                    selectedMinPoint,
                    selectedMaxPoint
                );
                detectedShapeColumn = RefineRecoveredShapeColumnWithPhysicalGridLines(
                    detectedShapeColumn,
                    analysisGridLines,
                    selectedMinPoint,
                    selectedMaxPoint
                );

                shapeColumn = SelectBestRecoveredShapeColumn(
                    parsedShapeColumn,
                    detectedShapeColumn,
                    tableTextRows,
                    rows
                );
            }

            if (shapeColumn == null || shapeColumn.RightX <= shapeColumn.LeftX
                || shapeColumn.RightX < selectedMinX - xMargin
                || shapeColumn.LeftX > selectedMaxX + xMargin)
            {
                return 0;
            }

            List<double> dataRowCenters = new List<double>();

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double effectiveCenterY = row.RowCenterY;

                if (Math.Abs(effectiveCenterY) <= 0.0001 && row.HasShapeCellBounds())
                {
                    effectiveCenterY = (row.ShapeCellMinY + row.ShapeCellMaxY) / 2.0;
                    row.RowCenterY = effectiveCenterY;
                }

                if (Math.Abs(effectiveCenterY) > 0.0001)
                {
                    bool duplicate = false;
                    int centerIndex;

                    for (centerIndex = 0; centerIndex < dataRowCenters.Count; centerIndex++)
                    {
                        if (Math.Abs(dataRowCenters[centerIndex] - effectiveCenterY) <= 0.0001)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        dataRowCenters.Add(effectiveCenterY);
                    }
                }
            }

            if (dataRowCenters.Count == 0)
            {
                return 0;
            }

            dataRowCenters.Sort();
            double averageTextHeight = GetAverageTextHeight(tableTextRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = 1.0;
            }

            double typicalRowGap = GetTypicalDataRowCenterGap(dataRowCenters, averageTextHeight);
            List<double> verifiedHorizontalYs = GetVerifiedPhysicalTableHorizontalYs(
                analysisGridLines,
                selectedMinX,
                selectedMaxX,
                averageTextHeight
            );
            double typicalPhysicalRowHeight = GetTypicalPhysicalShapeRowHeight(
                rows,
                verifiedHorizontalYs,
                averageTextHeight
            );
            List<double> summaryRowCenters = GetSummaryRowCenters(
                tableTextRows,
                selectedMinY - averageTextHeight,
                selectedMaxY + averageTextHeight,
                averageTextHeight
            );
            int recoveredCount = 0;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null
                    || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double previousMinX = row.ShapeCellMinX;
                double previousMaxX = row.ShapeCellMaxX;
                double previousMinY = row.ShapeCellMinY;
                double previousMaxY = row.ShapeCellMaxY;
                string previousBoundsSource = row.ShapeCellBoundsSource;
                bool hadShapeCellBounds = row.HasShapeCellBounds();

                bool existingPhysicalGridXIsAuthoritative = hadShapeCellBounds
                    && String.Equals(previousBoundsSource, "GRID", StringComparison.OrdinalIgnoreCase)
                    && previousMaxX > previousMinX
                    && previousMinX >= selectedMinX - xMargin
                    && previousMaxX <= selectedMaxX + xMargin;

                if (existingPhysicalGridXIsAuthoritative)
                {
                    /*
                     * 실제 표 GRID에서 이미 확정된 형상 열은 절대 전역 재검출 열로 덮어쓰지 않습니다.
                     * 동일 도면의 30~38 소구간은 정상인데 21~38 대구간에서만 실패한 직접 원인이
                     * 이 덮어쓰기였습니다.
                     */
                    row.ShapeCellMinX = previousMinX;
                    row.ShapeCellMaxX = previousMaxX;
                }
                else
                {
                    row.ShapeCellMinX = shapeColumn.LeftX;
                    row.ShapeCellMaxX = shapeColumn.RightX;
                }

                double existingHeight = hadShapeCellBounds
                    ? Math.Abs(previousMaxY - previousMinY)
                    : 0.0;
                double existingCenterY = hadShapeCellBounds
                    ? (previousMinY + previousMaxY) / 2.0
                    : row.RowCenterY;
                bool existingContainsRowCenter = hadShapeCellBounds
                    && (Math.Abs(row.RowCenterY) <= 0.0001
                        || (row.RowCenterY >= Math.Min(previousMinY, previousMaxY) - averageTextHeight * 0.20
                            && row.RowCenterY <= Math.Max(previousMinY, previousMaxY) + averageTextHeight * 0.20));
                bool containsSummaryCenter = DoesShapeCellBandContainSummaryCenter(
                    previousMinY,
                    previousMaxY,
                    row.RowCenterY,
                    summaryRowCenters,
                    averageTextHeight
                );
                bool containsInternalPhysicalGridLine = HasInternalVerifiedHorizontalGridLine(
                    previousMinY,
                    previousMaxY,
                    verifiedHorizontalYs,
                    averageTextHeight
                );
                bool gridBoundsAreAuthoritative = hadShapeCellBounds
                    && String.Equals(previousBoundsSource, "GRID", StringComparison.OrdinalIgnoreCase)
                    && existingContainsRowCenter
                    && existingHeight >= Math.Max(averageTextHeight * 1.05, 0.10)
                    && !containsSummaryCenter
                    && !containsInternalPhysicalGridLine;

                /*
                 * OVIA 2026-07-22 _07 - 물리 GRID 행 경계 우선권:
                 * 표 라인/셀 파서가 확정한 DATA 행의 상·하 경계는 DATA 중심 간격으로 다시 계산하지 않습니다.
                 * 중간 소계/총계 때문에 두 DATA 중심 사이가 두세 행 높이로 벌어져도, 각 DATA의 실제 셀은
                 * 이미 인접한 전폭 수평 표선 사이로 정확히 확정되어 있습니다. 이 경계를 대표 DATA 간격과
                 * 비교해 폐기하면 13~14처럼 요약행을 절반씩 포함한 거대한 형상 셀이 만들어집니다.
                 */
                if (gridBoundsAreAuthoritative)
                {
                    row.ShapeCellMinY = previousMinY;
                    row.ShapeCellMaxY = previousMaxY;
                    row.RowBandHeight = existingHeight;
                    row.ShapeCellBoundsSource = "GRID";
                }
                else if (TryApplyVerifiedPhysicalGridShapeRowBounds(
                    row,
                    verifiedHorizontalYs,
                    averageTextHeight,
                    typicalPhysicalRowHeight))
                {
                    // 실제 표 전체를 가로지르는 가장 가까운 수평선 두 개로 현재 DATA 행만 복구했습니다.
                }
                else
                {
                    double referenceHeight = typicalPhysicalRowHeight > 0.0001
                        ? typicalPhysicalRowHeight
                        : typicalRowGap;
                    bool existingYIsUsable = hadShapeCellBounds
                        && existingContainsRowCenter
                        && !containsSummaryCenter
                        && !containsInternalPhysicalGridLine
                        && existingHeight >= referenceHeight * 0.45
                        && existingHeight <= referenceHeight * 1.80
                        && Math.Abs(existingCenterY - row.RowCenterY) <= Math.Max(referenceHeight * 0.40, averageTextHeight);

                    if (existingYIsUsable)
                    {
                        row.ShapeCellMinY = previousMinY;
                        row.ShapeCellMaxY = previousMaxY;
                        row.RowBandHeight = existingHeight;
                        row.ShapeCellBoundsSource = previousBoundsSource == "" ? "PRESERVED" : previousBoundsSource;
                    }
                    else
                    {
                        ApplyCoordinateShapeCellBounds(
                            row,
                            shapeColumn,
                            dataRowCenters,
                            averageTextHeight
                        );
                        ClampCoordinateShapeCellBoundsToSummaryBarriers(
                            row,
                            summaryRowCenters,
                            referenceHeight,
                            averageTextHeight
                        );
                    }
                }

                if (!row.HasShapeCellBounds())
                {
                    double fallbackHeight = typicalPhysicalRowHeight > 0.0001
                        ? typicalPhysicalRowHeight
                        : typicalRowGap;
                    row.ShapeCellMinY = row.RowCenterY - (fallbackHeight / 2.0);
                    row.ShapeCellMaxY = row.RowCenterY + (fallbackHeight / 2.0);
                    row.RowBandHeight = fallbackHeight;
                    row.ShapeCellBoundsSource = "LAST_FALLBACK";
                }

                if (Math.Abs(previousMinX - row.ShapeCellMinX) > 0.0001
                    || Math.Abs(previousMaxX - row.ShapeCellMaxX) > 0.0001
                    || Math.Abs(previousMinY - row.ShapeCellMinY) > 0.0001
                    || Math.Abs(previousMaxY - row.ShapeCellMaxY) > 0.0001
                    || !String.Equals(previousBoundsSource, row.ShapeCellBoundsSource, StringComparison.OrdinalIgnoreCase))
                {
                    recoveredCount++;
                }
            }

            return recoveredCount;
        }

        private List<double> GetVerifiedPhysicalTableHorizontalYs(
            List<OviaGridLineSegment> gridLines,
            double selectedMinX,
            double selectedMaxX,
            double averageTextHeight)
        {
            double tableWidth = Math.Max(selectedMaxX - selectedMinX, 0.0001);

            if (gridLines == null || gridLines.Count == 0 || tableWidth <= 0.0001)
            {
                return new List<double>();
            }

            double axisTolerance = Math.Max(Math.Max(averageTextHeight * 0.12, tableWidth * 0.00025), 0.03);
            double mergeTolerance = Math.Max(Math.Max(averageTextHeight * 0.22, tableWidth * 0.00045), 0.05);
            List<double> result = ExtractCoveredGridCoordinates(
                gridLines,
                false,
                axisTolerance,
                mergeTolerance,
                Math.Max(tableWidth * 0.025, 0.20),
                tableWidth * 0.72,
                selectedMinX,
                selectedMaxX
            );

            result.Sort();
            return result;
        }

        private double GetTypicalPhysicalShapeRowHeight(
            List<OviaBarTableRow> rows,
            List<double> verifiedHorizontalYs,
            double averageTextHeight)
        {
            List<double> heights = new List<double>();
            int i;

            if (rows != null)
            {
                for (i = 0; i < rows.Count; i++)
                {
                    OviaBarTableRow row = rows[i];

                    if (row == null || !row.HasShapeCellBounds())
                    {
                        continue;
                    }

                    if (!String.Equals(row.ShapeCellBoundsSource, "GRID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double height = Math.Abs(row.ShapeCellMaxY - row.ShapeCellMinY);

                    if (height >= Math.Max(averageTextHeight * 1.05, 0.10))
                    {
                        heights.Add(height);
                    }
                }
            }

            if (heights.Count > 0)
            {
                return GetMedianCadGridValue(heights);
            }

            if (verifiedHorizontalYs != null && verifiedHorizontalYs.Count > 1)
            {
                for (i = 1; i < verifiedHorizontalYs.Count; i++)
                {
                    double gap = verifiedHorizontalYs[i] - verifiedHorizontalYs[i - 1];

                    if (gap >= Math.Max(averageTextHeight * 1.05, 0.10))
                    {
                        heights.Add(gap);
                    }
                }
            }

            return heights.Count == 0 ? 0.0 : GetMedianCadGridValue(heights);
        }

        private bool TryApplyVerifiedPhysicalGridShapeRowBounds(
            OviaBarTableRow row,
            List<double> verifiedHorizontalYs,
            double averageTextHeight,
            double typicalPhysicalRowHeight)
        {
            if (row == null || verifiedHorizontalYs == null || verifiedHorizontalYs.Count < 2)
            {
                return false;
            }

            double centerY = row.RowCenterY;
            double lowerY = Double.MinValue;
            double upperY = Double.MaxValue;
            int i;

            for (i = 0; i < verifiedHorizontalYs.Count; i++)
            {
                double y = verifiedHorizontalYs[i];

                if (y < centerY - 0.0001 && y > lowerY)
                {
                    lowerY = y;
                }

                if (y > centerY + 0.0001 && y < upperY)
                {
                    upperY = y;
                }
            }

            if (lowerY == Double.MinValue || upperY == Double.MaxValue || upperY <= lowerY)
            {
                return false;
            }

            double height = upperY - lowerY;
            double minimumHeight = Math.Max(averageTextHeight * 1.05, 0.10);

            if (height < minimumHeight)
            {
                return false;
            }

            if (typicalPhysicalRowHeight > 0.0001 && height > typicalPhysicalRowHeight * 1.80)
            {
                return false;
            }

            row.ShapeCellMinY = lowerY;
            row.ShapeCellMaxY = upperY;
            row.RowBandHeight = height;
            row.ShapeCellBoundsSource = "GRID_RECOVERED";
            return true;
        }

        private bool HasInternalVerifiedHorizontalGridLine(
            double minY,
            double maxY,
            List<double> verifiedHorizontalYs,
            double averageTextHeight)
        {
            if (verifiedHorizontalYs == null || verifiedHorizontalYs.Count == 0)
            {
                return false;
            }

            double lowerY = Math.Min(minY, maxY);
            double upperY = Math.Max(minY, maxY);
            double edgeTolerance = Math.Max(averageTextHeight * 0.18, 0.05);
            int i;

            for (i = 0; i < verifiedHorizontalYs.Count; i++)
            {
                double y = verifiedHorizontalYs[i];

                if (y > lowerY + edgeTolerance && y < upperY - edgeTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool DoesShapeCellBandContainSummaryCenter(
            double minY,
            double maxY,
            double rowCenterY,
            List<double> summaryRowCenters,
            double averageTextHeight)
        {
            if (summaryRowCenters == null || summaryRowCenters.Count == 0)
            {
                return false;
            }

            double lowerY = Math.Min(minY, maxY);
            double upperY = Math.Max(minY, maxY);
            double margin = Math.Max(averageTextHeight * 0.20, 0.05);
            int i;

            for (i = 0; i < summaryRowCenters.Count; i++)
            {
                double summaryY = summaryRowCenters[i];

                if (Math.Abs(summaryY - rowCenterY) <= margin)
                {
                    continue;
                }

                if (summaryY > lowerY + margin && summaryY < upperY - margin)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClampCoordinateShapeCellBoundsToSummaryBarriers(
            OviaBarTableRow row,
            List<double> summaryRowCenters,
            double referenceHeight,
            double averageTextHeight)
        {
            if (row == null || !row.HasShapeCellBounds())
            {
                return;
            }

            double centerY = row.RowCenterY;
            double lowerY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
            double upperY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
            int i;

            if (summaryRowCenters != null)
            {
                for (i = 0; i < summaryRowCenters.Count; i++)
                {
                    double summaryY = summaryRowCenters[i];

                    if (summaryY > centerY && summaryY < upperY)
                    {
                        upperY = Math.Min(upperY, (centerY + summaryY) / 2.0);
                    }
                    else if (summaryY < centerY && summaryY > lowerY)
                    {
                        lowerY = Math.Max(lowerY, (centerY + summaryY) / 2.0);
                    }
                }
            }

            double maximumHeight = Math.Max(referenceHeight * 1.35, averageTextHeight * 2.0);

            if (upperY - lowerY > maximumHeight)
            {
                upperY = centerY + maximumHeight / 2.0;
                lowerY = centerY - maximumHeight / 2.0;
            }

            if (upperY > lowerY)
            {
                row.ShapeCellMinY = lowerY;
                row.ShapeCellMaxY = upperY;
                row.RowBandHeight = upperY - lowerY;
                row.ShapeCellBoundsSource = "COORDINATE_SUMMARY_BARRIER";
            }
        }

        private OviaHeaderColumn SelectBestRecoveredShapeColumn(
            OviaHeaderColumn parsedShapeColumn,
            OviaHeaderColumn detectedShapeColumn,
            List<OviaTextRow> textRows,
            List<OviaBarTableRow> rows)
        {
            bool parsedValid = parsedShapeColumn != null && parsedShapeColumn.RightX > parsedShapeColumn.LeftX;
            bool detectedValid = detectedShapeColumn != null && detectedShapeColumn.RightX > detectedShapeColumn.LeftX;

            if (!parsedValid)
            {
                return detectedValid ? detectedShapeColumn : null;
            }

            if (!detectedValid)
            {
                return parsedShapeColumn;
            }

            double sameCenterTolerance = Math.Max(
                Math.Min(
                    parsedShapeColumn.RightX - parsedShapeColumn.LeftX,
                    detectedShapeColumn.RightX - detectedShapeColumn.LeftX
                ) * 0.10,
                0.25
            );

            if (Math.Abs(parsedShapeColumn.X - detectedShapeColumn.X) <= sameCenterTolerance)
            {
                return parsedShapeColumn;
            }

            int parsedScore = ScoreRecoveredShapeColumn(parsedShapeColumn, textRows, rows);
            int detectedScore = ScoreRecoveredShapeColumn(detectedShapeColumn, textRows, rows);

            return detectedScore > parsedScore ? detectedShapeColumn : parsedShapeColumn;
        }

        private int ScoreRecoveredShapeColumn(
            OviaHeaderColumn column,
            List<OviaTextRow> textRows,
            List<OviaBarTableRow> rows)
        {
            if (column == null || column.RightX <= column.LeftX || textRows == null || rows == null)
            {
                return Int32.MinValue;
            }

            int score = 0;
            int rowIndex;

            for (rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                OviaBarTableRow row = rows[rowIndex];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double minY = row.HasShapeCellBounds()
                    ? Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY)
                    : row.RowCenterY - Math.Max(row.RowBandHeight / 2.0, 0.5);
                double maxY = row.HasShapeCellBounds()
                    ? Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY)
                    : row.RowCenterY + Math.Max(row.RowBandHeight / 2.0, 0.5);
                double rowMargin = Math.Max((maxY - minY) * 0.08, 0.20);
                bool foundCandidateText = false;
                int textIndex;

                for (textIndex = 0; textIndex < textRows.Count; textIndex++)
                {
                    OviaTextRow textRow = textRows[textIndex];

                    if (textRow == null
                        || textRow.X < column.LeftX
                        || textRow.X > column.RightX
                        || textRow.Y < minY - rowMargin
                        || textRow.Y > maxY + rowMargin)
                    {
                        continue;
                    }

                    string value = CleanCellText(textRow.TextValue);

                    if (value == "" || IsHeaderRow(value) || IsSummaryText(value))
                    {
                        continue;
                    }

                    foundCandidateText = true;

                    if (ShapeRawTextContainsNumericValue(row.ShapeRawText, value))
                    {
                        score += 8;
                    }

                    decimal numericValue;

                    if (TryParseDecimalText(value, out numericValue))
                    {
                        if (Math.Abs(numericValue) >= 10M)
                        {
                            score += 2;
                        }
                        else if (Math.Abs(numericValue) > 0M && Math.Abs(numericValue) < 1M)
                        {
                            score -= 4;
                        }

                        if (IsSameRecoveredShapeCompareValue(numericValue, row.TotalLength)
                            || IsSameRecoveredShapeCompareValue(numericValue, row.TotalWeight))
                        {
                            score -= 12;
                        }
                    }
                    else
                    {
                        score += 1;
                    }
                }

                if (foundCandidateText)
                {
                    score += 1;
                }

                if (row.HasShapeCellBounds())
                {
                    double currentCenterX = (row.ShapeCellMinX + row.ShapeCellMaxX) / 2.0;
                    double columnWidth = Math.Max(column.RightX - column.LeftX, 0.0001);

                    if (Math.Abs(currentCenterX - column.X) <= columnWidth * 0.25)
                    {
                        score += 2;
                    }
                }
            }

            return score;
        }

        private bool ShapeRawTextContainsNumericValue(string shapeRawText, string value)
        {
            decimal target;

            if (!TryParseDecimalText(value, out target) || shapeRawText == null || shapeRawText.Trim() == "")
            {
                return false;
            }

            MatchCollection matches = GetExpectedCadShapeDimensionMatches(shapeRawText);
            int i;

            for (i = 0; i < matches.Count; i++)
            {
                decimal candidate;

                if (TryParseDecimalText(matches[i].Value, out candidate)
                    && AreDecimalValuesEqualAtThreeDecimals(candidate, target))
                {
                    return true;
                }
            }

            return false;
        }

        private MatchCollection GetExpectedCadShapeDimensionMatches(string shapeRawText)
        {
            return Regex.Matches(
                shapeRawText == null ? "" : shapeRawText,
                @"(?<![A-Za-z0-9])-?\d+(?:,\d{3})*(?:\.\d+)?(?![A-Za-z0-9])"
            );
        }

        private bool IsSameRecoveredShapeCompareValue(decimal candidate, string compareText)
        {
            decimal compareValue;

            return TryParseDecimalText(compareText, out compareValue)
                && AreDecimalValuesEqualAtThreeDecimals(candidate, compareValue);
        }

        private OviaHeaderColumn GetRecoveredShapeColumnFromExistingGridRows(
            List<OviaBarTableRow> rows,
            double selectedMinX,
            double selectedMaxX)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            List<double> leftXs = new List<double>();
            List<double> rightXs = new List<double>();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null
                    || !row.HasShapeCellBounds()
                    || !String.Equals(row.ShapeCellBoundsSource, "GRID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double leftX = Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX);
                double rightX = Math.Max(row.ShapeCellMinX, row.ShapeCellMaxX);

                if (rightX <= leftX
                    || leftX < selectedMinX - 0.5
                    || rightX > selectedMaxX + 0.5)
                {
                    continue;
                }

                leftXs.Add(leftX);
                rightXs.Add(rightX);
            }

            if (leftXs.Count == 0 || rightXs.Count == 0)
            {
                return null;
            }

            double leftMedian = GetMedianCadGridValue(leftXs);
            double rightMedian = GetMedianCadGridValue(rightXs);

            if (rightMedian <= leftMedian)
            {
                return null;
            }

            OviaHeaderColumn result = new OviaHeaderColumn();
            result.StandardKey = "SHAPE";
            result.OriginalTitle = "철근형상";
            result.LeftX = leftMedian;
            result.RightX = rightMedian;
            result.X = (leftMedian + rightMedian) / 2.0;
            result.SourceColumnIndex = -1;
            return result;
        }

        private OviaHeaderColumn RefineRecoveredShapeColumnWithPhysicalGridLines(
            OviaHeaderColumn shapeColumn,
            List<OviaGridLineSegment> gridLines,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint)
        {
            if (shapeColumn == null || shapeColumn.RightX <= shapeColumn.LeftX
                || gridLines == null || gridLines.Count == 0)
            {
                return shapeColumn;
            }

            double minX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double maxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double minY = Math.Min(selectedMinPoint.Y, selectedMaxPoint.Y);
            double maxY = Math.Max(selectedMinPoint.Y, selectedMaxPoint.Y);
            double width = Math.Max(maxX - minX, 0.0001);
            double height = Math.Max(maxY - minY, 0.0001);
            double originalLeftX = Math.Min(shapeColumn.LeftX, shapeColumn.RightX);
            double originalRightX = Math.Max(shapeColumn.LeftX, shapeColumn.RightX);
            double originalWidth = Math.Max(originalRightX - originalLeftX, 0.0001);
            double axisTolerance = Math.Max(Math.Min(width, height) * 0.002, 0.25);
            double mergeTolerance = Math.Max(Math.Min(width, height) * 0.003, 0.50);

            /*
             * 내부 U형/사각형 수직선은 여러 DATA 행에서 반복될 수 있지만 표의 세로 GRID처럼
             * 선택 높이 대부분을 연속 관통하지는 않습니다. 30% 커버리지를 사용하던 기존 조건을
             * 72%로 높이고, 열 중심에서 가장 가까운 선이 아니라 기존 헤더 경계에 가까운 선만
             * 스냅 후보로 허용합니다.
             */
            List<double> verticalXs = ExtractCoveredGridCoordinates(
                gridLines,
                true,
                axisTolerance,
                mergeTolerance,
                Math.Max(height * 0.020, 0.5),
                height * 0.72,
                minY,
                maxY
            );

            double snapTolerance = Math.Max(originalWidth * 0.18, mergeTolerance * 2.0);
            double leftX = Double.NaN;
            double rightX = Double.NaN;
            double leftDistance = Double.MaxValue;
            double rightDistance = Double.MaxValue;
            int i;

            for (i = 0; i < verticalXs.Count; i++)
            {
                double x = verticalXs[i];

                if (x < minX - mergeTolerance || x > maxX + mergeTolerance)
                {
                    continue;
                }

                double distanceToLeft = Math.Abs(x - originalLeftX);

                if (distanceToLeft <= snapTolerance && distanceToLeft < leftDistance)
                {
                    leftDistance = distanceToLeft;
                    leftX = x;
                }

                double distanceToRight = Math.Abs(x - originalRightX);

                if (distanceToRight <= snapTolerance && distanceToRight < rightDistance)
                {
                    rightDistance = distanceToRight;
                    rightX = x;
                }
            }

            if (Double.IsNaN(leftX) || Double.IsNaN(rightX) || rightX <= leftX)
            {
                return shapeColumn;
            }

            double refinedWidth = rightX - leftX;
            double refinedCenter = (leftX + rightX) / 2.0;
            double originalCenter = (originalLeftX + originalRightX) / 2.0;

            /*
             * 물리 GRID 스냅은 원래 헤더 열을 미세 보정하는 용도이지 열 폭을 새로 추론하는
             * 용도가 아닙니다. 폭이 25% 이상 축소/확대되거나 중심이 크게 이동하면 내부 형상선을
             * 집은 것으로 보고 원래 헤더 경계를 유지합니다.
             */
            if (refinedWidth < originalWidth * 0.75
                || refinedWidth > originalWidth * 1.25
                || Math.Abs(refinedCenter - originalCenter) > originalWidth * 0.12)
            {
                return shapeColumn;
            }

            OviaHeaderColumn refined = new OviaHeaderColumn();
            refined.StandardKey = shapeColumn.StandardKey;
            refined.OriginalTitle = shapeColumn.OriginalTitle;
            refined.HeaderTextVerified = shapeColumn.HeaderTextVerified;
            refined.X = refinedCenter;
            refined.LeftX = leftX;
            refined.RightX = rightX;
            refined.SourceColumnIndex = shapeColumn.SourceColumnIndex;
            return refined;
        }

        private List<OviaBarTableRow> BuildSpecAnchoredBarTableRows(
            List<OviaTextRow> selectedTextRows,
            List<OviaTextRow> analysisTextRows,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            out string diagnostic)
        {
            diagnostic = "";
            List<OviaBarTableRow> result = new List<OviaBarTableRow>();

            if (selectedTextRows == null || selectedTextRows.Count == 0)
            {
                return result;
            }

            double selectedMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double selectedMinY = Math.Min(selectedMinPoint.Y, selectedMaxPoint.Y);
            double selectedMaxY = Math.Max(selectedMinPoint.Y, selectedMaxPoint.Y);
            double selectedWidth = Math.Max(selectedMaxX - selectedMinX, 0.0001);
            double xMargin = Math.Max(selectedWidth * 0.025, 0.5);
            List<OviaTextRow> tableTextRows = new List<OviaTextRow>();
            List<OviaTextRow> headerSourceRows = analysisTextRows == null || analysisTextRows.Count == 0
                ? selectedTextRows
                : analysisTextRows;
            int i;

            for (i = 0; i < headerSourceRows.Count; i++)
            {
                OviaTextRow textRow = headerSourceRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.X < selectedMinX - xMargin || textRow.X > selectedMaxX + xMargin)
                {
                    continue;
                }

                tableTextRows.Add(textRow);
            }

            if (tableTextRows.Count == 0)
            {
                return result;
            }

            OviaHeaderMap headerMap = null;
            OviaHeaderMap relaxedHeaderMap = null;

            try
            {
                headerMap = DetectHeaderMap(GroupRowsByY(new List<OviaTextRow>(tableTextRows)), tableTextRows);
            }
            catch
            {
                headerMap = null;
            }

            try
            {
                relaxedHeaderMap = DetectRelaxedCurrentTableHeaderMap(
                    tableTextRows,
                    selectedMinX,
                    selectedMaxX,
                    selectedMaxY
                );
            }
            catch
            {
                relaxedHeaderMap = null;
            }

            /*
             * 실제 52~77 + 소계/총계 회귀:
             * CAD 헤더의 번호·규격·형상·길이·수량 문자는 같은 표 행에 보이더라도 DBText의
             * 기준 Y가 서로 달라 0.85×문자높이 그룹에서 둘 이상으로 갈라질 수 있습니다.
             * grid 오정렬을 차단한 뒤 기존 규격 앵커 fallback도 이 엄격한 headerMap을 요구해
             * 표준 헤더를 찾지 못하고 0행으로 종료했습니다.
             *
             * 현재 선택 표 X 안에서만 헤더 후보를 모으고, 문자높이 5배 이내의 느슨한 헤더 밴드로
             * 필수 물리 열을 모두 확인한 경우 해당 맵을 우선 사용합니다. 인접 표의 문자는 이미
             * tableTextRows 생성 단계에서 제외되므로 다른 표 스키마가 다시 섞이지 않습니다.
             */
            if (HasSpecAnchoredRequiredHeaderColumns(relaxedHeaderMap))
            {
                headerMap = relaxedHeaderMap;
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "현재 표의 느슨한 물리 헤더 밴드로 번호·규격·형상·길이·수량 열을 복구했습니다."
                );
            }

            if (!HasSpecAnchoredRequiredHeaderColumns(headerMap))
            {
                diagnostic = "규격 기준 최종 복구에서 표준 헤더를 확인하지 못했습니다.";
                return result;
            }

            List<OviaHeaderColumn> columns = CloneHeaderColumns(headerMap.Columns);
            OviaHeaderColumn specColumn = FindHeaderColumnByKey(columns, "SPEC");
            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");
            OviaHeaderColumn lengthColumn = FindHeaderColumnByKey(columns, "LENGTH_MM");
            OviaHeaderColumn qtyColumn = FindHeaderColumnByKey(columns, "QUANTITY_EA");
            OviaHeaderColumn markColumn = DetectMarkColumnDirectlyFromHeaderText(tableTextRows);

            if (markColumn == null)
            {
                markColumn = NormalizeTextHeaderMarkColumn(FindHeaderColumnByKey(columns, "MARK_NO"));
            }

            if (specColumn == null || shapeColumn == null || lengthColumn == null || qtyColumn == null || markColumn == null)
            {
                diagnostic = "규격 기준 최종 복구에 필요한 번호·형상·규격·길이·수량 헤더가 부족합니다.";
                return result;
            }

            double averageTextHeight = GetAverageTextHeight(tableTextRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = 1.0;
            }

            double specXMargin = Math.Max((specColumn.RightX - specColumn.LeftX) * 0.08, xMargin * 0.20);
            double yMargin = Math.Max(averageTextHeight * 0.80, 0.25);
            List<OviaTextRow> specAnchors = new List<OviaTextRow>();

            for (i = 0; i < selectedTextRows.Count; i++)
            {
                OviaTextRow textRow = selectedTextRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.Y < selectedMinY - yMargin || textRow.Y > selectedMaxY + yMargin)
                {
                    continue;
                }

                if (textRow.X < specColumn.LeftX - specXMargin || textRow.X > specColumn.RightX + specXMargin)
                {
                    continue;
                }

                if (DetectSpec(CleanCellText(textRow.TextValue)) == "")
                {
                    continue;
                }

                specAnchors.Add(textRow);
            }

            specAnchors.Sort(delegate (OviaTextRow a, OviaTextRow b)
            {
                return b.Y.CompareTo(a.Y);
            });

            specAnchors = MergeSpecAnchorsByRow(specAnchors, specColumn.X, averageTextHeight);

            if (specAnchors.Count == 0)
            {
                diagnostic = "선택 영역에서 실제 철근 규격 행을 찾지 못했습니다.";
                return result;
            }

            /*
             * OVIA 2026-07-21 소계/총계 행 완전 분리:
             * 58과 66 사이처럼 규격이 없는 소계 행이 끼면 두 규격 앵커 간격만 두 배가 됩니다.
             * 전체 간격의 산술평균을 쓰면 이 한 개의 큰 간격이 모든 DATA 행 높이를 넓혀 소계의
             * 352.04/0.351 및 표선을 58·66 행으로 끌어왔습니다. 정상 간격의 중앙값을 대표 행
             * 높이로 사용하고, 큰 간격은 양쪽 DATA 중심에서 각각 반 행까지만 사용합니다.
             */
            double typicalRowGap = GetTypicalSpecAnchorGap(specAnchors, averageTextHeight);
            double valueTolerance = Math.Max(averageTextHeight * 0.50, 0.25);
            List<double> summaryRowCenters = GetSummaryRowCenters(
                tableTextRows,
                selectedMinY - yMargin,
                selectedMaxY + yMargin,
                averageTextHeight
            );

            for (i = 0; i < specAnchors.Count; i++)
            {
                OviaTextRow anchor = specAnchors[i];
                double centerY = anchor.Y;
                double upperAnchorY = i == 0
                    ? centerY + typicalRowGap
                    : specAnchors[i - 1].Y;
                double lowerAnchorY = i == specAnchors.Count - 1
                    ? centerY - typicalRowGap
                    : specAnchors[i + 1].Y;
                double topY = GetSpecAnchorRowBoundary(
                    centerY,
                    upperAnchorY,
                    true,
                    typicalRowGap,
                    summaryRowCenters
                );
                double bottomY = GetSpecAnchorRowBoundary(
                    centerY,
                    lowerAnchorY,
                    false,
                    typicalRowGap,
                    summaryRowCenters
                );

                topY = Math.Min(topY, selectedMaxY + yMargin);
                bottomY = Math.Max(bottomY, selectedMinY - yMargin);

                if (topY <= bottomY)
                {
                    topY = centerY + (typicalRowGap / 2.0);
                    bottomY = centerY - (typicalRowGap / 2.0);
                }

                /*
                 * 한 DATA 행의 값은 규격 앵커 중심에 가까운 문자만 사용합니다. 이 필터는 소계라는
                 * 라벨을 못 읽는 도면에서도 규격이 없는 중간 행의 숫자를 DATA 값으로 승격시키지
                 * 않습니다. 요약 라벨과 같은 Y에 있는 숫자·문자는 라벨 위치와 함께 전부 제외합니다.
                 */
                List<OviaTextRow> rowTextRows = GetSpecAnchorDataTextRows(
                    tableTextRows,
                    centerY,
                    typicalRowGap,
                    averageTextHeight,
                    summaryRowCenters
                );

                OviaBarTableRow row = new OviaBarTableRow();
                row.No = result.Count + 1;
                row.SourceRowNo = i + 1;
                row.RowType = "DATA";
                row.RowCenterY = centerY;
                row.RowBandHeight = Math.Abs(topY - bottomY);
                row.ShapeCellMinX = shapeColumn.LeftX;
                row.ShapeCellMaxX = shapeColumn.RightX;
                row.ShapeCellMinY = bottomY;
                row.ShapeCellMaxY = topY;
                row.ShapeCellBoundsSource = "SPEC_ANCHOR";

                int textIndex;

                for (textIndex = 0; textIndex < rowTextRows.Count; textIndex++)
                {
                    OviaTextRow textRow = rowTextRows[textIndex];

                    if (textRow == null || textRow.Y < bottomY - valueTolerance || textRow.Y > topY + valueTolerance)
                    {
                        continue;
                    }

                    if (textRow.X < selectedMinX - xMargin || textRow.X > selectedMaxX + xMargin)
                    {
                        continue;
                    }

                    string value = CleanCellText(textRow.TextValue);

                    if (value == "" || IsHeaderRow(value) || IsSummaryText(value))
                    {
                        continue;
                    }

                    OviaHeaderColumn column = FindHeaderColumnByX(columns, textRow.X);

                    if (column != null)
                    {
                        ApplyValueByStandardKey(row, column.StandardKey, value);
                    }
                }

                row.RawText = JoinGridRowBandTextInSelectedRange(
                    rowTextRows,
                    topY,
                    bottomY,
                    selectedMinX,
                    selectedMaxX,
                    valueTolerance
                );
                row.Spec = DetectSpec(CleanCellText(anchor.TextValue));

                string recoveredMark = FindMarkNumberInPhysicalColumn(
                    rowTextRows,
                    markColumn,
                    bottomY - valueTolerance,
                    topY + valueTolerance,
                    centerY
                );

                if (recoveredMark != "")
                {
                    row.MarkNo = recoveredMark;
                    row.BarNo = recoveredMark;
                }

                RecoverGridRowValuesByHeaderBounds(
                    rowTextRows,
                    row,
                    columns,
                    topY,
                    bottomY,
                    valueTolerance
                );

                if (row.RawText != "")
                {
                    SupplementGridDataFromSpecAnchoredText(row.RawText, row, columns);
                    ApplyGridWeightAndNoteCorrection(
                        rowTextRows,
                        row,
                        columns,
                        topY,
                        bottomY,
                        valueTolerance
                    );
                }

                NormalizeBarTableNumericValues(row);

                if (IsActualRebarDataRow(row))
                {
                    result.Add(row);
                }
            }

            for (i = 0; i < result.Count; i++)
            {
                result[i].No = i + 1;
            }

            lastDetectedHeaderColumns = CloneHeaderColumns(columns);
            diagnostic = "규격 기준 최종 복구로 소계·총계를 제외하고 실제 철근 "
                + result.Count.ToString(CultureInfo.InvariantCulture)
                + "개 행을 구성했습니다.";
            return result;
        }

        private bool HasSpecAnchoredRequiredHeaderColumns(OviaHeaderMap headerMap)
        {
            if (headerMap == null || headerMap.Columns == null)
            {
                return false;
            }

            return FindHeaderColumnByKey(headerMap.Columns, "MARK_NO") != null
                && FindHeaderColumnByKey(headerMap.Columns, "SPEC") != null
                && FindHeaderColumnByKey(headerMap.Columns, "SHAPE") != null
                && FindHeaderColumnByKey(headerMap.Columns, "LENGTH_MM") != null
                && FindHeaderColumnByKey(headerMap.Columns, "QUANTITY_EA") != null;
        }

        private OviaHeaderMap DetectRelaxedCurrentTableHeaderMap(
            List<OviaTextRow> textRows,
            double selectedMinX,
            double selectedMaxX,
            double selectedTopY)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return null;
            }

            double averageTextHeight = GetAverageTextHeight(textRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = 1.0;
            }

            double yTolerance = Math.Max(averageTextHeight * 5.0, 2.0);
            List<OviaTextRow> headerCandidates = new List<OviaTextRow>();
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                string key = ClassifyHeaderTitle(CleanHeaderText(textRow.TextValue));

                if (key != "")
                {
                    headerCandidates.Add(textRow);
                }
            }

            if (headerCandidates.Count == 0)
            {
                return null;
            }

            OviaHeaderMap bestMap = null;
            int bestRequiredCount = -1;
            int bestScore = -1;
            double bestDistance = Double.MaxValue;

            for (i = 0; i < headerCandidates.Count; i++)
            {
                double bandY = headerCandidates[i].Y;
                List<OviaHeaderColumn> columns = new List<OviaHeaderColumn>();
                double totalY = 0;
                int bandCount = 0;
                int j;

                for (j = 0; j < headerCandidates.Count; j++)
                {
                    OviaTextRow candidate = headerCandidates[j];

                    if (Math.Abs(candidate.Y - bandY) > yTolerance)
                    {
                        continue;
                    }

                    string title = CleanHeaderText(candidate.TextValue);
                    string key = ClassifyHeaderTitle(title);

                    if (key == "")
                    {
                        continue;
                    }

                    OviaHeaderColumn existing = FindHeaderColumnByKey(columns, key);

                    if (existing == null)
                    {
                        OviaHeaderColumn column = new OviaHeaderColumn();
                        column.StandardKey = key;
                        column.OriginalTitle = NormalizeHeaderTitleForOutput(title, key);
                        column.HeaderTextVerified = true;
                        column.X = candidate.X;
                        columns.Add(column);
                    }
                    totalY += candidate.Y;
                    bandCount++;
                }

                if (columns.Count == 0 || bandCount == 0)
                {
                    continue;
                }

                columns.Sort(delegate (OviaHeaderColumn left, OviaHeaderColumn right)
                {
                    return left.X.CompareTo(right.X);
                });
                ApplyHeaderColumnBounds(columns, selectedMinX, selectedMaxX);

                OviaHeaderMap map = new OviaHeaderMap();
                map.HeaderRowIndex = -1;
                map.Columns = columns;
                map.MinX = selectedMinX;
                map.MaxX = selectedMaxX;

                int requiredCount = GetSpecAnchoredRequiredHeaderColumnCount(columns);
                int score = GetHeaderScore(columns);
                double distance = Math.Abs((totalY / (double)bandCount) - selectedTopY);

                if (requiredCount > bestRequiredCount
                    || (requiredCount == bestRequiredCount && score > bestScore)
                    || (requiredCount == bestRequiredCount && score == bestScore && distance < bestDistance))
                {
                    bestMap = map;
                    bestRequiredCount = requiredCount;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            return bestMap;
        }

        private int GetSpecAnchoredRequiredHeaderColumnCount(List<OviaHeaderColumn> columns)
        {
            if (columns == null)
            {
                return 0;
            }

            int count = 0;
            if (FindHeaderColumnByKey(columns, "MARK_NO") != null) count++;
            if (FindHeaderColumnByKey(columns, "SPEC") != null) count++;
            if (FindHeaderColumnByKey(columns, "SHAPE") != null) count++;
            if (FindHeaderColumnByKey(columns, "LENGTH_MM") != null) count++;
            if (FindHeaderColumnByKey(columns, "QUANTITY_EA") != null) count++;
            return count;
        }

        private List<OviaTextRow> MergeSpecAnchorsByRow(
            List<OviaTextRow> anchors,
            double specColumnCenterX,
            double averageTextHeight)
        {
            List<OviaTextRow> merged = new List<OviaTextRow>();

            if (anchors == null || anchors.Count == 0)
            {
                return merged;
            }

            double tolerance = Math.Max(averageTextHeight * 0.85, 0.25);
            int index = 0;

            while (index < anchors.Count)
            {
                OviaTextRow best = anchors[index];
                double baseY = anchors[index].Y;
                int next = index + 1;

                while (next < anchors.Count && Math.Abs(anchors[next].Y - baseY) <= tolerance)
                {
                    if (Math.Abs(anchors[next].X - specColumnCenterX) < Math.Abs(best.X - specColumnCenterX))
                    {
                        best = anchors[next];
                    }

                    next++;
                }

                merged.Add(best);
                index = next;
            }

            return merged;
        }

        private double GetTypicalSpecAnchorGap(List<OviaTextRow> anchors, double averageTextHeight)
        {
            List<double> gaps = new List<double>();

            if (anchors != null)
            {
                int i;

                for (i = 1; i < anchors.Count; i++)
                {
                    double gap = Math.Abs(anchors[i - 1].Y - anchors[i].Y);

                    if (gap > 0.0001)
                    {
                        gaps.Add(gap);
                    }
                }
            }

            if (gaps.Count > 0)
            {
                gaps.Sort();
                int middle = gaps.Count / 2;

                if ((gaps.Count % 2) == 1)
                {
                    return gaps[middle];
                }

                return (gaps[middle - 1] + gaps[middle]) / 2.0;
            }

            return Math.Max(averageTextHeight * 3.0, 1.0);
        }

        private List<double> GetSummaryRowCenters(
            List<OviaTextRow> textRows,
            double minY,
            double maxY,
            double averageTextHeight)
        {
            List<double> centers = new List<double>();

            if (textRows == null || textRows.Count == 0)
            {
                return centers;
            }

            double mergeTolerance = Math.Max(averageTextHeight * 0.85, 0.25);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null || textRow.Y < minY || textRow.Y > maxY)
                {
                    continue;
                }

                if (!IsSummaryText(CleanCellText(textRow.TextValue)))
                {
                    continue;
                }

                bool duplicate = false;
                int centerIndex;

                for (centerIndex = 0; centerIndex < centers.Count; centerIndex++)
                {
                    if (Math.Abs(centers[centerIndex] - textRow.Y) <= mergeTolerance)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    centers.Add(textRow.Y);
                }
            }

            centers.Sort();
            return centers;
        }

        private double GetSpecAnchorRowBoundary(
            double centerY,
            double adjacentAnchorY,
            bool upperBoundary,
            double typicalRowGap,
            List<double> summaryRowCenters)
        {
            double halfGap = Math.Max(typicalRowGap / 2.0, 0.5);
            double actualGap = Math.Abs(adjacentAnchorY - centerY);
            double boundary = (centerY + adjacentAnchorY) / 2.0;

            /*
             * 규격 앵커 간격이 정상 간격의 1.55배를 넘으면 사이에 규격 없는 행이 있다고 봅니다.
             * 행 종류를 추정해 그 내용을 이웃 DATA에 나눠 넣지 않고 중간 구간 전체를 비웁니다.
             */
            if (actualGap > typicalRowGap * 1.55)
            {
                boundary = upperBoundary ? centerY + halfGap : centerY - halfGap;
            }

            if (summaryRowCenters == null || summaryRowCenters.Count == 0)
            {
                return boundary;
            }

            double nearestSummary = 0;
            double nearestDistance = Double.MaxValue;
            int i;

            for (i = 0; i < summaryRowCenters.Count; i++)
            {
                double summaryY = summaryRowCenters[i];
                bool isBetween = upperBoundary
                    ? summaryY > centerY && summaryY <= adjacentAnchorY
                    : summaryY < centerY && summaryY >= adjacentAnchorY;

                if (!isBetween)
                {
                    continue;
                }

                double distance = Math.Abs(summaryY - centerY);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestSummary = summaryY;
                }
            }

            if (nearestDistance < Double.MaxValue && nearestDistance <= typicalRowGap * 2.5)
            {
                boundary = (centerY + nearestSummary) / 2.0;
            }

            return boundary;
        }

        private List<OviaTextRow> GetSpecAnchorDataTextRows(
            List<OviaTextRow> textRows,
            double centerY,
            double typicalRowGap,
            double averageTextHeight,
            List<double> summaryRowCenters)
        {
            List<OviaTextRow> result = new List<OviaTextRow>();

            if (textRows == null || textRows.Count == 0)
            {
                return result;
            }

            double dataHalfBand = Math.Max(typicalRowGap * 0.44, averageTextHeight * 1.25);
            dataHalfBand = Math.Min(dataHalfBand, typicalRowGap * 0.49);
            double summaryTolerance = Math.Max(averageTextHeight * 1.25, typicalRowGap * 0.20);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null || Math.Abs(textRow.Y - centerY) > dataHalfBand)
                {
                    continue;
                }

                bool belongsToSummary = false;
                int summaryIndex;

                for (summaryIndex = 0; summaryRowCenters != null && summaryIndex < summaryRowCenters.Count; summaryIndex++)
                {
                    if (Math.Abs(textRow.Y - summaryRowCenters[summaryIndex]) <= summaryTolerance)
                    {
                        belongsToSummary = true;
                        break;
                    }
                }

                if (!belongsToSummary)
                {
                    result.Add(textRow);
                }
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

        private List<OviaBarTableRow> FilterBarTableRowsBySelectionRectangles(
            List<OviaBarTableRow> rows,
            List<OviaSelectionRectangle> allowedRectangles,
            out int filteredRowCount)
        {
            filteredRowCount = 0;

            if (rows == null)
            {
                return new List<OviaBarTableRow>();
            }

            if (allowedRectangles == null || allowedRectangles.Count == 0)
            {
                return rows;
            }

            List<OviaBarTableRow> filtered = new List<OviaBarTableRow>();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null)
                {
                    continue;
                }

                /*
                 * 선택영역 중복은 철근 번호가 아니라 CAD의 실제 행 위치로만 판정합니다.
                 * 표 선 기반 파서는 철근형상 셀 Y 범위를, 좌표/규격 앵커 fallback 파서는
                 * RowCenterY와 RowBandHeight를 사용합니다. 따라서 같은 DWG 안의 서로 다른
                 * 철근재료표가 1번부터 다시 시작해도 번호가 같다는 이유로 삭제되지 않습니다.
                 */
                bool hasSpatialRowPosition = row.HasShapeCellBounds()
                    || Math.Abs(row.RowCenterY) > 0.0001
                    || Math.Abs(row.RowBandHeight) > 0.0001;

                if (hasSpatialRowPosition)
                {
                    double rowCenterY = row.HasShapeCellBounds()
                        ? (row.ShapeCellMinY + row.ShapeCellMaxY) / 2.0
                        : row.RowCenterY;
                    double rowHeight = row.HasShapeCellBounds()
                        ? Math.Abs(row.ShapeCellMaxY - row.ShapeCellMinY)
                        : Math.Abs(row.RowBandHeight);
                    double tolerance = Math.Max(rowHeight * 0.08, OviaBoxOverlapTolerance * 10.0);
                    bool allowed = false;
                    int rectangleIndex;

                    for (rectangleIndex = 0; rectangleIndex < allowedRectangles.Count; rectangleIndex++)
                    {
                        OviaSelectionRectangle rectangle = allowedRectangles[rectangleIndex];

                        if (rowCenterY >= rectangle.MinY - tolerance
                            && rowCenterY <= rectangle.MaxY + tolerance)
                        {
                            allowed = true;
                            break;
                        }
                    }

                    if (!allowed)
                    {
                        filteredRowCount++;
                        continue;
                    }
                }

                /*
                 * 좌표 자체를 얻지 못한 예외 행만 보존합니다. Desktop에서는 번호 기반 중복 삭제를
                 * 하지 않으므로, 서로 다른 표의 동일 번호 행은 원본 순서대로 모두 추가됩니다.
                 */
                filtered.Add(row);
            }

            for (i = 0; i < filtered.Count; i++)
            {
                filtered[i].No = i + 1;
            }

            return filtered;
        }

        private List<OviaBarTableRow> FilterActualRebarDataRows(List<OviaBarTableRow> rows, out int ignoredRowCount)
        {
            ignoredRowCount = 0;
            List<OviaBarTableRow> filtered = new List<OviaBarTableRow>();

            if (rows == null)
            {
                return filtered;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null)
                {
                    ignoredRowCount++;
                    continue;
                }

                NormalizeBarTableNumericValues(row);

                if (!IsActualRebarDataRow(row))
                {
                    ignoredRowCount++;
                    continue;
                }

                row.RowType = "DATA";
                row.No = filtered.Count + 1;
                filtered.Add(row);
            }

            return filtered;
        }

        private bool HasLikelyMisappliedGridSchema(List<OviaBarTableRow> rows, out int affectedRowCount)
        {
            affectedRowCount = 0;

            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            int dataRowCount = 0;
            int markEqualsQuantityCount = 0;
            int shapeEqualsTotalColumnsCount = 0;
            int duplicatedMarkCount = 0;
            HashSet<string> observedMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                /*
                 * OVIA 2026-07-21 실제 3차 CSV 재검증:
                 * 열 오정렬 검사는 FilterActualRebarDataRows보다 먼저 실행되므로, 이 시점에는
                 * 총길이/중량이 아직 한 칸 밀렸거나 RawText 복구 전 빈칸일 수 있습니다.
                 * 최종 CSV에서는 NormalizeBarTableNumericValues가 값을 복원한 뒤
                 * 형상원본=총길이+중량 17/17이 되지만, 기존 사전 검사는 빈 값과 비교해 놓쳤습니다.
                 * 판정에 사용하는 각 DATA 행을 먼저 동일한 최종 숫자 규칙으로 정규화하여
                 * 검사 시점과 실제 CSV 저장 시점의 값을 일치시킵니다.
                 */
                NormalizeBarTableNumericValues(row);
                dataRowCount++;

                string markText = row.MarkNo == null || row.MarkNo.Trim() == ""
                    ? row.BarNo
                    : row.MarkNo;
                decimal markValue;
                decimal quantityValue;

                if (TryParseDecimalText(markText, out markValue)
                    && TryParseDecimalText(row.Qty, out quantityValue)
                    && markValue == Decimal.Truncate(markValue)
                    && AreDecimalValuesEqualAtThreeDecimals(markValue, quantityValue))
                {
                    markEqualsQuantityCount++;
                }

                string normalizedMark = markText == null ? "" : markText.Trim();

                if (normalizedMark != "" && !observedMarks.Add(normalizedMark))
                {
                    duplicatedMarkCount++;
                }

                string shapeSource = row.ShapeRawText;

                if (shapeSource == null || shapeSource.Trim() == "")
                {
                    shapeSource = row.ShapeText;
                }

                if (ShapeSourceMatchesTotalColumns(shapeSource, row.TotalLength, row.TotalWeight))
                {
                    shapeEqualsTotalColumnsCount++;
                }
            }

            if (dataRowCount < 3)
            {
                return false;
            }

            int markThreshold = Math.Max(3, (int)Math.Ceiling(dataRowCount * 0.80));
            int shapeThreshold = Math.Max(3, (int)Math.Ceiling(dataRowCount * 0.70));

            if (markEqualsQuantityCount < markThreshold)
            {
                return false;
            }

            if (shapeEqualsTotalColumnsCount >= shapeThreshold)
            {
                affectedRowCount = Math.Min(markEqualsQuantityCount, shapeEqualsTotalColumnsCount);
                return true;
            }

            /*
             * 형상 셀에 문자 대신 선 객체만 존재하면 ShapeRawText/ShapeText가 비어 기존의
             * "형상=총길이·중량" 조건이 발동하지 않습니다. 그러나 수량 열을 번호로 잘못 쓴
             * 결과는 여러 행에서 번호=수량이 반복되고 같은 수량값이 중복 번호로 나타납니다.
             * DATA 5행 이상, 번호=수량 80% 이상, 중복 번호 20% 이상이 동시에 성립할 때만
             * 독립 차단하여 정상적인 소규모 표나 우연한 단일 일치를 건드리지 않습니다.
             */
            int duplicateThreshold = Math.Max(2, (int)Math.Ceiling(dataRowCount * 0.20));

            if (dataRowCount >= 5 && duplicatedMarkCount >= duplicateThreshold)
            {
                affectedRowCount = markEqualsQuantityCount;
                return true;
            }

            return false;
        }

        private List<OviaTextRow> FilterTextRowsToSelectedTableX(
            List<OviaTextRow> textRows,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint)
        {
            List<OviaTextRow> result = new List<OviaTextRow>();

            if (textRows == null || textRows.Count == 0)
            {
                return result;
            }

            double minX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double maxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double width = Math.Max(maxX - minX, 0.0001);
            double margin = Math.Max(width * 0.025, 0.5);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow row = textRows[i];

                if (row == null || row.X < minX - margin || row.X > maxX + margin)
                {
                    continue;
                }

                result.Add(row);
            }

            // 현재 표 X 범위에서 문자를 찾지 못한 경우 인접 표 전체로 되돌아가지 않습니다.
            // 복구를 건너뛰고 후단 안전 검증/fallback이 처리하게 해야 잘못된 번호 열이 섞이지 않습니다.
            return result;
        }

        private bool ShapeSourceMatchesTotalColumns(string shapeSource, string totalLengthText, string totalWeightText)
        {
            if (shapeSource == null || shapeSource.Trim() == "")
            {
                return false;
            }

            decimal totalLength;
            decimal totalWeight;

            if (!TryParseDecimalText(totalLengthText, out totalLength)
                || !TryParseDecimalText(totalWeightText, out totalWeight))
            {
                return false;
            }

            List<string> tokens = ExtractNumericTokensPreserveThousands(shapeSource);

            if (tokens.Count < 2 || tokens.Count > 4)
            {
                return false;
            }

            bool containsTotalLength = false;
            bool containsTotalWeight = false;
            int i;

            for (i = 0; i < tokens.Count; i++)
            {
                decimal tokenValue;

                if (!TryParseDecimalText(tokens[i], out tokenValue))
                {
                    continue;
                }

                if (AreDecimalValuesEqualAtThreeDecimals(tokenValue, totalLength))
                {
                    containsTotalLength = true;
                }

                if (AreDecimalValuesEqualAtThreeDecimals(tokenValue, totalWeight))
                {
                    containsTotalWeight = true;
                }
            }

            return containsTotalLength && containsTotalWeight;
        }

        private bool RecoverBarTableMarkNumbersByPhysicalColumn(
            List<OviaBarTableRow> rows,
            List<OviaTextRow> analysisTextRows,
            out int repairedCount,
            out int rejectedContaminatedCount)
        {
            repairedCount = 0;
            rejectedContaminatedCount = 0;

            if (rows == null || rows.Count == 0 || analysisTextRows == null || analysisTextRows.Count == 0)
            {
                return false;
            }

            /*
             * OVIA 2026-07-20 번호 열 오염 차단:
             * 좌표 fallback은 같은 행에서 규격보다 왼쪽에 있는 첫 정수를 번호로 보았습니다.
             * 번호 문자의 기준 Y가 다른 문자와 조금 어긋난 행에서는 실제 번호가 같은 그룹에서
             * 빠지고, 철근형상 치수(9000, 7300, 4240 등)가 번호로 승격될 수 있었습니다.
             *
             * 번호는 값의 크기나 행 순번으로 추정하지 않습니다. 분석 영역에서 실제 헤더를 다시
             * 찾고, MARK_NO 헤더가 확정한 물리 X 열 안에서 현재 데이터 행의 Y 범위에 들어오는
             * 양의 정수 문자만 채택합니다. 신뢰 가능한 후보 열이나 개별 행의 번호를 찾지 못하면
             * 기존 행을 변경하지 않아 번호 보정 실패가 전체 추출 실패로 확대되지 않게 합니다.
             */
            OviaHeaderColumn markColumn = null;
            OviaHeaderMap headerMap = null;
            List<OviaHeaderColumn> markColumnCandidates = new List<OviaHeaderColumn>();

            try
            {
                headerMap = DetectHeaderMap(GroupRowsByY(analysisTextRows), analysisTextRows);
            }
            catch
            {
                headerMap = null;
            }

            /*
             * 실제 "번호" 헤더와 바로 오른쪽 헤더의 중심 간격으로 만든 열을 최우선합니다.
             * 문자 그룹/표 선 기반 열은 보조 후보입니다. 복구 건수가 같거나 조금 더 많다는 이유로
             * 길이·형상 치수 열이 실제 번호 헤더 열을 덮어쓰지 못하게 후보 순서를 고정합니다.
             */
            AddMarkColumnCandidate(markColumnCandidates, DetectMarkColumnDirectlyFromHeaderText(analysisTextRows));

            if (headerMap != null && headerMap.Columns != null)
            {
                AddMarkColumnCandidate(
                    markColumnCandidates,
                    NormalizeTextHeaderMarkColumn(FindHeaderColumnByKey(headerMap.Columns, "MARK_NO"))
                );
            }

            AddMarkColumnCandidate(markColumnCandidates, FindHeaderColumnByKey(lastDetectedHeaderColumns, "MARK_NO"));

            if (markColumnCandidates.Count == 0)
            {
                return false;
            }

            double averageTextHeight = GetAverageTextHeight(analysisTextRows);

            if (averageTextHeight <= 0.0001)
            {
                averageTextHeight = 1.0;
            }

            int dataRowCount = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && String.Equals(rows[i].RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    dataRowCount++;
                }
            }

            if (dataRowCount == 0)
            {
                return false;
            }

            int minimumReliableCount = Math.Max(1, (int)Math.Ceiling(dataRowCount * 0.80));
            int bestRecoveredCount = -1;

            for (i = 0; i < markColumnCandidates.Count; i++)
            {
                int recoveredCount = CountRecoverableMarkNumbers(rows, analysisTextRows, markColumnCandidates[i], averageTextHeight);

                if (recoveredCount > bestRecoveredCount)
                {
                    bestRecoveredCount = recoveredCount;
                    markColumn = markColumnCandidates[i];
                }

                /*
                 * 후보는 신뢰도 순서(직접 번호 헤더 -> 문자 헤더 맵 -> 표 선)입니다.
                 * 먼저 기준을 만족한 후보를 즉시 확정하여 뒤쪽의 길이/형상 열 오인을 막습니다.
                 */
                if (recoveredCount >= minimumReliableCount)
                {
                    markColumn = markColumnCandidates[i];
                    bestRecoveredCount = recoveredCount;
                    break;
                }
            }

            /*
             * _03 회귀 방지:
             * 잘못 선택된 번호 열에서 복구 건수가 0이어도 모든 기존 번호를 비워 실제 철근행이
             * 전부 제거될 수 있었습니다. 전체 DATA 행의 80% 이상을 같은 물리 열에서 복구할 수
             * 있을 때만 그 열을 신뢰합니다. 기준 미달이면 어떤 행도 변경하지 않고 기존 파서
             * 결과를 유지하여 '전체 추출 불가' 상태를 만들지 않습니다.
             */
            if (markColumn == null || bestRecoveredCount < minimumReliableCount)
            {
                return false;
            }

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double rowCenterY;
                double rowMinY;
                double rowMaxY;

                if (!TryGetMarkSearchBand(row, averageTextHeight, out rowMinY, out rowMaxY, out rowCenterY))
                {
                    continue;
                }

                string recovered = FindMarkNumberInPhysicalColumn(
                    analysisTextRows,
                    markColumn,
                    rowMinY,
                    rowMaxY,
                    rowCenterY
                );

                string previous = row.MarkNo == null || row.MarkNo.Trim() == "" ? row.BarNo : row.MarkNo;

                if (recovered != "")
                {
                    if (!String.Equals(previous == null ? "" : previous.Trim(), recovered, StringComparison.OrdinalIgnoreCase))
                    {
                        repairedCount++;
                    }

                    row.MarkNo = recovered;
                    row.BarNo = recovered;
                }
                else
                {
                    /*
                     * 후보 열 전체가 신뢰 가능해도 개별 행의 문자 기준점이 도면마다 다를 수 있습니다.
                     * 해당 행만 복구하지 못한 경우 기존 값을 유지합니다. 복구 실패를 데이터 삭제로
                     * 전환하지 않는 것이 _03의 전체 추출 불가 회귀를 막는 최종 안전장치입니다.
                     */
                    continue;
                }
            }

            return true;
        }

        private bool RepairLengthContaminatedMarkNumbersFromRawText(
            List<OviaBarTableRow> rows,
            out int repairedCount)
        {
            repairedCount = 0;

            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string currentMark = row.MarkNo == null || row.MarkNo.Trim() == ""
                    ? row.BarNo
                    : row.MarkNo;

                if (!IsSameCadNumericText(currentMark, row.Length))
                {
                    continue;
                }

                string rawMark = RecoverGridMarkNoFromRawText(row.RawText);

                if (rawMark == ""
                    || String.Equals(rawMark.Trim(), currentMark == null ? "" : currentMark.Trim(), StringComparison.OrdinalIgnoreCase)
                    || IsSameCadNumericText(rawMark, row.Length))
                {
                    continue;
                }

                row.MarkNo = rawMark;
                row.BarNo = rawMark;
                repairedCount++;
            }

            return repairedCount > 0;
        }

        private bool IsSameCadNumericText(string left, string right)
        {
            decimal leftValue;
            decimal rightValue;

            return TryParseDecimalText(left, out leftValue)
                && TryParseDecimalText(right, out rightValue)
                && AreDecimalValuesEqualAtThreeDecimals(leftValue, rightValue);
        }

        private void AddMarkColumnCandidate(List<OviaHeaderColumn> candidates, OviaHeaderColumn candidate)
        {
            if (candidates == null || candidate == null || candidate.RightX <= candidate.LeftX)
            {
                return;
            }

            int i;

            for (i = 0; i < candidates.Count; i++)
            {
                double candidateWidth = Math.Max(candidate.RightX - candidate.LeftX, 0.0001);
                double existingWidth = Math.Max(candidates[i].RightX - candidates[i].LeftX, 0.0001);
                double sameCenterTolerance = Math.Min(candidateWidth, existingWidth) * 0.20;

                if (Math.Abs(candidates[i].X - candidate.X) <= sameCenterTolerance)
                {
                    return;
                }
            }

            candidates.Add(candidate);
        }

        private OviaHeaderColumn NormalizeTextHeaderMarkColumn(OviaHeaderColumn markColumn)
        {
            if (markColumn == null || markColumn.RightX <= markColumn.LeftX)
            {
                return null;
            }

            double leftHalfWidth = markColumn.X - markColumn.LeftX;
            double rightHalfWidth = markColumn.RightX - markColumn.X;

            if (rightHalfWidth <= 0.0001 || leftHalfWidth <= rightHalfWidth * 1.8)
            {
                return markColumn;
            }

            OviaHeaderColumn clamped = new OviaHeaderColumn();
            clamped.StandardKey = markColumn.StandardKey;
            clamped.OriginalTitle = markColumn.OriginalTitle;
            clamped.HeaderTextVerified = markColumn.HeaderTextVerified;
            clamped.X = markColumn.X;
            clamped.LeftX = markColumn.X - rightHalfWidth;
            clamped.RightX = markColumn.RightX;
            clamped.SourceColumnIndex = markColumn.SourceColumnIndex;
            return clamped;
        }

        private OviaHeaderColumn DetectMarkColumnDirectlyFromHeaderText(List<OviaTextRow> textRows)
        {
            if (textRows == null || textRows.Count == 0)
            {
                return null;
            }

            double averageHeight = GetAverageTextHeight(textRows);

            if (averageHeight <= 0.0001)
            {
                averageHeight = 1.0;
            }

            OviaTextRow bestMarkHeader = null;
            OviaTextRow bestRightHeader = null;
            double bestGap = Double.MaxValue;
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow markHeader = textRows[i];

                if (markHeader == null || ClassifyHeaderTitle(CleanHeaderText(markHeader.TextValue)) != "MARK_NO")
                {
                    continue;
                }

                int j;

                for (j = 0; j < textRows.Count; j++)
                {
                    OviaTextRow rightHeader = textRows[j];

                    if (rightHeader == null || rightHeader.X <= markHeader.X)
                    {
                        continue;
                    }

                    string rightKey = ClassifyHeaderTitle(CleanHeaderText(rightHeader.TextValue));

                    if (rightKey == "" || rightKey == "MARK_NO")
                    {
                        continue;
                    }

                    double yTolerance = Math.Max(averageHeight * 4.0, 5.0);

                    if (Math.Abs(rightHeader.Y - markHeader.Y) > yTolerance)
                    {
                        continue;
                    }

                    double gap = rightHeader.X - markHeader.X;

                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        bestMarkHeader = markHeader;
                        bestRightHeader = rightHeader;
                    }
                }
            }

            if (bestMarkHeader == null || bestRightHeader == null || bestGap <= 0.0001)
            {
                return null;
            }

            double halfWidth = bestGap / 2.0;
            OviaHeaderColumn column = new OviaHeaderColumn();
            column.StandardKey = "MARK_NO";
            column.OriginalTitle = "번호";
            column.HeaderTextVerified = true;
            column.X = bestMarkHeader.X;
            column.LeftX = bestMarkHeader.X - halfWidth;
            column.RightX = bestMarkHeader.X + halfWidth;
            column.SourceColumnIndex = -1;
            return column;
        }

        private int CountRecoverableMarkNumbers(
            List<OviaBarTableRow> rows,
            List<OviaTextRow> textRows,
            OviaHeaderColumn markColumn,
            double averageTextHeight)
        {
            int recoveredCount = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double rowMinY;
                double rowMaxY;
                double rowCenterY;

                if (!TryGetMarkSearchBand(row, averageTextHeight, out rowMinY, out rowMaxY, out rowCenterY))
                {
                    continue;
                }

                if (FindMarkNumberInPhysicalColumn(textRows, markColumn, rowMinY, rowMaxY, rowCenterY) != "")
                {
                    recoveredCount++;
                }
            }

            return recoveredCount;
        }

        private bool TryGetMarkSearchBand(
            OviaBarTableRow row,
            double averageTextHeight,
            out double rowMinY,
            out double rowMaxY,
            out double rowCenterY)
        {
            rowMinY = 0;
            rowMaxY = 0;
            rowCenterY = 0;

            if (row == null)
            {
                return false;
            }

            if (row.HasShapeCellBounds())
            {
                rowMinY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
                rowMaxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
                rowCenterY = (rowMinY + rowMaxY) / 2.0;

                double gridYMargin = Math.Max(averageTextHeight * 0.18, 0.1);
                rowMinY -= gridYMargin;
                rowMaxY += gridYMargin;
                return true;
            }

            if (row.RowBandHeight <= 0.0001)
            {
                return false;
            }

            rowCenterY = row.RowCenterY;
            double rowTextHeight = Math.Max(row.RowBandHeight, averageTextHeight);
            double coordinateYMargin = Math.Max(rowTextHeight * 1.25, 0.75);
            rowMinY = rowCenterY - coordinateYMargin;
            rowMaxY = rowCenterY + coordinateYMargin;
            return true;
        }

        private string FindMarkNumberInPhysicalColumn(
            List<OviaTextRow> textRows,
            OviaHeaderColumn markColumn,
            double rowMinY,
            double rowMaxY,
            double rowCenterY)
        {
            if (textRows == null || markColumn == null || markColumn.RightX <= markColumn.LeftX)
            {
                return "";
            }

            string bestValue = "";
            double bestScore = Double.MaxValue;
            double columnCenterX = (markColumn.LeftX + markColumn.RightX) / 2.0;
            double columnWidth = Math.Max(markColumn.RightX - markColumn.LeftX, 0.0001);
            double rowHeight = Math.Max(rowMaxY - rowMinY, 0.0001);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.X < markColumn.LeftX || textRow.X > markColumn.RightX)
                {
                    continue;
                }

                if (textRow.Y < rowMinY || textRow.Y > rowMaxY)
                {
                    continue;
                }

                string candidate = CleanCellText(textRow.TextValue).Trim();

                if (!IsPositiveRebarMarkNoText(candidate) || IsHeaderRow(candidate) || IsSummaryText(candidate))
                {
                    continue;
                }

                double xScore = Math.Abs(textRow.X - columnCenterX) / columnWidth;
                double yScore = Math.Abs(textRow.Y - rowCenterY) / rowHeight;
                double score = (xScore * 0.65) + (yScore * 0.35);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestValue = candidate;
                }
            }

            return bestValue;
        }

        private void NormalizeBarTableNumericValues(OviaBarTableRow row)
        {
            if (row == null)
            {
                return;
            }

            string value = PickGridNumericValue(row.Length, "LENGTH_MM");
            row.Length = value == "" ? "" : FormatLengthMmText(value);

            value = PickGridNumericValue(row.Qty, "QUANTITY_EA");
            row.Qty = value;

            value = PickGridNumericValue(row.TotalLength, "TOTAL_LENGTH_M");
            row.TotalLength = value;

            value = PickGridNumericValue(row.TotalWeight, "TOTAL_WEIGHT");
            row.TotalWeight = value;

            RepairShiftedTotalLengthAndWeightFromRawText(row);
        }

        private bool RepairShiftedTotalLengthAndWeightFromRawText(OviaBarTableRow row)
        {
            if (row == null
                || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase)
                || (row.TotalLength != null && row.TotalLength.Trim() != "")
                || row.RawText == null
                || row.RawText.Trim() == "")
            {
                return false;
            }

            decimal currentLength;
            decimal currentQty;
            decimal shiftedWeight;

            if (!TryParseDecimalText(row.Length, out currentLength)
                || !TryParseDecimalText(row.Qty, out currentQty)
                || !TryParseDecimalText(row.TotalWeight, out shiftedWeight)
                || currentLength <= 0
                || currentQty <= 0)
            {
                return false;
            }

            string text = CleanCellText(row.RawText);
            string[] parts = text.Split(
                new char[] { ' ', '\t', ';', '|', '/', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );
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
                return false;
            }

            List<string> numbersAfterSpec = new List<string>();

            for (i = specIndex + 1; i < parts.Length; i++)
            {
                if (DetectSpec(parts[i]) != "")
                {
                    continue;
                }

                MatchCollection matches = Regex.Matches(
                    parts[i],
                    @"-?\d+(?:,\d{3})*(?:\.\d+)?|-?\d+(?:\.\d+)?"
                );

                for (int j = 0; j < matches.Count; j++)
                {
                    numbersAfterSpec.Add(NormalizeNumericToken(matches[j].Value));
                }
            }

            string recoveredLength;
            string recoveredQty;
            string recoveredTotalLength;
            string recoveredTotalWeight;

            if (!TryPickSpecAnchoredBarValues(
                numbersAfterSpec,
                true,
                out recoveredLength,
                out recoveredQty,
                out recoveredTotalLength,
                out recoveredTotalWeight))
            {
                return false;
            }

            decimal parsedLength;
            decimal parsedQty;
            decimal parsedTotalLength;
            decimal parsedTotalWeight;

            if (!TryParseDecimalText(recoveredLength, out parsedLength)
                || !TryParseDecimalText(recoveredQty, out parsedQty)
                || !TryParseDecimalText(recoveredTotalLength, out parsedTotalLength)
                || !TryParseDecimalText(recoveredTotalWeight, out parsedTotalWeight))
            {
                return false;
            }

            decimal expectedTotalLength = currentLength * currentQty / 1000M;

            /*
             * 단순한 숫자 순서 추정은 하지 않습니다. 현재 길이·수량과 원문에서 복구한 길이·수량이
             * 같고, 길이×수량/1000 및 현재 중량 칸 값이 모두 원문의 총길이와 소수 셋째 자리까지
             * 일치할 때만 "총길이가 중량 칸으로 한 칸 밀린 경우"로 확정합니다.
             */
            if (!AreDecimalValuesEqualAtThreeDecimals(currentLength, parsedLength)
                || !AreDecimalValuesEqualAtThreeDecimals(currentQty, parsedQty)
                || !AreDecimalValuesEqualAtThreeDecimals(expectedTotalLength, parsedTotalLength)
                || !AreDecimalValuesEqualAtThreeDecimals(shiftedWeight, parsedTotalLength))
            {
                return false;
            }

            row.TotalLength = recoveredTotalLength;
            row.TotalWeight = recoveredTotalWeight;
            return true;
        }

        private bool AreDecimalValuesEqualAtThreeDecimals(decimal left, decimal right)
        {
            return Decimal.Round(left, 3, MidpointRounding.AwayFromZero)
                == Decimal.Round(right, 3, MidpointRounding.AwayFromZero);
        }

        private bool IsActualRebarDataRow(OviaBarTableRow row)
        {
            if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rawText = row.RawText == null ? "" : row.RawText;

            if (IsSummaryText(rawText))
            {
                return false;
            }

            string mark = row.MarkNo == null || row.MarkNo.Trim() == "" ? row.BarNo : row.MarkNo;
            bool markOk = IsPositiveRebarMarkNoText(mark);
            bool specOk = DetectSpec(row.Spec) != "";
            bool lengthOk = IsPositiveCadTableNumber(row.Length);
            bool qtyOk = IsPositiveCadTableNumber(row.Qty);

            return markOk && specOk && lengthOk && qtyOk;
        }

        private bool ValidateExtractedBarTableRows(List<OviaBarTableRow> rows, out string message)
        {
            message = "";

            if (rows == null || rows.Count == 0)
            {
                message = "데이터 행이 없습니다.";
                return false;
            }

            /*
             * grid → 좌표 → 규격 앵커 fallback을 모두 통과한 최종 결과도 저장 직전에 다시 검사합니다.
             * 앞 단계의 재분석 과정에서 같은 수량열/형상열 오정렬이 반복되더라도 잘못된 CSV와
             * .ready를 발행하지 않고 기존 OVIA BarList를 그대로 유지하는 마지막 fail-closed입니다.
             */
            int finalMisalignedRowCount;

            if (HasLikelyMisappliedGridSchema(rows, out finalMisalignedRowCount))
            {
                message = "최종 번호·철근형상 열 오정렬 "
                    + finalMisalignedRowCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행을 차단했습니다.";
                return false;
            }

            int contaminatedTotalLengthCount;

            if (HasContaminatedTotalLengthRows(rows, out contaminatedTotalLengthCount))
            {
                message = "소계·총계 또는 인접 행 숫자가 총길이 열에 섞인 DATA "
                    + contaminatedTotalLengthCount.ToString(CultureInfo.InvariantCulture)
                    + "개 행을 차단했습니다.";
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
                bool markOk = IsPositiveRebarMarkNoText(mark);
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

        private bool HasContaminatedTotalLengthRows(
            List<OviaBarTableRow> rows,
            out int affectedRowCount)
        {
            affectedRowCount = 0;

            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                decimal lengthMm;
                decimal quantity;
                decimal totalLength;

                /*
                 * 총길이 열 자체가 없는 표는 기존 계약대로 허용합니다. 값이 있는 표에서만
                 * 길이×수량/1000과 비교하여 58번의 352.04, 66번의 352.04 같은 요약값 혼입을
                 * CSV/.ready 발행 직전에 최종 차단합니다. 2자리 표시 반올림은 허용합니다.
                 */
                if (!TryParseDecimalText(row.Length, out lengthMm)
                    || !TryParseDecimalText(row.Qty, out quantity)
                    || !TryParseDecimalText(row.TotalLength, out totalLength)
                    || lengthMm <= 0M
                    || quantity <= 0M)
                {
                    continue;
                }

                decimal expected = (lengthMm * quantity) / 1000M;

                if (Math.Abs(expected - totalLength) > 0.006M)
                {
                    affectedRowCount++;
                }
            }

            return affectedRowCount > 0;
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

            /*
             * 전달 좌표보다 현재 선택 표에서 실제 검출된 물리 세로선 범위를 우선합니다.
             * 분석창은 인접 표 헤더를 찾기 위해 확장될 수 있지만, verticalXs는 이미
             * LimitGridVerticalCoordinatesToSelectedTable을 통과한 현재 표 전용 좌표입니다.
             */
            if (verticalXs != null && verticalXs.Count >= 2)
            {
                selectionMinX = verticalXs[0];
                selectionMaxX = verticalXs[0];
                int detectedIndex;

                for (detectedIndex = 1; detectedIndex < verticalXs.Count; detectedIndex++)
                {
                    selectionMinX = Math.Min(selectionMinX, verticalXs[detectedIndex]);
                    selectionMaxX = Math.Max(selectionMaxX, verticalXs[detectedIndex]);
                }
            }

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
                clone.HeaderTextVerified = item.HeaderTextVerified;
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

        private List<double> LimitGridVerticalCoordinatesToSelectedTable(
            List<double> verticalXs,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint,
            double tolerance)
        {
            if (verticalXs == null || verticalXs.Count < 3)
            {
                return verticalXs == null ? new List<double>() : verticalXs;
            }

            List<double> ordered = new List<double>(verticalXs);
            ordered.Sort();

            double selectionMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            double selectionMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            double selectionWidth = Math.Abs(selectionMaxX - selectionMinX);

            if (selectionWidth <= 0.0001)
            {
                return ordered;
            }

            /*
             * 인접한 2개 표가 나란히 배치된 도면에서 분석창의 왼쪽 여유 범위가
             * 앞 표의 총길이/중량 컬럼까지 포함할 수 있습니다. 표 구조는 사용자가
             * 지정한 좌우 경계에 가장 가까운 세로선을 실제 외곽선으로 확정하고,
             * 그 밖의 인접 표 세로선은 컬럼 분석에서 제외합니다.
             */
            double edgeSearchDistance = Math.Max(selectionWidth * 0.08, tolerance * 4.0);
            double leftBoundary = Double.NaN;
            double rightBoundary = Double.NaN;
            double leftDistance = Double.MaxValue;
            double rightDistance = Double.MaxValue;
            int i;

            for (i = 0; i < ordered.Count; i++)
            {
                double x = ordered[i];
                double distanceToLeft = Math.Abs(x - selectionMinX);
                double distanceToRight = Math.Abs(x - selectionMaxX);

                if (distanceToLeft <= edgeSearchDistance && distanceToLeft < leftDistance)
                {
                    leftBoundary = x;
                    leftDistance = distanceToLeft;
                }

                if (distanceToRight <= edgeSearchDistance && distanceToRight < rightDistance)
                {
                    rightBoundary = x;
                    rightDistance = distanceToRight;
                }
            }

            if (Double.IsNaN(leftBoundary) || Double.IsNaN(rightBoundary) || rightBoundary <= leftBoundary)
            {
                return ordered;
            }

            List<double> limited = new List<double>();
            double boundaryMargin = Math.Max(tolerance * 1.5, 0.5);

            for (i = 0; i < ordered.Count; i++)
            {
                double x = ordered[i];

                if (x >= leftBoundary - boundaryMargin && x <= rightBoundary + boundaryMargin)
                {
                    limited.Add(x);
                }
            }

            return limited.Count >= 3 ? limited : ordered;
        }

        private List<OviaTextRow> FilterGridTextRowsToSelectedTable(
            List<OviaTextRow> textRows,
            List<double> verticalXs,
            double tolerance)
        {
            if (textRows == null || verticalXs == null || verticalXs.Count < 2)
            {
                return textRows == null ? new List<OviaTextRow>() : textRows;
            }

            double tableMinX = verticalXs[0];
            double tableMaxX = verticalXs[0];
            int i;

            for (i = 1; i < verticalXs.Count; i++)
            {
                tableMinX = Math.Min(tableMinX, verticalXs[i]);
                tableMaxX = Math.Max(tableMaxX, verticalXs[i]);
            }

            double xMargin = Math.Max(tolerance * 2.0, 0.5);
            List<OviaTextRow> filtered = new List<OviaTextRow>();

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow textRow = textRows[i];

                if (textRow == null)
                {
                    continue;
                }

                if (textRow.X >= tableMinX - xMargin && textRow.X <= tableMaxX + xMargin)
                {
                    filtered.Add(textRow);
                }
            }

            return filtered;
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
            int firstIntegerIndex = -1;

            for (int c = 0; c < limit; c++)
            {
                string pattern = DetectGridColumnValuePattern(cellTexts, -1, c);

                if (pattern == "sequential_number")
                {
                    return c;
                }

                if (firstIntegerIndex < 0 && pattern == "integer_number")
                {
                    firstIntegerIndex = c;
                }
            }

            /*
             * 순차 번호 열을 확인하지 못했다고 해서 규격 왼쪽의 임의 정수 열을 번호로
             * 선택하지 않습니다. 철근형상 치수 열은 9000, 7300처럼 모두 정수이므로
             * 단순 integer 점수만으로는 번호와 구분할 수 없습니다.
             * 정수 후보가 여러 개면 가장 왼쪽 물리 열만 허용하며, 최종 값은 실제 번호
             * 헤더의 X 범위로 다시 검증합니다.
             */
            if (firstIntegerIndex == 0)
            {
                return firstIntegerIndex;
            }

            return 0;
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
            row.RowCenterY = GetAverageTextY(line);
            row.RowBandHeight = GetAverageTextHeight(line);

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


        private OviaCadTableGridModel BuildCadTableGridModel(
            List<OviaGridLineSegment> gridLines,
            List<OviaBarTableRow> rows,
            Point3d selectedMinPoint,
            Point3d selectedMaxPoint)
        {
            OviaCadTableGridModel model = new OviaCadTableGridModel();
            model.TableMinX = Math.Min(selectedMinPoint.X, selectedMaxPoint.X);
            model.TableMaxX = Math.Max(selectedMinPoint.X, selectedMaxPoint.X);
            model.TableMinY = Math.Min(selectedMinPoint.Y, selectedMaxPoint.Y);
            model.TableMaxY = Math.Max(selectedMinPoint.Y, selectedMaxPoint.Y);

            List<double> rowHeights = new List<double>();
            List<double> rowBoundaryYs = new List<double>();
            List<double> shapeBoundaryXs = new List<double>();
            double dataMinY = Double.MaxValue;
            double dataMaxY = Double.MinValue;
            int i;

            if (rows != null)
            {
                for (i = 0; i < rows.Count; i++)
                {
                    OviaBarTableRow row = rows[i];

                    if (row == null || !row.HasShapeCellBounds())
                    {
                        continue;
                    }

                    double rowMinY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
                    double rowMaxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
                    double rowHeight = rowMaxY - rowMinY;

                    if (rowHeight > 0.0001)
                    {
                        rowHeights.Add(rowHeight);
                        rowBoundaryYs.Add(rowMinY);
                        rowBoundaryYs.Add(rowMaxY);
                        shapeBoundaryXs.Add(Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX));
                        shapeBoundaryXs.Add(Math.Max(row.ShapeCellMinX, row.ShapeCellMaxX));
                        dataMinY = Math.Min(dataMinY, rowMinY);
                        dataMaxY = Math.Max(dataMaxY, rowMaxY);
                    }
                }
            }

            if (dataMinY == Double.MaxValue || dataMaxY == Double.MinValue || dataMaxY <= dataMinY)
            {
                dataMinY = model.TableMinY;
                dataMaxY = model.TableMaxY;
            }

            model.DataMinY = dataMinY;
            model.DataMaxY = dataMaxY;

            double tableWidth = Math.Max(model.TableMaxX - model.TableMinX, 0.0001);
            double dataHeight = Math.Max(dataMaxY - dataMinY, 0.0001);
            double typicalRowHeight = GetMedianCadGridValue(rowHeights);

            if (typicalRowHeight <= 0.0001)
            {
                typicalRowHeight = Math.Max(dataHeight / Math.Max(rows == null ? 1 : rows.Count, 1), 1.0);
            }

            model.TypicalRowHeight = typicalRowHeight;

            /*
             * GRID 좌표 허용오차는 행 높이에 비해 충분히 작게 유지합니다.
             * 과거 6~16% 수준의 넓은 허용범위는 셀 경계에 가까운 U형 수직선이나
             * 행 하단의 실제 수평 철근선을 GRID로 흡수할 수 있었습니다.
             * AutoCAD 표선은 같은 축에 정렬되므로 0.6~2.5% 범위면 블록 분해 오차를
             * 흡수하면서 내부 형상과의 분리도 유지할 수 있습니다.
             */
            model.AxisTolerance = Math.Max(Math.Max(typicalRowHeight * 0.006, tableWidth * 0.00025), 0.03);
            model.MergeTolerance = Math.Max(Math.Max(typicalRowHeight * 0.012, tableWidth * 0.00045), 0.05);
            model.MatchToleranceX = Math.Max(Math.Max(typicalRowHeight * 0.018, tableWidth * 0.00060), 0.06);
            model.MatchToleranceY = Math.Max(typicalRowHeight * 0.025, 0.06);

            if (gridLines == null || gridLines.Count == 0)
            {
                return model;
            }

            model.AllVerticalXs = ExtractCoveredGridCoordinates(
                gridLines,
                true,
                model.AxisTolerance,
                model.MergeTolerance,
                Math.Max(typicalRowHeight * 0.20, 0.20),
                dataHeight * 0.65,
                dataMinY,
                dataMaxY
            );

            model.AllHorizontalYs = ExtractCoveredGridCoordinates(
                gridLines,
                false,
                model.AxisTolerance,
                model.MergeTolerance,
                Math.Max(tableWidth * 0.012, 0.20),
                tableWidth * 0.68,
                model.TableMinX,
                model.TableMaxX
            );

            FilterCadGridCoordinatesToRange(
                model.AllVerticalXs,
                model.TableMinX - model.MatchToleranceX,
                model.TableMaxX + model.MatchToleranceX
            );

            model.VerticalXs = new List<double>(model.AllVerticalXs);
            model.HorizontalYs = new List<double>(model.AllHorizontalYs);

            /*
             * 수직축은 여러 행을 관통한다는 조건과 함께 실제 철근형상 셀의 좌/우 물리 경계에
             * 가까워야 합니다. 같은 폭의 U형이 여러 행 반복되더라도 내부 수직 철근선을 GRID로
             * 오인하지 않도록 전역 반복성 + 셀 경계 근접성을 동시에 사용합니다.
             */
            if (model.VerticalXs.Count > 0 && shapeBoundaryXs.Count > 0)
            {
                double shapeBoundaryTolerance = Math.Max(model.MatchToleranceX * 1.5, typicalRowHeight * 0.030);

                for (i = model.VerticalXs.Count - 1; i >= 0; i--)
                {
                    if (!IsCadGridCoordinateNearAny(model.VerticalXs[i], shapeBoundaryXs, shapeBoundaryTolerance))
                    {
                        model.VerticalXs.RemoveAt(i);
                    }
                }
            }

            /*
             * 수평축은 전폭 선이라는 조건 외에 실제 DATA 행 경계와 가까운지도 확인합니다.
             * 철근형상 안의 긴 수평선이 표 전체 폭의 일부 블록과 우연히 합쳐져 GRID가 되는 것을 차단합니다.
             */
            if (model.HorizontalYs.Count > 0 && rowBoundaryYs.Count > 0)
            {
                double rowBoundaryTolerance = Math.Max(typicalRowHeight * 0.040, model.MatchToleranceY * 1.5);

                for (i = model.HorizontalYs.Count - 1; i >= 0; i--)
                {
                    if (!IsCadGridCoordinateNearAny(model.HorizontalYs[i], rowBoundaryYs, rowBoundaryTolerance))
                    {
                        model.HorizontalYs.RemoveAt(i);
                    }
                }
            }

            /*
             * OVIA 2026-08-07 _03 - 표 GRID 원본 객체 소유권 보강:
             * 형상 셀 X 경계가 인접 길이 열까지 넓게 복구되는 예외에서도, 형상/길이 사이의
             * 실제 세로 셀라인은 DATA 행 경계들을 반복 관통합니다. 현재 형상 셀 좌우 경계에
             * 가까운 축만 남기면 이 내부 셀라인이 GRID 모델에서 사라져 철근선과 연결된 채
             * 형상 JSON에 포함될 수 있습니다.
             *
             * 따라서 전체 표에서 검출한 세로축 중 실제 DATA 행 경계를 충분히 반복 통과하는
             * 축을 별도로 확정하고, 그 축을 구성한 CAD 원본 객체 Handle도 함께 보존합니다.
             * 철근형상 자체가 여러 행에서 비슷한 X에 반복되더라도 행 경계를 관통하지 않으면
             * 표 세로축으로 승인되지 않습니다.
             */
            model.PhysicalTableVerticalXs = BuildPhysicalCadTableVerticalAxes(
                model.AllVerticalXs,
                gridLines,
                rowBoundaryYs,
                model
            );
            PopulateCadTableGridSourceHandles(model, gridLines, rowBoundaryYs);

            return model;
        }

        private List<double> BuildPhysicalCadTableVerticalAxes(
            List<double> candidateXs,
            List<OviaGridLineSegment> gridLines,
            List<double> rowBoundaryYs,
            OviaCadTableGridModel model)
        {
            List<double> result = new List<double>();

            if (candidateXs == null || candidateXs.Count == 0
                || gridLines == null || gridLines.Count == 0
                || rowBoundaryYs == null || rowBoundaryYs.Count == 0
                || model == null)
            {
                return result;
            }

            List<double> uniqueRowBoundaryYs = MergeGridCoordinates(
                new List<double>(rowBoundaryYs),
                Math.Max(model.MergeTolerance, model.MatchToleranceY),
                true
            );
            double coordinateTolerance = Math.Max(model.AxisTolerance * 1.5, model.MergeTolerance);
            double boundaryTolerance = Math.Max(model.AxisTolerance * 1.75, model.TypicalRowHeight * 0.015);
            double headerExtensionRequired = Math.Max(model.TypicalRowHeight * 0.20, boundaryTolerance * 2.0);
            int requiredBoundaryHits = uniqueRowBoundaryYs.Count <= 3
                ? Math.Min(2, uniqueRowBoundaryYs.Count)
                : Math.Max(3, (int)Math.Ceiling(uniqueRowBoundaryYs.Count * 0.55));
            int candidateIndex;

            for (candidateIndex = 0; candidateIndex < candidateXs.Count; candidateIndex++)
            {
                double candidateX = candidateXs[candidateIndex];
                HashSet<int> hitBoundaryIndexes = new HashSet<int>();
                bool hasHeaderBandEvidence = false;
                int segmentIndex;

                for (segmentIndex = 0; segmentIndex < gridLines.Count; segmentIndex++)
                {
                    OviaGridLineSegment segment = gridLines[segmentIndex];

                    if (segment == null)
                    {
                        continue;
                    }

                    double dx = Math.Abs(segment.X2 - segment.X1);
                    double dy = Math.Abs(segment.Y2 - segment.Y1);

                    if (dx > model.AxisTolerance || dy <= model.AxisTolerance)
                    {
                        continue;
                    }

                    double segmentX = (segment.X1 + segment.X2) / 2.0;

                    if (Math.Abs(segmentX - candidateX) > coordinateTolerance)
                    {
                        continue;
                    }

                    double segmentMinY = Math.Min(segment.Y1, segment.Y2);
                    double segmentMaxY = Math.Max(segment.Y1, segment.Y2);

                    if (segmentMinY <= model.DataMaxY + boundaryTolerance
                        && segmentMaxY >= model.DataMaxY + headerExtensionRequired)
                    {
                        hasHeaderBandEvidence = true;
                    }

                    double segmentLength = segmentMaxY - segmentMinY;
                    bool isLongTableAxisSegment = segmentLength >= model.TypicalRowHeight * 1.45;
                    int boundaryIndex;

                    if (isLongTableAxisSegment)
                    {
                        /*
                         * 하나의 연속 세로선이 여러 DATA 행을 관통하는 일반 GRID입니다.
                         * 선분 내부에 실제로 포함되는 행 경계만 누적합니다.
                         */
                        for (boundaryIndex = 0; boundaryIndex < uniqueRowBoundaryYs.Count; boundaryIndex++)
                        {
                            double boundaryY = uniqueRowBoundaryYs[boundaryIndex];

                            if (boundaryY >= segmentMinY - boundaryTolerance
                                && boundaryY <= segmentMaxY + boundaryTolerance)
                            {
                                hitBoundaryIndexes.Add(boundaryIndex);
                            }
                        }
                    }
                    else
                    {
                        /*
                         * 셀마다 잘린 세로 GRID는 한 행 높이와 거의 같고 양 끝점이 서로 다른
                         * 수평 행 경계에 정확히 닿습니다. 한쪽 끝만 경계에 가까운 ㄱ/U형 수직
                         * 철근선은 이 조건을 만족하지 않으므로 반복 형상도 GRID로 승격되지 않습니다.
                         */
                        int minBoundaryIndex = -1;
                        int maxBoundaryIndex = -1;
                        double minBoundaryDistance = Double.MaxValue;
                        double maxBoundaryDistance = Double.MaxValue;

                        for (boundaryIndex = 0; boundaryIndex < uniqueRowBoundaryYs.Count; boundaryIndex++)
                        {
                            double boundaryY = uniqueRowBoundaryYs[boundaryIndex];
                            double minDistance = Math.Abs(boundaryY - segmentMinY);
                            double maxDistance = Math.Abs(boundaryY - segmentMaxY);

                            if (minDistance <= boundaryTolerance && minDistance < minBoundaryDistance)
                            {
                                minBoundaryDistance = minDistance;
                                minBoundaryIndex = boundaryIndex;
                            }

                            if (maxDistance <= boundaryTolerance && maxDistance < maxBoundaryDistance)
                            {
                                maxBoundaryDistance = maxDistance;
                                maxBoundaryIndex = boundaryIndex;
                            }
                        }

                        if (minBoundaryIndex >= 0
                            && maxBoundaryIndex >= 0
                            && minBoundaryIndex != maxBoundaryIndex
                            && segmentLength >= model.TypicalRowHeight * 0.90)
                        {
                            hitBoundaryIndexes.Add(minBoundaryIndex);
                            hitBoundaryIndexes.Add(maxBoundaryIndex);
                        }
                    }
                }

                /*
                 * 이 목록은 현재 SHAPE 셀 내부로 들어온 인접 열 구분선을 찾는 보조 축입니다.
                 * 실제 열 구분선은 DATA 영역뿐 아니라 표 헤더 밴드까지 이어지므로 헤더 연속성도
                 * 필수로 요구합니다. DATA 행 안에서만 반복되는 동일 철근형상은 승인하지 않습니다.
                 */
                if (hitBoundaryIndexes.Count >= requiredBoundaryHits && hasHeaderBandEvidence)
                {
                    result.Add(candidateX);
                }
            }

            return MergeGridCoordinates(result, model.MergeTolerance, true);
        }

        private void PopulateCadTableGridSourceHandles(
            OviaCadTableGridModel model,
            List<OviaGridLineSegment> gridLines,
            List<double> rowBoundaryYs)
        {
            if (model == null || gridLines == null || gridLines.Count == 0)
            {
                return;
            }

            List<double> uniqueRowBoundaryYs = rowBoundaryYs == null
                ? new List<double>()
                : MergeGridCoordinates(
                    new List<double>(rowBoundaryYs),
                    Math.Max(model.MergeTolerance, model.MatchToleranceY),
                    true
                );
            double coordinateToleranceX = Math.Max(model.AxisTolerance * 1.5, model.MergeTolerance);
            double coordinateToleranceY = Math.Max(model.AxisTolerance * 1.5, model.MatchToleranceY);
            double boundaryTolerance = Math.Max(model.AxisTolerance * 1.75, model.TypicalRowHeight * 0.015);
            double tableWidth = Math.Max(model.TableMaxX - model.TableMinX, 0.0001);
            int i;

            for (i = 0; i < gridLines.Count; i++)
            {
                OviaGridLineSegment segment = gridLines[i];

                if (segment == null || String.IsNullOrWhiteSpace(segment.SourceHandle))
                {
                    continue;
                }

                double dx = Math.Abs(segment.X2 - segment.X1);
                double dy = Math.Abs(segment.Y2 - segment.Y1);
                bool vertical = dx <= model.AxisTolerance && dy > model.AxisTolerance;
                bool horizontal = dy <= model.AxisTolerance && dx > model.AxisTolerance;
                bool verifiedGridOwner = false;

                if (vertical)
                {
                    double x = (segment.X1 + segment.X2) / 2.0;

                    if (IsCadGridCoordinateNearAny(x, model.PhysicalTableVerticalXs, coordinateToleranceX))
                    {
                        double segmentMinY = Math.Min(segment.Y1, segment.Y2);
                        double segmentMaxY = Math.Max(segment.Y1, segment.Y2);
                        int boundaryHitCount = 0;
                        int boundaryIndex;

                        for (boundaryIndex = 0; boundaryIndex < uniqueRowBoundaryYs.Count; boundaryIndex++)
                        {
                            double boundaryY = uniqueRowBoundaryYs[boundaryIndex];

                            if (boundaryY >= segmentMinY - boundaryTolerance
                                && boundaryY <= segmentMaxY + boundaryTolerance)
                            {
                                boundaryHitCount++;
                            }
                        }

                        verifiedGridOwner = boundaryHitCount >= Math.Min(2, uniqueRowBoundaryYs.Count);
                    }
                }
                else if (horizontal)
                {
                    double y = (segment.Y1 + segment.Y2) / 2.0;
                    verifiedGridOwner = IsCadGridCoordinateNearAny(
                            y,
                            model.HorizontalYs,
                            coordinateToleranceY
                        )
                        && dx >= tableWidth * 0.62;
                }

                if (verifiedGridOwner)
                {
                    model.GridSourceHandles.Add(segment.SourceHandle);
                }
            }
        }

        private double GetMedianCadGridValue(List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0.0;
            }

            List<double> sorted = new List<double>();
            int i;

            for (i = 0; i < values.Count; i++)
            {
                if (values[i] > 0.0001 && !Double.IsNaN(values[i]) && !Double.IsInfinity(values[i]))
                {
                    sorted.Add(values[i]);
                }
            }

            if (sorted.Count == 0)
            {
                return 0.0;
            }

            sorted.Sort();
            int middle = sorted.Count / 2;

            if ((sorted.Count % 2) == 0)
            {
                return (sorted[middle - 1] + sorted[middle]) / 2.0;
            }

            return sorted[middle];
        }

        private void FilterCadGridCoordinatesToRange(List<double> values, double minValue, double maxValue)
        {
            if (values == null)
            {
                return;
            }

            int i;

            for (i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] < minValue || values[i] > maxValue)
                {
                    values.RemoveAt(i);
                }
            }
        }

        private bool IsCadGridCoordinateNearAny(double value, List<double> coordinates, double tolerance)
        {
            if (coordinates == null || coordinates.Count == 0)
            {
                return false;
            }

            int i;

            for (i = 0; i < coordinates.Count; i++)
            {
                if (Math.Abs(value - coordinates[i]) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCadShapeLinePartOfCompactConnectedGeometry(
            List<OviaCadShapeElement> elements,
            OviaCadShapeElement target,
            double rowWidth,
            double rowHeight,
            double axisTolerance)
        {
            if (elements == null || target == null || target.Type != "LINE" || !target.HasWorldLine)
            {
                return false;
            }

            int targetIndex = elements.IndexOf(target);

            if (targetIndex < 0)
            {
                return false;
            }

            double shortSide = Math.Max(Math.Min(rowWidth, rowHeight), 0.0001);
            double endpointTolerance = Math.Max(axisTolerance * 2.0, shortSide * 0.018);
            HashSet<int> visited = new HashSet<int>();
            Queue<int> queue = new Queue<int>();
            visited.Add(targetIndex);
            queue.Enqueue(targetIndex);
            double componentMinX = Math.Min(target.WorldX1, target.WorldX2);
            double componentMaxX = Math.Max(target.WorldX1, target.WorldX2);
            double componentMinY = Math.Min(target.WorldY1, target.WorldY2);
            double componentMaxY = Math.Max(target.WorldY1, target.WorldY2);
            bool hasHorizontal = false;
            bool hasVertical = false;

            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                OviaCadShapeElement current = elements[currentIndex];

                if (current == null || current.Type != "LINE" || !current.HasWorldLine)
                {
                    continue;
                }

                double currentDx = Math.Abs(current.WorldX2 - current.WorldX1);
                double currentDy = Math.Abs(current.WorldY2 - current.WorldY1);

                if (currentDx >= currentDy)
                {
                    hasHorizontal = hasHorizontal || currentDx > axisTolerance;
                }
                else
                {
                    hasVertical = hasVertical || currentDy > axisTolerance;
                }

                componentMinX = Math.Min(componentMinX, Math.Min(current.WorldX1, current.WorldX2));
                componentMaxX = Math.Max(componentMaxX, Math.Max(current.WorldX1, current.WorldX2));
                componentMinY = Math.Min(componentMinY, Math.Min(current.WorldY1, current.WorldY2));
                componentMaxY = Math.Max(componentMaxY, Math.Max(current.WorldY1, current.WorldY2));

                int compareIndex;

                for (compareIndex = 0; compareIndex < elements.Count; compareIndex++)
                {
                    if (visited.Contains(compareIndex))
                    {
                        continue;
                    }

                    OviaCadShapeElement compare = elements[compareIndex];

                    if (compare == null || compare.Type != "LINE" || !compare.HasWorldLine)
                    {
                        continue;
                    }

                    bool sameStableSource = !String.IsNullOrWhiteSpace(current.SourceHandle)
                        && !String.IsNullOrWhiteSpace(compare.SourceHandle)
                        && String.Equals(current.SourceHandle, compare.SourceHandle, StringComparison.OrdinalIgnoreCase);
                    bool endpointConnected = AreCadShapeWorldLineEndpointsNear(current, compare, endpointTolerance);

                    if (!sameStableSource && !endpointConnected)
                    {
                        continue;
                    }

                    visited.Add(compareIndex);
                    queue.Enqueue(compareIndex);
                }
            }

            if (visited.Count < 3 || !hasHorizontal || !hasVertical)
            {
                return false;
            }

            double componentWidth = Math.Max(componentMaxX - componentMinX, 0.0);
            double componentHeight = Math.Max(componentMaxY - componentMinY, 0.0);

            /*
             * 셀 전체 테두리 또는 표 GRID 컴포넌트는 폭/높이 중 하나가 물리 셀 대부분을
             * 차지하므로 보호하지 않습니다. 커플러·작은 사각 훅처럼 국소적으로 닫히거나
             * 세 변 이상 연결된 형상만 GRID 소유권보다 우선하여 보존합니다.
             */
            return componentWidth <= rowWidth * 0.32
                && componentHeight <= rowHeight * 0.58;
        }

        private bool AreCadShapeWorldLineEndpointsNear(
            OviaCadShapeElement left,
            OviaCadShapeElement right,
            double tolerance)
        {
            if (left == null || right == null || !left.HasWorldLine || !right.HasWorldLine)
            {
                return false;
            }

            double toleranceSquared = tolerance * tolerance;

            return IsCadShapeWorldPointNear(left.WorldX1, left.WorldY1, right.WorldX1, right.WorldY1, toleranceSquared)
                || IsCadShapeWorldPointNear(left.WorldX1, left.WorldY1, right.WorldX2, right.WorldY2, toleranceSquared)
                || IsCadShapeWorldPointNear(left.WorldX2, left.WorldY2, right.WorldX1, right.WorldY1, toleranceSquared)
                || IsCadShapeWorldPointNear(left.WorldX2, left.WorldY2, right.WorldX2, right.WorldY2, toleranceSquared);
        }

        private bool IsCadShapeWorldPointNear(
            double x1,
            double y1,
            double x2,
            double y2,
            double toleranceSquared)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return dx * dx + dy * dy <= toleranceSquared;
        }

        private void RemoveCadShapeElementsMatchingTableGrid(
            List<OviaCadShapeElement> elements,
            OviaBarTableRow row,
            OviaCadTableGridModel gridModel,
            double width,
            double height)
        {
            if (elements == null || elements.Count == 0 || row == null)
            {
                return;
            }

            double rowMinX = Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX);
            double rowMaxX = Math.Max(row.ShapeCellMinX, row.ShapeCellMaxX);
            double rowMinY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
            double rowMaxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
            double rowHeight = Math.Max(rowMaxY - rowMinY, height);
            double rowWidth = Math.Max(rowMaxX - rowMinX, width);
            double axisTolerance = gridModel == null
                ? Math.Max(Math.Min(rowWidth, rowHeight) * 0.010, 0.03)
                : Math.Max(gridModel.AxisTolerance, 0.03);
            double matchToleranceX = gridModel == null
                ? Math.Max(rowWidth * 0.012, 0.05)
                : Math.Max(gridModel.MatchToleranceX, 0.05);
            double matchToleranceY = gridModel == null
                ? Math.Max(rowHeight * 0.060, 0.05)
                : Math.Max(gridModel.MatchToleranceY, 0.05);
            double physicalAxisToleranceX = Math.Max(
                axisTolerance * 1.35,
                Math.Min(matchToleranceX, rowWidth * 0.0045)
            );
            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "LINE" || !item.HasWorldLine)
                {
                    continue;
                }

                double dx = Math.Abs(item.WorldX2 - item.WorldX1);
                double dy = Math.Abs(item.WorldY2 - item.WorldY1);
                bool vertical = dx <= axisTolerance && dy > axisTolerance;
                bool horizontal = dy <= axisTolerance && dx > axisTolerance;

                if (!vertical && !horizontal)
                {
                    continue;
                }

                double originalDx = Math.Abs(item.OriginalWorldX2 - item.OriginalWorldX1);
                double originalDy = Math.Abs(item.OriginalWorldY2 - item.OriginalWorldY1);
                bool sourceOwnedByVerifiedGrid = gridModel != null
                    && !String.IsNullOrWhiteSpace(item.SourceHandle)
                    && gridModel.GridSourceHandles.Contains(item.SourceHandle);

                bool matchesVerifiedShapeBoundaryGrid = false;
                bool matchesPhysicalTableGrid = false;

                if (gridModel != null)
                {
                    if (vertical)
                    {
                        double coordinate = (item.WorldX1 + item.WorldX2) / 2.0;
                        matchesVerifiedShapeBoundaryGrid = IsCadGridCoordinateNearAny(
                            coordinate,
                            gridModel.VerticalXs,
                            matchToleranceX
                        );
                        matchesPhysicalTableGrid = IsCadGridCoordinateNearAny(
                            coordinate,
                            gridModel.PhysicalTableVerticalXs,
                            physicalAxisToleranceX
                        );
                    }
                    else
                    {
                        double coordinate = (item.WorldY1 + item.WorldY2) / 2.0;
                        matchesVerifiedShapeBoundaryGrid = IsCadGridCoordinateNearAny(
                            coordinate,
                            gridModel.HorizontalYs,
                            matchToleranceY
                        );
                    }
                }

                /*
                 * 원본 CAD 객체 소유권은 가장 강한 근거이지만, 하나의 Polyline이 여러 선분을
                 * 소유하는 예외를 고려해 현재 선분도 실제 GRID 축과 일치할 때만 제거합니다.
                 * 철근선이 셀라인에 닿거나 가까워도 별도 Entity이거나 GRID 축과 다르면 보존됩니다.
                 */
                bool sourceSegmentMatchesVerifiedGrid = sourceOwnedByVerifiedGrid
                    && ((vertical && (matchesVerifiedShapeBoundaryGrid || matchesPhysicalTableGrid))
                        || (horizontal && matchesVerifiedShapeBoundaryGrid));
                bool compactConnectedShape = sourceSegmentMatchesVerifiedGrid
                    && IsCadShapeLinePartOfCompactConnectedGeometry(
                        elements,
                        item,
                        rowWidth,
                        rowHeight,
                        axisTolerance);

                if (sourceSegmentMatchesVerifiedGrid && !compactConnectedShape)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                bool extendsBeyondCurrentCell = vertical
                    ? originalDy >= rowHeight * 1.20
                    : originalDx >= rowWidth * 1.20;
                bool fillsCurrentCellBoundary = vertical
                    ? dy >= rowHeight * 0.965
                    : dx >= rowWidth * 0.965;
                bool liesOnCurrentCellBoundary;

                if (vertical)
                {
                    double coordinate = (item.WorldX1 + item.WorldX2) / 2.0;
                    liesOnCurrentCellBoundary = Math.Abs(coordinate - rowMinX) <= matchToleranceX
                        || Math.Abs(coordinate - rowMaxX) <= matchToleranceX;
                }
                else
                {
                    double coordinate = (item.WorldY1 + item.WorldY2) / 2.0;
                    liesOnCurrentCellBoundary = Math.Abs(coordinate - rowMinY) <= matchToleranceY
                        || Math.Abs(coordinate - rowMaxY) <= matchToleranceY;
                }

                if (matchesVerifiedShapeBoundaryGrid
                    && (extendsBeyondCurrentCell || (fillsCurrentCellBoundary && liesOnCurrentCellBoundary)))
                {
                    elements.RemoveAt(i);
                    continue;
                }

                /*
                 * 형상 셀 경계가 잘못 넓어진 경우에는 형상/길이 사이 세로 셀라인이 셀 내부에
                 * 놓이므로 liesOnCurrentCellBoundary가 false가 됩니다. 이때도 전체 DATA 행 경계를
                 * 반복 관통해 확정된 물리 표 축과 일치하고 원본 선이 현재 행을 넘어 이어지면
                 * 테이블선으로 제거합니다. 짧은 ㄱ/U형 수직 철근선은 보존됩니다.
                 */
                if (vertical && matchesPhysicalTableGrid && extendsBeyondCurrentCell)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                /*
                 * GRID 모델을 만들 수 없는 특수 도면의 보수적 fallback입니다.
                 * 원본 선분 자체가 행/셀 전체를 관통하고 실제 셀 네 경계와 거의 일치할 때만 제거합니다.
                 * 셀 내부의 철근선은 길이가 길어도 이 조건으로 삭제하지 않습니다.
                 */
                bool allowVerticalFallback = gridModel == null || gridModel.VerticalXs.Count == 0;
                bool allowHorizontalFallback = gridModel == null || gridModel.HorizontalYs.Count == 0;

                if ((vertical && !allowVerticalFallback) || (horizontal && !allowHorizontalFallback))
                {
                    continue;
                }

                if (vertical)
                {
                    double coordinate = (item.WorldX1 + item.WorldX2) / 2.0;
                    bool atCellSide = Math.Abs(coordinate - rowMinX) <= matchToleranceX
                        || Math.Abs(coordinate - rowMaxX) <= matchToleranceX;
                    bool spansOriginalRow = originalDy >= rowHeight * 0.92;

                    if (atCellSide && spansOriginalRow)
                    {
                        elements.RemoveAt(i);
                    }
                }
                else
                {
                    double coordinate = (item.WorldY1 + item.WorldY2) / 2.0;
                    bool atRowEdge = Math.Abs(coordinate - rowMinY) <= matchToleranceY
                        || Math.Abs(coordinate - rowMaxY) <= matchToleranceY;
                    bool spansOriginalCell = originalDx >= rowWidth * 0.92;

                    if (atRowEdge && spansOriginalCell)
                    {
                        elements.RemoveAt(i);
                    }
                }
            }
        }

        private void RemoveExactCadShapeCellBoundaryLines(
            List<OviaCadShapeElement> elements,
            OviaBarTableRow row,
            OviaCadTableGridModel gridModel,
            double width,
            double height)
        {
            if (elements == null || elements.Count == 0 || row == null || !row.HasShapeCellBounds())
            {
                return;
            }

            double rowMinX = Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX);
            double rowMaxX = Math.Max(row.ShapeCellMinX, row.ShapeCellMaxX);
            double rowMinY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
            double rowMaxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
            double rowWidth = Math.Max(rowMaxX - rowMinX, width);
            double rowHeight = Math.Max(rowMaxY - rowMinY, height);
            double axisTolerance = gridModel == null
                ? Math.Max(Math.Min(rowWidth, rowHeight) * 0.004, 0.02)
                : Math.Max(gridModel.AxisTolerance * 0.75, 0.02);
            double boundaryToleranceX = Math.Max(rowWidth * 0.008, axisTolerance);
            double boundaryToleranceY = Math.Max(rowHeight * 0.008, axisTolerance);
            int i;

            /*
             * SelectCrossingWindow로 선택된 긴 표 세로선/가로선은 셀 사각형으로 클리핑된 뒤
             * 정확히 셀 네 변을 이루는 선분이 됩니다. 전역 GRID 검출이 블록/Proxy 구조 때문에
             * 일부 실패하더라도, 셀 경계와 일치하면서 해당 변의 96.5% 이상을 덮는 선은 확실한
             * 표 경계입니다. 내부 철근선은 셀 변 전체를 거의 덮지 않으므로 이 조건으로 삭제하지 않습니다.
             */
            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "LINE" || !item.HasWorldLine)
                {
                    continue;
                }

                double dx = Math.Abs(item.WorldX2 - item.WorldX1);
                double dy = Math.Abs(item.WorldY2 - item.WorldY1);
                bool vertical = dx <= axisTolerance && dy > axisTolerance;
                bool horizontal = dy <= axisTolerance && dx > axisTolerance;

                if (vertical)
                {
                    double x = (item.WorldX1 + item.WorldX2) / 2.0;
                    bool atSide = Math.Abs(x - rowMinX) <= boundaryToleranceX
                        || Math.Abs(x - rowMaxX) <= boundaryToleranceX;

                    if (atSide && dy >= rowHeight * 0.965)
                    {
                        elements.RemoveAt(i);
                    }
                }
                else if (horizontal)
                {
                    double y = (item.WorldY1 + item.WorldY2) / 2.0;
                    bool atEdge = Math.Abs(y - rowMinY) <= boundaryToleranceY
                        || Math.Abs(y - rowMaxY) <= boundaryToleranceY;

                    if (atEdge && dx >= rowWidth * 0.965)
                    {
                        elements.RemoveAt(i);
                    }
                }
            }
        }

        private void CaptureCadShapeFilesForRows(Editor ed, Database db, string csvFilePath, List<OviaBarTableRow> rows, OviaCadTableGridModel gridModel)
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
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_NO_CELL_BOUNDS";
                    ed.WriteMessage("\nOVIA 형상 진단: 번호 " + (row.MarkNo == "" ? row.No.ToString() : row.MarkNo) + "의 철근형상 셀 경계를 찾지 못했습니다.\n");
                    continue;
                }

                Point3d minPoint = new Point3d(row.ShapeCellMinX, row.ShapeCellMinY, 0);
                Point3d maxPoint = new Point3d(row.ShapeCellMaxX, row.ShapeCellMaxY, 0);
                List<OviaCadShapeElement> elements = ExtractCadShapeElementsByWindow(ed, db, minPoint, maxPoint, row, gridModel);

                if (elements.Count == 0)
                {
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_EMPTY";
                    string entitySummary = GetCadShapeEntityTypeSummary(ed, db, minPoint, maxPoint);
                    ed.WriteMessage(
                        "\nOVIA 형상 진단: 번호 " + (row.MarkNo == "" ? row.No.ToString() : row.MarkNo)
                        + "의 형상 요소가 0개입니다. 셀 객체=" + entitySummary + "\n"
                    );
                    continue;
                }

                int expectedDimensionCount;
                int retainedDimensionCount;
                int retainedGeometryCount;

                if (IsCadShapeCaptureSeverelyIncomplete(
                    row,
                    elements,
                    out expectedDimensionCount,
                    out retainedDimensionCount,
                    out retainedGeometryCount))
                {
                    row.CadShapeJsonPath = "";
                    row.CadShapeTextValues = BuildCadShapeTextValues(elements);
                    row.ShapeSource = "CAD";
                    row.ShapeStatus = "CAD_CAPTURE_INCOMPLETE";
                    ed.WriteMessage(
                        "\nOVIA 형상 안전차단: 번호 "
                        + (row.MarkNo == "" ? row.No.ToString(CultureInfo.InvariantCulture) : row.MarkNo)
                        + "의 원본 치수/지오메트리가 과도하게 누락되어 해당 행의 형상 JSON을 생성하지 않습니다."
                        + " 기대치수=" + expectedDimensionCount.ToString(CultureInfo.InvariantCulture)
                        + ", 보존치수=" + retainedDimensionCount.ToString(CultureInfo.InvariantCulture)
                        + ", 지오메트리=" + retainedGeometryCount.ToString(CultureInfo.InvariantCulture)
                        + ", 경계출처=" + (row.ShapeCellBoundsSource == null ? "" : row.ShapeCellBoundsSource)
                        + ", 셀폭=" + Math.Abs(row.ShapeCellMaxX - row.ShapeCellMinX).ToString("0.###", CultureInfo.InvariantCulture)
                        + ", 셀높이=" + Math.Abs(row.ShapeCellMaxY - row.ShapeCellMinY).ToString("0.###", CultureInfo.InvariantCulture)
                        + "\n"
                    );
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
                     * OVIA 2026-08-07 _07 - 일반 표 문자 스캐너가 Dimension 표시문자를 읽지 못한
                     * 형상 셀의 CAD 캡처 문자 승격:
                     *
                     * 일부 도면은 형상 치수가 DBText/Attribute가 아니라 Dimension 등의
                     * 표시 구성요소로 작성되어 있습니다. 표 데이터용 ExtractRowsByWindow는
                     * Dimension 자체를 일반 DATA 문자로 펼치지 않으므로 ShapeRawText가 빈칸일 수 있지만,
                     * CAD 형상 수집기는 표시 객체를 Explode하여 실제 화면 치수 TEXT를 확보할 수 있습니다.
                     *
                     * 후단 필터를 통과한 CAD 형상 TEXT는 이미 물리 SHAPE 셀 소유권과 실제 지오메트리
                     * 근접성 검증을 마친 값이므로, 원본 형상문자가 비어 있을 때만 ShapeText/ShapeRawText의
                     * fallback으로 승격합니다. 기존 원본 문자가 있는 행은 절대 덮어쓰지 않습니다.
                     */
                    if ((row.ShapeRawText == null || row.ShapeRawText.Trim() == "")
                        && row.CadShapeTextValues != null
                        && row.CadShapeTextValues.Trim() != "")
                    {
                        string recoveredShapeText = row.CadShapeTextValues.Replace("|", " ").Trim();
                        row.ShapeText = recoveredShapeText;
                        row.ShapeRawText = recoveredShapeText;
                    }

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

        private bool IsCadShapeCaptureSeverelyIncomplete(
            OviaBarTableRow row,
            List<OviaCadShapeElement> elements,
            out int expectedDimensionCount,
            out int retainedDimensionCount,
            out int retainedGeometryCount)
        {
            expectedDimensionCount = 0;
            retainedDimensionCount = 0;
            retainedGeometryCount = 0;

            if (row != null && row.ShapeRawText != null && row.ShapeRawText.Trim() != "")
            {
                expectedDimensionCount = CountExpectedCadShapeDimensionValues(row.ShapeRawText);
            }

            if (elements != null)
            {
                int i;

                for (i = 0; i < elements.Count; i++)
                {
                    OviaCadShapeElement item = elements[i];

                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Type == "TEXT")
                    {
                        if (row != null && ShapeRawTextContainsNumericValue(row.ShapeRawText, item.Text))
                        {
                            retainedDimensionCount++;
                        }
                    }
                    else if (item.Type == "LINE" || item.Type == "ARC" || item.Type == "CIRCLE")
                    {
                        retainedGeometryCount++;
                    }
                }
            }

            if (retainedGeometryCount <= 0)
            {
                return true;
            }

            if (expectedDimensionCount < 3)
            {
                return false;
            }

            int minimumRetainedDimensionCount = Math.Max(
                1,
                (int)Math.Ceiling(expectedDimensionCount * 0.50)
            );

            return retainedDimensionCount < minimumRetainedDimensionCount;
        }

        private int CountExpectedCadShapeDimensionValues(string shapeRawText)
        {
            if (shapeRawText == null || shapeRawText.Trim() == "")
            {
                return 0;
            }

            /*
             * 형상원본에는 A/B/C뿐 아니라 a1~aN, R1, H1 같은 내부 식별 키가 포함될 수 있습니다.
             * 일반 숫자 정규식으로 개수를 세면 키의 숫자까지 치수로 오인하여 복잡 형상을
             * CAD_CAPTURE_INCOMPLETE로 차단합니다. 영문자에 바로 붙은 숫자는 식별 키로 제외하고,
             * 실제 치수값과 각도값(예: 135%%D)만 기대 치수 개수에 포함합니다.
             */
            return GetExpectedCadShapeDimensionMatches(shapeRawText).Count;
        }

        private bool ValidateCapturedCadShapeCompleteness(
            List<OviaBarTableRow> rows,
            out string validationMessage)
        {
            validationMessage = "";

            if (rows == null || rows.Count == 0)
            {
                validationMessage = "검증할 DATA 행이 없습니다.";
                return false;
            }

            List<string> failedMarks = new List<string>();
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!String.Equals(row.ShapeStatus, "CAD_CAPTURED", StringComparison.OrdinalIgnoreCase)
                    || row.CadShapeJsonPath == null
                    || row.CadShapeJsonPath.Trim() == "")
                {
                    failedMarks.Add(row.MarkNo == ""
                        ? row.No.ToString(CultureInfo.InvariantCulture)
                        : row.MarkNo);
                }
            }

            if (failedMarks.Count == 0)
            {
                return true;
            }

            validationMessage = "형상 캡처 실패/불완전 번호: " + String.Join(", ", failedMarks.ToArray());
            return false;
        }

        private string GetCadShapeEntityTypeSummary(Editor ed, Database db, Point3d point1, Point3d point2)
        {
            if (ed == null || db == null)
            {
                return "없음";
            }

            PromptSelectionResult selectionResult = ed.SelectCrossingWindow(point1, point2);

            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
            {
                return "선택 객체 없음";
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

                    if (!counts.ContainsKey(typeName))
                    {
                        counts[typeName] = 0;
                    }

                    counts[typeName]++;
                }

                tr.Commit();
            }

            if (counts.Count == 0)
            {
                return "객체 없음";
            }

            List<string> parts = new List<string>();

            foreach (KeyValuePair<string, int> item in counts)
            {
                parts.Add(item.Key + "=" + item.Value.ToString(CultureInfo.InvariantCulture));
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return String.Join(", ", parts.ToArray());
        }

        private List<OviaCadShapeElement> ExtractCadShapeElementsByWindow(Editor ed, Database db, Point3d point1, Point3d point2, OviaBarTableRow row, OviaCadTableGridModel gridModel)
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
             * OVIA 2026-07-22 _05 - 표 전체 위상 기반 GRID 분리:
             *
             * 기존 방식은 한 행의 로컬 셀 안에서 "셀 폭/높이의 몇 %인가"를 기준으로 표선을 지웠습니다.
             * 하지만 CAD 철근형상이 Dimension/Block으로 작성된 경우 실제 가로 철근선도 셀 폭 대부분을
             * 차지하고, 선택 범위가 달라지면 같은 선이 표 경계로 오판될 수 있습니다.
             *
             * 이번 방식은 선택한 표 전체에서 여러 행을 관통하는 수직축과 여러 열을 가로지르는 수평축을
             * 먼저 GRID로 확정합니다. 형상 셀에서는 그 전역 GRID 좌표와 일치하는 선만 제거합니다.
             * 내부 철근선은 길이·위치·컴포넌트 비율만으로 삭제하지 않습니다.
             */
            RemoveInvalidCadShapeElements(elements);
            RemoveCadShapeTextsOutsidePhysicalCellBounds(elements, captureWidth, captureHeight);
            RemoveCadShapeElementsMatchingTableGrid(elements, row, gridModel, captureWidth, captureHeight);
            RemoveExactCadShapeCellBoundaryLines(elements, row, gridModel, captureWidth, captureHeight);
            RestoreCompactClosedCadShapePathSegments(elements, row, captureWidth, captureHeight);
            RemoveCadShapeHeaderLabelTexts(elements);
            KeepOnlyActualCadShapeElements(row, elements, captureWidth, captureHeight);
            RemoveDuplicateCadShapeElements(elements);
            RemoveOverlappingCadShapeGhostDimensionTextClusters(elements, captureWidth, captureHeight);
            RemoveExcessCadShapeNumericTexts(row, elements, captureWidth, captureHeight);

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
             * (DOWN)/(UP) 및 가장자리 치수 문자를 후보에 포함하기 위해 선택 객체 집합은 소폭
             * 확장합니다. LINE/Polyline/Curve 지오메트리는 아래의 클리핑 단계에서 물리 셀 Y와
             * SHAPE 셀의 제한된 수평 소유 범위 안쪽만 보존합니다. 경계에 걸친 소형 연결 형상은
             * 짧은 외곽선까지 유지하고, 주변 표선은 전역 GRID 필터에서 제거합니다.
             */
            double geometryOwnershipMarginX = GetCadShapeHorizontalGeometryOwnershipMargin(width, height);
            double selectionMarginLeft = Math.Max(
                Math.Max(width * 0.025, 0.03),
                geometryOwnershipMarginX);
            double selectionMarginRight = Math.Max(width * 0.25, 0.20);
            double selectionMarginY = Math.Max(height * 0.10, 0.05);
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

            /*
             * OVIA 2026-08-07 _05 - GRID 소유권 판정 전 좌표 중복 제거 금지:
             * 형상선과 표선이 같은 좌표에 겹치는 도면에서는 두 객체의 좌표 키가 같을 수 있습니다.
             * 이 단계에서 먼저 하나를 삭제하면 선택 순서에 따라 실제 형상 객체가 사라지고, 남은
             * 표 GRID 객체도 후단에서 제거되어 폐합 Polyline의 한 변만 누락됩니다.
             * 중복 제거는 표 GRID 원본 객체·물리 축 제거가 끝난 뒤 한 번만 수행합니다.
             */
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

            /*
             * CAD 화면에서 숨겨진 객체는 형상 JSON에도 들어가면 안 됩니다.
             * 특히 동적 블록의 AttributeReference가 Invisible 상태이거나 비활성 가시성 상태의
             * DBText가 원시 BlockTableRecord에 남아 있으면 CAD에는 보이지 않지만 OVIA에는 작은
             * 중복 숫자로 표시될 수 있습니다.
             */
            if (!IsCadShapeEntityVisible(tr, entity))
            {
                return;
            }

            double originX = minX;
            double topY = maxY;

            /*
             * OVIA 2026-08-07 _04 - 형상 셀 경계에 걸친 소형 연결 형상 보존:
             * 커플러·정착구·작은 사각 훅은 SHAPE 셀의 좌우 경계에 걸쳐 배치될 수 있습니다.
             * 객체 선택 범위에는 들어오더라도 지오메트리를 물리 셀 X에 즉시 클리핑하면
             * 경계 밖의 짧은 세로변만 사라지고, 셀 안쪽의 상·하 가로변과 반대편 세로변만
             * 남아 열린 사각형처럼 저장됩니다.
             *
             * 문자 소유권과 행 Y 경계는 기존 물리 셀을 그대로 사용하고, LINE/Polyline/Curve에만
             * 매우 작은 수평 소유 여유를 적용합니다. 표 세로선은 이후 전역 GRID 축·Entity
             * 소유권 필터에서 제거하므로, 실제 형상만 셀 경계 밖의 짧은 연결부까지 보존됩니다.
             */
            double geometryOwnershipMarginX = GetCadShapeHorizontalGeometryOwnershipMargin(width, height);
            double geometryMinX = minX - geometryOwnershipMarginX;
            double geometryMaxX = maxX + geometryOwnershipMarginX;

            Line line = entity as Line;

            if (line != null)
            {
                Point3d p1 = line.StartPoint.TransformBy(transform);
                Point3d p2 = line.EndPoint.TransformBy(transform);

                TryAddClippedCadShapeLineElement(
                    elements,
                    entity,
                    p1,
                    p2,
                    geometryMinX,
                    geometryMaxX,
                    minY,
                    maxY,
                    originX,
                    topY
                );
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
                if (CollectCadShapePolylineSegments(
                    polyline,
                    transform,
                    elements,
                    geometryMinX,
                    geometryMaxX,
                    minY,
                    maxY,
                    originX,
                    topY,
                    width,
                    height))
                {
                    return;
                }
            }

            Curve curve = entity as Curve;

            if (curve != null)
            {
                if (CollectCadShapeCurveSegments(
                    curve,
                    transform,
                    elements,
                    geometryMinX,
                    geometryMaxX,
                    minY,
                    maxY,
                    originX,
                    topY,
                    width,
                    height))
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
                bool preferEvaluatedDisplay = IsCadShapeEvaluatedBlock(blockReference, blockRecord);
                int blockElementStartCount = elements.Count;
                int blockTextStartCount = CountCadShapeTextElements(elements);
                bool collectAttributeReferences = true;

                /*
                 * 동적·익명 블록은 현재 화면 상태가 반영된 Explode 결과를 단일 원본으로 사용합니다.
                 * 이전 구현은 Explode가 성공한 뒤에도 AttributeCollection을 다시 전부 합쳤기 때문에,
                 * 비활성 가시성 상태에 남아 있는 200/400/500 AttributeReference가 화면 표시 문자와
                 * 별도 좌표로 추가되어 11번처럼 셀 중앙에 작은 숫자 군집이 생길 수 있었습니다.
                 *
                 * Explode 결과에 TEXT가 하나라도 있으면 현재 표시 문자가 이미 확보된 것으로 보고
                 * AttributeCollection을 다시 합치지 않습니다. Explode가 선만 반환하거나 완전히 실패한
                 * 예외 블록에서만 가시 AttributeReference와 원시 정의를 fallback으로 사용합니다.
                 */
                if (preferEvaluatedDisplay)
                {
                    CollectCadShapeElementsFromExplodedBlock(
                        tr,
                        blockReference,
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

                    int explodedTextCount = CountCadShapeTextElements(elements) - blockTextStartCount;

                    if (elements.Count == blockElementStartCount)
                    {
                        CollectCadShapeElementsFromBlockRecord(
                            tr,
                            blockReference,
                            blockRecord,
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
                    else if (explodedTextCount > 0)
                    {
                        collectAttributeReferences = false;
                    }
                }
                else
                {
                    CollectCadShapeElementsFromBlockRecord(
                        tr,
                        blockReference,
                        blockRecord,
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

                    if (elements.Count == blockElementStartCount)
                    {
                        CollectCadShapeElementsFromExplodedBlock(
                            tr,
                            blockReference,
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
                }

                if (collectAttributeReferences)
                {
                    /*
                     * 일반 정적 블록 또는 Explode가 TEXT를 제공하지 못한 예외 블록만 실제 배치
                     * AttributeReference를 보조 수집합니다. 레이어 OFF/FROZEN, Visible=false,
                     * Invisible=true 속성은 공통 가시성 검사에서 제외합니다.
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

        private bool IsCadShapeEntityVisible(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                if (!entity.Visible)
                {
                    return false;
                }
            }
            catch
            {
                // 일부 Proxy 객체는 Visible 조회를 지원하지 않을 수 있으므로 나머지 수집을 계속합니다.
            }

            AttributeReference attributeReference = entity as AttributeReference;

            if (attributeReference != null)
            {
                try
                {
                    if (attributeReference.Invisible)
                    {
                        return false;
                    }
                }
                catch
                {
                }
            }

            AttributeDefinition attributeDefinition = entity as AttributeDefinition;

            if (attributeDefinition != null)
            {
                try
                {
                    if (attributeDefinition.Invisible)
                    {
                        return false;
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        private bool IsCadShapeEntityVisible(Transaction tr, Entity entity)
        {
            if (!IsCadShapeEntityVisible(entity))
            {
                return false;
            }

            /*
             * Entity.Visible은 객체 자체의 표시 플래그만 확인하며 레이어 OFF/FROZEN 상태까지
             * 보장하지 않습니다. 화면에 보이지 않는 레이어의 문자·속성이 동적 블록 내부에
             * 남아 있으면 OVIA 형상에만 나타날 수 있으므로 데이터베이스 상의 레이어 표시 상태도
             * 함께 확인합니다. Explode로 생성된 비DB 객체는 LayerId를 읽지 못할 수 있으므로
             * 그 경우에는 객체 자체 Visible 판정 결과를 유지합니다.
             */
            if (tr != null && entity != null)
            {
                try
                {
                    ObjectId layerId = entity.LayerId;

                    if (!layerId.IsNull)
                    {
                        LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead, false) as LayerTableRecord;

                        if (layer != null && (layer.IsOff || layer.IsFrozen))
                        {
                            return false;
                        }
                    }
                }
                catch
                {
                    // Explode 임시 객체·Proxy 객체는 LayerId 조회가 실패할 수 있습니다.
                }
            }

            return true;
        }

        private int CountCadShapeTextElements(List<OviaCadShapeElement> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return 0;
            }

            int count = 0;
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item != null
                    && item.Type == "TEXT"
                    && item.Text != null
                    && item.Text.Trim() != "")
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsCadShapeEvaluatedBlock(BlockReference blockReference, BlockTableRecord blockRecord)
        {
            if (blockReference == null)
            {
                return false;
            }

            try
            {
                if (blockReference.IsDynamicBlock)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                ObjectId dynamicBlockId = blockReference.DynamicBlockTableRecord;

                if (!dynamicBlockId.IsNull && dynamicBlockId != blockReference.BlockTableRecord)
                {
                    return true;
                }
            }
            catch
            {
            }

            if (blockRecord != null)
            {
                try
                {
                    if (blockRecord.IsAnonymous
                        || (blockRecord.Name != null
                            && blockRecord.Name.StartsWith("*U", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private void CollectCadShapeElementsFromBlockRecord(
            Transaction tr,
            BlockReference blockReference,
            BlockTableRecord blockRecord,
            Matrix3d parentTransform,
            List<OviaCadShapeElement> elements,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height,
            int depth)
        {
            if (tr == null
                || blockReference == null
                || blockRecord == null
                || elements == null
                || depth > 8)
            {
                return;
            }

            Matrix3d nextTransform = parentTransform * blockReference.BlockTransform;

            foreach (ObjectId childId in blockRecord)
            {
                Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                if (childEntity == null)
                {
                    continue;
                }

                CollectCadShapeElementsFromEntity(
                    tr,
                    childEntity,
                    nextTransform,
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
                /*
                 * 일반 형상 치수는 셀 경계에 아주 조금 걸친 경우만 허용합니다.
                 * (DOWN)/(UP)만 오른쪽 확장 범위를 사용합니다. 일반 숫자에 큰 여유를 주면
                 * 인접 길이·수량·총길이·중량 문자가 형상 후보에 섞이므로 방향 문자와 분리합니다.
                 */
                double marginX = Math.Max(width * 0.025, 0.03);
                double marginY = Math.Max(height * 0.08, 0.04);

                if (allowDirectionOutsideMargin)
                {
                    marginX = Math.Max(marginX, width * 0.25);
                    marginY = Math.Max(marginY, height * 0.12);
                }

                return textMaxX >= minX - marginX
                    && textMinX <= maxX + marginX
                    && textMaxY >= minY - marginY
                    && textMinY <= maxY + marginY;
            }

            return referencePoint.X >= minX - Math.Max(width * 0.025, 0.03)
                && referencePoint.X <= maxX + Math.Max(width * (allowDirectionOutsideMargin ? 0.25 : 0.025), 0.03)
                && referencePoint.Y >= minY - Math.Max(height * (allowDirectionOutsideMargin ? 0.12 : 0.08), 0.04)
                && referencePoint.Y <= maxY + Math.Max(height * (allowDirectionOutsideMargin ? 0.12 : 0.08), 0.04);
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
                || typeName.Equals("Shape", StringComparison.OrdinalIgnoreCase)
                || typeName.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Mline", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("MPolygon", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Proxy", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Region", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CollectCadShapeElementsFromExplodedBlock(
            Transaction tr,
            BlockReference blockReference,
            Matrix3d parentTransform,
            List<OviaCadShapeElement> elements,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double width,
            double height,
            int depth)
        {
            if (blockReference == null || elements == null || depth > 8)
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

            foreach (DBObject explodedObject in explodedObjects)
            {
                Entity explodedEntity = explodedObject as Entity;

                try
                {
                    if (explodedEntity == null)
                    {
                        continue;
                    }

                    CollectCadShapeElementsFromEntity(
                        tr,
                        explodedEntity,
                        parentTransform,
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
            double clipMinX,
            double clipMaxX,
            double clipMinY,
            double clipMaxY,
            double originX,
            double topY,
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

                    if (TryAddClippedCadShapeLineElement(
                        elements,
                        polyline,
                        startPoint,
                        endPoint,
                        clipMinX,
                        clipMaxX,
                        clipMinY,
                        clipMaxY,
                        originX,
                        topY))
                    {
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
                        && TryAddClippedCadShapeLineElement(
                            elements,
                            polyline,
                            previousPoint,
                            currentPoint,
                            clipMinX,
                            clipMaxX,
                            clipMinY,
                            clipMaxY,
                            originX,
                            topY))
                    {
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
            double clipMinX,
            double clipMaxX,
            double clipMinY,
            double clipMaxY,
            double originX,
            double topY,
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
                        && TryAddClippedCadShapeLineElement(
                            elements,
                            curve,
                            previousPoint,
                            currentPoint,
                            clipMinX,
                            clipMaxX,
                            clipMinY,
                            clipMaxY,
                            originX,
                            topY))
                    {
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

        private double GetCadShapeHorizontalGeometryOwnershipMargin(double width, double height)
        {
            double safeWidth = Math.Max(Math.Abs(width), 0.0001);
            double safeHeight = Math.Max(Math.Abs(height), 0.0001);
            double minimumMargin = Math.Max(safeWidth * 0.008, 0.03);
            double preferredMargin = Math.Max(safeWidth * 0.035, minimumMargin);
            double rowHeightCap = Math.Max(safeHeight * 0.20, minimumMargin);

            /*
             * 폭이 매우 넓은 형상 셀에서도 인접 데이터 열을 과도하게 소유하지 않도록
             * 행 높이의 20%를 상한으로 사용합니다. 일반 표에서는 형상 셀 폭의 약 3.5%가
             * 적용되어 경계에 걸친 커플러·작은 사각 훅의 한 변을 보존합니다.
             */
            return Math.Min(preferredMargin, rowHeightCap);
        }

        private string GetStableCadEntityHandle(Entity entity)
        {
            if (entity == null)
            {
                return "";
            }

            try
            {
                string value = entity.Handle.ToString();

                if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }

                return value;
            }
            catch
            {
                return "";
            }
        }

        private bool TryAddClippedCadShapeLineElement(
            List<OviaCadShapeElement> elements,
            Entity sourceEntity,
            Point3d point1,
            Point3d point2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double originX,
            double topY
        )
        {
            if (elements == null || sourceEntity == null
                || !IsFiniteCadShapePoint(point1) || !IsFiniteCadShapePoint(point2))
            {
                return false;
            }

            Point3d clippedPoint1;
            Point3d clippedPoint2;

            if (!TryClipCadShapeLineToCell(
                point1,
                point2,
                minX,
                maxX,
                minY,
                maxY,
                out clippedPoint1,
                out clippedPoint2))
            {
                return false;
            }

            if (clippedPoint1.DistanceTo(clippedPoint2) <= 0.000001)
            {
                return false;
            }

            OviaCadShapeElement item = new OviaCadShapeElement();
            item.Type = "LINE";
            item.ColorIndex = GetEntityColorIndex(sourceEntity);
            item.X1 = NormalizeCadShapeX(clippedPoint1.X, originX);
            item.Y1 = NormalizeCadShapeY(clippedPoint1.Y, topY);
            item.X2 = NormalizeCadShapeX(clippedPoint2.X, originX);
            item.Y2 = NormalizeCadShapeY(clippedPoint2.Y, topY);
            item.HasWorldLine = true;
            item.WorldX1 = clippedPoint1.X;
            item.WorldY1 = clippedPoint1.Y;
            item.WorldX2 = clippedPoint2.X;
            item.WorldY2 = clippedPoint2.Y;
            item.OriginalWorldX1 = point1.X;
            item.OriginalWorldY1 = point1.Y;
            item.OriginalWorldX2 = point2.X;
            item.OriginalWorldY2 = point2.Y;
            item.SourceType = sourceEntity.GetType().Name;
            item.SourceClosedPath = IsClosedCadShapePolylineSource(sourceEntity);

            item.SourceHandle = GetStableCadEntityHandle(sourceEntity);
            item.SourceIdentity = String.IsNullOrWhiteSpace(item.SourceHandle)
                ? "RUNTIME:" + sourceEntity.GetHashCode().ToString(CultureInfo.InvariantCulture)
                : "HANDLE:" + item.SourceHandle;

            elements.Add(item);
            return true;
        }

        private bool TryClipCadShapeLineToCell(
            Point3d point1,
            Point3d point2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point3d clippedPoint1,
            out Point3d clippedPoint2)
        {
            clippedPoint1 = Point3d.Origin;
            clippedPoint2 = Point3d.Origin;

            double width = Math.Abs(maxX - minX);
            double height = Math.Abs(maxY - minY);
            double tolerance = Math.Max(Math.Min(width, height) * 0.0025, 0.01);
            double left = Math.Min(minX, maxX) - tolerance;
            double right = Math.Max(minX, maxX) + tolerance;
            double bottom = Math.Min(minY, maxY) - tolerance;
            double top = Math.Max(minY, maxY) + tolerance;
            double dx = point2.X - point1.X;
            double dy = point2.Y - point1.Y;
            double u1 = 0.0;
            double u2 = 1.0;

            if (!ClipCadShapeLineParameter(-dx, point1.X - left, ref u1, ref u2)
                || !ClipCadShapeLineParameter(dx, right - point1.X, ref u1, ref u2)
                || !ClipCadShapeLineParameter(-dy, point1.Y - bottom, ref u1, ref u2)
                || !ClipCadShapeLineParameter(dy, top - point1.Y, ref u1, ref u2))
            {
                return false;
            }

            clippedPoint1 = new Point3d(
                point1.X + u1 * dx,
                point1.Y + u1 * dy,
                point1.Z + u1 * (point2.Z - point1.Z)
            );
            clippedPoint2 = new Point3d(
                point1.X + u2 * dx,
                point1.Y + u2 * dy,
                point1.Z + u2 * (point2.Z - point1.Z)
            );

            return IsFiniteCadShapePoint(clippedPoint1)
                && IsFiniteCadShapePoint(clippedPoint2);
        }

        private bool ClipCadShapeLineParameter(double p, double q, ref double u1, ref double u2)
        {
            if (Math.Abs(p) <= 0.000000001)
            {
                return q >= 0.0;
            }

            double ratio = q / p;

            if (p < 0.0)
            {
                if (ratio > u2)
                {
                    return false;
                }

                if (ratio > u1)
                {
                    u1 = ratio;
                }
            }
            else
            {
                if (ratio < u1)
                {
                    return false;
                }

                if (ratio < u2)
                {
                    u2 = ratio;
                }
            }

            return true;
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

        private bool IsLikelyCadShapeCellBorderLine(
            OviaCadShapeElement item,
            double width,
            double height,
            double axisTolerance,
            double edgeToleranceX,
            double edgeToleranceY)
        {
            if (item == null || item.Type != "LINE")
            {
                return false;
            }

            double dx = Math.Abs(item.X2 - item.X1);
            double dy = Math.Abs(item.Y2 - item.Y1);
            double centerX = (item.X1 + item.X2) / 2.0;
            double centerY = (item.Y1 + item.Y2) / 2.0;
            double minX = Math.Min(item.X1, item.X2);
            double maxX = Math.Max(item.X1, item.X2);
            double minY = Math.Min(item.Y1, item.Y2);
            double maxY = Math.Max(item.Y1, item.Y2);
            bool horizontal = dy <= axisTolerance && dx > axisTolerance;
            bool vertical = dx <= axisTolerance && dy > axisTolerance;

            if (horizontal
                && dx >= width * 0.86
                && minX <= edgeToleranceX
                && maxX >= width - edgeToleranceX
                && (centerY <= edgeToleranceY || centerY >= height - edgeToleranceY))
            {
                return true;
            }

            if (vertical
                && dy >= height * 0.86
                && minY <= edgeToleranceY
                && maxY >= height - edgeToleranceY
                && (centerX <= edgeToleranceX || centerX >= width - edgeToleranceX))
            {
                return true;
            }

            return false;
        }

        private bool ShouldPreserveConnectedCadShapeLine(
            OviaCadShapeElement candidate,
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (candidate == null || candidate.Type != "LINE" || elements == null || elements.Count < 2)
            {
                return false;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.090, 0.10);
            double edgeToleranceX = Math.Max(width * 0.060, 0.05);
            double edgeToleranceY = Math.Max(height * 0.060, 0.05);
            double dx = Math.Abs(candidate.X2 - candidate.X1);
            double dy = Math.Abs(candidate.Y2 - candidate.Y1);
            bool horizontal = dy <= axisTolerance && dx > axisTolerance;
            bool vertical = dx <= axisTolerance && dy > axisTolerance;

            if (!horizontal && !vertical)
            {
                return true;
            }

            double minX = Math.Min(candidate.X1, candidate.X2);
            double maxX = Math.Max(candidate.X1, candidate.X2);
            double minY = Math.Min(candidate.Y1, candidate.Y2);
            double maxY = Math.Max(candidate.Y1, candidate.Y2);

            /*
             * 실제 셀 가로/세로 경계 자체는 다른 표선과 교차하므로 단순 연결성만으로 보존하면 안 됩니다.
             * 양쪽 셀 끝을 잇는 선은 먼저 진짜 테이블선 후보로 남겨 후속 필터가 제거하게 합니다.
             */
            if (horizontal && minX <= edgeToleranceX && maxX >= width - edgeToleranceX)
            {
                return false;
            }

            if (vertical
                && minY <= edgeToleranceY
                && maxY >= height - edgeToleranceY
                && ((candidate.X1 + candidate.X2) / 2.0 <= edgeToleranceX
                    || (candidate.X1 + candidate.X2) / 2.0 >= width - edgeToleranceX))
            {
                return false;
            }

            bool firstEndpointConnected = false;
            bool secondEndpointConnected = false;
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement other = elements[i];

                if (other == null || Object.ReferenceEquals(other, candidate) || other.Type == "TEXT")
                {
                    continue;
                }

                if (IsLikelyCadShapeCellBorderLine(
                    other,
                    width,
                    height,
                    axisTolerance,
                    edgeToleranceX,
                    edgeToleranceY))
                {
                    continue;
                }

                if (other.Type == "LINE")
                {
                    /*
                     * 블록/폴리라인 explode 결과는 화면상 연결되어도 끝점 좌표가 아주 조금 벌어질 수 있고,
                     * T자 접속은 상대 선분의 중간에 닿습니다. 끝점 대 끝점 거리만 사용하면 U형 하단선과
                     * 38번 연결선이 분리된 것으로 오판됩니다. 끝점에서 상대 선분까지의 최단거리로 판정합니다.
                     */
                    if (CadShapePointToLineSegmentDistance(
                        candidate.X1,
                        candidate.Y1,
                        other.X1,
                        other.Y1,
                        other.X2,
                        other.Y2) <= connectionTolerance)
                    {
                        firstEndpointConnected = true;
                    }

                    if (CadShapePointToLineSegmentDistance(
                        candidate.X2,
                        candidate.Y2,
                        other.X1,
                        other.Y1,
                        other.X2,
                        other.Y2) <= connectionTolerance)
                    {
                        secondEndpointConnected = true;
                    }
                }
                else if (other.Type == "ARC" || other.Type == "CIRCLE")
                {
                    double radius = Math.Abs(other.Radius);

                    if (Math.Abs(CadShapePointDistance(candidate.X1, candidate.Y1, other.CX, other.CY) - radius) <= connectionTolerance)
                    {
                        firstEndpointConnected = true;
                    }

                    if (Math.Abs(CadShapePointDistance(candidate.X2, candidate.Y2, other.CX, other.CY) - radius) <= connectionTolerance)
                    {
                        secondEndpointConnected = true;
                    }
                }

                if (firstEndpointConnected || secondEndpointConnected)
                {
                    /*
                     * U형 하단선은 두 수직선, ㄱ형 가로선은 한 수직선, 38번 계열은 원/수직 연결부와
                     * 실제로 이어집니다. 셀 내부에서 실제 형상에 한 끝이라도 연결된 선은 길이와 관계없이
                     * 테이블선으로 삭제하지 않습니다.
                     */
                    return true;
                }
            }

            return false;
        }

        private void RemoveCadShapeGridLineChains(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count == 0 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.020, 0.02);
            double coordinateTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            double edgeToleranceX = Math.Max(width * 0.060, 0.05);
            double edgeToleranceY = Math.Max(height * 0.060, 0.05);
            bool[] remove = new bool[elements.Count];
            int i;

            /*
             * 일부 CAD의 표 선은 하나의 LINE이 아니라 Polyline/Block을 잘게 나눈 여러 선분으로
             * 구성됩니다. 기존 개별 선 길이 필터는 각 조각이 짧아 표 세로선을 제거하지 못했고,
             * OVIA 형상 셀에 여러 개의 세로선이 남는 원인이 되었습니다.
             *
             * 같은 X(또는 Y)에 놓인 선분을 하나의 chain으로 묶어 실제 덮는 길이를 계산하고,
             * 행의 위·아래(또는 셀의 좌·우)를 거의 모두 관통하는 chain만 테이블선으로 제거합니다.
             */
            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement seed = elements[i];

                if (seed == null || seed.Type != "LINE" || remove[i])
                {
                    continue;
                }

                double seedDx = Math.Abs(seed.X2 - seed.X1);
                double seedDy = Math.Abs(seed.Y2 - seed.Y1);
                bool vertical = seedDx <= axisTolerance && seedDy > axisTolerance;
                bool horizontal = seedDy <= axisTolerance && seedDx > axisTolerance;

                if (!vertical && !horizontal)
                {
                    continue;
                }

                double coordinate = vertical
                    ? (seed.X1 + seed.X2) / 2.0
                    : (seed.Y1 + seed.Y2) / 2.0;
                List<OviaGridAxisSegment> intervals = new List<OviaGridAxisSegment>();
                List<int> groupIndexes = new List<int>();
                int j;

                for (j = i; j < elements.Count; j++)
                {
                    OviaCadShapeElement candidate = elements[j];

                    if (candidate == null || candidate.Type != "LINE")
                    {
                        continue;
                    }

                    double dx = Math.Abs(candidate.X2 - candidate.X1);
                    double dy = Math.Abs(candidate.Y2 - candidate.Y1);
                    bool sameAxis = vertical
                        ? dx <= axisTolerance && dy > axisTolerance
                        : dy <= axisTolerance && dx > axisTolerance;

                    if (!sameAxis)
                    {
                        continue;
                    }

                    double candidateCoordinate = vertical
                        ? (candidate.X1 + candidate.X2) / 2.0
                        : (candidate.Y1 + candidate.Y2) / 2.0;

                    if (Math.Abs(candidateCoordinate - coordinate) > coordinateTolerance)
                    {
                        continue;
                    }

                    OviaGridAxisSegment interval = new OviaGridAxisSegment();

                    if (vertical)
                    {
                        interval.Start = Math.Max(0, Math.Min(candidate.Y1, candidate.Y2));
                        interval.End = Math.Min(height, Math.Max(candidate.Y1, candidate.Y2));
                    }
                    else
                    {
                        interval.Start = Math.Max(0, Math.Min(candidate.X1, candidate.X2));
                        interval.End = Math.Min(width, Math.Max(candidate.X1, candidate.X2));
                    }

                    if (interval.End > interval.Start)
                    {
                        intervals.Add(interval);
                        groupIndexes.Add(j);
                    }
                }

                if (intervals.Count == 0)
                {
                    continue;
                }

                intervals.Sort(delegate(OviaGridAxisSegment left, OviaGridAxisSegment right)
                {
                    return left.Start.CompareTo(right.Start);
                });

                double covered = 0;
                double mergedStart = intervals[0].Start;
                double mergedEnd = intervals[0].End;
                double firstStart = mergedStart;
                double lastEnd = mergedEnd;
                int intervalIndex;

                for (intervalIndex = 1; intervalIndex < intervals.Count; intervalIndex++)
                {
                    OviaGridAxisSegment interval = intervals[intervalIndex];

                    if (interval.Start <= mergedEnd + coordinateTolerance)
                    {
                        if (interval.End > mergedEnd)
                        {
                            mergedEnd = interval.End;
                        }
                    }
                    else
                    {
                        covered += Math.Max(0, mergedEnd - mergedStart);
                        mergedStart = interval.Start;
                        mergedEnd = interval.End;
                    }

                    if (interval.Start < firstStart)
                    {
                        firstStart = interval.Start;
                    }

                    if (interval.End > lastEnd)
                    {
                        lastEnd = interval.End;
                    }
                }

                covered += Math.Max(0, mergedEnd - mergedStart);
                double totalSpan = vertical ? height : width;
                double edgeTolerance = vertical ? edgeToleranceY : edgeToleranceX;
                bool touchesBothEdges = firstStart <= edgeTolerance && lastEnd >= totalSpan - edgeTolerance;
                bool coversGridSpan = covered >= totalSpan * 0.88;

                /*
                 * OVIA 2026-07-17 복합 형상 수평선 보존:
                 * U형·ㄱ형·긴 일자형의 실제 수평 철근선은 형상 셀 폭의 대부분을 차지할 수 있습니다.
                 * 기존 조건은 좌우를 거의 모두 덮는다는 이유만으로 셀 중앙의 실제 철근선까지
                 * 테이블 가로선 chain으로 삭제하여 수직선만 남기거나 CAD_EMPTY를 만들었습니다.
                 *
                 * 수평 테이블선은 반드시 행의 위/아래 경계 가까이에 있어야 제거합니다.
                 * 세로 테이블선은 실제 철근 수직구간과 달리 행 높이를 거의 모두 관통하므로
                 * 기존 전체 높이 조건을 유지합니다.
                 */
                bool horizontalGridBorder = vertical
                    || coordinate <= edgeToleranceY
                    || coordinate >= height - edgeToleranceY;

                if (!touchesBothEdges || !coversGridSpan || !horizontalGridBorder)
                {
                    continue;
                }

                bool containsConnectedShapeSegment = false;

                for (j = 0; j < groupIndexes.Count; j++)
                {
                    OviaCadShapeElement groupedLine = elements[groupIndexes[j]];

                    if (ShouldPreserveConnectedCadShapeLine(groupedLine, elements, width, height))
                    {
                        containsConnectedShapeSegment = true;
                        break;
                    }
                }

                if (containsConnectedShapeSegment)
                {
                    continue;
                }

                for (j = 0; j < groupIndexes.Count; j++)
                {
                    remove[groupIndexes[j]] = true;
                }
            }

            for (i = elements.Count - 1; i >= 0; i--)
            {
                if (remove[i])
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private void RemoveDetachedCadShapeHorizontalFragments(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count < 3 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.020, 0.02);
            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.038, 0.04);
            bool[] remove = new bool[elements.Count];
            int i;

            /*
             * 일부 도면의 치수선 또는 표 가로선 조각은 실제 철근형상과 떨어진 채 형상 셀의
             * 위쪽이나 아래쪽에 남습니다. 36~37번에서 보인 사각 형상 위의 짧은 수평선이
             * 대표 사례입니다.
             *
             * 철근 한 가닥의 실제 형상은 일반적으로 하나의 연결된 지오메트리를 이루므로,
             * 다음 조건을 모두 만족하는 수평 조각만 제거합니다.
             *  - 다른 의미 있는 형상(수직선/곡선/원 등)이 존재
             *  - 후보 수평선이 그 형상과 연결되지 않음
             *  - 후보가 주 형상의 위 또는 아래로 명확히 분리됨
             *  - 셀 상단/하단 바깥 띠에 위치
             *
             * 단일 일자형 철근은 다른 복합 지오메트리가 없으므로 이 필터의 대상이 아닙니다.
             */
            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement candidate = elements[i];

                if (candidate == null || candidate.Type != "LINE")
                {
                    continue;
                }

                double candidateDx = Math.Abs(candidate.X2 - candidate.X1);
                double candidateDy = Math.Abs(candidate.Y2 - candidate.Y1);
                bool horizontal = candidateDy <= axisTolerance && candidateDx >= width * 0.18;

                if (!horizontal)
                {
                    continue;
                }

                double candidateMinX;
                double candidateMinY;
                double candidateMaxX;
                double candidateMaxY;

                if (!TryGetCadShapeElementBounds(candidate, out candidateMinX, out candidateMinY, out candidateMaxX, out candidateMaxY))
                {
                    continue;
                }

                bool connectedToOtherGeometry = false;
                bool hasMeaningfulNonHorizontalGeometry = false;
                int otherGeometryCount = 0;
                double otherMinY = Double.MaxValue;
                double otherMaxY = Double.MinValue;
                int j;

                for (j = 0; j < elements.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    OviaCadShapeElement other = elements[j];

                    if (other == null || (other.Type != "LINE" && other.Type != "ARC" && other.Type != "CIRCLE"))
                    {
                        continue;
                    }

                    double otherElementMinX;
                    double otherElementMinY;
                    double otherElementMaxX;
                    double otherElementMaxY;

                    if (!TryGetCadShapeElementBounds(other, out otherElementMinX, out otherElementMinY, out otherElementMaxX, out otherElementMaxY))
                    {
                        continue;
                    }

                    otherGeometryCount++;

                    if (otherElementMinY < otherMinY)
                    {
                        otherMinY = otherElementMinY;
                    }

                    if (otherElementMaxY > otherMaxY)
                    {
                        otherMaxY = otherElementMaxY;
                    }

                    if (other.Type == "ARC" || other.Type == "CIRCLE")
                    {
                        hasMeaningfulNonHorizontalGeometry = true;
                    }
                    else
                    {
                        double otherDx = Math.Abs(other.X2 - other.X1);
                        double otherDy = Math.Abs(other.Y2 - other.Y1);

                        if (otherDy > axisTolerance || (otherDx > axisTolerance && otherDy > axisTolerance))
                        {
                            hasMeaningfulNonHorizontalGeometry = true;
                        }
                    }

                    if (AreCadShapeBoundsConnected(
                        candidateMinX,
                        candidateMinY,
                        candidateMaxX,
                        candidateMaxY,
                        otherElementMinX,
                        otherElementMinY,
                        otherElementMaxX,
                        otherElementMaxY,
                        connectionTolerance))
                    {
                        connectedToOtherGeometry = true;
                        break;
                    }
                }

                if (connectedToOtherGeometry || otherGeometryCount < 2 || !hasMeaningfulNonHorizontalGeometry)
                {
                    continue;
                }

                double centerY = (candidateMinY + candidateMaxY) / 2.0;
                bool inOuterBand = centerY <= height * 0.30 || centerY >= height * 0.70;
                bool separatedAbove = otherMinY != Double.MaxValue && candidateMaxY < otherMinY - connectionTolerance;
                bool separatedBelow = otherMaxY != Double.MinValue && candidateMinY > otherMaxY + connectionTolerance;

                if (inOuterBand && (separatedAbove || separatedBelow))
                {
                    remove[i] = true;
                }
            }

            for (i = elements.Count - 1; i >= 0; i--)
            {
                if (remove[i])
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private void RemoveDetachedCadShapeVerticalBorderFragments(List<OviaCadShapeElement> elements, double width, double height)
        {
            if (elements == null || elements.Count < 2 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.020, 0.02);
            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.045, 0.05);
            double minimumBorderHeight = Math.Max(height * 0.18, axisTolerance * 2.0);
            List<int> mainGeometryIndexes = new List<int>();
            double mainMinX = Double.MaxValue;
            double mainMaxX = Double.MinValue;
            int i;

            /*
             * 수평·대각선·곡선·원은 철근 본체 후보입니다. 수직선만 있는 실제 형상은
             * 본체 후보가 없으므로 이 필터를 적용하지 않습니다.
             */
            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || (item.Type != "LINE" && item.Type != "ARC" && item.Type != "CIRCLE"))
                {
                    continue;
                }

                bool verticalLine = item.Type == "LINE"
                    && Math.Abs(item.X2 - item.X1) <= axisTolerance
                    && Math.Abs(item.Y2 - item.Y1) > axisTolerance;

                if (verticalLine)
                {
                    continue;
                }

                double itemMinX;
                double itemMinY;
                double itemMaxX;
                double itemMaxY;

                if (!TryGetCadShapeElementBounds(item, out itemMinX, out itemMinY, out itemMaxX, out itemMaxY))
                {
                    continue;
                }

                mainGeometryIndexes.Add(i);
                mainMinX = Math.Min(mainMinX, itemMinX);
                mainMaxX = Math.Max(mainMaxX, itemMaxX);
            }

            if (mainGeometryIndexes.Count == 0 || mainMinX == Double.MaxValue || mainMaxX == Double.MinValue)
            {
                return;
            }

            bool[] remove = new bool[elements.Count];

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement candidate = elements[i];

                if (candidate == null || candidate.Type != "LINE")
                {
                    continue;
                }

                double dx = Math.Abs(candidate.X2 - candidate.X1);
                double dy = Math.Abs(candidate.Y2 - candidate.Y1);

                if (dx > axisTolerance || dy < minimumBorderHeight)
                {
                    continue;
                }

                double centerX = (candidate.X1 + candidate.X2) / 2.0;
                bool outsideMainGeometry = centerX < mainMinX - connectionTolerance
                    || centerX > mainMaxX + connectionTolerance;

                if (!outsideMainGeometry)
                {
                    continue;
                }

                bool connectedToMainGeometry = false;
                int mainIndex;

                for (mainIndex = 0; mainIndex < mainGeometryIndexes.Count; mainIndex++)
                {
                    OviaCadShapeElement mainGeometry = elements[mainGeometryIndexes[mainIndex]];

                    if (AreCadShapeGeometryElementsConnected(candidate, mainGeometry, connectionTolerance))
                    {
                        connectedToMainGeometry = true;
                        break;
                    }
                }

                if (!connectedToMainGeometry)
                {
                    remove[i] = true;
                }
            }

            for (i = elements.Count - 1; i >= 0; i--)
            {
                if (remove[i])
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private void RemoveCadShapeHeaderLabelTexts(List<OviaCadShapeElement> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }

            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item != null && item.Type == "TEXT" && IsCadShapeHeaderLabelText(item.Text))
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private bool IsCadShapeHeaderLabelText(string value)
        {
            string normalized = NormalizeGridHeaderText(CleanCadShapeText(value));

            if (normalized == "")
            {
                return false;
            }

            switch (normalized)
            {
                case "NO":
                case "번호":
                case "부호":
                case "MARK":
                case "BARNO":
                case "부위":
                case "위치":
                case "구간":
                case "ZONE":
                case "AREA":
                case "LOCATION":
                case "철근형상":
                case "형상":
                case "형태":
                case "SHAPE":
                case "형번":
                case "형상번호":
                case "형상코드":
                case "SHAPENO":
                case "SHAPECODE":
                case "철근규격":
                case "규격":
                case "강종":
                case "SIZE":
                case "DIA":
                case "길이":
                case "길이MM":
                case "LENGTH":
                case "LENGTHMM":
                case "수량":
                case "수량EA":
                case "QTY":
                case "QTYEA":
                case "총길이":
                case "총길이M":
                case "총연장":
                case "연장":
                case "TOTALLENGTH":
                case "TOTALLENGTHM":
                case "총중량":
                case "총중량TON":
                case "총중량KG":
                case "중량":
                case "중량TON":
                case "중량KG":
                case "WEIGHT":
                case "WEIGHTTON":
                case "WEIGHTKG":
                case "WT":
                case "비고":
                case "NOTE":
                case "REMARK":
                    return true;
            }

            return false;
        }

        private bool TryGetCadShapeElementBounds(
            OviaCadShapeElement item,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY)
        {
            minX = 0;
            minY = 0;
            maxX = 0;
            maxY = 0;

            if (item == null)
            {
                return false;
            }

            if (item.Type == "LINE")
            {
                minX = Math.Min(item.X1, item.X2);
                minY = Math.Min(item.Y1, item.Y2);
                maxX = Math.Max(item.X1, item.X2);
                maxY = Math.Max(item.Y1, item.Y2);
                return true;
            }

            if (item.Type == "ARC" || item.Type == "CIRCLE")
            {
                minX = item.CX - item.Radius;
                minY = item.CY - item.Radius;
                maxX = item.CX + item.Radius;
                maxY = item.CY + item.Radius;
                return item.Radius >= 0;
            }

            return false;
        }

        private bool AreCadShapeBoundsConnected(
            double firstMinX,
            double firstMinY,
            double firstMaxX,
            double firstMaxY,
            double secondMinX,
            double secondMinY,
            double secondMaxX,
            double secondMaxY,
            double tolerance)
        {
            return firstMaxX + tolerance >= secondMinX
                && firstMinX - tolerance <= secondMaxX
                && firstMaxY + tolerance >= secondMinY
                && firstMinY - tolerance <= secondMaxY;
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

                double minLineX = Math.Min(item.X1, item.X2);
                double maxLineX = Math.Max(item.X1, item.X2);
                double minLineY = Math.Min(item.Y1, item.Y2);
                double maxLineY = Math.Max(item.Y1, item.Y2);

                if (ShouldPreserveConnectedCadShapeLine(item, elements, width, height))
                {
                    continue;
                }

                /*
                 * 형상 셀 범위가 예외적으로 인접 컬럼까지 넓어져도, 행의 위·아래 경계를 모두
                 * 관통하는 수직선은 테이블 컬럼 구분선입니다. 기존에는 캡처 영역 좌우 끝에 있는
                 * 선만 삭제하여, 넓어진 영역 내부의 번호/규격/길이/수량 세로선이 그대로 남았습니다.
                 * 실제 철근의 수직선은 CAD 형상 셀 안에서 상하 여백을 두고 그려지므로 행 전체를
                 * 관통하지 않습니다.
                 */
                bool spansWholeRow = vertical
                    && dy >= height * 0.92
                    && minLineY <= edgeToleranceY
                    && maxLineY >= height - edgeToleranceY;

                if (spansWholeRow)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                /*
                 * 같은 원리로 캡처 영역의 좌우를 모두 관통하는 수평선은 행 경계선입니다.
                 */
                bool spansWholeCaptureWidth = horizontal
                    && dx >= width * 0.92
                    && minLineX <= edgeToleranceX
                    && maxLineX >= width - edgeToleranceX;

                if (spansWholeCaptureWidth)
                {
                    elements.RemoveAt(i);
                    continue;
                }

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
            if (!IsFiniteCadShapePoint(p1) || !IsFiniteCadShapePoint(p2))
            {
                return false;
            }

            double dx = Math.Abs(p1.X - p2.X);
            double dy = Math.Abs(p1.Y - p2.Y);

            if (dx < 0.000001 && dy < 0.000001)
            {
                return false;
            }

            /*
             * 선택 크기 불변 원칙:
             * 원본 후보 수집 단계에서는 선의 길이, 방향, 셀 외곽과의 거리만으로 삭제하지 않습니다.
             * 38번 상·하 철근선은 대량 선택에서 셀 높이가 미세하게 달라지자 84~90% 경계선 조건에
             * 걸려 raw 후보에서조차 사라졌고, 이후 복구 함수가 복구할 원본이 없었습니다.
             *
             * 여기서는 선분의 bounding box가 형상 셀의 안전 여유와 교차하는지만 확인합니다.
             * 실제 표 경계선 제거는 모든 후보 수집 후 RemoveVerifiedCadShapeCellBoundaryLines에서
             * 네 물리 경계와 96.5% 이상 일치하는 경우에만 수행합니다.
             */
            double inclusionMarginX = Math.Max(width * 0.08, 0.08);
            double inclusionMarginY = Math.Max(height * 0.18, 0.08);
            double segmentMinX = Math.Min(p1.X, p2.X);
            double segmentMaxX = Math.Max(p1.X, p2.X);
            double segmentMinY = Math.Min(p1.Y, p2.Y);
            double segmentMaxY = Math.Max(p1.Y, p2.Y);

            return segmentMaxX >= minX - inclusionMarginX
                && segmentMinX <= maxX + inclusionMarginX
                && segmentMaxY >= minY - inclusionMarginY
                && segmentMinY <= maxY + inclusionMarginY;
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

        private void RemoveInvalidCadShapeElements(List<OviaCadShapeElement> elements)
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
                    }

                    continue;
                }

                if (item.Type == "LINE"
                    && Math.Abs(item.X1 - item.X2) < 0.000001
                    && Math.Abs(item.Y1 - item.Y2) < 0.000001)
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if ((item.Type == "ARC" || item.Type == "CIRCLE")
                    && Math.Abs(item.Radius) < 0.000001)
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private void RemoveVerifiedCadShapeCellBoundaryLines(
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (elements == null || elements.Count == 0 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            double shortSide = Math.Max(Math.Min(width, height), 0.0001);
            double axisTolerance = Math.Max(shortSide * 0.0035, 0.01);
            double coordinateTolerance = Math.Max(shortSide * 0.0060, 0.02);
            double boundaryToleranceX = Math.Max(width * 0.012, coordinateTolerance);
            double boundaryToleranceY = Math.Max(height * 0.012, coordinateTolerance);
            double intervalEdgeToleranceX = Math.Max(width * 0.022, coordinateTolerance);
            double intervalEdgeToleranceY = Math.Max(height * 0.022, coordinateTolerance);
            bool[] remove = new bool[elements.Count];
            bool[] processed = new bool[elements.Count];
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                if (processed[i])
                {
                    continue;
                }

                OviaCadShapeElement seed = elements[i];

                if (seed == null || seed.Type != "LINE")
                {
                    continue;
                }

                double seedDx = Math.Abs(seed.X2 - seed.X1);
                double seedDy = Math.Abs(seed.Y2 - seed.Y1);
                bool vertical = seedDx <= axisTolerance && seedDy > axisTolerance;
                bool horizontal = seedDy <= axisTolerance && seedDx > axisTolerance;

                if (!vertical && !horizontal)
                {
                    continue;
                }

                double seedCoordinate = vertical
                    ? (seed.X1 + seed.X2) / 2.0
                    : (seed.Y1 + seed.Y2) / 2.0;
                bool nearPhysicalBoundary = vertical
                    ? seedCoordinate <= boundaryToleranceX || seedCoordinate >= width - boundaryToleranceX
                    : seedCoordinate <= boundaryToleranceY || seedCoordinate >= height - boundaryToleranceY;

                if (!nearPhysicalBoundary)
                {
                    continue;
                }

                List<int> clusterIndexes = new List<int>();
                List<OviaGridAxisSegment> intervals = new List<OviaGridAxisSegment>();
                int j;

                for (j = i; j < elements.Count; j++)
                {
                    OviaCadShapeElement candidate = elements[j];

                    if (candidate == null || candidate.Type != "LINE")
                    {
                        continue;
                    }

                    double dx = Math.Abs(candidate.X2 - candidate.X1);
                    double dy = Math.Abs(candidate.Y2 - candidate.Y1);
                    bool sameAxis = vertical
                        ? dx <= axisTolerance && dy > axisTolerance
                        : dy <= axisTolerance && dx > axisTolerance;

                    if (!sameAxis)
                    {
                        continue;
                    }

                    double coordinate = vertical
                        ? (candidate.X1 + candidate.X2) / 2.0
                        : (candidate.Y1 + candidate.Y2) / 2.0;

                    if (Math.Abs(coordinate - seedCoordinate) > coordinateTolerance)
                    {
                        continue;
                    }

                    bool candidateNearSameBoundary = vertical
                        ? ((seedCoordinate <= boundaryToleranceX && coordinate <= boundaryToleranceX)
                            || (seedCoordinate >= width - boundaryToleranceX && coordinate >= width - boundaryToleranceX))
                        : ((seedCoordinate <= boundaryToleranceY && coordinate <= boundaryToleranceY)
                            || (seedCoordinate >= height - boundaryToleranceY && coordinate >= height - boundaryToleranceY));

                    if (!candidateNearSameBoundary)
                    {
                        continue;
                    }

                    OviaGridAxisSegment interval = new OviaGridAxisSegment();
                    double totalSpan = vertical ? height : width;

                    if (vertical)
                    {
                        interval.Start = Math.Max(0, Math.Min(candidate.Y1, candidate.Y2));
                        interval.End = Math.Min(totalSpan, Math.Max(candidate.Y1, candidate.Y2));
                    }
                    else
                    {
                        interval.Start = Math.Max(0, Math.Min(candidate.X1, candidate.X2));
                        interval.End = Math.Min(totalSpan, Math.Max(candidate.X1, candidate.X2));
                    }

                    if (interval.End <= interval.Start)
                    {
                        continue;
                    }

                    clusterIndexes.Add(j);
                    intervals.Add(interval);
                    processed[j] = true;
                }

                if (intervals.Count == 0)
                {
                    continue;
                }

                intervals.Sort(delegate(OviaGridAxisSegment left, OviaGridAxisSegment right)
                {
                    return left.Start.CompareTo(right.Start);
                });

                double mergeTolerance = vertical ? intervalEdgeToleranceY : intervalEdgeToleranceX;
                double mergedStart = intervals[0].Start;
                double mergedEnd = intervals[0].End;
                double firstStart = mergedStart;
                double lastEnd = mergedEnd;
                double covered = 0;
                int intervalIndex;

                for (intervalIndex = 1; intervalIndex < intervals.Count; intervalIndex++)
                {
                    OviaGridAxisSegment interval = intervals[intervalIndex];

                    if (interval.Start <= mergedEnd + mergeTolerance)
                    {
                        if (interval.End > mergedEnd)
                        {
                            mergedEnd = interval.End;
                        }
                    }
                    else
                    {
                        covered += Math.Max(0, mergedEnd - mergedStart);
                        mergedStart = interval.Start;
                        mergedEnd = interval.End;
                    }

                    firstStart = Math.Min(firstStart, interval.Start);
                    lastEnd = Math.Max(lastEnd, interval.End);
                }

                covered += Math.Max(0, mergedEnd - mergedStart);
                double span = vertical ? height : width;
                double edgeTolerance = vertical ? intervalEdgeToleranceY : intervalEdgeToleranceX;
                bool touchesBothCellEnds = firstStart <= edgeTolerance && lastEnd >= span - edgeTolerance;
                bool coversVerifiedBoundary = covered >= span * 0.965;

                if (!touchesBothCellEnds || !coversVerifiedBoundary)
                {
                    continue;
                }

                for (j = 0; j < clusterIndexes.Count; j++)
                {
                    remove[clusterIndexes[j]] = true;
                }
            }

            for (i = elements.Count - 1; i >= 0; i--)
            {
                if (remove[i])
                {
                    elements.RemoveAt(i);
                }
            }
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

                    if (ShouldPreserveConnectedCadShapeLine(item, elements, width, height))
                    {
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

        private void RecoverConnectedCadShapeLineSegments(
            List<OviaCadShapeElement> filteredElements,
            List<OviaCadShapeElement> unfilteredElements,
            double width,
            double height)
        {
            if (filteredElements == null || unfilteredElements == null || unfilteredElements.Count == 0)
            {
                return;
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.090, 0.10);
            double edgeToleranceX = Math.Max(width * 0.060, 0.05);
            double edgeToleranceY = Math.Max(height * 0.060, 0.05);
            HashSet<string> existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < filteredElements.Count; i++)
            {
                OviaCadShapeElement existing = filteredElements[i];

                if (existing != null)
                {
                    existingKeys.Add(BuildCadShapeElementKey(existing));
                }
            }

            bool added;
            int pass = 0;

            do
            {
                added = false;
                pass++;

                for (i = 0; i < unfilteredElements.Count; i++)
                {
                    OviaCadShapeElement candidate = unfilteredElements[i];

                    if (candidate == null || candidate.Type != "LINE")
                    {
                        continue;
                    }

                    string key = BuildCadShapeElementKey(candidate);

                    if (existingKeys.Contains(key))
                    {
                        continue;
                    }

                    double dx = Math.Abs(candidate.X2 - candidate.X1);
                    double dy = Math.Abs(candidate.Y2 - candidate.Y1);
                    bool horizontal = dy <= axisTolerance && dx > axisTolerance;
                    bool vertical = dx <= axisTolerance && dy > axisTolerance;
                    double minLineX = Math.Min(candidate.X1, candidate.X2);
                    double maxLineX = Math.Max(candidate.X1, candidate.X2);
                    double minLineY = Math.Min(candidate.Y1, candidate.Y2);
                    double maxLineY = Math.Max(candidate.Y1, candidate.Y2);
                    double centerY = (candidate.Y1 + candidate.Y2) / 2.0;

                    bool trueHorizontalTableBorder = horizontal
                        && dx >= width * 0.88
                        && minLineX <= edgeToleranceX
                        && maxLineX >= width - edgeToleranceX
                        && (centerY <= edgeToleranceY || centerY >= height - edgeToleranceY);

                    bool trueVerticalTableBorder = vertical
                        && dy >= height * 0.88
                        && minLineY <= edgeToleranceY
                        && maxLineY >= height - edgeToleranceY;

                    if (trueHorizontalTableBorder || trueVerticalTableBorder)
                    {
                        continue;
                    }

                    if (!IsCadShapeLineConnectedToGeometry(candidate, filteredElements, connectionTolerance))
                    {
                        continue;
                    }

                    filteredElements.Add(candidate);
                    existingKeys.Add(key);
                    added = true;
                }
            }
            while (added && pass < 8);
        }

        private bool IsCadShapeLineConnectedToGeometry(
            OviaCadShapeElement candidate,
            List<OviaCadShapeElement> elements,
            double tolerance)
        {
            if (candidate == null || elements == null)
            {
                return false;
            }

            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement other = elements[i];

                if (other == null || other.Type == "TEXT")
                {
                    continue;
                }

                if (other.Type == "LINE")
                {
                    if (CadShapePointToLineSegmentDistance(
                            candidate.X1,
                            candidate.Y1,
                            other.X1,
                            other.Y1,
                            other.X2,
                            other.Y2) <= tolerance
                        || CadShapePointToLineSegmentDistance(
                            candidate.X2,
                            candidate.Y2,
                            other.X1,
                            other.Y1,
                            other.X2,
                            other.Y2) <= tolerance
                        || CadShapePointToLineSegmentDistance(
                            other.X1,
                            other.Y1,
                            candidate.X1,
                            candidate.Y1,
                            candidate.X2,
                            candidate.Y2) <= tolerance
                        || CadShapePointToLineSegmentDistance(
                            other.X2,
                            other.Y2,
                            candidate.X1,
                            candidate.Y1,
                            candidate.X2,
                            candidate.Y2) <= tolerance)
                    {
                        return true;
                    }

                    continue;
                }

                if (other.Type == "ARC" || other.Type == "CIRCLE")
                {
                    double firstRadiusGap = Math.Abs(CadShapePointDistance(candidate.X1, candidate.Y1, other.CX, other.CY) - Math.Abs(other.Radius));
                    double secondRadiusGap = Math.Abs(CadShapePointDistance(candidate.X2, candidate.Y2, other.CX, other.CY) - Math.Abs(other.Radius));

                    if (firstRadiusGap <= tolerance || secondRadiusGap <= tolerance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private double CadShapePointDistance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }


        private double CadShapePointToLineSegmentDistance(
            double pointX,
            double pointY,
            double lineX1,
            double lineY1,
            double lineX2,
            double lineY2)
        {
            double segmentX = lineX2 - lineX1;
            double segmentY = lineY2 - lineY1;
            double segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;

            if (segmentLengthSquared <= 0.0000001)
            {
                return CadShapePointDistance(pointX, pointY, lineX1, lineY1);
            }

            double projection = ((pointX - lineX1) * segmentX + (pointY - lineY1) * segmentY)
                / segmentLengthSquared;

            if (projection < 0.0)
            {
                projection = 0.0;
            }
            else if (projection > 1.0)
            {
                projection = 1.0;
            }

            double closestX = lineX1 + projection * segmentX;
            double closestY = lineY1 + projection * segmentY;
            return CadShapePointDistance(pointX, pointY, closestX, closestY);
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

        private void RecoverOverFilteredCadShapeTopology(
            OviaBarTableRow row,
            List<OviaCadShapeElement> filteredElements,
            List<OviaCadShapeElement> rawElements,
            double width,
            double height)
        {
            if (row == null
                || filteredElements == null
                || rawElements == null
                || rawElements.Count == 0
                || width <= 0.0001
                || height <= 0.0001)
            {
                return;
            }

            int expectedDimensionCount = 0;

            if (row.ShapeRawText != null && row.ShapeRawText.Trim() != "")
            {
                expectedDimensionCount = CountExpectedCadShapeDimensionValues(row.ShapeRawText);
            }

            int retainedDimensionCount = 0;
            int filteredGeometryCount = 0;
            int i;

            for (i = 0; i < filteredElements.Count; i++)
            {
                OviaCadShapeElement item = filteredElements[i];

                if (item == null)
                {
                    continue;
                }

                if (item.Type == "TEXT")
                {
                    if (ShapeRawTextContainsNumericValue(row.ShapeRawText, item.Text))
                    {
                        retainedDimensionCount++;
                    }
                }
                else if (item.Type == "LINE" || item.Type == "ARC" || item.Type == "CIRCLE")
                {
                    filteredGeometryCount++;
                }
            }

            double axisTolerance = Math.Max(Math.Min(width, height) * 0.025, 0.03);
            double edgeToleranceX = Math.Max(width * 0.060, 0.05);
            double edgeToleranceY = Math.Max(height * 0.060, 0.05);
            List<int> safeGeometryIndexes = new List<int>();

            for (i = 0; i < rawElements.Count; i++)
            {
                OviaCadShapeElement item = rawElements[i];

                if (item == null
                    || (item.Type != "LINE" && item.Type != "ARC" && item.Type != "CIRCLE"))
                {
                    continue;
                }

                if (item.Type == "LINE"
                    && IsLikelyCadShapeCellBorderLine(
                        item,
                        width,
                        height,
                        axisTolerance,
                        edgeToleranceX,
                        edgeToleranceY))
                {
                    continue;
                }

                safeGeometryIndexes.Add(i);
            }

            if (safeGeometryIndexes.Count == 0)
            {
                return;
            }

            int minimumExpectedRetained = expectedDimensionCount <= 0
                ? 0
                : Math.Max(2, (int)Math.Ceiling(expectedDimensionCount * 0.58));
            bool dimensionLossEvidence = expectedDimensionCount >= 3
                && retainedDimensionCount < minimumExpectedRetained;
            bool geometryLossEvidence = safeGeometryIndexes.Count >= 3
                && filteredGeometryCount <= Math.Max(1, (int)Math.Floor(safeGeometryIndexes.Count * 0.55));

            /*
             * 형상원본의 치수와 실제 JSON 치수가 함께 줄었거나, 비경계 지오메트리의 절반 이상이
             * 사라진 경우만 복구합니다. 정상 단독 추출과 단순 일자형은 이 조건에 들어오지 않습니다.
             */
            if (!dimensionLossEvidence && !geometryLossEvidence)
            {
                return;
            }

            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.12, 0.18);
            bool[] visited = new bool[safeGeometryIndexes.Count];
            List<List<int>> components = new List<List<int>>();

            for (i = 0; i < safeGeometryIndexes.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<int> component = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int localIndex = queue.Dequeue();
                    int rawIndex = safeGeometryIndexes[localIndex];
                    component.Add(rawIndex);
                    OviaCadShapeElement current = rawElements[rawIndex];
                    int j;

                    for (j = 0; j < safeGeometryIndexes.Count; j++)
                    {
                        if (visited[j])
                        {
                            continue;
                        }

                        OviaCadShapeElement candidate = rawElements[safeGeometryIndexes[j]];

                        if (AreCadShapeGeometryElementsConnected(current, candidate, connectionTolerance))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                components.Add(component);
            }

            if (components.Count == 0)
            {
                return;
            }

            List<int> bestComponent = null;
            double bestScore = Double.MinValue;
            double bestMinX = 0;
            double bestMinY = 0;
            double bestMaxX = 0;
            double bestMaxY = 0;

            for (i = 0; i < components.Count; i++)
            {
                List<int> component = components[i];
                double minX = Double.MaxValue;
                double minY = Double.MaxValue;
                double maxX = Double.MinValue;
                double maxY = Double.MinValue;
                double score = component.Count * 2.0;
                int j;

                for (j = 0; j < component.Count; j++)
                {
                    OviaCadShapeElement item = rawElements[component[j]];
                    double itemMinX;
                    double itemMinY;
                    double itemMaxX;
                    double itemMaxY;

                    if (TryGetCadShapeElementBounds(item, out itemMinX, out itemMinY, out itemMaxX, out itemMaxY))
                    {
                        minX = Math.Min(minX, itemMinX);
                        minY = Math.Min(minY, itemMinY);
                        maxX = Math.Max(maxX, itemMaxX);
                        maxY = Math.Max(maxY, itemMaxY);
                    }

                    if (item.Type == "LINE")
                    {
                        score += CadShapePointDistance(item.X1, item.Y1, item.X2, item.Y2)
                            / Math.Max(Math.Min(width, height), 0.0001);
                    }
                    else
                    {
                        score += 3.0;
                    }
                }

                if (minX == Double.MaxValue)
                {
                    continue;
                }

                double componentWidth = Math.Max(maxX - minX, 0.0);
                double componentHeight = Math.Max(maxY - minY, 0.0);
                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                if (centerX >= width * 0.08 && centerX <= width * 0.92
                    && centerY >= height * 0.06 && centerY <= height * 0.94)
                {
                    score += 4.0;
                }

                if (component.Count == 1
                    && ((componentWidth >= width * 0.84 && componentHeight <= height * 0.05)
                        || (componentHeight >= height * 0.84 && componentWidth <= width * 0.05)))
                {
                    score -= 12.0;
                }

                for (j = 0; j < rawElements.Count; j++)
                {
                    OviaCadShapeElement text = rawElements[j];

                    if (text == null || text.Type != "TEXT"
                        || !ShapeRawTextContainsNumericValue(row.ShapeRawText, text.Text))
                    {
                        continue;
                    }

                    double tx = text.HasBounds
                        ? (text.BoundsMinX + text.BoundsMaxX) / 2.0
                        : text.X1;
                    double ty = text.HasBounds
                        ? (text.BoundsMinY + text.BoundsMaxY) / 2.0
                        : text.Y1;
                    double marginX = Math.Max(componentWidth * 0.40, width * 0.06);
                    double marginY = Math.Max(componentHeight * 0.70, height * 0.16);

                    if (tx >= minX - marginX && tx <= maxX + marginX
                        && ty >= minY - marginY && ty <= maxY + marginY)
                    {
                        score += 8.0;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestComponent = component;
                    bestMinX = minX;
                    bestMinY = minY;
                    bestMaxX = maxX;
                    bestMaxY = maxY;
                }
            }

            if (bestComponent == null || bestComponent.Count == 0)
            {
                return;
            }

            List<OviaCadShapeElement> rebuilt = new List<OviaCadShapeElement>();
            HashSet<string> rebuiltKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (i = 0; i < bestComponent.Count; i++)
            {
                OviaCadShapeElement geometry = rawElements[bestComponent[i]];
                string key = BuildCadShapeElementKey(geometry);

                if (!rebuiltKeys.Contains(key))
                {
                    rebuilt.Add(geometry);
                    rebuiltKeys.Add(key);
                }
            }

            double bestWidth = Math.Max(bestMaxX - bestMinX, 0.0001);
            double bestHeight = Math.Max(bestMaxY - bestMinY, 0.0001);
            double textMarginX = Math.Max(bestWidth * 0.45, width * 0.08);
            double textMarginY = Math.Max(bestHeight * 0.80, height * 0.18);

            for (i = 0; i < rawElements.Count; i++)
            {
                OviaCadShapeElement text = rawElements[i];

                if (text == null || text.Type != "TEXT" || IsExternalRowValueText(row, text.Text))
                {
                    continue;
                }

                bool isExpectedDimension = ShapeRawTextContainsNumericValue(row.ShapeRawText, text.Text);
                bool isDirection = IsCadShapeDirectionLabel(text.Text);
                bool isNonMetricShapeLabel = !IsExternalRowMetricText(row, text.Text)
                    && !IsHeaderRow(text.Text)
                    && text.Text != null
                    && text.Text.Trim().Length <= 16;

                if (!isExpectedDimension && !isDirection && !isNonMetricShapeLabel)
                {
                    continue;
                }

                double centerX = text.HasBounds
                    ? (text.BoundsMinX + text.BoundsMaxX) / 2.0
                    : text.X1;
                double centerY = text.HasBounds
                    ? (text.BoundsMinY + text.BoundsMaxY) / 2.0
                    : text.Y1;
                bool nearBest = centerX >= bestMinX - textMarginX
                    && centerX <= bestMaxX + textMarginX
                    && centerY >= bestMinY - textMarginY
                    && centerY <= bestMaxY + textMarginY;

                if (!nearBest)
                {
                    continue;
                }

                string key = BuildCadShapeElementKey(text);

                if (!rebuiltKeys.Contains(key))
                {
                    rebuilt.Add(text);
                    rebuiltKeys.Add(key);
                }
            }

            int rebuiltGeometryCount = 0;
            int rebuiltDimensionCount = 0;

            for (i = 0; i < rebuilt.Count; i++)
            {
                OviaCadShapeElement item = rebuilt[i];

                if (item == null)
                {
                    continue;
                }

                if (item.Type == "TEXT")
                {
                    if (ShapeRawTextContainsNumericValue(row.ShapeRawText, item.Text))
                    {
                        rebuiltDimensionCount++;
                    }
                }
                else
                {
                    rebuiltGeometryCount++;
                }
            }

            if (rebuiltGeometryCount < 1)
            {
                return;
            }

            bool improvesGeometry = rebuiltGeometryCount > filteredGeometryCount;
            bool improvesDimensions = rebuiltDimensionCount > retainedDimensionCount;

            if (!improvesGeometry && !improvesDimensions)
            {
                return;
            }

            filteredElements.Clear();
            filteredElements.AddRange(rebuilt);
        }

        private void KeepDominantCadShapeComponentWhenContaminated(OviaBarTableRow row, List<OviaCadShapeElement> elements, double width, double height)
        {
            if (row == null || elements == null || elements.Count == 0 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            int externalMetricTextCount = 0;
            List<int> geometryIndexes = new List<int>();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null)
                {
                    continue;
                }

                if (item.Type == "TEXT")
                {
                    if (IsExternalRowMetricText(row, item.Text))
                    {
                        externalMetricTextCount++;
                    }
                }
                else if (item.Type == "LINE" || item.Type == "ARC" || item.Type == "CIRCLE")
                {
                    geometryIndexes.Add(i);
                }
            }

            /*
             * 정상 형상 셀에는 길이/수량/총길이/중량 값이 여러 개 동시에 들어오지 않습니다.
             * 캡처 폭도 일반적으로 행 높이의 수 배 이내입니다. 두 조건 중 하나도 없으면
             * 복합 형상이나 분리된 보조선을 손대지 않고 기존 로직을 그대로 사용합니다.
             */
            /*
             * 선택 범위가 커질 때 Y 경계가 수 픽셀만 달라져도 width/height 비율은 크게 바뀔 수 있습니다.
             * 폭 비율만으로 오염으로 판단하면 정상 U형과 38번 복합 형상에서 연결 컴포넌트를 삭제합니다.
             * 실제 길이·수량·총길이·중량 값이 둘 이상 형상 후보에 섞인 객관적 증거가 있을 때만
             * 지배 컴포넌트 정리를 수행합니다.
             */
            if (geometryIndexes.Count < 2 || externalMetricTextCount < 2)
            {
                return;
            }

            double connectionTolerance = Math.Max(Math.Min(width, height) * 0.055, 0.6);
            bool[] visited = new bool[geometryIndexes.Count];
            List<List<int>> components = new List<List<int>>();

            for (i = 0; i < geometryIndexes.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                List<int> component = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int localIndex = queue.Dequeue();
                    int elementIndex = geometryIndexes[localIndex];
                    component.Add(elementIndex);
                    OviaCadShapeElement current = elements[elementIndex];
                    int j;

                    for (j = 0; j < geometryIndexes.Count; j++)
                    {
                        if (visited[j])
                        {
                            continue;
                        }

                        OviaCadShapeElement candidate = elements[geometryIndexes[j]];

                        if (AreCadShapeGeometryElementsConnected(current, candidate, connectionTolerance))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                components.Add(component);
            }

            if (components.Count <= 1)
            {
                return;
            }

            List<int> bestComponent = null;
            double bestScore = Double.MinValue;

            for (i = 0; i < components.Count; i++)
            {
                List<int> component = components[i];
                double score = 0.0;
                double minX = Double.MaxValue;
                double minY = Double.MaxValue;
                double maxX = Double.MinValue;
                double maxY = Double.MinValue;
                int j;

                for (j = 0; j < component.Count; j++)
                {
                    OviaCadShapeElement item = elements[component[j]];
                    double itemMinX;
                    double itemMinY;
                    double itemMaxX;
                    double itemMaxY;

                    if (TryGetCadShapeElementBounds(item, out itemMinX, out itemMinY, out itemMaxX, out itemMaxY))
                    {
                        minX = Math.Min(minX, itemMinX);
                        minY = Math.Min(minY, itemMinY);
                        maxX = Math.Max(maxX, itemMaxX);
                        maxY = Math.Max(maxY, itemMaxY);
                    }

                    if (item.Type == "LINE")
                    {
                        score += CadShapePointDistance(item.X1, item.Y1, item.X2, item.Y2) / Math.Max(Math.Min(width, height), 0.0001);
                    }
                    else
                    {
                        score += 2.5;
                    }
                }

                score += component.Count * 2.0;

                if (minX != Double.MaxValue)
                {
                    double componentWidth = Math.Max(maxX - minX, 0.0);
                    double componentHeight = Math.Max(maxY - minY, 0.0);

                    // 행 높이 대부분을 관통하는 단독 수직선은 표 컬럼 경계일 가능성이 큽니다.
                    if (component.Count == 1 && componentHeight >= height * 0.72 && componentWidth <= width * 0.02)
                    {
                        score -= 8.0;
                    }

                    // 행 위/아래 경계를 거의 전폭으로 관통하는 단독 수평선도 표 경계로 감점합니다.
                    if (component.Count == 1 && componentWidth >= width * 0.82 && componentHeight <= height * 0.04)
                    {
                        score -= 8.0;
                    }

                    int textIndex;
                    for (textIndex = 0; textIndex < elements.Count; textIndex++)
                    {
                        OviaCadShapeElement text = elements[textIndex];

                        if (text == null || text.Type != "TEXT" || IsExternalRowValueText(row, text.Text))
                        {
                            continue;
                        }

                        double tx = text.HasBounds ? (text.BoundsMinX + text.BoundsMaxX) / 2.0 : text.X1;
                        double ty = text.HasBounds ? (text.BoundsMinY + text.BoundsMaxY) / 2.0 : text.Y1;
                        double marginX = Math.Max(componentWidth * 0.35, width * 0.025);
                        double marginY = Math.Max(componentHeight * 0.55, height * 0.08);

                        if (tx >= minX - marginX && tx <= maxX + marginX && ty >= minY - marginY && ty <= maxY + marginY)
                        {
                            score += IsExternalRowMetricText(row, text.Text) ? 0.25 : 1.0;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestComponent = component;
                }
            }

            if (bestComponent == null || bestComponent.Count == 0)
            {
                return;
            }

            HashSet<int> keepIndexes = new HashSet<int>(bestComponent);
            double bestMinX = Double.MaxValue;
            double bestMinY = Double.MaxValue;
            double bestMaxX = Double.MinValue;
            double bestMaxY = Double.MinValue;

            for (i = 0; i < bestComponent.Count; i++)
            {
                double itemMinX;
                double itemMinY;
                double itemMaxX;
                double itemMaxY;

                if (TryGetCadShapeElementBounds(elements[bestComponent[i]], out itemMinX, out itemMinY, out itemMaxX, out itemMaxY))
                {
                    bestMinX = Math.Min(bestMinX, itemMinX);
                    bestMinY = Math.Min(bestMinY, itemMinY);
                    bestMaxX = Math.Max(bestMaxX, itemMaxX);
                    bestMaxY = Math.Max(bestMaxY, itemMaxY);
                }
            }

            double bestWidth = bestMinX == Double.MaxValue ? 0.0 : Math.Max(bestMaxX - bestMinX, 0.0);
            double bestHeight = bestMinY == Double.MaxValue ? 0.0 : Math.Max(bestMaxY - bestMinY, 0.0);
            double textMarginX = Math.Max(bestWidth * 0.40, width * 0.035);
            double textMarginY = Math.Max(bestHeight * 0.65, height * 0.10);

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
                    if (!keepIndexes.Contains(i))
                    {
                        elements.RemoveAt(i);
                    }

                    continue;
                }

                if (IsExternalRowValueText(row, item.Text))
                {
                    elements.RemoveAt(i);
                    continue;
                }

                if (bestMinX == Double.MaxValue)
                {
                    continue;
                }

                double centerX = item.HasBounds ? (item.BoundsMinX + item.BoundsMaxX) / 2.0 : item.X1;
                double centerY = item.HasBounds ? (item.BoundsMinY + item.BoundsMaxY) / 2.0 : item.Y1;
                bool nearBest = centerX >= bestMinX - textMarginX && centerX <= bestMaxX + textMarginX
                    && centerY >= bestMinY - textMarginY && centerY <= bestMaxY + textMarginY;

                if (!nearBest && IsExternalRowMetricText(row, item.Text))
                {
                    elements.RemoveAt(i);
                }
            }
        }

        private bool AreCadShapeGeometryElementsConnected(OviaCadShapeElement first, OviaCadShapeElement second, double tolerance)
        {
            if (first == null || second == null)
            {
                return false;
            }

            double firstMinX;
            double firstMinY;
            double firstMaxX;
            double firstMaxY;
            double secondMinX;
            double secondMinY;
            double secondMaxX;
            double secondMaxY;

            if (!TryGetCadShapeElementBounds(first, out firstMinX, out firstMinY, out firstMaxX, out firstMaxY)
                || !TryGetCadShapeElementBounds(second, out secondMinX, out secondMinY, out secondMaxX, out secondMaxY))
            {
                return false;
            }

            return AreCadShapeBoundsConnected(
                firstMinX,
                firstMinY,
                firstMaxX,
                firstMaxY,
                secondMinX,
                secondMinY,
                secondMaxX,
                secondMaxY,
                tolerance
            );
        }

        private bool IsClosedCadShapePolylineSource(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            Polyline polyline = entity as Polyline;

            if (polyline == null)
            {
                return false;
            }

            try
            {
                return polyline.Closed;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreCompactClosedCadShapePathSegments(
            List<OviaCadShapeElement> elements,
            OviaBarTableRow row,
            double width,
            double height)
        {
            if (elements == null || elements.Count < 2 || row == null
                || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            /*
             * OVIA 2026-08-07 _05 - 폐합 Polyline 위상 복구:
             * 커플러·사각 기호의 한 변이 표 GRID와 같은 좌표에 겹치면, GRID 제거 후 폐합 경로가
             * 세 변만 남을 수 있습니다. 원본이 Closed Polyline임이 확인된 경우에만 같은 원본
             * Handle의 잔존 선분 끝점 차수를 계산하고, 정확히 두 개의 열린 끝점이 남은 국소
             * 직교 경로를 한 선분으로 복구합니다. 일반 ㄱ/U형처럼 원본이 열린 Polyline인 형상은
             * 대상이 아니므로 임의로 닫지 않습니다.
             */
            Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "LINE" || !item.SourceClosedPath
                    || String.IsNullOrWhiteSpace(item.SourceIdentity))
                {
                    continue;
                }

                List<int> indexes;

                if (!groups.TryGetValue(item.SourceIdentity, out indexes))
                {
                    indexes = new List<int>();
                    groups[item.SourceIdentity] = indexes;
                }

                indexes.Add(i);
            }

            if (groups.Count == 0)
            {
                return;
            }

            double endpointTolerance = Math.Max(Math.Min(width, height) * 0.018, 0.025);
            double axisTolerance = Math.Max(Math.Min(width, height) * 0.012, 0.02);
            double maximumClosureWidth = Math.Max(width * 0.38, endpointTolerance * 4.0);
            double maximumClosureHeight = Math.Max(height * 0.68, endpointTolerance * 4.0);
            List<OviaCadShapeElement> restored = new List<OviaCadShapeElement>();

            foreach (KeyValuePair<string, List<int>> group in groups)
            {
                List<int> indexes = group.Value;

                if (indexes == null || indexes.Count < 2)
                {
                    continue;
                }

                List<OviaCadShapeEndpointCluster> endpointClusters = new List<OviaCadShapeEndpointCluster>();
                double componentMinX = Double.MaxValue;
                double componentMinY = Double.MaxValue;
                double componentMaxX = Double.MinValue;
                double componentMaxY = Double.MinValue;
                int indexPosition;

                for (indexPosition = 0; indexPosition < indexes.Count; indexPosition++)
                {
                    OviaCadShapeElement line = elements[indexes[indexPosition]];

                    if (line == null || line.Type != "LINE")
                    {
                        continue;
                    }

                    AddCadShapeEndpointCluster(endpointClusters, line.X1, line.Y1, endpointTolerance);
                    AddCadShapeEndpointCluster(endpointClusters, line.X2, line.Y2, endpointTolerance);
                    componentMinX = Math.Min(componentMinX, Math.Min(line.X1, line.X2));
                    componentMinY = Math.Min(componentMinY, Math.Min(line.Y1, line.Y2));
                    componentMaxX = Math.Max(componentMaxX, Math.Max(line.X1, line.X2));
                    componentMaxY = Math.Max(componentMaxY, Math.Max(line.Y1, line.Y2));
                }

                List<OviaCadShapeEndpointCluster> openEndpoints = new List<OviaCadShapeEndpointCluster>();
                int endpointIndex;

                for (endpointIndex = 0; endpointIndex < endpointClusters.Count; endpointIndex++)
                {
                    OviaCadShapeEndpointCluster endpoint = endpointClusters[endpointIndex];

                    if (endpoint != null && (endpoint.Count % 2) == 1)
                    {
                        openEndpoints.Add(endpoint);
                    }
                }

                if (openEndpoints.Count != 2 || componentMinX == Double.MaxValue)
                {
                    continue;
                }

                double componentWidth = Math.Max(componentMaxX - componentMinX, 0.0);
                double componentHeight = Math.Max(componentMaxY - componentMinY, 0.0);

                if (componentWidth > maximumClosureWidth || componentHeight > maximumClosureHeight)
                {
                    continue;
                }

                OviaCadShapeEndpointCluster first = openEndpoints[0];
                OviaCadShapeEndpointCluster second = openEndpoints[1];
                double dx = Math.Abs(first.X - second.X);
                double dy = Math.Abs(first.Y - second.Y);
                bool verticalClosure = dx <= axisTolerance && dy > endpointTolerance;
                bool horizontalClosure = dy <= axisTolerance && dx > endpointTolerance;

                if (!verticalClosure && !horizontalClosure)
                {
                    continue;
                }

                if ((verticalClosure && dy > maximumClosureHeight)
                    || (horizontalClosure && dx > maximumClosureWidth))
                {
                    continue;
                }

                bool alreadyExists = false;

                for (indexPosition = 0; indexPosition < indexes.Count; indexPosition++)
                {
                    OviaCadShapeElement existing = elements[indexes[indexPosition]];

                    if (existing != null
                        && existing.Type == "LINE"
                        && AreCadShapeNormalizedLineEndpointsEquivalent(
                            existing,
                            first.X,
                            first.Y,
                            second.X,
                            second.Y,
                            endpointTolerance))
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (alreadyExists)
                {
                    continue;
                }

                OviaCadShapeElement source = elements[indexes[0]];
                OviaCadShapeElement closure = new OviaCadShapeElement();
                closure.Type = "LINE";
                closure.X1 = first.X;
                closure.Y1 = first.Y;
                closure.X2 = second.X;
                closure.Y2 = second.Y;
                closure.ColorIndex = source == null ? 256 : source.ColorIndex;
                closure.SourceType = source == null ? "Polyline" : source.SourceType;
                closure.SourceHandle = source == null ? "" : source.SourceHandle;
                closure.SourceIdentity = group.Key;
                closure.SourceClosedPath = true;
                closure.HasWorldLine = true;
                double rowMinX = Math.Min(row.ShapeCellMinX, row.ShapeCellMaxX);
                double rowMaxY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
                closure.WorldX1 = rowMinX + closure.X1;
                closure.WorldY1 = rowMaxY - closure.Y1;
                closure.WorldX2 = rowMinX + closure.X2;
                closure.WorldY2 = rowMaxY - closure.Y2;
                closure.OriginalWorldX1 = closure.WorldX1;
                closure.OriginalWorldY1 = closure.WorldY1;
                closure.OriginalWorldX2 = closure.WorldX2;
                closure.OriginalWorldY2 = closure.WorldY2;
                restored.Add(closure);
            }

            for (i = 0; i < restored.Count; i++)
            {
                elements.Add(restored[i]);
            }
        }

        private void AddCadShapeEndpointCluster(
            List<OviaCadShapeEndpointCluster> clusters,
            double x,
            double y,
            double tolerance)
        {
            if (clusters == null)
            {
                return;
            }

            double toleranceSquared = tolerance * tolerance;
            int i;

            for (i = 0; i < clusters.Count; i++)
            {
                OviaCadShapeEndpointCluster cluster = clusters[i];

                if (cluster == null)
                {
                    continue;
                }

                double dx = cluster.X - x;
                double dy = cluster.Y - y;

                if (dx * dx + dy * dy <= toleranceSquared)
                {
                    cluster.X = (cluster.X * cluster.Count + x) / (cluster.Count + 1);
                    cluster.Y = (cluster.Y * cluster.Count + y) / (cluster.Count + 1);
                    cluster.Count++;
                    return;
                }
            }

            OviaCadShapeEndpointCluster created = new OviaCadShapeEndpointCluster();
            created.X = x;
            created.Y = y;
            created.Count = 1;
            clusters.Add(created);
        }

        private bool AreCadShapeNormalizedLineEndpointsEquivalent(
            OviaCadShapeElement line,
            double x1,
            double y1,
            double x2,
            double y2,
            double tolerance)
        {
            if (line == null || line.Type != "LINE")
            {
                return false;
            }

            double toleranceSquared = tolerance * tolerance;
            bool direct = IsCadShapeNormalizedPointNear(line.X1, line.Y1, x1, y1, toleranceSquared)
                && IsCadShapeNormalizedPointNear(line.X2, line.Y2, x2, y2, toleranceSquared);
            bool reverse = IsCadShapeNormalizedPointNear(line.X1, line.Y1, x2, y2, toleranceSquared)
                && IsCadShapeNormalizedPointNear(line.X2, line.Y2, x1, y1, toleranceSquared);
            return direct || reverse;
        }

        private bool IsCadShapeNormalizedPointNear(
            double x1,
            double y1,
            double x2,
            double y2,
            double toleranceSquared)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return dx * dx + dy * dy <= toleranceSquared;
        }

        private void RemoveCadShapeTextsOutsidePhysicalCellBounds(
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (elements == null || elements.Count == 0 || width <= 0.0001 || height <= 0.0001)
            {
                return;
            }

            /*
             * 형상 셀 선택창은 셀 경계에 걸친 문자와 (UP)/(DOWN)을 놓치지 않기 위해
             * 상·하·좌·우로 조금 확장합니다. SelectCrossingWindow의 확장 영역은 객체 발견용일 뿐이며,
             * 발견된 TEXT의 실제 소속 셀까지 확장해서는 안 됩니다.
             *
             * 이전 구현은 Y(행) 소속만 확인했기 때문에, 물리 형상 셀 X 범위가 넓게 잡히거나
             * 인접 열의 문자가 셀 경계에 살짝 교차하면 번호·철근규격·길이·수량·총길이·중량까지
             * 철근형상 TEXT로 저장될 수 있었습니다. 첨부 2026-08-06 CSV의 21~38번에서 한 행의
             * 전체 데이터가 OVIA_CAD_SHAPE_TEXTS에 들어간 것이 이 실패 형태입니다.
             *
             * 각 TEXT는 물리 형상 셀의 정규화 X/Y 범위 안에 실제 extents 중심 또는 기준점이
             * 존재하는 경우에만 현재 셀 소유로 인정합니다. 단, (UP)/(DOWN)은 기존 요구사항대로
             * 형상 셀 우측에 배치될 수 있으므로 X축에만 최대 셀 폭 25%의 예외를 허용합니다.
             * 일반 숫자·문자는 extents가 셀라인과 교차했다는 이유만으로 인접 셀에 배정하지 않습니다.
             */
            double ownershipToleranceX = Math.Max(width * 0.001, 0.005);
            double ownershipToleranceY = Math.Max(height * 0.001, 0.005);
            int i;

            for (i = elements.Count - 1; i >= 0; i--)
            {
                OviaCadShapeElement item = elements[i];

                if (item == null || item.Type != "TEXT")
                {
                    continue;
                }

                double centerX;
                double centerY;
                GetCadShapeTextCenter(item, out centerX, out centerY);

                bool centerBelongsToRow = centerY >= -ownershipToleranceY
                    && centerY <= height + ownershipToleranceY;
                bool referenceBelongsToRow = item.Y1 >= -ownershipToleranceY
                    && item.Y1 <= height + ownershipToleranceY;

                bool isDirectionLabel = IsCadShapeDirectionLabel(item.Text);
                double rightOwnershipMargin = isDirectionLabel
                    ? Math.Max(width * 0.25, 0.20)
                    : ownershipToleranceX;
                double minOwnedX = -ownershipToleranceX;
                double maxOwnedX = width + rightOwnershipMargin;
                bool centerBelongsToColumn = centerX >= minOwnedX && centerX <= maxOwnedX;
                bool referenceBelongsToColumn = item.X1 >= minOwnedX && item.X1 <= maxOwnedX;

                if ((!centerBelongsToRow && !referenceBelongsToRow)
                    || (!centerBelongsToColumn && !referenceBelongsToColumn))
                {
                    elements.RemoveAt(i);
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
            double tightMarginX = Math.Max(geomWidth * 0.20, width * 0.025);
            double tightMarginY = Math.Max(geomHeight * 0.45, height * 0.08);
            double tightMinX = geomMinX - tightMarginX;
            double tightMaxX = geomMaxX + tightMarginX;
            double tightMinY = geomMinY - tightMarginY;
            double tightMaxY = geomMaxY + tightMarginY;
            bool hasTrustedShapeRawText = row != null
                && row.ShapeRawText != null
                && row.ShapeRawText.Trim() != "";

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

                /*
                 * 원본 형상문자가 존재하는 기존 도면은 2026-07-22 이후의 확정 규칙을 그대로 유지합니다.
                 * 실제 원본 치수 또는 (UP)/(DOWN)은 우선 보존하고, 원본에 없는 순수 숫자만 제거합니다.
                 * 이 분기를 변경하지 않아 21~38번 복합 형상, 반복치수, 번호와 같은 값의 정상 형상치수가
                 * 이번 Dimension fallback 때문에 회귀하지 않도록 합니다.
                 */
                if (hasTrustedShapeRawText)
                {
                    if (ShapeRawTextContainsNumericValue(row.ShapeRawText, item.Text)
                        || IsCadShapeDirectionLabel(item.Text))
                    {
                        continue;
                    }

                    decimal unexpectedNumericText;

                    if (TryParseDecimalText(item.Text, out unexpectedNumericText))
                    {
                        elements.RemoveAt(i);
                        continue;
                    }

                    if (IsExternalRowValueText(row, item.Text))
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

                    continue;
                }

                /*
                 * OVIA 2026-08-07 _07 - Dimension 기반 형상 수치 보존:
                 *
                 * 일반 표 문자 스캐너(ExtractRowsByWindow)는 DBText/MText/Attribute/Block의
                 * 문자만 읽고 Dimension/Leader/MLeader 자체의 표시문자는 DATA 원문에 넣지 않습니다.
                 * 반면 CAD 형상 수집기는 해당 객체를 Explode하여 화면에 보이는 치수 TEXT를
                 * 정상 확보합니다. 따라서 ShapeRawText가 빈 행에서 "원문에 없는 숫자"라는 이유만으로
                 * 모든 숫자를 삭제하면 선은 남고 실제 형상 수치만 사라집니다.
                 *
                 * 원본이 비어 있는 행에 한해서만 물리 SHAPE 셀 안에 있고, 표 GRID 제거 후 남은 실제
                 * 철근 지오메트리와 충분히 가까운 숫자/각도를 화면 형상치수로 인정합니다.
                 * 번호·규격은 먼저 제외하고, 길이·수량·중량 등 인접 열 값은 실제 철근선과 떨어져 있으면
                 * 근접성 검증을 통과하지 못하므로 형상 TEXT로 승격되지 않습니다.
                 */
                if (IsCadShapeDirectionLabel(item.Text))
                {
                    continue;
                }

                if (IsExternalRowValueText(row, item.Text))
                {
                    elements.RemoveAt(i);
                    continue;
                }

                string dimensionKind;
                decimal dimensionValue;

                if (TryParseCadShapeDimensionText(item.Text, out dimensionKind, out dimensionValue))
                {
                    if (IsCadShapeDimensionTextOwnedByGeometry(item, elements, width, height))
                    {
                        continue;
                    }

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

        private bool IsCadShapeDimensionTextOwnedByGeometry(
            OviaCadShapeElement textItem,
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (textItem == null
                || textItem.Type != "TEXT"
                || elements == null
                || elements.Count == 0
                || width <= 0.0001
                || height <= 0.0001)
            {
                return false;
            }

            double centerX;
            double centerY;
            GetCadShapeTextCenter(textItem, out centerX, out centerY);

            /*
             * 이미 RemoveCadShapeTextsOutsidePhysicalCellBounds를 통과했지만,
             * ShapeCellBounds가 예외적으로 넓게 잡힌 경우까지 대비해 중심점도 다시 물리 셀에
             * 소속되는지 확인합니다. (UP)/(DOWN)은 이 함수의 대상이 아닙니다.
             */
            double xTolerance = Math.Max(width * 0.01, 0.05);
            double yTolerance = Math.Max(height * 0.03, 0.05);

            if (centerX < -xTolerance
                || centerX > width + xTolerance
                || centerY < -yTolerance
                || centerY > height + yTolerance)
            {
                return false;
            }

            double textHeight = textItem.Height > 0.0001
                ? textItem.Height
                : Math.Max(height * 0.10, 0.10);
            double maximumDistance = Math.Max(height * 0.34, textHeight * 1.6);
            maximumDistance = Math.Min(maximumDistance, height * 0.48);
            double nearestDistance = Double.MaxValue;
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement geometry = elements[i];

                if (geometry == null || geometry == textItem || geometry.Type == "TEXT")
                {
                    continue;
                }

                double distance = Double.MaxValue;

                if (geometry.Type == "LINE")
                {
                    distance = CadShapePointToLineSegmentDistance(
                        centerX,
                        centerY,
                        geometry.X1,
                        geometry.Y1,
                        geometry.X2,
                        geometry.Y2
                    );
                }
                else if (geometry.Type == "ARC" || geometry.Type == "CIRCLE")
                {
                    double centerDistance = CadShapePointDistance(
                        centerX,
                        centerY,
                        geometry.CX,
                        geometry.CY
                    );
                    distance = Math.Abs(centerDistance - Math.Abs(geometry.Radius));
                }

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                }
            }

            if (nearestDistance == Double.MaxValue)
            {
                return false;
            }

            return nearestDistance <= maximumDistance;
        }

        private void RemoveOverlappingCadShapeGhostDimensionTextClusters(
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (elements == null || elements.Count < 2)
            {
                return;
            }

            /*
             * 동적 블록의 비활성 가시성 상태 문자가 현재 표시 Explode 결과와 함께 들어오면
             * 화면에는 없는 200/400/500 숫자뿐 아니라 91°/74° 같은 각도도 동일한 작은 영역에
             * 겹쳐 저장될 수 있습니다. 이전 _21 안전장치는 순수 숫자만 대상으로 삼아 복합 형상의
             * 숨김 각도 사본은 통과했습니다.
             *
             * 순수 숫자와 각도를 같은 "형상 치수 문자" 범주로 묶어 강하게 겹치는 군집을 찾습니다.
             * 군집 안의 값 중 군집 밖의 서로 겹치지 않는 정상 위치에 같은 종류·같은 값이 존재하는
             * 후보를 찾고, 그 후보끼리 직접 겹치는 하위 군집만 비활성 상태 사본으로 제거합니다.
             * 따라서 CAD에 실제로 서로 다른 위치에 반복된 500, 200, 135° 등은 그대로 보존합니다.
             */
            List<int> dimensionTextIndexes = new List<int>();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];
                string ignoredKind;
                decimal ignoredValue;

                if (item != null
                    && item.Type == "TEXT"
                    && TryParseCadShapeDimensionText(item.Text, out ignoredKind, out ignoredValue))
                {
                    dimensionTextIndexes.Add(i);
                }
            }

            if (dimensionTextIndexes.Count < 2)
            {
                return;
            }

            HashSet<int> visited = new HashSet<int>();
            HashSet<int> removeIndexes = new HashSet<int>();
            int textPosition;

            for (textPosition = 0; textPosition < dimensionTextIndexes.Count; textPosition++)
            {
                int seedIndex = dimensionTextIndexes[textPosition];

                if (visited.Contains(seedIndex))
                {
                    continue;
                }

                List<int> cluster = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(seedIndex);
                visited.Add(seedIndex);

                while (queue.Count > 0)
                {
                    int currentIndex = queue.Dequeue();
                    cluster.Add(currentIndex);
                    int comparePosition;

                    for (comparePosition = 0; comparePosition < dimensionTextIndexes.Count; comparePosition++)
                    {
                        int compareIndex = dimensionTextIndexes[comparePosition];

                        if (visited.Contains(compareIndex))
                        {
                            continue;
                        }

                        if (AreCadShapeDimensionTextsStronglyOverlapping(
                            elements[currentIndex],
                            elements[compareIndex],
                            width,
                            height))
                        {
                            visited.Add(compareIndex);
                            queue.Enqueue(compareIndex);
                        }
                    }
                }

                if (cluster.Count < 2)
                {
                    continue;
                }

                HashSet<int> clusterSet = new HashSet<int>(cluster);
                List<int> outsideDuplicatedClusterItems = new List<int>();
                int clusterPosition;

                for (clusterPosition = 0; clusterPosition < cluster.Count; clusterPosition++)
                {
                    int clusterIndex = cluster[clusterPosition];

                    if (HasIndependentOutsideCadShapeDimensionCopy(
                        clusterIndex,
                        clusterSet,
                        dimensionTextIndexes,
                        elements,
                        width,
                        height))
                    {
                        outsideDuplicatedClusterItems.Add(clusterIndex);
                    }
                }

                /*
                 * 숨김 91°/74°가 정상 130°/135°와 하나의 겹침 컴포넌트로 연결되더라도
                 * 컴포넌트 전체를 지우지 않습니다. 외부 정상 사본이 있는 후보끼리 직접 겹치는
                 * 2개 이상의 하위 군집만 제거하여 외부 사본이 없는 정상 치수를 보존합니다.
                 */
                if (outsideDuplicatedClusterItems.Count < 2)
                {
                    continue;
                }

                HashSet<int> duplicatedVisited = new HashSet<int>();
                int duplicatedPosition;

                for (duplicatedPosition = 0; duplicatedPosition < outsideDuplicatedClusterItems.Count; duplicatedPosition++)
                {
                    int duplicatedSeed = outsideDuplicatedClusterItems[duplicatedPosition];

                    if (duplicatedVisited.Contains(duplicatedSeed))
                    {
                        continue;
                    }

                    List<int> duplicatedSubCluster = new List<int>();
                    Queue<int> duplicatedQueue = new Queue<int>();
                    duplicatedQueue.Enqueue(duplicatedSeed);
                    duplicatedVisited.Add(duplicatedSeed);

                    while (duplicatedQueue.Count > 0)
                    {
                        int duplicatedCurrent = duplicatedQueue.Dequeue();
                        duplicatedSubCluster.Add(duplicatedCurrent);
                        int duplicatedComparePosition;

                        for (duplicatedComparePosition = 0; duplicatedComparePosition < outsideDuplicatedClusterItems.Count; duplicatedComparePosition++)
                        {
                            int duplicatedCompare = outsideDuplicatedClusterItems[duplicatedComparePosition];

                            if (duplicatedVisited.Contains(duplicatedCompare))
                            {
                                continue;
                            }

                            if (AreCadShapeDimensionTextsStronglyOverlapping(
                                elements[duplicatedCurrent],
                                elements[duplicatedCompare],
                                width,
                                height))
                            {
                                duplicatedVisited.Add(duplicatedCompare);
                                duplicatedQueue.Enqueue(duplicatedCompare);
                            }
                        }
                    }

                    if (duplicatedSubCluster.Count < 2)
                    {
                        continue;
                    }

                    int removePosition;

                    for (removePosition = 0; removePosition < duplicatedSubCluster.Count; removePosition++)
                    {
                        removeIndexes.Add(duplicatedSubCluster[removePosition]);
                    }
                }
            }

            if (removeIndexes.Count == 0)
            {
                return;
            }

            List<int> orderedRemoveIndexes = new List<int>(removeIndexes);
            orderedRemoveIndexes.Sort();

            for (i = orderedRemoveIndexes.Count - 1; i >= 0; i--)
            {
                elements.RemoveAt(orderedRemoveIndexes[i]);
            }
        }


        private bool HasIndependentOutsideCadShapeDimensionCopy(
            int targetIndex,
            HashSet<int> clusterSet,
            List<int> dimensionTextIndexes,
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (clusterSet == null
                || dimensionTextIndexes == null
                || elements == null
                || targetIndex < 0
                || targetIndex >= elements.Count)
            {
                return false;
            }

            string targetKind;
            decimal targetValue;

            if (!TryParseCadShapeDimensionText(elements[targetIndex].Text, out targetKind, out targetValue))
            {
                return false;
            }

            int outsidePosition;

            for (outsidePosition = 0; outsidePosition < dimensionTextIndexes.Count; outsidePosition++)
            {
                int outsideIndex = dimensionTextIndexes[outsidePosition];

                if (clusterSet.Contains(outsideIndex))
                {
                    continue;
                }

                string outsideKind;
                decimal outsideValue;

                if (TryParseCadShapeDimensionText(elements[outsideIndex].Text, out outsideKind, out outsideValue)
                    && String.Equals(targetKind, outsideKind, StringComparison.Ordinal)
                    && AreDecimalValuesEqualAtThreeDecimals(targetValue, outsideValue)
                    && IsCadShapeDimensionTextSpatiallyIsolated(
                        outsideIndex,
                        dimensionTextIndexes,
                        elements,
                        width,
                        height))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCadShapeDimensionTextSpatiallyIsolated(
            int targetIndex,
            List<int> dimensionTextIndexes,
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (dimensionTextIndexes == null || elements == null || targetIndex < 0 || targetIndex >= elements.Count)
            {
                return false;
            }

            int i;

            for (i = 0; i < dimensionTextIndexes.Count; i++)
            {
                int compareIndex = dimensionTextIndexes[i];

                if (compareIndex == targetIndex || compareIndex < 0 || compareIndex >= elements.Count)
                {
                    continue;
                }

                if (AreCadShapeDimensionTextsStronglyOverlapping(
                    elements[targetIndex],
                    elements[compareIndex],
                    width,
                    height))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AreCadShapeDimensionTextsStronglyOverlapping(
            OviaCadShapeElement first,
            OviaCadShapeElement second,
            double width,
            double height)
        {
            double firstMinX;
            double firstMinY;
            double firstMaxX;
            double firstMaxY;
            double secondMinX;
            double secondMinY;
            double secondMaxX;
            double secondMaxY;

            if (!TryGetCadShapeTextBoundsForOverlap(first, width, height, out firstMinX, out firstMinY, out firstMaxX, out firstMaxY)
                || !TryGetCadShapeTextBoundsForOverlap(second, width, height, out secondMinX, out secondMinY, out secondMaxX, out secondMaxY))
            {
                return false;
            }

            double intersectionWidth = Math.Min(firstMaxX, secondMaxX) - Math.Max(firstMinX, secondMinX);
            double intersectionHeight = Math.Min(firstMaxY, secondMaxY) - Math.Max(firstMinY, secondMinY);
            double firstWidth = Math.Max(firstMaxX - firstMinX, 0.0001);
            double firstHeight = Math.Max(firstMaxY - firstMinY, 0.0001);
            double secondWidth = Math.Max(secondMaxX - secondMinX, 0.0001);
            double secondHeight = Math.Max(secondMaxY - secondMinY, 0.0001);

            if (intersectionWidth > 0.0 && intersectionHeight > 0.0)
            {
                double intersectionArea = intersectionWidth * intersectionHeight;
                double smallerArea = Math.Min(firstWidth * firstHeight, secondWidth * secondHeight);

                if (smallerArea > 0.000001 && intersectionArea / smallerArea >= 0.50)
                {
                    return true;
                }
            }

            double firstCenterX = (firstMinX + firstMaxX) / 2.0;
            double firstCenterY = (firstMinY + firstMaxY) / 2.0;
            double secondCenterX = (secondMinX + secondMaxX) / 2.0;
            double secondCenterY = (secondMinY + secondMaxY) / 2.0;
            double deltaX = firstCenterX - secondCenterX;
            double deltaY = firstCenterY - secondCenterY;
            double centerDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            double textScale = Math.Max(Math.Min(firstHeight, secondHeight), Math.Min(width, height) * 0.005);

            return centerDistance <= Math.Max(textScale * 0.70, 0.01);
        }

        private bool TryGetCadShapeTextBoundsForOverlap(
            OviaCadShapeElement item,
            double width,
            double height,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY)
        {
            minX = 0.0;
            minY = 0.0;
            maxX = 0.0;
            maxY = 0.0;

            if (item == null || item.Type != "TEXT")
            {
                return false;
            }

            if (item.HasBounds
                && item.BoundsMaxX > item.BoundsMinX
                && item.BoundsMaxY > item.BoundsMinY)
            {
                minX = item.BoundsMinX;
                minY = item.BoundsMinY;
                maxX = item.BoundsMaxX;
                maxY = item.BoundsMaxY;
                return true;
            }

            double estimatedHeight = Math.Max(item.Height, Math.Max(Math.Min(width, height) * 0.015, 0.1));
            double estimatedWidth = Math.Max(
                estimatedHeight * 0.55 * Math.Max(item.Text == null ? 0 : item.Text.Length, 1),
                estimatedHeight
            );

            minX = item.X1 - estimatedWidth / 2.0;
            maxX = item.X1 + estimatedWidth / 2.0;
            minY = item.Y1 - estimatedHeight / 2.0;
            maxY = item.Y1 + estimatedHeight / 2.0;
            return true;
        }

        private bool TryParseCadShapeDimensionText(string value, out string kind, out decimal number)
        {
            kind = "";
            number = 0M;

            if (value == null)
            {
                return false;
            }

            string normalized = value.Trim();

            if (normalized == "")
            {
                return false;
            }

            normalized = normalized
                .Replace("%%D", "°")
                .Replace("%%d", "°")
                .Replace("˚", "°")
                .Replace("º", "°");

            bool isAngle = normalized.EndsWith("°", StringComparison.Ordinal);

            if (isAngle)
            {
                normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            }

            if (!TryParseDecimalText(normalized, out number))
            {
                return false;
            }

            kind = isAngle ? "ANGLE" : "NUMBER";
            return true;
        }

        private void RemoveExcessCadShapeNumericTexts(
            OviaBarTableRow row,
            List<OviaCadShapeElement> elements,
            double width,
            double height)
        {
            if (row == null
                || row.ShapeRawText == null
                || row.ShapeRawText.Trim() == ""
                || elements == null
                || elements.Count == 0)
            {
                return;
            }

            Dictionary<decimal, int> expectedCounts = BuildExpectedCadShapeNumericCounts(row.ShapeRawText);

            if (expectedCounts.Count == 0)
            {
                return;
            }

            Dictionary<decimal, List<int>> candidateIndexes = new Dictionary<decimal, List<int>>();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                OviaCadShapeElement item = elements[i];
                decimal numericValue;

                if (item == null
                    || item.Type != "TEXT"
                    || !TryParseDecimalText(item.Text, out numericValue))
                {
                    continue;
                }

                decimal key = Math.Round(numericValue, 3, MidpointRounding.AwayFromZero);

                if (!expectedCounts.ContainsKey(key))
                {
                    continue;
                }

                List<int> indexes;

                if (!candidateIndexes.TryGetValue(key, out indexes))
                {
                    indexes = new List<int>();
                    candidateIndexes.Add(key, indexes);
                }

                indexes.Add(i);
            }

            double geometryMinX;
            double geometryMinY;
            double geometryMaxX;
            double geometryMaxY;
            bool hasGeometry = GetCadShapeContentBounds(
                elements,
                false,
                out geometryMinX,
                out geometryMinY,
                out geometryMaxX,
                out geometryMaxY
            );
            HashSet<int> removeIndexes = new HashSet<int>();

            foreach (KeyValuePair<decimal, List<int>> pair in candidateIndexes)
            {
                int expectedCount;

                if (!expectedCounts.TryGetValue(pair.Key, out expectedCount)
                    || pair.Value.Count <= expectedCount)
                {
                    continue;
                }

                pair.Value.Sort(delegate(int firstIndex, int secondIndex)
                {
                    OviaCadShapeElement first = elements[firstIndex];
                    OviaCadShapeElement second = elements[secondIndex];
                    double firstOverflow = GetCadShapeTextCellOverflowDistance(first, width, height);
                    double secondOverflow = GetCadShapeTextCellOverflowDistance(second, width, height);
                    int overflowCompare = firstOverflow.CompareTo(secondOverflow);

                    if (overflowCompare != 0)
                    {
                        return overflowCompare;
                    }

                    if (hasGeometry)
                    {
                        double firstGeometryDistance = GetCadShapeTextGeometryDistance(
                            first,
                            geometryMinX,
                            geometryMinY,
                            geometryMaxX,
                            geometryMaxY
                        );
                        double secondGeometryDistance = GetCadShapeTextGeometryDistance(
                            second,
                            geometryMinX,
                            geometryMinY,
                            geometryMaxX,
                            geometryMaxY
                        );
                        int geometryCompare = firstGeometryDistance.CompareTo(secondGeometryDistance);

                        if (geometryCompare != 0)
                        {
                            return geometryCompare;
                        }
                    }

                    return firstIndex.CompareTo(secondIndex);
                });

                for (i = expectedCount; i < pair.Value.Count; i++)
                {
                    removeIndexes.Add(pair.Value[i]);
                }
            }

            if (removeIndexes.Count == 0)
            {
                return;
            }

            List<int> orderedRemoveIndexes = new List<int>(removeIndexes);
            orderedRemoveIndexes.Sort();

            for (i = orderedRemoveIndexes.Count - 1; i >= 0; i--)
            {
                elements.RemoveAt(orderedRemoveIndexes[i]);
            }
        }

        private Dictionary<decimal, int> BuildExpectedCadShapeNumericCounts(string shapeRawText)
        {
            Dictionary<decimal, int> counts = new Dictionary<decimal, int>();

            if (shapeRawText == null || shapeRawText.Trim() == "")
            {
                return counts;
            }

            MatchCollection matches = GetExpectedCadShapeDimensionMatches(shapeRawText);
            int i;

            for (i = 0; i < matches.Count; i++)
            {
                decimal value;

                if (!TryParseDecimalText(matches[i].Value, out value))
                {
                    continue;
                }

                decimal key = Math.Round(value, 3, MidpointRounding.AwayFromZero);
                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }

            return counts;
        }

        private double GetCadShapeTextCellOverflowDistance(OviaCadShapeElement item, double width, double height)
        {
            double centerX;
            double centerY;
            GetCadShapeTextCenter(item, out centerX, out centerY);
            double overflowX = centerX < 0.0
                ? -centerX
                : (centerX > width ? centerX - width : 0.0);
            double overflowY = centerY < 0.0
                ? -centerY
                : (centerY > height ? centerY - height : 0.0);

            return Math.Sqrt(overflowX * overflowX + overflowY * overflowY);
        }

        private double GetCadShapeTextGeometryDistance(
            OviaCadShapeElement item,
            double minX,
            double minY,
            double maxX,
            double maxY)
        {
            double centerX;
            double centerY;
            GetCadShapeTextCenter(item, out centerX, out centerY);
            double distanceX = centerX < minX
                ? minX - centerX
                : (centerX > maxX ? centerX - maxX : 0.0);
            double distanceY = centerY < minY
                ? minY - centerY
                : (centerY > maxY ? centerY - maxY : 0.0);

            return Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
        }

        private void GetCadShapeTextCenter(OviaCadShapeElement item, out double centerX, out double centerY)
        {
            centerX = 0.0;
            centerY = 0.0;

            if (item == null)
            {
                return;
            }

            centerX = item.HasBounds
                ? (item.BoundsMinX + item.BoundsMaxX) / 2.0
                : item.X1;
            centerY = item.HasBounds
                ? (item.BoundsMinY + item.BoundsMaxY) / 2.0
                : item.Y1;
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
            double geometryMinX;
            double geometryMinY;
            double geometryMaxX;
            double geometryMaxY;
            bool hasGeometryBounds = GetCadShapeContentBounds(
                elements,
                false,
                out geometryMinX,
                out geometryMinY,
                out geometryMaxX,
                out geometryMaxY);
            double physicalCellWidth = row == null ? 0.0 : Math.Abs(row.ShapeCellMaxX - row.ShapeCellMinX);
            double physicalCellHeight = row == null ? 0.0 : Math.Abs(row.ShapeCellMaxY - row.ShapeCellMinY);
            bool preservePhysicalCellLayout = physicalCellWidth > 0.0001 && physicalCellHeight > 0.0001;
            double offsetX;
            double offsetY;
            double outputWidth;
            double outputHeight;

            if (preservePhysicalCellLayout)
            {
                /*
                 * JSON v3 SOURCE_CELL 레이아웃:
                 * 철근형상 콘텐츠만 타이트하게 잘라 저장하면 CAD 셀 안에서의 좌우·상하 여백과
                 * 문자/숫자/철근선의 상대 좌표가 사라집니다. 이후 BarList에서 전체 콘텐츠를
                 * 다시 중앙 확대하면서 긴 설명문자가 철근규격 컬럼까지 넘어가는 원인이 됩니다.
                 *
                 * 새 추출본은 실제 철근형상 물리 셀의 0~width / 0~height 좌표계를 그대로 저장합니다.
                 * 단, 고정 기능인 (DOWN)/(UP)이 셀 가장자리에 조금 벗어나 배치된 도면은 해당 방향
                 * 문자의 실제 bounds까지만 viewport를 확장하여 누락시키지 않습니다.
                 */
                double viewportMinX = 0.0;
                double viewportMinY = 0.0;
                double viewportMaxX = physicalCellWidth;
                double viewportMaxY = physicalCellHeight;

                /*
                 * _04에서 수평 소유 여유로 복구한 커플러·작은 사각 훅의 외곽선은
                 * 원래 SHAPE 셀 좌표계에서 x<0 또는 x>cell.width가 될 수 있습니다.
                 * 추출만 복구하고 SOURCE_CELL viewport를 0~width로 고정하면 렌더러에서
                 * 다시 잘리므로, 허용된 수평 소유 여유 안의 실제 지오메트리 bounds까지만
                 * viewport를 함께 확장합니다. 문자와 행 Y는 기존 물리 셀 계약을 유지합니다.
                 */
                if (hasGeometryBounds)
                {
                    double geometryOwnershipMarginX = GetCadShapeHorizontalGeometryOwnershipMargin(
                        physicalCellWidth,
                        physicalCellHeight);
                    double minimumOwnedX = -geometryOwnershipMarginX;
                    double maximumOwnedX = physicalCellWidth + geometryOwnershipMarginX;

                    if (geometryMinX < viewportMinX)
                    {
                        viewportMinX = Math.Max(geometryMinX, minimumOwnedX);
                    }

                    if (geometryMaxX > viewportMaxX)
                    {
                        viewportMaxX = Math.Min(geometryMaxX, maximumOwnedX);
                    }
                }

                int directionIndex;

                for (directionIndex = 0; directionIndex < elements.Count; directionIndex++)
                {
                    OviaCadShapeElement directionItem = elements[directionIndex];

                    if (directionItem == null
                        || directionItem.Type != "TEXT"
                        || !IsCadShapeDirectionLabel(directionItem.Text))
                    {
                        continue;
                    }

                    if (directionItem.HasBounds)
                    {
                        viewportMinX = Math.Min(viewportMinX, directionItem.BoundsMinX);
                        viewportMinY = Math.Min(viewportMinY, directionItem.BoundsMinY);
                        viewportMaxX = Math.Max(viewportMaxX, directionItem.BoundsMaxX);
                        viewportMaxY = Math.Max(viewportMaxY, directionItem.BoundsMaxY);
                    }
                    else
                    {
                        viewportMinX = Math.Min(viewportMinX, directionItem.X1);
                        viewportMinY = Math.Min(viewportMinY, directionItem.Y1);
                        viewportMaxX = Math.Max(viewportMaxX, directionItem.X1);
                        viewportMaxY = Math.Max(viewportMaxY, directionItem.Y1);
                    }
                }

                offsetX = viewportMinX;
                offsetY = viewportMinY;
                outputWidth = Math.Max(viewportMaxX - viewportMinX, 1.0);
                outputHeight = Math.Max(viewportMaxY - viewportMinY, 1.0);
            }
            else
            {
                // 물리 셀 경계를 확인할 수 없는 과거/예외 데이터는 기존 content-bounds 호환 방식을 유지합니다.
                if (!hasBounds)
                {
                    cropMinX = 0;
                    cropMinY = 0;
                    cropMaxX = 100;
                    cropMaxY = 60;
                }

                double contentWidth = Math.Max(cropMaxX - cropMinX, 1.0);
                double contentHeight = Math.Max(cropMaxY - cropMinY, 1.0);
                double padX = Math.Max(contentWidth * 0.06, 0.8);
                double padY = Math.Max(contentHeight * 0.08, 0.8);
                offsetX = cropMinX - padX;
                offsetY = cropMinY - padY;
                outputWidth = contentWidth + padX * 2.0;
                outputHeight = contentHeight + padY * 2.0;
            }

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
            sb.Append("  \"layoutPolicy\": ");
            AppendJsonString(sb, preservePhysicalCellLayout ? "SOURCE_CELL" : "CONTENT_BOUNDS");
            sb.Append(",\r\n");
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

        private static void DeleteBatchExtractionArtifacts(string csvFilePath)
        {
            if (csvFilePath == null || csvFilePath.Trim() == "")
            {
                return;
            }

            DeleteFileQuietly(csvFilePath);
            DeleteFileQuietly(csvFilePath + ".tmp");
            DeleteFileQuietly(csvFilePath + ".ready");
            DeleteFileQuietly(csvFilePath + ".ready.tmp");

            try
            {
                string csvDirectory = Path.GetDirectoryName(csvFilePath);
                string csvBaseName = Path.GetFileNameWithoutExtension(csvFilePath);

                if (csvDirectory == null || csvDirectory.Trim() == ""
                    || csvBaseName == null || csvBaseName.Trim() == "")
                {
                    return;
                }

                string shapeDirectory = Path.Combine(
                    csvDirectory,
                    "Shapes",
                    SanitizeFileName(csvBaseName)
                );

                if (Directory.Exists(shapeDirectory))
                {
                    Directory.Delete(shapeDirectory, true);
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
                bool usedRedSelectionColor;
                CreateOviaBoxEntity(db, tr, point1, point2, dashedLineTypeId, out usedRedSelectionColor);
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
            List<OviaSelectionRectangle> existingRectangles = GetExistingOviaSelectionRectangles(db);

            return BuildNonOverlappingOviaSelectionRectangles(
                point1,
                point2,
                existingRectangles,
                out overlappedBoxCount
            );
        }

        private List<OviaSelectionRectangle> BuildNonOverlappingOviaSelectionRectangles(
            Point3d point1,
            Point3d point2,
            List<OviaSelectionRectangle> blockingRectangles,
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

            if (blockingRectangles == null || blockingRectangles.Count == 0)
            {
                return remainingRectangles;
            }

            int blockingIndex;

            for (blockingIndex = 0; blockingIndex < blockingRectangles.Count; blockingIndex++)
            {
                OviaSelectionRectangle blockingRectangle = blockingRectangles[blockingIndex];

                if (blockingRectangle == null)
                {
                    continue;
                }

                List<OviaSelectionRectangle> nextRectangles = new List<OviaSelectionRectangle>();
                bool overlappedCurrentBox = false;
                int remainingIndex;

                for (remainingIndex = 0; remainingIndex < remainingRectangles.Count; remainingIndex++)
                {
                    OviaSelectionRectangle candidate = remainingRectangles[remainingIndex];

                    if (!HasMeaningfulHorizontalOverlap(candidate, blockingRectangle))
                    {
                        nextRectangles.Add(candidate);
                        continue;
                    }

                    double overlapMinY = Math.Max(candidate.MinY, blockingRectangle.MinY);
                    double overlapMaxY = Math.Min(candidate.MaxY, blockingRectangle.MaxY);

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

        private bool ShouldUseRedOviaSelectionBox(
            Database db,
            Transaction tr,
            Point3d point1,
            Point3d point2)
        {
            if (db == null || tr == null)
            {
                return false;
            }

            OviaSelectionColorStats stats = new OviaSelectionColorStats();
            stats.MinX = Math.Min(point1.X, point2.X);
            stats.MaxX = Math.Max(point1.X, point2.X);
            stats.MinY = Math.Min(point1.Y, point2.Y);
            stats.MaxY = Math.Max(point1.Y, point2.Y);

            double width = Math.Max(stats.MaxX - stats.MinX, 0.0001);
            double height = Math.Max(stats.MaxY - stats.MinY, 0.0001);
            stats.AxisTolerance = Math.Max(Math.Max(width, height) * 0.0005, 0.0001);
            stats.MinimumAxisLength = Math.Max(Math.Min(width, height) * 0.01, stats.AxisTolerance * 4.0);

            BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;

            if (blockTable == null || !blockTable.Has(BlockTableRecord.ModelSpace))
            {
                return false;
            }

            BlockTableRecord modelSpace = tr.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForRead,
                false
            ) as BlockTableRecord;

            if (modelSpace == null)
            {
                return false;
            }

            foreach (ObjectId entityId in modelSpace)
            {
                Entity entity = tr.GetObject(entityId, OpenMode.ForRead, false) as Entity;

                if (entity == null)
                {
                    continue;
                }

                CollectSelectionBoxColorStatsFromEntity(
                    tr,
                    entity,
                    Matrix3d.Identity,
                    null,
                    false,
                    stats,
                    0
                );
            }

            if (stats.YellowAxisSegmentCount < 4
                || stats.YellowHorizontalCount < 2
                || stats.YellowVerticalCount < 2
                || stats.AxisSegmentCount <= 0)
            {
                return false;
            }

            double yellowCountRatio = (double)stats.YellowAxisSegmentCount / (double)stats.AxisSegmentCount;
            double yellowLengthRatio = stats.TotalAxisLength <= 0.0001
                ? 0.0
                : stats.YellowAxisLength / stats.TotalAxisLength;

            /*
             * 표 안의 철근 형상선은 흰색/회색일 수 있으므로 단순 객체 개수만 비교하면
             * 노란 테이블인데도 오판할 수 있습니다. 가로·세로 노란 경계선이 모두 있고,
             * 노란 선 길이 또는 개수 중 하나가 선택영역의 축 정렬선에서 우세하면
             * 테이블 선이 노란색인 것으로 판정합니다.
             */
            return yellowLengthRatio >= 0.55 || yellowCountRatio >= 0.60;
        }

        private void CollectSelectionBoxColorStatsFromEntity(
            Transaction tr,
            Entity entity,
            Matrix3d transform,
            OviaResolvedCadColor inheritedBlockColor,
            bool insideBlock,
            OviaSelectionColorStats stats,
            int depth)
        {
            if (tr == null || entity == null || stats == null || depth > 8)
            {
                return;
            }

            if (string.Equals(entity.Layer, OviaBoxLayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OviaResolvedCadColor effectiveColor = ResolveEffectiveEntityColor(
                tr,
                entity,
                inheritedBlockColor,
                insideBlock
            );

            Line line = entity as Line;

            if (line != null)
            {
                AddSelectionBoxColorSegment(
                    line.StartPoint.TransformBy(transform),
                    line.EndPoint.TransformBy(transform),
                    effectiveColor,
                    stats
                );
                return;
            }

            Polyline polyline = entity as Polyline;

            if (polyline != null)
            {
                int vertexCount = polyline.NumberOfVertices;
                int vertexIndex;

                for (vertexIndex = 0; vertexIndex < vertexCount - 1; vertexIndex++)
                {
                    AddSelectionBoxColorSegment(
                        polyline.GetPoint3dAt(vertexIndex).TransformBy(transform),
                        polyline.GetPoint3dAt(vertexIndex + 1).TransformBy(transform),
                        effectiveColor,
                        stats
                    );
                }

                if (polyline.Closed && vertexCount > 1)
                {
                    AddSelectionBoxColorSegment(
                        polyline.GetPoint3dAt(vertexCount - 1).TransformBy(transform),
                        polyline.GetPoint3dAt(0).TransformBy(transform),
                        effectiveColor,
                        stats
                    );
                }

                return;
            }

            BlockReference blockReference = entity as BlockReference;

            if (blockReference == null)
            {
                return;
            }

            BlockTableRecord blockRecord = tr.GetObject(
                blockReference.BlockTableRecord,
                OpenMode.ForRead,
                false
            ) as BlockTableRecord;

            if (blockRecord == null)
            {
                return;
            }

            Matrix3d nextTransform = transform * blockReference.BlockTransform;

            foreach (ObjectId childId in blockRecord)
            {
                Entity childEntity = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;

                if (childEntity == null)
                {
                    continue;
                }

                CollectSelectionBoxColorStatsFromEntity(
                    tr,
                    childEntity,
                    nextTransform,
                    effectiveColor,
                    true,
                    stats,
                    depth + 1
                );
            }
        }

        private void AddSelectionBoxColorSegment(
            Point3d point1,
            Point3d point2,
            OviaResolvedCadColor effectiveColor,
            OviaSelectionColorStats stats)
        {
            double dx = Math.Abs(point1.X - point2.X);
            double dy = Math.Abs(point1.Y - point2.Y);
            bool horizontal = dy <= stats.AxisTolerance && dx > stats.AxisTolerance;
            bool vertical = dx <= stats.AxisTolerance && dy > stats.AxisTolerance;

            if (!horizontal && !vertical)
            {
                return;
            }

            double segmentMinX = Math.Min(point1.X, point2.X);
            double segmentMaxX = Math.Max(point1.X, point2.X);
            double segmentMinY = Math.Min(point1.Y, point2.Y);
            double segmentMaxY = Math.Max(point1.Y, point2.Y);

            if (segmentMaxX < stats.MinX - stats.AxisTolerance
                || segmentMinX > stats.MaxX + stats.AxisTolerance
                || segmentMaxY < stats.MinY - stats.AxisTolerance
                || segmentMinY > stats.MaxY + stats.AxisTolerance)
            {
                return;
            }

            double coveredLength;

            if (horizontal)
            {
                coveredLength = Math.Max(
                    0.0,
                    Math.Min(segmentMaxX, stats.MaxX) - Math.Max(segmentMinX, stats.MinX)
                );
            }
            else
            {
                coveredLength = Math.Max(
                    0.0,
                    Math.Min(segmentMaxY, stats.MaxY) - Math.Max(segmentMinY, stats.MinY)
                );
            }

            if (coveredLength < stats.MinimumAxisLength)
            {
                return;
            }

            stats.AxisSegmentCount++;
            stats.TotalAxisLength += coveredLength;

            if (!IsResolvedCadColorYellow(effectiveColor))
            {
                return;
            }

            stats.YellowAxisSegmentCount++;
            stats.YellowAxisLength += coveredLength;

            if (horizontal)
            {
                stats.YellowHorizontalCount++;
            }
            else
            {
                stats.YellowVerticalCount++;
            }
        }

        private OviaResolvedCadColor ResolveEffectiveEntityColor(
            Transaction tr,
            Entity entity,
            OviaResolvedCadColor inheritedBlockColor,
            bool insideBlock)
        {
            if (entity == null)
            {
                return new OviaResolvedCadColor();
            }

            Color entityColor = null;
            int colorIndex = 256;

            try
            {
                entityColor = entity.Color;
                colorIndex = entityColor == null ? entity.ColorIndex : entityColor.ColorIndex;
            }
            catch
            {
                colorIndex = entity.ColorIndex;
            }

            if (colorIndex == 0)
            {
                if (inheritedBlockColor != null && inheritedBlockColor.IsValid)
                {
                    return inheritedBlockColor.Clone();
                }

                return ResolveLayerCadColor(tr, entity.LayerId);
            }

            if (colorIndex == 256)
            {
                if (insideBlock
                    && string.Equals(entity.Layer, "0", StringComparison.OrdinalIgnoreCase)
                    && inheritedBlockColor != null
                    && inheritedBlockColor.IsValid)
                {
                    return inheritedBlockColor.Clone();
                }

                return ResolveLayerCadColor(tr, entity.LayerId);
            }

            return CreateResolvedCadColor(entityColor, colorIndex);
        }

        private OviaResolvedCadColor ResolveLayerCadColor(Transaction tr, ObjectId layerId)
        {
            if (tr == null || layerId.IsNull)
            {
                return new OviaResolvedCadColor();
            }

            LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead, false) as LayerTableRecord;

            if (layer == null)
            {
                return new OviaResolvedCadColor();
            }

            Color layerColor = layer.Color;
            int layerColorIndex = layerColor == null ? 256 : layerColor.ColorIndex;
            return CreateResolvedCadColor(layerColor, layerColorIndex);
        }

        private OviaResolvedCadColor CreateResolvedCadColor(Color color, int colorIndex)
        {
            OviaResolvedCadColor result = new OviaResolvedCadColor();
            result.ColorIndex = colorIndex;
            result.IsValid = color != null;

            if (color == null)
            {
                return result;
            }

            try
            {
                result.Red = color.Red;
                result.Green = color.Green;
                result.Blue = color.Blue;
            }
            catch
            {
                result.Red = 0;
                result.Green = 0;
                result.Blue = 0;
            }

            return result;
        }

        private bool IsResolvedCadColorYellow(OviaResolvedCadColor color)
        {
            if (color == null || !color.IsValid)
            {
                return false;
            }

            if (color.ColorIndex == 2)
            {
                return true;
            }

            return color.Red >= 250 && color.Green >= 250 && color.Blue <= 10;
        }

        private ObjectId CreateOviaBoxEntity(
            Database db,
            Transaction tr,
            Point3d point1,
            Point3d point2,
            ObjectId dashedLineTypeId,
            out bool usedRedSelectionColor)
        {
            usedRedSelectionColor = ShouldUseRedOviaSelectionBox(db, tr, point1, point2);
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
            box.Color = usedRedSelectionColor
                ? Color.FromRgb(255, 0, 0)
                : Color.FromRgb(255, 255, 0);
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

            verticalXs = LimitGridVerticalCoordinatesToSelectedTable(
                verticalXs,
                selectedMinPoint,
                selectedMaxPoint,
                mergeTolerance
            );

            /*
             * OVIA 2026-08-07 _06 - 선택 표 X 소유권으로 수평 행 경계 재검증:
             * 분석창은 헤더 탐색을 위해 좌우로 확장되므로, 인접 표·형상 범례·요약표의 수평선과
             * 현재 SHAPE 셀 내부 철근선이 우연히 같은 Y에 놓이면 전체 분석폭의 50% 이상을
             * 덮는 것처럼 합산될 수 있습니다. 이 가짜 수평축이 DATA 한 행을 둘로 나누면
             * 같은 번호/규격/길이/수량/중량이 두 행에 복제되고 형상 치수만 위·아래로 분리됩니다.
             *
             * 세로 GRID로 현재 표의 실제 좌우 X를 먼저 확정한 뒤, 그 범위 안에 실제로 존재하는
             * 수평 선분만 잘라 다시 행 경계를 계산합니다. 실제 표 행선은 여러 물리 컬럼을
             * 가로지르지만 형상 내부 선은 한 SHAPE 컬럼에만 머무르므로 행 경계로 승인되지 않습니다.
             */
            List<double> selectedTableHorizontalYs = ExtractSelectedTableHorizontalGridCoordinates(
                gridLines,
                verticalXs,
                axisTolerance,
                mergeTolerance
            );

            if (selectedTableHorizontalYs.Count >= 3)
            {
                horizontalYs = selectedTableHorizontalYs;
                diagnostic = AppendDiagnostic(
                    diagnostic,
                    "현재 선택 표의 물리 X 범위 안에서 수평 행 경계를 다시 검증했습니다."
                );
            }

            List<OviaTextRow> selectedTableTextRows = FilterGridTextRowsToSelectedTable(
                textRows,
                verticalXs,
                mergeTolerance
            );

            int i;

            if (verticalXs.Count < 3 || horizontalYs.Count < 3)
            {
                diagnostic = "표 경계선 부족: 세로선 " + verticalXs.Count.ToString() + "개, 가로선 " + horizontalYs.Count.ToString() + "개";
                return result;
            }

            /*
             * OVIA 2026-07-20 인접 표 캐시 오적용 차단:
             * minPoint/maxPoint는 헤더 탐색을 위해 좌우로 확장된 분석창이므로 인접 표까지 포함할
             * 수 있습니다. 이 확장 좌표로 캐시 겹침을 계산하면 왼쪽 표의 컬럼 스키마가 오른쪽
             * 52~77 표에 재사용되어 번호=수량, 형상=총길이/중량으로 밀릴 수 있습니다.
             * 캐시 재사용 여부는 사용자가 실제 선택한 표 범위와 그 범위에서 검출한 세로선으로만
             * 판정합니다. 중간 소계·끝 소계/총계는 이 X축 표 식별에 영향을 주지 않습니다.
             */
            bool selectionContainsSummaryRows = ContainsSummaryTextRows(selectedTableTextRows);
            bool reusedCachedGridSchema = !selectionContainsSummaryRows
                && TryApplyCachedGridSchema(selectedMinPoint, selectedMaxPoint, ref verticalXs);
            string[,] cellTexts = BuildGridCellTextMatrix(selectedTableTextRows, verticalXs, horizontalYs, mergeTolerance);

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
                NormalizeGridColumnsByHeader(selectedTableTextRows, ref verticalXs, horizontalYs, mergeTolerance);
            }

            cellTexts = BuildGridCellTextMatrix(selectedTableTextRows, verticalXs, horizontalYs, mergeTolerance);

            if (cellTexts == null)
            {
                diagnostic = "셀 텍스트 매트릭스를 만들지 못했습니다.";
                return result;
            }

            int headerRowIndex = DetectGridHeaderRow(cellTexts, verticalXs, horizontalYs);

            /*
             * OVIA 2026-07-15 범용 표 구조 보정:
             * 데이터 행 전체의 세로선 커버리지로 컬럼을 찾으면, 철근형상 안에서 모든 행에
             * 반복되는 수직선이 실제 테이블 세로선처럼 누적될 수 있습니다. 반대로 일부 도면은
             * 형상 컬럼과 인접 컬럼 사이의 실제 경계선이 전체 높이 커버리지 조건에서 빠져
             * 형상 셀이 번호~수량 영역까지 넓어질 수 있습니다.
             *
             * 헤더 행에는 철근형상 객체가 존재하지 않고 실제 테이블 경계선만 통과하므로,
             * 헤더가 확인된 첫 추출에서는 헤더 밴드를 실제로 관통하는 세로선만 다시 수집하여
             * 물리 컬럼 경계를 확정합니다. 이후 2차·3차·N차 추출은 이 확정 스키마를 재사용합니다.
             */
            if (!reusedCachedGridSchema && headerRowIndex >= 0)
            {
                List<double> headerBandVerticalXs;
                int refinedHeaderRowIndex;

                if (TryRefineGridVerticalXsFromHeaderBand(
                    selectedTableTextRows,
                    gridLines,
                    horizontalYs,
                    headerRowIndex,
                    axisTolerance,
                    mergeTolerance,
                    selectedMinPoint,
                    selectedMaxPoint,
                    out headerBandVerticalXs,
                    out refinedHeaderRowIndex))
                {
                    verticalXs = headerBandVerticalXs;
                    cellTexts = BuildGridCellTextMatrix(selectedTableTextRows, verticalXs, horizontalYs, mergeTolerance);
                    headerRowIndex = refinedHeaderRowIndex;
                    diagnostic = AppendDiagnostic(diagnostic, "헤더 행 관통 세로선으로 실제 표 컬럼 경계를 확정했습니다.");
                }
            }

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

            /*
             * OVIA 2026-07-17 연속 선택 형상 셀 경계 고정:
             * 캐시된 컬럼은 최초 정상 추출에서 헤더 문자와 실제 표 선을 함께 검증한 최종 경계입니다.
             * 2차·3차 추출에서 이를 SourceColumnIndex 기반 물리 셀로 다시 덮어쓰면,
             * 병합 헤더 또는 형번/형상 분리 표에서 철근형상 셀이 인접 길이·수량 컬럼까지 넓어질 수 있습니다.
             * 그 결과 실제 철근선은 제거되고 표 세로선만 형상으로 남거나 CAD_EMPTY가 발생했습니다.
             *
             * 따라서 캐시 재사용 시에는 저장된 최종 LeftX/RightX를 그대로 사용합니다.
             * 새 표 스키마를 분석하는 최초 추출에서만 물리 경계 복원과 헤더 기반 clamp를 수행합니다.
             */
            if (!reusedCachedGridSchema)
            {
                ApplyGridHeaderColumnBoundsFromLines(columns, verticalXs);
                RestoreGridShapePhysicalBounds(columns);

                /*
                 * OVIA 2026-05-27 보정:
                 * 표 선 검출에 철근형상 내부 선/치수선이 섞이면 일부 행에서
                 * 철근형상 셀의 치수값이 길이/수량/총길이/중량 칸으로 들어갈 수 있습니다.
                 * 헤더 문자의 실제 X 위치를 기준으로 안전한 컬럼 범위를 다시 보정해서,
                 * 형상 칸의 값은 형상 전용 데이터로만 사용되도록 합니다.
                 */
                ApplyTextHeaderColumnBoundsIfAvailable(selectedTableTextRows, columns);
                RestoreGridShapePhysicalBounds(columns);
                ClampGridShapeBoundsFromTextHeader(selectedTableTextRows, columns);
                /*
                 * 소계·합계·총계 병합행이 포함된 선택은 행 구간마다 세로선 개수가 달라질 수 있습니다.
                 * 이때 데이터 패턴 fallback으로 만든 임시 컬럼을 정상 스키마처럼 캐시하면, 같은 표의
                 * 다음 일괄 선택에서 번호가 수량으로, 형상 셀이 총길이·중량으로 재사용될 수 있습니다.
                 * 요약행 포함 선택은 현재 추출에만 사용하고 다음 선택용 스키마로 저장하지 않습니다.
                 */
                if (!selectionContainsSummaryRows && headerRowIndex >= 0)
                {
                    CacheGridSchemaIfUsable(selectedMinPoint, selectedMaxPoint, verticalXs, columns);
                }
                else if (!selectionContainsSummaryRows)
                {
                    diagnostic = AppendDiagnostic(
                        diagnostic,
                        "문자 헤더로 검증되지 않은 데이터 패턴 컬럼은 다음 선택용 스키마로 저장하지 않았습니다."
                    );
                }
            }
            else
            {
                diagnostic = AppendDiagnostic(diagnostic, "최초 정상 추출의 철근형상 셀 경계를 그대로 유지했습니다.");
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
                row.RowCenterY = rowCenterY;
                row.RowBandHeight = Math.Abs(rowTopY - rowBottomY);

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
                        row.ShapeCellBoundsSource = "GRID";
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
                RecoverGridRowValuesByHeaderBounds(selectedTableTextRows, row, columns, rowTopY, rowBottomY, mergeTolerance);

                /*
                 * OVIA 2026-05-27 재보정:
                 * 철근형상 내부 숫자는 데이터로 쓰지 않되, 규격(SHD10 등) 오른쪽에 있는 실제 산정값은
                 * 반드시 복구해야 합니다. 셀/선 기반 범위가 조금 어긋나도 같은 행의 원문을 X순서로 모아
                 * 규격 뒤 마지막 숫자들을 길이/수량/총길이/중량으로 재확인합니다.
                 */
                if (row.RowType == "DATA")
                {
                    string rowBandText = JoinGridRowBandTextInSelectedRange(selectedTableTextRows, rowTopY, rowBottomY, selectedMinPoint.X, selectedMaxPoint.X, mergeTolerance);

                    if (rowBandText != "")
                    {
                        row.RawText = rowBandText;
                        SupplementGridDataFromSpecAnchoredText(rowBandText, row, columns);
                        ApplyGridWeightAndNoteCorrection(selectedTableTextRows, row, columns, rowTopY, rowBottomY, mergeTolerance);
                    }
                }

                string recoveredMarkNo = RecoverGridMarkNo(selectedTableTextRows, rowTopY, rowBottomY, columns, verticalXs, mergeTolerance, rowNo);

                if (recoveredMarkNo != "")
                {
                    row.MarkNo = recoveredMarkNo;
                    row.BarNo = recoveredMarkNo;
                }
                else if (row.RowType == "DATA")
                {
                    // 물리 번호 열에서 확인하지 못한 정수는 형상 치수 또는 길이일 수 있으므로 보존하지 않습니다.
                    row.MarkNo = "";
                    row.BarNo = "";
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

        private bool TryRefineGridVerticalXsFromHeaderBand(
            List<OviaTextRow> textRows,
            List<OviaGridLineSegment> gridLines,
            List<double> horizontalYs,
            int headerRowIndex,
            double axisTolerance,
            double mergeTolerance,
            Point3d minPoint,
            Point3d maxPoint,
            out List<double> refinedVerticalXs,
            out int refinedHeaderRowIndex)
        {
            refinedVerticalXs = new List<double>();
            refinedHeaderRowIndex = headerRowIndex;

            if (textRows == null || gridLines == null || horizontalYs == null)
            {
                return false;
            }

            if (headerRowIndex < 0 || headerRowIndex + 1 >= horizontalYs.Count)
            {
                return false;
            }

            double headerTopY = Math.Max(horizontalYs[headerRowIndex], horizontalYs[headerRowIndex + 1]);
            double headerBottomY = Math.Min(horizontalYs[headerRowIndex], horizontalYs[headerRowIndex + 1]);
            double headerHeight = Math.Max(headerTopY - headerBottomY, 0.0001);
            double tableMinX = Math.Min(minPoint.X, maxPoint.X);
            double tableMaxX = Math.Max(minPoint.X, maxPoint.X);
            double xMargin = Math.Max(mergeTolerance * 1.5, 0.5);
            double bandTolerance = Math.Max(mergeTolerance, headerHeight * 0.08);
            List<double> candidates = new List<double>();
            int i;

            for (i = 0; i < gridLines.Count; i++)
            {
                OviaGridLineSegment segment = gridLines[i];

                if (segment == null)
                {
                    continue;
                }

                double dx = Math.Abs(segment.X1 - segment.X2);
                double dy = Math.Abs(segment.Y1 - segment.Y2);

                if (dx > axisTolerance || dy <= axisTolerance)
                {
                    continue;
                }

                double segmentMinY = Math.Min(segment.Y1, segment.Y2);
                double segmentMaxY = Math.Max(segment.Y1, segment.Y2);
                double overlap = Math.Min(segmentMaxY, headerTopY) - Math.Max(segmentMinY, headerBottomY);
                bool crossesHeaderBand = segmentMinY <= headerBottomY + bandTolerance
                    && segmentMaxY >= headerTopY - bandTolerance;
                bool coversMostOfHeaderBand = overlap >= headerHeight * 0.78;

                if (!crossesHeaderBand && !coversMostOfHeaderBand)
                {
                    continue;
                }

                double x = (segment.X1 + segment.X2) / 2.0;

                if (x < tableMinX - xMargin || x > tableMaxX + xMargin)
                {
                    continue;
                }

                candidates.Add(x);
            }

            candidates = MergeGridCoordinates(candidates, mergeTolerance, true);

            if (candidates.Count < 4 || candidates.Count > 40)
            {
                return false;
            }

            string[,] refinedMatrix = BuildGridCellTextMatrix(textRows, candidates, horizontalYs, mergeTolerance);

            if (refinedMatrix == null)
            {
                return false;
            }

            int detectedHeaderRow = DetectGridHeaderRow(refinedMatrix, candidates, horizontalYs);

            if (detectedHeaderRow < 0)
            {
                return false;
            }

            List<OviaHeaderColumn> refinedColumns = BuildGridHeaderColumns(refinedMatrix, candidates, detectedHeaderRow);

            if (!HasRequiredGridExtractionHeaders(refinedColumns))
            {
                return false;
            }

            refinedVerticalXs = candidates;
            refinedHeaderRowIndex = detectedHeaderRow;
            return true;
        }

        private bool HasRequiredGridExtractionHeaders(List<OviaHeaderColumn> columns)
        {
            if (columns == null || columns.Count < 5)
            {
                return false;
            }

            return FindHeaderColumnByKey(columns, "MARK_NO") != null
                && FindHeaderColumnByKey(columns, "SHAPE") != null
                && FindHeaderColumnByKey(columns, "SPEC") != null
                && FindHeaderColumnByKey(columns, "LENGTH_MM") != null
                && FindHeaderColumnByKey(columns, "QUANTITY_EA") != null;
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

            if (shapeColumn == null || shapeColumn.RightX <= shapeColumn.LeftX)
            {
                return;
            }

            OviaHeaderColumn nearestLeft = null;
            OviaHeaderColumn nearestRight = null;
            int i;

            /*
             * OVIA 2026-07-17 형상 셀 경계 확정 규칙:
             * 형상 셀이 내부 수직선 때문에 여러 물리 셀로 쪼개졌을 때는 인접 데이터 컬럼 사이를
             * 하나의 형상 셀로 복원해야 합니다. 이전 구현은 현재 형상 bounds의 중심 X를 기준으로
             * 좌우 이웃을 찾았습니다. 형상 bounds가 이미 길이/수량/중량 쪽으로 잘못 확장된 경우
             * 그 중심도 오른쪽으로 이동하여, 잘못된 데이터 컬럼을 이웃으로 선택하는 문제가 있었습니다.
             *
             * 이제 X 중심이 아니라 최초 셀 매핑의 SourceColumnIndex 순서를 우선 사용합니다.
             * 표의 실제 컬럼 순서에서 SHAPE보다 왼쪽/오른쪽에 있는 가장 가까운 데이터 컬럼 경계만
             * 사용하므로, 번호|형상|규격 형식과 번호|규격|형번|형상 형식을 모두 처리합니다.
             */
            if (shapeColumn.SourceColumnIndex >= 0)
            {
                for (i = 0; i < columns.Count; i++)
                {
                    OviaHeaderColumn item = columns[i];

                    if (item == null || item == shapeColumn || item.RightX <= item.LeftX)
                    {
                        continue;
                    }

                    if (!IsGridShapeBoundaryColumnKey(item.StandardKey) || item.SourceColumnIndex < 0)
                    {
                        continue;
                    }

                    if (item.SourceColumnIndex < shapeColumn.SourceColumnIndex)
                    {
                        if (nearestLeft == null || item.SourceColumnIndex > nearestLeft.SourceColumnIndex)
                        {
                            nearestLeft = item;
                        }
                    }
                    else if (item.SourceColumnIndex > shapeColumn.SourceColumnIndex)
                    {
                        if (nearestRight == null || item.SourceColumnIndex < nearestRight.SourceColumnIndex)
                        {
                            nearestRight = item;
                        }
                    }
                }
            }

            /*
             * 문자 헤더에서 추가된 컬럼처럼 SourceColumnIndex가 없는 예외만 X 위치로 보완합니다.
             * 이 fallback도 형상 중심이 아니라 현재 형상 셀의 좌우 경계를 기준으로 찾습니다.
             */
            if (nearestLeft == null || nearestRight == null)
            {
                for (i = 0; i < columns.Count; i++)
                {
                    OviaHeaderColumn item = columns[i];

                    if (item == null || item == shapeColumn || item.RightX <= item.LeftX)
                    {
                        continue;
                    }

                    if (!IsGridShapeBoundaryColumnKey(item.StandardKey))
                    {
                        continue;
                    }

                    if (nearestLeft == null && item.RightX <= shapeColumn.LeftX + 0.0001)
                    {
                        if (nearestLeft == null || item.RightX > nearestLeft.RightX)
                        {
                            nearestLeft = item;
                        }
                    }

                    if (nearestRight == null && item.LeftX >= shapeColumn.RightX - 0.0001)
                    {
                        if (nearestRight == null || item.LeftX < nearestRight.LeftX)
                        {
                            nearestRight = item;
                        }
                    }
                }
            }

            double left = shapeColumn.LeftX;
            double right = shapeColumn.RightX;

            if (nearestLeft != null)
            {
                left = nearestLeft.RightX;
            }

            if (nearestRight != null)
            {
                right = nearestRight.LeftX;
            }

            if (right <= left)
            {
                return;
            }

            shapeColumn.LeftX = left;
            shapeColumn.RightX = right;
            shapeColumn.X = (left + right) / 2.0;
        }

        private bool IsGridShapeBoundaryColumnKey(string key)
        {
            if (key == null || key.Trim() == "" || key == "SHAPE")
            {
                return false;
            }

            return key == "MARK_NO"
                || key == "PART"
                || key == "SPEC"
                || key == "SHAPE_NO"
                || key == "IGNORE_SHAPE_NO"
                || key == "LENGTH_MM"
                || key == "QUANTITY_EA"
                || key == "TOTAL_LENGTH_M"
                || key == "TOTAL_WEIGHT"
                || key == "TOTAL_WEIGHT_KG"
                || key == "NOTE";
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

                if (source.HeaderTextVerified)
                {
                    target.HeaderTextVerified = true;
                }

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
                added.HeaderTextVerified = source.HeaderTextVerified;
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

        private void ClampGridShapeBoundsFromTextHeader(List<OviaTextRow> textRows, List<OviaHeaderColumn> columns)
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

            if (textHeaderMap == null || textHeaderMap.Columns == null)
            {
                return;
            }

            OviaHeaderColumn target = FindHeaderColumnByKey(columns, "SHAPE");
            OviaHeaderColumn source = FindHeaderColumnByKey(textHeaderMap.Columns, "SHAPE");

            if (target == null || source == null || source.RightX <= source.LeftX)
            {
                return;
            }

            double sourceWidth = source.RightX - source.LeftX;
            double padding = Math.Max(sourceWidth * 0.035, 0.05);
            double candidateLeft = source.LeftX - padding;
            double candidateRight = source.RightX + padding;

            /*
             * 헤더 문자의 좌우 인접 헤더 중심으로 만든 범위는 컬럼 순서가 달라도 안정적입니다.
             * 표 선 분석이 철근형상 내부 선을 컬럼 경계로 오인하거나 실제 경계를 놓쳤을 때도,
             * 형상 셀이 다른 데이터 헤더 중심을 넘어가지 않도록 최종 캡처 범위를 제한합니다.
             */
            int i;

            for (i = 0; i < textHeaderMap.Columns.Count; i++)
            {
                OviaHeaderColumn neighbor = textHeaderMap.Columns[i];

                if (neighbor == null || neighbor == source || neighbor.StandardKey == "SHAPE")
                {
                    continue;
                }

                if (!IsGridShapeBoundaryColumnKey(neighbor.StandardKey))
                {
                    continue;
                }

                if (neighbor.X < source.X)
                {
                    double boundary = (neighbor.X + source.X) / 2.0;

                    if (candidateLeft < boundary)
                    {
                        candidateLeft = boundary;
                    }
                }
                else if (neighbor.X > source.X)
                {
                    double boundary = (neighbor.X + source.X) / 2.0;

                    if (candidateRight > boundary)
                    {
                        candidateRight = boundary;
                    }
                }
            }

            if (candidateRight <= candidateLeft)
            {
                return;
            }

            target.LeftX = candidateLeft;
            target.RightX = candidateRight;
            target.X = (candidateLeft + candidateRight) / 2.0;
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
            row.ShapeCellBoundsSource = "GRID";
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

            /*
             * OVIA 2026-07-22 _06 인접 행 형상문자 혼입 차단:
             * 셀 매트릭스에서 임시로 채워진 ShapeText에는 인접 행 경계 근처의 치수문자가
             * 들어갈 수 있습니다. 예: 9번의 하단 치수 150/400이 10번의 4300과 합쳐져
             * "150 A E 400 4300 A"가 되면, 후단 완전성 검증이 기대치수 3개/보존치수 1개로
             * 오판하여 정상 일자형 형상 JSON 생성을 차단합니다.
             *
             * 따라서 물리 행 경계로 다시 읽은 shapeText를 빈 값까지 포함해 항상 최종값으로
             * 덮어씁니다. 형상 셀에 문자가 없는 도형도 이전 행의 문자를 물려받지 않습니다.
             */
            if (shapeColumn != null)
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

                    if (HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
                    {
                        row.Note = NormalizeGridNoteText(
                            GetGridNoteTextByPhysicalOwnership(
                                textRows,
                                columns,
                                rowTopY,
                                rowBottomY,
                                tolerance,
                                shapeColumn,
                                row.TotalWeight
                            )
                        );
                    }
                    else
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

            if (HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
            {
                row.Note = NormalizeGridNoteText(
                    GetGridNoteTextByPhysicalOwnership(
                        textRows,
                        columns,
                        rowTopY,
                        rowBottomY,
                        tolerance,
                        shapeColumn,
                        row.TotalWeight
                    )
                );
            }
            else
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
            double physicalTopY = Math.Max(rowTopY, rowBottomY);
            double physicalBottomY = Math.Min(rowTopY, rowBottomY);
            bool strictPhysicalRow = targetIsShape || String.Equals(key, "NOTE", StringComparison.OrdinalIgnoreCase);
            double yMargin = strictPhysicalRow ? 0.0 : Math.Max(tolerance * 2.5, 0.5);
            double xMargin = Math.Max(tolerance * 0.6, 0.15);
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                /*
                 * 철근형상 문자는 반드시 현재 물리 DATA 행 내부에서만 수집합니다.
                 * 일반 데이터값은 CAD 문자 정렬 오차를 흡수하기 위해 기존 여유범위를 유지하지만,
                 * 형상 열에 같은 여유를 적용하면 위·아래 행의 다단 치수가 동시에 들어옵니다.
                 * 행 경계는 실제 테이블 수평선으로 확정되어 있으므로 SHAPE만 무여유 구간을 사용합니다.
                 */
                if (strictPhysicalRow)
                {
                    if (text.Y < physicalBottomY || text.Y > physicalTopY)
                    {
                        continue;
                    }
                }
                else if (text.Y < physicalBottomY - yMargin || text.Y > physicalTopY + yMargin)
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

        private int RevalidateDuplicatedWeightNotesByPhysicalOwnership(
            List<OviaBarTableRow> rows,
            List<OviaTextRow> textRows,
            List<OviaHeaderColumn> columns)
        {
            if (rows == null || rows.Count == 0 || textRows == null || columns == null)
            {
                return 0;
            }

            OviaHeaderColumn shapeColumn = FindHeaderColumnByKey(columns, "SHAPE");
            double averageTextHeight = GetAverageTextHeight(textRows);
            double tolerance = Math.Max(averageTextHeight * 0.35, 0.05);
            int correctedCount = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaBarTableRow row = rows[i];

                if (row == null
                    || !String.Equals(row.RowType, "DATA", StringComparison.OrdinalIgnoreCase)
                    || !AreEquivalentGridNumericTexts(row.Note, row.TotalWeight))
                {
                    continue;
                }

                double rowTopY;
                double rowBottomY;

                if (row.HasShapeCellBounds())
                {
                    rowTopY = Math.Max(row.ShapeCellMinY, row.ShapeCellMaxY);
                    rowBottomY = Math.Min(row.ShapeCellMinY, row.ShapeCellMaxY);
                }
                else
                {
                    double halfHeight = row.RowBandHeight > 0.0001
                        ? row.RowBandHeight / 2.0
                        : Math.Max(averageTextHeight, 0.5);
                    rowTopY = row.RowCenterY + halfHeight;
                    rowBottomY = row.RowCenterY - halfHeight;
                }

                string physicalNote = NormalizeGridNoteText(
                    GetGridNoteTextByPhysicalOwnership(
                        textRows,
                        columns,
                        rowTopY,
                        rowBottomY,
                        tolerance,
                        shapeColumn,
                        row.TotalWeight
                    )
                );

                if (!String.Equals(row.Note, physicalNote, StringComparison.Ordinal))
                {
                    row.Note = physicalNote;
                    correctedCount++;
                }
            }

            return correctedCount;
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

            if (HasUsableNoteColumn(textRows, columns, rowTopY, rowBottomY, tolerance))
            {
                row.Note = NormalizeGridNoteText(
                    GetGridNoteTextByPhysicalOwnership(
                        textRows,
                        columns,
                        rowTopY,
                        rowBottomY,
                        tolerance,
                        shapeColumn,
                        row.TotalWeight
                    )
                );
            }
            else
            {
                row.Note = "";
            }
        }

        /*
         * OVIA 2026-08-07 _02 - NOTE 셀의 단일 물리 소유권:
         * 실제 비고 헤더가 확인되어도 NOTE 열의 X 여유범위가 인접 중량 셀까지 걸리면,
         * 같은 CAD 중량 Text 객체가 TOTAL_WEIGHT와 NOTE에 동시에 배정될 수 있습니다.
         * NOTE는 Y뿐 아니라 X도 물리 셀 내부만 허용하고, 동일 Text가 중량 열에도 소속되면
         * 비고 후보에서 제외합니다. 실제 숫자 비고가 중량과 우연히 같더라도 별도 NOTE 셀
         * 객체이면 유지하므로 값 동일 비교만으로 삭제하지 않습니다.
         */
        private string GetGridNoteTextByPhysicalOwnership(
            List<OviaTextRow> textRows,
            List<OviaHeaderColumn> columns,
            double rowTopY,
            double rowBottomY,
            double tolerance,
            OviaHeaderColumn shapeColumn,
            string currentWeight)
        {
            if (textRows == null || columns == null)
            {
                return "";
            }

            OviaHeaderColumn noteColumn = FindMatchingHeaderColumnForBounds(columns, "NOTE");

            if (noteColumn == null || noteColumn.RightX <= noteColumn.LeftX)
            {
                return "";
            }

            OviaHeaderColumn weightColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT");
            OviaHeaderColumn kgColumn = FindMatchingHeaderColumnForBounds(columns, "TOTAL_WEIGHT_KG");
            double physicalTopY = Math.Max(rowTopY, rowBottomY);
            double physicalBottomY = Math.Min(rowTopY, rowBottomY);
            double noteWidth = Math.Abs(noteColumn.RightX - noteColumn.LeftX);
            double ownershipEpsilon = Math.Max(Math.Min(noteWidth * 0.0025, 0.03), 0.0001);
            double weightNoteDividerX = noteColumn.LeftX;

            if (weightColumn != null && weightColumn.X < noteColumn.X)
            {
                weightNoteDividerX = (weightColumn.X + noteColumn.X) / 2.0;
            }

            if (kgColumn != null && kgColumn.X < noteColumn.X)
            {
                double kgDividerX = (kgColumn.X + noteColumn.X) / 2.0;

                if (kgDividerX > weightNoteDividerX)
                {
                    weightNoteDividerX = kgDividerX;
                }
            }
            List<OviaTextRow> candidates = new List<OviaTextRow>();
            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow text = textRows[i];

                if (text == null)
                {
                    continue;
                }

                if (text.Y < physicalBottomY || text.Y > physicalTopY)
                {
                    continue;
                }

                string value = CleanCellText(text.TextValue);

                if (value == "" || IsHeaderRow(value) || IsSummaryText(value))
                {
                    continue;
                }

                /*
                 * NOTE는 인접 열 문자 유입을 막기 위해 일반 데이터 열의 X margin을 사용하지 않습니다.
                 * 텍스트 기준점은 GeometricExtents 중심이므로 실제 NOTE 셀 문자라면 경계 안쪽에 존재합니다.
                 */
                if (text.X <= noteColumn.LeftX + ownershipEpsilon
                    || text.X >= noteColumn.RightX - ownershipEpsilon)
                {
                    continue;
                }

                if (shapeColumn != null
                    && IsXInsideHeaderColumn(text.X, shapeColumn, Math.Max(tolerance, 0.5)))
                {
                    continue;
                }

                /* 동일 CAD Text 객체가 중량 열의 허용범위에도 속하면 NOTE 소유가 아닙니다. */
                if (IsTextRowOwnedByGridDataColumn(
                        text,
                        weightColumn,
                        physicalTopY,
                        physicalBottomY,
                        tolerance)
                    || IsTextRowOwnedByGridDataColumn(
                        text,
                        kgColumn,
                        physicalTopY,
                        physicalBottomY,
                        tolerance))
                {
                    continue;
                }

                /*
                 * 총중량과 같은 숫자가 NOTE 범위에도 들어온 경우 헤더 중심의 중간선을 기준으로
                 * 단일 소유 열을 결정합니다. 실제 NOTE 셀에 별도로 작성된 같은 숫자는 NOTE 중심 쪽에
                 * 있으므로 유지되고, 중량 셀의 동일 CAD 문자가 경계 오차로 중복 배정된 경우만 제외됩니다.
                 */
                if (AreEquivalentGridNumericTexts(value, currentWeight)
                    && text.X <= weightNoteDividerX + ownershipEpsilon
                    && !HasSeparateGridWeightTextObject(
                        textRows,
                        text,
                        weightColumn,
                        kgColumn,
                        physicalTopY,
                        physicalBottomY,
                        tolerance,
                        currentWeight))
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

        private bool HasSeparateGridWeightTextObject(
            List<OviaTextRow> textRows,
            OviaTextRow noteCandidate,
            OviaHeaderColumn weightColumn,
            OviaHeaderColumn kgColumn,
            double physicalTopY,
            double physicalBottomY,
            double tolerance,
            string currentWeight)
        {
            if (textRows == null || noteCandidate == null || currentWeight == null || currentWeight.Trim() == "")
            {
                return false;
            }

            int i;

            for (i = 0; i < textRows.Count; i++)
            {
                OviaTextRow candidate = textRows[i];

                if (candidate == null
                    || AreSameGridTextObject(candidate, noteCandidate, tolerance)
                    || !AreEquivalentGridNumericTexts(candidate.TextValue, currentWeight))
                {
                    continue;
                }

                if (IsTextRowOwnedByGridDataColumn(
                        candidate,
                        weightColumn,
                        physicalTopY,
                        physicalBottomY,
                        tolerance)
                    || IsTextRowOwnedByGridDataColumn(
                        candidate,
                        kgColumn,
                        physicalTopY,
                        physicalBottomY,
                        tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AreSameGridTextObject(OviaTextRow left, OviaTextRow right, double tolerance)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.Handle != null && right.Handle != null
                && left.Handle.Trim() != "" && right.Handle.Trim() != ""
                && String.Equals(left.Handle.Trim(), right.Handle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            double positionTolerance = Math.Max(tolerance * 0.15, 0.001);

            return Math.Abs(left.X - right.X) <= positionTolerance
                && Math.Abs(left.Y - right.Y) <= positionTolerance
                && String.Equals(
                    CleanCellText(left.TextValue),
                    CleanCellText(right.TextValue),
                    StringComparison.Ordinal);
        }

        private bool IsTextRowOwnedByGridDataColumn(
            OviaTextRow text,
            OviaHeaderColumn column,
            double physicalTopY,
            double physicalBottomY,
            double tolerance)
        {
            if (text == null || column == null || column.RightX <= column.LeftX)
            {
                return false;
            }

            double yMargin = Math.Max(tolerance * 2.5, 0.5);
            double xMargin = Math.Max(tolerance * 0.6, 0.15);

            if (text.Y < physicalBottomY - yMargin || text.Y > physicalTopY + yMargin)
            {
                return false;
            }

            return IsXInsideHeaderColumn(text.X, column, xMargin);
        }

        private bool AreEquivalentGridNumericTexts(string left, string right)
        {
            decimal leftNumber;
            decimal rightNumber;

            if (!TryParseDecimalText(left, out leftNumber)
                || !TryParseDecimalText(right, out rightNumber))
            {
                return false;
            }

            return leftNumber == rightNumber;
        }

        private string NormalizeGridNoteText(string text)
        {
            text = CleanCellText(text);

            if (text == "" || IsHeaderRow(text) || IsSummaryText(text))
            {
                return "";
            }

            return text.Trim();
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

            /*
             * 2026-08-06 _02 / 2026-08-07 _01 비고 열 유효성:
             * 기존 코드는 현재 DATA 행에서 위쪽 약 4개 행 범위 안에 "비고" 헤더가 다시 보여야만
             * 비고 열을 유효하다고 판정했습니다. 표 아래쪽의 8번 이후 행은 헤더와 멀어져
             * 실제 "시공용" 문자를 읽고도 마지막 단계에서 row.Note를 비워 버렸습니다.
             *
             * 비고 열의 소유권은 행마다 재판정하지 않고, 실제 표 헤더 문자에서 확정된 NOTE 열 전체에
             * 적용합니다. 데이터 패턴 fallback이 임시로 만든 NOTE 열은 표시명이 "비고"여도 실제 헤더로
             * 간주하지 않으며, 선택영역 전체의 같은 X열에서 비고/NOTE/REMARK 문자를 확인한 경우에만 사용합니다.
             * 이 구분이 없으면 빈 비고 열이 있는 인접 표에서 중량 물리 셀이 NOTE로 추론되어 같은 중량이
             * 비고에 한 번 더 복제될 수 있습니다.
             */
            if (!noteColumn.HeaderTextVerified
                && !HasNoteTextNearColumn(textRows, noteColumn, rowTopY, rowBottomY, tolerance))
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

            /*
             * NOTE 열 헤더는 표 전체에서 한 번만 존재합니다. 현재 DATA 행과 헤더의 거리를 제한하면
             * 긴 표의 아래쪽 행에서 정상 비고가 삭제되므로 Y 범위를 두지 않고 선택영역 전체를 검사합니다.
             */
            double xMargin = Math.Max((column.RightX - column.LeftX) * 0.20, Math.Max(tolerance * 1.5, 0.35));
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

            if (key == "LENGTH_MM" || key == "QUANTITY_EA" || key == "TOTAL_LENGTH_M" || key == "TOTAL_WEIGHT" || key == "TOTAL_WEIGHT_KG")
            {
                string joined = PickJoinedThousandsCandidate(numbers, key);

                if (joined != "")
                {
                    return joined;
                }
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

            text = NormalizeSeparatedNumericPunctuation(text);

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

        private string PickJoinedThousandsCandidate(List<string> numbers, string key)
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

                if (Regex.IsMatch(left, @"^-?\d{1,3}$") && Regex.IsMatch(right, @"^\d{3}(?:\.\d+)?$"))
                {
                    if (key == "TOTAL_WEIGHT" && Regex.IsMatch(right, @"^\d{3}$"))
                    {
                        // Ton 값은 보통 소수점 셋째 자리까지 표기됩니다.
                        // 별도 문자 객체인 점이 누락되어 "0 020"으로 결합된 경우 0.020으로 복원합니다.
                        return left + "." + right;
                    }

                    return left + right;
                }
            }

            return "";
        }

        private string NormalizeSeparatedNumericPunctuation(string text)
        {
            if (text == null)
            {
                return "";
            }

            /*
             * CAD 도면에 따라 1,000 또는 35.20의 쉼표/소수점이 숫자와 별도 문자 객체로
             * 저장되어 셀 결합 결과가 "1 , 000", "35 . 20"처럼 들어올 수 있습니다.
             * 숫자 사이의 구두점 주변 공백만 제거하여 원래 수치 의미를 복원합니다.
             */
            return Regex.Replace(text, @"(?<=\d)\s*([,.])\s*(?=\d)", "$1");
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
                    "Line",
                    GetStableCadEntityHandle(entity)
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
                        "Polyline",
                        GetStableCadEntityHandle(entity)
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
                        "Polyline",
                        GetStableCadEntityHandle(entity)
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
                        AddGridLineSegmentCandidate(
                            line.StartPoint,
                            line.EndPoint,
                            segments,
                            minPoint,
                            maxPoint,
                            "ExplodedLine",
                            GetStableCadEntityHandle(explodedEntity)
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
                            AddGridLineSegmentCandidate(
                                polyline.GetPoint3dAt(i),
                                polyline.GetPoint3dAt(i + 1),
                                segments,
                                minPoint,
                                maxPoint,
                                "ExplodedPolyline",
                                GetStableCadEntityHandle(explodedEntity)
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
                                "ExplodedPolyline",
                                GetStableCadEntityHandle(explodedEntity)
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
            string sourceType,
            string sourceHandle
        )
        {
            if (segments == null)
            {
                return;
            }

            double windowMinX = Math.Min(minPoint.X, maxPoint.X);
            double windowMaxX = Math.Max(minPoint.X, maxPoint.X);
            double windowMinY = Math.Min(minPoint.Y, maxPoint.Y);
            double windowMaxY = Math.Max(minPoint.Y, maxPoint.Y);
            double segmentMinX = Math.Min(point1.X, point2.X);
            double segmentMaxX = Math.Max(point1.X, point2.X);
            double segmentMinY = Math.Min(point1.Y, point2.Y);
            double segmentMaxY = Math.Max(point1.Y, point2.Y);

            /*
             * 긴 표 경계선은 양 끝점이 분석 창 밖에 있어도 창을 가로지를 수 있습니다.
             * 끝점만 검사하면 실제 컬럼 경계가 누락되어 형상 셀 범위가 넓어지므로,
             * 선분 bounding box와 분석 창의 교차 여부로 수집합니다.
             */
            if (segmentMaxX < windowMinX || segmentMinX > windowMaxX
                || segmentMaxY < windowMinY || segmentMinY > windowMaxY)
            {
                return;
            }

            OviaGridLineSegment segment = new OviaGridLineSegment();
            segment.X1 = point1.X;
            segment.Y1 = point1.Y;
            segment.X2 = point2.X;
            segment.Y2 = point2.Y;
            segment.SourceType = sourceType == null ? "" : sourceType;
            segment.SourceHandle = sourceHandle == null ? "" : sourceHandle;
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

        private List<double> ExtractSelectedTableHorizontalGridCoordinates(
            List<OviaGridLineSegment> segments,
            List<double> verticalXs,
            double axisTolerance,
            double mergeTolerance)
        {
            List<double> result = new List<double>();

            if (segments == null || segments.Count == 0
                || verticalXs == null || verticalXs.Count < 2)
            {
                return result;
            }

            double tableLeftX = verticalXs[0];
            double tableRightX = verticalXs[verticalXs.Count - 1];

            if (tableLeftX > tableRightX)
            {
                double swap = tableLeftX;
                tableLeftX = tableRightX;
                tableRightX = swap;
            }

            double tableWidth = tableRightX - tableLeftX;

            if (tableWidth <= 0.0001)
            {
                return result;
            }

            double minimumSegmentLength = Math.Max(tableWidth * 0.010, 0.25);
            List<OviaGridAxisSegment> candidates = new List<OviaGridAxisSegment>();
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

                if (dy > axisTolerance || dx < minimumSegmentLength)
                {
                    continue;
                }

                double clippedStart = Math.Max(
                    tableLeftX,
                    Math.Min(segment.X1, segment.X2)
                );
                double clippedEnd = Math.Min(
                    tableRightX,
                    Math.Max(segment.X1, segment.X2)
                );

                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                OviaGridAxisSegment candidate = new OviaGridAxisSegment();
                candidate.Coordinate = (segment.Y1 + segment.Y2) / 2.0;
                candidate.Start = clippedStart;
                candidate.End = clippedEnd;
                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                return result;
            }

            candidates.Sort(delegate (OviaGridAxisSegment left, OviaGridAxisSegment right)
            {
                return left.Coordinate.CompareTo(right.Coordinate);
            });

            int candidateIndex = 0;
            int columnCount = verticalXs.Count - 1;
            int minimumTouchedColumns = Math.Max(
                2,
                (int)Math.Ceiling(columnCount * 0.50)
            );
            double minimumCoverage = tableWidth * 0.50;
            double edgeTolerance = Math.Max(tableWidth * 0.035, mergeTolerance * 2.0);

            while (candidateIndex < candidates.Count)
            {
                List<OviaGridAxisSegment> cluster = new List<OviaGridAxisSegment>();
                double baseCoordinate = candidates[candidateIndex].Coordinate;

                while (candidateIndex < candidates.Count
                    && Math.Abs(candidates[candidateIndex].Coordinate - baseCoordinate) <= mergeTolerance)
                {
                    cluster.Add(candidates[candidateIndex]);
                    candidateIndex++;
                }

                if (cluster.Count == 0)
                {
                    continue;
                }

                double coveredLength = GetMergedIntervalLength(
                    new List<OviaGridAxisSegment>(cluster),
                    mergeTolerance
                );

                if (coveredLength < minimumCoverage)
                {
                    continue;
                }

                double clusterMinX = Double.MaxValue;
                double clusterMaxX = Double.MinValue;
                double coordinateSum = 0.0;

                for (i = 0; i < cluster.Count; i++)
                {
                    coordinateSum += cluster[i].Coordinate;

                    if (cluster[i].Start < clusterMinX)
                    {
                        clusterMinX = cluster[i].Start;
                    }

                    if (cluster[i].End > clusterMaxX)
                    {
                        clusterMaxX = cluster[i].End;
                    }
                }

                bool touchesBothOuterEdges = clusterMinX <= tableLeftX + edgeTolerance
                    && clusterMaxX >= tableRightX - edgeTolerance;
                int touchedColumnCount = CountHorizontalGridClusterTouchedColumns(
                    cluster,
                    verticalXs,
                    mergeTolerance
                );

                if (!touchesBothOuterEdges && touchedColumnCount < minimumTouchedColumns)
                {
                    continue;
                }

                result.Add(coordinateSum / (double)cluster.Count);
            }

            result = MergeGridCoordinates(result, mergeTolerance, false);
            return result;
        }

        private int CountHorizontalGridClusterTouchedColumns(
            List<OviaGridAxisSegment> cluster,
            List<double> verticalXs,
            double tolerance)
        {
            if (cluster == null || cluster.Count == 0
                || verticalXs == null || verticalXs.Count < 2)
            {
                return 0;
            }

            int touchedCount = 0;
            int columnIndex;

            for (columnIndex = 0; columnIndex < verticalXs.Count - 1; columnIndex++)
            {
                double columnLeft = Math.Min(verticalXs[columnIndex], verticalXs[columnIndex + 1]);
                double columnRight = Math.Max(verticalXs[columnIndex], verticalXs[columnIndex + 1]);
                double columnWidth = columnRight - columnLeft;

                if (columnWidth <= 0.0001)
                {
                    continue;
                }

                double requiredOverlap = Math.Max(
                    Math.Min(columnWidth * 0.25, columnWidth),
                    tolerance * 0.50
                );
                bool touched = false;
                int clusterIndex;

                for (clusterIndex = 0; clusterIndex < cluster.Count; clusterIndex++)
                {
                    double overlap = Math.Min(columnRight, cluster[clusterIndex].End)
                        - Math.Max(columnLeft, cluster[clusterIndex].Start);

                    if (overlap >= requiredOverlap)
                    {
                        touched = true;
                        break;
                    }
                }

                if (touched)
                {
                    touchedCount++;
                }
            }

            return touchedCount;
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
            if (!IsPositiveRebarMarkNoText(first))
            {
                return "";
            }

            return first;
        }

        private bool IsPositiveRebarMarkNoText(string value)
        {
            if (value == null)
            {
                return false;
            }

            Match match = Regex.Match(value.Trim(), @"^([0-9]{1,6})[A-Za-z]?$", RegexOptions.IgnoreCase);
            int number;

            return match.Success
                && Int32.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                && number > 0;
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

            double xMargin = Math.Min(width * 0.04, Math.Max(tolerance * 0.15, 0.05));
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

                string candidate = value.Trim();

                if (!IsPositiveRebarMarkNoText(candidate))
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
                column.HeaderTextVerified = true;
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

        private bool ContainsSummaryTextRows(List<OviaTextRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            int i;

            for (i = 0; i < rows.Count; i++)
            {
                OviaTextRow row = rows[i];

                if (row != null && IsSummaryText(CleanCellText(row.TextValue)))
                {
                    return true;
                }
            }

            return false;
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

            if (!IsCadShapeEntityVisible(tr, entity))
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
                BlockTableRecord blockRecord = tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;
                bool preferEvaluatedDisplay = IsCadShapeEvaluatedBlock(blockReference, blockRecord);
                int blockRowStartCount = rows.Count;

                /*
                 * 형상 셀 원문을 만드는 일반 TEXT 스캐너도 CAD 형상 JSON 수집기와 동일한
                 * 블록 우선순위를 사용해야 합니다. 이전에는 AttributeCollection, 원시
                 * BlockTableRecord, Explode 결과를 모두 합쳐 OVIA_형상원본 자체에 숨김 상태의
                 * 200/400/500이 중복 기록됐고, 후단 기대 개수 필터가 이를 정상 반복 치수로
                 * 오인했습니다.
                 */
                if (preferEvaluatedDisplay)
                {
                    CollectTextRowsFromExplodedBlock(
                        tr,
                        blockReference,
                        rows,
                        minPoint,
                        maxPoint,
                        depth + 1
                    );

                    if (rows.Count == blockRowStartCount)
                    {
                        CollectTextRowsFromBlockAttributes(
                            tr,
                            blockReference,
                            transform,
                            rows,
                            minPoint,
                            maxPoint,
                            depth
                        );
                        CollectTextRowsFromBlockRecord(
                            tr,
                            blockReference,
                            blockRecord,
                            transform,
                            rows,
                            minPoint,
                            maxPoint,
                            depth
                        );
                    }
                }
                else
                {
                    CollectTextRowsFromBlockAttributes(
                        tr,
                        blockReference,
                        transform,
                        rows,
                        minPoint,
                        maxPoint,
                        depth
                    );
                    CollectTextRowsFromBlockRecord(
                        tr,
                        blockReference,
                        blockRecord,
                        transform,
                        rows,
                        minPoint,
                        maxPoint,
                        depth
                    );

                    if (rows.Count == blockRowStartCount)
                    {
                        CollectTextRowsFromExplodedBlock(
                            tr,
                            blockReference,
                            rows,
                            minPoint,
                            maxPoint,
                            depth + 1
                        );
                    }
                }

                return;
            }
        }

        private void CollectTextRowsFromBlockAttributes(
            Transaction tr,
            BlockReference blockReference,
            Matrix3d transform,
            List<OviaTextRow> rows,
            Point3d minPoint,
            Point3d maxPoint,
            int depth)
        {
            if (tr == null || blockReference == null || rows == null)
            {
                return;
            }

            foreach (ObjectId attributeId in blockReference.AttributeCollection)
            {
                AttributeReference attribute = tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;

                if (attribute == null || !IsCadShapeEntityVisible(tr, attribute))
                {
                    continue;
                }

                Matrix3d attributeTransform = depth > 0 ? transform : Matrix3d.Identity;
                Point3d attrPosition = GetTextReferencePoint(attribute, attribute.Position, attributeTransform);

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
        }

        private void CollectTextRowsFromBlockRecord(
            Transaction tr,
            BlockReference blockReference,
            BlockTableRecord blockRecord,
            Matrix3d transform,
            List<OviaTextRow> rows,
            Point3d minPoint,
            Point3d maxPoint,
            int depth)
        {
            if (tr == null
                || blockReference == null
                || blockRecord == null
                || rows == null
                || depth > 8)
            {
                return;
            }

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

        private void CollectTextRowsFromExplodedBlock(
            Transaction tr,
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
                    if (!IsCadShapeEntityVisible(tr, explodedEntity))
                    {
                        continue;
                    }

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
                            tr,
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

        private double GetAverageTextY(List<OviaTextRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return 0;
            }

            double total = 0;
            int count = 0;
            int i;

            for (i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                total += rows[i].Y;
                count++;
            }

            return count == 0 ? 0 : total / count;
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
            string baseFolder = ResolveOviaCadOutputDirectory();
            string drawingName = "unsaved";

            if (db != null && db.Filename != null && db.Filename.Trim() != "")
            {
                drawingName = Path.GetFileNameWithoutExtension(db.Filename);
            }

            drawingName = MakeSafeFileName(drawingName);

            string fileName = prefix + "_" + drawingName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv";

            return Path.Combine(baseFolder, fileName);
        }

        private string ResolveOviaCadOutputDirectory()
        {
            // OVIA Desktop이 현재 ERP project_no에 대응하는 Temp 경로를 hand-off 파일로 전달합니다.
            // 파일이 없거나 읽을 수 없는 독립 AutoCAD 실행은 기존 호환성을 위해 바탕화면으로 fallback합니다.
            try
            {
                string localRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OVIA"
                );
                string hintPath = Path.Combine(localRoot, "cad_output_path.txt");

                if (File.Exists(hintPath))
                {
                    string configured = File.ReadAllText(hintPath, Encoding.UTF8).Trim();
                    if (configured != "")
                    {
                        Directory.CreateDirectory(configured);
                        return configured;
                    }
                }
            }
            catch
            {
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
        public double RowCenterY = 0;
        public double RowBandHeight = 0;
        public double ShapeCellMinX = 0;
        public double ShapeCellMaxX = 0;
        public double ShapeCellMinY = 0;
        public double ShapeCellMaxY = 0;
        public string ShapeCellBoundsSource = "";

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
        public bool HasWorldLine = false;
        public double WorldX1 = 0;
        public double WorldY1 = 0;
        public double WorldX2 = 0;
        public double WorldY2 = 0;
        public double OriginalWorldX1 = 0;
        public double OriginalWorldY1 = 0;
        public double OriginalWorldX2 = 0;
        public double OriginalWorldY2 = 0;
        public string SourceType = "";
        public string SourceHandle = "";
        public string SourceIdentity = "";
        public bool SourceClosedPath = false;
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

    public class OviaCadShapeEndpointCluster
    {
        public double X = 0;
        public double Y = 0;
        public int Count = 0;
    }

    public class OviaCadTableGridModel
    {
        public List<double> AllVerticalXs = new List<double>();
        public List<double> AllHorizontalYs = new List<double>();
        public List<double> PhysicalTableVerticalXs = new List<double>();
        public List<double> VerticalXs = new List<double>();
        public List<double> HorizontalYs = new List<double>();
        public HashSet<string> GridSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double TableMinX = 0;
        public double TableMaxX = 0;
        public double TableMinY = 0;
        public double TableMaxY = 0;
        public double DataMinY = 0;
        public double DataMaxY = 0;
        public double TypicalRowHeight = 0;
        public double AxisTolerance = 0.03;
        public double MergeTolerance = 0.05;
        public double MatchToleranceX = 0.05;
        public double MatchToleranceY = 0.05;
    }

    public class OviaHeaderColumn
    {
        public string StandardKey = "";
        public string OriginalTitle = "";
        public bool HeaderTextVerified = false;
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
        public string SourceHandle = "";
    }

    public class OviaHeaderMap
    {
        public int HeaderRowIndex = -1;
        public List<OviaHeaderColumn> Columns = new List<OviaHeaderColumn>();
        public double MinX = 0;
        public double MaxX = 0;
    }
}
