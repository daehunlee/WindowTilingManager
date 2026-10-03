using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowTilingManager.Controls;
using WindowTilingManager.Native;
using static WindowTilingManager.Native.NativeMethods;

namespace WindowTilingManager
{
    public partial class MainWindow : Window, IAppShell
    {
        private readonly List<Workspace> _sets = new();
        private int _current = -1;

        private readonly DispatcherTimer _statusTimer;
        private readonly InputHooks _hooks = new();
        private readonly WindowDragWatcher _dragWatcher = new();
        private IntPtr _hwnd;

        // 전체 화면 전환 전 상태
        private bool _fullscreen;
        private WindowState _prevState;
        private WindowStyle _prevStyle;
        private ResizeMode _prevResizeMode;

        // 창 끌어다 놓기
        private IntPtr _dragging;
        private RECT _dragStartRect;
        private bool _dragIsMove;
        private CellControl? _dragTarget;
        private CellControl? _dragSource;      // 셀에 붙어 있던 창을 끌고 있을 때 그 셀
        private readonly DispatcherTimer _dragTimer;

        // 붙어 있는 창 위치 동기화
        private readonly DispatcherTimer _syncTimer;

        // 자동 저장 / 시작 시 복원
        private readonly DispatcherTimer _autosaveTimer;
        private bool _restoring;
        private string? _statusNotice;
        private DateTime _statusNoticeUntil;

        // 전체 화면에서 화면 가장자리로 세트 전환
        private readonly DispatcherTimer _edgeTimer;
        private int _edgeSide;
        private DateTime _edgeSince;
        private bool _edgeArmed = true;
        private ToastWindow? _toast;

        public MainWindow()
        {
            // 언어는 화면을 만들기 전에 정함 (저장된 설정 → Windows 표시 언어 → 영어)
            var layoutFile = LayoutStore.Load();
            Loc.Initialize(layoutFile?.Settings.Language);

            InitializeComponent();
            AppShell.Current = this;

            var settings = LoadLayout(layoutFile);
            RestoreOnStartupMenuItem.IsChecked = settings.RestoreWindowsOnStartup;
            EdgeSwitchMenuItem.IsChecked = settings.EdgeSwitchInFullscreen;
            DragToCellMenuItem.IsChecked = settings.DragToCell;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (_, _) => UpdateStatus();
            _statusTimer.Start();

            _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _dragTimer.Tick += (_, _) => UpdateDragTarget();

            // 붙어 있는 창은 별도의 최상위 창이므로, 셀 위치가 바뀔 때마다 따라가게 합니다.
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _syncTimer.Tick += (_, _) =>
            {
                foreach (var cell in AllCells().ToList()) cell.MonitorTick();
                SyncEmbeddedWindows();
            };
            _syncTimer.Start();

            // 비정상 종료에 대비해 1분마다 레이아웃 저장
            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _autosaveTimer.Tick += (_, _) => { if (!_restoring) TrySaveLayout(showError: false); };
            _autosaveTimer.Start();

            _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _edgeTimer.Tick += (_, _) => CheckScreenEdge();
            _edgeTimer.Start();

            // 창이 화면에 나타난 뒤 이전에 배정했던 창을 복원
            ContentRendered += (_, _) =>
            {
                RestoreWindowsOnStartup();
                BringEmbeddedWindowsToFront();
            };

            // 메인 창이 활성화될 때(클릭, Alt+Tab 등) 붙은 창들이 메인 창 뒤에 가려지지 않도록 앞으로
            Activated += (_, _) => BringEmbeddedWindowsToFront();

            LayoutUpdated += (_, _) => SyncEmbeddedWindows();
            LocationChanged += (_, _) => SyncEmbeddedWindows();
            StateChanged += (_, _) => SyncEmbeddedWindows();

            UpdateStatus();

            ApplyLanguage();
            Loc.LanguageChanged += (_, _) => ApplyLanguage();
        }

        // ═════════════════════════ 언어 ═════════════════════════

