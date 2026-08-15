using System.Collections.Generic;
using UnityEngine;

// 전투 슬롯에 들어가는 모든 유닛의 기반 MonoBehaviour
// CombatStats를 소유하고 슬롯 위치·진영을 관리
// 상속: PlayerCharacter / CompanionEntity / BossEntity / MinionEntity
public class CombatEntity : MonoBehaviour
{
    // ── Stats ──────────────────────────────────────────

    protected readonly CombatStats stats = new();
    public CombatStats Stats => stats;

    // ── 식별 정보 ──────────────────────────────────────

    public string EntityName { get; protected set; }
    public Sprite Portrait   { get; protected set; }

    // ── 슬롯 & 진영 ────────────────────────────────────

    public int  CurrentSlot  { get; private set; }    // 1~4, 0 = 미배치
    public bool IsPlayerSide { get; protected set; }  // true = 플레이어 진영

    // ── 상태 플래그 ────────────────────────────────────

    public bool IsActive => stats.IsAlive;
    public bool HasTaunt => stats.HasStatus(StatusEffect.Taunt);

    // 전투 행동(공격·스킬) 가능 여부
    public bool CanAct => stats.IsAlive && !stats.HasStatus(StatusEffect.Stun);

    // ── 이동 판정 ──────────────────────────────────────
    //
    // 이동은 전투 행동과 별개다. 고정포대처럼 "때릴 수는 있지만 못 움직이는" 유닛이 있고,
    // 반대로 쓰러진 동료는 스스로 못 움직이지만 다른 아군이 밀어내 치울 수는 있다.

    // 자리가 고정된 유닛 (고정포대·설치물 등)
    public bool IsImmobile { get; protected set; }

    // 스스로 이동할 수 있는가
    public bool CanMove => stats.IsAlive && !IsImmobile;

    // 다른 아군이 자리를 바꿔 밀어낼 수 있는가 (쓰러진 유닛도 치울 수 있다)
    public bool CanBeSwapped => !IsImmobile;

    // ── 초기화 ─────────────────────────────────────────

    protected void InitializeBase(int maxHp, int baseAttack, int maxActionPoints,
                                  string entityName, Sprite portrait)
    {
        EntityName = entityName;
        Portrait   = portrait;
        stats.Initialize(maxHp, baseAttack, maxActionPoints);
        stats.OnKnockedOut += HandleKnockedOut;
    }

    // ── 슬롯 배치 ──────────────────────────────────────

    public void SetSlot(int slot) => CurrentSlot = slot;

    // ── Stats 위임 ─────────────────────────────────────

    // CombatManager가 엔티티에 직접 접근할 때 CombatStats를 노출하지 않고 사용
    public void TakeDamage(int amount)                          => stats.TakeDamage(amount);
    public void LoseHp(int amount)                              => stats.LoseHp(amount);
    public void Heal(int amount)                                => stats.Heal(amount);
    public void AddShield(int amount)                           => stats.AddShield(amount);
    public void ClearShield()                                   => stats.ClearShield();
    public void ApplyStatus(StatusEffect s, int amount, int turns) => stats.ApplyStatus(s, amount, turns);
    public void ProcessStatusEffects()                          => stats.ProcessStatusEffects();
    public void RestoreActionPoints()                           => stats.RestoreActionPoints();
    public bool SpendActionPoint(int cost = 1)                  => stats.SpendActionPoint(cost);
    public int  CalculateAttack(int baseDamage)                 => stats.CalculateAttack(baseDamage);

    // ── 행동 패턴 ──────────────────────────────────────
    //
    // 패턴 SO는 여러 유닛이 공유하는 에셋이라 런타임 상태를 담을 수 없다.
    // 쿨다운·잔여 사용 횟수는 반드시 엔티티가 들고 있어야 서로 엉키지 않는다.
    //
    // 플레이어는 패턴을 갖지 않는다 (카드 + 슬롯 조작으로 싸운다).

    private class PatternState
    {
        public int cooldownLeft;
        public int usesLeft;
    }

    private readonly List<ActionPatternData> patterns = new();
    private readonly Dictionary<ActionPatternData, PatternState> patternStates = new();

