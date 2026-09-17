using System;
using System.Collections.Generic;
using UnityEngine;

// 플레이어·보스가 보유한 렐릭의 패시브/트리거 효과 관리
// 렐릭 획득 시 구독 등록, 트리거 발생 시 효과 실행
public class RelicManager : MonoBehaviour
{
    public static RelicManager Instance { get; private set; }

    // ── 보유 목록 ────────────────────────────────────────

    public IReadOnlyList<RelicData> PlayerRelics => playerRelics;
    public IReadOnlyList<RelicData> BossRelics   => bossRelics;

    private readonly List<RelicData> playerRelics = new();
    private readonly List<RelicData> bossRelics   = new();

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<RelicData, bool> OnRelicAdded; // (relic, isPlayer)

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 렐릭 추가 ────────────────────────────────────────

    public void AddPlayerRelic(RelicData relic)
    {
        playerRelics.Add(relic);
        ApplyPassives(relic, isPlayer: true);
        OnRelicAdded?.Invoke(relic, true);
    }

    public void AddBossRelic(RelicData relic)
    {
        bossRelics.Add(relic);
        ApplyPassives(relic, isPlayer: false);
        OnRelicAdded?.Invoke(relic, false);
    }

    // 보상·상점에서 중복 지급을 막기 위한 확인
    public bool PlayerHasRelic(RelicData relic) => relic != null && playerRelics.Contains(relic);

    // ── Passive 렐릭 즉시 적용 ───────────────────────────

    // Passive 효과는 획득 즉시 적용 (스탯 직접 보정)
    private void ApplyPassives(RelicData relic, bool isPlayer)
    {
        foreach (var effect in relic.effects)
        {
            if (effect.trigger != RelicTrigger.Passive) continue;

            if (isPlayer) ApplyPlayerPassive(effect);
            else          ApplyBossPassive(effect);
        }
    }

    private void ApplyPlayerPassive(RelicEffect effect)
    {
        var player = GameManager.Instance.Player;
        if (player == null) return;

        switch (effect.effectType)
        {
            case RelicEffectType.IncreaseDamage:
                player.Stats.AddBaseAttackBonus(effect.value);
                break;

            case RelicEffectType.IncreaseMaxHp:
                player.Stats.AdjustMaxHp(effect.value);
                break;

            case RelicEffectType.IncreaseBoardDice:
                GameManager.Instance.BoardPhaseManagerRef?.AddDiceBonus(effect.value);
                break;
        }
    }

    // 보스가 상점에서 선점한 렐릭도 실제로 보스를 강하게 만들어야
    // "먼저 사가면 손해"라는 긴장감이 성립한다. 적용 안 하면 선점이 그냥 재고만 없애는 셈이다.
    //
    // 스탯 계열만 적용한다 — 드로우·에너지·주사위는 플레이어 자원이라 보스에게 뜻이 없다.
    // 보스는 스테이지마다 새로 생성되므로(StartStage) 보정도 그때 함께 사라진다.
    private void ApplyBossPassive(RelicEffect effect)
    {
        var boss = GameManager.Instance.CurrentBoss;
        if (boss == null) return;

        switch (effect.effectType)
        {
            case RelicEffectType.IncreaseDamage:
                boss.Stats.AddBaseAttackBonus(effect.value);
                break;

            case RelicEffectType.IncreaseMaxHp:
                boss.Stats.AdjustMaxHp(effect.value);
                break;
        }
    }

    // ── 트리거 호출 (CombatManager·BoardPhaseManager 등이 호출) ──

    public void TriggerPlayerRelics(RelicTrigger trigger)
    {
        foreach (var relic in playerRelics)
            foreach (var effect in relic.effects)
                if (effect.trigger == trigger)
                    ExecutePlayerEffect(effect);
    }

    private void ExecutePlayerEffect(RelicEffect effect)
    {
        var player = GameManager.Instance.Player;

        switch (effect.effectType)
        {
            case RelicEffectType.DrawCards:
                player.DeckManager.DrawCards(effect.value);
                break;

            case RelicEffectType.GainEnergy:
                player.DeckManager.GainEnergy(effect.value);
                break;

            case RelicEffectType.GainShield:
                player.AddShield(effect.value);
                break;

            case RelicEffectType.HealHp:
                player.Heal(effect.value);
                break;

            case RelicEffectType.GainGold:
                GameManager.Instance.AddPlayerGold(effect.value);
                break;
        }
    }

    // ── 런 초기화 ────────────────────────────────────────

    public void ClearAll()
    {
        playerRelics.Clear();
        bossRelics.Clear();
    }

    // 보스 렐릭은 보스 **객체**에 귀속된다. 스테이지마다 보스가 새 객체이므로 목록도 새로 시작한다.
    // (스탯 보정은 이미 옛 보스 객체에만 적용돼 있어 따로 되돌릴 것이 없다)
    public void ClearBossRelics() => bossRelics.Clear();
}
