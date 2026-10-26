using UnityEngine;
using VRPortalToolkit.Remote;

/// <summary>
/// Lets VRPortalToolkit's remote portals send objects, by saving and rebuilding them with
/// serial.cs. Both applications need this file (with serial.cs); it sets itself up on start.
/// </summary>
public class SerialRemoteObjectSerializer : IRemoteObjectSerializer
{
    public string Serialize(GameObject gameObject)
    {
        return serial.ToJson(gameObject);
    }

    public GameObject Deserialize(string json)
    {
        // links to objects in the sending application mean nothing here, so they are cleared
        return serial.FromJson(json, false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        if (RemoteObjectSerializer.current == null)
            RemoteObjectSerializer.current = new SerialRemoteObjectSerializer();
    }
}
