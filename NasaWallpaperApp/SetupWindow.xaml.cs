using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Diagnostics;

namespace NasaWallpaperApp
{
    public partial class SetupWindow : Window
    {
        private string? _tempGoogleRefreshToken;

        public SetupWindow()
        {
            InitializeComponent();
            PopulateTimeCombos();
            LoadSettings();
        }

        private void PopulateTimeCombos()
        {
            for (int i = 0; i < 24; i++) HourCombo.Items.Add(i.ToString());
            int[] mins = { 0, 1, 5, 10, 15, 30, 45 };
            foreach (int m in mins) MinuteCombo.Items.Add(m.ToString());
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LoadSettings()
        {
            var config = ConfigManager.Load();
            if (config != null)
            {
                UrlInput.Text = config.BaseUrl;
                FallbackUrlInput.Text = config.FallbackBaseUrl;
                LocalPathText.Text = string.IsNullOrEmpty(config.LocalFolderPath) ? "No folder selected" : config.LocalFolderPath;
                KeyInput.Password = config.ApiKey;
                AutoChangeCheck.IsChecked = config.AutoChangeEnabled;
                MinimizeToTrayCheck.IsChecked = config.MinimizeToTrayOnClose;
                
                int h = config.AutoChangeIntervalMinutes / 60;
                int m = config.AutoChangeIntervalMinutes % 60;
                HourCombo.SelectedItem = h.ToString();
                if (!MinuteCombo.Items.Contains(m.ToString())) MinuteCombo.Items.Add(m.ToString());
                MinuteCombo.SelectedItem = m.ToString();
                
                GoogleClientIdInput.Text = config.GoogleClientId;
                GoogleClientSecretInput.Password = config.GoogleClientSecret;
                _tempGoogleRefreshToken = config.GoogleRefreshToken;
                
                if (!string.IsNullOrEmpty(_tempGoogleRefreshToken)) 
                {
                    GoogleStatus.Text = "Authenticated ✓";
                    GoogleFetchBtn.Visibility = Visibility.Visible;
                }

                if (config.Provider == PhotoProvider.Google) RbProvGoogle.IsChecked = true;
                else if (config.Provider == PhotoProvider.Local) RbProvLocal.IsChecked = true;
                else RbProvImmich.IsChecked = true;

                switch (config.Mode)
                {
                    case PhotoMode.People: RbModePeople.IsChecked = true; break;
                    case PhotoMode.Album: RbModeAlbum.IsChecked = true; break;
                    case PhotoMode.Random: RbModeRandom.IsChecked = true; break;
                }

                RestoreLists(config);
            }
            else
            {
                RbProvImmich.IsChecked = true;
                RbModePeople.IsChecked = true;
                HourCombo.SelectedIndex = 4;
                MinuteCombo.SelectedIndex = 0;
            }
            UpdateVisibility();
        }

        private void RestoreLists(AppConfig config)
        {
            if (config.Provider == PhotoProvider.Immich)
            {
                if (!string.IsNullOrEmpty(config.ImmichPersonIds))
                {
                    var list = config.ImmichPersonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => new SelectableItem { Id = id.Trim(), Name = $"ID: {id.Trim()}" }).ToList();
                    PeopleList.ItemsSource = list;
                    foreach(var item in list) PeopleList.SelectedItems.Add(item);
                }
                if (!string.IsNullOrEmpty(config.ImmichAlbumId))
                {
                    var list = new List<SelectableItem> { new SelectableItem { Id = config.ImmichAlbumId, Name = "Saved Album" } };
                    AlbumList.ItemsSource = list;
                    AlbumList.SelectedIndex = 0;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(config.GoogleCategories))
                {
                    var list = config.GoogleCategories.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => new SelectableItem { Id = id.Trim(), Name = id.Trim() }).ToList();
                    PeopleList.ItemsSource = list;
                    foreach(var item in list) PeopleList.SelectedItems.Add(item);
                }
                if (!string.IsNullOrEmpty(config.GoogleAlbumId))
                {
                    var list = new List<SelectableItem> { new SelectableItem { Id = config.GoogleAlbumId, Name = "Saved Album" } };
                    AlbumList.ItemsSource = list;
                    AlbumList.SelectedIndex = 0;
                }
            }
        }

