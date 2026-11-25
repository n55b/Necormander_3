using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
internal sealed class WorkshopVillage
{
    [Serializable] internal sealed class Actor
    {
        public string id, greeting;
        public Vector2 position;
        public bool hidden;
    }

    public int version = 1;
    public List<Actor> actors = new();
    public bool useLighting;
    public LightingPreview lighting;
    [NonSerialized] private Dictionary<string,string> projectBasis;
    private static string FilePath => Path.Combine(Path.GetDirectoryName(WorkshopFiles.Folder), "village.json");

    internal static WorkshopVillage Load()
    {
        if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 262144) throw new InvalidDataException("마을 파일이 너무 큽니다.");
        var doc = File.Exists(FilePath) ? JsonUtility.FromJson<WorkshopVillage>(File.ReadAllText(FilePath)) : new WorkshopVillage();
        if (doc == null) throw new InvalidDataException("마을 파일을 읽을 수 없습니다.");
        doc.Validate();
        if(ProjectContentStore.Authoring)
        {
            doc.projectBasis=ProjectVillageData.Snapshot();
            ProjectVillageData.Read(doc,doc.projectBasis);
        }
        return doc;
    }

    public void Save()
    {
        Validate();
        if(ProjectContentStore.Authoring)
        {
            projectBasis=ProjectVillageData.Save(this,projectBasis ?? new Dictionary<string,string>());
            return;
        }
        WorkshopFiles.WriteAtomic(FilePath,JsonUtility.ToJson(this,true));
    }

    internal void Validate()
    {
        if (version != 1 || actors == null || actors.Count > 256 || (useLighting && lighting == null))
            throw new InvalidDataException("마을 설정 형식을 확인하세요.");
        if (useLighting)
        {
            var l=lighting;
            foreach (float value in new[] {l.azimuth,l.elevation,l.intensity,l.shadows,l.highlight,l.sun.r,l.sun.g,l.sun.b,
                l.sun.a,l.bloom,l.threshold,l.radius,l.mist,l.shafts,l.airSuppression,l.depthSuppression,l.cloudSuppression,l.wind,l.canopy,l.borderOpacity,l.borderWidth,l.borderSpeed})
                if (!float.IsFinite(value)) throw new InvalidDataException("마을 조명에 읽을 수 없는 값이 있습니다.");
        }
        var seen = new HashSet<string>();
        foreach (var actor in actors)
            if (actor == null || string.IsNullOrEmpty(actor.id) || !seen.Add(actor.id) ||
                !(Mathf.Abs(actor.position.x) <= 32768 && Mathf.Abs(actor.position.y) <= 32768) ||
                actor.greeting?.Length > 1024)
                throw new InvalidDataException("NPC의 위치와 대사를 확인하세요.");
    }
}
