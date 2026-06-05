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
            
            // To ensure it repaints constantly while selected in Play Mode
            Repaint();
        }
    }
}
