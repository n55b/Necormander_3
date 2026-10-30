using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 사운드 테스트 툴 (메뉴: Tools > 사운드 > 사운드 테스트 툴)
///
/// [오브젝트 소리] 고른 몬스터·소환수·캐릭터·사물의 '사운드 세트'. 소리 추가/삭제, 언제 재생할지, 볼륨·음높이를 고치고 ▶ 로 듣는다.
/// [UI 소리]      창마다 다른 열림/닫힘 소리, 버튼·아이템 칸 마우스 올림/클릭 소리, 상점 구매 성공/실패 소리.
/// [전체 음원]    Assets의 모든 오디오 파일을 이름·경로로 검색, 미리듣기, 선택, 목록 복사.
/// 공통: 소리 칸에 오디오 파일을 끌어다 놓거나, 오른쪽 '오디오 파일 찾기'에서 [+] 로 넣는다.
///       아래 '재생 기록'에는 플레이 중 실제로 난 소리와 열리고 닫힌 창 이름이 쌓인다.
/// </summary>
public class SoundTestWindow : EditorWindow
{
    [MenuItem("Tools/사운드/사운드 테스트 툴", priority = 0)]
    public static SoundTestWindow Open()
    {
        var w = GetWindow<SoundTestWindow>("사운드 테스트");
        w.minSize = new Vector2(680, 460);
        return w;
    }

    public static void OpenFor(GameObject target)
    {
        var w = Open();
        w._tab = 0;
        w._followSelection = false;
        w.SetTarget(target);
    }

    private static readonly string[] Tabs = { "오브젝트 소리", "UI 소리", "게임 행동", "전체 음원" };

    private static readonly (string name, SoundTrigger trigger)[] Presets =
    {
        ("공격", SoundTrigger.OnAttackHit), ("공격 끝", SoundTrigger.OnAttackEnd), ("피격", SoundTrigger.OnHurt),
        ("사망", SoundTrigger.OnDeath), ("회복", SoundTrigger.OnHeal), ("등장", SoundTrigger.OnSpawn),
        ("스킬", SoundTrigger.Manual), ("발소리", SoundTrigger.Manual), ("직접 호출", SoundTrigger.Manual),
    };

    // ── 상태 ─────────────────────────────────────────────────────────
    private int _tab;
    private GameObject _target;
    private EntitySoundSet _set;
    private SerializedObject _setSO;
    private UISoundSettings _ui;
    private SerializedObject _uiSO;
    private SerializedObject _catalogSO;
    private bool _followSelection = true;
    private float _previewVolume = 1f;
    private bool _simplePreview;
    private Vector2 _scrollMain, _scrollClips, _scrollLog;
    private string _search = "", _lastSearch;
    private string _newWindowName = "";
    private readonly List<AudioClip> _found = new List<AudioClip>();
    private readonly List<AudioClip> _allClips = new List<AudioClip>();
    private bool _clipsDirty = true;
    private int _clipPage;
    private const int ClipsPerPage = 40;
    private readonly List<string> _log = new List<string>();
    private readonly HashSet<string> _folded = new HashSet<string>();
    private readonly HashSet<string> _seenWindows = new HashSet<string>();

    // [+] 로 넣을 대상: (어느 SerializedObject 의) 어느 소리 묶음
    private SerializedObject _pickSO;
    private string _pickPath, _pickLabel;

    private static AudioSource _previewSource;
    private static GUIStyle _box;

    // ── 수명 ─────────────────────────────────────────────────────────
    private void OnEnable()
    {
        EntitySoundSet.Played += OnObjectPlayed;
        PolishFeedback.SoundPlayed += OnGameplaySound;
        UIEventBus.OnUIOpen += OnWindowOpened;
        UIEventBus.OnUIClose += OnWindowClosed;
        if (_followSelection) SetTarget(Selection.activeGameObject);
    }

    private void OnDisable()
    {
        EntitySoundSet.Played -= OnObjectPlayed;
        PolishFeedback.SoundPlayed -= OnGameplaySound;
        UIEventBus.OnUIOpen -= OnWindowOpened;
        UIEventBus.OnUIClose -= OnWindowClosed;
        StopAll();
    }

    private void OnProjectChange() { _clipsDirty = true; Repaint(); }

    private void OnSelectionChange()
    {
        if (!_followSelection || _tab != 0) return;
        SetTarget(Selection.activeGameObject);
        Repaint();
    }

