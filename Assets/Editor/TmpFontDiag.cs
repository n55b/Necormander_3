using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TMPro;

public static class TmpFontDiag
{
    [MenuItem("Tools/TMP Font Diag2")]
    public static void Run()
    {
        var sb = new StringBuilder();

        string sample = "옵션소리크기전체화면해상도언어나가기계속하기밝기음악효과음적용취소저장불러오기";

        foreach (var g in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
            if (fa == null) continue;
            sb.AppendLine("FONT " + fa.name);
            sb.AppendLine("   atlas=" + fa.atlasWidth + "x" + fa.atlasHeight + " pad=" + fa.atlasPadding
                + " render=" + fa.atlasRenderMode + " mode=" + fa.atlasPopulationMode
                + " multiAtlas=" + fa.isMultiAtlasTexturesEnabled
                + " cachedChars=" + fa.characterTable.Count);
            var mat = fa.material;
            sb.AppendLine("   material=" + (mat ? mat.name : "null") + " shader=" + (mat && mat.shader ? mat.shader.name : "null"));

            uint[] missing;
            bool all = fa.HasCharacters(sample, out missing);
            int missCount = missing == null ? 0 : missing.Length;
            sb.AppendLine("   hasAllSampleHangul=" + all + " missingCount=" + missCount);
            if (missing != null && missing.Length > 0)
            {
                var s2 = new StringBuilder();
                for (int i = 0; i < missing.Length && i < 30; i++) s2.Append(char.ConvertFromUtf32((int)missing[i]));
                sb.AppendLine("   missingChars=" + s2.ToString());
            }
            var sf = fa.sourceFontFile;
            sb.AppendLine("   sourceFontFile=" + (sf ? AssetDatabase.GetAssetPath(sf) : "NULL"));
        }

        var outSb = new StringBuilder();
        outSb.AppendLine("// TMP FONT DIAG2 RESULT");
        foreach (var line in sb.ToString().Replace("\r\n", "\n").Split('\n'))
        {
            outSb.AppendLine("// " + line);
        }
        outSb.AppendLine("public static class TmpFontDiagResult2 { }");
        File.WriteAllText("Assets/Editor/TmpFontDiagResult2.cs", outSb.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();
    }
}
