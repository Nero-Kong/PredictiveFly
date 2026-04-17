using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;


namespace UnityExtension
{
    public static class GizmoExtent
    {
        public static void DrawPose(Vector3 position, Quaternion rotation)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(position, position + (rotation * Vector3.forward));

            Gizmos.color = Color.green;
            Gizmos.DrawLine(position, position + (rotation * Vector3.up));

            Gizmos.color = Color.red;
            Gizmos.DrawLine(position, position + (rotation * Vector3.right));
        }

        public static void DrawPose(Transform transform)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, transform.position + (transform.rotation * Vector3.forward));

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + (transform.rotation * Vector3.up));

            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, transform.position + (transform.rotation * Vector3.right));
        }

        public static void DrawPose(Pose pose)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(pose.position, pose.position + (pose.rotation * Vector3.forward));

            Gizmos.color = Color.green;
            Gizmos.DrawLine(pose.position, pose.position + (pose.rotation * Vector3.up));

            Gizmos.color = Color.red;
            Gizmos.DrawLine(pose.position, pose.position + (pose.rotation * Vector3.right));
        }

        public static void DrawPoly(List<Vector3> vertices, Color color = default)
        {
            if (vertices == null || vertices.Count < 3) return;

            if (color.Equals(default)) { color = Color.red; }

            Gizmos.color = color;
            for (int v = 0; v < vertices.Count-1; v++)
            {
                Gizmos.DrawLine(vertices[v], vertices[v + 1]);
            }

            Gizmos.DrawLine(vertices[vertices.Count - 1], vertices[0]);

        }

        public static void DrawPoly(Vector3[] vertices, Color color = default)
        {
            if (vertices == null || vertices.Length < 3) return;

            if (color.Equals(default)) { color = Color.red; }

            Gizmos.color = color;
            for (int v = 0; v < vertices.Length - 1; v++)
            {
                Gizmos.DrawLine(vertices[v], vertices[v + 1]);
            }

            Gizmos.DrawLine(vertices[vertices.Length - 1], vertices[0]);

        }

        public static void DrawLine(Vector3[] positions, Color color = default)
        {
            if (positions == null || positions.Length < 2) return;

            if (color.Equals(default)) { color = Color.red; }

            for (int i = 0; i < positions.Length - 1; i++)
            {
                Gizmos.DrawLine(positions[i], positions[i + 1]);
            }
        }

    }
}

