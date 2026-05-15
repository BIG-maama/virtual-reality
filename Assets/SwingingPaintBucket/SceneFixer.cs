//using UnityEngine;
//using System.Collections.Generic;

///// <summary>
///// النسخة المصححة الكاملة
///// تحل: السطل تحت الأرض + طول الحبل + الكاميرا + الطلاء
///// </summary>
//public class SceneFixer : MonoBehaviour
//{
//    [Header("=== الكائنات الأساسية ===")]
//    public Transform crossbar;
//    public Transform pivotPoint;
//    public GameObject bucketMetal;
//    public GameObject bucketWood;
//    public GameObject ropeCotton;
//    public GameObject ropeNylon;
//    public GameObject ropeSteel;
//    public Transform canvasSurface;
//    public Camera mainCamera;

//    [Header("=== إعدادات البندول ===")]
//    [Range(5f, 60f)]
//    public float startAngleDeg = 25f;

//    [Range(0.5f, 4f)]
//    public float ropeLength = 2.0f;     // ← قلّلنا من 3.5 إلى 2.0

//    [Range(0.01f, 0.3f)]
//    public float damping = 0.05f;

//    [Header("=== إعدادات الطلاء ===")]
//    public Color paintColor = Color.red;
//    [Range(5f, 30f)]
//    public float dropsPerSecond = 15f;

//    // ===== متغيرات داخلية =====
//    private Transform _activeBucket;
//    private Transform _activeAttach;
//    private LineRenderer _lr;
//    private bool _running = false;

//    // فيزياء البندول
//    private float _theta;
//    private float _thetaDot;
//    private float _phi = 0f;
//    private float _phiDot = 0f;

//    // جزيئات الطلاء
//    private struct Drop
//    {
//        public Vector3 pos;
//        public Vector3 vel;
//        public Color col;
//        public float age;
//        public bool alive;
//    }
//    private List<Drop> _drops = new List<Drop>();
//    private float _dropTimer = 0f;

//    // حدود الأرضية والغرفة (من البيانات)
//    private float _floorY = 0.06f;   // Canvas_Surface Y
//    private float _roomMinX = -4.85f;
//    private float _roomMaxX = 9.85f;
//    private float _roomMinZ = -4.85f;
//    private float _roomMaxZ = 4.95f;

//    // =========================================================
//    private void Start()
//    {
//        FixPivotPoint();
//        PlaceBucketCorrectly();
//        SetupRopeRenderer();
//        FixCamera();
//        BeginSimulation();
//    }

//    private void FixedUpdate()
//    {
//        if (!_running) return;
//        float dt = Time.fixedDeltaTime;
//        StepPhysics(dt);
//        MoveBucket();
//        DrawRope();
//        SpawnDrops(dt);
//        MoveDrops(dt);
//    }

//    // =========================================================
//    // 1. إصلاح PivotPoint - نقطة التعليق الحقيقية
//    // =========================================================
//    private void FixPivotPoint()
//    {
//        if (pivotPoint == null || crossbar == null) return;

//        // Crossbar: Pos=(3,6,0.04), Rot=X90, Scale=(0.1,4.9,0.1)
//        // نريد PivotPoint في أعلى الـ Crossbar (رأس العمود)
//        // بما أن Crossbar مائل 90 درجة، محور Y الخاص به هو Z في الفضاء العالمي
//        // نضع PivotPoint بـ LocalPosition = (0, 0, 0) ثم نصحح الـ World Y

//        pivotPoint.localPosition = Vector3.zero;
//        pivotPoint.localRotation = Quaternion.identity;

//        // نتأكد أن الـ Y أعلى من الأرضية بما يكفي
//        // PivotPoint World = (3, 6, 0.04) - هذا صح
//        Debug.Log("[SceneFixer] PivotPoint at: " + pivotPoint.position);
//    }

//    // =========================================================
//    // 2. وضع السطل في المكان الصحيح عند البداية
//    // =========================================================
//    private void PlaceBucketCorrectly()
//    {
//        if (bucketMetal != null) bucketMetal.SetActive(true);
//        if (bucketWood != null) bucketWood.SetActive(false);

