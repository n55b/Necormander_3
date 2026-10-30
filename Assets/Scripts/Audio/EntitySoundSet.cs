using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>사운드가 재생되는 시점.</summary>
public enum SoundTrigger
{
    [InspectorName("직접 호출 (애니메이션 이벤트 PlaySound / 코드)")] Manual,
    [InspectorName("나타날 때 (오브젝트가 켜질 때)")]                  OnSpawn,
    [InspectorName("무기 휘두르는 순간 (애니메이션 OnHitEvent / 빗나가도 재생)")] OnAttackHit,
    [InspectorName("공격 동작 끝 (애니메이션 이벤트 OnAttackEndEvent)")] OnAttackEnd,
    [InspectorName("맞았을 때")]                                        OnHurt,
    [InspectorName("회복했을 때")]                                      OnHeal,
    [InspectorName("죽을 때")]                                          OnDeath,
    [InspectorName("소환수 평타 실제 적중 (빗나가면 재생 안 함)")] OnBasicDamageDealt,
}

/// <summary>오브젝트 소리 하나: 소리 묶음 + 이름 + 재생 조건.</summary>
[Serializable]
public class SoundEntry : SoundClipGroup
{
    [KoLabel("이름"), Tooltip("애니메이션 이벤트 PlaySound(이름) 이나 코드 PlaySound(\"이름\") 으로 부를 때 쓰는 이름입니다.")]
    public string name = "새 소리";
    [KoLabel("언제 재생")]
    public SoundTrigger trigger = SoundTrigger.Manual;
    [KoLabel("애니메이션 상태 (비우면 공통)"), Tooltip("공격 타격/종료 이벤트를 받을 상태 이름입니다. 예: Attack, Skill. 비우면 모든 상태에서 재생합니다. 직접 호출과 미리 듣기에는 적용하지 않습니다.")]
    public string animationState = "";
    [KoLabel("같은 파일 공통 간격 (초)", 0f, 2f), Tooltip("같은 파일을 사용하는 여러 오브젝트 사이의 공통 간격입니다. 소리 묶음의 최소 간격은 이 항목 자체에 적용됩니다.")]
    public float minInterval = 0.05f;
    [KoLabel("지연 (초)", 0f, 2f), Tooltip("재생 조건이 된 뒤 이만큼 기다렸다가 재생합니다.")]
    public float delay = 0f;
}

/// <summary>
/// 사운드 세트 — 오브젝트 · 캐릭터 · 소환수 · 몬스터 어디에나 붙여서 소리를 넣는다.
///
/// · '맞았을 때 / 회복했을 때 / 죽을 때'는 같은 오브젝트(또는 자식)의 CharacterHealth 에 자동으로 연결된다.
/// · '공격이 맞는 순간 / 공격 동작 끝'은 기존 애니메이션 이벤트(OnHitEvent / OnAttackEndEvent)를 그대로 받는다.
///   Animator 가 자식에 있으면 중계 컴포넌트(SoundAnimEventRelay)를 실행 중에 자동으로 붙인다.
/// · 그 외 원하는 순간: 애니메이션 이벤트 함수 이름 PlaySound, 문자열 인자에 소리 이름.
/// · 재생은 SoundManager 효과음 풀을 거친다(옵션의 효과음 볼륨 반영).
///
/// 소리를 넣고 바로 들어보려면: Tools > 사운드 > 사운드 테스트 툴
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("사운드/사운드 세트")]
public class EntitySoundSet : MonoBehaviour
{
    [KoLabel("소리 목록")]
    public List<SoundEntry> sounds = new List<SoundEntry>();

    [KoLabel("체력 (비우면 자동으로 찾음)"), Tooltip("맞았을 때 / 회복 / 죽을 때 소리에 쓰입니다.")]
    public CharacterHealth health;

    [KoLabel("애니메이션 이벤트 받기"), Tooltip("켜면 자식 Animator 의 OnHitEvent / OnAttackEndEvent / PlaySound 이벤트로도 소리를 냅니다.")]
    public bool listenAnimationEvents = true;

    /// <summary>소리가 실제로 재생될 때마다 (사운드 테스트 툴의 '재생 기록'용).</summary>
    public static event Action<EntitySoundSet, SoundEntry, AudioClip> Played;

