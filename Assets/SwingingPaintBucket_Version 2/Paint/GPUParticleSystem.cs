using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// نظام الجسيمات المحسّن — يربط PaintEmitter مع الرسم على GPU
/// 
/// المهام:
///   ✅ استقبال جسيمات من PaintEmitter (CPU)
///   ✅ تحديث بيانات الجسيمات
///   ✅ رسم الجسيمات كـ 3D spheres على GPU
///   ✅ إعادة استخدام الجسيمات بكفاءة
/// </summary>
public class GPUParticleSystem : MonoBehaviour
{
    [Header("── المراجع ──")]
    [Tooltip("مرجع الكائن الذي يحتوي على GPULiquidSimulator")]
    public GPULiquidSimulator gpuSimulator;

    [Header("── إعدادات الأداء ──")]
    [Range(100, 15000)]
    public int maxActiveParticles = 10000;

    [Header("── الفيزياء ──")]
    [Tooltip("تسارع الجاذبية بنفس وحدة المشهد المستخدمة لموضع الجسيمات. " +
             "⚠️ يجب أن يطابق بالضبط القيمة المحسوبة في " +
             "PaintEmitter.EmitFromAllHoles (gravity_cms = env.gravity * 100) " +
             "وإلا تنقطع استمرارية حركة الجسيم لحظة الخروج من الثقب: " +
             "السرعة الابتدائية (Torricelli) ستكون محسوبة بمقياس مختلف عن " +
             "تسارع السقوط اللاحق، فتظهر الحركة متذبذبة وغير متّسقة. " +
             "القيمة الافتراضية هنا (980.665) تطابق env.gravity=9.80665 الشائعة.")]
    public float gravityScene = 9.80665f;

    [Header("── تماسك الجسيمات (Cohesion) ──")]
    [Tooltip("نطاق التماسك بنفس وحدة المشهد (سنتيمتر تقريباً، نفس مقياس " +
             "نصف قطر الدلو والثقب). مثال: لو نصف قطر الدلو ~9 وحدات، فنطاق " +
             "تماسك معقول هو 2-4 وحدات حول الجسيم.")]
    public float cohesionRadius = 0.5f;
    //public float cohesionRadius = 6f;

    [Tooltip("قوة التماسك")]
    public float cohesionStrength = 2.0f;
    //public float cohesionStrength = 2.5f;

    [Tooltip("أقصى عدد جسيمات يُفحص بينها تماسك كل فريم (للأداء)")]
    public int cohesionCheckLimit = 50; // تقليل العمل على CPU
    //public int cohesionCheckLimit = 200;

    // ═══════════════════════════════════════════════════════════
    // داخلي
    // ═══════════════════════════════════════════════════════════

    private class GPUParticleData
    {
        public Vector3 Position;      // سنتيمتر
        public Vector3 Velocity;      // سنتيمتر/ثانية
        public float Density;         // kg/cm³
        public float Mass;            // kg
        public Color Color;           // لون الجسيم
        public bool IsActive;         // نشط أم لا
        public float LifeTime;        // العمر بالثواني
        public long SeqId;   // ✅ جديد — رقم تسلسلي يحدد ترتيب الانبعاث الحقيقي
    }


    private List<GPUParticleData> _particles = new List<GPUParticleData>();
    private List<int> _activeIndices = new List<int>();
    private Stack<int> _freeIndices = new Stack<int>();
    private long _seqCounter = 0;   // ✅ جديد

    private Matrix4x4[] _matrices;
    private MaterialPropertyBlock _mpb;
    private bool _initialized = false;
    private float _maxLifeTime = 5f;

    // ✅ هذا كان الناقص: لا يوجد أي اكتشاف "هبوط" للجسيمات القادمة من GPU
    // فكانت تسقط للأبد بدون أي تسجيل طلاء على اللوحة حتى تنتهي حياتها (LifeTime)
    // وتختفي بصمت دون رسم أي splat.
    public struct LandedGPUParticle
    {
        public Vector3 position; // cm
        public Vector3 velocity; // cm/s
        public Color color;
    }
    private List<LandedGPUParticle> _landedBuffer = new List<LandedGPUParticle>();

    // Unity's hard limit per DrawMeshInstanced call
    private const int MAX_INSTANCED_PER_CALL = 1023;

    // ═══════════════════════════════════════════════════════════
    // Start
    // ═══════════════════════════════════════════════════════════

