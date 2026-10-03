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
            AddItem(items, Loc.T("Set.New"), shell.AddSet);
            AddItem(items, Loc.T("Set.RenameCurrent"), () => shell.RenameSet(shell.CurrentSetIndex));
            AddItem(items, Loc.T("Set.DeleteCurrent"), () => shell.DeleteSet(shell.CurrentSetIndex), shell.Sets.Count > 1);
            AddItem(items, Loc.T("Set.ResetCurrent"), () => shell.ResetSet(shell.CurrentSetIndex));
            AddItem(items, Loc.T("Cell.CloseMany"), shell.CloseCellsInCurrentSet);
            items.Add(new Separator());
            int current = shell.CurrentSetIndex;
            AddItem(items, Loc.T("Restore.Reattach"), shell.ReattachPreviousWindows);
            AddItem(items, Loc.T("Set.RelaunchAll"), () =>
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
