using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 신호가 오면 카메라를 흔든다. 카메라 리그(CinemachineCamera)에 '하나만' 붙이고,
/// 반응 목록에 "무슨 신호 → 얼마나 세게"를 줄줄이 적는다.
///
/// 흔드는 일 자체는 이미 완성된 Cinemachine Impulse(CameraManager.HitShakeCamera)에 그대로 맡긴다.
/// 카메라 추적이 Cinemachine Follow라서 카메라 위치를 직접 만지면 추적과 싸운다 — 임펄스가 유일한 정답이다.
/// 파형/지속시간/전역세기는 카메라 리그의 CinemachineImpulseSource / ImpulseListener 인스펙터에서 조절한다.
/// </summary>
[DisallowMultipleComponent] // 두 개 붙으면 경고 없이 세기가 2배가 된다. 신호별 세기는 아래 목록에 줄로 추가할 것.
public class CameraShaker : MonoBehaviour
{
    [System.Serializable]
    public class Reaction
    {
        [Tooltip("이 신호가 오면 아래 세기로 흔들린다.")]
        public ShakeSignal signal = ShakeSignal.플레이어피격;

        [Tooltip("흔들림 강도 배율. 카메라 리그의 ImpulseSource 기본 세기에 곱해진다.")]
        public float force = 1.5f;
    }

    [Tooltip("무슨 신호에 얼마나 흔들릴지. 필요한 만큼 줄을 추가하면 된다. 목록에 없는 신호엔 반응하지 않는다.")]
    [SerializeField]
    private List<Reaction> reactions = new List<Reaction> { new Reaction() };

    private void OnEnable() => Signal.Listen<ShakeSignal>(OnSignal);
    private void OnDisable() => Signal.Unlisten<ShakeSignal>(OnSignal);

    private void OnSignal(ShakeSignal signal)
    {
        // 첫 줄만 쓰고 끝낸다(UIShaker와 동일). 같은 신호를 실수로 두 줄 넣었을 때
        // 한쪽은 겹쳐 흔들리고 한쪽은 안 겹치면 기획자가 원인을 못 찾는다.
        foreach (var r in reactions)
        {
            if (r != null && r.signal == signal) { Shake(r.force); return; }
        }
    }

    /// <summary>인자가 없어서 버튼 OnClick이나 방 이벤트 슬롯에 그대로 잡힌다. 목록 첫 줄의 세기를 쓴다.</summary>
    public void Shake() => Shake(reactions.Count > 0 && reactions[0] != null ? reactions[0].force : 1f);

    public void Shake(float force)
    {
        if (CameraManager.Instance != null) CameraManager.Instance.HitShakeCamera(force);
    }

    [ContextMenu("Test - Shake")]
    private void TestShake() => Shake();
}
