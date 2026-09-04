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
    [Tooltip("일반 전투 방 중 아이템 보상을 배정할 정확한 방의 수량.\n" +
             "[26/08/15] 서브 소환수 보상 방을 아이템 보상 방으로 전환했다. 필드 이름만 바뀌었고 " +
             "직렬화 값은 FormerlySerializedAs 로 그대로 이어받는다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("subSummonRewardRoomCount")]
    public int itemRewardRoomCount = 4;
    [Tooltip("위 카운트를 다 채우고 남는 일반 방에도 무작위 보상을 배정할지. 끄면 남는 방은 보상 없음.")]
    public bool fillRemainingRoomsRandomly = true;

    [Header("Encounter Spawning Settings")]
    [Tooltip("일반방 전투 웨이브 수 (기본 2)")]
    public int wavesCount = 2;
    [Tooltip("적 개체 간 최소 간격")]
    public float minDistanceBetweenEnemies = 2.0f;
    [Tooltip("최소 간격을 만족하지 못할 때 포기할 최대 시도 횟수")]
    public int maxSpawnAttempts = 10;
    [Tooltip("문 앞 스폰 금지 구역의 폭. 통로와 직각인 방향의 전체 너비(유닛).\n\n" +
             "이 구역 안에는 적이 안 뜬다 — 문틈에 낀 적이 전투 후 닫힌 문 뒤에 갇혀서 못 잡는 사고를 막는다.\n" +
             "방 프리팹 루트를 고르면 씬 뷰에 빨간 상자로 그려진다. 0 으로 두면 이 기능이 통째로 꺼진다.")]
    public float doorKeepOutWidth = 5f;
    [Tooltip("문에서 '방 안쪽'으로 파고드는 깊이(유닛). 문 앞 광장을 얼마나 비워둘지.")]
    public float doorKeepOutInward = 4f;
    [Tooltip("문에서 '방 바깥'(통로 쪽)으로 나가는 깊이(유닛). 문틈과 통로에 끼는 걸 막는 쪽.")]
    public float doorKeepOutOutward = 4f;

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

    [Header("Tutorial")]
    [Tooltip("튜토리얼 방을 '지나갈 순서대로'. 최초 1회 새 게임에서 이 방들만 배치한다.\n\n" +
             "★ 배치 모양은 여기서 정하지 않는다 — 각 방 프리팹의 DoorAnchor 방향을 따라 걸어가며 " +
             "격자에 놓는다. 그러니 길을 꺾고 싶으면 방 프리팹의 앵커만 돌리면 된다.\n" +
             "비워두면 튜토리얼은 통째로 건너뛰고 평소 맵이 나온다.")]
    public List<GameObject> tutorialRooms = new List<GameObject>();

    [Tooltip("마지막 튜토리얼 방 한가운데에 세울 '마을로 나가는' 포탈. F 로 상호작용한다.\n" +
             "비우면 튜토리얼을 빠져나갈 길이 없다(에러 로그가 뜬다).")]
    public GameObject tutorialExitPortal;

    [Header("Floor Tuning")]
    [Tooltip("층별 난이도 조절표. 여기 없는 층은 아래 전역 기본값을 그대로 쓴다 — " +
             "즉 한 층만 저작해도 나머지 층은 손대지 않은 것과 완전히 동일하다.")]
    public List<FloorTuningEntry> floorTuning = new List<FloorTuningEntry>();

    /// <summary>
    /// 한 층의 난이도 배율. <b>군집(EnemyClusterSO) 에셋은 절대 건드리지 않는다</b> —
    /// 군집은 "어떤 조합으로 싸우는가"(조성)이고 여기는 "그걸 얼마나 주는가"(양)다.
    /// 둘을 섞으면 층이 하나 늘 때마다 군집 N개를 전부 다시 저작해야 한다.
    /// 그래서 배율은 저작 시점이 아니라 <b>스폰 직전(소비 시점)</b>에 먹인다.
    /// </summary>
    [System.Serializable]
    public class FloorTuningEntry
    {
        [Tooltip("GameManager.currentFloor 와 비교할 층수. 0은 튜토리얼, 일반 층은 1부터")]
        public int floor = 1;

        [Tooltip("켜면 이 층의 엘리트 방은 적을 소환하지 않고, 입장 시 즉시 클리어된다. " +
                 "기존 엘리트 방의 포탈·보상·클리어 이벤트는 그대로 실행된다.")]
        public bool skipEliteEncounter = false;

        [Tooltip("이 층의 방당 전투 웨이브 수. 0 이면 전역 wavesCount 를 그대로 쓴다")]
        public int wavesCount = 0;

        [Tooltip("군집 마릿수 배율. 0.5 면 4마리 군집이 2마리가 된다. 1 이면 그대로. " +
                 "증강 페널티(웨이브당 적 +N)는 플레이어가 고른 것이라 이 배율을 안 탄다")]
        [Range(0.1f, 3f)] public float enemyCountScale = 1f;

        [Tooltip("이 층에 나올 군집만 추린 목록. 비워두면 레지스트리 전체에서 뽑는다. " +
                 "새 군집을 저작하지 않고도 층별 몹 구성을 가를 수 있는 자리다")]
        public List<EnemyClusterSO> clusterPool = new List<EnemyClusterSO>();

        [Header("방 개수 (-1 = 전역값 그대로)")]
        [Tooltip("이 층의 방 총합(스폰 방 포함). 특수방을 다 빼고 남는 만큼이 일반 방이 된다")]
        public int totalRoomCount = -1;
        [Tooltip("일반 방 최소 보장 수. totalRoomCount 를 아무리 낮춰도 이 아래로는 안 내려간다")]
        public int minNormalRooms = -1;
        public int shopCount = -1;
        public int enhanceShopCount = -1;
        public int rewardCount = -1;
        public int eliteCount = -1;
        public int augmentRoomCount = -1;
    }

    /// <summary>한 층에 실제로 적용될 방 개수 묶음. 조절표에서 -1 로 남긴 항목은 전역값이 들어온다.</summary>
    public struct RoomCounts
    {
        public int total, minNormal, shop, enhanceShop, reward, elite, augment;

        /// <summary>스폰 방을 뺀, 이 층에 놓을 특수방 총 개수.</summary>
        public int Specials => shop + enhanceShop + reward + elite + augment;
    }

    /// <summary>
    /// 이 층의 방 개수. 방 배치 코드는 <b>generationData 필드를 직접 읽지 말고 반드시 이걸 거쳐야</b>
    /// 층별 저작이 먹는다. 전역 필드와 층 오버라이드가 갈라지는 지점은 여기 한 곳뿐이다.
    /// </summary>
    public RoomCounts GetRoomCountsForFloor(int floor)
    {
        var t = GetTuningForFloor(floor);
        return new RoomCounts
        {
            total       = Pick(t, t == null ? -1 : t.totalRoomCount,   totalRoomCount),
            minNormal   = Pick(t, t == null ? -1 : t.minNormalRooms,   minNormalRooms),
            shop        = Pick(t, t == null ? -1 : t.shopCount,        shopCount),
            enhanceShop = Pick(t, t == null ? -1 : t.enhanceShopCount, enhanceShopCount),
            reward      = Pick(t, t == null ? -1 : t.rewardCount,      rewardCount),
            elite       = Pick(t, t == null ? -1 : t.eliteCount,       eliteCount),
            augment     = Pick(t, t == null ? -1 : t.augmentRoomCount, augmentRoomCount),
        };

        // 0 은 유효한 값이다("이 층엔 상점 없음"). 그래서 미지정 표식은 0 이 아니라 -1 이어야 한다.
        static int Pick(FloorTuningEntry t, int over, int fallback) => (t != null && over >= 0) ? over : fallback;
    }

    /// <summary>해당 층의 조절표. 없으면 null(= 전역 기본값을 쓰라는 뜻).</summary>
    public FloorTuningEntry GetTuningForFloor(int floor)
    {
        if (floorTuning == null) return null;
        foreach (var entry in floorTuning)
        {
            if (entry != null && entry.floor == floor) return entry;
        }
        return null;
    }

    /// <summary>이 층의 웨이브 수. 조절표가 없거나 0 이면 전역 <see cref="wavesCount"/>.</summary>
    public int GetWavesForFloor(int floor)
    {
        var t = GetTuningForFloor(floor);
        return (t != null && t.wavesCount > 0) ? t.wavesCount : wavesCount;
    }

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
