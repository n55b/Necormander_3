using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 물리적 충돌(Collision)을 사용하여 적군만 통과할 수 없는 벽을 형성합니다.
/// 아군과 플레이어는 레이어 설정을 통해 통과하도록 설정해야 합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ShieldWallThrowEffect : MonoBehaviour
{
    private float _speed;
    private float _maxDistance;
    private float _damage;
    private float _knockbackForce;
    private Vector2 _direction;
    private Vector2 _startPos;
    private List<GameObject> _hitEnemies = new List<GameObject>();
    private LayerMask _enemyLayer;
    private Rigidbody2D _rb;

    [Header("배치 설정")]
    [SerializeField] private float unitSpacing = 0.5f; 

    public void Initialize(Vector2 direction, float speed, float distance, float damage, float knockback, int widthCount, GameObject unitPrefab, LayerMask enemyLayer)
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic; // 다른 힘에 밀리지 않고 정해진 속도로만 이동
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        _direction = direction.normalized;
        _speed = speed;
        _maxDistance = distance;
        _damage = damage;
        _knockbackForce = knockback;
        _startPos = transform.position;
        _enemyLayer = enemyLayer;

        float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        float startOffset = -(widthCount - 1) * unitSpacing * 0.5f;

        for (int i = 0; i < widthCount; i++)
        {
            float yOffset = startOffset + (i * unitSpacing);
            GameObject unit = Instantiate(unitPrefab, transform);
            
            unit.transform.localPosition = new Vector3(0, yOffset, 0);
            unit.transform.localRotation = Quaternion.identity;
            
            // 각 조각에 Collision 핸들러 부착
            var colHandler = unit.AddComponent<ShieldWallUnitCollision>();
            colHandler.Setup(this);

            // 유닛 조각의 레이어를 부모와 동일하게 설정 (레이어 기반 충돌 제어용)
            unit.layer = gameObject.layer;

            // 조각의 콜라이더가 isTrigger가 아님을 확실히 함
            if (unit.TryGetComponent<Collider2D>(out var col))
            {
                col.isTrigger = false;
            }
        }

        // 물리 속도 부여
        _rb.linearVelocity = _direction * _speed;
    }

    void Update()
    {
        if (Vector2.Distance(_startPos, transform.position) >= _maxDistance)
        {
            Destroy(gameObject);
        }
    }

    public void OnUnitCollisionEnter(Collision2D collision)
    {
        GameObject other = collision.gameObject;

        // 레이어 체크 및 중복 데미지 방지
        if (((1 << other.layer) & _enemyLayer) != 0 && !_hitEnemies.Contains(other))
        {
            _hitEnemies.Add(other);

            if (other.TryGetComponent<CharacterStat>(out var enemyStat))
            {
                DamageInfo info = new DamageInfo(_damage, DamageType.Physical, gameObject);
                enemyStat.GetDamage(info);
            }

            // 부딪힐 때 살짝 넉백을 주어 벽에 박히거나 튕기게 함
            if (other.TryGetComponent<Rigidbody2D>(out var enemyRb))
            {
                enemyRb.AddForce(_direction * _knockbackForce, ForceMode2D.Impulse);
            }
        }
    }
}

public class ShieldWallUnitCollision : MonoBehaviour
{
    private ShieldWallThrowEffect _parentEffect;

    public void Setup(ShieldWallThrowEffect parent)
    {
        _parentEffect = parent;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (_parentEffect != null)
        {
            _parentEffect.OnUnitCollisionEnter(collision);
        }
    }
}
