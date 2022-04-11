using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class FaceDetectionBlazePredictor : BasePredictor
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
        public event System.Action<long, int, int, Rect, float> OnFaceDetected;


        // compute shaders
        private ComputeShader preprocessShader = null;
        private ComputeShader postprocess1Shader = null;
        private ComputeShader postprocess2Shader = null;

        // the input texture
        private RenderTexture _texture;

        // face tracking detections
        private Detection[] _detections = null;


        /// <summary>
        /// Detection structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Detection
        {
            // Bounding box
            public readonly Vector2 center;
            public readonly Vector2 extent;

            // Key points
            public readonly Vector2 leftEye;
            public readonly Vector2 rightEye;
            public readonly Vector2 nose;
            public readonly Vector2 mouth;
            public readonly Vector2 leftEar;
            public readonly Vector2 rightEar;

            // Confidence score [0, 1]
            public readonly float score;

            // Padding
            public readonly float pad1, pad2, pad3;

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


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Face detecion (blaze)";
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
                preprocessShader = Resources.Load("BlazePreprocessShader") as ComputeShader;
                postprocess1Shader = Resources.Load("BlazePostprocess1Shader") as ComputeShader;
                postprocess2Shader = Resources.Load("BlazePostprocess2Shader") as ComputeShader;

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
            _detections = GetDetections();

            // invoke the event
            if (OnFaceDetected != null)
            {
                int count = _detections.Length, index = 0;
                foreach (Detection det in _detections)
                {
                    Rect rect = new Rect(det.center.x - det.extent.x / 2f, det.center.y - det.extent.y / 2f, det.extent.x, det.extent.y);
                    OnFaceDetected(inferenceFrameTime, count, index++, rect, det.score);
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

            for (int i = 0; i < _detections.Length; i++)
            {
                var det = _detections[i];
                Color clr = Utils.GetColorByIndex(i);

                Rect imageRect = controller.GetImageRect(1f);  // _size x _size
                controller.DrawRect(det.center.x - det.extent.x / 2f, det.center.y - det.extent.y / 2f, det.extent.x, det.extent.y, 1f, clr, imageRect);

                controller.DrawPoint(det.leftEye.x, det.leftEye.y, 10f, clr, imageRect);
                controller.DrawPoint(det.rightEye.x, det.rightEye.y, 10f, clr, imageRect);
                controller.DrawPoint(det.nose.x, det.nose.y, 10f, clr, imageRect);
                controller.DrawPoint(det.mouth.x, det.mouth.y, 10f, clr, imageRect);
                controller.DrawPoint(det.leftEar.x, det.leftEar.y, 10f, clr, imageRect);
                controller.DrawPoint(det.rightEar.x, det.rightEar.y, 10f, clr, imageRect);

                //Debug.Log(string.Format("D{0} - c:{1}, e:{2}", i, det.center, det.extent));
            }
        }


        private IEnumerator RunModelRoutine(Texture source, float threshold)
        {
            // create or recreate the texture
            int minSize = _size;  // source.width < source.height ? source.width : source.height;
            if (_texture == null || _texture.width != minSize || _texture.height != minSize)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(minSize, minSize, 0);
                //Debug.Log("Creating texture " + minSize + " x " + minSize);
            }

            // get the texture to process
            var scale = GetBlitScale(source, _texture);
            var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture, scale, offset);
            //Debug.Log(string.Format("scale: {0:F3}, offset: {1:F3}", scale, offset));

            // Reset the compute buffer counters.
            _post1Buffer.SetCounterValue(0);
            _post2Buffer.SetCounterValue(0);

            // Preprocessing
            var pre = preprocessShader;
            if(pre != null)
            {
                pre.SetInt("_ImageSize", _size);
                pre.SetTexture(0, "_Texture", _texture);
                pre.SetBuffer(0, "_Tensor", _preBuffer);
                pre.Dispatch(0, _size / 8, _size / 8, 1);
            }

            // Run the BlazeFace model.
            //using (var tensor = new Tensor(1, _size, _size, 3, _preBuffer))
            //    _worker.Execute(tensor);

            yield return _worker.ExecuteAndWaitForResult(_preBuffer, _size, _size);

            //// get results in coroutine
            //StartCoroutine(GetResultsRoutine());

            // Output tensors -> Temporary render textures
            var scores1RT = _worker.CopyOutputToTempRT("Identity", 1, 512);
            var scores2RT = _worker.CopyOutputToTempRT("Identity_1", 1, 384);
            var boxes1RT = _worker.CopyOutputToTempRT("Identity_2", 16, 512);
            var boxes2RT = _worker.CopyOutputToTempRT("Identity_3", 16, 384);

            //yield return null;

            // 1st postprocess (bounding box aggregation)
            var post1 = postprocess1Shader;
            if (post1 != null)
            {
                post1.SetFloat("_ImageSize", _size);
                post1.SetFloat("_Threshold", threshold);
                post1.SetTexture(0, "_Scores", scores1RT);
                post1.SetTexture(0, "_Boxes", boxes1RT);
                post1.SetBuffer(0, "_Output", _post1Buffer);
                post1.Dispatch(0, 1, 1, 1);

                post1.SetTexture(1, "_Scores", scores2RT);
                post1.SetTexture(1, "_Boxes", boxes2RT);
                post1.SetBuffer(1, "_Output", _post1Buffer);
                post1.Dispatch(1, 1, 1, 1);
            }

            // Release the temporary render textures.
            RenderTexture.ReleaseTemporary(scores1RT);
            RenderTexture.ReleaseTemporary(scores2RT);
            RenderTexture.ReleaseTemporary(boxes1RT);
            RenderTexture.ReleaseTemporary(boxes2RT);

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
            _post2ReadCache = null;
            //_detections = GetDetections();

            // complete the inference (set ready state)
            base.CompleteInference();
        }

        //// gets the inference results
        //private IEnumerator GetResultsRoutine()
        //{
        //    // Output tensors -> Temporary render textures
        //    var scores1RT = _worker.CopyOutputToTempRT("Identity", 1, 512);
        //    var scores2RT = _worker.CopyOutputToTempRT("Identity_1", 1, 384);
        //    var boxes1RT = _worker.CopyOutputToTempRT("Identity_2", 16, 512);
        //    var boxes2RT = _worker.CopyOutputToTempRT("Identity_3", 16, 384);

        //    //yield return null;

        //    // 1st postprocess (bounding box aggregation)
        //    var post1 = postprocess1Shader;
        //    if(post1 != null)
        //    {
        //        post1.SetFloat("_ImageSize", _size);
        //        post1.SetFloat("_Threshold", threshold);
        //        post1.SetTexture(0, "_Scores", scores1RT);
        //        post1.SetTexture(0, "_Boxes", boxes1RT);
        //        post1.SetBuffer(0, "_Output", _post1Buffer);
        //        post1.Dispatch(0, 1, 1, 1);

        //        post1.SetTexture(1, "_Scores", scores2RT);
        //        post1.SetTexture(1, "_Boxes", boxes2RT);
        //        post1.SetBuffer(1, "_Output", _post1Buffer);
        //        post1.Dispatch(1, 1, 1, 1);
        //    }

        //    // Release the temporary render textures.
        //    RenderTexture.ReleaseTemporary(scores1RT);
        //    RenderTexture.ReleaseTemporary(scores2RT);
        //    RenderTexture.ReleaseTemporary(boxes1RT);
        //    RenderTexture.ReleaseTemporary(boxes2RT);

        //    yield return null;

        //    // Retrieve the bounding box count.
        //    ComputeBuffer.CopyCount(_post1Buffer, _countBuffer, 0);

        //    // 2nd postprocess (overlap removal)
        //    var post2 = postprocess2Shader;
        //    if(post2 != null)
        //    {
        //        post2.SetBuffer(0, "_Input", _post1Buffer);
        //        post2.SetBuffer(0, "_Count", _countBuffer);
        //        post2.SetBuffer(0, "_Output", _post2Buffer);
        //        post2.Dispatch(0, 1, 1, 1);
        //    }

        //    // Retrieve the bounding box count after removal.
        //    ComputeBuffer.CopyCount(_post2Buffer, _countBuffer, 0);

        //    // get the face detections
        //    _post2ReadCache = null;
        //    //_detections = GetDetections();

        //    // complete the inference (set ready state)
        //    base.CompleteInference();
        //}


        // detections read cache
        private Detection[] _post2ReadCache = null;
        private int[] _countReadCache = new int[1];

        // updates the detections cache and returns the detections.
        private Detection[] GetDetections()
        {
            _countBuffer.GetData(_countReadCache, 0, 0, 1);
            var count = _countReadCache[0];

            _post2ReadCache = new Detection[count];
            _post2Buffer.GetData(_post2ReadCache, 0, 0, count);

            return _post2ReadCache;
        }

    }
}