        private void Provider_Checked(object sender, RoutedEventArgs e)
        {
            UpdateVisibility();
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (PeopleList == null || ModeHint == null) return; 

            ImmichServerPanel.Visibility = RbProvImmich.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            GoogleServerPanel.Visibility = RbProvGoogle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LocalServerPanel.Visibility = RbProvLocal.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            PeopleList.Visibility = Visibility.Collapsed;
            AlbumList.Visibility = Visibility.Collapsed;
            RandomPanel.Visibility = Visibility.Collapsed;
            
            if (RbProvLocal.IsChecked == true)
            {
                ModeHint.Text = "All photos in the selected folder will be used.";
                return;
            }

            bool isGoogle = RbProvGoogle.IsChecked == true;

            if (RbModePeople.IsChecked == true) 
            {
                PeopleList.Visibility = Visibility.Visible;
                ModeHint.Text = isGoogle 
                    ? "Google uses smart categories (People, Pets, etc.) instead of individual names." 
                    : "Select specific faces recognized by Immich.";
            }
            else if (RbModeAlbum.IsChecked == true) 
            {
                AlbumList.Visibility = Visibility.Visible;
                ModeHint.Text = "Select a specific album to use as your wallpaper source.";
            }
            else if (RbModeRandom.IsChecked == true) 
            {
                RandomPanel.Visibility = Visibility.Visible;
                ModeHint.Text = "Cycle through your entire photo library at random.";
            }

            RandomText.Text = isGoogle ? "Shuffle Google Library" : "Shuffle Immich Library";
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (RbProvImmich.IsChecked == true) await ConnectImmich();
            else await ConnectGoogle();
        }

        private async System.Threading.Tasks.Task ConnectImmich()
        {
            string url = UrlInput.Text.Trim();
            string key = KeyInput.Password.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (url.EndsWith("/")) url = url.TrimEnd('/');

            ConnectionStatus.Text = "Connecting to Immich...";
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("x-api-key", key);
                
                var pResp = await client.GetAsync($"{url}/api/people");
                if (!pResp.IsSuccessStatusCode) pResp = await client.GetAsync($"{url}/api/person");
                var allPeopleAndTags = new List<SelectableItem>();
                int namedCount = 0;
                if (pResp.IsSuccessStatusCode)
                {
                    dynamic res = JsonConvert.DeserializeObject(await pResp.Content.ReadAsStringAsync())!;
                    var items = (res is Newtonsoft.Json.Linq.JArray) ? res : (res?.items ?? res?.people);
                    if (items != null) 
                    {
                        foreach (var p in items) 
                        {
                            string personName = (string)p.name;
                            if (string.IsNullOrWhiteSpace(personName)) personName = "Unnamed Person";
                            else namedCount++;

                            allPeopleAndTags.Add(new SelectableItem { Id = (string)p.id, Name = personName });
                        }
                    }
                }

                // ... tags fetching ...
                var tResp = await client.GetAsync($"{url}/api/tag");
                if (tResp.IsSuccessStatusCode)
                {
                    dynamic tags = JsonConvert.DeserializeObject(await tResp.Content.ReadAsStringAsync())!;
                    if (tags != null) 
                    {
                        foreach (var t in tags) 
                            allPeopleAndTags.Add(new SelectableItem { Id = "tag:" + (string)t.id, Name = "🏷 " + (string)t.name });
                    }
                }

                var sorted = allPeopleAndTags
                    .OrderBy(x => x.Id.StartsWith("tag:") ? 0 : (x.Name == "Unnamed Person" ? 2 : 1))
                    .ThenBy(x => x.Name)
                    .ToList();

                PeopleList.ItemsSource = sorted;
                _ = LoadThumbnails(sorted.Where(x => !x.Id.StartsWith("tag:")).ToList(), url, key, true);
                
                ConnectionStatus.Text = $"Immich Connected! Found {namedCount} named people.";

                var aResp = await client.GetAsync($"{url}/api/albums");
                if (!aResp.IsSuccessStatusCode) aResp = await client.GetAsync($"{url}/api/album");
                if (aResp.IsSuccessStatusCode)
                {
                    dynamic res = JsonConvert.DeserializeObject(await aResp.Content.ReadAsStringAsync())!;
                    var items = (res is Newtonsoft.Json.Linq.JArray) ? res : res?.items;
                    var list = new List<SelectableItem>();
                    if (items != null) foreach (var a in items) list.Add(new SelectableItem { Id = (string)a.id, Name = (string)a.albumName ?? "Untitled", AssetId = (string)a.albumThumbnailAssetId });
                    AlbumList.ItemsSource = list.OrderBy(x => x.Name).ToList();
                    _ = LoadThumbnails(list, url, key, false);
                }
                ConnectionStatus.Text = "Immich Connected!";
            }
            catch (Exception ex) { ConnectionStatus.Text = "Error: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task ConnectGoogle()
        {
            if (string.IsNullOrEmpty(_tempGoogleRefreshToken))
            {
                await GoogleLoginFlow();
                if (string.IsNullOrEmpty(_tempGoogleRefreshToken)) return;
            }

            GoogleStatus.Text = "Connecting to Google Photos...";
            try
            {
                string? token = await GetGoogleAccessTokenInternal();
                if (token == null) { GoogleStatus.Text = "Token refresh failed."; return; }

                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var categories = new List<string> { "PEOPLE", "PETS", "LANDSCAPES", "CITYSCAPES", "TRAVEL", "FOOD", "BIRTHDAYS" };
                PeopleList.ItemsSource = categories.Select(c => new SelectableItem { Id = c, Name = c }).ToList();

                GoogleStatus.Text = "Fetching albums...";
                var albumList = new List<SelectableItem>();

                var resp = await client.GetAsync("https://photoslibrary.googleapis.com/v1/albums?pageSize=50");
                if (resp.IsSuccessStatusCode)
                {
                    dynamic res = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
                    if (res?.albums != null)
                    {
                        foreach (var a in res.albums) 
                            albumList.Add(new SelectableItem { Id = (string)a.id, Name = (string)a.title, ThumbnailUrl = (string)a.coverPhotoBaseUrl + "=w150-h150" });
                    }
                }
                else
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    System.Windows.MessageBox.Show("Album Fetch Error: " + err);
                }

                var sharedResp = await client.GetAsync("https://photoslibrary.googleapis.com/v1/sharedAlbums?pageSize=50");
                if (sharedResp.IsSuccessStatusCode)
                {
                    dynamic res = JsonConvert.DeserializeObject(await sharedResp.Content.ReadAsStringAsync())!;
                    if (res?.sharedAlbums != null)
                    {
                        foreach (var a in res.sharedAlbums)
                            albumList.Add(new SelectableItem { Id = (string)a.id, Name = "[Shared] " + (string)a.title, ThumbnailUrl = (string)a.coverPhotoBaseUrl + "=w150-h150" });
                    }
                }

                AlbumList.ItemsSource = albumList.OrderBy(x => x.Name).ToList();
                foreach(var item in albumList) _ = LoadWebThumbnail(item);
                
                GoogleStatus.Text = $"Connected! {albumList.Count} albums found.";
            }
            catch (Exception ex) { GoogleStatus.Text = "Google API Error: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task<string?> GetGoogleAccessTokenInternal()
        {
            string clientId = GoogleClientIdInput.Text.Trim();
            string clientSecret = GoogleClientSecretInput.Password.Trim();
            if (string.IsNullOrEmpty(_tempGoogleRefreshToken)) return null;

            using HttpClient client = new HttpClient();
            var values = new Dictionary<string, string> { {"client_id", clientId}, {"client_secret", clientSecret}, {"refresh_token", _tempGoogleRefreshToken}, {"grant_type", "refresh_token"} };
            var resp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values));
            if (!resp.IsSuccessStatusCode) return null;
            dynamic res = JsonConvert.DeserializeObject(await resp.Content.ReadAsStringAsync())!;
            return (string)res.access_token;
        }

