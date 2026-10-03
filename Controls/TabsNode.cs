using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowTilingManager.Controls
{
    /// <summary>
    /// 여러 노드를 탭으로 묶습니다.
    /// 숨겨진 탭도 화면 트리에 남겨 두고 Visibility 만 바꾸므로, 붙어 있는 외부 창이 파괴되지 않습니다.
    /// 탭 추가/닫기 등은 셀의 컨텍스트 메뉴(또는 탭 머리글 오른쪽 클릭)에서 합니다.
    /// </summary>
    public class TabsNode : LayoutNode, ILayoutContainer
    {
        private readonly WrapPanel _header;
        private readonly Grid _body = new();
        private readonly List<LayoutNode> _tabs = new();
        private int _selected = -1;

        public TabsNode()
        {
            _header = new WrapPanel { Background = Theme.Header };
            DockPanel.SetDock(_header, Dock.Top);

            var dock = new DockPanel();
            dock.Children.Add(_header);
            dock.Children.Add(_body);
            Child = dock;
        }

        public int Count => _tabs.Count;

        public IReadOnlyList<LayoutNode> Tabs => _tabs;

        public int SelectedIndex => _selected;

        public override string DisplayTitle =>
            _selected >= 0 && _selected < _tabs.Count ? _tabs[_selected].DisplayTitle : "탭";

        /// <summary>탭을 추가합니다. node 는 다른 부모에 붙어 있지 않아야 합니다.</summary>
        public void AddTab(LayoutNode node, bool select = true)
        {
            node.ParentContainer = this;
            _tabs.Add(node);
            _body.Children.Add(node);
            Select(select ? _tabs.Count - 1 : Math.Max(_selected, 0));
        }

        public void Select(int index)
        {
            if (_tabs.Count == 0) { _selected = -1; RefreshHeader(); return; }
            _selected = Math.Clamp(index, 0, _tabs.Count - 1);
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].Visibility = i == _selected ? Visibility.Visible : Visibility.Collapsed;
            RefreshHeader();
        }

        public void RefreshHeader()
        {
            _header.Children.Clear();

            for (int i = 0; i < _tabs.Count; i++)
            {
                int index = i;
                string title = _tabs[i].DisplayTitle;
                var button = new Button
                {
                    Content = Shorten(title, 28),
                    ToolTip = title + "\n(오른쪽 클릭: 메뉴, 가운데 클릭: 탭 닫기)",
                    Padding = new Thickness(10, 2, 10, 2),
                    Margin = new Thickness(0, 0, 1, 0),
                    BorderThickness = new Thickness(0),
                    Foreground = Theme.Text,
                    Background = index == _selected ? Theme.TabSelected : Theme.TabNormal,
                    Cursor = Cursors.Hand,
                    Focusable = false
                };
                button.Click += (_, _) => Select(index);
                button.MouseUp += (_, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle) CloseTab(index);
                };
                button.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    ShowTabMenu(index, button);
                };
                _header.Children.Add(button);
            }
        }

        private void ShowTabMenu(int index, UIElement anchor)
        {
            if (index < 0 || index >= _tabs.Count) return;
            Select(index);

            // 탭이 셀 하나라면 그 셀의 전체 메뉴를 보여 줌
            if (_tabs[index] is CellControl cell)
            {
                cell.ShowMenu();
                return;
            }

            var menu = new ContextMenu { PlacementTarget = anchor };
            var add = new MenuItem { Header = "새 탭" };
            add.Click += (_, _) => AddTab(new CellControl());
            var close = new MenuItem { Header = "탭 닫기 (안의 창은 바탕화면으로)" };
            close.Click += (_, _) => CloseTab(index);
            menu.Items.Add(add);
            menu.Items.Add(close);
            menu.IsOpen = true;
        }

        private void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            var node = _tabs[index];
            if (node is CellControl cell)
            {
                cell.CloseCell();
            }
            else
            {
                foreach (var leaf in node.GetLeaves().ToList()) leaf.ReleaseWindow();
                RemoveChild(node);
            }
        }

        public void ReplaceChild(LayoutNode oldChild, LayoutNode newChild)
        {
            int index = _tabs.IndexOf(oldChild);
            if (index < 0) return;

            _body.Children.Remove(oldChild);
            oldChild.ParentContainer = null;
            oldChild.Visibility = Visibility.Visible;

            newChild.ParentContainer = this;
            _tabs[index] = newChild;
            _body.Children.Add(newChild);
            Select(_selected);
        }

        public void RemoveChild(LayoutNode child)
        {
            int index = _tabs.IndexOf(child);
            if (index < 0) return;

            _tabs.RemoveAt(index);
            _body.Children.Remove(child);
            child.ParentContainer = null;
            child.Visibility = Visibility.Visible;

            if (_tabs.Count == 0)
            {
                ParentContainer?.RemoveChild(this);
                return;
            }

            if (_tabs.Count == 1)
            {
                // 탭이 하나만 남으면 탭 묶음을 풀고 그 노드가 자리를 차지
                var last = _tabs[0];
                _tabs.Clear();
                _body.Children.Remove(last);
                last.ParentContainer = null;
                last.Visibility = Visibility.Visible;
                ParentContainer?.ReplaceChild(this, last);
                return;
            }

            int newSelected = _selected;
            if (index < _selected || _selected >= _tabs.Count) newSelected--;
            Select(newSelected);
        }

        public override IEnumerable<CellControl> GetLeaves() => _tabs.SelectMany(t => t.GetLeaves());

        private static string Shorten(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
