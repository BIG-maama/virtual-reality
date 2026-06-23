using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// SPH Fluid - نسخة محسّنة
///   ✅ CheckExit مُصلَح: الثقب المركزي (angle=0, height=0) يلتقط كل الجسيمات السفلية
///   ✅ 1000 جسيم بدل 50
///   ✅ FillActivePositions() للـ GPU Instancing
///   ✅ subSteps=2 للاستقرار
/// </summary>
public class SPHFluid
{
    private int _particleCount = 1000;
    private float _h = 0.025f;
    private float _pressureStiffness = 200f;
    private float _surfaceTension = 0.072f;
    private float _restDensity = 1000f;
    private const float GRAVITY_MS2 = 9.80665f;

    private float _cellSize;
    private int3 _gridDimensions;
    private NativeArray<int> _cellStart;
    private NativeArray<int> _cellEntries;
    private NativeArray<int> _sortedIndices;

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<float3> _predictedPositions;
    private NativeArray<float> _densities;
    private NativeArray<float> _pressures;
    private NativeArray<float3> _forces;
    private NativeArray<bool> _active;
    private NativeArray<float> _temperatures;

    private List<ExitingParticle> _exitingParticles = new List<ExitingParticle>();

    private BucketData _bucketData;
    private PaintData _paintData;
    private EnvironmentData _envData;

    private float3 _bucketWorldPosCM;
    private float3 _bucketVelCMps;
    private float3 _bucketAngVel;
    private float3 _bucketAngAcc;

    private float _bucketRadiusM;
    private float _bucketHeightM;
    private float _bucketVolume;

    private float _simulationTime;
    private float _fillRatio = 1.0f;
    private int _activeCount;
    private bool _isInitialized = false;

    private float _poly6Factor;
    private float _spikyFactor;
    private float _viscosityFactor;

    private int _frameCounter = 0;

    public struct ExitingParticle
    {
        public float3 position;
        public float3 velocity;
        public float density;
        public float pressure;
        public float temperature;
        public float radius;
        public Color color;
    }

    // ═══════════════ Constructor ═══════════════
    public SPHFluid(BucketData bucket, PaintData paint, EnvironmentData env,
                    int particleCount = 1000)
    {
        _bucketData = bucket;
        _paintData = paint;
        _envData = env;
        _particleCount = Mathf.Clamp(particleCount, 10, 1000);

        _bucketRadiusM = bucket.innerRadius;
        _bucketHeightM = bucket.totalHeight;
        _bucketVolume = Mathf.PI * _bucketRadiusM * _bucketRadiusM * _bucketHeightM;

        float volumePerParticle = _bucketVolume / _particleCount;
        float particleSpacing = Mathf.Pow(volumePerParticle, 1f / 3f);
        _h = Mathf.Max(particleSpacing * 2.0f, 0.010f);
        _cellSize = _h;

        int gridX = Mathf.CeilToInt(_bucketRadiusM * 2f / _cellSize) + 2;
        int gridY = Mathf.CeilToInt(_bucketHeightM / _cellSize) + 2;
        int gridZ = Mathf.CeilToInt(_bucketRadiusM * 2f / _cellSize) + 2;
        _gridDimensions = new int3(gridX, gridY, gridZ);

        float h2 = _h * _h, h3 = h2 * _h, h6 = h3 * h3, h9 = h6 * h3;
        _poly6Factor = 315f / (64f * Mathf.PI * h9);
        _spikyFactor = 45f / (Mathf.PI * h6);
        _viscosityFactor = 45f / (Mathf.PI * h6);

        InitializeArrays();
        InitializeParticles();
        _isInitialized = true;

        Debug.Log($"[SPH] Init: {_particleCount} particles | h={_h:F4}m | " +
                  $"R={_bucketRadiusM:F3}m H={_bucketHeightM:F3}m | holes={bucket.holes.Count}");

        foreach (var hole in bucket.holes)
            Debug.Log($"[SPH] Hole: angle={hole.angularPosition}deg " +
                      $"hFromBot={hole.heightFromBottom:F4}m r={hole.radius:F4}m");
    }

    // ═══════════════ Initialize Arrays ═══════════════
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

