using System;
using System.Collections.Generic;
using UnityEngine;

// 이벤트 노드 착지 시 랜덤 이벤트 선택 + 선택지 효과 실행
// BoardPhaseManager.OnPlayerLanded → GameManager → EventManager.TriggerRandomEvent() 순으로 호출
public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    [SerializeField] private List<EventData> eventPool = new();

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<EventData>       OnEventStarted;        // UI가 구독 → 이벤트 패널 표시
    public event Action                  OnEventCompleted;      // 다음 보드 턴으로 진행 신호
    public event Action<List<CardData>>  OnRemoveCardRequested; // UI가 구독 → 카드 선택 UI 표시

    public EventData CurrentEvent { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 이벤트 발동 ─────────────────────────────────────

    public void TriggerRandomEvent()
    {
        if (eventPool.Count == 0) { OnEventCompleted?.Invoke(); return; }
        CurrentEvent = eventPool[UnityEngine.Random.Range(0, eventPool.Count)];
        OnEventStarted?.Invoke(CurrentEvent);
    }

    // ── 선택지 실행 ──────────────────────────────────────

    // UI에서 선택지 번호(0-based) 선택 시 호출
    public void SelectChoice(int choiceIndex)
    {
        if (CurrentEvent == null) return;
        if (choiceIndex < 0 || choiceIndex >= CurrentEvent.choices.Count) return;

        var choice = CurrentEvent.choices[choiceIndex];
        var gm     = GameManager.Instance;

        // 골드 조건 확인
        if (choice.requiresGold && !gm.SpendPlayerGold(choice.goldCost))
            return;  // 골드 부족 — UI가 버튼 비활성화로 사전 차단 권장

        bool pendingUiAction = false;

        foreach (var effect in choice.effects)
        {
            if (ExecuteEffect(effect))
                pendingUiAction = true;  // RemoveCard 등 UI 완료 대기 필요
        }

        if (!pendingUiAction)
            CompleteEvent();
    }

    // 반환값 true = UI 완료 대기 필요 (CompleteEvent를 나중에 호출해야 함)
    private bool ExecuteEffect(EventEffect effect)
    {
        var gm     = GameManager.Instance;
        var player = gm.Player;

        switch (effect.type)
        {
            case EventEffectType.HpChange:
                // 이벤트로 깎이는 HP는 '피격'이 아니라 '상실' — 방어막·피격 트리거를 타지 않는다
                if (effect.value > 0) player.Heal(effect.value);
                else                   player.LoseHp(-effect.value);
                break;

            case EventEffectType.GoldChange:
                if (effect.value > 0) gm.AddPlayerGold(effect.value);
                else                   gm.SpendPlayerGold(-effect.value);
                break;

            case EventEffectType.AddCard:
            case EventEffectType.AddCurseCard:
                if (effect.card != null)
                    player.DeckManager.AddCard(effect.card);
                break;

            case EventEffectType.RemoveCard:
                // 공용 카드 제거 창 (무료). 고르거나 취소하면 이벤트를 끝낸다
                gm.OpenRemovePicker("덱에서 뺄 카드를 고르세요", 0, onDone: CompleteEvent, onCancel: CompleteEvent);
                return true;

            case EventEffectType.GainRelic:
                if (effect.relic != null)
                    RelicManager.Instance?.AddPlayerRelic(effect.relic);
                break;

            case EventEffectType.GainCompanion:
                if (effect.companion != null)
                    gm.TryAddCompanion(effect.companion);
                break;

            case EventEffectType.TeleportToBoss:
                gm.StartHeraldEncounter();
                break;

            case EventEffectType.MaxHpChange:
                // 최대 HP 감소만으로는 쓰러지지 않는다 (CombatStats.AdjustMaxHp가 최소 1 보장)
                player.Stats.AdjustMaxHp(effect.value);
                break;

            case EventEffectType.Nothing:
                break;

            // 강화 이벤트 (GDD 9장) — 고르는 창을 띄우고, 고르거나 취소하면 이벤트를 끝낸다
            case EventEffectType.UpgradeCard:
                bool special = effect.value >= 2;
                gm.OpenUpgradePicker(
                    special ? "특별 강화 - 카드를 고르세요 (+2, 대가 1개)" : "강화할 카드·동료를 고르세요",
                    special ? 2 : 1, special,
                    includeCards: true, includeCompanions: !special,   // 페널티는 카드에만 붙는다
                    onDone: CompleteEvent, onCancel: CompleteEvent);
                return true;   // 창이 닫힐 때까지 이벤트를 끝내지 않는다
        }

        return false;
    }

    // 상점 등 외부에서 카드 제거 UI를 띄워야 할 때 호출
    public void RequestCardRemoval(System.Collections.Generic.List<CardData> cards)
    {
        OnRemoveCardRequested?.Invoke(cards);
    }

    // UI가 카드 제거 완료 후 호출
    public void RemoveSelectedCard(CardData card)
    {
        GameManager.Instance.Player.DeckManager.RemoveCard(card);
        CompleteEvent();
    }

    // 이벤트 완료 — 다음 페이즈로 진행
    public void CompleteEvent()
    {
        CurrentEvent = null;
        OnEventCompleted?.Invoke();
    }
}
