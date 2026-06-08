using UnityEngine;

/// <summary>
/// BucketVisualController — تطبيق بصري للفتل على الدلو
///
/// الحالة الحالية: مُعطَّل (bvc.enabled = false) في SceneConnectorFinal
/// SceneConnectorFinal.MoveBucket() يتحكم في الدوران مباشرة
///
/// هذا الكود جاهز للاستخدام المستقبلي إذا احتجت تفعيله
/// في هذه الحالة: احذف منطق الدوران من MoveBucket() في SCF
/// </summary>
public class BucketVisualController : MonoBehaviour
{
    [Header("Pivot Point Reference")]
    [Tooltip("Drag PivotPoint here")]
    public Transform pivotPoint;

    [Header("Simulation Manager Reference")]
    [Tooltip("Drag SimulationController here")]
    public SimulationManager simulationManager;

    [Header("Visual Motion Settings")]
    [Tooltip("مقدار ميل الدلو مع الحبل (0=بدون ميل، 1=ميل كامل مع الحبل)")]
    [Range(0f, 1f)]
    public float tiltFactor = 0.6f;

    [Tooltip("سرعة انتقال الدلو للميل الجديد")]
    [Range(1f, 20f)]
    public float tiltSmoothSpeed = 8f;

    [Header("Twist Settings")]
    [Tooltip("تفعيل الفتل (Twist) حول محور الحبل")]
    public bool enableTwist = true;

    [Tooltip("عامل تضخيم الفتل — اضبطه إذا كان الفتل ضعيف أو قوي")]
    [Range(0.1f, 5f)]
    public float twistAmplifier = 1.0f;

    private void LateUpdate()
    {
        if (simulationManager == null || simulationManager.Physics == null) return;
        if (pivotPoint == null) return;

        BucketPhysics physics = simulationManager.Physics;

        // ── الموضع ──
        Vector3 physicsOffset = physics.BucketPosition;
        Vector3 newPosition = pivotPoint.position + physicsOffset;
        transform.position = newPosition;

        // ── الدوران: ميل + فتل ψ ──
        Vector3 ropeDir = (pivotPoint.position - newPosition).normalized;

        if (ropeDir.sqrMagnitude > 0.001f)
        {
            Quaternion ropeAlignedRot = Quaternion.FromToRotation(Vector3.up, ropeDir);
            Quaternion baseTilt = Quaternion.Slerp(Quaternion.identity, ropeAlignedRot, tiltFactor);

            if (enableTwist)
            {
                // زاوية الفتل مباشرة من الفيزياء × مضخم اختياري
                float twistAngleDeg = physics.PsiDeg * twistAmplifier;
                Quaternion twistRot = Quaternion.AngleAxis(twistAngleDeg, ropeDir);
                Quaternion targetRot = twistRot * baseTilt;

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Time.deltaTime * tiltSmoothSpeed);
            }
            else
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    baseTilt,
                    Time.deltaTime * tiltSmoothSpeed);
            }
        }
    }
}