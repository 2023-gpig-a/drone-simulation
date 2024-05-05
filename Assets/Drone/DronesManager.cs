using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Drone
{
    public class DronesManager : MonoBehaviour
    {
        public static DronesManager Instance { get; private set; }

        // Inspector properties
        [SerializeField] private SimpleDroneNavigation dronePrefab;
        [SerializeField] private int droneCount = 5;
        [SerializeField] private float screenshotTime = 1f;
        
        // Private attributes
        private SimpleDroneNavigation[] _drones;
        private Camera[] _droneCameras;
        
        private Queue<int> _screenshotQueue;
        private int _processingScreenshot = -1;
        private bool _takenScreenshot;
        private float _timer = 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
            _screenshotQueue = new Queue<int>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SpawnDrones();
        }

        private void SpawnDrones()
        {
            var spawnPoints = GameObject.FindGameObjectsWithTag("DroneSpawn"); // Must be enough manually placed
            
            _drones = new SimpleDroneNavigation[droneCount];
            _droneCameras = new Camera[droneCount];

            // Iterate spawning in drones
            for (var i = 0; i < Mathf.Clamp(droneCount, 0, spawnPoints.Length); i++)
            {
                var spawnPoint = spawnPoints[i].transform;
                var drone = Instantiate(dronePrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
                drone.name = $"Drone {i}";
                _drones[i] = drone.GetComponent<SimpleDroneNavigation>();
                _drones[i].id = i;
                _droneCameras[i] = drone.GetComponentInChildren<Camera>();
            }
        }
        
        public void EnqueueScreenshot(int id)
        {
            _screenshotQueue.Enqueue(id);
        }

        private void Update()
        {
            // Grab a screenshot request to process if free
            if (_processingScreenshot < 0 && _screenshotQueue.Count > 0)
            {
                _processingScreenshot = _screenshotQueue.Dequeue();
            }
            
            if (_processingScreenshot >= 0) ProcessScreenshot(_processingScreenshot);
        }

        private void ProcessScreenshot(int id)
        {
            // Enable camera to start
            if (!_droneCameras[id].enabled)
            {
                _droneCameras[id].enabled = true;
                _timer = Time.time;
            }

            else switch (_takenScreenshot)
            {
                // Wait, then take the screenshot
                case false when Time.time > _timer + screenshotTime * .25f:
                {
                    var pos = _drones[id].transform.position;
                    var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var dir = $"{Application.streamingAssetsPath}/Photos";
                    var path = $"{dir}/{pos.x},{pos.z}_{time}.png";
        
                    System.IO.Directory.CreateDirectory(dir);
                    ScreenCapture.CaptureScreenshot(path);
                    _takenScreenshot = true;
                    print($"Saved photo to: {path}");
                    break;
                }
                
                // End, after giving time for screenshotting
                case true when Time.time > _timer + screenshotTime:
                    _droneCameras[id].enabled = false;
                    _takenScreenshot = false;
                    _processingScreenshot = -1;
                    _drones[id].OnScreenshotDone();
                    break;
            }
        }
    }
}