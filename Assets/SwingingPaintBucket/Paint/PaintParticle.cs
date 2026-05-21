using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// حالة جزيء الطلاء في المحاكاة
/// </summary>
public enum ParticleState
{
    Flying,     // في الهواء - خاضع للجاذبية ومقاومة الهواء
    Landed,     // وصل إلى اللوحة
    Evaporated  // تبخر (لم يصل إلى أي سطح)
}

/// <summary>
/// جزيء طلاء واحد يُحاكى بشكل مستقل في الفضاء ثلاثي الأبعاد
/// يخضع لقوانين الحركة المقذوفة + مقاومة الهواء
/// كل جزيء يمثل قطرة طلاء حقيقية بكتلتها وحجمها وسرعتها الخاصة
/// المرجع: الدراسة الفيزيائية - الجزء الثالث: مسار القطرة في الهواء
/// </summary>
public class PaintParticle
{
    // ===== الخصائص الأساسية =====
    public Vector3 Position      { get; private set; }
    public Vector3 Velocity      { get; private set; }
    public Color   ParticleColor { get; private set; }
    public float   Radius        { get; private set; } // نصف قطر القطرة (m)
    public float   Mass          { get; private set; } // كتلة القطرة (kg)
    public ParticleState State   { get; private set; }
    public float Density { get; private set; }
    public float   LifeTime      { get; private set; } // زمن الطيران (s)
    public Vector3 LandingPoint  { get; private set; } // نقطة الارتطام باللوحة

    // ===== ثوابت الحساب =====
    private readonly float _paintDensity;
    private readonly float _dragCoeffDroplet = 0.47f; // Cd للكرة
    private readonly float _maxLifeTime = 10f;        // حد أقصى لزمن الطيران (s)
    private float _surfaceTension;
    /// <summary>
    /// إنشاء جزيء طلاء جديد بخصائصه الابتدائية
    /// </summary>
    /// <param name="position">موضع الانطلاق (عند فتحة الثقب)</param>
    /// <param name="velocity">السرعة الابتدائية الكلية = سرعة الدلو + سرعة خروج الطلاء</param>
    /// <param name="color">لون الطلاء</param>
    /// <param name="radius">نصف قطر القطرة (m)</param>
    /// <param name="paintDensity">كثافة الطلاء (kg/m³)</param>
    public PaintParticle(Vector3 position, Vector3 velocity, Color color,
                     float radius, float paintDensity, float surfaceTension = 0.072f)
    {
        Position      = position;
        Velocity      = velocity;
        ParticleColor = color;
        Radius        = radius;
        _paintDensity = paintDensity;
        Density       = paintDensity; // Initialize Density
        State         = ParticleState.Flying;
        LifeTime      = 0f;

        // كتلة القطرة: m = ρ · (4/3)·π·r³
        Mass = paintDensity * (4f / 3f) * Mathf.PI * radius * radius * radius; _surfaceTension = surfaceTension;

    }

