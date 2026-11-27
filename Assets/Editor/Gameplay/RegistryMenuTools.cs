#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 레지스트리 정리 도구. Unity에서 새 원본 에셋을 만든 뒤 목록에 붙일 때 쓴다.
///  · MinionRegistrySO: 아군(메인 소환수) / 일반 적 / 보스 목록을 데이터 타입과 등급(tier)으로 다시 분류한다.
///    엘리트 목록은 손으로 고른 목록이라(910edc3a 'Elite 차저만 설정') 다시 만들지 않는다 —
///    목록에 없는 스폰 가능 엘리트는 경고만 띄우고, 넣을지는 기획자가 정한다.
///  · GrowthRegistrySO.minionDatas·items·equipments: 아직 없는 에셋만 맨 뒤에 붙인다(기존 순서 유지, 지우지 않음).
///    마을 소환수 카드 순서와 보상 대체 경로가 소환수 순서를 쓰므로 목록을 비우고 다시 만들지 않는다.
///    아이템·장비는 Assets/SOData 안의 에셋만 본다.
///  · 상점 목록(ShopRegistrySO.itemPool)은 건드리지 않는다. 상점 진열은 제작실 > 게임 콘텐츠 > 아이템의 '상점 진열'이 맡는다.
/// </summary>
public class RegistryMenuTools
{
    private const string ContentFolder = "Assets/SOData";

