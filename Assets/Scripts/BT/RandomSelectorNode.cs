using System.Collections.Generic;
using UnityEngine;

public class RandomSelectorNode : BTNode
{
    private readonly List<BTNode> children = new List<BTNode>();

    public RandomSelectorNode(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override BTNodeStatus Tick()
    {
        if (children.Count == 0)
        {
            return BTNodeStatus.Failure;
        }

        List<int> order = BuildRandomOrder();
        foreach (int index in order)
        {
            BTNodeStatus status = children[index].Tick();
            if (status != BTNodeStatus.Failure)
            {
                return status;
            }
        }

        return BTNodeStatus.Failure;
    }

    public override void Reset()
    {
        foreach (BTNode child in children)
        {
            child.Reset();
        }
    }

    private List<int> BuildRandomOrder()
    {
        List<int> order = new List<int>();
        for (int i = 0; i < children.Count; i++)
        {
            order.Add(i);
        }

        for (int i = 0; i < order.Count; i++)
        {
            int swapIndex = Random.Range(i, order.Count);
            int temp = order[i];
            order[i] = order[swapIndex];
            order[swapIndex] = temp;
        }

        return order;
    }
}
