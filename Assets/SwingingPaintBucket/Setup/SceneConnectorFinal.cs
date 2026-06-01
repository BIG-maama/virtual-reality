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

    [Header("── UI Panels ──")]
    public RectTransform panelLeft;
    public RectTransform panelRight;
    public RectTransform panelStats;
    public RectTransform panelBottom;
    public RectTransform panelReport;

    [Header("── إعدادات البندول ──")]
    [Range(5f, 70f)] public float startAngleDeg = 45f;
    [Range(0f, 360f)] public float startPhiDeg = 0f;
    [Range(25f, 500f)] public float ropeLengthCm = 35f;

    [Header("── إعدادات الطلاء ──")]
    public Color paintColor = Color.red;

    // ── ثوابت التحويل: 1 Unity Unit = 1 cm ──
    private const float CM_TO_M = 0.01f;
    private const float M_TO_CM = 100f;

    // ── المحركات ──
    private BucketPhysics _physics;
    private PaintEmitter _emitter;
    private CanvasPainter _painter;
    private SPHFluid _sphFluid;
    private SimulationConfig _config;
    private EnvironmentData _envCM;

    private Transform _activeBucket;
    private Transform _activeAttach;
    private LineRenderer _lr;
    private bool _running = false;
    private float _canvasYDynamic;

    public float BucketAngularVelocity { get; private set; }
    public float BucketAngularAcceleration { get; private set; }
    private float _lastTheta;
    private float _lastThetaDot;

    // ── Particle visuals ──
    private ParticleSystem _paintPS;

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
        SetupParticleSystem();

        _running = true;

        Debug.Log("[SCF] Started! Pivot=" + (pivotPoint?.position.ToString() ?? "NULL"));
        if (_physics != null)
            Debug.Log("[SCF] Period T=" + _physics.GetPeriod().ToString("F3") + "s");
    }

    // ═══════════════════════════════════════════════════════
    // FixedUpdate
    // ═══════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════
    // FixPivotRotation / FixCamera
    // ═══════════════════════════════════════════════════════
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
        cam.transform.position = new Vector3(40f, 35f, -20f);
        cam.transform.LookAt(new Vector3(40f, 10f, 0f));
    }

    // ═══════════════════════════════════════════════════════
    // BuildConfig
    // ═══════════════════════════════════════════════════════
    private void BuildConfig()
    {
        _config = new SimulationConfig();

        _config.bucket.innerRadius = 0.10f;
        _config.bucket.totalHeight = 0.20f;
        _config.bucket.emptyMass = 0.50f;

        // طول الحبل: ropeLengthCm وحدة Unity مباشرة → متر فيزيائي
        _config.rope.initialLength = ropeLengthCm * CM_TO_M;

        _config.rope.pivotPoint = pivotPoint != null
            ? pivotPoint.position * CM_TO_M
            : new Vector3(0.4066f, 0.5f, 0.0004f);

        _config.paint.initialHeight = 0.18f;

        Vector3 canvasPosCM = canvasSurface != null
            ? canvasSurface.position
            : new Vector3(42f, 1f, 0f);
        _config.canvas.position = canvasPosCM * CM_TO_M;
        _config.canvas.width = 5.0f;
        _config.canvas.height = 5.0f;

        // جاذبية BucketPhysics بالمتر
        _config.environment.gravity = 9.80665f;
        _config.environment.temperature = 20f;
        _config.environment.humidity = 50f;
        _config.environment.atmosphericPressure = 1013.25f;
        _config.environment.pivotFriction = 0.01f;
        _config.environment.windSpeed = 0f;

        _config.initialAngleDeg = startAngleDeg;
        _config.initialPhiDeg = startPhiDeg;
        _config.initialAngularVelocity = 0f;

        // ثقب صغير — تدفق خفيف وقوة معقولة
        _config.bucket.holes.Clear();
        _config.bucket.holes.Add(new HoleData
        {
            shape = HoleShape.Circular,
            radius = 0.005f,
            heightFromBottom = 0.0f,
            angularPosition = 0f,
            dischargeCoefficient = 0.7f
        });
    }

    // ═══════════════════════════════════════════════════════
    // InitBucket
    // ═══════════════════════════════════════════════════════
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
            p.y = Mathf.Max(p.y, 4f);
            _activeBucket.position = p;
        }

        Debug.Log("[SCF] Bucket at: " + _activeBucket.position);
    }

    // ═══════════════════════════════════════════════════════
    // InitRope
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
        _lr.startWidth = 0.3f;
        _lr.endWidth = 0.2f;

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.72f, 0.60f, 0.40f);
        _lr.material = mat;
        _lr.startColor = new Color(0.72f, 0.60f, 0.40f);
        _lr.endColor = new Color(0.50f, 0.40f, 0.25f);
    }

    // ═══════════════════════════════════════════════════════
    // InitPhysics
    // ═══════════════════════════════════════════════════════
    private void InitPhysics()
    {
        _physics = new BucketPhysics(
            _config.bucket, _config.rope,
            _config.paint, _config.environment
        );
        _physics.Initialize(startAngleDeg, startPhiDeg, 0f);

        // env بالسنتيمتر للـ emitter و SPH
        _envCM = new EnvironmentData();
        _envCM.gravity = 9.80665f;   // بطيء = مرئي بوضوح
        _envCM.temperature = _config.environment.temperature;
        _envCM.humidity = _config.environment.humidity;
        _envCM.windSpeed = _config.environment.windSpeed;
        _envCM.atmosphericPressure = _config.environment.atmosphericPressure;
        _envCM.pivotFriction = _config.environment.pivotFriction;

        _emitter = new PaintEmitter(_config.bucket, _config.paint, _envCM);
        _emitter.SetDropletRadius(0.3f);   // نقطة صغيرة
        _emitter.SetEmitRate(20f);

        _painter = new CanvasPainter(_config.canvas, _config.paint, _config.environment);

        _sphFluid = new SPHFluid(_config.bucket, _config.paint, _envCM, particleCount: 50);

        _canvasYDynamic = canvasSurface != null ? canvasSurface.position.y : 1f;

        Debug.Log($"[SCF] Canvas Y={_canvasYDynamic}  SPH={_sphFluid.GetActiveCount()}");
    }

    // ═══════════════════════════════════════════════════════
    // SetupParticleSystem — نقاط صغيرة مع trail
    // ═══════════════════════════════════════════════════════
    private void SetupParticleSystem()
    {
        var go = new GameObject("PaintParticleSystem");
        _paintPS = go.AddComponent<ParticleSystem>();

        var emission = _paintPS.emission;
        emission.enabled = false;

        var main = _paintPS.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 4f;
        main.startSpeed = 0f;
        main.startSize = 0.25f;       // نقطة صغيرة
        main.startColor = paintColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;          // نحن نتحكم يدوياً

        var trails = _paintPS.trails;
        trails.enabled = true;
        trails.lifetime = 0.4f;
        trails.minVertexDistance = 0.2f;
        trails.widthOverTrail = 0.15f;

        var renderer = _paintPS.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = paintColor;
        renderer.material = mat;
        renderer.trailMaterial = mat;
    }

    // ═══════════════════════════════════════════════════════
    // UpdateParticleVisuals — يحدّث موضع كل نقطة
    // ═══════════════════════════════════════════════════════
    private void UpdateParticleVisuals()
    {
        if (_paintPS == null || _emitter == null) return;

        foreach (var p in _emitter.ActiveParticles)
        {
            if (p.State != ParticleState.Flying) continue;

            var ep = new ParticleSystem.EmitParams();
            ep.position = p.Position;
            ep.velocity = p.Velocity * 0.02f;  // بطيء ومرئي
            ep.startSize = 0.25f;
            ep.startLifetime = 0.4f;
            ep.startColor = paintColor;

            _paintPS.Emit(ep, 1);
        }
    }

    // ═══════════════════════════════════════════════════════
    // MoveBucket
    // ═══════════════════════════════════════════════════════
    private void MoveBucket()
    {
        if (_activeBucket == null || pivotPoint == null) return;

        Vector3 bucketPosCM = _physics.BucketPosition * M_TO_CM;
        Vector3 attachPos = pivotPoint.position + bucketPosCM;
        Vector3 bucketPos = attachPos - Vector3.up * 2f;

        bucketPos.y = Mathf.Max(bucketPos.y, 4f);
        bucketPos.x = Mathf.Clamp(bucketPos.x, -5f, 85f);
        bucketPos.z = Mathf.Clamp(bucketPos.z, -5f, 45f);

        _activeBucket.position = bucketPos;

        Vector3 vel = _physics.BucketVelocity;
        if (vel.magnitude > 0.05f)
        {
            _activeBucket.rotation = Quaternion.Lerp(
                _activeBucket.rotation,
                Quaternion.Euler(-vel.z * 60f, 0f, vel.x * 60f),
                Time.deltaTime * 3f
            );
        }
    }

    // ═══════════════════════════════════════════════════════
    // UpdateRopeLine
    // ═══════════════════════════════════════════════════════
    private void UpdateRopeLine()
    {
        if (_lr == null || pivotPoint == null) return;
        _lr.SetPosition(0, pivotPoint.position);
        _lr.SetPosition(1, _activeAttach != null
            ? _activeAttach.position
            : _activeBucket != null
                ? _activeBucket.position + Vector3.up * 2f
                : pivotPoint.position);
    }

    // ═══════════════════════════════════════════════════════
    // HandlePaint
    // ═══════════════════════════════════════════════════════
    private void HandlePaint(float dt)
    {
        if (_activeBucket == null || _emitter == null || _sphFluid == null) return;
        if (_sphFluid.IsEmpty()) return;

        Vector3 bucketPosCM = _activeBucket.position;
        Vector3 bucketVelCM = _physics.BucketVelocity * M_TO_CM;

        _sphFluid.UpdateBucketState(bucketPosCM, bucketVelCM, Vector3.zero, Vector3.zero);
        _sphFluid.Step(dt);

        var exiting = _sphFluid.GetExitingParticles();
        foreach (var sphP in exiting)
            _emitter.EmitFromSPH(sphP.position, sphP.velocity, _canvasYDynamic);

        float paintHeightCM = _physics.CurrentPaintHeight * M_TO_CM;
        _emitter.UpdateEmission(dt, bucketPosCM, bucketVelCM, paintHeightCM, _canvasYDynamic);

        // جسيمات وصلت اللوحة
        var landed = _emitter.CollectLandedParticles();
        foreach (var p in landed)
        {
            float px = p.LandingPoint.x;
            float pz = p.LandingPoint.z;
            float cX = canvasSurface != null ? canvasSurface.position.x : 42f;
            float cHalf = canvasSurface != null ? canvasSurface.localScale.x * 0.5f : 2.5f;
            float cZ = canvasSurface != null ? canvasSurface.position.z : 0f;
            float cHalfZ = canvasSurface != null ? canvasSurface.localScale.z * 0.5f : 2.5f;

            bool onCanvas = px >= cX - cHalf && px <= cX + cHalf &&
                            pz >= cZ - cHalfZ && pz <= cZ + cHalfZ;

            if (onCanvas)
            {
                _painter.RegisterImpact(p, _config.environment.temperature);
                CreateSplat(p.LandingPoint, p.ParticleColor, p.Velocity.magnitude);
            }
        }

        _painter.Update(dt);
        UpdateParticleVisuals();
    }

    // ═══════════════════════════════════════════════════════
    // CreateSplat — بقعة + طرطشة
    // ═══════════════════════════════════════════════════════
    private void CreateSplat(Vector3 pos, Color col, float speed)
    {
        // البقعة الرئيسية
        var main = new GameObject("PaintSplat");
        var mf = main.AddComponent<MeshFilter>();
        mf.mesh = CreateCircleMesh(12);
        var mr = main.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = col;
        mr.material = mat;

        float r = Mathf.Clamp(speed * 0.08f, 0.2f, 1.5f);
        main.transform.position = new Vector3(pos.x, _canvasYDynamic + 0.05f, pos.z);
        main.transform.localScale = new Vector3(r, 0.1f, r);
        main.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        Destroy(main, 180f);

        // طرطشة حول البقعة
        int splashCount = Mathf.Clamp((int)(speed * 0.3f), 2, 6);
        for (int i = 0; i < splashCount; i++)
        {
            var splash = new GameObject("Splash");
            var smf = splash.AddComponent<MeshFilter>();
            smf.mesh = CreateCircleMesh(8);
            var smr = splash.AddComponent<MeshRenderer>();
            var smat = new Material(Shader.Find("Sprites/Default"));
            smat.color = col;
            smr.material = smat;

            float angle = i * (360f / splashCount) * Mathf.Deg2Rad;
            float dist = Random.Range(0.2f, r * 1.5f);
            float sr = Random.Range(0.05f, 0.15f);

            splash.transform.position = new Vector3(
                pos.x + Mathf.Cos(angle) * dist,
                _canvasYDynamic + 0.05f,
                pos.z + Mathf.Sin(angle) * dist
            );
            splash.transform.localScale = new Vector3(sr, 0.1f, sr);
            splash.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Destroy(splash, 180f);
        }
    }

    // ═══════════════════════════════════════════════════════
    // CreateCircleMesh
    // ═══════════════════════════════════════════════════════
    private Mesh CreateCircleMesh(int segments)
    {
        var mesh = new Mesh();
        var vertices = new Vector3[segments + 1];
        var triangles = new int[segments * 3];

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

    // ═══════════════════════════════════════════════════════
    // Public API
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
        _lr.startWidth = 0.3f;
        _lr.endWidth = 0.2f;
    }

    public void Restart()
    {
        foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (g != null && g.name is "PaintSplat" or "Splash") Destroy(g);
        BuildConfig();
        InitPhysics();
        InitBucket();
    }

    public string GetStatsText()
    {
        if (_physics == null) return "Not running";
        return
            $"Time:    {_physics.SimulationTime:F1}s\n" +
            $"Theta:   {_physics.Theta * Mathf.Rad2Deg:F1}°\n" +
            $"Mass:    {_physics.CurrentMass * 1000:F1} g\n" +
            $"Paint h: {_physics.CurrentPaintHeight * 100:F1} cm\n" +
            $"Rope L:  {_physics.CurrentRopeLength:F3} m\n" +
            $"Swings:  {_physics.SwingCount}\n" +
            $"KE:      {_physics.GetKineticEnergy():F4} J\n" +
            $"Tension: {_physics.GetRopeTension():F2} N\n" +
            $"Period:  {_physics.GetPeriod():F3} s\n" +
            $"Paths:   {_painter?.TotalPathCount ?? 0}\n" +
            $"Area:    {(_painter?.PaintedAreaM2 ?? 0) * 10000:F2} cm²\n" +
            $"SPH:     {_sphFluid?.GetActiveCount() ?? 0}\n" +
            $"Fill:    {(_sphFluid?.GetFillRatio() ?? 0) * 100:F0}%\n" +
            $"Emitted: {_emitter?.TotalEmittedCount ?? 0}\n";
    }

    public BucketPhysics GetPhysics() => _physics;
    public CanvasPainter GetPainter() => _painter;

    private void OnDestroy() => _sphFluid?.Dispose();

    // ═══════════════════════════════════════════════════════
    // Helpers
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
        Vector3 pivot = pivotPoint?.position ?? new Vector3(40.66f, 50f, 0.04f);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pivot, 1f);

        if (_activeBucket != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pivot, _activeBucket.position + Vector3.up * 2f);
        }

        Gizmos.color = Color.yellow;
        float canvasX = canvasSurface != null ? canvasSurface.position.x : 42f;
        float canvasHalf = canvasSurface != null ? canvasSurface.localScale.x * 0.5f : 2.5f;
        Gizmos.DrawWireCube(
            new Vector3(canvasX, _canvasYDynamic, 0f),
            new Vector3(canvasHalf * 2f, 0.1f, 5f)
        );
    }
}