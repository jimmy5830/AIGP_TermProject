using UnityEngine;

public class CameraFollowSwitcher : MonoBehaviour
{
    [SerializeField] private Transform agentA;
    [SerializeField] private Transform agentB;

    [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -6f);
    [SerializeField] private float smoothSpeed = 8f;

    private Transform target;

    private void Start()
    {
        target = agentA;
    }

    private void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            target = agentA;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            target = agentB;
        }

        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            smoothSpeed * Time.deltaTime
        );

        transform.LookAt(target.position + Vector3.up * 1.2f);
    }
}