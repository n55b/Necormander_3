using UnityEngine;

// 기존 에셋의 type 숫자는 보존한다. 현재 플레이는 Guard 한 종류만 사용한다.
public enum RightClickType { None = 0, Parry = 1, Counter = 2, Guard = 3 }

/// <summary>Lv1~4 누적 가드. 마을의 기존 우클릭 선택 NPC에서 에셋을 교체한다.</summary>
[System.Serializable]
public class RightClickConfig
{
    public RightClickType type = RightClickType.Guard;
    [Range(1, 4)] public int level = 1;
    [Header("가드 중 이동")]
    [Range(0f, 1f)] public float moveSpeedMultiplier = 0.3f;
    [Header("판정 범위")]
    [Min(0f)] public float radius = 2.5f;
    [Range(0f, 360f)] public float angle = 160f;
    public Color sectorColor = new Color(0.2f, 0.6f, 1f);
    public bool IsValid => type == RightClickType.Guard;
    public bool CanReflect => level >= 2;
    public float EffectiveRadius => Mathf.Max(0f, radius) * (level >= 3 ? 1.33f : 1f);
    public string Describe()
    {
        string text = $"가드 Lv{level} — 우클릭을 누르는 동안 전방 공격을 방어. 방어력 적용 후 피해량만큼 가드 게이지 소모, 퍼펙트 가드는 절반 소모.";
        if (level >= 2) text += " 원거리 투사체 반사.";
        if (level >= 3) text += " 반경 33% 증가.";
        if (level >= 4) text += " 4레벨 추가 효과는 준비 중.";
        return text;
    }
}
