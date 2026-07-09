using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using OVIA.Desktop.Controls;

namespace OVIA.Desktop
{
    public class OviaSystemSettings
    {
        public string ErpLoginUrl = "";
        public string ErpBaseDomain = OviaSystemSettingsStore.DefaultErpBaseDomain;
        public string ErpConnectionPath = OviaSystemSettingsStore.DefaultErpConnectionPath;
        public string ErpAuthPath = OviaSystemSettingsStore.DefaultErpAuthPath;
        public string ErpModuleBasePath = OviaSystemSettingsStore.DefaultErpModuleBasePath;
        public string CompanyLogoFilePath = "";
        public string VersionText = "";
        public int ListPageSize = 100;
        public string BrandPrimaryHex = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
        public string BrandHoverHex = OviaSystemSettingsStore.DefaultBrandHoverHex;
        public string LoadingAnimationImagePath = "";
        public int LoadingDelayUnit = OviaSystemSettingsStore.DefaultLoadingDelayUnit;
    }

    public static class OviaSystemSettingsStore
    {
        private const string SettingsFileName = "system_settings.dat";
        public const string DefaultErpBaseDomain = "https://dev03.celmon.com";
        public const string DefaultErpConnectionPath = "/erp";
        public const string DefaultErpAuthPath = "/auth";
        public const string DefaultErpModuleBasePath = "/erpo/?mid=";
        public const string DefaultBrandPrimaryHex = "#2563EB";
        public const string DefaultBrandHoverHex = "#1D4ED8";
        public const int DefaultLoadingDelayUnit = 35;
        public const int MinLoadingDelayUnit = 0;
        public const int MaxLoadingDelayUnit = 600;

        private static readonly object SyncRoot = new object();
        private static bool cacheLoaded = false;
        private static OviaSystemSettings cachedSettings = null;

        public static bool IsSuperAdminUser(string userId)
        {
            return IsSystemAdministrator(string.Empty, userId);
        }

        public static bool IsSystemAdministrator(string companyId, string userId)
        {
            return OviaSessionSecurity.IsCurrentSystemAdministrator(companyId, userId);
        }

        public static OviaSystemSettings Load()
        {
            lock (SyncRoot)
            {
                if (cacheLoaded && cachedSettings != null)
                {
                    return Clone(cachedSettings);
                }

                OviaSystemSettings settings = ReadSettingsFile();
                NormalizeSettings(settings);
                cachedSettings = Clone(settings);
                cacheLoaded = true;
                return Clone(settings);
            }
        }

