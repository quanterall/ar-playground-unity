using UnityEngine;
using UnityEngine.UI;

namespace com.quanterall.arplayground
{
    /// <summary>
    /// CameraInput is the component that deals with the web-camera input, or with external texture input.
    /// </summary>
    public sealed class CameraInput : MonoBehaviour
    {
        [Tooltip("Web-camera device name. If empty, the first web camera will be selected.")]
        public string deviceName = string.Empty;

        [Tooltip("Whether the camera should be front- or back-facing.")]
        public bool frontFacing = false;

        [Tooltip("Requested web-camera resolution.")]
        public Vector2Int reqResolution = Vector2Int.zero;

        [Tooltip("External texture to be used instead of the web-camera as image input.")]
        public Texture externalTexture = null;

        //[Tooltip("UI RawImage to display the source texture.")]
        //public RawImage cameraImage;

        [Tooltip("UI text to display information messages.")]
        public Text infoText;


        // web camera, texture & image props
        private WebCamTexture _webcam = null;
        //private AspectRatioFitter _camImageAspect = null;
        private RenderTexture _texture = null;
        private Material _textureMat = null;
        private long _lastUpdateTime = 0;
        private long _lastProcessTime = 0;


        /// <summary>
        /// The latest web-camera texture.
        /// </summary>
        public RenderTexture Texture => _texture;

        /// <summary>
        /// The last texture update time.
        /// </summary>
        public long LastUpdateTime => _lastUpdateTime;

        /// <summary>
        /// The last texture process time. Should be equal to the update time, in order to get the next image.
        /// </summary>
        public long LastProcessTime
        {
            get
            {
                return _lastProcessTime;
            }

            set
            {
                _lastProcessTime = value;
            }
        }

        // invoked by the toggle-camera button
        public void ToggleCameraFrontBack()
        {
            if(_webcam)
            {
                StopCamera();
                StartCamera(string.Empty, !frontFacing);
            }
        }


        void Start()
        {
            //if (cameraImage)
            //{
            //    _camImageAspect = cameraImage.gameObject.GetComponent<AspectRatioFitter>();
            //    if(_camImageAspect == null)
            //    {
            //        _camImageAspect = cameraImage.gameObject.GetComponentInParent<AspectRatioFitter>();
            //    }
            //}

            if(_textureMat == null)
            {
                _textureMat = new Material(Shader.Find("Custom/ResizeTexShader"));
            }

            if (externalTexture)
            {
                _texture = new RenderTexture(externalTexture.width, externalTexture.height, 0);

                //if (cameraImage)
                //{
                //    cameraImage.texture = _texture;

                //    if(_camImageAspect)
                //    {
                //        _camImageAspect.aspectRatio = (float)_texture.width / _texture.height;
                //    }
                //}

                return;
            }

            if (string.IsNullOrEmpty(deviceName))
            {
                // find the first webcam device
                var webcamDevices = WebCamTexture.devices;
                foreach (var device in webcamDevices)
                {
                    StartCamera(device.name, device.isFrontFacing);
                    break;
                }
            }
            else
            {
                // try to find the requested camera
                StartCamera(deviceName, frontFacing);
            }

            if (string.IsNullOrEmpty(deviceName))
            {
                string sInfoMessage = "Can't find the web camera!";
                Debug.LogError(sInfoMessage);

                if (infoText)
                    infoText.text = sInfoMessage;
                return;
            }
        }

        void OnDestroy()
        {
            StopCamera();
        }

