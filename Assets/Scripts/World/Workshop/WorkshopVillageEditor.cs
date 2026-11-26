using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

internal sealed class WorkshopVillageEditor : MonoBehaviour
{
    private WorkshopVillage doc;
    private NPCBase[] actors;
    private readonly Dictionary<string,Vector3> originals = new();
    private readonly Dictionary<string,bool> originallyVisible = new();
    private Canvas canvas;
    private RectTransform panel;
    private TMP_InputField greeting;
    private TMP_Text coordinates, status;
    private int index;
    private bool placing, heldInput;
    private NPCBase Selected => actors[index];

    internal void Initialize(Canvas target, WorkshopVillage draft)
    {
        canvas = target;
        doc = draft;
        var scene = gameObject.scene;
        actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NPCBase>(true))
            .Where(npc => npc.WorkshopActor != null && !string.IsNullOrEmpty(npc.WorkshopId))
            .OrderByDescending(npc => npc.WorkshopActor.gameObject.activeSelf).ThenBy(npc => npc.WorkshopId,StringComparer.Ordinal).ToArray();
        foreach (var npc in actors)
        {
            originallyVisible.Add(npc.WorkshopId,npc.WorkshopActor.gameObject.activeSelf);
            originals.Add(npc.WorkshopId,npc.WorkshopActor.position);
            var saved = doc.actors.Find(actor => actor.id == npc.WorkshopId);
            if (saved == null) continue;
            npc.WorkshopActor.gameObject.SetActive(!saved.hidden);
            Position(npc,saved.position);
            npc.WorkshopGreeting = saved.greeting;
        }
        LightingPreview.Active = doc.useLighting ? doc.lighting : null;
        WorkshopReturn.Block((RectTransform)WorkshopUI.Button(canvas.transform,"마을 편집",8,4,84,19,Toggle).transform);   // 패널은 열면 입력을 막으므로 토글만 등록한다
    }

    private static void Position(NPCBase npc, Vector2 position)
    {
        npc.WorkshopActor.position = new Vector3(position.x,position.y,npc.WorkshopActor.position.z);
        Physics2D.SyncTransforms();
    }

    private void Toggle()
    {
        if (panel != null) { Close(); return; }
        var player = GameManager.Instance?.PLAYERCONTROLLER;
        if (player == null || player.IsInputBlocked || UnityNote.SceneLoader.IsLoading ||
            DialogueUI.Instance != null && DialogueUI.Instance.IsPlaying) return;
        player.SetInputBlocked(true);
        heldInput = true;
        Draw();
    }

    private void Draw()
    {
        if (panel != null) { panel.gameObject.SetActive(false); Destroy(panel.gameObject); }
        panel = WorkshopUI.Frame(canvas.transform,"Village editing",7,29,229,288).rectTransform;
        WorkshopUI.Label(panel,"마을 편집",8,5,158,20,15);
        WorkshopUI.Button(panel,"닫기",177,5,44,18,Close);
        if (actors.Length > 0)
        {
            WorkshopUI.Button(panel,"이전 NPC",8,32,65,20,()=>Select(-1));
            WorkshopUI.Button(panel,"다음 NPC",156,32,65,20,()=>Select(1));
            WorkshopUI.Label(panel,(index+1)+" / "+actors.Length,91,32,65,20);
            string actorName = string.IsNullOrWhiteSpace(Selected.name) ? Selected.WorkshopActor.name : Selected.name;
            WorkshopUI.Label(panel,actorName,8,59,96,19,12);
            coordinates = WorkshopUI.Label(panel,"",106,59,115,19);
            UpdateCoordinates();
            WorkshopUI.Button(panel,"지도에 놓기",8,83,103,21,()=>{
                placing=true; status.text="지도에서 NPC의 발 위치를 누르세요.";
            });
            WorkshopUI.Button(panel,"원래 위치",118,83,103,21,()=>{
                placing=false; Position(Selected,originals[Selected.WorkshopId]); UpdateCoordinates();
            });
            WorkshopUI.Label(panel,"인사말 · 한 줄에 대사 하나",8,112,146,18);
            WorkshopUI.Button(panel,Selected.WorkshopActor.gameObject.activeSelf ? "표시 켜짐" : "표시 꺼짐",154,110,67,19,()=>{
                Selected.WorkshopActor.gameObject.SetActive(!Selected.WorkshopActor.gameObject.activeSelf);
                Draw();
            });
            var field = WorkshopUI.Box(panel,"NPC 인사말",8,132,213,54,WorkshopUI.ButtonColor);
            greeting=field.gameObject.AddComponent<TMP_InputField>();
            greeting.textViewport=field.rectTransform;
            var body=WorkshopUI.Label(field.transform,"",4,1,204,51,10);
            body.GetComponent<PixelText>().UseFaceResolver(WorkshopTrial.Library.FontForInput);
            body.alignment=TextAlignmentOptions.TopLeft;
            body.textWrappingMode=TextWrappingModes.Normal;
            greeting.textComponent=body; greeting.characterLimit=1024;
            greeting.lineType=TMP_InputField.LineType.MultiLineNewline;
            greeting.SetTextWithoutNotify(Selected.WorkshopGreeting ?? "");
            greeting.onValueChanged.AddListener(value=>Selected.WorkshopGreeting=value);
            WorkshopUI.Button(panel,"대사 미리보기",8,190,213,19,()=>{
                if (string.IsNullOrWhiteSpace(Selected.WorkshopGreeting)) { status.text="인사말을 입력하세요."; return; }
                if (DialogueUI.Instance==null) { status.text="대화창이 준비되지 않았습니다."; return; }
                Close();
                DialogueUI.Instance.PlayGreeting(Selected.WorkshopActor.name,actorName,Selected.WorkshopGreeting);
            });
        }
        WorkshopUI.Button(panel,"조명 조정",8,219,102,23,()=>{
            LightingDebugPanel.Instance?.SetOpen(true);status.text="조정 후 마을 저장을 누르세요.";
        });
        WorkshopUI.Button(panel,"마을 저장",117,219,104,23,Save,true);
        status=WorkshopUI.Label(panel,"배치와 대사는 이 마을 시험에 적용됩니다.",8,252,213,29);
    }

    private void Save()
    {
        try
        {
            foreach (var npc in actors)
            {
                doc.actors.RemoveAll(entry=>entry.id==npc.WorkshopId);
                var point=npc.WorkshopActor.position;
                if ((point-originals[npc.WorkshopId]).sqrMagnitude < .00000001f && string.IsNullOrWhiteSpace(npc.WorkshopGreeting) &&
                    originallyVisible[npc.WorkshopId]==npc.WorkshopActor.gameObject.activeSelf) continue;
                doc.actors.Add(new WorkshopVillage.Actor { id=npc.WorkshopId,position=point,greeting=npc.WorkshopGreeting,
                    hidden=!npc.WorkshopActor.gameObject.activeSelf });
            }
            doc.lighting=LightingPreview.Active;
            doc.useLighting=doc.lighting!=null;
            doc.Save(); status.text="마을 설정을 저장했습니다.";
        }
        catch (Exception ex) { status.text=ex.Message; }
    }

    private void Select(int delta)
    {
        index=(index+delta+actors.Length)%actors.Length;
        placing=false; Draw();
    }

    private void UpdateCoordinates()
    {
        var point=Selected.WorkshopActor.position;
        coordinates.text=$"X {point.x:0.##}   Y {point.y:0.##}";
    }

    private void Update()
    {
        var keys=Keyboard.current;
        if(panel!=null && keys!=null && keys.ctrlKey.isPressed && keys.sKey.wasPressedThisFrame)
        {
            if(greeting!=null && greeting.isFocused) greeting.DeactivateInputField();
            Save(); return;
        }
        var mouse=Mouse.current;
        if (!placing || panel==null || mouse==null || !mouse.leftButton.wasPressedThisFrame ||
            LightingDebugPanel.CapturesPointer || WorkshopReturn.CapturesPointer) return;
        var point=mouse.position.ReadValue();
        if (RectTransformUtility.RectangleContainsScreenPoint(panel,PixelViewport.PointerForCanvas(canvas,point),PixelViewport.CanvasCamera(canvas)) ||
            !PixelViewport.TryPointerToWorld(point,out var world)) return;
        float grid=PixelViewport.WorldUnitsPerPixel;
        if (grid<=0) return;
        Position(Selected,new Vector2(Mathf.Round(world.x/grid),Mathf.Round(world.y/grid))*grid);
        placing=false; UpdateCoordinates(); status.text="배치했습니다. 마을 저장으로 보관하세요.";
    }

    private void Close()
    {
        placing=false;
        if (panel!=null) { panel.gameObject.SetActive(false); Destroy(panel.gameObject); panel=null; }
        if (heldInput) GameManager.Instance?.PLAYERCONTROLLER?.SetInputBlocked(false);
        heldInput=false;
    }

    private void OnDestroy()
    {
        if (LightingDebugPanel.Instance != null) LightingDebugPanel.Instance.SetOpen(false);
        WorkshopTrial.Library?.ReleaseInputFonts();
    }
}
