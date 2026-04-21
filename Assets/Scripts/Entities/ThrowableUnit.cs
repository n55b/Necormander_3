using UnityEngine;
using Necromancer.Interfaces;
using Necromancer.Physics;

/// <summary>
/// 던져질 수 있는 유닛의 기본 구현입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(ArcMovement))]
public class ThrowableUnit : MonoBehaviour, IThrowable
{
    private float jumpHeight = 1.5f;
    private float straightHeight = 0.1f;
    private float minSpeed = 2f;
    private float maxSpeed = 18f;
    private float fullChargeSpeed = 25f;

    private Rigidbody2D _rb;
    private ArcMovement _arcMovement;
    private Collider2D _collider;
    private float _originalDamping;
    private LayerMask _hitLayers;
    private float _throwStartTime;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _arcMovement = GetComponent<ArcMovement>();
        _collider = GetComponent<Collider2D>();
        _rb.freezeRotation = true;
        _originalDamping = _rb.linearDamping;

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

        // [중요] 실제 물리 속도 할당
        _rb.linearVelocity = direction * speed;

        _arcMovement.StartArc(duration, maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time - _throwStartTime < 0.05f) return;

        if (_arcMovement.IsFlying && (_hitLayers.value & (1 << other.gameObject.layer)) != 0)
        {
            Debug.Log($"<color=red>[Throw Hit]</color> {gameObject.name} hit <b>{other.name}</b> (Layer: {LayerMask.LayerToName(other.gameObject.layer)})");
            _arcMovement.StopArc();
        }
    }

    public virtual void OnLanded()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.linearDamping = _originalDamping;
        _collider.isTrigger = false;
        
        Debug.Log($"{gameObject.name} landed!");
    }
}
