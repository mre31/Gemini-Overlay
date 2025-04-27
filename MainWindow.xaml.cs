using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.VisualBasic;
using System.Diagnostics;
using Gemeni.Services;

namespace Gemeni
{
    public partial class MainWindow : Window
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int VK_G = 0x47;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _windowHandle;
        private HwndSource _source;
        
        private readonly ApiKeyManager _apiKeyManager = ApiKeyManager.Instance;
        
        private SettingsManager _settingsManager;
        private CancellationTokenSource _cancellationTokenSource;
        private StringBuilder _fullResponseBuilder = new StringBuilder();
        private bool _userScrolledManually = false;
        private Gemeni.Services.DotSpinner _loadingSpinner;
        
        private bool _isNewConversation = true;
        
        private SystemTrayManager _systemTrayManager;

        private GeminiApiService _apiService;
        
        private MarkdownRenderer _markdownRenderer;

        private ImageQueryService _imageQueryService;

        private Image _attachmentIcon;

        public MainWindow()
        {
            InitializeComponent();
            
            _settingsManager = new SettingsManager();
            
            Loaded += MainWindow_Loaded;
            
            this.Loaded += (s, e) => 
            {
                if (_systemTrayManager == null)
                {
                    _systemTrayManager = new SystemTrayManager(this, _settingsManager.CurrentSettings);
                    _systemTrayManager.ModelChanged += (sender, modelId) => {
                        try {
                            _settingsManager.LoadSettings();
                            
                            var loadedSettings = _settingsManager.CurrentSettings;
                            _apiService = new GeminiApiService(loadedSettings, _apiKeyManager);
                            
                            _apiService.ClearConversation();
                            _isNewConversation = true;
                        } catch {
                        }
                    };
                }
                
                _apiService = new GeminiApiService(_settingsManager.CurrentSettings, _apiKeyManager);
                
                _markdownRenderer = new MarkdownRenderer(ResponseBox);
                
                _imageQueryService = new ImageQueryService(this, _apiService);
                _imageQueryService.RegisterScreenCaptureHotkey();
                
                CreateAttachmentIcon();
            };
            
            _loadingSpinner = new Gemeni.Services.DotSpinner();
            _loadingSpinner.Visibility = Visibility.Collapsed;
            
            if (GridSpinnerContainer != null)
            {
                GridSpinnerContainer.Children.Add(_loadingSpinner);
            }
            
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);

