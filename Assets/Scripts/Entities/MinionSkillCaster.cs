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
}
