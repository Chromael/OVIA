using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace OVIA.Desktop
{
    public sealed class OviaErpLaunchRequest
    {
        public string CompanyId { get; set; }
        public string Ticket { get; set; }
    }

    public sealed class OviaErpLogoutRequest
    {
        public string CompanyId { get; set; }
        public string Ticket { get; set; }
    }

    public sealed class OviaErpLaunchResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string CompanyId { get; set; }
        public string UserId { get; set; }
        public int UserLevel { get; set; }
        public string OviaToken { get; set; }
        public string WebSessionTicket { get; set; }
        public string LogoutTicket { get; set; }
        public string TargetType { get; set; }
        public string ProjectNo { get; set; }
        public string ProjectName { get; set; }
        public string ClientName { get; set; }
        public string ProjectStatus { get; set; }
        public int BarListId { get; set; }
    }

    /// <summary>
    /// ERP 웹에서 ovia:// 프로토콜로 전달한 1회용 Launch Ticket을 ERP 서버에서 교환합니다.
    /// ID/PW 또는 64자리 OVIA API 토큰은 URL/명령행으로 전달하지 않습니다.
    /// </summary>
    public static class OviaErpLaunchService
    {
        private const string Scheme = "ovia";
        private const string LaunchHost = "launch";
        private const string LogoutHost = "logout";
        private const string ExchangeMode = "ovia_launch_exchange";

        public static bool TryParseLaunchRequest(string[] args, out OviaErpLaunchRequest request)
        {
            request = null;
            if (args == null || args.Length == 0) return false;

            string raw = "";
            for (int i = 0; i < args.Length; i++)
            {
                string candidate = (args[i] ?? "").Trim();
                if (candidate.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase))
                {
                    raw = candidate;
                    break;
                }
            }

            if (raw == "") return false;

            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(uri.Host, LaunchHost, StringComparison.OrdinalIgnoreCase)) return false;

            var query = HttpUtility.ParseQueryString(uri.Query ?? "");
            string companyId = (query["company_id"] ?? query["company"] ?? "").Trim();
            string ticket = (query["ticket"] ?? "").Trim();

            if (companyId == "" || !IsSafeCompanyId(companyId)) return false;
            if (ticket == "" || ticket.Length < 20 || ticket.Length > 512) return false;

            request = new OviaErpLaunchRequest
            {
                CompanyId = companyId,
                Ticket = ticket
            };
            return true;
        }

        public static bool TryParseLogoutRequest(string[] args, out OviaErpLogoutRequest request)
        {
            request = null;
            if (args == null || args.Length == 0) return false;

            string raw = "";
            for (int i = 0; i < args.Length; i++)
            {
                string candidate = (args[i] ?? "").Trim();
                if (candidate.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase))
                {
                    raw = candidate;
                    break;
                }
            }

            if (raw == "") return false;

            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(uri.Host, LogoutHost, StringComparison.OrdinalIgnoreCase)) return false;

            var query = HttpUtility.ParseQueryString(uri.Query ?? "");
            string companyId = (query["company_id"] ?? query["company"] ?? "").Trim();
            string ticket = (query["ticket"] ?? "").Trim();

            if (companyId == "" || !IsSafeCompanyId(companyId)) return false;
            if (!IsHexToken64(ticket)) return false;

            request = new OviaErpLogoutRequest
            {
                CompanyId = companyId,
                Ticket = ticket.ToLowerInvariant()
            };
            return true;
        }

        private static bool IsHexToken64(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        public static async Task<OviaErpLaunchResult> ExchangeAsync(OviaErpLaunchRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CompanyId) || string.IsNullOrWhiteSpace(request.Ticket))
            {
                return Fail("ERP 실행 요청 정보가 올바르지 않습니다.");
            }

            string authBase = OviaCompanyConnectionStore.GetErpAuthUrl(request.CompanyId);
            Uri baseUri;
            if (!Uri.TryCreate((authBase ?? "").TrimEnd('/') + "/", UriKind.Absolute, out baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                return Fail("해당 기업의 OVIA Connection 정보를 확인할 수 없습니다.\r\nOVIA Connection을 먼저 설정해주세요.");
            }

            Uri endpointBase = new Uri(baseUri, "ovia_api.php");
            UriBuilder builder = new UriBuilder(endpointBase);
            builder.Query = "mode=" + Uri.EscapeDataString(ExchangeMode);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("OVIA-Desktop/1.0");

                    Dictionary<string, object> payload = new Dictionary<string, object>();
                    payload["company_id"] = request.CompanyId;
                    payload["ticket"] = request.Ticket;

                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    string json = serializer.Serialize(payload);
                    using (StringContent content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await client.PostAsync(builder.Uri, content))
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        return ParseExchangeResult(request.CompanyId, responseText, (int)response.StatusCode);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return Fail("ERP OVIA 실행 인증 시간이 초과되었습니다. 다시 실행해주세요.");
            }
            catch (HttpRequestException ex)
            {
                return Fail("ERP OVIA 실행 인증 서버에 연결할 수 없습니다.\r\n" + ex.Message);
            }
            catch (Exception ex)
            {
                return Fail("ERP OVIA 실행 처리 중 오류가 발생했습니다.\r\n" + ex.Message);
            }
        }

        public static bool IsNewBarListTarget(OviaErpLaunchResult launch)
        {
            return launch != null
                && string.Equals(launch.TargetType, "new_barlist", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(launch.ProjectNo);
        }

        public static async Task<string> PrepareBarListAsync(OviaErpLaunchResult launch)
        {
            if (launch == null || launch.BarListId <= 0 || string.IsNullOrWhiteSpace(launch.ProjectNo)) return "";

            string localDirectory = GetProjectBarListDirectory(launch.ProjectNo, launch.ProjectName);
            OviaErpBarListSyncResult sync = await OviaErpBarListSyncService.PullProjectBarListsAsync(
                launch.CompanyId,
                launch.ProjectNo,
                launch.ProjectName,
                localDirectory);

            if (!sync.IsSuccess)
            {
                return "";
            }

            if (!Directory.Exists(localDirectory)) return "";
            foreach (string csvPath in Directory.GetFiles(localDirectory, "*.csv", SearchOption.TopDirectoryOnly))
            {
                if (OviaErpBarListSyncService.GetPersistedErpBarListId(csvPath) == launch.BarListId)
                {
                    return csvPath;
                }
            }

            return "";
        }

        private static OviaErpLaunchResult ParseExchangeResult(string requestedCompanyId, string responseText, int statusCode)
        {
            string text = (responseText ?? "").Trim();
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1).TrimStart();
            if (text == "") return Fail("ERP OVIA 실행 인증 서버가 빈 응답을 반환했습니다.");

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                IDictionary<string, object> root = AsDictionary(serializer.DeserializeObject(text));
                if (root == null) return Fail("ERP OVIA 실행 인증 응답 형식이 올바르지 않습니다.");

                object resValue;
                if (!TryGet(root, "res", out resValue) || !ToBool(resValue))
                {
                    return Fail(ReadString(root, "msg", statusCode >= 400 ? "ERP OVIA 실행 인증에 실패했습니다." : "OVIA 실행 권한을 확인할 수 없습니다."));
                }

                IDictionary<string, object> data = root;
                object dataValue;
                if (TryGet(root, "data", out dataValue))
                {
                    IDictionary<string, object> parsedData = AsDictionary(dataValue);
                    if (parsedData != null) data = parsedData;
                }

                string oviaYn = ReadString(data, "ovia_yn", ReadString(root, "ovia_yn", "")).ToUpperInvariant();
                if (oviaYn != "Y") return Fail(oviaYn == "N" ? "사용권한이 없습니다." : "OVIA 사용 권한 정보를 확인할 수 없습니다.");

                string companyId = ReadString(data, "company_id", ReadString(root, "company_id", requestedCompanyId));
                string userId = ReadString(data, "user_id", ReadString(data, "member_id", ReadString(root, "user_id", "")));
                string token = ReadString(data, "ovia_token", ReadString(root, "ovia_token", ""));
                int userLevel = ReadLevel(data, root);

                if (!string.Equals(companyId, requestedCompanyId, StringComparison.OrdinalIgnoreCase))
                    return Fail("ERP 실행 요청 기업과 인증 결과 기업이 일치하지 않습니다.");
                if (userId == "") return Fail("ERP 실행 인증 응답에서 사용자 아이디를 확인할 수 없습니다.");
                if (token.Length != 64) return Fail("ERP 실행 인증 응답의 OVIA API 토큰 형식이 올바르지 않습니다.");

                IDictionary<string, object> target = null;
                object targetValue;
                if (TryGet(data, "target", out targetValue)) target = AsDictionary(targetValue);
                if (target == null && TryGet(root, "target", out targetValue)) target = AsDictionary(targetValue);
                if (target == null) target = data;

                string targetType = ReadString(target, "type", ReadString(target, "target_type", "")).Trim().ToLowerInvariant();
                int barListId = ReadInt(target, "barlist_idx");
                if (targetType == "" && barListId > 0) targetType = "barlist";
                string projectStatus = ReadString(target, "project_status", ReadString(target, "status", ""));
                if (projectStatus == "")
                {
                    string completed = ReadString(target, "is_completed", "").ToUpperInvariant();
                    if (completed == "Y" || completed == "1" || completed == "TRUE") projectStatus = "완료";
                    else if (completed != "") projectStatus = "진행중";
                }

                return new OviaErpLaunchResult
                {
                    IsSuccess = true,
                    Message = ReadString(root, "msg", ""),
                    CompanyId = companyId,
                    UserId = userId,
                    UserLevel = userLevel,
                    OviaToken = token,
                    WebSessionTicket = ReadString(
                        data,
                        "web_session_ticket",
                        ReadString(root, "web_session_ticket", "")
                    ),
                    LogoutTicket = ReadString(
                        data,
                        "logout_ticket",
                        ReadString(root, "logout_ticket", "")
                    ),
                    TargetType = targetType,
                    ProjectNo = ReadString(target, "project_no", ""),
                    ProjectName = ReadString(target, "project_name", ""),
                    ClientName = ReadString(target, "customer_name", ReadString(target, "client_name", "")),
                    ProjectStatus = projectStatus,
                    BarListId = barListId
                };
            }
            catch (Exception ex)
            {
                return Fail("ERP OVIA 실행 인증 서버의 응답을 해석할 수 없습니다.\r\n" + ex.Message);
            }
        }

        private static string GetProjectBarListDirectory(string projectNo, string projectName)
        {
            return OviaProjectWorkspacePaths.GetProjectBarListDirectory(projectNo);
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? "";
            char[] invalids = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalids.Length; i++) result = result.Replace(invalids[i], '_');
            return result.Trim();
        }

        private static bool IsSafeCompanyId(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')) return false;
            }
            return true;
        }

        private static IDictionary<string, object> AsDictionary(object value)
        {
            IDictionary<string, object> typed = value as IDictionary<string, object>;
            if (typed != null) return typed;
            IDictionary dictionary = value as IDictionary;
            if (dictionary == null) return null;
            Dictionary<string, object> converted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary) converted[Convert.ToString(entry.Key)] = entry.Value;
            return converted;
        }

        private static bool TryGet(IDictionary<string, object> data, string key, out object value)
        {
            value = null;
            if (data == null) return false;
            if (data.TryGetValue(key, out value)) return true;
            foreach (KeyValuePair<string, object> pair in data)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            return false;
        }

        private static string ReadString(IDictionary<string, object> data, string key, string fallback)
        {
            object value;
            if (!TryGet(data, key, out value) || value == null) return fallback ?? "";
            return Convert.ToString(value).Trim();
        }

        private static int ReadInt(IDictionary<string, object> data, string key)
        {
            int value;
            return int.TryParse(ReadString(data, key, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static int ReadLevel(IDictionary<string, object> data, IDictionary<string, object> root)
        {
            string[] keys = { "member_level", "meber_level", "mb_level", "user_level", "level" };
            for (int i = 0; i < keys.Length; i++)
            {
                string text = ReadString(data, keys[i], ReadString(root, keys[i], ""));
                int parsed;
                if (!int.TryParse(text, out parsed)) continue;
                if (parsed == 99) return 99;
                if (parsed < 1) return 1;
                if (parsed > 10) return 10;
                return parsed;
            }
            return 1;
        }

        private static bool ToBool(object value)
        {
            if (value is bool) return (bool)value;
            string text = value == null ? "" : Convert.ToString(value).Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        private static OviaErpLaunchResult Fail(string message)
        {
            return new OviaErpLaunchResult { IsSuccess = false, Message = message ?? "ERP OVIA 실행 인증에 실패했습니다." };
        }
    }
}
