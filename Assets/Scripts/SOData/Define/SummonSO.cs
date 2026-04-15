using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SummonSO", menuName = "SummonSO")]
public class SummonSO : ScriptableObject
{
    [SerializeField] private GameObject summonAlly;
    [TextArea(1,1)]
    public string note = "1. 전사 || 2. 아처 || 3. 방패병 || 4. 사제";
    [SerializeField] private List<CommandData> summonCommand;

    public GameObject Summon(List<CommandData> _list)
    {
        bool canSummon = CheckCommand(_list);

        if(canSummon)
        {
            return summonAlly;
        }

        return null;
    }

    private bool CheckCommand(List<CommandData> _list)
    {
        for(int i = 0; i < _list.Count; i++)
        {
            if(summonCommand[i] != _list[i]) return false;
        }

        return true;
    }
}
