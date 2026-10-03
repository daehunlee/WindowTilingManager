using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowTilingManager.Controls
{
    public enum SplitDirection
    {
        /// <summary>좌우로 나누기 (세로 구분선)</summary>
        LeftRight,
        /// <summary>상하로 나누기 (가로 구분선)</summary>
        TopBottom
    }

    /// <summary>두 개의 자식 노드를 드래그 가능한 구분선과 함께 배치합니다.</summary>
    public class SplitNode : LayoutNode, ILayoutContainer
    {
        private const double SplitterSize = 5;
        private const double MinPaneSize = 60;

        private readonly Grid _grid = new();
        private LayoutNode? _first;
        private LayoutNode? _second;

        public SplitNode(SplitDirection direction)
        {
            Direction = direction;
            Child = _grid;

            var splitter = new GridSplitter
            {
                Background = Theme.Splitter,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ShowsPreview = false
            };

            if (direction == SplitDirection.LeftRight)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = MinPaneSize });
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = MinPaneSize });
                splitter.ResizeDirection = GridResizeDirection.Columns;
                splitter.Cursor = Cursors.SizeWE;
                Grid.SetColumn(splitter, 1);
            }
            else
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinPaneSize });
                _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
                _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinPaneSize });
                splitter.ResizeDirection = GridResizeDirection.Rows;
                splitter.Cursor = Cursors.SizeNS;
                Grid.SetRow(splitter, 1);
            }

            _grid.Children.Add(splitter);
        }

        public SplitDirection Direction { get; }

        public LayoutNode? First => _first;
        public LayoutNode? Second => _second;

        /// <summary>첫 번째 칸이 차지하는 비율 (0~1). 레이아웃 저장/복원에 사용합니다.</summary>
        public double Ratio
        {
            get
            {
                double a, b;
                if (Direction == SplitDirection.LeftRight)
                {
                    a = _grid.ColumnDefinitions[0].ActualWidth;
                    b = _grid.ColumnDefinitions[2].ActualWidth;
                    if (a + b <= 0) { a = _grid.ColumnDefinitions[0].Width.Value; b = _grid.ColumnDefinitions[2].Width.Value; }
                }
                else
                {
                    a = _grid.RowDefinitions[0].ActualHeight;
                    b = _grid.RowDefinitions[2].ActualHeight;
                    if (a + b <= 0) { a = _grid.RowDefinitions[0].Height.Value; b = _grid.RowDefinitions[2].Height.Value; }
                }
                return a + b > 0 ? a / (a + b) : 0.5;
            }
            set
            {
                double r = double.IsNaN(value) ? 0.5 : System.Math.Clamp(value, 0.05, 0.95);
                var first = new GridLength(r, GridUnitType.Star);
                var second = new GridLength(1 - r, GridUnitType.Star);
                if (Direction == SplitDirection.LeftRight)
                {
                    _grid.ColumnDefinitions[0].Width = first;
                    _grid.ColumnDefinitions[2].Width = second;
                }
                else
                {
                    _grid.RowDefinitions[0].Height = first;
                    _grid.RowDefinitions[2].Height = second;
                }
            }
        }

        public override string DisplayTitle
        {
            get
            {
                var titled = GetLeaves().Where(l => l.HasWindow).ToList();
                if (titled.Count == 0) return "빈 셀";
                return titled.Count == 1 ? titled[0].DisplayTitle : $"{titled[0].DisplayTitle} 외 {titled.Count - 1}개";
            }
        }

        /// <summary>두 자식을 배치합니다. 두 노드 모두 다른 부모에 붙어 있지 않아야 합니다.</summary>
        public void SetChildren(LayoutNode first, LayoutNode second)
        {
            Place(first, 0);
            Place(second, 2);
        }

        private void Place(LayoutNode node, int index)
        {
            node.ParentContainer = this;
            if (Direction == SplitDirection.LeftRight) Grid.SetColumn(node, index);
            else Grid.SetRow(node, index);
            _grid.Children.Add(node);

            if (index == 0) _first = node;
            else _second = node;
        }

        public void ReplaceChild(LayoutNode oldChild, LayoutNode newChild)
        {
            int index = ReferenceEquals(oldChild, _first) ? 0 : 2;
            _grid.Children.Remove(oldChild);
            oldChild.ParentContainer = null;
            Place(newChild, index);
        }

        public void RemoveChild(LayoutNode child)
        {
            LayoutNode? remaining = ReferenceEquals(child, _first) ? _second : _first;

            _grid.Children.Remove(child);
            child.ParentContainer = null;

            if (remaining == null) return;
            _grid.Children.Remove(remaining);
            remaining.ParentContainer = null;
            _first = _second = null;

            // 남은 쪽이 이 분할 노드 자리를 대신 차지
            ParentContainer?.ReplaceChild(this, remaining);
        }

        public override IEnumerable<CellControl> GetLeaves()
        {
            if (_first != null) foreach (var l in _first.GetLeaves()) yield return l;
            if (_second != null) foreach (var l in _second.GetLeaves()) yield return l;
        }
    }
}
