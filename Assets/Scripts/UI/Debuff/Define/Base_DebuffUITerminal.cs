using UnityEngine;

/// <summary>
/// 유닛 머리 위 상태이상 아이콘 표시의 진입점.
///
/// [26/07/17] 구 DebuffStackType(취약/상처/부식/골절) 오버로드는 삭제됐다.
/// 상태이상은 DebuffBoolType 하나로 일원화한다. 스택을 갖는 상태이상(비폭)도
/// value 로 스택 수를 넘기면 되므로 별도 타입이 필요 없다.
/// </summary>
public abstract class Base_DebuffUITerminal : MonoBehaviour
{
    public abstract void UpdateUI(DebuffBoolType type, float value);
    public abstract void RemoveIcon(DebuffBoolType type);
    public abstract void RemoveAll();
}
