using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

public static class ClaudeGitStatus
{
    public static string Git(string args)
    {
        var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        using var p = Process.Start(psi);
        string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        return $"$ git {args}  (exit {p.ExitCode})\n{o}{e}";
    }

    static void Out(string s) { foreach (var l in s.Split('\n')) if (l.Length > 0) Debug.Log("[ClaudeGit] " + l); }

    [MenuItem("Tools/ClaudeTemp/Git Status")]
    public static void Run()
    {
        Out("root: " + Directory.GetParent(UnityEngine.Application.dataPath).FullName);
        Out(Git("-c core.quotepath=false status -sb"));
        Out(Git("remote -v"));
        Out(Git("log --oneline -5"));
        Out(Git("-c core.quotepath=false diff --stat HEAD"));
    }
}
