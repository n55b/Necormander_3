using UnityEngine;

/// <summary>
/// 게임의 경제 및 자원 관련 시스템을 담당하던 매니저입니다.
/// (현재 자원 시스템 제거로 인해 비어있는 상태이며, 추후 다른 경제 로직 필요 시 활용 가능합니다)
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public void Initialize()
    {
        Debug.Log("<color=yellow>[EconomyManager]</color> Initialized.");
    }
}
