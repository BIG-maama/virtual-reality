using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// مُصدر جزيئات الطلاء
/// يولّد جزيئات الطلاء من كل ثقب في الدلو في كل خطوة زمنية
/// السرعة الابتدائية لكل جزيء = سرعة الدلو + سرعة خروج الطلاء من الثقب
/// المرجع: الدراسة الفيزيائية - لحظة الخروج من الفتحة (السرعة الكلية)
/// </summary>
public class PaintEmitter
{
    private readonly BucketData _bucket;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    // مخزن جزيئات الطلاء النشطة
    private readonly List<PaintParticle> _activeParticles = new List<PaintParticle>();

    // إعدادات التوليد
    private float _emitAccumulator = 0f;           // متراكم الزمن للتحكم في معدل التوليد
    private float _particleEmitRate = 20f;          // عدد جزيئات لكل ثانية لكل ثقب
    private float _dropletRadius = 0.002f;          // نصف قطر القطرة (m)

    // ═══ Object Pooling - مهم جداً للأداء ═══
    private Queue<PaintParticle> _particlePool = new Queue<PaintParticle>();
    private const int MAX_POOL_SIZE = 1000;
    private const int MAX_ACTIVE_PARTICLES = 5000; // حد أقصى للأداء

    public IReadOnlyList<PaintParticle> ActiveParticles => _activeParticles;
    public int TotalEmittedCount { get; private set; }

    public PaintEmitter(BucketData bucket, PaintData paint, EnvironmentData env)
    {
        _bucket = bucket;
        _paint = paint;
        _env = env;
    }

    /// <summary>
    /// يُصدر جزيئات الطلاء من ثقوب الدلو ويحرّك الجزيئات الموجودة
    ///
    /// السرعة الكلية لحظة الخروج تتكون من مكونين:
    ///   1. سرعة الخروج (تورتشيلي): v_exit = Cd × √(2gh)  (رأسياً للأسفل)
    ///   2. سرعة الدلو الحالية: v_bucket_x, v_bucket_z   (أفقياً)
    ///
    /// v_total = √(vx² + v_exit² + vz²)
    /// المرجع: الدراسة الفيزيائية - السرعة الكلية للقطرة لحظة الخروج
    /// </summary>
    /// <param name="deltaTime">خطوة الزمن (s)</param>
    /// <param name="bucketWorldPos">موضع الدلو في الفضاء</param>
    /// <param name="bucketVelocity">سرعة الدلو الحالية (m/s)</param>
    /// <param name="currentPaintHeight">ارتفاع الطلاء المتبقي h(t) (m)</param>
    /// <param name="canvasY">ارتفاع اللوحة</param>
    public void UpdateEmission(float deltaTime, Vector3 bucketWorldPos,
        Vector3 bucketVelocity, float currentPaintHeight, float canvasY)
    {
        //float airDensity = _env.CalculateHumidAirDensity(); // ✅ هلق بترجع كغ/سم³
        //float gravity = _env.gravity; // ✅ 980.665 سم/ث²
        //foreach (PaintParticle p in _activeParticles)
        //{
        //    p.Update(deltaTime, gravity, airDensity, canvasY);
        //}
        // لا طلاء → لا إصدار
        if (currentPaintHeight <= 0.001f) return;

        _emitAccumulator += deltaTime;
        float emitInterval = 1f / (_particleEmitRate * Mathf.Max(1, _bucket.holes.Count));

        // إصدار جزيئات جديدة
        while (_emitAccumulator >= emitInterval)
        {
            EmitParticlesFromAllHoles(bucketWorldPos, bucketVelocity,
                                      currentPaintHeight, deltaTime);
            _emitAccumulator -= emitInterval;
        }
        // تطبيق قوى التماسك بين الجسيمات الطائرة
        ApplyCohesionForces();

        // تحريك جميع الجزيئات النشطة
        UpdateAllParticles(deltaTime, canvasY);
        ReturnDeadParticlesToPool();
        // تحريك جميع الجزيئات النشطة
        //  UpdateAllParticles(deltaTime, canvasY);

        // حذف الجزيئات التي وصلت إلى اللوحة أو تبخرت
        //_activeParticles.RemoveAll(p => p.State != ParticleState.Flying);
    }

