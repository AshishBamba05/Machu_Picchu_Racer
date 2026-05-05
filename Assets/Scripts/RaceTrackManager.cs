using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class RaceTrackManager : MonoBehaviour
{
    private const float CheckpointReachRadiusMeters = 9.144f;
    private const float RespawnHeightOffset = 0f;
    private const float CountdownDuration = 3f;
    private const int ExternalTrackSearchDepth = 5;

    [Header("Track Loading")]
    [SerializeField] private string preferredTrackFileName = "competition.xyz";
    [SerializeField] private string fallbackTrackFileName = "sample_track.xyz";
    [SerializeField] private bool allowProceduralFallback = false;

    private readonly List<RaceCheckpoint> checkpoints = new();
    private readonly List<Vector3> checkpointPositions = new();

    private Camera racerCamera;
    private Transform racer;
    private Travel travelController;
    private DroneViewModeController viewModeController;
    private Transform hudRoot;
    private TextMeshPro hudText;
    private Bounds courseBounds;
    private bool hasCourseBounds;
    private bool runtimePrepared;
    private bool initialCountdownPending;
    private bool stopwatchRunning;
    private bool raceFinished;
    private bool countdownActive;
    private bool loadedPreferredTrack;
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
        EnsureEnvironmentCollisionSetup();

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
        EnsureHud();
        runtimePrepared = true;
    }

    private void EnsureHud()
    {
        if (hudText != null)
        {
            return;
        }

        hudRoot = new GameObject("Race HUD").transform;
        hudRoot.SetParent(racerCamera.transform, false);
        hudRoot.localPosition = new Vector3(0f, -0.18f, 0.75f);
        hudRoot.localRotation = Quaternion.identity;
        hudRoot.localScale = Vector3.one * 0.0016f;

        var hudBackground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hudBackground.name = "HUD Background";
        hudBackground.transform.SetParent(hudRoot, false);
        hudBackground.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        hudBackground.transform.localScale = new Vector3(1.3f, 0.72f, 0.01f);
        Destroy(hudBackground.GetComponent<Collider>());

        var backgroundRenderer = hudBackground.GetComponent<MeshRenderer>();
        var backgroundShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        backgroundRenderer.material = new Material(backgroundShader);
        backgroundRenderer.material.color = new Color(0.03f, 0.06f, 0.08f, 0.82f);

        var hudTextObject = new GameObject("HUD Text");
        hudTextObject.transform.SetParent(hudRoot, false);
        hudText = hudTextObject.AddComponent<TextMeshPro>();
        hudText.alignment = TextAlignmentOptions.TopLeft;
        hudText.fontSize = 3.2f;
        hudText.color = Color.white;
        hudText.enableWordWrapping = false;
        hudText.overflowMode = TextOverflowModes.Overflow;
        hudText.rectTransform.sizeDelta = new Vector2(1100f, 560f);
        hudText.rectTransform.localPosition = new Vector3(-0.6f, 0.28f, 0f);
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

    private void EnsureEnvironmentCollisionSetup()
    {
        var repairedColliders = 0;

        foreach (var meshFilter in FindObjectsOfType<MeshFilter>(true))
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            var sceneObject = meshFilter.gameObject;
            if (sceneObject == null || sceneObject.transform.IsChildOf(racer) || sceneObject.transform.IsChildOf(transform))
            {
                continue;
            }

            var meshCollider = sceneObject.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                if (meshCollider.sharedMesh == meshFilter.sharedMesh)
                {
                    continue;
                }

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                repairedColliders++;
                continue;
            }

            if (sceneObject.GetComponent<Collider>() != null)
            {
                continue;
            }

            meshCollider = sceneObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            meshCollider.isTrigger = false;
            repairedColliders++;
        }

        if (repairedColliders > 0)
        {
            Debug.Log($"Prepared {repairedColliders} environment mesh collider(s) for crash detection.");
        }
    }

    private void LoadOrBuildTrack()
    {
        ClearTrackObjects();
        loadedPreferredTrack = false;

        if (TryLoadTrackFromFile(out var points, out var trackName))
        {
            checkpointPositions.AddRange(points);
            loadedTrackLabel = trackName;
            statusMessage = loadedPreferredTrack
                ? "Competition track loaded."
                : $"Loaded fallback track '{trackName}'.";
        }
        else if (allowProceduralFallback)
        {
            checkpointPositions.AddRange(BuildProceduralTrack());
            loadedTrackLabel = "Procedural fallback track";
            statusMessage = "Competition track missing. Using procedural fallback.";
        }
        else
        {
            loadedTrackLabel = "No track loaded.";
            statusMessage = $"Missing required track file '{preferredTrackFileName}'.";
            return;
        }

        BuildTrackVisuals();
    }

    private bool TryLoadTrackFromFile(out List<Vector3> points, out string trackName)
    {
        var candidates = new List<string>(EnumerateTrackCandidates());

        foreach (var filePath in candidates)
        {
            if (!Path.GetFileName(filePath).Equals(preferredTrackFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryLoadTrackFile(filePath, out points))
            {
                trackName = Path.GetFileName(filePath);
                loadedPreferredTrack = true;
                return true;
            }
        }

        foreach (var filePath in candidates)
        {
            if (!Path.GetFileName(filePath).Equals(fallbackTrackFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryLoadTrackFile(filePath, out points))
            {
                trackName = Path.GetFileName(filePath);
                return true;
            }
        }

        foreach (var filePath in candidates)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.Equals(preferredTrackFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(fallbackTrackFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryLoadTrackFile(filePath, out points))
            {
                trackName = fileName;
                return true;
            }
        }

        points = null;
        trackName = string.Empty;
        return false;
    }

    private static bool TryLoadTrackFile(string filePath, out List<Vector3> points)
    {
        points = null;

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            points = Parse.LoadTrack(filePath, 100);
            return points.Count >= 2;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Unable to load track file '{filePath}': {exception.Message}");
            return false;
        }
    }

    private IEnumerable<string> EnumerateTrackCandidates()
    {
        var candidates = new List<string>();
        var searchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var normalizedPath = Path.GetFullPath(path);
                if (addedFiles.Add(normalizedPath))
                {
                    candidates.Add(normalizedPath);
                }
            }
            catch (Exception)
            {
                // Ignore malformed paths and keep searching other locations.
            }
        }

        void AddDirectory(string directory, int recursiveDepth = 0)
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

            if (recursiveDepth <= 0)
            {
                return;
            }

            foreach (var subdirectory in Directory.GetDirectories(directory))
            {
                AddDirectory(subdirectory, recursiveDepth - 1);
            }
        }

        void AddDriveRoot(string directory)
        {
            AddDirectory(directory, ExternalTrackSearchDepth);
        }

        void AddExternalMediaCandidates()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    if (drive.DriveType == DriveType.Removable)
                    {
                        AddDriveRoot(drive.RootDirectory.FullName);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to inspect external drives: {exception.Message}");
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            AddDriveRoot("/Volumes");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            AddDriveRoot("/media");
            AddDriveRoot("/mnt");
            AddDriveRoot("/run/media");
#endif
        }

        AddDirectory(Application.streamingAssetsPath);
        AddDirectory(Application.persistentDataPath);
        AddDirectory(Path.GetDirectoryName(Application.dataPath));
        AddDirectory(Environment.CurrentDirectory);
        AddExternalMediaCandidates();

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

        checkpoint.CacheRenderers();
        checkpoint.SetState(CheckpointVisualState.Pending);
        return checkpoint;
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

    private void LateUpdate()
    {
        UpdateHud();
    }

    private void UpdateHud()
    {
        if (hudText == null)
        {
            return;
        }

        var displayedTime = raceFinished
            ? finishTime
            : stopwatchRunning
                ? Time.time - raceStartTime
                : 0f;

        var nextCheckpointLine = raceFinished
            ? "Track complete! Press R to run again."
            : $"Next checkpoint: {Mathf.Min(nextCheckpointIndex + 1, checkpoints.Count)}/{checkpoints.Count}";

        var bestTimeLine = bestTime > 0f
            ? $"Best time: {bestTime:0.00}s"
            : "Best time: --";

        var distanceLine = "Distance to next: --";
        if (!raceFinished && checkpointPositions.Count > 0 && nextCheckpointIndex < checkpointPositions.Count && racer != null)
        {
            var distance = Vector3.Distance(racer.position, checkpointPositions[nextCheckpointIndex]);
            distanceLine = $"Distance to next: {distance:0.0}m";
        }

        var countdownLine = countdownActive
            ? $"Countdown: {Mathf.CeilToInt(Mathf.Max(0f, countdownEndTime - Time.time))}"
            : "Countdown: --";

        hudText.text =
            $"Race Track\n" +
            $"Track: {loadedTrackLabel}\n" +
            $"Time: {displayedTime:0.00}s\n" +
            $"{bestTimeLine}\n" +
            $"{nextCheckpointLine}\n" +
            $"{distanceLine}\n" +
            $"{countdownLine}\n" +
            $"{statusMessage}";
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
            attachedBody.linearVelocity = Vector3.zero;
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

            if (sceneRenderer is TrailRenderer)
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
