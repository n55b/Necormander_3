using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 엘리트 몬스터(차저)가 방 진입 시 4방향에 소환하는 기둥입니다. (v1.3 전면 개편)
///
/// 4단계 상태머신으로 동작합니다:
/// [상태1: 활성화] -&gt; [상태2: 2초 균열] -&gt; [상태3: 마법진 충전] -&gt; [상태4: 재생성 쿵!] -&gt; (다시 상태1)
///
/// - 활성화: 평소 상태. 은은한 초록 오라로 상호작용 가능함을 알립니다. 기둥 뒤에 숨으면
///   차저의 공격을 회피할 수 있습니다.
/// - 2초 균열: 내구도가 0이 되면 즉시 사라지지 않고 2초간 버팁니다(시한폭탄). 이 안에
///   플레이어가 기본 공격(평타)으로 막타를 넣으면 파편이 보스에게 날아가 큰 경직(넉백)과
///   3초 그로기를 줍니다. 놓치면 그냥 조용히 무너지며 안전지대 밖에 폭발 피해를 줍니다.
/// - 마법진 충전: 무너진 자리에 거대한 마법진 전조가 15초에 걸쳐 서서히 채워집니다.
///   이 동안은 기둥이 없으므로 숨을 수 없습니다.
/// - 재생성 쿵!: 충전이 끝나면 기둥이 다시 솟아오릅니다. 이때 보스가 마법진 범위
///   (=원래 폭발 반경) 안에 있었다면(플레이어가 유도했다면) 역으로 피해 10 + 그로기 3초를 입습니다.
/// </summary>
public class EliteMonsterPillar : MonoBehaviour
{
    public enum PillarState { Active, Cracking, Charging }

    [Header("내구도 설정")]
    [SerializeField] private int maxHP = 4;
    [SerializeField] private int curHP;

    [Header("안전지대 설정")]
    [Tooltip("이 반경 안에 있으면 기둥 뒤에 숨은 것으로 간주합니다. 넉넉하게 잡아야 '기둥 뒤에 숨기'가 안정적으로 인식됩니다.")]
    [SerializeField] private float shelterRadius = 1.8f;
    [Tooltip("기둥이 무너질 때의 폭발/재생성 유도 판정에 쓰이는 반경")]
    [SerializeField] private float explosionRadius = 2f;
    [SerializeField] private float explosionDamage = 15f;
    [Tooltip("내구도 소진으로 균열이 시작될 때(DamagePattern)만 적용되는 즉시 충격 피해. 돌진 직격(CollapseInstantly)에는 적용되지 않습니다.")]
    [SerializeField] private float impactDamage = 10f;

    [Header("v1.3 재생성 시스템")]
    [Tooltip("내구도 0 이후, 완전히 무너지기까지의 유예 시간 (카운터 가능 구간)")]
    [SerializeField] private float crackDuration = 2f;
    [Tooltip("무너진 뒤 마법진이 채워져 다시 솟아오르기까지의 시간")]
    [SerializeField] private float rechargeDuration = 20f;
    [Tooltip("재생성 순간 보스가 마법진 범위 안에 있었을 때 입는 피해")]
    [SerializeField] private float regenCounterDamage = 20f;
    [Tooltip("균열 상태에서 플레이어 평타 막타로 카운터를 성공했을 때, 파편이 꼽혀며 보스에게 주는 피해")]
    [SerializeField] private float counterHitDamage = 20f;
    [Tooltip("재생성 순간 유도 그로기 시간")]
    [SerializeField] private float regenCounterGroggyDuration = 3f;
    [SerializeField] private Color auraColor = new Color(0.3f, 1f, 0.4f, 0.22f);
    [SerializeField] private Color magicCircleColor = new Color(0.35f, 0.65f, 1f, 0.5f);

