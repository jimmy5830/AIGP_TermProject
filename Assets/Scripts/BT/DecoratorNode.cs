using System;

public class DecoratorNode : BTNode
{
    private readonly BTNode child;
    private readonly Func<BTNodeStatus, BTNodeStatus> decorate;

    public DecoratorNode(BTNode child, Func<BTNodeStatus, BTNodeStatus> decorate)
    {
        this.child = child;
        this.decorate = decorate;
    }

    public override BTNodeStatus Tick()
    {
        if (child == null)
        {
            return BTNodeStatus.Failure;
        }

        BTNodeStatus childStatus = child.Tick();
        return decorate != null ? decorate(childStatus) : childStatus;
    }

    public override void Reset()
    {
        child?.Reset();
    }
}
