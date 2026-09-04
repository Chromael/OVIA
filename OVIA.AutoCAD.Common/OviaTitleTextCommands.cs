using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(OVIA.AutoCAD_2027.OviaTitleTextCommands))]

namespace OVIA.AutoCAD_2027
{
    /// <summary>
    /// BarList 수정 팝업의 제목 입력 전용 AutoCAD 명령입니다.
    /// 기존 OVIABOX/OVIABOXTABLE 데이터 추출 로직과 완전히 분리되어 있습니다.
    /// </summary>
    public sealed class OviaTitleTextCommands
    {
        private const string RequestFileName = "cad_title_text.request";
        private const string ResultFileName = "cad_title_text.result";

        private sealed class CadTitleTextPiece
        {
            public string Text = "";
            public double X;
            public double Y;
            public int SelectionOrder;
        }

        [CommandMethod("OVIATITLETEXT", CommandFlags.Modal)]
        public void SelectTitleText()
        {
            string requestToken = ReadRequestToken();
            Document document = Application.DocumentManager.MdiActiveDocument;

            if (document == null)
            {
                WriteResult(requestToken, "ERROR", "", "활성 AutoCAD 도면을 찾지 못했습니다.");
                return;
            }

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                PromptSelectionOptions options = new PromptSelectionOptions();
                options.MessageForAdding = "\n제목으로 가져올 TEXT 또는 MTEXT를 선택한 후 Enter를 누르세요: ";
                options.MessageForRemoval = "\n제목 선택에서 제외할 객체를 지정하세요: ";
                options.AllowDuplicates = false;

                SelectionFilter filter = new SelectionFilter(
                    new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.Start, "TEXT,MTEXT,ATTRIB")
                    }
                );

                PromptSelectionResult selectionResult = editor.GetSelection(options, filter);

                if (selectionResult.Status == PromptStatus.Cancel)
                {
                    WriteResult(requestToken, "CANCEL", "", "");
                    editor.WriteMessage("\nOVIA: 제목 텍스트 선택을 취소했습니다.\n");
                    return;
                }

                if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
                {
                    WriteResult(requestToken, "ERROR", "", "TEXT 또는 MTEXT를 선택하지 못했습니다.");
                    editor.WriteMessage("\nOVIA: TEXT 또는 MTEXT를 선택하지 못했습니다.\n");
                    return;
                }

                List<CadTitleTextPiece> pieces = new List<CadTitleTextPiece>();
                ObjectId[] objectIds = selectionResult.Value.GetObjectIds();

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    int index;
                    for (index = 0; index < objectIds.Length; index++)
                    {
                        Entity entity = transaction.GetObject(objectIds[index], OpenMode.ForRead, false) as Entity;
                        CadTitleTextPiece piece = CreateTextPiece(entity, index);

                        if (piece != null && piece.Text != "")
                        {
                            pieces.Add(piece);
                        }
                    }

                    transaction.Commit();
                }

                string titleText = BuildTitleText(pieces);
                if (titleText == "")
                {
                    WriteResult(requestToken, "ERROR", "", "선택한 객체에서 제목 텍스트를 읽지 못했습니다.");
                    editor.WriteMessage("\nOVIA: 선택한 객체에서 제목 텍스트를 읽지 못했습니다.\n");
                    return;
                }

                WriteResult(requestToken, "OK", titleText, "");
                editor.WriteMessage("\nOVIA: 선택한 CAD 텍스트를 BarList 제목 입력창으로 전달했습니다.\n");
            }
            catch (System.Exception ex)
            {
                WriteResult(requestToken, "ERROR", "", ex.Message);
                editor.WriteMessage("\nOVIA 제목 텍스트 추출 오류: " + ex.Message + "\n");
            }
        }

        private CadTitleTextPiece CreateTextPiece(Entity entity, int selectionOrder)
        {
            if (entity == null)
            {
                return null;
            }

            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                return CreatePiece(dbText.TextString, dbText.Position, selectionOrder);
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                string text = NormalizeText(mText.Text);
                if (text == "")
                {
                    text = NormalizeText(mText.Contents);
                }

                return CreatePiece(text, mText.Location, selectionOrder);
            }

            AttributeReference attribute = entity as AttributeReference;
            if (attribute != null)
            {
                return CreatePiece(attribute.TextString, attribute.Position, selectionOrder);
            }

            return null;
        }

        private CadTitleTextPiece CreatePiece(string text, Point3d position, int selectionOrder)
        {
            text = NormalizeText(text);
            if (text == "")
            {
                return null;
            }

            CadTitleTextPiece piece = new CadTitleTextPiece();
            piece.Text = text;
            piece.X = position.X;
            piece.Y = position.Y;
            piece.SelectionOrder = selectionOrder;
            return piece;
        }

        private string BuildTitleText(List<CadTitleTextPiece> pieces)
        {
            if (pieces == null || pieces.Count == 0)
            {
                return "";
            }

            double maxHeight = 0.0;
            double minHeight = 0.0;
            bool initialized = false;

            int i;
            for (i = 0; i < pieces.Count; i++)
            {
                if (!initialized)
                {
                    maxHeight = pieces[i].Y;
                    minHeight = pieces[i].Y;
                    initialized = true;
                }
                else
                {
                    maxHeight = Math.Max(maxHeight, pieces[i].Y);
                    minHeight = Math.Min(minHeight, pieces[i].Y);
                }
            }

            double yTolerance = Math.Max(0.0001, Math.Abs(maxHeight - minHeight) * 0.01);
            List<CadTitleTextPiece> ordered = pieces
                .OrderByDescending(piece => Math.Round(piece.Y / yTolerance))
                .ThenBy(piece => piece.X)
                .ThenBy(piece => piece.SelectionOrder)
                .ToList();

            StringBuilder title = new StringBuilder();
            for (i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Text == "")
                {
                    continue;
                }

                if (title.Length > 0)
                {
                    title.Append(" ");
                }

                title.Append(ordered[i].Text);
            }

            return NormalizeText(title.ToString());
        }

        private string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string normalized = value
                .Replace("\\P", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            normalized = Regex.Replace(normalized, "\\s+", " ");
            return normalized.Trim();
        }

        private string ReadRequestToken()
        {
            try
            {
                string requestPath = GetRequestFilePath();
                if (!File.Exists(requestPath))
                {
                    return "";
                }

                return File.ReadAllText(requestPath, Encoding.UTF8).Trim();
            }
            catch
            {
                return "";
            }
        }

        private void WriteResult(string requestToken, string status, string titleText, string errorMessage)
        {
            string bridgeDirectory = GetBridgeDirectory();
            Directory.CreateDirectory(bridgeDirectory);

            string resultPath = GetResultFilePath();
            string tempPath = resultPath + ".tmp";
            string encodedText = Convert.ToBase64String(Encoding.UTF8.GetBytes(titleText ?? ""));
            string encodedError = Convert.ToBase64String(Encoding.UTF8.GetBytes(errorMessage ?? ""));

            string[] lines = new string[]
            {
                requestToken ?? "",
                status ?? "ERROR",
                encodedText,
                encodedError
            };

            File.WriteAllLines(tempPath, lines, new UTF8Encoding(false));

            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            File.Move(tempPath, resultPath);
        }

        private string GetBridgeDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA",
                "Bridge"
            );
        }

        private string GetRequestFilePath()
        {
            return Path.Combine(GetBridgeDirectory(), RequestFileName);
        }

        private string GetResultFilePath()
        {
            return Path.Combine(GetBridgeDirectory(), ResultFileName);
        }
    }
}
