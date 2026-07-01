//using UnityEngine;
//using UnityEngine.UI;

///// <summary>
///// SceneConnector — النسخة المصححة الكاملة
/////
///// الإصلاحات:
/////  1. UI Panels تُعرض على الجدران من الداخل (World Space, Camera Facing صح)
/////  2. النصوص العربية تظهر بالاتجاه الصحيح (لا تعكس)
/////  3. تفعيل الدلو والحبل بشكل صحيح
/////  4. PivotPoint يُصحَّح من دوران Crossbar (X=90°)
/////
///// ملاحظة UI:
/////   Canvas يبقى Screen Space Overlay للـ HUD العادي
/////   كل Panel يُحوَّل إلى World Space منفصل على الجدار المطلوب
/////   هذا يحل مشكلة "الكتابة معكوسة"
///// </summary>
//public class SceneConnector : MonoBehaviour
//{
//    // =====================================================================
//    // Inspector — Drag from Hierarchy
//    // =====================================================================

//    [Header("=== Buckets ===")]
//    public GameObject bucketMetal;
//    public GameObject bucketWood;

//    [Header("=== Ropes ===")]
//    public GameObject ropeCotton;
//    public GameObject ropeNylon;
//    public GameObject ropeSteel;

//    [Header("=== Attachment Points ===")]
//    public Transform metalBucketAttachPoint;
//    public Transform woodBucketAttachPoint;

//    [Header("=== Pivot Point (inside Crossbar) ===")]
//    public Transform pivotPoint;

//    [Header("=== Floor Canvas ===")]
//    public GameObject canvasSurface;

//    [Header("=== Walls ===")]
//    public Transform wallLeft;    // X=-5,  Y=7.5, Z=0   (scale 0.3×15×10)
//    public Transform wallRight;   // X=10,  Y=7.5, Z=0
//    public Transform wallFront;   // X=2.5, Y=7.5, Z=5.1
//    public Transform wallBack;    // X=2.3, Y=7.5, Z=-5
//    public Transform wallRoof;    // X=2.5, Y=15,  Z=0

//    [Header("=== UI Panels (will be moved to walls) ===")]
//    public RectTransform panelLeft;     // Bucket/Rope/Paint buttons
//    public RectTransform panelRight;    // Settings Sliders
//    public RectTransform panelBottom;   // Start/Stop/Pause buttons
//    public RectTransform panelStats;    // Live statistics
//    public RectTransform panelReport;   // Report

//    [Header("=== Simulation Manager (auto-filled) ===")]
//    public SimulationManager simulationManager;

//    // =====================================================================
//    // Internal
//    // =====================================================================
//    private GameObject _activeBucket;
//    private GameObject _activeRope;
//    private LineRenderer _activeLineRenderer;

//    // =====================================================================
//    // Awake
//    // =====================================================================
//    private void Awake()
//    {
//        simulationManager = GetComponent<SimulationManager>();
//        if (simulationManager == null)
//            simulationManager = gameObject.AddComponent<SimulationManager>();
//    }

//    // =====================================================================
//    // Start
//    // =====================================================================
//    private void Start()
//    {
//        // 1. Fix PivotPoint
//        FixPivotRotation();

//        // 2. Ceiling light
//        SetupCeilingLight();

//        // 3. Setup LineRenderers
//        HideAllBuckets();
//        HideAllRopes();
//        SetupLineRenderers();

//        // 4. Activate defaults
//        ActivateBucket(BucketType.Metal);
//        ActivateRope(RopeType.Cotton);

//        // 5. Place Panels on walls
//        PlacePanelsOnWalls();
//    }

//    // =====================================================================
//    // LateUpdate — update rope every frame
//    // =====================================================================
//    private void LateUpdate()
//    {
//        if (_activeLineRenderer == null || _activeBucket == null || pivotPoint == null)
//            return;

//        Vector3 start = pivotPoint.position;
//        Transform attach = GetActiveAttachPoint();
//        Vector3 end = (attach != null) ? attach.position : _activeBucket.transform.position;

