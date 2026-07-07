using System;
using System.Text;
using OVIA.Desktop;

namespace OVIA.PreflightCheck
{
    internal static class Program
    {
        private const int ExitSupported = 0;
        private const int ExitWarning = 1;
        private const int ExitBlocked = 2;
        private const int ExitError = 9;

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;

                bool json = HasArg(args, "--json");
                bool silent = HasArg(args, "--silent");
                bool pause = HasArg(args, "--pause");
                bool help = HasArg(args, "--help") || HasArg(args, "/?");

                if (help)
                {
                    PrintHelp();
                    return ExitSupported;
                }

                OviaEnvironmentReport report = OviaEnvironmentChecker.Check();

                if (!silent)
                {
                    if (json)
                    {
                        Console.WriteLine(ToJson(report));
                    }
                    else
                    {
                        Console.WriteLine(report.GetDisplayText());
                        Console.WriteLine();
                        Console.WriteLine("[설치 프로그램 연동용 종료 코드]");
                        Console.WriteLine("0 = 지원 가능, 1 = 제한적 지원, 2 = 설치 차단, 9 = 점검 오류");
                        Console.WriteLine("현재 종료 코드: " + GetExitCode(report).ToString());
                    }
                }

                if (pause)
                {
                    Console.WriteLine();
                    Console.WriteLine("아무 키나 누르면 종료합니다.");
                    Console.ReadKey(true);
                }

                return GetExitCode(report);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("OVIA 환경 점검 중 오류가 발생했습니다.");
                Console.Error.WriteLine(ex.Message);
                return ExitError;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            int i;

            if (args == null)
            {
                return false;
            }

            for (i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("OVIA.PreflightCheck");
            Console.WriteLine();
            Console.WriteLine("설치 전 Windows / AutoCAD / .NET / 권한 환경을 점검하는 콘솔 도구입니다.");
            Console.WriteLine();
            Console.WriteLine("사용법:");
            Console.WriteLine("  OVIA.PreflightCheck.exe");
            Console.WriteLine("  OVIA.PreflightCheck.exe --json");
            Console.WriteLine("  OVIA.PreflightCheck.exe --silent");
            Console.WriteLine("  OVIA.PreflightCheck.exe --pause");
            Console.WriteLine();
            Console.WriteLine("종료 코드:");
            Console.WriteLine("  0 = 지원 가능");
            Console.WriteLine("  1 = 제한적 지원");
            Console.WriteLine("  2 = 설치 차단");
            Console.WriteLine("  9 = 점검 오류");
        }

        private static int GetExitCode(OviaEnvironmentReport report)
        {
            if (report == null)
            {
                return ExitError;
            }

            if (report.OverallStatus == OviaEnvironmentStatus.Blocked)
            {
                return ExitBlocked;
            }

            if (report.OverallStatus == OviaEnvironmentStatus.Warning)
            {
                return ExitWarning;
            }

            return ExitSupported;
        }

        private static string ToJson(OviaEnvironmentReport report)
        {
            StringBuilder json = new StringBuilder();
            int i;

            json.Append("{");
            AppendJsonProperty(json, "overallStatus", report.OverallStatus.ToString(), true);
            AppendJsonProperty(json, "overallStatusText", report.OverallStatusText, true);
            AppendJsonProperty(json, "installerDecisionText", report.GetInstallerDecisionText(), true);
            AppendJsonProperty(json, "windowsName", report.WindowsName, true);
            AppendJsonProperty(json, "windowsVersionText", report.WindowsVersionText, true);
            AppendJsonProperty(json, "windowsBuild", report.WindowsBuild.ToString(), false);
            AppendJsonProperty(json, "is64BitOperatingSystem", report.Is64BitOperatingSystem ? "true" : "false", false);
            AppendJsonProperty(json, "isArmProcessor", report.IsArmProcessor ? "true" : "false", false);
            AppendJsonProperty(json, "dotNetRelease", report.DotNetRelease.ToString(), false);
            AppendJsonProperty(json, "dotNetVersionText", report.DotNetVersionText, true);
            AppendJsonProperty(json, "isDotNet472OrHigher", report.IsDotNet472OrHigher ? "true" : "false", false);
            AppendJsonProperty(json, "isDotNet48OrHigher", report.IsDotNet48OrHigher ? "true" : "false", false);
            AppendJsonProperty(json, "isWebView2RuntimeAvailable", report.IsWebView2RuntimeAvailable ? "true" : "false", false);
            AppendJsonProperty(json, "webView2RuntimeVersionText", report.WebView2RuntimeVersionText, true);
            AppendJsonProperty(json, "isAutoCadRunning", report.IsAutoCadRunning ? "true" : "false", false);
            AppendJsonProperty(json, "canWriteOviaWorkFolder", report.CanWriteOviaWorkFolder ? "true" : "false", false);
            AppendJsonProperty(json, "oviaWorkFolder", report.OviaWorkFolder, true);

            json.Append("\"detectedAutoCad\":[");
            for (i = 0; i < report.AutoCadInstalls.Count; i++)
            {
                AutoCadInstallInfo info = report.AutoCadInstalls[i];

                if (i > 0)
                {
                    json.Append(",");
                }

                json.Append("{");
                AppendJsonProperty(json, "productName", info.ProductName, true);
                AppendJsonProperty(json, "year", info.Year.ToString(), false);
                AppendJsonProperty(json, "isLT", info.IsLT ? "true" : "false", false);
                AppendJsonProperty(json, "pluginGroup", info.PluginGroup, true);
                AppendJsonProperty(json, "installPath", info.InstallPath, true, true);
                json.Append("}");
            }
            json.Append("],");

            json.Append("\"recommendedAutoCad\":");
            if (report.RecommendedAutoCad == null)
            {
                json.Append("null,");
            }
            else
            {
                json.Append("{");
                AppendJsonProperty(json, "productName", report.RecommendedAutoCad.ProductName, true);
                AppendJsonProperty(json, "year", report.RecommendedAutoCad.Year.ToString(), false);
                AppendJsonProperty(json, "isLT", report.RecommendedAutoCad.IsLT ? "true" : "false", false);
                AppendJsonProperty(json, "pluginGroup", report.RecommendedAutoCad.PluginGroup, true, true);
                json.Append("},");
            }

            json.Append("\"issues\":[");
            for (i = 0; i < report.Issues.Count; i++)
            {
                OviaEnvironmentIssue issue = report.Issues[i];

                if (i > 0)
                {
                    json.Append(",");
                }

                json.Append("{");
                AppendJsonProperty(json, "status", issue.Status.ToString(), true);
                AppendJsonProperty(json, "title", issue.Title, true);
                AppendJsonProperty(json, "message", issue.Message, true);
                AppendJsonProperty(json, "actionGuide", issue.ActionGuide, true, true);
                json.Append("}");
            }
            json.Append("]");
            json.Append("}");

            return json.ToString();
        }

        private static void AppendJsonProperty(StringBuilder json, string name, string value, bool quoteValue)
        {
            AppendJsonProperty(json, name, value, quoteValue, false);
        }

        private static void AppendJsonProperty(StringBuilder json, string name, string value, bool quoteValue, bool lastInObject)
        {
            json.Append("\"");
            json.Append(EscapeJson(name));
            json.Append("\":");

            if (quoteValue)
            {
                json.Append("\"");
                json.Append(EscapeJson(value));
                json.Append("\"");
            }
            else
            {
                if (value == null || value == "")
                {
                    json.Append("null");
                }
                else
                {
                    json.Append(value);
                }
            }

            if (!lastInObject)
            {
                json.Append(",");
            }
        }

        private static string EscapeJson(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
