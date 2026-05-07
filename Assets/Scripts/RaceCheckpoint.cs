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
    private const float HeadArrowBaseDistance = 0.75f;
    private const float HeadArrowBaseHeight = 0.18f;
    private const float DefaultPoleHeight = 10f;
    private const float MinimumPoleHeight = 0.5f;
    private const float PoleRadius = 0.35f;
    private const float PoleGroundOffset = 0.05f;
    private const float PoleProbeDistance = 250f;

    private Renderer[] cachedRenderers = System.Array.Empty<Renderer>();
    private Transform headArrowRoot;
    private Camera headArrowCamera;
    private RaceTrackManager raceManager;
    private int checkpointIndex = -1;
    private CheckpointVisualState currentState = CheckpointVisualState.Pending;

    [SerializeField] private float activePulseSpeed = 2.4f;
    [SerializeField] private float activeEmissionMin = 0.75f;
    [SerializeField] private float activeEmissionMax = 2.2f;
    [SerializeField] private float activeArrowBobSpeed = 2f;
    [SerializeField] private float activeArrowBobDistance = 0.35f;
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

    public void CacheRenderers()
    {
        var checkpointSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        checkpointSphere.name = "Checkpoint Core";
        checkpointSphere.transform.SetParent(transform, false);
        checkpointSphere.transform.localPosition = Vector3.zero;
        checkpointSphere.transform.localScale = Vector3.one * checkpointSphereDiameter;
        Object.Destroy(checkpointSphere.GetComponent<Collider>());

        var sphereRadius = checkpointSphereDiameter * 0.5f;
        var poleHeight = GetPoleHeight(sphereRadius);
        var poleHalfHeight = poleHeight * 0.5f;

        var poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleObject.name = "Wayfinding Pole";
        poleObject.transform.SetParent(transform, false);
        poleObject.transform.localPosition = new Vector3(0f, -(sphereRadius + poleHalfHeight), 0f);
        poleObject.transform.localScale = new Vector3(PoleRadius, poleHalfHeight, PoleRadius);
        Object.Destroy(poleObject.GetComponent<Collider>());

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private float GetPoleHeight(float sphereRadius)
    {
        var origin = transform.position + Vector3.down * sphereRadius;
        if (Physics.Raycast(origin, Vector3.down, out var hit, PoleProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Max(hit.distance - PoleGroundOffset, MinimumPoleHeight);
        }

        return DefaultPoleHeight;
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

        if (EnsureHeadArrow())
        {
            var bobOffset = Mathf.Sin(Time.time * activeArrowBobSpeed) * activeArrowBobDistance;
            UpdateHeadArrowPose(bobOffset);
        }
    }

    public void SetState(CheckpointVisualState state)
    {
        currentState = state;
        var emission = state == CheckpointVisualState.Active ? activeEmissionMax : activeEmissionMin;
        ApplyVisuals(BasePoleColor, emission);

        if (state == CheckpointVisualState.Active)
        {
            if (EnsureHeadArrow())
            {
                headArrowRoot.gameObject.SetActive(true);
                UpdateHeadArrowPose(0f);
            }
        }
        else if (headArrowRoot != null)
        {
            headArrowRoot.gameObject.SetActive(false);
        }
    }

    private bool EnsureHeadArrow()
    {
        if (headArrowRoot != null && headArrowCamera != null)
        {
            return true;
        }

        headArrowCamera = Camera.main;
        if (headArrowCamera == null)
        {
            return false;
        }

        headArrowRoot = CreateArrow();
        headArrowRoot.SetParent(headArrowCamera.transform, false);
        headArrowRoot.gameObject.SetActive(false);
        return true;
    }

    private void UpdateHeadArrowPose(float bobOffset)
    {
        if (headArrowRoot == null || headArrowCamera == null)
        {
            return;
        }

        var toCheckpoint = transform.position - headArrowCamera.transform.position;
        if (toCheckpoint.sqrMagnitude < 0.0001f)
        {
            headArrowRoot.localPosition = new Vector3(0f, HeadArrowBaseHeight + bobOffset, HeadArrowBaseDistance);
            headArrowRoot.localRotation = Quaternion.identity;
            return;
        }

        var localDirection = headArrowCamera.transform.InverseTransformDirection(toCheckpoint.normalized);
        var planarDirection = new Vector2(localDirection.x, localDirection.y);
        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = localDirection.z >= 0f ? Vector2.down : Vector2.up;
        }

        planarDirection.Normalize();
        headArrowRoot.localPosition = new Vector3(
            planarDirection.x * 0.18f,
            HeadArrowBaseHeight + bobOffset + planarDirection.y * 0.12f,
            HeadArrowBaseDistance);

        var angle = Vector2.SignedAngle(Vector2.down, planarDirection);
        headArrowRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static Transform CreateArrow()
    {
        var root = new GameObject("Next Checkpoint Arrow").transform;

        var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.name = "Arrow Stem";
        stem.transform.SetParent(root, false);
        stem.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        stem.transform.localScale = new Vector3(0.025f, 0.16f, 0.025f);
        Object.Destroy(stem.GetComponent<Collider>());

        var leftHead = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftHead.name = "Arrow Head Left";
        leftHead.transform.SetParent(root, false);
        leftHead.transform.localPosition = new Vector3(-0.055f, -0.06f, 0f);
        leftHead.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        leftHead.transform.localScale = new Vector3(0.04f, 0.16f, 0.04f);
        Object.Destroy(leftHead.GetComponent<Collider>());

        var rightHead = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightHead.name = "Arrow Head Right";
        rightHead.transform.SetParent(root, false);
        rightHead.transform.localPosition = new Vector3(0.055f, -0.06f, 0f);
        rightHead.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        rightHead.transform.localScale = new Vector3(0.04f, 0.16f, 0.04f);
        Object.Destroy(rightHead.GetComponent<Collider>());

        return root;
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
