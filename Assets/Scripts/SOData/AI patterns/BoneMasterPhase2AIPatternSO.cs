using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BoneMasterPhase2AIPattern", menuName = "Necromancer/AI/BoneMasterPhase2AIPattern")]
public class BoneMasterPhase2AIPatternSO : BaseAIPatternSO
{
    private enum BossPhase2State 
    { 
        None, 
        Pattern1_Spear, 
        Pattern2_Charge, 
        Pattern3_SwordWave, 
        Basic_Attack 
    }

    private BossPhase2State _currentState = BossPhase2State.None;

    [Header("Phase 2 Defense")]
    public float rangedBarrierRadius = 15f; // [추가] 원거리 투사체 방어 반경
    public float pattern1Cooldown = 12f;
    public float pattern2Cooldown = 10f;
    public float pattern3Cooldown = 9f;

    [Header("Pattern 1 Settings (Spear)")]
    public float spearStealStunTime = 2f;

    [Header("Pattern 2 Settings (Charge)")]
    public float chargeTime = 3f;
    public float chargeStunTime = 3f;
    public float chargeHitRadius = 3f;
    [Range(0f, 1f)] public float chargeFakeChance = 0.25f;

    [Header("Pattern 3 Settings (Sword Wave)")]
    public int swordWaveCount = 3;
    public float swordWaveDelay = 0.3f;
    public float coneWaveDelay = 0.5f;

    [Header("Basic Attack Settings")]
    public float basicThrustRadius = 2.5f;
    public float basicSwingRadius = 3.5f;

    [Header("VFX Prefabs (Optional)")]
    public GameObject spearLaserPrefab;      // 창 투척 직선 광선 이펙트
    public GameObject chargeCounterPrefab;   // 페이크 차지 전범위 이펙트
    public GameObject chargeSweepPrefab;     // 돌진 휩쓸기 이펙트
    public GameObject swordWavePrefab;       // 검기 발사 (직선) 이펙트
    public GameObject basicThrustPrefab;     // 기본 찌르기 이펙트
    public GameObject basicSwingPrefab;      // 기본 휘두르기 이펙트

    private float _lastPattern1Time = -100f;
    private float _lastPattern2Time = -100f;
    private float _lastPattern3Time = -100f;

    private bool _isActionLocked = false;
    private BoneMasterController _bossController;

    private Coroutine _currentPatternCoroutine;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _currentState = BossPhase2State.None;
        _isActionLocked = false;
        
        _lastPattern1Time = -100f;
        _lastPattern2Time = -100f;
        _lastPattern3Time = -100f;

        _bossController = entity.GetComponent<BoneMasterController>();
        if (_bossController != null)
        {
            // 페이즈 2 전용 인터럽트는 OnFullChargeHitEvent 등을 재활용
            _bossController.OnFullChargeHitEvent += HandleInterrupt;
            _bossController.OnSpearHitEvent += HandleInterrupt;
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
        if (_bossController != null && _bossController.IsStunned)
        {
            if (_isActionLocked && _currentPatternCoroutine != null)
            {
                entity.StopCoroutine(_currentPatternCoroutine);
                _isActionLocked = false;
                _currentState = BossPhase2State.None;
                _bossController.SetStateText("기절! (패턴 캔슬됨)", Color.yellow);
            }
            StopNavAgent(entity);
            return;
        }

        if (_isActionLocked) return; 

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

        // [26/07/17] 패턴 1(창 훔치기) 분기 삭제. 투척 시스템을 철거하면서 플레이어가 뼈창을
        // 주워 던질 수 없게 됐고, 맵에 창이 생기지 않으니 이 패턴은 영원히 조건을 못 만족한다.
        // 보스 기믹을 다시 짤 때 이 자리에 새 패턴을 넣으면 된다 (canPattern1/pattern1Cooldown 은 남겨둠).
        if (canPattern2 && dist >= 8f) // 멀리 있을 때
        {
            _currentPatternCoroutine = entity.StartCoroutine(Pattern2_ChargeRoutine(entity));
        }
        else if (canPattern3 && dist >= 4f && dist < 12f) // 중거리
        {
            _currentPatternCoroutine = entity.StartCoroutine(Pattern3_SwordWaveRoutine(entity));
        }
        else if (dist < 4f) // 근거리 기본 공격
        {
            // 실제 공격 범위(최대 3.5f) 안에 있을 때만 공격 시전
            if (dist <= Mathf.Max(basicThrustRadius, basicSwingRadius))
            {
                _currentPatternCoroutine = entity.StartCoroutine(Basic_AttackRoutine(entity));
            }
            else
            {
                entity.CurrentState = AIState.Follow;
                if (_bossController != null) _bossController.SetStateText("추격 중...");
            }
        }
        else
        {
            // 아무 조건도 안 맞으면 상시 추격
            entity.CurrentState = AIState.Follow;
            if (_bossController != null) _bossController.SetStateText("추격 중...");
        }
    }



