using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns fish prefabs from Resources/ and configures each swim script so every
/// fish moves naturally inside a shared aquarium volume.
///
/// Usage: attach to any empty GameObject in the scene (e.g. "AquariumManager").
/// All fish become children of that object.
/// </summary>
public class AquariumSceneSetup : MonoBehaviour
{
    [System.Serializable]
    public class FishSchool
    {
        [Tooltip("Path inside Resources/ (no extension, no leading slash).")]
        public string prefabPath;
        [Min(0)] public int count = 4;
        [Tooltip("Multiplied onto each fish's base swim speed. Add slight variation per fish automatically.")]
        [Range(0.3f, 3f)] public float speedMultiplier = 1f;
        [Tooltip("Vertical placement: 0 = near bottom, 1 = near surface. Different species at different depths look natural.")]
        [Range(0f, 1f)] public float depthBias = 0.5f;
    }

    // ---------------------------------------------------------------
    [Header("Species")]
    public FishSchool bluefin = new FishSchool
    {
        prefabPath = "prefabs/bluefin_rigged_xplus",
        count = 3, speedMultiplier = 1.1f, depthBias = 0.50f
    };
    public FishSchool seabream = new FishSchool
    {
        prefabPath = "prefabs/seabream_rigged_xplus",
        count = 6, speedMultiplier = 0.85f, depthBias = 0.65f
    };
    public FishSchool tuna = new FishSchool
    {
        prefabPath = "prefabs/tuna_rigged_xplus",
        count = 2, speedMultiplier = 1.25f, depthBias = 0.42f
    };

    // ---------------------------------------------------------------
    [Header("Aquarium Volume")]
    [Tooltip("World-space center of the swim volume.")]
    public Vector3 aquariumCenter = Vector3.zero;
    [Tooltip("Full extent of the swim volume (X width, Y height, Z depth).")]
    public Vector3 aquariumSize = new Vector3(28f, 8f, 28f);
    [Tooltip("Fish begin steering away from the boundary this many units before reaching it.")]
    [Min(0.5f)] public float softMargin = 4f;

    // ---------------------------------------------------------------
    [Header("Spawn")]
    [Tooltip("Minimum world-space gap between any two fish at spawn time.")]
    [Min(0.5f)] public float minSeparation = 2.5f;

    // ---------------------------------------------------------------
    readonly List<Vector3> _placed = new List<Vector3>();

    void Start()
    {
        SpawnSchool(bluefin);
        SpawnSchool(seabream);
        SpawnSchool(tuna);
        _placed.Clear();
    }

    // ---------------------------------------------------------------
    void SpawnSchool(FishSchool school)
    {
        if (string.IsNullOrEmpty(school.prefabPath)) return;

        var prefab = Resources.Load<GameObject>(school.prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[AquariumSceneSetup] Resources/{school.prefabPath} not found.", this);
            return;
        }

        for (int i = 0; i < school.count; i++)
        {
            Vector3 pos = PickPosition(school.depthBias);
            _placed.Add(pos);

            float yaw = Random.Range(0f, 360f);
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), transform);
            go.name = $"{prefab.name}_{i:00}";

            Configure(go, school);
        }
    }

    void Configure(GameObject fish, FishSchool school)
    {
        // Per-fish speed jitter keeps a school from looking like a chorus line.
        float speedJitter = Random.Range(0.88f, 1.12f);
        Vector3 innerSize = aquariumSize - Vector3.one * (softMargin * 2f);
        float sphereRadius = Mathf.Min(aquariumSize.x, aquariumSize.z) * 0.5f - softMargin;

        // --- Bluefin ---
        var bf = fish.GetComponent<BluefinSwim>();
        if (bf != null)
        {
            bf.swimSpeed *= school.speedMultiplier * speedJitter;
            bf.maxTurnSpeed += Random.Range(-5f, 5f);
            bf.enableWander = true;

            // BluefinSwim has no built-in bounds; BoundsKeeper provides it.
            var keeper = fish.AddComponent<BoundsKeeper>();
            keeper.boundsCenter = aquariumCenter;
            keeper.boundsSize = aquariumSize;
            // softEdgeFraction = softMargin / half-width
            keeper.softEdgeFraction = Mathf.Clamp01(softMargin / (aquariumSize.x * 0.5f));
            return;
        }

        // --- Seabream ---
        var sb = fish.GetComponent<SeabreamSwim>();
        if (sb != null)
        {
            sb.swimSpeed *= school.speedMultiplier * speedJitter;
            // Slight beat-frequency variation desynchronises a school of seabream.
            sb.beatFrequency *= Random.Range(0.92f, 1.08f);
            sb.turnAmount += Random.Range(-4f, 4f);
            sb.useBounds = true;
            sb.boundsCenter = aquariumCenter;
            sb.boundsSize = innerSize;
            sb.boundsMargin = softMargin * 0.5f;
            return;
        }

        // --- Tuna ---
        var tn = fish.GetComponent<TunaSwim>();
        if (tn != null)
        {
            tn.cruiseSpeed *= school.speedMultiplier * speedJitter;
            tn.baseBeatFrequency *= Random.Range(0.90f, 1.10f);
            tn.wander = true;
            tn.useBounds = true;
            tn.boundsCenter = aquariumCenter;   // public field exposed for setup
            tn.boundsRadius = sphereRadius;
            return;
        }

        // --- Squid (fallback) ---
        var squid = fish.GetComponent<SquidSwimAnimator>();
        if (squid != null)
        {
            squid.speed *= school.speedMultiplier * speedJitter;
        }
    }

    // ---------------------------------------------------------------
    Vector3 PickPosition(float depthBias)
    {
        Vector3 half = aquariumSize * 0.5f;
        float yBottom = aquariumCenter.y - half.y * 0.85f;
        float yTop    = aquariumCenter.y + half.y * 0.85f;
        float yCenter = Mathf.Lerp(yBottom, yTop, depthBias);
        float yRange  = half.y * 0.20f;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            var candidate = new Vector3(
                aquariumCenter.x + Random.Range(-half.x * 0.80f, half.x * 0.80f),
                Mathf.Clamp(yCenter + Random.Range(-yRange, yRange), yBottom, yTop),
                aquariumCenter.z + Random.Range(-half.z * 0.80f, half.z * 0.80f)
            );

            bool tooClose = false;
            foreach (var p in _placed)
            {
                if (Vector3.Distance(candidate, p) < minSeparation)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose) return candidate;
        }

        // Fallback: accept any random position if separation couldn't be satisfied.
        return new Vector3(
            aquariumCenter.x + Random.Range(-half.x * 0.80f, half.x * 0.80f),
            yCenter,
            aquariumCenter.z + Random.Range(-half.z * 0.80f, half.z * 0.80f));
    }

    // ---------------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.55f, 1f, 0.08f);
        Gizmos.DrawCube(aquariumCenter, aquariumSize);
        Gizmos.color = new Color(0.1f, 0.55f, 1f, 0.45f);
        Gizmos.DrawWireCube(aquariumCenter, aquariumSize);

        // Inner "soft" boundary
        Gizmos.color = new Color(0.1f, 0.9f, 0.5f, 0.25f);
        Gizmos.DrawWireCube(aquariumCenter, aquariumSize - Vector3.one * softMargin * 2f);
    }
}
