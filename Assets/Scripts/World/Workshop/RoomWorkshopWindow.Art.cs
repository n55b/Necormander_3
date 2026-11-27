using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomWorkshopWindow
{
    private WorkshopArtSource.Entry[] artEntries;
    private WorkshopArtSource.Entry art;
    private string artSearch="", artBasis;
    private byte[] currentArt, nextArt;
    private int artPage, artRequest;
    private Texture2D artOriginal,artCandidate;
    private bool artReady;
    private void DisposeArt()
    {
        artRequest++;
        if(artOriginal!=null)Destroy(artOriginal);
        if(artCandidate!=null)Destroy(artCandidate);
        artOriginal=null;artCandidate=null;
    }
    private void OpenArt(WorkshopArtSource.Entry item)
    {
        var source=WorkshopArtSource.Read(WorkshopArtSource.Source(item),item);
        DisposeArt();art=item;currentArt=source;nextArt=null;
        artBasis=WorkshopArtSource.Digest(source);artReady=false;
        Draw(page);PreviewArt(source,false);
        RememberRecent(Area.Art,item.source,System.IO.Path.GetFileNameWithoutExtension(item.source));
    }
    private async void PreviewArt(byte[] source,bool candidate)
    {
        int request=++artRequest;
        try
        {
            if(candidate){nextArt=source;artReady=false;}
            var png=await WorkshopArtSource.Preview(WorkshopArtSource.Executable(),source);
            if(this==null||request!=artRequest)return;
            var tex=new Texture2D(2,2){filterMode=FilterMode.Point};
            if(!tex.LoadImage(png)){Destroy(tex);throw new InvalidOperationException("그림을 읽을 수 없습니다.");}
            if(candidate){if(artCandidate!=null)Destroy(artCandidate); artCandidate=tex;artReady=true;}
            else {if(artOriginal!=null)Destroy(artOriginal);artOriginal=tex;}
            if(page==Page.Art)Draw(page);
        }
        catch(Exception error){if(this!=null && request==artRequest)Say(error.Message);}
    }

    private void DrawArt()
    {
        if(art==null)
        {
            artEntries??=WorkshopArtSource.Entries();
            var search=Input("아트 검색",artSearch,12,39,335,24);
            search.onEndEdit.AddListener(query=>{if(query==artSearch)return;artSearch=query;artPage=0;Draw(page);});
            Btn("기준 팔레트",470,39,148,24,()=>OpenArt(artEntries.Single(a=>a.source=="PALETTE/necrohanderPalette.aseprite")));
            var entries=artEntries.Where(e=>e.source.IndexOf(artSearch,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Math.Max(1,(entries.Length+7)/8);
            artPage=Mathf.Clamp(artPage,0,pages-1);
            for(int n=artPage*8;n<Math.Min(entries.Length,(artPage+1)*8);n++)
            {
                var item=entries[n];
                Btn(item.source,12,73+n%8*27,608,24,()=>OpenArt(item));
            }
            Btn("이전",12,299,60,23,()=>{artPage--;Draw(page);});
            WorkshopUI.Label(content,(artPage+1)+" / "+pages,79,299,72,23);
            Btn("다음",158,299,60,23,()=>{artPage++;Draw(page);});return;
        }
        WorkshopUI.Label(content,art.source+"   "+art.width+"×"+art.height+"   "+art.frames+"f",12,40,610,23);
        Picture("현재 원본",artOriginal,12);Picture("변경 미리보기",artCandidate,325);
        var input=Input("가져올 .aseprite 경로","",12,228,426,23);
        Btn("교체본 비교",451,228,166,23,()=>{
            var file=input.text.Trim().Trim('"');
            PreviewArt(WorkshopArtSource.Read(file,art),true);
        });
        if(art.source=="PALETTE/necrohanderPalette.aseprite")
        {
            byte[] data=nextArt??currentArt;
            int offset=WorkshopArtSource.PaletteOffset(data,out int count);
            if(offset>=0)
            {
                WorkshopUI.Label(content,"색 번호 0–"+(count-1),12,260,101,23);
                var index=Input("색 번호","0",114,260,62,23);
                WorkshopUI.Label(content,"RGB",187,260,40,23);
                var rgb=Input("색상 코드","FFFFFF",231,260,120,23);
                Btn("색 비교",365,260,110,23,()=>{
                    if(!int.TryParse(index.text,out int n)||n<0||n>=count||rgb.text.Trim().TrimStart('#').Length!=6||
                        !ColorUtility.TryParseHtmlString("#"+rgb.text.Trim().TrimStart('#'),out var color))
                        throw new InvalidOperationException("색 번호와 RGB 여섯 자리를 확인하세요.");
                    var edited=(byte[])data.Clone();Color32 c=color;int i=offset+n*3;
                    edited[i]=c.r;edited[i+1]=c.g;edited[i+2]=c.b;PreviewArt(edited,true);
                });
            }
        }
        Btn("목록",12,300,80,23,()=>{DisposeArt();art=null;currentArt=null;nextArt=null;Draw(page);});
        Btn("원본 위치",106,300,104,23,()=>Application.OpenURL(new Uri(System.IO.Path.GetDirectoryName(WorkshopArtSource.Source(art))).AbsoluteUri));
        Btn("ARTIST 원본 저장",445,300,173,23,SaveArt,true);
    }
    private void Picture(string title,Texture2D image,int x)
    {
        WorkshopUI.Frame(content,title,x,70,294,144);
        WorkshopUI.Label(content,title,x+5,72,285,20);
        if(image==null)return;
        float scale=Math.Min(280f/image.width,117f/image.height);
        if(scale>1)scale=Mathf.Floor(scale);
        float w=image.width*scale,h=image.height*scale;
        var rect=WorkshopUI.Rect(content,title+" preview",x+(294-w)/2,92+(118-h)/2,w,h);
        var graphic=rect.gameObject.AddComponent<RawImage>();graphic.texture=image;graphic.raycastTarget=false;
    }
    private void SaveArt()
    {
        if(art==null||!artReady) throw new InvalidOperationException("교체본을 비교한 뒤 저장하세요.");
        WorkshopArtSource.Save(art,artBasis,nextArt);
        OpenArt(art);Say("ARTIST 저장됨 · Unity의 아트 동기화 후 적용됩니다.");
    }
}
