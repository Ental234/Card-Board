using System;
using System.Collections.Generic;
using UnityEngine;

// STS식 메타 진행 시스템
// 런 종료 시 메타 포인트 적립 → 카드 풀·캐릭터·시작 렐릭 해금
// PlayerPrefs로 영구 저장
//
// 이 클래스는 "무엇이 잠겨 있는가"만 답한다. 잠긴 것을 풀에서 빼는 건 각 풀의 주인 몫이다
// (카드 = RewardManager·ShopManager, 캐릭터·시작 렐릭 = GameManager.AvailableCharacters / MainMenuUI)
public class MetaProgressionManager : MonoBehaviour
{
    public static MetaProgressionManager Instance { get; private set; }

    // ── 이벤트 ──────────────────────────────────────────

    public event Action<int>         OnMetaPointsChanged;  // (현재 누적 포인트)
    public event Action<UnlockData>  OnUnlockAchieved;     // 해금 발생 시 UI 알림

    // ── 해금 항목 정의 ───────────────────────────────────

    [SerializeField] private List<UnlockData> allUnlocks = new();

    // ── 저장 키 ─────────────────────────────────────────

    private const string KeyMetaPoints  = "MetaPoints";
    private const string KeyUnlockPrefix = "Unlock_";

    // ── 공개 상태 ────────────────────────────────────────

    public int TotalMetaPoints { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        TotalMetaPoints = PlayerPrefs.GetInt(KeyMetaPoints, 0);
    }

    // ── 런 종료 포인트 적립 ──────────────────────────────

    // GameManager가 런 종료 시 호출. 보상 화면에 띄울 내역을 돌려준다.
    public RunReward OnRunEnd(bool playerWon, int stageReached)
    {
        int earned = CalcEarnedPoints(playerWon, stageReached);
        AddMetaPoints(earned);

        return new RunReward
        {
            playerWon    = playerWon,
            stageReached = stageReached,
            earnedPoints = earned,
            totalPoints  = TotalMetaPoints,
            newUnlocks   = CheckUnlocks(),
        };
    }

    private int CalcEarnedPoints(bool playerWon, int stageReached)
    {
        // 기본: 도달한 스테이지 수 × 10, 런 승리 시 추가 30
        int points = stageReached * 10;
        if (playerWon) points += 30;
        return points;
    }

    private void AddMetaPoints(int amount)
    {
        TotalMetaPoints += amount;
        PlayerPrefs.SetInt(KeyMetaPoints, TotalMetaPoints);
        PlayerPrefs.Save();
        OnMetaPointsChanged?.Invoke(TotalMetaPoints);
    }

    // ── 해금 체크 ────────────────────────────────────────

    // 이번에 새로 해금된 항목 목록을 반환 (보상 화면 표시용)
    private List<UnlockData> CheckUnlocks()
    {
        var newly = new List<UnlockData>();

        foreach (var unlock in allUnlocks)
        {
            if (unlock == null || IsUnlocked(unlock)) continue;
            if (TotalMetaPoints >= unlock.requiredPoints)
            {
                SetUnlocked(unlock);
                newly.Add(unlock);
                OnUnlockAchieved?.Invoke(unlock);
            }
        }

        return newly;
    }

    public bool IsUnlocked(UnlockData unlock)
        => PlayerPrefs.GetInt(KeyUnlockPrefix + unlock.unlockId, 0) == 1;

    private void SetUnlocked(UnlockData unlock)
    {
        PlayerPrefs.SetInt(KeyUnlockPrefix + unlock.unlockId, 1);
        PlayerPrefs.Save();
    }

