using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 호버 진입·이탈을 콜백으로 넘긴다.
// 툴팁(TooltipTrigger)과 달리 "무엇을 띄울지"는 받는 쪽이 정한다 — 보스 아이콘 → 보스 정보 패널 등.
// 클릭 인터페이스는 구현하지 않는다 (누름 대상이 돼 아래 오브젝트의 클릭을 가로채지 않게).
public class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action OnEnter;
    public event Action OnExit;

    public static HoverRelay Attach(GameObject go)
    {
        if (go == null) return null;
        var relay = go.GetComponent<HoverRelay>();
        return relay != null ? relay : go.AddComponent<HoverRelay>();
    }

    public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
    public void OnPointerExit(PointerEventData eventData)  => OnExit?.Invoke();

    private void OnDisable() => OnExit?.Invoke();
}
