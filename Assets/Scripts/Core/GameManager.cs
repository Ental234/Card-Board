using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 최상위 게임 오케스트레이터
// 런 생명주기 · 스테이지 전환 · 보드↔전투 페이즈 전환 · 이벤트 라우팅
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── 씬 참조 (Inspector) ─────────────────────────────

    [SerializeField] private BoardPhaseManager       boardPhaseManager;
    [SerializeField] private CombatManager           combatManager;
    [SerializeField] private BoardGenerator          boardGenerator;
    [SerializeField] private MetaProgressionManager  metaProgressionManager;

    [Header("스테이지 설정")]
    [SerializeField] private BoardStageData[] stageDataList;  // [0]=S1, [1]=S2, [2]=S3
    [SerializeField] private BossData[]       bossDataList;   // 스테이지별 보스 데이터

    [Header("프리팹 템플릿")]
    [SerializeField] private PlayerCharacter  playerCharacter;      // 씬에 배치된 플레이어
    [SerializeField] private BossEntity       bossEntityTemplate;   // 씬에 배치된 보스 (비활성)
    [SerializeField] private MinionEntity     minionEntityTemplate;
    [SerializeField] private CompanionEntity  companionEntityTemplate;

    [Header("런 시작 설정")]
    [SerializeField] private CharacterData startingCharacter;   // 캐릭터 선택 구현 전까지 고정
    [SerializeField] private bool          skipMainMenu;        // 디버그용 — 켜면 메뉴 없이 바로 런 시작

    [Header("노드 효과")]
    [Range(0f, 1f)]
    [SerializeField] private float restHealPercent = 0.3f;  // 휴식: 최대 HP 대비 회복률
    [SerializeField] private int   curseHpLoss     = 8;      // 저주: 즉시 HP 감소

    [Header("일반 전투 적 구성")]
    [SerializeField] private int monsterNodeEnemyMin = 1;
    [SerializeField] private int monsterNodeEnemyMax = 2;
    [SerializeField] private int eliteNodeEnemyMin   = 2;
    [SerializeField] private int eliteNodeEnemyMax   = 3;

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int>       OnStageChanged;    // (새 스테이지 번호, 1-based)
    public event Action<bool>      OnRunEnded;        // (playerWon)

    // 노드 이벤트 — UI·EventManager 등이 구독해서 처리
    public event Action<int, NodeType> OnPlayerNodeEvent; // (nodeIndex, type)
    public event Action<int, NodeType> OnBossNodeEvent;

    // ── 런타임 상태 ─────────────────────────────────────

    public GamePhase CurrentPhase  { get; private set; } = GamePhase.Idle;
    public int       CurrentStage  { get; private set; } = 0;  // 0-indexed

    private BossEntity            currentBoss;
    private List<CompanionEntity> companions    = new();
    private List<MinionData>      bossMinions   = new();  // 보드 페이즈 중 보스가 축적한 하수인
    private bool                  isBossEncounter;        // 현재 전투가 보스 인카운터인지
    private bool                  lastCombatWasElite;     // 보상 등급 판정용

    // 보상 화면이 닫힌 뒤 실행할 후속 동작 (보드 재개 / 스테이지 전환 등)
    private Action pendingAfterReward;

    // ── Unity 생명주기 ───────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 한 프레임 늦춰서 첫 페이즈를 알린다.
    // UIManager 등 다른 컴포넌트의 Start()에서 이벤트 구독이 끝난 뒤여야
    // 첫 OnPhaseChanged 를 놓치지 않는다.
    private IEnumerator Start()
    {
        yield return null;

        if (skipMainMenu) StartRun();
        else              SetPhase(GamePhase.MainMenu);
    }

    // ── 메인 메뉴 진입점 ────────────────────────────────

    // 메인 메뉴의 "게임 시작" 버튼이 호출
    public void StartRun()
    {
        if (startingCharacter == null)
        {
            Debug.LogError("[GameManager] startingCharacter가 비어 있어 런을 시작할 수 없습니다.");
            return;
        }
        StartRun(startingCharacter);
    }

    // 보상 화면의 "메인 메뉴로" 버튼이 호출.
    // 씬을 다시 로드해 런 상태(덱·보드·소환된 유닛)를 통째로 초기화한다.
    // MetaProgressionManager는 DontDestroyOnLoad라 누적 포인트는 유지된다.
    public void ReturnToMainMenu()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
    }

    // 직전 런의 보상 내역 — 보상 화면이 읽어간다.
    public RunReward LastRunReward { get; private set; }

    // ── 런 시작 ─────────────────────────────────────────

    public void StartRun(CharacterData characterData)
    {
        CurrentStage = 0;
        companions.Clear();
        bossMinions.Clear();

        // 런 단위 자원 초기화 — 골드·렐릭은 스테이지를 넘어 유지되지만 런이 바뀌면 리셋된다
        boardPhaseManager.ResetRunResources();
        RelicManager.Instance?.ClearAll();

        playerCharacter.Initialize(characterData);
        playerCharacter.OnRunEnd += HandlePlayerRunEnd;

        StartStage();
    }

    private void StartStage()
    {
        bossMinions.Clear();
        isBossEncounter = false;   // 이전 스테이지의 보스전 상태가 남지 않도록

        // 보스 초기화
        if (currentBoss != null)
            Destroy(currentBoss.gameObject);

        currentBoss = Instantiate(bossEntityTemplate);
        currentBoss.Initialize(bossDataList[CurrentStage]);

        // 보드 초기화
        var stageData = stageDataList[CurrentStage];
        boardPhaseManager.InitBoard(stageData, playerCharacter, currentBoss, bossDataList[CurrentStage]);

        SubscribeBoardEvents();
        OnStageChanged?.Invoke(CurrentStage + 1);

        SetPhase(GamePhase.BoardPhase);
        boardPhaseManager.StartBoardTurn();
    }

    // ── 보드 이벤트 구독 ─────────────────────────────────

    private void SubscribeBoardEvents()
    {
        boardPhaseManager.OnEncounterTriggered  += HandleEncounter;
        boardPhaseManager.OnPlayerLanded        += HandlePlayerLanded;
        boardPhaseManager.OnBossLanded          += HandleBossLanded;
        boardPhaseManager.OnBossAcquiresMinion  += HandleBossAcquiresMinion;
    }

    private void UnsubscribeBoardEvents()
    {
        boardPhaseManager.OnEncounterTriggered  -= HandleEncounter;
        boardPhaseManager.OnPlayerLanded        -= HandlePlayerLanded;
        boardPhaseManager.OnBossLanded          -= HandleBossLanded;
        boardPhaseManager.OnBossAcquiresMinion  -= HandleBossAcquiresMinion;
    }

    // ── 보드 이벤트 핸들러 ───────────────────────────────

    // 플레이어와 보스가 같은 칸 → 인카운터(보스 전투)
    private void HandleEncounter()
    {
        isBossEncounter = true;

        var minionEntities = SpawnMinions(bossMinions);
        boardPhaseManager.BeginResolution();   // 전투가 끝날 때까지 보드 턴 정지
        SetPhase(GamePhase.CombatPhase);

        combatManager.OnCombatEnd += HandleCombatEnd;
        combatManager.StartCombat(playerCharacter, companions, currentBoss, minionEntities);
    }

    // 플레이어 노드 착지 — 노드 이벤트 라우팅
    private void HandlePlayerLanded(int nodeIndex, NodeType type)
    {
        // 이벤트·상점은 UI가 열리므로 닫힐 때까지 보드 턴을 멈춘다.
        // 해제는 UIManager가 NotifyNodeResolved()로 알린다.
        if (type == NodeType.Event || type == NodeType.Shop)
            boardPhaseManager.BeginResolution();

        // 보물은 보상 화면을 띄우므로 게이트가 필요하다
        if (type == NodeType.Treasure)
            boardPhaseManager.BeginResolution();

        OnPlayerNodeEvent?.Invoke(nodeIndex, type);

        switch (type)
        {
            // 몬스터·정예: 전투 시작
            case NodeType.Monster:
            case NodeType.Elite:
                StartNodeCombat(nodeIndex, type);
                break;

            case NodeType.Rest:     ApplyRest();     break;
            case NodeType.Curse:    ApplyCurse();    break;
            case NodeType.Treasure: OfferTreasure(); break;
        }
    }

    // ── 노드 즉시 효과 ───────────────────────────────────

    // 휴식: HP 회복. (카드 업그레이드는 업그레이드 시스템 구현 후 선택지로 추가)
    private void ApplyRest()
    {
        int amount = Mathf.Max(1, Mathf.RoundToInt(playerCharacter.Stats.MaxHp * restHealPercent));
        playerCharacter.Heal(amount);
    }

    // 저주: 즉시 HP 상실. 전투 밖의 일이므로 방어막·취약·피격 트리거를 타지 않는다.
    // (저주 카드 추가는 저주 카드 에셋 마련 후 확장)
    private void ApplyCurse()
    {
        playerCharacter.LoseHp(curseHpLoss);
    }

    // 보물: 전투 없이 카드 선택 + 렐릭. 보상 화면이 닫히면 보드가 이어진다.
    private void OfferTreasure()
    {
        pendingAfterReward = () => boardPhaseManager.EndResolution();
        OfferReward(RewardKind.Treasure);
    }

    // 이벤트·상점 UI가 닫혔음을 알린다 (UIManager가 호출)
    public void NotifyNodeResolved() => boardPhaseManager.EndResolution();

    private void HandleBossLanded(int nodeIndex, NodeType type)
    {
        OnBossNodeEvent?.Invoke(nodeIndex, type);
        // 상점 방문 등 추가 처리는 ShopManager 등 구독자가 담당
    }

    private void HandleBossAcquiresMinion(MinionData minionData)
    {
        bossMinions.Add(minionData);
    }

    // ── 노드 전투 (몬스터·정예 칸) ──────────────────────

    // 일반 몬스터 전투 — 스테이지 보스의 하수인 풀에서 적을 뽑아 구성한다.
    // 적이 하나도 없으면 전투가 끝나지 않으므로 최소 1마리는 보장한다.
    private void StartNodeCombat(int nodeIndex, NodeType type)
    {
        var pool = bossDataList[CurrentStage]?.minionPool;
        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning($"[GameManager] 스테이지 {CurrentStage + 1} 하수인 풀이 비어 전투를 건너뜁니다.");
            return;
        }

        bool isElite = type == NodeType.Elite;
        int  min     = isElite ? eliteNodeEnemyMin : monsterNodeEnemyMin;
        int  max     = isElite ? eliteNodeEnemyMax : monsterNodeEnemyMax;
        int  count   = Mathf.Clamp(UnityEngine.Random.Range(min, max + 1), 1, 4);

        var picks = new List<MinionData>();
        for (int i = 0; i < count; i++)
            picks.Add(pool[UnityEngine.Random.Range(0, pool.Count)]);

        var enemies = SpawnMinions(picks, reserveBossSlot: false);
        if (enemies.Count == 0)
        {
            Debug.LogWarning("[GameManager] 적 생성에 실패해 전투를 건너뜁니다.");
            return;
        }

        isBossEncounter    = false;
        lastCombatWasElite = isElite;
        boardPhaseManager.BeginResolution();   // 전투가 끝날 때까지 보드 턴 정지
        SetPhase(GamePhase.CombatPhase);

        combatManager.OnCombatEnd += HandleCombatEnd;
        combatManager.StartCombat(playerCharacter, companions, bossEntity: null, minionList: enemies);
    }

    // ── 전투 종료 핸들러 ─────────────────────────────────

    private void HandleCombatEnd(bool playerWon)
    {
        combatManager.OnCombatEnd -= HandleCombatEnd;

        if (!playerWon)
        {
            EndRun(playerWon: false);
            return;
        }

        // 보상을 먼저 보여주고, 화면이 닫히면 후속 동작을 실행한다.
        if (isBossEncounter)
        {
            pendingAfterReward = () =>
            {
                if (CurrentStage >= stageDataList.Length - 1)
                {
                    EndRun(playerWon: true);
                }
                else
                {
                    UnsubscribeBoardEvents();
                    boardPhaseManager.StopBoard();   // 이전 스테이지 턴 루프 정리
                    CurrentStage++;
                    StartStage();
                }
            };
            OfferReward(RewardKind.Boss);
        }
        else
        {
            pendingAfterReward = () =>
            {
                // 게이트를 열면 멈춰 있던 보드 턴이 이어서 진행된다.
                SetPhase(GamePhase.BoardPhase);
                boardPhaseManager.EndResolution();
            };
            OfferReward(lastCombatWasElite ? RewardKind.Elite : RewardKind.Monster);
        }
    }

    // ── 전투 보상 ────────────────────────────────────────

    private void OfferReward(RewardKind kind)
    {
        var rm = RewardManager.Instance;
        if (rm == null)
        {
            // 보상 시스템이 없으면 건너뛰고 바로 진행
            RunPendingAfterReward();
            return;
        }

        rm.OnRewardClosed += HandleRewardClosed;
        rm.OfferCombatReward(kind, playerCharacter.ClassTag);
    }

    private void HandleRewardClosed()
    {
        if (RewardManager.Instance != null)
            RewardManager.Instance.OnRewardClosed -= HandleRewardClosed;

        RunPendingAfterReward();
    }

    private void RunPendingAfterReward()
    {
        var next = pendingAfterReward;
        pendingAfterReward = null;
        next?.Invoke();
    }

    private void HandlePlayerRunEnd()
    {
        EndRun(playerWon: false);
    }

    // ── 런 종료 ─────────────────────────────────────────

    private void EndRun(bool playerWon)
    {
        UnsubscribeBoardEvents();
        boardPhaseManager.StopBoard();
        combatManager.OnCombatEnd -= HandleCombatEnd;
        playerCharacter.OnRunEnd  -= HandlePlayerRunEnd;

        // 씬을 다시 로드하면 인스펙터 참조는 파괴된 인스턴스를 가리키므로
        // 반드시 살아남은 싱글턴을 쓴다. (두 번째 런부터 포인트가 안 쌓이던 원인)
        var meta = MetaProgressionManager.Instance;
        LastRunReward = meta != null
                      ? meta.OnRunEnd(playerWon, CurrentStage + 1)
                      : new RunReward { playerWon = playerWon, stageReached = CurrentStage + 1 };

        SetPhase(GamePhase.RunEnd);   // 보상 화면 표시
        OnRunEnded?.Invoke(playerWon);
    }

    // ── 동료 관리 ────────────────────────────────────────

    // 이벤트·보물·상점에서 동료 획득 시 호출
    public bool TryAddCompanion(CompanionData data)
    {
        int slot = FindFreePlayerSlot();
        if (slot == -1) return false;  // 슬롯 만석

        var companion = Instantiate(companionEntityTemplate);
        companion.Initialize(data);
        companion.SetSlot(slot);
        companions.Add(companion);
        return true;
    }

    // 슬롯 1은 플레이어 전용 — 2~4 중 빈 슬롯 탐색
    private int FindFreePlayerSlot()
    {
        var usedSlots = new HashSet<int>();
        usedSlots.Add(playerCharacter.CurrentSlot);
        foreach (var c in companions) usedSlots.Add(c.CurrentSlot);

        for (int s = 2; s <= 4; s++)
            if (!usedSlots.Contains(s)) return s;

        return -1;
    }

    // ── 보드 카드 사용 (UI → GameManager → BoardPhaseManager) ──

    public bool TryUseBoardCard(CardData card)
    {
        if (CurrentPhase != GamePhase.BoardPhase) return false;
        if (!playerCharacter.TryUseBoardCard(card))  return false;

        boardPhaseManager.ExecuteBoardCardEffects(card);
        return true;
    }

    // 이벤트 카드 '보스 순간이동' 효과
    public void TeleportPlayerToBoss()
    {
        boardPhaseManager.TeleportPlayer(boardPhaseManager.BossPosition);
        HandleEncounter();
    }

    // ── 골드 접근자 (UI용 편의 래퍼) ────────────────────

    public int  PlayerGold => boardPhaseManager.PlayerGold;
    public bool SpendPlayerGold(int amount) => boardPhaseManager.SpendPlayerGold(amount);
    public void AddPlayerGold(int amount)   => boardPhaseManager.AddPlayerGold(amount);

    // ── 내부 유틸 ────────────────────────────────────────

    // reserveBossSlot: 보스 인카운터에서는 보스가 슬롯을 먼저 차지하므로 비워둔다.
    //                  일반 노드 전투에는 보스가 없으므로 4슬롯을 모두 쓴다.
    private List<MinionEntity> SpawnMinions(List<MinionData> minionDataList,
                                            bool reserveBossSlot = true)
    {
        var entities  = new List<MinionEntity>();
        var usedSlots = new HashSet<int>();
        if (reserveBossSlot && currentBoss != null)
            usedSlots.Add(currentBoss.CurrentSlot);

        foreach (var data in minionDataList)
        {
            int slot = FindFreeEnemySlot(usedSlots);
            if (slot == -1) break;  // 슬롯 만석 (최대 3하수인)

            var minion = Instantiate(minionEntityTemplate);
            minion.Initialize(data);
            minion.SetSlot(slot);
            usedSlots.Add(slot);
            entities.Add(minion);
        }

        return entities;
    }

    private int FindFreeEnemySlot(HashSet<int> usedSlots)
    {
        for (int s = 1; s <= 4; s++)
            if (!usedSlots.Contains(s)) return s;
        return -1;
    }

    private void SetPhase(GamePhase phase)
    {
        CurrentPhase = phase;
        OnPhaseChanged?.Invoke(phase);
    }

    // ── 공개 조회 ────────────────────────────────────────

    public PlayerCharacter               Player               => playerCharacter;
    public BossEntity                    CurrentBoss          => currentBoss;
    public IReadOnlyList<CompanionEntity> Companions          => companions;
    public IReadOnlyList<MinionData>      BossMinions         => bossMinions;
    public BoardPhaseManager             BoardPhaseManagerRef => boardPhaseManager;
}
