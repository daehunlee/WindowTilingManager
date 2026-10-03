using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowTilingManager.Controls
{
    /// <summary>
    /// 타일 세트 하나. 자체 레이아웃 트리(TilingRoot)를 가지며,
    /// 셀 하나를 세트 전체 크기로 키우는 '셀 최대화' 기능을 제공합니다.
    /// 세트를 전환해도 숨겨진 세트의 창은 파괴되지 않고 계속 실행됩니다.
    /// </summary>
    public class Workspace : Grid, ILayoutContainer
    {
        private readonly Border _zoomLayer;
        private CellControl? _zoomed;
        private ZoomPlaceholder? _placeholder;

        public Workspace(string title)
        {
            Title = title;
            Root = new TilingRoot();
            _zoomLayer = new Border { Visibility = Visibility.Collapsed, Background = Theme.WindowBackground };
            Children.Add(Root);
            Children.Add(_zoomLayer);
        }

        /// <summary>세트 이름.</summary>
        public string Title { get; set; }

        public TilingRoot Root { get; }

        public IEnumerable<CellControl> Leaves => Root.Leaves;

        public bool IsZoomed => _zoomed != null;

        public CellControl? ZoomedCell => _zoomed;

        /// <summary>셀 하나를 세트 전체 크기로 표시합니다.</summary>
        public void Zoom(CellControl cell)
        {
            if (ReferenceEquals(_zoomed, cell)) return;
            ExitZoom();

            var parent = cell.ParentContainer;
            if (parent == null) return;

            // 원래 자리에 자리 표시자를 두고 셀을 최대화 레이어로 옮김
            var placeholder = new ZoomPlaceholder(cell);
            parent.ReplaceChild(cell, placeholder);

            _zoomed = cell;
            _placeholder = placeholder;
            cell.ParentContainer = this;
            _zoomLayer.Child = cell;
            _zoomLayer.Visibility = Visibility.Visible;
            Root.Visibility = Visibility.Collapsed;   // 나머지 셀(과 그 안의 창)은 숨김
        }

        /// <summary>셀 최대화를 해제하고 원래 자리로 되돌립니다.</summary>
        public void ExitZoom()
        {
            var cell = _zoomed;
            var placeholder = _placeholder;
            if (cell == null || placeholder == null) return;

            _zoomed = null;
            _placeholder = null;
            _zoomLayer.Child = null;
            _zoomLayer.Visibility = Visibility.Collapsed;
            Root.Visibility = Visibility.Visible;

            cell.ParentContainer = null;
            placeholder.ParentContainer?.ReplaceChild(placeholder, cell);
        }

        public void ToggleZoom(CellControl cell)
        {
            if (ReferenceEquals(_zoomed, cell)) ExitZoom();
            else Zoom(cell);
        }

        // 최대화된 셀에서 레이아웃 조작이 들어오면 먼저 원래 자리로 돌린 뒤 처리
        public void ReplaceChild(LayoutNode oldChild, LayoutNode newChild)
        {
            if (!ReferenceEquals(oldChild, _zoomed)) return;
            ExitZoom();
            oldChild.ParentContainer?.ReplaceChild(oldChild, newChild);
        }

        public void RemoveChild(LayoutNode child)
        {
            if (!ReferenceEquals(child, _zoomed)) return;
            ExitZoom();
            child.ParentContainer?.RemoveChild(child);
        }

        public void ReleaseAll()
        {
            ExitZoom();
            Root.ReleaseAll();
        }

        public void Reset()
        {
            ExitZoom();
            Root.Reset();
        }

        /// <summary>요소가 속한 세트를 찾습니다.</summary>
        public static Workspace? FindOwner(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Workspace ws) return ws;
                element = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
            }
            return null;
        }
    }

    /// <summary>최대화된 셀이 원래 있던 자리를 지키는 자리 표시자.</summary>
    internal sealed class ZoomPlaceholder : LayoutNode
    {
        public ZoomPlaceholder(CellControl cell)
        {
            Cell = cell;
            Background = Theme.WindowBackground;
        }

        public CellControl Cell { get; }

        public override string DisplayTitle => Cell.DisplayTitle;

        public override IEnumerable<CellControl> GetLeaves()
        {
            yield return Cell;
        }
    }
}
