using System;
using System.Collections.Generic;
using UnityEngine;

// 렐릭 발동 트리거
public enum RelicTrigger
{
    Passive,             // 항상 적용 (스탯 보정)
    OnCombatTurnStart,   // 전투 턴 시작 시
    OnCardPlayed,        // 카드 사용 시
    OnKillEnemy,         // 적 처치 시
    OnTakeDamage,        // 피해 받을 때
    OnBoardTurnStart,    // 보드 턴 시작 시
}

// 렐릭 효과 종류
public enum RelicEffectType
{
    DrawCards,          // N장 추가 드로우
    GainEnergy,         // N 에너지 획득
    GainShield,         // N 방어막 획득
    HealHp,             // N HP 회복
    GainGold,           // N 골드 획득
    IncreaseDamage,     // 기본 공격력 +N (Passive)
    IncreaseMaxHp,      // 최대 HP +N (Passive — 추후 CombatStats 확장 시 적용)
    IncreaseBoardDice,  // 주사위 +N (Passive, BoardPhaseManager 경유)
}

[Serializable]
public class RelicEffect
{
    public RelicTrigger    trigger;
    public RelicEffectType effectType;
    public int             value;
}

[CreateAssetMenu(fileName = "NewRelic", menuName = "Game/Relic Data")]
public class RelicData : ScriptableObject
{
    [Header("기본 정보")]
    public string            relicName;
    [TextArea]
    public string            description;
    public Sprite            icon;

    [Header("효과")]
    public List<RelicEffect> effects = new();
}
