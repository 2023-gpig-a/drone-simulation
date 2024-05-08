using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Drone
{
    public class SimulationDronesManager : MonoBehaviour
    {
        public static SimulationDronesManager Instance { get; private set; }

        // Inspector properties
        [Header("Drones")]
        [SerializeField] private SimpleDroneNavigation dronePrefab;
        [SerializeField] private int droneCount = 1;
        [SerializeField] private float screenshotTime = 1f;

        [Header("API")] 
        [SerializeField] private string URL = "";
        [SerializeField] private int port = 8080;
        [SerializeField] private bool useLocalhost;
        [SerializeField] private float heartbeat = 1f;

        [Header("Coordinate Space")] 
        [SerializeField] private Vector2 geoOrigin = new Vector2(54.285453f, -0.544649f);
        [SerializeField] private float geoScale = 5f;
        
        // Private attributes
        private SimpleDroneNavigation[] _drones;
        private Camera[] _droneCameras;
        
        private Queue<int> _screenshotQueue;
        private int _processingScreenshot = -1;
        private bool _takenScreenshot;
        
        private float _screenshotTimer;
        private float _heartbeatTimer;
        
        
        // MAIN EVENT FUNCTIONS

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
        
        private void Update()
        {
            // Drone heartbeat
            if (Time.time - _heartbeatTimer > heartbeat)
            {
                DroneHeartbeat();
                _heartbeatTimer = Time.time;
            }
            
            // Grab a screenshot request to process if free
            if (_processingScreenshot < 0 && _screenshotQueue.Count > 0)
            {
                _processingScreenshot = _screenshotQueue.Dequeue();
            }
            
            if (_processingScreenshot >= 0) ProcessScreenshot(_processingScreenshot);
        }
        
        
        // CUSTOM DRONE EVENTS

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

        private void ProcessScreenshot(int id)
        {
            // Enable camera to start
            if (!_droneCameras[id].enabled)
            {
                _droneCameras[id].enabled = true;
                _screenshotTimer = Time.time;
            }

            else switch (_takenScreenshot)
            {
                // Wait, then take the screenshot
                case false when Time.time > _screenshotTimer + screenshotTime * .25f:
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
                case true when Time.time > _screenshotTimer + screenshotTime:
                    _droneCameras[id].enabled = false;
                    _takenScreenshot = false;
                    _processingScreenshot = -1;
                    _drones[id].OnScreenshotDone();
                    break;
            }
        }
        
        
        // API EVENT FUNCTIONS

        private void DroneHeartbeat()
        {
            // Every heartbeat, send relevant drone status data to the DroneManager
            print("Drone Heartbeat.");
            for (var id = 0; id < _drones.Length; id++)
            {
                var drone = _drones[id];
                
                // Drone statuses: idle / flying / unknown
                var status = drone.navigating ? "flying" : "idle";
                
                // Time needs to be in ISO 8601 for the frontend
                var time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                
                // Coordinates need to be in lon/lat for the rest of the system
                var convertedPos = SimToGeoCoords(drone.transform.position);
                var x = convertedPos.x;
                var y = convertedPos.y;
                
                // Convert into JSON and POST it
                var data = $"{{\"status\": \"{status}\",\"battery\": 1,\"lastUpdate\": \"{time}\",\"lastSeen\": [{x},{y}]}}";
                StartCoroutine(SendPost(GetUri($"drone_status/{id}"), data));
            }
        }
        
        
        // API HELPER FUNCTIONS
        
        private string GetUri(string path)
        {
            return useLocalhost ? $"localhost:{port}/{path}" : $"{URL}:{port}/{path}";
        }
        
        private static IEnumerator SendPost(string uri, string data)
        {
            print($"POST: {data}");
            
            // Creates a unity POST request which supports application/json
            var request = new UnityWebRequest(uri, "POST");
            
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            // Log errors/success
            if (request.isNetworkError || request.isHttpError)
            {
                print(request.error);
            }
            else
            {
                print($"Successful POST to {uri}");
            }
        }

        private Vector2 SimToGeoCoords(Vector2 unityPos)
        {
            // 1 degree is approx 111,111m
            // The cos is to correct for the latitude
            var geoOffset = new Vector2(
                unityPos.y / 111111f, 
                unityPos.x / (111111f * Mathf.Cos(Mathf.Deg2Rad * geoOrigin.x)));

            return geoOrigin + geoOffset;
        }
        
        private Vector2 GeoToSimCoords(Vector2 lonLat)
        {
            // Simply an inverse of the SimToGeoCoords function
            var geoOffset = lonLat - geoOrigin;

            return new Vector2(
                geoOffset.y * 111111f,
                geoOffset.x * (111111f * Mathf.Cos(Mathf.Deg2Rad * geoOrigin.x)));
        }
        
        private Vector2 SimToGeoCoords(Vector3 unityPos)
        {
            return SimToGeoCoords(new Vector2(unityPos.x, unityPos.z));
        }
    }
}