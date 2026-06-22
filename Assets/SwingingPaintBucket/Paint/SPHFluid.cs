using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

// ══════════════════════════════════════════════════════════════════════════════
// SPHFluid — النسخة المُصحَّحة
//
// الإصلاحات:
//   ✅ كل الوحدات داخلية بالمتر (لا سنتيمتر)
//   ✅ ConvertPosToWorldM: موضع الدلو + إزاحة محلية (كلاهما بالمتر)
//   ✅ ConvertVelToWorldM: سرعة محلية بالمتر/ثانية مباشرة
//   ✅ exitingParticle.position و velocity = بالمتر (لـ EmitFromSPH)
//   ✅ إزالة × 100 الخاطئ في ComputeExitVecCM
//   ✅ particleCount = 50 (أداء مناسب لأي لابتوب)
//   ✅ subSteps ≤ 2
// ══════════════════════════════════════════════════════════════════════════════

public class SPHFluid
{
    // ── ثوابت ──
    private int _particleCount;
    private float _h;                       // smoothing length (m)
    private float _pressureStiffness = 200f;
    private float _surfaceTension = 0.072f;
    private float _restDensity = 1000f;  // kg/m³
    private const float GRAVITY = 9.80665f;    // m/s²

    // ── Spatial Hashing ──
    private float _cellSize;
    private int3 _gridDimensions;
    private NativeArray<int> _cellStart;
    private NativeArray<int> _cellEntries;
    private NativeArray<int> _sortedIndices;

    // ── Particle Data ──
    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<float3> _predictedPositions;
    private NativeArray<float> _densities;
    private NativeArray<float> _pressures;
    private NativeArray<float3> _forces;
    private NativeArray<bool> _active;
    private NativeArray<float> _temperatures;

    // ── Exiting Particles (بالمتر) ──
    private List<ExitingParticle> _exitingParticles = new List<ExitingParticle>();

    public struct ExitingParticle
    {
        public float3 position;   // متر — موضع عالمي
        public float3 velocity;   // m/s — سرعة عالمية
        public float density;
        public float pressure;
        public float temperature;
        public float radius;     // متر
        public Color color;
    }

    // ── Bucket Data ──
    private BucketData _bucketData;
    private PaintData _paintData;
    private EnvironmentData _envData;

    // ✅ موضع الدلو بالمتر (من SceneConnectorFinal)
    private float3 _bucketWorldPosM;
    private float3 _bucketVelMs;
    private float3 _bucketAngVel;
    private float3 _bucketAngAcc;

    private float _bucketRadiusM;
    private float _bucketHeightM;

    private float _simulationTime;
    private float _fillRatio = 1.0f;
    private int _activeCount;
    private bool _isInitialized = false;

    // Kernel factors
    private float _poly6Factor;
    private float _spikyFactor;
    private float _viscosityFactor;

    private int _frameCounter = 0;

    // ══════════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════════
    public SPHFluid(BucketData bucket, PaintData paint, EnvironmentData env,
                    int particleCount = 50)
    {
        _bucketData = bucket;
        _paintData = paint;
        _envData = env;
        _particleCount = Mathf.Clamp(particleCount, 10, 200);

        _bucketRadiusM = bucket.innerRadius;
        _bucketHeightM = bucket.totalHeight;

        float volume = Mathf.PI * _bucketRadiusM * _bucketRadiusM * _bucketHeightM;
        float volPerParticle = volume / _particleCount;
        float spacing = Mathf.Pow(volPerParticle, 1f / 3f);
        _h = Mathf.Max(spacing * 2.5f, 0.015f);
        _cellSize = _h;

        int gx = Mathf.CeilToInt(_bucketRadiusM * 2f / _cellSize) + 2;
        int gy = Mathf.CeilToInt(_bucketHeightM / _cellSize) + 2;
        int gz = gx;
        _gridDimensions = new int3(gx, gy, gz);

        float h2 = _h * _h, h3 = h2 * _h, h6 = h3 * h3, h9 = h6 * h3;
        _poly6Factor = 315f / (64f * Mathf.PI * h9);
        _spikyFactor = 45f / (Mathf.PI * h6);
        _viscosityFactor = 45f / (Mathf.PI * h6);

        InitializeArrays();
        InitializeParticles();
        _isInitialized = true;

        Debug.Log($"[SPH] Init: {_particleCount} particles | h={_h:F4}m | " +
                  $"grid=({gx},{gy},{gz}) | active={_activeCount}");
    }