    // ── 잠금 판정 (3단계 7번, 2026-11-20) ────────────────
    //
    // 해금 카드는 **보상·상점 풀에 그대로 넣어 두고, 잠겨 있는 동안 걸러낸다.**
    // 반대로 "풀에는 안 넣고 해금되면 더한다"로 하면, 누가 실수로 그 카드를 풀에 넣는 순간
    // 해금 전부터 나온다 — 그리고 아무도 알아채지 못한다. 거르는 방식은 실수해도 안 나오는 쪽으로 틀린다.
    //
    // 해금 항목이 가리키지 않는 것은 잠기지 않는다 (= 기존 에셋은 전부 처음부터 열려 있다)
    public bool IsLocked(UnityEngine.Object content)
    {
        if (content == null) return false;
        foreach (var unlock in allUnlocks)
        {
            if (unlock == null || unlock.Content != content) continue;
            if (!IsUnlocked(unlock)) return true;
        }
        return false;
    }

    // 매니저가 없는 씬(테스트 등)에서는 아무것도 잠그지 않는다 — 풀이 통째로 비면 보상이 사라진다
    public static bool IsAvailable(UnityEngine.Object content)
        => Instance == null || !Instance.IsLocked(content);

    // ── 해금 목록 조회 ───────────────────────────────────

    // 현재 해금된 카드 목록 (해금 화면 등 표시용 — 풀 필터는 IsLocked를 쓴다)
    public List<CardData> GetUnlockedCards()
    {
        var result = new List<CardData>();
        foreach (var unlock in allUnlocks)
            if (unlock.type == UnlockType.Card && IsUnlocked(unlock) && unlock.card != null)
                result.Add(unlock.card);
        return result;
    }

    public List<CharacterData> GetUnlockedCharacters()
    {
        var result = new List<CharacterData>();
        foreach (var unlock in allUnlocks)
            if (unlock.type == UnlockType.Character && IsUnlocked(unlock) && unlock.character != null)
                result.Add(unlock.character);
        return result;
    }

    public List<RelicData> GetUnlockedStartingRelics()
    {
        var result = new List<RelicData>();
        foreach (var unlock in allUnlocks)
            if (unlock.type == UnlockType.StartingRelic && IsUnlocked(unlock) && unlock.relic != null)
                result.Add(unlock.relic);
        return result;
    }

    // ── 디버그 ──────────────────────────────────────────

    [ContextMenu("Reset Meta Progression")]
    public void ResetAll()
    {
        TotalMetaPoints = 0;
        PlayerPrefs.SetInt(KeyMetaPoints, 0);
        foreach (var unlock in allUnlocks)
            PlayerPrefs.DeleteKey(KeyUnlockPrefix + unlock.unlockId);
        PlayerPrefs.Save();
        OnMetaPointsChanged?.Invoke(0);
    }
}

// ── 런 보상 내역 ────────────────────────────────────────

// 런 종료 화면에 표시할 결과 묶음
public class RunReward
{
    public bool             playerWon;
    public int              stageReached;
    public int              earnedPoints;
    public int              totalPoints;
    public List<UnlockData> newUnlocks = new();
}

// ── 해금 항목 정의 ScriptableObject ─────────────────────

public enum UnlockType { Card, Character, StartingRelic }

[CreateAssetMenu(fileName = "NewUnlock", menuName = "Game/Unlock Data")]
public class UnlockData : ScriptableObject
{
    public string        unlockId;        // 고유 식별자 (PlayerPrefs 키)
    public string        displayName;
    [TextArea]
    public string        description;
    public int           requiredPoints;
    public UnlockType    type;

    // 해금 내용 (type에 따라 하나만 사용)
    public CardData      card;
    public CharacterData character;
    public RelicData     relic;

    // type에 맞는 내용물. 다른 칸에 남은 값은 무시한다 —
    // 타입을 Card에서 StartingRelic으로 바꾸고 card 칸을 안 비웠다고 그 카드가 잠기면 안 된다
    public UnityEngine.Object Content => type switch
    {
        UnlockType.Card          => card,
        UnlockType.Character     => character,
        UnlockType.StartingRelic => relic,
        _                        => null,
    };
}
