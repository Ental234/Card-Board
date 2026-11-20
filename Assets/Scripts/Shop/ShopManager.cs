using System;
using System.Collections.Generic;
using UnityEngine;

// 상점 5섹션 관리
// Cards(플레이어 전용), Relics(공유 — 선점 긴장감), Potions(플레이어 전용),
// Companions(플레이어 전용), BossItems(보스 전용)
// 보스 AI는 Relics 섹션에서만 구매, 1회 방문당 maxBossRelicPurchases 제한
// 재고는 상점 노드마다 독립이다 (v0.6)
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
    [SerializeField] private int maxBossRelicPurchases = 1;  // 보스가 1회 방문당 구매 가능한 렐릭 수 (플레이어도 1개)

    [Header("카드 풀")]
    [SerializeField] private List<CardData>      allCardPool      = new();
    [SerializeField] private List<RelicData>     allRelicPool     = new();
    [SerializeField] private List<CardData>      allPotionPool    = new();
    [SerializeField] private List<CompanionData> allCompanionPool = new();
    [SerializeField] private List<RelicData>     allBossItemPool  = new();  // 보스 전용 아이템

    // ── 이벤트 ──────────────────────────────────────────

    public event Action OnShopRefreshed;   // UI가 구독 → 재고 목록 갱신

    // ── 재고 — 상점 노드마다 독립 (v0.6) ─────────────────
    //
    // 한 스테이지에 상점이 여럿이고 조우지점(보급소)도 상점 역할을 하므로,
    // 재고를 전역으로 두면 뒤쪽 상점이 텅 빈다. 노드 인덱스별로 따로 들고 있는다.
    //
    // 재고는 **그 노드를 처음 열 때 한 번** 뽑고 스테이지가 끝날 때까지 유지한다.
    // ★ 렐릭을 다시 뽑으면 선점이 성립하지 않는다 —
    //   보스가 사간 뒤 플레이어가 열었을 때 재고가 되살아나 "먼저 사갔다"가 없던 일이 된다.
    //   보급소에서 대기 턴마다 다시 여는 경우에도 같은 재고를 봐야 한다.
    //
    // 스테이지가 바뀌면 ResetStock()으로 전부 비운다 (노드 인덱스가 새 보드에서 재사용되므로).

    private class NodeStock
    {
        public readonly List<ShopItem<CardData>>      Cards      = new();
        public readonly List<ShopItem<RelicData>>     Relics     = new();
        public readonly List<ShopItem<CardData>>      Potions    = new();
        public readonly List<ShopItem<CompanionData>> Companions = new();
        public readonly List<ShopItem<RelicData>>     BossItems  = new();
    }

    private readonly Dictionary<int, NodeStock> stocks = new();
    private NodeStock current = new();   // 플레이어가 지금 보고 있는 상점

    public List<ShopItem<CardData>>      Cards      => current.Cards;
    public List<ShopItem<RelicData>>     Relics     => current.Relics;
    public List<ShopItem<CardData>>      Potions    => current.Potions;
    public List<ShopItem<CompanionData>> Companions => current.Companions;
    public List<ShopItem<RelicData>>     BossItems  => current.BossItems;

    // 렐릭은 방문당 1개 (양측). 선점의 긴장은 "어느 하나를 먼저 집느냐"에 있다 —
    // 쓸어담을 수 있으면 골드 많은 쪽이 다 가져가고 경쟁이 사라진다.
    private bool playerBoughtRelicThisVisit;
    public  bool CanBuyRelicThisVisit => !playerBoughtRelicThisVisit;

    // 카드 제거도 방문당 1회 (슬더스 관례)
    private bool removedCardThisVisit;
    public  bool CanRemoveCardThisVisit => !removedCardThisVisit;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 방문 ─────────────────────────────────────────────

    // 플레이어가 상점을 연다 (상점 노드 착지 / 보급소 이용)
    public void OpenShop(ClassTag playerClassTag, int nodeIndex)
    {
        current = GetOrCreateStock(nodeIndex, playerClassTag);
        playerBoughtRelicThisVisit = false;
        removedCardThisVisit       = false;
        OnShopRefreshed?.Invoke();
    }

    // 보스가 상점에 도착 — 그 노드의 렐릭을 가져간다. 샀으면 true.
    public bool BossVisitShop(int nodeIndex)
    {
        var  stock  = GetOrCreateStock(nodeIndex, CurrentPlayerClass());
        bool bought = BossAiBuyRelic(stock);

        // 플레이어가 같은 상점을 보고 있다면 팔린 표시를 갱신한다
        if (bought && stock == current) OnShopRefreshed?.Invoke();
        return bought;
    }

    // 이 노드에 보스가 살 렐릭이 남아 있는가 (보스 대기 행동 판단용)
    public bool HasUnsoldRelic(int nodeIndex)
    {
        var stock = GetOrCreateStock(nodeIndex, CurrentPlayerClass());
        foreach (var item in stock.Relics)
            if (!item.sold) return true;
        return false;
    }

    // 스테이지 전환 시 호출 — 다음 스테이지는 새 재고로 시작한다
    public void ResetStock()
    {
        stocks.Clear();
        current = new NodeStock();
        playerBoughtRelicThisVisit = false;
    }

    private NodeStock GetOrCreateStock(int nodeIndex, ClassTag playerClass)
    {
        if (stocks.TryGetValue(nodeIndex, out var stock)) return stock;

        stock = new NodeStock();

        // 카드: 플레이어 직업 + Universal 필터, 메타 해금 전인 카드 제외
        var filteredCards = allCardPool.FindAll(c =>
            (c.classTag == playerClass || c.classTag == ClassTag.Universal)
            && MetaProgressionManager.IsAvailable(c));

        // 카드·렐릭은 등급 가중 추첨 + 등급별 가격 (3단계 6번).
        // 물약은 CardData라 rarity 필드가 있지만, 물약 등급은 아직 정하지 않아 기본가 균등 추첨을 유지한다
        FillRaritySlots(stock.Cards,  filteredCards, cardSlots,  c => c.rarity, RarityTable.ShopCards,
                        c => RarityTable.CardPrice(cardBasePrice, c.rarity));
        FillRaritySlots(stock.Relics, allRelicPool,  relicSlots, r => r.rarity, RarityTable.Relics,
                        r => RarityTable.RelicPrice(relicBasePrice, r.rarity));
        FillSlots(stock.Potions,    allPotionPool,    potionSlots,    potionBasePrice);
        FillSlots(stock.Companions, allCompanionPool, companionSlots, companionBasePrice);
        FillSlots(stock.BossItems,  allBossItemPool,  bossItemSlots,  bossItemBasePrice);

        stocks[nodeIndex] = stock;
        return stock;
    }

    private static ClassTag CurrentPlayerClass()
        => GameManager.Instance != null && GameManager.Instance.Player != null
         ? GameManager.Instance.Player.ClassTag
         : ClassTag.Universal;

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

    // 가격은 재고를 뽑을 때 한 번 정해 ShopItem.price에 박는다 — 화면 가격과 차감액이 같은 값을 읽는다
    private static void FillRaritySlots<T>(List<ShopItem<T>> target, List<T> pool, int slots,
                                           Func<T, Rarity> rarityOf, RarityTable.Weights weights,
                                           Func<T, int> priceOf)
        where T : UnityEngine.Object
    {
        var remaining = pool.FindAll(x => x != null);
        for (int i = 0; i < slots; i++)
        {
            var picked = RarityTable.TakeWeighted(remaining, rarityOf, weights);
            if (picked == null) break;
            target.Add(new ShopItem<T>(picked, priceOf(picked)));
        }
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
        if (item.sold || playerBoughtRelicThisVisit) return false;
        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        RelicManager.Instance?.AddPlayerRelic(item.data);
        item.sold = true;
        playerBoughtRelicThisVisit = true;
        return true;
    }

    // 포션은 덱이 아니라 소지품으로 간다.
    // 소지 상한 검사를 골드 차감 **앞**에 둔다 — 순서가 반대면 가득 찬 상태에서
    // 돈만 나가고 포션은 안 들어온다.
    //
    // 포션은 **품절되지 않는다** (v0.6) — 조우 직전에 사서 바로 쓰는 것이 선착 보상의 핵심 값이다.
    // 소지 상한 3이 자연스러운 뚜껑 역할을 한다.
    public bool TryBuyPotion(ShopItem<CardData> item)
    {
        var potions = GameManager.Instance.Player.PotionManager;
        if (potions.IsFull) return false;

        if (!GameManager.Instance.SpendPlayerGold(item.price)) return false;

        potions.TryAdd(item.data);
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

    // 보스 상점 방문 시 자동 실행 — 렐릭 섹션만, 방문당 maxBossRelicPurchases개.
    // 포션은 사지 않는다 (v0.6 — 포션은 플레이어의 전술 자원).
    private bool BossAiBuyRelic(NodeStock stock)
    {
        int bought = 0;

        foreach (var item in stock.Relics)
        {
            if (bought >= maxBossRelicPurchases) break;
            if (item.sold) continue;

            // 보스는 골드 차감 없이 렐릭 획득 — 보스 골드 입수 경로는 미결정 (GDD 11·15장)
            RelicManager.Instance?.AddBossRelic(item.data);
            item.sold = true;
            bought++;
        }
        return bought > 0;
    }

    // 카드 제거 서비스 (상점에서 카드 슬리밍)
    [Header("카드 제거 서비스")]
    [SerializeField] private int cardRemovePrice = 75;

    public bool TryRemoveCard(CardData card)
    {
        if (removedCardThisVisit) return false;
        if (card == null || card.IsCurse) return false;   // 저주는 일반 제거 대상이 아니다 (창에서도 막지만 여기서 다시 확인)
        var deck = GameManager.Instance.Player.DeckManager;
        if (!deck.CanRemoveCard) return false;
        if (!GameManager.Instance.SpendPlayerGold(cardRemovePrice)) return false;

        deck.RemoveCard(card);
        removedCardThisVisit = true;
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
