using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// 본 마스터 보스 전용 컨트롤러입니다. 
/// 뼈 갑옷 스택, 페이즈 전환, 뼈 창 피격 상호작용 및 UI 상태 표시를 담당합니다.
/// </summary>
public class BoneMasterController : EnemyController
{
    [HideInInspector] public float maxBoneArmor = 10f;
    private float _currentBoneArmor;

    public bool IsStunned => _isStunned;
    private bool _isStunned = false;

    public bool IsVulnerable => _isVulnerable;
    private bool _isVulnerable = false;

    [Header("UI Reference")]
    [SerializeField] private TextMeshPro stateText; // 인스펙터나 런타임에 동적으로 부착
    
    public int CurrentPhase { get; private set; } = 1;

    protected override void Start()
    {
        base.Start();

        if (Stats != null && Stats.DEF > 0)
        {
            maxBoneArmor = Stats.DEF; // MinionData에서 지정된 방어력으로 동기화
        }
        
        _currentBoneArmor = maxBoneArmor;

        // UI 텍스트가 안 달려있으면 임시로 하나 생성
        if (stateText == null)
        {
            GameObject textObj = new GameObject("StateText");
            textObj.transform.SetParent(transform);
            textObj.transform.localPosition = new Vector3(0, 3f, 0); // 머리 위
            stateText = textObj.AddComponent<TextMeshPro>();
            stateText.alignment = TextAlignmentOptions.Center;
            stateText.fontSize = 4;
            stateText.color = Color.white;
            stateText.outlineWidth = 0.2f;
        }

        SetStateText("추격 중...");

        if (Stats != null && Stats.Health != null)
        {
            Stats.Health.OnBeforeDeath += HandlePhaseTransition;
        }
    }

    public void SetStateText(string text, Color? color = null)
    {
        if (stateText != null)
        {
            stateText.text = text;
            if (color.HasValue) stateText.color = color.Value;
        }
    }

    /// <summary>
    /// 플레이어가 뼈 창을 주워 던져서 보스에게 적중했을 때 호출됩니다.
    /// </summary>
    public void OnSpearHit(Vector3 attackerPos)
    {
        if (_isStunned || _isVulnerable) return;
        if (CheckProjectileBlocked(attackerPos)) return;

        // [수정] 뼈 갑옷 스택이 부서질 때마다 최대치의 절반(50%)씩 깎임
        _currentBoneArmor -= (maxBoneArmor / 2f);
        if (Stats != null) Stats.SetCurrentDef(_currentBoneArmor);

        Debug.Log($"<color=cyan>[BoneMaster]</color> 뼈 창 적중! 현재 방어력: {_currentBoneArmor}");

        // 방어력이 0 이하가 되면 4초 기절 -> 12초 회복 대기 루프 실행
        if (_currentBoneArmor <= 0)
        {
            StartCoroutine(StunAndVulnerableRoutine());
        }
        else
        {
            // 방어력이 깎였을 뿐, 아직 기절하지 않음
            // (AIPattern에서 페이크 차징 반격을 처리하기 위해 뼈 창 피격 이벤트를 체크할 수 있게 변수를 열어둘 수도 있음)
            // 이를 AIPattern에 알리려면 public Action OnSpearHitEvent; 등을 쓸 수 있습니다.
            OnSpearHitEvent?.Invoke();
        }
    }

    public System.Action OnSpearHitEvent; // 차징 캔슬 등을 위해 이벤트 발송

    /// <summary>
    /// 플레이어가 풀차지 소환수(isDirect)를 던져서 맞췄을 때 AI에게 알려주기 위한 함수
    /// CharacterStatus의 OnHit 등으로 우회 호출될 수 있지만, 편의상 여기에 직접 호출 함수 추가
    /// </summary>
    public void OnFullChargeMinionHit(Vector3 attackerPos)
    {
        if (_isStunned) return;
        if (CheckProjectileBlocked(attackerPos)) return;
        OnFullChargeHitEvent?.Invoke();
    }

    public System.Action OnFullChargeHitEvent;

    // [추가] 2페이즈 원거리 쉴드 판정
    private bool IsOutsideRangedBarrier(Vector3 attackerPos)
    {
        if (CurrentPhase < 2) return false;
        
        float currentRadius = 15f;
        if (_runtimeBrain is BoneMasterPhase2AIPatternSO p2Brain)
        {
            currentRadius = p2Brain.rangedBarrierRadius;
        }

        float dist = Vector2.Distance(transform.position, attackerPos);
        return dist > currentRadius;
    }

