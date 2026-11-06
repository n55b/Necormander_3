using UnityEngine;

public class MeleeDodgeController : MonoBehaviour
{
    [Header("대쉬 설정")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [Header("소환수 대쉬 공격 기본 히트박스 (소환수 SO 에 없을 때 사용)")]
    [SerializeField] private BaseHitBox dashHitBoxPrefab;
    [SerializeField] private BaseHitBox originHitBoxPrefab;
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeTime = 2.0f; // 스택 회복 시간
    
    [Header("대쉬 이펙트")]
    [SerializeField] private GameObject dashEffectPrefab; // 대쉬 이펙트 프리팹
    [SerializeField, Tooltip("위쪽으로 대시할 때 재생할 이펙트 Animator 상태 이름입니다. 원본 Aseprite 태그 이름을 사용합니다.")]
    private string upwardEffectState = "Up";
    [SerializeField, Tooltip("아래쪽 또는 수평으로 대시할 때 재생할 이펙트 Animator 상태 이름입니다. 좌우는 대시 방향에 맞춰 반전합니다.")]
    private string downwardEffectState = "Down";

    private int _currentCharges;
    private float _rechargeTimer;
    private int _lastMaxCharges;
    private float _lastRechargeTime;

    private bool _isDashing;
    private float _dashTimeLeft;
    private Vector2 _dashDir;
    private Vector2 _dashEndPosition;
    private Vector2 _dashStartPosition;
    private float _dashTravelDuration;
    private float _dashElapsed;
    private float _dashProgress;

    private PlayerController _player;
    private Rigidbody2D _rb;

    public bool IsDashing => _isDashing;
    /// <summary>TryDash 성공 시 발생</summary>
    public event System.Action OnDodgeStarted;
    private DashAfterimage _afterimage;

    public int CurrentCharges => _currentCharges;

    private void Awake()
    {
        _afterimage = GetComponent<DashAfterimage>();
        _player = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody2D>();
        _currentCharges = _lastMaxCharges = MaxCharges;
        _lastRechargeTime = EffectiveRechargeTime;
    }

    private void Update()
    {
        SyncItemBonuses();
        // 쿨타임(스택) 회복
        if (_currentCharges < MaxCharges)
        {
            _rechargeTimer -= Time.deltaTime;
            if (_rechargeTimer <= 0f)
            {
                _currentCharges++;
                if (_currentCharges < MaxCharges)
                {
                    _rechargeTimer = EffectiveRechargeTime;
                }
            }
        }

    }

    private void FixedUpdate()
    {
        if (_isDashing && _rb != null)
        {
            _dashElapsed += Time.fixedDeltaTime;
            bool finished = _player.AdvanceDash(_dashStartPosition, _dashEndPosition, _dashElapsed, _dashTravelDuration, ref _dashProgress);
            _dashTimeLeft -= Time.fixedDeltaTime;
            if (finished || _dashTimeLeft <= 0f) EndDash();
        }
    }

    public bool TryDash(Vector2 moveInput, float currentFacingSign)
    {
        SyncItemBonuses();
        if (_isDashing || _currentCharges <= 0) return false;

        // 스택 차감 및 타이머 시작
        if (_currentCharges == MaxCharges)
        {
            _rechargeTimer = EffectiveRechargeTime;
        }
        _currentCharges--;

        StartDash(moveInput, currentFacingSign);
        return true;
    }

    public GameObject DashEffectPrefab => dashEffectPrefab;

    private void PlayDashEffect()
    {
        if (dashEffectPrefab == null || !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.DashSmoke)) return;
        var data = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        GameObject fx = data != null ? data.TakePreparedObject(dashEffectPrefab, transform.position) : null;
        if (fx == null) fx = Instantiate(dashEffectPrefab, transform.position, Quaternion.identity);
        Vector3 scale = fx.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (_dashDir.x < 0f ? 1f : -1f);
        fx.transform.localScale = scale;
        var animator = fx.GetComponent<Animator>();
        string state = _dashDir.y > 0f ? upwardEffectState : downwardEffectState;
        if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(state)
            && animator.HasState(0, Animator.StringToHash(state)))
            animator.Play(state, 0, 0f);
    }

    private void StartDash(Vector2 moveInput, float currentFacingSign)
    {
        // 플레이어 평타만 캔슬한다 — 미니언 마무리는 남긴다(미니언은 R끼리만 회수). 스킬 시전도 취소.
        var meleeCtrl = _player.GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking)
        {
            meleeCtrl.CancelPlayerAttack();
        }


        _isDashing = true;

        // 입력 방향이 없으면 바라보는 방향으로 (Instantiate보다 먼저 계산해야 이펙트 방향을 알 수 있음)
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-Mathf.Sign(currentFacingSign), 0).normalized;
        }

        PlayDashEffect();

        // [추가] Unsteppable 안전 체크 및 대시 도달 범위 축소
        float baseDist = dashSpeed * dashDuration * (_player.Stat != null ? _player.Stat.DASH_DISTANCE_MULTIPLIER : 1f);
        float originalDist = baseDist;

        // 평소에는 기존 거리 그대로. 그 거리 안에 물(Unsteppable)이 끼어 있을 때만
        // 기본 대시의 1.5배까지 뻗어서 건너편 Ground 착지를 시도한다.
        float requestedDist = originalDist;
        Vector2 start = _rb.position;
        MapGenerator map = MapGenerator.Instance;
        if (map != null && map.HasUnsteppableBetween(start, _dashDir, originalDist))
            requestedDist = Mathf.Max(originalDist, baseDist * 1.5f);

        Vector2 safePos = _player.GetSafeDashPosition(start, _dashDir, requestedDist);
        _dashEndPosition = safePos;
        float actualDist = Vector2.Distance(start, safePos);
        if (actualDist > 0.0001f) _dashDir = (safePos - start).normalized;
        _dashStartPosition = _rb.position;
        _dashTravelDuration = actualDist / Mathf.Max(0.01f, dashSpeed);
        _dashElapsed = _dashProgress = 0f;
        _dashTimeLeft = _dashTravelDuration + 0.1f; // 충돌로 도착 불가할 때만 쓰는 안전 종료
        _afterimage?.BeginDash(_dashTimeLeft);

        if (_player.Stat != null && _player.Stat.Health != null)
        {
            _player.Stat.Health.Invincible = true; // 대쉬 무적
        }

        _player.SetDashLayer(true); // set dash layer

        // Lock the Idle/Walk auto-transition for the ACTUAL dash duration, otherwise
        // PlayerController.Update() overwrites the Dash pose one frame later.
        _player.LockAnimState(_dashTimeLeft + 0.1f);
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Dash", "Idle");
        OnDodgeStarted?.Invoke();
    }

    // ─── 소환수 대쉬 공격 (Space 스왑) ─────────────────────────────
    // 플레이어 대쉬와는 무관하다. 소환수가 플레이어 위치에서 마우스 방향으로 대쉬 공격을 한다.
    public bool CanCastSummonDashAttack(MainMinionDataSO main)
    {
        var mod = main != null ? main.dashModifier : null;
        if (mod == null || !mod.DealsDamage) return false;
        if (mod.hitBoxPrefab != null) return true;
        return (mod.hitAtOrigin ? originHitBoxPrefab : dashHitBoxPrefab) != null;
    }

    public MinionSkillCaster CastSummonDashAttack(MainMinionDataSO main)
    {
        if (!CanCastSummonDashAttack(main) || _player == null || _player.Stat == null) return null;
        var mod = main.dashModifier;
        Vector2 origin = transform.position;
        Vector2 dir = Vector2.right;
        if (Camera.main != null && UnityEngine.InputSystem.Mouse.current != null)
        {
            Vector2 aim = (Vector2)Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue()) - origin;
            if (aim.sqrMagnitude > 0.0001f) dir = aim.normalized;
        }

        float wanted = dashSpeed * dashDuration * Mathf.Max(0.01f, mod.lengthMultiplier);
        float dist = mod.hitAtOrigin ? wanted : Vector2.Distance(origin, SkillCombatUtil.GetSafeDestination(origin, dir, wanted));
        Vector2 end = origin + dir * dist;

        var caster = MinionSkillCaster.Spawn(main, origin);
        float life = SpawnSummonDashHitBox(main, mod, caster, origin, dir, dist);
        if (!PlaySummonDashVisual(caster, main, mod, origin, end, dir))
        {
            // 연출 없는 소환수는 판정 시간만큼 있다가 끝 지점에서 이어간다.
            if (!mod.hitAtOrigin) caster.transform.position = end;
            caster.SetLifetime(life);
        }
        return caster;
    }

    private float SpawnSummonDashHitBox(MainMinionDataSO main, MinionDashModifier mod, MinionSkillCaster caster, Vector2 origin, Vector2 dir, float dist)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float dmg = _player.Stat.ATK * mod.damageMultiplier * MinionEnhance.DashDamageMult(main);
        var weapon = EquipmentInventoryManager.Instance != null ? EquipmentInventoryManager.Instance.Weapon : null;
        dmg *= weapon != null ? weapon.minionDashDamageMultiplier : 1f;
        StatusType? status = mod.onHitStatus == StatusType.None ? (StatusType?)null : mod.onHitStatus;
        var info = new DamageInfo(dmg, mod.element, _player.gameObject, 1f, "Dash",
            causesHitstun: true, knockbackForce: mod.pushesEnemies ? mod.pushForce : 0f,
            category: DamageCategory.DashAttack, applyStatus: status);
        info.minionSource = caster;

        System.Action<CharacterHealth> onHit = null;
        if (mod.pushesEnemies)
        {
            onHit = (health) =>
            {
                var entity = health.GetComponentInParent<BaseEntity>();
                if (entity != null) entity.ApplyKnockback(dir * mod.pushForce);
            };
        }

        BaseHitBox box;
        float life;
        if (mod.hitAtOrigin)
        {
            var prefab = mod.hitBoxPrefab != null ? mod.hitBoxPrefab : originHitBoxPrefab;
            box = Instantiate(prefab, origin, Quaternion.identity);
            box.transform.localScale = new Vector3(mod.originRadius * 2f, mod.originRadius * 2f, 1f);
            life = 0.15f;
        }
        else
        {
            var prefab = mod.hitBoxPrefab != null ? mod.hitBoxPrefab : dashHitBoxPrefab;
            box = Instantiate(prefab, origin, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(Mathf.Max(0.01f, dist), mod.width, 1f);
            life = Mathf.Max(0.05f, dist / Mathf.Max(0.01f, dashSpeed));
        }
        box.hitEffectAngle = angle;
        box.isContinuousDamage = mod.hitCount > 1;
        if (mod.hitCount > 1) box.damageTickRate = life / mod.hitCount;
        main.ApplyHitEffect(box);
        box.Init(info, Layers.EnemyMask, life, 0f, true, onHit);
        return life;
    }

    /// <returns>연출을 재생했으면 true.</returns>
    private static bool PlaySummonDashVisual(MinionSkillCaster caster, MainMinionDataSO main, MinionDashModifier mod, Vector2 origin, Vector2 end, Vector2 dir)
    {
        var da = main.dashAnim;
        if (da == null || !da.HasVisual) return false;
        Vector2 center = (origin + end) * 0.5f;
        float? hitBoxRot = da.rotateHitBoxClipToDir ? (float?)(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg) : null;
        var steps = new System.Collections.Generic.List<(string clip, Vector3 pos, float? rotZ)>();
        if (mod.hitAtOrigin)
        {
            string clip = !string.IsNullOrWhiteSpace(da.startClip) ? da.startClip
                        : !string.IsNullOrWhiteSpace(da.hitBoxClip) ? da.hitBoxClip
                        : da.endClip;
            if (!string.IsNullOrWhiteSpace(clip)) steps.Add((clip, (Vector3)origin, null));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(da.startClip))  steps.Add((da.startClip,  (Vector3)origin, null));
            if (!string.IsNullOrWhiteSpace(da.hitBoxClip)) steps.Add((da.hitBoxClip, (Vector3)center, hitBoxRot));
            if (!string.IsNullOrWhiteSpace(da.endClip))    steps.Add((da.endClip,    (Vector3)end,    null));
        }
        if (steps.Count == 0) return false;
        caster.PlayDashSequence(da.visual, steps, da.clipDuration, dir.x >= 0f);
        return true;
    }

    private void EndDash()
    {
        _afterimage?.EndDash();
        _isDashing = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            // 목적지를 향해 매 물리 틱 제한 이동한다. 종료 시 목적지로 순간이동하지 않는다.
        }

        if (_player.Stat != null && _player.Stat.Health != null)
        {
            _player.Stat.Health.Invincible = false;
        }

        _player.SetDashLayer(false); // restore original layer

        _player.CanChangeAnimState(); // unlocks canChangeState and cancels the LockAnimState timeout
        _player.ResetAnimStateCache();
        _player.PlayAllAnim("Idle");
        _player.ResumeLocomotionAfterDash();
    }


