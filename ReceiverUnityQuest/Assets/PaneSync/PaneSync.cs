using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Assertions;

public class PaneSync : MonoBehaviour
{
    [SerializeField] Transform MeshReceiver;
    [SerializeField] CurvedScreen CurvedScreen;
    [SerializeField] float ScaleReference = 400.0f;
    [SerializeField] int Port = 8667;

    Vector2 screenResolution = new Vector2(1920, 1080);
    Vector2 panePosition = new Vector2(0, 148);
    Vector2 paneSize = new Vector2(768, 344);
    Material screenMaterial = null;

    private UdpClient udpClient;
    private Thread udpListenerThread;

    void Start()
    {
        screenMaterial = CurvedScreen.GetComponent<MeshRenderer>().material;
        // Start UDP listener thread
        udpListenerThread = new Thread(new ThreadStart(ListenForIncomingRequests));
        udpListenerThread.IsBackground = true;
        udpListenerThread.Start();
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 paneCenter = panePosition + paneSize / 2;
        Vector3 objectPosition = CurvedScreen.Get3dPosition(paneCenter / screenResolution);
        MeshReceiver.localPosition = objectPosition;
        float scale = Mathf.Min(paneSize.x, paneSize.y) / ScaleReference;
        MeshReceiver.localScale = new Vector3(scale, scale, scale);
        // Update the material
        screenMaterial.SetVector("_PanePosition", panePosition);
        screenMaterial.SetVector("_PaneSize", paneSize);
        screenMaterial.SetVector("_ScreenResolution", screenResolution);
    }

    void UpdatePaneInfo(string message)
    {
        string[] stringArray = message.Split(',');
        Assert.IsTrue(stringArray.Length == 4);
        panePosition = new Vector2(float.Parse(stringArray[0]), float.Parse(stringArray[1]));
        paneSize = new Vector2(float.Parse(stringArray[2]), float.Parse(stringArray[3]));
    }

    private void ListenForIncomingRequests()
    {
        udpClient = new UdpClient(Port);
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, Port);

        try
        {
            Debug.Log($"UDP server is listening at port {Port}");

            while (true)
            {
                // Receive bytes from the client
                byte[] receivedBytes = udpClient.Receive(ref endPoint);
                string message = Encoding.ASCII.GetString(receivedBytes);

                Debug.Log("Received from Blender (UDP): " + message);
                UpdatePaneInfo(message);
            }
        }
        catch (SocketException socketException)
        {
            Debug.Log("SocketException: " + socketException.ToString());
        }
    }

    private void OnApplicationQuit()
    {
        // Stop the UDP listener when quitting the application
        if (udpClient != null)
        {
            udpClient.Close();
        }

        // Clean up the listener thread
        if (udpListenerThread != null)
        {
            udpListenerThread.Abort();
        }
    }
}
