using UnityEngine;

/// <summary>
/// بيانات البيئة المحيطة بالمحاكاة
/// تحتوي على جميع متغيرات البيئة التي تؤثر على حركة الدلو والطلاء
/// </summary>
[System.Serializable]
public class EnvironmentData
{
    [Header("Gravity")]
    /// <summary>
    /// تسارع الجاذبية الأرضية g0 = 9.80665 m/s²
    /// المرجع: الدراسة الفيزيائية - قانون تسارع الجاذبية الأرضية
    /// </summary>
    public float gravity = 9.80665f;

    [Header("Air Resistance")]
    /// <summary>
    /// كثافة الهواء الجاف عند درجة الغرفة = 1.204 kg/m³
    /// المرجع: الدراسة الفيزيائية - قانون السحب العام
    /// </summary>
    public float airDensity = 1.204f;

    /// <summary>
    /// معامل السحب للدلو الكروي Cd ≈ 0.47
    /// المرجع: الدراسة الفيزيائية - قانون قوة السحب العام Fd = 0.5 * Cd * ρ * A * v²
    /// </summary>
    public float dragCoefficient = 0.47f;

    [Header("Humidity")]
    /// <summary>
    /// الرطوبة النسبية (0-100%)
    /// تؤثر على لزوجة الهواء وكثافته وفق معادلة CIPM-2007
    /// ρ_air = [0.34848·P − 0.009·H·exp(0.061·T)] / (273.15 + T)
    /// </summary>
    [Range(0f, 100f)]
    public float humidity = 50f;

    [Header("Temperature")]
    /// <summary>
    /// درجة الحرارة بالسيليوس
    /// تؤثر على: لزوجة الطلاء (معادلة Andrade) وطول الحبل (التمدد الحراري)
    /// </summary>
    public float temperature = 20f;

    [Header("Atmospheric Pressure")]
    /// <summary>
    /// الضغط الجوي بـ hPa
    /// قيمة مرجعية: 1013.25 hPa
    /// يستخدم في معادلة CIPM-2007 لحساب كثافة الهواء الرطب
    /// </summary>
    public float atmosphericPressure = 1013.25f;

    [Header("Friction")]
    /// <summary>
    /// معامل الاحتكاك عند نقطة التعليق
    /// يضاف إلى معامل التخميد الكلي
    /// </summary>
    [Range(0f, 1f)]
    public float pivotFriction = 0.01f;

    [Header("Wind")]
    /// <summary>
    /// سرعة الرياح بـ m/s
    /// تولد قوة: F_wind = 0.5 * ρ_air * Cd * A * V_wind²
    /// </summary>
    public float windSpeed = 0f;

    /// <summary>
    /// اتجاه الرياح (زاوية من محور X)
    /// يؤثر على: مسار أطول/أقصر (موازي) أو ملتوي ومتعرج (عمودي)
    /// </summary>
    public float windAngle = 0f;

    /// <summary>
    /// يحسب كثافة الهواء الرطب وفق معادلة CIPM-2007 المبسطة
    /// ρ_air = [0.34848·P − 0.009·H·exp(0.061·T)] / (273.15 + T)
    /// </summary>
    public float CalculateHumidAirDensity()
    {
        // معادلة CIPM-2007 المبسطة
        // P = الضغط الجوي بـ hPa, H = الرطوبة (0-100), T = درجة الحرارة بالسيليوس
        float numerator = 0.34848f * atmosphericPressure
                         - 0.009f * humidity * Mathf.Exp(0.061f * temperature);
        float denominator = 273.15f + temperature;
        return numerator / denominator;
    }

    /// <summary>
    /// يحسب تأثير الرطوبة على لزوجة الهواء
    /// الرطوبة تسبب فروقا تصل إلى +1.6% في لزوجة الهواء الجاف
    /// μ_dry = 1.983 × 10⁻⁵ Pa·s عند 20 درجة
    /// </summary>
    public float CalculateAirViscosity()
    {
        float muDry = 1.983e-5f; // لزوجة الهواء الجاف عند 20 درجة
        float humidityFactor = 1f + (humidity / 100f) * 0.016f;
        return muDry * humidityFactor;
    }

    /// <summary>
    /// يحسب قوة الرياح على الدلو
    /// F_wind = 0.5 * ρ_air * Cd * A * V_wind²
    /// المرجع: الدراسة الفيزيائية - تأثير الرياح على البندول
    /// </summary>
    public Vector3 CalculateWindForce(float bucketCrossSectionArea)
    {
        float rhoAir = CalculateHumidAirDensity();
        float forceMagnitude = 0.5f * rhoAir * dragCoefficient
                               * bucketCrossSectionArea * windSpeed * windSpeed;

        // تحويل اتجاه الرياح إلى متجه
        float windRad = windAngle * Mathf.Deg2Rad;
        Vector3 windDirection = new Vector3(Mathf.Cos(windRad), 0f, Mathf.Sin(windRad));
        return windDirection * forceMagnitude;
    }
}