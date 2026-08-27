using UnityEngine;

/// <summary>
/// 環境魚 1 匹分の移動ロジック。
///
/// あえて MonoBehaviour ではなくプレーンな C# クラスにしている。
///   ・魚システム全体で MonoBehaviour.Update は FishSystem.Update ただ 1 つ。
///     そこから List を tight ループで回して各 AmbientFish.Tick を呼ぶ。
///   ・static レジストリ・近傍クエリ・FindObjectsOfType を一切使わない。
///   → 1 匹あたり O(1)、全体 O(N)。誰かが後から Update を生やせない構造。
///
/// 骨のうねりはプレハブの Animator (ループクリップ) が担当し、
/// ここでは Animator.speed を遊泳速度に比例させるだけ。
///
/// 前進軸: モデルのローカル -X が正面 (_xplus 系)。cfg.modelYawOffset で進行方向へ合わせる。
/// </summary>
public class AmbientFish
{
    private Transform _tf;
    private Animator _animator;
    private FishSystem.AmbientSpecies _cfg;

    private float _heading, _targetHeading, _headingVel;
    private float _pitch, _targetPitch, _pitchVel;
    private float _roll, _rollVel;
    private float _speed;
    private float _wanderSeed;

    public Transform Tf => _tf;

    public void Init(Transform tf, Animator animator, FishSystem.AmbientSpecies cfg, float phaseSeed)
    {
        _tf = tf;
        _animator = animator;
        _cfg = cfg;

        _heading = _targetHeading = tf.rotation.eulerAngles.y;
        _pitch = _targetPitch = 0f;
        _roll = 0f;
        _speed = cfg.cruiseSpeed;

        ResetState(phaseSeed);
        ApplyPose(tf.position);
    }

    /// <summary>リサイクル時に呼ぶ。平滑化速度をゼロにし、徘徊位相を振り直す。</summary>
    public void ResetState(float phaseSeed)
    {
        _wanderSeed = phaseSeed * 1.618f + Random.value * 10f;
        _headingVel = _pitchVel = _rollVel = 0f;

        if (_tf != null)
        {
            _heading = _targetHeading = _tf.rotation.eulerAngles.y;
            _pitch = _targetPitch = 0f;
            _roll = 0f;
        }
        _speed = _cfg != null ? _cfg.cruiseSpeed : 1f;
    }

    /// <summary>FishSystem.Update から毎フレーム 1 回。O(1)、近傍アクセス・ボーン計算なし。</summary>
    public void Tick(float dt, Vector3 anchorPos)
    {
        if (_tf == null)
            return;

        Vector3 pos = _tf.position;
        float now = Time.time;

        // ── 速度 (Perlin ゆらぎ) ──
        float sN = Mathf.PerlinNoise(_wanderSeed * 0.5f, now * 0.2f) * 2f - 1f;
        _speed = _cfg.cruiseSpeed * (1f + sN * _cfg.speedVariation);

        // ── 1. 徘徊 (Perlin: 連続関数なので急変しない) ──
        float n = Mathf.PerlinNoise(_wanderSeed + now * _cfg.wanderNoiseSpeed, 0.137f) * 2f - 1f;
        _targetHeading += n * _cfg.maxYawRate * _cfg.wanderYawAmplitude * dt;

        // ── 2. ユーザー周辺の遊泳帯 (水平のみのソフト操舵) ──
        Vector3 flat = anchorPos - pos;
        flat.y = 0f;
        float d = flat.magnitude;
        if (d > 0.001f)
        {
            float inwardYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            if (d > _cfg.bandOuter)
            {
                float w = Mathf.Clamp01((d - _cfg.bandOuter) / Mathf.Max(_cfg.bandOuter, 0.01f)) * _cfg.bandPull;
                _targetHeading = Mathf.LerpAngle(_targetHeading, inwardYaw, w);
            }
            else if (d < _cfg.bandInner)
            {
                float w = Mathf.Clamp01((_cfg.bandInner - d) / Mathf.Max(_cfg.bandInner, 0.01f)) * _cfg.bandPull;
                _targetHeading = Mathf.LerpAngle(_targetHeading, inwardYaw + 180f, w);
            }
        }

        // ── 3. 深度キープ ──
        float targetY = anchorPos.y + _cfg.depthOffset;
        float yErr = targetY - pos.y;
        float depthPitch = Mathf.Clamp(-yErr * 10f, -_cfg.maxPitchAngle, _cfg.maxPitchAngle);
        _targetPitch = Mathf.Lerp(0f, depthPitch, _cfg.depthPull);

        // ── 4. なめらか追従 (レート制限) ──
        float prevHeading = _heading;
        _heading = Mathf.SmoothDampAngle(_heading, _targetHeading, ref _headingVel,
                                         _cfg.turnSmoothTime, _cfg.maxYawRate, dt);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel,
                                       _cfg.turnSmoothTime, _cfg.maxPitchRate, dt);
        _pitch = Mathf.Clamp(_pitch, -_cfg.maxPitchAngle, _cfg.maxPitchAngle);

        // ── 5. 見た目バンク ──
        float yawRate = Mathf.DeltaAngle(prevHeading, _heading) / Mathf.Max(dt, 1e-4f);
        float targetRoll = Mathf.Clamp(-yawRate / Mathf.Max(_cfg.maxYawRate, 1e-3f), -1f, 1f) * _cfg.maxBankAngle;
        _roll = Mathf.SmoothDampAngle(_roll, targetRoll, ref _rollVel, 0.4f);

        // ── 6. 位置・姿勢の適用 ──
        Vector3 dir = Quaternion.Euler(_pitch, _heading, 0f) * Vector3.forward;
        _tf.SetPositionAndRotation(
            pos + dir * (_speed * dt),
            Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, _cfg.modelYawOffset, _roll));

        // ── 7. アニメ再生速度 ──
        if (_animator != null)
        {
            _animator.speed = Mathf.Clamp(_speed / Mathf.Max(_cfg.cruiseSpeed, 0.01f),
                                          _cfg.animSpeedMin, _cfg.animSpeedMax);
        }
    }

    private void ApplyPose(Vector3 pos)
    {
        Vector3 dir = Quaternion.Euler(_pitch, _heading, 0f) * Vector3.forward;
        _tf.SetPositionAndRotation(
            pos,
            Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, _cfg.modelYawOffset, _roll));
    }
}
