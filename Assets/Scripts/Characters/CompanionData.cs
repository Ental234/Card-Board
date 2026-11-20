using System.Collections.Generic;
using UnityEngine;

// 동료 강화 한 단계 (GDD 9장). 전부 비워 두면 자동 규칙: 최대 HP +20%(최소 +3), 행동 수치 +30%
[System.Serializable]
public class CompanionUpgrade
{
    public int maxHpBonus;
    public int attackBonus;
    [Range(0, 200)] public int patternPowerPercent;           // 행동의 피해·방어막·회복 수치 증가율
    public List<ActionPatternData> extraPatterns = new();     // 강화로 새로 얻는 행동

    public bool IsEmpty => maxHpBonus == 0 && attackBonus == 0 && patternPowerPercent == 0
                        && (extraPatterns == null || extraPatterns.Count == 0);
}

[CreateAssetMenu(fileName = "NewCompanion", menuName = "Game/Companion Data")]
public class CompanionData : ScriptableObject
{
    [Header("기본 정보")]
    public string companionName;
    public Sprite portrait;
    [TextArea]
    public string description;

    [Header("기초 스탯")]
    public int maxHp;
    public int baseAttack;

    [Header("포지션")]
    [Range(1, 4)]
    public int preferredSlot = 2;  // 전투 배치 시 우선 슬롯 (SlotSystem이 빈 슬롯 없으면 조정)

    // 자리가 고정된 동료 (고정포대·설치물 등).
    // 전투 행동은 정상이지만 이동·자리 교체 대상이 되지 않는다.
    public bool isImmobile;

    [Header("행동 패턴")]
    // 동료는 배치만 하면 자기 타이밍에 알아서 싸운다 (손패에 카드를 보태지 않는다).
    public List<ActionPatternData> patterns = new();

    [Header("강화 (비워 두면 자동 규칙)")]
    public CompanionUpgrade upgrade1;
    public CompanionUpgrade upgrade2;

    [Header("(구) 전투 카드 — 사용처 없음. 패턴 이관 후 제거 예정")]
    public List<CardData> combatCards = new();
}
