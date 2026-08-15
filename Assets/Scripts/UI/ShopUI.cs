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
    }

    // ── 섹션별 버튼 생성 ─────────────────────────────────

    private void BuildCardButtons(ShopManager shop)
    {
        ClearChildren(cardSectionRoot);
        foreach (var item in shop.Cards)
        {
            var captured = item;
            CreateItemButton(cardSectionRoot, item.data.cardName, item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyCard(captured)) RefreshUI();
                });
        }
    }

    private void BuildRelicButtons(ShopManager shop)
    {
        ClearChildren(relicSectionRoot);
        foreach (var item in shop.Relics)
        {
            var captured = item;
            CreateItemButton(relicSectionRoot, item.data.relicName, item.price, item.sold,
                () =>
                {
                    if (shop.TryBuyRelic(captured)) RefreshUI();
                });
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
                });
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
                });
        }
    }

    // ── 카드 제거 서비스 ─────────────────────────────────

    private void OnRemoveCardClicked()
    {
        // EventUI의 카드 제거 UI 재사용 — EventManager를 통해 선택 UI 표시
        var allCards = GameManager.Instance.Player.DeckManager.GetAllCards();
        if (allCards.Count == 0) return;

        // 상점 카드 제거는 골드 차감 후 DeckManager.RemoveCard 직접 호출
        // 선택 UI는 EventManager의 RemoveCard 흐름 활용
        EventManager.Instance.RequestCardRemoval(allCards);
    }

    // ── 유틸 ────────────────────────────────────────────

    private void CreateItemButton(Transform root, string label, int price, bool sold,
                                  System.Action onClick)
    {
        var btn  = Instantiate(shopItemPrefab, root);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = sold ? $"{label}\n[매진]" : $"{label}\n{price}G";

        btn.interactable = !sold && GameManager.Instance.PlayerGold >= price;
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
