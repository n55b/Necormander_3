using UnityEngine;
using System.Collections;

/// <summary>
/// 피격 시 도화선이 타들어가는 연출(fuse_02~04) 후 폭발 애니메이션(boom_01~07)을 재생하여
/// 주변 영역에 큰 데미지를 주는 폭탄 통 함정입니다.
/// </summary>
public class TrapBombBarrel : MonoBehaviour, IDamageable
{
    [Header("Settings")]
    [SerializeField] private float hp = 1f;
    [SerializeField] private float fuseTime = 1.5f;
    [SerializeField] private float explosionRadius = 2.5f;
    [SerializeField] private float explosionDamage = 15f;
    [SerializeField] private LayerMask targetLayer;

    [Header("Visual & Effects")]
    [SerializeField] private GameObject explosionEffectPrefab; // Center Skill Hitbox Circle Prefab 연결 권장
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("폭발(boom) 프레임 한 장당 재생 시간")]
    [SerializeField] private float boomFrameDuration = 0.05f;

    [Header("Sound")]

    [Tooltip("최종 폭발 사운드")]
    [SerializeField] private AudioClip explosionSound;
    [Range(0f, 1f)]
    [SerializeField] private float explosionVolume = 1f;
    [Tooltip("폭발 시 카메라 흔들림 강도")]
    [SerializeField] private float explosionShakeForce;

    private const string SpriteSheetPath = "Sprites/Traps/Traps";
    private static Sprite[] _spriteCache;

    private bool _isDead = false;
    private bool _isTriggered = false;

    public bool IsDead => _isDead;

    private void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        // 평소(점화 전)에는 fuse_01 스프라이트로 대기
        SetSprite("barrel_fuse_01");

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

        // 점화 가능 상태: fuse_02 -> fuse_03 -> fuse_04 순서로 도화선이 타들어가는 연출 (fuseTime 동안 균등 재생)
        string[] fuseFrames = { "barrel_fuse_02", "barrel_fuse_03", "barrel_fuse_04" };
        float perFrame = fuseTime / fuseFrames.Length;
        foreach (var frameName in fuseFrames)
        {
            SetSprite(frameName);
            yield return new WaitForSeconds(perFrame);
        }

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
            // 1.5초(fuseTime) 선딜레이 동안 원형 장판이 차오르도록 설정하고, 1.5초 후 0.2초 동안 타격 판정 적용
            hitbox.Init(dmgInfo, targetLayer, 0.2f, startDelay: fuseTime, isAlly: false);
        }
    }

    private IEnumerator PlayBoomAndDestroy()
    {
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

        // boom_01 ~ boom_07 프레임을 순서대로 재생
        for (int i = 1; i <= 7; i++)
        {
            SetSprite($"barrel_boom_{i:00}");
            yield return new WaitForSeconds(boomFrameDuration);
        }

        // 이펙트는 장판 프리팹 내부의 BaseHitBox가 타격 시간에 맞춰 자체 처리하므로
        // 폭탄 통 본체는 폭발 프레임 재생이 끝나면 파괴하면 됩니다.
        Destroy(gameObject);
    }

    private void SetSprite(string spriteName)
    {
        if (spriteRenderer == null) return;
        Sprite sprite = FindSprite(spriteName);
        if (sprite != null)
        {
            spriteRenderer.sprite = sprite;
        }
    }

    private static Sprite FindSprite(string spriteName)
    {
        if (_spriteCache == null)
        {
            _spriteCache = Resources.LoadAll<Sprite>(SpriteSheetPath);
        }

        for (int i = 0; i < _spriteCache.Length; i++)
        {
            if (_spriteCache[i] != null && _spriteCache[i].name == spriteName)
            {
                return _spriteCache[i];
            }
        }

        Debug.LogWarning($"[TrapBombBarrel] Sprite '{spriteName}' not found in {SpriteSheetPath}.");
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