    // ══════════════════════════════════════════════════════════════
    // تهيئة Arrays
    // ══════════════════════════════════════════════════════════════
    private void InitializeArrays()
    {
        _positions = new NativeArray<float3>(_particleCount, Allocator.Persistent);
        _velocities = new NativeArray<float3>(_particleCount, Allocator.Persistent);
        _predictedPositions = new NativeArray<float3>(_particleCount, Allocator.Persistent);
        _densities = new NativeArray<float>(_particleCount, Allocator.Persistent);
        _pressures = new NativeArray<float>(_particleCount, Allocator.Persistent);
        _forces = new NativeArray<float3>(_particleCount, Allocator.Persistent);
        _active = new NativeArray<bool>(_particleCount, Allocator.Persistent);
        _temperatures = new NativeArray<float>(_particleCount, Allocator.Persistent);

        int totalCells = _gridDimensions.x * _gridDimensions.y * _gridDimensions.z;
        _cellStart = new NativeArray<int>(_particleCount + totalCells + 1, Allocator.Persistent);
        _cellEntries = new NativeArray<int>(_particleCount, Allocator.Persistent);
        _sortedIndices = new NativeArray<int>(_particleCount, Allocator.Persistent);
    }

    // ══════════════════════════════════════════════════════════════
    // توزيع الجسيمات الأولي داخل الدلو
    // ══════════════════════════════════════════════════════════════
    private void InitializeParticles()
    {
        float fillH = Mathf.Clamp(
            _bucketHeightM * (_paintData.initialHeight / _bucketData.totalHeight),
            0.02f, _bucketHeightM * 0.9f);

        int ppl = Mathf.CeilToInt(Mathf.Sqrt(_particleCount * 0.7f));
        int layers = Mathf.CeilToInt((float)_particleCount / (ppl * ppl));

        int idx = 0;
        for (int layer = 0; layer < layers && idx < _particleCount; layer++)
        {
            float y = -_bucketHeightM * 0.5f + (layer + 0.5f) * (fillH / layers);
            for (int i = 0; i < ppl && idx < _particleCount; i++)
                for (int j = 0; j < ppl && idx < _particleCount; j++)
                {
                    float angle = (float)j / ppl * 2f * Mathf.PI;
                    float r = Mathf.Sqrt((float)i / ppl) * _bucketRadiusM * 0.85f;
                    float x = r * Mathf.Cos(angle) + (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.04f;
                    float z = r * Mathf.Sin(angle) + (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.04f;

                    _positions[idx] = new float3(x, y, z);
                    _velocities[idx] = float3.zero;
                    _predictedPositions[idx] = _positions[idx];
                    _densities[idx] = _restDensity;
                    _pressures[idx] = 0f;
                    _forces[idx] = float3.zero;
                    _active[idx] = true;
                    _temperatures[idx] = _envData.temperature;
                    idx++;
                }
        }

        for (int i = idx; i < _particleCount; i++) _active[i] = false;
        _activeCount = idx;
    }

    // ══════════════════════════════════════════════════════════════
    // UpdateBucketState — يُستدعى من HandlePaint (بالمتر)
    // ══════════════════════════════════════════════════════════════
    public void UpdateBucketState(Vector3 worldPosM, Vector3 velMs,
                                  Vector3 angVel = default, Vector3 angAcc = default)
    {
        _bucketWorldPosM = worldPosM;
        _bucketVelMs = velMs;
        _bucketAngVel = angVel;
        _bucketAngAcc = angAcc;
    }

    // ══════════════════════════════════════════════════════════════
    // Step الرئيسي
    // ══════════════════════════════════════════════════════════════
    public void Step(float dt)
    {
        if (!_isInitialized || _activeCount == 0) return;

        int subSteps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.016f), 1, 2);
        float subDt = dt / subSteps;

        for (int s = 0; s < subSteps; s++) SubStep(subDt);

        _simulationTime += dt;
        _frameCounter++;
    }

    private void SubStep(float dt)
    {
        PredictPositions(dt);
        BuildSpatialHash();
        ComputeDensityPressure();
        ComputeForces(dt);
        Integrate(dt);
        ApplyBoundaryConditions();
        CheckExit();
    }

