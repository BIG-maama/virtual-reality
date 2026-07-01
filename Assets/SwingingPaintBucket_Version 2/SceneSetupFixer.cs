//using UnityEngine;
//using UnityEngine.UI;

///// <summary>
///// SceneSetupFixer — يصلح ثلاث مشاكل دفعة واحدة:
/////
/////  1. إحداثيات الحركة (الدلو يتحرك بندوليًا صحيحًا)
/////     - يفصل PivotPoint عن دوران Crossbar (X=90°)
/////     - يعيد حساب موضع الدلو الابتدائي بشكل صحيح من الـ PivotPoint الحقيقي
/////
/////  2. حركة الحبل مرئية وصحيحة
/////     - يربط LineRenderer بين PivotPoint والـ RopeAttachPoint
/////
/////  3. لوحة التحكم (MainCanvas) تُعلَّق على جدران الغرفة من الداخل
/////     - يضع Panel_Left  على الجدار الأيسر  (Wall_Left  X=-5)
/////     - يضع Panel_Right على الجدار الأيمن  (Wall_Right X=10)
/////     - يضع Panel_Bottom على الجدار الخلفي (Wall_Back  Z=-5)
/////     - يضع Panel_Stats  و Panel_Report على Wall_Front
/////
///// طريقة الاستخدام:
/////   1. احذف SceneFixer من SimulationController (أو عطّله)
/////   2. أضف هذا السكريبت على SimulationController
/////   3. اسحب المراجع المطلوبة في Inspector
/////   4. اضغط Play
///// </summary>
//public class SceneSetupFixer : MonoBehaviour
//{
//    // =====================================================================
//    // [INSPECTOR] اسحب هنا الكائنات من Hierarchy
//    // =====================================================================

//    [Header("=== نقطة التعليق والعارضة ===")]
//    [Tooltip("اسحب Crossbar هنا")]
//    public Transform crossbar;

//    [Tooltip("اسحب PivotPoint (child of Crossbar) هنا")]
//    public Transform pivotPoint;

//    [Header("=== الدلوان ===")]
//    public GameObject bucketMetal;
//    public GameObject bucketWood;

//    [Header("=== الحبال ===")]
//    public GameObject ropeCotton;
//    public GameObject ropeNylon;
//    public GameObject ropeSteel;

//    [Header("=== الجدران ===")]
//    public Transform wallLeft;     // Wall_Left   X=-5,  Y=7.5, Z=0
//    public Transform wallRight;    // Wall_right  X=10,  Y=7.5, Z=0
//    public Transform wallFront;    // Wall_Front  X=2.5, Y=7.5, Z=5.1
//    public Transform wallBack;     // Wall_Back   X=2.3, Y=7.5, Z=-5
//    public Transform wallRoof;     // Wall_Roof

//    [Header("=== UI Panels (children of MainCanvas) ===")]
//    [Tooltip("Panel_Left — زر الدلو والحبل والطلاء")]
//    public RectTransform panelLeft;

//    [Tooltip("Panel_Right — sliders الإعدادات")]
//    public RectTransform panelRight;

//    [Tooltip("Panel_Bottom — أزرار التحكم")]
//    public RectTransform panelBottom;

//    [Tooltip("Panel_Stats — الإحصاءات المباشرة")]
//    public RectTransform panelStats;

//    [Tooltip("Panel_Report — التقرير النهائي")]
//    public RectTransform panelReport;

//    [Header("=== إعدادات البندول ===")]
//    [Range(5f, 60f)]
//    public float startAngleDeg = 25f;

//    [Range(0.5f, 3.5f)]
//    public float ropeLength = 1.8f;

//    // =====================================================================
//    // داخلي
//    // =====================================================================
//    private Transform _activeBucket;
//    private LineRenderer _activeLR;

//    // حالة الفيزياء
//    private float _theta;
//    private float _thetaDot;
//    private float _phi;
//    private float _phiDot;
//    private bool _running;
//    private const float _damping = 0.04f;

//    // حدود الغرفة من Scene Report
//    // Wall_Left X=-5, Wall_right X=10, Wall_Back Z=-5, Wall_Front Z=5.1
//    // Plane Y=0, Roof Y=15
//    private const float _roomXMin = -4.7f;
//    private const float _roomXMax = 9.7f;
//    private const float _roomZMin = -4.7f;
//    private const float _roomZMax = 4.8f;
//    private const float _floorY = 0.06f;

