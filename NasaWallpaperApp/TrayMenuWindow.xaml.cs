using System;
using System.Windows;

namespace NasaWallpaperApp
{
    public partial class TrayMenuWindow : Window
    {
        private readonly MainWindow _main;

        public TrayMenuWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            this.Loaded += TrayMenuWindow_Loaded;
        }

        private void TrayMenuWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var mouse = System.Windows.Forms.Cursor.Position;
                
                // Get WPF scale factor (DPI)
                PresentationSource source = PresentationSource.FromVisual(this);
                double scaleX = 1.0, scaleY = 1.0;
                if (source != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                // Convert screen pixels to WPF units
                double mouseX = mouse.X / scaleX;
                double mouseY = mouse.Y / scaleY;

                // Position bottom-right of menu at mouse cursor (assuming bottom-right tray)
                this.Left = mouseX - this.ActualWidth;
                this.Top = mouseY - this.ActualHeight - 10;

                // Simple bounds check: if it goes off-screen top/left, flip it
                if (this.Top < 0) this.Top = mouseY + 10;
                if (this.Left < 0) this.Left = mouseX + 10;
            }
            catch
            {
                // Fallback
                this.Left = 100;
                this.Top = 100;
            }
            
            this.Activate();
            this.Focus();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            _main.ShowFromTray();
            this.Close();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _main.RefreshFromTray();
            this.Close();
        }

        private void Adjust_Click(object sender, RoutedEventArgs e)
        {
            _main.AdjustFromTray();
            this.Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _main.ExitFromTray();
            this.Close();
        }
    }
}