        private async System.Threading.Tasks.Task GoogleLoginFlow()
        {
            string clientId = GoogleClientIdInput.Text.Trim();
            string clientSecret = GoogleClientSecretInput.Password.Trim();
            if (string.IsNullOrEmpty(clientId)) { System.Windows.MessageBox.Show("Enter Client ID and Secret first."); return; }

            // Reset status
            _tempGoogleRefreshToken = null;
            GoogleStatus.Text = "Opening browser...";

            try
            {
                string redirectUri = "http://localhost:5000/";
                var listener = new HttpListener();
                listener.Prefixes.Add(redirectUri);
                listener.Start();

                // Use the absolute broadest scope and force the consent prompt
                string scope = Uri.EscapeDataString("https://www.googleapis.com/auth/photoslibrary.readonly");
                string encodedRedirect = Uri.EscapeDataString(redirectUri);
                
                // prompt=consent forces Google to show the checkboxes even if already logged in
                string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={clientId}&redirect_uri={encodedRedirect}&response_type=code&scope={scope}&access_type=offline&prompt=consent";
                
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                var context = await listener.GetContextAsync();
                string code = context.Request.QueryString["code"] ?? "";
                
                string responseString = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'><h1 style='color:green'>Success!</h1><p>You can close this tab and return to the application.</p></body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
                listener.Stop();

                using HttpClient client = new HttpClient();
                var values = new Dictionary<string, string> { {"code", code}, {"client_id", clientId}, {"client_secret", clientSecret}, {"redirect_uri", redirectUri}, {"grant_type", "authorization_code"} };
                var resp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values));
                string body = await resp.Content.ReadAsStringAsync();
                dynamic res = JsonConvert.DeserializeObject(body)!;
                
