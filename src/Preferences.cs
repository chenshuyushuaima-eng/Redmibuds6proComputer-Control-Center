// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace BudsControl
{
    public sealed class Preferences
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiBudsControl");
        private static readonly string Pathname = Path.Combine(Folder,"preferences.ini");
        public string DeviceId = "";
        public bool AskBeforeClose = true, CloseToTray = true, AutoReconnect = true;
        public static Preferences Load()
        {
            var settings = new Preferences();
            try
            {
                if (!File.Exists(Pathname)) return settings;
                foreach (string line in File.ReadAllLines(Pathname))
                {
                    if (line.StartsWith("device=")) settings.DeviceId = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(7)));
                    if (line.StartsWith("askclose=")) settings.AskBeforeClose = line.Substring(9) == "1";
                    if (line.StartsWith("tray=")) settings.CloseToTray = line.Substring(5) == "1";
                    if (line.StartsWith("reconnect=")) settings.AutoReconnect = line.Substring(10) == "1";
                }
            }
            catch { }
            return settings;
        }
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Pathname,new[] { "device=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(DeviceId)),
                "askclose=" + (AskBeforeClose ? "1" : "0"),"tray=" + (CloseToTray ? "1" : "0"),"reconnect=" + (AutoReconnect ? "1" : "0") },Encoding.UTF8);
        }
        public static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("MiBudsControl") != null;
        }
        public static void Startup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("MiBudsControl","\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --tray");
                else key.DeleteValue("MiBudsControl",false);
            }
        }
    }
}
