#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    private void OpenComparison()
    {
        EndStroke();
        var template=catalog.FindRoom(Doc.template);
        // The saved prefab itself, without the edits in progress.
        var saved=WorkshopRoom.Create(template);
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
        Btn("편집으로",495,299,130,23,()=>{
            comparison.Dispose();comparison=null;preview.ResizeFrame(MapWidth,WorkshopPreview.Height);
            RefreshSelection();preview.MarkStart(start);Draw(Page.Map);
        },true);
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
            pieceFields=(x,y);
            var xResult=WorkshopUI.Label(content,"",38,76,150,14);
            var yResult=WorkshopUI.Label(content,"",225,76,150,14);
            var help=WorkshopUI.Label(content,"",12,91,370,15);
            var original=position;
            // Each box accepts a number or arithmetic; an unfinished formula keeps the last good value.
            bool Axis(TMP_InputField field,TMP_Text result,out float value)
            {
                value=0;
                bool ok=WorkshopMath.TryEvaluate(field.text,out double computed) && System.Math.Abs(computed)<=512;
                if(ok) value=(float)computed;
                bool formula=WorkshopMath.IsFormula(field.text);
                result.text=!ok ? (formula ? "식 입력 중" : "-512 ~ 512") : formula ? "= "+value.ToString("0.###",CultureInfo.InvariantCulture) : "";
                result.color=ok ? (Color)WorkshopUI.Mint : new Color32(252,203,103,255);
                return ok;
            }
            bool Read(out Vector3 value)
            {
                value=original;
                bool formula=WorkshopMath.IsFormula(x.text) || WorkshopMath.IsFormula(y.text);
                help.text=(formula ? "Enter 계산 → 한 번 더 Enter 적용" : "Enter 적용 · 계산식 가능")+" · Tab X↔Y"+(snapMode>0 ? " · "+SnapLabel : "");
                help.color=formula ? new Color32(252,203,103,255) : WorkshopUI.Paper;
                bool okX=Axis(x,xResult,out float px), okY=Axis(y,yResult,out float py);
                if(!okX || !okY) return false;
                value=SnapLocal(new Vector3(px,py,original.z));
                if(snapMode>0)
                {
                    if(Mathf.Abs(value.x-px)>.0001f) xResult.text+=(xResult.text.Length>0 ? " " : "")+"→ "+value.x.ToString("0.###",CultureInfo.InvariantCulture);
                    if(Mathf.Abs(value.y-py)>.0001f) yResult.text+=(yResult.text.Length>0 ? " " : "")+"→ "+value.y.ToString("0.###",CultureInfo.InvariantCulture);
                }
                return true;
            }
            // Typing moves the object on the map at once; the document changes only when applied.
            void Live(string _)
            {
                if(!Read(out var next)) return;
                target.Value.position=preview.World(next);
                preview.PositionOutlines();
            }
            x.onValueChanged.AddListener(Live); y.onValueChanged.AddListener(Live);
            var shownIn=preview;
            revertPiece=()=>{
                // The map may already be closed or rebuilt; only the preview the panel belongs to is touched.
                if(preview!=shownIn || shownIn==null || target.Value==null) return;
                target.Value.position=shownIn.World(original); shownIn.PositionOutlines();
            };
            Btn("취소",12,108,90,24,()=>Draw(page));
            applyPiece=()=>{
                if(!Read(out var next)) throw new System.IO.InvalidDataException("좌표 또는 계산식을 확인하세요 (-512 ~ 512).");
                revertPiece=null; pieceFields=default;
                before=Doc.Serialize();
                EditPiece(target.Key).local=next;target.Value.position=preview.World(next);
                preview.PositionOutlines(); EndStroke();Draw(page);
            };
            string Number(double v) => ((float)v).ToString("0.###",CultureInfo.InvariantCulture);
            x.onSubmit.AddListener(_=>SubmitNumber(x,Number,applyPiece)); y.onSubmit.AddListener(_=>SubmitNumber(y,Number,applyPiece));
            Read(out _);
            x.Select(); x.ActivateInputField();
            Btn("배치 적용",247,108,120,24,applyPiece);
        }
        finally{content=previous;}
    }
    private System.Action applyPiece, revertPiece;
    private (TMPro.TMP_InputField x, TMPro.TMP_InputField y) pieceFields;
}
#endif
