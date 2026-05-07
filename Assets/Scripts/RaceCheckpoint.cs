using UnityEngine;

public enum CheckpointVisualState
{
    Pending,
    Active,
    Completed
}

[DisallowMultipleComponent]
public class RaceCheckpoint : MonoBehaviour
{
    private static readonly Color PendingCheckpointColor = new(0.15f, 0.65f, 1f);
    private static readonly Color CompletedCheckpointColor = new(0.5f, 0.32f, 0.16f);
    private const float PoleRadius = 0.35f;

    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();
    private RaceTrackManager raceManager;
    private int checkpointIndex = -1;
    private CheckpointVisualState currentState = CheckpointVisualState.Pending;

    [SerializeField] private float activePulseSpeed = 2.4f;
    [SerializeField] private float activeEmissionMin = 0.75f;
    [SerializeField] private float activeEmissionMax = 2.2f;
    [SerializeField] private float checkpointSphereDiameter = 1.8f;

    public void Initialize(RaceTrackManager raceManager, int index)
    {
        this.raceManager = raceManager;
        checkpointIndex = index;
        name = $"Checkpoint {index + 1}";
    }

    public int CheckpointIndex => checkpointIndex;

    public bool BelongsTo(RaceTrackManager manager)
    {
        return raceManager == manager;
    }

    public void CacheRenderers(Vector3 nextCheckpointPosition)
    {
        var checkpointSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        checkpointSphere.name = "Checkpoint Core";
        checkpointSphere.transform.SetParent(transform, false);
        checkpointSphere.transform.localPosition = Vector3.zero;
        checkpointSphere.transform.localScale = Vector3.one * checkpointSphereDiameter;
        Object.Destroy(checkpointSphere.GetComponent<Collider>());

        var sphereRadius = checkpointSphereDiameter * 0.5f;
        var toNextCheckpoint = nextCheckpointPosition - transform.position;
        if (toNextCheckpoint.sqrMagnitude > 0.001f)
        {
            var connectorLength = Mathf.Max(0f, toNextCheckpoint.magnitude - checkpointSphereDiameter);
            if (connectorLength > 0.001f)
            {
                var connectorDirection = toNextCheckpoint.normalized;
                var connectorOffset = connectorDirection * (sphereRadius + connectorLength * 0.5f);

                var poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                poleObject.name = "Checkpoint Connector";
                poleObject.transform.SetParent(transform, false);
                poleObject.transform.localPosition = transform.InverseTransformDirection(connectorOffset);
                poleObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(connectorDirection));
                poleObject.transform.localScale = new Vector3(PoleRadius, connectorLength * 0.5f, PoleRadius);
                Object.Destroy(poleObject.GetComponent<Collider>());
            }
        }

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Update()
    {
        if (currentState != CheckpointVisualState.Active)
        {
            return;
        }

        var pulse = Mathf.Lerp(
            activeEmissionMin,
            activeEmissionMax,
            0.5f + 0.5f * Mathf.Sin(Time.time * activePulseSpeed));

        ApplyVisuals(PendingCheckpointColor, pulse);
    }

    public void SetState(CheckpointVisualState state)
    {
        currentState = state;
        var emission = state == CheckpointVisualState.Active ? activeEmissionMax : activeEmissionMin;
        ApplyVisuals(GetStateColor(state), emission);
    }

    private static Color GetStateColor(CheckpointVisualState state)
    {
        return state switch
        {
            CheckpointVisualState.Active => PendingCheckpointColor,
            CheckpointVisualState.Completed => CompletedCheckpointColor,
            _ => PendingCheckpointColor
        };
    }

    private void ApplyVisuals(Color color, float emissionStrength)
    {
        foreach (var sceneRenderer in cachedRenderers)
        {
            if (sceneRenderer == null)
            {
                continue;
            }

            var material = sceneRenderer.material;
            material.color = color;

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emissionStrength);
            }
        }
    }
}
