using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아이템 에셋 생성 · 복사 · 삭제. 아이템 편집기(새 아이템 / 복제 / 삭제 / CSV)가 모두 이 경로를 쓴다.
///
/// 주의: 세이브(ItemPouch.SaveToData)와 검증 스크립트가 아이템을 '에셋 파일 이름'으로 찾는다.
/// 그래서 이름 중복을 막고, 삭제할 때는 참조하는 곳을 먼저 보여준다.
/// </summary>
public static class ItemCreation
{
    public const string DefaultFolder = "Assets/SOData/Items";
    public static string DefaultFolderOrAssets => AssetDatabase.IsValidFolder(DefaultFolder) ? DefaultFolder : "Assets";

    public sealed class Request
    {
        public string assetName = "NewItem";
        public string folder = DefaultFolder;
        public ItemSO copyFrom;                  // null 이면 빈 아이템
        public string displayName = "새 아이템";
        public string description;               // null 이면 건드리지 않음(복사 시 원본 유지)
        public string shopDescription;
        public ItemTier tier = ItemTier.Tier4;
        public int maxStack = 1;
        public bool availableAsReward = true;
        public Type firstEffect;                 // 빈 아이템일 때 처음 넣을 효과 종류(없으면 null)
        public bool addToCatalog = true;
        public bool addToShop = true;
    }

