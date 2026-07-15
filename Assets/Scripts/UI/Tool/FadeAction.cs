using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// "화면 가리고 → 그 사이에 이거 하고 → 다시 밝히기" 를 인스펙터에서 만드는 컴포넌트.
///
/// 이 컴포넌트 하나가 '무슨 일이 언제 일어나는지'를 통째로 들고 있다. Fader에는 아무것도 연결하지 않는다.
/// Fader는 '어떻게 보일지'(시간/색/커브)만 알고, 여기는 '무슨 일이 일어날지'만 안다.
///
/// 문(DoorController)이 코드로 하는 것과 정확히 같은 모양이다:
///     fader.FadeOutIn(FadeSignal.방이동, () => 텔레포트(), ...)
///                     ↑ 어떤 페이드     ↑ 가려진 사이에 할 일
/// 문은 텔레포트 대상이 런타임에만 정해져서 코드로 하고, 여기는 드래그로 한다. 구조는 같다.
///
/// 붙는 자리는 상관없다 — Fader와 달리 이건 항상 커튼(화면 전체)을 직접 찾아 쓴다. 정리용으로 빈 오브젝트를 권장할 뿐.
///
/// [쓰는 순서]
///   1. FadeSignal에 종류가 있는지 확인. 없으면 프로그래머에게 한 줄 추가 요청.
///   2. ScreenFadeCanvas 프리팹의 Fader 반응 목록에 그 종류 줄을 만들고 시간/유지/색을 정한다.
///      ★ 안 만들면 경고와 함께 맨 윗줄 수치로 돈다.
///   3. 이 컴포넌트에서 signal를 고르고, onBlackout에 '씬에 있는' 오브젝트를 드래그.
///   4. 트리거가 될 UnityEvent(방 이벤트, 버튼 등)에 이 오브젝트를 드래그 → Play().
///
/// ※ 코드가 런타임에 소환하는 대상(예: BossRoomEvent가 뽑는 보스)은 에디터에서 드래그할 수 없다.
///   그런 건 소환하는 코드 쪽에서 fader.FadeOutIn(...)을 쓰는 게 맞다.
/// </summary>
public class FadeAction : MonoBehaviour
{
    [Tooltip("어떤 페이드로 가릴지. 실제 시간/색/커브는 ScreenFadeCanvas의 Fader에서 이 이름의 줄을 찾아 쓴다. " +
             "여긴 '무슨 일'만, 거긴 '어떻게 보일지'만.")]
    [SerializeField] private FadeSignal signal = FadeSignal.방이동;

    [Tooltip("화면이 완전히 가려진 순간에 실행된다. = 아무도 못 보는 사이에 처리되므로 바뀌는 티가 안 난다.\n" +
             "보스 SetActive(true), UI 내용 갈아끼우기, 씬 로드 등 '뚝 끊기는 게 보이면 안 되는 일'을 꽂는다.\n" +
             "얼마나 오래 가려져 있을지는 Fader의 해당 줄 '유지' 값으로 늘린다.")]
    [SerializeField] private UnityEvent onBlackout;

    [Tooltip("다시 다 밝아진 뒤에 실행된다. 안 쓰면 비워두면 된다.")]
    [SerializeField] private UnityEvent onComplete;

    [Tooltip("플레이어가 이 트리거에 들어오면 자동 실행. (트리거 콜라이더가 필요하다)")]
    [SerializeField] private bool playOnPlayerEnter = false;

    [Tooltip("한 번만 실행하고 다시는 안 함. 일회성 연출용.")]
    [SerializeField] private bool onlyOnce = false;

    private bool _played;

    /// <summary>인자가 없어서 버튼 OnClick이나 방 이벤트 슬롯에 그대로 잡힌다.</summary>
    public void Play()
    {
        if (onlyOnce && _played) return;
        _played = true;

        Fader fader = Fader.FullScreenFader;
        if (fader == null)
        {
            // 페이더를 못 구해도 할 일은 해야 한다. 연출만 생략 (검은 화면에 갇히는 것보다 낫다)
            onBlackout?.Invoke();
            onComplete?.Invoke();
            return;
        }

        fader.FadeOutIn(signal, () => onBlackout?.Invoke(), () => onComplete?.Invoke());
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!playOnPlayerEnter) return;
        if (!other.transform.root.CompareTag("Player")) return; // 태그는 반드시 루트에서 본다
        Play();
    }

    [ContextMenu("Test - Play")]
    private void TestPlay() => Play();
}
