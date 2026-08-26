using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace OVIA.Desktop
{
    public class OviaNotificationEntry
    {
        public string Id = "";
        public string CompanyId = "";
        public string UserId = "";
        public string WorkContent = "";
        public string WorkPath = "";
        public DateTime WorkDate = DateTime.Now;
        public string Worker = "";
        public string Ip = "";
        public bool IsConfirmed = true;
    }

    public static class OviaNotificationStore
    {
        public static event EventHandler NotificationsChanged;
        public static string LastError = "";

        public static void AddWorkLog(string companyId, string userId, string workContent, string workPath)
        {
            AddWorkLog(companyId, userId, workContent, workPath, userId);
        }

        public static void AddWorkLog(string companyId, string userId, string workContent, string workPath, string worker)
        {
            if (string.IsNullOrWhiteSpace(workContent)) return;
            try
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["route"] = Safe(workPath);
                payload["description"] = Safe(workContent);
                Post(companyId, "ovia_log_write", payload);
                LastError = "";
                RaiseNotificationsChanged();
            }
            catch (Exception ex)
            {
                // 로그 전송 실패가 기존 저장/로그인/ERP 동작을 막아서는 안 된다.
                LastError = ex.Message;
            }
        }

        public static int GetUnreadCount(string companyId, string userId)
        {
            // ERP log_ovia는 읽음/미확인 상태를 보관하지 않으므로 배지는 사용하지 않는다.
            return 0;
        }

        public static List<OviaNotificationEntry> GetVisibleEntries(string companyId, string userId)
        {
            List<OviaNotificationEntry> result = new List<OviaNotificationEntry>();
            try
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["days"] = 7;
                IDictionary<string, object> root = Post(companyId, "ovia_log_list", payload);
                object dataValue;
                if (!root.TryGetValue("data", out dataValue) || dataValue == null) return result;

                object[] rows = dataValue as object[];
                ArrayList arrayList = dataValue as ArrayList;
                if (rows == null && arrayList != null) rows = arrayList.ToArray();
                if (rows == null) return result;

                for (int i = 0; i < rows.Length; i++)
                {
                    IDictionary<string, object> row = rows[i] as IDictionary<string, object>;
                    if (row == null) continue;
                    OviaNotificationEntry entry = new OviaNotificationEntry();
                    entry.Id = ReadString(row, "idx");
                    entry.CompanyId = ReadString(row, "site_id");
                    entry.UserId = ReadString(row, "mb_id");
                    entry.Worker = entry.UserId;
                    entry.WorkPath = ReadString(row, "route");
                    entry.WorkContent = ReadString(row, "description");
                    entry.Ip = ReadString(row, "ip");
                    DateTime dt;
                    if (!DateTime.TryParse(ReadString(row, "created_at"), out dt)) dt = DateTime.Now;
                    entry.WorkDate = dt;
                    entry.IsConfirmed = true;
                    result.Add(entry);
                }
                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

        public static void Confirm(string id) { }
        public static void ConfirmMany(List<string> ids) { }
        public static string GetNotificationFilePath() { return ""; }

        private static IDictionary<string, object> Post(string companyId, string mode, Dictionary<string, object> payload)
        {
            string token;
            if (!OviaErpAuthenticationService.TryGetCurrentErpApiToken(companyId, out token))
                throw new Exception("ERP API 인증정보가 없습니다. 다시 로그인해주세요.");

            string authBase = OviaCompanyConnectionStore.GetErpAuthUrl(companyId);
            Uri baseUri;
            if (!Uri.TryCreate((authBase ?? "").TrimEnd('/') + "/", UriKind.Absolute, out baseUri))
                throw new Exception("ERP 연결 주소가 올바르지 않습니다.");

            Uri endpointBase = new Uri(baseUri, "ovia_api.php");
            UriBuilder builder = new UriBuilder(endpointBase);
            builder.Query = "mode=" + Uri.EscapeDataString(mode ?? "");

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string json = serializer.Serialize(payload ?? new Dictionary<string, object>());

            using (HttpClientHandler handler = new HttpClientHandler())
            using (HttpClient client = new HttpClient(handler))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, builder.Uri))
            {
                handler.AllowAutoRedirect = false;
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                client.Timeout = TimeSpan.FromSeconds(8);
                request.Headers.TryAddWithoutValidation("Authorization", token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult();
                string responseText = response.Content == null ? "" : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode) throw new Exception("ERP 로그 API HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                object parsed = serializer.DeserializeObject(responseText);
                IDictionary<string, object> root = parsed as IDictionary<string, object>;
                if (root == null) throw new Exception("ERP 로그 API 응답 형식이 올바르지 않습니다.");
                string res = ReadString(root, "res");
                bool ok = string.Equals(res, "true", StringComparison.OrdinalIgnoreCase) || res == "1";
                object boolValue;
                if (root.TryGetValue("res", out boolValue) && boolValue is bool) ok = (bool)boolValue;
                if (!ok)
                {
                    string msg = ReadString(root, "msg");
                    throw new Exception(string.IsNullOrWhiteSpace(msg) ? "ERP 로그 API 요청이 거부되었습니다." : msg);
                }
                return root;
            }
        }

        private static string ReadString(IDictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static void RaiseNotificationsChanged()
        {
            EventHandler handler = NotificationsChanged;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static string Safe(string value) { return value == null ? "" : value.Trim(); }
    }
}
