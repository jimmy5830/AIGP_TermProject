public enum BTNodeStatus
{
    Success,
    Failure,
    Running
}

// Minimal behavior tree node base used by baseline and student strategies.
public abstract class BTNode
{
    public abstract BTNodeStatus Tick();

    public virtual void Reset()
    {
    }
}
