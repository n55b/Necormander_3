using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// 번역 표 갱신. 한국어 원문을 모아 "Game Text Table"의 ko 열에 올린다(다른 언어 열은 건드리지 않는다).
///  - SO의 LocText: 키가 비었거나 다른 칸과 겹치면 "에셋이름.필드경로"로 키를 채운다.
///  - 코드의 Loc.T/Loc.F("키", "한국어") 리터럴.
///  - 프리팹·씬의 LocTMP(키 + 같은 오브젝트의 TMP 글자)와 컴포넌트 안의 LocText. 이 둘의 키는 직접 넣는다
///    (키 없는 문구는 메뉴 실행 요약에 나온다).
/// 에디터를 켤 때 한 번 자동으로 돈다(지우지 않음). 메뉴로 돌리면 더 이상 쓰지 않는 키도 지운다.
/// 번역(영어 등)은 Window > Asset Management > Localization Tables 또는 CSV/Google Sheets로 채운다.
/// </summary>
[InitializeOnLoad]
public static class LocSync
{
    const string SessionKey = "LocSync.AutoRan";
    const string LocTmpGuid = "27cf918a1fb84821b0925764280ac86a";

    static LocSync()
    {
        if (Application.isBatchMode || SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.SetBool(SessionKey, false); return; }
            Run(false);
        };
    }

    [MenuItem("Tools/번역/번역 표 갱신")]
    static void RunFromMenu() => Run(true);

    sealed class Item { public SerializedObject so; public string assetName, path, ko, key; }

    // 번역 표에 못 올라가는 것들(요약 로그에 보인다). Sync마다 비운다.
    static readonly List<string> s_Keyless = new List<string>();  // 한국어는 있는데 키가 없는 문구
    static readonly List<string> s_BadCalls = new List<string>(); // "키", "한국어" 리터럴 두 개가 아닌 Loc.T/Loc.F

    static void Run(bool manual)
    {
        try { Sync(manual); }
        catch (Exception e) { Debug.LogException(e); }
    }

    static void Sync(bool manual)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(Loc.TableName);
        var koTable = collection != null ? collection.GetTable("ko") as StringTable : null;
        if (koTable == null)
        {
            if (manual) Debug.LogWarning($"[번역] '{Loc.TableName}'의 ko 표를 찾지 못했습니다.");
            return;
        }

        var texts = new Dictionary<string, string>();
        var conflicts = new List<string>();
        var memo = new Dictionary<Type, bool>();
        s_Keyless.Clear();
        s_BadCalls.Clear();
        int codeCount = CollectCode(texts, conflicts);
        int yamlCount = CollectYaml(texts, conflicts, memo);
        int soCount = CollectAssets(texts, conflicts, memo, collection.SharedData, out int rekeyed);

        int added = 0, updated = 0, removed = 0;
        foreach (var kv in texts)
        {
            var entry = koTable.GetEntry(kv.Key);
            if (entry == null) { koTable.AddEntry(kv.Key, kv.Value); added++; }
            else if (entry.Value != kv.Value) { entry.Value = kv.Value; updated++; }
        }

        if (manual)
        {
            var unused = collection.SharedData.Entries.Where(e => !texts.ContainsKey(e.Key)).Select(e => e.Key).ToList();
            if (unused.Count > 0 && EditorUtility.DisplayDialog("번역 표 갱신",
                    $"더 이상 쓰지 않는 키 {unused.Count}개를 모든 언어에서 지웁니다.\n\n" + string.Join("\n", unused.Take(15)) + (unused.Count > 15 ? "\n..." : ""),
                    "지우기", "남겨 두기"))
            {
                foreach (var k in unused) collection.RemoveEntry(k);
                removed = unused.Count;
            }
        }

        if (added + updated + removed > 0)
        {
            EditorUtility.SetDirty(koTable);
            if (added + removed > 0) EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            foreach (var table in collection.StringTables)
            {
                if (removed > 0) EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssetIfDirty(table);
            }
        }

        bool changed = added + updated + removed + rekeyed > 0;
        if (!manual && !changed && conflicts.Count == 0 && s_BadCalls.Count == 0) return;
        string summary = $"[번역] 번역 표 갱신: 키 {texts.Count}개 (SO {soCount}, 코드 {codeCount}, 프리팹·씬 {yamlCount}) / 추가 {added}, 한국어 수정 {updated}, 삭제 {removed}, SO 키 부여 {rekeyed}";
        if (conflicts.Count > 0)
            summary += $"\n같은 키에 다른 한국어가 {conflicts.Count}건 있습니다(처음 것을 씀):\n" + string.Join("\n", conflicts);
        if (s_BadCalls.Count > 0)
            summary += $"\n\"키\", \"한국어\" 문자열 리터럴 두 개가 아닌 Loc.T/Loc.F 호출 {s_BadCalls.Count}건(번역 표에 안 올라가거나 한국어가 잘림):\n" + string.Join("\n", s_BadCalls);
        // 자동 실행에서는 뺀다(은퇴한 가드 줄처럼 일부러 키를 안 둔 문구가 매번 뜨지 않게).
        if (manual && s_Keyless.Count > 0)
            summary += $"\n키가 없어 번역되지 않는 문구 {s_Keyless.Count}건(프리팹·씬은 인스펙터의 '번역 키' 칸에 넣는다):\n" + string.Join("\n", s_Keyless);
        if (conflicts.Count + s_BadCalls.Count > 0) Debug.LogWarning(summary);
        else Debug.Log(summary);
    }

    static void Add(Dictionary<string, string> texts, List<string> conflicts, string key, string ko, string where)
    {
        if (string.IsNullOrEmpty(ko)) return;
        if (string.IsNullOrEmpty(key)) { s_Keyless.Add($"{where}: \"{ko}\""); return; }
        if (!texts.TryGetValue(key, out var old)) texts[key] = ko;
        else if (old != ko) conflicts.Add($"{key}: \"{old}\" / \"{ko}\" ({where})");
    }

    // ── (a) ScriptableObject의 LocText ─────────────────────────────────────

    static int CollectAssets(Dictionary<string, string> texts, List<string> conflicts, Dictionary<Type, bool> memo, SharedTableData shared, out int rekeyed)
    {
        var guids = new HashSet<string>();
        foreach (var t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            if (t.Assembly == typeof(LocText).Assembly && !t.IsAbstract && !t.IsGenericType && Has(t, memo))
                guids.UnionWith(AssetDatabase.FindAssets("t:" + t.Name, new[] { "Assets" }));

        var items = new List<Item>();
        foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (asset == null) continue;
            var so = new SerializedObject(asset);
            string assetName = Path.GetFileNameWithoutExtension(path);
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                bool loc = it.propertyType == SerializedPropertyType.Generic && it.type == nameof(LocText);
                enter = !loc && (it.propertyType == SerializedPropertyType.Generic || it.propertyType == SerializedPropertyType.ManagedReference);
                if (!loc) continue;
                string ko = it.FindPropertyRelative("ko").stringValue;
                if (string.IsNullOrEmpty(ko)) continue;
                items.Add(new Item { so = so, assetName = assetName, path = it.propertyPath, ko = ko, key = it.FindPropertyRelative("key").stringValue });
            }
        }

        // 겹친 키는 에셋 이름이 키 앞부분과 맞는 칸(없으면 이름이 가장 짧은 칸 = 복제본 "X 1"이 아닌 원본)만 남기고
        // 나머지는 새 키를 받는다. 표에만 남은 키(지운 에셋의 번역)도 피해서, 새 칸이 옛 번역을 물려받지 않게 한다.
        var used = new HashSet<string>(items.Select(x => x.key).Where(k => !string.IsNullOrEmpty(k)));
        used.UnionWith(texts.Keys);
        used.UnionWith(shared.Entries.Select(e => e.Key));
        var keep = new HashSet<Item>(items.Where(x => !string.IsNullOrEmpty(x.key))
            .GroupBy(x => x.key)
            .Select(g => g.FirstOrDefault(x => x.key == SchemeKey(x)) ?? g.FirstOrDefault(x => x.key.StartsWith(x.assetName + ".", StringComparison.Ordinal))
                ?? g.OrderBy(x => x.assetName.Length).First()));
        var dirty = new HashSet<SerializedObject>();
        rekeyed = 0;
        foreach (var x in items)
        {
            if (!string.IsNullOrEmpty(x.key) && keep.Contains(x)) continue;
            string key = SchemeKey(x);
            for (int n = 2; used.Contains(key); n++) key = SchemeKey(x) + "_" + n;
            used.Add(key);
            x.key = key;
            x.so.FindProperty(x.path + ".key").stringValue = key;
            dirty.Add(x.so);
            rekeyed++;
        }
        foreach (var so in dirty)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so.targetObject);
            AssetDatabase.SaveAssetIfDirty(so.targetObject);
        }

        foreach (var x in items) Add(texts, conflicts, x.key, x.ko, x.assetName);
        return items.Count;
    }

    static string SchemeKey(Item x) => x.assetName + "." + x.path.Replace(".Array.data[", "[");

    // 이 타입(또는 그 안의 직렬화 필드, [SerializeReference] 하위 타입)에 LocText가 있나.
    static bool Has(Type type, Dictionary<Type, bool> memo)
    {
        if (type == typeof(LocText)) return true;
        if (type.IsArray) return Has(type.GetElementType(), memo);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return Has(type.GetGenericArguments()[0], memo);
        if (type.Assembly != typeof(LocText).Assembly || type.IsEnum) return false;
        if (memo.TryGetValue(type, out bool known)) return known;
        memo[type] = false; // 순환 참조 방지
        bool found = false;
        for (var t = type; t != null && !found && t.Assembly == typeof(LocText).Assembly; t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var ft = f.FieldType.IsArray ? f.FieldType.GetElementType()
                    : f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>) ? f.FieldType.GetGenericArguments()[0] : f.FieldType;
                if (typeof(UnityEngine.Object).IsAssignableFrom(ft)) continue;
                if (Has(ft, memo) || (f.IsDefined(typeof(SerializeReference)) && TypeCache.GetTypesDerivedFrom(ft).Any(d => Has(d, memo))))
                { found = true; break; }
            }
        return memo[type] = found;
    }

    // ── (b) 코드의 Loc.T / Loc.F 리터럴 ─────────────────────────────────────

    // 한국어 리터럴 뒤가 ',' 또는 ')'여야 한다("앞" + "뒤"처럼 이어 붙이면 잘린 한국어가 올라가므로 받지 않는다).
    static readonly Regex CodeCall = new Regex(@"\bLoc\.[TF]\(\s*""((?:[^""\\\r\n]|\\.)*)""\s*,\s*""((?:[^""\\\r\n]|\\.)*)""(?=\s*[,)])", RegexOptions.Compiled);
    static readonly Regex AnyCall = new Regex(@"\bLoc\.[TF]\(", RegexOptions.Compiled);

    static int CollectCode(Dictionary<string, string> texts, List<string> conflicts)
    {
        int count = 0;
        foreach (var file in Directory.EnumerateFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            if (src.IndexOf("Loc.", StringComparison.Ordinal) < 0) continue;
            var matched = new HashSet<int>();
            foreach (Match m in CodeCall.Matches(src))
            {
                matched.Add(m.Index);
                Add(texts, conflicts, UnescapeCSharp(m.Groups[1].Value), UnescapeCSharp(m.Groups[2].Value), Path.GetFileName(file));
                count++;
            }
            // Loc/LocText/LocTMP 자체는 변수로 부른다.
            if (Path.GetDirectoryName(file).Replace('\\', '/') == "Assets/Scripts/Localization") continue;
            foreach (Match m in AnyCall.Matches(src))
                if (!matched.Contains(m.Index))
                    s_BadCalls.Add($"{Path.GetFileName(file)}:{src.Take(m.Index).Count(c => c == '\n') + 1}");
        }
        return count;
    }

    // ── (c) 프리팹·씬 YAML: LocTMP + 같은 오브젝트의 TMP m_text, 컴포넌트 안의 LocText {ko, key} ──

    static int CollectYaml(Dictionary<string, string> texts, List<string> conflicts, Dictionary<Type, bool> memo)
    {
        // LocTMP나 LocText 필드를 가진 컴포넌트를 쓰는 프리팹·씬만 읽는다(전부 읽으면 100MB가 넘는다).
        var scripts = new HashSet<string>(MonoImporter.GetAllRuntimeMonoScripts()
            .Where(s => s.GetClass() is Type c && typeof(MonoBehaviour).IsAssignableFrom(c) && (c == typeof(LocTMP) || Has(c, memo)))
            .Select(AssetDatabase.GetAssetPath));
        var folders = new[] { "Assets" };
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders).Concat(AssetDatabase.FindAssets("t:Scene", folders)).Distinct())
        {
            string file = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.GetDependencies(file, false).Any(scripts.Contains)) continue;
            count += ScanYaml(File.ReadAllText(file), texts, conflicts, Path.GetFileNameWithoutExtension(file));
        }
        return count;
    }

    static readonly Regex KoLine = new Regex(@"^( *)(- )?ko: (.*)$", RegexOptions.Compiled);
    static readonly Regex GameObjectRef = new Regex(@"^  m_GameObject: \{fileID: (-?\d+)\}", RegexOptions.Compiled | RegexOptions.Multiline);

    static int ScanYaml(string text, Dictionary<string, string> texts, List<string> conflicts, string where)
    {
        int count = 0;
        text = text.Replace("\r", "");
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = KoLine.Match(lines[i]);
            if (!m.Success) continue;
            int col = m.Groups[1].Length + m.Groups[2].Length;
            string ko = ReadScalar(lines, ref i, col, m.Groups[3].Value);
            string keyPrefix = new string(' ', col) + "key: ";
            if (i + 1 >= lines.Length || !lines[i + 1].StartsWith(keyPrefix, StringComparison.Ordinal)) continue;
            i++;
            string key = ReadScalar(lines, ref i, col, lines[i].Substring(keyPrefix.Length));
            Add(texts, conflicts, key, ko, where);
            count++;
        }
        if (text.Contains(LocTmpGuid)) count += CollectLocTmp(text, texts, conflicts, where);
        return count;
    }

    static int CollectLocTmp(string text, Dictionary<string, string> texts, List<string> conflicts, string where)
    {
        var keys = new Dictionary<string, string>();   // GameObject fileID -> LocTMP key
        var labels = new Dictionary<string, string>(); // GameObject fileID -> TMP m_text
        foreach (var doc in text.Split(new[] { "\n--- " }, StringSplitOptions.None))
        {
            var go = GameObjectRef.Match(doc);
            if (!go.Success) continue;
            bool isLocTmp = doc.Contains(LocTmpGuid);
            string field = isLocTmp ? "  key: " : "  m_text: ";
            var lines = doc.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(field, StringComparison.Ordinal)) continue;
                string value = ReadScalar(lines, ref i, 2, lines[i].Substring(field.Length));
                (isLocTmp ? keys : labels)[go.Groups[1].Value] = value;
                break;
            }
        }
        int count = 0;
        foreach (var kv in keys)
            if (labels.TryGetValue(kv.Key, out var ko)) { Add(texts, conflicts, kv.Value, ko, where); count++; }
        return count;
    }

    #region 순수 문자열 처리 (YAML 스칼라, C# 이스케이프)

    /// <summary>
    /// YAML 스칼라 하나를 읽는다(평문/작은따옴표/큰따옴표, 여러 줄 접기 포함). first는 "필드: " 뒤의 글자,
    /// col은 필드 이름이 시작하는 열. i는 스칼라의 마지막 줄로 옮겨진다.
    /// </summary>
    public static string ReadScalar(string[] lines, ref int i, int col, string first)
    {
        if (first.Length > 0 && (first[0] == '"' || first[0] == '\''))
        {
            char q = first[0];
            var sb = new StringBuilder(first);
            int end;
            while ((end = ClosingQuote(sb.ToString(), q)) < 0 && i + 1 < lines.Length) sb.Append('\n').Append(lines[++i]);
            string all = sb.ToString();
            if (end < 0) end = all.Length;
            string inner = all.Substring(1, end - 1);
            return q == '"' ? UnescapeYaml(Fold(inner, true)) : Fold(inner, false).Replace("''", "'");
        }
        var raw = new List<string> { first };
        while (i + 1 < lines.Length && (lines[i + 1].Trim(' ', '\t').Length == 0 || Indent(lines[i + 1]) > col)) raw.Add(lines[++i]);
        while (raw.Count > 1 && raw[raw.Count - 1].Trim(' ', '\t').Length == 0) raw.RemoveAt(raw.Count - 1);
        return Fold(string.Join("\n", raw), false).Trim(' ', '\t');
    }

    static int Indent(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    static int ClosingQuote(string s, char q)
    {
        for (int k = 1; k < s.Length; k++)
        {
            if (q == '"' && s[k] == '\\') { k++; continue; }
            if (s[k] != q) continue;
            if (q == '\'' && k + 1 < s.Length && s[k + 1] == '\'') { k++; continue; }
            return k;
        }
        return -1;
    }

    // 흐름 스칼라의 줄 접기: 줄바꿈 하나는 공백, 빈 줄 n개는 줄바꿈 n개. 큰따옴표 안의 줄 끝 '\'는 공백 없이 잇는다.
    static string Fold(string inner, bool doubleQuoted)
    {
        var parts = inner.Split('\n');
        var sb = new StringBuilder();
        int empties = 0;
        bool glue = false;
        for (int k = 0; k < parts.Length; k++)
        {
            string s = parts[k];
            bool last = k == parts.Length - 1;
            if (k > 0) s = s.TrimStart(' ', '\t');
            bool escapedBreak = false;
            if (!last)
            {
                int slashes = 0;
                while (slashes < s.Length && s[s.Length - 1 - slashes] == '\\') slashes++;
                escapedBreak = doubleQuoted && slashes % 2 == 1;
                s = escapedBreak ? s.Substring(0, s.Length - 1) : s.TrimEnd(' ', '\t');
            }
            if (k > 0)
            {
                if (s.Length == 0 && !last && !escapedBreak) { empties++; continue; }
                if (empties > 0) sb.Append('\n', empties);
                else if (!glue) sb.Append(' ');
                empties = 0;
            }
            sb.Append(s);
            glue = escapedBreak;
        }
        return sb.ToString();
    }

    public static string UnescapeYaml(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int k = 0; k < s.Length; k++)
        {
            char c = s[k];
            if (c != '\\' || k + 1 >= s.Length) { sb.Append(c); continue; }
            char e = s[++k];
            switch (e)
            {
                case '0': sb.Append((char)0); break;
                case 'a': sb.Append((char)7); break;
                case 'b': sb.Append((char)8); break;
                case 't': case '\t': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'v': sb.Append((char)0x0B); break;
                case 'f': sb.Append((char)0x0C); break;
                case 'r': sb.Append('\r'); break;
                case 'e': sb.Append((char)0x1B); break;
                case 'N': sb.Append((char)0x85); break;
                case '_': sb.Append((char)0xA0); break;
                case 'L': sb.Append((char)0x2028); break;
                case 'P': sb.Append((char)0x2029); break;
                case 'x': sb.Append(Hex(s, ref k, 2)); break;
                case 'u': sb.Append(Hex(s, ref k, 4)); break;
                case 'U': sb.Append(Hex(s, ref k, 8)); break;
                default: sb.Append(e); break; // 따옴표, 역슬래시, 슬래시, 공백
            }
        }
        return sb.ToString();
    }

    public static string UnescapeCSharp(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int k = 0; k < s.Length; k++)
        {
            char c = s[k];
            if (c != '\\' || k + 1 >= s.Length) { sb.Append(c); continue; }
            char e = s[++k];
            switch (e)
            {
                case '0': sb.Append((char)0); break;
                case 'a': sb.Append((char)7); break;
                case 'b': sb.Append((char)8); break;
                case 'f': sb.Append((char)0x0C); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append((char)0x0B); break;
                case 'u': sb.Append(Hex(s, ref k, 4)); break;
                case 'U': sb.Append(Hex(s, ref k, 8)); break;
                case 'x':
                    int n = 0;
                    while (n < 4 && k + 1 + n < s.Length && Uri.IsHexDigit(s[k + 1 + n])) n++;
                    sb.Append(Hex(s, ref k, n));
                    break;
                default: sb.Append(e); break; // 따옴표, 역슬래시
            }
        }
        return sb.ToString();
    }

    // s[k]가 이스케이프 글자(u, x 등)일 때 뒤의 16진수 digits자리를 읽어 문자로 바꾸고 k를 옮긴다.
    static string Hex(string s, ref int k, int digits)
    {
        if (digits == 0 || k + digits >= s.Length || !s.Substring(k + 1, digits).All(Uri.IsHexDigit)) return "\\" + s[k];
        int code = Convert.ToInt32(s.Substring(k + 1, digits), 16);
        k += digits;
        // 서로게이트 반쪽(이모지의 앞뒤 절반)이 따로 오면 그대로 이어 붙인다.
        return code <= 0xFFFF ? ((char)code).ToString() : code <= 0x10FFFF ? char.ConvertFromUtf32(code) : ((char)0xFFFD).ToString();
    }

    #endregion
}
