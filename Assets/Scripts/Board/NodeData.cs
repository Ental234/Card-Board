using System.Collections.Generic;

// 보드 그래프의 노드 하나
// BoardGenerator가 생성, BoardPhaseManager가 소유
//
// 보드는 단일 원형 트랙이 아니라 분기 그래프다.
// exits가 2개 이상이면 교차로 — 플레이어는 방향을 고르고, 보스는 AI가 판단한다.
// 모든 분기는 본선에 다시 합류한다 (막다른 길 없음).
[System.Serializable]
public class NodeData
{
    public int      index;
    public NodeType type;

    // ── 그래프 연결 ─────────────────────────────────────

    // 이 노드에서 나갈 수 있는 노드 인덱스 목록 (진행 방향)
    public List<int> exits = new();

    public bool IsIntersection => exits.Count > 1;

    // ── 보스 AI 가중치 ──────────────────────────────────

    // 경로 판단 시 합산되는 일반 가중치
    public int weight;

    // 0보다 크면 일반 가중치 총합과 무관하게 그 경로를 무조건 우선한다.
    // "보스가 반드시 노리는 것"을 표현 (렐릭 상점 선점, 정예 칸 하수인 확보 등)
    public int priorityWeight;

    public bool HasPriority => priorityWeight > 0;

    // ── 재배치 ──────────────────────────────────────────

    // 시작 칸·보스 칸·주요 교차로는 루프 재배치 대상에서 제외
    public bool isFixed;

    // ── 선점 추적 ───────────────────────────────────────

    // 보스가 먼저 밟은 칸은 플레이어 보상 없음 (반대도 동일)
    public bool claimedByPlayer;
    public bool claimedByBoss;

    public NodeData(int index, NodeType type)
    {
        this.index = index;
        this.type  = type;
    }

    public void ClaimPlayer() => claimedByPlayer = true;
    public void ClaimBoss()   => claimedByBoss   = true;
}
