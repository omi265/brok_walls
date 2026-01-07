using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BroKWalls
{
    public partial class MainWindow : Window
    {
        private AppConfig? currentConfig;
        private WallpaperService _service = new WallpaperService();

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfigOrPrompt();
            UpdateStatusLabel();
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
                    ((App)System.Windows.Application.Current).UpdateConfigAndTimer();
                    UpdateStatusLabel();
                }
            }
        }
        
        private void UpdateStatusLabel()
        {
            if (currentConfig?.AutoChangeEnabled == true)
            {
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

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            // Close the window instead of hiding to free resources. 
            // App.xaml.cs handles keeping the process alive.
            this.Close(); 
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var setup = new SetupWindow();
            if (setup.ShowDialog() == true)
            {
                currentConfig = ConfigManager.Load();
                ((App)System.Windows.Application.Current).UpdateConfigAndTimer();
                UpdateStatusLabel();
            }
        }

        private async void FetchPhotos_Click(object sender, RoutedEventArgs e)
        {
            if (currentConfig == null) return;
            
            try
            {
                StatusLabel.Text = "Searching for photos...";
                var photos = await _service.FetchGalleryPhotos(currentConfig, 4);

                if (photos.Count == 0)
                {
                    StatusLabel.Text = "No photos found.";
                    return;
                }
                
                // Convert bytes to BitmapSource for UI
                foreach(var p in photos)
                {
                    if (p.ThumbnailBytes != null)
                    {
                        var bitmap = new BitmapImage();
                        using (var ms = new MemoryStream(p.ThumbnailBytes)) 
                        { 
                            bitmap.BeginInit(); 
                            bitmap.CacheOption = BitmapCacheOption.OnLoad; 
                            bitmap.StreamSource = ms; 
                            bitmap.EndInit(); 
                        }
                        bitmap.Freeze();
                        p.Thumbnail = bitmap;
                    }
                    else if (p.Id != null && File.Exists(p.Id)) // Local file
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(p.Id);
                        bitmap.DecodePixelWidth = 300;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        p.Thumbnail = bitmap;
                    }
                }

                ImageGallery.ItemsSource = photos;
                StatusLabel.Text = $"Loaded {photos.Count} photos.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Fetch Error: " + ex.Message);
            }
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
                    StatusLabel.Text = "Preparing editor...";
                    
                    if (currentConfig.Provider == PhotoProvider.Local)
                    {
                         File.Copy(selected.Id!, WallpaperService.SourceImagePath, true);
                    }
                    else
                    {
                        byte[] originalBytes = await _service.DownloadOriginal(currentConfig, selected.Id!);
                        await File.WriteAllBytesAsync(WallpaperService.SourceImagePath, originalBytes);
                    }
                    
                    OpenEditorForCurrentWallpaper();
                }
                catch (Exception ex) { System.Windows.MessageBox.Show("Editor Error: " + ex.Message); }
            }
        }
        
        public void OpenEditorForCurrentWallpaper()
        {
            if (!File.Exists(WallpaperService.SourceImagePath))
            {
                 System.Windows.MessageBox.Show("No active wallpaper found to edit.", "Notice");
                 return;
            }

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(WallpaperService.SourceImagePath))
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
                    StatusLabel.Text = "Adjusted Wallpaper Updated!";
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to load editor: " + ex.Message);
            }
        }

        private async void SetWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (ImageGallery.SelectedItem is ImmichPhoto selected && currentConfig != null)
            {
                try
                {
                    if (currentConfig.Provider == PhotoProvider.Local)
                    {
                         File.Copy(selected.Id!, WallpaperService.SourceImagePath, true);
                    }
                    else
                    {
                        StatusLabel.Text = "Downloading high-res...";
                        byte[] originalBytes = await _service.DownloadOriginal(currentConfig, selected.Id!);
                        await File.WriteAllBytesAsync(WallpaperService.SourceImagePath, originalBytes);
                    }

                    DesktopWallpaperPosition pos = DesktopWallpaperPosition.Fill;
                    if (RbFit.IsChecked == true) pos = DesktopWallpaperPosition.Fit;
                    else if (RbStretch.IsChecked == true) pos = DesktopWallpaperPosition.Stretch;
                    else if (RbTile.IsChecked == true) pos = DesktopWallpaperPosition.Tile;
                    else if (RbCenter.IsChecked == true) pos = DesktopWallpaperPosition.Center;
                    else if (RbSpan.IsChecked == true) pos = DesktopWallpaperPosition.Span;

                    WallpaperHelper.SetWallpaper(WallpaperService.SourceImagePath, pos);
                    StatusLabel.Text = "Wallpaper Updated!";
                }
                catch (Exception ex) { System.Windows.MessageBox.Show("Set Error: " + ex.Message); }
            }
        }
    }
}