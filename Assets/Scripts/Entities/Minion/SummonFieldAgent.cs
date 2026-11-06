using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 장착한 소환수의 필드 표시 + AI (전투 개편 프로토타입).
/// 상태: 동행(적이 없을 때 플레이어 주변) → 교전(적에게 다가가 소환수 기본 공격) → 연계(우클릭 스킬·스왑 대쉬 공격에 맞춰 이동·수행).
/// 이동은 NavMesh 경로 + 가속/도착 감속 + 소환수끼리 간격 유지로 움직이고, 경로가 없으면 벽 검사 직선 이동으로 대체한다.
///
/// 콜라이더·CharacterHealth·전투 레이어·NavMeshAgent 가 전혀 없다. 적의 탐색은 플레이어/레이어 기준이고
/// 피격은 콜라이더가 있어야 일어나므로, 적은 소환수를 타겟으로 삼거나 맞힐 수 없고 밀치지도 않는다.
/// 수치는 PlayerSkillController 의 Inspector 에서 조절한다.
/// </summary>
public class SummonFieldAgent : MonoBehaviour
{
    private const float ARRIVE_RADIUS = 0.25f;      // 이 안에서 감속한다
    private const float STOP_RADIUS = 0.12f;       // 목표에 도착했다고 보는 거리
    private const float REPATH_INTERVAL = 0.25f;
    private const float RETARGET_INTERVAL = 0.25f;
    private const float TARGET_KEEP_MARGIN = 2f;   // 타겟 유지 여유. 사거리 경계에서 상태가 깜빡이지 않게 한다
    private const float STUCK_SECONDS = 1.2f;
    private const float BOB_HEIGHT = 0.05f;
    private const float BOB_FREQUENCY = 9f;

    private PlayerSkillController _owner;
    private MainMinionDataSO _data;
    private int _slot;
    private Vector2 _sideOffset;
    private bool _following;
    private bool _backingOff;
    private Vector2 _velocity;
    private GameObject _visual;
    private SpriteRenderer _renderer;
    private Animator _anim;
    private string _poseState;
    private bool _poseFrozen;
    private float _attackTimer;
    private int _linkCount;
    private int _castCount;
    private bool _moving;
    private MinionSkillCaster _basicCaster;
    private CharacterHealth _target;
    private Transform _targetTransform;
    private float _retargetTimer;
    private readonly Vector3[] _corners = new Vector3[16];
    private NavMeshPath _path;
    private int _cornerCount;
    private int _cornerIndex;
    private float _repathTimer;
    private Vector2 _pathGoal;
    private bool _hasPath;
    private float _stuckTimer;
    private float _bobPhase;

    public MainMinionDataSO Data => _data;
    public bool IsLinking => _linkCount > 0;
    public bool IsEngaged => _target != null;
    public bool IsVisualVisible => _visual != null && _visual.activeSelf;
    public int AutoAttackCount { get; private set; }

    public static SummonFieldAgent Spawn(PlayerSkillController owner, MainMinionDataSO data, int slot)
    {
        Vector2 side = slot == 0 ? Vector2.left : Vector2.right;
        var go = new GameObject($"SummonField_{(data != null ? data.minionName : "?")}");
        go.transform.position = owner.transform.position + (Vector3)(side * 0.6f);
        var agent = go.AddComponent<SummonFieldAgent>();
        agent._owner = owner;
        agent._data = data;
        agent._sideOffset = side * 0.6f;
        // 두 마리가 같은 프레임에 때리지 않게 처음 한 번만 어긋나게 둔다.
        agent._attackTimer = owner.AutoAttackInterval * (slot == 0 ? 0.5f : 1f);
        agent._slot = slot;
        agent._path = new NavMeshPath();
        agent.BuildVisual();
        return agent;
    }

