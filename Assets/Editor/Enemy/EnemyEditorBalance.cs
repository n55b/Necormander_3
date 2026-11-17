using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 편집기 '처치 시간' 탭 (읽기 전용). 칸 = 플레이어 + 그 메인 소환수가 이 적 한 마리를 잡는 초,
/// 맨 오른쪽 = 이 적 혼자 플레이어를 잡는 초. 숫자는 전부 실제 데이터와 런타임 식에서 온다:
///  · 플레이어 스탯    — Player Melee 프리팹 CharacterStat 의 게터(ATK/ATKSPD/DEF/MAXHP/치명/증폭/쿨감).
///                       무기 패시브는 EquipmentInventoryManager 착용과 같은 EquipmentPassive.Apply(강화 = EquipmentInstance.Roll 의
///                       단계: upgradeOnly 면 upgradeStage, 아니면 0)로 잠깐 붙였다가 Mods.RemoveSource 로 뗀다(프리팹 에셋의 직렬화되지 않는 Mods 만 건드림).
///  · 플레이어 평타    — MeleeCombatController.ExecuteMeleeAttack: ATK × BASIC_ATK_MULT × 무기 3연타 배율 × basicDamagePerHit,
///                       접촉당 basicHitsPerContact 타. 한 타 길이 = max(복귀 이벤트, 판정 시작 + attackHitActiveDuration, attackCooldown),
///                       애니 속도 = Clamp(ATKSPD, 0.5, 3). 이벤트 시각은 Body Animator 의 Attack_* 클립(무기 handAnimationOverride 가 있으면
///                       그 클립 — PlayerEquipmentVisualController)에서 읽는다. 한 타가 comboResetTime 보다 길면 그 뒤로 콤보가 1타로 돌아간다.
///  · 소환수 자동 공격 — SummonFieldAgent.Engage + MeleeCombatController.CastSummonBasicAttack:
///                       주기 = max(autoAttackInterval, basicAnim 시간 ÷ ATKSPD + 시전자 꼬리), 피해 = ATK × finisher.damageMultiplier
///                       × basicDamagePerHit × autoAttackDamageMultiplier, finisher.hitCount × 접촉당 타수.
///  · 우클릭 스킬      — PlayerSkillController.CastActiveSkill + MinionActionSkillSO.Execute: 쿨타임(플레이어 스탯의 ApplySkillCooldown),
///                       ATK × damageMultiplier, hitCount(구역 충격파면 1). 시전 중(이동+시전+꼬리+복귀 지연)은 자동 공격을 쉰다.
///  · 받는 피해        — CharacterHealth.ApplyDamage: 취약(물리·출혈) × (1+속성 증폭) × 치명 → max(×(1−방어%), 1), 방어 75% 상한.
///                       명중 판정은 공격자(자식 포함)에서 CharacterStat 을 찾으면 돈다(그대로 따라 한다).
///  · 적 → 플레이어    — 맞으면 PlayerController.InvincibilitySequence 가 invincibilityDuration 동안 무적(ApplyDamage 가 막음).
/// 계산은 필터·되돌리기·무기·모드가 바뀔 때만 다시 한다.
/// </summary>
public partial class EnemyEditorPanel
{
    private const string BalancePlayerPrefab = "Assets/Prefabs/Player Melee.prefab";
    private const float BalanceCasterTail = 0.25f;     // MinionSkillCaster.DESPAWN_TAIL(private): 시전이 끝난 뒤 시전자가 남는 시간
    private const float BalanceAnimLockFallback = 3f;  // PlayerController.LockAnimState 기본값: 복귀 이벤트가 없으면 이 시간 뒤 평타가 풀린다
    private static readonly string[] BalanceSwingStates = { "Attack_Light1", "Attack_Light2", "Attack_Medium" }; // MeleeCombatController.PlayAttackAnimation

    private static readonly GUIContent[] BalanceModes =
    {
        new GUIContent("스킬 포함", "플레이어 평타 + 소환수 자동 공격 + 우클릭 스킬(쿨타임마다)."),
        new GUIContent("자동 공격만", "플레이어 평타(꾹 누르기) + 소환수 자동 공격. 우클릭 스킬은 쓰지 않는다."),
    };

    [SerializeField] private EquipmentSO balanceWeapon; // 비우면 시작 무기
    [SerializeField] private bool balanceAutoOnly;
    private Vector2 balanceScroll;
    private string balanceKey;
    private BalanceModel balance;

