using System.Collections;
using UnityEngine;

/// <summary>
/// 보스의 단일 행동(기본 공격 1종 또는 특수 패턴 1개)을 나타내는 조립 단위.
/// 스케줄러가 하나를 골라 <c>entity.StartCoroutine(action.Run(ctx))</c>로 실행한다.
/// 새 보스/패턴 추가 = 거대한 switch·코루틴 더미가 아니라 새 <see cref="IBossAction"/> 구현 1개.
/// </summary>
public interface IBossAction
{
    /// <summary>보스 머리 위 라벨에 표시할 한글 이름. null이면 라벨 미표시.</summary>
    string Label { get; }

    /// <summary>이 행동 1회를 수행하는 코루틴. 끝나면 스케줄러가 다음 행동을 진행한다.</summary>
    IEnumerator Run(BossContext ctx);
}

/// <summary>
/// 액션들이 공유하는 런타임 문맥. 개별 행동이 필요로 하는 엔티티/조준/기둥/난이도 배율을 한 곳에 묶어
/// 인자 폭발을 막는다. (구체 수치 설정은 각 액션이 자신의 config(=보스 SO)에서 직접 읽는다.)
/// </summary>
public class BossContext
{
    public readonly BaseEntity Entity;
    public readonly PillarField Pillars;

    /// <summary>v1.4 난이도 스케일 등 시간/수치에 곱할 배율. 기본 1.</summary>
    public float DifficultyScale = 1f;

    // 마지막으로 유효했던 조준 방향. 대상과 완전히 겹쳐 0벡터가 되는 순간에도 엉뚱한 방향으로
    // 공격이 나가지 않도록 이 값을 유지한다. (기존 EliteCharger.GetAimDir 과 동일 취지)
    private Vector2 _lastAimDir = Vector2.down;

    public BossContext(BaseEntity entity, PillarField pillars)
    {
        Entity = entity;
        Pillars = pillars;
    }

    /// <summary>대상 방향(정규화). 구할 수 없으면 마지막 유효 방향을 유지한다.</summary>
    public Vector2 AimDir()
    {
        if (Entity != null && Entity.Target != null)
        {
            Vector2 raw = (Vector2)Entity.Target.position - (Vector2)Entity.transform.position;
            if (raw.sqrMagnitude > 0.0001f) _lastAimDir = raw.normalized;
        }
        return _lastAimDir;
    }
}
