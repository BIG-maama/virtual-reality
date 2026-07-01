using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// شكل الدلو - يحدد توزيع الطلاء داخله وطريقة حساب المركز الكتلي
/// </summary>
public enum BucketShape
{
    Cylindrical,   // أسطواني: توزيع متجانس، ضغط متساوٍ → تدفق أكثر استقراراً
    Conical,       // مخروطي: يتجمع الطلاء في القاع
    Trapezoidal,   // ذو شكل شبه منحرف
    Spherical      // كروي
}

/// <summary>
/// بيانات الدلو الكاملة - محور المحاكاة الفيزيائية
/// يتغير مركز كتلة الدلو ديناميكياً مع تدفق الطلاء خارجه
/// مما يؤثر على طول البندول الفعلي L(t) وحركته
/// المرجع: الدراسة الفيزيائية - تأثير خصائص الدلو على حركة الطلاء
/// </summary>
[System.Serializable]
public class BucketData
{
    [Header("Bucket Shape and Dimensions")]
    public BucketShape shape = BucketShape.Cylindrical;

    /// <summary>
    /// نصف القطر الداخلي للدلو R (m)
    /// يُستخدم في:
    /// - حساب حجم الطلاء: V = π·R²·h
    /// - كتلة الطلاء: m_paint(t) = ρ_paint · π·R² · h(t)
    /// - وقت التفريغ: T_empty = 2πR²√h₀ / (Cd·A_hole·√(2g))
    /// </summary>
    public float innerRadius = 0.1f;

    /// <summary>
    /// الارتفاع الكلي للدلو H (m)
    /// </summary>
    public float totalHeight = 0.2f;

    [Header("Empty Bucket Weight")]
    /// <summary>
    /// كتلة الدلو الفارغ m_bucket (kg) - ثابتة
    /// المرجع: الدراسة الفيزيائية - حساب الكتلة المتغيرة
    /// m(t) = m_bucket + ρ_paint · π·R² · h(t)
    /// </summary>
    public float emptyMass = 0.5f;

    [Header("Holes")]
    /// <summary>
    /// قائمة الثقوب في الدلو
    /// عدد الثقوب يؤثر على التدفق الكلي: Q_total = n × A × v
    /// </summary>
    public List<HoleData> holes = new List<HoleData>();

    // ===== Derived Physical Properties =====

    /// <summary>
    /// يحسب الكتلة الكلية للدلو في لحظة زمنية t
    /// m(t) = m_bucket + ρ_paint · π·R² · h(t)
    /// المرجع: الدراسة الفيزيائية - حساب الكتلة المتغيرة m(t)
    /// </summary>
    /// <param name="paintHeight">ارتفاع الطلاء المتبقي h(t) (m)</param>
    /// <param name="paintDensity">كثافة الطلاء ρ_paint (kg/m³)</param>
    public float GetTotalMass(float paintHeight, float paintDensity)
    {
        // كتلة الطلاء = ρ · V = ρ · π · R² · h
        float paintMass = paintDensity * Mathf.PI * innerRadius * innerRadius * paintHeight;
        // m(t) = m_bucket + m_paint(t)
        return emptyMass + paintMass;
    }

    /// <summary>
    /// يحسب موضع مركز ثقل الطلاء داخل الدلو (من قاع الدلو)
    /// y_paint(t) = h(t) / 2  (مركز الطلاء في منتصف ارتفاعه)
    /// </summary>
    public float GetPaintCenterOfMass(float paintHeight)
    {
        return paintHeight / 2f; // مركز الطلاء في منتصف ارتفاعه
    }

    /// <summary>
    /// يحسب موضع مركز الثقل المشترك للدلو والطلاء من قاعدة الدلو
    /// y_com(t) = (m_bucket · y_bucket + m_paint(t) · y_paint(t)) / (m_bucket + m_paint(t))
    /// المرجع: الدراسة الفيزيائية - حساب مركز الكتلة المشترك
    /// </summary>
    /// <param name="paintHeight">ارتفاع الطلاء المتبقي h(t)</param>
    /// <param name="paintDensity">كثافة الطلاء ρ_paint</param>
    public float GetSystemCenterOfMass(float paintHeight, float paintDensity)
    {
        float yBucket = totalHeight / 2f; // موضع توازن الدلو = H/2
        float yPaint = GetPaintCenterOfMass(paintHeight);

        float paintMass = paintDensity * Mathf.PI * innerRadius * innerRadius * paintHeight;
        float totalMass = emptyMass + paintMass;

        if (totalMass <= 0f) return yBucket;

        // y_com = (m_bucket·y_bucket + m_paint·y_paint) / m_total
        return (emptyMass * yBucket + paintMass * yPaint) / totalMass;
    }

