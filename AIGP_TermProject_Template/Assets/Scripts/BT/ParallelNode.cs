using System.Collections.Generic;

public class ParallelNode : BTNode
{
    private readonly List<BTNode> children = new List<BTNode>();
    private readonly int successThreshold;
    private readonly int failureThreshold;

    public ParallelNode(int successThreshold, int failureThreshold, params BTNode[] nodes)
    {
        children.AddRange(nodes);
        this.successThreshold = successThreshold;
        this.failureThreshold = failureThreshold;
    }

    public override BTNodeStatus Tick()
    {
        if (children.Count == 0)
        {
            return BTNodeStatus.Failure;
        }

        int successes = 0;
        int failures = 0;

        foreach (BTNode child in children)
        {
            BTNodeStatus status = child.Tick();

            if (status == BTNodeStatus.Success)
            {
                successes++;
            }
            else if (status == BTNodeStatus.Failure)
            {
                failures++;
            }
        }

        if (successes >= successThreshold)
        {
            return BTNodeStatus.Success;
        }

        if (failures >= failureThreshold)
        {
            return BTNodeStatus.Failure;
        }

        return BTNodeStatus.Running;
    }

    public override void Reset()
    {
        foreach (BTNode child in children)
        {
            child.Reset();
        }
    }
}
