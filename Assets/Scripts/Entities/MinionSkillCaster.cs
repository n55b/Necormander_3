using UnityEngine;

/// <summary>
/// 소환수 스킬 시전용 임시 오브젝트.
///
/// 소환수는 필드에 상주하지 않는다(설계 3.1: 대기 → Space → 실체화 → 시전 → 소멸).
/// 예전엔 이 역할을 AllyController 가 맡아서, 스킬 한 번 쓸 때마다 NavMeshAgent 를 켜고
/// AIPatternSO 를 복제하고 브레인을 붙였다가 1.5초 뒤 버렸다. 새 구조에선 퍼펫이 평타 3타마다
/// + 스페이스바마다 뜨므로 그 비용을 감당할 이유가 없다.
///
/// 여기 남은 것은 딱 두 가지다:
///  1) 스킬이 위치를 옮길 수 있는 Transform
///  2) 스킬이 코루틴(타격 지연, 넉백)을 돌릴 수 있는 MonoBehaviour
/// 외형은 MinionSkillSO.skillAnimVisual 이 이 오브젝트의 자식으로 붙어서 담당한다.
/// </summary>
public class MinionSkillCaster : MonoBehaviour
{
    /// <summary>시전 주체인 소환수의 데이터. 스킬이 ATK 등을 여기서 읽는다.</summary>
    public MinionDataSO Data { get; private set; }

    // 아무도 수명을 안 정해줬을 때만 쓰는 상한. 실제로는 PlaySequenced 가 시전 시간에 맞춰 다시 잡는다.
    private const float DEFAULT_LIFETIME = 3f;

    // 넉백 코루틴(0.2s)이나 마지막 타격 판정이 아직 돌고 있을 수 있으므로 약간 여유를 준다.
    private const float DESPAWN_TAIL = 0.25f;

    private Coroutine _despawn;

    public static MinionSkillCaster Spawn(MinionDataSO data, Vector3 position)
    {
        var go = new GameObject($"MinionCaster_{(data != null ? data.minionName : "?")}");
        go.transform.position = position;

        var caster = go.AddComponent<MinionSkillCaster>();
        caster.Data = data;
        caster.SetLifetime(DEFAULT_LIFETIME);
        return caster;
    }

    /// <summary>
    /// 남은 수명을 다시 잡는다. 예전엔 Destroy(go, 3f) 로 고정이라, 0.9초짜리 마무리 일격을 써도
    /// 인형이 마지막 프레임을 물고 2초 넘게 화면에 서 있었다.
    /// </summary>
    public void SetLifetime(float seconds)
    {
        if (_despawn != null) StopCoroutine(_despawn);
        _despawn = StartCoroutine(DespawnRoutine(seconds));
    }

