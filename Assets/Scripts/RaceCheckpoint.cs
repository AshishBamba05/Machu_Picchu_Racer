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
    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();
    private Transform wayfindingPole;
    private RaceTrackManager raceManager;
    private int checkpointIndex = -1;

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
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        var poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleObject.name = "Wayfinding Pole";
        poleObject.transform.SetParent(transform, false);
        poleObject.transform.localPosition = new Vector3(0f, 7f, 0f);
        poleObject.transform.localScale = new Vector3(0.35f, 5f, 0.35f);
        Object.Destroy(poleObject.GetComponent<Collider>());
        wayfindingPole = poleObject.transform;
        wayfindingPole.gameObject.SetActive(false);
    }

    public void SetState(CheckpointVisualState state)
    {
        var color = state switch
        {
            CheckpointVisualState.Active => new Color(0.15f, 0.65f, 1f),
            CheckpointVisualState.Completed => new Color(0.2f, 1f, 0.5f),
            _ => new Color(0.15f, 0.8f, 1f)
        };

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
                material.SetColor("_EmissionColor", color * 0.8f);
            }
        }

        if (wayfindingPole != null)
        {
            wayfindingPole.gameObject.SetActive(state == CheckpointVisualState.Active);
        }
    }
}
