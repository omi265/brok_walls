using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Controls;

namespace NasaWallpaperApp
{
    public class ImmichPhoto
    {
        public string? Id { get; set; }
        public BitmapSource? Thumbnail { get; set; }
    }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private AppConfig? currentConfig;
        private System.Windows.Forms.NotifyIcon? trayIcon;
        private System.Windows.Threading.DispatcherTimer? autoTimer;

        private string SourceImagePath => Path.Combine(Path.GetTempPath(), "immich_source.jpg");

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            SetupTray();
        }

        private void SetupTray()
        {
            try
            {
                trayIcon = new System.Windows.Forms.NotifyIcon();
                trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
                trayIcon.Visible = true;
                trayIcon.Text = "Immich Wallpaper - Click to Refresh";
                
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Open Gallery", null, (s, e) => { ShowWindow(); });
                contextMenu.Items.Add("Adjust Current Wallpaper", null, (s, e) => { OpenEditorFromCache(); });
                contextMenu.Items.Add("Refresh Wallpaper Now", null, (s, e) => { PerformAutoChange(); });
                contextMenu.Items.Add("-");
                contextMenu.Items.Add("Exit", null, (s, e) => { 
                    trayIcon.Visible = false;
                    System.Windows.Application.Current.Shutdown(); 
                });
                
                trayIcon.ContextMenuStrip = contextMenu;
                
                trayIcon.MouseClick += (s, e) => {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        PerformAutoChange();
                        trayIcon.ShowBalloonTip(1000, "Immich Wallpaper", "Refreshing wallpaper...", System.Windows.Forms.ToolTipIcon.None);
                    }
                };

                trayIcon.DoubleClick += (s, e) => { ShowWindow(); };
            }
            catch { }
        }

        private void OpenEditorFromCache()
        {
            if (!File.Exists(SourceImagePath))
            {
                System.Windows.MessageBox.Show("No original image found. Set a wallpaper first.", "Notice");
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(SourceImagePath))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                var editor = new EditorWindow(bitmap);
                if (editor.ShowDialog() == true)
                {
                    WallpaperHelper.SetWallpaper(editor.ResultPath, DesktopWallpaperPosition.Fill);
                    trayIcon?.ShowBalloonTip(2000, "Immich Wallpaper", "Wallpaper adjusted!", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to load editor: " + ex.Message);
            }
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (currentConfig?.AutoChangeEnabled == true)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon?.ShowBalloonTip(2000, "Immich Wallpaper", "Running in background", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                if (trayIcon != null) trayIcon.Visible = false;
            }
            base.OnClosing(e);
        }

        private void SetupAutoTimer()
        {
            autoTimer?.Stop();
            if (currentConfig?.AutoChangeEnabled == true)
            {
                autoTimer = new System.Windows.Threading.DispatcherTimer();
                autoTimer.Interval = TimeSpan.FromMinutes(currentConfig.AutoChangeIntervalMinutes);
                autoTimer.Tick += (s, e) => PerformAutoChange();
                autoTimer.Start();
                
                string timeStr = currentConfig.AutoChangeIntervalMinutes >= 60 
                    ? $"{currentConfig.AutoChangeIntervalMinutes / 60}h {currentConfig.AutoChangeIntervalMinutes % 60}m"
                    : $"{currentConfig.AutoChangeIntervalMinutes}m";
                StatusLabel.Text = $"Auto active (every {timeStr})";
            }
            else
            {
                StatusLabel.Text = "Auto-change disabled.";
            }
        }

        private async System.Threading.Tasks.Task<string?> GetGoogleAccessToken()
        {
            if (currentConfig == null || string.IsNullOrEmpty(currentConfig.GoogleRefreshToken)) return null;
            try
            {
                using HttpClient client = new HttpClient();
                var values = new Dictionary<string, string>
                {
                    { "client_id", currentConfig.GoogleClientId },
                    { "client_secret", currentConfig.GoogleClientSecret },
                    { "refresh_token", currentConfig.GoogleRefreshToken },
                    { "grant_type", "refresh_token" }
                };
                var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values));
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;
                dynamic result = JsonConvert.DeserializeObject(json)!;
                return (string)result.access_token;
            }
            catch { return null; }
        }

        private async void PerformAutoChange()
        {
            if (currentConfig == null) return;
            
            try
            {
                using HttpClient client = new HttpClient();
                if (currentConfig.Provider == PhotoProvider.Google)
                {
                    string? token = await GetGoogleAccessToken();
                    if (token == null) return;
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                else
                {
                    if (string.IsNullOrEmpty(currentConfig.BaseUrl)) return;
                    client.DefaultRequestHeaders.Add("x-api-key", currentConfig.ApiKey);
                }
                
                var rawAssets = new List<dynamic>();
                if (currentConfig.Provider == PhotoProvider.Google)
                {
                    HttpResponseMessage resp;
                    if (currentConfig.Mode == PhotoMode.Album && !string.IsNullOrEmpty(currentConfig.GoogleAlbumId))
                    {
                        var data = new { albumId = currentConfig.GoogleAlbumId, pageSize = 100 };
                        resp = await client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json"));
                    }
                    else if (currentConfig.Mode == PhotoMode.People && !string.IsNullOrEmpty(currentConfig.GoogleCategories))
                    {
                        var categories = currentConfig.GoogleCategories.Split(',').Select(s => s.Trim()).ToArray();
                        var data = new { filters = new { contentFilter = new { includedContentCategories = categories } }, pageSize = 100 };
                        resp = await client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json"));
                    }
                    else
                    {
                        resp = await client.GetAsync("https://photoslibrary.googleapis.com/v1/mediaItems?pageSize=100");
                    }
                    
                    if (resp.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                        if (result?.mediaItems != null) foreach (var item in result.mediaItems) rawAssets.Add(item);
                    }
                }
                else // Immich
                {
                    if (currentConfig.Mode == PhotoMode.People)
                    {
                        var allIds = currentConfig.ImmichPersonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                        
                        // Use OR logic by searching for each person individually and aggregating results
                        foreach (var id in allIds)
                        {
                            object searchData;
                            if (id.StartsWith("tag:")) searchData = new { tagIds = new[] { id.Replace("tag:", "") }, withArchived = false, size = 500 };
                            else searchData = new { personIds = new[] { id }, withArchived = false, size = 500 };

                            var resp = await client.PostAsync($"{currentConfig.BaseUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                            if (resp.IsSuccessStatusCode)
                            {
                                dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                                if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                            }
                        }
                    }
                    else if (currentConfig.Mode == PhotoMode.Album)
                    {
                        var resp = await client.GetAsync($"{currentConfig.BaseUrl}/api/albums/{currentConfig.ImmichAlbumId}");
                        if (resp.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                            if (result?.assets != null) foreach (var item in result.assets) rawAssets.Add(item);
                        }
                    }
                    else
                    {
                        var searchData = new { type = "IMAGE", size = 500, withArchived = false };
                        var resp = await client.PostAsync($"{currentConfig.BaseUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                        if (resp.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                            if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                        }
                    }
                }

                if (rawAssets.Count > 0)
                {
                    // Deduplicate results by ID
                    var uniqueAssets = rawAssets.GroupBy(a => (string)a.id).Select(g => g.First()).ToList();
                    
                    Random rnd = new Random();
                    var selected = uniqueAssets[rnd.Next(uniqueAssets.Count)];
                    string? downloadUrl = (currentConfig.Provider == PhotoProvider.Google) ? (string)selected.baseUrl + "=w2500-h2500" : $"{currentConfig.BaseUrl}/api/assets/{selected.id}/original";
                    
                    byte[] imageBytes = await client.GetByteArrayAsync(downloadUrl);
                    File.WriteAllBytes(SourceImagePath, imageBytes);
                    WallpaperHelper.SetWallpaper(SourceImagePath, DesktopWallpaperPosition.Fill);
                }
            }
            catch { }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Hide(); 
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfigOrPrompt();
            SetupAutoTimer();
        }

        private void LoadConfigOrPrompt()
        {
            currentConfig = ConfigManager.Load();
            if (currentConfig == null || (currentConfig.Provider == PhotoProvider.Immich && (string.IsNullOrEmpty(currentConfig.BaseUrl) || string.IsNullOrEmpty(currentConfig.ApiKey))) || (currentConfig.Provider == PhotoProvider.Google && string.IsNullOrEmpty(currentConfig.GoogleRefreshToken)))
            {
                var setup = new SetupWindow();
                if (setup.ShowDialog() == true)
                {
                    currentConfig = ConfigManager.Load();
                    SetupAutoTimer();
                }
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var setup = new SetupWindow();
            if (setup.ShowDialog() == true)
            {
                currentConfig = ConfigManager.Load();
                SetupAutoTimer();
                StatusLabel.Text = "Settings saved & Timer reset.";
            }
        }

        private async void FetchPhotos_Click(object sender, RoutedEventArgs e)
        {
            if (currentConfig == null) return;

            try
            {
                using HttpClient client = new HttpClient();
                if (currentConfig.Provider == PhotoProvider.Google)
                {
                    string? token = await GetGoogleAccessToken();
                    if (token == null) { System.Windows.MessageBox.Show("Please log in to Google Photos in Settings first."); return; }
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                else
                {
                    if (string.IsNullOrEmpty(currentConfig.BaseUrl)) { System.Windows.MessageBox.Show("Please configure Immich in Settings."); return; }
                    client.DefaultRequestHeaders.Add("x-api-key", currentConfig.ApiKey);
                }

                StatusLabel.Text = "Searching for photos...";
                var rawAssets = new List<dynamic>();

                if (currentConfig.Provider == PhotoProvider.Google)
                {
                    HttpResponseMessage resp;
                    if (currentConfig.Mode == PhotoMode.Album && !string.IsNullOrEmpty(currentConfig.GoogleAlbumId))
                    {
                        var data = new { albumId = currentConfig.GoogleAlbumId, pageSize = 100 };
                        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                        resp = await client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", content);
                    }
                    else if (currentConfig.Mode == PhotoMode.People && !string.IsNullOrEmpty(currentConfig.GoogleCategories))
                    {
                        var categories = currentConfig.GoogleCategories.Split(',').Select(s => s.Trim()).ToArray();
                        var data = new { filters = new { contentFilter = new { includedContentCategories = categories } }, pageSize = 100 };
                        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                        resp = await client.PostAsync("https://photoslibrary.googleapis.com/v1/mediaItems:search", content);
                    }
                    else
                    {
                        resp = await client.GetAsync("https://photoslibrary.googleapis.com/v1/mediaItems?pageSize=100");
                    }
                    
                    if (resp.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                        if (result?.mediaItems != null) foreach (var item in result.mediaItems) rawAssets.Add(item);
                    }
                }
                else // Immich
                {
                    if (currentConfig.Mode == PhotoMode.People)
                    {
                        var allIds = currentConfig.ImmichPersonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                        
                        foreach (var id in allIds)
                        {
                            object searchData;
                            if (id.StartsWith("tag:")) searchData = new { tagIds = new[] { id.Replace("tag:", "") }, withArchived = false, size = 1000 };
                            else searchData = new { personIds = new[] { id }, withArchived = false, size = 1000 };

                            var response = await client.PostAsync($"{currentConfig.BaseUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                            if (response.IsSuccessStatusCode)
                            {
                                dynamic result = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync())!;
                                if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                            }
                        }
                    }
                    else if (currentConfig.Mode == PhotoMode.Album)
                    {
                        var response = await client.GetAsync($"{currentConfig.BaseUrl}/api/albums/{currentConfig.ImmichAlbumId}");
                        if (response.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync())!;
                            if (result?.assets != null) foreach (var item in result.assets) rawAssets.Add(item);
                        }
                    }
                    else
                    {
                        var searchData = new { type = "IMAGE", size = 1000, withArchived = false };
                        var response = await client.PostAsync($"{currentConfig.BaseUrl}/api/search/metadata", new StringContent(JsonConvert.SerializeObject(searchData), Encoding.UTF8, "application/json"));
                        if (response.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync())!;
                            if (result?.assets?.items != null) foreach (var item in result.assets.items) rawAssets.Add(item);
                        }
                    }
                }

                if (rawAssets.Count == 0) { StatusLabel.Text = "No photos found."; return; }

                // Deduplicate and Pick 4 random
                Random rnd = new Random();
                var selectedAssets = rawAssets
                    .GroupBy(p => (string)p.id) 
                    .Select(g => g.First())
                    .OrderBy(x => rnd.Next())
                    .Take(4)
                    .ToList();

                var displayList = new List<ImmichPhoto>();
                foreach (var asset in selectedAssets)
                {
                    StatusLabel.Text = $"Loading preview {displayList.Count + 1}/4...";
                    string thumbUrl = (currentConfig.Provider == PhotoProvider.Google) ? (string)asset.baseUrl + "=w500-h500" : $"{currentConfig.BaseUrl}/api/assets/{asset.id}/thumbnail";
                    byte[] thumbData = await client.GetByteArrayAsync(thumbUrl);

                    var bitmap = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream(thumbData)) { bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = ms; bitmap.EndInit(); }
                    bitmap.Freeze();
                    displayList.Add(new ImmichPhoto { Id = (string)asset.id, Thumbnail = bitmap });
                }

                ImageGallery.ItemsSource = displayList;
                StatusLabel.Text = $"Found {rawAssets.Count} unique candidates. Loaded 4.";
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Fetch Error: " + ex.Message); }
        }

        private void ImageGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetBtn.IsEnabled = ImageGallery.SelectedItem != null;
            AdjustBtn.IsEnabled = ImageGallery.SelectedItem != null;
        }

        private async void Adjust_Click(object sender, RoutedEventArgs e)
        {
            if (ImageGallery.SelectedItem is ImmichPhoto selected && currentConfig != null)
            {
                try
                {
                    StatusLabel.Text = "Downloading for editor...";
                    using HttpClient client = new HttpClient();
                    if (currentConfig.Provider == PhotoProvider.Google)
                    {
                        string? token = await GetGoogleAccessToken();
                        if (token == null) return;
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                    else
                    {
                        client.DefaultRequestHeaders.Add("x-api-key", currentConfig.ApiKey);
                    }

                    string? downloadUrl = (currentConfig.Provider == PhotoProvider.Google) 
                        ? (await client.GetAsync($"https://photoslibrary.googleapis.com/v1/mediaItems/{selected.Id}")
                            .ContinueWith(async t => {
                                var json = await (await t).Content.ReadAsStringAsync();
                                dynamic res = JsonConvert.DeserializeObject(json)!;
                                return (string)res.baseUrl + "=w2500-h2500";
                            })).Result 
                        : $"{currentConfig.BaseUrl}/api/assets/{selected.Id}/original";

                    byte[] imageBytes = await client.GetByteArrayAsync(downloadUrl);
                    File.WriteAllBytes(SourceImagePath, imageBytes);

                    var bitmap = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream(imageBytes)) { bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = ms; bitmap.EndInit(); }
                    bitmap.Freeze();

                    var editor = new EditorWindow(bitmap);
                    if (editor.ShowDialog() == true)
                    {
                        WallpaperHelper.SetWallpaper(editor.ResultPath, DesktopWallpaperPosition.Fill);
                        StatusLabel.Text = "Adjusted Wallpaper Updated!";
                    }
                }
                catch (Exception ex) { System.Windows.MessageBox.Show("Editor Error: " + ex.Message); }
            }
        }

        private async void SetWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (ImageGallery.SelectedItem is ImmichPhoto selected && currentConfig != null)
            {
                try
                {
                    StatusLabel.Text = "Applying high-res wallpaper...";
                    using HttpClient client = new HttpClient();
                    if (currentConfig.Provider == PhotoProvider.Google)
                    {
                        string? token = await GetGoogleAccessToken();
                        if (token == null) return;
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                    else
                    {
                        client.DefaultRequestHeaders.Add("x-api-key", currentConfig.ApiKey);
                    }

                    string? downloadUrl = (currentConfig.Provider == PhotoProvider.Google) 
                        ? (await client.GetAsync($"https://photoslibrary.googleapis.com/v1/mediaItems/{selected.Id}")
                            .ContinueWith(async t => {
                                var json = await (await t).Content.ReadAsStringAsync();
                                dynamic res = JsonConvert.DeserializeObject(json)!;
                                return (string)res.baseUrl + "=w2500-h2500";
                            })).Result 
                        : $"{currentConfig.BaseUrl}/api/assets/{selected.Id}/original";

                    byte[] imageBytes = await client.GetByteArrayAsync(downloadUrl);
                    File.WriteAllBytes(SourceImagePath, imageBytes);

                    DesktopWallpaperPosition pos = DesktopWallpaperPosition.Fill;
                    if (RbFit.IsChecked == true) pos = DesktopWallpaperPosition.Fit;
                    else if (RbStretch.IsChecked == true) pos = DesktopWallpaperPosition.Stretch;
                    else if (RbTile.IsChecked == true) pos = DesktopWallpaperPosition.Tile;
                    else if (RbCenter.IsChecked == true) pos = DesktopWallpaperPosition.Center;
                    else if (RbSpan.IsChecked == true) pos = DesktopWallpaperPosition.Span;

                    WallpaperHelper.SetWallpaper(SourceImagePath, pos);
                    StatusLabel.Text = "Wallpaper Updated!";
                }
                catch (Exception ex) { System.Windows.MessageBox.Show("Set Error: " + ex.Message); }
            }
        }
    }
}