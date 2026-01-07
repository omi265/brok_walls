using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;

namespace BroKWalls
{
    public partial class EditorWindow : Window
    {
        private System.Windows.Point _start;
        private System.Windows.Point _origin;
        private bool _isDragging;
        
        private double _currentRotation = 0;
        private bool _isInternalUpdate = false;

        private const double SnapThreshold = 25.0;

        public string ResultPath { get; private set; } = "";

        public EditorWindow(BitmapSource image)
        {
            InitializeComponent();
            
            // Detect the maximum resolution among all connected monitors
            // This ensures the crop is high-quality enough for the largest screen
            double maxW = 0;
            double maxH = 0;
            
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                // We use Bounds which gives us the actual pixel dimensions
                maxW = Math.Max(maxW, screen.Bounds.Width);
                maxH = Math.Max(maxH, screen.Bounds.Height);
            }

            // Fallback to primary if something goes wrong
            if (maxW == 0) maxW = SystemParameters.PrimaryScreenWidth;
            if (maxH == 0) maxH = SystemParameters.PrimaryScreenHeight;

            ScreenCanvas.Width = maxW;
            ScreenCanvas.Height = maxH;
            
            // Adjust guideline coordinates for the new resolution
            GuideH.Y1 = GuideH.Y2 = ScreenCanvas.Height / 2;
            GuideH.X2 = ScreenCanvas.Width;
            GuideV.X1 = GuideV.X2 = ScreenCanvas.Width / 2;
            GuideV.Y2 = ScreenCanvas.Height;

            EditImage.Source = image;

            // Default to "Fill" view on open
            RecalculateFitFill(fill: true);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void RecalculateFitFill(bool fill)
        {
            if (EditImage.Source is not BitmapSource img) return;

            bool isRotated = (Math.Abs(_currentRotation) % 180) == 90;
            double imgW = isRotated ? img.PixelHeight : img.PixelWidth;
            double imgH = isRotated ? img.PixelWidth : img.PixelHeight;

            double scaleX = ScreenCanvas.Width / imgW;
            double scaleY = ScreenCanvas.Height / imgH;

            double scale = fill ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);

            double transX = (ScreenCanvas.Width / 2.0) - (img.PixelWidth / 2.0);
            double transY = (ScreenCanvas.Height / 2.0) - (img.PixelHeight / 2.0);

            ImgRotate.Angle = _currentRotation;
            ImgTranslate.X = transX;
            ImgTranslate.Y = transY;
            
            _isInternalUpdate = true;
            ZoomSlider.Value = scale;
            _isInternalUpdate = false;
            
            ImgScale.ScaleX = scale;
            ImgScale.ScaleY = scale;
        }

        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            _currentRotation -= 90;
            RecalculateFitFill(fill: true); 
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            _currentRotation += 90;
            RecalculateFitFill(fill: true);
        }

        private void Fit_Click(object sender, RoutedEventArgs e)
        {
            RecalculateFitFill(fill: false);
        }

        private void Fill_Click(object sender, RoutedEventArgs e)
        {
            RecalculateFitFill(fill: true);
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInternalUpdate) return;
            ImgScale.ScaleX = e.NewValue;
            ImgScale.ScaleY = e.NewValue;
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newValue = ZoomSlider.Value * zoomFactor;
            
            if (newValue < ZoomSlider.Minimum) newValue = ZoomSlider.Minimum;
            if (newValue > ZoomSlider.Maximum) newValue = ZoomSlider.Maximum;

            ZoomSlider.Value = newValue; 
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _start = e.GetPosition(ScreenCanvas);
            _origin = new System.Windows.Point(ImgTranslate.X, ImgTranslate.Y);
            ScreenCanvas.CaptureMouse();
            this.Cursor = System.Windows.Input.Cursors.SizeAll;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ScreenCanvas.ReleaseMouseCapture();
            this.Cursor = System.Windows.Input.Cursors.Arrow;
            GuideH.Opacity = 0;
            GuideV.Opacity = 0;
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && EditImage.Source is BitmapSource img)
            {
                System.Windows.Point current = e.GetPosition(ScreenCanvas);
                Vector diff = current - _start;
                
                double targetX = _origin.X + diff.X;
                double targetY = _origin.Y + diff.Y;

                double centerX = (ScreenCanvas.Width / 2.0) - (img.PixelWidth / 2.0);
                double centerY = (ScreenCanvas.Height / 2.0) - (img.PixelHeight / 2.0);

                bool snappedX = false;
                bool snappedY = false;

                if (Math.Abs(targetX - centerX) < SnapThreshold)
                {
                    targetX = centerX;
                    snappedX = true;
                }

                if (Math.Abs(targetY - centerY) < SnapThreshold)
                {
                    targetY = centerY;
                    snappedY = true;
                }

                ImgTranslate.X = targetX;
                ImgTranslate.Y = targetY;

                GuideV.Opacity = snappedX ? 0.6 : 0;
                GuideH.Opacity = snappedY ? 0.6 : 0;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GuideH.Visibility = Visibility.Hidden;
                GuideV.Visibility = Visibility.Hidden;

                System.Windows.Size size = new System.Windows.Size(ScreenCanvas.Width, ScreenCanvas.Height);
                ScreenCanvas.Measure(size);
                ScreenCanvas.Arrange(new Rect(size));

                RenderTargetBitmap rtb = new RenderTargetBitmap(
                    (int)ScreenCanvas.Width, 
                    (int)ScreenCanvas.Height, 
                    96, 96, 
                    PixelFormats.Pbgra32);

                rtb.Render(ScreenCanvas);

                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                encoder.QualityLevel = 95; // Slightly higher quality

                string path = Path.Combine(Path.GetTempPath(), "immich_cropped.jpg");
                using (var fs = File.Create(path))
                {
                    encoder.Save(fs);
                }

                ResultPath = path;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save crop: " + ex.Message);
            }
        }
    }
}
