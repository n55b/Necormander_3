using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CharacterStat))]
public class CharacterStatEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CharacterStat stat = (CharacterStat)target;

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Calculated Stats (Debug)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            
            EditorGUILayout.FloatField("CUR HP", stat.CURHP);
            EditorGUILayout.FloatField("MAX HP", stat.MAXHP);
            EditorGUILayout.FloatField("ATK", stat.ATK);
            EditorGUILayout.FloatField("ATK SPD", stat.ATKSPD);
            EditorGUILayout.FloatField("ATK RANGE", stat.ATKRANGE);
            EditorGUILayout.FloatField("DEF", stat.DEF);
            EditorGUILayout.FloatField("MOVE SPEED", stat.MOVESPEED);
            EditorGUILayout.FloatField("EVASION", stat.EVASION);
            EditorGUILayout.FloatField("MISS CHANCE", stat.MISS_CHANCE);
            EditorGUILayout.FloatField("RESPAWN BONUS", stat.RESPAWN_BONUS);
            
            EditorGUI.EndDisabledGroup();
            
            // --- [추가된 디버프/특수 상태 모니터링] ---
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🩸 Debuff & Shield Status", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical("box");
            if (stat.Status != null)
            {
                EditorGUILayout.LabelField($"FLAT DEF (고정 방어력): {stat.FLAT_DEF:F2}");
                EditorGUILayout.Space(5);
                
                if (stat.Status.GetDebuffBool(DebuffBoolType.Bleeding))
                    EditorGUILayout.LabelField($"- Bleeding (Tier: {stat.Status.GetDebuffTier(DebuffBoolType.Bleeding)}) -> Taking Bleed DOT");
                
                if (stat.Status.GetDebuffBool(DebuffBoolType.Wounded))
                {
                    int t = stat.Status.GetDebuffTier(DebuffBoolType.Wounded);
                    float amp = t == 1 ? 15f : t == 2 ? 30f : 45f;
                    EditorGUILayout.LabelField($"- Wounded (Tier: {t}) -> Damage Amp: +{amp}%");
                }
                
                if (stat.Status.GetDebuffBool(DebuffBoolType.Corroded))
                {
                    int t = stat.Status.GetDebuffTier(DebuffBoolType.Corroded);
                    float red = t == 1 ? 12f : t == 2 ? 16f : 20f;
                    EditorGUILayout.LabelField($"- Corroded (Tier: {t}) -> Flat Def Reduction: -{red}");
                }
                
                if (stat.Status.GetDebuffBool(DebuffBoolType.Fractured))
                {
                    int t = stat.Status.GetDebuffTier(DebuffBoolType.Fractured);
                    float slow = t == 1 ? 15f : t == 2 ? 30f : 45f;
                    EditorGUILayout.LabelField($"- Fractured (Tier: {t}) -> Move/Atk Speed: -{slow}%");
                }
                if (stat.Status.GetDebuffBool(DebuffBoolType.Stunned))
                    EditorGUILayout.LabelField("- Stunned");
                    
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Total Shield: {stat.Status.TotalShield:F2}");
                EditorGUILayout.LabelField($"Vulnerability Stacks: {stat.Status.VulnerabilityStacks}");
            }
            EditorGUILayout.EndVertical();
            
            // To ensure it repaints constantly while selected in Play Mode
            Repaint();
        }
    }
}
