using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class BodyTrackingPosenetPredictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Tooltip("Maximum number of the detected bodies")]
        [Range(1, 20)]
        private int maxBodies = 20;

        [Tooltip("Score threshold for multi-pose estimation")]
        [Range(0, 1.0f)]
        private float scoreThreshold = 0.5f;

        [Tooltip("Non-maximum suppression distance")]
        private int nmsRadius = 20;

        [Tooltip("Minimum confidence level required to display a key point")]
        [Range(0f, 1f)]
        public float minConfidence = 0.7f;

        //[Tooltip("UI text to display information messages.")]
        //public UnityEngine.UI.Text infoText;

        /// <summary>
        /// Event, invoked when body gets detected (time, count, index, bodyPoints)
        /// </summary>
        public event System.Action<long, int, int, PosenetUtils.BodyPoints> OnBodyDetected;


        // compute shaders
        private ComputeShader preprocessShader;

        // the input texture
        private RenderTexture _texture;

        // currently estimated keypoint positions in texture
        private PosenetUtils.BodyPoints[] _bodyPoints;
        private PosenetUtils.Keypoint[][] _keypoints;
        private object _keypointLock = new object();
        private List<PosenetUtils.Keypoint> listAllKeypoints;

        // model worker
        private IWorker _worker = null;
        private int _preKernelIndex = 0;

        // the shorter texture dimension
        private const int MinTexSize = 320;

        // input texture width & height
        private int _inputWidth = 0;
        private int _inputHeight = 0;

        // layer names in the model
        private string _heatmapLayer = null;
        private string _offsetsLayer = null;
        private string _dispFwLayer = null;
        private string _dispBwLayer = null;
        private string _predictionLayer = "heatmap_predictions";

        // model output tensors
        private Tensor _heatmapTensor = null;
        private Tensor _offsetsTensor = null;
        private Tensor _dispFwdTensor = null;
        private Tensor _dispBwdTensor = null;
        private int _stride = 0;

        //// debug objects
        //private GameObject[] _debugObjs = new GameObject[10];
        //private Vector2[] _debugPoints = new Vector2[10];


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Body tracking (posenet)";
        }

        /// <summary>
        /// Initializes the predictor's model and worker.
        /// </summary>
        /// <returns></returns>
        public override bool InitPredictor()
        {
            if(_worker == null)
            {
                this.workInBackground = true;
                base.InitPredictor();

                // load the model
                var nnModel = ModelLoader.Load(model);

                var inShape = nnModel.inputs[0].shape;
                _inputWidth = inShape[6];
                _inputHeight = inShape[5];

                if (_inputWidth == 0 || _inputHeight == 0)
                {
                    // set constant tex size
                    _inputWidth = _inputHeight = MinTexSize;
                }

                //Debug.Log(string.Format("Input texture width: {0}, height: {1}", _inputWidth, _inputHeight));

                // load compute shaders
                preprocessShader = Resources.Load("PosenetPreprocessShader") as ComputeShader;

                if (!model.name.Contains("resnet"))
                {
                    _dispFwLayer = nnModel.outputs[2];
                    _dispBwLayer = nnModel.outputs[3];

                    _preKernelIndex = preprocessShader.FindKernel("PreprocessMobileNet");
                }
                else
                {
                    _dispFwLayer = nnModel.outputs[3];
                    _dispBwLayer = nnModel.outputs[2];

                    _preKernelIndex = preprocessShader.FindKernel("PreprocessResNet");
                }

                _heatmapLayer = nnModel.outputs[0];
                _offsetsLayer = nnModel.outputs[1];

                //Debug.Log(string.Format("Model: {0}, preKernel: {1}, hmLayer: {2}, ofsLayer: {3}, fwdLayer: {4}, bwdLayer: {5}", 
                //    model.name, _preKernelIndex, _heatmapLayer, _offsetsLayer, _dispFwLayer, _dispBwLayer));

                // add a sigmoid layer to the layer
                ModelBuilder modelBuilder = new ModelBuilder(nnModel);
                modelBuilder.Sigmoid(_predictionLayer, _heatmapLayer);

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, modelBuilder.model, false);  // nnModel
                //infoText.text = workerType.ToString();

                //// debug objs
                //for(int i = 0; i < _debugObjs.Length; i++)
                //{
                //    _debugObjs[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                //    _debugObjs[i].transform.localScale = Vector3.one * 0.1f;
                //    _debugObjs[i].name = "DebugBody" + i;
                //    _debugObjs[i].transform.parent = transform;
                //}

                return true;
            }

            return false;
        }

        /// <summary>
        /// Releases the resources used by the predictor.
        /// </summary>
        public override void FinishPredictor()
        {
            if (_worker != null)
            {
                base.FinishPredictor();

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
            //RunModel(texture);
            StartCoroutine(RunModelRoutine(texture));

            return true;
        }

        /// <summary>
        /// Completes the last started inference.
        /// </summary>
        public override bool CompleteInference()
        {
            if (GetInferenceResults())
                return base.CompleteInference();
            else
                return false;
        }

        /// <summary>
        /// Tries to get the last inference results in the main thread.
        /// </summary>
        /// <returns></returns>
        public override bool TryGetResults(PlaygroundController controller)
        {
            if (_keypoints == null)
                return false;

            //Debug.Log("trying to get results at: " + DateTime.Now.ToString("o"));
            lock (_keypointLock)
            {
                int numElements = _keypoints.GetLength(0);

                if(_bodyPoints == null || _bodyPoints.Length != numElements)
                {
                    _bodyPoints = new PosenetUtils.BodyPoints[numElements];
                }

                for (int i = 0; i < numElements; i++)
                {
                    _bodyPoints[i] = PosenetUtils.GetBodyPoints(_keypoints[i], _texture.width, _texture.height, scoreThreshold);
                    //Debug.Log(string.Format("  body {0} - pos: {1}, score: {2}", i, _bodyPoints[i].position, _bodyPoints[i].score));

                    if(controller.IsDepthAvailable)
                    {
                        Rect imageRect = controller.GetImageRect();

                        for (int k = 0; k < _bodyPoints[i].keypoints.Length; k++)
                        {
                            PosenetUtils.Keypoint kp = _bodyPoints[i].keypoints[k];
                            Vector2 kpNormPos = new Vector2(kp.position.x / _texture.width, kp.position.y / _texture.height);
                            //Debug.Log(string.Format("  body {0}, kp {1} - pos: {2}, w: {3}, h: {4}, norm: {5}", i, k, kp.position, _texture.width, _texture.height, kpNormPos));

                            float depth = controller.GetDepthForPixel(kpNormPos);
                            kp.spacePos = controller.UnprojectPoint(kpNormPos, depth);

                            _bodyPoints[i].keypoints[k] = kp;
                        }

                        _bodyPoints[i].spacePos = _bodyPoints[i].keypoints[0].spacePos;
                    }
                }
            }

            // invoke the event
            if (OnBodyDetected != null)
            {
                int count = _bodyPoints.Length, index = 0;
                foreach (var body in _bodyPoints)
                {
                    OnBodyDetected(inferenceFrameTime, count, index++, body);
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
            if (_bodyPoints == null)
                return;

            //Debug.Log("    displaying inference results at: " + DateTime.Now.ToString("o"));
            int bodyCount = _bodyPoints.Length;
            Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight

            Tuple<int, int>[] displayBones = PosenetUtils.displayBones;
            int bonesCount = displayBones.GetLength(0);
            //Debug.Log(string.Format("BodyCount: {0}, BonesCount: {1}, ImageRect: {2}", bodyCount, bonesCount, imageRect));

            for (int i = 0; i < bodyCount; i++)
            {
                Color clr = Utils.GetColorByIndex(i);  // Color.red; // 

                for (int j = 0; j < bonesCount; j++)
                {
                    int k1 = displayBones[j].Item1;
                    int k2 = displayBones[j].Item2;

                    PosenetUtils.Keypoint kp1 = _bodyPoints[i].keypoints[k1];
                    PosenetUtils.Keypoint kp2 = _bodyPoints[i].keypoints[k2];
                    //Debug.Log(string.Format("  d-body: {0}, kp: {1}, pos1: {2}/{3:F2}, pos2: {4}/{5:F2}", i, j, kp1.position, kp1.score, kp2.position, kp2.score));

                    if (kp1.score >= minConfidence && kp2.score >= minConfidence)
                    {
                        controller.DrawLine(kp1.position.x / _texture.width, 1f - kp1.position.y / _texture.height,
                            kp2.position.x / _texture.width, 1f - kp2.position.y / _texture.height, 2f, clr, imageRect);
                    }
                }

                //if(_bodyPoints[i].score >= minConfidence)
                //    Debug.Log(string.Format("  body {0} - P0: ({1:F2}, {2:F2}), score: {3:F2}", i, _bodyPoints[i].position.x, _bodyPoints[i].position.y, _bodyPoints[i].score));
            }

            //// keypoits & displacements
            //for (int i = 0; i < listAllKeypoints.Count; i++)
            //{
            //    PosenetUtils.Keypoint kp = listAllKeypoints[i];
            //    controller.DrawPoint(kp.position.x / _texture.width, 1f - kp.position.y / _texture.height, 7f, Color.yellow, imageRect);

            //    controller.DrawLine(kp.posSrc.x / _texture.width, 1f - kp.posSrc.y / _texture.height,
            //        kp.posTgt.x / _texture.width, 1f - kp.posTgt.y / _texture.height, 2f, Color.magenta, imageRect);
            //}

            //// draw projection points
            //for (int i = 0; i < _debugPoints.Length; i++)
            //{
            //    Color clr = Utils.GetColorByIndex(i);
            //    controller.DrawPoint(_debugPoints[i].x, 1f - _debugPoints[i].y, 20f, clr, imageRect);
            //}
        }


        // runs the model in coroutine
        private IEnumerator RunModelRoutine(Texture source)
        {
            //Debug.Log("RunModelRoutine starting at: " + DateTime.Now.ToString("o"));

            // create or recreate the texture
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputHeight)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(_inputWidth, _inputHeight, 0, RenderTextureFormat.ARGBHalf);
                //Debug.Log("Creating texture " + _texture.width + " x " + _texture.height);
            }

            // get the texture to process
            Graphics.Blit(source, _texture);

            // preprocess the texture
            RenderTexture result = RenderTexture.GetTemporary(_texture.width, _texture.height, 0, RenderTextureFormat.ARGBHalf);
            result.enableRandomWrite = true;
            result.Create();

            preprocessShader.SetTexture(_preKernelIndex, "InputImage", _texture);
            preprocessShader.SetTexture(_preKernelIndex, "Result", result);
            preprocessShader.Dispatch(_preKernelIndex, result.width / 8, result.height / 8, 1);

            // copy the preprocessed texture back
            Graphics.Blit(result, _texture);
            RenderTexture.ReleaseTemporary(result);

            //Debug.Log("texture preprocessing completed at: " + DateTime.Now.ToString("o"));
            //yield return null;

            // NNworker inference
            //Debug.Log("worker execution starting at " + DateTime.Now.ToString("o"));
            //using (var t = new Tensor(_texture, channels: 3))
            //{
            //    //Debug.Log("  worker execution started at "  + DateTime.Now.ToString("o"));
            //    _heatmapTensor = _worker.Execute(t).PeekOutput(_predictionLayer);
            //    //Debug.Log(string.Format("ExecTime - Prev: {0}, Now: {1}, Diff: {2}", timePrev, DateTime.Now.Ticks, (DateTime.Now.Ticks - timePrev)));
            //}

            //yield return new WaitForCompletion(_heatmapTensor);  // null;

            yield return _worker.ExecuteAndWaitForResult(_texture, _predictionLayer);
            //Debug.Log("  worker execution finished at: " + System.DateTime.Now.ToString("o"));

            // get the model output
            _heatmapTensor = _worker.CopyOutput(_predictionLayer);  // .PeekOutput(_predictionLayer);
            _offsetsTensor = _worker.CopyOutput(_offsetsLayer);  // .PeekOutput(_offsetsLayer);
            _dispFwdTensor = _worker.CopyOutput(_dispFwLayer);  // .PeekOutput(_dispFwLayer);
            _dispBwdTensor = _worker.CopyOutput(_dispBwLayer);  // .PeekOutput(_dispBwLayer);
            //Debug.Log("  copy output tensors finished at: " + System.DateTime.Now.ToString("o"));

            //heatmaps.PrepareCacheForAccess();
            //offsets.PrepareCacheForAccess();
            //displacementFWD.PrepareCacheForAccess();
            //displacementBWD.PrepareCacheForAccess();

            // Calculate the stride used to scale down the inputImage
            _stride = (_texture.height - 1) / (_heatmapTensor.shape.height - 1);
            _stride -= (_stride % 8);

            //Debug.Log("RunModel finished at: " + System.DateTime.Now.ToString("o"));

            // start inference completion in background
            CompleteInferenceInBackground();
        }

        // gets the inference results
        private bool GetInferenceResults()
        {
            if(_heatmapTensor != null && _offsetsTensor != null && _dispFwdTensor != null && _dispBwdTensor != null)
            {
                // determine the key points
                //Debug.Log("decoding multiple poses started at: " + DateTime.Now.ToString("o"));
                var keypoints = PosenetUtils.DecodeMultiplePoses(_heatmapTensor, _offsetsTensor, _dispFwdTensor, _dispBwdTensor, out listAllKeypoints, _stride, maxBodies, scoreThreshold, nmsRadius);
                //Debug.Log("decoding multiple poses finished at: " + DateTime.Now.ToString("o"));

                lock (_keypointLock)
                {
                    _keypoints = keypoints;
                }

                _heatmapTensor.Dispose();
                _offsetsTensor.Dispose();
                _dispFwdTensor.Dispose();
                _dispBwdTensor.Dispose();

                _heatmapTensor = _offsetsTensor = _dispFwdTensor = _dispBwdTensor = null;
                //Debug.Log("getting inference results finished at: " + DateTime.Now.ToString("o"));

                return true;
            }

            return false;
        }

    }
}
