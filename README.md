# UnityMazeRL

A Unity-based procedural maze environment designed for reinforcement learning research and experimentation using Unity ML-Agents.

---

## 🔧 Features

* **Procedural Maze Generator**: Uses recursive backtracking with adjustable width, height, and exit count.
* **ML-Agent Integration**: Customizable reward functions, including step penalties, wall collision penalties, and goal-reaching rewards.
* **Dynamic Environment Manager**: Automatically spawns agents, adapts difficulty via performance-based scaling, and captures heuristic-based demonstrations.

---

## 📋 Requirements

* **Unity**: 2020.3 LTS or newer
* **Unity ML-Agents**: v3.0.0
* **.NET**: 4.x scripting runtime

---

## 🏋️‍♂️ Usage

### Training the Agent

1. Edit `trainer_config.yaml` in the root folder to set hyperparameters.
2. Run training:

   ```bash
   mlagents-learn maze_trainer.yaml --run-id=MazeRun01
   ```
3. In Unity Editor, click **Play** to start simulation and training.

### Heuristic Demonstrations

* Enable **Heuristic Only** in the `BehaviorParameters` on `MazeAgent` prefab.
* Run scene and record under `Assets/ML-Agents/Demonstrations`.
