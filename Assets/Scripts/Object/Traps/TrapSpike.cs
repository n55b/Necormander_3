using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 압력 발판을 밟으면 1초 뒤 가시가 발생하여 5초 동안 데미지를 주는 함정입니다.
/// </summary>
public class TrapSpike : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float activationDelay = 1f;
    [SerializeField] private float duration = 5f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private Vector2 damageAreaSize = new Vector2(1f, 1f);
    [SerializeField] private LayerMask targetLayer;

    [Header("Visual References")]
    [SerializeField] private GameObject spikeVisual; // 솟아오르는 가시 오브젝트

    private bool _isTriggered = false;
    private bool _isActive = false;
    private HashSet<CharacterHealth> _hitTargets = new HashSet<CharacterHealth>();

    private void Start()
    {
        if (spikeVisual != null)
        {
            spikeVisual.SetActive(false);
        }
        
        // 기본적으로 targetLayer가 설정되어 있지 않다면 Player, Enemy, Ally 등을 포함
        if (targetLayer == 0)
        {
            targetLayer = LayerMask.GetMask("Player", "Enemy", "Ally", "Army", "Player_Dash");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 타겟 레이어 검사
        if (((1 << other.gameObject.layer) & targetLayer) == 0) return;

        // [추가] 방이 클리어되었다면 작동 안함
        RoomInstance room = GetComponentInParent<RoomInstance>();
        if (room != null && room.isCleared) return;

        // 발판을 밟으면 1초 후 가시 발동 코루틴 실행
        if (!_isTriggered)
        {
            StartCoroutine(TriggerRoutine());
        }

        // 가시가 활성화된 상태에서 새로 진입하는 캐릭터에게 데미지
        if (_isActive)
        {
            ApplyDamageToCollider(other);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (_isActive)
        {
            ApplyDamageToCollider(other);
        }
    }

    private IEnumerator TriggerRoutine()
    {
        _isTriggered = true;

        // 1초 대기
        yield return new WaitForSeconds(activationDelay);

        // 가시 활성화
        _isActive = true;
        if (spikeVisual != null)
        {
            spikeVisual.SetActive(true);
        }

        // 솟아오르는 즉시 영역 내 모든 캐릭터에게 1회 데미지 가함
        Collider2D[] hitColliders = Physics2D.OverlapBoxAll(transform.position, damageAreaSize, 0f, targetLayer);
        foreach (var col in hitColliders)
        {
            ApplyDamageToCollider(col);
        }

        // 5초 유지
        yield return new WaitForSeconds(duration);

        // 가시 비활성화
        _isActive = false;
        if (spikeVisual != null)
        {
            spikeVisual.SetActive(false);
        }

        // 상태 초기화
        _hitTargets.Clear();
        _isTriggered = false;
    }

    private void ApplyDamageToCollider(Collider2D col)
    {
        var health = col.GetComponent<CharacterHealth>();
        if (health == null) health = col.GetComponentInParent<CharacterHealth>();
        if (health == null) health = col.GetComponentInChildren<CharacterHealth>();

        if (health != null && !health.IsDead)
        {
            if (!_hitTargets.Contains(health))
            {
                _hitTargets.Add(health);
                DamageInfo info = new DamageInfo(damage, DamageType.Physical, gameObject);
                health.GetDamage(info);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, damageAreaSize);
    }
}