//        _activeBucket = bucketMetal?.transform;
//        if (_activeBucket == null) return;

//        // تعطيل BucketVisualController
//        var bvc = _activeBucket.GetComponent<BucketVisualController>();
//        if (bvc != null) bvc.enabled = false;

//        // تعطيل TrailRenderer
//        var tr = _activeBucket.GetComponent<TrailRenderer>();
//        if (tr != null) tr.enabled = false;

//        // إيجاد نقطة ربط الحبل
//        _activeAttach = FindChild(_activeBucket, "RopeAttachPoint");

//        // وضع السطل عند موضعه الابتدائي الصحيح
//        // PivotPoint=(3,6,0.04), RopeLength=2 → السطل عند Y=4 تقريباً
//        if (pivotPoint != null)
//        {
//            float startTheta = startAngleDeg * Mathf.Deg2Rad;
//            Vector3 startOffset = new Vector3(
//                ropeLength * Mathf.Sin(startTheta),
//               -ropeLength * Mathf.Cos(startTheta),
//                0f
//            );
//            _activeBucket.position = pivotPoint.position + startOffset;
//        }

//        Debug.Log("[SceneFixer] Bucket placed at: " + _activeBucket.position);
//    }

//    // =========================================================
//    // 3. إعداد LineRenderer للحبل
//    // =========================================================
//    private void SetupRopeRenderer()
//    {
//        if (ropeCotton == null) return;

//        if (ropeCotton != null) ropeCotton.SetActive(true);
//        if (ropeNylon != null) ropeNylon.SetActive(false);
//        if (ropeSteel != null) ropeSteel.SetActive(false);

//        // إخفاء Mesh الحبل
//        var mr = ropeCotton.GetComponent<MeshRenderer>();
//        if (mr != null) mr.enabled = false;

//        // إعداد LineRenderer
//        _lr = ropeCotton.GetComponent<LineRenderer>();
//        if (_lr == null) _lr = ropeCotton.AddComponent<LineRenderer>();

//        _lr.positionCount = 2;
//        _lr.useWorldSpace = true;
//        _lr.startWidth = 0.04f;
//        _lr.endWidth = 0.02f;
//        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

//        var mat = new Material(Shader.Find("Sprites/Default"));
//        mat.color = new Color(0.75f, 0.65f, 0.45f);
//        _lr.material = mat;
//        _lr.startColor = new Color(0.75f, 0.65f, 0.45f);
//        _lr.endColor = new Color(0.55f, 0.45f, 0.30f);
//    }

//    // =========================================================
//    // 4. تصحيح موضع الكاميرا لترى المشهد كاملاً
//    // =========================================================
//    private void FixCamera()
//    {
//        if (mainCamera == null)
//            mainCamera = Camera.main;
//        if (mainCamera == null) return;

//        // الغرفة مركزها X=2.5, Y=3, Z=0
//        // نضع الكاميرا أمام الغرفة قليلاً
//        mainCamera.transform.position = new Vector3(2.5f, 4.0f, -6.5f);
//        mainCamera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

//        Debug.Log("[SceneFixer] Camera repositioned to see the room.");
//    }

//    // =========================================================
//    // 5. بدء المحاكاة
//    // =========================================================
//    private void BeginSimulation()
//    {
//        _theta = startAngleDeg * Mathf.Deg2Rad;
//        _thetaDot = 0f;
//        _phi = 0f;
//        _phiDot = 0f;
//        _running = true;
//        Debug.Log("[SceneFixer] Simulation started!");
//    }

//    // =========================================================
//    // 6. خطوة فيزياء البندول (معادلات لاغرانج)
//    // =========================================================
//    private void StepPhysics(float dt)
//    {
//        float g = 9.80665f;
//        float L = ropeLength;
//        float sinT = Mathf.Sin(_theta);
//        float cosT = Mathf.Cos(_theta);
//        float cotT = Mathf.Abs(sinT) > 0.001f ? cosT / sinT : 0f;

//        // θ̈ = φ̇²·sinθ·cosθ − (g/L)·sinθ − b·θ̇
//        float tDD = _phiDot * _phiDot * sinT * cosT
//                  - (g / L) * sinT
//                  - damping * _thetaDot;

