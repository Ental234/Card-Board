using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 보드 페이즈 전체 흐름 관리
// 순서: 보드 카드 사용 → 주사위 → 플레이어 이동 → 노드 이벤트 → 보스 이동 → 보스 노드 이벤트 → 인카운터 판정
public class BoardPhaseManager : MonoBehaviour
{
    [SerializeField] private BoardGenerator boardGenerator;
    [SerializeField] private CombatManager  combatManager;

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<int, NodeType>  OnPlayerLanded;       // (nodeIndex, type) — 노드 이벤트 구독
    public event Action<int, NodeType>  OnBossLanded;         // (nodeIndex, type)
    public event Action<MinionData>     OnBossAcquiresMinion; // 보스 몬스터 칸 착지 시
    public event Action                 OnBoardCardPhaseStarted; // UI가 보드 손패를 연다
    public event Action                 OnBoardCardPhaseEnded;   // 주사위를 굴려 페이즈 종료
    public event Action<int>            OnDiceRolled;         // (최종 주사위 값)
    public event Action<int[]>          OnNodesRevealed;      // 예언의 주사위 — UI용
    public event Action                 OnEncounterTriggered; // 인카운터 발생
    public event Action<int>            OnPlayerGoldChanged;  // 월급 등 골드 변화
    public event Action<int>            OnBossGoldChanged;

    // ── 상태 ────────────────────────────────────────────

    private NodeData[]      board;
    private List<int>[]     predecessors;   // 역방향 이동용 — 들어오는 간선
    private int             playerPosition;
    private int             bossPosition;

    private PlayerCharacter player;
    private BossEntity      bossEntity;
    private BossData        bossData;
    private int             bossGold;
    private int             playerGold;

    private int  bossFreezeturns;       // 속박의 사슬 남은 턴
    private bool nextMoveReversed;       // 역방향 이동 카드
    private int  diceBonusOnce;         // 이번 주사위에만 추가
    private bool allowRerollOnce;       // 이번 주사위 재굴림 허용
    private bool blockNextBossMinion;   // 보스 다음 하수인 획득 차단
    private int  revealAheadCount;      // 예언의 주사위 — 미리 볼 칸 수

    private int  totalNodes => board?.Length ?? 0;
    private bool boardActive;
    private bool turnLoopRunning;

    // 전투·이벤트·상점이 진행되는 동안 보드 턴을 멈추기 위한 게이트.
    // 노드 이벤트를 처리하는 쪽(GameManager 등)이 Begin/EndResolution으로 여닫는다.
    private int pendingResolutions;
    public bool IsResolving => pendingResolutions > 0;

    public void BeginResolution() => pendingResolutions++;
    public void EndResolution()   => pendingResolutions = Mathf.Max(0, pendingResolutions - 1);

    // 월급 금액 (밸런싱 단계에서 조정)
    private const int SalaryAmount = 50;

    // 턴 사이 간격 (연출 여유)
    private const float TurnInterval = 0.4f;

    // ── 초기화 ──────────────────────────────────────────

