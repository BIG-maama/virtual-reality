using UnityEngine;

/// <summary>
/// أنواع الطلاء المتاحة - لكل نوع خصائص فيزيائية مختلفة
/// </summary>
public enum PaintType
{
    WaterBased, // طلاء مائي:  ρ≈1100 kg/m³, σ=40 mN/m, τ_dry=8-15 دقيقة
    OilBased,   // طلاء زيتي:  ρ≈1050 kg/m³, σ=32 mN/m, τ_dry=60-180 دقيقة
    Acrylic,    // أكريليك:    ρ≈1150 kg/m³, σ=36 mN/m, τ_dry=5-12 دقيقة
    Rubber      // مطاطي:      ρ≈1200 kg/m³, σ=34 mN/m, τ_dry=10-20 دقيقة
}

/// <summary>
/// بيانات الطلاء وخصائصه الفيزيائية
/// الطلاء سائل غير نيوتوني (Non-Newtonian) يخضع لـ Cross Model
/// لزوجته تتغير مع معدل القص وترتفع مع انخفاض الحرارة
/// المرجع: الدراسة الفيزيائية - ميكانيك السوائل والسلوك الريولوجي
/// </summary>
[System.Serializable]
public class PaintData
{
    [Header("Paint Type")]
    public PaintType paintType = PaintType.WaterBased;

    [Header("Quantity")]
    /// <summary>
    /// الارتفاع الابتدائي للطلاء في الدلو h₀ (m)
    /// يُستخدم في: h(t) = h₀ · (1 - t/T_empty)²
    /// </summary>
    public float initialHeight = 0.15f;

    [Header("Colors")]
    /// <summary>
    /// قائمة ألوان الطلاء المستخدمة
    /// يدعم أكثر من لون يُمزج وفق معادلة: C_result = α·C_new + (1-α)·C_existing
    /// </summary>
    public Color[] colors = { Color.red ,Color.green};

    // ===== Material Constants =====
    // الكثافة (kg/m³) لكل نوع طلاء
    private static readonly float[] Densities = { 1100f, 1050f, 1150f, 1200f };
    // التوتر السطحي (N/m) عند 20°C
    private static readonly float[] SurfaceTension = { 0.040f, 0.032f, 0.036f, 0.034f };
    // اللزوجة عند السكون μ₀ (Pa·s)
    private static readonly float[] ZeroShearVisc = { 0.10f, 0.80f, 0.25f, 0.30f };
    // اللزوجة عند قص لانهائي μ∞ (Pa·s)
    private static readonly float[] InfShearVisc = { 0.001f, 0.01f, 0.002f, 0.003f };
    // زمن ثابت K للـ Cross Model
    private static readonly float[] CrossModelK = { 1.0f, 5.0f, 2.0f, 3.0f };
    // مؤشر التدفق n للطلاء (0 < n < 1 → تخفيف بالقص)
    private static readonly float[] FlowIndex = { 0.6f, 0.7f, 0.65f, 0.68f };
    // زمن الجفاف τ_dry بالثواني عند 20°C
    private static readonly float[] DryingTimes = { 720f, 7200f, 540f, 900f };
    // معامل نمو البقعة b (حسب نوع السطح)
    private static readonly float[] SpreadFactor = { 0.194f, 0.091f, 0.194f, 0.091f };

    // ===== Derived Properties =====
    public float Density => Densities[(int)paintType];
    public float SigmaRef => SurfaceTension[(int)paintType]; // التوتر السطحي المرجعي
    public float Mu0 => ZeroShearVisc[(int)paintType];
    public float MuInf => InfShearVisc[(int)paintType];
    public float CrossK => CrossModelK[(int)paintType];
    public float FlowN => FlowIndex[(int)paintType];
    public float TauDryRef => DryingTimes[(int)paintType];
    public float B => SpreadFactor[(int)paintType];

    // ===== Viscosity Laws =====

    /// <summary>
    /// يحسب اللزوجة الديناميكية بـ Cross Model
    /// μ(γ̇) = μ∞ + (μ₀ - μ∞) / (1 + (K·γ̇)ⁿ)
    /// حيث γ̇ = معدل القص اللحظي = θ̇(t)·L / d_orifice
    /// المرجع: الدراسة الفيزيائية - النموذج الرياضي الصحيح Cross Model
    /// </summary>
    /// <param name="shearRate">معدل القص (1/s)</param>
    public float GetDynamicViscosity(float shearRate)
    {
        float shearAbs = Mathf.Abs(shearRate);
        float denom = 1f + Mathf.Pow(CrossK * shearAbs, FlowN);
        // Cross Model: μ(γ̇) = μ∞ + (μ₀ - μ∞) / (1 + (K·γ̇)ⁿ)
        return MuInf + (Mu0 - MuInf) / denom;
    }

    /// <summary>
    /// يحسب لزوجة الطلاء عند درجة حرارة T بمعادلة Andrade
    /// μ(T) = A · exp(B/T)
    /// اللزوجة تنخفض مع ارتفاع الحرارة
    /// المرجع: الدراسة الفيزيائية - تأثير درجة الحرارة على لزوجة الطلاء
    /// </summary>
    /// <param name="temperatureCelsius">درجة الحرارة بالسيليوس</param>
    public float GetViscosityAtTemperature(float temperatureCelsius)
    {
        float T = temperatureCelsius + 273.15f; // تحويل إلى كلفن
        // ثوابت Andrade التقريبية للطلاء (A, B)
        float A = Mu0 * Mathf.Exp(-3000f / 293.15f);
        float B = 3000f; // ثابت طاقة التنشيط
        // μ(T) = A · e^(B/T)
        return A * Mathf.Exp(B / T);
    }

