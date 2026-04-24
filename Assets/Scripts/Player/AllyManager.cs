using System.Collections.Generic;
using UnityEngine;

public class AllyManager : MonoBehaviour
{
    [Header("아군 유닛들")]
    [SerializeField] List<AllyController> allys;
    [SerializeField] bool isBattle = false;
    [SerializeField] LayerMask playerLayer;

    // 아군 유닛 소환 함수
    public AllyController SpawnAlly(MinionDataSO data, Vector3 _position)
    {
        // 리스트 정리
        RemoveNullinAllys();

        if (data == null) return null;

        // [중요] 조립은 중앙 공장(DataManager)에 맡깁니다.
        GameObject obj = GameManager.Instance.dataManager.CreateUnit(data, _position);
        if (obj == null) return null;
        
        AllyController _ally = obj.GetComponent<AllyController>();
        if (_ally != null)
        {
            // 아군으로서의 추가 설정만 수행
            _ally.player = this.gameObject.transform;
            _ally.SetBattleState(isBattle);
            allys.Add(_ally);
        }

        Debug.Log($"<color=cyan>[AllyManager]</color> {data.minionName} 소환 및 리스트 등록 완료");
        return _ally;
    }

    // 아군 전투 중인지 상태 받아서 유닛들에게 뿌려주는 함수
    public void SetBattleState(bool _bool)
    {
        isBattle = _bool;

        RemoveNullinAllys(); // 리스트 정리

        foreach (var ally in allys)
        {
            ally.SetBattleState(isBattle);
        }
    }

    // 아군 유닛들이 전투 중인지 확인하는 함수
    public bool CheckAllyState()
    {
        RemoveNullinAllys();

        foreach (var ally in allys)
        {
            // 1. ally 및 브레인 유효성 체크
            if (ally == null || ally.Brain == null) continue;

            // 2. 현재 타겟이 없으면 전투 중이 아닌 것으로 간주 (Pass)
            if (ally.Brain.Target == null) continue;

            // 3. 타겟이 플레이어가 아니라면 (즉, 적군을 조준 중이라면) 전투 중으로 판단
            if (ally.Brain.Target.gameObject.layer != playerLayer)
            {
                return true;
            }
        }

        return false;
    }

    // 아군 리스트 null이 된 개체들 삭제
    private void RemoveNullinAllys()
    {
        allys.RemoveAll(item => !item);
    }
}
