using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 상태이상 편집기 (Tools/기획/상태이상 편집기).
///
///  · 왼쪽  : 상태이상 목록. 지금 수치를 한 줄로 요약해서 보여준다.
///  · 한눈에 보기: 전 상태이상의 지속시간 · 피해 주기 · 1회 피해 · 초당 피해 · 총 피해를 한 표로 비교한다.
///  · 상태이상별: 한글 이름 + 단위 + 설명으로 수치를 고치고, 피해가 언제 몇 번 들어가는지 타임라인으로 보여준다.
///
/// 수치는 전부 Resources/Settings/StatusBalance.asset(StatusBalanceSO) 하나에 저장된다.
/// 편집은 SerializedObject 를 거치므로 Ctrl+Z 가 되고, '저장' 버튼이나 Ctrl+S 때 디스크에 기록된다.
/// 런타임(StatusRules)은 에셋 값을 매번 읽으므로 플레이 중에 고쳐도 바로 반영된다.
/// </summary>
public class StatusEffectEditorWindow : EditorWindow
{
    private const string AssetPath = "Assets/Resources/Settings/StatusBalance.asset";
    private const string PrefSampleAtk = "StatusEffectEditor.SampleAtk";

    [MenuItem("Tools/기획/상태이상 편집기", priority = 8)]
    public static void Open()
    {
        var w = GetWindow<StatusEffectEditorWindow>("상태이상 편집기");
        w.minSize = new Vector2(860f, 560f);
        w.Show();
    }

    // ── 페이지 정의 ─────────────────────────────────────────────────
    private enum Page { Overview, Common, Stun, Freeze, Bleed, Poison, BloodPop, Vulnerable, Hitstun }

    private class PageInfo
    {
        public Page page;
        public string title;
        public string desc;
        public Color color;
        public StatusType icon;
        public string[] fields; // '기본값으로' 버튼이 되돌릴 필드
    }

    private static readonly PageInfo[] Pages =
    {
        new PageInfo { page = Page.Overview, title = "한눈에 보기", desc = "모든 상태이상 수치를 한 표로 비교합니다.", color = new Color(0.55f, 0.6f, 0.7f), icon = StatusType.None, fields = new string[0] },
        new PageInfo { page = Page.Common, title = "공통 지속시간", desc = "출혈 · 중독 · 비폭 · 취약이 함께 쓰는 지속시간입니다.", color = new Color(0.7f, 0.7f, 0.7f), icon = StatusType.None,
            fields = new[] { "defaultDuration" } },
        new PageInfo { page = Page.Stun, title = "기절", desc = "행동 완전 불가. 피해는 없습니다.", color = new Color(1f, 0.85f, 0.3f), icon = StatusType.Stun,
            fields = new[] { "stunDuration", "stunMinDuration", "stunMaxDuration", "stunImmunityDuration" } },
        new PageInfo { page = Page.Freeze, title = "빙결", desc = "행동 불가. 맞으면 깨지면서 고정 피해를 받고 풀립니다.", color = new Color(0.45f, 0.85f, 1f), icon = StatusType.Freeze,
            fields = new[] { "freezeDuration", "freezeImmunityDuration", "freezeBreakDamage" } },
        new PageInfo { page = Page.Bleed, title = "출혈", desc = "건 공격자의 공격력에 비례하는 피해를 일정 간격으로 줍니다(물리 피해).", color = new Color(1f, 0.35f, 0.35f), icon = StatusType.Bleed,
            fields = new[] { "bleedAtkRatio", "bleedTickInterval", "bleedFallbackTickDamage" } },
        new PageInfo { page = Page.Poison, title = "중독", desc = "고정 피해를 일정 간격으로 줍니다.", color = new Color(0.5f, 0.9f, 0.4f), icon = StatusType.Poison,
            fields = new[] { "poisonTickDamage", "poisonTickInterval" } },
        new PageInfo { page = Page.BloodPop, title = "비폭 (혈액 튀어오름)", desc = "스택형. 정해진 스택에 닿으면 자신과 주변에 폭발합니다.", color = new Color(0.85f, 0.35f, 0.75f), icon = StatusType.BloodPop,
            fields = new[] { "bloodPopThreshold", "bloodPopDamage", "bloodPopRadius", "bloodPopFuse", "bloodPopHitDuration" } },
        new PageInfo { page = Page.Vulnerable, title = "취약", desc = "받는 물리 피해(출혈 포함)가 늘어납니다. 행동 제한은 없습니다.", color = new Color(1f, 0.6f, 0.25f), icon = StatusType.Vulnerable,
            fields = new[] { "vulnerableDamageBonus" } },
        new PageInfo { page = Page.Hitstun, title = "경직", desc = "평타에 묻는 짧은 행동 불가. 공통 수치가 없습니다.", color = new Color(0.6f, 0.6f, 0.65f), icon = StatusType.Hitstun, fields = new string[0] },
    };

