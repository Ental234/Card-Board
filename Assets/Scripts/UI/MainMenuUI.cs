using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 타이틀 화면 — 런 시작 대기 상태
// 누적 메타 포인트를 보여줘 이전 런의 성과가 이어진다는 걸 알린다.
public class MainMenuUI : MonoBehaviour
{
    [Header("텍스트")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI metaPointsText;

    [Header("버튼")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    private void OnEnable()
    {
        startButton?.onClick.AddListener(OnStartClicked);
        quitButton ?.onClick.AddListener(OnQuitClicked);

        Refresh();
    }

    private void OnDisable()
    {
        startButton?.onClick.RemoveListener(OnStartClicked);
        quitButton ?.onClick.RemoveListener(OnQuitClicked);
    }

    private void Refresh()
    {
        if (titleText != null) titleText.text = "나의 꿈 4트";

        if (metaPointsText == null) return;

        var meta = MetaProgressionManager.Instance;
        metaPointsText.text = meta != null
                            ? $"누적 메타 포인트 {meta.TotalMetaPoints}"
                            : "누적 메타 포인트 0";
    }

    // ── 버튼 ────────────────────────────────────────────

    private void OnStartClicked() => GameManager.Instance?.StartRun();

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
