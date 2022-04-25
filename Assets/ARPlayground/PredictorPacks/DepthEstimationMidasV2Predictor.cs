using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Barracuda;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public class DepthEstimationMidasV2Predictor : BasePredictor
    {
        [Tooltip("Neural network model to use when performing inference")]
        public NNModel model = null;

        [Tooltip("Worker type to use when performing inference")]
        public WorkerFactory.Type workerType = WorkerFactory.Type.Auto;

        [Tooltip("Minimum distance, in meters")]
        [Range(0, 10)]
        public float minDistance = 0.5f;

        [Tooltip("Maximum distance, in meters")]
        [Range(0, 10)]
        public float maxDistance = 4f;

        [Tooltip("RawImage to display the depth")]
        public UnityEngine.UI.RawImage depthImage;

        [Tooltip("Minimum depth")]
        [Range(0, 10000)]
        private float minDepth = 0f;

        [Tooltip("Maximum depth")]
        [Range(0, 10000)]
        private float maxDepth = 1000f;

        [Tooltip("Log normalization factor")]
        [Range(0, 2)]
        private float logNormFactor = 1f;

        //[Tooltip("Debug text")]
        //public UnityEngine.UI.Text debugText;


        // the input texture
        private RenderTexture _texture;

        // model output
        private string _outputName;
        private Tensor _outputTensor;
        private RenderTexture _outputTex;

        // mask material and texture
        private Material _depthMaskMaterial;
        private RenderTexture _depthMaskTex;

        private static readonly int _MinDepth = Shader.PropertyToID("_MinDepth");
        private static readonly int _MaxDepth = Shader.PropertyToID("_MaxDepth");
        private static readonly int _LogNormFactor = Shader.PropertyToID("_LogNormFactor");


        /// <summary>
        /// Event, invoked when face gets detected (time, count, index, className, normRect, score)
        /// </summary>
        public event System.Action<long, RenderTexture, Tensor> OnDepthEstimated;


        private int _inputWidth = 0;
        private int _inputHeight = 0;

        private IWorker _worker = null;
        //private IEnumerator _routine = null;


        ///// <summary>
        ///// Gets the depth for the given pixel coordinates.
        ///// </summary>
        ///// <param name="pixel">Pixel coordinates</param>
        ///// <param name="imageRect">Image rectangle</param>
        ///// <returns>Distance, in meters</returns>
        //public float GetDepthForPixel(Vector2 pixel, Rect imageRect)
        //{
        //    float nx = Mathf.Clamp01((pixel.x - imageRect.x) / imageRect.width);
        //    float ny = Mathf.Clamp01((pixel.y - imageRect.y) / imageRect.height);

        //    return GetDepthForPixel(new Vector2(nx, ny));
        //}

        /// <summary>
        /// Gets the depth for the given pixel, in normalized coordinates.
        /// </summary>
        /// <param name="normPixel">Normalized pixel coordinates</param>
        /// <returns>Distance, in meters</returns>
        public float GetDepthForPixel(Vector2 normPixel, bool isDebug = false)
        {
            if (_outputTensor != null)
            {
                int x = (int)(Mathf.Clamp01(normPixel.x) * _inputWidth) % _inputWidth;
                int y = (int)(Mathf.Clamp01(normPixel.y) * _inputHeight) % _inputHeight;

                try
                {
                    // barracuda inverts x & y
                    int i = x * _inputWidth + y;
                    float d = _outputTensor[i];

                    if (isDebug)
                    {
                        return d;
                    }

                    d = Mathf.Clamp01((d - minDepth) / (maxDepth - minDepth));
                    //return d;

                    return (minDistance + (1f - d) * (maxDistance - minDistance));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(string.Format("Error looking for depth at ({0}, {1}), size: ({2}, {3})", x, y, _inputWidth, _inputHeight));
                    Debug.LogError(ex);
                }
            }

            return 0f;
        }


        protected override void OnEnable()
        {
            base.OnEnable();

            if (depthImage)
            {
                depthImage.gameObject.SetActive(true);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (depthImage)
            {
                depthImage.gameObject.SetActive(false);
            }
        }


        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        public override string GetPredictorName()
        {
            return "Depth estimation (MiDaS-v2)";
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

                _inputWidth = inShape[6];
                _inputHeight = inShape[5];
                _outputName = nnModel.outputs[0];

                // create worker
                workerType = WorkerFactory.ValidateType(workerType);
                _worker = WorkerFactory.CreateWorker(workerType, nnModel, false);  // nnModel.CreateWorker();

                if(depthImage)
                {
                    // create depth material
                    Shader depthMaskShader = Shader.Find("Custom/DepthMaskShader");
                    if (depthMaskShader)
                    {
                        _depthMaskMaterial = new Material(depthMaskShader);
                    }
                }

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

                _outputTensor?.Dispose();
                _outputTensor = null;

                if (_texture)
                    Utils.Destroy(_texture);
                _texture = null;

                if (_outputTex)
                    Utils.Destroy(_outputTex);
                _outputTex = null;

                if (_depthMaskTex)
                    Utils.Destroy(_depthMaskTex);
                _depthMaskTex = null;

                _depthMaskMaterial = null;
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
            if(controller.displayResults)
            {
                // show depth as mask texture
                if (depthImage && !depthImage.gameObject.activeSelf)
                {
                    depthImage.gameObject.SetActive(true);
                }

                if (_outputTex != null && _depthMaskTex != null)
                {
                    _depthMaskMaterial.SetFloat(_MinDepth, minDepth);
                    _depthMaskMaterial.SetFloat(_MaxDepth, maxDepth);
                    _depthMaskMaterial.SetFloat(_LogNormFactor, logNormFactor);

                    Graphics.Blit(_outputTex, _depthMaskTex, _depthMaskMaterial);
                }
            }
            else
            {
                // hide depth mask
                if (depthImage && depthImage.gameObject.activeSelf)
                {
                    depthImage.gameObject.SetActive(false);
                }
            }

            // invoke the event
            OnDepthEstimated?.Invoke(inferenceFrameTime, _outputTex, _outputTensor);

            //float d5050 = GetDepthForPixel(new Vector2(0.5f, 0.5f));
            //float d2575 = GetDepthForPixel(new Vector2(0.25f, 0.75f));
            //float d7525 = GetDepthForPixel(new Vector2(0.75f, 0.25f));

            ////Debug.Log("Depth(0.5, 0.5): " + d5050 + "\nDepth(0.25, 0.75): " + d2575 + "\nDepth(0.75, 0.25): " + d7525);

            //if (debugText)
            //{
            //    debugText.text = "Depth(0.5, 0.5): " + d5050 + "\nDepth(0.25, 0.75): " + d2575 + "\nDepth(0.75, 0.25): " + d7525 +
            //        "\nCam FL: " + Camera.main.focalLength + ", rect: " + Camera.main.pixelRect;
            //}

            return true;
        }

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        public override void DisplayInferenceResults(PlaygroundController controller)
        {
            //if (depthImage == null || _outputTex == null)
            //    return;

            //Rect imageRect = controller.GetImageRect();

            //controller.DrawPoint(0.5f, 1f - 0.5f, 15f, Color.yellow, imageRect);
            //controller.DrawPoint(0.25f, 1f - 0.75f, 15f, Color.green, imageRect);
            //controller.DrawPoint(0.75f, 1f - 0.25f, 15f, Color.red, imageRect);
        }


        // runs the inference model
        private IEnumerator RunModelRoutine(Texture source)
        {
            // create or recreate the texture
            if (_texture == null || _texture.width != _inputWidth || _texture.height != _inputHeight)
            {
                if (_texture)
                    Utils.Destroy(_texture);

                _texture = new RenderTexture(_inputWidth, _inputHeight, 0, RenderTextureFormat.ARGB32);
                _outputTex = new RenderTexture(_inputWidth, _inputHeight, 0, RenderTextureFormat.RFloat);

                if(depthImage)
                {
                    _depthMaskTex = new RenderTexture(_inputWidth, _inputHeight, 0, RenderTextureFormat.ARGB32);
                    depthImage.texture = _depthMaskTex;
                }

                //Debug.Log("Creating texture " + _inputWidth + " x " + _inputHeight);
            }

            // get the texture to process
            //var scale = GetBlitScale(source, _texture);
            //var offset = GetBlitOffset(source, _texture);
            Graphics.Blit(source, _texture);  //, scale, offset);

            // NNworker inference
            //TensorShape inputShape = new TensorShape(1, _inputHeight, _inputWidth, 3);
            //using (var t = new Tensor(inputShape, _buffers.preBuf))
            //    _worker.Execute(t);

            yield return _worker.ExecuteAndWaitForResult(this, _texture);

            // NN output retrieval
            Tensor outputTensor = _worker.PeekOutput(_outputName).Reshape(new TensorShape(1, _inputHeight, _inputWidth, 1));
            outputTensor.ToRenderTexture(_outputTex);

            _outputTensor?.Dispose();
            _outputTensor = _worker.CopyOutput(_outputName);

            // complete the inference (set ready state)
            base.CompleteInference();
        }

    }
}
