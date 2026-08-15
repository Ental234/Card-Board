using System;
using System.Collections.Generic;
using UnityEngine;

// STS식 메타 진행 시스템
// 런 종료 시 메타 포인트 적립 → 카드 풀·캐릭터·시작 렐릭 해금
// PlayerPrefs로 영구 저장
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

    // ── 해금 목록 조회 ───────────────────────────────────

    // 현재 해금된 카드 목록 (카드 보상 풀 필터링에 사용)
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
}