    private void OnObjectPlayed(EntitySoundSet set, SoundEntry entry, AudioClip clip)
        => AddLog($"{(set != null ? set.name : "?")}  ·  {entry.name}  ·  {(clip != null ? clip.name : "-")}");
    private void OnGameplaySound(FeedbackCue cue, AudioClip clip)
        => AddLog($"{PolishFeedback.Catalog?.Find(cue)?.title ?? cue.ToString()} · {clip?.name}");

    private void OnWindowOpened(string name) => LogWindow(name, "열림");
    private void OnWindowClosed(string name) => LogWindow(name, "닫힘");

    private void LogWindow(string name, string what)
    {
        _seenWindows.Add(name);
        var w = UISoundSettings.Active != null ? UISoundSettings.Active.FindWindow(name) : null;
        string note = w == null ? "  (목록에 없음 → 기본 소리)" : string.IsNullOrEmpty(w.label) ? "" : $"  ({w.label})";
        AddLog($"창 {what}  ·  {name}{note}");
    }

    private void AddLog(string line)
    {
        _log.Insert(0, $"{System.DateTime.Now:HH:mm:ss}  {line}");
        if (_log.Count > 80) _log.RemoveAt(_log.Count - 1);
        Repaint();
    }

    private void SetTarget(GameObject go)
    {
        _target = go;
        _set = go != null ? go.GetComponentInParent<EntitySoundSet>(true) : null;
        _setSO = _set != null ? new SerializedObject(_set) : null;
        if (_pickSO != null && _pickSO != _uiSO) ClearPick();
    }

    private void ClearPick() { _pickSO = null; _pickPath = _pickLabel = null; }