    [MenuItem("Tools/기획/레지스트리 새로고침 (새 소환수·아이템·장비 등록)", priority = 22)]
    public static void RefreshAllRegistries()
    {
        var report = new StringBuilder();

        // 1. MinionRegistrySO 검색 및 아군/적군/보스 자동 분류 갱신. 엘리트 목록은 유지한다.
        string[] minionRegGuids = AssetDatabase.FindAssets("t:MinionRegistrySO");
        if (minionRegGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(minionRegGuids[0]);
            MinionRegistrySO minionReg = AssetDatabase.LoadAssetAtPath<MinionRegistrySO>(path);
            if (minionReg != null)
            {
                Undo.RecordObject(minionReg, "레지스트리 새로고침");
                minionReg.allyMinionData.Clear();
                minionReg.enemyMinionData.Clear();
                minionReg.bossMinionData.Clear();
                var missingElites = new List<string>();

                string[] minionGuids = AssetDatabase.FindAssets("t:MinionDataSO");
                foreach (var guid in minionGuids)
                {
                    string minionPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (minionPath.Contains("/Deprecated/")) continue;
                    MinionDataSO minionAsset = AssetDatabase.LoadAssetAtPath<MinionDataSO>(minionPath);
                    if (minionAsset == null) continue;

                    // 분류 기준은 타입이다. 예전엔 경로 문자열("/SOData/Minion/")로 갈랐는데,
                    // 그러면 에셋을 폴더 밖으로 옮기는 순간 적이 아군 풀에 섞인다.
                    // 서브 소환수 삭제로 아군은 메인만 남았다.
                    if (minionAsset is MainMinionDataSO)
                    {
                        if (!minionReg.allyMinionData.Contains(minionAsset))
                        {
                            minionReg.allyMinionData.Add(minionAsset);
                        }
                        continue;
                    }

                    if (minionAsset is not EnemyMinionDataSO enemyAsset) continue;

                    // 예전엔 이름/파일명에 "Boss" 가 들어가는지로 분류했다. 그래서
                    // 'Summoner Boss Minions' 같은 일반 몹이 보스 목록에 들어갔다.
                    // 이제 데이터의 등급(EnemyMinionDataSO.tier) 하나만 본다 — 오분류가 구조적으로 불가능하다.
                    if (enemyAsset.tier == EnemyTier.Boss)
                    {
                        if (!minionReg.bossMinionData.Contains(enemyAsset))
                        {
                            minionReg.bossMinionData.Add(enemyAsset);
                        }
                    }
                    else if (enemyAsset.tier == EnemyTier.Elite)
                    {
                        // 엘리트 목록은 층 엘리트 추첨 풀이라 손으로 고른다(등급으로 다시 채우면 빼 둔 엘리트가 되살아난다).
                        // 스폰 가능한데 목록에 없는 것만 알려준다. 2페이즈 데이터(isPhaseVariant)는 원래 안 넣는다.
                        if (enemyAsset.IsSpawnableElite && !minionReg.eliteMinionData.Contains(enemyAsset))
                        {
                            missingElites.Add(enemyAsset.name);
                        }
                    }
                    else
                    {
                        // 일반 적군 분류
                        if (!minionReg.enemyMinionData.Contains(enemyAsset))
                        {
                            minionReg.enemyMinionData.Add(enemyAsset);
                        }
                    }
                }

                EditorUtility.SetDirty(minionReg);
                AssetDatabase.SaveAssetIfDirty(minionReg);
                Debug.Log($"<color=green>[MinionRegistry]</color> 자동 분류 갱신 완료! 아군: {minionReg.allyMinionData.Count}, 적군: {minionReg.enemyMinionData.Count}, 엘리트(유지): {minionReg.eliteMinionData.Count}, 보스: {minionReg.bossMinionData.Count}");
                if (missingElites.Count > 0)
                    Debug.LogWarning($"[MinionRegistry] 엘리트 목록에 없는 스폰 가능 엘리트 {missingElites.Count}개: {string.Join(", ", missingElites)}\n" +
                                     $"층 엘리트로 나오게 하려면 {minionReg.name} 의 엘리트 목록에 직접 추가하세요. 이 메뉴는 엘리트 목록을 바꾸지 않습니다.", minionReg);

                report.AppendLine($"[{minionReg.name}]");
                report.AppendLine($"· 다시 분류: 아군 {minionReg.allyMinionData.Count} / 일반 적 {minionReg.enemyMinionData.Count} / 보스 {minionReg.bossMinionData.Count}");
                report.AppendLine($"· 엘리트 목록 {minionReg.eliteMinionData.Count}개는 그대로 유지(손으로 고른 목록)");
                if (missingElites.Count > 0)
                    report.AppendLine($"· 엘리트 목록에 없는 스폰 가능 엘리트 {missingElites.Count}개 — 콘솔 경고 참고. 필요하면 직접 추가하세요.");
            }
        }
        else
        {
            Debug.LogWarning("[MinionRegistry] 프로젝트 내에서 MinionRegistrySO 에셋을 찾을 수 없습니다.");
            report.AppendLine("MinionRegistrySO 에셋을 찾지 못했습니다.");
        }

        // 2. GrowthRegistrySO: 빠진 메인 소환수·아이템·장비만 맨 뒤에 붙인다. 목록을 비우고 다시 만들면
        //    손으로 맞춘 순서(마을 카드 순서, InventoryManager 의 minionType 대체 검색)가 바뀌므로 그렇게 하지 않는다.
        string[] growthGuids = AssetDatabase.FindAssets("t:GrowthRegistrySO");
        GrowthRegistrySO growth = growthGuids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<GrowthRegistrySO>(AssetDatabase.GUIDToAssetPath(growthGuids[0])) : null;
        if (growth != null)
        {
            Undo.RecordObject(growth, "레지스트리 새로고침");
            report.AppendLine($"\n[{growth.name}]");
            int added = AppendMissing(growth.minionDatas, "t:MainMinionDataSO", "Assets", "소환수", report)
                      + AppendMissing(growth.items, "t:ItemSO", ContentFolder, "아이템", report)
                      + AppendMissing(growth.equipments, "t:EquipmentSO", ContentFolder, "장비", report);
            if (added > 0)
            {
                EditorUtility.SetDirty(growth);
                AssetDatabase.SaveAssetIfDirty(growth);
            }
            report.AppendLine("· 상점 아이템 목록은 바꾸지 않습니다(제작실 > 게임 콘텐츠 > 아이템의 상점 진열).");
        }
        else
        {
            report.AppendLine("\nGrowthRegistrySO 에셋을 찾지 못했습니다.");
        }

        EditorUtility.DisplayDialog("레지스트리 새로고침 완료", report.ToString(), "확인");
    }

    /// <summary>folder 안의 filter 에셋 중 list 에 없는 것을 찾은 순서대로 맨 뒤에 붙이고 붙인 개수를 돌려준다.</summary>
    private static int AppendMissing<T>(List<T> list, string filter, string folder, string label, StringBuilder report) where T : Object
    {
        var added = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets(filter, new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Deprecated/")) continue;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null || list.Contains(asset)) continue;
            list.Add(asset);
            added.Add(asset.name);
        }
        if (added.Count > 0)
        {
            Debug.Log($"<color=green>[GrowthRegistry]</color> 새 {label} {added.Count}개를 맨 뒤에 등록: {string.Join(", ", added)}");
            report.AppendLine($"· 새 {label} {added.Count}개를 맨 뒤에 등록: {string.Join(", ", added)}");
        }
        else report.AppendLine($"· 새로 등록할 {label} 없음");
        return added.Count;
    }
}
#endif
