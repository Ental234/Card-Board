using System;
using System.Collections.Generic;
using UnityEngine;

// 모든 전투 유닛(플레이어·동료·보스·하수인)의 런타임 스탯
// MonoBehaviour 아님 — CombatEntity가 소유하는 순수 C# 클래스
public class CombatStats
{
    // ── 이벤트 ──────────────────────────────────────────

    public event Action<int, int>          OnHpChanged;      // (현재, 최대)
    public event Action<int>               OnShieldChanged;  // (현재 방어막)
    public event Action<int, int>          OnApChanged;      // (현재, 최대) 행동력
    public event Action<StatusEffect, int> OnStatusChanged;  // (상태이상, 스택)
    public event Action<int>               OnDamageTaken;    // (실제로 HP를 깎은 양) 렐릭 트리거용
    public event Action                    OnKnockedOut;     // HP = 0

    // ── 프로퍼티 ────────────────────────────────────────

    public int  MaxHp               { get; private set; }
    public int  CurrentHp           { get; private set; }
    public int  Shield              { get; private set; }
    public int  BaseAttack          { get; private set; }
    public int  MaxActionPoints     { get; private set; }
    public int  CurrentActionPoints { get; private set; }

    public bool IsAlive      => CurrentHp > 0;
    public bool IsKnockedOut => CurrentHp <= 0;

    // 상태이상 스택 수 + 남은 지속 턴
    private readonly Dictionary<StatusEffect, int> stacks   = new();
    private readonly Dictionary<StatusEffect, int> duration = new();

    // ── 초기화 ──────────────────────────────────────────

    public void Initialize(int maxHp, int baseAttack, int maxActionPoints)
    {
        MaxHp               = maxHp;
        CurrentHp           = maxHp;
        BaseAttack          = baseAttack;
        MaxActionPoints     = maxActionPoints;
        CurrentActionPoints = maxActionPoints;
        Shield              = 0;
        stacks.Clear();
        duration.Clear();
    }

    // ── HP ──────────────────────────────────────────────

    // ── 피격(Damage) vs 상실(HP Loss) ───────────────────
    //
    // 피격: 전투 중 공격을 맞는 것. 취약 배수·방어막 흡수를 거치고 OnDamageTaken을 발동한다.
    // 상실: 저주 칸·이벤트 페널티처럼 전투와 무관하게 HP만 잃는 것.
    //       수정치·방어막·피격 트리거를 전부 건너뛴다.
    //
    // 슬더스와 같은 구분 — 이벤트로 피가 깎여도 "피격 시" 유물은 발동하지 않는다.

    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;

        // 취약: 받는 피해 50% 증가
        if (HasStatus(StatusEffect.Vulnerable))
            amount = Mathf.RoundToInt(amount * 1.5f);

        // 방어막 선흡수
        int absorbed = Mathf.Min(Shield, amount);
        if (absorbed > 0)
        {
            Shield -= absorbed;
            amount -= absorbed;
            OnShieldChanged?.Invoke(Shield);
        }

        if (amount <= 0) return;   // 방어막이 전부 막아냈다 — 피격으로 치지 않는다

