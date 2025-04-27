using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using System.Windows.Interop;

namespace Gemeni.Services
{
    public class ImageQueryService
    {
        private MainWindow _mainWindow;
        private ScreenCaptureOverlay _overlay;
        private readonly GeminiApiService _apiService;
        
        private string _capturedImageBase64;
        public bool HasCapturedImage => !string.IsNullOrEmpty(_capturedImageBase64);
        
        public ImageQueryService(MainWindow mainWindow, GeminiApiService apiService)
        {
            _mainWindow = mainWindow;
            _apiService = apiService;
            _capturedImageBase64 = null;
        }

        public void RegisterScreenCaptureHotkey()
        {
            _mainWindow.KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _capturedImageBase64 = null;
                
                StartScreenCapture();
                e.Handled = true;
            }
        }

        private void StartScreenCapture()
        {
            _mainWindow.Hide();

            _overlay = new ScreenCaptureOverlay();
            _overlay.CaptureComplete += Overlay_CaptureComplete;
            _overlay.Show();
        }

        private void Overlay_CaptureComplete(object sender, CaptureEventArgs e)
        {
            _overlay.Close();
            _overlay = null;

            _mainWindow.Show();

            if (e.Captured && e.CaptureRectangle.Width > 10 && e.CaptureRectangle.Height > 10)
            {
                _capturedImageBase64 = CaptureScreenshot(e.CaptureRectangle);
                
                if (!string.IsNullOrEmpty(_capturedImageBase64))
                {
                    ShowCaptureSuccessMessage();
                }
            }
        }
        
        private void ShowCaptureSuccessMessage()
        {
            _mainWindow.ShowAttachmentIcon();
        }

        private string CaptureScreenshot(Rectangle region)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(region.Width, region.Height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size);
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        byte[] imageBytes = ms.ToArray();
                        return Convert.ToBase64String(imageBytes);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public async Task ProcessImageQueryWithText(string userText)
        {
            if (string.IsNullOrEmpty(_capturedImageBase64) || string.IsNullOrWhiteSpace(userText))
            {
                return;
            }
            
            try
            {
                _mainWindow.Dispatcher.Invoke(() => {
                    if (_mainWindow is MainWindow window)
                    {
                        var responseBorder = window.FindName("ResponseBorder") as System.Windows.Controls.Border;
                        if (responseBorder != null)
                        {
                            responseBorder.Visibility = System.Windows.Visibility.Collapsed;
                        }
                    }
                });
                
                _mainWindow.PublicStartLoadingAnimation();
                
                await _apiService.QueryGeminiWithImage(
                    _capturedImageBase64, 
                    userText,
                    _mainWindow.PublicOnApiResponseUpdated,
                    _mainWindow.PublicOnApiError,
                    _mainWindow.PublicOnApiResponseStart);
                
                _capturedImageBase64 = null;
            }
            catch
            {
                _mainWindow.PublicOnApiError("Görsel işleme hatası");
                _capturedImageBase64 = null;
            }
        }
        
