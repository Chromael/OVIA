using System;
using System.IO;
using System.Text;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA 프로젝트 로컬 작업공간의 단일 경로 규칙.
    /// ERP의 project_no만 물리 폴더 키로 사용하며 공사명은 경로에 포함하지 않는다.
    /// Projects는 영구 원장이 아니라 ERP에서 재생성 가능한 작업 캐시다.
    /// </summary>
    public static class OviaProjectWorkspacePaths
    {
        private const string CadOutputHintFileName = "cad_output_path.txt";

        public static string GetOviaLocalRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OVIA"
            );
        }

        public static string GetProjectsRoot()
        {
            return Path.Combine(GetOviaLocalRoot(), "Projects");
        }

        public static string GetProjectDirectory(string projectNo)
        {
            string key = SanitizeProjectNo(projectNo);
            if (key == "") key = "NoProject";
            return Path.Combine(GetProjectsRoot(), key);
        }

        public static string GetProjectBarListDirectory(string projectNo)
        {
            return Path.Combine(GetProjectDirectory(projectNo), "BarList");
        }

        public static string GetProjectBarListTempDirectory(string projectNo)
        {
            return Path.Combine(GetProjectBarListDirectory(projectNo), "Temp");
        }

        public static string GetCadOutputHintPath()
        {
            return Path.Combine(GetOviaLocalRoot(), CadOutputHintFileName);
        }

        public static string PrepareCadOutputDirectory(string projectNo)
        {
            string tempDirectory = GetProjectBarListTempDirectory(projectNo);
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(GetOviaLocalRoot());

            // AutoCAD 플러그인은 별도 프로세스이므로 현재 프로젝트의 출력경로를
            // 작은 hand-off 파일로 전달한다. CAD 추출 데이터/알고리즘은 변경하지 않는다.
            string hintPath = GetCadOutputHintPath();
            string tempHintPath = hintPath + ".tmp";
            File.WriteAllText(tempHintPath, tempDirectory, new UTF8Encoding(false));

            if (File.Exists(hintPath))
            {
                File.Delete(hintPath);
            }

            File.Move(tempHintPath, hintPath);
            return tempDirectory;
        }

        public static bool IsPathInsideDirectory(string filePath, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directoryPath)) return false;

            try
            {
                string file = Path.GetFullPath(filePath);
                string directory = Path.GetFullPath(directoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                return file.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SanitizeProjectNo(string value)
        {
            string result = value == null ? "" : value.Trim();
            char[] invalids = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalids.Length; i++)
            {
                result = result.Replace(invalids[i], '_');
            }

            result = result.Replace(" ", "_");
            return result.Trim('_');
        }
    }
}
