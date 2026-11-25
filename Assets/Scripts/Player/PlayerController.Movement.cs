using System.Collections.Generic;
using UnityEngine;

public partial class PlayerController
{
    private void AdvanceLocomotion(Vector2 input, float deltaTime)
    {
        _locomotion.Step(input, movementAccelerationTime, movementSmoothTime, movementTurnTime, deltaTime);
        MoveDirection = _locomotion.Direction;
    }

    private void StopLocomotion(bool stopVelocity = true) => ResetLocomotion(Vector2.zero, stopVelocity);

    private void ResetLocomotion(Vector2 input, bool stopVelocity = true)
    {
        _locomotion.Reset(input);
        MoveDirection = input;
        if (stopVelocity && _rb != null) _rb.linearVelocity = Vector2.zero;
    }

    public void UpdateLocomotionAnimation()
    {
        if (stat != null && stat.IsDead) return;
        if (!canChangeState) return; // 공격/대시의 모션과 재생 속도는 해당 행동이 소유한다.
        bool blocked = _inputBlocked || IsGameplayPaused || IsCCed;
        Vector2 input = blocked ? Vector2.zero : moveInput;
        bool moving = !blocked && (input.sqrMagnitude > PlayerLocomotion.InputDeadZoneSquared || _locomotion.Direction.sqrMagnitude >= PlayerLocomotion.StopThresholdSquared);
        if (visualSquash != null)
        {
            visualSquash.SetActorAmplitude(CameraSettingsSO.Instance?.playerPoseStrength ?? .36f);
            visualSquash.Locomotion(moving);
        }
        if (moving)
        {
            TransitionToState(walkState);
            float speed = stat != null ? stat.MOVESPEED * SpeedMultiplier * roomWalkScale : 5f;
            UpdateWalkAnimSpeed(_locomotion.Direction.magnitude * speed);
        }
        else
        {
            ResetWalkAnimSpeed();
            TransitionToState(idleState);
        }
        // 감속 벡터가 이전 방향을 가리켜도 새 입력 방향으로 즉시 돌아본다.
        if (input.x > 0.1f) transform.localScale = new Vector3(-1f, transform.localScale.y, transform.localScale.z);
        else if (input.x < -0.1f) transform.localScale = new Vector3(1f, transform.localScale.y, transform.localScale.z);
    }

    /// <summary>대시 종료 후 현재 누른 방향으로 걷기를 이어간다. 대시 속도/옛 평타 전진은 넘기지 않는다.</summary>
    public void ResumeLocomotionAfterDash()
    {
        StopAttackDash();
        StopLocomotion();
        if (_inputBlocked || IsGameplayPaused || IsCCed || (stat != null && stat.IsDead)) return;
        visualSquash?.PlayLand();
        ResetLocomotion(Vector2.ClampMagnitude(moveInput, 1f));
        if (_rb != null && stat != null) _rb.linearVelocity = _locomotion.Direction * stat.MOVESPEED * SpeedMultiplier * roomWalkScale;
        UpdateLocomotionAnimation();
    }

    public enum SpeedModifierSource
    {
        [UnityEngine.InspectorName("근접 공격")] MeleeAttack,
        [UnityEngine.InspectorName("패링")] Parry,
        [UnityEngine.InspectorName("스킬")] Skill,
        [UnityEngine.InspectorName("디버프")] Debuff
    }

    private Dictionary<SpeedModifierSource, float> _speedModifiers = new Dictionary<SpeedModifierSource, float>();

    public void SetSpeedModifier(SpeedModifierSource source, float multiplier)
    {
        _speedModifiers[source] = multiplier;
        if (multiplier <= 0f) StopLocomotion();
    }

    public void RemoveSpeedModifier(SpeedModifierSource source)
    {
        if (_speedModifiers.ContainsKey(source))
        {
            _speedModifiers.Remove(source);
        }
    }

    public float SpeedMultiplier
    {
        get
        {
            float total = 1.0f;
            foreach (var mult in _speedModifiers.Values)
            {
                total *= mult;
            }
            return total;
        }
    }

