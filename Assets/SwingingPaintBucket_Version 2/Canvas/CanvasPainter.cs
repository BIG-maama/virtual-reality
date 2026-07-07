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
///
/// ✅ المزج الحقيقي عند تقاطع الخطوط:
///    كل خلية عالشبكة عندها "كتلة طلاء متراكمة" (مو بس لون).
///    عند وصول قطرة جديدة: اللون الناتج = متوسط موزون بالكتلة
///        C_result = (mass_old·C_old + mass_new_effective·C_new) / (mass_old + mass_new_effective)
///    اللزوجة بتأثر بمكانين:
///       1) mass_new_effective — كلما زادت اللزوجة، قلّت نسبة "تسلل" اللون الجديد داخل القديم
///       2) سرعة التقارب — الطلاء الأخف (لزوجة قليلة) يوصل للمزيج النهائي أسرع من الثقيل
///    والنتيجة ما بتنطبق فوراً — بتتخزن كـ"هدف" وبتتقارب تدريجياً كل فريم (Lerp أسّي بالزمن)
/// المرجع: الدراسة الفيزيائية - رسم الطلاء بطريقة VOF + مزج الألوان الموزون بالكتلة
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
    // شبكة الألوان المقابلة (اللون المعروض فعلياً — يتقارب تدريجياً نحو الهدف)
    private readonly Color[,] _colorGrid;
    // ✅ شبكة الكتلة المتراكمة لكل خلية — تمثل "كمية" اللون القديم بهذه النقطة
    private readonly float[,] _massGrid;

    private const int GridResolution = 256; // دقة الشبكة (256×256 خلية)

    // ✅ خلايا قيد التقارب التدريجي نحو لون هدف جديد
    private struct MixTarget
    {
        public int x, y;
        public Color target;
        public float rate; // معدل التقارب (1/ثانية) — يعتمد على اللزوجة
    }
    private readonly Dictionary<int, MixTarget> _mixTargets = new Dictionary<int, MixTarget>();
    private readonly List<int> _keysToRemove = new List<int>();

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
        _massGrid = new float[GridResolution, GridResolution];

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
    public bool RegisterImpact(PaintParticle particle, float currentTemperature)
    {
        Vector2 canvasUV = WorldToCanvasUV(particle.LandingPoint);

        if (canvasUV.x < 0f || canvasUV.x > 1f ||
            canvasUV.y < 0f || canvasUV.y > 1f) return false;

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

        // ✅ لزوجة الطلاء الحالية عند درجة الحرارة هاي — تتحكم بمقاومة الاختلاط وسرعة التقارب
        float viscosity = _paint.GetViscosityAtTemperature(currentTemperature);

        // ✅ كتلة القطرة (تمثل "كمية" اللون الجديد) — من حجم الكرة الحقيقي × كثافة الطلاء
        float dropMass = (4f / 3f) * Mathf.PI *
                          Mathf.Pow(Mathf.Max(particle.Radius, 0.0001f), 3) * _paint.Density;

        // لون تقريبي لتسجيل PaintPoint (مزج موزون عند مركز الاصطدام تحديداً)
        Color centerExisting = SampleColorAt(canvasUV);
        float centerMass = SampleMassAt(canvasUV);
        Color pointColor = WeightedMix(centerExisting, centerMass, particle.ParticleColor, dropMass, viscosity);

        // إنشاء نقطة الطلاء
        var point = new PaintPoint(canvasUV, pointColor, impactRadius,
                                   weberNum, _paint, tauDry);
        _paintPoints.Add(point);

        // ✅ تحديث الشبكة: مزج حقيقي موزون بالكتلة + اللزوجة، مع هدف تقارب تدريجي
        UpdateVOFGridWithMixing(canvasUV, impactRadius, particle.ParticleColor, dropMass, viscosity);

        // تحديث مساحة الانتشار
        UpdatePaintedArea(impactRadius);

        return true;
    }

    /// <summary>
    /// يُحدّث حالة جفاف جميع نقاط الطلاء + يقارب ألوان الخلايا تدريجياً نحو أهدافها الممزوجة
    /// </summary>
    public void Update(float deltaTime)
    {
        foreach (PaintPoint p in _paintPoints)
            p.UpdateTime(deltaTime);

        // ✅ التقارب التدريجي: كل خلية "قيد المزج" تتحرك نحو لونها الهدف
        // بمعدل يعتمد على اللزوجة وقت الاصطدام (خُزّن مسبقاً بـ MixTarget.rate)
        if (_mixTargets.Count > 0)
        {
            _keysToRemove.Clear();
            foreach (var kv in _mixTargets)
            {
                MixTarget mt = kv.Value;
                float alpha = 1f - Mathf.Exp(-mt.rate * deltaTime);
                Color current = _colorGrid[mt.x, mt.y];
                Color next = Color.Lerp(current, mt.target, alpha);
                _colorGrid[mt.x, mt.y] = next;

                if (ColorClose(next, mt.target))
                    _keysToRemove.Add(kv.Key);
            }
            for (int k = 0; k < _keysToRemove.Count; k++)
                _mixTargets.Remove(_keysToRemove[k]);
        }
    }

    /// <summary>
    /// مزج موزون بالكتلة بين لونين — يأخذ بعين الاعتبار كمية كل لون ولزوجة الطلاء
    /// C_result = (mass_old·C_old + mass_new_effective·C_new) / (mass_old + mass_new_effective)
    /// حيث mass_new_effective تنخفض مع ارتفاع اللزوجة (طلاء كثيف يقاوم الاختلاط الفوري)
    /// </summary>
    private Color WeightedMix(Color oldColor, float oldMass, Color newColor, float newMass, float viscosity)
    {
        float resistance = Mathf.Clamp01(viscosity / 2f); // 0=سائل خفيف جداً، 1=كثيف جداً
        float effectiveNewMass = newMass * Mathf.Lerp(1f, 0.3f, resistance);
        float totalMass = oldMass + effectiveNewMass;
        if (totalMass <= 1e-12f) return newColor;

        return new Color(
            (oldColor.r * oldMass + newColor.r * effectiveNewMass) / totalMass,
            (oldColor.g * oldMass + newColor.g * effectiveNewMass) / totalMass,
            (oldColor.b * oldMass + newColor.b * effectiveNewMass) / totalMass,
            1f);
    }

    /// <summary>
    /// يُحدّث شبكة VOF + شبكة الكتلة عند ارتطام قطرة، ويحسب هدف اللون الممزوج
    /// لكل خلية متأثرة (بدون تطبيقه فوراً — بيصير تدريجياً بـ Update)
    /// المرجع: الدراسة الفيزيائية - طريقة VOF لرسم الطلاء + مزج موزون بالكتلة/اللزوجة
    /// </summary>
    private void UpdateVOFGridWithMixing(Vector2 centerUV, float radius, Color newColor,
                                          float dropMass, float viscosity)
    {
        // تحويل نصف القطر من المتر إلى خلايا الشبكة
        float radiusCells = (radius / _canvas.width) * GridResolution;

        int cx = Mathf.RoundToInt(centerUV.x * (GridResolution - 1));
        int cy = Mathf.RoundToInt(centerUV.y * (GridResolution - 1));
        int r = Mathf.CeilToInt(radiusCells);

        float resistance = Mathf.Clamp01(viscosity / 2f);
        // لزوجة أعلى → مزج أبطأ (يوصل للمزيج النهائي بزمن أطول)
        float convergenceRate = Mathf.Lerp(4f, 0.5f, resistance);
        // لزوجة أعلى → نسبة أقل من اللون الجديد "تدخل" فوراً بالكتلة القديمة
        float newMassFactor = Mathf.Lerp(1f, 0.3f, resistance);

        for (int i = Mathf.Max(0, cx - r); i <= Mathf.Min(GridResolution - 1, cx + r); i++)
        {
            for (int j = Mathf.Max(0, cy - r); j <= Mathf.Min(GridResolution - 1, cy + r); j++)
            {
                float dist = Mathf.Sqrt((i - cx) * (i - cx) + (j - cy) * (j - cy));
                if (dist > radiusCells) continue;

                // معامل الملء: 1 في المركز، ينخفض نحو الحافة
                float fillFactor = 1f - (dist / radiusCells);

                // F جديد = F قديم + fillFactor (مع الحد بـ 1)
                _vofGrid[i, j] = Mathf.Min(1f, _vofGrid[i, j] + fillFactor * 0.8f);

                // ✅ كتلة القطرة المؤثرة على هذه الخلية تحديداً (أكبر بالمركز، أقل بالحافة)
                float cellDropMass = dropMass * fillFactor;
                float existingMass = _massGrid[i, j];
                float effectiveDropMass = cellDropMass * newMassFactor;
                float totalMass = existingMass + effectiveDropMass;

                Color existingColor = _colorGrid[i, j];
                Color targetColor = totalMass > 1e-12f
                    ? new Color(
                        (existingColor.r * existingMass + newColor.r * effectiveDropMass) / totalMass,
                        (existingColor.g * existingMass + newColor.g * effectiveDropMass) / totalMass,
                        (existingColor.b * existingMass + newColor.b * effectiveDropMass) / totalMass,
                        1f)
                    : newColor;

                // الكتلة الحقيقية المتراكمة تكبر بالكامل (اللزوجة تأثر على سرعة/نسبة الاختلاط، مو على الكمية الفعلية)
                _massGrid[i, j] = existingMass + cellDropMass;

                int key = i * GridResolution + j;
                _mixTargets[key] = new MixTarget { x = i, y = j, target = targetColor, rate = convergenceRate };
            }
        }
    }

    private static bool ColorClose(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.003f &&
               Mathf.Abs(a.g - b.g) < 0.003f &&
               Mathf.Abs(a.b - b.b) < 0.003f;
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
    /// يحصل على الكتلة المتراكمة عند إحداثي UV
    /// </summary>
    private float SampleMassAt(Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (GridResolution - 1)), 0, GridResolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (GridResolution - 1)), 0, GridResolution - 1);
        return _massGrid[x, y];
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
    }
}