        void Update()
        {
            // check, whether the last image is processed or not
            if (_lastUpdateTime != _lastProcessTime)
                return;

            if(externalTexture)
            {
                // blit the external texture
                Graphics.Blit(externalTexture, _texture);
            }
            else if (_webcam && _webcam.didUpdateThisFrame)
            {
                BlitCameraTexture();

                //// check the texture size
                ////bool reqResValid = reqResolution.x != 0 && reqResolution.y != 0;
                ////int camWidth = reqResValid ? reqResolution.x : _webcam.width;
                ////int camHeight = reqResValid ? reqResolution.y : _webcam.height;

                //GetCameraResolution(out int camWidth, out int camHeight, out bool vflip, out bool hflip,
                //    out float aspect1, out float aspect2, out float gap);
                ////Debug.Log("Camera width: " + camWidth + ", height: " + camHeight + ", vflip: " + vflip + ", hflip: " + hflip);

                //if (_texture == null || _texture.width != camWidth || _texture.height != camHeight)
                //{
                //    if (_texture)
                //    {
                //        _texture.Release();
                //        Utils.Destroy(_texture);
                //    }

                //    _texture = new RenderTexture(camWidth, camHeight, 0);

                //    if (cameraImage)
                //    {
                //        cameraImage.texture = _texture;
                //    }
                //}

                ////var aspect1 = (float)_webcam.width / _webcam.height;
                ////var aspect2 = reqResValid ? ((float)reqResolution.x / reqResolution.y) : aspect1;
                ////var gap = aspect2 / aspect1;

                //// check the aspect ratio
                //if (_camImageAspect && _camImageAspect.aspectRatio != aspect2)
                //{
                //    _camImageAspect.aspectRatio = aspect2;
                //}

                ////var vflip = _webcam.videoVerticallyMirrored;

                //var scale = new Vector2(hflip ? -gap : gap, vflip ? -1f : 1f);
                //var offset = new Vector2(hflip ? (1f + gap) / 2f : (1f - gap) / 2f, vflip ? 1f : 0);

                //// get the texture to process
                //Graphics.Blit(_webcam, _texture, scale, offset);

                //if (infoText)
                //{
                //    //infoText.text = string.Format("Res: {1}x{2}, HM: {3}, VM: {4} - {0}", deviceName, camWidth, camHeight, hflip, vflip);
                //    //infoText.text = string.Format("Res: {1}x{2}, SO: {3}, VM: {4} - {0}", deviceName, Screen.width, Screen.height, Screen.orientation, vflip);
                //}
            }

            _lastUpdateTime = System.DateTime.UtcNow.Ticks;
            _lastProcessTime = _lastUpdateTime;
        }


        // finds and starts the requested camera, if possible
        private void StartCamera(string reqDeviceName, bool reqFrontFacing)
        {
            //deviceName = string.Empty;

            var webcamDevices = WebCamTexture.devices;
            foreach (var device in webcamDevices)
            {
                if(!string.IsNullOrEmpty(reqDeviceName))
                {
                    // check for requested device name
                    if(device.name == reqDeviceName)
                    {
                        deviceName = device.name;
                        frontFacing = device.isFrontFacing;
                        break;
                    }
                }
                else
                {
                    // check for requested front/back facing
                    if(device.isFrontFacing == reqFrontFacing)
                    {
                        deviceName = device.name;
                        frontFacing = device.isFrontFacing;
                        break;
                    }
                }
            }

            Debug.Log(string.Format("Selected camera: '{0}', front-facing: {1}", deviceName, frontFacing));
            if (string.IsNullOrEmpty(deviceName))
                return;

            if (reqResolution != Vector2Int.zero)
            {
                _webcam = new WebCamTexture(deviceName, reqResolution.x, reqResolution.y);
            }
            else
            {
                _webcam = new WebCamTexture(deviceName);
            }

            _webcam.Play();
        }

        // stops the camera, if running
        private void StopCamera()
        {
            if (_webcam)
            {
                _webcam.Stop();
                Utils.Destroy(_webcam);
                _webcam = null;
            }

            if (_texture)
            {
                _texture.Release();
                Utils.Destroy(_texture);
                _texture = null;
            }
        }

