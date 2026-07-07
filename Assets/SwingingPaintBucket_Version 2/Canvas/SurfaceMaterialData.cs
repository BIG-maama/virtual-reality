using UnityEngine;

public enum SurfaceMaterial
{
    Canvas,
    Wood,
    Metal,
    Paper
}

[System.Serializable]
public class SurfaceMaterialData
{
    public SurfaceMaterial type = SurfaceMaterial.Canvas;

    // نسبة الامتصاص (0 = غير ماص ، 1 = شديد الامتصاص)
    public float Absorbency
    {
        get
        {
            return type switch
            {
                SurfaceMaterial.Canvas => 0.55f,
                SurfaceMaterial.Wood => 0.35f,
                SurfaceMaterial.Metal => 0.02f,
                SurfaceMaterial.Paper => 0.75f,
                _ => 0.55f
            };
        }
    }

    // زاوية التماس (درجة)
    public float ContactAngleDeg
    {
        get
        {
            return type switch
            {
                SurfaceMaterial.Canvas => 70f,
                SurfaceMaterial.Wood => 60f,
                SurfaceMaterial.Metal => 150f,
                SurfaceMaterial.Paper => 35f,
                _ => 70f
            };
        }
    }

    // خشونة السطح
    public float Roughness
    {
        get
        {
            return type switch
            {
                SurfaceMaterial.Canvas => 0.70f,
                SurfaceMaterial.Wood => 0.45f,
                SurfaceMaterial.Metal => 0.05f,
                SurfaceMaterial.Paper => 0.30f,
                _ => 0.70f
            };
        }
    }

    // مضاعف زمن الجفاف
    public float DryingMultiplier
    {
        get
        {
            return type switch
            {
                SurfaceMaterial.Canvas => 0.80f,
                SurfaceMaterial.Wood => 1.00f,
                SurfaceMaterial.Metal => 1.60f,
                SurfaceMaterial.Paper => 0.60f,
                _ => 1f
            };
        }
    }

    // أس الانتشار (Tanner)
    //public float SpreadExponent
    //{
    //    get
    //    {
    //        return type switch
    //        {
    //            SurfaceMaterial.Metal => 0.091f,
    //            _ => 0.100f
    //        };
    //    }
    //}
    public float SpreadExponent => type switch
    {
        SurfaceMaterial.Paper => 0.471f,
        SurfaceMaterial.Metal => 0.091f,
        _ => 0.194f,
    };

    // معامل البلل (0..1)
    public float WettingFactor
    {
        get
        {
            float c = Mathf.Cos(ContactAngleDeg * Mathf.Deg2Rad);
            return Mathf.Clamp01((c + 1f) * 0.5f);
        }
    }

    // معامل مقاومة السطح للانتشار
    public float SurfaceResistance
    {
        get
        {
            return Mathf.Lerp(1f, 2f, Roughness);
        }
    }

    // معامل الانتشار النهائي المستخدم في المحاكاة
    public float SpreadFactor
    {
        get
        {
            return WettingFactor *
                   (1f - Absorbency * 0.4f) /
                   SurfaceResistance;
        }
    }
}