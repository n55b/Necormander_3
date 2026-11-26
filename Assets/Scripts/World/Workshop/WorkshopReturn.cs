using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class WorkshopReturn : MonoBehaviour
{
    private static readonly List<RectTransform> blockers=new();
    private static bool dragging;
    private RectTransform statusPanel; private TMP_Text statusResult; private int target;   // 0 가까운 적, 1 모든 적, 2 플레이어

    internal static void Block(RectTransform rect){ if(rect!=null && !blockers.Contains(rect)) blockers.Add(rect); }
    // 도구막대·열린 패널 사각형 안이거나, 그 안에서 시작한 드래그 중이면 전투 클릭을 막는다(LightingDebugPanel 방식).
    internal static bool CapturesPointer=>WorkshopTrial.Active && (dragging || Inside());
    private static bool Inside()
    {
        var mouse=Mouse.current; if(mouse==null) return false;
        blockers.RemoveAll(r=>r==null);
        foreach(var rect in blockers)
        {
            if(!rect.gameObject.activeInHierarchy) continue;
            var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
            if(RectTransformUtility.RectangleContainsScreenPoint(rect,PixelViewport.PointerForCanvas(canvas,mouse.position.ReadValue()),PixelViewport.CanvasCamera(canvas))) return true;
        }
        return false;
    }
    private void Start()
    {
        var canvas=WorkshopUI.Canvas("Workshop trial toolbar",32000);
        canvas.transform.SetParent(transform,false);
        if (WorkshopTrial.Village!=null && gameObject.scene.name=="VillageScene")
            gameObject.AddComponent<WorkshopVillageEditor>().Initialize(canvas,WorkshopTrial.Village);   // Initialize가 '마을 편집' 토글을 Block한다
        Block((RectTransform)WorkshopUI.Button(canvas.transform,"편집으로  F6",275,4,90,19,()=>WorkshopTrial.Open()).transform);
        Block((RectTransform)WorkshopUI.Button(canvas.transform,"상태이상",369,4,64,19,()=>ToggleStatus(canvas),false,"상태이상 부여").transform);
        if(DraftStore.Previewing) WorkshopUI.Label(canvas.transform,"미저장 초안으로 시험 중",437,6,160,15,8).color=WorkshopUI.Mint;
    }
    private void ToggleStatus(Canvas canvas)
    {
        if(statusPanel!=null){ Destroy(statusPanel.gameObject); statusPanel=null; return; }
        statusPanel=WorkshopUI.Frame(canvas.transform,"Trial status",369,25,196,82).rectTransform; Block(statusPanel);
        WorkshopUI.Choice(statusPanel,"대상",4,4,188,18,new[]{"가까운 적","모든 적","플레이어"},()=>target,i=>target=i);   // 버튼 이름 "대상/<라벨>"
        var kinds=new[]{(StatusType.Stun,"기절"),(StatusType.Freeze,"빙결"),(StatusType.Bleed,"출혈"),(StatusType.Poison,"중독"),(StatusType.BloodPop,"비폭"),(StatusType.Vulnerable,"취약")};
        for(int i=0;i<kinds.Length;i++)
        {
            var (type,label)=kinds[i];
            WorkshopUI.Button(statusPanel,label,4+i%3*64,26+i/3*20,60,18,()=>{
                var (hit,total)=ApplyStatus(type,target); statusResult.text=label+" → "+hit+"/"+total+" 걸림"; },false,"부여/"+label);
        }
        // ApplyStatus는 내성(기절 뒤 3초)·슈퍼아머·사망이면 조용히 돌아간다. 연달아 눌러 안 걸리는 것이 고장처럼 보이지 않게 숫자로 보인다.
        statusResult=WorkshopUI.Label(statusPanel,"",4,68,188,11,8);
    }
    // StatusDebugKeys(UNITY_EDITOR·DEVELOPMENT_BUILD 전용)와 달리 제작판 릴리스에서도 컴파일된다.
    // 출혈은 실제 공격처럼 플레이어 공격력을 기억시킨다(CharacterHealth의 power 계산과 같다). 반환: 부여 뒤 실제로 그 상태인 대상 수 / 대상 수.
    internal static (int hit,int total) ApplyStatus(StatusType type,int target)
    {
        var player=GameManager.Instance!=null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if(player==null) return (0,0);
        float power=player.Stat!=null ? player.Stat.ATK*(1f+player.Stat.PHYS_AMP) : 0f;
        var targets=target==2 ? new[]{player.GetComponentInChildren<CharacterStatus>()} :          // 플레이어 상태는 자식 CharacterStatStuff에 있다
            target==1 ? CharacterStatus.ActiveEnemies.ToArray() :
            CharacterStatus.ActiveEnemies.Where(s=>s!=null).OrderBy(s=>(s.transform.position-player.transform.position).sqrMagnitude).Take(1).ToArray();
        int hit=0;
        foreach(var status in targets)   // 순회 중 비폭 폭발이 목록을 바꿀 수 있어 복사본을 돈다
        {
            if(status==null) continue;
            status.ApplyStatus(type,power:power);
            if(status!=null && status.HasStatus(type)) hit++;
        }
        return (hit,targets.Length);
    }
    private void Update()
    {
        var mouse=Mouse.current;
        if(mouse!=null){ if(mouse.leftButton.wasPressedThisFrame) dragging=Inside(); if(!mouse.leftButton.isPressed) dragging=false; }
        if (Keyboard.current?.f6Key.wasPressedThisFrame==true) WorkshopTrial.Open();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBlockers(){ blockers.Clear(); dragging=false; }
}
