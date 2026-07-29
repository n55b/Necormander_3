using UnityEngine;

/// <summary>
/// 투사체를 발사하는 원거리 유닛용 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "RangedAIPattern", menuName = "Necromancer/AI/RangedPattern")]
public class RangedAIPatternSO : BaseAIPatternSO
{
    [Header("원거리 공격 설정")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float launchOffset = 0.5f;

    [Header("원거리 조준선 설정")]
    [Tooltip("화살을 쏘기 전 궤적을 보여줄 조준선 프리팹 (콜라이더가 없는 얇은 BaseHitBox를 권장합니다)")]
    [SerializeField] private GameObject aimLinePrefab;
    [Tooltip("기존 조준선(락온 연출). 예고 레인이 같은 역할을 하므로 기본 꺼짐. 발사 방향은 레인과 동일하게 유지된다.")]
    [SerializeField] private bool showAimLine = false;

    // 윈드업 동안 갱신되는 록온 방향. 조준선을 꺼도 발사 방향이 유지되도록 여기에 보관한다.
    private Vector2 _aimDir = Vector2.right;

    [Header("발사 예고 레인")]
    [Tooltip("발사 경로를 바닥에 깔고, 두께가 차오르며 발사 타이밍을 알린다. 조준선은 발사 방향 결정에 쓰이므로 그대로 유지된다.")]
    [SerializeField] private bool showShotTelegraph = true;
    [Tooltip("배경 레인 색. '어디로 올지'를 알린다.")]
    [SerializeField] private Color shotTelegraphColor = new Color(1f, 0.85f, 0.2f, 0.18f);
    [Tooltip("두께로 차오르는 게이지 색. '언제 올지'를 알린다. 배경보다 진해야 읽힌다.")]
    [SerializeField] private Color shotTelegraphFillColor = new Color(1f, 0.6f, 0.1f, 0.65f);
    [Tooltip("레인 두께(월드 단위). 투사체 히트 판정 폭에 맞추면 예고가 정직해진다.")]
    [SerializeField] private float shotTelegraphWidth = 0.35f;

    // 엔티티별 클론(BaseEntity._runtimeBrain)이라 인스턴스 상태를 둬도 안전하다.
    private GameObject _shotTelegraph;
    private float _windupDuration;
    private float _windupStartTime;


    private void OnEnable()
    {
        // 원거리 유닛은 기본적으로 근접용 장판(Telegraph)을 자동 생성하지 않습니다.
        spawnTelegraph = false;
    }

protected override void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 예고 레인 진행도 계산용. OnWindupUpdate 에는 진행도가 넘어오지 않는다.
        _windupDuration = windupTime;
        _windupStartTime = Time.time;
        ClearShotTelegraph(); // 이전 시도가 남긴 게 있으면 먼저 정리

        if (showAimLine && aimLinePrefab != null && entity.Target != null)
        {
            // [수정] 월드가 아닌 시전자(entity) 자식으로 매달아 몬스터가 이동/넉백되어도 오프셋 지점에 록온 가이드 라인이 따라붙도록 처리
            GameObject aimLine = Instantiate(aimLinePrefab, entity.transform.position, Quaternion.identity, entity.transform);
            aimLine.transform.localPosition = Vector3.up * launchOffset;

            var hitbox = aimLine.GetComponent<BaseHitBox>();
            if (hitbox != null)
            {
                // 데미지 0짜리 가짜 페이로드 (시각적 표시 용도)
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, entity.gameObject, 0f);

                // windupTime 동안 차오르도록 설정
                // 발사 즉시 사라져야 하므로 duration을 매우 짧게 주거나 0으로 설정
                hitbox.Init(dummyInfo, 0, 0.1f, windupTime, entity.team == Team.Ally);

                // 취소 시 같이 지워질 수 있도록 엔티티에 등록
                entity.SetActiveHitbox(hitbox);
            }
        }
    }

protected override void OnWindupUpdate(BaseEntity entity)
    {
        base.OnWindupUpdate(entity);

        if (entity.Target == null)
        {
            ClearShotTelegraph();
            return;
        }

        // 조준선을 꺼도 동작해야 하므로 발사 위치를 엔티티 기준으로 직접 계산한다.
        // ExecuteBasicAttack 의 spawnPos 와 같은 식이라 예고와 실제 발사가 어긋나지 않는다.
        Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
        _aimDir = ((Vector2)entity.Target.position - spawnPos).normalized;

        // 조준선이 켜져 있을 때만 기존 회전/길이 갱신 유지
        if (entity.ActiveHitbox != null)
        {
            float angle = Mathf.Atan2(_aimDir.y, _aimDir.x) * Mathf.Rad2Deg;
            entity.ActiveHitbox.transform.localRotation = Quaternion.Euler(0, 0, angle);

            float currentDistToTarget = Vector2.Distance(spawnPos, entity.Target.position);
            entity.ActiveHitbox.transform.localScale = new Vector3(currentDistToTarget, 1f, 1f);
        }

        if (showShotTelegraph)
        {
            if (_shotTelegraph == null)
                _shotTelegraph = BossTelegraph.SpawnRect(shotTelegraphColor);

            // 플레이어는 투사체를 막지 않으므로 레인도 플레이어에서 끊지 않는다.
            // 투사체는 Layers.WallMask 에 닿으면 소멸하므로(Projectile.OnTriggerEnter2D) 거기까지만 그린다.
            float laneLength = projectileSpeed * lifeTime;
            RaycastHit2D laneBlock = Physics2D.Raycast(spawnPos, _aimDir, laneLength, Layers.WallMask);
            if (laneBlock.collider != null)
                laneLength = laneBlock.distance;

            float progress = _windupDuration > 0f
                ? Mathf.Clamp01((Time.time - _windupStartTime) / _windupDuration)
                : 1f;

            BossTelegraph.UpdateRectWithFill(_shotTelegraph, spawnPos, _aimDir, laneLength, shotTelegraphWidth, progress, shotTelegraphFillColor);
        }
        else
        {
            ClearShotTelegraph();
        }
    }

protected override void ExecuteBasicAttack(BaseEntity entity)
    {
        ClearShotTelegraph();

        if (projectilePrefab == null || entity.Target == null) return;

        Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);

        // 타겟의 '현재' 위치가 아니라 윈드업 동안 록온된 방향으로 쏜다(예고 레인과 동일한 방향).
        // 조준선 오브젝트에 의존하지 않으므로 조준선을 꺼도 락온 동작이 그대로 유지된다.
        Vector2 shootDir = _aimDir.sqrMagnitude > 0.0001f
            ? _aimDir
            : ((Vector2)entity.Target.position - spawnPos).normalized;

        float maxTravelDistance = projectileSpeed * lifeTime;
        Vector3 shootTargetPos = spawnPos + shootDir * maxTravelDistance;

        GameObject projectileObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        Projectile projectile = projectileObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Init(shootTargetPos, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
        }
    }

// 스턴/사망 등으로 윈드업이 끊겨도 레인이 화면에 남지 않도록 정리.
/// <summary>발사 예고 레인 제거. 정상 발사/취소/파괴 모두에서 호출된다.</summary>
    private void ClearShotTelegraph()
    {
        if (_shotTelegraph != null)
        {
            Destroy(_shotTelegraph);
            _shotTelegraph = null;
        }
    }

    // 피격/스턴으로 공격이 끊길 때. StopCoroutine 은 루틴 뒷정리를 실행하지 않는다.
    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
        ClearShotTelegraph();
    }

    // 엔티티 사망/씬 언로드로 브레인 클론이 파괴될 때.
    private void OnDisable()
    {
        ClearShotTelegraph();
    }

}
