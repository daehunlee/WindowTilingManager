using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WindowTilingManager.Native;

namespace WindowTilingManager.Controls
{
    /// <summary>
    /// 외부 프로그램 창 하나를 담는 셀.
    /// 머리글이나 버튼 없이 얇은 테두리만 있고, 모든 조작은 컨텍스트 메뉴로 합니다.
    ///   - 빈 셀: 아무 곳이나 오른쪽 클릭
    ///   - 창이 있는 셀: 테두리를 오른쪽 클릭하거나, 셀 안 어디서든 Ctrl+오른쪽 클릭
    /// </summary>
    public class CellControl : LayoutNode
    {
        private readonly Grid _body;
        private readonly Border _placeholder;
        private readonly TextBlock _placeholderText;
        private WindowHost? _host;
        private bool _busy;
        private string _busyText = string.Empty;
        private bool _isDropTarget;

        public CellControl()
        {
            BorderThickness = new Thickness(3);
            BorderBrush = Theme.Frame;
            Background = Theme.CellBackground;

            _placeholderText = new TextBlock
            {
                Foreground = Theme.SubText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(12)
            };

            _placeholder = new Border
            {
                Background = Brushes.Transparent,   // 투명 배경이어야 빈 곳에서도 마우스 이벤트를 받음
                Child = _placeholderText,
                AllowDrop = true
            };
            _placeholder.DragOver += Placeholder_DragOver;
            _placeholder.Drop += Placeholder_Drop;

            _body = new Grid { MinHeight = 30, MinWidth = 40 };
            _body.Children.Add(_placeholder);
            Child = _body;

            MouseEnter += (_, _) => UpdateFrame();
            MouseLeave += (_, _) => UpdateFrame();
            MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowMenu();
            };

            UpdatePlaceholder();
        }

        /// <summary>창이 배정되어 있는지 여부.</summary>
        public bool HasWindow => _host != null;

        /// <summary>이 셀에 배정되었던 프로그램의 실행 파일 경로 (레이아웃과 함께 저장됨).</summary>
        public string? ProgramPath { get; set; }

        private bool CanRelaunch => !string.IsNullOrEmpty(ProgramPath) && File.Exists(ProgramPath);

        public override string DisplayTitle
        {
            get
            {
                if (_host != null)
                    return string.IsNullOrWhiteSpace(_host.Title) ? Loc.T("Cell.NoTitle") : _host.Title;
                if (_busy) return Loc.T("Cell.Launching");
                return string.IsNullOrEmpty(ProgramPath)
                    ? Loc.T("Cell.Empty")
                    : Loc.T("Cell.EmptyWithProgram", Path.GetFileNameWithoutExtension(ProgramPath));
            }
        }

        public override IEnumerable<CellControl> GetLeaves()
        {
            yield return this;
        }

        /// <summary>다른 창을 끌어 이 셀 위에 올렸을 때 강조 표시.</summary>
        public bool IsDropTarget
        {
            get => _isDropTarget;
            set
            {
                if (_isDropTarget == value) return;
                _isDropTarget = value;
                UpdateFrame();
            }
        }

        /// <summary>화면 좌표(픽셀)가 이 셀 안에 있는지.</summary>
        public bool ContainsScreenPoint(int x, int y)
        {
            if (!IsVisible || ActualWidth <= 0 || PresentationSource.FromVisual(this) == null) return false;
            Point a = PointToScreen(new Point(0, 0));
            Point b = PointToScreen(new Point(ActualWidth, ActualHeight));
            return x >= Math.Min(a.X, b.X) && x < Math.Max(a.X, b.X)
                && y >= Math.Min(a.Y, b.Y) && y < Math.Max(a.Y, b.Y);
        }

        // ───────────────────────── 레이아웃 조작 ─────────────────────────

        /// <summary>이 셀을 둘로 나눕니다. 기존 내용은 왼쪽(위쪽)에 남고 오른쪽(아래쪽)에 빈 셀이 생깁니다.</summary>
        public void Split(SplitDirection direction) => SplitInto(direction, 2);