    // ── 상태 ────────────────────────────────────────────────────────
    private StatusBalanceSO _asset;
    private SerializedObject _so;
    private readonly Dictionary<string, float> _defaults = new Dictionary<string, float>();
    private DebuffDataSO _icons;

    private Page _page = Page.Overview;
    private Vector2 _listScroll, _bodyScroll;
    private float _sampleAtk = 100f;

    private GUIStyle _title, _hint, _fieldLabel, _sideTitle, _sideSub, _cellBold, _cell, _changedTag, _bigNumber;
    private bool _stylesReady;

    private void OnEnable()
    {
        _sampleAtk = EditorPrefs.GetFloat(PrefSampleAtk, 100f);
        CacheDefaults();
        Reload();
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.projectChanged += Reload;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.projectChanged -= Reload;
        // 기본값은 숫자만 들고 있어 정리할 것이 없다.
    }

    private void OnUndoRedo() { _so?.Update(); Repaint(); }

    private void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

    private void Reload()
    {
        _asset = AssetDatabase.LoadAssetAtPath<StatusBalanceSO>(AssetPath);
        if (_asset == null) _asset = Resources.Load<StatusBalanceSO>(StatusRules.BalanceResourcePath);
        _so = _asset != null ? new SerializedObject(_asset) : null;

        _icons = null;
        foreach (var g in AssetDatabase.FindAssets("t:DebuffDataSO"))
        {
            _icons = AssetDatabase.LoadAssetAtPath<DebuffDataSO>(AssetDatabase.GUIDToAssetPath(g));
            if (_icons != null) break;
        }
        Repaint();
    }

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;
        _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        _hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = new Color(0.62f, 0.62f, 0.62f) } };
        _fieldLabel = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
        _sideTitle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, richText = true };
        _sideSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        _cellBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        _cell = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false };
        _changedTag = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.7f, 0.3f) } };
        _bigNumber = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, richText = true };
    }

    // ── 값 읽기 도우미 ─────────────────────────────────────────────
    private float F(string prop)
    {
        var p = _so.FindProperty(prop);
        if (p == null) return 0f;
        return p.propertyType == SerializedPropertyType.Integer ? p.intValue : p.floatValue;
    }

