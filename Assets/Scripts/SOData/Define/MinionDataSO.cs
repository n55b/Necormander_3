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

    [Header("투척 효과 설정")]
    public ThrowEffectCategory throwEffectCategory;
    public float baseEffectValue; // CC위력, 쉴드량, 넉백힘 등 (전사/궁수는 투척 데미지로 사용)
    public float effectMultiplier = 1.0f; // 효과 배율 지수 (위력 조절)

    [Tooltip("궁수(Archer) 타입일 때만 사용되는 범위 반지름입니다.")]
    public float baseAreaRadius = 3.0f; 

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

    [Header("프리팹 설정")]
    public GameObject minionPrefab;
}
