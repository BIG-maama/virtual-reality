using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU Renderer للجزيئات — يدعم 10,000+ جزيئة
/// الحل: DrawMeshInstanced تقبل أقصى 1023 per call
///       نقسّمها على batches تلقائياً
/// تحسينات:
///   ✅ Quad (4 vertices) بدل Sphere → أسرع 5×
///   ✅ Batching تلقائي
///   ✅ Camera-facing billboards
///   ✅ MaterialPropertyBlock للألوان
/// </summary>
public class ParticleGPURenderer
{
    private const int BATCH_SIZE = 1023;
    private const int MAX_PARTICLES = 12000;

    private Mesh _mesh;
    private Material _material;
    private Matrix4x4[] _batchBuffer;
    private MaterialPropertyBlock _mpb;
    private bool _useMultiColor = false;
    private Vector4[] _colorBatch;

    public int LastDrawnCount { get; private set; }

    // ══════════════════════════════════════════════════════════
    public ParticleGPURenderer(Color color)
    {
        _mesh = CreateBillboardMesh();
        _material = CreateParticleMaterial(color);
        _batchBuffer = new Matrix4x4[BATCH_SIZE];
        _colorBatch = new Vector4[BATCH_SIZE];
        _mpb = new MaterialPropertyBlock();
    }

    // ══════════════════════════════════════════════════════════
    public void SetColor(Color color)
    {
        if (_material != null)
            _material.color = color;
        _useMultiColor = false;
    }

    public void EnableMultiColor() => _useMultiColor = true;

    // ══════════════════════════════════════════════════════════
    // Main Render — يُستدعى كل frame من Update
    // ══════════════════════════════════════════════════════════
    public void UpdateAndDraw(IReadOnlyList<PaintParticle> particles)
    {
        if (particles == null || particles.Count == 0) return;

        LastDrawnCount = 0;
        int batchIdx = 0;

        foreach (PaintParticle p in particles)
        {
            if (p.State != ParticleState.Flying) continue;
            if (LastDrawnCount >= MAX_PARTICLES) break;

            // حجم بصري: Radius*150 يعطي قطرة مرئية
            float size = Mathf.Clamp(p.Radius * 150f, 1.0f, 4.0f);

            _batchBuffer[batchIdx] = Matrix4x4.TRS(
                p.Position,
                GetBillboardRotation(),
                Vector3.one * size
            );

            if (_useMultiColor)
            {
                Color c = p.ParticleColor;
                _colorBatch[batchIdx] = new Vector4(c.r, c.g, c.b, c.a);
            }

            batchIdx++;
            LastDrawnCount++;

            if (batchIdx == BATCH_SIZE)
            {
                FlushBatch(batchIdx);
                batchIdx = 0;
            }
        }

        if (batchIdx > 0) FlushBatch(batchIdx);
    }

    // ══════════════════════════════════════════════════════════
    private void FlushBatch(int count)
    {
        if (_useMultiColor)
        {
            _mpb.SetVectorArray("_Color", _colorBatch);
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _batchBuffer, count,
                _mpb,
                UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
        else
        {
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _batchBuffer, count,
                null,
                UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }

    // ══════════════════════════════════════════════════════════
    private static Quaternion GetBillboardRotation()
    {
        Camera cam = Camera.main;
        return cam != null
            ? Quaternion.LookRotation(cam.transform.forward)
            : Quaternion.identity;
    }

    // ══════════════════════════════════════════════════════════
    // Quad بدل Sphere — أسرع بكثير
    // ══════════════════════════════════════════════════════════
    private Mesh CreateBillboardMesh()
    {
        var mesh = new Mesh { name = "ParticleBillboard" };

        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };

        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ══════════════════════════════════════════════════════════
    private Material CreateParticleMaterial(Color color)
    {
        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Standard")
                 ?? Shader.Find("Unlit/Color");

        var mat = new Material(sh)
        {
            color = color,
            enableInstancing = true
        };

        // شفافية
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        return mat;
    }
}