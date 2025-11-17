using Crucible.Core;
using Crucible.Core.ECS;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class NodeTests
{
    public static bool UUIDTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        NodeKeeper keeper2 = new NodeKeeper();
        NodeKeeper keeper3 = new NodeKeeper();
        var node1 = keeper.KeptNode.Acquire();
        var node2 = keeper2.KeptNode.Acquire();
        var node3 = keeper3.KeptNode.Acquire();
        if (node1.Uuid() != node1.Uuid() || node2.Uuid() != node2.Uuid() || node3.Uuid() != node3.Uuid())
        {
            return false;
        }
        if (node1.Uuid() == node2.Uuid() || node2.Uuid() == node3.Uuid() || node1.Uuid() == node3.Uuid())
        {
            return false;
        }
        return true;
    }

    public static bool NameTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        if (!string.IsNullOrEmpty(node.Name))
        {
            return false;
        }
        node.Name = "Test";
        if (node.Name != "Test")
        {
            return false;
        }
        node.Name = "◡̈ツ";
        if (node.Name != "◡̈ツ")
        {
            return false;
        }
        return true;
    }

    public static bool ParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        var greatGrandChild = grandchild2.AddChild();
        if (greatGrandChild.Parent() != grandchild2)
        {
            return false;
        }
        if (grandchild2.Parent() != child || grandchild1.Parent() != child)
        {
            return false;
        }

        if (child.Parent() != node)
        {
            return false;
        }

        if (!node.Parent().IsNull)
        {
            return false;
        }
        grandchild2.SetParent(node);
        if (greatGrandChild.Parent() != grandchild2)
        {
            return false;
        }

        if (grandchild2.Parent() != node)
        {
            return false;
        }

        if (node.ChildCount() != 2)
        {
            return false;
        }

        if (child.ChildCount() != 1)
        {
            return false;
        }
        return true;
    }

    public static bool TryGetParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        if (!child.TryGetParent(out Node parent))
        {
            return false;
        }

        if (parent != node)
        {
            return false;
        }
        if (node.TryGetParent(out Node grandParent))
        {
            return false;
        }
        return true;
    }

    public static bool SetParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        var greatGrandChild = grandchild2.AddChild();
        if (greatGrandChild.Parent() != grandchild2)
        {
            return false;
        }
        if (grandchild2.Parent() != child || grandchild1.Parent() != child)
        {
            return false;
        }

        if (child.Parent() != node)
        {
            return false;
        }

        if (!node.Parent().IsNull)
        {
            return false;
        }
        grandchild2.SetParent(node);
        if (greatGrandChild.Parent() != grandchild2)
        {
            return false;
        }

        if (grandchild2.Parent() != node)
        {
            return false;
        }

        if (node.ChildCount() != 2)
        {
            return false;
        }

        if (child.ChildCount() != 1)
        {
            return false;
        }
        return true;
    }

    public static bool SetParentToSelfTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        try
        {
            node.SetParent(node);
        }
        catch (Exception e)
        {
            return true;
        }
        return false;
    }

    public static bool SetParentToNullTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        try
        {
            var nullNode = node.Parent();
            node.SetParent(nullNode);
        }
        catch (NullReferenceException e)
        {
            return true;
        }
        return false;
    }

    public static bool AddChildTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild();
        node.AddChild("child");
        if (node.ChildCount() != 2)
        {
            return false;
        }

        if (node.GetChild(1).Name != "child")
        {
            return false;
        }
        return true;
    }

    public static bool GetChildTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("child");
        var child = node.GetChild(0);

        if (child.Name != "child")
        {
            return false;
        }

        if (child.HasParent() == false)
        {
            return false;
        }
        return true;
    }

    public static bool RemoveChildByIndexTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("keep1");
        node.AddChild("remove");
        node.AddChild("keep2");
        node.RemoveChild(1);
        if (node.ChildCount() != 2)
        {
            return false;
        }

        if (node.GetChild(0).Name != "keep1")
        {
            return false;
        }

        if (node.GetChild(1).Name != "keep2")
        {
            return false;
        }
        return true;
    }

    public static bool RemoveChildByReferenceTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("keep1");
        var remove = node.AddChild("remove");
        node.RemoveChild(remove);
        if (node.ChildCount() != 1)
        {
            return false;
        }

        if (node.GetChild(0).Name != "keep1")
        {
            return false;
        }
        return true;
    }

    public static bool AddDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform nodeTransform = new Transform();
        nodeTransform.Translate(10,20,30);
        node.AddDataComponent(nodeTransform);
        var nTransform = node.GetDataComponent<Transform>();
        if (!Vector3.Approximately(nTransform.Position, new Vector3(10, 20, 30)))
        {
            return false;
        }
        return true;
    }

    public static bool AddDataComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var type = DataTypeId.For<Vector3>();
        node.AddDataComponentUnsafe(type,nodeVector3);
        var nVector3 = node.GetDataComponent<Vector3>();
        if (!Vector3.Approximately(nVector3, new Vector3(5, 10, 15)))
        {
            return false;
        }
        return true;
    }

    public static bool RemoveDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        node.AddDataComponent(nodeVector3);
        node.RemoveDataComponent<Vector3>();
        if (node.HasDataComponent<Vector3>())
        {
            return false;
        }
        return true;
    }

    public static bool RemoveDataComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var id = DataTypeId.For<Vector3>();
        node.AddDataComponent(nodeVector3);
        node.RemoveDataComponent(id);
        if (node.HasDataComponent<Vector3>())
        {
            return false;
        }
        return true;
    }

    public static bool HasDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector4 nvec4 = new Vector4();
        node.AddDataComponent(nvec4);
        if (!node.HasDataComponent<Vector4>())
        {
            return false;
        }

        if (node.HasDataComponent<Vector3>())
        {
            return false;
        }
        return true;
    }

    public static bool HasDataComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var id = DataTypeId.For<Vector4>();
        Vector4 nvec4 = new Vector4();
        node.AddDataComponent(nvec4);
        if (!node.HasDataComponent(id))
        {
            return false;
        }
        id = DataTypeId.For<Vector3>();
        if (node.HasDataComponent(id))
        {
            return false;
        }
        return true;
    }

    public static bool GetDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform nodeTransform = new Transform();
        nodeTransform.Translate(10,20,30);
        node.AddDataComponent(nodeTransform);
        var nTransform = node.GetDataComponent<Transform>();
        if (!Vector3.Approximately(nTransform.Position, new Vector3(10, 20, 30)))
        {
            return false;
        }
        return true;
    }

    public static bool GetDataComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var type = DataTypeId.For<Vector3>();
        node.AddDataComponent(nodeVector3);
        var nVector3 = node.GetDataComponentUnsafe<Vector3>(type);
        if (!Vector3.Approximately(nVector3, new Vector3(5, 10, 15)))
        {
            return false;
        }
        return true;
    }

    public static bool AddReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponent<DummyReferenceType>();
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return false;
        }

        if (!ReferenceEquals(rc, component))
        {
            return false;
        }
        
        return true;
    }

    public static bool AddReferenceComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        ReferenceTypeId componentId = ReferenceTypeId.For<DummyReferenceType>();
        node.AddReferenceComponentUnsafe(componentId,component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponent<DummyReferenceType>();
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return false;
        }

        if (!ReferenceEquals(rc, component))
        {
            return false;
        }
        
        return true;
    }

    public static bool GetReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponent<DummyReferenceType>();
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return false;
        }

        if (!ReferenceEquals(rc, component))
        {
            return false;
        }
        
        return true;
    }

    public static bool GetReferenceComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        ReferenceTypeId componentId = ReferenceTypeId.For<DummyReferenceType>();
        node.AddReferenceComponent(component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponentUnsafe<DummyReferenceType>(componentId);
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return false;
        }

        if (!ReferenceEquals(rc, component))
        {
            return false;
        }
        
        return true;
    }

    public static bool HasReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }

        if (node.HasReferenceComponent<Texture2D>())
        {
            return false;
        }
        return true;
    }

    public static bool HasReferenceComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        ReferenceTypeId componentId = ReferenceTypeId.For<DummyReferenceType>();
        ReferenceTypeId textureId = ReferenceTypeId.For<Texture2D>();
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent(componentId))
        {
            return false;
        }

        if (node.HasReferenceComponent(textureId))
        {
            return false;
        }
        return true;
    }

    public static bool RemoveReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        node.RemoveReferenceComponent<DummyReferenceType>();
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        return true;
    }

    public static bool RemoveReferenceComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        var componentId = ReferenceTypeId.For<DummyReferenceType>();
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        node.RemoveReferenceComponent(componentId);
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return false;
        }
        return true;
    }

    public static bool CumulativeTransformNoTransformTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        if (node.HasDataComponent<Transform>())
        {
            return false;
        }
        var transform = node.CumulativeTransform();
        if (node.HasDataComponent<Transform>())
        {
            return false;
        }

        if (!Vector3.Approximately(transform.Position, new Vector3(0, 0, 0)))
        {
            return false;
        }

        if (!Vector3.Approximately(transform.Scale, new Vector3(1, 1, 1)))
        {
            return false;
        }

        if (!Quaternion.Approximately(transform.Rotation, new Quaternion(0, 0, 0, 1)))
        {
            return false;
        }
        return true;
    }

    public static bool CumulativeTransformWithTransformTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform transform = new Transform();
        transform.Position = new Vector3(1, 2, 3);
        node.AddDataComponent(transform);
        var nodeTransform = node.CumulativeTransform();
        if (!Transform.Approximately(transform, nodeTransform))
        {
            return false;
        }
        return true;
    }

    public static bool CumulativeTransformNoTransformInChainTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild = child.AddChild();
        Transform transform = new Transform();
        transform.Position = new Vector3(1, 2, 3);
        transform.Scale = new Vector3(1, 1, 1);
        grandchild.AddDataComponent(transform);
        var nodeTransform = grandchild.CumulativeTransform();
        if (!Transform.Approximately(transform, nodeTransform))
        {
            return false;
        }
        return true;
    }

    public static bool CumulativeTransformOnlyInChainTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild = child.AddChild();
        Transform transform = new Transform();
        transform.Position = new Vector3(1, 2, 3);
        transform.Scale = new Vector3(1, 1, 1);
        child.AddDataComponent(transform);
        var nodeTransform = grandchild.CumulativeTransform();
        if (!Transform.Approximately(transform, nodeTransform))
        {
            return false;
        }
        return true;
    }

    public static bool CumulativeTransformWithTransformInChainTest()
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
        var combined = transform + transform;
        if (!Transform.Approximately(nodeTransform, combined))
        {
            return false;
        }
        return true;
    }

    public static bool EnabledDisabledTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        if (node.Enabled ||  child.Enabled ||  grandchild1.Enabled || grandchild2.Enabled)
        {
            return false;
        }
        node.Enabled = true;
        if (!node.Enabled)
        {
            return false;
        }
        if (!child.Enabled)
        {
            return false;
        }
        if (!grandchild1.Enabled)
        {
            return false;
        }
        if (!grandchild2.Enabled)
        {
            return false;
        }
        node.Enabled = false;
        if (node.Enabled ||  child.Enabled ||  grandchild1.Enabled || grandchild2.Enabled)
        {
            return false;
        }
        return true;
    }
}