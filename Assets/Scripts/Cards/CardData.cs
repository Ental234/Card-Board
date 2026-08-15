using UnityEngine;

public enum CardType
{
    Combat,  // 전투 페이즈에서만 사용
    Board,   // 보드 페이즈(주사위 굴리기 전)에서만 사용
}

public enum RangeType
{
    Melee,   // 근접: 사용 슬롯 Front, 타겟 슬롯 적 Front
    Ranged,  // 원거리: 슬롯 제한 없음
    None,    // 버프·힐·포지션 이동 등 공격 범위 없는 카드
}

// 카드 한 가지 효과 단위
[System.Serializable]
public class CardEffect
{
    public EffectType   type;
    public int          value;     // 피해량, 회복량, 방어막 수치, 이동 칸 수 등
    public StatusEffect status;    // type == ApplyStatus 일 때만 사용
    public int          duration;  // 상태이상 지속 턴
}

[CreateAssetMenu(fileName = "NewCard", menuName = "Game/Card Data")]
public class CardData : ScriptableObject
{
    [Header("기본 정보")]
    public string cardName;
    [TextArea] public string description;
    public Sprite artwork;

    [Header("분류")]
    public CardType  cardType;
    public RangeType rangeType;
    public ClassTag  classTag;

    [Header("비용")]
    public int energyCost;  // 전투 카드: 에너지 소모. 보드 카드: 0이면 1회 소모 무료

    [Header("포지션 규칙 (전투 카드)")]
    public SlotMask useableSlots;  // 이 카드를 사용할 수 있는 플레이어 슬롯
    public SlotMask targetSlots;   // 타겟 가능한 적(또는 아군) 슬롯
    public bool     targetAlly;    // true = 아군 타겟 (힐·버프)
    public bool     isAoe;         // true = targetSlots 내 모든 유닛에 동시 적용

    // true = 타겟 선택 없이 시전자 자신에게 즉시 적용 (방어·버프 등)
    // 매 턴 쓰는 카드에서 선택지가 하나뿐인 클릭을 없앤다.
    // targetAlly / targetSlots / isAoe 보다 우선한다.
    public bool     targetSelf;

    [Header("효과")]
    public CardEffect[] effects;
}
