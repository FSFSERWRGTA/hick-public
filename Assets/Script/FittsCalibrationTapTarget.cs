using UnityEngine;
using UnityEngine.EventSystems;

public class FittsCalibrationTapTarget : MonoBehaviour, IPointerDownHandler
{
    public enum TapRole
    {
        Start,
        Target,
    }

    [SerializeField] private FittsCalibrationExperiment experiment;
    [SerializeField] private TapRole role = TapRole.Start;

    public void Configure(FittsCalibrationExperiment owner, TapRole tapRole)
    {
        experiment = owner;
        role = tapRole;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (experiment == null || eventData == null) return;
        experiment.OnCalibrationTap(role, eventData.position);
    }
}