private float Def(string prop) => _defaults.TryGetValue(prop, out var v) ? v : 0f;

    /// <summary>StatusBalanceSO 필드 초기값(코드 기본값)을 읽어 둔다. 임시 인스턴스는 바로 지운다.</summary>
    private void CacheDefaults()
    {
        _defaults.Clear();
        var tmp = CreateInstance<StatusBalanceSO>();
        foreach (var fi in typeof(StatusBalanceSO).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (fi.FieldType == typeof(float)) _defaults[fi.Name] = (float)fi.GetValue(tmp);
            else if (fi.FieldType == typeof(int)) _defaults[fi.Name] = (int)fi.GetValue(tmp);
        }
        DestroyImmediate(tmp);
    }

    private static float MinOf(string prop)
    {
        var fi = typeof(StatusBalanceSO).GetField(prop, BindingFlags.Public | BindingFlags.Instance);
        var min = fi != null ? fi.GetCustomAttribute<MinAttribute>() : null;
        return min != null ? min.min : float.MinValue;
    }

    private static int Ticks(float duration, float interval)
        => interval <= 0f ? 0 : Mathf.FloorToInt(duration / interval + 0.0001f);

    private static string N(float v) => v.ToString("0.##");

    // ── GUI ─────────────────────────────────────────────────────────
    private void OnGUI()
    {
        InitStyles();

        if (_so == null)
        {
            DrawMissingAsset();
            return;
        }
        _so.Update();

        DrawToolbar();
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSidebar();
            using (new EditorGUILayout.VerticalScope())
            {
                _bodyScroll = EditorGUILayout.BeginScrollView(_bodyScroll);
                GUILayout.Space(6f);
                DrawPage();
                GUILayout.Space(12f);
                EditorGUILayout.EndScrollView();
            }
        }

        _so.ApplyModifiedProperties();
    }

    private void DrawMissingAsset()
    {
        EditorGUILayout.Space(20f);
        EditorGUILayout.HelpBox("상태이상 수치 에셋이 없습니다.\n" + AssetPath + "\n\n없으면 게임은 코드에 적힌 기본값을 씁니다.", MessageType.Warning);
        if (GUILayout.Button("수치 에셋 만들기 (기본값으로)", GUILayout.Height(30f)))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Settings")) AssetDatabase.CreateFolder("Assets/Resources", "Settings");
            var created = CreateInstance<StatusBalanceSO>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            Reload();
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            bool dirty = EditorUtility.IsDirty(_asset);
            var saveLabel = dirty ? "● 저장" : "저장됨";
            using (new EditorGUI.DisabledScope(!dirty))
                if (GUILayout.Button(new GUIContent(saveLabel, "수정한 수치를 파일에 저장합니다 (Ctrl+S 와 같음)"), EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    AssetDatabase.SaveAssetIfDirty(_asset);
                    ShowNotification(new GUIContent("저장했습니다"));
                }
            if (dirty)
                GUILayout.Label("저장하지 않은 변경이 있습니다  (Ctrl+Z 로 되돌리기 가능)", _changedTag);

            GUILayout.FlexibleSpace();
            if (Application.isPlaying)
            {
                var c = GUI.color; GUI.color = new Color(0.5f, 1f, 0.6f);
                GUILayout.Label(new GUIContent("▶ 플레이 중 — 고치면 바로 반영되고, 플레이를 멈춰도 남습니다",
                    "SO 에셋 수정은 플레이 종료 시 되돌아가지 않습니다. 시험용으로 바꿨다면 원래 값을 기록해 두세요."), EditorStyles.miniBoldLabel);
                GUI.color = c;
            }
            if (GUILayout.Button(new GUIContent("에셋 위치", "프로젝트 창에서 수치 에셋을 보여줍니다"), EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                Selection.activeObject = _asset;
                EditorGUIUtility.PingObject(_asset);
            }
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(60f))) Reload();
        }
    }

    // ── 왼쪽 목록 ───────────────────────────────────────────────────
    private void DrawSidebar()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(220f)))
        {
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, "box");
            foreach (var info in Pages)
            {
                Rect r = GUILayoutUtility.GetRect(200f, 42f, GUILayout.ExpandWidth(true));
                bool sel = info.page == _page;
                if (sel) EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                else if (r.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.05f));
                EditorGUI.DrawRect(new Rect(r.x, r.y + 3f, 4f, r.height - 6f), info.color);

                var iconRect = new Rect(r.x + 10f, r.y + 7f, 28f, 28f);
                var sprite = info.icon != StatusType.None && _icons != null ? _icons.GetSprite(info.icon) : null;
                if (sprite != null) DrawSprite(iconRect, sprite);
                else EditorGUI.DrawRect(new Rect(iconRect.x + 8f, iconRect.y + 8f, 12f, 12f), info.color);

                bool changed = false;
                foreach (var f in info.fields) if (!Mathf.Approximately(F(f), Def(f))) { changed = true; break; }
                string name = info.title + (changed ? "  <color=#ffb44d><size=9>수정됨</size></color>" : "");
                GUI.Label(new Rect(r.x + 44f, r.y + 4f, r.width - 48f, 18f), name, _sideTitle);
                GUI.Label(new Rect(r.x + 44f, r.y + 22f, r.width - 48f, 16f), Summary(info.page), _sideSub);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _page = info.page;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("수치는 모든 유닛이 함께 씁니다.\n공격·스킬에 지속시간을 따로 적으면 그 값이 우선합니다.", MessageType.None);
        }
    }

    private string Summary(Page p)
    {
        float dur = F("defaultDuration");
        switch (p)
        {
            case Page.Overview: return "전체 비교표";
            case Page.Common: return $"{N(dur)}초";
            case Page.Stun: return $"{N(F("stunDuration"))}초 · 내성 {N(F("stunImmunityDuration"))}초";
            case Page.Freeze: return $"{N(F("freezeDuration"))}초 · 깨짐 피해 {N(F("freezeBreakDamage"))}";
            case Page.Bleed: return $"ATK {N(F("bleedAtkRatio") * 100f)}% / {N(F("bleedTickInterval"))}초마다";
            case Page.Poison: return $"{N(F("poisonTickDamage"))} 피해 / {N(F("poisonTickInterval"))}초마다";
            case Page.BloodPop: return $"{N(F("bloodPopThreshold"))}스택 → {N(F("bloodPopDamage"))} 피해";
            case Page.Vulnerable: return $"물리 피해 +{N(F("vulnerableDamageBonus") * 100f)}%";
            case Page.Hitstun: return "공격마다 따로 설정";
        }
        return "";
    }

    // ── 본문 ────────────────────────────────────────────────────────
    private void DrawPage()
    {
        PageInfo info = Array.Find(Pages, x => x.page == _page);
        DrawHeader(info);

        switch (_page)
        {
            case Page.Overview: DrawOverview(); break;
            case Page.Common: DrawCommon(); break;
            case Page.Stun: DrawStun(); break;
            case Page.Freeze: DrawFreeze(); break;
            case Page.Bleed: DrawBleed(); break;
            case Page.Poison: DrawPoison(); break;
            case Page.BloodPop: DrawBloodPop(); break;
            case Page.Vulnerable: DrawVulnerable(); break;
            case Page.Hitstun: DrawHitstun(); break;
        }
    }

    private void DrawHeader(PageInfo info)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(4f);
            Rect bar = GUILayoutUtility.GetRect(5f, 40f, GUILayout.Width(5f));
            EditorGUI.DrawRect(bar, info.color);
            GUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(info.title, _title);
                GUILayout.Label(info.desc, EditorStyles.wordWrappedLabel);
            }
            GUILayout.FlexibleSpace();
            if (info.fields.Length > 0)
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(140f)))
                {
                    GUILayout.Space(8f);
                    if (GUILayout.Button(new GUIContent("이 항목 기본값으로", "이 페이지의 수치를 처음 값으로 되돌립니다 (Ctrl+Z 가능)"))
                        && EditorUtility.DisplayDialog("기본값으로 되돌리기", $"'{info.title}' 수치를 모두 기본값으로 되돌릴까요?", "되돌리기", "취소"))
                    {
                        foreach (var f in info.fields) ResetField(f);
                    }
                }
            }
        }
        GUILayout.Space(8f);
    }

