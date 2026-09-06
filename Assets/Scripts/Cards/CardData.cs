using UnityEngine;

public enum CardType
{
    Combat,  // 전투 페이즈에서만 사용
    Board,   // 보드 페이즈(주사위 굴리기 전)에서만 사용
}

// 카드가 어디까지 닿는가. 무기 종류가 아니라 **도달 범위**를 뜻한다 —
// 검을 휘두르는 회전베기가 진영 전체에 닿듯, 연출과 도달 범위는 별개다.
// 정확한 슬롯은 useableSlots / targetSlots가 정하며 둘은 교집합(AND)으로 함께 걸린다
// (CardData.EffectiveUseableSlots 참고).
//
// ※ Unity는 열거형을 정수로 직렬화한다. 번호가 곧 에셋에 저장된 값이므로
//    이름은 바꿔도 되지만 순서를 바꾸거나 중간에 끼워 넣으면 기존 카드가 조용히 밀린다.
public enum RangeType
{
    FrontLine = 0,  // 전열 교전 전용 — 시전·타겟 모두 Front (강타·돌진)
    AnySlot   = 1,  // 전 슬롯 도달 (타격·회전베기, 향후 마법사 카드)
    NonCombat = 2,  // 공격 범위 개념이 없는 카드 (버프·힐·보드 카드)
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

    // true = 플레이어가 고르지 않는다 (논타겟). targetMode가 대상을 정한다.
    // 적 패턴이 쓰는 것과 같은 TargetMode 열거형이라, 새 선정 방식을 만들 필요가 없다.
    //
    // ※ 별도 bool인 이유: TargetMode.Slots(0)은 패턴에서 이미 "전방 우선 자동 선택"을 뜻한다.
    //   기존 카드 에셋은 이 필드가 없어 0으로 역직렬화되므로, Slots에 "플레이어가 고른다"는
    //   두 번째 의미를 얹으면 강타·돌진이 조용히 자동 타겟으로 바뀐다.
    //   bool로 분리해야 기존 카드가 전부 autoTarget = false로 떨어져 동작이 유지된다.
    public bool       autoTarget;
    public TargetMode targetMode;

    // true = 타겟 선택 없이 시전자 자신에게 즉시 적용 (방어·버프 등)
    // 매 턴 쓰는 카드에서 선택지가 하나뿐인 클릭을 없앤다.
    // targetAlly / targetSlots / isAoe 보다 우선한다.
    public bool     targetSelf;

    [Header("효과")]
    public CardEffect[] effects;

    // ── 사거리 계산 ──────────────────────────────────────
    //
    // rangeType이 큰 틀을, 마스크가 정확한 슬롯을 정한다. 둘 다 통과해야 한다(AND).
    // 슬롯 판정을 하는 쪽은 반드시 아래 Effective* 를 쓸 것 —
    // 원본 필드를 직접 읽으면 rangeType이 다시 죽는다.
    //
    //   FrontLine → 시전 Front · 타겟 Front
    //   AnySlot   → 제한 없음 (마스크만 적용)
    //   NonCombat → 제한 없음 (공격 범위 개념이 없는 카드)
    //
    // 두 마스크의 None은 의미가 정반대라 계산도 다르다:
    //   useableSlots.None = "제한 없음" → rangeType 규칙만 남긴다
    //   targetSlots.None  = "후보 0개 = 불발" → 교집합에서 그대로 None으로 떨어진다
    //
    // 주의: FrontLine인데 useableSlots를 Back으로 두는 식의 모순된 조합은
    // 교집합이 None이 되어 그 카드가 조용히 사용 불가가 된다.

    private SlotMask RangeMask => rangeType == RangeType.FrontLine ? SlotMask.Front : SlotMask.All;

    public SlotMask EffectiveUseableSlots
        => useableSlots == SlotMask.None ? RangeMask : (RangeMask & useableSlots);

    public SlotMask EffectiveTargetSlots => RangeMask & targetSlots;
}
