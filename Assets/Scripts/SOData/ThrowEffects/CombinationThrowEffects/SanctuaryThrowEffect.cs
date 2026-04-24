using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 방패병 + 사제 조합으로 생성되는 성역 효과입니다.
/// 범위 내 아군을 지속적으로 치유합니다.
/// </summary>
public class SanctuaryThrowEffect : MonoBehaviour
{
    private float _radius;
    private float _duration;
    private float _healAmount;
    private float _healInterval = 0.5f; // 0.5초마다 힐 적용
    private LayerMask _allyLayer;
    private float _timer;
    private float _nextHealTime;

    [Header("비주얼 설정")]
    [SerializeField] private SpriteRenderer fieldRenderer;

    public void Initialize(float radius, float duration, float healAmount, LayerMask allyLayer)
    {
        _radius = radius;
        _duration = duration;
        _healAmount = healAmount;
        _allyLayer = allyLayer;
        _timer = 0f;
        _nextHealTime = 0f;

        // 시각적 크기 조정
        transform.localScale = new Vector3(_radius * 2, _radius * 2, 1);
        
        if (fieldRenderer != null)
        {
            Color c = fieldRenderer.color;
            c.a = 0.4f;
            fieldRenderer.color = c;
        }
    }

    void Update()
    {
        _timer += Time.deltaTime;

        // 서서히 투명해지는 연출
        if (fieldRenderer != null)
        {
            Color c = fieldRenderer.color;
            c.a = Mathf.Lerp(0.4f, 0f, _timer / _duration);
            fieldRenderer.color = c;
        }

        if (_timer >= _duration)
        {
            Destroy(gameObject);
            return;
        }

        // 주기적인 힐링 처리
        if (_timer >= _nextHealTime)
        {
            ApplyHeal();
            _nextHealTime = _timer + _healInterval;
        }
    }

    private void ApplyHeal()
    {
        // 성역은 아군(Ally, Player 등)을 대상으로 함
        Collider2D[] allies = Physics2D.OverlapCircleAll(transform.position, _radius, _allyLayer);

        foreach (var col in allies)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                // 성역 유지 시간 동안 초당 healAmount만큼 회복되도록 인터벌(0.5s) 반영
                stat.Heal(_healAmount * _healInterval);
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
