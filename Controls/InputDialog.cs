using System.Windows;
using System.Windows.Controls;

namespace WindowTilingManager.Controls
{
    /// <summary>한 줄 텍스트를 입력받는 간단한 대화상자.</summary>
    public class InputDialog : Window
    {
        private readonly TextBox _text;

        public InputDialog(string title, string prompt, string initial)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _text = new TextBox { Text = initial, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 6, 0, 12) };

            var ok = new Button { Content = "확인", MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), IsDefault = true };
            ok.Click += (_, _) => DialogResult = true;
            var cancel = new Button { Content = "취소", MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = prompt });
            panel.Children.Add(_text);
            panel.Children.Add(buttons);
            Content = panel;

            Loaded += (_, _) =>
            {
                _text.Focus();
                _text.SelectAll();
            };
        }

        public string Value => _text.Text.Trim();
    }
}