    /// <summary>
    /// 플레이어가 투척한 뼈 창 또는 투사체가 보스에게 적중했을 때, 
    /// 2페이즈 방어막에 의해 막혔는지 여부를 반환합니다.
    /// </summary>
    public bool CheckProjectileBlocked(Vector3 attackerPos)
    {
        if (IsOutsideRangedBarrier(attackerPos))
        {
            Debug.Log("<color=grey>[BoneMaster]</color> 원거리 투사체를 튕겨냄! (거리 15 밖)");
            SetStateText("튕겨냄!", Color.grey);
            return true;
        }
        return false;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        // 2페이즈 기본 방어 상태 가시 데미지 (플레이어 전용)
        if (CurrentPhase == 2 && !_isStunned && !_isVulnerable)
        {
            if (collision.CompareTag("Player") || collision.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                var hp = collision.GetComponentInParent<CharacterHealth>();
                if (hp == null) hp = collision.GetComponentInChildren<CharacterHealth>();
                
                if (hp != null)
                {
                    DamageInfo dInfo = new DamageInfo(1f, DamageType.Fixed, gameObject);
                    hp.GetDamage(dInfo); // 무조건 1 데미지
                }
            }
        }
    }

    private IEnumerator StunAndVulnerableRoutine()
    {
        // 1. 즉시 4초 기절
        _isStunned = true;
        SetStateText("기절! (방어력 0)", Color.yellow);
        
        // 애니메이션 등에 스턴 신호 전달
        if (Stats != null && Stats.Status != null) Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, 4f);
        
        yield return new WaitForSeconds(4f);

        // 2. 기절 끝, 12초간 취약(회복 대기) 상태
        _isStunned = false;
        _isVulnerable = true;
        SetStateText("취약 (회복 대기 중...)", Color.gray);
        yield return new WaitForSeconds(12f);

        // 3. 12초 후 뼈 갑옷 원상 복구 및 루프 리셋
        _isVulnerable = false;
        _currentBoneArmor = maxBoneArmor;
        if (Stats != null) Stats.SetCurrentDef(_currentBoneArmor);
        
        SetStateText("뼈 갑옷 재생!", Color.white);
        Debug.Log("<color=cyan>[BoneMaster]</color> 뼈 갑옷 재생 완료.");
    }

    private bool HandlePhaseTransition(CharacterHealth hp)
    {
        if (CurrentPhase == 1)
        {
            // 1페이즈 체력이 다 떨어졌을 때 호출됨 -> 2페이즈 진입
            Debug.Log("<color=red>[BoneMaster]</color> 페이즈 1 종료! 페이즈 2 진입...");
            SetStateText("양손검 각성! (페이즈 2)", Color.red);
            
            CurrentPhase = 2;
            
            // OnBeforeDeath를 한 번만 처리하도록 구독 해제 (2페이즈에선 죽게 냅둠)
            if (Stats != null && Stats.Health != null) Stats.Health.OnBeforeDeath -= HandlePhaseTransition;

            StartCoroutine(Phase2TransitionRoutine());
            
            return true; // 죽음을 취소함
        }
        return false;
    }

    private IEnumerator Phase2TransitionRoutine()
    {
        // 1초 무적 및 연출 대기
        _isStunned = true; // 잠시 행동 불가
        if (Stats != null && Stats.Health != null) Stats.Health.Invincible = true;

        yield return new WaitForSeconds(1.5f);

        // 2페이즈 스탯 및 AI 패턴 교체 (1페이즈 뇌에서 2페이즈 데이터 정보를 가져옴)
        BoneMasterAIPatternSO p1Brain = Brain as BoneMasterAIPatternSO;
        if (p1Brain != null && p1Brain.phase2Data != null)
        {
            // 1. 미니언 데이터로 스탯 전체 덮어씌우기 (HP 700, DEF 50 등)
            Stats.InitializeStats(p1Brain.phase2Data);
            
            // 2. 뼈 갑옷 컨트롤러 변수 동기화
            maxBoneArmor = p1Brain.phase2Data.defense;
            _currentBoneArmor = maxBoneArmor;

            // 3. 미니언 데이터에 연결된 2페이즈 AI 패턴 교체
            if (p1Brain.phase2Data.aiPattern != null)
            {
                var newAi = ScriptableObject.Instantiate(p1Brain.phase2Data.aiPattern);
                newAi.Init(this);
                _runtimeBrain = newAi; // 상위 클래스(BaseEntity)의 런타임 뇌 교체
            }
        }

        if (Stats != null && Stats.Health != null) Stats.Health.Invincible = false;
        _isStunned = false;

        Debug.Log("<color=red>[BoneMaster]</color> 페이즈 2 전투 시작!");
    }

    private void OnDrawGizmosSelected()
    {
        // 2페이즈 투사체 방어막 반경 표시 (기즈모)
        if (CurrentPhase == 2)
        {
            float currentRadius = 15f;
            if (_runtimeBrain is BoneMasterPhase2AIPatternSO p2Brain)
            {
                currentRadius = p2Brain.rangedBarrierRadius;
            }

            Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, currentRadius);
        }
    }
}
