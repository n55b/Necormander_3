using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "MapGenerationData", menuName = "Map/MapGenerationData")]
public class MapGenerationDataSO : ScriptableObject
{
    [Header("General Settings")]
    public int totalRoomCount = 15;
    public float gridUnit = 1.0f;
    
    [Header("Room Distribution")]
    public int minNormalRooms = 5;
    public int shopCount = 1;
    public int rewardCount = 2;
    public int eliteCount = 1;

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
}