//    // =====================================================================
//    void Start()
//    {
//        FixPivotWorldRotation();
//        SelectAndPlaceBucket();
//        SetupActiveRope();
//        FixUIOnWalls();
//        BeginPendulum();
//    }

//    void FixedUpdate()
//    {
//        if (!_running) return;
//        StepPhysics(Time.fixedDeltaTime);
//        MoveBucket();
//        DrawRope();
//    }

//    // =====================================================================
//    // 1. إصلاح دوران PivotPoint
//    //    Crossbar دوران X=90 → يرثه PivotPoint → نكسر هذا الإرث
//    // =====================================================================
//    void FixPivotWorldRotation()
//    {
//        if (pivotPoint == null) return;

//        // فصل PivotPoint عن parent مؤقتاً للحصول على World position الصحيح
//        // PivotPoint World = (3, 6, 0.04) كما يظهر في SceneObjects.txt
//        Vector3 worldPos = pivotPoint.position;

//        // نعيد ضبط الـ rotation العالمي لـ PivotPoint إلى identity
//        // حتى لا تؤثر دوران Crossbar (X=90) على حسابات الفيزياء
//        pivotPoint.rotation = Quaternion.identity;

//        Debug.Log($"[SceneSetupFixer] PivotPoint World Position: {worldPos}");
//        Debug.Log($"[SceneSetupFixer] PivotPoint rotation fixed to identity.");
//    }

//    // =====================================================================
//    // 2. تفعيل الدلو المعدني ووضعه في موضعه الابتدائي الصحيح
//    //    الموضع الصحيح = PivotPoint + إزاحة بندولية بزاوية startAngleDeg
//    // =====================================================================
//    void SelectAndPlaceBucket()
//    {
//        if (bucketMetal != null) bucketMetal.SetActive(true);
//        if (bucketWood != null) bucketWood.SetActive(false);

//        _activeBucket = bucketMetal?.transform;
//        if (_activeBucket == null || pivotPoint == null) return;

//        // تعطيل أي مكوّن يحرك الدلو تلقائياً (BucketVisualController / TrailRenderer)
//        var bvc = _activeBucket.GetComponent<BucketVisualController>();
//        if (bvc != null) bvc.enabled = false;

//        var tr = _activeBucket.GetComponent<TrailRenderer>();
//        if (tr != null) tr.enabled = false;

//        // موضع بندولي ابتدائي صحيح
//        // x = Pivot.x + L·sin(θ)
//        // y = Pivot.y - L·cos(θ)
//        // z = Pivot.z  (لا إزاحة في Z عند φ=0)
//        float θ0 = startAngleDeg * Mathf.Deg2Rad;
//        Vector3 pivot = pivotPoint.position;
//        Vector3 initPos = new Vector3(
//            pivot.x + ropeLength * Mathf.Sin(θ0),
//            pivot.y - ropeLength * Mathf.Cos(θ0),
//            pivot.z
//        );

//        // تأكد أن الدلو فوق الأرضية
//        initPos.y = Mathf.Max(initPos.y, _floorY + 0.15f);
//        _activeBucket.position = initPos;

//        Debug.Log($"[SceneSetupFixer] Bucket initial position: {initPos}");
//        Debug.Log($"[SceneSetupFixer] Pivot position: {pivot}, ropeLength={ropeLength}");
//    }

//    // =====================================================================
//    // 3. إعداد LineRenderer على الحبل النشط
//    // =====================================================================
//    void SetupActiveRope()
//    {
//        if (ropeCotton != null) ropeCotton.SetActive(true);
//        if (ropeNylon != null) ropeNylon.SetActive(false);
//        if (ropeSteel != null) ropeSteel.SetActive(false);

//        if (ropeCotton == null) return;

//        // إخفاء mesh الحبل الأصلي (الأسطوانة) وإبقاء LineRenderer فقط
//        var mr = ropeCotton.GetComponent<MeshRenderer>();
//        if (mr != null) mr.enabled = false;

//        _activeLR = ropeCotton.GetComponent<LineRenderer>();
//        if (_activeLR == null)
//            _activeLR = ropeCotton.AddComponent<LineRenderer>();

//        _activeLR.positionCount = 2;
//        _activeLR.useWorldSpace = true;
//        _activeLR.startWidth = 0.03f;
//        _activeLR.endWidth = 0.02f;
//        _activeLR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

