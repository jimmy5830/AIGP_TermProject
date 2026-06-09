# BT/RL Combat Template Setup Guide

This project is a scaffold for comparing Behavior Tree and RL decision making on top of the same Core Combat API.

## Core Combat Components

Each combat agent should have:

- `Rigidbody`
- `CapsuleCollider`
- `CombatCharacter`
- `CooldownSystem`
- `CombatActionController`
- `CombatHitDetector`

`CombatHitDetector.target` should point to the opponent's `CombatCharacter`.

`GameManager` should have:

- `EpisodeManager`
- `Agent A`
- `Agent B`
- `Spawn Point A`
- `Spawn Point B`

## Controller Rule

Enable only one decision controller per agent at a time.

Examples:

- Manual test: `ManualCombatInput` enabled on `Agent_A`
- BT attacker: `BaselineAttackerBT` enabled
- BT defender: `BaselineDefenderBT` enabled
- RL agent: `StudentCombatAgent` enabled

BT and RL scripts should call only:

- `CombatActionController.Move(direction)`
- `CombatActionController.Attack()`
- `CombatActionController.Block()`
- `CombatActionController.Dodge(direction)`

They should not directly modify health, damage, or cooldown values.

## ML-Agents Behavior Parameters

Use these settings for `StudentCombatAgent`:

- Behavior Name: `CombatAgent`
- Behavior Type: `Default`
- Vector Observation Space Size: `15`
- Continuous Actions: `0`
- Discrete Branches: `2`
- Branch 0 Size: `5`
- Branch 1 Size: `4`

Action mapping:

- Branch 0: `0 stay`, `1 forward`, `2 backward`, `3 left`, `4 right`
- Branch 1: `0 none`, `1 attack`, `2 block`, `3 dodge`

Recommended `Decision Requester`:

- Decision Period: `5`
- Take Actions Between Decisions: `true`

## Training Config

PPO config path:

```text
Assets/Config/combat_ppo.yaml
```

Run with a Python ML-Agents trainer version compatible with the local Unity package. Confirm locally with:

```text
mlagents-learn --version
mlagents-learn --help
```

## Play Mode Checks

- `Agent_A` WASD movement works with `ManualCombatInput`.
- Moving updates the capsule's `transform.forward`.
- `J` attacks and can hit in range/angle.
- Out-of-range attacks log miss.
- Outside-angle attacks log miss.
- `K` block prevents incoming damage during `blockDuration`.
- `L` dodge prevents incoming damage during `dodgeInvincibleDuration`.
- Death triggers `EpisodeManager` reset.
- Reset clears health, cooldowns, action states, velocity, angular velocity, position, and rotation.
