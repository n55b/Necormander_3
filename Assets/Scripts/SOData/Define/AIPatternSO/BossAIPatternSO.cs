using UnityEngine;

/// <summary>
/// 모든 보스 AI 패턴의 기반이 되는 클래스입니다.
/// 페이즈 전환 등 보스 공통 로직을 관리합니다.
/// </summary>
public abstract class BossAIPatternSO : AIPatternSO
{
    [Header("Boss Phase Settings")]
    public float phase2Threshold = 0.5f; // 페이즈 2 전환 체력 비율
    protected int currentPhase = 1;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        currentPhase = 1;
    }

    protected void UpdatePhase(BaseEntity entity)
    {
        if (entity.Stats == null || entity.Stats.Health == null) return;

        float hpRatio = entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP;
        if (currentPhase == 1 && hpRatio <= phase2Threshold)
        {
            currentPhase = 2;
            OnPhaseChanged(entity, 2);
        }
    }

    protected virtual void OnPhaseChanged(BaseEntity entity, int newPhase)
    {
        Debug.Log($"<color=red>[Boss]</color> Phase Changed to <b>{newPhase}</b>!");
    }
}
