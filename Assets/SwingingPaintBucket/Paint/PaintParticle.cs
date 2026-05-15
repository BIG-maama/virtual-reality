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
    public float   LifeTime      { get; private set; } // زمن الطيران (s)
    public Vector3 LandingPoint  { get; private set; } // نقطة الارتطام باللوحة

    // ===== ثوابت الحساب =====
    private readonly float _paintDensity;
    private readonly float _dragCoeffDroplet = 0.47f; // Cd للكرة
    private readonly float _maxLifeTime = 10f;        // حد أقصى لزمن الطيران (s)

    /// <summary>
    /// إنشاء جزيء طلاء جديد بخصائصه الابتدائية
    /// </summary>
    /// <param name="position">موضع الانطلاق (عند فتحة الثقب)</param>
    /// <param name="velocity">السرعة الابتدائية الكلية = سرعة الدلو + سرعة خروج الطلاء</param>
    /// <param name="color">لون الطلاء</param>
    /// <param name="radius">نصف قطر القطرة (m)</param>
    /// <param name="paintDensity">كثافة الطلاء (kg/m³)</param>
    public PaintParticle(Vector3 position, Vector3 velocity, Color color,
                         float radius, float paintDensity)
    {
        Position      = position;
        Velocity      = velocity;
        ParticleColor = color;
        Radius        = radius;
        _paintDensity = paintDensity;
        State         = ParticleState.Flying;
        LifeTime      = 0f;

        // كتلة القطرة: m = ρ · (4/3)·π·r³
        Mass = paintDensity * (4f / 3f) * Mathf.PI * radius * radius * radius;
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

        // تحقق من انتهاء العمر
        if (LifeTime > _maxLifeTime)
        {
            State = ParticleState.Evaporated;
            return;
        }

        // ===== حساب قوة السحب الهوائي على القطرة =====
        // مساحة المقطع العرضي للقطرة: A_drop = π·r²
        float areaOfDrop = Mathf.PI * Radius * Radius;
        float speedSq    = Velocity.sqrMagnitude;
        float speed      = Mathf.Sqrt(speedSq);

        // Fd = 0.5 × Cd_drop × ρ_air × A_drop × |v|²
        float dragMagnitude = 0f;
        if (speed > 0.001f)
        {
            dragMagnitude = 0.5f * _dragCoeffDroplet * airDensity * areaOfDrop * speedSq;
        }

        // ===== تسارع كل محور =====
        Vector3 acceleration = Vector3.zero;

        if (speed > 0.001f)
        {
            Vector3 velocityDir = Velocity / speed;

            // ẍ = -Fd·(ẋ/|v|) / m  (لا جاذبية أفقية)
            acceleration.x = -dragMagnitude * velocityDir.x / Mass;

            // ÿ = -g - Fd·(ẏ/|v|) / m  (جاذبية + سحب)
            acceleration.y = -gravity - dragMagnitude * velocityDir.y / Mass;

            // z̈ = -Fd·(ż/|v|) / m  (لا جاذبية عمقية)
            acceleration.z = -dragMagnitude * velocityDir.z / Mass;
        }
        else
        {
            // سرعة منخفضة جداً - فقط الجاذبية
            acceleration.y = -gravity;
        }

        // ===== تكامل Explicit Euler لتحديث السرعة والموضع =====
        // θ(t+Δt) = θ(t) + Δt · f(θ(t))
        Velocity += acceleration * deltaTime;
        Position += Velocity * deltaTime;

        // ===== اكتشاف الارتطام باللوحة =====
        if (Position.y <= canvasYPosition)
        {
            Position      = new Vector3(Position.x, canvasYPosition, Position.z);
            LandingPoint  = Position;
            State         = ParticleState.Landed;
        }
    }

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
}
