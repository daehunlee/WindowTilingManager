using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static WindowTilingManager.Native.NativeMethods;

namespace WindowTilingManager.Native
{
    /// <summary>
    /// 저수준 마우스/키보드 훅.
    /// 셀 안에 붙인 다른 프로그램 창은 그 프로그램이 직접 입력을 받기 때문에,
    /// Ctrl+오른쪽 클릭이나 F11 같은 조작을 이 프로그램이 가로채려면 훅이 필요합니다.
    /// 훅 콜백은 훅을 설치한 UI 스레드에서 호출되므로 WPF 개체에 바로 접근할 수 있습니다.
    /// </summary>
    internal sealed class InputHooks : IDisposable
    {
        private readonly LowLevelHookProc _mouseProc;
        private readonly LowLevelHookProc _keyboardProc;
        private IntPtr _mouseHook;
        private IntPtr _keyboardHook;
        private bool _swallowRightUp;
        private readonly HashSet<uint> _swallowKeyUps = new();

        public InputHooks()
        {
            // 델리게이트가 GC 되지 않도록 필드에 보관
            _mouseProc = MouseProc;
            _keyboardProc = KeyboardProc;
        }

        /// <summary>Ctrl+오른쪽 버튼을 누른 지점(화면 픽셀). true 를 반환하면 클릭을 가로챕니다.</summary>
        public Func<POINT, bool>? CtrlRightButtonDown { get; set; }

        /// <summary>가로챈 Ctrl+오른쪽 클릭이 끝났을 때 (버튼을 뗀 지점).</summary>
        public Action<POINT>? CtrlRightClick { get; set; }

        /// <summary>키가 눌렸을 때 (가상 키 코드). true 를 반환하면 키 입력을 가로챕니다.</summary>
        public Func<int, bool>? KeyDown { get; set; }

        public bool IsInstalled => _mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero;

        public void Install()
        {
            IntPtr module = GetModuleHandle(null);
            if (_mouseHook == IntPtr.Zero)
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        }

        public void Dispose()
        {
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_RBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    bool take = false;
                    try { take = IsKeyDown(VK_CONTROL) && CtrlRightButtonDown?.Invoke(data.pt) == true; }
                    catch { /* 훅 안에서 예외가 나가면 안 됨 */ }
                    if (take)
                    {
                        _swallowRightUp = true;
                        return new IntPtr(1);
                    }
                }
                else if (msg == WM_RBUTTONUP && _swallowRightUp)
                {
                    _swallowRightUp = false;
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    try { CtrlRightClick?.Invoke(data.pt); } catch { }
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    bool take = false;
                    try { take = KeyDown?.Invoke((int)data.vkCode) == true; }
                    catch { }
                    if (take)
                    {
                        _swallowKeyUps.Add(data.vkCode);
                        return new IntPtr(1);
                    }
                }
                else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowKeyUps.Remove(data.vkCode))
                {
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }
}
