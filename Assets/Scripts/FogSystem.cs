using System.Collections;
using UnityEngine;

/// <summary>
/// 海の濁り（= Unity のフォグ）を一元管理するサブシステム。
///
/// ・<see cref="RenderSettings"/> の fog* に触れるのはこのクラスだけにする。
/// ・QuizManager などの上位からは「きれい↔濁り」を段階（<see cref="StepDirtier"/>）や
///   割合（<see cref="SetPollution"/>）で指示するだけでよい。
/// ・内部に汚染度 0〜1 を持ち、きれいな値と濁った値の間を補間する。
///   補間中に次の指示が来たら、進行中の補間は止めて新しい目標へ向かう。
///
/// シーンに 1 つだけ置く。
/// </summary>
public class FogSystem : MonoBehaviour
{
    /// <summary>フォグの片側の見た目（色と密度）。</summary>
    [System.Serializable]
    public struct FogState
    {
        public Color color;
        [Min(0f)] public float density;
    }

    [Header("フォグの両端")]
    [SerializeField]
    private FogMode fogMode = FogMode.ExponentialSquared;

    [Tooltip("汚染度 0。きれいな海。既定値はシーンの現行フォグ設定に合わせてある。")]
    [SerializeField]
    private FogState clean = new FogState
    {
        color = new Color(0.45310697f, 0.8531536f, 0.99371064f, 0.4117647f),
        density = 0.027f,
    };

    [Tooltip("汚染度 1。最も濁った海。見た目に合わせて調整する。")]
    [SerializeField]
    private FogState dirty = new FogState
    {
        color = new Color(0.30f, 0.34f, 0.22f, 0.5f),
        density = 0.12f,
    };

    [Header("段階")]
    [Tooltip("きれい → 最も濁るまでに必要な StepDirtier の回数。")]
    [SerializeField, Min(1)]
    private int stepCount = 5;

    [Tooltip("1 段階ぶんの遷移にかける時間 (s)。")]
    [SerializeField, Min(0f)]
    private float stepDuration = 1.5f;

    [Header("補間カーブ")]
    [Tooltip("汚染度 0〜1 を、実際の見た目の補間率へ変換するカーブ。既定は線形。")]
    [SerializeField]
    private AnimationCurve blend = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("起動時")]
    [Tooltip("Awake でフォグを有効化し、きれいな状態へ初期化する。")]
    [SerializeField]
    private bool applyOnAwake = true;

    [Header("デバッグ（Editor / Play 中の手動確認用）")]
    [SerializeField]
    private bool debugKeys = false;

    [SerializeField]
    private KeyCode dirtierKey = KeyCode.PageDown;

    [SerializeField]
    private KeyCode cleanerKey = KeyCode.PageUp;

    [SerializeField]
    private KeyCode resetKey = KeyCode.Home;

    private float _pollution01;
    private int _step;
    private Coroutine _blendCo;

    /// <summary>現在の汚染度。0 = きれい、1 = 最も濁った状態。</summary>
    public float Pollution01 => _pollution01;

    /// <summary>これ以上 StepDirtier しても濁らない（上限に達している）。</summary>
    public bool IsAtMax => _step >= stepCount;

    private void Awake()
    {
        if (!applyOnAwake)
            return;

        RenderSettings.fog = true;
        RenderSettings.fogMode = fogMode;

        _step = 0;
        _pollution01 = 0f;
        ApplyImmediate(0f);
    }

    private void Update()
    {
        if (!debugKeys)
            return;

        if (Input.GetKeyDown(dirtierKey))
            StepDirtier();

        if (Input.GetKeyDown(cleanerKey))
            StepCleaner();

        if (Input.GetKeyDown(resetKey))
            ResetToClean(stepDuration);
    }

    // ── QuizManager 向け API ─────────────────────────────────────────────────

    /// <summary>きれいな海に戻す。クイズ開始時に呼ぶ。</summary>
    public void ResetToClean(float duration = 0f)
    {
        _step = 0;
        BlendTo(0f, duration);
    }

    /// <summary>不正解 1 回ぶん、1 段階濁らせる。累積し、上限がある。</summary>
    public void StepDirtier()
    {
        StepDirtier(stepDuration);
    }

    /// <inheritdoc cref="StepDirtier()"/>
    public void StepDirtier(float duration)
    {
        _step = Mathf.Min(_step + 1, stepCount);
        BlendTo((float)_step / stepCount, duration);
    }

    /// <summary>正解などで 1 段階だけ回復させる。0 未満にはならない。</summary>
    public void StepCleaner()
    {
        StepCleaner(stepDuration);
    }

    /// <inheritdoc cref="StepCleaner()"/>
    public void StepCleaner(float duration)
    {
        _step = Mathf.Max(_step - 1, 0);
        BlendTo((float)_step / stepCount, duration);
    }

    /// <summary>
    /// 汚染度を任意の割合へ時間補間する。0 = きれい、1 = 最も濁る。
    /// 結果画面や演出用。段階カウンタも近い値へ同期する。
    /// </summary>
    public void SetPollution(float target01, float duration = 0f)
    {
        target01 = Mathf.Clamp01(target01);
        _step = Mathf.Clamp(Mathf.RoundToInt(target01 * stepCount), 0, stepCount);
        BlendTo(target01, duration);
    }

    // ── 内部 ────────────────────────────────────────────────────────────────

    private void BlendTo(float target, float duration)
    {
        if (_blendCo != null)
        {
            StopCoroutine(_blendCo);
            _blendCo = null;
        }

        // Play 中でない／非アクティブ／即時指定ならその場で反映する
        if (duration <= 0f || !Application.isPlaying || !isActiveAndEnabled)
        {
            _pollution01 = target;
            ApplyImmediate(target);
            return;
        }

        _blendCo = StartCoroutine(BlendRoutine(target, duration));
    }

    private IEnumerator BlendRoutine(float target, float duration)
    {
        float from = _pollution01;

        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            _pollution01 = Mathf.Lerp(from, target, t);
            ApplyImmediate(_pollution01);
            yield return null;
        }

        _pollution01 = target;
        ApplyImmediate(target);
        _blendCo = null;
    }

    private void ApplyImmediate(float pollution01)
    {
        float k = blend.Evaluate(pollution01);
        RenderSettings.fogColor = Color.Lerp(clean.color, dirty.color, k);
        RenderSettings.fogDensity = Mathf.Lerp(clean.density, dirty.density, k);
    }

#if UNITY_EDITOR
    [ContextMenu("Test: 1 段階濁らせる")]
    private void CtxDirtier() => StepDirtier(0f);

    [ContextMenu("Test: 1 段階回復")]
    private void CtxCleaner() => StepCleaner(0f);

    [ContextMenu("Test: きれいに戻す")]
    private void CtxReset() => ResetToClean(0f);

    [ContextMenu("Test: 最大まで濁らせる")]
    private void CtxMax() => SetPollution(1f, 0f);
#endif
}
