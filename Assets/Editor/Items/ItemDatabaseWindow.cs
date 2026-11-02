using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기획자용 아이템 편집기 (Tools > Items > 아이템 편집기).
///
///  · 표로 보기   — 모든 아이템의 이름/등급/중첩/설명/효과를 한 화면에서 고친다.
///  · 하나씩 편집 — 왼쪽 목록에서 고르고 오른쪽에서 아이템 인스펙터와 같은 화면으로 고친다.
///  · 확인 필요만 — 설명 문구에 실제 효과 수치가 없거나 효과가 빈 아이템만 본다.
///  · CSV 내보내기/가져오기 — 이름·등급·중첩·설명을 엑셀/구글 시트에서 일괄 수정한다(효과는 이 창에서).
///
/// 모든 수정은 Undo(Ctrl+Z) 가 된다. 아이템을 찾는 키는 에셋 파일 이름이다(세이브도 같은 키).
/// </summary>
public class ItemDatabaseWindow : EditorWindow
{
    private enum View { Table, Detail }

    [SerializeField] private View view = View.Table;
    [SerializeField] private string search = "";
    [SerializeField] private int tierFilter;          // 0 = 전체, 1~4 = 해당 등급
    [SerializeField] private bool onlyWarnings;
    [SerializeField] private ItemSO selected;

    private List<ItemSO> items = new List<ItemSO>();
    private readonly Dictionary<ItemSO, SerializedObject> serialized = new Dictionary<ItemSO, SerializedObject>();
    private Vector2 tableScroll, listScroll, detailScroll;
    private string renameText;
    private ItemSO renameTarget;

    // 편집 중인 글이 다른 아이템으로 옮겨 붙는 것을 막기 위한 상태(OnGUI 참고).
    private int itemsVersion, undoVersion;
    private string lastContext, lastFilter;
    private List<ItemSO> visible;

    private const string DefaultFolder = "Assets/SOData/Items";
    private static readonly string[] TierOptions = { "전체 등급", "등급 1", "등급 2", "등급 3", "등급 4" };

    private static readonly (string title, float width, string tip)[] Columns =
    {
        ("", 52, ""),
        ("이름 / 에셋", 140, "게임 표시 이름 / 에셋 파일 이름(세이브 키)"),
        ("등급 / 가격", 88, "가격은 등급에서 자동으로 정해진다"),
        ("최대", 46, "최대 보유 수"),
        ("보상", 34, "상점 · 방 보상에 등장"),
        ("설명", 250, "툴팁 · 보상 카드 · 주머니 · 능력창에 표시"),
        ("상점 카드 설명", 170, "비우면 설명을 그대로 쓴다"),
        ("효과 (수치 · 표시 문구)", 340, "표시 문구를 비우면 자동 문구"),
        ("주머니 표시 (1개) · 확인", 230, "게임에서 보이는 효과 문구와 확인 필요 항목"),
    };

    // ── 열기 ──────────────────────────────────────────────────────────
    [MenuItem("Tools/Items/아이템 편집기", priority = 0)]
    public static void Open() => Open(null);

    public static void Open(ItemSO select)
    {
        var w = GetWindow<ItemDatabaseWindow>();
        w.titleContent = new GUIContent("아이템 편집기");
        w.minSize = new Vector2(720, 400);
        w.Reload();
        if (select != null) { w.selected = select; w.view = View.Detail; }
        w.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("아이템 편집기");
        Reload();
        EditorApplication.projectChanged += OnProjectChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= OnProjectChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        ClearSerialized();
    }

    private void OnProjectChanged() { Reload(); Repaint(); }

    // 되돌리기 후에도 입력 중이던 글 칸이 옛 글을 들고 있다가 다시 써넣지 않도록 편집을 닫는다.
    private void OnUndoRedo() { undoVersion++; Repaint(); }

    private void ClearSerialized()
    {
        foreach (var so in serialized.Values) so?.Dispose();
        serialized.Clear();
    }

