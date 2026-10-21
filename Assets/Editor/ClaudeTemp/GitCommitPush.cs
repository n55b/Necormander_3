using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

public static class ClaudeGitCommitPush
{
    const string Message =
@"소환수 진화 계열 정리 + 맵 오브젝트 크기/텐트 콜라이더 수정

- 근접 소환수 진화 트리: 전사 → 기사 → 용사 / 전사 → 무사 → 장수
  (기사·용사 = 기존 Breaker 능력, 무사·장수 = 기존 Whirlwind 능력, 장수 신규)
- 보상방에서 강화 없이 바로 진화 (진화 요구 강화 단계 기본값 0)
- 소환수 에셋 영어 이름으로 변경 (Fighter/Knight/Champion/Warrior/Warlord)
  · MainMinionDataSO.formerAssetNames 로 옛 이름 세이브도 그대로 로드
- 신규 소환수 스프라이트 피벗을 캐릭터 발밑으로 수정
  · 소환 위치/좌우 반전 시 외형이 공격 판정과 어긋나던 문제 해결, 아이콘 통일
- KeywordDictionary 에 전사/기사/용사/무사/장수 한·영 항목 추가
- 테스트 SO 는 Deprecated/SpriteTests 로 이동, 검증 메뉴 교체
  (Tools/Village/Verify Minion Evolution Line)
- 맵 오브젝트 크기 조정, 텐트 콜라이더 수정";

    static void Out(string s) { foreach (var l in s.Split('\n')) if (l.Length > 0) Debug.Log("[ClaudeGit] " + l); }

    [MenuItem("Tools/ClaudeTemp/Git Commit Push")]
    public static void Run()
    {
        var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        var msgFile = Path.Combine(root, "Temp", "claude_commit_msg.txt");
        File.WriteAllText(msgFile, Message, new System.Text.UTF8Encoding(false));

        Out(ClaudeGitStatus.Git("add -A -- . " +
            "\":(exclude)Assets/TextMesh Pro/Fonts/Galmuri11 SDF.asset\" " +
            "\":(exclude)Assets/TextMesh Pro/Fonts/Galmuri11-Bold SDF.asset\" " +
            "\":(exclude)Assets/Editor/ClaudeTemp\" \":(exclude)Assets/Editor/ClaudeTemp.meta\""));
        Out(ClaudeGitStatus.Git("-c core.quotepath=false diff --cached --stat -M"));
        Out(ClaudeGitStatus.Git($"commit -F \"{msgFile}\""));
        Out(ClaudeGitStatus.Git("push origin HEAD"));
        Out(ClaudeGitStatus.Git("-c core.quotepath=false status -sb"));
        Out(ClaudeGitStatus.Git("log --oneline -2"));
    }
}
