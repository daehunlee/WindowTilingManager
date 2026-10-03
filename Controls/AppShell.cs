using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WindowTilingManager.Controls
{
    /// <summary>셀의 컨텍스트 메뉴에서 앱 전체 기능(전체 화면, 세트)을 다루기 위한 인터페이스.</summary>
    public interface IAppShell
    {
        bool IsFullscreen { get; }
        void ToggleFullscreen();

        IReadOnlyList<Workspace> Sets { get; }
        int CurrentSetIndex { get; }
        void SwitchToSet(int index);
        void AddSet();
        void RenameSet(int index);
        void DeleteSet(int index);
        Task RelaunchProgramsAsync(Workspace set);
        void ReattachPreviousWindows();
        void ResetSet(int index);
        void CloseCellsInCurrentSet();

        bool DragToCellEnabled { get; set; }
    }

    public static class AppShell
    {
        public static IAppShell? Current { get; set; }

        /// <summary>'세트' 메뉴 항목들을 채웁니다 (메인 메뉴와 셀 메뉴에서 공용).</summary>
        public static void FillSetMenu(ItemCollection items, IAppShell shell)
        {
            items.Clear();

            for (int i = 0; i < shell.Sets.Count; i++)
            {
                int index = i;
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = shell.Sets[i].Title },
                    IsCheckable = false,
                    IsChecked = i == shell.CurrentSetIndex,
                    InputGestureText = i < 9 ? $"Ctrl+Alt+{i + 1}" : string.Empty
                };
                item.Click += (_, _) => shell.SwitchToSet(index);
                items.Add(item);
            }

            items.Add(new Separator());
            AddItem(items, "새 세트", shell.AddSet);
            AddItem(items, "현재 세트 이름 바꾸기...", () => shell.RenameSet(shell.CurrentSetIndex));
            AddItem(items, "현재 세트 삭제...", () => shell.DeleteSet(shell.CurrentSetIndex), shell.Sets.Count > 1);
            AddItem(items, "현재 세트 초기화...", () => shell.ResetSet(shell.CurrentSetIndex));
            AddItem(items, "셀 여러 개 선택해서 닫기...", shell.CloseCellsInCurrentSet);
            items.Add(new Separator());
            int current = shell.CurrentSetIndex;
            AddItem(items, "실행 중인 이전 창 다시 붙이기", shell.ReattachPreviousWindows);
            AddItem(items, "현재 세트의 이전 프로그램 모두 실행...", () =>
            {
                if (current >= 0 && current < shell.Sets.Count)
                    _ = shell.RelaunchProgramsAsync(shell.Sets[current]);
            });
        }

        private static void AddItem(ItemCollection items, string header, System.Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            items.Add(item);
        }
    }
}
