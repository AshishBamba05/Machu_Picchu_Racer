using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GhostChampion : MonoBehaviour
{
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
    public bool hideGhostWhenNotPlaying = false;

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
        Debug.Log("GhostChampion Start running");
        Debug.Log("Ghost save path: " + SavePath);

        if (File.Exists(SavePath))
        {
        Debug.Log("Ghost replay file FOUND.");
        }
        else
        {
            Debug.Log("Ghost replay file NOT found.");
        }

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
    }

    // Call this when the race finishes
    public void StopRecordingAndSaveIfBest(float finalRaceTime)
    {
        if (!isRecording)
            return;

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

    private void RecordUpdate()
    {
        float elapsed = Time.time - recordingStartTime;
        float sampleInterval = 1f / samplesPerSecond;

        if (elapsed < nextSampleTime)
            return;

        GhostFrame frame = new GhostFrame();
        frame.time = elapsed;
        frame.position = playerDrone.position;
        frame.rotation = playerDrone.rotation;

        currentRun.frames.Add(frame);

        nextSampleTime += sampleInterval;
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

        Debug.Log("Saved ghost champion best run to: " + SavePath);
    }

    private void LoadBestRun()
    {
        if (!File.Exists(SavePath))
            return;

        string json = File.ReadAllText(SavePath);
        bestRun = JsonUtility.FromJson<GhostRunData>(json);

        Debug.Log("Loaded ghost champion best run from: " + SavePath);
    }

    public bool HasBestRun()
    {
        return bestRun != null &&
               bestRun.frames != null &&
               bestRun.frames.Count > 1;
    }
}