using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal sealed class ProjectPlayerTuning
{
    internal const string Document="BALANCE/PLAYER";
    internal const string Sprint="clearedRoomSprint", Push="hurtPushDistance", Aim="aimAssistDegrees",
        Reach="reachAssistDistance", Walk="attackWalkSpeedMultiplier";
    internal static readonly string[] Fields={Sprint,Push,Aim,Reach,Walk};
    internal static readonly string[] Titles={"클리어 방 질주 배율","피격 밀림 거리","조준 보정 각도","거리 보정 한도","공격 중 걷기 배율"};
    internal static readonly float[] Min={1,0,0,0,0}, Max={2,16,12,.25f,1}, Steps={.05f,.05f,.5f,.01f,.05f};
    private Dictionary<string,string> basis;
    private readonly JObject overrides = new();
    private static string Section(int i)=> i<2 ? "movement.json" : "melee.json";

    internal ProjectPlayerTuning()
    {
        basis=ProjectContentStore.WithDocument(Document,(folder,_)=>ProjectContentStore.Read(folder));
        foreach(var pair in basis) foreach(var field in JObject.Parse(pair.Value).Properties()) overrides[field.Name]=field.Value;
        Validate(basis);
    }
    internal float Value(string key,float fallback)=>overrides.TryGetValue(key,out var v) ? (float)v : fallback;
    internal void Set(int i,float value)=>overrides[Fields[i]]=Mathf.Clamp(value,Min[i],Max[i]);
    internal void ResetField(int i)=>overrides.Remove(Fields[i]);
    internal void Save()
    {
        var mine=new Dictionary<string,string>();
        for(int i=0;i<Fields.Length;i++)
        {
            if(!overrides.TryGetValue(Fields[i],out var v)) continue;
            string section=Section(i);
            var group=mine.TryGetValue(section,out string json)?JObject.Parse(json):new JObject();
            group[Fields[i]]=v; mine[section]=ProjectContentStore.Json(group);
        }
        basis=ProjectContentStore.Save(Document,basis,mine,Validate);
        overrides.RemoveAll();
        foreach(var pair in basis) foreach(var field in JObject.Parse(pair.Value).Properties()) overrides[field.Name]=field.Value;
    }
    private static void Validate(Dictionary<string,string> files)
    {
        foreach(var pair in files)
        foreach(var field in JObject.Parse(pair.Value).Properties())
        {
            int i=Array.IndexOf(Fields,field.Name);
            if(i<0 || pair.Key!=Section(i) || field.Value.Type is not (JTokenType.Float or JTokenType.Integer) ||
                !float.IsFinite((float)field.Value) || (float)field.Value<Min[i] || (float)field.Value>Max[i])
                throw new InvalidDataException("플레이어 설정 범위를 확인하세요. "+field.Path);
        }
    }
}

public partial class PlayerController
{
    internal void ApplyProjectTuning(ProjectPlayerTuning data)
    {
        clearedRoomSprint=data.Value(ProjectPlayerTuning.Sprint,clearedRoomSprint);
        hurtPushDistance=data.Value(ProjectPlayerTuning.Push,hurtPushDistance);
    }
    internal float[] WorkshopTuningDefaults() => new[]{clearedRoomSprint,hurtPushDistance};
}
public partial class MeleeCombatController
{
    internal void ApplyProjectTuning(ProjectPlayerTuning data)
    {
        aimAssistDegrees=data.Value(ProjectPlayerTuning.Aim,aimAssistDegrees);
        reachAssistDistance=data.Value(ProjectPlayerTuning.Reach,reachAssistDistance);
        attackWalkSpeedMultiplier=data.Value(ProjectPlayerTuning.Walk,attackWalkSpeedMultiplier);
    }
    internal float[] WorkshopTuningDefaults() => new[]{aimAssistDegrees,reachAssistDistance,attackWalkSpeedMultiplier};
}