    private void BuildVisual()
    {
        GameObject prefab = _data.basicAnim != null ? _data.basicAnim.visual : null;
        if (prefab == null && _data.skillAnim != null) prefab = _data.skillAnim.visual;
        if (prefab == null) return;

        _visual = Instantiate(prefab, transform);
        _visual.transform.localPosition = Vector3.zero;
        // 프리팹에 물리가 붙어 있어도 필드 소환수에는 남기지 않는다(피격 판정 없음 보장).
        foreach (var col in _visual.GetComponentsInChildren<Collider2D>(true)) Destroy(col);
        foreach (var body in _visual.GetComponentsInChildren<Rigidbody2D>(true)) Destroy(body);

        _renderer = _visual.GetComponentInChildren<SpriteRenderer>();

        _anim = _visual.GetComponentInChildren<Animator>();
        if (_anim == null || _anim.runtimeAnimatorController == null) { _anim = null; return; }
        _anim.gameObject.AddComponent<MinionAnimEventRelay>(); // 클립 이벤트의 "has no receiver" 경고 흡수

        // 대기 클립이 있으면 반복 재생, 없으면 첫 클립의 첫 프레임을 기본 자세로 쓴다. 이동감은 코드 흔들림이 담당한다.
        string first = null;
        foreach (var clip in _anim.runtimeAnimatorController.animationClips)
        {
            if (clip == null) continue;
            if (first == null) first = clip.name;
            if (_poseState == null && clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) _poseState = clip.name;
        }
        _poseFrozen = _poseState == null;
        if (_poseFrozen) _poseState = first;
        ReturnToPose();
    }

    private void ReturnToPose()
    {
        if (_anim == null || _poseState == null || !_anim.isActiveAndEnabled) return;
        _anim.speed = _poseFrozen ? 0f : 1f;
        _anim.Play(_poseState, 0, 0f);
    }

    private void Update()
    {
        if (_owner == null) { Destroy(gameObject); return; }
        _attackTimer -= Time.deltaTime;
        if (IsLinking) return;

        // 문 이동처럼 플레이어와 너무 멀어지면 곁으로 옮긴다.
        Vector2 player = _owner.transform.position;
        if (Vector2.Distance(transform.position, player) > _owner.FollowTeleportDistance)
            SettleAt(player + _sideOffset);

        UpdateTarget(player);
        if (_target != null) Engage();
        else Accompany(player);
    }

