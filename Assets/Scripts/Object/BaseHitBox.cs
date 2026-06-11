using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 모든 형태(원형, 사각형 등)의 장판/히트박스 프리팹에 공용으로 붙일 수 있는 스크립트입니다.
/// Trigger Collider2D를 이용해 들어온 적에게 데미지를 줍니다.
/// </summary>
public class BaseHitBox : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("장판이 유지되는 시간 (0이면 무한유지)")]
    public float duration = 0.5f; 
    public bool destroyOnFinish = true; 
    
    [Tooltip("지속 데미지 여부 (체크 시 주기적으로 데미지, 해제 시 1회 타격)")]
    public bool isContinuousDamage = false;
    public float damageTickRate = 0.5f;

    [Header("Visuals (Optional)")]
    [Tooltip("장판이 찰 때 크기가 점점 커질 내부 이미지(Transform)")]
    public Transform fillingTransform;
    [Tooltip("가득 찼을 때의 최대 로컬 스케일")]
    public Vector3 maxFillScale = Vector3.one;

    private DamageInfo _damageInfo;
    private LayerMask _targetLayer;
    private bool _isInitialized = false;
    private float _tickTimer;

    // 1회 타격 시 중복 타격 방지
    private HashSet<CharacterHealth> _hitTargets = new HashSet<CharacterHealth>();

    public void Init(DamageInfo damageInfo, LayerMask targetLayer, float overrideDuration = -1f, float startDelay = 0f, bool isAlly = false)
    {
        _damageInfo = damageInfo;
        _targetLayer = targetLayer;
        
        if (overrideDuration > 0)
            duration = overrideDuration;

        // [추가] 아군/적군에 따른 장판 색상 자동 변경 (파란색 / 빨간색)
        if (fillingTransform != null)
        {
            var sr = fillingTransform.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                // 투명도(Alpha)를 유지하면서 색상만 변경
                float alpha = sr.color.a;
                sr.color = isAlly ? new Color(0.2f, 0.6f, 1f, alpha) : new Color(1f, 0.2f, 0.2f, alpha);
            }
        }

        // 선딜레이가 끝날 때까지 콜라이더 비활성화
        var col = GetComponent<Collider2D>();
        if (col != null && startDelay > 0f)
        {
            col.enabled = false;
        }

        if (fillingTransform != null)
        {
            fillingTransform.localScale = Vector3.zero; // 시각적 크기 0으로 초기화
        }

        if (startDelay > 0f)
        {
            StartCoroutine(DelayRoutine(startDelay, col));
        }
        else
        {
            if (fillingTransform != null) fillingTransform.localScale = maxFillScale;
            ActivateHitBox();
        }

        if (destroyOnFinish && duration > 0)
        {
            // startDelay가 있으므로 파괴 시간도 늦춰야 함
            Destroy(gameObject, duration + startDelay);
        }
    }

    private System.Collections.IEnumerator DelayRoutine(float delay, Collider2D col)
    {
        float timer = 0f;
        while (timer < delay)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / delay);
            
            if (fillingTransform != null)
            {
                fillingTransform.localScale = Vector3.Lerp(Vector3.zero, maxFillScale, progress);
            }
            
            yield return null;
        }

        ForceActivate();
    }

    /// <summary>
    /// 외부에서 강제로 선딜레이를 끝내고 즉시 데미지 판정을 시작할 때 호출합니다. (애니메이션 이벤트 연동용)
    /// </summary>
    public void ForceActivate()
    {
        if (_isInitialized) return; // 이미 활성화되었다면 무시

        StopAllCoroutines(); // 진행 중이던 DelayRoutine 취소

        if (fillingTransform != null) fillingTransform.localScale = maxFillScale;
        
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;
        
        ActivateHitBox();

        // [수정] 애니메이션 타격 프레임에 강제 발동된 시점으로부터 duration만큼 유지 후 삭제
        if (destroyOnFinish && duration > 0)
        {
            Destroy(gameObject, duration);
        }
    }

    private void ActivateHitBox()
    {
        _isInitialized = true;
        _tickTimer = damageTickRate; // 시작하자마자 즉시 데미지가 들어가도록 세팅
    }

    private void Update()
    {
        if (!_isInitialized) return;

        if (isContinuousDamage)
        {
            _tickTimer += Time.deltaTime;
        }
    }

    private void OnTriggerStay2D(Collider2D col)
    {
        if (!_isInitialized) return;

        // 타겟 레이어 검사
        if (((1 << col.gameObject.layer) & _targetLayer) == 0) return;

        var health = col.GetComponent<CharacterHealth>();
        if (health == null) health = col.GetComponentInParent<CharacterHealth>();
        if (health == null) health = col.GetComponentInChildren<CharacterHealth>();

        if (health != null && !health.IsDead)
        {
            if (isContinuousDamage)
            {
                // 지속 데미지 (장판)
                if (_tickTimer >= damageTickRate)
                {
                    health.GetDamage(_damageInfo);
                    _tickTimer = 0f; // 모든 적에게 동시 데미지가 들어가는 구조 (원한다면 개별 쿨타임으로 개선 가능)
                }
            }
            else
            {
                // 단발성 히트박스 (1번만 타격)
                if (!_hitTargets.Contains(health))
                {
                    _hitTargets.Add(health);
                    health.GetDamage(_damageInfo);
                }
            }
        }
    }
}
