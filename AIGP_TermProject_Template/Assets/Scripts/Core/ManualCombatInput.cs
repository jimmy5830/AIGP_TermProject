using UnityEngine;

[RequireComponent(typeof(CombatActionController))]
// Temporary keyboard controller for Agent_A. Replace this with BT/RL controllers later.
public class ManualCombatInput : MonoBehaviour
{
    [SerializeField] private CombatActionController actionController;

    private void Awake()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void Update()
    {
        Vector3 moveDirection = ReadMoveDirection();
        actionController.Move(moveDirection);

        if (Input.GetKeyDown(KeyCode.J))
        {
            actionController.Attack();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            actionController.Block();
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            Vector3 dodgeDirection = moveDirection.sqrMagnitude > 0.0001f ? moveDirection : transform.forward;
            actionController.Dodge(dodgeDirection);
        }
    }

    private Vector3 ReadMoveDirection()
    {
        Vector3 direction = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
        {
            direction += Vector3.forward;
        }

        if (Input.GetKey(KeyCode.S))
        {
            direction += Vector3.back;
        }

        if (Input.GetKey(KeyCode.A))
        {
            direction += Vector3.left;
        }

        if (Input.GetKey(KeyCode.D))
        {
            direction += Vector3.right;
        }

        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private void FillDefaultReferences()
    {
        if (actionController == null)
        {
            actionController = GetComponent<CombatActionController>();
        }
    }
}
