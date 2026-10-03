using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace WindowTilingManager.Controls
{
    /// <summary>닫기 대화상자의 목록 한 줄 (셀 하나).</summary>
    public sealed class CellEntry
    {
        public bool IsChecked { get; set; }
        public int Number { get; init; }
        public string Location { get; init; } = "";
        public string Title { get; init; } = "";
        public string Program { get; init; } = "";
        public CellControl Cell { get; init; } = null!;
    }

    /// <summary>
    /// 세트 안의 셀(탭 포함)을 목록으로 보여 주고, 여러 개를 골라 한 번에 닫는 대화상자.
    /// 목록에서 줄을 선택하면 그 셀의 테두리가 파랗게 강조되어 어느 셀인지 알 수 있습니다.
    /// </summary>
    public class CloseCellsDialog : Window
    {
        private readonly ObservableCollection<CellEntry> _entries = new();
        private readonly ListView _list;
        private readonly RadioButton _releaseWindows;
        private readonly RadioButton _closePrograms;
        private readonly Button _ok;
        private CellControl? _highlighted;

        public CloseCellsDialog(Workspace set)
        {
            Title = $"셀 여러 개 닫기 - {set.Title}";
            Width = 780;
            Height = 520;
            MinWidth = 500;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            int n = 1;
            foreach (var cell in set.Leaves)
            {
                _entries.Add(new CellEntry
                {
                    Number = n++,
                    Location = DescribeLocation(cell),
                    Title = cell.HasWindow ? cell.DisplayTitle : (cell.RestorePending ? "(복원 대기 중) " + cell.SavedTitle : "(빈 셀)"),
                    Program = string.IsNullOrEmpty(cell.ProgramPath) ? "" : Path.GetFileNameWithoutExtension(cell.ProgramPath),
                    Cell = cell
                });
            }

            var hint = new TextBlock
            {
                Text = "닫을 셀에 체크하세요. 목록에서 줄을 클릭하면 해당 셀의 테두리가 파랗게 표시됩니다. " +
                       "세트의 셀을 모두 닫으면 빈 셀 하나만 남습니다.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // 빠른 선택 버튼
            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            quick.Children.Add(QuickButton("모두 체크", _ => true));
            quick.Children.Add(QuickButton("빈 셀만 체크", e => !e.Cell.HasWindow));
            quick.Children.Add(QuickButton("창이 있는 셀만 체크", e => e.Cell.HasWindow));
            quick.Children.Add(QuickButton("모두 해제", _ => false));

            var top = new StackPanel();
            top.Children.Add(hint);
            top.Children.Add(quick);
            DockPanel.SetDock(top, Dock.Top);

            // 목록
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(CellEntry.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => UpdateOkButton()));
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "", Width = 34, CellTemplate = new DataTemplate { VisualTree = check } });
            view.Columns.Add(new GridViewColumn { Header = "#", Width = 36, DisplayMemberBinding = new Binding(nameof(CellEntry.Number)) });
            view.Columns.Add(new GridViewColumn { Header = "위치", Width = 200, DisplayMemberBinding = new Binding(nameof(CellEntry.Location)) });
            view.Columns.Add(new GridViewColumn { Header = "창 제목", Width = 340, DisplayMemberBinding = new Binding(nameof(CellEntry.Title)) });
            view.Columns.Add(new GridViewColumn { Header = "프로그램", Width = 130, DisplayMemberBinding = new Binding(nameof(CellEntry.Program)) });
            _list = new ListView { View = view, ItemsSource = _entries, SelectionMode = SelectionMode.Single };
            _list.SelectionChanged += (_, _) => Highlight((_list.SelectedItem as CellEntry)?.Cell);

            // 창 처리 방법
            _releaseWindows = new RadioButton
            {
                Content = "셀의 창은 바탕화면으로 되돌리기 (프로그램은 계속 실행)",
                IsChecked = true,
                GroupName = "windows",
                Margin = new Thickness(0, 0, 0, 4)
            };
            _closePrograms = new RadioButton
            {
                Content = "셀의 창도 닫기 (각 프로그램에 닫기 요청, 저장 안 한 내용은 프로그램이 물어봄)",
                GroupName = "windows"
            };
            _releaseWindows.Checked += (_, _) => UpdateOkButton();
            _closePrograms.Checked += (_, _) => UpdateOkButton();
            var windowsBox = new GroupBox
            {
                Header = "창이 들어 있는 셀은",
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Content = new StackPanel { Children = { _releaseWindows, _closePrograms } }
            };

            // 확인/취소
            _ok = new Button { MinWidth = 150, Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
            _ok.Click += (_, _) => Accept();
            var cancel = new Button { Content = "취소", MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);

            var bottom = new StackPanel();
            bottom.Children.Add(windowsBox);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);

            var root = new DockPanel { Margin = new Thickness(12) };
            root.Children.Add(top);
            root.Children.Add(bottom);
            root.Children.Add(_list);
            Content = root;

            Closed += (_, _) => Highlight(null);
            UpdateOkButton();
        }

        /// <summary>체크된 셀들.</summary>
        public List<CellControl> SelectedCells { get; private set; } = new();

        /// <summary>창의 프로그램도 닫기를 골랐는지.</summary>
        public bool ClosePrograms { get; private set; }

        private Button QuickButton(string text, Func<CellEntry, bool> rule)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
            button.Click += (_, _) =>
            {
                foreach (var entry in _entries) entry.IsChecked = rule(entry);
                RefreshList();
            };
            return button;
        }

        private void RefreshList()
        {
            _list.Items.Refresh();
            UpdateOkButton();
        }

        private void UpdateOkButton()
        {
            int count = _entries.Count(e => e.IsChecked);
            if (_ok == null) return;
            _ok.Content = count == 0 ? "선택한 셀 닫기" : $"선택한 셀 {count}개 닫기";
            _ok.IsEnabled = count > 0;
        }

        private void Highlight(CellControl? cell)
        {
            if (ReferenceEquals(_highlighted, cell)) return;
            if (_highlighted != null) _highlighted.IsDropTarget = false;
            _highlighted = cell;
            if (cell != null) cell.IsDropTarget = true;
        }

        private void Accept()
        {
            SelectedCells = _entries.Where(e => e.IsChecked).Select(e => e.Cell).ToList();
            if (SelectedCells.Count == 0) return;

            ClosePrograms = _closePrograms.IsChecked == true;
            int withWindow = SelectedCells.Count(c => c.HasWindow);
            if (ClosePrograms && withWindow > 0)
            {
                var answer = MessageBox.Show(this,
                    $"창이 들어 있는 셀 {withWindow}개의 프로그램에 닫기 요청을 보냅니다. 계속할까요?",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.OK) return;
            }
            DialogResult = true;
        }

        /// <summary>셀의 위치를 '탭 2 › 왼쪽 › 위' 같은 글로 나타냅니다.</summary>
        private static string DescribeLocation(CellControl cell)
        {
            var parts = new List<string>();
            LayoutNode node = cell;
            ILayoutContainer? parent = node.ParentContainer;
            while (parent is LayoutNode container)
            {
                switch (container)
                {
                    case SplitNode split:
                        bool first = ReferenceEquals(split.First, node);
                        parts.Add(split.Direction == SplitDirection.LeftRight
                            ? (first ? "왼쪽" : "오른쪽")
                            : (first ? "위" : "아래"));
                        break;
                    case TabsNode tabs:
                        int index = -1;
                        for (int i = 0; i < tabs.Tabs.Count; i++)
                            if (ReferenceEquals(tabs.Tabs[i], node)) index = i;
                        parts.Add(index >= 0 ? $"탭 {index + 1}" : "탭");
                        break;
                }
                node = container;
                parent = node.ParentContainer;
            }
            parts.Reverse();
            return parts.Count == 0 ? "전체" : string.Join(" › ", parts);
        }
    }
}
