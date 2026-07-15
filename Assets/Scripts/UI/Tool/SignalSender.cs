using UnityEngine;

/// <summary>
/// 신호를 쏘는 쪽. 기획자가 씬에 직접 놓는 트리거용이다.
/// (문 통과·피격처럼 '코드가 판단하는' 신호는 이 컴포넌트가 필요 없다. 코드에서 Signal&lt;T&gt;.Fire() 한 줄이면 된다.)
///
/// 쓰는 법 두 가지:
///   1. 이 오브젝트에 트리거 콜라이더를 달고 sendOnPlayerEnter 체크 → 플레이어가 밟으면 발사
///   2. 버튼 OnClick이나 방 이벤트 슬롯에 이 오브젝트를 드래그 → Send() 선택
///
/// 이 클래스는 직접 붙이지 못한다. 신호 종류별 껍데기(FadeSender, ShakeSender)를 붙인다.
/// 로직은 여기 한 번만 있고 껍데기는 한 줄짜리라, 종류가 늘어도 모양이 절대 안 갈라진다.
/// </summary>
public abstract class SignalSender<T> : MonoBehaviour where T : struct, System.Enum
{
    [Tooltip("무슨 신호를 쏠지. 이 신호를 듣고 있는 리시버들이 전부 반응한다.")]
    [SerializeField] private T signal;

    [Tooltip("플레이어가 이 트리거에 들어오면 자동으로 발사. (트리거 콜라이더가 필요하다)")]
    [SerializeField] private bool sendOnPlayerEnter = false;

    [Tooltip("한 번만 발사하고 다시는 안 쏨. 일회성 연출용.")]
    [SerializeField] private bool onlyOnce = false;

    private bool _sent;

    /// <summary>인자가 없어서 버튼 OnClick이나 UnityEvent 슬롯에 그대로 잡힌다.</summary>
    public void Send()
    {
        if (onlyOnce && _sent) return;
        _sent = true;
        Signal.Fire(signal);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!sendOnPlayerEnter) return;
        if (!other.transform.root.CompareTag("Player")) return; // 태그는 반드시 루트에서 본다
        Send();
    }

    [ContextMenu("Test - Send")]
    private void TestSend() => Send();
}
