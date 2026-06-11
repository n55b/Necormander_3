using UnityEngine;
using System.Collections;

[CreateAssetMenu(fileName = "BoneMasterAIPattern", menuName = "Necromancer/AI/BoneMasterPattern")]
public class BoneMasterAIPatternSO : BaseAIPatternSO
{
    private enum BossPhaseState 
    { 
        None, 
        Pattern1_Charge, 
        Pattern2_Swing, 
        Pattern3_Thrust, 
        Basic_Push 
    }

    private BossPhaseState _currentBossState = BossPhaseState.None;

    [Header("Phase 2 Transition")]
    public MinionDataSO phase2Data; // 체력 소진 시 적용될 2페이즈 스탯 및 AI 패턴 데이터

    [Header("Attack Settings")]
    public float howlingRadius = 8f;
    public float swingRadius = 6f;
    public float thrustRange = 4f;
    public float shieldPushRadius = 3f;

    [Header("VFX Prefabs (Optional)")]
    public GameObject chargeIndicatorPrefab; // 돌진 범위 표시
    public GameObject howlingEffectPrefab;   // 하울링 반격 이펙트
    public GameObject swingEffectPrefab;     // 휩쓸기 (부채꼴) 이펙트
    public GameObject thrustEffectPrefab;    // 찌르기 이펙트
    public GameObject pushEffectPrefab;      // 방패 밀치기 이펙트

    [Header("Cooldowns")]
    public float pattern1Cooldown = 10f;
    public float pattern2Cooldown = 6f;
    public float pattern3Cooldown = 8f;

    private float _lastPattern1Time = -100f;
    private float _lastPattern2Time = -100f;
    private float _lastPattern3Time = -100f;

    private bool _isActionLocked = false; // 패턴 시전 중 다른 판단을 막기 위함
    private BoneMasterController _bossController;

    private Coroutine _currentPatternCoroutine;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _currentBossState = BossPhaseState.None;
        _isActionLocked = false;
        
        _lastPattern1Time = -100f;
        _lastPattern2Time = -100f;
        _lastPattern3Time = -100f;

