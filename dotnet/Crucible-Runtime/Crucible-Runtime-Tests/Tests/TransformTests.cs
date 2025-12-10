using Crucible.Core;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class TransformTests
{
    public static TestResult PositionTest()
    {
        Transform transform = new Transform();
        if (transform.Position.X != 0 || transform.Position.Y != 0 || transform.Position.Z != 0)
        {
            return TestResult.Fail($"Transform not created with default position, instead: {transform.Position}");
        }

        transform.Position = new Vector3(10, 20, 30);
        if (transform.Position.X != 10 || transform.Position.Y != 20 || transform.Position.Z != 30)
        {
            return TestResult.Fail($"Transform not assigned given position position, instead: {transform.Position}");
        }
        return TestResult.Pass();
    }

    public static TestResult TranslateTest()
    {
        Transform transform = new Transform();
        transform.Translate(new Vector3(-10.5f, 2, 30000));
        if (transform.Position.X != -10.5f || transform.Position.Y != 2 || transform.Position.Z != 30000)
        {
            return TestResult.Fail($"Transform does not have expected position, instead: {transform.Position}");
        }
        transform.Translate(new Vector3(1, 1, 1));
        if (!Vector3.Approximately(transform.Position, new Vector3(-9.5f, 3, 30001)))
        {
            return TestResult.Fail($"Transform does not have expected position, instead: {transform.Position}");
        }
        return TestResult.Pass();
    }

    public static TestResult RotationTest()
    {
        Transform transform = new Transform();
        if (transform.Rotation.X != 0 || transform.Rotation.Y != 0 || transform.Rotation.Z != 0 || transform.Rotation.W != 1)
        {
            return TestResult.Fail($"Transform does not have default rotation, instead: {transform.Rotation}");
        }
        Quaternion quaternion = new Quaternion(45,new Vector3(0, 1, 0));
        transform.Rotation = quaternion;
        if (!Quaternion.Approximately(transform.Rotation, quaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        Quaternion quaternion2 = new Quaternion(16,new Vector3(1, 0, 0));
        transform.Rotation = quaternion2;
        if (!Quaternion.Approximately(transform.Rotation, quaternion2))
        {
            return TestResult.Fail($"Transform does not have assigned rotation, instead: {transform.Rotation}");
        }
        return TestResult.Pass();
    }

    public static TestResult RotateAxisAngleTest()
    {
        Transform transform = new Transform();
        Quaternion baseQuaternion = new Quaternion(0,0,0,1);
        if (transform.Rotation.X != 0 || transform.Rotation.Y != 0 || transform.Rotation.Z != 0 || transform.Rotation.W != 1)
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        transform.Rotate(0.7853982f,new Vector3(0, 1, 0));
        var initial = transform.Rotation;
        baseQuaternion *= new Quaternion(0.7853982f,new Vector3(0, 1, 0));
        if (!Quaternion.Approximately(transform.Rotation,baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        transform.Rotate(0.7853982f,new Vector3(0, 1, 0));
        baseQuaternion *=  new Quaternion(0.7853982f, new Vector3(0, 1, 0));
        if (!Quaternion.Approximately(transform.Rotation, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        if (Quaternion.Approximately(initial, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {baseQuaternion}");
        }
        
        return TestResult.Pass();
    }

    public static TestResult RotateQuaternionTest()
    {
        Transform transform = new Transform();
        Quaternion baseQuaternion = new Quaternion(0,0,0,1);
        if (transform.Rotation.X != 0 || transform.Rotation.Y != 0 || transform.Rotation.Z != 0 || transform.Rotation.W != 1)
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        Quaternion quaternion = new Quaternion(0.7853982f, new Vector3(0, 1, 0));
        transform.Rotate(quaternion);
        var initial = transform.Rotation;
        baseQuaternion *= quaternion;
        if (!Quaternion.Approximately(transform.Rotation, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        transform.Rotate(quaternion);
        baseQuaternion *= quaternion;
        if (!Quaternion.Approximately(transform.Rotation, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        if (Quaternion.Approximately(initial, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {baseQuaternion}");
        }
        return TestResult.Pass();
    }

    public static TestResult RotateEulerAnglesTest()
    {
        Transform transform = new Transform();
        Quaternion baseQuaternion = new Quaternion(0,0,0,1);
        if (transform.Rotation.X != 0 || transform.Rotation.Y != 0 || transform.Rotation.Z != 0 || transform.Rotation.W != 1)
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        transform.Rotate(0.7853982f,0,0);
        var initial = transform.Rotation;
        Quaternion quaternion = new Quaternion(0.7853982f,0,0);
        baseQuaternion *= quaternion;
        if (!Quaternion.Approximately(transform.Rotation, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }
        transform.Rotate(0.7853982f,0,0);
        baseQuaternion *= quaternion;
        if (!Quaternion.Approximately(transform.Rotation, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {transform.Rotation}");
        }

        if (Quaternion.Approximately(initial, baseQuaternion))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {baseQuaternion}");
        }
        return TestResult.Pass();
    }

    public static TestResult ScaleTest()
    {
        Transform transform = new Transform();
        if (transform.Scale.X != 1 || transform.Scale.Y != 1 || transform.Scale.Z != 1)
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.Scale = new Vector3(10,.1f,-1);
        if (!Vector3.Approximately(transform.Scale, new Vector3(10, .1f, -1)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.Scale = new Vector3(1,.15f,6);
        if (!Vector3.Approximately(transform.Scale, new Vector3(1, .15f, 6)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        return TestResult.Pass();
    }

    public static TestResult ScaleByVectorTest()
    {
        Transform transform = new Transform();
        if (transform.Scale.X != 1 || transform.Scale.Y != 1 || transform.Scale.Z != 1)
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.ScaleBy(new Vector3(2,5,-1));
        if (!Vector3.Approximately(transform.Scale, new Vector3(2, 5, -1)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.ScaleBy(new Vector3(3,2,5));
        if (!Vector3.Approximately(transform.Scale, new Vector3(6, 10, -5)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        return TestResult.Pass();
    }

    public static TestResult ScaleByFloatTest()
    {
        Transform transform = new Transform();
        if (transform.Scale.X != 1 || transform.Scale.Y != 1 || transform.Scale.Z != 1)
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.ScaleBy(.2f);
        if (!Vector3.Approximately(transform.Scale, new Vector3(.2f, .2f, .2f)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        transform.ScaleBy(-1);
        if (!Vector3.Approximately(transform.Scale, new Vector3(-.2f, -.2f, -.2f)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {transform.Scale}");
        }
        return TestResult.Pass();
    }

    public static TestResult ToGlobalTest()
    {
        NodeKeeper nodeKeeper = new NodeKeeper();
        var node = nodeKeeper.KeptNode.Acquire();
        Transform transform = new Transform();
        transform.Position = new Vector3(5, 10, 15);
        transform.Rotate(0.7853982f,new Vector3(0,0,1.0f));
        transform.Scale = new Vector3(1, .5f, 1);
        node.AddDataComponent(transform);
        var global = transform.ToGlobal(node);
        transform = transform + transform;
        if (!Transform.Approximately(global, transform))
        {
            return TestResult.Fail($"Transform does not have expected value, instead: {transform}");
        }
        return TestResult.Pass();
    }

    public static TestResult InverseTest()
    {
        Transform transform = new Transform(new Vector3(10,20,30),new Quaternion(0.7853982f, new Vector3(0,1,0)),new Vector3(1,.5f,1));
        var inverse = transform.Inverse();
        var identity = transform + inverse;
        Transform baseTransform = new Transform();
        if (!Transform.Approximately(identity, baseTransform))
        {
            return TestResult.Fail($"Transform modified by it's inverse is not identity, instead: {baseTransform}");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild = child.AddChild();
        Transform transform = new Transform();
        transform.Position = new Vector3(1, 2, 3);
        transform.Rotate(45.0f,new Vector3(1,0,0));
        node.AddDataComponent(transform);
        grandchild.AddDataComponent(transform);
        var nodeTransform = grandchild.CumulativeTransform();
        var cumulative = Transform.Cumulative(grandchild);
        if (!Transform.Approximately(cumulative, nodeTransform))
        {
            return TestResult.Fail($"Node's transform is not correct cumulative transform, instead: {nodeTransform}");
        }
        return TestResult.Pass();
    }

    public static TestResult Concat()
    {
        Transform transform = new Transform(new Vector3(10,20,30), new Quaternion(0.7853982f, new Vector3(0,1,0)),new Vector3(1,.5f,1));
        Transform transform2 = new Transform(new Vector3(10,10,-2), new Quaternion(0.7853982f, new Vector3(1,0,0)), new Vector3(2,.5f,-1));
        var result = transform + transform2;
        if (!Vector3.Approximately(result.Position, new Vector3(30, -5.6765976f, -15.057169f)))
        {
            return TestResult.Fail($"Transform does not have expected position, instead: {result.Position}");
        }

        if (!Quaternion.Approximately(result.Rotation,
                new Quaternion(0.48575816f, 0.48575816f, -0.61848646f, 0.38151357f)))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {result.Rotation}");
        }

        if (!Vector3.Approximately(result.Scale, new Vector3(2, 0.25f, -1)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {result.Scale}");
        }
        return TestResult.Pass();
    }

    public static TestResult UnConcat()
    {
        Transform transform = new Transform(new Vector3(10,20,30), new Quaternion(0.7853982f, new Vector3(0,1,0)),new Vector3(1,.5f,1));
        Transform transform2 = new Transform(new Vector3(10,10,-2), new Quaternion(0.7853982f, new Vector3(1,0,0)), new Vector3(2,.5f,-1));
        var result = transform - transform2;
        if (!Vector3.Approximately(result.Position, new Vector3(20.162859f, 24.312763f, 27.332336f)))
        {
            return TestResult.Fail($"Transform does not have expected position, instead: {result.Position}");
        }

        if (!Quaternion.Approximately(result.Rotation,
                new Quaternion(-0.4857581f, 0.48575813f, -0.6184864f,0.38151354f)))
        {
            return TestResult.Fail($"Transform does not have expected rotation, instead: {result.Rotation}");
        }

        if (!Vector3.Approximately(result.Scale, new Vector3(0.5f, 1, -1)))
        {
            return TestResult.Fail($"Transform does not have expected scale, instead: {result.Scale}");
        }
        return TestResult.Pass();
    }
    
    
}