using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEngine.XR.Interaction.Toolkit.Samples.WorldSpaceUI
{
    /// <summary>
    /// Adds click/touch-drag scrolling to a UI Toolkit ScrollView with the same
    /// elastic feel as UGUI's ScrollRect. Uses the ScrollView's own elasticity
    /// and touchScrollBehavior — no extra configuration needed.
    /// Attach to the same GameObject as the UIDocument.
    /// </summary>
    public class ScrollViewDragScroll : MonoBehaviour
    {
        [SerializeField, Tooltip("Drag speed multiplier to compensate for panel pixel vs world unit scale difference.")]
        float m_ScrollSensitivity = 30f;

        ScrollView m_ScrollView;
        bool m_Dragging;
        int m_PointerId;
        Vector3 m_LastPanelPos;
        Coroutine m_SpringCoroutine;

        void Start()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null) return;

            m_ScrollView = doc.rootVisualElement.Q<ScrollView>();
            if (m_ScrollView == null) return;

            var viewport = m_ScrollView.Q("unity-content-viewport");
            if (viewport == null) return;

            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_ScrollView.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_ScrollView.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (m_SpringCoroutine != null)
            {
                StopCoroutine(m_SpringCoroutine);
                m_SpringCoroutine = null;
            }

            m_Dragging = true;
            m_PointerId = evt.pointerId;
            m_LastPanelPos = evt.position;
            m_ScrollView.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!m_Dragging || !m_ScrollView.HasPointerCapture(evt.pointerId))
                return;

            Vector2 delta = (Vector2)evt.position - (Vector2)m_LastPanelPos;
            m_LastPanelPos = evt.position;

            Vector2 newOffset = m_ScrollView.scrollOffset + new Vector2(-delta.x, delta.y) * m_ScrollSensitivity;
            m_ScrollView.scrollOffset = ApplyScrollBehavior(newOffset);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!m_Dragging || evt.pointerId != m_PointerId) return;
            EndDrag();
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!m_Dragging || evt.pointerId != m_PointerId) return;
            EndDrag();
        }

        void EndDrag()
        {
            m_Dragging = false;
            m_ScrollView.ReleasePointer(m_PointerId);

            if (m_ScrollView.touchScrollBehavior == ScrollView.TouchScrollBehavior.Elastic)
                m_SpringCoroutine = StartCoroutine(SpringBack());
        }

        Vector2 ApplyScrollBehavior(Vector2 offset)
        {
            switch (m_ScrollView.touchScrollBehavior)
            {
                case ScrollView.TouchScrollBehavior.Elastic: return ElasticClamp(offset);
                case ScrollView.TouchScrollBehavior.Clamped: return HardClamp(offset);
                default:                                     return offset;
            }
        }

        // Same rubber-band formula as UGUI's ScrollRect: diminishing resistance
        // past the boundary, bounded to one viewport length of overscroll.
        Vector2 ElasticClamp(Vector2 offset)
        {
            Vector2 max      = GetMaxScrollOffset();
            Vector2 viewSize = m_ScrollView.contentViewport.layout.size;
            return new Vector2(
                RubberDelta(offset.x, 0f, max.x, viewSize.x),
                RubberDelta(offset.y, 0f, max.y, viewSize.y)
            );
        }

        static float RubberDelta(float value, float min, float max, float viewSize)
        {
            if (value >= min && value <= max)
                return value;

            float edge         = value < min ? min : max;
            float overstretch  = value - edge;
            float rubberOffset = (1f - 1f / (Mathf.Abs(overstretch) * 0.55f / viewSize + 1f)) * viewSize * Mathf.Sign(overstretch);
            return edge + rubberOffset;
        }

        Vector2 HardClamp(Vector2 offset)
        {
            Vector2 max = GetMaxScrollOffset();
            return new Vector2(Mathf.Clamp(offset.x, 0f, max.x), Mathf.Clamp(offset.y, 0f, max.y));
        }

        Vector2 GetMaxScrollOffset()
        {
            Vector2 content  = m_ScrollView.contentContainer.layout.size;
            Vector2 viewport = m_ScrollView.contentViewport.layout.size;
            return Vector2.Max(Vector2.zero, content - viewport);
        }

        // Spring speed derived from elasticity: lower elasticity = stiffer spring, same as UGUI.
        IEnumerator SpringBack()
        {
            float springSpeed = 1f / Mathf.Max(0.001f, m_ScrollView.elasticity);
            Vector2 target = HardClamp(m_ScrollView.scrollOffset);

            while (Vector2.Distance(m_ScrollView.scrollOffset, target) > 0.5f)
            {
                m_ScrollView.scrollOffset = Vector2.Lerp(
                    m_ScrollView.scrollOffset, target, Time.deltaTime * springSpeed);
                yield return null;
            }
            m_ScrollView.scrollOffset = target;
            m_SpringCoroutine = null;
        }
    }
}
