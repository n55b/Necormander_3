using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 소리 설정 — 창 열림/닫힘(창마다 다르게), 버튼·아이템 칸 마우스 올림/클릭, 상점 구매 성공/실패.
/// 위치: Assets/Resources/UI/UISoundSettings.asset
/// 소리를 넣고 바로 들어보려면: Tools > 사운드 > 사운드 테스트 툴 → [UI 소리] 탭
/// </summary>
[CreateAssetMenu(fileName = "UISoundSettings", menuName = "사운드/UI 소리 설정", order = 1)]
public class UISoundSettings : ScriptableObject
{
    public const string ResourcePath = "UI/UISoundSettings";

    private static UISoundSettings _active;
    private static bool _searched;
    public static UISoundSettings Active
    {
        get
        {
            // 파일이 없는 빌드에서 매번 Resources.Load 하지 않도록 한 번만 찾는다. (에디터는 도중에 파일을 만들 수 있어 계속 찾음)
            if (_active == null && !_searched)
            {
                _active = Resources.Load<UISoundSettings>(ResourcePath);
                _searched = !Application.isEditor;
            }
            return _active;
        }
    }

    /// <summary>창 하나의 열림/닫힘 소리.</summary>
    [Serializable]
    public class WindowSound
    {
        [KoLabel("창 이름 (코드 이름)"), Tooltip("코드가 UIEventBus.NotifyOpen/Close 로 보내는 이름과 정확히 같아야 합니다. 사운드 테스트 툴의 재생 기록에서 실제 이름을 확인할 수 있습니다.")]
        public string uiName = "";
        [KoLabel("설명"), Tooltip("사람이 알아보기 위한 이름입니다. 게임 동작에는 영향이 없습니다.")]
        public string label = "";
        [KoLabel("열릴 때")] public SoundClipGroup open = new SoundClipGroup();
        [KoLabel("닫힐 때")] public SoundClipGroup close = new SoundClipGroup();
    }

    public enum Cue
    {
        [InspectorName("버튼 · 마우스 올림")] ButtonHover,
        [InspectorName("버튼 · 클릭")]       ButtonClick,
        [InspectorName("아이템 칸 · 마우스 올림")] SlotHover,
        [InspectorName("아이템 칸 · 클릭")]  SlotClick,
        [InspectorName("탭 · 클릭")]         TabClick,
        [InspectorName("상점 · 구매 성공")]  PurchaseSuccess,
        [InspectorName("상점 · 구매 실패")]  PurchaseFail,
    }

    [Header("① 창 열림 · 닫힘")]
    [KoLabel("UI 소리 켜기"), Tooltip("끄면 이 설정의 모든 UI 소리가 나지 않습니다.")]
    public bool soundEnabled = true;
    [KoLabel("기본 열림 소리"), Tooltip("아래 목록에 없는 창, 또는 '열릴 때'가 비어 있는 창에 쓰입니다.")]
    public SoundClipGroup defaultOpen = new SoundClipGroup();
    [KoLabel("기본 닫힘 소리")]
    public SoundClipGroup defaultClose = new SoundClipGroup();
    [KoLabel("창별 소리")]
    public List<WindowSound> windows = new List<WindowSound>();

    [Header("② 버튼 · 아이템 칸")]
    [KoLabel("버튼 · 마우스 올림")] public SoundClipGroup buttonHover = new SoundClipGroup();
    [KoLabel("버튼 · 클릭")] public SoundClipGroup buttonClick = new SoundClipGroup();
    [KoLabel("아이템 칸 · 마우스 올림")] public SoundClipGroup slotHover = new SoundClipGroup();
    [KoLabel("아이템 칸 · 클릭")] public SoundClipGroup slotClick = new SoundClipGroup();
    [KoLabel("탭 · 클릭"), Tooltip("주머니 정렬 버튼처럼 탭 모양 버튼")] public SoundClipGroup tabClick = new SoundClipGroup();
    [KoLabel("마우스 올림 소리 최소 간격 (초)", 0f, 0.5f), Tooltip("마우스를 빠르게 훑을 때 소리가 너무 많이 겹치지 않게 합니다.")]
    public float hoverInterval = 0.05f;

    [Header("③ 상점")]
    [KoLabel("구매 성공"), Tooltip("연출(Feedback)의 'Purchase'에 이미 소리가 들어 있다면 겹치지 않게 비워 두세요.")]
    public SoundClipGroup purchaseSuccess = new SoundClipGroup();
    [KoLabel("구매 실패"), Tooltip("골드가 모자라거나 살 수 없는 상품의 구매 버튼을 눌렀을 때. 강화 상점의 골드 부족도 포함합니다.")]
    public SoundClipGroup purchaseFail = new SoundClipGroup();

    // ─────────────────────────────────────────────────────────────────
    private Dictionary<string, WindowSound> _byName;
    private static float _lastHover = -1f;

    public SoundClipGroup Get(Cue cue)
    {
        switch (cue)
        {
            case Cue.ButtonHover:     return buttonHover;
            case Cue.ButtonClick:     return buttonClick;
            case Cue.SlotHover:       return slotHover;
            case Cue.SlotClick:       return slotClick;
            case Cue.TabClick:        return tabClick;
            case Cue.PurchaseSuccess: return purchaseSuccess;
            case Cue.PurchaseFail:    return purchaseFail;
        }
        return null;
    }

    public WindowSound FindWindow(string uiName)
    {
        if (string.IsNullOrEmpty(uiName)) return null;
        if (_byName == null)
        {
            _byName = new Dictionary<string, WindowSound>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in windows)
                if (w != null && !string.IsNullOrEmpty(w.uiName)) _byName[w.uiName] = w;
        }
        _byName.TryGetValue(uiName, out var found);
        return found;
    }

    /// <summary>창 열림/닫힘 소리. 그 창의 소리가 비어 있으면 기본 소리.</summary>
    public void PlayWindow(string uiName, bool open)
    {
        if (!soundEnabled) return;
        var w = FindWindow(uiName);
        var group = w != null ? (open ? w.open : w.close) : null;
        if (group == null || !group.HasClip) group = open ? defaultOpen : defaultClose;
        group.Play();
    }

    /// <summary>버튼/상점 소리를 재생한다. 설정 파일이 없거나 꺼져 있으면 아무것도 안 한다.</summary>
    public static void PlayCue(Cue cue, SoundClipGroup overrideGroup = null)
    {
        var s = Active;
        if (s == null || !s.soundEnabled) return;
        bool hover = cue == Cue.ButtonHover || cue == Cue.SlotHover;
        if (hover)
        {
            float now = Time.unscaledTime;
            if (now - _lastHover < s.hoverInterval) return;
            _lastHover = now;
        }
        var group = overrideGroup != null && overrideGroup.HasClip ? overrideGroup : s.Get(cue);
        group?.Play();
    }

    /// <summary>창 목록을 코드로 바꾼 뒤 이름 찾기 캐시를 비운다.</summary>
    public void InvalidateCache() => _byName = null;

    private void OnValidate() => _byName = null;
    private void OnEnable() => _byName = null;
}
