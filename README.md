# AR-Playground for Unity

## Introduction

This is a free AR-model playground for Unity, courtesy of Quanterall Ltd. It works with different AR models and predictors, and requires Unity 2020.3.0f1 or later. To see it in action, please open and run the 'ARPlayground'-scene in 'ARPlayground/DemoScenes'-folder. Then you can turn on and off the available AR predictors, to see their results and how they affect the overall performance (FPS).

The AR predictors in the scene are set up as components of the child objects of PlaygroundController-game object.

So far the asset contains the following predictors:
* FaceDetectionBlazePredictor - provides face detection using the Blaze-Face model. For more information about the model, please look at [MediaPipe BlazeFace](https://sites.google.com/view/perception-cv4arvr/blazeface).
* FaceDetectionUltraPredictor - provides face detection using the Blaze-Face model. For more information about the model, please look at [UltraFace](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB).
* BodyTrackingPosenetPredictor - provides body tracking using the Posenet model. For more information about the model, please look at [PoseNet](https://medium.com/tensorflow/real-time-human-pose-estimation-in-the-browser-with-tensorflow-js-7dd0bc881cd5).
* BodyTrackingBodypixPredictor - provides body tracking using the BodyPix model. For more information about the model, please look at [BodyPix](https://blog.tensorflow.org/2019/11/updated-bodypix-2.html).
* BodySegmentationSelfiePredictor - provides body segmentation using the Selfie model. For more information about the model, please look at [Selfie](https://google.github.io/mediapipe/solutions/selfie_segmentation.html).
* ObjectDetectionYoloV4Predictor - provides object detection using the Yolo-v4 model. For more information about the model, please look at [Yolo-v4](https://arxiv.org/abs/2004.10934) and [yolov4-tiny-keras](https://github.com/bubbliiiing/yolov4-tiny-keras).
* DepthEstimationMidasV2Predictor - provides monocular depth estimation using the MiDaS-v2 model. For more information about the model, please look at [MiDaS-v2](https://arxiv.org/abs/1907.01341v3) and [isl-org-MiDaS](https://github.com/isl-org/MiDaS).
* HandTrackingBlazePredictor - provides lightweight hand detector using the MediaPipe BlazePalm model. For more information about the model, please look at [Real-Time Hand Tracking with MediaPipe](https://ai.googleblog.com/2019/08/on-device-real-time-hand-tracking-with.html) and [MediaPipe Handpose](https://github.com/tensorflow/tfjs-models/tree/master/handpose).

We hope you'll enjoy the tool, and put it into good use.

## Documentation

The online documentation will come later.

## License

[MIT License](LICENSE)

