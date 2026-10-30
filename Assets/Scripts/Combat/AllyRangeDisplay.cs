using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아군(소환수) 공격 범위 표시 on/off 스위치 — 에디터 디버그 전용.
/// 소환수 마무리 일격 · Space 스킬 · 대쉬 피해의 범위(히트박스 외곽선/채움)를 보이거나 숨긴다.
/// 숨겨도 '그림만' 꺼질 뿐, 판정·데미지는 그대로다.
///
/// · 에디터: 메뉴 Tools > 디버그 > 소환수 공격 범위 표시 (Alt + Shift + H). 기본은 숨김, 값은 이 PC 에 기억된다.
///           플레이 중이든 아니든 바꿀 수 있고, 플레이 중이면 지금 떠 있는 범위에도 바로 반영된다.
/// · 빌드:   항상 숨김.
/// (플레이어 평타 범위는 MeleeCombatController 의 '평타 범위 표시 숨김' 옵션이 따로 관리한다.)
/// </summary>
public static class AllyRangeDisplay
{
    public const string PrefKey = "Debug.ShowAllyAttackRange";

    private static readonly HashSet<BaseHitBox> _live = new HashSet<BaseHitBox>();
    private static readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
    private static readonly List<SpriteMask> _masks = new List<SpriteMask>();
#if UNITY_EDITOR
    private static bool? _visible;
#endif

    public static bool Visible
    {
        get
        {
#if UNITY_EDITOR
            if (_visible == null) _visible = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            return _visible.Value;
#else
            return false;
#endif
        }
        set
        {
#if UNITY_EDITOR
            _visible = value;
            UnityEditor.EditorPrefs.SetBool(PrefKey, value);
            _live.RemoveWhere(b => b == null);
            foreach (var box in _live) Apply(box, value);
#endif
        }
    }

    /// <summary>아군 히트박스를 등록하고 현재 설정대로 보이게/숨긴다.</summary>
    public static void Register(BaseHitBox box)
    {
        if (box == null) return;
        _live.Add(box);
        Apply(box, Visible);
    }

    public static void Unregister(BaseHitBox box) => _live.Remove(box);

    private static void Apply(BaseHitBox box, bool show)
    {
        if (box == null) return;
        // 히트박스는 공격마다 생기므로 목록을 재사용해 쓰레기(GC)를 만들지 않는다.
        box.GetComponentsInChildren(true, _renderers);
        foreach (var sr in _renderers) sr.enabled = show;
        // SpriteMask 도 꺼야 한다 — 남아 있으면 안 보이는 마스크가 겹치는 적 텔레그래프 외곽선을 잘라먹는다.
        box.GetComponentsInChildren(true, _masks);
        foreach (var mask in _masks) mask.enabled = show;
        _renderers.Clear(); _masks.Clear();
    }
}
