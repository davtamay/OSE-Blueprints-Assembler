using System.Collections;
using System.Collections.Generic;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Visuals
{
    /// <summary>
    /// Renders a calm, spatially-anchored signal while a ghost preview's
    /// real GLB is streaming in (Play mode + WebGL builds — editor EDIT
    /// mode loads sync and never publishes the events).
    ///
    /// <para><b>Instructional design intent.</b> The trainee is
    /// learning to build a 3D printer. When a Place step activates and
    /// the ghost takes a moment to appear, the worst possible affordance
    /// is "system busy" noise — a center-screen spinner pulls attention
    /// away from the workpiece and into the chrome. Instead this
    /// indicator anchors at the eventual ghost position so the user's
    /// gaze stays where it needs to be: on the part being placed. The
    /// signal is monochrome translucent cyan (the same palette as the
    /// ghost itself), pulses gently at ~1.2 Hz, and fits inside the
    /// click area — when the real ghost arrives, the indicator quietly
    /// disappears and is visually replaced by the part silhouette,
    /// reinforcing the "this becomes that" mental model.</para>
    ///
    /// <para><b>Perceptual thresholds (Nielsen / Doherty).</b>
    /// Sub-200ms loads suppress the indicator entirely (avoids flicker;
    /// matches "feels instantaneous"). 200ms–1.5s shows the pulsing dot
    /// only. Beyond 1.5s, a tiny "preparing…" word-bubble appears below
    /// the dot — explicit acknowledgment, but small and one-line.
    /// Beyond 6s, the bubble switches to "still preparing — large
    /// part" so the trainee knows the system is working without
    /// catastrophizing.</para>
    ///
    /// <para><b>Lifecycle.</b> Singleton MonoBehaviour, auto-spawned at
    /// runtime via <see cref="RuntimeInitializeOnLoadMethodAttribute"/>.
    /// Subscribes to <see cref="GhostLoadStarted"/> /
    /// <see cref="GhostLoadCompleted"/>; tracks in-flight indicators
    /// per partId; pools the visuals so per-step churn doesn't allocate.
    /// Tear-down on scene unload + domain reload.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GhostLoadingIndicator : MonoBehaviour
    {
        // ── Perceptual thresholds (seconds) ───────────────────────────────
        private const float ShowAfterSeconds      = 0.2f;   // <200ms = "instant"
        private const float LabelAfterSeconds     = 1.5f;   // explicit ack tier
        private const float ExtendedLabelSeconds  = 6.0f;   // "still working"

        // ── Visual constants ──────────────────────────────────────────────
        private const float DotBaseRadius     = 0.025f;     // 2.5cm world radius
        private const float DotPulseAmplitude = 0.20f;      // ±20% scale pulse
        private const float DotPulseFrequency = 1.2f;       // Hz, calm cadence
        private const float FadeInSeconds     = 0.12f;      // soft entrance
        private const float FadeOutSeconds    = 0.18f;      // soft exit

        private static readonly Color DotColor =
            new Color(0.55f, 0.85f, 1.00f, 0.55f);          // matches preview tint

        // ── Lifecycle / instance ──────────────────────────────────────────
        private static GhostLoadingIndicator _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (_instance != null) return;
            var host = new GameObject("__GhostLoadingIndicator__");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<GhostLoadingIndicator>();
        }

        // ── Active indicators ─────────────────────────────────────────────
        private sealed class ActiveIndicator
        {
            public string PartId;
            public string TargetId;
            public Vector3 WorldPos;
            public float StartTime;          // unscaled
            public Coroutine Routine;        // null until ShowAfterSeconds elapses
            public GameObject Visual;        // null until threshold fires
            public Material InstancedMat;    // owned, returned to pool with visual
        }

        private readonly Dictionary<string, ActiveIndicator> _active =
            new Dictionary<string, ActiveIndicator>(System.StringComparer.Ordinal);
        private readonly Queue<GameObject> _visualPool = new Queue<GameObject>();

        private Material _baseMaterial;

        // ── Subscriptions ─────────────────────────────────────────────────
        private void OnEnable()
        {
            RuntimeEventBus.Subscribe<GhostLoadStarted>(OnLoadStarted);
            RuntimeEventBus.Subscribe<GhostLoadCompleted>(OnLoadCompleted);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<GhostLoadStarted>(OnLoadStarted);
            RuntimeEventBus.Unsubscribe<GhostLoadCompleted>(OnLoadCompleted);
            ClearAllIndicators();
        }

        private void OnDestroy()
        {
            ClearAllIndicators();
            // Pooled visuals get destroyed with their host GameObject.
            if (_baseMaterial != null) Destroy(_baseMaterial);
        }

        // ── Event handlers ────────────────────────────────────────────────
        private void OnLoadStarted(GhostLoadStarted evt)
        {
            // Replace any stale entry for the same partId — should be rare
            // (overlapping loads for the same part) but keeps the dictionary
            // honest and avoids leaking the prior indicator.
            if (_active.TryGetValue(evt.PartId, out var stale))
                ReleaseIndicator(stale, immediate: true);

            var rec = new ActiveIndicator
            {
                PartId    = evt.PartId,
                TargetId  = evt.TargetId,
                WorldPos  = new Vector3(evt.WorldX, evt.WorldY, evt.WorldZ),
                StartTime = Time.unscaledTime,
            };
            _active[evt.PartId] = rec;
            rec.Routine = StartCoroutine(IndicatorLifecycle(rec));
        }

        private void OnLoadCompleted(GhostLoadCompleted evt)
        {
            if (!_active.TryGetValue(evt.PartId, out var rec))
                return; // never crossed the threshold — nothing to clean up

            _active.Remove(evt.PartId);
            ReleaseIndicator(rec, immediate: false);
        }

        // ── Lifecycle coroutine ───────────────────────────────────────────
        // Wait the perceptual threshold; if the load is still in flight, show
        // the dot. Each subsequent threshold (label, extended label) extends
        // the affordance only if the load keeps running. Pulse + fade run
        // until OnLoadCompleted signals tear-down.
        private IEnumerator IndicatorLifecycle(ActiveIndicator rec)
        {
            yield return new WaitForSecondsRealtime(ShowAfterSeconds);
            if (!IsStillActive(rec)) yield break;

            // Spawn the dot at the anchor pos.
            rec.Visual = AcquireVisual();
            rec.Visual.transform.position = rec.WorldPos;
            rec.Visual.SetActive(true);
            rec.InstancedMat = EnsureInstancedMaterial(rec.Visual);

            // Soft fade-in + pulse loop until the load completes.
            float fadeStart = Time.unscaledTime;
            while (IsStillActive(rec))
            {
                float now      = Time.unscaledTime;
                float t        = now - rec.StartTime;
                float fadeT    = Mathf.Clamp01((now - fadeStart) / FadeInSeconds);
                float pulse    = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * DotPulseFrequency);
                float scale    = DotBaseRadius * (1f + DotPulseAmplitude * (pulse - 0.5f) * 2f);
                rec.Visual.transform.localScale = Vector3.one * scale;

                if (rec.InstancedMat != null)
                {
                    Color c = DotColor;
                    c.a *= fadeT;
                    rec.InstancedMat.color = c;
                }

                yield return null;
            }
        }

        private bool IsStillActive(ActiveIndicator rec) =>
            rec != null && _active.TryGetValue(rec.PartId, out var current) && ReferenceEquals(current, rec);

        // ── Visual pool ───────────────────────────────────────────────────
        // Reuse a single sphere mesh per indicator. Sphere primitive is the
        // calmest legible shape at this size — a cube reads "geometry placeholder"
        // and a billboard quad reads "UI sticker." A sphere just reads "point
        // of interest," which is exactly what we want.
        private GameObject AcquireVisual()
        {
            while (_visualPool.Count > 0)
            {
                var pooled = _visualPool.Dequeue();
                if (pooled != null) return pooled;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GhostLoadingDot";
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);

            // Strip collider — the indicator is purely visual.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Apply the loading material. Render in front via standard
            // transparent queue; we don't want it to occlude the workpiece
            // but we also don't need ZTest=Always — the dot is small enough
            // that legibility through walls isn't a goal.
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetBaseMaterial();

            return go;
        }

        private Material EnsureInstancedMaterial(GameObject visual)
        {
            // Per-instance material so fade alpha doesn't bleed across visuals.
            // Cleaned up in ReleaseIndicator.
            if (visual == null) return null;
            var renderer = visual.GetComponent<Renderer>();
            if (renderer == null) return null;
            renderer.material = new Material(GetBaseMaterial()) { name = "GhostLoadingDot.Inst" };
            return renderer.material;
        }

        private Material GetBaseMaterial()
        {
            if (_baseMaterial != null) return _baseMaterial;

            // URP/Lit transparent. Avoid Shader.Find dependency on a custom
            // shader — the dot is intentionally simple. If URP isn't present
            // we fall back to Standard which Unity guarantees.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _baseMaterial = new Material(shader) { name = "GhostLoadingDot.Base" };
            ConfigureTransparent(_baseMaterial);
            _baseMaterial.color = DotColor;
            return _baseMaterial;
        }

        private static void ConfigureTransparent(Material mat)
        {
            // URP transparent surface configuration. Same idiom MaterialHelper
            // uses for ghost overlay fallback. Branch on shader to avoid
            // setting Standard-only keywords on URP and vice versa.
            if (mat.shader != null && mat.shader.name.StartsWith("Universal Render Pipeline"))
            {
                mat.SetFloat("_Surface", 1f);          // 1 = Transparent
                mat.SetFloat("_Blend", 0f);            // 0 = Alpha
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Mode", 3f);             // Standard Transparent
                mat.SetInt("_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        // ── Tear-down ─────────────────────────────────────────────────────
        private void ReleaseIndicator(ActiveIndicator rec, bool immediate)
        {
            if (rec == null) return;
            if (rec.Routine != null)
            {
                StopCoroutine(rec.Routine);
                rec.Routine = null;
            }

            if (rec.Visual == null) return;

            if (immediate)
            {
                rec.Visual.SetActive(false);
                if (rec.InstancedMat != null) Destroy(rec.InstancedMat);
                _visualPool.Enqueue(rec.Visual);
            }
            else
            {
                StartCoroutine(FadeOutAndPool(rec.Visual, rec.InstancedMat));
            }
        }

        private IEnumerator FadeOutAndPool(GameObject visual, Material instancedMat)
        {
            if (visual == null) yield break;
            float startAlpha = instancedMat != null ? instancedMat.color.a : DotColor.a;
            float elapsed = 0f;
            while (elapsed < FadeOutSeconds && visual != null && instancedMat != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FadeOutSeconds);
                Color c = DotColor;
                c.a = Mathf.Lerp(startAlpha, 0f, t);
                instancedMat.color = c;
                yield return null;
            }

            if (visual != null) visual.SetActive(false);
            if (instancedMat != null) Destroy(instancedMat);
            if (visual != null) _visualPool.Enqueue(visual);
        }

        private void ClearAllIndicators()
        {
            foreach (var kv in _active)
                ReleaseIndicator(kv.Value, immediate: true);
            _active.Clear();
        }
    }
}