        /// <summary>메뉴와 화면의 글자를 현재 언어로 다시 채웁니다.</summary>
        private void ApplyLanguage()
        {
            FileMenu.Header = Loc.T("Main.File");
            SaveLayoutMenuItem.Header = Loc.T("Main.SaveLayout");
            RestoreOnStartupMenuItem.Header = Loc.T("Main.RestoreOnStartup");
            RestoreOnStartupMenuItem.ToolTip = Loc.T("Main.RestoreOnStartupTip");
            ReattachMenuItem.Header = Loc.T("Restore.Reattach");
            ReattachMenuItem.ToolTip = Loc.T("Main.ReattachTip");
            ReleaseAllMenuItem.Header = Loc.T("Main.ReleaseAll");
            ReleaseAllMenuItem.ToolTip = Loc.T("Main.ReleaseAllTip");
            CloseCellsMenuItem.Header = Loc.T("Cell.CloseMany");
            CloseCellsMenuItem.ToolTip = Loc.T("Main.CloseCellsTip");
            ResetSetMenuItem.Header = Loc.T("Set.ResetCurrent");
            ResetSetMenuItem.ToolTip = Loc.T("Main.ResetSetTip");
            ExitMenuItem.Header = Loc.T("Main.Exit");

            SetMenu.Header = Loc.T("Main.Sets");

            ViewMenu.Header = Loc.T("Main.View");
            FullscreenMenuItem.Header = Loc.T("Menu.Fullscreen");
            EdgeSwitchMenuItem.Header = Loc.T("Main.EdgeSwitch");
            TopmostMenuItem.Header = Loc.T("Main.Topmost");
            DragToCellMenuItem.Header = Loc.T("Main.DragToCell");
            LanguageMenu.Header = Loc.T("Main.Language");

            HelpMenu.Header = Loc.T("Main.Help");
            HelpMenuItem.Header = Loc.T("Help.Title");

            RefreshSetBar();
            foreach (var cell in AllCells().ToList()) cell.RefreshTexts();
            UpdateStatus();
        }

