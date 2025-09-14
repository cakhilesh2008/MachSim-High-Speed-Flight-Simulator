using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Deformation : MonoBehaviour
{
    [Header("Cage Mesh (low-poly)")]
    public MeshFilter cageMeshFilter;   // Assign the Cage child here

    [Header("Deformation (Plastic)")]
    public float impactRadius = 0.5f;     // meters — influence radius
    public float plasticYield = 100f;     // impulse threshold (N·s)
    public float plasticScale = 0.0015f;  // translation per (impulse - yield)
    public float maxVertDisp = 0.20f;     // clamp per-vertex displacement from rest (m)

    [Header("Spring Solver")]
    public float springK = 500f;
    public float damping = 20f;
    public int springIterations = 2;

    [Header("Stabilization")]
    public float pinStrength = 15f;       // pull toward rest each frame

    [Header("Region Colliders")]
    public bool autoCollectRegions = true;
    public List<Collider> regionColliders = new List<Collider>();

    [Header("Bending (adds to plastic)")]
    public bool enableBend = true;
    public float bendAmount = 0.05f;      // extra translation away/toward normal
    public float maxBendAngle = 8f;       // hinge twist (degrees)
    public Vector3 mirrorAxis = Vector3.right; // local axis to mirror/twist around

    // Working data
    Mesh workingMesh;
    Vector3[] restPos;
    Vector3[] curPos;
    Vector3[] vel;

    int[] edges;           // pairs of indices
    int[] vertRegion;      // per-vertex region index (-1 if none)

    Bounds[] regionBoundsExpanded;

    void Awake()
    {
        if (!cageMeshFilter || !cageMeshFilter.sharedMesh)
        {
            Debug.LogError("Assign a low-poly Cage MeshFilter to Deformation.");
            enabled = false; return;
        }

        // Clone mesh so we can deform it at runtime without touching the asset
        workingMesh = Instantiate(cageMeshFilter.sharedMesh);
        workingMesh.MarkDynamic();
        cageMeshFilter.sharedMesh = workingMesh;

        restPos = workingMesh.vertices;
        curPos = workingMesh.vertices;
        vel = new Vector3[curPos.Length];

        BuildSafeEdgesFromTriangles();

        if (autoCollectRegions && regionColliders.Count == 0)
            CollectRegionCollidersStrict(transform);

        if (regionColliders.Count == 0)
            Debug.LogWarning("No region colliders set. Assign Capsule/Boxes to 'regionColliders'.");

        AssignVerticesToRegions();
        CacheExpandedBounds();
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Edge spring-damper relaxation
        for (int iter = 0; iter < springIterations; iter++)
        {
            for (int i = 0; i < edges.Length; i += 2)
            {
                int a = edges[i], b = edges[i + 1];
                Vector3 pa = curPos[a];
                Vector3 pb = curPos[b];

                float rest = (restPos[a] - restPos[b]).magnitude;
                if (rest < 1e-6f) continue;

                Vector3 dir = pb - pa;
                float len = dir.magnitude;
                if (len < 1e-6f) continue;

                dir /= len;

                float stretch = len - rest;
                Vector3 relVel = vel[b] - vel[a];
                float dampAlong = Vector3.Dot(relVel, dir);

                Vector3 force = dir * (springK * stretch + damping * dampAlong);

                vel[a] += force * dt;
                vel[b] += -force * dt;
            }
        }

        // Pin-to-rest to kill slow drift (keep modest so plastic dents survive)
        if (pinStrength > 0f)
        {
            float pinDt = dt * pinStrength;
            for (int i = 0; i < curPos.Length; i++)
            {
                Vector3 toRest = (restPos[i] - curPos[i]);
                vel[i] += toRest * pinDt;
            }
        }

        // Integrate + clamp + NaN guards
        for (int i = 0; i < curPos.Length; i++)
        {
            if (!IsFinite(vel[i])) vel[i] = Vector3.zero;

            curPos[i] += vel[i] * dt;

            Vector3 disp = curPos[i] - restPos[i];
            float d = disp.magnitude;
            if (d > maxVertDisp)
            {
                curPos[i] = restPos[i] + disp * (maxVertDisp / Mathf.Max(d, 1e-6f));
                vel[i] *= 0.2f;
            }
        }

        workingMesh.vertices = curPos;
        workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();
    }

    void OnCollisionEnter(Collision col)
    {
        if (col.contactCount == 0) return;

        // Use Unity's resolved impulse (N·s)
        float J = col.impulse.magnitude;
        if (J < plasticYield) return;

        foreach (var c in col.contacts)
        {
            var hitCol = c.thisCollider;
            int regionIdx = regionColliders.IndexOf(hitCol);
            if (regionIdx < 0) regionIdx = FindRegionByTransform(hitCol.transform);
            if (regionIdx < 0) continue;

            ApplyPlasticBend(regionIdx, c.point, c.normal, J);
        }
    }

    // ---------------- Helpers ----------------

    void CollectRegionCollidersStrict(Transform root)
    {
        regionColliders.Clear();
        var cols = root.GetComponentsInChildren<Collider>(true);
        foreach (var col in cols)
        {
            if (col == null || col.isTrigger) continue;
            if (col is CapsuleCollider || col is BoxCollider)
                regionColliders.Add(col);
        }
    }

    void BuildSafeEdgesFromTriangles()
    {
        var mesh = cageMeshFilter.sharedMesh;
        var tris = mesh.triangles;
        var v = mesh.vertices;

        var set = new HashSet<(int, int)>();

        void TryAdd(int a, int b)
        {
            if (a == b) return;
            float restSqr = (v[a] - v[b]).sqrMagnitude;
            if (restSqr < 1e-10f) return; // skip degenerate
            if (a > b) (a, b) = (b, a);
            set.Add((a, b));
        }

        for (int i = 0; i < tris.Length; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            TryAdd(i0, i1);
            TryAdd(i1, i2);
            TryAdd(i2, i0);
        }

        edges = new int[set.Count * 2];
        int e = 0;
        foreach (var pair in set)
        {
            edges[e++] = pair.Item1;
            edges[e++] = pair.Item2;
        }
    }

    void AssignVerticesToRegions()
    {
        vertRegion = new int[curPos.Length];

        CacheExpandedBounds();

        for (int i = 0; i < curPos.Length; i++)
        {
            Vector3 wp = cageMeshFilter.transform.TransformPoint(curPos[i]);

            int bestIdx = -1;
            float bestDist = float.PositiveInfinity;

            // First pass: only consider colliders whose expanded bounds contain the vertex
            for (int r = 0; r < regionColliders.Count; r++)
            {
                var col = regionColliders[r];
                if (!col) continue;

                if (!regionBoundsExpanded[r].Contains(wp)) continue;

                float d = (wp - col.ClosestPoint(wp)).sqrMagnitude;

                // Prefer Capsule at tie
                if (Mathf.Approximately(d, bestDist) &&
                    col is CapsuleCollider &&
                    bestIdx >= 0 && !(regionColliders[bestIdx] is CapsuleCollider))
                {
                    bestIdx = r;
                    continue;
                }

                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = r;
                }
            }

            // Fallback: if none contained it, test all
            if (bestIdx < 0)
            {
                for (int r = 0; r < regionColliders.Count; r++)
                {
                    var col = regionColliders[r];
                    if (!col) continue;
                    float d = (wp - col.ClosestPoint(wp)).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = r;
                    }
                }
            }

            vertRegion[i] = bestIdx; // keep -1 if none
        }
    }

    void CacheExpandedBounds()
    {
        if (regionColliders == null || regionColliders.Count == 0)
        {
            regionBoundsExpanded = new Bounds[0];
            return;
        }

        regionBoundsExpanded = new Bounds[regionColliders.Count];
        for (int r = 0; r < regionColliders.Count; r++)
        {
            var col = regionColliders[r];
            if (col)
            {
                var b = col.bounds;
                b.Expand(impactRadius * 2f + 0.5f);
                regionBoundsExpanded[r] = b;
            }
            else
            {
                regionBoundsExpanded[r] = new Bounds();
            }
        }
    }

    int FindRegionByTransform(Transform t)
    {
        for (int r = 0; r < regionColliders.Count; r++)
        {
            var col = regionColliders[r];
            if (!col) continue;
            var rt = col.transform;
            if (t == rt || t.IsChildOf(rt) || rt.IsChildOf(t))
                return r;
        }
        return -1;
    }

    // Core: plastic translation + hinge-style bend (twist) with mirroring
    void ApplyPlasticBend(int regionIdx, Vector3 worldPoint, Vector3 worldNormal, float impulse)
    {
        if (regionIdx < 0 || regionIdx >= regionColliders.Count) return;

        float over = Mathf.Max(0f, impulse - plasticYield);
        if (over <= 0f) return;

        if (regionBoundsExpanded == null || regionBoundsExpanded.Length != regionColliders.Count)
            CacheExpandedBounds();

        Bounds wb = regionBoundsExpanded[regionIdx];
        Transform cageT = cageMeshFilter.transform;

        // Rotation-only for directions (avoid non-uniform scale)
        Vector3 nLocal = cageT.InverseTransformDirection(worldNormal).normalized;
        Vector3 axisLocal = mirrorAxis.sqrMagnitude < 1e-8f ? Vector3.right : mirrorAxis.normalized;

        for (int i = 0; i < curPos.Length; i++)
        {
            if (vertRegion[i] != regionIdx) continue;

            Vector3 wp = cageT.TransformPoint(curPos[i]);
            if (!wb.Contains(wp)) continue;

            float dist = Vector3.Distance(wp, worldPoint);
            if (dist > impactRadius) continue;

            // Smooth falloff
            float t = 1f - Mathf.SmoothStep(0f, 1f, dist / Mathf.Max(impactRadius, 1e-5f));

            // Determine side relative to the mirror axis (in local space)
            Vector3 localPos = curPos[i];
            float sideSign = Mathf.Sign(Vector3.Dot(localPos, axisLocal)); // +1 on one side, -1 on the other
            if (sideSign == 0f) sideSign = 1f; // treat exact axis as +1

            // ---------------------------
            // 1) Plastic translation (away/toward normal, scaled by impulse)
            // ---------------------------
            float plasticMag = plasticScale * over * t;
            Vector3 bendDirWorld = (sideSign > 0f) ? -worldNormal : worldNormal; // bend AWAY on hit side
            Vector3 deltaLocal = cageT.InverseTransformDirection(bendDirWorld) * plasticMag;

            // Apply translation plastically
            localPos += deltaLocal;

            // ---------------------------
            // 2) Hinge-style twist around mirror axis
            // ---------------------------
            if (enableBend && maxBendAngle != 0f)
            {
                float angle = maxBendAngle * t * ((sideSign > 0f) ? -1f : 1f);
                Quaternion q = Quaternion.AngleAxis(angle, axisLocal);

                // Decompose into axial (parallel to axis) and radial (perpendicular) components
                Vector3 axisDir = axisLocal;
                Vector3 axial = Vector3.Project(localPos, axisDir);
                Vector3 radial = localPos - axial;

                localPos = axial + q * radial;
            }

            // Bake plastic: update both current and rest positions
            curPos[i] = localPos;
            restPos[i] = localPos;

            // Dampen velocity on affected verts for stability
            vel[i] *= 0.25f;
        }
    }

    static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    static bool IsFinite(float f) => !(float.IsNaN(f) || float.IsInfinity(f));
}
