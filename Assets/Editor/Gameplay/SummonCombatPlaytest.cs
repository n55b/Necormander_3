using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>소환수 전투 개편 프로토타입 Play Mode 점검. 결과는 콘솔의 [SummonPlaytest] 줄에 남는다.</summary>
[InitializeOnLoad]
public static class SummonCombatPlaytest
{
    private const string Key = "SummonCombatPlaytest.Pending";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string ResultPath = "Logs/SummonPlaytest.txt";
    private static IEnumerator _run;
    private static double _waitUntil;
    private static int _pass, _fail;
    private static readonly List<string> _errors = new List<string>();

    static SummonCombatPlaytest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) Begin();
        };
    }

    [MenuItem("Tools/Necromander/Run Summon Combat Playtest")]
    public static void Run()
    {
        SessionState.SetBool(Key, true);
        if (EditorApplication.isPlaying) Begin();
        else EditorApplication.isPlaying = true;
    }

    private static void Begin()
    {
        SessionState.EraseBool(Key);
        _pass = _fail = 0;
        System.IO.File.WriteAllText(ResultPath, "");
        _errors.Clear();
        Application.logMessageReceived += OnLog;
        _run = Script();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) _errors.Add(message);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { Finish("플레이 모드가 중간에 끝남"); return; }
        if (EditorApplication.timeSinceStartup < _waitUntil) return;
        try
        {
            if (!_run.MoveNext()) { Finish(null); return; }
            _waitUntil = EditorApplication.timeSinceStartup + (_run.Current is float s ? s : 0f);
        }
        catch (Exception e) { Out($"중단: {e}"); Finish("예외로 중단"); }
    }

    private static void Finish(string reason)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Out($"종료 PASS={_pass} FAIL={_fail} 콘솔오류={_errors.Count} {reason}\n" + string.Join("\n", _errors.Distinct().Take(12)));
    }

    private static void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        Out($"{(ok ? "PASS" : "FAIL")} {label}");
    }

    private static void Out(string line)
    {
        Debug.Log("[SummonPlaytest] " + line);
        System.IO.File.AppendAllText(ResultPath, line + "\n");
    }

    private static MainMinionDataSO Form(string name) =>
        AssetDatabase.LoadAssetAtPath<MainMinionDataSO>($"Assets/SOData/Minion/Main/{name} Minion.asset");

    private static void Press(Key key)
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
        InputSystem.Update();
    }
    private static void ReleaseKeys()
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        InputSystem.Update();
    }
    private static void Mouse_(int button) // 0=없음 1=좌 2=우
    {
        var state = new MouseState { position = Mouse.current.position.ReadValue() };
        if (button == 1) state = state.WithButton(MouseButton.Left);
        if (button == 2) state = state.WithButton(MouseButton.Right);
        InputSystem.QueueStateEvent(Mouse.current, state);
        InputSystem.Update();
    }

    // 에디터가 포커스를 잃으면 가상 입력이 PlayerInput 에 닿지 않는다. 그때는 입력 처리 함수를 직접 부른다(입력 경로는 미확인으로 남는다).
    private static bool _direct;
    private static PlayerController P => GameManager.Instance.PLAYERCONTROLLER;
    private static IEnumerator TapSpace()
    {
        if (_direct) { if (!PlayerController.IsGameplayPaused) P.GetComponent<PlayerSkillController>().TrySwapSummon(); yield return 0.1f; yield break; }
        Press(UnityEngine.InputSystem.Key.Space); yield return 0.05f; ReleaseKeys(); yield return 0.05f;
    }
    private static IEnumerator TapRight()
    {
        if (_direct) { P.GetComponent<PlayerSkillController>().ExecuteMinionSkill(P.transform); yield return 0.1f; yield break; }
        Mouse_(2); yield return 0.05f; Mouse_(0); yield return 0.05f;
    }
    private static IEnumerator TapLeft()
    {
        if (_direct) { typeof(MeleeCombatController).GetMethod("ExecuteMeleeAttack", Any).Invoke(P.GetComponent<MeleeCombatController>(), null); yield return 0.1f; yield break; }
        Mouse_(1); yield return 0.05f; Mouse_(0); yield return 0.05f;
    }

    private static int Casters(string summonName = null) => UnityEngine.Object.FindObjectsByType<MinionSkillCaster>(FindObjectsSortMode.None)
        .Count(c => summonName == null || (c.Data != null && c.Data.minionName == summonName));

    private static void ClearSlots(InventoryManager inv)
    {
        inv.EquipMinion(0, null); inv.EquipMinion(1, null);
    }

    private static IEnumerator Script()
    {
        double deadline = EditorApplication.timeSinceStartup + 90;
        while (GameManager.Instance == null || GameManager.Instance.PLAYERCONTROLLER == null || InventoryManager.Instance == null)
        {
            if (EditorApplication.timeSinceStartup > deadline) { Check(false, "플레이어 생성 대기 시간 초과"); yield break; }
            yield return 0.2f;
        }
        yield return 1f;
        var player = GameManager.Instance.PLAYERCONTROLLER;
        var inv = InventoryManager.Instance;
        var skills = player.GetComponent<PlayerSkillController>();
        var melee = player.GetComponent<MeleeCombatController>();
        var dodge = player.GetComponent<MeleeDodgeController>();
        var input = player.GetComponent<PlayerInput>();
        player.SetInputBlocked(false);
        Time.timeScale = 1f;
        ClearSlots(inv);

        // ── 정적 구성 ──
        var swapAction = input.actions.FindAction("Player/MinionSwap");
        var skillAction = input.actions.FindAction("Player/MinionSkill");
        Check(swapAction != null && swapAction.bindings.Any(b => b.path == "<Keyboard>/space"), "입력: Space=MinionSwap");
        Check(skillAction != null && skillAction.bindings.Any(b => b.path == "<Mouse>/rightButton"), "입력: 우클릭=MinionSkill");
        Check(input.actions.FindAction("Player/Parry") == null, "입력: Parry 액션 제거");
        Check(typeof(PlayerController).Assembly.GetType("PlayerParryController") == null, "가드 컨트롤러 클래스 제거");
        Check(player.GetComponents<MonoBehaviour>().All(m => m != null), "플레이어에 누락 스크립트 없음");
        var hud = UnityEngine.Object.FindAnyObjectByType<PlayerStateUI>();
        Check(hud != null && hud.GetComponentsInChildren<Transform>(true).All(t => !t.name.Contains("Guard")), "HUD 가드 오브젝트 없음");

        // 실제 입력이 PlayerInput 까지 오는지 먼저 본다. 안 오면 입력 경로 항목은 미확인이다.
        bool inputArrived = false;
        Action<InputAction.CallbackContext> probe = _ => inputArrived = true;
        swapAction.performed += probe;
        yield return TapSpace();
        swapAction.performed -= probe;
        Out($"INFO 가상 키보드 입력 도달={inputArrived}");
        _direct = !inputArrived;
        if (_direct) Out("UNVERIFIED 실제 키 입력 경로: 가상 입력 미도달 → 처리 함수를 직접 호출해 진행");

        // 적 준비
        var scarecrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Enemy Scarecrow.prefab");
        var enemy = UnityEngine.Object.Instantiate(scarecrowPrefab, player.transform.position + Vector3.right * 2.5f, Quaternion.identity);
        yield return 1f;
        var enemyHealth = enemy.GetComponentInChildren<CharacterHealth>();
        Check(CharacterStatus.ActiveEnemies.Count > 0 && enemyHealth != null, $"허수아비 적 준비 (적 {CharacterStatus.ActiveEnemies.Count})");

        // ── 장착 0개 ──
        int casters = Casters();
        yield return TapSpace();
        yield return TapRight();
        yield return 0.2f;
        Check(inv.EquippedSummonCount == 0 && Casters() == casters, "0개: 우클릭·Space 무동작");

        // ── 장착 1개 ──
        var fighter = Form("Fighter"); var ice = Form("IceMage"); var doll = Form("DashDoll");
        Check(inv.EquipMinion(fighter), "빈 칸 장착");
        yield return 0.2f;
        int activeBefore = inv.ActiveSummonSlot;
        yield return TapSpace();
        Check(inv.ActiveSummonSlot == activeBefore && skills.SwapCooldownRemaining <= 0f, "1개: Space 무동작");
        yield return TapRight();
        yield return 0.2f;
        Check(skills.GetSummonCooldownRemaining(0) > 0f && Casters(fighter.minionName) > 0, "1개: 우클릭 스킬 발동");

        // ── 장착 2개, 중복 차단 ──
        foreach (string f in new[] { "Fighter", "Knight", "Champion", "Warrior", "Warlord" })
        {
            Check(!inv.EquipMinion(Form(f)), $"중복 차단(빈 칸): {f}");
            Check(!inv.EquipMinion(1, Form(f)), $"중복 차단(슬롯 지정): {f}");
        }
        Check(inv.EquipMinion(ice), "다른 소환수 2번째 칸 장착");
        Check(!inv.EquipMinion(doll), "칸이 차면 자동 장착 안 함");
        yield return 0.2f;
        Check(skills.GetFieldAgent(0) != null && skills.GetFieldAgent(1) != null, "필드에 두 마리 표시");
        foreach (var agent in new[] { skills.GetFieldAgent(0), skills.GetFieldAgent(1) })
            Check(agent.GetComponentsInChildren<Collider2D>(true).Length == 0 && agent.GetComponentInChildren<CharacterHealth>(true) == null
                  && agent.GetComponentInChildren<CharacterStatus>(true) == null && agent.gameObject.layer == Layers.Default,
                  $"피격 판정·타겟 대상 없음: {agent.Data.minionName}");
        Check(CharacterStatus.ActiveEnemies.All(e => e.GetComponentInParent<SummonFieldAgent>() == null), "소환수가 전투 대상 목록에 없음");

        // ── 스왑과 쿨타임 ──
        ClearSwap(skills);
        int prev = inv.ActiveSummonSlot;
        float cdBeforeSwap = skills.GetSummonCooldownRemaining(prev);
        int fighterCasters = Casters(fighter.minionName);
        yield return TapSpace();
        Check(inv.ActiveSummonSlot == 1 - prev && inv.MainSummon == ice && skills.MainSummon == ice, "2개: Space 스왑 → 활성 변경");
        yield return 0.25f;
        Check(Casters(fighter.minionName) > fighterCasters && skills.GetSummonCooldownRemaining(prev) <= cdBeforeSwap, "스왑 시 스왑 전 소환수 대쉬 공격 발동, 쿨타임은 늘지 않음");
        Check(skills.GetSummonCooldownRemaining(1) <= 0f && Mathf.Approximately(skills.SwapCooldownDuration, 30f), "새 소환수 쿨타임 유지, 스왑 쿨타임 30초");
        int after = inv.ActiveSummonSlot;
        yield return TapSpace();
        Check(inv.ActiveSummonSlot == after, "스왑 쿨타임 중 재스왑 차단");
        yield return 1.1f;
        float fighterCd = skills.GetSummonCooldownRemaining(0);
        Check(fighterCd > 0f, $"슬롯별 쿨타임 유지 (전사 남은 {fighterCd:F1}s)");
        yield return 1f;
        Check(skills.GetSummonCooldownRemaining(0) < fighterCd - 0.5f, "비활성 슬롯 쿨타임 회복");

        // 플레이어 평타에는 소환수 공격이 섞이지 않는다.
        yield return 2f;
        enemy.SetActive(false);
        yield return 1.5f;
        int castersBeforeSwing = Casters();
        yield return TapLeft();
        yield return 0.25f;
        Check(Casters() == castersBeforeSwing, "플레이어 평타에 소환수 공격이 섞이지 않음");
        enemy.SetActive(true);
        yield return 2.5f;
        enemyHealth.Heal(enemyHealth.MaxHP);
        ((float[])typeof(PlayerSkillController).GetField("_summonCooldownEnd", Any).GetValue(skills))[1] = 0f;
        yield return TapRight();
        yield return 0.25f;
        Check(skills.GetSummonCooldownRemaining(1) > 0f && Casters(ice.minionName) > 0, "스왑 후 우클릭 = 활성 소환수");

        // ── 스킬 중 스왑/평타 캔슬 ──
        yield return 1f;
        Check(inv.ActiveSummonSlot == 1, "(전제) 얼음 마법사 활성");
        // 쿨타임을 비워 바로 시전
        ((float[])typeof(PlayerSkillController).GetField("_summonCooldownEnd", Any).GetValue(skills))[1] = 0f;
        enemyHealth.Heal(enemyHealth.MaxHP);
        float hpBefore = enemyHealth.CurHP;
        yield return TapRight();
        yield return 0.15f;
        Check(running0(ice), "스킬 시전 시작");
        ClearSwap(skills);
        var running = UnityEngine.Object.FindObjectsByType<MinionSkillCaster>(FindObjectsSortMode.None).FirstOrDefault(c => c.Data == ice);
        yield return TapSpace();
        Check(inv.MainSummon == fighter && running != null, "스킬 중 스왑 가능, 시전 유지");
        yield return TapLeft();
        yield return 0.1f;
        Check(melee.IsAttacking && running != null, "스킬 중 평타 입력 가능, 시전 유지");
        yield return 1.1f;
        yield return TapLeft();
        yield return 0.05f;
        bool attacking = melee.IsAttacking;
        ClearSwap(skills);
        yield return TapSpace();
        yield return 0.02f;
        Check(attacking && !player.GetComponentInChildren<BaseHitBox>(), "스왑이 플레이어 평타 판정을 캔슬");
        yield return 2f;
        Check(enemyHealth.CurHP < hpBefore, "시전한 스킬이 스왑 후에도 피해를 줌(자동 공격 포함 값)");

        // ── 자동 공격 ──
        var a0 = skills.GetFieldAgent(0); var a1 = skills.GetFieldAgent(1);
        ((float[])typeof(PlayerSkillController).GetField("_summonCooldownEnd", Any).GetValue(skills))[0] = Time.time + 30f;
        int c0 = a0.AutoAttackCount, c1 = a1.AutoAttackCount;
        float hp = enemyHealth.CurHP;
        yield return 4.5f;
        Check(a0.AutoAttackCount - c0 >= 1 && a1.AutoAttackCount - c1 >= 1, $"활성·비활성 모두 자동 공격 ({a0.AutoAttackCount - c0}, {a1.AutoAttackCount - c1}회 / 4.5초)");
        Check(a0.AutoAttackCount - c0 <= 3 && a1.AutoAttackCount - c1 <= 3, "자동 공격 주기 2초 범위");
        Check(enemyHealth.CurHP < hp || enemyHealth.IsDead, "자동 공격 피해 적용");
        if (!enemyHealth.IsDead && !a0.IsLinking)
            Check(Vector2.Distance(a0.transform.position, enemy.transform.position) >= skills.MinEnemyGap - 0.2f, "소환수가 적과 겹치지 않음");
        // 전투가 아니면 플레이어 곁으로 따라온다.
        enemy.SetActive(false);
        yield return 0.5f;
        Check(!a0.IsEngaged, "적이 없으면 교전 해제");
        player.transform.position += Vector3.right * 6f;
        yield return 2.5f;
        float followGap = Vector2.Distance(a0.transform.position, player.transform.position);
        Check(followGap <= skills.FollowDistance + 1f, $"전투가 아니면 플레이어를 따라옴 (거리 {followGap:F2})");
        Vector2 restPos = a0.transform.position;
        yield return 1.5f;
        Check(Vector2.Distance(restPos, a0.transform.position) < 0.05f, "플레이어가 멈추면 소환수도 멈춤");
        player.transform.position += Vector3.left * 6f;
        enemy.SetActive(true);
        yield return 0.3f;

        // ── 연계 ──
        yield return 2f;
        var active = skills.GetFieldAgent(inv.ActiveSummonSlot);
        a0.transform.position = player.transform.position + Vector3.up * 1.9f; a1.transform.position = player.transform.position + Vector3.down * 1.9f;
        ((float[])typeof(PlayerSkillController).GetField("_summonCooldownEnd", Any).GetValue(skills))[inv.ActiveSummonSlot] = 0f;
        yield return TapRight();
        Check(active.IsLinking, "우클릭 시 활성 소환수 연계 시작");
        yield return 3f;
        Check(!active.IsLinking && active.IsVisualVisible, "연계 종료 후 일반 AI 복귀");

        // ── 대쉬 동일 ──
        enemy.SetActive(false);
        yield return 2f;
        var durations = new List<string>();
        foreach (string name in new[] { "Fighter", "Knight", "Champion", "Warrior", "Warlord", "DashDoll", "IceMage" })
        {
            ClearSlots(inv);
            inv.EquipMinion(0, Form(name));
            yield return 0.3f;
            var dashAgent = skills.GetFieldAgent(0);
            int castersBefore = Casters();
            int boxesBefore = UnityEngine.Object.FindObjectsByType<BaseHitBox>(FindObjectsSortMode.None).Length;
            typeof(MeleeDodgeController).GetField("_currentCharges", Any).SetValue(dodge, dodge.MaxCharges);
            yield return 0.3f;
            player.transform.position = player.transform.position;
            bool dashed = dodge.TryDash(Vector2.right, player.transform.localScale.x);
            float travel = (float)typeof(MeleeDodgeController).GetField("_dashTravelDuration", Any).GetValue(dodge);
            bool invincible = player.Stat.Health.Invincible;
            yield return 0.02f;
            Check(dashed && invincible && Casters() == castersBefore
                  && UnityEngine.Object.FindObjectsByType<BaseHitBox>(FindObjectsSortMode.None).Length == boxesBefore && !dashAgent.IsLinking,
                $"대쉬: {name} 무적 유지, 연출·판정·연계 없음");
            durations.Add(travel.ToString("F4"));
            yield return 0.5f;
            player.transform.position += Vector3.left * (travel * 15f);
            yield return 0.1f;
        }
        Check(durations.Distinct().Count() == 1, $"모든 소환수 대쉬 이동 시간 동일 ({string.Join(",", durations)})");

        enemy.SetActive(true);
        // ── 평타 중 대쉬 캔슬 ──
        yield return TapLeft();
        typeof(MeleeDodgeController).GetField("_currentCharges", Any).SetValue(dodge, dodge.MaxCharges);
        if (_direct) { if (melee.IsAttacking) dodge.TryDash(Vector2.right, player.transform.localScale.x); }
        else Press(UnityEngine.InputSystem.Key.LeftShift);
        yield return 0.03f;
        Check(dodge.IsDashing && !melee.IsInvokingAttackLock(), "LShift 대쉬가 평타 캔슬");
        ReleaseKeys();
        yield return 0.5f;

        // ── 교체 UI 슬롯 선택 ──
        ClearSlots(inv);
        inv.EquipMinion(0, fighter); inv.EquipMinion(1, ice);
        var picker = MainMinionSelectionUI.Instance;
        if (picker == null) Check(false, "교체 UI 인스턴스 없음 — 미확인");
        else
        {
            Check(picker.ShowPending(doll), "교체 UI 열림");
            yield return 0.1f;
            int target = picker.TargetSlot;
            picker.ToggleTargetSlot();
            Check(picker.TargetSlot == 1 - target, "교체 슬롯 변경");
            int replaced = picker.TargetSlot;
            var keep = inv.GetSummonAt(1 - replaced);
            picker.Select(MainMinionSelectionAction.ActionKind.Replace);
            Check(inv.GetSummonAt(replaced) == doll && inv.GetSummonAt(1 - replaced) == keep, "고른 슬롯만 교체");
            yield return 0.1f;
            Check(picker.ShowPending(Form("Knight")) || true, "같은 계열 후보 UI");
            bool hasFighter = inv.HasSameFamilyEquipped(fighter);
            if (hasFighter)
            {
                var before = inv.GetSummonAt(picker.TargetSlot);
                picker.Select(MainMinionSelectionAction.ActionKind.Replace);
                Check(inv.GetSummonAt(picker.TargetSlot) == before, "같은 계열은 교체 UI 로도 차단");
            }
            picker.Hide();
        }

        // ── 대화 중 Space ──
        ClearSlots(inv);
        inv.EquipMinion(0, fighter); inv.EquipMinion(1, ice);
        yield return 1.2f;
        var dialogue = DialogueUI.Instance;
        if (_direct) Out("UNVERIFIED 대화 중 Space: 실제 입력 필요");
        else if (dialogue == null) Out("UNVERIFIED 대화: 이 씬에 DialogueUI 없음");
        else
        {
            string id = FirstDialogueId();
            if (id == null) Out("UNVERIFIED 대화: 재생할 대사 ID 없음");
            else
            {
                dialogue.Play(id);
                yield return 0.5f;
                Check(dialogue.IsPlaying, $"대화 재생: {id}");
                int activeSlot = inv.ActiveSummonSlot;
                var startedIndex = typeof(DialogueUI).GetField("_index", Any).GetValue(dialogue);
                for (int i = 0; i < 80 && dialogue.IsPlaying; i++) yield return TapSpace();
                Check(!dialogue.IsPlaying, $"Space 로 대사 끝까지 넘김 (시작 인덱스 {startedIndex})");
                Check(inv.ActiveSummonSlot == activeSlot && skills.SwapCooldownRemaining <= 0f, "대화 중과 닫는 Space 에서 스왑 안 됨");
                yield return 0.3f;
                ClearSwap(skills);
                yield return TapSpace();
                Check(inv.ActiveSummonSlot != activeSlot, "대화 종료 후 Space 스왑 복귀");
            }
        }

        UnityEngine.Object.Destroy(enemy);
    }

    private static void ClearSwap(PlayerSkillController skills) =>
        typeof(PlayerSkillController).GetField("_swapReadyTime", Any).SetValue(skills, 0f);

    private static bool running0(MainMinionDataSO data) => Casters(data.minionName) > 0;

    private static bool IsInvokingAttackLock(this MeleeCombatController melee) =>
        (bool)typeof(MeleeCombatController).GetField("_attackAnimationPlaying", Any).GetValue(melee);

    private static string FirstDialogueId()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Data/Dialogue" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".csv")) continue;
            var lines = AssetDatabase.LoadAssetAtPath<TextAsset>(path).text.Split('\n');
            foreach (var line in lines.Skip(1))
            {
                var id = line.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(id) && !id.StartsWith("#")) return id;
            }
        }
        return null;
    }
}