//        // مادة بسيطة لونها بيج (قطن)
//        var mat = new Material(Shader.Find("Sprites/Default"));
//        if (mat == null) mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
//        if (mat == null) mat = new Material(Shader.Find("Unlit/Color"));
//        if (mat != null)
//        {
//            mat.color = new Color(0.80f, 0.70f, 0.50f);
//            _activeLR.material = mat;
//        }
//        _activeLR.startColor = new Color(0.80f, 0.70f, 0.50f);
//        _activeLR.endColor = new Color(0.60f, 0.50f, 0.35f);

//        Debug.Log("[SceneSetupFixer] LineRenderer configured on Rope_Cotton.");
//    }

//    // =====================================================================
//    // 4. وضع UI Panels على جدران الغرفة من الداخل
//    //
//    //    الغرفة (من SceneObjects.txt):
//    //      Wall_Left  : X=-5,   Y=7.5, Z=0   (Scale 0.3 × 15 × 10)
//    //      Wall_right : X=10,   Y=7.5, Z=0   (Scale 0.3 × 15 × 10)
//    //      Wall_Front : X=2.5,  Y=7.5, Z=5.1 (Scale 15 × 15 × 0.3)
//    //      Wall_Back  : X=2.3,  Y=7.5, Z=-5  (Scale 15 × 15 × 0.3)
//    //
//    //    استراتيجية الوضع:
//    //      Panel_Left   → جدار الغرفة الأيسر  (داخل الغرفة: X ~ -4.8, Y=4, Z=0)
//    //      Panel_Right  → جدار الغرفة الأيمن  (X ~ 9.8, Y=4, Z=0)
//    //      Panel_Bottom → جدار الغرفة الخلفي  (Z ~ -4.8, Y=2, X=2.3)
//    //      Panel_Stats  → جدار الغرفة الأمامي  (Z ~ 4.8, Y=6, X=2.5)
//    //      Panel_Report → جدار الغرفة الأمامي  (Z ~ 4.8, Y=3, X=2.5)
//    //
//    //    نستخدم World Space Canvas وننقل كل Panel كـ world-space object
//    // =====================================================================
//    void FixUIOnWalls()
//    {
//        Canvas canvas = FindFirstObjectByType<Canvas>();
//        if (canvas == null)
//        {
//            Debug.LogWarning("[SceneSetupFixer] لم يتم العثور على Canvas في المشهد!");
//            return;
//        }

//        // تحويل Canvas إلى World Space حتى نضع اللوحات على الجدران
//        canvas.renderMode = RenderMode.WorldSpace;
//        canvas.worldCamera = Camera.main;

//        // ضبط حجم Canvas العام (سيتم تجاوزه بحجم كل Panel منفرداً)
//        var canvasRT = canvas.GetComponent<RectTransform>();
//        if (canvasRT != null)
//        {
//            canvasRT.localScale = new Vector3(0.003f, 0.003f, 0.003f);
//        }

//        // --- Panel_Left على الجدار الأيسر ---
//        // الجدار الأيسر: X=-5, السطح الداخلي عند X=-4.85
//        // اللوحة تواجه الداخل → rotation Y=+90°
//        if (panelLeft != null)
//        {
//            PlacePanelOnWall(
//                panel: panelLeft,
//                worldPos: new Vector3(-4.70f, 4.5f, 0f),
//                rotation: Quaternion.Euler(0f, 90f, 0f),
//                scale: new Vector3(0.005f, 0.005f, 0.005f),
//                label: "Panel_Left (جدار أيسر)"
//            );
//        }

//        // --- Panel_Right على الجدار الأيمن ---
//        // الجدار الأيمن: X=10, السطح الداخلي عند X=9.85
//        // اللوحة تواجه الداخل → rotation Y=-90°
//        if (panelRight != null)
//        {
//            PlacePanelOnWall(
//                panel: panelRight,
//                worldPos: new Vector3(9.70f, 4.5f, 0f),
//                rotation: Quaternion.Euler(0f, -90f, 0f),
//                scale: new Vector3(0.005f, 0.005f, 0.005f),
//                label: "Panel_Right (جدار أيمن)"
//            );
//        }

//        // --- Panel_Bottom على الجدار الخلفي ---
//        // Wall_Back: Z=-5, السطح الداخلي عند Z=-4.85
//        // اللوحة تواجه للأمام → rotation Y=0°
//        if (panelBottom != null)
//        {
//            PlacePanelOnWall(
//                panel: panelBottom,
//                worldPos: new Vector3(2.5f, 1.8f, -4.75f),
//                rotation: Quaternion.Euler(0f, 0f, 0f),
//                scale: new Vector3(0.006f, 0.006f, 0.006f),
//                label: "Panel_Bottom (جدار خلفي)"
//            );
//        }

