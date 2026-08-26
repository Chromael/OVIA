using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    public sealed class OviaBackupManifest
    {
        public string product = "OVIA";
        public int format_version = 1;
        public string created_at = "";
        public string company_id = "";
        public List<string> contents = new List<string>();
        public List<OviaBackupManifestFile> files = new List<OviaBackupManifestFile>();
    }

    public sealed class OviaBackupManifestFile
    {
        public string path = "";
        public string sha256 = "";
        public long length = 0;
    }

    public sealed class OviaBackupSystemSettings
    {
        public string company_logo_mode = "DEFAULT";
        public int list_page_size = 100;
        public string brand_primary_hex = OviaSystemSettingsStore.DefaultBrandPrimaryHex;
        public string brand_hover_hex = OviaSystemSettingsStore.DefaultBrandHoverHex;
        public int loading_delay_unit = OviaSystemSettingsStore.DefaultLoadingDelayUnit;
        public string company_logo_asset = "";
        public string loading_asset = "";
    }

    public sealed class OviaBackupRestoreSelection
    {
        public bool SystemSettings = true;
        public bool CompanyConnections = true;
        public bool BarListMapping = true;
        public bool RebarUnitWeight = true;
    }

    public sealed class OviaBackupInspection
    {
        public OviaBackupManifest Manifest;
        public bool HasSystemSettings;
        public bool HasCompanyConnections;
        public bool HasBarListMapping;
        public bool HasRebarUnitWeight;
        public int CompanyConnectionCount;
    }

    public static class OviaBackupRestoreService
    {
        public const int CurrentFormatVersion = 1;

        private const string ManifestEntry = "manifest.json";
        private const string SystemSettingsEntry = "settings/system_settings.json";
        private const string MappingEntry = "mapping/barlist_mapping.json";
        private const string RebarEntry = "rebar/rebar_unit_weight.csv";
        private const string ConnectionPrefix = "connections/";
        private const string AssetPrefix = "settings/assets/";

        public static string CreateBackup(string destinationZipPath, string companyId)
        {
            if (string.IsNullOrWhiteSpace(destinationZipPath))
            {
                throw new ArgumentException("백업 파일 저장 경로가 비어 있습니다.");
            }

            string stage = CreateTemporaryDirectory("backup");
            try
            {
                List<string> stagedFiles = new List<string>();

                OviaBackupSystemSettings settingsBackup = BuildSystemSettingsBackup(stage, stagedFiles);
                WriteJson(Path.Combine(stage, "settings", "system_settings.json"), settingsBackup);
                stagedFiles.Add(SystemSettingsEntry);

                string mappingPath = Path.Combine(stage, "mapping", "barlist_mapping.json");
                EnsureDirectoryForFile(mappingPath);
                OviaBarListMappingStore.LoadDefault().SaveToFile(mappingPath);
                stagedFiles.Add(MappingEntry);

                string rebarPath = Path.Combine(stage, "rebar", "rebar_unit_weight.csv");
                EnsureDirectoryForFile(rebarPath);
                WriteRebarRows(rebarPath, OviaRebarUnitWeightStore.LoadRows());
                stagedFiles.Add(RebarEntry);

                int connectionCount = ExportConnections(stage, stagedFiles);

                OviaBackupManifest manifest = new OviaBackupManifest();
                manifest.product = "OVIA";
                manifest.format_version = CurrentFormatVersion;
                manifest.created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                manifest.company_id = companyId == null ? "" : companyId.Trim();
                manifest.contents.Add("system_settings");
                manifest.contents.Add("company_connections");
                manifest.contents.Add("barlist_mapping");
                manifest.contents.Add("rebar_unit_weight");

                for (int i = 0; i < stagedFiles.Count; i++)
                {
                    string relative = NormalizeEntryPath(stagedFiles[i]);
                    string full = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        continue;
                    }

                    OviaBackupManifestFile item = new OviaBackupManifestFile();
                    item.path = relative;
                    item.length = new FileInfo(full).Length;
                    item.sha256 = ComputeSha256(full);
                    manifest.files.Add(item);
                }

                string manifestPath = Path.Combine(stage, ManifestEntry);
                WriteJson(manifestPath, manifest);

                string destinationDirectory = Path.GetDirectoryName(destinationZipPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                string tempZip = destinationZipPath + ".tmp";
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }

                using (FileStream stream = new FileStream(tempZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddFileToArchive(archive, manifestPath, ManifestEntry);

                    for (int i = 0; i < manifest.files.Count; i++)
                    {
                        string relative = manifest.files[i].path;
                        string full = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                        AddFileToArchive(archive, full, relative);
                    }
                }

                if (File.Exists(destinationZipPath))
                {
                    File.Delete(destinationZipPath);
                }

                File.Move(tempZip, destinationZipPath);
                return destinationZipPath;
            }
            finally
            {
                TryDeleteDirectory(stage);
            }
        }

        public static OviaBackupInspection InspectBackup(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new FileNotFoundException("선택한 OVIA 백업 파일을 찾을 수 없습니다.", zipPath);
            }

            using (FileStream stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ValidateArchiveEntries(archive);

                ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntry);
                if (manifestEntry == null)
                {
                    throw new InvalidDataException("OVIA 백업정보(manifest.json)가 없습니다.");
                }

                OviaBackupManifest manifest = ReadJsonEntry<OviaBackupManifest>(manifestEntry);
                ValidateManifest(manifest);

                Dictionary<string, ZipArchiveEntry> entryMap = BuildEntryMap(archive);
                VerifyManifestFiles(manifest, entryMap);

                OviaBackupInspection result = new OviaBackupInspection();
                result.Manifest = manifest;
                result.HasSystemSettings = entryMap.ContainsKey(SystemSettingsEntry);
                result.HasBarListMapping = entryMap.ContainsKey(MappingEntry);
                result.HasRebarUnitWeight = entryMap.ContainsKey(RebarEntry);

                int connections = 0;
                foreach (string key in entryMap.Keys)
                {
                    if (key.StartsWith(ConnectionPrefix, StringComparison.OrdinalIgnoreCase)
                        && key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    {
                        connections++;
                    }
                }

                result.CompanyConnectionCount = connections;
                result.HasCompanyConnections = connections > 0;

                return result;
            }
        }

        public static void RestoreBackup(string zipPath, OviaBackupRestoreSelection selection)
        {
            if (selection == null)
            {
                throw new ArgumentNullException("selection");
            }

            OviaBackupInspection inspection = InspectBackup(zipPath);

            if (!selection.SystemSettings
                && !selection.CompanyConnections
                && !selection.BarListMapping
                && !selection.RebarUnitWeight)
            {
                throw new InvalidOperationException("복원할 항목을 한 개 이상 선택해 주세요.");
            }

            string stage = CreateTemporaryDirectory("restore_stage");
            string rollback = CreateTemporaryDirectory("restore_rollback");

            try
            {
                ExtractAllowedFiles(zipPath, stage);
                ValidateStagedContent(stage, inspection, selection);

                RestoreTransaction transaction = new RestoreTransaction(rollback);

                try
                {
                    if (selection.SystemSettings && inspection.HasSystemSettings)
                    {
                        RestoreSystemSettings(stage, transaction);
                    }

                    if (selection.BarListMapping && inspection.HasBarListMapping)
                    {
                        string source = Path.Combine(stage, MappingEntry.Replace('/', Path.DirectorySeparatorChar));
                        string target = OviaBarListMappingStore.GetWritableMappingFilePath();
                        transaction.ReplaceFile(source, target);
                    }

                    if (selection.RebarUnitWeight && inspection.HasRebarUnitWeight)
                    {
                        string source = Path.Combine(stage, RebarEntry.Replace('/', Path.DirectorySeparatorChar));
                        string target = GetRebarWritablePath();
                        transaction.ReplaceFile(source, target);
                    }

                    if (selection.CompanyConnections && inspection.HasCompanyConnections)
                    {
                        RestoreConnections(stage, transaction);
                    }

                    transaction.Commit();
                    OviaSystemSettingsStore.ClearCache();
                }
                catch
                {
                    transaction.Rollback();
                    OviaSystemSettingsStore.ClearCache();
                    throw;
                }
            }
            finally
            {
                TryDeleteDirectory(stage);
                TryDeleteDirectory(rollback);
            }
        }

        private static OviaBackupSystemSettings BuildSystemSettingsBackup(string stage, List<string> stagedFiles)
        {
            OviaSystemSettings settings = OviaSystemSettingsStore.Load();
            OviaBackupSystemSettings dto = new OviaBackupSystemSettings();
            dto.company_logo_mode = settings.CompanyLogoMode == null ? "DEFAULT" : settings.CompanyLogoMode;
            dto.list_page_size = settings.ListPageSize;
            dto.brand_primary_hex = settings.BrandPrimaryHex;
            dto.brand_hover_hex = settings.BrandHoverHex;
            dto.loading_delay_unit = settings.LoadingDelayUnit;

            if (dto.company_logo_mode.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase)
                && File.Exists(settings.CompanyLogoFilePath))
            {
                string extension = NormalizeSafeExtension(Path.GetExtension(settings.CompanyLogoFilePath));
                string relative = AssetPrefix + "company_logo" + extension;
                string target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                CopyFile(settings.CompanyLogoFilePath, target);
                dto.company_logo_asset = relative;
                stagedFiles.Add(relative);
            }

            if (File.Exists(settings.LoadingAnimationImagePath))
            {
                string extension = NormalizeSafeExtension(Path.GetExtension(settings.LoadingAnimationImagePath));
                string relative = AssetPrefix + "loading_symbol" + extension;
                string target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                CopyFile(settings.LoadingAnimationImagePath, target);
                dto.loading_asset = relative;
                stagedFiles.Add(relative);
            }

            return dto;
        }

        private static int ExportConnections(string stage, List<string> stagedFiles)
        {
            string folder = OviaCompanyConnectionStore.GetConnectionFolder();
            if (!Directory.Exists(folder))
            {
                return 0;
            }

            string[] files = Directory.GetFiles(folder, "*.ini", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int count = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string relative = ConnectionPrefix + fileName;
                string target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                CopyFile(files[i], target);
                stagedFiles.Add(relative);
                count++;
            }

            return count;
        }

        private static void RestoreSystemSettings(string stage, RestoreTransaction transaction)
        {
            string sourceJson = Path.Combine(stage, SystemSettingsEntry.Replace('/', Path.DirectorySeparatorChar));
            OviaBackupSystemSettings dto = ReadJsonFile<OviaBackupSystemSettings>(sourceJson);

            if (dto == null)
            {
                throw new InvalidDataException("시스템 설정 백업정보를 읽을 수 없습니다.");
            }

            OviaSystemSettings current = OviaSystemSettingsStore.Load();
            OviaSystemSettings restored = new OviaSystemSettings();

            // 버전정보와 ERP 연결정보는 이 백업 항목의 대상이 아니다.
            restored.VersionText = current.VersionText;
            restored.ErpLoginUrl = current.ErpLoginUrl;
            restored.ErpBaseDomain = current.ErpBaseDomain;
            restored.ErpConnectionPath = current.ErpConnectionPath;
            restored.ErpAuthPath = current.ErpAuthPath;

            restored.CompanyLogoMode = string.IsNullOrWhiteSpace(dto.company_logo_mode) ? "DEFAULT" : dto.company_logo_mode.Trim();
            restored.ListPageSize = dto.list_page_size;
            restored.BrandPrimaryHex = dto.brand_primary_hex;
            restored.BrandHoverHex = dto.brand_hover_hex;
            restored.LoadingDelayUnit = dto.loading_delay_unit;

            string settingsFolder = OviaSystemSettingsStore.GetSettingsFolder();

            if (!string.IsNullOrWhiteSpace(dto.company_logo_asset))
            {
                string source = ResolveStagedSafePath(stage, dto.company_logo_asset);
                string extension = NormalizeSafeExtension(Path.GetExtension(source));
                string target = Path.Combine(settingsFolder, "Brand", "company_logo" + extension);
                transaction.ReplaceFile(source, target);
                restored.CompanyLogoFilePath = target;
                restored.CompanyLogoMode = "CUSTOM";
            }
            else
            {
                restored.CompanyLogoFilePath = "";
            }

            if (!string.IsNullOrWhiteSpace(dto.loading_asset))
            {
                string source = ResolveStagedSafePath(stage, dto.loading_asset);
                string extension = NormalizeSafeExtension(Path.GetExtension(source));
                string target = Path.Combine(settingsFolder, "Loading", "loading_symbol" + extension);
                transaction.ReplaceFile(source, target);
                restored.LoadingAnimationImagePath = target;
            }
            else
            {
                restored.LoadingAnimationImagePath = "";
            }

            string settingsFile = OviaSystemSettingsStore.GetSettingsFilePath();
            transaction.BackupTarget(settingsFile);
            OviaSystemSettingsStore.Save(restored);
            transaction.MarkTargetWritten(settingsFile);
        }

        private static void RestoreConnections(string stage, RestoreTransaction transaction)
        {
            string sourceFolder = Path.Combine(stage, "connections");
            if (!Directory.Exists(sourceFolder))
            {
                return;
            }

            string targetFolder = OviaCompanyConnectionStore.GetConnectionFolder();
            string[] files = Directory.GetFiles(sourceFolder, "*.ini", SearchOption.TopDirectoryOnly);

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string target = Path.Combine(targetFolder, fileName);
                transaction.ReplaceFile(files[i], target);
            }
        }

        private static void ValidateStagedContent(string stage, OviaBackupInspection inspection, OviaBackupRestoreSelection selection)
        {
            if (selection.SystemSettings && inspection.HasSystemSettings)
            {
                string path = Path.Combine(stage, SystemSettingsEntry.Replace('/', Path.DirectorySeparatorChar));
                OviaBackupSystemSettings settings = ReadJsonFile<OviaBackupSystemSettings>(path);

                if (settings == null)
                {
                    throw new InvalidDataException("시스템 설정 백업정보가 올바르지 않습니다.");
                }

                if (settings.list_page_size <= 0 || settings.list_page_size > 10000)
                {
                    throw new InvalidDataException("백업 파일의 목록 페이지 크기 값이 올바르지 않습니다.");
                }

                if (!string.IsNullOrWhiteSpace(settings.company_logo_asset))
                {
                    ResolveStagedSafePath(stage, settings.company_logo_asset);
                }

                if (!string.IsNullOrWhiteSpace(settings.loading_asset))
                {
                    ResolveStagedSafePath(stage, settings.loading_asset);
                }
            }

            if (selection.BarListMapping && inspection.HasBarListMapping)
            {
                string path = Path.Combine(stage, MappingEntry.Replace('/', Path.DirectorySeparatorChar));
                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer serializer = CreateSerializer();
                object parsed = serializer.DeserializeObject(json);
                if (parsed == null || json.IndexOf("\"standardColumns\"", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException("BarList 항목 매핑 백업정보가 올바르지 않습니다.");
                }
            }

            if (selection.RebarUnitWeight && inspection.HasRebarUnitWeight)
            {
                string path = Path.Combine(stage, RebarEntry.Replace('/', Path.DirectorySeparatorChar));
                string firstLine = ReadFirstTextLine(path);
                if (firstLine.IndexOf("규격", StringComparison.OrdinalIgnoreCase) < 0
                    || firstLine.IndexOf("단위중량", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException("이형철근 단위중량표 백업정보가 올바르지 않습니다.");
                }
            }

            if (selection.CompanyConnections && inspection.HasCompanyConnections)
            {
                string folder = Path.Combine(stage, "connections");
                string[] files = Directory.GetFiles(folder, "*.ini", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string text = File.ReadAllText(files[i], Encoding.UTF8);
                    if (text.IndexOf("CompanyId=", StringComparison.OrdinalIgnoreCase) < 0
                        || text.IndexOf("ErpBaseDomain=", StringComparison.OrdinalIgnoreCase) < 0
                        || text.IndexOf("ErpConnectionPath=", StringComparison.OrdinalIgnoreCase) < 0
                        || text.IndexOf("ErpAuthPath=", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        throw new InvalidDataException("ERP 연결 설정 백업정보가 올바르지 않습니다: " + Path.GetFileName(files[i]));
                    }
                }
            }
        }

        private static void ValidateManifest(OviaBackupManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidDataException("OVIA 백업정보를 읽을 수 없습니다.");
            }

            if (!string.Equals(manifest.product, "OVIA", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("OVIA 백업 파일이 아닙니다.");
            }

            if (manifest.format_version != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    "지원하지 않는 OVIA 백업 형식입니다.\r\n"
                    + "백업 형식: " + manifest.format_version.ToString(CultureInfo.InvariantCulture)
                    + " / 현재 지원: " + CurrentFormatVersion.ToString(CultureInfo.InvariantCulture));
            }

            if (manifest.files == null)
            {
                throw new InvalidDataException("OVIA 백업 파일 목록이 없습니다.");
            }
        }

        private static void VerifyManifestFiles(OviaBackupManifest manifest, Dictionary<string, ZipArchiveEntry> entryMap)
        {
            for (int i = 0; i < manifest.files.Count; i++)
            {
                OviaBackupManifestFile item = manifest.files[i];
                if (item == null || string.IsNullOrWhiteSpace(item.path))
                {
                    throw new InvalidDataException("백업 파일 목록에 잘못된 항목이 있습니다.");
                }

                string path = NormalizeEntryPath(item.path);
                ZipArchiveEntry entry;
                if (!entryMap.TryGetValue(path, out entry))
                {
                    throw new InvalidDataException("백업 내부 파일이 누락되었습니다: " + path);
                }

                if (entry.Length != item.length)
                {
                    throw new InvalidDataException("백업 내부 파일 크기가 일치하지 않습니다: " + path);
                }

                string hash = ComputeSha256(entry);
                if (!string.Equals(hash, item.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("백업 내부 파일 검증에 실패했습니다: " + path);
                }
            }
        }

        private static void ValidateArchiveEntries(ZipArchive archive)
        {
            for (int i = 0; i < archive.Entries.Count; i++)
            {
                ZipArchiveEntry entry = archive.Entries[i];
                string path = NormalizeEntryPath(entry.FullName);

                if (path == "" || path.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (path == ManifestEntry
                    || path == SystemSettingsEntry
                    || path == MappingEntry
                    || path == RebarEntry
                    || IsSafeChildEntry(path, ConnectionPrefix)
                    || IsSafeChildEntry(path, AssetPrefix))
                {
                    continue;
                }

                throw new InvalidDataException("허용되지 않은 파일이 백업에 포함되어 있습니다: " + path);
            }
        }

        private static bool IsSafeChildEntry(string path, string prefix)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = path.Substring(prefix.Length);
            return remainder != ""
                && remainder.IndexOf('/') < 0
                && remainder.IndexOf('\\') < 0
                && remainder != "."
                && remainder != "..";
        }

        private static Dictionary<string, ZipArchiveEntry> BuildEntryMap(ZipArchive archive)
        {
            Dictionary<string, ZipArchiveEntry> map =
                new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < archive.Entries.Count; i++)
            {
                ZipArchiveEntry entry = archive.Entries[i];
                string key = NormalizeEntryPath(entry.FullName);
                if (key == "" || key.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (map.ContainsKey(key))
                {
                    throw new InvalidDataException("백업 파일에 중복된 경로가 있습니다: " + key);
                }

                map.Add(key, entry);
            }

            return map;
        }

        private static void ExtractAllowedFiles(string zipPath, string stage)
        {
            using (FileStream stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ValidateArchiveEntries(archive);

                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    string relative = NormalizeEntryPath(entry.FullName);
                    if (relative == "" || relative.EndsWith("/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string target = ResolveStagedSafePathForWrite(stage, relative);
                    EnsureDirectoryForFile(target);

                    using (Stream source = entry.Open())
                    using (FileStream destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(destination);
                    }
                }
            }
        }

        private static string ResolveStagedSafePath(string stage, string relative)
        {
            string full = ResolveStagedSafePathForWrite(stage, relative);
            if (!File.Exists(full))
            {
                throw new InvalidDataException("백업 내부 파일을 찾을 수 없습니다: " + relative);
            }

            return full;
        }

        private static string ResolveStagedSafePathForWrite(string stage, string relative)
        {
            relative = NormalizeEntryPath(relative);
            if (relative.Contains(".."))
            {
                throw new InvalidDataException("잘못된 백업 경로입니다.");
            }

            string root = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("백업 경로가 허용된 영역을 벗어났습니다.");
            }

            return full;
        }

        private static string GetRebarWritablePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OVIA",
                "Rebar",
                "rebar_unit_weight.csv");
        }

        private static void WriteRebarRows(string path, List<RebarUnitWeightRow> rows)
        {
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(BuildRebarHeader());

                if (rows == null)
                {
                    return;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    RebarUnitWeightRow row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    StringBuilder line = new StringBuilder();
                    line.Append(Csv(row.Spec));
                    line.Append(",").Append(Csv(row.UnitWeightKgPerMeter.ToString("0.000", CultureInfo.InvariantCulture)));

                    for (int j = 0; j < 9; j++)
                    {
                        line.Append(",").Append(Csv(GetArrayValue(row.OneBarWeights, j)));
                        line.Append(",").Append(Csv(GetArrayValue(row.BundleCounts, j)));
                        line.Append(",").Append(Csv(GetArrayValue(row.BundleWeights, j)));
                        line.Append(",").Append(Csv(GetArrayValue(row.TotalLengths, j)));
                    }

                    writer.WriteLine(line.ToString());
                }
            }
        }

        private static string BuildRebarHeader()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("규격,단위중량(kg/m)");
            string[] lengths = new string[] { "6", "6.5", "7", "7.5", "8", "9", "10", "11", "12" };

            for (int i = 0; i < lengths.Length; i++)
            {
                builder.Append(",").Append(lengths[i]).Append("_1본중량");
                builder.Append(",").Append(lengths[i]).Append("_총본수");
                builder.Append(",").Append(lengths[i]).Append("_중량");
                builder.Append(",").Append(lengths[i]).Append("_총길이");
            }

            return builder.ToString();
        }

        private static string GetArrayValue(string[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null)
            {
                return "";
            }

            return values[index];
        }

        private static string Csv(string value)
        {
            value = value == null ? "" : value;
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string ReadFirstTextLine(string path)
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
            {
                return reader.ReadLine() ?? "";
            }
        }

        private static void AddFileToArchive(ZipArchive archive, string sourcePath, string entryPath)
        {
            ZipArchiveEntry entry = archive.CreateEntry(NormalizeEntryPath(entryPath), CompressionLevel.Optimal);
            using (Stream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Stream destination = entry.Open())
            {
                source.CopyTo(destination);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return ComputeSha256(stream);
            }
        }

        private static string ComputeSha256(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return ComputeSha256(stream);
            }
        }

        private static string ComputeSha256(Stream stream)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            serializer.RecursionLimit = 128;
            return serializer;
        }

        private static void WriteJson<T>(string path, T value)
        {
            EnsureDirectoryForFile(path);
            JavaScriptSerializer serializer = CreateSerializer();
            File.WriteAllText(path, serializer.Serialize(value), new UTF8Encoding(false));
        }

        private static T ReadJsonFile<T>(string path)
        {
            JavaScriptSerializer serializer = CreateSerializer();
            return serializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        private static T ReadJsonEntry<T>(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                JavaScriptSerializer serializer = CreateSerializer();
                return serializer.Deserialize<T>(reader.ReadToEnd());
            }
        }

        private static string NormalizeEntryPath(string path)
        {
            return (path == null ? "" : path.Trim()).Replace('\\', '/').TrimStart('/');
        }

        private static string NormalizeSafeExtension(string extension)
        {
            string value = extension == null ? "" : extension.Trim().ToLowerInvariant();
            if (value == ".png" || value == ".jpg" || value == ".jpeg" || value == ".bmp" || value == ".gif")
            {
                return value;
            }

            return ".png";
        }

        private static string CreateTemporaryDirectory(string prefix)
        {
            string root = Path.Combine(Path.GetTempPath(), "OVIA", "BackupRestore");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, prefix + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void CopyFile(string source, string target)
        {
            EnsureDirectoryForFile(target);
            File.Copy(source, target, true);
        }

        private static void EnsureDirectoryForFile(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private sealed class RestoreTransaction
        {
            private readonly string rollbackRoot;
            private readonly Dictionary<string, string> backups =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> originallyMissing =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> writtenTargets =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private bool committed;

            public RestoreTransaction(string rollbackRoot)
            {
                this.rollbackRoot = rollbackRoot;
            }

            public void ReplaceFile(string source, string target)
            {
                BackupTarget(target);
                EnsureDirectoryForFile(target);

                string temp = target + ".ovia_restore_tmp";
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }

                File.Copy(source, temp, true);

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temp, target);
                MarkTargetWritten(target);
            }

            public void BackupTarget(string target)
            {
                if (backups.ContainsKey(target) || originallyMissing.Contains(target))
                {
                    return;
                }

                if (File.Exists(target))
                {
                    string backupPath = Path.Combine(rollbackRoot, Guid.NewGuid().ToString("N") + ".bak");
                    File.Copy(target, backupPath, true);
                    backups.Add(target, backupPath);
                }
                else
                {
                    originallyMissing.Add(target);
                }
            }

            public void MarkTargetWritten(string target)
            {
                writtenTargets.Add(target);
            }

            public void Commit()
            {
                committed = true;
            }

            public void Rollback()
            {
                if (committed)
                {
                    return;
                }

                foreach (string target in writtenTargets)
                {
                    try
                    {
                        if (originallyMissing.Contains(target) && File.Exists(target))
                        {
                            File.Delete(target);
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (KeyValuePair<string, string> pair in backups)
                {
                    try
                    {
                        EnsureDirectoryForFile(pair.Key);
                        File.Copy(pair.Value, pair.Key, true);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
