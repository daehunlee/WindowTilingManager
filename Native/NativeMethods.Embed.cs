using System;
using System.Runtime.InteropServices;

namespace WindowTilingManager.Native
{
    /// <summary>소유 창(오버레이) 방식 임베딩에 필요한 Win32 API.</summary>
    internal static partial class NativeMethods
    {
        public const int GWL_HWNDPARENT = -8;      // 최상위 창에서는 '소유자(owner)'를 뜻함
        public const uint GA_ROOTOWNER = 3;

        public const uint SWP_HIDEWINDOW = 0x0080;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public const int SW_SHOWNOACTIVATE = 4;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        /// <summary>창이 있는 모니터의 전체 영역(픽셀).</summary>
        public static bool TryGetMonitorRect(IntPtr hwnd, out RECT rect)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                rect = info.rcMonitor;
                return true;
            }
            rect = default;
            return false;
        }

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        public static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        /// <summary>최상위 창의 소유자를 바꿉니다. 소유된 창은 항상 소유자 위에 표시되고, 소유자와 함께 최소화됩니다.</summary>
        public static void SetOwner(IntPtr hWnd, IntPtr owner)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hWnd, GWL_HWNDPARENT, owner);
            else
                SetWindowLong32(hWnd, GWL_HWNDPARENT, owner.ToInt32());
        }

        public static IntPtr GetOwner(IntPtr hWnd) => GetWindow(hWnd, GW_OWNER);

        /// <summary>
        /// 창 사각형과 실제로 보이는 사각형(DWM 확장 프레임)의 차이.
        /// Windows 10/11 창에는 보이지 않는 테두리가 있어서, 이 값을 보정해야 셀에 딱 맞습니다.
        /// </summary>
        public static RECT GetInvisibleBorder(IntPtr hWnd)
        {
            var border = new RECT();
            if (GetWindowRect(hWnd, out RECT wr) &&
                DwmGetWindowAttributeRect(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, Marshal.SizeOf<RECT>()) == 0)
            {
                border.Left = Math.Max(0, fr.Left - wr.Left);
                border.Top = Math.Max(0, fr.Top - wr.Top);
                border.Right = Math.Max(0, wr.Right - fr.Right);
                border.Bottom = Math.Max(0, wr.Bottom - fr.Bottom);
            }
            return border;
        }
    }
}
