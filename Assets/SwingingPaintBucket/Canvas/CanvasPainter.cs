using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// نوع سطح اللوحة - يؤثر على معامل انتشار الطلاء b
/// المرجع: الدراسة الفيزيائية - تأثير الرطوبة على انتشار الطلاء (جدول b)
/// </summary>
public enum CanvasSurface
{
    DryUnprimed,  // سطح جاف (غير مجمَّد):  b = 0.091 → امتصاص سريع، انتشار بطيء
    WetPrimed5,   // سطح رطب (مجمَّد 5 دق): b = 0.194 → انتشار متوسط
    WetPrimed30   // سطح مجمَّد 30 دقيقة:   b = 0.471 → انتشار واسع، أنماط متفرعة
}

/// <summary>
/// بيانات اللوحة وأبعادها وخصائص سطحها
/// </summary>
[System.Serializable]
public class CanvasData
{
    [Header("Canvas Dimensions")]
    public float width = 1.0f;  // عرض اللوحة (m)
    public float height = 1.0f;  // ارتفاع اللوحة (m)

    [Header("Canvas Position")]
    public Vector3 position = Vector3.zero;  // مركز اللوحة

    [Header("Canvas Tilt")]
    /// <summary>
    /// زاوية ميلان اللوحة بالدرجات (0=أفقية، 90=رأسية)
    /// الميلان يؤثر على: توزيع الطلاء وانزلاقه
    /// </summary>
    [Range(0f, 90f)]
    public float tiltAngle = 0f;

    [Header("Surface Type")]
    public CanvasSurface surface = CanvasSurface.DryUnprimed;

    /// <summary>
    /// ارتفاع سطح اللوحة (Y في Unity) مع مراعاة الميلان
    /// </summary>
    public float SurfaceY => position.y + Mathf.Sin(tiltAngle * Mathf.Deg2Rad) * height * 0.5f;
}

/// <summary>
/// نقطة طلاء على اللوحة
/// كل نقطة تمثل موضع ارتطام جزيء طلاء واحد مع بياناته الكاملة
/// </summary>
public class PaintPoint
{
    public Vector2 Position { get; set; }  // موضع النقطة على اللوحة (0-1)
    public Color Color { get; set; }  // اللون المحسوب بعد المزج
    public float Radius { get; set; }  // نصف قطر البقعة
    public float TimeSincePainted { get; set; }  // الزمن منذ الرسم (s) - للجفاف
    public float WeberNumber { get; set; }  // عدد ويبر لحظة الاصطدام
    public float DrynessFactor => _paint.GetDrynessFactor(TimeSincePainted, _tauDry);

    private readonly PaintData _paint;
    private readonly float _tauDry;

    public PaintPoint(Vector2 pos, Color color, float radius,
                      float weberNumber, PaintData paint, float tauDry)
    {
        Position = pos;
        Color = color;
        Radius = radius;
        WeberNumber = weberNumber;
        TimeSincePainted = 0f;
        _paint = paint;
        _tauDry = tauDry;
    }

    public void UpdateTime(float deltaTime) => TimeSincePainted += deltaTime;
}

/// <summary>
/// رسّام اللوحة - يتعامل مع إضافة نقاط الطلاء ومزج الألوان وتتبع المسارات
/// يطبق: VOF (Volume of Fluid) لتتبع الطلاء على الشبكة
/// ومعادلة مزج الألوان: C_result = α·C_new + (1-α)·C_existing
/// المرجع: الدراسة الفيزيائية - رسم الطلاء بطريقة VOF
/// </summary>
public class CanvasPainter
{
    private readonly CanvasData _canvas;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    // قائمة جميع نقاط الطلاء على اللوحة
    private readonly List<PaintPoint> _paintPoints = new List<PaintPoint>();

    // شبكة VOF لتتبع ملء كل خلية (0=هواء، 1=طلاء كامل)
    private readonly float[,] _vofGrid;
    // شبكة الألوان المقابلة
    private readonly Color[,] _colorGrid;