    private void Start()
    {
        if (_initialized) return;
        if (gpuSimulator == null)
            gpuSimulator = GetComponent<GPULiquidSimulator>();
        ForceInit();
        // ✅ إلزامي: Graphics.DrawMeshInstanced يرمي Exception إذا المادة
        // ما كانت enableInstancing = true (هذا سبب الخطأ
        // "Material needs to enable instancing for use with DrawMeshInstanced")
        if (gpuSimulator != null && gpuSimulator.RenderMaterial != null)
            gpuSimulator.RenderMaterial.enableInstancing = true;

        InitializeBuffers();
        _initialized = true;
        _matrices = new Matrix4x4[Mathf.Min(maxActiveParticles, MAX_INSTANCED_PER_CALL)];
        _mpb = new MaterialPropertyBlock();

        Debug.Log($"[GPUParticleSystem] ✅ Initialized with max {maxActiveParticles} particles");
    }

    // ═══════════════════════════════════════════════════════════
    // تهيئة الـ Buffers
    // ═══════════════════════════════════════════════════════════

    private void InitializeBuffers()
    {
        _particles.Clear();
        _activeIndices.Clear();
        _freeIndices.Clear();

        for (int i = 0; i < maxActiveParticles; i++)
        {
            _particles.Add(new GPUParticleData
            {
                Position = Vector3.zero,
                Velocity = Vector3.zero,
                Density = 1000f,
                Mass = 0.02f,
                Color = Color.red,
                IsActive = false,
                LifeTime = 0f
            });
            _freeIndices.Push(i);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // إضافة جسيم جديد
    // ═══════════════════════════════════════════════════════════
    public void ForceInit()
    {
        if (_initialized) return;
        if (gpuSimulator != null && gpuSimulator.RenderMaterial != null)
            gpuSimulator.RenderMaterial.enableInstancing = true;
        InitializeBuffers();
        _initialized = true;
        _matrices = new Matrix4x4[Mathf.Min(maxActiveParticles, MAX_INSTANCED_PER_CALL)];
        _mpb = new MaterialPropertyBlock();
        Debug.Log("[GPUParticleSystem] ForceInit done");
    }
    public bool EmitParticle(Vector3 positionCM, Vector3 velocityCMps,
                             Color color, float radiusCM, float densityKgCm3)
    {
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[EMIT-STATUS] initialized={_initialized} | " +
                      $"active={_activeIndices.Count} | max={maxActiveParticles} | " +
                      $"freeSlots={_freeIndices.Count}");
        if (!_initialized || _activeIndices.Count >= maxActiveParticles)
            return false;

        int index;
        if (_freeIndices.Count > 0)
            index = _freeIndices.Pop();
        else
            return false;

        // حساب الكتلة: m = ρ · (4/3)πr³
        float mass = densityKgCm3 * (4f / 3f) * Mathf.PI
                     * radiusCM * radiusCM * radiusCM;

        var particle = _particles[index];
        particle.Position = positionCM;
        particle.Velocity = velocityCMps;
        particle.Density = densityKgCm3;
        particle.Mass = Mathf.Max(mass, 1e-6f);
        particle.Color = color;
        particle.IsActive = true;
        particle.LifeTime = 0f;
        particle.SeqId = _seqCounter++;   // ✅ جديد

        _activeIndices.Add(index);

        //if (Time.frameCount % 60 == 0)  // طباعة كل ثانية تقريباً، تجنّب فيضان الـ Console
        //    Debug.Log($"[GPU-EMIT-DEBUG] spawnY={positionCM.y:F2}");

        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // ✅ جديد: التقاط الجسيمات التي عبرت مستوى اللوحة (هبطت)
    //
    // المشكلة القديمة: الجسيمات على GPU كانت تسقط بالجاذبية للأبد
    // ولا أحد كان يفحص إذا وصلت لليوحة → فلا يُسجَّل أي طلاء أبداً
    // على CanvasPainter ولا يُرسم أي splat — حتى لو كانت الكرات
    // ظاهرة وهي طايرة.
    //
    // هذا الميثود يلتقط كل جسيم Position.y <= canvasYCM، يخرجه من
    // قائمة النشطين (يتوقف عن الرسم كـ كرة طايرة) ويرجّع بياناته
    // لـ PaintEmitter ليُحوَّل لـ PaintParticle "هابط" ويُسجَّل على اللوحة.
    // ═══════════════════════════════════════════════════════════
    public List<LandedGPUParticle> CollectLanded(float canvasYCM)
    {
        _landedBuffer.Clear();

        for (int i = _activeIndices.Count - 1; i >= 0; i--)
        {
            int idx = _activeIndices[i];
            var p = _particles[idx];

            if (!p.IsActive) continue;
            if (p.Position.y > canvasYCM) continue;

            _landedBuffer.Add(new LandedGPUParticle
            {
                position = p.Position,
                velocity = p.Velocity,
                color = p.Color
            });

            // أخرجه من الجسيمات النشطة (ما عاد يُرسم كـ كرة طايرة)
            p.IsActive = false;
            _activeIndices.RemoveAt(i);
            _freeIndices.Push(idx);
        }

        return _landedBuffer;
    }

    // ═══════════════════════════════════════════════════════════
    // تحديث البيانات والرسم
    // ═══════════════════════════════════════════════════════════

    //private void FixedUpdate()
    //{
    //    if (!_initialized || _activeIndices.Count == 0)
    //        return;

    //    UpdateParticleData();
    //}

    private void LateUpdate()
    {
        if (!_initialized || gpuSimulator == null) return;

        UpdateParticleData();

        if (Time.frameCount % 120 == 0)
            Debug.Log($"[GPU-STATUS] initialized={_initialized} | " +
                      $"activeParticles={_activeIndices.Count} | " +
                      $"meshNull={gpuSimulator?.SphereMesh == null} | " +
                      $"matNull={gpuSimulator?.RenderMaterial == null} | " +
                      $"matInstancing={gpuSimulator?.RenderMaterial?.enableInstancing}");

        if (_activeIndices.Count == 0) return;

        RenderParticles();
    }
    private void UpdateParticleData()
    {
        float dt = Time.deltaTime;

        // ✅ تماسك أولاً (قبل تطبيق الجاذبية) — هذا كان غائباً كلياً
        // عن مسار GPU، فالجسيمات الطائرة بعد الخروج من الثقب كانت
        // كل واحدة تتحرك لحالها بدون أي قوة تجاذب بينها، فتتفرّق
        // فوراً وتبدو "كل وحدة لحالها" بدل أن تتجمّع كتيار طلاء واحد.
        ApplyCohesionForces();
        int n = _activeIndices.Count;
        for (int i = _activeIndices.Count - 1; i >= 0; i--)
        {
            int idx = _activeIndices[i];
            var p = _particles[idx];

            if (!p.IsActive)
            {
                _activeIndices.RemoveAt(i);
                _freeIndices.Push(idx);
                continue;
            }

            // زيادة العمر
            p.LifeTime += dt;

            // إذا انتهى العمر، أزل الجسيم
            if (p.LifeTime > _maxLifeTime)
            {
                p.IsActive = false;
                _activeIndices.RemoveAt(i);
                _freeIndices.Push(idx);
                continue;
            }

            // ✅ الجاذبية بنفس وحدة المشهد المستخدمة لموضع/سرعة الجسيم
            // (لا 980.665 الثابتة التي كانت تفترض "سنتيمتر" بينما الموضع
            // الفعلي أصلاً بوحدة المشهد الموحّدة مع موضع الدلو)
            p.Velocity.y -= gravityScene * dt * 4f;

            // حد أقصى للسرعة (بنفس وحدة المشهد/ثانية)
            //float maxSpeedScene = gravityScene * 0.6f; // نسبي ومتّسق مع مقياس الجاذبية الفعلي
            float maxSpeedScene = 100f; // رفع الحد الأقصى للسرعة

            if (p.Velocity.magnitude > maxSpeedScene)
                p.Velocity = p.Velocity.normalized * maxSpeedScene;

            // تحديث الموضع
            p.Position += p.Velocity * dt;

            // تخميد خفيف
            p.Velocity *= 0.98f;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ✅ جديد: تماسك بين الجسيمات الطائرة (نفس مبدأ
    // PaintParticle.ComputeCohesionForce الأصلي، لكن مطبّق هنا
    // لأن مسار GPU لا يمر بـ PaintParticle.AddExternalForce إطلاقاً)
    // ═══════════════════════════════════════════════════════════
    //private void ApplyCohesionForces()
    //{
    //    int limit = Mathf.Min(_activeIndices.Count, cohesionCheckLimit);
    //    float eq = cohesionRadius * 0.4f;

    //    for (int i = 0; i < limit; i++)
    //    {
    //        int idxA = _activeIndices[i];
    //        var a = _particles[idxA];
    //        if (!a.IsActive) continue;

    //        for (int j = i + 1; j < limit; j++)
    //        {
    //            int idxB = _activeIndices[j];
    //            var b = _particles[idxB];
    //            if (!b.IsActive) continue;

    //            Vector3 diff = b.Position - a.Position;
    //            float dist = diff.magnitude;
    //            if (dist < 0.001f || dist > cohesionRadius) continue;

    //            float mag = dist < eq
    //                ? -cohesionStrength * (eq - dist) / (dist + 0.001f) * 3f
    //                : cohesionStrength * (dist - eq) / cohesionRadius;

    //            Vector3 dir = diff / dist;
    //            Vector3 f = dir * mag;

    //            // قوة متساوية ومعاكسة (نفس مبدأ نيوتن الثالث، مطابق
    //            // لمنطق ComputeCohesionForce الأصلي في PaintParticle)
    //            a.Velocity += f * Time.fixedDeltaTime;
    //            b.Velocity -= f * Time.fixedDeltaTime;
    //        }
    //    }
    //}


    //private void ApplyCohesionForces()
    //{
    //    int n = _activeIndices.Count;
    //    if (n < 2) return;

    //    // ✅ نافذة منزلقة: كل جسيم يتفحص مع الجسيمات التالية له بالقائمة
    //    // (مش فقط أول 150) — هيك الجسيمات الجديدة المُصدرة توّاً تدخل بالحساب كمان
    //    int windowSize = Mathf.Min(cohesionCheckLimit, n - 1);
    //    float eq = cohesionRadius * 0.4f;
    //    // ✅ حد أدنى آمن للمسافة - يمنع الانفجار العددي وقت تولد جسيمتين بنفس النقطة
    //    float minSafeDist = cohesionRadius * 0.08f;

    //    for (int i = 0; i < n; i++)
    //    {
    //        int idxA = _activeIndices[i];
    //        var a = _particles[idxA];
    //        if (!a.IsActive) continue;

    //        int jEnd = Mathf.Min(i + windowSize, n - 1);
    //        for (int j = i + 1; j <= jEnd; j++)
    //        {
    //            int idxB = _activeIndices[j];
    //            var b = _particles[idxB];
    //            if (!b.IsActive) continue;

    //            Vector3 diff = b.Position - a.Position;
    //            float dist = diff.magnitude;
    //            if (dist > cohesionRadius || dist < 0.0001f) continue;

    //            float safeDist = Mathf.Max(dist, minSafeDist);
    //            float mag;

    //            if (safeDist < eq)
    //            {
    //                // تنافر "ناعم" (تربيعي) بدل القسمة على مسافة قريبة من الصفر
    //                float t = (eq - safeDist) / eq;        // 0..1
    //                mag = -cohesionStrength * t * t * 2f;   // محدود، ما بينفجر
    //            }
    //            else
    //            {
    //                float t = (safeDist - eq) / (cohesionRadius - eq + 0.0001f);
    //                mag = cohesionStrength * t;
    //            }

    //            Vector3 dir = diff / dist;
    //            Vector3 f = dir * mag;

    //            a.Velocity += f * Time.fixedDeltaTime;
    //            b.Velocity -= f * Time.fixedDeltaTime;
    //        }
    //    }
    //}

    private void ApplyCohesionForces()
    {
        int n = _activeIndices.Count;
        if (n < 2) return;

        int windowSize = Mathf.Min(cohesionCheckLimit, n - 1);
        float eq = cohesionRadius * 0.4f;
        float minSafeDist = cohesionRadius * 0.08f; // ✅ يمنع الانفجار العددي

        for (int i = 0; i < n; i++)
        {
            int idxA = _activeIndices[i];
            var a = _particles[idxA];
            if (!a.IsActive) continue;

            int jEnd = Mathf.Min(i + windowSize, n - 1);
            for (int j = i + 1; j <= jEnd; j++)
            {
                int idxB = _activeIndices[j];
                var b = _particles[idxB];
                if (!b.IsActive) continue;

                Vector3 diff = b.Position - a.Position;
                float dist = diff.magnitude;
                if (dist > cohesionRadius || dist < 0.0001f) continue;

                float safeDist = Mathf.Max(dist, minSafeDist);

                float mag;



                if (safeDist < eq)
                {
                    float t = (eq - safeDist) / eq;
                    mag = -cohesionStrength * t * t * 2f;   // ✅ تنافر ناعم محدود
                }
                else
                {
                    float t = (safeDist - eq) / (cohesionRadius - eq + 0.0001f);
                    mag = cohesionStrength * t;
                }
                float speedFactor = Mathf.Clamp01(a.Velocity.magnitude / 2f);
                mag *= speedFactor;
                Vector3 dir = diff / dist;
                Vector3 f = dir * mag;

                a.Velocity += f * Time.fixedDeltaTime;
                b.Velocity -= f * Time.fixedDeltaTime;
            }
        }
    }
    private void RenderParticles()
    {
        if (gpuSimulator.SphereMesh == null || gpuSimulator.RenderMaterial == null)
            return;

        _mpb.SetColor("_BaseColor", gpuSimulator.particleColor);
        _mpb.SetColor("_Color", gpuSimulator.particleColor);

        int total = _activeIndices.Count;

        // ✅ Graphics.DrawMeshInstanced بيقبل بحد أقصى 1023 instance لكل نداء
        // (كان الكود القديم يحاول يرسلهم كلهم برسمة واحدة → Exception
        //  لو تجاوز عدد الجسيمات النشطة 1023)
        for (int start = 0; start < total; start += MAX_INSTANCED_PER_CALL)
        {
            int count = Mathf.Min(MAX_INSTANCED_PER_CALL, total - start);

            for (int i = 0; i < count; i++)
            {
                int idx = _activeIndices[start + i];
                var p = _particles[idx];

                // ✅ إصلاح: p.Position محسوبة بالفعل بنفس وحدة المشهد
                // (نفس وحدة Transform.position للدلو)، فلا حاجة لأي قسمة/ضرب
                // هنا. القسمة ×0.01 القديمة كانت تفترض أن وحدة Unity = متر
                // بينما المشروع يعمل بوحدة واحدة موحّدة لكل من الدلو والجسيمات.
                // إن طبّقنا القسمة هنا فقط (دون تطبيقها في مكان توليد السرعة/الجاذبية)
                // تنتج حركة غير متّسقة: الموضع يبدو صحيحاً لحظياً لكن سرعة الانتقال
                // بين الفريمات تكون 100× أسرع من المفروض، فتتفرّق الجسيمات فوراً
                // ولا يبقى أي تماسك بينها (وهذا تحديداً ما كان يظهر بصرياً).
                _matrices[i] = Matrix4x4.TRS(
    p.Position,
    Quaternion.identity,
    Vector3.one * gpuSimulator.sphereScale
);
            }

            Graphics.DrawMeshInstanced(
                gpuSimulator.SphereMesh,
                0,
                gpuSimulator.RenderMaterial,
                _matrices,
                count,
                _mpb,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                false,
                0
            );
        }
    }


    // ═══════════════════════════════════════════════════════════
    // 
    // ═══════════════════════════════════════════════════════════
    /// <summary>
    /// يملأ القائمة بمواضع الجسيمات الطايرة الآن، بترتيب الانبعاث تقريباً
    /// (الأقدم أولاً) — تُستخدم لرسم خط/تيار متصل يربط بينها.
    /// </summary>
    //public void FillActivePositionsOrdered(List<Vector3> result)
    //{
    //    for (int i = 0; i < _activeIndices.Count; i++)
    //    {
    //        int idx = _activeIndices[i];
    //        var p = _particles[idx];
    //        if (!p.IsActive) continue;
    //        result.Add(p.Position);
    //    }
    //} 

    public void FillActivePositionsOrdered(List<Vector3> result)
    {
        // ✅ رتّب نسخة من المؤشرات حسب SeqId الحقيقي (وقت الانبعاث)
        // بدل الاعتماد على ترتيب _activeIndices اللي بيتأثر بإعادة استخدام الـ slots
        var sorted = new List<int>(_activeIndices);
        sorted.Sort((ia, ib) => _particles[ia].SeqId.CompareTo(_particles[ib].SeqId));

        foreach (int idx in sorted)
        {
            var p = _particles[idx];
            if (!p.IsActive) continue;
            result.Add(p.Position);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Getters
    // ═══════════════════════════════════════════════════════════

    public int ActiveCount => _activeIndices.Count;

    public void ClearAllParticles()
    {
        foreach (var idx in _activeIndices)
        {
            _particles[idx].IsActive = false;
            _freeIndices.Push(idx);
        }
        _activeIndices.Clear();
    }
    //private void OnDrawGizmos()
    //{
    //    if (_activeIndices == null || _particles == null) return;

    //    Gizmos.color = Color.yellow;
    //    foreach (int idx in _activeIndices)
    //    {
    //        if (idx >= _particles.Count) continue;
    //        var p = _particles[idx];
    //        if (!p.IsActive) continue;
    //        Gizmos.DrawWireSphere(p.Position, 0.5f);
    //    }
    //}
}