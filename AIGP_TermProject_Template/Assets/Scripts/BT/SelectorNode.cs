using System.Collections.Generic;

public class SelectorNode : BTNode
{
    private readonly List<BTNode> children = new List<BTNode>();
    private int currentIndex;

    public SelectorNode(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override BTNodeStatus Tick()
    {
        while (currentIndex < children.Count)
        {
            BTNodeStatus status = children[currentIndex].Tick();

            if (status == BTNodeStatus.Success)
            {
                currentIndex = 0;
                return BTNodeStatus.Success;
            }

            if (status == BTNodeStatus.Running)
            {
                return BTNodeStatus.Running;
            }

            currentIndex++;
        }

        currentIndex = 0;
        return BTNodeStatus.Failure;
    }

    public override void Reset()
    {
        currentIndex = 0;

        foreach (BTNode child in children)
        {
            child.Reset();
        }
    }
}
