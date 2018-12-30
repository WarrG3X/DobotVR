# DobotVR
This is a Unity App to control the Dobot-Magician Robotic Arm using an Oculus-Rift.
This is an extension and meant to be used along with the [gym-dobot](https://github.com/WarrG3X/gym-dobot) environment.
It was made using the [Mujoco Unity Integration Plugin](http://www.mujoco.org/book/unity.html).
The main use-case is to collect human demonstrations with the Oculus which can be used for Imitation/Reinforcement Learning.


![dobot-vr-gif](https://github.com/WarrG3X/DobotVR/blob/master/Misc/dobot-vr.gif)

## Installation
 - Firstly follow instructions to setup [gym-dobot](https://github.com/WarrG3X/gym-dobot) along with its dependencies. 
 - Install Unity and make sure the Oculus is setup.
 - Clone this repo and open the project in Unity. You can directly launch the SampleScene or you may build an executable and then launch it.
 
 
 
**NOTE -** This is not a standalone application. It doesn't replace Mujoco but is meant to be used along with it. For more information on how the Mujoco Unity Plugin works, refer [this](http://www.mujoco.org/book/unity.html).

## Usage Workflow
General sequence for usage - 
 1. Start the Unity App from the Unity Editor or from the built executable. The default address is `127.0.0.1:1050`. This can be changed by selecting the `MuJoCo` object in the Unity Editor and changing the IP/Port in the Inspector.
 
 2. Start gym-dobot by `python -m gym_dobot.run_env --unity_remote=1`. If you changed the default IP/Port in Unity, make the corresponding changes in `gym-dobot/gym_dobot/envs/mjremote.py` in the `connect()` function. You can run the Unity App and Mujoco Simulation on different systems but this can cause severe latency so running both on localhost is recommended. **NOTE -** Currently the app only supports the `DobotClutterPickAndPlaceEnv`. Other environments won't work.
 
 3. You may change the number of blocks by passing the `clutter` parameter. If the simulation is too slow, disable Mujoco rendering. Eg - `python -m gym_dobot.run_env --unity_remote=1 --clutter=15 --render=0`
 
 4. Initially the Unity will app show Waiting for Connection. But once Mujoco connects, a counter will be displayed on front and the simulation will start running. The counter corresponds to the number of steps in the Mujoco simulation before the environment resets. This can be changed by passing the `steps` parameter when launching python.
 
 5. The Arm is moved by tracking the Right Controller. You may have to adjust your position and test around a bit to obtain optimal movement. Press Right Index Trigger for grippers. Left Controller Thumbstick moves the camera. Press the the left thumbstick button to reset camera to default position.

6. At the end of an episode, simulation will be paused with the prompt “Save Demo?” with options (A) Yes, (B) No, (X) Quit. Press the appropriate button as desired. Then sim will reset and next ep will start if the player did not quit. If the next episode starts that means no error occured and file was successfully saved if (A) was pressed. This can be verified by looking at the Anaconda Terminal which will print a message ‘Saved demo….npz”.

7. All demos are saved in the `gym-dobot/gym_dobot/envs/Demos` folder. Each demo is saved as a separate file with a timestamp which contains the action, observation, info value for each step in an episode.

8. After all demos have been taken, they need to be merged into a single file. This can be done using the `demo_merge.py` script in the `envs` folder. This script will parse all `.npz` files in the `Demos` folder and save a merged timestamped `.npz` file in the `Demos/Merged/` folder.



## Notes
 - In case you need to edit the Unity Env, main logic is in `MJRemote.cs`. This script was originally provided as a part of the Mujoco Unity Plugin but was modified for Oculus support. You can compare it with `MJRemoteOriginal.cs` script in the `Misc` directory to understand the modifications.
 - Don’t save a demo the very first time the simulation runs as there is usually a brief stutter when it connects for the first time.

