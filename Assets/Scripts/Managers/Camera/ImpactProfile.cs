using UnityEngine;

public enum ImpactKind
{
    [InspectorName("일반 타격")] Normal,
    [InspectorName("소환수")] Summon,
    [InspectorName("치명타")] Critical,
    [InspectorName("콤보 마무리")] Finisher,
    [InspectorName("플레이어 피격")] PlayerHurt,
    [InspectorName("보스 처치")] BossDeath,
    [InspectorName("보스 착지")] BossLanding,
    [InspectorName("방 마지막 일격")] RoomFinish
}

[System.Serializable]
public class ImpactProfile
{
    [InspectorName("사건")] public ImpactKind kind;
    [InspectorName("우선순위")] public int priority;
    [InspectorName("반동 거리 (픽셀)"), Range(0, 12)] public float pixels;
    [InspectorName("반동 시간 (ms)"), Range(0, 400)] public int shakeMs;
    [InspectorName("히트스톱 (ms)"), Range(0, 150)] public int stopMs;
    [InspectorName("감쇠 지수"), Range(1, 4)] public float decay = 2;
    [InspectorName("방향 보정 (도)"), Tooltip("공격 방향에서 반시계 방향으로 회전할 각도")] public float angle;

    public ImpactProfile(ImpactKind kind, int priority, float pixels, int shakeMs, int stopMs)
    {
        this.kind = kind;
        this.priority = priority;
        this.pixels = pixels;
        this.shakeMs = shakeMs;
        this.stopMs = stopMs;
    }
}