        public void ClearCapturedImage()
        {
            _capturedImageBase64 = null;
        }
    }

    public class CaptureEventArgs : EventArgs
    {
        public bool Captured { get; set; }
        public Rectangle CaptureRectangle { get; set; }
    }

    public class ScreenCaptureOverlay : Window
    {
        private bool _isCapturing = false;
        private System.Windows.Point _startPoint;
        private System.Windows.Point _endPoint;
        private System.Windows.Shapes.Rectangle _selectionRectangle;
        private System.Windows.Controls.Canvas _canvas;
        private System.Windows.Controls.TextBlock _instructionText;

        public event EventHandler<CaptureEventArgs> CaptureComplete;

        public ScreenCaptureOverlay()
        {
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0));
            this.Topmost = true;
            this.WindowState = WindowState.Maximized;
            this.Cursor = System.Windows.Input.Cursors.Cross;

            _canvas = new System.Windows.Controls.Canvas();
            this.Content = _canvas;

            _selectionRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red),
                StrokeThickness = 2,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 173, 216, 230))
            };
            _canvas.Children.Add(_selectionRectangle);
            
            _instructionText = new System.Windows.Controls.TextBlock
            {
                Text = "Select an area to send to AI",
                FontSize = 24,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center,
                Padding = new System.Windows.Thickness(16, 8, 16, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    Opacity = 0.6,
                    BlurRadius = 5
                }
            };
            
            _canvas.Children.Add(_instructionText);
            
            this.Loaded += (s, e) =>
            {
                PositionInstructionText();
            };
            
            this.SizeChanged += (s, e) =>
            {
                PositionInstructionText();
            };

            this.MouseLeftButtonDown += ScreenCaptureOverlay_MouseLeftButtonDown;
            this.MouseMove += ScreenCaptureOverlay_MouseMove;
            this.MouseLeftButtonUp += ScreenCaptureOverlay_MouseLeftButtonUp;
            this.KeyDown += ScreenCaptureOverlay_KeyDown;
        }
        
        private void PositionInstructionText()
        {
            if (_instructionText != null && _canvas != null)
            {
                _instructionText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = _instructionText.DesiredSize.Width;
                double textHeight = _instructionText.DesiredSize.Height;
                
                System.Windows.Controls.Canvas.SetLeft(_instructionText, (this.ActualWidth - textWidth) / 2);
                System.Windows.Controls.Canvas.SetTop(_instructionText, (this.ActualHeight - textHeight) / 2);
            }
        }

        private void ScreenCaptureOverlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CaptureComplete?.Invoke(this, new CaptureEventArgs { Captured = false });
            }
        }

        private void ScreenCaptureOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isCapturing = true;
            _startPoint = e.GetPosition(this);
            
            _selectionRectangle.Width = 0;
            _selectionRectangle.Height = 0;
            System.Windows.Controls.Canvas.SetLeft(_selectionRectangle, _startPoint.X);
            System.Windows.Controls.Canvas.SetTop(_selectionRectangle, _startPoint.Y);
            
            _instructionText.Visibility = Visibility.Collapsed;
            
            CaptureMouse();
        }

        private void ScreenCaptureOverlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isCapturing)
            {
                _endPoint = e.GetPosition(this);
                
                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);
                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);
                
                System.Windows.Controls.Canvas.SetLeft(_selectionRectangle, left);
                System.Windows.Controls.Canvas.SetTop(_selectionRectangle, top);
                _selectionRectangle.Width = width;
                _selectionRectangle.Height = height;
            }
        }

        private void ScreenCaptureOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isCapturing)
            {
                _isCapturing = false;
                ReleaseMouseCapture();
                
                _endPoint = e.GetPosition(this);
                
                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);
                double right = Math.Max(_startPoint.X, _endPoint.X);
                double bottom = Math.Max(_startPoint.Y, _endPoint.Y);
                
                System.Windows.Point topLeft = new System.Windows.Point(left, top);
                System.Windows.Point bottomRight = new System.Windows.Point(right, bottom);
                
                System.Windows.Point wpfScreenTopLeft = this.PointToScreen(topLeft);
                System.Windows.Point wpfScreenBottomRight = this.PointToScreen(bottomRight);
                
                System.Drawing.Point screenTopLeft = new System.Drawing.Point(
                    (int)wpfScreenTopLeft.X, 
                    (int)wpfScreenTopLeft.Y
                );
                
                System.Drawing.Point screenBottomRight = new System.Drawing.Point(
                    (int)wpfScreenBottomRight.X, 
                    (int)wpfScreenBottomRight.Y
                );
                
                int width = screenBottomRight.X - screenTopLeft.X;
                int height = screenBottomRight.Y - screenTopLeft.Y;
                
                var captureRect = new Rectangle(
                    screenTopLeft.X, 
                    screenTopLeft.Y, 
                    width, 
                    height);
                
                CaptureComplete?.Invoke(this, new CaptureEventArgs 
                { 
                    Captured = true, 
                    CaptureRectangle = captureRect 
                });
            }
        }
    }
}