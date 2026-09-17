using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 보드 페이즈 전체 흐름 관리 (v0.6 — 구획 단위 일방통행 메시 + 조우지점)
//
// 한 보드 턴:
//   ① 플레이어  — 조우지점에서 대기 중이면 보급 1회
//                 아니면 보드 카드 → 주사위 → 도달 가능 노드 중 택 1 → 노드 이벤트
//   ② 보스      — 조우지점에서 대기 중이면 무장 1회
//                 아니면 주사위 → 경로 가중치/성향으로 착지 → 노드 이벤트
//   조우지점에 두 번째로 닿은 쪽이 나오면 그 자리에서 조우 전투 → 늦은 쪽 보급 1회 → 다음 구획
//
// 플레이어와 보스는 각자 자기 트랙만 밟는다. 겹치는 곳은 시작 칸과 조우지점뿐이다.
public class BoardPhaseManager : MonoBehaviour
{
    [SerializeField] private BoardGenerator boardGenerator;
    [SerializeField] private CombatManager  combatManager;

    [Header("조우지점 보급")]
    [Range(0f, 1f)]
    [SerializeField] private float campHealPercent   = 0.3f;   // 야영지 선착 즉시 회복 (최대 HP 대비)
    [Range(0f, 1f)]
    [SerializeField] private float supplyHealPercent = 0.1f;   // 대기 메뉴 "HP 회복(소)"

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<int, NodeType>  OnPlayerLanded;       // (nodeIndex, type) — 노드 이벤트 구독
    public event Action<int, NodeType>  OnBossLanded;         // (nodeIndex, type)
    public event Action<MinionData>     OnBossAcquiresMinion; // 보스 몬스터 칸 착지 / 조우지점 무장
    public event Action                 OnBoardCardPhaseStarted; // UI가 보드 손패를 연다
    public event Action                 OnBoardCardPhaseEnded;   // 주사위를 굴려 페이즈 종료
    public event Action<int>            OnDiceRolled;         // (플레이어 최종 주사위 값)
    public event Action<int>            OnBossDiceRolled;     // (보스 주사위 값)
    public event Action<int>            OnBossDiceRevealed;   // 예언의 주사위 — 보스의 다음 주사위
    public event Action<EncounterKind>  OnEncounterTriggered; // 조우 전투 발생 — 구독자가 전투를 연다
    public event Action<int>            OnPlayerGoldChanged;
    public event Action<int>            OnBossGoldChanged;

    public event Action<List<int>>      OnDestinationChoiceRequested; // 도달 가능 노드 — UI가 하나를 고른다
    public event Action                 OnPositionsChanged;           // 말 위치가 바뀜 (맵 아이콘·진행도)
    public event Action<int>            OnSectionChanged;             // (새 구획, 0-based)

    // 조우지점 — 플레이어 대기 메뉴 (조우지점 노드). UI가 SubmitSupplyChoice로 답한다
    public event Action<NodeData>       OnPlayerSupplyRequested;
    // 보급소 기능으로 상점을 연다 (nodeIndex) — 구독자가 게이트를 닫고 상점 UI를 연다
    public event Action<int>            OnSupplyShopRequested;
    // 조우지점 알림 (UI 로그용) — "보스가 렐릭을 샀다" 등
    public event Action<string>         OnEncounterLog;

    // ── 상태 ────────────────────────────────────────────

    private NodeData[]      board;
    private int             playerPosition;
    private int             bossPosition;
    private int             currentSection;
    private int             sectionCount;
    private int[]           sectionEncounters;   // 구획 → 그 구획의 조우지점 노드

    private bool            playerAtEncounter;   // 이번 구획의 조우지점에 도착해 고정됐는가
    private bool            bossAtEncounter;

    private PlayerCharacter player;
    private BossEntity      bossEntity;
    private BossData        bossData;
    private int             bossGold;
    private int             playerGold;

    private int  bossFreezeturns;       // 속박의 사슬 남은 턴
    private int  diceBonusOnce;         // 이번 주사위에만 추가
    private bool allowRerollOnce;       // 이번 주사위 재굴림 허용
    private bool anyDistanceOnce;       // 순간이동 — 이번 턴 주사위 값 이하 어느 거리든 착지 가능
    private bool blockNextBossMinion;   // 보스 다음 하수인 획득 차단
    private int  bossRollPreview;       // 예언의 주사위 — 미리 굴려둔 보스 주사위 (0 = 없음)

