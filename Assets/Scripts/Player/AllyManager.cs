using System.Collections.Generic;
using UnityEngine;

public class AllyManager : MonoBehaviour
{
    [Header("아군 유닛들")]
    [SerializeField] List<AllyController> allys;
    [SerializeField] bool isBattle = false;
    [SerializeField] LayerMask playerLayer;

    [Header("아군 유닛 프리팹")]
    [SerializeField] GameObject allyPrefab;

    // 아군 유닛 소환 함수
    public void SpawnAlly(Vector2 _position)
    {
        // 리스트 정리
        RemoveNullinAllys();

        GameObject obj = Instantiate(allyPrefab);
        obj.transform.position = _position;
        AllyController _ally = obj.GetComponent<AllyController>();
        allys.Add(_ally);
        // Init으로 지정하든 해야 할듯
        _ally.player = this.gameObject.transform;
        _ally.SetBattleState(isBattle);

        Debug.Log("Ally 스폰");
    }

    // 아군 전투 중인지 상태 받아서 유닛들에게 뿌려주는 함수
    public void SetBattleState(bool _bool)
    {
        isBattle = _bool;

        RemoveNullinAllys(); // 리스트 정리

        foreach(var ally in allys)
        {
            ally.SetBattleState(isBattle);
        }
    }

    // 아군 유닛들이 전투 중인지 확인하는 함수
    public bool CheckAllyState()
    {
        foreach(var ally in allys)
        {
            if(ally.FSM.target.gameObject.layer != playerLayer)
            {
                return true;
            }
        }

        return false;
    }

    // 아군 리스트 null이 된 개체들 삭제
    private void RemoveNullinAllys()
    {
        allys.RemoveAll(item => item == null);
    }
}