    public IReadOnlyList<ActionPatternData> Patterns => patterns;

    public void SetPatterns(IEnumerable<ActionPatternData> source)
    {
        patterns.Clear();
        if (source != null)
            foreach (var p in source)
                if (p != null && !patterns.Contains(p)) patterns.Add(p);

        ResetPatternState();
    }

    // 전투 시작·종료 시 호출.
    // 쿨다운과 잔여 횟수만 초기화하고 HP·상태이상은 건드리지 않는다 —
    // 동료는 상처를 안고 다음 전투로 넘어가지만 스킬은 매 전투 새로 시작한다.
    public void ResetPatternState()
    {
        patternStates.Clear();
        foreach (var p in patterns)
            patternStates[p] = new PatternState
            {
                cooldownLeft = ToWaitTurns(p.initialCooldown),
                usesLeft     = p.maxUsesPerCombat > 0 ? p.maxUsesPerCombat : int.MaxValue,
            };

        ClearIntent();
    }

    // "쉬는 턴 수"를 내부 카운터로 바꾼다.
    //
    // 쿨다운 감소(TickPatternCooldowns)가 발동 검사보다 먼저 도는 턴 구조라,
    // 값을 그대로 넣으면 세워둔 턴 수보다 한 턴 일찍 발동한다 (cooldown 1이 매 턴 발동이 된다).
    // +1을 얹어 "cooldown N = 정확히 N턴 쉰다"를 지킨다.
    private static int ToWaitTurns(int turns) => turns > 0 ? turns + 1 : 0;

    public bool IsPatternReady(ActionPatternData pattern)
    {
        if (pattern == null || !patternStates.TryGetValue(pattern, out var s)) return false;
        return s.cooldownLeft <= 0 && s.usesLeft > 0;
    }

    public void MarkPatternUsed(ActionPatternData pattern)
    {
        if (pattern == null || !patternStates.TryGetValue(pattern, out var s)) return;

        s.cooldownLeft = ToWaitTurns(pattern.cooldown);
        if (s.usesLeft != int.MaxValue) s.usesLeft--;
    }

    // 라운드마다 1씩 감소.
    //
    // OnTurnStart에 두면 안 된다 — 적의 인텐트는 라운드 시작(플레이어 턴 전)에 정해지는데
    // 적의 OnTurnStart는 그보다 늦게 오므로, 쿨다운이 판정보다 한 박자 늦어 적만 한 라운드씩
    // 더 쉬게 된다. 그래서 CombatManager가 "그 쿨다운을 실제로 보는 시점" 직전에 직접 호출한다.
    public void TickPatternCooldowns()
    {
        foreach (var s in patternStates.Values)
            if (s.cooldownLeft > 0) s.cooldownLeft--;
    }

    // ── 인텐트 (적이 다음 턴에 할 행동 예고) ───────────
    //
    // 동료는 즉시 실행이라 인텐트를 쓰지 않는다.

    public EnemyIntent CurrentIntent { get; private set; }

    public void SetIntent(EnemyIntent intent) => CurrentIntent = intent;
    public void ClearIntent()                 => CurrentIntent = null;

    // ── 턴 흐름 ────────────────────────────────────────

    // CombatManager가 각 유닛 턴 시작 시 호출
    // 서브클래스에서 override 시 base.OnTurnStart() 먼저 호출
    public virtual void OnTurnStart()
    {
        stats.ClearShield();
        stats.ProcessStatusEffects();
        stats.RestoreActionPoints();
    }

    public virtual void OnTurnEnd() { }

    // ── KnockOut ───────────────────────────────────────

    private void HandleKnockedOut() => OnKnockedOut();

    // 서브클래스별 처리
    //  PlayerCharacter → 런 종료
    //  CompanionEntity → 쓰러짐 (슬롯 유지, 전투 종료 후 1HP 복귀)
    //  BossEntity      → 스테이지 클리어
    //  MinionEntity    → 슬롯 제거
    protected virtual void OnKnockedOut() { }

    protected virtual void OnDestroy()
    {
        stats.OnKnockedOut -= HandleKnockedOut;
    }
}
