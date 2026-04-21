using UnityEngine;
using System.Collections.Generic;

namespace Necromancer.Object
{
    /// <summary>
    /// 전사 + 마법사 조합으로 생성되는 중력장 효과입니다.
    /// 범위 내의 적을 중심으로 강하게 끌어당깁니다.
    /// </summary>
    public class GravityFieldThrowEffect : MonoBehaviour
    {
        private float _radius;
        private float _duration;
        private float _pullStrength;
        private LayerMask _enemyLayer;
        private float _timer;

        [Header("비주얼 설정")]
        [SerializeField] private SpriteRenderer fieldRenderer;

        public void Initialize(float radius, float duration, float pullStrength, LayerMask enemyLayer)
        {
            _radius = radius;
            _duration = duration;
            _pullStrength = pullStrength;
            _enemyLayer = enemyLayer;
            _timer = 0f;

            // 시각적 크기를 반지름에 맞게 조정
            // 원형 스프라이트 기준 (기본 크기가 반지름 0.5라면 2배를 곱해 지름을 맞춤)
            transform.localScale = new Vector3(_radius * 2, _radius * 2, 1);
            
            if (fieldRenderer != null)
            {
                // 시간에 따라 사라지는 연출을 위해 컬러 초기화
                Color c = fieldRenderer.color;
                c.a = 0.6f;
                fieldRenderer.color = c;
            }
        }

        void FixedUpdate()
        {
            _timer += Time.fixedDeltaTime;
            
            // 서서히 투명해지는 연출
            if (fieldRenderer != null)
            {
                Color c = fieldRenderer.color;
                c.a = Mathf.Lerp(0.6f, 0f, _timer / _duration);
                fieldRenderer.color = c;
            }

            if (_timer >= _duration)
            {
                Destroy(gameObject);
                return;
            }

            // 범위 내 적 탐색
            Collider2D[] enemies = Physics2D.OverlapCircleAll(transform.position, _radius, _enemyLayer);

            foreach (var col in enemies)
            {
                if (col.TryGetComponent<Rigidbody2D>(out var rb))
                {
                    Vector2 center = transform.position;
                    Vector2 enemyPos = col.transform.position;
                    Vector2 direction = center - enemyPos;
                    float distance = direction.magnitude;

                    if (distance > 0.2f) 
                    {
                        // 탈출을 불가능하게 하기 위해 단순 Force가 아닌 Velocity 제어 방식을 사용
                        // 중심 방향으로 끌어당기는 속도 벡터 생성
                        Vector2 targetVelocity = direction.normalized * _pullStrength;
                        
                        // 현재 적의 속도를 무시하고 중력장 방향으로 강제 이동 (Lerp로 부드러움 유지)
                        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, targetVelocity, Time.fixedDeltaTime * 10f);
                    }
                    else
                    {
                        // 중심부에 도달하면 맴돌게 하거나 멈춤
                        rb.linearVelocity *= 0.8f;
                    }
                }
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
