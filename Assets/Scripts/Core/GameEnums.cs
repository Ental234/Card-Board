using System;

// 전투 슬롯 비트마스크
// Slot1=0001  Slot2=0010  Slot3=0100  Slot4=1000
[Flags]
public enum SlotMask
{
    None  = 0,
    Slot1 = 1 << 0,
    Slot2 = 1 << 1,
    Slot3 = 1 << 2,
    Slot4 = 1 << 3,
    Front = Slot1 | Slot2,
    Back  = Slot3 | Slot4,
    All   = Slot1 | Slot2 | Slot3 | Slot4,
}

// 카드·캐릭터 직업 분류
public enum ClassTag
{
    Warrior,
    Mage,
    Rogue,
    Universal,  // 직업 무관 — 보상 풀·상점 모두 등장
}

// 전투 상태이상
public enum StatusEffect
{
    None,
    Bleed,       // 출혈: 매 턴 고정 피해
    Poison,      // 독: 매 턴 스택 피해
    Burn,        // 화상
    Stun,        // 기절: 1턴 행동 불가
    Taunt,       // 도발: 적 공격 강제 집중
    Weak,        // 약화: 공격력 감소
    Vulnerable,  // 취약: 받는 피해 증가
}

// 보드 노드 타입
public enum NodeType
{
    Start,    // 시작 칸 (고정)
    Empty,    // 빈 칸: 소소한 보상
    Monster,  // 일반 몬스터: 카드·골드 보상. 보스 착지 시 하수인 획득
    Elite,    // 정예 몬스터: 강한 전투, 렐릭 보상. 보스 착지 시 하수인 획득
    Event,    // 이벤트: 랜덤 선택지
    Shop,     // 상점: 카드·렐릭·포션·동료 구매
    Rest,     // 휴식: HP 회복 or 카드 업그레이드
    Treasure, // 보물: 렐릭·카드·동료 무료 획득
    Curse,    // 저주: 부정 효과
    Salary,   // 월급: 플레이어·보스 모두 골드 지급
}

// 카드 효과 종류 (전투 카드 + 보드 카드 공용)
public enum EffectType
{
    // 전투 카드
    Damage,        // 피해
    Heal,          // 회복
    Shield,        // 방어막
    ApplyStatus,   // 상태이상 부여
    MovePosition,  // 포지션 이동 (value: +전진 / -후퇴)
    DrawCard,      // 카드 드로우
    GainEnergy,    // 에너지 획득

    // 보드 카드
    BoardDiceBonus,   // 주사위 강화 (value = 추가값)
    BoardReroll,      // 주사위 재굴림 허용
    BoardReverseMove, // 역방향 이동
    BoardTeleport,    // 순간이동 (value = 목표 nodeIndex, -1이면 플레이어가 UI로 선택)
    BoardFreezeBoss,  // 속박의 사슬 (value = 봉쇄 턴 수)
    BoardRevealNodes, // 예언의 주사위 (value = 미리 볼 칸 수)
    BoardBlockMinion, // 하수인 차단 (1회)
}

// 행동 패턴 발동 타이밍 (동료·적 공용)
// 동료는 플레이어 턴 흐름에 얹히고, 적은 자기 턴에 행동한다.
// 두 방식을 한 열거형에 담아 실행 엔진을 공유한다 — 차이는 호출 지점에만 있다.
//
// ※ 번호를 반드시 명시할 것. Unity는 열거형을 정수로 직렬화하므로
//   중간에 값을 끼워 넣으면 이미 만든 패턴 에셋의 타이밍이 통째로 밀려 깨진다.
//   앞으로 타이밍을 세분화할 예정이라 구간마다 번호를 비워 뒀다.
public enum TriggerTiming
{
    None = 0,

    // ── 턴 흐름 (10번대) ──
    TurnStart = 10,  // 자기 진영 턴 시작 직후
    TurnEnd   = 11,  // 자기 진영 턴 종료 직전

    // ── 플레이어 행동 반응 (20번대) ──
    // 플레이어 본인의 행동에만 발화한다. 동료 스킬은 다른 동료를 깨우지 않는다.
    OnPlayerAttack  = 20,  // 피해(Damage) 효과가 든 카드를 쓴 직후
    OnPlayerDefend  = 21,  // 방어막(Shield) 효과가 든 카드를 쓴 직후
    OnPlayerDamaged = 22,  // 플레이어가 실제로 HP를 잃은 직후

    // ── 적 (50번대) ──
    OwnTurn = 50,  // 자기 턴에 행동 (보스·하수인 기본)
}

// 행동 패턴의 타겟 선정 방식
// Slots = 슬롯 기준, Nearest/Farthest = 거리 기준 (SlotSystem.GetDistance)
// 거리 기준 모드도 targetSlots로 1차 필터를 건 뒤 그 안에서 고른다
// → "적 후열 중 가장 가까운 놈" 같은 조합이 가능하다.
public enum TargetMode
{
    Slots    = 0,  // targetSlots에 해당하는 슬롯을 그대로 노린다
    Nearest  = 10, // 시전자와 가장 가까운 대상
    Farthest = 11, // 시전자와 가장 먼 대상 (후열 저격)
    LowestHp = 20, // 현재 HP가 가장 낮은 대상 (마무리·집중 치유)
    Self     = 30, // 시전자 자신 (targetAlly / targetSlots 무시)
    Random   = 40, // 후보 중 무작위
}

// 적 의도(인텐트) 표시 종류 — 아이콘·색을 고르는 데만 쓴다
// ※ TriggerTiming과 같은 이유로 번호를 명시한다 (에셋에 직렬화되지는 않지만 규칙을 통일)
public enum IntentKind
{
    Unknown      = 0,
    Attack       = 10,  // 단일 공격
    AttackAoe    = 11,  // 광역 공격
    Debuff       = 20,  // 적에게 상태이상
    Buff         = 30,  // 자기·아군 강화 (방어막 포함)
    Heal         = 31,
    Taunt        = 32,
    SelfDestruct = 40,  // 자폭 — 가장 눈에 띄어야 한다
}

// 게임 전체 페이즈 상태
// 순환: MainMenu → BoardPhase ⇄ CombatPhase → RunEnd → (메인 메뉴 복귀)
public enum GamePhase
{
    Idle,        // 초기화 직후 (아직 아무 화면도 결정되지 않음)
    MainMenu,    // 타이틀 화면 — 런 시작 대기
    BoardPhase,  // 보드 이동 페이즈
    CombatPhase, // 전투 페이즈
    RunEnd,      // 런 종료 (승패 결정 + 보상 화면)
}