    // 같은 클립이 여러 오브젝트에서 동시에 터지는 것을 막는 전역 간격 기록
    private static readonly Dictionary<int, float> _lastPlayByClip = new Dictionary<int, float>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlayback() => _lastPlayByClip.Clear();

    private bool _subscribed;

    private void Awake()
    {
        if (health == null) health = GetComponentInChildren<CharacterHealth>(true);
        if (!listenAnimationEvents) return;
        foreach (var animator in GetComponentsInChildren<Animator>(true))
        {
            // 같은 오브젝트의 Animator 는 이 컴포넌트가 직접 이벤트를 받는다.
            if (animator.gameObject == gameObject) continue;
            var relay = animator.GetComponent<SoundAnimEventRelay>();
            if (relay == null) relay = animator.gameObject.AddComponent<SoundAnimEventRelay>();
            relay.owner = this;
        }
    }

    private void OnEnable()
    {
        if (health != null && !_subscribed)
        {
            health.OnDamageTaken += HandleHurt;
            health.OnHeal += HandleHeal;
            health.OnDeath += HandleDeath;
            _subscribed = true;
        }
        Play(SoundTrigger.OnSpawn);
    }

    private void OnDisable()
    {
        if (health != null && _subscribed)
        {
            health.OnDamageTaken -= HandleHurt;
            health.OnHeal -= HandleHeal;
            health.OnDeath -= HandleDeath;
        }
        _subscribed = false;
    }

    private void HandleHurt(float damage) { if (damage > 0f) Play(SoundTrigger.OnHurt); }
    private void HandleHeal() => Play(SoundTrigger.OnHeal);
    private void HandleDeath() => Play(SoundTrigger.OnDeath);

    // ── 애니메이션 이벤트 (같은 오브젝트에 Animator 가 있을 때 직접 받음) ──
    public void OnHitEvent(AnimationEvent evt) { if (listenAnimationEvents) Play(SoundTrigger.OnAttackHit, evt); }
    public void OnAttackEndEvent(AnimationEvent evt) { if (listenAnimationEvents) Play(SoundTrigger.OnAttackEnd, evt); }

    /// <summary>애니메이션 이벤트 / 코드에서 이름으로 재생.</summary>
    public void PlaySound(string soundName)
    {
        for (int i = 0; i < sounds.Count; i++)
        {
            var e = sounds[i];
            if (e != null && string.Equals(e.name, soundName, StringComparison.OrdinalIgnoreCase)) PlayEntry(e);
        }
    }

    /// <summary>해당 시점의 소리를 모두 재생.</summary>
    public void Play(SoundTrigger trigger, AnimationEvent evt = null)
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < sounds.Count; i++)
        {
            var e = sounds[i];
            if (e == null || e.trigger != trigger) continue;
            if ((trigger == SoundTrigger.OnAttackHit || trigger == SoundTrigger.OnAttackEnd)
                && !string.IsNullOrEmpty(e.animationState)
                && (evt == null || !evt.isFiredByAnimator || !evt.animatorStateInfo.IsName(e.animationState))) continue;
            PlayEntry(e);
        }
    }

    /// <summary>소리 하나 재생 (지연·간격·무작위 적용).</summary>
    public void PlayEntry(SoundEntry e)
    {
        if (e == null || !Application.isPlaying || !e.HasClip) return;
        if (e.delay > 0f && isActiveAndEnabled) StartCoroutine(PlayDelayed(e));
        else Emit(e);
    }

    private IEnumerator PlayDelayed(SoundEntry e)
    {
        yield return new WaitForSeconds(e.delay);
        Emit(e);
    }

    private void Emit(SoundEntry e)
    {
        var clip = e.PickClip();
        if (clip == null) return;
        int key = clip.GetInstanceID();
        float now = Time.unscaledTime;
        if (_lastPlayByClip.TryGetValue(key, out float last) && now - last < e.minInterval) return;
        if (e.PlayChosenClip(clip, transform.position))
        {
            _lastPlayByClip[key] = now;
            Played?.Invoke(this, e, clip);
        }
    }
}
