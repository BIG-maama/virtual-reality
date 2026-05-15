using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// السكريبت الرئيسي النهائي - يربط كل كائنات المشهد مع الفيزياء
/// 
/// يستخدم BucketPhysics (معادلات لاغرانج + RK4)
/// يستخدم PaintEmitter (تورتشيلي + حركة مقذوفة)
/// يستخدم CanvasPainter (VOF + مزج ألوان + جفاف)
///
/// من بيانات مشهدك الحقيقية:
///   Crossbar:       Pos=(3, 6, 0.04)
///   PivotPoint:     WorldPos=(3, 6, 0.04)
///   Canvas_Surface: Pos=(3.2, 0.06, 0)  Scale=(0.8,1,0.8)
///   Bucket Scale:   (0.2, 0.1, 0.2)
///   Handle_Top:     LocalPos=(0,2,0) → WorldOffset Y=0.2
///
/// توزيع الـ Panels على الجدران:
///   Panel_Left   → Wall_Left   (X=-4.85)  يواجه الداخل  Rot Y=+90
///   Panel_Right  → Wall_right  (X= 9.85)  يواجه الداخل  Rot Y=-90
///   Panel_Stats  → Wall_Back   (Z=-4.85)  يواجه الأمام  Rot Y=  0
///   Panel_Bottom → Wall_Back   (Z=-4.85)  تحت Stats      Rot Y=  0
///   Panel_Report → Wall_right  (X= 9.85)  تحت Right      Rot Y=-90
/// </summary>
public class SceneConnectorFinal : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════
    // اسحب الكائنات من Hierarchy هنا
    // ═══════════════════════════════════════════════════════
    [Header("── الكائنات الأساسية (اسحب من Hierarchy) ──")]
    public Transform pivotPoint;
    public GameObject bucketMetal;
    public GameObject bucketWood;
    public GameObject ropeCotton;
    public GameObject ropeNylon;
    public GameObject ropeSteel;
    public Transform canvasSurface;
    public Camera mainCamera;

    // ═══════════════════════════════════════════════════════
    // UI Panels — اسحب من Hierarchy
    // ═══════════════════════════════════════════════════════
    [Header("── UI Panels (اسحب من Hierarchy) ──")]
    public RectTransform panelLeft;    // أزرار الدلو/الحبل/الطلاء → جدار اليسار
    public RectTransform panelRight;   // Sliders الإعدادات        → جدار اليمين
    public RectTransform panelStats;   // الإحصاءات المباشرة       → جدار الخلف (أعلى)
    public RectTransform panelBottom;  // أزرار Start/Stop/Pause   → جدار الخلف (أسفل)
    public RectTransform panelReport;  // التقرير النهائي          → جدار اليمين (أسفل)

    // ═══════════════════════════════════════════════════════
    // إعدادات يمكن تغييرها من Inspector
    // ═══════════════════════════════════════════════════════
    [Header("── إعدادات البندول ──")]
    [Range(5f, 70f)] public float startAngleDeg = 30f;
    [Range(0f, 360f)] public float startPhiDeg = 0f;
    [Range(0.5f, 5f)] public float ropeLength = 2.5f;

    [Header("── إعدادات الطلاء ──")]
    public Color paintColor = Color.red;

    // ═══════════════════════════════════════════════════════
    // المحركات الأساسية
    // ═══════════════════════════════════════════════════════
    private BucketPhysics _physics;
    private PaintEmitter _emitter;
    private CanvasPainter _painter;
    private SimulationConfig _config;

    private Transform _activeBucket;
    private Transform _activeAttach;
    private LineRenderer _lr;
    private bool _running = false;

    // ثوابت من بيانات المشهد
    private const float CANVAS_Y = 0.061f;
    private const float CANVAS_X = 3.2f;
    private const float CANVAS_HALF = 0.4f;

    // ═══════════════════════════════════════════════════════
    // إحداثيات الجدران الداخلية (من SceneObjects الحقيقي)
    //
    //   Wall_Left  : X=-5.0,  سمك=0.3  → سطح داخلي X=-4.85
    //   Wall_right : X=10.0,  سمك=0.3  → سطح داخلي X= 9.85
    //   Wall_Back  : Z=-5.0,  سمك=0.3  → سطح داخلي Z=-4.85
    //   Wall_Front : Z= 5.1,  سمك=0.3  → سطح داخلي Z= 4.95
    //   ارتفاع الغرفة: Y=0 (أرضية) إلى Y=15 (سقف)
    //   مركز مريح للعرض: Y=4 إلى Y=8
    // ═══════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════
    // Start
    // ═══════════════════════════════════════════════════════
    private void Start()
    {
        FixPivotRotation();
        FixCamera();
        BuildConfig();
        InitBucket();
        InitRope();
        InitPhysics();

        // وضع الـ Panels على الجدران
        //PlacePanelsOnWalls();

        _running = true;

        Debug.Log("[SCF] Started! Pivot=" + (pivotPoint?.position.ToString() ?? "NULL"));
        Debug.Log("[SCF] Period T=" + _physics.GetPeriod().ToString("F3") + "s");
    }

    // ═══════════════════════════════════════════════════════
    // FixedUpdate - حلقة الفيزياء
    // ═══════════════════════════════════════════════════════
    private void FixedUpdate()
    {
        if (!_running || _physics == null) return;
        float dt = Time.fixedDeltaTime;

        _physics.Step(dt);
        MoveBucket();
        UpdateRopeLine();
        HandlePaint(dt);
    }

    // ═══════════════════════════════════════════════════════
    // وضع الـ Panels على الجدران الداخلية
    //
    // المبدأ:
    //   كل Panel يُفصل عن MainCanvas ويتحول لـ World Space Canvas مستقل
    //   يُوضع ملاصقاً للجدار من الداخل بدوران يجعل النص يواجه المستخدم
    //
    // Panel_Left   → Wall_Left  X=-4.85  Rot(0,+90,0)  يواجه يمين الغرفة
    // Panel_Right  → Wall_right X= 9.85  Rot(0,-90,0)  يواجه يسار الغرفة
    // Panel_Stats  → Wall_Back  Z=-4.85  Rot(0,  0,0)  يواجه الأمام
    // Panel_Bottom → Wall_Back  Z=-4.85  Rot(0,  0,0)  يواجه الأمام (أسفل Stats)
    // Panel_Report → Wall_right X= 9.85  Rot(0,-90,0)  يواجه يسار الغرفة (أسفل Right)
    // ═══════════════════════════════════════════════════════
    //private void PlacePanelsOnWalls()
    //{
    //    // ──────────────────────────────────────────────────
    //    // Panel_Left → جدار اليسار
    //    // الموضع: X=-4.85 (سطح الجدار من الداخل)
    //    //         Y=5.5   (منتصف الغرفة عمودياً)
    //    //         Z=0.0   (منتصف الجدار أفقياً)
    //    // الدوران: Y=+90 حتى يواجه داخل الغرفة (ناحية اليمين)
    //    // الحجم: 2.5m عرض × 4.0m ارتفاع على الجدار
    //    // ──────────────────────────────────────────────────
    //    AttachPanelToWall(
    //        panel: panelLeft,
    //        worldPos: new Vector3(-4.85f, 5.5f, 0.0f),
    //        eulerRot: new Vector3(0f, 90f, 0f),
    //        widthM: 2.5f,
    //        heightM: 4.0f
    //    );

    //    // ──────────────────────────────────────────────────
    //    // Panel_Right → جدار اليمين (القسم العلوي)
    //    // الموضع: X=9.85  Y=7.0  Z=0.0
    //    // الدوران: Y=-90 حتى يواجه داخل الغرفة (ناحية اليسار)
    //    // الحجم: 2.5m عرض × 3.0m ارتفاع
    //    // ──────────────────────────────────────────────────
    //    AttachPanelToWall(
    //        panel: panelRight,
    //        worldPos: new Vector3(9.85f, 7.0f, 0.0f),
    //        eulerRot: new Vector3(0f, -90f, 0f),
    //        widthM: 2.5f,
    //        heightM: 3.0f
    //    );

    //    // ──────────────────────────────────────────────────
    //    // Panel_Stats → جدار الخلف (القسم العلوي)
    //    // الموضع: X=2.5  Y=7.5  Z=-4.85
    //    // الدوران: Y=0 يواجه الأمام (نحو الكاميرا)
    //    // الحجم: 3.0m عرض × 2.5m ارتفاع
    //    // ──────────────────────────────────────────────────
    //    AttachPanelToWall(
    //        panel: panelStats,
    //        worldPos: new Vector3(2.5f, 7.5f, -4.85f),
    //        eulerRot: new Vector3(0f, 0f, 0f),
    //        widthM: 3.0f,
    //        heightM: 2.5f
    //    );

    //    // ──────────────────────────────────────────────────
    //    // Panel_Bottom → جدار الخلف (تحت Stats)
    //    // الموضع: X=2.5  Y=4.0  Z=-4.85
    //    // الدوران: Y=0 يواجه الأمام
    //    // الحجم: 3.5m عرض × 1.2m ارتفاع (أزرار أفقية)
    //    // ──────────────────────────────────────────────────
    //    AttachPanelToWall(
    //        panel: panelBottom,
    //        worldPos: new Vector3(2.5f, 4.0f, -4.85f),
    //        eulerRot: new Vector3(0f, 0f, 0f),
    //        widthM: 3.5f,
    //        heightM: 1.2f
    //    );

    //    // ──────────────────────────────────────────────────
    //    // Panel_Report → جدار اليمين (تحت Panel_Right)
    //    // الموضع: X=9.85  Y=3.5  Z=0.0
    //    // الدوران: Y=-90 يواجه داخل الغرفة
    //    // الحجم: 2.5m عرض × 2.0m ارتفاع
    //    // ──────────────────────────────────────────────────
    //    AttachPanelToWall(
    //        panel: panelReport,
    //        worldPos: new Vector3(9.85f, 3.5f, 0.0f),
    //        eulerRot: new Vector3(0f, -90f, 0f),
    //        widthM: 2.5f,
    //        heightM: 2.0f
    //    );

    //    Debug.Log("[SCF] ✅ Panels وُضعت على الجدران بنجاح.");
    //}

    /// <summary>
    /// يفصل Panel عن Canvas الأصلي ويحوله إلى World Space Canvas مستقل
    /// ويضعه على الجدار بالموضع والدوران والحجم المطلوب
    ///
    /// widthM  = العرض الفعلي المطلوب بالمتر في عالم Unity
    /// heightM = الارتفاع الفعلي المطلوب بالمتر في عالم Unity
    /// يُحسب localScale تلقائياً من حجم الـ RectTransform الأصلي
    /// </summary>
    //private void AttachPanelToWall(RectTransform panel, Vector3 worldPos,
    //                                Vector3 eulerRot, float widthM, float heightM)
    //{
    //    if (panel == null) return;

    //    // 1. فصل Panel عن Canvas الأصلي
    //    panel.SetParent(null, false);
    //    panel.gameObject.SetActive(true);

    //    // 2. تعيين الموضع والدوران في العالم
    //    panel.position = worldPos;
    //    panel.rotation = Quaternion.Euler(eulerRot);

    //    // 3. حساب Scale من حجم الـ RectTransform الأصلي بالـ pixels
    //    //    Scale = الحجم المطلوب (متر) / حجم الـ rect (pixels)
    //    Rect r = panel.rect;
    //    float scaleX = (r.width > 1f) ? widthM / r.width : 0.003f;
    //    float scaleY = (r.height > 1f) ? heightM / r.height : 0.003f;
    //    // نأخذ الأصغر للحفاظ على التناسب
    //    float scale = Mathf.Min(scaleX, scaleY);
    //    panel.localScale = new Vector3(scale, scale, scale);

    //    // 4. تحويل Panel إلى World Space Canvas مستقل
    //    Canvas panelCanvas = panel.GetComponent<Canvas>();
    //    if (panelCanvas == null)
    //        panelCanvas = panel.gameObject.AddComponent<Canvas>();

    //    panelCanvas.renderMode = RenderMode.WorldSpace;
    //    panelCanvas.worldCamera = mainCamera != null ? mainCamera : Camera.main;
    //    panelCanvas.overrideSorting = true;
    //    panelCanvas.sortingOrder = 10;

    //    // 5. إضافة GraphicRaycaster للتفاعل بالنقر
    //    if (panel.GetComponent<GraphicRaycaster>() == null)
    //        panel.gameObject.AddComponent<GraphicRaycaster>();

    //    Debug.Log($"[SCF] Panel '{panel.name}' → Pos={worldPos}  Rot={eulerRot}  Scale={scale:F5}");
    //}

    // ═══════════════════════════════════════════════════════
    // إصلاح دوران PivotPoint
    // ═══════════════════════════════════════════════════════
    private void FixPivotRotation()
    {
        if (pivotPoint == null) return;
        pivotPoint.localRotation = Quaternion.identity;
        Debug.Log("[SCF] PivotPoint World = " + pivotPoint.position);
    }

    // ═══════════════════════════════════════════════════════
    // تصحيح الكاميرا
    // ═══════════════════════════════════════════════════════
    private void FixCamera()
    {
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(2.5f, 5.0f, -7.0f);
        cam.transform.LookAt(new Vector3(3f, 3f, 0f));
    }

    // ═══════════════════════════════════════════════════════
    // بناء SimulationConfig
    // ═══════════════════════════════════════════════════════
    private void BuildConfig()
    {
        _config = new SimulationConfig();

        _config.bucket.shape = BucketShape.Cylindrical;
        _config.bucket.innerRadius = 0.10f;
        _config.bucket.totalHeight = 0.20f;
        _config.bucket.emptyMass = 0.50f;
        _config.bucket.holes.Clear();
        _config.bucket.holes.Add(new HoleData
        {
            shape = HoleShape.Circular,
            radius = 0.003f,
            heightFromBottom = 0.01f,
            dischargeCoefficient = 0.7f
        });

        _config.rope.material = RopeMaterial.Cotton;
        _config.rope.initialLength = ropeLength;
        _config.rope.radius = 0.005f;
        _config.rope.pivotPoint = pivotPoint != null
                                     ? pivotPoint.position
                                     : new Vector3(3f, 6f, 0.04f);

        _config.paint.paintType = PaintType.WaterBased;
        _config.paint.initialHeight = 0.12f;
        _config.paint.colors = new Color[] { paintColor };

        _config.canvas.position = canvasSurface != null
                                  ? canvasSurface.position
                                  : new Vector3(3.2f, 0.06f, 0f);
        _config.canvas.width = 0.8f;
        _config.canvas.height = 0.8f;

        _config.environment.gravity = 9.80665f;
        _config.environment.temperature = 20f;
        _config.environment.humidity = 50f;
        _config.environment.atmosphericPressure = 1013.25f;
        _config.environment.pivotFriction = 0.01f;
        _config.environment.windSpeed = 0f;

        _config.initialAngleDeg = startAngleDeg;
        _config.initialPhiDeg = startPhiDeg;
        _config.initialAngularVelocity = 0f;
    }

    // ═══════════════════════════════════════════════════════
    // تهيئة الدلو
    // ═══════════════════════════════════════════════════════
    private void InitBucket()
    {
        if (bucketMetal != null) bucketMetal.SetActive(true);
        if (bucketWood != null) bucketWood.SetActive(false);
        _activeBucket = bucketMetal?.transform;
        if (_activeBucket == null) return;

        var bvc = _activeBucket.GetComponent<BucketVisualController>();
        if (bvc != null) bvc.enabled = false;
        var tr = _activeBucket.GetComponent<TrailRenderer>();
        if (tr != null) tr.enabled = false;

        _activeAttach = FindDeep(_activeBucket, "RopeAttachPoint");

        if (pivotPoint != null)
        {
            float st = Mathf.Sin(startAngleDeg * Mathf.Deg2Rad);
            float ct = Mathf.Cos(startAngleDeg * Mathf.Deg2Rad);
            Vector3 attachPos = new Vector3(
                pivotPoint.position.x + ropeLength * st,
                pivotPoint.position.y - ropeLength * ct,
                pivotPoint.position.z
            );
            _activeBucket.position = attachPos - Vector3.up * 0.2f;
            var p = _activeBucket.position;
            p.y = Mathf.Max(p.y, 0.15f);
            _activeBucket.position = p;
        }
    }

    // ═══════════════════════════════════════════════════════
    // تهيئة LineRenderer للحبل
    // ═══════════════════════════════════════════════════════
    private void InitRope()
    {
        if (ropeCotton != null) ropeCotton.SetActive(true);
        if (ropeNylon != null) ropeNylon.SetActive(false);
        if (ropeSteel != null) ropeSteel.SetActive(false);
        if (ropeCotton == null) return;

        var mr = ropeCotton.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        _lr = ropeCotton.GetComponent<LineRenderer>();
        if (_lr == null) _lr = ropeCotton.AddComponent<LineRenderer>();
        _lr.positionCount = 2;
        _lr.useWorldSpace = true;
        _lr.startWidth = 0.035f;
        _lr.endWidth = 0.02f;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.72f, 0.60f, 0.40f);
        _lr.material = mat;
        _lr.startColor = new Color(0.72f, 0.60f, 0.40f);
        _lr.endColor = new Color(0.50f, 0.40f, 0.25f);
    }

    // ═══════════════════════════════════════════════════════
    // تهيئة BucketPhysics + PaintEmitter + CanvasPainter
    // ═══════════════════════════════════════════════════════
    private void InitPhysics()
    {
        _physics = new BucketPhysics(
            _config.bucket, _config.rope,
            _config.paint, _config.environment
        );
        _physics.Initialize(startAngleDeg, startPhiDeg, 0f);

        _emitter = new PaintEmitter(
            _config.bucket, _config.paint, _config.environment
        );

        _painter = new CanvasPainter(
            _config.canvas, _config.paint, _config.environment
        );
    }

    // ═══════════════════════════════════════════════════════
    // تحريك الدلو
    // ═══════════════════════════════════════════════════════
    private void MoveBucket()
    {
        if (_activeBucket == null || pivotPoint == null) return;

        Vector3 attachPos = pivotPoint.position + _physics.BucketPosition;
        Vector3 bucketPos = attachPos - Vector3.up * 0.2f;

        bucketPos.y = Mathf.Max(bucketPos.y, 0.15f);
        bucketPos.x = Mathf.Clamp(bucketPos.x, -4.7f, 9.7f);
        bucketPos.z = Mathf.Clamp(bucketPos.z, -4.7f, 4.9f);

        _activeBucket.position = bucketPos;

        Vector3 vel = _physics.BucketVelocity;
        if (vel.magnitude > 0.05f)
        {
            _activeBucket.rotation = Quaternion.Lerp(
                _activeBucket.rotation,
                Quaternion.Euler(-vel.z * 8f, 0f, vel.x * 8f),
                Time.deltaTime * 4f
            );
        }
    }

    // ═══════════════════════════════════════════════════════
    // تحديث LineRenderer الحبل
    // ═══════════════════════════════════════════════════════
    private void UpdateRopeLine()
    {
        if (_lr == null || pivotPoint == null) return;
        _lr.SetPosition(0, pivotPoint.position);
        _lr.SetPosition(1, _activeAttach != null
                           ? _activeAttach.position
                           : _activeBucket != null
                             ? _activeBucket.position + Vector3.up * 0.2f
                             : pivotPoint.position);
    }

    // ═══════════════════════════════════════════════════════
    // إدارة الطلاء
    // ═══════════════════════════════════════════════════════
    private void HandlePaint(float dt)
    {
        if (_activeBucket == null || _emitter == null) return;

        _emitter.UpdateEmission(
            dt,
            _activeBucket.position,
            _physics.BucketVelocity,
            _physics.CurrentPaintHeight,
            CANVAS_Y
        );

        var landed = _emitter.CollectLandedParticles();
        foreach (var p in landed)
        {
            float px = p.LandingPoint.x;
            float pz = p.LandingPoint.z;
            bool onCanvas = px >= (CANVAS_X - CANVAS_HALF) &&
                            px <= (CANVAS_X + CANVAS_HALF) &&
                            pz >= -CANVAS_HALF && pz <= CANVAS_HALF;
            if (onCanvas)
            {
                _painter.RegisterImpact(p, _config.environment.temperature);
                CreateSplat(p.LandingPoint, p.ParticleColor, p.Velocity.magnitude);
            }
        }

        _painter.Update(dt);
    }

    // ═══════════════════════════════════════════════════════
    // إنشاء بقعة طلاء مرئية
    // ═══════════════════════════════════════════════════════
    private void CreateSplat(Vector3 pos, Color col, float speed)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "PaintSplat";
        Destroy(go.GetComponent<CapsuleCollider>());

        float r = Mathf.Clamp(speed * 0.013f, 0.015f, 0.07f);
        go.transform.position = new Vector3(pos.x, CANVAS_Y + 0.001f, pos.z);
        go.transform.localScale = new Vector3(r, 0.002f, r);

        var rend = go.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat == null || !mat.shader.isSupported)
            mat = new Material(Shader.Find("Standard"));
        mat.color = col;
        rend.material = mat;
        Destroy(go, 180f);
    }

    // ═══════════════════════════════════════════════════════
    // واجهات عامة للـ UI
    // ═══════════════════════════════════════════════════════
    public void SetPaintColor(Color c)
    {
        paintColor = c;
        if (_config != null) _config.paint.colors = new Color[] { c };
    }

    public void SwitchBucket(bool metal)
    {
        bucketMetal?.SetActive(metal);
        bucketWood?.SetActive(!metal);
        _activeBucket = metal ? bucketMetal?.transform : bucketWood?.transform;
        if (_activeBucket != null)
        {
            var bvc = _activeBucket.GetComponent<BucketVisualController>();
            if (bvc != null) bvc.enabled = false;
        }
        _activeAttach = FindDeep(_activeBucket, "RopeAttachPoint");
    }

    public void SwitchRope(int idx)
    {
        ropeCotton?.SetActive(idx == 0);
        ropeNylon?.SetActive(idx == 1);
        ropeSteel?.SetActive(idx == 2);
        GameObject r = idx == 0 ? ropeCotton : idx == 1 ? ropeNylon : ropeSteel;
        if (r == null) return;
        r.GetComponent<MeshRenderer>()?.gameObject.SetActive(false);
        _lr = r.GetComponent<LineRenderer>() ?? r.AddComponent<LineRenderer>();
        _lr.positionCount = 2;
        _lr.useWorldSpace = true;
        _lr.startWidth = 0.035f;
        _lr.endWidth = 0.02f;
    }

    public void Restart()
    {
        foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (g != null && g.name == "PaintSplat") Destroy(g);
        BuildConfig();
        InitPhysics();
        InitBucket();
    }

    public string GetStatsText()
    {
        if (_physics == null) return "Not running";
        return
            $"Time: {_physics.SimulationTime:F1}s\n" +
            $"Theta: {_physics.Theta * Mathf.Rad2Deg:F1} deg\n" +
            $"Mass m(t): {_physics.CurrentMass * 1000:F1} g\n" +
            $"Paint h(t): {_physics.CurrentPaintHeight * 100:F1} cm\n" +
            $"Rope L(t): {_physics.CurrentRopeLength:F3} m\n" +
            $"Swings: {_physics.SwingCount}\n" +
            $"KE: {_physics.GetKineticEnergy():F4} J\n" +
            $"Tension: {_physics.GetRopeTension():F2} N\n" +
            $"Period: {_physics.GetPeriod():F3} s\n" +
            $"Paths: {_painter?.TotalPathCount ?? 0}\n" +
            $"Area: {(_painter?.PaintedAreaM2 ?? 0) * 10000:F2} cm2";
    }

    public BucketPhysics GetPhysics() => _physics;
    public CanvasPainter GetPainter() => _painter;

    // ═══════════════════════════════════════════════════════
    // دوال مساعدة
    // ═══════════════════════════════════════════════════════
    private Transform FindDeep(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform c in parent)
        {
            if (c.name == name) return c;
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 pivot = pivotPoint?.position ?? new Vector3(3f, 6f, 0.04f);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pivot, 0.1f);
        if (_activeBucket != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pivot, _activeBucket.position + Vector3.up * 0.2f);
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(CANVAS_X, CANVAS_Y, 0f),
                            new Vector3(0.8f, 0.01f, 0.8f));
    }
}