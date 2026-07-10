using UnityEngine;

/// <summary>
/// 던져질 수 있는 유닛의 기본 구현입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(ArcMovement))]
public class ThrowableUnit : MonoBehaviour, IThrowable
{
    // [추가] 인터페이스 구현
    public virtual CommandData MinionType => CommandData.None;
    public virtual MinionDataSO MinionData => null;

    // [추가] 물리 수치 노출
    public float MaxSpeed => maxSpeed;
    public float MinSpeed => minSpeed;
    public float FullChargeSpeed => fullChargeSpeed;
    public float JumpHeight => jumpHeight;
    public float StraightHeight => straightHeight;

    private float jumpHeight = 1.5f;
    private float straightHeight = 0.1f;
    private float minSpeed = 5f;
    private float maxSpeed = 20f;
    private float fullChargeSpeed = 30f;

    private Rigidbody2D _rb;
    private ArcMovement _arcMovement;
    private Collider2D _collider;
    private float _originalDamping;
    private LayerMask _hitLayers;
    private float _throwStartTime;
    
    // [추가] 레이어 및 충돌 상태 관리
    private int _originalLayer;
    protected bool _isImpacted;
    private Transform _originalParent; // [추가] 던지기 전 원래 부모 오브젝트

    protected virtual void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _arcMovement = GetComponent<ArcMovement>();
        _collider = GetComponent<Collider2D>();
        _rb.freezeRotation = true;
        _originalDamping = _rb.linearDamping;
        _originalLayer = gameObject.layer;

        _hitLayers = Layers.EnemyWallObstacle;
        if (_hitLayers == 0)
        {
            _hitLayers = ~(Layers.PlayerMask | (1 << 2)); 
        }
    }

    public virtual void OnPickedUp()
    {
        // 이미 클러스터에 잡힌 상태에서 재호출될 경우, 부모를 덮어씌우지 않도록 방어 코드 추가
        if (transform.parent == null || transform.parent.GetComponent<ThrowCluster>() == null)
        {
            _originalParent = transform.parent; // 원래 부모 기억
        }
        
        _rb.simulated = false;
        _collider.enabled = false;
        _isImpacted = false; // [추가] 잡을 때 상태 초기화

        // 잡혔을 때부터 FlyingObject 레이어로 변경하여 발 밑 충돌 무시 및 드랍 시 복구 보장
        gameObject.layer = Layers.FlyingObject;
    }

    public virtual void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        transform.rotation = Quaternion.identity;

        // [수정] 클러스터에 포함되어 던져지는 경우, 이동과 물리는 클러스터가 전담하므로 개별 물리 연산을 생략합니다.
        // 이를 생략하지 않으면 상자 내부의 자체 ArcMovement가 실행되어 착지 시 0,0,0으로 위치를 강제 텔레포트시키는 심각한 버그가 발생합니다.
        if (transform.parent != null && transform.parent.GetComponent<ThrowCluster>() != null)
        {
            return;
        }

        _rb.simulated = true;
        _collider.enabled = true;
        _collider.isTrigger = true;

        _originalDamping = _rb.linearDamping;
        _rb.linearDamping = 0f;

        Vector2 startPos = _rb.position;
        Vector2 diff = targetPosition - startPos;
        float distance = diff.magnitude;
        Vector2 direction = diff.normalized;

        float speed;
        float duration;
        float maxHeight;

        if (chargeRatio >= 1.0f)
        {
            speed = fullChargeSpeed;
            duration = 2.0f; 
            maxHeight = straightHeight;
        }
        else
        {
            speed = Mathf.Lerp(minSpeed, maxSpeed, chargeRatio);
            duration = distance / speed;

            float targetHeight = Mathf.Lerp(jumpHeight, straightHeight, chargeRatio);
            maxHeight = Mathf.Min(targetHeight, distance * 0.5f); 
        }

        _rb.linearVelocity = direction * speed;
        _arcMovement.StartArc(duration, maxHeight);
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time - _throwStartTime < 0.05f) return;

        if (_arcMovement.IsFlying && (_hitLayers.value & (1 << other.gameObject.layer)) != 0)
        {
            _arcMovement.StopArc();
        }
    }

    public virtual void OnLanded()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.linearDamping = _originalDamping;
        
        // [수정] simulated를 켜면 물리 엔진이 과거 위치(0,0)로 Transform을 덮어씌우는 현상 완벽 방어
        Vector3 currentWorldPos = transform.position;
        _rb.simulated = true;
        transform.position = currentWorldPos;
        _rb.position = currentWorldPos;
        
        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = false;
        }
        
        // 착지 시 레이어 및 부모 복구
        gameObject.layer = _originalLayer;
        
        if (_originalParent != null)
        {
            transform.SetParent(_originalParent, true); // true: 현재 월드 좌표 유지
        }
        
        Debug.Log($"{gameObject.name} landed!");
    }

    public virtual void PrepareForClusterThrow(float chargeRatio, bool isDirect) { }
    public virtual void SetImpacted(bool value) { _isImpacted = value; }
    public virtual void ApplyThrowCost() { } 
}
