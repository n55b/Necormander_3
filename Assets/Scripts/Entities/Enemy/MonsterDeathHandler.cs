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
        [Tooltip("Animator에서 재생할 죽음 스테이트 이름. 이 이름이 컨트롤러에 없으면 " +
                 "Die / Dead 중 실제로 있는 쪽으로 자동 대체한다(아트가 태그를 어느 이름으로 달았든 동작).")]
        [SerializeField] private string deathStateName = "Die";

        /// <summary>사망 스테이트 이름 후보. 아트마다 태그를 Die 로도 Dead 로도 달아서 둘 다 받는다.</summary>
        private static readonly string[] DeathStateAliases = { "Die", "Dead" };

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
        private BaseEntity _baseEntity;
        private Canvas _hpCanvas;
        private bool _isDead;
        private bool _hasFinished;

        public bool IsDead => _isDead;

        private void Awake()
        {
            // BaseEntity와 동일하게 자식 오브젝트에서 Animator를 찾는다 (스프라이트가 자식에 있는 구조)
            _animator = GetComponentInChildren<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _rb = GetComponent<Rigidbody2D>();
            _baseEntity = GetComponent<BaseEntity>();
            _hpCanvas = GetComponentInChildren<Canvas>(true); // 머리 위 HP바/디버프 패널이 담긴 Canvas
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

            // 돌격 등 코루틴으로 이동 중인 공격이 있다면 먼저 멈춤 (enabled=false만으론 코루틴이 안 멈춤)
            _baseEntity?.CancelAttack();

            // 사망 시 BaseEntity와 MonsterDeathHandler에 남아있는 모든 공격/패턴 코루틴 강제 정지
            if (_baseEntity != null)
            {
                _baseEntity.StopAllCoroutines();
            }
            StopAllCoroutines();

            // 죽는 순간 남아있는 관성(미끄러짐) 제거 - BaseEntity의 기절 처리와 동일한 패턴
            // isOnNavMesh 까지 봐야 한다. 사망 시점엔 이미 에이전트가 NavMesh 에서 떨어져 있는
            // 경우가 흔해서(넉백 도중 사망, 벽 밖으로 밀려난 뒤 사망) 여기서 경고가 제일 많이 났다.
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
            }

            // 애니메이션 재생 중엔 머리 위 HP 캔버스(체력바+디버프 패널)를 숨김
            if (_hpCanvas != null)
                _hpCanvas.gameObject.SetActive(false);

            float delay = fallbackDelay;

            if (_animator != null)
            {
                // [26/08/16] 배속으로 공격 모션을 재생하던 중에 죽으면 그 배속이 사망 클립까지 따라온다.
                // (엘리트 차저처럼 클립 길이를 예비동작에 맞추려고 Animator.speed 를 건드리는 패턴이 있다.)
                // 느려진 채로 재생되면 fallbackDelay 안에 페이드아웃이 못 끝나고 뚝 끊긴다.
                _animator.speed = 1f;
                _animator.Play(ResolveDeathState(), 0, 0f);

                // [26/08/18] 사망 클립이 fallbackDelay 보다 길면 연출이 중간에 잘린다
                // (해태 Dead 는 1.2초인데 기본값은 1.0초다). 클립 길이를 읽어 더 긴 쪽을 쓴다.
                // Play 직후엔 스테이트가 아직 반영 전이라 Update(0) 으로 한 번 밀어야 길이가 잡힌다.
                if (_animator.isActiveAndEnabled)
                {
                    _animator.Update(0f);
                    float clipLength = _animator.GetCurrentAnimatorStateInfo(0).length;
                    if (clipLength > delay) delay = clipLength + 0.05f; // 마지막 프레임이 한 번은 그려지게
                }
            }

            // 죽음 클립에 Animation Event를 안 걸어뒀을 경우를 대비한 fallback
            Invoke(nameof(OnDeathAnimationFinished), delay);
        }

        /// <summary>
        /// 실제로 재생할 사망 스테이트 이름. 인스펙터 값이 컨트롤러에 없으면 Die / Dead 중
        /// 있는 쪽으로 대체한다 — Animator.Play 는 없는 스테이트에 대해 <b>조용한 no-op</b> 이라,
        /// 이름이 어긋나면 시체가 마지막 공격 포즈로 굳은 채 사라진다(에러도 안 뜬다).
        /// </summary>
        private string ResolveDeathState()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return deathStateName;
            if (_animator.HasState(0, Animator.StringToHash(deathStateName))) return deathStateName;

            foreach (var alt in DeathStateAliases)
                if (alt != deathStateName && _animator.HasState(0, Animator.StringToHash(alt))) return alt;

            return deathStateName;
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