        private void LanguageMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, LanguageMenu)) return;
            LanguageMenu.Items.Clear();

            foreach (var lang in Loc.Available)
            {
                string code = lang.Code;
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = $"{lang.Name} ({lang.Code})" },
                    IsChecked = string.Equals(code, Loc.CurrentCode, StringComparison.OrdinalIgnoreCase),
                    ToolTip = lang.Source == "built-in" ? Loc.T("Lang.BuiltIn") : lang.Source
                };
                item.Click += (_, _) =>
                {
                    Loc.SetLanguage(code);
                    TrySaveLayout(showError: false);
                };
                LanguageMenu.Items.Add(item);
            }

            LanguageMenu.Items.Add(new Separator());
            var openFolder = new MenuItem { Header = Loc.T("Lang.OpenFolder"), ToolTip = Loc.T("Lang.OpenFolderTip") };
            openFolder.Click += (_, _) => OpenLanguagesFolder();
            LanguageMenu.Items.Add(openFolder);

            var reload = new MenuItem { Header = Loc.T("Lang.Reload") };
            reload.Click += (_, _) => Loc.Reload();
            LanguageMenu.Items.Add(reload);
        }

        private void OpenLanguagesFolder()
        {
            try
            {
                string folder = Loc.EnsureLanguagesFolder();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.T("Lang.OpenFolderFailed", ex.Message), "Window Tiling Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>모든 세트의 모든 셀.</summary>
        private IEnumerable<CellControl> AllCells() => _sets.SelectMany(s => s.Leaves);

        /// <summary>붙어 있는 모든 창을 각 셀의 현재 화면 위치에 맞추고, 보이지 않는 셀의 창은 숨깁니다.</summary>
        private void SyncEmbeddedWindows()
        {
            // 최소화 상태에서는 Windows 가 소유 창들을 함께 숨기므로 손대지 않음
            if (_hwnd == IntPtr.Zero || WindowState == WindowState.Minimized) return;
            bool ownerVisible = IsVisible;
            foreach (var cell in AllCells()) cell.SyncWindow(ownerVisible);
        }

        /// <summary>현재 세트에서 보이는 셀의 붙은 창들을 메인 창 위로 올립니다.</summary>
        private void BringEmbeddedWindowsToFront()
        {
            if (_hwnd == IntPtr.Zero || WindowState == WindowState.Minimized) return;
            SyncEmbeddedWindows();
            var set = CurrentSet;
            if (set == null) return;
            foreach (var cell in set.Leaves) cell.BringWindowToFront();
        }

        /// <summary>창 핸들로 그 창이 붙어 있는 셀을 찾습니다.</summary>
        private CellControl? FindCellByWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            return AllCells().FirstOrDefault(c => c.EmbeddedHandle == hwnd);
        }

        /// <summary>이 프로그램의 창이거나, 이 프로그램이 소유한(셀에 붙어 있는) 창인지.</summary>
        private bool IsOurWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _hwnd == IntPtr.Zero) return false;
            return hwnd == _hwnd || GetAncestor(hwnd, GA_ROOTOWNER) == _hwnd;
        }

        // ═════════════════════════ 시작 / 종료 ═════════════════════════

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            _hooks.CtrlRightButtonDown = pt => FindCellUnderCursorInOurWindow(pt) != null;
            _hooks.CtrlRightClick = pt =>
            {
                var cell = FindCellUnderCursorInOurWindow(pt);
                if (cell != null)
                    Dispatcher.BeginInvoke(new Action(cell.ShowMenu));
            };
            _hooks.KeyDown = OnGlobalKeyDown;
            _hooks.Install();

            _dragWatcher.MoveSizeStarted += OnMoveSizeStarted;
            _dragWatcher.MoveSizeEnded += OnMoveSizeEnded;
            _dragWatcher.Start();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _statusTimer.Stop();
            _dragTimer.Stop();
            _syncTimer.Stop();
            _autosaveTimer.Stop();
            _edgeTimer.Stop();
            _toast?.Close();

            _hooks.Dispose();
            _dragWatcher.Dispose();

            foreach (var set in _sets) set.ExitZoom();
            TrySaveLayout(showError: false);

            // 이 창이 닫히면 자식으로 붙어 있는 외부 창도 함께 파괴되므로 먼저 모두 돌려보냅니다.
            foreach (var set in _sets) set.ReleaseAll();
        }

        private SettingsModel LoadLayout(LayoutFileModel? file)
        {
            if (file != null)
            {
                foreach (var model in file.Sets)
                {
                    var set = CreateSet(string.IsNullOrWhiteSpace(model.Name) ? Loc.T("Set.DefaultName", _sets.Count + 1) : model.Name);
                    set.Root.Load(LayoutStore.FromModel(model.Root));
                }
                SwitchToSet(Math.Clamp(file.ActiveSet, 0, _sets.Count - 1));
            }
            else
            {
                CreateSet(Loc.T("Set.DefaultName", 1));
                SwitchToSet(0);
            }
            return file?.Settings ?? new SettingsModel();
        }

        private SettingsModel CurrentSettings() => new()
        {
            RestoreWindowsOnStartup = RestoreOnStartupMenuItem.IsChecked,
            EdgeSwitchInFullscreen = EdgeSwitchMenuItem.IsChecked,
            DragToCell = DragToCellMenuItem.IsChecked,
            Language = Loc.CurrentCode
        };

        /// <summary>
        /// 시작할 때, 지난번 종료 시점에 창이 배정되어 있던 셀에 '이미 실행 중인' 같은 프로그램의 창을 다시 붙입니다.
        /// 프로그램을 새로 실행하는 일은 하지 않습니다 (예상치 못한 프로그램 실행 방지).
        /// 실행 중이 아닌 셀은 '복원 대기' 상태로 남고, 나중에 사용자가 프로그램을 직접 띄운 뒤
        /// '실행 중인 이전 창 다시 붙이기'로 붙일 수 있습니다.
        /// </summary>
        private void RestoreWindowsOnStartup()
        {
            if (!RestoreOnStartupMenuItem.IsChecked) return;
            if (!AllCells().Any(c => c.RestorePending)) return;

            var (attached, remaining) = ReattachPreviousWindowsCore();
            SetStatusNotice(remaining == 0
                ? Loc.T("Restore.StatusAll", attached)
                : Loc.T("Restore.StatusPartial", attached, remaining), 15);
        }

        /// <summary>메뉴에서 직접 실행: 복원 대기 중인 셀에 실행 중인 같은 프로그램 창을 다시 붙입니다.</summary>
        public void ReattachPreviousWindows()
        {
            if (!AllCells().Any(c => c.RestorePending))
            {
                MessageBox.Show(this, Loc.T("Restore.NothingPending"),
                    "Window Tiling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (attached, remaining) = ReattachPreviousWindowsCore();
            string message = Loc.T("Restore.Attached", attached);
            if (remaining > 0)
                message += "\n" + Loc.T("Restore.Remaining", remaining);
            MessageBox.Show(this, message, "Window Tiling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 복원 대기 셀마다, 저장된 실행 파일 경로와 같은 프로그램의 창 중 아직 어디에도 붙지 않은 창을 찾아 붙입니다.
        /// 1차로 창 제목까지 같은 창을, 2차로 경로만 같은 창을 고릅니다.
        /// </summary>
        private (int Attached, int Remaining) ReattachPreviousWindowsCore()
        {
            var pending = AllCells().Where(c => c.RestorePending && !c.HasWindow).ToList();
            if (pending.Count == 0) return (0, 0);

            _restoring = true;
            try
            {
                var running = new List<(WindowInfo Info, string Path)>();
                foreach (var w in WindowEnumerator.GetTopLevelWindows())
                {
                    string? path = WindowEnumerator.TryGetProcessPath(w.Handle);
                    if (path != null) running.Add((w, path));
                }

                var claimed = new HashSet<IntPtr>();
                int attached = 0;
                foreach (bool exactTitle in new[] { true, false })
                {
                    foreach (var cell in pending.Where(c => c.RestorePending && !c.HasWindow).ToList())
                    {
                        var match = running.FirstOrDefault(r =>
                            !claimed.Contains(r.Info.Handle)
                            && string.Equals(r.Path, cell.ProgramPath, StringComparison.OrdinalIgnoreCase)
                            && (!exactTitle || r.Info.Title == cell.SavedTitle));
                        if (match.Info == null) continue;

                        claimed.Add(match.Info.Handle);
                        if (cell.AssignWindow(match.Info.Handle, cell.ProgramPath, showErrors: false))
                            attached++;
                    }
                }

                return (attached, pending.Count(c => !c.HasWindow));
            }
            finally
            {
                _restoring = false;
            }
        }

        private void SetStatusNotice(string text, int seconds)
        {
            _statusNotice = text;
            _statusNoticeUntil = DateTime.UtcNow.AddSeconds(seconds);
            UpdateStatus();
        }

        private bool TrySaveLayout(bool showError)
        {
            try
            {
                LayoutStore.Save(_sets, _current, CurrentSettings());
                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                    MessageBox.Show(this, Loc.T("Save.Failed", ex.Message), Loc.T("Save.FailedTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        // ═════════════════════════ 세트 (IAppShell) ═════════════════════════

        public IReadOnlyList<Workspace> Sets => _sets;

        public int CurrentSetIndex => _current;

        private Workspace? CurrentSet => _current >= 0 && _current < _sets.Count ? _sets[_current] : null;

        private Workspace CreateSet(string title)
        {
            var set = new Workspace(title) { Visibility = Visibility.Collapsed };
            _sets.Add(set);
            WorkspaceHost.Children.Add(set);
            return set;
        }

        public void SwitchToSet(int index)
        {
            if (index < 0 || index >= _sets.Count) return;
            _current = index;
            // 숨긴 세트의 창은 화면에서만 사라지고 프로그램은 계속 실행됩니다.
            for (int i = 0; i < _sets.Count; i++)
                _sets[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            RefreshSetBar();
            UpdateStatus();

            if (_fullscreen && _hwnd != IntPtr.Zero)
                ShowToast($"{index + 1} / {_sets.Count}   {_sets[index].Title}");
        }

        /// <summary>메인 창 위쪽 가운데에 잠깐 알림을 표시합니다.</summary>
        private void ShowToast(string text)
        {
            if (!GetWindowRect(_hwnd, out RECT r)) return;
            var source = PresentationSource.FromVisual(this);
            var toDip = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            Point a = toDip.Transform(new Point(r.Left, r.Top));
            Point b = toDip.Transform(new Point(r.Right, r.Bottom));
            _toast ??= new ToastWindow();
            _toast.ShowMessage(text, new Rect(a, b));
        }

        // ═════════════════════════ 전체 화면: 화면 가장자리로 세트 전환 ═════════════════════════

        /// <summary>
        /// 전체 화면일 때 마우스를 화면 왼쪽 끝(오른쪽 끝)에 잠깐 두면 이전(다음) 세트로 전환합니다.
        /// 한 번 전환한 뒤에는 마우스가 가장자리에서 떨어졌다가 다시 와야 또 전환됩니다.
        /// </summary>
        private void CheckScreenEdge()
        {
            if (!_fullscreen || !EdgeSwitchMenuItem.IsChecked || _sets.Count < 2
                || !IsOurWindow(GetForegroundWindow())
                || IsKeyDown(VK_LBUTTON) || IsKeyDown(VK_RBUTTON)
                || !GetCursorPos(out var pt) || !TryGetMonitorRect(_hwnd, out RECT m)
                || pt.Y < m.Top || pt.Y >= m.Bottom)
            {
                _edgeSide = 0;
                return;
            }

            int side = pt.X <= m.Left ? -1 : pt.X >= m.Right - 1 ? +1 : 0;
            if (side == 0)
            {
                if (pt.X > m.Left + 40 && pt.X < m.Right - 40) _edgeArmed = true;
                _edgeSide = 0;
                return;
            }
            if (!_edgeArmed) return;

            if (side != _edgeSide)
            {
                _edgeSide = side;
                _edgeSince = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - _edgeSince < TimeSpan.FromMilliseconds(350)) return;

            _edgeArmed = false;
            _edgeSide = 0;
            int n = _sets.Count;
            SwitchToSet(((_current + side) % n + n) % n);
        }

        public void AddSet()
        {
            int n = _sets.Count + 1;
            while (_sets.Any(s => s.Title == Loc.T("Set.DefaultName", n))) n++;
            CreateSet(Loc.T("Set.DefaultName", n));
            SwitchToSet(_sets.Count - 1);
        }

        public void RenameSet(int index)
        {
            if (index < 0 || index >= _sets.Count) return;
            var dialog = new InputDialog(Loc.T("Set.RenameTitle"), Loc.T("Set.RenamePrompt"), _sets[index].Title) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Value.Length > 0)
            {
                _sets[index].Title = dialog.Value;
                RefreshSetBar();
            }
        }

        public void DeleteSet(int index)
        {
            if (index < 0 || index >= _sets.Count || _sets.Count <= 1) return;
            var set = _sets[index];
            var answer = MessageBox.Show(this,
                Loc.T("Set.DeleteConfirm", set.Title),
                Loc.T("Set.DeleteTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            set.ReleaseAll();
            WorkspaceHost.Children.Remove(set);
            _sets.RemoveAt(index);
            SwitchToSet(Math.Min(index, _sets.Count - 1));
        }

        public async Task RelaunchProgramsAsync(Workspace set)
        {
            // 동시에 실행하면 새 창을 서로 가로챌 수 있으므로 차례대로 실행
            var cells = set.Leaves.Where(c => !c.HasWindow && !string.IsNullOrEmpty(c.ProgramPath)).ToList();
            if (cells.Count == 0)
            {
                MessageBox.Show(this, Loc.T("Relaunch.Nothing"), "Window Tiling Manager",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 실행하기 전에 무엇을 실행할지 반드시 확인
            string list = string.Join("\n", cells.Select(c => "  - " + c.ProgramPath));
            var answer = MessageBox.Show(this,
                Loc.T("Relaunch.Confirm", cells.Count, list),
                Loc.T("Relaunch.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            int failed = 0;
            foreach (var cell in cells)
            {
                if (!await cell.RelaunchIfEmptyAsync()) failed++;
            }

            if (failed > 0)
                MessageBox.Show(this, Loc.T("Relaunch.Failed", failed),
                    "Window Tiling Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RefreshSetBar()
        {
            SetTabs.Children.Clear();
            for (int i = 0; i < _sets.Count; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = new TextBlock { Text = _sets[i].Title },
                    ToolTip = (i < 9 ? $"Ctrl+Alt+{i + 1}\n" : "") + Loc.T("SetBar.Hint"),
                    Padding = new Thickness(14, 3, 14, 3),
                    Margin = new Thickness(0, 0, 1, 0),
                    BorderThickness = new Thickness(0),
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = i == _current
                        ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x37)),
                    Focusable = false,
                    Cursor = Cursors.Hand
                };
                button.Click += (_, _) => SwitchToSet(index);
                button.MouseDoubleClick += (_, _) => RenameSet(index);
                button.MouseRightButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    SwitchToSet(index);
                    var menu = new ContextMenu();
                    AppShell.FillSetMenu(menu.Items, this);
                    menu.IsOpen = true;
                };
                SetTabs.Children.Add(button);
            }

            var add = new Button
            {
                Content = "＋",
                ToolTip = Loc.T("Set.NewTooltip"),
                Padding = new Thickness(10, 3, 10, 3),
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent,
                Focusable = false,
                Cursor = Cursors.Hand
            };
            add.Click += (_, _) => AddSet();
            SetTabs.Children.Add(add);
        }

        private void SetMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, SetMenu))
                AppShell.FillSetMenu(SetMenu.Items, this);
        }

        // ═════════════════════════ 전체 화면 ═════════════════════════

        public bool IsFullscreen => _fullscreen;

        public void ToggleFullscreen()
        {
            if (!_fullscreen)
            {
                _prevState = WindowState;
                _prevStyle = WindowStyle;
                _prevResizeMode = ResizeMode;

                MainMenu.Visibility = Visibility.Collapsed;
                SetBar.Visibility = Visibility.Collapsed;
                StatusBarControl.Visibility = Visibility.Collapsed;

                // 최대화 상태에서 바로 스타일을 바꾸면 작업 표시줄이 남으므로 일단 Normal 로
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                _fullscreen = true;
            }
            else
            {
                WindowState = WindowState.Normal;
                WindowStyle = _prevStyle;
                ResizeMode = _prevResizeMode;
                WindowState = _prevState == WindowState.Minimized ? WindowState.Normal : _prevState;

                MainMenu.Visibility = Visibility.Visible;
                SetBar.Visibility = Visibility.Visible;
                StatusBarControl.Visibility = Visibility.Visible;
                _fullscreen = false;
            }
            FullscreenMenuItem.IsChecked = _fullscreen;
        }

        // ═════════════════════════ 단축키 ═════════════════════════

        /// <summary>
        /// 저수준 키보드 훅. 셀 안의 다른 프로그램에 포커스가 있어도 이 창이 활성 상태이면 단축키를 처리합니다.
        /// </summary>
        private bool OnGlobalKeyDown(int vk)
        {
            if (!IsOurWindow(GetForegroundWindow())) return false;
            return HandleShortcut(vk, IsKeyDown(VK_CONTROL), IsKeyDown(VK_MENU), IsKeyDown(VK_SHIFT));
        }

        // 훅이 설치되지 않은 경우를 위한 대비 (WPF 영역에 포커스가 있을 때)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            var mods = Keyboard.Modifiers;
            if (HandleShortcut(vk, mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift)))
                e.Handled = true;
        }

        private bool HandleShortcut(int vk, bool ctrl, bool alt, bool shift)
        {
            // F11: 전체 화면
            if (vk == VK_F11 && !ctrl && !alt && !shift)
            {
                Dispatcher.BeginInvoke(new Action(ToggleFullscreen));
                return true;
            }

            if (ctrl && alt && !shift)
            {
                // Ctrl+Alt+1~9: 세트 전환
                if (vk >= 0x31 && vk <= 0x39)
                {
                    int index = vk - 0x31;
                    if (index >= _sets.Count) return false;
                    Dispatcher.BeginInvoke(new Action(() => SwitchToSet(index)));
                    return true;
                }

                // Ctrl+Alt+Z: 마우스 아래 셀 최대화 / 해제
                if (vk == 0x5A)
                {
                    Dispatcher.BeginInvoke(new Action(ToggleZoomUnderCursor));
                    return true;
                }
            }
            return false;
        }

        private void ToggleZoomUnderCursor()
        {
            var set = CurrentSet;
            if (set == null) return;
            if (set.IsZoomed)
            {
                set.ExitZoom();
                return;
            }
            if (GetCursorPos(out var pt))
            {
                var cell = CellAt(pt);
                if (cell != null) set.Zoom(cell);
            }
        }

        // ═════════════════════════ 위치로 셀 찾기 ═════════════════════════

        /// <summary>현재 세트에서 화면 좌표(픽셀) 아래에 보이는 셀.</summary>
        private CellControl? CellAt(POINT pt)
        {
            var set = CurrentSet;
            if (set == null) return null;
            return set.Leaves.FirstOrDefault(c => c.ContainsScreenPoint(pt.X, pt.Y));
        }

        /// <summary>커서 바로 아래 창이 이 프로그램의 창일 때만 셀을 돌려줍니다 (Ctrl+오른쪽 클릭용).</summary>
        private CellControl? FindCellUnderCursorInOurWindow(POINT pt)
        {
            if (_hwnd == IntPtr.Zero || !IsVisible || WindowState == WindowState.Minimized) return null;
            IntPtr hit = WindowFromPoint(pt);
            if (hit == IntPtr.Zero) return null;
            IntPtr root = GetAncestor(hit, GA_ROOT);
            if (root == _hwnd) return CellAt(pt);
            // 셀에 붙어 있는 다른 프로그램 창 위
            var cell = FindCellByWindow(root);
            return cell != null && cell.IsVisible ? cell : null;
        }

        // ═════════════════════════ 창 끌어다 놓기 ═════════════════════════

        public bool DragToCellEnabled
        {
            get => DragToCellMenuItem.IsChecked;
            set => DragToCellMenuItem.IsChecked = value;
        }

        private void OnMoveSizeStarted(IntPtr hwnd)
        {
            // 셀에 붙어 있는 창을 사용자가 끌기 시작한 경우 (예: 브라우저 탭 줄을 잡고 끌기)
            var source = FindCellByWindow(hwnd);
            if (source != null)
            {
                _dragging = hwnd;
                _dragSource = source;
                source.SetDragging(true);
                GetWindowRect(hwnd, out _dragStartRect);
                _dragIsMove = GetCursorPos(out var p) && IsInsideFrame(_dragStartRect, p);
                if (_dragIsMove) _dragTimer.Start();
                return;
            }

            if (!DragToCellEnabled || !WindowEnumerator.IsAssignable(hwnd)) return;

            _dragging = hwnd;
            GetWindowRect(hwnd, out _dragStartRect);

            // 테두리(크기 조절)가 아닌 제목 표시줄을 잡고 끄는 경우만 '이동'으로 판단
            _dragIsMove = GetCursorPos(out var pt) && IsInsideFrame(_dragStartRect, pt);
            if (_dragIsMove) _dragTimer.Start();
        }

        private void OnMoveSizeEnded(IntPtr hwnd)
        {
            if (hwnd != _dragging) return;

            _dragTimer.Stop();
            bool hasCursor = GetCursorPos(out var pt);
            var target = _dragIsMove && hasCursor ? FindDropTarget(pt) : null;
            SetDragTarget(null);
            _dragging = IntPtr.Zero;

            var source = _dragSource;
            _dragSource = null;
            if (source != null)
            {
                FinishEmbeddedDrag(source, target, hasCursor && IsOverMainWindow(pt));
                return;
            }

            if (target == null) return;

            // 끝난 직후 시스템이 창 위치를 마무리할 시간을 조금 준 뒤 배정
            var dropped = hwnd;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (target.ParentContainer != null && WindowEnumerator.IsAssignable(dropped))
                {
                    target.AssignWindow(dropped);
                    Activate();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 셀에 붙어 있던 창을 끌어서 놓았을 때:
        ///  - 다른 셀 위 → 그 셀로 옮김
        ///  - 메인 창 밖 → 그 자리에서 셀에서 분리
        ///  - 그 밖(같은 셀, 메인 창 안) → 원래 셀 위치로 되돌림
        /// </summary>
        private void FinishEmbeddedDrag(CellControl source, CellControl? target, bool droppedOverMainWindow)
        {
            source.SetDragging(false);

            if (_dragIsMove && target != null && !ReferenceEquals(target, source))
            {
                string? path = source.ProgramPath;
                var host = source.DetachHost();
                if (host != null) target.AdoptHost(host, path);
            }
            else if (_dragIsMove && target == null && !droppedOverMainWindow)
            {
                source.ReleaseWindowAtCurrentPosition();
            }

            SyncEmbeddedWindows();
        }

        private bool IsOverMainWindow(POINT pt) =>
            _hwnd != IntPtr.Zero && GetWindowRect(_hwnd, out RECT r) && NativeMethods.Contains(r, pt);

        private void UpdateDragTarget()
        {
            if (_dragging == IntPtr.Zero || !GetCursorPos(out var pt))
            {
                SetDragTarget(null);
                return;
            }
            SetDragTarget(FindDropTarget(pt));
        }

        private void SetDragTarget(CellControl? cell)
        {
            if (ReferenceEquals(_dragTarget, cell)) return;
            if (_dragTarget != null) _dragTarget.IsDropTarget = false;
            _dragTarget = cell;
            if (cell != null) cell.IsDropTarget = true;
        }

        /// <summary>끌고 있는 창을 제외하고, 커서 아래 가장 위에 있는 창이 이 프로그램일 때 그 위치의 셀.</summary>
        private CellControl? FindDropTarget(POINT pt)
        {
            if (_hwnd == IntPtr.Zero || !IsVisible || WindowState == WindowState.Minimized) return null;
            if (!IsOurWindow(TopWindowAt(pt, _dragging))) return null;
            return CellAt(pt);
        }

        private static IntPtr TopWindowAt(POINT pt, IntPtr exclude)
        {
            for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = NativeMethods.GetWindow(h, GW_HWNDNEXT))
            {
                if (h == exclude || !IsWindowVisible(h) || IsIconic(h)) continue;

                uint ex = GetStyle(h, GWL_EXSTYLE);
                if ((ex & WS_EX_TRANSPARENT) != 0) continue;                                   // 클릭이 통과하는 오버레이
                if ((ex & WS_EX_LAYERED) != 0 && (ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) continue;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;

                if (GetWindowRect(h, out RECT r) && NativeMethods.Contains(r, pt)) return h;
            }
            return IntPtr.Zero;
        }

        private static bool IsInsideFrame(RECT r, POINT p)
        {
            const int border = 8;   // 이보다 가장자리에 가까우면 크기 조절로 봄
            return p.X > r.Left + border && p.X < r.Right - border
                && p.Y > r.Top + 4 && p.Y < r.Bottom - border;
        }

        // ═════════════════════════ 메뉴 / 상태 ═════════════════════════

        private void UpdateStatus()
        {
            var set = CurrentSet;
            if (set == null) return;
            var leaves = set.Leaves.ToList();
            int withWindow = leaves.Count(l => l.HasWindow);
            string zoom = set.IsZoomed ? Loc.T("Status.Zoomed") : "";
            if (_statusNotice != null && DateTime.UtcNow < _statusNoticeUntil)
            {
                StatusText.Text = Loc.T("Status.Summary", set.Title, leaves.Count, withWindow) + zoom + "   |   " + _statusNotice;
                return;
            }
            StatusText.Text = Loc.T("Status.Summary", set.Title, leaves.Count, withWindow) + zoom + "   |   " + Loc.T("Status.Hint");
        }

        private void SaveLayout_Click(object sender, RoutedEventArgs e)
        {
            if (TrySaveLayout(showError: true))
                MessageBox.Show(this, Loc.T("Save.Done", LayoutStore.FilePath), Loc.T("Save.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ReleaseAll_Click(object sender, RoutedEventArgs e) => CurrentSet?.ReleaseAll();

        private void Reattach_Click(object sender, RoutedEventArgs e) => ReattachPreviousWindows();

        private void ResetLayout_Click(object sender, RoutedEventArgs e) => ResetSet(_current);

        private void CloseCells_Click(object sender, RoutedEventArgs e) => CloseCellsInCurrentSet();

        /// <summary>
        /// 세트를 빈 셀 하나로 초기화합니다. 나눈 모양, 탭, 이전 프로그램 기록이 모두 지워지고,
        /// 들어 있던 창은 바탕화면으로 돌아갑니다 (프로그램은 닫지 않음).
        /// </summary>
        public void ResetSet(int index)
        {
            if (index < 0 || index >= _sets.Count) return;
            var set = _sets[index];
            var cells = set.Leaves.ToList();
            int windows = cells.Count(c => c.HasWindow);

            string detail = windows > 0
                ? "\n\n" + Loc.T("Reset.WindowsDetail", windows)
                : "";
            var answer = MessageBox.Show(this,
                Loc.T("Reset.Confirm", set.Title, cells.Count) + detail,
                Loc.T("Reset.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            set.Reset();
            UpdateStatus();
            SyncEmbeddedWindows();
        }

        /// <summary>현재 세트의 셀(탭 포함)을 목록에서 여러 개 골라 한 번에 닫습니다.</summary>
        public void CloseCellsInCurrentSet()
        {
            var set = CurrentSet;
            if (set == null) return;
            set.ExitZoom();   // 최대화 중이면 위치 표시가 정확하도록 먼저 해제

            var dialog = new CloseCellsDialog(set) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            foreach (var cell in dialog.SelectedCells)
            {
                if (cell.ParentContainer != null)
                    cell.CloseCell(dialog.ClosePrograms);
            }
            UpdateStatus();
            SyncEmbeddedWindows();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void Topmost_Changed(object sender, RoutedEventArgs e) => Topmost = TopmostMenuItem.IsChecked;

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, Loc.T("Help.Text"), Loc.T("Help.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