//        // φ̈ = −2·θ̇·φ̇·cotθ − b·φ̇
//        float pDD = -2f * _thetaDot * _phiDot * cotT
//                  - damping * _phiDot;

//        _thetaDot += tDD * dt;
//        _phiDot += pDD * dt;
//        _theta += _thetaDot * dt;
//        _phi += _phiDot * dt;
//    }

//    // =========================================================
//    // 7. تحريك السطل
//    // x = Pivot.x + L·sin(θ)·cos(φ)
//    // y = Pivot.y − L·cos(θ)        ← لا يتجاوز الأرضية
//    // z = Pivot.z + L·sin(θ)·sin(φ)
//    // =========================================================
//    private void MoveBucket()
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

//        // منع السطل من النزول تحت الأرضية
//        float minY = _floorY + 0.15f;
//        if (by < minY)
//        {
//            by = minY;
//            // ارتداد بسيط عند الأرضية
//            if (_thetaDot < 0f) _thetaDot = -_thetaDot * 0.3f;
//        }

//        // منع الخروج من الغرفة
//        bx = Mathf.Clamp(bx, _roomMinX + 0.2f, _roomMaxX - 0.2f);
//        bz = Mathf.Clamp(bz, _roomMinZ + 0.2f, _roomMaxZ - 0.2f);

//        _activeBucket.position = new Vector3(bx, by, bz);
//    }

//    // =========================================================
//    // 8. رسم الحبل بين PivotPoint والسطل
//    // =========================================================
//    private void DrawRope()
//    {
//        if (_lr == null || pivotPoint == null || _activeBucket == null) return;
//        _lr.SetPosition(0, pivotPoint.position);
//        _lr.SetPosition(1, _activeAttach != null
//                           ? _activeAttach.position
//                           : _activeBucket.position + Vector3.up * 0.2f);
//    }

//    // =========================================================
//    // 9. إصدار قطرات الطلاء
//    // =========================================================
//    private void SpawnDrops(float dt)
//    {
//        if (_activeBucket == null) return;
//        _dropTimer += dt;
//        float interval = 1f / dropsPerSecond;
//        if (_dropTimer < interval) return;
//        _dropTimer = 0f;

//        Vector3 bPos = _activeBucket.position;
//        Vector3 bVel = GetBucketVelocity();

//        // سرعة خروج الطلاء للأسفل (تورتشيلي h=0.1m)
//        float vExit = Mathf.Sqrt(2f * 9.80665f * 0.1f) * 0.7f;

//        _drops.Add(new Drop
//        {
//            pos = bPos + Vector3.down * 0.12f,
//            vel = bVel + new Vector3(
//                        Random.Range(-0.08f, 0.08f),
//                       -vExit,
//                        Random.Range(-0.08f, 0.08f)),
//            col = paintColor,
//            age = 0f,
//            alive = true
//        });
//    }

//    // =========================================================
//    // 10. تحريك القطرات والاصطدام باللوحة
//    // =========================================================
//    private void MoveDrops(float dt)
//    {
//        float g = 9.80665f;
//        float canvasY = _floorY;

//        for (int i = _drops.Count - 1; i >= 0; i--)
//        {
//            Drop d = _drops[i];
//            if (!d.alive) { _drops.RemoveAt(i); continue; }

//            // جاذبية + مقاومة هواء خفيفة
//            d.vel += Vector3.down * g * dt;
//            d.vel *= (1f - 0.02f * dt);
//            d.pos += d.vel * dt;
//            d.age += dt;

//            // اصطدام باللوحة
//            if (d.pos.y <= canvasY)
//            {
//                // تأكد أن القطرة داخل حدود اللوحة
//                // Canvas_Surface: X=3.2, Z=0, Scale=0.8
//                // نطاق اللوحة: X ∈ [2.8, 3.6], Z ∈ [-0.4, 0.4]
//                if (d.pos.x >= 2.4f && d.pos.x <= 4.0f &&
//                    d.pos.z >= -0.8f && d.pos.z <= 0.8f)
//                {
//                    CreateSplat(d.pos, d.col, d.vel.magnitude);
//                }
//                d.alive = false;
//            }

