using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class BodySegmentationSelfiePredictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Tooltip("RawImage to display the body segmentation mask")]
        public UnityEngine.UI.RawImage maskImage;

        /// <summary>
        /// Event, invoked when the body segmentation is estimated (time, segmentationTex)
        /// </summary>
        public event System.Action<long, RenderTexture> OnBodySegmentation;


        // compute shaders
        private ComputeShader preprocessShader = null;
        private ComputeShader postprocessShader = null;

        // the input texture
        private RenderTexture _texture;


        private int _inputWidth = 0;
        private int _inputHeight = 0;

        private ComputeBuffer _preprocessBuf;
        private RenderTexture _inferenceTex;
        private RenderTexture _temp1Tex;
        private RenderTexture _temp2Tex;
        private RenderTexture _outputTex;


        private IWorker _worker = null;
        //private IEnumerator _routine = null;


        protected override void OnEnable()
        {
            base.OnEnable();

            if (maskImage)
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
            return "Body segmentation (selfie)";
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
                var inShape = nnModel.inputs[0].shape; // NHWC

                _inputWidth = inShape[6];
                _inputHeight = inShape[5];

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker();

                // load compute shaders
                preprocessShader = Resources.Load("SelfiePreprocessShader") as ComputeShader;
                postprocessShader = Resources.Load("SelfiePostprocessShader") as ComputeShader;

                // Buffer & tex allocation
                _preprocessBuf = new ComputeBuffer(_inputWidth * _inputHeight * 3, sizeof(float));
                _inferenceTex = Utils.CreateFloat1RT(_inputWidth, _inputHeight);
                _temp1Tex = Utils.CreateRFloatUavRT(_inputWidth, _inputHeight);
                _temp2Tex = Utils.CreateRFloatUavRT(_inputWidth, _inputHeight);
                _outputTex = Utils.CreateR8UavRT(_inputWidth, _inputHeight);

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

                _preprocessBuf?.Dispose();
                _preprocessBuf = null;

                Utils.Destroy(_inferenceTex);
                _inferenceTex = null;

                Utils.Destroy(_temp1Tex);
                _temp1Tex = null;

                Utils.Destroy(_temp2Tex);
                _temp2Tex = null;

                Utils.Destroy(_outputTex);
                _outputTex = null;
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
            StartCoroutine(RunModelRoutine(texture));

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
            // invoke the event
            OnBodySegmentation?.Invoke(inferenceFrameTime, _outputTex);

            return true;
        }

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        public override void DisplayInferenceResults(PlaygroundController controller)
        {
        }


        // runs the inference model
        private IEnumerator RunModelRoutine(Texture source)
        {
            // create or recreate the texture
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputHeight)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(_inputWidth, _inputHeight, 0);
                //Debug.Log("Creating texture " + _inputWidth + " x " + _inputHeight);

                if(maskImage)
                {
                    maskImage.texture = _outputTex;
                }
            }

            // get the texture to process
            var scale = GetBlitScale(source, _texture);
            var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture);  //, scale, offset);

            // Preprocessing
            var pre = preprocessShader;
            if (pre != null)
            {
                pre.SetInts("_Dimensions", _inputWidth, _inputHeight);

                pre.SetTexture(0, "_InputTexture", _texture);
                pre.SetBuffer(0, "_OutputBuffer", _preprocessBuf);
                pre.DispatchThreads(0, _inputWidth, _inputHeight, 1);
            }

            // NNworker inference
            //TensorShape inputShape = new TensorShape(1, _inputHeight, _inputWidth, 3);
            //using (var t = new Tensor(inputShape, _preprocessBuf))
            //    _worker.Execute(t);

            yield return _worker.ExecuteAndWaitForResult(this, _preprocessBuf, _inputWidth, _inputWidth);

            ////get results in coroutine
            //StartCoroutine(GetResultsRoutine());

            // NN output retrieval
            _worker.PeekOutput().ToRenderTexture(_inferenceTex);

            var post = postprocessShader;
            if (post != null)
            {
                // erosion
                post.SetInts("_Dimensions", _inputWidth, _inputHeight);

                post.SetTexture(0, "_Inference", _inferenceTex);
                post.SetTexture(0, "_MaskOutput", _temp1Tex);
                post.DispatchThreads(0, _inputWidth, _inputHeight, 1);

                //yield return null;

                // horizontal bilateral filter
                post.SetTexture(1, "_MaskInput", _temp1Tex);
                post.SetTexture(1, "_MaskOutput", _temp2Tex);
                post.DispatchThreads(1, _inputWidth, _inputHeight, 1);

                // vertical bilateral filter
                post.SetTexture(2, "_MaskInput", _temp2Tex);
                post.SetTexture(2, "_MaskOutput", _outputTex);
                post.DispatchThreads(2, _inputWidth, _inputHeight, 1);
            }

            // complete the inference (set ready state)
            base.CompleteInference();
        }

        //// gets the inference results
        //private IEnumerator GetResultsRoutine()
        //{
        //    // NN output retrieval
        //    _worker.PeekOutput().ToRenderTexture(_inferenceTex);

        //    var post = postprocessShader;
        //    if (post != null)
        //    {
        //        // erosion
        //        post.SetInts("_Dimensions", _inputWidth, _inputHeight);

        //        post.SetTexture(0, "_Inference", _inferenceTex);
        //        post.SetTexture(0, "_MaskOutput", _temp1Tex);
        //        post.DispatchThreads(0, _inputWidth, _inputHeight, 1);

        //        yield return null;

        //        // horizontal bilateral filter
        //        post.SetTexture(1, "_MaskInput", _temp1Tex);
        //        post.SetTexture(1, "_MaskOutput", _temp2Tex);
        //        post.DispatchThreads(1, _inputWidth, _inputHeight, 1);

        //        // vertical bilateral filter
        //        post.SetTexture(2, "_MaskInput", _temp2Tex);
        //        post.SetTexture(2, "_MaskOutput", _outputTex);
        //        post.DispatchThreads(2, _inputWidth, _inputHeight, 1);
        //    }

        //    // complete the inference (set ready state)
        //    base.CompleteInference();
        //}

    }
}