    public void InitBoard(BoardStageData stageData, PlayerCharacter pc, BossEntity boss, BossData bossConfig)
    {
        player      = pc;
        bossEntity  = boss;
        bossData    = bossConfig;

        // 골드는 런 단위 자원이라 여기서 건드리지 않는다.
        // 스테이지마다 초기화하면 보스 보상 골드가 다음 스테이지에서 증발한다.
        // 런 시작 시 GameManager가 ResetRunResources()로 초기화한다.

        board           = boardGenerator.Generate(stageData);
        BuildPredecessors();
        playerPosition  = 0;
        bossPosition    = Mathf.Clamp(stageData.bossStartNode, 0, board.Length - 1);

        board[playerPosition].ClaimPlayer();
        board[bossPosition].ClaimBoss();

        ResetTurnModifiers();
        boardActive = true;
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

        // ② 보드 카드 페이즈 — 주사위를 굴리기 전에 카드로 개입할 기회
        diceRollRequested = false;
        player.StartBoardPhase();
        OnBoardCardPhaseStarted?.Invoke();

        // UI가 없으면 무한 대기하므로, 구독자가 없을 때만 즉시 통과시킨다
        if (OnBoardCardPhaseStarted != null)
            yield return new WaitUntil(() => IsBoardCardPhaseComplete());

        OnBoardCardPhaseEnded?.Invoke();
        player.EndBoardPhase();

        // ② 예언의 주사위 미리보기 (사용한 경우)
        if (revealAheadCount > 0)
        {
            int[] preview = PeekAheadNodes(playerPosition, revealAheadCount);
            OnNodesRevealed?.Invoke(preview);
            revealAheadCount = 0;
            yield return new WaitForSeconds(1.5f);  // UI가 노드 표시할 시간
        }

        // ③ 주사위 굴리기 + 이동
        int steps = RollDice();
        OnDiceRolled?.Invoke(steps);
        yield return new WaitForSeconds(0.5f);

        if (nextMoveReversed)
            playerPosition = WalkBackward(playerPosition, steps);
        else
            yield return StartCoroutine(WalkPlayerRoutine(playerPosition, steps));

        ArriveAt(playerPosition, isPlayer: true);

        // ④ 플레이어 노드 이벤트
        yield return StartCoroutine(TriggerPlayerNodeEvent());

        // ⑤ 보스 턴 — 턴 수를 세고 패닉 조건을 확인한다.
        //    이 호출이 없으면 turnCount가 늘지 않아 패닉이 영영 발동하지 않는다.
        bossEntity.OnBoardTurn();

        // 보스 이동 (속박 중이면 스킵)
        if (bossFreezeturns > 0)
        {
            bossFreezeturns--;
        }
        else
        {
            bossPosition = CalcBossDestination();
            ArriveAt(bossPosition, isPlayer: false);

            // ⑥ 보스 노드 이벤트
            yield return StartCoroutine(TriggerBossNodeEvent());
        }

        // ⑦ 인카운터 판정
        if (playerPosition == bossPosition)
        {
            OnEncounterTriggered?.Invoke();
            // CombatManager.StartCombat()은 구독자(GameManager 등)가 호출
            yield return null;
            yield return new WaitUntil(() => !IsResolving);
        }

        ResetTurnModifiers();
    }

    // 보드 카드 페이즈 — "주사위 굴리기"를 누를 때까지 대기한다.
    // UI가 RequestDiceRoll()을 호출하면 페이즈가 끝나고 주사위로 넘어간다.
    private bool diceRollRequested;

    public void RequestDiceRoll() => diceRollRequested = true;

    private bool IsBoardCardPhaseComplete() => diceRollRequested;

    // ── 주사위 ───────────────────────────────────────────

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

    // 역방향 이동 카드를 위해 들어오는 간선을 미리 모아둔다
    private void BuildPredecessors()
    {
        predecessors = new List<int>[board.Length];
        for (int i = 0; i < board.Length; i++)
            predecessors[i] = new List<int>();

        for (int i = 0; i < board.Length; i++)
            foreach (int next in board[i].exits)
                predecessors[next].Add(i);
    }

    // ── 이동 (그래프 순회) ──────────────────────────────

    // 교차로에 도달하면 UI에 방향 선택을 요청한다. (현재 노드, 고를 수 있는 출구들)
    public event Action<NodeData, List<int>> OnBranchChoiceRequested;

    // UI가 고른 출구. -1이면 아직 미선택.
    private int pendingBranchChoice = -1;

    // 선택 UI가 호출
    public void SubmitBranchChoice(int nodeIndex) => pendingBranchChoice = nodeIndex;

    // 플레이어 이동 — 교차로를 만나면 입력을 기다려야 하므로 코루틴이다.
    private IEnumerator WalkPlayerRoutine(int from, int steps)
    {
        int cur = from;

        for (int i = 0; i < steps; i++)
        {
            var node = board[cur];
            if (node.exits.Count == 0) break;        // 이론상 없음 (막다른 길 미사용)

            if (node.exits.Count == 1)
            {
                cur = node.exits[0];
                continue;
            }

            // 교차로 — 선택을 기다린다
            pendingBranchChoice = -1;
            OnBranchChoiceRequested?.Invoke(node, node.exits);

            // UI가 없으면 무한 대기하므로, 구독자가 없으면 본선으로 진행한다
            if (OnBranchChoiceRequested == null)
            {
                cur = node.exits[0];
                continue;
            }

            yield return new WaitUntil(() => pendingBranchChoice >= 0);
            cur = pendingBranchChoice;
        }

        playerPosition = cur;
    }

    // 보스는 경로를 미리 정해두므로 대기 없이 진행한다
    private int WalkForward(int from, int steps)
    {
        int cur = from;
        for (int i = 0; i < steps; i++)
        {
            var exits = board[cur].exits;
            if (exits.Count == 0) break;
            cur = exits[0];
        }
        return cur;
    }

