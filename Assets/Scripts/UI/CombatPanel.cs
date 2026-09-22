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

    // 포션 바는 인텐트 배지·상태이상 칩과 같이 런타임에 만든다 (프리팹 오버라이드 함정 회피)
    private PotionBarUI potionBar;

    // 타겟팅 중인 것이 카드인지 포션인지 구분한다.
    // 둘 다 CardData라 슬롯 클릭만 보고는 알 수 없다.
    private CardData pendingPotion;

    // 조우 전투 남은 턴 — 턴 제한이 있는 전투에서만 보인다 (런타임 생성)
    private TextMeshProUGUI roundText;

    // 더미 보기 버튼 (뽑을 / 버린 / 소모) — 런타임 생성, 장수는 매 프레임 비교해 바뀔 때만 고친다
    private TextMeshProUGUI drawPileLabel, discardPileLabel, exhaustPileLabel;
    private int shownDraw = -1, shownDiscard = -1, shownExhaust = -1;

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

        EnsurePotionBar();
        EnsurePileButtons();
        potionBar?.Bind(player.PotionManager, energyText != null ? energyText.font : null);

        RefreshEnergy(player.DeckManager.CurrentEnergy, player.DeckManager.MaxEnergy);
        RefreshAp(player.Stats.CurrentActionPoints, player.Stats.MaxActionPoints);
    }

    private void HandleCombatEnd(bool _)
    {
        slotGridUI.UnbindAll();
        slotGridUI.CancelTargeting();
        potionBar?.Unbind();
        pendingPotion = null;
        endTurnButton.interactable = false;
        if (roundText != null) roundText.gameObject.SetActive(false);
        DeckViewerUI.Close();

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

        if (isPlayerTurn) RefreshRoundText();
    }

    // 조우 전투는 2턴에 끊긴다. 남은 턴이 안 보이면 버스트를 언제 쏟을지 판단할 수 없다.
    private void RefreshRoundText()
    {
        if (combatManager.TurnLimit <= 0)
        {
            if (roundText != null) roundText.gameObject.SetActive(false);
            return;
        }

        EnsureRoundText();
        if (roundText == null) return;

        roundText.gameObject.SetActive(true);
        bool lastRound = combatManager.CurrentRound >= combatManager.TurnLimit;
        roundText.text  = lastRound
                        ? $"조우 전투 {combatManager.CurrentRound}/{combatManager.TurnLimit}턴 - 마지막 턴"
                        : $"조우 전투 {combatManager.CurrentRound}/{combatManager.TurnLimit}턴";
        roundText.color = lastRound ? new Color(1f, 0.55f, 0.35f) : new Color(1f, 0.9f, 0.6f);
    }

    private void EnsureRoundText()
    {
        if (roundText != null || endTurnButton == null) return;

        var go = new GameObject("RoundText", typeof(RectTransform));
        go.transform.SetParent(endTurnButton.transform.parent, false);

        var btnRt = endTurnButton.GetComponent<RectTransform>();
        var rt    = go.GetComponent<RectTransform>();
        rt.anchorMin        = btnRt.anchorMin;
        rt.anchorMax        = btnRt.anchorMax;
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(320f, 32f);
        rt.anchoredPosition = btnRt.anchoredPosition + new Vector2(0f, btnRt.rect.height * (1f - btnRt.pivot.y) + 8f);

        roundText = go.AddComponent<TextMeshProUGUI>();
        roundText.alignment     = TextAlignmentOptions.Center;
        roundText.fontSize      = 22;
        roundText.raycastTarget = false;
        if (energyText != null)
        {
            roundText.font               = energyText.font;
            roundText.fontSharedMaterial = energyText.fontSharedMaterial;
        }
    }

    private void OnEndTurnClicked()
    {
        slotGridUI.CancelTargeting();
        pendingPotion = null;
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

        pendingPotion = null;   // 포션 타겟팅 중에 카드를 고르면 포션 쪽을 버린다
        slotGridUI.BeginTargeting(card, player, slotSystem);

        // 논타겟 카드(AoE·autoTarget)는 BeginTargeting에서 이미 발동됐다 — 클릭을 기다리지 않는다
        waitingForTarget = !card.isAoe && !card.autoTarget && !card.targetSelf;
    }

    // 포션도 CardData라 타겟팅 흐름은 카드와 같다. 다른 것은 실행 함수뿐이다.
    private void HandlePotionSelected(CardData potion)
    {
        if (player == null || potion == null) return;

        pendingPotion = potion;
        slotGridUI.BeginTargeting(potion, player, slotSystem);
        waitingForTarget = !potion.isAoe && !potion.autoTarget && !potion.targetSelf;
    }

    private void HandleTargetSelected(CardData card, CombatEntity user, CombatEntity target)
    {
        // 포션은 에너지·덱을 거치지 않는 별도 경로로 보낸다
        if (pendingPotion != null && pendingPotion == card)
        {
            combatManager.TryUsePotion(card, target);
            pendingPotion = null;
        }
        else
        {
            combatManager.TryPlayCard(card, user, target);
        }

        waitingForTarget = false;
    }

    // ── 더미 보기 ────────────────────────────────────────
    //
    // 슬더스처럼 뽑을 더미·버린 더미·소모 더미를 언제든 열어볼 수 있게 한다.
    // 소모 카드가 실제로 소모 더미로 가는지 확인하는 수단이기도 하다 (3단계 소모 카드 검증용).

    private void EnsurePileButtons()
    {
        if (drawPileLabel != null) return;

        drawPileLabel    = CreatePileButton("DrawPileButton",    2, OpenDrawPile);
        discardPileLabel = CreatePileButton("DiscardPileButton", 1, OpenDiscardPile);
        exhaustPileLabel = CreatePileButton("ExhaustPileButton", 0, OpenExhaustPile);
        shownDraw = shownDiscard = shownExhaust = -1;
    }

    // 오른쪽 아래에 가로로 나란히. indexFromRight 0이 맨 오른쪽
    private TextMeshProUGUI CreatePileButton(string name, int indexFromRight, System.Action onClick)
    {
        const float w = 150f, h = 40f, gap = 8f;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-24f - indexFromRight * (w + gap), 20f);
        rt.sizeDelta        = new Vector2(w, h);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.18f, 0.15f, 0.95f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize      = 16;   // 16px 배수
        label.alignment     = TextAlignmentOptions.Center;
        label.color         = new Color(0.95f, 0.90f, 0.80f);
        label.raycastTarget = false;
        if (energyText != null) { label.font = energyText.font; label.fontSharedMaterial = energyText.fontSharedMaterial; }
        return label;
    }

    private void Update()
    {
        if (player == null || drawPileLabel == null) return;

        var deck = player.DeckManager;
        if (deck.DrawPileCount != shownDraw)
        {
            shownDraw = deck.DrawPileCount;
            drawPileLabel.text = $"뽑을 더미 {shownDraw}";
        }
        if (deck.DiscardPileCount != shownDiscard)
        {
            shownDiscard = deck.DiscardPileCount;
            discardPileLabel.text = $"버린 더미 {shownDiscard}";
        }
        if (deck.ExhaustPileCount != shownExhaust)
        {
            shownExhaust = deck.ExhaustPileCount;
            exhaustPileLabel.text = $"소모 {shownExhaust}";
        }
    }

    // 뽑을 더미는 순서가 곧 정보라 정렬해서 보여준다 — 다음에 뭐가 나올지 들키면 안 된다
    private void OpenDrawPile()
    {
        if (player == null) return;
        DeckViewerUI.Show(transform as RectTransform, "뽑을 더미",
            new DeckViewerUI.Section(null, DeckViewerUI.Sorted(player.DeckManager.DrawPile)));
    }

    private void OpenDiscardPile()
    {
        if (player == null) return;
        DeckViewerUI.Show(transform as RectTransform, "버린 더미",
            new DeckViewerUI.Section(null, DeckViewerUI.Sorted(player.DeckManager.DiscardPile)));
    }

    private void OpenExhaustPile()
    {
        if (player == null) return;
        DeckViewerUI.Show(transform as RectTransform, "소모 더미",
            new DeckViewerUI.Section(null, DeckViewerUI.Sorted(player.DeckManager.ExhaustPile)));
    }

    // 포션 바를 처음 한 번만 만든다. 전투가 끝나도 오브젝트는 남기고 Bind만 풀어
    // 다음 전투에서 다시 붙인다 (매 전투 생성·파괴를 피한다).
    private void EnsurePotionBar()
    {
        if (potionBar != null) return;

        var go = new GameObject("PotionBar", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        potionBar = go.AddComponent<PotionBarUI>();
        potionBar.OnPotionSelected += HandlePotionSelected;
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
