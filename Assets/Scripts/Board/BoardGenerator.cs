using System.Collections.Generic;
using UnityEngine;

// BoardStageData를 읽어 스테이지 하나의 보드 그래프를 생성한다. (v0.6 — 구획 단위 일방통행 메시)
//
// 구조:
//   0번 = 시작 칸(공용)
//   구획마다  [구획 시작] ─┬─ 플레이어 갈래 N개 ─┬─ [조우지점]
//                         └─ 보스 갈래 M개 ────┘
//   조우지점이 다음 구획의 시작이 된다. 마지막 구획의 조우지점 = 최종 결전.
//
// · 갈래마다 길이가 다르다 — 짧은 길 / 돌아가는 길
// · 이웃 갈래 사이에 건너가는 간선을 몇 개 둔다. 진행도가 항상 커지는 쪽으로만 이어서
//   그래프가 순환하지 않는다 (일방통행 보장)
// · 트랙끼리는 섞이지 않는다. 합류는 조우지점에서만 일어난다
public class BoardGenerator : MonoBehaviour
{
    // 보스 AI가 경로를 판단할 때 합산하는 노드별 기본 가중치
    private static int GetTypeWeight(NodeType type) => type switch
    {
        NodeType.Elite    => 5,
        NodeType.Treasure => 4,
        NodeType.Monster  => 3,
        NodeType.Shop     => 3,
        NodeType.Event    => 1,
        NodeType.Rest     => 1,
        _                 => 0,   // Start, Curse, Encounter
    };

    public NodeData[] Generate(BoardStageData data)
    {
        var nodes = new List<NodeData>();

        var start = AddNode(nodes, NodeType.Start, BoardTrack.Shared, section: 0, lane: 0, progress: 0f);
        start.isFixed = true;

        int sectionStart = start.index;
        int sections     = Mathf.Max(1, data.sectionCount);

        for (int s = 0; s < sections; s++)
        {
            bool isLast = s == sections - 1;

            // 조우지점을 먼저 만들어 두고 갈래 끝을 여기로 잇는다
            var encounter = AddNode(nodes, NodeType.Encounter, BoardTrack.Shared, s, lane: 0, progress: 1f);
            encounter.isFixed       = true;
            encounter.encounterKind = isLast ? EncounterKind.Final
                                    : (s % 2 == 0 ? EncounterKind.Supply : EncounterKind.Camp);

            BuildTrack(nodes, data, BoardTrack.Player, s, sectionStart, encounter.index,
                       data.playerLaneCount, data.crossLinksPerLanePair);
            BuildTrack(nodes, data, BoardTrack.Boss,   s, sectionStart, encounter.index,
                       data.bossLaneCount,   data.bossCrossLinks);

            sectionStart = encounter.index;
        }

        FinalizeNodes(nodes, data);
        return nodes.ToArray();
    }

    // ── 트랙 생성 ────────────────────────────────────────

    // 한 갈림길의 갈래는 기본 2개, 많아도 3개 — 더 많으면 선택이 "고민"이 아니라 "훑어보기"가 된다
    public const int MinLanes = 2;
    public const int MaxLanes = 3;

    private void BuildTrack(List<NodeData> nodes, BoardStageData data, BoardTrack track,
                            int section, int from, int to, int laneCount, int crossLinks)
    {
        laneCount = Mathf.Clamp(laneCount, MinLanes, MaxLanes);

        var lengths = PickLaneLengths(laneCount, data.laneLengthMin, data.laneLengthMax);
        var lanes   = new List<List<int>>(laneCount);

        for (int l = 0; l < laneCount; l++)
        {
            var lane = new List<int>(lengths[l]);
            for (int i = 0; i < lengths[l]; i++)
            {
                float progress = (i + 1f) / (lengths[l] + 1f);
                lane.Add(AddNode(nodes, NodeType.Monster, track, section, l, progress).index);
            }

            // 구획 시작 → 갈래 → 조우지점
            nodes[from].exits.Add(lane[0]);
            for (int i = 0; i < lane.Count - 1; i++)
                nodes[lane[i]].exits.Add(lane[i + 1]);
            nodes[lane[lane.Count - 1]].exits.Add(to);

            lanes.Add(lane);
        }

        for (int l = 0; l + 1 < laneCount; l++)
        {
            AddCrossLinks(nodes, lanes[l], lanes[l + 1], crossLinks);
            AddCrossLinks(nodes, lanes[l + 1], lanes[l], crossLinks);
        }

        foreach (var lane in lanes)
            AssignTypes(nodes, lane, data);
    }

