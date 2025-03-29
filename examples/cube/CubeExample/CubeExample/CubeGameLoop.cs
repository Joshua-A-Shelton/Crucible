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
        
        MeshRenderer meshRenderer = new MeshRenderer("resources/cube.cmodel","flat-test",0);
        model.AddReferenceComponent(meshRenderer);
        
        var camera = root.AddChild("Camera");
        Camera newCamera = new Camera(.01f,100,90,1920,1080,true);
        camera.AddDataComponent(newCamera);
        Transform cameraTransform = new Transform();
        cameraTransform.Translate(0,0,-10f);
        camera.AddDataComponent(cameraTransform);
        
        var cubeTexture = new Texture("resources/cube.png",Texture.Format.R8G8B8A8_UNORM,1);
        meshRenderer.Material.SetTexture(meshRenderer.Material.GetTextureName(0), cubeTexture);
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