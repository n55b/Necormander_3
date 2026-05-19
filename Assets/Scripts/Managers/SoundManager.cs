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
    [SerializeField]private AudioClip HitClip;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }

        activeSource = sourceA; // 처음 시작 소스 설정
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

    public void HITSoundPlay()
    {
        if (HitClip != null) sfxSource.PlayOneShot(HitClip);
    }

    public void PlaySFX(AudioClip clip, float volume)
    {
        if(clip == null) return;
        sfxSource.PlayOneShot(clip, volume);
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
            newSource.volume = Mathf.Lerp(0, 1, t);

            yield return null;
        }

        // 페이드 완료 후 정리
        oldSource.Stop();
    }
}