    [SerializeField, Range(1f, 2f)] private float clearedRoomSprint = 1.45f;
    private float roomWalkScale = 1f, nextSprintTrail;
    public bool IsRoomSprinting => roomWalkScale > 1.01f && MoveDirection.sqrMagnitude > .01f;
    [SerializeField, Min(0)] private float hurtPushDistance = .25f;
    private Vector2 hurtDirection;
    private float hurtTime;
    private const float HurtTravelTime = .12f;

    internal bool ReactToHit(Vector2 direction)
    {
        if (_rb == null || IsDashing || _inputBlocked) return false;
        if (_melee != null && _melee.IsAttacking) _melee.CancelPlayerAttack();
        hurtDirection = direction.normalized;
        hurtTime = HurtTravelTime;
        return true;
    }

    private const float KnockbackSpeedSquared = 200f;

    private void FixedUpdate()
    {
        if (_inputBlocked || stat == null || stat.Health == null || stat.Health.IsDead)
        {
            StopLocomotion();
            return;
        }

        if (hurtTime > 0)
        {
            float dt = Mathf.Min(hurtTime, Time.fixedDeltaTime);
            float start = hurtTime / HurtTravelTime;
            hurtTime -= dt;
            float end = hurtTime / HurtTravelTime;
            float travel = hurtPushDistance * (start * start - end * end);
            var next = SkillCombatUtil.ClampToWall(_rb.position, hurtDirection, travel, DashRadius);
            _rb.linearVelocity = (next - _rb.position) / Time.fixedDeltaTime;
            return;
        }
        if (_isDashing)
        {
            _dashElapsed += Time.fixedDeltaTime;
            bool finished = AdvanceDash(_dashStartPosition, _dashEndPosition, _dashElapsed, _dashTravelDuration, ref _dashProgress);
            _dashTimeLeft -= Time.fixedDeltaTime;
            if (finished || _dashTimeLeft <= 0f) EndDash();
            return;
        }

        // The two-charge dodge owns velocity, invulnerability and layer until it finishes.
        if (IsDashing) return;
        if (_isAttackDashing)
        {
            if (IsCCed) StopAttackDash();
            else if (IsGameplayPaused) _rb.linearVelocity = Vector2.zero;
            else AdvanceAttackDash();
            return;
        }

        // Preserve an externally applied knockback velocity.
        if (_rb == null || _rb.linearVelocity.sqrMagnitude >= KnockbackSpeedSquared) return;
        var room = MapGenerator.Instance != null ? MapGenerator.Instance.CurrentRoom : null;
        bool sprint = room != null && room.CanSprintAt(_rb.position) && !_speedModifiers.ContainsKey(SpeedModifierSource.MeleeAttack);
        roomWalkScale = sprint ? Mathf.MoveTowards(roomWalkScale, clearedRoomSprint, Time.fixedDeltaTime * 3f) : 1f;
        float speed = stat.MOVESPEED * SpeedMultiplier * roomWalkScale;
        if (IsGameplayPaused || IsCCed || speed <= 0f)
        {
            StopLocomotion();
            return;
        }

        AdvanceLocomotion(moveInput, Time.fixedDeltaTime);
        _rb.linearVelocity = MoveDirection * speed;
        if (IsRoomSprinting && Time.time >= nextSprintTrail)
        {
            nextSprintTrail = Time.time + .12f;
            WorldSparkField.Emit(WorldSparkField.Kind.Sprint, _rb.position, -MoveDirection, 2);
        }
    }

    private void TryStartDodge()
    {
        if (_dodgeController != null)
        {
            if (_dodgeController.TryDash(moveInput, transform.localScale.x)) visualSquash?.PlayDash();
            return;
        }

        if (_isDashing || Time.time < _lastDashTime + DashCooldown) return;
        StartDash();
        visualSquash?.PlayDash();
    }