    private int  totalNodes => board?.Length ?? 0;
    private bool boardActive;
    private bool turnLoopRunning;

    // 전투·이벤트·상점이 진행되는 동안 보드 턴을 멈추기 위한 게이트.
    // 노드 이벤트를 처리하는 쪽(GameManager 등)이 Begin/EndResolution으로 여닫는다.
    private int pendingResolutions;
    public bool IsResolving => pendingResolutions > 0;

    public void BeginResolution() => pendingResolutions++;
    public void EndResolution()   => pendingResolutions = Mathf.Max(0, pendingResolutions - 1);

    // 턴 사이 간격 (연출 여유)
    private const float TurnInterval = 0.4f;

    // 보스가 조우지점에서 무장할 때 거느릴 수 있는 하수인 상한 (보스 포함 4슬롯)
    public const int MaxBossMinions = 3;

    // ── 초기화 ──────────────────────────────────────────

    public void InitBoard(BoardStageData stageData, PlayerCharacter pc, BossEntity boss, BossData bossConfig)
    {
        player      = pc;
        bossEntity  = boss;
        bossData    = bossConfig;

        // 골드는 런 단위 자원이라 여기서 건드리지 않는다.
        // 스테이지마다 초기화하면 보스 보상 골드가 다음 스테이지에서 증발한다.
        // 런 시작 시 GameManager가 ResetRunResources()로 초기화한다.

        board = boardGenerator.Generate(stageData);
        BoardVersion++;
        IndexSections();

        playerPosition = 0;
        bossPosition   = 0;
        board[0].ClaimPlayer();
        board[0].ClaimBoss();

        currentSection    = 0;
        playerAtEncounter = false;
        bossAtEncounter   = false;

        bossFreezeturns     = 0;
        blockNextBossMinion = false;
        bossRollPreview     = 0;
        pendingResolutions  = 0;
        ResetTurnModifiers();
        boardActive = true;

        OnSectionChanged?.Invoke(currentSection);
        OnPositionsChanged?.Invoke();
    }

    private void IndexSections()
    {
        int max = 0;
        foreach (var n in board) max = Mathf.Max(max, n.section);
        sectionCount = max + 1;

        sectionEncounters = new int[sectionCount];
        foreach (var n in board)
            if (n.IsEncounter) sectionEncounters[n.section] = n.index;
    }

    // ── 턴 진입점 ────────────────────────────────────────

    // 보드 턴을 반복 실행한다. 이미 돌고 있으면 중복 시작하지 않는다.
    public void StartBoardTurn()
    {
        if (!boardActive || turnLoopRunning) return;
        StartCoroutine(BoardTurnLoop());
    }

    // 런 단위 자원 초기화 — 새 런을 시작할 때만 호출한다 (스테이지 전환 시에는 호출하지 않음)
    public void ResetRunResources()
    {
        playerGold = 0;
        bossGold   = 0;
        OnPlayerGoldChanged?.Invoke(playerGold);
        OnBossGoldChanged?.Invoke(bossGold);
    }

    // 스테이지 전환 등으로 보드를 내릴 때 호출
    public void StopBoard()
    {
        boardActive     = false;
        turnLoopRunning = false;
        pendingResolutions = 0;
        StopAllCoroutines();
    }

    private IEnumerator BoardTurnLoop()
    {
        turnLoopRunning = true;
        while (boardActive)
        {
            yield return StartCoroutine(BoardTurnRoutine());
            if (!boardActive) break;
            yield return new WaitForSeconds(TurnInterval);
        }
        turnLoopRunning = false;
    }

