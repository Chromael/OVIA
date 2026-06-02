using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class OviaProjectInfo
    {
        public string ProjectNo = "";
        public string ProjectName = "";
        public string ClientName = "";
        public string Status = "";
        public string CreatedDate = "";
        public string LastWorkDate = "";
        public string Manager = "";
        public string Memo = "";

        public OviaProjectInfo()
        {
        }

        public OviaProjectInfo(
            string projectNo,
            string projectName,
            string clientName,
            string status,
            string createdDate,
            string lastWorkDate,
            string manager,
            string memo
        )
        {
            ProjectNo = projectNo;
            ProjectName = projectName;
            ClientName = clientName;
            Status = status;
            CreatedDate = createdDate;
            LastWorkDate = lastWorkDate;
            Manager = manager;
            Memo = memo;
        }

        public string DisplayName
        {
            get
            {
                return ProjectNo + "  " + ProjectName;
            }
        }
    }

    public class OviaBarListSummary
    {
        public string FilePath = "";
        public string Title = "";
        public string CreatedDate = "";
        public string ModifiedDate = "";
        public int RowCount = 0;
        public double TotalQty = 0;
        public double TotalLength = 0;
        public double TotalWeight = 0;
        public string Writer = "";
        public string Status = "";
        public string Note = "";
    }

    public static class OviaLocalStore
    {
        public static string GetBaseDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA"
            );
        }

        public static List<OviaProjectInfo> GetSampleProjects()
        {
            List<OviaProjectInfo> list = new List<OviaProjectInfo>();

            list.Add(new OviaProjectInfo("1538", "2026_공장판매", "셀먼", "진행", "2026-04-28", "2026-05-20", "임대표", "최근 추출 테스트"));
            list.Add(new OviaProjectInfo("1563", "광주 화정 주상복합 1BL 신축공사", "현대건설", "진행", "2026-02-03", "2026-05-19", "김팀장", ""));
            list.Add(new OviaProjectInfo("1606", "광양 홍숭 수성복합 신축공사", "거래처A", "진행", "2026-04-15", "2026-05-18", "관리자", ""));
            list.Add(new OviaProjectInfo("1618", "나주 봉황 참송 이앤씨", "거래처B", "진행", "2026-05-01", "2026-05-14", "관리자", ""));
            list.Add(new OviaProjectInfo("1523", "고창 프로젝트", "거래처C", "완료", "2026-03-02", "2026-04-10", "관리자", "완료공사"));

            return list;
        }

        public static string GetProjectDirectory(OviaProjectInfo project)
        {
            string key = SanitizeFileName(project.ProjectNo + "_" + project.ProjectName);

            if (key == "_")
            {
                key = "NoProject";
            }

            return Path.Combine(GetBaseDirectory(), "Projects", key);
        }

        public static string GetProjectBarListDirectory(OviaProjectInfo project)
        {
            return Path.Combine(GetProjectDirectory(project), "BarList");
        }

        public static string SaveBarListCsv(OviaProjectInfo project, string title, List<string> headers, List<List<string>> rows)
        {
            string dir = GetProjectBarListDirectory(project);
            Directory.CreateDirectory(dir);

            string safeTitle = SanitizeFileName(title);

            if (safeTitle == "")
            {
                safeTitle = "BarList";
            }

            string fileName =
                "BarList_" +
                project.ProjectNo +
                "_" +
                safeTitle +
                "_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                ".csv";

            string filePath = Path.Combine(dir, fileName);

            using (StreamWriter writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                WriteCsvLine(writer, headers);

                int i;

                for (i = 0; i < rows.Count; i++)
                {
                    WriteCsvLine(writer, rows[i]);
                }
            }

            return filePath;
        }

        public static List<OviaBarListSummary> GetBarListSummaries(OviaProjectInfo project)
        {
            List<OviaBarListSummary> list = new List<OviaBarListSummary>();

            string dir = GetProjectBarListDirectory(project);

            if (!Directory.Exists(dir))
            {
                return list;
            }

            string[] files = Directory.GetFiles(dir, "BarList_*.csv");

            int i;

            for (i = 0; i < files.Length; i++)
            {
                OviaBarListSummary summary = BuildSummary(files[i]);
                list.Add(summary);
            }

            list.Sort(delegate (OviaBarListSummary a, OviaBarListSummary b)
            {
                DateTime ad;
                DateTime bd;

                DateTime.TryParse(a.ModifiedDate, out ad);
                DateTime.TryParse(b.ModifiedDate, out bd);

                return bd.CompareTo(ad);
            });

            return list;
        }

        public static OviaBarListSummary BuildSummary(string filePath)
        {
            OviaBarListSummary summary = new OviaBarListSummary();

            summary.FilePath = filePath;
            summary.Title = GetTitleFromFileName(filePath);
            summary.CreatedDate = File.GetCreationTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.ModifiedDate = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm");
            summary.Writer = Environment.UserName;
            summary.Status = "저장";
            summary.Note = "";

            try
            {
                List<List<string>> rows = ReadCsv(filePath);

                if (rows.Count > 1)
                {
                    List<string> headers = rows[0];

                    int qtyIndex = FindHeaderIndex(headers, "수량");
                    int totalLengthIndex = FindHeaderIndex(headers, "총길이");
                    int weightIndex = FindHeaderIndex(headers, "중량");

                    int r;

                    for (r = 1; r < rows.Count; r++)
                    {
                        summary.RowCount++;

                        if (qtyIndex >= 0 && qtyIndex < rows[r].Count)
                        {
                            summary.TotalQty += ParseNumber(rows[r][qtyIndex]);
                        }

                        if (totalLengthIndex >= 0 && totalLengthIndex < rows[r].Count)
                        {
                            summary.TotalLength += ParseNumber(rows[r][totalLengthIndex]);
                        }

                        if (weightIndex >= 0 && weightIndex < rows[r].Count)
                        {
                            summary.TotalWeight += ParseNumber(rows[r][weightIndex]);
                        }
                    }
                }
            }
            catch
            {
            }

            return summary;
        }

        public static List<List<string>> ReadCsv(string filePath)
        {
            string content = File.ReadAllText(filePath, Encoding.UTF8);

            if (content.Length > 0 && content[0] == '\uFEFF')
            {
                content = content.Substring(1);
            }

            return ParseCsv(content);
        }

        public static List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();

            bool inQuotes = false;
            int i = 0;

            while (i < content.Length)
            {
                char ch = content[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"')
                    {
                        inQuotes = true;
                    }
                    else if (ch == ',')
                    {
                        row.Add(cell.ToString());
                        cell.Length = 0;
                    }
                    else if (ch == '\r')
                    {
                    }
                    else if (ch == '\n')
                    {
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }

                i++;
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        public static string Csv(string value)
        {
            if (value == null)
            {
                value = "";
            }

            value = value.Replace("\"", "\"\"");

            return "\"" + value + "\"";
        }

        public static void WriteCsvLine(StreamWriter writer, List<string> cells)
        {
            int i;

            for (i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    writer.Write(",");
                }

                writer.Write(Csv(cells[i]));
            }

            writer.WriteLine();
        }

        public static int FindHeaderIndex(List<string> headers, string keyword)
        {
            if (headers == null)
            {
                return -1;
            }

            int i;

            for (i = 0; i < headers.Count; i++)
            {
                if (headers[i] != null && headers[i].IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public static double ParseNumber(string text)
        {
            if (text == null)
            {
                return 0;
            }

            text = text.Trim();
            text = text.Replace(",", "");
            text = text.Replace(" ", "");

            if (text == "")
            {
                return 0;
            }

            double value;

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            return 0;
        }

        public static string SanitizeFileName(string value)
        {
            if (value == null)
            {
                value = "";
            }

            char[] invalids = Path.GetInvalidFileNameChars();
            int i;

            for (i = 0; i < invalids.Length; i++)
            {
                value = value.Replace(invalids[i], '_');
            }

            value = value.Replace(" ", "_");

            return value;
        }

        private static string GetTitleFromFileName(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);

            if (name == null)
            {
                return "";
            }

            string[] parts = name.Split('_');

            if (parts.Length >= 4)
            {
                return parts[2];
            }

            return name;
        }
    }

    public class OviaUiCard : Panel
    {
        public Color SurfaceColor = Color.FromArgb(244, 248, 255);

        public OviaUiCard()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.BackColor = Color.White;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(SurfaceColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaUiDraw.RoundRect(rect, 14))
            {
                using (SolidBrush fill = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(Color.FromArgb(230, 235, 246), 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    public class OviaUiButton : Control
    {
        public Color StartColor = Color.FromArgb(91, 49, 225);
        public Color EndColor = Color.FromArgb(37, 30, 130);

        private bool hover;

        public OviaUiButton()
        {
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);

            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color s = hover ? Lighten(StartColor, 18) : StartColor;
            Color en = hover ? Lighten(EndColor, 18) : EndColor;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = OviaUiDraw.RoundRect(rect, 7))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, s, en, LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                this.Text,
                OviaFluentTheme.FontKorean(9F, FontStyle.Bold),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            base.OnPaint(e);
        }

        private Color Lighten(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, color.R + amount),
                Math.Min(255, color.G + amount),
                Math.Min(255, color.B + amount)
            );
        }
    }

    public static class OviaUiDraw
    {
        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
