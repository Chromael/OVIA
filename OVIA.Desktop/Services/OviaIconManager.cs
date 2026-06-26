using System;
using System.Drawing;
using System.Linq;

namespace OVIA.Desktop.Services
{
    /// <summary>
    /// OVIA system icon helper.
    ///
    /// Icon policy:
    /// - Do not bundle external icon PNG/SVG/font files.
    /// - Prefer Windows system icon fonts for DPI-safe offline rendering.
    /// - Windows 11: Segoe Fluent Icons.
    /// - Windows 10 fallback: Segoe MDL2 Assets.
    /// - Final fallback: Segoe UI Symbol.
    /// </summary>
    internal static class OviaIconManager
    {
        public const string FontSegoeFluentIcons = "Segoe Fluent Icons";
        public const string FontSegoeMdl2Assets = "Segoe MDL2 Assets";
        public const string FontSegoeUiSymbol = "Segoe UI Symbol";

        public static Font CreateIconFont(float size)
        {
            return CreateIconFont(size, FontStyle.Regular);
        }

        public static Font CreateIconFont(float size, FontStyle style)
        {
            string familyName = GetAvailableIconFontFamilyName();
            return new Font(familyName, size, style, GraphicsUnit.Point);
        }

        public static string GetAvailableIconFontFamilyName()
        {
            if (IsFontInstalled(FontSegoeFluentIcons))
            {
                return FontSegoeFluentIcons;
            }

            if (IsFontInstalled(FontSegoeMdl2Assets))
            {
                return FontSegoeMdl2Assets;
            }

            return FontSegoeUiSymbol;
        }

        public static bool IsFontInstalled(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
            {
                return false;
            }

            try
            {
                return FontFamily.Families.Any(delegate(FontFamily family)
                {
                    return string.Equals(family.Name, familyName, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                return false;
            }
        }

        // Common OVIA glyphs. Glyph values are kept as code points rather than bundled image files.
        public const string Home = "\uE80F";
        public const string Monitor = "\uE7F4";
        public const string Project = "\uE90F";
        public const string Link = "\uE71B";
        public const string Download = "\uE896";
        public const string Table = "\uE8A5";
        public const string Settings = "\uE713";
        public const string ChevronDown = "\uE70D";
        public const string Info = "\uE946";
        public const string License = "\uE8D7";
        public const string Backup = "\uE74E";
        public const string Calculator = "\uE9D9";
        public const string Back = "\uE72B";
        public const string Forward = "\uE72A";
        public const string Up = "\uE74A";
        public const string Refresh = "\uE72C";
        public const string Power = "\uE7E8";
        public const string Logout = "\uE8BB";
    }
}
