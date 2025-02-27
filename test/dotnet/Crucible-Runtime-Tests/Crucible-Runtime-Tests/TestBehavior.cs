using Crucible.Core;

namespace CrucibleRuntimeTests;

public class TestBehavior: NodeBehavior
{
    public double lastDeltaTime = 0;
    public override void Update(double deltaTime)
    {
        lastDeltaTime = deltaTime;
    }

    public double GetLastDeltaTime()
    {
        return lastDeltaTime;
    }

    public void SetLastDeltaTime(double deltaTime)
    {
        lastDeltaTime = deltaTime;
    }

    public DummyObject CreateDummyObject()
    {
        return new DummyObject();
    }
}