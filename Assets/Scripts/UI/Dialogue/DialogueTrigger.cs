using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 인스펙터 배선용 대화 트리거.
///
/// <see cref="Play"/> 가 인자를 받지 않으므로 UnityEvent 슬롯에 그대로 잡힌다.
/// BossRoomEvent 의 OnBossCombatStart / OnBossCombatClear, 문·포탈의 FadeAction 등
/// 이미 있는 UnityEvent 어디에든 이 컴포넌트를 끌어다 놓으면 대화가 붙는다.
/// (이 프로젝트는 '드래그로 이을 수 있으면 UnityEvent' 가 규약이다 — FadeAction.cs 참고)
///
/// 방에 들어가자마자 자동 재생하려면 playOnEnable 을 켜고 오브젝트를 켜면 된다.
/// </summary>
public class DialogueTrigger : MonoBehaviour
{
    [Header("대화")]
    [Tooltip("재생할 대화 id. CSV 의 id 칸 값과 같아야 한다.")]
    [SerializeField] private string dialogueId;

    [Tooltip("한 번만 재생하고 그 뒤로는 무시한다. 방을 다시 들어와도 안 뜨게 하려는 것.")]
    [SerializeField] private bool playOnce = true;

    [Tooltip("이 오브젝트가 켜질 때 자동으로 재생한다.")]
    [SerializeField] private bool playOnEnable = false;

    [Header("끝난 뒤")]
    [Tooltip("대화가 끝나면 실행할 것들. 보스 스폰, 문 열기 등을 여기 건다.")]
    [SerializeField] private UnityEvent onDialogueComplete;

    private bool _played;

    private void OnEnable()
    {
        if (playOnEnable) Play();
    }

    /// <summary>UnityEvent 슬롯에서 부르는 진입점.</summary>
    public void Play()
    {
        if (playOnce && _played) return;
        if (DialogueUI.Instance == null)
        {
            Debug.LogWarning($"<color=orange>[DialogueTrigger]</color> 씬에 DialogueUI 가 없다. '{dialogueId}' 를 건너뛴다.");
            onDialogueComplete?.Invoke();
            return;
        }

        _played = true;
        DialogueUI.Instance.Play(dialogueId, () => onDialogueComplete?.Invoke());
    }

    /// <summary>playOnce 를 다시 열어준다. 런 재시작 같은 데서 쓸 것.</summary>
    public void ResetPlayed()
    {
        _played = false;
    }
}
