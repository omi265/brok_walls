using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace BroKWalls
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;
        private MainWindow? _mainWindow;
        private WallpaperService _wallpaperService = new WallpaperService();
        private System.Threading.Timer? _timer;
        private AppConfig? _config;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure app doesn't close when MainWindow closes
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = ConfigManager.Load();

            SetupTrayIcon();
            SetupTimer();
            
            // Optional: Run one update immediately if enabled
            if (_config?.AutoChangeEnabled == true)
            {
                Task.Run(() => _wallpaperService.PerformAutoChangeAsync());
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            // Use the icon from the exe
            _trayIcon.Icon = Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
            _trayIcon.Visible = true;
            _trayIcon.Text = "Bro-k Walls";

            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        }

        private void ShowTrayMenu()
        {
            var menu = new TrayMenuWindow();
            menu.OpenRequested += ShowMainWindow;
            menu.RefreshRequested += async () => 
            {
                _trayIcon?.ShowBalloonTip(1000, "Bro-k Walls", "Refreshing wallpaper...", ToolTipIcon.None);
                await _wallpaperService.PerformAutoChangeAsync();
            };
            menu.AdjustRequested += () => 
            {
                ShowMainWindow();
                _mainWindow?.OpenEditorForCurrentWallpaper();
            };
            menu.ExitRequested += () => Shutdown();
            menu.Show();
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (s, e) => _mainWindow = null; // Dispose reference on close
            }
            _mainWindow.Show();
            _mainWindow.Activate();
        }

        private void SetupTimer()
        {
            _timer?.Dispose();
            if (_config?.AutoChangeEnabled == true)
            {
                // Convert minutes to milliseconds
                long period = (long)TimeSpan.FromMinutes(_config.AutoChangeIntervalMinutes).TotalMilliseconds;
                // Start after period, repeat every period
                _timer = new System.Threading.Timer(TimerCallback, null, period, period);
            }
        }

        private async void TimerCallback(object? state)
        {
            await _wallpaperService.PerformAutoChangeAsync();
        }
        
        // Public method for MainWindow to request timer reset/update
        public void UpdateConfigAndTimer()
        {
            _config = ConfigManager.Load();
            SetupTimer();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _timer?.Dispose();
            base.OnExit(e);
        }
    }
}