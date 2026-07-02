using UnityEngine;
using UnityEngine.AI;

namespace AstroNuts.Monsters
{
    /// <summary>
    /// 몬스터/미니언 공통 사망 처리.
    /// 이 프로젝트의 관례(BaseEntity.UpdateAnimation)와 동일하게
    /// Animator.Play()로 직접 죽음 스테이트를 재생한다 (Trigger 파라미터 없음).
    ///
    /// 애니메이션 종료 감지는 OnHitEvent/OnAttackEndEvent와 같은 패턴을 따른다:
    /// 죽음 클립에 Animation Event로 OnDeathAnimationFinished를 걸어두면 정확한 타이밍에 발동하고,
    /// 깜빡 잊어도 fallbackDelay 타이머가 대신 처리해서 몬스터가 영원히 안 사라지는 일은 없다.
    /// </summary>
    public class MonsterDeathHandler : MonoBehaviour
    {
        [Tooltip("Animator에서 재생할 죽음 스테이트 이름 (컨트롤러의 State 이름과 동일해야 함)")]
        [SerializeField] private string deathStateName = "Die";

        [Tooltip("죽는 순간 즉시 꺼야 할 콜라이더들")]
        [SerializeField] private Collider2D[] collidersToDisable;

        [Tooltip("죽는 순간 즉시 꺼야 할 스크립트들 (이동, AI, 공격 등)")]
        [SerializeField] private MonoBehaviour[] behavioursToDisable;

        [Tooltip("체크하면 Destroy 대신 SetActive(false)만 함 (오브젝트 풀링용)")]
        [SerializeField] private bool useObjectPooling = false;

        [Tooltip("죽음 클립에 Animation Event를 안 걸어둔 경우를 대비한 안전장치 시간(초)")]
        [SerializeField] private float fallbackDelay = 1.0f;

        private Animator _animator;
        private NavMeshAgent _agent;
        private Rigidbody2D _rb;
        private bool _isDead;
        private bool _hasFinished;

        public bool IsDead => _isDead;

        private void Awake()
        {
            // BaseEntity와 동일하게 자식 오브젝트에서 Animator를 찾는다 (스프라이트가 자식에 있는 구조)
            _animator = GetComponentInChildren<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _rb = GetComponent<Rigidbody2D>();
        }

        /// <summary>외부(전투 로직 등)에서 이 몬스터/미니언을 죽일 때 호출.</summary>
        public void Die()
        {
            if (_isDead) return; // 중복 호출 방지
            _isDead = true;

            foreach (var col in collidersToDisable)
                if (col != null) col.enabled = false;

            foreach (var behaviour in behavioursToDisable)
                if (behaviour != null) behaviour.enabled = false;

            // 죽는 순간 남아있는 관성(미끄러짐) 제거 - BaseEntity의 기절 처리와 동일한 패턴
            if (_agent != null && _agent.isActiveAndEnabled)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
            }

            if (_animator != null)
                _animator.Play(deathStateName);

            // 죽음 클립에 Animation Event를 안 걸어뒀을 경우를 대비한 fallback
            Invoke(nameof(OnDeathAnimationFinished), fallbackDelay);
        }

        /// <summary>
        /// 죽음 애니메이션 클립의 마지막 프레임에 Animation Event로 연결하면
        /// (선택 사항) 정확한 타이밍에 이 함수가 불린다.
        /// 안 걸어둬도 fallbackDelay 이후 자동으로 호출된다.
        /// </summary>
        public void OnDeathAnimationFinished()
        {
            if (_hasFinished) return;
            _hasFinished = true;
            CancelInvoke(nameof(OnDeathAnimationFinished));

            if (useObjectPooling)
                gameObject.SetActive(false);
            else
                Destroy(gameObject);
        }
    }
}