    // ══════════════════════════════════════════════════════════════
    // 1. Predict
    // ══════════════════════════════════════════════════════════════
    private void PredictPositions(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
            if (_active[i])
                _predictedPositions[i] = _positions[i] + _velocities[i] * dt;
    }

    // ══════════════════════════════════════════════════════════════
    // 2. Spatial Hash
    // ══════════════════════════════════════════════════════════════
    private void BuildSpatialHash()
    {
        int totalCells = _gridDimensions.x * _gridDimensions.y * _gridDimensions.z;

        for (int i = 0; i <= totalCells; i++) _cellStart[i] = 0;
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            int ci = GetCellIndex(GetCell(_predictedPositions[i]));
            if (ci >= 0 && ci < totalCells) _cellStart[ci + 1]++;
        }
        for (int i = 1; i <= totalCells; i++) _cellStart[i] += _cellStart[i - 1];

        var tmp = new NativeArray<int>(totalCells + 1, Allocator.Temp);
        for (int i = 0; i <= totalCells; i++) tmp[i] = _cellStart[i];
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            int ci = GetCellIndex(GetCell(_predictedPositions[i]));
            if (ci >= 0 && ci < totalCells)
            {
                _cellEntries[tmp[ci]] = i;
                tmp[ci]++;
            }
        }
        tmp.Dispose();
    }

    private int3 GetCell(float3 pos) => new int3(
        Mathf.FloorToInt((pos.x + _bucketRadiusM) / _cellSize),
        Mathf.FloorToInt((pos.y + _bucketHeightM * 0.5f) / _cellSize),
        Mathf.FloorToInt((pos.z + _bucketRadiusM) / _cellSize));

    private int GetCellIndex(int3 c)
    {
        if (c.x < 0 || c.x >= _gridDimensions.x ||
            c.y < 0 || c.y >= _gridDimensions.y ||
            c.z < 0 || c.z >= _gridDimensions.z) return -1;
        return c.x + _gridDimensions.x * (c.y + _gridDimensions.y * c.z);
    }

    // ══════════════════════════════════════════════════════════════
    // 3. Density & Pressure
    // ══════════════════════════════════════════════════════════════
    private void ComputeDensityPressure()
    {
        int totalCells = _gridDimensions.x * _gridDimensions.y * _gridDimensions.z;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float density = 0f;
            float3 pos = _predictedPositions[i];
            int3 cell = GetCell(pos);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int ci = GetCellIndex(cell + new int3(dx, dy, dz));
                        if (ci < 0) continue;
                        int start = _cellStart[ci];
                        int end = (ci + 1 <= totalCells) ? _cellStart[ci + 1] : _particleCount;
                        for (int k = start; k < end && k < _particleCount; k++)
                        {
                            int pi = _cellEntries[k];
                            if (pi == i || !_active[pi]) continue;
                            float dSq = math.lengthsq(pos - _predictedPositions[pi]);
                            if (dSq < _h * _h)
                            {
                                float dH = _h * _h - dSq;
                                density += dH * dH * dH;
                            }
                        }
                    }

            _densities[i] = math.max(density * _poly6Factor, _restDensity * 0.1f);
            _pressures[i] = _pressureStiffness * (_densities[i] - _restDensity);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 4. Forces
    // ══════════════════════════════════════════════════════════════
    private void ComputeForces(float dt)
    {
        float shear = ComputeAvgShearRate();
        float mu = GetDynamicViscosity(shear);
        int totalCells = _gridDimensions.x * _gridDimensions.y * _gridDimensions.z;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            float3 fP = float3.zero, fV = float3.zero, fS = float3.zero;
            float3 pos = _predictedPositions[i];
            int3 cell = GetCell(pos);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int ci = GetCellIndex(cell + new int3(dx, dy, dz));
                        if (ci < 0) continue;
                        int start = _cellStart[ci];
                        int end = (ci + 1 <= totalCells) ? _cellStart[ci + 1] : _particleCount;
                        for (int k = start; k < end && k < _particleCount; k++)
                        {
                            int pi = _cellEntries[k];
                            if (pi == i || !_active[pi]) continue;

                            float3 diff = pos - _predictedPositions[pi];
                            float dist = math.length(diff);
                            if (dist >= _h || dist < 0.001f) continue;
                            float dH = _h - dist;

                            float pAvg = (_pressures[i] + _pressures[pi]) * 0.5f;
                            float rhoAvg = math.max((_densities[i] + _densities[pi]) * 0.5f, 0.001f);
                            fP += diff / dist * (-_spikyFactor * dH * dH * pAvg / (rhoAvg * rhoAvg));

                            float3 vDiff = _velocities[pi] - _velocities[i];
                            fV += vDiff * (_viscosityFactor * dH * mu / (_densities[pi] * _densities[i] + 0.001f));

                            if (dist < _h * 0.5f)
                            {
                                float r2 = _h * 0.5f - dist;
                                fS += diff / dist * (-_surfaceTension * r2 * r2);
                            }
                        }
                    }

            float3 fGrav = new float3(0f, -GRAVITY, 0f);
            float3 fCor = -2f * math.cross(_bucketAngVel, _velocities[i]);
            float3 fCen = -math.cross(_bucketAngVel, math.cross(_bucketAngVel, pos));
            float3 fEuler = -math.cross(_bucketAngAcc, pos);

            float3 total = fP + fV + fS + fGrav + fCor + fCen + fEuler;
            _forces[i] = math.clamp(total, new float3(-500f), new float3(500f));
        }
    }

    private float ComputeAvgShearRate()
    {
        float total = 0f; int cnt = 0;
        for (int i = 0; i < _particleCount; i++)
            if (_active[i]) { total += math.length(_velocities[i]) / (_h + 0.001f); cnt++; }
        return cnt > 0 ? total / cnt : 0f;
    }

    private float GetDynamicViscosity(float shearRate)
    {
        float mu0 = _paintData.Mu0, muInf = _paintData.MuInf;
        float K = _paintData.CrossK, n = _paintData.FlowN;
        float denom = 1f + math.pow(K * math.abs(shearRate), n);
        return muInf + (mu0 - muInf) / denom;
    }

    // ══════════════════════════════════════════════════════════════
    // 5. Integrate
    // ══════════════════════════════════════════════════════════════
    private void Integrate(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float3 accel = _forces[i] / math.max(_densities[i], 100f);
            _velocities[i] = math.clamp(_velocities[i] + accel * dt,
                                        new float3(-10f), new float3(10f));
            _positions[i] += _velocities[i] * dt;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 6. Boundary
    // ══════════════════════════════════════════════════════════════
    private void ApplyBoundaryConditions()
    {
        const float rest = 0.2f;
        const float frict = 0.1f;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float3 pos = _positions[i];
            float3 vel = _velocities[i];

            // جدار أسطواني
            float rXZ = math.length(new float2(pos.x, pos.z));
            if (rXZ > _bucketRadiusM * 0.95f)
            {
                float3 n = math.normalizesafe(new float3(pos.x, 0f, pos.z));
                pos -= n * (rXZ - _bucketRadiusM * 0.95f);
                float vn = math.dot(vel, n);
                if (vn > 0f) vel -= n * vn * (1f + rest);
                float3 vt = vel - n * math.dot(vel, n);
                vel -= vt * frict;
            }

            // سقف
            if (pos.y > _bucketHeightM * 0.5f)
            {
                pos.y = _bucketHeightM * 0.5f;
                if (vel.y > 0f) vel.y *= -rest;
            }

            // قاع (لا إيقاف — نسمح بالخروج عبر CheckExit)
            float botY = -_bucketHeightM * 0.5f;
            if (pos.y < botY)
            {
                pos.y = botY;
                if (vel.y < 0f) vel.y *= -rest * 0.3f;
            }

            _positions[i] = pos;
            _velocities[i] = vel;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 7. CheckExit — الجسيمات التي تخرج من الثقب
    // ══════════════════════════════════════════════════════════════
    private void CheckExit()
    {
        _exitingParticles.Clear();
        if (_bucketData.holes.Count == 0) return;

        float botY = -_bucketHeightM * 0.5f;
        float exitThreshold = botY + 0.008f;       // 8 مم فوق القاع
      //  float minPressure = _pressureStiffness * 0.0005f;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            if (_positions[i].y > exitThreshold) continue;
            //if (_pressures[i] < minPressure) continue;
            //if (_velocities[i].y > 0f) continue;

            foreach (var hole in _bucketData.holes)
            {
                float hAngle = hole.angularPosition * Mathf.Deg2Rad;
                float hDist = 0f;
                float hx = hDist * Mathf.Cos(hAngle);
                float hz = hDist * Mathf.Sin(hAngle);

                float dx = _positions[i].x - hx;
                float dz = _positions[i].z - hz;

                // منطقة الخروج = دائرة نصف قطرها 3 × r_hole
                if (dx * dx + dz * dz < hole.radius * hole.radius * 9f)
                {
                    float vExit = ComputeExitVelocityM(i, hole);  // m/s

                    // ✅ الموضع والسرعة بالمتر
                    _exitingParticles.Add(new ExitingParticle
                    {
                        position = ConvertPosToWorldM(_positions[i]),
                        velocity = ComputeExitVelocityVecM(i, hole, vExit),
                        density = _densities[i],
                        pressure = _pressures[i],
                        temperature = _temperatures[i],
                        radius = 0.003f,    // 3 مم
                        color = _paintData.colors.Length > 0
                                      ? _paintData.colors[0] : Color.red
                    });

                    _active[i] = false;
                    _activeCount--;
                    break;
                }
            }
        }

        _fillRatio = (float)_activeCount / Mathf.Max(_particleCount, 1);
    }

    // ── سرعة الخروج بالمتر/ثانية (Torricelli) ──
    private float ComputeExitVelocityM(int idx, HoleData hole)
    {
        float h = _positions[idx].y - (-_bucketHeightM * 0.5f);
        h = math.max(h, 0.001f);
        float vT = math.sqrt(2f * GRAVITY * h);         // m/s
        float mu = GetDynamicViscosity(ComputeAvgShearRate());
        float r = hole.radius;
        float L = 0.005f;
        float corr = math.clamp(
            1f - (8f * mu * L) / (Mathf.PI * r * r * r * _restDensity * vT + 0.001f),
            0.1f, 1f);
        return vT * corr * hole.dischargeCoefficient;
    }

    // ── متجه السرعة الكاملة عند الخروج (بالمتر/ثانية) ──
    private float3 ComputeExitVelocityVecM(int idx, HoleData hole, float exitSpeedMs)
    {
        // اتجاه رئيسي: للأسفل
        float3 dir = new float3(0f, -1f, 0f);

        // أضف سرعة الدلو
        dir += _bucketVelMs * 0.1f;

        // تشتت بسيط
        dir.x += (UnityEngine.Random.value - 0.5f) * 0.1f;
        dir.z += (UnityEngine.Random.value - 0.5f) * 0.1f;
        dir = math.normalizesafe(dir);

        // ✅ النتيجة بالمتر/ثانية
        return dir * exitSpeedMs + (float3)_bucketVelMs;
    }

    // ── تحويل الموضع المحلي (متر) إلى الفضاء العالمي (متر) ──
    private float3 ConvertPosToWorldM(float3 localPosM)
    {
        // ✅ موضع الدلو بالمتر + الإزاحة المحلية بالمتر
        return _bucketWorldPosM + localPosM;
    }

    // ══════════════════════════════════════════════════════════════
    // Getters
    // ══════════════════════════════════════════════════════════════
    public List<ExitingParticle> GetExitingParticles() => _exitingParticles;
    public bool IsEmpty() => _activeCount <= 2;
    public int GetActiveCount() => _activeCount;
    public float GetFillRatio() => _fillRatio;
    public float GetSimTime() => _simulationTime;

    // ══════════════════════════════════════════════════════════════
    // Dispose
    // ══════════════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_velocities.IsCreated) _velocities.Dispose();
        if (_predictedPositions.IsCreated) _predictedPositions.Dispose();
        if (_densities.IsCreated) _densities.Dispose();
        if (_pressures.IsCreated) _pressures.Dispose();
        if (_forces.IsCreated) _forces.Dispose();
        if (_active.IsCreated) _active.Dispose();
        if (_temperatures.IsCreated) _temperatures.Dispose();
        if (_cellStart.IsCreated) _cellStart.Dispose();
        if (_cellEntries.IsCreated) _cellEntries.Dispose();
        if (_sortedIndices.IsCreated) _sortedIndices.Dispose();
    }
}