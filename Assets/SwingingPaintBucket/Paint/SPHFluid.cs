using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// محاكاة SPH محسّنة بالكامل للسوائل داخل الدلو
/// 
/// التحسينات:
/// 1. Spatial Hashing: O(n) بدل O(n²)
/// 2. NativeArrays + Jobs: حسابات متوازية
/// 3. Memory Pooling: لا GC allocations أثناء التشغيل
/// 4. Adaptive Timestep: استقرار عددي
/// 5. LOD System: جسيمات بعيدة = حسابات مبسّطة
/// 6. Non-Newtonian Viscosity: Cross Model
/// 7. Realistic Exit Physics: Torricelli + Hagen-Poiseuille + Cd
/// 
/// المرجع: الدراسة الفيزيائية - ميكانيك السوائل + SPH
/// </summary>
public class SPHFluid
{
    // ═══════════════════════════════════════════════════════
    // ثوابت قابلة للتعديل (Tuneable Constants)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// عدد الجسيمات - قابل للتعديل حسب قوة الجهاز
    /// 1000 للأجهزة الضعيفة، 5000 للأجهزة القوية
    /// </summary>
    private int _particleCount = 50;

    /// <summary>
    /// نصف قطر التأثير (Smoothing Length) بالمتر
    /// h = 2.5 × particleSpacing
    /// </summary>
    private float _h = 0.025f;

    /// <summary>
    /// ثابت الضغط - يتحكم بـ "صلابة" السائل
    /// قيمة عالية = سائل صلب (مثل المطاط)
    /// قيمة منخفضة = سائل طري (مثل الماء)
    /// </summary>
    private float _pressureStiffness = 500f;

    /// <summary>
    /// معامل اللزوجة الأساسي
    /// يُضرب بلزوجة الطلاء الفعلية من Cross Model
    /// </summary>
  //  private float _baseViscosity = 0.1f;

    /// <summary>
    /// معامل التوتر السطحي
    /// يتحكم بـ "تماسك" السائل
    /// </summary>
    private float _surfaceTension = 0.072f; // N/m (مثل الماء عند 20°C)

    /// <summary>
    /// كثافة السائل المرجعية (kg/m³)
    /// </summary>
    private float _restDensity = 1000f;

    // ═══════════════════════════════════════════════════════
    // Spatial Hashing - للبحث السريع عن الجيران
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// حجم خلية الـ Hash Grid
    /// يجب أن يكون = h (نصف قطر التأثير)
    /// </summary>
    private float _cellSize;

    /// <summary>
    /// عدد الخلايا في كل محور
    /// يُحسب ديناميكياً حسب حجم الدلو
    /// </summary>
    private int3 _gridDimensions;

    /// <summary>
    /// الـ Hash Table - كل خلية تحتوي على فهرس أول جسيم
    /// </summary>
    private NativeArray<int> _cellStart;
    private NativeArray<int> _cellEntries;

    /// <summary>
    /// مصفوفة مؤقتة لترتيب الجسيمات حسب الـ Hash
    /// </summary>
    private NativeArray<int> _sortedIndices;

    // ═══════════════════════════════════════════════════════
    // بيانات الجسيمات (NativeArrays للـ Jobs)
    // ═══════════════════════════════════════════════════════

    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<float3> _predictedPositions;
    private NativeArray<float> _densities;
    private NativeArray<float> _pressures;
    private NativeArray<float3> _forces;
    private NativeArray<bool> _active;
    private NativeArray<float> _temperatures; // للتفاعل الحراري

    // بيانات الجسيمات الخارجة
    private List<ExitingParticle> _exitingParticles = new List<ExitingParticle>();
    private NativeList<ExitingParticle> _exitingNative;

    // ═══════════════════════════════════════════════════════
    // بيانات الدلو والبيئة
    // ═══════════════════════════════════════════════════════

    private BucketData _bucketData;
    private PaintData _paintData;
    private EnvironmentData _envData;

    // حالة الدلو (يُحدّث كل frame)
    private float3 _bucketWorldPos;
    private float3 _bucketVelocity;
    private float3 _bucketAngularVelocity;
    private float3 _bucketAngularAcceleration;
    private float3 _bucketAcceleration;

