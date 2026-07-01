using UnityEngine;

/// <summary>
/// شكل الثقب في الدلو - يحدد مساحة الفتحة وتدفق الطلاء
/// </summary>
public enum HoleShape
{
    Circular,   // دائري: A = π·r²     → تدفق أكثر انتظاماً
    Square,     // مربع:  A = a²        → زوايا حادة تعطي تدفقاً أقل انتظاماً
    Rectangular // مستطيل: A = l × w
}

/// <summary>
/// بيانات ثقب واحد في الدلو
/// يمثل نقطة خروج الطلاء بخصائصها الفيزيائية الكاملة
/// </summary>
[System.Serializable]
public class HoleData
{
    [Header("Hole Shape")]
    public HoleShape shape = HoleShape.Circular;

    [Header("Hole Dimensions")]
    /// <summary>
    /// نصف قطر الثقب الدائري (m)
    /// </summary>
    public float radius = 0.003f;

    /// <summary>
    /// طول الثقب المستطيل (m)
    /// </summary>
    public float length = 0.003f;

    /// <summary>
    /// عرض الثقب المستطيل (m)
    /// </summary>
    public float width = 0.003f;

    [Header("Hole Position")]
    /// <summary>
    /// ارتفاع الثقب من قاع الدلو بالمتر
    /// يؤثر على سرعة خروج الطلاء بقانون توريتشيلي:
    /// v = √(2gh) حيث h = ارتفاع الطلاء فوق الثقب
    /// الثقوب السفلية → h أكبر → سرعة أكبر
    /// الثقوب العلوية → h أصغر → سرعة أقل
    /// </summary>
    public float heightFromBottom = 0f;

    /// <summary>
    /// الزاوية الأفقية لموضع الثقب حول محيط الدلو (درجات)
    /// تحدد موضع الثقب على محيط الدلو
    /// </summary>
    public float angularPosition = 0f;

    /// <summary>
    /// معامل التصريف Cd (بين 0.6 و 0.85 للثقوب العملية)
    /// يعوض عن تأثيرات الانقباض والاحتكاك عند الثقب
    /// </summary>
    [Range(0.6f, 0.85f)]
    public float dischargeCoefficient = 0.7f;

    // ===== Hole Area Calculation =====

    /// <summary>
    /// يحسب مساحة مقطع الثقب بناءً على شكله
    /// للدائرة: A = π·r²
    /// للمربع: A = a²
    /// للمستطيل: A = l × w
    /// المرجع: الدراسة الفيزيائية - شكل الثقوب ومساحتها
    /// </summary>
    public float GetArea()
    {
        switch (shape)
        {
            case HoleShape.Circular:
                return Mathf.PI * radius * radius;      // A = π·r²
            case HoleShape.Square:
                return radius * 2f * radius * 2f;       // A = (2r)² = a²
            case HoleShape.Rectangular:
                return length * width;                   // A = l × w
            default:
                return Mathf.PI * radius * radius;
        }
    }

    /// <summary>
    /// يحسب سرعة خروج الطلاء من هذا الثقب بقانون توريتشيلي
    /// v_exit = Cd × √(2 × g × h)
    /// حيث h = ارتفاع الطلاء المتبقي فوق الثقب
    /// المرجع: الدراسة الفيزيائية - قانون توريتشيلي لسرعة الخروج
    /// </summary>
    /// <param name="paintHeightInBucket">ارتفاع الطلاء المتبقي في الدلو (m)</param>
    /// <param name="gravity">تسارع الجاذبية (m/s²)</param>
    public float GetExitVelocity(float paintHeightInBucket, float gravity)
    {
        // h = الارتفاع الفعلي للطلاء فوق هذا الثقب
        float h = paintHeightInBucket - heightFromBottom;
        if (h <= 0f) return 0f; // الثقب مكشوف (لا طلاء فوقه)

        // v = Cd × √(2·g·h) - قانون توريتشيلي المعدَّل بمعامل التصريف
        return dischargeCoefficient * Mathf.Sqrt(2f * gravity * h);
    }

    /// <summary>
    /// يحسب معدل التدفق الحجمي من هذا الثقب
    /// Q = A × v
    /// المرجع: الدراسة الفيزيائية - قانون معدل التدفق Q = A × v
    /// </summary>
    public float GetFlowRate(float paintHeightInBucket, float gravity)
    {
        float exitVelocity = GetExitVelocity(paintHeightInBucket, gravity);
        return GetArea() * exitVelocity; // Q = A × v
    }

    /// <summary>
    /// يحسب موضع الثقب في الفضاء نسبةً لمركز الدلو
    /// يُستخدم لتحديد نقطة انطلاق جزيئات الطلاء
    /// </summary>
    public Vector3 GetWorldPosition(Vector3 bucketCenter, float bucketRadius, float bucketHeight)
    {
        float angRad = angularPosition * Mathf.Deg2Rad;
        float x = bucketCenter.x + bucketRadius * Mathf.Cos(angRad);
        float y = bucketCenter.y - bucketHeight / 2f + heightFromBottom;
        float z = bucketCenter.z + bucketRadius * Mathf.Sin(angRad);
        return new Vector3(x, y, z);
    }
}