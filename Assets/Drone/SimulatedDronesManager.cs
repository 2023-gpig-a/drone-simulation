using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using CompactExifLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Drone
{
    public class SimulatedDronesManager : MonoBehaviour
    {
        public static SimulatedDronesManager Instance { get; private set; }

        // Inspector properties
        [Header("Drones")]
        [SerializeField] private SimulatedDrone dronePrefab;
        [SerializeField] private int droneCount = 1;
        [SerializeField] private float screenshotTime = 1f;
        
        [Header("API")] 
        [SerializeField] private string hostURL = "";
        [SerializeField] private bool useLocalhost;
        [SerializeField] private int droneManagerPort = 8082;
        [SerializeField] private int dmasPort = 8081;
        [SerializeField] private float heartbeat = 1f;

        [Header("Coordinate Space")] 
        // Both of these will need adjusting for the demo itself
        // Especially geoScale, as this helps scale down real-world positions to fit the confines of the sim
        [SerializeField] private Vector2 geoOrigin = new Vector2(54.285453f, -0.544649f);
        [SerializeField] private float geoScale = 5f;
        
        // Private attributes
        private SimulatedDrone[] _drones;
        private Camera[] _droneCameras;
        
        private Queue<int> _screenshotQueue;
        
        private float _screenshotTimer;
        private float _heartbeatTimer;
        
        private int _processingScreenshot = -1;
        
        private bool _takenScreenshot;
        private bool _requestingPathing;

        private string _screenshotPath;
        private DateTime _screenshotTimeTaken;
        
        // MAIN EVENT FUNCTIONS

        private void Awake()
        {
            // Enforce singleton
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // Instantiate properties
            _screenshotQueue = new Queue<int>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SpawnDrones();
            _requestingPathing = true;
        }
        
        private void Update()
        {
            // Drone heartbeat
            if (Time.time - _heartbeatTimer > heartbeat)
            {
                DroneHeartbeat();
                _heartbeatTimer = Time.time;

                // GET request for new path
                // In heartbeat to reduce coroutine calls
                if (_requestingPathing) StartCoroutine(GetPath());
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
            
            _drones = new SimulatedDrone[droneCount];
            _droneCameras = new Camera[droneCount];

            // Iterate spawning in drones
            for (var i = 0; i < Mathf.Clamp(droneCount, 0, spawnPoints.Length); i++)
            {
                var spawnPoint = spawnPoints[i].transform;
                var drone = Instantiate(dronePrefab.gameObject, spawnPoint.position, spawnPoint.rotation);
                drone.name = $"Drone {i}";
                _drones[i] = drone.GetComponent<SimulatedDrone>();
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
            var position = SimToGeoCoords(_drones[id].transform.position);
            
            // Enable camera to start
            if (!_droneCameras[id].enabled)
            {
                _droneCameras[id].enabled = true;
                _screenshotTimer = Time.time;
            }

            else switch (_takenScreenshot)
            {
                // Wait, then take the screenshot
                case false when Time.time > _screenshotTimer + screenshotTime * .5f:
                {
                    var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var dir = $"{Application.streamingAssetsPath}/Photos";
                    var path = $"{dir}/{position.x},{position.y}_{time}.png";
                    
                    System.IO.Directory.CreateDirectory(dir);
                    ScreenCapture.CaptureScreenshot(path);
                    _takenScreenshot = true;
                    _screenshotPath = path;
                    _screenshotTimeTaken = DateTimeOffset.UtcNow.DateTime;
                    print($"Saved photo to: {path}");
                    break;
                }
                
                // End, after giving time for screenshotting
                case true when Time.time > _screenshotTimer + screenshotTime:
                    _droneCameras[id].enabled = false;
                    _takenScreenshot = false;
                    _processingScreenshot = -1;
                    _drones[id].OnScreenshotDone();
                    
                    StartCoroutine(PostScreenshot(position));
                    break;
            }
        }

        public void RequestNewPathing()
        {
            _requestingPathing = true;
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
                var convertedPosition = SimToGeoCoords(drone.transform.position);
                var x = convertedPosition.x;
                var y = convertedPosition.y;
                
                // Convert into JSON and POST it
                var data = $"{{\"status\": \"{status}\",\"battery\": 1,\"lastUpdate\": \"{time}\",\"lastSeen\": [{x},{y}]}}";
                StartCoroutine(SendPost(GetUri($"drone_status/{id}", droneManagerPort), data));
            }
        }

        private IEnumerator GetPath()
        {
            // Send GET request
            var request = UnityWebRequest.Get(GetUri("next_area", droneManagerPort));
            yield return request.SendWebRequest(); 
            
            // Validate response
            if (request.isNetworkError || request.isHttpError)
            {
                print($"GET PATH ERROR: {request.error}");
            }
            else
            {
                // Process new goals into list of vectors
                // Does not use any algorithm for distributing goals, due to the simplicity of the demo,
                // this would be a feature to add in the future, if further multi-drone simulation was required
                var processedData = JsonConvert.DeserializeObject<List<List<float>>>(request.downloadHandler.text);
                print($"Retrieved {processedData.Count} goals from DroneManager.");
                for (var i = 0; i < processedData.Count; i++)
                {
                    var goal = GeoToSimCoords(new Vector2(processedData[i][0], processedData[i][1]));
                    _drones[i % _drones.Length].EnqueueGoal(goal);
                }
                _requestingPathing = false;
            }
        }

        private IEnumerator PostScreenshot(Vector2 geoPosition)
        {
            EmbedImageMetadata(_screenshotPath, geoPosition, _screenshotTimeTaken);
            
            // Process image into form-data
            var imageBytes = System.IO.File.ReadAllBytes(_screenshotPath);
            var form = new WWWForm();
            form.AddBinaryData("file", imageBytes);

            // Send POST request
            var request = UnityWebRequest.Post(GetUri("upload_image", dmasPort), form);
            yield return request.SendWebRequest();
            
            // Validate response
            if (request.isNetworkError || request.isHttpError)
            {
                print($"IMAGE POST ERROR: {request.error}");
            }
            else
            {
                System.IO.File.Delete(_screenshotPath);
            }
        }
        
        
        // API HELPER FUNCTIONS
        
        private string GetUri(string path, int port)
        {
            return useLocalhost ? $"localhost:{port}/{path}" : $"{hostURL}:{port}/{path}";
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
                print($"{uri} ERROR: {request.error}");
            }
            else
            {
                print($"Successful POST to {uri}");
            }
        }

        private Vector2 SimToGeoCoords(Vector2 unityPosition)
        {
            // 1 degree is approx 111,111m
            // The cos is to correct for the latitude
            var geoOffset = new Vector2(
                unityPosition.y / 111111f, 
                unityPosition.x / (111111f * Mathf.Cos(Mathf.Deg2Rad * geoOrigin.y)));

            return geoOrigin + geoOffset * geoScale;
        }
        
        private Vector2 SimToGeoCoords(Vector3 unityPosition)
        {
            return SimToGeoCoords(new Vector2(unityPosition.x, unityPosition.z));
        }
        
        private Vector2 GeoToSimCoords(Vector2 geoPosition)
        {
            // Simply an inverse of the SimToGeoCoords function
            var geoOffset = (geoPosition - geoOrigin);

            return new Vector2(
                geoOffset.y * 111111f,
                geoOffset.x * (111111f * Mathf.Cos(Mathf.Deg2Rad * geoOrigin.y))) * geoScale;
        }

        private static void EmbedImageMetadata(string filepath, Vector2 geoPosition, DateTime timeTaken)
        {
            var exifData = new ExifData(filepath);
            
            // Convert longitude and latitude
            var longitude = GeoCoordinate.FromDecimal((decimal)geoPosition.y, false);
            var latitude = GeoCoordinate.FromDecimal((decimal)geoPosition.x, true);
            
            // Embed data and save
            exifData.SetGpsLongitude(longitude);
            exifData.SetGpsLatitude(latitude);
            exifData.SetGpsDateTimeStamp(timeTaken);
            
            exifData.Save();
            print("Embedded metadata");
        }
    }
}