using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 런 종료 보상 화면
// 승패 결과 + 이번 런 획득 포인트 + 누적 포인트 + 신규 해금 목록 → 메인 메뉴 복귀
public class RunEndUI : MonoBehaviour
{
    [Header("결과")]
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI earnedPointsText;   // 이번 런 획득 내역
    [SerializeField] private TextMeshProUGUI metaPointsText;     // 누적 포인트
    [SerializeField] private TextMeshProUGUI unlockText;         // 신규 해금 목록

    [Header("버튼")]
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;

    [Header("승패 색상")]
    [SerializeField] private Color winColor  = new(0.4f, 0.9f, 0.5f);
    [SerializeField] private Color loseColor = new(0.9f, 0.3f, 0.3f);

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnRunEnded += ShowResult;

        mainMenuButton?.onClick.AddListener(OnMainMenuClicked);
        quitButton    ?.onClick.AddListener(OnQuitClicked);

        // 패널이 이벤트보다 늦게 켜진 경우를 대비해 저장된 내역으로 한 번 그린다.
        Render(GameManager.Instance?.LastRunReward);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnRunEnded -= ShowResult;

        mainMenuButton?.onClick.RemoveListener(OnMainMenuClicked);
        quitButton    ?.onClick.RemoveListener(OnQuitClicked);
    }

    // ── 표시 ────────────────────────────────────────────

    private void ShowResult(bool playerWon) => Render(GameManager.Instance?.LastRunReward);

    private void Render(RunReward reward)
    {
        if (reward == null) return;

        if (resultText != null)
        {
            resultText.text  = reward.playerWon ? "승리!" : "패배";
            resultText.color = reward.playerWon ? winColor : loseColor;
        }

        if (earnedPointsText != null)
            earnedPointsText.text = $"스테이지 {reward.stageReached} 도달   +{reward.earnedPoints}";

        if (metaPointsText != null)
            metaPointsText.text = $"누적 메타 포인트 {reward.totalPoints}";

        if (unlockText != null)
            unlockText.text = BuildUnlockText(reward.newUnlocks);
    }

    private static string BuildUnlockText(List<UnlockData> unlocks)
    {
        if (unlocks == null || unlocks.Count == 0)
            return "새로 해금된 항목 없음";

        var sb = new StringBuilder("신규 해금\n");
        foreach (var u in unlocks)
        {
            if (u == null) continue;
            sb.Append("· ")
              .Append(string.IsNullOrEmpty(u.displayName) ? u.unlockId : u.displayName)
              .Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ── 버튼 ────────────────────────────────────────────

    private void OnMainMenuClicked() => GameManager.Instance?.ReturnToMainMenu();

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
