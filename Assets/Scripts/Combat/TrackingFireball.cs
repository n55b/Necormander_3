using UnityEngine;

/// <summary>
/// 대상을 추적하는 화염구 투사체입니다.
/// Projectile을 상속받아 충돌 및 기본 설정을 공유합니다.
/// </summary>
public class TrackingFireball : Projectile
{
    [Header("유도 선회")]
    [SerializeField, Min(0f), Tooltip("초당 최대 회전각. 낮출수록 완만하게 선회한다.")]
    private float turnSpeedDegrees = 60f;
    [SerializeField, Range(0f, 90f), Tooltip("진행 방향에서 이 각도 이상 벗어난 대상은 놓친다. 이후 재추적 없이 직진한다.")]
    private float trackingHalfAngle = 75f;

    protected Transform _targetTransform;
    public override DamageInfo GuardInfo => new DamageInfo(_damage, DamageType.Magic, _shooter,
        category: _isDeflected ? DamageCategory.Parry : DamageCategory.None, isRanged: true, hitFrom: HitFromPoint);

    public void Init(Transform target, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = target;
        // 부모의 Init을 활용하되, targetPos는 현재 타겟 위치로 초기화
        base.Init(target.position, damage, targetLayer, shooter, customSpeed, customLifeTime);
        this.transform.localRotation = Quaternion.identity; // 초기 회전값을 리셋
    }

    public void InitLinear(Vector2 targetPos, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = null;
        base.Init(targetPos, damage, targetLayer, shooter, customSpeed, customLifeTime);
    }

    protected override void Move()
    {
        Steer(Time.deltaTime);
        base.Move(); // 일정 속도 유지. 대상을 놓쳐도 멈추거나 U턴하지 않는다.
    }

    private void Steer(float deltaTime)
    {
        if (_targetTransform == null) return; // 직선 발사/패리도 이 경로
        Vector2 toTarget = (Vector2)_targetTransform.position - (Vector2)transform.position;
        if (toTarget.sqrMagnitude < 0.0001f ||
            Vector2.Angle(_direction, toTarget) > Mathf.Clamp(trackingHalfAngle, 0f, 90f))
        {
            _targetTransform = null; // 한 번 옆으로 흘려보냈으면 재포착하지 않는다.
            return;
        }

        float angle = Mathf.MoveTowardsAngle(
            Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg,
            Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg,
            Mathf.Max(0f, turnSpeedDegrees) * Mathf.Max(0f, deltaTime));
        _direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        if (rotateToDirection) transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    public override void InitDeflected(Vector2 newDirection, float damage, LayerMask targetLayer, GameObject shooter, float customSpeed, float customLifeTime)
    {
        _targetTransform = null; // 패리 시 유도 대상 해제 (직선 비행)
        base.InitDeflected(newDirection, damage, targetLayer, shooter, customSpeed, customLifeTime);
    }
}
