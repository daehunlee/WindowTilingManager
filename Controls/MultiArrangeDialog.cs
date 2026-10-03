using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using WindowTilingManager.Native;

namespace WindowTilingManager.Controls
{
    /// <summary>여러 프로그램을 한꺼번에 배치하는 방법.</summary>
    public enum ArrangeMode
    {
        Tabs,
        LeftRight,
        TopBottom
    }

    /// <summary>배치할 항목 하나 (실행 중인 창 또는 실행할 프로그램).</summary>
    public sealed class ArrangeItem
    {
        public bool IsChecked { get; set; }
        public string Kind { get; init; } = "";
        public string Name { get; init; } = "";
        public string Detail { get; init; } = "";

        /// <summary>실행 중인 창이면 그 핸들, 아니면 0.</summary>
        public IntPtr Handle { get; init; }

        /// <summary>실행할 프로그램이면 그 경로.</summary>
        public string? Path { get; init; }
    }

    /// <summary>
    /// 실행 중인 창과 실행 파일 여러 개를 골라, 탭 또는 좌우/상하 나란히 한꺼번에 배치하는 대화상자.
    /// </summary>
    public class MultiArrangeDialog : Window
    {
        private readonly ObservableCollection<ArrangeItem> _items = new();
        private readonly RadioButton _tabs;
        private readonly RadioButton _leftRight;
        private readonly RadioButton _topBottom;
        private readonly ListView _list;
        private readonly ComboBox _kinds;

