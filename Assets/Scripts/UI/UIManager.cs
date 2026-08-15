using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 패널 전환 총괄 + 카드 상세 팝업 싱글턴
// GameManager.OnPhaseChanged를 구독해 패널 on/off
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("패널")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject boardPanel;
    [SerializeField] private GameObject combatPanel;
    [SerializeField] private GameObject eventPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private GameObject runEndPanel;
    [SerializeField] private GameObject rewardPanel;
    [SerializeField] private GameObject hudPanel;

    [Header("카드 상세 팝업")]
    [SerializeField] private GameObject      cardDetailPopup;
    [SerializeField] private TextMeshProUGUI popupCardName;
    [SerializeField] private TextMeshProUGUI popupDescription;
    [SerializeField] private TextMeshProUGUI popupCost;

    [Header("보드 맵 뷰")]
    [SerializeField] private BoardMapView boardMapView;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        GameManager.Instance.OnPhaseChanged  += HandlePhaseChanged;
        GameManager.Instance.OnStageChanged  += OnStageChanged;
        GameManager.Instance.OnRunEnded      += OnRunEnded;

        // 이벤트/상점 노드 처리
        GameManager.Instance.OnPlayerNodeEvent += HandlePlayerNodeEvent;

        // 전투 보상 화면
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardOffered += ShowReward;
            RewardManager.Instance.OnRewardClosed  += HideReward;
        }

        // 모두 끈 채로 시작한다. GameManager가 한 프레임 뒤 첫 페이즈를 알리면
        // HandlePhaseChanged가 알맞은 패널을 켠다.
        HideAll();
        hudPanel?.SetActive(false);
        cardDetailPopup?.SetActive(false);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnPhaseChanged    -= HandlePhaseChanged;
        GameManager.Instance.OnStageChanged    -= OnStageChanged;
        GameManager.Instance.OnRunEnded        -= OnRunEnded;
        GameManager.Instance.OnPlayerNodeEvent -= HandlePlayerNodeEvent;

        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardOffered -= ShowReward;
            RewardManager.Instance.OnRewardClosed  -= HideReward;
        }
    }

    // ── 전투 보상 화면 ───────────────────────────────────

    // 보드·전투 패널 위에 덮어씌운다 (페이즈 전환이 아니라 오버레이)
    private void ShowReward(CombatReward reward)
    {
        if (rewardPanel == null) return;
        rewardPanel.SetActive(true);
        rewardPanel.GetComponent<RewardUI>()?.Render(reward);
    }

    private void HideReward() => rewardPanel?.SetActive(false);

    // ── 페이즈 전환 ─────────────────────────────────────

    private void HandlePhaseChanged(GamePhase phase)
    {
        HideAll();

        // 메뉴·결과 화면에서는 HUD를 숨긴다 (런 중에만 의미 있는 정보라서)
        bool inRun = phase == GamePhase.BoardPhase || phase == GamePhase.CombatPhase;
        hudPanel?.SetActive(inRun);

        switch (phase)
        {
            case GamePhase.MainMenu:
                mainMenuPanel?.SetActive(true);
                break;

            case GamePhase.BoardPhase:
                boardPanel?.SetActive(true);
                boardMapView?.BuildMap();   // 그래프를 읽어 노드·간선 재생성
                break;

            case GamePhase.CombatPhase:
                combatPanel?.SetActive(true);
                break;

            case GamePhase.RunEnd:
                runEndPanel?.SetActive(true);
                break;
        }
    }

    // ── 노드 이벤트 라우팅 ───────────────────────────────

    private void HandlePlayerNodeEvent(int nodeIndex, NodeType type)
    {
        switch (type)
        {
            case NodeType.Event:
                ShowEvent();
                break;
            case NodeType.Shop:
                ShowShop();
                break;
        }
    }

    // ── 패널 show/hide ────────────────────────────────────

    public void ShowEvent()
    {
        eventPanel?.SetActive(true);
        EventManager.Instance?.TriggerRandomEvent();

        // 이벤트 완료 시 패널 닫기
        EventManager.Instance.OnEventCompleted += CloseEvent;
    }

    private void CloseEvent()
    {
        eventPanel?.SetActive(false);
        EventManager.Instance.OnEventCompleted -= CloseEvent;
        GameManager.Instance?.NotifyNodeResolved();   // 멈춰 있던 보드 턴 재개
    }

    public void ShowShop()
    {
        var gm = GameManager.Instance;

        // 패널을 먼저 켜야 ShopUI.OnEnable이 OnShopRefreshed를 구독한다.
        // 순서가 반대면 갱신 이벤트를 놓쳐 상품 목록이 비어 보인다.
        shopPanel?.SetActive(true);
        ShopManager.Instance?.OpenShop(gm.Player.ClassTag);
    }

    public void HideShop()
    {
        shopPanel?.SetActive(false);
        GameManager.Instance?.NotifyNodeResolved();   // 멈춰 있던 보드 턴 재개
    }

    // ── 카드 상세 팝업 ───────────────────────────────────

    public void ShowCardDetail(CardData data, Vector3 worldPos)
    {
        if (cardDetailPopup == null) return;

        cardDetailPopup.SetActive(true);
        cardDetailPopup.transform.position = worldPos + new Vector3(0, 120f, 0);

        if (popupCardName    != null) popupCardName.text    = data.cardName;
        if (popupDescription != null) popupDescription.text = data.description;
        if (popupCost        != null) popupCost.text        = $"Cost: {data.energyCost}";
    }

    public void HideCardDetail()
    {
        cardDetailPopup?.SetActive(false);
    }

    // ── 내부 유틸 ────────────────────────────────────────

    private void HideAll()
    {
        mainMenuPanel?.SetActive(false);
        rewardPanel?  .SetActive(false);
        boardPanel? .SetActive(false);
        combatPanel?.SetActive(false);
        eventPanel? .SetActive(false);
        shopPanel?  .SetActive(false);
        runEndPanel?.SetActive(false);
        hudPanel?   .SetActive(false);
    }

    private void OnStageChanged(int stage) { }  // HUD가 직접 구독
    private void OnRunEnded(bool won)      { }  // RunEndPanel이 처리
}
