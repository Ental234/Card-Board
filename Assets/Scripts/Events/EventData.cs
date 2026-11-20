using System;
using System.Collections.Generic;
using UnityEngine;

public enum EventEffectType
{
    HpChange,        // value: HP 증감 (음수 가능)
    MaxHpChange,     // value: 최대 HP 증감 (추후 CombatStats 확장 시 연결)
    GoldChange,      // value: 골드 증감
    AddCard,         // 덱에 카드 추가
    AddCurseCard,    // 저주 카드 추가 (AddCard와 동일 동작, 시각적 구분용)
    RemoveCard,      // 덱에서 카드 1장 제거 — UI가 플레이어 선택 처리
    GainRelic,       // 렐릭 획득
    GainCompanion,   // 동료 획득
    TeleportToBoss,  // 보스 위치 순간이동 → 강제 인카운터
    Nothing,
    UpgradeCard,     // value 1 = 일반 강화(카드·동료 +1) / 2 = 특별 강화(카드 +2, 무작위 페널티) — 끝에 덧붙인다(직렬화 번호)
}

[Serializable]
public class EventEffect
{
    public EventEffectType type;
    public int             value;
    public CardData        card;
    public RelicData       relic;
    public CompanionData   companion;
}

[Serializable]
public class EventChoice
{
    public string            label;
    [TextArea]
    public string            effectDescription;
    public List<EventEffect> effects    = new();
    public bool              requiresGold;  // true: 선택 전 goldCost 확인
    public int               goldCost;
}

[CreateAssetMenu(fileName = "NewEvent", menuName = "Game/Event Data")]
public class EventData : ScriptableObject
{
    public string            eventName;
    [TextArea]
    public string            description;
    public List<EventChoice> choices = new();
}
