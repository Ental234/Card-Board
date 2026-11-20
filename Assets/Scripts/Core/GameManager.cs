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
    [SerializeField] private CharacterData startingCharacter;   // 처음부터 열려 있는 캐릭터. 나머지는 메타 해금으로 늘어난다 (AvailableCharacters)
    [SerializeField] private bool          skipMainMenu;        // 디버그용 — 켜면 메뉴 없이 바로 런 시작

    [Header("노드 효과")]
    [Range(0f, 1f)]
    [SerializeField] private float restHealPercent = 0.3f;  // 휴식: 최대 HP 대비 회복률

    [Header("일반 전투 적 구성")]
    [SerializeField] private int monsterNodeEnemyMin = 1;
    [SerializeField] private int monsterNodeEnemyMax = 2;
    [SerializeField] private int eliteNodeEnemyMin   = 2;
    [SerializeField] private int eliteNodeEnemyMax   = 3;

    [Header("조우 전투")]
    [SerializeField] private int encounterTurnLimit = 2;   // 조우지점·마계의 전령 전투 턴 상한 (최종 결전은 무제한)

    [Header("조우지점 준비 — 다음 보스 전투 1회 (GDD 4장, 6-A에서 조정)")]
    [SerializeField] private int fortifyShield = 8;   // 전열 정비: 1턴 방어막
    [SerializeField] private int ambushEnergy  = 1;   // 기습 준비: 1턴 에너지
    [SerializeField] private int trapStunTurns = 1;   // 함정 설치: 하수인 기절 턴

    [Header("특별 강화")]
    [SerializeField] private List<CardData> curseCards = new();   // '저주 동반' 페널티용 — 저주 카드(3단계 4번)가 생기면 채운다

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int>       OnStageChanged;    // (새 스테이지 번호, 1-based)
    public event Action<bool>      OnRunEnded;        // (playerWon)

    // 노드 이벤트 — UI·EventManager 등이 구독해서 처리
    public event Action<int, NodeType> OnPlayerNodeEvent; // (nodeIndex, type)
    public event Action<int, NodeType> OnBossNodeEvent;

    // 상점 UI를 열어 달라는 요청 (nodeIndex) — 상점 노드 착지·보급소 이용 공용
    public event Action<int> OnShopRequested;

    // 휴식 노드 — UI가 "HP 회복 / 강화" 선택 창을 연다. RestHeal / RestUpgrade로 답한다
    public event Action OnRestRequested;
    public bool IsRestPending { get; private set; }

    // ── 런타임 상태 ─────────────────────────────────────

    public GamePhase CurrentPhase  { get; private set; } = GamePhase.Idle;
    public int       CurrentStage  { get; private set; } = 0;  // 0-indexed

    private BossEntity            currentBoss;
    private List<CompanionEntity> companions    = new();
    private List<MinionData>      bossMinions   = new();  // 보드 페이즈 중 보스가 축적한 하수인
    private bool                  isBossEncounter;        // 현재 전투가 보스 인카운터인지
    private bool                  lastCombatWasElite;     // 보상 등급 판정용
    private List<MinionEntity>    spawnedEnemies = new(); // 이번 전투에 소환한 적 — 전투가 끝나면 정리한다

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

    // 메인 메뉴의 캐릭터 선택지 — 기본 캐릭터 + 메타 해금된 캐릭터 (3단계 7번)
    // 기본 캐릭터를 해금 항목으로도 걸어 두는 실수가 있어도 두 번 나오지 않게 거른다
    public List<CharacterData> AvailableCharacters
    {
        get
        {
            var list = new List<CharacterData>();
            if (startingCharacter != null) list.Add(startingCharacter);

            var meta = MetaProgressionManager.Instance;
            if (meta == null) return list;
            foreach (var c in meta.GetUnlockedCharacters())
                if (c != null && !list.Contains(c)) list.Add(c);
            return list;
        }
    }

    // 해금된 시작 렐릭 — 런 시작 때 하나를 골라 캐릭터 고유 렐릭에 **더해서** 받는다 (GDD 16장)
    public List<RelicData> AvailableStartingRelics
        => MetaProgressionManager.Instance != null
         ? MetaProgressionManager.Instance.GetUnlockedStartingRelics()
         : new List<RelicData>();

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

    // extraRelic — 메타 해금으로 고른 시작 렐릭. 없으면 null
    public void StartRun(CharacterData characterData, RelicData extraRelic = null)
    {
        CurrentStage = 0;

        // 목록만 비우면 이전 런의 동료 오브젝트가 씬에 그대로 남는다.
        // 메인 메뉴 경유(씬 리로드)로는 정리되지만, StartRun을 직접 다시 부르는 경로에서는 누적된다.
        foreach (var c in companions)
            if (c != null) Destroy(c.gameObject);
        companions.Clear();
        bossMinions.Clear();

        // 런 단위 자원 초기화 — 골드·렐릭은 스테이지를 넘어 유지되지만 런이 바뀌면 리셋된다
        boardPhaseManager.ResetRunResources();
        RelicManager.Instance?.ClearAll();

        playerCharacter.Initialize(characterData);

        // Initialize **뒤**여야 한다 — Initialize가 스탯을 새로 깔기 때문에,
        // 앞에서 받으면 최대 HP 같은 Passive 보정이 지워진다. ClearAll(위)보다도 뒤라 지워지지 않는다
        if (characterData.startingRelic != null)
            RelicManager.Instance?.AddPlayerRelic(characterData.startingRelic);
        // 고유 렐릭과 같은 걸 골랐으면 두 번 주지 않는다 (렐릭은 한 종류당 하나)
        if (extraRelic != null && extraRelic != characterData.startingRelic)
            RelicManager.Instance?.AddPlayerRelic(extraRelic);

        // 반드시 먼저 해제한다 — EndRun을 거치지 않고 StartRun이 다시 불리면
        // 구독이 쌓여 런 종료 처리가 두 번 돌아간다
        playerCharacter.OnRunEnd -= HandlePlayerRunEnd;
        playerCharacter.OnRunEnd += HandlePlayerRunEnd;

        StartStage();
    }

    private void StartStage()
    {
        bossMinions.Clear();
        isBossEncounter = false;   // 이전 스테이지의 보스전 상태가 남지 않도록

        // 상점 재고는 스테이지 단위 — 새 스테이지는 새 재고로 시작한다.
        // (렐릭 섹션은 스테이지 안에서 유지되어야 보스 선점이 성립하므로 여기서만 비운다)
        ShopManager.Instance?.ResetStock();

        // 보스 렐릭은 보스 객체 소유 — 새 보스는 빈손으로 시작한다
        RelicManager.Instance?.ClearBossRelics();

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
        // 스테이지를 넘길 때 해제하지만, 혹시 남아 있어도 두 번 붙지 않도록 먼저 뗀다
        UnsubscribeBoardEvents();

        boardPhaseManager.OnEncounterTriggered  += HandleEncounter;
        boardPhaseManager.OnPlayerLanded        += HandlePlayerLanded;
        boardPhaseManager.OnBossLanded          += HandleBossLanded;
        boardPhaseManager.OnBossAcquiresMinion  += HandleBossAcquiresMinion;
        boardPhaseManager.OnSupplyShopRequested += HandleSupplyShopRequested;
        boardPhaseManager.OnSupplyUpgradeRequested += HandleSupplyUpgradeRequested;
        boardPhaseManager.OnSupplyRemoveRequested  += HandleSupplyRemoveRequested;
        boardPhaseManager.OnEncounterPrepChosen    += AddEncounterPrep;
    }

    private void UnsubscribeBoardEvents()
    {
        boardPhaseManager.OnEncounterTriggered  -= HandleEncounter;
        boardPhaseManager.OnPlayerLanded        -= HandlePlayerLanded;
        boardPhaseManager.OnBossLanded          -= HandleBossLanded;
        boardPhaseManager.OnBossAcquiresMinion  -= HandleBossAcquiresMinion;
        boardPhaseManager.OnSupplyShopRequested -= HandleSupplyShopRequested;
        boardPhaseManager.OnSupplyUpgradeRequested -= HandleSupplyUpgradeRequested;
        boardPhaseManager.OnSupplyRemoveRequested  -= HandleSupplyRemoveRequested;
        boardPhaseManager.OnEncounterPrepChosen    -= AddEncounterPrep;
    }

    // ── 보드 이벤트 핸들러 ───────────────────────────────

    // 조우지점에 둘 다 모였다 → 조우 전투 (최종 결전만 턴 제한 없음)
    private void HandleEncounter(EncounterKind kind)
    {
        StartBossCombat(kind == EncounterKind.Final ? 0 : encounterTurnLimit);
    }

    // 보스 전투 공용 — 조우지점·최종 결전·마계의 전령
    private void StartBossCombat(int turnLimit)
    {
        isBossEncounter = true;

        spawnedEnemies = SpawnMinions(bossMinions);
        boardPhaseManager.BeginResolution();   // 전투가 끝날 때까지 보드 턴 정지
        SetPhase(GamePhase.CombatPhase);

        combatManager.OnCombatEnd -= HandleCombatEnd;
        combatManager.OnCombatEnd += HandleCombatEnd;
        // 진형에 세운 동료만 데려간다. 대기열까지 넘기면 전투에 없는 동료가
        // 턴을 받고 패턴까지 발동한다.
        //
        // 순서가 중요하다: 방어막·에너지 준비는 StartCombat **앞**에서 구독하고, 함정은 **뒤**에서 건다.
        // 첫 플레이어 턴 시작 신호가 언제 나가는지는 전투 루프 코루틴이 어디서 멈추느냐에 달려 있다 —
        // 뒤에서 구독하면 그 신호를 놓쳐 2라운드에 들어올 수 있다 (2026-11-20 코드 리뷰 지적.
        // 지금 구조에선 한 프레임 뒤에 나가 실제로 놓치진 않았지만, 루프를 고치는 순간 조용히 깨진다).
        // 함정은 반대로 전투가 1라운드 예고를 정한 뒤여야 그 예고를 지울 수 있다
        SubscribePrepResources();
        combatManager.StartCombat(playerCharacter, DeployedCompanions, currentBoss, spawnedEnemies, turnLimit);
        ApplyPrepTraps();
    }

    // 보급소 이용 — 그 조우지점의 상점을 연다. 닫히면 UIManager가 NotifyNodeResolved로 게이트를 연다
    private void HandleSupplyShopRequested(int nodeIndex)
    {
        boardPhaseManager.BeginResolution();
        OnShopRequested?.Invoke(nodeIndex);
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

        // 휴식은 "HP 회복 / 강화" 선택 창을 띄운다
        if (type == NodeType.Rest)
            boardPhaseManager.BeginResolution();

        OnPlayerNodeEvent?.Invoke(nodeIndex, type);

        switch (type)
        {
            // 몬스터·정예: 전투 시작
            case NodeType.Monster:
            case NodeType.Elite:
                StartNodeCombat(nodeIndex, type);
                break;

            case NodeType.Rest:     RequestRest();   break;
            case NodeType.Treasure: OfferTreasure(); break;
        }
    }

    // ── 노드 즉시 효과 ───────────────────────────────────

    // ── 휴식 노드 — HP 회복 / 강화 1장 중 택 1 (슬더스 모닥불) ──

    private void RequestRest()
    {
        IsRestPending = true;
        if (OnRestRequested != null) { OnRestRequested.Invoke(); return; }

        // UI가 없으면 회복으로 처리 — 보드가 선택을 기다리며 멈추지 않게
        RestHeal();
    }

    public int RestHealAmount => Mathf.Max(1, Mathf.RoundToInt(playerCharacter.Stats.MaxHp * restHealPercent));

    public void RestHeal()
    {
        if (!IsRestPending) return;
        IsRestPending = false;
        ApplyRest();
        boardPhaseManager.EndResolution();
    }

    // 강화 창을 연다. 취소하면 onCancel(선택 창으로 돌아가기)
    public void RestUpgrade(Action onCancel)
    {
        if (!IsRestPending) return;
        OpenUpgradePicker("강화할 카드·동료를 고르세요", 1, special: false, includeCards: true, includeCompanions: true,
            onDone: () => { IsRestPending = false; boardPhaseManager.EndResolution(); },
            onCancel: onCancel);
    }

    // ── 강화 창 (휴식·조우지점·이벤트 공용) ──────────────

    // levels: 1 = 일반 강화, 2 = 특별 강화(한 번에 +2, 대신 무작위 페널티 — 카드만)
    public void OpenUpgradePicker(string title, int levels, bool special, bool includeCards, bool includeCompanions,
                                  Action onDone, Action onCancel)
    {
        var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        var anyUi  = canvas != null ? canvas.transform as RectTransform : null;

        var request = new DeckViewerUI.PickRequest
        {
            title            = title,
            cards            = includeCards ? DeckViewerUI.Sorted(playerCharacter.DeckManager.GetAllCards()) : null,
            companions       = includeCompanions ? new List<CompanionEntity>(companions) : null,
            // 특별 강화(+2)는 강화 안 된 카드만 — +카드는 ++에서 막혀 한 단계만 오르는데
            // 대가는 그대로 붙었다 (2026-11-20 코드 리뷰에서 발견)
            canPickCard      = special ? (Func<CardData, bool>)(c => CardUpgrader.CanUpgrade(c) && c.upgradeLevel == 0)
                                       : CardUpgrader.CanUpgrade,
            canPickCompanion = c => c.CanUpgrade,
            cardTooltip      = c => CardUpgrader.Preview(c, levels) + (special ? "\n\n[대가] 무작위 페널티 1개가 붙는다" : ""),
            companionTooltip = c => c.NextUpgradePreview(),
            onCancel         = onCancel,
        };

        request.onPickCard = card =>
        {
            var penalty = special ? CardUpgrader.RandomPenalty(card, curseCards.Count > 0) : CardPenalty.None;
            var upgraded = CardUpgrader.Upgrade(card, levels, penalty);
            playerCharacter.DeckManager.ReplaceCard(card, upgraded);

            if (penalty == CardPenalty.AddCurse && curseCards.Count > 0)
                playerCharacter.DeckManager.AddCard(curseCards[UnityEngine.Random.Range(0, curseCards.Count)]);

            onDone?.Invoke();
        };

        request.onPickCompanion = c =>
        {
            for (int i = 0; i < levels; i++) c.Upgrade();
            OnFormationChanged?.Invoke();   // 편성창 이름·HP 갱신
            onDone?.Invoke();
        };

        DeckViewerUI.ShowPicker(anyUi, request);
    }

    // 조우지점 대기 메뉴의 강화 / 동료 훈련
    private void HandleSupplyUpgradeRequested(bool companionsOnly)
    {
        OpenUpgradePicker(companionsOnly ? "훈련할 동료를 고르세요" : "강화할 카드·동료를 고르세요",
            1, special: false, includeCards: !companionsOnly, includeCompanions: true,
            onDone:   () => boardPhaseManager.NotifySupplyPickResult(true),
            onCancel: () => boardPhaseManager.NotifySupplyPickResult(false));
    }

    // ── 카드 제거 창 (대기 메뉴·상점·이벤트 공용) ──────────
    //
    // 예전엔 상점이 이벤트 창의 제거 UI를 빌려 써서 **골드를 받지 않고** 이벤트 완료 신호까지 보냈다.
    // 세 곳 모두 이 창 하나로 통일한다. goldCost > 0이면 ShopManager.TryRemoveCard로 골드를 받는다.
    public void OpenRemovePicker(string title, int goldCost, Action onDone, Action onCancel)
    {
        var deck   = playerCharacter.DeckManager;
        var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();

        var request = new DeckViewerUI.PickRequest
        {
            title       = deck.CanRemoveCard ? title : $"{title} - 덱이 {deck.DrawPerTurn}장 이하라 더 뺄 수 없다",
            cards       = DeckViewerUI.Sorted(deck.GetAllCards()),
            // 저주는 여기서 뺄 수 없다 (2026-09-27 결정) — 저주만 없애는 이벤트를 따로 둔다(4단계).
            // 일반 제거로 빠지면 저주가 "제거 1회로 지우는 것"이 되어 무게가 사라진다
            canPickCard = c => !c.IsCurse && deck.CanRemoveCard,
            cardTooltip = c => c.IsCurse ? "저주는 여기서 뺄 수 없다"
                                         : "이 카드를 덱에서 영구히 뺀다" + (goldCost > 0 ? $" ({goldCost}골드)" : ""),
            onCancel    = onCancel,
        };

        request.onPickCard = card =>
        {
            bool ok = goldCost > 0 ? ShopManager.Instance != null && ShopManager.Instance.TryRemoveCard(card)
                                   : deck.RemoveCard(card);
            if (ok) onDone?.Invoke();
            else    onCancel?.Invoke();   // 골드 부족 등 — 아무것도 빠지지 않았다
        };

        DeckViewerUI.ShowPicker(canvas != null ? canvas.transform as RectTransform : null, request);
    }

    private void HandleSupplyRemoveRequested()
    {
        OpenRemovePicker("덱에서 뺄 카드를 고르세요 (무료)", 0,
            onDone:   () => boardPhaseManager.NotifySupplyPickResult(true),
            onCancel: () => boardPhaseManager.NotifySupplyPickResult(false));
    }

    // ── 조우지점 준비 — 다음 보스 전투 1회에 적용 (여러 번 고르면 쌓인다) ──

    private int prepShield, prepEnergy, prepTraps;

    private void AddEncounterPrep(SupplyChoice kind)
    {
        switch (kind)
        {
            case SupplyChoice.Fortify: prepShield += fortifyShield; break;
            case SupplyChoice.Ambush:  prepEnergy += ambushEnergy;  break;
            case SupplyChoice.Trap:    prepTraps  += 1;             break;
        }
    }

    public bool HasEncounterPrep => prepShield > 0 || prepEnergy > 0 || prepTraps > 0;

    // 함정 — 보스 전투 시작 직후 (StartCombat 뒤)
    private void ApplyPrepTraps()
    {
        if (prepTraps <= 0) return;

        // 함정: 앞쪽 하수인부터 기절시키고 **이번 라운드 예고를 지운다.**
        // 전투가 시작되자마자 1라운드 예고가 이미 정해져 있어서, 기절만 걸면 적 턴 시작에 1턴짜리 기절이
        // 먼저 풀리고 그대로 행동한다 (PLAN 검토 대기 — 기절 타이밍). 예고가 없으면 그 턴은 쉰다.
        // 다음 라운드부터는 기절이 예고 판정에서 걸러 준다.
        var targets = spawnedEnemies.FindAll(m => m != null && m.IsActive);
        targets.Sort((a, b) => a.CurrentSlot.CompareTo(b.CurrentSlot));
        for (int i = 0; i < prepTraps && i < targets.Count; i++)
        {
            targets[i].ApplyStatus(StatusEffect.Stun, 1, trapStunTurns);
            targets[i].ClearIntent();
        }
        combatManager.RefreshIntentPreviews();
        prepTraps = 0;
    }

    // 방어막·에너지 — 첫 플레이어 턴이 시작된 뒤에 준다 (StartCombat 앞에서 구독).
    // 턴 시작 처리가 방어막을 비우고 에너지를 채우므로 그보다 먼저 주면 사라진다.
    // 1회용이다: 받았거나 전투가 끝나면 구독을 푼다 — 안 풀면 다음 전투 첫 턴에 공짜로 들어온다
    [NonSerialized] private Action<CombatEntity> prepTurnHandler;
    [NonSerialized] private Action<bool>         prepEndHandler;

    private void SubscribePrepResources()
    {
        UnsubscribePrepResources();   // 이전 전투에서 남은 게 있으면 버린다

        int shield = prepShield, energy = prepEnergy;
        prepShield = prepEnergy = 0;
        if (shield <= 0 && energy <= 0) return;

        prepTurnHandler = entity =>
        {
            if (!(entity is PlayerCharacter)) return;
            UnsubscribePrepResources();
            if (shield > 0) playerCharacter.Stats.AddShield(shield);
            if (energy > 0) playerCharacter.DeckManager.GainEnergy(energy);
        };
        prepEndHandler = _ => UnsubscribePrepResources();

        combatManager.OnEntityTurnBegin += prepTurnHandler;
        combatManager.OnCombatEnd       += prepEndHandler;
    }

    private void UnsubscribePrepResources()
    {
        if (prepTurnHandler != null) combatManager.OnEntityTurnBegin -= prepTurnHandler;
        if (prepEndHandler  != null) combatManager.OnCombatEnd       -= prepEndHandler;
        prepTurnHandler = null;
        prepEndHandler  = null;
    }

    // 휴식: HP 회복
    private void ApplyRest()
    {
        int amount = Mathf.Max(1, Mathf.RoundToInt(playerCharacter.Stats.MaxHp * restHealPercent));
        playerCharacter.Heal(amount);
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

        // 보스는 **상점 칸에 착지한 그 순간** 산다.
        // 예전에는 아무도 BossVisitShop을 부르지 않아 보스가 상점을 아예 쓰지 않았고,
        // 그 전에는 플레이어가 상점을 열 때 보스 구매가 함께 돌아
        // "먼저 도착한 쪽이 선점한다"는 규칙이 성립하지 않았다.
        if (type == NodeType.Shop)
            ShopManager.Instance?.BossVisitShop(nodeIndex);
    }

    // 보스 진영은 보스 포함 4슬롯이라 하수인은 3마리까지만 거느린다
    private void HandleBossAcquiresMinion(MinionData minionData)
    {
        if (bossMinions.Count >= BoardPhaseManager.MaxBossMinions) return;
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
        spawnedEnemies = enemies;

        isBossEncounter    = false;
        lastCombatWasElite = isElite;
        boardPhaseManager.BeginResolution();   // 전투가 끝날 때까지 보드 턴 정지
        SetPhase(GamePhase.CombatPhase);

        combatManager.OnCombatEnd -= HandleCombatEnd;
        combatManager.OnCombatEnd += HandleCombatEnd;
        combatManager.StartCombat(playerCharacter, DeployedCompanions, bossEntity: null, minionList: enemies);
    }

    // ── 전투 종료 핸들러 ─────────────────────────────────

    private void HandleCombatEnd(bool playerWon)
    {
        combatManager.OnCombatEnd -= HandleCombatEnd;

        CleanupSpawnedEnemies();

        if (!playerWon)
        {
            EndRun(playerWon: false);
            return;
        }

        // 조우 전투가 턴 제한으로 끝났다 — 양쪽 다 살아 있다.
        // 보상 화면 없이 보드로 돌아간다. 늦게 온 쪽의 보급과 다음 구획은 BoardPhaseManager가 잇는다.
        if (isBossEncounter && combatManager.EndedByTurnLimit)
        {
            isBossEncounter = false;
            SetPhase(GamePhase.BoardPhase);
            boardPhaseManager.EndResolution();
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
    //
    // 자동 배치하지 않고 대기열로 보낸다. 진형에 누구를 세울지는 편성창에서 정한다.
    // 슬롯이 4칸뿐이라 자동 배치하면 나중에 얻은 동료가 조용히 버려지거나
    // 원치 않는 자리에 꽂히는데, 그게 오히려 손해다.
    public bool TryAddCompanion(CompanionData data)
    {
        var companion = Instantiate(companionEntityTemplate);
        companion.Initialize(data);
        companion.SetSlot(SlotReserve);   // 대기열
        companions.Add(companion);

        OnFormationChanged?.Invoke();
        return true;
    }

    // ── 편성 (보드 페이즈) ───────────────────────────────
    //
    // 전투 밖에서는 행동력 없이 자유롭게 진형을 짠다.
    // 전투 중 이동은 CombatManager.TryMoveFriendly가 담당하며 AP를 쓴다 — 둘은 별개다.
    //
    // 배치 여부는 CurrentSlot 하나로 표현한다 (CombatEntity의 원래 정의: 1~4 배치 / 0 미배치).
    // 별도의 '배치된 동료' 목록을 두면 두 목록이 어긋나는 순간 유닛이 사라지거나 중복된다.

    public const int SlotReserve = 0;   // 대기열

    public event Action OnFormationChanged;

    // 진형에 세운 동료만. 전투에는 이쪽만 넘긴다.
    public List<CompanionEntity> DeployedCompanions
    {
        get
        {
            var list = new List<CompanionEntity>();
            foreach (var c in companions)
                if (c.CurrentSlot >= 1 && c.CurrentSlot <= 4) list.Add(c);
            return list;
        }
    }

    // 대기열에 있는 동료 (진형 밖)
    public List<CompanionEntity> ReserveCompanions
    {
        get
        {
            var list = new List<CompanionEntity>();
            foreach (var c in companions)
                if (c.CurrentSlot == SlotReserve) list.Add(c);
            return list;
        }
    }

    public CombatEntity GetFormationOccupant(int slot)
    {
        if (slot < 1 || slot > 4) return null;

        if (playerCharacter != null && playerCharacter.CurrentSlot == slot) return playerCharacter;

        foreach (var c in companions)
            if (c.CurrentSlot == slot) return c;

        return null;
    }

    // slotA와 slotB의 점유자를 맞바꾼다. 한쪽이 비어 있으면 그냥 이동한다.
    public bool TrySwapFormation(int slotA, int slotB)
    {
        if (!CanEditFormation())                  return false;
        if (slotA == slotB)                       return false;
        if (slotA < 1 || slotA > 4 || slotB < 1 || slotB > 4) return false;

        var a = GetFormationOccupant(slotA);
        var b = GetFormationOccupant(slotB);
        if (a == null && b == null) return false;

        a?.SetSlot(slotB);
        b?.SetSlot(slotA);

        OnFormationChanged?.Invoke();
        return true;
    }

    // 대기열 → 진형. 그 자리에 동료가 서 있었으면 서로 자리를 바꾼다(그 동료가 대기열로).
    public bool TryDeployCompanion(CompanionEntity companion, int slot)
    {
        if (!CanEditFormation())                     return false;
        if (companion == null || slot < 1 || slot > 4) return false;
        if (!companions.Contains(companion))         return false;
        if (companion.CurrentSlot == slot)           return false;

        var occupant = GetFormationOccupant(slot);

        // 플레이어는 대기열로 내릴 수 없다 — 진형에서 빠지면 전투에 나갈 사람이 없다
        if (occupant == playerCharacter) return false;

        int from = companion.CurrentSlot;    // 대기열(0)일 수도, 다른 슬롯일 수도 있다
        companion.SetSlot(slot);

        // 밀려난 동료는 시전자가 있던 자리로. 대기열에서 왔다면 대기열로 내려간다.
        (occupant as CompanionEntity)?.SetSlot(from);

        OnFormationChanged?.Invoke();
        return true;
    }

    // 진형 → 대기열
    public bool TryWithdrawFromSlot(int slot)
    {
        if (!CanEditFormation()) return false;

        var occupant = GetFormationOccupant(slot) as CompanionEntity;
        if (occupant == null) return false;   // 빈 칸이거나 플레이어

        occupant.SetSlot(SlotReserve);
        OnFormationChanged?.Invoke();
        return true;
    }

    // 빈 자리에 바로 세운다 (편성창에서 더블클릭 등 빠른 배치용)
    public bool TryDeployToFreeSlot(CompanionEntity companion)
    {
        int slot = FindFreeFormationSlot();
        if (slot == -1) return false;

        return TryDeployCompanion(companion, slot);
    }

    // 아무도 서 있지 않은 슬롯.
    //
    // 슬롯 1을 플레이어 전용으로 고정하면 안 된다 — CharacterData.initialSlot이
    // 캐릭터마다 다르다(GDD: 전사 1 / 도적 2 / 마법사 3). 전용으로 두면
    // 마법사를 골랐을 때 슬롯 1이 영영 비어 동료가 2명밖에 못 들어간다.
    private int FindFreeFormationSlot()
    {
        for (int s = 1; s <= 4; s++)
            if (GetFormationOccupant(s) == null) return s;

        return -1;
    }

    // 전투 중에는 편성을 못 바꾼다 — 그쪽은 AP를 쓰는 한 칸 이동 규칙을 따라야 한다
    private bool CanEditFormation() => CurrentPhase == GamePhase.BoardPhase;

    // ── 보드 카드 사용 (UI → GameManager → BoardPhaseManager) ──

    public bool TryUseBoardCard(CardData card)
    {
        if (CurrentPhase != GamePhase.BoardPhase) return false;
        if (!playerCharacter.TryUseBoardCard(card))  return false;

        boardPhaseManager.ExecuteBoardCardEffects(card);
        return true;
    }

    // 이벤트 '마계의 전령' — 플레이어가 스스로 고르는 추가 조우 (GDD 12장, v0.6 재설계).
    // 조우지점과 같은 2턴 전투. 보스 HP는 누적되고, 진행 위치는 바뀌지 않는다.
    public void StartHeraldEncounter()
    {
        if (currentBoss == null) return;
        StartBossCombat(encounterTurnLimit);
    }

    // ── 골드 접근자 (UI용 편의 래퍼) ────────────────────

    public int  PlayerGold => boardPhaseManager.PlayerGold;
    public bool SpendPlayerGold(int amount) => boardPhaseManager.SpendPlayerGold(amount);
    public void AddPlayerGold(int amount)   => boardPhaseManager.AddPlayerGold(amount);

    // ── 내부 유틸 ────────────────────────────────────────

    // 전투가 끝난 적 오브젝트를 정리한다.
    // 보스 하수인은 **살아남은 것만** 다음 조우로 넘어간다 — 조우가 여러 번이 됐으므로
    // 쓰러뜨린 하수인이 다음 조우에 되살아나면 깎은 의미가 없다.
    private void CleanupSpawnedEnemies()
    {
        if (isBossEncounter)
        {
            bossMinions.Clear();
            foreach (var m in spawnedEnemies)
                if (m != null && m.IsActive && m.Data != null) bossMinions.Add(m.Data);
        }

        foreach (var m in spawnedEnemies)
            if (m != null) Destroy(m.gameObject);
        spawnedEnemies.Clear();
    }

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
