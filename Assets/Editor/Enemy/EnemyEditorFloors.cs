using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 편집기 '층별 구성' 탭. 게임이 실제로 도는 층마다 방 구성 · 웨이브 · 군집 풀 · 엘리트 · 보스 · 기본 골드를
/// 런타임과 같은 규칙으로 계산해 보여준다:
///  · 방 개수   — MapGenerationDataSO.GetRoomCountsForFloor + MapGenerator.BuildFloorSpecialRoomTypes(보스 방이 엘리트 한 자리를 대신)
///  · 웨이브    — GetWavesForFloor, 군집은 NormalRoomEvent.SpawnWaves 처럼 웨이브마다 목록에서 균등 추첨, 마릿수는 AppendWaveEnemies(EnemyAssets.WaveCount)
///  · 보스/끝   — GetBossForFloor, 보스 처치 = 게임 클리어(BossRoomEvent → GameOverManager.TriggerGameClear)
///  · 골드      — RewardManager.RequestClearReward: 일반 · 증강 방 클리어마다 ShopRegistrySO.normalClearGold
/// 층 조절 행이 있는 층의 웨이브 수 · 마릿수 배율만 여기서 고친다(Undo 됨). 나머지는 '맵 데이터 열기'로 인스펙터에서.
/// </summary>
public partial class EnemyEditorPanel
{
    private class FloorRow
    {
        public int floor, tuningIndex, normal, augment, elite, total, waves, gold;
        public float scale, enemies, hp; // enemies · hp = 일반(증강) 방 하나의 기댓값
        public bool boss, skipElite, poolFromTuning;
        public EnemyMinionDataSO bossData;
        public string bossLinked; // 보스 프리팹·AI 패턴에 걸린 다른 적 데이터(2페이즈 등)
        public MapGenerationDataSO.RoomCounts rc;
        public string overrides;
        public int poolSize;
        public readonly List<FloorPoolLine> pool = new List<FloorPoolLine>();
        public readonly List<string> warnings = new List<string>();
    }

    private class FloorPoolLine
    {
        public EnemyClusterSO cluster;
        public int weight, count, scaled; // weight = 목록에 든 칸 수(두 칸이면 두 배로 뽑힘)
        public float spawned, split;      // 배율 적용 후 실제로 나오는 수(프리팹 없는 적 뺌) · 분열로 더 생기는 수(기댓값)
        public float hp;                  // 배율 적용 후 웨이브 하나의 최대 체력 합(분열 포함, 기댓값)
        public List<EnemyCount> enemies;
    }

    private Vector2 floorsScroll;
    [NonSerialized] private List<FloorRow> floorRows;
    [NonSerialized] private List<EnemyMinionDataSO> floorsElites;
    [NonSerialized] private ShopRegistrySO floorsShop;
    [NonSerialized] private int floorsKey, floorsShopVersion, floorsLast, floorsTotalGold;
    [NonSerialized] private float floorsTotalEnemies;
    [NonSerialized] private string floorsNotes;
    private static GUIStyle floorTitle;

    private const string FloorsBasis =
        "계산 기준 — 웨이브마다 군집을 목록에서 균등하게 하나 뽑는다(같은 군집이 두 칸이면 2배). 마릿수 배율은 웨이브 합계에 곱해 반올림(최소 1)한 뒤 " +
        "무작위로 빼거나 복제하므로 체력 합은 기댓값이다. 적 체력은 데이터의 최대 체력 그대로(층별 보정 없음, 방어력 미반영). " +
        "죽으면 갈라지는 적(분열 패턴 — 사자탈 등)은 갈라져 나온 개체도 방을 깨려면 잡아야 해서 마릿수 · 체력에 더했고(분열 자리를 못 찾아 덜 나오는 경우는 무시), " +
        "프리팹이 없는 적은 뽑혀도 게임이 만들지 못해 뺐다. " +
        "증강 방 페널티(웨이브당 추가 적 · 적 체력 증가)는 빼고 계산했다. 골드는 모든 일반 · 증강 방을 깨고 보상 상자를 열었을 때의 기본 클리어 골드만 — " +
        "증강 골드 카드의 추가분, 보상방 상자의 '최대 강화 대체 골드'는 플레이어 선택 · 상태에 따라 달라 뺐다.";

