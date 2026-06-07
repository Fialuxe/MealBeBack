using System.Collections;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Fog")]
    [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
    [SerializeField] private Color   fogColor   = new Color(0.4f, 0.55f, 0.65f, 1f);
    [SerializeField] private float   fogDensity = 0.02f;

    [Header("Terrain")]
    [Tooltip("Assign the Terrain GameObject's Terrain component here")]
    [SerializeField] private Terrain terrain;

    private void Awake()
    {
        RenderSettings.fog      = true;
        RenderSettings.fogMode  = fogMode;
        RenderSettings.fogColor   = fogColor;
        RenderSettings.fogDensity = fogDensity;
    }

    // ── Fog ──────────────────────────────────────────────────────────────────

    /// <summary>Sets fog color immediately or with a smooth transition.</summary>
    public void SetFogColor(Color color, float duration = 0f)
    {
        fogColor = color;
        StopIfRunning(ref _fogColorCo);
        _fogColorCo = duration > 0f
            ? StartCoroutine(LerpFogColor(color, duration))
            : null;
        if (duration <= 0f) RenderSettings.fogColor = color;
    }

    /// <summary>Sets fog density (0 = no fog, higher = thicker).</summary>
    public void SetFogDensity(float density, float duration = 0f)
    {
        fogDensity = density;
        StopIfRunning(ref _fogDensityCo);
        _fogDensityCo = duration > 0f
            ? StartCoroutine(LerpFogDensity(density, duration))
            : null;
        if (duration <= 0f) RenderSettings.fogDensity = density;
    }

    // ── Terrain ───────────────────────────────────────────────────────────────

    /// <summary>Sets the terrain material's main color immediately or with a smooth transition.</summary>
    public void SetTerrainColor(Color color, float duration = 0f)
    {
        if (!ValidateTerrain()) return;
        StopIfRunning(ref _terrainColorCo);
        _terrainColorCo = duration > 0f
            ? StartCoroutine(LerpTerrainColor(color, duration))
            : null;
        if (duration <= 0f) ApplyTerrainColor(color);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");

    private Coroutine _fogColorCo;
    private Coroutine _fogDensityCo;
    private Coroutine _terrainColorCo;

    private void StopIfRunning(ref Coroutine co)
    {
        if (co != null) { StopCoroutine(co); co = null; }
    }

    private bool ValidateTerrain()
    {
        if (terrain != null) return true;
        terrain = Terrain.activeTerrain;       // fallback: find the active terrain
        return terrain != null;
    }

    private void ApplyTerrainColor(Color color)
    {
        Material mat = terrain.materialTemplate;
        if (mat == null) return;
        if (mat.HasProperty(BaseColorID)) mat.SetColor(BaseColorID, color);
        if (mat.HasProperty(ColorID))     mat.SetColor(ColorID,     color);
    }

    private IEnumerator LerpFogColor(Color target, float duration)
    {
        Color from = RenderSettings.fogColor;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            RenderSettings.fogColor = Color.Lerp(from, target, t);
            yield return null;
        }
        RenderSettings.fogColor = target;
        _fogColorCo = null;
    }

    private IEnumerator LerpFogDensity(float target, float duration)
    {
        float from = RenderSettings.fogDensity;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            RenderSettings.fogDensity = Mathf.Lerp(from, target, t);
            yield return null;
        }
        RenderSettings.fogDensity = target;
        _fogDensityCo = null;
    }

    private IEnumerator LerpTerrainColor(Color target, float duration)
    {
        if (!ValidateTerrain()) yield break;
        Material mat  = terrain.materialTemplate;
        if (mat == null) yield break;

        Color fromBase = mat.HasProperty(BaseColorID) ? mat.GetColor(BaseColorID) : Color.white;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            ApplyTerrainColor(Color.Lerp(fromBase, target, t));
            yield return null;
        }
        ApplyTerrainColor(target);
        _terrainColorCo = null;
    }
}
