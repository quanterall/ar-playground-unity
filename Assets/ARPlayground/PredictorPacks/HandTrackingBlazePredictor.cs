using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Rendering;

namespace com.quanterall.arplayground
{
    public class HandTrackingBlazePredictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Range(0, 1)]
        public float threshold = 0.75f;

        /// <summary>
        /// Event, invoked when face gets detected (time, count, index, className, normRect, score)
        /// </summary>
        public event System.Action<long, int, int, Rect, float> OnHandDetected;


        // compute shaders
        private ComputeShader preprocessShader = null;
        private ComputeShader postprocess1Shader = null;
        private ComputeShader postprocess2Shader = null;

        // the input texture
        private RenderTexture _texture;

        // face tracking detections
        private Detection[] _detections = null;
        private int _detCount = 0;


        /// <summary>
        /// Detection structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Detection
        {
            // Bounding box
            public Vector2 center;
            public Vector2 extent;

            // Key points
            public Vector2 wrist;
            public Vector2 index;
            public Vector2 middle;
            public Vector2 ring;
            public Vector2 pinky;
            public Vector2 thumb;

            // Confidence score [0, 1]
            public float score;

            // Padding
            public float pad1, pad2, pad3;

            // sizeof(Detection)
            public const int Size = 20 * sizeof(float);
        };

        // Maximum number of detections.
        public const int MaxDetection = 64;


        // buffers and workers
        private ComputeBuffer _preBuffer;
        private ComputeBuffer _post1Buffer;
        private ComputeBuffer _post2Buffer;
        private ComputeBuffer _countBuffer;

        private IWorker _worker = null;
        private int _size = 0;

        private Vector2 _scale = Vector2.one;
        private Vector2 _offset = Vector2.zero;

        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Hand detecion (blaze)";
        }

        /// <summary>
        /// Initializes the predictor's model and worker.
        /// </summary>
        /// <returns></returns>
        public override bool InitPredictor()
        {
            if(_worker == null)
            {
                base.InitPredictor();

                var nnModel = ModelLoader.Load(model);
                _size = nnModel.inputs[0].shape[6]; // Input tensor width

                // load compute shaders
                preprocessShader = Resources.Load("BlazePalmPreprocessShader") as ComputeShader;
                postprocess1Shader = Resources.Load("BlazePalmPostprocess1Shader") as ComputeShader;
                postprocess2Shader = Resources.Load("BlazePalmPostprocess2Shader") as ComputeShader;

                _preBuffer = new ComputeBuffer(_size * _size * 3, sizeof(float));
                _post1Buffer = new ComputeBuffer(MaxDetection, Detection.Size, ComputeBufferType.Append);
                _post2Buffer = new ComputeBuffer(MaxDetection, Detection.Size, ComputeBufferType.Append);
                _countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker(workerType);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Releases the resources used by the predictor.
        /// </summary>
        public override void FinishPredictor()
        {
            if(_worker != null)
            {
                base.FinishPredictor();

                _preBuffer?.Dispose();
                _preBuffer = null;

                _post1Buffer?.Dispose();
                _post1Buffer = null;

                _post2Buffer?.Dispose();
                _post2Buffer = null;

                _countBuffer?.Dispose();
                _countBuffer = null;

                _worker?.Dispose();
                _worker = null;

                if (_texture)
                    Utils.Destroy(_texture);
                _texture = null;
            }
        }

        /// <summary>
        /// Starts predictor's inference on the given image.
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public override bool StartInference(Texture texture, long cameraFrameTime)
        {
            base.StartInference(texture, cameraFrameTime);
            StartCoroutine(RunModelRoutine(texture, threshold));

            return true;
        }

        /// <summary>
        /// Completes the last started inference.
        /// </summary>
        public override bool CompleteInference()
        {
            //GetResults();
            //return base.CompleteInference();
            return true;
        }

        /// <summary>
        /// Tries to get the last inference results in the main thread.
        /// </summary>
        /// <returns></returns>
        public override bool TryGetResults(PlaygroundController controller)
        {
            // get detections
            _detections = GetDetections(out _detCount);

            // invoke the event
            if (OnHandDetected != null)
            {
                for(int index = 0; index < _detCount; index++)
                {
                    Detection det = _detections[index];

                    Rect rect = new Rect(det.center.x - det.extent.x / 2f, det.center.y - det.extent.y / 2f, det.extent.x, det.extent.y);
                    OnHandDetected(inferenceFrameTime, _detCount, index, rect, det.score);
                }
            }

            return true;
        }

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        public override void DisplayInferenceResults(PlaygroundController controller)
        {
            if (_detections == null)
                return;

            for (int i = 0; i < _detCount; i++)
            {
                var det = _detections[i];
                Color clr = Utils.GetColorByIndex(i);

                Rect imageRect = controller.GetImageRect();  // 1f = _size x _size
                controller.DrawRect(det.center.x - det.extent.x / 2f, det.center.y - det.extent.y / 2f, det.extent.x, det.extent.y, 1f, clr, imageRect);

                controller.DrawPoint(det.wrist.x, det.wrist.y, 10f, clr, imageRect);
                controller.DrawPoint(det.index.x, det.index.y, 10f, clr, imageRect);
                controller.DrawPoint(det.middle.x, det.middle.y, 10f, clr, imageRect);
                controller.DrawPoint(det.ring.x, det.ring.y, 10f, clr, imageRect);
                controller.DrawPoint(det.pinky.x, det.pinky.y, 10f, clr, imageRect);
                controller.DrawPoint(det.thumb.x, det.thumb.y, 10f, clr, imageRect);

                //Debug.Log(string.Format("D{0} - c:{1}, e:{2}", i, det.center, det.extent));
            }
        }


        private IEnumerator RunModelRoutine(Texture source, float threshold)
        {
            // create or recreate the texture
            //int minSize = _size;  // source.width < source.height ? source.width : source.height;
            if (_texture == null || _texture.width != source.width || _texture.height != source.height)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(source.width, source.height, 0);
                //Debug.Log("Creating texture " + source.width + " x " + source.height);

                // Letterboxing scale factor
                _scale = new Vector2(Mathf.Max((float)_texture.height / _texture.width, 1),
                   Mathf.Max(1, (float)_texture.width / _texture.height));
                _offset = (_scale - Vector2.one) / 2f;
                Debug.Log("Scale: " + _scale + ", offset: " + _offset);
            }

            //// get the texture to process
            //var scale = GetBlitScale(source, _texture);
            //var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture);  // , scale, offset);
            //Debug.Log(string.Format("scale: {0:F3}, offset: {1:F3}", scale, offset));

            // Reset the compute buffer counters.
            _post1Buffer.SetCounterValue(0);
            _post2Buffer.SetCounterValue(0);

            // Preprocessing
            var pre = preprocessShader;
            if(pre != null)
            {
                pre.SetInt("_ImageWidth", _size);
                pre.SetVector("_ImageScale", _scale);
                pre.SetTexture(0, "_Texture", _texture);
                pre.SetBuffer(0, "_Tensor", _preBuffer);
                pre.Dispatch(0, _size / 8, _size / 8, 1);
            }

            // Run the BlazeFace model.
            //using (var tensor = new Tensor(1, _size, _size, 3, _preBuffer))
            //    _worker.Execute(tensor);

            yield return _worker.ExecuteAndWaitForResult(this, _preBuffer, _size, _size);

            //// get results in coroutine
            //StartCoroutine(GetResultsRoutine());

            // Output tensors -> Temporary render textures
            var scoresRT = _worker.CopyOutputToTempRT("classificators", 1, 896);
            var boxesRT = _worker.CopyOutputToTempRT("regressors", 18, 896);

            //yield return null;

            // 1st postprocess (bounding box aggregation)
            var post1 = postprocess1Shader;
            if (post1 != null)
            {
                post1.SetFloat("_ImageSize", _size);
                post1.SetFloat("_Threshold", threshold);

                post1.SetTexture(0, "_Scores", scoresRT);
                post1.SetTexture(0, "_Boxes", boxesRT);
                post1.SetBuffer(0, "_Output", _post1Buffer);
                post1.Dispatch(0, 1, 1, 1);

                post1.SetTexture(1, "_Scores", scoresRT);
                post1.SetTexture(1, "_Boxes", boxesRT);
                post1.SetBuffer(1, "_Output", _post1Buffer);
                post1.Dispatch(1, 1, 1, 1);
            }

            // Release the temporary render textures.
            RenderTexture.ReleaseTemporary(scoresRT);
            RenderTexture.ReleaseTemporary(boxesRT);

            //yield return null;

            // Retrieve the bounding box count.
            ComputeBuffer.CopyCount(_post1Buffer, _countBuffer, 0);

            // 2nd postprocess (overlap removal)
            var post2 = postprocess2Shader;
            if (post2 != null)
            {
                post2.SetBuffer(0, "_Input", _post1Buffer);
                post2.SetBuffer(0, "_Count", _countBuffer);
                post2.SetBuffer(0, "_Output", _post2Buffer);
                post2.Dispatch(0, 1, 1, 1);
            }

            // Retrieve the bounding box count after removal.
            ComputeBuffer.CopyCount(_post2Buffer, _countBuffer, 0);

            // get the face detections
            //_post2ReadCache = null;
            //_detections = GetDetections();

            AsyncGPUReadback.Request(_countBuffer, sizeof(int), 0, ReadCountCompleteAction);
            AsyncGPUReadback.Request(_post2Buffer, Detection.Size * MaxDetection, 0, ReadCacheCompleteAction);

            // complete the inference (set ready state)
            base.CompleteInference();
        }

        private System.Action<AsyncGPUReadbackRequest> ReadCountCompleteAction => OnReadCountComplete;
        private void OnReadCountComplete(AsyncGPUReadbackRequest req)
          => req.GetData<int>().CopyTo(_countReadCache);

        private System.Action<AsyncGPUReadbackRequest> ReadCacheCompleteAction => OnReadCacheComplete;
        private void OnReadCacheComplete(AsyncGPUReadbackRequest req)
        {
            req.GetData<Detection>().CopyTo(_post2ReadCache);

            for (int i = 0; i < MaxDetection; i++)
            {
                Detection det = _post2ReadCache[i];
                if (det.score < threshold)
                    continue;

                det.center = det.center * _scale - _offset;
                det.extent *= _scale;

                det.wrist = det.wrist * _scale - _offset;
                det.thumb = det.thumb * _scale - _offset;

                det.index = det.index * _scale - _offset;
                det.middle = det.middle * _scale - _offset;
                det.ring = det.ring * _scale - _offset;
                det.pinky = det.pinky * _scale - _offset;

                _post2ReadCache[i] = det;
            }
        }


        // detections read cache
        private int[] _countReadCache = new int[1];
        private Detection[] _post2ReadCache = new Detection[MaxDetection];

        // updates the detections cache and returns the detections.
        private Detection[] GetDetections(out int count)
        {
            //_countBuffer.GetData(_countReadCache, 0, 0, 1);
            count = _countReadCache[0];

            //_post2ReadCache = new Detection[count];
            //_post2Buffer.GetData(_post2ReadCache, 0, 0, count);

            return _post2ReadCache;
        }

    }
}
