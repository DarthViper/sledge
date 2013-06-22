﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sledge.DataStructures.Transformations;

namespace Sledge.DataStructures.Geometric
{
    /// <summary>
    /// Represents a coplanar, directed polygon with at least 3 vertices
    /// </summary>
    public class Polygon
    {
        public List<Coordinate> Vertices { get; set; }
        public Plane Plane { get; set; }

        /// <summary>
        /// Creates a polygon from a list of points
        /// </summary>
        /// <param name="vertices">The vertices of the polygon</param>
        public Polygon(IEnumerable<Coordinate> vertices)
        {
            Vertices = vertices.ToList();
            Plane = new Plane(Vertices[0], Vertices[1], Vertices[2]);
            Simplify();
        }

        /// <summary>
        /// Creates a polygon from a plane and a radius.
        /// Expands the plane to the radius size to create a large polygon with 4 vertices.
        /// </summary>
        /// <param name="plane">The polygon plane</param>
        /// <param name="radius">The polygon radius</param>
        public Polygon(Plane plane, decimal radius = 1000000m)
        {
            Plane = plane;

            // Get aligned up and right axes to the plane
            var direction = Plane.GetClosestAxisToNormal();
            var tempV = direction == Coordinate.UnitZ ? -Coordinate.UnitY : -Coordinate.UnitZ;
            var up = tempV.Cross(Plane.Normal).Normalise();
            var right = Plane.Normal.Cross(up).Normalise();

            Vertices = new List<Coordinate>
                           {
                               plane.PointOnPlane - right + up, // Top left
                               plane.PointOnPlane + right + up, // Top right
                               plane.PointOnPlane + right - up, // Bottom right
                               plane.PointOnPlane - right - up  // Bottom left
                           };
            Expand(radius);
        }

        public Polygon Clone()
        {
            return new Polygon(new List<Coordinate>(Vertices));
        }

        public void Unclone(Polygon polygon)
        {
            Vertices = new List<Coordinate>(polygon.Vertices);
            Plane = polygon.Plane.Clone();
        }

        /// <summary>
        /// Returns the origin of this polygon.
        /// </summary>
        /// <returns></returns>
        public Coordinate GetOrigin()
        {
            return Vertices.Aggregate(Coordinate.Zero, (x, y) => x + y) / Vertices.Count;
        }

        /// <summary>
        /// Get the lines representing the edges of this polygon.
        /// </summary>
        /// <returns>A list of lines</returns>
        public IEnumerable<Line> GetLines()
        {
            for (var i = 1; i < Vertices.Count; i++)
            {
                yield return new Line(Vertices[i - 1], Vertices[i]);
            }
        }

        /// <summary>
        /// Checks that all the points in this polygon are valid.
        /// </summary>
        /// <returns>True if the plane is valid</returns>
        public bool IsValid()
        {
            return Vertices.All(x => Plane.OnPlane(x) == 0);
        }

        /// <summary>
        /// Removes any colinear vertices in the polygon
        /// </summary>
        public void Simplify()
        {
            // Remove colinear vertices
            for (var i = 0; i < Vertices.Count - 2; i++)
            {
                var v1 = Vertices[i];
                var v2 = Vertices[i + 2];
                var p = Vertices[i + 1];
                var line = new Line(v1, v2);
                // If the midpoint is on the line, remove it
                if (line.ClosestPoint(p).EquivalentTo(p))
                {
                    Vertices.RemoveAt(i + 1);
                }
            }
        }

        /// <summary>
        /// Transforms all the points in the polygon.
        /// </summary>
        /// <param name="transform">The transformation to perform</param>
        public void Transform(IUnitTransformation transform)
        {
            Vertices = Vertices.Select(transform.Transform).ToList();
            Plane = new Plane(Vertices[0], Vertices[1], Vertices[2]);
        }

        /// <summary>
        /// Expands this plane's points outwards from the origin by a radius value.
        /// </summary>
        /// <param name="radius">The distance the points will be from the origin after expanding</param>
        public void Expand(decimal radius)
        {
            // 1. Center the polygon at the world origin
            // 2. Normalise all the vertices
            // 3. Multiply them by the radius
            // 4. Move the polygon back to the original origin
            var origin = GetOrigin();
            Vertices = Vertices.Select(x => (x - origin).Normalise() * radius + origin).ToList();
            Plane = new Plane(Vertices[0], Vertices[1], Vertices[2]);
        }

        /// <summary>
        /// Determines if this polygon is behind, in front, or spanning a plane.
        /// </summary>
        /// <param name="p">The plane to test against</param>
        /// <returns>A PlaneClassification value.</returns>
        public PlaneClassification ClassifyAgainstPlane(Plane p)
        {
            int front = 0, back = 0, onplane = 0, count = Vertices.Count;

            foreach (var test in Vertices.Select(p.OnPlane))
            {
                // Vertices on the plane are both in front and behind the plane in this context
                if (test <= 0) back++;
                if (test >= 0) front++;
                if (test == 0) onplane++;
            }

            if (onplane == count) return PlaneClassification.OnPlane;
            if (front == count) return PlaneClassification.Front;
            if (back == count) return PlaneClassification.Back;
            return PlaneClassification.Spanning;
        }

        /// <summary>
        /// Splits this polygon by a clipping plane, discarding the front side.
        /// The original polygon is modified to be the back side of the split.
        /// </summary>
        /// <param name="clip">The clipping plane</param>
        public void Split(Plane clip)
        {
            Polygon front, back;
            if (Split(clip, out back, out front))
            {
                Unclone(back);
            }
        }

        /// <summary>
        /// Splits this polygon by a clipping plane, returning the back and front planes.
        /// The original polygon is not modified.
        /// </summary>
        /// <param name="clip">The clipping plane</param>
        /// <param name="back">The back polygon</param>
        /// <param name="front">The front polygon</param>
        /// <returns>True if the split was successful</returns>
        public bool Split(Plane clip, out Polygon back, out Polygon front)
        {
            // If the polygon doesn't span the plane, return false.
            var classify = ClassifyAgainstPlane(clip);
            if (classify != PlaneClassification.Spanning)
            {
                back = front = null;
                if (classify == PlaneClassification.Back) back = this;
                else if (classify == PlaneClassification.Front) front = this;
                return false;
            }

            // Get the new front and back vertices
            var backVerts = new List<Coordinate>();
            var frontVerts = new List<Coordinate>();
            var prev = 0;

            for (var i = 0; i <= Vertices.Count; i++)
            {
                var end = Vertices[i % Vertices.Count];
                var cls = clip.OnPlane(end);

                // Check plane crossing
                if (i > 0 && cls != 0 && prev != cls)
                {
                    // This line end point has crossed the plane
                    // Add the line intersect to the 
                    var start = Vertices[i - 1];
                    var line = new Line(start, end);
                    var isect = clip.GetIntersectionPoint(line, true);
                    if (isect == null) throw new Exception("Expected intersection, got null.");
                    frontVerts.Add(isect);
                    backVerts.Add(isect);
                }

                // Add original points
                if (i < Vertices.Count)
                {
                    // OnPlane points get put in both polygons, doesn't generate split
                    if (cls >= 0) frontVerts.Add(end);
                    if (cls <= 0) backVerts.Add(end);
                }

                prev = cls;
            }

            back = new Polygon(backVerts);
            front = new Polygon(frontVerts);

            return true;
        }
    }
}
