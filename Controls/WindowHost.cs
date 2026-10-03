using System;
using WindowTilingManager.Native;
using static WindowTilingManager.Native.NativeMethods;

namespace WindowTilingManager.Controls
{
    /// <summary>
    /// 다른 프로그램의 창 하나를 셀에 '붙여' 관리합니다.
    ///
    /// [방식] 창을 자식 창(SetParent)으로 만들지 않고, 이 프로그램이 소유한 일반 최상위 창으로 둔 채
    /// 셀 위치에 정확히 겹쳐 놓습니다.
    ///  - 자식 창은 Windows에서 '활성 창'이 될 수 없어서 Chrome/Edge 등이 키보드 입력을 받지 못하고,
    ///    분리한 뒤에도 입력 큐가 묶인 채 남아 동작이 이상해지는 문제가 있었습니다.
    ///  - 소유 창은 정상적으로 활성화되고 키보드를 받으며, 항상 소유자(메인 창) 위에 표시되고
    ///    메인 창과 함께 최소화/복원됩니다. 분리할 때는 소유자와 스타일만 되돌리면 됩니다.
    /// </summary>
    public sealed class WindowHost
    {
        private readonly IntPtr _target;
        private readonly IntPtr _owner;
        private bool _attached;

        private uint _origStyle;
        private uint _origExStyle;
        private IntPtr _origOwner;
        private RECT _origRect;
        private bool _wasMaximized;

        private bool? _shown;          // 마지막으로 적용한 표시 상태

        private WindowHost(IntPtr target, IntPtr owner)
        {
            _target = target;
            _owner = owner;
            Title = WindowEnumerator.GetTitle(target);
        }

        /// <summary>붙어 있는 외부 창의 핸들.</summary>
        public IntPtr TargetHandle => _target;

        /// <summary>붙어 있는 외부 창의 현재 제목.</summary>
        public string Title { get; private set; }

        public bool IsAttached => _attached;

        /// <summary>사용자가 이 창을 끌고 있는 동안에는 위치를 맞추지 않습니다.</summary>
        public bool IsBeingDragged { get; set; }

        /// <summary>외부 창이 닫혔을 때.</summary>
        public event EventHandler? TargetClosed;

        /// <summary>외부 창의 제목이 바뀌었을 때.</summary>
        public event EventHandler? TitleChanged;

        /// <summary>창을 붙입니다. 실패하면 null 과 함께 오류 메시지를 돌려줍니다.</summary>
        public static WindowHost? TryAttach(IntPtr target, IntPtr owner, out string? error)
        {
            var host = new WindowHost(target, owner);
            error = host.Attach();
            return error == null ? host : null;
        }

