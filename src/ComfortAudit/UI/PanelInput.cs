using UnityEngine;
using UnityEngine.EventSystems;

namespace ComfortAudit.UI
{
    /// <summary>
    /// Dragging, wheel scrolling and Ctrl+wheel scaling for the panel. Replaces Jotunn's
    /// DragWindowCntrl, which clamps transform.position as if it were the panel's centre: with
    /// our top-left pivot that kept the top edge at least half a panel below the top of the
    /// screen, so a tall panel could not be dragged above the middle.
    /// </summary>
    internal sealed class PanelInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        internal ComfortPanel Owner;

        private RectTransform _rect;
        private Vector2 _startPointer;
        private Vector2 _startPosition;
        private bool _dragging;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = TryPointer(eventData, out _startPointer);
            _startPosition = _rect.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || Owner == null || !TryPointer(eventData, out Vector2 pointer))
                return;

            // Both points are in the parent's space, the same units as anchoredPosition, so the
            // canvas scale and the panel's own scale need no correction.
            Owner.DragTo(_startPosition + (pointer - _startPointer));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }

        public void OnScroll(PointerEventData eventData)
        {
            // During play the cursor is locked to the centre of the screen, which a centred panel
            // covers. The wheel there belongs to the game (camera zoom, piece rotation).
            if (Owner == null || Cursor.lockState == CursorLockMode.Locked)
                return;

            float notches = eventData.scrollDelta.y;
            if (Mathf.Approximately(notches, 0f))
                return;

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                Owner.ScaleBy(notches);
            else
                Owner.ScrollBy(notches);
        }

        private bool TryPointer(PointerEventData eventData, out Vector2 local)
        {
            local = Vector2.zero;
            var parent = _rect.parent as RectTransform;
            return parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, eventData.position, eventData.pressEventCamera, out local);
        }
    }
}