//        _activeLineRenderer.SetPosition(0, start);
//        _activeLineRenderer.SetPosition(1, end);
//    }

//    // =====================================================================
//    // Fix PivotPoint
//    // =====================================================================
//    private void FixPivotRotation()
//    {
//        if (pivotPoint == null) return;
//        // Crossbar has Rotation X=90 → inherited by PivotPoint
//        // Reset it with world rotation = identity
//        pivotPoint.rotation = Quaternion.identity;
//        Debug.Log($"[SceneConnector] PivotPoint @ {pivotPoint.position} rotation fixed.");
//    }

//    // =====================================================================
//    // Activate bucket
//    // =====================================================================
//    public void ActivateBucket(BucketType type)
//    {
//        HideAllBuckets();
//        switch (type)
//        {
//            case BucketType.Metal:
//                if (bucketMetal != null)
//                {
//                    bucketMetal.SetActive(true);
//                    _activeBucket = bucketMetal;

//                    // Disable BucketVisualController (SimulationManager controls motion)
//                    var bvc = bucketMetal.GetComponent<BucketVisualController>();
//                    if (bvc != null) bvc.enabled = false;

//                    var tr = bucketMetal.GetComponent<TrailRenderer>();
//                    if (tr != null) tr.enabled = false;
//                }
//                break;

//            case BucketType.Wood:
//                if (bucketWood != null)
//                {
//                    bucketWood.SetActive(true);
//                    _activeBucket = bucketWood;

//                    var bvc = bucketWood.GetComponent<BucketVisualController>();
//                    if (bvc != null) bvc.enabled = false;
//                }
//                break;
//        }
//        Debug.Log($"[SceneConnector] Bucket: {type}");
//    }

//    // =====================================================================
//    // Activate rope
//    // =====================================================================
//    public void ActivateRope(RopeType type)
//    {
//        HideAllRopes();

//        switch (type)
//        {
//            case RopeType.Cotton: _activeRope = ropeCotton; break;
//            case RopeType.Nylon: _activeRope = ropeNylon; break;
//            case RopeType.Steel: _activeRope = ropeSteel; break;
//        }

//        if (_activeRope != null)
//        {
//            _activeRope.SetActive(true);
//            _activeLineRenderer = _activeRope.GetComponent<LineRenderer>();
//            if (_activeLineRenderer == null)
//                _activeLineRenderer = _activeRope.AddComponent<LineRenderer>();
//            ConfigureLR(_activeLineRenderer, type);
//        }
//        Debug.Log($"[SceneConnector] Rope: {type}");
//    }

//    // =====================================================================
//    // Place Panels on walls — The correct solution
//    //
//    // Principle:
//    //   - Each Panel is detached from MainCanvas
//    //   - Converted to independent World Space Canvas
//    //   - Placed flush against the wall from inside with correct orientation
//    //   - Rotation makes text face into the room (readable correctly)
//    //
//    // Room coordinates (from SceneObjects.txt):
//    //   Wall_Left  X=-5,   Z=0   → Inner surface at X=-4.85
//    //   Wall_right X=10,   Z=0   → Inner surface at X= 9.85
//    //   Wall_Front X=2.5,  Z=5.1 → Inner surface at Z= 4.95
//    //   Wall_Back  X=2.3,  Z=-5  → Inner surface at Z=-4.85
//    // =====================================================================
//    private void PlacePanelsOnWalls()
//    {
//        // ── Panel_Left → left wall (X=-5)
//        // Panel faces into room: rotation Y=+90°
//        if (panelLeft != null)
//        {
//            AttachPanelToWall(
//                panel: panelLeft,
//                worldPos: new Vector3(-4.83f, 4.0f, 0f),
//                eulerRot: new Vector3(0f, 90f, 0f),
//                // Actual panel size in meters: 0.6m width × 2.5m height on wall
//                widthM: 0.6f,
//                heightM: 2.5f
//            );
//        }

