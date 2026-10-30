using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 전역 사운드 매니저.
/// SFX: 고정 AudioSource 풀 — 중요 소리를 보존하고 과도한 중복은 제한.
/// PlaySFX 단일 진입점으로 중복 코드 제거.
/// </summary>
public class SoundManager : MonoBehaviour
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Min(0f), Tooltip("방 이동 시 이전 배경음에서 새 배경음으로 바뀌는 시간입니다. 0이면 즉시 전환합니다. 코드에서 시간을 명시하면 그 값이 우선합니다.")]
    private float bgmCrossfadeDuration = 1f;

    public static SoundManager Instance;

    [Header("배경음 소스")]
    public AudioSource sourceA;
    public AudioSource sourceB;
    private AudioSource _activeSource;
    public  AudioSource newSource; // CrossFade 외부 참조 호환 유지
    public  AudioSource oldSource;

    [Header("효과음 재사용 풀")]
    [Tooltip("동시 재생 허용 채널 수. 로딩 때 한 번 준비하고 전투 중 추가 생성하지 않습니다.")]
    [SerializeField] private int sfxPoolSize = 16;
    [SerializeField, Range(1, 8), Tooltip("같은 클립이 동시에 겹칠 수 있는 수. 다수 적의 같은 공격 소리가 과도하게 커지는 것을 막습니다.")]
    private int sameClipVoiceLimit = 3;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;
    private AudioSource[] _sfxPool;
    private int           _sfxPoolIndex;

    [Header("재생 클립")]
    [SerializeField] private AudioClip ParabolaClip;
    [SerializeField] private AudioClip PurchaseClip;

    [Header("전체 볼륨")]
    public float globalBgmVolume = 1f;
    public float globalSfxVolume = 1f;

    // ─────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }
        else { Destroy(gameObject); return; }

        BuildSfxPool();
        LoadSettings();
        _activeSource = sourceA;
    }

    // ─── SFX 풀 ──────────────────────────────────────────────────────
    private void BuildSfxPool()
    {
        _sfxPool = new AudioSource[Mathf.Max(1, sfxPoolSize)];
        for (int i = 0; i < _sfxPool.Length; i++)
        {
            var go           = new GameObject("SFX_Pool_" + i);
            go.transform.SetParent(transform);
            var src          = go.AddComponent<AudioSource>();
            src.playOnAwake  = false;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = sfxMixerGroup;
            _sfxPool[i]      = src;
        }
    }

    // ─── 단일 진입점 (pitch 기본값 1f) ───────────────────────────────
    public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        PlaySFXWithPitch(clip, volume, pitch);
    }

    /// <summary>음높이까지 적용해서 재생한다. (사운드 세트 · 사운드 테스트 툴용)</summary>
    public bool PlaySFXWithPitch(AudioClip clip, float volume, float pitch, int priority = 128)
    {
        if (clip == null || volume <= 0f || globalSfxVolume <= 0f) return false;
        priority = Mathf.Clamp(priority, 0, 256);
        var src    = GetNextSfxSource(clip, priority);
        if (src == null) return false;
        src.pitch  = Mathf.Clamp(pitch, 0.1f, 3f);
        src.priority = priority;
        src.volume = Mathf.Round(volume * globalSfxVolume * 100f) / 100f;
        src.clip   = clip;
        src.Play();
        return true;
    }

    // 발소리가 카운터 성공음/보스 타격음을 잘라 버리지 않도록 우선순위를 보존한다.
    private AudioSource GetNextSfxSource(AudioClip clip, int priority)
    {
        if (_sfxPool == null || _sfxPool.Length == 0) return null;
        int len = _sfxPool.Length;
        int sameClip = 0, free = -1, weakest = -1;
        for (int i = 0; i < len; i++)
        {
            int idx = (_sfxPoolIndex + i) % len;
            var candidate = _sfxPool[idx];
            if (!candidate.isPlaying)
            {
                if (free < 0) free = idx;
                continue;
            }
            if (candidate.clip == clip) sameClip++;
            if (weakest < 0 || candidate.priority > _sfxPool[weakest].priority) weakest = idx;
        }
        if (sameClip >= Mathf.Max(1, sameClipVoiceLimit)) return null;
        int chosen = free >= 0 ? free : weakest;
        if (chosen < 0 || (free < 0 && _sfxPool[chosen].priority < priority)) return null;
        _sfxPoolIndex = (chosen + 1) % len;
        return _sfxPool[chosen];
    }

    // ─── 기존 호환 ───────────────────────────────────────────────────
    public void HITSoundPlay(bool isPurchase)
        => PlaySFX(isPurchase ? PurchaseClip : ParabolaClip);

    // ─── 볼륨 ────────────────────────────────────────────────────────
    private void LoadSettings()
    {
        globalBgmVolume = PlayerPrefs.GetFloat("BGM_Volume", 1f);
        globalSfxVolume = PlayerPrefs.GetFloat("SFX_Volume", 1f);
        if (sourceA != null) sourceA.volume = globalBgmVolume;
        if (sourceB != null) sourceB.volume = globalBgmVolume;
    }

    public void UpdateBgmVolume(float volume)
    {
        globalBgmVolume = volume;
        PlayerPrefs.SetFloat("BGM_Volume", volume);
        PlayerPrefs.Save();
        if (_activeSource != null) _activeSource.volume = volume;
    }

    public void UpdateSfxVolume(float volume)
    {
        globalSfxVolume = volume;
        PlayerPrefs.SetFloat("SFX_Volume", volume);
        PlayerPrefs.Save();
    }

    // ─── BGM CrossFade ───────────────────────────────────────────────
    public void ChangeBGM(AudioClip newClip, float duration = -1f)
    {
        if (newClip == null) return;
        if (_activeSource != null && _activeSource.clip == newClip && _activeSource.isPlaying) return;

        newSource        = (_activeSource == sourceA) ? sourceB : sourceA;
        oldSource        = _activeSource;
        newSource.clip   = newClip;
        newSource.volume = 0f;
        newSource.Play();
        _activeSource = newSource;

        StopAllCoroutines();
        StartCoroutine(CrossFade(duration < 0f ? Mathf.Max(0f, bgmCrossfadeDuration) : duration));
    }


    /// <summary>
    /// 현재 나오고 있는 BGM을 서서히 끈다. 타이틀(StartScene) 등 BGM 지정이 없는 씬으로 돌아왔을 때,
    /// DontDestroyOnLoad로 살아남은 지난 씬 BGM이 그대로 계속 나오는 문제를 막는다.
    /// </summary>
    public void StopBGM(float fadeDuration = 0.5f)
    {
        StopAllCoroutines();

        if (fadeDuration <= 0f)
        {
            if (sourceA != null) { sourceA.Stop(); sourceA.volume = 0f; }
            if (sourceB != null) { sourceB.Stop(); sourceB.volume = 0f; }
            return;
        }

        StartCoroutine(FadeOutAndStop(fadeDuration));
    }

    private IEnumerator FadeOutAndStop(float duration)
    {
        float elapsed = 0f;
        float startVolA = sourceA != null ? sourceA.volume : 0f;
        float startVolB = sourceB != null ? sourceB.volume : 0f;
        float invDuration = duration > 0f ? 1f / duration : 1f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed * invDuration;
            if (sourceA != null) sourceA.volume = Mathf.Lerp(startVolA, 0f, t);
            if (sourceB != null) sourceB.volume = Mathf.Lerp(startVolB, 0f, t);
            yield return null;
        }

        if (sourceA != null) { sourceA.Stop(); sourceA.volume = 0f; }
        if (sourceB != null) { sourceB.Stop(); sourceB.volume = 0f; }
    }


    private IEnumerator CrossFade(float duration)
    {
        float elapsed     = 0f;
        float startOldVol = oldSource != null ? oldSource.volume : 0f;
        float invDuration = duration > 0f ? 1f / duration : 1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed * invDuration; // 나눗셈 → 곱셈
            if (oldSource != null) oldSource.volume = Mathf.Lerp(startOldVol, 0f, t);
            newSource.volume = Mathf.Lerp(0f, globalBgmVolume, t);
            yield return null;
        }
        if (oldSource != null) { oldSource.volume = 0f; oldSource.Stop(); }
        newSource.volume = globalBgmVolume;
    }
}
