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

            EditorGUI.EndDisabledGroup();
            
            // --- [상태이상/쉴드 모니터링] ---
            // Phase 5 에서 상태이상 5종이 들어오면 여기에 줄을 추가하면 된다.
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🩸 Status & Shield", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");
            if (stat.Status != null)
            {
                if (stat.Status.GetDebuffBool(DebuffBoolType.Stunned))
                    EditorGUILayout.LabelField("- Stunned (기절)");
                if (stat.Status.GetDebuffBool(DebuffBoolType.Hitstunned))
                    EditorGUILayout.LabelField("- Hitstunned (경직)");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Super Armor: {stat.Status.SuperArmorGauge:F0} / {stat.Status.MaxSuperArmorGauge:F0}" +
                                           (stat.Status.HasSuperArmor ? "" : "  (파괴됨)"));
                EditorGUILayout.LabelField($"Total Shield: {stat.Status.TotalShield:F2}");
            }
            EditorGUILayout.EndVertical();
            
            // To ensure it repaints constantly while selected in Play Mode
            Repaint();
        }
    }
}
