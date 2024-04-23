using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

[RequireComponent(typeof(NavMeshAgent))]
public class SimpleDroneNavigation : MonoBehaviour
{
    // Inspector properties
    [Header("Properties")] 
    [SerializeField] private float flightHeight = 5f;
    [SerializeField] private float arrivalThreshold = 0.1f;
    
    [Header("Debug")] 
    [SerializeField] private bool debug;
    [SerializeField] private Vector2 debugDestination;
    
    // Private attributes
    private NavMeshAgent _agent;
    
    private Queue<Vector2> _destinations;
    private Vector3 _currentDestination;
    private Vector2 _previousDebugDestination;
    
    private void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        
        // Initialise the debug destination
        var position = transform.position;
        debugDestination = new Vector2(position.x, position.z);
        _previousDebugDestination = debugDestination;
        
        // Initialise the main destinations
        _destinations = new Queue<Vector2>();
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
        
        // If arrived at destination, move onto the next one if possible
        if (_destinations.Count > 0 && _agent.remainingDistance <= arrivalThreshold)
        {
            var newDestination = _destinations.Dequeue();
            _agent.SetDestination(new Vector3(newDestination.x, flightHeight, newDestination.y));
        }
    }
}
