using Crucible.Core;

namespace CubeExample;

public class CubeGameLoop: GameLoop
{
    public override void Initialize()
    {
        Console.WriteLine("Initializing Cube Example");
        var root = GameManager.RootNode;
        
        
        var model = root.AddChild("Model");
        Transform modelTransform = new Transform();
        model.AddDataComponent(modelTransform);
        
        var camera = root.AddChild("Camera");
        Camera newCamera = new Camera(.01f,100,90,1920,1080,true);
        camera.AddDataComponent(newCamera);
        Transform cameraTransform = new Transform();
        cameraTransform.Translate(0,0,-10f);
        camera.AddDataComponent(cameraTransform);
    }

    public override void Update(double deltaTime)
    {
        var model = GameManager.RootNode.GetChild(0);
        spinModel(ref model.GetDataComponent<Transform>(),deltaTime);
    }

    private void spinModel(ref Transform modelTransform, double deltaTime)
    {
        modelTransform.Rotate((float)(2*3.14159f*deltaTime), new Vector3(0,1,0));
    }
}