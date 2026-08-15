using UnityEngine;

// 동료·적 공용 행동 패턴
// "언제(timing) + 누구를(targetMode) + 무엇을(effects)"
//
// 효과 로직은 기존 CardEffect를 그대로 재사용한다 — 새로 짜지 않는다.
// 동료는 배치만 하면 자기 타이밍에 알아서 발동하고, 적도 같은 구조로 행동한다.
//
// ※ 이 SO에 런타임 상태(현재 쿨다운 등)를 절대 넣지 말 것.
//   하수인 여러 마리가 같은 에셋을 공유하므로 셋의 쿨다운이 엉킨다.
//   에디터에서는 플레이 종료 후에도 값이 남아 다음 실행에 새어 들어간다.
//   런타임 상태는 전부 CombatEntity가 들고 있다.
[CreateAssetMenu(fileName = "NewPattern", menuName = "Game/Action Pattern")]
public class ActionPatternData : ScriptableObject
{
    [Header("기본 정보")]
    public string patternName;
    [TextArea] public string description;
    public Sprite icon;  // 인텐트 UI에서 사용 (아직 미연결)

    [Header("발동 타이밍")]
    public TriggerTiming timing = TriggerTiming.TurnStart;

    [Header("발동 제한")]
    public int cooldown;           // 발동 후 쉬는 턴 수 (0 = 매 턴)
    public int initialCooldown;    // 전투 시작 후 첫 발동까지 대기 턴 수
    public int maxUsesPerCombat;   // 전투당 최대 사용 횟수 (0 = 무제한)
    public int priority;           // 적 인텐트 선택 우선순위 (높을수록 우선)

    [Header("추가 발동 조건 (0 / false = 조건 없음)")]
    [Range(0f, 1f)] public float hpBelowRatio;  // 시전자 HP가 이 비율 이하일 때만
    public int  minPhase;                       // 보스 페이즈 N 이상일 때만
    public bool requirePanic;                   // 보스 패닉 모드에서만

    [Header("시전 슬롯 제한")]
    // 주의: None = "제한 없음"이다 (CardData.useableSlots와 같은 규칙).
    //       아래 targetSlots의 None(= 후보 0개 = 불발)과 의미가 정반대이니 헷갈리지 말 것.
    public SlotMask casterSlots = SlotMask.None;

    [Header("타겟")]
    public TargetMode targetMode = TargetMode.Slots;

    // 주의: None = 후보 0개 = 불발. 거리 기준 모드에서도 1차 필터로 쓰인다.
    //       기본값을 All로 두는 이유가 이것 — 새 에셋을 만들자마자 불발하는 사고를 막는다.
    public SlotMask targetSlots = SlotMask.All;

    public bool targetAlly;                // true = 아군 대상 (치유·버프)
    public bool isAoe;                     // true = 후보 전원 타격
    public int  maxTargets = 1;            // isAoe가 false일 때 최대 대상 수
    public bool respectTaunt = true;       // false = 도발 무시 (후열 저격)
    public bool includeKnockedOut;         // true = 쓰러진 유닛도 후보 (소생)

    [Header("효과")]
    public CardEffect[] effects;           // 기존 카드 효과를 그대로 재사용

    [Header("특수")]
    // 효과를 전부 적용한 뒤 시전자가 스스로 쓰러진다 (자폭 하수인).
    // 방어막·취약 배수·피격 렐릭을 타지 않아야 하므로 LoseHp로 처리한다.
    public bool selfDestructAfterUse;
}
