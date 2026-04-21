using UnityEngine;

/// <summary>
/// 미니언(아군/적군 공용)의 마스터 데이터입니다. 
/// 스탯과 고유 공격 패턴(Attack State)을 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewMinionData", menuName = "Necromancer/Data/MinionData")]
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
    public int cost;

    [Header("고유 행동 패턴")]
    public FSMStateSO attackState; // 이 유닛만의 특수한 공격/행동 패턴 (예: 힐, 자폭 등)

    [Header("투척 효과 (아군 전용)")]
    public BaseThrowImpactSO throwImpact;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}