    private IEnumerator BoardTurnRoutine()
    {
        // ① 보드 턴 시작 렐릭 트리거
        RelicManager.Instance?.TriggerPlayerRelics(RelicTrigger.OnBoardTurnStart);

        // ② 플레이어
        bool encounterResolved = false;

        if (playerAtEncounter)
        {
            // 선착해 기다리는 턴 — 주사위 대신 보급 1회
            yield return StartCoroutine(PlayerSupplyRoutine(immediate: false));
        }
        else
        {
            yield return StartCoroutine(PlayerMoveRoutine());

            if (board[playerPosition].IsEncounter)
            {
                playerAtEncounter = true;

                if (bossAtEncounter)
                {
                    // 늦게 도착 — 전투 먼저, 보급 나중
                    yield return StartCoroutine(EncounterRoutine(lateIsPlayer: true));
                    encounterResolved = true;
                }
                else
                {
                    // 선착 — 그 지점의 기능 즉시 1회
                    yield return StartCoroutine(PlayerSupplyRoutine(immediate: true));
                }
            }
            else
            {
                yield return StartCoroutine(TriggerPlayerNodeEvent());
            }
        }

        if (!boardActive) yield break;

        // 조우가 이 턴에 끝났으면 턴을 닫는다 — 다음 턴은 새 구획에서 둘 다 조우지점에서 출발한다
        if (encounterResolved)
        {
            ResetTurnModifiers();
            yield break;
        }

        // ③ 보스 — HP 기반 패닉 판정 (조우에서 깎인 결과를 반영)
        bossEntity.OnBoardTurn();

        if (bossAtEncounter)
        {
            BossSupply(immediate: false);
        }
        else if (bossFreezeturns > 0)
        {
            bossFreezeturns--;
            OnEncounterLog?.Invoke("속박의 사슬 — 보스가 움직이지 못했다");
        }
        else
        {
            MoveBoss();

            if (board[bossPosition].IsEncounter)
            {
                bossAtEncounter = true;

                if (playerAtEncounter)
                    yield return StartCoroutine(EncounterRoutine(lateIsPlayer: false));
                else
                    BossSupply(immediate: true);
            }
            else
            {
                yield return StartCoroutine(TriggerBossNodeEvent());
            }
        }

        ResetTurnModifiers();
    }

    // ── 플레이어 이동 ────────────────────────────────────

    // 보드 카드 페이즈 — "주사위 굴리기"를 누를 때까지 대기한다.
    private bool diceRollRequested;
    public void RequestDiceRoll() => diceRollRequested = true;

    // UI가 고른 목적지. -1이면 아직 미선택.
    private int       pendingDestination = -1;
    // [NonSerialized] — 에디터 플레이 진입·스크립트 리로드 때 Unity는 private 필드까지 직렬화해
    // null 목록을 빈 목록으로, null 참조를 기본 인스턴스로 되살린다. "대기 중인가"를 null로 판정하는
    // 필드는 반드시 막아야 한다 (UI가 없는 요청을 기다리는 것으로 오판한다).
    [NonSerialized] private List<int> destinationCandidates;

    public void SubmitDestination(int nodeIndex) => pendingDestination = nodeIndex;

    private IEnumerator PlayerMoveRoutine()
    {
        // 보드 카드 페이즈 — 주사위를 굴리기 전에 카드로 개입할 기회
        diceRollRequested = false;
        player.StartBoardPhase();
        OnBoardCardPhaseStarted?.Invoke();

        // UI가 없으면 무한 대기하므로, 구독자가 있을 때만 기다린다
        if (OnBoardCardPhaseStarted != null)
            yield return new WaitUntil(() => diceRollRequested);

        OnBoardCardPhaseEnded?.Invoke();
        player.EndBoardPhase();

        int steps = RollDice();
        OnDiceRolled?.Invoke(steps);
        yield return new WaitForSeconds(0.3f);

        var candidates = GetLandingCandidates(playerPosition, steps, BoardTrack.Player, anyDistanceOnce);
        if (candidates.Count == 0) yield break;   // 정상 보드에선 발생하지 않음

        int destination = candidates[0];

        // 갈 곳이 하나뿐이면 묻지 않는다 — 선택지 없는 클릭은 흐름만 끊는다
        if (candidates.Count > 1 && OnDestinationChoiceRequested != null)
        {
            pendingDestination    = -1;
            destinationCandidates = candidates;
            OnDestinationChoiceRequested.Invoke(candidates);

            yield return new WaitUntil(() => candidates.Contains(pendingDestination));
            destination           = pendingDestination;
            destinationCandidates = null;
        }

        playerPosition = destination;
        board[playerPosition].ClaimPlayer();
        OnPositionsChanged?.Invoke();
    }

    // 지금 목적지 선택을 기다리는 중이면 그 후보 (UI 복구용)
    public IReadOnlyList<int> PendingDestinationCandidates
        => destinationCandidates != null && destinationCandidates.Count > 0 ? destinationCandidates : null;

