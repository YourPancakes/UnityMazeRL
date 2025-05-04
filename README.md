# UnityMazeRL

A Unity-based procedural maze environment designed for reinforcement learning research and experimentation using Unity ML-Agents.

---

## 🔧 Features

* **Procedural Maze Generator**: Uses recursive backtracking with adjustable width, height, and exit count.
* **ML-Agent Integration**: Customizable reward functions, including step penalties, wall collision penalties, and goal-reaching rewards.
* **Dynamic Environment Manager**: Automatically spawns agents, adapts difficulty via performance-based scaling, and captures heuristic-based demonstrations.
* **Editor-Friendly Camera Controller**: Click-and-drag panning with scroll-wheel zoom for easy scene navigation.

---

## 📋 Requirements

* **Unity**: 2020.3 LTS or newer
* **Unity ML-Agents**: v2.x or higher
* **.NET**: 4.x scripting runtime

---

## 🚀 Installation

1. **Clone** this repo:

   ```bash
   git clone https://github.com/<your-username>/UnityMazeRL.git
   ```
2. **Open** `UnityHub` and add the project.
3. **Install** ML-Agents via the Unity Package Manager.
4. **Open** `Assets/Scenes/MazeDemo.unity` and press **Play**.

---

## 🏋️‍♂️ Usage

### Training the Agent

1. Edit `trainer_config.yaml` in the root folder to set hyperparameters.
2. Run training:

   ```bash
   mlagents-learn trainer_config.yaml --run-id=MazeRun01
   ```
3. In Unity Editor, click **Play** to start simulation and training.

### Heuristic Demonstrations

* Enable **Heuristic Only** in the `BehaviorParameters` on `MazeAgent` prefab.
* Run scene and record under `Assets/ML-Agents/Demonstrations`.
