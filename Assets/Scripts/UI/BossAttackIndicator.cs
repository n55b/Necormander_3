using UnityEngine;

/// <summary>
/// 보스 머리 위 "패턴 예고 게이지"(Enemy 프리팹의 Canvas/CounterPanel + <see cref="CenterMeetFill"/>) 스위치.
///
/// 이 게이지는 공격을 <b>발사하지 않는다</b>. 패턴 코루틴이 이미 돌리고 있는 예고 시간을 그대로
/// 넘겨받아 따라 그리기만 한다. 그래서 게이지가 가득 차는 순간과 실제 판정이 나가는 순간이
/// 같은 변수 하나에서 나오고, 배속 보정(csMul)이나 파훼로 예고가 짧아져도 절대 어긋나지 않는다.
/// (<see cref="CenterMeetFill.OnFillComplete"/> 는 일부러 안 쓴다 — 그걸로 공격을 쏘면
/// 타이밍 소스가 게이지와 코루틴 둘로 갈라져서, 어긋나는 날이 반드시 온다.)
///
/// 부르는 규칙은 텔레그래프와 1:1 이다. 예고 하나에 이 두 줄이 짝으로 붙는다:
/// <code>
///     GameObject t = BoneMasterTelegraphUtil.SpawnLane(...);
///     BossAttackIndicator.Begin(entity, windup, dir);   // 스폰 바로 뒤
///     ... 예고 대기 루프 ...
///     BossAttackIndicator.Stop(entity);                 // Destroy 옆
///     if (t != null) Object.Destroy(t);
/// </code>
/// 경직/페이즈 전환으로 코루틴이 통째로 죽는 경로는 <see cref="BoneMasterController.CleanupDanglingTelegraphs"/>
/// 가 받아준다(텔레그래프 잔여물을 치우는 그 자리에서 게이지도 같이 끈다).
/// </summary>
public static class BossAttackIndicator
{
    /// <summary>
    /// CounterImage 가 CounterPanel 기준 로컬 +Y(위쪽)에 얹혀 있어서 회전 0도 = "위" 다.
    /// 월드 방향각(0도 = 오른쪽)을 그대로 넣으면 90도 어긋난다.
    /// 프리팹에서 CounterImage 를 다른 방향에 얹으면 이 값도 같이 바꿔야 한다.
    /// </summary>
    private const float UpIsZeroDegrees = -90f;

    /// <summary>예고 게이지를 켠다.</summary>
    /// <param name="duration">공격 판정이 나갈 때까지 남은 시간. 패턴이 <b>실제로 기다리는 값과
    /// 같은 식</b>을 넘겨야 한다(배속 보정까지 곱한 뒤의 값).</param>
    /// <param name="dir">공격이 나가는 방향. 방향이 없는 패턴(회전 베기, 착지 장판 등)은
    /// 비워두면 플레이어 쪽을 본다 — 아래 <see cref="Angle"/> 참고.</param>
    /// <param name="color">
    /// 게이지 색 = 카운터 신호. 노랑이면 '지금 때리면 패턴이 취소된다', 빨강이면 '때리면 즉시 시전한다',
    /// 무채색이면 '카운터 불가'. 비워두면 프리팹에 박힌 색(빨강)이 그대로 남는다.
    ///
    /// <see cref="Stop"/> 은 색을 되돌리지 않는다 — 켤 때마다 반드시 다시 정해야 이전 패턴의
    /// 색이 새 예고에 묻어나지 않는다.
    /// </param>
    public static void Begin(BaseEntity entity, float duration, Vector2 dir = default, Color? color = null)
    {
        CenterMeetFill fill = Find(entity);
        if (fill == null) return;

        if (color.HasValue) fill.SetColor(color.Value);

        fill.transform.localRotation = Quaternion.Euler(0f, 0f, Angle(entity, dir));
        fill.gameObject.SetActive(true);
        fill.StartFill(duration);
    }

    /// <summary>
    /// 게이지를 돌릴 각도. 방향이 있으면 그쪽, 없으면 <b>플레이어 쪽</b>이다.
    ///
    /// 무방향 패턴(회전 베기 / 몸통 박치기 / 마무리 베기 — 페이즈2 패턴의 과반)을 그냥 위쪽에
    /// 고정하면, 사방으로 터지는 장판인데 게이지는 "위로 뭔가 나간다"고 가리키는 꼴이 된다.
    /// 게이지가 거짓말을 하느니 이미 플레이어가 서 있는 방향을 보게 둔다 — 정보가 늘지는 않지만
    /// 최소한 틀린 방향을 짚지 않고, 항상 플레이어 시선 안에 들어온다.
    /// (표적이 없으면 그때만 위쪽으로 떨어진다.)
    /// </summary>
    private static float Angle(BaseEntity entity, Vector2 dir)
    {
        if (dir.sqrMagnitude <= 0.0001f)
        {
            if (entity.Target == null) return 0f;
            dir = (Vector2)entity.Target.position - (Vector2)entity.transform.position;
            if (dir.sqrMagnitude <= 0.0001f) return 0f;
        }
        return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + UpIsZeroDegrees;
    }

    /// <summary>
    /// 채우는 도중에 방향만 다시 조준한다. 예고 내내 플레이어를 실시간으로 주시하는 패턴
    /// (해태 패턴1의 3초 조준 돌진 등)에서 매 프레임 부른다. 진행도는 건드리지 않는다.
    /// 한 번 방향을 못 박는 패턴은 <see cref="Begin"/> 한 번이면 되고 이건 부를 필요가 없다.
    /// </summary>
    public static void Aim(BaseEntity entity, Vector2 dir)
    {
        CenterMeetFill fill = Find(entity);
        if (fill == null || !fill.IsFilling) return;

        fill.transform.localRotation = Quaternion.Euler(0f, 0f, Angle(entity, dir));
    }

    /// <summary>예고 게이지를 끈다. 판정이 나간 직후, 그리고 패턴이 취소될 때 부른다.</summary>
    public static void Stop(BaseEntity entity)
    {
        CenterMeetFill fill = Find(entity);
        if (fill == null) return;

        fill.StopFill();
        fill.gameObject.SetActive(false);
    }

    // 예고 하나에 두 번만 부르므로 캐시하지 않는다. 적은 풀에서 재사용되니까 static 캐시를 들면
    // 죽은 참조를 붙들고 있게 되는데, 그 대가가 계층 탐색 몇 번보다 비싸다.
    // CounterPanel 은 평소 꺼져 있으므로 includeInactive: true 가 필수다.
    private static CenterMeetFill Find(BaseEntity entity)
        => entity != null ? entity.GetComponentInChildren<CenterMeetFill>(true) : null;
}
