using System.Collections;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("BGM Sources")]
    public AudioSource sourceA;
    public AudioSource sourceB;
    private AudioSource activeSource;
    // BGM 페이드 전환을 위한 변수
    public AudioSource newSource;
    public AudioSource oldSource;

    [Header("SFX Sources")]
    public AudioSource sfxSource;

    [Header("Clips")]
    [SerializeField] private AudioClip ParabolaClip;
    [SerializeField] private AudioClip PurchaseClip;

    [Header("Global Volume Settings")]
    public float globalBgmVolume = 1f;
    public float globalSfxVolume = 1f;

    private void Awake()
    {
        if (Instance == null) 
        { 
            Instance = this; 
            transform.SetParent(null); // [수정] 자식 오브젝트로 있을 경우 경고 발생 방지
            DontDestroyOnLoad(gameObject); 
        }
        else { Destroy(gameObject); }

        LoadSettings();
        activeSource = sourceA; // 처음 시작 소스 설정
    }

    public void AllStop()
    {
        sourceA.Stop();
        sourceB.Stop();
        sfxSource.Stop();
    }

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
        PlayerPrefs.SetFloat("BGM_Volume", globalBgmVolume);
        PlayerPrefs.Save();

        if (activeSource != null)
        {
            activeSource.volume = globalBgmVolume;
        }
    }

    public void UpdateSfxVolume(float volume)
    {
        globalSfxVolume = volume;
        PlayerPrefs.SetFloat("SFX_Volume", globalSfxVolume);
        PlayerPrefs.Save();
    }

    public void ChangeBGM(AudioClip newClip, float duration = 1.0f)
    {
        if (newClip == null) return;
        if (activeSource != null && activeSource.clip == newClip) return;

        // 다음에 재생할 소스 결정
        newSource = (activeSource == sourceA) ? sourceB : sourceA;
        oldSource = activeSource;

        // 1. 새 소스 세팅 및 재생 시작
        newSource.clip = newClip;
        newSource.volume = 0; // 0부터 시작
        newSource.Play();

        // 2. 활성 소스 변경
        activeSource = newSource;

        // 3. 페이드 실행 (기존 코루틴은 중지하고 새로 시작)
        StopAllCoroutines();
        StartCoroutine(CrossFade(duration));
    }

    public void HITSoundPlay(bool isPurchase)
    {
        if (isPurchase && PurchaseClip != null)
        {
            sfxSource.PlayOneShot(PurchaseClip, globalSfxVolume);
        }
        else if (ParabolaClip != null)
        {
            sfxSource.PlayOneShot(ParabolaClip, globalSfxVolume);
        }
    }

    public void PlaySFX(AudioClip clip, float volume)
    {
        if(clip == null) return;
        sfxSource.PlayOneShot(clip, volume * globalSfxVolume);
    }

    private IEnumerator CrossFade(float duration)
    {
        float time = 0;
        float startOldVol = (oldSource != null) ? oldSource.volume : 0;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;

            // 기존 소리는 작게, 새 소리는 크게
            if (oldSource != null) oldSource.volume = Mathf.Lerp(startOldVol, 0, t);
            newSource.volume = Mathf.Lerp(0, globalBgmVolume, t);

            yield return null;
        }

        // 페이드 완료 후 정리
        oldSource.Stop();
    }
}