//            if (d.age > 6f) d.alive = false;
//            _drops[i] = d;
//        }
//    }

//    // =========================================================
//    // 11. إنشاء بقعة الطلاء على اللوحة
//    // =========================================================
//    private void CreateSplat(Vector3 pos, Color col, float speed)
//    {
//        GameObject splat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
//        splat.name = "Splat";
//        Destroy(splat.GetComponent<CapsuleCollider>());

//        // حجم البقعة يعتمد على السرعة
//        float r = Mathf.Clamp(speed * 0.012f, 0.015f, 0.08f);
//        splat.transform.position = new Vector3(pos.x, _floorY + 0.001f, pos.z);
//        splat.transform.localScale = new Vector3(r, 0.002f, r);

//        var rend = splat.GetComponent<Renderer>();
//        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
//        if (mat == null) mat = new Material(Shader.Find("Standard"));
//        // لون البقعة مع تعديل بسيط عشوائي
//        Color splatCol = new Color(
//            Mathf.Clamp01(col.r + Random.Range(-0.05f, 0.05f)),
//            Mathf.Clamp01(col.g + Random.Range(-0.05f, 0.05f)),
//            Mathf.Clamp01(col.b + Random.Range(-0.05f, 0.05f))
//        );
//        mat.color = splatCol;
//        rend.material = mat;

//        Destroy(splat, 120f);
//    }

//    // =========================================================
//    // سرعة السطل الحالية
//    // =========================================================
//    private Vector3 GetBucketVelocity()
//    {
//        float L = ropeLength;
//        float sinT = Mathf.Sin(_theta);
//        float cosT = Mathf.Cos(_theta);
//        float sinP = Mathf.Sin(_phi);
//        float cosP = Mathf.Cos(_phi);
//        return new Vector3(
//            L * (_thetaDot * cosT * cosP - _phiDot * sinT * sinP),
//            L * _thetaDot * sinT,
//            L * (_thetaDot * cosT * sinP + _phiDot * sinT * cosP)
//        );
//    }

//    // =========================================================
//    // واجهة عامة للـ UI
//    // =========================================================
//    public void SetPaintColor(Color c) => paintColor = c;
//    public void SetStartAngle(float deg) { startAngleDeg = deg; }
//    public void SetRopeLength(float len) { ropeLength = Mathf.Clamp(len, 0.5f, 4f); }

//    public void RestartSimulation()
//    {
//        _drops.Clear();
//        // حذف البقع
//        foreach (var s in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
//            if (s != null && s.name == "Splat") Destroy(s);
//        BeginSimulation();
//    }

//    public void SwitchToBucket(bool metal)
//    {
//        bucketMetal?.SetActive(metal);
//        bucketWood?.SetActive(!metal);
//        _activeBucket = metal
//            ? bucketMetal?.transform
//            : bucketWood?.transform;
//        if (_activeBucket != null)
//        {
//            var bvc = _activeBucket.GetComponent<BucketVisualController>();
//            if (bvc != null) bvc.enabled = false;
//            var tr = _activeBucket.GetComponent<TrailRenderer>();
//            if (tr != null) tr.enabled = false;
//        }
//        _activeAttach = FindChild(_activeBucket, "RopeAttachPoint");
//    }

//    // =========================================================
//    // Gizmos - فقط في Editor
//    // =========================================================
//    private void OnDrawGizmosSelected()
//    {
//        if (pivotPoint == null) return;
//        Gizmos.color = Color.green;
//        Gizmos.DrawWireSphere(pivotPoint.position, 0.08f);
//        if (_activeBucket != null)
//        {
//            Gizmos.color = Color.cyan;
//            Gizmos.DrawLine(pivotPoint.position, _activeBucket.position);
//        }
//    }

//    // =========================================================
//    // دالة مساعدة للبحث عن Child
//    // =========================================================
//    private Transform FindChild(Transform parent, string childName)
//    {
//        if (parent == null) return null;
//        foreach (Transform c in parent)
//        {
//            if (c.name == childName) return c;
//            var found = FindChild(c, childName);
//            if (found != null) return found;
//        }
//        return null;
//    }
//}