    private void ApplyCohesionForces()
    {
        // نحد بـ 50 جسيم للأداء
        int limit = Mathf.Min(_activeParticles.Count, 50);

        for (int i = 0; i < limit; i++)
        {
            if (_activeParticles[i].State != ParticleState.Flying) continue;

            for (int j = i + 1; j < limit; j++)
            {
                if (_activeParticles[j].State != ParticleState.Flying) continue;

                Vector3 force = _activeParticles[i].ComputeCohesionForce(
                    _activeParticles[j]
                );

                if (force.sqrMagnitude > 0.0001f)
                {
                    _activeParticles[i].AddExternalForce(force);
                    _activeParticles[j].AddExternalForce(-force); // نيوتن الثالث
                }
            }
        }
    }
    private void UpdateAllParticles(float deltaTime, float canvasY)
    {
        float airDensity = _env.CalculateHumidAirDensity();
        foreach (PaintParticle p in _activeParticles)
        {
            p.Update(deltaTime, _env.gravity, airDensity, canvasY);
        }
    }

    /// <summary>
    /// يُعيد الجزيئات التي ارتطمت باللوحة في هذه الخطوة
    /// </summary>
    public List<PaintParticle> CollectLandedParticles()
    {
        var landed = new List<PaintParticle>();
        foreach (PaintParticle p in _activeParticles)
        {
            if (p.State == ParticleState.Landed)
                landed.Add(p);
        }
        return landed;
    }

    public void SetEmitRate(float ratePerSecondPerHole)
    {
        _particleEmitRate = Mathf.Max(1f, ratePerSecondPerHole);
    }

    public void SetDropletRadius(float radius)
    {
        _dropletRadius = Mathf.Clamp(radius, 0.0005f, 0.01f);
    }/// <summary>
     /// يستقبل جسيم خارج من SPH ويحوله لـ PaintParticle طائر
     /// بدل توليد الجسيمات من تورشيلي مباشرة
     /// </summary>
     /// <summary>
     /// يستقبل جسيم خارج من SPH ويحوله لـ PaintParticle طائر
     /// النسخة المحسّنة - تستقبل بيانات كاملة من SPH
     /// </summary>
    public void EmitFromSPH(Vector3 worldPos, Vector3 worldVelocity, float canvasY)
    {
        // التحقق من الحد الأقصى
        if (_activeParticles.Count >= MAX_ACTIVE_PARTICLES) return;

        PaintParticle particle;

        // استخدام من الـ Pool إذا متاح
        if (_particlePool.Count > 0)
        {
            particle = _particlePool.Dequeue();
            particle.Reset(worldPos, worldVelocity, _paint.colors[0], _dropletRadius, _paint.Density);
        }
        else
        {
            // إنشاء جديد إذا الـ Pool فاضي
            particle = new PaintParticle(worldPos, worldVelocity,
                _paint.colors[0], _dropletRadius, _paint.Density);
        }

        _activeParticles.Add(particle);
        TotalEmittedCount++;
    }

    private void ReturnDeadParticlesToPool()
    {
        for (int i = _activeParticles.Count - 1; i >= 0; i--)
        {
            if (_activeParticles[i].State != ParticleState.Flying)
            {
                if (_particlePool.Count < MAX_POOL_SIZE)
                    _particlePool.Enqueue(_activeParticles[i]);
                _activeParticles.RemoveAt(i);
            }
        }
    }

    // Define the missing EmitParticlesFromAllHoles method

    private void EmitParticlesFromAllHoles(Vector3 bucketWorldPos, Vector3 bucketVelocity,
        float currentPaintHeight, float deltaTime)
    {
        foreach (var hole in _bucket.holes)
        {
            // Calculate the position of the hole in world space
            float holeAngle = hole.angularPosition * Mathf.Deg2Rad;
            float holeRadius = _bucket.innerRadius * 0.9f; // Assume holes are near the inner wall
            Vector3 holePosition = bucketWorldPos + new Vector3(
                holeRadius * Mathf.Cos(holeAngle),
                hole.heightFromBottom - _bucket.totalHeight * 0.5f,
                holeRadius * Mathf.Sin(holeAngle)
            );

            // Calculate the velocity of the emitted particle
            float exitVelocity = Mathf.Sqrt(2f * _env.gravity * currentPaintHeight) * hole.dischargeCoefficient;
            Vector3 holeVelocity = bucketVelocity + new Vector3(0f, -exitVelocity, 0f);

            // Emit the particle
            EmitFromSPH(holePosition, holeVelocity, 0f);
        }
    }
}