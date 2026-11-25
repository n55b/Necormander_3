using System;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Localization.Tables;
using UnityEditor;
using UnityEngine;

internal sealed class TextMotionPreview
{
    private int index, frame;
    private bool play, english, reduced;
    private string korean = "치명타! 128  획득", latin = "Critical! 128  Reward";
    private double start;
    private TMP_FontAsset font;
    private string validation;
    private static string CheckReferences(TextMotionLibrary library)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var errors=new HashSet<string>();
        foreach(var preset in library.presets)
            if(preset==null || string.IsNullOrWhiteSpace(preset.id) || !ids.Add(preset.id))
                errors.Add("비어 있거나 중복된 프리셋 ID");
        var ranges=new List<TextEffectRange>();
        foreach(string guid in AssetDatabase.FindAssets("t:StringTable",new[]{"Assets"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var table=AssetDatabase.LoadAssetAtPath<StringTable>(path);
            if(table==null) continue;
            foreach(var entry in table.Values)
            {
                TextEffectParser.Parse(entry.LocalizedValue,ranges);
                foreach(var range in ranges)
                {
                    string id=range.preset ?? range.type.ToString();
                    if(!ids.Contains(id)) errors.Add($"{table.name} / {entry.Key} → {id}");
                }
            }
        }
        return errors.Count==0 ? "프리셋 ID와 번역 문구의 연결을 확인했습니다." : string.Join("\n",errors);
    }

    internal void Draw(TextMotionLibrary library)
    {
        if (library == null || library.presets == null || library.presets.Length == 0) return;
        if(GUILayout.Button("번역 문구와 프리셋 ID 검사")) validation=CheckReferences(library);
        if(!string.IsNullOrEmpty(validation)) KoreanEditorText.Help(validation,MessageType.Info);
        index = Mathf.Clamp(index, 0, library.presets.Length - 1);
        index = EditorGUILayout.Popup("프리셋",index,Array.ConvertAll(library.presets, p => p!=null ? p.id : "(빈 프리셋)"));
        var preset = library.presets[index];
        if(preset==null) return;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(play ? "일시정지" : "재생")) { play = !play; start = EditorApplication.timeSinceStartup - frame/30d; }
        if (GUILayout.Button("한 프레임")) { play=false; frame=(frame+1)%181; }
        if (GUILayout.Button("프리셋 복제"))
        {
            Undo.RecordObject(library,"Duplicate text preset");
            var clone = JsonUtility.FromJson<TextMotionLibrary.Preset>(JsonUtility.ToJson(preset));
            string baseID = preset.id + "Copy";
            clone.id = baseID;
            for (int n=2; library.Find(clone.id)!=null; n++) clone.id = baseID+n;
            Array.Resize(ref library.presets, library.presets.Length+1);
            library.presets[index=library.presets.Length-1]=clone;
            EditorUtility.SetDirty(library);
        }
        EditorGUILayout.EndHorizontal();
        if (play) frame = (int)((EditorApplication.timeSinceStartup-start)*30)%181;
        int scrub = EditorGUILayout.IntSlider("프레임 (30fps)",frame,0,180);
        if (scrub!=frame) { play=false; frame=scrub; }
        english=EditorGUILayout.Toggle("영문",english);
        reduced=EditorGUILayout.Toggle("모션 감소 미리보기",reduced);
        string line = english ? (latin=EditorGUILayout.TextField("문장",latin)) : (korean=EditorGUILayout.TextField("문장",korean));
        var area = GUILayoutUtility.GetRect(200,65,GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area,new Color(.14f,.12f,.17f));
        if (font == null) font = Resources.Load<TMP_FontAsset>("PixelFonts/Body");
        if (font == null || Event.current.type != EventType.Repaint) return;
        float x = area.x+8, y = area.center.y+10;
        int word=0;
        bool space=true;
        foreach (var (ch,i) in Characters(line))
        {
            if (!reduced && preset.shape == TextMotionLibrary.Shape.Typewriter &&
                i >= Mathf.FloorToInt(frame * Mathf.Max(1, preset.charactersPerSecond) / 30f)) break;
            if (char.IsWhiteSpace(ch)) { x+=8; space=true; continue; }
            if (space) { word++; space=false; }
            if (!font.characterLookupTable.TryGetValue(ch,out var character)) continue;
            var glyph=character.glyph;
            var r=glyph.glyphRect;
            var atlas=font.atlasTextures[glyph.atlasIndex];
            int unit=preset.unit==TextMotionLibrary.Unit.Label ? 0 : preset.unit==TextMotionLibrary.Unit.Word ? word-1 : i;
            Vector2 offset=preset.Sample(frame/30f,unit,1,out var tint);
            if (reduced) { offset=Vector2.zero; tint=.28f; }
            var pos = new Rect(x+2*(Mathf.Round(offset.x)+glyph.metrics.horizontalBearingX),
                y-2*(glyph.metrics.horizontalBearingY+Mathf.Round(offset.y)),2*r.width,2*r.height);
            var uv=new Rect((float)r.x/atlas.width,(float)r.y/atlas.height,(float)r.width/atlas.width,(float)r.height/atlas.height);
            var old=GUI.color; GUI.color=Color.Lerp(Color.white,reduced ? preset.accent : preset.Accent(frame/30f,unit,1),tint);
            GUI.DrawTextureWithTexCoords(pos,atlas,uv,true); GUI.color=old;
            x+=2*glyph.metrics.horizontalAdvance;
            if (x>=area.xMax-16) break;
        }
    }
    private static System.Collections.Generic.IEnumerable<(char,int)> Characters(string line)
    {
        for(int i=0;i<line.Length;i++) yield return (line[i],i);
    }
}
