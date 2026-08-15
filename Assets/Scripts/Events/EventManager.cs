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
                var allCards = player.DeckManager.GetAllCards();
                OnRemoveCardRequested?.Invoke(allCards);
                return true;  // UI가 RemoveSelectedCard() 호출 후 CompleteEvent() 호출

            case EventEffectType.GainRelic:
                if (effect.relic != null)
                    RelicManager.Instance?.AddPlayerRelic(effect.relic);
                break;

            case EventEffectType.GainCompanion:
                if (effect.companion != null)
                    gm.TryAddCompanion(effect.companion);
                break;

            case EventEffectType.TeleportToBoss:
                gm.TeleportPlayerToBoss();
                break;

            case EventEffectType.MaxHpChange:
            case EventEffectType.Nothing:
                break;
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
