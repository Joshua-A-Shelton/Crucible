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
}