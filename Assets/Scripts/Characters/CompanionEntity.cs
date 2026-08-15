using System;
using System.Collections.Generic;
using UnityEngine;

// 플레이어 진영 슬롯 2~4를 채우는 동료 유닛
//
// 용어 주의: 여기서 말하는 '쓰러짐'은 HP=0 상태이고,
//           상태이상 '기절'(StatusEffect.Stun)과는 다른 것이다.
//   · 쓰러짐(HP 0) — 해당 전투 동안 행동 불가, 스스로 이동 불가, 전투 종료 후 1HP 복귀 (영구 사망 없음)
//   · 기절(Stun)   — 행동만 봉인되고 이동은 가능
public class CompanionEntity : CombatEntity
{
    public event Action<CompanionEntity> OnKnockedOutInCombat; // CombatManager가 구독 → 쓰러짐 처리
    public event Action<CompanionEntity> OnRevived;            // 전투 종료 후 복귀 알림

    public bool IsKnockedOutThisCombat { get; private set; }

    private List<CardData> companionCards = new();
    public IReadOnlyList<CardData> CompanionCards => companionCards;

    // ── 초기화 ──────────────────────────────────────────

    public void Initialize(CompanionData data)
    {
        IsPlayerSide = true;

        InitializeBase(
            maxHp:           data.maxHp,
            baseAttack:      data.baseAttack,
            maxActionPoints: 0,              // 동료는 행동력 시스템 미사용
            entityName:      data.companionName,
            portrait:        data.portrait
        );

        SetSlot(data.preferredSlot);
        IsImmobile = data.isImmobile;   // 고정포대형 동료

        companionCards = new List<CardData>(data.combatCards);
        SetPatterns(data.patterns);
        IsKnockedOutThisCombat = false;
    }

    // ── 전투 시작/종료 ──────────────────────────────────

    public void OnCombatStart()
    {
        IsKnockedOutThisCombat = false;

        // 쿨다운·잔여 사용 횟수는 전투마다 초기화한다.
        // HP는 여기서 건드리지 않는다 — 동료는 상처를 안고 다음 전투로 넘어간다.
        ResetPatternState();
    }

    // CombatManager가 전투 종료 후 호출 → 1HP 복귀
    public void OnCombatEnd()
    {
        if (!IsKnockedOutThisCombat) return;

        stats.Heal(1);  // CombatStats.Heal()이 최소 1 보장 (Max(0,...) 이므로 직접 heal)
        IsKnockedOutThisCombat = false;
        OnRevived?.Invoke(this);
    }

    // ── 턴 흐름 ────────────────────────────────────────

    public override void OnTurnStart()
    {
        // 쓰러진 동안은 base를 타지 않으므로 패턴 쿨다운도 돌지 않는다.
        // 의도한 동작이다 — 쓰러져 있는 사이에 스킬이 저절로 준비되면 이상하다.
        if (IsKnockedOutThisCombat) return;
        base.OnTurnStart();
    }

    // ── KnockOut ────────────────────────────────────────

    protected override void OnKnockedOut()
    {
        IsKnockedOutThisCombat = true;
        OnKnockedOutInCombat?.Invoke(this);
        // 슬롯은 유지 — 쓰러진 채로 자리를 지킨다.
        // 스스로 못 움직이지만 다른 아군이 자리를 바꿔 치울 수는 있다 (CanBeSwapped)
    }
}
