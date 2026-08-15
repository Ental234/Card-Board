using System;
using System.Collections.Generic;
using UnityEngine;

// 상점 5섹션 관리
// Cards(플레이어 전용), Relics(공유 — 선점 긴장감), Potions(플레이어 전용),
// Companions(플레이어 전용), BossItems(보스 전용)
// 보스 AI는 Relics 섹션에서만 구매, 1회 방문당 maxBossRelicPurchases 제한
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    // ── 설정 ────────────────────────────────────────────

    [Header("재고 설정")]
    [SerializeField] private int cardSlots      = 4;   // 카드 섹션 슬롯 수
    [SerializeField] private int relicSlots     = 2;
    [SerializeField] private int potionSlots    = 2;
    [SerializeField] private int companionSlots = 1;
    [SerializeField] private int bossItemSlots  = 2;

    [Header("가격 설정")]
    [SerializeField] private int cardBasePrice      = 50;
    [SerializeField] private int relicBasePrice     = 80;
    [SerializeField] private int potionBasePrice    = 30;
    [SerializeField] private int companionBasePrice = 60;
    [SerializeField] private int bossItemBasePrice  = 60;

    [Header("보스 AI")]
    [SerializeField] private int maxBossRelicPurchases = 1;  // 보스가 1회 방문당 구매 가능한 렐릭 수

    [Header("카드 풀")]
    [SerializeField] private List<CardData>      allCardPool      = new();
    [SerializeField] private List<RelicData>     allRelicPool     = new();
    [SerializeField] private List<CardData>      allPotionPool    = new();
    [SerializeField] private List<CompanionData> allCompanionPool = new();
    [SerializeField] private List<RelicData>     allBossItemPool  = new();  // 보스 전용 아이템

    // ── 이벤트 ──────────────────────────────────────────

    public event Action OnShopRefreshed;   // UI가 구독 → 재고 목록 갱신

    // ── 현재 재고 ────────────────────────────────────────

    public List<ShopItem<CardData>>      Cards      { get; } = new();
    public List<ShopItem<RelicData>>     Relics     { get; } = new();
    public List<ShopItem<CardData>>      Potions    { get; } = new();
    public List<ShopItem<CompanionData>> Companions { get; } = new();
    public List<ShopItem<RelicData>>     BossItems  { get; } = new();

    private ClassTag playerClass;
    private int      bossRelicPurchasesThisVisit;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 재고 생성 ────────────────────────────────────────

    // 플레이어가 상점 칸에 착지할 때 호출
    public void OpenShop(ClassTag playerClassTag)
    {
        playerClass = playerClassTag;
        bossRelicPurchasesThisVisit = 0;

        RefreshInventory();
        OnShopRefreshed?.Invoke();
    }

    // 보스가 상점 칸에 착지할 때 호출 (재고는 동일, 보스 AI 구매만 실행)
    public void BossVisitShop()
    {
        bossRelicPurchasesThisVisit = 0;
        BossAiBuyRelics();
    }

    private void RefreshInventory()
    {
        Cards.Clear();
        Relics.Clear();
        Potions.Clear();
        Companions.Clear();
        BossItems.Clear();

        // 카드: 플레이어 직업 + Universal 필터
        var filteredCards = allCardPool.FindAll(c =>
            c.classTag == playerClass || c.classTag == ClassTag.Universal);
        FillSlots(Cards, filteredCards, cardSlots, cardBasePrice);

        FillSlots(Relics,     allRelicPool,     relicSlots,     relicBasePrice);
        FillSlots(Potions,    allPotionPool,    potionSlots,    potionBasePrice);
        FillSlots(Companions, allCompanionPool, companionSlots, companionBasePrice);
        FillSlots(BossItems,  allBossItemPool,  bossItemSlots,  bossItemBasePrice);
    }

    private void FillSlots<T>(List<ShopItem<T>> target, List<T> pool, int slots, int basePrice)
        where T : UnityEngine.Object
    {
        var shuffled = new List<T>(pool);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        for (int i = 0; i < Mathf.Min(slots, shuffled.Count); i++)
            target.Add(new ShopItem<T>(shuffled[i], basePrice));
    }

    // ── 구매 — 플레이어 ──────────────────────────────────

    public bool TryBuyCard(ShopItem<CardData> item)
    {
        if (item.sold) return false;
        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        GameManager.Instance.Player.DeckManager.AddCard(item.data);
        item.sold = true;
        return true;
    }

    public bool TryBuyRelic(ShopItem<RelicData> item)
    {
        if (item.sold) return false;
        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        RelicManager.Instance?.AddPlayerRelic(item.data);
        item.sold = true;
        return true;
    }

    public bool TryBuyPotion(ShopItem<CardData> item)
    {
        if (item.sold) return false;
        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        // 포션 즉시 사용 or 인벤토리 보관 — 추후 PotionManager 연결
        GameManager.Instance.Player.DeckManager.AddCard(item.data);
        item.sold = true;
        return true;
    }

    public bool TryBuyCompanion(ShopItem<CompanionData> item)
    {
        if (item.sold) return false;
        if (!GameManager.Instance.TryAddCompanion(item.data)) return false;
        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        item.sold = true;
        return true;
    }

    // ── 구매 — 보스 AI ───────────────────────────────────

    // 보스 상점 방문 시 자동 실행 — 렐릭 섹션만, 최대 maxBossRelicPurchases개
    private void BossAiBuyRelics()
    {
        int bought = 0;

        foreach (var item in Relics)
        {
            if (bought >= maxBossRelicPurchases) break;
            if (item.sold) continue;

            // 보스는 골드 차감 없이 렐릭 획득 (보스 골드 시스템은 단순화)
            RelicManager.Instance?.AddBossRelic(item.data);
            item.sold = true;
            bought++;
            bossRelicPurchasesThisVisit++;
        }
    }

    // 카드 제거 서비스 (상점에서 카드 슬리밍)
    [Header("카드 제거 서비스")]
    [SerializeField] private int cardRemovePrice = 75;

    public bool TryRemoveCard(CardData card)
    {
        if (!GameManager.Instance.SpendPlayerGold(cardRemovePrice)) return false;
        GameManager.Instance.Player.DeckManager.RemoveCard(card);
        return true;
    }

    public int CardRemovePrice => cardRemovePrice;
}

// ── 상점 아이템 래퍼 ─────────────────────────────────────

[Serializable]
public class ShopItem<T>
{
    public T    data;
    public int  price;
    public bool sold;

    public ShopItem(T data, int price)
    {
        this.data  = data;
        this.price = price;
        sold       = false;
    }
}