    private static GUIStyle balanceCenter, balanceHead;
    private static GUIStyle BalanceCenter => balanceCenter ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
    private static GUIStyle BalanceHead => balanceHead ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

    // kind: 0 플레이어 평타 · 1 소환수 자동 공격 · 2 우클릭 스킬. rollsHit = 평타 갈래(명중 판정 대상).
    private sealed class BalanceHit { public int kind; public float amount, perSec; public DamageType type; public bool rollsHit; }

    private sealed class BalanceCol
    {
        public readonly List<BalanceHit> hits = new List<BalanceHit>();
        public float dot, vuln = 1f; // dot = 방어 무시 상태이상 초당 피해, vuln = 취약 가동률을 반영한 물리 배율
        public GUIContent head, dps;
    }

    private sealed class BalanceRow
    {
        public EnemyMinionDataSO e;
        public GUIContent name, kill;
        public GUIContent[] cells;
        public Color[] tints;
        public Color killTint;
    }

    private sealed class BalanceModel
    {
        public string error, note, info;
        public EquipmentSO weapon;
        public List<EquipmentSO> weapons = new List<EquipmentSO>();
        public string[] weaponLabels = new string[0];
        public float hp, def, eva, acc, physAmp, magicAmp, critP, critMul, iframe;
        public bool playerRollsHit;
        public readonly List<BalanceCol> cols = new List<BalanceCol>();
        public readonly List<BalanceRow> rows = new List<BalanceRow>();
    }

    // ── 그리기 ────────────────────────────────────────────────────────
    private void DrawBalance(List<EnemyMinionDataSO> list)
    {
        // 무기·모드는 Defer 로만 바뀌고, 필터·되돌리기·탭 전환은 lastContext/lastFilter 에 이미 들어 있다.
        string key = $"{lastContext}|{lastFilter}|{(balanceWeapon != null ? balanceWeapon.GetInstanceID() : 0)}|{balanceAutoOnly}";
        if (balance == null || key != balanceKey) { balanceKey = key; balance = BuildBalance(list); }
        var m = balance;
        if (m.error != null) { EditorGUILayout.HelpBox(m.error, MessageType.Error); return; }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent("무기", "계산에 쓸 플레이어 무기. 처음엔 새 게임 시작 무기(GrowthRegistry 의 기본 무기)."), GUILayout.Width(30));
            int wi = m.weapons.IndexOf(m.weapon);
            int nwi = EditorGUILayout.Popup(wi, m.weaponLabels, GUILayout.Width(190));
            if (nwi != wi && nwi >= 0) { var w = m.weapons[nwi]; Defer(() => balanceWeapon = w); }
            GUILayout.Space(8);
            int mode = balanceAutoOnly ? 1 : 0;
            int nm = GUILayout.Toolbar(mode, BalanceModes, EditorStyles.miniButton, GUILayout.Width(180));
            if (nm != mode) { bool v = nm == 1; Defer(() => balanceAutoOnly = v); }
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.HelpBox(m.note, MessageType.None);
        EditorGUILayout.LabelField(m.info, EnemyAssets.WrapMini);
        if (m.cols.Count == 0) { EditorGUILayout.HelpBox("GrowthRegistry 에 메인 소환수가 없습니다.", MessageType.Info); return; }

        const float thumbW = 26f, nameW = 150f, hpW = 46f, defW = 44f, cellW = 76f, killW = 92f;
        float width = thumbW + nameW + hpW + defW + cellW * m.cols.Count + killW;
        var line = new Color(0.5f, 0.5f, 0.5f, 0.35f);

        balanceScroll = EditorGUILayout.BeginScrollView(balanceScroll);
        // 머리줄 — 줄마다 같은 x 로 직접 잘라 그려야 칸이 맞는다.
        var r = GUILayoutUtility.GetRect(width, 30, GUILayout.Width(width));
        float x = r.x + thumbW;
        GUI.Label(new Rect(x, r.y, nameW, r.height), new GUIContent("적", "이름을 누르면 '하나씩 편집'으로 연다."), EditorStyles.miniBoldLabel); x += nameW;
        GUI.Label(new Rect(x, r.y, hpW, r.height), new GUIContent("체력", "최대 체력(maxHP)"), BalanceHead); x += hpW;
        GUI.Label(new Rect(x, r.y, defW, r.height), new GUIContent("방어%", "피해 감소율. 75%에서 잘린다."), BalanceHead); x += defW;
        foreach (var c in m.cols) { GUI.Label(new Rect(x, r.y, cellW - 2, r.height), c.head, BalanceHead); x += cellW; }
        GUI.Label(new Rect(x, r.y, killW, r.height), new GUIContent("적 → 플레이어", "이 적 혼자 플레이어(체력 가득)를 잡는 초. 빨간 칸 = 소환수 중앙값보다 먼저 플레이어를 잡는다."), BalanceHead);