        private string? Attach()
        {
            if (!IsWindow(_target))
                return "선택한 창이 이미 닫혔습니다.";
            if (_owner == IntPtr.Zero)
                return "메인 창이 아직 준비되지 않았습니다.";

            _wasMaximized = IsZoomed(_target);
            if (IsIconic(_target) || _wasMaximized)
                ShowWindow(_target, SW_RESTORE);

            GetWindowRect(_target, out _origRect);
            _origStyle = GetStyle(_target, GWL_STYLE);
            _origExStyle = GetStyle(_target, GWL_EXSTYLE);
            _origOwner = GetOwner(_target);

            // 제목 표시줄·크기 조절 테두리를 없애고, 작업 표시줄/Alt+Tab 에서 빠지도록 소유 창으로 만듦
            uint style = _origStyle & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            uint exStyle = _origExStyle & ~(WS_EX_APPWINDOW | WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);

            SetStyle(_target, GWL_STYLE, style);
            SetStyle(_target, GWL_EXSTYLE, exStyle);
            SetOwner(_target, _owner);

            if (GetOwner(_target) != _owner)
            {
                // 실패 시 원래 상태로 복구 (관리자 권한 창 등)
                SetStyle(_target, GWL_STYLE, _origStyle);
                SetStyle(_target, GWL_EXSTYLE, _origExStyle);
                SetWindowPos(_target, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
                if (_wasMaximized) ShowWindow(_target, SW_MAXIMIZE);

                return "이 창은 가져올 수 없습니다 (액세스 거부).\n관리자 권한으로 실행된 프로그램이라면 Window Tiling Manager 도 관리자 권한으로 실행해야 합니다.";
            }

            SetWindowPos(_target, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);

            _attached = true;
            return null;
        }

        /// <summary>
        /// 셀의 화면 위치(픽셀)에 창을 맞추고 표시 여부를 적용합니다.
        /// 바뀐 것이 없으면 아무것도 하지 않으므로 자주 호출해도 됩니다.
        /// </summary>
        public void Sync(int x, int y, int width, int height, bool visible)
        {
            if (!_attached || IsBeingDragged || !IsWindow(_target)) return;

            if (!visible || width < 1 || height < 1)
            {
                if (_shown != false)
                {
                    ShowWindow(_target, SW_HIDE);
                    _shown = false;
                }
                return;
            }

            // 프로그램이 스스로 최소화/최대화했으면 되돌림
            if (IsIconic(_target)) ShowWindow(_target, SW_SHOWNOACTIVATE);
            else if (IsZoomed(_target)) ShowWindow(_target, SW_RESTORE);

            // 보이지 않는 테두리만큼 바깥으로 늘려서, 보이는 영역이 셀에 딱 맞게 함
            RECT b = GetInvisibleBorder(_target);
            var want = new RECT
            {
                Left = x - b.Left,
                Top = y - b.Top,
                Right = x + width + b.Right,
                Bottom = y + height + b.Bottom
            };

            GetWindowRect(_target, out RECT now);
            bool moved = now.Left != want.Left || now.Top != want.Top || now.Right != want.Right || now.Bottom != want.Bottom;
            bool needShow = _shown != true || !IsWindowVisible(_target);

            if (!moved && !needShow) return;

            uint flags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
            if (needShow) flags |= SWP_SHOWWINDOW;
            SetWindowPos(_target, IntPtr.Zero, want.Left, want.Top, want.Width, want.Height, flags);

            _shown = true;
        }

        /// <summary>주기적 점검: 창이 닫혔는지, 소유 관계가 풀렸는지, 제목이 바뀌었는지.</summary>
        public void Tick()
        {
            if (!_attached) return;

            if (!IsWindow(_target))
            {
                _attached = false;
                TargetClosed?.Invoke(this, EventArgs.Empty);
                return;
            }

            // 일부 프로그램은 스스로 소유자를 바꾸므로 다시 지정
            if (GetOwner(_target) != _owner)
                SetOwner(_target, _owner);

            string title = WindowEnumerator.GetTitle(_target);
            if (title != Title)
            {
                Title = title;
                TitleChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 원래 스타일·소유자·크기로 되돌려 바탕화면에 독립된 창으로 내려놓습니다. 여러 번 호출해도 안전합니다.
        /// keepPosition 이 true 면 지금 위치(끌어서 놓은 자리)에 둡니다.
        /// </summary>
        public void Release(bool keepPosition = false)
        {
            if (!_attached) return;
            _attached = false;
            IsBeingDragged = false;

            if (!IsWindow(_target)) return;

            GetWindowRect(_target, out RECT current);

            SetOwner(_target, _origOwner);
            SetStyle(_target, GWL_STYLE, _origStyle);
            SetStyle(_target, GWL_EXSTYLE, _origExStyle);

            int w = _origRect.Width, h = _origRect.Height;
            int x = _origRect.Left, y = _origRect.Top;
            if (keepPosition)
            {
                x = current.Left;
                y = current.Top;
            }
            if (x < -10000 || y < -10000 || w < 100 || h < 50)
            {
                x = 100; y = 100; w = 1024; h = 768;
            }

            SetWindowPos(_target, IntPtr.Zero, x, y, w, h,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            if (_wasMaximized && !keepPosition) ShowWindow(_target, SW_MAXIMIZE);
            else ShowWindow(_target, SW_SHOW);

            SetForegroundWindow(_target);
        }

        /// <summary>붙어 있는 창에 닫기 요청(WM_CLOSE)을 보냅니다. 저장 확인 등은 해당 프로그램이 처리합니다.</summary>
        public void CloseTarget()
        {
            if (IsWindow(_target))
                PostMessage(_target, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
