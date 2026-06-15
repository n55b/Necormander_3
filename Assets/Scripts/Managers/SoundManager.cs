using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 전역 사운드 매니저.
/// SFX: 라운드로빈 AudioSource 풀 — 동시 재생 씹힘 없음.
/// PlaySFX 단일 진입점으로 중복 코드 제거.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("BGM Sources")]
    public AudioSource sourceA;
    public AudioSource sourceB;
    private AudioSource _activeSource;
    public  AudioSource newSource; // CrossFade 외부 참조 호환 유지
    public  AudioSource oldSource;

    [Header("SFX Pool")]
    [Tooltip("동시 재생 허용 채널 수 (권장 8)")]
    [SerializeField] private int sfxPoolSize = 8;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;
    private AudioSource[] _sfxPool;
    private int           _sfxPoolIndex;

    [Header("Clips")]
    [SerializeField] private AudioClip ParabolaClip;
    [SerializeField] private AudioClip PurchaseClip;

    [Header("Global Volume")]
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
        _sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
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
        if (clip == null) return;
        var src    = GetNextSfxSource();
        src.pitch  = pitch;
        src.volume = volume * globalSfxVolume; // Clamp01은 Audio 엔진이 처리
        src.clip   = clip;
        src.Play();
    }

    // 빈 슬롯 우선, 전부 재생 중이면 라운드로빈으로 덮어씀
    private AudioSource GetNextSfxSource()
    {
        int len = _sfxPool.Length;
        for (int i = 0; i < len; i++)
        {
            int idx = (_sfxPoolIndex + i) % len;
            if (!_sfxPool[idx].isPlaying)
            {
                _sfxPoolIndex = (idx + 1) % len;
                return _sfxPool[idx];
            }
        }
        // 전부 재생 중 → 현재 인덱스 덮어쓰고 인덱스 전진
        var src = _sfxPool[_sfxPoolIndex];
        _sfxPoolIndex = (_sfxPoolIndex + 1) % len;
        return src;
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
    public void ChangeBGM(AudioClip newClip, float duration = 1f)
    {
        if (newClip == null) return;
        if (_activeSource != null && _activeSource.clip == newClip) return;

        newSource        = (_activeSource == sourceA) ? sourceB : sourceA;
        oldSource        = _activeSource;
        newSource.clip   = newClip;
        newSource.volume = 0f;
        newSource.Play();
        _activeSource = newSource;

        StopAllCoroutines();
        StartCoroutine(CrossFade(duration));
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
