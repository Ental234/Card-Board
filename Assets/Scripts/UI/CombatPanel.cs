using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 화면 총괄 — CombatManager 이벤트 구독 + 입력 연결
// CombatSlotGridUI, HandUI, 에너지/행동력 표시, 턴 종료 버튼
public class CombatPanel : MonoBehaviour
{
    [SerializeField] private CombatManager    combatManager;
    [SerializeField] private SlotSystem       slotSystem;
    [SerializeField] private CombatSlotGridUI slotGridUI;
    [SerializeField] private HandUI           handUI;

    [Header("HUD 요소")]
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI actionPointText;
    [SerializeField] private Button          endTurnButton;

    private PlayerCharacter player;
    private bool            waitingForTarget;

    // ── 활성화 ──────────────────────────────────────────

    private void OnEnable()
    {
        combatManager.OnCombatStart     += HandleCombatStart;
        combatManager.OnCombatEnd       += HandleCombatEnd;
        combatManager.OnEntityTurnBegin += HandleTurnBegin;

        slotGridUI.OnTargetSelected += HandleTargetSelected;
        handUI.OnCardSelected       += HandleCardSelected;

        endTurnButton.onClick.AddListener(OnEndTurnClicked);
    }

    private void OnDisable()
    {
        combatManager.OnCombatStart     -= HandleCombatStart;
        combatManager.OnCombatEnd       -= HandleCombatEnd;
        combatManager.OnEntityTurnBegin -= HandleTurnBegin;

        slotGridUI.OnTargetSelected -= HandleTargetSelected;
        handUI.OnCardSelected       -= HandleCardSelected;

        endTurnButton.onClick.RemoveListener(OnEndTurnClicked);

        if (player != null)
        {
            player.DeckManager.OnHandChanged    -= OnHandChanged;
            player.DeckManager.OnEnergyChanged  -= OnEnergyChanged;
            player.Stats.OnApChanged            -= OnApChanged;
        }
    }

    // ── 전투 시작/종료 ───────────────────────────────────

    private void HandleCombatStart()
    {
        player = GameManager.Instance.Player;

        player.DeckManager.OnHandChanged   += OnHandChanged;
        player.DeckManager.OnEnergyChanged += OnEnergyChanged;
        player.Stats.OnApChanged           += OnApChanged;

        slotGridUI.BindCombat(slotSystem);
        endTurnButton.interactable = false;

        RefreshEnergy(player.DeckManager.CurrentEnergy, player.DeckManager.MaxEnergy);
        RefreshAp(player.Stats.CurrentActionPoints, player.Stats.MaxActionPoints);
    }

    private void HandleCombatEnd(bool _)
    {
        slotGridUI.UnbindAll();
        slotGridUI.CancelTargeting();
        endTurnButton.interactable = false;

        if (player == null) return;
        player.DeckManager.OnHandChanged   -= OnHandChanged;
        player.DeckManager.OnEnergyChanged -= OnEnergyChanged;
        player.Stats.OnApChanged           -= OnApChanged;
        player = null;
    }

    // ── 턴 흐름 ─────────────────────────────────────────

    private void HandleTurnBegin(CombatEntity entity)
    {
        bool isPlayerTurn = entity is PlayerCharacter;
        endTurnButton.interactable = isPlayerTurn;
    }

    private void OnEndTurnClicked()
    {
        slotGridUI.CancelTargeting();
        combatManager.EndPlayerTurn();
        endTurnButton.interactable = false;
    }

    // ── 카드 선택 → 타겟 흐름 ───────────────────────────

    private void HandleCardSelected(CardData card)
    {
        if (player == null) return;
        slotGridUI.BeginTargeting(card, player, slotSystem);
        waitingForTarget = !card.isAoe;
    }

    private void HandleTargetSelected(CardData card, CombatEntity user, CombatEntity target)
    {
        combatManager.TryPlayCard(card, user, target);
        waitingForTarget = false;
    }

    // ── 표시 갱신 ────────────────────────────────────────

    private void OnHandChanged(System.Collections.Generic.List<CardData> hand)
    {
        if (player == null) return;
        handUI.RefreshHand(hand, player.DeckManager.CurrentEnergy);
    }

    private void OnEnergyChanged(int current, int max)
    {
        handUI.UpdateEnergy(current);
        RefreshEnergy(current, max);
    }

    private void OnApChanged(int current, int max) => RefreshAp(current, max);

    private void RefreshEnergy(int cur, int max)
    {
        if (energyText != null) energyText.text = $"{cur}/{max}";
    }

    private void RefreshAp(int cur, int max)
    {
        if (actionPointText != null) actionPointText.text = $"AP {cur}/{max}";
    }
}
