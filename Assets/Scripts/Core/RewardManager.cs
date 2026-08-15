using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 종류
public enum RewardKind
{
    Monster,   // 일반 몬스터 — 골드 + 카드 선택
    Elite,     // 정예       — 골드↑ + 카드 선택 + 렐릭
    Boss,      // 보스       — 골드↑↑ + 카드 선택 + 렐릭
    Treasure,  // 보물 노드  — 전투 없이 카드 선택 + 렐릭 (골드 없음)
}

// 한 번의 전투 보상 묶음
public class CombatReward
{
    public RewardKind      kind;
    public int             gold;
    public List<CardData>  cardChoices = new();  // 이 중 1장 선택
    public RelicData       relic;                // null이면 렐릭 없음
}

// 전투 승리 시 보상 산정 + 선택 처리
// 골드는 즉시 지급, 카드는 UI에서 고를 때까지 대기한다.
public class RewardManager : MonoBehaviour
{
    public static RewardManager Instance { get; private set; }

    [Header("보상 풀")]
    [SerializeField] private List<CardData>  rewardCardPool = new();
    [SerializeField] private List<RelicData> rewardRelicPool = new();

    [Header("골드")]
    [SerializeField] private int monsterGoldMin = 15;
    [SerializeField] private int monsterGoldMax = 30;
    [SerializeField] private int eliteGoldMin   = 40;
    [SerializeField] private int eliteGoldMax   = 70;
    [SerializeField] private int bossGoldMin    = 90;
    [SerializeField] private int bossGoldMax    = 130;

    [Header("카드 선택지")]
    [SerializeField] private int cardChoiceCount = 3;

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<CombatReward> OnRewardOffered;  // UI가 구독 → 보상 화면 표시
    public event Action               OnRewardClosed;   // 선택·건너뛰기 완료

    public CombatReward Current { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 보상 제시 ───────────────────────────────────────

    public void OfferCombatReward(RewardKind kind, ClassTag playerClass)
    {
        Current = BuildReward(kind, playerClass);

        // 골드는 선택할 게 없으므로 즉시 지급
        if (Current.gold > 0)
            GameManager.Instance?.AddPlayerGold(Current.gold);

        // 렐릭도 즉시 지급 (선택지가 아니라 확정 보상)
        if (Current.relic != null)
            RelicManager.Instance?.AddPlayerRelic(Current.relic);

        OnRewardOffered?.Invoke(Current);

        // 고를 카드가 없으면 기다릴 이유가 없다
        if (Current.cardChoices.Count == 0)
            Close();
    }

    private CombatReward BuildReward(RewardKind kind, ClassTag playerClass)
    {
        var r = new CombatReward { kind = kind };

        r.gold = kind switch
        {
            RewardKind.Elite    => UnityEngine.Random.Range(eliteGoldMin,   eliteGoldMax   + 1),
            RewardKind.Boss     => UnityEngine.Random.Range(bossGoldMin,    bossGoldMax    + 1),
            RewardKind.Treasure => 0,   // 보물은 골드 대신 카드·렐릭
            _                   => UnityEngine.Random.Range(monsterGoldMin, monsterGoldMax + 1),
        };

        r.cardChoices = PickCards(playerClass, cardChoiceCount);

        if (kind != RewardKind.Monster)
            r.relic = PickRelic();

        return r;
    }

    // 플레이어 직업 + 범용 카드만 후보로. 중복 없이 뽑는다.
    private List<CardData> PickCards(ClassTag playerClass, int count)
    {
        var pool = new List<CardData>();
        foreach (var c in rewardCardPool)
        {
            if (c == null) continue;
            if (c.classTag == playerClass || c.classTag == ClassTag.Universal)
                pool.Add(c);
        }

        var picks = new List<CardData>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            picks.Add(pool[idx]);
            pool.RemoveAt(idx);   // 같은 보상 안에서 중복 방지
        }
        return picks;
    }

    private RelicData PickRelic()
    {
        var owned = RelicManager.Instance;
        var pool  = new List<RelicData>();

        foreach (var r in rewardRelicPool)
        {
            if (r == null) continue;
            if (owned != null && owned.PlayerHasRelic(r)) continue;  // 중복 획득 방지
            pool.Add(r);
        }

        if (pool.Count == 0) return null;
        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    // ── 선택 처리 (UI가 호출) ───────────────────────────

    public void SelectCard(CardData card)
    {
        if (card != null)
            GameManager.Instance?.Player?.DeckManager.AddCard(card);
        Close();
    }

    public void Skip() => Close();

    private void Close()
    {
        Current = null;
        OnRewardClosed?.Invoke();
    }
}