//        // ── Panel_Right → right wall (X=10)
//        // Faces into room: rotation Y=-90°
//        if (panelRight != null)
//        {
//            AttachPanelToWall(
//                panel: panelRight,
//                worldPos: new Vector3(9.83f, 4.0f, 0f),
//                eulerRot: new Vector3(0f, -90f, 0f),
//                widthM: 0.6f,
//                heightM: 2.5f
//            );
//        }

//        // ── Panel_Bottom → back wall (Z=-5)
//        // Faces forward (towards PivotPoint): rotation Y=0°
//        if (panelBottom != null)
//        {
//            AttachPanelToWall(
//                panel: panelBottom,
//                worldPos: new Vector3(2.5f, 1.2f, -4.83f),
//                eulerRot: new Vector3(0f, 0f, 0f),
//                widthM: 1.2f,
//                heightM: 0.4f
//            );
//        }

//        // ── Panel_Stats → back wall as well (above Panel_Bottom)
//        if (panelStats != null)
//        {
//            AttachPanelToWall(
//                panel: panelStats,
//                worldPos: new Vector3(2.5f, 2.5f, -4.83f),
//                eulerRot: new Vector3(0f, 0f, 0f),
//                widthM: 0.5f,
//                heightM: 1.0f
//            );
//        }

//        // ── Panel_Report → right wall (below Panel_Right)
//        if (panelReport != null)
//        {
//            AttachPanelToWall(
//                panel: panelReport,
//                worldPos: new Vector3(9.83f, 6.5f, 0f),
//                eulerRot: new Vector3(0f, -90f, 0f),
//                widthM: 0.6f,
//                heightM: 1.5f
//            );
//        }

//        Debug.Log("[SceneConnector] ✅ Panels placed on walls.");
//    }

//    /// <summary>
//    /// Detaches Panel from Canvas and converts it to World Space on the wall
//    ///
//    /// Why detach from Canvas?
//    ///   Because Screen Space Canvas converts everything to screen coordinates,
//    ///   and when converting to World Space, text appears reversed.
//    ///   Solution: each Panel = independent World Space Canvas with correct rotation.
//    ///
//    /// widthM/heightM = actual size in meters in Unity world
//    /// Scale will be calculated from original Panel size (RectTransform.rect)
//    /// </summary>
//    private void AttachPanelToWall(RectTransform panel, Vector3 worldPos,
//                                    Vector3 eulerRot, float widthM, float heightM)
//    {
//        if (panel == null) return;

//        // Detach Panel from original Canvas and make it a root object
//        panel.SetParent(null, false);
//        panel.gameObject.SetActive(true);

//        // Set world position and rotation
//        panel.position = worldPos;
//        panel.rotation = Quaternion.Euler(eulerRot);

//        // Calculate Scale from actual dimensions (rect.width, rect.height in pixels)
//        Rect r = panel.rect;
//        float scaleX = (r.width > 1f) ? widthM / r.width : 0.003f;
//        float scaleY = (r.height > 1f) ? heightM / r.height : 0.003f;
//        float scale = Mathf.Min(scaleX, scaleY);   // maintain aspect ratio
//        panel.localScale = new Vector3(scale, scale, scale);

//        // Ensure Panel has Canvas Component (World Space)
//        Canvas panelCanvas = panel.GetComponent<Canvas>();
//        if (panelCanvas == null)
//            panelCanvas = panel.gameObject.AddComponent<Canvas>();

//        panelCanvas.renderMode = RenderMode.WorldSpace;
//        panelCanvas.worldCamera = Camera.main;
//        panelCanvas.overrideSorting = true;
//        panelCanvas.sortingOrder = 10;

//        // GraphicRaycaster for click interaction
//        if (panel.GetComponent<GraphicRaycaster>() == null)
//            panel.gameObject.AddComponent<GraphicRaycaster>();

//        Debug.Log($"[SceneConnector] Panel '{panel.name}' → {worldPos}  rot={eulerRot}  scale={scale:F5}");
//    }

