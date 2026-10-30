using UnityEngine;

/// <summary>
/// 자식 Animator 의 애니메이션 이벤트를 부모의 사운드 세트로 넘겨준다.
/// 사운드 세트(EntitySoundSet)가 실행 중에 자동으로 붙이므로 직접 붙일 필요는 없다.
/// </summary>
[AddComponentMenu("")]
public class SoundAnimEventRelay : MonoBehaviour
{
    [HideInInspector] public EntitySoundSet owner;

    public void OnHitEvent() { if (owner != null && owner.listenAnimationEvents) owner.Play(SoundTrigger.OnAttackHit); }
    public void OnAttackEndEvent() { if (owner != null && owner.listenAnimationEvents) owner.Play(SoundTrigger.OnAttackEnd); }
    public void PlaySound(string soundName) { if (owner != null) owner.PlaySound(soundName); }
}
