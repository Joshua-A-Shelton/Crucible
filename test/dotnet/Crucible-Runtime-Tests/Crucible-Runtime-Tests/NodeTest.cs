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
    
    struct TestStruct
    {
        public int TestValue;
    }

    class TestClass
    {
        public int TestValue=0;
    }

    private static bool Components(ref UUID rootNode)
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

            root.AddDataComponent(new TestStruct());

            var componentStruct = root.GetDataComponent<TestStruct>();
            componentStruct.TestValue = 7;
            root.GetDataComponent<TestStruct>() = componentStruct;
            var componentStruct2 = root.GetDataComponent<TestStruct>();
            if (componentStruct.TestValue != componentStruct2.TestValue)
            {
                Console.WriteLine("Data Components not updating properly");
                return false;
            }
            
            root.AddReferenceComponent(new TestClass());
            var tClass = root.GetReferenceComponent<TestClass>();
            tClass.TestValue = 777;
            var tClass2 = root.GetReferenceComponent<TestClass>();
            if (tClass != tClass2)
            {
                Console.WriteLine("Reference components don't refer to same reference");
                return false;
            }
            if (tClass2.TestValue != 777)
            {
                Console.WriteLine("Reference data is not persistent");
                return false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }

        return true;
    }

    private static bool Names(ref UUID rootNode)
    {
        NodeReference? rootReference = NodeReference.FromUUID(rootNode);
        if (rootReference == null)
        {
            Console.WriteLine("Root node cannot be retrieved from UUID");
            return false;
        }
        var root = rootReference.Value;
        
        root.Name = "I Am Root";
        if (root.Name != "I Am Root")
        {
            Console.WriteLine("Unable to get or set root name");
            Console.WriteLine("Getting: "+root.Name);
            return false;
        }
        
        Console.WriteLine(root.Name);

        var child = root.AddChild("I Am Child");
        if (child.Name != "I Am Child")
        {
            Console.WriteLine("Unable to get or set root name");
            Console.WriteLine("Getting: "+child.Name);
            return false;
        }

        return true;
    }
}