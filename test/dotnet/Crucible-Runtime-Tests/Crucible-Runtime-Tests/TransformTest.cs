using Crucible.Core;

namespace CrucibleRuntimeTests;

public class TransformTest
{
    private static bool Concat()
    {
        Transform t1 = new Transform();
        t1.Position = new Vector3(1, 0, 0);
        t1.Rotation = new Quaternion(1.57079632679f, new Vector3(1, 0, 0));
        t1.Scale = new Vector3(1, 1, 1);
        
        Transform t2 = new Transform();
        t2.Position = new Vector3(0,1,0);
        t2.Rotation = new Quaternion(1,0,0,0);
        t2.Scale = new Vector3(1, 1, 1);
        
        Transform sum = t2 + t1;
        
        Transform compTransform = new Transform(new Vector3(1,0,-1), new Quaternion(0.707106829f, 0.707106829f,0,0),new Vector3(1, 1, 1));

        if (!Transform.Approximate(compTransform, sum))
        {
            Console.WriteLine("Transforms not concatenating correctly");
            Console.WriteLine("expected:\n"+compTransform.ToString()+"\nactual:\n"+sum.ToString());
            return false;
        }
        
        return true;
    }

    private static bool Decat()
    {
        Transform t1 = new Transform();
        t1.Position = new Vector3(15, .3f, -2565);
        t1.Rotation = new Quaternion(1.57079632679f, new Vector3(1, 2, 0));
        t1.Scale = new Vector3(1, 1, 1);
        
        Transform t2 = new Transform();
        t2.Position = new Vector3(-400,1.5f,0);
        t2.Rotation = new Quaternion(1,0,0,0);
        t2.Scale = new Vector3(2, 3, 2);
        
        Transform sum = t2 + t1;
        Transform sum2 = t1 + t2;

        var compT1 = sum - t2;
        var compT2 = sum2 - t1;

        if (!(Transform.Approximate(compT1, t1) && Transform.Approximate(compT2, t2)))
        {
            Console.WriteLine("Transforms not de-concatenating correctly");
            Console.WriteLine("expected:\n"+t1.ToString()+"\nactual:\n"+compT1.ToString()+"\n");
            Console.WriteLine("expected:\n"+t2.ToString()+"\nactual:\n"+compT2.ToString()+"\n");
            return false;
        }

        return true;
    }

    private static bool Inverse()
    {
        Transform t1 = new Transform();
        t1.Position = new Vector3(-4.265f,8.21f, .0024f);
        t1.Rotation = new Quaternion(1.57079632679f, new Vector3(1, -1, .5f));
        t1.Scale = new Vector3(3, .21f, 1);

        var identity = new Transform();
        var final = t1 + t1.Inverse();

        if (!(Transform.Approximate(identity, final)))
        {
            Console.WriteLine("Transforms inverse not inverting correctly");
            Console.WriteLine("expected:\n"+t1.ToString()+"\nactual:\n"+t1.Inverse().ToString());
            return false;
        }
        return true;
    }

    private static bool Cumulative(ref UUID nodeID)
    {
        var noderef = NodeReference.FromUUID(nodeID);
        if (noderef != null)
        {
            var node = noderef.Value;
            var child = node.AddChild();
            var grandchild = child.AddChild();
            var greatGrandChild = grandchild.AddChild();
            Transform t = new Transform();
            t.Position = new Vector3(1000, 250000, -3200.564f);
            node.AddDataComponent(t);
            t.Position = new Vector3(5, 5, 5);
            grandchild.AddDataComponent(t);
            t.Position = new Vector3(15, 0, 0);
            greatGrandChild.AddDataComponent(t);
            var final = Transform.Cumulative(greatGrandChild);
            var expected = new Vector3(1020, 250005, -3195.564f);
            if (!Vector3.Approximately(final.Position, expected))
            {
                Console.WriteLine("Transforms not accumulating correctly");
                Console.WriteLine("expected:\n"+expected.ToString()+"\nactual:\n"+final.Position.ToString());
                return false;
            }

        }
        else
        {
            Console.WriteLine("Unable to get node reference");
            return false;
        }
        return true;
    }
}