    // ── 그리기 ───────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (_box == null) _box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 4, 6) };
        DrawToolbar();
        if (_tab == 3)
        {
            EditorGUILayout.HelpBox("Assets 폴더의 전체 음원입니다(미사용 파일 포함). 이름·경로로 검색하고 ▶로 들어볼 수 있습니다. 게임의 볼륨/피치는 앞의 세 탭에서 조절하세요. 피치는 음높이와 속도를 함께 바꾸며, 구간 자르기·음높이/속도 개별 편집은 원본 파일 편집이 필요합니다.", MessageType.Info);
            DrawClipBrowser(true);
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.6f)))
            {
                _scrollMain = EditorGUILayout.BeginScrollView(_scrollMain);
                if (_tab == 0) DrawObjectTab(); else if (_tab == 1) DrawUITab(); else DrawGameplayTab();
                EditorGUILayout.EndScrollView();
            }
            using (new EditorGUILayout.VerticalScope())
                DrawClipBrowser();
        }
        DrawLog();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int tab = GUILayout.Toolbar(_tab, Tabs, EditorStyles.toolbarButton, GUILayout.Width(350));
            if (tab != _tab) { _tab = tab; ClearPick(); }
            GUILayout.FlexibleSpace();
            GUILayout.Label(new GUIContent("미리듣기 볼륨", "이 툴에서 ▶ 로 들을 때만 쓰는 볼륨입니다. 게임 값은 바뀌지 않습니다."), GUILayout.Width(80));
            _previewVolume = GUILayout.HorizontalSlider(_previewVolume, 0f, 1f, GUILayout.Width(80));
            _simplePreview = GUILayout.Toggle(_simplePreview,
                new GUIContent("단순 미리듣기", "▶ 를 눌러도 소리가 안 나면 켜 보세요. 유니티 기본 미리듣기를 씁니다(볼륨·음높이는 반영 안 됨)."),
                EditorStyles.toolbarButton, GUILayout.Width(80));
            if (GUILayout.Button("■ 정지", EditorStyles.toolbarButton, GUILayout.Width(50))) StopAll();
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // 오브젝트 소리
    // ═════════════════════════════════════════════════════════════════
    private void DrawGameplayTab()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<FeedbackCatalog>("Assets/Resources/FeedbackCatalog.asset");
        if (catalog == null) { EditorGUILayout.HelpBox("FeedbackCatalog 설정이 없습니다.", MessageType.Info); return; }
        if (_catalogSO == null || _catalogSO.targetObject != catalog) _catalogSO = new SerializedObject(catalog);
        _catalogSO.Update();
        EditorGUILayout.HelpBox("발소리·대시·가드·보스·보상 소리입니다. 파일을 교체하거나 볼륨/음높이/최소 간격을 조절하세요. 시각 효과의 수치는 바꾸지 않습니다.", MessageType.Info);
        var effects = _catalogSO.FindProperty("effects");
        for (int i = 0; i < effects.arraySize; i++)
        {
            var entry = effects.GetArrayElementAtIndex(i);
            var audio = entry.FindPropertyRelative("audio");
            string title = entry.FindPropertyRelative("title").stringValue;
            if (!AnyClip(audio) && entry.FindPropertyRelative("cue").intValue < 86) continue;
            DrawGroup(_catalogSO, audio, title);
        }
        _catalogSO.ApplyModifiedProperties();
    }

    private void DrawObjectTab()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var picked = (GameObject)EditorGUILayout.ObjectField("대상", _target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) { _followSelection = false; SetTarget(picked); }
            _followSelection = GUILayout.Toggle(_followSelection,
                new GUIContent("선택 따라가기", "켜면 Project/Hierarchy 에서 클릭한 오브젝트가 자동으로 대상이 됩니다."), "Button", GUILayout.Width(90));
        }

        if (_target == null)
        {
            EditorGUILayout.HelpBox(
                "소리를 넣을 대상을 고르세요.\n\n• Project 창에서 몬스터/소환수/캐릭터/사물 프리팹을 클릭하거나,\n• Hierarchy 에서 씬 오브젝트를 클릭하면 여기에 나타납니다.\n\n" +
                "프리팹을 고르면 같은 프리팹을 쓰는 모든 오브젝트에 소리가 들어갑니다.", MessageType.Info);
            return;
        }
        if (_set == null) { DrawAddSet(); return; }
        if (_setSO == null || _setSO.targetObject == null) { SetTarget(_target); if (_setSO == null) return; }

        _setSO.Update();
        var list = _setSO.FindProperty("sounds");
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label($"{_set.name} 의 소리 ({list.arraySize})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 소리 추가 ▼", GUILayout.Width(100))) ShowAddEntryMenu();
        }

        if (!EditorUtility.IsPersistent(_set) && PrefabUtility.IsPartOfPrefabInstance(_set))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox("씬에 놓인 오브젝트입니다. 여기서 고친 값은 이 오브젝트에만 적용됩니다.", MessageType.None);
                if (GUILayout.Button("프리팹 원본 열기", GUILayout.Width(110), GUILayout.Height(38)))
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(_set.gameObject);
                    if (src != null) { _followSelection = false; SetTarget(src.transform.root.gameObject); Selection.activeObject = _target; }
                }
            }
        }
        if (list.arraySize == 0)
            EditorGUILayout.HelpBox("[+ 소리 추가] 를 눌러 '공격 / 피격 / 사망' 같은 소리를 만든 뒤, 오디오 파일을 끌어다 놓으세요.", MessageType.Info);

        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var pName = e.FindPropertyRelative("name");
            var pTrigger = e.FindPropertyRelative("trigger");
            bool delete = false;
            DrawGroup(_setSO, e, null, header: () =>
            {
                pName.stringValue = EditorGUILayout.TextField(pName.stringValue, GUILayout.MinWidth(70));
                EditorGUILayout.PropertyField(pTrigger, GUIContent.none, GUILayout.MinWidth(140));
                if (Application.isPlaying && !EditorUtility.IsPersistent(_set) && i < _set.sounds.Count &&
                    GUILayout.Button(new GUIContent("게임▶", "플레이 중인 게임 안에서 재생합니다(옵션 효과음 볼륨 반영, 재생 기록에 남음)."), GUILayout.Width(48)))
                    _set.PlayEntry(_set.sounds[i]);
                if (GUILayout.Button(new GUIContent("✕", "이 소리 삭제"), GUILayout.Width(22)))
                    delete = EditorUtility.DisplayDialog("소리 삭제", $"'{pName.stringValue}' 소리를 삭제할까요?", "삭제", "취소");
            }, footer: () =>
            {
                if ((SoundTrigger)pTrigger.enumValueIndex == SoundTrigger.OnAttackHit ||
                    (SoundTrigger)pTrigger.enumValueIndex == SoundTrigger.OnAttackEnd)
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("animationState"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("minInterval"));
                EditorGUILayout.PropertyField(e.FindPropertyRelative("delay"));
                if ((SoundTrigger)pTrigger.enumValueIndex == SoundTrigger.Manual)
                    EditorGUILayout.HelpBox($"애니메이션 이벤트: 함수 PlaySound, 문자열 \"{pName.stringValue}\"   /   코드: GetComponent<EntitySoundSet>().PlaySound(\"{pName.stringValue}\")", MessageType.None);
            });
            if (delete) remove = i;
        }
        if (remove >= 0)
        {
            if (_pickSO == _setSO && _pickPath != null && _pickPath.StartsWith(list.GetArrayElementAtIndex(remove).propertyPath)) ClearPick();
            list.DeleteArrayElementAtIndex(remove);
        }
        if (_setSO.ApplyModifiedProperties()) EditorUtility.SetDirty(_set);
    }

    private void DrawAddSet()
    {
        EditorGUILayout.HelpBox($"'{_target.name}' 에는 아직 사운드 세트가 없습니다.", MessageType.None);
        if (EditorUtility.IsPersistent(_target))
        {
            if (GUILayout.Button("이 프리팹에 사운드 세트 붙이기", GUILayout.Height(28))) AddSetToPrefab(AssetDatabase.GetAssetPath(_target), false);
            return;
        }
        var source = PrefabUtility.GetCorrespondingObjectFromSource(_target);
        string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
        if (!string.IsNullOrEmpty(sourcePath) &&
            GUILayout.Button(new GUIContent("프리팹 원본에 붙이기 (권장)", "같은 프리팹을 쓰는 모든 오브젝트에 적용됩니다.\n" + sourcePath), GUILayout.Height(28)))
            AddSetToPrefab(sourcePath, true);
        if (GUILayout.Button("이 씬 오브젝트에만 붙이기", GUILayout.Height(22)))
        {
            Undo.AddComponent<EntitySoundSet>(_target);
            SetTarget(_target);
        }
    }

    private void AddSetToPrefab(string path, bool selectAsset)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<EntitySoundSet>() == null) root.AddComponent<EntitySoundSet>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (selectAsset) { _followSelection = false; Selection.activeObject = asset; }
        SetTarget(selectAsset || EditorUtility.IsPersistent(_target) ? asset : _target);
    }

    private void ShowAddEntryMenu()
    {
        var menu = new GenericMenu();
        foreach (var p in Presets)
        {
            var preset = p;
            menu.AddItem(new GUIContent(preset.name), false, () =>
            {
                _setSO.Update();
                var l = _setSO.FindProperty("sounds");
                int i = l.arraySize;
                l.InsertArrayElementAtIndex(i);
                var e = l.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = preset.name;
                e.FindPropertyRelative("trigger").enumValueIndex = (int)preset.trigger;
                e.FindPropertyRelative("animationState").stringValue = "";
                ResetGroup(e, 0.8f, 0.95f, 1.05f);
                e.FindPropertyRelative("minInterval").floatValue = 0.05f;
                e.FindPropertyRelative("delay").floatValue = 0f;
                _setSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(_set);
                Pick(_setSO, e, preset.name);
            });
        }
        menu.ShowAsContext();
    }

    // ═════════════════════════════════════════════════════════════════
    // UI 소리
    // ═════════════════════════════════════════════════════════════════
    private void DrawUITab()
    {
        if (_ui == null) _ui = AssetDatabase.LoadAssetAtPath<UISoundSettings>(UISoundSetupTool.SettingsPath);
        if (_ui == null)
        {
            EditorGUILayout.HelpBox("UI 소리 설정 파일이 아직 없습니다. 아래 버튼으로 만드세요.", MessageType.Info);
            if (GUILayout.Button("UI 소리 설정 만들기", GUILayout.Height(28))) _ui = UISoundSetupTool.EnsureSettings();
            return;
        }
        if (_uiSO == null || _uiSO.targetObject != _ui) _uiSO = new SerializedObject(_ui);
        _uiSO.Update();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(_uiSO.FindProperty("soundEnabled"));
            if (GUILayout.Button(new GUIContent("설정 파일 선택", UISoundSetupTool.SettingsPath), GUILayout.Width(100))) { Selection.activeObject = _ui; EditorGUIUtility.PingObject(_ui); }
            if (GUILayout.Button(new GUIContent("UI 버튼에 소리 붙이기", "인게임 UI 프리팹의 버튼·아이템 칸에 'UI 버튼 소리' 컴포넌트를 붙입니다(이미 있으면 그대로)."), GUILayout.Width(140)))
                UISoundSetupTool.AttachButtonSounds();
        }

        // ① 창
        if (Section("windows", "창 열림 · 닫힘 (창마다 다르게)"))
        {
            DrawGroup(_uiSO, _uiSO.FindProperty("defaultOpen"), "기본 열림 소리 (목록에 없거나 비어 있는 창)");
            DrawGroup(_uiSO, _uiSO.FindProperty("defaultClose"), "기본 닫힘 소리");

            var windows = _uiSO.FindProperty("windows");
            int remove = -1;
            for (int i = 0; i < windows.arraySize; i++)
            {
                var w = windows.GetArrayElementAtIndex(i);
                string uiName = w.FindPropertyRelative("uiName").stringValue;
                string label = w.FindPropertyRelative("label").stringValue;
                string key = "w:" + uiName;
                using (new EditorGUILayout.VerticalScope(_box))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool open = !_folded.Contains(key);
                        bool now = EditorGUILayout.Foldout(open, $"{(string.IsNullOrEmpty(label) ? uiName : label)}   [{uiName}]" + (HasClips(w, "open") || HasClips(w, "close") ? "" : "   · 기본 소리 사용"), true);
                        if (now != open) { if (now) _folded.Remove(key); else _folded.Add(key); }
                        if (GUILayout.Button(new GUIContent("✕", "이 창을 목록에서 빼기(기본 소리로 돌아감)"), GUILayout.Width(22)) &&
                            EditorUtility.DisplayDialog("창 빼기", $"'{uiName}' 을(를) 목록에서 뺄까요? 기본 소리로 돌아갑니다.", "빼기", "취소")) remove = i;
                    }
                    if (_folded.Contains(key)) continue;
                    EditorGUILayout.PropertyField(w.FindPropertyRelative("label"));
                    EditorGUILayout.PropertyField(w.FindPropertyRelative("uiName"));
                    DrawGroup(_uiSO, w.FindPropertyRelative("open"), "열릴 때");
                    DrawGroup(_uiSO, w.FindPropertyRelative("close"), "닫힐 때");
                }
            }
            if (remove >= 0) { ClearPick(); windows.DeleteArrayElementAtIndex(remove); }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 창 추가 ▼", GUILayout.Width(100))) ShowAddWindowMenu();
                _newWindowName = EditorGUILayout.TextField(_newWindowName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newWindowName)))
                    if (GUILayout.Button("이 이름으로 추가", GUILayout.Width(100)))
                    {
                        _uiSO.ApplyModifiedProperties();
                        UISoundSetupTool.AddWindow(_ui, _newWindowName.Trim(), _newWindowName.Trim());
                        _newWindowName = "";
                        _uiSO.Update();
                    }
            }
            EditorGUILayout.HelpBox("창 이름은 코드가 보내는 이름과 같아야 합니다. 플레이하면서 창을 열고 닫으면 아래 '재생 기록'에 실제 이름이 나옵니다.", MessageType.None);
        }

        // ② 버튼
        if (Section("buttons", "버튼 · 아이템 칸"))
        {
            DrawGroup(_uiSO, _uiSO.FindProperty("buttonHover"), "버튼 · 마우스 올림");
            DrawGroup(_uiSO, _uiSO.FindProperty("buttonClick"), "버튼 · 클릭");
            DrawGroup(_uiSO, _uiSO.FindProperty("slotHover"), "아이템 칸 · 마우스 올림");
            DrawGroup(_uiSO, _uiSO.FindProperty("slotClick"), "아이템 칸 · 클릭");
            DrawGroup(_uiSO, _uiSO.FindProperty("tabClick"), "탭 · 클릭");
            EditorGUILayout.PropertyField(_uiSO.FindProperty("hoverInterval"));
            EditorGUILayout.HelpBox("버튼마다 다른 소리를 쓰려면 그 버튼의 'UI 버튼 소리' 컴포넌트에서 '이 버튼 전용' 칸에 넣으세요.", MessageType.None);
        }

        // ③ 상점
        if (Section("shop", "상점"))
        {
            DrawGroup(_uiSO, _uiSO.FindProperty("purchaseSuccess"), "구매 성공");
            DrawGroup(_uiSO, _uiSO.FindProperty("purchaseFail"), "구매 실패 (골드 부족 등)");
        }

        if (_uiSO.ApplyModifiedProperties()) { EditorUtility.SetDirty(_ui); _ui.InvalidateCache(); }
    }

    private bool Section(string key, string title)
    {
        bool open = !_folded.Contains("s:" + key);
        bool now = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
        if (now != open) { if (now) _folded.Remove("s:" + key); else _folded.Add("s:" + key); }
        return now;
    }

    private void ShowAddWindowMenu()
    {
        var menu = new GenericMenu();
        var names = UISoundSetupTool.KnownWindows.Select(k => (k.name, k.label))
            .Concat(_seenWindows.Select(n => (n, n)))
            .Where(k => _ui.FindWindow(k.Item1) == null)
            .GroupBy(k => k.Item1).Select(g => g.First()).ToList();
        if (names.Count == 0) menu.AddDisabledItem(new GUIContent("추가할 창이 없습니다 (직접 이름을 입력하세요)"));
        foreach (var n in names)
        {
            var item = n;
            menu.AddItem(new GUIContent($"{item.Item2} [{item.Item1}]"), false, () =>
            {
                _uiSO.ApplyModifiedProperties();
                UISoundSetupTool.AddWindow(_ui, item.Item1, item.Item2);
                _uiSO.Update();
            });
        }
        menu.ShowAsContext();
    }

    private static bool HasClips(SerializedProperty window, string group)
    {
        var clips = window.FindPropertyRelative(group).FindPropertyRelative("clips");
        for (int i = 0; i < clips.arraySize; i++) if (clips.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;
        return false;
    }

    // ═════════════════════════════════════════════════════════════════
    // 공용: 소리 묶음(SoundClipGroup) 한 칸
    // ═════════════════════════════════════════════════════════════════
    private void DrawGroup(SerializedObject so, SerializedProperty g, string title)
        => DrawGroup(so, g, title, null, null);

    private void DrawGroup(SerializedObject so, SerializedProperty g, string title, System.Action header, System.Action footer = null)
    {
        bool picked = _pickSO == so && _pickPath == g.propertyPath;
        string key = so.targetObject.GetInstanceID() + ":" + g.propertyPath;
        var prev = GUI.backgroundColor;
        if (picked) GUI.backgroundColor = new Color(1f, 0.85f, 0.55f);
        using (new EditorGUILayout.VerticalScope(_box))
        {
            GUI.backgroundColor = prev;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool folded = _folded.Contains(key);
                if (GUILayout.Button(folded ? "▶" : "▼", EditorStyles.miniLabel, GUILayout.Width(14)))
                { if (folded) _folded.Remove(key); else _folded.Add(key); }
                if (GUILayout.Toggle(picked, new GUIContent("", "켜면 오른쪽 오디오 목록의 [+] 가 이 칸에 추가됩니다."), GUILayout.Width(16)) != picked)
                { if (picked) ClearPick(); else Pick(so, g, title ?? g.FindPropertyRelative("name")?.stringValue); }
                if (title != null) GUILayout.Label(title, EditorStyles.boldLabel, GUILayout.MinWidth(80));
                header?.Invoke();
                if (title != null) GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!AnyClip(g)))
                    if (GUILayout.Button(new GUIContent("▶ 테스트", "볼륨·음높이·무작위 선택을 실제 게임처럼 적용해서 들어봅니다."), GUILayout.Width(64)))
                        PreviewGroup(g);
            }
            if (_folded.Contains(key)) return;

            var clips = g.FindPropertyRelative("clips");
            int removeClip = -1;
            for (int c = 0; c < clips.arraySize; c++)
            {
                var pc = clips.GetArrayElementAtIndex(c);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(32);
                    var clip = pc.objectReferenceValue as AudioClip;
                    using (new EditorGUI.DisabledScope(clip == null))
                        if (GUILayout.Button("▶", GUILayout.Width(24))) PreviewClip(clip, g.FindPropertyRelative("volume").floatValue, 1f);
                    EditorGUILayout.PropertyField(pc, GUIContent.none);
                    if (clip != null) GUILayout.Label($"{clip.length:0.00}초", EditorStyles.miniLabel, GUILayout.Width(44));
                    if (GUILayout.Button("−", GUILayout.Width(22))) removeClip = c;
                }
            }
            if (removeClip >= 0)
            {
                clips.GetArrayElementAtIndex(removeClip).objectReferenceValue = null;
                clips.DeleteArrayElementAtIndex(removeClip);
            }

            var drop = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            drop.xMin += 32;
            GUI.Box(drop, "여기에 오디오 파일을 끌어다 놓으세요 (여러 개 가능)", EditorStyles.centeredGreyMiniLabel);
            HandleDrop(drop, clips);

            EditorGUILayout.PropertyField(g.FindPropertyRelative("volume"));
            DrawPitchRange(g);
            foreach (var field in new[] { "enabled", "volumeVariation", "minPlaybackInterval", "priority", "audibleRange" })
                EditorGUILayout.PropertyField(g.FindPropertyRelative(field));
            footer?.Invoke();
        }
    }

    private static void DrawPitchRange(SerializedProperty g)
    {
        var pMin = g.FindPropertyRelative("pitchMin");
        var pMax = g.FindPropertyRelative("pitchMax");
        float lo = pMin.floatValue, hi = pMax.floatValue;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(new GUIContent("피치 · 재생 속도", "음높이와 속도가 함께 변합니다. 1 = 원본, 1.2 = 높고 빠르게, 0.8 = 낮고 느리게. 두 값을 같게 하면 고정, 다르게 하면 매번 범위 안에서 무작위입니다."));
            lo = EditorGUILayout.FloatField(lo, GUILayout.Width(40));
            EditorGUILayout.MinMaxSlider(ref lo, ref hi, 0.5f, 2f);
            hi = EditorGUILayout.FloatField(hi, GUILayout.Width(40));
        }
        lo = Mathf.Clamp(Mathf.Round(lo * 100f) / 100f, 0.5f, 2f);
        hi = Mathf.Clamp(Mathf.Round(hi * 100f) / 100f, lo, 2f);
        if (!Mathf.Approximately(lo, pMin.floatValue)) pMin.floatValue = lo;
        if (!Mathf.Approximately(hi, pMax.floatValue)) pMax.floatValue = hi;
    }

    private static void ResetGroup(SerializedProperty g, float volume, float pitchMin, float pitchMax)
    {
        g.FindPropertyRelative("clips").ClearArray();
        g.FindPropertyRelative("volume").floatValue = volume;
        g.FindPropertyRelative("pitchMin").floatValue = pitchMin;
        g.FindPropertyRelative("pitchMax").floatValue = pitchMax;
    }

    private void Pick(SerializedObject so, SerializedProperty g, string label)
    {
        _pickSO = so;
        _pickPath = g.propertyPath;
        _pickLabel = string.IsNullOrEmpty(label) ? g.displayName : label;
    }

    private static bool AnyClip(SerializedProperty g)
    {
        var clips = g.FindPropertyRelative("clips");
        for (int i = 0; i < clips.arraySize; i++) if (clips.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;
        return false;
    }

    private static void HandleDrop(Rect area, SerializedProperty clips)
    {
        var ev = Event.current;
        if (!area.Contains(ev.mousePosition) || (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform)) return;
        var dropped = DragAndDrop.objectReferences.OfType<AudioClip>().ToList();
        if (dropped.Count == 0) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (ev.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var c in dropped) AppendClip(clips, c);
        }
        ev.Use();
    }

    private static void AppendClip(SerializedProperty clips, AudioClip clip)
    {
        for (int i = 0; i < clips.arraySize; i++)
            if (clips.GetArrayElementAtIndex(i).objectReferenceValue == clip) return;
        int n = clips.arraySize;
        clips.InsertArrayElementAtIndex(n);
        clips.GetArrayElementAtIndex(n).objectReferenceValue = clip;
    }

    // ═════════════════════════════════════════════════════════════════
    // 오디오 파일 찾기 · 재생 기록
    // ═════════════════════════════════════════════════════════════════
    private void DrawClipBrowser(bool fullList = false)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(fullList ? "전체 음원 목록" : "오디오 파일 찾기", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("새로고침", GUILayout.Width(70))) _clipsDirty = true;
        }
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        if (_clipsDirty || _search != _lastSearch) RunSearch();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label($"검색 {_found.Count} / 전체 {_allClips.Count}개", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (fullList && GUILayout.Button("목록 복사", GUILayout.Width(75)))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", _found.Where(c => c != null)
                    .Select(c => $"{c.name}\t{c.length:0.000}초\t{AssetDatabase.GetAssetPath(c)}"));
        }

        bool canAdd = _pickSO != null && _pickSO.targetObject != null && _pickPath != null;
        if (!fullList) GUILayout.Label(canAdd ? $"[+] → '{_pickLabel}'" : "[+] 를 쓰려면 왼쪽 소리 칸의 체크를 켜세요", EditorStyles.miniLabel);

        _scrollClips = EditorGUILayout.BeginScrollView(_scrollClips);
        int end = Mathf.Min((_clipPage + 1) * ClipsPerPage, _found.Count);
        for (int index = _clipPage * ClipsPerPage; index < end; index++)
        {
            var clip = _found[index];
            if (clip == null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("▶", GUILayout.Width(24))) PreviewClip(clip, 1f, 1f);
                var content = new GUIContent(clip.name, AssetDatabase.GetAssetPath(clip));
                var rect = GUILayoutUtility.GetRect(content, EditorStyles.label, GUILayout.MinWidth(60));
                EditorGUI.LabelField(rect, content);
                var ev = Event.current;
                if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new Object[] { clip };
                    DragAndDrop.StartDrag(clip.name);
                    ev.Use();
                }
                if (ev.type == EventType.MouseDown && ev.clickCount == 2 && rect.Contains(ev.mousePosition)) EditorGUIUtility.PingObject(clip);
                GUILayout.Label($"{clip.length:0.0}초", EditorStyles.miniLabel, GUILayout.Width(36));
                if (fullList && GUILayout.Button("파일 선택", GUILayout.Width(70)))
                { Selection.activeObject = clip; EditorGUIUtility.PingObject(clip); }
                if (!fullList) using (new EditorGUI.DisabledScope(!canAdd))
                {
                    if (GUILayout.Button(new GUIContent("+", "선택한 소리 칸에 추가"), GUILayout.Width(22)))
                    {
                        _pickSO.Update();
                        var g = _pickSO.FindProperty(_pickPath);
                        if (g != null) AppendClip(g.FindPropertyRelative("clips"), clip);
                        if (_pickSO.ApplyModifiedProperties()) EditorUtility.SetDirty(_pickSO.targetObject);
                    }
                }
            }
            if (fullList) EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(clip), EditorStyles.miniLabel, GUILayout.Height(17));
        }
        if (_found.Count == 0) GUILayout.Label("찾은 오디오 파일이 없습니다.", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndScrollView();
        int pages = Mathf.Max(1, Mathf.CeilToInt(_found.Count / (float)ClipsPerPage));
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_clipPage == 0))
                if (GUILayout.Button("이전")) { _clipPage--; _scrollClips = Vector2.zero; }
            GUILayout.Label($"{_clipPage + 1} / {pages}", EditorStyles.centeredGreyMiniLabel);
            using (new EditorGUI.DisabledScope(_clipPage + 1 >= pages))
                if (GUILayout.Button("다음")) { _clipPage++; _scrollClips = Vector2.zero; }
        }
    }

    private void RunSearch()
    {
        _lastSearch = _search;
        _found.Clear();
        if (_clipsDirty)
        {
            _allClips.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) _allClips.Add(clip);
            }
            _allClips.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            _clipsDirty = false;
        }
        string query = _search.Trim();
        _found.AddRange(_allClips.Where(c => c != null && (query.Length == 0 ||
            c.name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            AssetDatabase.GetAssetPath(c).IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)));
        _clipPage = 0;
        _scrollClips = Vector2.zero;
    }

    private void DrawLog()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(Application.isPlaying ? "재생 기록 (플레이 중 실제로 난 소리 · 열리고 닫힌 창)" : "재생 기록 — 플레이하면 실제로 난 소리와 열리고 닫힌 창 이름이 여기에 쌓입니다", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("지우기", GUILayout.Width(50))) _log.Clear();
        }
        _scrollLog = EditorGUILayout.BeginScrollView(_scrollLog, GUILayout.Height(84));
        foreach (var line in _log) GUILayout.Label(line, EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();
    }

    // ═════════════════════════════════════════════════════════════════
    // 미리듣기
    // ═════════════════════════════════════════════════════════════════
    private void PreviewGroup(SerializedProperty g)
    {
        var clips = g.FindPropertyRelative("clips");
        var list = new List<AudioClip>();
        for (int i = 0; i < clips.arraySize; i++)
            if (clips.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip c) list.Add(c);
        if (list.Count == 0) return;
        float lo = g.FindPropertyRelative("pitchMin").floatValue, hi = g.FindPropertyRelative("pitchMax").floatValue;
        PreviewClip(list[Random.Range(0, list.Count)], g.FindPropertyRelative("volume").floatValue, Random.Range(Mathf.Min(lo, hi), Mathf.Max(lo, hi)));
    }

    private void PreviewClip(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        if (_simplePreview)
        {
            CallAudioUtil("StopAllPreviewClips");
            CallAudioUtil("PlayPreviewClip", clip, 0, false);
            return;
        }
        if (_previewSource == null)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags("SoundTestPreview", HideFlags.HideAndDontSave, typeof(AudioSource));
            _previewSource = go.GetComponent<AudioSource>();
            _previewSource.playOnAwake = false;
            _previewSource.spatialBlend = 0f;
        }
        _previewSource.Stop();
        _previewSource.clip = clip;
        _previewSource.volume = volume * _previewVolume;
        _previewSource.pitch = pitch;
        _previewSource.Play();
    }

    private static void StopAll()
    {
        if (_previewSource != null) _previewSource.Stop();
        CallAudioUtil("StopAllPreviewClips");
    }

    private static MethodInfo[] _audioUtil;

    private static void CallAudioUtil(string method, params object[] args)
    {
        if (_audioUtil == null)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            _audioUtil = type != null ? type.GetMethods(BindingFlags.Static | BindingFlags.Public) : new MethodInfo[0];
        }
        var m = _audioUtil.FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        try { m?.Invoke(null, args); } catch { /* 유니티 버전에 따라 없을 수 있음 */ }
    }
}
