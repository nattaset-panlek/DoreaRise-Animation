using System.Collections.Generic;
using UnityEngine;

namespace Dorea.CharacterTailComponent
{
    public static class CatmullRomSpline
    {
        #region [Public Methods]
        public static Vector3 Evaluate(List<Vector3> points, float t)
        {
            if (points == null || points.Count < 2)
                return Vector3.zero;

            int numSections = points.Count - 1;
            int currPt = Mathf.Min(Mathf.FloorToInt(t * numSections), numSections - 1);
            float u = t * numSections - currPt;

            Vector3 p0 = points[Mathf.Max(currPt - 1, 0)];
            Vector3 p1 = points[currPt];
            Vector3 p2 = points[currPt + 1];
            Vector3 p3 = points[Mathf.Min(currPt + 2, points.Count - 1)];

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * u +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u +
                (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u
            );
        }

        public static Vector3 EvaluateWorld(List<Vector3> localPoints, Transform space, float t)
        {
            if (localPoints == null || localPoints.Count < 2 || space == null)
                return Vector3.zero;

            int numSections = localPoints.Count - 1;
            int currPt = Mathf.Min(Mathf.FloorToInt(t * numSections), numSections - 1);
            float u = t * numSections - currPt;

            Vector3 p0 = space.TransformPoint(localPoints[Mathf.Max(currPt - 1, 0)]);
            Vector3 p1 = space.TransformPoint(localPoints[currPt]);
            Vector3 p2 = space.TransformPoint(localPoints[currPt + 1]);
            Vector3 p3 = space.TransformPoint(localPoints[Mathf.Min(currPt + 2, localPoints.Count - 1)]);

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * u +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u +
                (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u
            );
        }
        #endregion
    }
}
