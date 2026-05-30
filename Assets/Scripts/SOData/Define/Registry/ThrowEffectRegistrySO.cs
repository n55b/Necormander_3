using UnityEngine;

/// <summary>
/// 투척 카테고리별 시각 효과(VFX) 및 장판 프리팹을 관리하는 레지스트리입니다.
/// </summary>
[CreateAssetMenu(fileName = "ThrowEffectRegistry", menuName = "Necromancer/Registry/ThrowEffectRegistry")]
public class ThrowEffectRegistrySO : ScriptableObject
{
    [Header("아군 (직구/포물선)")]
    public GameObject allyfullchargeVFX;
    public GameObject allynochargeVFX;
    [Header("적군 (직구/포물선)")]
    public GameObject enemyfullchargeVFX;
    public GameObject enemynochargeVFX;

    [Header("CC (사제/슬로우)")]
    public GameObject ccAreaPrefab;
    public GameObject ccAttachVFX;

    [Header("Shield (방패병/보호막)")]
    public GameObject shieldAreaPrefab;
    public GameObject shieldAttachVFX;
    public GameObject shieldCollectiblePrefab;

    [Header("Formation (창병/넉백)")]
    public GameObject formationAreaVFX;

    [Header("Basic Impact (기본 데미지 장판)")]
    public GameObject basicAreaVFX;

    [Header("Debuff Special (상태 이상 특수 효과)")]
    public GameObject bloodPopVFX;
    
    [Header("Poison Unique (중독 유니크 전용)")]
    public GameObject poisonFootprintPrefab;
    public GameObject poisonPotionPrefab;

    [Header("BloodPop Unique (비폭 유니크 전용)")]
    public GameObject meltingCorpsePuddlePrefab;

    [Header("Aging Unique (노화 유니크 전용)")]
    public GameObject goryeojangAuraPrefab;
}
