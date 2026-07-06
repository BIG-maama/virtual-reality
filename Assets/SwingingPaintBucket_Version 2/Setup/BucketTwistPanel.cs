using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BucketTwistPanel : MonoBehaviour
{
    [Header("=== المراجع الأساسية ===")]
    public SceneConnectorFinal connector;

    // ══════════════════════════════════════════════════════════
    // Live: تُطبَّق فوراً على الفيزياء
    // ══════════════════════════════════════════════════════════
    [Header("=== Live: صلابة وتخميد الفتل ===")]
    public Slider sliderRopeStiffness;
    public TMP_Text labelRopeStiffness;

    public Slider sliderTwistDamping;
    public TMP_Text labelTwistDamping;

    public Slider sliderTwistCoupling;
    public TMP_Text labelTwistCoupling;

    public Slider sliderMaxTwistAngle;
    public TMP_Text labelMaxTwistAngle;

    // ══════════════════════════════════════════════════════════
    // Live أيضاً: تُطبَّق مباشرة على الفيزياء بدون Restart
    // ══════════════════════════════════════════════════════════
    [Header("=== Live: فتل ابتدائي وزوايا ===")]

    [Tooltip("ψ₀ — فتل ابتدائي مباشر: يُطبَّق فوراً على الحالة الحالية")]
    public Slider sliderInitialTwistAngle;
    public TMP_Text labelInitialTwistAngle;

    [Tooltip("θ₀ — زاوية الميل: تُطبَّق فوراً على الحالة الحالية")]
    public Slider sliderStartAngle;
    public TMP_Text labelStartAngle;

    [Tooltip("φ₀ — اتجاه التأرجح: يُطبَّق فوراً")]
    public Slider sliderStartPhi;
    public TMP_Text labelStartPhi;

    [Tooltip("φ̇₀ — السرعة الأفقية: تُطبَّق فوراً")]
    public Slider sliderStartPhiAngVelocity;
    public TMP_Text labelStartPhiAngVelocity;

    // ══════════════════════════════════════════════════════════
    // عرض حي (Read-only)
    // ══════════════════════════════════════════════════════════
    [Header("=== Live Readout ===")]
    public TMP_Text textTwistReadout;

    private float _readoutTimer = 0f;
    private const float READOUT_INTERVAL = 0.1f;

    [Header("=== Live: الرياح ===")]
    public Slider sliderWindForce;
    public TMP_Text labelWindForce;

    public Slider sliderWindDirection;
    public TMP_Text labelWindDirection;

    // ══════════════════════════════════════════════════════════
    private void Start()
    {
        SetupAllLiveSliders();
        SyncFromConnector();
    }

    private void Update()
    {
        _readoutTimer += Time.deltaTime;
        if (_readoutTimer >= READOUT_INTERVAL)
        {
            _readoutTimer = 0f;
            RefreshReadout();
        }
    }

    // ══════════════════════════════════════════════════════════
    // كل السلايدرات Live — تُطبَّق فوراً
    // ══════════════════════════════════════════════════════════
    private void SetupAllLiveSliders()
    {
        // ── صلابة الحبل ──
        sliderRopeStiffness?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.ropeStiffness = v;
            var p = connector.GetPhysics();
            if (p != null) p.ropeStiffness = v;
            if (labelRopeStiffness)
                labelRopeStiffness.text = $"Rope Stiffness: {v:F3}";
        });

        // ── تخميد الفتل ──
        sliderTwistDamping?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.twistDamping = v;
            var p = connector.GetPhysics();
            if (p != null) p.twistDamping = v;
            if (labelTwistDamping)
                labelTwistDamping.text = $"Twist Damping: {v:F4}";
        });

        // ── قوة الترابط ──
        sliderTwistCoupling?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.twistCoupling = v;
            var p = connector.GetPhysics();
            if (p != null) p.twistCoupling = v;
            if (labelTwistCoupling)
                labelTwistCoupling.text = $"Twist Coupling: {v:F1}";
        });

        // ── أقصى زاوية فتل ──
        sliderMaxTwistAngle?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.maxTwistAngle = v;
            var p = connector.GetPhysics();
            if (p != null) p.maxTwistAngleDeg = v;
            if (labelMaxTwistAngle)
                labelMaxTwistAngle.text = $"Max Twist: {v:F0}°";
        });

        // ══════════════════════════════════════════════════════
        // ── الفتل الابتدائي ψ₀: يُطبَّق مباشرة بدون Restart ──
        // ══════════════════════════════════════════════════════
        sliderInitialTwistAngle?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.initialTwistVelocity = v;

            var p = connector.GetPhysics();
            if (p != null)
            {
                // ✅ تطبيق مباشر على الفيزياء الحالية
                // SetTwistState(psiRad, psiDotRad)
                // نحتفظ بـ psiDot الحالي ونغير psi فقط
                p.SetTwistState(v, 0f);
            }

            if (labelInitialTwistAngle)
                labelInitialTwistAngle.text = $"Twist ψ₀: {v:F2} rad ({v * Mathf.Rad2Deg:F0}°)";
        });
        // ── قوة الرياح ──
        sliderWindForce?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.windForce = v;
            var p = connector.GetPhysics();
            if (p != null) p.windForce = v;
            if (labelWindForce)
                labelWindForce.text = $"Wind Force: {v:F1} N";
        });

        // ── اتجاه الرياح ──
        sliderWindDirection?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.windDirectionDeg = v;
            var p = connector.GetPhysics();
            if (p != null) p.windDirectionDeg = v;
            if (labelWindDirection)
            {
                string dir = v < 90f || v > 270f ? "يسار الشاشة ⟵" : "يمين الشاشة ⟶";
                labelWindDirection.text = $"Wind Dir: {v:F0}° ({dir})";
            }
        });

        // ══════════════════════════════════════════════════════
        // ── زاوية الميل θ: تُطبَّق مباشرة على الفيزياء ──
        // ══════════════════════════════════════════════════════
        sliderStartAngle?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.startAngleDeg = v;

            var p = connector.GetPhysics();
            if (p != null)
            {
                // ✅ تطبيق مباشر: نغير theta مع الحفاظ على باقي الحالة
                p.SetThetaState(v * Mathf.Deg2Rad, p.ThetaDot);
            }

            if (labelStartAngle)
                labelStartAngle.text = $"Angle θ₀: {v:F1}°";
        });

        // ── اتجاه φ ──
        sliderStartPhi?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.startPhiDeg = v;

            var p = connector.GetPhysics();
            if (p != null)
            {
                // ✅ تطبيق مباشر على phi
                p.SetPhiState(v * Mathf.Deg2Rad, p.PhiDot);
            }

            if (labelStartPhi)
                labelStartPhi.text = $"Direction φ₀: {v:F1}°";
        });

        // ── السرعة الأفقية φ̇ ──
        sliderStartPhiAngVelocity?.onValueChanged.AddListener(v =>
        {
            if (connector == null) return;
            connector.startPhiAngVelocity = v;

            var p = connector.GetPhysics();
            if (p != null)
            {
                // ✅ تطبيق مباشر على phiDot
                p.SetPhiState(p.Phi, v);
            }

            if (labelStartPhiAngVelocity)
                labelStartPhiAngVelocity.text = $"φ̇₀: {v:F3} rad/s";
        });
    }

    // ── مزامنة من الـ Inspector ──
    private void SyncFromConnector()
    {
        if (connector == null) return;

        sliderRopeStiffness?.SetValueWithoutNotify(connector.ropeStiffness);
        sliderTwistDamping?.SetValueWithoutNotify(connector.twistDamping);
        sliderTwistCoupling?.SetValueWithoutNotify(connector.twistCoupling);
        sliderMaxTwistAngle?.SetValueWithoutNotify(connector.maxTwistAngle);
        sliderStartAngle?.SetValueWithoutNotify(connector.startAngleDeg);
        sliderStartPhi?.SetValueWithoutNotify(connector.startPhiDeg);
        sliderStartPhiAngVelocity?.SetValueWithoutNotify(connector.startPhiAngVelocity);
        sliderInitialTwistAngle?.SetValueWithoutNotify(connector.initialTwistVelocity);
        sliderWindForce?.SetValueWithoutNotify(connector.windForce);
        sliderWindDirection?.SetValueWithoutNotify(connector.windDirectionDeg);

        // تحديث الـ Labels
        if (labelRopeStiffness)
            labelRopeStiffness.text = $"Rope Stiffness: {connector.ropeStiffness:F3}";
        if (labelTwistDamping)
            labelTwistDamping.text = $"Twist Damping: {connector.twistDamping:F4}";
        if (labelTwistCoupling)
            labelTwistCoupling.text = $"Twist Coupling: {connector.twistCoupling:F1}";
        if (labelMaxTwistAngle)
            labelMaxTwistAngle.text = $"Max Twist: {connector.maxTwistAngle:F0} deg";
        if (labelStartAngle)
            labelStartAngle.text = $"Angle θ: {connector.startAngleDeg:F1} deg";
        if (labelStartPhi)
            labelStartPhi.text = $"Direction φ: {connector.startPhiDeg:F1} deg";
        if (labelStartPhiAngVelocity)
            labelStartPhiAngVelocity.text = $"φ̇: {connector.startPhiAngVelocity:F3} rad/s";
        if (labelInitialTwistAngle)
            labelInitialTwistAngle.text = $"Twist ψ: {connector.initialTwistVelocity:F2} rad";
        if (labelWindForce)
            labelWindForce.text = $"Wind Force: {connector.windForce:F1} N";
        if (labelWindDirection)
            labelWindDirection.text = $"Wind Dir: {connector.windDirectionDeg:F0} deg";
    }

    // ── عرض حي ──
    private void RefreshReadout()
    {
        if (textTwistReadout == null || connector == null) return;
        var p = connector.GetPhysics();
        if (p == null) { textTwistReadout.text = "Not running"; return; }

        textTwistReadout.text =
            $"θ: {p.Theta * Mathf.Rad2Deg:F1}°\n" +
            $"φ: {p.Phi * Mathf.Rad2Deg:F1}°\n" +
            $"φ̇: {p.PhiDot:F3} rad/s\n" +
            $"ψ: {p.PsiDeg:F1}°\n" +
            $"ψ̇: {p.PsiDot:F2} rad/s\n" +
            $"Rope: {connector.GetCurrentRopeMaterial()}";
    }
}