        /// <summary>
        /// 이 셀을 count 칸으로 똑같이 나눕니다. 기존 내용은 첫 칸에 남습니다.
        /// 반환값: 첫 칸(이 셀)을 포함한 모든 칸, 왼쪽(위쪽)부터 순서대로.
        /// </summary>
        public List<CellControl> SplitInto(SplitDirection direction, int count)
        {
            var cells = new List<CellControl> { this };
            if (count < 2) return cells;

            ExitZoomIfZoomed();
            var parent = ParentContainer;
            if (parent == null) return cells;

            for (int i = 1; i < count; i++) cells.Add(new CellControl());

            // 두 칸짜리 분할을 이어 붙여 n칸을 만들고, 비율을 1/n, 1/(n-1), ... 로 맞춰 균등하게 함
            var top = new SplitNode(direction);
            parent.ReplaceChild(this, top);
            top.SetChildren(this, BuildEqualSplit(direction, cells, 1));
            top.Ratio = 1.0 / count;
            return cells;
        }

        private static LayoutNode BuildEqualSplit(SplitDirection direction, List<CellControl> cells, int start)
        {
            if (start == cells.Count - 1) return cells[start];
            var split = new SplitNode(direction);
            split.SetChildren(cells[start], BuildEqualSplit(direction, cells, start + 1));
            split.Ratio = 1.0 / (cells.Count - start);
            return split;
        }

        /// <summary>이 셀 옆에 새 탭을 하나 추가합니다.</summary>
        public void AddTab() => AddTabs(1);

        /// <summary>
        /// 이 셀 옆에 새 탭을 count 개 추가합니다. 탭 묶음 안이 아니면 이 셀을 첫 탭으로 하는 탭 묶음을 만듭니다.
        /// 반환값: 새로 만든 셀들.
        /// </summary>
        public List<CellControl> AddTabs(int count)
        {
            var created = new List<CellControl>();
            if (count < 1) return created;

            ExitZoomIfZoomed();
            if (ParentContainer is not TabsNode tabs)
            {
                var parent = ParentContainer;
                if (parent == null) return created;
                tabs = new TabsNode();
                parent.ReplaceChild(this, tabs);
                tabs.AddTab(this, select: false);
            }

            for (int i = 0; i < count; i++)
            {
                var cell = new CellControl();
                tabs.AddTab(cell, select: i == 0);
                created.Add(cell);
            }
            return created;
        }

        // Ctrl 을 누른 채 메뉴를 클릭하면 개수를 물어봄
        private static bool IsCtrlDown() =>
            NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        private void SplitFromMenu(SplitDirection direction)
        {
            if (!IsCtrlDown())
            {
                Split(direction);
                return;
            }
            string title = direction == SplitDirection.LeftRight ? Loc.T("Menu.SplitLeftRight") : Loc.T("Menu.SplitTopBottom");
            int? n = AskCount(title, Loc.T("Ask.SplitCount", MaxSplit), 3, 2, MaxSplit);
            if (n.HasValue) SplitInto(direction, n.Value);
        }

        private void AddTabFromMenu()
        {
            if (!IsCtrlDown())
            {
                AddTab();
                return;
            }
            int? n = AskCount(Loc.T("Menu.AddTab"), Loc.T("Ask.TabCount", MaxTabs), 2, 1, MaxTabs);
            if (n.HasValue) AddTabs(n.Value);
        }

        private const int MaxSplit = 16;
        private const int MaxTabs = 20;

        private int? AskCount(string title, string prompt, int initial, int min, int max)
        {
            while (true)
            {
                var dialog = new InputDialog(title, prompt, initial.ToString());
                var owner = Window.GetWindow(this);
                if (owner != null) dialog.Owner = owner;
                if (dialog.ShowDialog() != true) return null;

                if (int.TryParse(dialog.Value, out int n) && n >= min && n <= max) return n;
                ShowWarning(Loc.T("Ask.NumberRange", min, max));
            }
        }

        // ───────────────────────── 여러 프로그램 한꺼번에 배치 ─────────────────────────

