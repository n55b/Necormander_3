using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [안날라가기] 능력을 구현하는 클래스입니다.
/// 유닛을 집을 때 실제 유닛을 비활성화하지 않고 데이터만 복사하여 클러스터에 담습니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowPhantomAbility", menuName = "Necromancer/Growth/Throw Ability/Phantom")]
public class ThrowPhantomAbilitySO : ThrowAbilitySO
{
    public override bool OnTryPickUp(IThrowable target, List<IThrowable> heldList)
    {
        // 이미 해당 오브젝트가 리스트에 있다면 중복 추가 방지
        if (heldList.Contains(target)) return false;

        if (target is AllyController originalAlly)
        {
            // [추가] 유저 요청: "장착 안 한 미니언들은 못 주워와야 해."
            // 현재 인벤토리 슬롯에 해당 직업이 있는지 확인합니다.
            if (InventoryManager.Instance != null && !InventoryManager.Instance.HasJobInSlots(originalAlly.MinionType))
            {
                Debug.Log($"<color=orange>[Phantom]</color> {originalAlly.MinionType} is not equipped in slots. Cannot capture essence.");
                return false;
            }

            // 1. 유저 요청: "실제로 집어지지도 않고 던질때도 걔는 영향이 없음. 그 효과랑 수치만 쏙 빼오는거임."
            // -> 실제 유닛은 필드에 그대로 두고, 데이터만 가진 'Phantom' 오브젝트를 생성하여 대신 집어넣습니다.
            
            GameObject phantomObj = new GameObject($"Phantom_{originalAlly.gameObject.name}");
            phantomObj.transform.position = originalAlly.transform.position;
            
            // Phantom을 위한 최소한의 데이터 공급자 역할
            PhantomAlly phantom = phantomObj.AddComponent<PhantomAlly>();
            phantom.Init(originalAlly);
            
            heldList.Add(phantom);
            
            Debug.Log($"<color=cyan>[Ability: Phantom]</color> Captured essence of {originalAlly.MinionType} without consuming unit.");
            return true; // 기존의 PerformPickUp 로직을 스킵하도록 함
        }

        return false;
    }
}

/// <summary>
/// Phantom 능력을 통해 생성된 가짜 유닛입니다. 
/// IThrowable 인터페이스를 구현하여 ThrowCluster와 ThrowStrategy가 실제 유닛처럼 인식하게 합니다.
/// </summary>
public class PhantomAlly : MonoBehaviour, IThrowable
{
    private AllyController _original;
    public CommandData MinionType => _original.MinionType;
    public MinionDataSO MinionData => _original.MinionData;

    // [추가] 원본 유닛의 물리/성장 수치 실시간 대답
    public float MaxSpeed => _original.MaxSpeed;
    public float MinSpeed => _original.MinSpeed;
    public float FullChargeSpeed => _original.FullChargeSpeed;
    public float JumpHeight => _original.JumpHeight;
    public float StraightHeight => _original.StraightHeight;

    public void Init(AllyController original)
    {
        _original = original;
        // 비주얼적으로 "정수만 쏙 빼왔음"을 알리기 위해 투명한 구체나 이펙트를 추가할 수 있습니다.
    }

    public void OnPickedUp() { /* Phantom은 이미 가짜라 추가 처리가 필요 없음 */ }
    public void OnThrown(Vector2 targetPosition, float chargeRatio) { /* 클러스터 안에서만 작동하므로 개별 이동 불필요 */ }
    public void OnLanded() 
    {
        // 던지기 완료 후 가짜 오브젝트 파괴
        Destroy(gameObject);
    }

    // [추가] 인터페이스 구현
    public void PrepareForClusterThrow(float chargeRatio, bool isDirect) { }
    public void SetImpacted(bool value) { }

    // ThrowStrategy가 기대하는 컴포넌트 접근을 위해 래핑
    public new T GetComponentInParent<T>() where T : Component => _original.GetComponentInParent<T>();
}
