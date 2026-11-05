using UnityEngine;

public abstract class NumberButtonSpawnerBase : MonoBehaviour, INumberButtonSpawner
{
    public abstract void SetCount(int n);

    // Maximum number of buttons supported per scene/layout
    public virtual int GetMaxSupportedCount()
    {
        return int.MaxValue;
    }

    public virtual bool TryGetFittsMetrics(int logicalNumber, out FittsMetrics metrics)
    {
        metrics = default;
        return false;
    }
}