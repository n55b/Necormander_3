using UnityEngine;

// The scene clock composes a bounded impact stop followed by any pending slow motion.
public class HitStopManager : MonoBehaviour
{
    [System.Flags]
    public enum PauseOwner { Gameplay = 1, Options = 2, Reward = 4, GameOver = 8 }
    private static PauseOwner pauseOwners;
    public static bool IsPaused => pauseOwners != 0;
    public static HitStopManager Instance;
    [SerializeField] private float defaultDuration = .08f;
    [Range(.001f, 1f), SerializeField] private float freezeScale = .02f;
    [Min(.001f), SerializeField] private float maxDuration = .15f;
    private float stopUntil, stopLimit, recoveryUntil, slowSeconds, slowScale, slowRelease, lastTick;
    private float baseScale = 1, applied = 1;
    private bool active;

    public void Initialize()
    {
        if (Instance != null && Instance != this) Instance.ReleaseClock();
        Instance = this;
    }
    private void OnEnable() => Initialize();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForScene()
    {
        if (Instance != null) Instance.Clear();
        pauseOwners = 0;
        Time.timeScale = 1;
    }

    public static void SetPaused(bool paused, PauseOwner owner = PauseOwner.Gameplay)
    {
        var next = paused ? pauseOwners | owner : pauseOwners & ~owner;
        if (next == pauseOwners) return;
        pauseOwners = next;
        if (Instance != null) Instance.Clear();
        Time.timeScale = IsPaused ? 0f : 1f;
    }

    private void Begin(float now)
    {
        if (active) return;
        active = true;
        baseScale = Time.timeScale;
        lastTick = now;
    }

    public void DoHitStop(float duration = -1)
    {
        if (Time.timeScale <= 0 || !PolishFeedback.ExistingEnabled(ExistingFeedbackCue.HitStop)) return;
        float now = Time.unscaledTime;
        float seconds = duration < 0 ? defaultDuration : duration;
        if (seconds <= 0 || maxDuration <= 0) return;
        // A saturated burst must give simulation at least one physics step to recover.
        if (now >= stopLimit && now < recoveryUntil) return;
        if (now >= stopUntil) stopLimit = now + maxDuration;
        Begin(now);
        stopUntil = Mathf.Min(stopLimit, Mathf.Max(stopUntil, now + seconds));
        if (stopUntil >= stopLimit) recoveryUntil = stopLimit + Time.fixedDeltaTime;
        Apply(Mathf.Min(baseScale, Mathf.Max(.001f, freezeScale)));
    }

    public void DoSlowMotion(float duration, float scale)
    {
        if (Time.timeScale <= 0 || duration <= 0 || !PolishFeedback.Enabled(FeedbackCue.SlowMotion)) return;
        Begin(Time.unscaledTime);
        slowScale = slowSeconds > 0 ? Mathf.Min(slowScale, scale) : scale;
        slowScale = Mathf.Clamp(slowScale, .05f, 1);
        slowSeconds = Mathf.Max(slowSeconds, Mathf.Min(1.2f, duration));
        slowRelease = .45f * slowSeconds;
        if (Time.unscaledTime >= stopUntil) Apply(Mathf.Min(baseScale, slowScale));
    }

    private void Update()
    {
        if (!active) return;
        if (!Mathf.Approximately(applied, Time.timeScale)) { Clear(); return; }
        float now = Time.unscaledTime;
        if (!PolishFeedback.ExistingEnabled(ExistingFeedbackCue.HitStop)) stopUntil = 0;
        if (!PolishFeedback.Enabled(FeedbackCue.SlowMotion)) slowSeconds = 0;
        if (now >= stopUntil)
        {
            slowSeconds -= now - Mathf.Max(lastTick, stopUntil);
            if (slowSeconds > 0)
            {
                float t = Mathf.Clamp01(1f - slowSeconds / slowRelease);
                Apply(Mathf.SmoothStep(Mathf.Min(baseScale, slowScale), baseScale, t));
            }
            else { Apply(baseScale); Clear(); }
        }
        lastTick = now;
    }

    private void Apply(float scale) { applied = scale; Time.timeScale = scale; }

    private void Clear()
    {
        active = false;
        stopUntil = slowSeconds = 0;
    }

    private void ReleaseClock()
    {
        if (active && Mathf.Approximately(Time.timeScale, applied)) Apply(baseScale);
        Clear();
    }

    private void OnDisable()
    {
        if (Instance != this) return;
        ReleaseClock();
        Instance = null;
    }
}
