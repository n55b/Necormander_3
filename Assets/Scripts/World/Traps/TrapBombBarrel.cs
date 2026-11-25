using UnityEngine;
using System.Collections;

/// <summary>
/// 피격 시 도화선이 타들어가는 연출(Jar_Active) 후 폭발 애니메이션(Jar_Explode)을 재생하여
/// 주변 영역에 큰 데미지를 주는 폭탄 항아리(통) 함정입니다.
/// 스프라이트 프레임은 Animator(BombBarrel.controller)가 구동합니다.
/// </summary>
public class TrapBombBarrel : MonoBehaviour, IDamageable
{
    [Header("설정")]
    [SerializeField] private float hp = 1f;
    [SerializeField] private float fuseTime = 1.5f;
    [SerializeField] private float explosionRadius = 2.5f;
    [SerializeField] private float explosionDamage = 15f;
    [SerializeField] private LayerMask targetLayer;

    [Header("외형과 효과")]
    [SerializeField] private GameObject explosionEffectPrefab; // Center Skill Hitbox Circle Prefab 연결 권장
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Visual의 Animator (미지정 시 자식에서 자동 탐색)")]
    [SerializeField] private Animator animator;

    [Header("소리")]

    [Tooltip("최종 폭발 사운드")]
    [SerializeField] private AudioClip explosionSound;
    [Range(0f, 1f)]
    [SerializeField] private float explosionVolume = 1f;
    [Tooltip("폭발 시 카메라 흔들림 강도")]
    [SerializeField] private float explosionShakeForce;

    // Traps.aseprite 태그 기반 Animator 상태 (BombBarrel.controller)
    private const string StateIdle = "Jar_Idle";       // 점화 전 대기
    private const string StateFuse = "Jar_Active";     // 도화선 타들어감 (루프)
    private const string StateExplode = "Jar_Explode"; // 폭발
    private const float FallbackExplodeLength = 0.525f;
    private const string HitEventName = "OnHitEvent";
    private const float FallbackExplodeHitTime = 0.2f; // Jar_Active_0 클립의 OnHitEvent 시점

    private TrapAnimationEventRelay _relay;
    private bool _hitEventFired = false;

    private bool _isDead = false;
    private bool _isTriggered = false;
    private RuntimeAnimatorController timingController;
    private float explodeHitTime = FallbackExplodeHitTime;
    private float explodeLength = FallbackExplodeLength;

    public bool IsDead => _isDead;

    private void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        // 폭발 클립의 OnHitEvent(폭발 타격 프레임) 수신
        if (animator != null)
        {
            _relay = animator.GetComponent<TrapAnimationEventRelay>();
            if (_relay != null) _relay.HitEvent += OnRelayHit;
        }


        // 평소(점화 전)에는 Idle 상태로 대기
        PlayState(StateIdle);
        PrepareClipTiming();

        if (targetLayer == 0)
        {
            targetLayer = Layers.TrapTargets;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (_isDead) return;

        hp -= info.amount;
        if (hp <= 0)
        {
            _isDead = true;
            if (!_isTriggered)
            {
                StartCoroutine(ExplodeSequence());
            }
        }
    }

    private IEnumerator ExplodeSequence()
    {
        _isTriggered = true;

        // 타격을 받는 순간 바닥에 시각 장판(Telegraph) 생성 및 fuseTime 후 예약 작동
        SpawnExplosionTelegraph();

        // 점화: 도화선이 타들어가는 애니메이션을 fuseTime 동안 루프 재생
        PlayState(StateFuse);
        yield return new WaitForSeconds(fuseTime);

        // 최종 폭발 연출 및 파괴
        yield return StartCoroutine(PlayBoomAndDestroy());
    }

    private void SpawnExplosionTelegraph()
    {
        if (explosionEffectPrefab == null) return;

        // 타격을 받는 즉시 장판 생성
        GameObject explosionObj = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

        // 프리팹의 원형 충돌체 크기를 explosionRadius에 맞추기 위해 스케일 변경
        explosionObj.transform.localScale = new Vector3(explosionRadius * 2f, explosionRadius * 2f, 1f);

        BaseHitBox hitbox = explosionObj.GetComponent<BaseHitBox>();
        if (hitbox != null)
        {
            DamageInfo dmgInfo = new DamageInfo(explosionDamage, DamageType.Physical, gameObject, category: DamageCategory.Trap);
            // fuseTime(도화선) + 폭발 클립의 OnHitEvent 시점까지 원형 장판이 차오르고, 그 프레임에 0.2초 동안 타격 판정 적용
            hitbox.Init(dmgInfo, targetLayer, 0.2f, startDelay: fuseTime + GetExplodeHitTime(), isAlly: false);
        }
    }

    private IEnumerator PlayBoomAndDestroy()
    {
        // 폭발 애니메이션 재생
        _hitEventFired = false;
        PlayState(StateExplode);

        // 폭발 타격 프레임(OnHitEvent)까지 대기 (이벤트가 없으면 클립 기준 시간으로 대체)
        float hitTime = GetExplodeHitTime();
        float waited = 0f;
        while (!_hitEventFired && waited < hitTime + 0.1f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        PolishFeedback.Play(FeedbackCue.PropBreak, transform.position, Vector2.up);
        // 폭발 사운드 재생
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(explosionSound, explosionVolume);
        }

        // 폭발 시 카메라 흔들림
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.HitShakeCamera(explosionShakeForce);
        }

        // 남은 폭발 애니메이션 재생 후 파괴
        // (피해 판정은 장판 프리팹의 BaseHitBox가 같은 타이밍에 자체 처리)
        float remain = GetExplodeLength() - waited;
        if (remain > 0f) yield return new WaitForSeconds(remain);
        Destroy(gameObject);
    }

    private void OnRelayHit()
    {
        _hitEventFired = true;
    }

    private void OnDestroy()
    {
        if (_relay != null) _relay.HitEvent -= OnRelayHit;
    }

    private float GetExplodeHitTime()
    {
        PrepareClipTiming();
        return explodeHitTime;
    }

    private void PrepareClipTiming()
    {
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == timingController) return;
        timingController = controller;
        explodeHitTime = ReadExplodeHitTime();
        explodeLength = ReadExplodeLength();
    }

    private float ReadExplodeHitTime()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null || (clip.name != StateExplode && clip.name != "Jar_Active_0")) continue;
                foreach (var e in clip.events)
                {
                    if (e.functionName == HitEventName) return e.time;
                }
            }
        }
        return FallbackExplodeHitTime;
    }


    private void PlayState(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.Play(stateName, 0, 0f);
    }

    private float GetExplodeLength()
    {
        PrepareClipTiming();
        return explodeLength;
    }

    private float ReadExplodeLength()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                // 폭발 상태에 연결된 클립 (Aseprite 태그명 Jar_Active_0)
                if (clip != null && (clip.name == StateExplode || clip.name == "Jar_Active_0"))
                {
                    return clip.length;
                }
            }
        }
        return FallbackExplodeLength;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
