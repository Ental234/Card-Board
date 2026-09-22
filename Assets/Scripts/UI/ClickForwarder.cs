using UnityEngine;
using UnityEngine.EventSystems;

// 받은 클릭을 다른 오브젝트로 넘긴다.
//
// 툴팁 때문에 레이캐스트를 켠 장식(인텐트 배지·상태이상 칩)이 슬롯 가장자리와 겹치면
// 그 부분 클릭이 슬롯에 닿지 않는다. 장식은 슬롯의 자식이 아니라서 이벤트가 위로 번지지도 않는다.
// 이 컴포넌트가 클릭만 슬롯으로 되돌려 보낸다.
public class ClickForwarder : MonoBehaviour, IPointerClickHandler
{
    public GameObject target;

    public static void Attach(GameObject go, GameObject clickTarget)
    {
        if (go == null || clickTarget == null) return;
        var f = go.GetComponent<ClickForwarder>();
        if (f == null) f = go.AddComponent<ClickForwarder>();
        f.target = clickTarget;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (target != null)
            ExecuteEvents.ExecuteHierarchy(target, eventData, ExecuteEvents.pointerClickHandler);
    }
}
