using UnityEngine;

/// <summary>
/// 패널 오브젝트에 붙이는 UI 사운드 컴포넌트.
/// SetActive(true/false)로 열리고 닫히는 UI에 코드 수정 없이 사운드를 붙일 수 있습니다.
///
/// 재생 조건 3가지 모두 충족 시에만 재생:
///   1. Start() 이후 (씬 초기화 단계 제외)
///   2. SoundManager 존재
///   3. UISoundController.GetIsReady() = true (로딩 완료 이후)
/// </summary>
public class UISound : MonoBehaviour
{
    [Header("클립")]
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    [Range(0f, 1f)]
    [SerializeField] private float volume = 1f;

    private bool _initialized;

    private void Start()
    {
        _initialized = true;
    }

    private void OnEnable()
    {
        if (!CanPlay() || openClip == null) return;
        SoundManager.Instance.PlaySFX(openClip, volume);
    }

    private void OnDisable()
    {
        if (!CanPlay() || closeClip == null) return;
        SoundManager.Instance.PlaySFX(closeClip, volume);
    }

    private bool CanPlay()
    {
        return _initialized
            && SoundManager.Instance != null
            && UISoundController.GetIsReady();
    }
}
