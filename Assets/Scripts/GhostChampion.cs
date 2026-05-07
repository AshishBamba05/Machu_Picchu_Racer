using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GhostChampion : MonoBehaviour
{
    private static readonly Color GhostColor = new Color(0.35f, 0.95f, 1f, 0.32f);

    [Serializable]
    public class GhostFrame
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class GhostRunData
    {
        public float totalTime;
        public List<GhostFrame> frames = new List<GhostFrame>();
    }

    [Header("References")]
    public Transform playerDrone;
    public Transform ghostDrone;

    [Header("Recording Settings")]
    public float samplesPerSecond = 90f;

    [Header("Ghost Visual")]
    public bool hideGhostWhenNotPlaying = true;

    private GhostRunData currentRun = new GhostRunData();
    private GhostRunData bestRun;

    private bool isRecording = false;
    private bool isReplaying = false;

    private float recordingStartTime;
    private float replayStartTime;
    private float nextSampleTime;

    private string SavePath
    {
        get
        {
            return Path.Combine(
                Application.persistentDataPath,
                "ghost_champion_best_run.json"
            );
        }
    }

    private void Start()
    {
        EnsureGhostDroneVisual();
        LoadBestRun();

        if (ghostDrone != null && hideGhostWhenNotPlaying)
            ghostDrone.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (isRecording)
            RecordUpdate();

        if (isReplaying)
            ReplayUpdate();
    }

    // Call this when the race starts
    public void StartRecording()
    {
        if (playerDrone == null)
            return;

        currentRun = new GhostRunData();

        recordingStartTime = Time.time;
        nextSampleTime = 0f;
        isRecording = true;
        RecordFrame(0f);
    }

    // Call this when the race finishes
    public void StopRecordingAndSaveIfBest(float finalRaceTime)
    {
        if (!isRecording)
            return;

        RecordFrame(finalRaceTime);
        isRecording = false;
        currentRun.totalTime = finalRaceTime;

        bool noBestYet = bestRun == null || bestRun.frames == null || bestRun.frames.Count == 0;
        bool isNewBest = noBestYet || finalRaceTime < bestRun.totalTime;

        if (isNewBest)
        {
            bestRun = currentRun;
            SaveBestRun();
        }
    }

    // Call this at race start if you want the ghost to race with you
    public void StartGhostReplay()
    {
        EnsureGhostDroneVisual();

        if (ghostDrone == null)
            return;

        if (bestRun == null || bestRun.frames == null || bestRun.frames.Count < 2)
            return;

        ghostDrone.gameObject.SetActive(true);

        ghostDrone.position = bestRun.frames[0].position;
        ghostDrone.rotation = bestRun.frames[0].rotation;

        replayStartTime = Time.time;
        isReplaying = true;
    }

    public void StopGhostReplay()
    {
        isReplaying = false;

        if (ghostDrone != null && hideGhostWhenNotPlaying)
            ghostDrone.gameObject.SetActive(false);
    }

    public void ResetRunState()
    {
        isRecording = false;
        isReplaying = false;
        currentRun = new GhostRunData();

        if (ghostDrone != null && hideGhostWhenNotPlaying)
            ghostDrone.gameObject.SetActive(false);
    }

    private void RecordUpdate()
    {
        float elapsed = Time.time - recordingStartTime;
        float sampleInterval = 1f / samplesPerSecond;

        while (elapsed >= nextSampleTime)
        {
            RecordFrame(nextSampleTime);
            nextSampleTime += sampleInterval;
        }
    }

    private void ReplayUpdate()
    {
        if (bestRun == null || bestRun.frames == null || bestRun.frames.Count < 2)
            return;

        float replayTime = Time.time - replayStartTime;

        if (replayTime >= bestRun.totalTime)
        {
            StopGhostReplay();
            return;
        }

        GhostFrame before = bestRun.frames[0];
        GhostFrame after = bestRun.frames[bestRun.frames.Count - 1];

        for (int i = 0; i < bestRun.frames.Count - 1; i++)
        {
            if (bestRun.frames[i].time <= replayTime &&
                bestRun.frames[i + 1].time >= replayTime)
            {
                before = bestRun.frames[i];
                after = bestRun.frames[i + 1];
                break;
            }
        }

        float timeRange = after.time - before.time;
        float t = 0f;

        if (timeRange > 0.0001f)
            t = (replayTime - before.time) / timeRange;

        ghostDrone.position = Vector3.Lerp(before.position, after.position, t);
        ghostDrone.rotation = Quaternion.Slerp(before.rotation, after.rotation, t);
    }

    private void SaveBestRun()
    {
        string json = JsonUtility.ToJson(bestRun, true);
        File.WriteAllText(SavePath, json);
    }

    private void LoadBestRun()
    {
        if (!File.Exists(SavePath))
            return;

        string json = File.ReadAllText(SavePath);
        bestRun = JsonUtility.FromJson<GhostRunData>(json);
    }

    public bool HasBestRun()
    {
        return bestRun != null &&
               bestRun.frames != null &&
               bestRun.frames.Count > 1;
    }

    private void RecordFrame(float timestamp)
    {
        if (playerDrone == null)
            return;

        if (currentRun.frames.Count > 0)
        {
            GhostFrame previousFrame = currentRun.frames[currentRun.frames.Count - 1];
            if (Mathf.Abs(previousFrame.time - timestamp) < 0.0001f)
            {
                previousFrame.position = playerDrone.position;
                previousFrame.rotation = playerDrone.rotation;
                return;
            }
        }

        GhostFrame frame = new GhostFrame
        {
            time = timestamp,
            position = playerDrone.position,
            rotation = playerDrone.rotation
        };

        currentRun.frames.Add(frame);
    }

    private void EnsureGhostDroneVisual()
    {
        if (ghostDrone != null)
            return;

        GameObject ghostRoot = new GameObject("Ghost Champion Drone");
        ghostRoot.transform.SetParent(transform, false);
        ghostDrone = ghostRoot.transform;

        CreateGhostPrimitive(ghostDrone, PrimitiveType.Cube, "Core", Vector3.zero, new Vector3(0.28f, 0.08f, 0.32f), Vector3.zero);
        CreateGhostPrimitive(ghostDrone, PrimitiveType.Cylinder, "Arm A", Vector3.zero, new Vector3(0.03f, 0.34f, 0.03f), new Vector3(0f, 0f, 45f));
        CreateGhostPrimitive(ghostDrone, PrimitiveType.Cylinder, "Arm B", Vector3.zero, new Vector3(0.03f, 0.34f, 0.03f), new Vector3(0f, 0f, -45f));
        CreateGhostPrimitive(ghostDrone, PrimitiveType.Capsule, "Nose", new Vector3(0f, 0f, 0.3f), new Vector3(0.08f, 0.18f, 0.14f), new Vector3(90f, 0f, 0f));

        Vector3[] propOffsets =
        {
            new Vector3(0.48f, 0f, 0.48f),
            new Vector3(-0.48f, 0f, 0.48f),
            new Vector3(0.48f, 0f, -0.48f),
            new Vector3(-0.48f, 0f, -0.48f)
        };

        for (int index = 0; index < propOffsets.Length; index++)
        {
            CreateGhostPrimitive(
                ghostDrone,
                PrimitiveType.Sphere,
                $"Prop {index + 1}",
                propOffsets[index],
                Vector3.one * 0.12f,
                Vector3.zero);
        }

        if (hideGhostWhenNotPlaying)
            ghostDrone.gameObject.SetActive(false);
    }

    private void CreateGhostPrimitive(
        Transform parent,
        PrimitiveType primitiveType,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEulerAngles)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localEulerAngles = localEulerAngles;
        primitive.transform.localScale = localScale;

        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");

        if (shader == null)
            return;

        Material material = new Material(shader);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", GhostColor);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", GhostColor);

        material.color = GhostColor;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        renderer.material = material;

        foreach (Material sharedMaterial in renderer.materials)
        {
            sharedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sharedMaterial.EnableKeyword("_ALPHABLEND_ON");
        }
    }
}
