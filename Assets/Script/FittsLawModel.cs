using UnityEngine;

public enum FittsSizingModel
{
    LegacyLinear,
    BaselineIsoId,
    FFittsSqrtIsoId,
    FFittsLinearIsoId,
}

public struct FittsMetrics
{
    public float amplitudeMm;
    public float widthMm;
    public float idBaseline;
    public float idFFitts;
    public string fittsModel;
}

public static class FittsLawModel
{
    // Galaxy S24 Ultra QHD+ (3120×1440, 6.8" ≈ 172.72mm) → √(3120²+1440²)/172.72 ≈ 19.89 (matches the official 505ppi)
    public const float DefaultPixelsPerMm = 19.89f;
    public const float DefaultAmbiguityC2D_mm = 0.285f;
    public const float DefaultAmbiguityC1D_mm = 0.581f;

    const float Eps = 1e-6f;

    public static float ShannonId(float amplitudeMm, float widthMm)
    {
        amplitudeMm = Mathf.Max(amplitudeMm, 0f);
        widthMm = Mathf.Max(widthMm, Eps);
        return Mathf.Log(amplitudeMm / widthMm + 1f, 2f);
    }

    public static float FFittsIdSqrt(float amplitudeMm, float widthMm, float cMm)
    {
        amplitudeMm = Mathf.Max(amplitudeMm, 0f);
        widthMm = Mathf.Max(widthMm, Eps);
        float inner = Mathf.Max(widthMm * widthMm - cMm * cMm, Eps);
        return Mathf.Log(amplitudeMm / Mathf.Sqrt(inner) + 1f, 2f);
    }

    public static float FFittsIdLinear(float amplitudeMm, float widthMm, float cMm)
    {
        amplitudeMm = Mathf.Max(amplitudeMm, 0f);
        widthMm = Mathf.Max(widthMm, Eps);
        return Mathf.Log(amplitudeMm / Mathf.Max(widthMm - cMm, Eps) + 1f, 2f);
    }

    public static float WidthForIsoId(float amplitudeMm, float targetIdBits, FittsSizingModel model, float cMm)
    {
        amplitudeMm = Mathf.Max(amplitudeMm, 0f);
        float denom = Mathf.Max(Mathf.Pow(2f, targetIdBits) - 1f, Eps);
        float ratio = amplitudeMm / denom;

        switch (model)
        {
            case FittsSizingModel.BaselineIsoId:
                return Mathf.Max(ratio, Eps);
            case FittsSizingModel.FFittsSqrtIsoId:
                return Mathf.Sqrt(cMm * cMm + ratio * ratio);
            case FittsSizingModel.FFittsLinearIsoId:
                return Mathf.Max(ratio + cMm, Eps);
            default:
                return Mathf.Max(ratio, Eps);
        }
    }

    public static float PxToMm(float px, float pixelsPerMm) => px / Mathf.Max(pixelsPerMm, Eps);

    public static float MmToPx(float mm, float pixelsPerMm) => mm * pixelsPerMm;

    public static string ModelKey(FittsSizingModel model)
    {
        switch (model)
        {
            case FittsSizingModel.LegacyLinear:
                return "legacy_linear";
            case FittsSizingModel.BaselineIsoId:
                return "baseline";
            case FittsSizingModel.FFittsSqrtIsoId:
                return "ffitts_sqrt";
            case FittsSizingModel.FFittsLinearIsoId:
                return "ffitts_linear";
            default:
                return "unknown";
        }
    }

    public static float ResolveTargetIdBits(float configuredIdBits, float referenceAmplitudePx, float referenceWidthPx, float pixelsPerMm)
    {
        if (configuredIdBits > 0f)
            return configuredIdBits;

        float aMm = PxToMm(referenceAmplitudePx, pixelsPerMm);
        float wMm = PxToMm(referenceWidthPx, pixelsPerMm);
        return ShannonId(aMm, wMm);
    }
}
