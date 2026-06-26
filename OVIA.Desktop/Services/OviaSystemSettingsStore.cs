using System;
using System.IO;
using System.Text;

namespace OVIA.Desktop
{
    public class OviaSystemSettings
    {
        public string ErpLoginUrl = "";
        public string CompanyLogoFilePath = "";
        public string VersionText = "";
    }

    public static class OviaSystemSettingsStore
    {
        private const string SettingsFileName = "system_settings.dat";

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
                }
            }
            catch
            {
                return new OviaSystemSettings();
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

            return settings;
        }

        public static void Save(OviaSystemSettings settings)
        {
            if (settings == null)
            {
                settings = new OviaSystemSettings();
            }

            string folder = GetSettingsFolder();
            Directory.CreateDirectory(folder);

            string[] lines = new string[]
            {
                "ErpLoginUrl=" + Encode(settings.ErpLoginUrl),
                "CompanyLogoFilePath=" + Encode(settings.CompanyLogoFilePath),
                "VersionText=" + Encode(NormalizeVersionText(settings.VersionText))
            };

            File.WriteAllLines(GetSettingsFilePath(), lines, Encoding.UTF8);
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
