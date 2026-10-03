using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using static WindowTilingManager.Native.NativeMethods;

namespace WindowTilingManager.Native
{
    /// <summary>배정 가능한 최상위 창 정보.</summary>
    public sealed class WindowInfo
    {
        public WindowInfo(IntPtr handle, string title, string processName, int processId)
        {
            Handle = handle;
            Title = title;
            ProcessName = processName;
            ProcessId = processId;
        }

        public IntPtr Handle { get; }
        public string Title { get; }
        public string ProcessName { get; }
        public int ProcessId { get; }

        public override string ToString() => $"[{ProcessName}] {Title}";
    }

    /// <summary>바탕화면의 최상위 창 목록을 가져옵니다.</summary>
    public static class WindowEnumerator
    {
        private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow"
        };

        /// <summary>작업 표시줄에 나타나는 일반 창(자기 자신 제외)을 반환합니다.</summary>
        public static List<WindowInfo> GetTopLevelWindows()
        {
            var result = new List<WindowInfo>();
            int selfPid = Environment.ProcessId;
            var nameCache = new Dictionary<int, string>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsCandidate(hWnd)) return true;

                GetWindowThreadProcessId(hWnd, out uint pidRaw);
                int pid = (int)pidRaw;
                if (pid == selfPid) return true;

                string title = GetWindowTextSimple(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;

                if (!nameCache.TryGetValue(pid, out string? name))
                {
                    try { name = Process.GetProcessById(pid).ProcessName; }
                    catch { name = "?"; }
                    nameCache[pid] = name;
                }

                result.Add(new WindowInfo(hWnd, title, name, pid));
                return true;
            }, IntPtr.Zero);

            return result;
        }

        /// <summary>셀에 배정할 수 있는 일반 최상위 창인지 (자기 자신의 창 제외).</summary>
        public static bool IsAssignable(IntPtr hWnd)
        {
            if (!IsWindow(hWnd) || !IsCandidate(hWnd)) return false;
            GetWindowThreadProcessId(hWnd, out uint pid);
            return (int)pid != Environment.ProcessId;
        }

        /// <summary>창을 만든 프로세스의 실행 파일 경로 (얻지 못하면 null).</summary>
        public static string? TryGetProcessPath(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;   // 관리자 권한 프로세스 등은 접근 불가
            }
        }

        private static bool IsCandidate(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;      // 대화상자 등 소유 창 제외

            uint ex = GetStyle(hWnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;

            // 숨겨진(cloaked) UWP 창, 다른 가상 데스크톱의 창 제외
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            var cls = new StringBuilder(256);
            GetClassName(hWnd, cls, cls.Capacity);
            return !ExcludedClasses.Contains(cls.ToString());
        }

        private static string GetWindowTextSimple(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// 다른 프로세스의 자식 창 제목도 읽을 수 있도록 WM_GETTEXT 를 (타임아웃과 함께) 보냅니다.
        /// 임베딩 후에는 캡션 스타일이 제거되어 GetWindowText 가 빈 문자열을 돌려주기 때문입니다.
        /// </summary>
        public static string GetTitle(IntPtr hWnd)
        {
            if (SendMessageTimeout(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, 200, out IntPtr lenPtr) != IntPtr.Zero)
            {
                int len = (int)Math.Min(lenPtr.ToInt64(), 4096);
                if (len <= 0) return string.Empty;
                var sb = new StringBuilder(len + 1);
                if (SendMessageTimeoutText(hWnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb,
                        SMTO_ABORTIFHUNG, 200, out _) != IntPtr.Zero)
                    return sb.ToString();
            }
            return GetWindowTextSimple(hWnd);
        }
    }
}