public int   MaxCharges      => Mathf.Max(1, maxCharges + (_player != null && _player.Stat != null
    ? Mathf.FloorToInt(_player.Stat.Mods.Flat(StatType.DashCharges)) : 0));
    public float RechargeTime    => rechargeTime;

    /// <summary>대쉬 쿨감(DASH_CDR)이 반영된 실제 재충전 시간. 스탯 없으면 원본 rechargeTime.</summary>
    private float EffectiveRechargeTime
        => (_player != null && _player.Stat != null) ? _player.Stat.ApplyDashCooldown(rechargeTime) : rechargeTime;

    public float RechargeProgress
    {
        get
        {
            float rt = EffectiveRechargeTime;
            return (_currentCharges < MaxCharges && rt > 0f)
                ? 1f - Mathf.Clamp01(_rechargeTimer / rt)
                : 1f;
        }
    }

    private void SyncItemBonuses()
    {
        int max = MaxCharges;
        _currentCharges = Mathf.Clamp(_currentCharges + Mathf.Max(0, max - _lastMaxCharges), 0, max);
        _lastMaxCharges = max;
        float time = EffectiveRechargeTime;
        if (_lastRechargeTime > 0f && !Mathf.Approximately(time, _lastRechargeTime))
            _rechargeTimer *= time / _lastRechargeTime;
        _lastRechargeTime = time;
    }
}
