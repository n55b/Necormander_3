using System.Collections.Generic;
using UnityEngine;

public class DataManager : MonoBehaviour
{
    [SerializeField] List<SummonSO> summons1com;
    [SerializeField] List<SummonSO> summons2com;

    public List<SummonSO> SUMMONS1 {get{return summons1com;}}
    public List<SummonSO> SUMMONS2 {get{return summons2com;}}

    // 플레이어 입력을 바탕으로 소환 할 오브젝트를 찾아주는 함수
    public GameObject SummonAlly(List<CommandData> _datas)
    {
        if(_datas.Count == 1)
        {
            foreach(var summon in summons1com)
            {
                GameObject obj = summon.Summon(_datas[0]);
                if(!ReferenceEquals(obj, null))
                {
                    return obj;
                }
            }
        }
        else if(_datas.Count == 2)
        {
            foreach(var summon in summons2com)
            {
                GameObject obj = summon.Summon(_datas[1]);
                if(!ReferenceEquals(obj, null))
                {
                    return obj;
                }
            }
        }

        return null;
    }
}
