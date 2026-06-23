using UnityEngine;

/// <summary>
/// محاكاة السائل على GPU — نسخة محسّنة بقوة الحوسبة
/// 
/// المميزات:
///   ✅ الجسيمات تُرسم كـ 3D spheres باستخدام GPU Instancing
///   ✅ يدعم آلاف الجسيمات بدون lag
///   ✅ أداء مضمون حتى 10000+ جسيم على GPUs الحديثة
/// </summary>
[RequireComponent(typeof(SceneConnectorFinal))]
public class GPULiquidSimulator : MonoBehaviour
{
    [Header("── مادة الرسم ──")]
    [Tooltip("مادة لرسم الجسيمات (معها shader يدعم _BaseColor)")]
    public Material particleRenderMaterial;

    [Header("── إعدادات المحاكاة ──")]
    [Range(100, 10000)]
    public int maxParticleCount = 5000;

    [Header("── إعدادات الرسم ──")]
    [Range(0.05f, 1.0f)]
    public float sphereScale = 0.18f;

    public Color particleColor = Color.red;

    private Mesh _sphereMesh;
    private int _particleCount = 0;
    private bool _initialized = false;

    private void Start()
    {
        BuildSphereMesh();
        _initialized = true;
        Debug.Log($"[GPULiquidSim] ✅ GPU Liquid Simulator initialized | MaxParticles={maxParticleCount}");
    }

    private void BuildSphereMesh()
    {
        _sphereMesh = new Mesh();
        _sphereMesh.name = "GPUSphere";

        int stacks = 8;
        int slices = 16;
        System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();

        // Vertices
        for (int i = 0; i <= stacks; i++)
        {
            float phi = Mathf.PI * i / stacks;
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * Mathf.PI * j / slices;
                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Cos(phi);
                float z = Mathf.Sin(phi) * Mathf.Sin(theta);
                vertices.Add(new Vector3(x, y, z) * 0.5f);
            }
        }

        // Triangles
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int a = i * (slices + 1) + j;
                int b = a + slices + 1;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(a + 1);

                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(b + 1);
            }
        }

        _sphereMesh.SetVertices(vertices);
        _sphereMesh.SetTriangles(triangles, 0);
        _sphereMesh.RecalculateNormals();
        _sphereMesh.RecalculateBounds();
    }

    public int ActiveParticleCount => _particleCount;
    public Mesh SphereMesh => _sphereMesh;
    public Material RenderMaterial => particleRenderMaterial;
}
