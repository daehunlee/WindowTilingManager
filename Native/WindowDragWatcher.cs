using System;
using static WindowTilingManager.Native.NativeMethods;

namespace WindowTilingManager.Native
{
    /// <summary>
    /// 다른 프로그램 창의 이동/크기 조절 시작과 끝을 감지합니다 (SetWinEventHook).
    /// 창을 제목 표시줄로 끌어 셀 위에 놓으면 배정하는 기능에 사용합니다.
    /// </summary>
    internal sealed class WindowDragWatcher : IDisposable
    {
        private readonly WinEventProc _proc;
        private IntPtr _hook;

        public WindowDragWatcher()
        {
            _proc = OnWinEvent;   // GC 방지
        }

        public event Action<IntPtr>? MoveSizeStarted;
        public event Action<IntPtr>? MoveSizeEnded;

        public bool IsRunning => _hook != IntPtr.Zero;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc,
                0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            try
            {
                if (eventType == EVENT_SYSTEM_MOVESIZESTART) MoveSizeStarted?.Invoke(hwnd);
                else if (eventType == EVENT_SYSTEM_MOVESIZEEND) MoveSizeEnded?.Invoke(hwnd);
            }
            catch
            {
                // 콜백 밖으로 예외가 나가지 않도록
            }
        }
    }
}
