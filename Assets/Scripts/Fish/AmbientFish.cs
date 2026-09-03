using UnityEngine;

/// <summary>
/// 環境魚 1 匹分の移動ロジック。
///
/// あえて MonoBehaviour ではなくプレーンな C# クラスにしている。
///   ・魚システム全体で MonoBehaviour.Update は FishSystem.Update ただ 1 つ。
///     そこから List を tight ループで回して各 AmbientFish.Tick を呼ぶ。
///   ・近傍参照は FishSystem の空間ハッシュ経由 (平均 O(1))。総当たりループは無い。
///   → 1 匹あたり O(1)、全体 O(N)。
///
/// 骨のうねりはプレハブの Animator (ループクリップ) が担当し、
/// ここでは Animator.speed を遊泳速度に比例させるだけ。
///
/// 種別の癖:
///   ・swayAmplitude / swayPeriod … 進路に乗る横うねり (鯛 = 大きく S 字、マグロ = ほぼ直進)。
///   ・turnSmoothTime / maxYawRate … 旋回の機敏さと慣性 (鯛 = 小回り、マグロ = 大回り)。
///   ・schooling …            同種で群れる (鯛のみ)。
///   ・bandInner/Outer, depthOffset, cruiseSpeed … 距離帯・深度・速度。
///
/// 前進軸: モデルのローカル -X が正面 (_xplus 系)。cfg.modelYawOffset で進行方向へ合わせる。
/// </summary>
public class AmbientFish
{
    private Transform _tf;
    private Animator _animator;
    private FishSystem.AmbientSpecies _cfg;
    private int _speciesIndex;

    private float _heading, _targetHeading, _headingVel;
    private float _pitch, _targetPitch, _pitchVel;
    private float _roll, _rollVel;
    private float _speed;
    private float _wanderSeed;

    private Vector3 _pos;    // フレームスナップショット (近傍参照用)
    private Vector3 _fwd;

    public Transform Tf => _tf;
    public bool Alive => _tf != null;
    public Vector3 Pos => _pos;
    public Vector3 Fwd => _fwd;
    public int SpeciesIndex => _speciesIndex;
    public FishSystem.AmbientSpecies Cfg => _cfg;

