using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SelectNDISouce : MonoBehaviour
{
    [SerializeField] string sourceName;
    NdiReceiver receiver;
    bool sourceFound = false;
    void Start()
    {
        receiver = GetComponent<NdiReceiver>();
    }

    // Update is called once per frame
    void Update()
    {
        if (!sourceFound)
        {
            List<string> availableSourceNames = NdiFinder.sourceNames.ToList();
            string toSearch = $"({sourceName})";
            for (int i = 0; i < availableSourceNames.Count; i++)
            {
                if (availableSourceNames[i].EndsWith(toSearch))
                {
                    receiver.ndiName = availableSourceNames[i];
                    sourceFound = true;
                    break;
                }
            }
        }
    }
}
