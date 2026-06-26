using System;
using System.Collections.Generic;
using System.IO;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA 시스템 설정/정보 화면에서 공통으로 사용하는 얇은 호환 래퍼입니다.
    ///
    /// 일부 화면(Form1 등)에서 버전/라이선스 페이지 추가 과정 중
    /// OviaSystemSettingsStore 이름을 참조하도록 변경된 경우가 있어,
    /// 실제 저장소인 OviaVersionInfoStore / OviaLicenseStore와 연결합니다.
    /// </summary>
    internal static class OviaSystemSettingsStore
    {
        public static OviaVersionInfoData Load()
        {
            return LoadVersionInfo();
        }

        public static void Save(OviaVersionInfoData data)
        {
            SaveVersionInfo(data);
        }

        public static OviaVersionInfoData LoadVersionInfo()
        {
            try
            {
                return OviaVersionInfoStore.Load();
            }
            catch
            {
                return CreateFallbackVersionInfo();
            }
        }

        public static OviaVersionInfoData GetVersionInfo()
        {
            return LoadVersionInfo();
        }

        public static void SaveVersionInfo(OviaVersionInfoData data)
        {
            if (data == null)
            {
                data = CreateFallbackVersionInfo();
            }

            OviaVersionInfoStore.Save(data);
        }

        public static List<OviaLicenseEntry> LoadLicenses()
        {
            try
            {
                return OviaLicenseStore.Load();
            }
            catch
            {
                return new List<OviaLicenseEntry>();
            }
        }

        public static void SaveLicenses(List<OviaLicenseEntry> entries)
        {
            if (entries == null)
            {
                entries = new List<OviaLicenseEntry>();
            }

            OviaLicenseStore.Save(entries);
        }


        public static string GetDisplayVersionText()
        {
            return GetVersionText();
        }

        public static string GetConfiguredCompanyLogoPath()
        {
            return GetConfiguredCompanyLogoPath(string.Empty);
        }

        public static string GetConfiguredCompanyLogoPath(string companyId)
        {
            try
            {
                List<string> candidates = new List<string>();

                string safeCompanyId = SafeFilePart(companyId);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string systemDir = Path.Combine(appData, "OVIA", "System");

                if (!string.IsNullOrWhiteSpace(safeCompanyId))
                {
                    candidates.Add(Path.Combine(systemDir, "company_logo_" + safeCompanyId + ".png"));
                    candidates.Add(Path.Combine(systemDir, safeCompanyId + "_logo.png"));
                }

                candidates.Add(Path.Combine(systemDir, "company_logo.png"));
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ovia_logo.png"));
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "ovia_logo.png"));

                foreach (string path in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // 로그인 화면의 로고는 부가 정보이므로, 오류가 나도 로그인 흐름을 막지 않는다.
            }

            return string.Empty;
        }

        public static string VersionText
        {
            get { return GetVersionText(); }
        }

        public static string ProductName
        {
            get { return SafeText(LoadVersionInfo().ProductName, "OVIA / 오비아"); }
        }

        public static string ReleaseDate
        {
            get { return SafeText(LoadVersionInfo().ReleaseDate, DateTime.Now.ToString("yyyy-MM-dd")); }
        }

        public static string BuildMode
        {
            get { return SafeText(LoadVersionInfo().BuildMode, "개발/테스트 버전"); }
        }

        public static string Description
        {
            get { return SafeText(LoadVersionInfo().Description, string.Empty); }
        }

        public static string GetVersionText()
        {
            OviaVersionInfoData data = LoadVersionInfo();
            string version = SafeText(data.Version, "개발 버전");

            if (version.StartsWith("Version ", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }

            return "Version " + version;
        }

        public static string GetPlainVersionText()
        {
            return SafeText(LoadVersionInfo().Version, "개발 버전");
        }

        public static string GetProductName()
        {
            return ProductName;
        }

        public static string GetReleaseDate()
        {
            return ReleaseDate;
        }

        public static string GetBuildMode()
        {
            return BuildMode;
        }

        public static string GetDescription()
        {
            return Description;
        }


        private static string SafeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = value.Trim();
            foreach (char ch in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(ch.ToString(), string.Empty);
            }

            return result;
        }

        private static string SafeText(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim();
        }

        private static OviaVersionInfoData CreateFallbackVersionInfo()
        {
            OviaVersionInfoData data = new OviaVersionInfoData();
            data.ProductName = "OVIA / 오비아";
            data.Version = "개발 버전";
            data.ReleaseDate = DateTime.Now.ToString("yyyy-MM-dd");
            data.BuildMode = "개발/테스트 버전";
            data.Description = "Operation + Value + Intelligence + Automation";
            return data;
        }
    }
}
