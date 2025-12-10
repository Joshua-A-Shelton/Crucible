using Crucible.Core;
using Crucible.Core.ECS;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class NodeTests
{
    public static TestResult UUIDTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        NodeKeeper keeper2 = new NodeKeeper();
        NodeKeeper keeper3 = new NodeKeeper();
        var node1 = keeper.KeptNode.Acquire();
        var node2 = keeper2.KeptNode.Acquire();
        var node3 = keeper3.KeptNode.Acquire();
        if (node1.Uuid() != node1.Uuid() || node2.Uuid() != node2.Uuid() || node3.Uuid() != node3.Uuid())
        {
            return TestResult.Fail("Node UUID is not consistent");
        }
        if (node1.Uuid() == node2.Uuid() || node2.Uuid() == node3.Uuid() || node1.Uuid() == node3.Uuid())
        {
            return TestResult.Fail("Node UUID is not unique");
        }
        return TestResult.Pass();
    }

    public static TestResult NameTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        if (!string.IsNullOrEmpty(node.Name))
        {
            return TestResult.Fail("Node name is not empty");
        }
        node.Name = "Test";
        if (node.Name != "Test")
        {
            return TestResult.Fail("Node name is not equal to assigned name");
        }
        node.Name = "◡̈ツ";
        if (node.Name != "◡̈ツ")
        {
            return TestResult.Fail("Node name is unable to handle non ascii characters");
        }
        return TestResult.Pass();
    }

    public static TestResult ParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        var greatGrandChild = grandchild2.AddChild();
        if (greatGrandChild.Parent() != grandchild2)
        {
            return TestResult.Fail("Node assigned parent is not expected");
        }
        if (grandchild2.Parent() != child || grandchild1.Parent() != child)
        {
            return TestResult.Fail("Node assigned hierarchy is not expected");
        }

        if (child.Parent() != node)
        {
            return TestResult.Fail("Node assigned hierarchy is not expected");
        }

        if (!node.Parent().IsNull)
        {
            return TestResult.Fail("Root node parent IsNull is not true");
        }
        grandchild2.SetParent(node);
        if (greatGrandChild.Parent() != grandchild2)
        {
            return TestResult.Fail("Node assigned hierarchy is not expected");
        }

        if (grandchild2.Parent() != node)
        {
            return TestResult.Fail("Node assigned hierarchy is not expected");
        }

        if (node.ChildCount() != 2)
        {
            return TestResult.Fail("Node child count is not expected");
        }

        if (child.ChildCount() != 1)
        {
            return TestResult.Fail("Node child count is not expected");
        }
        return TestResult.Pass();
    }

    public static TestResult TryGetParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        if (!child.TryGetParent(out Node parent))
        {
            return TestResult.Fail("Child unable to get parent");
        }

        if (parent != node)
        {
            return TestResult.Fail("Child got node which is not parent");
        }
        if (node.TryGetParent(out Node grandParent))
        {
            return TestResult.Fail("Root node acquired parent node");
        }
        return TestResult.Pass();
    }

    public static TestResult SetParentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        var greatGrandChild = grandchild2.AddChild();
        if (greatGrandChild.Parent() != grandchild2)
        {
            return TestResult.Fail("Node's parent is not assigned parent");
        }
        if (grandchild2.Parent() != child || grandchild1.Parent() != child)
        {
            return TestResult.Fail("Nodes' parent is not assigned parent");
        }

        if (child.Parent() != node)
        {
            return TestResult.Fail("Node's parent is not assigned parent");
        }

        if (!node.Parent().IsNull)
        {
            return TestResult.Fail("Root node's parent is not null");
        }
        grandchild2.SetParent(node);
        if (greatGrandChild.Parent() != grandchild2)
        {
            return TestResult.Fail("Node's parent is not assigned parent");
        }

        if (grandchild2.Parent() != node)
        {
            return TestResult.Fail("Node's parent is not re-assigned parent");
        }

        if (node.ChildCount() != 2)
        {
            return TestResult.Fail("Node's child count is unexpected");
        }

        if (child.ChildCount() != 1)
        {
            return TestResult.Fail("Node's child count is unexpected");
        }
        return TestResult.Pass();
    }

    public static TestResult SetParentToSelfTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        try
        {
            node.SetParent(node);
        }
        catch (Exception e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Setting node to own parent was expected to throw an exception");
    }

    public static TestResult SetParentToNullTest()
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
            return TestResult.Pass();
        }
        return TestResult.Fail("Setting node to null was expected to throw an exception");
    }

    public static TestResult AddChildTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild();
        node.AddChild("child");
        if (node.ChildCount() != 2)
        {
            return TestResult.Fail("Node's child count doesn't reflected added child");
        }

        if (node.GetChild(1).Name != "child")
        {
            return TestResult.Fail("Node's new child isn't the same as the one added to it");
        }
        return TestResult.Pass();
    }

    public static TestResult GetChildTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("child");
        var child = node.GetChild(0);

        if (child.Name != "child")
        {
            return TestResult.Fail("Node's new child had different name than the one added to it");
        }

        if (child.HasParent() == false)
        {
            return TestResult.Fail("Child node isn't correctly assigned to parent");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveChildByIndexTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("keep1");
        node.AddChild("remove");
        node.AddChild("keep2");
        node.RemoveChild(1);
        if (node.ChildCount() != 2)
        {
            return TestResult.Fail("Node's child count is unexpected");
        }

        if (node.GetChild(0).Name != "keep1")
        {
            return TestResult.Fail("Node's first child isn't the expected node after removing a later sibling");
        }

        if (node.GetChild(1).Name != "keep2")
        {
            return TestResult.Fail("Node's second child isn't the expected node after removing a previous sibling");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveChildByReferenceTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.AddChild("keep1");
        var remove = node.AddChild("remove");
        node.RemoveChild(remove);
        if (node.ChildCount() != 1)
        {
            return TestResult.Fail("Node's child count is unexpected");
        }

        if (node.GetChild(0).Name != "keep1")
        {
            return TestResult.Fail("Detached node is incorrect");
        }
        return TestResult.Pass();
    }

    public static TestResult Detach()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        node.Enabled = true;

        var child = node.AddChild();
        var grandChild = child.AddChild();
        if (child.Enabled == false)
        {
            return TestResult.Fail("Node already disabled before removal");
        }
        var childKeeper = child.Detach();
        if (node.ChildCount() != 0)
        {
            return TestResult.Fail("Root node did not removed detached node");
        }

        if (child.ChildCount() != 1)
        {
            return TestResult.Fail("Detached node altered children as a result of disattachment");
        }

        if (child.HasParent())
        {
            return TestResult.Fail("Detached node has parent");
        }

        if (child.Enabled)
        {
            return TestResult.Fail("Detached node is enabled after removing");
        }

        if (grandChild.Parent() != child)
        {
            return TestResult.Fail("Detached node altered children as a result of disattachment");
        }

        child.Enabled = true;
        var grandchildKeeper = grandChild.Detach(false);
        if (!grandChild.Enabled)
        {
            return TestResult.Fail("Detached node is diabled after removing but not requesting auto disable");
        }
        
        return TestResult.Pass();

    }

    public static TestResult DetachFailIfRoot()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        try
        {
            node.Detach();
        }
        catch (InvalidOperationException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Root node detached, but didn't throw exception");
    }

    public static TestResult DetachFailIfAlreadyKept()
    {
        
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        NodeKeeper keeper2 = new NodeKeeper();
        var node2 = keeper2.KeptNode.Acquire();
        //can't detach root nodes
        node2.SetParent(node);
        try
        {
            node2.Detach();
        }
        catch (InvalidOperationException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Detached node detached again, but didn't throw exception");
    }

    public static TestResult AddDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform nodeTransform = new Transform();
        nodeTransform.Translate(10,20,30);
        node.AddDataComponent(nodeTransform);
        var nTransform = node.GetDataComponent<Transform>();
        if (!Vector3.Approximately(nTransform.Position, new Vector3(10, 20, 30)))
        {
            return TestResult.Fail("Node data component not the same value as assigned component");
        }
        return TestResult.Pass();
    }

    public static TestResult AddDataComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var type = DataTypeId.For<Vector3>();
        node.AddDataComponentUnsafe(type,nodeVector3);
        var nVector3 = node.GetDataComponent<Vector3>();
        if (!Vector3.Approximately(nVector3, new Vector3(5, 10, 15)))
        {
            return TestResult.Fail("Node data component not the same value as assigned component");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        node.AddDataComponent(nodeVector3);
        node.RemoveDataComponent<Vector3>();
        if (node.HasDataComponent<Vector3>())
        {
            return TestResult.Fail("Node still retains data component after removing");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveDataComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var id = DataTypeId.For<Vector3>();
        node.AddDataComponent(nodeVector3);
        node.RemoveDataComponent(id);
        if (node.HasDataComponent<Vector3>())
        {
            return TestResult.Fail("Node still retains data component after removing");;
        }
        return TestResult.Pass();
    }

    public static TestResult HasDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector4 nvec4 = new Vector4();
        node.AddDataComponent(nvec4);
        if (!node.HasDataComponent<Vector4>())
        {
            return TestResult.Fail("Node unable to retrieve assigned data component");
        }

        if (node.HasDataComponent<Vector3>())
        {
            return TestResult.Fail("Node has data component that was never assigned");
        }
        return TestResult.Pass();
    }

    public static TestResult HasDataComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var id = DataTypeId.For<Vector4>();
        Vector4 nvec4 = new Vector4();
        node.AddDataComponent(nvec4);
        if (!node.HasDataComponent(id))
        {
            return TestResult.Fail("Node unable to retrieve assigned data component");
        }
        id = DataTypeId.For<Vector3>();
        if (node.HasDataComponent(id))
        {
            return TestResult.Fail("Node has data component that was never assigned");
        }
        return TestResult.Pass();
    }

    public static TestResult GetDataComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform nodeTransform = new Transform();
        nodeTransform.Translate(10,20,30);
        node.AddDataComponent(nodeTransform);
        var nTransform = node.GetDataComponent<Transform>();
        if (!Vector3.Approximately(nTransform.Position, new Vector3(10, 20, 30)))
        {
            return TestResult.Fail("Unable to get assigned data component");
        }
        return TestResult.Pass();
    }

    public static TestResult GetDataComponentUnsafeTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Vector3 nodeVector3 = new Vector3(5,10,15);
        var type = DataTypeId.For<Vector3>();
        node.AddDataComponent(nodeVector3);
        var nVector3 = node.GetDataComponentUnsafe<Vector3>(type);
        if (!Vector3.Approximately(nVector3, new Vector3(5, 10, 15)))
        {
            return TestResult.Fail("Node unable to retrieve assigned data component");
        }
        return TestResult.Pass();
    }

    public static TestResult AddReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponent<DummyReferenceType>();
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return TestResult.Fail("Nodes assigned identical reference component have different objects");
        }

        if (!ReferenceEquals(rc, component))
        {
            return TestResult.Fail("Nodes assigned identical reference component have different objects");
        }
        
        return TestResult.Pass();
    }

    public static TestResult AddReferenceComponentUnsafeTest()
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
            return TestResult.Fail("Nodes assigned identical reference component have different objects");
        }

        if (!ReferenceEquals(rc, component))
        {
            return TestResult.Fail("Nodes assigned identical reference component have different objects");
        }
        
        return TestResult.Pass();
    }

    public static TestResult GetReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        component.thing1 = 50;
        var rc = node.GetReferenceComponent<DummyReferenceType>();
        if (rc.thing1 != 50 && rc.thing2 != 2)
        {
            return TestResult.Fail("Nodes assigned reference component have different objects");
        }

        if (!ReferenceEquals(rc, component))
        {
            return TestResult.Fail("Nodes assigned reference component have different objects");
        }
        
        return TestResult.Pass();
    }

    public static TestResult GetReferenceComponentUnsafeTest()
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
            return TestResult.Fail("Nodes assigned reference component have different objects");
        }

        if (!ReferenceEquals(rc, component))
        {
            return TestResult.Fail("Nodes assigned reference component have different objects");
        }
        
        return TestResult.Pass();
    }

    public static TestResult HasReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node does not have assigned reference component");
        }

        if (node.HasReferenceComponent<Texture2D>())
        {
            return TestResult.Fail("Node has unassigned reference component");
        }
        return TestResult.Pass();
    }

    public static TestResult HasReferenceComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        ReferenceTypeId componentId = ReferenceTypeId.For<DummyReferenceType>();
        ReferenceTypeId textureId = ReferenceTypeId.For<Texture2D>();
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent(componentId))
        {
            return TestResult.Fail("Node does not have assigned reference component");
        }

        if (node.HasReferenceComponent(textureId))
        {
            return TestResult.Fail("Node has unassigned reference component");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveReferenceComponentTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node has unassigned reference component");
        }
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node does not have assigned reference component");
        }
        node.RemoveReferenceComponent<DummyReferenceType>();
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node has removed reference component");
        }
        return TestResult.Pass();
    }

    public static TestResult RemoveReferenceComponentByIdTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        DummyReferenceType component = new DummyReferenceType(1, 2);
        var componentId = ReferenceTypeId.For<DummyReferenceType>();
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node has unassigned reference component");
        }
        node.AddReferenceComponent(component);
        if (!node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node does not have assigned reference component");
        }
        node.RemoveReferenceComponent(componentId);
        if (node.HasReferenceComponent<DummyReferenceType>())
        {
            return TestResult.Fail("Node has removed reference component");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTransformNoTransformTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        if (node.HasDataComponent<Transform>())
        {
            return TestResult.Fail("Node has unassigned transform component");
        }
        var transform = node.CumulativeTransform();
        if (node.HasDataComponent<Transform>())
        {
            return TestResult.Fail("Node has unassigned transform component");
        }

        if (!Vector3.Approximately(transform.Position, new Vector3(0, 0, 0)))
        {
            return TestResult.Fail("Transform position not default position");
        }

        if (!Vector3.Approximately(transform.Scale, new Vector3(1, 1, 1)))
        {
            return TestResult.Fail("Transform scale not default scale");
        }

        if (!Quaternion.Approximately(transform.Rotation, new Quaternion(0, 0, 0, 1)))
        {
            return TestResult.Fail("Transform orientation not default orientation");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTransformWithTransformTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        Transform transform = new Transform();
        transform.Position = new Vector3(1, 2, 3);
        node.AddDataComponent(transform);
        var nodeTransform = node.CumulativeTransform();
        if (!Transform.Approximately(transform, nodeTransform))
        {
            return TestResult.Fail("Transform not the same as transform component");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTransformNoTransformInChainTest()
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
            return TestResult.Fail("Transform chain not calculating correctly");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTransformOnlyInChainTest()
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
            return TestResult.Fail("Transform chain not calculating correctly");
        }
        return TestResult.Pass();
    }

    public static TestResult CumulativeTransformWithTransformInChainTest()
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
            return TestResult.Fail("Transform chain not calculating correctly");
        }
        return TestResult.Pass();
    }

    public static TestResult EnabledDisabledTest()
    {
        NodeKeeper keeper = new NodeKeeper();
        var node = keeper.KeptNode.Acquire();
        var child = node.AddChild();
        var grandchild1 = child.AddChild();
        var grandchild2 = child.AddChild();
        if (node.Enabled ||  child.Enabled ||  grandchild1.Enabled || grandchild2.Enabled)
        {
            return TestResult.Fail("Nodes added to disabled node are enabled");
        }
        node.Enabled = true;
        if (!node.Enabled)
        {
            return TestResult.Fail("Node was not enabled when set to enabled");
        }
        if (!child.Enabled)
        {
            return TestResult.Fail("child node was not enabled when set to enabled");
        }
        if (!grandchild1.Enabled)
        {
            return TestResult.Fail("child node was not enabled when set to enabled");
        }
        if (!grandchild2.Enabled)
        {
            return TestResult.Fail("child node was not enabled when set to enabled");
        }
        node.Enabled = false;
        if (node.Enabled ||  child.Enabled ||  grandchild1.Enabled || grandchild2.Enabled)
        {
            return TestResult.Fail("Children nodes were not disabled with parent");
        }
        return TestResult.Pass();
    }
}