    public static List<ItemSO> AllItems() => AssetDatabase.FindAssets("t:ItemSO")
        .Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g)))
        .Where(i => i != null).ToList();

    /// <summary>에셋 이름 검사. 문제 없으면 null.</summary>
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "에셋 파일 이름을 입력하세요.";
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "파일 이름에 쓸 수 없는 문자가 있습니다.";
        if (name.StartsWith(".") || name.EndsWith(".")) return "점(.)으로 시작하거나 끝날 수 없습니다.";
        if (AllItems().Any(i => string.Equals(i.name, name, StringComparison.OrdinalIgnoreCase)))
            return $"'{name}' 아이템이 이미 있습니다. 세이브가 에셋 이름으로 아이템을 찾으므로 겹치면 안 됩니다.";
        return null;
    }

    /// <summary>규칙상 문제는 없지만 권장하지 않는 이름이면 안내 문구.</summary>
    public static string NameHint(string name)
        => !string.IsNullOrEmpty(name) && name.Any(c => c > 127)
            ? "한글 등 영문 외 문자가 있습니다. 동작은 하지만 검증 스크립트·버전 관리에서 다루기 쉽게 영문(예: IronRing)을 권장합니다."
            : null;

    /// <summary>겹치지 않는 이름. 'NewItem' → 'NewItem2' …</summary>
    public static string UniqueName(string baseName)
    {
        var used = new HashSet<string>(AllItems().Select(i => i.name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName)) return baseName;
        for (int n = 2; ; n++) if (!used.Contains(baseName + n)) return baseName + n;
    }

    /// <summary>요청대로 아이템을 만든다. 실패하면 null 과 이유.</summary>
    public static ItemSO Create(Request r, out string error)
    {
        error = ValidateName(r.assetName);
        if (error != null) return null;
        string name = r.assetName.Trim();
        string folder = string.IsNullOrEmpty(r.folder) || !AssetDatabase.IsValidFolder(r.folder) ? DefaultFolderOrAssets : r.folder;
        string path = $"{folder}/{name}.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) { error = $"{path} 에 이미 파일이 있습니다."; return null; }

        ItemSO item;
        if (r.copyFrom != null)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(r.copyFrom), path)) { error = "복사에 실패했습니다."; return null; }
            item = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
        }
        else
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, path);
            if (r.firstEffect != null) item.effects.Add((ItemEffect)Activator.CreateInstance(r.firstEffect));
        }

        item.itemName = r.displayName ?? "";
        if (r.description != null) item.description = r.description;
        if (r.shopDescription != null) item.shopDescription = r.shopDescription;
        item.tier = r.tier;
        item.maxStack = Mathf.Max(1, r.maxStack);
        item.availableAsReward = r.availableAsReward;
        EditorUtility.SetDirty(item);

        var growth = ItemEditorGUI.Growth;
        var shop = ItemEditorGUI.Shop;
        if (r.addToCatalog && growth != null) ItemEditorGUI.Toggle(growth, growth.items, item, true);
        if (r.addToShop && shop != null) ItemEditorGUI.Toggle(shop, shop.itemPool, item, true);

        AssetDatabase.SaveAssets();
        return item;
    }

    // ── 삭제 ──────────────────────────────────────────────────────────
    /// <summary>참조 검사 → 확인 → 목록에서 제거 → 에셋을 OS 휴지통으로. 지웠으면 true.</summary>
    public static bool Delete(ItemSO item)
    {
        if (item == null) return false;
        var growth = ItemEditorGUI.Growth;
        var shop = ItemEditorGUI.Shop;
        if (growth != null && growth.startingItem == item)
        {
            EditorUtility.DisplayDialog("아이템 삭제", $"'{item.DisplayName}' 은(는) 새 게임 기본 지급 아이템이라 지울 수 없습니다.\n먼저 {growth.name} 의 시작 아이템을 바꾸세요.", "확인");
            return false;
        }

        string path = AssetDatabase.GetAssetPath(item);
        var skip = new HashSet<string> { path };
        if (growth != null) skip.Add(AssetDatabase.GetAssetPath(growth));
        if (shop != null) skip.Add(AssetDatabase.GetAssetPath(shop));
        if (!FindReferences(item, skip, out var assetRefs, out var codeRefs)) return false; // 검사 취소

        var msg = new StringBuilder();
        msg.AppendLine($"'{item.DisplayName}' ({item.name}) 을(를) 삭제합니다.");
        msg.AppendLine("에셋은 OS 휴지통으로 이동합니다(휴지통에서 복구 가능).\n");
        if (growth != null && growth.items.Contains(item)) msg.AppendLine($"· {growth.name} 아이템 목록에서 자동으로 뺍니다.");
        if (shop != null && shop.itemPool.Contains(item)) msg.AppendLine($"· {shop.name} 상점 후보에서 자동으로 뺍니다.");
        if (assetRefs.Count > 0)
        {
            msg.AppendLine("\n다른 에셋·씬·프리팹이 참조 중입니다(삭제 후 빈 칸이 됩니다):");
            foreach (var p in assetRefs.Take(8)) msg.AppendLine("  · " + p);
            if (assetRefs.Count > 8) msg.AppendLine($"  … 외 {assetRefs.Count - 8}곳");
        }
        if (codeRefs.Count > 0)
        {
            msg.AppendLine("\n코드에 이 아이템 이름이 적혀 있습니다(함께 고쳐야 할 수 있음):");
            foreach (var p in codeRefs.Take(8)) msg.AppendLine("  · " + p);
        }
        msg.AppendLine("\n이미 세이브에 들어 있는 아이템이면 불러올 때 사라집니다.");

        if (!EditorUtility.DisplayDialog("아이템 삭제", msg.ToString(), "삭제", "취소")) return false;

        if (growth != null && growth.items.Remove(item)) { growth.items.RemoveAll(x => x == item); EditorUtility.SetDirty(growth); }
        if (shop != null && shop.itemPool.Remove(item)) { shop.itemPool.RemoveAll(x => x == item); EditorUtility.SetDirty(shop); }
        AssetDatabase.SaveAssets();

        if (!AssetDatabase.MoveAssetToTrash(path))
        {
            EditorUtility.DisplayDialog("아이템 삭제", "휴지통으로 옮기지 못했습니다. 목록에서는 이미 빠졌으니 Project 창에서 직접 지워 주세요.", "확인");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 이 아이템을 GUID 로 참조하는 에셋/씬/프리팹과, 이름을 문자열로 적은 C# 파일을 찾는다.
    /// 사용자가 진행 표시줄에서 취소하면 false.
    /// </summary>
    public static bool FindReferences(ItemSO item, HashSet<string> skip, out List<string> assetRefs, out List<string> codeRefs)
    {
        assetRefs = new List<string>();
        codeRefs = new List<string>();
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item));
        string quoted = "\"" + item.name + "\"";
        string root = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";

        var files = Directory.EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".prefab") || f.EndsWith(".unity") || f.EndsWith(".asset") || f.EndsWith(".cs"))
            .ToList();
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i].Replace('\\', '/').Replace(root, "");
                if (i % 40 == 0 && EditorUtility.DisplayCancelableProgressBar("아이템 참조 검사", rel, (float)i / files.Count)) return false;
                if (skip.Contains(rel)) continue;
                string text;
                try { text = File.ReadAllText(files[i]); } catch { continue; }
                if (rel.EndsWith(".cs")) { if (text.Contains(quoted)) codeRefs.Add(rel); }
                else if (text.Contains(guid)) assetRefs.Add(rel);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        return true;
    }
}

