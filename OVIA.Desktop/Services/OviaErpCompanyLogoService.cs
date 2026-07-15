using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    /// <summary>
    /// ERP에 등록된 회사 로고를 회사별 LocalAppData 캐시로 동기화합니다.
    /// 로그인은 로고 다운로드 성공 여부와 무관하게 계속 진행되어야 합니다.
    /// </summary>
    internal static class OviaErpCompanyLogoService
    {
        private static readonly string[] SupportedExtensions = new string[] { ".png", ".gif", ".jpg" };
        private const int RequestTimeoutMilliseconds = 3500;

        public static bool Synchronize(string companyId)
        {
            string safeCompanyId = NormalizeCompanyId(companyId);
            if (safeCompanyId == "")
            {
                return false;
            }

            bool serverConfirmedNotFound = true;

            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                string extension = SupportedExtensions[i];
                string url = BuildLogoUrl(safeCompanyId, extension);

                DownloadResult result = TryDownload(url, safeCompanyId, extension);
                if (result == DownloadResult.Downloaded)
                {
                    return true;
                }

                if (result == DownloadResult.Unavailable)
                {
                    serverConfirmedNotFound = false;
                    break;
                }
            }

            // 세 확장자 모두 서버에서 404로 확인된 경우에만 기존 캐시를 제거합니다.
            // 네트워크 장애나 서버 오류일 때는 이전 캐시를 유지합니다.
            if (serverConfirmedNotFound)
            {
                DeleteCachedLogos(safeCompanyId);
            }
            return false;
        }

        public static string GetCachedLogoPath(string companyId)
        {
            string safeCompanyId = NormalizeCompanyId(companyId);
            if (safeCompanyId == "")
            {
                return "";
            }

            string folder = GetCompanyFolder(safeCompanyId);
            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                string path = Path.Combine(folder, "company_logo" + SupportedExtensions[i]);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "";
        }

        private static DownloadResult TryDownload(string url, string companyId, string extension)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = RequestTimeoutMilliseconds;
                request.ReadWriteTimeout = RequestTimeoutMilliseconds;
                request.AllowAutoRedirect = true;
                request.UserAgent = "OVIA/1.0";
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return DownloadResult.Unavailable;
                    }

                    using (MemoryStream memory = new MemoryStream())
                    {
                        using (Stream responseStream = response.GetResponseStream())
                        {
                            if (responseStream == null)
                            {
                                return DownloadResult.Unavailable;
                            }

                            responseStream.CopyTo(memory);
                        }

                        byte[] bytes = memory.ToArray();
                        if (!IsValidImage(bytes))
                        {
                            return DownloadResult.Unavailable;
                        }

                        SaveLogo(companyId, extension, bytes);
                        return DownloadResult.Downloaded;
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    return DownloadResult.NotFound;
                }

                if (response != null)
                {
                    response.Dispose();
                }

                return DownloadResult.Unavailable;
            }
            catch
            {
                return DownloadResult.Unavailable;
            }
        }

        private static bool IsValidImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image image = Image.FromStream(stream, true, true))
                {
                    return image.Width > 0 && image.Height > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SaveLogo(string companyId, string extension, byte[] bytes)
        {
            string folder = GetCompanyFolder(companyId);
            Directory.CreateDirectory(folder);

            DeleteCachedLogos(companyId);

            string temporaryPath = Path.Combine(folder, "company_logo.tmp");
            File.WriteAllBytes(temporaryPath, bytes);

            string targetPath = Path.Combine(folder, "company_logo" + extension);
            File.Move(temporaryPath, targetPath);
        }

        public static void ClearCachedLogo(string companyId)
        {
            string safeCompanyId = NormalizeCompanyId(companyId);
            if (safeCompanyId != "") DeleteCachedLogos(safeCompanyId);
        }

        private static void DeleteCachedLogos(string companyId)
        {
            try
            {
                string folder = GetCompanyFolder(companyId);
                if (!Directory.Exists(folder))
                {
                    return;
                }

                string[] files = Directory.GetFiles(folder, "company_logo.*");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
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

        private static string BuildLogoUrl(string companyId, string extension)
        {
            string baseDomain = OviaSystemSettingsStore.GetErpBaseDomain();
            return baseDomain.TrimEnd('/') + "/erp/uploads/" + Uri.EscapeDataString(companyId) + "/company/logo" + extension;
        }

        private static string GetCompanyFolder(string companyId)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "OVIA", "Companies", companyId);
        }

        private static string NormalizeCompanyId(string companyId)
        {
            string value = companyId == null ? "" : companyId.Trim();
            if (value == "" || !Regex.IsMatch(value, "^[A-Za-z0-9_-]+$"))
            {
                return "";
            }

            return value;
        }

        private enum DownloadResult
        {
            Downloaded,
            NotFound,
            Unavailable
        }
    }
}
