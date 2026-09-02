using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace OVIA.Desktop
{
    public enum OviaEnvironmentStatus
    {
        Supported,
        Warning,
        Blocked
    }

    public class OviaEnvironmentIssue
    {
        public OviaEnvironmentStatus Status = OviaEnvironmentStatus.Supported;
        public string Title = "";
        public string Message = "";
        public string ActionGuide = "";
    }

    public class OviaEnvironmentReport
    {
        public OviaEnvironmentStatus OverallStatus = OviaEnvironmentStatus.Supported;
        public string WindowsName = "";
        public string WindowsVersionText = "";
        public int WindowsMajor = 0;
        public int WindowsMinor = 0;
        public int WindowsBuild = 0;
        public bool Is64BitOperatingSystem = false;
        public bool IsArmProcessor = false;
        public int DotNetRelease = 0;
        public string DotNetVersionText = "";
        public bool IsDotNet472OrHigher = false;
        public bool IsDotNet48OrHigher = false;
        public bool IsWebView2RuntimeAvailable = false;
        public string WebView2RuntimeVersionText = "";
        public bool IsAutoCadRunning = false;
        public bool CanWriteOviaWorkFolder = false;
        public string OviaWorkFolder = "";
        public List<AutoCadInstallInfo> AutoCadInstalls = new List<AutoCadInstallInfo>();
        public AutoCadInstallInfo RecommendedAutoCad = null;
        public List<OviaEnvironmentIssue> Issues = new List<OviaEnvironmentIssue>();

        public string OverallStatusText
        {
            get
            {
                if (OverallStatus == OviaEnvironmentStatus.Blocked)
                {
                    return "설치 차단";
                }

                if (OverallStatus == OviaEnvironmentStatus.Warning)
                {
                    return "제한적 지원";
                }

                return "지원 가능";
            }
        }

        public static bool IsOviaExtractionPluginSupportedYear(int year)
        {
            // 현재 실제 빌드/배포 프로젝트가 준비된 버전만 추출 가능으로 판정합니다.
            // 상단 AutoCAD ON/OFF UI는 이 목록과 무관하게 모든 acad.exe에 공통 적용합니다.
            return year == 2024 || year == 2027;
        }

        public string GetShortAutoCadText()
        {
            if (RecommendedAutoCad == null)
            {
                return "AutoCAD 미감지";
            }

            if (RecommendedAutoCad.IsLT)
            {
                return "AutoCAD LT 미지원";
            }

            return "AutoCAD " + RecommendedAutoCad.YearText;
        }

        public string GetDesktopAutoCadStatusText()
        {
            if (OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                return "환경 점검 필요";
            }

            if (RecommendedAutoCad == null)
            {
                return "AutoCAD 미감지";
            }

            if (RecommendedAutoCad.IsLT)
            {
                return "AutoCAD LT 미지원";
            }

            if (!IsOviaExtractionPluginSupportedYear(RecommendedAutoCad.Year))
            {
                return "플러그인 준비 필요";
            }

            return IsAutoCadRunning ? "AutoCAD 활성" : "AutoCAD 비활성";
        }

        public string GetDesktopAutoCadDetailText()
        {
            if (OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                if (Issues != null && Issues.Count > 0)
                {
                    return Issues[0].Title + "\r\n" + Issues[0].ActionGuide;
                }

                return "현재 PC 환경에서는 OVIA AutoCAD 연동을 사용할 수 없습니다.";
            }

            if (RecommendedAutoCad == null)
            {
                return "AutoCAD가 아직 설치되지 않았습니다. OVIA는 먼저 설치할 수 있으며 AutoCAD 설치 후 플러그인 번들이 자동 감지됩니다.";
            }

            if (RecommendedAutoCad.IsLT)
            {
                return "AutoCAD LT는 OVIA 플러그인 연동 대상이 아닙니다.";
            }

            if (!IsOviaExtractionPluginSupportedYear(RecommendedAutoCad.Year))
            {
                return RecommendedAutoCad.ProductName + "\r\n" + RecommendedAutoCad.PluginGroup + "\r\n현재 배포본에는 이 버전용 OVIA 추출 플러그인이 아직 포함되지 않았습니다.";
            }

            if (IsAutoCadRunning)
            {
                return RecommendedAutoCad.ProductName + "이(가) 실행 중입니다. OVIA ApplicationPlugins 번들에서 해당 버전 플러그인을 자동 로드합니다.";
            }

            return RecommendedAutoCad.ProductName + "은(는) 감지되었지만 현재 실행 중이 아닙니다.";
        }

        public bool IsCurrentDevelopmentAutoCadReady()
        {
            return OverallStatus != OviaEnvironmentStatus.Blocked
                && RecommendedAutoCad != null
                && !RecommendedAutoCad.IsLT
                && IsOviaExtractionPluginSupportedYear(RecommendedAutoCad.Year)
                && IsAutoCadRunning;
        }

        public string GetAutoCadExtractionBlockMessage()
        {
            if (OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                return "현재 PC 환경에서는 OVIA AutoCAD 추출을 진행할 수 없습니다.";
            }

            if (RecommendedAutoCad == null)
            {
                return "AutoCAD 정식 버전이 감지되지 않았습니다.";
            }

            if (RecommendedAutoCad.IsLT)
            {
                return "현재 감지된 AutoCAD는 LT 버전입니다. OVIA는 AutoCAD 정식 버전에서만 사용할 수 있습니다.";
            }

            if (!IsOviaExtractionPluginSupportedYear(RecommendedAutoCad.Year))
            {
                return "AutoCAD " + RecommendedAutoCad.YearText + "은(는) 감지되었지만 현재 OVIA 설치본에 해당 버전용 추출 플러그인이 포함되어 있지 않습니다.";
            }

            if (!IsAutoCadRunning)
            {
                return "AutoCAD가 설치되어 있지만 현재 실행 중이 아닙니다. AutoCAD를 먼저 실행하고 DWG 도면을 연 뒤 다시 시도해 주세요.";
            }

            return "";
        }

        public string GetAutoCadExtractionReadyMessage()
        {
            if (RecommendedAutoCad == null)
            {
                return "AutoCAD 감지 정보가 없습니다.";
            }

            return RecommendedAutoCad.ProductName + " 활성 상태입니다.\r\n\r\nOVIA 설치 프로그램이 ApplicationPlugins 번들에 배치한 해당 버전 플러그인이 자동 로드되며 OVIABOX / OVIABOXTABLE 명령어를 사용할 수 있습니다.";
        }

        public string GetDisplayText()
        {
            StringBuilder text = new StringBuilder();

            text.AppendLine("OVIA 설치 전 환경 점검 결과");
            text.AppendLine("================================");
            text.AppendLine();
            text.AppendLine("[최종 판단]");
            text.AppendLine(GetInstallerDecisionText());
            text.AppendLine();

            text.AppendLine("[Windows]");
            text.AppendLine("운영체제: " + WindowsName);
            text.AppendLine("버전: " + WindowsVersionText);
            text.AppendLine("64비트 OS: " + (Is64BitOperatingSystem ? "예" : "아니오"));
            text.AppendLine("ARM 계열: " + (IsArmProcessor ? "예" : "아니오"));
            text.AppendLine();

            text.AppendLine("[.NET Framework]");
            text.AppendLine("감지 버전: " + DotNetVersionText);
            text.AppendLine("OVIA Desktop 실행 기준: " + (IsDotNet472OrHigher ? "충족" : "미충족"));
            text.AppendLine("권장 기준 4.8 이상: " + (IsDotNet48OrHigher ? "충족" : "주의"));
            text.AppendLine();

            text.AppendLine("[WebView2 Runtime]");
            text.AppendLine("감지 상태: " + (IsWebView2RuntimeAvailable ? "감지됨" : "미감지"));
            if (IsWebView2RuntimeAvailable && WebView2RuntimeVersionText != "")
            {
                text.AppendLine("버전: " + WebView2RuntimeVersionText);
            }
            text.AppendLine("용도: OVIA Desktop 내부 Web ERP 화면 표시");
            text.AppendLine();

            text.AppendLine("[AutoCAD]");
            text.AppendLine("실행 상태: " + (IsAutoCadRunning ? "acad.exe 실행 중" : "실행 안 됨"));

            if (RecommendedAutoCad == null)
            {
                text.AppendLine("설치 감지: AutoCAD 정식 버전 미감지");
            }
            else
            {
                text.AppendLine("사용 대상: " + RecommendedAutoCad.ProductName);
                text.AppendLine("OVIA 모듈: " + RecommendedAutoCad.PluginGroup);

                if (RecommendedAutoCad.InstallPath != "")
                {
                    text.AppendLine("경로: " + RecommendedAutoCad.InstallPath);
                }

                if (AutoCadInstalls.Count > 1)
                {
                    text.AppendLine("감지된 AutoCAD 정식 버전 수: " + AutoCadInstalls.Count.ToString());
                }
            }

            text.AppendLine();
            text.AppendLine("[OVIA 작업 폴더]");
            text.AppendLine("경로: " + OviaWorkFolder);
            text.AppendLine("쓰기 권한: " + (CanWriteOviaWorkFolder ? "가능" : "불가"));
            text.AppendLine();

            if (Issues.Count > 0)
            {
                text.AppendLine("[확인 필요 항목]");

                int i;

                for (i = 0; i < Issues.Count; i++)
                {
                    OviaEnvironmentIssue issue = Issues[i];
                    text.AppendLine((i + 1).ToString() + ". " + issue.Title);
                    text.AppendLine("   내용: " + issue.Message);

                    if (issue.ActionGuide != "")
                    {
                        text.AppendLine("   조치: " + issue.ActionGuide);
                    }
                }
            }
            else
            {
                text.AppendLine("[확인 필요 항목]");
                text.AppendLine("현재 설치를 막는 문제는 감지되지 않았습니다.");
            }

            return text.ToString();
        }

        public string GetInstallerDecisionText()
        {
            if (OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                return "설치 불가 - 아래 문제를 해결한 뒤 다시 설치해야 합니다.";
            }

            if (OverallStatus == OviaEnvironmentStatus.Warning)
            {
                return "설치 가능 - 단, 일부 항목은 제한적 지원 또는 추가 플러그인 준비가 필요합니다.";
            }

            return "설치 가능 - 현재 PC는 OVIA 권장 실행 조건을 충족합니다.";
        }
    }

    public static class OviaEnvironmentChecker
    {
        private const int DotNet472ReleaseMinimum = 461808;
        private const int DotNet48ReleaseMinimum = 528040;
        private const int Windows10MinimumBuild = 17763;
        private const int Windows10RecommendedBuild = 19045;
        private const int Windows11MinimumBuild = 22000;
        private const int Windows11RecommendedBuild = 26100;

        private static OviaEnvironmentReport cachedReport = null;
        private static DateTime cachedAt = DateTime.MinValue;

        public static OviaEnvironmentReport Check()
        {
            OviaEnvironmentReport report = new OviaEnvironmentReport();

            FillWindowsInfo(report);
            FillDotNetInfo(report);
            FillWebView2Info(report);
            FillAutoCadInfo(report);
            FillStorageInfo(report);
            EvaluateWindows(report);
            EvaluateDotNet(report);
            EvaluateWebView2(report);
            EvaluateAutoCad(report);
            EvaluateStorage(report);
            UpdateOverallStatus(report);

            cachedReport = report;
            cachedAt = DateTime.Now;

            return report;
        }

        public static OviaEnvironmentReport CheckForUi()
        {
            if (cachedReport != null && (DateTime.Now - cachedAt).TotalSeconds < 15)
            {
                bool isAutoCadRunning = AutoCadRuntimeChecker.IsAutoCadRunning();

                // OVIA를 먼저 실행/설치한 뒤 AutoCAD를 나중에 설치한 경우에도
                // 기존 "미감지" 캐시가 15초 동안 추출을 막지 않도록 즉시 재탐색합니다.
                if (isAutoCadRunning && cachedReport.RecommendedAutoCad == null)
                {
                    return Check();
                }

                cachedReport.IsAutoCadRunning = isAutoCadRunning;
                return cachedReport;
            }

            return Check();
        }

        private static void FillWindowsInfo(OviaEnvironmentReport report)
        {
            OviaOsVersionInfo version = OviaWindowsVersionReader.GetVersion();

            report.WindowsMajor = version.Major;
            report.WindowsMinor = version.Minor;
            report.WindowsBuild = version.Build;
            report.Is64BitOperatingSystem = Environment.Is64BitOperatingSystem;
            report.IsArmProcessor = IsArmProcessor();

            if (version.Major == 10 && version.Build >= Windows11MinimumBuild)
            {
                report.WindowsName = "Windows 11";
            }
            else if (version.Major == 10)
            {
                report.WindowsName = "Windows 10";
            }
            else if (version.Major > 0)
            {
                report.WindowsName = "Windows " + version.Major.ToString() + "." + version.Minor.ToString();
            }
            else
            {
                report.WindowsName = "Windows 버전 확인 실패";
            }

            if (version.Build > 0)
            {
                report.WindowsVersionText = version.Major.ToString() + "." + version.Minor.ToString() + "." + version.Build.ToString();
            }
            else
            {
                report.WindowsVersionText = "확인 실패";
            }
        }

        private static void FillDotNetInfo(OviaEnvironmentReport report)
        {
            report.DotNetRelease = ReadDotNetRelease();
            report.DotNetVersionText = GetDotNetVersionText(report.DotNetRelease);
            report.IsDotNet472OrHigher = report.DotNetRelease >= DotNet472ReleaseMinimum;
            report.IsDotNet48OrHigher = report.DotNetRelease >= DotNet48ReleaseMinimum;
        }

        private static void FillWebView2Info(OviaEnvironmentReport report)
        {
            OviaWebView2RuntimeInfo runtime = OviaWebView2RuntimeChecker.GetRuntimeInfo();
            report.IsWebView2RuntimeAvailable = runtime != null && runtime.IsAvailable;
            report.WebView2RuntimeVersionText = runtime == null || runtime.VersionText == null ? "" : runtime.VersionText;
        }

        private static void FillAutoCadInfo(OviaEnvironmentReport report)
        {
            report.IsAutoCadRunning = AutoCadRuntimeChecker.IsAutoCadRunning();
            report.AutoCadInstalls = AutoCadDetector.FindInstalledAutoCad();
            report.RecommendedAutoCad = SelectRecommendedAutoCad(report.AutoCadInstalls);
        }

        private static void FillStorageInfo(OviaEnvironmentReport report)
        {
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (baseFolder == null || baseFolder.Trim() == "")
            {
                report.OviaWorkFolder = "";
                report.CanWriteOviaWorkFolder = false;
                return;
            }

            report.OviaWorkFolder = Path.Combine(baseFolder, "OVIA");
            report.CanWriteOviaWorkFolder = CanWriteFolder(report.OviaWorkFolder);
        }

        private static void EvaluateWindows(OviaEnvironmentReport report)
        {
            if (!report.Is64BitOperatingSystem)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    "64비트 Windows가 아닙니다.",
                    "OVIA와 AutoCAD 연동 모듈은 64비트 Windows 기준으로 지원됩니다.",
                    "64비트 Windows 10 22H2 이상 또는 Windows 11 PC에서 설치해 주세요."
                );
            }

            if (report.IsArmProcessor)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    "ARM 기반 Windows 환경입니다.",
                    "AutoCAD 데스크톱 연동 안정성을 보장하기 어렵습니다.",
                    "Intel 또는 AMD x64 프로세서 기반 Windows PC를 사용해 주세요."
                );
            }

            if (report.WindowsMajor < 10)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    "지원하지 않는 Windows 버전입니다.",
                    "OVIA는 Windows 10 이상을 기준으로 개발됩니다.",
                    "Windows 11 24H2 이상을 권장합니다."
                );
                return;
            }

            if (report.WindowsMajor == 10 && report.WindowsBuild < Windows10MinimumBuild)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    "Windows 10 버전이 너무 낮습니다.",
                    "AutoCAD 2021~2027 연동 기준으로 안정적인 지원이 어렵습니다.",
                    "Windows 10 22H2 또는 Windows 11로 업데이트해 주세요."
                );
                return;
            }

            if (report.WindowsMajor == 10 && report.WindowsBuild >= Windows11MinimumBuild && report.WindowsBuild < Windows11RecommendedBuild)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "Windows 11 권장 버전보다 낮을 수 있습니다.",
                    "OVIA 공식 권장 기준은 Windows 11 24H2 이상입니다.",
                    "가능하면 Windows 11 24H2 이상에서 사용해 주세요."
                );
                return;
            }

            if (report.WindowsMajor == 10 && report.WindowsBuild < Windows11MinimumBuild && report.WindowsBuild < Windows10RecommendedBuild)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "Windows 10 22H2 미만으로 보입니다.",
                    "Windows 10 지원 범위는 22H2 기준으로 제한하는 것이 안전합니다.",
                    "Windows 10 22H2 또는 Windows 11로 업데이트해 주세요."
                );
            }

            if (report.WindowsMajor == 10 && report.WindowsBuild < Windows11MinimumBuild && report.WindowsBuild >= Windows10RecommendedBuild)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "Windows 10 제한적 지원 환경입니다.",
                    "OVIA는 Windows 11을 권장하며 Windows 10은 현장 호환성 목적의 제한적 지원으로 보는 것이 안전합니다.",
                    "장기적으로 Windows 11 PC 사용을 권장합니다."
                );
            }
        }

        private static void EvaluateDotNet(OviaEnvironmentReport report)
        {
            if (!report.IsDotNet472OrHigher)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    ".NET Framework 4.7.2 이상이 감지되지 않았습니다.",
                    "OVIA Desktop 실행에 필요한 .NET Framework 기준을 충족하지 못했습니다.",
                    ".NET Framework 4.8 이상을 설치한 뒤 다시 시도해 주세요."
                );
                return;
            }

            if (!report.IsDotNet48OrHigher)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    ".NET Framework 4.8 미만 환경입니다.",
                    "OVIA Desktop은 4.7.2 기준으로 실행 가능하지만 배포 환경은 4.8 이상을 권장합니다.",
                    ".NET Framework 4.8 이상 설치를 권장합니다."
                );
            }
        }

        private static void EvaluateWebView2(OviaEnvironmentReport report)
        {
            if (!report.IsWebView2RuntimeAvailable)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "WebView2 Runtime이 감지되지 않았습니다.",
                    "OVIA가 Web ERP 화면을 내부 WebView2로 표시하려면 Microsoft Edge WebView2 Runtime이 필요합니다.",
                    "OVIA 설치 프로그램에서 WebView2 Runtime 존재 여부를 확인하고, 없으면 Evergreen Runtime 설치를 연결해야 합니다."
                );
            }
        }

        private static void EvaluateAutoCad(OviaEnvironmentReport report)
        {
            if (report.AutoCadInstalls == null || report.AutoCadInstalls.Count == 0)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "AutoCAD 정식 버전이 아직 감지되지 않았습니다.",
                    "OVIA Desktop은 먼저 설치할 수 있습니다. CAD 추출 기능만 AutoCAD 설치 전까지 사용할 수 없습니다.",
                    "AutoCAD를 나중에 설치해도 OVIA ApplicationPlugins 번들이 시작 시 자동 감지됩니다. AutoCAD LT는 지원하지 않습니다."
                );
                return;
            }

            if (report.RecommendedAutoCad == null)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "지원 가능한 AutoCAD 정식 버전을 찾지 못했습니다.",
                    "AutoCAD LT 또는 지원 정책 밖의 버전만 감지되었습니다.",
                    "AutoCAD 2021~2027 정식 버전 사용을 권장합니다."
                );
                return;
            }

            if (report.RecommendedAutoCad.IsLT)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "AutoCAD LT는 지원하지 않습니다.",
                    "OVIA는 AutoCAD .NET API 기반 플러그인 로드가 필요합니다.",
                    "AutoCAD 정식 버전을 사용해 주세요."
                );
                return;
            }

            if (report.RecommendedAutoCad.Year == 2027)
            {
                if (report.WindowsName == "Windows 10")
                {
                    AddIssue(
                        report,
                        OviaEnvironmentStatus.Warning,
                        "AutoCAD 2027 + Windows 10 조합입니다.",
                        "AutoCAD 2027은 Windows 11 환경을 기준으로 보는 것이 안전합니다.",
                        "AutoCAD 2027 사용자는 Windows 11 PC를 권장합니다."
                    );
                }

                return;
            }

            if (report.RecommendedAutoCad.Year == 2024)
            {
                // AutoCAD 2024용 .NET Framework 4.8 OVIA 플러그인 프로젝트/번들 배포가 준비되어 있습니다.
                return;
            }

            if (report.RecommendedAutoCad.Year >= 2025 && report.RecommendedAutoCad.Year <= 2026)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "AutoCAD 2025~2026 전용 OVIA 플러그인이 필요합니다.",
                    "AutoCAD 2025~2026은 .NET 8 계열의 별도 OVIA 플러그인이 필요합니다.",
                    "해당 버전용 플러그인 프로젝트를 추가한 뒤 같은 ApplicationPlugins 번들에 배포해야 합니다."
                );
                return;
            }

            if (report.RecommendedAutoCad.Year >= 2021 && report.RecommendedAutoCad.Year <= 2023)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Warning,
                    "AutoCAD 2021~2023용 OVIA 플러그인이 아직 필요합니다.",
                    "현재 실제 배포 준비가 완료된 .NET Framework 4.8 플러그인은 AutoCAD 2024 대상입니다.",
                    "2021~2023 지원 시 해당 릴리즈 API 기준 프로젝트를 같은 공유 추출소스에서 추가해야 합니다."
                );
                return;
            }

            AddIssue(
                report,
                OviaEnvironmentStatus.Warning,
                "지원 정책 밖의 AutoCAD 버전입니다.",
                "감지된 AutoCAD 버전에 대한 OVIA 플러그인 정책이 아직 확정되지 않았습니다.",
                "AutoCAD 2021~2027 정식 버전 사용을 권장합니다."
            );
        }

        private static void EvaluateStorage(OviaEnvironmentReport report)
        {
            if (!report.CanWriteOviaWorkFolder)
            {
                AddIssue(
                    report,
                    OviaEnvironmentStatus.Blocked,
                    "OVIA 작업 폴더 쓰기 권한이 없습니다.",
                    "CSV 추출 파일, 설정 파일, 임시 진단 파일 저장에 실패할 수 있습니다.",
                    "%LOCALAPPDATA%\\OVIA 경로 생성 및 쓰기 권한을 확인해 주세요."
                );
            }
        }

        private static void UpdateOverallStatus(OviaEnvironmentReport report)
        {
            OviaEnvironmentStatus status = OviaEnvironmentStatus.Supported;
            int i;

            for (i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Status == OviaEnvironmentStatus.Blocked)
                {
                    status = OviaEnvironmentStatus.Blocked;
                    break;
                }

                if (report.Issues[i].Status == OviaEnvironmentStatus.Warning)
                {
                    status = OviaEnvironmentStatus.Warning;
                }
            }

            report.OverallStatus = status;
        }

        private static void AddIssue(OviaEnvironmentReport report, OviaEnvironmentStatus status, string title, string message, string actionGuide)
        {
            OviaEnvironmentIssue issue = new OviaEnvironmentIssue();
            issue.Status = status;
            issue.Title = title;
            issue.Message = message;
            issue.ActionGuide = actionGuide;
            report.Issues.Add(issue);
        }

        private static AutoCadInstallInfo SelectRecommendedAutoCad(List<AutoCadInstallInfo> installs)
        {
            if (installs == null || installs.Count == 0)
            {
                return null;
            }

            int i;

            for (i = 0; i < installs.Count; i++)
            {
                if (!installs[i].IsLT && installs[i].Year >= 2021 && installs[i].Year <= 2027)
                {
                    return installs[i];
                }
            }

            for (i = 0; i < installs.Count; i++)
            {
                if (!installs[i].IsLT)
                {
                    return installs[i];
                }
            }

            return installs[0];
        }

        private static bool CanWriteFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string testFile = Path.Combine(folder, "ovia_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(testFile, "OVIA");
                File.Delete(testFile);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadDotNetRelease()
        {
            try
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");

                if (key == null)
                {
                    return 0;
                }

                object value = key.GetValue("Release");
                key.Close();

                if (value == null)
                {
                    return 0;
                }

                int release = 0;
                int.TryParse(value.ToString(), out release);

                return release;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetDotNetVersionText(int release)
        {
            if (release >= 533320)
            {
                return ".NET Framework 4.8.1 이상 (Release " + release.ToString() + ")";
            }

            if (release >= DotNet48ReleaseMinimum)
            {
                return ".NET Framework 4.8 이상 (Release " + release.ToString() + ")";
            }

            if (release >= DotNet472ReleaseMinimum)
            {
                return ".NET Framework 4.7.2 이상 (Release " + release.ToString() + ")";
            }

            if (release > 0)
            {
                return "Release " + release.ToString();
            }

            return "확인 실패";
        }

        private static bool IsArmProcessor()
        {
            string arch = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            string archWow = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? "").ToUpperInvariant();

            return arch.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0
                || archWow.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class OviaOsVersionInfo
    {
        public int Major = 0;
        public int Minor = 0;
        public int Build = 0;
    }

    public static class OviaWindowsVersionReader
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOEX
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX versionInfo);

        public static OviaOsVersionInfo GetVersion()
        {
            OviaOsVersionInfo result = new OviaOsVersionInfo();

            try
            {
                RTL_OSVERSIONINFOEX versionInfo = new RTL_OSVERSIONINFOEX();
                versionInfo.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOEX));

                int status = RtlGetVersion(ref versionInfo);

                if (status == 0)
                {
                    result.Major = (int)versionInfo.dwMajorVersion;
                    result.Minor = (int)versionInfo.dwMinorVersion;
                    result.Build = (int)versionInfo.dwBuildNumber;
                    return result;
                }
            }
            catch
            {
            }

            Version fallback = Environment.OSVersion.Version;
            result.Major = fallback.Major;
            result.Minor = fallback.Minor;
            result.Build = fallback.Build;

            return result;
        }
    }
}
