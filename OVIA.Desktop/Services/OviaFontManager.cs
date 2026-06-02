using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA 폰트 역할 관리자.
    /// - Brand/Title/Button: Pretendard 우선 사용
    /// - System/Data/Input/Status: Windows 기본 UI 폰트(Malgun Gothic → Segoe UI → Arial) 사용
    ///
    /// </summary>
    public static class OviaFontManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly PrivateFontCollection PrivateFonts = new PrivateFontCollection();
        private static bool initialized;

        private static readonly string[] PretendardKeywords = new[]
        {
            "Pretendard"
        };

        private static readonly string[] InstalledBrandFontNames = new[]
        {
            "Pretendard"
        };

        private static readonly string[] InstalledSystemFontNames = new[]
        {
            "Malgun Gothic",
            "맑은 고딕",
            "Segoe UI",
            "Arial"
        };

        public static Font CreateBrandFont(float size, FontStyle style)
        {
            return CreateBrandFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateBrandFont(float size, FontStyle style, GraphicsUnit unit)
        {
            EnsureInitialized();

            Font privateFont = TryCreatePrivatePretendardFont(size, style, unit);
            if (privateFont != null)
            {
                return privateFont;
            }

            Font installedBrand = TryCreateInstalledFont(InstalledBrandFontNames, size, style, unit);
            if (installedBrand != null)
            {
                return installedBrand;
            }

            return CreateSystemFont(size, style, unit);
        }

        public static Font CreateButtonFont(float size, FontStyle style)
        {
            return CreateBrandFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateTitleFont(float size, FontStyle style)
        {
            return CreateBrandFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateSystemFont(float size, FontStyle style)
        {
            return CreateSystemFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateSystemFont(float size, FontStyle style, GraphicsUnit unit)
        {
            Font installedSystem = TryCreateInstalledFont(InstalledSystemFontNames, size, style, unit);
            if (installedSystem != null)
            {
                return installedSystem;
            }

            return new Font(FontFamily.GenericSansSerif, size, style, unit);
        }

        public static Font CreateDataFont(float size, FontStyle style)
        {
            return CreateSystemFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateInputFont(float size, FontStyle style)
        {
            return CreateSystemFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateStatusFont(float size, FontStyle style)
        {
            return CreateSystemFont(size, style, GraphicsUnit.Point);
        }

        // 기존 코드 호환용: 일반 한글/데이터/입력 계열은 Windows 시스템 폰트로 처리한다.
        public static Font CreateKoreanFont(float size, FontStyle style)
        {
            return CreateSystemFont(size, style, GraphicsUnit.Point);
        }

        public static Font CreateKoreanFont(float size, FontStyle style, GraphicsUnit unit)
        {
            return CreateSystemFont(size, style, unit);
        }

        // 기존 코드 호환용: UI 포인트 계열은 Pretendard 역할로 처리한다.
        public static Font CreateUIFont(float size, FontStyle style)
        {
            return CreateBrandFont(size, style, GraphicsUnit.Point);
        }

        public static string CurrentBrandFontName()
        {
            EnsureInitialized();

            FontFamily privateFamily = FindPrivatePretendardFamily(FontStyle.Regular);
            if (privateFamily != null)
            {
                return privateFamily.Name;
            }

            foreach (string name in InstalledBrandFontNames)
            {
                if (IsInstalledFontAvailable(name))
                {
                    return name;
                }
            }

            return CurrentSystemFontName();
        }

        public static string CurrentSystemFontName()
        {
            foreach (string name in InstalledSystemFontNames)
            {
                if (IsInstalledFontAvailable(name))
                {
                    return name;
                }
            }

            return "GenericSansSerif";
        }

        public static string CurrentPreferredFontName()
        {
            return CurrentBrandFontName();
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (initialized)
                {
                    return;
                }

                foreach (string fontDirectory in GetCandidateFontDirectories())
                {
                    LoadFontDirectory(fontDirectory);
                }

                initialized = true;
            }
        }

        private static IEnumerable<string> GetCandidateFontDirectories()
        {
            List<string> directories = new List<string>();

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AddFontDirectoryCandidates(directories, baseDirectory);

            DirectoryInfo current = new DirectoryInfo(baseDirectory);
            for (int i = 0; i < 8 && current != null; i++)
            {
                AddFontDirectoryCandidates(directories, current.FullName);
                current = current.Parent;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                AddFontDirectoryCandidates(directories, Path.Combine(localAppData, "OVIA"));
            }

            return directories.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddFontDirectoryCandidates(List<string> directories, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            directories.Add(Path.Combine(rootPath, "Assets", "Fonts"));
            directories.Add(Path.Combine(rootPath, "OVIA.Desktop", "Assets", "Fonts"));
        }

        private static void LoadFontDirectory(string fontDirectory)
        {
            if (string.IsNullOrWhiteSpace(fontDirectory) || !Directory.Exists(fontDirectory))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(fontDirectory, "*.otf"))
            {
                TryAddPretendardFontFile(filePath);
            }

            foreach (string filePath in Directory.GetFiles(fontDirectory, "*.ttf"))
            {
                TryAddPretendardFontFile(filePath);
            }
        }

        private static void TryAddPretendardFontFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName == null || fileName.IndexOf("Pretendard", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            try
            {
                PrivateFonts.AddFontFile(filePath);
            }
            catch
            {
                // 폰트 파일이 손상되었거나 지원되지 않는 경우 앱 실행을 막지 않고 fallback 한다.
            }
        }

        private static Font TryCreatePrivatePretendardFont(float size, FontStyle style, GraphicsUnit unit)
        {
            FontFamily family = FindPrivatePretendardFamily(style);
            if (family == null)
            {
                return null;
            }

            FontStyle safeStyle = family.IsStyleAvailable(style) ? style : FontStyle.Regular;
            return new Font(family, size, safeStyle, unit);
        }

        private static FontFamily FindPrivatePretendardFamily(FontStyle style)
        {
            FontFamily[] families = PrivateFonts.Families;

            foreach (string keyword in PretendardKeywords)
            {
                FontFamily best = families
                    .Where(delegate(FontFamily f)
                    {
                        return f.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                            && (f.IsStyleAvailable(style) || f.IsStyleAvailable(FontStyle.Regular));
                    })
                    .OrderBy(delegate(FontFamily f) { return GetFamilyWeightRank(f.Name, style); })
                    .ThenBy(delegate(FontFamily f) { return f.Name; })
                    .FirstOrDefault();

                if (best != null)
                {
                    return best;
                }
            }

            return null;
        }

        private static int GetFamilyWeightRank(string familyName, FontStyle requestedStyle)
        {
            string name = familyName == null ? "" : familyName.ToLowerInvariant();

            if ((requestedStyle & FontStyle.Bold) == FontStyle.Bold)
            {
                if (name.Contains("semibold") || name.Contains("semi bold")) return 0;
                if (name.Contains("bold")) return 1;
                if (name.Contains("medium")) return 2;
                if (name.Contains("regular")) return 3;
                return 4;
            }

            if (name.Contains("regular")) return 0;
            if (name.Contains("medium")) return 1;
            if (name.Equals("pretendard")) return 2;
            if (name.Contains("light") || name.Contains("thin")) return 3;
            if (name.Contains("bold") || name.Contains("black")) return 5;
            return 4;
        }

        private static Font TryCreateInstalledFont(string[] fontNames, float size, FontStyle style, GraphicsUnit unit)
        {
            foreach (string name in fontNames)
            {
                if (!IsInstalledFontAvailable(name))
                {
                    continue;
                }

                try
                {
                    return new Font(name, size, style, unit);
                }
                catch
                {
                    try
                    {
                        return new Font(name, size, FontStyle.Regular, unit);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static bool IsInstalledFontAvailable(string familyName)
        {
            try
            {
                using (Font test = new Font(familyName, 9F, FontStyle.Regular, GraphicsUnit.Point))
                {
                    return string.Equals(test.FontFamily.Name, familyName, StringComparison.OrdinalIgnoreCase)
                        || test.FontFamily.Name.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
