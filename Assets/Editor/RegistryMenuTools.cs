#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 프로젝트의 모든 미니언 데이터(MinionDataSO)와 성장 리소스들을 
/// 자동으로 스캔, 분류 및 갱신해 주는 유니티 에디터 도구 스크립트입니다.
/// </summary>
public class RegistryMenuTools
{
    [MenuItem("Tools/Registry/Refresh All Registries")]
    public static void RefreshAllRegistries()
    {
        // 1. MinionRegistrySO 검색 및 아군/적군/보스 3단 정밀 자동 분류 갱신
        string[] minionRegGuids = AssetDatabase.FindAssets("t:MinionRegistrySO");
        if (minionRegGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(minionRegGuids[0]);
            MinionRegistrySO minionReg = AssetDatabase.LoadAssetAtPath<MinionRegistrySO>(path);
            if (minionReg != null)
            {
                minionReg.allyMinionData.Clear();
                minionReg.enemyMinionData.Clear();
                minionReg.eliteMinionData.Clear();
                minionReg.bossMinionData.Clear();

                string[] minionGuids = AssetDatabase.FindAssets("t:MinionDataSO");
                foreach (var guid in minionGuids)
                {
                    string minionPath = AssetDatabase.GUIDToAssetPath(guid);
                    MinionDataSO minionAsset = AssetDatabase.LoadAssetAtPath<MinionDataSO>(minionPath);
                    if (minionAsset == null) continue;

                    // 아군 미니언 분류 (/SOData/Minion/ 경로 존재 시)
                    if (minionPath.Contains("/SOData/Minion/"))
                    {
                        if (!minionReg.allyMinionData.Contains(minionAsset))
                        {
                            minionReg.allyMinionData.Add(minionAsset);
                        }
                    }
                    // 적군 및 보스, 엘리트 분류 (/SOData/Enemy/ 경로 존재 시)
                    else if (minionPath.Contains("/SOData/Enemy/"))
                    {
                        // 보스 분류 조건: 이름에 Boss가 포함되거나 파일명에 Boss가 포함될 때
                        bool isBossUnit = minionAsset.minionName.Contains("Boss") || 
                                          Path.GetFileNameWithoutExtension(minionPath).Contains("Boss");

                        if (isBossUnit)
                        {
                            if (!minionReg.bossMinionData.Contains(minionAsset))
                            {
                                minionReg.bossMinionData.Add(minionAsset);
                            }
                        }
                        else if (minionAsset.isElite)
                        {
                            // 엘리트 분류 조건: 보스가 아니며 isElite가 켜진 유닛
                            if (!minionReg.eliteMinionData.Contains(minionAsset))
                            {
                                minionReg.eliteMinionData.Add(minionAsset);
                            }
                        }
                        else
                        {
                            // 일반 적군 분류
                            if (!minionReg.enemyMinionData.Contains(minionAsset))
                            {
                                minionReg.enemyMinionData.Add(minionAsset);
                            }
                        }
                    }
                }

                EditorUtility.SetDirty(minionReg);
                Debug.Log($"<color=green>[MinionRegistry]</color> 자동 분류 갱신 완료! 아군: {minionReg.allyMinionData.Count}, 적군: {minionReg.enemyMinionData.Count}, 엘리트: {minionReg.eliteMinionData.Count}, 보스: {minionReg.bossMinionData.Count}");
            }
        }
        else
        {
            Debug.LogWarning("[MinionRegistry] 프로젝트 내에서 MinionRegistrySO 에셋을 찾을 수 없습니다.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("레지스트리 자동 갱신 완료", 
            "모든 미니언(아군/적군/엘리트/보스) 분류 정렬 및 MinionRegistrySO 갱신이 완료되었습니다!", "확인");
    }

    [MenuItem("Tools/Registry/Fix Summoner Boss Projectile Settings")]
    public static void FixSummonerBossProjectileSettings()
    {
        string prefabPath = "Assets/Prefabs/Skill Visual Effects/Projectiles/Summoner Boss Projectile.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("설정 실패", $"프리팹을 찾을 수 없습니다: {prefabPath}", "확인");
            return;
        }

        Projectile projectileComp = prefab.GetComponent<Projectile>();
        if (projectileComp != null)
        {
            // SerializedObject를 사용하여 protected/private 직렬화 필드인 rotateToDirection을 안전하게 수정
            SerializedObject serializedObj = new SerializedObject(projectileComp);
            SerializedProperty rotateProp = serializedObj.FindProperty("rotateToDirection");
            if (rotateProp != null)
            {
                rotateProp.boolValue = false;
                serializedObj.ApplyModifiedProperties();
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("설정 완료", "보스 구체 투사체의 회전 방지 속성(rotateToDirection = false)이 완벽히 저장되었습니다!", "확인");
            }
            else
            {
                EditorUtility.DisplayDialog("오류", "rotateToDirection 프로퍼티를 찾을 수 없습니다.", "확인");
            }
        }
        else
        {
            EditorUtility.DisplayDialog("오류", "프리팹에 Projectile 컴포넌트가 존재하지 않습니다.", "확인");
        }
    }
}
#endif
