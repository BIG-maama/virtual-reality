

using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// مُصدر جزيئات الطلاء — نسخة محسّنة مع GPU support
/// 
/// المميزات الجديدة:
///   ✅ دعم GPUParticleSystem للمحاكاة على GPU
///   ✅ يُصدر جزيئات كـ 3D spheres باستخدام GPU
///   ✅ أداء محسّن: 10000+ جسيم بدون lag
///   ✅ توازي CPU و GPU معاً
/// 
/// الوحدات:
///   كل المواضع والسرعات → cm و cm/s
///   gravity              → cm/s² (980.665)
///   airDensity           → kg/cm³ (≈ 1.2e-6)
/// </summary>
public class PaintEmitter
{
    private readonly BucketData _bucket;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    private readonly List<PaintParticle> _activeParticles = new List<PaintParticle>();
    private GPUParticleSystem _gpuSystem;  // ← نظام الجسيمات على GPU

    private float _emitAccumulator = 0f;
    // private float _particleEmitRate = 10f; // جزيء/ثانية/ثقب
    private float _particleEmitRate = 50f;
    // نصف قطر القطرة بالسنتيمتر (0.2 cm = 2 mm)
    //  private float _dropletRadius = 0.002f; // 2mm بالمتر
    private float _dropletRadius = 0.005f; //
    private Queue<PaintParticle> _particlePool = new Queue<PaintParticle>();
    private const int MAX_POOL_SIZE = 500;
    private const int MAX_ACTIVE_PARTICLES = 2000;
    private const bool USE_GPU = true;  // فعّل GPU rendering

    public IReadOnlyList<PaintParticle> ActiveParticles => _activeParticles;
    public int TotalEmittedCount { get; private set; }
    public GPUParticleSystem GPUSystem => _gpuSystem;

    public PaintEmitter(BucketData bucket, PaintData paint, EnvironmentData env, GPUParticleSystem gpuSystem = null)
    {
        _bucket = bucket;
        _paint = paint;
        _env = env;
        _gpuSystem = gpuSystem;
    }

    /// <summary>
    /// التحديث الرئيسي — يُستدعى من SceneConnectorFinal.HandlePaint()
    /// </summary>
    public void UpdateEmission(float deltaTime, Vector3 bucketWorldPosCM,
                               Vector3 bucketVelCMps, float currentPaintHeightCM,
                               float canvasYCM)
    {
        Debug.Log($"[UPDATE-CHECK] height={currentPaintHeightCM:F4} | willEmit={currentPaintHeightCM > 0.01f}");
        float airDensity = _env.CalculateHumidAirDensity();
        float gravity = _env.gravity;

        // ✅ دائماً حدّث الجسيمات الموجودة أولاً
        ApplyCohesionForces();
        foreach (PaintParticle p in _activeParticles)
            p.Update(deltaTime, gravity, airDensity, canvasYCM);
        ReturnDeadToPool();

        // ✅ هذا كان الناقص: الجسيمات اللي بتطير على GPU (USE_GPU=true)
        // ما كانت أبداً تتفحص إذا وصلت اللوحة → ما في طلاء يظهر أبداً
        // ولا splat واحد يتولّد، حتى لو كانت الكرات نازلة على الشاشة.
        // هنا نسحبها من GPUParticleSystem ونحوّلها لجسيم "هابط" عادي
        // حتى يلتقطه CollectLandedParticles() ويسجّله SceneConnectorFinal.
        if (_gpuSystem != null)
        {
            var gpuLanded = _gpuSystem.CollectLanded(canvasYCM);
            foreach (var gl in gpuLanded)
            {
                var landedParticle = new PaintParticle(
                    gl.position, gl.velocity, gl.color,
                    _dropletRadius, _paint.Density);

                // موضعه أصلاً عند/تحت مستوى اللوحة → dt=0 يضمن إنه
                // يتثبّت Landed فوراً بدون أي حركة إضافية ممكن تغيّر النتيجة
                landedParticle.Update(0f, gravity, airDensity, canvasYCM);

                _activeParticles.Add(landedParticle);
            }
        }
        
        // ✅ فقط لما في طلاء — أصدر جسيمات جديدة
        if (currentPaintHeightCM <= 0.01f) return;  // 0.01 cm = 0.1 mm (كثير جداً)

        _emitAccumulator += deltaTime;
        int holeCount = Mathf.Max(1, _bucket.holes.Count);
        float emitInterval = 1f / (_particleEmitRate * holeCount);

        while (_emitAccumulator >= emitInterval
               && _activeParticles.Count < MAX_ACTIVE_PARTICLES)
        {
            EmitFromAllHoles(bucketWorldPosCM, bucketVelCMps, currentPaintHeightCM);
            _emitAccumulator -= emitInterval;
        }

        if (_emitAccumulator > emitInterval * 5f)
            _emitAccumulator = 0f;
    }