private void ResetField(string prop)
    {
        var p = _so.FindProperty(prop);
        if (p == null || !_defaults.TryGetValue(prop, out var d)) return;
        if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(d);
        else p.floatValue = d;
    }

    /// <summary>
    /// 기획자용 수치 한 줄: [이름] [값] [단위] [기본값 표시 · 되돌리기] + 아래 설명.
    /// percent 면 0.1 을 10% 로 보여주고 입력받는다.
    /// </summary>
    private void Field(string prop, string label, string unit, bool percent = false, string extraHint = null)
    {
        var p = _so.FindProperty(prop);
        if (p == null) { EditorGUILayout.HelpBox($"'{prop}' 수치를 찾지 못했습니다. StatusBalanceSO 가 바뀌었는지 확인하세요.", MessageType.Error); return; }

        bool isInt = p.propertyType == SerializedPropertyType.Integer;
        float k = percent ? 100f : 1f;
        float min = MinOf(prop);
        float cur = F(prop), def = Def(prop);
        bool changed = !Mathf.Approximately(cur, def);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            Rect line = EditorGUILayout.BeginHorizontal();
            if (changed && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(line.x - 3f, line.y, 3f, line.height), new Color(1f, 0.7f, 0.3f));

            GUILayout.Label(new GUIContent(label, p.tooltip), _fieldLabel, GUILayout.Width(190f));
            EditorGUI.BeginChangeCheck();
            if (isInt)
            {
                int v = EditorGUILayout.IntField(p.intValue, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck()) p.intValue = Mathf.Max((int)min, v);
            }
            else
            {
                float v = EditorGUILayout.FloatField(p.floatValue * k, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck()) p.floatValue = Mathf.Max(min, v / k);
            }
            GUILayout.Label(unit, GUILayout.Width(44f));

            if (changed)
            {
                GUILayout.Label($"기본값 {N(def * k)}{unit}", _changedTag, GUILayout.Width(110f));
                if (GUILayout.Button(new GUIContent("↺", "기본값으로 되돌리기"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    ResetField(prop);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(p.tooltip)) GUILayout.Label(p.tooltip, _hint);
            if (!string.IsNullOrEmpty(extraHint)) GUILayout.Label(extraHint, _hint);
        }
    }

    private void Section(string title)
    {
        GUILayout.Space(6f);
        GUILayout.Label(title, EditorStyles.boldLabel);
    }

    /// <summary>계산 결과 카드: 큰 숫자 + 설명.</summary>
    private void Result(params (string label, string value)[] items)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var (label, value) in items)
            {
                using (new EditorGUILayout.VerticalScope("box", GUILayout.MinWidth(120f)))
                {
                    GUILayout.Label(label, EditorStyles.miniLabel);
                    GUILayout.Label(value, _bigNumber);
                }
            }
            GUILayout.FlexibleSpace();
        }
    }

    // ── 타임라인 ────────────────────────────────────────────────────
    private struct Span { public string label; public float length; public Color color; public Span(string l, float len, Color c) { label = l; length = len; color = c; } }

    /// <summary>시간 막대. spans 를 차례로 잇고, ticks 시각에 피해 표시(▼)를 찍는다.</summary>
    private void Timeline(Span[] spans, float tickInterval, float tickUntil, Color tickColor, string tickNote)
    {
        float total = 0f;
        foreach (var s in spans) total += Mathf.Max(0f, s.length);
        total = Mathf.Max(total, 0.01f);

        Rect r = GUILayoutUtility.GetRect(100f, 62f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.22f));
        Rect bar = new Rect(r.x + 8f, r.y + 20f, r.width - 16f, 16f);
        float X(float t) => bar.x + bar.width * Mathf.Clamp01(t / total);

        float t0 = 0f;
        foreach (var s in spans)
        {
            if (s.length <= 0f) continue;
            var sr = new Rect(X(t0), bar.y, Mathf.Max(1f, X(t0 + s.length) - X(t0)), bar.height);
            EditorGUI.DrawRect(sr, s.color);
            if (sr.width > 40f) GUI.Label(sr, $" {s.label} {N(s.length)}초", EditorStyles.miniLabel);
            t0 += s.length;
        }

        int ticks = Ticks(tickUntil, tickInterval);
        int drawn = Mathf.Min(ticks, 300);
        for (int i = 1; i <= drawn; i++)
        {
            float x = X(i * tickInterval);
            EditorGUI.DrawRect(new Rect(x - 1f, bar.y - 6f, 2f, bar.height + 6f), tickColor);
        }

        // 눈금(초)
        int step = total > 20f ? 5 : total > 8f ? 2 : 1;
        for (int s = 0; s <= Mathf.FloorToInt(total); s += step)
        {
            float x = X(s);
            EditorGUI.DrawRect(new Rect(x, bar.yMax, 1f, 4f), new Color(1f, 1f, 1f, 0.35f));
            GUI.Label(new Rect(x - 14f, bar.yMax + 4f, 40f, 14f), s + "초", EditorStyles.miniLabel);
        }

        if (!string.IsNullOrEmpty(tickNote))
            GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 16f), tickNote, EditorStyles.miniLabel);
    }

    // ── 한눈에 보기 ─────────────────────────────────────────────────
    private static readonly (string title, float width)[] OverviewCols =
    {
        ("상태이상", 120f), ("지속시간", 90f), ("피해 간격", 80f), ("1회 피해", 110f), ("초당 피해", 80f), ("총 피해", 90f), ("특이사항", 230f), ("", 50f),
    };

    private void DrawOverview()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent("출혈 계산용 공격력", "출혈 피해는 건 공격자의 공격력에 비례합니다. 이 값으로 예시 피해를 계산합니다(저장되지 않는 계산용 값)."), GUILayout.Width(120f));
            EditorGUI.BeginChangeCheck();
            _sampleAtk = Mathf.Max(0f, EditorGUILayout.FloatField(_sampleAtk, GUILayout.Width(70f)));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetFloat(PrefSampleAtk, _sampleAtk);
            GUILayout.Label("← 예시 계산용 (게임에 영향 없음)", _hint);
            GUILayout.FlexibleSpace();
        }
        GUILayout.Space(4f);

        float total = 0f;
        foreach (var c in OverviewCols) total += c.width + 4f;

        Rect head = GUILayoutUtility.GetRect(total, 22f, GUILayout.Width(total));
        EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.3f));
        float hx = head.x;
        foreach (var c in OverviewCols) { GUI.Label(new Rect(hx + 4f, head.y + 3f, c.width, 18f), c.title, EditorStyles.miniBoldLabel); hx += c.width + 4f; }

        float dur = F("defaultDuration");
        float bleedTick = _sampleAtk * F("bleedAtkRatio");
        float bleedInt = F("bleedTickInterval");
        float poisonTick = F("poisonTickDamage");
        float poisonInt = F("poisonTickInterval");

        Row(Page.Stun, $"{N(F("stunDuration"))}초", "-", "-", "-", "-",
            $"공격 지정 시 {N(F("stunMinDuration"))}~{N(F("stunMaxDuration"))}초로 제한 · 끝나면 {N(F("stunImmunityDuration"))}초 내성");
        Row(Page.Freeze, $"{N(F("freezeDuration"))}초", "-", $"깨질 때 {N(F("freezeBreakDamage"))}", "-", $"{N(F("freezeBreakDamage"))}",
            $"맞으면 깨짐 · 깨진 뒤 {N(F("freezeImmunityDuration"))}초 내성");
        Row(Page.Bleed, $"{N(dur)}초 <color=#999>(공통)</color>", $"{N(bleedInt)}초", $"ATK×{N(F("bleedAtkRatio") * 100f)}% = {N(bleedTick)}",
            N(bleedTick / bleedInt), $"{N(bleedTick * Ticks(dur, bleedInt))} <color=#999>({Ticks(dur, bleedInt)}회)</color>", $"공격력 {N(_sampleAtk)} 기준 · 물리 피해(취약 적용)");
        Row(Page.Poison, $"{N(dur)}초 <color=#999>(공통)</color>", $"{N(poisonInt)}초", N(poisonTick),
            N(poisonTick / poisonInt), $"{N(poisonTick * Ticks(dur, poisonInt))} <color=#999>({Ticks(dur, poisonInt)}회)</color>", "고정 피해");
        Row(Page.BloodPop, $"{N(dur)}초 <color=#999>(공통)</color>", "-", $"폭발 {N(F("bloodPopDamage"))}", "-", "-",
            $"{N(F("bloodPopThreshold"))}스택에 폭발 · 반지름 {N(F("bloodPopRadius"))} · 예고 {N(F("bloodPopFuse"))}초");
        Row(Page.Vulnerable, $"{N(dur)}초 <color=#999>(공통)</color>", "-", "-", "-", "-",
            $"받는 물리 피해 +{N(F("vulnerableDamageBonus") * 100f)}%");
        Row(Page.Hitstun, "공격별", "-", "-", "-", "-", "평타·공격마다 따로 정합니다");

        GUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "· 지속시간에 '(공통)'이 붙은 항목은 '공통 지속시간' 하나를 같이 씁니다. 하나만 바꾸려면 공격/스킬 쪽에서 지속시간을 직접 적으세요.\n" +
            "· 총 피해 = 1회 피해 × 지속시간 동안 들어가는 횟수(대략). 다시 걸리면 지속시간이 갱신됩니다.\n" +
            "· 표의 '편집' 또는 왼쪽 목록을 눌러 수치를 고칩니다.", MessageType.Info);
    }

    private void Row(Page page, string duration, string interval, string perHit, string dps, string totalDmg, string note)
    {
        PageInfo info = Array.Find(Pages, x => x.page == page);
        float total = 0f;
        foreach (var c in OverviewCols) total += c.width + 4f;
        Rect line = GUILayoutUtility.GetRect(total, 26f, GUILayout.Width(total));
        if (line.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.05f));
        EditorGUI.DrawRect(new Rect(line.x, line.y + 3f, 4f, line.height - 6f), info.color);
        EditorGUI.DrawRect(new Rect(line.x, line.yMax - 1f, line.width, 1f), new Color(1f, 1f, 1f, 0.06f));

        string[] values = { info.title, duration, interval, perHit, dps, totalDmg, note };
        float x = line.x;
        for (int i = 0; i < values.Length; i++)
        {
            var rc = new Rect(x + 8f, line.y + 4f, OverviewCols[i].width - 4f, 18f);
            GUI.Label(rc, new GUIContent(values[i], values[i]), i == 0 ? _cellBold : _cell);
            x += OverviewCols[i].width + 4f;
        }
        if (GUI.Button(new Rect(x + 4f, line.y + 4f, OverviewCols[OverviewCols.Length - 1].width - 4f, 18f), "편집", EditorStyles.miniButton))
        {
            _page = page;
            GUI.FocusControl(null);
        }
    }

    // ── 상태이상별 페이지 ───────────────────────────────────────────
    private void DrawSharedDuration(string who)
    {
        Field("defaultDuration", "지속시간 (공통)", "초", extraHint: "⚠ " + who + "이(가) 같이 쓰는 값입니다. 여기서 바꾸면 모두 바뀝니다.");
    }

    private void DrawCommon()
    {
        Field("defaultDuration", "공통 지속시간", "초");
        Section("이 값을 쓰는 상태이상");
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var p in new[] { Page.Bleed, Page.Poison, Page.BloodPop, Page.Vulnerable })
            {
                var info = Array.Find(Pages, x => x.page == p);
                var c = GUI.backgroundColor;
                GUI.backgroundColor = info.color;
                if (GUILayout.Button(info.title.Split(' ')[0], GUILayout.Height(26f), GUILayout.Width(110f))) _page = p;
                GUI.backgroundColor = c;
            }
            GUILayout.FlexibleSpace();
        }
        GUILayout.Label("기절 · 빙결은 각자 따로 지속시간이 있습니다. 공격이나 스킬에 지속시간을 직접 적으면 그 값이 우선합니다.", _hint);
    }

    private void DrawStun()
    {
        Section("지속시간");
        Field("stunDuration", "기본 지속시간", "초");
        Field("stunMinDuration", "최소 시간", "초");
        Field("stunMaxDuration", "최대 시간", "초");
        if (F("stunMaxDuration") < F("stunMinDuration"))
            EditorGUILayout.HelpBox("최대 시간이 최소 시간보다 작습니다. 게임에서는 최대 시간 = 최소 시간으로 처리됩니다.", MessageType.Warning);

        Section("내성");
        Field("stunImmunityDuration", "기절 후 내성 시간", "초");

        Section("미리보기");
        var col = Array.Find(Pages, x => x.page == Page.Stun).color;
        Timeline(new[]
        {
            new Span("기절", F("stunDuration"), new Color(col.r, col.g, col.b, 0.75f)),
            new Span("내성", F("stunImmunityDuration"), new Color(0.5f, 0.5f, 0.5f, 0.5f)),
        }, 0f, 0f, col, "기본 기절 → 자연 종료 → 내성 (이 동안 다시 기절하지 않음)");
        Result(("공격에 시간을 적은 기절", $"{N(F("stunMinDuration"))} ~ {N(Mathf.Max(F("stunMinDuration"), F("stunMaxDuration")))}초"),
               ("다시 기절할 수 있기까지", $"{N(F("stunDuration") + F("stunImmunityDuration"))}초"));
        GUILayout.Label("보스 그로기처럼 제한을 무시하는 기절은 최소/최대 시간을 적용하지 않습니다. 슈퍼아머 중에는 걸리지 않습니다.", _hint);
    }

    private void DrawFreeze()
    {
        Section("지속시간");
        Field("freezeDuration", "기본 지속시간", "초");
        Section("깨질 때");
        Field("freezeBreakDamage", "깨짐 피해", "피해");
        Field("freezeImmunityDuration", "깨진 뒤 내성 시간", "초");

        Section("미리보기");
        var col = Array.Find(Pages, x => x.page == Page.Freeze).color;
        Timeline(new[]
        {
            new Span("빙결", F("freezeDuration"), new Color(col.r, col.g, col.b, 0.75f)),
        }, 0f, 0f, col, "맞지 않으면 끝까지 얼어 있다가 자연 종료 (내성 없음)");
        Timeline(new[]
        {
            new Span("빙결", F("freezeDuration") * 0.4f, new Color(col.r, col.g, col.b, 0.75f)),
            new Span("내성", F("freezeImmunityDuration"), new Color(0.5f, 0.5f, 0.5f, 0.5f)),
        }, F("freezeDuration") * 0.4f, F("freezeDuration") * 0.4f, new Color(1f, 0.4f, 0.4f), $"중간에 맞으면 ▼ 깨짐 피해 {N(F("freezeBreakDamage"))} + 즉시 해제 → 내성");
        GUILayout.Label("무기 전용 빙결 효과(무기에 적힌 추가 피해)는 이 수치와 별개로 더해집니다. 슈퍼아머 중에는 걸리지 않습니다.", _hint);
    }

    private void DrawBleed()
    {
        Section("피해");
        Field("bleedAtkRatio", "1회 피해 (공격력 비율)", "%");
        Field("bleedTickInterval", "피해 간격", "초");
        Field("bleedFallbackTickDamage", "공격력 모를 때 1회 피해", "피해");
        Section("지속시간");
        DrawSharedDuration("출혈 · 중독 · 비폭 · 취약");

        Section("미리보기");
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("예시 공격력", GUILayout.Width(80f));
            EditorGUI.BeginChangeCheck();
            _sampleAtk = Mathf.Max(0f, EditorGUILayout.FloatField(_sampleAtk, GUILayout.Width(70f)));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetFloat(PrefSampleAtk, _sampleAtk);
            GUILayout.Label("← 계산용 (게임에 영향 없음)", _hint);
            GUILayout.FlexibleSpace();
        }
        float dur = F("defaultDuration"), interval = F("bleedTickInterval");
        float tick = _sampleAtk * F("bleedAtkRatio");
        int n = Ticks(dur, interval);
        var col = Array.Find(Pages, x => x.page == Page.Bleed).color;
        Timeline(new[] { new Span("출혈", dur, new Color(col.r, col.g, col.b, 0.35f)) }, interval, dur, col, $"▼ = 피해 {N(tick)}  (총 {n}회)");
        Result(("1회 피해", N(tick)), ("초당 피해", N(tick / interval)), ("총 피해", $"{N(tick * n)}"),
               ("취약 상태일 때 총 피해", N(tick * n * (1f + F("vulnerableDamageBonus")))));
        GUILayout.Label("걸리는 순간의 공격력을 기억합니다. 이후 공격력이 바뀌어도 그 출혈의 피해는 그대로입니다.", _hint);
    }

    private void DrawPoison()
    {
        Section("피해");
        Field("poisonTickDamage", "1회 피해", "피해");
        Field("poisonTickInterval", "피해 간격", "초");
        Section("지속시간");
        DrawSharedDuration("출혈 · 중독 · 비폭 · 취약");

        Section("미리보기");
        float dur = F("defaultDuration"), interval = F("poisonTickInterval"), tick = F("poisonTickDamage");
        int n = Ticks(dur, interval);
        var col = Array.Find(Pages, x => x.page == Page.Poison).color;
        Timeline(new[] { new Span("중독", dur, new Color(col.r, col.g, col.b, 0.35f)) }, interval, dur, col, $"▼ = 피해 {N(tick)}  (총 {n}회)");
        Result(("1회 피해", N(tick)), ("초당 피해", N(tick / interval)), ("총 피해", N(tick * n)));
    }

    private void DrawBloodPop()
    {
        Section("스택");
        Field("bloodPopThreshold", "폭발 스택", "스택");

        // 스택 그림
        int th = Mathf.Clamp((int)F("bloodPopThreshold"), 1, 40);
        var col = Array.Find(Pages, x => x.page == Page.BloodPop).color;
        Rect r = GUILayoutUtility.GetRect(100f, 22f, GUILayout.ExpandWidth(true));
        float w = Mathf.Min(18f, (r.width - 80f) / th - 3f);
        for (int i = 0; i < th; i++)
            EditorGUI.DrawRect(new Rect(r.x + 8f + i * (w + 3f), r.y + 4f, w, 14f), i == th - 1 ? new Color(1f, 0.3f, 0.3f) : new Color(col.r, col.g, col.b, 0.7f));
        GUI.Label(new Rect(r.x + 12f + th * (w + 3f), r.y + 3f, 80f, 18f), "→ 폭발!", EditorStyles.miniBoldLabel);

        Section("폭발");
        Field("bloodPopDamage", "폭발 피해", "피해");
        Field("bloodPopRadius", "폭발 반지름", "유닛");
        Field("bloodPopFuse", "폭발 예고 시간", "초");
        Field("bloodPopHitDuration", "타격 판정 유지 시간", "초");

        Section("지속시간");
        DrawSharedDuration("출혈 · 중독 · 비폭 · 취약");

        Section("미리보기");
        Timeline(new[]
        {
            new Span("예고", F("bloodPopFuse"), new Color(1f, 0.85f, 0.3f, 0.6f)),
            new Span("판정", F("bloodPopHitDuration"), new Color(1f, 0.3f, 0.3f, 0.8f)),
        }, 0f, 0f, col, $"{th}스택 도달 → 예고 표시 → 폭발 판정 (자신과 반지름 {N(F("bloodPopRadius"))} 안의 대상)");
        Result(("폭발 피해", N(F("bloodPopDamage"))), ("범위 지름", $"{N(F("bloodPopRadius") * 2f)} 유닛"));
    }

    private void DrawVulnerable()
    {
        Section("효과");
        Field("vulnerableDamageBonus", "받는 물리 피해 증가", "%");
        Section("지속시간");
        DrawSharedDuration("출혈 · 중독 · 비폭 · 취약");

        Section("미리보기");
        float b = F("vulnerableDamageBonus");
        Result(("물리 피해 100 →", N(100f * (1f + b))), ("물리 피해 250 →", N(250f * (1f + b))));
        GUILayout.Label("물리 피해(출혈 틱 포함)에만 적용됩니다. 중독·비폭·빙결 깨짐 같은 다른 피해 유형에는 적용되지 않습니다. 용사 계열 전용입니다.", _hint);
    }

    private void DrawHitstun()
    {
        EditorGUILayout.HelpBox(
            "경직은 공통 수치가 없습니다.\n" +
            "평타·공격마다 지속시간을 따로 넘기므로, 해당 공격/무기/소환수 설정에서 조절하세요.\n\n" +
            "기절과 따로 둔 이유: '기절 시간 증가' 같은 효과가 경직에는 붙지 않게 하기 위함입니다.\n" +
            "걸릴 때 화면에 글자를 띄우지 않습니다(평타마다 묻기 때문).", MessageType.Info);
    }

    // ── 유틸 ────────────────────────────────────────────────────────
    private static void DrawSprite(Rect r, Sprite s)
    {
        if (s == null || s.texture == null) return;
        var tr = s.textureRect;
        float k = Mathf.Min(r.width / tr.width, r.height / tr.height);
        var dst = new Rect(0f, 0f, tr.width * k, tr.height * k) { center = r.center };
        var tex = s.texture;
        GUI.DrawTextureWithTexCoords(dst, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
    }
}
