using UnityEngine;

/// <summary>
/// 사망 시 서로 다른 외형(프리팹)을 가진 두 개체로 분화하는 AI 패턴입니다.
/// 예: LionMask가 사망하면 서로 다르게 생긴 LionMask_A, LionMask_B 두 마리로 갈라짐.
/// SlimeAIPatternSO와 분열 컨셉은 같지만, 같은 프리팹을 N번 복제하는 대신
/// 서로 다른 두 프리팹을 각각 1번씩 스폰합니다.
/// </summary>
[CreateAssetMenu(fileName = "DualSplitAIPattern", menuName = "Necromancer/AI/DualSplitPattern")]
public class DualSplitAIPatternSO : BaseAIPatternSO
{
    [Header("분화 설정 (Dual Split)")]
    [Tooltip("분화 시 생성할 첫 번째 개체 프리팹입니다.")]
    public GameObject splitPrefabA;
    [Tooltip("분화 시 생성할 두 번째 개체 프리팹입니다.")]
    public GameObject splitPrefabB;
    [Tooltip("원본 위치 기준 좌우로 얼마나 떨어진 곳에 스폰할지 (서로 반대 방향으로 적용됩니다)")]
    public float spawnOffset = 0.5f;

    private BaseEntity _myEntity;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _myEntity = entity;

        var health = entity.GetComponentInChildren<CharacterHealth>();
        if (health != null)
        {
            // 중복 구독 방지를 위해 한 번 빼주고 다시 등록
            health.OnDeath -= HandleDeath;
            health.OnDeath += HandleDeath;
        }
    }

    private void HandleDeath()
    {
        if (_myEntity == null) return;

        SpawnSplit(splitPrefabA, -1f);
        SpawnSplit(splitPrefabB, 1f);
    }

    private void SpawnSplit(GameObject prefab, float sideSign)
    {
        if (prefab == null) return;

        Vector3 spawnPos = _myEntity.transform.position + new Vector3(sideSign * spawnOffset, 0f, 0f);
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

        if (obj.TryGetComponent<BaseEntity>(out var splitEntity))
        {
            // 1. 프리팹에 등록된 minionData를 기반으로 즉시 Initialize 처리
            MinionDataSO data = splitEntity.MinionData;
            if (data != null)
            {
                splitEntity.Initialize(data);
            }

            // 2. 부모(원본)의 스포너에 동적 등록하여 방 클리어 판정에 귀속시킴
            if (_myEntity.Spawner != null)
            {
                splitEntity.Spawner = _myEntity.Spawner;
                _myEntity.Spawner.RegisterActiveEnemy(obj);
            }
        }
    }
}