        private static OviaSystemSettings ReadSettingsFile()
        {
            OviaSystemSettings settings = new OviaSystemSettings();
            string path = GetSettingsFilePath();

            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int i;

                for (i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] == null ? "" : lines[i];
                    int index = line.IndexOf('=');

                    if (index <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, index).Trim();
                    string value = Decode(line.Substring(index + 1));

                    if (key.Equals("ErpLoginUrl", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ErpLoginUrl = value;
                    }
                    else if (key.Equals("ErpBaseDomain", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ErpBaseDomain = value;
                    }
                    else if (key.Equals("ErpConnectionPath", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ErpConnectionPath = value;
                    }
                    else if (key.Equals("ErpAuthPath", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ErpAuthPath = value;
                    }
                    else if (key.Equals("ErpModuleBasePath", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ErpModuleBasePath = value;
                    }
                    else if (key.Equals("CompanyLogoFilePath", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.CompanyLogoFilePath = value;
                    }
                    else if (key.Equals("VersionText", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.VersionText = NormalizeVersionText(value);
                    }
                    else if (key.Equals("ListPageSize", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.ListPageSize = NormalizeListPageSize(value);
                    }
                    else if (key.Equals("BrandPrimaryHex", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.BrandPrimaryHex = NormalizeHexColor(value, DefaultBrandPrimaryHex);
                    }
                    else if (key.Equals("BrandHoverHex", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.BrandHoverHex = NormalizeHexColor(value, DefaultBrandHoverHex);
                    }
                    else if (key.Equals("LoadingAnimationImagePath", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.LoadingAnimationImagePath = value;
                    }
                    else if (key.Equals("LoadingDelayUnit", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.LoadingDelayUnit = NormalizeLoadingDelayUnit(value);
                    }
                }
            }
            catch
            {
                return new OviaSystemSettings();
            }

            return settings;
        }

        public static void Save(OviaSystemSettings settings)
        {
            if (settings == null)
            {
                settings = new OviaSystemSettings();
            }

            NormalizeSettings(settings);

            string folder = GetSettingsFolder();
            Directory.CreateDirectory(folder);

            string[] lines = new string[]
            {
                "ErpLoginUrl=" + Encode(settings.ErpLoginUrl),
                "ErpBaseDomain=" + Encode(settings.ErpBaseDomain),
                "ErpConnectionPath=" + Encode(settings.ErpConnectionPath),
                "ErpAuthPath=" + Encode(settings.ErpAuthPath),
                "ErpModuleBasePath=" + Encode(settings.ErpModuleBasePath),
                "CompanyLogoFilePath=" + Encode(settings.CompanyLogoFilePath),
                "VersionText=" + Encode(NormalizeVersionText(settings.VersionText)),
                "ListPageSize=" + Encode(NormalizeListPageSize(settings.ListPageSize.ToString()).ToString()),
                "BrandPrimaryHex=" + Encode(NormalizeHexColor(settings.BrandPrimaryHex, DefaultBrandPrimaryHex)),
                "BrandHoverHex=" + Encode(NormalizeHexColor(settings.BrandHoverHex, DefaultBrandHoverHex)),
                "LoadingAnimationImagePath=" + Encode(settings.LoadingAnimationImagePath),
                "LoadingDelayUnit=" + Encode(NormalizeLoadingDelayUnit(settings.LoadingDelayUnit.ToString()).ToString())
            };

            File.WriteAllLines(GetSettingsFilePath(), lines, Encoding.UTF8);

            lock (SyncRoot)
            {
                cachedSettings = Clone(settings);
                cacheLoaded = true;
            }
        }

        public static void ClearCache()
        {
            lock (SyncRoot)
            {
                cachedSettings = null;
                cacheLoaded = false;
            }
        }

        private static void NormalizeSettings(OviaSystemSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.ErpLoginUrl == null)
            {
                settings.ErpLoginUrl = "";
            }

            if (settings.ErpBaseDomain == null)
            {
                settings.ErpBaseDomain = "";
            }

            if (settings.ErpConnectionPath == null)
            {
                settings.ErpConnectionPath = "";
            }

            if (settings.ErpAuthPath == null)
            {
                settings.ErpAuthPath = "";
            }

            if (settings.ErpModuleBasePath == null)
            {
                settings.ErpModuleBasePath = "";
            }

            if (settings.CompanyLogoFilePath == null)
            {
                settings.CompanyLogoFilePath = "";
            }

            if (settings.VersionText == null)
            {
                settings.VersionText = "";
            }

            if (settings.LoadingAnimationImagePath == null)
            {
                settings.LoadingAnimationImagePath = "";
            }

            settings.ErpBaseDomain = NormalizeErpBaseDomain(settings.ErpBaseDomain);
            settings.ErpConnectionPath = NormalizeErpPath(settings.ErpConnectionPath, DefaultErpConnectionPath);
            settings.ErpAuthPath = NormalizeErpPath(settings.ErpAuthPath, DefaultErpAuthPath);
            settings.ErpModuleBasePath = NormalizeErpPath(settings.ErpModuleBasePath, DefaultErpModuleBasePath);
            settings.ErpLoginUrl = BuildErpConnectionUrl(settings);
            settings.VersionText = NormalizeVersionText(settings.VersionText);
            settings.ListPageSize = NormalizeListPageSize(settings.ListPageSize.ToString());
            settings.BrandPrimaryHex = NormalizeHexColor(settings.BrandPrimaryHex, DefaultBrandPrimaryHex);
            settings.BrandHoverHex = NormalizeHexColor(settings.BrandHoverHex, DefaultBrandHoverHex);
            settings.LoadingAnimationImagePath = NormalizeExistingImagePath(settings.LoadingAnimationImagePath);
            settings.LoadingDelayUnit = NormalizeLoadingDelayUnit(settings.LoadingDelayUnit.ToString());
        }

        private static OviaSystemSettings Clone(OviaSystemSettings source)
        {
            if (source == null)
            {
                return new OviaSystemSettings();
            }

            OviaSystemSettings clone = new OviaSystemSettings();
            clone.ErpLoginUrl = source.ErpLoginUrl == null ? "" : source.ErpLoginUrl;
            clone.ErpBaseDomain = NormalizeErpBaseDomain(source.ErpBaseDomain);
            clone.ErpConnectionPath = NormalizeErpPath(source.ErpConnectionPath, DefaultErpConnectionPath);
            clone.ErpAuthPath = NormalizeErpPath(source.ErpAuthPath, DefaultErpAuthPath);
            clone.ErpModuleBasePath = NormalizeErpPath(source.ErpModuleBasePath, DefaultErpModuleBasePath);
            clone.ErpLoginUrl = BuildErpConnectionUrl(clone);
            clone.CompanyLogoFilePath = source.CompanyLogoFilePath == null ? "" : source.CompanyLogoFilePath;
            clone.VersionText = source.VersionText == null ? "" : source.VersionText;
            clone.ListPageSize = source.ListPageSize;
            clone.BrandPrimaryHex = NormalizeHexColor(source.BrandPrimaryHex, DefaultBrandPrimaryHex);
            clone.BrandHoverHex = NormalizeHexColor(source.BrandHoverHex, DefaultBrandHoverHex);
            clone.LoadingAnimationImagePath = NormalizeExistingImagePath(source.LoadingAnimationImagePath);
            clone.LoadingDelayUnit = NormalizeLoadingDelayUnit(source.LoadingDelayUnit.ToString());
            return clone;
        }



        public static string GetErpBaseDomain()
        {
            return NormalizeErpBaseDomain(Load().ErpBaseDomain);
        }

        public static string GetErpConnectionUrl()
        {
            return BuildErpConnectionUrl(Load());
        }

        public static string GetErpAuthUrl()
        {
            return BuildErpAuthUrl(Load());
        }

        public static string GetErpModuleBaseUrl()
        {
            return BuildErpModuleBaseUrl(Load());
        }

        public static string BuildErpConnectionUrl(OviaSystemSettings settings)
        {
            if (settings == null)
            {
                settings = new OviaSystemSettings();
            }

            string domain = NormalizeErpBaseDomain(settings.ErpBaseDomain);
            string path = NormalizeErpPath(settings.ErpConnectionPath, DefaultErpConnectionPath);
            return CombineErpUrl(domain, path);
        }

        public static string BuildErpAuthUrl(OviaSystemSettings settings)
        {
            string connectionUrl = BuildErpConnectionUrl(settings);
            string authPath = NormalizeErpPath(settings == null ? DefaultErpAuthPath : settings.ErpAuthPath, DefaultErpAuthPath);
            return CombineErpUrl(connectionUrl, authPath);
        }

        public static string BuildErpModuleBaseUrl(OviaSystemSettings settings)
        {
            if (settings == null)
            {
                settings = new OviaSystemSettings();
            }

            string domain = NormalizeErpBaseDomain(settings.ErpBaseDomain);
            string moduleBasePath = NormalizeErpPath(settings.ErpModuleBasePath, DefaultErpModuleBasePath);
            return CombineErpUrl(domain, moduleBasePath);
        }

        public static string BuildErpModuleUrl(string moduleName)
        {
            return BuildErpModuleUrl(Load(), moduleName);
        }

        public static string BuildErpModuleUrl(OviaSystemSettings settings, string moduleName)
        {
            string baseUrl = BuildErpModuleBaseUrl(settings);
            string module = NormalizeErpModuleName(moduleName);
            if (module == "")
            {
                return baseUrl;
            }

            return baseUrl + Uri.EscapeDataString(module);
        }

        public static string NormalizeErpBaseDomain(string value)
        {
            string text = value == null ? "" : value.Trim();
            if (text == "")
            {
                text = DefaultErpBaseDomain;
            }

            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                text = "https://" + text;
            }

            while (text.EndsWith("/", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }

            return text;
        }

        public static string NormalizeErpPath(string value, string fallback)
        {
            string text = value == null ? "" : value.Trim();
            if (text == "")
            {
                text = fallback == null ? "" : fallback.Trim();
            }

            if (text == "")
            {
                return "";
            }

            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Uri uri = new Uri(text, UriKind.Absolute);
                    text = uri.PathAndQuery;
                }
                catch
                {
                }
            }

            if (!text.StartsWith("/", StringComparison.Ordinal))
            {
                text = "/" + text;
            }

            return text;
        }

        public static string NormalizeErpModuleName(string value)
        {
            string text = value == null ? "" : value.Trim();
            if (text.StartsWith("/", StringComparison.Ordinal))
            {
                text = text.TrimStart('/');
            }

            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static string CombineErpUrl(string baseUrl, string path)
        {
            string left = NormalizeErpBaseDomain(baseUrl);
            string right = path == null ? "" : path.Trim();
            if (right == "")
            {
                return OviaWebViewHost.NormalizeUrl(left);
            }

            if (right.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || right.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return OviaWebViewHost.NormalizeUrl(right);
            }

            if (!right.StartsWith("/", StringComparison.Ordinal))
            {
                right = "/" + right;
            }

            return OviaWebViewHost.NormalizeUrl(left + right);
        }

        public static string CopyLoadingAnimationImageToStore(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return "";
            }

            string ext = Path.GetExtension(sourcePath);
            if (ext == null)
            {
                ext = ".png";
            }

            ext = ext.ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp" && ext != ".gif")
            {
                throw new InvalidOperationException("로딩 애니메이션 이미지는 PNG, JPG, JPEG, BMP, GIF 이미지 파일만 등록할 수 있습니다.");
            }

            string folder = Path.Combine(GetSettingsFolder(), "Loading");
            Directory.CreateDirectory(folder);

            string targetPath = Path.Combine(folder, "loading_symbol" + ext);

            string[] oldFiles = Directory.GetFiles(folder, "loading_symbol.*");
            int i;
            for (i = 0; i < oldFiles.Length; i++)
            {
                try
                {
                    if (!oldFiles[i].Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(oldFiles[i]);
                    }
                }
                catch
                {
                }
            }

            File.Copy(sourcePath, targetPath, true);
            return targetPath;
        }

        public static string GetConfiguredLoadingAnimationImagePath()
        {
            OviaSystemSettings settings = Load();
            string imagePath = NormalizeExistingImagePath(settings.LoadingAnimationImagePath);

            if (imagePath != "")
            {
                return imagePath;
            }

            return GetDefaultLoadingSymbolPath();
        }

        public static string GetDefaultLoadingSymbolPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string startupPath = "";

            try
            {
                startupPath = System.Windows.Forms.Application.StartupPath;
            }
            catch
            {
                startupPath = baseDirectory;
            }

            string[] candidates = new string[]
            {
                Path.Combine(baseDirectory, "Assets", "Icons", "ovia_symbol.png"),
                Path.Combine(startupPath, "Assets", "Icons", "ovia_symbol.png"),
                Path.Combine(baseDirectory, "ovia_symbol.png"),
                Path.Combine(startupPath, "ovia_symbol.png")
            };

            int i;
            for (i = 0; i < candidates.Length; i++)
            {
                string path = NormalizeExistingImagePath(candidates[i]);
                if (path != "")
                {
                    return path;
                }
            }

            return "";
        }

        public static int GetLoadingDelayUnit()
        {
            return NormalizeLoadingDelayUnit(Load().LoadingDelayUnit.ToString());
        }

        public static int GetLoadingDelayMilliseconds()
        {
            return LoadingDelayUnitToMilliseconds(GetLoadingDelayUnit());
        }

        public static int LoadingDelayUnitToMilliseconds(int delayUnit)
        {
            return NormalizeLoadingDelayUnit(delayUnit.ToString()) * 10;
        }

        public static string FormatLoadingDelaySecondsText(int delayUnit)
        {
            double seconds = LoadingDelayUnitToMilliseconds(delayUnit) / 1000.0;
            return seconds.ToString("0.##", CultureInfo.InvariantCulture) + "초";
        }

        public static int NormalizeLoadingDelayUnit(string value)
        {
            int delayUnit;
            if (!int.TryParse(value == null ? "" : value.Trim(), out delayUnit))
            {
                delayUnit = DefaultLoadingDelayUnit;
            }

            if (delayUnit < MinLoadingDelayUnit)
            {
                delayUnit = MinLoadingDelayUnit;
            }
            else if (delayUnit > MaxLoadingDelayUnit)
            {
                delayUnit = MaxLoadingDelayUnit;
            }

            return delayUnit;
        }

        private static string NormalizeExistingImagePath(string path)
        {
            string value = path == null ? "" : path.Trim();

            if (value == "")
            {
                return "";
            }

            try
            {
                if (File.Exists(value))
                {
                    return Path.GetFullPath(value);
                }
            }
            catch
            {
            }

            return "";
        }

        public static string CopyCompanyLogoToStore(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return "";
            }

            string ext = Path.GetExtension(sourcePath);
            if (ext == null)
            {
                ext = ".png";
            }

            ext = ext.ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp" && ext != ".gif")
            {
                throw new InvalidOperationException("회사 로고는 PNG, JPG, JPEG, BMP, GIF 이미지 파일만 등록할 수 있습니다.");
            }

            string folder = Path.Combine(GetSettingsFolder(), "Brand");
            Directory.CreateDirectory(folder);

            string targetPath = Path.Combine(folder, "company_logo" + ext);

            string[] oldFiles = Directory.GetFiles(folder, "company_logo.*");
            int i;
            for (i = 0; i < oldFiles.Length; i++)
            {
                try
                {
                    if (!oldFiles[i].Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(oldFiles[i]);
                    }
                }
                catch
                {
                }
            }

            File.Copy(sourcePath, targetPath, true);
            return targetPath;
        }

        public static string GetConfiguredCompanyLogoPath()
        {
            OviaSystemSettings settings = Load();
            string logoPath = settings.CompanyLogoFilePath == null ? "" : settings.CompanyLogoFilePath.Trim();

            if (logoPath != "" && File.Exists(logoPath))
            {
                return logoPath;
            }

            return "";
        }

        public static string GetConfiguredVersionText()
        {
            OviaSystemSettings settings = Load();
            string fallbackVersion = NormalizeVersionText(settings.VersionText);
            return OviaVersionInfoStore.GetLatestVersionText(fallbackVersion);
        }

        public static string GetDisplayVersionText()
        {
            string version = GetConfiguredVersionText();
            return OviaVersionInfoStore.FormatDisplayVersion(version);
        }

        public static int GetListPageSize()
        {
            return NormalizeListPageSize(Load().ListPageSize.ToString());
        }

        public static int NormalizeListPageSize(string value)
        {
            int size;
            if (!int.TryParse(value == null ? "" : value.Trim(), out size))
            {
                size = 100;
            }

            if (size < 1)
            {
                size = 1;
            }
            else if (size > 1000)
            {
                size = 1000;
            }

            return size;
        }

        public static Color GetBrandPrimaryColor()
        {
            return HexToColor(Load().BrandPrimaryHex, Color.FromArgb(37, 99, 235));
        }

        public static Color GetBrandHoverColor()
        {
            return HexToColor(Load().BrandHoverHex, Color.FromArgb(29, 78, 216));
        }

        public static bool TryNormalizeHexColor(string value, out string normalizedHex)
        {
            normalizedHex = "";
            string raw = value == null ? "" : value.Trim();

            if (raw == "")
            {
                return false;
            }

            if (raw.StartsWith("#"))
            {
                raw = raw.Substring(1);
            }

            if (raw.Length != 6)
            {
                return false;
            }

            int i;
            for (i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');

                if (!isHex)
                {
                    return false;
                }
            }

            normalizedHex = "#" + raw.ToUpperInvariant();
            return true;
        }

        public static string NormalizeHexColor(string value, string fallback)
        {
            string normalized;
            if (TryNormalizeHexColor(value, out normalized))
            {
                return normalized;
            }

            if (TryNormalizeHexColor(fallback, out normalized))
            {
                return normalized;
            }

            return DefaultBrandPrimaryHex;
        }

        public static Color HexToColor(string value, Color fallback)
        {
            string normalized;
            if (!TryNormalizeHexColor(value, out normalized))
            {
                return fallback;
            }

            try
            {
                int r = int.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int g = int.Parse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = int.Parse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb(r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

        public static string ColorToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        public static string NormalizeVersionText(string value)
        {
            string version = value == null ? "" : value.Trim();

            if (version.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Substring("Version".Length).Trim();
            }

            return version;
        }

        public static string GetSettingsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA"
            );
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetSettingsFolder(), SettingsFileName);
        }

        private static string Encode(string value)
        {
            if (value == null)
            {
                value = "";
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            try
            {
                if (value == null)
                {
                    return "";
                }

                byte[] bytes = Convert.FromBase64String(value.Trim());
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
