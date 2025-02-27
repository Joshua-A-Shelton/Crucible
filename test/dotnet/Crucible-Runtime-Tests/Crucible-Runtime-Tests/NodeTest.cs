using System.Runtime.InteropServices;
using Crucible.Core;

namespace CrucibleRuntimeTests;

public static class NodeTest
{
    private static bool Family(ref UUID rootNode)
    {
        try
        {
            NodeReference? rootReference = NodeReference.FromUUID(rootNode);
            if (rootReference == null)
            {
                Console.WriteLine("Root node cannot be retrieved from UUID");
                return false;
            }
            var root = rootReference.Value;
            if (root.ChildCount() != 0)
            {
                Console.WriteLine("Root node has children before adding them");
                return false;
            }
            var child = root.AddChild();
            if (root.ChildCount() != 1)
            {
                Console.WriteLine("Root node doesn't have children after adding one");
                return false;
            }
            var grandchild = child.AddChild();
            if (grandchild.Parent() != child)
            {
                Console.WriteLine("Grandchild Node's parent is not child");
                return false;
            }
            grandchild.SetParent(root);
            if (root.ChildCount() != 2 || child.ChildCount()!=0 || grandchild.Parent() != root)
            {
                Console.WriteLine("Reparenting not successful");
                return false;
            }
        
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return false;
        }
    }

    private static bool Transforms(ref UUID nodeID1, ref UUID nodeID2)
    {
        var node1R = NodeReference.FromUUID(nodeID1);
        var node2R = NodeReference.FromUUID(nodeID2);
        if (node1R == null || node2R == null)
        {
            Console.WriteLine("Unable to get nodes from IDs");
            return false;
        }

        NodeReference node1 = node1R.Value;
        NodeReference node2 = node2R.Value;
        
        node1.Transform.Translate(1,2,3);
        if (node1.Transform.Position != new Vector3(1, 2, 3))
        {
            Console.WriteLine("Transform didn't translate");
            return false;
        }

        //I've got a lot more to test
        return false;

        return true;

    }
}