    [Header("체력 표시(작은 4칸 바)")]
    [SerializeField] private bool showHealthBar = true;
    [SerializeField] private Vector3 healthBarOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private float healthPipSize = 0.14f;
    [SerializeField] private float healthPipSpacing = 0.18f;
    [SerializeField] private Color healthPipFilledColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color healthPipEmptyColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);

    [Header("Refs")]
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private Collider2D physicsCollider;

    [Header("기둥 비주얼 (프리팹에 미리 배치된 세그먼트 그룹)")]
    [Tooltip("온전한 기둥 세그먼트(Traps_14~17)들을 담은 그룹 오브젝트. 프리팹 자식으로 미리 배치. 미지정 시 자식 'Pillar_Intact' 탐색.")]
    [SerializeField] private GameObject intactVisual;
    [Tooltip("부서진 기둥 세그먼트(Traps_18~21) 그룹. 균열 상태에서 표시. 프리팹 자식으로 미리 배치(기본 비활성). 미지정 시 자식 'Pillar_Broken' 탐색.")]
    [SerializeField] private GameObject brokenVisual;

    /// <summary>이 기둥을 소환한 엘리트 몬스터(공격자 정보로 사용).</summary>
    public GameObject Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            // v1.4 B1: Awake 시점엔 아직 Owner가 없어 리시버 레이어를 못 잡는 경우가 있어, Owner가
            // 나중에 세팅되는 시점에도 기존에 만들어진 히트 리시버들의 레이어를 다시 맞춰줍니다.
            if (_owner != null)
            {
                if (_activeHitReceiverObj != null) _activeHitReceiverObj.layer = _owner.layer;
                if (_hitReceiverObj != null) _hitReceiverObj.layer = _owner.layer;
            }
        }
    }
    private GameObject _owner;

    /// <summary>상태1(활성화)일 때만 true. 돌진/충격파 등 물리적 충돌 판정에 사용됩니다.</summary>
    public bool IsAlive => _state == PillarState.Active;
    /// <summary>기둥 뒤에 숨어 회피할 수 있는지 여부. 활성화/균열 상태에서는 true, 충전(마법진) 상태에서는 false.</summary>
    public bool ProvidesShelter => _state == PillarState.Active || _state == PillarState.Cracking;
    public PillarState State => _state;

    public System.Action<EliteMonsterPillar> OnCollapsed;
    public System.Action<EliteMonsterPillar> OnFullyDestroyed;

    private PillarState _state = PillarState.Active;
    private GameObject _healthBarRoot;
    private readonly List<SpriteRenderer> _healthPips = new List<SpriteRenderer>();

    private GameObject _auraObj;
    private GameObject _crackWarningObj;
    private GameObject _magicCircleObj;
    private GameObject _magicCircleFillObj;
    private GameObject _hitReceiverObj;
    private GameObject _activeHitReceiverObj; // v1.4 B1: 활성화 상태에서 평타 파괴를 받기 위한 리시버
    private GameObject _intactRoot;  // 온전한 기둥(14~17 스택)
    private GameObject _brokenRoot;  // 부서진 기둥(18~21 스택)
    private Coroutine _stateCoroutine;

    private void Awake()
    {
        curHP = maxHP;
        if (physicsCollider == null) physicsCollider = GetComponent<Collider2D>();

        // 세그먼트 그룹은 프리팹에 미리 authoring 되어 있다. 미지정이면 이름으로 자식 탐색.
        if (intactVisual == null) { var t = transform.Find("Pillar_Intact"); if (t != null) intactVisual = t.gameObject; }
        if (brokenVisual == null) { var t = transform.Find("Pillar_Broken"); if (t != null) brokenVisual = t.gameObject; }
        _intactRoot = intactVisual;
        _brokenRoot = brokenVisual;

        EnterActiveState(initial: true);
    }

    private void Start()
    {
        // 세그먼트들의 동적 Y정렬 기준점을 '기둥 밑동(루트)'으로 통일한다. 그래야 키 큰 기둥이 한 덩어리로
        // 정렬되어, 플레이어가 밑동보다 아래(앞)에 있으면 기둥 앞에, 위(뒤)에 있으면 기둥 뒤에 가려진다.
        // (세그먼트마다 자기 Y로 정렬하면 플레이어 몸이 기둥 중간에서 잘려 보인다.) baseOffset은 세그먼트 간
        // 상대 순서(아래일수록 위)만 담당하고, 실제 깊이는 이 공통 밑동 Y가 결정한다.
        // Start에서 세팅하는 이유: 모든 컴포넌트 Awake(=YSortableObject가 pivot을 자기 자신으로 초기화하는 시점)
        // 이후에 덮어써야 하고, 세그먼트는 isDynamic=true라 이후 LateUpdate에서 이 기준으로 재정렬되기 때문.
        foreach (var ys in GetComponentsInChildren<YSortableObject>(true))
            ys.SetPivot(transform);
    }

    // 자신이 만든 FX(오라/균열 경고/마법진 재생성 마커/카운터 리시버/체력바)를 파괴 시 함께 정리한다.
    // 마법진·균열 마커 등 일부는 이 오브젝트의 자식이 아니라 월드-스페이스 오브젝트라, 기둥이
    // (보스 사망 등으로) 통째로 파괴될 때 여기서 치우지 않으면 마커가 씬에 그대로 남는다.
    private void OnDestroy()
    {
        DestroyIfExists(ref _auraObj);
        DestroyIfExists(ref _crackWarningObj);
        DestroyIfExists(ref _magicCircleObj);
        DestroyIfExists(ref _magicCircleFillObj);
        DestroyIfExists(ref _hitReceiverObj);
        DestroyIfExists(ref _activeHitReceiverObj);
        DestroyIfExists(ref _healthBarRoot);
    }

    public void SetMaxHP(int hp)
    {
        maxHP = hp;
        curHP = hp;
        RebuildHealthBar();
    }

    /// <summary>
    /// worldPos가 기둥의 안전지대(shelterRadius) 안에 있는지 검사합니다.
    /// </summary>
    public bool IsSheltering(Vector2 worldPos)
    {
        if (!ProvidesShelter) return false;
        return Vector2.Distance(transform.position, worldPos) <= shelterRadius;
    }

    /// <summary>
    /// 패턴에 의한 내구도 피해입니다. (예: 중력 도넛 폭발 / 바닥 충격파에서 숨은 기둥에게)
    /// 내구도가 0이 되면 즉시 무너지지 않고 2초 균열 상태로 전환됩니다.
    /// </summary>
    public void DamagePattern(int amount)
    {
        if (_state != PillarState.Active) return;
        curHP -= amount;
        UpdateHealthBar();
        if (curHP <= 0) EnterCrackingState(dealImmediateImpact: true);
    }

    /// <summary>
    /// v1.4 B1: 활성화 상태 기둥도 플레이어 평타로 파괴할 수 있습니다. 평타 1대당 내구도 1을 잃습니다.
    /// 패턴 피해(DamagePattern)와 달리 즉시 충격 피해가 없습니다 (플레이어가 기둥 바로 옆에서 때리는
    /// 상황이므로, 폭발 피해를 주면 스스로를 때리는 꼴이 되어버리기 때문입니다).
    /// </summary>
    public void DamageByPlayerBasicAttack()
    {
        if (_state != PillarState.Active) return;
        curHP -= 1;
        UpdateHealthBar();
        if (curHP <= 0) EnterCrackingState(dealImmediateImpact: false);
    }

    /// <summary>
    /// 체력과 무관하게 즉시 균열 상태로 전환합니다. (강한 돌진 직격)
    /// 기획서대로, 이 경로에서는 즉시 피해가 전혀 없습니다.
    /// </summary>
    public void CollapseInstantly()
    {
        if (_state != PillarState.Active) return;
        curHP = 0;
        UpdateHealthBar();
        EnterCrackingState(dealImmediateImpact: false);
    }

    // ==============================================================
    // 상태 전환
    // ==============================================================
    private void EnterActiveState(bool initial)
    {
        _state = PillarState.Active;
        curHP = maxHP;

        if (physicsCollider != null) physicsCollider.enabled = true;
        if (_intactRoot != null) _intactRoot.SetActive(true);
        if (_brokenRoot != null) _brokenRoot.SetActive(false);
        RebuildHealthBar();
        if (_healthBarRoot != null) _healthBarRoot.SetActive(true);

        DestroyIfExists(ref _crackWarningObj);
        DestroyIfExists(ref _magicCircleObj);
        DestroyIfExists(ref _magicCircleFillObj);
        DestroyIfExists(ref _hitReceiverObj);

        // v1.4 B1: 활성화 상태 진입 때마다(최초 소환 및 재생성 시) 평타 파괴용 리시버를 새로 만듭니다.
        DestroyIfExists(ref _activeHitReceiverObj);
        _activeHitReceiverObj = CreateActiveHitReceiver();

        if (_auraObj == null) _auraObj = CreateAura();
        _auraObj.SetActive(true);

        if (!initial)
        {
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(RegenPopRoutine());
        }
    }

    private void EnterCrackingState(bool dealImmediateImpact)
    {
        if (_state == PillarState.Charging) return; // 안전장치 (이미 사라진 상태에서 중복 호출 방지)

        _state = PillarState.Cracking;
        OnCollapsed?.Invoke(this);

        if (_auraObj != null) _auraObj.SetActive(false);
        if (_healthBarRoot != null) _healthBarRoot.SetActive(false);

        // v1.4 B1: 균열 상태로 넘어가면 평타 파괴 리시버는 더 이상 필요 없습니다 (카운터 리시버로 대체됨).
        DestroyIfExists(ref _activeHitReceiverObj);

        if (dealImmediateImpact)
        {
            // 안전지대(shelterRadius) 안에 숨어있던 대상은 제외하고 즉시 충격 피해를 줍니다.
            ApplyAreaDamage(explosionRadius, impactDamage, shelterRadius);
        }

        // 균열: 온전한 기둥을 부서진 스프라이트(18~21)로 교체
        if (_intactRoot != null) _intactRoot.SetActive(false);
        if (_brokenRoot != null) _brokenRoot.SetActive(true);

        _crackWarningObj = CreateFallbackCircle(new Color(1f, 0f, 0f, 0.35f), explosionRadius * 2f);
        _crackWarningObj.transform.position = transform.position;

        _hitReceiverObj = CreateCounterHitReceiver();

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(CrackingRoutine());
    }

    private IEnumerator CrackingRoutine()
    {
        yield return new WaitForSeconds(crackDuration);

        // 유예시간 안에 카운터를 맞지 못했다면(=아직 균열 상태라면) 그냥 조용히 무너집니다.
        if (_state == PillarState.Cracking)
        {
            ApplyAreaDamage(explosionRadius, explosionDamage, 0f);
            EnterChargingState();
        }
    }

    /// <summary>
    /// 균열 상태에서 플레이어가 평타로 막타를 넣었을 때 호출됩니다. (v1.3 카운터 기믹)
    /// 파편이 보스에게 날아가 큰 경직(넉백)과 그로기를 부여하고, 바로 마법진 충전 상태로 넘어갑니다.
    /// </summary>
