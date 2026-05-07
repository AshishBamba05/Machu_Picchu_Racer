// using UnityEngine;

// public static class RaceTrackBootstrap
// {
//     [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
//     private static void EnsureRaceTrackExists()
//     {
//         if (!Application.isPlaying || Object.FindObjectOfType<RaceTrackManager>() != null)
//         {
//             return;
//         }

//         var trackObject = new GameObject("Race Track");
//         trackObject.AddComponent<RaceTrackManager>();
//     }
// }
