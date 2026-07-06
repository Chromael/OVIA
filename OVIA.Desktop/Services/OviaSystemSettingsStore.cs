using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace OVIA.Desktop
{
    public class OviaSystemSettings
    {
        public string ErpLoginUrl = "";
        public string CompanyLogoFilePath = "";
        public string VersionText = "";
        public int ListPageSize = 100;
        public string BrandPrimaryHex = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
        public string BrandHoverHex = OviaSystemSettingsStore.DefaultBrandHoverHex;
    }

    public static class OviaSystemSettingsStore
    {
        private const string SettingsFileName = "system_settings.dat";
        public const string DefaultBrandPrimaryHex = "#2563EB";
        public const string DefaultBrandHoverHex = "#1D4ED8";

        private static readonly object SyncRoot = new object();
        private static bool cacheLoaded = false;
        private static OviaSystemSettings cachedSettings = null;

        public static bool IsSuperAdminUser(string userId)
        {
            string value = userId == null ? "" : userId.Trim().ToLowerInvariant();

            if (value == "")
            {
                return false;
            }

            return value == "admin"
                || value == "administrator"
                || value == "root"
                || value == "celmon"
                || value == "oviaadmin"
                || value == "system"
                || value == "superadmin"
                || value == "systemadmin"
                || value == "sysadmin"
                || value == "최고관리자"
                || value == "시스템관리자";
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
                "CompanyLogoFilePath=" + Encode(settings.CompanyLogoFilePath),
                "VersionText=" + Encode(NormalizeVersionText(settings.VersionText)),
                "ListPageSize=" + Encode(NormalizeListPageSize(settings.ListPageSize.ToString()).ToString()),
                "BrandPrimaryHex=" + Encode(NormalizeHexColor(settings.BrandPrimaryHex, DefaultBrandPrimaryHex)),
                "BrandHoverHex=" + Encode(NormalizeHexColor(settings.BrandHoverHex, DefaultBrandHoverHex))
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

            if (settings.CompanyLogoFilePath == null)
            {
                settings.CompanyLogoFilePath = "";
            }

            if (settings.VersionText == null)
            {
                settings.VersionText = "";
            }

            settings.VersionText = NormalizeVersionText(settings.VersionText);
            settings.ListPageSize = NormalizeListPageSize(settings.ListPageSize.ToString());
            settings.BrandPrimaryHex = NormalizeHexColor(settings.BrandPrimaryHex, DefaultBrandPrimaryHex);
            settings.BrandHoverHex = NormalizeHexColor(settings.BrandHoverHex, DefaultBrandHoverHex);
        }

        private static OviaSystemSettings Clone(OviaSystemSettings source)
        {
            if (source == null)
            {
                return new OviaSystemSettings();
            }

            OviaSystemSettings clone = new OviaSystemSettings();
            clone.ErpLoginUrl = source.ErpLoginUrl == null ? "" : source.ErpLoginUrl;
            clone.CompanyLogoFilePath = source.CompanyLogoFilePath == null ? "" : source.CompanyLogoFilePath;
            clone.VersionText = source.VersionText == null ? "" : source.VersionText;
            clone.ListPageSize = source.ListPageSize;
            clone.BrandPrimaryHex = NormalizeHexColor(source.BrandPrimaryHex, DefaultBrandPrimaryHex);
            clone.BrandHoverHex = NormalizeHexColor(source.BrandHoverHex, DefaultBrandHoverHex);
            return clone;
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
            return NormalizeVersionText(settings.VersionText);
        }

        public static string GetDisplayVersionText()
        {
            string version = GetConfiguredVersionText();

            if (version == "")
            {
                version = "1.0.0";
            }

            return "Version " + version;
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
