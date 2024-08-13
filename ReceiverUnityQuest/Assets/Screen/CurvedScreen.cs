using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class CurvedScreen : MonoBehaviour
{
    [SerializeField] float width = 16.0f / 9.0f;
    [SerializeField] float height = 1.0f;
    [SerializeField] float depth = 0.1f;
    [SerializeField] int HorizontalSubdivision = 100;
    [SerializeField] int VerticalSubdivision = 1;

     public static Vector2 FindCircumcenter(Vector2 A, Vector2 B, Vector2 C)
    {
        // Calculate the midpoints of AB and BC
        Vector2 midAB = (A + B) / 2f;
        Vector2 midBC = (B + C) / 2f;

        // Calculate the perpendicular bisectors of AB and BC
        Vector2 dirAB = B - A;
        Vector2 dirBC = C - B;

        Vector2 perpAB = new Vector2(-dirAB.y, dirAB.x);
        Vector2 perpBC = new Vector2(-dirBC.y, dirBC.x);

        // Calculate the circumcenter using the intersection of the perpendicular bisectors
        Vector2 circumcenter = LineIntersection(midAB, midAB + perpAB, midBC, midBC + perpBC);

        return circumcenter;
    }

    private static Vector2 LineIntersection(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        // Line p1p2 represented as a1x + b1y = c1
        float a1 = p2.y - p1.y;
        float b1 = p1.x - p2.x;
        float c1 = a1 * p1.x + b1 * p1.y;

        // Line q1q2 represented as a2x + b2y = c2
        float a2 = q2.y - q1.y;
        float b2 = q1.x - q2.x;
        float c2 = a2 * q1.x + b2 * q1.y;

        float determinant = a1 * b2 - a2 * b1;

        if (determinant == 0)
        {
            // Lines are parallel, no intersection.
            // Returning a zero vector as a placeholder (this should be handled properly in production code)
            return Vector2.zero;
        }
        else
        {
            float x = (b2 * c1 - b1 * c2) / determinant;
            float y = (a1 * c2 - a2 * c1) / determinant;
            return new Vector2(x, y);
        }
    }

    Mesh GenerateCurvedScreenMesh()
    {
        // compute the center of the screen
        Vector2 leftCorner = new Vector2(-width / 2, 0);
        Vector2 middle = new Vector2(0, depth);
        Vector2 rightCorner = new Vector2(width / 2, 0);
        Vector2 center = FindCircumcenter(leftCorner, middle, rightCorner);
        float radius = Vector2.Distance(center, leftCorner);
        float leftAngle = Mathf.Atan2(leftCorner.y - center.y, leftCorner.x - center.x);
        float rightAngle = Mathf.Atan2(rightCorner.y - center.y, rightCorner.x - center.x);
        // Generate the mesh
        Mesh mesh = new Mesh();
        Vector3[] positions = new Vector3[(HorizontalSubdivision + 1) * (VerticalSubdivision + 1)];
        Vector2[] uvs = new Vector2[positions.Length];
        int[] indices = new int[HorizontalSubdivision * VerticalSubdivision * 6];
        // Generate indices
        int i = 0;
        for (int y = 0; y < VerticalSubdivision; y++)
        {
            for (int x = 0; x < HorizontalSubdivision; x++)
            {
                int v = y * (HorizontalSubdivision + 1) + x;
                // Triangle 1
                indices[i++] = v + 1;
                indices[i++] = v;
                indices[i++] = v + HorizontalSubdivision + 1;
                // Triangle 2
                indices[i++] = v + HorizontalSubdivision + 2;
                indices[i++] = v + 1;
                indices[i++] = v + HorizontalSubdivision + 1;
            }
        }
        // Generate positions
        i = 0;
        for (int y = 0; y <= VerticalSubdivision; y++)
        {
            for (int x = 0; x <= HorizontalSubdivision; x++, i++)
            {
                float u = (float)x / HorizontalSubdivision;
                float v = (float)y / VerticalSubdivision;
                uvs[i] = new Vector2(u, v);
                float angle = math.lerp(leftAngle, rightAngle, u);
                Vector2 pos2d = new Vector2(center.x + radius * Mathf.Cos(angle), center.y + radius * Mathf.Sin(angle));
                positions[i] = new Vector3(
                    pos2d.x,
                    (v - 0.5f) * height,
                    pos2d.y
                );
            }
        }
        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.triangles = indices;
        mesh.name = "CurvedScreen";
        return mesh;
    }
    void Start()
    {
        Mesh mesh = GenerateCurvedScreenMesh();
        GetComponent<MeshFilter>().mesh = mesh;
    }
}