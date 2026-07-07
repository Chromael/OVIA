using System;
using Microsoft.Win32;

namespace OVIA.Desktop
{
    public class OviaWebView2RuntimeInfo
    {
        public bool IsAvailable = false;
        public string VersionText = "";
        public string Source = "";
    }

    public static class OviaWebView2RuntimeChecker
    {
        public static OviaWebView2RuntimeInfo GetRuntimeInfo()
        {
            OviaWebView2RuntimeInfo info = new OviaWebView2RuntimeInfo();

            TryFindRuntimeInRegistry(info, RegistryHive.LocalMachine, RegistryView.Registry64);
            if (info.IsAvailable)
            {
                return info;
            }

            TryFindRuntimeInRegistry(info, RegistryHive.LocalMachine, RegistryView.Registry32);
            if (info.IsAvailable)
            {
                return info;
            }

            TryFindRuntimeInRegistry(info, RegistryHive.CurrentUser, RegistryView.Registry64);
            if (info.IsAvailable)
            {
                return info;
            }

            TryFindRuntimeInRegistry(info, RegistryHive.CurrentUser, RegistryView.Registry32);
            return info;
        }

        public static bool IsRuntimeAvailable()
        {
            return GetRuntimeInfo().IsAvailable;
        }

        private static void TryFindRuntimeInRegistry(OviaWebView2RuntimeInfo info, RegistryHive hive, RegistryView view)
        {
            if (info == null || info.IsAvailable)
            {
                return;
            }

            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey clients = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients");

                if (clients == null)
                {
                    if (baseKey != null)
                    {
                        baseKey.Close();
                    }
                    return;
                }

                string[] subKeyNames = clients.GetSubKeyNames();
                int i;

                for (i = 0; i < subKeyNames.Length; i++)
                {
                    RegistryKey client = null;

                    try
                    {
                        client = clients.OpenSubKey(subKeyNames[i]);
                        if (client == null)
                        {
                            continue;
                        }

                        string name = ReadString(client, "name");
                        string version = ReadString(client, "pv");

                        if (IsWebView2RuntimeName(name) && !string.IsNullOrWhiteSpace(version))
                        {
                            info.IsAvailable = true;
                            info.VersionText = version.Trim();
                            info.Source = hive.ToString() + " / " + view.ToString();
                            break;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (client != null)
                        {
                            client.Close();
                        }
                    }
                }

                clients.Close();
                baseKey.Close();
            }
            catch
            {
            }
        }

        private static bool IsWebView2RuntimeName(string name)
        {
            string value = name == null ? "" : name.Trim().ToLowerInvariant();

            if (value == "")
            {
                return false;
            }

            return value.Contains("webview2") || value.Contains("webview 2");
        }

        private static string ReadString(RegistryKey key, string name)
        {
            if (key == null)
            {
                return "";
            }

            object value = key.GetValue(name);
            if (value == null)
            {
                return "";
            }

            return Convert.ToString(value) ?? "";
        }
    }
}
