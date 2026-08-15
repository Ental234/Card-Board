using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 이벤트 노드 팝업 — EventManager 구독
// 선택지 버튼을 동적으로 생성하고 플레이어 선택을 EventManager로 전달
public class EventUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI eventTitleText;
    [SerializeField] private TextMeshProUGUI eventDescText;
    [SerializeField] private Transform       choiceButtonRoot;
    [SerializeField] private Button          choiceButtonPrefab;

    private readonly List<Button> choiceButtons = new();

    private void OnEnable()
    {
        EventManager.Instance.OnEventStarted       += ShowEvent;
        EventManager.Instance.OnRemoveCardRequested += ShowCardRemovalUI;
    }

    private void OnDisable()
    {
        if (EventManager.Instance == null) return;
        EventManager.Instance.OnEventStarted       -= ShowEvent;
        EventManager.Instance.OnRemoveCardRequested -= ShowCardRemovalUI;
    }

    // ── 이벤트 표시 ─────────────────────────────────────

    private void ShowEvent(EventData eventData)
    {
        if (eventTitleText != null) eventTitleText.text = eventData.eventName;
        if (eventDescText  != null) eventDescText.text  = eventData.description;

        BuildChoiceButtons(eventData);
    }

    private void BuildChoiceButtons(EventData eventData)
    {
        foreach (var btn in choiceButtons) Destroy(btn.gameObject);
        choiceButtons.Clear();

        for (int i = 0; i < eventData.choices.Count; i++)
        {
            int capturedIndex = i;
            var choice        = eventData.choices[i];

            var btn  = Instantiate(choiceButtonPrefab, choiceButtonRoot);
            var text = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                string goldSuffix = choice.requiresGold ? $" ({choice.goldCost}G)" : "";
                text.text = choice.label + goldSuffix;
            }

            // 골드 부족 시 버튼 비활성화
            bool canAfford = !choice.requiresGold ||
                             GameManager.Instance.PlayerGold >= choice.goldCost;
            btn.interactable = canAfford;

            btn.onClick.AddListener(() => EventManager.Instance.SelectChoice(capturedIndex));
            choiceButtons.Add(btn);
        }
    }

    // ── 카드 제거 UI (RemoveCard 효과) ───────────────────

    // 실제 카드 목록을 버튼으로 나열 — 선택 시 EventManager.RemoveSelectedCard() 호출
    private void ShowCardRemovalUI(List<CardData> cards)
    {
        foreach (var btn in choiceButtons) Destroy(btn.gameObject);
        choiceButtons.Clear();

        if (eventTitleText != null) eventTitleText.text = "제거할 카드 선택";
        if (eventDescText  != null) eventDescText.text  = "덱에서 카드 1장을 영구 제거합니다.";

        foreach (var card in cards)
        {
            var captured = card;
            var btn      = Instantiate(choiceButtonPrefab, choiceButtonRoot);
            var text     = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = $"{card.cardName} (cost {card.energyCost})";

            btn.onClick.AddListener(() => EventManager.Instance.RemoveSelectedCard(captured));
            choiceButtons.Add(btn);
        }
    }
}