    // ═══════════════ Initialize Particles ═══════════════
    private void InitializeParticles()
    {
        float fillH = Mathf.Clamp(
            _bucketHeightM * (_paintData.initialHeight / _bucketData.totalHeight),
            0.02f, _bucketHeightM * 0.9f);

        int ppl = Mathf.CeilToInt(Mathf.Sqrt(_particleCount * 0.7f));
        int layers = Mathf.CeilToInt((float)_particleCount / (ppl * ppl));

        int index = 0;
        for (int layer = 0; layer < layers && index < _particleCount; layer++)
        {
            float y = -_bucketHeightM * 0.5f + (layer + 0.5f) * (fillH / layers);
            for (int i = 0; i < ppl && index < _particleCount; i++)
                for (int j = 0; j < ppl && index < _particleCount; j++)
                {
                    float angle = (float)j / ppl * 2f * Mathf.PI;
                    float r = Mathf.Sqrt((float)i / ppl) * _bucketRadiusM * 0.85f;

                    _positions[index] = new float3(
                        r * Mathf.Cos(angle) + (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.04f,
                        y,
                        r * Mathf.Sin(angle) + (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.04f);
                    _velocities[index] = float3.zero;
                    _predictedPositions[index] = _positions[index];
                    _densities[index] = _restDensity;
                    _pressures[index] = 0f;
                    _forces[index] = float3.zero;
                    _active[index] = true;
                    _temperatures[index] = _envData.temperature;
                    index++;
                }
        }
        for (int i = index; i < _particleCount; i++) _active[i] = false;
        _activeCount = index;
        _fillRatio = 1.0f;
    }

    // ═══════════════ UpdateBucketState ═══════════════
    public void UpdateBucketState(Vector3 worldPosCM, Vector3 velCMps,
                                   Vector3 angularVelocity = default,
                                   Vector3 angularAcceleration = default)
    {
        _bucketWorldPosCM = worldPosCM;
        _bucketVelCMps = velCMps;
        _bucketAngVel = angularVelocity;
        _bucketAngAcc = angularAcceleration;
    }

    // ═══════════════ Step ═══════════════
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

    // ═══════════════ Predict ═══════════════
    private void PredictPositions(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
            if (_active[i])
                _predictedPositions[i] = _positions[i] + _velocities[i] * dt;
    }

    // ═══════════════ Spatial Hash ═══════════════
    private void BuildSpatialHash()
    {
        int totalCells = _gridDimensions.x * _gridDimensions.y * _gridDimensions.z;
        for (int i = 0; i <= totalCells; i++) _cellStart[i] = 0;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            int idx = GetCellIndex(GetCell(_predictedPositions[i]));
            if (idx >= 0 && idx < totalCells) _cellStart[idx + 1]++;
        }
        for (int i = 1; i <= totalCells; i++) _cellStart[i] += _cellStart[i - 1];

        var tmp = new NativeArray<int>(totalCells + 1, Allocator.Temp);
        for (int i = 0; i <= totalCells; i++) tmp[i] = _cellStart[i];

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            int idx = GetCellIndex(GetCell(_predictedPositions[i]));
            if (idx >= 0 && idx < totalCells)
            {
                int entry = tmp[idx];
                _cellEntries[entry] = i;
                _sortedIndices[entry] = i;
                tmp[idx]++;
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

    // ═══════════════ Density & Pressure ═══════════════
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
                            float distSq = math.lengthsq(pos - _predictedPositions[pi]);
                            if (distSq < _h * _h)
                            { float dH = _h * _h - distSq; density += dH * dH * dH; }
                        }
                    }
            _densities[i] = math.max(density * _poly6Factor, _restDensity * 0.1f);
            _pressures[i] = _pressureStiffness * (_densities[i] - _restDensity);
        }
    }

    // ═══════════════ Forces ═══════════════
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
                            float rhoAvg = (_densities[i] + _densities[pi]) * 0.5f + 0.001f;
                            fP += diff / dist * (-_spikyFactor * dH * dH * pAvg / (rhoAvg * rhoAvg));
                            float3 vDiff = _velocities[pi] - _velocities[i];
                            fV += vDiff * (_viscosityFactor * dH * mu / (_densities[pi] * _densities[i] + 0.001f));
                            if (dist < _h * 0.5f)
                            {
                                float coh = -_surfaceTension * (_h * 0.5f - dist) * (_h * 0.5f - dist);
                                fS += diff / dist * coh;
                            }
                        }
                    }

            float3 fGrav = new float3(0f, -GRAVITY_MS2, 0f);
            float3 fCoriolis = -2f * math.cross(_bucketAngVel, _velocities[i]);
            float3 fCentrifugal = -math.cross(_bucketAngVel, math.cross(_bucketAngVel, pos));
            float3 fEuler = -math.cross(_bucketAngAcc, pos);
            float3 total = fP + fV + fS + fGrav + fCoriolis + fCentrifugal + fEuler;
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
        return muInf + (mu0 - muInf) / (1f + math.pow(K * math.abs(shearRate), n));
    }

    // ═══════════════ Integrate ═══════════════
    private void Integrate(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float3 accel = _forces[i] / math.max(_densities[i], 100f);
            _velocities[i] += accel * dt;
            _velocities[i] = math.clamp(_velocities[i], new float3(-10f), new float3(10f));
            _positions[i] += _velocities[i] * dt;
        }
    }

    // ═══════════════ Boundary ═══════════════
    private void ApplyBoundaryConditions()
    {
        const float restitution = 0.2f;
        const float friction = 0.1f;
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float3 pos = _positions[i];
            float3 vel = _velocities[i];

            float rXZ = math.length(new float2(pos.x, pos.z));
            if (rXZ > _bucketRadiusM * 0.95f)
            {
                float3 n = math.normalizesafe(new float3(pos.x, 0f, pos.z));
                pos -= n * (rXZ - _bucketRadiusM * 0.95f);
                float vn = math.dot(vel, n);
                if (vn > 0f) vel -= n * vn * (1f + restitution);
                vel -= (vel - n * math.dot(vel, n)) * friction;
            }

            float topY = _bucketHeightM * 0.5f;
            if (pos.y > topY) { pos.y = topY; if (vel.y > 0f) vel.y *= -restitution; }

            float botY = -_bucketHeightM * 0.5f;
            if (pos.y < botY) { pos.y = botY; if (vel.y < 0f) vel.y *= -restitution * 0.3f; }

            _positions[i] = pos;
            _velocities[i] = vel;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CheckExit — مُصلَح كلياً
    //
    // المشكلة القديمة:
    //   hDist = _bucketRadiusM * 0.85f  ← دائماً على الحافة
    //   حتى لو angularPosition=0 و heightFromBottom=0 (ثقب مركزي في القاع)
    //   يعني الكود كان يدور عن الجسيمات على بعد R*0.85 من المركز
    //   بينما الجسيمات في المنتصف → ما يلتقي أحد!
    //
    // الإصلاح:
    //   - ثقب قاعي (heightFromBottom ≈ 0):
    //       موضعه الأفقي على الحافة بزاوية angularPosition
    //       لكن منطقة الالتقاط = كل القاع (radius * catchFactor كبير)
    //   - ثقب جانبي (heightFromBottom > 0):
    //       موضعه على الحافة بزاوية angularPosition
    //       منطقة الالتقاط محدودة
    //   - exitThreshold رُفع لـ 30mm لالتقاط أكثر جسيمات
    // ═══════════════════════════════════════════════════════════════
    private void CheckExit()
    {
        _exitingParticles.Clear();
        if (_bucketData.holes.Count == 0) return;

        float botY = -_bucketHeightM * 0.5f;
        float exitThreshold = botY + 0.03f;   // 30mm فوق القاع

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            if (_positions[i].y > exitThreshold) continue;

            foreach (var hole in _bucketData.holes)
            {
                // موضع الثقب الأفقي الحقيقي
                float hAngle = hole.angularPosition * Mathf.Deg2Rad;
                float hx, hz, catchRadius;

                if (hole.heightFromBottom <= 0.002f)
                {
                    // ثقب قاعي: يمكن أن يكون في أي موضع أفقي
                    // إذا angularPosition=0 → الثقب في المركز
                    if (hole.angularPosition == 0f)
                    {
                        // ثقب مركزي في القاع → يلتقط كل الجسيمات السفلية
                        hx = 0f;
                        hz = 0f;
                        catchRadius = _bucketRadiusM * 0.95f; // كل القاع
                    }
                    else
                    {
                        // ثقب قاعي على حافة بزاوية محددة
                        hx = _bucketRadiusM * 0.85f * Mathf.Cos(hAngle);
                        hz = _bucketRadiusM * 0.85f * Mathf.Sin(hAngle);
                        catchRadius = hole.radius * 8f;
                    }
                }
                else
                {
                    // ثقب جانبي
                    hx = _bucketRadiusM * 0.9f * Mathf.Cos(hAngle);
                    hz = _bucketRadiusM * 0.9f * Mathf.Sin(hAngle);
                    catchRadius = hole.radius * 8f;

                    // ثقب جانبي يحتاج الجسيم يكون قريب من ارتفاعه أيضاً
                    float holeWorldY = botY + hole.heightFromBottom;
                    if (Mathf.Abs(_positions[i].y - holeWorldY) > 0.02f) continue;
                }

                float diffX = _positions[i].x - hx;
                float diffZ = _positions[i].z - hz;

                if (diffX * diffX + diffZ * diffZ < catchRadius * catchRadius)
                {
                    float vExit = ComputeExitVelocity(i, hole);
                    _exitingParticles.Add(new ExitingParticle
                    {
                        position = ConvertPosToWorldCM(_positions[i]),
                        velocity = ConvertVelToWorldCM(_velocities[i])
                                    + ComputeExitVecCM(i, hole, vExit),
                        density = _densities[i],
                        pressure = _pressures[i],
                        temperature = _temperatures[i],
                        radius = 0.2f,
                        color = _paintData.colors[0]
                    });
                    _active[i] = false;
                    _activeCount--;
                    break;
                }
            }
        }

        _fillRatio = (float)_activeCount / _particleCount;

        if (_frameCounter % 120 == 0)
            Debug.Log($"[SPH] frame={_frameCounter} | active={_activeCount} | " +
                      $"exiting={_exitingParticles.Count} | fill={_fillRatio * 100:F0}%");
    }

    private float ComputeExitVelocity(int idx, HoleData hole)
    {
        float h = _positions[idx].y - (-_bucketHeightM * 0.5f);
        h = math.max(h, 0.001f);
        float vT = math.sqrt(2f * GRAVITY_MS2 * h);
        float mu = GetDynamicViscosity(ComputeAvgShearRate());
        float r = hole.radius;
        float L = 0.005f;
        float corr = math.clamp(
            1f - (8f * mu * L) / (Mathf.PI * r * r * r * _restDensity * vT + 0.001f),
            0.1f, 1f);
        return vT * corr * hole.dischargeCoefficient;
    }

    private float3 ComputeExitVecCM(int idx, HoleData hole, float exitSpeedMS)
    {
        float3 dir = new float3(0f, -1f, 0f);
        dir += _bucketVelCMps * 0.005f;
        dir.x += (UnityEngine.Random.value - 0.5f) * 0.02f;
        dir.z += (UnityEngine.Random.value - 0.5f) * 0.02f;
        dir = math.normalizesafe(dir);
        return dir * exitSpeedMS * 100f;
    }

    private float3 ConvertPosToWorldCM(float3 localPosM)
        => _bucketWorldPosCM + localPosM * 100f;

    private float3 ConvertVelToWorldCM(float3 localVelMs)
        => localVelMs * 100f;

    // ═══════════════ GPU Data ═══════════════
    /// <summary>
    /// يملأ القائمة بمواضع الجسيمات النشطة للرسم على GPU
    /// xyz = موضع عالمي (وحدات Unity) | w = نصف القطر
    /// </summary>
    public void FillActivePositions(List<Vector4> result)
    {
        result.Clear();
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;
            float3 wp = ConvertPosToWorldCM(_positions[i]);
            result.Add(new Vector4(wp.x, wp.y, wp.z, 0.12f));
        }
    }

    // ═══════════════ Getters ═══════════════
    public List<ExitingParticle> GetExitingParticles() => _exitingParticles;
    public bool IsEmpty() => _activeCount <= 2;
    public int GetActiveCount() => _activeCount;
    public float GetFillRatio() => _fillRatio;
    public float GetSimTime() => _simulationTime;

    // ═══════════════ Dispose ═══════════════
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
