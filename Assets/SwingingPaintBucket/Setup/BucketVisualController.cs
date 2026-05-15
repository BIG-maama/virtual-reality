using UnityEngine;

/// <summary>
/// يتحكم في حركة الدلو ثلاثي الأبعاد بناءً على الفيزياء المحسوبة
/// 
/// ضعه على كل دلو: Bucket_Metal و Bucket_Wood
/// الفيزياء في BucketPhysics تحسب الموضع رياضياً
/// هذا السكريبت يأخذ تلك النتيجة ويطبقها على Transform الدلو
/// 
/// لا Rigidbody - لا Collider - فيزياء يدوية بالكامل
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
    [Tooltip("Bucket rotation speed with motion")]
    public float tiltFactor = 15f;

    // Last position (for rotation calculation)
    private Vector3 _lastPosition;

    private void Start()
    {
        _lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (simulationManager == null || simulationManager.Physics == null) return;
        if (pivotPoint == null) return;

        BucketPhysics physics = simulationManager.Physics;

        // ===========================================================
        // 1. Update bucket position
        // Position = PivotPoint + offset calculated by Lagrange equations
        // x = L·sin(θ)·cos(φ)
        // y = -L·cos(θ)
        // z = L·sin(θ)·sin(φ)
        // ===========================================================
        Vector3 physicsOffset = physics.BucketPosition;
        Vector3 newPosition = pivotPoint.position + physicsOffset;
        transform.position = newPosition;

        // ===========================================================
        // 2. Tilt bucket with motion direction (realistic visual effect)
        // ===========================================================
        Vector3 velocity = physics.BucketVelocity;
        if (velocity.magnitude > 0.01f)
        {
            // Slight tilt in direction of motion
            float tiltX = -velocity.z * tiltFactor;
            float tiltZ = velocity.x * tiltFactor;
            Quaternion targetRot = Quaternion.Euler(tiltX, transform.rotation.eulerAngles.y, tiltZ);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * 5f);
        }
        else
        {
            // Return to normal position when stopped
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                Quaternion.identity,
                Time.deltaTime * 3f
            );
        }

        _lastPosition = newPosition;
    }
}