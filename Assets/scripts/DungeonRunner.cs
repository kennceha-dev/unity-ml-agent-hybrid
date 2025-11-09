using UnityEngine;

[RequireComponent(typeof(DungeonGenerator))]
public class DungeonRunner : MonoBehaviour
{
    private DungeonGenerator generator;

    void Awake()
    {
        generator = GetComponent<DungeonGenerator>();
    }

    void Start()
    {
        generator.Generate();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
            generator.Generate();
    }
}
