using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    /// <summary>
    /// 실행 중인 AutoCAD 프로세스 기준 런타임 상태입니다.
    /// AutoCAD 프로세스 실행 여부와 OVIA Plugin 연결 여부는 서로 다른 상태로 취급합니다.
    /// </summary>
    public sealed class AutoCadRuntimeSnapshot
    {
        public bool IsRunning { get; internal set; }
        public int Year { get; internal set; }
        public string ProductVersion { get; internal set; }
        public string ExecutablePath { get; internal set; }

        public AutoCadRuntimeSnapshot()
        {
            ProductVersion = string.Empty;
            ExecutablePath = string.Empty;
        }
    }

    /// <summary>
    /// 특정 AutoCAD 설치 경로나 플러그인 연결 여부에 의존하지 않고 acad.exe 자체를 기준으로
    /// 실행 상태와 표시용 AutoCAD 연도를 판별합니다.
    ///
    /// 중요:
    /// - AutoCAD OFF/ON은 acad.exe 실행 여부만 사용합니다.
    /// - 2024는 Autodesk Release 24.3으로 판별합니다.
    /// - UI 동기화 Guard는 이 Resolver가 실제 화면에서 사용되는 즉시 전역 동기화를 시작합니다.
    /// </summary>
    public static class AutoCadVersionResolver
    {
        private static readonly Regex YearRegex = new Regex(
            @"(?<!\d)(20(?:2\d|3\d))(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ReleaseRegex = new Regex(
            @"(?<!\d)(\d{2})\.(\d)(?:\.|\D|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsAutoCadRunning()
        {
            EnsureRuntimeUiSynchronization();
            return GetRunningAutoCadCore().IsRunning;
        }

        public static int GetRunningAutoCadYear()
        {
            EnsureRuntimeUiSynchronization();
            return GetRunningAutoCadCore().Year;
        }

        public static string GetFooterSuffix()
        {
            EnsureRuntimeUiSynchronization();

            AutoCadRuntimeSnapshot snapshot = GetRunningAutoCadCore();
            if (!snapshot.IsRunning || snapshot.Year <= 0)
            {
                return string.Empty;
            }

            return " | AutoCAD V. : " + snapshot.Year.ToString();
        }

        public static AutoCadRuntimeSnapshot GetRunningAutoCad()
        {
            EnsureRuntimeUiSynchronization();
            return GetRunningAutoCadCore();
        }

        private static void EnsureRuntimeUiSynchronization()
        {
            // 이전 패치처럼 별도 Apply 스크립트가 OviaWorkspaceHeader를 수정해야만 동작하는 구조를 사용하지 않습니다.
            // Resolver가 기존 footer/header 코드에서 한 번이라도 호출되면 전체 OVIA 화면 동기화가 활성화됩니다.
            try
            {
                AutoCadFooterVisibilityGuard.EnsureGlobalSynchronization();
            }
            catch
            {
                // UI 보정 실패가 로그인/CAD 추출/ERP 동작을 방해해서는 안 됩니다.
            }
        }

        private static AutoCadRuntimeSnapshot GetRunningAutoCadCore()
        {
            AutoCadRuntimeSnapshot best = new AutoCadRuntimeSnapshot();
            Process[] processes = null;

            try
            {
                processes = Process.GetProcessesByName("acad");
            }
            catch
            {
                return best;
            }

            if (processes == null || processes.Length == 0)
            {
                return best;
            }

            best.IsRunning = true;

            int i;
            for (i = 0; i < processes.Length; i++)
            {
                Process process = processes[i];
                if (process == null)
                {
                    continue;
                }

                try
                {
                    AutoCadRuntimeSnapshot current = ReadProcess(process);
                    current.IsRunning = true;

                    // 여러 AutoCAD가 동시에 실행된 경우 연도를 확인 가능한 프로세스를 우선합니다.
                    // 둘 이상이면 표시용 대표값으로 높은 연도를 사용합니다.
                    if (current.Year > best.Year)
                    {
                        best = current;
                    }
                    else if (best.Year <= 0 && current.Year <= 0 && best.ExecutablePath.Length == 0)
                    {
                        best = current;
                    }
                }
                catch
                {
                    // 한 프로세스의 메타데이터 조회가 실패해도 acad.exe 존재 자체는 ON입니다.
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            best.IsRunning = true;

            // 권한/보안제품 때문에 실행 파일 버전 조회가 막힌 경우에도,
            // 설치된 정식 AutoCAD Release가 하나뿐이면 표시용 연도를 안전하게 보완합니다.
            if (best.Year <= 0)
            {
                best.Year = TryResolveSingleInstalledSupportedYear();
            }

            return best;
        }

        private static AutoCadRuntimeSnapshot ReadProcess(Process process)
        {
            AutoCadRuntimeSnapshot snapshot = new AutoCadRuntimeSnapshot();
            snapshot.IsRunning = true;

            string executablePath = TryGetExecutablePath(process);
            snapshot.ExecutablePath = executablePath;

            string mainWindowTitle = TryGetMainWindowTitle(process);
            string productVersion = string.Empty;
            string fileVersion = string.Empty;
            string productName = string.Empty;
            string fileDescription = string.Empty;

            FileVersionInfo info = TryGetVersionInfo(process, executablePath);
            if (info != null)
            {
                productVersion = SafeTrim(info.ProductVersion);
                fileVersion = SafeTrim(info.FileVersion);
                productName = SafeTrim(info.ProductName);
                fileDescription = SafeTrim(info.FileDescription);
            }

            snapshot.ProductVersion = productVersion.Length > 0 ? productVersion : fileVersion;

            // 경로/창 제목/ProductName에 2024 등의 연도가 직접 포함되는 환경을 먼저 처리합니다.
            string allText =
                executablePath + " " +
                mainWindowTitle + " " +
                productName + " " +
                fileDescription;

            int year = ExtractYear(allText);

            // 일반적인 acad.exe 메타데이터는 2024 대신 24.3.x 형식을 사용하므로 Release Number로 매핑합니다.
            if (year <= 0)
            {
                year = ResolveYearFromAutoCadRelease(productVersion);
            }

            if (year <= 0)
            {
                year = ResolveYearFromAutoCadRelease(fileVersion);
            }

            snapshot.Year = year;
            return snapshot;
        }

        private static FileVersionInfo TryGetVersionInfo(Process process, string executablePath)
        {
            try
            {
                if (process != null && process.MainModule != null && process.MainModule.FileVersionInfo != null)
                {
                    return process.MainModule.FileVersionInfo;
                }
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                try
                {
                    return FileVersionInfo.GetVersionInfo(executablePath);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string TryGetExecutablePath(Process process)
        {
            try
            {
                if (process != null && process.MainModule != null && process.MainModule.FileName != null)
                {
                    return process.MainModule.FileName.Trim();
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string TryGetMainWindowTitle(Process process)
        {
            try
            {
                return process == null || process.MainWindowTitle == null
                    ? string.Empty
                    : process.MainWindowTitle.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeTrim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static int ExtractYear(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            Match match = YearRegex.Match(text);
            if (!match.Success)
            {
                return 0;
            }

            int year;
            if (!int.TryParse(match.Groups[1].Value, out year))
            {
                return 0;
            }

            if (year < 2019 || year > 2039)
            {
                return 0;
            }

            return year;
        }

        private static int ResolveYearFromAutoCadRelease(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return 0;
            }

            Match match = ReleaseRegex.Match(version);
            if (!match.Success)
            {
                return 0;
            }

            int major;
            int minor;
            if (!int.TryParse(match.Groups[1].Value, out major) ||
                !int.TryParse(match.Groups[2].Value, out minor))
            {
                return 0;
            }

            return ResolveYearFromReleaseParts(major, minor);
        }

        private static int ResolveYearFromReleaseKey(string releaseKey)
        {
            if (string.IsNullOrWhiteSpace(releaseKey))
            {
                return 0;
            }

            Match match = Regex.Match(
                releaseKey,
                @"R?(\d{2})\.(\d)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return 0;
            }

            int major;
            int minor;
            if (!int.TryParse(match.Groups[1].Value, out major) ||
                !int.TryParse(match.Groups[2].Value, out minor))
            {
                return 0;
            }

            return ResolveYearFromReleaseParts(major, minor);
        }

        private static int ResolveYearFromReleaseParts(int major, int minor)
        {
            // Autodesk AutoCAD Release Number 기준.
            // 24.0=2021, 24.1=2022, 24.2=2023, 24.3=2024
            // 25.0=2025, 25.1=2026, 26.0=2027
            if (major == 24 && minor >= 0 && minor <= 3)
            {
                return 2021 + minor;
            }

            if (major == 25 && minor == 0)
            {
                return 2025;
            }

            if (major == 25 && minor == 1)
            {
                return 2026;
            }

            if (major == 26 && minor == 0)
            {
                return 2027;
            }

            return 0;
        }

        private static int TryResolveSingleInstalledSupportedYear()
        {
            HashSet<int> years = new HashSet<int>();

            CollectInstalledYears(Registry.LocalMachine, @"SOFTWARE\Autodesk\AutoCAD", years);
            CollectInstalledYears(Registry.CurrentUser, @"SOFTWARE\Autodesk\AutoCAD", years);

            if (years.Count != 1)
            {
                return 0;
            }

            foreach (int year in years)
            {
                return year;
            }

            return 0;
        }

        private static void CollectInstalledYears(RegistryKey hive, string path, HashSet<int> years)
        {
            if (hive == null || years == null)
            {
                return;
            }

            RegistryKey key = null;
            try
            {
                key = hive.OpenSubKey(path, false);
                if (key == null)
                {
                    return;
                }

                string[] names = key.GetSubKeyNames();
                int i;
                for (i = 0; i < names.Length; i++)
                {
                    int year = ResolveYearFromReleaseKey(names[i]);
                    if (year > 0)
                    {
                        years.Add(year);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (key != null)
                {
                    try
                    {
                        key.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
