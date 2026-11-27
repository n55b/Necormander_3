#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

internal static class WorkshopArtSource
{
    [Serializable] internal sealed class Entry { public string source,asset; public int frames,width,height; }
    [Serializable] private sealed class Manifest { public Entry[] entries; }
    internal static string Root=>Path.Combine(ProjectDataConnection.Checkout,"ARTIST");
    internal static Entry[] Entries()=>JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Root,"manifest.json"))).entries;
    internal static string Source(Entry e)
    {
        string root=Path.GetFullPath(Root)+Path.DirectorySeparatorChar;
        string path=Path.GetFullPath(Path.Combine(root,e.source));
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!path.EndsWith(".aseprite",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ARTIST 원본 경로를 확인하세요.");
        return path;
    }
    internal static string Digest(byte[] bytes)
    {
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
    }
    internal static byte[] Read(string path,Entry e)
    {
        var file=new FileInfo(path);
        if(!file.Extension.Equals(".aseprite",StringComparison.OrdinalIgnoreCase)||!file.Exists||file.Length<128||file.Length>134217728)
            throw new IOException("128 MB 이내의 Aseprite 원본을 선택하세요.");
        var data=File.ReadAllBytes(file.FullName);
        if(BitConverter.ToUInt32(data,0)!=data.Length || BitConverter.ToUInt16(data,4)!=0xa5e0 ||
            BitConverter.ToUInt16(data,6)!=e.frames || BitConverter.ToUInt16(data,8)!=e.width || BitConverter.ToUInt16(data,10)!=e.height)
            throw new InvalidDataException("캔버스 크기와 프레임 수가 원본과 다릅니다.");
        return data;
    }

    internal static string Executable()
    {
        string preferred=Environment.GetEnvironmentVariable("ASEPRITE_PATH");
#if UNITY_EDITOR
        preferred=UnityEditor.EditorPrefs.GetString("Necromander.Artist.Aseprite",preferred ?? "");
#endif
        foreach(var path in new[]{preferred,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam","steamapps","common","Aseprite","Aseprite.exe")})
            if(!string.IsNullOrEmpty(path)&&File.Exists(path))return path;
        throw new FileNotFoundException("Aseprite 설치 경로나 ASEPRITE_PATH를 확인하세요.");
    }
    internal static Task<byte[]> Preview(string executable,byte[] source)
    {
        return Task.Run(()=>{
            string prefix=Path.Combine(Path.GetTempPath(),"WorkshopArt-"+Guid.NewGuid().ToString("N"));
            string input=prefix+".aseprite",output=prefix+".png";
            try
            {
                File.WriteAllBytes(input,source);
                using var process=Process.Start(new ProcessStartInfo(executable){
                    Arguments="-b \""+input+"\" --frame-range 0,0 --save-as \""+output+"\"",
                    UseShellExecute=false,CreateNoWindow=true});
                if(process==null)throw new IOException("Aseprite를 실행하지 못했습니다.");
                if(!process.WaitForExit(30000)){process.Kill();throw new TimeoutException("아트 미리보기 시간 초과");}
                if(process.ExitCode!=0 || !File.Exists(output))throw new InvalidDataException("첫 프레임을 읽을 수 없습니다.");
                return File.ReadAllBytes(output);
            }
            finally {File.Delete(input);File.Delete(output);}
        });
    }
    internal static void Save(Entry entry,string baseline,byte[] proposed)
    {
        string path=Source(entry);
        string state=Path.Combine(ProjectDataConnection.Checkout,"Library","WorkshopArt",entry.source);
        Directory.CreateDirectory(Path.GetDirectoryName(state));
        using var guard=new FileStream(state+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string actual=Digest(Read(path,entry));
        if(Digest(proposed)==actual)return;
        if(actual!=baseline)throw new IOException("다른 사람이 이 그림을 변경했습니다. 목록에서 다시 열어 두 원본을 비교하세요.");
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {output.Write(proposed,0,proposed.Length);output.Flush(true);}
            File.Replace(temp,path,state+".bak");
        }
        finally{File.Delete(temp);}
    }

    // The project palette stores a single 8-bit RGB packet. Preserve the cels, layer and frame bytes.
    internal static int PaletteOffset(byte[] data,out int count)
    {
        count=0;
        if(BitConverter.ToUInt16(data,12)!=8 || BitConverter.ToUInt16(data,6)!=1)return -1;
        int offset=-1;
        for(int pos=144;pos<=data.Length-6;)
        {
            int length=BitConverter.ToInt32(data,pos);
            if(length<6||length>data.Length-pos)return -1;
            int type=BitConverter.ToUInt16(data,pos+4);
            if(type==0x2019)return -1;
            if(type==4)
            {
                if(offset>=0||length<10||BitConverter.ToUInt16(data,pos+6)!=1||data[pos+8]!=0)return -1;
                count=data[pos+9]==0?256:data[pos+9];
                if(length!=10+count*3)return -1;
                offset=pos+10;
            }
            pos+=length;
        }
        return offset;
    }
}
#endif