    #region Pattern 2: Charge / Counter
    private bool _p2Interrupted = false;

    private IEnumerator Pattern2_ChargeRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _lastPattern2Time = Time.time;
        _currentState = BossPhase2State.Pattern2_Charge;
        StopNavAgent(entity);

        _p2Interrupted = false;
        bool isFake = Random.value <= chargeFakeChance; // 페이크 확률

        if (_bossController != null)
        {
            if (isFake)
                _bossController.SetStateText("집중... (붉은 빛)", Color.red);
            else
                _bossController.SetStateText("집중... (하얀 빛)", Color.white);
        }

        float timer = 0f;
        while (timer < chargeTime)
        {
            if (_p2Interrupted) break;
            timer += Time.deltaTime;
            yield return null;
        }

        if (_p2Interrupted)
        {
            if (isFake)
            {
                if (_bossController != null) _bossController.SetStateText("전범위 카운터 발도!", Color.red);
                yield return new WaitForSeconds(0.5f);
                ExecuteMeleeAttack(entity, 20f, 2.0f, chargeCounterPrefab); // 전범위 2배 데미지
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                if (_bossController != null) _bossController.SetStateText("집중 캔슬됨! (기절)", Color.yellow);
                yield return new WaitForSeconds(chargeStunTime);
            }
        }
        else
        {
            if (isFake)
            {
                if (_bossController != null) _bossController.SetStateText("허탕... (그로기)", Color.gray);
                yield return new WaitForSeconds(3f);
            }
            else
            {
                if (_bossController != null) _bossController.SetStateText("돌진 휩쓸기!", Color.yellow);
                
                if (entity.Target != null)
                {
                    Vector3 dashTarget = entity.Target.position;
                    float dashTime = 0.4f;
                    float elapsed = 0f;
                    Vector3 startPos = entity.transform.position;
                    while (elapsed < dashTime)
                    {
                        entity.transform.position = Vector3.Lerp(startPos, dashTarget, elapsed / dashTime);
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }

                ExecuteMeleeAttack(entity, chargeHitRadius, 1.5f, chargeSweepPrefab);
                yield return new WaitForSeconds(2f);
            }
        }

        _isActionLocked = false;
        _currentState = BossPhase2State.None;
    }

    private void HandleInterrupt()
    {
        if (_currentState == BossPhase2State.Pattern2_Charge && !_p2Interrupted)
        {
            _p2Interrupted = true;
        }
    }
    #endregion

    #region Pattern 3: Sword Wave
    private IEnumerator Pattern3_SwordWaveRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _lastPattern3Time = Time.time;
        _currentState = BossPhase2State.Pattern3_SwordWave;
        StopNavAgent(entity);

        if (_bossController != null) _bossController.SetStateText("연속 검기 발사!", Color.cyan);
        
        // 직선 발사 (투사체 대신 즉발 선형 판정)
        for (int i = 0; i < swordWaveCount; i++)
        {
            yield return new WaitForSeconds(swordWaveDelay);
            if (entity.Target != null) ApplyLineDamage(entity, entity.Target.position, 12f, 0.8f, swordWavePrefab);
        }
        
        yield return new WaitForSeconds(coneWaveDelay);
        if (_bossController != null) _bossController.SetStateText("3갈래 확산 검기!", Color.cyan);
        
