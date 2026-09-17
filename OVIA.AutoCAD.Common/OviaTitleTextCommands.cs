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
    /// BarList 신규등록/수정 팝업의 제목 입력 전용 AutoCAD 명령입니다.
    /// 기존 OVIABOX/OVIABOXTABLE 데이터 추출 로직과 완전히 분리되어 있습니다.
    /// TEXT/MTEXT/ATTRIB뿐 아니라 선택한 BLOCK(INSERT) 내부의 표시 텍스트만 읽습니다.
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
                editor.WriteMessage("\nOVIA: 제목으로 가져올 글자 자체를 선택하세요. 여러 글자는 차례로 선택하고 Enter로 완료합니다.\n");

                List<CadTitleTextPiece> pieces = new List<CadTitleTextPiece>();
                int selectionOrder = 0;

                while (true)
                {
                    PromptNestedEntityOptions options =
                        new PromptNestedEntityOptions("\n제목 TEXT/MTEXT 선택 <Enter=완료>: ");
                    options.AllowNone = true;

                    PromptNestedEntityResult nestedResult = editor.GetNestedEntity(options);

                    if (nestedResult.Status == PromptStatus.None)
                    {
                        break;
                    }

                    if (nestedResult.Status == PromptStatus.Cancel)
                    {
                        WriteResult(requestToken, "CANCEL", "", "");
                        editor.WriteMessage("\nOVIA: 제목 텍스트 선택을 취소했습니다.\n");
                        return;
                    }

                    if (nestedResult.Status != PromptStatus.OK)
                    {
                        continue;
                    }

                    bool added = false;

                    using (Transaction transaction = database.TransactionManager.StartTransaction())
                    {
                        Entity entity = transaction.GetObject(
                            nestedResult.ObjectId, OpenMode.ForRead, false) as Entity;

                        Matrix3d transform = GetNestedTransform(
                            nestedResult.GetContainers(), transaction);

                        added = AddSelectedLeafText(
                            entity, transform, selectionOrder, pieces);

                        transaction.Commit();
                    }

                    if (added)
                    {
                        selectionOrder++;
                    }
                    else
                    {
                        // 중요: BLOCK 전체를 재귀 순회하는 fallback은 사용하지 않습니다.
                        editor.WriteMessage(
                            "\nOVIA: BLOCK 자체가 아니라 제목 글자(TEXT/MTEXT/속성)를 직접 클릭하세요.\n");
                    }
                }

                string titleText = BuildTitleText(pieces);
                if (titleText == "")
                {
                    WriteResult(requestToken, "ERROR", "",
                        "선택한 객체에서 제목 텍스트를 읽지 못했습니다.");
                    editor.WriteMessage(
                        "\nOVIA: 선택한 객체에서 제목 텍스트를 읽지 못했습니다.\n");
                    return;
                }

                // 기존 정상 버전의 Desktop Bridge 계약을 그대로 유지합니다.
                WriteResult(requestToken, "OK", titleText, "");
                editor.WriteMessage(
                    "\nOVIA: 선택한 CAD 텍스트를 BarList 제목 입력창으로 전달했습니다.\n");
            }
            catch (System.Exception ex)
            {
                WriteResult(requestToken, "ERROR", "", ex.Message);
                editor.WriteMessage(
                    "\nOVIA 제목 텍스트 추출 오류: " + ex.Message + "\n");
            }
        }

        private bool AddSelectedLeafText(
            Entity entity,
            Matrix3d transform,
            int selectionOrder,
            List<CadTitleTextPiece> pieces)
        {
            if (entity == null)
            {
                return false;
            }

            DBText dbText = entity as DBText;
            if (dbText != null)
            {
                AddPiece(
                    pieces,
                    dbText.TextString,
                    dbText.Position.TransformBy(transform),
                    selectionOrder);
                return true;
            }

            MText mText = entity as MText;
            if (mText != null)
            {
                string text = NormalizeText(mText.Text);
                if (text == "")
                {
                    text = NormalizeText(mText.Contents);
                }

                AddPiece(
                    pieces,
                    text,
                    mText.Location.TransformBy(transform),
                    selectionOrder);
                return true;
            }

            AttributeReference attributeReference = entity as AttributeReference;
            if (attributeReference != null)
            {
                AddPiece(
                    pieces,
                    attributeReference.TextString,
                    attributeReference.Position.TransformBy(transform),
                    selectionOrder);
                return true;
            }

            AttributeDefinition attributeDefinition = entity as AttributeDefinition;
            if (attributeDefinition != null)
            {
                AddPiece(
                    pieces,
                    attributeDefinition.TextString,
                    attributeDefinition.Position.TransformBy(transform),
                    selectionOrder);
                return true;
            }

            // BlockReference인 경우에도 내부 전체 텍스트를 수집하지 않습니다.
            return false;
        }

        private Matrix3d GetNestedTransform(
            ObjectId[] containers,
            Transaction transaction)
        {
            Matrix3d transform = Matrix3d.Identity;

            if (containers == null || containers.Length == 0)
            {
                return transform;
            }

            // GetContainers()는 바깥쪽/안쪽 BLOCK 경로를 제공합니다.
            // 문자열 추출에는 변환이 필요 없지만, 기존 BuildTitleText의 위치 정렬을
            // 유지하기 위해 선택된 leaf의 표시 위치에만 적용합니다.
            for (int i = containers.Length - 1; i >= 0; i--)
            {
                BlockReference blockReference =
                    transaction.GetObject(
                        containers[i], OpenMode.ForRead, false) as BlockReference;

                if (blockReference != null)
                {
                    transform = transform * blockReference.BlockTransform;
                }
            }

            return transform;
        }

        private void AddPiece(List<CadTitleTextPiece> pieces, string text, Point3d position, int selectionOrder)
        {
            text = NormalizeText(text);
            if (text == "")
            {
                return;
            }

            CadTitleTextPiece piece = new CadTitleTextPiece();
            piece.Text = text;
            piece.X = position.X;
            piece.Y = position.Y;
            piece.SelectionOrder = selectionOrder;
            pieces.Add(piece);
        }

        private string BuildTitleText(List<CadTitleTextPiece> pieces)
        {
            if (pieces == null || pieces.Count == 0)
            {
                return "";
            }

            double maxHeight = pieces.Max(piece => piece.Y);
            double minHeight = pieces.Min(piece => piece.Y);
            double yTolerance = Math.Max(0.0001, Math.Abs(maxHeight - minHeight) * 0.01);

            List<CadTitleTextPiece> ordered = pieces
                .OrderByDescending(piece => Math.Round(piece.Y / yTolerance))
                .ThenBy(piece => piece.X)
                .ThenBy(piece => piece.SelectionOrder)
                .ToList();

            StringBuilder title = new StringBuilder();
            int i;
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
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OVIA", "Bridge");
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
