using System;
using System.Drawing;
using System.Drawing.Text;

namespace OVIA.Desktop
{
    public static class OviaSystemIconManager
    {
        public const string Home = "\uE80F";
        public const string Settings = "\uE713";
        public const string Back = "\uE72B";
        public const string Forward = "\uE72A";
        public const string Refresh = "\uE72C";
        public const string Download = "\uE896";
        public const string Link = "\uE71B";
        public const string Power = "\uE7E8";
        public const string List = "\uE8A5";
        public const string Info = "\uE946";

        public static string PreferredIconFontName()
        {
            if (HasFontFamily("Segoe Fluent Icons"))
            {
                return "Segoe Fluent Icons";
            }

            if (HasFontFamily("Segoe MDL2 Assets"))
            {
                return "Segoe MDL2 Assets";
            }

            return "Segoe UI Symbol";
        }

        public static Font CreateIconFont(float size)
        {
            return new Font(PreferredIconFontName(), size, FontStyle.Regular, GraphicsUnit.Point);
        }

        private static bool HasFontFamily(string familyName)
        {
            try
            {
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    FontFamily[] families = fonts.Families;
                    int i;
                    for (i = 0; i < families.Length; i++)
                    {
                        if (families[i].Name.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
