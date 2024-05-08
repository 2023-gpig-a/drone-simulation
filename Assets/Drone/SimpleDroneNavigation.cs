using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Drone
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class SimpleDroneNavigation : MonoBehaviour
    {
        // Public attributes
        public int id;
        public bool navigating;
        
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
        
        private bool _takingPhoto = true;
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

            // Move onto the next destination if idle
            if (!navigating && _destinations.Count > 0)
            {
                NextDestination();
                navigating = true;
            }
            
            // If the destination is unreachable, skip
            var vec2AgentDestination = new Vector2(_agent.destination.x, _agent.destination.z);
            var unreachable = (_currentDestination - vec2AgentDestination).magnitude > reachableAllowance;
            
            if (unreachable) print($"Cannot reach destination {_currentDestination}, skipping.");
        
            // If arrived at destination, take a photo
            var arrived = _agent.remainingDistance <= arrivalThreshold;
            if (arrived && !_takingPhoto && !unreachable)
            {
                print($"Path: {_agent.pathStatus}");
                _takingPhoto = true;
                SimulationDronesManager.Instance.EnqueueScreenshot(id); // TODO: Fix bug where it can take multiple sometimes
            }
        }
        
        private void NextDestination()
        {
            // If no destinations queued, idle
            if (_destinations.Count <= 0)
            {
                navigating = false;
                return;
            }
            
            // Move on to the next target after taking a photo
            _currentDestination = _destinations.Dequeue();
            _agent.SetDestination(new Vector3(_currentDestination.x, flightHeight, _currentDestination.y));
            _takingPhoto = false;
        }

        public void OnScreenshotDone()
        {
            NextDestination();
        }
    }
}