    private void DrawFloors()
    {
        var map = EnemyAssets.MapData;
        if (map == null) { EditorGUILayout.HelpBox("MapGenerationDataSO 를 찾지 못해 층 구성을 볼 수 없습니다.", MessageType.Warning); return; }

        // 다시 계산은 Layout 에서만 한다(같은 프레임의 Layout/Repaint 가 같은 줄 수를 그리도록).
        // 다른 탭 · 인스펙터에서 군집 · 적 · 맵 데이터를 고쳐도 맞도록 관련 에셋의 수정 횟수를 키에 섞는다.
        if (floorRows == null || Event.current.type == EventType.Layout)
        {
            if (floorRows == null || floorsShopVersion != version)
            {
                floorsShopVersion = version;
                var g = AssetDatabase.FindAssets("t:ShopRegistrySO");
                floorsShop = g.Length > 0 ? AssetDatabase.LoadAssetAtPath<ShopRegistrySO>(AssetDatabase.GUIDToAssetPath(g[0])) : null;
                floorRows = null;
            }
            int key = FloorsKey(map);
            if (floorRows == null || key != floorsKey) { floorsKey = key; BuildFloors(map); }
        }

        int gold = floorsShop != null ? floorsShop.normalClearGold : 0;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(floorsLast > 0 ? $"게임이 도는 층: 1 ~ {floorsLast}층   ({floorsLast}층 보스 처치 = 게임 클리어)" : "보스가 배정된 층이 없어 런이 끝나지 않습니다",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("맵 데이터 열기", "층 조절표(조절 행 추가 · 방 개수 · 군집 목록 · 엘리트 건너뛰기) · 층수별 보스 · 전역 기본값을 인스펙터에서 고친다."), EditorStyles.miniButtonLeft))
            { Selection.activeObject = map; EditorGUIUtility.PingObject(map); }
            using (new EditorGUI.DisabledScope(floorsShop == null))
                if (GUILayout.Button(new GUIContent("상점 데이터 열기", "일반 방 클리어 골드(normalClearGold)는 이 에셋에서 고친다."), EditorStyles.miniButtonRight))
                { Selection.activeObject = floorsShop; EditorGUIUtility.PingObject(floorsShop); }
        }
        EditorGUILayout.LabelField($"전역 기본값 ({map.name}): 방 총합 {map.totalRoomCount} · 일반 방 최소 {map.minNormalRooms} · 웨이브 {map.wavesCount} · 마릿수 배율 1   ·   " +
            $"일반 방 클리어 골드 {gold} ({(floorsShop != null ? floorsShop.name : "상점 데이터 없음 — 게임도 0 을 준다")})", EnemyAssets.WrapMini);
        EditorGUILayout.HelpBox(FloorsBasis, MessageType.None);
        if (!string.IsNullOrEmpty(floorsNotes)) EditorGUILayout.HelpBox(floorsNotes, MessageType.Info);

        var mso = SO(map);
        mso.Update();
        var tuningArr = mso.FindProperty("floorTuning");
        floorsScroll = EditorGUILayout.BeginScrollView(floorsScroll);
        foreach (var r in floorRows) DrawFloorRow(r, tuningArr, map, gold);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"1 ~ {floorRows.Count}층 합계: 일반 · 증강 방 전투 적 ≈ {floorsTotalEnemies:0}마리 · 기본 골드 ≈ {floorsTotalGold} G", EditorStyles.boldLabel);
        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();
        mso.ApplyModifiedProperties();
    }

    private void DrawFloorRow(FloorRow r, SerializedProperty tuningArr, MapGenerationDataSO map, int gold)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var c = r.rc;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{r.floor}층", floorTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 }, GUILayout.Width(44));
                GUILayout.Label(r.tuningIndex >= 0
                        ? new GUIContent("층 조절 행 있음" + (r.overrides.Length > 0 ? $" (방 개수 지정: {r.overrides})" : ""), "MapGenerationData 의 층 조절표에 이 층 행이 있다. 방 개수는 -1 로 둔 항목만 전역값을 쓴다.")
                        : new GUIContent("층 조절 행 없음 → 전역 기본값 그대로", "층 조절표에 이 층 행이 없어 방 개수 · 웨이브 · 군집 목록 모두 전역값, 마릿수 배율 1, 엘리트 전투 있음. 행 추가는 '맵 데이터 열기'."),
                    EditorStyles.miniBoldLabel);
                if (r.boss) GUILayout.Label("보스 층", EditorStyles.miniBoldLabel);
                if (r.floor == floorsLast) GUILayout.Label("마지막 층", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(new GUIContent($"기본 골드 ≈ {r.gold} G", $"(일반 {r.normal} + 증강 {r.augment}방) × 클리어 골드 {gold}. 모든 방을 깼을 때."), EditorStyles.boldLabel);
            }

            string rooms = $"방 {r.total}개: 스폰 1 · 일반 {r.normal} · 증강 {r.augment} · 엘리트 {r.elite}" + (r.boss ? " · 보스 1" : "") +
                           $" · 상점 {Mathf.Max(0, c.shop)} · 강화 상점 {Mathf.Max(0, c.enhanceShop)} · 보상 {Mathf.Max(0, c.reward)}";
            if (r.total > c.total)
            {
                int specials = r.total - 1 - r.normal;
                int over = Mathf.Max(0, specials + 1 - c.total);                     // 특수방 + 스폰이 이미 총합을 넘은 몫
                int fromMin = r.normal - Mathf.Max(0, c.total - specials - 1);        // 일반 방 최소 보장으로 더 생긴 몫
                var why = new List<string>();
                if (over > 0) why.Add($"특수방 + 스폰 1 이 총합보다 {over}개 많음");
                if (fromMin > 0) why.Add($"일반 방 최소 보장 {c.minNormal} 로 {fromMin}개 추가");
                rooms += $"   (설정 총합 {c.total}보다 {r.total - c.total}개 많음: {string.Join(" · ", why)})";
            }
            EditorGUILayout.LabelField(new GUIContent(rooms, "일반 방 = max(일반 방 최소, 방 총합 - 특수방 - 스폰 1). 보스 층은 엘리트 한 자리를 보스 방이 대신하고, 엘리트 수가 0 이어도 보스 방은 하나 생긴다."), EnemyAssets.WrapMini);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool editable = r.tuningIndex >= 0 && tuningArr != null && r.tuningIndex < tuningArr.arraySize;
                if (editable)
                {
                    var el = tuningArr.GetArrayElementAtIndex(r.tuningIndex);
                    GUILayout.Label(new GUIContent("웨이브", $"이 층 일반 · 증강 방의 웨이브 수. 0 이면 전역값 {map.wavesCount}."), EditorStyles.miniLabel, GUILayout.Width(40));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("wavesCount"), GUIContent.none, GUILayout.Width(36));
                    GUILayout.Label($"(실제 {r.waves})", EditorStyles.miniLabel, GUILayout.Width(52));
                    GUILayout.Label(new GUIContent("마릿수 배율", "군집 마릿수에 곱한다. 웨이브 합계에 곱해 반올림, 최소 1마리."), EditorStyles.miniLabel, GUILayout.Width(62));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("enemyCountScale"), GUIContent.none, GUILayout.Width(160));
                }
                else GUILayout.Label($"웨이브 {r.waves} (전역) · 마릿수 배율 1 (조절 행 없음)", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                GUILayout.Space(8);
                GUILayout.Label(new GUIContent($"→ 방 하나 ≈ 적 {r.enemies:0.#}마리 · 최대 체력 합 {r.hp:0}   ·   층 전체 {r.normal + r.augment}방 ≈ {r.enemies * (r.normal + r.augment):0}마리 · {r.hp * (r.normal + r.augment):0}",
                    "일반 방과 증강 방은 같은 전투(웨이브 · 군집 · 배율)를 한다. 위 '계산 기준' 참고."), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.LabelField(r.poolFromTuning ? $"군집 풀: 층 지정 목록 ({r.poolSize}칸)"
                : $"군집 풀: 전체 군집 목록 {(EnemyAssets.ClusterRegistry != null ? EnemyAssets.ClusterRegistry.name : "(없음)")} ({r.poolSize}칸)", EditorStyles.miniBoldLabel);
            foreach (var p in r.pool)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8);
                    if (GUILayout.Button(new GUIContent(EnemyAssets.ClusterName(p.cluster), "누르면 '군집 편집'에서 연다"), EditorStyles.miniButton, GUILayout.Width(150)))
                    { var t = p.cluster; Defer(() => { selectedCluster = t; view = View.Cluster; }); }
                    GUILayout.Label(new GUIContent($"{100f * p.weight / r.poolSize:0.#}% · 웨이브당 {(p.scaled != p.count ? $"{p.count}→{p.scaled}" : p.count.ToString())}마리"
                            + (p.spawned < p.scaled - 0.01f ? $"(나옴 {p.spawned:0.#})" : "") + (p.split > 0f ? $" + 분열 {p.split:0.#}" : "") + $" · 체력 {p.hp:0}",
                            "뽑히는 수(배율 적용) · 프리팹이 없어 안 나오는 적을 뺀 수 · 죽으면 갈라져 더 생기는 수. 체력은 갈라진 개체까지 더한 합(기댓값)."),
                        EditorStyles.miniLabel, GUILayout.Width(250));
                    foreach (var e in p.enemies) FloorEnemyChip(e.enemyData, $" ×{e.count}");
                    if (p.enemies.Count == 0) GUILayout.Label("(비어 있음 — 뽑히면 그 웨이브는 0마리)", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("엘리트", EditorStyles.miniBoldLabel, GUILayout.Width(44));
                if (r.elite == 0) GUILayout.Label(r.boss && c.elite > 0 ? "엘리트 방 없음 (보스 방이 엘리트 한 자리를 대신함)" : "엘리트 방 없음", EditorStyles.miniLabel);
                else if (r.skipElite) GUILayout.Label($"엘리트 방 {r.elite}개 · 전투 건너뜀 (입장 즉시 클리어 — 포탈 · 보상은 그대로)", EditorStyles.miniLabel);
                else
                {
                    GUILayout.Label(new GUIContent($"엘리트 방 {r.elite}개 · 후보 {floorsElites.Count}종 중 층마다 1종 무작위:",
                        "MapGenerator.PickFloorElite 가 층마다 하나를 균등 추첨해 그 층 엘리트 방 모두에 쓴다. 방 프리팹에 고정 엘리트가 꽂혀 있으면 그 엘리트, 마릿수는 방 프리팹(EliteRoomEvent)이 정한다."),
                        EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    foreach (var e in floorsElites) FloorEnemyChip(e, "");
                }
                GUILayout.FlexibleSpace();
            }

            if (r.boss)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("보스", EditorStyles.miniBoldLabel, GUILayout.Width(44));
                    FloorEnemyChip(r.bossData, $"  (체력 {r.bossData.maxHP:0})");
                    if (r.bossLinked.Length > 0)
                        GUILayout.Label(new GUIContent("· " + r.bossLinked, "보스 프리팹 · AI 패턴에 걸린 다른 적 데이터. 2페이즈면 전환 때 스탯을 이 데이터로 통째로 바꾸고 체력을 다시 채운다.\n" +
                            "언제 넘어가는지는 보스 코드가 정해서(예: BoneMasterController 는 흉갑 파괴 = 1페이즈 체력 40% 선, ArcherBossAIPatternSO 는 체력 0 에서 부활) 두 체력을 더하지 않는다."),
                            EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }

            foreach (var w in r.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
        }
    }

    private void FloorEnemyChip(EnemyMinionDataSO e, string suffix)
    {
        var ir = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
        EquipmentEditorPanel.DrawSprite(ir, EnemyAssets.Preview(e));
        if (GUILayout.Button(new GUIContent(EnemyAssets.DisplayName(e) + suffix, $"{EnemyAssets.TierName(e.tier)} · 체력 {e.maxHP:0} · 공격력 {e.attack:0.#}\n누르면 '하나씩 편집'에서 연다"),
                EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
        { var t = e; Defer(() => { selected = t; view = View.Detail; }); }
    }

    private int FloorsKey(MapGenerationDataSO map)
    {
        int k = version;
        void Mix(UnityEngine.Object o) => k = unchecked(k * 31 + (o != null ? EditorUtility.GetDirtyCount(o) : 0));
        Mix(map); Mix(EnemyAssets.ClusterRegistry); Mix(EnemyAssets.MinionRegistry); Mix(floorsShop);
        foreach (var c in clusters) Mix(c);
        foreach (var e in all) Mix(e);
        return k;
    }

    private void BuildFloors(MapGenerationDataSO map)
    {
        floorRows = new List<FloorRow>();
        floorsElites = EnemyAssets.ElitePool();
        var notes = new List<string>();
        var tuning = map.floorTuning ?? new List<MapGenerationDataSO.FloorTuningEntry>();
        var bosses = (map.floorBosses ?? new List<MapGenerationDataSO.FloorBossEntry>()).Where(b => b != null).ToList();
        var bossFloors = bosses.Where(b => b.floor >= 1 && map.GetBossForFloor(b.floor) != null).Select(b => b.floor).ToList();
        floorsLast = bossFloors.Count > 0 ? bossFloors.Min() : 0;
        int shown = floorsLast > 0 ? floorsLast : Mathf.Max(1, tuning.Where(t => t != null).Select(t => t.floor).DefaultIfEmpty(1).Max());
        var registry = EnemyAssets.ClusterRegistry != null && EnemyAssets.ClusterRegistry.enemyClusters != null
            ? EnemyAssets.ClusterRegistry.enemyClusters : new List<EnemyClusterSO>();
        int gold = floorsShop != null ? floorsShop.normalClearGold : 0;

        // 표에 안 나오는 행 — 런타임은 같은 층 첫 행만 읽고(GetTuningForFloor/GetBossForFloor), 마지막 층 뒤로는 가지 않는다.
        if (floorsLast == 0) notes.Add($"보스 처치가 유일한 게임 클리어 조건인데 보스가 배정된 층이 없습니다. 조절표에 있는 층({shown}층)까지만 보여줍니다.");
        foreach (var g in tuning.Where(t => t != null).GroupBy(t => t.floor).OrderBy(x => x.Key))
        {
            if (g.Count() > 1) notes.Add($"{g.Key}층 조절 행이 {g.Count()}개 — 위쪽 첫 행만 쓰입니다.");
            if (g.Key == 0) notes.Add("0층 조절 행은 튜토리얼용입니다(직접 배치한 적이 있는 튜토리얼 방은 이 값을 안 씁니다). 이 표에는 안 나옵니다.");
            else if (g.Key < 0 || (floorsLast > 0 && g.Key > floorsLast)) notes.Add($"{g.Key}층 조절 행은 게임이 가지 않는 층이라 쓰이지 않습니다.");
        }
        foreach (var g in bosses.GroupBy(b => b.floor).OrderBy(x => x.Key))
        {
            if (g.Count() > 1) notes.Add($"{g.Key}층 보스 배정이 {g.Count()}개 — 위쪽 첫 칸만 쓰입니다.");
            if (map.GetBossForFloor(g.Key) == null) notes.Add($"{g.Key}층 보스 칸이 비어 있어 그 층은 보스 층이 아닙니다.");
            else if (g.Key < 1 || g.Key > floorsLast) notes.Add($"{g.Key}층 보스는 {(g.Key < 1 ? "1층 미만이라" : $"{floorsLast}층 보스 처치(게임 클리어) 뒤라")} 나오지 않습니다.");
        }
        if (!map.useIsaacStylePlacement) notes.Add("물리 분산 배치 모드: 처음 1~4개 일반 방을 무작위로 먼저 놓아서 일반 방이 아래 값보다 많을 수 있습니다.");
        floorsNotes = string.Join("\n", notes);

        floorsTotalEnemies = 0f; floorsTotalGold = 0;
        for (int f = 1; f <= shown; f++)
        {
            var t = map.GetTuningForFloor(f);
            var c = map.GetRoomCountsForFloor(f);
            var r = new FloorRow
            {
                floor = f, rc = c, tuningIndex = t != null ? tuning.IndexOf(t) : -1,
                waves = Mathf.Max(1, map.GetWavesForFloor(f)), // 웨이브 1 은 값이 0 이하여도 항상 나온다(NormalRoomEvent)
                scale = t != null ? t.enemyCountScale : 1f,
                bossData = map.GetBossForFloor(f), skipElite = t != null && t.skipEliteEncounter,
                augment = Mathf.Max(0, c.augment),
            };
            r.boss = r.bossData != null;
            r.bossLinked = r.boss ? FloorLinkedData(r.bossData) : "";
            r.elite = Mathf.Max(0, r.boss ? c.elite - 1 : c.elite);
            int specials = Mathf.Max(0, c.shop) + Mathf.Max(0, c.enhanceShop) + r.elite + (r.boss ? 1 : 0) + Mathf.Max(0, c.reward) + r.augment;
            r.normal = Mathf.Max(0, Mathf.Max(c.minNormal, c.total - specials - 1));
            r.total = 1 + specials + r.normal;
            r.overrides = t == null ? "" : string.Join(", ", new[]
            {
                (t.totalRoomCount, "총합"), (t.minNormalRooms, "일반 최소"), (t.shopCount, "상점"), (t.enhanceShopCount, "강화 상점"),
                (t.rewardCount, "보상"), (t.eliteCount, "엘리트"), (t.augmentRoomCount, "증강"),
            }.Where(x => x.Item1 >= 0).Select(x => x.Item2));

            // NormalRoomEvent.SpawnWaves: 전체 목록이 비어 있으면 층 목록을 보기 전에 멈춘다. 그다음 층 목록에 빈 칸 아닌 군집이 있으면
            // 그것만, 아니면 전체 목록(빈 칸도 그대로 추첨된다).
            var listed = t != null && t.clusterPool != null ? t.clusterPool.Where(x => x != null).ToList() : new List<EnemyClusterSO>();
            if (listed.Count == 0 && t != null && t.clusterPool != null && t.clusterPool.Count > 0)
                r.warnings.Add("층 지정 군집 목록이 전부 빈 칸이라 전체 군집 목록을 씁니다.");
            if (registry.Count == 0 && listed.Count > 0)
            {
                r.warnings.Add("전체 군집 목록이 비어 있어 층 지정 목록이 있어도 게임은 적을 하나도 안 뽑습니다(NormalRoomEvent 가 전체 목록부터 확인).");
                listed.Clear();
            }
            r.poolFromTuning = listed.Count > 0;
            var pool = r.poolFromTuning ? listed : registry;
            r.poolSize = pool.Count;
            float n = 0f, hp = 0f;
            var noPrefab = new HashSet<EnemyMinionDataSO>();
            foreach (var g in pool.Where(x => x != null).GroupBy(x => x))
            {
                var line = new FloorPoolLine { cluster = g.Key, weight = g.Count(), count = EnemyAssets.WaveCount(g.Key), scaled = EnemyAssets.WaveCount(g.Key, r.scale) };
                line.enemies = g.Key.enemies?.Where(x => x.enemyData != null && x.count > 0).ToList() ?? new List<EnemyCount>();
                // 줄이기 = 무작위 개체 제거, 늘리기 = 원래 개체 중 무작위 복제 → 기댓값은 (원래 합 / 원래 수) × 바뀐 수.
                // 프리팹 없는 적은 DataManager.CreateUnit 이 null 이라 자리만 차지하고 안 나온다(배율 계산엔 들어감).
                float k = line.count > 0 ? (float)line.scaled / line.count : 0f;
                foreach (var x in line.enemies)
                {
                    if (x.enemyData.minionPrefab == null) { noPrefab.Add(x.enemyData); continue; }
                    var (sn, shp) = FloorSplit(x.enemyData, 0);
                    line.spawned += x.count * k;
                    line.split += sn * x.count * k;
                    line.hp += (x.enemyData.maxHP + shp) * x.count * k;
                }
                n += (line.spawned + line.split) * line.weight;
                hp += line.hp * line.weight;
                r.pool.Add(line);
            }
            if (noPrefab.Count > 0)
                r.warnings.Add($"프리팹이 없는 적 {string.Join(", ", noPrefab.Select(EnemyAssets.DisplayName))} — 뽑혀도 게임이 만들지 못해 안 나옵니다(마릿수 · 체력에서 뺌).");
            if (pool.Count > 0) { r.enemies = r.waves * n / pool.Count; r.hp = r.waves * hp / pool.Count; }

            int nulls = pool.Count(x => x == null);
            if (pool.Count == 0) r.warnings.Add("군집이 하나도 없어 일반 · 증강 방에 적이 나오지 않습니다(입장하면 바로 클리어).");
            else if (nulls > 0) r.warnings.Add($"전체 군집 목록에 빈 칸 {nulls}개 — 뽑히면 그 웨이브는 적이 0마리입니다.");
            if (!r.boss && r.elite == 0) r.warnings.Add("엘리트 방이 0개라 다음 층 포탈이 생기지 않습니다(포탈은 엘리트 · 보스 방을 깨야 생김).");
            if (r.elite > 0 && !r.skipElite && floorsElites.Count == 0) r.warnings.Add("엘리트 후보 목록이 비어 있습니다.");

            r.gold = (r.normal + r.augment) * gold;
            floorsTotalEnemies += r.enemies * (r.normal + r.augment);
            floorsTotalGold += r.gold;
            floorRows.Add(r);
        }
    }

    /// <summary>DualSplitAIPatternSO: 죽으면 splitPrefabA/B 를 하나씩 만들어 방에 등록한다(방을 깨려면 이것도 잡아야 함).
    /// 갈라진 개체의 데이터 = 그 프리팹 BaseEntity.minionData, 그 패턴이 또 분열이면 이어진다. (더 생기는 수, 그 체력 합)</summary>
    private static (float n, float hp) FloorSplit(EnemyMinionDataSO e, int depth)
    {
        float n = 0f, hp = 0f;
        if (depth < 4 && e.aiPattern is DualSplitAIPatternSO d) // 서로를 가리키는 순환 대비
            foreach (var p in new[] { d.splitPrefabA, d.splitPrefabB })
            {
                var c = p != null && p.TryGetComponent<BaseEntity>(out var be) ? be.MinionData : null;
                if (c == null) continue;
                var (cn, chp) = FloorSplit(c, depth + 1);
                n += 1f + cn;
                hp += c.maxHP + chp;
            }
        return (n, hp);
    }

    /// <summary>보스 프리팹 컴포넌트 · AI 패턴에 직렬화된 다른 적 데이터(BoneMasterController.phase2Data 등). 표시용 한 줄.</summary>
    private static string FloorLinkedData(EnemyMinionDataSO boss)
    {
        var parts = new List<string>();
        var owners = new List<UnityEngine.Object> { boss.aiPattern };
        if (boss.minionPrefab != null) owners.AddRange(boss.minionPrefab.GetComponentsInChildren<MonoBehaviour>(true));
        foreach (var o in owners.Where(x => x != null))
            using (var so = new SerializedObject(o))
            {
                var it = so.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is EnemyMinionDataSO d && d != boss
                        && !(o is BaseEntity && it.propertyPath == "minionData"))
                        parts.Add($"{ObjectNames.NicifyVariableName(it.name)}: {EnemyAssets.DisplayName(d)} 체력 {d.maxHP:0}");
            }
        return string.Join(" · ", parts.Distinct());
    }
}