    /// <summary>
    /// يحسب المسافة من الفوهة (نقطة التعليق) إلى مركز الكتلة المشتركة χ(t)
    /// χ(t) = H - y_com(t)
    /// حيث H = الارتفاع الكلي للدلو (ثابت)
    /// المرجع: الدراسة الفيزيائية - الطول البندول الفعلي
    /// </summary>
    public float GetChiDistance(float paintHeight, float paintDensity)
    {
        float yCom = GetSystemCenterOfMass(paintHeight, paintDensity);
        // χ(t) = H - (m_bucket·H + m_paint·h(t)/2) / (m_bucket + m_paint)
        // الصيغة النهائية: χ(t) = H - [m_bucket·H + m_paint·h(t)/2] / (2·m_total)
        // من الدراسة: χ(t) = H - [m_bucket·H + m_paint(t)·h(t)] / (2·(m_bucket + m_paint(t)))
        float paintMass = paintDensity * Mathf.PI * innerRadius * innerRadius * paintHeight;
        float totalMass = emptyMass + paintMass;
        return totalHeight - (emptyMass * totalHeight + paintMass * paintHeight)
               / (2f * totalMass);
    }

    /// <summary>
    /// يحسب عزم القصور الذاتي للدلو كنقطة كتلة حول نقطة التعليق
    /// I_bucket = m_bucket × L²
    /// المرجع: الدراسة الفيزيائية - القصور الذاتي للبندول
    /// </summary>
    public float GetMomentOfInertia(float ropeLength)
    {
        // I_bucket = m_bucket × L²  (الدلو كنقطة كتلية)
        return emptyMass * ropeLength * ropeLength;
    }

    /// <summary>
    /// يحسب معدل التدفق الكلي من جميع الثقوب
    /// Q_total = Σ(n × A_i × v_i) لكل ثقب
    /// المرجع: الدراسة الفيزيائية - Q_total = n × A × v
    /// </summary>
    public float GetTotalFlowRate(float paintHeight, float gravity)
    {
        float totalFlow = 0f;
        foreach (HoleData hole in holes)
        {
            // Q_i = A_i × v_i لكل ثقب
            totalFlow += hole.GetFlowRate(paintHeight, gravity);
        }
        return totalFlow;
    }

    /// <summary>
    /// يحسب وقت التفريغ الكامل للدلو
    /// T_empty = 2πR²√h₀ / (Cd · A_hole · √(2g))
    /// المرجع: الدراسة الفيزيائية - ارتفاع الطلاء المتبقي h(t)
    /// </summary>
    public float GetEmptyingTime(float initialPaintHeight, float gravity)
    {
        if (holes.Count == 0) return float.MaxValue; // لا يوجد ثقوب

        float totalHoleArea = 0f;
        float avgCd = 0f;
        foreach (HoleData h in holes)
        {
            totalHoleArea += h.GetArea();
            avgCd += h.dischargeCoefficient;
        }
        avgCd /= holes.Count;

        // T_empty = 2πR²√h₀ / (Cd·A_hole·√(2g))
        float numerator = 2f * Mathf.PI * innerRadius * innerRadius * Mathf.Sqrt(initialPaintHeight);
        float denominator = avgCd * totalHoleArea * Mathf.Sqrt(2f * gravity);
        return (denominator > 0f) ? (numerator / denominator) : float.MaxValue;
    }

    /// <summary>
    /// يحسب المساحة المقطعية للدلو عمودياً على اتجاه الحركة
    /// تُستخدم في حساب قوة السحب الهوائي
    /// </summary>
    public float GetCrossSectionArea()
    {
        return Mathf.PI * innerRadius * innerRadius;
    }

    /// <summary>
    /// يحسب الحجم الكلي للدلو
    /// يُستخدم في حساب قوة الطفو الهوائي (أرخميدس)
    /// F_b = ρ_air × V_bucket × g
    /// </summary>
    public float GetVolume()
    {
        switch (shape)
        {
            case BucketShape.Cylindrical:
                return Mathf.PI * innerRadius * innerRadius * totalHeight;
            case BucketShape.Conical:
                return (1f / 3f) * Mathf.PI * innerRadius * innerRadius * totalHeight;
            default:
                return Mathf.PI * innerRadius * innerRadius * totalHeight;
        }
    }
}