    private const int GridResolution = 256; // دقة الشبكة (256×256 خلية)

    // إحصاءات اللوحة
    public int TotalPathCount => _paintPoints.Count;
    public float PaintedAreaM2 { get; private set; }    // مساحة انتشار اللون (m²)
    public IReadOnlyList<PaintPoint> PaintPoints => _paintPoints;
  
    public CanvasPainter(CanvasData canvas, PaintData paint, EnvironmentData env)
    {
        _canvas = canvas;
        _paint = paint;
        _env = env;
        _vofGrid = new float[GridResolution, GridResolution];
        _colorGrid = new Color[GridResolution, GridResolution];

        // تهيئة الشبكة (0 = لا طلاء)
        for (int i = 0; i < GridResolution; i++)
            for (int j = 0; j < GridResolution; j++)
                _colorGrid[i, j] = Color.white;
    }

    /// <summary>
    /// يُسجّل ارتطام جزيء طلاء باللوحة ويحسب البقعة الناتجة
    /// </summary>
    /// <param name="particle">جزيء الطلاء الذي وصل إلى اللوحة</param>
    /// <param name="currentTemperature">درجة الحرارة الحالية</param>
    public void RegisterImpact(PaintParticle particle, float currentTemperature)
    {
        // تحويل موضع العالم إلى إحداثيات اللوحة (0-1)
        Vector2 canvasUV = WorldToCanvasUV(particle.LandingPoint);

        // التحقق أن النقطة داخل اللوحة
        if (canvasUV.x < 0f || canvasUV.x > 1f ||
            canvasUV.y < 0f || canvasUV.y > 1f) return;

        // حساب عدد ويبر لتحديد نمط الاصطدام
        float weberNum = _paint.GetWeberNumber(
            particle.Velocity.magnitude,
            particle.Radius * 2f,
            currentTemperature
        );

        // نصف قطر البقعة على اللوحة
        float impactRadius = particle.GetImpactRadius(weberNum);

        // معامل الجفاف τ_dry
        float tauDry = _paint.GetDryingTimeConstant(currentTemperature, impactRadius * 0.1f);

        // الحصول على اللون الموجود عند هذه النقطة ومزجه مع اللون الجديد
        Color existingColor = SampleColorAt(canvasUV);
        float dryness = SampleDrynessAt(canvasUV);
        // C_mixed = F_dry × C_old + (1 − F_dry) × C_new
        Color blendedColor = _paint.BlendColors(existingColor, particle.ParticleColor,
                                                   dryness, 0.85f);

        // إنشاء نقطة الطلاء
        var point = new PaintPoint(canvasUV, blendedColor, impactRadius,
                                   weberNum, _paint, tauDry);
        _paintPoints.Add(point);

        // تحديث شبكة VOF
        UpdateVOFGrid(canvasUV, impactRadius, blendedColor);

        // تحديث مساحة الانتشار
        UpdatePaintedArea(impactRadius);
    }

    /// <summary>
    /// يُحدّث حالة جفاف جميع نقاط الطلاء
    /// </summary>
    public void Update(float deltaTime)
    {
        foreach (PaintPoint p in _paintPoints)
            p.UpdateTime(deltaTime);
    }

