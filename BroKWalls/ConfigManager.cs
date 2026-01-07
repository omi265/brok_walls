using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BroKWalls
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PhotoProvider
    {
        Immich,
        Google,
        Local
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PhotoMode
    {
        People,
        Album,
        Random
    }

    public class AppConfig
    {
        public PhotoProvider Provider { get; set; } = PhotoProvider.Immich;
        public PhotoMode Mode { get; set; } = PhotoMode.People;

        // Local Settings
        public string LocalFolderPath { get; set; } = "";

        // Immich Settings
        public string BaseUrl { get; set; } = "";
        public string FallbackBaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ImmichPersonIds { get; set; } = ""; 
        public string ImmichAlbumId { get; set; } = "";
        
        // Google Photos Settings
        public string GoogleClientId { get; set; } = "";
        public string GoogleClientSecret { get; set; } = "";
        public string GoogleRefreshToken { get; set; } = "";
        public string GoogleCategories { get; set; } = ""; // Used for "People/Pets" etc
        public string GoogleAlbumId { get; set; } = "";

        // Automation Settings
        public bool AutoChangeEnabled { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public int AutoChangeIntervalMinutes { get; set; } = 240;
    }

    public static class ConfigManager
    {
        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bro-k Walls",
            "config.json");

        public static void Save(AppConfig config)
        {
            try
            {
                var fileInfo = new FileInfo(ConfigPath);
                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save settings: {ex.Message}");
            }
        }

        public static AppConfig? Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonConvert.DeserializeObject<AppConfig>(json);
                }
            }
            catch { }
            return null;
        }
    }
}
