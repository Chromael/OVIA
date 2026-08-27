using System;
using System.IO;
using System.Threading;

namespace OVIA.Desktop
{
    /// <summary>
    /// OVIA Projects 작업공간은 현재 실행 세션에서만 사용하는 캐시다.
    /// 정상적인 사용자 세션 종료/프로그램 종료 시 Projects 전체와 CAD 출력 hand-off를 정리한다.
    /// ERP의 공사/BarList/shape_json이 영구 원장이므로 다음 실행 시 필요한 캐시는 ERP에서 재생성한다.
    /// </summary>
    internal static class OviaSessionCacheService
    {
        private const int DeleteRetryCount = 3;
        private const int DeleteRetryDelayMilliseconds = 120;

        public static void CleanupProjectsCache()
        {
            DeleteDirectoryWithRetry(OviaProjectWorkspacePaths.GetProjectsRoot());
            DeleteFileWithRetry(OviaProjectWorkspacePaths.GetCadOutputHintPath());
        }

        private static void DeleteDirectoryWithRetry(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)) return;

            for (int attempt = 0; attempt < DeleteRetryCount; attempt++)
            {
                try
                {
                    if (!Directory.Exists(directoryPath)) return;

                    ClearReadOnlyAttributes(directoryPath);
                    Directory.Delete(directoryPath, true);
                    return;
                }
                catch
                {
                    if (attempt + 1 >= DeleteRetryCount) return;
                    Thread.Sleep(DeleteRetryDelayMilliseconds);
                }
            }
        }

        private static void DeleteFileWithRetry(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            for (int attempt = 0; attempt < DeleteRetryCount; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath)) return;

                    FileAttributes attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                    }

                    File.Delete(filePath);
                    return;
                }
                catch
                {
                    if (attempt + 1 >= DeleteRetryCount) return;
                    Thread.Sleep(DeleteRetryDelayMilliseconds);
                }
            }
        }

        private static void ClearReadOnlyAttributes(string directoryPath)
        {
            try
            {
                string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(files[i]);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(files[i], attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }
}
