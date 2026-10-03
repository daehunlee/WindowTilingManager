using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WindowTilingManager.Native;

namespace WindowTilingManager.Controls
{
    /// <summary>실행 중인 창 목록에서 셀에 배정할 창을 고르는 대화상자.</summary>
    public class WindowPickerDialog : Window
    {
        private readonly TextBox _filter;
        private readonly ListView _list;
        private List<WindowInfo> _all = new();

        public WindowPickerDialog()
        {
            Title = Loc.T("Picker.Title");
            Width = 720;
            Height = 480;
            MinWidth = 400;
            MinHeight = 250;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // 검색 줄
            _filter = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
            _filter.TextChanged += (_, _) => ApplyFilter();
            var filterLabel = new TextBlock { Text = Loc.T("Common.Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 8) };
            var filterRow = new DockPanel();
            DockPanel.SetDock(filterLabel, Dock.Left);
            filterRow.Children.Add(filterLabel);
            filterRow.Children.Add(_filter);
            DockPanel.SetDock(filterRow, Dock.Top);

            // 목록
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = Loc.T("Col.Program"), Width = 150, DisplayMemberBinding = new Binding(nameof(WindowInfo.ProcessName)) });
            view.Columns.Add(new GridViewColumn { Header = Loc.T("Col.WindowTitle"), Width = 440, DisplayMemberBinding = new Binding(nameof(WindowInfo.Title)) });
            view.Columns.Add(new GridViewColumn { Header = "PID", Width = 70, DisplayMemberBinding = new Binding(nameof(WindowInfo.ProcessId)) });
            _list = new ListView { View = view, SelectionMode = SelectionMode.Single };
            _list.MouseDoubleClick += (_, _) =>
            {
                if (_list.SelectedItem != null) Accept();
            };

            // 버튼 줄
            var refresh = new Button { Content = Loc.T("Common.Refresh"), Padding = new Thickness(12, 4, 12, 4), MinWidth = 90 };
            refresh.Click += (_, _) => LoadWindows();
            var ok = new Button { Content = Loc.T("Picker.Assign"), Padding = new Thickness(12, 4, 12, 4), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
            ok.Click += (_, _) => Accept();
            var cancel = new Button { Content = Loc.T("Common.Cancel"), Padding = new Thickness(12, 4, 12, 4), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

            var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            rightButtons.Children.Add(ok);
            rightButtons.Children.Add(cancel);

            var buttonRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(refresh, Dock.Left);
            buttonRow.Children.Add(refresh);
            buttonRow.Children.Add(rightButtons);
            DockPanel.SetDock(buttonRow, Dock.Bottom);

            var root = new DockPanel { Margin = new Thickness(12) };
            root.Children.Add(filterRow);
            root.Children.Add(buttonRow);
            root.Children.Add(_list);
            Content = root;

            Loaded += (_, _) =>
            {
                LoadWindows();
                _filter.Focus();
            };
        }

        /// <summary>사용자가 고른 창 (취소하면 null).</summary>
        public WindowInfo? SelectedWindow { get; private set; }

        private void LoadWindows()
        {
            _all = WindowEnumerator.GetTopLevelWindows()
                .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string f = _filter.Text.Trim();
            _list.ItemsSource = string.IsNullOrEmpty(f)
                ? _all
                : _all.Where(w => w.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
                               || w.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void Accept()
        {
            if (_list.SelectedItem is not WindowInfo info) return;
            SelectedWindow = info;
            DialogResult = true;
        }
    }
}
