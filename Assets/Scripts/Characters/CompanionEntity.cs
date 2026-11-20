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

    // 툴팁 등에서 원본 설명·패턴을 되짚는다
    public CompanionData Data { get; private set; }

    // ── 강화 (GDD 9장) ──────────────────────────────────
    // 동료는 원래 한 명씩 따로 존재하므로 카드처럼 복제할 필요가 없다. 이 동료에게 단계만 쌓는다.
    public const int MaxUpgradeLevel = 2;
    public int  UpgradeLevel { get; private set; }
    public bool CanUpgrade   => UpgradeLevel < MaxUpgradeLevel;

    public void Upgrade()
    {
        if (!CanUpgrade || Data == null) return;

        var step = UpgradeStepFor(Data, UpgradeLevel + 1);
        UpgradeLevel++;

        if (step.hp     != 0) Stats.AdjustMaxHp(step.hp);          // 늘어난 만큼 현재 HP도 오른다
        if (step.attack != 0) Stats.AddBaseAttackBonus(step.attack);

        // 행동 수치 증가 — 패턴 에셋은 여러 유닛이 공유하므로 이 동료 몫만 복제해서 고친다
        var list = new List<ActionPatternData>();
        foreach (var p in Patterns)
        {
            if (p == null) continue;
            list.Add(step.powerPercent > 0 ? ScaledCopy(p, step.powerPercent) : p);
        }
        if (step.extra != null)
            foreach (var p in step.extra) if (p != null && !list.Contains(p)) list.Add(p);
        SetPatterns(list);

        EntityName = Data.companionName + new string('+', UpgradeLevel);
    }

    // 강화 한 단계에서 실제로 적용될 값 — 데이터가 비어 있으면 자동 규칙
    public readonly struct UpgradeStep
    {
        public readonly int hp, attack, powerPercent;
        public readonly List<ActionPatternData> extra;
        public UpgradeStep(int hp, int attack, int powerPercent, List<ActionPatternData> extra)
        { this.hp = hp; this.attack = attack; this.powerPercent = powerPercent; this.extra = extra; }
    }

    public static UpgradeStep UpgradeStepFor(CompanionData data, int toLevel)
    {
        var up = toLevel == 1 ? data.upgrade1 : data.upgrade2;
        if (up == null || up.IsEmpty)
            return new UpgradeStep(Mathf.Max(3, Mathf.RoundToInt(data.maxHp * 0.2f)), 0, 30, null);
        return new UpgradeStep(up.maxHpBonus, up.attackBonus, up.patternPowerPercent, up.extraPatterns);
    }

    // 다음 강화 미리보기 문구
    public string NextUpgradePreview()
    {
        if (Data == null) return "";
        if (!CanUpgrade) return "최대 강화 (++)";

        var step = UpgradeStepFor(Data, UpgradeLevel + 1);
        var sb = new System.Text.StringBuilder();
        sb.Append($"{EntityName} → {Data.companionName}{new string('+', UpgradeLevel + 1)}");
        if (step.hp != 0)           sb.Append($"\n최대 HP +{step.hp}");
        if (step.attack != 0)       sb.Append($"\n공격력 +{step.attack}");
        if (step.powerPercent != 0) sb.Append($"\n행동 수치 +{step.powerPercent}%");
        if (step.extra != null)
            foreach (var p in step.extra) if (p != null) sb.Append($"\n새 행동: {p.patternName}");
        return sb.ToString();
    }

    private static ActionPatternData ScaledCopy(ActionPatternData source, int percent)
    {
        var copy = UnityEngine.Object.Instantiate(source);   // 직렬화 복제라 효과 배열까지 새로 만들어진다
        copy.name = source.name;
        if (copy.effects != null)
            foreach (var e in copy.effects)
                if (e != null && e.value > 0 && CardUpgrader.IsScalable(e.type))
                    e.value += Mathf.Max(1, Mathf.RoundToInt(e.value * percent / 100f));
        return copy;
    }

    public void Initialize(CompanionData data)
    {
        Data = data;
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
        if (IsKnockedOutThisCombat) return;  // 쓰러진 동안 행동 불가
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
