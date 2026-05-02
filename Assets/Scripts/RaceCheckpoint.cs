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
    private RaceTrackManager manager;
    private int checkpointIndex;
    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();

    public void Initialize(RaceTrackManager raceManager, int index)
    {
        manager = raceManager;
        checkpointIndex = index;
    }

    public void CacheRenderers()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    public void SetState(CheckpointVisualState state)
    {
        var color = state switch
        {
            CheckpointVisualState.Active => new Color(1f, 0.5f, 0.1f),
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
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager != null && manager.IsRacer(other))
        {
            manager.HandleCheckpointEntered(checkpointIndex);
        }
    }
}