    private void Reload()
    {
        ClearSerialized();
        items = AssetDatabase.FindAssets("t:ItemSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i != null)
            .OrderBy(i => (int)i.tier).ThenBy(i => i.DisplayName, StringComparer.Ordinal)
            .ToList();
        if (selected != null && !items.Contains(selected)) selected = null;
        itemsVersion++;
    }

    private SerializedObject SO(ItemSO item)
    {
        if (!serialized.TryGetValue(item, out var so) || so == null || so.targetObject == null)
        {
            so = new SerializedObject(item);
            serialized[item] = so;
        }
        return so;
    }

    /// <summary>화면 구성이 바뀌는 동작은 다음 프레임으로 미룬다(IMGUI 레이아웃 불일치 방지).</summary>
    private void Defer(Action action)
    {
        EditorApplication.delayCall += () => { if (this == null) return; action(); Repaint(); };
    }

    // ── 필터 ──────────────────────────────────────────────────────────
    private List<ItemSO> Filtered() => items.Where(i => i != null
            && (tierFilter == 0 || (int)i.tier == tierFilter)
            && (!onlyWarnings || ItemEditorGUI.Warnings(i).Count > 0)
            && Matches(i)).ToList();

    private bool Matches(ItemSO i)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string s = search.Trim();
        return Has(i.name, s) || Has(i.itemName, s) || Has(i.description, s) || Has(i.shopDescription, s) || Has(i.EffectSummary(1), s);
    }

    private static bool Has(string text, string s) => !string.IsNullOrEmpty(text) && text.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

    // ── 그리기 ────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (items.Any(i => i == null)) Reload();

        // [입력 중이던 글이 다른 아이템에 붙는 문제]
        // IMGUI 는 입력 중인 글을 '화면 위 칸 번호'로 기억한다. 같은 자리에 다른 아이템이 그려지면
        // 입력하던 글이 그 아이템에 그대로 들어가 버린다. 그래서 보여주는 대상(선택 아이템, 보기, 필터,
        // 목록)이 바뀐 첫 프레임에, 아무것도 그리기 전에 편집 중인 글 칸을 닫는다.
        string context = $"{view}|{(selected != null ? selected.GetInstanceID() : 0)}|{tierFilter}|{onlyWarnings}|{itemsVersion}|{undoVersion}";
        if (context != lastContext) { lastContext = context; ItemEditorGUI.EndTextEditing(); }

        // 표시 목록은 검색어/필터가 바뀔 때만 다시 계산한다. 편집하다 경고가 사라지거나 등급이 바뀌어도
        // 줄이 그 자리에서 빠지지 않게 하려는 것(줄이 밀리면 입력 중이던 칸에 옆 아이템이 들어온다).
        // 툴바보다 먼저 계산해서, 필터를 바꾼 그 프레임에는 이전 목록 그대로 그린다.
        string filter = $"{search}|{tierFilter}|{onlyWarnings}|{itemsVersion}";
        if (visible == null || filter != lastFilter) { lastFilter = filter; visible = Filtered(); }
        var list = visible.Where(i => i != null).ToList();

        DrawToolbar();

        int warnCount = items.Count(i => ItemEditorGUI.Warnings(i).Count > 0);
        EditorGUILayout.LabelField(
            $"아이템 {items.Count}개 · 표시 중 {list.Count}개 · 확인 필요 {warnCount}개 · 모든 수정은 Ctrl+Z 로 되돌릴 수 있습니다"
            + (onlyWarnings ? " · 고친 아이템은 ↻ 를 누르면 목록에서 빠집니다" : "")
            + (Application.isPlaying ? " · 플레이 중 수정도 에셋에 그대로 남습니다" : ""), EditorStyles.miniLabel);