        // estimates the camera resolution
        private void GetCameraResolution(out int camWidth, out int camHeight, out bool vflip, out bool hflip, 
            out float aspect1, out float aspect2, out float gap)
        {
            bool reqResValid = reqResolution.x != 0 && reqResolution.y != 0;
            camWidth = reqResValid ? reqResolution.x : _webcam.width;
            camHeight = reqResValid ? reqResolution.y : _webcam.height;

            vflip = _webcam ? _webcam.videoVerticallyMirrored : false;
            hflip = frontFacing;

            aspect1 = (float)_webcam.width / _webcam.height;
            aspect2 = (float)camWidth / camHeight;

#if UNITY_ANDROID || UNITY_IOS
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeLeft:
                    break;

                case ScreenOrientation.LandscapeRight:
                    vflip = !vflip;
                    hflip = !hflip;

                    aspect1 = (float)_webcam.height / _webcam.width;
                    aspect2 = (float)camHeight / camWidth;
                    break;

                case ScreenOrientation.Portrait:
                    var temp = camWidth; camWidth = camHeight; camHeight = temp;
                    break;

                case ScreenOrientation.PortraitUpsideDown:
                    temp = camWidth; camWidth = camHeight; camHeight = temp;
                    vflip = !vflip;
                    hflip = !hflip;

                    aspect1 = (float)_webcam.height / _webcam.width;
                    aspect2 = (float)camHeight / camWidth;
                    break;
            }
#endif

            gap = aspect2 / aspect1;
        }

        // blits the camera texture to target texture
        private void BlitCameraTexture()
        {
            int camWidth = _webcam.width;
            int camHeight = _webcam.height;

            int camRotation = _webcam.videoRotationAngle;
            bool isPortraitMode = camRotation == 90 || camRotation == 270;

            bool reqResValid = reqResolution.x != 0 && reqResolution.y != 0;
            int tgtWidth = reqResValid ? reqResolution.x : Screen.width;
            int tgtHeight = reqResValid ? reqResolution.y : Screen.height;

            if (isPortraitMode)
            {
                // swap width and height
                (camWidth, camHeight) = (camHeight, camWidth);
            }

            float camAspect = (float)camWidth / camHeight;
            float tgtAspect = (float)tgtWidth / tgtHeight;

            if (_texture == null || _texture.width != tgtWidth || _texture.height != tgtHeight)
            {
                if (_texture)
                {
                    Utils.Destroy(_texture);
                }

                _texture = new RenderTexture(tgtWidth, tgtHeight, 0, RenderTextureFormat.ARGB32);

                //if (cameraImage)
                //{
                //    cameraImage.texture = _texture;
                //}
            }

            // fix bug on Android
            if (Application.platform == RuntimePlatform.Android)
            {
                camRotation = -camRotation;
            }

            Matrix4x4 transMat = Matrix4x4.identity;
            Vector4 texST = Vector4.zero;

            if (isPortraitMode)
            {
                transMat = GetTransformMat(camRotation, _webcam.videoVerticallyMirrored, frontFacing);
                texST = GetTexST(tgtAspect, camAspect);
            }
            else
            {
                transMat = GetTransformMat(camRotation, frontFacing, _webcam.videoVerticallyMirrored);
                texST = GetTexST(camAspect, tgtAspect);
            }

            //// check the aspect ratio
            //if (_camImageAspect && _camImageAspect.aspectRatio != tgtAspect)
            //{
            //    _camImageAspect.aspectRatio = tgtAspect;
            //}

            // blit the texture
            _textureMat.SetMatrix(_TransformMatParam, transMat);
            _textureMat.SetVector(_TexSTParam, texST);

            Graphics.Blit(_webcam, _texture, _textureMat, 0);
        }

        private static readonly int _TransformMatParam = Shader.PropertyToID("_TransformMat");
        private static readonly int _TexSTParam = Shader.PropertyToID("_TexST");

        // returns the camera transform matrix
        private Matrix4x4 GetTransformMat(float rotation, bool mirrorHorizontal, bool mirrorVertical)
        {
            Vector3 scale = new Vector3(mirrorHorizontal ? -1f : 1f, mirrorVertical ? -1f : 1f, 1f);
            Matrix4x4 mat = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, rotation), scale);

            return PUSH_MATRIX * mat * POP_MATRIX;
        }

        private static readonly Matrix4x4 PUSH_MATRIX = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
        private static readonly Matrix4x4 POP_MATRIX = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0));

        // returns the camera texture offsets
        private Vector4 GetTexST(float srcAspect, float dstAspect)
        {
            if (srcAspect > dstAspect)
            {
                float s = dstAspect / srcAspect;
                return new Vector4(s, 1, (1 - s) / 2, 0);
            }
            else
            {
                float s = srcAspect / dstAspect;
                return new Vector4(1, s, 0, (1 - s) / 2);
            }
        }

    }
}
