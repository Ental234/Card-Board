using UnityEngine;

public enum CardType
{
    Combat,  // 전투 페이즈에서만 사용
    Board,   // 보드 페이즈(주사위 굴리기 전)에서만 사용
    Curse,   // 저주 — 전투 덱에 섞이는 나쁜 카드. 강화·일반 카드 제거 대상이 아니다 (3단계 4번)
}

// 카드가 덱에 머무는 기간. 번호는 직렬화 규칙대로 구간을 비워 둔다
public enum CardScope
{
    Run    = 0,   // 제거하기 전까지 덱에 남는다 (기본값 — 기존 카드는 전부 이것)
    Combat = 10,  // 다음 전투가 끝나면 덱에서 사라진다. 전투 밖에서 얻으면 그다음 전투까지 들고 간다
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

// 강화 한 단계의 정의 (GDD 9장 — 강화 데이터는 원본 에셋 안에 적는다)
// 전부 비워 두면 자동 규칙: 피해·방어막·회복 수치 +30% (최소 +2), 설명 속 숫자도 자동으로 바뀐다.
[System.Serializable]
public class CardUpgrade
{
    // 비어 있지 않으면 효과 목록 전체를 이것으로 바꾼다 — 수치 변경과 효과 추가 모두 여기서
    public CardEffect[] effects;
    public int          costDelta;               // -1이면 비용 1 감소
    [TextArea] public string description;        // 비워 두면 설명 속 숫자만 자동으로 바꾼다
}

// 특별 강화(+2를 한 번에)의 대가. 번호는 직렬화 규칙대로 구간을 비워 둔다
public enum CardPenalty
{
    None      = 0,
    CostUp    = 10,  // 비용 +1
    Exhaust   = 20,  // 쓰면 그 전투에서 소모 더미로
    FrontOnly = 30,  // 1·2번 슬롯에서만 사용
    AddCurse  = 40,  // 저주 카드 1장이 덱에 들어온다
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
    public Rarity    rarity;    // 추첨 비중·상점 가격·이름 색. 저주·물약도 필드는 있지만 저주는 추첨 풀에 없어 쓰이지 않는다

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

    [Header("소모")]
    public bool exhaust;   // true = 쓰면 버린 더미가 아니라 소모 더미로 간다 (그 전투 동안 돌아오지 않음)

    // ── 특수 규칙 ──────────────────────────────────────
    // 저주는 아래 속성을 **조합해서** 만든다 (2026-09-27 결정). 저주 전용 코드를 따로 두지 않는다.
    //   짐     = unplayable
    //   고통   = unplayable + turnEndInHandEffects(HP 상실)
    //   망설임 = 사용 가능(비용 1) + exhaust  — 에너지를 내고 이번 전투에서 치워 버리는 저주
    //   상처   = unplayable + scope Combat
    // 속성 자체는 저주 전용이 아니라서, 일반 카드에 붙여도 그대로 동작한다.
    [Header("특수 규칙")]
    public bool         unplayable;             // 사용 불가 — 손패 한 칸을 차지하는 죽은 패
    public CardEffect[] turnEndInHandEffects;   // 손패에 든 채 플레이어 턴이 끝나면 발동 (대상은 플레이어 자신)
    public CardScope    scope;                  // 덱에 머무는 기간

    [Header("강화 (비워 두면 자동 규칙)")]
    public CardUpgrade upgrade1;   // + 로 가는 단계
    public CardUpgrade upgrade2;   // ++ 로 가는 단계

    // ── 런타임 강화 복제본 정보 ──────────────────────────
    // 강화는 "그 한 장만" 복제해서 한다 (같은 카드 4장 중 1장만 강화되게). 복제본만 이 값을 가진다.
    // NonSerialized — 에셋에 저장되지 않고, 에디터 플레이 진입 직렬화에도 휘말리지 않는다.
    [System.NonSerialized] public int         upgradeLevel;
    [System.NonSerialized] public CardData    baseCard;
    [System.NonSerialized] public CardPenalty penalty;

    // "같은 카드인가" 판정은 이걸로 한다 — 강화된 타격도 원본은 타격이다
    public CardData Original => baseCard != null ? baseCard : this;

    public bool IsCurse => cardType == CardType.Curse;

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
