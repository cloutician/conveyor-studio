using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ConveyorControlBoxSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public ConveyorControlBox owner;
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner.Press(data.position, data.pointerId); }
    public void OnDrag(PointerEventData data) => owner.Drag(data.position, data.pointerId);
    public void OnPointerUp(PointerEventData data) => owner.Release(data.position, data.pointerId);
    public void OnScroll(PointerEventData data) => owner.ScrollDial(data.position, data.scrollDelta.y);
    public void OnPointerEnter(PointerEventData data) => owner.Hover(data.position);
    public void OnPointerMove(PointerEventData data) => owner.Hover(data.position);
    public void OnPointerExit(PointerEventData data) => owner.Leave();
    private void OnDisable() { if (owner != null) { owner.CancelInteraction(); owner.Leave(); } }
}

public enum ConveyorPanelControl { None, Speed, Sync, Start, Stop }
