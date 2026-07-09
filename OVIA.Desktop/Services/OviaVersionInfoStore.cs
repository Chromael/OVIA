using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public class OviaVersionInfoEntry
    {
        public int SequenceNo = 0;
        public string VersionText = string.Empty;
        public string WorkDateText = string.Empty;
        public string UpdateContent = string.Empty;

        public OviaVersionInfoEntry Clone()
        {
            return new OviaVersionInfoEntry
            {
                SequenceNo = SequenceNo,
                VersionText = VersionText == null ? string.Empty : VersionText,
                WorkDateText = WorkDateText == null ? string.Empty : WorkDateText,
                UpdateContent = UpdateContent == null ? string.Empty : UpdateContent
            };
        }
    }

    public static class OviaVersionInfoStore
    {
        private const string VersionFolderName = "Version";
        private const string VersionFileName = "ovia_version_history.ovia";
        private const string FileHeader = "OVIA_VERSION_HISTORY_V1";
        private static readonly object SyncRoot = new object();

        public static List<OviaVersionInfoEntry> Load()
        {
            return Load(string.Empty);
        }

        public static List<OviaVersionInfoEntry> Load(string fallbackVersion)
        {
            lock (SyncRoot)
            {
                string path = GetEffectiveVersionInfoFilePath();
                List<OviaVersionInfoEntry> entries = ReadFile(path);
                if (entries.Count == 0)
                {
                    entries = CreateDefaultEntries(fallbackVersion);
                }

                NormalizeEntries(entries);
                return CloneEntries(entries);
            }
        }

        public static void Save(List<OviaVersionInfoEntry> entries)
        {
            lock (SyncRoot)
            {
                List<OviaVersionInfoEntry> normalized = CloneEntries(entries);
                NormalizeEntries(normalized);

                string primaryPath = GetInstallVersionInfoFilePath();
                try
                {
                    WriteFile(primaryPath, normalized);
                    return;
                }
                catch
                {
                }

                string fallbackPath = GetUserVersionInfoFilePath();
                WriteFile(fallbackPath, normalized);
            }
        }

        public static string GetLatestVersionText()
        {
            return GetLatestVersionText(string.Empty);
        }

        public static string GetLatestVersionText(string fallbackVersion)
        {
            List<OviaVersionInfoEntry> entries = Load(fallbackVersion);
            OviaVersionInfoEntry latest = GetLatestEntry(entries);
            if (latest == null)
            {
                return NormalizeVersionText(fallbackVersion);
            }

            return NormalizeVersionText(latest.VersionText);
        }

        public static OviaVersionInfoEntry GetLatestEntry(List<OviaVersionInfoEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            OviaVersionInfoEntry latest = null;
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaVersionInfoEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (latest == null || entry.SequenceNo >= latest.SequenceNo)
                {
                    latest = entry;
                }
            }

            return latest == null ? null : latest.Clone();
        }

        public static string GetRecommendedVersionInfoFilePath()
        {
            return GetInstallVersionInfoFilePath();
        }

        public static string GetEffectiveVersionInfoFilePath()
        {
            string installPath = GetInstallVersionInfoFilePath();
            string userPath = GetUserVersionInfoFilePath();
            bool hasInstall = File.Exists(installPath);
            bool hasUser = File.Exists(userPath);

            if (hasInstall && hasUser)
            {
                try
                {
                    DateTime installTime = File.GetLastWriteTimeUtc(installPath);
                    DateTime userTime = File.GetLastWriteTimeUtc(userPath);
                    return userTime > installTime ? userPath : installPath;
                }
                catch
                {
                    return installPath;
                }
            }

            if (hasInstall)
            {
                return installPath;
            }

            if (hasUser)
            {
                return userPath;
            }

            return installPath;
        }

        public static string GetInstallVersionInfoFilePath()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                if (!string.IsNullOrWhiteSpace(Application.StartupPath))
                {
                    basePath = Application.StartupPath;
                }
            }
            catch
            {
            }

            return Path.Combine(basePath, "Data", VersionFolderName, VersionFileName);
        }

        public static string GetDisplayInstallVersionInfoFilePath()
        {
            string runtimePath = string.Empty;
            try
            {
                runtimePath = Application.StartupPath == null ? string.Empty : Application.StartupPath.Trim();
            }
            catch
            {
                runtimePath = string.Empty;
            }

            if (IsDevelopmentRuntimePath(runtimePath))
            {
                return Path.Combine(GetExpectedProgramFilesOviaPath(), "Data", VersionFolderName, VersionFileName);
            }

            if (string.IsNullOrWhiteSpace(runtimePath))
            {
                runtimePath = GetExpectedProgramFilesOviaPath();
            }

            return Path.Combine(runtimePath, "Data", VersionFolderName, VersionFileName);
        }

        private static bool IsDevelopmentRuntimePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            string normalized = path.Replace('/', '\\');
            if (normalized.IndexOf("\\bin\\Debug", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\bin\\Release", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\OVIA.Desktop", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\Project\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string GetExpectedProgramFilesOviaPath()
        {
            string programFiles = string.Empty;
            try
            {
                programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }
            catch
            {
                programFiles = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(programFiles))
            {
                programFiles = @"C:\Program Files";
            }

            return Path.Combine(programFiles, "OVIA");
        }

        public static string GetUserVersionInfoFilePath()
        {
            return Path.Combine(OviaSystemSettingsStore.GetSettingsFolder(), "Data", VersionFolderName, VersionFileName);
        }

        public static OviaVersionInfoEntry CreateNewEntry(List<OviaVersionInfoEntry> currentEntries)
        {
            int nextNo = 1;
            if (currentEntries != null)
            {
                int i;
                for (i = 0; i < currentEntries.Count; i++)
                {
                    if (currentEntries[i] != null && currentEntries[i].SequenceNo >= nextNo)
                    {
                        nextNo = currentEntries[i].SequenceNo + 1;
                    }
                }
            }

            string latestVersion = GetLatestVersionText("1.0.0");
            if (latestVersion == string.Empty)
            {
                latestVersion = "1.0.0";
            }

            return new OviaVersionInfoEntry
            {
                SequenceNo = nextNo,
                VersionText = latestVersion,
                WorkDateText = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                UpdateContent = string.Empty
            };
        }

        public static bool IsValidVersionText(string value)
        {
            string version = NormalizeVersionText(value);
            if (version == string.Empty)
            {
                return false;
            }

            string[] parts = version.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            int i;
            for (i = 0; i < parts.Length; i++)
            {
                if (!IsValidVersionPartText(parts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidVersionPartText(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            if (text == string.Empty)
            {
                return true;
            }

            if (text.Length > 3)
            {
                return false;
            }

            int number;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }

            return number >= 0 && number <= 999;
        }

        public static string NormalizeVersionPartText(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            if (text == string.Empty)
            {
                return "0";
            }

            int number;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return text;
            }

            if (number < 0)
            {
                number = 0;
            }
            else if (number > 999)
            {
                number = 999;
            }

            return number.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildVersionText(string buildVersion, string featureVersion, string patchVersion)
        {
            return NormalizeVersionPartText(buildVersion)
                + "." + NormalizeVersionPartText(featureVersion)
                + "." + NormalizeVersionPartText(patchVersion);
        }

        public static int GetVersionPart(string value, int partIndex)
        {
            string version = NormalizeVersionText(value);
            string[] parts = version.Split('.');
            if (partIndex < 0 || partIndex >= parts.Length)
            {
                return 0;
            }

            int number;
            if (!int.TryParse(parts[partIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return 0;
            }

            if (number < 0)
            {
                return 0;
            }

            if (number > 999)
            {
                return 999;
            }

            return number;
        }

        public static string GetVersionPartText(string value, int partIndex)
        {
            return GetVersionPart(value, partIndex).ToString(CultureInfo.InvariantCulture);
        }

        public static string NormalizeVersionText(string value)
        {
            string version = value == null ? string.Empty : value.Trim();
            if (version.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Substring("Version".Length).Trim();
            }

            version = version.Replace(" ", string.Empty);
            if (version == string.Empty)
            {
                return string.Empty;
            }

            string[] rawParts = version.Split('.');
            if (rawParts.Length == 0)
            {
                return string.Empty;
            }

            string buildVersion = rawParts.Length > 0 ? rawParts[0] : "0";
            string featureVersion = rawParts.Length > 1 ? rawParts[1] : "0";
            string patchVersion = rawParts.Length > 2 ? rawParts[2] : "0";
            return BuildVersionText(buildVersion, featureVersion, patchVersion);
        }

        public static string FormatDisplayVersion(string value)
        {
            string version = NormalizeVersionText(value);
            if (version == string.Empty)
            {
                version = "1.0.0";
            }

            return "Version " + version;
        }

        public static string NormalizeWorkDateText(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            if (text == string.Empty)
            {
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            DateTime date;
            string[] compatibilityFormats = new string[]
            {
                "yyyy-MM-dd HH:mm",
                "yyyy-MM-dd H:mm",
                "yyyyMMdd HH:mm",
                "yyyyMMddHHmm",
                "yyyy-MM-dd",
                "yyyy.MM.dd HH:mm",
                "yyyy.MM.dd"
            };

            if (DateTime.TryParseExact(text, compatibilityFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return text;
        }

        public static bool IsValidWorkDateText(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            if (text == string.Empty)
            {
                return true;
            }

            DateTime date;
            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        public static void ExportToExcelHtml(string filePath, List<OviaVersionInfoEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            List<OviaVersionInfoEntry> normalized = CloneEntries(entries);
            NormalizeEntries(normalized);

            StringBuilder html = new StringBuilder();
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />");
            html.AppendLine("<style>table{border-collapse:collapse;font-family:Malgun Gothic,Arial;font-size:10pt;}th,td{border:1px solid #cccccc;padding:6px;}th{background:#f3f4f6;}td.content{width:720px;white-space:pre-wrap;}</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>순번</th><th>빌드버전</th><th>기능버전</th><th>수정버전</th><th>작업일</th><th>업데이트 내용</th></tr>");

            int i;
            for (i = 0; i < normalized.Count; i++)
            {
                OviaVersionInfoEntry entry = normalized[i];
                html.AppendLine("<tr>");
                html.Append("<td>").Append(EscapeHtml(entry.SequenceNo.ToString(CultureInfo.InvariantCulture))).AppendLine("</td>");
                html.Append("<td>").Append(EscapeHtml(GetVersionPartText(entry.VersionText, 0))).AppendLine("</td>");
                html.Append("<td>").Append(EscapeHtml(GetVersionPartText(entry.VersionText, 1))).AppendLine("</td>");
                html.Append("<td>").Append(EscapeHtml(GetVersionPartText(entry.VersionText, 2))).AppendLine("</td>");
                html.Append("<td>").Append(EscapeHtml(entry.WorkDateText)).AppendLine("</td>");
                html.Append("<td class=\"content\">").Append(EscapeHtml(entry.UpdateContent).Replace("\r\n", "<br />").Replace("\n", "<br />")).AppendLine("</td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</table>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            string folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(filePath, html.ToString(), Encoding.UTF8);
        }

        public static void NormalizeEntries(List<OviaVersionInfoEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            List<OviaVersionInfoEntry> valid = new List<OviaVersionInfoEntry>();
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                OviaVersionInfoEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                entry.VersionText = NormalizeVersionText(entry.VersionText);
                entry.WorkDateText = NormalizeWorkDateText(entry.WorkDateText);
                entry.UpdateContent = entry.UpdateContent == null ? string.Empty : entry.UpdateContent.Trim();

                if (entry.VersionText == string.Empty && entry.UpdateContent == string.Empty)
                {
                    continue;
                }

                valid.Add(entry);
            }

            entries.Clear();
            for (i = 0; i < valid.Count; i++)
            {
                valid[i].SequenceNo = i + 1;
                entries.Add(valid[i]);
            }
        }

        private static List<OviaVersionInfoEntry> ReadFile(string path)
        {
            List<OviaVersionInfoEntry> entries = new List<OviaVersionInfoEntry>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return entries;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int i;
                for (i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] == null ? string.Empty : lines[i].Trim();
                    if (line == string.Empty || line == FileHeader || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    OviaVersionInfoEntry entry = DecodeEntry(line);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch
            {
                return new List<OviaVersionInfoEntry>();
            }

            return entries;
        }

        private static void WriteFile(string path, List<OviaVersionInfoEntry> entries)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            List<string> lines = new List<string>();
            lines.Add(FileHeader);
            int i;
            for (i = 0; i < entries.Count; i++)
            {
                lines.Add(EncodeEntry(entries[i]));
            }

            File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        private static List<OviaVersionInfoEntry> CreateDefaultEntries(string fallbackVersion)
        {
            string version = NormalizeVersionText(fallbackVersion);
            if (version == string.Empty)
            {
                version = "1.0.0";
            }

            return new List<OviaVersionInfoEntry>
            {
                new OviaVersionInfoEntry
                {
                    SequenceNo = 1,
                    VersionText = version,
                    WorkDateText = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    UpdateContent = "OVIA 초기 버전정보입니다."
                }
            };
        }

        private static List<OviaVersionInfoEntry> CloneEntries(List<OviaVersionInfoEntry> entries)
        {
            List<OviaVersionInfoEntry> clones = new List<OviaVersionInfoEntry>();
            if (entries == null)
            {
                return clones;
            }

            int i;
            for (i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                {
                    clones.Add(entries[i].Clone());
                }
            }

            return clones;
        }

        private static string EncodeEntry(OviaVersionInfoEntry entry)
        {
            if (entry == null)
            {
                entry = new OviaVersionInfoEntry();
            }

            string raw = entry.SequenceNo.ToString(CultureInfo.InvariantCulture)
                + "\t" + (entry.VersionText == null ? string.Empty : entry.VersionText)
                + "\t" + (entry.WorkDateText == null ? string.Empty : entry.WorkDateText)
                + "\t" + (entry.UpdateContent == null ? string.Empty : entry.UpdateContent);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        private static OviaVersionInfoEntry DecodeEntry(string encoded)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded.Trim());
                string raw = Encoding.UTF8.GetString(bytes);
                string[] parts = raw.Split(new char[] { '\t' }, 4);
                if (parts.Length < 4)
                {
                    return null;
                }

                int sequenceNo;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequenceNo))
                {
                    sequenceNo = 0;
                }

                return new OviaVersionInfoEntry
                {
                    SequenceNo = sequenceNo,
                    VersionText = parts[1],
                    WorkDateText = parts[2],
                    UpdateContent = parts[3]
                };
            }
            catch
            {
                return null;
            }
        }

        private static string EscapeHtml(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
