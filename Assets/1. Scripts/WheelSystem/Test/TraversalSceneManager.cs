using Fusion;

/// <summary>Maps the already loaded test scene without editing Build Settings.</summary>
public class TraversalSceneManager : NetworkSceneManagerDefault
{
    public string ScenePath;
    public override SceneRef GetSceneRef(string sceneNameOrPath) =>
        sceneNameOrPath == ScenePath ? SceneRef.FromIndex(0) : base.GetSceneRef(sceneNameOrPath);
}
