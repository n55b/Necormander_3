using UnityEngine;

/// <summary>
/// 특정 미니언 타입의 모든 데이터(스탯, 투척 효과 등)를 관리하는 마스터 SO입니다.
/// </summary>
[CreateAssetMenu(fileName = "MinionData", menuName = "Necromancer/MinionData")]
public class MinionDataSO : ScriptableObject
{
    public CommandData minionType;
    public string minionName;

    [Header("기본 능력치")]
    public float maxHP = 100f;
    public float attack = 10f;
    public float attackSpeed = 1f;
    public float attackRange = 2f;
    public float detectRange = 10f;
    public float defense = 0f;
    public float moveSpeed = 5f;

    [Header("투척 효과")]
    public BaseThrowImpactSO throwImpact;

    [Header("소환 설정")]
    public GameObject minionPrefab;
}
