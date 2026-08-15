using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 전투 전체 흐름 관리
// 플레이어 턴 → 적 턴 코루틴 반복, 카드 효과 실행, 승패 판정
public class CombatManager : MonoBehaviour
{
    [SerializeField] private SlotSystem slotSystem;

    // ── 이벤트 ──────────────────────────────────────────

    public event Action               OnCombatStart;
    public event Action<bool>         OnCombatEnd;       // (playerWon)
    public event Action<CombatEntity> OnEntityTurnBegin;
    public event Action<CombatEntity> OnEntityTurnEnd;

    // 적 의도(인텐트)
    public event Action<CombatEntity> OnIntentDecided;
    public event Action<CombatEntity> OnIntentExecuted;
    public event Action               OnIntentsRefreshed;  // 표시값이 갱신됨 (UI 재그리기)

    // ── 참조 ────────────────────────────────────────────

    private PlayerCharacter       player;
    private List<CompanionEntity> companions = new();
    private BossEntity            boss;
    private List<MinionEntity>    minions    = new();

    private bool combatActive;
    private bool playerTurnActive;

    // 패턴 실행 중 재진입 차단 (동료 패턴이 또 다른 패턴을 연쇄 발동시키는 것을 막는다)
    private bool patternRunning;

    // ── 전투 시작 ────────────────────────────────────────

    public void StartCombat(PlayerCharacter pc,
                            List<CompanionEntity> companionList,
                            BossEntity bossEntity       = null,
                            List<MinionEntity> minionList = null)
    {
        player    = pc;
        companions = companionList ?? new List<CompanionEntity>();
        boss      = bossEntity;
        minions   = minionList   ?? new List<MinionEntity>();

        slotSystem.ClearAll();
        PlaceAllEntities();
        SubscribeEvents();

        foreach (var c in companions) c.OnCombatStart();

        // 보스는 스테이지 내내 살아남아 재사용되므로 쿨다운을 반드시 여기서 초기화한다
        boss?.ResetPatternState();
        foreach (var m in minions) m.ResetPatternState();

        combatActive     = true;
        playerTurnActive = false;
        patternRunning   = false;

        OnCombatStart?.Invoke();
        StartCoroutine(CombatLoop());
    }

    private void PlaceAllEntities()
    {
        slotSystem.PlaceEntity(player, true, player.CurrentSlot);

        // 대기열 동료(CurrentSlot 0)가 섞여 들어오면 PlaceEntity가 조용히 실패한다.
        // 여기서 걸러 "왜 안 나왔지"를 배치 단계에서 드러나게 한다.
        foreach (var c in companions)
        {
            if (c.CurrentSlot < 1 || c.CurrentSlot > 4)
            {
                Debug.LogWarning($"[CombatManager] {c.EntityName}의 슬롯이 {c.CurrentSlot}이라 배치하지 않았습니다.");
                continue;
            }
            slotSystem.PlaceEntity(c, true, c.CurrentSlot);
        }

        if (boss != null) slotSystem.PlaceEntity(boss, false, boss.CurrentSlot);
        foreach (var m in minions)    slotSystem.PlaceEntity(m, false, m.CurrentSlot);
    }

    // ── 이벤트 구독 ──────────────────────────────────────

    private void SubscribeEvents()
    {
        player.OnRunEnd            += HandlePlayerDeath;
        player.Stats.OnDamageTaken += HandlePlayerDamaged;

        // 자리가 바뀌면 예고 표시도 따라와야 한다 — 안 그러면 피한 뒤에도 옛 칸이 위협으로 남는다
        slotSystem.OnSlotsChanged += RefreshIntentPreviews;

        foreach (var c in companions)
            c.OnKnockedOutInCombat += HandleCompanionKnockout;

        if (boss != null)
            boss.OnStageClear += HandleBossDefeated;

        foreach (var m in minions)
            m.OnSlotRemoveRequested += HandleMinionDefeated;
    }

