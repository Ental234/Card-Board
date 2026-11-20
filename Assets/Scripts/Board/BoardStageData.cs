using UnityEngine;

// 스테이지 한 개의 보드 생성 설정 (v0.6 — 구획 단위 일방통행 메시)
//
// 스테이지 = 구획 sectionCount개. 각 구획은
//   시작 칸(또는 이전 조우지점) → 플레이어 갈래 N개 / 보스 갈래 M개 → 조우지점
// 마지막 구획의 조우지점이 최종 결전이다.
[CreateAssetMenu(fileName = "NewStage", menuName = "Game/Board Stage Data")]
public class BoardStageData : ScriptableObject
{
    public string stageName;

    [Header("구획")]
    [Min(1)] public int sectionCount = 3;

    [Header("플레이어 트랙")]
    [Range(2, 3)] public int playerLaneCount = 2;  // 갈래 수 — 기본 2, 많아도 3
    [Min(1)] public int laneLengthMin   = 18;      // 가장 짧은 갈래의 노드 수 (짧은 길)
    [Min(1)] public int laneLengthMax   = 24;      // 가장 긴 갈래의 노드 수 (돌아가는 길)
    [Min(0)] public int crossLinksPerLanePair = 2; // 이웃 갈래 사이 건너가는 간선 수

    [Header("보스 트랙")]
    [Range(2, 3)] public int bossLaneCount = 2;
    [Min(0)] public int bossCrossLinks = 1;

    [Header("보스 AI 가중치")]
    public int shopPriorityWeight  = 10;
    public int elitePriorityWeight = 8;

    [Header("노드 타입 비율 (합 1 권장 — 모자란 만큼은 비율대로 채운다)")]
    [Range(0, 1)] public float monsterRatio  = 0.30f;
    [Range(0, 1)] public float eliteRatio    = 0.15f;
    [Range(0, 1)] public float eventRatio    = 0.20f;
    [Range(0, 1)] public float shopRatio     = 0.10f;
    [Range(0, 1)] public float restRatio     = 0.10f;
    [Range(0, 1)] public float treasureRatio = 0.10f;

    [Header("배치 제약")]
    public int maxConsecutiveSameType = 2;
}