//        // --- Panel_Stats على الجدار الأمامي يمين ---
//        // Wall_Front: Z=5.1, السطح الداخلي عند Z=4.95
//        // اللوحة تواجه للخلف → rotation Y=180°
//        if (panelStats != null)
//        {
//            PlacePanelOnWall(
//                panel: panelStats,
//                worldPos: new Vector3(0.5f, 5.5f, 4.80f),
//                rotation: Quaternion.Euler(0f, 180f, 0f),
//                scale: new Vector3(0.004f, 0.004f, 0.004f),
//                label: "Panel_Stats (جدار أمامي يمين)"
//            );
//        }

//        // --- Panel_Report في منتصف الجدار الأمامي ---
//        if (panelReport != null)
//        {
//            PlacePanelOnWall(
//                panel: panelReport,
//                worldPos: new Vector3(4.5f, 5.0f, 4.80f),
//                rotation: Quaternion.Euler(0f, 180f, 0f),
//                scale: new Vector3(0.004f, 0.004f, 0.004f),
//                label: "Panel_Report (جدار أمامي وسط)"
//            );
//        }

//        Debug.Log("[SceneSetupFixer] تم وضع جميع Panels على الجدران.");
//    }

//    // =====================================================================
//    // مساعد: ينقل Panel إلى موضع على الجدار
//    // =====================================================================
//    void PlacePanelOnWall(RectTransform panel, Vector3 worldPos,
//                          Quaternion rotation, Vector3 scale, string label)
//    {
//        if (panel == null) return;

//        panel.SetParent(null);          // افصله عن Canvas
//        panel.position = worldPos;
//        panel.rotation = rotation;
//        panel.localScale = scale;

//        // تأكد أن الـ Canvas الخاص به يسمح بالعرض في World Space
//        Canvas subCanvas = panel.GetComponent<Canvas>();
//        if (subCanvas == null)
//            subCanvas = panel.gameObject.AddComponent<Canvas>();
//        subCanvas.overrideSorting = true;
//        subCanvas.sortingOrder = 5;

//        Debug.Log($"[SceneSetupFixer] {label} → {worldPos}");
//    }

//    // =====================================================================
//    // 5. بدء البندول
//    // =====================================================================
//    void BeginPendulum()
//    {
//        _theta = startAngleDeg * Mathf.Deg2Rad;
//        _thetaDot = 0f;
//        _phi = 0f;
//        _phiDot = 0f;
//        _running = true;
//        Debug.Log("[SceneSetupFixer] ✅ البندول بدأ!");
//    }

//    // =====================================================================
//    // 6. خطوة فيزياء البندول (Lagrangian)
//    //    θ̈ = φ̇²·sin(θ)·cos(θ) − (g/L)·sin(θ) − b·θ̇
//    //    φ̈ = −2·θ̇·φ̇·cot(θ)    − b·φ̇
//    // =====================================================================
//    void StepPhysics(float dt)
//    {
//        float g = 9.80665f;
//        float L = ropeLength;
//        float sinT = Mathf.Sin(_theta);
//        float cosT = Mathf.Cos(_theta);
//        float cotT = (Mathf.Abs(sinT) > 0.0015f) ? cosT / sinT : 0f;

//        float θDD = _phiDot * _phiDot * sinT * cosT
//                  - (g / L) * sinT
//                  - _damping * _thetaDot;

//        float φDD = -2f * _thetaDot * _phiDot * cotT
//                  - _damping * _phiDot;

//        _thetaDot += θDD * dt;
//        _phiDot += φDD * dt;
//        _theta += _thetaDot * dt;
//        _phi += _phiDot * dt;

//        // تقييد الزاوية لمنع الفوضى العددية
//        _theta = Mathf.Clamp(_theta, -Mathf.PI * 0.92f, Mathf.PI * 0.92f);
//    }

//    // =====================================================================
//    // 7. تحريك الدلو في الفضاء ثلاثي الأبعاد
//    //    x = Pivot.x + L·sin(θ)·cos(φ)
//    //    y = Pivot.y − L·cos(θ)
//    //    z = Pivot.z + L·sin(θ)·sin(φ)
//    // =====================================================================
//    void MoveBucket()
//    {
//        if (_activeBucket == null || pivotPoint == null) return;

