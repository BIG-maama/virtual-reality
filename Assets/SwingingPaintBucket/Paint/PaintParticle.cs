using System.Collections.Generic;
using UnityEngine;

public enum ParticleState
{
    Flying,
    Landed,
    Evaporated
}

/// <summary>
/// جزيء طلاء — كل الوحدات بالسنتيمتر (1 Unity unit = 1 cm)
/// 
/// الإصلاحات:
///   ✅ gravity يُستخدم مباشرة (لا ضرب × 100 — كان يجعل الجاذبية 1000× أقوى)
///   ✅ airDensity يُستخدم مباشرة (لا قسمة × 1e-6 — كانت مقاومة الهواء = صفر تقريباً)
///   ✅ Mass يُحسب بالسنتيمتر لاتساق الوحدات
/// 
/// الوحدات المتوقعة من الخارج:
///   gravity      → cm/s²   (980.665)
///   airDensity   → kg/cm³  (من CalculateHumidAirDensity، ≈ 1.2e-6)
///   canvasY      → cm      (موضع اللوحة في Unity)
///   position/vel → cm / cm/s
/// </summary>
public class PaintParticle
{
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Color ParticleColor { get; private set; }
    public float Radius { get; private set; } // cm
    public float Mass { get; private set; } // kg
    public float Density { get; private set; } // kg/cm³
    public ParticleState State { get; private set; }
    public float LifeTime { get; private set; }
    public Vector3 LandingPoint { get; private set; }

    private readonly float _dragCoeff = 0.47f;  // Cd كرة
    private readonly float _maxLifeTime = 15f;     // ثانية
    private float _surfaceTension;
    private Vector3 _pendingForce = Vector3.zero;

    public PaintParticle(Vector3 position, Vector3 velocity, Color color,
                         float radiusCM, float paintDensityKgCm3,
                         float surfaceTension = 0.072f)
    {
        Position = position;
        Velocity = velocity;
        ParticleColor = color;
        Radius = radiusCM;
        Density = paintDensityKgCm3;
        State = ParticleState.Flying;
        LifeTime = 0f;
        _surfaceTension = surfaceTension;

        // m = ρ · (4/3)·π·r³   (كل شيء بالسنتيمتر)
        Mass = paintDensityKgCm3 * (4f / 3f) * Mathf.PI
               * radiusCM * radiusCM * radiusCM;

        // حد أدنى للكتلة لمنع القسمة على صفر
        if (Mass < 1e-12f) Mass = 1e-12f;
    }

    /// <summary>
    /// تحديث الجسيم كل frame.
    /// 
    /// المعاملات (كل الوحدات بالسنتيمتر / ثانية):
    ///   gravity    → cm/s²  (980.665 مباشرة من EnvironmentData.gravity)
    ///   airDensity → kg/cm³ (من CalculateHumidAirDensity)
    ///   canvasY    → cm
    /// </summary>
    public void Update(float deltaTime, float gravity, float airDensity, float canvasYPosition)
    {
        if (State != ParticleState.Flying) return;

        LifeTime += deltaTime;
        if (LifeTime > _maxLifeTime) { State = ParticleState.Evaporated; return; }

        float speed = Velocity.magnitude;

        // ═══ قوة السحب ═══
        float dragMagnitude = 0f;
        if (speed > 0.01f)
        {
            // μ_air ≈ 1.8e-4 g/(cm·s) = 1.8e-4 Poise
            float muAir = 1.983e-5f; // Pa·s بالمتر
            float diameter = Radius * 2f;                          // cm
            float reynolds = airDensity * speed * diameter / muAir;

            float area = Mathf.PI * Radius * Radius;                // cm²

            if (reynolds < 1f)
                // Stokes drag: F = 3π·μ·d·v
                dragMagnitude = 3f * Mathf.PI * muAir * diameter * speed;
            else
                // Newton drag: F = 0.5·Cd·ρ·A·v²
                dragMagnitude = 0.5f * _dragCoeff * airDensity * area * speed * speed;
        }

        // ═══ تسارع ═══
        Vector3 accel = Vector3.zero;
        if (speed > 0.01f)
        {
            Vector3 vDir = Velocity / speed;
            accel.x = -(dragMagnitude * vDir.x) / Mass;
            accel.y = -gravity - (dragMagnitude * vDir.y) / Mass;   // ✅ gravity مباشرة
            accel.z = -(dragMagnitude * vDir.z) / Mass;
        }
        else
        {
            accel.y = -gravity;                                      // ✅ بدون ضرب × 100
        }

        // ═══ قوى خارجية (تماسك) ═══
        if (_pendingForce.sqrMagnitude > 1e-8f)
        {
            accel += _pendingForce / Mass;
            _pendingForce = Vector3.zero;
        }

        // ═══ تكامل ═══
        Velocity += accel * deltaTime;
        Velocity = Vector3.ClampMagnitude(Velocity, 20f); // m/s
        Position += Velocity * deltaTime;

        // ═══ اكتشاف الارتطام ═══
        if (Position.y <= canvasYPosition)
        {
            Position = new Vector3(Position.x, canvasYPosition, Position.z);
            LandingPoint = Position;
            State = ParticleState.Landed;
        }
    }

    public void AddExternalForce(Vector3 force) => _pendingForce += force;

    /// <summary>نصف قطر البقعة بناءً على عدد ويبر</summary>
    public float GetImpactRadius(float weberNumber)
    {
        if (weberNumber < 5f) return Radius * 1.5f;
        else if (weberNumber < 20f) return Radius * 2.5f;
        else if (weberNumber < 100f) return Radius * 4.0f;
        else return Radius * 6.0f;
    }

    public Vector3 GetMomentum() => Mass * Velocity;

    /// <summary>
    /// قوة التماسك بين جسيمين طائرين — تجعلهم يتجمعون كقطرة
    /// </summary>
    public Vector3 ComputeCohesionForce(PaintParticle other,
                                        float cohesionRadius = 8f,   // cm
                                        float cohesionStrength = 0.15f)
    {
        if (other == null || other.State != ParticleState.Flying) return Vector3.zero;

        Vector3 diff = other.Position - this.Position;
        float dist = diff.magnitude;

        if (dist < 0.01f || dist > cohesionRadius) return Vector3.zero;

        float eq = cohesionRadius * 0.4f;
        float mag = dist < eq
            ? -cohesionStrength * (eq - dist) / (dist + 0.01f) * 3f
            : cohesionStrength * (dist - eq) / cohesionRadius;

        return diff.normalized * mag;
    }

    public void Reset(Vector3 position, Vector3 velocity, Color color,
                      float radiusCM, float densityKgCm3)
    {
        Position = position;
        Velocity = velocity;
        ParticleColor = color;
        Radius = radiusCM;
        Density = densityKgCm3;
        State = ParticleState.Flying;
        LifeTime = 0f;
        _pendingForce = Vector3.zero;

        Mass = densityKgCm3 * (4f / 3f) * Mathf.PI * radiusCM * radiusCM * radiusCM;
        if (Mass < 1e-12f) Mass = 1e-12f;
    }
}