using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>에디터 표시 사전. 저장 키/애니메이션 태그/Resources 경로는 번역하지 않는다.</summary>
public static class DesignerInspectorLabels
{
    private static readonly Regex Tokens = new Regex(@"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|\d+|[가-힣]+", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> Words = ParseWords();
    private static readonly Dictionary<string, string[]> Specific = ParseSpecific();
    private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>();

    private static Dictionary<string, string> ParseWords()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in WordData.Split(new[] {';', '\n', '\r'}, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('='); if (eq > 0) result[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
        }
        return result;
    }
    private static Dictionary<string, string[]> ParseSpecific()
    {
        var result = new Dictionary<string, string[]>();
        foreach (string line in SpecificData.Split(new[] {'\n', '\r'}, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length >= 3) result[parts[0]] = parts;
        }
        return result;
    }
    public static string Translate(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (Translations.TryGetValue(name, out var found)) return found;
        var words = new List<string>();
        foreach (Match token in Tokens.Matches(name))
            words.Add(Words.TryGetValue(token.Value, out var translated) ? translated : token.Value);
        string result = string.Join(" ", words).Trim();
        Translations[name] = result;
        return result;
    }
    public static IEnumerable<string> UnknownWords(string name)
    {
        foreach (Match token in Tokens.Matches(name))
            if (Regex.IsMatch(token.Value, "[a-zA-Z]") && !Words.ContainsKey(token.Value)) yield return token.Value;
    }
    private static string Key(FieldInfo f) => f.DeclaringType.Name + "." + f.Name;
    private static readonly HashSet<string> Unused = new HashSet<string>
    {
        "PlayerController.summonNum", "PlayerController.summonRange", "AIPatternSO.pushRadius", "AIPatternSO.pushStrength",
        "ArcherBossAIPatternSO.phase2HpThreshold", "ArcherBossAIPatternSO.arrowsPerDirection", "BossHPBarUI.delayCatchUpSpeed",
        "EliteChargerAIPatternSO.chargeTelegraphOvershoot", "EnemyMagicianCircleAttack.outerBoundary", "PlayerStateUI.panelParent",
        "BoneMasterAIPatternSO.telegraphWarnColor", "BoneMasterPhase2AIPatternSO.telegraphWarnColor"
    };
    public static bool ReadOnly(FieldInfo f) => f != null && Unused.Contains(Key(f));

    public static GUIContent Content(FieldInfo field, string fallback, string existingHelp, bool code = false)
    {
        if (field == null) return new GUIContent(fallback, existingHelp);
        string label = Translate(field.Name);
        string help = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? existingHelp ?? "";
        if (Specific.TryGetValue(Key(field), out var exact) || Specific.TryGetValue("*." + field.Name, out exact)) { label = exact[1]; help = exact[2]; }
        bool numeric = field.FieldType == typeof(float) || field.FieldType == typeof(int);
        string lower = field.Name.ToLowerInvariant();
        if (numeric && !label.Contains("("))
        {
            if (Regex.IsMatch(lower, "(duration|delay|interval|cooldown|time|seconds|windup|recovery|gap|pause|lifetime|displaytime)$") && !lower.Contains("ratio")) label += " (초)";
            else if (lower.EndsWith("angle") || lower.EndsWith("degrees")) label += " (도)";
        }
        if (string.IsNullOrEmpty(help) || !Regex.IsMatch(help, "[가-힣]"))
        {
            string original = help;
            if (field.FieldType == typeof(AnimationCurve)) help = "그래프를 클릭해 모양을 편집합니다. 시간·진행률 곡선의 축 의미는 이 컴포넌트의 사용 목적을 확인하세요.";
            else if (numeric && label.EndsWith("(초)")) help = "시간을 초 단위로 지정합니다. 값을 높이면 이 구간이 길어집니다. 0의 의미는 해당 동작의 설명을 확인하세요.";
            else if (numeric && lower.Contains("multiplier")) help = "기준 값에 곱합니다. 1 = 원래 값, 0.5 = 절반, 2 = 두 배.";
            else if (numeric && (lower.EndsWith("count") || lower.EndsWith("charges"))) help = "횟수 또는 개수입니다. 정수 단위로 조절합니다.";
            else if (numeric && (lower.EndsWith("radius") || lower.EndsWith("distance") || lower.EndsWith("length"))) help = "범위를 정하는 거리입니다. 값을 높이면 더 멀리까지 적용합니다. 월드 공간 값은 Unity 유닛 기준입니다.";
            else if (numeric && lower.EndsWith("speed")) help = "동작 속도입니다. 값을 높이면 더 빠르게 움직이거나 변합니다. 다른 배율 설정이 있으면 함께 적용됩니다.";
            else if (numeric && lower.EndsWith("ratio")) help = "기준 값에 대한 비율. 0.5 = 50%, 1 = 100%입니다.";
            else if (field.FieldType == typeof(Color)) help = "색상과 투명도를 지정합니다. A(알파)가 0이면 투명, 1이면 불투명합니다.";
            else if (field.FieldType == typeof(LayerMask)) help = "검사하거나 적용할 물리 레이어를 선택합니다. 그림의 앞뒤 순서(Sorting Layer)와는 다른 설정입니다.";
            else if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) help = "사용할 " + field.FieldType.Name + "을 연결합니다. 기존 연결을 바꾸기 전에 사용처를 확인하세요.";
            else if (field.FieldType == typeof(bool)) help = "체크하면 이 기능을 사용합니다.";
            else help = label + " 설정입니다.";
            if (!string.IsNullOrEmpty(original)) help += "\n기존 설명: " + original;
        }
        var range = field.GetCustomAttribute<RangeAttribute>();
        if (range != null) help += $"\n조절 범위: {range.min:g} ~ {range.max:g}.";
        var min = field.GetCustomAttribute<MinAttribute>();
        if (min != null) help += $"\n최솟값: {min.min:g}.";
        if (ReadOnly(field))
        {
            if (!label.StartsWith("사용 안 함")) label = "사용 안 함: " + label;
            help = "현재 코드에서 값을 읽는 곳이 없는 남은 설정입니다. 바꿔도 효과가 없습니다. 데이터 호환을 위해 보존했습니다.";
        }
        help += "\n코드: " + Key(field);
        if (code) label += " · " + field.Name;
        return new GUIContent(label, help);
    }

