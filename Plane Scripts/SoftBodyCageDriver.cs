using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SoftBodyCageDriver : MonoBehaviour
{
    [Header("Follow")]
    [Tooltip("When ON, proxies follow cage bones every FixedUpdate.")]
    public bool followCageTransforms = true;

    [Range(1, 8)] public int fixedUpdateSkip = 1;

    [Header("Plasticity (connectedAnchor-based)")]
    [Tooltip("Force magnitude that triggers plastic offset accumulation.")]
    public float yieldForce = 150f;

    [Tooltip("How far to slide the rest toward the deformed position per triggering frame (0..1).")]
    [Range(0f, 1f)] public float plasticity = 0.5f;

    [Tooltip("Clamp the maximum dent depth (meters, world space). 0 = no clamp.")]
    public float maxPlasticOffset = 0.5f;

    [Header("Auto-Bake")]
    [Tooltip("Automatically commit dents to the cage when any node exceeds this world-space dent depth (0 = off).")]
    public float autoBakeThreshold = 0.0f; // e.g., 0.25f for obvious dents

    [Tooltip("Minimum seconds between auto-bakes (debounce).")]
    public float minSecondsBetweenAutoBakes = 1.0f;

    [Tooltip("Also bake on an interval (seconds). 0 = off.")]
    public float bakeEverySeconds = 0.0f;

    [Header("Gizmos")]
    public bool drawLines = false;
    public Color lineColor = new Color(0.25f, 0.75f, 1f, 0.8f);

    private readonly List<SoftBodyNodeLink> _links = new();
    private int _tick;
    private float _lastBakeTime;
    private float _lastIntervalTime;

    public bool triggerBakeNow;     // toggle true in Play mode to bake once
    public bool triggerClearNow;    // toggle true in Play mode to clear once
    public bool logBakeDetails = false;

    void Update()
    {
        if (triggerBakeNow)
        {
            triggerBakeNow = false;
            BakeConnectedAnchorsIntoCage(forceTeleportProxies: true);
        }
        if (triggerClearNow)
        {
            triggerClearNow = false;
            ClearConnectedAnchorOffsets(forceTeleportProxies: true);
        }
    }


    void Awake()
    {
        _links.Clear();
        _links.AddRange(GetComponentsInChildren<SoftBodyNodeLink>(true));
        if (_links.Count == 0) { Debug.LogError("[Driver] No SoftBodyNodeLink found."); enabled = false; return; }

        foreach (var l in _links)
        {
            if (l.dynamicBody == null || l.joint == null || l.cageBone == null || l.proxy == null)
            {
                Debug.LogError($"[Driver] Missing refs on {l.name}"); enabled = false; return;
            }

            // IMPORTANT: always capture base anchor (it’s fine if it’s (0,0,0))
            l.baseConnectedAnchor = l.joint.connectedAnchor;

            // don’t zero existing offsets here
        }
    }

    void FixedUpdate()
    {
        if ((++_tick % fixedUpdateSkip) != 0) return;

        // 1) Follow: move proxies to cage (rotation always; position if follow is true)
        foreach (var l in _links)
        {
            if (followCageTransforms)
                l.proxy.MovePosition(l.cageBone.position);
            l.proxy.MoveRotation(l.cageBone.rotation);
        }

        // 2) Plasticity: push connectedAnchor in proxy-local space so rest shifts permanently
        float maxObservedDent = 0f;

        foreach (var l in _links)
        {
            // World-space positions of the joint’s attachment points
            Vector3 A = l.joint.transform.TransformPoint(l.joint.anchor);               // dynamic side (world)
            Vector3 B = l.proxy.transform.TransformPoint(l.joint.connectedAnchor);      // proxy side   (world)

            // Measure stretch at the joint (this is what you “see”)
            Vector3 stretchW = A - B;
            float sep = stretchW.magnitude;

            // (Optional) force trigger (may be zero on some rigs)
#if UNITY_2020_2_OR_NEWER
      float forceMag = l.joint.currentForce.magnitude;
#else
            float forceMag = 0f;
#endif

            bool hitByForce = forceMag > yieldForce;
            bool hitBySeparation = sep > 0.005f; // small tolerance (meters)

            if (hitByForce || hitBySeparation)
            {
                // Move the proxy rest point toward A by a fraction
                // Compute a world shift for the connectedAnchor
                Vector3 moveW = stretchW * (hitByForce ? plasticity : Mathf.Clamp01(plasticity));

                // Convert to proxy-local and accumulate
                Vector3 moveLocal = l.proxy.transform.InverseTransformVector(moveW);
                Vector3 newLocal = l.connectedAnchorOffset + moveLocal;

                // Clamp by world magnitude if requested
                if (maxPlasticOffset > 0f)
                {
                    Vector3 worldVec = l.proxy.transform.TransformVector(newLocal);
                    if (worldVec.magnitude > maxPlasticOffset)
                        newLocal = l.proxy.transform.InverseTransformVector(worldVec.normalized * maxPlasticOffset);
                }

                l.connectedAnchorOffset = newLocal;
                l.joint.connectedAnchor = l.baseConnectedAnchor + l.connectedAnchorOffset;
            }

            // Track max dent depth (world)
            if (l.connectedAnchorOffset != Vector3.zero)
            {
                float dent = l.proxy.transform.TransformVector(l.connectedAnchorOffset).magnitude;
                if (dent > maxObservedDent) maxObservedDent = dent;
            }
        }

        // (debug) see it change frame-by-frame
        if (logBakeDetails)
            Debug.Log($"[Driver] jointStretch(max)~{maxObservedDent:F3} m, offsets now applied.");

        // 3) Auto-bake policies
        float now = Time.time;

        // threshold-based bake
        if (autoBakeThreshold > 0f && maxObservedDent >= autoBakeThreshold && (now - _lastBakeTime) >= minSecondsBetweenAutoBakes)
        {
            BakeConnectedAnchorsIntoCage();
            _lastBakeTime = now;
        }

        float maxDent = 0f;
        foreach (var l in _links)
        {
            float d = l.proxy.transform.TransformVector(l.connectedAnchorOffset).magnitude;
            if (d > maxDent) maxDent = d;
        }
        if (logBakeDetails) Debug.Log($"[Driver] maxDent={maxDent:F3} m");

        // interval bake
        if (bakeEverySeconds > 0f && (now - _lastIntervalTime) >= bakeEverySeconds)
        {
            BakeConnectedAnchorsIntoCage();
            _lastIntervalTime = now;
            _lastBakeTime = now;
        }
    }

    // ---------- BAKING ----------

    /// <summary>
    /// Move the cage bones by the world-equivalent of each node's connectedAnchor offset,
    /// then zero the offsets so the joint rest is clean again.
    /// </summary>
    [ContextMenu("Bake Dents Into Cage (commit offsets)")]
    public void BakeConnectedAnchorsIntoCage(bool forceTeleportProxies = false)
    {
        int moved = 0;
        foreach (var l in _links)
        {
            if (l.connectedAnchorOffset == Vector3.zero) continue;

            // Convert proxy-local offset -> world vector
            Vector3 worldShift = l.proxy.transform.TransformVector(l.connectedAnchorOffset);

            // Apply to cage bone **in local space** so parents/scale don’t fight us
            Transform p = l.cageBone.parent;
            Vector3 localShift = p ? p.InverseTransformVector(worldShift) : worldShift;

            Vector3 beforeLocal = l.cageBone.localPosition;
            l.cageBone.localPosition = beforeLocal + localShift;

            // Reset joint rest: zero the offset and restore base anchor
            l.connectedAnchorOffset = Vector3.zero;
            l.joint.connectedAnchor = l.baseConnectedAnchor;

            // Snap proxy immediately so you see the change this frame
            if (forceTeleportProxies || !followCageTransforms)
            {
                l.proxy.position = l.cageBone.position;
                l.proxy.rotation = l.cageBone.rotation;
                l.proxy.linearVelocity = Vector3.zero;
                l.proxy.angularVelocity = Vector3.zero;
            }

            moved++;
            if (logBakeDetails)
            {
                Debug.Log($"[Bake] {l.name}: Δlocal={localShift}  before={beforeLocal}  after={l.cageBone.localPosition}");
            }
        }

        if (logBakeDetails)
            Debug.Log(moved == 0 ? "[Bake] No dents to commit (all offsets zero)." : $"[Bake] Committed {moved} dents into cage.");
    }

    [ContextMenu("Clear Dents (keep cage)")]
    public void ClearConnectedAnchorOffsets(bool forceTeleportProxies = false)
    {
        int cleared = 0;
        foreach (var l in _links)
        {
            if (l.connectedAnchorOffset == Vector3.zero) continue;

            l.connectedAnchorOffset = Vector3.zero;
            l.joint.connectedAnchor = l.baseConnectedAnchor;
            cleared++;
        }

        if (forceTeleportProxies || !followCageTransforms)
        {
            foreach (var l in _links)
            {
                l.proxy.position = l.cageBone.position;
                l.proxy.rotation = l.cageBone.rotation;
                l.proxy.linearVelocity = Vector3.zero;
                l.proxy.angularVelocity = Vector3.zero;
            }
        }

        if (logBakeDetails)
            Debug.Log($"[Clear] Cleared {cleared} offsets; cage unchanged.");
    }


    // ---------- HELP ----------

    private static bool ValidateLink(SoftBodyNodeLink l)
    {
        if (l.dynamicBody == null) { Debug.LogError($"[SoftBodyCageDriver] Missing dynamicBody on {l.name}"); return false; }
        if (l.joint == null) { Debug.LogError($"[SoftBodyCageDriver] Missing joint on {l.name}"); return false; }
        if (l.cageBone == null) { Debug.LogError($"[SoftBodyCageDriver] Missing cageBone on {l.name}"); return false; }
        if (l.proxy == null) { Debug.LogError($"[SoftBodyCageDriver] Missing proxy on {l.name}"); return false; }
        if (!l.proxy.isKinematic) { Debug.LogWarning($"[SoftBodyCageDriver] Proxy on {l.cageBone.name} should be kinematic."); }
        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawLines) return;
        Gizmos.color = lineColor;

        var links = _links.Count > 0 ? _links : new List<SoftBodyNodeLink>(GetComponentsInChildren<SoftBodyNodeLink>(true));
        foreach (var l in links)
        {
            if (l == null || l.dynamicBody == null || l.proxy == null) continue;
            Gizmos.DrawLine(l.dynamicBody.position, l.proxy.position);
        }
    }
}