        if (entity.Target != null)
        {
            Vector3 centerDir = (entity.Target.position - entity.transform.position).normalized;
            Vector3 leftDir = Quaternion.Euler(0, 0, 30) * centerDir;
            Vector3 rightDir = Quaternion.Euler(0, 0, -30) * centerDir;

            ApplyLineDamage(entity, entity.transform.position + centerDir * 12f, 12f, 1.0f, swordWavePrefab);
            ApplyLineDamage(entity, entity.transform.position + leftDir * 12f, 12f, 1.0f, swordWavePrefab);
            ApplyLineDamage(entity, entity.transform.position + rightDir * 12f, 12f, 1.0f, swordWavePrefab);
        }
        yield return new WaitForSeconds(1f);

        _isActionLocked = false;
        _currentState = BossPhase2State.None;
    }
    #endregion

    #region Basic Attack
    private IEnumerator Basic_AttackRoutine(BaseEntity entity)
    {
        _isActionLocked = true;
        _currentState = BossPhase2State.Basic_Attack;
        StopNavAgent(entity);

        bool isThrust = Random.value > 0.5f;

        if (isThrust)
        {
            if (_bossController != null) _bossController.SetStateText("양손검 찌르기!", Color.white);
            yield return new WaitForSeconds(0.3f); // 선딜
            ExecuteMeleeAttack(entity, basicThrustRadius, 1f, basicThrustPrefab);
            yield return new WaitForSeconds(0.7f); // 후딜
        }
        else
        {
            if (_bossController != null) _bossController.SetStateText("양손검 휘두르기!", Color.white);
            yield return new WaitForSeconds(0.4f); // 선딜
            ExecuteMeleeAttack(entity, basicSwingRadius, 1f, basicSwingPrefab);
            yield return new WaitForSeconds(0.8f); // 후딜
        }

        _isActionLocked = false;
        _currentState = BossPhase2State.None;
    }

    private void ExecuteMeleeAttack(BaseEntity entity, float radius, float damageMultiplier = 1f, GameObject vfxPrefab = null)
    {
        if (vfxPrefab != null)
        {
            Instantiate(vfxPrefab, entity.transform.position, Quaternion.identity);
        }
        else
        {
            SpawnDebugCircle(entity.transform.position, radius, Color.red, 0.5f);
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(entity.transform.position, radius, entity.opponentLayer);
        foreach (var hit in hits)
        {
            var hp = hit.GetComponentInParent<CharacterHealth>();
            if (hp == null) hp = hit.GetComponentInChildren<CharacterHealth>();
            if (hp != null)
            {
                DamageInfo info = new DamageInfo(entity.Stats.BaseAtk * damageMultiplier, DamageType.Physical, entity.gameObject);
                hp.GetDamage(info);
            }
        }
    }

    private void ApplyLineDamage(BaseEntity entity, Vector3 targetPos, float length, float damageMultiplier = 1f, GameObject vfxPrefab = null)
    {
        Vector2 dir = (targetPos - entity.transform.position).normalized;
        
        if (vfxPrefab != null)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Instantiate(vfxPrefab, entity.transform.position, Quaternion.Euler(0, 0, angle));
        }
        else
        {
            SpawnDebugLine(entity.transform.position, entity.transform.position + (Vector3)dir * length, 2f, Color.cyan, 0.5f);
        }

        RaycastHit2D[] hits = Physics2D.BoxCastAll(entity.transform.position, new Vector2(2f, length), Vector2.SignedAngle(Vector2.up, dir), dir, length, entity.opponentLayer);
        foreach(var hit in hits)
        {
            var hp = hit.collider.GetComponentInParent<CharacterHealth>();
            if (hp == null) hp = hit.collider.GetComponentInChildren<CharacterHealth>();
            if (hp != null)
            {
                DamageInfo info = new DamageInfo(entity.Stats.BaseAtk * damageMultiplier, DamageType.Physical, entity.gameObject);
                hp.GetDamage(info);
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

    private void SpawnDebugLine(Vector3 start, Vector3 end, float width, Color color, float duration)
    {
        GameObject go = new GameObject("DebugLine");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        
        // 투명도 조절
        color.a = 0.5f;
        lr.startColor = color;
        lr.endColor = color;

        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.sortingLayerName = "Default";
        lr.sortingOrder = 100;
        Destroy(go, duration);
    }
    #endregion
}