public void OnCounterHit()
    {
        if (_state != PillarState.Cracking) return;

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);

        if (Owner != null)
        {
            StartCoroutine(CounterShardRoutine(Owner.transform));
        }

        EnterChargingState();
    }

    private IEnumerator CounterShardRoutine(Transform target)
    {
        GameObject shard = CreateFallbackCircle(new Color(1f, 0.55f, 0.1f, 0.9f), 0.5f);
        Vector2 start = transform.position;
        float t = 0f;
        float duration = 0.25f;
        while (t < duration && target != null)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            if (shard != null) shard.transform.position = Vector2.Lerp(start, target.position, f);
            yield return null;
        }
        if (shard != null) Destroy(shard);

        if (target == null) yield break;

        // 파편은 오직 데미지만 줍니다 (경직/그로기 없음).
        BaseEntity ownerEntity = target.GetComponent<BaseEntity>();
        if (ownerEntity != null && ownerEntity.Stats != null && ownerEntity.Stats.Health != null && !ownerEntity.Stats.Health.IsDead)
        {
            ownerEntity.Stats.Health.GetDamage(new DamageInfo(counterHitDamage, DamageType.Physical, gameObject, 1f));

            // v1.4 A1: 균열 카운터 성공 연출 강화 (화면 흔들림 + 히트스탑). 게임적 효과(경직/그로기)는 여전히 없음, 순수 피드백용.
            if (CameraManager.Instance != null) CameraManager.Instance.HitShakeCamera(2.2f);
            if (HitStopManager.Instance != null) HitStopManager.Instance.DoHitStop(0.1f);

            // v1.4 B2: 균열 카운터 성공 - 누적 강화 스택 부여
            NotifyPillarStackToBrain(ownerEntity);
        }
    }

    private void EnterChargingState()
    {
        _state = PillarState.Charging;

        DestroyIfExists(ref _crackWarningObj);
        DestroyIfExists(ref _hitReceiverObj);

        if (physicsCollider != null) physicsCollider.enabled = false;
        if (_intactRoot != null) _intactRoot.SetActive(false);
        if (_brokenRoot != null) _brokenRoot.SetActive(false);

        OnFullyDestroyed?.Invoke(this);

        _magicCircleObj = CreateFallbackRingStatic(magicCircleColor, explosionRadius * 2.2f);
        _magicCircleObj.transform.position = transform.position;

        _magicCircleFillObj = CreateFallbackCircle(new Color(magicCircleColor.r, magicCircleColor.g, magicCircleColor.b, 0.15f), 0.1f);
        _magicCircleFillObj.transform.position = transform.position;

        if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
        _stateCoroutine = StartCoroutine(ChargingRoutine());
    }

    private IEnumerator ChargingRoutine()
    {
        float t = 0f;
        float maxDiameter = explosionRadius * 2f;

        while (t < rechargeDuration)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / rechargeDuration);
            if (_magicCircleFillObj != null)
            {
                float d = Mathf.Lerp(0.1f, maxDiameter, f);
                _magicCircleFillObj.transform.localScale = new Vector3(d, d, 1f);
                var sr = _magicCircleFillObj.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(magicCircleColor.r, magicCircleColor.g, magicCircleColor.b, Mathf.Lerp(0.1f, 0.5f, f));
            }
            yield return null;
        }

        // "재생성 쿵!" - 이 순간 보스가 마법진 범위(explosionRadius) 안에 있는지 확인합니다. (유도 그로기)
        if (Owner != null)
        {
            float dist = Vector2.Distance(Owner.transform.position, transform.position);
            if (dist <= explosionRadius)
            {
                BaseEntity ownerEntity = Owner.GetComponent<BaseEntity>();
                if (ownerEntity != null && ownerEntity.Stats != null && ownerEntity.Stats.Status != null)
                {
                    ownerEntity.Stats.Status.ApplyStatus(StatusType.Stun, regenCounterGroggyDuration);
                }

                CharacterStat ownerStat = Owner.GetComponent<CharacterStat>();
                if (ownerStat != null && ownerStat.Health != null && !ownerStat.Health.IsDead)
                {
                    ownerStat.Health.GetDamage(new DamageInfo(regenCounterDamage, DamageType.Physical, gameObject, 1f));
                }

                // v1.4 B2: 재생성 유도 성공 - 누적 강화 스택 부여
                NotifyPillarStackToBrain(ownerEntity);
            }
        }

        DestroyIfExists(ref _magicCircleObj);
        DestroyIfExists(ref _magicCircleFillObj);

        EnterActiveState(initial: false);
    }

    private IEnumerator RegenPopRoutine()
    {
        Vector3 baseScale = transform.localScale;
        Vector3 sunkScale = baseScale * 0.4f;
        Vector3 popScale = baseScale * 1.15f;

        transform.localScale = sunkScale;
        float t = 0f;
        float duration = 0.25f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(sunkScale, popScale, Mathf.Clamp01(t / duration));
            yield return null;
        }

        t = 0f;
        duration = 0.15f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(popScale, baseScale, Mathf.Clamp01(t / duration));
            yield return null;
        }
        transform.localScale = baseScale;
    }

    // ==============================================================
    // 카운터 히트 감지 (균열 상태에서만 활성화되는 별도 트리거)
    // ==============================================================
    private class PillarCounterHitReceiver : MonoBehaviour, IDamageable
    {
        public EliteMonsterPillar pillar;
        private bool _consumed;
        public bool IsDead => _consumed;

        public void TakeDamage(DamageInfo info)
        {
            if (_consumed || pillar == null) return;
            if (info.category != DamageCategory.BasicAttack) return; // 평타(기본 공격) 막타만 카운터로 인정합니다.
            if (info.attacker != null && info.attacker == pillar.Owner) return; // 보스 자신의 공격은 무시

            _consumed = true;
            pillar.OnCounterHit();
        }
    }

    private GameObject CreateCounterHitReceiver()
    {
        GameObject obj = new GameObject("Pillar_CounterHitReceiver");
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = Vector3.zero;

        var col = obj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.6f;

        // 플레이어의 평타가 정상적으로 타겟팅할 수 있도록, 보스(Owner)와 동일한 레이어를 사용합니다.
        if (Owner != null) obj.layer = Owner.layer;

        var receiver = obj.AddComponent<PillarCounterHitReceiver>();
        receiver.pillar = this;

        return obj;
    }

    /// <summary>
    /// v1.4 B1: 활성화 상태 전용 히트 리시버입니다. 균열 리시버(PillarCounterHitReceiver)와 달리
    /// 1회 소모되지 않고, 활성화 상태인 동안 평타를 맞을 때마다 계속 내구도를 깎습니다.
    /// </summary>
    private class PillarActiveHitReceiver : MonoBehaviour, IDamageable
    {
        public EliteMonsterPillar pillar;
        public bool IsDead => false;

        public void TakeDamage(DamageInfo info)
        {
            if (pillar == null) return;
            if (info.category != DamageCategory.BasicAttack) return; // 평타(기본 공격)만 인정합니다.
            if (info.attacker != null && info.attacker == pillar.Owner) return; // 보스 자신의 공격은 무시

            pillar.DamageByPlayerBasicAttack();
        }
    }

    private GameObject CreateActiveHitReceiver()
    {
        GameObject obj = new GameObject("Pillar_ActiveHitReceiver");
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = Vector3.zero;

        var col = obj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.6f;

        // 플레이어의 평타가 정상적으로 타겟팅할 수 있도록, 보스(Owner)와 동일한 레이어를 사용합니다.
        if (Owner != null) obj.layer = Owner.layer;

        var receiver = obj.AddComponent<PillarActiveHitReceiver>();
        receiver.pillar = this;

        return obj;
    }

    // ==============================================================
    // 상시 오라 / 마법진 등 비주얼 헬퍼
    // ==============================================================
    private GameObject CreateAura()
    {
        GameObject obj = new GameObject("Pillar_Aura");
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = Vector3.zero;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = auraColor;
        sr.sortingOrder = 4;
        obj.transform.localScale = Vector3.one * (shelterRadius * 2f);
        return obj;
    }

    private GameObject CreateFallbackCircle(Color color, float diameter)
    {
        GameObject obj = new GameObject("Pillar_Fx_Circle");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = color;
        sr.sortingOrder = 8;
        obj.transform.localScale = Vector3.one * diameter;
        return obj;
    }

    private GameObject CreateFallbackRingStatic(Color color, float diameter)
    {
        GameObject obj = new GameObject("Pillar_Fx_Ring");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateRingSprite();
        sr.color = color;
        sr.sortingOrder = 7;
        obj.transform.localScale = Vector3.one * diameter;
        return obj;
    }

    private void DestroyIfExists(ref GameObject obj)
    {
        if (obj != null)
        {
            Destroy(obj);
            obj = null;
        }
    }

    /// <summary>
    /// v1.4 B2: 기둥 파편 카운터/재생성 유도가 성공했을 때, 보스의 AI 브레인(EliteChargerAIPatternSO)에게
    /// 알려 누적 강화 스택을 쌓게 합니다. 다른 보스 AI 패턴에 붙어있는 경우 조용히 무시됩니다.
    /// </summary>
    private void NotifyPillarStackToBrain(BaseEntity ownerEntity)
    {
        if (ownerEntity == null) return;
        if (ownerEntity.Brain is EliteChargerAIPatternSO chargerBrain)
        {
            chargerBrain.AddPillarDamageStack();
        }
    }

    /// <summary>
    /// radius 안의 대상에게 damage를 줍니다. excludeRadius가 0보다 크면 그 반경 이내(안전지대)는 제외합니다.
    /// </summary>
    private void ApplyAreaDamage(float radius, float damage, float excludeRadius)
    {
        LayerMask targetLayer = LayerMask.GetMask("Player", "Army"); // "Ally"는 프로젝트에 없는 레이어라 제거(런타임 동일)
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, targetLayer);
        foreach (var hit in hits)
        {
            float d = Vector2.Distance(transform.position, hit.transform.position);
            if (excludeRadius > 0f && d <= excludeRadius) continue;

            CharacterStat stat = hit.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = hit.GetComponentInChildren<CharacterStat>();
            if (stat != null && stat.Health != null && !stat.Health.IsDead)
            {
                DamageInfo info = new DamageInfo(damage, DamageType.Physical, Owner, 1f);
                stat.Health.GetDamage(info);
            }
        }
    }

    // ==============================================================
    // 작은 4칸 체력 표시
    // ==============================================================
    private void RebuildHealthBar()
    {
        if (_healthBarRoot != null)
        {
            Destroy(_healthBarRoot);
            _healthPips.Clear();
        }

        if (!showHealthBar || maxHP <= 0) return;

        _healthBarRoot = new GameObject("Pillar_HealthBar");
        _healthBarRoot.transform.SetParent(transform, false);
        _healthBarRoot.transform.localPosition = healthBarOffset;

        float totalWidth = (maxHP - 1) * healthPipSpacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < maxHP; i++)
        {
            GameObject pip = new GameObject("Pip" + i);
            pip.transform.SetParent(_healthBarRoot.transform, false);
            pip.transform.localPosition = new Vector3(startX + i * healthPipSpacing, 0f, 0f);

            SpriteRenderer sr = pip.AddComponent<SpriteRenderer>();
            sr.sprite = GetOrCreateSquareSprite();
            sr.sortingOrder = 20;
            pip.transform.localScale = Vector3.one * healthPipSize;

            _healthPips.Add(sr);
        }

        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        for (int i = 0; i < _healthPips.Count; i++)
        {
            if (_healthPips[i] == null) continue;
            _healthPips[i].color = i < curHP ? healthPipFilledColor : healthPipEmptyColor;
        }
    }

    // 절차적 텔레그래프 스프라이트는 BossTelegraph 로 일원화(SO/기둥/BossTelegraph 3벌 중복 제거).
    private static Sprite GetOrCreateCircleSprite() => BossTelegraph.GetCircleSprite();
    private static Sprite GetOrCreateSquareSprite() => BossTelegraph.GetSquareSprite();
    private static Sprite GetOrCreateRingSprite() => BossTelegraph.GetRingSprite();
}
