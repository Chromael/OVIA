using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OVIA.Desktop
{
    public class AutoCadInstallInfo
    {
        public string ProductName = "";
        public string VersionKey = "";
        public string InstallPath = "";
        public int Year = 0;
        public bool IsLT = false;

        public string YearText
        {
            get
            {
                if (Year > 0)
                {
                    return Year.ToString();
                }

                return "감지됨";
            }
        }

        public string PluginGroup
        {
            get
            {
                if (IsLT)
                {
                    return "AutoCAD LT는 지원하지 않습니다.";
                }

                if (Year >= 2027)
                {
                    return ".NET 10용 OVIA 모듈 대상";
                }

                if (Year >= 2025 && Year <= 2026)
                {
                    return ".NET 8용 OVIA 모듈 대상";
                }

                if (Year >= 2021 && Year <= 2024)
                {
                    return ".NET Framework 4.8용 OVIA 모듈 대상";
                }

                if (Year >= 2019 && Year <= 2020)
                {
                    return "2차 지원 검토 대상";
                }

                return "지원 버전 추가 검토 필요";
            }
        }

        public string GetDisplayText()
        {
            string text = "";

            text += "AutoCAD 감지 결과\r\n\r\n";
            text += "제품명: " + ProductName + "\r\n";

            if (VersionKey != "")
            {
                text += "버전 키: " + VersionKey + "\r\n";
            }

            if (Year > 0)
            {
                text += "판단 연도: " + Year.ToString() + "\r\n";
            }

            if (InstallPath != "")
            {
                text += "설치 경로: " + InstallPath + "\r\n";
            }

            text += "\r\nOVIA 판단: " + PluginGroup;

            return text;
        }
    }

    public static class AutoCadDetector
    {
        public static List<AutoCadInstallInfo> FindInstalledAutoCad()
        {
            List<AutoCadInstallInfo> results = new List<AutoCadInstallInfo>();

            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.LocalMachine, RegistryView.Registry32);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry64);
            ScanAutoCadRegistryRoot(results, RegistryHive.CurrentUser, RegistryView.Registry32);

            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanUninstallRegistry(results, RegistryHive.LocalMachine, RegistryView.Registry32);

            RemoveDuplicates(results);
            SortByYearDesc(results);
            RemoveLtOnlyIfGeneralExists(results);

            return results;
        }

        private static void ScanAutoCadRegistryRoot(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");

                if (root == null)
                {
                    return;
                }

                ScanRegistryRecursive(results, root, "", 0);
                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static void ScanRegistryRecursive(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey, int depth)
        {
            if (depth > 4 || key == null)
            {
                return;
            }

            TryReadAutoCadInfo(results, key, versionKey);

            string[] subNames;

            try
            {
                subNames = key.GetSubKeyNames();
            }
            catch
            {
                return;
            }

            int i;

            for (i = 0; i < subNames.Length; i++)
            {
                try
                {
                    RegistryKey sub = key.OpenSubKey(subNames[i]);

                    string nextVersionKey = versionKey;

                    if (nextVersionKey == "")
                    {
                        nextVersionKey = subNames[i];
                    }
                    else
                    {
                        nextVersionKey += "\\" + subNames[i];
                    }

                    ScanRegistryRecursive(results, sub, nextVersionKey, depth + 1);

                    if (sub != null)
                    {
                        sub.Close();
                    }
                }
                catch
                {
                }
            }
        }

        private static void TryReadAutoCadInfo(List<AutoCadInstallInfo> results, RegistryKey key, string versionKey)
        {
            string productName = ReadRegistryString(key, "ProductName");

            if (productName == "")
            {
                productName = ReadRegistryString(key, "DisplayName");
            }

            if (productName == "")
            {
                productName = ReadRegistryString(key, "Product");
            }

            if (productName == "")
            {
                return;
            }

            if (!IsDisplayableAutoCadProductName(productName))
            {
                return;
            }

            AutoCadInstallInfo info = new AutoCadInstallInfo();
            info.ProductName = productName;
            info.VersionKey = versionKey;
            info.InstallPath = ReadPossibleInstallPath(key);
            info.Year = ExtractYear(productName + " " + versionKey);
            info.IsLT = productName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

            results.Add(info);
        }

        private static void ScanUninstallRegistry(List<AutoCadInstallInfo> results, RegistryHive hive, RegistryView view)
        {
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

                if (root == null)
                {
                    return;
                }

                string[] subNames = root.GetSubKeyNames();
                int i;

                for (i = 0; i < subNames.Length; i++)
                {
                    RegistryKey sub = root.OpenSubKey(subNames[i]);

                    if (sub == null)
                    {
                        continue;
                    }

                    string displayName = ReadRegistryString(sub, "DisplayName");

                    if (IsDisplayableAutoCadProductName(displayName))
                    {
                        AutoCadInstallInfo info = new AutoCadInstallInfo();
                        info.ProductName = displayName;
                        info.VersionKey = subNames[i];
                        info.InstallPath = ReadPossibleInstallPath(sub);
                        info.Year = ExtractYear(displayName);
                        info.IsLT = displayName.IndexOf("LT", StringComparison.OrdinalIgnoreCase) >= 0;

                        results.Add(info);
                    }

                    sub.Close();
                }

                root.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static string ReadPossibleInstallPath(RegistryKey key)
        {
            string value = "";

            value = ReadRegistryString(key, "AcadLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallLocation");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "Location");
            if (value != "")
            {
                return value;
            }

            value = ReadRegistryString(key, "InstallDir");
            if (value != "")
            {
                return value;
            }

            return "";
        }

        private static string ReadRegistryString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);

                if (value == null)
                {
                    return "";
                }

                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static int ExtractYear(string text)
        {
            Match match = Regex.Match(text, @"20\d{2}");

            if (!match.Success)
            {
                return 0;
            }

            int year = 0;
            int.TryParse(match.Value, out year);

            return year;
        }

        private static void RemoveDuplicates(List<AutoCadInstallInfo> list)
        {
            int i;
            int j;

            for (i = list.Count - 1; i >= 0; i--)
            {
                for (j = 0; j < i; j++)
                {
                    if (IsSameAutoCadInstall(list[i], list[j]))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool IsSameAutoCadInstall(AutoCadInstallInfo a, AutoCadInstallInfo b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.Year != b.Year)
            {
                return false;
            }

            if (a.IsLT != b.IsLT)
            {
                return false;
            }

            if (string.Equals(a.ProductName, b.ProductName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (NormalizePath(a.InstallPath) != "" && string.Equals(NormalizePath(a.InstallPath), NormalizePath(b.InstallPath), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (NormalizeAutoCadProductName(a.ProductName) != "" && string.Equals(NormalizeAutoCadProductName(a.ProductName), NormalizeAutoCadProductName(b.ProductName), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (path == null)
            {
                return "";
            }

            return path.Trim().TrimEnd('\\', '/');
        }

        private static string NormalizeAutoCadProductName(string productName)
        {
            if (productName == null)
            {
                return "";
            }

            return productName
                .Replace("Autodesk", "")
                .Replace("autodesk", "")
                .Trim();
        }

        private static bool IsDisplayableAutoCadProductName(string productName)
        {
            if (productName == null)
            {
                return false;
            }

            string name = productName.Trim();

            if (name.IndexOf("AutoCAD", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (name.IndexOf("MCP Server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Open in Desktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Open Desktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (name.IndexOf("Desktop Connector", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        private static void SortByYearDesc(List<AutoCadInstallInfo> list)
        {
            list.Sort(delegate (AutoCadInstallInfo a, AutoCadInstallInfo b)
            {
                return b.Year.CompareTo(a.Year);
            });
        }

        private static void RemoveLtOnlyIfGeneralExists(List<AutoCadInstallInfo> list)
        {
            bool hasGeneral = false;
            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (!list[i].IsLT)
                {
                    hasGeneral = true;
                    break;
                }
            }

            if (!hasGeneral)
            {
                return;
            }

            for (i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsLT)
                {
                    list.RemoveAt(i);
                }
            }
        }
    }

    public static class AutoCadRuntimeChecker
    {
        public static bool IsAutoCadRunning()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("acad");

                return processes != null && processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

}
