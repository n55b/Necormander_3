using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "MapGenerationData", menuName = "Map/MapGenerationData")]
public class MapGenerationDataSO : ScriptableObject
{
    [Header("General Settings")]
    public int totalRoomCount = 15;
    public float gridUnit = 1.0f;
    
    [Header("Isaac Style Settings")]
    [Tooltip("기존 물리 분산 방식 대신 아이작 스타일 그리드 배치 및 텔레포트 이동 사용 여부")]
    public bool useIsaacStylePlacement = true;
    [Tooltip("아이작 스타일 배치 시 방 간의 배치 간격 (물리 충돌 방지용)")]
    public float gridSpacing = 160f;
    
    [Header("Room Distribution")]
    public int minNormalRooms = 5;
    public int shopCount = 1;
    [Tooltip("장비 강화 전용 상점의 수. 진열품 없이 NPC 에게 F 를 눌러 착용 장비를 강화하는 방. " +
             "층당 1개가 기본. 0 이면 그 층엔 안 나온다.")]
    public int enhanceShopCount = 1;
    public int rewardCount = 2;
    public int eliteCount = 1;
    [Tooltip("증강 선택 방의 수. 전투 시작 전 페널티 카드를 고르고, 클리어 보상이 그 카드로 정해지는 방. " +
             "층당 1개가 기본. 0 이면 그 층엔 안 나온다.")]
    public int augmentRoomCount = 1;
    // 일반 방 보상 배분. 이 숫자들이 '어떤 풀을 어디서 파밍하는가'의 유일한 스위치다.
    // 특정 풀을 Reward 방으로 옮기거나 로비 고정으로 돌리고 싶으면 여기 카운트를 0으로 내리면 된다.
    [Tooltip("일반 전투 방 중 플레이어 스킬 보상을 배정할 정확한 방의 수량")]
    public int playerSkillRewardRoomCount = 3;
    [Tooltip("일반 전투 방 중 메인 소환수 보상을 배정할 정확한 방의 수량")]
    public int mainSummonRewardRoomCount = 3;
    [Tooltip("일반 전투 방 중 서브 소환수 보상을 배정할 정확한 방의 수량")]
    public int subSummonRewardRoomCount = 3;
    [Tooltip("위 카운트를 다 채우고 남는 일반 방에도 무작위 보상을 배정할지. 끄면 남는 방은 보상 없음.")]
    public bool fillRemainingRoomsRandomly = true;

    [Header("Encounter Spawning Settings")]
    [Tooltip("일반방 전투 웨이브 수 (기본 2)")]
    public int wavesCount = 2;
    [Tooltip("적 개체 간 최소 간격")]
    public float minDistanceBetweenEnemies = 2.0f;
    [Tooltip("최소 간격을 만족하지 못할 때 포기할 최대 시도 횟수")]
    public int maxSpawnAttempts = 10;

    [Header("Spreading Settings")]
    public float spreadingForce = 5f;
    public float maxVelocityThreshold = 0.1f;
    public int maxPhysicsIteration = 500;
    public float minSpawnRadius = 5f;
    public float maxSpawnRadius = 12f;

    [Header("Corridor Settings")]
    public TileBase floorTile;
    public TileBase wallTile;
    public TileBase shadowTile;

    [Header("Door Settings")]
    public GameObject doorUp;
    public GameObject doorDown;
    public GameObject doorLeft;
    public GameObject doorRight;

    [Tooltip("방의 벽을 뚫고 나올 때 강제로 나가는 직선 거리")]
    public int corridorStraightLength = 3;
    [Tooltip("통로가 다른 방의 벽을 피해가는 최소 거리 (통로 벽 두께 감안)")]
    public int corridorAvoidMargin = 2;

    [Header("Boss Settings")]
    [Tooltip("층수별 고정 보스. '몇 층에 누가 나오는가'의 유일한 스위치다 — 여기 없는 층은 " +
             "보스 층이 아니라서 보스 방 자체가 생성되지 않고, 있는 층은 항상 지정된 보스가 나온다.")]
    public List<FloorBossEntry> floorBosses = new List<FloorBossEntry>();

    [System.Serializable]
    public class FloorBossEntry
    {
        [Tooltip("GameManager.currentFloor 와 비교할 층수 (1부터)")]
        public int floor = 4;
        public EnemyMinionDataSO boss;
    }

    /// <summary>해당 층에 배정된 보스를 반환한다. 배정이 없으면 null(= 보스 층이 아님).</summary>
    public EnemyMinionDataSO GetBossForFloor(int floor)
    {
        if (floorBosses == null) return null;
        foreach (var entry in floorBosses)
        {
            if (entry != null && entry.floor == floor) return entry.boss;
        }
        return null;
    }
}
