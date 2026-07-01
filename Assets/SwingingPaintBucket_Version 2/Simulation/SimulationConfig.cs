using UnityEngine;

/// <summary>
/// تكوين المحاكاة الكاملة - يجمع جميع المدخلات في مكان واحد
/// يُستخدم لحفظ التجارب ومقارنتها
/// المرجع: الدراسة الفيزيائية - المدخلات المطلوبة للمحاكاة
/// </summary>
[System.Serializable]
public class SimulationConfig
{
    [Header("Experiment Name")]
    public string experimentName = "First Experiment";

    [Header("Bucket Properties")]
    public BucketData bucket = new BucketData();

    [Header("Rope Properties")]
    public RopeData rope = new RopeData();

    [Header("Paint Properties")]
    public PaintData paint = new PaintData();

    [Header("Canvas Properties")]
    public CanvasData canvas = new CanvasData();

    [Header("Environment Properties")]
    public EnvironmentData environment = new EnvironmentData();

    [Header("Motion Start")]
    /// <summary>
    /// زاوية البداية θ₀ بالدرجات (زاوية الإطلاق)
    /// </summary>
    [Range(0f, 90f)]
    public float initialAngleDeg = 30f;

    /// <summary>
    /// زاوية φ الابتدائية (تحدد اتجاه التأرجح ومراعاة جهة الرياح)
    /// 0° = تأرجح في اتجاه X
    /// 90° = تأرجح في اتجاه Z
    /// </summary>
    [Range(0f, 360f)]
    public float initialPhiDeg = 0f;

    /// <summary>
    /// السرعة الزاوية الابتدائية θ̇₀ (rad/s)
    /// السرعة الخطية الابتدائية = L × θ̇₀
    /// </summary>
    public float initialAngularVelocity = 0f;

    /// <summary>
    /// عدد مرات التأرجح المطلوبة (0 = حتى التوقف)
    /// </summary>
    public int maxSwingCount = 0;

    /// <summary>
    /// الزمن الأقصى للمحاكاة بالثواني (0 = بدون حد)
    /// </summary>
    public float maxSimulationTime = 60f;
}