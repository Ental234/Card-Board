using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 보드 페이즈 편성창 — 전투 밖에서 진형을 짠다.
//
// 위쪽 4칸이 전투에 나가는 진형, 아래가 대기열이다.
// 동료를 얻으면 자동 배치되지 않고 대기열로 들어오므로, 누구를 세울지는 여기서 정한다.
//
// 조작은 클릭과 드래그 둘 다 된다. 판정은 ApplyMove 한 곳에서만 하므로
// 어느 쪽으로 조작하든 결과가 같다.
//
// 위젯은 런타임에 만든다 — 이동 화살표·의도 배지와 같은 방식이라
// 프리팹 오버라이드가 되돌아가는 함정을 피한다.
public class FormationPanel : MonoBehaviour
{
    [SerializeField] private RectTransform  slotRoot;     // 진형 4칸
    [SerializeField] private RectTransform  rosterRoot;   // 대기열
    [SerializeField] private Button          closeButton;
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("위젯")]
    [SerializeField] private Vector2 slotSize   = new(190f, 200f);
    [SerializeField] private float   slotGap    = 28f;
    [SerializeField] private Vector2 rosterSize = new(150f, 92f);
    [SerializeField] private float   rosterGap  = 14f;
    [SerializeField] private int     fontSize   = 16;   // Neo둥근모는 16px 배수만 쓴다

    // 선택 상태는 위젯 참조가 아니라 '무엇을 골랐는가'로 들고 있는다.
    // 위젯은 재구성할 때마다 파괴되므로 참조로 두면 하이라이트가 매번 풀린다.
    private int          selectedSlot = -1;   // -1 = 고른 것 없음
    private CombatEntity selectedOccupant;

    private readonly List<GameObject> widgets = new();

    private void Awake()
    {
        // 리스너는 여기서 한 번만 건다. OnEnable에 걸면 창을 여닫을 때마다 쌓인다.
        closeButton?.onClick.AddListener(Close);
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFormationChanged += Rebuild;

        ClearSelection();
        Rebuild();
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFormationChanged -= Rebuild;

        ClearWidgets();
    }

    public void Close() => UIManager.Instance?.HideFormation();

    // ── 그리기 ──────────────────────────────────────────

    // 전투 화면과 같은 좌우 배치 — 왼쪽이 최후방(슬롯4), 오른쪽이 최전방(슬롯1).
    // 순서를 뒤집으면 편성창에서 짠 그림과 전투 화면이 좌우로 어긋나 헷갈린다.
    private static int SlotForIndex(int index) => 4 - index;

    private void Rebuild()
    {
        ClearWidgets();

        var gm = GameManager.Instance;
        if (gm == null || slotRoot == null) return;

        // ── 진형 4칸 ──
        float totalWidth = 4 * slotSize.x + 3 * slotGap;
        float startX     = -totalWidth * 0.5f + slotSize.x * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            int slot = SlotForIndex(i);
            BuildWidget(slotRoot, slot, gm.GetFormationOccupant(slot),
                        new Vector2(startX + i * (slotSize.x + slotGap), 0f),
                        slotSize, $"슬롯 {slot}  {PositionName(slot)}");
        }

        // ── 대기열 ──
        if (rosterRoot != null)
        {
            var reserve = gm.ReserveCompanions;

            if (reserve.Count == 0)
            {
                BuildLabel(rosterRoot.gameObject, "대기열이 비어 있습니다",
                           Vector2.zero, new Vector2(400f, 40f), new Color(0.45f, 0.45f, 0.45f));
            }
            else
            {
                float rosterWidth = reserve.Count * rosterSize.x + (reserve.Count - 1) * rosterGap;
                float rosterStart = -rosterWidth * 0.5f + rosterSize.x * 0.5f;

                for (int i = 0; i < reserve.Count; i++)
                {
                    BuildWidget(rosterRoot, GameManager.SlotReserve, reserve[i],
                                new Vector2(rosterStart + i * (rosterSize.x + rosterGap), 0f),
                                rosterSize, "대기");
                }
            }
        }

        RefreshHint();
    }

    private void BuildWidget(RectTransform parent, int slot, CombatEntity occupant,
                             Vector2 position, Vector2 size, string header)
    {
        var go = new GameObject(slot >= 1 ? $"FormationSlot_{slot}" : $"Reserve_{occupant?.EntityName}",
                                typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta        = size;

        bool isSelected = occupant != null
                       && selectedSlot == slot
                       && selectedOccupant == occupant;

        var img = go.AddComponent<Image>();
        img.color = isSelected
                  ? new Color(0.42f, 0.34f, 0.14f, 0.98f)   // 고른 칸은 밝게
                  : new Color(0.14f, 0.13f, 0.16f, 0.96f);

        var widget = go.AddComponent<FormationSlotWidget>();
        widget.Bind(this, slot, occupant);

        BuildLabel(go, header, new Vector2(0f, size.y * 0.5f - 18f),
                   new Vector2(size.x - 12f, 28f), new Color(0.75f, 0.72f, 0.62f));

        string body = occupant == null
                    ? "(빈 칸)"
                    : $"{occupant.EntityName}\nHP {occupant.Stats.CurrentHp}/{occupant.Stats.MaxHp}";

        BuildLabel(go, body, new Vector2(0f, -6f), new Vector2(size.x - 12f, size.y - 40f),
                   occupant == null ? new Color(0.45f, 0.45f, 0.45f) : Color.white);

        widgets.Add(go);
    }

    private void BuildLabel(GameObject parent, string text, Vector2 offset, Vector2 size, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta        = size;

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text             = text;
        label.color            = color;
        label.fontSize         = fontSize;
        label.alignment        = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.raycastTarget    = false;      // 칸 클릭·드래그를 가로채면 안 된다
        label.fontStyle        = FontStyles.Normal;   // 볼드·이탤릭 금지

        // 대기열이 비었을 때의 안내문은 위젯 목록에 넣어 같이 지운다
        if (parent == rosterRoot?.gameObject) widgets.Add(go);
    }

    // ── 드래그 고스트 ────────────────────────────────────

    // 커서를 따라다니는 반투명 표식. 레이캐스트를 받으면 드롭 대상을 가리므로 꺼둔다.
    public GameObject CreateDragGhost(CombatEntity occupant, Vector2 screenPosition)
    {
        var go = new GameObject("DragGhost", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.SetAsLastSibling();          // 항상 맨 위에 그린다

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = rosterSize;
        rt.position  = screenPosition;

        var img = go.AddComponent<Image>();
        img.color         = new Color(0.55f, 0.45f, 0.18f, 0.85f);
        img.raycastTarget = false;

        BuildLabel(go, occupant.EntityName, Vector2.zero,
                   new Vector2(rosterSize.x - 10f, rosterSize.y - 10f), Color.white);

        return go;
    }

    // ── 조작 ────────────────────────────────────────────

    public void HandleClick(FormationSlotWidget widget)
    {
        // 아직 아무것도 안 골랐다 → 이 칸을 고른다
        if (selectedSlot == -1)
        {
            if (widget.Occupant == null)
            {
                ShowHint("빈 칸입니다. 옮길 유닛을 먼저 고르세요.");
                return;
            }

            selectedSlot     = widget.Slot;
            selectedOccupant = widget.Occupant;
            Rebuild();
            return;
        }

        // 같은 칸을 다시 누르면 선택 해제
        if (selectedSlot == widget.Slot && selectedOccupant == widget.Occupant)
        {
            ClearSelection();
            Rebuild();
            return;
        }

        int          sourceSlot     = selectedSlot;
        CombatEntity sourceOccupant = selectedOccupant;
        ClearSelection();

        if (!ApplyMove(sourceSlot, sourceOccupant, widget.Slot))
        {
            ShowHint(FailureReason(sourceSlot, sourceOccupant, widget.Slot));
            Rebuild();
        }
    }

    public void HandleDrop(FormationSlotWidget source, PointerEventData eventData)
    {
        ClearSelection();

        int targetSlot = ResolveDropTarget(eventData);
        if (targetSlot == -1) { Rebuild(); return; }   // 허공에 놓음 — 아무 일도 없다

        if (!ApplyMove(source.Slot, source.Occupant, targetSlot))
        {
            ShowHint(FailureReason(source.Slot, source.Occupant, targetSlot));
            Rebuild();
        }
    }

    private void ClearSelection()
    {
        selectedSlot     = -1;
        selectedOccupant = null;
    }

    // 커서 아래에 무엇이 있는지 → 진형 슬롯 번호 / 대기열(0) / 없음(-1)
    private int ResolveDropTarget(PointerEventData eventData)
    {
        var hovered = eventData.pointerCurrentRaycast.gameObject;
        if (hovered != null)
        {
            var widget = hovered.GetComponentInParent<FormationSlotWidget>();
            if (widget != null) return widget.Slot;
        }

        // 대기열의 빈 공간에 놓는 경우 — 위젯이 없어도 영역 안이면 받아준다
        if (rosterRoot != null &&
            RectTransformUtility.RectangleContainsScreenPoint(rosterRoot, eventData.position, null))
            return GameManager.SlotReserve;

        return -1;
    }

    // 클릭과 드래그가 공유하는 단 하나의 판정
    private bool ApplyMove(int sourceSlot, CombatEntity sourceOccupant, int targetSlot)
    {
        var gm = GameManager.Instance;
        if (gm == null || sourceOccupant == null) return false;

        // 진형에서 출발
        if (sourceSlot >= 1)
        {
            if (targetSlot == GameManager.SlotReserve) return gm.TryWithdrawFromSlot(sourceSlot);
            return gm.TrySwapFormation(sourceSlot, targetSlot);
        }

        // 대기열에서 출발 — 대기열끼리 옮기는 건 의미가 없다
        if (targetSlot == GameManager.SlotReserve) return false;

        return gm.TryDeployCompanion(sourceOccupant as CompanionEntity, targetSlot);
    }

    private string FailureReason(int sourceSlot, CombatEntity sourceOccupant, int targetSlot)
    {
        var gm = GameManager.Instance;
        if (gm == null) return "자리를 바꿀 수 없습니다.";

        // 주인공이 빠지면 전투에 나갈 사람이 없어진다
        if (targetSlot == GameManager.SlotReserve && sourceOccupant == gm.Player)
            return "주인공은 대기열로 내릴 수 없습니다.";

        if (sourceSlot == GameManager.SlotReserve && gm.GetFormationOccupant(targetSlot) == gm.Player)
            return "주인공이 선 자리에는 넣을 수 없습니다. 주인공을 먼저 옮기세요.";

        return "자리를 바꿀 수 없습니다.";
    }

    private static string PositionName(int slot)
    {
        switch (slot)
        {
            case 1:  return "최전방";
            case 4:  return "최후방";
            default: return "중열";
        }
    }

    private void RefreshHint()
    {
        if (selectedOccupant != null)
        {
            ShowHint($"{selectedOccupant.EntityName} 선택됨 — 놓을 자리를 누르세요.");
            return;
        }

        ShowHint("끌어다 놓거나, 두 칸을 차례로 누르세요. 대기열로 내리면 전투에 나가지 않습니다.");
    }

    private void ShowHint(string text)
    {
        if (hintText != null) hintText.text = text;
    }

    private void ClearWidgets()
    {
        foreach (var go in widgets)
        {
            if (go == null) continue;

            // Destroy는 프레임 끝에 처리된다. 그냥 두면 새로 만든 위젯과 한 프레임 겹쳐
            // 옛 글자가 비쳐 보이므로, 먼저 꺼서 그리기부터 멈춘다.
            go.SetActive(false);
            Destroy(go);
        }
        widgets.Clear();
    }
}
