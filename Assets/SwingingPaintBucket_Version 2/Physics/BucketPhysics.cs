using UnityEngine;

/// <summary>
/// BucketPhysics — بندول كروي مع فيزياء فتل حقيقية (Torsional Coupling)
///
/// المبدأ الفيزيائي:
///   • الحبل والدلو نظام واحد مترابط (Torsional Coupling)
///   • الدوران الأفقي φ̇ يولّد عزماً يفتل الحبل (ψ)
///   • الحبل المفتول يحاول العودة بصلابته (k_rope)
///   • الدلو يدور بنفس ψ حول محور الحبل
///
/// النتيجة: تفتل الدلو ↔ تفتل الحبل — علاقة ثنائية الاتجاه
/// </summary>
public class BucketPhysics
{
    // ══════════════════════════════════════════════════════════════
    // الحالة الأساسية للبندول الكروي
    // ══════════════════════════════════════════════════════════════
    private float _theta;       // زاوية الميل عن الشاقول (rad)
    private float _phi;         // زاوية الدوران الأفقي (rad)
    private float _thetaDot;
    private float _phiDot;

    private readonly BucketData _bucket;
    private readonly RopeData _rope;
    private readonly PaintData _paint;
    private readonly EnvironmentData _env;

    private float _currentPaintHeight;
    private float _currentRopeLength;
    private float _simulationTime;
    private int _swingCount;
    private float _lastPhi;

    // ══════════════════════════════════════════════════════════════
    // ① نظام الفتل (Torsion) — الحبل والدلو معاً
    // ══════════════════════════════════════════════════════════════

    // ψ  = زاوية الفتل المشتركة بين الحبل والدلو
    // ψ̇  = سرعة الفتل (rad/s)
    // ψ̈  = تسارع الفتل
    private float _psi;
    private float _psiDot;

    [Tooltip("صلابة الفتل للحبل: كبيرة = حبل صلب يرجع بسرعة")]
    public float ropeStiffness = 0.8f;   // k_rope (N·m/rad) — صلابة الحبل تجاه الفتل

    [Tooltip("تخميد الفتل: كبير = يتوقف سريعاً")]
    public float twistDamping = 0.05f;  // c (N·m·s/rad)

    [Tooltip("قوة الترابط: φ̇ كبير → فتل قوي")]
    public float twistCoupling = 5.0f;   // كيف φ̇ يفتل الحبل

    [Tooltip("أقصى فتل مسموح (درجة)")]
    public float maxTwistAngleDeg = 120f;

    [Tooltip("فتل ابتدائي (rad/s)")]
    public float initialTwistVelocity = 2.0f;

    // للتوافق مع SceneConnectorFinal القديم
    public float twistStiffness
    {
        get => ropeStiffness;
        set => ropeStiffness = value;
    }

    // ── خصائص عامة للفتل ──
    public float Psi => _psi;
    public float PsiDeg => _psi * Mathf.Rad2Deg;
    public float PsiDot => _psiDot;

    // ══════════════════════════════════════════════════════════════
    // ② نظام النايلون (مرونة + زحف)
    // ══════════════════════════════════════════════════════════════
    private float _creepExt;
    private float _elasticExt;
    private float _elasticExtDot;

    private const float K_STATIC = 20f;
    private const float C_VISCOUS = 0.3f;
    private const float CREEP_RATE = 0.00008f;
    private const float CREEP_MAX = 0.06f;

    // ══════════════════════════════════════════════════════════════
    // خصائص عامة
    // ══════════════════════════════════════════════════════════════
    public float Theta => _theta;
    public float Phi => _phi;
    public float ThetaDot => _thetaDot;
    public float PhiDot => _phiDot;
    public float CurrentPaintHeight => _currentPaintHeight;
    public float CurrentRopeLength => _currentRopeLength;
    public float SimulationTime => _simulationTime;
    public int SwingCount => _swingCount;
    public float CurrentMass => _bucket.GetTotalMass(_currentPaintHeight, _paint.Density);
    public float ElasticExtension => _elasticExt;
    public float CreepExtension => _creepExt;

    private bool IsNylon => _rope.material == RopeMaterial.Nylon;

    // ── معامل التخميد الزاوي ──
    private float GetDampingCoeff()
    {
        float m = CurrentMass;
        return (m > 0.001f) ? _env.pivotFriction / m : _env.pivotFriction;
    }

    // ── الطول الفعّال مع مرونة النايلون ──
    private float GetEffectiveLength()
    {
        float chi = _bucket.GetChiDistance(_currentPaintHeight, _paint.Density);
        if (!IsNylon) return _currentRopeLength + chi;
        return _currentRopeLength + chi + _elasticExt + _creepExt;
    }

