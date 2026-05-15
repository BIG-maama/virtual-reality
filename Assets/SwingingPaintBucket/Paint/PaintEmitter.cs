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
    private readonly BucketData      _bucket;
    private readonly PaintData       _paint;
    private readonly EnvironmentData _env;

    // مخزن جزيئات الطلاء النشطة
    private readonly List<PaintParticle> _activeParticles = new List<PaintParticle>();

    // إعدادات التوليد
    private float _emitAccumulator = 0f;           // متراكم الزمن للتحكم في معدل التوليد
    private float _particleEmitRate = 20f;          // عدد جزيئات لكل ثانية لكل ثقب
    private float _dropletRadius = 0.002f;          // نصف قطر القطرة (m)

    public IReadOnlyList<PaintParticle> ActiveParticles => _activeParticles;
    public int TotalEmittedCount { get; private set; }

    public PaintEmitter(BucketData bucket, PaintData paint, EnvironmentData env)
    {
        _bucket = bucket;
        _paint  = paint;
        _env    = env;
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

        // تحريك جميع الجزيئات النشطة
        UpdateAllParticles(deltaTime, canvasY);

        // حذف الجزيئات التي وصلت إلى اللوحة أو تبخرت
        _activeParticles.RemoveAll(p => p.State != ParticleState.Flying);
    }

    /// <summary>
    /// يُصدر جزيئات من جميع ثقوب الدلو
    /// </summary>
    private void EmitParticlesFromAllHoles(Vector3 bucketPos, Vector3 bucketVelocity,
                                            float paintHeight, float deltaTime)
    {
        foreach (HoleData hole in _bucket.holes)
        {
            // حساب سرعة الخروج بقانون توريتشيلي
            // v_exit = Cd × √(2·g·h)
            float exitSpeed = hole.GetExitVelocity(paintHeight, _env.gravity);
            if (exitSpeed <= 0f) continue;

            // موضع الثقب في الفضاء
            Vector3 holeWorldPos = hole.GetWorldPosition(bucketPos, _bucket.innerRadius,
                                                          _bucket.totalHeight);

            // ===== بناء السرعة الكلية للقطرة =====
            // المكون الأفقي: من سرعة الدلو
            float v0x = bucketVelocity.x;  // سرعة الدلو في اتجاه X
            float v0z = bucketVelocity.z;  // سرعة الدلو في اتجاه Z
            // المكون الرأسي: من قانون توريتشيلي (سالب → للأسفل)
            float v0y = -exitSpeed;

            // إضافة تشتت عشوائي صغير لمحاكاة الاضطراب الطبيعي
            float scatter = 0.05f;
            v0x += Random.Range(-scatter, scatter) * exitSpeed;
            v0z += Random.Range(-scatter, scatter) * exitSpeed;

            Vector3 initialVelocity = new Vector3(v0x, v0y, v0z);

            // اختيار لون عشوائي من قائمة الألوان
            Color color = _paint.colors[Random.Range(0, _paint.colors.Length)];

            // إنشاء الجزيء
            var particle = new PaintParticle(
                holeWorldPos,
                initialVelocity,
                color,
                _dropletRadius,
                _paint.Density
            );

            _activeParticles.Add(particle);
            TotalEmittedCount++;
        }
    }

    /// <summary>
    /// يحرّك جميع الجزيئات النشطة في الهواء
    /// يطبق قوانين الحركة المقذوفة على كل جزيء
    /// </summary>
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
    }
}
