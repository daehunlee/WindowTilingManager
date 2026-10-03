using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace WindowTilingManager
{
    /// <summary>
    /// 다국어 문자열.
    ///
    /// 언어 파일은 JSON 이며 두 곳에서 읽습니다.
    ///  1) 프로그램에 내장된 기본 언어 (ko, en, ja, zh)
    ///  2) 실행 파일 옆 Languages 폴더의 *.json — 같은 코드면 내장 파일보다 우선하고, 새 코드면 언어가 추가됩니다.
    ///
    /// 파일 이름(확장자 제외)이 언어 코드입니다 (예: fr.json → "fr").
    /// "_name" 키에 메뉴에 보일 언어 이름을 넣습니다 (예: "Français").
    /// 번역이 없는 키는 영어 → 한국어 → 키 이름 순서로 대신 표시합니다.
    /// </summary>
    public static class Loc
    {
        public sealed class LanguageInfo
        {
            public LanguageInfo(string code, string name, string source)
            {
                Code = code;
                Name = name;
                Source = source;
            }

            public string Code { get; }
            public string Name { get; }

            /// <summary>"내장" 또는 파일 경로.</summary>
            public string Source { get; }

            public override string ToString() => $"{Name} ({Code})";
        }

        private const string ResourcePrefix = "Languages.";
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LanguageInfo> Languages = new();
        private static Dictionary<string, string> _current = new();
        private static Dictionary<string, string> _english = new();
        private static Dictionary<string, string> _korean = new();

        /// <summary>현재 언어 코드 (예: "ko").</summary>
        public static string CurrentCode { get; private set; } = "en";

        /// <summary>사용할 수 있는 언어 목록.</summary>
        public static IReadOnlyList<LanguageInfo> Available => Languages;

        /// <summary>언어가 바뀌었을 때.</summary>
        public static event EventHandler? LanguageChanged;

        /// <summary>사용자가 언어 파일을 넣는 폴더 (실행 파일 옆 Languages).</summary>
        public static string LanguagesFolder => Path.Combine(AppContext.BaseDirectory, "Languages");

        /// <summary>
        /// 언어 파일을 읽고 언어를 고릅니다.
        /// preferred(저장된 설정)가 없거나 없는 언어면 Windows 표시 언어, 그것도 없으면 영어.
        /// </summary>
        public static void Initialize(string? preferred)
        {
            LoadAll();

            string? code = Find(preferred);
            if (code == null)
            {
                var culture = CultureInfo.CurrentUICulture;
                code = Find(culture.Name) ?? Find(culture.TwoLetterISOLanguageName);
            }
            code ??= Find("en") ?? Languages.FirstOrDefault()?.Code ?? "en";

            Apply(code, raiseEvent: false);
        }

        /// <summary>언어를 바꿉니다.</summary>
        public static void SetLanguage(string code)
        {
            string? found = Find(code);
            if (found == null || string.Equals(found, CurrentCode, StringComparison.OrdinalIgnoreCase)) return;
            Apply(found, raiseEvent: true);
        }

        /// <summary>언어 파일을 다시 읽습니다 (Languages 폴더에 파일을 추가한 뒤).</summary>
        public static void Reload()
        {
            string code = CurrentCode;
            LoadAll();
            Apply(Find(code) ?? Find("en") ?? code, raiseEvent: true);
        }

        /// <summary>번역된 문자열.</summary>
        public static string T(string key)
        {
            if (_current.TryGetValue(key, out var s) && s.Length > 0) return s;
            if (_english.TryGetValue(key, out s) && s.Length > 0) return s;
            if (_korean.TryGetValue(key, out s) && s.Length > 0) return s;
            return key;
        }

        /// <summary>번역된 문자열에 {0}, {1} ... 값을 채웁니다.</summary>
        public static string T(string key, params object?[] args)
        {
            string format = T(key);
            try { return string.Format(CultureInfo.CurrentCulture, format, args); }
            catch (FormatException) { return format; }
        }

        /// <summary>
        /// Languages 폴더를 만들고, 폴더에 없는 내장 언어 파일을 복사해 둡니다.
        /// 새 언어를 만들 때 en.json 을 복사해서 번역하면 됩니다.
        /// </summary>
        public static string EnsureLanguagesFolder()
        {
            string folder = LanguagesFolder;
            Directory.CreateDirectory(folder);
            var assembly = Assembly.GetExecutingAssembly();
            foreach (string resource in assembly.GetManifestResourceNames().Where(IsLanguageResource))
            {
                string fileName = resource.Substring(ResourcePrefix.Length);
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path)) continue;
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                using var file = File.Create(path);
                stream.CopyTo(file);
            }
            return folder;
        }

        // ─────────────────────────────────────────────

        private static void Apply(string code, bool raiseEvent)
        {
            CurrentCode = code;
            _current = Tables.TryGetValue(code, out var table) ? table : new Dictionary<string, string>();
            _english = Tables.TryGetValue("en", out var en) ? en : new Dictionary<string, string>();
            _korean = Tables.TryGetValue("ko", out var ko) ? ko : new Dictionary<string, string>();
            if (raiseEvent) LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>코드와 정확히 같거나, 앞부분이 같은 언어(예: "zh-CN" → "zh")를 찾습니다.</summary>
        private static string? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var exact = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Code;
            string head = code.Split('-', '_')[0];
            return Languages.FirstOrDefault(l => string.Equals(l.Code, head, StringComparison.OrdinalIgnoreCase))?.Code;
        }

        private static bool IsLanguageResource(string name) =>
            name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        private static void LoadAll()
        {
            Tables.Clear();
            Languages.Clear();
            var names = new Dictionary<string, (string Name, string Source)>(StringComparer.OrdinalIgnoreCase);

            // 1) 내장 언어
            var assembly = Assembly.GetExecutingAssembly();
            foreach (string resource in assembly.GetManifestResourceNames().Where(IsLanguageResource))
            {
                string code = Path.GetFileNameWithoutExtension(resource.Substring(ResourcePrefix.Length));
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                var table = Parse(stream);
                if (table == null) continue;
                Tables[code] = table;
                names[code] = (table.TryGetValue("_name", out var n) ? n : code, "built-in");
            }

            // 2) Languages 폴더 (같은 키는 덮어씀)
            try
            {
                if (Directory.Exists(LanguagesFolder))
                {
                    foreach (string path in Directory.GetFiles(LanguagesFolder, "*.json"))
                    {
                        string code = Path.GetFileNameWithoutExtension(path);
                        Dictionary<string, string>? table;
                        using (var stream = File.OpenRead(path)) table = Parse(stream);
                        if (table == null) continue;

                        if (Tables.TryGetValue(code, out var existing))
                        {
                            foreach (var pair in table) existing[pair.Key] = pair.Value;
                        }
                        else
                        {
                            Tables[code] = table;
                        }
                        names[code] = (table.TryGetValue("_name", out var n) ? n : names.TryGetValue(code, out var old) ? old.Name : code, path);
                    }
                }
            }
            catch
            {
                // 폴더를 읽지 못해도 내장 언어로 동작
            }

            foreach (var pair in names.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                Languages.Add(new LanguageInfo(pair.Key, pair.Value.Name, pair.Value.Source));
        }

        private static Dictionary<string, string>? Parse(Stream stream)
        {
            try
            {
                var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                using var doc = JsonDocument.Parse(stream, options);
                var table = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        table[prop.Name] = prop.Value.GetString() ?? "";
                }
                return table;
            }
            catch
            {
                return null;   // 잘못된 JSON 파일은 건너뜀
            }
        }
    }
}
