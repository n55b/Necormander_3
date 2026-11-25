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
        if (Keyboard.current?.f6Key.wasPressedThisFrame==true) WorkshopTrial.Open();
    }
}
