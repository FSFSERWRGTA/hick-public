using UnityEngine;

public interface INumberButtonSpawner
{
    void SetCount(int n);

    bool TryGetFittsMetrics(int logicalNumber, out FittsMetrics metrics);
}