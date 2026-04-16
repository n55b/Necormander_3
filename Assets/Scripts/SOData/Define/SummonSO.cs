using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SummonSO", menuName = "SummonSO")]
public class SummonSO : ScriptableObject
{
    [SerializeField] private GameObject summonAlly;
    [TextArea(1, 2)]
    public string note = "[1티어 소환수]\n1. 전사 || 2. 아처 || 3. 방패병 || 4. 사제";
    [SerializeField] private CommandData summonCommand;



    public GameObject Summon(CommandData _com)
    {
        bool canSummon = CheckCommand(_com);

        if (canSummon)
        {
            return summonAlly;
        }

        return null;
    }


    // 체크 알고리즘
    private bool CheckCommand(CommandData _com)
    {
        if(_com == summonCommand)
            return true;

        return false;
    }
}
