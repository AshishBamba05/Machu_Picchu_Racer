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
    private static readonly Color BasePoleColor = new(0.15f, 0.65f, 1f);

    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();
    private RaceTrackManager raceManager;
    private int checkpointIndex = -1;
    private CheckpointVisualState currentState = CheckpointVisualState.Pending;

    [SerializeField] private float activePulseSpeed = 2.4f;
    [SerializeField] private float activeEmissionMin = 0.75f;
    [SerializeField] private float activeEmissionMax = 2.2f;

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

    public void CacheRenderers()
    {
        var poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleObject.name = "Wayfinding Pole";
        poleObject.transform.SetParent(transform, false);
        poleObject.transform.localPosition = new Vector3(0f, 5f, 0f);
        poleObject.transform.localScale = new Vector3(0.35f, 5f, 0.35f);
        Object.Destroy(poleObject.GetComponent<Collider>());

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

        ApplyVisuals(BasePoleColor, pulse);
    }

    public void SetState(CheckpointVisualState state)
    {
        currentState = state;
        var emission = state == CheckpointVisualState.Active ? activeEmissionMax : activeEmissionMin;
        ApplyVisuals(BasePoleColor, emission);
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