        ApplyHpLoss(amount);
        OnDamageTaken?.Invoke(amount);
        CheckKnockedOut();
    }

    // 실제로 깎지 않고 최종 피해량만 계산한다 (적 의도 표시용).
    // TakeDamage의 취약 배수와 반드시 같은 식을 써야 표시와 실제가 어긋나지 않는다.
    // 방어막은 빼지 않는다 — 화면에 방어막이 따로 표시되므로 '들어오는 공격량'을 보여준다.
    public int PreviewIncomingDamage(int amount)
    {
        if (amount <= 0) return 0;

        if (HasStatus(StatusEffect.Vulnerable))
            amount = Mathf.RoundToInt(amount * 1.5f);

        return amount;
    }

    // HP 상실 — 방어막도 취약도 피격 트리거도 무시하고 HP만 깎는다
    public void LoseHp(int amount)
    {
        if (amount <= 0) return;

        ApplyHpLoss(amount);
        CheckKnockedOut();
    }

    private void ApplyHpLoss(int amount)
    {
        CurrentHp = Mathf.Max(0, CurrentHp - amount);
        OnHpChanged?.Invoke(CurrentHp, MaxHp);
    }

    private void CheckKnockedOut()
    {
        if (CurrentHp <= 0)
            OnKnockedOut?.Invoke();
    }

    public void Heal(int amount)
    {
        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
        OnHpChanged?.Invoke(CurrentHp, MaxHp);
    }

    // 방어막은 전투 턴 시작 시 CombatManager가 ClearShield() 호출
    public void AddShield(int amount)
    {
        Shield += amount;
        OnShieldChanged?.Invoke(Shield);
    }

    public void ClearShield()
    {
        Shield = 0;
        OnShieldChanged?.Invoke(0);
    }

    // 렐릭 등 외부 효과로 기본 공격력 보정
    public void AddBaseAttackBonus(int amount)
    {
        BaseAttack += amount;
    }

    // ── 행동력 ──────────────────────────────────────────

    public bool SpendActionPoint(int cost = 1)
    {
        if (CurrentActionPoints < cost) return false;
        CurrentActionPoints -= cost;
        OnApChanged?.Invoke(CurrentActionPoints, MaxActionPoints);
        return true;
    }

    public void RestoreActionPoints()
    {
        CurrentActionPoints = MaxActionPoints;
        OnApChanged?.Invoke(CurrentActionPoints, MaxActionPoints);
    }

    // ── 공격력 계산 ─────────────────────────────────────

    // 카드의 기본 피해값을 받아 스탯 보정 후 반환
    // 실제 피해 공식은 추후 밸런싱 단계에서 확장
    public int CalculateAttack(int cardBaseDamage)
    {
        int total = cardBaseDamage + BaseAttack;

        // 약화: 공격력 25% 감소
        if (HasStatus(StatusEffect.Weak))
            total = Mathf.RoundToInt(total * 0.75f);

        return Mathf.Max(0, total);
    }

    // ── 상태이상 ────────────────────────────────────────

    public void ApplyStatus(StatusEffect status, int amount, int turns)
    {
        if (status == StatusEffect.None) return;

        if (stacks.ContainsKey(status))
        {
            stacks[status]   += amount;
            duration[status]  = Mathf.Max(duration[status], turns);
        }
        else
        {
            stacks[status]   = amount;
            duration[status] = turns;
        }

        OnStatusChanged?.Invoke(status, stacks[status]);
    }

    public bool HasStatus(StatusEffect status) =>
        stacks.TryGetValue(status, out int v) && v > 0;

    public int GetStatusStacks(StatusEffect status) =>
        stacks.TryGetValue(status, out int v) ? v : 0;

    // 남은 지속 턴 — 기절·도발·약화·취약처럼 스택이 무의미한 상태에 표시할 값
    public int GetStatusDuration(StatusEffect status) =>
        duration.TryGetValue(status, out int v) ? v : 0;

    // 턴 시작 시 CombatManager가 호출
    // 상태이상 피해 적용 → 지속 턴 감소 → 만료 제거
    public void ProcessStatusEffects()
    {
        var keys    = new List<StatusEffect>(stacks.Keys);
        var expired = new List<StatusEffect>();

        // 상태이상 피해는 현재 TakeDamage(피격)로 처리한다.
        // 방어막을 무시해야 하는 종류(독 등)는 추후 LoseHp(상실)로 옮길 것.
        foreach (StatusEffect status in keys)
        {
            switch (status)
            {
                case StatusEffect.Bleed:
                    TakeDamage(stacks[status]);           // 고정 피해, 스택 유지
                    break;
                case StatusEffect.Poison:
                    TakeDamage(stacks[status]);
                    stacks[status] = Mathf.Max(0, stacks[status] - 1); // 매 턴 스택 1 감소
                    break;
                case StatusEffect.Burn:
                    TakeDamage(stacks[status]);
                    break;
                // Stun·Taunt·Weak·Vulnerable은 피해 없이 지속 턴만 감소
            }

            duration[status]--;

            if (duration[status] <= 0 || stacks[status] <= 0)
                expired.Add(status);
            else
                // 독처럼 스택이 줄거나 지속 턴이 깎인 것도 알려야 화면 숫자가 낡지 않는다
                OnStatusChanged?.Invoke(status, stacks[status]);
        }

        foreach (StatusEffect s in expired)
        {
            stacks.Remove(s);
            duration.Remove(s);
            OnStatusChanged?.Invoke(s, 0);
        }
    }
}
