using UnityEngine;

/// <summary>
/// 흔들림 신호를 쏘는 컴포넌트. 기획자가 씬에 직접 놓는 트리거용이다.
/// (피격처럼 '코드가 판단하는' 신호는 이게 필요 없다. 그 자리에서 Signal.Fire() 한 줄이면 된다.)
///
/// 페이드의 FadeAction과 달리 '할 일' 칸이 없다. 흔들림엔 페이드의 '가려진 순간' 같은
/// 중간 타이밍이 없어서, 쏘면 듣는 애들(카메라/체력바)이 각자 알아서 흔들리면 그만이기 때문이다.
///
/// 쓰는 법:
///   1. 트리거 콜라이더 달고 sendOnPlayerEnter 체크 → 밟으면 발사
///   2. 버튼 OnClick이나 방 이벤트 슬롯에 이 오브젝트를 드래그 → Send() 선택
///
/// ※ 드래그로 이을 수 있으면 신호를 쓰지 마라. CameraShaker/UIShaker의 Shake()를
///    그 자리 UnityEvent 슬롯에 직접 드래그하는 게 더 낫다. 자세한 건 Signal.cs 참고.
/// </summary>
public class ShakeSender : MonoBehaviour
{
    [Tooltip("무슨 신호를 쏠지. 이 신호를 듣고 있는 리시버들이 전부 각자의 세기로 반응한다.")]
    [SerializeField] private ShakeSignal signal = ShakeSignal.플레이어피격;

    [Tooltip("플레이어가 이 트리거에 들어오면 자동 발사. (트리거 콜라이더가 필요하다)")]
    [SerializeField] private bool sendOnPlayerEnter = false;

    [Tooltip("한 번만 발사하고 다시는 안 쏨. 일회성 연출용.")]
    [SerializeField] private bool onlyOnce = false;

    private bool _sent;

    /// <summary>인자가 없어서 버튼 OnClick이나 방 이벤트 슬롯에 그대로 잡힌다.</summary>
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