/// <summary>새 아이템 / 아이템 복제 창.</summary>
public class NewItemPopup : EditorWindow
{
    private ItemCreation.Request req;
    private Action<ItemSO> onCreated;
    private List<ItemSO> sources;
    private int mode;          // 0 = 빈 아이템, 1 = 기존 아이템 복사
    private int sourceIndex;
    private int effectIndex;   // 0 = 없음
    private string error;

    public static void Open(ItemSO copyFrom, Action<ItemSO> onCreated)
    {
        var w = CreateInstance<NewItemPopup>();
        w.titleContent = new GUIContent(copyFrom != null ? "아이템 복제" : "새 아이템");
        w.onCreated = onCreated;
        w.sources = ItemCreation.AllItems().OrderBy(i => (int)i.tier).ThenBy(i => i.DisplayName, StringComparer.Ordinal).ToList();
        w.req = new ItemCreation.Request
        {
            folder = ItemCreation.DefaultFolderOrAssets,
            assetName = ItemCreation.UniqueName("NewItem"),
        };
        w.effectIndex = 1 + ItemEditorGUI.EffectTypes.ToList().IndexOf(typeof(ItemStatEffect)); // 가장 흔한 '능력치 보정'
        if (copyFrom != null)
        {
            w.mode = 1;
            w.sourceIndex = Mathf.Max(0, w.sources.IndexOf(copyFrom));
            w.ApplySource();
        }
        w.minSize = w.maxSize = new Vector2(460, 470);
        w.ShowUtility();
    }