        if (items.Count == 0)
        {
            EditorGUILayout.HelpBox("ItemSO 에셋이 없습니다. '새 아이템'으로 만드세요.", MessageType.Info);
            return;
        }
        if (view == View.Table) DrawTable(list); else DrawDetail(list);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var newView = (View)GUILayout.Toolbar((int)view, new[] { "표로 보기", "하나씩 편집" }, EditorStyles.toolbarButton, GUILayout.Width(170));
            if (newView != view) { var v = newView; Defer(() => view = v); }
            GUILayout.Space(6);
            search = GUILayout.TextField(search ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(120), GUILayout.MaxWidth(260));
            tierFilter = EditorGUILayout.Popup(tierFilter, TierOptions, EditorStyles.toolbarPopup, GUILayout.Width(82));
            onlyWarnings = GUILayout.Toggle(onlyWarnings, new GUIContent("확인 필요만", "설명에 실제 효과 수치가 없거나, 효과가 비어 있는 아이템만 본다."), EditorStyles.toolbarButton, GUILayout.Width(78));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("새 아이템", "이름·등급·첫 효과·목록 등록을 정해 새 아이템을 만든다. 기존 아이템 복사로 시작할 수도 있다."), EditorStyles.toolbarButton))
                Defer(() => NewItemPopup.Open(null, OnCreated));
            if (GUILayout.Button(new GUIContent("CSV 내보내기", "이름·등급·중첩·보상 등장·설명을 CSV 로 저장(엑셀/구글 시트용)."), EditorStyles.toolbarButton)) Defer(() => ItemCsv.Export(items));
            if (GUILayout.Button(new GUIContent("CSV 가져오기", "내보낸 CSV 를 고친 뒤 다시 불러와 일괄 반영. 바뀌는 곳을 먼저 보여준다."), EditorStyles.toolbarButton)) Defer(() => { ItemCsv.Import(items); ClearSerialized(); });
            if (GUILayout.Button(new GUIContent("↻", "목록 새로고침"), EditorStyles.toolbarButton, GUILayout.Width(24))) Defer(Reload);
        }
    }

    // ── 표 보기 ───────────────────────────────────────────────────────
    private void DrawTable(List<ItemSO> list)
    {
        tableScroll = EditorGUILayout.BeginScrollView(tableScroll);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(4);
            foreach (var c in Columns) GUILayout.Label(new GUIContent(c.title, c.tip), EditorStyles.miniBoldLabel, GUILayout.Width(c.width));
        }

        foreach (var item in list)
        {
            var so = SO(item);
            so.Update();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[0].width)))
                {
                    var r = GUILayoutUtility.GetRect(44, 40, GUILayout.Width(44), GUILayout.Height(40));
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), item.TierColor);
                    DrawSprite(new Rect(r.x + 6, r.y + 2, 36, 36), item.icon);
                    if (GUILayout.Button(new GUIContent("편집", "하나씩 편집 화면에서 연다"), EditorStyles.miniButton, GUILayout.Width(44)))
                    { var target = item; Defer(() => { selected = target; view = View.Detail; }); }
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[1].width)))
                {
                    EditorGUILayout.PropertyField(so.FindProperty("itemName"), GUIContent.none, GUILayout.Width(Columns[1].width));
                    EditorGUILayout.LabelField(new GUIContent(item.name, "에셋 파일 이름(세이브 키)"), EditorStyles.miniLabel, GUILayout.Width(Columns[1].width));
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[2].width)))
                {
                    EditorGUILayout.PropertyField(so.FindProperty("tier"), GUIContent.none, GUILayout.Width(Columns[2].width));
                    EditorGUILayout.LabelField($"{item.Price} 골드", EditorStyles.miniLabel, GUILayout.Width(Columns[2].width));
                }
                EditorGUILayout.PropertyField(so.FindProperty("maxStack"), GUIContent.none, GUILayout.Width(Columns[3].width));
                EditorGUILayout.PropertyField(so.FindProperty("availableAsReward"), GUIContent.none, GUILayout.Width(Columns[4].width));
                TextAreaProp(so.FindProperty("description"), Columns[5].width);
                TextAreaProp(so.FindProperty("shopDescription"), Columns[6].width);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[7].width)))
                    ItemEditorGUI.DrawEffects(so.FindProperty("effects"), item, true);

                so.ApplyModifiedProperties();

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Columns[8].width)))
                {
                    EditorGUILayout.LabelField(item.EffectSummary(1), ItemEditorGUI.WrapLabel, GUILayout.Width(Columns[8].width));
                    foreach (var w in ItemEditorGUI.Warnings(item))
                        EditorGUILayout.LabelField(new GUIContent("⚠ " + w.Split('\n')[0], w), ItemEditorGUI.WrapMini, GUILayout.Width(Columns[8].width));
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static void TextAreaProp(SerializedProperty p, float width)
    {
        EditorGUI.BeginChangeCheck();
        string v = EditorGUILayout.TextArea(p.stringValue ?? "", ItemEditorGUI.WrapArea, GUILayout.Width(width), GUILayout.MinHeight(38));
        if (EditorGUI.EndChangeCheck()) p.stringValue = v;
    }

    // ── 하나씩 편집 ───────────────────────────────────────────────────
    private void DrawDetail(List<ItemSO> list)
    {
        if (selected == null && list.Count > 0) selected = list[0];

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
            {
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                foreach (var item in list) DrawListRow(item);
                EditorGUILayout.EndScrollView();
            }

            var div = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(div, new Color(0.5f, 0.5f, 0.5f, 0.4f));

            using (new EditorGUILayout.VerticalScope())
            {
                if (selected == null) { EditorGUILayout.HelpBox("왼쪽에서 아이템을 고르세요.", MessageType.Info); return; }
                DrawDetailHeader(selected);
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                ItemEditorGUI.DrawItem(SO(selected));
                EditorGUILayout.Space(12);
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawListRow(ItemSO item)
    {
        var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
        if (item == selected) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.35f));
        EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 4, 4, r.height - 8), item.TierColor);
        DrawSprite(new Rect(r.x + 10, r.y + 3, 20, 20), item.icon);
        GUI.Label(new Rect(r.x + 34, r.y + 4, r.width - 58, 18), new GUIContent(item.DisplayName, item.name));
        if (ItemEditorGUI.Warnings(item).Count > 0)
        {
            var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
            icon.tooltip = "확인 필요";
            GUI.Label(new Rect(r.xMax - 22, r.y + 4, 20, 18), icon);
        }
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            var target = item;
            Defer(() => selected = target); // 편집 중인 글 칸은 다음 OnGUI 시작에서 닫힌다
            Event.current.Use();
        }
    }

    private void DrawDetailHeader(ItemSO item)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(item.DisplayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("프로젝트에서 찾기", "Project 창에서 에셋을 가리킨다"), EditorStyles.miniButtonLeft))
                EditorGUIUtility.PingObject(item);
            if (GUILayout.Button(new GUIContent("복제", "이 아이템을 복사해 새 아이템을 만든다(이름·등록 지정)"), EditorStyles.miniButtonMid))
            { var target = item; Defer(() => Duplicate(target)); }
            if (GUILayout.Button(new GUIContent("삭제", "참조하는 곳을 확인한 뒤 목록에서 빼고 OS 휴지통으로 옮긴다"), EditorStyles.miniButtonRight))
            { var target = item; Defer(() => Delete(target)); }
        }

        if (renameTarget != item) { renameTarget = item; renameText = item.name; }
        using (new EditorGUILayout.HorizontalScope())
        {
            renameText = EditorGUILayout.TextField(new GUIContent("에셋 파일 이름",
                "세이브 데이터와 검증 스크립트가 이 이름으로 아이템을 찾는다. 이미 세이브에 들어간 아이템은 바꾸지 마세요."), renameText);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(renameText) || renameText == item.name))
                if (GUILayout.Button("바꾸기", EditorStyles.miniButton, GUILayout.Width(50)))
                { var target = item; string to = renameText.Trim(); Defer(() => Rename(target, to)); }
        }
    }

    // ── 에셋 작업 ─────────────────────────────────────────────────────
    /// <summary>새 아이템/복제 창에서 만들어진 아이템을 바로 편집 화면에 띄운다.</summary>
    private void OnCreated(ItemSO item)
    {
        Reload();
        search = ""; tierFilter = 0; onlyWarnings = false;
        selected = item; view = View.Detail;
        EditorGUIUtility.PingObject(item);
        Focus();
        Repaint();
    }

    private void Duplicate(ItemSO item) => NewItemPopup.Open(item, OnCreated);

    private void Delete(ItemSO item)
    {
        if (!ItemCreation.Delete(item)) return;
        selected = null;
        renameTarget = null;
        Reload();
    }

    private void Rename(ItemSO item, string newName)
    {
        if (!EditorUtility.DisplayDialog("에셋 이름 바꾸기",
                $"'{item.name}' → '{newName}'\n\n세이브 데이터가 에셋 이름으로 아이템을 찾습니다. 기존 세이브에 들어 있는 아이템이면 불러올 때 사라집니다.\n" +
                "검증 스크립트(CriticalItemsCheck 등)에 이름이 적혀 있으면 함께 고쳐야 합니다.", "바꾸기", "취소")) return;
        string err = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(item), newName);
        if (!string.IsNullOrEmpty(err)) { EditorUtility.DisplayDialog("이름 변경 실패", err, "확인"); return; }
        AssetDatabase.SaveAssets();
        renameTarget = null;
    }

    private static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) return;
        try
        {
            var t = s.texture;
            var tr = s.textureRect;
            var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
            float aspect = tr.height > 0 ? tr.width / tr.height : 1f;
            var fit = r;
            if (aspect > 1f) { fit.height = r.width / aspect; fit.y += (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x += (r.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, t, uv);
        }
        catch (Exception)
        {
            var preview = AssetPreview.GetAssetPreview(s);
            if (preview != null) GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
        }
    }
}

