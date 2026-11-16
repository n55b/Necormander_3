using System;
using UnityEngine;

/// <summary>
/// Aseprite에서 생성된 클립에 포함된 애니메이션 이벤트(OnHitEvent)를 받아
/// 부모 함정 스크립트로 전달하는 중계 컴포넌트입니다. Animator와 같은 오브젝트에 붙입니다.
/// </summary>
public class TrapAnimationEventRelay : MonoBehaviour
{
    /// <summary>클립의 OnHitEvent 프레임에서 호출됩니다.</summary>
    public event Action HitEvent;

    // Animation Event 수신 (Spike_Attack / Jar_Active_0 클립)
    public void OnHitEvent()
    {
        HitEvent?.Invoke();
    }
}
