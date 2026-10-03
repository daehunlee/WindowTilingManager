using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowTilingManager.Controls
{
    /// <summary>자식 노드를 담을 수 있는 레이아웃 요소 (분할 / 탭 / 루트).</summary>
    public interface ILayoutContainer
    {
        /// <summary>oldChild 를 화면에서 떼어 내고 그 자리에 newChild 를 넣습니다.</summary>
        void ReplaceChild(LayoutNode oldChild, LayoutNode newChild);

        /// <summary>child 를 제거하고 남은 레이아웃을 정리합니다.</summary>
        void RemoveChild(LayoutNode child);
    }

    /// <summary>
    /// 레이아웃 트리의 노드.
    /// CellControl(창 하나를 담는 셀), SplitNode(좌우/상하 분할), TabsNode(탭 묶음) 가 있습니다.
    /// </summary>
    public abstract class LayoutNode : Border
    {
        public ILayoutContainer? ParentContainer { get; internal set; }

        /// <summary>탭 머리글 등에 표시할 제목.</summary>
        public abstract string DisplayTitle { get; }

        /// <summary>이 노드 아래의 모든 셀.</summary>
        public abstract IEnumerable<CellControl> GetLeaves();

        /// <summary>제목이 바뀌었음을 상위의 탭 묶음에 알립니다.</summary>
        protected void NotifyTitleChanged()
        {
            ILayoutContainer? p = ParentContainer;
            while (p != null)
            {
                if (p is TabsNode tabs) tabs.RefreshHeader();
                p = (p as LayoutNode)?.ParentContainer;
            }
        }
    }

    /// <summary>공통 색상.</summary>
    internal static class Theme
    {
        public static readonly Brush WindowBackground = Make(0x1E, 0x1E, 0x1E);
        public static readonly Brush CellBackground = Make(0x25, 0x25, 0x26);
        public static readonly Brush Header = Make(0x2D, 0x2D, 0x30);
        public static readonly Brush Border = Make(0x3F, 0x3F, 0x46);
        public static readonly Brush Splitter = Make(0x3F, 0x3F, 0x46);
        public static readonly Brush Text = Make(0xE0, 0xE0, 0xE0);
        public static readonly Brush SubText = Make(0x9A, 0x9A, 0x9A);
        public static readonly Brush TabNormal = Make(0x33, 0x33, 0x37);
        public static readonly Brush TabSelected = Make(0x00, 0x7A, 0xCC);
        public static readonly Brush Accent = Make(0x00, 0x7A, 0xCC);
        public static readonly Brush Frame = Make(0x2D, 0x2D, 0x30);
        public static readonly Brush FrameHover = Make(0x50, 0x6E, 0x8C);
        public static readonly Brush DropTarget = Make(0x00, 0xA2, 0xFF);

        private static Brush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