        r = GUILayoutUtility.GetRect(width, 18, GUILayout.Width(width));
        GUI.Label(new Rect(r.x + thumbW, r.y, nameW + hpW + defW, r.height),
            new GUIContent("초당 피해 (방어 0% 상대)", "소환수마다 실제로 쓴 초당 피해. 숫자에 마우스를 올리면 평타·자동 공격·스킬·상태이상으로 나눠 보여준다."), EditorStyles.miniLabel);
        x = r.x + thumbW + nameW + hpW + defW;
        foreach (var c in m.cols) { GUI.Label(new Rect(x, r.y, cellW - 2, r.height), c.dps, BalanceCenter); x += cellW; }
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, width, 1), line);

        for (int i = 0; i < m.rows.Count; i++)
        {
            var row = m.rows[i];
            r = GUILayoutUtility.GetRect(width, 24, GUILayout.Width(width));
            if (i % 2 == 1) EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.06f));
            EquipmentEditorPanel.DrawSprite(new Rect(r.x + 2, r.y + 2, 20, 20), EnemyAssets.Preview(row.e));
            x = r.x + thumbW;
            EditorGUI.DrawRect(new Rect(x, r.y + 4, 3, 16), EnemyAssets.TierColor(row.e.tier));
            var nr = new Rect(x + 6, r.y + 3, nameW - 8, 18);
            EditorGUIUtility.AddCursorRect(nr, MouseCursor.Link);
            if (GUI.Button(nr, row.name, EditorStyles.linkLabel)) { var t = row.e; Defer(() => { selected = t; view = View.Detail; }); }
            x += nameW;
            GUI.Label(new Rect(x, r.y, hpW, r.height), row.e.maxHP.ToString("0"), BalanceCenter); x += hpW;
            GUI.Label(new Rect(x, r.y, defW, r.height), Mathf.Clamp(row.e.defense, 0f, CharacterStat.MAX_DEF_PERCENT).ToString("0.#"), BalanceCenter); x += defW;
            for (int j = 0; j < row.cells.Length; j++)
            {
                var cr = new Rect(x, r.y + 2, cellW - 2, r.height - 4);
                EditorGUI.DrawRect(cr, row.tints[j]);
                GUI.Label(cr, row.cells[j], BalanceCenter);
                x += cellW;
            }
            var kr = new Rect(x, r.y + 2, killW - 2, r.height - 4);
            EditorGUI.DrawRect(kr, row.killTint);
            GUI.Label(kr, row.kill, BalanceCenter);
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("색: 초록 = 이 적에게 소환수 중앙값보다 빨리 잡음 · 빨강 = 느림 (중앙값의 ½배·2배에서 가장 진함) · — = 피해 0. " +
                                   "칸에 마우스를 올리면 초당 피해와 계산을 보여준다. 다른 창에서 소환수·무기·프리팹을 고쳤다면 ↻ 로 다시 계산.", EnemyAssets.WrapMini);
        EditorGUILayout.EndScrollView();
    }

    // ── 계산 ──────────────────────────────────────────────────────────
    private BalanceModel BuildBalance(List<EnemyMinionDataSO> list)
    {
        var m = new BalanceModel();
        var growth = EquipmentAssets.Growth;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BalancePlayerPrefab);
        var stat = prefab != null ? prefab.GetComponentInChildren<CharacterStat>(true) : null;
        var pc = prefab != null ? prefab.GetComponent<PlayerController>() : null;
        var melee = prefab != null ? prefab.GetComponent<MeleeCombatController>() : null;
        var psc = prefab != null ? prefab.GetComponent<PlayerSkillController>() : null;
        if (growth == null) { m.error = "GrowthRegistrySO 를 찾지 못해 소환수·무기 목록을 만들 수 없습니다."; return m; }
        if (stat == null || pc == null || melee == null || psc == null)
        {
            m.error = $"{BalancePlayerPrefab} 에서 CharacterStat · PlayerController · MeleeCombatController · PlayerSkillController 를 찾지 못했습니다.";
            return m;
        }

        m.weapons = growth.equipments.Where(w => w != null).Distinct().ToList();
        m.weapon = balanceWeapon != null && m.weapons.Contains(balanceWeapon) ? balanceWeapon
            : growth.startingEquipment != null ? growth.startingEquipment : m.weapons.FirstOrDefault();
        if (m.weapon != null && !m.weapons.Contains(m.weapon)) m.weapons.Insert(0, m.weapon);
        m.weaponLabels = m.weapons.Select(w => EquipmentAssets.DisplayName(w) + (w == growth.startingEquipment ? " (시작 무기)" : "")).ToArray();
        var weapon = m.weapon;

        var missing = new List<string>();
        float activeDur, swingGap, lightTele, thirdTele, comboReset;
        using (var so = new SerializedObject(melee))
        {
            float F(string p, float fallback = 0f) { var sp = so.FindProperty(p); if (sp == null) missing.Add("MeleeCombatController." + p); return sp != null ? sp.floatValue : fallback; }
            activeDur = F("attackHitActiveDuration"); swingGap = F("attackCooldown"); lightTele = F("lightTelegraphDuration"); thirdTele = F("thirdTelegraphDuration");
            comboReset = F("comboResetTime", float.PositiveInfinity);
        }
        UnityEditor.Animations.AnimatorController ctrl;
        using (var so = new SerializedObject(pc))
        {
            var body = so.FindProperty("BodyAnimator")?.objectReferenceValue as Animator; // PlayerController.PlayAllAnim 이 재생하는 애니메이터
            ctrl = body != null ? body.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
            var inv = so.FindProperty("invincibilityDuration"); // 피격 후 무적(InvincibilitySequence)
            if (inv == null) missing.Add("PlayerController.invincibilityDuration");
            m.iframe = inv != null ? Mathf.Max(0f, inv.floatValue) : 0f;
        }
        // PlayerEquipmentVisualController: 기반 컨트롤러가 같은 무기 손 오버라이드만 Body Animator 에 씌운다(다르면 원래 클립).
        var handOv = weapon != null && weapon.handAnimationOverride != null && weapon.handAnimationOverride.runtimeAnimatorController == ctrl ? weapon.handAnimationOverride : null;
        // EquipmentInstance.Roll / UpgradeTo: 단계형(upgradeOnly) 무기는 강화 = upgradeStage 로 착용된다.
        int weaponLv = weapon != null && weapon.upgradeOnly ? weapon.upgradeStage : 0;

        var src = new object();
        try
        {
            if (weapon != null && weapon.passives != null)
                foreach (var p in weapon.passives) p?.Apply(stat, src, weaponLv);

            float atk = stat.ATK, atkSpd = stat.ATKSPD;
            m.hp = stat.MAXHP; m.def = stat.DEF; m.eva = stat.EVASION; m.acc = stat.ACCURACY;
            m.physAmp = stat.PHYS_AMP; m.magicAmp = stat.MAGIC_AMP;
            m.critP = Mathf.Clamp01(stat.CRIT_CHANCE / 100f); m.critMul = stat.CRIT_DAMAGE / 100f;
            // 게임은 명중 판정(CharacterHealth.ResolveStat)·쿨감(PlayerController.Stat)에서 자식 CharacterStatStuff 의 스탯을 찾는다 — 그대로 따른다.
            bool hasStat = prefab.GetComponentInChildren<CharacterStat>(true) != null;
            m.playerRollsHit = hasStat;
            float dmgPerHit = weapon != null ? weapon.basicDamagePerHit : 1f;
            int contact = Mathf.Max(1, weapon != null ? weapon.basicHitsPerContact : 1);

            // 플레이어 3연타 (꾹 누르면 IsAttacking 이 풀리는 즉시 다음 타)
            float spd = Mathf.Clamp(atkSpd, 0.5f, 3f);
            var swing = new float[3];
            var playerHits = new List<BalanceHit>();
            for (int i = 0; i < 3; i++)
            {
                var state = ctrl != null && ctrl.layers.Length > 0
                    ? ctrl.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s != null && s.name == BalanceSwingStates[i]) : null;
                var clip = state != null ? state.motion as AnimationClip : null;
                if (clip != null && handOv != null) { var o = handOv[clip]; if (o != null) clip = o; }
                float animSpd = spd * (state != null && state.speed > 0f ? state.speed : 1f);
                float ret = BalanceEventTime(clip, "CanChangeAnimState"), hit = BalanceEventTime(clip, "OnAttackHitFrame");
                if (ret < 0f) missing.Add(BalanceSwingStates[i] + " 복귀 이벤트(3초 안전장치로 계산)");
                float tele = (i == 2 ? thirdTele : lightTele) / spd;
                float open = hit >= 0f ? Mathf.Min(tele, hit / animSpd) : tele; // 타격 프레임 이벤트가 선딜을 앞당긴다(ForceActivate)
                swing[i] = Mathf.Max(ret >= 0f ? ret / animSpd : BalanceAnimLockFallback, Mathf.Max(open + activeDur, swingGap));
                float mult = stat.BASIC_ATK_MULT * (weapon != null ? weapon.ComboMultiplier(i) : (i == 2 ? 1.5f : 1f)) * dmgPerHit;
                playerHits.Add(new BalanceHit { kind = 0, amount = atk * mult, type = DamageType.Physical, rollsHit = true, perSec = contact });
            }
            // MeleeCombatController.Update: 직전 타 시작부터 comboResetTime 이 지나면 다음 타가 1타로 돌아간다.
            int combo = 3;
            for (int i = 0; i < 2; i++) if (swing[i] > comboReset) { combo = i + 1; break; }
            playerHits.RemoveRange(combo, 3 - combo);
            float cycle = Mathf.Max(0.01f, swing.Take(combo).Sum());
            foreach (var h in playerHits) h.perSec /= cycle;

            foreach (var mm in growth.minionDatas.OfType<MainMinionDataSO>().Where(x => x != null).Distinct())
            {
                var c = new BalanceCol();
                c.hits.AddRange(playerHits);
                var rates = new Dictionary<StatusType, float>(); // 상태이상을 거는 횟수/초
                var tip = new StringBuilder();
                var skill = balanceAutoOnly ? null : mm.minionSkill as MinionActionSkillSO;
                float skillPeriod = 0f, busy = 0f;
                if (skill != null)
                {
                    float baseCd = hasStat ? stat.ApplySkillCooldown(skill.cooldownTime) : skill.cooldownTime; // × ActiveAugment.MinionCooldownMult (증강 없음 = 1)
                    float cd = baseCd;
                    string seren = "";
                    if (weapon != null && weapon.serendipity)
                    {
                        // 평타 한 번마다 ResetChance(행운 포함) 확률로 쿨타임 초기화 — 쿨타임 안에 처음 터지는 시점의 기대값.
                        float p = weapon.ResetChance(stat.LUCK), rate = combo / cycle;
                        if (p > 0f) { cd = Mathf.Min(cd, (1f - Mathf.Pow(1f - p, cd * rate)) / (p * rate)); seren = $" · 세렌디피티 기대 {cd:0.##}초"; }
                    }
                    busy = psc.LinkMoveDuration + (mm.skillAnim != null ? mm.skillAnim.ResolvedDuration : 1f) + BalanceCasterTail + psc.LinkReturnDelay;
                    skillPeriod = Mathf.Max(cd, busy);
                    bool zones = skill.zoneCount > 1 && skill.hitBoxSize.x > 0f && skill.hitBoxSize.y > 0f; // 구역 충격파: 대상은 한 구역에서 1타
                    int n = skill.useHitBox && skill.hitBoxPrefab != null && !zones ? Mathf.Max(1, skill.hitCount) : 1;
                    float amt = atk * skill.damageMultiplier; // × MinionEnhance.SkillDamageMult (강화 0 = 1)
                    c.hits.Add(new BalanceHit { kind = 2, amount = amt, type = skill.element, perSec = n / skillPeriod });
                    BalanceAddRate(rates, skill.onHitStatus, 1f / skillPeriod);
                    tip.AppendLine($"우클릭 '{(string.IsNullOrWhiteSpace(skill.skillName.ko) ? skill.name : skill.skillName.ko)}': {n}타 × {amt:0.#} ({EquipmentAssets.EnumName(skill.element)}), {skillPeriod:0.##}초마다"
                                   + $" (쿨타임 {skill.cooldownTime:0.##}{(hasStat ? $" → {baseCd:0.##}" : "")}초{seren}, 시전 {busy:0.##}초)" + BalanceStatusText(skill.onHitStatus));
                }
                else if (!balanceAutoOnly)
                    tip.AppendLine(mm.minionSkill == null ? "우클릭 스킬 없음" : $"우클릭 스킬 {mm.minionSkill.GetType().Name} 은(는) 계산하지 않음");

                var fin = mm.finisher;
                if (fin != null && fin.IsValid)
                {
                    float cast = (mm.basicAnim != null ? mm.basicAnim.ResolvedDuration : 1f) / Mathf.Max(0.05f, atkSpd);
                    float period = Mathf.Max(psc.AutoAttackInterval, cast + BalanceCasterTail);
                    float share = skill != null ? Mathf.Max(0f, 1f - busy / skillPeriod) : 1f; // 스킬 시전 중엔 자동 공격을 쉰다(근사)
                    float amt = atk * fin.damageMultiplier * dmgPerHit * psc.AutoAttackDamageMultiplier; // × MinionEnhance.BasicAttackMult (강화 0 = 1)
                    c.hits.Add(new BalanceHit { kind = 1, amount = amt, type = fin.element, rollsHit = true, perSec = fin.hitCount * contact * share / period });
                    BalanceAddRate(rates, fin.onHitStatus, share / period);
                    tip.AppendLine($"자동 공격: {fin.hitCount * contact}타 × {amt:0.#} ({EquipmentAssets.EnumName(fin.element)}), {period:0.##}초마다"
                                   + (share < 1f ? $" · 스킬 시전 중 쉼 ×{share:0.##}" : "") + BalanceStatusText(fin.onHitStatus));
                }
                else tip.AppendLine("자동 공격 없음 (finisher.hitCount 0)");

                // 상태이상: 재적중은 지속시간만 갱신하므로 가동률 = min(1, 거는 횟수/초 × 기본 지속시간). 출혈·중독·빙결 해제는 방어 무시.
                float Up(StatusType t) => rates.TryGetValue(t, out var rr) ? Mathf.Min(1f, rr * StatusRules.DefaultDuration(t)) : 0f;
                c.vuln = 1f + StatusRules.VULNERABLE_DAMAGE_BONUS * Up(StatusType.Vulnerable);
                float breaks = Mathf.Min(rates.TryGetValue(StatusType.Freeze, out var fr) ? fr : 0f, 1f / Mathf.Max(0.01f, StatusRules.FREEZE_IMMUNITY));
                c.dot = atk * (1f + m.physAmp) * StatusRules.BLEED_ATK_RATIO / StatusRules.BLEED_TICK_INTERVAL * Up(StatusType.Bleed) * c.vuln
                      + StatusRules.POISON_TICK_DAMAGE / StatusRules.POISON_TICK_INTERVAL * Up(StatusType.Poison)
                      + StatusRules.FREEZE_BREAK_DAMAGE * breaks; // 다음 타격에 깨지며 고정 피해, 깨진 뒤 내성
                if (rates.Count > 0)
                    tip.AppendLine("상태이상: " + string.Join(" · ", rates.Keys.Select(k =>
                        EquipmentAssets.EnumName(k) + (k == StatusType.Freeze ? $" 해제 {breaks:0.##}회/초"
                            : k == StatusType.Bleed || k == StatusType.Poison || k == StatusType.Vulnerable ? $" 가동률 {Up(k) * 100f:0}%" : " (피해 계산 안 함)"))));

                string name = string.IsNullOrWhiteSpace(mm.minionName.ko) ? mm.name : mm.minionName.ko;
                c.head = new GUIContent(name + (mm.isEvolvedForm ? "\n(진화)" : ""), $"{mm.name}{(mm.isEvolvedForm ? " · 진화 형태" : "")}\n{tip}");
                float total = BalanceDps(m, c, 0f, 0f);
                c.dps = new GUIContent(total.ToString("0.#"),
                    $"플레이어 평타 {BalanceDps(m, c, 0f, 0f, 0):0.#} + 자동 공격 {BalanceDps(m, c, 0f, 0f, 1):0.#} + 스킬 {BalanceDps(m, c, 0f, 0f, 2):0.#} + 상태이상 {c.dot:0.#} = {total:0.#}\n" +
                    $"평타: {combo}연타 {cycle:0.00}초에 {contact * combo}타\n{tip}");
                m.cols.Add(c);
            }

            foreach (var e in list) m.rows.Add(BalanceRowFor(m, e));

            var dodge = prefab.GetComponent<MeleeDodgeController>();
            bool standbyDash = dodge == null || dodge.DashAttackSource != MeleeDodgeController.PlayerDashAttackSource.ActiveSummon;
            m.note = "가정: 1:1 · 이동·접근 시간 없음 · 플레이어는 평타를 계속 누르고(콤보 반복), 소환수는 자동 공격 주기마다 친다"
                     + (balanceAutoOnly ? " · 우클릭 스킬은 쓰지 않는다." : " · 우클릭 스킬은 쿨타임마다 쓰고 대상은 늘 범위 안(시전 중엔 자동 공격을 쉬고, 끊기는 평타 1회는 무시).")
                     + $"\n런 시작 기준: 소환수 강화 0 · 아이템·증강·Q/E 스킬 없음 · 무기 패시브는 스탯형만(강화 {weaponLv} — 런에서 이 무기를 얻을 때의 단계) · 치명타는 기대값(지금 {m.critP * 100f:0.#}%).\n"
                     + "대쉬 공격·대기 효과는 " + (standbyDash ? "대기(두 번째) 소환수 몫이라" : "대쉬 빈도를 알 수 없어")
                     + " 뺀다 · 출혈·중독·빙결 해제·취약은 거는 주기와 기본 지속시간으로 가동률을 잡는다(슈퍼아머 무시)."
                     + (hasStat ? "" : "\n플레이어 프리팹에서 CharacterStat 을 못 찾아 명중 판정·우클릭 쿨감을 뺐다.")
                     + $"\n적 → 플레이어: 공격력 × (1 − 플레이어 방어%)를 공격 간격(1/공속)과 피격 후 무적 {m.iframe:0.##}초 중 긴 쪽마다 한 대(무적 동안은 안 맞는다)"
                     + " — AI 패턴의 배율·특수기, 경직·빙결로 끊기는 것은 무시한 최대치.";
            m.info = $"플레이어 ({EquipmentAssets.DisplayName(weapon)}): 체력 {m.hp:0} · 공격력 {atk:0.#} · 공속 {atkSpd:0.##} · 방어 {m.def:0.#}%"
                     + $" · {combo}연타 {string.Join("+", swing.Take(combo).Select(s => s.ToString("0.00")))} = {cycle:0.00}초"
                     + $" · 소환수 자동 공격 {psc.AutoAttackInterval:0.##}초마다 ×{psc.AutoAttackDamageMultiplier:0.##}"
                     + (combo < 3 ? $"\n⚠ {combo}타 길이 {swing[combo - 1]:0.00}초가 콤보 초기화 시간(comboResetTime {comboReset:0.##}초)보다 길어 게임은 1~{combo}타만 반복한다." : "")
                     + (missing.Count > 0 ? $"\n⚠ 못 읽은 값: {string.Join(", ", missing)}" : "");
        }
        finally { stat.Mods.RemoveSource(src); }
        return m;
    }

    private static BalanceRow BalanceRowFor(BalanceModel m, EnemyMinionDataSO e)
    {
        int n = m.cols.Count;
        var row = new BalanceRow { e = e, cells = new GUIContent[n], tints = new Color[n] };
        row.name = new GUIContent(EnemyAssets.DisplayName(e) + (e.isPhaseVariant ? " (페이즈)" : ""), e.name + "\n누르면 '하나씩 편집'으로 연다.");
        float hp = Mathf.Max(0f, e.maxHP);
        var dps = new float[n];
        var ttk = new float[n];
        for (int i = 0; i < n; i++)
        {
            dps[i] = BalanceDps(m, m.cols[i], e.defense, e.baseEvasion);
            ttk[i] = dps[i] > 0f ? hp / dps[i] : float.PositiveInfinity;
        }
        var sorted = ttk.Where(t => !float.IsInfinity(t)).OrderBy(t => t).ToList();
        float median = sorted.Count == 0 ? float.PositiveInfinity
            : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float ratio = median > 0f && !float.IsInfinity(median) ? ttk[i] / median : float.NaN; // 체력 0 이면 중앙값 0 → 비교 없음
            row.cells[i] = new GUIContent(BalanceSec(ttk[i]), dps[i] <= 0f ? "피해 0 — 이 조합으로는 잡지 못한다."
                : $"초당 {dps[i]:0.#} → 체력 {hp:0} ÷ {dps[i]:0.#} = {BalanceSec(ttk[i])}" + (float.IsNaN(ratio) ? "" : $"\n이 적의 소환수 중앙값 {BalanceSec(median)} 대비 ×{ratio:0.##}"));
            row.tints[i] = BalanceTint(ratio);
        }

        // 적 → 플레이어: 기본 공격(BaseEntity/패턴) 피해 = ATK, 간격 = CharacterStat.AttackInterval. 적은 치명·증폭 없음(InitializeStats).
        float interval = 1f / Mathf.Max(0.05f, e.attackSpeed);
        bool rolls = e.minionPrefab != null && e.minionPrefab.GetComponentInChildren<CharacterStat>(true) != null; // 공격자 = 적 루트(스탯은 자식에서 찾는다)
        float chance = rolls ? Mathf.Clamp01(e.baseAccuracy - m.eva) : 1f;
        float perHit = e.attack > 0f ? Mathf.Max(e.attack * (1f - m.def / 100f), 1f) : 0f;
        // 맞은 뒤 무적 동안 공격은 막히고(빗나감은 무적을 안 켬), 무적이 풀린 뒤엔 명중할 때까지 간격마다 다시 굴린다.
        // max(간격, 무적)은 실제(간격의 배수로 올림)보다 짧게 잡은 하한이라 초당 피해는 여전히 최대치다.
        float gap = Mathf.Max(interval, m.iframe) + (chance > 0f ? (1f / chance - 1f) * interval : 0f);
        float edps = chance > 0f ? perHit / gap : 0f;
        float kill = edps > 0f ? m.hp / edps : float.PositiveInfinity;
        bool danger = !float.IsInfinity(kill) && kill <= median;
        row.kill = new GUIContent(BalanceSec(kill),
            $"한 대 {perHit:0.#} (공격력 {e.attack:0.#} × (1 − 방어 {m.def:0.#}%)) · 명중 {chance * 100f:0}%{(rolls ? "" : " (적 프리팹에 CharacterStat 이 없어 명중 판정 안 함)")}\n" +
            $"공격 간격 {interval:0.##}초 · 피격 후 무적 {m.iframe:0.##}초 → 맞는 간격 ≈ {gap:0.##}초 = 초당 {edps:0.#}\n" +
            $"플레이어 체력 {m.hp:0} ÷ {edps:0.#} = {BalanceSec(kill)}" + (danger ? "\n⚠ 소환수 중앙값보다 먼저 플레이어를 잡는다" : ""));
        row.killTint = danger ? new Color(0.95f, 0.35f, 0.3f, 0.4f) : Color.clear;
        return row;
    }

    /// <summary>초당 피해. CharacterHealth.ApplyDamage 와 같은 순서: 취약(물리·출혈) → (1+속성 증폭) → 치명 → max(×(1−방어), 1).
    /// 고정(Fixed)은 방어·치명 없음. kind: -1 전부, 0 평타, 1 자동 공격, 2 스킬 (상태이상 dot 은 -1 일 때만 더한다).</summary>
    private static float BalanceDps(BalanceModel m, BalanceCol c, float defense, float evasion, int kind = -1)
    {
        float d = Mathf.Clamp(defense, 0f, CharacterStat.MAX_DEF_PERCENT) / 100f;
        float sum = kind < 0 ? c.dot : 0f;
        foreach (var h in c.hits)
        {
            if (kind >= 0 && h.kind != kind) continue;
            float a = h.amount * (h.type == DamageType.Physical || h.type == DamageType.Bleed ? c.vuln : 1f)
                      * (1f + (h.type == DamageType.Physical ? m.physAmp : h.type == DamageType.Magic ? m.magicAmp : 0f));
            if (a <= 0f) continue;
            float hit = h.type == DamageType.Fixed ? a
                : (1f - m.critP) * Mathf.Max(a * (1f - d), 1f) + m.critP * Mathf.Max(a * m.critMul * (1f - d), 1f);
            float chance = h.rollsHit && m.playerRollsHit ? Mathf.Clamp01(m.acc - evasion) : 1f;
            sum += hit * h.perSec * chance;
        }
        return sum;
    }

    private static float BalanceEventTime(AnimationClip clip, string function)
    {
        if (clip == null) return -1f;
        foreach (var ev in AnimationUtility.GetAnimationEvents(clip)) if (ev.functionName == function) return ev.time;
        return -1f;
    }

    private static void BalanceAddRate(Dictionary<StatusType, float> rates, StatusType st, float perSec)
    {
        if (st == StatusType.None || perSec <= 0f) return;
        rates.TryGetValue(st, out var r);
        rates[st] = r + perSec;
    }

    private static string BalanceStatusText(StatusType st) => st == StatusType.None ? "" : $" · 적중 시 {EquipmentAssets.EnumName(st)}";

    private static string BalanceSec(float s) => float.IsInfinity(s) || float.IsNaN(s) ? "—" : s >= 100f ? $"{s:0}초" : $"{s:0.0}초";

    private static Color BalanceTint(float ratio)
    {
        if (float.IsNaN(ratio) || ratio <= 0f) return Color.clear;
        float t = float.IsInfinity(ratio) ? 1f : Mathf.Clamp(Mathf.Log(ratio, 2f), -1f, 1f); // 중앙값의 ½배 ~ 2배에서 가장 진하다
        return t < 0f ? new Color(0.3f, 0.85f, 0.4f, -t * 0.45f) : new Color(0.95f, 0.35f, 0.3f, t * 0.45f);
    }
}
