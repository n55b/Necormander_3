using UnityEngine;
using System.Collections;

/// <summary>
/// 피격 시 1.5초 동안 점멸한 후 폭발하여 주변 영역에 큰 데미지를 주는 폭탄 통 함정입니다.
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

    private bool _isDead = false;
    private bool _isTriggered = false;

    public bool IsDead => _isDead;

    private void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (targetLayer == 0)
        {
            targetLayer = LayerMask.GetMask("Player", "Enemy", "Ally", "Army", "Player_Dash");
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

        // 타격을 받는 순간 바닥에 시각 장판(Telegraph) 생성 및 1.5초 예약 작동
        SpawnExplosionTelegraph();

        float timer = 0f;
        Color originalColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        float blinkInterval = 0.15f;
        bool isBlink = false;

        // 점멸 연출 (1.5초 동안 깜빡임)
        while (timer < fuseTime)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = isBlink ? Color.red : originalColor;
            }
            isBlink = !isBlink;

            yield return new WaitForSeconds(blinkInterval);
            timer += blinkInterval;
            
            // 시간이 지날수록 깜빡임 주기가 빨라짐
            blinkInterval = Mathf.Max(0.05f, blinkInterval * 0.85f);
        }

        // 최종 폭발 연출 및 파괴
        Explode();
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
            DamageInfo dmgInfo = new DamageInfo(explosionDamage, DamageType.Physical, gameObject);
            // 1.5초(fuseTime) 선딜레이 동안 원형 장판이 차오르도록 설정하고, 1.5초 후 0.2초 동안 타격 판정 적용
            hitbox.Init(dmgInfo, targetLayer, 0.2f, startDelay: fuseTime, isAlly: false);
        }
    }

    private void Explode()
    {
        // 사운드 재생
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.HITSoundPlay(false);
        }

        // 이펙트는 장판 프리팹 내부의 BaseHitBox가 타격 시간에 맞춰 자체 처리하므로
        // 폭탄 통 본체는 사운드를 내고 즉시 파괴하면 됩니다.
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