        public MultiArrangeDialog()
        {
            Title = Loc.T("Arrange.Title");
            Width = 760;
            Height = 520;
            MinWidth = 480;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // 배치 방법
            _tabs = new RadioButton { Content = Loc.T("Arrange.Tabs"), IsChecked = true, Margin = new Thickness(0, 0, 16, 0), GroupName = "mode" };
            _leftRight = new RadioButton { Content = Loc.T("Arrange.LeftRight"), Margin = new Thickness(0, 0, 16, 0), GroupName = "mode" };
            _topBottom = new RadioButton { Content = Loc.T("Arrange.TopBottom"), GroupName = "mode" };
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            modeRow.Children.Add(new TextBlock { Text = Loc.T("Arrange.Mode"), Margin = new Thickness(0, 0, 10, 0), FontWeight = FontWeights.Bold });
            modeRow.Children.Add(_tabs);
            modeRow.Children.Add(_leftRight);
            modeRow.Children.Add(_topBottom);

            var hint = new TextBlock
            {
                Text = Loc.T("Arrange.Hint"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // 프로그램 종류별 일괄 선택 (예: 모든 notepad 창)
            _kinds = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0) };
            var checkKind = new Button { Content = Loc.T("Arrange.CheckKind"), Padding = new Thickness(10, 2, 10, 2) };
            checkKind.Click += (_, _) => SetKindChecked(true);
            var uncheckKind = new Button { Content = Loc.T("Arrange.UncheckKind"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            uncheckKind.Click += (_, _) => SetKindChecked(false);
            var uncheckAll = new Button { Content = Loc.T("Common.UncheckAll"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            uncheckAll.Click += (_, _) => UncheckAll();
            var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            kindRow.Children.Add(new TextBlock { Text = Loc.T("Arrange.ByKind"), Margin = new Thickness(0, 3, 10, 0), FontWeight = FontWeights.Bold });
            kindRow.Children.Add(_kinds);
            kindRow.Children.Add(checkKind);
            kindRow.Children.Add(uncheckKind);
            kindRow.Children.Add(uncheckAll);

            var top = new StackPanel();
            top.Children.Add(modeRow);
            top.Children.Add(kindRow);
            top.Children.Add(hint);
            DockPanel.SetDock(top, Dock.Top);

            // 목록 (체크박스 열 포함)
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ArrangeItem.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = new DataTemplate { VisualTree = check } });
            view.Columns.Add(new GridViewColumn { Header = Loc.T("Col.Kind"), Width = 70, DisplayMemberBinding = new Binding(nameof(ArrangeItem.Kind)) });
            view.Columns.Add(new GridViewColumn { Header = Loc.T("Col.Program"), Width = 140, DisplayMemberBinding = new Binding(nameof(ArrangeItem.Name)) });
            view.Columns.Add(new GridViewColumn { Header = Loc.T("Col.TitleOrPath"), Width = 470, DisplayMemberBinding = new Binding(nameof(ArrangeItem.Detail)) });
            _list = new ListView { View = view, ItemsSource = _items, SelectionMode = SelectionMode.Extended };

            // 버튼
            var addExe = new Button { Content = Loc.T("Arrange.AddExe"), Padding = new Thickness(12, 4, 12, 4) };
            addExe.Click += (_, _) => AddExecutables();
            var refresh = new Button { Content = Loc.T("Arrange.RefreshWindows"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
            refresh.Click += (_, _) => LoadWindows();
            var up = new Button { Content = "▲", ToolTip = Loc.T("Arrange.MoveUp"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
            up.Click += (_, _) => MoveSelected(-1);
            var down = new Button { Content = "▼", ToolTip = Loc.T("Arrange.MoveDown"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0) };
            down.Click += (_, _) => MoveSelected(+1);

            var ok = new Button { Content = Loc.T("Arrange.OK"), MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
            ok.Click += (_, _) => Accept();
            var cancel = new Button { Content = Loc.T("Common.Cancel"), MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(addExe);
            left.Children.Add(refresh);
            left.Children.Add(up);
            left.Children.Add(down);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(ok);
            right.Children.Add(cancel);

            var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(left, Dock.Left);
            buttons.Children.Add(left);
            buttons.Children.Add(right);
            DockPanel.SetDock(buttons, Dock.Bottom);

            var root = new DockPanel { Margin = new Thickness(12) };
            root.Children.Add(top);
            root.Children.Add(buttons);
            root.Children.Add(_list);
            Content = root;

            Loaded += (_, _) => LoadWindows();
        }

        public ArrangeMode Mode =>
            _leftRight.IsChecked == true ? ArrangeMode.LeftRight :
            _topBottom.IsChecked == true ? ArrangeMode.TopBottom : ArrangeMode.Tabs;

        /// <summary>체크된 항목 (목록 순서).</summary>
        public List<ArrangeItem> SelectedItems { get; private set; } = new();

        private void LoadWindows()
        {
            // 실행 파일 항목과 체크 상태는 유지하고, 창 목록만 다시 읽음
            var checkedHandles = new HashSet<IntPtr>(_items.Where(i => i.IsChecked && i.Handle != IntPtr.Zero).Select(i => i.Handle));
            var exes = _items.Where(i => i.Path != null).ToList();

            _items.Clear();
            foreach (var exe in exes) _items.Add(exe);
            foreach (var w in WindowEnumerator.GetTopLevelWindows()
                         .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase))
            {
                _items.Add(new ArrangeItem
                {
                    IsChecked = checkedHandles.Contains(w.Handle),
                    Kind = Loc.T("Arrange.KindRunning"),
                    Name = w.ProcessName,
                    Detail = w.Title,
                    Handle = w.Handle
                });
            }
            _list.Items.Refresh();
            RefreshKinds();
        }

        /// <summary>실행 중인 창을 프로그램 이름별로 묶어 콤보 상자에 채웁니다.</summary>
        private void RefreshKinds()
        {
            string? previous = (_kinds.SelectedItem as KindEntry)?.Name;
            var kinds = _items.Where(i => i.Handle != IntPtr.Zero)
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KindEntry(g.Key, g.Count()))
                .ToList();
            _kinds.ItemsSource = kinds;
            _kinds.SelectedItem = kinds.FirstOrDefault(k => string.Equals(k.Name, previous, StringComparison.OrdinalIgnoreCase))
                                  ?? kinds.FirstOrDefault();
        }

        private void UncheckAll()
        {
            foreach (var item in _items) item.IsChecked = false;
            _list.Items.Refresh();
        }

        private void SetKindChecked(bool isChecked)
        {
            if (_kinds.SelectedItem is not KindEntry kind) return;
            foreach (var item in _items.Where(i => i.Handle != IntPtr.Zero
                                                   && string.Equals(i.Name, kind.Name, StringComparison.OrdinalIgnoreCase)))
                item.IsChecked = isChecked;
            _list.Items.Refresh();
        }

        private sealed class KindEntry
        {
            public KindEntry(string name, int count) { Name = name; Count = count; }
            public string Name { get; }
            public int Count { get; }
            public override string ToString() => Loc.T("Arrange.KindEntry", Name, Count);
        }

        private void AddExecutables()
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.T("Arrange.PickExesTitle"),
                Filter = Loc.T("Common.ExeFilter"),
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true) return;

            int insertAt = _items.Count(i => i.Path != null);
            foreach (var file in dialog.FileNames)
            {
                _items.Insert(insertAt++, new ArrangeItem
                {
                    IsChecked = true,
                    Kind = Loc.T("Arrange.KindLaunch"),
                    Name = System.IO.Path.GetFileNameWithoutExtension(file),
                    Detail = file,
                    Path = file
                });
            }
        }

        private void MoveSelected(int delta)
        {
            if (_list.SelectedItem is not ArrangeItem item) return;
            int index = _items.IndexOf(item);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _items.Count) return;
            _items.Move(index, target);
            _list.SelectedItem = item;
            _list.ScrollIntoView(item);
        }

        private void Accept()
        {
            SelectedItems = _items.Where(i => i.IsChecked).ToList();
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Arrange.NothingChecked"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