    public void Init(Transform tf, Animator animator, FishSystem.AmbientSpecies cfg, int speciesIndex, float phaseSeed)
    {
        _tf = tf;
        _animator = animator;
        _cfg = cfg;
        _speciesIndex = speciesIndex;

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

    /// <summary>FishSystem.Update から毎フレーム 1 回。O(1) (近傍は空間ハッシュ経由)。</summary>
    public void Tick(float dt, Vector3 anchorPos, FishSystem sys, int selfIndex)
    {
        if (_tf == null)
            return;

        Vector3 pos = _tf.position;
        float now = Time.time;

        // ── 速度: 巡航 × Perlin ゆらぎ (= アニメ速度の基準) → × 推進脈動 (蹴る → 滑空) ──
        float sN = Mathf.PerlinNoise(_wanderSeed * 0.5f, now * 0.2f) * 2f - 1f;
        float baseSpeed = _cfg.cruiseSpeed * (1f + sN * _cfg.speedVariation);
        _speed = baseSpeed;
        if (_cfg.thrustPulse > 0f)
        {
            float beat = now * (Mathf.PI * 2f / Mathf.Max(_cfg.beatPeriod, 0.05f)) * 2f + _wanderSeed;
            _speed *= Mathf.Max(0.1f, 1f + _cfg.thrustPulse * Mathf.Sin(beat));
        }

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

        // ── 3. 近隣 (回避 = 全種 / 群れ = 同種)。空間ハッシュを 1 回走査。 ──
        if (sys != null && (_cfg.schooling || _cfg.neighborAvoidDist > 0f))
        {
            sys.NeighborSteer(selfIndex, out Vector3 school, out Vector3 avoid);

            // 回避が最優先。
            float am = avoid.magnitude;
            if (am > 1e-3f)
            {
                float avoidYaw = Mathf.Atan2(avoid.x, avoid.z) * Mathf.Rad2Deg;
                _targetHeading = Mathf.LerpAngle(_targetHeading, avoidYaw, Mathf.Clamp01(am));
            }

            float sm = school.magnitude;
            if (sm > 1e-3f)
            {
                float schoolYaw = Mathf.Atan2(school.x, school.z) * Mathf.Rad2Deg;
                _targetHeading = Mathf.LerpAngle(_targetHeading, schoolYaw, Mathf.Clamp01(sm) * 0.7f);
            }
        }

        // ── 4. 深度キープ ──
        float targetY = anchorPos.y + _cfg.depthOffset;
        float yErr = targetY - pos.y;
        float depthPitch = Mathf.Clamp(-yErr * 10f, -_cfg.maxPitchAngle, _cfg.maxPitchAngle);
        _targetPitch = Mathf.Lerp(0f, depthPitch, _cfg.depthPull);

        // ── 4b. 前方の地形を回避 (水平操舵 + 機首上げ)。高さサンプルのみ、レイキャスト無し。 ──
        if (sys != null && sys.AvoidGroundEnabled)
        {
            float lookDist = sys.GroundLookAhead;
            float clr = sys.GroundAvoidClearance;
            float hRad = _heading * Mathf.Deg2Rad;
            Vector3 fwdFlat = new Vector3(Mathf.Sin(hRad), 0f, Mathf.Cos(hRad));
            Vector3 rightFlat = new Vector3(fwdFlat.z, 0f, -fwdFlat.x);

            float gAhead = sys.GroundHeightAt(pos + fwdFlat * lookDist);
            float margin = (gAhead + clr) - pos.y;   // > 0 なら地形が近い
            if (!float.IsNegativeInfinity(gAhead) && margin > 0f)
            {
                float gL = sys.GroundHeightAt(pos + (fwdFlat * 0.5f - rightFlat) * lookDist);
                float gR = sys.GroundHeightAt(pos + (fwdFlat * 0.5f + rightFlat) * lookDist);
                float urgency = Mathf.Clamp01(margin / Mathf.Max(clr, 0.5f));
                float turn = gL <= gR ? -1f : 1f;    // 低い側へ曲がる
                _targetHeading += turn * sys.GroundAvoidTurnRate * urgency * dt;
                _targetPitch = Mathf.Min(_targetPitch, -sys.GroundAvoidPitchUp * urgency);
            }
        }

        // ── 5. なめらか追従 (レート制限 = 旋回の慣性) ──
        float prevHeading = _heading;
        _heading = Mathf.SmoothDampAngle(_heading, _targetHeading, ref _headingVel,
                                         _cfg.turnSmoothTime, _cfg.maxYawRate, dt);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel,
                                       _cfg.turnSmoothTime, _cfg.maxPitchRate, dt);
        _pitch = Mathf.Clamp(_pitch, -_cfg.maxPitchAngle, _cfg.maxPitchAngle);

        // ── 6. 見た目バンク + 旋回中の減速 ──
        float yawRate = Mathf.DeltaAngle(prevHeading, _heading) / Mathf.Max(dt, 1e-4f);
        float turnRatio = Mathf.Clamp01(Mathf.Abs(yawRate) / Mathf.Max(_cfg.maxYawRate, 1e-3f));
        float targetRoll = Mathf.Clamp(-yawRate / Mathf.Max(_cfg.maxYawRate, 1e-3f), -1f, 1f) * _cfg.maxBankAngle;
        _roll = Mathf.SmoothDampAngle(_roll, targetRoll, ref _rollVel, 0.4f);

        if (_cfg.turnSpeedPenalty > 0f)
            _speed *= Mathf.Lerp(1f, 1f - _cfg.turnSpeedPenalty, turnRatio);

        // ── 7. 進路に乗る横うねり (見た目のみ。進行方向には積分しない) ──
        float sway = _cfg.swayAmplitude *
                     Mathf.Sin(now * (Mathf.PI * 2f / Mathf.Max(_cfg.swayPeriod, 0.1f)) + _wanderSeed);

        // ── 8. 位置・姿勢の適用 ──
        Vector3 travel = Quaternion.Euler(_pitch, _heading, 0f) * Vector3.forward;
        Vector3 look = Quaternion.Euler(_pitch, _heading + sway, 0f) * Vector3.forward;
        Vector3 newPos = pos + travel * (_speed * dt);
        // ロールは「進行方向 (+Z) まわり」に適用してから modelYawOffset で -X 前進軸へ合わせる。
        // Euler(0, yaw, roll) にまとめると roll が yaw 後の横軸まわり = ピッチとして効いてしまう。
        _tf.SetPositionAndRotation(
            newPos,
            Quaternion.LookRotation(look, Vector3.up)
                * Quaternion.AngleAxis(_roll, Vector3.forward)
                * Quaternion.Euler(0f, _cfg.modelYawOffset, 0f));

        _pos = newPos;
        _fwd = travel;

        // ── 9. アニメ再生速度 (脈動は含めない基準速度で。尾振りは動かない周期のまま) ──
        if (_animator != null)
        {
            _animator.speed = Mathf.Clamp(baseSpeed / Mathf.Max(_cfg.cruiseSpeed, 0.01f),
                                          _cfg.animSpeedMin, _cfg.animSpeedMax);
        }
    }

    /// <summary>
    /// 海底より下へ抜けていたら minY まで引き上げ、次フレーム以降は上向きに泳がせる。
    /// FishSystem.Update の O(N) ループから Tick 後に 1 回呼ばれる。1 匹 O(1)。
    /// </summary>
    public void EnforceFloor(float minY)
    {
        if (_tf == null)
            return;

        Vector3 p = _tf.position;
        if (p.y >= minY)
            return;

        p.y = minY;
        _tf.position = p;
        _pos = p;

        // ピッチ正 = 機首下げ (Unity)。海底沿いに登れるよう下向き成分を止める。
        _pitchVel = 0f;
        if (_pitch > 0f)
            _pitch = 0f;
        _targetPitch = Mathf.Min(_targetPitch, -8f);
    }

    private void ApplyPose(Vector3 pos)
    {
        Vector3 dir = Quaternion.Euler(_pitch, _heading, 0f) * Vector3.forward;
        _tf.SetPositionAndRotation(
            pos,
            Quaternion.LookRotation(dir, Vector3.up)
                * Quaternion.AngleAxis(_roll, Vector3.forward)
                * Quaternion.Euler(0f, _cfg.modelYawOffset, 0f));
        _pos = pos;
        _fwd = dir;
    }
}
