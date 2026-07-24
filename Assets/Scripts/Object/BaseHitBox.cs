using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 모든 형태(원형, 사각형 등)의 장판/히트박스 프리팹에 공용으로 붙일 수 있는 스크립트입니다.
/// Trigger Collider2D를 이용해 들어온 적에게 데미지를 줍니다.
/// </summary>
public class BaseHitBox : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("장판이 유지되는 시간 (0이면 무한유지)")]
    public float duration = 0.5f; 
    public bool destroyOnFinish = true; 
    
    [Tooltip("지속 데미지 여부 (체크 시 주기적으로 데미지, 해제 시 1회 타격)")]
    public bool isContinuousDamage = false;
    public float damageTickRate = 0.5f;

    [Header("Visuals (Optional)")]
    [Tooltip("장판이 찰 때 크기가 점점 커질 내부 이미지(Transform)")]
    public Transform fillingTransform;
    [Tooltip("가득 찼을 때의 최대 로컬 스케일")]
    public Vector3 maxFillScale = Vector3.one;

    [Header("Hit Effect")]
    [Tooltip("적중 시 생성할 타격 이펙트 프리팹 (HitEffect)")]
    public GameObject hitEffectPrefab;
    [Tooltip("적중 시 함께 생성할 파티클 이펙트 프리팹 (예: MeleeHitSparkEffect)")]
    public GameObject hitParticlePrefab;
    [Tooltip("빙결(Freeze) 상태인 대상을 때렸을 때 타격 이펙트에 입힐 색상")]
    public Color frozenHitEffectColor = Color.blue;


    [HideInInspector] public float hitEffectAngle = 0f; // [추가] 히트 이펙트 전용 실제(월드) 방향 각도. 부모(공격자) 미러링 보정과는 무관하게 실제 타격 방향을 그대로 담습니다.

    public enum FillStyle { Uniform, Horizontal }
    [Tooltip("장판 차오르는 방식 (Uniform: 점방사형, Horizontal: 로딩바형)")]
    public FillStyle fillStyle = FillStyle.Uniform;

    private DamageInfo _damageInfo;
    private LayerMask _targetLayer;
    private bool _isInitialized = false;
    private float _elapsed;
    // 대상별 '지금까지 때린 횟수'. 공용 타이머를 쓰면 첫 번째 적이 틱을 독점한다.
    // 다음 '시각'이 아니라 '횟수'를 들고 있는 게 핵심 — 아래 OnTriggerStay2D 주석 참조.
    private readonly Dictionary<IDamageable, int> _tickCount = new Dictionary<IDamageable, int>();

    // 1회 타격 시 중복 타격 방지
    private HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();
    private System.Action<CharacterHealth> _onHitEnemy;

    // 이벤트 펄스로만 타격하는 모드(넉백 없는 다단히트용). true 면 OnTriggerStay2D 자동타격을 끄고,
    // PulseDamageOverlapping() 호출 때마다 겹친 대상을 1회씩 때린다 — OnTriggerStay 재호출(대상이 안 움직여
    // 리지드바디가 잠들면 안 옴)에 의존하지 않아, 넉백 없는 스킬도 다단히트가 온전히 들어간다.
    private bool _manualHitOnly;
    private static readonly List<Collider2D> _overlapScratch = new List<Collider2D>();

    /// <summary>이벤트 펄스 전용 타격 모드 on/off. 이벤트당 다단히트(useEvent) 경로에서 켠다.</summary>
    public void SetManualHitOnly(bool value) => _manualHitOnly = value;

    /// <summary>
    /// '이미 때린 대상' 기록을 지운다. 다음 물리 스텝에 범위 안 대상들을 다시 한 번 때린다.
    ///
    /// 애니메이션의 OnHitEvent 하나 = 타격 하나로 쓰기 위한 것. 적 공격 클립이 2타면 OnHitEvent 를
    /// 2번 박는 것과 같은 방식이라(BaseEntity 의 [애니메이션 작업자 가이드라인] 참조), 타격 시점이
    /// 그림에 직접 박혀 있어서 시간 배분을 따로 계산할 필요가 없다.
    /// </summary>
    public void ResetHitTargets() => _hitTargets.Clear();

    /// <summary>
    /// 지금 이 히트박스와 겹친 대상들을 즉시 1회씩 때린다(이 호출 = 1타).
    /// OnTriggerStay2D 의 매 프레임 재호출에 기대지 않는 '확정 타격'이라, 대상이 안 움직여
    /// 리지드바디가 잠들어도(넉백 없는 다단히트 등) 타격이 씹히지 않는다. 이벤트당 다단히트가 이걸 쓴다.
    /// </summary>
    public void PulseDamageOverlapping()
    {
        if (!_isInitialized) return;
        var col = GetComponent<Collider2D>();
        if (col == null || !col.enabled) return;

        ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(_targetLayer);
        _overlapScratch.Clear();
        col.Overlap(filter, _overlapScratch);

        foreach (var hit in _overlapScratch)
        {
            if (hit == null) continue;
            var damageable = hit.GetComponent<IDamageable>()
                ?? hit.GetComponentInParent<IDamageable>()
                ?? hit.GetComponentInChildren<IDamageable>();
            if (damageable == null || damageable.IsDead) continue;
            bool wasFrozen = IsTargetFrozen(hit); // 빙결은 피격 즉시 풀리므로 때리기 전에 읽는다
            damageable.TakeDamage(_damageInfo);
            SpawnHitEffect(hit.transform.position, wasFrozen);
            if (damageable is CharacterHealth ch) _onHitEnemy?.Invoke(ch);
        }
    }

    public void Init(DamageInfo damageInfo, LayerMask targetLayer, float overrideDuration = -1f, float startDelay = 0f, bool isAlly = false, System.Action<CharacterHealth> onHitEnemy = null)
    {
        _onHitEnemy = onHitEnemy;
        _damageInfo = damageInfo;
        _targetLayer = targetLayer;
        
        if (overrideDuration > 0)
            duration = overrideDuration;

        // [추가] 아군/적군에 따른 장판 색상 자동 변경 (파란색 / 빨간색)
        if (fillingTransform != null)
        {
            var sr = fillingTransform.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                // 투명도(Alpha)를 유지하면서 색상만 변경
                float alpha = sr.color.a;
                sr.color = isAlly ? new Color(0.2f, 0.6f, 1f, alpha) : new Color(1f, 0.2f, 0.2f, alpha);
            }
        }

        // 선딜레이가 끝날 때까지 콜라이더 비활성화
        var col = GetComponent<Collider2D>();
        if (col != null && startDelay > 0f)
        {
            col.enabled = false;
        }

        if (fillingTransform != null)
        {
            fillingTransform.localScale = Vector3.zero; // 시각적 크기 0으로 초기화
        }

        if (startDelay > 0f)
        {
            StartCoroutine(DelayRoutine(startDelay, col));
        }
        else
        {
            if (fillingTransform != null) fillingTransform.localScale = maxFillScale;
            ActivateHitBox();
        }

        if (destroyOnFinish && duration > 0)
        {
            // startDelay가 있으므로 파괴 시간도 늦춰야 함
            Destroy(gameObject, duration + startDelay);
        }
    }

    private System.Collections.IEnumerator DelayRoutine(float delay, Collider2D col)
    {
        float timer = 0f;
        while (timer < delay)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / delay);
            
            if (fillingTransform != null)
            {
                if (fillStyle == FillStyle.Horizontal)
                {
                    // 두께(Y,Z)는 처음부터 최대로 유지하고 가로(X)만 0에서 최대로 커지는 로딩바 방식
                    Vector3 startScale = new Vector3(0f, maxFillScale.y, maxFillScale.z);
                    fillingTransform.localScale = Vector3.Lerp(startScale, maxFillScale, progress);
                }
                else
                {
                    // 점 하나에서부터 상하좌우로 둥글게 커지는 방식
                    fillingTransform.localScale = Vector3.Lerp(Vector3.zero, maxFillScale, progress);
                }
            }
            
            yield return null;
        }

        ForceActivate();
    }

    /// <summary>
    /// 외부에서 강제로 선딜레이를 끝내고 즉시 데미지 판정을 시작할 때 호출합니다. (애니메이션 이벤트 연동용)
    /// </summary>
    public void ForceActivate()
    {
        if (_isInitialized) return; // 이미 활성화되었다면 무시

        StopAllCoroutines(); // 진행 중이던 DelayRoutine 취소

        if (fillingTransform != null) fillingTransform.localScale = maxFillScale;
        
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;
        
        ActivateHitBox();

        // [수정] 애니메이션 타격 프레임에 강제 발동된 시점으로부터 duration만큼 유지 후 삭제
        if (destroyOnFinish && duration > 0)
        {
            Destroy(gameObject, duration);
        }
    }

    private void ActivateHitBox()
    {
        _isInitialized = true;
        _elapsed = 0f;
        _tickCount.Clear(); // 대상별 타격 횟수 초기화 (첫 접촉 시 즉시 1타)
    }

    private void Update()
    {
        if (!_isInitialized) return;

        if (isContinuousDamage)
        {
            _elapsed += Time.deltaTime;
        }
    }

    private void OnTriggerStay2D(Collider2D col)
    {
        if (!_isInitialized) return;
        if (_manualHitOnly) return; // 이벤트 펄스 전용 모드 — 자동타격 안 함(PulseDamageOverlapping 로만 때린다)

        // 타겟 레이어 검사
        if (((1 << col.gameObject.layer) & _targetLayer) == 0) return;

        var damageable = col.GetComponent<IDamageable>();
        if (damageable == null) damageable = col.GetComponentInParent<IDamageable>();
        if (damageable == null) damageable = col.GetComponentInChildren<IDamageable>();

        if (damageable != null && !damageable.IsDead)
        {
            if (isContinuousDamage)
            {
                // 지속 데미지 (장판). 틱은 '대상별'로 센다 —
                // 예전엔 공용 타이머를 첫 번째 적이 리셋해버려서, N타짜리 장판이 범위 안 적들에게
                // N타를 '나눠주는' 꼴이었다 (적 3명이면 각자 ~N/3타). 소환수 액티브가 "범위 내의
                // 적에게 5번의 피해"를 표방하므로 각 적이 온전히 N타를 받아야 한다.
                // 마감 시각을 '관측된 _elapsed + tickRate' 로 재예약하면 매 틱의 초과분이
                // 다음 마감에 누적돼, 5타짜리는 마지막 1타가 히트박스 수명 밖으로 밀려난다
                // (60fps 에서 드리프트 0.083s vs 여유 0.100s — 프레임이 한 번만 튀어도 4타).
                // 그래서 절대 격자로 센다: n 번째 타격은 항상 n * tickRate 지점.
                _tickCount.TryGetValue(damageable, out int n);   // 없으면 0 -> _elapsed >= 0 -> 즉시 1타
                if (_elapsed >= n * damageTickRate)
                {
                    bool wasFrozen = IsTargetFrozen(col); // 빙결은 피격 즉시 풀리므로 때리기 전에 읽는다
                    damageable.TakeDamage(_damageInfo);
                    SpawnHitEffect(col.transform.position, wasFrozen);
                    _tickCount[damageable] = n + 1;

                    if (damageable is CharacterHealth ch)
                    {
                        _onHitEnemy?.Invoke(ch);
                    }
                }
            }
            else
            {
                // 단발성 히트박스 (1번만 타격)
                if (!_hitTargets.Contains(damageable))
                {
                    _hitTargets.Add(damageable);
                    bool wasFrozen = IsTargetFrozen(col); // 빙결은 피격 즉시 풀리므로 때리기 전에 읽는다
                    damageable.TakeDamage(_damageInfo);
                    SpawnHitEffect(col.transform.position, wasFrozen);
                    
                    if (damageable is CharacterHealth ch)
                    {
                        _onHitEnemy?.Invoke(ch);
                    }
                }
            }
        }
    }



    /// <summary>
    /// 적중 위치에 타격 이펙트(HitEffect)를 생성합니다.
    /// </summary>
    private void SpawnHitEffect(Vector3 position, bool frozenTarget = false)
    {
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, position, Quaternion.Euler(0f, 0f, hitEffectAngle));
            if (frozenTarget) TintEffect(effect, frozenHitEffectColor);
            Destroy(effect, 1.0f);
        }

        if (hitParticlePrefab != null)
        {
            GameObject particle = Instantiate(hitParticlePrefab, position, Quaternion.Euler(0f, 0f, hitEffectAngle));
            if (frozenTarget) TintEffect(particle, frozenHitEffectColor);
            Destroy(particle, 1.0f);
        }
    }

    /// <summary>
    /// 이펙트 인스턴스를 지정 색으로 물들인다. HitEffect 는 SpriteRenderer, 파티클 계열은 ParticleSystem 이라
    /// 둘 다 처리한다(자식까지 포함). 알파는 원본 값을 유지해 페이드 연출을 깨지 않는다.
    /// </summary>
    private static void TintEffect(GameObject effect, Color tint)
    {
        if (effect == null) return;

        foreach (var sr in effect.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.color = new Color(tint.r, tint.g, tint.b, sr.color.a);
        }

        foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            Color baseColor = main.startColor.color;
            main.startColor = new Color(tint.r, tint.g, tint.b, baseColor.a);
        }
    }

    /// <summary>
    /// 타격 대상이 지금 빙결 상태인지. 반드시 TakeDamage '전'에 읽어야 한다 —
    /// 피해가 들어가면 CharacterStatus.OnDirectDamageTaken 이 빙결을 즉시 해제하므로,
    /// 때린 뒤에 물으면 항상 false 가 되어 푸른 이펙트가 절대 안 나온다.
    /// </summary>
    private static bool IsTargetFrozen(Component target)
    {
        if (target == null) return false;
        var status = target.GetComponent<CharacterStatus>()
            ?? target.GetComponentInParent<CharacterStatus>()
            ?? target.GetComponentInChildren<CharacterStatus>();
        return status != null && status.HasStatus(StatusType.Freeze);
    }
}
