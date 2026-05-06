using UnityEngine;
using System.Collections;

/// <summary>
/// 바닥에 드랍되어 플레이어나 아군이 먹으면 보호막을 부여하는 아이템입니다.
/// </summary>
public class ShieldCollectible : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float shieldAmount;
    [SerializeField] private float duration = 5.0f;
    [SerializeField] private float collectRadius = 1.0f;
    
    [Header("Visual Juice")]
    [SerializeField] private float bounceHeight = 0.5f;
    [SerializeField] private float bounceDuration = 0.6f;

    private bool _isCollected = false;

    public void Init(float amount, float shieldDuration)
    {
        shieldAmount = amount;
        duration = shieldDuration;
        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        // 생성 시 살짝 튀어 오르는 연출
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos;
        float elapsed = 0f;

        while (elapsed < bounceDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / bounceDuration;
            float yOffset = Mathf.Sin(t * Mathf.PI) * bounceHeight;
            transform.position = startPos + new Vector3(0, yOffset, 0);
            yield return null;
        }
        transform.position = startPos;
    }

    private void Update()
    {
        if (_isCollected) return;

        // 주변 아군이나 플레이어 감지 (물리 레이어 사용)
        int targetMask = LayerMask.GetMask("Player", "Army");
        Collider2D hit = Physics2D.OverlapCircle(transform.position, collectRadius, targetMask);

        if (hit != null)
        {
            OnCollected(hit.gameObject);
        }
    }

    private void OnCollected(GameObject collector)
    {
        _isCollected = true;

        // 엔티티의 Status를 찾아 보호막 부여
        CharacterStat stat = collector.GetComponentInChildren<CharacterStat>();
        if (stat != null)
        {
            stat.Status.AddShield(shieldAmount, duration);
            Debug.Log($"<color=cyan>[Collectible]</color> {collector.name}가 보호막 {shieldAmount:F1}을 획득했습니다!");
            
            // 시각 효과 재생 (레지스트리에서 이펙트를 가져와서 재생 가능)
            ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
            if (registry != null && registry.shieldAttachVFX != null)
            {
                Instantiate(registry.shieldAttachVFX, collector.transform.position, Quaternion.identity, collector.transform);
            }
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, collectRadius);
    }
}
