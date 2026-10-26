using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CharacterStat))]
public class CharacterStatEditor : Editor
{
    private DesignerInspectorView designerView;
    public override void OnInspectorGUI()
    {
        (designerView ??= new DesignerInspectorView()).Draw(serializedObject);

        CharacterStat stat = (CharacterStat)target;

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("실행 중 최종 능력치 (읽기 전용)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            
            EditorGUILayout.FloatField("현재 체력", stat.CURHP);
            EditorGUILayout.FloatField("최대 체력", stat.MAXHP);
            EditorGUILayout.FloatField("물리 공격력", stat.ATK);
            EditorGUILayout.FloatField("마법 공격력", stat.MAGIC);
            EditorGUILayout.FloatField("공격 속도 (회/초)", stat.ATKSPD);
            EditorGUILayout.FloatField("공격 간격 (초)", stat.AttackInterval);
            EditorGUILayout.FloatField("방어율 % (상한 75)", stat.DEF);
            EditorGUILayout.FloatField("이동 속도", stat.MOVESPEED);
            EditorGUILayout.FloatField("치명타 확률 %", stat.CRIT_CHANCE);
            EditorGUILayout.FloatField("치명타 피해 %", stat.CRIT_DAMAGE);
            EditorGUILayout.FloatField("회피율", stat.EVASION);
            EditorGUILayout.FloatField("명중률", stat.ACCURACY);

            if (stat.IsPlayer)
            {
                EditorGUILayout.FloatField("스킬 재사용 대기 감소", stat.SKILL_CDR);
                EditorGUILayout.FloatField("행운 (%)", stat.LUCK);
                EditorGUILayout.FloatField("대시 재사용 대기 감소", stat.DASH_CDR);
                EditorGUILayout.FloatField("기본 공격 배율", stat.BASIC_ATK_MULT);
            }
            else
            {
                EditorGUILayout.FloatField("공격 범위", stat.ATKRANGE);
                EditorGUILayout.EnumPopup("공격 종류", stat.ATTACK_TYPE);
            }

            EditorGUI.EndDisabledGroup();
            
            // --- [상태이상/쉴드 모니터링] ---
            // Phase 5 에서 상태이상 5종이 들어오면 여기에 줄을 추가하면 된다.
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("상태이상과 보호막", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");
            if (stat.Status != null)
            {
                if (stat.Status.HasStatus(StatusType.Stun))
                    EditorGUILayout.LabelField("- 기절 중");
                if (stat.Status.HasStatus(StatusType.Hitstun))
                    EditorGUILayout.LabelField("- 경직 중");

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"슈퍼아머: {stat.Status.SuperArmorGauge:F0} / {stat.Status.MaxSuperArmorGauge:F0}" +
                                           (stat.Status.HasSuperArmor ? "" : "  (파괴됨)"));
                EditorGUILayout.LabelField($"전체 보호막: {stat.Status.TotalShield:F2}");
            }
            EditorGUILayout.EndVertical();
            
            // To ensure it repaints constantly while selected in Play Mode
            Repaint();
        }
    }
}
