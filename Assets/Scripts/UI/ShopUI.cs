using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 상점 5섹션 UI — ShopManager 구독
// 섹션별 아이템 버튼을 동적 생성, 구매 버튼 클릭 시 ShopManager로 전달
public class ShopUI : MonoBehaviour
{
    [Header("섹션 루트 (아이템 버튼을 여기에 생성)")]
    [SerializeField] private Transform cardSectionRoot;
    [SerializeField] private Transform relicSectionRoot;
    [SerializeField] private Transform potionSectionRoot;
    [SerializeField] private Transform companionSectionRoot;
    [SerializeField] private Transform bossItemSectionRoot;   // 보스 전용 (비표시 or 잠금)

    [Header("카드 제거 서비스")]
    [SerializeField] private Button          removeCardButton;
    [SerializeField] private TextMeshProUGUI removeCardPriceText;

    [Header("공용 아이템 버튼 프리팹")]
    [SerializeField] private Button shopItemPrefab;  // 버튼 하나 — 이름 + 가격 텍스트

    [Header("닫기 버튼")]
    [SerializeField] private Button closeButton;

    private void OnEnable()
    {
        ShopManager.Instance.OnShopRefreshed += RefreshUI;
        closeButton?.onClick.AddListener(CloseShop);
        removeCardButton?.onClick.AddListener(OnRemoveCardClicked);

        // 가격 표시
        if (removeCardPriceText != null)
            removeCardPriceText.text = $"{ShopManager.Instance.CardRemovePrice}G";
    }

    private void OnDisable()
    {
        if (ShopManager.Instance != null)
            ShopManager.Instance.OnShopRefreshed -= RefreshUI;

        closeButton?.onClick.RemoveListener(CloseShop);
        removeCardButton?.onClick.RemoveListener(OnRemoveCardClicked);
    }

    // ── 전체 UI 갱신 ─────────────────────────────────────

    private void RefreshUI()
    {
        var shop = ShopManager.Instance;

        BuildCardButtons(shop);
        BuildRelicButtons(shop);
        BuildPotionButtons(shop);
        BuildCompanionButtons(shop);
        RefreshRemoveButton();
    }

    // ── 섹션별 버튼 생성 ─────────────────────────────────

    private void BuildCardButtons(ShopManager shop)
    {
        ClearChildren(cardSectionRoot);
        foreach (var item in shop.Cards)
        {
            var captured = item;
            CreateItemButton(cardSectionRoot, RarityTable.Colorize(item.data.cardName, item.data.rarity), item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyCard(captured)) RefreshUI();
                },
                tooltip: item.data.description);
        }
    }

    private void BuildRelicButtons(ShopManager shop)
    {
        ClearChildren(relicSectionRoot);
        foreach (var item in shop.Relics)
        {
            var captured = item;
            // 렐릭은 방문당 1개 — 하나 사면 나머지는 잠긴다 (선점 경쟁)
            CreateItemButton(relicSectionRoot, RarityTable.Colorize(item.data.relicName, item.data.rarity), item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyRelic(captured)) RefreshUI();
                },
                locked: !shop.CanBuyRelicThisVisit,
                tooltip: item.data.description + "\n\n렐릭은 방문당 1개. 보스가 먼저 오면 사 간다.");
        }
    }

    private void BuildPotionButtons(ShopManager shop)
    {
        ClearChildren(potionSectionRoot);
        foreach (var item in shop.Potions)
        {
            var captured = item;
            CreateItemButton(potionSectionRoot, item.data.cardName, item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyPotion(captured)) RefreshUI();
                },
                tooltip: item.data.description + "\n\n포션은 품절되지 않는다. 소지 한도까지 살 수 있다.");
        }
    }

    private void BuildCompanionButtons(ShopManager shop)
    {
        ClearChildren(companionSectionRoot);
        foreach (var item in shop.Companions)
        {
            var captured = item;
            CreateItemButton(companionSectionRoot, item.data.companionName, item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyCompanion(captured)) RefreshUI();
                },
                tooltip: $"HP {item.data.maxHp}\n{item.data.description}\n\n산 동료는 대기열로 간다. 편성창에서 배치할 것.");
        }
    }

    // ── 카드 제거 서비스 ─────────────────────────────────

    // 공용 카드 제거 창(GameManager.OpenRemovePicker)을 연다. 골드는 고른 뒤에 받는다
    private void OnRemoveCardClicked()
    {
        var shop = ShopManager.Instance;
        if (shop == null || !shop.CanRemoveCardThisVisit) return;

        GameManager.Instance.OpenRemovePicker($"덱에서 뺄 카드를 고르세요 ({shop.CardRemovePrice}골드)",
            shop.CardRemovePrice, onDone: RefreshUI, onCancel: null);
    }

    private void RefreshRemoveButton()
    {
        var shop = ShopManager.Instance;
        if (removeCardButton == null || shop == null) return;
        removeCardButton.interactable = shop.CanRemoveCardThisVisit
                                     && GameManager.Instance.PlayerGold >= shop.CardRemovePrice
                                     && GameManager.Instance.Player.DeckManager.CanRemoveCard;
        if (removeCardPriceText != null)
            removeCardPriceText.text = shop.CanRemoveCardThisVisit ? $"{shop.CardRemovePrice}G" : "완료";
    }

    // ── 유틸 ────────────────────────────────────────────

    private void CreateItemButton(Transform root, string label, int price, bool sold,
                                  System.Action onClick, bool locked = false, string tooltip = null)
    {
        var btn  = Instantiate(shopItemPrefab, root);
        TooltipTrigger.Attach(btn.gameObject, label, tooltip);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = sold   ? $"{label}\n[매진]"
                      : locked ? $"{label}\n[이번 방문 구매 완료]"
                      :          $"{label}\n{price}G";

        btn.interactable = !sold && !locked && GameManager.Instance.PlayerGold >= price;
        btn.onClick.AddListener(() => onClick());
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }

    private void CloseShop() => UIManager.Instance?.HideShop();
}
