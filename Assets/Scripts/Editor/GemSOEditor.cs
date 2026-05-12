using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GemSO의 인스펙터를 커스터마이징하여 [SerializeReference] 리스트에 
/// 하위 클래스(GemStatEffect 등)를 쉽게 추가할 수 있도록 돕는 에디터 스크립트입니다.
/// </summary>
[CustomEditor(typeof(GemSO))]
public class GemSOEditor : Editor
{
    private SerializedProperty _effectsProperty;

    private void OnEnable()
    {
        _effectsProperty = serializedObject.FindProperty("effects");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. 기본 필드들 그리기 (effects 리스트와 m_Script 제외)
        DrawPropertiesExcluding(serializedObject, "effects", "m_Script");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Gem Effects List", EditorStyles.boldLabel);

        // 2. 효과 리스트 그리기
        for (int i = 0; i < _effectsProperty.arraySize; i++)
        {
            SerializedProperty element = _effectsProperty.GetArrayElementAtIndex(i);
            
            // 박스 형태로 각 효과 구분
            EditorGUILayout.BeginVertical(GUI.skin.box);
            
            EditorGUILayout.BeginHorizontal();
            // 현재 클래스 이름 추출 (GemStatEffect 등)
            string typeName = element.managedReferenceFullTypename.Split(' ').LastOrDefault();
            if (string.IsNullOrEmpty(typeName)) typeName = "Empty (Null)";
            else typeName = typeName.Split('.').LastOrDefault();

            EditorGUILayout.LabelField($"[{i}] {typeName}", EditorStyles.miniBoldLabel);
            
            // 삭제 버튼
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                _effectsProperty.DeleteArrayElementAtIndex(i);
                serializedObject.ApplyModifiedProperties();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            // 효과 내부 필드들 그리기
            EditorGUILayout.PropertyField(element, true);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // 3. 새 효과 추가 버튼
        EditorGUILayout.Space();
        if (GUILayout.Button("Add New Gem Effect", GUILayout.Height(30)))
        {
            ShowTypeSelectionMenu();
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// GemEffect를 상속받는 모든 클래스를 찾아 드롭다운 메뉴로 보여줍니다.
    /// </summary>
    private void ShowTypeSelectionMenu()
    {
        GenericMenu menu = new GenericMenu();
        
        // 프로젝트 내의 모든 GemEffect 하위 클래스 검색
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(GemEffect).IsAssignableFrom(p) && !p.IsAbstract);

        foreach (var type in types)
        {
            // 메뉴 아이템 추가 (예: GemStatEffect)
            menu.AddItem(new GUIContent(type.Name), false, () => AddEffect(type));
        }
        
        menu.ShowAsContext();
    }

    private void AddEffect(Type type)
    {
        serializedObject.Update();
        
        int index = _effectsProperty.arraySize;
        _effectsProperty.InsertArrayElementAtIndex(index);
        
        // 선택한 타입의 인스턴스를 생성하여 리스트에 할당
        _effectsProperty.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
        
        serializedObject.ApplyModifiedProperties();
    }
}