    // 역방향 이동 — 들어오는 간선을 따라 거꾸로 간다
    private int WalkBackward(int from, int steps)
    {
        int cur = from;
        for (int i = 0; i < steps; i++)
        {
            var prev = predecessors[cur];
            if (prev.Count == 0) break;
            cur = prev[0];
        }
        return cur;
    }

    private void ArriveAt(int position, bool isPlayer)
    {
        var node = board[position];
        if (isPlayer) node.ClaimPlayer();
        else          node.ClaimBoss();
    }

    // 순간이동 (보드 카드 — 특정 노드로 직접 이동)
    public void TeleportPlayer(int targetNodeIndex)
    {
        if (board == null || board.Length == 0) return;
        playerPosition = Mathf.Clamp(targetNodeIndex, 0, board.Length - 1);
        board[playerPosition].ClaimPlayer();
    }

    // ── 경로 열거 ───────────────────────────────────────

    // from에서 정확히 steps칸 이동하는 모든 경로.
    // 각 경로는 지나간 노드 인덱스 목록 (출발지 제외, 착지 포함).
    // 분기 계수가 작아 (주사위 6 기준 최대 수십 개) 완전 열거해도 부담 없다.
    private List<List<int>> EnumeratePaths(int from, int steps)
    {
        var results = new List<List<int>>();
        var buffer  = new List<int>(steps);

        void Walk(int cur, int remaining)
        {
            if (remaining == 0)
            {
                results.Add(new List<int>(buffer));
                return;
            }

            foreach (int next in board[cur].exits)
            {
                buffer.Add(next);
                Walk(next, remaining - 1);
                buffer.RemoveAt(buffer.Count - 1);
            }
        }

        Walk(from, steps);
        return results;
    }

    // 그래프 최단 거리 (패닉 추격용)
    private int GraphDistance(int from, int to)
    {
        if (from == to) return 0;

        var dist    = new Dictionary<int, int> { [from] = 0 };
        var queue   = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (int next in board[cur].exits)
            {
                if (dist.ContainsKey(next)) continue;
                dist[next] = dist[cur] + 1;
                if (next == to) return dist[next];
                queue.Enqueue(next);
            }
        }