    private System.Collections.IEnumerator DespawnRoutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (this != null) Destroy(gameObject);
    }

    /// <summary>
    /// 소환수 외형을 붙이고 태그 시퀀스를 순서대로 재생하면서, '언제 때릴지'를 알려준다.
    ///
    /// [타격 타이밍을 숫자로 안 박는다 — 그림이 정한다]
    /// 두 가지 방식을 지원하고, 이벤트가 있으면 이벤트가 이긴다.
    ///
    /// 1) OnHitEvent (권장 — 적 공격 클립과 같은 방식)
    ///    클립에 박힌 OnHitEvent 하나가 타격 하나다. 2타면 2번 박으면 된다.
    ///    OnAttackEndEvent 가 오면 판정을 닫는다. BaseEntity 의 [애니메이션 작업자 가이드라인] 참조.
    ///    Aseprite 에서는 해당 프레임 셀의 user data 에 `event:OnHitEvent` 라고 적으면
    ///    임포터가 자동으로 심어준다 (재임포트해도 유지된다).
    ///
    /// 2) damageState (태그 경계)
    ///    이벤트를 아직 안 박았을 때의 폴백. MeleeDoll 은 Start(때리기 전)/Slash(때리는 중)/
    ///    End(때린 후) 로 이미 나뉘어 있어서 "Slash 동안 판정"이라고만 하면 된다.
    /// </summary>
    /// <param name="hitCount">이 스킬이 낼 '타수'의 단일 진실. 애니 이벤트는 '타이밍'만 준다.
    ///   OnAttackEnd 있으면 창에 hitCount 를 균등 배분, 없으면 OnHitEvent 마다 1타
    ///   (E==N 1:1 / E&gt;N 초과 무시 / E&lt;N 마지막 이벤트에서 몰아치기).</param>
    /// <param name="onHitWindow">판정을 열 때 호출. (열려 있을 시간(초), 지속틱을 쓸지 여부).
    ///   true = 창 동안 hitCount 균등 틱, false = 이벤트당 단발(펄스가 타수를 만든다).</param>
    /// <param name="onHitPulse">OnHitEvent 마다 1타(ResetHitTargets). 이벤트당 모드에서만.</param>
    /// <param name="onAttackEnd">OnAttackEndEvent 가 오면 호출. 판정을 닫으라는 뜻.</param>
    public GameObject PlaySequenced(GameObject visual, string[] sequence, string damageState, string hitEvent,
                                    float castDuration, float hitWindow, int hitCount, bool faceRight,
                                    System.Action<float, bool> onHitWindow,
                                    System.Action onHitPulse = null, System.Action onAttackEnd = null)
    {
        if (visual == null) return null;

        var vfx = Instantiate(visual, transform.position, Quaternion.identity, transform);
        vfx.transform.localPosition = Vector3.zero;

        var sr = vfx.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.flipX = faceRight;

        var anim = vfx.GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null)
        {
            onHitWindow?.Invoke(hitWindow, true); // 애니가 없으면 기다릴 이유가 없다(창=지속)
            return vfx;
        }

        float natural = SequenceLength(anim, sequence);
        if (natural <= 0f) natural = 1f;
        float speed = natural / Mathf.Max(0.01f, castDuration);
        anim.speed = speed;

        // 애니메이션이 끝나면 곧바로 사라진다. 고정 3초를 물고 서 있지 않는다.
        SetLifetime(castDuration + DESPAWN_TAIL);

        // [이벤트 유무를 실제로 검사한다]
        // hitEvent 를 적어놨어도 클립에 실제로 안 박혀 있으면 판정이 영영 안 열린다(조용히 데미지 0).
        // 적 쪽도 같은 문제를 같은 방식으로 푼다 — BaseAIPatternSO 가 HasAnimationEvent 로 확인하고
        // 없으면 임시 타이머로 폴백한다. 여기도 똑같이 태그 방식으로 되돌린다.
        // ── 판정창을 '언제 / 얼마나' 열지 결정한다. 우선순위 ─────────────────────────
        //  (1) OnHitEvent + OnAttackEndEvent 둘 다 박힘 = 시간형 다단히트 규약.
        //      OnHitEvent 에 열고, 창 길이 = 두 이벤트의 '실시간' 간격. 재생 속도가 바뀌면 간격도
        //      같이 줄어들어(공속 자동 반영) hitCount 가 그 창 안에 균등 배분된다.
        //  (2) SO 의 hitEvent 필드(예: DashDoll 단타) → OnHitEvent 순간에 연다.
        //  (3) damageState 태그(예: MeleeDoll Slash) → 그 태그가 재생되는 '동안' 연다.
        //  (4) 아무것도 없으면 → 시퀀스 전체를 판정창으로 가정(아트가 안 박은 폴백). 경고를 남긴다.
        bool clipHasHit  = HasEvent(anim, sequence, "OnHitEvent");
        bool clipHasEnd  = HasEvent(anim, sequence, "OnAttackEndEvent");
        bool eventWindow = clipHasHit && clipHasEnd;                                    // (1)
        bool useEvent    = !eventWindow && !string.IsNullOrEmpty(hitEvent)
                           && HasEvent(anim, sequence, hitEvent);                       // (2)
        bool tagMode     = !eventWindow && !useEvent && !string.IsNullOrEmpty(damageState); // (3)

        if (!eventWindow && !useEvent && !string.IsNullOrEmpty(hitEvent))
        {
            Debug.LogWarning($"<color=orange>[MinionCaster]</color> '{visual.name}' 클립에 '{hitEvent}' 이벤트가 없습니다. " +
                             $"태그(damageState='{damageState}') 방식으로 폴백합니다. " +
                             $"Aseprite 에서 타격 프레임 셀의 user data 에 `event:{hitEvent}` 를 넣으면 정확해집니다.");
        }

        // 클립에 이벤트가 하나라도 박혀 있으면 항상 relay 를 붙인다 — 없으면 Unity 가 "has no receiver!"
        // 경고를 낸다. 창을 이벤트로 여는 (1)(2) 에서만 실제 핸들러를 연결하고, 그 외엔 조용히 흡수만 한다.
        if (clipHasHit || clipHasEnd)
        {
            var relay = anim.gameObject.AddComponent<MinionAnimEventRelay>();
            if (eventWindow)
            {
                // (1) OnHit~OnAttackEnd 사이가 판정창. hitCount 타를 그 창에 균등 배분(지속틱).
                float span = ComputeEventSpan(anim, sequence, speed, hitWindow);
                bool opened = false;
                relay.OnHit = () => { if (!opened) { opened = true; onHitWindow?.Invoke(span, true); } };
                relay.OnAttackEnd = () => onAttackEnd?.Invoke(); // 창 길이로도 닫히지만 이중 안전
            }
            else if (useEvent)
            {
                // (2) OnHitEvent 마다 1타(박스 단발, 펄스가 타수를 만든다).
                //     hitCount(N)=타수 진실, 이벤트(E)=타이밍. E==N 1:1 / E>N 초과 무시 / E<N 마지막에 몰아치기.
                int e = CountEvents(anim, sequence, "OnHitEvent");
                int n = Mathf.Max(1, hitCount);
                if (e != n)
                    Debug.LogWarning($"<color=orange>[MinionCaster]</color> '{visual.name}': 선언 타수(hitCount={n})와 OnHitEvent 수({e})가 다릅니다. " +
                                     (e > n ? $"뒤 {e - n}개 이벤트를 무시합니다." : $"마지막 이벤트에서 {n - e}타를 몰아칩니다."));

                bool opened = false;
                int fired = 0;
                relay.OnHit = () =>
                {
                    if (!opened) { opened = true; onHitWindow?.Invoke(castDuration, false); } // 이벤트당 = 박스 단발
                    fired++;
                    if (fired > n) return;                     // E>N: 초과 이벤트 무시
                    onHitPulse?.Invoke();                      // 이 이벤트 = 1타
                    if (fired == e && n > e)                   // E<N: 마지막 이벤트 뒤로 나머지 몰아치기
                        StartCoroutine(BurstExtraPulses(onHitPulse, n - e));
                };
                relay.OnAttackEnd = () => onAttackEnd?.Invoke();
            }
        }

        // (4) 이벤트도 태그도 없으면 시퀀스 전체를 창으로 가정하고 즉시 연다(지속).
        if (!eventWindow && !useEvent && !tagMode)
        {
            Debug.LogWarning($"<color=orange>[MinionCaster]</color> '{visual.name}' 에 OnHitEvent/OnAttackEndEvent 도, " +
                             $"damageState 태그도 없습니다. 시퀀스 전체({castDuration:0.00}s)를 판정창으로 가정합니다. " +
                             $"정확히 하려면 Aseprite 에 OnHitEvent(긴 판정이면 +OnAttackEndEvent)를 박으세요.");
            onHitWindow?.Invoke(castDuration, true);
        }

        // (3) 태그 방식일 때만 SequenceRoutine 이 태그 경계에서 창을 연다. 나머지는 위에서 이미 처리됨.
        StartCoroutine(SequenceRoutine(anim, sequence, damageState, speed, tagMode ? onHitWindow : null));
        return vfx;
    }

    private System.Collections.IEnumerator SequenceRoutine(Animator anim, string[] sequence, string damageState,
                                                           float speed, System.Action<float, bool> onHitWindow)
    {
        if (sequence == null || sequence.Length == 0)
        {
            onHitWindow?.Invoke(0.1f, true);
            yield break;
        }

        foreach (var stateName in sequence)
        {
            if (anim == null) yield break; // 도중에 시전자가 소멸했을 수 있다

            float len = ClipLength(anim, stateName);
            if (len <= 0f)
            {
                Debug.LogWarning($"<color=orange>[MinionCaster]</color> 애니메이터에 '{stateName}' 상태가 없습니다. 건너뜁니다.");
                continue;
            }

            anim.Play(stateName, 0, 0f);

            // 이 태그가 '때리는 중' 태그라면, 정확히 이 태그가 재생되는 동안만 판정을 연다(지속틱).
            if (onHitWindow != null && stateName == damageState)
                onHitWindow.Invoke(len / speed, true);

            yield return new WaitForSeconds(len / speed);
        }
    }

    private static float ClipLength(Animator anim, string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return 0f;
        foreach (var c in anim.runtimeAnimatorController.animationClips)
            if (c != null && c.name == stateName) return c.length;
        return 0f;
    }

    /// <summary>시퀀스 클립들에 박힌 특정 이벤트의 총 개수. HasEvent 의 세는 버전(이벤트당 타수 판정용).</summary>
    private static int CountEvents(Animator anim, string[] sequence, string eventName)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return 0;
        int n = 0;
        foreach (var c in anim.runtimeAnimatorController.animationClips)
        {
            if (c == null) continue;
            if (sequence != null && sequence.Length > 0 && System.Array.IndexOf(sequence, c.name) < 0) continue;
            foreach (var ev in c.events)
                if (ev.functionName == eventName) n++;
        }
        return n;
    }

    /// <summary>
    /// E&lt;N 보정: 이벤트가 선언 타수보다 적을 때, 마지막 이벤트 뒤로 남은 타를 물리 스텝마다 하나씩 몰아친다.
    /// 각 펄스(ResetHitTargets) 다음 FixedUpdate 에서 박스가 한 번씩 재타격한다.
    /// ponytail: 마지막 OnHitEvent 가 클립 맨 끝 프레임이고 N이 E보다 훨씬 크면, 뒷 몇 타가 박스 수명 밖으로
    ///           밀릴 수 있다. 그럴 땐 "이벤트를 더 박으라"는 경고가 이미 떠 있으니 상한만 인지하고 둔다.
    /// </summary>
    private System.Collections.IEnumerator BurstExtraPulses(System.Action pulse, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new WaitForFixedUpdate();
            pulse?.Invoke();
        }
    }

    /// <summary>재생할 클립들 중 하나라도 이 이벤트를 갖고 있는가. BaseEntity.HasAnimationEvent 와 같은 검사.</summary>
    private static bool HasEvent(Animator anim, string[] sequence, string eventName)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        foreach (var c in anim.runtimeAnimatorController.animationClips)
        {
            if (c == null) continue;
            // 시퀀스를 지정했으면 거기 속한 클립만 본다.
            if (sequence != null && sequence.Length > 0 && System.Array.IndexOf(sequence, c.name) < 0) continue;
            foreach (var ev in c.events)
                if (ev.functionName == eventName) return true;
        }
        return false;
    }

    /// <summary>
    /// 시퀀스에 박힌 첫 OnHitEvent → 첫 OnAttackEndEvent 사이의 '실시간' 길이(초)를 구한다.
    /// 클립은 speed 배속으로 재생되므로 이벤트 간격도 그만큼 줄어든다(=공속이 자동 반영된다).
    /// 이벤트가 여러 클립에 흩어져 있어도 앞 클립들의 재생시간을 누적해 정확히 계산한다.
    /// 못 찾거나 순서가 뒤집혔으면 fallback(초)을 돌려준다.
    /// </summary>
    private static float ComputeEventSpan(Animator anim, string[] sequence, float speed, float fallback)
    {
        if (anim == null || anim.runtimeAnimatorController == null || sequence == null) return fallback;

        float inv = 1f / Mathf.Max(0.01f, speed);
        float cursor = 0f, hitAt = -1f, endAt = -1f;

        foreach (var state in sequence)
        {
            AnimationClip clip = null;
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c != null && c.name == state) { clip = c; break; }
            if (clip == null) continue;

            foreach (var ev in clip.events)
            {
                float t = cursor + ev.time * inv;
                if (ev.functionName == "OnHitEvent" && hitAt < 0f) hitAt = t;
                else if (ev.functionName == "OnAttackEndEvent" && endAt < 0f) endAt = t;
            }
            cursor += clip.length * inv;
        }

        return (hitAt >= 0f && endAt > hitAt) ? endAt - hitAt : fallback;
    }

    private static float SequenceLength(Animator anim, string[] sequence)
    {
        if (sequence == null || sequence.Length == 0)
        {
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c != null) return c.length; // 기본 상태 = 처음 추가된 클립
            return 0f;
        }
        float sum = 0f;
        foreach (var s in sequence) sum += ClipLength(anim, s);
        return sum;
    }

    /// <summary>
    /// 소환수 외형을 시전자 밑에 붙이고, 지정한 애니메이터 상태를 fitDuration 에 '정확히 맞게' 재생한다.
    ///
    /// [여기가 애니메이션-시전시간 동기화의 유일한 지점이다]
    /// 재생 속도를 클립길이/시전시간 으로 잡기 때문에, 나중에 공속 등으로 시전이 빨라져
    /// fitDuration 이 줄면 애니메이션도 정확히 같은 비율로 빨라진다. 타격 시점은 비율로 잡혀 있으므로
    /// 둘이 절대 어긋나지 않는다.
    ///
    /// aseprite 임포터가 만들어주는 컨트롤러는 태그마다 상태를 하나씩 만들어 놓고 트랜지션을
    /// 하나도 안 건다(AnimatorControllerGeneration 은 AddMotion 만 호출한다). 그래서 기본 상태
    /// 외의 상태는 Play() 로 직접 지정하지 않으면 영원히 재생되지 않는다.
    /// </summary>
    /// <param name="stateName">재생할 상태 이름. 비우면 기본 상태를 그대로 둔다.</param>
    /// <returns>생성된 비주얼 인스턴스. visual 이 null 이면 null.</returns>
    public GameObject AttachVisual(GameObject visual, string stateName, float fitDuration, bool faceRight)
    {
        if (visual == null) return null;

        var vfx = Instantiate(visual, transform.position, Quaternion.identity, transform);
        vfx.transform.localPosition = Vector3.zero;

        var sr = vfx.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.flipX = faceRight;

        PlayStateFitted(vfx, stateName, fitDuration);
        return vfx;
    }

    /// <summary>지정 상태를 fitDuration 길이에 맞춰 재생한다. 상태를 못 찾으면 속도만 두고 넘어간다.</summary>
    public static void PlayStateFitted(GameObject vfx, string stateName, float fitDuration)
    {
        if (vfx == null || fitDuration <= 0f) return;

        var anim = vfx.GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null) return;

        var clips = anim.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0) return;

        // 상태 이름 = 클립 이름 (AddMotion 이 클립 이름으로 상태를 만든다).
        // 이름이 비어 있으면 기본 상태 = 첫 번째로 추가된 클립.
        float clipLen = 0f;
        foreach (var c in clips)
        {
            if (c == null) continue;
            if (string.IsNullOrEmpty(stateName)) { clipLen = c.length; break; }
            if (c.name == stateName) { clipLen = c.length; break; }
        }

        if (clipLen <= 0f)
        {
            Debug.LogWarning($"<color=orange>[MinionCaster]</color> '{vfx.name}' 애니메이터에 '{stateName}' 상태가 없습니다. 기본 상태로 재생합니다.");
            return;
        }

        anim.speed = clipLen / fitDuration;
        if (!string.IsNullOrEmpty(stateName)) anim.Play(stateName, 0, 0f);
    }
}
