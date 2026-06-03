import json

translations = {
    "UniqueEffect_AgingHunter": "필드 위의 총 노화 스택 100당 아군의 공격 속도가 10% 증가합니다.",
    "UniqueEffect_DimVision": "노화 스택이 50 이상인 적의 공격 빗나감 확률이 25% 증가합니다.",
    "UniqueEffect_Goryeojang": "가장 노화 스택이 높은 적 주변에 20% 둔화 및 노화 장판을 생성합니다.",
    "UniqueEffect_NoCountryForOldMen": "최대 노화 스택이 100으로 증가합니다. 최대 스택 도달 시 대상을 즉사시킵니다 (보스 제외).",
    "UniqueEffect_AimedStrike": "투척 범위 중심에 있는 적에게 20%의 추가 피해를 입힙니다.",
    "UniqueEffect_HuntersHerding": "범위 내에 적중한 적 하나당 투척 피해가 5% 증가합니다.",
    "UniqueEffect_ReflectingNature": "공격 빗나감 시 체력이 50% 미만이면 공격력이 15% 증가하고, 50% 이상이면 공격 속도가 15% 증가합니다. (보이지 않는 빗나감 필요)",
    "UniqueEffect_SpreadShot": "궁수의 투척 범위 반경이 20% 증가합니다.",
    "UniqueEffect_SupportFire": "궁수의 광역 투척 피해가 15% 증가합니다.",
    "UniqueEffect_TensionPower": "기본 공격 속도가 25% 감소하지만 기본 공격 피해가 25% 증가합니다.",
    "UniqueEffect_UnseenMiss": "트리거로 작용합니다: 기본 공격 빗나감 시 다음 버프가 장전됩니다. (자연의 투영 필요)",
    "UniqueEffect_WindDirection": "비행 거리에 비례하여 투척 피해가 1%에서 33%까지 증가합니다.",
    "UniqueEffect_AmIExplodingToo": "희생자가 20%의 추가 피해를 받는 약화 디버프를 부여합니다.",
    "UniqueEffect_BloodArmor": "폭발 시 피의 폭발 스택의 두 배만큼 아군에게 보호막을 부여합니다.",
    "UniqueEffect_ExplodingFlesh": "폭발 피해의 1/4을 주변 적에게 피의 폭발 스택으로 부여합니다.",
    "UniqueEffect_GoreParty": "폭발 반경 내의 아군을 피의 폭발 스택 수만큼 회복시킵니다.",
    "UniqueEffect_ImprovisedExplosive": "피의 폭발 피해가 10% 증가합니다.",
    "UniqueEffect_MeltingCorpse": "폭발 후 5초간 유지되는 독 장판을 생성합니다.",
    "UniqueEffect_MutualDestruction": "폭발 전 1.5배 반경 내의 적들을 끌어당깁니다.",
    "UniqueEffect_AbsoluteZero": "빙결 시 반경 내에 50의 한기 스택을 부여합니다 (방당 1회).",
    "UniqueEffect_AchingBones": "빙결 시 한기 스택이 10에서 시작합니다. 빙결 중에는 스택이 쌓이지 않습니다.",
    "UniqueEffect_BitingWind": "주변 적들에게 1의 한기 스택을 부여합니다.",
    "UniqueEffect_ColdBloodedHunter": "한기가 묻은 적에게 10%의 추가 둔화를 부여합니다.",
    "UniqueEffect_Frostbreaker": "한기가 묻은 적에게 5%의 추가 피해를 입힙니다.",
    "UniqueEffect_ShatterIcicle": "투척 시 빙결을 해제하고 50%의 추가 피해를 입힙니다.",
    "UniqueEffect_SlowlyFreezingFlower": "최대 한기 스택과 빙결 임계값이 10 증가합니다 (총 30).",
    "UniqueEffect_DoubleCorrosion": "부식 시너지의 피해 증폭량이 10% 상승합니다.",
    "UniqueEffect_PriestsCantAttack": "부식 시너지가 활성화되어 있을 때 아군이 받는 치유량이 20% 증가합니다.",
    "UniqueEffect_RustedArmor": "부식된 적에게서 5번 피격될 때마다 부식된 적에게 최대 체력의 5% 고정 피해를 반사합니다.",
    "UniqueEffect_WeaponCorrosion": "부식된 적의 공격력이 10% 감소합니다.",
    "UniqueEffect_Fear": "대상을 처형할 때 1초 동안 주변 적들에게 공포를 부여합니다.",
    "UniqueEffect_Guillotine": "처형 임계값이 10% 완화됩니다.",
    "UniqueEffect_GreenFluid": "독 피해 틱마다 포션을 생성합니다.",
    "UniqueEffect_LethalDose": "독 피해 틱 간격을 절반으로 줄입니다.",
    "UniqueEffect_LethalPoison": "대상의 독 스택 수만큼 투척 피해를 추가합니다.",
    "UniqueEffect_PoisonContagion": "사망 시 주변 적들에게 독을 전염시킵니다.",
    "UniqueEffect_PoisonFlask": "기본 투척 독 스택 부여량이 1 증가합니다.",
    "UniqueEffect_PoisonFootprint": "독 발자국 장판을 생성합니다. 아군의 이동 속도가 15% 증가합니다.",
    "UniqueEffect_PoisonHost": "3초마다 주변 반경에 독 스택을 퍼뜨립니다.",
    "UniqueEffect_WoundInfection": "기본 공격 시 독 피해 틱 간격이 감소합니다.",
    "UniqueEffect_AuraOfOverwhelming": "5초마다 방패병 최대 체력의 4%만큼 주변 적들에게 피해를 주고 가한 피해의 120%만큼 회복합니다.",
    "UniqueEffect_AuraOfPatience": "5초마다 아군에게 방패병 최대 체력의 18%에 해당하는 보호막을 부여합니다.",
    "UniqueEffect_HeavyArmor": "단일 투척 적중 시 보호막 수치의 14%에 해당하는 물리 피해를 추가로 입힙니다.",
    "UniqueEffect_ShieldsWillClash": "방패병의 모든 스탯이 10% 증가합니다.\\n방에 진입 시 10초 동안 아군의 공격력이 8% 증가합니다.",
    "UniqueEffect_ShieldsWillCourage": "방패병의 모든 스탯이 10% 증가합니다.\\n방에 진입 시 10초 동안 아군의 공격 속도가 12% 증가합니다.",
    "UniqueEffect_ShieldsWillWind": "방패병의 모든 스탯이 10% 증가합니다.\\n방에 진입 시 10초 동안 아군의 이동 속도가 14% 증가합니다.",
    "UniqueEffect_SturdyShield": "방에 진입 시 방패병에게 최대 체력의 50%에 해당하는 보호막을 부여합니다.",
    "UniqueEffect_ThornArmor": "기본 공격을 받을 때 공격자 현재 체력의 2%에 해당하는 고정 피해를 반사합니다.",
    "UniqueEffect_TwistedGround": "단일 투척 적중 시 보호막 수치의 20%에 해당하는 광역 피해를 입힙니다.",
    "UniqueEffect_IronMountain": "기본 공격 시 적을 넉백시킵니다. 적이 벽에 부딪히면 최대 체력의 12%를 피해로 입습니다.",
    "UniqueEffect_SpearSwiftness": "투척 착탄 시간을 33% 단축시킵니다.",
    "UniqueEffect_ThousandStabs": "창병의 기본 공격 피해가 3% 추가로 증가합니다.",
    "UniqueEffect_Vanguard": "돌진 거리가 증가합니다.",
    "UniqueEffect_CrushingPower": "적을 처형할 때 초과된 투척 피해만큼 전사를 회복시킵니다.",
    "UniqueEffect_FanaticRage": "기본 공격에 3%의 생명력 흡수를 부여합니다.",
    "UniqueEffect_TrackingEye": "같은 대상을 투척으로 반복해서 맞출 때 12%의 추가 피해를 입힙니다.",
    "UniqueEffect_WarriorBallistics1": "전사의 포물선 투척 피해가 10% 증가합니다.",
    "UniqueEffect_WarriorBallistics2": "전사의 직사 투척 피해가 10% 증가합니다.",
    "UniqueEffect_WarriorBallistics3": "전사의 단일 대상 투척 피해가 15% 증가합니다.",
    "UniqueEffect_WarriorsMedal": "전사의 모든 기본 스탯이 15% 증가합니다.",
    "UniqueEffect_Closer": "최대 차지 시간 제한이 5초로 고정됩니다. 풀 차지 시 투척 효과가 50% 증가합니다.",
    "UniqueEffect_ExperiencedPitcher": "차지 중 이동 속도 감소가 25%로 줄어듭니다.",
    "UniqueEffect_MagicPitchArirangBall": "차지 시간이 0.5초 감소하고, 강속구 효과가 40% 감소합니다.",
    "UniqueEffect_MagicPitchFireball": "요구 차지 시간 1초당 강속구 효과가 10% 증가합니다.",
    "UniqueEffect_SetPosition": "차지 시간이 0.1초 감소하고, 강속구 효과가 2% 감소합니다.",
    "UniqueEffect_Windup": "차지 시간이 0.5초 증가하고, 강속구 효과가 2% 증가합니다.",
    "UniqueEffect_BasicFitness": "최대 스태미나가 20 증가합니다.",
    "UniqueEffect_CatchBreath": "비전투 시 자연 스태미나 회복량이 증가합니다.",
    "UniqueEffect_EfficientThrow": "최대 스태미나가 40 감소하고, 투척 효과가 60% 증가합니다.",
    "UniqueEffect_EndlessVitality": "자연 스태미나 회복량이 증가합니다 (+0.5).",
    "UniqueEffect_HarvestOfDeath": "죽은 미니언 수에 비례하여 스태미나 회복량이 증가합니다.",
    "UniqueEffect_LimitBreak": "스태미나가 0 미만으로 떨어질 수 있습니다 (최대 -50). 음수일 때는 회복량이 절반이 됩니다.",
    "UniqueEffect_MasterOfRapidFire": "스태미나 소모가 7 감소하고, 투척 효과가 30% 감소합니다.",
    "UniqueEffect_OrderedBreath": "스태미나 소모가 3 감소합니다.",
    "UniqueEffect_OverflowingThrow": "스태미나 소모가 5 증가하고, 투척 효과가 25% 증가합니다.",
    "UniqueEffect_ThrowOverload": "스태미나 소모량 1당 투척 효과가 2% 증가합니다."
}

with open("untranslated.json", "r", encoding="utf-8") as f:
    data = json.load(f)

for item in data.get("items", []):
    key = item["key"]
    if key in translations:
        item["koreanText"] = translations[key]
    else:
        print(f"Warning: No translation for {key}")

with open("translated.json", "w", encoding="utf-8") as f:
    json.dump(data, f, indent=4, ensure_ascii=False)