    private const string SpecificData = @"
FeedbackCatalog.dashTrailRampDuration|대시 잔상 간격 변화 시간|출발부터 이 시간이 지나면 끝 간격에 도달합니다.
FeedbackCatalog.dashTrailIntervalRange|대시 잔상 간격 배율 (시작/끝)|기존 잔상 간격에 곱합니다. 1은 기존과 같습니다.

FeedbackRecipe.enabled|사용|끄면 이 신규 연출만 중단합니다.
FeedbackRecipe.intensity|연출 강도|0은 끄기, 1은 기본 강도입니다. 원본 그림은 확대하지 않습니다.
FeedbackRecipe.cue|효과 번호|후보 문서와 같은 번호입니다.
FeedbackRecipe.title|이름|기획자가 찾는 표시 이름입니다.
FeedbackRecipe.description|설명|이 효과가 언제 나오는지 설명합니다.
FeedbackRecipe.prefab|월드 효과 프리팹|프리팹의 선과 아이콘을 사용합니다. 비어 있는 화면/소리 전용 효과도 있습니다.
FeedbackRecipe.shape|도형 형태|별·고리·점·연결선 등을 선택합니다.
FeedbackRecipe.color|색상|원본 캐릭터 색을 바꾸지 않습니다.
FeedbackRecipe.duration|지속 시간|이 시간이 지나면 효과를 정리합니다.
FeedbackRecipe.delay|시작 지연|호출된 뒤 기다리는 시간입니다.
FeedbackRecipe.count|빛 조각 수|프리팹에 있는 최대 16개 선에서 사용합니다.
FeedbackRecipe.radius|퍼지는 거리|월드 유닛입니다. 원본 도트 크기를 늘리지 않습니다.
FeedbackRecipe.width|선 굵기|0.03125는 32 PPU 기준 1픽셀입니다.
FeedbackRecipe.drift|진행 중 이동|효과 로컬 좌표의 이동량입니다.
FeedbackRecipe.minimumTier|최소 성장 단계|0 기본 / 1 강화 / 2 진화 / 3 최종.
FeedbackRecipe.scaleWithTier|성장에 따른 강약 적용|성장 단계의 입자 수·거리·강도를 곱합니다.
FeedbackRecipe.interval|같은 효과 최소 간격|짧은 시간에 몰리는 동일 효과의 출력을 제한합니다.
FeedbackRecipe.motion|움직임 곡선|가로는 수명 비율, 세로는 진행 비율입니다.
FeedbackRecipe.sound|추가 효과음|프로젝트에 있는 소리만 연결합니다.
FeedbackRecipe.volume|추가 효과음 크기|기존 효과음 위에 작게 덧붙입니다.
FeedbackRecipe.pitchVariation|효과음 음높이 변화|80번을 켜면 이 범위에서 조금씩 달라집니다.
FeedbackTier.intensity|연출 강도|0은 끄기, 1은 기본 강도입니다. 원본 그림은 확대하지 않습니다.
FeedbackTier.title|이름|기획자가 찾는 표시 이름입니다.
FeedbackTier.density|입자 수 배율|원본 프리팹 한도 안에서 적용합니다.
FeedbackTier.reach|퍼지는 거리 배율|그림 배율은 바꾸지 않습니다.
FeedbackCatalog.enabled|사용|끄면 이 신규 연출만 중단합니다.
FeedbackCatalog.intensity|연출 강도|0은 끄기, 1은 기본 강도입니다. 원본 그림은 확대하지 않습니다.
FeedbackCatalog.simultaneousBursts|동시에 남길 월드 효과 수|전투가 몰려도 이 수를 넘게 생성하지 않습니다.
FeedbackCatalog.burstsPerFrame|프레임당 월드 효과 제한|한 프레임에 생성할 최대 효과 수입니다.
FeedbackCatalog.soundsPerFrame|프레임당 추가 효과음 제한|기존 공격 소리 외에 덧붙이는 소리의 수입니다.
FeedbackCatalog.tiers|성장 단계|기본·강화·진화·최종 순서입니다.
FeedbackCatalog.effects|개별 연출|번호는 이펙트 후보 문서와 같습니다.
FeedbackCatalog.overlayPrefab|화면 연출 프리팹|GameManager에 실제 배치된 프리팹입니다.
FeedbackCatalog.lowHealthRatio|저체력 기준 비율|이 비율 아래일 때 화면 가장자리가 호흡합니다.
FeedbackCatalog.lowHealthInterval|저체력 호흡 주기|가장자리 밝기가 반복되는 시간입니다.
FeedbackCatalog.ambientInterval|환경 효과 반복 간격|포탈·드롭·환경 먼지의 생성 간격입니다.
FeedbackCatalog.trailInterval|탄환·대시 보조 잔광 간격|높이면 성기게 생성합니다.
FeedbackCatalog.stepInterval|발걸음 간격|움직이는 동안의 발 먼지 생성 간격입니다.
FeedbackCatalog.waterInterval|물가 파문 간격|플레이어 주변 물가만 검사합니다.
FeedbackCatalog.dirtColor|흙 발걸음 색|기본 지면 색입니다.
FeedbackCatalog.stoneColor|돌 발걸음 색|타일 이름에 stone/rock/brick이 있는 지면입니다.
FeedbackCatalog.waterColor|물가 발걸음 색|인접 물 타일이 있을 때 적용합니다.
FeedbackCatalog.cameraForce|타격 카메라 충격|기존 Cinemachine 충격 제한을 공유합니다.
FeedbackCatalog.hitStopSeconds|강타 정지 시간|진화 이상 강타의 짧은 정지 시간입니다.
FeedbackCatalog.slowMotionScale|보스 처치 감속 배율|1이면 원래 속도입니다.
FeedbackCatalog.slowMotionSeconds|보스 처치 감속 시간|실제 시간 기준으로 끝납니다.
FeedbackBurst.lines|빛 조각 참조|프리팹에 실제 배치한 선입니다.
FeedbackBurst.icon|원본 아이콘|원본 배율 1배를 유지합니다.
FeedbackOverlay.canvasRect|화면 캔버스|좌표 변환 기준입니다.
FeedbackOverlay.iconSize|HUD 알림 아이콘 크기|중첩 알림 등에 쓰는 최대 UI 크기입니다. 습득 아이콘은 바닥에서 보이던 화면 크기를 유지합니다.
FeedbackOverlay.healAbsorbDuration|회복 구슬 흡수 시간|체력바에 도착한 구슬이 작아지며 흡수되는 시간(초)입니다.
FeedbackOverlay.motes|화면 아이콘 풀|에디터에 미리 배치한 이미지입니다.
FeedbackOverlay.edges|가장자리 표시|상·우·하·좌 순서입니다.
FeedbackOverlay.bagAnchor|가방 도착점|아이템 아이콘이 날아갈 위치입니다.
FeedbackOverlay.healthAnchor|체력바 기본 도착점|미리보기용 기본 위치입니다. 실제 회복은 HUD의 회복될 빈 구간을 따라갑니다.
FeedbackOverlay.skillAnchor|스킬 준비 위치|스킬 준비 완료 표시 위치입니다.
FeedbackOverlay.goldAnchor|골드 표시 위치|구매 피드백 위치입니다.
FeedbackSurface.kind|환경 종류|포탈·풀·먼지·드롭 중 선택합니다.
FeedbackSurface.visibleDistance|주변 표시 거리|플레이어가 이 거리 안에 있을 때만 반복 효과를 만듭니다.
FeedbackSurface.touchDistance|풀 반응 거리|가까이 들어올 때 한 번 잎 파편을 만듭니다.
FeedbackSurface.offset|효과 위치 보정|게임 판정 위치를 바꾸지 않고 연출 위치만 옮깁니다.
MainMinionDataSO.feedbackTier|연출 성장 단계|미니언 강화 단계가 추가됩니다.
MainMinionDataSO.feedbackColor|계통 연출 색|기사·무사의 적중 연출 색입니다.
EnemyMinionDataSO.impactColor|피격 재질 색|금속은 노랑, 뼈는 흰색 등으로 조절합니다.
MeleeDodgeController.upwardEffectState|위쪽 대시 이펙트 상태|위쪽 대시에 재생할 Animator 상태 이름입니다. 기본은 원본 Aseprite 태그 Up입니다.
MeleeDodgeController.downwardEffectState|아래·수평 대시 이펙트 상태|아래쪽 또는 수평 대시에 재생할 Animator 상태 이름입니다. 기본은 Down입니다. 좌우는 이동 방향에 맞춰 반전합니다.
BaseAIPatternSO.attackRangeInset|공격 거리 안쪽 여유 (유닛)|사거리에서 이 거리만큼 더 접근한 뒤 공격합니다. 0이면 사거리 경계에서 공격합니다.
BaseAIPatternSO.rangedFleeThreshold|후퇴하는 원거리 기준 (유닛)|공격 사거리가 이 값 이상인 일반 적은 공격 대기 중 후퇴합니다. 돌진형은 제외됩니다.
BaseAIPatternSO.fleeTriggerDistance|후퇴 시작 거리 (유닛)|공격 대기 중 플레이어가 이 거리 안에 있으면 후퇴합니다.
BaseAIPatternSO.fleeStepDistance|한 번에 후퇴할 거리 (유닛)|새 후퇴 목적지를 이 거리만큼 떨어진 곳에서 찾습니다.
BaseAIPatternSO.fleeArrivalDistance|후퇴 목적지 갱신 거리 (유닛)|현재 목적지까지 남은 거리가 이 값보다 작으면 다음 후퇴 방향을 고릅니다.
BaseAIPatternSO.fleeSpreadAngle|후퇴 방향 흔들림 (도)|플레이어 반대 방향에서 좌우로 무작위 선택할 각도입니다. 0이면 바로 뒤로 물러납니다.
BaseAIPatternSO.fallbackAttackWindup|타격 이벤트가 없을 때 예고 (초)|공격 애니메이션이나 OnHitEvent가 없을 때 사용할 준비 시간입니다. 이벤트가 있으면 해당 시각을 따릅니다.
BaseAIPatternSO.attackAnimationIntervalRatio|공격 간격 중 모션 비율|공격 간격의 이 비율 안에 모션이 끝나도록 빠르게 재생합니다. 0.9는 90%입니다.
NormalRoomEvent.spawnStartDelay|방 진입 후 소환 대기 (초)|방에 들어간 뒤 카메라가 자리 잡도록 기다리는 시간입니다. 0이면 바로 소환 절차를 시작합니다.
NormalRoomEvent.spawnTelegraphDuration|몬스터 등장 예고 (초)|일반 웨이브와 튜토리얼 배치 몬스터가 등장하기 전 바닥 예고가 차오르는 시간입니다.
NormalRoomEvent.spawnTelegraphScale|등장 예고 크기 배율|몬스터 등장 위치 표시의 가로·세로 크기입니다. 몬스터 크기나 피해 판정은 바꾸지 않습니다.
NormalRoomEvent.superArmorEnemyCount|웨이브 슈퍼아머 몬스터 수|일반 랜덤 웨이브에서 슈퍼아머를 주는 몬스터 수입니다. 0이면 주지 않습니다. 튜토리얼 직접 배치에는 적용하지 않습니다.
NormalRoomEvent.spawnSuperArmorAmount|소환 시 슈퍼아머 게이지|무작위로 선정된 일반 몬스터에게 주는 슈퍼아머 양입니다. 엘리트·보스의 영구 슈퍼아머 설정과 별개입니다.
BossRoomEvent.spawnStartDelay|방 진입 후 소환 대기 (초)|방에 들어간 뒤 카메라가 자리 잡도록 기다리는 시간입니다. 0이면 바로 소환 절차를 시작합니다.
EliteRoomEvent.spawnStartDelay|방 진입 후 소환 대기 (초)|방에 들어간 뒤 카메라가 자리 잡도록 기다리는 시간입니다. 0이면 바로 소환 절차를 시작합니다.
BaseEntity.attackHitActiveDuration|기본 공격 판정 유지 (초)|공통 기본 공격 히트박스가 타격 순간 이후 남아 있는 시간입니다. 별도 보스·스킬 판정은 각 패턴 설정을 따릅니다.
BaseEntity.attackSoundVolume|공격 효과음 볼륨|유닛 공격 효과음의 볼륨입니다. 전체 효과음 볼륨과 곱해집니다.
MeleeCombatController.attackHitActiveDuration|평타 판정 유지 (초)|플레이어 평타 히트박스의 활성 시간입니다. 모션이나 콤보 수는 변경하지 않습니다.
PlayerParryController.perfectGuardCostRatio|퍼펙트 가드 소모 배율|일반 가드 소모량에 곱합니다. 0.5이면 절반, 0이면 퍼펙트 가드 시 소모하지 않습니다.
PlayerController.walkAnimationReferenceSpeed|걷기 모션 1배속 기준 속도|이 이동속도일 때 걷기 모션을 원래 속도로 재생합니다. 실제 이동속도는 바꾸지 않습니다.
PlayerController.walkAnimationSpeedRange|걷기 모션 배속 범위|X는 최소, Y는 최대 재생 배속입니다. 이동·대시·공격 판정 시간에는 영향이 없습니다.
MinionActionSkillSO.teleportTargetDistance|근접 스킬의 대상과 간격 (유닛)|대상 앞이나 뒤로 순간이동하는 스킬에서 대상 중심과 떨어질 거리입니다. 원거리형 스킬에는 적용하지 않습니다.
EliteChargerAIPatternSO.leapImpactDelay|착지 후 타격 예고 (초)|착지 공격 히트박스의 마지막 예고 시간입니다. 실제 타격 대기도 같은 값을 사용합니다.
EliteChargerAIPatternSO.impactHitActiveDuration|휩쓸기·착지 판정 유지 (초)|휩쓸기와 착지 타격 히트박스가 활성 상태로 남아 있는 시간입니다.
EliteChargerAIPatternSO.wallChargeMaxDuration|벽 돌진 최대 시간 (초)|벽을 향한 특수 돌진의 최대 진행 시간입니다. 먼저 벽을 만나면 기존 벽 충돌 처리가 적용됩니다.
ArcherBossAIPatternSO.phaseTwoMoveSpeed|2페이즈 데이터가 없을 때 이동속도|2페이즈 전용 데이터가 없을 때만 사용하는 기본 이동속도입니다. 데이터가 연결되어 있으면 그 스탯을 따릅니다.
ArcherBossAIPatternSO.centerReturnSpeedMultiplier|중앙 복귀 이동속도 배율|방 중앙으로 복귀할 때 기본 이동속도에 곱하는 배율입니다.
ArcherBossAIPatternSO.arrowLifetime|화살 최대 생존 시간 (초)|보스가 발사하는 화살이 명중하지 않았을 때 유지되는 최대 시간입니다.
DashCooldownUI.usedPipAlpha|충전 중 대시 칸 불투명도|사용한 대시 칸의 밝기입니다. 0이면 보이지 않고 1이면 완전히 표시됩니다.
PlayerController.uiIconGap|툴팁과 F 표시 간격 (유닛)|툴팁 위쪽 끝에서 F 아이콘 아래쪽 끝까지의 간격입니다. 상호작용 대상 선택에는 영향이 없습니다.
PlayerStateUI.unavailableSkillAlpha|사용 불가 스킬 불투명도|사용할 수 없는 스킬 아이콘의 불투명도입니다. 0이면 숨기고 1이면 원래 밝기로 표시합니다.
PlayerStateUI.healFillDuration|회복 게이지 채움 시간|구슬 도착 후 체력바와 숫자가 차오르는 시간(초)입니다. 실제 체력은 즉시 회복합니다.
PlayerStateUI.healFillCurve|회복 게이지 채움 곡선|구슬 도착 후 시간(가로)에 따른 회복 표시 진행률(세로, 0~1)입니다.
SkillExplainUI.statsRefreshInterval|스탯 표시 갱신 간격 (초)|정보창의 스탯 수치를 새로 읽는 간격입니다. 작을수록 빠르게 반영되지만 갱신 횟수가 늘어납니다.
DebuffIcon.triggeredScale|상태이상 아이콘 강조 배율|상태이상 아이콘을 표시할 때 적용하는 크기 배율입니다.
DebuffIcon.triggeredOutlineDistance|상태이상 아이콘 외곽선 두께|아이콘의 빨간 외곽선 두께입니다. X와 Y는 UI 픽셀 단위입니다.
DebuffIcon.triggeredOutlineColor|상태이상 아이콘 외곽선 색|상태이상이 활성화되었을 때 아이콘에 표시할 외곽선 색입니다.
SoundManager.bgmCrossfadeDuration|방 배경음 교체 시간 (초)|방 이동 시 이전 배경음에서 새 배경음으로 바뀌는 시간입니다. 0이면 즉시 전환합니다. 코드에서 시간을 명시하면 그 값이 우선합니다.
StatusBalanceSO.defaultDuration|공통 기본 지속시간|별도 시간이 없는 출혈·중독·비폭·취약의 지속시간(초)입니다. 공격이나 스킬에 명시한 시간은 그 설정을 따릅니다.
StatusBalanceSO.stunDuration|기절 기본 지속시간|공격에서 시간을 지정하지 않은 기절의 기본 지속시간(초)입니다.
StatusBalanceSO.stunMinDuration|기절 최소 시간|일반 공격으로 부여하는 기절의 최소 시간(초)입니다. 보스 그로기처럼 제한을 우회하는 기절에는 적용하지 않습니다.
StatusBalanceSO.stunMaxDuration|기절 최대 시간|일반 공격으로 부여하는 기절의 최대 시간(초)입니다. 최소 시간보다 작게 입력하면 최소 시간으로 보정합니다.
StatusBalanceSO.freezeDuration|빙결 기본 지속시간|공격에서 시간을 지정하지 않은 빙결의 기본 지속시간(초)입니다.
StatusBalanceSO.stunImmunityDuration|기절 종료 후 내성|기절이 자연 종료된 뒤 다시 기절하지 않는 시간(초)입니다. 0이면 내성이 없습니다.
StatusBalanceSO.freezeImmunityDuration|빙결 파괴 후 내성|피해로 빙결이 깨진 뒤 다시 얼지 않는 시간(초)입니다. 자연 종료에는 적용하지 않습니다.
StatusBalanceSO.freezeBreakDamage|빙결 파괴 추가 피해|일반 빙결을 타격으로 해제할 때 추가되는 고정 피해입니다. 무기 전용 빙결 효과와는 별개입니다.
StatusBalanceSO.bleedHitDamage|출혈 타격 추가 피해|출혈 중 직접 타격을 받을 때 추가되는 고정 피해입니다.
StatusBalanceSO.poisonTickDamage|중독 1회 피해|중독이 한 번 발동할 때의 고정 피해입니다. 초당 피해는 이 값 / 중독 피해 간격입니다.
StatusBalanceSO.poisonTickInterval|중독 피해 간격|중독 피해 사이의 간격(초)입니다. 작을수록 자주 피해를 주므로 초당 피해도 늘어납니다.
StatusBalanceSO.bloodPopThreshold|비폭 발동 스택|이 스택에 도달하면 폭발하고 스택을 소모합니다.
StatusBalanceSO.bloodPopDamage|비폭 폭발 피해|비폭이 자신과 주변 대상에게 주는 고정 피해입니다.
StatusBalanceSO.bloodPopRadius|비폭 폭발 반지름|비폭의 폭발 반지름(Unity 유닛)입니다. 표시 크기와 타격 범위를 함께 바꿉니다.
StatusBalanceSO.bloodPopFuse|비폭 폭발 예고|비폭 폭발 전에 표시하는 예고 시간(초)입니다. 0이면 즉시 타격합니다.
StatusBalanceSO.bloodPopHitDuration|비폭 판정 유지 시간|비폭 폭발의 타격 판정이 남는 시간(초)입니다.
StatusBalanceSO.vulnerableDamageBonus|취약 추가 물리 피해 비율|취약 상태에서 받는 물리 피해의 추가 비율입니다. 0.2는 +20%입니다. 다른 피해 유형에는 적용하지 않습니다.
*.closeRange|근거리 구간 (유닛)|이 거리 이내를 근거리로 봅니다. 보스의 근거리 후보는 휩쓸기/찌르기입니다.
*.midRange|중거리 구간 끝 (유닛)|근거리 밖부터 이 거리까지는 중거리(찌르기/도약), 더 멀면 원거리(도약)입니다.
*.engageRange|공격 개시 거리 (유닛)|이 거리 안에 들어오면 공격 후보를 고릅니다. 밖에 있으면 추격하되 추격 제한 시간을 따릅니다.
*.attackGap|다음 패턴 전 대기 (초)|패턴을 마친 뒤 다음 공격을 허용하기까지의 간격. 패턴 후 회복 시간과 함께 전체 템포를 결정합니다.
*.chaseTimeLimit|공격 전 최대 추격 시간 (초)|공격 개시 거리 밖에서도 이 시간 이상 추격하면 현재 거리의 패턴을 시도합니다.
*.postPatternRecovery|패턴 후 회복 시간 (초)|패턴 종료 뒤 쉬는 시간. 다음 패턴 전 대기와 함께 조절합니다.
*.sweepStepDistance|휩쓸기 전진 거리 (유닛)|0 = 제자리에서 휩쓸기. 양수면 해당 거리 전방에 부채꼴을 예고하고 그곳으로 전진해 타격합니다.
*.sweepHalfAngle|휩쓸기 반각 (도)|부채꼴 전체 각도의 절반. 90이면 전체 180도 반원입니다.
*.basicAttackWindup|평타 예고 시간 (초)|공격을 준비하는 시간. 늘리면 피하거나 카운터를 맞출 여유가 늘어납니다.
*.hitboxSize|타격 판정 크기 (가로 / 세로)|월드 공간의 타격 판정 크기입니다. 그림 크기와는 별도로 적용됩니다.
*.useUnscaledTime|일시정지 중에도 진행|켜면 히트스톱이나 일시정지에도 실제 시간 기준으로 연출을 진행합니다.
*.unscaledTime|일시정지 중에도 진행|켜면 일시정지와 무관하게 실제 시간으로 연출합니다.
*.turnSpeed|최대 회전 속도 (도/초)|초당 꺾을 수 있는 각도입니다. 작게 하면 완만하게 추적합니다. 0의 동작은 개별 스크립트의 사용처를 확인하세요.
*.hitFlashDuration|피격 섬광 시간 (초)|공격을 맞은 뒤 밝게 반짝이는 시간입니다.
*.hitFlashStrength|피격 섬광 강도 (0~1)|0 = 섬광 없음, 1 = 최대 섬광입니다.
*.damageMultiplier|피해 배율|기본 피해에 곱합니다. 1 = 기본 피해, 1.5 = 150% 피해.
*.castDuration|시전 시간 (초)|시전 동작의 전체 시간입니다. 애니메이션 이벤트가 있으면 실제 타격 시점은 해당 이벤트를 따릅니다.
*.normalPositionPunchTime|일반 숫자 위치 반동 시간 (초)|일반 데미지 숫자가 튀었다 제자리로 돌아오는 시간입니다.
*.normalScalePunchTime|일반 숫자 확대 시간 (초)|일반 데미지 숫자의 크기 반동 시간입니다.
*.criticalPunchTime|치명타 숫자 확대 시간 (초)|치명타 숫자가 커졌다 되돌아오는 시간입니다.
*.criticalShakeTime|치명타 숫자 회전 시간 (초)|치명타 숫자가 회전하며 흔들리는 시간입니다.
*.criticalShakeAngle|치명타 숫자 회전 강도 (도)|숫자 회전 흔들림의 각도입니다. 크게 하면 더 과장됩니다.
ShopRegistrySO.equipmentPool|상점 장비 후보|상점이 장비 상품을 만들 때 사용하는 목록입니다. 현재 장비 강화는 강화 NPC가 처리합니다.
ShopRegistrySO.itemPool|상점 아이템 후보|진열할 아이템 후보입니다. 구매 시 보유 한도까지 즉시 습득하고, 습득할 수 없는 경우 바닥에 드롭합니다.
ShopRegistrySO.enhanceCostPerLevel|강화 단계당 추가 비용 (골드)|기본 강화 비용 + 현재 단계 × 이 값. 상점당 강화 제한은 강화 NPC/디버그 옵션에서 결정합니다.
PlayerController.summonNum|사용 안 함: 옛 소환 수|현재 실행 코드에서 읽지 않는 옛 필드입니다. 변경해도 효과가 없습니다. 직렬화 호환을 위해 남겨둔 값입니다.
PlayerController.summonRange|사용 안 함: 옛 소환 범위|현재 실행 코드에서 읽지 않는 옛 필드입니다. 변경해도 효과가 없습니다.
PlayerController.movementAccelerationTime|출발 가속 시간 (초)|이동 입력 후 목표 속도에 도달하는 부드러움. 작으면 즉각 출발, 크면 천천히 출발합니다.
PlayerController.movementSmoothTime|정지 감속 시간 (초)|입력을 놓았을 때 멈추는 부드러움. 작으면 단단하게 멈추고, 크면 미끄러지는 느낌입니다.
PlayerController.movementTurnTime|방향 전환 시간 (초)|반대 방향 입력에 반응하는 시간. 작으면 빠르게 꺾입니다.
PlayerController.dashSpeed|일반 대시 속도 (유닛/초)|대시 이동 속도. 총 거리는 속도와 지속 시간의 영향을 함께 받습니다.
PlayerController.dashDuration|일반 대시 지속 시간 (초)|대시 동작의 전체 시간. 속도와 함께 조절합니다.
PlayerController.attackDashSpeed|평타 전진 속도 (유닛/초)|평타를 낼 때 앞으로 미는 속도입니다. 장비/콤보의 전진 배율도 적용됩니다.
PlayerController.attackDashDuration|평타 전진 시간 (초)|평타로 앞으로 이동하는 시간. 늘리면 전진 거리가 함께 커집니다.
PlayerParryController.guardRegenPerSecond|일반 가드 회복량 (게이지/초)|가드를 내리고 회복 대기 시간이 지난 뒤 초당 회복하는 양입니다. 파괴 회복에는 적용되지 않습니다.
PlayerParryController.guardBreakRecoveryDuration|가드 파괴 후 완충 시간 (초)|최대 게이지를 이 시간으로 나눈 만큼 매초 회복합니다. 최대 50 / 12초 = 약 4.17/초. 완충 전에는 가드를 사용할 수 없습니다.
PlayerParryController.perfectGuardWindow|퍼펙트 가드 판정 시간 (초)|가드를 올린 뒤 실제 공격을 막기까지 이 시간 이내면 퍼펙트 가드입니다.
PlayerParryController.guardBreakThresholdRatio|가드 파괴 기준 비율 (0~1)|방어 후 남은 게이지가 최대치의 이 비율보다 작으면 파괴됩니다. 0.2 = 20%.
PlayerParryController.guardRegenDelay|가드 내린 뒤 회복 대기 (초)|가드를 내린 순간부터 이 시간이 지나면 일반 회복을 시작합니다.
MeleeCombatController.comboResetTime|콤보 유지 시간 (초)|다음 평타를 이어갈 수 있는 시간입니다. 끊기면 첫 타부터 다시 시작합니다.
MeleeCombatController.lightAttackDashMultiplier|평타 전진 거리 배율|플레이어의 평타 전진 속도에 곱하는 배율. 1 = 기본, 0 = 전진 없음.
MeleeDodgeController.rechargeTime|대시 1회 충전 시간 (초)|소모한 대시를 충전하는 기본 시간입니다. 아이템 충전 속도 보정은 별도로 적용됩니다.
VisualSquashStretch.visual|변형할 그림 전용 오브젝트|충돌체·카메라·UI가 없는 그림 전용 Transform을 연결합니다. 물리 루트를 연결하지 마세요.
VisualSquashStretch.deformRenderer|몬스터 그림 렌더러|물리와 그림이 같은 루트인 몬스터용. _FeedbackScale 지원 재질의 정점만 변형합니다. 지정하면 그림 전용 오브젝트보다 우선합니다.
Pulse.name|동작 이름 (호출 키)|Attack / Dash / Land / Hit / Guard / Appear 등의 코드 호출 키입니다. 번역하거나 임의로 바꾸면 해당 동작이 재생되지 않습니다.
Pulse.scale|최대 변형 배율 (가로 / 세로)|1,1 = 원래 모양. 1.2,0.8 = 가로 20% 늘리고 세로 20% 압축. 물리 판정은 변하지 않습니다. 최종 변형은 각 축 0.5~1.5로 제한됩니다.
Pulse.curve|눌림·반동 곡선|X=전체 시간 중 진행률(0~1), Y=변형 적용량. 0=원래 모양, 1=설정한 최대 변형, 음수=반대 방향 반동. 양 끝은 0을 권장합니다.
AnimPhase.tag|애니메이션 태그 (호출 키)|Aseprite/Animator의 실제 태그 이름과 정확히 일치해야 합니다. 한글로 번역하지 마세요.
AnimPhase.offset|단계의 목표 위치 (로컬)|이 애니메이션 단계가 끝날 때 도달할 위치입니다. 이동 곡선으로 중간 속도를 조절합니다.
ImpactFeedback.arcDelay|파동 사이 간격 (초)|원형 파동이 순차적으로 퍼지는 시간차. 0이면 동시에 재생합니다.
ImpactFeedback.sparks|작은 빛 파편 목록|프리팹에 배치한 선 도형. 슬래시 원본 그림의 도트 크기와 독립적으로 움직입니다.
ImpactFeedback.sparkDuration|빛 파편 수명 (초)|파편 하나가 사라질 때까지 걸리는 시간. 전체 이펙트 수명을 넘기지 않습니다.
ImpactFeedback.sparkDelayStep|빛 파편 출발 간격 (초)|파편별 출발 시간차. 0이면 동시에 퍼집니다.
ImpactFeedback.sparkTravelDistance|빛 파편 확산 거리 (유닛)|중심에서 바깥으로 날아갈 거리. 파편 크기는 바꾸지 않습니다.
ImpactFeedback.sparkDropDistance|빛 파편 낙하 거리 (유닛)|수명이 끝날 때까지 아래로 내려갈 거리.
ImpactFeedback.emission|슬래시 발광 곡선|X=수명 진행률(0~1), Y=발광 배율. 1이 기본 밝기이며 크게 하면 더 밝습니다.
ImpactFeedback.opacity|슬래시 불투명도 곡선|X=수명 진행률(0~1), Y=원래 알파에 곱할 비율. 1=유지, 0=사라짐.
ImpactFeedback.streakLifetimeRatio|선 파편 수명 비율 (0~1)|전체 이펙트 수명 중 파편이 보이는 비율. 0.65 = 수명의 65% 지점에서 사라집니다.
ImpactFeedback.arcLifetimeRatio|원형 파동 수명 비율 (0~1)|전체 이펙트 수명 중 원형 파동이 보이는 비율입니다.
ImpactFeedback.streakScaleRange|선 파편 크기 (시작 / 끝)|원래 선 크기에 곱하는 배율. X=처음, Y=마지막.
ImpactFeedback.arcScaleRange|원형 파동 크기 (시작 / 끝)|X=처음 크기, Y=마지막 크기. 원래 선 크기에 곱합니다.
ImpactFeedback.flashScaleRange|순간 섬광 크기 (시작 / 끝)|X=처음, Y=끝. 섬광 지속 시간 동안 커집니다.
TextFloating.punchDuration|일반 숫자 튀는 크기|옛 필드 이름은 duration이지만 실제로는 크기 펀치의 세기입니다. 시간은 일반 숫자 확대 시간에서 조절합니다.
TextFloating.CiriPunchDuration|치명타 숫자 튀는 크기|옛 필드 이름은 Duration이지만 실제로는 크기 펀치의 세기입니다. 시간은 치명타 숫자 확대 시간에서 조절합니다.
TextFloating.displaytime|숫자 표시 시간 (초)|숫자가 떠 있는 전체 시간입니다.
TextFloating.horizontalJitter|숫자 가로 흩어짐 (유닛)|생성 위치의 좌우 무작위 범위. 0이면 가운데에 겹칩니다.
EquipmentSO.basicRepeatHitInterval|추가 타격 사이 간격 (초)|마리오네트 등 같은 공격의 반복 타격 사이 간격입니다. 늘리면 연속 타격이 더 또렷하게 분리됩니다.
EquipmentSO.comboMultipliers|콤보별 평타 피해 배율|목록 순서가 플레이어 1타, 2타, 3타 순서입니다. 현재 3타 콤보이므로 세 항목을 유지하세요. 소환수의 평타 피해는 소환수 데이터에서 별도로 조절합니다.
MinionFinisher.internalCooldown|소환수 평타 내부 대기 (초)|연계 소환수 평타를 낸 뒤 다음 연계를 허용하기까지의 시간입니다.
EquipmentSO.basicDamagePerHit|반복 타격의 1회 피해 배율|각 추가 타격에 곱할 배율. 마리오네트 0.6 = 원래 피해의 60%.
BoneMasterAIPatternSO.sweepTurnSpeed|휩쓸기 최대 조준 회전 속도 (도/초)|0이면 속도 제한 없이 조준합니다. 공격 직전 조준 고정 시간에는 이 값과 무관하게 방향이 고정됩니다.
BoneMasterPhase2AIPatternSO.sweepTurnSpeed|휩쓸기 최대 조준 회전 속도 (도/초)|0이면 무제한. 마지막 조준 고정 구간에는 방향이 고정됩니다.
BoneMasterPhase2AIPatternSO.executionCounterAttempts|집행 카운터 시도 횟수|성공 횟수가 아니라 카운터 기회를 준 횟수입니다. 이 횟수 후 집행을 끝냅니다.
BoneMasterPhase2AIPatternSO.executionCastSpeedScale|집행 시전 시간 배율|1 = 원래 시간, 0.75 = 25% 짧은 시간. 작을수록 빨라집니다.
BoneMasterAIPatternSO.realCounterChance|진짜 카운터 확률 (0~1)|노란색 카운터의 확률. 0.3 = 30%. 페이크 확률과의 나머지는 일반(회색) 패턴입니다.
BoneMasterPhase2AIPatternSO.realCounterChance|진짜 카운터 확률 (0~1)|노란색 카운터 확률. 0.3 = 30%. 남은 비율은 일반 패턴입니다.
BoneMasterAIPatternSO.fakeCounterChance|페이크 카운터 확률 (0~1)|빨간색 카운터 확률. 0.2 = 20%. 맞으면 보스가 회복하며 즉시 시전하지 않습니다.
BoneMasterPhase2AIPatternSO.fakeCounterChance|페이크 카운터 확률 (0~1)|빨간색 카운터 확률. 0.2 = 20%. 피격 시 보스가 회복합니다.
";

