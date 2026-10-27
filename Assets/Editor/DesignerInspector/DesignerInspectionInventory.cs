using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DesignerInspectionInventory
{
    [Serializable] public class Entry
    {
        public string owner, field, type, tooltip, header, source;
    }
    [Serializable] public class Report { public List<Entry> fields = new List<Entry>(); }
    [MenuItem("Tools/기획/코드 조절항목 조사")]
    public static void Export()
    {
        var report = new Report();
        var scripts = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Scripts" })
            .Select(AssetDatabase.GUIDToAssetPath).ToArray();
        var assembly = typeof(PlayerController).Assembly;
        foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName))
        {
            if (type.IsEnum || type.IsInterface) continue;
            if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type) && !type.IsSerializable) continue;
            var outer = type;
            while (outer.DeclaringType != null) outer = outer.DeclaringType;
            string source = scripts.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == outer.Name);
            if (source == null) source = scripts.FirstOrDefault(p => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(p), @"\b(class|struct)\s+" + System.Text.RegularExpressions.Regex.Escape(outer.Name.Split('`')[0]) + @"\b"));
            if (source == null) continue;
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsStatic || f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField)) && !f.IsDefined(typeof(SerializeReference))) continue;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType.IsInterface) continue;
                if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() != typeof(List<>)) continue;
                report.fields.Add(new Entry { owner = type.FullName, field = f.Name, type = f.FieldType.ToString(), source = source,
                    tooltip = f.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "", header = f.GetCustomAttribute<HeaderAttribute>()?.header ?? "" });
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Designer-fields.json", JsonUtility.ToJson(report, true));
        Debug.Log($"[DesignerInventory] {report.fields.Count} fields / {report.fields.Select(f => f.owner).Distinct().Count()} types");
    }
}
