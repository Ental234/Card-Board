using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 이 오브젝트에 마우스를 올리면 툴팁을 띄운다.
//
// 내용은 고정 문자열이나 함수로 준다. 함수로 주면 **띄우는 순간** 읽으므로
// HP·남은 턴처럼 바뀌는 값을 따로 갱신할 필요가 없다.
//
//   TooltipTrigger.Attach(icon, relic.relicName, relic.description);
//   TooltipTrigger.Attach(chip, () => (StatusLabel(s), StatusBody(s)));
//
// ※ 클릭 핸들러를 달지 않는다. 포인터 다운·클릭 인터페이스를 구현하면 이 오브젝트가
//   누름 대상이 돼 부모(슬롯 등)의 클릭을 가로챈다. 호버만 받는다.
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Func<(string title, string body)> provider;
    private float width;     // 0이면 기본 폭
    private bool  hovering;

    public static TooltipTrigger Attach(GameObject go, string title, string body)
        => Attach(go, () => (title, body));

    public static TooltipTrigger Attach(GameObject go, Func<(string title, string body)> contentProvider,
                                        float width = 0f)
    {
        if (go == null) return null;

        var trigger = go.GetComponent<TooltipTrigger>();
        if (trigger == null) trigger = go.AddComponent<TooltipTrigger>();
        trigger.provider = contentProvider;
        trigger.width    = width;

        // 호버를 받으려면 레이캐스트 대상 그래픽이 있어야 한다.
        // 칩·배지는 원래 raycastTarget을 꺼 두었는데, 슬롯 바깥에 있으니 켜도 클릭을 가로채지 않는다.
        // 루트에 그래픽이 없으면(보드 노드 프리팹 등) 자식 그래픽을 켠다 — 호버 이벤트는 부모로 번진다
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null) graphic = go.GetComponentInChildren<Graphic>();
        if (graphic != null) graphic.raycastTarget = true;

        return trigger;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (provider == null) return;

        var (title, body) = provider();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body)) return;

        hovering = true;
        TooltipView.Show(this, transform as RectTransform, title, body, width);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!hovering) return;
        hovering = false;
        TooltipView.Hide(this);
    }

    // 떠 있는 동안 대상이 꺼지거나 파괴되면(칩 재생성·패널 전환) 툴팁이 남지 않게 닫는다
    private void OnDisable()
    {
        if (!hovering) return;
        hovering = false;
        TooltipView.Hide(this);
    }
}
