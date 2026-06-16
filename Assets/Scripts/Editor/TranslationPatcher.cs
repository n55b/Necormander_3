using UnityEngine;
using UnityEditor;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System.Collections.Generic;

public class TranslationPatcher : EditorWindow
{
    [MenuItem("Tools/Patch Translations")]
    public static void Patch()
    {
        PatchTable("UI Text Table");
        PatchTable("Reward Text Table");
        Debug.Log("Translation patch completed!");
    }

    private static void PatchTable(string tableName)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
        if (collection == null)
        {
            Debug.LogWarning($"Could not find collection: {tableName}");
            return;
        }

        var koTable = collection.GetTable("ko") as StringTable;
        var enTable = collection.GetTable("en") as StringTable;
        
        if (koTable == null) return;

        var entries = new Dictionary<string, string>()
        {
            {"Skill_PlayerGroundSmash_Name", "바닥 부수기"},
            {"Skill_PlayerGroundSmash_Desc", "플레이어가 바닥을 내리쳐, 2칸 주변의 적을 끌어당기고 취약 1스택을 부여합니다."},
            {"Skill_PlayerTetsuzanko_Name", "철산고"},
            {"Skill_PlayerTetsuzanko_Desc", "전방으로 돌진하며 적을 밀쳐내고 피해를 입힙니다."},
            {"Skill_PlayerGuardBreak_Name", "가드 부수기"},
            {"Skill_PlayerGuardBreak_Desc", "강력한 정권 지르기로 적을 밀쳐내며 피해를 입힙니다."},
            {"Skill_PlayerRapidPunch_Name", "둥둥타"},
            {"Skill_PlayerRapidPunch_Desc", "빠르게 3연타 공격을 날립니다."},
            {"Skill_PlayerStunSmash_Name", "꽁!"},
            {"Skill_PlayerStunSmash_Desc", "기절 상태인 적을 타격하여 기절을 소모하고 피해를 입힙니다."},
            {"Skill_PlayerStrikeFlurry_Name", "총난타"},
            {"Skill_PlayerStrikeFlurry_Desc", "격파 상태인 적을 타격하여 격파를 소모하고 6연타 피해를 입힙니다."},
            {"Skill_PlayerSmashPunch_Name", "육왕권"},
            {"Skill_PlayerSmashPunch_Desc", "강타 상태인 적을 타격하여 강타를 소모하고 치명적인 일격을 가합니다."},
            {"MinionReaction_Type1_Damage_Name", "연계 1"},
            {"MinionReaction_Type1_Damage_Desc", "대상에게 순간이동하여 기본 공격력의 200% 피해를 입힙니다."},
            {"MinionReaction_Type2_1_Push_Name", "연계 2.1"},
            {"MinionReaction_Type2_1_Push_Desc", "대상에게 순간이동하여 적을 넉백시키고 피해를 입힙니다."},
            {"MinionReaction_Type2_2_Pull_Name", "연계 2.2"},
            {"MinionReaction_Type2_2_Pull_Desc", "대상에게 순간이동하여 적을 끌어당기고 피해를 입힙니다."},
            {"MinionReaction_Type4_1_Stun_Name", "연계 4.1"},
            {"MinionReaction_Type4_1_Stun_Desc", "대상에게 순간이동하여 타격하고, 취약 스택이 3스택 이상이면 기절시킵니다."},
            {"MinionReaction_Type4_2_Strike_Name", "연계 4.2"},
            {"MinionReaction_Type4_2_Strike_Desc", "대상에게 순간이동하여 타격하고, 취약 스택이 3스택 이상이면 격파합니다."},
            {"MinionReaction_Type4_3_Smash_Name", "연계 4.3"},
            {"MinionReaction_Type4_3_Smash_Desc", "대상에게 순간이동하여 타격하고, 취약 스택이 3스택 이상이면 강타를 부여합니다."},
            {"MinionReaction_Type5_StunExt_Name", "연계 5"},
            {"MinionReaction_Type5_StunExt_Desc", "대상에게 순간이동하여 타격하고, 대상이 기절 상태이면 기절 시간을 연장합니다."}
        };

        foreach (var kvp in entries)
        {
            if (!collection.SharedData.Contains(kvp.Key))
            {
                collection.SharedData.AddKey(kvp.Key);
            }
            koTable.AddEntry(kvp.Key, kvp.Value);
            if (enTable != null) enTable.AddEntry(kvp.Key, kvp.Value);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(koTable);
        if (enTable != null) EditorUtility.SetDirty(enTable);
        AssetDatabase.SaveAssets();
    }
}
