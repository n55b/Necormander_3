using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;
    private CinemachineCamera rig;
    private ImpactCameraOffset offset;
    private float defaultLens;
    private ImpactProfile pending;
    private Vector2 pendingDirection;
    private float pendingStrength, pendingStop;
    private readonly Dictionary<(long, int, int), (int frame, int priority)> recent = new();
    private readonly Queue<(long, int, int)> history = new();
#if UNITY_EDITOR
    private readonly Queue<string> trace = new();
    public IEnumerable<string> Trace => trace;
#endif
    private void Record(ImpactKind kind, string result)
    {
#if UNITY_EDITOR
        if (trace.Count == 12) trace.Dequeue();
        trace.Enqueue($"{Time.unscaledTime:F2}  {kind}  {result}");
#endif
    }

    private void OnEnable()
    {
        Initialize();
        DamageEventBus.OnAttackHitResolved += OnHit;
        DamageEventBus.OnDamageReceived += OnHurt;
        DamageEventBus.OnEntityDied += OnDeath;
    }

    private static Vector2 Direction(CharacterHealth target, DamageInfo hit)
    {
        Vector2 origin = hit.hitFrom ?? (hit.attacker != null ? (Vector2)hit.attacker.transform.position : (Vector2)target.transform.position);
        return (Vector2)target.transform.position - origin;
    }

    private void OnHit(CharacterHealth target, CharacterStat attacker, DamageInfo hit, bool critical)
    {
        if (Instance != this || target == null || !DamageRules.IsPlayerSourced(hit.category) || hit.category == DamageCategory.Debuff) return;
        var kind = hit.feedbackComboFinish ? ImpactKind.Finisher : critical ? ImpactKind.Critical :
            hit.minionSource != null ? ImpactKind.Summon : ImpactKind.Normal;
        var key = (hit.impactId, hit.impactPulse, hit.feedbackHitIndex);
        var settings = CameraSettingsSO.Instance;
        if (offset != null && settings != null && PolishFeedback.Catalog != null && PolishFeedback.Catalog.enabled &&
            !HitStopManager.IsPaused && hit.minionSource == null && !hit.isRedirected &&
            attacker != null && GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null &&
            attacker == GameManager.Instance.PLAYERCONTROLLER.Stat && !recent.ContainsKey(key))
            offset.PushZoom(hit.feedbackComboFinish ? 1.5f : 1f);
        Request(kind, Direction(target, hit), hit, hit.impactStrength ?? 1);
    }

    private void OnHurt(CharacterHealth target, DamageInfo hit)
    {
        if (Instance == this && target != null && target.GetComponentInParent<PlayerController>() != null)
            Request(ImpactKind.PlayerHurt, Direction(target, hit), hit);
    }

    private void OnDeath(CharacterHealth target, DamageInfo hit)
    {
        if (Instance != this || target == null) return;
        var entity = target.GetComponentInParent<BaseEntity>();
        if (entity != null && entity.team == Team.Enemy && entity.MinionData != null && entity.MinionData.tier == EnemyTier.Boss)
            Request(ImpactKind.BossDeath, Direction(target, hit), hit);
    }

    public void Initialize()
    {
        Instance = this;
        if (offset != null || !TryGetComponent(out rig)) return;
        // Initialize the hit-history buffers before the first simultaneous contacts.
        var initial = (0L, 0, 0);
        recent[initial] = default;
        recent.Remove(initial);
        history.Enqueue(initial);
        history.Dequeue();
        if (!TryGetComponent(out offset)) offset = gameObject.AddComponent<ImpactCameraOffset>();
        defaultLens = CameraSettingsSO.Instance != null ? CameraSettingsSO.Instance.orthographicSize : rig.Lens.OrthographicSize;
        if (!TryGetComponent<HitStopManager>(out _)) gameObject.AddComponent<HitStopManager>();
    }

    public void WarpCamera(Transform target, Vector3 delta)
    {
        ResetEffects();
        if (rig != null) rig.OnTargetObjectWarped(rig.Follow != null ? rig.Follow : target, delta);
    }

    public void SetZoom(bool zoomOut)
    {
        if (rig == null) return;
        var lens = rig.Lens;
        float normal = CameraSettingsSO.Instance != null ? CameraSettingsSO.Instance.orthographicSize : defaultLens;
        float nativeLens = PixelViewport.Height / (2f * Mathf.Max(1, CameraSettingsSO.Instance?.assetsPPU ?? 32));
        lens.OrthographicSize = zoomOut ? nativeLens / Mathf.Max(1, Mathf.RoundToInt(nativeLens / normal) - 1) : normal;
        rig.Lens = lens;
    }

    public void SetPixelZoom(int scale)
    {
        if (rig == null) return;
        var lens = rig.Lens;
        lens.OrthographicSize = PixelViewport.Height / (2f * Mathf.Max(1, CameraSettingsSO.Instance?.assetsPPU ?? 32) * Mathf.Clamp(scale, 1, 8));
        rig.Lens = lens;
    }

    public void HitShakeCamera(float force = 1) => DirectionalKick(Vector2.up, force);
    public void DirectionalKick(Vector2 direction, float force)
    {
        if (force > 0) Request(ImpactKind.Normal, direction, strength: force);
    }

    public void Request(ImpactKind kind, Vector2 direction, DamageInfo hit = default, float strength = 1f)
    {
        var catalog = PolishFeedback.Catalog;
        if (catalog == null || !catalog.enabled || Time.timeScale <= 0) return;
        var profile = catalog.Impact(kind);
        if (profile == null) return;
        var key = (hit.impactId, hit.impactPulse, hit.feedbackHitIndex);
        if (hit.impactId != 0)
        {
            if (recent.TryGetValue(key, out var previous))
            {
                if (profile.priority <= previous.priority || (previous.frame != Time.frameCount && kind != ImpactKind.BossDeath))
                {
                    Record(kind, "동일 타격 병합");
                    return;
                }
            }
            else
            {
                history.Enqueue(key);
                if (history.Count > 512) recent.Remove(history.Dequeue());
            }
            recent[key] = (Time.frameCount, profile.priority);
        }

        float stop = Mathf.Max(hit.impactStopSeconds, profile.stopMs * .001f);
        if (pending != null && (pending.priority > profile.priority ||
            (pending.priority == profile.priority && pending.pixels * pendingStrength >= profile.pixels * strength)))
        {
            pendingStop = Mathf.Max(pendingStop, stop);
            Record(kind, "같은 프레임 병합");
            return;
        }
        if (pending != null) Record(pending.kind, "높은 우선순위로 교체");
        pending = profile;
        pendingDirection = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.up;
        pendingStrength = strength;
        pendingStop = Mathf.Max(pendingStop, stop);
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0 || !PolishFeedback.Catalog || !PolishFeedback.Catalog.enabled)
        { ResetEffects(); return; }
        if (offset != null && (!PolishFeedback.Enabled(FeedbackCue.CameraKick) ||
            !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.CameraShake))) offset.pixels = 0;
        if (pending == null) return;
        var profile = pending;
        pending = null;
        var catalog = PolishFeedback.Catalog;
        float now = Time.unscaledTime;
        bool cameraEnabled = offset != null && PolishFeedback.Enabled(FeedbackCue.CameraKick) &&
            PolishFeedback.ExistingEnabled(ExistingFeedbackCue.CameraShake);
        bool cameraApplied = cameraEnabled && (offset.pixels == 0 || now >= offset.started + offset.duration || offset.priority <= profile.priority);
        if (cameraApplied)
        {
            offset.direction = Quaternion.Euler(0, 0, profile.angle) * (Vector3)pendingDirection;
            offset.started = now;
            offset.priority = profile.priority;
            offset.pixels = Mathf.Clamp(profile.pixels * pendingStrength * catalog.Find(FeedbackCue.CameraKick).intensity, 0, 12);
            offset.duration = profile.shakeMs * .001f;
            offset.decay = Mathf.Max(1, profile.decay);
        }
        if (pendingStop > 0 && PolishFeedback.Enabled(FeedbackCue.HitStop))
            HitStopManager.Instance?.DoHitStop(pendingStop * catalog.Find(FeedbackCue.HitStop).intensity);
        if (profile.kind == ImpactKind.RoomFinish || profile.kind == ImpactKind.BossLanding || profile.kind == ImpactKind.BossDeath)
            CombatHudMotion.GroundImpact(profile.pixels * pendingStrength);
        if (profile.kind == ImpactKind.BossDeath)
        {
            HitStopManager.Instance?.DoSlowMotion(catalog.slowMotionSeconds, catalog.slowMotionScale);
        }
        pendingStop = 0;
        Record(profile.kind, cameraApplied ? "적용" : cameraEnabled ? "더 강한 반동 유지" : "카메라 효과 꺼짐");
    }

    private void ResetEffects()
    {
        pending = null;
        pendingStop = 0;
        if (offset != null) { offset.pixels = 0; offset.ClearZoom(); }
    }

    private void OnDisable()
    {
        ResetEffects();
        DamageEventBus.OnAttackHitResolved -= OnHit;
        DamageEventBus.OnDamageReceived -= OnHurt;
        DamageEventBus.OnEntityDied -= OnDeath;
        recent.Clear();
        history.Clear();
        if (Instance == this) Instance = null;
    }
}
