using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.quanterall.arplayground
{
    public interface IPredictor
    {
        /// <summary>
        /// Gets the predictor name.
        /// </summary>
        /// <returns></returns>
        string GetPredictorName();

        /// <summary>
        /// Initializes the predictor's model and worker.
        /// </summary>
        /// <returns></returns>
        bool InitPredictor();

        /// <summary>
        /// Releases the resources used by the predictor.
        /// </summary>
        void FinishPredictor();

        /// <summary>
        /// Starts predictor's inference on the given image.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="cameraFrameTime"></param>
        /// <returns></returns>
        bool StartInference(Texture texture, long cameraFrameTime);

        /// <summary>
        /// Completes the last started inference.
        /// </summary>
        /// <returns></returns>
        bool CompleteInference();

        /// <summary>
        /// Checks whether the last inference is ready or not.
        /// </summary>
        /// <returns></returns>
        bool IsInferenceReady();

        /// <summary>
        /// Tries to get the last inference results in the main thread.
        /// </summary>
        /// <param name="controller"></param>
        /// <returns></returns>
        bool TryGetResults(PlaygroundController controller);

        /// <summary>
        /// Displays the inference results on screen.
        /// </summary>
        /// <param name="controller"></param>
        void DisplayInferenceResults(PlaygroundController controller);

        /// <summary>
        /// Displays the results-related GUI (labels, etc.) on screen.
        /// </summary>
        /// <param name="controller"></param>
        void DisplayResultsGUI(PlaygroundController controller);
    }
}
