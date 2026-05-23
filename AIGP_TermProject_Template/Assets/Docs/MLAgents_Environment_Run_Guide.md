# ML-Agents Environment and Run Guide

This guide explains how to install the Python training environment with `requirements-mlagents.txt`, start the ML-Agents trainer, open Unity, and run the training scene.

Project path:

```text
C:\Projects\UnityProject\AIGP_Template
```

Unity version:

```text
6000.0.39f1
```

## 1. Open a Terminal at the Project Root

Use Anaconda Prompt, CMD, or PowerShell.

```cmd
cd C:\Projects\UnityProject\AIGP_Template
```

## 2. Create the Conda Environment

Use Python `3.10.12`. ML-Agents `1.1.0` requires Python `>=3.10.1, <=3.10.12`.

If the environment already exists and you want a clean reinstall:

```cmd
conda deactivate
conda remove -n mlagents-combat --all
```

Create and activate the environment:

```cmd
conda create -n mlagents-combat python=3.10.12
conda activate mlagents-combat
python --version
```

Expected Python version:

```text
Python 3.10.12
```

## 3. Install Python Packages

Install with the fixed requirements file:

```cmd
python -m pip install --upgrade pip
pip install -r requirements-mlagents.txt
```

Check that ML-Agents is available:

```cmd
mlagents-learn --version
```

Expected version information should include:

```text
ml-agents: 1.1.0
ml-agents-envs: 1.1.0
Communicator API: 1.5.0
```

## 4. Start the Trainer First

Run this command before pressing Play in Unity:

```cmd
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test1 --timeout-wait 600
```

What this does:

- Loads `Assets/Config/combat_ppo.yaml`.
- Creates or uses `results/test1`.
- Waits up to 600 seconds for Unity to connect.
- Trains the behavior named `CombatAgent`.

If `test1` already exists, use a new run id:

```cmd
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test2 --timeout-wait 600
```

Or overwrite the existing run:

```cmd
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test1 --force --timeout-wait 600
```

## 5. Open the Unity Editor

Recommended: open the project from Unity Hub.

Alternative command-line launch:

```cmd
"C:\Program Files\Unity\Hub\Editor\6000.0.39f1\Editor\Unity.exe" -projectPath "C:\Projects\UnityProject\AIGP_Template"
```

Wait until Unity finishes importing/compiling.

## 6. Open the Training Scene

In Unity, open:

```text
Assets/Scenes/TermProject_Arena.unity
```

## 7. Check RL Scene Settings Before Play

For RL training, the active scene should be `AgentA` versus `AgentB`.

`AgentA` should be the RL agent:

```text
StudentCombatAgent: ON
BehaviorParameters: ON
DecisionRequester: ON
BaselineAttackerBT: OFF
CombatActionController: ON
CombatHitDetector: ON
CombatAnimatorDriver: ON
```

`AgentA` references:

```text
StudentCombatAgent.self -> AgentA CombatCharacter
StudentCombatAgent.opponent -> AgentB CombatCharacter
StudentCombatAgent.episodeManager -> GameManager EpisodeManager
CombatHitDetector.target -> AgentB CombatCharacter
Behavior Name -> CombatAgent
Behavior Type -> Default
```

`AgentB` should be the BT opponent:

```text
BaselineDefenderBT: ON
StudentCombatAgent: OFF
BehaviorParameters: OFF
DecisionRequester: OFF
CombatActionController: ON
CombatHitDetector: ON
CombatAnimatorDriver: ON
```

`AgentB` references:

```text
BaselineDefenderBT.target -> AgentA CombatCharacter
CombatHitDetector.target -> AgentA CombatCharacter
```

`GameManager` should reference the active agents:

```text
EpisodeManager.agentA -> AgentA CombatCharacter
EpisodeManager.agentB -> AgentB CombatCharacter
Spawn Point A -> assigned
Spawn Point B -> assigned
```

Important rule:

```text
Only one decision path should be active on an agent.
For RL AgentA, keep StudentCombatAgent/BehaviorParameters/DecisionRequester ON and BaselineAttackerBT OFF.
```

## 8. Press Play in Unity

After the Python terminal is waiting, press Play in Unity.

Expected result:

- Unity connects to the Python trainer.
- The Python terminal starts printing training logs.
- AgentA actions are decided by PPO through `StudentCombatAgent`.
- AgentB acts through `BaselineDefenderBT`.

The data flow is:

```text
Unity observations -> Python PPO trainer -> selected action -> Unity combat simulation -> reward -> repeat
```

## 9. Find the Saved Model

Training outputs are saved under:

```text
C:\Projects\UnityProject\AIGP_Template\results\<run-id>
```

For `--run-id=test1`:

```text
C:\Projects\UnityProject\AIGP_Template\results\test1
```

When export completes, look for an `.onnx` file in that run folder.

## 10. Common Errors

### Previous data from this run ID was found

Cause: the same `--run-id` already exists.

Fix: use a new run id:

```cmd
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test2 --timeout-wait 600
```

Or overwrite:

```cmd
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test1 --force --timeout-wait 600
```

### Unity does not connect

Check:

- Python trainer is running before Unity Play.
- `BehaviorParameters.Behavior Name` is `CombatAgent`.
- `StudentCombatAgent`, `BehaviorParameters`, and `DecisionRequester` are enabled on AgentA.
- Unity is playing `TermProject_Arena`.

### Python package install fails

Check:

- Conda environment uses Python `3.10.12`.
- Install uses `requirements-mlagents.txt.

