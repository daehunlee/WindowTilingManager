using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowTilingManager.Controls
{
    /// <summary>저장 파일 전체.</summary>
    public sealed class LayoutFileModel
    {
        public int Version { get; set; } = 1;
        public int ActiveSet { get; set; }
        public List<SetModel> Sets { get; set; } = new();
        public SettingsModel Settings { get; set; } = new();
    }

    /// <summary>사용자 설정.</summary>
    public sealed class SettingsModel
    {
        /// <summary>시작할 때 이전에 배정했던 창을 자동으로 다시 붙이거나 실행.</summary>
        public bool RestoreWindowsOnStartup { get; set; } = true;

        /// <summary>전체 화면에서 화면 왼쪽/오른쪽 끝으로 세트 전환.</summary>
        public bool EdgeSwitchInFullscreen { get; set; } = true;

        /// <summary>다른 창을 끌어다 셀에 놓으면 배정.</summary>
        public bool DragToCell { get; set; } = true;
    }

    /// <summary>세트 하나.</summary>
    public sealed class SetModel
    {
        public string Name { get; set; } = "세트";
        public NodeModel? Root { get; set; }
    }

    /// <summary>레이아웃 트리의 노드 하나.</summary>
    public sealed class NodeModel
    {
        /// <summary>"cell", "split", "tabs"</summary>
        public string Type { get; set; } = "cell";

        // split
        public string? Direction { get; set; }
        public double Ratio { get; set; } = 0.5;

        // split / tabs
        public List<NodeModel>? Children { get; set; }

        // tabs
        public int Selected { get; set; }

        // cell
        public string? Program { get; set; }

        /// <summary>저장할 때 이 셀에 창이 배정되어 있었는지 (시작 시 자동 복원 대상).</summary>
        public bool Assigned { get; set; }

        /// <summary>저장할 때의 창 제목 (같은 프로그램 창이 여러 개일 때 고르는 데 사용).</summary>
        public string? Title { get; set; }
    }

    /// <summary>
    /// 세트와 레이아웃을 %AppData%\WindowTilingManager\layouts.json 에 저장/복원합니다.
    /// 창 자체는 저장할 수 없으므로 각 셀에 배정됐던 프로그램 경로를 함께 저장해 두고,
    /// 다음 실행 때 메뉴에서 다시 실행할 수 있게 합니다.
    /// </summary>
    public static class LayoutStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowTilingManager", "layouts.json");

        // ── 저장 ────────────────────────────────

        public static void Save(IReadOnlyList<Workspace> sets, int activeIndex, SettingsModel settings)
        {
            var file = new LayoutFileModel
            {
                ActiveSet = activeIndex,
                Settings = settings,
                Sets = sets.Select(s => new SetModel { Name = s.Title, Root = ToModel(s.Root.RootNode) }).ToList()
            };

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }

        public static NodeModel ToModel(LayoutNode node)
        {
            switch (node)
            {
                case SplitNode split:
                    return new NodeModel
                    {
                        Type = "split",
                        Direction = split.Direction == SplitDirection.LeftRight ? "leftRight" : "topBottom",
                        Ratio = Math.Round(split.Ratio, 4),
                        Children = new List<NodeModel>
                        {
                            split.First != null ? ToModel(split.First) : new NodeModel(),
                            split.Second != null ? ToModel(split.Second) : new NodeModel()
                        }
                    };

                case TabsNode tabs:
                    return new NodeModel
                    {
                        Type = "tabs",
                        Selected = tabs.SelectedIndex,
                        Children = tabs.Tabs.Select(ToModel).ToList()
                    };

                case ZoomPlaceholder zoom:
                    return ToModel(zoom.Cell);

                case CellControl cell:
                    return new NodeModel
                    {
                        Type = "cell",
                        Program = cell.ProgramPath,
                        // 복원을 기다리는 셀도 '배정됨'으로 저장해야, 복원 도중 종료해도 다음에 다시 시도함
                        Assigned = (cell.HasWindow || cell.RestorePending) && !string.IsNullOrEmpty(cell.ProgramPath),
                        Title = cell.HasWindow ? cell.DisplayTitle : cell.SavedTitle
                    };

                default:
                    return new NodeModel();
            }
        }

        // ── 불러오기 ────────────────────────────

        /// <summary>저장 파일을 읽습니다. 없거나 손상되었으면 null.</summary>
        public static LayoutFileModel? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var file = JsonSerializer.Deserialize<LayoutFileModel>(File.ReadAllText(FilePath), Options);
                return file?.Sets.Count > 0 ? file : null;
            }
            catch
            {
                return null;
            }
        }

        public static LayoutNode FromModel(NodeModel? model)
        {
            if (model == null) return new CellControl();

            switch (model.Type)
            {
                case "split" when model.Children is { Count: 2 }:
                {
                    var direction = model.Direction == "topBottom" ? SplitDirection.TopBottom : SplitDirection.LeftRight;
                    var split = new SplitNode(direction);
                    split.SetChildren(FromModel(model.Children[0]), FromModel(model.Children[1]));
                    split.Ratio = model.Ratio;
                    return split;
                }

                case "tabs" when model.Children is { Count: > 0 }:
                {
                    if (model.Children.Count == 1) return FromModel(model.Children[0]);
                    var tabs = new TabsNode();
                    foreach (var child in model.Children)
                        tabs.AddTab(FromModel(child), select: false);
                    tabs.Select(model.Selected);
                    return tabs;
                }

                default:
                {
                    var cell = new CellControl();
                    cell.RestoreProgramPath(model.Program, model.Assigned, model.Title);
                    return cell;
                }
            }
        }
    }
}
