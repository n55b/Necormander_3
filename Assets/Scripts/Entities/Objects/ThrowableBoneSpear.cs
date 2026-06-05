using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 본 마스터 레이드 전용 투척물인 뼈 창(Bone Spear)입니다.
/// 플레이어가 클릭하여 주운 뒤 던질 수 있으며, 마우스 주변이나 적에게 피해/기믹을 가합니다.
/// </summary>
public class ThrowableBoneSpear : MonoBehaviour, IThrowable
{
    private bool _isHeld = false;
    private bool _hasImpacted = false;

    // IThrowable 구현
    public CommandData MinionType => CommandData.None; // 특별한 타입 없음
    public MinionDataSO MinionData => null; // 미니언 데이터 없음
    public float MaxSpeed => 25f;
    public float MinSpeed => 15f;
    public float FullChargeSpeed => 35f;
    public float JumpHeight => 2f;
    public float StraightHeight => 0.5f;

    private Collider2D _collider;
    private Rigidbody2D _rb;
    private TrailRenderer _trail;
    private Vector3 _throwOrigin;

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _rb = GetComponent<Rigidbody2D>();
        _trail = GetComponentInChildren<TrailRenderer>();

        if (_trail != null) _trail.emitting = false;
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.simulated = true;
        }
    }

    public void OnPickedUp()
    {
        _isHeld = true;
        _hasImpacted = false;

        if (_collider != null) _collider.enabled = false;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }
        if (_trail != null) _trail.emitting = false;

        // 투척 큐로 들어감에 따라 스케일 초기화 (UI 쪽에서 관리될 것)
        transform.localScale = Vector3.one; 
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _isHeld = false;
        _throwOrigin = transform.position; // 던진 순간의 위치 저장
        
        // 투척 이펙트 활성화
        if (_trail != null) _trail.emitting = true;

        if (_collider != null) _collider.enabled = true; // 투척 중 충돌 판정 다시 켬

        // 실제 던지는 로직은 PlayerController의 ThrowStrategy에서 처리하지만, 
        // 투척 이펙트, 사운드 등을 여기서 추가 재생 가능
    }

    public void OnLanded()
    {
        if (_trail != null) _trail.emitting = false;
        if (_collider != null) _collider.enabled = true;
        
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
        }
        
        // 튕겨져 나온 후 바닥에 완전히 안착함. 다시 주울 수 있는 상태.
        gameObject.layer = LayerMask.NameToLayer("Default"); // 마우스 클릭 가능 레이어
    }

    public void PrepareForClusterThrow(float chargeRatio, bool isDirect)
    {
        // 다중 투척 시 추가 준비 로직 필요하면 구현
    }

    public void SetImpacted(bool value)
    {
        _hasImpacted = value;
    }

    public void ApplyThrowCost()
    {
        // 뼈 창은 던질 때 스태미나를 소모하지 않거나 기본 소모량만 쓰도록 둠 (ThrowController에 위임)
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_isHeld) return;
        
        // 던져진 상태에서 충돌 발생
        if (collision.CompareTag("Enemy") || collision.CompareTag("Boss"))
        {
            // 보스 충돌 처리 (BoneMasterController에서 OnSpearHit를 받아서 처리할 수도 있고 여기서 보낼 수도 있음)
            BoneMasterController boss = collision.GetComponentInParent<BoneMasterController>();
            if (boss != null)
            {
                boss.OnSpearHit(_throwOrigin); // [수정] 발사 위치 전달
                
                // 적중 시 보스 근처로 튕겨 떨어짐
                BounceOff(collision.transform.position);
            }
            else
            {
                // 일반 적 적중 시 관통하거나 피해를 주고 멈춤
                BounceOff(collision.transform.position);
            }
        }
        else if (collision.CompareTag("Wall") || collision.gameObject.layer == LayerMask.NameToLayer("Obstacle"))
        {
            // 벽에 꽂힘
            StickToWall();
        }
    }

    private void BounceOff(Vector2 hitPos)
    {
        // 보스/적에게 맞은 뒤 바닥으로 툭 떨어지는 물리 연출
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.gravityScale = 0f;
            _rb.linearDamping = 5f;
            
            Vector2 bounceDir = ((Vector2)transform.position - hitPos).normalized;
            bounceDir += new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
            _rb.AddForce(bounceDir.normalized * 10f, ForceMode2D.Impulse);
        }

        Invoke(nameof(OnLanded), 0.5f); // 0.5초 뒤 완전히 안착
    }

    private void StickToWall()
    {
        // 벽에 맞고 정지
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }
        OnLanded();
    }
}
