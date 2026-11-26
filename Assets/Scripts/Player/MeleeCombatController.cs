using UnityEngine;

public partial class MeleeCombatController : MonoBehaviour
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Min(0.01f), Tooltip("플레이어 평타 히트박스의 활성 시간입니다. 모션이나 콤보 수는 변경하지 않습니다.")]
    private float attackHitActiveDuration = 0.2f;

    [SerializeField] private GameObject telegraphPrefab; // 인스펙터 할당
    public GameObject TelegraphPrefab => telegraphPrefab;

    [Header("공격 스폰 포인트")]
    [Tooltip("플레이어 공격 히트박스가 스폰될 시작 기준점 트랜스폼입니다. 미지정 시 플레이어 본체 피벗을 사용합니다.")]
    [SerializeField] private Transform attackSpawnPoint;

    [Header("평타 준비 이동")]
    [Tooltip("공격 모션 전 입력 방향으로 이동하는 거리의 배율입니다. 0이면 바로 공격합니다.")]
    [SerializeField] private float lightAttackDashMultiplier = 1.0f;

    [SerializeField, Range(0f, 1f), Tooltip("준비 이동이 끝난 뒤 다음 평타를 낼 수 있을 때까지 적용하는 걷기 속도 비율입니다.")]
    private float attackWalkSpeedMultiplier = 0.25f;

    private PlayerController _player;
    private float _lastAttackTime;
    // 0/1/2 = 플레이어 3연타. 소환수 일격은 별도 내부 쿨타임으로 어떤 타수에도 붙는다.
    private int _comboStep = 0;

    /// <summary>플레이어 자체 3연타. 마리오네트도 콤보/모션 수는 바꾸지 않는다.</summary>
    private const int PLAYER_COMBO_LENGTH = 3;

    /// <summary>소환수 유무와 무관한 플레이어 콤보 수.</summary>
    private int ComboLength => PLAYER_COMBO_LENGTH;

    // 소환수 자동 공격은 플레이어 평타와 연결되지 않는다(CastSummonBasicAttack 참고). 주기는
    // PlayerSkillController.autoAttackInterval 이 정한다 — finisher.internalCooldown 은 쓰지 않는다.

    [Header("콤보 설정")]
    [SerializeField] private float comboResetTime = 1.0f;
    [SerializeField] private float attackCooldown = 0.3f; // 콤보 간 최소 딜레이

    [Header("타격 범위 설정")]
    [SerializeField] private Vector2 lightHitboxSize = new Vector2(2f, 1.5f);
    [SerializeField] private float lightTelegraphDuration = 0.2f;
    [Tooltip("3타 애니메이션 이벤트 누락 시의 예비 선딜. 정상 타격은 Attack_Medium의 OnAttackHitFrame이 앞서 활성화한다.")]
    [SerializeField] private float thirdTelegraphDuration = 0.4f;

    [Header("평타 범위 표시(텔레그래프) 숨김")]
    [Tooltip("평타(기본 공격)의 범위 표시용 히트박스 시각효과를 숨깁니다. 데미지 판정은 그대로 유지됩니다. (스킬/적 텔레그래프에는 영향 없음)")]
    [SerializeField] private bool hideBasicAttackTelegraph = true;

    [Tooltip("소환수 마무리 일격의 범위 표시도 숨길지. 기본은 '보임' — 마무리는 선딜(HitDelay)이 있어서 " +
             "차오르는 바가 '어디를 언제 치는지'를 알려주는 게 평타보다 중요합니다.")]
    [SerializeField] private bool hideFinisherTelegraph = false;

    [Header("대기 소환수 효과 (평타)")]
    [Tooltip("필드에 없는 대기 소환수의 '대기 효과'(빙결·출혈·취약 등)를 플레이어 평타에 묻힌다.\n" +
             "확률·타수·지속시간은 소환수 에셋의 standbyInfusion 에서 정한다. 소환수가 1마리면 대기 소환수가 없어 발동하지 않는다.")]
    [SerializeField] private bool standbyInfusionEnabled = true;

    public bool StandbyInfusionEnabled { get => standbyInfusionEnabled; set => standbyInfusionEnabled = value; }

    /// <summary>마지막으로 평타에 묻은 대기 효과(툴·점검 표시용). 묻지 않았으면 갱신하지 않는다.</summary>
    public MainMinionDataSO LastInfusionMinion { get; private set; }
    public StatusType LastInfusionStatus { get; private set; } = StatusType.None;

    /// <summary>지금 평타에 묻을 수 있는 대기 소환수. 꺼져 있거나 대기 소환수·효과가 없으면 null.</summary>
    public MainMinionDataSO GetStandbyInfusionSource()
    {
        if (!standbyInfusionEnabled || _player == null) return null;
        var skills = _player.GetComponent<PlayerSkillController>();
        var standby = skills != null ? skills.StandbySummon : null;
        return standby != null && standby.HasStandbyInfusion ? standby : null;
    }

    /// <summary>
    /// 이번 휘두르기에 대기 효과들을 효과마다 따로 추첨한다.
    /// 첫 번째로 붙은 효과는 피해 정보(applyStatus)에 싣고, 두 번째부터는 적중 콜백으로 따로 건다.
    /// </summary>
    /// <returns>추가로 걸 효과가 있으면 그 적중 콜백, 없으면 null.</returns>
    private System.Action<CharacterHealth> ApplyStandbyInfusion(ref DamageInfo info, int swingIndex)
    {
        LastInfusionStatuses.Clear();
        var source = GetStandbyInfusionSource();
        if (source == null) return null;
        System.Collections.Generic.List<MinionStandbyInfusion> extras = null;
        foreach (var inf in source.standbyInfusions)
        {
            if (inf == null || !inf.IsActive || !inf.AppliesToSwing(swingIndex) || Random.value >= inf.chance) continue;
            LastInfusionStatuses.Add(inf.status);
            if (!info.applyStatus.HasValue)
            {
                info.applyStatus = inf.status;
                info.statusDuration = inf.duration;
            }
            else (extras ??= new System.Collections.Generic.List<MinionStandbyInfusion>()).Add(inf);
        }
        if (LastInfusionStatuses.Count == 0) return null;
        LastInfusionMinion = source;
        LastInfusionStatus = LastInfusionStatuses[0];
        if (extras == null) return null;

        float power = _player.Stat != null ? _player.Stat.ATK * (1f + _player.Stat.PHYS_AMP) : 0f;
        return health =>
        {
            if (health == null || health.IsDead) return;
            var status = health.GetComponent<CharacterStatus>();
            if (status == null) status = health.GetComponentInParent<CharacterStatus>();
            if (status == null) return;
            foreach (var e in extras) status.ApplyStatus(e.status, e.duration, power: power);
        };
    }

    /// <summary>마지막 휘두르기에 붙은 대기 효과들(툴·점검용). 아무것도 안 붙었으면 비어 있다.</summary>
    public readonly System.Collections.Generic.List<StatusType> LastInfusionStatuses = new System.Collections.Generic.List<StatusType>();

    private bool _isHoldingAttack = false;
    private BaseHitBox _activeHitbox;
    private bool _preparingAttack;
    private bool _attackAnimationPlaying;
    private bool _ownsAnimationLock;

    // 콤보 스텝을 함께 전달하는 공격 시작 이벤트 (int = comboStep 0/1/2)
    public event System.Action<int> OnAttackExecuted;

    // 준비 이동부터 모션, 판정창, 최소 공격 간격까지 평타가 행동 슬롯을 점유한다.
    public bool IsAttacking => _preparingAttack || _attackAnimationPlaying || _activeHitbox != null ||
                               (Time.time - _lastAttackTime) < attackCooldown;
    public bool IsAimingAttack => _ownsAnimationLock;

    /// <summary>
    /// 취소해야 할 '진행 중 공격'이 있는가 — 평캔/대쉬/패링의 캔슬 게이트 전용.
    /// 플레이어 모션/히트박스/최소 간격에 소환수와는 무관하다.
    /// 소환수 일격이 플레이어 평타보다 오래 남아도 Space 재사용 시 회수할 수 있어야 한다.
    /// 콤보 진행에는 IsAttacking만 사용한다 — 이 속성은 캔슬 게이트 전용이다.
    /// </summary>
    public bool IsInAttackAction => IsAttacking;

    public Vector2 CurrentAttackDir { get; private set; } = Vector2.right;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    /// <summary>
    /// 소환수 기본 공격. 필드 소환수 AI 가 자기 위치에서 호출한다. 플레이어 평타와는 연결되지 않는다.
    /// 피해 = 플레이어 ATK × 소환수 고유 배율 × 강화 × Inspector 의 소환수 평타 피해 배율.
    /// </summary>
    /// <returns>시전 오브젝트. 시전하지 못하면 null.</returns>
    public MinionSkillCaster CastSummonBasicAttack(MainMinionDataSO main, Vector2 origin2D, Vector2 dir)
    {
        var fin = main != null && main.finisher != null && main.finisher.IsValid ? main.finisher : null;
        if (main == null || fin == null || _player.Stat == null) return null;

        float atkSpd = Mathf.Max(0.05f, _player.Stat.ATKSPD);
        var animSet = main.basicAnim;
        float castDuration = animSet.ResolvedDuration / atkSpd;
        float hitWindow = animSet.EventHitWindow / atkSpd;

        Vector3 origin = origin2D;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        // 히트박스는 조준 각도에 맞춰 자유 회전시킨다(스프라이트는 좌우로만 뒤집힌다).
        bool faceRight = dir.x >= 0f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        // 공격 거리가 길면 그 거리의 적에게 판정이 닿도록 판정을 앞으로 민다.
        float boxOffset = Mathf.Max(fin.spawnOffset, fin.summonAttackDistance + 0.5f - fin.hitBoxSize.x * 0.5f);
        Vector3 spawnPos = origin + (Vector3)(dir * boxOffset);
        // 소환수는 서 있던 자리에서 그대로 때리고, 판정만 앞쪽(spawnPos)에 깔린다. 위치를 옮기면 때릴 때마다 순간이동해 보인다.
        Vector3 casterPos = origin;

        // 자주 나오는 공격이라 나타나는 연출은 생략한다.
        var caster = MinionSkillCaster.Spawn(main, casterPos, quiet: true);

        // 이펙트 오버레이(예: DashDoll 의 Skill_Attack_Effect)는 이제 PlaySequenced 가 effectState 로 직접
        // 겹쳐 재생한다 — 타격 이벤트가 이펙트 클립에 박힌 경우 그쪽에 relay 를 붙여야 하기 때문이다.

        // 프리팹: 소환수 SO(finisher)가 자기 것을 지정했으면 그걸, 아니면 컨트롤러의 Telegraph Prefab 으로 폴백.
        var finBoxPrefab = fin.hitBoxPrefab != null ? fin.hitBoxPrefab : telegraphPrefab;
        if (finBoxPrefab == null) return caster;

        GameObject go = Instantiate(finBoxPrefab, spawnPos, Quaternion.identity, caster.transform);
        if (hideFinisherTelegraph)
        {
            foreach (var vis in go.GetComponentsInChildren<SpriteRenderer>(true)) vis.enabled = false;
            foreach (var mask in go.GetComponentsInChildren<SpriteMask>(true)) mask.enabled = false;
        }

        var box = go.GetComponent<BaseHitBox>();
        if (box == null) return caster;

        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        go.transform.localScale = new Vector3(fin.hitBoxSize.x, fin.hitBoxSize.y, 1f);
        box.hitEffectAngle = angle;
        main.ApplyHitEffect(box);

        // Init(팀 색 포함)은 OnHit 판정창에서 늦게 불린다 → 윈드업 동안 프리팹 기본색이 뜬다.
        // 스폰 직후 직접 물들여 처음부터 아군 색이 나오게 한다.
        box.ApplyTeamColor(true);

        // 피해는 '플레이어의 ATK * 소환수 고유 배율'.
        // 예전엔 소환수 SO 자신의 attack 을 썼는데, 이제 베이스 ATK 를 공유한다.
        // 그래야 "아군 공격력 증가" 같은 버프를 플레이어 ATK 하나에만 걸어도
        // 주먹과 소환수 마무리에 동시에 먹는다. 소환수의 개성은 배율이 유지한다.
        // [강화] 보상방 강화 단계만큼 일반 공격 배율이 붙는다(MinionEnhance).
        float enhanceMult = MinionEnhance.BasicAttackMult(main);
        var weapon = EquipmentInventoryManager.Instance != null ? EquipmentInventoryManager.Instance.Weapon : null;
        enhanceMult *= weapon != null ? weapon.basicDamagePerHit : 1f;
        box.HitsPerContact = weapon != null ? weapon.basicHitsPerContact : 1;
        box.ContactHitInterval = weapon != null ? weapon.basicRepeatHitInterval : 0f;
        var summonCtrl = _player.GetComponent<PlayerSkillController>();
        float autoMult = summonCtrl != null ? summonCtrl.AutoAttackDamageMultiplier : 1f;
        var info = new DamageInfo(_player.Stat.ATK * fin.damageMultiplier * enhanceMult * autoMult, fin.element, _player.gameObject, 1f, !string.IsNullOrEmpty(main.minionName) ? $"{main.minionName} 마무리" : "Finisher", false, causesHitstun: fin.causesHitstun, knockbackForce: fin.knockbackForce, superArmorDamage: fin.superArmorDamage, category: DamageCategory.BasicAttack, applyStatus: fin.onHitStatus == StatusType.None ? (StatusType?)null : fin.onHitStatus); // 소환수 마무리 일격도 평타 갈래
        info.minionSource = caster;

        // 판정은 '언제 열지'를 애니메이션이 정한다 — 초로 박지 않는다.
        //  · damageState 를 쓰면 그 태그가 재생되는 동안만 열린다 (MeleeDoll: Slash).
        //  · hitEvent 를 쓰면 Aseprite 셀에 심어둔 event: 프레임에 열린다 (DashDoll).
        // 그때까지는 콜라이더를 꺼둔 채로 기다린다. 히트박스를 미리 만들어두는 이유는
        // 텔레그래프(차오르는 바)가 '어디를 칠지'를 그동안 보여줘야 하기 때문이다.
        var col = go.GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        caster.PlaySequenced(
            animSet, castDuration, hitWindow, fin.hitCount, faceRight,
            // 판정 열기. useContinuous=true 면 창 동안 hitCount 균등 틱, false(이벤트당 모드)면 단발+펄스.
            (window, useContinuous) =>
            {
                if (box == null) return;
                if (useContinuous && fin.hitCount > 1)
                {
                    // 균등 지속: 판정창(window) 동안 hitCount 를 균등 배분(OnHit~OnAttackEnd / 태그 / 시퀀스 전체).
                    box.isContinuousDamage = true;
                    box.damageTickRate = window / fin.hitCount;
                }
                else
                {
                    // 단발/이벤트당: 창이 열리는 순간부터 OnHitEvent 펄스가 타격을 만든다.
                    box.isContinuousDamage = false;
                }
                box.SetManualHitOnly(!useContinuous); // 이벤트당 모드면 펄스로만 타격(OnTriggerStay/sleep 비의존)
                if (col != null) col.enabled = true;
                box.Init(info, Layers.EnemyMask, window, 0f, true);
            },
            // OnHitEvent 마다 겹친 대상을 확정 1타. 적 공격 클립이 2타면 OnHitEvent 를 2번 박는 것과 같은 계약.
            onHitPulse: () => { if (box != null) box.PulseDamageOverlapping(); },
            // OnAttackEndEvent: 후딜까지 끝났으니 판정을 닫는다.
            onAttackEnd: () => { if (col != null) col.enabled = false; });
        return caster;
    }
}
