using UnityEngine;

// 스테이지별 보드 구성 설정 (디자인 타임)
// BoardGenerator가 이 데이터를 읽어 런타임 NodeData[] 생성
[CreateAssetMenu(fileName = "NewBoardStage", menuName = "Game/Board Stage Data")]
public class BoardStageData : ScriptableObject
{
    [Header("기본 설정")]
    public string stageName;
    public int    totalNodes = 20;  // 본선(메인 루프) 칸 수. 분기 칸은 여기에 추가로 붙는다

    [Header("분기 구조")]
    // 본선에서 갈라졌다가 다시 합류하는 우회로. 막다른 길은 만들지 않는다.
    public int branchCount     = 2;   // S1=1~2, S2=3~4, S3=多
    public int branchLengthMin = 2;   // 우회로 칸 수
    public int branchLengthMax = 3;
    public int branchSpanMin   = 3;   // 갈라진 뒤 본선 몇 칸 뒤에서 합류하는지
    public int branchSpanMax   = 4;

    [Header("보스 AI 우선 가중치")]
    // 0보다 크면 일반 가중치 총합과 무관하게 해당 경로를 무조건 우선한다
    public int shopPriorityWeight  = 10;  // 렐릭 선점 경쟁
    public int elitePriorityWeight = 8;   // 하수인 확보

    [Header("노드 비율 (합산 1.0 권장)")]
    [Range(0f, 1f)] public float monsterRatio  = 0.30f;
    [Range(0f, 1f)] public float eliteRatio    = 0.10f;
    [Range(0f, 1f)] public float eventRatio    = 0.20f;
    [Range(0f, 1f)] public float shopRatio     = 0.10f;
    [Range(0f, 1f)] public float restRatio     = 0.10f;
    [Range(0f, 1f)] public float treasureRatio = 0.10f;
    [Range(0f, 1f)] public float curseRatio    = 0.05f;
    [Range(0f, 1f)] public float salaryRatio   = 0.03f;
    // 나머지 = Empty (자동 계산)

    [Header("보스 시작 위치")]
    public int bossStartNode = 10;  // 보통 보드 중간쯤에서 시작

    [Header("연속 동일 타입 제한")]
    public int maxConsecutiveSameType = 2;  // 이 수 이상 연속 배치 금지
}
