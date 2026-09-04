using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OVIA.Desktop
{
    public sealed class OviaErpProjectListResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public int HttpStatusCode { get; set; }
        public List<OviaProjectRow> Projects { get; set; }
        public string SessionCompanyId { get; set; }
        public string SessionUserId { get; set; }
        public string SessionUserName { get; set; }
        public string SessionIpAddress { get; set; }

        public OviaErpProjectListResult()
        {
            Projects = new List<OviaProjectRow>();
        }
    }

    /// <summary>
    /// 로그인 성공 시 메모리에 보관한 64자리 ovia_token으로 ERP API를 호출합니다.
    /// 토큰은 Authorization 헤더에 원문 그대로 전달하며 파일/설정/로그에 저장하지 않습니다.
    /// </summary>
    public static class OviaErpApiService
    {
        private const string ProjectListRelativePath = "api/ovia_api.php?mode=project_list";

        // project_list 응답의 현재 ERP 세션 사용자 정보를 메모리에만 보관한다.
        // BarList 작성자 ID가 현재 로그인 사용자와 같고 pull 응답에 표시명이 없을 때
        // ID 대신 세션 사용자명을 표시하기 위한 안전한 fallback 용도다.
        public static string CurrentSessionUserId { get; private set; } = "";
        public static string CurrentSessionUserName { get; private set; } = "";

        public static async Task<OviaErpProjectListResult> GetProjectListAsync(string companyId)
        {
            companyId = companyId == null ? "" : companyId.Trim();

            string token;
            if (!OviaErpAuthenticationService.TryGetCurrentErpApiToken(companyId, out token))
            {
                return Failure("ERP API 인증정보를 확인할 수 없습니다. 다시 로그인해주세요.", 0);
            }

            string requestUrl = BuildProjectListApiUrl(companyId);
            Uri requestUri;
            if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out requestUri)
                || (requestUri.Scheme != Uri.UriSchemeHttp && requestUri.Scheme != Uri.UriSchemeHttps))
            {
                return Failure("ERP 연결정보를 확인할 수 없습니다. 시스템 설정의 ERP 연결 경로를 확인해주세요.", 0);
            }

            try
            {
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.AllowAutoRedirect = false;
                    handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                    using (HttpClient client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("OVIA-Desktop/1.0");
                        return await SendProjectListRequestAsync(client, requestUri, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return Failure("ERP 공사목록 서버 응답 시간이 초과되었습니다. 네트워크 또는 ERP 서버 상태를 확인해주세요.", 0);
            }
            catch (HttpRequestException ex)
            {
                return Failure("ERP 공사목록 서버에 연결할 수 없습니다.\r\n" + ex.Message, 0);
            }
            catch (Exception ex)
            {
                return Failure("ERP 공사목록 조회 중 오류가 발생했습니다.\r\n" + ex.Message, 0);
            }
        }

        private static async Task<OviaErpProjectListResult> SendProjectListRequestAsync(
            HttpClient client,
            Uri initialUri,
            string token)
        {
            const int maxRedirects = 5;
            Uri requestUri = initialUri;

            for (int redirectCount = 0; redirectCount <= maxRedirects; redirectCount++)
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    // ERP 계약: Bearer를 붙이지 않고 발급받은 64자리 토큰 원문을 그대로 전달한다.
                    request.Headers.TryAddWithoutValidation("Authorization", token);

                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        if (IsRedirectStatus(response.StatusCode))
                        {
                            if (response.Headers.Location == null)
                            {
                                return Failure("ERP 공사목록 서버가 이동 응답을 반환했지만 이동 주소가 없습니다.", (int)response.StatusCode);
                            }

                            if (redirectCount >= maxRedirects)
                            {
                                return Failure("ERP 공사목록 서버의 이동 횟수가 허용 범위를 초과했습니다.", (int)response.StatusCode);
                            }

                            Uri nextUri = response.Headers.Location.IsAbsoluteUri
                                ? response.Headers.Location
                                : new Uri(requestUri, response.Headers.Location);

                            if (!IsSafeRedirect(initialUri, nextUri))
                            {
                                return Failure("ERP 공사목록 요청이 다른 서버로 이동되어 보안을 위해 중단했습니다. ERP 연결 주소를 확인해주세요.", (int)response.StatusCode);
                            }

                            requestUri = nextUri;
                            continue;
                        }

                        string responseText = response.Content == null
                            ? ""
                            : await response.Content.ReadAsStringAsync();

                        OviaErpProjectListResult parsed = ParseProjectListResponse(responseText, (int)response.StatusCode);
                        if (parsed.IsSuccess)
                        {
                            return parsed;
                        }

                        // 401/403은 서버 본문이 JSON이 아니더라도 인증 오류를 우선 안내한다.
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            return Failure("ERP API 인증이 만료되었거나 공사목록 조회 권한이 없습니다. 다시 로그인해주세요.", (int)response.StatusCode);
                        }

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return Failure(
                                "ERP 공사목록 API를 찾을 수 없습니다.\r\n"
                                + "요청 주소: " + requestUri.AbsoluteUri + "\r\n"
                                + "시스템 설정의 기본 도메인과 ERP 연결 경로를 확인해주세요.",
                                (int)response.StatusCode);
                        }

                        if (!string.IsNullOrWhiteSpace(parsed.Message))
                        {
                            return parsed;
                        }

                        return Failure("ERP 공사목록을 불러오지 못했습니다.", (int)response.StatusCode);
                    }
                }
            }

            return Failure("ERP 공사목록 요청을 완료하지 못했습니다.", 0);
        }

        private static bool IsRedirectStatus(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        private static bool IsSafeRedirect(Uri initialUri, Uri nextUri)
        {
            if (initialUri == null || nextUri == null)
            {
                return false;
            }

            bool validScheme = nextUri.Scheme == Uri.UriSchemeHttp || nextUri.Scheme == Uri.UriSchemeHttps;
            return validScheme && string.Equals(initialUri.Host, nextUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildProjectListApiUrl(string companyId)
        {
            // ERP API는 로그인 인증 경로와 같은 base를 사용한다.
            // 예) 기본 도메인=https://dev03.celmon.com
            //     ERP 연결 경로=erp
            //     ERP 사용자 인증 경로=auth
            //     -> https://dev03.celmon.com/erp/auth/ovia_api.php?mode=project_list
            string authBaseUrl = OviaCompanyConnectionStore.GetErpAuthUrl(companyId);
            Uri authBaseUri;
            if (string.IsNullOrWhiteSpace(authBaseUrl)
                || !Uri.TryCreate(authBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out authBaseUri)
                || (authBaseUri.Scheme != Uri.UriSchemeHttp && authBaseUri.Scheme != Uri.UriSchemeHttps))
            {
                return "";
            }

            Uri endpointUri = new Uri(authBaseUri, "ovia_api.php");
            UriBuilder builder = new UriBuilder(endpointUri);
            builder.Query = "mode=project_list";
            return builder.Uri.AbsoluteUri;
        }

        private static OviaErpProjectListResult ParseProjectListResponse(string responseText, int httpStatusCode)
        {
            string raw = ExtractJsonObject(NormalizeResponseText(responseText));
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Failure("ERP 공사목록 서버가 빈 응답을 반환했습니다.", httpStatusCode);
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                serializer.RecursionLimit = 100;
                object root = serializer.DeserializeObject(raw);
                IDictionary<string, object> data = ToStringObjectDictionary(root);
                if (data == null)
                {
                    return Failure("ERP 공사목록 응답 형식이 올바르지 않습니다.", httpStatusCode);
                }

                object resValue;
                if (!TryGetValueIgnoreCase(data, "res", out resValue))
                {
                    return Failure("ERP 공사목록 응답에서 res 값을 찾을 수 없습니다.", httpStatusCode);
                }

                bool success = ConvertToBoolean(resValue);
                object msgValue;
                string message = TryGetValueIgnoreCase(data, "msg", out msgValue) && msgValue != null
                    ? Convert.ToString(msgValue).Trim()
                    : "";

                if (!success)
                {
                    return Failure(
                        string.IsNullOrWhiteSpace(message)
                            ? "공사목록 조회 중 오류가 발생했습니다."
                            : message,
                        httpStatusCode);
                }

                object listValue;
                if (!TryGetValueIgnoreCase(data, "data", out listValue) || listValue == null)
                {
                    return Failure("ERP 공사목록 응답에서 data 값을 찾을 수 없습니다.", httpStatusCode);
                }

                List<OviaProjectRow> projects = new List<OviaProjectRow>();
                IEnumerable items = listValue as IEnumerable;
                if (items == null || listValue is string)
                {
                    return Failure("ERP 공사목록 data 형식이 올바르지 않습니다.", httpStatusCode);
                }

                foreach (object item in items)
                {
                    IDictionary<string, object> row = ToStringObjectDictionary(item);
                    if (row == null || row.Count == 0)
                    {
                        continue;
                    }

                    projects.Add(new OviaProjectRow(
                        ReadString(row, "project_no"),
                        ReadString(row, "project_name"),
                        ReadString(row, "customer_name"),
                        ConvertCompletionStatus(ReadObject(row, "is_completed")),
                        ReadString(row, "created_at"),
                        ReadString(row, "updated_at"),
                        ReadString(row, "manager_name"),
                        ReadString(row, "remark")
                    ));
                }

                string sessionCompanyId = "";
                string sessionUserId = "";
                string sessionUserName = "";
                string sessionIpAddress = "";

                object sessionValue;
                if (TryGetValueIgnoreCase(data, "session", out sessionValue) && sessionValue != null)
                {
                    IDictionary<string, object> session = ToStringObjectDictionary(sessionValue);
                    if (session != null)
                    {
                        sessionCompanyId = ReadString(session, "company_id");
                        sessionUserId = ReadString(session, "user_id");
                        sessionUserName = ReadString(session, "user_name");
                        sessionIpAddress = ReadString(session, "ip");
                    }
                }

                CurrentSessionUserId = sessionUserId == null ? "" : sessionUserId.Trim();
                CurrentSessionUserName = sessionUserName == null ? "" : sessionUserName.Trim();

                return new OviaErpProjectListResult
                {
                    IsSuccess = true,
                    Message = message,
                    HttpStatusCode = httpStatusCode,
                    Projects = projects,
                    SessionCompanyId = sessionCompanyId,
                    SessionUserId = sessionUserId,
                    SessionUserName = sessionUserName,
                    SessionIpAddress = sessionIpAddress
                };
            }
            catch (Exception)
            {
                return Failure("ERP 공사목록 서버의 JSON 응답을 해석하지 못했습니다. ERP API 응답 형식을 확인해주세요.", httpStatusCode);
            }
        }

        private static string ConvertCompletionStatus(object value)
        {
            if (value == null)
            {
                return "진행중";
            }

            if (value is bool)
            {
                return (bool)value ? "완료" : "진행중";
            }

            string text = Convert.ToString(value).Trim();
            if (text.Equals("1", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || text.Equals("YES", StringComparison.OrdinalIgnoreCase)
                || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("완료", StringComparison.OrdinalIgnoreCase))
            {
                return "완료";
            }

            return "진행중";
        }

        private static object ReadObject(IDictionary<string, object> data, string key)
        {
            object value;
            return TryGetValueIgnoreCase(data, key, out value) ? value : null;
        }

        private static string ReadString(IDictionary<string, object> data, string key)
        {
            object value = ReadObject(data, key);
            return value == null ? "" : Convert.ToString(value).Trim();
        }

        private static IDictionary<string, object> ToStringObjectDictionary(object value)
        {
            IDictionary<string, object> typed = value as IDictionary<string, object>;
            if (typed != null)
            {
                return typed;
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary == null)
            {
                return null;
            }

            Dictionary<string, object> converted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                converted[Convert.ToString(entry.Key)] = entry.Value;
            }
            return converted;
        }

        private static bool TryGetValueIgnoreCase(IDictionary<string, object> data, string key, out object value)
        {
            value = null;
            if (data == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, object> item in data)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool ConvertToBoolean(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            if (value == null)
            {
                return false;
            }

            string text = Convert.ToString(value).Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }

        private static string ExtractJsonObject(string responseText)
        {
            string text = responseText ?? "";
            if (text == "")
            {
                return "";
            }

            // PHP Warning/Notice, UTF-8 BOM 또는 기타 텍스트가 JSON 앞뒤에 붙어도
            // 실제 최상위 JSON 객체만 안전하게 잘라 파싱한다.
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < firstBrace)
            {
                return "";
            }

            return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        private static string NormalizeResponseText(string responseText)
        {
            string text = responseText ?? "";
            text = text.Replace("\0", "").Trim();

            while (text.Length > 0 && (text[0] == '\uFEFF' || text[0] == '\u200B'))
            {
                text = text.Substring(1).TrimStart();
            }

            return text;
        }

        private static OviaErpProjectListResult Failure(string message, int httpStatusCode)
        {
            return new OviaErpProjectListResult
            {
                IsSuccess = false,
                Message = message,
                HttpStatusCode = httpStatusCode,
                Projects = new List<OviaProjectRow>()
            };
        }
    }
}
