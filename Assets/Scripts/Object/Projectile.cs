using UnityEngine;

/// <summary>
/// 모든 투사체의 기반 클래스입니다.
/// 기본적인 직선 이동과 충격 판정을 처리합니다.
/// </summary>
public class Projectile : MonoBehaviour
{
    [Header("Base Projectile Settings")]
    [SerializeField] protected float speed = 15f;
    [SerializeField] protected float lifeTime = 3f;
    [SerializeField] protected bool rotateToDirection = true; // [추가] 날아가는 궤적 방향으로 오브젝트를 회전시킬지 여부 (구체형 등은 false)

    protected float _damage;
    protected LayerMask _targetLayer;
    protected GameObject _shooter;
    protected Vector2 _direction;
    protected System.Collections.Generic.HashSet<Collider2D> _ignoredColliders = new System.Collections.Generic.HashSet<Collider2D>();

    public virtual void Init(Vector2 targetPos, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetLayer = targetLayer;
        
        if (GameManager.Instance != null && GameManager.Instance.testMode_DisableAutoBattle)
        {
            if (shooter != null && shooter.layer == Layers.Enemy)
            {
                _targetLayer.value &= ~(1 << Layers.Army);
            }
        }

        _damage = damage;
        _shooter = shooter;
        speed = customSpeed;
        lifeTime = customLifeTime;

        // 방향 계산 및 초기 회전/반전 설정
        _direction = (targetPos - (Vector2)transform.position).normalized;
        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        }
        else
        {
            transform.localScale = new Vector3(Mathf.Sign(_direction.x) * Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        }

        // 일정 시간 후 자동 파괴
        Destroy(gameObject, lifeTime);

        // 초기 겹쳐 있는 콜라이더 오버랩 체크
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            ContactFilter2D filter = new ContactFilter2D();
            filter.useTriggers = true;

            Collider2D[] results = new Collider2D[20];
            int count = myCol.Overlap(filter, results);
            for (int i = 0; i < count; i++)
            {
                Collider2D otherCol = results[i];
                if (otherCol == null || otherCol.gameObject == shooter) continue;

                // 겹쳐 있는 것 중 플레이어(타겟 레이어)가 있다면 즉시 대미지를 입히고 파괴 처리
                if ((_targetLayer.value & (1 << otherCol.gameObject.layer)) != 0)
                {
                    CharacterStat targetStat = GetTargetStat(otherCol);
                    if (targetStat != null)
                    {
                        OnHitTarget(targetStat);
                        return;
                    }
                }

                // 겹쳐 있는 것이 벽/장애물이면 안전구역 진입 전까지 임시 무시
                if (((Layers.WallMask) & (1 << otherCol.gameObject.layer)) != 0)
                {
                    _ignoredColliders.Add(otherCol);
                }
            }
        }
    }

    protected virtual void Update()
    {
        Move();
    }

    protected virtual void Move()
    {
        // [수정] 패링 회전 여부(rotateToDirection)와 무관하게 항상 조준한 월드 방향(_direction)으로 비행하도록 월드 좌표 이동 적용
        transform.Translate(_direction * speed * Time.deltaTime, Space.World);
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (_ignoredColliders.Contains(other)) return;
        // 1. 벽이나 장애물에 부딪히면 파괴
        if (((Layers.WallMask) & (1 << other.gameObject.layer)) != 0)
        {
            OnHitObstacle(other);
            return;
        }

        // 2. 타겟 레이어와 충돌 체크
        if ((_targetLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null) damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) damageable = other.GetComponentInChildren<IDamageable>();

            if (damageable != null && !damageable.IsDead)
            {
                DamageInfo info = new DamageInfo(_damage, DamageType.Physical, _shooter, false, 1f, true);
                damageable.TakeDamage(info);
                Destroy(gameObject);
                return;
            }

            CharacterStat targetStat = GetTargetStat(other);
            
            if (targetStat != null)
            {
                OnHitTarget(targetStat);
            }
        }
    }

    protected virtual void OnTriggerExit2D(Collider2D other)
    {
        if (_ignoredColliders.Contains(other))
        {
            _ignoredColliders.Remove(other);
        }
    }

    protected virtual CharacterStat GetTargetStat(Collider2D other)
    {
        CharacterStat targetStat = other.GetComponent<CharacterStat>();
        if (targetStat == null)
        {
            int flyingLayer = Layers.FlyingObject;
            foreach (var s in other.GetComponentsInChildren<CharacterStat>())
            {
                if (s.gameObject.layer != flyingLayer)
                {
                    targetStat = s;
                    break;
                }
            }
        }
        return targetStat;
    }

    protected virtual void OnHitTarget(CharacterStat targetStat)
    {
        DamageInfo info = new DamageInfo(_damage, DamageType.Physical, _shooter, false, 1f, true);
        targetStat.Health.GetDamage(info);
        Destroy(gameObject);
    }

    protected virtual void OnHitObstacle(Collider2D other)
    {
        Destroy(gameObject);
    }

    public Vector2 Direction => _direction;
    public LayerMask TargetLayer => _targetLayer;

    public virtual void InitDeflected(Vector2 newDirection, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetLayer = targetLayer;
        _damage = damage;
        _shooter = shooter;
        speed = customSpeed;
        lifeTime = customLifeTime;

        _direction = newDirection.normalized;

        if (rotateToDirection)
        {
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        }
        else
        {
            transform.localScale = new Vector3(Mathf.Sign(_direction.x) * Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        }

        // 이전 무시 콜라이더 클리어
        _ignoredColliders.Clear();

        // 튕겨나가는 순간에 겹쳐 있는 것 체크 (적군)
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            ContactFilter2D filter = new ContactFilter2D();
            filter.useTriggers = true;

            Collider2D[] results = new Collider2D[20];
            int count = myCol.Overlap(filter, results);
            for (int i = 0; i < count; i++)
            {
                Collider2D otherCol = results[i];
                if (otherCol == null || otherCol.gameObject == shooter) continue;

                // 겹쳐 있는 것 중 적(새로운 타겟 레이어)이 있다면 즉시 대미지를 입히고 파괴 처리
                if ((_targetLayer.value & (1 << otherCol.gameObject.layer)) != 0)
                {
                    CharacterStat targetStat = GetTargetStat(otherCol);
                    if (targetStat != null)
                    {
                        OnHitTarget(targetStat);
                        return;
                    }
                }

                // 겹쳐 있는 것이 벽/장애물이면 임시 무시
                if (((Layers.WallMask) & (1 << otherCol.gameObject.layer)) != 0)
                {
                    _ignoredColliders.Add(otherCol);
                }
            }
        }

        // 일정 시간 후 자동 파괴
        Destroy(gameObject, lifeTime);
    }

    public virtual Projectile Deflect(GameObject newShooter, LayerMask newTargetLayer, Vector2 newDirection)
    {
        GameObject go = Instantiate(gameObject, transform.position, Quaternion.identity);
        Projectile newProj = go.GetComponent<Projectile>();
        if (newProj != null)
        {
            go.transform.localScale = transform.localScale;
            // 복제본에 InitDeflected 호출 (새로운 방향으로 날아감, 데미지 1.5배)
            newProj.InitDeflected(newDirection, _damage * 1.5f, newTargetLayer, newShooter, speed, lifeTime);
        }

        // 원본 투사체 파괴
        Destroy(gameObject);

        return newProj;
    }
}
