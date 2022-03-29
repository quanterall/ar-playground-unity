using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class ObjectDetectionYoloV4Predictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Range(0, 1)]
        [Tooltip("IOU threshold")]
        public float threshold = 0.5f;

        /// <summary>
        /// Event, invoked when object gets detected (className, pixelRect, score)
        /// </summary>
        public event System.Action<long, int, int, string, Rect, float> OnObjectDetected;


        // compute shaders
        private ComputeShader preprocessShader = null;
        private ComputeShader postprocess1Shader = null;
        private ComputeShader postprocess2Shader = null;

        // the input texture
        private RenderTexture _texture;

        // face tracking detections
        private Detection[] _detections = null;

        // GUI style
        private GUIStyle guiStyle = new GUIStyle();


        /// <summary>
        /// Detection structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Detection
        {
            public readonly float x, y, w, h;
            public readonly uint classIndex;
            public readonly float score;

            // sizeof(Detection)
            public static int Size = 5 * sizeof(float) + sizeof(uint);
        };

        // maximum number of detections & anchor count.
        public const int MaxDetection = 512;
        public const int AnchorCount = 3;

        // model parameters
        private int _inputWidth = 0;
        //private int _inputHeight = 0;

        private int _classCount = 0;
        private int _featMap1Width = 0;
        private int _featMap2Width = 0;

        private string[] _classNames = null;
        private float[] _anchorArray1 = null;
        private float[] _anchorArray2 = null;

        (ComputeBuffer preprocess,
         RenderTexture feature1,
         RenderTexture feature2,
         ComputeBuffer post1,
         ComputeBuffer post2,
         ComputeBuffer counter,
         ComputeBuffer countRead) _buffers;

        private IWorker _worker = null;
        //private IEnumerator _routine = null;


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Object detecion (yolo-v4)";
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
                var out1Shape = nnModel.GetShapeByName(nnModel.outputs[0]).Value;
                var out2Shape = nnModel.GetShapeByName(nnModel.outputs[1]).Value;

                _inputWidth = inShape[6];  // width
                _classCount = out1Shape.channels / AnchorCount - 5;
                _featMap1Width = out1Shape.width;
                _featMap2Width = out2Shape.width;

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker();

                // load compute shaders
                preprocessShader = Resources.Load("YoloV4PreprocessShader") as ComputeShader;
                postprocess1Shader = Resources.Load("YoloV4Postprocess1Shader") as ComputeShader;
                postprocess2Shader = Resources.Load("YoloV4Postprocess2Shader") as ComputeShader;

                // Buffer allocation
                _buffers.preprocess = new ComputeBuffer(_inputWidth * _inputWidth * 3, sizeof(float));

                int featDataSize = (5 + _classCount) * AnchorCount;
                _buffers.feature1 = Utils.CreateFloat1RT(featDataSize, _featMap1Width * _featMap1Width);
                _buffers.feature2 = Utils.CreateFloat1RT(featDataSize, _featMap2Width * _featMap2Width);

                _buffers.post1 = new ComputeBuffer(MaxDetection, Detection.Size);
                _buffers.post2 = new ComputeBuffer(MaxDetection, Detection.Size, ComputeBufferType.Append);

                _buffers.counter = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);
                _buffers.countRead = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

                // read class names
                _classNames = Utils.GetResourceStrings("YoloV4ClassNames");

                // load anchors 
                LoadYoloAnchorArrays();

                // GUI style
                guiStyle.fontSize = 16;
                guiStyle.wordWrap = true;
                guiStyle.richText = true;
                guiStyle.stretchWidth = true;
                guiStyle.stretchHeight = true;

                return true;
            }

            return false;
        }

        // load yolo-v4 anchor arrays
        private void LoadYoloAnchorArrays()
        {
            string[] anchorValues = Utils.GetResourceStrings("YoloV4Anchors");
            string[] allAnchorIndices = Utils.GetResourceStrings("YoloV4AnchorIndices");

            if(anchorValues != null && allAnchorIndices != null && allAnchorIndices.Length >= 2)
            {
                var scale = 1.0f / _inputWidth;
                _anchorArray1 = GetYoloAnchors(allAnchorIndices[0].Split(",".ToCharArray()), anchorValues, scale);
                _anchorArray2 = GetYoloAnchors(allAnchorIndices[1].Split(",".ToCharArray()), anchorValues, scale);
            }
        }

        // returns yolo anchor array
        private float[] GetYoloAnchors(string[] anchorIndices, string[] anchorValues, float scale)
        {
            float[] anchors = new float[anchorIndices.Length * 4];

            for (int i = 0; i < anchorIndices.Length; i++)
            {
                int ai = int.Parse(anchorIndices[i]);
                string[] anchorXY = anchorValues[ai].Split(",".ToCharArray());

                anchors[i * 4] = float.Parse(anchorXY[0]) * scale;
                anchors[i * 4 + 1] = float.Parse(anchorXY[1]) * scale;
            }

            return anchors;
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

                _buffers.preprocess?.Dispose();
                _buffers.preprocess = null;

                Utils.Destroy(_buffers.feature1);
                _buffers.feature1 = null;

                Utils.Destroy(_buffers.feature2);
                _buffers.feature2 = null;

                _buffers.post1?.Dispose();
                _buffers.post1 = null;

                _buffers.post2?.Dispose();
                _buffers.post2 = null;

                _buffers.counter?.Dispose();
                _buffers.counter = null;

                _buffers.countRead?.Dispose();
                _buffers.countRead = null;
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
            _detections = GetDetections();

            Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight
            if (OnObjectDetected != null)
            {
                int count = _detections.Length, index = 0;
                foreach (Detection det in _detections)
                {
                    Vector3 pos = controller.GetImagePos(det.x - det.w / 2f, 1f - det.y - det.h / 2f, imageRect);
                    Rect rect = new Rect(pos.x, pos.y, det.w * imageRect.width, det.h * imageRect.height);
                    OnObjectDetected(inferenceFrameTime, count, index++, _classNames[det.classIndex], rect, det.score);
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

            Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight

            for (int i = 0; i < _detections.Length; i++)
            {
                var det = _detections[i];
                //Color clr = Utils.GetColorByIndex(i);
                Color clr = GetColorByClassIndex(det.classIndex);

                controller.DrawRect(det.x - det.w / 2f, 1f - det.y - det.h / 2f, det.w, det.h, 2f, clr, imageRect);
                //Debug.Log(string.Format("D{0} {1} - {6:F3}, R: ({2:F2}, {3:F2}, {4:F2}, {5:F2})", i, _classNames[det.classIndex], det.x, det.y, det.w, det.h, det.score));
            }
        }

        /// <summary>
        /// Displays the results-related GUI (labels, etc.) on screen.
        /// </summary>
        /// <param name="controller"></param>
        public override void DisplayResultsGUI(PlaygroundController controller)
        {
            if (_detections == null)
                return;

            Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight

            for (int i = 0; i < _detections.Length; i++)
            {
                var det = _detections[i];
                //Color clr = Utils.GetColorByIndex(i);
                Color clr = GetColorByClassIndex(det.classIndex);

                Vector3 pos = controller.GetImagePos(det.x - det.w / 2f, det.y - det.h / 2f, imageRect);
                Rect rect = new Rect(pos.x, pos.y, 300, 50);  //, (1f - det.y - det.h / 2f) * imageRect.height + imageRect.y, det.w * imageRect.width, det.h * imageRect.height);

                GUI.Label(rect, string.Format("<size=30><color=#{1}> {0}</color></size>", _classNames[det.classIndex], ColorUtility.ToHtmlStringRGBA(clr)), guiStyle);
                //Debug.Log(_classNames[det.classIndex] + ", clr: " + ColorUtility.ToHtmlStringRGBA(clr));
            }
        }

        // returns the color by class index
        private Color GetColorByClassIndex(uint classIndex)
        {
            float hue = classIndex * 0.073f % 1f;
            return Color.HSVToRGB(hue, 1, 1);
        }


        // runs the inference model
        private IEnumerator RunModelRoutine(Texture source, float threshold)
        {
            // create or recreate the texture
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputWidth)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(_inputWidth, _inputWidth, 0);
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
                pre.SetInt("Size", _inputWidth);
                pre.SetTexture(0, "Image", source);
                pre.SetBuffer(0, "Tensor", _buffers.preprocess);
                pre.DispatchThreads(0, _inputWidth, _inputWidth, 1);
            }

            // NNworker inference
            //TensorShape inputShape = new TensorShape(1, _inputWidth, _inputWidth, 3);
            //using (var t = new Tensor(inputShape, _buffers.preprocess))
            //    _worker.Execute(t);

            yield return _worker.ExecuteAndWaitForResult(_buffers.preprocess, _inputWidth, _inputWidth, "Identity");

            //// get results in coroutine
            //StartCoroutine(GetResultsRoutine());

            // NN output retrieval
            _worker.CopyOutput("Identity", _buffers.feature1);
            _worker.CopyOutput("Identity_1", _buffers.feature2);

            // Counter buffer reset
            _buffers.post2.SetCounterValue(0);
            _buffers.counter.SetCounterValue(0);

            //yield return null;

            // First stage postprocessing (detection data aggregation)
            var post1 = postprocess1Shader;
            if (post1 != null)
            {
                post1.SetInt("ClassCount", _classCount);
                post1.SetFloat("Threshold", threshold);
                post1.SetBuffer(0, "Output", _buffers.post1);
                post1.SetBuffer(0, "OutputCount", _buffers.counter);

                // (feature map 1)
                var width1 = _featMap1Width;
                post1.SetTexture(0, "Input", _buffers.feature1);
                post1.SetInt("InputSize", width1);
                post1.SetFloats("Anchors", _anchorArray1);
                post1.DispatchThreads(0, width1, width1, 1);

                // (feature map 2)
                var width2 = _featMap2Width;
                post1.SetTexture(0, "Input", _buffers.feature2);
                post1.SetInt("InputSize", width2);
                post1.SetFloats("Anchors", _anchorArray2);
                post1.DispatchThreads(0, width2, width2, 1);
            }

            //yield return null;

            // Second stage postprocessing (overlap removal)
            var post2 = postprocess2Shader;
            if (post2)
            {
                post2.SetFloat("Threshold", threshold);
                post2.SetBuffer(0, "Input", _buffers.post1);
                post2.SetBuffer(0, "InputCount", _buffers.counter);
                post2.SetBuffer(0, "Output", _buffers.post2);
                post2.Dispatch(0, 1, 1, 1);

                // Detection count after removal
                ComputeBuffer.CopyCount(_buffers.post2, _buffers.countRead, 0);
            }

            //yield return null;

            // get face-tracking results
            _cached = null;
            //_detections = GetDetections();

            // complete the inference (set ready state)
            base.CompleteInference();
        }

        //// gets the inference results
        //private IEnumerator GetResultsRoutine()
        //{
        //    // NN output retrieval
        //    _worker.CopyOutput("Identity", _buffers.feature1);
        //    _worker.CopyOutput("Identity_1", _buffers.feature2);

        //    // Counter buffer reset
        //    _buffers.post2.SetCounterValue(0);
        //    _buffers.counter.SetCounterValue(0);

        //    //yield return null;

        //    // First stage postprocessing (detection data aggregation)
        //    var post1 = postprocess1Shader;
        //    if (post1 != null)
        //    {
        //        post1.SetInt("ClassCount", _classCount);
        //        post1.SetFloat("Threshold", threshold);
        //        post1.SetBuffer(0, "Output", _buffers.post1);
        //        post1.SetBuffer(0, "OutputCount", _buffers.counter);

        //        // (feature map 1)
        //        var width1 = _featMap1Width;
        //        post1.SetTexture(0, "Input", _buffers.feature1);
        //        post1.SetInt("InputSize", width1);
        //        post1.SetFloats("Anchors", _anchorArray1);
        //        post1.DispatchThreads(0, width1, width1, 1);

        //        // (feature map 2)
        //        var width2 = _featMap2Width;
        //        post1.SetTexture(0, "Input", _buffers.feature2);
        //        post1.SetInt("InputSize", width2);
        //        post1.SetFloats("Anchors", _anchorArray2);
        //        post1.DispatchThreads(0, width2, width2, 1);
        //    }

        //    yield return null;

        //    // Second stage postprocessing (overlap removal)
        //    var post2 = postprocess2Shader;
        //    if (post2)
        //    {
        //        post2.SetFloat("Threshold", threshold);
        //        post2.SetBuffer(0, "Input", _buffers.post1);
        //        post2.SetBuffer(0, "InputCount", _buffers.counter);
        //        post2.SetBuffer(0, "Output", _buffers.post2);
        //        post2.Dispatch(0, 1, 1, 1);

        //        // Detection count after removal
        //        ComputeBuffer.CopyCount(_buffers.post2, _buffers.countRead, 0);
        //    }

        //    //yield return null;

        //    // get face-tracking results
        //    _cached = null;
        //    //_detections = GetDetections();

        //    // complete the inference (set ready state)
        //    base.CompleteInference();
        //}


        // detections read cache
        private Detection[] _cached;
        private int[] _countRead = new int[1];

        // updates the detections cache and returns the detections.
        private Detection[] GetDetections()
        {
            _buffers.countRead.GetData(_countRead, 0, 0, 1);
            var count = _countRead[0];

            _cached = new Detection[count];
            _buffers.post2.GetData(_cached, 0, 0, count);

            return _cached;
        }

    }
}