    private void UnsubscribeEvents()
    {
        player.OnRunEnd            -= HandlePlayerDeath;
        player.Stats.OnDamageTaken -= HandlePlayerDamaged;

        slotSystem.OnSlotsChanged -= RefreshIntentPreviews;

        foreach (var c in companions)
            c.OnKnockedOutInCombat -= HandleCompanionKnockout;

        if (boss != null)
            boss.OnStageClear -= HandleBossDefeated;

        foreach (var m in minions)
            m.OnSlotRemoveRequested -= HandleMinionDefeated;
    }

    // ── 전투 루프 ────────────────────────────────────────

    private IEnumerator CombatLoop()
    {
        while (combatActive)
        {
            // 플레이어 턴이 시작되기 '전'에 적의 다음 행동을 정한다.
            // 그래야 플레이어가 턴 내내 인텐트를 보고 이동·방어로 대응할 수 있다.
            DecideEnemyIntents();

            yield return StartCoroutine(PlayerTurn());
            if (!combatActive) yield break;

            yield return StartCoroutine(EnemyTurn());
        }
    }

    // ── 플레이어 턴 ──────────────────────────────────────

    private IEnumerator PlayerTurn()
    {
        player.OnTurnStart();  // 방어막·상태이상·행동력·에너지·드로우 포함

        foreach (var c in companions)
        {
            c.OnTurnStart();
            // 쓰러진 동료는 쿨다운도 돌지 않는다 — 쓰러져 있는 사이에 스킬이 준비되면 이상하다
            if (c.IsActive) c.TickPatternCooldowns();
        }

        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnCombatTurnStart);

        // 렐릭 뒤에 둔다 — 순서가 반대면 렐릭이 건 버프가 동료 행동에 한 턴 늦게 반영된다
        yield return StartCoroutine(RunCompanionPatterns(TriggerTiming.TurnStart));

        // 턴 시작에 벌어진 일이 예상 피해를 바꾼다 —
        // 취약이 만료되거나, 동료 패턴이 적에게 약화를 걸거나, 상태이상 피해로 누가 쓰러지거나.
        // 플레이어가 판단을 시작하기 직전에 한 번 맞춰둔다.
        RefreshIntentPreviews();

        OnEntityTurnBegin?.Invoke(player);
        playerTurnActive = true;

        // UI의 "턴 종료" 버튼이 EndPlayerTurn()을 호출할 때까지 대기
        yield return new WaitUntil(() => !playerTurnActive);

        // player.OnTurnEnd() '앞'이어야 한다.
        // OnTurnEnd가 deckManager.EndTurn()으로 손패를 전부 버리기 때문에,
        // 뒤에 두면 동료의 드로우 패턴이 버려진 직후에 뽑아 다음 턴 드로우와 겹친다.
        yield return StartCoroutine(RunCompanionPatterns(TriggerTiming.TurnEnd));

