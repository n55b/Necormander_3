using UnityEngine;
using UnityEngine.InputSystem;

public partial class MeleeCombatController
{
    private void Update()
    {
        if (_preparingAttack)
        {
            if (PlayerController.IsGameplayPaused) return;
            if (!_player.CanStartGameplayAction || _player.IsDashing || PlayerController.IsCombatPointerBlocked)
            {
                CancelAttackPreparation();
                return;
            }

            if (!_player.IsAttackStepping) StartSwing();
            return;
        }

        if (IsAttacking) return;

        if (_ownsAnimationLock)
        {
            _ownsAnimationLock = false;
            _player.CanChangeAnimState();
        }
        if (_player != null)
            _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack);

        if (Time.time - _lastAttackTime > comboResetTime) _comboStep = 0;
        if (_isHoldingAttack) ExecuteMeleeAttack();
    }

    public void OnAttackInput(InputAction.CallbackContext context)
    {
        if (context.canceled || PlayerController.IsCombatPointerBlocked ||
            (_player != null && _player.IsInputBlocked))
        {
            ReleaseAttackInput();
            return;
        }
        if (!context.started) return;

        _isHoldingAttack = true;
        ExecuteMeleeAttack();
    }

    internal void ReleaseAttackInput() => _isHoldingAttack = false;

    private void OnDisable() => CancelAttackPreparation();

    internal void CancelAttackPreparation()
    {
        if (!_preparingAttack) return;
        _preparingAttack = false;
        if (_player == null) return;
        _player.StopAttackDash();
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack);
    }

    private void ExecuteMeleeAttack()
    {
        if (PlayerController.IsCombatPointerBlocked)
        {
            ReleaseAttackInput();
            return;
        }
        if (_player == null || !_player.CanStartGameplayAction || _player.IsDashing || IsAttacking) return;

        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack, 0f);
        Vector2 step = _player.MoveInput.normalized * (_player.AttackStepDistance * lightAttackDashMultiplier);
        if (TryAim(step, out _, out var correction)) step += correction;
        _preparingAttack = _player.ApplyAttackDash(step.normalized, step.magnitude / Mathf.Max(.001f, _player.AttackStepDistance));
        if (!_preparingAttack) StartSwing();
    }

    private void StartSwing()
    {
        _preparingAttack = false;
        _player.PlayAttackSquash();

        _lastAttackTime = Time.time;
        _player.SetSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack, attackWalkSpeedMultiplier);
        int swingIndex = _comboStep;

        float telegraphDuration = (swingIndex == 2 ? thirdTelegraphDuration : lightTelegraphDuration)
            / Mathf.Clamp(_player.Stat.ATKSPD, 0.5f, 3f);
        var weapon = EquipmentInventoryManager.Instance != null ? EquipmentInventoryManager.Instance.Weapon : null;
        float damageMultiplier = _player.Stat.BASIC_ATK_MULT;
        damageMultiplier *= weapon != null ? weapon.ComboMultiplier(swingIndex) : (swingIndex == 2 ? 1.5f : 1f);
        damageMultiplier *= weapon != null ? weapon.basicDamagePerHit : 1f;

        PlayAttackAnimation(swingIndex);
        SpawnPlayerHitbox(swingIndex, telegraphDuration, damageMultiplier, weapon);
        _comboStep = (_comboStep + 1) % ComboLength;
        OnAttackExecuted?.Invoke(swingIndex);
    }

    [SerializeField, Range(0, 12)] private float aimAssistDegrees = 8;
    [SerializeField, Range(0, .25f)] private float reachAssistDistance = .12f;
    private readonly Collider2D[] aimCandidates = new Collider2D[16];
    private BoxCollider2D attackShape;

    private bool TryAim(Vector2 plannedStep, out Vector2 aim, out Vector2 extraStep)
    {
        aim = CurrentAttackDir;
        extraStep = Vector2.zero;
        if (Mouse.current == null || !PixelViewport.TryPointerToWorld(Mouse.current.position.ReadValue(), out var pointer)) return false;
        float facing = pointer.x - transform.position.x;
        var scale = transform.localScale;
        if (Mathf.Abs(facing) > .001f) scale.x = -Mathf.Sign(facing) * Mathf.Abs(scale.x);
        transform.localScale = scale;
        Vector2 origin = (Vector2)(attackSpawnPoint != null ? attackSpawnPoint.position : transform.position) + plannedStep;
        aim = (pointer - origin).normalized;
        if (telegraphPrefab == null) return true;
        if (attackShape == null) attackShape = telegraphPrefab.GetComponent<BoxCollider2D>();
        if (attackShape == null) return true;
        float reach = (attackShape.offset.x + .5f * attackShape.size.x) * lightHitboxSize.x * Mathf.Abs(transform.lossyScale.x);
        ResolveAim(origin, pointer, plannedStep, reach, out aim, out extraStep);
        if (Mathf.Abs(aim.x) > .001f && Mathf.Sign(aim.x) == Mathf.Sign(transform.localScale.x))
        {
            scale.x = -Mathf.Sign(aim.x) * Mathf.Abs(scale.x);
            transform.localScale = scale;
            origin = (Vector2)(attackSpawnPoint != null ? attackSpawnPoint.position : transform.position) + plannedStep;
            ResolveAim(origin, pointer, plannedStep, reach, out aim, out extraStep);
        }
        return true;
    }

    public void ResolveAim(Vector2 origin, Vector2 pointer, Vector2 plannedStep, float reach,
        out Vector2 aim, out Vector2 extraStep)
    {
        aim = (pointer-origin).normalized;
        extraStep = Vector2.zero;
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(Layers.EnemyMask);
        if (aim.sqrMagnitude < .0001f) aim = CurrentAttackDir;
        if (TryAimAtSingleContact(origin, reach, filter, ref aim)) return;
        int count = Physics2D.OverlapCircle(pointer, .27f, filter, aimCandidates);
        Vector2 raw = aim;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var collider = aimCandidates[i];
            var health = collider.GetComponentInParent<CharacterHealth>() ?? collider.GetComponentInChildren<CharacterHealth>();
            if (health == null || health.IsDead || health.Invincible) continue;
            Vector2 near = collider.ClosestPoint(pointer);
            Vector2 towards = near - origin;
            if (towards.sqrMagnitude < .00001f) continue;
            Vector2 dir = towards.normalized;
            float angle = Vector2.Angle(raw, dir);
            if (angle > aimAssistDegrees) continue;
            Vector2 edge = collider.ClosestPoint(origin);
            float gap = Vector2.Distance(origin, edge) - reach;
            if (gap > reachAssistDistance || Physics2D.Linecast(origin, near, Layers.WallMask)) continue;
            float score = (near-pointer).sqrMagnitude + angle*.0001f;
            if (score >= best) continue;
            best = score;
            aim = dir;
            extraStep = gap > 0 && Vector2.Dot(plannedStep, dir) >= -.001f
                ? (edge-origin).normalized * Mathf.Min(reachAssistDistance,gap+.012f) : Vector2.zero;
        }
    }

    private bool TryAimAtSingleContact(Vector2 origin, float reach, ContactFilter2D filter, ref Vector2 aim)
    {
        var settings = CameraSettingsSO.Instance;
        float contactRadius = settings != null ? settings.singleEnemyContactRadius : .42f;
        if (contactRadius <= 0f) return false;
        int count = Physics2D.OverlapCircle(origin, Mathf.Max(reach + reachAssistDistance, contactRadius), filter, aimCandidates);
        if (count == aimCandidates.Length) return false;
        CharacterHealth single = null;
        float nearest = float.PositiveInfinity;
        Vector2 center = origin;
        for (int index = 0; index < count; index++)
        {
            var shape = aimCandidates[index];
            var target = shape.GetComponentInParent<CharacterHealth>() ?? shape.GetComponentInChildren<CharacterHealth>();
            if (target == null || target.IsDead || target.Invincible) continue;
            var point = shape.ClosestPoint(origin);
            if (Physics2D.Linecast(origin, point, Layers.WallMask)) continue;
            if (single != null && target != single) return false;
            single = target;
            float distance = (point - origin).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            center = shape.bounds.center;
        }
        if (single == null || nearest > contactRadius * contactRadius) return false;
        var direction = center - origin;
        aim = direction.sqrMagnitude > .00001f ? direction.normalized : CurrentAttackDir;
        return true;
    }

    private void SpawnPlayerHitbox(int swingIndex, float telegraphDuration, float damageMultiplier, EquipmentSO weapon)
    {
        if (telegraphPrefab == null)
        {
            Debug.LogError("[MeleeCombat] telegraphPrefab이 인스펙터에 할당되지 않았습니다.");
            return;
        }

        Vector2 direction = CurrentAttackDir;
        if (TryAim(Vector2.zero, out var aimed, out _)) direction = aimed;
        CurrentAttackDir = direction;

        Vector3 spawn = attackSpawnPoint != null ? attackSpawnPoint.position : transform.position;
        var hitboxObject = Instantiate(telegraphPrefab, spawn, Quaternion.identity, transform);
        if (hideBasicAttackTelegraph)
        {
            foreach (var renderer in hitboxObject.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.enabled = false;
            // An active mask would cut holes in other enemies' visible telegraphs.
            foreach (var mask in hitboxObject.GetComponentsInChildren<SpriteMask>(true))
                mask.enabled = false;
        }

        _activeHitbox = hitboxObject.GetComponent<BaseHitBox>();
        if (_activeHitbox == null)
        {
            Debug.LogError("[MeleeCombat] telegraphPrefab에 BaseHitBox 스크립트가 없습니다!");
            return;
        }

        var hitboxTransform = hitboxObject.transform;
        hitboxTransform.localPosition = attackSpawnPoint != null ? attackSpawnPoint.localPosition : Vector3.zero;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float localAngle = transform.localScale.x < 0f ? 180f - angle : angle;
        hitboxTransform.localRotation = Quaternion.Euler(0f, 0f, localAngle);
        hitboxTransform.localScale = new Vector3(lightHitboxSize.x, lightHitboxSize.y, 1f);
        _activeHitbox.hitEffectAngle = angle; // Hit effects use the world-space direction.

        var damage = new DamageInfo(_player.Stat.ATK * damageMultiplier, DamageType.Physical, gameObject, 1f, "", false,
            causesHitstun: true, knockbackForce: 2f, superArmorDamage: 20f, category: DamageCategory.BasicAttack)
        {
            playerBasicAttack = new PlayerBasicAttackRoll(weapon),
            feedbackComboFinish = swingIndex == 2
        };
        var infusionOnHit = ApplyStandbyInfusion(ref damage, swingIndex);
        _activeHitbox.HitsPerContact = weapon != null ? weapon.basicHitsPerContact : 1;
        _activeHitbox.ContactHitInterval = weapon != null ? weapon.basicRepeatHitInterval : 0f;
        _activeHitbox.Init(damage, Layers.EnemyMask, attackHitActiveDuration, telegraphDuration, onHitEnemy: infusionOnHit);
    }

    private void PlayAttackAnimation(int swingIndex)
    {
        _attackAnimationPlaying = true;
        _ownsAnimationLock = true;
        _player.LockAnimState();
        _player.ResetAnimStateCache();
        if (swingIndex == 0) _player.PlayAllAnim("Attack_Light1", "Attack");
        else if (swingIndex == 1) _player.PlayAllAnim("Attack_Light2", "Attack");
        else _player.PlayAllAnim("Attack_Medium", "Attack");
        if (_player.Stat != null)
            _player.SetAttackAnimSpeed(Mathf.Clamp(_player.Stat.ATKSPD, 0.5f, 3f));
    }

    /// <summary>
    /// 복귀 이벤트(또는 PlayerController의 누락 이벤트 안전장치)가 모션 완료를 알린다.
    /// 판정창과 최소 공격 간격이 끝날 때까지 공격 방향과 걷기 속도 배율을 유지한다.
    /// </summary>
    public bool TryFinishAttackAnimation()
    {
        if (!_ownsAnimationLock) return true;
        _attackAnimationPlaying = false;
        if (IsAttacking) return false;
        _ownsAnimationLock = false;
        return true;
    }

    /// <summary>
    /// Attack 애니메이션 클립의 실제 타격 프레임(Animation Event)에서 호출됩니다.
    /// 텔레그래프 선딜레이를 강제로 끝내고 즉시 데미지 판정을 시작합니다.
    /// (BodyAnimationEventRelay -> 여기로 전달됨)
    /// </summary>
    public void OnAttackHitFrame()
    {
        if (_activeHitbox != null) _activeHitbox.ForceActivate(); // ?. 는 Unity의 == 오버로드를 안 타서 파괴된 오브젝트를 못 거른다
    }

    /// <summary>
    /// 플레이어 평타만 취소한다. 소환수의 행동은 건드리지 않아,
    /// 3타 도중 대쉬/Q·E 를 써도 미니언은 남아서 마무리를 끝낸다.
    /// </summary>
    public void CancelPlayerAttack()
    {
        CancelAttackPreparation();
        // 이후 도착하는 평타 종료 처리로 새 대쉬/가드의 잠금을 풀지 않는다.
        _attackAnimationPlaying = false;
        _ownsAnimationLock = false;
        if (_activeHitbox != null)
        {
            Destroy(_activeHitbox.gameObject);
            _activeHitbox = null;
        }
        _comboStep = 0;
        // An attack held through a dodge or guard can resume when the gate reopens.
        _player.SetAttackAnimSpeed(1f);

        // 공격이 중간에 취소되더도 Idle/Walk 전환이 영원히 잠겨있지 않도록 해제합니다.
        _player.CanChangeAnimState();
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _player.RemoveSpeedModifier(PlayerController.SpeedModifierSource.MeleeAttack); // 이동 속도 복구
    }
}
