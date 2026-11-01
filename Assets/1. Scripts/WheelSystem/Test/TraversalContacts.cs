using System.Collections.Generic;
using UnityEngine;

public class TraversalContacts : MonoBehaviour
{
    public readonly HashSet<string> Names = new();
    private void OnCollisionStay(Collision collision) => Names.Add(collision.collider.name);
    private void OnCollisionEnter(Collision collision) => Names.Add(collision.collider.name);
}
