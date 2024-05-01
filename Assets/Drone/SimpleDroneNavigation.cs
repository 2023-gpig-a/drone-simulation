using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Drone
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class SimpleDroneNavigation : MonoBehaviour
    {
        // Inspector properties
        [Header("Navigation")] 
        [SerializeField] private float flightHeight = 5f;
        [SerializeField] private float arrivalThreshold = 0.1f;
        [SerializeField] private float stopTime = 1f;
        [SerializeField] private float reachableAllowance = 1f;
    
        [Header("Debug")] 
        [SerializeField] private bool debug;
        [SerializeField] private Vector2 debugDestination;
    
        // Private attributes
        private NavMeshAgent _agent;
        private Camera _cam;
    
        private Queue<Vector2> _destinations;
        private Vector2 _currentDestination;
        private Vector2 _previousDebugDestination;

        private float _arrivalTime;
        private bool _takenPhoto = true;
        private int _numCameraOffCalls;
    
        private void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _cam = GetComponentInChildren<Camera>();
        
            // Disable the camera from the start (save on processing when not in use)
            _cam.enabled = false;
        
            // Initialise the debug destination
            var position = transform.position;
            debugDestination = new Vector2(position.x, position.z);
            _previousDebugDestination = debugDestination;
        
            // Initialise the main destinations
            _destinations = new Queue<Vector2>();
            _currentDestination = debugDestination;
            _agent.destination = position;
        }

        private void Update()
        {
            // Add the debug destination to the queue if applicable
            if (debug && debugDestination != _previousDebugDestination)
            {
                _previousDebugDestination = debugDestination;
                _destinations.Enqueue(debugDestination);
            }
            
            // If the destination is unreachable, skip
            var vec2AgentDestination = new Vector2(_agent.destination.x, _agent.destination.z);
            var unreachable = (_currentDestination - vec2AgentDestination).magnitude > reachableAllowance;
            
            if (unreachable) print($"Cannot reach destination {_currentDestination}, skipping.");
        
            // If arrived at destination, take a photo
            var arrived = _agent.remainingDistance <= arrivalThreshold;
            if (arrived && !_takenPhoto && !unreachable)
            {
                print($"Path: {_agent.pathStatus}");
                
                _cam.enabled = true;
                _takenPhoto = true;
                _arrivalTime = Time.time;
            
                // Method requires invoking to give the screen time to re-appear
                Invoke("TakePhoto", stopTime);
            }
        
            // When ready, start heading to the next destination
            if (_destinations.Count > 0 && ((arrived && Time.time - _arrivalTime >= stopTime) || unreachable))
            {
                _currentDestination = _destinations.Dequeue();
                _agent.SetDestination(new Vector3(_currentDestination.x, flightHeight, _currentDestination.y));
                _takenPhoto = false;
            }
        }

        private void TakePhoto()
        {
            // Saves current screen to a screenshot
            var pos = transform.position;
            var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dir = $"{Application.streamingAssetsPath}/Photos";
            var path = $"{dir}/{pos.x},{pos.z}_{time}.png";
        
            System.IO.Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(path);
            print($"Saved photo to: {path}");
        
            // Can take time, so the camera being disabled has to be delayed
            _numCameraOffCalls++;
            Invoke("CameraOff", 1f);
        }

        private void CameraOff()
        {
            _numCameraOffCalls--;
            if (_numCameraOffCalls <= 0) _cam.enabled = false;
        }
    }
}