    /// <summary>
    /// يحسب التوتر السطحي عند درجة حرارة T
    /// σ(T) = σ_ref × [1 − K_σ × (T − T_ref)]
    /// التوتر السطحي يقل مع ارتفاع الحرارة → انتشار أكثر
    /// المرجع: الدراسة الفيزيائية - التوتر السطحي وقاعدة Eotvos
    /// </summary>
    public float GetSurfaceTension(float temperatureCelsius)
    {
        float kSigma = 0.002f; // معامل انخفاض التوتر السطحي مع الحرارة
        float tRef = 20f;
        // σ(T) = σ_ref × [1 − K_σ × (T − T_ref)]
        return SigmaRef * (1f - kSigma * (temperatureCelsius - tRef));
    }

    /// <summary>
    /// يحسب ارتفاع الطلاء المتبقي في الدلو عند الزمن t
    /// h(t) = h₀ · (1 − t/T_empty)²
    /// المرجع: الدراسة الفيزيائية - ارتفاع الطلاء المتبقي في الدلو
    /// </summary>
    /// <param name="t">الزمن المنقضي منذ بداية التفريغ (s)</param>
    /// <param name="tEmpty">الزمن اللازم للإفراغ الكامل T_empty (s)</param>
    public float GetCurrentHeight(float t, float tEmpty)
    {
        if (t >= tEmpty) return 0f;
        float ratio = 1f - t / tEmpty;
        // h(t) = h₀ · (1 − t/T_empty)²
        return initialHeight * ratio * ratio;
    }

    /// <summary>
    /// يحسب معامل الجفاف τ_dry بناءً على درجة الحرارة ونوع الطلاء
    /// τ_dry = μ(T) × h²_film / (D_solvent × ρ_paint)
    /// </summary>
    /// <param name="temperature">درجة الحرارة بالسيليوس</param>
    /// <param name="filmThickness">سماكة فيلم الطلاء (m)</param>
    public float GetDryingTimeConstant(float temperature, float filmThickness = 0.0001f)
    {
        float mu = GetViscosityAtTemperature(temperature);
        float dSolvent = 1e-9f; // معامل انتشار المذيب (m²/s)
        // τ_dry = μ(T) × h²_film / (D_solvent × ρ_paint)
        return mu * filmThickness * filmThickness / (dSolvent * Density);
    }

    /// <summary>
    /// يحسب درجة جفاف نقطة على اللوحة عند الزمن t
    /// F_dry(t) = 1 − exp(−t / τ_dry)
    /// F_dry = 0 → طازج تماماً، F_dry = 1 → جاف تماماً
    /// المرجع: الدراسة الفيزيائية - معادلة الجفاف
    /// </summary>
    public float GetDrynessFactor(float timeSincePainted, float tauDry)
    {
        // F_dry(t) = 1 - e^(-t/τ_dry)
        return 1f - Mathf.Exp(-timeSincePainted / tauDry);
    }

    /// <summary>
    /// يحسب لون الطلاء الناتج عند مزجه مع لون موجود على اللوحة
    /// C_mixed = F_dry × C_old + (1 − F_dry) × C_new
    /// المرجع: الدراسة الفيزيائية - مزج الألوان بناءً على الجفاف
    /// </summary>
    /// <param name="existingColor">اللون الموجود على اللوحة</param>
    /// <param name="newColor">اللون الجديد القادم من الدلو</param>
    /// <param name="drynessFactor">درجة جفاف اللون الموجود (0=طازج, 1=جاف)</param>
    /// <param name="alpha">معامل الكثافة والسُمك</param>
    public Color BlendColors(Color existingColor, Color newColor, float drynessFactor, float alpha)
    {
        // C_result = α · C_new + (1 − α) · C_existing  (معادلة المزج العامة)
        // مع تأثير الجفاف: C_mixed = F_dry · C_old + (1 − F_dry) · C_new
        Color blended = drynessFactor * existingColor + (1f - drynessFactor) * newColor;
        // تطبيق معامل الكثافة alpha
        return Color.Lerp(existingColor, blended, alpha);
    }

    /// <summary>
    /// يحسب نصف قطر البقعة على اللوحة بعد مرور زمن t
    /// r ~ a · t^b  حيث b معامل انتشار السطح
    /// المرجع: الدراسة الفيزيائية - تأثير الرطوبة على انتشار الطلاء
    /// </summary>
    /// <param name="t">الزمن بعد الاصطدام (s)</param>
    /// <param name="initialDropRadius">نصف قطر القطرة عند الاصطدام (m)</param>
    public float GetSpreadRadius(float t, float initialDropRadius)
    {
        if (t <= 0f) return initialDropRadius;
        float a = initialDropRadius; // معامل الانتشار الابتدائي
        // r(t) = a · t^b
        return a * Mathf.Pow(t, B);
    }

    /// <summary>
    /// يحسب عدد ويبر لتحديد نمط اصطدام قطرة الطلاء باللوحة
    /// We = ρ · v² · d / σ
    /// We < 5: بقعة دائرية نظيفة
    /// 5-20:  بقعة مع تموج
    /// 20-100: حلقة تناثر + نقاط
    /// > 100: تفكك كامل
    /// المرجع: الدراسة الفيزيائية - تشكل المسار اللوني على اللوحة
    /// </summary>
    /// <param name="dropVelocity">سرعة القطرة عند الاصطدام (m/s)</param>
    /// <param name="dropDiameter">قطر القطرة (m)</param>
    /// <param name="temperature">درجة الحرارة (°C)</param>
    public float GetWeberNumber(float dropVelocity, float dropDiameter, float temperature)
    {
        float sigma = GetSurfaceTension(temperature);
        // We = ρ · v² · d / σ
        return Density * dropVelocity * dropVelocity * dropDiameter / sigma;
    }
}