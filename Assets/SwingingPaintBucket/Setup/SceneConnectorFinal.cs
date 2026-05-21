using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SceneConnectorFinal : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════
    // الكائنات - اسحبهم من Hierarchy
    // ═══════════════════════════════════════════════════════
    [Header("── الكائنات الأساسية ──")]
    public Transform pivotPoint;
    public GameObject bucketMetal;
    public GameObject bucketWood;
    public GameObject ropeCotton;
    public GameObject ropeNylon;
    public GameObject ropeSteel;
    public Transform canvasSurface;
    public Camera mainCamera;

    // ═══════════════════════════════════════════════════════
    // UI Panels
    // ═══════════════════════════════════════════════════════
    [Header("── UI Panels ──")]
    public RectTransform panelLeft;
    public RectTransform panelRight;
    public RectTransform panelStats;
    public RectTransform panelBottom;
    public RectTransform panelReport;

    // ═══════════════════════════════════════════════════════
    // إعدادات البندول
    // ═══════════════════════════════════════════════════════
    [Header("── إعدادات البندول ──")]
    [Range(5f, 70f)] public float startAngleDeg = 30f;
    [Range(0f, 360f)] public float startPhiDeg = 0f;
    [Range(25f, 500f)] public float ropeLengthCm = 25f;

    [Header("── إعدادات الطلاء ──")]
    public Color paintColor = Color.red;

    // ═══════════════════════════════════════════════════════
    // ثوابت التحويل: 1 Unity Unit = 1 cm
    // ═══════════════════════════════════════════════════════
    private const float CM_TO_M = 0.01f;
    private const float M_TO_CM = 100f;

    // ═══════════════════════════════════════════════════════
    // المحركات
    // ═══════════════════════════════════════════════════════
    private BucketPhysics _physics;
    private PaintEmitter _emitter;
    private CanvasPainter _painter;
    private SPHFluid _sphFluid;
    private SimulationConfig _config;

    private Transform _activeBucket;
    private Transform _activeAttach;
    private LineRenderer _lr;
    private bool _running = false;
    private float _canvasYDynamic;

    public float BucketAngularVelocity { get; private set; }
    public float BucketAngularAcceleration { get; private set; }
    private float _lastTheta;
    private float _lastThetaDot;

    private void Start()
    {
        FixPivotRotation();
        FixCamera();
        BuildConfig();
        InitBucket();
        InitRope();
        InitPhysics();

        _running = true;

        Debug.Log("[SCF] Started! Pivot=" + (pivotPoint?.position.ToString() ?? "NULL"));
        if (_physics != null)
            Debug.Log("[SCF] Period T=" + _physics.GetPeriod().ToString("F3") + "s");
    }

    private void FixedUpdate()
    {
        if (!_running || _physics == null) return;
        float dt = Time.fixedDeltaTime;

        _physics.Step(dt);

        float currentTheta = _physics.Theta;
        float currentThetaDot = _physics.ThetaDot;
        BucketAngularVelocity = (currentTheta - _lastTheta) / dt;
        BucketAngularAcceleration = (currentThetaDot - _lastThetaDot) / dt;
        _lastTheta = currentTheta;
        _lastThetaDot = currentThetaDot;

        MoveBucket();
        UpdateRopeLine();
        HandlePaint(dt);
    }

    private void FixPivotRotation()
    {
        if (pivotPoint == null) return;
        pivotPoint.localRotation = Quaternion.identity;
        Debug.Log("[SCF] PivotPoint World = " + pivotPoint.position);
    }

    private void FixCamera()
    {
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(40f, 52f, -10f);
        cam.transform.LookAt(new Vector3(40f, 26f, 0f)); // ينظر للمنطقة
    }

    private void BuildConfig()
    {
        _config = new SimulationConfig();

        _config.bucket.innerRadius = 0.10f; // 10 cm
        _config.bucket.totalHeight = 0.20f; // 20 cm
        _config.bucket.emptyMass = 0.50f;   // 500 g

        _config.rope.initialLength = ropeLengthCm * CM_TO_M; // 25 cm → 0.25 m

        // ✅ Pivot عند (40.66, 50, 0.004) سم
        _config.rope.pivotPoint = pivotPoint != null
            ? pivotPoint.position * CM_TO_M  // (40.66, 50, 0.004) → (0.4066, 0.5, 0.00004) متر
            : new Vector3(0.4066f, 0.5f, 0.0004f);

        _config.paint.initialHeight = 0.12f; // 12 cm

        Vector3 canvasPosCM = canvasSurface != null 
            ? canvasSurface.position
             : new Vector3(42f, 0.006f, 0f);
        _config.canvas.position = canvasPosCM * CM_TO_M; // cm → m
        _config.canvas.width = 0.08f;  // 80 cm
        _config.canvas.height = 0.08f; // 80 cm

        _config.environment.gravity = 980.665f;
        _config.environment.temperature = 20f;
        _config.environment.humidity = 50f;
        _config.environment.atmosphericPressure = 1013.25f;
        _config.environment.pivotFriction = 0.01f;
        _config.environment.windSpeed = 0f;

        _config.initialAngleDeg = startAngleDeg;
        _config.initialPhiDeg = startPhiDeg;
        _config.initialAngularVelocity = 0f;
    }

    private void InitBucket()
    {
        if (bucketMetal != null) bucketMetal.SetActive(true);
        if (bucketWood != null) bucketWood.SetActive(false);
        _activeBucket = bucketMetal?.transform;

        if (_activeBucket == null)
        {
            Debug.LogError("[SCF] Bucket_Metal not assigned!");
            return;
        }

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
                pivotPoint.position.x + ropeLengthCm * st,
                pivotPoint.position.y - ropeLengthCm * ct,
                pivotPoint.position.z
            );

            _activeBucket.position = attachPos - Vector3.up * 2f;

            var p = _activeBucket.position;
            p.y = Mathf.Max(p.y, 1.5f);
            _activeBucket.position = p;
        }

        Debug.Log("[SCF] Bucket initialized at: " + _activeBucket.position);
    }

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
        _lr.startWidth = 0.3f;
        _lr.endWidth = 0.2f;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.72f, 0.60f, 0.40f);
        _lr.material = mat;
        _lr.startColor = new Color(0.72f, 0.60f, 0.40f);
        _lr.endColor = new Color(0.50f, 0.40f, 0.25f);
    }

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

        _sphFluid = new SPHFluid(
            _config.bucket,
            _config.paint,
            _config.environment,
            particleCount: 50
        );

        _canvasYDynamic = canvasSurface != null
                  ? canvasSurface.position.y
                  : 6f;

        Debug.Log($"[SCF] SPH initialized: {_sphFluid.GetActiveCount()} particles");
        Debug.Log($"[SCF] Canvas Y = {_canvasYDynamic} cm");
    }

    private void MoveBucket()
    {
        if (_activeBucket == null || pivotPoint == null) return;

        Vector3 bucketPosMeters = _physics.BucketPosition;
        Vector3 bucketPosCM = bucketPosMeters * M_TO_CM;

        Vector3 attachPos = pivotPoint.position + bucketPosCM;
        Vector3 bucketPos = attachPos - Vector3.up * 2f;

        bucketPos.y = Mathf.Max(bucketPos.y, 1.5f);
        bucketPos.x = Mathf.Clamp(bucketPos.x, -5f, 85f);
        bucketPos.z = Mathf.Clamp(bucketPos.z, -5f, 45f);

        _activeBucket.position = bucketPos;

        Vector3 vel = _physics.BucketVelocity;
        if (vel.magnitude > 0.05f)
        {
            _activeBucket.rotation = Quaternion.Lerp(
            _activeBucket.rotation,
            Quaternion.Euler(-vel.z * 80f, 0f, vel.x * 80f),
            Time.deltaTime * 4f
        );
        }
    }

    private void UpdateRopeLine()
    {
        if (_lr == null || pivotPoint == null) return;

        _lr.SetPosition(0, pivotPoint.position);
        _lr.SetPosition(1, _activeAttach != null
                               ? _activeAttach.position
                               : _activeBucket != null
                                 // ✅ 2 سم بدل 20 سم
                                 ? _activeBucket.position + Vector3.up * 2f
                                 : pivotPoint.position);
    }

    private void HandlePaint(float dt)
    {
        if (_activeBucket == null || _emitter == null || _sphFluid == null) return;
        if (_sphFluid.IsEmpty()) return;

        // ✅ أرسل موقع الدلو بالسنتيمتر (كما هو في Unity)
        Vector3 bucketPosCM = _activeBucket.position; // (531.6, 263.5, 0.04) سم
        Vector3 bucketVelCM = _physics.BucketVelocity * M_TO_CM; // متر/ث → سم/ث

        _sphFluid.UpdateBucketState(
            bucketPosCM,           // ✅ بالسنتيمتر
            bucketVelCM,           // ✅ بالسنتيمتر/ثانية
            Vector3.zero,
            Vector3.zero
        );

        _sphFluid.Step(dt);

        // ✅ استقبل الجسيمات الخارجة (بالسنتيمتر)
        var exiting = _sphFluid.GetExitingParticles();
        foreach (var sphP in exiting)
        {
            _emitter.EmitFromSPH(sphP.position, sphP.velocity, _canvasYDynamic);
        }

        // ✅ UpdateEmission بالسنتيمتر
        _emitter.UpdateEmission(
            dt,
            _activeBucket.position,              // سم
            _physics.BucketVelocity * M_TO_CM,   // سم/ث
            _physics.CurrentPaintHeight * M_TO_CM, // سم
            _canvasYDynamic                       // سم
        );

        var landed = _emitter.CollectLandedParticles();
        foreach (var p in landed)
        {
            float px = p.LandingPoint.x;
            float pz = p.LandingPoint.z;

            float canvasCenterX = canvasSurface != null ? canvasSurface.position.x : 420f;
            float canvasHalfWidth = canvasSurface != null ? canvasSurface.localScale.x * 0.5f : 25f;
            float canvasMinX = canvasCenterX - canvasHalfWidth;
            float canvasMaxX = canvasCenterX + canvasHalfWidth;

            bool onCanvas = px >= canvasMinX && px <= canvasMaxX &&
                            pz >= -25f && pz <= 25f;

            if (onCanvas)
            {
                _painter.RegisterImpact(p, _config.environment.temperature);
                CreateSplat(p.LandingPoint, p.ParticleColor, p.Velocity.magnitude);
            }
        }

        _painter.Update(dt);
    }

    private void CreateSplat(Vector3 pos, Color col, float speed)
    {
        var go = new GameObject("PaintSplat");

        var meshFilter = go.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateCircleMesh(16); // 16 قطاع

        var renderer = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        if (mat == null || !mat.shader.isSupported)
            mat = new Material(Shader.Find("Standard"));
        mat.color = col;
        renderer.material = mat;

        float r = Mathf.Clamp(speed * 1.3f, 1.5f, 7f);
        go.transform.position = new Vector3(pos.x, _canvasYDynamic + 0.1f, pos.z);
        go.transform.localScale = new Vector3(r, 0.2f, r);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Destroy(go, 180f);
    }

    private Mesh CreateCircleMesh(int segments)
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 2) > segments ? 1 : i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

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
        _lr.startWidth = 3.5f;
        _lr.endWidth = 2f;
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
            $"Area: {(_painter?.PaintedAreaM2 ?? 0) * 10000:F2} cm2\n" +
            $"SPH Particles: {_sphFluid?.GetActiveCount() ?? 0}\n" +
            $"SPH Fill: {(_sphFluid?.GetFillRatio() ?? 0) * 100:F0}%\n";
    }

    public BucketPhysics GetPhysics() => _physics;
    public CanvasPainter GetPainter() => _painter;

    private void OnDestroy()
    {
        _sphFluid?.Dispose();
    }

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
        // ✅ (40.66, 50, 0.004) بدل (406.6, 500, 0.04)
        Vector3 pivot = pivotPoint?.position ?? new Vector3(40.66f, 50f, 0.04f);
        Gizmos.color = Color.green;
        // ✅ 1 سم بدل 10 سم
        Gizmos.DrawWireSphere(pivot, 1f);

        if (_activeBucket != null)
        {
            Gizmos.color = Color.cyan;
            // ✅ 2 سم بدل 20 سم
            Gizmos.DrawLine(pivot, _activeBucket.position + Vector3.up * 2f);
        }

        Gizmos.color = Color.yellow;
        float canvasX = canvasSurface != null ? canvasSurface.position.x : 42f;
        // ✅ 5 سم بدل 25 سم (نصف عرض اللوحة)
        float canvasHalf = canvasSurface != null ? canvasSurface.localScale.x * 0.5f : 2.5f;
        Gizmos.DrawWireCube(new Vector3(canvasX, _canvasYDynamic, 0f),
                            // ✅ 5×5 سم
                            new Vector3(canvasHalf * 2f, 0.1f, 5f));
    }
}