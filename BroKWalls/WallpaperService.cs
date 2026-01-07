using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BroKWalls
{
    public class WallpaperService
    {
        private static readonly HttpClient _client = new HttpClient();
        private const string SourceImageFileName = "immich_source.jpg";

        public static string SourceImagePath => Path.Combine(Path.GetTempPath(), SourceImageFileName);

        public event Action<string>? StatusUpdated;

        private void ReportStatus(string message)
        {
            StatusUpdated?.Invoke(message);
        }

        public async Task PerformAutoChangeAsync()
        {
            var config = ConfigManager.Load();
            if (config == null) return;

            try
            {
                if (config.Provider == PhotoProvider.Local)
                {
                    await ProcessLocalProvider(config);
                    return;
                }

                string immichUrl = config.BaseUrl;

                // Setup Auth
                if (config.Provider == PhotoProvider.Google)
                {
                    string? token = await GetGoogleAccessToken(config);
                    if (token == null) return;
                    _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                else
                {
                    if (string.IsNullOrEmpty(config.BaseUrl)) return;
                    
                    if (_client.DefaultRequestHeaders.Contains("x-api-key"))
                        _client.DefaultRequestHeaders.Remove("x-api-key");
                        
                    _client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                    immichUrl = await GetResponsiveBaseUrl(config);
                }

                // Fetch Candidates
                var rawAssets = await FetchCandidates(config, immichUrl);

                if (rawAssets.Count > 0)
                {
                    // Deduplicate results by ID
                    var uniqueAssets = rawAssets.GroupBy(a => (string)a.id).Select(g => g.First()).ToList();

                    Random rnd = new Random();
                    var selected = uniqueAssets[rnd.Next(uniqueAssets.Count)];
                    string? downloadUrl = (config.Provider == PhotoProvider.Google) 
                        ? (string)selected.baseUrl + "=w2500-h2500" 
                        : $"{immichUrl}/api/assets/{selected.id}/original";

                    byte[] imageBytes = await _client.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(SourceImagePath, imageBytes);
                    WallpaperHelper.SetWallpaper(SourceImagePath, DesktopWallpaperPosition.Fill);
                    ReportStatus("Wallpaper updated successfully.");
                }
            }
            catch (Exception ex) 
            {
                ReportStatus($"Error updating wallpaper: {ex.Message}");
            }
        }

        private async Task ProcessLocalProvider(AppConfig config)
        {
            if (string.IsNullOrEmpty(config.LocalFolderPath) || !Directory.Exists(config.LocalFolderPath)) return;

            await Task.Run(() =>
            {
                var files = Directory.GetFiles(config.LocalFolderPath, "*.*")
                    .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count > 0)
                {
                    var file = files[new Random().Next(files.Count)];
                    File.Copy(file, SourceImagePath, true);
                    WallpaperHelper.SetWallpaper(SourceImagePath, DesktopWallpaperPosition.Fill);
                    ReportStatus("Local wallpaper updated.");
                }
            });
        }

        private async Task<List<dynamic>> FetchCandidates(AppConfig config, string immichUrl)
        {
            var rawAssets = new List<dynamic>();

            if (config.Provider == PhotoProvider.Google)
            {
                HttpResponseMessage resp;
                if (config.Mode == PhotoMode.Album && !string.IsNullOrEmpty(config.GoogleAlbumId))
                {
                    var data = new { albumId = config.GoogleAlbumId, pageSize = 100 };
                    resp = await _client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json"));
                }
                else if (config.Mode == PhotoMode.People && !string.IsNullOrEmpty(config.GoogleCategories))
                {
                    var categories = config.GoogleCategories.Split(',').Select(s => s.Trim()).ToArray();
                    var data = new { filters = new { contentFilter = new { includedContentCategories = categories } }, pageSize = 100 };
                    resp = await _client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json"));
                }
                else
                {
                    resp = await _client.GetAsync("https://photoslibrary.googleapis.com/v1/mediaItems?pageSize=100");
                }

                if (resp.IsSuccessStatusCode)
                {
                    dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                    if (result?.mediaItems != null) foreach (var item in result.mediaItems) rawAssets.Add(item);
                }
            }
            else // Immich
            {
                if (config.Mode == PhotoMode.People)
                {
                    var allIds = config.ImmichPersonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

                    foreach (var id in allIds)
                    {
                        object searchData;
                        if (id.StartsWith("tag:")) searchData = new { tagIds = new[] { id.Replace("tag:", "") }, withArchived = false, size = 500 };
                        else searchData = new { personIds = new[] { id }, withArchived = false, size = 500 };

                        var resp = await _client.PostAsync($"{immichUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                        if (resp.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                            if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                        }
                    }
                }
                else if (config.Mode == PhotoMode.Album)
                {
                    var resp = await _client.GetAsync($"{immichUrl}/api/albums/{config.ImmichAlbumId}");
                    if (resp.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                        if (result?.assets != null) foreach (var item in result.assets) rawAssets.Add(item);
                    }
                }
                else
                {
                    var searchData = new { type = "IMAGE", size = 500, withArchived = false };
                    var resp = await _client.PostAsync($"{immichUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                    if (resp.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                        if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                    }
                }
            }

            return rawAssets;
        }

        public async Task<string?> GetGoogleAccessToken(AppConfig config)
        {
            if (string.IsNullOrEmpty(config.GoogleRefreshToken)) return null;
            try
            {
                var values = new Dictionary<string, string>
                {
                    { "client_id", config.GoogleClientId },
                    { "client_secret", config.GoogleClientSecret },
                    { "refresh_token", config.GoogleRefreshToken },
                    { "grant_type", "refresh_token" }
                };
                var response = await _client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values));
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;
                dynamic result = JsonConvert.DeserializeObject(json)!;
                return (string)result.access_token;
            }
            catch { return null; }
        }

        public async Task<string> GetResponsiveBaseUrl(AppConfig config)
        {
            // Try Primary
            try
            {
                using var cts = new CancellationTokenSource(1500);
                var resp = await _client.GetAsync($"{config.BaseUrl}/api/server-info/ping", cts.Token);
                if (resp.IsSuccessStatusCode) return config.BaseUrl;
            }
            catch { }

            // Try Fallback
            if (!string.IsNullOrEmpty(config.FallbackBaseUrl))
            {
                try
                {
                    using var cts = new CancellationTokenSource(1500);
                    var resp = await _client.GetAsync($"{config.FallbackBaseUrl}/api/server-info/ping", cts.Token);
                    if (resp.IsSuccessStatusCode) return config.FallbackBaseUrl;
                }
                catch { }
            }

            return config.BaseUrl;
        }
        
        // Helper to fetch thumbnails for the gallery UI
        public async Task<List<ImmichPhoto>> FetchGalleryPhotos(AppConfig config, int count)
        {
             if (config.Provider == PhotoProvider.Local)
            {
                if (string.IsNullOrEmpty(config.LocalFolderPath) || !Directory.Exists(config.LocalFolderPath)) 
                    return new List<ImmichPhoto>();
                
                var files = Directory.GetFiles(config.LocalFolderPath, "*.*")
                    .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => Guid.NewGuid())
                    .Take(count)
                    .ToList();

                var list = new List<ImmichPhoto>();
                foreach (var file in files) list.Add(new ImmichPhoto { Id = file });
                return list;
            }

            string immichUrl = config.BaseUrl;
             // Setup Auth
            if (config.Provider == PhotoProvider.Google)
            {
                string? token = await GetGoogleAccessToken(config);
                if (token == null) throw new Exception("Google Auth failed");
                _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                if (_client.DefaultRequestHeaders.Contains("x-api-key"))
                     _client.DefaultRequestHeaders.Remove("x-api-key");
                _client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                immichUrl = await GetResponsiveBaseUrl(config);
            }
            
            var rawAssets = await FetchCandidates(config, immichUrl);
            if (rawAssets.Count == 0) return new List<ImmichPhoto>();

            var selectedAssets = rawAssets
                    .GroupBy(p => (string)p.id) 
                    .Select(g => g.First())
                    .OrderBy(x => Guid.NewGuid())
                    .Take(count)
                    .ToList();

            var displayList = new List<ImmichPhoto>();
             foreach (var asset in selectedAssets)
             {
                string thumbUrl = (config.Provider == PhotoProvider.Google) ? (string)asset.baseUrl + "=w500-h500" : $"{immichUrl}/api/assets/{asset.id}/thumbnail";
                // We just return the URL/ID here. The UI (MainWindow) will handle the actual Bitmap creation to keep this service UI-agnostic
                // Wait, ImmichPhoto expects a BitmapSource. Let's change ImmichPhoto or just return data.
                // Actually, I'll just return the bytes and ID? Or just the ID and let the UI fetch bytes?
                // For now, to keep it simple, I will download the bytes here but NOT create BitmapSource.
                
                byte[] thumbData = await _client.GetByteArrayAsync(thumbUrl);
                displayList.Add(new ImmichPhoto { Id = (string)asset.id, ThumbnailBytes = thumbData }); 
             }
             return displayList;
        }
        
        // Helper to download original for editor
        public async Task<byte[]> DownloadOriginal(AppConfig config, string assetId)
        {
             string immichUrl = config.BaseUrl;
             if (config.Provider == PhotoProvider.Google)
             {
                 string? token = await GetGoogleAccessToken(config);
                 if (token != null) _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                 
                  // Google needs a fresh fetch to get the baseUrl
                 var json = await _client.GetStringAsync($"https://photoslibrary.googleapis.com/v1/mediaItems/{assetId}");
                 dynamic res = JsonConvert.DeserializeObject(json)!;
                 string downloadUrl = (string)res.baseUrl + "=w2500-h2500";
                 return await _client.GetByteArrayAsync(downloadUrl);
             }
             else
             {
                 // Immich
                 if (_client.DefaultRequestHeaders.Contains("x-api-key")) _client.DefaultRequestHeaders.Remove("x-api-key");
                 _client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                 immichUrl = await GetResponsiveBaseUrl(config);
                 return await _client.GetByteArrayAsync($"{immichUrl}/api/assets/{assetId}/original");
             }
        }
    }


}
