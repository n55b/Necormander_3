using UnityEngine;

/// <summary>
/// 디버그용 상태이상 부여기. 숫자키 1~5 를 누르면 씬의 '모든 적'에게 해당 상태이상이 1회 걸린다.
/// 비폭은 1스택씩 쌓이므로 10번 눌러야 터진다.
///
/// [왜 필요한가]
/// 상태이상의 정식 부여 수단은 유물/아이템인데 그 시스템이 아직 없다. 그래서 이게 없으면
/// 상태이상을 만들어놓고도 게임에서 한 번도 볼 수가 없다.
///
/// [입력 처리 방식]
/// 일부러 InputSystem 을 안 쓰고 레거시 Input 을 하드코딩했다 — 디버그용이라 액션 에셋과
/// 프리팹 배선을 늘릴 이유가 없다. 프로젝트가 activeInputHandler: 2(둘 다)로 켜져 있어서
/// Input.GetKeyDown 이 그대로 동작한다. 같은 방식의 선례로 DPSMeter(F3/F4)가 있다.
/// 숫자 1~4 는 구 SummonButton 액션에 바인딩만 남아 있고 받는 쪽이 없어서 충돌하지 않는다.
/// </summary>
public class StatusDebugKeys : MonoBehaviour
{
    [Tooltip("끄면 디버그 키가 동작하지 않습니다. 빌드에 실수로 남아도 이 체크만 풀면 됩니다.")]
    [SerializeField] private bool enableDebugKeys = true;

    private static readonly (KeyCode key, StatusType type, string label)[] Bindings =
    {
        (KeyCode.Alpha1, StatusType.Stun,     "기절"),
        (KeyCode.Alpha2, StatusType.Freeze,   "빙결"),
        (KeyCode.Alpha3, StatusType.Bleed,    "출혈"),
        (KeyCode.Alpha4, StatusType.Poison,   "중독"),
        (KeyCode.Alpha5, StatusType.BloodPop, "비폭"),
    };

    private void Update()
    {
        if (!enableDebugKeys) return;

        foreach (var (key, type, label) in Bindings)
        {
            if (!Input.GetKeyDown(key)) continue;
            ApplyToAllEnemies(type, label);
        }
    }

    private static void ApplyToAllEnemies(StatusType type, string label)
    {
        int hit = 0, blocked = 0;

        // 순회 중 비폭 폭발이 적을 죽여 리스트가 바뀔 수 있으므로 복사본을 돈다.
        var targets = CharacterStatus.ActiveEnemies.ToArray();
        foreach (var status in targets)
        {
            if (status == null) continue;

            bool wouldBlock = status.HasSuperArmor && StatusRules.BlockedBySuperArmor(type);
            status.ApplyStatus(type); // 스택형은 기본 1스택
            if (wouldBlock) blocked++; else hit++;
        }

        string note = blocked > 0 ? $" (슈퍼아머로 {blocked}마리 씹힘)" : "";
        Debug.Log($"<color=magenta>[상태이상 디버그]</color> {label} -> 적 {hit}마리{note}");
    }
}
