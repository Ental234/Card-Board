using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 카드 페이즈 UI
//
// 주사위를 굴리기 전, 보드 카드를 써서 이번 턴에 개입할 기회를 준다.
// "주사위 굴리기"를 누르면 페이즈가 끝나고 이동이 시작된다.
public class BoardCardPanelUI : MonoBehaviour
{
    [SerializeField] private BoardPhaseManager boardPhaseManager;

    [Header("표시")]
    [SerializeField] private GameObject      root;         // 카드 페이즈 동안만 켜진다
    [SerializeField] private HandUI          handUI;       // 보드 손패 (전투 손패와 별도 인스턴스)
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI diceText;     // 굴린 결과 표시
    [SerializeField] private Button          rollButton;

    private BoardCardManager deck;

    private void OnEnable()
    {
        boardPhaseManager.OnBoardCardPhaseStarted += HandlePhaseStarted;
        boardPhaseManager.OnBoardCardPhaseEnded   += HandlePhaseEnded;
        boardPhaseManager.OnDiceRolled            += HandleDiceRolled;

        if (handUI != null) handUI.OnCardSelected += HandleCardSelected;
        rollButton?.onClick.AddListener(OnRollClicked);

        root?.SetActive(false);
    }

    private void OnDisable()
    {
        boardPhaseManager.OnBoardCardPhaseStarted -= HandlePhaseStarted;
        boardPhaseManager.OnBoardCardPhaseEnded   -= HandlePhaseEnded;
        boardPhaseManager.OnDiceRolled            -= HandleDiceRolled;

        if (handUI != null) handUI.OnCardSelected -= HandleCardSelected;
        rollButton?.onClick.RemoveListener(OnRollClicked);

        Unbind();
    }

    // ── 페이즈 흐름 ─────────────────────────────────────

    private void HandlePhaseStarted()
    {
        var player = GameManager.Instance?.Player;
        if (player == null) return;

        Bind(player.BoardCardManager);

        root?.SetActive(true);
        if (rollButton != null) rollButton.interactable = true;
        if (diceText   != null) diceText.text = "";

        Refresh();
    }

    private void HandlePhaseEnded()
    {
        if (rollButton != null) rollButton.interactable = false;
        root?.SetActive(false);
        Unbind();
    }

    private void HandleDiceRolled(int value)
    {
        if (diceText != null) diceText.text = $"주사위 {value}";
    }

    // ── 덱 구독 ─────────────────────────────────────────

    private void Bind(BoardCardManager d)
    {
        Unbind();
        deck = d;
        if (deck == null) return;

        deck.OnHandChanged        += OnHandChanged;
        deck.OnBoardEnergyChanged += OnEnergyChanged;
    }

    private void Unbind()
    {
        if (deck == null) return;
        deck.OnHandChanged        -= OnHandChanged;
        deck.OnBoardEnergyChanged -= OnEnergyChanged;
        deck = null;
    }

    private void OnHandChanged(List<CardData> hand)
    {
        if (deck == null) return;
        handUI?.RefreshHand(hand, deck.CurrentBoardEnergy);
    }

    private void OnEnergyChanged(int current, int max)
    {
        handUI?.UpdateEnergy(current);
        RefreshEnergyText(current, max);
    }

    private void Refresh()
    {
        if (deck == null) return;

        handUI?.RefreshHand(new List<CardData>(deck.Hand), deck.CurrentBoardEnergy);
        RefreshEnergyText(deck.CurrentBoardEnergy, deck.MaxBoardEnergy);
    }

    private void RefreshEnergyText(int current, int max)
    {
        if (energyText != null) energyText.text = $"보드 에너지 {current}/{max}";
    }

    // ── 입력 ────────────────────────────────────────────

    private void HandleCardSelected(CardData card)
    {
        // 효과 실행은 BoardPhaseManager가 담당 (GameManager 경유)
        GameManager.Instance?.TryUseBoardCard(card);
    }

    private void OnRollClicked()
    {
        if (rollButton != null) rollButton.interactable = false;
        boardPhaseManager.RequestDiceRoll();
    }
}