    // 갈래 길이 — 첫 갈래는 짧은 길, 마지막 갈래는 돌아가는 길로 고정하고 사이는 고르게 벌린다.
    // 그래야 매 구획 "짧은 길 vs 돌아가는 길" 선택이 반드시 생긴다.
    // 순서는 섞는다 — 늘 위가 짧으면 읽을 것도 없어진다.
    private static List<int> PickLaneLengths(int count, int min, int max)
    {
        min = Mathf.Max(1, min);
        max = Mathf.Max(min, max);

        var lengths = new List<int>(count);
        for (int l = 0; l < count; l++)
        {
            float t = count == 1 ? 0.5f : (float)l / (count - 1);
            int   baseLen = Mathf.RoundToInt(Mathf.Lerp(min, max, t));
            int   jitter  = (l == 0 || l == count - 1) ? 0 : Random.Range(-1, 2);
            lengths.Add(Mathf.Clamp(baseLen + jitter, min, max));
        }

        for (int i = lengths.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (lengths[i], lengths[j]) = (lengths[j], lengths[i]);
        }
        return lengths;
    }

    // a 갈래의 한 노드에서 b 갈래의 "조금 더 앞선" 노드로 건너가는 간선.
    // 진행도가 엄격히 커지는 쪽으로만 이으므로 순환이 생기지 않는다.
    private static void AddCrossLinks(List<NodeData> nodes, List<int> a, List<int> b, int count)
    {
        if (count <= 0 || a.Count < 2 || b.Count < 2) return;

        // 갈래 전체에 고르게 흩뿌린다 — 한곳에 몰리면 사실상 한 번의 선택이 된다
        for (int k = 0; k < count; k++)
        {
            float lo = (float)k / count;
            float hi = (k + 1f) / count;
            int   i  = Mathf.Clamp(Mathf.FloorToInt(Random.Range(lo, hi) * (a.Count - 1)), 0, a.Count - 2);

            float fromProgress = nodes[a[i]].progress;
            int   target = -1;
            foreach (int idx in b)
            {
                if (nodes[idx].progress > fromProgress) { target = idx; break; }
            }
            if (target < 0) continue;

            var exits = nodes[a[i]].exits;
            if (!exits.Contains(target)) exits.Add(target);
        }
    }

    // ── 노드 타입 배분 ───────────────────────────────────

    private void AssignTypes(List<NodeData> nodes, List<int> lane, BoardStageData data)
    {
        var pool   = BuildPool(data, lane.Count);
        Shuffle(pool);
        var placed = PlaceWithConstraint(pool, data.maxConsecutiveSameType);

        for (int i = 0; i < lane.Count; i++)
            nodes[lane[i]].type = placed[i];
    }

    // 비율대로 개수를 잡고, 반올림으로 모자란 칸은 비율을 가중치로 삼아 채운다.
    // (예전엔 나머지를 빈 칸으로 채웠다. 빈 칸은 v0.6에서 폐기)
    private static List<NodeType> BuildPool(BoardStageData data, int count)
    {
        var table = new (NodeType type, float ratio)[]
        {
            (NodeType.Monster,  data.monsterRatio),
            (NodeType.Elite,    data.eliteRatio),
            (NodeType.Event,    data.eventRatio),
            (NodeType.Shop,     data.shopRatio),
            (NodeType.Rest,     data.restRatio),
            (NodeType.Treasure, data.treasureRatio),
            // 저주 노드는 폐기 (2026-09-27) — 밟으면 손해만 보는 칸이라 이벤트로 통합했다
        };

        var pool = new List<NodeType>(count);
        foreach (var (type, ratio) in table)
            for (int i = 0; i < Mathf.FloorToInt(ratio * count); i++)
                pool.Add(type);

        float total = 0f;
        foreach (var (_, ratio) in table) total += ratio;

        while (pool.Count < count)
        {
            if (total <= 0f) { pool.Add(NodeType.Monster); continue; }

            float roll = Random.Range(0f, total);
            foreach (var (type, ratio) in table)
            {
                roll -= ratio;
                if (roll <= 0f) { pool.Add(type); break; }
            }
            if (pool.Count < count && roll > 0f) pool.Add(NodeType.Monster);  // 부동소수 끝자락
        }

        while (pool.Count > count) pool.RemoveAt(pool.Count - 1);
        return pool;
    }

    // ── 가중치 & 고정 표시 ───────────────────────────────

    private static void FinalizeNodes(List<NodeData> nodes, BoardStageData data)
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
        }
    }

    private static NodeData AddNode(List<NodeData> nodes, NodeType type, BoardTrack track,
                                    int section, int lane, float progress)
    {
        var n = new NodeData(nodes.Count, type)
        {
            track    = track,
            section  = section,
            lane     = lane,
            progress = progress,
        };
        nodes.Add(n);
        return n;
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
        if (maxConsecutive <= 0 || idx < maxConsecutive) return false;
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
