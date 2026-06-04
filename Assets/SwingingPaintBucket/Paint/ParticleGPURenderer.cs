using UnityEngine;

public class ParticleGPURenderer
{
    private Mesh _mesh;
    private Material _material;
    private Matrix4x4[] _matrices;
    private int _count;
    private const int MAX = 5000; // الحد الأقصى

    public ParticleGPURenderer(Color color)
    {
        // كرة صغيرة للجسيم
        _mesh = CreateSphereMesh();
        _material = new Material(Shader.Find("Standard"));
        _material.color = color;
        // تفعيل GPU Instancing ضروري
        _material.enableInstancing = true;
        _matrices = new Matrix4x4[MAX];
    }

    // تحديث مواضع الجسيمات للرسم
    public void UpdateAndDraw(System.Collections.Generic.IReadOnlyList<PaintParticle> particles)
    {
        _count = 0;
        foreach (var p in particles)
        {
            if (p.State != ParticleState.Flying) continue;
            if (_count >= MAX) break;

            // حجم الجسيم البصري (مستقل عن حجم الفيزياء)
            float visualSize = 0.15f;
            _matrices[_count] = Matrix4x4.TRS(
                p.Position,
                Quaternion.identity,
                Vector3.one * visualSize
            );
            _count++;
        }

        if (_count > 0)
            // رسم كل الجسيمات بـ draw call واحد فقط!
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices, _count);
    }

    private Mesh CreateSphereMesh()
    {
        // كرة مبسطة من Unity
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(go);
        return mesh;
    }
}