        return int.MaxValue;   // 도달 불가 (정상 보드에선 발생하지 않음)
    }

    // ── 노드 이벤트 ──────────────────────────────────────

    private IEnumerator TriggerPlayerNodeEvent()
    {
        var node = board[playerPosition];
        OnPlayerLanded?.Invoke(playerPosition, node.type);

        if (node.type == NodeType.Salary)
        {
            playerGold += SalaryAmount;
            OnPlayerGoldChanged?.Invoke(playerGold);
        }

        // 구독자(GameManager·EventManager 등)가 전투·이벤트·상점을 열면
        // BeginResolution으로 게이트가 닫힌다. 끝날 때까지 보드 턴을 멈춘다.
        yield return null;                              // 구독자가 게이트를 닫을 한 프레임
        yield return new WaitUntil(() => !IsResolving);
    }

    private IEnumerator TriggerBossNodeEvent()
    {
        var node = board[bossPosition];
        OnBossLanded?.Invoke(bossPosition, node.type);

        if (node.type == NodeType.Salary)
        {
            bossGold += SalaryAmount;
            OnBossGoldChanged?.Invoke(bossGold);
        }

        // 보스 몬스터 칸 착지 → 하수인 획득
        // 단, 플레이어가 먼저 밟은 칸이면 선점 → 획득 불가
        bool isMonsterNode = node.type == NodeType.Monster || node.type == NodeType.Elite;
        if (isMonsterNode && !node.claimedByPlayer)
        {
            if (blockNextBossMinion)
            {
                blockNextBossMinion = false;  // 차단 1회 소모
            }
            else
            {
                MinionData minion = PickMinionFromPool();
                if (minion != null)
                    OnBossAcquiresMinion?.Invoke(minion);
            }
        }

        yield return null;
    }

    // ── 보스 이동 AI ─────────────────────────────────────

    // 보스도 주사위로 거리를 정한다. AI가 판단하는 건 "교차로에서 어느 경로로 갈지"뿐.
    //   일반: 경로상 노드 가중치 총합이 최대인 경로. 단 우선 가중치가 있으면 무조건 우선.
    //   패닉: 가중치를 버리고 플레이어에게 가장 가까워지는 경로 (고정 패턴)
    private int CalcBossDestination()
    {
        int steps = RollBossDice();
        var paths = EnumeratePaths(bossPosition, steps);
        if (paths.Count == 0) return bossPosition;

        var best = bossEntity.IsPanicMode
                 ? PickChasePath(paths)
                 : PickWeightedPath(paths);

        return best[best.Count - 1];   // 착지 칸
    }

    private int RollBossDice()
    {
        int roll = UnityEngine.Random.Range(1, 7);   // 1d6 — 플레이어와 동일

        // 패닉 시 추격이 빨라진다 (BossData.baseMoveDistance를 추가 이동으로 사용)
        if (bossEntity.IsPanicMode)
            roll += Mathf.Max(0, bossData.baseMoveDistance);

        return roll;
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

    // 패닉 고정 패턴 — 플레이어에게 가장 가까워지는 착지 칸
    private List<int> PickChasePath(List<List<int>> paths)
    {
        var best     = paths[0];
        int bestDist = GraphDistance(best[best.Count - 1], playerPosition);

        foreach (var p in paths)
        {
            int landing = p[p.Count - 1];
            if (landing == playerPosition) return p;   // 즉시 인카운터

            int d = GraphDistance(landing, playerPosition);
            if (d < bestDist) { bestDist = d; best = p; }
        }

        return best;
    }

    // 착지 칸만이 아니라 지나가는 칸 전부를 합산한다
    private int PathWeight(List<int> path)
    {
        int sum = 0;
        foreach (int idx in path)
        {
            var node = board[idx];
            // 플레이어가 이미 선점한 칸은 보스에게 가치가 없다
            if (node.claimedByPlayer) continue;
            sum += node.weight;
        }
        return sum;
    }

    private int PathPriority(List<int> path)
    {
        int max = 0;
        foreach (int idx in path)
        {
            var node = board[idx];
            if (node.claimedByPlayer) continue;
            max = Mathf.Max(max, node.priorityWeight);
        }
        return max;
    }

    // 노드별 가중치는 BoardGenerator가 생성 시 NodeData.weight에 심어둔다.
    // (예전에는 여기서 타입별로 계산했지만, 경로 총합 방식에서는 노드가 직접 들고 있어야 한다)

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
            case EffectType.BoardReverseMove: nextMoveReversed  = true;         break;
            case EffectType.BoardFreezeBoss:  bossFreezeturns  += effect.value; break;
            case EffectType.BoardRevealNodes: revealAheadCount  = effect.value; break;
            case EffectType.BoardBlockMinion: blockNextBossMinion = true;       break;
            case EffectType.BoardTeleport:
                if (effect.value >= 0)
                    TeleportPlayer(effect.value);
                // effect.value == -1: UI가 목적지 선택 후 TeleportPlayer() 직접 호출
                break;
        }
    }

    // 직접 호출 API (보드 카드 외부 효과용)
    public void AddDiceBonus(int amount)          => diceBonusOnce += amount;
    public void SetRerollAllowed()                => allowRerollOnce = true;
    public void SetReverseMove()                  => nextMoveReversed = true;
    public void FreezeBoss(int turns)             => bossFreezeturns += turns;
    public void BlockNextBossMinion()             => blockNextBossMinion = true;
    public void SetRevealAhead(int count)         => revealAheadCount = count;

    // ── 유틸 ────────────────────────────────────────────

    // 예언의 주사위 — 앞쪽 노드 미리보기.
    // 교차로에서는 본선(첫 출구)을 따라간 경로를 보여준다.
    private int[] PeekAheadNodes(int from, int count)
    {
        var result = new int[count];
        int cur = from;

        for (int i = 0; i < count; i++)
        {
            var exits = board[cur].exits;
            cur = exits.Count > 0 ? exits[0] : cur;
            result[i] = cur;
        }
        return result;
    }

    private MinionData PickMinionFromPool()
    {
        if (bossData.minionPool == null || bossData.minionPool.Count == 0) return null;
        return bossData.minionPool[UnityEngine.Random.Range(0, bossData.minionPool.Count)];
    }

    private void ResetTurnModifiers()
    {
        diceBonusOnce    = 0;
        allowRerollOnce  = false;
        nextMoveReversed = false;
        revealAheadCount = 0;
        // bossFreezeturns·blockNextBossMinion은 유지 (다음 턴에도 효과)
    }

    // ── 공개 조회 ────────────────────────────────────────

    public int      PlayerPosition => playerPosition;
    public int      BossPosition   => bossPosition;
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
