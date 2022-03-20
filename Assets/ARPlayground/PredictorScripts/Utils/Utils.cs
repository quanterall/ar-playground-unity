using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public static class Utils
    {
        public static void Destroy(Object o)
        {
            if (o == null)
                return;

            if(o is RenderTexture)
            {
                ((RenderTexture)o).Release();
            }

            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }


        // returns the text contained in the given resource asset, or null if not found
        public static string GetResourceText(string resFileName)
        {
            TextAsset textRes = Resources.Load(resFileName, typeof(TextAsset)) as TextAsset;
            if (textRes == null)
            {
                Debug.LogWarning("Resource not found: " + resFileName);
                return null;
            }

            return textRes.text;
        }

        // returns the text contained in the given resource asset, or null if not found
        public static string[] GetResourceStrings(string resFileName)
        {
            var resText = GetResourceText(resFileName);
            if (string.IsNullOrEmpty(resText))
                return null;

            string[] resStrings = resText.Split("\n".ToCharArray());

            for(int i = resStrings.Length - 1; i >= 0; i--)
            {
                resStrings[i] = resStrings[i].Trim();
            }

            return resStrings;
        }


        // draws point with the given size and color
        public static void DrawPoint(int x, int y, float size, Color color)
        {
            Vector3 vPoint = new Vector3(x, y, 0);
            DrawPoint(vPoint, size, color);
        }

        // draws point with the given size and color
        public static void DrawPoint(Vector3 vPoint, float quadSize, Color color)
        {
            if (!matRender)
            {
                SetRenderMat();
            }

            GL.PushMatrix();
            matRender.SetPass(0);

            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(color);

            _DrawPoint(vPoint, quadSize);

            GL.End();
            GL.PopMatrix();
        }

        // draws list of points with the given size and color
        public static void DrawPoints(List<Vector3> alPoints, float quadSize, Color color)
        {
            if (alPoints == null)
                return;

            if (!matRender)
            {
                SetRenderMat();
            }

            GL.PushMatrix();
            matRender.SetPass(0);

            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(color);

            foreach (Vector3 v in alPoints)
            {
                _DrawPoint(v, quadSize);
            }

            GL.End();
            GL.PopMatrix();
        }

        // draws point with given size
        private static void _DrawPoint(Vector3 v, float quadSize)
        {
            float q2 = quadSize / 2f;
            GL.Vertex3(v.x - q2, v.y - q2, 0f);
            GL.Vertex3(v.x - q2, v.y + q2, 0f);
            GL.Vertex3(v.x + q2, v.y + q2, 0f);
            GL.Vertex3(v.x + q2, v.y - q2, 0f);
        }

        // draws a line with the given width and color
        public static void DrawLine(int x0, int y0, int x1, int y1, float width, Color color)
        {
            Vector3 v0 = new Vector3(x0, y0, 0);
            Vector3 v1 = new Vector3(x1, y1, 0);
            DrawLine(v0, v1, width, color);
        }

        // draws a line with the given width and color
        public static void DrawLine(Vector3 v0, Vector3 v1, float lineWidth, Color color)
        {
            if (!matRender)
            {
                SetRenderMat();
            }

            GL.PushMatrix();
            matRender.SetPass(0);

            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(color);

            _DrawLine(v0, v1, lineWidth);

            GL.End();
            GL.PopMatrix();
        }

        // draws list of lines with the given width and color
        public static void DrawLines(List<Vector3> alLinePoints, float lineWidth, Color color)
        {
            if (alLinePoints == null)
                return;

            if (!matRender)
            {
                SetRenderMat();
            }

            GL.PushMatrix();
            matRender.SetPass(0);

            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(color);

            for (int i = 0; i < alLinePoints.Count; i += 2)
            {
                Vector3 v0 = alLinePoints[i];
                Vector3 v1 = alLinePoints[i + 1];

                _DrawLine(v0, v1, lineWidth);
            }

            GL.End();
            GL.PopMatrix();
        }

        // draws rectangle with the given width and color
        public static void DrawRect(Rect rect, float width, Color color)
        {
            Vector3 topLeft = new Vector3(rect.xMin, rect.yMin, 0);
            Vector3 bottomRight = new Vector3(rect.xMax, rect.yMax, 0);
            DrawRect(topLeft, bottomRight, width, color);
        }

        // draws rectangle with the given width and color
        public static void DrawRect(Vector3 topLeft, Vector3 bottomRight, float lineWidth, Color color)
        {
            if (!matRender)
            {
                SetRenderMat();
            }

            GL.PushMatrix();
            matRender.SetPass(0);

            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(color);

            // top
            Vector3 v0 = topLeft;
            Vector3 v1 = topLeft; v1.x = bottomRight.x;
            _DrawLine(v0, v1, lineWidth);

            // right
            v0 = v1;
            v1 = bottomRight;
            _DrawLine(v0, v1, lineWidth);

            // bottom
            v0 = v1;
            v1 = topLeft; v1.y = bottomRight.y;
            _DrawLine(v0, v1, lineWidth);

            // left
            v0 = v1;
            v1 = topLeft;
            _DrawLine(v0, v1, lineWidth);

            GL.End();
            GL.PopMatrix();
        }

        // draws line from v0 to v1 with the given width
        private static void _DrawLine(Vector3 v0, Vector3 v1, float lineWidth)
        {
            Vector3 n = ((new Vector3(v1.y, v0.x, 0f)) - (new Vector3(v0.y, v1.x, 0f))).normalized * lineWidth;
            GL.Vertex3(v0.x - n.x, v0.y - n.y, 0f);
            GL.Vertex3(v0.x + n.x, v0.y + n.y, 0f);
            GL.Vertex3(v1.x + n.x, v1.y + n.y, 0f);
            GL.Vertex3(v1.x - n.x, v1.y - n.y, 0f);
        }

        // current render material
        private static Material matRender = null;

        // sets up the render material, if needed
        private static void SetRenderMat()
        {
            if (!matRender)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                matRender = new Material(shader);

                matRender.hideFlags = HideFlags.HideAndDontSave;
                matRender.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                matRender.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                matRender.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                matRender.SetInt("_ZWrite", 0);
            }
        }


        // creates a new float1 render texture
        public static RenderTexture CreateFloat1RT(int w, int h)
          => new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);

        // creates a new float2 render texture
        public static RenderTexture CreateFloat2RT(int w, int h)
          => new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat);

        // creates a new float4 render texture
        public static RenderTexture CreateFloat4RT(int w, int h)
          => new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);

        public static RenderTexture CreateArgbUavRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.Create();

            return rt;
        }

        public static RenderTexture CreateRFloatUavRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);
            rt.enableRandomWrite = true;
            rt.Create();

            return rt;
        }

        public static RenderTexture CreateR8UavRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.R8);
            rt.enableRandomWrite = true;
            rt.Create();

            return rt;
        }

        // return the number of channels of the given render texture
        public static int CountChannels(RenderTexture rt)
          => rt.format == RenderTextureFormat.RFloat ? 1 : rt.format == RenderTextureFormat.RGFloat ? 2 : 4;


        // colors to paint object, according to their detection indices
        private static readonly Color[] _colorsByIndex = { Color.green, Color.yellow, Color.red, Color.green, Color.blue, Color.magenta, Color.cyan, Color.gray };

        // returns the color for the given detection index
        public static Color GetColorByIndex(int i)
        {
            return _colorsByIndex[i % _colorsByIndex.Length];
        }

    }


    // IWorker extension methods
    static class IWorkerExtensions
    {
        public static IEnumerator ExecuteInTime(this IWorker worker, long timeThresholdTicks = 333333)  // 10 000 000 ticks per second
        {
            var workerEnum = worker.StartManualSchedule();
            long timePrev = System.DateTime.Now.Ticks;

            while (workerEnum.MoveNext())
            {
                long timeNow = System.DateTime.Now.Ticks;

                if((timeNow - timePrev) >= timeThresholdTicks)
                {
                    timePrev = timeNow;
                    yield return null;
                }
            }
        }

        // Gets an output tensor from the worker and returns it as a temporary render texture.
        // The caller must release it using RenderTexture.ReleaseTemporary.
        public static RenderTexture CopyOutputToTempRT(this IWorker worker, string name, int w, int h)
        {
            var fmt = RenderTextureFormat.RFloat;
            var shape = new TensorShape(1, h, w, 1);
            var rt = RenderTexture.GetTemporary(w, h, 0, fmt);

            using (var tensor = worker.PeekOutput(name).Reshape(shape))
                tensor.ToRenderTexture(rt);

            return rt;
        }

        // Gets an output tensor from the worker and copies it to the provided render texture.
        public static void CopyOutput(this IWorker worker, string tensorName, RenderTexture rt)
        {
            var output = worker.PeekOutput(tensorName);
            var channels = Utils.CountChannels(rt);
            var shape = new TensorShape(1, rt.height, rt.width, channels);

            using (var tensor = output.Reshape(shape))
                tensor.ToRenderTexture(rt);
        }
    }


    // ComputeShader extension methods
    static class ComputeShaderExtensions
    {
        public static void SetDimensions(this ComputeShader compute, string name, Texture texture)
          => compute.SetInts(name, texture.width, texture.height);

        public static void DispatchThreads(this ComputeShader compute, int kernel, int x, int y, int z)
        {
            uint xc, yc, zc;
            compute.GetKernelThreadGroupSizes(kernel, out xc, out yc, out zc);

            x = (x + (int)xc - 1) / (int)xc;
            y = (y + (int)yc - 1) / (int)yc;
            z = (z + (int)zc - 1) / (int)zc;

            compute.Dispatch(kernel, x, y, z);
        }

        public static void DispatchThreadPerPixel(this ComputeShader compute, int kernel, Texture texture)
          => compute.DispatchThreads(kernel, texture.width, texture.height, 1);
    }

}
