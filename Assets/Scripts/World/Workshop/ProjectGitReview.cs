using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed class ProjectGitReview
{
    internal sealed class FileConflict
    {
        internal string path, working;
        internal readonly string[] blobs=new string[3];
        internal readonly Dictionary<string,ProjectContentStore.Conflict> decisions=new();
    }

    internal static string Git(string arguments,string checkout=null)
    {
        var start=new ProcessStartInfo("git","--literal-pathspecs "+arguments) {WorkingDirectory=checkout ?? ProjectDataConnection.Checkout,
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        using var git=Process.Start(start);
        var errors=git.StandardError.ReadToEndAsync();
        string result=git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        if(git.ExitCode!=0) throw new IOException(errors.GetAwaiter().GetResult());
        return result;
    }
    internal static List<FileConflict> Files()
    {
        var conflicts=new SortedDictionary<string,FileConflict>(StringComparer.Ordinal);
        foreach(string record in Git("ls-files --unmerged -z -- DATA").Split('\0'))
        {
            int tab=record.IndexOf('\t');
            if(tab<0) continue;
            string path=record.Substring(tab+1);
            if(!path.StartsWith("DATA/",StringComparison.Ordinal) || !path.EndsWith(".json",StringComparison.Ordinal)) continue;
            var split=record.Substring(0,tab).Split(' ');
            var absolute=ProjectContentStore.PathFor(ProjectDataConnection.Checkout,path);
            if(!conflicts.TryGetValue(path,out var file))
                conflicts.Add(path,file=new FileConflict{path=path,working=File.Exists(absolute)?File.ReadAllText(absolute):null});
            file.blobs[int.Parse(split[2])-1]=split[1];
        }
        return conflicts.Values.ToList();
    }
    internal static JToken Prepare(FileConflict file)
    {
        JToken Blob(string sha)=>sha==null ? null : JToken.Parse(Git("cat-file blob "+sha));
        return ProjectContentStore.Merge(Blob(file.blobs[0]),Blob(file.blobs[1]),Blob(file.blobs[2]),file.path,file.decisions);
    }
    internal static void Stage(FileConflict file)
    {
        var fresh=Files().Find(f=>f.path==file.path);
        if(fresh==null || fresh.working!=file.working || !fresh.blobs.SequenceEqual(file.blobs))
            throw new IOException("파일이나 Git 병합 기준이 달라졌습니다. 목록을 다시 여세요.");
        var result=Prepare(file);
        string path=ProjectContentStore.PathFor(ProjectDataConnection.Checkout,file.path);
        ProjectContentStore.ReplaceFile(path,result==null ? null : ProjectContentStore.Json(result));
        Git("add -- \""+file.path+"\"");
    }
}
