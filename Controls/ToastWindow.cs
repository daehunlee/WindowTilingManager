using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowTilingManager.Native;

namespace WindowTilingManager.Controls
{
    /// <summary>
    /// 화면 위쪽 가운데에 잠깐 나타났다 사라지는 알림 (예: 전체 화면에서 세트를 바꿨을 때 세트 이름).
    /// 포커스를 가져가지 않고, 마우스 클릭도 통과합니다.
    /// </summary>
    public class ToastWindow : Window
    {
        private readonly TextBlock _text;
        private readonly DispatcherTimer _hideTimer;

        public ToastWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            IsHitTestVisible = false;

            _text = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center
            };
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(28, 14, 28, 14),
                Child = _text
            };

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                Hide();
            };

            SourceInitialized += (_, _) =>
            {
                // 클릭 통과 + 활성화 안 됨 + Alt+Tab 에 안 나옴
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                uint ex = NativeMethods.GetStyle(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetStyle(hwnd, NativeMethods.GWL_EXSTYLE,
                    ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            };
        }

        /// <summary>area(DIP 단위 화면 영역)의 위쪽 가운데에 메시지를 잠깐 표시합니다.</summary>
        public void ShowMessage(string text, Rect area)
        {
            _text.Text = text;
            if (!IsVisible) Show();
            UpdateLayout();
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + area.Height * 0.08;
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }
}
