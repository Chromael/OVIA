using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OVIA.Desktop
{
    public sealed class OviaErpAuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public bool HasAuthenticationResponse { get; set; }
        public string Message { get; set; }
        public string AuthenticationUrl { get; set; }
        public string RequestMethod { get; set; }
        public int HttpStatusCode { get; set; }
        public string RawResponse { get; set; }
        public int UserLevel { get; set; }
    }

    public sealed class OviaErpSessionCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
        public DateTime? ExpiresUtc { get; set; }
    }

    /// <summary>
    /// 시스템 설정의 ERP 사용자 인증 주소에 OVIA 로그인 정보를 전송합니다.
    /// ERP 인증 페이지가 $_POST만 사용하므로 POST(application/x-www-form-urlencoded)만 사용합니다.
    /// 301/302/303/307/308 리다이렉트가 발생하더라도 POST 본문을 유지한 채 새 주소로 다시 전송합니다.
    /// ERP가 res=false와 msg를 반환하면 msg를 변경하지 않고 그대로 사용자에게 전달합니다.
    /// </summary>
    public static class OviaErpAuthenticationService
    {
        private static readonly object SyncRoot = new object();
        private static List<OviaErpSessionCookie> sessionCookies = new List<OviaErpSessionCookie>();

        public static IList<OviaErpSessionCookie> GetSessionCookies()
        {
            lock (SyncRoot)
            {
                return new List<OviaErpSessionCookie>(sessionCookies);
            }
        }

        public static async Task<OviaErpAuthenticationResult> AuthenticateAsync(string companyId, string userId, string password)
        {
            string authUrl = OviaSystemSettingsStore.GetErpAuthUrl();
            Uri authUri;

            if (!Uri.TryCreate(authUrl, UriKind.Absolute, out authUri) ||
                (authUri.Scheme != Uri.UriSchemeHttp && authUri.Scheme != Uri.UriSchemeHttps))
            {
                return Failure(
                    "ERP 사용자 인증주소가 올바르지 않습니다.\r\n시스템 설정의 ERP 연결 정보를 확인해주세요.",
                    authUrl,
                    "",
                    0,
                    "",
                    false);
            }

            companyId = (companyId ?? "").Trim();
            userId = (userId ?? "").Trim();
            password = password ?? "";

            CookieContainer cookieContainer = new CookieContainer();

            try
            {
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.CookieContainer = cookieContainer;
                    handler.UseCookies = true;
                    handler.AllowAutoRedirect = false;

                    using (HttpClient client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("OVIA-Desktop/1.0");

                        OviaErpAuthenticationResult postResult = await SendPostFollowingRedirectsAsync(
                            client,
                            authUri,
                            companyId,
                            userId,
                            password);

                        if (postResult.HasAuthenticationResponse)
                        {
                            return CompleteResult(postResult, cookieContainer, authUri);
                        }

                        ClearSessionCookies();
                        return postResult;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                ClearSessionCookies();
                return Failure(
                    "ERP 인증 서버 응답 시간이 초과되었습니다.\r\n네트워크 또는 ERP 서버 상태를 확인해주세요.",
                    authUrl,
                    "POST",
                    0,
                    "",
                    false);
            }
            catch (HttpRequestException ex)
            {
                ClearSessionCookies();
                return Failure(
                    "ERP 인증 서버에 연결할 수 없습니다.\r\n" + ex.Message,
                    authUrl,
                    "POST",
                    0,
                    "",
                    false);
            }
            catch (Exception ex)
            {
                ClearSessionCookies();
                return Failure(
                    "ERP 로그인 처리 중 오류가 발생했습니다.\r\n" + ex.Message,
                    authUrl,
                    "POST",
                    0,
                    "",
                    false);
            }
        }

        private static async Task<OviaErpAuthenticationResult> SendPostFollowingRedirectsAsync(
            HttpClient client,
            Uri initialUri,
            string companyId,
            string userId,
            string password)
        {
            const int maxRedirects = 5;
            Uri requestUri = initialUri;

            for (int redirectCount = 0; redirectCount <= maxRedirects; redirectCount++)
            {
                Dictionary<string, string> fields = CreateFields(companyId, userId, password);

                using (FormUrlEncodedContent content = new FormUrlEncodedContent(fields))
                using (HttpResponseMessage response = await client.PostAsync(requestUri, content))
                {
                    int statusCode = (int)response.StatusCode;

                    if (IsRedirectStatus(response.StatusCode))
                    {
                        if (response.Headers.Location == null)
                        {
                            return Failure(
                                "ERP 인증 서버가 리다이렉트 응답을 반환했지만 이동 주소가 없습니다.",
                                initialUri.AbsoluteUri,
                                "POST",
                                statusCode,
                                "",
                                false);
                        }

                        if (redirectCount >= maxRedirects)
                        {
                            return Failure(
                                "ERP 인증 서버의 리다이렉트 횟수가 허용 범위를 초과했습니다.",
                                initialUri.AbsoluteUri,
                                "POST",
                                statusCode,
                                "",
                                false);
                        }

                        requestUri = response.Headers.Location.IsAbsoluteUri
                            ? response.Headers.Location
                            : new Uri(requestUri, response.Headers.Location);

                        // HttpClient 자동 리다이렉트를 사용하지 않고 POST 본문을 유지하여 재전송합니다.
                        continue;
                    }

                    string responseText = await response.Content.ReadAsStringAsync();
                    return ParseResult(responseText, requestUri.AbsoluteUri, "POST", statusCode);
                }
            }

            return Failure(
                "ERP 인증 요청을 완료하지 못했습니다.",
                initialUri.AbsoluteUri,
                "POST",
                0,
                "",
                false);
        }

        private static bool IsRedirectStatus(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        private static Dictionary<string, string> CreateFields(string companyId, string userId, string password)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>();
            fields["site_id"] = companyId;
            fields["erp_id"] = userId;
            fields["erp_pwd"] = password;
            return fields;
        }

        private static string BuildQueryString(Dictionary<string, string> fields)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(Uri.EscapeDataString(field.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(field.Value ?? ""));
            }
            return builder.ToString();
        }

        private static OviaErpAuthenticationResult CompleteResult(
            OviaErpAuthenticationResult result,
            CookieContainer cookieContainer,
            Uri authUri)
        {
            if (!result.IsSuccess)
            {
                ClearSessionCookies();
                return result;
            }

            StoreCookies(cookieContainer, authUri);
            return result;
        }

        private static OviaErpAuthenticationResult ParseResult(
            string responseText,
            string authUrl,
            string requestMethod,
            int httpStatusCode)
        {
            string rawResponse = NormalizeResponseText(responseText);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return Failure(
                    "ERP 인증 서버가 빈 응답을 반환했습니다.",
                    authUrl,
                    requestMethod,
                    httpStatusCode,
                    rawResponse,
                    false);
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object root = serializer.DeserializeObject(rawResponse);
                IDictionary<string, object> data = ToStringObjectDictionary(root);

                if (data == null)
                {
                    return Failure(
                        "ERP 인증 서버의 JSON 최상위 구조가 객체 형식이 아닙니다.",
                        authUrl,
                        requestMethod,
                        httpStatusCode,
                        rawResponse,
                        false);
                }

                object resValue;
                if (!TryGetValueIgnoreCase(data, "res", out resValue))
                {
                    return Failure(
                        "ERP 인증 응답에서 res 값을 찾을 수 없습니다.",
                        authUrl,
                        requestMethod,
                        httpStatusCode,
                        rawResponse,
                        false);
                }

                bool success = ConvertToBoolean(resValue);
                object msgValue;
                string message = TryGetValueIgnoreCase(data, "msg", out msgValue) && msgValue != null
                    ? Convert.ToString(msgValue).Trim()
                    : "";

                if (success)
                {
                    int userLevel = ReadUserLevel(data);
                    return new OviaErpAuthenticationResult
                    {
                        IsSuccess = true,
                        HasAuthenticationResponse = true,
                        Message = message,
                        AuthenticationUrl = authUrl,
                        RequestMethod = requestMethod,
                        HttpStatusCode = httpStatusCode,
                        RawResponse = rawResponse,
                        UserLevel = userLevel
                    };
                }

                // ERP가 실패 메시지를 주면 한 글자도 임의 변경하지 않고 그대로 전달합니다.
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "ERP 인증 서버가 res=false를 반환했지만 msg 값은 비어 있습니다.";
                }

                return Failure(
                    message,
                    authUrl,
                    requestMethod,
                    httpStatusCode,
                    rawResponse,
                    true);
            }
            catch (Exception ex)
            {
                return Failure(
                    "ERP 인증 서버의 응답이 올바른 JSON 형식이 아닙니다.\r\n" + ex.Message,
                    authUrl,
                    requestMethod,
                    httpStatusCode,
                    rawResponse,
                    false);
            }
        }

        private static int ReadUserLevel(IDictionary<string, object> data)
        {
            string[] keys = { "member_level", "meber_level", "mb_level", "user_level", "level" };
            foreach (string key in keys)
            {
                object value;
                if (!TryGetValueIgnoreCase(data, key, out value) || value == null)
                {
                    continue;
                }

                int parsed;
                if (int.TryParse(Convert.ToString(value).Trim(), out parsed))
                {
                    if (parsed == 99) return 99;
                    if (parsed < 1) return 1;
                    if (parsed > 10) return 10;
                    return parsed;
                }
            }

            return 1;
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
            if (data.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (KeyValuePair<string, object> pair in data)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string NormalizeResponseText(string responseText)
        {
            string text = responseText ?? "";
            text = text.Trim();
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1).TrimStart();
            }
            return text;
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

        private static void StoreCookies(CookieContainer container, Uri uri)
        {
            List<OviaErpSessionCookie> cookies = new List<OviaErpSessionCookie>();

            foreach (Cookie cookie in container.GetCookies(uri))
            {
                cookies.Add(new OviaErpSessionCookie
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = string.IsNullOrWhiteSpace(cookie.Domain) ? uri.Host : cookie.Domain.TrimStart('.'),
                    Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                    IsSecure = cookie.Secure,
                    IsHttpOnly = cookie.HttpOnly,
                    ExpiresUtc = cookie.Expires == DateTime.MinValue ? (DateTime?)null : cookie.Expires.ToUniversalTime()
                });
            }

            lock (SyncRoot)
            {
                sessionCookies = cookies;
            }
        }

        private static void ClearSessionCookies()
        {
            lock (SyncRoot)
            {
                sessionCookies = new List<OviaErpSessionCookie>();
            }
        }

        private static OviaErpAuthenticationResult Failure(
            string message,
            string authUrl,
            string requestMethod,
            int httpStatusCode,
            string rawResponse,
            bool hasAuthenticationResponse)
        {
            return new OviaErpAuthenticationResult
            {
                IsSuccess = false,
                HasAuthenticationResponse = hasAuthenticationResponse,
                Message = message,
                AuthenticationUrl = authUrl,
                RequestMethod = requestMethod,
                HttpStatusCode = httpStatusCode,
                RawResponse = rawResponse
            };
        }
    }
}