//    // =====================================================================
//    // Ceiling light
//    // =====================================================================
//    private void SetupCeilingLight()
//    {
//        if (wallRoof == null) return;

//        Light existing = wallRoof.GetComponentInChildren<Light>();
//        if (existing != null)
//        {
//            existing.color = Color.white;
//            existing.intensity = 2.5f;
//            existing.range = 14f;
//            return;
//        }

//        var lightObj = new GameObject("CeilingLight");
//        lightObj.transform.SetParent(wallRoof);
//        lightObj.transform.position = new Vector3(2.5f, 14.5f, 0f);

//        var light = lightObj.AddComponent<Light>();
//        light.type = LightType.Point;
//        light.color = Color.white;
//        light.intensity = 3.0f;
//        light.range = 14f;

//        Debug.Log("[SceneConnector] Ceiling light added.");
//    }

//    // =====================================================================
//    // Setup LineRenderers
//    // =====================================================================
//    private void SetupLineRenderers()
//    {
//        SetupOneLR(ropeCotton, RopeType.Cotton);
//        SetupOneLR(ropeNylon, RopeType.Nylon);
//        SetupOneLR(ropeSteel, RopeType.Steel);
//    }

//    private void SetupOneLR(GameObject rope, RopeType type)
//    {
//        if (rope == null) return;
//        var lr = rope.GetComponent<LineRenderer>() ?? rope.AddComponent<LineRenderer>();
//        ConfigureLR(lr, type);

//        // Hide Mesh
//        var mr = rope.GetComponent<MeshRenderer>();
//        if (mr != null) mr.enabled = false;
//    }

//    private void ConfigureLR(LineRenderer lr, RopeType type)
//    {
//        lr.positionCount = 2;
//        lr.useWorldSpace = true;
//        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

//        Color col;
//        float w;
//        switch (type)
//        {
//            case RopeType.Cotton: col = new Color(0.82f, 0.73f, 0.58f); w = 0.025f; break;
//            case RopeType.Nylon: col = new Color(0.20f, 0.60f, 1.00f); w = 0.015f; break;
//            case RopeType.Steel: col = new Color(0.70f, 0.70f, 0.75f); w = 0.018f; break;
//            default: col = Color.white; w = 0.02f; break;
//        }
//        lr.startWidth = w; lr.endWidth = w * 0.6f;
//        lr.startColor = col; lr.endColor = col;

//        Shader sh = Shader.Find("Sprites/Default")
//                 ?? Shader.Find("Universal Render Pipeline/Unlit")
//                 ?? Shader.Find("Unlit/Color");
//        if (sh != null)
//        {
//            var mat = new Material(sh) { color = col };
//            lr.material = mat;
//        }
//    }

//    // =====================================================================
//    // Helpers
//    // =====================================================================
//    private void HideAllBuckets()
//    {
//        bucketMetal?.SetActive(false);
//        bucketWood?.SetActive(false);
//    }

//    private void HideAllRopes()
//    {
//        ropeCotton?.SetActive(false);
//        ropeNylon?.SetActive(false);
//        ropeSteel?.SetActive(false);
//    }

//    private Transform GetActiveAttachPoint()
//    {
//        if (_activeBucket == bucketMetal) return metalBucketAttachPoint;
//        if (_activeBucket == bucketWood) return woodBucketAttachPoint;
//        return null;
//    }

//    // =====================================================================
//    // Gizmos
//    // =====================================================================
//    private void OnDrawGizmos()
//    {
//        if (pivotPoint == null) return;
//        Gizmos.color = Color.yellow;
//        Gizmos.DrawWireSphere(pivotPoint.position, 0.06f);

//        var a = GetActiveAttachPoint();
//        if (a != null)
//        {
//            Gizmos.color = Color.green;
//            Gizmos.DrawLine(pivotPoint.position, a.position);
//        }
//    }
//}

//// Enums
//public enum BucketType { Metal, Wood }
//public enum RopeType { Cotton, Nylon, Steel }