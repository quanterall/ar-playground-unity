using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class FaceDetectionUltraPredictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Range(0, 1)]
        public float threshold = 0.7f;


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
            // face rectangle
            public readonly float x1, y1, x2, y2;

            // confidence score
            public readonly float score;

            // padding
            public readonly float pad1, pad2, pad3;

            // sizeof (Detection)
            public static int Size = 8 * sizeof(float);
        };

        // Maximum number of detections.
        public const int MaxDetection = 512;

        private int _inputWidth = 0;
        private int _inputHeight = 0;
        private int _outputCount = 0;

        // buffers and workers
        private (ComputeBuffer preBuf,
         ComputeBuffer post1Buf,
         ComputeBuffer post2Buf,
         RenderTexture scores,
         RenderTexture boxes,
         ComputeBuffer counterBuf,
         ComputeBuffer countReadBuf) _buffers;

        private IWorker _worker = null;
        //private IEnumerator _routine = null;


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Face detecion (ultra)";
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
                var inShape = nnModel.inputs[0].shape;
                var outShape = nnModel.GetShapeByName(nnModel.outputs[0]).Value;

                _inputWidth = inShape[6];
                _inputHeight = inShape[5];
                _outputCount = outShape[6];

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker();

                // load compute shaders
                preprocessShader = Resources.Load("UltraPreprocessShader") as ComputeShader;
                postprocess1Shader = Resources.Load("UltraPostprocess1Shader") as ComputeShader;
                postprocess2Shader = Resources.Load("UltraPostprocess2Shader") as ComputeShader;

                // Buffer allocation
                _buffers.preBuf = new ComputeBuffer(_inputWidth * _inputHeight * 3, sizeof(float));
                _buffers.post1Buf = new ComputeBuffer(MaxDetection, Detection.Size);
                _buffers.post2Buf = new ComputeBuffer(MaxDetection, Detection.Size, ComputeBufferType.Append);

                _buffers.scores = Utils.CreateFloat2RT(_outputCount / 20, 20);
                _buffers.boxes = Utils.CreateFloat4RT(_outputCount / 20, 20);

                _buffers.counterBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);

                _buffers.countReadBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

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

                _worker?.Dispose();
                _worker = null;

                if (_texture)
                    Utils.Destroy(_texture);
                _texture = null;

                _buffers.preBuf?.Dispose();
                _buffers.preBuf = null;

                _buffers.post1Buf?.Dispose();
                _buffers.post1Buf = null;

                _buffers.post2Buf?.Dispose();
                _buffers.post2Buf = null;

                Utils.Destroy(_buffers.scores);
                _buffers.scores = null;

                Utils.Destroy(_buffers.boxes);
                _buffers.boxes = null;

                _buffers.counterBuf?.Dispose();
                _buffers.counterBuf = null;

                _buffers.countReadBuf?.Dispose();
                _buffers.countReadBuf = null;
            }
        }

        /// <summary>
        /// Starts predictor's inference on the given image.
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public override bool StartInference(Texture texture)
        {
            base.StartInference(texture);
            RunModel(texture, threshold);

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
        public override bool TryGetResults()
        {
            _detections = GetDetections();
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

            Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight

            for (int i = 0; i < _detections.Length; i++)
            {
                var det = _detections[i];
                Color clr = Utils.GetColorByIndex(i);

                controller.DrawRect(det.x1, 1f - det.y2, det.x2 - det.x1, det.y2 - det.y1, 2f, clr, imageRect);
                //Debug.Log(string.Format("D{0} - R: ({1:F2}, {2:F2}, {3:F2}, {4:F2}), score: {5:F3}", i, det.x1, det.y1, det.x2, det.y2, det.score));
            }
        }


        // runs the inference model
        private void RunModel(Texture source, float threshold)
        {
            // create or recreate the texture
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputHeight)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(_inputWidth, _inputHeight, 0);
                //Debug.Log("Creating texture " + _inputWidth + " x " + _inputHeight);
            }

            // get the texture to process
            var scale = GetBlitScale(source, _texture);
            var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture);  //, scale, offset);

            // Preprocessing
            var pre = preprocessShader;
            if(pre != null)
            {
                pre.SetInts("ImageSize", _inputWidth, _inputHeight);
                pre.SetTexture(0, "Input", source);
                pre.SetBuffer(0, "Output", _buffers.preBuf);
                pre.DispatchThreads(0, _inputWidth, _inputHeight, 1);
            }

            // NNworker inference
            TensorShape inputShape = new TensorShape(1, _inputHeight, _inputWidth, 3);
            using (var t = new Tensor(inputShape, _buffers.preBuf))
                _worker.Execute(t);

            // get results in coroutine
            StartCoroutine(GetResultsRoutine());
        }

        // gets the inference results
        private IEnumerator GetResultsRoutine()
        {
            // NN output retrieval
            _worker.CopyOutput("scores", _buffers.scores);
            _worker.CopyOutput("boxes", _buffers.boxes);

            // Counter buffer reset
            _buffers.post2Buf.SetCounterValue(0);
            _buffers.counterBuf.SetCounterValue(0);

            //yield return null;

            // First stage postprocessing (detection data aggregation)
            var post1 = postprocess1Shader;
            if (post1 != null)
            {
                post1.SetTexture(0, "Scores", _buffers.scores);
                post1.SetTexture(0, "Boxes", _buffers.boxes);
                post1.SetDimensions("InputSize", _buffers.boxes);
                post1.SetFloat("Threshold", threshold);
                post1.SetBuffer(0, "Output", _buffers.post1Buf);
                post1.SetBuffer(0, "OutputCount", _buffers.counterBuf);
                post1.DispatchThreadPerPixel(0, _buffers.boxes);
            }

            yield return null;

            // Second stage postprocessing (overlap removal)
            var post2 = postprocess2Shader;
            if (post2)
            {
                post2.SetFloat("Threshold", 0.5f);
                post2.SetBuffer(0, "Input", _buffers.post1Buf);
                post2.SetBuffer(0, "InputCount", _buffers.counterBuf);
                post2.SetBuffer(0, "Output", _buffers.post2Buf);
                post2.Dispatch(0, 1, 1, 1);

                // Detection count after removal
                ComputeBuffer.CopyCount(_buffers.post2Buf, _buffers.countReadBuf, 0);
            }

            yield return null;

            // get face-tracking results
            _cached = null;
            //_detections = GetDetections();

            // complete the inference (set ready state)
            base.CompleteInference();
        }


        // detections read cache
        private Detection[] _cached;
        private int[] _countRead = new int[1];

        // updates the detections cache and returns the detections.
        private Detection[] GetDetections()
        {
            _buffers.countReadBuf.GetData(_countRead, 0, 0, 1);
            var count = _countRead[0];

            _cached = new Detection[count];
            _buffers.post2Buf.GetData(_cached, 0, 0, count);

            return _cached;
        }

    }
}
