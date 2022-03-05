using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public static class PosenetUtils
    {
        /// <summary>
        /// Body-points structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BodyPoints
        {
            public Vector2 position;
            public float score;

            public Keypoint[] keypoints;
        }

        // the number of detected keypoints
        public const int KeypointCount = 17;

        public enum KeypointID
        {
            Nose,                           // 0    
            LeftEye, RightEye,              // 1, 2
            LeftEar, RightEar,              // 3, 4
            LeftShoulder, RightShoulder,    // 5, 6
            LeftElbow, RightElbow,          // 7, 8
            LeftWrist, RightWrist,          // 9, 10
            LeftHip, RightHip,              // 11, 12
            LeftKnee, RightKnee,            // 13, 14
            LeftAnkle, RightAnkle           // 15, 16
        }


        // the size of the Keypoint-struct in bytes
        public const int KeypointStructSize = 40;

        /// <summary>
        /// Keypoint structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Keypoint
        {
            public int id;
            public Vector2 position;
            public Vector2Int posIndex;
            public float score;
            //public uint padding;

            public Vector2 posSrc;
            public Vector2 posTgt;

            public Keypoint(int id, float score, Vector2 position, Vector2Int posIndex)
            {
                this.id = id;
                this.score = score;
                this.position = position;
                this.posIndex = posIndex;

                this.posSrc = Vector2.zero;
                this.posTgt = Vector2.zero;
            }
        }


        // Maximum number of detected bodies.
        public const int MaxBodies = 20;

        // The pairs of key points that should be connected on a body
        public static readonly System.Tuple<int, int>[] displayBones = new System.Tuple<int, int>[]
        {
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.LeftEye),
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.RightEye),
            System.Tuple.Create((int)KeypointID.LeftEye, (int)KeypointID.LeftEar),
            System.Tuple.Create((int)KeypointID.RightEye, (int)KeypointID.RightEar),
            System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.RightShoulder),
            System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.LeftHip),
            System.Tuple.Create((int)KeypointID.RightShoulder, (int)KeypointID.RightHip),
            //System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.RightHip),
            //System.Tuple.Create((int)KeypointID.RightShoulder, (int)KeypointID.LeftHip),
            System.Tuple.Create((int)KeypointID.LeftHip, (int)KeypointID.RightHip),
            System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.LeftElbow),
            System.Tuple.Create((int)KeypointID.LeftElbow, (int)KeypointID.LeftWrist),
            System.Tuple.Create((int)KeypointID.RightShoulder, (int)KeypointID.RightElbow),
            System.Tuple.Create((int)KeypointID.RightElbow, (int)KeypointID.RightWrist),
            System.Tuple.Create((int)KeypointID.LeftHip, (int)KeypointID.LeftKnee),
            System.Tuple.Create((int)KeypointID.LeftKnee, (int)KeypointID.LeftAnkle),
            System.Tuple.Create((int)KeypointID.RightHip, (int)KeypointID.RightKnee),
            System.Tuple.Create((int)KeypointID.RightKnee, (int)KeypointID.RightAnkle)
        };

        // parent->child relations, used in multi-pose detection.
        private static readonly System.Tuple<int, int>[] boneTree = new System.Tuple<int, int>[] {
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.LeftEye),             // 0
            System.Tuple.Create((int)KeypointID.LeftEye, (int)KeypointID.LeftEar),          // 1
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.RightEye),            // 2
            System.Tuple.Create((int)KeypointID.RightEye, (int)KeypointID.RightEar),        // 3
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.LeftShoulder),        // 4
            System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.LeftElbow),   // 5
            System.Tuple.Create((int)KeypointID.LeftElbow, (int)KeypointID.LeftWrist),      // 6
            System.Tuple.Create((int)KeypointID.LeftShoulder, (int)KeypointID.LeftHip),     // 7
            System.Tuple.Create((int)KeypointID.LeftHip, (int)KeypointID.LeftKnee),         // 8
            System.Tuple.Create((int)KeypointID.LeftKnee, (int)KeypointID.LeftAnkle),       // 9
            System.Tuple.Create((int)KeypointID.Nose, (int)KeypointID.RightShoulder),       // 10
            System.Tuple.Create((int)KeypointID.RightShoulder, (int)KeypointID.RightElbow), // 11
            System.Tuple.Create((int)KeypointID.RightElbow, (int)KeypointID.RightWrist),    // 12
            System.Tuple.Create((int)KeypointID.RightShoulder, (int)KeypointID.RightHip),   // 13
            System.Tuple.Create((int)KeypointID.RightHip, (int)KeypointID.RightKnee),       // 14
            System.Tuple.Create((int)KeypointID.RightKnee, (int)KeypointID.RightAnkle)      // 15
        };


        // creates a BodyPoints struct out of the provided keypoints
        public static BodyPoints GetBodyPoints(Keypoint[] keypoints, float scoreThreshold = 0.5f)
        {
            BodyPoints bp = new BodyPoints();
            bp.keypoints = new Keypoint[KeypointCount];

            for(int k = 0; k < keypoints.Length; k++)
            {
                bp.keypoints[k] = keypoints[k];

                if(k == 0)
                {
                    bp.position = keypoints[k].position;
                    bp.score = keypoints[k].score;
                }
            }

            return bp;
        }


        // the size of the local window in the heatmap to look for confidence scores higher than the one at the current heatmap coordinate
        private const int kLocalMaximumRadius = 1;


        // detects multiple bodies and finds their parts from part scores and displacement vectors. 
        public static Keypoint[][] DecodeMultiplePoses(Tensor heatmaps, Tensor offsets, Tensor displacementsFwd, Tensor displacementBwd, out List<Keypoint> alAllKeypoints,
            int stride, int maxPoseDetections, float scoreThreshold = 0.5f, int nmsRadius = 20)
        {
            List<Keypoint[]> poses = new List<Keypoint[]>();
            float squaredNmsRadius = (float)nmsRadius * nmsRadius;
            //Debug.Log(string.Format("Heatmaps: {0}, offsets: {1}, dispFwd: {2}, dispBwd: {3}", heatmaps.shape, offsets.shape, displacementsFwd.shape, displacementBwd.shape));

            // get the list of keypoints
            List<Keypoint> listAllKeypoints = BuildPartList(scoreThreshold, kLocalMaximumRadius, stride, heatmaps, offsets);
            listAllKeypoints = listAllKeypoints.OrderByDescending(x => x.score).ToList();

            alAllKeypoints = new List<Keypoint>();
            //alAllKeypoints.AddRange(listAllKeypoints);

            // keypoints by type
            List<Keypoint>[] alKeypointsByType = new List<Keypoint>[KeypointCount];
            foreach(Keypoint kp in listAllKeypoints)
            {
                int kpId = kp.id;
                if (alKeypointsByType[kpId] == null)
                    alKeypointsByType[kpId] = new List<Keypoint>();
                alKeypointsByType[kpId].Add(kp);
            }

            // decode poses until the max number of poses has been reach or the keypoint list is empty
            while (poses.Count < maxPoseDetections && listAllKeypoints.Count > 0)
            {
                Keypoint rootPart = listAllKeypoints[0];
                listAllKeypoints.RemoveAt(0);

                //Vector2 rootImageCoords = GetImageCoords(rootPart, stride, offsets);
                if (IsWithinNmsRadius(poses, squaredNmsRadius, rootPart.position, rootPart.id))
                    continue;

                Keypoint[] keypoints = DecodePose(rootPart, heatmaps, offsets, stride, displacementsFwd, displacementBwd, listAllKeypoints, ref alKeypointsByType);
                poses.Add(keypoints);
            }

            return poses.ToArray();
        }

        // creates a list of keypoints with the highest values within the provided radius.
        private static List<Keypoint> BuildPartList(float scoreThreshold, int localMaximumRadius, int stride, Tensor heatmaps, Tensor offsets)
        {
            List<Keypoint> list = new List<Keypoint>();

            for (int c = 0; c < heatmaps.channels; c++)
            {
                for (int y = 0; y < heatmaps.height; y++)
                {
                    for (int x = 0; x < heatmaps.width; x++)
                    {
                        float score = heatmaps[0, y, x, c];
                        if (score < scoreThreshold)
                            continue;

                        if (IsScoreMaxInLocalWindow(c, score, y, x, localMaximumRadius, heatmaps))
                        {
                            Vector2Int posIndex = new Vector2Int(x, y);
                            Vector2 position = GetImageCoords(c, posIndex, stride, offsets);

                            Keypoint part = new Keypoint(c, score, position, posIndex);
                            list.Add(part);
                        }
                    }
                }
            }

            return list;
        }

        // compares the value at the current heatmap location to the surrounding values
        private static bool IsScoreMaxInLocalWindow(int keypointId, float score, int heatmapY, int heatmapX, int localMaxRadius, Tensor heatmaps)
        {
            bool localMaximum = true;

            // y-range
            int yStart = Mathf.Max(heatmapY - localMaxRadius, 0);
            int yEnd = Mathf.Min(heatmapY + localMaxRadius + 1, heatmaps.height);

            // x-range
            int xStart = Mathf.Max(heatmapX - localMaxRadius, 0);
            int xEnd = Mathf.Min(heatmapX + localMaxRadius + 1, heatmaps.width);

            for (int yCurrent = yStart; yCurrent < yEnd; ++yCurrent)
            {
                for (int xCurrent = xStart; xCurrent < xEnd; ++xCurrent)
                {
                    if (heatmaps[0, yCurrent, xCurrent, keypointId] > score)
                    {
                        localMaximum = false;
                        break;
                    }
                }

                if (!localMaximum)
                    break;
            }

            return localMaximum;
        }

        // checks if the provided image coordinates are too close to any keypoints in existing poses.
        private static bool IsWithinNmsRadius(List<Keypoint[]> poses, float squaredNmsRadius, Vector2 vec, int keypointId)
        {
            return poses.Any(pose => (vec - pose[keypointId].position).sqrMagnitude <= squaredNmsRadius);
        }

        // follows the displacement fields to decode the full pose of the object instance given the position of a part that acts as root.
        private static Keypoint[] DecodePose(Keypoint root, Tensor scores, Tensor offsets, int stride, Tensor displacementsFwd, Tensor displacementsBwd,
            List<Keypoint> listAllKeypoints, ref List<Keypoint>[] alKeypointsByType)
        {
            Keypoint[] instanceKeypoints = new Keypoint[scores.channels];

            //Vector2 rootPoint = GetImageCoords(root, stride, offsets);
            instanceKeypoints[root.id] = root;  // new Keypoint(root.score, rootPoint, root.id);
            int numEdges = boneTree.Length;

            // decode the part positions upwards in the tree, following the backward displacements
            for (int edge = numEdges - 1; edge >= 0; --edge)
            {
                int sourceKeypointId = boneTree[edge].Item2;
                int targetKeypointId = boneTree[edge].Item1;

                if (instanceKeypoints[sourceKeypointId].score > 0.0f && instanceKeypoints[targetKeypointId].score == 0.0f)
                {
                    instanceKeypoints[targetKeypointId] = TraverseToTargetKeypoint(edge, instanceKeypoints[sourceKeypointId], targetKeypointId, 
                        scores, offsets, stride, displacementsBwd, listAllKeypoints, ref alKeypointsByType);
                }
            }

            // decode the part positions downwards in the tree, following the forward displacements
            for (int edge = 0; edge < numEdges; ++edge)
            {
                int sourceKeypointId = boneTree[edge].Item1;
                int targetKeypointId = boneTree[edge].Item2;

                if (instanceKeypoints[sourceKeypointId].score > 0.0f && instanceKeypoints[targetKeypointId].score == 0.0f)
                {
                    instanceKeypoints[targetKeypointId] = TraverseToTargetKeypoint(edge, instanceKeypoints[sourceKeypointId], targetKeypointId,
                        scores, offsets, stride, displacementsFwd, listAllKeypoints, ref alKeypointsByType);
                }
            }

            return instanceKeypoints;
        }

        // gets the next keypoint along the provided edgeId for the pose instance.
        private static Keypoint TraverseToTargetKeypoint(int edgeId, Keypoint sourceKeypoint, int targetKeypointId,
            Tensor scores, Tensor offsets, int stride, Tensor displacements, List<Keypoint> listAllKeypoints, ref List<Keypoint>[] alKeypointsByType, 
            int offsetRefineStep = 1)
        {
            int height = scores.height;
            int width = scores.width;

            Vector2Int sourceKeypointIndices = sourceKeypoint.posIndex;  // GetStridedIndexNearPoint(sourceKeypoint.position, stride, height, width);

            Vector2 displacement = GetDisplacement(edgeId, sourceKeypointIndices, displacements);
            Vector2 targetKeypointPos = sourceKeypoint.position + displacement;
            Vector2Int targetKeypointIndices = Vector2Int.zero;

            for (int i = 0; i < offsetRefineStep; i++)
            {
                targetKeypointIndices = GetStridedIndexNearPoint(targetKeypointPos, stride, height, width);
                Vector2 offsetVector = GetOffsetVector(targetKeypointIndices.y, targetKeypointIndices.x, targetKeypointId, offsets);
                targetKeypointPos = (targetKeypointIndices * stride) + offsetVector;
            }

            int minKpId = -1;
            float minSqrDist = float.MaxValue;
            int tkpCount = alKeypointsByType[targetKeypointId] != null ? alKeypointsByType[targetKeypointId].Count : 0;

            for (int i = 0; i < tkpCount; i++)
            {
                float sqrDist = (targetKeypointPos - alKeypointsByType[targetKeypointId][i].position).sqrMagnitude;
                if(minSqrDist > sqrDist)
                {
                    minKpId = i;
                    minSqrDist = sqrDist;
                }
            }

            if(minKpId >= 0 && minSqrDist <= 100f)  // max-dist = 50
            {
                Keypoint targetKeypoint = alKeypointsByType[targetKeypointId][minKpId];
                alKeypointsByType[targetKeypointId].Remove(targetKeypoint);
                listAllKeypoints.Remove(targetKeypoint);

                //targetKeypoint.posSrc = sourceKeypoint.position;
                //targetKeypoint.posTgt = targetKeypointPos;

                return targetKeypoint;
            }

            //Vector2Int targetKeyPointIndices = GetStridedIndexNearPoint(targetKeypointPos, stride, height, width);
            //float score = scores[0, targetKeyPointIndices.y, targetKeyPointIndices.x, targetKeypointId];

            //return new Keypoint(score, targetKeypointPos, targetKeypointId);

            targetKeypointIndices = GetStridedIndexNearPoint(targetKeypointPos, stride, height, width);
            return new Keypoint(targetKeypointId, 0f, targetKeypointPos, targetKeypointIndices);
        }

        // retrieves the displacement values for the provided point.
        private static Vector2 GetDisplacement(int edgeId, Vector2Int point, Tensor displacements)
        {
            int numEdges = displacements.channels >> 1;
            return new Vector2(displacements[0, point.y, point.x, numEdges + edgeId], displacements[0, point.y, point.x, edgeId]);
        }

        // calculates the heatmap indices closest to the provided point.
        private static Vector2Int GetStridedIndexNearPoint(Vector2 point, int stride, int height, int width)
        {
            return new Vector2Int((int)Mathf.Clamp(Mathf.Round(point.x / stride), 0, width - 1), (int)Mathf.Clamp(Mathf.Round(point.y / stride), 0, height - 1));
        }

        //// calculates the position of the provided keypoint in the input image.
        //private static Vector2 GetImageCoords(Keypoint part, int stride, Tensor offsets)
        //{
        //    Vector2 offsetVector = GetOffsetVector((int)part.posIndex.y, (int)part.posIndex.x, part.id, offsets);
        //    return (part.posIndex * stride) + offsetVector;
        //}

        // calculates the position of the provided keypoint in the input image.
        private static Vector2 GetImageCoords(int id, Vector2Int posIndex, int stride, Tensor offsets)
        {
            Vector2 offsetVector = GetOffsetVector(posIndex.y, posIndex.x, id, offsets);
            return new Vector2(posIndex.x * stride + offsetVector.x, posIndex.y * stride + offsetVector.y);
        }

        // gets the offset values for the provided heatmap indices.
        public static Vector2 GetOffsetVector(int y, int x, int keypoint, Tensor offsets)
        {
            int xCoordOfs = offsets.channels >> 1;
            return new Vector2(offsets[0, y, x, keypoint + xCoordOfs], offsets[0, y, x, keypoint]);
        }

    }
}
