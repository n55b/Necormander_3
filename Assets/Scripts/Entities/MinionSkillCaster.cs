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

    // ponytail: 수명 고정값. 애니메이션 + 타격 지연 + 넉백(0.2s)을 덮는 넉넉한 상한.
    // 스킬별로 정밀하게 맞춰야 할 만큼 길어지면 MinionSkillSO 에 lifetime 필드를 빼면 된다.
    private const float DEFAULT_LIFETIME = 3f;

    public static MinionSkillCaster Spawn(MinionDataSO data, Vector3 position)
    {
        var go = new GameObject($"MinionCaster_{(data != null ? data.minionName : "?")}");
        go.transform.position = position;

        var caster = go.AddComponent<MinionSkillCaster>();
        caster.Data = data;

        Destroy(go, DEFAULT_LIFETIME);
        return caster;
    }

    /// <summary>
    /// 소환수 외형을 붙이고 태그 시퀀스를 순서대로 재생하면서, '언제 때릴지'를 알려준다.
    ///
    /// [타격 타이밍을 숫자로 안 박는 이유]
    /// 태그 경계 자체가 이미 아티스트가 그림에 찍어놓은 마커다. MeleeDoll 은 Start(때리기 전) →
    /// Slash(때리는 중) → End(때린 후) 로 나뉘어 있으므로, "Slash 태그가 재생되는 동안 판정"
    /// 이라고만 하면 초도 비율도 필요 없다. 나중에 아티스트가 Start 길이를 바꿔도 판정이 알아서 따라온다.
    ///
    /// 태그로 안 나뉘는 경우(DashDoll 처럼 Attack 하나에 준비+타격이 다 들어있음)를 위해
    /// Animation Event 도 받는다. Aseprite 셀 user data 에 `event:MinionHit` 을 적으면
    /// 임포터가 그 프레임에 이벤트를 심어준다. 이벤트가 있으면 그게 태그보다 우선한다.
    /// </summary>
    /// <param name="onHitWindow">판정을 열어야 할 때 호출. 인자는 판정이 열려 있을 시간(초).</param>
    public GameObject PlaySequenced(GameObject visual, string[] sequence, string damageState, string hitEvent,
                                    float castDuration, float hitWindow, bool faceRight,
                                    System.Action<float> onHitWindow)
    {
        if (visual == null) return null;

        var vfx = Instantiate(visual, transform.position, Quaternion.identity, transform);
        vfx.transform.localPosition = Vector3.zero;

        var sr = vfx.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.flipX = faceRight;

        var anim = vfx.GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null)
        {
            onHitWindow?.Invoke(hitWindow); // 애니가 없으면 기다릴 이유가 없다
            return vfx;
        }

        float natural = SequenceLength(anim, sequence);
        if (natural <= 0f) natural = 1f;
        float speed = natural / Mathf.Max(0.01f, castDuration);
        anim.speed = speed;

        // 이벤트 방식: 클립에 event:MinionHit 이 심겨 있으면 그 프레임에 판정이 열린다.
        bool useEvent = !string.IsNullOrEmpty(hitEvent);
        if (useEvent)
        {
            var relay = anim.gameObject.AddComponent<MinionAnimEventRelay>();
            bool fired = false;
            relay.OnHit = () => { if (!fired) { fired = true; onHitWindow?.Invoke(hitWindow); } };
        }

        StartCoroutine(SequenceRoutine(anim, sequence, damageState, speed, useEvent ? null : onHitWindow));
        return vfx;
    }

    private System.Collections.IEnumerator SequenceRoutine(Animator anim, string[] sequence, string damageState,
                                                           float speed, System.Action<float> onHitWindow)
    {
        if (sequence == null || sequence.Length == 0)
        {
            onHitWindow?.Invoke(0.1f);
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

            // 이 태그가 '때리는 중' 태그라면, 정확히 이 태그가 재생되는 동안만 판정을 연다.
            if (onHitWindow != null && stateName == damageState)
                onHitWindow.Invoke(len / speed);

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