        player.OnTurnEnd();
        foreach (var c in companions) c.OnTurnEnd();
        OnEntityTurnEnd?.Invoke(player);
    }

    // ── 포지션 직접 이동 (화살표 조작) ───────────────────

    // 아군 유닛을 좌우 한 칸 옮긴다. 목적지에 아군이 있으면 자리를 바꾼다.
    // 행동력은 파티 공용 자원으로 플레이어의 AP에서 1 소모한다.
    // (동료는 행동력을 따로 갖지 않는다 — maxActionPoints = 0)
    public bool TryMoveFriendly(CombatEntity entity, int direction)
    {
        if (!combatActive || !playerTurnActive)              return false;
        if (entity == null || !entity.IsPlayerSide)          return false;
        // 이동 판정은 전투 행동과 별개 (고정포대는 공격은 되지만 이동 불가)
        if (!entity.CanMove)                                 return false;
        if (!slotSystem.CanMoveOrSwap(entity, direction, out _)) return false;

        if (!player.Stats.SpendActionPoint())                return false;  // 행동력 부족

        return slotSystem.MoveOrSwap(entity, direction);
    }

    // UI가 화살표를 보일지 판단할 때 쓴다 (행동력까지 함께 확인)
    public bool CanMoveFriendly(CombatEntity entity, int direction)
    {
        if (!combatActive || !playerTurnActive)     return false;
        if (entity == null || !entity.IsPlayerSide) return false;
        if (!entity.CanMove)                        return false;
        if (player.Stats.CurrentActionPoints <= 0)  return false;

        return slotSystem.CanMoveOrSwap(entity, direction, out _);
    }

    // UI — 턴 종료 버튼
    public void EndPlayerTurn()
    {
        if (playerTurnActive)
            playerTurnActive = false;
    }

    // ── 적 턴 ────────────────────────────────────────────

    private IEnumerator EnemyTurn()
    {
        // 보스 → 하수인 순으로 행동 (보스가 먼저 위협)
        var enemies = new List<CombatEntity>();
        if (boss != null && boss.IsActive) enemies.Add(boss);
        enemies.AddRange(minions.FindAll(m => m.IsActive));

        foreach (var enemy in enemies)
        {
            if (!combatActive) yield break;

            enemy.OnTurnStart();
            OnEntityTurnBegin?.Invoke(enemy);

            yield return StartCoroutine(EnemyAct(enemy));

            enemy.OnTurnEnd();
            OnEntityTurnEnd?.Invoke(enemy);
        }
    }

    // 적이 다음 턴에 할 행동을 미리 정해 인텐트로 보관한다.
    //
    // 패턴만 고정하고 타겟은 실행 시점에 다시 계산하므로,
    // 플레이어가 그 사이에 슬롯을 옮기면 공격이 빗나간다.
    private void DecideEnemyIntents()
    {
        if (!combatActive) return;

        var enemies = new List<CombatEntity>();
        if (boss != null && boss.IsActive) enemies.Add(boss);
        enemies.AddRange(minions.FindAll(m => m.IsActive));

        foreach (var enemy in enemies)
        {
            enemy.ClearIntent();

            // 쿨다운은 이 판정 '직전'에 깎는다. 적의 OnTurnStart는 이보다 늦게 오므로
            // 거기서 깎으면 적만 한 라운드씩 더 쉬게 된다.
            enemy.TickPatternCooldowns();

            if (!enemy.CanAct) continue;  // 기절·쓰러짐이면 예고 자체가 없다

            var candidates = new List<ActionPatternData>();
            int bestPriority = int.MinValue;

            foreach (var pattern in enemy.Patterns)
            {
                if (pattern == null || pattern.timing != TriggerTiming.OwnTurn) continue;
                if (!enemy.IsPatternReady(pattern))                            continue;
                if (!MeetsCondition(enemy, pattern))                           continue;

                // 지금 노릴 대상이 없는 패턴은 후보로도 올리지 않는다
                if (slotSystem.GetPatternTargets(enemy, pattern).Count == 0) continue;

                if (pattern.priority > bestPriority)
                {
                    bestPriority = pattern.priority;
                    candidates.Clear();
                    candidates.Add(pattern);
                }
                else if (pattern.priority == bestPriority)
                {
                    candidates.Add(pattern);
                }
            }

            // 후보가 없으면 인텐트도 없다 → EnemyAct의 기본 공격 폴백이 처리한다.
            // 이 검사가 아래 Random.Range의 방어이기도 하다 (min >= max면 min을 그대로 반환해
            // 빈 리스트에 인덱스 0으로 접근하게 된다).
            if (candidates.Count == 0) continue;

            var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            enemy.SetIntent(BuildIntent(enemy, picked));
            OnIntentDecided?.Invoke(enemy);
        }

        OnIntentsRefreshed?.Invoke();
    }

    // UI에 보여줄 예고. 실행 로직은 이 값을 쓰지 않는다.
    private EnemyIntent BuildIntent(CombatEntity enemy, ActionPatternData pattern)
    {
        var intent = new EnemyIntent { pattern = pattern };
        FillPreview(enemy, intent);
        return intent;
    }

    // 지금 이 순간의 슬롯 배치로 예고 표시값을 다시 채운다.
    // 패턴은 건드리지 않는다 — 바뀌는 건 '누구를 노리는가'뿐이다.
    private void FillPreview(CombatEntity enemy, EnemyIntent intent)
    {
        var pattern = intent.pattern;

        intent.previewSlots = SlotMask.None;
        intent.previewValue = 0;
        intent.kind         = IntentKind.Unknown;

        foreach (var target in slotSystem.GetPatternTargets(enemy, pattern))
        {
            if (target.CurrentSlot >= 1 && target.CurrentSlot <= 4)
                intent.previewSlots |= (SlotMask)(1 << (target.CurrentSlot - 1));
        }

        if (pattern.effects != null)
        {
            foreach (var effect in pattern.effects)
            {
                switch (effect.type)
                {
                    case EffectType.Damage:
                        intent.kind         = pattern.isAoe ? IntentKind.AttackAoe : IntentKind.Attack;
                        intent.previewValue = enemy.CalculateAttack(effect.value);
                        break;

                    case EffectType.ApplyStatus:
                        // 도발은 자기에게 거는 것이라 공격과 구분해서 보여준다
                        if (intent.kind == IntentKind.Unknown)
                            intent.kind = effect.status == StatusEffect.Taunt
                                        ? IntentKind.Taunt
                                        : (pattern.targetAlly ? IntentKind.Buff : IntentKind.Debuff);
                        break;

                    case EffectType.Heal:
                        if (intent.kind == IntentKind.Unknown) intent.kind = IntentKind.Heal;
                        if (intent.previewValue == 0) intent.previewValue = effect.value;
                        break;

                    case EffectType.Shield:
                        if (intent.kind == IntentKind.Unknown) intent.kind = IntentKind.Buff;
                        if (intent.previewValue == 0) intent.previewValue = effect.value;
                        break;
                }

                // 공격이 확정되면 더 볼 것 없다 — 피해가 가장 중요한 정보다
                if (intent.kind == IntentKind.Attack || intent.kind == IntentKind.AttackAoe) break;
            }
        }

        // 자폭은 무엇을 하든 이게 제일 중요하다
        if (pattern.selfDestructAfterUse) intent.kind = IntentKind.SelfDestruct;
    }

    // 슬롯 배치나 상태가 바뀌면 예고 표시를 다시 계산한다.
    //
    // 이걸 하지 않으면 플레이어가 노려진 칸에서 빠져나간 뒤에도 옛 칸이 위협 표시로 남아
    // 화면이 실제 결과와 다른 말을 하게 된다. 반대로 갱신하면 말을 옮길 때마다
    // 위협 표시가 따라오거나 떨어져 나가는 게 눈에 보인다.
    public void RefreshIntentPreviews()
    {
        if (!combatActive) return;

        if (boss != null && boss.CurrentIntent != null) FillPreview(boss, boss.CurrentIntent);

        foreach (var m in minions)
            if (m.CurrentIntent != null) FillPreview(m, m.CurrentIntent);

        OnIntentsRefreshed?.Invoke();
    }

    // 플레이어 슬롯(1~4)이 이번 라운드에 받을 예상 피해 합계.
    // 예고를 낸 적 전부를 훑어 지금 배치 기준으로 계산한다.
    public int GetIncomingDamage(int playerSlot)
    {
        if (!combatActive) return 0;

        var occupant = slotSystem.GetEntityAt(true, playerSlot);
        if (occupant == null || !occupant.IsActive) return 0;

        int total = 0;
        if (boss != null) total += IncomingFrom(boss, occupant);
        foreach (var m in minions) total += IncomingFrom(m, occupant);

        return total;
    }

    // 이 방향으로 옮겼을 때 각 플레이어 슬롯이 받게 될 피해 (인덱스 0 = 슬롯 1).
    //
    // 행동력을 쓰기 '전'에 결과를 보여주기 위한 것이다.
    // 취약 같은 보정은 사람을 따라 움직이므로, 화면 숫자를 원본으로 낮춰 표시하는 대신
    // 옮긴 뒤의 정확한 값을 그대로 계산해 보여준다 — 플레이어가 암산할 게 없어야 한다.
    public int[] PreviewIncomingAfterMove(CombatEntity entity, int direction)
    {
        var result = new int[4];
        if (!combatActive) return result;

        slotSystem.SimulateMove(entity, direction, () =>
        {
            for (int i = 0; i < 4; i++)
                result[i] = GetIncomingDamage(i + 1);
        });

        return result;
    }

    private int IncomingFrom(CombatEntity enemy, CombatEntity target)
    {
        var intent = enemy.CurrentIntent;
        if (intent == null || !enemy.IsActive || !enemy.CanAct) return 0;
        if (intent.pattern.effects == null)                     return 0;

        if (!slotSystem.GetPatternTargets(enemy, intent.pattern).Contains(target)) return 0;

        int total = 0;
        foreach (var effect in intent.pattern.effects)
            if (effect.type == EffectType.Damage)
                total += target.Stats.PreviewIncomingDamage(enemy.CalculateAttack(effect.value));

        return total;
    }

    // 인텐트에 실린 패턴을 실행한다.
    // 패턴이 없는 적은 기존 동작(최전방 1타)을 그대로 유지한다.
    private IEnumerator EnemyAct(CombatEntity enemy)
    {
        if (!enemy.CanAct) { enemy.ClearIntent(); yield break; }

        var pattern = enemy.CurrentIntent?.pattern;

        if (pattern == null)
        {
            // 폴백은 '패턴 에셋이 하나도 붙지 않은 적'만을 위한 것이다.
            // 패턴은 있는데 전부 쿨다운·조건 미충족이라 예고가 없는 적은 그냥 쉰다 —
            // 여기서 폴백을 태우면 자폭을 기다리는 유닛이 그 사이에 평타를 때린다.
            if (enemy.Patterns.Count == 0)
            {
                // 기존 동작을 그대로 둔다 — 여기서 도발을 반영하면 지금 밸런스가 조용히 바뀐다.
                // 도발을 존중하는 기본 공격이 필요하면 Nearest 패턴 에셋을 붙이면 된다.
                var target = GetFrontmostPlayerEntity();
                if (target != null)
                    target.TakeDamage(enemy.CalculateAttack(enemy.Stats.BaseAttack));
            }

            yield return null;
            yield break;
        }

        // ★ 예고해 둔 슬롯이 아니라 '지금'의 슬롯 배치로 다시 계산한다.
        //   이 한 줄이 "이동으로 회피"를 성립시킨다.
        var targets = slotSystem.GetPatternTargets(enemy, pattern);

        // 대상이 전부 사라졌으면 불발. 쿨다운도 쓰지 않아 다음 턴에 다시 노린다.
        if (targets.Count == 0) { enemy.ClearIntent(); yield break; }

        enemy.MarkPatternUsed(pattern);
        ApplyPatternEffects(enemy, pattern, targets);

        // 먼저 지우고 알린다 — 구독자가 다시 그릴 때 이미 끝난 예고가 남아 있으면 안 된다
        enemy.ClearIntent();
        OnIntentExecuted?.Invoke(enemy);

        yield return null;  // 애니메이션 대기 슬롯 — 추후 WaitForSeconds 등으로 교체
    }

    private CombatEntity GetFrontmostPlayerEntity()
    {
        for (int slot = 1; slot <= 4; slot++)
        {
            var e = slotSystem.GetEntityAt(true, slot);
            if (e != null && e.IsActive) return e;
        }
        return null;
    }

    // ── 카드 실행 (UI → CombatManager) ──────────────────

    // targetOverride: 단일 타겟 카드에서 플레이어가 UI로 직접 선택한 타겟
    //                 null이면 SlotSystem이 자동 결정 (도발·최전방 우선)
    public bool TryPlayCard(CardData card, CombatEntity user, CombatEntity targetOverride = null)
    {
        if (!playerTurnActive) return false;

        // 타겟을 먼저 확정한다. 에너지를 쓰기 전에 걸러야 잘못된 타겟으로
        // 카드만 소모되는 일이 없다. UI를 믿지 않고 여기서 다시 검증한다.
        var targets = ResolveTargets(card, user, targetOverride);
        if (targets == null || targets.Count == 0) return false;

        if (!player.DeckManager.TryPlayCard(card)) return false;

        foreach (var effect in card.effects)
            foreach (var target in targets)
                ExecuteEffect(effect, user, target);

        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnCardPlayed);

        // 반응형 트리거 — 플레이어 '본인'이 쓴 카드에만 반응한다.
        // 이 조건 하나로 동료 스킬이 다른 동료를 깨우는 연쇄가 원천 차단된다.
        if (user == player)
        {
            var reaction = GetReactionTiming(card);
            if (reaction != TriggerTiming.None)
                RunCompanionPatternsImmediate(reaction);
        }

        // 카드로 취약을 걸거나 적을 처치했으면 예상 피해가 달라진다
        RefreshIntentPreviews();

        return true;
    }

    // 카드가 어떤 종류의 행동인지 판정한다.
    // 피해와 방어막을 함께 가진 카드는 '공격'으로 친다 (규칙을 단순하게 유지).
    private TriggerTiming GetReactionTiming(CardData card)
    {
        if (card.effects == null) return TriggerTiming.None;

        bool hasShield = false;

        foreach (var effect in card.effects)
        {
            if (effect.type == EffectType.Damage) return TriggerTiming.OnPlayerAttack;
            if (effect.type == EffectType.Shield) hasShield = true;
        }

        return hasShield ? TriggerTiming.OnPlayerDefend : TriggerTiming.None;
    }

    // 카드가 실제로 적용될 대상 목록. 유효하지 않으면 null.
    private List<CombatEntity> ResolveTargets(CardData card, CombatEntity user,
                                              CombatEntity targetOverride)
    {
        if (card.targetSelf)
            return new List<CombatEntity> { user };

        var valid = slotSystem.GetValidTargets(user, card);

        if (card.isAoe) return valid;

        if (targetOverride != null)
        {
            // 지정한 대상이 유효 타겟 목록에 없으면 사용 실패 (아군을 때리거나 적을 버프하는 것 차단)
            return valid.Contains(targetOverride)
                 ? new List<CombatEntity> { targetOverride }
                 : null;
        }

        // 타겟 미지정 — 도발·최전방 우선순위로 자동 선택
        return valid.Count > 0 ? new List<CombatEntity> { valid[0] } : null;
    }

    // ── 효과 실행 ────────────────────────────────────────

    // 카드와 행동 패턴이 함께 쓰는 효과 실행기.
    // "시전자 + 효과 + 타겟" 세 가지만 받으므로 카드가 아니어도 그대로 재사용된다.
    public void ExecuteEffect(CardEffect effect, CombatEntity user, CombatEntity target)
    {
        switch (effect.type)
        {
            case EffectType.Damage:
                target.TakeDamage(user.CalculateAttack(effect.value));
                break;

            case EffectType.Heal:
                target.Heal(effect.value);
                break;

            case EffectType.Shield:
                target.AddShield(effect.value);
                break;

            case EffectType.ApplyStatus:
                target.ApplyStatus(effect.status, effect.value, effect.duration);
                break;

            case EffectType.MovePosition:
                // effect.value: +1 전방이동, -1 후방이동
                // 움직이는 건 타겟이 아니라 '시전자'다 (돌진처럼 내가 파고드는 카드).
                // 카드 이동은 행동력을 쓰지 않는다 — 행동력은 화살표 직접 이동 전용.
                slotSystem.MoveOrSwap(user, effect.value);
                break;

            // 드로우·에너지는 플레이어 덱 전용 자원이다.
            // 동료 패턴이 쓰면 플레이어 덱에 작용하고(치유사가 카드를 뽑아주는 식),
            // 적이 쓰면 조용히 무시한다.
            case EffectType.DrawCard:
                if (player != null && user.IsPlayerSide)
                    player.DeckManager.DrawCards(effect.value);
                break;

            case EffectType.GainEnergy:
                if (player != null && user.IsPlayerSide)
                    player.DeckManager.GainEnergy(effect.value);
                break;
        }
    }

    // 타겟이 아니라 '시전자'에게 작용하는 효과.
    // AoE에서 타겟 수만큼 반복 실행되면 안 되므로 순회 밖에서 한 번만 처리한다.
    private static bool IsCasterEffect(EffectType type)
        => type == EffectType.MovePosition
        || type == EffectType.DrawCard
        || type == EffectType.GainEnergy;

    // ── 행동 패턴 실행 (동료·적 공용) ───────────────────

    // 한 유닛의 패턴 중 해당 타이밍에 맞고 준비된 것을 전부 실행한다.
    // 반환값: 하나라도 발동했는가 (연출 대기를 넣을지 판단용)
    private bool RunEntityPatterns(CombatEntity entity, TriggerTiming timing)
    {
        // 기절·쓰러짐이면 발동하지 않는다 (이동 판정과는 별개)
        if (entity == null || !entity.CanAct) return false;

        bool fired = false;

        foreach (var pattern in entity.Patterns)
        {
            if (!combatActive) break;

            if (pattern == null || pattern.timing != timing) continue;
            if (!entity.IsPatternReady(pattern))             continue;
            if (!MeetsCondition(entity, pattern))            continue;

            // 타겟은 실행하는 이 순간에 계산한다
            var targets = slotSystem.GetPatternTargets(entity, pattern);

            // 대상이 하나도 없으면 불발 — 쿨다운도 소모하지 않아 다음 턴에 다시 시도한다
            if (targets.Count == 0) continue;

            entity.MarkPatternUsed(pattern);
            ApplyPatternEffects(entity, pattern, targets);
            fired = true;

            // 자폭 등으로 시전자가 사라졌으면 남은 패턴은 돌리지 않는다
            if (!entity.IsActive) break;
        }

        return fired;
    }

    private void ApplyPatternEffects(CombatEntity user, ActionPatternData pattern,
                                     List<CombatEntity> targets)
    {
        if (pattern.effects != null)
        {
            foreach (var effect in pattern.effects)
            {
                if (!combatActive) return;

                // 시전자에게 작용하는 효과는 대상이 여럿이어도 한 번만
                if (IsCasterEffect(effect.type))
                {
                    ExecuteEffect(effect, user, user);
                    continue;
                }

                foreach (var target in targets)
                {
                    if (!combatActive) return;
                    if (target == null) continue;

                    // 앞선 효과로 이미 쓰러진 대상은 건너뛴다 (소생 패턴은 예외)
                    if (!target.IsActive && !pattern.includeKnockedOut) continue;

                    ExecuteEffect(effect, user, target);
                }
            }
        }

        // 자폭 — 효과를 전부 적용한 뒤 스스로 쓰러진다.
        // 방어막·취약 배수·피격 렐릭을 타면 안 되므로 피격이 아니라 상실로 처리한다.
        if (pattern.selfDestructAfterUse && user.IsActive)
            user.LoseHp(user.Stats.CurrentHp);
    }

    // 추가 발동 조건 (전부 0 / false면 조건 없음)
    private bool MeetsCondition(CombatEntity entity, ActionPatternData pattern)
    {
        if (pattern.hpBelowRatio > 0f)
        {
            int maxHp = entity.Stats.MaxHp;
            if (maxHp <= 0) return false;
            if ((float)entity.Stats.CurrentHp / maxHp > pattern.hpBelowRatio) return false;
        }

        // 페이즈·패닉은 보스만 갖는 개념이다
        if (pattern.minPhase > 0 || pattern.requirePanic)
        {
            var bossEntity = entity as BossEntity;
            if (bossEntity == null) return false;

            if (pattern.minPhase > 0 && bossEntity.CurrentPhase < pattern.minPhase) return false;
            if (pattern.requirePanic && !bossEntity.IsPanicMode)                    return false;
        }

        return true;
    }

    // 슬롯 번호가 앞선 동료부터 발동한다 (전방 → 후방)
    private List<CompanionEntity> OrderedCompanions()
    {
        // 원본 리스트를 정렬하면 안 된다 — Subscribe/UnsubscribeEvents가 같은 리스트를 순회한다
        var ordered = new List<CompanionEntity>(companions);
        ordered.Sort((a, b) => a.CurrentSlot.CompareTo(b.CurrentSlot));
        return ordered;
    }

    // 턴 시작·종료용. 유닛 사이에 연출 대기를 끼워 넣을 수 있도록 코루틴으로 둔다.
    private IEnumerator RunCompanionPatterns(TriggerTiming timing)
    {
        if (!combatActive || patternRunning) yield break;

        patternRunning = true;
        try
        {
            foreach (var companion in OrderedCompanions())
            {
                if (!combatActive) break;

                if (RunEntityPatterns(companion, timing))
                    yield return null;  // 연출 대기 자리 — 추후 WaitForSeconds 등으로 교체
            }
        }
        finally
        {
            patternRunning = false;  // 도중에 빠져나가도 반드시 풀어야 영구히 잠기지 않는다
        }
    }

    // 반응형 트리거용 동기 버전.
    // TryPlayCard는 bool을 돌려주는 UI 진입점이라 코루틴을 기다릴 수 없다.
    // 여기서 StartCoroutine을 쏘면 카드 결과와 동료 반응 사이에 프레임 간극이 생겨 순서가 깨진다.
    private void RunCompanionPatternsImmediate(TriggerTiming timing)
    {
        if (!combatActive || patternRunning) return;

        patternRunning = true;
        try
        {
            foreach (var companion in OrderedCompanions())
            {
                if (!combatActive) break;
                RunEntityPatterns(companion, timing);
            }
        }
        finally
        {
            patternRunning = false;
        }
    }

    // ── KnockOut 핸들러 ──────────────────────────────────

    private void HandlePlayerDeath()
    {
        EndCombat(playerWon: false);
    }

    private void HandleCompanionKnockout(CompanionEntity companion)
    {
        // 슬롯 유지, 쓰러짐 표시 — UI 이펙트는 추후 구현
        // companion.IsKnockedOutThisCombat = true 는 CompanionEntity가 자체 처리
    }

    // 플레이어가 실제로 HP를 잃었을 때만 호출된다 (방어막 흡수분 제외)
    private void HandlePlayerDamaged(int amount)
    {
        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnTakeDamage);

        // 반격형 동료. 플레이어의 행동이 아니라 '당한 것'에 반응하므로
        // 동료끼리의 연쇄와는 무관하다 (동료 패턴이 플레이어를 때릴 경로도 없다).
        RunCompanionPatternsImmediate(TriggerTiming.OnPlayerDamaged);
    }

    private void HandleBossDefeated(BossEntity defeatedBoss)
    {
        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnKillEnemy);
        EndCombat(playerWon: true);
    }

    private void HandleMinionDefeated(MinionEntity minion)
    {
        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnKillEnemy);

        minion.ClearIntent();
        slotSystem.RemoveEntity(minion);
        minions.Remove(minion);
        // 적 전멸 체크 (보스 없는 일반 전투)
        if (boss == null && slotSystem.GetAllActive(false).Count == 0)
            EndCombat(playerWon: true);
    }

    // ── 전투 종료 ────────────────────────────────────────

    private void EndCombat(bool playerWon)
    {
        if (!combatActive) return;

        combatActive     = false;
        playerTurnActive = false;

        UnsubscribeEvents();

        foreach (var c in companions) c.OnCombatEnd();

        OnCombatEnd?.Invoke(playerWon);
    }
}
