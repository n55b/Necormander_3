#if LEVEL_WORKSHOP_QA
using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class WorkshopPlayerCheck
{
    private IEnumerator CheckFreshRecipient(string path,bool reopen)
    {
        yield return Click("레벨 제작실");
        yield return Wait(()=>WorkshopTrial.Library!=null && !UnityNote.SceneLoader.IsLoading);
        var incoming=WorkshopPackage.Read(path,WorkshopTrial.Library);
        if (!reopen)
        {
            Require(WorkshopWorkspace.ActiveId=="" && !Directory.EnumerateFiles(WorkshopFiles.Folder).Any(),"fresh recipient storage");
            Directory.CreateDirectory(WorkshopWorkspace.Shared);
            File.Copy(path,Path.Combine(WorkshopWorkspace.Shared,"handoff.levelpack"));
            yield return Click("공유"); yield return Click("제작 묶음"); yield return Click("비교 handoff.levelpack");
            yield return Click("새 작업 공간으로 열기");
        }
        Require(WorkshopWorkspace.ActiveId!="" && incoming.rooms.All(r=>
            WorkshopFiles.Read(WorkshopFiles.FileFor(r.id),WorkshopTrial.Library).Serialize()==r.Serialize()),
            "fresh process resolves exact room revisions");
        yield return Click("게임 설정"); yield return Click("마을"); yield return Click("무대 플레이");
        yield return Wait(()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="VillageScene" &&
            GameManager.Instance!=null && GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        var transferred=incoming.village.actors.Single(a=>a.id=="village-guide");
        var npc=FindObjectsByType<NPCBase>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(n=>n.WorkshopId==transferred.id);
        Require(Vector2.Distance(npc.WorkshopActor.position,transferred.position)<.001f &&
            npc.WorkshopGreeting==transferred.greeting && JsonUtility.ToJson(LightingPreview.Active)==JsonUtility.ToJson(incoming.village.lighting),
            "fresh process plays village with transferred NPC and lighting");
        Finish(true);
    }

    private IEnumerator CheckSharing(string profiles,string roomId)
    {
        var catalog=WorkshopTrial.Library;
        string firstHome=Path.Combine(profiles,"A"),secondHome=Path.Combine(profiles,"B");
        yield return Click("공유");
        yield return Click("제작 묶음");
        GameObject.Find("묶음 이름 입력").GetComponent<TMP_InputField>().text="마을과 튜토리얼";
        yield return Click("묶음 내보내기");
        string original=Directory.GetFiles(WorkshopWorkspace.Shared,"*.levelpack").Single();
        File.Copy(original,Path.Combine(output,"handoff.levelpack"),true);
        var sent=WorkshopPackage.Read(original,catalog);
        Require(sent.floor.slots.Count>=2 && sent.village.actors.Count>0 && sent.rooms.Any(r=>r.id==roomId),
            "package includes saved rooms, linked route and village");
        var missing=WorkshopPackage.Read(original,catalog);
        missing.rooms.RemoveAll(r=>r.id==missing.floor.slots[0].document);
        bool rejected=false;
        try { missing.Validate(catalog); } catch (InvalidDataException) { rejected=true; }
        Require(rejected,"missing room dependency rejected before import");

        var incompatible=WorkshopPackage.Read(original,catalog);
        incompatible.templates[0].hash="old revision";
        rejected=false;
        try { incompatible.Validate(catalog); } catch (InvalidDataException) { rejected=true; }
        Require(rejected,"incompatible base revision rejected before import");
        WorkshopWorkspace.UseTestHome(secondHome);
        Directory.CreateDirectory(WorkshopWorkspace.Shared);
        string received=Path.Combine(WorkshopWorkspace.Shared,Path.GetFileName(original));
        File.Copy(original,received);
        yield return Click("제작 묶음");
        yield return Click("비교 "+Path.GetFileName(received));
        yield return Click("새 작업 공간으로 열기");
        string receivedId=WorkshopWorkspace.ActiveId;
        Require(receivedId!="" && WorkshopFiles.Read(WorkshopFiles.FileFor(roomId),catalog).reward==3 &&
            WorkshopVillage.Load().actors.Any(a=>!string.IsNullOrWhiteSpace(a.greeting)),"blank receiver opened self-contained workspace");
        WorkshopWorkspace.UseTestHome(secondHome);
        Require(WorkshopWorkspace.ActiveId==receivedId,"workspace selection survives reopening storage");
        yield return Click("게임 설정");
        yield return Click("튜토리얼");
        yield return Click("무대 플레이");
        yield return Wait(()=>GameManager.Instance!=null && GameManager.Instance.IsPlayerReady && !UnityNote.SceneLoader.IsLoading);
        Require(TutorialFlow.IsRunning && sent.floor.slots.Where(s=>s.scenario==WorkshopFloor.Scenario.Tutorial)
            .All(s=>WorkshopTrial.RoomAt(s.cell)?.id==s.document),"recipient plays transferred route with its original room links");
        var input=GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerInput>();
        if (input!=null && input.user.valid) UnityEngine.InputSystem.Users.InputUser.PerformPairingWithDevice(mouse,input.user);
        yield return Click("편집으로  F6");
        yield return Wait(()=>!WorkshopTrial.Active && GameObject.Find("무대 플레이")!=null);
        yield return Click("내 작업");
        yield return Click("편집 "+roomId);
        yield return Click("전투 설정"); yield return Click("메인 소환"); yield return Click("닫기"); yield return Click("저장");
        yield return Click("공유"); yield return Click("제작 묶음");
        GameObject.Find("묶음 이름 입력").GetComponent<TMP_InputField>().text="전투 수정";
        yield return Click("묶음 내보내기");
        string returned=Directory.GetFiles(WorkshopWorkspace.Shared,"*.levelpack").Single(path=>path!=received);
        WorkshopWorkspace.UseTestHome(firstHome);
        string inbox=Path.Combine(WorkshopWorkspace.Shared,Path.GetFileName(returned));
        File.Copy(returned,inbox);
        var local=WorkshopFiles.Read(WorkshopFiles.FileFor(roomId),catalog);
        local.reward=1;
        WorkshopFiles.WriteAtomic(WorkshopFiles.FileFor(roomId),local.Serialize());
        string originalRoom=File.ReadAllText(WorkshopFiles.FileFor(roomId));
        var restoredBytes=File.ReadAllText(WorkshopFiles.FileFor(roomId));
        WorkshopFiles.WriteAtomic(WorkshopFiles.FileFor(roomId),"{");
        var damaged=new WorkshopPackageReview(WorkshopPackage.Read(inbox,catalog),catalog);
        Require(!damaged.parts.Single(p=>p.key==roomId).canChoose &&
            File.ReadAllText(WorkshopFiles.FileFor(roomId))=="{","damaged local room preserved while team revision is available");
        WorkshopFiles.WriteAtomic(WorkshopFiles.FileFor(roomId),restoredBytes);
        var changed=new WorkshopPackageReview(WorkshopPackage.Read(inbox,catalog),catalog);
        Require(changed.parts.Single(p=>p.key==roomId).summary.Contains("양쪽에서 수정"),"common parent detects independent edits to the same reward");
        yield return Click("제작 묶음");
        yield return Click("비교 "+Path.GetFileName(inbox));
        yield return Click("차이만 보기");
        while (GameObject.Find("개정 "+roomId)==null) yield return Click("다음 항목");
        yield return Click("개정 "+roomId);
        yield return Click("내 버전 사용");
        yield return Capture("sharing-conflict");
        yield return Click("새 작업 공간으로 열기");
        string merged=WorkshopWorkspace.ActiveId;
        Require(merged!="" && WorkshopFiles.Read(WorkshopFiles.FileFor(roomId),catalog).reward==1,"review imports selected local revision");
        yield return Click("작업 공간 기본");
        Require(WorkshopWorkspace.ActiveId=="" && File.ReadAllText(WorkshopFiles.FileFor(roomId))==originalRoom,
            "switching back restores the byte-identical original room");
        yield return Click("제작 묶음");
        yield return Click("비교 "+Path.GetFileName(inbox));
        yield return Click("새 작업 공간으로 열기");
        Require(WorkshopWorkspace.ActiveId!=merged && WorkshopFiles.Read(WorkshopFiles.FileFor(roomId),catalog).reward==2,
            "team revision can be opened independently");
        yield return Click("작업 공간 기본");
    }
}
#endif
