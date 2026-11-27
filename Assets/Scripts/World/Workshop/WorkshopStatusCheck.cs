#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 제작실 상태이상 화면 검사. WorkshopPlayModeCheck(Editor 어셈블리)가 게임 설정 화면에서 부른다(처음 화면을 거쳐 게임 콘텐츠로 간다).
// DraftStore·WorkshopTrial·ProjectParameters·RoomWorkshopWindow.StatusRows 같은 internal 멤버를 쓰므로 런타임 어셈블리에 둔다.
// 저장한 상태이상 문서와 초안을 지우고 상태이상 화면을 다시 연 채로 끝난다(이어지는 '메뉴'에서 미저장 확인이 뜨지 않는다).
public static class WorkshopStatusCheck
{
    private static readonly string[] Tiles={"플레이어 수치","소환수","몬스터","아이템","장비","상태이상","화면·연출","사운드","대사 편집","UI·보상 번역","아트 원본","플레이테스트","콘텐츠 수치"};

    // capture: 행마다 화면을 남기는 편집기 쪽 저장 함수(검토용 캡처). 없으면 건너뛴다.
    public static IEnumerator Run(Action<string> capture=null)
    {
        var balance=Resources.Load<StatusBalanceSO>(StatusRules.BalanceResourcePath);
        Require(balance!=null,"status balance asset");
        // 1. 첫 화면 카드(글리프·넘침) → 허브. 최근 항목은 사용자 데이터 이름이라 넘침 검사에서 뺀다.
        Click("처음 화면"); yield return Frames();
        VerifyText("home");
        Click("게임 콘텐츠"); yield return Frames();
        foreach(var tile in Tiles) Require(Find(tile)!=null,"hub tile "+tile);
        Require(Find("상태이상").interactable,"status tile opens in an authoring workspace");
        // 2. 화면이 보이는 필드 = SO 필드(이름이 바뀌거나 빠진 필드를 모두 잡는다)
        var shown=RoomWorkshopWindow.StatusRows.SelectMany(r=>r.fields).Append("defaultDuration").ToHashSet();
        var actual=ProjectParameters.Fields(balance).Select(f=>f.path).ToHashSet();
        Require(shown.SetEquals(actual),"status rows cover every StatusBalanceSO field; hidden: "+string.Join(",",actual.Except(shown))+
            " missing: "+string.Join(",",shown.Except(actual)));
        // 3. 아홉 행: 글리프와 넘침
        Click("상태이상"); yield return Frames();
        foreach(var row in RoomWorkshopWindow.StatusRows)
        {
            Click("상태이상/"+row.key); yield return Frames();
            Require(Find("상태이상/"+row.key)!=null,"status row "+row.key);
            VerifyText(row.key);
            capture?.Invoke(row.key);
        }
        Debug.Log("[Workshop status] PASS hub, field coverage, glyphs and fitting labels on nine rows");
        // 4. 휠 한 칸 = 한 줄(22px), 정수 위치
        Click("상태이상/bloodpop"); yield return Frames();
        var view=Object.FindObjectsByType<ScrollRect>(FindObjectsSortMode.None).Single(s=>s.name=="Status detail");
        float top=view.content.anchoredPosition.y;
        float perTick=EventSystem.current.currentInputModule is InputSystemUIInputModule module && module.scrollDeltaPerTick>0 ? module.scrollDeltaPerTick : 6;
        ExecuteEvents.Execute(view.gameObject,new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-perTick)},ExecuteEvents.scrollHandler);
        yield return Frames();
        float moved=view.content.anchoredPosition.y;
        Require(moved==top+22 && moved==Mathf.Round(moved),"one wheel tick scrolls one 22px row to a whole pixel ("+top+"→"+moved+")");
        // 5. 제자리 편집 · 실행 취소 묶음
        Click("상태이상/poison"); yield return Frames();
        var plus=GameObject.Find("poisonTickDamage/+").GetComponent<Button>();
        float published=StatusRules.POISON_TICK_DAMAGE;
        plus.onClick.Invoke(); plus.onClick.Invoke(); yield return Frames();
        string Value()=>GameObject.Find("poisonTickDamage/값").GetComponentInChildren<TMP_Text>().text;
        string Shown(float v)=>v.ToString("0.##",CultureInfo.InvariantCulture);
        Require(plus!=null && plus.isActiveAndEnabled,"stepper edits in place without redrawing the screen");
        Require(Value()==Shown(published+2),"stepper shows the edited value");
        Require(Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(t=>t.name=="결과/1회 피해")
            .GetComponentsInChildren<TMP_Text>().Any(t=>t.text==Shown(published+2)),"result card follows the draft");
        Require(Find("미저장")?.gameObject.activeSelf==true,"header counts the unsaved status draft");
        Require(StatusRules.POISON_TICK_DAMAGE==published,"draft is not applied before a trial");
        var previousSettings=InputSystem.settings;
        var settings=Object.Instantiate(previousSettings);
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings=settings;
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try
        {
            IEnumerator Press(params Key[] keys)
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); yield return Frames();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return Frames();
            }
            yield return Press(Key.LeftCtrl,Key.Z);
            Require(Value()==Shown(published),"Ctrl+Z undoes consecutive steps on one field at once");
            yield return Press(Key.LeftCtrl,Key.Y);
            Require(Value()==Shown(published+2),"Ctrl+Y redoes them");
        }
        finally { InputSystem.RemoveDevice(keyboard); InputSystem.settings=previousSettings; Object.Destroy(settings); }
        // 6~8. 시험장: 미리 적용, 허수아비, 도구막대 부여, 편집으로 복귀
        Click("시험 F6");
        yield return Battle();
        Require(WorkshopTrial.Active && DraftStore.Previewing && StatusRules.POISON_TICK_DAMAGE==published+2,"trial previews the unsaved draft");
        Require(CharacterStatus.ActiveEnemies.Any(s=>s!=null && s.GetComponentsInParent<Transform>().Any(t=>t.name.StartsWith(WorkshopCatalog.DummyName,StringComparison.Ordinal))),
            "arena dummy is an active enemy");
        Require(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text=="미저장 초안으로 시험 중"),"toolbar says the trial uses the draft");
        Click("상태이상 부여"); Click("대상/가까운 적"); Click("부여/중독"); yield return Frames();
        Require(CharacterStatus.ActiveEnemies.Any(s=>s!=null && s.HasStatus(StatusType.Poison)),"toolbar poisons the dummy");
        Require(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text=="중독 → 1/1 걸림"),"toolbar reports the hit count");
        Click("편집으로  F6");
        yield return Workshop();
        RequireRestored(published);
        // 9. 게임 오버로 돌아와도 같다
        Click("시험 F6");
        yield return Battle();
        GameManager.Instance.Gameover();
        yield return Workshop();
        RequireRestored(published);
        Debug.Log("[Workshop status] PASS in-place edit, grouped undo, draft trial, toolbar status and both return paths");
        // 10. 저장
        var draft=DraftStore.All.First(d=>d.entry.source is StatusBalanceSO);
        string document=draft.entry.Document, file=Path.Combine(ProjectContentStore.Root,document,"parameters.json");
        float saved=published+2;
        Click("프로젝트에 저장"); yield return Frames();
        Require(File.Exists(file) && (string)JObject.Parse(File.ReadAllText(file))["poisonTickDamage"]==saved.ToString(CultureInfo.InvariantCulture),"status value saved to DATA");
        Require(StatusRules.POISON_TICK_DAMAGE==saved,"saved value reaches StatusRules");
        Require(Find("미저장")?.gameObject.activeSelf!=true,"nothing left unsaved");
        Require(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("다음 부여부터")),"status line says when it applies");
        // 11. 다른 판의 키: 실행 중에는 건너뛰고, 화면은 저장을 막고 정리할 수 있다
        var json=JObject.Parse(File.ReadAllText(file)); json["futureField"]="1";
        File.WriteAllText(file,ProjectContentStore.Json(json));
        DraftStore.Clear();
        Click("게임 콘텐츠"); Click("상태이상"); yield return Frames();
        Require(!Find("프로젝트에 저장").interactable,"unknown key blocks saving");
        Require(Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("이 판에 없는 항목")),"screen explains the version difference");
        ProjectParameters.Apply(balance,json);
        Require(StatusRules.POISON_TICK_DAMAGE==saved,"runtime skips the unknown key and applies the rest");
        Click("이 판에 없는 항목 지우기"); yield return Frames();
        Require(Find("프로젝트에 저장").interactable,"dropping unknown keys unblocks saving");
        Click("프로젝트에 저장"); yield return Frames();
        Require(!JObject.Parse(File.ReadAllText(file)).ContainsKey("futureField"),"saving removes the unknown key from DATA");
        Debug.Log("[Workshop status] PASS DATA save, live value and version-difference cleanup");
        // 12. 정리
        ProjectContentStore.Save(document,ProjectParameters.Snapshot(document),new Dictionary<string,string>(),_=>{});
        ProjectParameters.Apply(balance,new JObject());
        DraftStore.Clear();
        Require(!File.Exists(file) && StatusRules.POISON_TICK_DAMAGE==published,"cleanup restores the authored value");
        Click("게임 콘텐츠"); Click("상태이상"); yield return Frames();
    }

    private static void RequireRestored(float published)
    {
        Require(GameObject.Find("poisonTickDamage")!=null,"returning reopens the poison row");
        Require(Find("미저장")?.gameObject.activeSelf==true,"draft survives the trial");
        Require(!WorkshopTrial.Active && !DraftStore.Previewing && StatusRules.POISON_TICK_DAMAGE==published,"returning ends the preview");
    }
    private static IEnumerator Battle()
    {
        yield return Until(()=>SceneManager.GetActiveScene().name=="BattleScene" && !UnityNote.SceneLoader.IsLoading &&
            GameManager.Instance!=null && GameManager.Instance.IsPlayerReady);
        yield return Frames(30);   // 허수아비·도구막대가 켜질 때까지
    }
    private static IEnumerator Workshop()
    {
        yield return Until(()=>SceneManager.GetActiveScene().name==WorkshopTrial.SceneName && !UnityNote.SceneLoader.IsLoading &&
            Object.FindFirstObjectByType<RoomWorkshopWindow>()!=null);
        yield return Frames();
    }
    // 제작실 캔버스의 글자: 고정 아틀라스에 없는 글자와 말줄임으로 잘린 칸을 찾는다.
    internal static void VerifyText(string row)
    {
        var canvas=GameObject.Find("Workshop canvas");
        foreach(var text in canvas.GetComponentsInChildren<TMP_Text>())
        {
            if(text.font!=null && text.font.atlasPopulationMode==AtlasPopulationMode.Static)
            {
                string plain=new string(Regex.Replace(text.text,"<[^>]*>","").Where(c=>c>=' ').ToArray());
                Require(text.font.HasCharacters(plain,out List<char> missing) || missing.Count==0,
                    row+": '"+text.text+"' has glyphs missing from "+text.font.name+": "+new string(missing?.ToArray() ?? Array.Empty<char>()));
            }
            if(text.GetComponentInParent<Button>()?.name.Contains("최근")==true) continue;
            Require(!(text.overflowMode==TextOverflowModes.Ellipsis && text.isTextOverflowing),row+": '"+text.text+"' does not fit its box");
        }
    }
    internal static Button Find(string name)=>Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None)
        .Where(b=>b.name==name).OrderByDescending(b=>b.gameObject.activeInHierarchy).FirstOrDefault();
    // 같은 이름의 활성 버튼이 둘이면 실패한다(WorkshopPlayModeCheck.Button과 같은 규칙).
    internal static void Click(string name)
    {
        var found=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy && b.name==name).ToArray();
        Require(found.Length==1,found.Length+" active buttons named '"+name+"'");
        found[0].onClick.Invoke();
    }
    internal static IEnumerator Frames(int count=2)
    {
        int end=Time.frameCount+count;
        while(Time.frameCount<end) yield return null;
    }
    private static IEnumerator Until(Func<bool> done)
    {
        while(!done()) yield return null;
    }
    private static void Require(bool condition,string message)
    {
        if(!condition) throw new InvalidOperationException("[Workshop status] "+message);
    }
}
#endif
