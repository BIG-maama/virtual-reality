using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>
/// SPHParticleRenderer — كرات 3D حقيقية على GPU
///
/// كيف يشتغل:
///   Graphics.DrawMeshInstanced → رسم كل الجسيمات في GPU call واحد
///   بدل: foreach particle { _paintPS.Emit() } = CPU call لكل جسيم
///
/// النتيجة:
///   - الجسيمات تظهر ككرات ثلاثية الأبعاد حقيقية (مو مربعات Billboard)
///   - الرسم على GPU (Dedicated GPU memory يرتفع في Task Manager)
///   - يدعم 1000 جسيم بدون lag
///
/// الإعداد في Unity:
///   1. اضغط SimulationController في Hierarchy
///   2. Add Component → SPHParticleRenderer
///   3. اسحب SimulationController في حقل "Connector"
/// </summary>
[RequireComponent(typeof(SceneConnectorFinal))]
public class SPHParticleRenderer : MonoBehaviour
{
    [Header("مرجع المحاكاة")]
    public SceneConnectorFinal connector;

    [Header("إعدادات الكرة")]
    [Tooltip("حجم الكرة الواحدة بوحدات Unity")]
    [Range(0.05f, 1.0f)]
    public float sphereScale = 0.18f;

    [Tooltip("لون الجسيمات داخل الدلو")]
    public Color sphereColor = Color.red;

    [Tooltip("بريق الكرة (0=مطفي، 1=لامع)")]
    [Range(0f, 1f)]
    public float smoothness = 0.75f;

    // داخلي
    private Mesh _sphereMesh;
    private Material _mat;
    private List<Vector4> _particlePositions = new List<Vector4>(1024);
    private Matrix4x4[] _matrices = new Matrix4x4[1023];
    private MaterialPropertyBlock _mpb;

    // ═══════════════ Start ═══════════════
    private void Start()
    {
        // إذا ما ربط يدوياً، جرّب GetComponent
        if (connector == null)
            connector = GetComponent<SceneConnectorFinal>();

        _sphereMesh = BuildSphereMesh(12, 10);
        _mat = BuildMaterial();
        _mpb = new MaterialPropertyBlock();

        Debug.Log("[SPHRenderer] ✅ GPU Instanced sphere renderer initialized | " +
                  $"mesh verts={_sphereMesh.vertexCount}");
    }

    // ═══════════════ LateUpdate — رسم كل Frame ═══════════════
    private void LateUpdate()
    {
        if (connector == null) return;

        SPHFluid sph = connector.GetSPHFluid();
        if (sph == null) return;

        // جلب مواضع الجسيمات النشطة
        sph.FillActivePositions(_particlePositions);
        int total = _particlePositions.Count;
        if (total == 0) return;

        // تحديث اللون إذا تغير
        _mpb.SetColor("_BaseColor", sphereColor);
        _mpb.SetColor("_Color", sphereColor);

        // رسم على GPU بـ batches من 1023 (حد Unity)
        for (int start = 0; start < total; start += 1023)
        {
            int count = Mathf.Min(1023, total - start);
            for (int i = 0; i < count; i++)
            {
                Vector4 p = _particlePositions[start + i];
                Vector3 pos = new Vector3(p.x, p.y, p.z);
                _matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity,
                                               Vector3.one * sphereScale);
            }

            Graphics.DrawMeshInstanced(
                _sphereMesh,
                submeshIndex: 0,
                _mat,
                _matrices,
                count,
                _mpb,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false,
                layer: 0
            );
        }
    }

    // ═══════════════ بناء Sphere Mesh يدوياً ═══════════════
    // لا نستخدم GameObject.CreatePrimitive لأنه يضيف Collider
    private Mesh BuildSphereMesh(int latSegs, int lonSegs)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        for (int lat = 0; lat <= latSegs; lat++)
        {
            float theta = lat * Mathf.PI / latSegs;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= lonSegs; lon++)
            {
                float phi = lon * 2f * Mathf.PI / lonSegs;
                float3 n3 = new float3(
                    Mathf.Cos(phi) * sinTheta,
                    cosTheta,
                    Mathf.Sin(phi) * sinTheta);
                verts.Add(new Vector3(n3.x, n3.y, n3.z) * 0.5f);
                norms.Add(new Vector3(n3.x, n3.y, n3.z));
            }
        }

        for (int lat = 0; lat < latSegs; lat++)
            for (int lon = 0; lon < lonSegs; lon++)
            {
                int a = lat * (lonSegs + 1) + lon;
                int b = a + lonSegs + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
            }

        var mesh = new Mesh { name = "SPH_Sphere" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ═══════════════ بناء Material مع GPU Instancing ═══════════════
    private Material BuildMaterial()
    {
        // URP أولاً (المشروع يستخدم URP)
        Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                 ?? Shader.Find("Standard");

        var mat = new Material(sh) { color = sphereColor };
        mat.enableInstancing = true; // ✅ ضروري لـ DrawMeshInstanced

        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.05f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", sphereColor);

        return mat;
    }

    /// <summary>تحديث اللون من SceneConnectorFinal عند تغيير المستخدم للون</summary>
    public void SetColor(Color c)
    {
        sphereColor = c;
        if (_mat != null)
        {
            _mat.color = c;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        }
    }
}