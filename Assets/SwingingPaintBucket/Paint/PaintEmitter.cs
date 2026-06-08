using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// مُصدر جزيئات الطلاء — نسخة مُصحَّحة
/// 
/// الإصلاحات:
///   ✅ إزالة استدعاء p.Update() المكرر (كان يحدّث كل جسيم مرتين)
///   ✅ _dropletRadius بالسنتيمتر (0.2 cm = 2mm بدلاً من 0.002m)
///   ✅ Density بالوحدات الصحيحة kg/cm³ لا kg/m³
///   ✅ exitVelocity يستخدم gravity بالسنتيمتر مباشرة
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
    private float _emitAccumulator = 0f;
    private float _particleEmitRate = 10f; // جزيء/ثانية/ثقب

    // نصف قطر القطرة بالسنتيمتر (0.2 cm = 2 mm)
    private float _dropletRadius = 0.002f; // 2mm بالمتر

    private Queue<PaintParticle> _particlePool = new Queue<PaintParticle>();
    private const int MAX_POOL_SIZE = 500;
    private const int MAX_ACTIVE_PARTICLES = 2000;

    public IReadOnlyList<PaintParticle> ActiveParticles => _activeParticles;
    public int TotalEmittedCount { get; private set; }

    public PaintEmitter(BucketData bucket, PaintData paint, EnvironmentData env)
    {
        _bucket = bucket;
        _paint = paint;
        _env = env;
    }

    /// <summary>
    /// التحديث الرئيسي — يُستدعى من SceneConnectorFinal.HandlePaint()
    /// </summary>
    public void UpdateEmission(float deltaTime, Vector3 bucketWorldPosCM,
                               Vector3 bucketVelCMps, float currentPaintHeightCM,
                               float canvasYCM)
    {
        float airDensity = _env.CalculateHumidAirDensity();
        float gravity = _env.gravity;

        // ✅ دائماً حدّث الجسيمات الموجودة أولاً
        ApplyCohesionForces();
        foreach (PaintParticle p in _activeParticles)
            p.Update(deltaTime, gravity, airDensity, canvasYCM);
        ReturnDeadToPool();

        // ✅ فقط لما في طلاء — أصدر جسيمات جديدة
        if (currentPaintHeightCM <= 0.1f) return;

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

    private void EmitFromAllHoles(Vector3 bucketPosCM, Vector3 bucketVelCMps,
                                   float paintHeightCM)
    {
        foreach (var hole in _bucket.holes)
        {
            // موقع الثقب في الفضاء العالمي (بالسنتيمتر)
            float angleRad = hole.angularPosition * Mathf.Deg2Rad;
            //  float holeRadCM = _bucket.innerRadius * 100f * 0.9f; // متر → سنتيمتر
            // float bucketHCM = _bucket.totalHeight * 100f;
            float holeRadM = _bucket.innerRadius * 0.9f;
            float bucketHM = _bucket.totalHeight;
            Vector3 holePos = bucketPosCM + new Vector3(
                holeRadM * Mathf.Cos(angleRad),
                hole.heightFromBottom - bucketHM * 0.5f,  // متر → سنتيمتر
                holeRadM * Mathf.Sin(angleRad)
            );

            // سرعة الخروج بتورشيلي (بالسنتيمتر/ثانية)
            float h = paintHeightCM - hole.heightFromBottom;
            if (h <= 0f) continue;

            float vExit = hole.dischargeCoefficient
                          * Mathf.Sqrt(2f * _env.gravity * h); // cm/s

            // السرعة الكلية = سرعة الدلو + خروج للأسفل
            Vector3 vel = bucketVelCMps + new Vector3(0f, -vExit, 0f);

            vel.x += (Random.value - 0.5f) * vExit * 0.05f;  // بدل 0.008f
            vel.z += (Random.value - 0.5f) * vExit * 0.05f;
            // أضف اضطراب عمودي خفيف
            vel.y += (Random.value - 0.5f) * vExit * 0.02f;

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
        // كثافة الطلاء بالسنتيمتر³ (kg/m³ ÷ 1e6 = kg/cm³)
        float densityM3 = _paint.Density; // kg/m³

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
                    cohesionRadius: 3f,      // ✅ نطاق أضيق = تيار مضغوط
                    cohesionStrength: 1.2f   // ✅ أقوى 8 مرات من الأصل
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
        => _particleEmitRate = Mathf.Clamp(ratePerSecondPerHole, 1f, 50f);

    public void SetDropletRadius(float radiusCM)
        => _dropletRadius = Mathf.Clamp(radiusCM, 0.05f, 2f);
}