    private void LateUpdate()
    {
        if (_visual == null || IsLinking) return;
        // 걷는 클립이 없어도 움직이는 느낌이 나도록 이동 중에 살짝 뛰게 한다.
        float speed = _velocity.magnitude;
        if (speed > 0.3f)
        {
            _bobPhase += Time.deltaTime * BOB_FREQUENCY * Mathf.Clamp(speed / 4f, 0.6f, 1.4f);
            _visual.transform.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(_bobPhase)) * BOB_HEIGHT);
        }
        else
        {
            _visual.transform.localPosition = Vector3.MoveTowards(_visual.transform.localPosition, Vector3.zero, Time.deltaTime);
        }
    }

    private void SettleAt(Vector2 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas)) position = hit.position;
        transform.position = position;
        _velocity = Vector2.zero;
        _hasPath = false;
        _stuckTimer = 0f;
    }

    // ─── 상태: 동행 ─────────────────────────────────────────────
    /// <summary>
    /// 플레이어가 멀어질 때만 쫓아가고, 충분히 가까워지면 그 자리에 멈춘다.
    /// 플레이어가 멈춰 있으면 소환수도 움직이지 않는다.
    /// </summary>
    private void Accompany(Vector2 player)
    {
        Vector2 me = transform.position;
        Vector2 fromPlayer = me - player;
        float distance = fromPlayer.magnitude;

        if (!_following && distance > _owner.FollowDistance) _following = true;
        else if (_following && distance <= _owner.FollowStopDistance) _following = false;

        if (!_following) { _hasPath = false; Stop(); return; }

        // 플레이어 옆 고정 자리가 아니라, 지금 있는 쪽에서 곧장 다가가 불필요한 궤도 이동을 없앤다.
        if (distance < 0.001f) fromPlayer = _sideOffset;
        MoveTo(player + fromPlayer.normalized * _owner.FollowStopDistance, _owner.ChaseSpeed);
    }

    // ─── 상태: 교전 ─────────────────────────────────────────────
    private void Engage()
    {
        Vector2 me = transform.position;
        Vector2 targetPos = _targetTransform.position;
        Vector2 away = me - targetPos;
        if (away.sqrMagnitude < 0.0001f) away = _sideOffset;
        float distance = away.magnitude;

        // 적과 겹치지 않을 최소 거리. 덩치 큰 적은 콜라이더 크기만큼 더 띄운다.
        float gap = _owner.MinEnemyGap + BodyRadius(_targetTransform);
        float reach = Mathf.Max(_data.finisher != null ? _data.finisher.summonAttackDistance : 2f, gap + 0.3f); // 소환수별 공격 거리

        // 스킬이 끝난 직후처럼 너무 붙어 있으면 먼저 물러난다. 벽에 막히면 그 자리에서 때린다.
        if (distance < gap) _backingOff = true;
        else if (distance >= Mathf.Lerp(gap, reach, 0.5f)) _backingOff = false;
        if (_backingOff)
        {
            Steer(away.normalized * _owner.ChaseSpeed);
            bool blocked = ((Vector2)transform.position - me).sqrMagnitude < 0.000001f && _velocity.sqrMagnitude > 1f;
            if (!blocked) return;
            _backingOff = false;
        }

        if (distance <= reach)
        {
            Stop();
            Face(targetPos.x - me.x);
            if (_attackTimer > 0f) return;
            _attackTimer = _owner.AutoAttackInterval;
            AutoAttack(targetPos);
            return;
        }

        // 두 마리가 한 점에 겹치지 않게 적을 사이에 두고 서로 다른 쪽으로 붙는다.
        float angle = _slot == 0 ? 25f : -25f;
        Vector2 stand = targetPos + (Vector2)(Quaternion.Euler(0f, 0f, angle) * (Vector3)(away.normalized * (reach * 0.9f)));
        MoveTo(stand, _owner.ChaseSpeed);
    }

    private static float BodyRadius(Transform enemy)
    {
        var body = enemy.GetComponentInChildren<Collider2D>();
        return body != null && !body.isTrigger ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) : 0f;
    }

    private void UpdateTarget(Vector2 player)
    {
        if (!CanFight()) { ClearTarget(); return; }

        // 지금 타겟은 사거리를 조금 벗어나도 유지하고, 플레이어와 너무 벌어지면 놓는다.
        if (_target != null && (_targetTransform == null || _target.IsDead || _target.Invincible
            || Vector2.Distance(_targetTransform.position, transform.position) > _owner.AutoAttackRange + TARGET_KEEP_MARGIN
            || Vector2.Distance(_targetTransform.position, player) > _owner.CombatLeashDistance))
            ClearTarget();

        _retargetTimer -= Time.deltaTime;
        if (_target != null || _retargetTimer > 0f) return;
        _retargetTimer = RETARGET_INTERVAL;
        _target = FindTarget(player, out _targetTransform);
    }

    private void ClearTarget()
    {
        _target = null;
        _targetTransform = null;
    }

    // ─── 이동 ───────────────────────────────────────────────────
    private void Stop() => Steer(Vector2.zero);

    private void MoveTo(Vector2 goal, float speed)
    {
        Vector2 me = transform.position;
        float distance = Vector2.Distance(me, goal);
        if (distance <= STOP_RADIUS) { _hasPath = false; Stop(); return; }

        Vector2 waypoint = NextWaypoint(me, goal);
        Vector2 toWaypoint = waypoint - me;
        // 마지막 구간에서만 감속한다. 경유점에서는 속도를 유지해 부드럽게 돈다.
        float arrive = Mathf.Clamp01(distance / ARRIVE_RADIUS);
        Vector2 desired = toWaypoint.sqrMagnitude > 0.0001f ? toWaypoint.normalized * (speed * arrive) : Vector2.zero;
        Steer(desired);
        TrackStuck(me, distance);
    }

    private Vector2 NextWaypoint(Vector2 me, Vector2 goal)
    {
        _repathTimer -= Time.deltaTime;
        if (!_hasPath || _repathTimer <= 0f || (goal - _pathGoal).sqrMagnitude > 0.25f)
        {
            _repathTimer = REPATH_INTERVAL;
            _pathGoal = goal;
            _hasPath = TryBuildPath(me, goal);
        }
        if (!_hasPath) return goal;

        while (_cornerIndex < _cornerCount - 1 && ((Vector2)_corners[_cornerIndex] - me).sqrMagnitude < 0.04f)
            _cornerIndex++;
        return _corners[_cornerIndex];
    }

    private bool TryBuildPath(Vector2 from, Vector2 to)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1.5f, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit end, 1.5f, NavMesh.AllAreas)) return false;
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path)) return false;
        if (_path.status == NavMeshPathStatus.PathInvalid) return false;
        _cornerCount = _path.GetCornersNonAlloc(_corners);
        _cornerIndex = _cornerCount > 1 ? 1 : 0; // 0번은 출발점이다
        return _cornerCount > 0;
    }

    /// <summary>원하는 속도로 가속/감속하고 소환수끼리 간격을 띄운 뒤, 벽을 넘지 않게 한 칸 움직인다.</summary>
    private void Steer(Vector2 desired)
    {
        // 멈춰 있을 때는 간격을 벌리지 않는다. 서서 미끄러지면 떠다니는 것처럼 보인다.
        if (desired.sqrMagnitude > 0.0001f) desired += Separation();
        _velocity = Vector2.MoveTowards(_velocity, desired, _owner.MoveAcceleration * Time.deltaTime);
        if (_velocity.sqrMagnitude < 0.0004f) { _velocity = Vector2.zero; return; }

        Vector2 me = transform.position;
        Vector2 step = _velocity * Time.deltaTime;
        Vector2 next = SkillCombatUtil.GetSafeDestination(me, step, step.magnitude);
        transform.position = next;
        if (Mathf.Abs(_velocity.x) > 0.3f) Face(_velocity.x);
    }

    private Vector2 Separation()
    {
        Vector2 push = SeparationFromSummon();
        // 이동 중에는 다른 적의 몸도 비켜 간다. 지금 타겟과의 거리는 Engage 가 맞춘다.
        Vector2 me = transform.position;
        float gap = _owner.MinEnemyGap;
        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null || gap <= 0f) continue;
            Vector2 away = me - (Vector2)enemy.transform.position;
            float distance = away.magnitude;
            if (distance >= gap || distance < 0.001f) continue;
            push += away.normalized * (_owner.ChaseSpeed * (1f - distance / gap));
        }
        return push;
    }

    private Vector2 SeparationFromSummon()
    {
        var other = _owner.GetFieldAgent(1 - _slot);
        float range = _owner.SeparationDistance;
        if (other == null || other == this || other.IsLinking || range <= 0f) return Vector2.zero;
        Vector2 away = (Vector2)transform.position - (Vector2)other.transform.position;
        float distance = away.magnitude;
        if (distance >= range) return Vector2.zero;
        if (distance < 0.001f) away = _sideOffset;
        return away.normalized * (_owner.ChaseSpeed * 0.3f * (1f - distance / range));
    }

    /// <summary>벽 모서리 등에 끼어 오래 못 움직이면 플레이어 곁으로 옮겨 풀어 준다.</summary>
    private void TrackStuck(Vector2 before, float distanceToGoal)
    {
        bool moved = ((Vector2)transform.position - before).sqrMagnitude > 0.000001f;
        _stuckTimer = moved || distanceToGoal <= ARRIVE_RADIUS ? 0f : _stuckTimer + Time.deltaTime;
        if (_stuckTimer < STUCK_SECONDS) return;
        SettleAt((Vector2)_owner.transform.position + _sideOffset);
    }

    private void Face(float dirX)
    {
        if (_renderer != null && Mathf.Abs(dirX) > 0.0001f) _renderer.flipX = dirX > 0f;
    }

    private bool CanFight()
    {
        var player = _owner.GetComponent<PlayerController>();
        return player != null && player.Stat != null && !player.Stat.IsDead
            && !player.IsInputBlocked && !PlayerController.IsGameplayPaused;
    }

    /// <summary>소환수 또는 플레이어 근처(발견 범위)의 가장 가까운 적. 플레이어 곁에서 이미 싸우는 적도 찾도록 양쪽을 본다.</summary>
    private CharacterHealth FindTarget(Vector2 player, out Transform targetTransform)
    {
        targetTransform = null;
        CharacterHealth best = null;
        float range = _owner.AutoAttackRange;
        float bestSqr = float.MaxValue;
        Vector2 me = transform.position;
        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null) continue;
            var health = enemy.GetComponent<CharacterHealth>() ?? enemy.GetComponentInParent<CharacterHealth>();
            if (health == null || health.IsDead || health.Invincible) continue;
            Vector2 pos = enemy.transform.position;
            float sqr = (pos - me).sqrMagnitude;
            if (sqr > range * range && (pos - player).sqrMagnitude > range * range) continue;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = health;
            targetTransform = enemy.transform;
        }
        return best;
    }

    private void AutoAttack(Vector2 targetPos)
    {
        var melee = _owner.GetComponent<MeleeCombatController>();
        if (melee == null) return;
        Vector2 me = transform.position;
        Vector2 dir = targetPos - me;
        Face(dir.x);
        AutoAttackCount++;
        StartCoroutine(ActionRoutine(() =>
        {
            _basicCaster = melee.CastSummonBasicAttack(_data, me, dir);
            return _basicCaster;
        }, false));
    }

    /// <summary>우클릭 스킬이 나가면 진행 중인 기본 공격은 접는다. 같은 소환수가 동시에 둘을 하지 않는다.</summary>
    public void CancelBasicAttack()
    {
        if (_basicCaster != null) Destroy(_basicCaster.gameObject);
    }

    /// <summary>
    /// 스킬/대쉬 공격 연계: 플레이어 위치로 빠르게 이동 → 행동 수행 → 행동이 끝나고 복귀 지연 뒤 그 자리에서 일반 AI.
    /// 행동의 외형은 MinionSkillCaster 가 그리므로, 그동안 필드 외형은 숨겨 두 마리로 보이지 않게 한다.
    /// </summary>
    /// <param name="perform">실제 행동. 시전 오브젝트를 돌려주며, 실패하면 null.</param>
    public void PlayLink(Func<MinionSkillCaster> perform)
    {
        StartCoroutine(ActionRoutine(perform, true));
    }

    private IEnumerator ActionRoutine(Func<MinionSkillCaster> perform, bool moveToPlayer)
    {
        _linkCount++;
        _velocity = Vector2.zero;
        _hasPath = false;
        ClearTarget();
        if (_visual != null) _visual.transform.localPosition = Vector3.zero;

        if (_moving) yield return new WaitWhile(() => _moving); // 앞선 연계가 이동 중이면 그 도착에 맞춰 함께 나간다.
        else if (moveToPlayer && IsVisualVisible)
        {
            _moving = true;
            Vector2 from = transform.position;
            float duration = _owner.LinkMoveDuration;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                transform.position = Vector2.Lerp(from, _owner.transform.position, t / duration);
                yield return null;
            }
            transform.position = _owner.transform.position;
            _moving = false;
        }

        _castCount++;
        if (_visual != null) _visual.SetActive(false);
        MinionSkillCaster caster = perform != null ? perform() : null;
        // 스킬로 돌진하면 시전 오브젝트가 움직인다. 끝난 자리에서 이어가도록 위치를 따라간다.
        while (caster != null)
        {
            transform.position = caster.transform.position;
            yield return null;
        }
        _castCount--;

        // 사라지는 틈 없이 끝난 자리에 바로 나타난다. 복귀 지연 동안에는 그 자리에 서 있다.
        if (_castCount == 0 && _visual != null)
        {
            _visual.SetActive(true);
            ReturnToPose();
            SettleAt(transform.position);
        }
        if (moveToPlayer) yield return new WaitForSeconds(_owner.LinkReturnDelay);

        _linkCount--;
        if (_linkCount == 0) _retargetTimer = 0f; // 그 자리에서 바로 적을 찾아간다.
    }
}
