using UnityEngine;

/// <summary>
/// أنواع مواد الحبل المتاحة
/// كل نوع له خصائص فيزيائية مختلفة (كثافة، معامل يونغ، تمدد حراري)
/// </summary>
public enum RopeMaterial
{
    Cotton,     // قطن:   ρ=1530 kg/m³, E=9 GPa,   α=10×10⁻⁶ K⁻¹
    Polyester,  // بوليستر: ρ=1390 kg/m³, E=10 GPa,  α=60×10⁻⁶ K⁻¹
    Nylon,      // نايلون: ρ=1145 kg/m³, E=3.5 GPa, α=75×10⁻⁶ K⁻¹
    SteelWire   // فولاذ: ρ=7800 kg/m³, E=200 GPa, α=12×10⁻⁶ K⁻¹
}

/// <summary>
/// بيانات الحبل وخصائصه الفيزيائية
/// يعتمد الحبل على: الطول، نوع المادة، درجة المرونة، ونقطة التعليق
/// المرجع: الدراسة الفيزيائية - خصائص الحبل وتأثيره على حركة الدلو
/// </summary>
[System.Serializable]
public class RopeData
{
    [Header("Rope properties")]
    /// <summary>
    /// الطول الابتدائي للحبل عند درجة الحرارة المرجعية (20°C) بالمتر
    /// </summary>
    public float initialLength = 1.0f;

    /// <summary>
    /// نصف قطر الحبل بالمتر
    /// يُستخدم لحساب: مساحة المقطع A = π·r²
    /// وكتلة الحبل: m_rope = ρ·A·L
    /// </summary>
    public float radius = 0.005f;

    /// <summary>
    /// نوع مادة الحبل
    /// </summary>
    public RopeMaterial material = RopeMaterial.Cotton;

    [Header("Pivot Point")]
    /// <summary>
    /// موضع نقطة التعليق في الفضاء ثلاثي الأبعاد
    /// هذه هي نقطة الدوران (Pivot) في نظام البندول
    /// </summary>
    public Vector3 pivotPoint = Vector3.zero;

    // ===== ثوابت المواد =====
    // الكثافة (kg/m³) - المرجع: جدول المواد في الدراسة الفيزيائية
    private static readonly float[] MaterialDensities = { 1530f, 1390f, 1145f, 7800f };

    // معامل يونغ (GPa) - يقيس صلابة المادة ومقاومتها للتشوه
    // قانون: E = (F × L₀) / (A × ΔL)
    private static readonly float[] YoungModuli = { 9e9f, 10e9f, 3.5e9f, 200e9f };

    // معامل التمدد الحراري الطولي (×10⁻⁶ K⁻¹)
    // قانون التمدد الطولي: ΔL = α · L₀ · ΔT
    private static readonly float[] ThermalExpansionCoeffs = { 10e-6f, 60e-6f, 75e-6f, 12e-6f };

    // ===== الخصائص المشتقة =====

    /// <summary>
    /// كثافة الحبل حسب نوع المادة (kg/m³)
    /// </summary>
    public float Density => MaterialDensities[(int)material];

    /// <summary>
    /// معامل يونغ للحبل حسب نوع المادة (Pa)
    /// </summary>
    public float YoungModulus => YoungModuli[(int)material];

    /// <summary>
    /// معامل التمدد الحراري الطولي (K⁻¹)
    /// </summary>
    public float ThermalExpansion => ThermalExpansionCoeffs[(int)material];

    /// <summary>
    /// مساحة المقطع العرضي للحبل (m²)
    /// A = π × r²
    /// </summary>
    public float CrossSectionArea => Mathf.PI * radius * radius;

    /// <summary>
    /// يحسب الطول الفعلي للحبل عند درجة حرارة معينة
    /// قانون التمدد الطولي الحراري: L(T) = L₀ × (1 + α × ΔT)
    /// المرجع: الدراسة الفيزيائية - تمدد الحبل الحراري
    /// </summary>
    /// <param name="temperature">درجة الحرارة الحالية بالسيليوس</param>
    /// <param name="referenceTemperature">درجة الحرارة المرجعية (20°C)</param>
    public float GetCurrentLength(float temperature, float referenceTemperature = 20f)
    {
        float deltaT = temperature - referenceTemperature;
        // ΔL = α · L₀ · ΔT
        return initialLength * (1f + ThermalExpansion * deltaT);
    }

    /// <summary>
    /// يحسب كتلة الحبل
    /// m_rope = ρ · V = ρ · A · L
    /// المرجع: الدراسة الفيزيائية - الكتلة والقصور الذاتي للحبل
    /// </summary>
    public float GetMass(float currentLength)
    {
        return Density * CrossSectionArea * currentLength;
    }

    /// <summary>
    /// يحسب ثابت المرونة للحبل (قانون هوك)
    /// k = E · A / L₀
    /// حيث F = -k · x (قانون هوك)
    /// المرجع: الدراسة الفيزيائية - المرونة وتأثيرها على الحركة
    /// </summary>
    public float GetSpringConstant(float currentLength)
    {
        // k = E·A / L₀
        return YoungModulus * CrossSectionArea / currentLength;
    }

    /// <summary>
    /// يحسب عزم القصور الذاتي للحبل حول نقطة التعليق
    /// I_rope = (1/3) × m_rope × L²
    /// يُنمذج الحبل كقضيب رفيع موزع بانتظام
    /// المرجع: الدراسة الفيزيائية - الكتلة والقصور الذاتي
    /// </summary>
    public float GetMomentOfInertia(float currentLength)
    {
        float ropeM = GetMass(currentLength);
        // I_rope = (1/3) × m_rope × L²
        return (1f / 3f) * ropeM * currentLength * currentLength;
    }

    /// <summary>
    /// يحسب تأثير الحبل على معامل التخميد الإضافي
    /// k = (k_air · L · D) / (3 · m) + C/m
    /// المرجع: الدراسة الفيزيائية - تأثير الحبل على التخميد الإضافي
    /// </summary>
    public float GetRopeDampingContribution(float ropeMass, float airResistanceC, float currentLength)
    {
        // الحد الأول يمثل تأثير الحبل، الحد الثاني تأثير الدلو
        float kAir = 1.2e-4f; // معامل مقاومة الهواء للحبل
        float D = 2f * radius; // قطر الحبل
        return (kAir * currentLength * D) / (3f * ropeMass) + airResistanceC / ropeMass;
    }

    /// <summary>
    /// يحسب نسبة الاستطالة عند الانقطاع
    /// ε = (L - L₀) / L₀ × 100
    /// المرجع: الدراسة الفيزيائية - الاستطالة عند الانقطاع
    /// </summary>
    public float GetBreakingElongationPercent(float currentLength)
    {
        return ((currentLength - initialLength) / initialLength) * 100f;
    }
}