                if (res.refresh_token != null)
                {
                    _tempGoogleRefreshToken = (string)res.refresh_token;
                    GoogleStatus.Text = "Authenticated ✓";
                    GoogleFetchBtn.Visibility = Visibility.Visible;
                    await ConnectGoogle();
                }
                else
                {
                    System.Windows.MessageBox.Show("Google did not return a refresh token. This usually happens if you didn't check the 'Photos' permission box. Please try again.");
                    GoogleStatus.Text = "Permissions Missing";
                }
            }
            catch (Exception ex) { GoogleStatus.Text = "Auth Error: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task LoadThumbnails(List<SelectableItem> items, string baseUrl, string apiKey, bool isPerson)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            foreach (var item in items)
            {
                try
                {
                    string url = isPerson ? $"{baseUrl}/api/people/{item.Id}/thumbnail" : $"{baseUrl}/api/assets/{item.AssetId}/thumbnail";
                    byte[] data = await client.GetByteArrayAsync(url);
                    await Dispatcher.InvokeAsync(() => {
                        var bmp = new BitmapImage();
                        using (var ms = new MemoryStream(data)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); }
                        bmp.Freeze(); item.Thumbnail = bmp;
                    });
                } catch { }
            }
        }

        private async System.Threading.Tasks.Task LoadWebThumbnail(SelectableItem item)
        {
            if (string.IsNullOrEmpty(item.ThumbnailUrl)) return;
            try
            {
                using HttpClient client = new HttpClient();
                byte[] data = await client.GetByteArrayAsync(item.ThumbnailUrl);
                await Dispatcher.InvokeAsync(() => {
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(data)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); }
                    bmp.Freeze(); item.Thumbnail = bmp;
                });
            } catch { }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LocalPathText.Text = dialog.SelectedPath;
            }
        }

        private void SaveAutomation_Click(object sender, RoutedEventArgs e)
        {
            int h = int.Parse(HourCombo.SelectedItem?.ToString() ?? "0");
            int m = int.Parse(MinuteCombo.SelectedItem?.ToString() ?? "0");
            int total = (h * 60) + m;
            var c = ConfigManager.Load() ?? new AppConfig();
            c.AutoChangeEnabled = AutoChangeCheck.IsChecked == true;
            c.MinimizeToTrayOnClose = MinimizeToTrayCheck.IsChecked == true;
            c.AutoChangeIntervalMinutes = total > 0 ? total : 1;
            ConfigManager.Save(c);
            System.Windows.MessageBox.Show("Automation updated!");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var c = ConfigManager.Load() ?? new AppConfig();
            c.Provider = RbProvGoogle.IsChecked == true ? PhotoProvider.Google : (RbProvLocal.IsChecked == true ? PhotoProvider.Local : PhotoProvider.Immich);
            c.Mode = RbModeAlbum.IsChecked == true ? PhotoMode.Album : (RbModeRandom.IsChecked == true ? PhotoMode.Random : PhotoMode.People);
            
            c.BaseUrl = UrlInput.Text.Trim();
            c.FallbackBaseUrl = FallbackUrlInput.Text.Trim();
            c.LocalFolderPath = LocalPathText.Text == "No folder selected" ? "" : LocalPathText.Text;
            c.ApiKey = KeyInput.Password.Trim();
            c.GoogleClientId = GoogleClientIdInput.Text.Trim();
            c.GoogleClientSecret = GoogleClientSecretInput.Password.Trim();
            c.GoogleRefreshToken = _tempGoogleRefreshToken ?? "";

            if (c.Provider == PhotoProvider.Immich)
            {
                if (PeopleList.ItemsSource != null) c.ImmichPersonIds = string.Join(",", PeopleList.SelectedItems.Cast<SelectableItem>().Select(x => x.Id));
                if (AlbumList.SelectedItem is SelectableItem alb) c.ImmichAlbumId = alb.Id;
            }
            else
            {
                if (PeopleList.ItemsSource != null) c.GoogleCategories = string.Join(",", PeopleList.SelectedItems.Cast<SelectableItem>().Select(x => x.Id));
                if (AlbumList.SelectedItem is SelectableItem alb) c.GoogleAlbumId = alb.Id;
            }

            ConfigManager.Save(c);
            this.DialogResult = true;
            this.Close();
        }

        private void ResetApp_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Delete all data?", "Reset", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NasaWallpaperApp");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void GoogleLogin_Click(object sender, RoutedEventArgs e) { _ = GoogleLoginFlow(); }
    }
}