    // ── موضع الدلو في الفضاء ──
    public Vector3 BucketPosition
    {
        get
        {
            float sT = Mathf.Sin(_theta);
            float cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi);
            float cP = Mathf.Cos(_phi);
            float L = GetEffectiveLength();
            return new Vector3(L * sT * cP, -L * cT, L * sT * sP);
        }
    }

    // ── سرعة الدلو ──
    public Vector3 BucketVelocity
    {
        get
        {
            float sT = Mathf.Sin(_theta);
            float cT = Mathf.Cos(_theta);
            float sP = Mathf.Sin(_phi);
            float cP = Mathf.Cos(_phi);
            float L = GetEffectiveLength();
            float vx = L * (_thetaDot * cT * cP - _phiDot * sT * sP);
            float vy = L * _thetaDot * sT;
            float vz = L * (_thetaDot * cT * sP + _phiDot * sT * cP);

            if (IsNylon && _elasticExtDot != 0f)
            {
                Vector3 ropeDir = new Vector3(sT * cP, -cT, sT * sP);
                return new Vector3(vx, vy, vz) + ropeDir * _elasticExtDot;
            }
            return new Vector3(vx, vy, vz);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════════
    public BucketPhysics(BucketData bucket, RopeData rope,
                         PaintData paint, EnvironmentData env)
    {
        _bucket = bucket;
        _rope = rope;
        _paint = paint;
        _env = env;
    }

    // ══════════════════════════════════════════════════════════════
    // Initialize
    // ══════════════════════════════════════════════════════════════
    public void Initialize(float initialAngleDeg, float initialPhiDeg,
                           float initialAngVelocity,
                           float initialPhiAngVelocity = 0f)
    {
        _theta = initialAngleDeg * Mathf.Deg2Rad;
        _phi = initialPhiDeg * Mathf.Deg2Rad;
        _thetaDot = initialAngVelocity;
        _phiDot = initialPhiAngVelocity;

        _currentPaintHeight = _paint.initialHeight;
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);
        _simulationTime = 0f;
        _swingCount = 0;
        _lastPhi = _phi;
        _creepExt = 0f;

        // ── فتل ابتدائي ──
        // _psi = 0 لأن الحبل يبدأ غير مفتول
        // _psiDot = initialTwistVelocity: يعطي لفّة أولى واضحة
        _psi = 0f;
        _psiDot = initialTwistVelocity;

        if (IsNylon)
        {
            float m0 = CurrentMass;
            float cosT = Mathf.Cos(_theta);
            _elasticExt = Mathf.Max(0f, m0 * _env.gravity * cosT / K_STATIC);
            _elasticExtDot = 0f;
        }
        else
        {
            _elasticExt = 0f;
            _elasticExtDot = 0f;
        }

        Debug.Log($"[BucketPhysics] Init | Material={_rope.material} | " +
                  $"θ={initialAngleDeg:F1}° | γ={GetDampingCoeff():F5} | " +
                  $"ψ̇₀={initialTwistVelocity:F2} rad/s | " +
                  $"k_rope={ropeStiffness:F2} | coupling={twistCoupling:F1}");
    }

    // ── تعيين حالة الفتل مباشرة (عند تبديل الحبل) ──
    public void SetTwistState(float psiRad, float psiDotRad)
    {
        _psi = psiRad;
        _psiDot = psiDotRad;
    }

    // ══════════════════════════════════════════════════════════════
    // ③ خطوة الفتل الحقيقية — Torsional Coupling
    // ══════════════════════════════════════════════════════════════
    // المعادلة:
    //   I·ψ̈ = τ_drive − k_rope·ψ − c·ψ̇
    //
    //   τ_drive = twistCoupling × φ̇ × |sinθ|
    //
    //   τ_drive يأتي من:
    //   عندما الدلو يدور أفقياً (φ̇) ، قوة الطرد المركزي تلوي الحبل
    //   |sinθ| يزداد مع الميل — عند θ≈0 لا يوجد فتل (الدلو فوق الـ pivot)
    //
    //   k_rope يعتمد على نوع الحبل:
    //   Steel > Nylon > Cotton (حسب صلابة المادة)
    // ══════════════════════════════════════════════════════════════
    private void StepTwist(float dt)
    {
        float m = CurrentMass;
        float sinT = Mathf.Sin(_theta);

        // عزم قصور ذاتي الدلو حول محوره الرأسي
        // I = ½·m·r² (أسطوانة) + جزء صغير للطلاء
        float r = _bucket.innerRadius;
        float I = 0.5f * m * (r * r + 0.004f);
        I = Mathf.Max(I, 0.0001f);

        // ── العزم المولّد من الدوران الأفقي ──
        // الفيزياء: الدوران φ̇ يولّد قوة كوريوليس/جيروسكوبية تفتل الحبل
        // sinθ لأن تأثير الفتل يعتمد على مدى ابتعاد الدلو أفقياً
        float tau_drive = twistCoupling * _phiDot * Mathf.Abs(sinT);

        // ── صلابة الحبل تجاه الفتل (تعتمد على المادة) ──
        // هذه تمثل مقاومة الحبل للالتواء
        float k_eff = ropeStiffness * GetRopeTorsionalFactor();

        // ── معادلة الحركة ──
        float psiDD = (tau_drive - k_eff * _psi - twistDamping * _psiDot) / I;

        // تكامل Euler نصف-ضمني (أكثر استقراراً من Euler العادي)
        _psiDot += psiDD * dt;
        _psi += _psiDot * dt;

        // ── حد الفتل مع ارتداد ناعم ──
        float maxRad = maxTwistAngleDeg * Mathf.Deg2Rad;
        if (_psi > maxRad)
        {
            _psi = maxRad;
            _psiDot = Mathf.Min(_psiDot, 0f) * 0.4f; // ارتداد جزئي
        }
        else if (_psi < -maxRad)
        {
            _psi = -maxRad;
            _psiDot = Mathf.Max(_psiDot, 0f) * 0.4f;
        }
    }

    // ── معامل الصلابة الالتوائية حسب نوع الحبل ──
    // Steel: صلب جداً → يقاوم الفتل → يرجع بسرعة
    // Cotton: مرن → يفتل كثيراً → يرجع ببطء
    // Nylon: بينهما
    private float GetRopeTorsionalFactor()
    {
        switch (_rope.material)
        {
            case RopeMaterial.SteelWire: return 4.0f;   // صلب جداً
            case RopeMaterial.Nylon: return 1.5f;   // متوسط
            case RopeMaterial.Polyester: return 1.2f;
            default: return 0.8f;   // Cotton: أكثر مرونة
        }
    }

    // ══════════════════════════════════════════════════════════════
    // نايلون: مرونة + زحف
    // ══════════════════════════════════════════════════════════════
    private void StepCreep(float dt, float tension)
    {
        if (!IsNylon) return;
        if (_creepExt >= CREEP_MAX) return;
        float creepSpeed = CREEP_RATE * Mathf.Max(tension, 0f);
        _creepExt = Mathf.Min(_creepExt + creepSpeed * dt, CREEP_MAX);
    }

    private void StepElastic(float dt, float tension)
    {
        if (!IsNylon) return;
        float m = CurrentMass;
        float netF = tension - K_STATIC * _elasticExt - C_VISCOUS * _elasticExtDot;
        float acc = (m > 0.001f) ? netF / m : 0f;
        _elasticExtDot += acc * dt;
        _elasticExt += _elasticExtDot * dt;
        if (_elasticExt < 0f) { _elasticExt = 0f; _elasticExtDot = Mathf.Max(0f, _elasticExtDot); }
        _elasticExt = Mathf.Min(_elasticExt, _currentRopeLength * 0.35f);
    }

    // ══════════════════════════════════════════════════════════════
    // RK4 للبندول الكروي
    // ══════════════════════════════════════════════════════════════
    private struct State { public float theta, phi, thetaDot, phiDot; }

    private State Derivatives(State s, float gamma, float g, float L)
    {
        float sT = Mathf.Sin(s.theta);
        float cT = Mathf.Cos(s.theta);
        float sT_safe = Mathf.Abs(sT) < 0.002f ? Mathf.Sign(sT) * 0.002f : sT;

        float thetaDD = s.phiDot * s.phiDot * sT * cT
                      - (g / L) * sT
                      - gamma * s.thetaDot;

        float phiDD = -2f * s.thetaDot * s.phiDot * (cT / sT_safe)
                      - gamma * s.phiDot;

        return new State
        {
            theta = s.thetaDot,
            phi = s.phiDot,
            thetaDot = thetaDD,
            phiDot = phiDD
        };
    }

    private State RK4Step(State s, float dt, float gamma, float g, float L)
    {
        var k1 = Derivatives(s, gamma, g, L);
        var s2 = new State
        {
            theta = s.theta + .5f * dt * k1.theta,
            phi = s.phi + .5f * dt * k1.phi,
            thetaDot = s.thetaDot + .5f * dt * k1.thetaDot,
            phiDot = s.phiDot + .5f * dt * k1.phiDot
        };
        var k2 = Derivatives(s2, gamma, g, L);
        var s3 = new State
        {
            theta = s.theta + .5f * dt * k2.theta,
            phi = s.phi + .5f * dt * k2.phi,
            thetaDot = s.thetaDot + .5f * dt * k2.thetaDot,
            phiDot = s.phiDot + .5f * dt * k2.phiDot
        };
        var k3 = Derivatives(s3, gamma, g, L);
        var s4 = new State
        {
            theta = s.theta + dt * k3.theta,
            phi = s.phi + dt * k3.phi,
            thetaDot = s.thetaDot + dt * k3.thetaDot,
            phiDot = s.phiDot + dt * k3.phiDot
        };
        var k4 = Derivatives(s4, gamma, g, L);

        return new State
        {
            theta = s.theta + dt * (k1.theta + 2 * k2.theta + 2 * k3.theta + k4.theta) / 6f,
            phi = s.phi + dt * (k1.phi + 2 * k2.phi + 2 * k3.phi + k4.phi) / 6f,
            thetaDot = s.thetaDot + dt * (k1.thetaDot + 2 * k2.thetaDot + 2 * k3.thetaDot + k4.thetaDot) / 6f,
            phiDot = s.phiDot + dt * (k1.phiDot + 2 * k2.phiDot + 2 * k3.phiDot + k4.phiDot) / 6f
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Step الرئيسي
    // ══════════════════════════════════════════════════════════════
    public void Step(float dt)
    {
        _currentPaintHeight = _paint.GetCurrentHeight(_simulationTime,
            _bucket.GetEmptyingTime(_paint.initialHeight, _env.gravity));
        _currentRopeLength = _rope.GetCurrentLength(_env.temperature);

        float m = CurrentMass;
        float cosT = Mathf.Cos(_theta);
        float sT = Mathf.Sin(_theta);
        float L = GetEffectiveLength();
        float omega2 = _thetaDot * _thetaDot + sT * sT * _phiDot * _phiDot;
        float tension = Mathf.Max(0f, m * _env.gravity * cosT + m * L * omega2);

        StepCreep(dt, tension);
        StepElastic(dt, tension);

        // ── RK4 للبندول ──
        var current = new State
        {
            theta = _theta,
            phi = _phi,
            thetaDot = _thetaDot,
            phiDot = _phiDot
        };
        var next = RK4Step(current, dt, GetDampingCoeff(), _env.gravity, L);

        next.theta = Mathf.Clamp(next.theta, 0.001f, Mathf.PI - 0.001f);
        next.thetaDot = Mathf.Clamp(next.thetaDot, -20f, 20f);
        next.phiDot = Mathf.Clamp(next.phiDot, -20f, 20f);

        _theta = next.theta;
        _phi = next.phi;
        _thetaDot = next.thetaDot;
        _phiDot = next.phiDot;

        if (Mathf.Floor(_lastPhi / Mathf.PI) != Mathf.Floor(_phi / Mathf.PI))
            _swingCount++;
        _lastPhi = _phi;

        // ── فتل الحبل/الدلو (بعد تحديث φ̇) ──
        StepTwist(dt);

        _simulationTime += dt;
    }

    // ══════════════════════════════════════════════════════════════
    // حسابات الطاقة والإحصائيات
    // ══════════════════════════════════════════════════════════════
    public float GetKineticEnergy()
    {
        float m = CurrentMass;
        float L = GetEffectiveLength();
        float sT = Mathf.Sin(_theta);
        float ke = 0.5f * m * L * L *
                   (_thetaDot * _thetaDot + sT * sT * _phiDot * _phiDot);
        if (IsNylon) ke += 0.5f * m * _elasticExtDot * _elasticExtDot;
        // طاقة الفتل
        float r = _bucket.innerRadius;
        float I = 0.5f * m * (r * r + 0.004f);
        ke += 0.5f * I * _psiDot * _psiDot;
        return ke;
    }

    public float GetPotentialEnergy()
    {
        float pe = CurrentMass * _env.gravity *
                   GetEffectiveLength() * (1f - Mathf.Cos(_theta));
        if (IsNylon) pe += 0.5f * K_STATIC * _elasticExt * _elasticExt;
        // طاقة الفتل المخزنة في الحبل
        pe += 0.5f * ropeStiffness * GetRopeTorsionalFactor() * _psi * _psi;
        return pe;
    }

    public float GetTotalEnergy() => GetKineticEnergy() + GetPotentialEnergy();

    public float GetPeriod() =>
        2f * Mathf.PI * Mathf.Sqrt(GetEffectiveLength() / _env.gravity);

    public float GetRopeTension()
    {
        float m = CurrentMass;
        float L = GetEffectiveLength();
        float v = BucketVelocity.magnitude;
        return m * _env.gravity * Mathf.Cos(_theta) + m * v * v / L;
    }

    public float CalculateDampingCoefficient() => GetDampingCoeff();
}