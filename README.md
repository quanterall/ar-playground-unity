# AR-Playground for Unity

## Introduction

This is a free AR-model playground for Unity, courtesy of Quanterall Ltd. It works with different AR models and predictors, and requires Unity 2020.3.0f1 or later. To see it in action, please open and run the 'ARPlayground'-scene in 'ARPlayground/DemoScenes'-folder. Then you can turn on and off the available AR predictors, to see their results and how they affect the overall performance (FPS).

The AR predictors in the scene are set up as components of the child objects of PlaygroundController-game object.

So far the asset contains the following predictors:
* FaceDetectionBlazePredictor - provides face detection using the Blaze-Face model. For more information about the model, please look at [MediaPipe BlazeFace](https://sites.google.com/view/perception-cv4arvr/blazeface).
* FaceDetectionUltraPredictor - provides face detection using the Blaze-Face model. For more information about the model, please look at [UltraFace](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB).
* BodyTrackingPosenetPredictor - provides body tracking using the Posenet model. For more information about the model, please look at [PoseNet](https://medium.com/tensorflow/real-time-human-pose-estimation-in-the-browser-with-tensorflow-js-7dd0bc881cd5).
* BodyTrackingBodypixPredictor - provides body tracking using the BodyPix model. For more information about the model, please look at [BodyPix](https://blog.tensorflow.org/2019/11/updated-bodypix-2.html).

We hope you'll enjoy the tool, and put it into good use.

## Documentation

The online documentation is yet to come.

## License

[MIT License](LICENSE)