/// <summary>
/// 아이템 CSV 내보내기/가져오기. 키는 에셋 파일 이름. 효과 열은 참고용이라 가져오기에서 무시한다
/// (효과 종류/수치는 구조가 있는 데이터라 표 한 칸으로 옮기면 실수가 쉽다 — 편집기에서 고친다).
/// </summary>
internal static class ItemCsv
{
    private static readonly string[] Header = { "에셋", "이름", "등급", "최대보유", "보상등장", "설명", "상점설명", "효과(참고용·가져오기 안 함)" };

    private sealed class Change
    {
        public ItemSO item; public string field, from, to; public Action apply;
    }

    public static void Export(List<ItemSO> items)
    {
        string path = EditorUtility.SaveFilePanel("아이템 CSV 내보내기", "", "items.csv", "csv");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Header.Select(Esc))).Append("\r\n");
        foreach (var i in items.Where(x => x != null))
        {
            var cells = new[]
            {
                i.name, i.itemName, ((int)i.tier).ToString(), i.maxStack.ToString(), i.availableAsReward ? "1" : "0",
                i.description, i.shopDescription, i.EffectSummary(1),
            };
            sb.Append(string.Join(",", cells.Select(Esc))).Append("\r\n");
        }
        // BOM 을 붙여야 엑셀이 한글을 깨뜨리지 않는다.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        EditorUtility.RevealInFinder(path);
    }

    public static void Import(List<ItemSO> items)
    {
        string path = EditorUtility.OpenFilePanel("아이템 CSV 가져오기", "", "csv");
        if (string.IsNullOrEmpty(path)) return;

        string text;
        try { text = ReadText(path); }
        catch (Exception e) { EditorUtility.DisplayDialog("CSV 가져오기", "파일을 읽지 못했습니다. 'CSV UTF-8' 형식으로 저장해 주세요.\n\n" + e.Message, "확인"); return; }

        var rows = Parse(text);
        if (rows.Count == 0 || rows[0].Count == 0 || rows[0][0].Trim('\uFEFF', ' ') != Header[0])
        {
            EditorUtility.DisplayDialog("CSV 가져오기", "첫 줄이 '에셋,이름,등급,…' 인 CSV(이 창에서 내보낸 형식)만 가져올 수 있습니다.", "확인");
            return;
        }

        var head = rows[0].Select(h => h.Trim('\uFEFF', ' ')).ToList();
        int cName = head.IndexOf("이름"), cTier = head.IndexOf("등급"), cMax = head.IndexOf("최대보유"),
            cReward = head.IndexOf("보상등장"), cDesc = head.IndexOf("설명"), cShop = head.IndexOf("상점설명");

        var byName = items.Where(i => i != null).GroupBy(i => i.name).ToDictionary(g => g.Key, g => g.First());
        var changes = new List<Change>();
        var newRows = new List<List<string>>();
        var errors = new List<string>();

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string asset = Cell(row, 0).Trim();
            if (asset.Length == 0) { errors.Add($"{r + 1}번째 줄: 에셋 이름이 비어 있음"); continue; }
            if (!byName.TryGetValue(asset, out var item)) { newRows.Add(row); continue; }

            string name = Get(row, cName);
            if (name != null && name != (item.itemName ?? ""))
                changes.Add(new Change { item = item, field = "이름", from = item.itemName, to = name, apply = () => item.itemName = name });

            string desc = Get(row, cDesc);
            if (desc != null && desc != Norm(item.description))
                changes.Add(new Change { item = item, field = "설명", from = item.description, to = desc, apply = () => item.description = desc });

            string shop = Get(row, cShop);
            if (shop != null && shop != Norm(item.shopDescription))
                changes.Add(new Change { item = item, field = "상점설명", from = item.shopDescription, to = shop, apply = () => item.shopDescription = shop });

            string tierText = Get(row, cTier);
            if (!string.IsNullOrWhiteSpace(tierText))
            {
                if (TryTier(tierText, out var tier))
                { if (tier != item.tier) changes.Add(new Change { item = item, field = "등급", from = ((int)item.tier).ToString(), to = ((int)tier).ToString(), apply = () => item.tier = tier }); }
                else errors.Add($"{asset} 등급 '{tierText}' (1~4)");
            }

            string maxText = Get(row, cMax);
            if (!string.IsNullOrWhiteSpace(maxText))
            {
                if (int.TryParse(maxText.Trim(), out int max) && max >= 1)
                { if (max != item.maxStack) changes.Add(new Change { item = item, field = "최대보유", from = item.maxStack.ToString(), to = max.ToString(), apply = () => item.maxStack = max }); }
                else errors.Add($"{asset} 최대보유 '{maxText}' (1 이상 정수)");
            }

            string rewardText = Get(row, cReward);
            if (!string.IsNullOrWhiteSpace(rewardText))
            {
                if (TryBool(rewardText, out bool reward))
                { if (reward != item.availableAsReward) changes.Add(new Change { item = item, field = "보상등장", from = item.availableAsReward ? "1" : "0", to = reward ? "1" : "0", apply = () => item.availableAsReward = reward }); }
                else errors.Add($"{asset} 보상등장 '{rewardText}' (1/0)");
            }
        }

        var msg = new StringBuilder();
        if (changes.Count == 0) msg.AppendLine("기존 아이템에서 바뀌는 값은 없습니다.");
        else
        {
            msg.AppendLine($"기존 아이템 {changes.Select(c => c.item).Distinct().Count()}개, {changes.Count}곳이 바뀝니다.\n");
            foreach (var c in changes.Take(12)) msg.AppendLine($"· {c.item.name} / {c.field}: {Short(c.from)} → {Short(c.to)}");
            if (changes.Count > 12) msg.AppendLine($"… 외 {changes.Count - 12}곳");
        }
        if (newRows.Count > 0)
        {
            var names = newRows.Select(r => Cell(r, 0).Trim()).ToList();
            msg.AppendLine($"\n프로젝트에 없는 아이템 {names.Count}개: {string.Join(", ", names.Take(10))}{(names.Count > 10 ? " …" : "")}");
            msg.AppendLine($"새로 만들면 {ItemCreation.DefaultFolderOrAssets} 에 생깁니다. 효과는 비어 있고 목록·상점 등록은 꺼진 상태라, 편집기에서 효과를 넣고 '목록 등록'을 켜야 게임에 나옵니다.");
        }
        if (errors.Count > 0) msg.AppendLine("\n읽지 못한 값(건너뜀):\n" + string.Join("\n", errors.Take(8)));

        bool apply, create;
        if (changes.Count == 0 && newRows.Count == 0) { EditorUtility.DisplayDialog("CSV 가져오기", msg.ToString(), "확인"); return; }
        if (newRows.Count == 0)
        {
            apply = EditorUtility.DisplayDialog("CSV 가져오기", msg.ToString(), "적용", "취소");
            create = false;
        }
        else if (changes.Count == 0)
        {
            create = EditorUtility.DisplayDialog("CSV 가져오기", msg.ToString(), "새로 만들기", "취소");
            apply = false;
        }
        else
        {
            int choice = EditorUtility.DisplayDialogComplex("CSV 가져오기", msg.ToString(), "적용 + 새로 만들기", "취소", "기존 아이템만 적용");
            apply = choice == 0 || choice == 2;
            create = choice == 0;
        }
        if (!apply && !create) return;

        if (apply)
        {
            var targets = changes.Select(c => (UnityEngine.Object)c.item).Distinct().ToArray();
            Undo.RecordObjects(targets, "아이템 CSV 가져오기");
            foreach (var c in changes) c.apply();
            foreach (var t in targets) EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();
        }

        if (create)
        {
            var created = new List<string>();
            var failed = new List<string>();
            foreach (var row in newRows)
            {
                string asset = Cell(row, 0).Trim();
                var req = new ItemCreation.Request
                {
                    assetName = asset,
                    folder = ItemCreation.DefaultFolderOrAssets,
                    displayName = string.IsNullOrEmpty(Get(row, cName)) ? asset : Get(row, cName),
                    description = Get(row, cDesc) ?? "",
                    shopDescription = Get(row, cShop) ?? "",
                    tier = TryTier(Get(row, cTier) ?? "", out var t) ? t : ItemTier.Tier4,
                    maxStack = int.TryParse((Get(row, cMax) ?? "").Trim(), out int m) && m >= 1 ? m : 1,
                    availableAsReward = !TryBool(Get(row, cReward) ?? "", out bool rw) || rw,
                    addToCatalog = false,
                    addToShop = false,
                };
                if (ItemCreation.Create(req, out string err) != null) created.Add(asset);
                else failed.Add($"{asset}: {err}");
            }
            var done = new StringBuilder($"새 아이템 {created.Count}개를 만들었습니다.");
            if (created.Count > 0) done.AppendLine("\n효과를 넣고 '목록 등록'을 켜야 게임에 나옵니다(편집기의 '확인 필요만'으로 모아 볼 수 있음).");
            if (failed.Count > 0) done.AppendLine("\n만들지 못함:\n" + string.Join("\n", failed.Take(8)));
            EditorUtility.DisplayDialog("CSV 가져오기", done.ToString(), "확인");
        }
    }

    // ── 도우미 ────────────────────────────────────────────────────────
    private static string Cell(List<string> row, int c) => c >= 0 && c < row.Count ? row[c] : "";
    /// <summary>열이 없으면 null(=건드리지 않음).</summary>
    private static string Get(List<string> row, int c) => c < 0 ? null : Norm(Cell(row, c));
    private static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
    private static string Short(string s) { s = Norm(s).Replace("\n", "⏎"); return s.Length > 28 ? s.Substring(0, 28) + "…" : (s.Length == 0 ? "(빈칸)" : s); }

    private static bool TryTier(string s, out ItemTier tier)
    {
        tier = ItemTier.Tier4;
        foreach (char ch in s) if (ch >= '1' && ch <= '4') { tier = (ItemTier)(ch - '0'); return true; }
        return false;
    }

    private static bool TryBool(string s, out bool value)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "1": case "true": case "o": case "y": case "yes": case "on": case "예": case "켜기": value = true; return true;
            case "0": case "false": case "x": case "n": case "no": case "off": case "아니오": case "끄기": value = false; return true;
            default: value = false; return false;
        }
    }

    private static string Esc(string s)
    {
        s = Norm(s);
        bool quote = s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ");
        return quote ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>UTF-8(BOM 유무 무관) 우선, 엑셀이 ANSI(CP949)로 저장한 파일이면 그걸로 다시 읽는다.</summary>
    private static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(bytes); }
    }

    /// <summary>따옴표/쉼표/줄바꿈이 든 칸을 지원하는 CSV 파서.</summary>
    private static List<List<string>> Parse(string text)
    {
        text = Norm(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