            RegisterHotKey(_windowHandle, 9000, MOD_CONTROL | MOD_SHIFT, VK_G);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!e.Cancel)
            {
                DisableHook();
                _systemTrayManager?.Dispose();
            }
            
            e.Cancel = true;
            HideOverlay();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 9000)
            {
                if (this.Visibility == Visibility.Visible)
                {
                    HideOverlay();
                }
                else
                {
                    ShowOverlay();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void ShowOverlay()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            Left = (screenWidth - Width) / 2;
            
            double bottomMarginPercentage = 0.0556;
            double targetTop = screenHeight - Height - (screenHeight * bottomMarginPercentage);
            
            InputBox.Text = string.Empty;
            
            ResponseBorder.Visibility = Visibility.Collapsed;
            TopFogEffect.Visibility = Visibility.Collapsed;
            BottomFogEffect.Visibility = Visibility.Collapsed;
            
            Gemeni.Services.UIAnimationHelpers.ShowWindowOverlay(this, targetTop);
            
            InputBox.Focus();
        }

        public void HideOverlay()
        {
            _apiService?.CancelRequest();
            
            _fullResponseBuilder.Clear();
            _userScrolledManually = false;
            
            _apiService?.ClearConversation();
            _isNewConversation = true;
            
            StopLoadingAnimation();
            
            _imageQueryService?.ClearCapturedImage();
            HideAttachmentIcon();
            
            if (Dispatcher.CheckAccess())
            {
                ClearResponseBox();
                InputBox.Text = string.Empty;
                ResponseBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                Dispatcher.Invoke(() => {
                    ClearResponseBox();
                    InputBox.Text = string.Empty;
                    ResponseBorder.Visibility = Visibility.Collapsed;
                });
            }
            
            Gemeni.Services.UIAnimationHelpers.HideWindowOverlay(this);
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
        }

        private void StartLoadingAnimation()
        {
            HideAttachmentIcon();
            _loadingSpinner.Start();
        }
        
        public void PublicStartLoadingAnimation()
        {
            StartLoadingAnimation();
        }
        
        private void StopLoadingAnimation()
        {
            _loadingSpinner.Stop();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideOverlay();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string query = InputBox.Text.Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource = new CancellationTokenSource();
                    
                    if (_isNewConversation)
                    {
                        ClearResponseBox();
                        _fullResponseBuilder.Clear();
                        _isNewConversation = false;
                    }
                    else
                    {
                        _fullResponseBuilder.Clear();
                        ClearResponseBox();
                    }
                    
                    TopFogEffect.Visibility = Visibility.Collapsed;
                    BottomFogEffect.Visibility = Visibility.Collapsed;
                    
                    string userQuery = query;
                    
                    InputBox.Text = string.Empty;
                    
                    if (_imageQueryService != null && _imageQueryService.HasCapturedImage)
                    {
                        StartLoadingAnimation();
                        _ = _imageQueryService.ProcessImageQueryWithText(userQuery);
                    }
                    else
                    {
                        _apiService.AddUserMessageToHistory(userQuery);
                        
                        if (ResponseBorder.Visibility == Visibility.Visible)
                        {
                            Gemeni.Services.UIAnimationHelpers.FadeElementOpacity(ResponseBorder, 0.0, () => {
                                StartLoadingAnimation();
                                
                                _ = _apiService.QueryGeminiAPI(
                                    userQuery, 
                                    OnApiResponseUpdated, 
                                    OnApiError, 
                                    OnApiResponseStart, 
                                    !_userScrolledManually || Math.Abs(GetScrollViewer(ResponseBox)?.VerticalOffset - GetScrollViewer(ResponseBox)?.ScrollableHeight ?? 0) < 5);
                            });
                        }
                        else
                        {
                            StartLoadingAnimation();
                            
                            _ = _apiService.QueryGeminiAPI(
                                userQuery, 
                                OnApiResponseUpdated, 
                                OnApiError, 
                                OnApiResponseStart, 
                                !_userScrolledManually || Math.Abs(GetScrollViewer(ResponseBox)?.VerticalOffset - GetScrollViewer(ResponseBox)?.ScrollableHeight ?? 0) < 5);
                        }
                    }
                }
            }
        }

        private void PasteCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                string clipboardText = Clipboard.GetText();
                
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    clipboardText = clipboardText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                    
                    clipboardText = System.Text.RegularExpressions.Regex.Replace(clipboardText, @"\s+", " ");
                    
                    int caretIndex = InputBox.CaretIndex;
                    string currentText = InputBox.Text;
                    
                    string newText = currentText.Insert(caretIndex, clipboardText);
                    
                    InputBox.Text = newText;
                    InputBox.CaretIndex = caretIndex + clipboardText.Length;
                    
                    e.Handled = true;
                }
            }
            catch {
            }
        }

        private void PasteCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
            e.Handled = true;
        }

        private void InputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
        }

        private void ClearResponseBox()
        {
            ResponseBox.Document.Blocks.Clear();
            ResponseBox.Document.Blocks.Add(new Paragraph());
        }

        private ScrollViewer GetScrollViewer(RichTextBox richTextBox)
        {
            if (richTextBox == null) return null;
            
            ScrollViewer viewer = richTextBox.Template?.FindName("PART_ContentHost", richTextBox) as ScrollViewer;
            if (viewer != null) return viewer;
            
            try
            {
                DependencyObject child = null;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(richTextBox) && viewer == null; i++)
                {
                    child = VisualTreeHelper.GetChild(richTextBox, i);
                    viewer = child as ScrollViewer;
                    if (viewer == null && child != null)
                    {
                        viewer = FindVisualChild<ScrollViewer>(child);
                    }
                }
                
                return viewer;
            }
            catch {
                if (richTextBox.IsLoaded)
                {
                    richTextBox.Dispatcher.BeginInvoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                return null;
            }
        }
        
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }

        private void AppendToResponseBox(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendToResponseBox(text));
                return;
            }

            try
            {
                if (ResponseBorder.Visibility != Visibility.Visible)
                {
                    ShowResponseBoxWithAnimation();
                }
                else if (ResponseBorder.Opacity < 0.5)
                {
                    Gemeni.Services.UIAnimationHelpers.FadeElementOpacity(ResponseBorder, 0.95);
                }
                
                _fullResponseBuilder.Append(text);
                
                _markdownRenderer.ParseAndDisplayMarkdown(_fullResponseBuilder.ToString());
                
                var scrollViewer = GetScrollViewer(ResponseBox);
                if (scrollViewer != null)
                {
                    if (!_userScrolledManually || Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 5)
                    {
                        Dispatcher.BeginInvoke(new Action(() => {
                            scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight);
                        }), DispatcherPriority.Background);
                    }
                }
            }
            catch {
                try 
                {
                    if (ResponseBox.Document.Blocks.Count == 0)
                    {
                        ResponseBox.Document.Blocks.Add(new Paragraph());
                    }
                    
                    if (ResponseBox.Document.Blocks.LastBlock is Paragraph paragraph)
                    {
                        paragraph.Inlines.Add(new Run(text));
                        ResponseBox.ScrollToEnd();
                    }
                    else
                    {
                        Paragraph newParagraph = new Paragraph();
                        newParagraph.Inlines.Add(new Run(text));
                        ResponseBox.Document.Blocks.Add(newParagraph);
                        ResponseBox.ScrollToEnd();
                    }
                }
                catch {
                }
            }
        }

        private void ResponseBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _userScrolledManually = true;
        }

        private void ResponseBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                var scrollViewer = GetScrollViewer(ResponseBox);
                if (scrollViewer == null) return;
                
                if (e.VerticalChange != 0 && Math.Abs(e.VerticalChange) > 0.1)
                {
                    double scrollableHeight = scrollViewer.ScrollableHeight;
                    double currentOffset = scrollViewer.VerticalOffset;
                    
                    if (currentOffset < scrollableHeight - 20 && e.VerticalChange < 0)
                    {
                        _userScrolledManually = true;
                    }
                    else if (currentOffset > scrollableHeight - 20 && e.VerticalChange > 0)
                    {
                        _userScrolledManually = false;
                    }
                }
                
                if (Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 5)
                {
                    _userScrolledManually = false;
                }
                
                UpdateFogEffects(scrollViewer);
            }
            catch {
                _userScrolledManually = false;
            }
        }
        
        private void UpdateFogEffects(ScrollViewer scrollViewer)
        {
            if (scrollViewer == null) return;
            
            double verticalOffset = scrollViewer.VerticalOffset;
            double scrollableHeight = scrollViewer.ScrollableHeight;
            double viewportHeight = scrollViewer.ViewportHeight;
            
            bool hasScrollableContent = scrollableHeight > 0;
            
            if (!hasScrollableContent)
            {
                TopFogEffect.Visibility = Visibility.Collapsed;
                BottomFogEffect.Visibility = Visibility.Collapsed;
                return;
            }
            
            bool isAtTop = verticalOffset <= 2;
            
            bool isAtBottom = Math.Abs(verticalOffset - scrollableHeight) <= 2;
            
            TopFogEffect.Visibility = isAtTop ? Visibility.Collapsed : Visibility.Visible;
            
            BottomFogEffect.Visibility = isAtBottom ? Visibility.Collapsed : Visibility.Visible;
        }

        private void DisableHook()
        {
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
            }
            try
            {
                UnregisterHotKey(_windowHandle, 9000);
            }
            catch {
            }
        }

        private void ShowResponseBoxWithAnimation()
        {
            Gemeni.Services.UIAnimationHelpers.ShowElementWithFadeAnimation(ResponseBorder, 0.95, () => {
                InputBox.Focus();
            });
            
            ResponseBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        private void OnApiResponseUpdated(string responseText, bool isComplete)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnApiResponseUpdated(responseText, isComplete));
                return;
            }

            try
            {
                _fullResponseBuilder.Clear();
                _fullResponseBuilder.Append(responseText);
                
                bool isCurrentlyAtEnd = false;
                var scrollViewer = GetScrollViewer(ResponseBox);
                if (scrollViewer != null)
                {
                    isCurrentlyAtEnd = !_userScrolledManually && 
                        Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 5;
                }
                
                _markdownRenderer.ParseAndDisplayMarkdown(responseText, isCurrentlyAtEnd);
                
                if (isComplete)
                {
                    StopLoadingAnimation();
                }
            }
            catch {
                StopLoadingAnimation();
            }
        }
        
        private void OnApiError(string errorMessage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnApiError(errorMessage));
                return;
            }
            
            ShowResponseBoxWithAnimation();

            ResponseBox.Document.Blocks.Clear();
            Paragraph paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run(errorMessage));
            ResponseBox.Document.Blocks.Add(paragraph);
            
            StopLoadingAnimation();
        }
        
        private void OnApiResponseStart()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnApiResponseStart());
                return;
            }
            
            ShowResponseBoxWithAnimation();
            StartLoadingAnimation();
        }

        public string GetInputBoxText()
        {
            return InputBox?.Text ?? string.Empty;
        }
        
        public void ClearInputBox()
        {
            if (InputBox != null)
            {
                if (Dispatcher.CheckAccess())
                {
                    InputBox.Text = string.Empty;
                }
                else
                {
                    Dispatcher.Invoke(() => InputBox.Text = string.Empty);
                }
            }
        }

        public void PublicOnApiResponseUpdated(string responseText, bool isComplete)
        {
            OnApiResponseUpdated(responseText, isComplete);
        }
        
        public void PublicOnApiError(string errorMessage)
        {
            OnApiError(errorMessage);
        }
        
        public void PublicOnApiResponseStart()
        {
            OnApiResponseStart();
        }

        public void SetInputBoxText(string text)
        {
            if (InputBox != null)
            {
                if (Dispatcher.CheckAccess())
                {
                    InputBox.Text = text;
                    InputBox.SelectAll();
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        InputBox.Text = text;
                        InputBox.SelectAll();
                    });
                }
            }
        }

        private void CreateAttachmentIcon()
        {
            _attachmentIcon = new Image
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            CreateDefaultAttachmentIcon();
            
            if (GridSpinnerContainer != null)
            {
                GridSpinnerContainer.Children.Add(_attachmentIcon);
            }
        }
        
        private void CreateDefaultAttachmentIcon()
        {
            var paperClipIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M16.5,6V17.5A4,4 0 0,1 12.5,21.5A4,4 0 0,1 8.5,17.5V5A2.5,2.5 0 0,1 11,2.5A2.5,2.5 0 0,1 13.5,5V15.5A1,1 0 0,1 12.5,16.5A1,1 0 0,1 11.5,15.5V6H10V15.5A2.5,2.5 0 0,0 12.5,18A2.5,2.5 0 0,0 15,15.5V5A4,4 0 0,0 11,1A4,4 0 0,0 7,5V17.5A5.5,5.5 0 0,0 12.5,23A5.5,5.5 0 0,0 18,17.5V6H16.5Z"),
                Fill = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var paperClipViewbox = new Viewbox
            {
                Child = paperClipIcon,
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var iconGrid = new Grid
            {
                Width = 24,
                Height = 24
            };
            
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                CornerRadius = new CornerRadius(4),
                Width = 24,
                Height = 24
            };
            
            iconGrid.Children.Add(border);
            iconGrid.Children.Add(paperClipViewbox);
            
            var renderBitmap = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
            iconGrid.Measure(new Size(24, 24));
            iconGrid.Arrange(new Rect(new Size(24, 24)));
            renderBitmap.Render(iconGrid);
            
            _attachmentIcon.Source = renderBitmap;
        }
        
        public void ShowAttachmentIcon()
        {
            if (_attachmentIcon != null)
            {
                if (Dispatcher.CheckAccess())
                {
                    _loadingSpinner.Visibility = Visibility.Collapsed;
                    _attachmentIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        _loadingSpinner.Visibility = Visibility.Collapsed;
                        _attachmentIcon.Visibility = Visibility.Visible;
                    });
                }
            }
        }
        
        public void HideAttachmentIcon()
        {
            if (_attachmentIcon != null)
            {
                if (Dispatcher.CheckAccess())
                {
                    _attachmentIcon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        _attachmentIcon.Visibility = Visibility.Collapsed;
                    });
                }
            }
        }
        
        public bool IsInputBoxFocused()
        {
            return InputBox != null && InputBox.IsFocused;
        }
    }
}