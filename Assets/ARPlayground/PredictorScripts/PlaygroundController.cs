using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace com.quanterall.arplayground
{
    public class PlaygroundController : MonoBehaviour
    {
        [Tooltip("Web-camera or texture input.")]
        public CameraInput cameraInput;

        [Tooltip("UI RawImage to display the source texture.")]
        public RawImage cameraImage;

        [Tooltip("Whether to sync the camera image with the predictors' inference results or not.")]
        public bool syncImageWithResults = true;

        [Tooltip("UI text to display information messages.")]
        public Text infoText;

        [Tooltip("UI text to display FPS information.")]
        public Text fpsText;

        [Tooltip("UI panel to show the available predictors.")]
        public RectTransform predictorsPanel;

        [Tooltip("Predictor toggle prefab.")]
        public GameObject predTogglePrefab;


        // list of the available predictors
        private List<BasePredictor> _predInterfaces = new List<BasePredictor>();
        private Dictionary<string, BasePredictor> _dictPredictors = new Dictionary<string, BasePredictor>();

        // camera-image aspect ratio
        private AspectRatioFitter _camImageAspect = null;

        // texture to display on screen
        private RenderTexture _texture = null;

        // the last camera frame timestamps
        private long _lastCameraFrameTime = 0;
        //private long lastResultFrameTime = 0;

        // fps estimation
        private float _lastUpdateTime = 0f;
        private float _fpsEstimation = 0f;


        void Awake()
        {
            InitPredictors();

            if(!cameraInput)
            {
                cameraInput = GetComponent<CameraInput>();
            }

            if (cameraImage)
            {
                _camImageAspect = cameraImage.gameObject.GetComponent<AspectRatioFitter>();
                if (_camImageAspect == null)
                {
                    _camImageAspect = cameraImage.gameObject.GetComponentInParent<AspectRatioFitter>();
                }
            }
        }

        void OnDestroy()
        {
            if(_texture)
            {
                Utils.Destroy(_texture);
            }

            FinishPredictors();
        }

        void Update()
        {
            if (!cameraInput)
                return;

            // these flags need to be estimated before starting the inferences
            // bool bAllPredReady = IsAllPredictorsReady();
            bool bAnyPredReady = IsAnyPredictorReady();

            // estimate fps
            //if (bAllPredReady)
            {
                float currentTime = Time.time;
                if (currentTime != _lastUpdateTime)
                {
                    float currentFps = 1f / (currentTime - _lastUpdateTime);
                    _fpsEstimation = _fpsEstimation != 0f ? Mathf.Lerp(_fpsEstimation, currentFps, Time.deltaTime) : currentFps;

                    if (fpsText)
                    {
                        fpsText.text = string.Format("{0:F0} FPS", _fpsEstimation);
                    }

                    _lastUpdateTime = Time.time;
                    //Debug.Log(string.Format("Update frame time: {0:F3} s", currentTime));
                }
            }

            // checks if all predictors are ready
            long curCameraFrameTime = cameraInput.LastUpdateTime;

            if (_lastCameraFrameTime != curCameraFrameTime /**&& bAllPredReady*/)
            {
                // do predictor inferences
                _lastCameraFrameTime = curCameraFrameTime;
                StartPredictorInferences(cameraInput.Texture);
            }

            // complete the inferences, if needed
            CompletePredictorInferences();

            // update the camera image
            bool bUpdateCamImage = !syncImageWithResults || bAnyPredReady;

            Texture srcTexture = cameraInput.Texture;
            if (cameraImage && srcTexture && bUpdateCamImage)
            {
                if (_texture == null || _texture.width != srcTexture.width || _texture.height != srcTexture.height)
                {
                    if (_texture != null)
                        Utils.Destroy(_texture);

                    _texture = new RenderTexture(srcTexture.width, srcTexture.height, 0, RenderTextureFormat.ARGB32);
                    cameraImage.texture = _texture;
                }

                Graphics.CopyTexture(srcTexture, _texture);
            }

            // check the aspect ratio
            if(_texture)
            {
                float tgtAspect = (float)_texture.width / _texture.height;
                if (_camImageAspect && _camImageAspect.aspectRatio != tgtAspect)
                {
                    _camImageAspect.aspectRatio = tgtAspect;
                }
            }
        }

        void OnRenderObject()
        {
            // display current results
            DisplayAllResults();
        }


        // initializes the available predictors
        private void InitPredictors()
        {
            try
            {
                List<BasePredictor> predictorInts = new List<BasePredictor>();
                predictorInts.AddRange(gameObject.GetComponents<BasePredictor>());
                predictorInts.AddRange(gameObject.GetComponentsInChildren<BasePredictor>());

                for (int i = 0; i < predictorInts.Count; i++)
                {
                    var predInt = predictorInts[i];

                    if (/**predInt.enabled &&*/ predInt.gameObject.activeSelf)
                    {
                        if(predInt.InitPredictor())
                        {
                            _predInterfaces.Add(predInt);
                            _dictPredictors[predInt.GetPredictorName()] = predInt;
                            AddPredictorToggle(predInt.GetPredictorName(), predInt.enabled);
                            //predInt.FinishPredictor();

                            Debug.Log(string.Format("P{0}: {1} successfully started.", i, predInt.GetType().Name));
                        }
                        else
                        {
                            Debug.Log(string.Format("P{0}: {1} could not be started.", i, predInt.GetType().Name));
                        }
                    }
                    else
                    {
                        Debug.Log(string.Format("P{0}: {1} disabled.", i, predInt.GetType().Name));
                    }
                }
            }
            catch (System.Exception ex)
            {
                string message = ex.Message;
                Debug.LogError(message);
                Debug.LogException(ex);

                if (infoText != null)
                {
                    infoText.text = message;
                }
            }
        }

        // finishes the initialized predictors
        private void FinishPredictors()
        {
            // stop predictors
            for (int i = _predInterfaces.Count - 1; i >= 0; i--)
            {
                try
                {
                    var predInt = _predInterfaces[i];

                    predInt.FinishPredictor();
                    _predInterfaces.RemoveAt(i);

                    Debug.Log(string.Format("P{0}: {1} successfully stopped.", i, predInt.GetType().Name));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                }
            }

            // clear the dictionary
            _dictPredictors.Clear();

            // clear predictors panel
            if(predictorsPanel)
            {
                int predToggleCount = predictorsPanel.childCount;

                for (int t = predToggleCount - 1; t >= 0; t--)
                {
                    var toggleTrans = predictorsPanel.GetChild(t);
                    Destroy(toggleTrans.gameObject);
                }
            }
        }

        // adds predictor toggle to the panel
        private void AddPredictorToggle(string predictorName, bool predEnabled)
        {
            if (!predictorsPanel || !predTogglePrefab)
                return;

            // create predictor toggle from the prefab
            GameObject predToggleObj = Instantiate(predTogglePrefab, predictorsPanel);

            Toggle predToggle = predToggleObj.GetComponent<Toggle>();
            predToggle.isOn = predEnabled;
            predToggle.name = predictorName;

            Text predToggleText = predToggle.GetComponentInChildren<Text>();
            predToggleText.text = predictorName;

            // add listener for when the state of the Toggle changes
            predToggle.onValueChanged.AddListener(delegate {
                ToggleValueChanged(predToggle);
            });
        }

        // ToggleValueChanged updates the enabled-property of the predictor
        void ToggleValueChanged(Toggle change)
        {
            string predName = change.name;  // .GetComponentInChildren<Text>().text;
            //infoText.text = "Value of '" + predName + "' is " + change.isOn;

            if (_dictPredictors.ContainsKey(predName))
            {
                _dictPredictors[predName].enabled = change.isOn;
            }
        }

        // checks whether all predictors are ready or not
        private bool IsAllPredictorsReady()
        {
            foreach(var pred in _predInterfaces)
            {
                if (pred.enabled && !pred.IsInferenceReady())
                    return false;
            }

            return true;
        }

        // checks whether any predictor is ready or not
        private bool IsAnyPredictorReady()
        {
            bool bAnyReady = true;

            foreach (var pred in _predInterfaces)
            {
                if(pred.enabled)
                {
                    if (pred.IsInferenceReady())
                        return true;
                    else
                        bAnyReady = false;
                }
            }

            return bAnyReady;
        }

        // starts inferences of predictors that are in ready state
        private bool StartPredictorInferences(Texture texture)
        {
            if (!texture)
                return false;

            bool bAllStarted = true;
            foreach (var pred in _predInterfaces)
            {
                try
                {
                    if (pred.enabled && pred.IsInferenceReady())
                    {
                        bAllStarted &= pred.StartInference(texture);
                        //Debug.Log("  started inference of: " + pred.GetPredictorName());
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);

                    if(infoText)
                    {
                        infoText.text = ex.Message;
                    }
                }
            }

            return bAllStarted;
        }

        // completes the inferences of non-background predictors
        private bool CompletePredictorInferences()
        {
            bool bAllCompleted = true;

            foreach (var pred in _predInterfaces)
            {
                try
                {
                    if (pred.enabled && !pred.workInBackground && !pred.IsInferenceReady())
                    {
                        bAllCompleted &= pred.CompleteInference();
                        //Debug.Log("  completed inference of: " + pred.GetPredictorName());
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                    pred.SetInferenceReady(true);  // fix the issue stopping further inferences after exception

                    if (infoText)
                    {
                        infoText.text = ex.Message;
                    }
                }
            }

            return bAllCompleted;
        }

        // displays all predictors' inference results
        private void DisplayAllResults()
        {
            foreach (var pred in _predInterfaces)
            {
                try
                {
                    if (pred.enabled)
                    {
                        pred.DisplayInferenceResults(this);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
        }


        // draws point on the image
        public void DrawPoint(float x, float y, float size, Color color, Rect imageRect)
        {
            Vector3 vPoint = GetImagePos(x, y, imageRect);
            Utils.DrawPoint(vPoint, size, color);
        }

        // draws line on the image
        public void DrawLine(float x1, float y1, float x2, float y2, float size, Color color, Rect imageRect)
        {
            Vector3 v1 = GetImagePos(x1, y1, imageRect);
            Vector3 v2 = GetImagePos(x2, y2, imageRect);
            Utils.DrawLine(v1, v2, size, color);
        }

        // draws rectangle on the image
        public void DrawRect(float x, float y, float width, float height, float size, Color color, Rect imageRect)
        {
            Vector3 vPos = GetImagePos(x, y, imageRect);
            Rect rect = new Rect(vPos.x, vPos.y, width * imageRect.width, height * imageRect.height);
            Utils.DrawRect(rect, size, color);
        }

        // converts the normalized clipped-image coordinates to screen corrdinates
        public Vector3 GetImagePos(float x, float y, Rect imageRect)
        {
            //if (imageHMirorred)
            //    x = 1f - x;
            //if (imageVMirorred)
            //    y = 1f - y;

            float rX = imageRect.x + x * imageRect.width;
            float rY = imageRect.y + y * imageRect.height;

            return new Vector3(rX, rY, 0f);
        }

        // returns the raw image rect, in pixels
        public Rect GetImageRect()
        {
            if (cameraImage == null)
                return Rect.zero;

            float aspectRatio = _camImageAspect ? _camImageAspect.aspectRatio : (float)cameraImage.texture.width / cameraImage.texture.height;
            return GetImageRect(aspectRatio);
        }

        // returns the raw image rect, in pixels
        public Rect GetImageRect(float aspectRatio)
        {
            Rect cameraRect = Camera.main ? Camera.main.pixelRect : new Rect(0, 0, Screen.width, Screen.height);

            float rectHeight = cameraRect.height;
            float rectWidth = rectHeight * aspectRatio;

            float rectOfsX = (cameraRect.width - rectWidth) / 2;
            float rectOfsY = (cameraRect.height - rectHeight) / 2;

            Rect imgRect = new Rect(rectOfsX, rectOfsY, rectWidth, rectHeight);

            return imgRect;
        }

    }
}
