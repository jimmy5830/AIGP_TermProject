using System;

public class ActionNode : BTNode
{
    private readonly Func<BTNodeStatus> action;

    public ActionNode(Action action)
        : this(() =>
        {
            action?.Invoke();
            return BTNodeStatus.Success;
        })
    {
    }

    public ActionNode(Func<BTNodeStatus> action)
    {
        this.action = action;
    }

    public override BTNodeStatus Tick()
    {
        return action != null ? action() : BTNodeStatus.Failure;
    }
}
