using UnityEngine;

/// <summary>
/// Editor-populated references so runtime never creates joints.
/// </summary>
[DisallowMultipleComponent]
public class SoftBodyNodeLink : MonoBehaviour
{
    public Rigidbody dynamicBody;
    public ConfigurableJoint joint;
    public Transform cageBone;
    public Rigidbody proxy;
    public Vector3 baseConnectedAnchor;
    public Vector3 connectedAnchorOffset;


    [HideInInspector] public Vector3 accumulatedPlastic;
}