    /// <summary>
    /// يُحدّث شبكة VOF عند ارتطام قطرة
    /// ∂F/∂t + (u·∇)F = 0
    /// كل خلية تحمل قيمة F بين 0 و 1
    /// المرجع: الدراسة الفيزيائية - طريقة VOF لرسم الطلاء
    /// </summary>
    private void UpdateVOFGrid(Vector2 centerUV, float radius, Color newColor)
    {
        // تحويل نصف القطر من المتر إلى خلايا الشبكة
        float radiusCells = (radius / _canvas.width) * GridResolution;

        int cx = Mathf.RoundToInt(centerUV.x * (GridResolution - 1));
        int cy = Mathf.RoundToInt(centerUV.y * (GridResolution - 1));
        int r = Mathf.CeilToInt(radiusCells);

        for (int i = Mathf.Max(0, cx - r); i <= Mathf.Min(GridResolution - 1, cx + r); i++)
        {
            for (int j = Mathf.Max(0, cy - r); j <= Mathf.Min(GridResolution - 1, cy + r); j++)
            {
                float dist = Mathf.Sqrt((i - cx) * (i - cx) + (j - cy) * (j - cy));
                if (dist <= radiusCells)
                {
                    // معامل الملء: 1 في المركز، ينخفض نحو الحافة
                    float fillFactor = 1f - (dist / radiusCells);
                    // F جديد = F قديم + fillFactor (مع الحد بـ 1)
                    _vofGrid[i, j] = Mathf.Min(1f, _vofGrid[i, j] + fillFactor * 0.8f);

                    // مزج الألوان: C_result = α·C_new + (1-α)·C_existing
                    float alpha = fillFactor * 0.85f;
                    _colorGrid[i, j] = Color.Lerp(_colorGrid[i, j], newColor, alpha);
                }
            }
        }
    }

    /// <summary>
    /// يُحدّث حساب مساحة الانتشار الكلية
    /// </summary>
    private void UpdatePaintedArea(float dropRadius)
    {
        // مساحة بقعة واحدة: A = π·r²
        PaintedAreaM2 += Mathf.PI * dropRadius * dropRadius;
    }

    /// <summary>
    /// يحصل على اللون الموجود عند إحداثي UV
    /// </summary>
    private Color SampleColorAt(Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (GridResolution - 1)), 0, GridResolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (GridResolution - 1)), 0, GridResolution - 1);
        return _colorGrid[x, y];
    }

    /// <summary>
    /// يحصل على درجة جفاف النقطة عند UV
    /// </summary>
    private float SampleDrynessAt(Vector2 uv)
    {
        // البحث عن أقرب نقطة طلاء
        float minDist = float.MaxValue;
        float dryness = 1f; // جاف افتراضياً
        foreach (PaintPoint p in _paintPoints)
        {
            float d = Vector2.Distance(uv, p.Position);
            if (d < minDist && d < p.Radius / _canvas.width)
            {
                minDist = d;
                dryness = p.DrynessFactor;
            }
        }
        return dryness;
    }

    /// <summary>
    /// يحوّل موضع نقطة في الفضاء ثلاثي الأبعاد إلى إحداثيات UV على اللوحة (0-1)
    /// </summary>
    private Vector2 WorldToCanvasUV(Vector3 worldPoint)
    {
        float u = (worldPoint.x - _canvas.position.x + _canvas.width * 0.5f) / _canvas.width;
        float v = (worldPoint.z - _canvas.position.z + _canvas.height * 0.5f) / _canvas.height;
        return new Vector2(u, v);
    }

    /// <summary>
    /// يولّد Texture2D من شبكة VOF لعرض اللوحة النهائية
    /// </summary>
    public Texture2D GenerateCanvasTexture()
    {
        var tex = new Texture2D(GridResolution, GridResolution);
        for (int i = 0; i < GridResolution; i++)
            for (int j = 0; j < GridResolution; j++)
                tex.SetPixel(i, j, _colorGrid[i, j]);
        tex.Apply();
        return tex;
    }/// <summary>
/// يُعيد مواضع جزيئات الطلاء المستقرة على اللوحة
/// يُستخدم من SimulationManager لعرضها بـ GPU Instancing
/// </summary>
public List<Vector3> GetLandedPositions()
{
    var result = new List<Vector3>();
    foreach (var p in _paintPoints)
    {
        // تحويل UV (0-1) إلى موضع عالمي على اللوحة
        float worldX = _canvas.position.x + (p.Position.x - 0.5f) * _canvas.width;
        float worldZ = _canvas.position.z + (p.Position.y - 0.5f) * _canvas.height;
        result.Add(new Vector3(worldX, _canvas.position.y, worldZ));
    }
    return result;
}
}