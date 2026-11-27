#if UNITY_EDITOR || LEVEL_WORKSHOP
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class WorkshopReturn : MonoBehaviour
{
    private void Start()
    {
        var canvas=WorkshopUI.Canvas("Workshop trial toolbar",32000);
        canvas.transform.SetParent(transform,false);
        if (WorkshopTrial.Village!=null && gameObject.scene.name=="VillageScene")
            gameObject.AddComponent<WorkshopVillageEditor>().Initialize(canvas,WorkshopTrial.Village);
        WorkshopUI.Button(canvas.transform,"편집으로  F6",275,4,90,19,()=>WorkshopTrial.Open());
        if (WorkshopTrial.Village==null)
            WorkshopUI.Label(canvas.transform,"R 다시 시작"+(WorkshopTrial.Walking ? "  ·  걷기 모드 · 적 없음" : ""),371,4,260,19);
    }
    internal static bool OverButton
    {
        get
        {
            if (!WorkshopTrial.Active || Mouse.current==null) return false;
            var point=PixelViewport.ScreenToGamePoint(Mouse.current.position.ReadValue());
            return point.y>=337 && point.y<=356 && ((point.x>=275 && point.x<=365) ||
                (WorkshopTrial.Village!=null && point.x>=8 && point.x<=92));
        }
    }
    private void Update()
    {
        var keys=Keyboard.current;
        if (keys==null) return;
        if (keys.f6Key.wasPressedThisFrame) { WorkshopTrial.Open(); return; }
        if (keys.rKey.wasPressedThisFrame && !Typing()) WorkshopTrial.Restart();
    }
    private static bool Typing()
    {
        var focused=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        return focused!=null && focused.TryGetComponent<TMPro.TMP_InputField>(out var field) && field.isFocused;
    }
}
#endif
