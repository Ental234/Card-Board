using System.Collections.Generic;

// 보드 노드 한 칸의 런타임 데이터
[System.Serializable]
public class NodeData
{
    public int      index;
    public NodeType type;

    // 이 노드에서 나갈 수 있는 노드들 (일방통행 — 되돌아가는 간선은 없다)
    public List<int> exits = new();
    public bool IsIntersection => exits.Count > 1;

    // ── 구획 구조 (v0.6) ────────────────────────────────

    public int        section;        // 속한 구획 (0-based). 조우지점은 자신이 끝내는 구획
    public BoardTrack track;          // 누구의 길인가
    public int        lane;           // 트랙 안에서 몇 번째 갈래인가 (배치 힌트)
    public float      progress;       // 구획 안 진행도 0(시작)~1(조우지점) (배치 힌트)
    public EncounterKind encounterKind; // type == Encounter일 때만 의미

    public bool IsEncounter => type == NodeType.Encounter;

    // ── 보스 AI ─────────────────────────────────────────

    public int  weight;               // 경로 가중치 합산용
    public int  priorityWeight;       // 0보다 크면 이 칸을 지나는 경로를 절대 우선
    public bool HasPriority => priorityWeight > 0;

    // 구획 내 고정 노드(시작 칸·조우지점) 표시
    public bool isFixed;

    // 방문 기록 (UI 표시·보스 AI가 참조)
    public bool claimedByPlayer;
    public bool claimedByBoss;

    public NodeData(int index, NodeType type)
    {
        this.index = index;
        this.type  = type;
    }

    public void ClaimPlayer() => claimedByPlayer = true;
    public void ClaimBoss()   => claimedByBoss   = true;

    // 이 트랙의 주인이 밟을 수 있는 노드인가
    public bool IsWalkableBy(BoardTrack mover) => track == BoardTrack.Shared || track == mover;
}
