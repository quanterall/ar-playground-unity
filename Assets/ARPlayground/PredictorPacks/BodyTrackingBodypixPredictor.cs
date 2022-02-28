using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class BodyTrackingBodypixPredictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        //[Tooltip("Width and height of the model input image")]
        //public Vector2Int inputResolution = new Vector2Int(512, 384);

        [Tooltip("Model stride")]
        public Stride stride = Stride._8;
        public enum Stride : int { _8 = 8, _16 = 16 }

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


        [Tooltip("RawImage to display the body segmentation mask")]
        public UnityEngine.UI.RawImage maskImage;


        // compute shaders
        private ComputeShader preprocessShader = null;
        private ComputeShader maskShader = null;
        private ComputeShader keypointsShader = null;

        // the input texture
        private RenderTexture _texture;

        // model output tensors
        private Tensor _heatmapTensor = null;
        private Tensor _offsetsTensor = null;
        private Tensor _dispFwdTensor = null;
        private Tensor _dispBwdTensor = null;


        // input and output image dimensions
        private int _inputWidth = 0;
        private int _inputHeight = 0;
        private int _outputWidth = 0;
        private int _outputHeight = 0;

        // buffers and textures
        private ComputeBuffer preprocessBuf;
        private RenderTexture segmentTex;
        //private RenderTexture heatmapsTex;

        // keypoints
        private PosenetUtils.Keypoint[][] _keypoints = null;
        private object _keypointLock = new object();
        private List<PosenetUtils.Keypoint> listAllKeypoints;

        private IWorker _worker = null;

        // the shorter texture dimension
        private const int MinTexSize = 320;  // 384;


        protected override void OnEnable()
        {
            base.OnEnable();

            if(maskImage)
            {
                maskImage.gameObject.SetActive(true);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (maskImage)
            {
                maskImage.gameObject.SetActive(false);
            }
        }


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Body segmentation (bodypix)";
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

                // load the model
                var nnModel = ModelLoader.Load(model);

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker();

                // load compute shaders
                preprocessShader = Resources.Load("BodypixPreprocessShader") as ComputeShader;
                maskShader = Resources.Load("BodypixMaskShader") as ComputeShader;
                keypointsShader = Resources.Load("BodypixPreprocessShader") as ComputeShader;

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

                // dispose the worker
                _worker?.Dispose();
                _worker = null;

                // free the buffers & render textures
                DeallocateBuffers();
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
            //RunModel(texture);  
            StartCoroutine(RunModelRoutine(texture));

            return true;
        }

        /// <summary>
        /// Completes the last started inference.
        /// </summary>
        public override bool CompleteInference()
        {
            ////GetResults();
            ////return base.CompleteInference();
            //return true;

            if (GetResults())
                return base.CompleteInference();
            else
                return false;
        }

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        public override void DisplayInferenceResults(PlaygroundController controller)
        {
            //if (_keypoints == null)
            //    return;

            //lock(_keypointLock)
            //{
            //    int bodyCount = _keypoints.GetLength(0);
            //    Rect imageRect = controller.GetImageRect();  // (float)_inputWidth / _inputHeight

            //    System.Tuple<int, int>[] displayBones = PosenetUtils.displayBones;
            //    int bonesCount = displayBones.GetLength(0);
            //    //Debug.Log(string.Format("BodyCount: {0}, BonesCount: {1}, ImageRect: {2}", bodyCount, bonesCount, imageRect));

            //    for (int i = 0; i < bodyCount; i++)
            //    {
            //        Color clr = Utils.GetColorByIndex(i);  // Color.red; // 

            //        for (int j = 0; j < bonesCount; j++)
            //        {
            //            int k1 = displayBones[j].Item1;
            //            int k2 = displayBones[j].Item2;

            //            PosenetUtils.Keypoint kp1 = _keypoints[i][k1];
            //            PosenetUtils.Keypoint kp2 = _keypoints[i][k2];
            //            //Debug.Log(string.Format("  body: {0}, kp: {1}, pos1: {2}/{3:F2}, pos2: {4}/{5:F2}", i, j, kp1.position, kp1.score, kp2.position, kp2.score));

            //            if (kp1.score >= minConfidence && kp2.score >= minConfidence)
            //            {
            //                controller.DrawLine(kp1.position.x / _texture.width, 1f - kp1.position.y / _texture.height,
            //                    kp2.position.x / _texture.width, 1f - kp2.position.y / _texture.height, 2f, clr, imageRect);
            //            }
            //        }

            //        //Keypoint kp0 = _keypoints[i][0];
            //        //if(kp0.score >= minConfidence)
            //        //    Debug.Log(string.Format("B{0} - P0: ({1:F2}, {2:F2}), score: {3:F2}", i, kp0.position.x, kp0.position.y, kp0.score));
            //    }

            //    //// keypoits & displacements
            //    //for (int i = 0; i < listAllKeypoints.Count; i++)
            //    //{
            //    //    PosenetUtils.Keypoint kp = listAllKeypoints[i];
            //    //    controller.DrawPoint(kp.position.x / _texture.width, 1f - kp.position.y / _texture.height, 7f, Color.yellow, imageRect);

            //    //    controller.DrawLine(kp.posSrc.x / _texture.width, 1f - kp.posSrc.y / _texture.height,
            //    //        kp.posTgt.x / _texture.width, 1f - kp.posTgt.y / _texture.height, 2f, Color.magenta, imageRect);
            //    //}

            //}
        }


        // allocates the needed buffers and render textures
        private void AllocateBuffers()
        {
            // allocate buffers
            int inputFootprint = _inputWidth * _inputHeight * 3;
            preprocessBuf = new ComputeBuffer(inputFootprint, sizeof(float));

            segmentTex = Utils.CreateFloat1RT(_outputWidth, _outputHeight);
            //heatmapsTex = Utils.CreateFloat1RT(_outputWidth * PosenetUtils.KeypointCount, _outputHeight);
        }

        // frees the allocated buffers and render textures
        private void DeallocateBuffers()
        {
            // dispose input texture
            if (_texture)
                Utils.Destroy(_texture);
            _texture = null;

            // dispose buffers
            preprocessBuf?.Dispose();
            preprocessBuf = null;

            Utils.Destroy(segmentTex);
            segmentTex = null;

            //Utils.Destroy(heatmapsTex);
            //heatmapsTex = null;
        }

        // preprocess coefficients
        private readonly Vector4 PreprocessCoeffsMobileNetV1 = new Vector4(-1, -1, -1, 2);
        private readonly Vector4 PreprocessCoeffsResNet50 = new Vector4(-123.15f, -115.90f, -103.06f, 255);

        private const long TIME_THRESHOLD_TICKS = 100000;  // 30 FPS -> 10000000 tps / 30 = 333333

        // runs the inference model
        private IEnumerator RunModelRoutine(Texture source)
        {
            //Debug.Log("RunModel starting at: " + System.DateTime.Now.ToString("o"));

            // estimate input & output resolutions
            if (source.width > source.height)
            {
                _inputWidth = (MinTexSize * source.width / source.height + 15) / 16 * 16 + 1;
                _inputHeight = (MinTexSize + 15) / 16 * 16 + 1;
            }
            else
            {
                _inputWidth = (MinTexSize + 15) / 16 * 16 + 1;
                _inputHeight = (MinTexSize * source.height / source.width + 15) / 16 * 16 + 1;
            }

            _outputWidth = _inputWidth / (int)stride + 1;
            _outputHeight = _inputHeight / (int)stride + 1;

            // create or recreate the texture
            bool bImageSizeChanged = false;
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputHeight)
            {
                //if (_texture)
                //    Utils.Destroy(_texture);
                DeallocateBuffers();

                _texture = new RenderTexture(_inputWidth, _inputHeight, 0);
                AllocateBuffers();

                bImageSizeChanged = true;
                //Debug.Log(string.Format("Created input texture: ({0}x{1}), output: ({2}x{3}), source: ({4}x{5})", _inputWidth, _inputHeight, _outputWidth, _outputHeight, source.width, source.height));
            }

            // get the texture to process
            //var scale = GetBlitScale(source, _texture);
            //var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture);  // , scale, offset);  // 
            //Debug.Log("  texture blitted at: " + System.DateTime.Now.ToString("o"));

            // Preprocessing
            var pre = preprocessShader;
            if(pre != null)
            {
                pre.SetTexture(0, "Input", _texture);
                pre.SetBuffer(0, "Output", preprocessBuf);
                pre.SetInts("InputSize", _inputWidth, _inputHeight);
                pre.SetVector("ColorCoeffs", PreprocessCoeffsMobileNetV1);
                pre.SetBool("InputIsLinear", QualitySettings.activeColorSpace == ColorSpace.Linear);
                pre.DispatchThreads(0, _inputWidth, _inputHeight, 1);
                //Debug.Log("  preprocessing finished at: " + System.DateTime.Now.ToString("o"));
            }

            //yield return null;

            // NNworker inference
            TensorShape inputShape = new TensorShape(1, _inputHeight, _inputWidth, 3);
            using (var t = new Tensor(inputShape, preprocessBuf))
            {
                //Debug.Log("    worker execution started at " + System.DateTime.Now.ToString("o"));
                _heatmapTensor = _worker.Execute(t).PeekOutput("heatmaps");
            }

            // wait for completion
            yield return new WaitForCompletion(_worker.PeekOutput("heatmaps"));
            //Debug.Log("  worker execution finished at: " + System.DateTime.Now.ToString("o"));

            // get results in coroutine
            //StartCoroutine(GetResultsRoutine());

            // NN output retrieval
            _worker.CopyOutput("segments", segmentTex);
            //_worker.CopyOutput("heatmaps", heatmapsTex);
            //Debug.Log("  copy output textures finished at: " + System.DateTime.Now.ToString("o"));

            // update the mask image, if needed
            if (bImageSizeChanged && maskImage != null)
            {
                Texture targetTex = segmentTex;  // segmentTex  // heatmapsTex
                maskImage.texture = targetTex;
            }

            _heatmapTensor = _worker.CopyOutput("heatmaps");  // CopyOutput
            _offsetsTensor = _worker.CopyOutput("short_offsets");
            _dispFwdTensor = _worker.CopyOutput("displacement_fwd");
            _dispBwdTensor = _worker.CopyOutput("displacement_bwd");
            //Debug.Log("  copy output tensors finished at: " + System.DateTime.Now.ToString("o"));

            //yield return null;

            //Debug.Log("RunModel finished at: " + System.DateTime.Now.ToString("o"));

            // start inference completion in background
            CompleteInferenceInBackground();
        }

        // gets the inference results
        private bool GetResults()
        {
            //Debug.Log("GetResultsRoutine started at: " + System.DateTime.Now.ToString("o"));
            if (_heatmapTensor != null && _offsetsTensor != null && _dispFwdTensor != null && _dispBwdTensor != null)
            {
                // determine the key points
                lock (_keypointLock)
                {
                    //Debug.Log("  decoding multiple poses started at: " + System.DateTime.Now.ToString("o"));
                    _keypoints = PosenetUtils.DecodeMultiplePoses(_heatmapTensor, _offsetsTensor, _dispFwdTensor, _dispBwdTensor, out listAllKeypoints, (int)stride, maxBodies, scoreThreshold, nmsRadius);
                    //Debug.Log("  decoding multiple poses finished at: " + System.DateTime.Now.ToString("o"));
                }

                _heatmapTensor.Dispose();
                _offsetsTensor.Dispose();
                _dispFwdTensor.Dispose();
                _dispBwdTensor.Dispose();

                _heatmapTensor = _offsetsTensor = _dispFwdTensor = _dispBwdTensor = null;

                //Debug.Log("GetResultsRoutine finished at: " + System.DateTime.Now.ToString("o"));
                return true;
            }

            // complete the inference (set ready state)
            //base.CompleteInference();

            //Debug.Log("GetResultsRoutine finished at: " + System.DateTime.Now.ToString("o"));
            //yield return null;
            return false;
        }

    }
}
