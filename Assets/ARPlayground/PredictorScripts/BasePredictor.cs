using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public abstract class BasePredictor : MonoBehaviour, IPredictor
    {
        [Tooltip("Whether to run the predictor in background, whenever possible, or not.")]
        //[HideInInspector]
        public bool workInBackground = false;

        // thread and synchronization events
        private Thread predThread = null;
        private AutoResetEvent threadStopEvent = new AutoResetEvent(false);
        private AutoResetEvent threadWaitEvent = new AutoResetEvent(false);

        private bool predThreadStopping = false;
        protected bool isInferenceReady = true;  // to allow the first inference


        protected virtual void OnEnable()
        {
            //InitPredictor();
        }

        protected virtual void OnDisable()
        {
            //FinishPredictor();
        }

        // dummy update method (to prevent hiding of the enabled-checkbox)
        public void Update()
        {
        }


        // returns the prediction name
        public abstract string GetPredictorName();

        /// <summary>
        /// Initializes the predictor's model and worker.
        /// </summary>
        /// <returns></returns>
        public virtual bool InitPredictor()
        {
            if(workInBackground)
            {
                predThread = new Thread(() => PredictorThreadProc());
                predThread.Name = GetPredictorName();
                predThread.IsBackground = true;
                predThread.Start();
            }

            return true;
        }

        /// <summary>
        /// Releases the resources used by the predictor.
        /// </summary>
        public virtual void FinishPredictor()
        {
            if(predThread != null)
            {
                string predThreadName = predThread.Name;
                //Debug.Log("Stopping thread: " + predThreadName);

                // stop the frame-polling thread
                predThreadStopping = true;
                threadWaitEvent.Set();
                threadStopEvent.Set();

                predThread.Join();
                predThread = null;

                threadWaitEvent.Dispose();
                threadWaitEvent = null;

                threadStopEvent.Dispose();
                threadStopEvent = null;

                //Debug.Log("Thread stopped: " + predThreadName);
            }
        }

        // thread sleep time in milliseconds (to balance between cpu usage and performance)
        internal const int THREAD_SLEEP_TIME_MS = 5;

        // pedictor thread procedure
        private void PredictorThreadProc()
        {
            while (!threadStopEvent.WaitOne(0))
            {
                // wait for the awake-event
                if (!predThreadStopping)  // avoid thread-stop blocking in debug mode
                    threadWaitEvent.WaitOne();

                if (!predThreadStopping)
                {
                    try
                    {
                        while(!CompleteInference())
                        {
                            Thread.Sleep(THREAD_SLEEP_TIME_MS);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError(ex);
                        SetInferenceReady(true);  // fix the issue stopping future inferences after error
                    }
                }
            }
        }

        /// <summary>
        /// Starts predictor's inference on the given image.
        /// </summary>
        /// <param name="texture"></param>
        /// <returns></returns>
        public virtual bool StartInference(Texture texture)
        {
            //Debug.Log("Inference starting at: " + System.DateTime.Now.ToString("o"));
            isInferenceReady = false;

            return true;
        }

        /// <summary>
        /// Unlocks the background worker thread, to complete the inference.
        /// </summary>
        public void CompleteInferenceInBackground()
        {
            if (predThread != null)
            {
                threadWaitEvent.Set();  // awake the thread
            }
        }

        /// <summary>
        /// Completes the last started inference.
        /// </summary>
        public virtual bool CompleteInference()
        {
            //Debug.Log("Inference completed at: " + System.DateTime.Now.ToString("o"));
            isInferenceReady = true;

            return true;
        }

        /// <summary>
        /// Checks whether the last inference is ready or not.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsInferenceReady()
        {
            return isInferenceReady;
        }

        /// <summary>
        /// Sets the inference-ready flag.
        /// </summary>
        /// <param name="bInferenceReady"></param>
        public void SetInferenceReady(bool bInferenceReady)
        {
            isInferenceReady = bInferenceReady;
            //Debug.Log("InferenceReady set to " + bInferenceReady);
        }

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        public virtual void DisplayInferenceResults(PlaygroundController controller)
        {
        }


        // estimates the scale for blitting the source texture into a target texture
        public Vector2 GetBlitScale(Texture fromTexture, Texture toTexture)
        {
            if (fromTexture == null || toTexture == null)
                return Vector2.one;

            var aspect1 = (float)fromTexture.width / fromTexture.height;
            var aspect2 = (float)toTexture.width / toTexture.height;
            var gap = aspect2 / aspect1;

            return new Vector2(gap, 1f);
        }

        // estimates the offset for blitting the source texture into a target texture
        public Vector2 GetBlitOffset(Texture fromTexture, Texture toTexture)
        {
            if (fromTexture == null || toTexture == null)
                return Vector2.zero;

            var aspect1 = (float)fromTexture.width / fromTexture.height;
            var aspect2 = (float)toTexture.width / toTexture.height;
            var gap = aspect2 / aspect1;

            return new Vector2((1f - gap) / 2f, 0);
        }

    }
}
