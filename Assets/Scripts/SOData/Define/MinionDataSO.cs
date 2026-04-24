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

    [Header("AI 행동 패턴")]
    public AIPatternSO aiPattern; // 이 유닛의 전체적인 AI 행동 (대기/추격/공격 통합)

    [Header("투척 효과 (아군 전용)")]
    public BaseThrowImpactSO throwImpact;

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}
