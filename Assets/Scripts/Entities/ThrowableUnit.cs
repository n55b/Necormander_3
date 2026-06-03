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

    protected virtual void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _arcMovement = GetComponent<ArcMovement>();
        _collider = GetComponent<Collider2D>();
        _rb.freezeRotation = true;
        _originalDamping = _rb.linearDamping;
        _originalLayer = gameObject.layer;

        _hitLayers = LayerMask.GetMask("Enemy", "Wall", "Obstacle");
        if (_hitLayers == 0)
        {
            _hitLayers = ~(LayerMask.GetMask("Player") | (1 << 2)); 
        }
    }

    public virtual void OnPickedUp()
    {
        _rb.simulated = false;
        _collider.enabled = false;
        _isImpacted = false; // [추가] 잡을 때 상태 초기화

        // 잡혔을 때부터 FlyingObject 레이어로 변경하여 발 밑 충돌 무시 및 드랍 시 복구 보장
        gameObject.layer = LayerMask.NameToLayer("FlyingObject");
    }

    public virtual void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        transform.rotation = Quaternion.identity;

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
        _collider.isTrigger = false;
        
        // 착지 시 레이어 복구
        gameObject.layer = _originalLayer;
        
        Debug.Log($"{gameObject.name} landed!");
    }

    public virtual void PrepareForClusterThrow(float chargeRatio, bool isDirect) { }
    public virtual void SetImpacted(bool value) { _isImpacted = value; }
    public virtual void ApplyThrowCost() { } 
}
