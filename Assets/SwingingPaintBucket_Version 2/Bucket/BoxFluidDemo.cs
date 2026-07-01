using UnityEngine;
using System.Collections.Generic;

public class BoxFluidDemo : MonoBehaviour
{
    public int particleCount = 300;
    public Vector3 boxSize = new Vector3(1f, 1f, 1f);
    public float particleMass = 0.02f;
    public float smoothingRadius = 0.12f;
    public float stiffness = 80f;          // ⬇️ خفضناها (كانت 250 — سبب رئيسي للانفجار)
    public float viscosity = 0.6f;          // ⬆️ رفعناها (تخميد أكبر = استقرار واستكانة)
    public float gravity = 9.8f;
    public float wallDamping = 0.4f;
    public float sphereScale = 0.05f;
    public Color fluidColor = new Color(0.2f, 0.5f, 1f, 1f);
    public float dragSpeed = 0.02f;
    public float maxSpeed = 3f;             // ✅ جديد: حد أقصى للسرعة يمنع "الطيران"

    float restDensity;                       // ✅ جديد: تُحسب تلقائياً من التعبئة الفعلية، مو رقم ثابت

    Vector3[] pos;
    Vector3[] vel;
    float[] density;
    float[] pressure;
    Vector3[] force;

    Mesh sphereMesh;
    Material mat;
    Matrix4x4[] matrices;
    MaterialPropertyBlock mpb;

    float poly6, spiky, visc;
    Vector3 lastMouse;
    bool dragging;

    // ✅ جديد: تتبّع سرعة وتسارع الصندوق نفسه
    Vector3 boxVelocity;
    Vector3 boxPrevPos;

    void Start()
    {
        pos = new Vector3[particleCount];
        vel = new Vector3[particleCount];
        density = new float[particleCount];
        pressure = new float[particleCount];
        force = new Vector3[particleCount];

        int side = Mathf.CeilToInt(Mathf.Pow(particleCount, 1f / 3f));
        int idx = 0;
        for (int x = 0; x < side && idx < particleCount; x++)
            for (int y = 0; y < side && idx < particleCount; y++)
                for (int z = 0; z < side && idx < particleCount; z++)
                {
                    // ✅ نملأ السائل بالثلث السفلي بس من الصندوق (متل سائل حقيقي مستقر بالقاع)
                    Vector3 local = new Vector3(
                        (x / (float)side - 0.5f) * boxSize.x * 0.7f,
                        (y / (float)side) * boxSize.y * 0.35f - boxSize.y * 0.48f,
                        (z / (float)side - 0.5f) * boxSize.z * 0.7f);
                    pos[idx] = transform.position + local;
                    idx++;
                }

        float spacing = (boxSize.x * 0.7f) / side;
        smoothingRadius = spacing * 2.0f;

        float h = smoothingRadius;
        poly6 = 315f / (64f * Mathf.PI * Mathf.Pow(h, 9));
        spiky = 45f / (Mathf.PI * Mathf.Pow(h, 6));
        visc = 45f / (Mathf.PI * Mathf.Pow(h, 6));

        // ✅ نحسب restDensity الحقيقية من نفس التعبئة قبل ما نبلش —
        // هيك الضغط عند السكون تقريباً = صفر، ما في انفجار أول فريم
        restDensity = ComputeAverageDensity(h);

        sphereMesh = BuildSphere(8, 8);
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.enableInstancing = true;
        mat.color = fluidColor;
        matrices = new Matrix4x4[particleCount];
        mpb = new MaterialPropertyBlock();

        boxPrevPos = transform.position;
    }

