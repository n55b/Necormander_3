using System.Collections;
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
/// 방 프리팹에 붙이면 RoomInstance 가 입장 시 Play 를 호출한다. 그 외에는 기존 UnityEvent 에 연결한다.
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

    [Tooltip("Play 호출 후 실제 대화를 띄울 때까지 기다리는 시간(실시간 초). 방 이동 페이드가 끝난 뒤 띄울 때 사용한다.")]
    [SerializeField] private float entryDelay = 0f;

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
        _played = true;

        if (entryDelay > 0f)
        {
            StartCoroutine(PlayAfterDelay());
            return;
        }

        PlayNow();
    }

    private IEnumerator PlayAfterDelay()
    {
        while (GameManager.Instance != null && !GameManager.Instance.IsPlayerReady)
            yield return null;

        yield return new WaitForSecondsRealtime(entryDelay);
        PlayNow();
    }

    private void PlayNow()
    {
        if (DialogueUI.Instance == null)
        {
            _played = false;
            Debug.LogWarning($"<color=orange>[DialogueTrigger]</color> 씬에 DialogueUI 가 없다. '{dialogueId}' 를 건너뛴다.");
            onDialogueComplete?.Invoke();
            return;
        }

        DialogueUI.Instance.Play(dialogueId, () => onDialogueComplete?.Invoke());
    }

    /// <summary>playOnce 를 다시 열어준다. 런 재시작 같은 데서 쓸 것.</summary>
    public void ResetPlayed()
    {
        _played = false;
    }
}