    /// <summary>
    /// 곡선의 이번 물리 틱 이동량을 속도로 전달한다. 무적/대시 레이어 전환은 하지 않는다.
    /// </summary>
    private void AdvanceAttackDash()
    {
        // Keep the final movement through physics. The following stationary tick also
        // settles the interpolated body before the melee controller starts the swing.
        if (_attackDashTimeLeft <= 0f)
        {
            StopAttackDash();
            return;
        }
        _attackDashTimeLeft -= Time.fixedDeltaTime;
        if (_attackDashTimeLeft < .00001f) _attackDashTimeLeft = 0f;
        float progress = Mathf.Max(_attackDashProgress, SkillCombatUtil.EvaluateMotionProgress(
            attackDashMovementCurve, 1f - _attackDashTimeLeft / attackDashDuration));
        float distance = _attackDashSpeedVal * attackDashDuration * (progress - _attackDashProgress);
        _attackDashProgress = progress;
        // 가파른 그래프라도 물리 한 틱에 벽을 넘어가지 않게 같은 경로 검사를 사용한다.
        Vector2 from = _rb.position;
        Vector2 next = SkillCombatUtil.ClampToWall(from, _attackDashDir, distance, DashRadius);
        _rb.linearVelocity = (next - from) / Time.fixedDeltaTime;
    }

    internal float AttackStepDistance => attackDashSpeed * attackDashDuration;
    internal bool IsAttackStepping => _isAttackDashing;

    public bool ApplyAttackDash(Vector2 direction, float forceMultiplier = 1.0f)
    {
        if (_rb == null || _inputBlocked || (stat != null && stat.IsDead) || IsDashing ||
            direction.sqrMagnitude <= .001f || attackDashDuration <= 0f ||
            attackDashSpeed <= 0f || forceMultiplier <= 0f) return false;

        _isAttackDashing = true;
        _attackDashDir = direction.normalized;
        _attackDashSpeedVal = attackDashSpeed * forceMultiplier;
        _attackDashTimeLeft = attackDashDuration;
        _attackDashProgress = 0f;
        return true;
    }

    internal void StopAttackDash()
    {
        if (!_isAttackDashing) return;
        _isAttackDashing = false;
        _attackDashTimeLeft = 0f;
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
    }

    internal void PlayAttackSquash() => visualSquash?.PlayAttack();

    public void SetDashLayer(bool isDash)
    {
        if (isDash)
        {
            gameObject.layer = _dashLayer;
        }
        else
        {
            RestoreDashLanding();
            gameObject.layer = _originalLayer;
        }
    }

