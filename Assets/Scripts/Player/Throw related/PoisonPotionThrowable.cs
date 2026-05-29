using UnityEngine;
using System.Collections.Generic;

public class PoisonPotionThrowable : MonoBehaviour, IThrowable
{
    [Header("Potion Settings")]
    public float explosionRadius = 2.5f;
    public float explosionDamage = 10f; // 에디터 수정 가능 (약한 피해)
    public float poisonStacks = 3f;     // 에디터 수정 가능 (중독 중첩)
    
    private bool _isHeld = false;
    private Rigidbody2D _rb;
    private Collider2D _col;

    // 전역 포션 개수 제한을 위한 정적 리스트
    public static List<PoisonPotionThrowable> ActivePotions = new List<PoisonPotionThrowable>();
    public const int MAX_POTIONS = 5;

    // --- IThrowable Implementation ---
    public CommandData MinionType => CommandData.None; // 미니언이 아님
    public MinionDataSO MinionData => null;
    public float MaxSpeed => 25f;
    public float MinSpeed => 15f;
    public float FullChargeSpeed => 35f;
    public float JumpHeight => 2f;
    public float StraightHeight => 0.5f;

    public void OnPickedUp()
    {
        _isHeld = true;
        if (_col != null) _col.enabled = false;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _isHeld = false;
        transform.SetParent(null);
        if (_col != null) _col.enabled = true;
        
        // ThrowController 등에서 물리/곡선 궤적 연산을 해주므로 여기서는 부모 해제 등만 처리
        // 만약 직접 힘을 준다면 여기서 처리 가능
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Dynamic;
            // ThrowController 쪽에서 물리 연산을 해주거나 여기서 직진 처리
            float force = Mathf.Lerp(15f, 30f, chargeRatio);
            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            _rb.AddForce(direction * force, ForceMode2D.Impulse);
        }
    }

    public void OnLanded()
    {
        // 땅에 떨어졌을 때 이펙트 발생 등
    }

    public void PrepareForClusterThrow(float chargeRatio, bool isDirect)
    {
        // 구현 필요시 작성
    }

    public void SetImpacted(bool value)
    {
    }

    public void ApplyThrowCost()
    {
    }
    // ---------------------------------

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<Collider2D>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
        if (_col == null) 
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.3f;
            circle.isTrigger = true;
            _col = circle;
        }
        
        _rb.gravityScale = 0f;
        _rb.mass = 0.5f;
        _rb.linearDamping = 5f;
        _rb.angularDamping = 5f;
    }

    private void Start()
    {
        ActivePotions.Add(this);
        ManagePotionLimit();
    }

    private void OnDestroy()
    {
        ActivePotions.Remove(this);
    }

    private void ManagePotionLimit()
    {
        if (ActivePotions.Count > MAX_POTIONS)
        {
            // 가장 오래된 포션을 찾아서 삭제 (held 상태가 아닌 것 우선)
            for (int i = 0; i < ActivePotions.Count; i++)
            {
                if (ActivePotions[i] != this && !ActivePotions[i]._isHeld)
                {
                    Destroy(ActivePotions[i].gameObject);
                    return;
                }
            }
            // 전부 held 라면 그냥 제일 오래된 것 삭제
            Destroy(ActivePotions[0].gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 몬스터나 벽에 부딪히면 폭발 (던져졌을 때만)
        if (!_isHeld)
        {
            // 던져진 후 0.1초 정도 뒤부터 활성화하거나, 속도로 판별 가능
            if (_rb != null && _rb.linearVelocity.magnitude > 2f)
            {
                Explode();
            }
        }
    }

    private void Explode()
    {
        LayerMask enemyLayer = LayerMask.GetMask("Enemy");
        Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, explosionRadius, enemyLayer);
        
        foreach (var col in colls)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health != null && !health.IsDead)
            {
                health.GetDamage(new DamageInfo(explosionDamage, DamageType.Ice, null)); // 약한 고정피해 또는 마법피해(Ice로 대체 가능)
                
                var status = col.GetComponentInChildren<CharacterStatus>();
                if (status != null)
                {
                    status.AddDebuffStack(DebuffStackType.Poison, poisonStacks);
                }
            }
        }

        // 폭발 이펙트 스폰 (옵션)
        ThrowEffectRegistrySO registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        if (registry != null && registry.bloodPopVFX != null)
        {
            GameObject vfx = Instantiate(registry.bloodPopVFX, transform.position, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (explosionRadius);
            Destroy(vfx, 1.0f);
        }

        Destroy(gameObject);
    }
}