        /// <summary>
        /// '같은 종류의 창 모두 배치 ▸ 프로그램 이름 (n개) ▸ 탭으로 / 좌우로 / 상하로' 하위 메뉴.
        /// 예: 실행 중인 메모장 창을 모두 이 자리에 좌우로 나란히 배치.
        /// </summary>
        private MenuItem BuildSameKindMenu()
        {
            var root = new MenuItem { Header = Loc.T("Menu.ArrangeSameKind"), IsEnabled = !_busy };
            var groups = WindowEnumerator.GetTopLevelWindows()
                .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                root.Items.Add(new MenuItem { Header = Loc.T("Menu.NoWindowsToArrange"), IsEnabled = false });
                return root;
            }

            foreach (var group in groups)
            {
                var windows = group.ToList();
                var item = new MenuItem { Header = new TextBlock { Text = Loc.T("Arrange.KindEntry", group.Key, windows.Count) } };
                item.ToolTip = string.Join("\n", windows.Take(15).Select(w => w.Title)) + (windows.Count > 15 ? "\n..." : "");

                AddItem(item.Items, Loc.T("Arrange.Tabs"), () => _ = ArrangeManyAsync(ToItems(windows), ArrangeMode.Tabs));
                AddItem(item.Items, Loc.T("Arrange.LeftRight"), () => _ = ArrangeManyAsync(ToItems(windows), ArrangeMode.LeftRight));
                AddItem(item.Items, Loc.T("Arrange.TopBottom"), () => _ = ArrangeManyAsync(ToItems(windows), ArrangeMode.TopBottom));
                root.Items.Add(item);
            }
            return root;
        }

        private static List<ArrangeItem> ToItems(IEnumerable<WindowInfo> windows) =>
            windows.Select(w => new ArrangeItem
            {
                IsChecked = true,
                Kind = Loc.T("Arrange.KindRunning"),
                Name = w.ProcessName,
                Detail = w.Title,
                Handle = w.Handle
            }).ToList();

        private void ArrangeManyFromMenu()
        {
            if (_busy) return;
            var dialog = new MultiArrangeDialog();
            var owner = Window.GetWindow(this);
            if (owner != null) dialog.Owner = owner;
            if (dialog.ShowDialog() == true)
                _ = ArrangeManyAsync(dialog.SelectedItems, dialog.Mode);
        }

        /// <summary>
        /// 여러 창/프로그램을 이 셀 자리에 탭 또는 좌우/상하 나란히 배치합니다.
        /// 이 셀이 비어 있으면 첫 항목은 이 셀에 들어가고, 창이 있으면 그 뒤로 추가됩니다.
        /// </summary>
        public async Task ArrangeManyAsync(IReadOnlyList<ArrangeItem> items, ArrangeMode mode)
        {
            if (items.Count == 0) return;

            bool useThis = !HasWindow;
            int newCount = useThis ? items.Count - 1 : items.Count;

            var targets = new List<CellControl>();
            if (useThis) targets.Add(this);

            if (newCount > 0)
            {
                if (mode == ArrangeMode.Tabs)
                {
                    targets.AddRange(AddTabs(newCount));
                }
                else
                {
                    var direction = mode == ArrangeMode.LeftRight ? SplitDirection.LeftRight : SplitDirection.TopBottom;
                    var all = SplitInto(direction, newCount + 1);
                    targets.AddRange(all.Skip(1));
                }
            }

            // 새로 실행하는 프로그램끼리 창을 가로채지 않도록 하나씩 차례대로 처리
            var failed = new List<string>();
            for (int i = 0; i < items.Count && i < targets.Count; i++)
            {
                var item = items[i];
                var cell = targets[i];
                bool ok = item.Handle != IntPtr.Zero
                    ? cell.AssignWindow(item.Handle, null, showErrors: false)
                    : item.Path != null && await cell.LaunchProgramAsync(item.Path, showErrors: false);
                if (!ok) failed.Add(item.Name);
            }

            if (failed.Count > 0)
                ShowWarning(Loc.T("Arrange.Failed") + "\n\n- " + string.Join("\n- ", failed));
        }

        /// <summary>배정된 창을 바탕화면으로 돌려보낸 뒤 이 셀을 닫습니다.</summary>
        /// <summary>셀을 닫습니다. closeProgram 이 true 면 창을 바탕화면으로 돌려놓은 뒤 그 프로그램에 닫기 요청도 보냅니다.</summary>
        public void CloseCell(bool closeProgram)
        {
            IntPtr hwnd = EmbeddedHandle;
            CloseCell();
            if (closeProgram && hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public void CloseCell()
        {
            ExitZoomIfZoomed();
            ReleaseWindow();
            ParentContainer?.RemoveChild(this);
        }

        private void ExitZoomIfZoomed()
        {
            if (ParentContainer is Workspace ws) ws.ExitZoom();
        }

        // ───────────────────────── 창 배정 / 해제 ─────────────────────────

        private void AssignRunningWindow()
        {
            if (_busy) return;
            var dialog = new WindowPickerDialog();
            var owner = Window.GetWindow(this);
            if (owner != null) dialog.Owner = owner;

            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
                AssignWindow(dialog.SelectedWindow.Handle);
        }

        /// <summary>지정한 최상위 창을 이 셀에 붙입니다. 기존 창은 바탕화면으로 돌아갑니다.</summary>
        public bool AssignWindow(IntPtr hwnd, string? programPath = null, bool showErrors = true)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                if (showErrors) ShowWarning(Loc.T("Error.WindowClosed"));
                return false;
            }

            var owner = Window.GetWindow(this);
            IntPtr ownerHwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;

            ReleaseWindow();

            var host = WindowHost.TryAttach(hwnd, ownerHwnd, out string? error);
            if (host == null)
            {
                if (showErrors) ShowWarning(error ?? Loc.T("Error.AttachFailed"));
                return false;
            }

            ProgramPath = programPath ?? WindowEnumerator.TryGetProcessPath(hwnd) ?? ProgramPath;
            Adopt(host);
            if (showErrors) NativeMethods.SetForegroundWindow(hwnd);
            return true;
        }

        /// <summary>이미 붙어 있는 창을 다른 셀에서 넘겨받습니다 (창을 끌어 다른 셀로 옮길 때).</summary>
        public void AdoptHost(WindowHost host, string? programPath)
        {
            ReleaseWindow();
            ProgramPath = programPath ?? ProgramPath;
            Adopt(host);
        }

        /// <summary>창을 바탕화면으로 돌려놓지 않고 이 셀에서만 떼어 냅니다 (다른 셀로 옮길 때).</summary>
        public WindowHost? DetachHost()
        {
            var host = _host;
            if (host == null) return null;
            Unwire(host);
            _host = null;
            _placeholder.Visibility = Visibility.Visible;
            UpdatePlaceholder();
            NotifyTitleChanged();
            return host;
        }

        private void Adopt(WindowHost host)
        {
            RestorePending = false;
            host.TargetClosed += Host_TargetClosed;
            host.TitleChanged += Host_TitleChanged;
            _host = host;
            _placeholder.Visibility = Visibility.Collapsed;
            NotifyTitleChanged();
            SyncWindow(true);
        }

        private void Unwire(WindowHost host)
        {
            host.TargetClosed -= Host_TargetClosed;
            host.TitleChanged -= Host_TitleChanged;
        }

        private void Host_TargetClosed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _host)) RemoveHost(restore: false, keepPosition: false);
        }

        private void Host_TitleChanged(object? sender, EventArgs e) => NotifyTitleChanged();

        /// <summary>배정된 창을 원래 모양·위치로 되돌려 바탕화면에 내려놓습니다.</summary>
        public void ReleaseWindow() => RemoveHost(restore: true, keepPosition: false);

        /// <summary>배정된 창을 지금 위치 그대로 바탕화면에 내려놓습니다 (셀 밖으로 끌어냈을 때).</summary>
        public void ReleaseWindowAtCurrentPosition() => RemoveHost(restore: true, keepPosition: true);

        private void RemoveHost(bool restore, bool keepPosition)
        {
            var host = _host;
            if (host == null) return;
            Unwire(host);
            _host = null;

            if (restore) host.Release(keepPosition);

            _placeholder.Visibility = Visibility.Visible;
            UpdatePlaceholder();
            NotifyTitleChanged();
        }

        /// <summary>붙어 있는 창의 핸들 (없으면 0).</summary>
        public IntPtr EmbeddedHandle => _host?.TargetHandle ?? IntPtr.Zero;

        /// <summary>사용자가 붙어 있는 창을 끄는 중인지 표시합니다 (그동안 위치를 맞추지 않음).</summary>
        public void SetDragging(bool dragging)
        {
            if (_host != null) _host.IsBeingDragged = dragging;
        }

        /// <summary>
        /// 붙어 있는 창을 셀의 현재 화면 위치에 맞춥니다.
        /// 셀이 보이지 않으면(다른 세트, 다른 탭, 다른 셀 최대화 중) 창을 숨깁니다.
        /// </summary>
        public void SyncWindow(bool ownerVisible)
        {
            var host = _host;
            if (host == null) return;

            bool visible = ownerVisible && IsVisible && _body.ActualWidth >= 1 && _body.ActualHeight >= 1
                           && PresentationSource.FromVisual(_body) != null;
            if (!visible)
            {
                host.Sync(0, 0, 0, 0, false);
                return;
            }

            Point a = _body.PointToScreen(new Point(0, 0));
            Point b = _body.PointToScreen(new Point(_body.ActualWidth, _body.ActualHeight));
            int x = (int)Math.Round(Math.Min(a.X, b.X));
            int y = (int)Math.Round(Math.Min(a.Y, b.Y));
            int w = (int)Math.Round(Math.Abs(b.X - a.X));
            int h = (int)Math.Round(Math.Abs(b.Y - a.Y));
            host.Sync(x, y, w, h, true);
        }

        /// <summary>주기적 점검 (창 닫힘, 제목 변경).</summary>
        public void MonitorTick() => _host?.Tick();

        /// <summary>언어가 바뀌었을 때 셀 안의 안내 문구와 제목을 다시 표시합니다.</summary>
        public void RefreshTexts()
        {
            if (_busy) return;
            UpdatePlaceholder();
            NotifyTitleChanged();
        }

        /// <summary>다음 위치 맞추기 때 무조건 다시 맞추도록 표시합니다.</summary>
        public void ResetWindowSync() => _host?.ForceResync();

        /// <summary>붙어 있는 창을 메인 창 위로 올립니다 (메인 창이 활성일 때만).</summary>
        public void BringWindowToFront() => _host?.BringToFront();

        private void CloseEmbeddedProgram()
        {
            if (_host == null) return;
            var answer = MessageBox.Show(
                Window.GetWindow(this) ?? Application.Current.MainWindow!,
                Loc.T("CloseProgram.Confirm", _host.Title),
                Loc.T("CloseProgram.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.OK) _host.CloseTarget();
        }

        // ───────────────────────── 프로그램 실행 ─────────────────────────

        /// <summary>
        /// 프로그램을 실행하고, 새로 나타난 창을 이 셀에 붙입니다.
        /// path 가 null 이면 파일 선택 창을 띄웁니다. 성공하면 true.
        /// </summary>
        public async Task<bool> LaunchProgramAsync(string? path, bool showErrors = true)
        {
            if (_busy) return false;

            if (path == null)
            {
                var dialog = new OpenFileDialog
                {
                    Title = Loc.T("Launch.PickTitle"),
                    Filter = Loc.T("Common.ExeFilter")
                };
                if (dialog.ShowDialog() != true) return false;
                path = dialog.FileName;
            }

            var before = new HashSet<IntPtr>(WindowEnumerator.GetTopLevelWindows().Select(w => w.Handle));

            Process? process;
            try
            {
                process = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                if (showErrors) ShowWarning(Loc.T("Launch.Failed", ex.Message));
                return false;
            }

            int? pid = null;
            try { pid = process?.Id; } catch { /* 셸 실행 등으로 프로세스 정보를 얻지 못할 수 있음 */ }

            _busy = true;
            _busyText = Loc.T("Launch.Waiting", Path.GetFileName(path));
            UpdatePlaceholder();
            NotifyTitleChanged();

            IntPtr found = IntPtr.Zero;
            try
            {
                var started = DateTime.UtcNow;
                while (DateTime.UtcNow - started < TimeSpan.FromSeconds(20))
                {
                    await Task.Delay(300);
                    if (ParentContainer == null) return false;    // 기다리는 동안 셀이 닫힘

                    var fresh = WindowEnumerator.GetTopLevelWindows().Where(w => !before.Contains(w.Handle)).ToList();
                    if (fresh.Count == 0) continue;

                    // 실행한 프로세스의 창을 우선. 런처형 프로그램(다른 프로세스가 창을 띄움)을 위해
                    // 일정 시간이 지나거나 프로세스가 끝났으면 아무 새 창이나 받아들임.
                    bool ownProcessAlive = pid.HasValue && IsAlive(process);
                    var match = fresh.FirstOrDefault(w => pid.HasValue && w.ProcessId == pid.Value);
                    if (match == null && (!ownProcessAlive || DateTime.UtcNow - started > TimeSpan.FromSeconds(4)))
                        match = fresh[0];

                    if (match != null)
                    {
                        found = match.Handle;
                        break;
                    }
                }

                if (found != IntPtr.Zero)
                    await Task.Delay(500);   // 창이 초기화될 시간을 조금 줌
            }
            finally
            {
                _busy = false;
                UpdatePlaceholder();
                NotifyTitleChanged();
            }

            if (ParentContainer == null) return false;

            if (found != IntPtr.Zero)
            {
                AssignWindow(found, path, showErrors);
                return HasWindow;
            }

            if (showErrors)
                ShowWarning(Loc.T("Launch.WindowNotFound"));
            return false;
        }

        /// <summary>창이 없고 이전 프로그램 경로가 있으면 다시 실행합니다.</summary>
        public Task<bool> RelaunchIfEmptyAsync()
        {
            if (HasWindow || _busy || !CanRelaunch) return Task.FromResult(false);
            return LaunchProgramAsync(ProgramPath, showErrors: false);
        }

        private static bool IsAlive(Process? process)
        {
            try { return process != null && !process.HasExited; }
            catch { return false; }
        }

        private void Placeholder_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        private void Placeholder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                _ = LaunchProgramAsync(files[0]);
        }

        // ───────────────────────── 컨텍스트 메뉴 ─────────────────────────

        /// <summary>이 셀의 컨텍스트 메뉴를 마우스 위치에 엽니다.</summary>
        public void ShowMenu()
        {
            var menu = new ContextMenu();

            // 제목 (창 이름)
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = Shorten(DisplayTitle, 60) },   // TextBlock: '_' 가 단축키로 해석되지 않게
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            });
            menu.Items.Add(new Separator());

            // 창 배정
            AddItem(menu.Items, Loc.T("Menu.AssignRunning"), AssignRunningWindow, !_busy);
            AddItem(menu.Items, Loc.T("Menu.Launch"), () => _ = LaunchProgramAsync(null), !_busy);
            if (!HasWindow && CanRelaunch)
                AddItem(menu.Items, new TextBlock { Text = Loc.T("Menu.Relaunch", Path.GetFileName(ProgramPath)) },
                    () => _ = LaunchProgramAsync(ProgramPath), !_busy);
            menu.Items.Add(new Separator());

            // 레이아웃
            AddItem(menu.Items, Loc.T("Menu.SplitLeftRight"), () => SplitFromMenu(SplitDirection.LeftRight)).InputGestureText = Loc.T("Menu.CtrlClickCount");
            AddItem(menu.Items, Loc.T("Menu.SplitTopBottom"), () => SplitFromMenu(SplitDirection.TopBottom)).InputGestureText = Loc.T("Menu.CtrlClickCount");
            AddItem(menu.Items, Loc.T("Menu.AddTab"), AddTabFromMenu).InputGestureText = Loc.T("Menu.CtrlClickTabs");
            AddItem(menu.Items, Loc.T("Menu.ArrangeMany"), ArrangeManyFromMenu, !_busy);
            menu.Items.Add(BuildSameKindMenu());
            menu.Items.Add(new Separator());

            // 보기
            var workspace = Workspace.FindOwner(this);
            var zoom = AddItem(menu.Items, Loc.T("Menu.ZoomCell"), () => workspace?.ToggleZoom(this), workspace != null);
            zoom.IsChecked = workspace != null && ReferenceEquals(workspace.ZoomedCell, this);
            zoom.InputGestureText = "Ctrl+Alt+Z";

            var shell = AppShell.Current;
            if (shell != null)
            {
                var full = AddItem(menu.Items, Loc.T("Menu.Fullscreen"), shell.ToggleFullscreen);
                full.IsChecked = shell.IsFullscreen;
                full.InputGestureText = "F11";

                var sets = new MenuItem { Header = new TextBlock { Text = Loc.T("Menu.SetsOf", CurrentSetTitle(shell)) } };
                AppShell.FillSetMenu(sets.Items, shell);
                menu.Items.Add(sets);
            }
            menu.Items.Add(new Separator());

            // 창
            AddItem(menu.Items, Loc.T("Menu.ReleaseWindow"), () => ReleaseWindow(), HasWindow);
            AddItem(menu.Items, Loc.T("Menu.CloseProgram"), CloseEmbeddedProgram, HasWindow);
            menu.Items.Add(new Separator());
            AddItem(menu.Items, ParentContainer is TabsNode ? Loc.T("Menu.CloseTab") : Loc.T("Menu.CloseCell"), () => CloseCell());
            if (shell != null)
            {
                AddItem(menu.Items, Loc.T("Cell.CloseMany"), shell.CloseCellsInCurrentSet);
                AddItem(menu.Items, Loc.T("Set.ResetCurrent"), () => shell.ResetSet(shell.CurrentSetIndex));
            }

            // 마우스 위치에 표시 (셀 안의 다른 프로그램 창 위에서 열릴 수도 있으므로 화면 좌표 사용)
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.AbsolutePoint;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                var source = PresentationSource.FromVisual(this);
                Matrix toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                Point p = toDip.Transform(new Point(pt.X, pt.Y));
                menu.HorizontalOffset = p.X;
                menu.VerticalOffset = p.Y;
            }
            else
            {
                menu.Placement = PlacementMode.MousePoint;
            }

            Window.GetWindow(this)?.Activate();
            menu.IsOpen = true;
        }

        private static string CurrentSetTitle(IAppShell shell)
        {
            int i = shell.CurrentSetIndex;
            return i >= 0 && i < shell.Sets.Count ? shell.Sets[i].Title : "-";
        }

        private static MenuItem AddItem(ItemCollection items, object header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            items.Add(item);
            return item;
        }

        // ───────────────────────── 표시 ─────────────────────────

        private void UpdateFrame()
        {
            BorderBrush = _isDropTarget ? Theme.DropTarget : IsMouseOver ? Theme.FrameHover : Theme.Frame;
        }

        private void UpdatePlaceholder()
        {
            if (_busy)
            {
                _placeholderText.Text = _busyText;
                return;
            }

            string text = Loc.T("Cell.EmptyHint");
            if (_restorePending && !string.IsNullOrEmpty(ProgramPath))
            {
                string title = string.IsNullOrWhiteSpace(SavedTitle) ? "" : $"\n\"{SavedTitle}\"";
                text = Loc.T("Cell.PendingHint", title, Path.GetFileName(ProgramPath));
            }
            else if (!string.IsNullOrEmpty(ProgramPath))
                text += "\n\n" + Loc.T("Cell.PreviousProgram", Path.GetFileName(ProgramPath));
            _placeholderText.Text = text;
        }

        /// <summary>저장된 레이아웃에서 복원할 때 이전 프로그램 정보를 설정합니다.</summary>
        public void RestoreProgramPath(string? path, bool wasAssigned = false, string? title = null)
        {
            ProgramPath = string.IsNullOrWhiteSpace(path) ? null : path;
            RestorePending = wasAssigned && ProgramPath != null;
            SavedTitle = title;
            UpdatePlaceholder();
        }

        private bool _restorePending;

        /// <summary>
        /// 지난번 종료할 때 창이 있었지만 아직 다시 붙지 않은 셀인지 (복원 대기).
        /// 같은 프로그램의 창이 실행 중이면 '실행 중인 이전 창 다시 붙이기'로 붙습니다.
        /// </summary>
        public bool RestorePending
        {
            get => _restorePending;
            set
            {
                if (_restorePending == value) return;
                _restorePending = value;
                UpdatePlaceholder();
            }
        }

        /// <summary>마지막으로 저장된 창 제목.</summary>
        public string? SavedTitle { get; private set; }

        private void ShowWarning(string message)
        {
            var owner = Window.GetWindow(this);
            if (owner != null)
                MessageBox.Show(owner, message, "Window Tiling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(message, "Window Tiling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string Shorten(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
