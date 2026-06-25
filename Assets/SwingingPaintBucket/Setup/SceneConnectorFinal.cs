using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SceneConnectorFinal : MonoBehaviour
{
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
    [Range(0f, 70f)] public float startAngleDeg = 45f;
    [Range(0f, 360f)] public float startPhiDeg = 0f;
    [Range(0f, 1f)] public float startPhiAngVelocity = 0.05f;
    [Range(25f, 500f)] public float ropeLengthCm = 35f;

    [Header("── نوع الحبل الابتدائي ──")]
    public RopeMaterial initialRopeMaterial = RopeMaterial.Cotton;

    [Header("── إعدادات الطلاء ──")]
    public Color paintColor = Color.red;

    // ══════════════════════════════════════════════════════════════
    // إعدادات الفتل — الحبل والدلو نظام واحد
    // ══════════════════════════════════════════════════════════════
    [Header("── فيزياء الفتل (Torsional Coupling) ──")]
    [Tooltip("صلابة الحبل ضد الالتواء: Steel أكثر، Cotton أقل (تُضرب بمعامل المادة)")]
    [Range(0f, 5f)] public float ropeStiffness = 0.8f;

    [Tooltip("تخميد الفتل: كبير = يتوقف سريعاً")]
    [Range(0f, 0.5f)] public float twistDamping = 0.05f;

    [Tooltip("قوة الترابط مع الدوران الأفقي: كبير = يفتل أكثر")]
    [Range(0f, 20f)] public float twistCoupling = 5.0f;

    [Tooltip("أقصى زاوية فتل (درجة)")]
    [Range(0f, 360f)] public float maxTwistAngle = 120f;

    [Tooltip("سرعة فتل ابتدائية (rad/s)")]
    [Range(-5f, 5f)] public float initialTwistVelocity = 2.0f;

    // ══════════════════════════════════════════════════════════════
    // إعدادات الحبل البصرية
    // ══════════════════════════════════════════════════════════════
    [Header("── مظهر الحبل الحلزوني ──")]
    [Tooltip("عدد لفّات الحلزون الكاملة على طول الحبل")]
    [Range(0f, 10f)] public float ropeHelixTurns = 2.0f;

    [Tooltip("نصف قطر الحلزون (متر) — كبر الحلزون")]
    [Range(0f, 1f)] public float ropeHelixRadius = 0.15f;

    [Tooltip("عدد نقاط الرسم (أكثر = أنعم)")]
    [Range(8, 40)] public int ropeSegments = 24;

    // ══════════════════════════════════════════════════════════════
    // متغيرات داخلية
    // ══════════════════════════════════════════════════════════════
    private const float SCALE = 1f;

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

    private int _textureUpdateCounter = 0;

    private RopeMaterial _currentRopeMaterial;

    public float BucketAngularVelocity { get; private set; }
    public float BucketAngularAcceleration { get; private set; }
    private float _lastTheta;
    private float _lastThetaDot;

    private ParticleSystem _paintPS;
    private LineRenderer _streamLR;

    // Track last applied SPH particle color to avoid accessing non-existing renderer properties
    private Color _lastSPHColor = Color.clear;

    // ══════════════════════════════════════════════════════════════
    // Start
    // ══════════════════════════════════════════════════════════════
    private void Start()
    {
        _currentRopeMaterial = initialRopeMaterial;
        FixPivotRotation();
        FixCamera();
        BuildConfig();
        InitBucket();
        InitRope(_currentRopeMaterial);
        InitPhysics();
        SetupParticleSystem();
        _running = true;

        Debug.Log("[SCF] Started! Pivot=" + (pivotPoint?.position.ToString() ?? "NULL"));
        Debug.Log($"[SCF] Rope material: {_currentRopeMaterial}");
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

        // Debug الفتل كل ثانية
        if (Mathf.FloorToInt(_physics.SimulationTime) >
            Mathf.FloorToInt(_physics.SimulationTime - dt))
        {
            Debug.Log($"[TWIST] ψ={_physics.PsiDeg:F1}° | " +
                      $"ψ̇={_physics.PsiDot:F2} rad/s | " +
                      $"φ̇={_physics.PhiDot:F3} rad/s");
        }
    }

    private void FixPivotRotation()
    {
        if (pivotPoint == null) return;
        pivotPoint.localRotation = Quaternion.identity;
    }

    private void FixCamera()
    {
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(40f, 35f, -20f);
        cam.transform.LookAt(new Vector3(40f, 10f, 0f));
    }



    private void BuildConfig()
    {
        _config = new SimulationConfig();

        Debug.Log($"[CANVAS-DEBUG] canvasYDynamic={_canvasYDynamic:F2} | " +
          $"canvas.position={_config.canvas.position} | " +
          $"canvas.width={_config.canvas.width:F2} | canvas.height={_config.canvas.height:F2}");




        _config.bucket.innerRadius = 0.10f;
        _config.bucket.totalHeight = 0.20f;
        _config.bucket.emptyMass = 0.50f;

        _config.rope.initialLength = ropeLengthCm;
        _config.rope.material = _currentRopeMaterial;
        _config.rope.pivotPoint = pivotPoint != null
            ? pivotPoint.position
            : new Vector3(0.4066f, 0.5f, 0.0004f);

        _config.paint.initialHeight = 0.18f;

        //Vector3 canvasPosCM = canvasSurface != null
        //    ? canvasSurface.position
        //    : new Vector3(42f, 1f, 0f);
        //_config.canvas.position = canvasPosCM;
        //_config.canvas.width = 5.0f;
        //_config.canvas.height = 5.0f;

        if (canvasSurface != null)
        {
            var rend = canvasSurface.GetComponentInChildren<Renderer>();
           // var rend = canvasSurface.GetComponent<Renderer>();
            if (rend != null)
            {
                Bounds b = rend.bounds;
                _config.canvas.position = b.center;
                _config.canvas.width = b.size.x;
                _config.canvas.height = b.size.z;
                _canvasYDynamic = b.max.y;   // ✅ سطح اللوحة الفعلي (الأعلى)، مش position.y بس
            }
            else
            {
                Debug.LogError("[SCF] ⚠️ canvasSurface ما عنده Renderer ولا بأي child! تحقق من الـ Inspector.");
            }
        }
        else
        {
            Debug.LogError("[SCF] ⚠️ canvasSurface غير معيّن بالـ Inspector!");
        }

    

var streamGO = new GameObject("PaintStream");


        _config.environment.gravity = 9.80665f;
        _config.environment.temperature = 20f;
        _config.environment.humidity = 50f;
        _config.environment.atmosphericPressure = 1013.25f;
        _config.environment.pivotFriction = GetFrictionForMaterial(_currentRopeMaterial);
        _config.environment.windSpeed = 0f;

        _config.initialAngleDeg = startAngleDeg;
        _config.initialPhiDeg = startPhiDeg;

        float omega = Mathf.Sqrt(_config.environment.gravity / _config.rope.initialLength);
        _config.initialAngularVelocity = -omega * 0.4f;

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

    private float GetFrictionForMaterial(RopeMaterial mat)
    {
        switch (mat)
        {
            case RopeMaterial.Cotton: return 0.010f;
            case RopeMaterial.Nylon: return 0.006f;
            case RopeMaterial.Polyester: return 0.008f;
            case RopeMaterial.SteelWire: return 0.015f;
            default: return 0.010f;
        }
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
        if (bvc != null)
        {
            bvc.enabled = false;
            bvc.pivotPoint = pivotPoint;
            bvc.simulationManager = GetComponent<SimulationManager>();
        }

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
                pivotPoint.position.z);
            _activeBucket.position = attachPos - Vector3.up * 2f;
            var p = _activeBucket.position;
            p.y = Mathf.Max(p.y, 4f);
            _activeBucket.position = p;

            Debug.Log($"[BUCKET-DEBUG] bucket spawn Y={_activeBucket.position.y:F2} | " +
          $"pivot Y={(pivotPoint != null ? pivotPoint.position.y : -1):F2}");
        }
    }

    private void InitRope(RopeMaterial mat)
    {
        if (ropeCotton != null) ropeCotton.SetActive(false);
        if (ropeNylon != null) ropeNylon.SetActive(false);
        if (ropeSteel != null) ropeSteel.SetActive(false);

        GameObject ropeGO;
        Color ropeColorStart, ropeColorEnd;
        float startWidth, endWidth;

        switch (mat)
        {
            case RopeMaterial.Nylon:
                ropeGO = ropeNylon;
                ropeColorStart = new Color(0.90f, 0.90f, 0.92f);
                ropeColorEnd = new Color(0.70f, 0.70f, 0.75f);
                startWidth = 0.20f;
                endWidth = 0.15f;
                break;
            case RopeMaterial.SteelWire:
                ropeGO = ropeSteel;
                ropeColorStart = new Color(0.60f, 0.62f, 0.65f);
                ropeColorEnd = new Color(0.40f, 0.42f, 0.45f);
                startWidth = 0.35f;
                endWidth = 0.28f;
                break;
            case RopeMaterial.Polyester:
                ropeGO = ropeCotton;
                ropeColorStart = new Color(0.80f, 0.50f, 0.20f);
                ropeColorEnd = new Color(0.60f, 0.35f, 0.10f);
                startWidth = 0.25f;
                endWidth = 0.18f;
                break;
            default:
                ropeGO = ropeCotton;
                ropeColorStart = new Color(0.72f, 0.60f, 0.40f);
                ropeColorEnd = new Color(0.50f, 0.40f, 0.25f);
                startWidth = 0.30f;
                endWidth = 0.20f;
                break;
        }

        if (ropeGO == null)
        {
            Debug.LogWarning($"[SCF] Rope GameObject for {mat} is not assigned!");
            return;
        }

        ropeGO.SetActive(true);

        var mr = ropeGO.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        _lr = ropeGO.GetComponent<LineRenderer>() ?? ropeGO.AddComponent<LineRenderer>();
        _lr.positionCount = ropeSegments + 2;
        _lr.useWorldSpace = true;
        _lr.startWidth = startWidth;
        _lr.endWidth = endWidth;

        var mat2 = new Material(Shader.Find("Sprites/Default"));
        mat2.color = ropeColorStart;
        _lr.material = mat2;
        _lr.startColor = ropeColorStart;
        _lr.endColor = ropeColorEnd;

        Debug.Log($"[SCF] Rope switched to: {mat}");
    }

    private void InitPhysics()
    {
        _physics = new BucketPhysics(
            _config.bucket, _config.rope,
            _config.paint, _config.environment);

        // نقل الإعدادات
        _physics.ropeStiffness = ropeStiffness;
        _physics.twistDamping = twistDamping;
        _physics.twistCoupling = twistCoupling;
        _physics.maxTwistAngleDeg = maxTwistAngle;
        _physics.initialTwistVelocity = initialTwistVelocity;

        _physics.Initialize(startAngleDeg, startPhiDeg,
                            _config.initialAngularVelocity,
                            startPhiAngVelocity);

        _envCM = new EnvironmentData();
        _envCM.gravity = 9.80665f;
        _envCM.temperature = _config.environment.temperature;
        _envCM.humidity = _config.environment.humidity;
        _envCM.windSpeed = _config.environment.windSpeed;
        _envCM.atmosphericPressure = _config.environment.atmosphericPressure;
        _envCM.pivotFriction = _config.environment.pivotFriction;

        // ✅ تهيئة نظام الجسيمات على GPU
        var gpuSimulator = gameObject.AddComponent<GPULiquidSimulator>();
        gpuSimulator.maxParticleCount = 5000;
        gpuSimulator.particleColor = paintColor;

        // 🎨 إنشاء مادة الرسم للجسيمات
        Material particleMaterial = new Material(Shader.Find("Standard"));
        particleMaterial.name = "PaintParticleMaterial";
        particleMaterial.SetColor("_Color", paintColor);
        particleMaterial.SetColor("_BaseColor", paintColor);
        particleMaterial.SetFloat("_Smoothness", 0.6f);
        particleMaterial.SetFloat("_Metallic", 0.3f);
        gpuSimulator.particleRenderMaterial = particleMaterial;

        var gpuSystem = gameObject.AddComponent<GPUParticleSystem>();
        gpuSystem.gpuSimulator = gpuSimulator;
        gpuSystem.maxActiveParticles = 5000;
        // ✅ ضروري: يجب أن تطابق هذه القيمة بالضبط gravity_cms المحسوبة في
        // PaintEmitter.EmitFromAllHoles (= env.gravity * 100)، وإلا تنقطع
        // استمرارية حركة الجسيم عند الخروج من الثقب (سرعة ابتدائية بمقياس
        // مختلف عن تسارع السقوط اللاحق → حركة متذبذبة وتفرّق غير طبيعي).
        gpuSystem.gravityScene = _envCM.gravity * 100f;

        // أضف PaintEmitter مع GPU support
        _emitter = new PaintEmitter(_config.bucket, _config.paint, _envCM, gpuSystem);
        //_emitter.SetDropletRadius(0.4f);
        _emitter.SetDropletRadius(0.5f);
        //_emitter.SetEmitRate(40f);
        _emitter.SetEmitRate(70f);

        _painter = new CanvasPainter(_config.canvas, _config.paint, _config.environment);
        _sphFluid = new SPHFluid(_config.bucket, _config.paint, _envCM, particleCount: 50);

       // _canvasYDynamic = canvasSurface != null ? canvasSurface.position.y : 1f;

        Debug.Log($"[SCF] Physics init | {_currentRopeMaterial} | " +
                  $"k_rope={ropeStiffness:F2} | coupling={twistCoupling:F1} | " +
                  $"damp={twistDamping:F3}");
        Debug.Log("[SCF] ✅ GPU Liquid Simulator initialized for 3D sphere rendering");
    }

    private void SetupParticleSystem()
    {
        var go = new GameObject("PaintParticleSystem");
        _paintPS = go.AddComponent<ParticleSystem>();
        var emission = _paintPS.emission;
        emission.enabled = false;
        var main = _paintPS.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.2f;
        main.startSpeed = 0f;
        main.startSize = 0.3f;
        main.startColor = paintColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        var rend = _paintPS.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = paintColor;
        rend.material = mat;

        var streamGO = new GameObject("PaintStream");
        _streamLR = streamGO.AddComponent<LineRenderer>();
        var streamMat = new Material(Shader.Find("Sprites/Default"));
        streamMat.color = paintColor;
        _streamLR.material = streamMat;
        _streamLR.startWidth = 0.5f;
        _streamLR.endWidth = 0.3f;
        _streamLR.useWorldSpace = true;
        _streamLR.positionCount = 0;
        _streamLR.numCapVertices = 4;
        _streamLR.startColor = paintColor;
        _streamLR.endColor = new Color(paintColor.r, paintColor.g, paintColor.b, 0.6f);
    }

    // ══════════════════════════════════════════════════════════════
    // MoveBucket — يطبّق موضع ودوران الدلو
    //
    // الدوران يعتمد على ψ (زاوية الفتل المشتركة)
    // الحبل مفتول → يدور الدلو، الدلو يدور → يفتل الحبل
    // ══════════════════════════════════════════════════════════════
    private void MoveBucket()
    {
        if (_activeBucket == null || pivotPoint == null) return;

        // ── الموضع ──
        Vector3 attachPos = pivotPoint.position + _physics.BucketPosition;
        Vector3 bucketPos = attachPos - Vector3.up * 2f;
        bucketPos.y = Mathf.Max(bucketPos.y, 0f);
        _activeBucket.position = bucketPos;

        // ── الدوران: ميل مع الحبل + فتل ψ ──
        Vector3 ropeDir = (pivotPoint.position - _activeBucket.position).normalized;
        if (ropeDir.sqrMagnitude > 0.001f)
        {
            // 1. الميل الأساسي مع الحبل
            Quaternion ropeRot = Quaternion.FromToRotation(Vector3.up, ropeDir);
            Quaternion baseTilt = Quaternion.Slerp(Quaternion.identity, ropeRot, 0.6f);

            // 2. الفتل حول محور الحبل بزاوية ψ
            //    نفس ψ المحسوبة في BucketPhysics
            float psiDeg = _physics.PsiDeg;
            Quaternion twistRot = Quaternion.AngleAxis(psiDeg, ropeDir);

            // 3. الدمج: الفتل يطبَّق فوق الميل
            Quaternion targetRot = twistRot * baseTilt;

            _activeBucket.rotation = Quaternion.Slerp(
                _activeBucket.rotation,
                targetRot,
                Time.deltaTime * 8f);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // UpdateRopeLine — حبل حلزوني يعتمد على ψ مباشرة
    //
    // الفكرة:
    //   • عدد لفّات الحلزون = ropeHelixTurns × (|ψ| / maxψ)
    //   • كلما ازداد الفتل (ψ) → ازدادت لفّات الحلزون
    //   • اتجاه اللفّ يعكس إشارة ψ (+ للحق، - لليسار)
    //   • عند ψ=0 → الحبل مستقيم تماماً
    // ══════════════════════════════════════════════════════════════
    private void UpdateRopeLine()
    {
        if (_lr == null || pivotPoint == null) return;

        Vector3 start = pivotPoint.position;
        Vector3 end = _activeAttach != null
            ? _activeAttach.position
            : _activeBucket != null
                ? _activeBucket.position + Vector3.up * 2f
                : pivotPoint.position;

        int n = ropeSegments + 2;
        _lr.positionCount = n;

        // ── حساب الفتل الفعلي ──
        float psi = _physics.Psi;                          // rad
        float maxPsi = maxTwistAngle * Mathf.Deg2Rad;

        // نسبة الفتل (0..1): تتحكم في عدد اللفّات ونصف القطر
        float twistRatio = Mathf.Abs(psi) / Mathf.Max(maxPsi, 0.001f);
        twistRatio = Mathf.Clamp01(twistRatio);

        // عدد اللفّات الفعلية
        float activeTurns = ropeHelixTurns * twistRatio;

        // نصف قطر الحلزون يتناسب مع الفتل (عند ψ=0 → حبل مستقيم)
        float activeRadius = ropeHelixRadius * twistRatio;

        // اتجاه اللفّ من إشارة ψ
        float twistSign = Mathf.Sign(psi);

        // ── محاور عمودية على الحبل ──
        Vector3 ropeDir = (end - start).normalized;
        Vector3 perp = Vector3.Cross(ropeDir, Vector3.forward).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(ropeDir, Vector3.up).normalized;
        Vector3 perp2 = Vector3.Cross(ropeDir, perp).normalized;

        // ── الزاوية الأولى: تتطابق مع دوران الدلو (ψ) ──
        // هذا يضمن أن الحبل والدلو يبدآن من نفس الاتجاه
        float startAngle = psi;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);      // 0..1 من Pivot إلى الدلو
            Vector3 pos = Vector3.Lerp(start, end, t);

            if (activeRadius > 0.001f)
            {
                // الزاوية تتراكم من Pivot (startAngle) إلى الدلو
                // اللفّات تتوزع على طول الحبل
                float angle = startAngle + twistSign * t * activeTurns * Mathf.PI * 2f;

                // التلاشي: الحلزون يكون واضحاً في المنتصف، يختفي عند الأطراف
                // (Pivot مثبت → لا حركة، الدلو: مصدر الفتل)
                float fade = Mathf.SmoothStep(0f, 1f, t); // يبدأ من 0 عند Pivot

                Vector3 offset = (perp * Mathf.Cos(angle)
                                + perp2 * Mathf.Sin(angle))
                                * activeRadius * fade;
                pos += offset;
            }

            _lr.SetPosition(i, pos);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // HandlePaint
    // ══════════════════════════════════════════════════════════════


    private void HandlePaint(float dt)
    {
        if (_activeBucket == null || _emitter == null) return;

        Vector3 bucketPosM = _activeBucket.position;
        Vector3 bucketVelM = _physics.BucketVelocity;

        // ✅ بدون أي ×100 — هاي القيم أصلاً بنفس وحدة المشهد
        // (نفس الوحدة اللي بيشتغل عليها GPUParticleSystem وSPHFluid)
        Vector3 bucketPosScene = bucketPosM;
        Vector3 bucketVelScene = bucketVelM;

        //if (_sphFluid != null && !_sphFluid.IsEmpty())
        //{
        //    _sphFluid.UpdateBucketState(bucketPosM, bucketVelM, Vector3.zero, Vector3.zero);
        //    _sphFluid.Step(dt);

        //    var exiting = _sphFluid.GetExitingParticles();
        //    foreach (var sphP in exiting)
        //        _emitter.EmitFromSPH(sphP.position, sphP.velocity, _canvasYDynamic);
        //}

        if (_sphFluid != null && !_sphFluid.IsEmpty())
        {
            // ✅ مرّر دوران الدلو الفعلي (نفس اللي مطبّق بـ MoveBucket على _activeBucket.rotation)
            _sphFluid.UpdateBucketState(bucketPosM, bucketVelM, _activeBucket.rotation, Vector3.zero, Vector3.zero);
            _sphFluid.Step(dt);

            var exiting = _sphFluid.GetExitingParticles();
            foreach (var sphP in exiting)
                _emitter.EmitFromSPH(sphP.position, sphP.velocity, _canvasYDynamic);
        }

        float paintHeightM = _physics.CurrentPaintHeight;
        float paintHeightCM = paintHeightM * 100f;   // هاي تبقى ×100 (تحويل أبعاد فعلية، مش موضع)

        // ✅ canvasYCM بدون ×100 — نفس الإطار اللي بيتحرك فيه bucketPosScene
        _emitter.UpdateEmission(dt, bucketPosScene, bucketVelScene, paintHeightCM, _canvasYDynamic);

        // var landed = _emitter.CollectLandedParticles();


        //private void HandlePaint(float dt)
        //{
        //    if (_activeBucket == null || _emitter == null) return;

        //    Vector3 bucketPosM = _activeBucket.position;
        //    Vector3 bucketVelM = _physics.BucketVelocity;

        //    // 🎨 تحويل إلى سنتيمتر
        //    Vector3 bucketPosCM = bucketPosM * 100f;
        //    Vector3 bucketVelCMps = bucketVelM * 100f;

        //    // ✅ تحديث SPH Fluid إذا كان موجود
        //    if (_sphFluid != null && !_sphFluid.IsEmpty())
        //    {
        //        _sphFluid.UpdateBucketState(bucketPosM, bucketVelM, Vector3.zero, Vector3.zero);
        //        _sphFluid.Step(dt);

        //        var exiting = _sphFluid.GetExitingParticles();
        //        foreach (var sphP in exiting)
        //            _emitter.EmitFromSPH(sphP.position, sphP.velocity, _canvasYDynamic);
        //    }

        //    // ✅ أساسي: تحديث الإصدار من الثقوب دائماً
        //    float paintHeightM = _physics.CurrentPaintHeight;
        //    float paintHeightCM = paintHeightM * 100f;  // تحويل من متر إلى سنتيمتر

        //    Debug.Log($"[HandlePaint] Height: {paintHeightM:F4}m = {paintHeightCM:F2}cm, Time: {_physics.SimulationTime:F2}s");

        //    _emitter.UpdateEmission(dt, bucketPosCM, bucketVelCMps, paintHeightCM, _canvasYDynamic * 100f);

        //    var landed = _emitter.CollectLandedParticles();
        //foreach (var p in landed)
        //{
        //    float px = p.LandingPoint.x;
        //    float pz = p.LandingPoint.z;
        //    float cX = canvasSurface != null ? canvasSurface.position.x : 42f;
        //    float cHalf = canvasSurface != null ? canvasSurface.localScale.x * 0.5f : 2.5f;
        //    float cZ = canvasSurface != null ? canvasSurface.position.z : 0f;
        //    float cHalfZ = canvasSurface != null ? canvasSurface.localScale.z * 0.5f : 2.5f;

        //    bool onCanvas = px >= cX - cHalf && px <= cX + cHalf &&
        //                    pz >= cZ - cHalfZ && pz <= cZ + cHalfZ;
        //    if (onCanvas)
        //    {
        //        _painter.RegisterImpact(p, _config.environment.temperature);
        //        CreateSplat(p.LandingPoint, p.ParticleColor, p.Velocity.magnitude);
        //    }
        //}


        var landed = _emitter.CollectLandedParticles();
        foreach (var p in landed)
        {
            bool onCanvas = _painter.RegisterImpact(p, _config.environment.temperature);
            if (onCanvas)
                CreateSplat(p.LandingPoint, p.ParticleColor, p.Velocity.magnitude);
        }
        _painter.Update(dt);

        //// ✅ أضف هذا: طبّق texture اللوحة على Canvas_Surface كل 30 frame
        //_textureUpdateCounter++;
        //if (_textureUpdateCounter >= 30)
        //{
        //    _textureUpdateCounter = 0;
        //    var rend = canvasSurface?.GetComponent<MeshRenderer>();
        //    if (rend != null)
        //        rend.material.mainTexture = _painter.GenerateCanvasTexture();
        //}

        UpdateParticleVisuals();
    }

    //private void UpdateParticleVisuals()
    //{
    //    if (_emitter == null) return;

    //    // ✅ جسيمات PaintEmitter (الطائرة بعد الخروج) — LineRenderer فقط
    //    // الـ SPH داخل الدلو يرسمه SPHParticleRenderer على GPU بشكل كرات 3D
    //    var flyingPositions = new List<Vector3>();

    //    foreach (var p in _emitter.ActiveParticles)
    //    {
    //        if (p.State != ParticleState.Flying) continue;
    //        flyingPositions.Add(p.Position);

    //        // ✅ رسم الجسيمات الطائرةككرات صغيرة عبر GPU Instancing
    //        // (بدل _paintPS.Emit الذي كان يشتغل على CPU)
    //        if (_paintPS != null)
    //        {
    //            var ep = new ParticleSystem.EmitParams();
    //            ep.position = p.Position;
    //            ep.velocity = Vector3.zero;
    //            ep.startSize = 0.30f;
    //            ep.startLifetime = 0.08f;
    //            ep.startColor = paintColor;
    //            _paintPS.Emit(ep, 1);
    //        }
    //    }

    //    // تيار الطلاء (LineRenderer)
    //    if (_streamLR != null)
    //    {
    //        _streamLR.positionCount = flyingPositions.Count;
    //        for (int i = 0; i < flyingPositions.Count; i++)
    //            _streamLR.SetPosition(i, flyingPositions[i]);
    //    }

    //    // ✅ تحديث لون SPHParticleRenderer إذا تغيّر
    //    var sphRenderer = GetComponent<SPHParticleRenderer>();
    //    if (sphRenderer != null && _lastSPHColor != paintColor)
    //    {
    //        sphRenderer.SetColor(paintColor);
    //        _lastSPHColor = paintColor;
    //    }
    //}



    private void UpdateParticleVisuals()
    {
        if (_emitter == null) return;

        var flyingPositions = new List<Vector3>();

        // احتياطي CPU (نادراً يكون فيه شي لما USE_GPU=true)
        foreach (var p in _emitter.ActiveParticles)
        {
            if (p.State != ParticleState.Flying) continue;
            flyingPositions.Add(p.Position);
        }

        // ✅ المصدر الحقيقي: الجزيئات الطايرة فعلياً على GPU الآن
        var gpuSystem = _emitter.GPUSystem;
        if (gpuSystem != null)
            gpuSystem.FillActivePositionsOrdered(flyingPositions);

        if (_streamLR != null)
        {
            
            _streamLR.positionCount = flyingPositions.Count;
            for (int i = 0; i < flyingPositions.Count; i++)
                _streamLR.SetPosition(i, flyingPositions[i]);
        }

        var sphRenderer = GetComponent<SPHParticleRenderer>();
        if (sphRenderer != null && _lastSPHColor != paintColor)
        {
            sphRenderer.SetColor(paintColor);
            _lastSPHColor = paintColor;
        }
    }
    private void CreateSplat(Vector3 pos, Color col, float speed)
    {
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
        //Destroy(main, 180f);

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
                pos.z + Mathf.Sin(angle) * dist);
            splash.transform.localScale = new Vector3(sr, 0.1f, sr);
            splash.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            //Destroy(splash, 180f);
        }
    }

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

    // ══════════════════════════════════════════════════════════════
    // Public API
    // ══════════════════════════════════════════════════════════════
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
            if (bvc != null)
            {
                bvc.enabled = false;
                bvc.pivotPoint = pivotPoint;
                bvc.simulationManager = GetComponent<SimulationManager>();
            }
        }
        _activeAttach = FindDeep(_activeBucket, "RopeAttachPoint");
    }

    public void SwitchRope(int idx)
    {
        RopeMaterial newMat;
        switch (idx)
        {
            case 1: newMat = RopeMaterial.Nylon; break;
            case 2: newMat = RopeMaterial.SteelWire; break;
            case 3: newMat = RopeMaterial.Polyester; break;
            default: newMat = RopeMaterial.Cotton; break;
        }

        _currentRopeMaterial = newMat;

        // حفظ حالة الفتل
        float savedPsi = _physics?.Psi ?? 0f;
        float savedPsiDot = _physics?.PsiDot ?? 0f;

        InitRope(newMat);
        _config.rope.material = newMat;
        _config.environment.pivotFriction = GetFrictionForMaterial(newMat);

        if (_physics != null)
        {
            float currentTheta = _physics.Theta;
            float currentPhi = _physics.Phi;
            float currentThetaDot = _physics.ThetaDot;
            float currentPhiDot = _physics.PhiDot;

            _physics = new BucketPhysics(
                _config.bucket, _config.rope,
                _config.paint, _config.environment);

            _physics.ropeStiffness = ropeStiffness;
            _physics.twistDamping = twistDamping;
            _physics.twistCoupling = twistCoupling;
            _physics.maxTwistAngleDeg = maxTwistAngle;
            _physics.initialTwistVelocity = initialTwistVelocity;

            _physics.Initialize(
                currentTheta * Mathf.Rad2Deg,
                currentPhi * Mathf.Rad2Deg,
                currentThetaDot,
                currentPhiDot);

            // استعادة الفتل
            _physics.SetTwistState(savedPsi, savedPsiDot);
        }

        Debug.Log($"[SCF] Rope → {newMat} | ψ={savedPsi * Mathf.Rad2Deg:F1}°");
    }

    public RopeMaterial GetCurrentRopeMaterial() => _currentRopeMaterial;

    public void Restart()
    {
        foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (g != null && g.name is "PaintSplat" or "Splash") Destroy(g);
        BuildConfig();
        InitPhysics();
        InitBucket();
        InitRope(_currentRopeMaterial);
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
            $"Rope:    {_currentRopeMaterial}\n" +
            $"Swings:  {_physics.SwingCount}\n" +
            $"KE:      {_physics.GetKineticEnergy():F4} J\n" +
            $"Tension: {_physics.GetRopeTension():F2} N\n" +
            $"Period:  {_physics.GetPeriod():F3} s\n" +
            $"Twist ψ: {_physics.PsiDeg:F1}°\n" +
            $"Twist ω: {_physics.PsiDot:F2} rad/s\n" +
            $"Paths:   {_painter?.TotalPathCount ?? 0}\n" +
            $"Area:    {(_painter?.PaintedAreaM2 ?? 0) * 10000:F2} cm²\n" +
            $"SPH:     {_sphFluid?.GetActiveCount() ?? 0}\n" +
            $"Fill:    {(_sphFluid?.GetFillRatio() ?? 0) * 100:F0}%\n" +
            $"Emitted: {_emitter?.TotalEmittedCount ?? 0}\n";
    }

    public BucketPhysics Physics => _physics;
    public BucketPhysics GetPhysics() => _physics;
    public CanvasPainter GetPainter() => _painter;
    public SPHFluid GetSPHFluid() => _sphFluid;   // ✅ للـ SPHParticleRenderer

    private void OnDestroy() => _sphFluid?.Dispose();

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
            new Vector3(canvasHalf * 2f, 0.1f, 5f));
    }
}

