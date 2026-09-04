using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OVIA.Desktop
{
    public sealed class OviaErpBarListSyncResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public int BarListId { get; set; }
        public int SyncedItemCount { get; set; }
        public string LocalCachePath { get; set; }
        internal string RawResponse { get; set; }
    }

    /// <summary>
    /// 공사별 BarList의 ERP 동기화 전용 서비스.
    /// CAD 추출/형상 편집 계약은 변경하지 않고, 저장된 CSV + Shapes JSON만 전송 캐시로 사용한다.
    /// 동기화는 화면 진입/새로고침/저장/수정/삭제 액션에서만 수행하며 polling 하지 않는다.
    /// </summary>
    public static class OviaErpBarListSyncService
    {
        private const string ErpIdHeader = "OVIA_ERP_BARLIST_IDX";
        private const string ShapeHeader = "OVIA_CAD_SHAPE_JSON";
        private const string SyncPendingHeader = "OVIA_ERP_SYNC_PENDING";
        private const string ErpListOrderHeader = "OVIA_ERP_LIST_ORDER";
        private const string FinalLengthHeader = "OVIA_FINAL_LENGTH";

        /// <summary>
        /// 신규 BarList 상세 화면에서 사용자가 '저장완료'를 확정한 시점에 ERP 헤더만 생성합니다.
        /// 신규등록 팝업 저장 시에는 호출하지 않으며, CAD 철근 items가 0건인 최초 저장 전용입니다.
        /// </summary>
        public static async Task<OviaErpBarListSyncResult> RegisterNewBarListAsync(
            string companyId,
            string projectNo,
            BarListEditResult registration)
        {
            if (registration == null) return Fail("신규 BarList 등록정보가 없습니다.");
            if (string.IsNullOrWhiteSpace(projectNo)) return Fail("공사번호가 없습니다.");
            if (string.IsNullOrWhiteSpace(registration.Title)) return Fail("BarList 제목이 없습니다.");

            try
            {
                Dictionary<string, object> payload = BuildRegistrationPayload(projectNo, registration);
                return await PostAsync(companyId, "barlist_sync_register", payload);
            }
            catch (Exception ex)
            {
                return Fail("ERP 신규 BarList 등록 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        public static async Task<OviaErpBarListSyncResult> PushSavedBarListAsync(string companyId, string projectNo, string csvPath)
        {
            return await PushSavedBarListInternalAsync(companyId, projectNo, csvPath, false);
        }

        /// <summary>
        /// 공사별 BarList 목록의 우클릭 '수정' 전용 저장.
        /// 철근 상세행과 ERP 진행상태를 건드리지 않고 제목/기본정보만 즉시 UPDATE한다.
        /// 서버의 barlist_sync_push는 items 1건 이상을 요구하므로 빈 BarList 제목 수정에는 사용할 수 없다.
        /// </summary>
        public static async Task<OviaErpBarListSyncResult> PushSavedBarListMetadataAsync(string companyId, string projectNo, string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                return Fail("저장된 BarList 파일을 찾을 수 없습니다.");

            try
            {
                List<List<string>> rows = ReadCsv(csvPath);
                if (rows.Count < 2) return Fail("ERP로 전송할 BarList 메타정보가 없습니다.");

                List<string> headers = rows[0];
                int erpId = ParseInt(GetFirstValue(rows, headers, ErpIdHeader));
                if (erpId <= 0) return Fail("ERP BarList 식별자를 찾을 수 없습니다.");

                // PATCH 13: 목록 제목/기본정보 수정은 상세 items 전체 교체 API와 분리한다.
                // barlist_sync_meta_update는 기존 barlist_idx의 헤더 메타만 UPDATE하고
                // progress_status와 items를 요청/갱신하지 않는다.
                Dictionary<string, object> payload = BuildMetadataOnlyPushPayload(projectNo, erpId, rows, headers);
                OviaErpBarListSyncResult result = await PostAsync(companyId, "barlist_sync_meta_update", payload);

                // 메타 수정 실패를 CAD 철근데이터 미저장 상태로 취급하지 않는다.
                // OVIA_ERP_SYNC_PENDING은 검토 후 저장(CAD 데이터 push) 보호용이며,
                // 목록 제목 수정 실패 때문에 상세 화면의 '검토 후 저장' 버튼을 활성화하면 안 된다.
                return result;
            }
            catch (Exception ex)
            {
                return Fail("ERP BarList 메타정보 전송 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        private static async Task<OviaErpBarListSyncResult> PushSavedBarListInternalAsync(string companyId, string projectNo, string csvPath, bool unusedPreserveErpProgress)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                return Fail("저장된 BarList 파일을 찾을 수 없습니다.");

            try
            {
                List<List<string>> rows = ReadCsv(csvPath);
                if (rows.Count < 2) return Fail("ERP로 전송할 BarList 행이 없습니다.");

                List<string> headers = rows[0];
                int erpId = ParseInt(GetFirstValue(rows, headers, ErpIdHeader));
                Dictionary<string, object> payload = BuildPushPayload(projectNo, erpId, csvPath, rows, headers);
                OviaErpBarListSyncResult result = await PostAsync(companyId, "barlist_sync_push", payload);

                if (result.IsSuccess && result.BarListId > 0)
                {
                    // ERP가 성공 응답과 고유 idx를 반환한 즉시 로컬 BarList와 영구 연결한다.
                    // item_count 보조 검증이 0/누락이어도 idx 연결을 버리면 다음 저장 때 INSERT가 반복되어
                    // 동일 BarList가 ERP에 계속 생성될 수 있으므로, 고유 idx 보존을 최우선으로 한다.
                    // ERP가 실제 저장한 기준과 OVIA 로컬 CSV를 동일하게 유지한다.
                    // 주문량은 화면 fallback 값까지 ERP에 보낸 canonical 값으로 확정하고,
                    // 작성자는 신규등록 당시 writer_user_id를 최초 작성자로 유지하며 이후 저장에서 덮어쓰지 않는다.
                    PersistCanonicalMetaAfterPush(companyId, rows, headers);
                    PersistErpId(csvPath, rows, headers, result.BarListId);
                    SetSyncPending(csvPath, false);
                    result.LocalCachePath = CanonicalizeLocalCachePath(projectNo, csvPath, result.BarListId);
                    CleanupUnreferencedShapeFiles(Path.GetDirectoryName(result.LocalCachePath == "" ? csvPath : result.LocalCachePath));
                }
                else
                {
                    SetSyncPending(csvPath, true);
                }

                return result;
            }
            catch (Exception ex)
            {
                // 네트워크/응답 파싱/로컬 처리 예외도 다음 pull에서 덮어쓰지 않도록 보류 상태를 남긴다.
                SetSyncPending(csvPath, true);
                return Fail("ERP BarList 전송 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        public static async Task<OviaErpBarListSyncResult> PullProjectBarListsAsync(string companyId, string projectNo, string projectName, string localDirectory)
        {
            try
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["project_no"] = projectNo == null ? "" : projectNo.Trim();
                OviaErpBarListSyncResult call = await PostRawAsync(companyId, "barlist_sync_pull", payload);
                if (!call.IsSuccess) return call;
                string responseText = call.RawResponse;

                JavaScriptSerializer serializer = CreateSerializer();
                IDictionary<string, object> root = AsDictionary(serializer.DeserializeObject(ExtractJsonObject(responseText)));
                object dataValue;
                if (root == null || !TryGet(root, "data", out dataValue)) return Fail("ERP BarList 동기화 응답에 data가 없습니다.");

                object[] barlists = dataValue as object[];
                if (barlists == null)
                {
                    ArrayList arrayList = dataValue as ArrayList;
                    if (arrayList != null) barlists = arrayList.ToArray();
                }
                if (barlists == null) barlists = new object[0];

                Directory.CreateDirectory(localDirectory);
                HashSet<int> serverIds = new HashSet<int>();
                for (int i = 0; i < barlists.Length; i++)
                {
                    IDictionary<string, object> barlist = AsDictionary(barlists[i]);
                    if (barlist == null) continue;
                    int id = ReadInt(barlist, "barlist_idx");
                    if (id <= 0) continue;
                    serverIds.Add(id);
                    MaterializeBarList(localDirectory, projectNo, id, barlist, i);
                }

                RemoveServerDeletedLocalCaches(localDirectory, serverIds);
                CleanupUnreferencedShapeFiles(localDirectory);
                return Ok("ERP BarList 동기화 완료", 0);
            }
            catch (Exception ex)
            {
                return Fail("ERP BarList 조회 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        /// <summary>
        /// 현재 BarList의 철근행/형상 파일은 건드리지 않고 ERP 헤더 메타만 다시 수신한다.
        /// 저장 직후 ERP의 진행상태(대기/진행중/완료)를 로컬 canonical CSV에 반영하기 위한 경량 Pull이다.
        /// </summary>
        public static async Task<OviaErpBarListSyncResult> RefreshBarListMetadataFromErpAsync(
            string companyId,
            string projectNo,
            string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                return Fail("ERP 메타정보를 반영할 BarList 파일을 찾을 수 없습니다.");

            try
            {
                List<List<string>> localRows = ReadCsv(csvPath);
                if (localRows.Count < 2) return Fail("BarList 로컬 메타정보가 없습니다.");

                List<string> headers = localRows[0];
                int erpId = ParseInt(GetFirstValue(localRows, headers, ErpIdHeader));
                if (erpId <= 0) return Fail("ERP BarList 식별자를 찾을 수 없습니다.");

                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["project_no"] = projectNo == null ? "" : projectNo.Trim();
                OviaErpBarListSyncResult call = await PostRawAsync(companyId, "barlist_sync_pull", payload);
                if (!call.IsSuccess) return call;

                JavaScriptSerializer serializer = CreateSerializer();
                IDictionary<string, object> root = AsDictionary(serializer.DeserializeObject(ExtractJsonObject(call.RawResponse)));
                object dataValue;
                if (root == null || !TryGet(root, "data", out dataValue))
                    return Fail("ERP BarList 메타 재조회 응답에 data가 없습니다.");

                object[] barlists = dataValue as object[];
                if (barlists == null)
                {
                    ArrayList arrayList = dataValue as ArrayList;
                    if (arrayList != null) barlists = arrayList.ToArray();
                }
                if (barlists == null) barlists = new object[0];

                IDictionary<string, object> matched = null;
                for (int i = 0; i < barlists.Length; i++)
                {
                    IDictionary<string, object> candidate = AsDictionary(barlists[i]);
                    if (candidate != null && ReadInt(candidate, "barlist_idx") == erpId)
                    {
                        matched = candidate;
                        break;
                    }
                }

                if (matched == null)
                    return Fail("ERP에서 현재 BarList를 찾을 수 없습니다.");

                IDictionary<string, object> meta = null;
                object metaValue;
                if (TryGet(matched, "meta", out metaValue)) meta = AsDictionary(metaValue);
                if (meta == null) meta = matched;

                string[] headerArray = headers.ToArray();
                for (int r = 1; r < localRows.Count; r++)
                {
                    while (localRows[r].Count < headers.Count) localRows[r].Add("");
                    ApplyMeta(localRows[r], headerArray, meta, matched);
                    Set(localRows[r], headerArray, ErpIdHeader, erpId.ToString(CultureInfo.InvariantCulture));
                }

                WriteCsvIfChanged(csvPath, localRows);
                return Ok("ERP BarList 메타정보 갱신 완료", erpId);
            }
            catch (Exception ex)
            {
                return Fail("ERP BarList 메타정보 재조회 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        public static async Task<OviaErpBarListSyncResult> DeleteBarListAsync(string companyId, string projectNo, string csvPath)
        {
            try
            {
                List<List<string>> rows = ReadCsv(csvPath);
                int id = rows.Count == 0 ? 0 : ParseInt(GetFirstValue(rows, rows[0], ErpIdHeader));
                if (id <= 0)
                {
                    // 삭제 권한과 발주/출하 등 업무 상태를 ERP가 최종 판단해야 한다.
                    // ERP 식별자가 없는 로컬 전용 데이터는 서버에서 검증할 수 없으므로 임의 삭제하지 않는다.
                    return Fail("ERP BarList 식별자가 없어 삭제 권한과 업무 상태를 확인할 수 없습니다. 먼저 ERP 저장 동기화를 완료한 뒤 다시 삭제해주세요.");
                }

                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["project_no"] = projectNo == null ? "" : projectNo.Trim();
                payload["barlist_idx"] = id;
                return await PostAsync(companyId, "barlist_sync_delete", payload);
            }
            catch (Exception ex)
            {
                return Fail("ERP BarList 삭제 동기화 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        public static int GetPersistedErpBarListId(string csvPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return 0;
                List<List<string>> rows = ReadCsv(csvPath);
                if (rows.Count < 2) return 0;
                return ParseInt(GetFirstValue(rows, rows[0], ErpIdHeader));
            }
            catch
            {
                return 0;
            }
        }

        public static void RestorePersistedErpBarListId(string csvPath, int erpId)
        {
            if (erpId <= 0 || string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return;
            List<List<string>> rows = ReadCsv(csvPath);
            if (rows.Count == 0) return;
            PersistErpId(csvPath, rows, rows[0], erpId);
        }

        /// <summary>
        /// barlist_sync_register 성공 후 빈 BarList 로컬 CSV에 ERP idx를 연결하고
        /// project_no + ERP idx canonical 파일명으로 확정합니다. 서버 재호출은 하지 않습니다.
        /// </summary>
        public static string FinalizeRegisteredEmptyBarListLocalCache(string projectNo, string csvPath, int erpId)
        {
            if (erpId <= 0 || string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return csvPath ?? "";

            List<List<string>> rows = ReadCsv(csvPath);
            if (rows.Count == 0) return csvPath;

            PersistErpId(csvPath, rows, rows[0], erpId);
            SetSyncPending(csvPath, false);
            string canonical = CanonicalizeLocalCachePath(projectNo, csvPath, erpId);
            CleanupUnreferencedShapeFiles(Path.GetDirectoryName(canonical));
            return canonical;
        }

        public static bool IsSynchronizationPending(string csvPath)
        {
            return IsSyncPending(csvPath);
        }

        public static void DeleteLocalShapeDirectory(string csvPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(csvPath);
                if (string.IsNullOrWhiteSpace(dir)) return;
                // Shapes는 여러 BarList가 공유할 수 있으므로 전체 폴더를 지우지 않는다.
                // ERP pull 전용 파일만 개별 정리한다.
                int id = GetPersistedErpBarListId(csvPath);
                if (id <= 0)
                {
                    string name = Path.GetFileNameWithoutExtension(csvPath);
                    int marker = name == null ? -1 : name.LastIndexOf("_ERP_", StringComparison.OrdinalIgnoreCase);
                    id = marker < 0 ? 0 : ParseInt(name.Substring(marker + 5));
                }
                if (id <= 0) return;
                string shapeDir = Path.Combine(dir, "Shapes");
                if (!Directory.Exists(shapeDir)) return;
                foreach (string file in Directory.GetFiles(shapeDir, "erp_" + id.ToString(CultureInfo.InvariantCulture) + "_*.json"))
                    File.Delete(file);
            }
            catch { }
        }

        private static Dictionary<string, object> BuildRegistrationPayload(string projectNo, BarListEditResult registration)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["project_no"] = projectNo == null ? "" : projectNo.Trim();

            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["barlist_status"] = "";
            meta["write_location"] = registration.WriteStatus ?? "";
            meta["order_number"] = "";
            meta["order_date"] = registration.OrderDate ?? "";
            meta["registered_date"] = registration.CreatedDate ?? "";
            meta["due_date"] = registration.DueDate ?? "";
            meta["building"] = registration.Building ?? "";
            meta["floor"] = registration.Floor ?? "";
            meta["work_type"] = registration.WorkType ?? "";
            // 진행상태는 ERP 단독 원장이다. 신규등록을 포함해 OVIA에서는 progress_status를 전송하지 않는다.
            // ERP가 신규 BarList의 초기 상태(예: 대기)를 결정하고 OVIA는 Pull로만 수신한다.
            meta["title"] = registration.Title ?? "";
            meta["tags"] = registration.Tags ?? "";
            meta["color_code"] = registration.Color ?? "";
            meta["order_qty"] = "";
            meta["tag_issue_status"] = "";
            meta["etc_status"] = "";
            meta["long_bar_status"] = "";
            meta["cutting_status"] = "";
            meta["bending_status"] = "";
            meta["shipment_qty"] = "";
            meta["unshipment_qty"] = "";
            meta["writer_user_id"] = registration.Writer ?? "";
            meta["remark"] = registration.Memo ?? "";
            payload["meta"] = meta;
            return payload;
        }


        private static Dictionary<string, object> BuildMetadataOnlyPushPayload(string projectNo, int erpId, List<List<string>> rows, List<string> headers)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["project_no"] = projectNo == null ? "" : projectNo.Trim();
            payload["barlist_idx"] = erpId;

            Dictionary<string, object> meta = new Dictionary<string, object>();
            AddMeta(meta, "barlist_status", rows, headers, "상태");
            AddMeta(meta, "write_location", rows, headers, "작성");
            AddMeta(meta, "order_number", rows, headers, "발주번호");
            AddMeta(meta, "order_date", rows, headers, "발주일");
            AddMeta(meta, "registered_date", rows, headers, "등록일");
            AddMeta(meta, "due_date", rows, headers, "납기일");
            AddMeta(meta, "building", rows, headers, "동");
            AddMeta(meta, "floor", rows, headers, "층");
            AddMeta(meta, "work_type", rows, headers, "공종");
            AddMeta(meta, "title", rows, headers, "제목");
            AddMeta(meta, "tags", rows, headers, "태그");
            AddMeta(meta, "color_code", rows, headers, "색상");
            meta["order_qty"] = ResolveOrderQtyForErp(rows, headers);
            AddMeta(meta, "tag_issue_status", rows, headers, "태그발행");
            AddMeta(meta, "etc_status", rows, headers, "기타");
            AddMeta(meta, "long_bar_status", rows, headers, "장대");
            AddMeta(meta, "cutting_status", rows, headers, "절단");
            AddMeta(meta, "bending_status", rows, headers, "절곡");
            AddMeta(meta, "shipment_qty", rows, headers, "출하");
            AddMeta(meta, "unshipment_qty", rows, headers, "미출하");
            AddMeta(meta, "writer_user_id", rows, headers, "작성자");
            AddMeta(meta, "remark", rows, headers, "OVIA_BARLIST_MEMO", "BARLIST_MEMO");

            // 중요: progress_status는 ERP 단독 원장이므로 포함하지 않는다.
            // 중요: items 키 자체도 포함하지 않는다. 우클릭 수정은 철근 상세를 건드리지 않는다.
            payload["meta"] = meta;
            return payload;
        }

        private static Dictionary<string, object> BuildPushPayload(string projectNo, int erpId, string csvPath, List<List<string>> rows, List<string> headers)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["project_no"] = projectNo == null ? "" : projectNo.Trim();
            payload["barlist_idx"] = erpId;

            Dictionary<string, object> meta = new Dictionary<string, object>();
            AddMeta(meta, "barlist_status", rows, headers, "상태");
            AddMeta(meta, "write_location", rows, headers, "작성");
            AddMeta(meta, "order_number", rows, headers, "발주번호");
            AddMeta(meta, "order_date", rows, headers, "발주일");
            AddMeta(meta, "registered_date", rows, headers, "등록일");
            AddMeta(meta, "due_date", rows, headers, "납기일");
            AddMeta(meta, "building", rows, headers, "동");
            AddMeta(meta, "floor", rows, headers, "층");
            AddMeta(meta, "work_type", rows, headers, "공종");
            // 진행상태는 ERP 단독 원장이다.
            // 신규등록, 제목/기본정보 수정, CAD 검토 후 저장을 포함해 어떤 OVIA 저장 경로에서도
            // progress_status를 ERP로 전송하지 않는다. ERP에서 결정된 상태는 Pull로만 수신한다.
            AddMeta(meta, "title", rows, headers, "제목");
            AddMeta(meta, "tags", rows, headers, "태그");
            AddMeta(meta, "color_code", rows, headers, "색상");
            meta["order_qty"] = ResolveOrderQtyForErp(rows, headers);
            AddMeta(meta, "tag_issue_status", rows, headers, "태그발행");
            AddMeta(meta, "etc_status", rows, headers, "기타");
            AddMeta(meta, "long_bar_status", rows, headers, "장대");
            AddMeta(meta, "cutting_status", rows, headers, "절단");
            AddMeta(meta, "bending_status", rows, headers, "절곡");
            AddMeta(meta, "shipment_qty", rows, headers, "출하");
            AddMeta(meta, "unshipment_qty", rows, headers, "미출하");
            // 신규등록 팝업에서 확정한 최초 작성자는 첫 barlist_sync_push INSERT에서 ERP에 함께 저장한다.
            AddMeta(meta, "writer_user_id", rows, headers, "작성자");
            AddMeta(meta, "remark", rows, headers, "OVIA_BARLIST_MEMO", "BARLIST_MEMO");
            payload["meta"] = meta;

            List<object> items = new List<object>();
            for (int r = 1; r < rows.Count; r++)
            {
                // 신규 BarList를 기본정보만으로 저장할 때 생성되는 메타 전용 로컬 1행은
                // ERP 철근 items로 전송하지 않는다. 따라서 CAD 미추출 저장은 items=[]로 생성된다.
                if (IsMetadataOnlyBarListRow(rows[r], headers))
                {
                    continue;
                }

                Dictionary<string, object> item = new Dictionary<string, object>();
                item["part"] = GetValue(rows[r], headers, "부위");
                item["source_row_no"] = GetValue(rows[r], headers, "번호");
                item["dia"] = GetValue(rows[r], headers, "철근규격", "규격");
                item["shape_json"] = ReadErpTransportShapeJson(csvPath, GetValue(rows[r], headers, ShapeHeader, "CAD_SHAPE_JSON"));
                item["length_mm"] = GetValue(rows[r], headers, "길이(mm)", "길이");
                item["final_length"] = GetValue(rows[r], headers, FinalLengthHeader, "final_length");
                item["qty_ea"] = GetValue(rows[r], headers, "수량(EA)", "수량");
                item["total_length_m"] = GetValue(rows[r], headers, "총길이(M)", "총길이");
                item["weight_ton"] = GetValue(rows[r], headers, "중량(Ton)", "중량");
                item["remark"] = GetValue(rows[r], headers, "비고");
                item["source_drawing_name"] = GetValue(rows[r], headers, "원본 도면", "원본도면");
                items.Add(item);
            }
            payload["items"] = items;
            return payload;
        }

        private static bool IsMetadataOnlyBarListRow(List<string> row, List<string> headers)
        {
            if (row == null || headers == null) return true;

            return string.IsNullOrWhiteSpace(GetValue(row, headers, "부위"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "번호"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "철근규격", "규격"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, ShapeHeader, "CAD_SHAPE_JSON"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "길이(mm)", "길이"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, FinalLengthHeader, "final_length"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "수량(EA)", "수량"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "총길이(M)", "총길이"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "중량(Ton)", "중량"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "비고"))
                && string.IsNullOrWhiteSpace(GetValue(row, headers, "원본 도면", "원본도면"));
        }

        private static async Task<OviaErpBarListSyncResult> PostAsync(string companyId, string mode, Dictionary<string, object> payload)
        {
            return await PostRawAsync(companyId, mode, payload);
        }

        private static async Task<OviaErpBarListSyncResult> PostRawAsync(string companyId, string mode, Dictionary<string, object> payload)
        {
            string token;
            if (!OviaErpAuthenticationService.TryGetCurrentErpApiToken(companyId, out token)) return Fail("ERP API 인증정보가 없습니다. 다시 로그인해주세요.");
            string authBase = OviaCompanyConnectionStore.GetErpAuthUrl(companyId);
            Uri baseUri;
            if (!Uri.TryCreate((authBase ?? "").TrimEnd('/') + "/", UriKind.Absolute, out baseUri)) return Fail("ERP 연결 주소가 올바르지 않습니다.");

            // URL에는 짧은 mode만 둔다. BarList/철근형상 JSON은 절대 form-urlencoded/URI 인코딩하지 않는다.
            // .NET Framework의 FormUrlEncodedContent는 큰 shape_json을 Uri.EscapeDataString으로 인코딩하면서
            // "URI 문자열이 너무 깁니다" 예외를 낼 수 있으므로 raw application/json POST body를 사용한다.
            Uri endpointBase = new Uri(baseUri, "ovia_api.php");
            UriBuilder endpointBuilder = new UriBuilder(endpointBase);
            endpointBuilder.Query = "mode=" + Uri.EscapeDataString(mode ?? "");
            Uri endpoint = endpointBuilder.Uri;

            JavaScriptSerializer serializer = CreateSerializer();
            string payloadJson = serializer.Serialize(payload ?? new Dictionary<string, object>());

            using (HttpClientHandler handler = new HttpClientHandler())
            using (HttpClient client = new HttpClient(handler))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                handler.AllowAutoRedirect = false;
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                client.Timeout = TimeSpan.FromSeconds(45);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OVIA-Desktop/1.0");
                request.Headers.TryAddWithoutValidation("Authorization", token);
                // 철근형상 JSON을 포함한 전체 payload를 raw JSON body로 전송한다.
                // URL/form-urlencoded 인코딩을 거치지 않아 큰 형상 데이터도 URI 길이 제한의 영향을 받지 않는다.
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await client.SendAsync(request))
                {
                    string responseText = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        return Fail("ERP API HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                            + BuildResponseDetail(responseText));
                    }

                    IDictionary<string, object> root = null;
                    try
                    {
                        root = AsDictionary(CreateSerializer().DeserializeObject(ExtractJsonObject(responseText)));
                    }
                    catch
                    {
                        return Fail("ERP API 응답 형식이 올바르지 않습니다." + BuildResponseDetail(responseText));
                    }

                    if (root == null) return Fail("ERP API 응답 형식이 올바르지 않습니다." + BuildResponseDetail(responseText));
                    bool success = ReadBool(root, "res");
                    string msg = ReadString(root, "msg");
                    int id = ReadInt(root, "barlist_idx");
                    int itemCount = ReadInt(root, "item_count");
                    OviaErpBarListSyncResult result = success ? Ok(msg, id) : Fail(string.IsNullOrWhiteSpace(msg) ? "ERP BarList 동기화가 거부되었습니다." : msg);
                    result.SyncedItemCount = itemCount;
                    result.RawResponse = responseText;
                    return result;
                }
            }
        }

        private static void MaterializeBarList(string dir, string projectNo, int id, IDictionary<string, object> barlist, int erpListOrder)
        {
            IDictionary<string, object> meta = null;
            object metaValue;
            if (TryGet(barlist, "meta", out metaValue)) meta = AsDictionary(metaValue);
            if (meta == null) meta = barlist;

            object itemsValue;
            object[] items = new object[0];
            if (TryGet(barlist, "items", out itemsValue))
            {
                items = itemsValue as object[];
                ArrayList al = itemsValue as ArrayList;
                if (items == null && al != null) items = al.ToArray();
                if (items == null) items = new object[0];
            }

            string existing = FindLocalFileByErpId(dir, id);
            if (existing != "" && IsSyncPending(existing)) return;

            // ERP barlist_idx 하나당 로컬 CSV도 반드시 하나만 유지한다.
            // 제목/공사명/저장시각은 물리 파일 식별자로 사용하지 않는다.
            string csvPath = Path.Combine(dir, "BarList_" + Safe(projectNo) + "_ERP_" + id.ToString(CultureInfo.InvariantCulture) + ".csv");
            string shapeDir = Path.Combine(dir, "Shapes");
            Directory.CreateDirectory(shapeDir);

            string[] headers = new string[] { "부위", "번호", "철근규격", "철근형상", "길이(mm)", "수량(EA)", "총길이(M)", "중량(Ton)", "비고", "원본 도면", ShapeHeader, "OVIA_SHAPE_SOURCE", "OVIA_SHAPE_STATUS", FinalLengthHeader, ErpIdHeader, ErpListOrderHeader, "상태", "작성", "발주번호", "발주일", "등록일", "납기일", "동", "층", "공종", "진행", "제목", "태그", "색상", "주문량", "태그발행", "기타", "장대", "절단", "절곡", "출하", "미출하", "작성자", "OVIA_WRITER_USER_NAME", "OVIA_BARLIST_MEMO" };
            List<List<string>> rows = new List<List<string>>();
            rows.Add(new List<string>(headers));

            // ERP에 헤더만 존재하고 철근 items가 0건이어도 OVIA 목록에서 반드시 보여야 한다.
            // 메타 전용 1행을 생성해 공사별 BarList 목록/수정/후속 CAD 추출의 연결점을 보존한다.
            if (items.Length == 0)
            {
                List<string> metaOnlyRow = new List<string>();
                for (int h = 0; h < headers.Length; h++) metaOnlyRow.Add("");
                Set(metaOnlyRow, headers, ErpIdHeader, id.ToString(CultureInfo.InvariantCulture));
                Set(metaOnlyRow, headers, ErpListOrderHeader, erpListOrder.ToString(CultureInfo.InvariantCulture));
                ApplyMeta(metaOnlyRow, headers, meta, barlist);
                rows.Add(metaOnlyRow);
            }

            for (int i = 0; i < items.Length; i++)
            {
                IDictionary<string, object> item = AsDictionary(items[i]);
                if (item == null) continue;
                List<string> row = new List<string>();
                for (int h = 0; h < headers.Length; h++) row.Add("");
                Set(row, headers, "부위", ReadString(item, "part"));
                Set(row, headers, "번호", ReadString(item, "source_row_no"));
                Set(row, headers, "철근규격", ReadString(item, "dia"));
                string shapeJson = StripErpTransportFallbacks(ReadString(item, "shape_json"));
                if (!string.IsNullOrWhiteSpace(shapeJson))
                {
                    string shapeFile = "erp_" + id.ToString(CultureInfo.InvariantCulture) + "_" + (i + 1).ToString("000", CultureInfo.InvariantCulture) + ".json";
                    WriteTextIfChanged(
                        Path.Combine(shapeDir, shapeFile),
                        shapeJson,
                        new UTF8Encoding(false)
                    );
                    Set(row, headers, ShapeHeader, "Shapes/" + shapeFile);
                    Set(row, headers, "OVIA_SHAPE_SOURCE", "CAD");
                    Set(row, headers, "OVIA_SHAPE_STATUS", "CAD_CAPTURED");
                }
                Set(row, headers, "길이(mm)", ReadString(item, "length_mm"));
                Set(row, headers, FinalLengthHeader, ReadString(item, "final_length"));
                Set(row, headers, "수량(EA)", ReadString(item, "qty_ea"));
                Set(row, headers, "총길이(M)", ReadString(item, "total_length_m"));
                Set(row, headers, "중량(Ton)", ReadString(item, "weight_ton"));
                Set(row, headers, "비고", ReadString(item, "remark"));
                Set(row, headers, "원본 도면", ReadString(item, "source_drawing_name"));
                Set(row, headers, ErpIdHeader, id.ToString(CultureInfo.InvariantCulture));
                Set(row, headers, ErpListOrderHeader, erpListOrder.ToString(CultureInfo.InvariantCulture));
                ApplyMeta(row, headers, meta, barlist);
                rows.Add(row);
            }
            // ERP pull은 공사 전체 BarList를 반환한다.
            // 내용이 동일한 다른 CSV까지 매번 다시 저장하면 모든 LastWriteTime이
            // 같은 시각으로 갱신되므로 실제 내용이 변경된 BarList만 저장한다.
            WriteCsvIfChanged(csvPath, rows);

            if (existing != "")
            {
                try
                {
                    if (!string.Equals(Path.GetFullPath(existing), Path.GetFullPath(csvPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(existing);
                    }
                }
                catch
                {
                }
            }
        }

        private static void ApplyMeta(List<string> row, string[] headers, IDictionary<string, object> meta, IDictionary<string, object> root)
        {
            Set(row, headers, "상태", ReadFirstMetaString(meta, root, "barlist_status", "status"));
            Set(row, headers, "작성", ReadFirstMetaString(meta, root, "write_location", "write_status"));
            Set(row, headers, "발주번호", ReadFirstMetaString(meta, root, "order_number", "order_no"));
            Set(row, headers, "발주일", ReadFirstMetaString(meta, root, "order_date"));
            Set(row, headers, "등록일", ReadFirstMetaString(meta, root, "registered_date", "created_date", "created_at"));
            Set(row, headers, "납기일", ReadFirstMetaString(meta, root, "due_date", "delivery_date"));
            Set(row, headers, "동", ReadFirstMetaString(meta, root, "building"));
            Set(row, headers, "층", ReadFirstMetaString(meta, root, "floor"));
            Set(row, headers, "공종", ReadFirstMetaString(meta, root, "work_type"));
            // ERP 구현 버전에 따라 진행상태 필드가 meta.progress_status 또는 root.progress/status_progress 등으로
            // 반환될 수 있으므로 모두 수용한다. OVIA 목록의 '진행'은 ERP 값을 그대로 표시한다.
            Set(row, headers, "진행", ReadFirstMetaString(meta, root, "progress_status", "progress", "progress_state", "process_status", "work_progress", "status_progress", "work_status", "work_state", "process", "process_state", "process_name", "progress_name", "progress_text", "barlist_progress", "barlist_progress_status", "production_status"));
            Set(row, headers, "제목", ReadFirstMetaString(meta, root, "title", "barlist_title"));
            Set(row, headers, "태그", ReadFirstMetaString(meta, root, "tags", "tag"));
            Set(row, headers, "색상", ReadFirstMetaString(meta, root, "color_code", "color"));
            Set(row, headers, "주문량", ReadFirstMetaString(meta, root, "order_qty", "order_quantity"));
            Set(row, headers, "태그발행", ReadFirstMetaString(meta, root, "tag_issue_status"));
            Set(row, headers, "기타", ReadFirstMetaString(meta, root, "etc_status"));
            Set(row, headers, "장대", ReadFirstMetaString(meta, root, "long_bar_status"));
            Set(row, headers, "절단", ReadFirstMetaString(meta, root, "cutting_status"));
            Set(row, headers, "절곡", ReadFirstMetaString(meta, root, "bending_status"));
            Set(row, headers, "출하", ReadFirstMetaString(meta, root, "shipment_qty"));
            Set(row, headers, "미출하", ReadFirstMetaString(meta, root, "unshipment_qty"));
            // 저장 계약은 writer_user_id(아이디)를 그대로 유지한다. 목록 표시용 사용자명은 별도 숨김 메타에 저장한다.
            Set(row, headers, "작성자", ReadFirstMetaString(meta, root, "writer_user_id", "writer_id", "mb_id"));
            Set(row, headers, "OVIA_WRITER_USER_NAME", ReadFirstMetaString(meta, root,
                "writer_user_name", "writer_name", "writer_display_name", "user_name", "mb_name", "author_name"));
            Set(row, headers, "OVIA_BARLIST_MEMO", ReadFirstMetaString(meta, root, "remark", "memo"));
        }

        private static string ReadFirstMetaString(IDictionary<string, object> meta, IDictionary<string, object> root, params string[] names)
        {
            if (names == null) return "";
            for (int i = 0; i < names.Length; i++)
            {
                string value = meta == null ? "" : ReadString(meta, names[i]);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            for (int i = 0; i < names.Length; i++)
            {
                string value = root == null ? "" : ReadString(root, names[i]);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private static void RemoveServerDeletedLocalCaches(string dir, HashSet<int> serverIds)
        {
            foreach (string file in Directory.GetFiles(dir, "BarList_*.csv"))
            {
                try
                {
                    List<List<string>> rows = ReadCsv(file);
                    if (rows.Count < 2)
                    {
                        if (!IsSyncPending(file)) File.Delete(file);
                        continue;
                    }

                    int id = ParseInt(GetFirstValue(rows, rows[0], ErpIdHeader));

                    // 성공한 ERP pull이 목록의 원장이다.
                    // ERP id가 없고 pending도 아닌 과거 로컬 CSV는 stale cache이므로 제거한다.
                    if (!IsSyncPending(file) && (id <= 0 || !serverIds.Contains(id)))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }

        private static string FindLocalFileByErpId(string dir, int id)
        {
            foreach (string file in Directory.GetFiles(dir, "BarList_*.csv"))
            {
                try { List<List<string>> rows = ReadCsv(file); if (rows.Count > 1 && ParseInt(GetFirstValue(rows, rows[0], ErpIdHeader)) == id) return file; } catch { }
            }
            return "";
        }

        private static string CanonicalizeLocalCachePath(string projectNo, string csvPath, int erpId)
        {
            if (erpId <= 0 || string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return csvPath ?? "";

            try
            {
                string dir = Path.GetDirectoryName(csvPath);
                if (string.IsNullOrWhiteSpace(dir)) return csvPath;

                string canonical = Path.Combine(
                    dir,
                    "BarList_" + Safe(projectNo) + "_ERP_" + erpId.ToString(CultureInfo.InvariantCulture) + ".csv"
                );

                if (string.Equals(Path.GetFullPath(csvPath), Path.GetFullPath(canonical), StringComparison.OrdinalIgnoreCase))
                    return canonical;

                File.Copy(csvPath, canonical, true);
                File.Delete(csvPath);
                return canonical;
            }
            catch
            {
                return csvPath;
            }
        }

        private static void CleanupUnreferencedShapeFiles(string barListDirectory)
        {
            if (string.IsNullOrWhiteSpace(barListDirectory) || !Directory.Exists(barListDirectory)) return;

            try
            {
                string shapeDirectory = Path.Combine(barListDirectory, "Shapes");
                if (!Directory.Exists(shapeDirectory)) return;

                HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string csv in Directory.GetFiles(barListDirectory, "BarList_*.csv"))
                {
                    try
                    {
                        List<List<string>> rows = ReadCsv(csv);
                        if (rows.Count < 2) continue;
                        List<string> headers = rows[0];
                        for (int r = 1; r < rows.Count; r++)
                        {
                            string value = GetValue(rows[r], headers, ShapeHeader, "CAD_SHAPE_JSON");
                            string path = ResolveShapePath(csv, value);
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            {
                                keep.Add(Path.GetFullPath(path));
                                AddOriginalSourceCompanion(path, keep);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (string shapeFile in Directory.GetFiles(shapeDirectory, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (!keep.Contains(Path.GetFullPath(shapeFile))) File.Delete(shapeFile);
                    }
                    catch
                    {
                    }
                }

                // 빈 하위 폴더만 정리한다. Shapes 루트 자체는 유지한다.
                string[] subDirectories = Directory.GetDirectories(shapeDirectory, "*", SearchOption.AllDirectories);
                Array.Sort(subDirectories, delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                for (int i = 0; i < subDirectories.Length; i++)
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(subDirectories[i]).Length == 0) Directory.Delete(subDirectories[i]);
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

        private static void AddOriginalSourceCompanion(string shapePath, HashSet<string> keep)
        {
            try
            {
                string json = File.ReadAllText(shapePath, Encoding.UTF8);
                IDictionary<string, object> doc = AsDictionary(CreateSerializer().DeserializeObject(json));
                string original = ReadString(doc, "originalSourcePath");
                if (string.IsNullOrWhiteSpace(original)) return;

                string originalPath = original;
                if (!Path.IsPathRooted(originalPath))
                {
                    originalPath = Path.Combine(Path.GetDirectoryName(shapePath) ?? "", originalPath.Replace('/', Path.DirectorySeparatorChar));
                }

                if (File.Exists(originalPath)) keep.Add(Path.GetFullPath(originalPath));
            }
            catch
            {
            }
        }

        /// <summary>
        /// ERP 철근형상 셀 표시 전용 전송 JSON을 생성합니다.
        ///
        /// 핵심 원칙:
        /// - OVIA 로컬 편집 JSON 원본은 절대 변경하지 않습니다.
        /// - ERP에는 최초 CAD 추출과 동일한 "셀 내부 로컬 좌표" 형태로 보냅니다.
        /// - 편집 후 좌표가 커지거나 음수가 되어도 전체 형상을 원본 CAD 셀의
        ///   콘텐츠 영역 안으로 단일 배율로 맞추고 가운데 정렬합니다.
        /// - LINE/ARC/CIRCLE/TEXT를 모두 같은 배율과 이동값으로 변환합니다.
        /// - 길이/각도/형상 비율은 왜곡하지 않습니다.
        /// - 문자 height/bounds, 원/호 radius도 같은 배율로 변환하여
        ///   최초 추출 형상과 글씨/라인의 상대 비율을 유지합니다.
        /// </summary>
        private static string ReadErpTransportShapeJson(string csvPath, string value)
        {
            string json = ReadMinifiedShapeJson(csvPath, value);

            if (string.IsNullOrWhiteSpace(json))
            {
                return "";
            }

            try
            {
                JavaScriptSerializer serializer = CreateSerializer();
                IDictionary<string, object> edited = AsDictionary(serializer.DeserializeObject(json));

                if (edited == null)
                {
                    return json;
                }

                // 철근형상 확인·수정 팝업은 적용 시점에 이미 현재 OVIA BarList 표시와 동일한
                // compact SOURCE_CELL 좌표계로 정규화합니다. 이 좌표를 ERP 전송 직전에 다시
                // 최초 CAD raw 셀로 재매핑하면 문자/각도/나사의 상대 위치가 달라지고, 추가한
                // TEXT가 bounds를 넓히는 순간 전체 형상까지 더 작아지는 이중 정규화가 발생합니다.
                // 따라서 SOURCE_CELL 문서는 현재 저장된 cell + element 좌표를 그대로 ERP에 전달합니다.
                if (UsesSourceCellTransportCoordinates(edited))
                {
                    double directCellWidth = ReadNestedNumber(edited, "cell", "width", 0D);
                    double directCellHeight = ReadNestedNumber(edited, "cell", "height", 0D);

                    if (directCellWidth <= 0D) directCellWidth = ReadNumber(edited, "width", 100D);
                    if (directCellHeight <= 0D) directCellHeight = ReadNumber(edited, "height", 60D);
                    if (directCellWidth <= 0D) directCellWidth = 100D;
                    if (directCellHeight <= 0D) directCellHeight = 60D;

                    IDictionary<string, object> directCanonical = BuildCanonicalErpShapeDocument(
                        edited,
                        directCellWidth,
                        directCellHeight
                    );

                    return serializer.Serialize(directCanonical);
                }

                // SOURCE_CELL이 아닌 과거/레거시 데이터만 기존 content-bounds 맞춤을 유지합니다.
                IDictionary<string, object> raw = TryLoadOriginalCadShapeDocument(csvPath, value, edited);

                ShapeViewport target = BuildTargetViewport(edited, raw);
                ShapeBounds editedBounds = GetShapeBounds(edited);

                if (!editedBounds.HasValue)
                {
                    return json;
                }

                double editedWidth = Math.Max(editedBounds.MaxX - editedBounds.MinX, 0.0001D);
                double editedHeight = Math.Max(editedBounds.MaxY - editedBounds.MinY, 0.0001D);
                double targetWidth = Math.Max(target.ContentMaxX - target.ContentMinX, 0.0001D);
                double targetHeight = Math.Max(target.ContentMaxY - target.ContentMinY, 0.0001D);

                double scale = Math.Min(targetWidth / editedWidth, targetHeight / editedHeight);

                if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0D)
                {
                    scale = 1D;
                }

                // 비정상적으로 작은/큰 수치가 들어와 ERP SVG가 깨지는 것을 방지합니다.
                scale = Math.Max(0.000001D, Math.Min(scale, 1000000D));

                double scaledWidth = editedWidth * scale;
                double scaledHeight = editedHeight * scale;
                double targetCenterX = (target.ContentMinX + target.ContentMaxX) / 2D;
                double targetCenterY = (target.ContentMinY + target.ContentMaxY) / 2D;
                double editedCenterX = (editedBounds.MinX + editedBounds.MaxX) / 2D;
                double editedCenterY = (editedBounds.MinY + editedBounds.MaxY) / 2D;

                double offsetX = targetCenterX - editedCenterX * scale;
                double offsetY = targetCenterY - editedCenterY * scale;

                TransformShapeElements(edited, scale, offsetX, offsetY);

                // ERP renderer에는 OVIA 편집기 전용 확장 속성을 그대로 넘기지 않고,
                // AutoCAD 최초 추출이 만드는 version=3 표준 스키마와 같은 형태로 재구성합니다.
                IDictionary<string, object> canonical = BuildCanonicalErpShapeDocument(
                    edited,
                    target.CellWidth,
                    target.CellHeight
                );

                return serializer.Serialize(canonical);
            }
            catch
            {
                // 변환에 실패한 경우 기존 정상 동기화 기능을 막지 않고 원문을 보냅니다.
                return json;
            }
        }

        private static bool UsesSourceCellTransportCoordinates(IDictionary<string, object> document)
        {
            if (document == null) return false;

            string layoutPolicy = ReadString(document, "layoutPolicy");
            if (layoutPolicy.Equals("SOURCE_CELL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // v3/v4 일부 데이터는 layoutPolicy가 누락될 수 있으므로 cell 크기가 명시되어 있고
            // OVIA 편집/수동 소스이면 현재 저장 좌표 자체를 표시 좌표로 간주합니다.
            string source = ReadString(document, "source");
            double cellWidth = ReadNestedNumber(document, "cell", "width", 0D);
            double cellHeight = ReadNestedNumber(document, "cell", "height", 0D);

            return cellWidth > 0D
                && cellHeight > 0D
                && (source.Equals("OVIA_EDIT", StringComparison.OrdinalIgnoreCase)
                    || source.Equals("OVIA_MANUAL", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class ShapeBounds
        {
            public bool HasValue;
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }

        private sealed class ShapeViewport
        {
            public double CellWidth;
            public double CellHeight;
            public double ContentMinX;
            public double ContentMinY;
            public double ContentMaxX;
            public double ContentMaxY;
        }

        private static IDictionary<string, object> TryLoadOriginalCadShapeDocument(
            string csvPath,
            string shapeValue,
            IDictionary<string, object> edited)
        {
            try
            {
                string editPath = ResolveShapePath(csvPath, shapeValue);
                string originalSourcePath = ReadString(edited, "originalSourcePath");

                if (editPath == "" || originalSourcePath == "")
                {
                    return null;
                }

                string rawPath = originalSourcePath;
                if (!Path.IsPathRooted(rawPath))
                {
                    rawPath = Path.Combine(
                        Path.GetDirectoryName(editPath) ?? "",
                        rawPath.Replace('/', Path.DirectorySeparatorChar)
                    );
                }

                if (!File.Exists(rawPath))
                {
                    return null;
                }

                string rawJson = File.ReadAllText(rawPath, Encoding.UTF8);
                JavaScriptSerializer serializer = CreateSerializer();
                return AsDictionary(serializer.DeserializeObject(rawJson));
            }
            catch
            {
                return null;
            }
        }

        private static ShapeViewport BuildTargetViewport(
            IDictionary<string, object> edited,
            IDictionary<string, object> raw)
        {
            IDictionary<string, object> basis = raw ?? edited;
            double cellWidth = ReadNestedNumber(basis, "cell", "width", 0D);
            double cellHeight = ReadNestedNumber(basis, "cell", "height", 0D);

            if (cellWidth <= 0D)
            {
                cellWidth = ReadNumber(basis, "width", 100D);
            }

            if (cellHeight <= 0D)
            {
                cellHeight = ReadNumber(basis, "height", 60D);
            }

            if (cellWidth <= 0D) cellWidth = 100D;
            if (cellHeight <= 0D) cellHeight = 60D;

            ShapeViewport viewport = new ShapeViewport();
            viewport.CellWidth = cellWidth;
            viewport.CellHeight = cellHeight;

            // 수정 형상을 최초 raw 형상의 좁은 bounds에 다시 맞추지 않습니다.
            // ERP는 cell.width / cell.height 전체를 viewBox 기준으로 사용하고
            // 컬럼 폭 변경 시 SVG 전체를 자동 축소하는 기능이 이미 정상입니다.
            // 따라서 전체 셀의 안전영역을 목표 viewport로 사용합니다.
            double padX = Math.Max(cellWidth * 0.055D, 1D);
            double padY = Math.Max(cellHeight * 0.075D, 1D);

            viewport.ContentMinX = padX;
            viewport.ContentMinY = padY;
            viewport.ContentMaxX = Math.Max(padX + 1D, cellWidth - padX);
            viewport.ContentMaxY = Math.Max(padY + 1D, cellHeight - padY);

            if (viewport.ContentMaxX <= viewport.ContentMinX)
            {
                viewport.ContentMinX = 0D;
                viewport.ContentMaxX = cellWidth;
            }

            if (viewport.ContentMaxY <= viewport.ContentMinY)
            {
                viewport.ContentMinY = 0D;
                viewport.ContentMaxY = cellHeight;
            }

            return viewport;
        }

        private static ShapeBounds GetShapeBounds(IDictionary<string, object> document)
        {
            ShapeBounds result = new ShapeBounds();
            result.MinX = Double.MaxValue;
            result.MinY = Double.MaxValue;
            result.MaxX = Double.MinValue;
            result.MaxY = Double.MinValue;

            object elementsValue;
            if (document == null || !TryGet(document, "elements", out elementsValue))
            {
                return result;
            }

            object[] elements = ToObjectArray(elementsValue);

            for (int i = 0; i < elements.Length; i++)
            {
                IDictionary<string, object> element = AsDictionary(elements[i]);
                if (element == null) continue;

                string type = ReadString(element, "type").Trim().ToUpperInvariant();

                if (type == "LINE")
                {
                    IncludeBounds(result, ReadNumber(element, "x1", 0D), ReadNumber(element, "y1", 0D));
                    IncludeBounds(result, ReadNumber(element, "x2", 0D), ReadNumber(element, "y2", 0D));
                }
                else if (type == "ARC" || type == "CIRCLE")
                {
                    double cx = ReadNumber(element, "cx", 0D);
                    double cy = ReadNumber(element, "cy", 0D);
                    double radius = Math.Abs(ReadNumber(element, "radius", 0D));
                    IncludeBounds(result, cx - radius, cy - radius);
                    IncludeBounds(result, cx + radius, cy + radius);
                }
                else if (type == "TEXT")
                {
                    // 편집 이후 저장된 bounds가 예전 크기를 유지할 수 있으므로
                    // 현재 x/y + height × textScale을 기준으로 실제 표시영역을 다시 계산합니다.
                    double x = ReadNumber(element, "x", ReadNumber(element, "x1", 0D));
                    double y = ReadNumber(element, "y", ReadNumber(element, "y1", 0D));
                    double height = Math.Max(ReadNumber(element, "height", 2.5D), 0.1D);
                    double textScale = Math.Max(ReadNumber(element, "textScale", 1D), 0.25D);
                    string value = ReadString(element, "text");
                    double estimatedHeight = height * textScale;
                    double estimatedWidth = Math.Max(
                        estimatedHeight * 0.62D * Math.Max(value.Length, 1),
                        estimatedHeight
                    );

                    IncludeBounds(result, x - estimatedWidth / 2D, y - estimatedHeight / 2D);
                    IncludeBounds(result, x + estimatedWidth / 2D, y + estimatedHeight / 2D);
                }
            }

            return result;
        }

        private static void TransformShapeElements(
            IDictionary<string, object> document,
            double scale,
            double offsetX,
            double offsetY)
        {
            object elementsValue;
            if (document == null || !TryGet(document, "elements", out elementsValue))
            {
                return;
            }

            object[] elements = ToObjectArray(elementsValue);

            for (int i = 0; i < elements.Length; i++)
            {
                IDictionary<string, object> element = AsDictionary(elements[i]);
                if (element == null) continue;

                string type = ReadString(element, "type").Trim().ToUpperInvariant();

                if (type == "LINE")
                {
                    SetNumber(element, "x1", ReadNumber(element, "x1", 0D) * scale + offsetX);
                    SetNumber(element, "y1", ReadNumber(element, "y1", 0D) * scale + offsetY);
                    SetNumber(element, "x2", ReadNumber(element, "x2", 0D) * scale + offsetX);
                    SetNumber(element, "y2", ReadNumber(element, "y2", 0D) * scale + offsetY);
                }
                else if (type == "ARC" || type == "CIRCLE")
                {
                    SetNumber(element, "cx", ReadNumber(element, "cx", 0D) * scale + offsetX);
                    SetNumber(element, "cy", ReadNumber(element, "cy", 0D) * scale + offsetY);
                    SetNumber(element, "radius", Math.Abs(ReadNumber(element, "radius", 0D) * scale));
                }
                else if (type == "TEXT")
                {
                    string xKey = HasKey(element, "x") ? "x" : "x1";
                    string yKey = HasKey(element, "y") ? "y" : "y1";

                    SetNumber(element, xKey, ReadNumber(element, xKey, 0D) * scale + offsetX);
                    SetNumber(element, yKey, ReadNumber(element, yKey, 0D) * scale + offsetY);

                    if (HasNumber(element, "height"))
                    {
                        SetNumber(element, "height", Math.Abs(ReadNumber(element, "height", 0D) * scale));
                    }

                    if (HasNumber(element, "boundsMinX"))
                    {
                        SetNumber(element, "boundsMinX", ReadNumber(element, "boundsMinX", 0D) * scale + offsetX);
                        SetNumber(element, "boundsMinY", ReadNumber(element, "boundsMinY", 0D) * scale + offsetY);
                        SetNumber(element, "boundsMaxX", ReadNumber(element, "boundsMaxX", 0D) * scale + offsetX);
                        SetNumber(element, "boundsMaxY", ReadNumber(element, "boundsMaxY", 0D) * scale + offsetY);
                    }
                }
            }
        }

        private static IDictionary<string, object> BuildCanonicalErpShapeDocument(
            IDictionary<string, object> edited,
            double cellWidth,
            double cellHeight)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = 3;
            string sourceName = ReadString(edited, "source");
            root["source"] = String.IsNullOrWhiteSpace(sourceName) ? "CAD" : sourceName.Trim();
            root["coordinateSystem"] = "TOP_LEFT_Y_DOWN";
            root["layoutPolicy"] = "SOURCE_CELL";

            Dictionary<string, object> textPolicy = new Dictionary<string, object>();
            textPolicy["fontFamily"] = "맑은 고딕";
            textPolicy["fontSizePt"] = 8;
            textPolicy["preservePosition"] = true;
            textPolicy["editableTextIds"] = true;
            root["textPolicy"] = textPolicy;

            // ERP 공통 formatter가 지원하는 경우 사용할 수 있는 비파괴 표시 힌트입니다.
            // 현재 제공된 ERP BarList 소스는 shape_json을 formatter에 전달만 하므로
            // 지원하지 않는 formatter에서는 단순히 무시됩니다.
            Dictionary<string, object> renderPolicy = new Dictionary<string, object>();
            renderPolicy["strokeWidthPx"] = CadShapeVisualPolicy.GridStrokeWidthPx;
            renderPolicy["nonScalingStroke"] = true;
            renderPolicy["textScaleMin"] = CadShapeVisualPolicy.MinimumTextScale;
            renderPolicy["textScaleMax"] = CadShapeVisualPolicy.MaximumTextScale;
            root["renderPolicy"] = renderPolicy;

            root["rowNo"] = ReadInt(edited, "rowNo");

            Dictionary<string, object> cell = new Dictionary<string, object>();
            cell["width"] = RoundShapeNumber(cellWidth);
            cell["height"] = RoundShapeNumber(cellHeight);
            root["cell"] = cell;

            List<object> canonicalElements = new List<object>();
            double erpTextReferenceHeight = ResolveErpTextReferenceHeight(edited, cellWidth, cellHeight);

            object elementsValue;
            if (edited != null && TryGet(edited, "elements", out elementsValue))
            {
                object[] elements = ToObjectArray(elementsValue);

                for (int i = 0; i < elements.Length; i++)
                {
                    IDictionary<string, object> source = AsDictionary(elements[i]);

                    if (source == null)
                    {
                        continue;
                    }

                    string type = ReadString(source, "type").Trim().ToUpperInvariant();

                    if (type != "LINE"
                        && type != "ARC"
                        && type != "CIRCLE"
                        && type != "TEXT")
                    {
                        continue;
                    }

                    Dictionary<string, object> output = new Dictionary<string, object>();
                    output["type"] = type;

                    if (type == "LINE")
                    {
                        output["x1"] = RoundShapeNumber(ReadNumber(source, "x1", 0D));
                        output["y1"] = RoundShapeNumber(ReadNumber(source, "y1", 0D));
                        output["x2"] = RoundShapeNumber(ReadNumber(source, "x2", 0D));
                        output["y2"] = RoundShapeNumber(ReadNumber(source, "y2", 0D));
                    }
                    else if (type == "ARC" || type == "CIRCLE")
                    {
                        output["cx"] = RoundShapeNumber(ReadNumber(source, "cx", 0D));
                        output["cy"] = RoundShapeNumber(ReadNumber(source, "cy", 0D));
                        output["radius"] = RoundShapeNumber(Math.Abs(ReadNumber(source, "radius", 0D)));
                        output["startAngle"] = RoundShapeNumber(ReadNumber(source, "startAngle", 0D));
                        output["endAngle"] = RoundShapeNumber(ReadNumber(source, "endAngle", 0D));
                    }
                    else if (type == "TEXT")
                    {
                        output["text"] = ReadString(source, "text");

                        string textId = ReadString(source, "textId");
                        if (!String.IsNullOrWhiteSpace(textId))
                        {
                            output["textId"] = textId.Trim();
                        }

                        double x = HasNumber(source, "x")
                            ? ReadNumber(source, "x", 0D)
                            : ReadNumber(source, "x1", 0D);
                        double y = HasNumber(source, "y")
                            ? ReadNumber(source, "y", 0D)
                            : ReadNumber(source, "y1", 0D);

                        // ERP SVG의 문자 크기는 원본 CAD height를 직접 사용하지 않습니다.
                        // CAD 도면마다 문자 높이 단위가 달라 수정 후 특정 숫자만 지나치게 크거나
                        // 작아지는 문제가 있었으므로, 현재 cell viewBox에서 계산한 표준 높이를 기준으로
                        // 제한된 TextScale만 반영합니다. 회전/좌표는 기존 값을 그대로 유지합니다.
                        double textScale = CadShapeVisualPolicy.ClampTextScale(
                            ReadNumber(source, "textScale", 1D)
                        );
                        double effectiveHeight = erpTextReferenceHeight * textScale;

                        output["x"] = RoundShapeNumber(x);
                        output["y"] = RoundShapeNumber(y);
                        output["height"] = RoundShapeNumber(effectiveHeight);
                        output["rotation"] = RoundShapeNumber(ReadNumber(source, "rotation", 0D));
                        output["align"] = "CENTER";

                        // ERP renderer는 height를 실제 SVG font-size로 사용하므로 위 height는 표시용입니다.
                        // OVIA가 다시 pull할 때 사용자가 편집창에서 조절한 비율을 정확히 복원할 수 있도록
                        // 편집용 배율은 별도 메타로 함께 보관합니다. ERP 화면에는 영향을 주지 않습니다.
                        output["oviaTextScale"] = RoundShapeNumber(textScale);

                        // ERP v3 bounds도 현재 문자 위치/크기 기준으로 다시 생성합니다.
                        string textValue = ReadString(source, "text");
                        double estimatedTextWidth = Math.Max(
                            effectiveHeight * 0.62D * Math.Max(textValue.Length, 1),
                            effectiveHeight
                        );

                        output["boundsMinX"] = RoundShapeNumber(x - estimatedTextWidth / 2D);
                        output["boundsMinY"] = RoundShapeNumber(y - effectiveHeight / 2D);
                        output["boundsMaxX"] = RoundShapeNumber(x + estimatedTextWidth / 2D);
                        output["boundsMaxY"] = RoundShapeNumber(y + effectiveHeight / 2D);
                    }

                    int colorIndex = ReadInt(source, "colorIndex");
                    output["colorIndex"] = colorIndex <= 0 ? 7 : colorIndex;
                    canonicalElements.Add(output);

                    // 현재 ERP SVG 변환기는 일부 OVIA 편집 ARC를 SVG path로 만들지 못하는
                    // 호환 문제가 있습니다. 원본 ARC는 그대로 전송하면서 ERP 표시용으로만
                    // 같은 곡선을 짧은 LINE 묶음으로 추가합니다. ERP가 ARC를 지원하는 경우
                    // 동일 위치에 겹쳐 보일 뿐 형상은 변하지 않습니다. pull 시에는 아래
                    // oviaErpFallback 마커를 제거하여 OVIA 편집 JSON에 중복 LINE이 남지 않습니다.
                    if (type == "ARC")
                    {
                        AppendErpArcLineFallbacks(canonicalElements, source, colorIndex <= 0 ? 7 : colorIndex);
                    }
                }
            }

            root["elements"] = canonicalElements.ToArray();
            return root;
        }

        private static double ResolveErpTextReferenceHeight(
            IDictionary<string, object> document,
            double cellWidth,
            double cellHeight)
        {
            // 원본 CAD TEXT height의 중앙값/최대값을 기준으로 삼지 않습니다.
            // 도면별 CAD 문자 단위 편차가 ERP SVG font-size에 그대로 전파되면
            // 동일한 철근형상 안에서도 160은 거의 안 보이고 2600은 과도하게 커지는 현상이 생깁니다.
            // ERP 표시는 cell viewBox 비율만으로 결정하여 저장/재조회/재수정 반복에도 크기가 누적되지 않습니다.
            return CadShapeVisualPolicy.ResolveErpReferenceTextHeight(cellWidth, cellHeight);
        }

        private static void AppendErpArcLineFallbacks(
            List<object> destination,
            IDictionary<string, object> source,
            int colorIndex)
        {
            if (destination == null || source == null) return;

            double cx = ReadNumber(source, "cx", 0D);
            double cy = ReadNumber(source, "cy", 0D);
            double radius = Math.Abs(ReadNumber(source, "radius", 0D));
            double start = ReadNumber(source, "startAngle", 0D);
            double end = ReadNumber(source, "endAngle", 0D);
            double sweep = end - start;

            if (radius <= 0D || Double.IsNaN(sweep) || Double.IsInfinity(sweep) || Math.Abs(sweep) < 0.01D)
            {
                return;
            }

            // 편집기 GetArcScreenPoints와 같은 약 6도 간격을 사용하되
            // ERP payload 증가를 제한하기 위해 최대 48구간으로 제한합니다.
            int segments = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / 6D));
            segments = Math.Min(48, segments);
            string groupId = "ERP_ARC_"
                + RoundShapeNumber(cx).ToString(CultureInfo.InvariantCulture) + "_"
                + RoundShapeNumber(cy).ToString(CultureInfo.InvariantCulture) + "_"
                + RoundShapeNumber(radius).ToString(CultureInfo.InvariantCulture) + "_"
                + RoundShapeNumber(start).ToString(CultureInfo.InvariantCulture) + "_"
                + RoundShapeNumber(end).ToString(CultureInfo.InvariantCulture);

            double previousX = cx + Math.Cos(start * Math.PI / 180D) * radius;
            double previousY = cy - Math.Sin(start * Math.PI / 180D) * radius;

            for (int i = 1; i <= segments; i++)
            {
                double angle = start + sweep * i / segments;
                double x = cx + Math.Cos(angle * Math.PI / 180D) * radius;
                double y = cy - Math.Sin(angle * Math.PI / 180D) * radius;

                Dictionary<string, object> line = new Dictionary<string, object>();
                line["type"] = "LINE";
                line["x1"] = RoundShapeNumber(previousX);
                line["y1"] = RoundShapeNumber(previousY);
                line["x2"] = RoundShapeNumber(x);
                line["y2"] = RoundShapeNumber(y);
                line["colorIndex"] = colorIndex;
                line["oviaErpFallback"] = "ARC_LINE";
                line["oviaErpFallbackGroupId"] = groupId;
                destination.Add(line);

                previousX = x;
                previousY = y;
            }
        }

        private static string StripErpTransportFallbacks(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return json == null ? "" : json;
            }

            bool hasFallback = json.IndexOf("oviaErpFallback", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasTextScaleMetadata = json.IndexOf("oviaTextScale", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasFallback && !hasTextScaleMetadata)
            {
                return json;
            }

            try
            {
                JavaScriptSerializer serializer = CreateSerializer();
                IDictionary<string, object> root = AsDictionary(serializer.DeserializeObject(json));
                if (root == null) return json;

                object elementsValue;
                if (!TryGet(root, "elements", out elementsValue)) return json;

                object[] elements = ToObjectArray(elementsValue);
                List<object> clean = new List<object>();

                for (int i = 0; i < elements.Length; i++)
                {
                    IDictionary<string, object> element = AsDictionary(elements[i]);
                    if (element == null)
                    {
                        clean.Add(elements[i]);
                        continue;
                    }

                    if (!String.IsNullOrWhiteSpace(ReadString(element, "oviaErpFallback")))
                    {
                        continue;
                    }

                    if (ReadString(element, "type").Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                        && HasNumber(element, "oviaTextScale"))
                    {
                        double textScale = CadShapeVisualPolicy.ClampTextScale(
                            ReadNumber(element, "oviaTextScale", 1D)
                        );

                        // ERP의 height는 SVG 표시용 viewBox 크기이므로 편집기로 그대로 되가져오면
                        // 다음 저장에서 크기가 다시 커질 수 있습니다. 편집기 내부 기준 height로 복원하고
                        // 사용자가 조절한 비율만 TextScale로 되돌립니다.
                        element["height"] = CadShapeVisualPolicy.EditorCanonicalTextHeight;
                        element["textScale"] = RoundShapeNumber(textScale);
                        RemoveKeyIgnoreCase(element, "oviaTextScale");
                        RemoveKeyIgnoreCase(element, "boundsMinX");
                        RemoveKeyIgnoreCase(element, "boundsMinY");
                        RemoveKeyIgnoreCase(element, "boundsMaxX");
                        RemoveKeyIgnoreCase(element, "boundsMaxY");
                    }

                    clean.Add(elements[i]);
                }

                root["elements"] = clean.ToArray();
                return serializer.Serialize(root);
            }
            catch
            {
                return json;
            }
        }

        private static double RoundShapeNumber(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                return 0D;
            }

            return Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }


        private static string ResolveShapePath(string csvPath, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string source = value.Trim();

            if (source.StartsWith("{", StringComparison.Ordinal)
                || source.StartsWith("[", StringComparison.Ordinal))
            {
                return "";
            }

            string path = source;

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(
                    Path.GetDirectoryName(csvPath) ?? "",
                    path.Replace('/', Path.DirectorySeparatorChar)
                );
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static object[] ToObjectArray(object value)
        {
            object[] array = value as object[];
            if (array != null) return array;

            ArrayList list = value as ArrayList;
            if (list != null) return list.ToArray();

            return new object[0];
        }

        private static IDictionary<string, object> GetOrCreateDictionary(
            IDictionary<string, object> parent,
            string key)
        {
            object value;
            if (parent != null && TryGet(parent, key, out value))
            {
                IDictionary<string, object> existing = AsDictionary(value);
                if (existing != null) return existing;
            }

            Dictionary<string, object> created = new Dictionary<string, object>();
            parent[key] = created;
            return created;
        }

        private static void IncludeBounds(ShapeBounds bounds, double x, double y)
        {
            if (bounds == null || Double.IsNaN(x) || Double.IsInfinity(x)
                || Double.IsNaN(y) || Double.IsInfinity(y))
            {
                return;
            }

            if (x < bounds.MinX) bounds.MinX = x;
            if (y < bounds.MinY) bounds.MinY = y;
            if (x > bounds.MaxX) bounds.MaxX = x;
            if (y > bounds.MaxY) bounds.MaxY = y;
            bounds.HasValue = true;
        }

        private static double ReadNestedNumber(
            IDictionary<string, object> dictionary,
            string parentKey,
            string childKey,
            double defaultValue)
        {
            object parentValue;
            if (dictionary == null || !TryGet(dictionary, parentKey, out parentValue))
            {
                return defaultValue;
            }

            IDictionary<string, object> child = AsDictionary(parentValue);
            return child == null ? defaultValue : ReadNumber(child, childKey, defaultValue);
        }

        private static double ReadNumber(
            IDictionary<string, object> dictionary,
            string key,
            double defaultValue)
        {
            object value;
            if (dictionary == null || !TryGet(dictionary, key, out value) || value == null)
            {
                return defaultValue;
            }

            double number;
            return Double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number
            ) ? number : defaultValue;
        }

        private static bool HasNumber(IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !TryGet(dictionary, key, out value) || value == null)
            {
                return false;
            }

            double number;
            return Double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number
            );
        }

        private static bool HasKey(IDictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary != null && TryGet(dictionary, key, out value);
        }

        private static void SetNumber(IDictionary<string, object> dictionary, string key, double value)
        {
            if (dictionary == null) return;
            dictionary[key] = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }

        private static void RemoveKeyIgnoreCase(IDictionary<string, object> dictionary, string key)
        {
            if (dictionary == null) return;

            string found = null;
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                if (String.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    found = pair.Key;
                    break;
                }
            }

            if (found != null)
            {
                dictionary.Remove(found);
            }
        }


        private static string ReadMinifiedShapeJson(string csvPath, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            string source = value.Trim();
            string json;

            // ERP pull 또는 향후 호환 입력에서 JSON 본문이 직접 들어오는 경우도 그대로 지원한다.
            if (source.StartsWith("{", StringComparison.Ordinal) || source.StartsWith("[", StringComparison.Ordinal))
            {
                json = source;
            }
            else
            {
                string path = source;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Path.GetDirectoryName(csvPath) ?? "", path.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(path))
                    throw new FileNotFoundException("ERP로 전송할 철근형상 JSON 파일을 찾을 수 없습니다.", path);

                json = File.ReadAllText(path, Encoding.UTF8);
            }

            if (string.IsNullOrWhiteSpace(json)) return "";

            try
            {
                object parsed = CreateSerializer().DeserializeObject(json);
                return CreateSerializer().Serialize(parsed);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("ERP로 전송할 철근형상 JSON 형식이 올바르지 않습니다. " + ex.Message, ex);
            }
        }

        private static bool IsSyncPending(string csvPath)
        {
            try { List<List<string>> rows = ReadCsv(csvPath); return rows.Count > 1 && GetFirstValue(rows, rows[0], SyncPendingHeader).Equals("Y", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }

        private static void SetSyncPending(string csvPath, bool pending)
        {
            try
            {
                List<List<string>> rows = ReadCsv(csvPath);
                if (rows.Count == 0) return;
                List<string> headers = rows[0];
                int idx = FindHeader(headers, SyncPendingHeader);
                if (idx < 0) { headers.Add(SyncPendingHeader); idx = headers.Count - 1; }
                for (int r = 1; r < rows.Count; r++) { while (rows[r].Count < headers.Count) rows[r].Add(""); rows[r][idx] = pending ? "Y" : "N"; }
                WriteCsv(csvPath, rows);
            }
            catch { }
        }

        private static void PersistErpId(string csvPath, List<List<string>> rows, List<string> headers, int id)
        {
            int idx = FindHeader(headers, ErpIdHeader);
            if (idx < 0) { headers.Add(ErpIdHeader); idx = headers.Count - 1; }
            for (int r = 1; r < rows.Count; r++) { while (rows[r].Count < headers.Count) rows[r].Add(""); rows[r][idx] = id.ToString(CultureInfo.InvariantCulture); }
            WriteCsv(csvPath, rows);
        }

        private static string ResolveOrderQtyForErp(List<List<string>> rows, List<string> headers)
        {
            // 명시적인 주문량이 있으면 그 값을 사용한다.
            string explicitValue = GetFirstValue(rows, headers, "주문량");
            double explicitNumber;
            if (TryParseDecimalNumber(explicitValue, out explicitNumber) && explicitNumber > 0)
            {
                return explicitNumber.ToString("0.###", CultureInfo.InvariantCulture);
            }

            // OVIA 목록은 주문량이 비어 있을 때 수량 합계를 주문량으로 표시한다.
            // ERP에도 같은 값을 보내 OVIA 화면과 ERP DB가 불일치하지 않게 한다.
            double totalQty = 0;
            for (int r = 1; r < rows.Count; r++)
            {
                string value = GetValue(rows[r], headers, "수량(EA)", "수량");
                double qty;
                if (TryParseDecimalNumber(value, out qty)) totalQty += qty;
            }
            return totalQty.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDecimalNumber(string value, out double number)
        {
            string normalized = (value ?? "").Trim().Replace(",", "");
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return true;
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
        }

        private static void PersistCanonicalMetaAfterPush(string companyId, List<List<string>> rows, List<string> headers)
        {
            if (rows == null || rows.Count < 2 || headers == null) return;

            string orderQty = ResolveOrderQtyForErp(rows, headers);
            int orderQtyIndex = FindHeader(headers, "주문량");
            if (orderQtyIndex < 0)
            {
                headers.Add("주문량");
                orderQtyIndex = headers.Count - 1;
            }

            // 작성자는 신규등록 당시의 최초 작성자를 유지한다.
            // 이후 검토 후 저장/수정 작업자의 ID로 덮어쓰지 않는다.
            for (int r = 1; r < rows.Count; r++)
            {
                while (rows[r].Count < headers.Count) rows[r].Add("");
                rows[r][orderQtyIndex] = orderQty;
            }
        }

        private static void AddMeta(Dictionary<string, object> meta, string key, List<List<string>> rows, List<string> headers, params string[] names) { meta[key] = GetFirstValue(rows, headers, names); }
        private static string GetFirstValue(List<List<string>> rows, List<string> headers, params string[] names) { for (int r = 1; r < rows.Count; r++) { string v = GetValue(rows[r], headers, names); if (!string.IsNullOrWhiteSpace(v)) return v; } return ""; }
        private static string GetValue(List<string> row, List<string> headers, params string[] names) { for (int n=0;n<names.Length;n++){int i=FindHeader(headers,names[n]); if(i>=0&&i<row.Count)return row[i]??"";} return ""; }
        private static int FindHeader(List<string> headers, string name) { string target=Norm(name); for(int i=0;i<headers.Count;i++) if(Norm(headers[i])==target) return i; return -1; }
        private static string Norm(string s) { return (s??"").Replace(" ","").Replace("_","").Replace("-","").ToUpperInvariant(); }
        private static int ParseInt(string s) { int v; return int.TryParse((s??"").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)?v:0; }
        private static string Safe(string s) { if(s==null)s=""; foreach(char c in Path.GetInvalidFileNameChars()) s=s.Replace(c,'_'); return s; }

        private static List<List<string>> ReadCsv(string path) { return ParseCsv(File.ReadAllText(path, Encoding.UTF8)); }
        private static List<List<string>> ParseCsv(string text) { List<List<string>> rows=new List<List<string>>(); List<string> row=new List<string>(); StringBuilder cell=new StringBuilder(); bool q=false; for(int i=0;i<text.Length;i++){char c=text[i]; if(q){if(c=='"'&&i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else if(c=='"')q=false;else cell.Append(c);}else if(c=='"')q=true;else if(c==','){row.Add(cell.ToString());cell.Length=0;}else if(c=='\r'||c=='\n'){if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;row.Add(cell.ToString());cell.Length=0;rows.Add(row);row=new List<string>();}else cell.Append(c);} if(cell.Length>0||row.Count>0){row.Add(cell.ToString());rows.Add(row);} return rows; }
        private static void WriteCsvIfChanged(string path, List<List<string>> rows)
        {
            if (File.Exists(path))
            {
                try
                {
                    List<List<string>> existing = ReadCsv(path);
                    if (CsvRowsEqual(existing, rows))
                    {
                        return;
                    }
                }
                catch
                {
                    // 비교 실패 시 기존 저장 방식으로 안전하게 갱신한다.
                }
            }

            WriteCsv(path, rows);
        }

        private static bool CsvRowsEqual(List<List<string>> a, List<List<string>> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int r = 0; r < a.Count; r++)
            {
                List<string> ar = a[r] ?? new List<string>();
                List<string> br = b[r] ?? new List<string>();

                int max = Math.Max(ar.Count, br.Count);
                for (int c = 0; c < max; c++)
                {
                    string av = c < ar.Count ? (ar[c] ?? "") : "";
                    string bv = c < br.Count ? (br[c] ?? "") : "";

                    if (!string.Equals(av, bv, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void WriteTextIfChanged(string path, string content, Encoding encoding)
        {
            content = content ?? "";

            if (File.Exists(path))
            {
                try
                {
                    string existing = File.ReadAllText(path, encoding);
                    if (string.Equals(existing, content, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }

            File.WriteAllText(path, content, encoding);
        }

        private static void WriteCsv(string path, List<List<string>> rows) { using(StreamWriter w=new StreamWriter(path,false,new UTF8Encoding(true))){foreach(List<string> row in rows){for(int i=0;i<row.Count;i++){if(i>0)w.Write(',');w.Write(Csv(row[i]));}w.WriteLine();}} }
        private static string Csv(string v){v=v??""; return (v.IndexOfAny(new char[]{',','"','\r','\n'})>=0)?"\""+v.Replace("\"","\"\"")+"\"":v;}
        private static void Set(List<string> row, string[] headers, string name, string value){for(int i=0;i<headers.Length;i++)if(headers[i]==name){row[i]=value??"";return;}}

        private static JavaScriptSerializer CreateSerializer(){JavaScriptSerializer s=new JavaScriptSerializer();s.MaxJsonLength=int.MaxValue;s.RecursionLimit=200;return s;}
        private static IDictionary<string, object> AsDictionary(object o){return o as IDictionary<string, object>;}
        private static bool TryGet(IDictionary<string,object>d,string k,out object v){foreach(KeyValuePair<string,object>p in d)if(string.Equals(p.Key,k,StringComparison.OrdinalIgnoreCase)){v=p.Value;return true;}v=null;return false;}
        private static string ReadString(IDictionary<string,object>d,string k){object v;return d!=null&&TryGet(d,k,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
        private static int ReadInt(IDictionary<string,object>d,string k){return ParseInt(ReadString(d,k));}
        private static bool ReadBool(IDictionary<string,object>d,string k){string s=ReadString(d,k);return s.Equals("true",StringComparison.OrdinalIgnoreCase)||s=="1"||s.Equals("Y",StringComparison.OrdinalIgnoreCase);}
        private static string ExtractJsonObject(string raw){if(string.IsNullOrWhiteSpace(raw))return "";int a=raw.IndexOf('{'),b=raw.LastIndexOf('}');return a>=0&&b>=a?raw.Substring(a,b-a+1):raw;}
        private static string BuildResponseDetail(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string value = raw.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > 500) value = value.Substring(0, 500) + "...";
            return "\r\n서버 응답: " + value;
        }
        private static OviaErpBarListSyncResult Ok(string m,int id){return new OviaErpBarListSyncResult{IsSuccess=true,Message=m??"",BarListId=id,SyncedItemCount=-1};}
        private static OviaErpBarListSyncResult Fail(string m){return new OviaErpBarListSyncResult{IsSuccess=false,Message=m??"",BarListId=0,SyncedItemCount=-1};}
    }
}