    private const string WordData = @"
feedback=연출;intensity=강도;low=낮음;minimum=최소;motion=움직임;shape=형태;sounds=소리;trail=잔광;water=물;with=함께;
simultaneous=동시;bursts=효과;burst=효과;motes=빛조각;edges=가장자리;cue=효과번호;recipe=효과설정;recipes=효과설정;density=입자밀도;reach=거리;drift=이동;distortion=굴절;surface=지면;dirt=흙;stone=돌;foliage=풀;ambient=환경;catalog=전체설정;tiers=단계;variation=변화;material=재질;touch=접근;slow=감속;lines=선;bag=가방;kind=종류;simultaneous=동시;skills=스킬;combat=전투;
Upward=위쪽;Downward=아래쪽;
Alpha=불투명도;Immunity=내성;Impact=타격;Refresh=갱신;Return=복귀;Two=2;
Inset=안쪽 여유;Flee=후퇴;Arrival=도착;Reference=기준;Fuse=폭발 예고;Vulnerable=취약;Crossfade=교차 전환;Triggered=활성;Unavailable=사용 불가;
spark=빛 파편;sparks=빛 파편;travel=이동;duration=지속 시간;color=색상;prefab=프리팹;text=텍스트;time=시간;hit=타격;room=방;count=개수;speed=속도;telegraph=공격 예고;radius=반경;max=최대;state=상태;attack=공격;icon=아이콘;entity=개체;damage=피해;charge=돌진;base=기본;name=이름;slam=내려찍기;dash=대시;per=매;minion=소환수;anim=애니메이션;data=데이터;reward=보상;offset=위치 보정;sweep=휩쓸기;label=표시 문구;fill=채움;box=상자;player=플레이어;effect=효과;on=발생 시;volume=음량;is=여부;multiplier=배율;ui=UI;counter=카운터;sprite=스프라이트;panel=패널;spawn=생성;windup=준비 시간;dir=방향;type=종류;arrow=화살표;delay=지연;item=아이템;basic=평타;thrust=찌르기;pattern=패턴;curve=곡선;interval=간격;normal=일반;range=범위;leap=도약;debug=디버그;elite=엘리트;hp=체력;target=대상;armor=아머;super=슈퍼;execution=집행;description=설명;shop=상점;map=맵;current=현재;scale=배율;force=강제;skill=스킬;enhance=강화;level=단계;guard=가드;movement=이동;phase=단계;to=대상;layer=레이어;wall=벽;bonus=추가량;root=루트;width=너비;gauge=게이지;move=이동;line=선;image=이미지;door=문;sound=소리;slot=슬롯;angle=각도;distance=거리;origin=시작점;start=시작;source=원본;show=표시;cooldown=재사용 대기;default=기본값;enemy=적;size=크기;manager=관리자;button=버튼;tilemap=타일맵;custom=사용자 지정;use=사용;step=전진;chance=확률;break=파괴;outline=외곽선;clip=클립;min=최소;right=오른쪽;crit=치명타;explosion=폭발;fade=페이드;floor=층;visual=외형;title=제목;point=지점;fake=페이크;amount=양;end=종료;flash=섬광;lead=선행 시간;augment=증강;sfx=효과음;wave=파동;gold=골드;starting=시작 시;global=전체;clips=클립 목록;parry=패링;push=밀기;strength=강도;threshold=기준값;fallback=대체값;info=정보;close=근거리;recovery=회복;check=검사;heal=회복;boss=보스;combat=전투;id=ID;a=A;b=B;mini=짧은;pursuit=추격;seconds=시간;summon=소환;pool=풀;cursor=커서;shadow=그림자;hud=HUD;terrain=지형;projectile=투사체;entries=항목;dictionary=사전;object=오브젝트;card=카드;half=절반;length=길이;cast=시전;margin=여백;aim=조준;center=중앙;shake=흔들림;tint=덧칠 색;material=재질;slots=슬롯 목록;scene=씬;stab=찌르기;burst=순간 가속;override=덮어쓰기;equipment=장비;cost=비용;status=상태이상;out=사라짐;hold=유지;ally=아군;animator=애니메이터;tutorial=튜토리얼;rooms=방 목록;tile=타일;from=시작;active=활성;full=최대;punch=튀는 동작;fire=발사;spike=가시;indicator=인디케이터;direction=방향;timer=경과 시간;frame=프레임;tier=등급;gap=간격;turn=방향 전환;circle=원형;real=진짜;success=성공;groggy=그로기;jump=점프;part=부위;empty=없음;window=판정 시간;atk=공격력;pop=튀어오름;next=다음;vfx=시각 효과;keyword=키워드;registry=목록 데이터;cluster=몬스터 묶음;stack=중첩;enable=활성화;body=몸체;idle=대기;drop=드롭;n=N;open=열기;keep=유지;enemies=적 목록;value=값;canvas=캔버스;selection=선택;stage=단계;combo=콤보;reset=초기화;r=R;spacing=간격;renderer=렌더러;ground=바닥;items=아이템 목록;life=수명;main=메인;total=전체;overlay=덧그림;detail=상세;shot=발사;plate=판;loading=로딩;include=포함;bombardment=폭격;stun=기절;transform=트랜스폼;disable=비활성화;camera=카메라;rewards=보상 목록;stat=능력치;remaining=남은;x=X;y=Y;lock=고정;post=종료 후;reduction=감소;ratio=비율;pause=멈춤;ring=고리;lines=선 목록;kind=종류;portal=포탈;clear=클리어;reactions=신호별 반응;signal=신호;magic=마법;has=보유;blood=혈액;debuff=디버프;launch=발사;stop=정지;effects=효과 목록;world=월드;freeze=빙결;dialogue=대화;click=클릭;names=이름 목록;hitbox=타격 판정;upgrade=업그레이드;buff=강화 효과;percent=백분율;action=행동;inventory=인벤토리;pouch=주머니;candidate=후보;details=상세 내용;skip=건너뛰기;pulse=반동;evolve=진화;job=작업;grid=그리드;down=아래;generation=생성;light=약한;hide=숨기기;element=항목;zone=영역;opened=열림;so=SO;bgm=배경음;change=변경;parent=부모;scroll=스크롤;sort=정렬;group=그룹;container=컨테이너;template=원본;stats=능력치;amplitude=진폭;dot=지속 피해;warning=경고;arrows=화살표 목록;cards=카드 목록;no=없음;team=진영;detect=감지;last=마지막;finish=마무리;tick=주기;filling=채우는 중;particle=파티클;frozen=빙결 상태;style=방식;walk=걷기;mid=중거리;engage=공격 개시;chase=추격;limit=제한;warn=경고;lane=직선;none=없음;fall=낙하;tele=텔레포트;transition=전환;invincible=무적;bounds=영역;between=사이;attempts=시도 횟수;appear=등장;res=결과;thickness=두께;pips=표시 구슬;broken=파괴 상태;controller=컨트롤러;up=위;smooth=부드러움;left=왼쪽;evasion=회피율;accuracy=명중률;cdr=재사용 대기 감소;increase=증가;freezing=빙결;pulses=동작별 변형;head=머리;perfect=퍼펙트;ai=AI;table=표;bleed=출혈;poison=중독;hitstun=경직;play=재생;once=한 번만;complete=완료;chars=글자;second=초;speaker=화자;pixels=픽셀;input=입력;split=분할;slow=느림;ready=준비;special=특수;expand=확장;forced=강제;wait=대기;bubble=말풍선;reapply=재적용;rarity=등급;hand=손;weakness=약점;apply=적용;trigger=트리거;goal=목표;visible=보임;notice=알림;config=설정;mouse=마우스;test=테스트;mode=모드;auto=자동;game=게임;disassemble=분해;position=위치;localized=번역된;treasures=보물 목록;equipments=장비 목록;clicks=클릭 목록;anchor=기준점;required=필요;minions=소환수 목록;gain=획득;equipped=장착;exit=나가기;finisher=마무리 공격;steps=단계;waves=파동;corridor=복도;log=로그;unsteppable=이동 불가 바닥;needed=필요;depth=깊이;dist=거리;col=색상;caster=시전자;squash=눌림;e=E;face=바라보기;cell=칸;pos=위치;popup=팝업;sorting=표시 정렬;menu=메뉴;slider=슬라이더;index=순번;hurt=피격;pitch=음 높이;dodge=회피;fail=실패;invincibility=무적;interact=상호작용;silhouette=실루엣;layers=레이어 목록;regen=자연 회복;saved=저장된;passive=패시브;shield=보호막;content=내용;desc=설명;visited=방문한;icons=아이콘 목록;price=가격;buy=구매;summary=요약;brightness=밝기;jitter=흩어짐;rainbow=무지개;background=배경;priority=우선순위;unscaled=일시정지 무시;tag=태그;snap=즉시 맞춤;ramp=변화;fan=부채꼴;spread=퍼짐;archer=궁수;loop=반복;death=사망;colliders=충돌체 목록;behaviours=컴포넌트 목록;pooling=풀 재사용;flavor=분위기;risk=위험;guarantee=확정;p=P;allow=허용;penalties=불이익;my=자신;opponent=상대;destroy=제거;continuous=지속;rate=비율;receiver=수신 대상;smog=연무;foot=발;startup=시작;ratios=비율 목록;incoming=받는;helmet=투구;pauldron=견갑;chest=상자;colors=색상 목록;motions=동작;strike=타격;spin=회전;cycle=주기;volley=동시 발사;followup=후속;cone=부채꼴;mask=마스크;filled=채워진;result=결과;countered=카운터 성공;hijacked=중단;elapsed=경과;catch=추격;specific=지정;aiming=조준;lean=기울기;weight=가중치;cur=현재;dead=사망;spd=속도;def=방어;luck=행운;mult=배율;set=설정;done=완료;actions=행동 목록;missed=빗나간;terminal=표시 연결;rb=리지드바디;iron=강철;mountain=산;stacks=중첩;off=꺼짐;buffer=버퍼;stuck=끼임;pixel=픽셀;tooltip=설명 도움말;footer=하단;resource=리소스;path=경로;ghost=잔상;pip=표시 구슬;fills=채움 목록;growth=성장;vulnerable=취약;csv=CSV;files=파일 목록;locale=언어;entry=항목;skips=건너뛰기;typing=타이핑;highlight=강조;block=차단;every=매;key=키;placeholder=대체 표시;upright=세워두기;child=자식;dungeon=던전;decel=감속;deceleration=감속;font=글꼴;match=맞추기;overshoot=초과 전진;pre=직전;melee=근접;air=공중;found=찾은;maxed=최대 강화;clusters=몬스터 묶음 목록;outer=바깥;boundary=경계;defense=방어;variant=변형;dedicated=전용;psi=PSI;inv=무적;condition=조건;animation=애니메이션;passives=패시브 목록;options=선택지;only=전용;multipliers=배율 목록;hits=타수;contact=접촉;repeat=반복;exploits=공략;serendipity=뜻밖의 행운;guaranteed=확정;proc=발동;in=진입;deactivate=비활성화;numbers=숫자;vec=벡터;float=실수;subscribed=구독;globalvolume=전체 음량;offscreen=화면 밖;generator=생성기;battle=전투;unlimited=무제한;enhancements=강화 횟수;arena=전투장;over=초과;align=맞춤;recycle=재활용;datas=데이터 목록;abilities=능력 목록;owner=소유자;comparison=비교;replace=교체;single=단일;confirm=확인;lifetime=수명;slash=베기;streaks=선 파편;arcs=원형 파동;echoes=지연 잔상;echo=지연 잔상;expansion=확장;emission=발광;opacity=불투명도;quantities=수량 목록;format=형식;treasure=보물;shattered=파편;quantity=수량;copies=복제 수;available=사용 가능;as=용도;load=불러오기;modifier=보정;evolutions=진화 선택지;evolved=진화 후;form=형태;former=이전;asset=에셋;unit=유닛;isaac=아이작식;placement=배치;randomly=무작위;inward=안쪽;outward=바깥쪽;spreading=퍼뜨리기;velocity=속도;physics=물리;iteration=반복;straight=직선;avoid=회피;tuning=튜닝;bosses=보스 목록;encounter=전투;dump=상세 출력;timings=시간 목록;came=이전;messages=문구 목록;types=종류 목록;reached=도달;unreached=미도달;third=3타;fin=마무리;charges=충전 횟수;recharge=충전;mod=보정;forward=앞쪽;closest=가장 가까운;invoked=호출;zones=영역 목록;at=시점;pushes=밀기 횟수;internal=내부;causes=유발;knockback=밀치기;choices=선택지;span=구간;fired=발사;sequence=재생 순서;tf=트랜스폼;phases=단계 목록;texture=텍스처;hotspot=커서 기준점;hover=마우스 올림;detection=감지;scan=탐색;nearest=가장 가까운;stride=간격;system=시스템;enabled=활성화;edge=가장자리;order=순서;settings=설정;language=언어;dropdown=드롭다운;num=개수;blink=깜빡임;interactable=상호작용 대상;can=가능;acceleration=가속;occluder=가림막;message=문구;common=공통;totals=전체 값;copy=복사;buttons=버튼 목록;inactive=비활성;rotate=회전;sector=부채꼴;display=표시;used=사용된;anchors=기준점 목록;revealed=발견한;minimap=미니맵;cleared=클리어한;been=이력;objects=오브젝트 목록;save=저장;version=버전;option=설정;stock=재고;rt=렉트 트랜스폼;npc=NPC;mover=이동체;header=상단;linked=연결된;new=새;old=이전;mixer=믹서;parabola=포물선;purchase=구매;keys=키 목록;physical=물리;fixed=고정;miss=빗나감;property=속성;emphasize=강조;mesh=메시;displaytime=표시 시간;ciri=치명타;frequency=빈도;saturation=채도;parse=해석;own=자신;degrees=각도;tracking=추적;inset=안쪽 여백;raycast=광선 검사;padding=안쪽 여백;fuse=점화;boom=폭발;activation=활성화;area=영역;quest=목표;rect=표시 영역;instead=대신;unvisited=미방문;overlap=겹침;marker=표식;tiles=타일 목록;sync=동기화;z=Z;rotation=회전;tr=트랜스폼;teleport=텔레포트;cam=카메라;delta=변화량;esc=ESC;closable=닫기 허용;vibrato=진동 횟수;screen=화면;back=뒤;sprites=스프라이트 목록;progress=진행률;selected=선택한;m=연결;rc=우클릭;deform=변형;health=체력;bar=바;dynamic=동적;precision=정밀도;pivot=피벗;critical=치명타;vignette=화면 가장자리;streak=선 파편;arc=원형 파동;amp=증폭;and=및;bomber=폭탄병;compare=비교;conversion=변환;descend=다음 층;early=초반;epic=영웅;flat=고정값;follow=추적;legendary=전설;magician=마법사;metamorphosis=변이;modal=모달;or=또는;overflow=초과분;phys=물리;priest=사제;pull=당기기;rare=희귀;recent=최근;shieldbearer=방패병;skeleton=해골;spearman=창병;stunned=기절; taken=받은;targeting=대상 조준;thief=도적;transitioning=전환 중;trap=함정;uniform=균일;warrior=전사;persist=유지;across=전환 후에도;scenes=씬;event=이벤트;portraits=초상화 목록;prefabs=프리팹 목록;blend=이전 반동 혼합;ability=능력;address=주소;attacker=공격자;badge=표시 배지;bypass=무시;category=분류;evolution=진화;expression=표정;ranged=원거리;redirected=방향 변경;throw=던지기;horizontal=가로;vertical=세로;elasticity=반동;amplification=증폭
";
}
