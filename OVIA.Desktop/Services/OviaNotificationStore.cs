using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        public bool IsConfirmed = false;
    }

    public static class OviaNotificationStore
    {
        private const string NotificationFolderName = "Notifications";
        private const string NotificationFileName = "work_notifications.dat";
        private static readonly object syncRoot = new object();

        public static event EventHandler NotificationsChanged;

        public static void AddWorkLog(string companyId, string userId, string workContent, string workPath)
        {
            AddWorkLog(companyId, userId, workContent, workPath, userId);
        }

        public static void AddWorkLog(string companyId, string userId, string workContent, string workPath, string worker)
        {
            if (string.IsNullOrWhiteSpace(workContent))
            {
                return;
            }

            lock (syncRoot)
            {
                List<OviaNotificationEntry> entries = LoadAllInternal(false);
                entries.Add(new OviaNotificationEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CompanyId = Safe(companyId),
                    UserId = Safe(userId),
                    WorkContent = Safe(workContent),
                    WorkPath = Safe(workPath),
                    WorkDate = DateTime.Now,
                    Worker = string.IsNullOrWhiteSpace(worker) ? Safe(userId) : Safe(worker),
                    IsConfirmed = false
                });

                SaveAllInternal(Prune(entries));
            }

            RaiseNotificationsChanged();
        }

        public static int GetUnreadCount(string companyId, string userId)
        {
            List<OviaNotificationEntry> entries = GetVisibleEntries(companyId, userId);
            int count = 0;
            int i;

            for (i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsConfirmed)
                {
                    count++;
                }
            }

            return count;
        }

        public static List<OviaNotificationEntry> GetVisibleEntries(string companyId, string userId)
        {
            lock (syncRoot)
            {
                List<OviaNotificationEntry> entries = LoadAllInternal(true);
                bool isAdmin = OviaSystemSettingsStore.IsSuperAdminUser(userId);
                string currentCompany = Safe(companyId);
                string currentUser = Safe(userId);
                List<OviaNotificationEntry> result = new List<OviaNotificationEntry>();
                int i;

                for (i = 0; i < entries.Count; i++)
                {
                    OviaNotificationEntry entry = entries[i];

                    if (!IsSameCompanyOrBlank(entry.CompanyId, currentCompany))
                    {
                        continue;
                    }

                    if (!isAdmin && !string.Equals(entry.UserId, currentUser, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(Clone(entry));
                }

                result.Sort(delegate(OviaNotificationEntry a, OviaNotificationEntry b)
                {
                    int dateCompare = b.WorkDate.CompareTo(a.WorkDate);
                    if (dateCompare != 0)
                    {
                        return dateCompare;
                    }

                    return string.Compare(b.Id, a.Id, StringComparison.OrdinalIgnoreCase);
                });

                return result;
            }
        }

        public static void Confirm(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            bool changed = false;

            lock (syncRoot)
            {
                List<OviaNotificationEntry> entries = LoadAllInternal(false);
                int i;

                for (i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!entries[i].IsConfirmed)
                        {
                            entries[i].IsConfirmed = true;
                            changed = true;
                        }
                        break;
                    }
                }

                if (changed)
                {
                    SaveAllInternal(Prune(entries));
                }
            }

            if (changed)
            {
                RaiseNotificationsChanged();
            }
        }

        public static void ConfirmMany(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            bool changed = false;

            lock (syncRoot)
            {
                List<OviaNotificationEntry> entries = LoadAllInternal(false);
                int i;
                int j;

                for (i = 0; i < entries.Count; i++)
                {
                    for (j = 0; j < ids.Count; j++)
                    {
                        if (string.Equals(entries[i].Id, ids[j], StringComparison.OrdinalIgnoreCase))
                        {
                            if (!entries[i].IsConfirmed)
                            {
                                entries[i].IsConfirmed = true;
                                changed = true;
                            }
                            break;
                        }
                    }
                }

                if (changed)
                {
                    SaveAllInternal(Prune(entries));
                }
            }

            if (changed)
            {
                RaiseNotificationsChanged();
            }
        }

        public static string GetNotificationFilePath()
        {
            return Path.Combine(GetNotificationFolder(), NotificationFileName);
        }

        private static void RaiseNotificationsChanged()
        {
            EventHandler handler = NotificationsChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        private static List<OviaNotificationEntry> LoadAllInternal(bool pruneAndSave)
        {
            List<OviaNotificationEntry> entries = new List<OviaNotificationEntry>();
            string path = GetNotificationFilePath();

            if (!File.Exists(path))
            {
                return entries;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int i;

                for (i = 0; i < lines.Length; i++)
                {
                    OviaNotificationEntry entry = ParseLine(lines[i]);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch
            {
                return new List<OviaNotificationEntry>();
            }

            List<OviaNotificationEntry> pruned = Prune(entries);
            if (pruneAndSave && pruned.Count != entries.Count)
            {
                SaveAllInternal(pruned);
            }

            return pruned;
        }

        private static List<OviaNotificationEntry> Prune(List<OviaNotificationEntry> entries)
        {
            List<OviaNotificationEntry> result = new List<OviaNotificationEntry>();
            DateTime minDate = DateTime.Now.AddDays(-7);
            int i;

            if (entries == null)
            {
                return result;
            }

            for (i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].WorkDate >= minDate)
                {
                    result.Add(entries[i]);
                }
            }

            return result;
        }

        private static void SaveAllInternal(List<OviaNotificationEntry> entries)
        {
            string folder = GetNotificationFolder();
            Directory.CreateDirectory(folder);

            List<string> lines = new List<string>();
            int i;

            if (entries != null)
            {
                for (i = 0; i < entries.Count; i++)
                {
                    lines.Add(FormatLine(entries[i]));
                }
            }

            File.WriteAllLines(GetNotificationFilePath(), lines.ToArray(), Encoding.UTF8);
        }

        private static string GetNotificationFolder()
        {
            return Path.Combine(OviaSystemSettingsStore.GetSettingsFolder(), NotificationFolderName);
        }

        private static string FormatLine(OviaNotificationEntry entry)
        {
            if (entry == null)
            {
                entry = new OviaNotificationEntry();
            }

            string[] values = new string[]
            {
                entry.Id,
                entry.CompanyId,
                entry.UserId,
                entry.WorkContent,
                entry.WorkPath,
                entry.WorkDate.ToString("o"),
                entry.Worker,
                entry.IsConfirmed ? "1" : "0"
            };

            int i;
            for (i = 0; i < values.Length; i++)
            {
                values[i] = Encode(values[i]);
            }

            return string.Join("\t", values);
        }

        private static OviaNotificationEntry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string[] values = line.Split('\t');
            if (values.Length < 8)
            {
                return null;
            }

            DateTime workDate;
            if (!DateTime.TryParse(Decode(values[5]), null, System.Globalization.DateTimeStyles.RoundtripKind, out workDate))
            {
                workDate = DateTime.Now;
            }

            OviaNotificationEntry entry = new OviaNotificationEntry();
            entry.Id = Decode(values[0]);
            entry.CompanyId = Decode(values[1]);
            entry.UserId = Decode(values[2]);
            entry.WorkContent = Decode(values[3]);
            entry.WorkPath = Decode(values[4]);
            entry.WorkDate = workDate;
            entry.Worker = Decode(values[6]);
            entry.IsConfirmed = Decode(values[7]) == "1";

            if (entry.Id.Trim() == "")
            {
                entry.Id = Guid.NewGuid().ToString("N");
            }

            return entry;
        }

        private static OviaNotificationEntry Clone(OviaNotificationEntry entry)
        {
            OviaNotificationEntry clone = new OviaNotificationEntry();
            clone.Id = entry.Id;
            clone.CompanyId = entry.CompanyId;
            clone.UserId = entry.UserId;
            clone.WorkContent = entry.WorkContent;
            clone.WorkPath = entry.WorkPath;
            clone.WorkDate = entry.WorkDate;
            clone.Worker = entry.Worker;
            clone.IsConfirmed = entry.IsConfirmed;
            return clone;
        }

        private static bool IsSameCompanyOrBlank(string entryCompanyId, string currentCompanyId)
        {
            string entryCompany = Safe(entryCompanyId);
            string currentCompany = Safe(currentCompanyId);

            if (entryCompany == "" || currentCompany == "")
            {
                return true;
            }

            return string.Equals(entryCompany, currentCompany, StringComparison.OrdinalIgnoreCase);
        }

        private static string Safe(string value)
        {
            return value == null ? "" : value.Trim();
        }

        private static string Encode(string value)
        {
            if (value == null)
            {
                value = "";
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            try
            {
                if (value == null)
                {
                    return "";
                }

                return Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));
            }
            catch
            {
                return "";
            }
        }
    }
}
