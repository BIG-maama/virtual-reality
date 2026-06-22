using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// مُصدر جزيئات الطلاء — كل الوحدات بالمتر
/// 
/// الوحدات:
///   المواضع والسرعات → m و m/s
///   gravity           → m/s² (9.80665)
///   airDensity        → kg/m³ (≈ 1.204)
///   نصف قطر القطرة   → m (0.002 = 2mm)
///   الكثافة           → kg/m³
/// </summary>
public class PaintEmitter
{
    private readonly BucketData _bucket;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    private readonly List<PaintParticle> _activeParticles = new List<PaintParticle>();
    private float _emitAccumulator = 0f;
    private float _particleEmitRate = 80f; // جزيء/ثانية/ثقب

    // نصف قطر القطرة بالمتر (0.002m = 2mm)
    private float _dropletRadius = 0.002f;

    private Queue<PaintParticle> _particlePool = new Queue<PaintParticle>();
    private const int MAX_POOL_SIZE = 500;
    private const int MAX_ACTIVE_PARTICLES = 1000;

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
    /// كل المعاملات بالمتر
    /// </summary>
    public void UpdateEmission(float deltaTime, Vector3 bucketWorldPosM,
                               Vector3 bucketVelMs, float currentPaintHeightM,
                               float canvasYM)
    {
        float airDensity = _env.CalculateHumidAirDensity();
        float gravity = _env.gravity;

        ApplyCohesionForces();
        foreach (PaintParticle p in _activeParticles)
            p.Update(deltaTime, gravity, airDensity, canvasYM);

        // ← احذف ReturnDeadToPool() من هون

        if (currentPaintHeightM <= 0.001f) return;

        _emitAccumulator += deltaTime;
        int holeCount = Mathf.Max(1, _bucket.holes.Count);
        float emitInterval = 1f / (_particleEmitRate * holeCount);

        while (_emitAccumulator >= emitInterval
               && _activeParticles.Count < MAX_ACTIVE_PARTICLES)
        {
            EmitFromAllHoles(bucketWorldPosM, bucketVelMs, currentPaintHeightM);
            _emitAccumulator -= emitInterval;
        }

        if (_emitAccumulator > emitInterval * 5f)
            _emitAccumulator = 0f;
    }
    public void CleanupDeadParticles()
    {
        ReturnDeadToPool();
    }

    private void EmitFromAllHoles(Vector3 bucketPosM, Vector3 bucketVelMs,
                                  float paintHeightM)
    {
        foreach (var hole in _bucket.holes)
        {
            float h = paintHeightM - hole.heightFromBottom;
            if (h <= 0f) continue;

            // موضع الثقب — قاع الدلو بالفضاء العالمي
            Vector3 holePos = new Vector3(
                bucketPosM.x,
                bucketPosM.y - _bucket.totalHeight * 0.5f,
                bucketPosM.z
            );

            // قانون تورشيلي
            float vExit = hole.dischargeCoefficient
                          * Mathf.Sqrt(2f * 9.80665f * h);

            // السرعة للأسفل فقط
            Vector3 vel = new Vector3(0f, -vExit, 0f);

            SpawnParticle(holePos, vel);
        }
    }
    /// <summary>
    /// استقبال جسيم خارج من SPH — كل شيء بالمتر
    /// </summary>
    public void EmitFromSPH(Vector3 worldPosM, Vector3 worldVelMs, float canvasYM)
    {
        if (_activeParticles.Count >= MAX_ACTIVE_PARTICLES) return;
        SpawnParticle(worldPosM, worldVelMs);
    }

    private void SpawnParticle(Vector3 posM, Vector3 velMs)
    {
        float radiusM = _dropletRadius;  // m
        float densityKgM3 = _paint.Density; // kg/m³

        PaintParticle p;
        if (_particlePool.Count > 0)
        {
            p = _particlePool.Dequeue();
            p.Reset(posM, velMs, _paint.colors[0], radiusM, densityKgM3);
        }
        else
        {
            p = new PaintParticle(posM, velMs, _paint.colors[0], radiusM, densityKgM3);
        }
        _activeParticles.Add(p);
        TotalEmittedCount++;
    }

    private void ApplyCohesionForces()
    {
        int limit = Mathf.Min(_activeParticles.Count, 100);
        for (int i = 0; i < limit; i++)
        {
            if (_activeParticles[i].State != ParticleState.Flying) continue;
            for (int j = i + 1; j < limit; j++)
            {
                if (_activeParticles[j].State != ParticleState.Flying) continue;
                Vector3 f = _activeParticles[i].ComputeCohesionForce(
                    _activeParticles[j],
                    cohesionRadius: 0.03f,   // 3cm بالمتر
                    cohesionStrength: 1.2f
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

    // نصف القطر بالمتر (0.0005 = 0.5mm, 0.02 = 2cm)
    public void SetDropletRadius(float radiusM)
        => _dropletRadius = Mathf.Clamp(radiusM, 0.0005f, 0.02f);

    public void SetEmitRate(float ratePerSecondPerHole)
        => _particleEmitRate = Mathf.Clamp(ratePerSecondPerHole, 1f, 200f);
}