    private void ApplySource()
    {
        if (sources.Count == 0) { mode = 0; return; }
        var src = sources[Mathf.Clamp(sourceIndex, 0, sources.Count - 1)];
        req.folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(src))?.Replace('\\', '/') ?? ItemCreation.DefaultFolderOrAssets;
        req.assetName = ItemCreation.UniqueName(src.name + "Copy");
        req.displayName = src.DisplayName + " (사본)";
        req.tier = src.tier;
        req.maxStack = src.MaxStack;
        req.availableAsReward = src.availableAsReward;
    }

    private void OnGUI()
    {
        if (req == null) { Close(); return; }
        EditorGUIUtility.labelWidth = 130;
        EditorGUILayout.Space(6);

        int newMode = GUILayout.Toolbar(mode, new[] { "빈 아이템으로 시작", "기존 아이템 복사" });
        if (newMode != mode) { mode = newMode; if (mode == 1) ApplySource(); }
        EditorGUILayout.Space(4);

        if (mode == 1)
        {
            if (sources.Count == 0) { EditorGUILayout.HelpBox("복사할 아이템이 없습니다.", MessageType.Info); mode = 0; }
            else
            {
                var labels = sources.Select(s => $"{s.DisplayName}  ({s.name})").ToArray();
                int idx = EditorGUILayout.Popup(new GUIContent("복사할 아이템", "효과·설명·아이콘을 그대로 가져온다."), sourceIndex, labels);
                if (idx != sourceIndex) { sourceIndex = idx; ApplySource(); }
            }
        }

        ItemEditorGUI.Header("이름");
        req.assetName = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름", "세이브와 검증 스크립트가 이 이름으로 아이템을 찾는다. 나중에 바꾸기 어렵다."), req.assetName);
        string nameError = ItemCreation.ValidateName(req.assetName);
        if (nameError != null) EditorGUILayout.HelpBox(nameError, MessageType.Error);
        else if (ItemCreation.NameHint(req.assetName) is string hint) EditorGUILayout.HelpBox(hint, MessageType.Info);
        req.displayName = EditorGUILayout.TextField(new GUIContent("게임 표시 이름"), req.displayName);

        ItemEditorGUI.Header("기본 값");
        req.tier = (ItemTier)EditorGUILayout.EnumPopup(new GUIContent("등급", "가격·색은 등급에서 자동"), req.tier);
        req.maxStack = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("최대 보유 수"), req.maxStack));
        req.availableAsReward = EditorGUILayout.Toggle(new GUIContent("상점 · 방 보상에 등장"), req.availableAsReward);
        if (mode == 0)
        {
            var names = new[] { "없음 (나중에 추가)" }.Concat(ItemEditorGUI.EffectTypes.Select(ItemEditorGUI.TypeName)).ToArray();
            effectIndex = EditorGUILayout.Popup(new GUIContent("첫 효과", "만들면서 효과 한 칸을 넣어 둔다. 수치는 만든 뒤 편집기에서 정한다."), Mathf.Clamp(effectIndex, 0, names.Length - 1), names);
        }

        ItemEditorGUI.Header("저장 위치 · 등록");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("폴더"), new GUIContent(req.folder));
            if (GUILayout.Button("변경…", GUILayout.Width(56)))
            {
                string abs = EditorUtility.OpenFolderPanel("아이템 저장 폴더", req.folder, "");
                string data = Application.dataPath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(abs))
                {
                    abs = abs.Replace('\\', '/');
                    if (abs == data || abs.StartsWith(data + "/")) req.folder = "Assets" + abs.Substring(data.Length);
                    else EditorUtility.DisplayDialog("폴더", "프로젝트의 Assets 폴더 안을 골라 주세요.", "확인");
                }
                GUIUtility.ExitGUI();
            }
        }
        var growth = ItemEditorGUI.Growth;
        var shop = ItemEditorGUI.Shop;
        using (new EditorGUI.DisabledScope(growth == null))
            req.addToCatalog = EditorGUILayout.ToggleLeft(new GUIContent("아이템 목록에 등록" + (growth != null ? $" ({growth.name})" : " (목록 에셋 없음)"),
                "보상 추첨과 세이브 불러오기에 쓰는 전체 목록. 빠지면 게임에 나오지 않는다."), req.addToCatalog && growth != null);
        using (new EditorGUI.DisabledScope(shop == null))
            req.addToShop = EditorGUILayout.ToggleLeft(new GUIContent("상점 진열 후보에 등록" + (shop != null ? $" ({shop.name})" : " (상점 에셋 없음)")), req.addToShop && shop != null);

        if (mode == 0 && (req.addToCatalog || req.addToShop))
            EditorGUILayout.HelpBox("등록하면 바로 게임 보상·상점에 나올 수 있습니다. 효과 수치와 설명을 채우기 전까지는 '상점 · 방 보상에 등장'을 꺼 두는 것도 방법입니다.", MessageType.None);
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("취소", GUILayout.Width(80))) { Close(); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(nameError != null))
                if (GUILayout.Button(mode == 1 ? "복제" : "만들기", GUILayout.Width(100)))
                {
                    req.copyFrom = mode == 1 && sources.Count > 0 ? sources[sourceIndex] : null;
                    req.firstEffect = mode == 0 && effectIndex > 0 ? ItemEditorGUI.EffectTypes[effectIndex - 1] : null;
                    var item = ItemCreation.Create(req, out error);
                    if (item != null)
                    {
                        var callback = onCreated;
                        Close();
                        callback?.Invoke(item);
                    }
                    GUIUtility.ExitGUI();
                }
        }
        EditorGUILayout.Space(6);
    }
}