    private void StartDash()
    {
        if (_melee != null && _melee.IsAttacking) _melee.CancelPlayerAttack();

        _isDashing = true;
        _lastDashTime = Time.time;

        SetDashLayer(true);

        // 이동 입력이 있으면 그 방향으로, 없으면 현재 바라보는 방향(또는 우측)으로 대쉬
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-transform.localScale.x, 0).normalized; // x scale이 -1이면 오른쪽
        }

        // Unsteppable 안전 체크 및 대시 도달 범위 축소
        float originalDist = dashSpeed * dashDuration * (stat != null ? stat.DASH_DISTANCE_MULTIPLIER : 1f);
        Vector2 start = _rb.position;
        if (MapGenerator.Instance != null && MapGenerator.Instance.HasUnsteppableBetween(start, _dashDir, originalDist))
            originalDist *= 1.5f; // 기본 회피 경로도 물을 넘을 때만 연장. Wall 제한은 그대로 유지.
        Vector2 safePos = GetSafeDashPosition(start, _dashDir, originalDist);
        _dashEndPosition = safePos;
        float actualDist = Vector2.Distance(start, safePos);
        if (actualDist > 0.0001f) _dashDir = (safePos - start).normalized;
        _dashStartPosition = _rb.position;
        _dashTravelDuration = actualDist / Mathf.Max(0.01f, dashSpeed);
        _dashElapsed = _dashProgress = 0f;
        _dashTimeLeft = _dashTravelDuration + 0.1f; // 물리 충돌로 이동 불가할 때의 안전 종료

        if (stat != null && stat.Health != null)
        {
            stat.Health.Invincible = true; // 대쉬 무적 시작
        }

    }

    private void EndDash()
    {
        _isDashing = false;

        SetDashLayer(false);

        ResumeLocomotionAfterDash();

        if (stat != null && stat.Health != null)
        {
            stat.Health.Invincible = false; // 대쉬 무적 종료
        }
    }

    public Vector2 GetSafeDashPosition(Vector2 startPos, Vector2 direction, float maxDistance)
    {
        _lastSafeDashPosition = startPos;
        _dashLandingTracked = true;
        Vector2 offset = DashBodyOffset;
        return SkillCombatUtil.GetSafeBoxDestination(startPos + offset, direction, maxDistance, DashBodySize) - offset;
    }

    // bounds.center는 물리 위치, transform.position은 보간된 표시 위치일 수 있다.
    // 둘을 빼면 FixedUpdate/강제 이동 직후 발 오프셋이 흔들리므로 로컬 콜라이더 오프셋을 사용한다.
    private Vector2 DashBodyOffset => _dashBody != null ? (Vector2)transform.TransformVector(_dashBody.offset) : Vector2.zero;
    private Vector2 DashBodySize => _dashBody != null ? (Vector2)_dashBody.bounds.size : Vector2.one * .4f;

    private void TrackDashLanding(Vector2 position)
    {
        if (SkillCombatUtil.IsSafeBoxLanding(position + DashBodyOffset, DashBodySize))
            _lastSafeDashPosition = position;
    }

    private void RestoreDashLanding()
    {
        if (!_dashLandingTracked || _rb == null) return;
        _dashLandingTracked = false;
        if (SkillCombatUtil.IsSafeBoxLanding(_rb.position + DashBodyOffset, DashBodySize)) return;
        // 물 위에서 중단되면 이미 지나온 안전 지점으로 복귀한 뒤 일반 충돌을 복원한다.
        Vector2 safe = _lastSafeDashPosition;
        if (!SkillCombatUtil.IsSafeBoxLanding(safe + DashBodyOffset, DashBodySize))
        {
            // 출발점부터 물가에 붙어 있었다면 가까운 땅 안쪽으로 밀어낸다.
            bool found = false;
            for (float r = .025f; r <= 1f && !found; r += .025f)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    Vector2 candidate = safe + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (!SkillCombatUtil.IsSafeBoxLanding(candidate + DashBodyOffset, DashBodySize)) continue;
                    Vector2 checkedPoint = SkillCombatUtil.ClampBoxToWall(safe + DashBodyOffset, candidate - safe, DashBodySize);
                    if (Vector2.Distance(checkedPoint, candidate + DashBodyOffset) > .001f) continue;
                    safe = candidate; found = true; break;
                }
        }
        _rb.position = safe;
        transform.position = new Vector3(safe.x, safe.y, transform.position.z);
        _rb.linearVelocity = Vector2.zero;
    }

    private float DashRadius
    {
        get
        {
            Collider2D body = _dashBody;
            return body != null
                ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) + 0.02f
                : 0.3f;
        }
    }

    /// <summary>두 대쉬 컨트롤러 공용. 물리 한 틱의 이동도 Wall/최종 착지점을 넘지 않게 제한한다.</summary>
    public bool AdvanceDash(Vector2 destination, float speed)
    {
        if (_rb == null) return true;
        Vector2 from = _rb.position;
        TrackDashLanding(from);
        Vector2 next = Vector2.MoveTowards(from, destination, Mathf.Max(0f, speed) * Time.fixedDeltaTime);
        Vector2 step = next - from;
        next = SkillCombatUtil.ClampBoxToWall(from + DashBodyOffset, step, DashBodySize) - DashBodyOffset;
        _rb.linearVelocity = (next - from) / Time.fixedDeltaTime;
        return (next - from).sqrMagnitude < 0.000001f;
    }

    /// <summary>곡선 대시. 평평한 구간은 정지/종료가 아니라 대기이며, 실제 벽에 막혔을 때만 조기 종료한다.</summary>
    public bool AdvanceDash(Vector2 start, Vector2 destination, float elapsed, float duration, ref float progress)
    {
        if (_rb == null) return true;
        progress = Mathf.Max(progress, SkillCombatUtil.EvaluateMotionProgress(
            dashMovementCurve, duration > 0f ? elapsed / duration : 1f));
        Vector2 from = _rb.position;
        TrackDashLanding(from);
        Vector2 next = Vector2.Lerp(start, destination, progress);
        Vector2 step = next - from;
        Vector2 allowed = SkillCombatUtil.ClampBoxToWall(from + DashBodyOffset, step, DashBodySize) - DashBodyOffset;
        _rb.linearVelocity = (allowed - from) / Time.fixedDeltaTime;
        bool blocked = step.sqrMagnitude > 0.000001f && (allowed - from).sqrMagnitude < 0.000001f;
        bool arrived = progress >= 1f && (destination - from).sqrMagnitude < 0.000001f;
        return blocked || arrived;
    }
}
