using System.Collections.Generic;
using UnityEngine;

// BoardStageData를 읽어 런타임 보드 그래프를 생성한다.
//
// 구조: 본선(메인 루프) + 우회 분기.
//   · 분기는 본선의 어느 칸에서 갈라져 몇 칸 뒤 본선에 다시 합류한다 (막다른 길 없음)
//   · 갈라지는 칸은 exits가 2개 = 교차로
// 규칙: index 0 = Start 고정, 동일 타입 maxConsecutiveSameType 초과 연속 금지
public class BoardGenerator : MonoBehaviour
{
    // 보스 AI가 경로를 판단할 때 합산하는 노드별 기본 가중치
    private static int GetTypeWeight(NodeType type) => type switch
    {
        NodeType.Elite    => 5,
        NodeType.Treasure => 4,
        NodeType.Monster  => 3,
        NodeType.Shop     => 3,
        NodeType.Salary   => 2,
        NodeType.Event    => 1,
        NodeType.Rest     => 1,
        _                 => 0,   // Empty, Start, Curse
    };

    public NodeData[] Generate(BoardStageData stageData)
    {
        int mainCount = Mathf.Max(4, stageData.totalNodes);
        var nodes     = new List<NodeData>(mainCount);

        // ── ① 본선 루프 ────────────────────────────────
        for (int i = 0; i < mainCount; i++)
            nodes.Add(new NodeData(i, NodeType.Empty));

        for (int i = 0; i < mainCount; i++)
            nodes[i].exits.Add((i + 1) % mainCount);

        // ── ② 우회 분기 붙이기 ──────────────────────────
        AttachBranches(nodes, stageData, mainCount);

        // ── ③ 노드 타입 배분 ────────────────────────────
        AssignTypes(nodes, stageData);

        // ── ④ 가중치 & 고정 표시 ────────────────────────
        FinalizeNodes(nodes, stageData);

        return nodes.ToArray();
    }

    // ── 분기 생성 ────────────────────────────────────────

    private void AttachBranches(List<NodeData> nodes, BoardStageData data, int mainCount)
    {
        if (data.branchCount <= 0) return;

        // 갈라지는 지점이 서로 겹치지 않도록 본선을 균등 분할해 후보를 잡는다
        var splitPoints = PickSplitPoints(data.branchCount, mainCount);

        foreach (int split in splitPoints)
        {
            int span = Random.Range(data.branchSpanMin, data.branchSpanMax + 1);
            int rejoin = (split + span) % mainCount;
            if (rejoin == split) continue;   // 한 바퀴 돌아 제자리면 무의미

            int length = Random.Range(data.branchLengthMin, data.branchLengthMax + 1);
            if (length <= 0) continue;

            // 우회로 노드들을 새 인덱스로 추가
            int first = nodes.Count;
            for (int i = 0; i < length; i++)
                nodes.Add(new NodeData(nodes.Count, NodeType.Empty));

            // 갈라짐: 본선 split 칸에 두 번째 출구 추가 → 교차로가 된다
            nodes[split].exits.Add(first);

            // 우회로 내부 연결 후 본선으로 합류
            for (int i = 0; i < length; i++)
            {
                int cur  = first + i;
                int next = (i == length - 1) ? rejoin : first + i + 1;
                nodes[cur].exits.Add(next);
            }
        }
    }

    // 본선을 균등 분할해 분기 시작점을 고른다 (0번 = 시작 칸은 제외)
    private List<int> PickSplitPoints(int branchCount, int mainCount)
    {
        var points = new List<int>();
        int stride  = Mathf.Max(2, mainCount / Mathf.Max(1, branchCount));

        for (int b = 0; b < branchCount; b++)
        {
            int p = 1 + b * stride;
            if (p >= mainCount) break;
            points.Add(p);
        }
        return points;
    }

    // ── 노드 타입 배분 ───────────────────────────────────

    private void AssignTypes(List<NodeData> nodes, BoardStageData data)
    {
        nodes[0].type = NodeType.Start;

        int need = nodes.Count - 1;                 // 0번(Start) 제외
        var pool = BuildPool(data, need);
        Shuffle(pool);
        var placed = PlaceWithConstraint(pool, data.maxConsecutiveSameType);

        for (int i = 0; i < placed.Count; i++)
            nodes[i + 1].type = placed[i];
    }

    private List<NodeType> BuildPool(BoardStageData data, int count)
    {
        var pool = new List<NodeType>();
        AddToPool(pool, NodeType.Monster,  data.monsterRatio,  count);
        AddToPool(pool, NodeType.Elite,    data.eliteRatio,    count);
        AddToPool(pool, NodeType.Event,    data.eventRatio,    count);
        AddToPool(pool, NodeType.Shop,     data.shopRatio,     count);
        AddToPool(pool, NodeType.Rest,     data.restRatio,     count);
        AddToPool(pool, NodeType.Treasure, data.treasureRatio, count);
        AddToPool(pool, NodeType.Curse,    data.curseRatio,    count);
        AddToPool(pool, NodeType.Salary,   data.salaryRatio,   count);

        while (pool.Count < count) pool.Add(NodeType.Empty);      // 나머지 = Empty
        while (pool.Count > count) pool.RemoveAt(pool.Count - 1); // 초과분 제거

        return pool;
    }

    private void AddToPool(List<NodeType> pool, NodeType type, float ratio, int total)
    {
        int count = Mathf.RoundToInt(ratio * total);
        for (int i = 0; i < count; i++)
            pool.Add(type);
    }

    // ── 가중치 & 고정 표시 ───────────────────────────────

    private void FinalizeNodes(List<NodeData> nodes, BoardStageData data)
    {
        foreach (var n in nodes)
        {
            n.weight = GetTypeWeight(n.type);

            n.priorityWeight = n.type switch
            {
                NodeType.Shop  => data.shopPriorityWeight,
                NodeType.Elite => data.elitePriorityWeight,
                _              => 0,
            };

            // 시작 칸과 교차로는 루프 재배치 대상에서 제외
            n.isFixed = n.index == 0 || n.IsIntersection;
        }
    }

    // ── 연속 제한 배치 ───────────────────────────────────

    // 셔플된 풀에서 동일 타입이 maxConsecutive 이상 연속되면 스왑
    private List<NodeType> PlaceWithConstraint(List<NodeType> pool, int maxConsecutive)
    {
        var result = new List<NodeType>(pool);
        int maxAttempts = result.Count * 3;

        for (int i = 0; i < result.Count; i++)
        {
            if (!IsViolating(result, i, maxConsecutive)) continue;

            // 뒤에 교환할 대상이 없으면 해결 불가 — Random.Range(n, n)는 n을 그대로
            // 돌려주므로 그냥 두면 범위를 벗어난 인덱스를 참조하게 된다.
            if (i + 1 >= result.Count) break;

            bool swapped = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int j = Random.Range(i + 1, result.Count);
                if (result[j] == result[i]) continue;

                (result[i], result[j]) = (result[j], result[i]);
                if (!IsViolating(result, i, maxConsecutive)) { swapped = true; break; }
                (result[i], result[j]) = (result[j], result[i]);  // 원복
            }

            if (!swapped) break;  // 완벽한 해가 없으면 그대로 진행
        }

        return result;
    }

    private bool IsViolating(List<NodeType> list, int idx, int maxConsecutive)
    {
        if (idx < maxConsecutive) return false;
        NodeType t = list[idx];
        for (int k = 1; k <= maxConsecutive; k++)
            if (list[idx - k] != t) return false;
        return true;
    }

    // ── 셔플 ─────────────────────────────────────────────

    private static void Shuffle(List<NodeType> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
