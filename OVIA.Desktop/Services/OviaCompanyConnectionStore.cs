using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    /// <summary>
    /// 기업아이디별 ERP 연결정보입니다.
    /// 비밀번호/사용자 계정은 저장하지 않으며 ERP 접속에 필요한 공개 연결정보만 보관합니다.
    /// </summary>
    public sealed class OviaCompanyConnectionProfile
    {
        public string CompanyId = "";
        public string ErpBaseDomain = "";
        public string ErpConnectionPath = "";
        public string ErpAuthPath = "";
    }

    /// <summary>
    /// OVIA 최초 실행 및 다중 기업 로그인을 위한 기업별 연결정보 저장소입니다.
    /// 저장 위치: %LOCALAPPDATA%\OVIA\Connections\{기업아이디}.ini
    ///
    /// 파일은 사용자가 내용을 모두 지우거나 파일 자체를 삭제하면 미등록 상태로 판단합니다.
    /// 기업별 ERP 연결정보만 저장하며 ERP 사용자 아이디/비밀번호는 절대 저장하지 않습니다.
    /// </summary>
    public static class OviaCompanyConnectionStore
    {
        private const string ConnectionFolderName = "Connections";
        private const string ConnectionFileExtension = ".ini";

        public static bool IsValidCompanyId(string companyId)
        {
            string value = NormalizeCompanyId(companyId);
            return value != "" && Regex.IsMatch(value, "^[A-Za-z0-9_-]+$");
        }

        public static string NormalizeCompanyId(string companyId)
        {
            return companyId == null ? "" : companyId.Trim();
        }

        public static bool HasAnyProfile()
        {
            try
            {
                string folder = GetConnectionFolder();
                if (!Directory.Exists(folder))
                {
                    return false;
                }

                string[] files = Directory.GetFiles(folder, "*" + ConnectionFileExtension, SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    OviaCompanyConnectionProfile profile;
                    if (TryLoadFromPath(files[i], out profile))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool Exists(string companyId)
        {
            OviaCompanyConnectionProfile profile;
            return TryLoad(companyId, out profile);
        }

        public static bool TryLoad(string companyId, out OviaCompanyConnectionProfile profile)
        {
            profile = null;
            string normalizedCompanyId = NormalizeCompanyId(companyId);
            if (!IsValidCompanyId(normalizedCompanyId))
            {
                return false;
            }

            try
            {
                string path = GetProfilePath(normalizedCompanyId);
                if (!File.Exists(path))
                {
                    return false;
                }

                OviaCompanyConnectionProfile loaded;
                if (!TryLoadFromPath(path, out loaded))
                {
                    return false;
                }

                if (!string.Equals(loaded.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                profile = Clone(loaded);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static OviaCompanyConnectionProfile Load(string companyId)
        {
            OviaCompanyConnectionProfile profile;
            return TryLoad(companyId, out profile) ? profile : null;
        }

        public static void Save(OviaCompanyConnectionProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            OviaCompanyConnectionProfile normalized = NormalizeProfile(profile);
            ValidateProfile(normalized);

            string folder = GetConnectionFolder();
            Directory.CreateDirectory(folder);

            string path = GetProfilePath(normalized.CompanyId);
            string tempPath = path + ".tmp";

            string[] lines = new string[]
            {
                "; OVIA Connection",
                "; 이 파일의 내용을 모두 지우거나 파일을 삭제하면 해당 기업은 다시 OVIA Connection 설정이 필요합니다.",
                "; ERP 사용자 아이디와 비밀번호는 이 파일에 저장하지 않습니다.",
                "[OVIA Connection]",
                "CompanyId=" + normalized.CompanyId,
                "ErpBaseDomain=" + normalized.ErpBaseDomain,
                "ErpConnectionPath=" + normalized.ErpConnectionPath,
                "ErpAuthPath=" + normalized.ErpAuthPath
            };

            File.WriteAllLines(tempPath, lines, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }

        public static void Delete(string companyId)
        {
            string normalizedCompanyId = NormalizeCompanyId(companyId);
            if (!IsValidCompanyId(normalizedCompanyId))
            {
                return;
            }

            try
            {
                string path = GetProfilePath(normalizedCompanyId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public static string GetErpBaseDomain(string companyId)
        {
            OviaCompanyConnectionProfile profile;
            return TryLoad(companyId, out profile) ? profile.ErpBaseDomain : "";
        }

        public static string GetErpConnectionUrl(string companyId)
        {
            OviaCompanyConnectionProfile profile;
            if (!TryLoad(companyId, out profile))
            {
                return "";
            }

            OviaSystemSettings settings = ToSystemSettings(profile);
            return OviaSystemSettingsStore.BuildErpConnectionUrl(settings);
        }

        public static string GetErpAuthUrl(string companyId)
        {
            OviaCompanyConnectionProfile profile;
            if (!TryLoad(companyId, out profile))
            {
                return "";
            }

            OviaSystemSettings settings = ToSystemSettings(profile);
            return OviaSystemSettingsStore.BuildErpAuthUrl(settings);
        }

        public static string GetProfilePath(string companyId)
        {
            string normalizedCompanyId = NormalizeCompanyId(companyId);
            if (!IsValidCompanyId(normalizedCompanyId))
            {
                return "";
            }

            return Path.Combine(GetConnectionFolder(), normalizedCompanyId + ConnectionFileExtension);
        }

        public static string GetConnectionFolder()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "OVIA", ConnectionFolderName);
        }

        private static bool TryLoadFromPath(string path, out OviaCompanyConnectionProfile profile)
        {
            profile = null;

            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                OviaCompanyConnectionProfile loaded = new OviaCompanyConnectionProfile();

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] == null ? "" : lines[i].Trim();
                    if (line == "" || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("[", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (key.Equals("CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.CompanyId = value;
                    }
                    else if (key.Equals("ErpBaseDomain", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.ErpBaseDomain = value;
                    }
                    else if (key.Equals("ErpConnectionPath", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.ErpConnectionPath = value;
                    }
                    else if (key.Equals("ErpAuthPath", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.ErpAuthPath = value;
                    }
                }

                loaded = NormalizeProfile(loaded);
                if (!IsCompleteProfile(loaded))
                {
                    return false;
                }

                profile = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static OviaCompanyConnectionProfile NormalizeProfile(OviaCompanyConnectionProfile source)
        {
            OviaCompanyConnectionProfile profile = new OviaCompanyConnectionProfile();
            profile.CompanyId = NormalizeCompanyId(source == null ? "" : source.CompanyId);
            string rawDomain = source == null || source.ErpBaseDomain == null ? "" : source.ErpBaseDomain.Trim();
            profile.ErpBaseDomain = rawDomain == "" ? "" : OviaSystemSettingsStore.NormalizeErpBaseDomain(rawDomain);
            profile.ErpConnectionPath = OviaSystemSettingsStore.NormalizeErpPath(source == null ? "" : source.ErpConnectionPath, "");
            profile.ErpAuthPath = OviaSystemSettingsStore.NormalizeErpPath(source == null ? "" : source.ErpAuthPath, "");
            return profile;
        }

        private static bool IsCompleteProfile(OviaCompanyConnectionProfile profile)
        {
            if (profile == null || !IsValidCompanyId(profile.CompanyId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(profile.ErpBaseDomain)
                || string.IsNullOrWhiteSpace(profile.ErpConnectionPath)
                || string.IsNullOrWhiteSpace(profile.ErpAuthPath))
            {
                return false;
            }

            Uri domainUri;
            if (!Uri.TryCreate(profile.ErpBaseDomain, UriKind.Absolute, out domainUri))
            {
                return false;
            }

            return domainUri.Scheme == Uri.UriSchemeHttp || domainUri.Scheme == Uri.UriSchemeHttps;
        }

        private static void ValidateProfile(OviaCompanyConnectionProfile profile)
        {
            if (!IsValidCompanyId(profile.CompanyId))
            {
                throw new InvalidOperationException("기업 아이디는 영문, 숫자, 하이픈(-), 밑줄(_)만 사용할 수 있습니다.");
            }

            if (string.IsNullOrWhiteSpace(profile.ErpConnectionPath))
            {
                throw new InvalidOperationException("ERP 연결 URL을 입력해 주세요.");
            }

            if (string.IsNullOrWhiteSpace(profile.ErpAuthPath))
            {
                throw new InvalidOperationException("ERP 사용자 인증을 입력해 주세요.");
            }

            Uri domainUri;
            if (!Uri.TryCreate(profile.ErpBaseDomain, UriKind.Absolute, out domainUri)
                || (domainUri.Scheme != Uri.UriSchemeHttp && domainUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("ERP 기본 도메인은 http:// 또는 https:// 형식의 주소로 입력해 주세요.");
            }
        }

        private static OviaSystemSettings ToSystemSettings(OviaCompanyConnectionProfile profile)
        {
            OviaSystemSettings settings = new OviaSystemSettings();
            settings.ErpBaseDomain = profile.ErpBaseDomain;
            settings.ErpConnectionPath = profile.ErpConnectionPath;
            settings.ErpAuthPath = profile.ErpAuthPath;
            return settings;
        }

        private static OviaCompanyConnectionProfile Clone(OviaCompanyConnectionProfile source)
        {
            OviaCompanyConnectionProfile clone = new OviaCompanyConnectionProfile();
            if (source != null)
            {
                clone.CompanyId = source.CompanyId == null ? "" : source.CompanyId;
                clone.ErpBaseDomain = source.ErpBaseDomain == null ? "" : source.ErpBaseDomain;
                clone.ErpConnectionPath = source.ErpConnectionPath == null ? "" : source.ErpConnectionPath;
                clone.ErpAuthPath = source.ErpAuthPath == null ? "" : source.ErpAuthPath;
            }
            return clone;
        }
    }
}
