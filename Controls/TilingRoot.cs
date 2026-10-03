using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace WindowTilingManager.Controls
{
    /// <summary>레이아웃 트리의 최상위 컨테이너. 메인 창에 하나 놓입니다.</summary>
    public class TilingRoot : Border, ILayoutContainer
    {
        private LayoutNode _root = null!;

        public TilingRoot()
        {
            Background = Theme.WindowBackground;
            SetRoot(new CellControl());
        }

        public LayoutNode RootNode => _root;

        /// <summary>현재 모든 셀.</summary>
        public IEnumerable<CellControl> Leaves => _root.GetLeaves();

        private void SetRoot(LayoutNode node)
        {
            node.ParentContainer = this;
            _root = node;
            Child = node;
        }

        public void ReplaceChild(LayoutNode oldChild, LayoutNode newChild)
        {
            if (!ReferenceEquals(oldChild, _root)) return;
            Child = null;
            oldChild.ParentContainer = null;
            SetRoot(newChild);
        }

        public void RemoveChild(LayoutNode child)
        {
            if (!ReferenceEquals(child, _root)) return;
            Child = null;
            child.ParentContainer = null;
            SetRoot(new CellControl());   // 마지막 셀을 닫으면 빈 셀 하나로 시작
        }

        /// <summary>저장된 레이아웃으로 교체합니다 (창이 배정되지 않은 상태에서 사용).</summary>
        public void Load(LayoutNode node)
        {
            Child = null;
            _root.ParentContainer = null;
            SetRoot(node);
        }

        /// <summary>모든 셀의 창을 바탕화면으로 돌려보냅니다 (레이아웃은 유지).</summary>
        public void ReleaseAll()
        {
            foreach (var leaf in Leaves.ToList())
                leaf.ReleaseWindow();
        }

        /// <summary>모든 창을 돌려보내고 빈 셀 하나로 초기화합니다.</summary>
        public void Reset()
        {
            ReleaseAll();
            Child = null;
            _root.ParentContainer = null;
            SetRoot(new CellControl());
        }
    }
}
