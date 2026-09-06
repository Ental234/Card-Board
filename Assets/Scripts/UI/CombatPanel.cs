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

        // 슬롯이 바뀌면 사거리가 바뀌므로 손패의 사용 가능 표시를 다시 칠한다
        slotSystem.OnSlotsChanged += handUI.RefreshUsability;
        handUI.ExtraUsableCheck    = CanPlayFromCurrentPosition;

        endTurnButton.onClick.AddListener(OnEndTurnClicked);
    }

    private void OnDisable()
    {
        combatManager.OnCombatStart     -= HandleCombatStart;
        combatManager.OnCombatEnd       -= HandleCombatEnd;
        combatManager.OnEntityTurnBegin -= HandleTurnBegin;

        slotGridUI.OnTargetSelected -= HandleTargetSelected;
        handUI.OnCardSelected       -= HandleCardSelected;

        slotSystem.OnSlotsChanged -= handUI.RefreshUsability;
        handUI.ExtraUsableCheck    = null;

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

    // ── 사용 가능 판정 ───────────────────────────────────

    // 에너지만으로는 부족하다 — 지금 서 있는 슬롯에서 실제로 나갈 수 있는 카드인지 본다.
    // 판정 식은 CombatManager.ResolveTargets와 같아야 한다.
    // 어긋나면 "밝게 보이는데 눌러도 안 나가는" 카드가 다시 생긴다.
    private bool CanPlayFromCurrentPosition(CardData card)
    {
        if (player == null || card == null) return true;   // 전투 밖에서는 막지 않는다
        if (card.targetSelf)                return true;

        return slotSystem.GetValidTargets(player, card).Count > 0;
    }

    // ── 카드 선택 → 타겟 흐름 ───────────────────────────

    private void HandleCardSelected(CardData card)
    {
        if (player == null) return;
        slotGridUI.BeginTargeting(card, player, slotSystem);

        // 논타겟 카드(AoE·autoTarget)는 BeginTargeting에서 이미 발동됐다 — 클릭을 기다리지 않는다
        waitingForTarget = !card.isAoe && !card.autoTarget && !card.targetSelf;
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
