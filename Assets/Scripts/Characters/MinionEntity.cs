using System;
using UnityEngine;

// 보스 진영 슬롯을 채우는 하수인 유닛
// OnKnockedOut → 슬롯 제거 이벤트 (SlotSystem이 구독)
public class MinionEntity : CombatEntity
{
    public event Action<MinionEntity> OnSlotRemoveRequested; // SlotSystem이 구독 → 슬롯 비우기
    public event Action<MinionEntity> OnRevived;             // 부활 알림 (연출용)

    private int   reviveLeft;
    private float reviveHpPercent = 0.5f;

    // ── 초기화 ──────────────────────────────────────────

    public void Initialize(MinionData data)
    {
        IsPlayerSide = false;

        InitializeBase(
            maxHp:           data.maxHp,
            baseAttack:      data.baseAttack,
            maxActionPoints: 0,
            entityName:      data.minionName,
            portrait:        data.portrait
        );

        SetSlot(data.preferredSlot);
        IsImmobile = data.isImmobile;   // 고정포대형 하수인

        SetPatterns(data.patterns);

        reviveLeft      = data.reviveCount;
        reviveHpPercent = data.reviveHpPercent;

        // 도발은 상태이상으로 구현돼 있다. 전투 내내 유지되도록 긴 지속턴을 준다.
        if (data.startsWithTaunt)
            ApplyStatus(StatusEffect.Taunt, 1, 99);
    }

    // ── KnockOut ────────────────────────────────────────

    protected override void OnKnockedOut()
    {
        // 부활 — 슬롯 제거 이벤트를 쏘지 않고 그 자리에서 되살아난다.
        // Heal은 CheckKnockedOut을 부르지 않으므로 여기서 호출해도 재진입이 없다.
        if (reviveLeft > 0)
        {
            reviveLeft--;
            ClearIntent();  // 쓰러지는 사이 예고해 둔 행동은 무효

            int amount = Mathf.Max(1, Mathf.RoundToInt(Stats.MaxHp * reviveHpPercent));
            Heal(amount);

            OnRevived?.Invoke(this);
            return;
        }

        OnSlotRemoveRequested?.Invoke(this);
    }
}
