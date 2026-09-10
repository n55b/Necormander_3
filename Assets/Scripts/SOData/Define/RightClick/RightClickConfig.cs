using UnityEngine;

// 기존 에셋의 type 숫자는 보존한다. 현재 플레이는 Guard 한 종류만 사용한다.
public enum RightClickType { None = 0, Parry = 1, Counter = 2, Guard = 3 }

/// <summary>Lv1~4 누적 가드. 마을의 기존 우클릭 선택 NPC에서 에셋을 교체한다.</summary>
[System.Serializable]
public class RightClickConfig
{
    public RightClickType type = RightClickType.Guard;
    [Range(1, 4)] public int level = 1;
    [Header("타이밍 (초)")]
    [Tooltip("한 번 누르면 이 시간 동안 전방 공격을 계속 방어한다. 성공해도 종료되거나 시간이 늘어나지 않는다.")]
    [Min(0f)] public float activeDuration = 0.4f;
    [Min(0f)] public float recoveryDuration = 0.3f;
    [Min(0f)] public float cooldownDuration = 3f;
    [Range(0f, 1f)] public float moveSpeedMultiplier = 0.3f;
    [Header("판정 범위")]
    [Min(0f)] public float radius = 2.5f;
    [Range(0f, 360f)] public float angle = 160f;
    public Color sectorColor = new Color(0.2f, 0.6f, 1f);
    public bool IsValid => type == RightClickType.Guard;
    public bool CanReflect => level >= 2;
    public float EffectiveRadius => Mathf.Max(0f, radius) * (level >= 3 ? 1.33f : 1f);
    public float SuccessRefund => level >= 4 ? Mathf.Max(0f, cooldownDuration) * 0.25f : 0f;
    public string Describe()
    {
        string text = $"가드 Lv{level} — {activeDuration:0.##}초 동안 전방의 가드 가능한 공격을 모두 방어. 재사용 {cooldownDuration:0.##}초.";
        if (level >= 2) text += " 원거리 투사체 반사.";
        if (level >= 3) text += " 반경 33% 증가.";
        if (level >= 4) text += " 사용당 최초 방어 성공 시 쿨타임 25% 환급.";
        return text;
    }
}