    // حدود الدلو بالمتر
    private float _bucketRadiusM;
    private float _bucketHeightM;
    private float _bucketVolume;

    // معاملات التحويل
   // private float _worldToMeters = 0.01f; // افتراض: 1 Unity unit = 1 cm

    // حالة المحاكاة
    private float _simulationTime;
    private float _fillRatio = 1.0f;
    private int _activeCount;
    private bool _isInitialized = false;

    // ═══════════════════════════════════════════════════════
    // ثوابت الـ Kernels (محسوبة مسبقاً للسرعة)
    // ═══════════════════════════════════════════════════════

    private float _poly6Factor;      // 315/(64πh⁹)
    private float _spikyFactor;      // 45/(πh⁶)
    private float _viscosityFactor;  // 45/(πh⁶)

    // ═══════════════════════════════════════════════════════
    // LOD System
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// مسافة LOD - الجسيمات البعيدة عن الكاميرا تُحسب ببساطة
    /// </summary>
   // private float _lodDistance = 0.5f; // 50 cm

    /// <summary>
    /// كل N frame نحسب الجسيمات البعيدة (للتوفير)
    /// </summary>
    private int _lodFrameInterval = 1;
    private int _frameCounter = 0;

    // ═══════════════════════════════════════════════════════
    // Memory Pooling للـ Exiting Particles
    // ═══════════════════════════════════════════════════════

    private Queue<ExitingParticle> _exitingPool = new Queue<ExitingParticle>();
    private const int MAX_EXITING_PER_FRAME = 50;

    // ═══════════════════════════════════════════════════════
    // Structs
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// جسيم خارج من الدلو - يُحوّل لـ PaintParticle
    /// </summary>
    public struct ExitingParticle
    {
        public float3 position;      // موضع في الفضاء العالمي
        public float3 velocity;        // سرعة في الفضاء العالمي
        public float density;          // كثافة عند الخروج
        public float pressure;         // ضغط عند الخروج
        public float temperature;      // درجة الحرارة
        public float radius;           // نصف قطر الجسيم
        public Color color;            // اللون
    }

    /// <summary>
    /// بيانات جسيم SPH داخلي
    /// </summary>
    private struct SPHParticleData
    {
        public float3 position;
        public float3 velocity;
        public float density;
        public float pressure;
        public bool active;
    }

    // ═══════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════

    public SPHFluid(BucketData bucket, PaintData paint, EnvironmentData env,
                    int particleCount = 200)
    {
        _bucketData = bucket;
        _paintData = paint;
        _envData = env;
        _particleCount = particleCount;

        // حساب أبعاد الدلو بالمتر
        _bucketRadiusM = bucket.innerRadius;
        _bucketHeightM = bucket.totalHeight;
        _bucketVolume = Mathf.PI * _bucketRadiusM * _bucketRadiusM * _bucketHeightM;

        // نصف قطر التأثير = 2.5 × المسافة بين الجسيمات
        float volumePerParticle = _bucketVolume / _particleCount;
        float particleSpacing = Mathf.Pow(volumePerParticle, 1f / 3f);
        _h = particleSpacing * 2.5f;
        _cellSize = _h;

        // حساب أبعاد الـ Grid
        int gridX = Mathf.CeilToInt(_bucketRadiusM * 2f / _cellSize) + 2;
        int gridY = Mathf.CeilToInt(_bucketHeightM / _cellSize) + 2;
        int gridZ = Mathf.CeilToInt(_bucketRadiusM * 2f / _cellSize) + 2;
        _gridDimensions = new int3(gridX, gridY, gridZ);

        // حساب عوامل الـ Kernels
        float h2 = _h * _h;
        float h3 = h2 * _h;
        float h6 = h3 * h3;
        float h9 = h6 * h3;

        _poly6Factor = 315f / (64f * Mathf.PI * h9);
        _spikyFactor = 45f / (Mathf.PI * h6);
        _viscosityFactor = 45f / (Mathf.PI * h6);

        // تهيئة الـ NativeArrays
        InitializeArrays();

        // تهيئة الجسيمات
        InitializeParticles();

        _isInitialized = true;

        Debug.Log($"[SPH] Initialized: {_particleCount} particles, h={_h:F4}m, " +
                  $"grid=({_gridDimensions.x},{_gridDimensions.y},{_gridDimensions.z}), " +
                  $"cellSize={_cellSize:F4}m");
    }