    private int RollDice()
    {
        int roll = UnityEngine.Random.Range(1, 7);  // 1d6

        if (allowRerollOnce)
        {
            int reroll = UnityEngine.Random.Range(1, 7);
            roll = Mathf.Max(roll, reroll);  // 재굴림 시 더 높은 값 사용
        }

        return Mathf.Max(1, roll + diceBonusOnce);
    }

    // ── 경로 탐색 (일방통행 그래프) ──────────────────────

    // from에서 steps칸 갔을 때 착지할 수 있는 노드들.
    // · 자기 트랙과 공용 노드만 밟는다
    // · 조우지점에 닿으면 남은 칸과 무관하게 거기서 멈춘다 (도착하면 고정 — GDD 3장)
    // · anyDistance(순간이동)면 1칸~steps칸 어디든 착지할 수 있다
    public List<int> GetLandingCandidates(int from, int steps, BoardTrack mover, bool anyDistance = false)
    {
        var result  = new List<int>();
        var visited = new HashSet<(int, int)>();   // (노드, 남은 칸) — 합류가 많아도 중복 탐색하지 않는다

        void Walk(int cur, int remaining)
        {
            foreach (int next in board[cur].exits)
            {
                var node = board[next];
                if (!node.IsWalkableBy(mover)) continue;

                int left = remaining - 1;
                if (!visited.Add((next, left))) continue;

                if (node.IsEncounter || left <= 0)
                {
                    if (!result.Contains(next)) result.Add(next);
                    continue;
                }

                if (anyDistance && !result.Contains(next)) result.Add(next);
                Walk(next, left);
            }
        }

        if (steps > 0) Walk(from, steps);
        return result;
    }

    // from에서 steps칸 이동하는 모든 경로 (보스 AI용).
    // 각 경로는 지나간 노드 인덱스 목록 (출발지 제외, 착지 포함). 조우지점에서 끊긴다.
    private List<List<int>> EnumeratePaths(int from, int steps, BoardTrack mover)
    {
        var results = new List<List<int>>();
        var buffer  = new List<int>(steps);

        void Walk(int cur, int remaining)
        {
            foreach (int next in board[cur].exits)
            {
                if (!board[next].IsWalkableBy(mover)) continue;

                buffer.Add(next);
                if (board[next].IsEncounter || remaining - 1 <= 0)
                    results.Add(new List<int>(buffer));
                else
                    Walk(next, remaining - 1);
                buffer.RemoveAt(buffer.Count - 1);
            }
        }

        if (steps > 0) Walk(from, steps);
        return results;
    }

    // 이번 구획의 조우지점까지 최단 칸 수 (자기 트랙 기준). 이미 도착했으면 0.
    public int StepsToEncounter(int from, BoardTrack mover)
    {
        if (board == null || sectionEncounters == null) return -1;

        int target = sectionEncounters[currentSection];
        if (from == target) return 0;

        var dist  = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (int next in board[cur].exits)
            {
                if (dist.ContainsKey(next) || !board[next].IsWalkableBy(mover)) continue;
                dist[next] = dist[cur] + 1;
                if (next == target) return dist[next];
                if (board[next].IsEncounter) continue;   // 다른 조우지점 너머로는 가지 않는다
                queue.Enqueue(next);
            }
        }