    /// <summary>
    /// يُحدّث موضع وسرعة جزيء الطلاء في كل خطوة زمنية
    /// يطبق معادلات الحركة في المحاور الثلاثة X, Y, Z
    ///
    /// المحور X (أفقي):
    ///   m · ẍ = -Fd · (ẋ / |v_drop|)
    ///
    /// المحور Y (رأسي):
    ///   m · ÿ = -m·g - Fd · (ẏ / |v_drop|)
    ///
    /// المحور Z (عمق):
    ///   m · z̈ = -Fd · (ż / |v_drop|)
    ///
    /// قوة السحب: Fd = 0.5 × Cd_drop × ρ_air × A_drop × |v_drop|²
    /// المرجع: الدراسة الفيزيائية - الجزء الثالث: مسار القطرة في الهواء
    /// </summary>
    /// <param name="deltaTime">خطوة الزمن (s)</param>
    /// <param name="gravity">تسارع الجاذبية (m/s²)</param>
    /// <param name="airDensity">كثافة الهواء (kg/m³)</param>
    /// <param name="canvasYPosition">ارتفاع اللوحة لاكتشاف الارتطام</param>
    public void Update(float deltaTime, float gravity, float airDensity, float canvasYPosition)
    {
        if (State != ParticleState.Flying) return;

        LifeTime += deltaTime;

        if (LifeTime > _maxLifeTime)
        {
            State = ParticleState.Evaporated;
            return;
        }

        // ✅ تحويل gravity للسنتيمتر: 9.80665 → 980.665 سم/ث²
        float gravityCM = gravity * 100f;

        // ✅ تحويل airDensity للسنتيمتر: 1.225 → 0.001225 كغ/سم³
        float airDensityCM = airDensity * 1e-6f;

        // ===== حساب قوة السحب =====
        float areaOfDrop = Mathf.PI * Radius * Radius; // Radius بالمتر = 0.002m = 0.2cm
                                                       // ❌ wait - Radius بالمتر!

        // ✅ أحسن: خلي Radius بالسنتيمتر
        // RadiusCM = Radius * 100f
        float radiusCM = Radius * 100f;
        float areaOfDropCM = Mathf.PI * radiusCM * radiusCM;

        float speed = Velocity.magnitude; // Velocity بالسنتيمتر/ثانية
        float dragMagnitude = 0f;

        if (speed > 0.001f)
        {
            // ✅ Reynolds بالسنتيمتر
            float reynolds = (airDensityCM * speed * radiusCM * 2f) / 0.00018f; // μ_air = 1.8×10⁻⁴ poise

            if (reynolds < 1f)
            {
                dragMagnitude = 6f * Mathf.PI * 0.00018f * radiusCM * speed;
            }
            else
            {
                dragMagnitude = 0.5f * _dragCoeffDroplet * airDensityCM * areaOfDropCM * speed * speed;
            }
        }

        // ===== تسارع =====
        Vector3 acceleration = Vector3.zero;
        if (speed > 0.001f)
        {
            Vector3 velocityDir = Velocity / speed;
            acceleration.x = -dragMagnitude * velocityDir.x / Mass;
            acceleration.y = -gravityCM - dragMagnitude * velocityDir.y / Mass;
            acceleration.z = -dragMagnitude * velocityDir.z / Mass;
        }
        else
        {
            acceleration.y = -gravityCM;
        }

        if (_pendingForce.sqrMagnitude > 0.0001f)
        {
            acceleration += _pendingForce / Mass;
            _pendingForce = Vector3.zero;
        }

        Velocity += acceleration * deltaTime;
        Velocity = Vector3.ClampMagnitude(Velocity, 50f);

        Position += Velocity * deltaTime;

        // ===== اكتشاف الارتطام باللوحة =====
        if (Position.y <= canvasYPosition)
        {
            Position = new Vector3(Position.x, canvasYPosition, Position.z);
            LandingPoint = Position;
            State = ParticleState.Landed;
        }
    }
    private void ApplySurfaceTension(List<PaintParticle> neighbors)
    {
        Vector3 force = Vector3.zero;

        foreach (var neighbor in neighbors)
        {
            if (neighbor == this) continue;

            Vector3 diff = neighbor.Position - this.Position;
            float dist = diff.magnitude;

            if (dist < 0.05f && dist > 0.001f) // 5cm نصف قطر تأثير
            {
                float cohesion = _surfaceTension * (0.05f - dist) * (0.05f - dist);
                force += diff.normalized * cohesion;
            }
        }

        // تطبيق القوة
        Velocity += force / Mass * Time.fixedDeltaTime;
    }
    /// <summary>
    /// يحسب قوة التماسك مع جسيم آخر قريب
    /// تجعل الجزيئات الطائرة تتجمع كقطرة بدل ما تتفرق
    /// 
    /// قوة التماسك: F = k_cohesion × (1/r² - 1/r_eq²) × r̂
    /// إذا r < r_eq → تنافر (لمنع التداخل)
    /// إذا r > r_eq → تجاذب (للتجمع كقطرة)
    /// </summary>
    /// <param name="other">الجسيم الآخر</param>
    /// <param name="cohesionRadius">نصف قطر التأثير (m)</param>
    /// <param name="cohesionStrength">قوة التماسك</param>
    public Vector3 ComputeCohesionForce(PaintParticle other,
                                         float cohesionRadius = 0.08f,
                                         float cohesionStrength = 0.15f)
    {
        if (other == null || other.State != ParticleState.Flying) return Vector3.zero;

        Vector3 diff = other.Position - this.Position;
        float dist = diff.magnitude;

        if (dist < 0.001f || dist > cohesionRadius) return Vector3.zero;

        float equilibriumDist = cohesionRadius * 0.4f;

        float forceMag;
        if (dist < equilibriumDist)
        {
            // تنافر قوي لمنع التداخل
            forceMag = -cohesionStrength *
                       (equilibriumDist - dist) / (dist + 0.001f) * 3f;
        }
        else
        {
            // تجاذب خفيف للتجمع
            forceMag = cohesionStrength *
                       (dist - equilibriumDist) / cohesionRadius;
        }

        return diff.normalized * forceMag;
    }

    /// <summary>
    /// يضيف قوة خارجية للجسيم (من التماسك مع الجسيمات الأخرى)
    /// يُستدعى من PaintEmitter قبل Update
    /// </summary>
    public void AddExternalForce(Vector3 force)
    {
        // نخزن القوة ونطبقها في Update القادم
        _pendingForce += force;
    }

    // ═══ متغير خاص للقوة المعلقة ═══
    private Vector3 _pendingForce = Vector3.zero;
    /// <summary>
    /// يحسب نصف قطر البقعة الناتجة على اللوحة بعد الارتطام
    /// يعتمد على عدد ويبر We = ρ·v²·d/σ
    /// We < 5:   بقعة نظيفة = r_drop × 1.5
    /// 5–20:     بقعة مع تموج = r_drop × 2.5
    /// 20–100:   تناثر + حلقة = r_drop × 4.0
    /// > 100:    تفكك كامل = r_drop × 6.0
    /// المرجع: الدراسة الفيزيائية - تشكل المسار اللوني وعدد ويبر
    /// </summary>
    /// <param name="weberNumber">عدد ويبر لحظة الاصطدام</param>
    public float GetImpactRadius(float weberNumber)
    {
        if      (weberNumber < 5f)   return Radius * 1.5f;   // انتشار هادئ
        else if (weberNumber < 20f)  return Radius * 2.5f;   // انتشار مع تموج
        else if (weberNumber < 100f) return Radius * 4.0f;   // حلقة تناثر
        else                          return Radius * 6.0f;   // تفكك كامل
    }

    /// <summary>
    /// يحسب الزخم الكلي للجزيء: p = m × v
    /// المرجع: الدراسة الفيزيائية - الزخم p = m × v
    /// </summary>
    public Vector3 GetMomentum() => Mass * Velocity;

    // Add a Reset method to the PaintParticle class

    public void Reset(Vector3 position, Vector3 velocity, Color color, float radius, float density)
    {
        Position = position;
        Velocity = velocity;
        ParticleColor = color;
        Radius = radius;
        Density = density;
        State = ParticleState.Flying;
        LifeTime = 0f;
    }
}
