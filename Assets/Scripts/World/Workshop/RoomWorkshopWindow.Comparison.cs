using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    private void OpenComparison()
    {
        EndStroke();
        var template=catalog.FindRoom(Doc.template);
        var saved=ProjectContentStore.Authoring && projectBasis?.Count>0 ? ProjectRooms.Decode(Doc.id,projectBasis,catalog) :
            !ProjectContentStore.Authoring && persisted!=null ? JsonUtility.FromJson<WorkshopRoom>(persisted) : WorkshopRoom.Create(template);
        comparison?.Dispose();
        comparison=new WorkshopPreview(template,saved,catalog,true);
        preview.ResizeFrame(310,176);
        comparison.ResizeFrame(310,176);
        comparison.MatchView(preview);
        preview.HighlightSelection(System.Array.Empty<string>());
        preview.Hover(null,Vector3.zero,false,Color.clear);
        preview.MarkStart(null);
        Draw(Page.Compare);
    }
    private void DrawComparison()
    {
        void Show(string label,WorkshopPreview map,int x)
        {
            WorkshopUI.Label(content,label,x,58,310,26,14);
            var image=WorkshopUI.Rect(content,label+" render",x,88,310,176).gameObject.AddComponent<RawImage>();
            image.texture=map.Frame;image.raycastTarget=false;
        }
        Show("편집 기준 방",comparison,7);
        Show("편집 중인 방",preview,323);
        Btn("편집으로",495,299,130,23,CloseComparison,true);
    }
    private void CloseComparison()
    {
        comparison.Dispose();comparison=null;preview.ResizeFrame(WorkshopPreview.Width,WorkshopPreview.Height);
        RefreshSelection();preview.MarkStart(start);Draw(Page.Map);
    }
    private void SelectedObject()
    {
        if(selection.Count!=1){Say("지도에서 배치물 하나를 선택하세요.");return;}
        var target=preview.Pieces.Single(p=>selection.Contains(p.Key));
        var panel=WorkshopUI.Frame(content,"배치물 위치",52,84,399,162);
        var previous=content;content=panel.rectTransform;
        try
        {
            var position=preview.Local(target.Value.position);
            WorkshopUI.Label(content,target.Value.name,12,8,370,22,12);
            WorkshopUI.Label(content,"X",12,50,25,23);WorkshopUI.Label(content,"Y",199,50,25,23);
            var x=Input("배치 X",position.x.ToString("R",CultureInfo.InvariantCulture),38,50,142,25);
            var y=Input("배치 Y",position.y.ToString("R",CultureInfo.InvariantCulture),225,50,142,25);
            Btn("취소",12,108,90,24,()=>Draw(page));
            applyPiece=()=>{
                if(!float.TryParse(x.text,NumberStyles.Float,CultureInfo.InvariantCulture,out float px) ||
                    !float.TryParse(y.text,NumberStyles.Float,CultureInfo.InvariantCulture,out float py) ||
                    !float.IsFinite(px)||!float.IsFinite(py)||Mathf.Abs(px)>512||Mathf.Abs(py)>512)
                    throw new System.IO.InvalidDataException("좌표는 -512에서 512 사이로 입력하세요.");
                before=Doc.Serialize();position.x=Mathf.Round(px*32)/32;position.y=Mathf.Round(py*32)/32;
                EditPiece(target.Key).local=position;target.Value.position=preview.World(position);
                preview.PositionOutlines(); EndStroke();Draw(page);
            };
            Btn("배치 적용",247,108,120,24,applyPiece);
        }
        finally{content=previous;}
    }
    private System.Action applyPiece;
}
