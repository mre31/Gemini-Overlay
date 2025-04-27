using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Gemeni.Services
{
    public class DotSpinner : FrameworkElement
    {
        private readonly int _dotCount = 3;
        private readonly double _dotDiameter = 5;
        private readonly double _dotSpacing = 8;
        private int _animationStep = 0;
        private DispatcherTimer _timer;
        private bool _isActive = false;

        public DotSpinner()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _timer.Tick += (s, e) => 
            {
                _animationStep = (_animationStep + 1) % (_dotCount * 2);
                InvalidateVisual();
            };
            
            Width = 24;
            Height = 24;
        }

        public void Start()
        {
            _isActive = true;
            _animationStep = 0;
            Visibility = Visibility.Visible;
            _timer.Start();
        }

        public void Stop()
        {
            _isActive = false;
            _timer.Stop();
            Visibility = Visibility.Collapsed;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            if (!_isActive)
                return;

            double startX = ActualWidth / 2 - ((_dotCount - 1) * _dotSpacing) / 2;
            double centerY = ActualHeight / 2;

            for (int i = 0; i < _dotCount; i++)
            {
                int distance = (i - _animationStep / 2 + _dotCount) % _dotCount;
                double opacity = distance == 0 ? 1.0 : 0.3;

                var brush = new SolidColorBrush(Color.FromArgb(
                    (byte)(255 * opacity),
                    0x00,
                    0x7A,
                    0xFF));
                
                drawingContext.DrawEllipse(
                    brush,
                    null,
                    new Point(startX + i * _dotSpacing, centerY),
                    _dotDiameter / 2,
                    _dotDiameter / 2);
            }
        }
    }

    public static class UIAnimationHelpers
    {
        public static void ShowWindowOverlay(Window window, double targetTop)
        {
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            
            window.Top = screenHeight;
            window.Opacity = 0;
            
            var storyboard = new Storyboard();
            
            var topAnimation = new DoubleAnimation
            {
                From = screenHeight,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(topAnimation, window);
            Storyboard.SetTargetProperty(topAnimation, new PropertyPath(Window.TopProperty));
            storyboard.Children.Add(topAnimation);
            
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacityAnimation, window);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(opacityAnimation);
            
            window.Show();
            window.Opacity = 0;
            
            storyboard.Begin();
            
            window.Activate();
        }

        public static void HideWindowOverlay(Window window)
        {
            DoubleAnimation fadeOutAnimation = new DoubleAnimation
            {
                From = window.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            fadeOutAnimation.Completed += (s, e) => 
            {
                window.Hide();
                
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                window.Top = screenHeight;
                window.Opacity = 0;
            };
            
            window.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
        }

        public static void ShowElementWithFadeAnimation(UIElement element, double targetOpacity, Action onCompleted = null)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            
            var animation = new DoubleAnimation
            {
                From = 0,
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            
            if (onCompleted != null)
            {
                animation.Completed += (s, e) => onCompleted();
            }
            
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
        
        public static void FadeElementOpacity(UIElement element, double toOpacity, Action onCompleted = null)
        {
            var animation = new DoubleAnimation
            {
                From = element.Opacity,
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            if (onCompleted != null)
            {
                animation.Completed += (s, e) => onCompleted();
            }
            
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }
} 