//        float L = ropeLength;
//        float sinT = Mathf.Sin(_theta);
//        float cosT = Mathf.Cos(_theta);
//        float sinP = Mathf.Sin(_phi);
//        float cosP = Mathf.Cos(_phi);

//        Vector3 pivot = pivotPoint.position;

//        float bx = pivot.x + L * sinT * cosP;
//        float by = pivot.y - L * cosT;
//        float bz = pivot.z + L * sinT * sinP;

//        // منع الدلو من السقوط تحت الأرضية
//        if (by < _floorY + 0.15f)
//        {
//            by = _floorY + 0.15f;
//            if (_thetaDot < 0f) _thetaDot *= -0.25f; // ارتداد خفيف
//        }

//        // منع الخروج من الغرفة
//        bx = Mathf.Clamp(bx, _roomXMin + 0.25f, _roomXMax - 0.25f);
//        bz = Mathf.Clamp(bz, _roomZMin + 0.25f, _roomZMax - 0.25f);

//        _activeBucket.position = new Vector3(bx, by, bz);
//    }

//    // =====================================================================
//    // 8. رسم الحبل
//    // =====================================================================
//    void DrawRope()
//    {
//        if (_activeLR == null || pivotPoint == null || _activeBucket == null) return;

//        // نقطة بداية الحبل = PivotPoint
//        Vector3 start = pivotPoint.position;

//        // نقطة نهاية الحبل = RopeAttachPoint (مقبض الدلو) إذا موجود
//        Transform attach = FindDeepChild(_activeBucket, "RopeAttachPoint");
//        Vector3 end = (attach != null)
//            ? attach.position
//            : _activeBucket.position + Vector3.up * 0.20f;

//        _activeLR.SetPosition(0, start);
//        _activeLR.SetPosition(1, end);
//    }

//    // =====================================================================
//    // 9. البحث عن Child بالاسم (recursive)
//    // =====================================================================
//    Transform FindDeepChild(Transform parent, string name)
//    {
//        if (parent == null) return null;
//        foreach (Transform c in parent)
//        {
//            if (c.name == name) return c;
//            var found = FindDeepChild(c, name);
//            if (found != null) return found;
//        }
//        return null;
//    }

//    // =====================================================================
//    // Gizmos — مساعدة بصرية في Editor
//    // =====================================================================
//    void OnDrawGizmosSelected()
//    {
//        if (pivotPoint == null) return;
//        Gizmos.color = Color.green;
//        Gizmos.DrawWireSphere(pivotPoint.position, 0.08f);

//        if (_activeBucket != null)
//        {
//            Gizmos.color = Color.yellow;
//            Gizmos.DrawLine(pivotPoint.position, _activeBucket.position);
//            Gizmos.color = Color.red;
//            Gizmos.DrawWireSphere(_activeBucket.position, 0.12f);
//        }

//        // رسم حدود الغرفة
//        Gizmos.color = Color.cyan;
//        Vector3 center = new Vector3(2.5f, 4f, 0f);
//        Vector3 size = new Vector3(_roomXMax - _roomXMin, 8f, _roomZMax - _roomZMin);
//        Gizmos.DrawWireCube(center, size);
//    }

//    // =====================================================================
//    // واجهة عامة للـ SceneConnector / MainUI
//    // =====================================================================
//    public void RestartPendulum()
//    {
//        BeginPendulum();
//        SelectAndPlaceBucket();
//    }

//    public void SetStartAngle(float deg)
//    {
//        startAngleDeg = Mathf.Clamp(deg, 5f, 60f);
//    }

//    public void SetRopeLength(float len)
//    {
//        ropeLength = Mathf.Clamp(len, 0.5f, 3.5f);
//    }

//    /// <summary>
//    /// يُعيد الدلو إلى موضعه الابتدائي بزاوية جديدة
//    /// استدعِ هذا من زر "إعادة التشغيل" في الواجهة
//    /// </summary>
//    public void ResetWithAngle(float angleDeg)
//    {
//        SetStartAngle(angleDeg);
//        _theta = startAngleDeg * Mathf.Deg2Rad;
//        _thetaDot = 0f;
//        _phi = 0f;
//        _phiDot = 0f;
//        SelectAndPlaceBucket();
//        Debug.Log($"[SceneSetupFixer] إعادة تشغيل بزاوية {angleDeg}°");
//    }
//}