    float ComputeAverageDensity(float h)
    {
        float sum = 0f;
        for (int i = 0; i < particleCount; i++)
        {
            float d = 0f;
            for (int j = 0; j < particleCount; j++)
            {
                float r2 = (pos[i] - pos[j]).sqrMagnitude;
                if (r2 < h * h) d += particleMass * poly6 * Mathf.Pow(h * h - r2, 3);
            }
            sum += d;
        }
        return sum / particleCount;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) { dragging = true; lastMouse = Input.mousePosition; }
        if (Input.GetMouseButtonUp(0)) dragging = false;
        if (dragging)
        {
            Vector3 delta = Input.mousePosition - lastMouse;
            transform.position += new Vector3(delta.x, delta.y, 0f) * dragSpeed * Time.deltaTime;
            lastMouse = Input.mousePosition;
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float h = smoothingRadius;

        // ✅ حساب سرعة/تسارع الصندوق نفسه (عطالة) — هاد أهم جزء لطلب الأستاذ
        boxVelocity = (transform.position - boxPrevPos) / dt;
        boxPrevPos = transform.position;
        // القوة الوهمية بإطار غير عطالي = -تسارع الصندوق (نقرّبها من الفرق بالسرعة)
        Vector3 inertialForce = -boxVelocity / dt * 0.02f; // معامل تخفيف تجريبي

        for (int i = 0; i < particleCount; i++)
        {
            float d = 0f;
            for (int j = 0; j < particleCount; j++)
            {
                float r2 = (pos[i] - pos[j]).sqrMagnitude;
                if (r2 < h * h) d += particleMass * poly6 * Mathf.Pow(h * h - r2, 3);
            }
            density[i] = d;
            // ✅ الضغط لا يكون سالباً أبداً (يمنع "التجاذب المتفجر")
            pressure[i] = Mathf.Max(0f, stiffness * (d - restDensity));
        }

        for (int i = 0; i < particleCount; i++)
        {
            Vector3 f = Vector3.zero;
            for (int j = 0; j < particleCount; j++)
            {
                if (i == j) continue;
                Vector3 diff = pos[i] - pos[j];
                float r = diff.magnitude;
                if (r < h && r > 0.0001f)
                {
                    Vector3 dir = diff / r;
                    float pTerm = (pressure[i] + pressure[j]) / (2f * density[j] + 0.0001f);
                    f -= dir * particleMass * pTerm * spiky * Mathf.Pow(h - r, 2);
                    f += (vel[j] - vel[i]) * viscosity * particleMass * visc * (h - r) / (density[j] + 0.0001f);
                }
            }
            f += Vector3.down * gravity * Mathf.Max(density[i], 1f);
            f += inertialForce * Mathf.Max(density[i], 1f); // ✅ تأثير حركة الصندوق على السائل
            force[i] = f;
        }

        Vector3 half = boxSize * 0.5f;
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 a = force[i] / Mathf.Max(density[i], 1f);
            vel[i] += a * dt;
            vel[i] = Vector3.ClampMagnitude(vel[i], maxSpeed); // ✅ يمنع "الطيران"
            pos[i] += vel[i] * dt;

            Vector3 local = pos[i] - transform.position;
            if (Mathf.Abs(local.x) > half.x) { local.x = Mathf.Sign(local.x) * half.x; vel[i].x *= -wallDamping; }
            if (Mathf.Abs(local.y) > half.y) { local.y = Mathf.Sign(local.y) * half.y; vel[i].y *= -wallDamping; }
            if (Mathf.Abs(local.z) > half.z) { local.z = Mathf.Sign(local.z) * half.z; vel[i].z *= -wallDamping; }
            pos[i] = transform.position + local;
        }
    }

    void LateUpdate()
    {
        if (sphereMesh == null) return;
        mpb.SetColor("_BaseColor", fluidColor);
        for (int start = 0; start < particleCount; start += 1023)
        {
            int count = Mathf.Min(1023, particleCount - start);
            for (int i = 0; i < count; i++)
                matrices[i] = Matrix4x4.TRS(pos[start + i], Quaternion.identity, Vector3.one * sphereScale);
            Graphics.DrawMeshInstanced(sphereMesh, 0, mat, matrices, count, mpb);
        }
    }

    Mesh BuildSphere(int lat, int lon)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= lat; i++)
        {
            float phi = Mathf.PI * i / lat;
            for (int j = 0; j <= lon; j++)
            {
                float theta = 2f * Mathf.PI * j / lon;
                verts.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f);
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, b = a + lon + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}