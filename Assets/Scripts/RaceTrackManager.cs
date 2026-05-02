using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RaceTrackManager : MonoBehaviour
{
    private const int CheckpointCount = 8;

    private readonly List<RaceCheckpoint> checkpoints = new();

    private Camera racerCamera;
    private Transform racer;
    private LineRenderer pathLine;
    private Bounds courseBounds;
    private bool hasCourseBounds;
    private bool raceActive;
    private bool raceFinished;
    private int nextCheckpointIndex;
    private float raceStartTime;
    private float finishTime;
    private float bestTime = -1f;
    private string statusMessage = "Fly through the glowing checkpoints in order.";
    private Vector3 respawnPosition;
    private Quaternion respawnRotation;

    private void Start()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (racer == null)
        {
            TryInitialize();
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRace(true);
        }

        if (raceActive && hasCourseBounds)
        {
            var horizontalDistance = Vector2.Distance(
                new Vector2(racer.position.x, racer.position.z),
                new Vector2(courseBounds.center.x, courseBounds.center.z));

            var maxDistance = Mathf.Max(courseBounds.extents.x, courseBounds.extents.z) * 2.4f;
            if (horizontalDistance > maxDistance)
            {
                statusMessage = "You drifted away from the course. Press R to restart.";
            }
        }
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(16f, 16f, 360f, 172f), "Race Track");

        var displayedTime = raceFinished ? finishTime : raceActive ? Time.time - raceStartTime : 0f;
        GUI.Label(new Rect(28f, 46f, 320f, 24f), $"Time: {displayedTime:0.00}s");
        GUI.Label(
            new Rect(28f, 70f, 320f, 24f),
            raceFinished
                ? "Track complete! Press R to run again."
                : $"Next checkpoint: {Mathf.Min(nextCheckpointIndex + 1, checkpoints.Count)}/{checkpoints.Count}");

        if (bestTime > 0f)
        {
            GUI.Label(new Rect(28f, 94f, 320f, 24f), $"Best time: {bestTime:0.00}s");
        }

        GUI.Label(new Rect(28f, 118f, 332f, 24f), statusMessage);

        if (!raceFinished && checkpoints.Count > 0 && nextCheckpointIndex < checkpoints.Count)
        {
            var nextCheckpoint = checkpoints[nextCheckpointIndex].transform.position;
            var distance = Vector3.Distance(racer.position, nextCheckpoint);
            GUI.Label(new Rect(28f, 142f, 332f, 24f), $"Distance: {distance:0.0}m");
        }
    }

    public bool IsRacer(Collider other)
    {
        return racer != null && other.transform.root == racer.root;
    }

    public void HandleCheckpointEntered(int checkpointIndex)
    {
        if (raceFinished)
        {
            return;
        }

        if (checkpointIndex != nextCheckpointIndex)
        {
            statusMessage = $"Wrong checkpoint. Head to checkpoint {nextCheckpointIndex + 1}.";
            return;
        }

        checkpoints[checkpointIndex].SetState(CheckpointVisualState.Completed);
        nextCheckpointIndex++;

        if (nextCheckpointIndex >= checkpoints.Count)
        {
            FinishRace();
            return;
        }

        checkpoints[nextCheckpointIndex].SetState(CheckpointVisualState.Active);
        statusMessage = $"Checkpoint {checkpointIndex + 1} cleared.";
    }

    private void TryInitialize()
    {
        racerCamera = Camera.main;
        if (racerCamera == null)
        {
            statusMessage = "Waiting for a Main Camera to spawn.";
            return;
        }

        racer = racerCamera.transform;
        EnsureRacerCollisionSetup();

        if (checkpoints.Count == 0)
        {
            BuildTrack();
        }
        ResetRace(false);
    }

    private void EnsureRacerCollisionSetup()
    {
        if (racer == null)
        {
            return;
        }

        var sphereCollider = racer.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = racer.gameObject.AddComponent<SphereCollider>();
            sphereCollider.radius = 0.35f;
            sphereCollider.center = Vector3.zero;
        }

        var rigidbody = racer.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = racer.gameObject.AddComponent<Rigidbody>();
        }

        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void BuildTrack()
    {
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint != null)
            {
                Destroy(checkpoint.gameObject);
            }
        }

        checkpoints.Clear();

        hasCourseBounds = TryGetSceneBounds(out courseBounds);
        var center = hasCourseBounds ? courseBounds.center : Vector3.zero;
        var height = hasCourseBounds ? Mathf.Max(courseBounds.extents.y, 8f) : 12f;
        var radiusX = hasCourseBounds ? Mathf.Max(courseBounds.extents.x * 1.6f, 18f) : 22f;
        var radiusZ = hasCourseBounds ? Mathf.Max(courseBounds.extents.z * 1.6f, 18f) : 22f;
        var baseHeight = hasCourseBounds ? Mathf.Max(center.y + courseBounds.extents.y * 0.55f, 6f) : 8f;

        var positions = new List<Vector3>(CheckpointCount);
        for (var i = 0; i < CheckpointCount; i++)
        {
            var angle = (Mathf.PI * 2f * i / CheckpointCount) + (Mathf.PI * 0.2f);
            var x = Mathf.Cos(angle) * radiusX;
            var z = Mathf.Sin(angle) * radiusZ;
            var yOffset = Mathf.Sin(angle * 2f) * (height * 0.25f) + ((i % 2 == 0) ? 2f : -1.5f);
            positions.Add(center + new Vector3(x, baseHeight + yOffset, z));
        }

        for (var i = 0; i < positions.Count; i++)
        {
            var nextPosition = positions[(i + 1) % positions.Count];
            checkpoints.Add(CreateCheckpoint(i, positions[i], nextPosition));
        }

        pathLine = gameObject.GetComponent<LineRenderer>();
        if (pathLine == null)
        {
            pathLine = gameObject.AddComponent<LineRenderer>();
        }

        pathLine.material = new Material(Shader.Find("Sprites/Default"));
        pathLine.widthMultiplier = 0.2f;
        pathLine.positionCount = positions.Count + 1;
        pathLine.loop = false;
        pathLine.startColor = new Color(1f, 0.85f, 0.2f, 0.55f);
        pathLine.endColor = new Color(0.2f, 0.85f, 1f, 0.55f);
        pathLine.useWorldSpace = true;

        for (var i = 0; i < positions.Count; i++)
        {
            pathLine.SetPosition(i, positions[i]);
        }

        pathLine.SetPosition(positions.Count, positions[0]);

        respawnPosition = positions[0] - (positions[1] - positions[0]).normalized * 4f + Vector3.up * 1.2f;
        respawnRotation = Quaternion.LookRotation((positions[1] - positions[0]).normalized, Vector3.up);
    }

    private RaceCheckpoint CreateCheckpoint(int index, Vector3 position, Vector3 nextPosition)
    {
        var checkpointRoot = new GameObject($"Checkpoint {index + 1}");
        checkpointRoot.transform.SetParent(transform, false);
        checkpointRoot.transform.position = position;
        checkpointRoot.transform.rotation = Quaternion.LookRotation(nextPosition - position, Vector3.up);

        var trigger = checkpointRoot.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(5f, 4f, 1.8f);

        var checkpoint = checkpointRoot.AddComponent<RaceCheckpoint>();
        checkpoint.Initialize(this, index);

        CreatePole(checkpointRoot.transform, new Vector3(-2f, 0f, 0f));
        CreatePole(checkpointRoot.transform, new Vector3(2f, 0f, 0f));
        CreateBeam(checkpointRoot.transform, new Vector3(0f, 2f, 0f), new Vector3(4.3f, 0.25f, 0.25f));
        CreateBeam(checkpointRoot.transform, new Vector3(0f, -2f, 0f), new Vector3(4.3f, 0.25f, 0.25f));
        CreateBeacon(checkpointRoot.transform);

        checkpoint.CacheRenderers();
        checkpoint.SetState(index == 0 ? CheckpointVisualState.Active : CheckpointVisualState.Pending);
        return checkpoint;
    }

    private static void CreatePole(Transform parent, Vector3 localPosition)
    {
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(parent, false);
        pole.transform.localPosition = localPosition;
        pole.transform.localScale = new Vector3(0.18f, 2f, 0.18f);
        Destroy(pole.GetComponent<Collider>());
    }

    private static void CreateBeam(Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Beam";
        beam.transform.SetParent(parent, false);
        beam.transform.localPosition = localPosition;
        beam.transform.localScale = localScale;
        Destroy(beam.GetComponent<Collider>());
    }

    private static void CreateBeacon(Transform parent)
    {
        var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        beacon.name = "Beacon";
        beacon.transform.SetParent(parent, false);
        beacon.transform.localPosition = new Vector3(0f, 3f, 0f);
        beacon.transform.localScale = Vector3.one * 0.7f;
        Destroy(beacon.GetComponent<Collider>());
    }

    private void ResetRace(bool forceRespawn)
    {
        if (checkpoints.Count == 0 || racer == null)
        {
            return;
        }

        nextCheckpointIndex = 0;
        raceStartTime = Time.time;
        finishTime = 0f;
        raceActive = true;
        raceFinished = false;
        statusMessage = "Race started. Fly through checkpoint 1.";

        for (var i = 0; i < checkpoints.Count; i++)
        {
            checkpoints[i].SetState(i == 0 ? CheckpointVisualState.Active : CheckpointVisualState.Pending);
        }

        if (forceRespawn)
        {
            racer.SetPositionAndRotation(respawnPosition, respawnRotation);

            var attachedBody = racer.GetComponent<Rigidbody>();
            if (attachedBody != null)
            {
                attachedBody.linearVelocity = Vector3.zero;
                attachedBody.angularVelocity = Vector3.zero;
            }
        }
    }

    private void FinishRace()
    {
        raceActive = false;
        raceFinished = true;
        finishTime = Time.time - raceStartTime;
        bestTime = bestTime < 0f ? finishTime : Mathf.Min(bestTime, finishTime);
        statusMessage = "Finish line cleared.";
    }

    private bool TryGetSceneBounds(out Bounds bounds)
    {
        var renderers = FindObjectsOfType<Renderer>();
        var foundBounds = false;
        bounds = default;

        foreach (var sceneRenderer in renderers)
        {
            if (!sceneRenderer.enabled || sceneRenderer.transform.IsChildOf(transform))
            {
                continue;
            }

            if (sceneRenderer is TrailRenderer || sceneRenderer is LineRenderer)
            {
                continue;
            }

            if (!foundBounds)
            {
                bounds = sceneRenderer.bounds;
                foundBounds = true;
            }
            else
            {
                bounds.Encapsulate(sceneRenderer.bounds);
            }
        }

        return foundBounds;
    }
}