        _bossController = entity.GetComponent<BoneMasterController>();
        if (_bossController != null)
        {
            _bossController.OnSpearHitEvent += HandleSpearHitInterrupt;
            _bossController.OnFullChargeHitEvent += HandleFullChargeInterrupt;
        }
    }

    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 유저 요청: 패턴을 보기 위해 무조건 플레이어만 타겟팅하도록 고정
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            entity.Target = GameManager.Instance.PLAYERCONTROLLER.transform;
        }
        else
        {
            entity.Target = null;
        }
    }

    public override void Execute(BaseEntity entity)
    {
        // 보스가 기절 상태면 아무것도 하지 않음
        if (_bossController != null && _bossController.IsStunned)
        {
            if (_isActionLocked && _currentPatternCoroutine != null)
            {
                // 기절이 걸리면 돌진 차징 등이 무조건 취소됨
                entity.StopCoroutine(_currentPatternCoroutine);
                _isActionLocked = false;
                _currentBossState = BossPhaseState.None;
                _bossController.SetStateText("기절! (패턴 캔슬됨)", Color.yellow);
            }
            StopNavAgent(entity);
            return;
        }

        // 패턴 시전 중이면 상태 전이(추적 등) 로직을 스킵함
        if (_isActionLocked)
        {
            return; 
        }

        base.Execute(entity);
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        if (entity.Target == null) return;

        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        float time = Time.time;

        bool canPattern1 = (time - _lastPattern1Time >= pattern1Cooldown);
        bool canPattern2 = (time - _lastPattern2Time >= pattern2Cooldown);
        bool canPattern3 = (time - _lastPattern3Time >= pattern3Cooldown);

        // 유저 요청 로직:
        // 패턴 1은 20 이상일때만
        // 패턴 3은 20이하 10 이상일때만
        // 패턴 2는 10 미만 (기본공격 판정 거리)일때 쿨이 돌아있으면 시전.
        // 모두 아니면 추적(Follow) 또는 10 미만 시 기본 공격(Push)

        if (dist >= 10f)
        {
            if (canPattern1)
            {
                _currentPatternCoroutine = entity.StartCoroutine(Pattern1_ChargeRoutine(entity));
            }
            else
            {
                entity.CurrentState = AIState.Follow;
                if (_bossController != null) _bossController.SetStateText("추격 중...");
            }
        }
        else if (dist >= 5f && dist < 10f)
        {
            if (canPattern3)
            {
                _currentPatternCoroutine = entity.StartCoroutine(Pattern3_ThrustRoutine(entity));
            }
            else
            {
                entity.CurrentState = AIState.Follow;
                if (_bossController != null) _bossController.SetStateText("추격 중...");
            }
        }
        else // dist < 5f
        {
            // 쿨타임이 차있고 사거리(swingRadius) 안에 들어오면 부채꼴 공격
            if (canPattern2 && dist <= swingRadius)
            {
                _currentPatternCoroutine = entity.StartCoroutine(Pattern2_SwingRoutine(entity));
            }
            // 쿨타임이 안 찼거나 패턴2 사거리가 아닐 때, 기본 공격 사거리(shieldPushRadius) 안에 있으면 방밀
            else if (dist <= shieldPushRadius)
            {
                _currentPatternCoroutine = entity.StartCoroutine(Basic_PushRoutine(entity));
            }
            // 둘 다 사거리가 안 닿으면 다가감
            else
            {
                entity.CurrentState = AIState.Follow;
                if (_bossController != null) _bossController.SetStateText("추격 중...");
            }
        }
    }

    #region Pattern 1: Charge
    private bool _p1Interrupted = false;
    private bool _isFakeCharge = false;

    private IEnumerator Pattern1_ChargeRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _lastPattern1Time = Time.time;
        _currentBossState = BossPhaseState.Pattern1_Charge;
        StopNavAgent(entity);

        _p1Interrupted = false;
        _isFakeCharge = Random.value <= 0.25f; // 25% 확률로 페이크

        if (_bossController != null)
        {
            if (_isFakeCharge)
                _bossController.SetStateText("차징 중... (페이크/붉은 빛)", Color.red);
            else
                _bossController.SetStateText("차징 중... (정상/하얀 빛)", Color.white);
        }

        // 4초 대기 (이 동안 인터럽트 이벤트를 기다림)
        float timer = 0f;
        while (timer < 4f)
        {
            if (_p1Interrupted) break;
            timer += Time.deltaTime;
            yield return null;
        }

        if (_p1Interrupted)
        {
            if (_isFakeCharge)
            {
                if (_bossController != null) _bossController.SetStateText("하울링 반격!", Color.magenta);
                Debug.Log("<color=magenta>[BoneMaster]</color> 페이크 차지 중 피격! 하울링 반격 발동!");
                yield return new WaitForSeconds(0.5f); // 딜레이
                ApplyAreaDamage(entity, Vector2.zero, howlingRadius, 1.5f, false, howlingEffectPrefab); // 1.5배 데미지, 이펙트 생성
                yield return new WaitForSeconds(1.0f); // 반격 모션 시간
            }
            else
            {
                if (_bossController != null) _bossController.SetStateText("차지 캔슬됨!", Color.gray);
                Debug.Log("<color=gray>[BoneMaster]</color> 정상 차지 중 피격! 패턴 캔슬.");
                yield return new WaitForSeconds(1f); // 캔슬 후딜레이
            }
        }
        else
        {
            if (_isFakeCharge)
            {
                if (_bossController != null) _bossController.SetStateText("허탕... (그로기)", Color.gray);
                Debug.Log("<color=gray>[BoneMaster]</color> 4초 경과. 페이크 차지 허탕! 그로기 상태 돌입.");
                yield return new WaitForSeconds(3f); // 3초간 기절(그로기)
            }
            else
            {
                if (_bossController != null) _bossController.SetStateText("돌진 휩쓸기!", Color.yellow);
                Debug.Log("<color=yellow>[BoneMaster]</color> 4초 경과. 돌진 후 휩쓸기 발동!");
                
                // 타겟 방향으로 돌진
                if (entity.Target != null)
                {
                    Vector3 dashTarget = entity.Target.position;
                    float dashTime = 0.5f;
                    float elapsed = 0f;
                    Vector3 startPos = entity.transform.position;
                    while(elapsed < dashTime)
                    {
                        entity.transform.position = Vector3.Lerp(startPos, dashTarget, elapsed / dashTime);
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }

                // 돌진 후 주변 휩쓸기 데미지
                ApplyAreaDamage(entity, Vector2.zero, swingRadius, 2.0f, false, swingEffectPrefab); // 2배 데미지
                yield return new WaitForSeconds(1.5f); // 공격 후 모션 대기
            }
        }

        _isActionLocked = false;
        _currentBossState = BossPhaseState.None;
    }

    private void HandleSpearHitInterrupt()
    {
        // 창 투척은 돌진 패턴을 취소시키지 않지만, 페이크 차지의 반격을 유도합니다. (유저 기획: 뼈 창이나 소환수 명중 시)
        // 만약 창으로도 정상 차지를 취소하고 싶다면 아래 주석을 풀면 됩니다.
        if (_currentBossState == BossPhaseState.Pattern1_Charge && !_p1Interrupted)
        {
            _p1Interrupted = true;
        }
    }

    private void HandleFullChargeInterrupt()
    {
        if (_currentBossState == BossPhaseState.Pattern1_Charge && !_p1Interrupted)
        {
            _p1Interrupted = true;
        }
    }
    #endregion

    #region Pattern 2: Swing
    private IEnumerator Pattern2_SwingRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _lastPattern2Time = Time.time;
        _currentBossState = BossPhaseState.Pattern2_Swing;
        StopNavAgent(entity);

        if (_bossController != null) _bossController.SetStateText("부채꼴 베기!", Color.red);
        Debug.Log("<color=red>[BoneMaster]</color> 랜스 휘두르기 시전 완료");
        
        yield return new WaitForSeconds(0.5f); // 선딜레이
        ApplyConeDamage(entity, swingRadius, 90f, 1.2f, swingEffectPrefab); // 90도 부채꼴 1.2배 데미지
        yield return new WaitForSeconds(1.0f); // 스킬 모션 시간

        _isActionLocked = false;
        _currentBossState = BossPhaseState.None;
    }
    #endregion

    #region Pattern 3: Thrust
    private IEnumerator Pattern3_ThrustRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _lastPattern3Time = Time.time;
        _currentBossState = BossPhaseState.Pattern3_Thrust;
        StopNavAgent(entity);

        if (_bossController != null) _bossController.SetStateText("단거리 돌진 & 찌르기!", Color.blue);
        Debug.Log("<color=blue>[BoneMaster]</color> 전방 2회 연속 찌르기 시전 완료");
        
        // 짧은 돌진 (부드럽게 이동)
        if (entity.Target != null)
        {
            Vector3 startPos = entity.transform.position;
            Vector3 dir = (entity.Target.position - startPos).normalized;
            Vector3 dashTarget = startPos + dir * 2f; // 2 유닛 앞으로
            float dashTime = 0.15f; // 0.15초 동안 빠르게 돌진
            float elapsed = 0f;

            while (elapsed < dashTime)
            {
                entity.transform.position = Vector3.Lerp(startPos, dashTarget, elapsed / dashTime);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(0.15f);
        }
        ApplyAreaDamage(entity, Vector2.zero, thrustRange, 0.8f, false, thrustEffectPrefab); // 1타
        yield return new WaitForSeconds(0.4f);
        ApplyAreaDamage(entity, Vector2.zero, thrustRange, 0.8f, false, thrustEffectPrefab); // 2타
        
        yield return new WaitForSeconds(0.9f); // 스킬 모션 시간

        _isActionLocked = false;
        _currentBossState = BossPhaseState.None;
    }
    #endregion

    #region Basic Attack: Push
    private IEnumerator Basic_PushRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _currentBossState = BossPhaseState.Basic_Push;
        StopNavAgent(entity);

        if (_bossController != null) _bossController.SetStateText("방패 밀쳐내기!", Color.white);
        Debug.Log("<color=white>[BoneMaster]</color> 주변 방패 밀쳐내기");
        
        yield return new WaitForSeconds(0.3f);
        ApplyAreaDamage(entity, Vector2.zero, shieldPushRadius, 0.5f, true, pushEffectPrefab); // 0.5배 데미지, 넉백 효과
        yield return new WaitForSeconds(0.7f); // 스킬 모션 시간

        _isActionLocked = false;
        _currentBossState = BossPhaseState.None;
    }
    #endregion

    #region Damage Utilities
    private void ApplyAreaDamage(BaseEntity entity, Vector2 offset, float radius, float damageMultiplier = 1f, bool pushBack = false, GameObject vfxPrefab = null)
    {
        Vector2 center = (Vector2)entity.transform.position + offset;
        
        // 이펙트 생성 또는 디버그 시각화
        if (vfxPrefab != null)
        {
            Instantiate(vfxPrefab, center, Quaternion.identity);
        }
        else
        {
            SpawnDebugCircle(center, radius, Color.red, 0.5f);
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, entity.opponentLayer);
        foreach (var col in hits)
        {
            var hp = col.GetComponentInParent<CharacterHealth>();
            if (hp == null) hp = col.GetComponentInChildren<CharacterHealth>();
            if (hp != null)
            {
                float dmg = entity.Stats.ATK * damageMultiplier;
                hp.GetDamage(new DamageInfo(dmg, DamageType.Physical, entity.gameObject));

                if (pushBack)
                {
                    Vector2 pushDir = (col.transform.position - entity.transform.position).normalized;
                    var status = col.GetComponentInParent<CharacterStatus>();
                    if (status == null) status = col.GetComponentInChildren<CharacterStatus>();
                    if (status != null)
                    {
                        status.ApplyKnockback(pushDir, 5f, 0.15f);
                    }
                    else if (col.attachedRigidbody != null)
                    {
                        // Player/Enemy가 아닌 일반 물리 객체 대비
                        col.attachedRigidbody.AddForce(pushDir * 5f, ForceMode2D.Impulse);
                    }
                }
            }
        }
    }

    private void ApplyConeDamage(BaseEntity entity, float radius, float angleDegree, float damageMultiplier = 1f, GameObject vfxPrefab = null)
    {
        if (entity.Target == null) return;
        Vector2 dirToTarget = (entity.Target.position - entity.transform.position).normalized;
        
        if (vfxPrefab != null)
        {
            float angle = Mathf.Atan2(dirToTarget.y, dirToTarget.x) * Mathf.Rad2Deg;
            Instantiate(vfxPrefab, entity.transform.position, Quaternion.Euler(0, 0, angle));
        }
        else
        {
            SpawnDebugCone(entity.transform.position, dirToTarget, radius, angleDegree, Color.yellow, 0.5f);
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(entity.transform.position, radius, entity.opponentLayer);
        foreach (var col in hits)
        {
            Vector2 dirToCol = (col.transform.position - entity.transform.position).normalized;
            if (Vector2.Angle(dirToTarget, dirToCol) <= angleDegree / 2f)
            {
                var hp = col.GetComponentInParent<CharacterHealth>();
                if (hp == null) hp = col.GetComponentInChildren<CharacterHealth>();
                if (hp != null)
                {
                    float dmg = entity.Stats.ATK * damageMultiplier;
                    hp.GetDamage(new DamageInfo(dmg, DamageType.Physical, entity.gameObject));
                }
            }
        }
    }

    private void SpawnDebugCircle(Vector2 pos, float radius, Color color, float duration)
    {
        GameObject go = new GameObject("DebugCircle");
        go.transform.position = pos;
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.startColor = color;
        lr.endColor = color;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.positionCount = 36;
        lr.useWorldSpace = false;
        for (int i = 0; i < 36; i++)
        {
            float angle = i * Mathf.PI * 2 / 36;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0));
        }
        lr.loop = true;
        lr.sortingLayerName = "Default";
        lr.sortingOrder = 100;
        Destroy(go, duration);
    }

    private void SpawnDebugCone(Vector2 pos, Vector2 dir, float radius, float angleDegree, Color color, float duration)
    {
        GameObject go = new GameObject("DebugCone");
        go.transform.position = pos;
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.startColor = color;
        lr.endColor = color;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        
        int segments = 20;
        lr.positionCount = segments + 2;
        lr.useWorldSpace = false;
        
        lr.SetPosition(0, Vector3.zero);
        float baseAngle = Mathf.Atan2(dir.y, dir.x);
        float halfAngle = (angleDegree / 2f) * Mathf.Deg2Rad;
        
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = baseAngle - halfAngle + (halfAngle * 2f * i / segments);
            lr.SetPosition(i + 1, new Vector3(Mathf.Cos(currentAngle) * radius, Mathf.Sin(currentAngle) * radius, 0));
        }
        lr.loop = true;
        lr.sortingLayerName = "Default";
        lr.sortingOrder = 100;
        Destroy(go, duration);
    }
    #endregion
}