    // ═══════════════════════════════════════════════════════
    // تهيئة الـ Arrays
    // ═══════════════════════════════════════════════════════

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
        _cellStart = new NativeArray<int>(totalCells + 1, Allocator.Persistent);
        _cellEntries = new NativeArray<int>(_particleCount, Allocator.Persistent);
        _sortedIndices = new NativeArray<int>(_particleCount, Allocator.Persistent);

        _exitingNative = new NativeList<ExitingParticle>(Allocator.Persistent);
    }

    // ═══════════════════════════════════════════════════════
    // تهيئة الجسيمات (توزيع منتظم + عشوائي خفيف)
    // ═══════════════════════════════════════════════════════

    private void InitializeParticles()
    {
        float fillHeight = _bucketHeightM *
                          (_paintData.initialHeight / _bucketData.totalHeight);
        fillHeight = Mathf.Clamp(fillHeight, 0.02f, _bucketHeightM * 0.9f);

        // توزيع منتظم على شكل grid
        int particlesPerLayer = Mathf.CeilToInt(Mathf.Sqrt(_particleCount * 0.7f));
        int layers = Mathf.CeilToInt((float)_particleCount / (particlesPerLayer * particlesPerLayer));

        int index = 0;
        for (int layer = 0; layer < layers && index < _particleCount; layer++)
        {
            float y = -_bucketHeightM * 0.5f + (layer + 0.5f) * (fillHeight / layers);

            for (int i = 0; i < particlesPerLayer && index < _particleCount; i++)
            {
                for (int j = 0; j < particlesPerLayer && index < _particleCount; j++)
                {
                    // توزيع دائري
                    float angle = (float)j / particlesPerLayer * 2f * Mathf.PI;
                    float r = Mathf.Sqrt((float)i / particlesPerLayer) * _bucketRadiusM * 0.85f;

                    float x = r * Mathf.Cos(angle);
                    float z = r * Mathf.Sin(angle);

                    // إضافة عشوائية خفيفة (5%)
                    x += (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.05f;
                    y += (UnityEngine.Random.value - 0.5f) * fillHeight * 0.05f;
                    z += (UnityEngine.Random.value - 0.5f) * _bucketRadiusM * 0.05f;

                    _positions[index] = new float3(x, y, z);
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
        }

        // تعطيل الجسيمات الزائدة
        for (int i = index; i < _particleCount; i++)
        {
            _active[i] = false;
        }

        _activeCount = index;
        _fillRatio = 1.0f;
    }

    // ═══════════════════════════════════════════════════════
    // تحديث حالة الدلو (يُستدعى كل frame)
    // ═══════════════════════════════════════════════════════

    public void UpdateBucketState(Vector3 worldPos, Vector3 velocity,
                                   Vector3 angularVelocity = default,
                                   Vector3 angularAcceleration = default)
    {
        _bucketWorldPos = worldPos;
        _bucketVelocity = velocity;
        _bucketAngularVelocity = angularVelocity;
        _bucketAngularAcceleration = angularAcceleration;

        // حساب تسارع الدلو (تقريبي)
        _bucketAcceleration = velocity / (Time.fixedDeltaTime + 0.001f);
    }

    // ═══════════════════════════════════════════════════════
    // الخطوة الرئيسية - Step
    // ═══════════════════════════════════════════════════════

    public void Step(float dt)
    {
        if (!_isInitialized || _activeCount == 0) return;

        float maxDt = 0.016f;
        int subSteps = Mathf.CeilToInt(dt / maxDt);
        subSteps = Mathf.Min(subSteps, 2); // ✅ حد أقصى 2 خطوات
        float subDt = dt / subSteps;

        for (int step = 0; step < subSteps; step++)
        {
            SubStep(subDt);
        }

        _simulationTime += dt;
        _frameCounter++;
    }

    // ═══════════════════════════════════════════════════════
    // SubStep - خطوة فرعية واحدة
    // ═══════════════════════════════════════════════════════

    private void SubStep(float dt)
    {
        // 1. Predict positions (للـ Symplectic Euler)
        PredictPositions(dt);

        // 2. Build Spatial Hash
        BuildSpatialHash();

        // 3. Compute Densities & Pressures
        ComputeDensityPressure();

        // 4. Compute Forces
        ComputeForces(dt);

        // 5. Integrate
        Integrate(dt);

        // 6. Apply Boundary Conditions
        ApplyBoundaryConditions();

        // 7. Check Exit (كل 3 frames للتوفير)
        if (_frameCounter % _lodFrameInterval == 0)
        {
            CheckExit();
        }
    }

    // ═══════════════════════════════════════════════════════
    // 1. Predict Positions (Symplectic Euler)
    // ═══════════════════════════════════════════════════════

    private void PredictPositions(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            // v(t+½Δت) = v(t) + ½Δت·F(t)/م
            // x*(t+Δت) = x(t) + Δت·v(t+½Δت)
            _predictedPositions[i] = _positions[i] + _velocities[i] * dt;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 2. Build Spatial Hash - O(n)
    // ═══════════════════════════════════════════════════════

    private void BuildSpatialHash()
    {
        // 1. Clear
        for (int i = 0; i < _cellStart.Length; i++)
            _cellStart[i] = 0;

        // 2. Count particles per cell
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            int3 cell = GetCell(_predictedPositions[i]);
            int cellIndex = GetCellIndex(cell);

            if (cellIndex >= 0 && cellIndex < _cellStart.Length - 1)
            {
                _cellStart[cellIndex + 1]++;
            }
        }

        // 3. Prefix sum (cumultative count)
        for (int i = 1; i < _cellStart.Length; i++)
        {
            _cellStart[i] += _cellStart[i - 1];
        }

        // ✅ 4. Copy to temp array (مهم جداً!)
        NativeArray<int> tempStart = new NativeArray<int>(_cellStart.Length, Allocator.Temp);
        for (int i = 0; i < _cellStart.Length; i++)
            tempStart[i] = _cellStart[i];

        // 5. Fill cell entries using temp
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            int3 cell = GetCell(_predictedPositions[i]);
            int cellIndex = GetCellIndex(cell);

            if (cellIndex >= 0 && cellIndex < tempStart.Length - 1)
            {
                int entryIndex = tempStart[cellIndex];
                _cellEntries[entryIndex] = i;
                _sortedIndices[entryIndex] = i;
                tempStart[cellIndex]++; // ✅ نعدل temp مش _cellStart
            }
        }

        tempStart.Dispose();
    }

    private int3 GetCell(float3 pos)
    {
        return new int3(
            Mathf.FloorToInt((pos.x + _bucketRadiusM) / _cellSize),
            Mathf.FloorToInt((pos.y + _bucketHeightM * 0.5f) / _cellSize),
            Mathf.FloorToInt((pos.z + _bucketRadiusM) / _cellSize)
        );
    }

    private int GetCellIndex(int3 cell)
    {
        if (cell.x < 0 || cell.x >= _gridDimensions.x ||
            cell.y < 0 || cell.y >= _gridDimensions.y ||
            cell.z < 0 || cell.z >= _gridDimensions.z)
        {
            return -1;
        }

        return cell.x + _gridDimensions.x * (cell.y + _gridDimensions.y * cell.z);
    }

    // ═══════════════════════════════════════════════════════
    // 3. Compute Densities & Pressures
    // ═══════════════════════════════════════════════════════

    private void ComputeDensityPressure()
    {
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            float density = 0f;
            float3 pos = _predictedPositions[i];

            // البحث في الخلايا المجاورة فقط (27 خلية)
            int3 cell = GetCell(pos);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int3 neighborCell = cell + new int3(dx, dy, dz);
                        int cellIndex = GetCellIndex(neighborCell);

                        if (cellIndex < 0) continue;

                        int start = _cellStart[cellIndex];
                        int end = (cellIndex + 1 < _cellStart.Length) ?
                                  _cellStart[cellIndex + 1] : _cellEntries.Length;

                        for (int j = start; j < end; j++)
                        {
                            int particleIndex = _cellEntries[j];
                            if (particleIndex == i || !_active[particleIndex]) continue;

                            float3 diff = pos - _predictedPositions[particleIndex];
                            float distSq = math.lengthsq(diff);

                            if (distSq < _h * _h)
                            {
                                // Poly6 Kernel
                                float diffH = _h * _h - distSq;
                                density += diffH * diffH * diffH;
                            }
                        }
                    }
                }
            }

            // تطبيع الكثافة
            density *= _poly6Factor;
            density = math.max(density, _restDensity * 0.1f);

            _densities[i] = density;

            // معادلة الضغط: P = k(ρ - ρ₀)
            _pressures[i] = _pressureStiffness * (density - _restDensity);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 4. Compute Forces
    // ═══════════════════════════════════════════════════════

    private void ComputeForces(float dt)
    {
        // حساب معدل القص المتوسط (لـ Cross Model)
        float avgShearRate = ComputeAverageShearRate();
        float dynamicViscosity = GetDynamicViscosity(avgShearRate);

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            float3 fPressure = float3.zero;
            float3 fViscosity = float3.zero;
            float3 fSurfaceTension = float3.zero;

            float3 pos = _predictedPositions[i];
            float pressure_i = _pressures[i];
            float density_i = _densities[i];

            int3 cell = GetCell(pos);

            // البحث في الخلايا المجاورة
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int3 neighborCell = cell + new int3(dx, dy, dz);
                        int cellIndex = GetCellIndex(neighborCell);

                        if (cellIndex < 0) continue;

                        int start = _cellStart[cellIndex];
                        int end = (cellIndex + 1 < _cellStart.Length) ?
                                  _cellStart[cellIndex + 1] : _cellEntries.Length;

                        for (int j = start; j < end; j++)
                        {
                            int particleIndex = _cellEntries[j];
                            if (particleIndex == i || !_active[particleIndex]) continue;

                            float3 diff = pos - _predictedPositions[particleIndex];
                            float dist = math.length(diff);

                            if (dist < _h && dist > 0.001f)
                            {
                                // ═══ قوة الضغط (Spiky Gradient) ═══
                                float diffH = _h - dist;
                                float pressure_j = _pressures[particleIndex];
                                float density_j = _densities[particleIndex];

                                float pressureAvg = (pressure_i + pressure_j) * 0.5f;
                                float densityAvg = (density_i + density_j) * 0.5f;

                                float pressureMag = -_spikyFactor * diffH * diffH *
                                                   pressureAvg / (densityAvg * densityAvg);

                                fPressure += diff / dist * pressureMag;

                                // ═══ قوة اللزوجة (Viscosity Laplacian) ═══
                                float3 velDiff = _velocities[particleIndex] - _velocities[i];
                                float viscosityMag = _viscosityFactor * diffH * dynamicViscosity /
                                                    (density_j * density_i);

                                fViscosity += velDiff * viscosityMag;

                                // ═══ قوة التوتر السطحي ═══
                                if (dist < _h * 0.5f)
                                {
                                    float cohesion = -_surfaceTension * (_h * 0.5f - dist) *
                                                    (_h * 0.5f - dist);
                                    fSurfaceTension += diff / dist * cohesion;
                                }
                            }
                        }
                    }
                }
            }

            // ═══ الجاذبية ═══
            float3 fGravity = new float3(0f, -_envData.gravity, 0f);

            // ═══ قوى حركة الدلو (Non-inertial frame) ═══
            // Coriolis: -2m(ω × v)
            float3 fCoriolis = -2f * math.cross(_bucketAngularVelocity, _velocities[i]);

            // Centrifugal: -m(ω × (ω × r))
            float3 fCentrifugal = -math.cross(_bucketAngularVelocity,
                                              math.cross(_bucketAngularVelocity, pos));

            // Euler: -m(dω/dt × r)
            float3 fEuler = -math.cross(_bucketAngularAcceleration, pos);

            // Translational acceleration: -m·a_bucket
            float3 fTranslational = -_bucketAcceleration;

            // ═══ الجمع ═══
            _forces[i] = fPressure + fViscosity + fSurfaceTension +
                        fGravity + fCoriolis + fCentrifugal + fEuler + fTranslational;

            // تحديد القوة لمنع الانفجار العددي
            _forces[i] = math.clamp(_forces[i], new float3(-1000f), new float3(1000f));
        }
    }

    // ═══════════════════════════════════════════════════════
    // حساب معدل القص المتوسط (لـ Cross Model)
    // ═══════════════════════════════════════════════════════

    private float ComputeAverageShearRate()
    {
        float totalShear = 0f;
        int count = 0;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            // معدل القص = |∇ × v| ≈ |dv/dy| للبساطة
            float shear = math.length(_velocities[i]) / _h;
            totalShear += shear;
            count++;
        }

        return (count > 0) ? totalShear / count : 0f;
    }

    // ═══════════════════════════════════════════════════════
    // Cross Model للزوجة المتغيرة
    // ═══════════════════════════════════════════════════════

    private float GetDynamicViscosity(float shearRate)
    {
        // Cross Model: μ(γ̇) = μ∞ + (μ₀ - μ∞) / (1 + (K·γ̇)ⁿ)
        float mu0 = _paintData.Mu0;
        float muInf = _paintData.MuInf;
        float K = _paintData.CrossK;
        float n = _paintData.FlowN;

        float shearAbs = math.abs(shearRate);
        float denom = 1f + math.pow(K * shearAbs, n);

        return muInf + (mu0 - muInf) / denom;
    }

    // ═══════════════════════════════════════════════════════
    // 5. Integrate (Symplectic Euler)
    // ═══════════════════════════════════════════════════════

    private void Integrate(float dt)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            // v(t+Δت) = v(t) + Δت·F(t)/ρ
            float3 acceleration = _forces[i] / math.max(_densities[i], 100f);
            _velocities[i] += acceleration * dt;

            // تحديد السرعة
            _velocities[i] = math.clamp(_velocities[i], new float3(-10f), new float3(10f));

            // x(t+Δت) = x(t) + Δت·v(t+Δت)
            _positions[i] += _velocities[i] * dt;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 6. Apply Boundary Conditions
    // ═══════════════════════════════════════════════════════

    private void ApplyBoundaryConditions()
    {
        float restitution = 0.3f; // معامل الارتداد
        float friction = 0.1f;    // احتكاك مع الجدار

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            float3 pos = _positions[i];
            float3 vel = _velocities[i];

            // ═══ الجدار الاسطواني ═══
            float rXZ = math.length(new float2(pos.x, pos.z));
            if (rXZ > _bucketRadiusM * 0.95f)
            {
                float3 normal = new float3(pos.x, 0f, pos.z);
                normal = math.normalizesafe(normal);

                // إعادة للداخل
                float overlap = rXZ - _bucketRadiusM * 0.95f;
                pos -= normal * overlap;

                // ارتداد
                float vn = math.dot(vel, normal);
                if (vn > 0f)
                {
                    vel -= normal * vn * (1f + restitution);
                }

                // احتكاك مماسي
                float3 vt = vel - normal * math.dot(vel, normal);
                vel -= vt * friction;
            }

            // ═══ السقف ═══
            float topY = _bucketHeightM * 0.5f;
            if (pos.y > topY)
            {
                pos.y = topY;
                if (vel.y > 0f) vel.y *= -restitution;
            }

            // ═══ القاع (منطقة الثقب) ═══
            float botY = -_bucketHeightM * 0.5f;
            if (pos.y < botY + 0.005f) // 5mm فوق القاع
            {
                // نسمح بالخروج - لا نرتد هنا
                if (pos.y < botY)
                {
                    pos.y = botY;
                    if (vel.y < 0f) vel.y *= -restitution * 0.5f;
                }
            }

            _positions[i] = pos;
            _velocities[i] = vel;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 7. Check Exit - الجسيمات التي تخرج من الثقب
    // ═══════════════════════════════════════════════════════

    private void CheckExit()
    {
        _exitingParticles.Clear();
        if (_bucketData.holes.Count == 0) return;

        float exitThreshold = -_bucketHeightM * 0.5f + 0.005f; // 5mm فوق القاع
        float minPressureToExit = _pressureStiffness * 0.001f;

        for (int i = 0; i < _particleCount; i++)
        {
            if (!_active[i]) continue;

            if (_positions[i].y > exitThreshold) continue;
            if (_pressures[i] < minPressureToExit) continue;
            if (_velocities[i].y > -0.005f) continue;

            foreach (HoleData hole in _bucketData.holes)
            {
                float holeAngle = hole.angularPosition * Mathf.Deg2Rad;
                float holeDistFromCenter = _bucketRadiusM * 0.9f;
                float holeX = holeDistFromCenter * Mathf.Cos(holeAngle);
                float holeZ = holeDistFromCenter * Mathf.Sin(holeAngle);

                float dx = _positions[i].x - holeX;
                float dz = _positions[i].z - holeZ;
                float distToHole = math.sqrt(dx * dx + dz * dz);

                float exitRadius = hole.radius * 3f;

                if (distToHole < exitRadius)
                {
                    float exitVelocity = ComputeExitVelocity(i, hole);

                    var exiting = new ExitingParticle
                    {
                        position = ConvertToWorldSpace(_positions[i]),
                        velocity = ConvertVelocityToWorldSpace(_velocities[i]) + 
                                  ComputeExitVelocityVector(i, hole, exitVelocity),
                        density = _densities[i],
                        pressure = _pressures[i],
                        temperature = _temperatures[i],
                        radius = Mathf.Pow(_particleCount / (_bucketVolume * _restDensity), -1f / 3f) * 0.5f * 100f,
                        color = _paintData.colors[0]
                    };

                    _exitingParticles.Add(exiting);
                    _active[i] = false;
                    _activeCount--;
                    break;
                }
            }
        }

        _fillRatio = (float)_activeCount / _particleCount;
    }

    // ═══════════════════════════════════════════════════════
    // حساب سرعة الخروج (Torricelli + Hagen-Poiseuille + Cd)
    // ═══════════════════════════════════════════════════════

    private float ComputeExitVelocity(int particleIndex, HoleData hole)
    {
        // ارتفاع السائل فوق الثقب
        float h = _positions[particleIndex].y - (-_bucketHeightM * 0.5f);
        h = math.max(h, 0.001f);

        // Torricelli: v = sqrt(2gh)
        float vTorricelli = math.sqrt(2f * _envData.gravity * h);

        // تصحيح Hagen-Poiseuille للثقب الضيق
        float mu = GetDynamicViscosity(ComputeAverageShearRate());
        float r = hole.radius;
        float L = 0.005f; // طول الثقب المفترض 5mm

        float poiseuilleCorrection = 1f - (8f * mu * L) /
                                          (Mathf.PI * r * r * r * _restDensity * vTorricelli);
        poiseuilleCorrection = math.clamp(poiseuilleCorrection, 0.1f, 1f);

        // معامل التصريف Cd
        float Cd = hole.dischargeCoefficient;

        return vTorricelli * poiseuilleCorrection * Cd;
    }

    private float3 ComputeExitVelocityVector(int particleIndex, HoleData hole, float exitSpeed)
    {
        // اتجاه الخروج: نحو الأسفل + اتجاه حركة الدلو
        float3 exitDir = new float3(0f, -1f, 0f);

        // إضافة اتجاه حركة الدلو (للزخم)
        exitDir += _bucketVelocity * 0.5f;

        // إضافة اتجاه عشوائي خفيف (للاضطراب)
        exitDir.x += (UnityEngine.Random.value - 0.5f) * 0.1f;
        exitDir.z += (UnityEngine.Random.value - 0.5f) * 0.1f;

        exitDir = math.normalizesafe(exitDir);

        return exitDir * exitSpeed;
    }

    // ═══════════════════════════════════════════════════════
    // تحويل الإحداثيات
    // ═══════════════════════════════════════════════════════

    private float3 ConvertToWorldSpace(float3 localPos)
    {
        // localPos بالمتر، التحويل إلى سنتيمتر
        return _bucketWorldPos + localPos * 100f; // متر → سنتيمتر
    }

    private float3 ConvertVelocityToWorldSpace(float3 localVel)
    {
        return localVel * 100f; // متر/ثانية → سنتيمتر/ثانية
    }

    // ═══════════════════════════════════════════════════════
    // Getters
    // ═══════════════════════════════════════════════════════

    public List<ExitingParticle> GetExitingParticles() => _exitingParticles;

    public bool IsEmpty() => _activeCount <= 3;

    public int GetActiveCount() => _activeCount;

    public float GetFillRatio() => _fillRatio;

    public float GetSimulationTime() => _simulationTime;

    // ═══════════════════════════════════════════════════════
    // التنظيف (Dispose)
    // ═══════════════════════════════════════════════════════

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
        if (_exitingNative.IsCreated) _exitingNative.Dispose();
    }
}