    private void EmitFromAllHoles(Vector3 bucketPos, Vector3 bucketVel,
                              float paintHeightM)
    {
        Debug.Log($"[EMIT-CHECK] holes={_bucket.holes.Count} | paintHeight={paintHeightM:F4}");
        foreach (var hole in _bucket.holes)
        {
            float angleRad = hole.angularPosition * Mathf.Deg2Rad;

            // ✅ كل شيء بالمتر
            float holeRad = (hole.heightFromBottom == 0f) ? 0f : _bucket.innerRadius * 0.9f;
            float bucketH = _bucket.totalHeight;
            float holeHeight = hole.heightFromBottom;

            // موضع الثقب في الفضاء العالمي
            Vector3 holePos = bucketPos + new Vector3(
                holeRad * Mathf.Cos(angleRad),
              holeHeight - bucketH * 0.5f ,
                holeRad * Mathf.Sin(angleRad)
            );

            float h = paintHeightM - holeHeight;
            if (h <= 0f) continue;

            // سرعة خروج تورشيلي بـ m/s
            float vExit = hole.dischargeCoefficient
                          * Mathf.Sqrt(2f * _env.gravity * h);

            // سرعة الجسيمة = سرعة الدلو + خروج للأسفل
            Vector3 vel = bucketVel + new Vector3(0f, -vExit, 0f);

            vel.x += (Random.value - 0.5f) * 0.05f;
            vel.z += (Random.value - 0.5f) * 0.05f;
            SpawnParticle(holePos, vel);
        }
    }

    /// <summary>
    /// استقبال جسيم خارج من SPH وتحويله لـ PaintParticle
    /// </summary>
    public void EmitFromSPH(Vector3 worldPosCM, Vector3 worldVelCMps, float canvasYCM)
    {
        if (_activeParticles.Count >= MAX_ACTIVE_PARTICLES) return;
        SpawnParticle(worldPosCM, worldVelCMps);
    }

    private void SpawnParticle(Vector3 posCM, Vector3 velCMps)
    {
        if (USE_GPU && _gpuSystem != null)
        {
       
                Debug.Log($"[SPAWN-CHECK] gpuSystemNotNull=true");
     
            // كثافة الطلاء بالسنتيمتر³ (kg/m³ ÷ 1e6 = kg/cm³)
            float densityM3 = _paint.Density; // kg/m³

            // إذا كان GPU system متاح، أضف الجسيم هناك بدلاً من CPU
            if (USE_GPU && _gpuSystem != null)
            {
                bool emitted = _gpuSystem.EmitParticle(
                    posCM,
                    velCMps,
                    _paint.colors[0],
                    _dropletRadius,
                    densityM3
                );
                if (emitted)
                {
                    TotalEmittedCount++; return;
                    //if (TotalEmittedCount % 10 == 0)
                    //    Debug.Log($"[PaintEmitter] 🎨 GPU Emitted {TotalEmittedCount} particles | Pos={posCM} | Vel={velCMps}");
                    //return;  // لا تضيف إلى CPU list
                }
            }

            // Fallback إلى CPU rendering إذا فشل GPU
            PaintParticle p;
            if (_particlePool.Count > 0)
            {
                p = _particlePool.Dequeue();
                p.Reset(posCM, velCMps, _paint.colors[0], _dropletRadius, densityM3);
            }
            else
            {
                p = new PaintParticle(posCM, velCMps, _paint.colors[0],
                                      _dropletRadius, densityM3);
            }

            _activeParticles.Add(p);
            TotalEmittedCount++;
        }
    }

    private void ApplyCohesionForces()
    {
        // ✅ أكثر جسيمات + قوة تماسك أكبر
        int limit = Mathf.Min(_activeParticles.Count, 100);
        for (int i = 0; i < limit; i++)
        {
            if (_activeParticles[i].State != ParticleState.Flying) continue;
            for (int j = i + 1; j < limit; j++)
            {
                if (_activeParticles[j].State != ParticleState.Flying) continue;
                Vector3 f = _activeParticles[i].ComputeCohesionForce(
                    _activeParticles[j],
              cohesionRadius: 0.08f,   // بالمتر — أضيق = تيار مضغوط مش انتشار
cohesionStrength: 0.05f
                );
                if (f.sqrMagnitude > 1e-8f)
                {
                    _activeParticles[i].AddExternalForce(f);
                    _activeParticles[j].AddExternalForce(-f);
                }
            }
        }
    }

    private void ReturnDeadToPool()
    {
        for (int i = _activeParticles.Count - 1; i >= 0; i--)
        {
            if (_activeParticles[i].State == ParticleState.Flying) continue;
            if (_particlePool.Count < MAX_POOL_SIZE)
                _particlePool.Enqueue(_activeParticles[i]);
            _activeParticles.RemoveAt(i);
        }
    }

    public List<PaintParticle> CollectLandedParticles()
    {
        var landed = new List<PaintParticle>();
        foreach (var p in _activeParticles)
            if (p.State == ParticleState.Landed) landed.Add(p);
        return landed;
    }

    public void SetEmitRate(float ratePerSecondPerHole)
    => _particleEmitRate = Mathf.Clamp(ratePerSecondPerHole, 1f, 200f);

    public void SetDropletRadius(float radiusCM)
        => _dropletRadius = Mathf.Clamp(radiusCM, 0.05f, 2f);
}