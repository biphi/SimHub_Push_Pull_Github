using System;
using System.IO;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace SimHub_Push_Pull_Github
{
    public class PluginSettings
    {
        public string RemoteUrl { get; set; }
        public string Branch { get; set; }
        public bool AutoPullOnStart { get; set; }
        public string DashboardsPath { get; set; }
        public List<string> SelectedDashboards { get; set; }
        public string GitUsername { get; set; }
        public string GitToken { get; set; }
        // New: sequential build / version increment
        public int BuildNumber { get; set; }

        public PluginSettings()
        {
            RemoteUrl = string.Empty;
            Branch = "master";
            AutoPullOnStart = false;
            DashboardsPath = string.Empty;
            SelectedDashboards = new List<string>();
            GitUsername = string.Empty;
            GitToken = string.Empty;
            BuildNumber = 0; // start at 0
        }

        internal static string GetSettingsPath()
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimHub", "Plugins", "GitHubDashboardSync");
            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, "settings.xml");
        }

        public static PluginSettings Load()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return new PluginSettings();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var ser = new XmlSerializer(typeof(PluginSettings));
                    return (PluginSettings)ser.Deserialize(fs);
                }
            }
            catch
            {
                return new PluginSettings();
            }
        }

        public void Save()
        {
            var path = GetSettingsPath();
            try
            {
                using (var fs = File.Create(path))
                {
                    var ser = new XmlSerializer(typeof(PluginSettings));
                    ser.Serialize(fs, this);
                }
            }
            catch
            {
                // ignore persistence issues
            }
        }
    }
}
