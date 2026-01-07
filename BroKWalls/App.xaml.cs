using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private Icon? _originalIcon;
        private bool _isRefreshing = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _config = ConfigManager.Load();

            SetupTrayIcon();
            SetupTimer();

            ShowMainWindow();
            
            if (_config?.AutoChangeEnabled == true)
            {
                _ = PerformRefreshWithFeedback();
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            _originalIcon = Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");
            _trayIcon.Icon = _originalIcon;
            _trayIcon.Visible = true;
            _trayIcon.Text = "Bro-k Walls";

            _trayIcon.MouseClick += TrayIcon_MouseClick;
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _ = PerformRefreshWithFeedback();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        }

        private async Task PerformRefreshWithFeedback()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            _trayIcon?.ShowBalloonTip(1000, "Bro-k Walls", "Refreshing wallpaper...", ToolTipIcon.None);
            
            using var cts = new CancellationTokenSource();
            var animationTask = StartLoadingAnimation(cts.Token);

            try
            {
                await _wallpaperService.PerformAutoChangeAsync();
            }
            finally
            {
                cts.Cancel();
                await animationTask;
                _isRefreshing = false;
                if (_trayIcon != null) _trayIcon.Icon = _originalIcon;
            }
        }

        private async Task StartLoadingAnimation(CancellationToken token)
        {
            int angle = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var bitmap = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        
                        // Draw a spinning arc
                        using var pen = new Pen(Color.FromArgb(76, 194, 255), 4); // Accent color #4CC2FF
                        g.DrawArc(pen, 4, 4, 24, 24, angle, 270);
                    }

                    var hIcon = bitmap.GetHicon();
                    using var newIcon = Icon.FromHandle(hIcon);
                    
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        if (_trayIcon != null && !token.IsCancellationRequested)
                            _trayIcon.Icon = (Icon)newIcon.Clone();
                    });

                    // Cleanup hIcon to prevent memory leak
                    DestroyIcon(hIcon);
                }
                catch { }

                angle = (angle + 30) % 360;
                await Task.Delay(100);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private void ShowTrayMenu()
        {
            var menu = new TrayMenuWindow();
            menu.OpenRequested += ShowMainWindow;
            menu.RefreshRequested += () => _ = PerformRefreshWithFeedback();
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
                _mainWindow.Closed += (s, e) => _mainWindow = null;
            }
            _mainWindow.Show();
            _mainWindow.Activate();
        }

        private void SetupTimer()
        {
            _timer?.Dispose();
            if (_config?.AutoChangeEnabled == true)
            {
                long period = (long)TimeSpan.FromMinutes(_config.AutoChangeIntervalMinutes).TotalMilliseconds;
                _timer = new System.Threading.Timer(TimerCallback, null, period, period);
            }
        }

        private void TimerCallback(object? state)
        {
            _ = PerformRefreshWithFeedback();
        }
        
        public void UpdateConfigAndTimer()
        {
            _config = ConfigManager.Load();
            SetupTimer();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _timer?.Dispose();
            _originalIcon?.Dispose();
            base.OnExit(e);
        }
    }
}