        return -1;   // 도달 불가 (정상 보드에선 발생하지 않음)
    }

    // ── 조우지점 ─────────────────────────────────────────

    private IEnumerator EncounterRoutine(bool lateIsPlayer)
    {
        var encounter = board[sectionEncounters[currentSection]];
        OnPositionsChanged?.Invoke();

        OnEncounterTriggered?.Invoke(encounter.encounterKind);
        // 구독자(GameManager)가 BeginResolution 후 전투를 연다.
        // 보스를 쓰러뜨리면 GameManager가 스테이지를 넘기며 StopBoard로 이 코루틴을 끊는다.
        yield return null;
        yield return new WaitUntil(() => !IsResolving);
        if (!boardActive) yield break;

        // 늦게 온 쪽은 전투가 끝난 뒤에야 보급 1회
        if (lateIsPlayer) yield return StartCoroutine(PlayerSupplyRoutine(immediate: false));
        else              BossSupply(immediate: false);

        if (!boardActive) yield break;
        AdvanceSection();
    }

    private void AdvanceSection()
    {
        // 최종 결전은 턴 제한이 없어 누군가 쓰러져야 끝난다 — 여기까지 오지 않는다
        if (currentSection >= sectionCount - 1) return;

        currentSection++;
        playerAtEncounter = false;
        bossAtEncounter   = false;

        OnSectionChanged?.Invoke(currentSection);
        OnPositionsChanged?.Invoke();
    }

    // 플레이어 보급.
    //   immediate — 선착 즉시 "그 지점의 기능": 보급소=상점 / 야영지=HP 회복
    //   대기 턴·늦은 도착 — 메뉴에서 택 1 (구매 / HP 회복(소) / 카드 강화 / 카드 제거)
    private SupplyChoice? pendingSupply;
    public  void SubmitSupplyChoice(SupplyChoice choice) => pendingSupply = choice;

    // 지금 보급 선택을 기다리는 조우지점 (UI 복구용 — 패널이 꺼져 있다 켜져도 메뉴를 다시 띄운다)
    [NonSerialized] private NodeData pendingSupplyRequest;
    public NodeData PendingSupplyRequest => pendingSupplyRequest;

    private IEnumerator PlayerSupplyRoutine(bool immediate)
    {
        var encounter = board[sectionEncounters[currentSection]];

        // 스테이지 끝은 선착 보상이 없다. 대기 턴은 한 박자 쉬어 보스 이동이 읽히게 한다
        if (encounter.encounterKind == EncounterKind.Final)
        {
            if (!immediate) yield return new WaitForSeconds(0.6f);
            yield break;
        }

        SupplyChoice choice;
        if (immediate)
        {
            choice = encounter.encounterKind == EncounterKind.Supply ? SupplyChoice.Shop : SupplyChoice.Heal;
        }
        else
        {
            pendingSupply        = null;
            pendingSupplyRequest = encounter;
            OnPlayerSupplyRequested?.Invoke(encounter);

            if (OnPlayerSupplyRequested != null)
                yield return new WaitUntil(() => pendingSupply.HasValue);

            pendingSupplyRequest = null;
            choice = pendingSupply ?? SupplyChoice.Heal;
        }

        yield return StartCoroutine(ApplyPlayerSupply(encounter, choice, immediate));
    }

    private IEnumerator ApplyPlayerSupply(NodeData encounter, SupplyChoice choice, bool immediate)
    {
        switch (choice)
        {
            case SupplyChoice.Shop:
                if (encounter.encounterKind != EncounterKind.Supply) break;
                OnSupplyShopRequested?.Invoke(encounter.index);
                yield return null;                               // 구독자가 게이트를 닫을 한 프레임
                yield return new WaitUntil(() => !IsResolving);
                break;

            case SupplyChoice.Heal:
                float pct = immediate ? campHealPercent : supplyHealPercent;
                int amount = Mathf.Max(1, Mathf.RoundToInt(player.Stats.MaxHp * pct));
                player.Heal(amount);
                OnEncounterLog?.Invoke($"HP {amount} 회복");
                break;

            // 강화·제거는 대기 메뉴용 UI가 아직 없다 — UI가 비활성으로 표시한다
            case SupplyChoice.Upgrade:
            case SupplyChoice.RemoveCard:
            case SupplyChoice.Skip:
                break;
        }
    }

    // 보스 무장 — 회복은 없다 (보스가 회복하면 "조우를 거듭하며 깎는다"가 무너진다)
    //   보급소에 렐릭이 남아 있으면 렐릭 / 아니면 하수인 추가
    private void BossSupply(bool immediate)
    {
        var encounter = board[sectionEncounters[currentSection]];
        if (encounter.encounterKind == EncounterKind.Final) return;

        var shop = ShopManager.Instance;
        if (encounter.encounterKind == EncounterKind.Supply && shop != null
            && shop.HasUnsoldRelic(encounter.index) && shop.BossVisitShop(encounter.index))
        {
            OnEncounterLog?.Invoke("보스가 보급소에서 렐릭을 샀다");
            return;
        }

        if (TryGiveBossMinion())
            OnEncounterLog?.Invoke("보스가 하수인을 불러들였다");
    }

    // ── 노드 이벤트 ──────────────────────────────────────

    private IEnumerator TriggerPlayerNodeEvent()
    {
        var node = board[playerPosition];
        OnPlayerLanded?.Invoke(playerPosition, node.type);

        // 구독자(GameManager·EventManager 등)가 전투·이벤트·상점을 열면
        // BeginResolution으로 게이트가 닫힌다. 끝날 때까지 보드 턴을 멈춘다.
        yield return null;                              // 구독자가 게이트를 닫을 한 프레임
        yield return new WaitUntil(() => !IsResolving);
    }

    private IEnumerator TriggerBossNodeEvent()
    {
        var node = board[bossPosition];
        OnBossLanded?.Invoke(bossPosition, node.type);

        // 보스 몬스터 칸 착지 → 하수인 획득
        if (node.type == NodeType.Monster || node.type == NodeType.Elite)
            TryGiveBossMinion();

        yield return null;
    }

    private bool TryGiveBossMinion()
    {
        if (blockNextBossMinion)
        {
            blockNextBossMinion = false;  // 차단 1회 소모
            OnEncounterLog?.Invoke("하수인 차단 — 보스의 하수인 획득을 막았다");
            return false;
        }

        MinionData minion = PickMinionFromPool();
        if (minion == null) return false;

        OnBossAcquiresMinion?.Invoke(minion);
        return true;
    }

    // ── 보스 이동 AI ─────────────────────────────────────

    // 보스도 주사위로 거리를 정한다. AI가 판단하는 건 "어느 착지 칸으로 갈지"뿐.
    //   수집형: 경로 가중치 총합이 최대인 경로. 단 우선 가중치가 있으면 무조건 우선
    //   선점형: 조우지점까지 남은 거리가 가장 짧아지는 경로 (동률이면 가중치)
    //   패닉  : 성향과 무관하게 선점형으로 뒤집힌다 — 보상을 버리고 조우지점으로 돌진
    private void MoveBoss()
    {
        int steps = RollBossDice();
        OnBossDiceRolled?.Invoke(steps);

        var paths = EnumeratePaths(bossPosition, steps, BoardTrack.Boss);
        if (paths.Count == 0) return;

        var best = bossEntity.EffectiveMoveStyle == BossMoveStyle.Rusher
                 ? PickRushPath(paths)
                 : PickWeightedPath(paths);

        bossPosition = best[best.Count - 1];
        board[bossPosition].ClaimBoss();
        OnPositionsChanged?.Invoke();
    }

    private int RollBossDice()
    {
        // 예언의 주사위로 미리 본 값이 있으면 그 값을 쓴다 — 본 것과 실제가 달라선 안 된다
        if (bossRollPreview > 0)
        {
            int preview = bossRollPreview;
            bossRollPreview = 0;
            return preview;
        }
        return UnityEngine.Random.Range(1, 7);   // 1d6 — 플레이어와 동일
    }

    // 우선 가중치 > 일반 가중치 총합
    private List<int> PickWeightedPath(List<List<int>> paths)
    {
        int bestPriority = 0;
        foreach (var p in paths)
            bestPriority = Mathf.Max(bestPriority, PathPriority(p));

        // 우선 가중치를 가진 경로가 있으면 그 그룹만 후보로 남긴다
        var candidates = bestPriority > 0
                       ? paths.FindAll(p => PathPriority(p) == bestPriority)
                       : paths;

        var best      = candidates[0];
        int bestScore = PathWeight(best);

        foreach (var p in candidates)
        {
            int score = PathWeight(p);
            if (score > bestScore) { bestScore = score; best = p; }
        }

        return best;
    }

    private List<int> PickRushPath(List<List<int>> paths)
    {
        List<int> best = null;
        int bestDist = int.MaxValue, bestScore = int.MinValue;

        foreach (var p in paths)
        {
            int landing = p[p.Count - 1];
            int d = StepsToEncounter(landing, BoardTrack.Boss);
            if (d < 0) d = int.MaxValue - 1;

            int score = PathWeight(p);
            if (d < bestDist || (d == bestDist && score > bestScore))
            {
                best = p; bestDist = d; bestScore = score;
            }
        }

        return best ?? paths[0];
    }

    // 착지 칸만이 아니라 지나가는 칸 전부를 합산한다
    private int PathWeight(List<int> path)
    {
        int sum = 0;
        foreach (int idx in path) sum += board[idx].weight;
        return sum;
    }

    private int PathPriority(List<int> path)
    {
        int max = 0;
        foreach (int idx in path) max = Mathf.Max(max, board[idx].priorityWeight);
        return max;
    }

    // ── 보드 카드 효과 실행 ──────────────────────────────
    // GameManager가 보드 카드 사용 시 호출

    public void ExecuteBoardCardEffects(CardData card)
    {
        foreach (var effect in card.effects)
            ExecuteBoardEffect(effect);
    }

    private void ExecuteBoardEffect(CardEffect effect)
    {
        switch (effect.type)
        {
            case EffectType.BoardDiceBonus:   diceBonusOnce    += effect.value; break;
            case EffectType.BoardReroll:      allowRerollOnce   = true;         break;
            case EffectType.BoardFreezeBoss:  bossFreezeturns  += effect.value; break;
            case EffectType.BoardBlockMinion: blockNextBossMinion = true;       break;

            // 순간이동 (v0.6 재정의) — 메시에서 "원하는 칸"은 성립하지 않는다.
            // 이번 턴 주사위 값 이하의 거리라면 어디든 착지할 수 있다.
            case EffectType.BoardTeleport:    anyDistanceOnce   = true;         break;

            // 예언의 주사위 (v0.6 재정의) — "앞 N칸 확인"은 선형 개념이고 노드는 이미 다 보인다.
            // 대신 보스의 다음 주사위를 미리 굴려 보여준다. 레이스를 계산하는 정보다.
            case EffectType.BoardRevealNodes: RevealBossDice();                 break;

            // BoardReverseMove(역방향 이동)는 카드째 삭제됐다 (v0.6 — "되돌아가기 없음"과 충돌)
        }
    }

    private void RevealBossDice()
    {
        if (bossRollPreview <= 0)
            bossRollPreview = UnityEngine.Random.Range(1, 7);
        OnBossDiceRevealed?.Invoke(bossRollPreview);
    }

    // 직접 호출 API (보드 카드 외부 효과용)
    public void AddDiceBonus(int amount)          => diceBonusOnce += amount;
    public void SetRerollAllowed()                => allowRerollOnce = true;
    public void FreezeBoss(int turns)             => bossFreezeturns += turns;
    public void BlockNextBossMinion()             => blockNextBossMinion = true;

    // ── 유틸 ────────────────────────────────────────────

    private MinionData PickMinionFromPool()
    {
        if (bossData.minionPool == null || bossData.minionPool.Count == 0) return null;
        return bossData.minionPool[UnityEngine.Random.Range(0, bossData.minionPool.Count)];
    }

    private void ResetTurnModifiers()
    {
        diceBonusOnce    = 0;
        allowRerollOnce  = false;
        anyDistanceOnce  = false;
        // bossFreezeturns·blockNextBossMinion·bossRollPreview는 유지 (다음 턴에도 효과)
    }

    // ── 공개 조회 ────────────────────────────────────────

    // 보드를 새로 생성할 때마다 오른다 — 맵 뷰가 "같은 보드면 다시 짓지 않기" 판정에 쓴다
    public int      BoardVersion      { get; private set; }

    public int      PlayerPosition    => playerPosition;
    public int      BossPosition      => bossPosition;
    public int      CurrentSection    => currentSection;
    public int      SectionCount      => sectionCount;
    public bool     PlayerAtEncounter => playerAtEncounter;
    public bool     BossAtEncounter   => bossAtEncounter;
    public int      BossFreezeTurns   => bossFreezeturns;
    public NodeData CurrentEncounter  => board != null && sectionEncounters != null
                                       ? board[sectionEncounters[currentSection]] : null;

    public int PlayerStepsToEncounter => board == null ? -1 : StepsToEncounter(playerPosition, BoardTrack.Player);
    public int BossStepsToEncounter   => board == null ? -1 : StepsToEncounter(bossPosition,   BoardTrack.Boss);

    public NodeData GetNode(int index)
    {
        if (board == null || index < 0 || index >= board.Length) return null;
        return board[index];
    }
    public int      TotalNodes     => totalNodes;
    public int      PlayerGold     => playerGold;
    public int      BossGold       => bossGold;

    public void AddPlayerGold(int amount)
    {
        playerGold += amount;
        OnPlayerGoldChanged?.Invoke(playerGold);
    }

    public bool SpendPlayerGold(int amount)
    {
        if (playerGold < amount) return false;
        playerGold -= amount;
        OnPlayerGoldChanged?.Invoke(playerGold);
        return true;
    }
}
