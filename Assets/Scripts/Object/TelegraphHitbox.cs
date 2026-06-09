using UnityEngine;
using System.Collections.Generic;

public class TelegraphHitbox : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private SpriteRenderer fillSprite; // 서서히 차오르는 이미지
    // BoxCollider2D는 필요하지 않으므로 제거합니다.

    private float _duration;
    private float _timer;
    private DamageInfo _damageInfo;
    private LayerMask _targetLayer;
    private bool _isExecuting = false;
    private Vector2 _originalSize;
    private float _initialFillScaleX;

    public void Init(float duration, DamageInfo damageInfo, LayerMask targetLayer, Vector2 size)
    {
        _duration = duration;
        _damageInfo = damageInfo;
        _targetLayer = targetLayer;
        
        _originalSize = size;
        
        // [수정] 루트 오브젝트의 스케일을 타격 범위(size)로 강제하여, 하위의 경계선(Border)과 차오르는 영역(Fill)이 모두 일치하도록 만듭니다.
        transform.localScale = new Vector3(size.x, size.y, 1f);

        // 시작할 때 시각적 크기를 0으로 초기화
        if (fillSprite != null)
        {
            _initialFillScaleX = fillSprite.transform.localScale.x;
            fillSprite.transform.localScale = new Vector3(0f, fillSprite.transform.localScale.y, fillSprite.transform.localScale.z);
        }

        _timer = 0f;
        _isExecuting = true;
    }

    private void Update()
    {
        if (!_isExecuting) return;

        _timer += Time.deltaTime;
        float progress = Mathf.Clamp01(_timer / _duration);

        // 시각적 게이지 채우기 (로컬 스케일을 원래 비율에 맞춰 증가시킴)
        if (fillSprite != null)
        {
            fillSprite.transform.localScale = new Vector3(_initialFillScaleX * progress, fillSprite.transform.localScale.y, fillSprite.transform.localScale.z);
        }

        if (_timer >= _duration)
        {
            ExecuteDamage();
        }
    }

    private void ExecuteDamage()
    {
        _isExecuting = false;

        // 물리 중심점은 현재 오브젝트의 위치(transform.position)입니다.
        Collider2D[] hitColliders = Physics2D.OverlapBoxAll(
            transform.position,
            _originalSize,
            transform.eulerAngles.z,
            _targetLayer
        );

        Debug.Log($"<color=red>[TelegraphHitbox]</color> {gameObject.name} 타격 판정! 중심: {transform.position}, 크기: {_originalSize}, 각도: {transform.eulerAngles.z}, 감지된 타겟 수: {hitColliders.Length}");

            // 중복 타격 방지
            HashSet<CharacterHealth> hitTargets = new HashSet<CharacterHealth>();

            foreach (var col in hitColliders)
            {
                // 부모, 자기자신, 자식 모두에서 CharacterHealth를 찾습니다.
                var health = col.GetComponent<CharacterHealth>();
                if (health == null) health = col.GetComponentInParent<CharacterHealth>();
                if (health == null) health = col.GetComponentInChildren<CharacterHealth>();

                if (health != null && !health.IsDead && !hitTargets.Contains(health))
                {
                    Debug.Log($"<color=red>[TelegraphHitbox]</color> 적중! 타겟: {health.gameObject.name}, 데미지: {_damageInfo.amount}");
                    hitTargets.Add(health);
                    health.GetDamage(_damageInfo);
                }
            }

        // 타격 완료 후 오브젝트 파괴
        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        
        Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.matrix = rotationMatrix;
        Gizmos.DrawWireCube(Vector3.zero, _originalSize);
    }
}
