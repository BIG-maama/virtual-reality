using UnityEngine;
using System.Collections.Generic;


public class PaintEmitter
{
    private readonly BucketData _bucket;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;
    private SpatialHashGrid _grid = new SpatialHashGrid(0.06f);
    private readonly List<PaintParticle> _activeParticles = new List<PaintParticle>();
    private float _emitAccumulator = 0f;
    private float _particleEmitRate = 120f; 
    private float _cohesionStrength = 1.2f;
    public void SetCohesionStrength(float s) => _cohesionStrength = Mathf.Clamp(s, 0.1f, 5f);

    // نصف قطر القطرة بالمتر (0.002m = 2mm)
    private float _dropletRadius = 0.002f;

    private Queue<PaintParticle> _particlePool = new Queue<PaintParticle>();
    private const int MAX_POOL_SIZE = 5000;
    private const int MAX_ACTIVE_PARTICLES = 10000;

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

        ApplySPHForces();
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

    private void ApplySPHForces()
    {
        int limit = Mathf.Min(_activeParticles.Count, 300);
        var pos = new Vector3[limit];
        for (int i = 0; i < limit; i++) pos[i] = _activeParticles[i].Position;
        _grid.Build(pos);

        float h = 0.06f, k = 50f, sigma = 0.04f, restDensity = 800f;

        for (int i = 0; i < limit; i++)
        {
            if (_activeParticles[i].State != ParticleState.Flying) continue;
            var neighbors = _grid.GetNeighbors(pos[i]);

            float density = 0f;
            foreach (int j in neighbors)
            {
                float d = Vector3.Distance(pos[i], pos[j]);
                if (d < h) { float dh = h * h - d * d; density += dh * dh * dh; }
            }
            density = Mathf.Max(density * 0.001f, 1f);
            float pressure = k * (density - restDensity);

            Vector3 force = Vector3.zero;
            foreach (int j in neighbors)
            {
                if (i == j) continue;
                Vector3 diff = pos[i] - pos[j];
                float dist = diff.magnitude;
                if (dist < 0.001f || dist > h) continue;
                float dh = h - dist;
                force += diff.normalized * (-dh * dh * pressure / density);
                if (dist < h * 0.5f)
                    force += diff.normalized * (-sigma * (h * 0.5f - dist));
            }
            _activeParticles[i].AddExternalForce(force * 0.1f);
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
      => _particleEmitRate = Mathf.Clamp(ratePerSecondPerHole, 1f, 2000f);
}