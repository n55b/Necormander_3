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
            EditorGUILayout.FloatField("ATK (물리)", stat.ATK);
            EditorGUILayout.FloatField("MAGIC (마법)", stat.MAGIC);
            EditorGUILayout.FloatField("ATK SPD (회/초)", stat.ATKSPD);
            EditorGUILayout.FloatField("ATK INTERVAL (초)", stat.AttackInterval);
            EditorGUILayout.FloatField("DEF % (상한 75)", stat.DEF);
            EditorGUILayout.FloatField("MOVE SPEED", stat.MOVESPEED);
            EditorGUILayout.FloatField("CRIT CHANCE %", stat.CRIT_CHANCE);
            EditorGUILayout.FloatField("CRIT DAMAGE %", stat.CRIT_DAMAGE);
            EditorGUILayout.FloatField("EVASION", stat.EVASION);
            EditorGUILayout.FloatField("ACCURACY", stat.ACCURACY);

            if (stat.IsPlayer)
            {
                EditorGUILayout.FloatField("SKILL CDR", stat.SKILL_CDR);
                EditorGUILayout.FloatField("DASH CDR", stat.DASH_CDR);
                EditorGUILayout.FloatField("BASIC ATK MULT", stat.BASIC_ATK_MULT);
            }
            else
            {
                EditorGUILayout.FloatField("ATK RANGE", stat.ATKRANGE);
                EditorGUILayout.EnumPopup("ATTACK TYPE", stat.ATTACK_TYPE);
            }

            EditorGUI.EndDisabledGroup();
            
            // --- [상태이상/쉴드 모니터링] ---
            // Phase 5 에서 상태이상 5종이 들어오면 여기에 줄을 추가하면 된다.
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("🩸 Status & Shield", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");
            if (stat.Status != null)
            {
                if (stat.Status.HasStatus(StatusType.Stun))
                    EditorGUILayout.LabelField("- Stunned (기절)");
                if (stat.Status.HasStatus(StatusType.Hitstun))
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
