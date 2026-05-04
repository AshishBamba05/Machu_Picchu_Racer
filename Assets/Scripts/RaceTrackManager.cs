using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public class RaceTrackManager : MonoBehaviour
{
    private const float CheckpointReachRadiusMeters = 9.144f;
    private const float RespawnHeightOffset = 0f;
    private const float CountdownDuration = 3f;

    [Header("Track Loading")]
    [SerializeField] private string preferredTrackFileName = "competition.xyz";
    [SerializeField] private string fallbackTrackFileName = "sample_track.xyz";
    [SerializeField] private bool allowProceduralFallback = true;

    private readonly List<RaceCheckpoint> checkpoints = new();
    private readonly List<Vector3> checkpointPositions = new();

    private Camera racerCamera;
    private Transform racer;
    private Travel travelController;
    private DroneViewModeController viewModeController;
    private LineRenderer pathLine;
    private Bounds courseBounds;
    private bool hasCourseBounds;
    private bool runtimePrepared;
    private bool initialCountdownPending;
    private bool stopwatchRunning;
    private bool raceFinished;
    private bool countdownActive;
    private int nextCheckpointIndex;
    private int lastClearedCheckpointIndex;
    private float countdownEndTime;
    private float raceStartTime;
    private float finishTime;
    private float bestTime = -1f;
    private string statusMessage = "Waiting for hand-tracked drone setup.";
    private string loadedTrackLabel = "No track loaded.";

    private void Start()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (racer == null || racerCamera == null)
        {
            TryInitialize();
            return;
        }

        if (!runtimePrepared)
        {
            TryInitialize();
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRace(true, false);
        }

        UpdateCountdown();
        UpdateCheckpointProgress();
        UpdateCourseWarning();
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(16f, 16f, 420f, 224f), "Race Track");

        var displayedTime = raceFinished
            ? finishTime
            : stopwatchRunning
                ? Time.time - raceStartTime
                : 0f;

        GUI.Label(new Rect(28f, 46f, 380f, 24f), $"Track: {loadedTrackLabel}");
        GUI.Label(new Rect(28f, 70f, 380f, 24f), $"Time: {displayedTime:0.00}s");
        GUI.Label(
            new Rect(28f, 94f, 380f, 24f),
            raceFinished
                ? "Track complete! Press R to run again."
                : $"Next checkpoint: {Mathf.Min(nextCheckpointIndex + 1, checkpoints.Count)}/{checkpoints.Count}");

        if (bestTime > 0f)
        {
            GUI.Label(new Rect(28f, 118f, 380f, 24f), $"Best time: {bestTime:0.00}s");
        }

        if (!raceFinished && checkpointPositions.Count > 0 && nextCheckpointIndex < checkpointPositions.Count && racer != null)
        {
            var distance = Vector3.Distance(racer.position, checkpointPositions[nextCheckpointIndex]);
            GUI.Label(new Rect(28f, 142f, 380f, 24f), $"Distance to next: {distance:0.0}m");
        }

        if (countdownActive)
        {
            var remaining = Mathf.Max(0f, countdownEndTime - Time.time);
            GUI.Label(new Rect(28f, 166f, 380f, 24f), $"Countdown: {Mathf.CeilToInt(remaining)}");
        }

        GUI.Label(new Rect(28f, 190f, 380f, 24f), statusMessage);
    }

    public bool IsRacer(Collider other)
    {
        return racer != null && other.transform.root == racer;
    }

    public bool IsCheckpoint(Collider other)
    {
        return other.GetComponent<RaceCheckpoint>() != null;
    }

    private void TryInitialize()
    {
        racerCamera = Camera.main;
        if (racerCamera == null)
        {
            statusMessage = "Waiting for a Main Camera to spawn.";
            return;
        }

        racer = racerCamera.transform.root;
        PrepareRuntimeRig();

        if (checkpointPositions.Count == 0)
        {
            LoadOrBuildTrack();
        }

        if (checkpointPositions.Count >= 2)
        {
            ResetRace(false, true);
        }
    }

    private void PrepareRuntimeRig()
    {
        if (runtimePrepared || racer == null || racerCamera == null)
        {
            return;
        }

        DisableConflictingLocomotion();
        EnsureRacerCollisionSetup();

        travelController = racer.GetComponent<Travel>();
        if (travelController == null)
        {
            travelController = racer.gameObject.AddComponent<Travel>();
        }

        travelController.drone = racer;
        travelController.canMove = false;
        travelController.TriggerEntered -= HandleRacerTriggerEntered;
        travelController.TriggerEntered += HandleRacerTriggerEntered;

        viewModeController = racer.GetComponent<DroneViewModeController>();
        if (viewModeController == null)
        {
            viewModeController = racer.gameObject.AddComponent<DroneViewModeController>();
        }

        viewModeController.Initialize(racer, racerCamera);
        runtimePrepared = true;
    }

    private void DisableConflictingLocomotion()
    {
        var locomotionTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActionBasedControllerManager",
            "ContinuousMoveProvider",
            "ContinuousMoveProviderBase",
            "ContinuousTurnProvider",
            "ContinuousTurnProviderBase",
            "DynamicMoveProvider",
            "SnapTurnProvider",
            "SnapTurnProviderBase",
            "GrabMoveProvider",
            "TwoHandedGrabMoveProvider",
            "ClimbProvider",
            "TeleportationProvider",
            "CharacterControllerDriver",
            "TunnelingVignetteController",
            "JumpProvider"
        };

        foreach (var behaviour in racer.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
            {
                continue;
            }

            if (locomotionTypes.Contains(behaviour.GetType().Name))
            {
                behaviour.enabled = false;
            }
        }
    }

    private void EnsureRacerCollisionSetup()
    {
        var sphereCollider = racer.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = racer.gameObject.AddComponent<SphereCollider>();
        }

        sphereCollider.radius = 0.35f;
        sphereCollider.center = Vector3.zero;
        sphereCollider.isTrigger = true;

        var rigidbody = racer.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = racer.gameObject.AddComponent<Rigidbody>();
        }

        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void LoadOrBuildTrack()
    {
        ClearTrackObjects();

        if (TryLoadTrackFromFile(out var points, out var trackName))
        {
            checkpointPositions.AddRange(points);
            loadedTrackLabel = trackName;
        }
        else if (allowProceduralFallback)
        {
            checkpointPositions.AddRange(BuildProceduralTrack());
            loadedTrackLabel = "Procedural fallback track";
        }
        else
        {
            statusMessage = "No .xyz track file found.";
            return;
        }

        BuildTrackVisuals();
    }

    private bool TryLoadTrackFromFile(out List<Vector3> points, out string trackName)
    {
        foreach (var filePath in EnumerateTrackCandidates())
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            try
            {
                points = Parse.LoadTrack(filePath, 100);
                if (points.Count >= 2)
                {
                    trackName = Path.GetFileName(filePath);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load track file '{filePath}': {exception.Message}");
            }
        }

        points = null;
        trackName = string.Empty;
        return false;
    }

    private IEnumerable<string> EnumerateTrackCandidates()
    {
        var candidates = new List<string>();
        var searchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path);
            }
        }

        void AddDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || !searchedDirectories.Add(directory))
            {
                return;
            }

            AddFile(Path.Combine(directory, preferredTrackFileName));
            AddFile(Path.Combine(directory, fallbackTrackFileName));

            foreach (var filePath in Directory.GetFiles(directory, "*.xyz"))
            {
                AddFile(filePath);
            }
        }

        AddDirectory(Application.streamingAssetsPath);
        AddDirectory(Application.persistentDataPath);
        AddDirectory(Path.GetDirectoryName(Application.dataPath));

        return candidates;
    }

    private List<Vector3> BuildProceduralTrack()
    {
        hasCourseBounds = TryGetSceneBounds(out courseBounds);
        var center = hasCourseBounds ? courseBounds.center : Vector3.zero;
        var height = hasCourseBounds ? Mathf.Max(courseBounds.extents.y, 8f) : 12f;
        var radiusX = hasCourseBounds ? Mathf.Max(courseBounds.extents.x * 1.6f, 18f) : 22f;
        var radiusZ = hasCourseBounds ? Mathf.Max(courseBounds.extents.z * 1.6f, 18f) : 22f;
        var baseHeight = hasCourseBounds ? Mathf.Max(center.y + courseBounds.extents.y * 0.55f, 6f) : 8f;

        var positions = new List<Vector3>(8);
        for (var index = 0; index < 8; index++)
        {
            var angle = (Mathf.PI * 2f * index / 8f) + (Mathf.PI * 0.2f);
            var x = Mathf.Cos(angle) * radiusX;
            var z = Mathf.Sin(angle) * radiusZ;
            var yOffset = Mathf.Sin(angle * 2f) * (height * 0.25f) + ((index % 2 == 0) ? 2f : -1.5f);
            positions.Add(center + new Vector3(x, baseHeight + yOffset, z));
        }

        return positions;
    }

    private void BuildTrackVisuals()
    {
        hasCourseBounds = TryGetSceneBounds(out courseBounds);

        for (var index = 0; index < checkpointPositions.Count; index++)
        {
            var nextPosition = checkpointPositions[Mathf.Min(index + 1, checkpointPositions.Count - 1)];
            if (index == checkpointPositions.Count - 1)
            {
                nextPosition = checkpointPositions[index];
            }

            checkpoints.Add(CreateCheckpoint(index, checkpointPositions[index], nextPosition));
        }

        pathLine = gameObject.GetComponent<LineRenderer>();
        if (pathLine == null)
        {
            pathLine = gameObject.AddComponent<LineRenderer>();
        }

        pathLine.material = new Material(Shader.Find("Sprites/Default"));
        pathLine.widthMultiplier = 0.2f;
        pathLine.positionCount = checkpointPositions.Count;
        pathLine.loop = false;
        pathLine.startColor = new Color(1f, 0.85f, 0.2f, 0.55f);
        pathLine.endColor = new Color(0.2f, 0.85f, 1f, 0.55f);
        pathLine.useWorldSpace = true;

        for (var index = 0; index < checkpointPositions.Count; index++)
        {
            pathLine.SetPosition(index, checkpointPositions[index]);
        }
    }

    private RaceCheckpoint CreateCheckpoint(int index, Vector3 position, Vector3 nextPosition)
    {
        var checkpointRoot = new GameObject($"Checkpoint {index + 1}");
        checkpointRoot.transform.SetParent(transform, false);
        checkpointRoot.transform.position = position;
        checkpointRoot.transform.rotation = Quaternion.LookRotation(
            (nextPosition - position).sqrMagnitude > 0.001f ? nextPosition - position : Vector3.forward,
            Vector3.up);

        var trigger = checkpointRoot.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = CheckpointReachRadiusMeters;

        var checkpoint = checkpointRoot.AddComponent<RaceCheckpoint>();
        checkpoint.Initialize(this, index);

        CreatePole(checkpointRoot.transform, new Vector3(-2f, 0f, 0f));
        CreatePole(checkpointRoot.transform, new Vector3(2f, 0f, 0f));
        CreateBeam(checkpointRoot.transform, new Vector3(0f, 2f, 0f), new Vector3(4.3f, 0.25f, 0.25f));
        CreateBeam(checkpointRoot.transform, new Vector3(0f, -2f, 0f), new Vector3(4.3f, 0.25f, 0.25f));
        CreateBeacon(checkpointRoot.transform);

        checkpoint.CacheRenderers();
        checkpoint.SetState(CheckpointVisualState.Pending);
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

    private void ResetRace(bool forceRespawn, bool initialStart)
    {
        if (checkpoints.Count < 2 || racer == null || travelController == null)
        {
            return;
        }

        raceFinished = false;
        stopwatchRunning = false;
        initialCountdownPending = true;
        countdownActive = true;
        countdownEndTime = Time.time + CountdownDuration;
        nextCheckpointIndex = 1;
        lastClearedCheckpointIndex = 0;
        finishTime = 0f;
        statusMessage = "Race ready. Hold position for countdown.";

        for (var index = 0; index < checkpoints.Count; index++)
        {
            var state = index == 0
                ? CheckpointVisualState.Completed
                : index == 1
                    ? CheckpointVisualState.Active
                    : CheckpointVisualState.Pending;
            checkpoints[index].SetState(state);
        }

        if (forceRespawn || initialStart)
        {
            RespawnAtCheckpoint(0);
        }

        travelController.canMove = false;
    }

    private void UpdateCountdown()
    {
        if (!countdownActive || Time.time < countdownEndTime)
        {
            return;
        }

        countdownActive = false;
        travelController.canMove = !raceFinished;

        if (initialCountdownPending)
        {
            raceStartTime = Time.time;
            stopwatchRunning = true;
            initialCountdownPending = false;
        }

        statusMessage = $"Checkpoint {nextCheckpointIndex + 1} is live.";
    }

    private void UpdateCheckpointProgress()
    {
        if (countdownActive || raceFinished || nextCheckpointIndex >= checkpointPositions.Count || racer == null)
        {
            return;
        }

        var nextCheckpointPosition = checkpointPositions[nextCheckpointIndex];
        if (Vector3.Distance(racer.position, nextCheckpointPosition) > CheckpointReachRadiusMeters)
        {
            return;
        }

        checkpoints[nextCheckpointIndex].SetState(CheckpointVisualState.Completed);
        lastClearedCheckpointIndex = nextCheckpointIndex;
        nextCheckpointIndex++;

        if (nextCheckpointIndex >= checkpointPositions.Count)
        {
            FinishRace();
            return;
        }

        checkpoints[nextCheckpointIndex].SetState(CheckpointVisualState.Active);
        statusMessage = $"Checkpoint {lastClearedCheckpointIndex + 1} cleared.";
    }

    private void UpdateCourseWarning()
    {
        if (!stopwatchRunning || raceFinished || !hasCourseBounds || racer == null)
        {
            return;
        }

        var horizontalDistance = Vector2.Distance(
            new Vector2(racer.position.x, racer.position.z),
            new Vector2(courseBounds.center.x, courseBounds.center.z));

        var maxDistance = Mathf.Max(courseBounds.extents.x, courseBounds.extents.z) * 2.8f;
        if (horizontalDistance > maxDistance)
        {
            statusMessage = "You drifted away from the course. Press R to restart.";
        }
    }

    private void FinishRace()
    {
        raceFinished = true;
        stopwatchRunning = false;
        travelController.canMove = false;
        finishTime = Time.time - raceStartTime;
        bestTime = bestTime < 0f ? finishTime : Mathf.Min(bestTime, finishTime);
        statusMessage = "Finish line cleared.";
    }

    private void HandleRacerTriggerEntered(Collider other)
    {
        if (raceFinished || countdownActive || other == null)
        {
            return;
        }

        if (IsCheckpoint(other))
        {
            return;
        }

        if (other.transform.IsChildOf(racer))
        {
            return;
        }

        CrashAndRespawn();
    }

    private void CrashAndRespawn()
    {
        travelController.canMove = false;
        countdownActive = true;
        countdownEndTime = Time.time + CountdownDuration;
        statusMessage = "Crash detected. Returning to last cleared checkpoint.";
        RespawnAtCheckpoint(lastClearedCheckpointIndex);
    }

    private void RespawnAtCheckpoint(int checkpointIndex)
    {
        var spawnPosition = checkpointPositions[checkpointIndex] + Vector3.up * RespawnHeightOffset;
        var facingTarget = checkpointPositions[Mathf.Min(checkpointIndex + 1, checkpointPositions.Count - 1)];
        var flatDirection = facingTarget - checkpointPositions[checkpointIndex];
        flatDirection.y = 0f;

        var spawnRotation = flatDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatDirection.normalized, Vector3.up)
            : Quaternion.identity;

        racer.SetPositionAndRotation(spawnPosition, spawnRotation);

        var attachedBody = racer.GetComponent<Rigidbody>();
        if (attachedBody != null)
        {
            attachedBody.velocity = Vector3.zero;
            attachedBody.angularVelocity = Vector3.zero;
        }
    }

    private void ClearTrackObjects()
    {
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint != null)
            {
                Destroy(checkpoint.gameObject);
            }
        }

        checkpoints.Clear();
        checkpointPositions.Clear();

        if (pathLine != null)
        {
            Destroy(pathLine);
            pathLine = null;
        }
    }

    private bool TryGetSceneBounds(out Bounds bounds)
    {
        var renderers = FindObjectsOfType<Renderer>();
        var foundBounds = false;
        bounds = default;

        foreach (var sceneRenderer in renderers)
        {
            if (!sceneRenderer.enabled || sceneRenderer.transform.IsChildOf(transform) || sceneRenderer.transform.IsChildOf(racer))
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
