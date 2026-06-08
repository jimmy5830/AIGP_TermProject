using System;

public class ConditionNode : BTNode
{
    private readonly Func<bool> condition;

    public ConditionNode(Func<bool> condition)
    {
        this.condition = condition;
    }

    public override BTNodeStatus Tick()
    {
        return condition != null && condition()
            ? BTNodeStatus.Success
            : BTNodeStatus.Failure;
    }
}
