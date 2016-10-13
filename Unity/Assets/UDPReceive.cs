
using UnityEngine;
using System.Collections;

using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class UDPReceive : MonoBehaviour {

    public Transform trackedObject;
    public int port = 8919;
    public Vector3 Lighthouse1XYZ = new Vector3(0, 0, 0);
    public Vector3 Lighthouse2XYZ = new Vector3(1.3f, 0f, 1.9f);

    UdpClient client;
    bool abort;
    float x = 0.0f, y = 0.0f, z = 0.0f;
    Thread receiveThread;

    string lastReceivedUDPPacket = "";


    void OnGUI() {
        Rect rectObj = new Rect(40, 10, 200, 400);
        GUIStyle style = new GUIStyle();
        style.alignment = TextAnchor.UpperLeft;
        GUI.Box(rectObj, "UDPReceive\n127.0.0.1 " + port + "\n"
            + "\nLast Packet: \n" + lastReceivedUDPPacket
            , style);
        style.fontSize = 24;
        rectObj = new Rect(150, 10, 350, 400);
        GUI.Box(rectObj, "X: " + x + "\nY: " + y + "\nZ: " + z, style);
    }

    void OnDisable() {
        Debug.Log("Aborting");
        abort = true;
    }

    // init
    private void Start() {
        abort = false;
        receiveThread = new Thread(
            new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

    }
    public static bool LineLineIntersection(out Vector3 intersection, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2) {

        Vector3 lineVec3 = linePoint2 - linePoint1;
        Vector3 crossVec1and2 = Vector3.Cross(lineVec1, lineVec2);
        Vector3 crossVec3and2 = Vector3.Cross(lineVec3, lineVec2);

        float planarFactor = Vector3.Dot(lineVec3, crossVec1and2);

        //is coplanar, and not parrallel
        if (Mathf.Abs(planarFactor) < 0.0001f && crossVec1and2.sqrMagnitude > 0.0001f) {
            float s = Vector3.Dot(crossVec3and2, crossVec1and2) / crossVec1and2.sqrMagnitude;
            intersection = linePoint1 + (lineVec1 * s);
            return true;
        } else {
            intersection = Vector3.zero;
            return false;
        }
    }

    // receive thread
    private void ReceiveData() {

        client = new UdpClient(port);
        while (!abort) {

            try {
                // Bytes empfangen.
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);

                // Bytes mit der UTF8-Kodierung in das Textformat kodieren.
                string text = Encoding.UTF8.GetString(data);

                // Den abgerufenen Text anzeigen.
                print(">> " + text);

                Char spc = ' ';
                String[] coords = text.Split(spc);
                /* x = Convert.ToSingle(coords[0]);
				 y = Convert.ToSingle(coords[1]);
				 //z = Convert.ToSingle(coords[2]);*/
                float thetaA = Convert.ToSingle(coords[0]) * Mathf.Deg2Rad;
                float thetaB = (180f + Convert.ToSingle(coords[2])) * Mathf.Deg2Rad;
                float phiA = (90f - Convert.ToSingle(coords[1])) * Mathf.Deg2Rad;
                float phiB = Convert.ToSingle(coords[3]) * Mathf.Deg2Rad;
                Vector3 line1 = new Vector3(Mathf.Cos(thetaA), 0f, Mathf.Sin(thetaA));
                Vector3 line2 = new Vector3(Mathf.Cos(thetaB), 0f, Mathf.Sin(thetaB));
                Vector3 result;
                if (LineLineIntersection(out result, Lighthouse1XYZ, line1, Lighthouse2XYZ, line2)) {
                    x = result.x;
                    y = -result.z * Mathf.Tan(phiA);
                    z = result.z;
                    print("X: " + x + " Y: " + y + " Z: " + z);
                }
                lastReceivedUDPPacket = text;


            } catch (Exception err) {
                print(err.ToString());
            }
        }
    }

    void Update() {
        trackedObject.position = new Vector3(x, y, z);
    }

}